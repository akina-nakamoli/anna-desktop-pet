using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AnnaDesktopPet
{
    internal static class LiveStreamingTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--live") < 0) { Console.WriteLine("SKIP: explicit --live required (three synthetic API requests)."); return 2; }
            Program.Smoke = true;
            try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: live streaming, bilingual sentence delivery and cancellation."); return 0; }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex.GetType().Name); return 1; }
        }
        private static async Task Run()
        {
            GptSettings settings = GptSettings.Load();
            if (String.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("No saved key");
            var client = new GptClient();
            for (int run = 0; run < 2; run++)
            {
                bool bilingual = run == 1;
                var watch = Stopwatch.StartNew();
                long first = -1, sentenceFirst = -1;
                int updates = 0, sentences = 0;
                string visible = "", speech = "";
                var reply = await client.SendReplyAsync(settings, new List<ChatMessage>(),
                    "这是合成测试，不要记忆。请用六个简短句子描述一个虚构的雨天书店场景。", bilingual, CancellationToken.None,
                    delegate(GptStreamUpdate update) {
                        if (!String.IsNullOrEmpty(update.DisplayText) && update.DisplayText != visible) {
                            if (first < 0) first = watch.ElapsedMilliseconds;
                            visible = update.DisplayText; updates++;
                        }
                        if (!String.IsNullOrWhiteSpace(update.JapaneseSentence)) {
                            if (sentenceFirst < 0) sentenceFirst = watch.ElapsedMilliseconds;
                            speech += update.JapaneseSentence; sentences++;
                        }
                    });
                long total = watch.ElapsedMilliseconds;
                Console.WriteLine((bilingual ? "BILINGUAL" : "TEXT") + " first_text_ms=" + first + " first_sentence_ms=" + sentenceFirst + " total_ms=" + total + " text_updates=" + updates + " sentences=" + sentences);
                if (first < 0 || first >= total || updates < 2 || visible != reply.DisplayText || visible.Contains("\"display_text\"")) throw new Exception("Not incremental visible text");
                if (bilingual && (sentences < 2 || sentenceFirst >= total || String.IsNullOrWhiteSpace(speech))) throw new Exception("Not incremental speech");
            }
            using (var stop = new CancellationTokenSource())
            {
                int afterCancel = 0; bool cancelled = false;
                var watch = Stopwatch.StartNew(); long requested = -1;
                try {
                    await client.SendReplyAsync(settings, new List<ChatMessage>(), "合成测试：请用十个句子讲一个小猫寻找雨伞的虚构故事。", false, stop.Token,
                        delegate(GptStreamUpdate update) {
                            if (stop.IsCancellationRequested) { afterCancel++; return; }
                            if (!String.IsNullOrWhiteSpace(update.DisplayText)) { requested = watch.ElapsedMilliseconds; stop.Cancel(); }
                        });
                }
                catch (OperationCanceledException) { cancelled = true; }
                Console.WriteLine("CANCEL elapsed_after_request_ms=" + (watch.ElapsedMilliseconds - requested) + " later_callbacks=" + afterCancel);
                if (!cancelled || requested < 0 || afterCancel != 0) throw new Exception("Stream failed to stop");
            }
        }
    }
}
