using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    // Offline integration tests: real chat orchestration/router, fake HTTP and QQ boundaries.
    // Compile all app sources with /main:AnnaDesktopPet.ContextMusicTests, excluding other *Tests.cs.
    internal static class ContextMusicTests
    {
        private static int assertions;
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        private sealed class Step
        {
            internal string Text;
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal Action<Dictionary<string, object>> Inspect;
            internal Func<CancellationToken, Task<HttpResponseMessage>> Respond;
            internal Step(string text, Action<Dictionary<string, object>> inspect = null) { Text = text; Inspect = inspect; }
        }

        private sealed class ModelScript
        {
            private readonly Queue<Step> steps;
            internal int Calls;
            internal GptClient Client;
            internal ModelScript(params Step[] expected)
            {
                steps = new Queue<Step>(expected);
                Client = new GptClient(async delegate(HttpRequestMessage request, CancellationToken cancellation)
                {
                    Check(steps.Count > 0, "No unexpected model request or retry");
                    Calls++;
#if DEEPSEEK
                    Check(request.RequestUri.AbsoluteUri == "https://api.deepseek.com/chat/completions", "DeepSeek endpoint remains provider-specific");
#else
                    Check(request.RequestUri.AbsoluteUri == "https://api.openai.com/v1/responses", "GPT endpoint remains provider-specific");
#endif
                    Step step = steps.Dequeue();
                    var body = Object(await request.Content.ReadAsStringAsync());
                    if (step.Inspect != null) step.Inspect(body);
                    if (step.Respond != null) return await step.Respond(cancellation);
                    return Response(step.Text, step.Status);
                });
            }
            internal void Complete() { Check(steps.Count == 0, "All expected model stages ran"); }
        }

        private sealed class FakeMusic : IDisposable
        {
            internal int Searches, Dispatches;
            internal string LastQuery, LastId;
            internal MusicRouter Router;
            internal Func<string, CancellationToken, Task<List<QqTrack>>> SearchOverride;
            internal Action AfterDispatch;
            internal FakeMusic()
            {
                Router = new MusicRouter(delegate { }, null, new string[0],
                    delegate(string query, CancellationToken token)
                    {
                        Searches++; LastQuery = query;
                        if (SearchOverride != null) return SearchOverride(query, token);
                        return Task.FromResult(new List<QqTrack> {
                            new QqTrack { Id = "original", Title = query, Artist = "YOASOBI", Album = "Original" },
                            new QqTrack { Id = "cover", Title = query, Artist = "Cover Singer", Album = "Cover" }
                        });
                    },
                    delegate(QqTrack track, CancellationToken token)
                    {
                        token.ThrowIfCancellationRequested(); Dispatches++; LastId = track.Id;
                        if (AfterDispatch != null) AfterDispatch();
                        return Task.FromResult(0);
                    });
            }
            public void Dispose() { Router.Dispose(); }
        }

        [STAThread]
        public static int Main()
        {
            Program.Smoke = true;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                Pump(Run());
#if DEEPSEEK
                Console.WriteLine("PASS DeepSeek: " + assertions + " context/music integration assertions. Fake HTTP and QQ only.");
#else
                Console.WriteLine("PASS GPT: " + assertions + " context/music integration assertions. Fake HTTP and QQ only.");
#endif
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        }

        private static void Pump(Task task)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Offline regression test timed out");
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
                timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }

        private static async Task Run()
        {
            await RecommendationAndCandidateFlow();
            await ChatAndClarification();
            await MalformedIntents();
            await CancellationBoundaries();
            await DispatchReceiptSurvivesCancellation();
            await NarrationFailureKeepsOutcome();
            await BilingualNarration();
            await StaleSelection();
            await ActualChatSendPath();
            await PretenderFollowUpChatPath();
            await ClarificationFollowUpChatPath();
            await RepairsOnlyInvalidIntentOnce();
        }

        private static GptSettings Settings() { return new GptSettings { ApiKey = "offline-test-not-a-real-key" }; }
        private static List<ChatMessage> Recommendation()
        {
            return new List<ChatMessage> {
                new ChatMessage("user", "推荐一首日语歌给我吧"),
                new ChatMessage("assistant", "那就《夜に駆ける》吧！节奏很带感。")
            };
        }

        private static async Task RecommendationAndCandidateFlow()
        {
            using (var music = new FakeMusic())
            {
                var history = Recommendation();
                var script = new ModelScript(
                    new Step(Intent("play", "夜に駆ける"), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        Check((string)packet["user_text"] == "可以，那你开始播放吧", "Latest confirmation is separate from history");
                        Check(Serializer.Serialize(packet["history"]).Contains("夜に駆ける"), "Resolver sees actual prior recommendation");
                        Check(((object[])packet["candidates"]).Length == 0, "No invented initial candidates");
                    }),
                    new Step("找到两个版本，你想听哪一个？", delegate(Dictionary<string, object> body)
                    {
                        string result = ToolPacket(body);
                        Check(result.Contains("YOASOBI") && result.Contains("Cover Singer"), "Narrator receives real candidates");
                    }),
                    new Step(Intent("choose", "", 2), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        Check(((object[])packet["candidates"]).Length == 2, "Resolver sees current candidate state");
                        Check(Serializer.Serialize(packet["history"]).Contains("local_assistant"), "Music results are kept in context");
                    }),
                    new Step("点播请求已送出，还没确认客户端开始播放。", delegate(Dictionary<string, object> body)
                    {
                        Check(ToolPacket(body).Contains("Cover Singer"), "Narration grounded in selected track receipt");
                    }));
                var flow = new MusicConversation(script.Client, music.Router);
                GptReply search = await flow.SendAsync(Settings(), history, "可以，那你开始播放吧", false, CancellationToken.None, null);
                Check(music.Searches == 1 && music.LastQuery == "夜に駆ける" && music.Dispatches == 0, "Confirmation searches contextual song, does not auto-pick a version");
                Check(search.ToolResultText.Contains("1.") && search.ToolResultText.Contains("2."), "Authoritative candidates remain visible");
                history.Add(new ChatMessage("user", "可以，那你开始播放吧"));
                history.Add(new ChatMessage("local_assistant", search.ToolResultText));
                history.Add(new ChatMessage("assistant", search.DisplayText));
                GptReply chosen = await flow.SendAsync(Settings(), history, "第二个版本", false, CancellationToken.None, null);
                Check(music.Searches == 1 && music.Dispatches == 1 && music.LastId == "cover", "Contextual choice dispatches actual second candidate once");
                Check(chosen.ToolResultText.Contains("发送播放请求") && !chosen.ToolResultText.Contains("正在播放"), "Unverified dispatch is not labelled confirmed playback");
                Check(music.Router.CaptureCandidates().Candidates.Count == 0, "Selected candidates are consumed");
                script.Complete();
            }
        }

        private static async Task ChatAndClarification()
        {
            using (var music = new FakeMusic())
            {
                var script = new ModelScript(new Step(Intent("chat")), new Step("可以聊聊旋律。"),
                    new Step(Intent("chat")), new Step("好，我们只聊这首歌。"),
                    new Step(Intent("clarify", "", 0, "", "刚才提到了两首，想听哪一首？")));
                var flow = new MusicConversation(script.Client, music.Router);
                GptReply chat = await flow.SendAsync(Settings(), Recommendation(), "你喜欢这首吗", false, CancellationToken.None, null);
                Check(chat.DisplayText.Contains("旋律") && String.IsNullOrEmpty(chat.ToolResultText), "Discussion stays normal chat");
                await flow.SendAsync(Settings(), Recommendation(), "不要播放，我只想了解它", false, CancellationToken.None, null);
                GptReply question = await flow.SendAsync(Settings(), Recommendation(), "放刚才那首", false, CancellationToken.None, null);
                Check(question.DisplayText.Contains("哪一首"), "Uncertain intent asks instead of guessing");
                Check(music.Searches == 0 && music.Dispatches == 0, "Chat, negation and ambiguity have no music side effects");
                script.Complete();
            }
        }

        private static async Task MalformedIntents()
        {
            string[] invalid = {
                "我现在就播放。", "{", "{}", "[]",
                "{\"intent\":\"play\",\"query\":\"Lemon\",\"choice\":0,\"command\":\"\",\"question\":\"\",\"extra\":true}",
                Intent("execute", "Lemon"), Intent("control", "", 0, "shell"),
                Intent("play", "Lemon", 1),
                "{\"intent\":\"choose\",\"query\":\"\",\"choice\":\"2\",\"command\":\"\",\"question\":\"\"}"
            };
            using (var music = new FakeMusic())
            {
                foreach (string raw in invalid)
                {
                    var script = new ModelScript(new Step(raw), new Step(raw));
                    var flow = new MusicConversation(script.Client, music.Router);
                    await Throws(delegate { return flow.SendAsync(Settings(), Recommendation(), "你来放", false, CancellationToken.None, null); }, "Malformed intent must fail closed");
                    Check(music.Searches == 0 && music.Dispatches == 0, "Invalid model text cannot execute music");
                    Check(script.Calls == 2, "Exactly one format repair, then fail closed");
                    script.Complete();
                }
                var missing = new ModelScript(new Step(Intent("play")), new Step(Intent("choose", "", 2)));
                var missingFlow = new MusicConversation(missing.Client, music.Router);
                GptReply noTitle = await missingFlow.SendAsync(Settings(), null, "帮我放", false, CancellationToken.None, null);
                GptReply noList = await missingFlow.SendAsync(Settings(), null, "第二首", false, CancellationToken.None, null);
                Check(!String.IsNullOrEmpty(noTitle.DisplayText) && !String.IsNullOrEmpty(noList.DisplayText), "Missing title/list asks for clarification");
                Check(music.Searches == 0 && music.Dispatches == 0, "Missing reference never guesses a song");
                missing.Complete();
            }
        }

        private static async Task CancellationBoundaries()
        {
            using (var music = new FakeMusic())
            using (var cancellation = new CancellationTokenSource())
            {
                Step wait = new Step("") { Respond = delegate(CancellationToken token)
                {
                    var completion = new TaskCompletionSource<HttpResponseMessage>();
                    token.Register(delegate { completion.TrySetCanceled(); });
                    cancellation.Cancel(); return completion.Task;
                }};
                var script = new ModelScript(wait);
                await Canceled(delegate { return new MusicConversation(script.Client, music.Router).SendAsync(Settings(), Recommendation(), "开始播放吧", false, cancellation.Token, null); });
                Check(music.Searches == 0 && music.Dispatches == 0, "Cancel during resolution prevents all QQ calls");
            }
            using (var music = new FakeMusic())
            using (var cancellation = new CancellationTokenSource())
            {
                music.SearchOverride = delegate(string query, CancellationToken token)
                {
                    var completion = new TaskCompletionSource<List<QqTrack>>();
                    token.Register(delegate { completion.TrySetCanceled(); });
                    cancellation.Cancel(); return completion.Task;
                };
                var script = new ModelScript(new Step(Intent("play", "Lemon")));
                await Canceled(delegate { return new MusicConversation(script.Client, music.Router).SendAsync(Settings(), Recommendation(), "播放Lemon", false, cancellation.Token, null); });
                Check(music.Dispatches == 0 && music.Router.CaptureCandidates().Candidates.Count == 0, "Cancel search leaves no playable stale candidates");
            }
        }

        private static async Task DispatchReceiptSurvivesCancellation()
        {
            foreach (bool directMatch in new[] { false, true })
            using (var music = new FakeMusic())
            using (var cancellation = new CancellationTokenSource())
            {
                if (directMatch)
                    music.SearchOverride = delegate(string query, CancellationToken token) {
                        return Task.FromResult(new List<QqTrack> { new QqTrack { Id = "original", Title = "Lemon", Artist = "米津玄師", Album = "Original" } });
                    };
                else await music.Router.Handle("播放 Lemon");
                // QQ has accepted the request, but cancellation wins before status confirmation.
                music.AfterDispatch = delegate { cancellation.Cancel(); };
                var receipts = new List<string>();
                var script = new ModelScript(new Step(directMatch ? Intent("play", "米津玄師 Lemon") : Intent("choose", "", 1)));
                await Canceled(delegate { return new MusicConversation(script.Client, music.Router).SendAsync(
                    Settings(), null, "就放这首", false, cancellation.Token, null, null, receipts.Add); });
                Check(receipts.Count == 1 && receipts[0].Contains("Lemon") && receipts[0].Contains("尚未确认"),
                    "Accepted QQ dispatch is recorded before cancellation, for " + (directMatch ? "direct match" : "candidate choice"));
                Check(!receipts[0].Contains("正在播放") && music.Dispatches == 1 && script.Calls == 1,
                    "Canceled confirmation neither claims playback nor repeats dispatch/narration");
                script.Complete();
            }
        }

        private static async Task NarrationFailureKeepsOutcome()
        {
            using (var music = new FakeMusic())
            {
                await music.Router.Handle("播放 Lemon");
                var script = new ModelScript(new Step(Intent("choose", "", 1)),
                    new Step("backend unavailable") { Status = HttpStatusCode.InternalServerError });
                GptReply result = await new MusicConversation(script.Client, music.Router).SendAsync(Settings(), null, "第一个", false, CancellationToken.None, null);
                Check(music.Dispatches == 1 && script.Calls == 2, "Narration HTTP failure does not retry completed action");
                Check(result.ToolResultText.Contains("发送播放请求") && !String.IsNullOrEmpty(result.SpeechNotice), "Receipt survives narration failure");
                script.Complete();
            }
            using (var music = new FakeMusic())
            using (var cancellation = new CancellationTokenSource())
            {
                await music.Router.Handle("播放 Lemon");
                var stop = new Step("") { Respond = delegate(CancellationToken token)
                {
                    cancellation.Cancel();
                    var completion = new TaskCompletionSource<HttpResponseMessage>(); completion.SetCanceled(); return completion.Task;
                }};
                var script = new ModelScript(new Step(Intent("choose", "", 1)), stop);
                GptReply result = await new MusicConversation(script.Client, music.Router).SendAsync(Settings(), null, "第一个", false, cancellation.Token, null);
                Check(music.Dispatches == 1 && !String.IsNullOrEmpty(result.ToolResultText), "Cancel narration preserves already dispatched result");
            }
        }

        private static async Task StaleSelection()
        {
            using (var music = new FakeMusic())
            {
                await music.Router.Handle("播放 Lemon");
                MusicCandidateSnapshot old = music.Router.CaptureCandidates();
                await music.Router.Handle("播放 First Love");
                string result = await music.Router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 1 }, old, CancellationToken.None);
                Check(music.Dispatches == 0 && !String.IsNullOrEmpty(result), "Old list selection cannot play a new list song");
                MusicCandidateSnapshot current = music.Router.CaptureCandidates();
                music.Router.CancelPending();
                await music.Router.ExecuteIntentAsync(new MusicIntent { Intent = "choose", Choice = 1 }, current, CancellationToken.None);
                Check(music.Dispatches == 0, "Cleared/new conversation list cannot dispatch");
            }
        }

        private static async Task BilingualNarration()
        {
            using (var music = new FakeMusic())
            {
                const string japanese = "二つ見つかったよ。どちらを聴く？";
                var script = new ModelScript(new Step(Intent("play", "夜に駆ける")),
                    new Step(Serializer.Serialize(new { display_text = "找到了两个版本，想听哪一个？", speech_ja = japanese }),
                        delegate(Dictionary<string, object> body)
                        {
                            Check(ToolPacket(body).Contains("YOASOBI"), "Bilingual narrator receives same authoritative result");
#if !DEEPSEEK
                            var format = (Dictionary<string, object>)((Dictionary<string, object>)body["text"])["format"];
                            Check((string)format["name"] == "anna_bilingual_reply", "Narration preserves bilingual response schema");
#endif
                        }));
                GptReply result = await new MusicConversation(script.Client, music.Router).SendAsync(Settings(), Recommendation(), "放吧", true, CancellationToken.None, null);
                Check(result.JapaneseSpeechText == japanese && result.DisplayText.Contains("两个版本"), "Japanese speech and Chinese text survive music orchestration");
                Check(result.ToolResultText.Contains("Cover Singer"), "Bilingual response keeps full candidate receipt");
                script.Complete();
            }
        }

        private static async Task ActualChatSendPath()
        {
            using (var music = new FakeMusic())
            {
                var history = Recommendation();
                var script = new ModelScript(new Step(Intent("play", "夜に駆ける")), new Step("选个版本吧。"));
                var chat = new GptChatWindow(null, history, Settings(), delegate { }, delegate { }, null, null, delegate { return false; },
                    music.Router.Handle, music.Router.CancelPending, contextualMusic: music.Router, modelClient: script.Client);
                try
                {
                    await chat.SubmitAsync("可以，那你开始播放吧");
                    Check(music.Searches == 1 && music.LastQuery == "夜に駆ける", "Actual UI submit invokes resolver and QQ search");
                    bool receipt = false;
                    foreach (ChatMessage item in history) if (item.Role == "local_assistant" && item.Content.Contains("YOASOBI")) receipt = true;
                    Check(receipt, "Actual UI history records authoritative tool result for follow-up");
                    script.Complete();
                }
                finally { chat.Close(); }
            }
        }

        private static async Task PretenderFollowUpChatPath()
        {
            using (var music = new FakeMusic())
            {
                const string artist = "Official髭男dism";
                const string query = artist + " Pretender";
                var history = new List<ChatMessage> {
                    new ChatMessage("user", "推荐一首日语歌"),
                    new ChatMessage("assistant", "推荐 Official髭男dism 的《Pretender》，旋律很抓耳。你平时偏快歌还是抒情一点？")
                };
                music.SearchOverride = delegate(string text, CancellationToken token)
                {
                    Check(text == query, "Follow-up searches the recommended song and explicit artist");
                    Check(!text.Contains("给我听听"), "Follow-up wording never becomes the QQ search query");
                    return Task.FromResult(new List<QqTrack> {
                        new QqTrack { Id = "pretender-original", Title = "Pretender", Artist = artist, Album = "Traveler" },
                        new QqTrack { Id = "pretender-cover", Title = "Pretender", Artist = "Cover Artist", Album = "Covers" }
                    });
                };
                var script = new ModelScript(
                    new Step(Intent("play", query), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        Check((string)packet["user_text"] == "放给我听听", "Exact screenshot follow-up reaches model intact");
                        string prior = Serializer.Serialize(packet["history"]);
                        Check(prior.Contains("Pretender") && prior.Contains(artist) && prior.Contains("你平时偏快歌还是抒情一点"),
                            "Resolver gets recommendation plus trailing preference question");
                    }),
                    new Step("这首的点播请求送出啦，还需要等客户端确认播放。", delegate(Dictionary<string, object> body)
                    {
                        Check(ToolPacket(body).Contains("Pretender"), "Follow-up narration receives actual selected song result");
                    }));
                var chat = new GptChatWindow(null, history, Settings(), delegate { }, delegate { }, null, null, delegate { return false; },
                    music.Router.Handle, music.Router.CancelPending, contextualMusic: music.Router, modelClient: script.Client);
                try
                {
                    await chat.SubmitAsync("放给我听听");
                    Check(music.Searches == 1 && music.LastQuery == query, "Actual UI routes colloquial playback confirmation through context");
                    Check(music.Dispatches == 1 && music.LastId == "pretender-original", "Exact artist/title selects original rather than arbitrary cover");
                    bool receipt = false;
                    foreach (ChatMessage item in history)
                        if (item.Role == "local_assistant" && item.Content.Contains("Pretender") && item.Content.Contains(artist)) receipt = true;
                    Check(receipt, "UI keeps actual Pretender playback request receipt");
                    script.Complete();
                }
                finally { chat.Close(); }
            }
        }

        private static List<ChatMessage> AmbiguousRecommendation()
        {
            return new List<ChatMessage> {
                new ChatMessage("user", "推荐一首日语歌"),
                new ChatMessage("assistant", "推荐《夜に駆ける》吧，YOASOBI 的，节奏轻快，听一遍脑子里就甩不掉了。想更安静一点的话《打上花火》也不错——你今天是想要提神的还是想听慢歌？")
            };
        }

        private static async Task ClarificationFollowUpChatPath()
        {
            using (var music = new FakeMusic())
            {
                var history = AmbiguousRecommendation();
                var script = new ModelScript(
                    new Step(Intent("clarify", "", 0, "", "你想听《夜に駆ける》还是《打上花火》？")),
                    new Step(Intent("play", "YOASOBI 夜に駆ける"), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        Check((string)packet["user_text"] == "前一首吧", "Clarification answer reaches actual UI model stage intact");
                        string prior = Serializer.Serialize(packet["history"]);
                        Check(prior.Contains("你想听《夜に駆ける》还是《打上花火》"), "Clarification question is in next model request history");
                        Check(((object[])packet["candidates"]).Length == 0, "History recommendation choice is not a catalog candidate choice");
                    }),
                    new Step("找到版本了，选一个吧。"));
                var chat = new GptChatWindow(null, history, Settings(), delegate { }, delegate { }, null, null, delegate { return false; },
                    music.Router.Handle, music.Router.CancelPending, contextualMusic: music.Router, modelClient: script.Client);
                try
                {
                    await chat.SubmitAsync("放给我听听");
                    Check(music.Searches == 0 && music.Dispatches == 0, "Ambiguous first follow-up asks before music execution");
                    Check(history[history.Count - 1].Content.Contains("还是《打上花火》"), "Actual UI stores clarification for next turn");
                    await chat.SubmitAsync("前一首吧");
                    Check(music.Searches == 1 && music.LastQuery == "YOASOBI 夜に駆ける", "Answer to clarification resolves first recommended song");
                    script.Complete();
                }
                finally { chat.Close(); }
            }
        }

        private static async Task RepairsOnlyInvalidIntentOnce()
        {
            using (var music = new FakeMusic())
            {
                var history = AmbiguousRecommendation();
                history.Add(new ChatMessage("user", "放给我听听"));
                history.Add(new ChatMessage("assistant", "你想听《夜に駆ける》还是《打上花火》？"));
                var script = new ModelScript(
                    new Step(Intent("play", "YOASOBI 夜に駆ける", 1)),
                    new Step(Intent("play", "YOASOBI 夜に駆ける"), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        Check((bool)packet["format_retry"], "Repair is explicitly marked for model");
                        Check((string)packet["user_text"] == "前一首吧", "Repair preserves latest user input");
                        Check(Serializer.Serialize(packet["history"]).Contains("还是《打上花火》"), "Repair preserves clarification history");
                        Check(music.Searches == 0 && music.Dispatches == 0, "Invalid first intent had no side effects");
                    }),
                    new Step("找到两个版本了。"));
                GptReply result = await new MusicConversation(script.Client, music.Router).SendAsync(Settings(), history, "前一首吧", false, CancellationToken.None, null);
                Check(music.Searches == 1 && !String.IsNullOrEmpty(result.ToolResultText), "Repaired intent executes music only once");
                Check(script.Calls == 3, "Exactly one parse repair plus narration");
                script.Complete();
            }
            using (var music = new FakeMusic())
            {
                await music.Router.Handle("播放 Lemon");
                var script = new ModelScript(
                    new Step(Intent("choose", "Lemon", 2)),
                    new Step(Intent("choose", "", 2), delegate(Dictionary<string, object> body)
                    {
                        var packet = ResolverPacket(body);
                        var candidates = (object[])packet["candidates"];
                        Check((bool)packet["format_retry"] && candidates.Length == 2 && Convert.ToString(candidates[1]).Contains("Cover Singer"),
                            "Selection format repair preserves original candidate snapshot");
                        Check(music.Dispatches == 0, "Invalid candidate decision is not executed before repair");
                    }), new Step("点播请求已送出。"));
                await new MusicConversation(script.Client, music.Router).SendAsync(Settings(), Recommendation(), "第二个版本", false, CancellationToken.None, null);
                Check(music.Searches == 1 && music.Dispatches == 1 && music.LastId == "cover", "Repaired selection dispatches original second candidate without re-search");
                script.Complete();
            }
            using (var music = new FakeMusic())
            {
                var authFailure = new ModelScript(new Step("Unauthorized") { Status = HttpStatusCode.Unauthorized });
                await Throws(delegate { return new MusicConversation(authFailure.Client, music.Router).SendAsync(Settings(), Recommendation(), "放吧", false, CancellationToken.None, null); }, "Auth failures must not invoke parse repair");
                Check(authFailure.Calls == 1 && music.Searches == 0 && music.Dispatches == 0, "Authentication error returns without retry or music action");
                authFailure.Complete();
            }
            using (var music = new FakeMusic())
            using (var cancellation = new CancellationTokenSource())
            {
                var canceled = new ModelScript(new Step(Intent("play", "Lemon", 1)) { Respond = delegate(CancellationToken token)
                {
                    cancellation.Cancel();
                    return Task.FromResult(Response(Intent("play", "Lemon", 1), HttpStatusCode.OK));
                }});
                await Canceled(delegate { return new MusicConversation(canceled.Client, music.Router).SendAsync(Settings(), Recommendation(), "放吧", false, cancellation.Token, null); });
                Check(canceled.Calls == 1 && music.Searches == 0 && music.Dispatches == 0, "Canceled invalid response never issues repair request");
                canceled.Complete();
            }
        }

        private static Dictionary<string, object> ResolverPacket(Dictionary<string, object> body)
        {
#if DEEPSEEK
            Check((string)((Dictionary<string, object>)body["response_format"])["type"] == "json_object", "DeepSeek uses documented JSON mode");
            object[] entries = (object[])body["messages"];
#else
            var format = (Dictionary<string, object>)((Dictionary<string, object>)body["text"])["format"];
            Check((string)format["type"] == "json_schema" && (bool)format["strict"], "GPT uses strict intent schema");
            object[] entries = (object[])body["input"];
#endif
            return Object((string)((Dictionary<string, object>)entries[entries.Length - 1])["content"]);
        }

        private static string ToolPacket(Dictionary<string, object> body)
        {
#if DEEPSEEK
            object[] entries = (object[])body["messages"];
#else
            object[] entries = (object[])body["input"];
#endif
            return (string)Object((string)((Dictionary<string, object>)entries[entries.Length - 1])["content"])["tool_result"];
        }

        private static string Intent(string intent, string query = "", int choice = 0, string command = "", string question = "")
        {
            return Serializer.Serialize(new { intent = intent, query = query, choice = choice, command = command, question = question });
        }

        private static Dictionary<string, object> Object(string value) { return (Dictionary<string, object>)Serializer.DeserializeObject(value); }
        private static HttpResponseMessage Response(string text, HttpStatusCode status)
        {
#if DEEPSEEK
            string envelope = Serializer.Serialize(new { choices = new[] { new { message = new { content = text }, finish_reason = "stop" } } });
#else
            string envelope = Serializer.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = text } } } } });
#endif
            return new HttpResponseMessage(status) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
        private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
        private static async Task Throws(Func<Task<GptReply>> action, string name)
        {
            try { await action(); } catch (InvalidOperationException) { assertions++; return; }
            throw new Exception(name);
        }
        private static async Task Canceled(Func<Task<GptReply>> action)
        {
            try { await action(); } catch (OperationCanceledException) { assertions++; return; }
            throw new Exception("Expected operation cancellation");
        }
    }
}
