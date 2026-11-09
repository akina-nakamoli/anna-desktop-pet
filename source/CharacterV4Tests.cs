using System;
using System.Collections;
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
    // Offline request/response contracts, not a claim about real model characterization quality.
    internal static class CharacterV4Tests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static int assertions;
        private static readonly string[] Modules = { "V4.0", "人物缺点", "自主决策", "信息隔离", "场景状态", "事件账本", "冲突修正" };

        [STAThread]
        public static int Main()
        {
            Program.Smoke = true; // Never read or write the person's real memories or settings.
            try
            {
                Run().GetAwaiter().GetResult();
#if DEEPSEEK
                const string provider = "DeepSeek";
#else
                const string provider = "GPT";
#endif
                Console.WriteLine("PASS " + provider + ": " + assertions + " character V4 request/response assertions. Fake HTTP only.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        }

        private static async Task Run()
        {
            foreach (bool japanese in new[] { false, true })
                foreach (bool streaming in new[] { false, true })
                {
                    await ConversationContract(japanese, streaming);
                    await MusicNarrationContract(japanese, streaming);
                }
            await SummaryContract();
            await MemoryContract();
            await MemoryBatchSourceContract();
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static GptSettings Settings() { return new GptSettings { ApiKey = "offline-character-test-no-real-key" }; }
        private static Dictionary<string, object> Object(string json) { return (Dictionary<string, object>)Json.DeserializeObject(json); }
        private static string SystemText(Dictionary<string, object> body)
        {
#if DEEPSEEK
            return (string)((Dictionary<string, object>)((IList)body["messages"])[0])["content"];
#else
            return (string)body["instructions"];
#endif
        }
        private static IList Turns(Dictionary<string, object> body)
        {
#if DEEPSEEK
            var turns = new ArrayList((ICollection)body["messages"]);
            turns.RemoveAt(0);
            return turns;
#else
            return (IList)body["input"];
#endif
        }
        private static string TurnText(object turn) { return (string)((Dictionary<string, object>)turn)["content"]; }

        private static void CharacterRules(Dictionary<string, object> body, string path)
        {
            string rules = SystemText(body);
            foreach (string module in Modules)
                Check(rules.Contains(module), path + " outgoing instructions must include " + module);
            Check(rules.IndexOf("V4.0", StringComparison.Ordinal) == rules.LastIndexOf("V4.0", StringComparison.Ordinal), path + " adds the V4 core only once");
            Check(rules.Contains("八奈见杏菜"), path + " retains the established character identity");
            Check(rules.Contains("芝士"), path + " retains relevant established food lore");
            Check(rules.Contains("最新明确纠正"), path + " retains general continuity and correction precedence");
        }

        private static void OutputContract(Dictionary<string, object> body, bool japanese, bool streaming)
        {
            Check(body.ContainsKey("stream") ? (bool)body["stream"] == streaming : !streaming, "Requested streaming mode survives persona injection");
#if DEEPSEEK
            Check(body.ContainsKey("messages") && !body.ContainsKey("instructions"), "DeepSeek keeps its messages transport contract");
            if (japanese) Check(SystemText(body).Contains("display_text") && SystemText(body).Contains("speech_ja"), "DeepSeek retains bilingual JSON fields");
#else
            Check(body.ContainsKey("store") && !(bool)body["store"], "OpenAI storage remains disabled");
            if (japanese)
            {
                var format = (Dictionary<string, object>)((Dictionary<string, object>)body["text"])["format"];
                Check((string)format["type"] == "json_schema" && (bool)format["strict"], "Persona retains strict bilingual schema");
                var schema = (Dictionary<string, object>)format["schema"];
                var properties = (Dictionary<string, object>)schema["properties"];
                Check(properties.Count == 2 && properties.ContainsKey("display_text") && properties.ContainsKey("speech_ja"), "Persona adds no lore metadata to visible reply schema");
            }
#endif
        }

        private static List<ChatMessage> Evidence()
        {
            return new List<ChatMessage> {
                new ChatMessage("user", "来角色扮演吧：今天在文艺部约好明天一起吃面包。这是虚构场景，只有我们知道。"),
                new ChatMessage("assistant", "好，明天的约定先记着。"),
                new ChatMessage("user", "纠正一下，刚才的约定已经取消，还没去，别让温水知道。")
            };
        }

        private static async Task ConversationContract(bool japanese, bool streaming)
        {
            const string visible = "那就取消明天的约定吧。芝士面包嘛，下次再说。";
            const string speech = "明日の約束は取り消しね。また今度。";
            var script = new Script(japanese ? Json.Serialize(new { display_text = visible, speech_ja = speech }) : visible, streaming);
            var updates = new List<GptStreamUpdate>();
            GptReply reply = await script.Client.SendReplyAsync(Settings(), Evidence(), "你喜欢吃什么？刚才那件事先别告诉别人。", japanese, CancellationToken.None, streaming ? (Action<GptStreamUpdate>)updates.Add : null);
            Check(script.Calls == 1, "A V4 reply needs no separate persona HTTP request");
            CharacterRules(script.Body, "ordinary/bilingual chat");
            OutputContract(script.Body, japanese, streaming);
            string evidence = Json.Serialize(Turns(script.Body));
            Check(evidence.Contains("虚构场景") && evidence.Contains("约定已经取消") && evidence.Contains("还没去"), "Actual request retains roleplay boundaries, cancellation and latest correction evidence");
            Check(reply.DisplayText == visible, "Character scaffolding never replaces or prefixes the parsed reply");
            if (japanese) Check(reply.JapaneseSpeechText == speech, "Bilingual speech parses normally after V4 injection");
            else Check(String.IsNullOrEmpty(reply.JapaneseSpeechText), "Plain text mode stays plain");
            if (streaming)
            {
                Check(updates.Count > 0, "Streaming still emits updates");
                Check(updates.TrueForAll(x => !x.DisplayText.Contains("V4.0") && !x.DisplayText.Contains("display_text")), "Live subtitles contain neither persona headers nor raw JSON field names");
                Check(updates.Exists(x => x.DisplayText == visible), "Streaming eventually exposes the full visible reply");
            }
        }

        private static async Task MusicNarrationContract(bool japanese, bool streaming)
        {
            const string actual = "搜索找到候选，尚未播放：1. LOVE 2000。";
            const string visible = "找到啦，还没开始播放，你选一下。";
            const string speech = "見つけたよ。選んでね。";
            var script = new Script(japanese ? Json.Serialize(new { display_text = visible, speech_ja = speech }) : visible, streaming);
            var updates = new List<GptStreamUpdate>();
            var reply = await script.Client.SendMusicResultAsync(Settings(), Evidence(), "吃面包时想听这首歌，帮我播放。", actual, japanese, CancellationToken.None, streaming ? (Action<GptStreamUpdate>)updates.Add : null);
            CharacterRules(script.Body, "music result narration");
            OutputContract(script.Body, japanese, streaming);
            IList turns = Turns(script.Body);
            var receipt = Object(TurnText(turns[turns.Count - 1]));
            Check((string)receipt["tool_result"] == actual, "Actual tool receipt is preserved as data alongside character rules");
            Check(!SystemText(script.Body).Contains(actual), "Untrusted music receipt is never promoted into character instructions");
            Check(SystemText(script.Body).Contains("不得虚构成功"), "Music success must still be grounded in tool results");
            Check(reply.DisplayText == visible, "Music narration retains clean ordinary/bilingual response parsing");
        }

        private static void MaintenanceRules(Dictionary<string, object> body, string path)
        {
            string rules = SystemText(body);
            foreach (string boundary in new[] { "角色扮演", "已取消", "知情范围", "未完成" })
                Check(rules.Contains(boundary), path + " outgoing maintenance instructions must preserve " + boundary);
            Check(rules.Contains("created_at"), path + " retains source-time grounding");
            Check(!rules.Contains("人物缺点") && !rules.Contains("自主决策"), path + " uses archive rules instead of the full acting persona");
            Check(!body.ContainsKey("stream") || !(bool)body["stream"], path + " remains a structured background task");
        }

        private static async Task SummaryContract()
        {
            const string summary = "角色扮演中的面包约定已取消，未完成；知情范围仅用户与杏菜。";
            var script = new Script(Json.Serialize(new { summary = summary }), false);
            string result = await script.Client.SummarizeConversationAsync(Settings(), "原创场景：明天一起吃面包。", Evidence(), CancellationToken.None);
            MaintenanceRules(script.Body, "conversation summary");
            var packet = Object(TurnText(Turns(script.Body)[0]));
            Check(packet.ContainsKey("previous_summary") && packet.ContainsKey("older_messages"), "Summary keeps the established evidence packet");
            Check(Json.Serialize(packet["older_messages"]).Contains("约定已经取消"), "Summary receives cancellation evidence without rewriting it");
            Check(result == summary, "New persona rules preserve summary parsing");
        }

        private static async Task MemoryContract()
        {
            var script = new Script("{\"memories\":[]}", false);
            string result = await script.Client.ExtractMemoriesAsync(Settings(), Evidence(), new[] { "用户喜欢面包。" }, CancellationToken.None);
            MaintenanceRules(script.Body, "semantic memory");
            var packet = Object(TurnText(Turns(script.Body)[0]));
            Check(packet.ContainsKey("existing_records") && packet.ContainsKey("recorded_date"), "Memory keeps source and correction metadata");
            Check(Json.Serialize(packet["messages"]).Contains("虚构场景"), "Memory extractor receives explicit fiction boundary evidence");
            Check(SystemText(script.Body).Contains("replaces") && SystemText(script.Body).Contains("tentative"), "Memory retains replacement and uncertainty contracts");
            Check(((object[])Object(result)["memories"]).Length == 0, "No real user facts is a valid empty extraction");
        }

        private static async Task MemoryBatchSourceContract()
        {
            const string opening = "现在开始角色扮演，我演原创咖啡师，后面的第一人称不是真实自述。";
            var history = new ConversationHistory { Summary = "之前在演文艺部故事。", SummarizedCount = 1, MemoryProcessedCount = 12 };
            history.Add(new ChatMessage("user", opening) { CreatedUtc = new DateTime(2026, 11, 8, 4, 0, 0, DateTimeKind.Utc) });
            for (int i = 1; i < 12; i++) history.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", "场景对白" + i));
            history.Add(new ChatMessage("user", "我喜欢咖啡。"));
            var script = new Script("{\"memories\":[]}", false);
            string notice = await new ConversationMaintenance(script.Client).UpdateAsync(Settings(), history, CancellationToken.None);
            Check(script.Calls == 1 && notice.Length == 0, "Pending batch uses one successful memory request without an unrelated summary request");
            var packet = Object(TurnText(Turns(script.Body)[0]));
            Check(packet.ContainsKey("source_context"), "Actual maintenance caller supplies source context across the extraction-batch boundary");
            var context = Object((string)packet["source_context"]);
            IList evidence = (IList)packet["messages"];
            IList prior = (IList)context["prior_messages"];
            Check(evidence.Count == 1 && TurnText(evidence[0]) == "我喜欢咖啡。", "Only new batch messages may be candidate evidence");
            Check(prior.Count == 12 && TurnText(prior[0]) == opening, "Twelve-turn source context retains RP opening from the previous batch");
            Check((string)context["summary"] == "之前在演文艺部故事。", "Earlier summary remains available for source interpretation");
            var origin = (Dictionary<string, object>)prior[0];
            Check((string)origin["role"] == "user" && (string)origin["created_at"] == ConversationContext.Timestamp(history[0]), "Source context preserves speaker and original timestamp");
            Check(!Json.Serialize(prior).Contains("我喜欢咖啡"), "Current batch is not duplicated into earlier context");
            Check(SystemText(script.Body).Contains("source_context") && !SystemText(script.Body).Contains(opening), "Source context has an evidence-boundary instruction but stays untrusted input data");
            Check(history.MemoryProcessedCount == 13 && history.Count == 13, "Empty extraction advances the cursor without discarding the actual conversation");

            history.SummarizedCount = 13;
            history.Summary = "后来已经退出角色扮演，这个信息在旧批次之后。";
            context = Object(ConversationContext.MemorySourceContext(history, 12));
            Check(String.IsNullOrEmpty((string)context["summary"]), "A summary ahead of the pending batch cannot rewrite its earlier reality/fiction source");
            history.Add(new ChatMessage("assistant", "新的场景对白。"));
            history.Add(new ChatMessage("user", "这是尚未处理的新批次。"));
            context = Object(ConversationContext.MemorySourceContext(history, 14));
            prior = (IList)context["prior_messages"];
            Check(prior.Count == 12 && Convert.ToInt32(context["omitted_prior_messages"]) == 2, "Source history obeys its twelve-turn bound and reports omitted context");
            Check(!Json.Serialize(prior).Contains("尚未处理的新批次"), "Source helper never includes future messages past the evidence boundary");
        }

        private sealed class Script
        {
            internal readonly GptClient Client;
            internal Dictionary<string, object> Body;
            internal int Calls;
            internal Script(string answer, bool streaming)
            {
                Client = new GptClient(async delegate(HttpRequestMessage request, CancellationToken cancellation) {
                    Calls++;
                    Body = Object(await request.Content.ReadAsStringAsync());
                    if (!streaming) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json.Serialize(FinalBody(answer)), Encoding.UTF8, "application/json") };
                    int split = answer.Length / 2;
                    string events = Delta(answer.Substring(0, split)) + Delta(answer.Substring(split)) + End(answer);
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(events))) };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                    return response;
                });
            }
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
    }
}
