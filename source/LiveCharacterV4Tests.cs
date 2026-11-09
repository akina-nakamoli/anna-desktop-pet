using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AnnaDesktopPet
{
    // Opt-in real provider probes using synthetic conversations only. No stored conversation is loaded,
    // no memory proposal is applied, and no music or other desktop action is invoked.
    internal static class LiveCharacterV4Tests
    {
        private static int requests, passed, failed, reviews;
        [STAThread]
        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--live") < 0)
            {
                Console.WriteLine("SKIP: --live is required; All makes seven real requests, --chat four, --maintenance three.");
                return 2;
            }
            // Set before any PetMemory method is used; existing_records and persona memory stay empty.
            Program.Smoke = true;
            try
            {
                string scope = Array.IndexOf(args, "--chat") >= 0 ? "Chat" :
                    Array.IndexOf(args, "--maintenance") >= 0 ? "Maintenance" : "All";
                return Run(scope).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                Console.WriteLine("BLOCKED: evaluation did not finish. No exception details or credentials are printed.");
                return 2;
            }
        }

        private static async Task<int> Run(string scope)
        {
#if DEEPSEEK
            const string provider = "DeepSeek";
#else
            const string provider = "GPT";
#endif
            if (PetMemory.Context().Length != 0 || PetMemory.SnapshotJson() != "[]")
            {
                Console.WriteLine("BLOCKED: synthetic memory isolation check failed; no request made.");
                return 2;
            }
            GptSettings settings = GptSettings.Load();
            if (String.IsNullOrWhiteSpace(settings.ApiKey))
            {
                Console.WriteLine(provider + " SKIP: configured API key unavailable.");
                return 2;
            }
            var client = new GptClient();
            try
            {
                if (scope != "Maintenance") await Chat(provider, client, settings);
                if (scope != "Chat") await Maintenance(provider, client, settings);
            }
            catch (Exception)
            {
                // Provider/transport exceptions may contain request details. Do not print them here.
                Console.WriteLine(provider + " BLOCKED: request or response validation failed after " + requests + " requests; remaining cases were not sent.");
                return 2;
            }
            Console.WriteLine(provider + ": " + requests + " synthetic requests; " + passed + " structural/boundary checks passed, " + failed + " failed; " + reviews + " replies require human review.");
            Console.WriteLine("Printed character behavior is for qualitative review, not an automatic persona-quality verdict. No personal memory was read or written; no desktop action ran.");
            return failed == 0 ? 0 : 1;
        }

        private static async Task Chat(string provider, GptClient client, GptSettings settings)
        {
            var commitment = new ConversationHistory {
                new ChatMessage("user", "我们进行原创校园角色扮演：周六下午两点，你已经答应小鞠一起整理文艺部书架。这个约定尚未变更。"),
                new ChatMessage("assistant", "嗯，小鞠和我已经约好了，周六两点去文艺部整理书架。")
            };
            await ReviewReply(provider, client, settings, "commitment vs new invitation", commitment,
                "周六下午两点跟我去看电影吧，就我们两个人。", false,
                "Keep the established commitment or discuss rescheduling; do not silently cancel it or invent other commitments.");

            var knowledge = new ConversationHistory {
                new ChatMessage("user", "原创剧情中，我只悄悄告诉了你一个秘密：我给小鞠准备了蓝色书签。温水没有在场，之后你和我都没有告诉他，也没人看见书签。"),
                new ChatMessage("assistant", "好，蓝色书签的事我先保密。")
            };
            await ReviewReply(provider, client, settings, "private knowledge scope", knowledge,
                "温水现在知道我要送什么给小鞠吗？", false,
                "He has not learned this secret; do not claim mind-reading or an offscreen disclosure.");

            var cancelled = new ConversationHistory {
                new ChatMessage("user", "在这段原创剧情中，我们原定周日在河边野餐，只是计划，还没有出发。"),
                new ChatMessage("assistant", "好，先把周日的野餐记下来。"),
                new ChatMessage("user", "更正：那次野餐已经取消，我们没有去，也没有另约时间。"),
                new ChatMessage("assistant", "知道了，取消那次野餐，还没有新的安排。")
            };
            await ReviewReply(provider, client, settings, "cancelled plan stays unexecuted", cancelled,
                "我们上次在河边野餐时吃了什么？", false,
                "Correct the false premise: the fictional plan was cancelled. Do not invent a meal or another date.");

            await ReviewReply(provider, client, settings, "ordinary desktop chat in bilingual mode", new ConversationHistory(),
                "我刚忙完，想和你随便聊两句。你有什么轻松的话题吗？", true,
                "Natural friendly conversation with matching Japanese speech; do not invent a current lesson, trip or appointment to refuse chatting.");
        }

        private static async Task ReviewReply(string provider, GptClient client, GptSettings settings, string name,
            IList<ChatMessage> history, string input, bool bilingual, string expectation)
        {
            requests++;
            GptReply reply = await client.SendReplyAsync(settings, history, input, bilingual, CancellationToken.None);
            Check(provider, name + " nonempty response", reply != null && !String.IsNullOrWhiteSpace(reply.DisplayText));
            if (bilingual) Check(provider, name + " speech returned", reply != null && !String.IsNullOrWhiteSpace(reply.JapaneseSpeechText));
            Review(provider, name, expectation, reply == null ? "[null]" : reply.DisplayText, settings);
            if (bilingual && reply != null) PrintSafe("Japanese speech: " + reply.JapaneseSpeechText, settings);
        }

        private static async Task Maintenance(string provider, GptClient client, GptSettings settings)
        {
            var summaryEvidence = new List<ChatMessage> {
                new ChatMessage("user", "以下是原创角色扮演剧情。我们原定周日在河边给小鞠送蓝色书签，但尚未出发；书签的秘密只有我和杏菜知道，温水没有被告知。"),
                new ChatMessage("assistant", "记下这个剧情中的约定，先不告诉温水。"),
                new ChatMessage("user", "剧情更新：送书签计划取消了，没有去河边，也没有送出去；书签仍是秘密。"),
                new ChatMessage("assistant", "那我现在把书签交给【生成中断，未完成】")
            };
            requests++;
            string summary = await client.SummarizeConversationAsync(settings, "", summaryEvidence, CancellationToken.None);
            Check(provider, "summary returned within storage bound", !String.IsNullOrWhiteSpace(summary) && summary.Length <= 6000);
            Review(provider, "fictional event summary", "Preserve original roleplay scope, cancellation, unexecuted gift, interrupted unfinished reply and limited knowledge; do not turn the cut-off reply into a completed handover.", summary, settings);

            var fiction = new List<ChatMessage> {
                new ChatMessage("user", "我们只是在角色扮演：我扮演的角色已经搬到京都，还答应杏菜明天去海边。这不是我的现实经历或计划。"),
                new ChatMessage("assistant", "这是剧情设定。"),
                new ChatMessage("user", "昨晚梦里我开了一家甜品店；梦里的我最爱草莓蛋糕。这些是梦境内容，不是我真实的经历或喜好。")
            };
            requests++;
            string raw = await client.ExtractMemoriesAsync(settings, fiction, new string[0], CancellationToken.None);
            var root = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
            object rows;
            bool empty = root != null && root.Count == 1 && root.TryGetValue("memories", out rows) && rows is object[] && ((object[])rows).Length == 0;
            Check(provider, "roleplay and dream create no proposed personal memories", empty);
            PrintSafe("Fiction-only memory proposal: " + raw, settings);

            var realPreference = new List<ChatMessage> {
                new ChatMessage("user", "我喜欢喝无糖咖啡，推荐饮料时优先考虑这个口味。")
            };
            requests++;
            raw = await client.ExtractMemoriesAsync(settings, realPreference, new string[0], CancellationToken.None);
            List<SemanticMemory> candidates = SemanticMemory.Parse(raw, realPreference);
            bool supported = candidates.Exists(delegate(SemanticMemory item) {
                return item.Certainty == "explicit" && item.Text.Contains("咖啡") && item.Evidence.Length > 0;
            });
            Check(provider, "explicit real preference remains eligible for personal memory", supported);
            PrintSafe("Synthetic real-preference proposal (not saved): " + raw, settings);
        }

        private static void Check(string provider, string label, bool ok)
        {
            if (ok) passed++; else failed++;
            Console.WriteLine(provider + " " + (ok ? "PASS" : "FAIL") + " " + label);
        }
        private static void Review(string provider, string label, string expectation, string answer, GptSettings settings)
        {
            reviews++;
            Console.WriteLine(provider + " REVIEW " + label + " — " + expectation);
            PrintSafe(answer, settings);
        }
        private static void PrintSafe(string text, GptSettings settings)
        {
            string safe = (text ?? "").Replace(settings.ApiKey, "[redacted]");
            Console.WriteLine(safe.Length > 7000 ? safe.Substring(0, 7000) + " [truncated]" : safe);
        }
    }
}
