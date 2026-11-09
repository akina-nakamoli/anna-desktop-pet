using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // Explicit opt-in synthetic evaluation. No QQ actions, transcripts, memory writes or real user stories.
    internal static class LiveGeneralContextTests
    {
        private static int passed, failed;
        [STAThread]
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--live") < 0) {
                Console.WriteLine("SKIP: --live is required. Default mode makes five real requests; --maintenance makes four."); return 2;
            }
            Program.Smoke = true;
            try { return Run(Array.IndexOf(args, "--maintenance") >= 0).GetAwaiter().GetResult(); }
            catch (Exception) { Console.WriteLine("BLOCKED: evaluation could not finish; no credentials are printed."); return 2; }
        }

        private static async Task<int> Run(bool maintenance)
        {
#if DEEPSEEK
            const string provider = "DeepSeek";
#else
            const string provider = "GPT";
#endif
            GptSettings settings = GptSettings.Load();
            if (String.IsNullOrWhiteSpace(settings.ApiKey)) {
                Console.WriteLine(provider + " SKIP: saved API key unavailable."); return 2;
            }
            var client = new GptClient();
            try
            {
                if (maintenance) return await RunMaintenance(provider, client, settings);
                var referent = new ConversationHistory {
                    new ChatMessage("user", "以下全是测试设定。安禾是我的同学，雪团是她的猫。明天她带雪团去复查。"),
                    new ChatMessage("assistant", "希望复查顺利。")
                };
                string answer = await Reply(client, settings, referent, "刚才说的她叫什么？她的猫呢？只回答这两个名字。");
                Report(provider, "recent named referent", answer, answer.Contains("安禾") && answer.Contains("雪团"));

                var oldTopic = new ConversationHistory();
                oldTopic.Summary = "测试故事：用户和同学安禾已经约好下周去白鹭营地露营；这是用户确认的安排。";
                oldTopic.SummarizedCount = 2;
                oldTopic.Add(new ChatMessage("user", "我已经和安禾约好下周去白鹭营地露营。"));
                oldTopic.Add(new ChatMessage("assistant", "好，我们之后可以再讨论准备的东西。"));
                for (int i = 0; i < 10; i++) {
                    oldTopic.Add(new ChatMessage("user", "临时换个话题：今天第" + i + "个天气记录是晴。"));
                    oldTopic.Add(new ChatMessage("assistant", "这条天气记录是晴。"));
                }
                answer = await Reply(client, settings, oldTopic, "回到之前露营的安排，谁和我一起，目的地叫什么？只回答人名和地点。");
                Report(provider, "older summary topic return", answer, answer.Contains("安禾") && answer.Contains("白鹭营地"));

                var correction = new ConversationHistory();
                correction.Summary = "旧测试信息：用户露营预算是300元。";
                correction.SummarizedCount = 2;
                correction.Add(new ChatMessage("user", "我的露营预算是300元。"));
                correction.Add(new ChatMessage("assistant", "按300元计划。"));
                correction.Add(new ChatMessage("user", "更正，我的最终预算改成500元，之前的300元已经作废。"));
                correction.Add(new ChatMessage("assistant", "收到，按你的最新更正来。"));
                answer = await Reply(client, settings, correction, "我的最终预算是多少？只回答最终金额。");
                Report(provider, "latest correction beats old summary", answer, answer.Contains("500") && !answer.Contains("300"));

                var options = new ConversationHistory {
                    new ChatMessage("user", "我的周末两种出行方案：第一种去河边骑自行车，第二种去科技馆。"),
                    new ChatMessage("assistant", "骑车能活动身体，科技馆适合慢慢逛。"),
                    new ChatMessage("user", "先换个话题，晚饭吃乌冬面。"),
                    new ChatMessage("assistant", "好，那晚饭就乌冬面。"),
                    new ChatMessage("user", "说回最开始周末的两种出行方案。"),
                    new ChatMessage("assistant", "我们回到出行方案。")
                };
                answer = await Reply(client, settings, options, "我要选第一种，具体是什么？只说那个方案，不要说晚饭。");
                Report(provider, "topic return resolves first option", answer,
                    (answer.Contains("自行车") || answer.Contains("骑车") || answer.Contains("骑行")) && !answer.Contains("乌冬"));

                answer = await Reply(client, settings, new ConversationHistory(), "他叫什么名字？");
                Report(provider, "missing referent asks instead of inventing", answer,
                    answer.Contains("谁") || answer.Contains("哪位") || answer.Contains("哪个") || answer.Contains("指的是") || answer.Contains("说的是"));

                var later = new ConversationHistory {
                    new ChatMessage("user", "这是虚构的测试故事：小林申请转岗，随后通过了审核，去了设计部；小陈仍在销售部。"),
                    new ChatMessage("assistant", "刚才说到小林申请转岗。")
                };
                answer = await Reply(client, settings, later, "他后来呢？只说已有故事里的后续，不要续写。");
                Report(provider, "he later follows the named subject without writing a new story", answer,
                    answer.Contains("设计部") && !answer.Contains("去了销售部"));

                var previous = new ConversationHistory {
                    new ChatMessage("user", "周末方案有两个：第一个去河边骑自行车，第二个去科技馆。"),
                    new ChatMessage("assistant", "那就去科技馆？")
                };
                answer = await Reply(client, settings, previous, "不是这个，我说的是前一个。是哪一个？只回答那个方案。");
                Report(provider, "not this the previous one resolves corrected selection", answer,
                    (answer.Contains("自行车") || answer.Contains("骑车") || answer.Contains("骑行")) && !answer.Contains("科技馆"));
            }
            catch (Exception ex)
            {
                string safe = (ex.Message ?? "").Replace(settings.ApiKey, "[redacted]");
                Console.WriteLine(provider + " BLOCKED: " + (safe.Length > 250 ? safe.Substring(0, 250) : safe));
                Console.WriteLine("Remaining cases not requested. Only synthetic conversations were sent."); return 2;
            }
            Console.WriteLine(provider + ": " + passed + " passed, " + failed + " failed synthetic semantic probes. Heuristic checks plus printed replies; not a guarantee for all conversations.");
            return failed == 0 ? 0 : 1;
        }

        private static async Task<int> RunMaintenance(string provider, GptClient client, GptSettings settings)
        {
            var summaryEvidence = new List<ChatMessage> {
                new ChatMessage("user", "安禾是我的同学，她的猫叫雪团。"),
                new ChatMessage("assistant", "安禾和雪团，我明白了。"),
                new ChatMessage("user", "更正露营预算，现在最终是500元，之前300元作废；同行的人和地点都没变。")
            };
            string summary = await client.SummarizeConversationAsync(settings, "用户和同学安禾下周去白鹭营地露营，预算300元。", summaryEvidence, CancellationToken.None);
            Report(provider, "generated summary preserves named referents and latest correction", summary,
                summary.Contains("安禾") && summary.Contains("雪团") && summary.Contains("白鹭营地") && summary.Contains("500"));

            var repeatedChoice = new List<ChatMessage> {
                new ChatMessage("user", "每天上班路上我都会买一杯无糖咖啡。"),
                new ChatMessage("assistant", "那条路上应该有顺路的咖啡店吧。"),
                new ChatMessage("user", "今天在饮料区我又挑了无糖咖啡，甜的那几款还是没拿。")
            };
            string proposed = await client.ExtractMemoriesAsync(settings, repeatedChoice, new string[0], CancellationToken.None);
            List<SemanticMemory> candidates = SemanticMemory.Parse(proposed, repeatedChoice);
            bool inferred = candidates.Exists(delegate(SemanticMemory candidate) {
                return candidate.Kind == "preference" && candidate.Certainty == "tentative" && candidate.Text.Contains("咖啡") && candidate.Evidence.Length > 0;
            });
            Report(provider, "implicit beverage preference is evidence-grounded and tentative", proposed, inferred);

            const string previous = "用户可能偏好无糖咖啡";
            var correctionEvidence = new List<ChatMessage> {
                new ChatMessage("user", "不是喜欢咖啡，我已经戒咖啡了，以后别再推荐咖啡。")
            };
            string corrected = await client.ExtractMemoriesAsync(settings, correctionEvidence, new[] { previous }, CancellationToken.None);
            candidates = SemanticMemory.Parse(corrected, correctionEvidence);
            bool replaced = candidates.Exists(delegate(SemanticMemory candidate) {
                return candidate.Certainty == "explicit" && candidate.Replaces == previous && candidate.Text.Contains("咖啡") &&
                    (candidate.Text.Contains("戒") || candidate.Text.Contains("不推荐") || candidate.Text.Contains("不要推荐") || candidate.Text.Contains("不希望"));
            });
            Report(provider, "latest explicit correction names exact old memory replacement", corrected, replaced);

            var normal = new List<ChatMessage> {
                new ChatMessage("user", "安禾是我的同学，雪团是她的猫。"),
                new ChatMessage("assistant", "我知道你说的安禾和雪团了。")
            };
            MusicIntent intent = await client.ResolveMusicIntentAsync(settings, normal, "她的猫叫什么？", new MusicCandidateSnapshot(), CancellationToken.None);
            Report(provider, "ordinary referent remains chat in real intent resolver", intent.Intent, intent.Intent == "chat");
            Console.WriteLine(provider + ": " + passed + " passed, " + failed + " failed synthetic maintenance probes. No proposed memory was persisted; no QQ action executed.");
            return failed == 0 ? 0 : 1;
        }

        private static async Task<string> Reply(GptClient client, GptSettings settings, IList<ChatMessage> history, string input)
        {
            GptReply reply = await client.SendReplyAsync(settings, history, input, false, CancellationToken.None);
            return (reply.DisplayText ?? "").Replace(settings.ApiKey, "[redacted]");
        }
        private static void Report(string provider, string scenario, string answer, bool ok)
        {
            if (ok) passed++; else failed++;
            Console.WriteLine(provider + " " + (ok ? "PASS" : "FAIL") + " " + scenario + ": " + answer);
        }
    }
}
