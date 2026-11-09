using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace AnnaDesktopPet
{
    internal static class StreamClientTests
    {
        private static int assertions;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        [STAThread] public static int Main()
        {
            Program.Smoke = true;
            try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + assertions + " streaming client assertions."); return 0; }
            catch(Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        }
        private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        private static GptSettings Settings() { return new GptSettings { ApiKey = "test-stream-key" }; }
        private static HttpResponseMessage Response(Stream stream)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream"); return response;
        }
        private static string Data(object value) { return "data: " + Json.Serialize(value) + "\n\n"; }
        private static string Delta(string value)
        {
#if DEEPSEEK
            return Data(new { choices = new[] { new { index = 0, delta = new { content = value }, finish_reason = (string)null } } });
#else
            return Data(new { type = "response.output_text.delta", delta = value, output_index = 0, content_index = 0 });
#endif
        }
        private static object FinalBody(string value)
        {
#if DEEPSEEK
            return new { choices = new[] { new { message = new { content = value }, finish_reason = "stop" } } };
#else
            return new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = value } } } } };
#endif
        }
        private static string End(string value)
        {
#if DEEPSEEK
            return Data(new { choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } } }) + "data: [DONE]\n\n";
#else
            return Data(new { type = "response.completed", response = FinalBody(value) });
#endif
        }
        private static async Task<GptReply> Read(string events, bool japanese, List<GptStreamUpdate> updates)
        {
            var client = new GptClient(delegate(HttpRequestMessage req, CancellationToken ct) { return Task.FromResult(Response(new FragmentStream(events, 3))); });
            return await client.SendReplyAsync(Settings(), new List<ChatMessage>(), "继续聊聊", japanese, CancellationToken.None, updates.Add);
        }
        private static async Task Fails(string events, string expected)
        {
            try { await Read(events, false, new List<GptStreamUpdate>()); throw new Exception("Expected stream failure: " + expected); }
            catch(InvalidOperationException ex) { Check(ex.Message.Contains(expected), "Friendly error should contain " + expected + ": " + ex.Message); }
        }
        private static async Task Run()
        {
            await FirstChunkBeforeFinal();
            var updates = new List<GptStreamUpdate>();
            var reply = await Read(": keepalive\r\nevent: ignored\r\n\r\n" + Delta("你好，") + Delta("雪团🙂。") + End("你好，雪团🙂。"), false, updates);
            Check(reply.DisplayText == "你好，雪团🙂。", "UTF8 fragment reconstruction");
            Check(updates.Exists(x => x.DisplayText == "你好，"), "Intermediate plain text emitted");
            Check(updates.TrueForAll(x => x.JapaneseSentence.Length == 0), "Plain text never spoken as Japanese");
            await BilingualEscapesAndSentences();
            await TimestampDoesNotFlash();
            await Fails(Delta("半句话"), "提前结束");
            await Fails("data: {bad}\n\n", "格式异常");
            await Fails("data: [DONE]\n\n", "完成标记");
            await Fails(Data(new { error = new { message = "secret-server-body" } }), "生成途中");
#if DEEPSEEK
            await Fails(Delta("半句") + Data(new { choices = new[] { new { delta = new { }, finish_reason = "length" } } }) + "data: [DONE]\n\n", "长度限制");
            updates.Clear();
            reply = await Read(Data(new { choices = new[] { new { delta = new { reasoning_content = "hidden-thought" }, finish_reason = (string)null } } }) + Delta("可见") + End("可见"), false, updates);
#else
            await Fails(Delta("半句") + Data(new { type = "response.incomplete", response = new { status = "incomplete" } }), "未完整");
            updates.Clear();
            reply = await Read(Data(new { type = "response.reasoning_summary_text.delta", delta = "hidden-thought" }) + Delta("可见") + End("可见"), false, updates);
#endif
            Check(reply.DisplayText == "可见" && updates.TrueForAll(x => !x.DisplayText.Contains("hidden")), "Reasoning hidden");
            await JsonFallbackAndHttpError();
            await CancelBlockedRead();
            await NoCallbackAfterCancel();
            await MultiLineEvent();
            await MusicNarrationStreams();
            updates.Clear();
            string malformed = "{\"display_text\":\"你好\",\"speech_ja\":\"こんにちは";
            try { await Read(Delta(malformed) + End(malformed), true, updates); throw new Exception("Malformed final bilingual JSON must fail"); }
            catch(InvalidOperationException) { Check(updates.TrueForAll(x => !x.DisplayText.StartsWith("{")), "Malformed final bilingual JSON never shown raw"); }
        }
        private static async Task FirstChunkBeforeFinal()
        {
            var gate = new GateStream(Delta("第一句。"));
            var arrived = new TaskCompletionSource<bool>();
            string requestJson = "";
            var client = new GptClient(async delegate(HttpRequestMessage req, CancellationToken ct) { requestJson = await req.Content.ReadAsStringAsync(); return Response(gate); });
            var task = client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", false, CancellationToken.None, delegate(GptStreamUpdate u) { if(u.DisplayText == "第一句。") arrived.TrySetResult(true); });
            Check(await Task.WhenAny(arrived.Task, Task.Delay(2500)) == arrived.Task, "First delta delivered before server completion");
            Check(!task.IsCompleted, "Full response still waiting after first callback");
            Check(requestJson.Contains("\"stream\":true"), "Actual streaming request flag");
            gate.Finish(Delta("第二句。") + End("第一句。第二句。"));
            var reply = await task;
            Check(reply.DisplayText == "第一句。第二句。", "Completion retains full text");
        }
        private static async Task BilingualEscapesAndSentences()
        {
            var updates = new List<GptStreamUpdate>();
            string raw = "{\"display_text\":\"你好\\n\\\"雪团\\\"\\uD83D\\uDE42\",\"speech_ja\":\"こんにちは。元気ですね！またね\"}";
            var all = new StringBuilder();
            foreach(char ch in raw) all.Append(Delta(ch.ToString()));
            all.Append(End(raw));
            var reply = await Read(all.ToString(), true, updates);
            Check(reply.DisplayText == "你好\n\"雪团\"🙂", "Partial JSON escapes and surrogate pair decoded");
            Check(updates.Exists(x => x.DisplayText == "你好"), "Bilingual Chinese before JSON completion");
            Check(updates.TrueForAll(x => !x.DisplayText.Contains("display_text") && !x.DisplayText.Contains("speech_ja") && !x.DisplayText.StartsWith("{")), "Raw JSON never displayed");
            var sentences = updates.FindAll(x => x.JapaneseSentence.Length > 0);
            Check(sentences.Count == 3, "Sentences once, final punctuation-free tail once");
            Check(sentences[0].JapaneseSentence == "こんにちは。" && sentences[1].JapaneseSentence == "元気ですね！" && sentences[2].JapaneseSentence == "またね", "Speech sentence order");
            Check(updates.TrueForAll(x => x.DisplayText.Length == 0 || !Char.IsHighSurrogate(x.DisplayText[x.DisplayText.Length - 1])), "No dangling high surrogate emitted");
            updates.Clear();
            raw = "{\"display_text\":\"引用 speech_ja: \\\"不是日语\\\"\",\"speech_ja\":\"你好。\"}";
            reply = await Read(Delta(raw) + End(raw), true, updates);
            Check(updates.TrueForAll(x => x.JapaneseSentence.Length == 0), "Invalid Chinese speech rejected");
            Check(reply.JapaneseSpeechText == "", "Final invalid Japanese absent");
            updates.Clear();
            raw = "{\"display_text\":\"你好\",\"speech_ja\":\"こんにちは。まだ\"}";
            string start = raw.Substring(0, raw.IndexOf("まだ"));
            var gate = new GateStream(Delta(start));
            var spoke = new TaskCompletionSource<bool>();
            var client = new GptClient((req, ct) => Task.FromResult(Response(gate)));
            var pending = client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", true, CancellationToken.None, delegate(GptStreamUpdate u) { if(u.JapaneseSentence == "こんにちは。") spoke.TrySetResult(true); });
            Check(await Task.WhenAny(spoke.Task, Task.Delay(2500)) == spoke.Task && !pending.IsCompleted, "Japanese complete sentence emitted before final JSON/server");
            gate.Finish(Delta(raw.Substring(start.Length)) + End(raw)); await pending;
        }
        private static async Task TimestampDoesNotFlash()
        {
            var updates = new List<GptStreamUpdate>();
            string raw = "【消息时间：2026-11-09T12:30:00+08:00】\n你好。";
            var events = new StringBuilder(); foreach(char ch in raw) events.Append(Delta(ch.ToString())); events.Append(End(raw));
            var reply = await Read(events.ToString(), false, updates);
            Check(reply.DisplayText == "你好。", "Final internal timestamp cleaned");
            Check(updates.TrueForAll(x => !x.DisplayText.Contains("消息时间") && !x.DisplayText.Contains("2026")), "Internal timestamp never flashed while partial");
        }
        private static async Task JsonFallbackAndHttpError()
        {
            int count = 0;
            var client = new GptClient((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json.Serialize(FinalBody("完整兼容回复")), Encoding.UTF8, "application/json") }));
            var reply = await client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", false, CancellationToken.None, delegate { count++; });
            Check(count == 1 && reply.DisplayText == "完整兼容回复", "Nonstream JSON compatibility once");
            int requests = 0;
            client = new GptClient(delegate(HttpRequestMessage req, CancellationToken ct) { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("secret-service-error") }); });
            try { await client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", false, CancellationToken.None, delegate { throw new Exception("Auth must not call progress"); }); throw new Exception("Auth should fail"); }
            catch(InvalidOperationException ex) { Check(!ex.Message.Contains("secret"), "HTTP error hides raw service payload"); }
            Check(requests == 1, "No retry on failed streamed request");
        }
        private static async Task CancelBlockedRead()
        {
            var gate = new GateStream(Delta("已经显示")); var token = new CancellationTokenSource();
            var seen = new TaskCompletionSource<bool>();
            var client = new GptClient((req, ct) => Task.FromResult(Response(gate)));
            var task = client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", false, token.Token, delegate { seen.TrySetResult(true); });
            await seen.Task; token.Cancel();
            Check(await Task.WhenAny(task, Task.Delay(2500)) == task, "Cancel releases stalled ReadAsync");
            try { await task; throw new Exception("Cancellation expected"); } catch(OperationCanceledException) { Check(gate.Disposed, "Cancellation disposes underlying body"); }
            token.Dispose();
        }
        private static async Task NoCallbackAfterCancel()
        {
            var token = new CancellationTokenSource(); int count = 0;
            var client = new GptClient((req, ct) => Task.FromResult(Response(new FragmentStream(Delta("一") + Delta("二") + End("一二"), 1024))));
            try { await client.SendReplyAsync(Settings(), new List<ChatMessage>(), "测试", false, token.Token, delegate { count++; token.Cancel(); }); throw new Exception("Cancellation expected"); }
            catch(OperationCanceledException) { Check(count == 1, "No progress after token cancelled in callback"); }
            token.Dispose();
        }
        private static async Task MultiLineEvent()
        {
            string ev = Delta("你好"); int comma = ev.IndexOf(',');
            ev = ev.Substring(0, comma + 1) + "\ndata: " + ev.Substring(comma + 1);
            var reply = await Read(ev + End("你好"), false, new List<GptStreamUpdate>());
            Check(reply.DisplayText == "你好", "Multiple data lines joined per SSE standard");
        }
        private static async Task MusicNarrationStreams()
        {
            int callbacks = 0; string json = "";
            var client = new GptClient(async delegate(HttpRequestMessage req, CancellationToken ct) { json = await req.Content.ReadAsStringAsync(); return Response(new FragmentStream(Delta("找到候选，请选择。") + End("找到候选，请选择。"), 3)); });
            var result = await client.SendMusicResultAsync(Settings(), new List<ChatMessage>(), "放这首", "找到候选，尚未播放", false, CancellationToken.None, delegate { callbacks++; });
            Check(callbacks > 0 && result.DisplayText.Contains("候选"), "Music narration callback wired");
            Check(json.Contains("找到候选，尚未播放") && json.Contains("\"stream\":true"), "Music tool result retained in streamed request");
        }
        private class FragmentStream : MemoryStream
        {
            private readonly int fragment;
            internal FragmentStream(string value, int size) : base(Encoding.UTF8.GetBytes(value)) { fragment = size; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            { return Task.FromResult(Read(buffer, offset, Math.Min(count, fragment))); }
        }
        private sealed class GateStream : Stream
        {
            private MemoryStream current;
            private readonly TaskCompletionSource<string> tail = new TaskCompletionSource<string>();
            private bool tailRead;
            internal bool Disposed;
            internal GateStream(string prefix) { current = new MemoryStream(Encoding.UTF8.GetBytes(prefix)); }
            internal void Finish(string value) { tail.TrySetResult(value); }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                int read = current.Read(buffer, offset, count); if(read > 0 || tailRead) return read;
                string text = await tail.Task.ConfigureAwait(false); tailRead = true;
                current = new MemoryStream(Encoding.UTF8.GetBytes(text)); return current.Read(buffer, offset, count);
            }
            protected override void Dispose(bool disposing) { Disposed = true; tail.TrySetCanceled(); base.Dispose(disposing); }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() {} public override int Read(byte[] b,int o,int c) { throw new NotSupportedException(); }
            public override long Seek(long o,SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long l) { throw new NotSupportedException(); }
            public override void Write(byte[] b,int o,int c) { throw new NotSupportedException(); }
        }
    }
}
