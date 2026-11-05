using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal static class GptSovitsClientTests
    {
        private static int count;
        [STAThread]
        public static int Main(string[] args)
        {
            string folder = args.Length > 0 ? Path.GetFullPath(args[0]) : AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(folder);
            string reference = Path.Combine(folder, "contract-reference.wav"); byte[] wave = MakeWave(); File.WriteAllBytes(reference, wave);
            try
            {
                Assert(GptSovitsClient.TtsUri("http://127.0.0.1:9880").AbsolutePath == "/tts", "Root endpoint maps to /tts");
                Assert(GptSovitsClient.TtsUri("http://localhost:9880/tts").Host == "localhost", "Explicit localhost endpoint accepted");
                Assert(GptSovitsClient.TtsUri("http://[::1]:9880").Port == 9880, "IPv6 loopback accepted");
                Reject(delegate { GptSovitsClient.TtsUri("https://example.com"); }, "Remote service rejected");
                Reject(delegate { GptSovitsClient.TtsUri("http://127.0.0.1.evil.test"); }, "Loopback-looking hostname rejected");
                Reject(delegate { GptSovitsClient.TtsUri("http://user:password@localhost:9880"); }, "URL credentials rejected");
                Reject(delegate { GptSovitsClient.TtsUri("file:///C:/audio.wav"); }, "Non HTTP endpoint rejected");
                Reject(delegate { GptSovitsClient.TtsUri("http://127.0.0.1:9880/control"); }, "Control endpoint rejected");
                Reject(delegate { GptSovitsClient.TtsUri("http://127.0.0.1:9880/?x=1"); }, "Query endpoint rejected");
                Reject(delegate { GptSovitsClient.ValidateConfiguration("http://127.0.0.1:9880", "missing.wav", "こんにちは"); }, "Missing reference rejected");
                Reject(delegate { GptSovitsClient.ValidateConfiguration("http://127.0.0.1:9880", reference, " "); }, "Missing transcript rejected");
                GptSovitsClient.ValidateWave(wave); count++;
                Reject(delegate { GptSovitsClient.ValidateWave(Encoding.UTF8.GetBytes("{\"error\":\"bad model\"}")); }, "JSON response cannot be played as audio");
                byte[] malformed = (byte[])wave.Clone(); malformed[40] = 255; malformed[41] = 255; malformed[42] = 255; malformed[43] = 127;
                Reject(delegate { GptSovitsClient.ValidateWave(malformed); }, "Truncated data chunk rejected");

                StubHandler handler = new StubHandler(); handler.Wave = wave;
                GptSovitsClient client = new GptSovitsClient(handler);
                byte[] generated = client.GenerateAsync("http://127.0.0.1:9880", reference, "参考台詞。", "こんにちは。", 5, CancellationToken.None).GetAwaiter().GetResult();
                Assert(generated.Length == wave.Length && handler.RequestUri.AbsolutePath == "/tts" && handler.Method == "POST", "POST returns validated wave");
                Dictionary<string, object> body = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(handler.Body);
                Assert((string)body["text_lang"] == "ja" && (string)body["prompt_lang"] == "ja", "Target and reference language forced to Japanese");
                Assert((string)body["ref_audio_path"] == reference && (string)body["prompt_text"] == "参考台詞。", "Reference path and exact transcript passed");
                Assert((string)body["media_type"] == "wav" && !(bool)body["streaming_mode"] && Convert.ToDouble(body["speed_factor"]) == 1.25, "Non-streaming WAV and bounded speed configured");
                handler.Status = HttpStatusCode.BadRequest;
                Reject(delegate { client.GenerateAsync("http://127.0.0.1:9880", reference, "参考", "こんにちは", 0, CancellationToken.None).GetAwaiter().GetResult(); }, "Server error is surfaced without playback");
                handler.Status = HttpStatusCode.OK; handler.Wave = new byte[GptSovitsClient.MaximumWaveBytes + 1];
                Reject(delegate { client.GenerateAsync("http://127.0.0.1:9880", reference, "参考", "こんにちは", 0, CancellationToken.None).GetAwaiter().GetResult(); }, "Oversized response rejected before reading");
                handler.Wave = wave;
                string longText = "こんにちは。" + new string('あ', 100) + "、" + new string('い', 120) + "。終わりです。";
                IList<string> pieces = SpeechService.SplitSpeech(longText);
                Assert(pieces.Count > 2 && pieces[0] == "こんにちは。", "Long speech starts with a short complete sentence");
                Assert(String.Concat(pieces) == longText, "Speech chunks preserve text and order");
                Assert(SpeechService.SplitSpeech(new string('あ', 200))[0].Length <= 42, "Unpunctuated long sentence gets a bounded first request");
                using (CancellationTokenSource cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    bool stopped = false;
                    try { client.GenerateAsync("http://127.0.0.1:9880", reference, "参考", "こんにちは", 0, cancelled.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { stopped = true; }
                    Assert(stopped, "Cancellation preserved");
                }

                string output = Path.Combine(folder, "contract-output.wav");
                using (SpeechService service = new SpeechService(Dispatcher.CurrentDispatcher, output, client))
                {
                    service.ConfigureBackend("gpt-sovits", "http://127.0.0.1:9880", reference, "参考台詞。");
                    Assert(service.Available && !service.IsSpeaking && service.Status.Contains("尚未验证"), "Valid config does not claim a running service or autoplay");
                    bool finished = false; service.StatusChanged += delegate(string status) { if (status.Contains("测试文件")) finished = true; };
                    service.Configure("", 1, 50); service.Speak("新しい台詞です。");
                    PumpUntil(delegate { return finished; }, 5000);
                    Assert(File.Exists(output) && !service.IsSpeaking, "Selected backend generates diagnostic audio without speakers");
                    finished = false; handler.RequestTexts.Clear(); service.Speak(longText);
                    PumpUntil(delegate { return finished; }, 5000);
                    Assert(handler.RequestTexts.Count == pieces.Count && handler.RequestTexts[0] == pieces[0] && String.Concat(handler.RequestTexts) == longText, "Long speech sends segments in order with first phrase first");
                    service.ConfigureBackend("gpt-sovits", "http://127.0.0.1:9880", "missing.wav", "参考台詞。");
                    Assert(!service.Available && !service.IsSpeaking, "Invalid clone config stays unavailable with no SAPI fallback");
                }

                DelayedHandler delayed = new DelayedHandler();
                string staleFile = Path.Combine(folder, "must-not-play.wav");
                using (SpeechService service = new SpeechService(Dispatcher.CurrentDispatcher, staleFile, new GptSovitsClient(delayed)))
                {
                    service.ConfigureBackend("gpt-sovits", "http://127.0.0.1:9880", reference, "参考台詞。");
                    service.Speak("遅延テスト。"); Assert(service.IsBusy && !service.IsSpeaking, "Pending synthesis is busy before playback"); service.Stop(); Assert(!service.IsBusy, "Cancellation clears pending synthesis");
                    delayed.Pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(wave) });
                    PumpFor(150);
                    Assert(!File.Exists(staleFile) && !service.IsSpeaking && service.Status.Contains("停止"), "Late cancelled response cannot write or start playing");
                }
                string report = "PASS: " + count + " local GPT-SoVITS contract assertions. Mock HTTP only; no real model, network connection, API credentials or speaker output.\r\n";
                File.WriteAllText(Path.Combine(folder, "clone-contract-report.txt"), report, new UTF8Encoding(false)); Console.WriteLine(report); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            internal byte[] Wave; internal string Body, Method; internal Uri RequestUri; internal HttpStatusCode Status = HttpStatusCode.OK; internal readonly List<string> RequestTexts = new List<string>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested(); RequestUri = request.RequestUri; Method = request.Method.Method;
                Body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                RequestTexts.Add((string)new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Body)["text"]);
                return new HttpResponseMessage(Status) { Content = new ByteArrayContent(Wave) };
            }
        }
        private sealed class DelayedHandler : HttpMessageHandler
        {
            internal readonly TaskCompletionSource<HttpResponseMessage> Pending = new TaskCompletionSource<HttpResponseMessage>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { return Pending.Task; }
        }
        private static byte[] MakeWave()
        {
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memory))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 3200); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(16000); writer.Write(32000); writer.Write((ushort)2); writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(3200);
                for (int i = 0; i < 1600; i++) writer.Write((short)(Math.Sin(i / 12.0) * 1000));
                return memory.ToArray();
            }
        }
        private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
        private static void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected, label); }
        private static void PumpUntil(Func<bool> condition, int milliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew(); DispatcherFrame frame = new DispatcherFrame(); DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
            timer.Interval = TimeSpan.FromMilliseconds(10); timer.Tick += delegate { if (condition() || elapsed.ElapsedMilliseconds >= milliseconds) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); if (!condition()) throw new TimeoutException("Mock voice test timed out.");
        }
        private static void PumpFor(int milliseconds) { Stopwatch elapsed = Stopwatch.StartNew(); PumpUntil(delegate { return elapsed.ElapsedMilliseconds >= milliseconds; }, milliseconds + 500); }
    }
}
