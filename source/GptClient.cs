using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    internal sealed class ChatMessage
    {
        public string Role;
        public string Content;
        // Null represents legacy records whose original date is unknown, not the current date.
        public DateTime? CreatedUtc;
        public ChatMessage(string role, string content) { Role = role; Content = content; CreatedUtc = DateTime.UtcNow; }
    }

    internal sealed class GptReply
    {
        public string DisplayText = "";
        public string JapaneseSpeechText = "";
        public string SpeechNotice = "";
        public string ToolResultText = "";
    }

    internal sealed class GptSettings
    {
#if DEEPSEEK
        public string Model = "deepseek-flash";
#else
        public string Model = "gpt-6-luna";
#endif
        public string ApiKey = "";
        public bool RememberKey = false;
#if DEEPSEEK
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AnnaDesktopPet.DeepSeek.v1");
        private static string SettingsPath { get { return Path.Combine(Program.DataDir, "deepseek-settings.json"); } }
        private static string KeyPath { get { return Path.Combine(Program.DataDir, "deepseek-key.bin"); } }
#else
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AnnaDesktopPet.OpenAI.v1");
        private static string SettingsPath { get { return Path.Combine(Program.DataDir, "gpt-settings.json"); } }
        private static string KeyPath { get { return Path.Combine(Program.DataDir, "gpt-key.bin"); } }
#endif

        public static GptSettings Load()
        {
            GptSettings settings = new GptSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return settings;
                Dictionary<string, object> data = Json().DeserializeObject(File.ReadAllText(SettingsPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (data == null) return settings;
                object value;
                if (data.TryGetValue("model", out value) && value is string)
                {
                    try { settings.Model = GptClient.NormalizeModel((string)value); } catch (ArgumentException) { }
                }
                if (data.TryGetValue("remember_key", out value) && value is bool) settings.RememberKey = (bool)value;
                if (settings.RememberKey && File.Exists(KeyPath))
                {
                    byte[] encrypted = File.ReadAllBytes(KeyPath);
                    byte[] plain = null;
                    try
                    {
                        plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                        settings.ApiKey = Encoding.UTF8.GetString(plain).Trim();
                    }
                    finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
                }
            }
            catch
            {
                // A copied or damaged credential file must never expose exception details or break startup.
                settings.ApiKey = "";
            }
            return settings;
        }

        public void Save()
        {
            if (Program.Smoke) return;
            string model = GptClient.NormalizeModel(Model);
            string key = (ApiKey ?? "").Trim();
            ValidateKey(key);
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                if (RememberKey && key.Length > 0)
                {
                    byte[] plain = Encoding.UTF8.GetBytes(key);
                    try { WriteAtomic(KeyPath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
                    finally { Array.Clear(plain, 0, plain.Length); }
                }
                else if (File.Exists(KeyPath)) File.Delete(KeyPath);
                Dictionary<string, object> data = new Dictionary<string, object>();
                data["model"] = model;
                data["remember_key"] = RememberKey && key.Length > 0;
                WriteAtomic(SettingsPath, Encoding.UTF8.GetBytes(Json().Serialize(data)));
                Model = model;
            }
            catch
            {
                throw new InvalidOperationException("无法保存 GPT 设置。请检查本机用户配置文件夹是否可写。");
            }
        }

        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 16384 }; }
        internal static void ValidateKey(string key)
        {
            if (key.Length > 1000) throw new ArgumentException("API Key 格式不正确，请重新粘贴完整密钥。");
            foreach (char ch in key)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || "-._~+/=".IndexOf(ch) >= 0))
                    throw new ArgumentException("API Key 格式不正确，请重新粘贴完整密钥。");
        }
        private static void WriteAtomic(string path, byte[] bytes)
        {
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed partial class GptClient
    {
#if DEEPSEEK
        private const string Endpoint = "https://api.deepseek.com/chat/completions";
#else
        private const string Endpoint = "https://api.openai.com/v1/responses";
#endif
        private const string Instructions = "你是以《败犬女主太多了！》八奈见杏菜为灵感的同人桌面伙伴，昵称杏菜，由 GPT 提供对话。默认用中文自然交流，活泼、有一点俏皮，喜欢聊美食，也会认真帮助用户。普通闲聊通常用一到三句简短回复；用户请教问题时按需要认真解释。不要机械重复口头禅，不给用户制造陪伴压力，不自称真人或官方角色。你能参考用户输入、最近对话和桌宠提供的音乐工具结果；不能看到屏幕或随意读取文件。宿主桌宠可以搜索和控制 QQ 音乐，由程序执行。只有工具明确确认正在播放时才能说已经播放；已发送请求不等于播放成功。没有工具结果时不许假称执行成功，也不要笼统说桌宠无法播放音乐。";
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> transport;
        public GptClient() { }
        internal GptClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> testTransport) { transport = testTransport; }

        internal static string ConversationRole(string role)
        {
            if (role == "user" || role == "local_user") return "user";
            if (role == "assistant" || role == "local_assistant") return "assistant";
            return null;
        }

        public async Task<string> SendAsync(GptSettings settings, IList<ChatMessage> history, string userText, CancellationToken cancellation)
        {
            return (await SendCoreAsync(settings, history, userText, false, cancellation).ConfigureAwait(false)).DisplayText;
        }

        public async Task<GptReply> SendReplyAsync(GptSettings settings, IList<ChatMessage> history, string userText, bool japaneseSpeech, CancellationToken cancellation, Action<GptStreamUpdate> onProgress = null)
        {
            if (onProgress != null)
            {
#if DEEPSEEK
                string payload = DeepSeekPayload(settings.Model, history, userText, japaneseSpeech);
#else
                string payload = BuildPayload(settings.Model, history, userText, japaneseSpeech);
#endif
                return await SendStreamingPayloadAsync(settings, payload, japaneseSpeech, cancellation, onProgress).ConfigureAwait(false);
            }
            if (!japaneseSpeech)
                return new GptReply { DisplayText = await SendAsync(settings, history, userText, cancellation).ConfigureAwait(false) };
            return await SendCoreAsync(settings, history, userText, true, cancellation).ConfigureAwait(false);
        }

        private async Task<GptReply> SendCoreAsync(GptSettings settings, IList<ChatMessage> history, string userText, bool japaneseSpeech, CancellationToken cancellation)
        {
            if (settings == null) throw new ArgumentNullException("settings");
#if DEEPSEEK
            string payload = DeepSeekPayload(settings.Model, history, userText, japaneseSpeech);
#else
            string payload = BuildPayload(settings.Model, history, userText, japaneseSpeech);
#endif
            return CleanConversationReply(await SendPayloadAsync(settings, payload, japaneseSpeech, cancellation).ConfigureAwait(false));
        }

        private static GptReply CleanConversationReply(GptReply reply)
        {
            if (reply == null) return null;
            reply.DisplayText = RemoveMessageTimeLabels(reply.DisplayText);
            reply.JapaneseSpeechText = RemoveMessageTimeLabels(reply.JapaneseSpeechText);
            return reply;
        }
        private static string RemoveMessageTimeLabels(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            // Only our standalone metadata line is hidden; actual dates and inline quotes remain intact.
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"(?m)^[ \t]*【消息时间：(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})|未知，不推定为今天)】[ \t]*(?:\r?\n|$)", "").Trim();
        }

        private async Task<GptReply> SendPayloadAsync(GptSettings settings, string payload, bool japaneseSpeech, CancellationToken cancellation)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            cancellation.ThrowIfCancellationRequested();
            string key = (settings.ApiKey ?? "").Trim();
            if (key.Length == 0) throw new InvalidOperationException("请先在模型设置里填写 API Key。");
            GptSettings.ValidateKey(key);
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 on .NET Framework.
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        bool retryEmpty = false;
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                            try
                            {
                                if (transport != null)
                                {
                                    using (HttpResponseMessage response = await transport(request, timeout.Token).ConfigureAwait(false))
                                    {
                                        GptReply reply = await ReadReplyAsync(response, japaneseSpeech).ConfigureAwait(false);
                                        cancellation.ThrowIfCancellationRequested();
                                        return reply;
                                    }
                                }
                                using (HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false })
                                using (HttpClient client = new HttpClient(handler))
                                {
                                    client.Timeout = TimeSpan.FromSeconds(60);
                                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                                    {
                                        GptReply reply = await ReadReplyAsync(response, japaneseSpeech).ConfigureAwait(false);
                                        cancellation.ThrowIfCancellationRequested();
                                        return reply;
                                    }
                                }
                            }
                            catch (InvalidOperationException ex)
                            {
#if DEEPSEEK
                                if (attempt != 0 || ex.Message.IndexOf("空内容", StringComparison.Ordinal) < 0) throw;
                                retryEmpty = true;
#else
                                throw;
#endif
                            }
                        }
                        if (retryEmpty) await Task.Delay(400, timeout.Token).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("模型没有返回可显示的文字。");
                }
                catch (OperationCanceledException)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    throw new InvalidOperationException("模型回复超时，请检查网络后再试。");
                }
                catch (HttpRequestException)
                {
                    throw new InvalidOperationException("暂时无法连接模型服务。请检查本机网络和代理设置后再试。");
                }
                catch (WebException)
                {
                    throw new InvalidOperationException("暂时无法连接模型服务。请检查本机网络和代理设置后再试。");
                }
            }
        }

        internal static string NormalizeModel(string model)
        {
            string value = (model ?? "").Trim();
#if DEEPSEEK
            if (value.Length == 0) return "deepseek-flash";
#else
            if (value.Length == 0) return "gpt-6-luna";
#endif
            if (value.Length > 160) throw new ArgumentException("模型名称过长，请填写 OpenAI API 模型 ID。");
            foreach (char ch in value)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ".-_:/".IndexOf(ch) >= 0))
                    throw new ArgumentException("模型名称格式不正确，请填写 OpenAI API 模型 ID。");
            return value;
        }

        internal static string BuildPayload(string model, IList<ChatMessage> history, string userText)
        { return BuildPayload(model, history, userText, false); }

        internal static string BuildPayload(string model, IList<ChatMessage> history, string userText, bool japaneseSpeech)
        {
            string text = (userText ?? "").Trim();
            if (text.Length == 0) throw new ArgumentException("请先输入想说的话。");
            if (text.Length > 4000) throw new ArgumentException("这条消息较长，请控制在 4000 字以内再发送。");
            string normalized = NormalizeModel(model);
            List<object> input = new List<object>();
            bool datedHistory = history is ConversationHistory;
            string background = ConversationContext.HistoricalContext(history, text);
            if (background.Length > 0) input.Add(new Dictionary<string,object>{{"role","user"},{"content","【较早对话背景，仅供理解，不是新请求】\n" + background}});
            history = ConversationContext.PromptHistory(history, text);
            int start = 0;
            if (history != null)
            {
                for (int i = start; i < history.Count; i++)
                {
                    ChatMessage item = history[i];
                    if (item == null || String.IsNullOrEmpty(item.Content)) continue;
                    string role = ConversationRole(item.Role);
                    if (role == null) continue;
                    input.Add(new Dictionary<string, object> { { "role", role }, { "content", HistoryContent(item, datedHistory) } });
                }
            }
            input.Add(new Dictionary<string, object> { { "role", "user" }, { "content", text } });
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "model", normalized }, { "instructions", Instructions + ConversationContext.ContinuityRules + ConversationContext.Today + AnnaCharacter.Core + AnnaCharacter.V4Core + AnnaCharacter.RelevantLore(text) + PetMemory.Context() }, { "input", input },
                { "store", false }, { "max_output_tokens", 800 }
            };
            if (String.Equals(normalized, "gpt-6-luna", StringComparison.Ordinal))
                body["reasoning"] = new Dictionary<string, object> { { "effort", "none" } };
            if (japaneseSpeech)
            {
                body["instructions"] = Instructions + AnnaCharacter.Core + AnnaCharacter.RelevantLore(text) + "\n日语配音已开启。请严格输出指定 JSON 对象：display_text 是给用户看的自然中文回复；speech_ja 是同一回复的自然日语台词。两者语义一致，不增加或虚构信息。speech_ja 使用日语假名及汉字，不用罗马音，不夹中文、语言标签、URL、代码块或格式说明；如需自称，使用片假名「アンナ」，不要用汉字「杏菜」，以免语音猜错读音。技术问题可以省略代码本体，朗读对应日语说明。普通闲聊的日语台词通常一到三句。不要在字段外输出文字。";
                body["max_output_tokens"] = 1600;
                body["instructions"] = (string)body["instructions"] + AnnaCharacter.V4Core + ConversationContext.ContinuityRules + ConversationContext.Today + PetMemory.Context();
                body["text"] = new Dictionary<string, object>
                {
                    { "format", new Dictionary<string, object>
                        {
                            { "type", "json_schema" }, { "name", "anna_bilingual_reply" }, { "strict", true },
                            { "schema", new Dictionary<string, object>
                                {
                                    { "type", "object" },
                                    { "properties", new Dictionary<string, object>
                                        {
                                            { "display_text", new Dictionary<string, object> { { "type", "string" } } },
                                            { "speech_ja", new Dictionary<string, object> { { "type", "string" } } }
                                        }
                                    },
                                    { "required", new string[] { "display_text", "speech_ja" } }, { "additionalProperties", false }
                                }
                            }
                        }
                    }
                };
            }
            return Json().Serialize(body);
        }

        private static string HistoryContent(ChatMessage item, bool dated)
        {
            string prefix = dated && ConversationRole(item.Role) == "user" ? "【消息时间：" + (ConversationContext.Timestamp(item) ?? "未知，不推定为今天") + "】\n" : "";
            return prefix + (item.Role == "local_assistant" ? "【桌宠工具实际结果】\n" : "") + item.Content;
        }

        private static async Task<GptReply> ReadReplyAsync(HttpResponseMessage response, bool japaneseSpeech)
        {
#if DEEPSEEK
            if (response == null) throw new InvalidOperationException("DeepSeek 没有返回有效响应。");
            string deepSeekBody = response.Content == null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                int status=(int)response.StatusCode;
                if(status==401 || status==403)throw new InvalidOperationException("DeepSeek API Key 无效或无权访问（HTTP "+status+"）。请检查设置中的密钥。");
                if(status==402)throw new InvalidOperationException("DeepSeek API 余额不足（HTTP 402）。请在 DeepSeek 开放平台检查额度。");
                if(status==429)throw new InvalidOperationException("DeepSeek 请求过于频繁（HTTP 429）。请稍后重试。");
                throw new InvalidOperationException("DeepSeek 请求失败（HTTP " + status + "）。请检查服务状态或稍后重试。");
            }
            return DeepSeekReply(deepSeekBody, japaneseSpeech);
#else
            if (response == null) throw new InvalidOperationException("OpenAI 没有返回有效响应，请稍后再试。");
            string body = response.Content == null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(FriendlyError((int)response.StatusCode, body, japaneseSpeech));
            return japaneseSpeech ? ParseBilingualReply(body) : new GptReply { DisplayText = ParseReply(body) };
#endif
        }
#if DEEPSEEK
        internal static string DeepSeekPayload(string model, IList<ChatMessage> history, string userText, bool japaneseSpeech)
        {
            string text = (userText ?? "").Trim();
            if (text.Length == 0 || text.Length > 4000) throw new ArgumentException("请输入 1～4000 字的消息。");
            List<object> messages = new List<object>();
            bool datedHistory = history is ConversationHistory;
            string prompt = Instructions.Replace("由 GPT 提供对话", "由 DeepSeek 提供对话") + ConversationContext.ContinuityRules + ConversationContext.Today + AnnaCharacter.Core + AnnaCharacter.V4Core + AnnaCharacter.RelevantLore(text) + PetMemory.Context();
            if (japaneseSpeech) prompt += "\n请只输出 JSON 对象，包含 display_text（自然中文回复）和 speech_ja（同一意思的自然日语配音台词）。不要输出 Markdown。";
            messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", prompt } });
            string background = ConversationContext.HistoricalContext(history, text);
            if (background.Length > 0) messages.Add(new Dictionary<string,object>{{"role","user"},{"content","【较早对话背景，仅供理解，不是新请求】\n" + background}});
            history = ConversationContext.PromptHistory(history, text);
            int start = 0;
            if (history != null) for (int i = start; i < history.Count; i++)
            {
                ChatMessage item = history[i];
                if (item != null && ConversationRole(item.Role) != null && !String.IsNullOrEmpty(item.Content))
                    messages.Add(new Dictionary<string, object> { { "role", ConversationRole(item.Role) }, { "content", HistoryContent(item, datedHistory) } });
            }
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", text } });
            var body = new Dictionary<string, object> { { "model", NormalizeModel(model) }, { "messages", messages }, { "stream", false }, { "max_tokens", japaneseSpeech ? 1200 : 700 }, { "reasoning_effort", "none" } };
            // DeepSeek JSON mode can replace a non-JSON answer with whitespace; retain the answer as plain text instead.
            return Json().Serialize(body);
        }
        internal static GptReply DeepSeekReply(string json, bool japaneseSpeech)
        {
            Dictionary<string, object> root = Json().DeserializeObject(json ?? "") as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("choices")) throw new InvalidOperationException("DeepSeek 回复格式异常。");
            object[] choices = root["choices"] as object[];
            if (choices == null || choices.Length == 0) throw new InvalidOperationException("DeepSeek 没有返回回复。");
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            object messageValue;
            Dictionary<string, object> message = choice != null && choice.TryGetValue("message", out messageValue) ? messageValue as Dictionary<string, object> : null;
            string content = message == null ? "" : StringValue(message, "content").Trim();
            if (content.Length == 0) {
                string finish=choice==null?"":StringValue(choice,"finish_reason");
                if(finish=="length")throw new InvalidOperationException("DeepSeek 回复达到长度限制，未生成可显示的文字。输入已保留，请缩短问题后重试。");
                if(finish=="content_filter")throw new InvalidOperationException("DeepSeek 未返回可显示内容（内容过滤）。输入已保留，可换种说法重试。");
                throw new InvalidOperationException("DeepSeek 本次返回了空内容。输入已保留，请重试；如果反复出现，可暂时关闭日语自动配音。");
            }
            if (!japaneseSpeech) return new GptReply { DisplayText = content };
            Dictionary<string, object> bilingual = null;
            try { bilingual = Json().DeserializeObject(content) as Dictionary<string, object>; } catch { }
            if (bilingual == null) return WithoutSpeech(content, "本次未生成日语台词，已保留中文回复。");
            string display = StringValue(bilingual, "display_text").Trim();
            string speech = StringValue(bilingual, "speech_ja").Trim();
            if (display.Length == 0) throw new InvalidOperationException("DeepSeek 没有返回中文回复。");
            if (!IsUsableJapanese(speech)) return WithoutSpeech(display, "本次未生成可用的日语台词。");
            return new GptReply { DisplayText = display, JapaneseSpeechText = speech };
        }
#endif

        internal static GptReply ParseBilingualReply(string json)
        {
            Dictionary<string, object> response;
            try { response = Json().DeserializeObject(json ?? "") as Dictionary<string, object>; }
            catch { throw new InvalidOperationException("GPT 返回的数据格式异常，请稍后重试。"); }
            if (response == null) throw new InvalidOperationException("GPT 返回的数据格式异常，请稍后重试。");
            string status = StringValue(response, "status");
            if (status == "failed" || status == "cancelled") throw new InvalidOperationException("GPT 没有完成这次回复，请稍后重试。");
            if (status == "in_progress" || status == "queued") throw new InvalidOperationException("GPT 尚未生成完整回复，请稍后重试。");
            StringBuilder text = new StringBuilder();
            StringBuilder refusal = new StringBuilder();
            object output;
            if (response.TryGetValue("output", out output) && output is IEnumerable)
            {
                foreach (object item in (IEnumerable)output)
                {
                    Dictionary<string, object> message = item as Dictionary<string, object>;
                    if (message == null || StringValue(message, "type") != "message") continue;
                    object content;
                    if (!message.TryGetValue("content", out content) || !(content is IEnumerable)) continue;
                    foreach (object part in (IEnumerable)content)
                    {
                        Dictionary<string, object> block = part as Dictionary<string, object>;
                        if (block == null) continue;
                        if (StringValue(block, "type") == "output_text") text.Append(StringValue(block, "text"));
                        else if (StringValue(block, "type") == "refusal") Append(refusal, StringValue(block, "refusal"));
                    }
                }
            }
            if (refusal.Length > 0)
                return WithoutSpeech(refusal.ToString(), "这次回复没有生成日语台词，未播放配音。");
            string raw = text.ToString().Trim();
            if (raw.Length == 0)
                throw new InvalidOperationException(status == "incomplete" ? "GPT 回复未完整生成，尚无可显示的中文，请重试。" : "GPT 没有返回文字内容，请换种问法再试。");
            Dictionary<string, object> bilingual = null;
            object decoded = null;
            bool parsedJson = false;
            try { decoded = Json().DeserializeObject(raw); parsedJson = true; bilingual = decoded as Dictionary<string, object>; } catch { }
            string display = bilingual == null ? ExtractCompleteStringField(raw, "display_text") : StringValue(bilingual, "display_text").Trim();
            if (String.IsNullOrWhiteSpace(display))
            {
                // A genuine plain-text reply may be shown, but never sent to a Japanese voice.
                if (bilingual == null && !LooksStructured(raw))
                {
                    string plain = parsedJson ? decoded as string : raw;
                    if (!String.IsNullOrWhiteSpace(plain) && !LooksStructured(plain))
                        return status == "incomplete" ? WithoutSpeech(plain + "\n\n（这次回复达到长度限制，可能尚未说完。）", "回复未完整生成，本次未播放日语配音。") : WithoutSpeech(plain, "模型未按双语格式回复，本次未播放日语配音。");
                }
                throw new InvalidOperationException("GPT 的双语回复格式不完整，未能取得中文内容。请重试，或关闭自动配音。");
            }
            if (status == "incomplete")
                return WithoutSpeech(display + "\n\n（这次回复达到长度限制，可能尚未说完。）", "回复未完整生成，本次未播放日语配音。");
            if (bilingual == null)
                return WithoutSpeech(display, "双语回复格式不完整，已保留中文，本次未播放日语配音。");
            string japanese = StringValue(bilingual, "speech_ja").Trim();
            if (!IsUsableJapanese(japanese))
                return WithoutSpeech(display, "这次未生成可用的日语台词，已保留中文回复。");
            return new GptReply { DisplayText = display, JapaneseSpeechText = japanese };
        }

        private static GptReply WithoutSpeech(string display, string notice)
        { return new GptReply { DisplayText = display.Trim(), SpeechNotice = notice }; }

        private static bool IsUsableJapanese(string value)
        {
            if (value.Length == 0 || value.IndexOf("http://", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("https://", StringComparison.OrdinalIgnoreCase) >= 0 || value.Contains("```") || value.Contains("display_text") || value.Contains("speech_ja")) return false;
            bool kana = false;
            foreach (char ch in value)
            {
                if ((ch >= '\u3041' && ch <= '\u3096') || (ch >= '\u30a1' && ch <= '\u30fa') || (ch >= '\uff66' && ch <= '\uff9d')) kana = true;
                // Common simplified Chinese characters do not belong in a Japanese voice script.
                if ("这们吗说话觉欢谢请为时现让边发饭么样实个还对".IndexOf(ch) >= 0) return false;
            }
            return kana;
        }

        private static bool LooksStructured(string value)
        {
            string trimmed = value.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("```", StringComparison.Ordinal) || value.Contains("\"display_text\"") || value.Contains("\"speech_ja\"");
        }

        private static string ExtractCompleteStringField(string raw, string field)
        {
            string marker = "\"" + field + "\"";
            int search = 0;
            while (search < raw.Length)
            {
                int at = raw.IndexOf(marker, search, StringComparison.Ordinal);
                if (at < 0) return "";
                search = at + marker.Length;
                int cursor = search;
                while (cursor < raw.Length && Char.IsWhiteSpace(raw[cursor])) cursor++;
                if (cursor >= raw.Length || raw[cursor++] != ':') continue;
                while (cursor < raw.Length && Char.IsWhiteSpace(raw[cursor])) cursor++;
                if (cursor >= raw.Length || raw[cursor] != '"') continue;
                int start = cursor++;
                bool escaped = false;
                for (; cursor < raw.Length; cursor++)
                {
                    char ch = raw[cursor];
                    if (!escaped && ch == '"')
                    {
                        try { return (Json().DeserializeObject(raw.Substring(start, cursor - start + 1)) as string ?? "").Trim(); }
                        catch { break; }
                    }
                    if (!escaped && ch == '\\') escaped = true;
                    else escaped = false;
                }
            }
            return "";
        }

        internal static string ParseReply(string json)
        {
            Dictionary<string, object> response;
            try { response = Json().DeserializeObject(json ?? "") as Dictionary<string, object>; }
            catch { throw new InvalidOperationException("GPT 返回的数据格式异常，请稍后重试。"); }
            if (response == null) throw new InvalidOperationException("GPT 返回的数据格式异常，请稍后重试。");
            string status = StringValue(response, "status");
            if (status == "failed" || status == "cancelled") throw new InvalidOperationException("GPT 没有完成这次回复，请稍后重试。");
            StringBuilder text = new StringBuilder();
            StringBuilder refusal = new StringBuilder();
            object output;
            if (response.TryGetValue("output", out output) && output is IEnumerable)
            {
                foreach (object item in (IEnumerable)output)
                {
                    Dictionary<string, object> message = item as Dictionary<string, object>;
                    if (message == null || StringValue(message, "type") != "message") continue;
                    object content;
                    if (!message.TryGetValue("content", out content) || !(content is IEnumerable)) continue;
                    foreach (object part in (IEnumerable)content)
                    {
                        Dictionary<string, object> block = part as Dictionary<string, object>;
                        if (block == null) continue;
                        string type = StringValue(block, "type");
                        if (type == "output_text") Append(text, StringValue(block, "text"));
                        else if (type == "refusal") Append(refusal, StringValue(block, "refusal"));
                    }
                }
            }
            if (text.Length > 0)
            {
                if (status == "incomplete") text.Append("\n\n（这次回复达到长度限制，可能尚未说完。）");
                return text.ToString().Trim();
            }
            if (refusal.Length > 0) return refusal.ToString().Trim();
            if (status == "incomplete") throw new InvalidOperationException("GPT 回复达到长度限制，尚未生成可显示的文字，请重试或缩短问题。");
            if (status == "in_progress" || status == "queued") throw new InvalidOperationException("GPT 尚未生成完整回复，请稍后重试。");
            throw new InvalidOperationException("GPT 没有返回文字内容，请换种问法再试。");
        }

        internal static string FriendlyError(int status, string json)
        { return FriendlyError(status, json, false); }

        internal static string FriendlyError(int status, string json, bool japaneseSpeech)
        {
            string code = "";
            string parameter = "";
            string message = "";
            try
            {
                Dictionary<string, object> data = Json().DeserializeObject(json ?? "") as Dictionary<string, object>;
                object error;
                if (data != null && data.TryGetValue("error", out error))
                {
                    Dictionary<string, object> detail = error as Dictionary<string, object>;
                    if (detail != null)
                    {
                        code = StringValue(detail, "code");
                        parameter = StringValue(detail, "param");
                        message = StringValue(detail, "message");
                    }
                }
            }
            catch { }
            if (code == "insufficient_quota" || code == "billing_hard_limit_reached" || code == "billing_not_active")
                return "OpenAI API 余额或额度不足，请到 OpenAI 平台检查 API 账单。";
            if (status == 401) return "API Key 无效或已失效，请在 GPT 设置里检查并重新填写。";
            if (status == 403) return "当前 API 账户没有访问权限，请检查 OpenAI 项目和模型权限。";
            if (status == 404 || code == "model_not_found") return "找不到此模型，或当前 API 账户无权使用。请在 GPT 设置里更换模型 ID。";
            if (japaneseSpeech && status == 400 && (parameter.StartsWith("text", StringComparison.OrdinalIgnoreCase) || parameter.IndexOf("response_format", StringComparison.OrdinalIgnoreCase) >= 0 || code == "unsupported_model" || message.IndexOf("json_schema", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("structured output", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("response_format", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("text.format", StringComparison.OrdinalIgnoreCase) >= 0))
                return "当前模型不支持日语双语输出。请换用默认模型 gpt-6-luna，或关闭自动配音后再试。";
            if (status == 429) return "OpenAI 请求过于频繁或额度暂时不足，请稍后再试并检查 API 用量。";
            if (status == 408 || status == 504) return "GPT 回复超时，请稍后再试。";
            if (status >= 500) return "OpenAI 服务暂时繁忙，请稍后重试。";
            if (status >= 300 && status < 400) return "OpenAI 连接收到异常重定向，已停止发送。请检查网络代理设置。";
            return "OpenAI 未接受这次请求。请检查模型 ID、API 账户及消息内容后再试。";
        }

        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 64 }; }
        private static string StringValue(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value as string ?? "" : ""; }
        private static void Append(StringBuilder target, string value)
        { if (value.Length == 0) return; if (target.Length > 0) target.Append('\n'); target.Append(value); }
    }
}
