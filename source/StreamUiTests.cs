using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal static class StreamUiTests
    {
        private static int assertions;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        [STAThread]
        public static int Main(string[] args)
        {
            Program.Smoke = true;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                Check(typeof(GptChatWindow).GetMethod("PrepareVoiceInput", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                    "Voice recording must have an immediate interruption entry point: PrepareVoiceInput.");
                Pump(Run(args.Length == 0 ? "" : args[0]));
#if DEEPSEEK
                const string provider = "DeepSeek";
#else
                const string provider = "GPT";
#endif
                bool packagedVoice = args.Length > 0 && args[0] == "--packaged-voice";
                Console.WriteLine("PASS " + provider + ": " + assertions +
                    (packagedVoice ? " packaged voice checks with synthetic chat and real offline synthesis." : " stream UI contract assertions. Fake HTTP and speech only."));
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); return 1; }
        }

        private static async Task Run(string scenario)
        {
            if (scenario == "--packaged-voice") { await PackagedVoiceReadsChat(); return; }
            if (scenario == "--voice-gates") { VoiceLifecycleUsesPureSubmissionGate(); return; }
            if (scenario == "--rebuild") { await RebuildPreservesActiveStream(); return; }
            if (scenario == "--late-receipt") { await LateMusicReceiptBelongsToInterruptedTurn(); return; }
            if (scenario == "--music-stream-failure") { await FailedMusicNarrationStopsStreamingSpeech(); return; }
            DraftRemainsProtected();
            VoiceLifecycleUsesPureSubmissionGate();
            await PartialReplyAndSentenceBeforeCompletion();
            await RebuildPreservesActiveStream();
            await StopButtonInterruptsPendingStream();
            await InterruptWhileOldStreamStillPending(true);
            await InterruptWhileOldStreamStillPending(false);
            await LateMusicReceiptBelongsToInterruptedTurn();
            await FailedMusicNarrationStopsStreamingSpeech();
            await ClosingRejectsLateWork();
            await UnauthorizedRemainsRetryable();
        }

        private static async Task PackagedVoiceReadsChat()
        {
            var settings = new SpeechSettings();
            Check(settings.Engine == "gpt-sovits" && !settings.AutoReadReplies && File.Exists(settings.ReferenceAudio),
                "Fresh package presents the reference voice with its bundled sample and waits for setup");
            Check(PortableJapaneseVoice.Available, "Optional offline Japanese voice is installed");
            settings.Engine = "portable";
            settings.AutoReadReplies = true;
            string wave = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-ja-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                using (var speech = new SpeechService(Dispatcher.CurrentDispatcher, wave))
                {
                    speech.ConfigureBackend(settings.Engine, "", "", "");
                    speech.Configure(settings.VoiceId, settings.Rate, settings.Volume);
                    speech.Speak("こんにちは、アンナだよ。");
                    await WaitForPackagedWave(speech, wave);
                    File.Delete(wave);
                    using (var fixture = new Fixture(settings.AutoReadReplies, null, speech))
                    {
                        string reply = "{\"display_text\":\"今天聊点什么？\",\"speech_ja\":\"今日は何を話そうか？\"}";
                        var stream = fixture.Transport.AddStream();
                        Task send = fixture.Chat.SubmitAsync("你好，杏菜");
                        stream.Push(Delta(reply));
                        stream.Complete(Completed(reply));
                        await send;
                        Check(Visible(fixture.Chat).Contains("今天聊点什么？"), "Chat displayed its Chinese reply");
                        await WaitForPackagedWave(speech, wave);
                    }
                }
            }
            finally { if (File.Exists(wave)) File.Delete(wave); }
        }

        private static async Task WaitForPackagedWave(SpeechService speech, string wave)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (speech.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(25);
            Check(!speech.IsBusy && File.Exists(wave), "Offline voice produced a WAV: " + speech.Status);
            byte[] data = File.ReadAllBytes(wave);
            GptSovitsClient.ValidateWave(data);
            bool hasSignal = false;
            for (int i = 44; i + 1 < data.Length; i += 2)
                if (Math.Abs((int)BitConverter.ToInt16(data, i)) > 100) { hasSignal = true; break; }
            Check(data.Length > 48000 && hasSignal, "Offline speech WAV contains audible PCM");
        }

        private static void VoiceLifecycleUsesPureSubmissionGate()
        {
            // Never call VoiceChat's constructor: it installs a real global hook and warms ASR.
            // A hidden empty HWND lets the existing cleanup path run without a pet or microphone.
            using (var source = new HwndSource(new HwndSourceParameters("offline-voice-lifecycle-test") { Width = 0, Height = 0, WindowStyle = 0 }))
            {
                var voice = (VoiceChat)FormatterServices.GetUninitializedObject(typeof(VoiceChat));
                int prepareCalls = 0, submitChecks = 0, sent = 0, notices = 0;
                Func<bool> prepare = delegate { prepareCalls++; return false; };
                Func<bool> canSubmit = delegate { submitChecks++; return false; };
                SetVoiceField(voice, "source", source);
                SetVoiceField(voice, "timer", new DispatcherTimer());
                SetVoiceField(voice, "ready", prepare);
                SetVoiceField(voice, "send", new Action<string>(delegate { sent++; }));
                SetVoiceField(voice, "notice", new Action<string>(delegate { notices++; }));
                FieldInfo submissionField = typeof(VoiceChat).GetField("canSubmit", BindingFlags.Instance | BindingFlags.NonPublic);
                if (submissionField != null) submissionField.SetValue(voice, canSubmit);
                SetVoiceField(voice, "captured", true);
                InvokeVoice(voice, "Start");
                Check(prepareCalls == 0 && submitChecks == 1, "Deferred recording start must use pure submission readiness, never repeat the side-button interruption");
                Check(sent == 0 && GetVoiceField(voice, "localRecorder") == null, "A busy new turn prevents recording without initializing a microphone");

                SetVoiceField(voice, "ready", new Func<bool>(delegate { prepareCalls++; return true; }));
                SetVoiceField(voice, "text", new StringBuilder("识别完成时已有新请求"));
                InvokeVoice(voice, "Complete");
                Check(prepareCalls == 0 && submitChecks == 2 && sent == 0, "Recognized results cannot interrupt or submit over a newly busy turn");
                SetVoiceField(voice, "text", new StringBuilder());
                InvokeVoice(voice, "Complete");
                Check(prepareCalls == 0 && submitChecks == 2 && notices == 1, "Empty recognition reports no speech without calling either readiness gate");
                submissionField.SetValue(voice, new Func<bool>(delegate { submitChecks++; return true; }));
                SetVoiceField(voice, "text", new StringBuilder("准备就绪后的识别结果"));
                InvokeVoice(voice, "Complete");
                Check(prepareCalls == 0 && submitChecks == 3 && sent == 1, "A recognized result submits once through the pure gate without repeating interruption");

                // Invoke the hook handler with a synthetic side-button message, without ever
                // installing a hook. The queued Start is blocked by canSubmit before recording.
                submissionField.SetValue(voice, canSubmit);
                SetVoiceField(voice, "captured", false);
                Type mouseType = typeof(VoiceChat).GetNestedType("MouseData", BindingFlags.NonPublic);
                object mouse = Activator.CreateInstance(mouseType);
                mouseType.GetField("Buttons").SetValue(mouse, (uint)65536);
                IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(mouseType));
                try
                {
                    Marshal.StructureToPtr(mouse, pointer, false);
                    object handled = typeof(VoiceChat).GetMethod("MouseMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(voice, new object[] { 0, new IntPtr(0x20B), pointer });
                    Check((IntPtr)handled == new IntPtr(1) && prepareCalls == 1, "Initial side-button press prepares and interrupts exactly once");
                    source.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    Check(prepareCalls == 1 && submitChecks == 4 && GetVoiceField(voice, "localRecorder") == null, "Queued recording start uses pure readiness without repeating interruption or opening a microphone");
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
        }

        private static async Task RebuildPreservesActiveStream()
        {
            using (var fixture = new Fixture(false))
            {
                for (int i = 0; i < 82; i++) fixture.History.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", "旧消息-" + i));
                var stream = fixture.Transport.AddStream();
                Task send = fixture.Chat.SubmitAsync("翻看聊天时也继续显示这条回复");
                stream.Push(Delta("重建前的一部分"));
                await Until(delegate { return Visible(fixture.Chat).Contains("重建前的一部分"); }, "Reply is visible before viewing older history");
                Button earlier = FindButton(fixture.Chat, "查看更早的聊天（本机已保存）");
                Check(earlier != null, "Long history exposes the actual earlier-history button");
                earlier.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                stream.Push(Delta("，重建之后仍然可见"));
                await Until(delegate { return Visible(fixture.Chat).Contains("重建前的一部分，重建之后仍然可见"); }, "Rebuilding history must reattach the active partial bubble for future streamed text");
                Check(Visible(fixture.Chat).Contains("翻看聊天时也继续显示这条回复") && !send.IsCompleted, "Active user and response remain visible during history browsing");
                stream.Complete(Completed("重建前的一部分，重建之后仍然可见"));
                await send;
                Check(fixture.History.Count == 84 && fixture.History[83].Content == "重建前的一部分，重建之后仍然可见", "Rebuilding active UI does not duplicate persisted turns");
            }
        }

        private static async Task LateMusicReceiptBelongsToInterruptedTurn()
        {
            using (var fixture = new Fixture(false))
            {
                // Drive real submit/interruption state; inject only the private tool callback.
                // This isolates the UI race without dispatching music or using external services.
                var oldStream = fixture.Transport.AddStream();
                Task oldSend = fixture.Chat.SubmitAsync("请播放测试曲A");
                oldStream.Push(Delta("正在处理点播"));
                await Until(delegate { return Visible(fixture.Chat).Contains("正在处理点播"); }, "Old music-related turn has a visible partial");
                object oldTurn = typeof(GptChatWindow).GetField("activeReply", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Chat);
                Check(Prepare(fixture.Chat), "Old turn is interrupted before the music receipt arrives");
                ChatMessage originalUser = fixture.History[0];
                var newStream = fixture.Transport.AddStream();
                Task newSend = fixture.Chat.SubmitAsync("更正，先聊明天计划");
                newStream.Push(Delta("明天先去公园"));
                await Until(delegate { return Visible(fixture.Chat).Contains("明天先去公园"); }, "New turn is already streaming when the old receipt arrives");
                string statusBefore = Status(fixture.Chat);
                string speechBefore = fixture.Speech.Snapshot();
                const string dispatched = "已向 QQ 音乐发送点播请求：测试曲A；尚未确认开始播放。";
                RecordReceipt(fixture.Chat, oldTurn, dispatched);
                Check(fixture.History.Count == 3 && Object.ReferenceEquals(fixture.History[0], originalUser) && fixture.History[1].Role == "local_assistant" && fixture.History[1].Content == dispatched, "Late dispatched receipt must be inserted into its interrupted old turn, not discarded");
                Check(fixture.History[2].Content.Contains("回复已打断，未完成") && CountRole(fixture.History, "user") == 1, "Late receipt neither duplicates the old user nor replaces the incomplete reply");
                Check(!newSend.IsCompleted && !Input(fixture.Chat).IsEnabled && Status(fixture.Chat) == statusBefore && Visible(fixture.Chat).Contains("明天先去公园") && fixture.Speech.Snapshot() == speechBefore, "Late old receipt cannot alter the new turn's visible response, status, busy state, or speech");
                ConversationHistory restored = ConversationHistory.Load(fixture.Path);
                Check(restored.Count == 3 && restored[1].Content == dispatched, "Late real tool evidence is persisted in the old turn");
                const string confirmed = "QQ 音乐正在播放：测试曲A。";
                RecordReceipt(fixture.Chat, oldTurn, confirmed);
                RecordReceipt(fixture.Chat, oldTurn, confirmed);
                Check(fixture.History.Count == 3 && fixture.History[1].Content == confirmed, "Later confirmation updates the original receipt without duplicating entries");
                oldStream.Complete(Completed("旧轮完整文字不能回来"));
                await oldSend;
                newStream.Push(Delta("，晚上再看书"));
                await Until(delegate { return Visible(fixture.Chat).Contains("明天先去公园，晚上再看书"); }, "New stream continues visibly after old receipt insertion and old task cleanup");
                newStream.Complete(Completed("明天先去公园，晚上再看书"));
                await newSend;
                Check(fixture.History.Count == 5 && CountRole(fixture.History, "user") == 2 && fixture.History[3].Content == "更正，先聊明天计划" && fixture.History[4].Content == "明天先去公园，晚上再看书", "Late receipt remains ordered under the old user when the new turn completes");
                Button fresh = FindButton(fixture.Chat, "新对话");
                fresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RecordReceipt(fixture.Chat, oldTurn, "切换会话后到达的回执不应重新导入旧记录");
                Check(fixture.History.Count == 0 && ConversationHistory.Load(fixture.Path).Count == 0, "Receipt cannot reintroduce the old turn after a new conversation removes its original user");
            }
        }

        private static void RecordReceipt(GptChatWindow chat, object turn, string text)
        {
            typeof(GptChatWindow).GetMethod("RecordToolReceipt", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, new object[] { turn, text });
        }

        private static async Task FailedMusicNarrationStopsStreamingSpeech()
        {
            int searches = 0, dispatched = 0;
            var music = new MusicRouter(delegate { }, null, new string[0],
                delegate(string query, CancellationToken token)
                {
                    searches++;
                    return Task.FromResult(new List<QqTrack> { new QqTrack { Id = "10000", Title = "测试歌曲", Artist = "测试歌手", Album = "离线专辑", Type = 0 } });
                },
                delegate(QqTrack track, CancellationToken token) { dispatched++; return Task.FromResult(0); });
            try
            {
                using (var fixture = new Fixture(true, music))
                {
                    fixture.Transport.Add(JsonReply("{\"intent\":\"play\",\"query\":\"测试歌手 测试歌曲\",\"choice\":0,\"command\":\"\",\"question\":\"\"}"));
                    var narration = fixture.Transport.AddStream();
                    Task send = fixture.Chat.SubmitAsync("请播放测试歌手的测试歌曲");
                    narration.Push(Delta("{\"display_text\":\"点播请求已经发出。\",\"speech_ja\":\"リクエストを送ったよ。"));
                    await Until(delegate { return Visible(fixture.Chat).Contains("点播请求已经发出。") && fixture.Speech.Sentences.Count == 1; }, "Music narration displays partial text and starts one Japanese sentence before failure");
                    Check(dispatched == 1 && searches == 1 && fixture.Transport.Bodies.Count == 2 && !send.IsCompleted, "Fake music action completes once before its model narration stream finishes");
                    int stopsBefore = fixture.Speech.Stops;
                    int endsBefore = fixture.Speech.Ends;
                    narration.Complete(""); // Missing terminal event is a genuine truncated SSE failure.
                    await send;
                    Check(dispatched == 1 && searches == 1 && fixture.Transport.Bodies.Count == 2, "Narration failure never repeats the actual music action or model intent request");
                    Check(CountRole(fixture.History, "user") == 1 && HasReceipt(fixture.History, "测试歌曲"), "Real tool receipt remains in history when streamed narration fails");
                    Check(HasReceipt(ConversationHistory.Load(fixture.Path), "测试歌曲"), "Music receipt survives restart after narration failure");
                    Check(fixture.Speech.Stops > stopsBefore && fixture.Speech.Ends == endsBefore, "Failed music narration must stop queued speech instead of completing and draining the invalid stream");
                    Check(fixture.Speech.Finals.Count == 0 && fixture.Chat.CanAcceptVoice, "Failed narration does not replay a final voice reply and leaves the next turn ready");
                    foreach (ChatMessage item in fixture.History)
                        if (item.Role == "assistant") Check(item.Content.Contains("未完成"), "Any retained narration partial is explicitly incomplete rather than a completed answer");
                }
            }
            finally { music.Dispose(); }
        }

        private static bool HasReceipt(IEnumerable<ChatMessage> history, string title)
        {
            foreach (ChatMessage item in history)
                if (item.Role == "local_assistant" && item.Content.Contains(title) && item.Content.Contains("发送")) return true;
            return false;
        }

        private static HttpResponseMessage JsonReply(string text)
        {
#if DEEPSEEK
            string body = Json.Serialize(new { choices = new[] { new { message = new { content = text }, finish_reason = "stop" } } });
#else
            string body = Json.Serialize(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = text } } } } });
#endif
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private static void SetVoiceField(VoiceChat voice, string name, object value) { typeof(VoiceChat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(voice, value); }
        private static object GetVoiceField(VoiceChat voice, string name) { return typeof(VoiceChat).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(voice); }
        private static void InvokeVoice(VoiceChat voice, string name) { typeof(VoiceChat).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(voice, null); }

        private static void DraftRemainsProtected()
        {
            using (var fixture = new Fixture(false))
            {
                fixture.Chat.ReviewVoice("还没发送的草稿");
                Check(!fixture.Chat.CanAcceptVoice, "Unsent draft blocks actual voice submission");
                Check(!Prepare(fixture.Chat), "Preparing voice cannot overwrite an unsent draft");
                Check(Input(fixture.Chat).Text == "还没发送的草稿", "Unsent draft is preserved verbatim");
                Check(fixture.Transport.Bodies.Count == 0, "Draft protection never sends a model request");
            }
        }

        private static async Task PartialReplyAndSentenceBeforeCompletion()
        {
            using (var fixture = new Fixture(true))
            {
                var stream = fixture.Transport.AddStream();
                Task send = fixture.Chat.SubmitAsync("先说第一句，再继续");
                stream.Push(Delta("{\"display_text\":\"这是"));
                await Until(delegate { return Visible(fixture.Chat).Contains("这是"); }, "Display begins inside an unfinished JSON string before speech exists");
                Check(!send.IsCompleted && fixture.Speech.Sentences.Count == 0, "Chinese text appears without waiting for the Japanese field");
                stream.Push(Delta("第一句。\",\"speech_ja\":\"まず聞いてね。"));
                await Until(delegate { return Visible(fixture.Chat).Contains("这是第一句。"); }, "Partial text is visible before the server completes");
                await Until(delegate { return fixture.Speech.Sentences.Count > 0; }, "First Japanese sentence reaches speech before completion");
                Check(!send.IsCompleted, "Partial display and speech do not wait for full model completion");
                Check(CountRole(fixture.History, "assistant") == 0, "Visible partial is not committed as a completed assistant turn");
                Check(fixture.Speech.Sentences[0] == "まず聞いてね。", "Incremental speech passes a complete Japanese sentence");
                Check(fixture.Speech.Finals.Count == 0, "Partial streaming never invokes the full-reply speech fallback");
                stream.Push(Delta("続けて話すよ。\"}"));
                string whole = "{\"display_text\":\"这是第一句。\",\"speech_ja\":\"まず聞いてね。続けて話すよ。\"}";
                stream.Complete(Completed(whole));
                await send;
                Check(fixture.History.Count == 2 && fixture.History[0].Role == "user" && fixture.History[1].Role == "assistant", "Completed stream saves exactly one user and one assistant turn");
                Check(fixture.History[1].Content == "这是第一句。", "Completed history contains final display without transport JSON");
                Check(fixture.Speech.Sentences.Count == 2 && fixture.Speech.Sentences[1] == "続けて話すよ。", "Each streamed sentence is spoken once");
                Check(fixture.Speech.Starts == 1 && fixture.Speech.Ends == 1, "One streamed reply opens and closes one speech sequence");
                Check(fixture.Speech.Finals.Count == 0, "Final reply does not repeat speech already streamed");
                Check(fixture.Chat.CanAcceptVoice && Input(fixture.Chat).Text.Length == 0, "Completed turn restores ready state with sent input cleared");
                ConversationHistory restored = ConversationHistory.Load(fixture.Path);
                Check(restored.Count == 2 && restored[1].Content == "这是第一句。", "Restart restores the completed response exactly once");
            }
        }

        private static async Task StopButtonInterruptsPendingStream()
        {
            using (var fixture = new Fixture(false))
            {
                var stream = fixture.Transport.AddStream();
                Task send = fixture.Chat.SubmitAsync("这条用停止按钮打断");
                stream.Push(Delta("按钮停止前的部分文字"));
                await Until(delegate { return Visible(fixture.Chat).Contains("按钮停止前的部分文字"); }, "Plain text streams before stop");
                Button stop = FindButton(fixture.Chat, "停止");
                Check(stop != null, "Actual chat has a stop button");
                int stopsBefore = fixture.Speech.Stops;
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(fixture.Transport.Tokens[0].IsCancellationRequested && !send.IsCompleted, "Stop button cancels token without waiting for non-cooperative network");
                Check(fixture.Speech.Stops > stopsBefore && fixture.Chat.CanAcceptVoice, "Stop button halts speech and immediately restores submission readiness");
                Check(fixture.History.Count == 2 && fixture.History[1].Content.Contains("回复已打断，未完成") && fixture.History[1].Content.Contains("按钮停止前的部分文字"), "Stop button retains visible partial with explicit incomplete status");
                string historyBefore = Json.Serialize(fixture.History);
                stream.Push(Delta("后续不能添加"));
                stream.Complete(Completed("按钮停止前的部分文字后续不能添加"));
                await send;
                Check(Json.Serialize(fixture.History) == historyBefore && !Visible(fixture.Chat).Contains("后续不能添加"), "Stopped stream cannot append late content to UI or history");
            }
        }

        private static async Task InterruptWhileOldStreamStillPending(bool withPartial)
        {
            using (var fixture = new Fixture(true))
            {
                var oldStream = fixture.Transport.AddStream();
                const string original = "我刚才说的是周六去海边";
                Task oldSend = fixture.Chat.SubmitAsync(original);
                await Until(delegate { return fixture.Transport.Bodies.Count == 1; }, "First stream started");
                if (withPartial)
                {
                    oldStream.Push(Delta("{\"display_text\":\"那就周六\",\"speech_ja\":\"土曜日だね。"));
                    await Until(delegate { return Visible(fixture.Chat).Contains("那就周六") && fixture.Speech.Sentences.Count == 1; }, "Old partial is visible and queued for speech");
                }
                int stopsBefore = fixture.Speech.Stops;
                Check(Prepare(fixture.Chat), "Voice preparation immediately accepts interruption while model stream is pending");
                Check(fixture.Transport.Tokens[0].IsCancellationRequested, "Voice preparation cancels the old model token immediately");
                Check(fixture.Speech.Stops > stopsBefore, "Voice preparation immediately stops old speech");
                Check(fixture.Chat.CanAcceptVoice && Input(fixture.Chat).Text.Length == 0, "Sent input is cleared and a new voice turn is ready immediately");
                Check(!oldSend.IsCompleted, "Read deliberately ignores cancellation so new turn readiness does not depend on old task exit");
                Check(fixture.History.Count >= 1 && fixture.History[0].Role == "user" && fixture.History[0].Content == original, "Interrupted original user message remains available even before any reply text");
                if (withPartial)
                {
                    Check(fixture.History.Count == 2 && fixture.History[1].Role == "assistant", "Interrupted visible partial is retained once");
                    Check(fixture.History[1].Content.StartsWith("【回复已打断，未完成】", StringComparison.Ordinal) && fixture.History[1].Content.Contains("那就周六"), "Interrupted partial explicitly says it is incomplete");
                }
                else Check(CountRole(fixture.History, "assistant") == 0, "No-text interruption cannot fabricate an assistant response");
                ConversationHistory restored = ConversationHistory.Load(fixture.Path);
                Check(restored.Count == fixture.History.Count && restored[0].Content == original, "Interrupted evidence persists across restart");

                var newStream = fixture.Transport.AddStream();
                Task newSend = fixture.Chat.SubmitAsync("不对，我说的是周日");
                await Until(delegate { return fixture.Transport.Bodies.Count == 2; }, "Correction starts while old response is pending");
                string correctionPayload = fixture.Transport.Bodies[1];
                Check(correctionPayload.Contains(original) && correctionPayload.Contains("不对，我说的是周日"), "Correction request carries the interrupted original user context");
                if (withPartial) Check(correctionPayload.Contains("回复已打断，未完成"), "Future model context knows old partial was interrupted");
                newStream.Push(Delta("{\"display_text\":\"好的，周日\",\"speech_ja\":\"日曜日だね。"));
                await Until(delegate { return Visible(fixture.Chat).Contains("好的，周日") && fixture.Speech.Sentences.Contains("日曜日だね。"); }, "New response has its own active partial and speech");
                string historyBefore = Json.Serialize(fixture.History);
                string statusBefore = Status(fixture.Chat);
                int speechEndsBefore = fixture.Speech.Ends;
                int speechStartsBefore = fixture.Speech.Starts;
                int sentencesBefore = fixture.Speech.Sentences.Count;
                int stopsDuringNew = fixture.Speech.Stops;
                oldStream.Push(Delta("遅い古い音声だよ。\"}"));
                oldStream.Complete(Completed("{\"display_text\":\"旧回复最终内容不能覆盖\",\"speech_ja\":\"遅い古い音声だよ。\"}"));
                await oldSend;
                await Task.Delay(20);
                Check(!newSend.IsCompleted && !Input(fixture.Chat).IsEnabled && !fixture.Chat.CanAcceptVoice, "Old finally cannot clear the new turn's busy state");
                Check(Status(fixture.Chat) == statusBefore && Visible(fixture.Chat).Contains("好的，周日"), "Old callbacks cannot replace new status or visible response");
                Check(Json.Serialize(fixture.History) == historyBefore, "Late old completion cannot mutate history or duplicate interrupted evidence");
                Check(fixture.Speech.Ends == speechEndsBefore && fixture.Speech.Starts == speechStartsBefore && fixture.Speech.Stops == stopsDuringNew && fixture.Speech.Sentences.Count == sentencesBefore, "Old stream cannot stop, close, restart, or append to the new speech sequence");
                Check(!Visible(fixture.Chat).Contains("旧回复最终内容不能覆盖"), "Late old final content never appears in the UI");
                newStream.Push(Delta("\"}"));
                newStream.Complete(Completed("{\"display_text\":\"好的，周日\",\"speech_ja\":\"日曜日だね。\"}"));
                await newSend;
                Check(fixture.History[fixture.History.Count - 2].Content == "不对，我说的是周日" && fixture.History[fixture.History.Count - 1].Content == "好的，周日", "New correction finishes in chronological order");
                Check(fixture.Chat.CanAcceptVoice && fixture.Speech.Finals.Count == 0, "New turn finishes ready without duplicated final speech");
            }
        }

        private static async Task ClosingRejectsLateWork()
        {
            using (var fixture = new Fixture(true))
            {
                var stream = fixture.Transport.AddStream();
                Task send = fixture.Chat.SubmitAsync("关闭前的请求");
                stream.Push(Delta("{\"display_text\":\"关闭前部分\",\"speech_ja\":\"先に話すよ。"));
                await Until(delegate { return Visible(fixture.Chat).Contains("关闭前部分"); }, "Partial exists before closing");
                fixture.Chat.Close();
                Check(fixture.Transport.Tokens[0].IsCancellationRequested && !fixture.Chat.CanAcceptVoice, "Closing cancels model and prevents new voice submissions");
                string historyBefore = Json.Serialize(fixture.History);
                string statusBefore = Status(fixture.Chat);
                string speechBefore = fixture.Speech.Snapshot();
                stream.Push(Delta("閉じた後の音声だよ。\"}"));
                stream.Complete(Completed("{\"display_text\":\"关闭后的最终文本\",\"speech_ja\":\"閉じた後の音声だよ。\"}"));
                await send;
                await Task.Delay(20);
                Check(Json.Serialize(fixture.History) == historyBefore, "Late completion after closing cannot change persisted conversation");
                Check(Status(fixture.Chat) == statusBefore && fixture.Speech.Snapshot() == speechBefore, "Closing suppresses later status and speech callbacks");
            }
        }

        private static async Task UnauthorizedRemainsRetryable()
        {
            using (var fixture = new Fixture(false))
            {
                fixture.Transport.Add(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"offline-denied\"}}", Encoding.UTF8, "application/json") });
                await fixture.Chat.SubmitAsync("保留这条失败输入供重试");
                Check(fixture.History.Count == 0 && !File.Exists(fixture.Path), "Ordinary 401 does not create a successful or interrupted turn");
                Check(Input(fixture.Chat).Text == "保留这条失败输入供重试" && Input(fixture.Chat).IsEnabled, "401 retains editable input for retry");
                Check(!Prepare(fixture.Chat), "Retryable failed input is protected as an unsent draft");
                Check(Status(fixture.Chat).Contains("失败"), "401 retains an explicit failure status");
                var stream = fixture.Transport.AddStream();
                Task retry = fixture.Chat.SubmitAsync(Input(fixture.Chat).Text);
                stream.Push(Delta("这次回复成功。"));
                stream.Complete(Completed("这次回复成功。"));
                await retry;
                Check(fixture.Transport.Bodies.Count == 2 && fixture.History.Count == 2 && fixture.History[1].Content == "这次回复成功。", "Retry succeeds once without duplicate failed history");
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Script Transport = new Script();
            internal readonly SpeechSpy Speech = new SpeechSpy();
            internal readonly ConversationHistory History;
            internal readonly GptChatWindow Chat;
            internal readonly string Path;
            private readonly string directory;
            internal Fixture(bool japanese, MusicRouter contextualMusic = null, SpeechService realSpeech = null)
            {
                string root = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "synthetic-stream-ui-" + Guid.NewGuid().ToString("N")));
                Check(directory.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Synthetic persistence stays within test output");
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, "chat-test.bin");
                History = ConversationHistory.Load(Path);
                // Reflection keeps the initial red test compilable before the new optional API exists.
                ConstructorInfo constructor = typeof(GptChatWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
                ParameterInfo[] parameters = constructor.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = null; args[1] = History; args[2] = new GptSettings { ApiKey = "offline-stream-ui-test-no-real-key" };
                args[3] = new Action<string, double>(delegate { }); args[4] = new Action<bool>(delegate { });
                args[5] = new Action<string>(delegate(string text) { Speech.Finals.Add(text); if (realSpeech != null) realSpeech.Speak(text); });
                args[6] = new Action(delegate { Speech.Stops++; if (realSpeech != null) realSpeech.Stop(); }); args[7] = new Func<bool>(delegate { return japanese; });
                for (int i = 8; i < parameters.Length; i++)
                {
                    if (parameters[i].Name == "modelClient") args[i] = Transport.Client;
                    else if (parameters[i].Name == "contextualMusic") args[i] = contextualMusic;
                    else if (parameters[i].Name == "beginStreamSpeech") args[i] = new Action(delegate { Speech.Starts++; if (realSpeech != null) realSpeech.BeginStream(); });
                    else if (parameters[i].Name == "appendStreamSpeech") args[i] = new Action<string>(delegate(string text) { Speech.Sentences.Add(text); if (realSpeech != null) realSpeech.AppendStream(text); });
                    else if (parameters[i].Name == "endStreamSpeech") args[i] = new Action(delegate { Speech.Ends++; if (realSpeech != null) realSpeech.CompleteStream(); });
                    else args[i] = null;
                }
                Chat = (GptChatWindow)constructor.Invoke(args);
            }
            public void Dispose()
            {
                Chat.Close();
                Transport.ReleaseAll();
                History.DeleteAll();
                string archive = History.ArchiveDirectory;
                if (Directory.Exists(archive) && Directory.GetFileSystemEntries(archive).Length == 0) Directory.Delete(archive);
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
            }
        }

        private sealed class SpeechSpy
        {
            internal int Starts, Ends, Stops;
            internal readonly List<string> Sentences = new List<string>();
            internal readonly List<string> Finals = new List<string>();
            internal string Snapshot() { return Starts + ":" + Ends + ":" + Stops + ":" + String.Join("|", Sentences) + ":" + String.Join("|", Finals); }
        }

        private sealed class Script
        {
            private readonly Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>();
            private readonly List<GateStream> streams = new List<GateStream>();
            internal readonly List<string> Bodies = new List<string>();
            internal readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            internal readonly GptClient Client;
            internal Script()
            {
                Client = new GptClient(async delegate(HttpRequestMessage request, CancellationToken token)
                {
                    Bodies.Add(await request.Content.ReadAsStringAsync()); Tokens.Add(token);
                    if (responses.Count == 0) throw new Exception("Unexpected model request in isolated stream UI test");
                    return responses.Dequeue();
                });
            }
            internal void Add(HttpResponseMessage response) { responses.Enqueue(response); }
            internal GateStream AddStream()
            {
                var stream = new GateStream(); streams.Add(stream);
                var content = new StreamContent(stream);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                Add(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
                return stream;
            }
            internal void ReleaseAll() { foreach (GateStream stream in streams) stream.Complete(""); }
        }

        // Deliberately non-cooperative network: ReadAsync ignores cancellation/Dispose until the
        // test releases it, proving that UI interruption doesn't await old transport cleanup.
        private sealed class GateStream : Stream
        {
            private readonly Queue<byte[]> chunks = new Queue<byte[]>();
            private readonly object sync = new object();
            private TaskCompletionSource<bool> available = new TaskCompletionSource<bool>();
            private byte[] current;
            private int offset;
            private bool complete;
            internal void Push(string text)
            {
                TaskCompletionSource<bool> wake;
                lock (sync) { if (text.Length > 0) chunks.Enqueue(Encoding.UTF8.GetBytes(text)); wake = available; available = new TaskCompletionSource<bool>(); }
                wake.TrySetResult(true);
            }
            internal void Complete(string text)
            {
                Push(text);
                TaskCompletionSource<bool> wake;
                lock (sync) { complete = true; wake = available; }
                wake.TrySetResult(true);
            }
            public override async Task<int> ReadAsync(byte[] buffer, int bufferOffset, int count, CancellationToken token)
            {
                while (true)
                {
                    Task wait;
                    lock (sync)
                    {
                        if (current == null && chunks.Count > 0) { current = chunks.Dequeue(); offset = 0; }
                        if (current != null)
                        {
                            int length = Math.Min(count, current.Length - offset);
                            Array.Copy(current, offset, buffer, bufferOffset, length); offset += length;
                            if (offset == current.Length) current = null;
                            return length;
                        }
                        if (complete) return 0;
                        wait = available.Task;
                    }
                    await wait.ConfigureAwait(false);
                }
            }
            protected override void Dispose(bool disposing) { }
            public override int Read(byte[] buffer, int offset, int count) { return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult(); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        private static string Delta(string text)
        {
#if DEEPSEEK
            return "data: " + Json.Serialize(new { choices = new[] { new { delta = new { content = text }, finish_reason = (string)null } } }) + "\n\n";
#else
            return "event: response.output_text.delta\ndata: " + Json.Serialize(new { type = "response.output_text.delta", delta = text, output_index = 0, content_index = 0 }) + "\n\n";
#endif
        }
        private static string Completed(string text)
        {
#if DEEPSEEK
            return "data: " + Json.Serialize(new { choices = new[] { new { delta = new { }, finish_reason = "stop" } } }) + "\n\ndata: [DONE]\n\n";
#else
            return "event: response.completed\ndata: " + Json.Serialize(new { type = "response.completed", response = new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = text } } } } } }) + "\n\n";
#endif
        }
        private static bool Prepare(GptChatWindow chat) { return (bool)typeof(GptChatWindow).GetMethod("PrepareVoiceInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chat, null); }
        private static TextBox Input(GptChatWindow chat) { return (TextBox)typeof(GptChatWindow).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chat); }
        private static string Status(GptChatWindow chat) { return ((TextBlock)typeof(GptChatWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chat)).Text; }
        private static string Visible(DependencyObject node)
        {
            var text = node as TextBox;
            if (text != null && text.IsReadOnly) return text.Text + "\n";
            var result = new StringBuilder();
            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                var dependency = child as DependencyObject;
                if (dependency != null) result.Append(Visible(dependency));
            }
            return result.ToString();
        }
        private static Button FindButton(DependencyObject node, string label)
        {
            var button = node as Button;
            if (button != null && Convert.ToString(button.Content) == label) return button;
            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                var dependency = child as DependencyObject;
                if (dependency != null) { Button found = FindButton(dependency, label); if (found != null) return found; }
            }
            return null;
        }
        private static int CountRole(IEnumerable<ChatMessage> history, string role) { int count = 0; foreach (ChatMessage item in history) if (item.Role == role) count++; return count; }
        private static async Task Until(Func<bool> condition, string message)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(4);
            while (!condition()) { if (DateTime.UtcNow > until) throw new Exception(message + " (timed out)"); await Task.Delay(5); }
            assertions++;
        }
        private static void Pump(Task task)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(40);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > until) throw new Exception("Offline stream UI tests timed out");
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
                timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
    }
}
