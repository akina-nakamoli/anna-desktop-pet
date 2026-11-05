using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // Compile this file with GptClient.cs only. Tests never read real user configuration or make network calls.
    internal static class Program
    {
        internal static string DataDir;
        internal static bool Smoke;
    }

    internal static class GptBackendTests
    {
        private static int count;
        public static int Main(string[] args)
        {
            Program.DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mock-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                Run().GetAwaiter().GetResult();
                Console.WriteLine("PASS: " + count + " offline assertions. No real API key read; no network calls.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex.Message); return 1; }
        }

        private static async Task Run()
        {
            List<ChatMessage> history = new List<ChatMessage>();
            for (int i = 0; i < 12; i++) { history.Add(new ChatMessage("user", "问题" + i)); history.Add(new ChatMessage("assistant", "答复" + i)); }
            string original = "来点\"面包\"吧\n下一行\\尾巴🍞";
            string payload = GptClient.BuildPayload("gpt-6-luna", history, original);
            Dictionary<string, object> parsed = Json(payload);
            ArrayList input = new ArrayList((ICollection)parsed["input"]);
            Assert(input.Count == 21, "History limited to 20 previous entries plus current user");
            Assert((string)((Dictionary<string, object>)input[0])["content"] == "问题2", "History keeps most recent turns in order");
            Assert((string)((Dictionary<string, object>)input[20])["content"] == original, "JSON preserves Chinese, quotes, slash, newline, emoji");
            Assert((bool)parsed["store"] == false, "Responses store=false");
            Assert((int)parsed["max_output_tokens"] == 800, "Output budget");
            Assert((string)((Dictionary<string, object>)parsed["reasoning"])["effort"] == "none", "Luna disables unnecessary reasoning");
            Assert(!Json(GptClient.BuildPayload("my-custom-model", null, "hello")).ContainsKey("reasoning"), "Custom model is not forced into unsupported reasoning option");
            Assert(history.Count == 24, "Request does not mutate history");
            Throws(delegate { GptClient.BuildPayload("gpt-6-luna", null, new string('x', 4001)); }, "Long input rejected");
            Throws(delegate { GptClient.NormalizeModel("bad\nmodel"); }, "Invalid model rejected");
            Throws(delegate { GptSettings.ValidateKey("test\tkey"); }, "Whitespace in API key rejected without exposing its value");
            Throws(delegate { GptSettings.ValidateKey("test\"key"); }, "Invalid authentication header characters rejected");
            string response = "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"杏菜\"},{\"type\":\"output_text\",\"text\":\"在这里！\"}]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"一起吃面包吧。\"}]}]}";
            Assert(GptClient.ParseReply(response) == "杏菜\n在这里！\n一起吃面包吧。", "All output message blocks parsed after reasoning item");
            Assert(GptClient.ParseReply("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"这个请求我无法帮助。\"}]}]}") == "这个请求我无法帮助。", "Refusal shown");
            Assert(GptClient.ParseReply("{\"status\":\"incomplete\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"部分答复\"}]}]}").Contains("长度限制"), "Partial output marked incomplete");
            Throws(delegate { GptClient.ParseReply("{\"status\":\"incomplete\",\"output\":[]}"); }, "Incomplete empty output rejected");
            Throws(delegate { GptClient.ParseReply("{\"status\":\"failed\",\"error\":{\"message\":\"secret\"}}"); }, "Failed response rejected");
            Throws(delegate { GptClient.ParseReply("not json"); }, "Malformed response rejected");
            Assert(GptClient.FriendlyError(401, "{\"error\":{\"message\":\"secret-key\"}}").Contains("API Key"), "401 mapped");
            Assert(!GptClient.FriendlyError(401, "{\"error\":{\"message\":\"secret-key\"}}").Contains("secret-key"), "Error text does not echo raw API error");
            Assert(GptClient.FriendlyError(429, "{\"error\":{\"code\":\"insufficient_quota\"}}").Contains("余额"), "Quota mapped distinctly");
            Assert(GptClient.FriendlyError(404, "{}").Contains("模型"), "Model permission error mapped");
            Assert(GptClient.FriendlyError(302, "{}").Contains("重定向"), "Redirect rejected");

            GptSettings settings = new GptSettings { ApiKey = "test-key-not-real", Model = "gpt-6-luna" };
            bool transportCalled = false;
            GptClient client = new GptClient(async delegate(HttpRequestMessage request, CancellationToken cancel)
            {
                transportCalled = true;
                Assert(request.RequestUri.AbsoluteUri == "https://api.openai.com/v1/responses", "Transport endpoint fixed to OpenAI");
                Assert(request.Headers.Authorization.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "test-key-not-real", "Bearer header applied");
                Assert(Json(await request.Content.ReadAsStringAsync())["model"].ToString() == "gpt-6-luna", "Transport payload model");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            });
            Assert((await client.SendAsync(settings, history, "你好", CancellationToken.None)).Contains("杏菜"), "Mock request/response round trip");
            Assert(transportCalled, "Mock transport was used");
            Assert(history.Count == 24, "Send does not mutate history");
            GptClient failing = new GptClient(delegate(HttpRequestMessage request, CancellationToken cancel)
            { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"test-key-not-real\"}}") }); });
            await ThrowsAsync(delegate { return failing.SendAsync(settings, history, "你好", CancellationToken.None); }, "Mock 401 request fails with friendly error");
            GptClient waiting = new GptClient(delegate(HttpRequestMessage request, CancellationToken cancel)
            {
                TaskCompletionSource<HttpResponseMessage> pending = new TaskCompletionSource<HttpResponseMessage>();
                cancel.Register(delegate { pending.TrySetCanceled(); });
                return pending.Task;
            });
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task<string> pending = waiting.SendAsync(settings, null, "你好", cancellation.Token);
                cancellation.Cancel();
                try { await pending; throw new Exception("Cancellation not observed"); }
                catch (OperationCanceledException) { Assert(true, "In-flight cancellation observed"); }
            }
            await ThrowsAsync(delegate { return client.SendAsync(new GptSettings(), null, "你好", CancellationToken.None); }, "Missing API key blocked without network");

            Dictionary<string, object> bilingualPayload = Json(GptClient.BuildPayload("gpt-6-luna", history, "早上好", true));
            Assert(!Json(GptClient.BuildPayload("gpt-6-luna", history, "早上好", false)).ContainsKey("text"), "Japanese mode off does not add JSON schema");
            Dictionary<string, object> format = (Dictionary<string, object>)((Dictionary<string, object>)bilingualPayload["text"])["format"];
            Dictionary<string, object> schema = (Dictionary<string, object>)format["schema"];
            Assert((string)format["type"] == "json_schema" && (string)format["name"] == "anna_bilingual_reply" && (bool)format["strict"], "Japanese mode uses strict Responses JSON schema");
            Assert((bool)schema["additionalProperties"] == false && ((ICollection)schema["required"]).Count == 2, "Both bilingual fields required and no extra properties");
            Assert(((Dictionary<string, object>)schema["properties"]).ContainsKey("display_text") && ((Dictionary<string, object>)schema["properties"]).ContainsKey("speech_ja"), "Schema separates display and Japanese speech");
            Assert((int)bilingualPayload["max_output_tokens"] == 1600 && (bool)bilingualPayload["store"] == false, "Bilingual output budget without response storage");
            Assert(bilingualPayload["instructions"].ToString().Contains("アンナ"), "Japanese name pronunciation is explicit");
            string dualText = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "display_text", "早上好！一起吃面包吧。" }, { "speech_ja", "おはよう！一緒にパンを食べよう。" } });
            string dualResponse = Envelope(new string[] { dualText }, "completed", "");
            GptReply dual = GptClient.ParseBilingualReply(dualResponse);
            Assert(dual.DisplayText == "早上好！一起吃面包吧。", "Chinese display extracted without protocol JSON");
            Assert(dual.JapaneseSpeechText == "おはよう！一緒にパンを食べよう。" && dual.SpeechNotice == "", "Only Japanese text reaches voice output");
            GptReply split = GptClient.ParseBilingualReply(Envelope(new string[] { dualText.Substring(0, 18), dualText.Substring(18) }, "completed", ""));
            Assert(split.DisplayText == dual.DisplayText && split.JapaneseSpeechText == dual.JapaneseSpeechText, "Bilingual JSON fragments across all output messages reassembled");
            GptReply denied = GptClient.ParseBilingualReply(Envelope(new string[0], "completed", "这个请求我无法帮助。"));
            Assert(denied.DisplayText == "这个请求我无法帮助。" && denied.JapaneseSpeechText == "" && denied.SpeechNotice.Length > 0, "Refusal displayed without reading Chinese as Japanese");
            GptReply plainUnexpected = GptClient.ParseBilingualReply(Envelope(new string[] { "我在这里，慢慢说。" }, "completed", ""));
            Assert(plainUnexpected.DisplayText == "我在这里，慢慢说。" && plainUnexpected.JapaneseSpeechText == "", "Unexpected genuine plain text remains readable and unspoken");
            GptReply primitiveText = GptClient.ParseBilingualReply(Envelope(new string[] { "\"单独的中文字符串\"" }, "completed", ""));
            Assert(primitiveText.DisplayText == "单独的中文字符串" && primitiveText.JapaneseSpeechText == "", "JSON string primitive decoded rather than exposing serialized quotes");
            Throws(delegate { GptClient.ParseBilingualReply(Envelope(new string[] { "123" }, "completed", "")); }, "Non-text JSON primitive is not displayed as protocol output");
            GptReply missingSpeech = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"中文仍可见\"}" }, "completed", ""));
            Assert(missingSpeech.DisplayText == "中文仍可见" && missingSpeech.JapaneseSpeechText == "" && missingSpeech.SpeechNotice.Length > 0, "Missing speech field preserves Chinese only");
            GptReply wrongLanguage = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"你好。\",\"speech_ja\":\"你好，今天也请加油。\"}" }, "completed", ""));
            Assert(wrongLanguage.JapaneseSpeechText == "" && wrongLanguage.SpeechNotice.Length > 0, "Chinese-only voice text blocked");
            GptReply mixedLanguage = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"你好。\",\"speech_ja\":\"こんにちは，今天我们一起加油。\"}" }, "completed", ""));
            Assert(mixedLanguage.JapaneseSpeechText == "", "Obvious Chinese mixed into kana text blocked");
            GptReply romanized = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"你好。\",\"speech_ja\":\"konnichiwa\"}" }, "completed", ""));
            Assert(romanized.JapaneseSpeechText == "", "Romanized text not passed as Japanese script");
            GptReply speechUrl = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"查看链接。\",\"speech_ja\":\"こちらです https://example.invalid\"}" }, "completed", ""));
            Assert(speechUrl.JapaneseSpeechText == "", "URL voice script blocked");
            GptReply incompleteDual = GptClient.ParseBilingualReply(Envelope(new string[] { dualText }, "incomplete", ""));
            Assert(incompleteDual.DisplayText.Contains("早上好") && incompleteDual.JapaneseSpeechText == "" && incompleteDual.SpeechNotice.Length > 0, "Incomplete response never spoken even when inner JSON is complete");
            GptReply truncated = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"保留中文\\n第二行\",\"speech_ja\":\"まだ" }, "incomplete", ""));
            Assert(truncated.DisplayText.Contains("保留中文\n第二行") && truncated.JapaneseSpeechText == "", "Truncated JSON salvages completed escaped display field only");
            GptReply damaged = GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"仍可显示\",\"speech_ja\":BROKEN}" }, "completed", ""));
            Assert(damaged.DisplayText == "仍可显示" && damaged.JapaneseSpeechText == "", "Damaged JSON salvages display without exposing raw JSON");
            Throws(delegate { GptClient.ParseBilingualReply(Envelope(new string[] { "{\"speech_ja\":\"こんにちは\"}" }, "completed", "")); }, "Missing display rejects response without raw JSON");
            Throws(delegate { GptClient.ParseBilingualReply(Envelope(new string[] { "{\"display_text\":\"未完成" }, "incomplete", "")); }, "Truncated display without complete string fails safely");
            Throws(delegate { GptClient.ParseBilingualReply("{\"status\":\"failed\",\"output\":[]}"); }, "Failed bilingual response rejected");
            Assert(GptClient.FriendlyError(400, "{\"error\":{\"code\":\"unsupported_parameter\",\"param\":\"text.format\",\"message\":\"secret-key\"}}", true).Contains("关闭自动配音"), "Unsupported structured output gives explicit model or mode remedy");
            Assert(!GptClient.FriendlyError(400, "{\"error\":{\"code\":\"unsupported_parameter\",\"param\":\"text.format\",\"message\":\"secret-key\"}}", true).Contains("secret-key"), "Bilingual error does not echo raw API error");
            int bilingualCalls = 0;
            GptClient bilingualClient = new GptClient(async delegate(HttpRequestMessage request, CancellationToken cancel)
            {
                bilingualCalls++;
                Assert(Json(await request.Content.ReadAsStringAsync()).ContainsKey("text"), "Enabled SendReplyAsync sends schema");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(dualResponse, Encoding.UTF8, "application/json") };
            });
            GptReply sentDual = await bilingualClient.SendReplyAsync(settings, history, "早上好", true, CancellationToken.None);
            Assert(sentDual.DisplayText == dual.DisplayText && sentDual.JapaneseSpeechText == dual.JapaneseSpeechText && bilingualCalls == 1, "One API request produces both display and Japanese voice text");
            Assert(history.Count == 24, "Bilingual request does not mutate history");
            GptClient plainClient = new GptClient(async delegate(HttpRequestMessage request, CancellationToken cancel)
            {
                Assert(!Json(await request.Content.ReadAsStringAsync()).ContainsKey("text"), "Disabled SendReplyAsync reuses plain request without schema");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            });
            GptReply sentPlain = await plainClient.SendReplyAsync(settings, history, "你好", false, CancellationToken.None);
            Assert(sentPlain.DisplayText.Contains("杏菜") && sentPlain.JapaneseSpeechText == "" && sentPlain.SpeechNotice == "", "Disabled speech preserves original Chinese behavior");
            int unsupportedCalls = 0;
            GptClient unsupported = new GptClient(delegate(HttpRequestMessage request, CancellationToken cancel)
            {
                unsupportedCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"param\":\"text.format\",\"code\":\"unsupported_parameter\"}}") });
            });
            await ThrowsAsync(async delegate { return (await unsupported.SendReplyAsync(settings, history, "你好", true, CancellationToken.None)).DisplayText; }, "Unsupported model fails without automatic fallback request");
            Assert(unsupportedCalls == 1 && history.Count == 24, "Unsupported bilingual mode does not issue second request or pollute history");

            settings.RememberKey = true;
            settings.Save();
            byte[] encrypted = File.ReadAllBytes(Path.Combine(Program.DataDir, "gpt-key.bin"));
            Assert(!Encoding.UTF8.GetString(encrypted).Contains("test-key-not-real"), "DPAPI file is encrypted");
            Assert(!File.ReadAllText(Path.Combine(Program.DataDir, "gpt-settings.json")).Contains("test-key-not-real"), "Settings JSON contains no API secret");
            GptSettings restored = GptSettings.Load();
            Assert(restored.ApiKey == "test-key-not-real" && restored.RememberKey, "DPAPI current-user round trip");
            settings.RememberKey = false;
            settings.Save();
            Assert(!File.Exists(Path.Combine(Program.DataDir, "gpt-key.bin")), "Opting out removes stored credential");
            Assert(GptSettings.Load().ApiKey == "", "Unremembered key not restored");
            Program.Smoke = true;
            settings.RememberKey = true;
            settings.Save();
            Assert(!File.Exists(Path.Combine(Program.DataDir, "gpt-key.bin")), "Smoke mode does not save credentials");
            Program.Smoke = false;
            settings.Save();
            File.WriteAllBytes(Path.Combine(Program.DataDir, "gpt-key.bin"), new byte[] { 1, 2, 3 });
            Assert(GptSettings.Load().ApiKey == "", "Damaged credential does not crash startup");
        }

        private static Dictionary<string, object> Json(string value) { return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(value); }
        private static string Envelope(string[] parts, string status, string refusal)
        {
            List<object> output = new List<object>();
            output.Add(new Dictionary<string, object> { { "type", "reasoning" }, { "summary", new object[0] } });
            foreach (string part in parts)
                output.Add(new Dictionary<string, object> { { "type", "message" }, { "content", new object[] { new Dictionary<string, object> { { "type", "output_text" }, { "text", part } } } } });
            if (refusal.Length > 0)
                output.Add(new Dictionary<string, object> { { "type", "message" }, { "content", new object[] { new Dictionary<string, object> { { "type", "refusal" }, { "refusal", refusal } } } } });
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "status", status }, { "output", output } });
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
        private static void Throws(Action action, string name)
        { try { action(); } catch (ArgumentException) { Assert(true, name); return; } catch (InvalidOperationException) { Assert(true, name); return; } throw new Exception(name); }
        private static async Task ThrowsAsync(Func<Task<string>> action, string name)
        { try { await action(); } catch (InvalidOperationException) { Assert(true, name); return; } throw new Exception(name); }
    }
}
