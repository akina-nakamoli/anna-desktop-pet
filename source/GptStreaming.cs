using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    internal sealed class GptStreamUpdate
    {
        public string DisplayText = "";
        public string JapaneseSentence = "";
    }

    internal sealed partial class GptClient
    {
        private async Task<GptReply> SendStreamingPayloadAsync(GptSettings settings, string payload, bool japanese,
            CancellationToken cancellation, Action<GptStreamUpdate> progress)
        {
            cancellation.ThrowIfCancellationRequested();
            if (settings == null) throw new ArgumentNullException("settings");
            string key = (settings.ApiKey ?? "").Trim();
            if (key.Length == 0) throw new InvalidOperationException("请先在模型设置里填写 API Key。");
            GptSettings.ValidateKey(key);
            var body = Json().DeserializeObject(payload) as Dictionary<string, object>;
            body["stream"] = true;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(Json().Serialize(body), Encoding.UTF8, "application/json");
                try
                {
                    if (transport != null)
                    {
                        using (var response = await transport(request, timeout.Token).ConfigureAwait(false))
                            return await ReadStreamingReplyAsync(response, japanese, timeout.Token, progress).ConfigureAwait(false);
                    }
                    using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(60);
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            return await ReadStreamingReplyAsync(response, japanese, timeout.Token, progress).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    throw new InvalidOperationException("模型回复超时，请检查网络后再试。");
                }
                catch (Exception ex)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    if (timeout.IsCancellationRequested) throw new InvalidOperationException("模型回复超时，请检查网络后再试。");
                    if (ex is HttpRequestException || ex is WebException || ex is IOException)
                        throw new InvalidOperationException("流式回复连接中断，已显示的内容可能尚未说完。请检查网络后重试。");
                    throw;
                }
            }
        }

        private static async Task<GptReply> ReadStreamingReplyAsync(HttpResponseMessage response, bool japanese,
            CancellationToken cancellation, Action<GptStreamUpdate> progress)
        {
            if (response == null) throw new InvalidOperationException("模型没有返回有效响应。");
            // Also closes a stalled body read on .NET Framework, whose ReadAsync has no token overload.
            using (cancellation.Register(delegate { response.Dispose(); }))
            {
                cancellation.ThrowIfCancellationRequested();
                string contentType = response.Content == null || response.Content.Headers.ContentType == null ? "" : response.Content.Headers.ContentType.MediaType;
                if (!response.IsSuccessStatusCode || !String.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = CleanConversationReply(await ReadReplyAsync(response, japanese).ConfigureAwait(false));
                    cancellation.ThrowIfCancellationRequested();
                    if (progress != null) progress(new GptStreamUpdate { DisplayText = fallback.DisplayText, JapaneseSentence = fallback.JapaneseSpeechText });
                    return fallback;
                }
                var state = new ReplyStreamState(japanese, progress, cancellation);
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024))
                {
                    var line = new StringBuilder();
                    var data = new StringBuilder();
                    char[] chars = new char[1024];
                    int count;
                    while (!state.Finished && (count = await reader.ReadAsync(chars, 0, chars.Length).ConfigureAwait(false)) != 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        for (int i = 0; i < count && !state.Finished; i++)
                        {
                            char ch = chars[i];
                            if (ch == '\n')
                            {
                                string current = line.ToString().TrimEnd('\r'); line.Length = 0;
                                if (current.Length == 0)
                                {
                                    if (data.Length > 0) { state.Event(data.ToString()); data.Length = 0; }
                                }
                                else if (current.StartsWith("data:", StringComparison.Ordinal))
                                {
                                    string value = current.Substring(5); if (value.StartsWith(" ")) value = value.Substring(1);
                                    if (data.Length > 0) data.Append('\n'); data.Append(value);
                                    if (data.Length > 262144) throw new InvalidOperationException("流式回复片段过大。");
                                }
                            }
                            else { line.Append(ch); if (line.Length > 262144) throw new InvalidOperationException("流式回复片段过大。"); }
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (!state.Finished) throw new InvalidOperationException("流式回复连接提前结束，回复尚未完成，请重试。");
                    return state.Reply;
                }
            }
        }

        private sealed class ReplyStreamState
        {
            private readonly bool japanese;
            private readonly Action<GptStreamUpdate> progress;
            private readonly CancellationToken token;
            private readonly StringBuilder raw = new StringBuilder();
            private string display = "";
            private int spoken;
#if DEEPSEEK
            private bool finishSeen;
#endif
            private bool emittedSpeech;
            internal bool Finished;
            internal GptReply Reply;
            internal ReplyStreamState(bool bilingual, Action<GptStreamUpdate> callback, CancellationToken cancellation)
            { japanese = bilingual; progress = callback; token = cancellation; }

            internal void Event(string payload)
            {
                token.ThrowIfCancellationRequested();
                if (payload == "[DONE]")
                {
#if DEEPSEEK
                    if (!finishSeen) throw new InvalidOperationException("流式回复缺少完成标记。");
                    Complete(null); return;
#else
                    if (!Finished) throw new InvalidOperationException("流式回复缺少完成标记。");
                    return;
#endif
                }
                Dictionary<string, object> frame;
                try { frame = Json().DeserializeObject(payload) as Dictionary<string, object>; }
                catch { throw new InvalidOperationException("流式回复数据格式异常，请重试。"); }
                if (frame == null) throw new InvalidOperationException("流式回复数据格式异常，请重试。");
                if (frame.ContainsKey("error")) throw new InvalidOperationException("模型在生成途中返回错误，回复尚未完成。");
#if DEEPSEEK
                object choicesValue; object[] choices;
                if (!frame.TryGetValue("choices", out choicesValue) || (choices = choicesValue as object[]) == null || choices.Length == 0) return;
                var choice = choices[0] as Dictionary<string, object>;
                if (choice == null) throw new InvalidOperationException("流式回复选择格式异常。");
                object deltaValue;
                var delta = choice.TryGetValue("delta", out deltaValue) ? deltaValue as Dictionary<string, object> : null;
                if (delta != null) Add(StringValue(delta, "content")); // Never expose reasoning_content.
                string finish = StringValue(choice, "finish_reason");
                if (finish.Length > 0)
                {
                    if (finish != "stop") throw new InvalidOperationException("模型提前结束了回复（" + (finish == "length" ? "达到长度限制" : "未正常完成") + "）。");
                    finishSeen = true;
                }
#else
                string type = StringValue(frame, "type");
                if (type == "response.output_text.delta") Add(StringValue(frame, "delta"));
                else if (type == "response.completed")
                {
                    object responseValue;
                    var result = frame.TryGetValue("response", out responseValue) ? responseValue as Dictionary<string, object> : null;
                    if (result == null || StringValue(result, "status") != "completed") throw new InvalidOperationException("模型没有确认回复完成。");
                    Complete(result);
                }
                else if (type == "response.failed" || type == "response.incomplete" || type == "error")
                    throw new InvalidOperationException("模型未完整生成这次回复，请重试。");
#endif
            }
            private void Add(string chunk)
            {
                if (chunk.Length == 0) return;
                if (raw.Length + chunk.Length > 262144) throw new InvalidOperationException("回复内容过长，已停止接收。");
                raw.Append(chunk);
                string visible, speech = "";
                if (japanese)
                {
                    var fields = PartialBilingual(raw.ToString());
                    fields.TryGetValue("display_text", out visible); fields.TryGetValue("speech_ja", out speech);
                }
                else visible = raw.ToString();
                visible = VisibleStreamText(visible ?? "");
                if (visible != display) { display = visible; Publish(""); }
                if (japanese && speech != null) PublishSentences(RemoveMessageTimeLabels(speech), false);
            }
            private void Publish(string sentence)
            { token.ThrowIfCancellationRequested(); if (progress != null) progress(new GptStreamUpdate { DisplayText = display, JapaneseSentence = sentence }); }
            private void PublishSentences(string speech, bool final)
            {
                for (int i = spoken; i < speech.Length; i++)
                {
                    if ("。！？!?\n".IndexOf(speech[i]) < 0 && !(final && i == speech.Length - 1)) continue;
                    string sentence = RemoveMessageTimeLabels(speech.Substring(spoken, i - spoken + 1)).Trim();
                    spoken = i + 1;
                    if (IsUsableJapanese(sentence)) { emittedSpeech = true; Publish(sentence); }
                }
            }
            private void Complete(Dictionary<string, object> finalResponse)
            {
                token.ThrowIfCancellationRequested();
                if (japanese && LooksStructured(raw.ToString()))
                {
                    Dictionary<string, object> structured = null;
                    try { structured = Json().DeserializeObject(raw.ToString()) as Dictionary<string, object>; } catch { }
                    if (structured == null || !structured.ContainsKey("display_text") || !(structured["display_text"] is string))
                        throw new InvalidOperationException("流式双语回复格式不完整，已停止后续配音，请重试。");
                }
#if DEEPSEEK
                string response = Json().Serialize(new { choices = new[] { new { message = new { content = raw.ToString() }, finish_reason = "stop" } } });
                Reply = CleanConversationReply(DeepSeekReply(response, japanese));
#else
                if (finalResponse == null) throw new InvalidOperationException("流式回复缺少最终响应。");
                string response = Json().Serialize(finalResponse);
                Reply = CleanConversationReply(japanese ? ParseBilingualReply(response) : new GptReply { DisplayText = ParseReply(response) });
#endif
                // An authoritative final response must not silently rewrite already spoken text.
                if (japanese && emittedSpeech)
                {
                    string streamedSpeech; PartialBilingual(raw.ToString()).TryGetValue("speech_ja", out streamedSpeech);
                    streamedSpeech = RemoveMessageTimeLabels(streamedSpeech ?? "");
                    if (streamedSpeech == null || Reply.JapaneseSpeechText.Length < spoken ||
                        !Reply.JapaneseSpeechText.StartsWith(streamedSpeech.Substring(0, spoken), StringComparison.Ordinal))
                        throw new InvalidOperationException("模型的最终台词与流式台词不一致，已停止后续配音。");
                }
                display = Reply.DisplayText; Publish("");
                if (japanese && Reply.JapaneseSpeechText.Length > 0) PublishSentences(Reply.JapaneseSpeechText, true);
                Finished = true;
            }
        }

        private static string VisibleStreamText(string text)
        {
            // Hold an unfinished internal timestamp line so it never flashes in the chat bubble.
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.Length > 0 && ("【消息时间：".StartsWith(trimmed, StringComparison.Ordinal) || trimmed.StartsWith("【消息时间：", StringComparison.Ordinal)))
                    lines[i] = RemoveMessageTimeLabels(lines[i]);
                if (i == lines.Length - 1 && trimmed.Length > 0 && ("【消息时间：".StartsWith(trimmed, StringComparison.Ordinal) || trimmed.StartsWith("【消息时间：", StringComparison.Ordinal))) lines[i] = "";
            }
            return RemoveMessageTimeLabels(String.Join("\n", lines));
        }

        // Decode only top-level string values; incomplete escapes are held until the next delta.
        private static Dictionary<string, string> PartialBilingual(string text)
        {
            var values = new Dictionary<string, string>(); int at = 0;
            SkipSpace(text, ref at); if (at >= text.Length || text[at++] != '{') return values;
            while (at < text.Length)
            {
                SkipSpace(text, ref at); if (at < text.Length && text[at] == '}') break;
                string key, value; bool complete;
                if (!ReadPartialString(text, ref at, out key, out complete) || !complete) break;
                SkipSpace(text, ref at); if (at >= text.Length || text[at++] != ':') break;
                SkipSpace(text, ref at); if (!ReadPartialString(text, ref at, out value, out complete)) break;
                if ((key == "display_text" || key == "speech_ja") && !values.ContainsKey(key)) values[key] = value;
                if (!complete) break;
                SkipSpace(text, ref at); if (at >= text.Length || text[at++] != ',') break;
            }
            return values;
        }
        private static void SkipSpace(string text, ref int at) { while (at < text.Length && Char.IsWhiteSpace(text[at])) at++; }
        private static bool ReadPartialString(string text, ref int at, out string value, out bool complete)
        {
            value = ""; complete = false; if (at >= text.Length || text[at++] != '"') return false;
            var result = new StringBuilder();
            while (at < text.Length)
            {
                char ch = text[at++];
                if (ch == '"') { complete = true; break; }
                if (ch == '\\')
                {
                    if (at >= text.Length) break;
                    ch = text[at++];
                    if (ch == 'u')
                    {
                        if (at + 4 > text.Length) break;
                        ushort number;
                        if (!UInt16.TryParse(text.Substring(at, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out number)) break;
                        at += 4; ch = (char)number;
                    }
                    else if (ch == 'n') ch = '\n'; else if (ch == 'r') ch = '\r'; else if (ch == 't') ch = '\t';
                    else if (ch == 'b') ch = '\b'; else if (ch == 'f') ch = '\f';
                    else if (ch != '"' && ch != '\\' && ch != '/') break;
                }
                result.Append(ch);
            }
            if (result.Length > 0 && Char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
            value = result.ToString(); return true;
        }
    }
}
