using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // Opt-in only. Uses synthetic text and existing per-provider credentials through the app's loader.
    // Does not execute music, read conversations, save settings, or print credentials/HTTP payloads.
    internal static class LiveMusicIntentTests
    {
        private static int passed;
        private static int failed;

        [STAThread]
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--live") < 0)
            {
                Console.WriteLine("SKIP: opt-in required (--live). This test makes real API requests.");
                return 2;
            }
            Program.Smoke = true;
            try { return Run(Array.IndexOf(args, "--pretender-only") >= 0, Array.IndexOf(args, "--clarify-follow-up-only") >= 0).GetAwaiter().GetResult(); }
            catch (Exception) { Console.WriteLine("BLOCKED: test could not finish. No credentials or raw response are logged."); return 2; }
        }

        private static async Task<int> Run(bool pretenderOnly, bool clarifyFollowUpOnly)
        {
#if DEEPSEEK
            const string provider = "DeepSeek";
#else
            const string provider = "GPT";
#endif
            GptSettings settings = GptSettings.Load();
            if (String.IsNullOrWhiteSpace(settings.ApiKey))
            {
                Console.WriteLine(provider + " SKIP: no saved API key accessible through GptSettings.Load.");
                return 2;
            }
            var history = new List<ChatMessage> {
                new ChatMessage("user", "推荐一首日语歌给我吧"),
                new ChatMessage("assistant", "推荐 YOASOBI 的《夜に駆ける》，节奏很有活力。")
            };
            var empty = new MusicCandidateSnapshot { Version = "test-empty" };
            var choices = new MusicCandidateSnapshot { Version = "test-list", Candidates = new List<string> {
                "夜に駆ける — YOASOBI（THE BOOK）", "夜に駆ける (Piano Cover) — Piano Artist（Piano Covers）"
            }};
            var client = new GptClient();
            try
            {
                if (clarifyFollowUpOnly)
                {
                    var clarificationHistory = new List<ChatMessage> {
                        new ChatMessage("user", "推荐一首日语歌"),
                        new ChatMessage("assistant", "推荐《夜に駆ける》吧，YOASOBI 的，节奏轻快，听一遍脑子里就甩不掉了。想更安静一点的话《打上花火》也不错——你今天是想要提神的还是想听慢歌？"),
                        new ChatMessage("user", "放给我听听"),
                        new ChatMessage("assistant", "你想听《夜に駆ける》还是《打上花火》？")
                    };
                    // The transport is real only in this explicitly opted-in synthetic diagnostic.
                    // Log model content/field types, never request headers, keys or user history.
                    var diagnostic = new GptClient(async delegate(HttpRequestMessage request, CancellationToken token)
                    {
                        using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                        using (var http = new HttpClient(handler))
                        {
                            http.Timeout = TimeSpan.FromSeconds(60);
                            HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode) LogSyntheticIntent(await response.Content.ReadAsStringAsync().ConfigureAwait(false), settings.ApiKey);
                            return response;
                        }
                    });
                    MusicIntent followUp = await diagnostic.ResolveMusicIntentAsync(settings, clarificationHistory, "前一首吧", empty, CancellationToken.None);
                    Check(provider, "first recommendation after clarification", followUp,
                        followUp.Intent == "play" && followUp.Query.Contains("夜に駆ける") &&
                        followUp.Query.IndexOf("YOASOBI", StringComparison.OrdinalIgnoreCase) >= 0);
                    Console.WriteLine(provider + ": " + passed + " passed, " + failed + " failed live classification cases; no music actions executed.");
                    return failed == 0 ? 0 : 1;
                }
                if (pretenderOnly)
                {
                    var pretenderHistory = new List<ChatMessage> {
                        new ChatMessage("user", "推荐一首日语歌"),
                        new ChatMessage("assistant", "推荐 Official髭男dism 的《Pretender》，旋律很抓耳。你平时偏快歌还是抒情一点？")
                    };
                    MusicIntent followUp = await client.ResolveMusicIntentAsync(settings, pretenderHistory, "放给我听听", empty, CancellationToken.None);
                    Check(provider, "Pretender follow-up after recommendation and question", followUp,
                        followUp.Intent == "play" && followUp.Query.IndexOf("Pretender", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        followUp.Query.Contains("髭男") && !followUp.Query.Contains("给我听听"));
                    Console.WriteLine(provider + ": " + passed + " passed, " + failed + " failed live classification cases; no music actions executed.");
                    return failed == 0 ? 0 : 1;
                }
                MusicIntent intent = await client.ResolveMusicIntentAsync(settings, history, "可以，那你开始播放吧", empty, CancellationToken.None);
                Check(provider, "recommendation acceptance", intent,
                    intent.Intent == "play" && intent.Query.Contains("夜に駆ける") && intent.Query.IndexOf("YOASOBI", StringComparison.OrdinalIgnoreCase) >= 0);
                intent = await client.ResolveMusicIntentAsync(settings, history, "第二个版本", choices, CancellationToken.None);
                Check(provider, "current candidate selection", intent, intent.Intent == "choose" && intent.Choice == 2);
                intent = await client.ResolveMusicIntentAsync(settings, history, "不要播放，只聊聊这首歌", choices, CancellationToken.None);
                Check(provider, "negative playback instruction", intent, intent.Intent == "chat");
                intent = await client.ResolveMusicIntentAsync(settings, new List<ChatMessage>(), "你来放", empty, CancellationToken.None);
                Check(provider, "missing referent clarification", intent, intent.Intent == "clarify" && !String.IsNullOrWhiteSpace(intent.Question));
            }
            catch (Exception ex)
            {
                string message = (ex.Message ?? "").Replace(settings.ApiKey, "[redacted]");
                if (message.Length > 350) message = message.Substring(0, 350);
                Console.WriteLine(provider + " BLOCKED: " + message);
                Console.WriteLine("No remaining scenarios requested. No QQ Music action was executed.");
                return 2;
            }
            Console.WriteLine(provider + ": " + passed + " passed, " + failed + " failed live classification cases; no music actions executed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string provider, string scenario, MusicIntent intent, bool valid)
        {
            if (valid) passed++; else failed++;
            Console.WriteLine(provider + " " + (valid ? "PASS" : "FAIL") + " " + scenario + ": intent=" + intent.Intent + ", query=" + intent.Query + ", choice=" + intent.Choice);
        }

        private static void LogSyntheticIntent(string envelope, string apiKey)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = (Dictionary<string, object>)serializer.DeserializeObject(envelope);
#if DEEPSEEK
                var choices = (object[])root["choices"];
                var first = (Dictionary<string, object>)choices[0];
                var message = (Dictionary<string, object>)first["message"];
                string content = Convert.ToString(message["content"]);
#else
                string content = GptClient.ParseReply(envelope);
#endif
                string redacted = String.IsNullOrEmpty(apiKey) ? content : content.Replace(apiKey, "[redacted]");
                if (redacted.Length > 1800) redacted = redacted.Substring(0, 1800);
                Console.WriteLine("Synthetic model intent content: " + redacted);
                var data = serializer.DeserializeObject(content) as Dictionary<string, object>;
                if (data != null)
                    foreach (var field in data)
                        Console.WriteLine("Synthetic field type: " + field.Key + "=" + (field.Value == null ? "null" : field.Value.GetType().Name));
            }
            catch { Console.WriteLine("Synthetic response shape could not be decoded; no raw HTTP response logged."); }
        }
    }
}
