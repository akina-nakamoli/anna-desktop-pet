using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnnaDesktopPet
{
    internal sealed partial class GptChatWindow : Window
    {
        internal event Action UserMessageSubmitted;
        internal event Action<bool> ReplyMotionFinished;
        internal bool HasPendingModelTurn { get { return activeReply != null && OwnsTurn(activeReply); } }
        private readonly IList<ChatMessage> history;
        private readonly GptSettings settings;
        private readonly Action<string, double> preview;
        private readonly Action<bool> thinking;
        private readonly Action<string> assistantReply;
        private readonly Action stopSpeech;
        private readonly Func<bool> wantsJapaneseSpeech;
        private readonly Func<string, Task<string>> musicCommand;
        private readonly Action cancelMusic;
        private readonly GptClient modelClient;
        private readonly MusicConversation musicConversation;
        private StackPanel messages;
        private ScrollViewer conversation;
        private TextBox input;
        private TextBlock status;
        private Button send, stop, fresh;
        private ProgressBar progress;
        private CancellationTokenSource request;
        private CancellationTokenSource maintenanceRequest;
        private int visibleMessageLimit = 80;
        private bool busy, closing;
        private bool voiceInput;
        internal string CallAssistantText { get; private set; }
        internal string CallStatus { get { return String.IsNullOrEmpty(status.Text) ? connectionText.Text : status.Text; } }
        internal Task SubmitCallAsync(string text)
        {
            if (!CanAcceptVoice) throw new InvalidOperationException("聊天正在忙碌，或输入框里还有草稿。");
            voiceInput = true;
            return SubmitAsync(text);
        }
        internal bool CanAcceptVoice {get{return !busy && !closing && String.IsNullOrWhiteSpace(input.Text);}}
        internal void ReviewVoice(string text){if(!CanAcceptVoice)return;input.Text=text;SetStatus("识别结果不确定，请修改后点击发送，或清空输入框重新说。",true);FocusInput();}
        internal void SendVoice(string text){if(!CanAcceptVoice)return;input.Text=text;voiceInput=true;Send();}

        internal GptChatWindow(Window pet, IList<ChatMessage> savedHistory, GptSettings savedSettings, Action<string, double> petPreview, Action<bool> petThinking, Action<string> onAssistantReply, Action stopReplySpeech, Func<bool> wantsJapaneseReply, Func<string, Task<string>> localMusic = null, Action stopMusicSearch = null, MusicRouter contextualMusic = null, GptClient modelClient = null, Action beginStreamSpeech = null, Action<string> appendStreamSpeech = null, Action endStreamSpeech = null)
        {
            Owner = pet;
            history = savedHistory; settings = savedSettings; preview = petPreview; thinking = petThinking;
            assistantReply = onAssistantReply;
            stopSpeech = stopReplySpeech;
            this.beginStreamSpeech = beginStreamSpeech; this.appendStreamSpeech = appendStreamSpeech; this.endStreamSpeech = endStreamSpeech;
            wantsJapaneseSpeech = wantsJapaneseReply; musicCommand = localMusic; cancelMusic = stopMusicSearch;
            this.modelClient = modelClient ?? new GptClient();
            if (contextualMusic != null) musicConversation = new MusicConversation(this.modelClient, contextualMusic);
#if DEEPSEEK
            Title = "和杏菜聊天 · DeepSeek";
#else
            Title = "和杏菜聊天 · GPT";
#endif
            Title += " · v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(2);
            BuildCompanionPanel(contextualMusic);
            Closing += delegate
            {
                closing = true;
                utilityGeneration++;
                InterruptReply("关闭窗口时中断");
                CancelMaintenance();
                if (cancelMusic != null) cancelMusic();
                StopSpeech();
                if (request != null) { request.Cancel(); preview("这次先停在这里，想聊时再发给我。", 4); }
                thinking(false);
            };
            Loaded += delegate { if (SelectedCompanionTab == "chat") FocusInput(); StartMaintenance(); };
            revealTimer.Tick += RevealTick; Closed += delegate { revealTimer.Stop(); };
            RebuildMessages(); RefreshReadyStatus();
        }

        internal void FocusInput() { if (SelectedCompanionTab == "chat" && input.IsEnabled) { input.Focus(); Keyboard.Focus(input); } }

        private void RebuildMessages(bool scrollToEnd = true)
        {
            messages.Children.Clear();
            if (history.Count == 0 && activeReply == null)
            {
                if (Program.ChatPreview)
                {
                    AddBubble("user", "杏菜，陪我听首歌吧。");
                    AddBubble("assistant", "好呀！今天想听哪首？告诉我歌名，或者点「一起听」，我们接着听你正在放的那首。");
                }
                else messages.Children.Add(BuildWelcomeMessage());
            }
            if (history.Count > visibleMessageLimit)
            {
                Button earlier = ChatButton("查看更早的聊天（本机已保存）", delegate { visibleMessageLimit += 80; RebuildMessages(false); conversation.ScrollToTop(); }, false);
                earlier.Margin = new Thickness(0, 0, 0, 12); messages.Children.Add(earlier);
            }
            for (int i = Math.Max(0, history.Count - visibleMessageLimit); i < history.Count; i++)
            {
                var item = history[i];
                if (activeReply != null && item == activeReply.DisplayedAnswer)
                {
                    var old = activeReply.Bubble;
                    string zh = old == null ? "" : old.Text;
                    string ja = old == null ? "" : ((TextBox)old.Tag).Text;
                    activeReply.Bubble = AddBubble(item.Role, zh, ja, item.JapaneseText);
                    Reveal(activeReply, item.Content, item.JapaneseText);
                }
                else AddBubble(item.Role, item.Content, item.JapaneseText, item.JapaneseText);
            }
            if (activeReply != null && !activeReply.Committed)
            {
                AddBubble("user", activeReply.UserText);
                if (activeReply.Bubble != null) activeReply.Bubble = AddBubble("assistant", activeReply.Bubble.Text, ((TextBox)activeReply.Bubble.Tag).Text);
            }
            if (scrollToEnd) ScrollToEnd();
        }

        private static readonly Brush OutgoingBubble = GptUi.Brush(65, 105, 132);
        private static readonly Brush IncomingBubble = PanelPalette.Incoming;

        private TextBox AddBubble(string role, string text, string japanese = null, string replayText = null)
        {
            bool user = role == "user" || role == "local_user";
            bool localResult = role == "local_assistant";
            string speaker = user ? "你" : companionName.Text;
            var row = new Grid {
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 485, Margin = new Thickness(user ? 50 : 6, 2, user ? 7 : 36, 13)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (!user)
            {
                var avatar = new Border {
                    Width = 30, Height = 30, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Top,
                    CornerRadius = new CornerRadius(15), Background = Brushes.White, ToolTip = speaker
                };
                if (companionPortrait != null)
                {
                    avatar.Child = new Border { CornerRadius = new CornerRadius(15), Background = companionPortrait };
                    RenderOptions.SetBitmapScalingMode(avatar, BitmapScalingMode.HighQuality);
                }
                else
                    avatar.Child = new TextBlock { Text = speaker, FontSize = 10, Foreground = PanelText, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(avatar);
            }
            var copyable = new TextBox {
                Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = user ? Brushes.White : PanelText,
                FontSize = 14, Padding = new Thickness(0), IsReadOnlyCaretVisible = false
            };
            AutomationProperties.SetName(copyable, localResult ? speaker + " · 操作结果" : speaker);
            FrameworkElement body = copyable;
            if (role == "assistant")
            {
                var content = new StackPanel();
                content.Children.Add(copyable);
                var translation = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = PanelQuiet,
                    Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(0, 5, 0, 0),
                    Background = Brushes.Transparent, Foreground = PanelText, FontSize = 13,
                    Text = japanese ?? "", Visibility = String.IsNullOrWhiteSpace(japanese) ? Visibility.Collapsed : Visibility.Visible };
                AutomationProperties.SetName(translation, "日语译文");
                copyable.Tag = translation;
                content.Children.Add(translation);
                body = content;
            }
            if (localResult)
            {
                var content = new StackPanel();
                var label = GptUi.Text("操作结果", 11, PanelQuiet);
                label.FontWeight = FontWeights.SemiBold;
                label.Margin = new Thickness(0, 0, 0, 3);
                content.Children.Add(label);
                content.Children.Add(copyable);
                body = content;
            }
            var bubble = new Border {
                Background = user ? OutgoingBubble : IncomingBubble,
                CornerRadius = user ? new CornerRadius(16, 16, 5, 16) : new CornerRadius(5, 16, 16, 16),
                Padding = new Thickness(13, 8, 13, 8), Child = body
            };
            Grid.SetColumn(bubble, 1);
            row.Children.Add(bubble);
            if (role == "assistant" && !String.IsNullOrWhiteSpace(replayText))
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var replay = new Button { Content = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14, Width = 27, Height = 27, ToolTip = "重播这条日语配音",
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 0, 2),
                    BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = PanelText };
                AutomationProperties.SetName(replay, "重播这条日语配音");
                replay.Click += delegate {
                    if (HasPendingModelTurn) { SetStatus("请等当前回复完成后再重播。", false); return; }
                    var pet = Owner as PetWindow;
                    if (pet != null) pet.SpeakVoiceReply(replayText);
                };
                Grid.SetColumn(replay, 2);
                row.Children.Add(replay);
            }
            messages.Children.Add(row);
            return copyable;
        }

        private void ScrollToEnd() { Dispatcher.BeginInvoke(new Action(delegate { if (!closing) conversation.ScrollToEnd(); })); }
        private void RefreshReadyStatus()
        {
            var saved = history as ConversationHistory;
            string notice = saved == null ? "" : saved.StorageNotice;
            bool configured = !String.IsNullOrWhiteSpace(settings.ApiKey);
            connectionText.Text = configured ? "当前模型 · " + settings.Model : "模型待配置";
            connectionDot.Background = configured ? GptUi.Brush(54, 142, 130) : GptUi.Brush(113, 139, 158);
            SetStatus(notice, !String.IsNullOrEmpty(notice));
        }
        private void SetStatus(string text, bool error) {
            status.Text = text ?? ""; status.Visibility = String.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            status.Foreground = error ? GptUi.Brush(155, 58, 54) : PanelQuiet;
        }
        private void SetBusy(bool active)
        {
            busy = active; input.IsEnabled = !active; send.IsEnabled = !active; stop.IsEnabled = true; fresh.IsEnabled = !active;
            progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed; thinking(active);
        }

        private async void Send() { await SendCoreAsync(); }

        internal Task SubmitAsync(string text)
        {
            if (busy || closing) return Task.FromResult(0);
            input.Text = text;
            return SendCoreAsync();
        }

        private async Task SendCoreAsync()
        {
            if (busy || closing) return;
            StopSpeech();
            string userText = input.Text.Trim();
            DateTime userSentUtc = DateTime.UtcNow;
            bool fromVoice=voiceInput;voiceInput=false;
            CallAssistantText = "";
            if (userText.Length == 0) { SetStatus("先写一点想说的话吧。", false); input.Focus(); return; }
            // Keep explicit local utilities available without a model. Resolve conversational music below.
            string localMemory = PetMemory.Handle(userText);
            if (localMemory != null)
            {
                CancelMaintenance();
                history.Add(new ChatMessage("local_user", userText) { CreatedUtc = userSentUtc }); history.Add(new ChatMessage("local_assistant", localMemory));
                var saved = history as ConversationHistory;
                if (saved != null && localMemory.StartsWith("记忆已删除", StringComparison.Ordinal))
                {
                    saved.MemoryProcessedCount = history.Count;
                    saved.DiscardPendingMemory();
                }
                string storage = PersistHistory();
                input.Clear(); RebuildMessages(); SetStatus("本机记忆操作已完成" + storage, storage.Length > 0);
                StartMaintenance(); return;
            }
            string remembered = PetMemory.Observe(userText, userSentUtc, history);
            if (remembered != null && String.IsNullOrWhiteSpace(settings.ApiKey))
            {
                const string reply = "嗯，我记住了。";
                history.Add(new ChatMessage("user", userText) { CreatedUtc = userSentUtc });
                history.Add(new ChatMessage("local_assistant", reply));
                if (UserMessageSubmitted != null) UserMessageSubmitted();
                CallAssistantText = reply;
                string storage = PersistHistory();
                input.Clear(); RebuildMessages();
                SetStatus("记忆已加密保存在本机。配置聊天模型后可以继续对话。" + storage, storage.Length > 0);
                return;
            }
            bool localUtility = System.Text.RegularExpressions.Regex.IsMatch(userText, @"^(?:本地(?:播放|放|听)|(?:播放|放|听)本地|移除收藏第?\d+首?$|播放收藏第?\d+首?$|(?:打开)?(?:音乐面板|正在播放面板)$)");
            if (musicCommand != null && (String.IsNullOrWhiteSpace(settings.ApiKey) || musicConversation == null || localUtility))
            {
                long generation = ++utilityGeneration;
                string musicResult;
                try
                {
                    Task<string> pendingMusic = musicCommand(userText);
                    if (!pendingMusic.IsCompleted) SetBusy(true);
                    musicResult = await pendingMusic;
                }
                catch (Exception) { musicResult = "音乐操作未能完成，请重试。"; }
                finally { if (generation == utilityGeneration && busy && request == null && !closing) SetBusy(false); }
                if (closing || generation != utilityGeneration) return;
                if (musicResult != null)
                {
                    history.Add(new ChatMessage("local_user", userText) { CreatedUtc = userSentUtc }); history.Add(new ChatMessage("local_assistant", musicResult));
                    string storage = PersistHistory();
                    input.Clear(); RebuildMessages(); SetStatus("音乐点播 · 搜索词发送至 QQ 音乐 · 本次未调用模型" + storage, storage.Length > 0);
                    preview(musicResult.Length > 35 ? musicResult.Substring(0, 35) + "…" : musicResult, 8); input.Focus(); return;
                }
            }
            if (string.IsNullOrWhiteSpace(settings.ApiKey)) { SetStatus("还没有 API Key。请在「设置」的「模型连接」中填写。本次没有发送任何请求。", true); return; }
            bool japaneseSpeech = fromVoice || wantsJapaneseSpeech != null && wantsJapaneseSpeech();
            CancellationTokenSource activeRequest = new CancellationTokenSource(); request = activeRequest;
            ReplyTurn turn = new ReplyTurn { SpeakJapanese = japaneseSpeech, Cancellation = activeRequest, UserText = userText, SentUtc = userSentUtc }; activeReply = turn;
            SetBusy(true); SetStatus("杏菜正在思考……按住鼠标后退侧键可以打断并说话。", false);
            RebuildMessages(); ScrollToEnd();
            preview("我正在想你的问题……", 180);
            try
            {
                if (UserMessageSubmitted != null) UserMessageSubmitted();
                GptReply reply = musicConversation == null
                    ? await modelClient.SendReplyAsync(settings, history, userText, japaneseSpeech, activeRequest.Token, delegate(GptStreamUpdate update) { ApplyStream(turn, update); })
                    : await musicConversation.SendAsync(settings, history, userText, japaneseSpeech, activeRequest.Token,
                        delegate(string message) { if (OwnsTurn(turn)) SetStatus(message, false); },
                        delegate(GptStreamUpdate update) { ApplyStream(turn, update); }, delegate(string receipt) { RecordToolReceipt(turn, receipt); });
                if (activeReply != turn || closing) return;
                bool hasToolResult = reply != null && !String.IsNullOrWhiteSpace(reply.ToolResultText);
                if (!hasToolResult) activeRequest.Token.ThrowIfCancellationRequested();
                string result = reply == null ? null : reply.DisplayText;
                if (string.IsNullOrWhiteSpace(result) && !hasToolResult) throw new InvalidOperationException("模型没有返回可显示的文字，请重试。");
                turn.Committed = true;
                history.Add(new ChatMessage("user", userText) { CreatedUtc = userSentUtc });
                if (hasToolResult) history.Add(new ChatMessage("local_assistant", reply.ToolResultText));
                ChatMessage answer = null;
                if (!string.IsNullOrWhiteSpace(result))
                {
                    answer = new ChatMessage("assistant", result) { JapaneseText = reply.JapaneseSpeechText };
                    turn.DisplayedAnswer = answer;
                    history.Add(answer);
                }
                bool narrationFailed = hasToolResult && String.IsNullOrWhiteSpace(result);
                if (narrationFailed && !String.IsNullOrWhiteSpace(turn.PartialText))
                    history.Add(new ChatMessage("assistant", "【生成中断，未完成】\n" + turn.PartialText));
                if (String.IsNullOrWhiteSpace(result)) result = reply.ToolResultText;
                string storageNotice = PersistHistory();
                input.Clear(); RebuildMessages();
                if (japaneseSpeech && answer != null && !turn.SpeechStarted && !String.IsNullOrWhiteSpace(reply.DisplayText)
                    && String.IsNullOrWhiteSpace(reply.JapaneseSpeechText) && OwnsTurn(turn))
                {
                    SetStatus("中文回复已显示，正在补充日语配音……", false);
                    try
                    {
                        reply.JapaneseSpeechText = await modelClient.TranslateForSpeechAsync(settings, reply.DisplayText, activeRequest.Token);
                    }
                    catch (OperationCanceledException) { if (activeRequest.IsCancellationRequested) return; }
                    catch (Exception ex) { Program.Log("Voice translation failed: " + ex.GetType().Name); }
                    if (!OwnsTurn(turn)) return;
                    if (!String.IsNullOrWhiteSpace(reply.JapaneseSpeechText))
                    {
                        answer.JapaneseText = reply.JapaneseSpeechText;
                        storageNotice = PersistHistory();
                        RebuildMessages();
                    }
                    reply.SpeechNotice = String.IsNullOrWhiteSpace(reply.JapaneseSpeechText)
                        ? "日语补译未成功，中文回复已保留。" : "";
                }
                string replyStatus = "已收到回复 · " + settings.Model + (musicConversation != null ? " · 上下文解析已启用" : "") + (hasToolResult ? " · 操作结果以音乐工具回执为准" : "");
                CallAssistantText = result;
                replyStatus += storageNotice.Length == 0 ? " · 聊天已保存到本机" : storageNotice;
                if (!string.IsNullOrWhiteSpace(reply.SpeechNotice)) replyStatus += "\n" + reply.SpeechNotice;
                SetStatus(replyStatus, storageNotice.Length > 0);
                string shortText = result.Replace("\r", " ").Replace("\n", " ").Trim();
                if (shortText.Length > 30) shortText = shortText.Substring(0, 30) + "…";
                preview(shortText, 12);
                PetWindow petOwner=Owner as PetWindow; if(petOwner!=null)petOwner.ReactToReply(result);
                if (narrationFailed) StopSpeech();
                else if (turn.SpeechStarted) { if (endStreamSpeech != null) endStreamSpeech(); }
                else if(fromVoice && petOwner!=null)petOwner.SpeakVoiceReply(reply.JapaneseSpeechText);
                else if (japaneseSpeech && assistantReply != null && !string.IsNullOrWhiteSpace(reply.JapaneseSpeechText)) assistantReply(reply.JapaneseSpeechText);
                if (!narrationFailed && ReplyMotionFinished != null) ReplyMotionFinished(true);
                if (!activeRequest.IsCancellationRequested) StartMaintenance();
            }
            catch (OperationCanceledException)
            {
                if (activeReply == turn && !closing)
                {
                    StopSpeech();
                    string interruptedNotice = CommitInterrupted(turn, "回复已打断");
                    input.Clear();
                    RebuildMessages();
                    SetStatus("回复已中断，已显示部分标为未完成，可以继续提问。" + interruptedNotice, !activeRequest.IsCancellationRequested || interruptedNotice.Length > 0);
                    preview("这次先停在这里，想聊时再发给我。", 4);
                }
            }
            catch (Exception ex)
            {
                if (activeReply == turn && !closing)
                {
                    StopSpeech();
                    bool partial = turn.PartialText.Length > 0 || turn.Receipt.Length > 0;
                    string savedNotice = partial ? CommitInterrupted(turn, "生成中断") : "";
                    if (partial) input.Clear();
                    RebuildMessages();
                    string error = ex.Message ?? "未知错误";
                    if (!string.IsNullOrEmpty(settings.ApiKey)) error = error.Replace(settings.ApiKey, "[已隐藏密钥]");
                    if (error.Length > 450) error = error.Substring(0, 450);
                    SetStatus("发送失败：" + error + (partial ? "\n已显示部分标为未完成，可以继续提问。" : "\n输入内容已保留，可以修改设置后重试。") + (remembered == null ? "" : "\n这条记忆已保存在本机。") + savedNotice, true);
                    preview("消息没发成功，可以在聊天窗口查看原因。", 5);
                }
            }
            finally
            {
                bool owns = activeReply == turn;
                if (owns) { request = null; activeReply = null; }
                activeRequest.Dispose();
                if (owns && !closing) { SetBusy(false); input.Focus(); }
            }
        }

        private void StopSpeech()
        {
            if (ReplyMotionFinished != null) ReplyMotionFinished(false);
            if (stopSpeech != null) { try { stopSpeech(); } catch { } }
        }
        private void Stop()
        {
            if (activeReply != null) { InterruptReply("回复已打断"); return; }
            CancelMaintenance();
            StopSpeech();
            if (cancelMusic != null) cancelMusic();
            if (request != null) { request.Cancel(); stop.IsEnabled = false; SetStatus("正在停止……", false); }
            else SetStatus("已请求停止搜索和日语配音；已发送的点播请在 QQ 音乐中停止。", false);
        }
        private void NewConversation()
        {
            if (busy) return;
            CancelMaintenance(); StopSpeech(); if (cancelMusic != null) cancelMusic();
            var saved = history as ConversationHistory;
            if (saved != null)
            {
                string previous = saved.SessionId;
                saved.StartNew(PetMemory.MemoryGeneration);
                if (previous == saved.SessionId) { SetStatus(saved.StorageNotice, true); return; }
            }
            else history.Clear();
            visibleMessageLimit = 80; input.Clear(); RebuildMessages(); RefreshReadyStatus(); input.Focus();
            StartMaintenance();
        }

        private string PersistHistory()
        {
            var saved = history as ConversationHistory;
            if (saved == null || saved.Save()) return "";
            return "\n" + saved.StorageNotice;
        }
        private void CancelMaintenance()
        {
            if (maintenanceRequest != null) { maintenanceRequest.Cancel(); maintenanceRequest = null; }
        }
        private async void StartMaintenance()
        {
            var saved = history as ConversationHistory;
            if (saved == null || Program.Smoke || closing || maintenanceRequest != null || String.IsNullOrWhiteSpace(settings.ApiKey)) return;
            var pending = new CancellationTokenSource(); maintenanceRequest = pending;
            string sessionId = saved.SessionId;
            try
            {
                var maintenance = new ConversationMaintenance(modelClient);
                while (saved.SessionId == sessionId && !pending.IsCancellationRequested)
                {
                    ConversationHistory target = saved;
                    bool active = saved.MemoryProcessedCount < saved.Count || ConversationMaintenance.NextSummaryEnd(saved) > saved.SummarizedCount;
                    int generation = PetMemory.MemoryGeneration;
                    if (generation < 0 || (!active && !saved.TryLoadPendingMemory(generation, out target))) break;
                    int before = target.MemoryProcessedCount, summaryBefore = target.SummarizedCount;
                    pending.CancelAfter(TimeSpan.FromSeconds(25));
                    string notice = await maintenance.UpdateAsync(settings, target, pending.Token, active, generation);
                    if (closing || pending.IsCancellationRequested || maintenanceRequest != pending || saved.SessionId != sessionId) break;
                    if (!active && (PetMemory.MemoryGeneration != generation || !saved.CommitPendingMemory(target, generation))) break;
                    if (!busy && notice.Length > 0) SetStatus(notice, false);
                    if (target.MemoryProcessedCount <= before && target.SummarizedCount <= summaryBefore) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally { if (maintenanceRequest == pending) maintenanceRequest = null; pending.Dispose(); }
        }
        private void OpenSettings()
        {
            if (busy) return;
            CancelMaintenance();
            foreach (Window window in OwnedWindows)
                if (window is GptSettingsWindow) { window.Show(); window.Activate(); return; }
            GptSettingsWindow editor = new GptSettingsWindow(settings, ClearChatHistory); editor.Owner = this;
            editor.Closed += delegate { if (!closing) { RebuildMessages(); RefreshReadyStatus(); FocusInput(); StartMaintenance(); } };
            editor.Show();
        }

        private string ClearChatHistory()
        {
            if (busy) return "请等当前回复结束后再清空聊天记录。";
            CancelMaintenance();
            var saved = history as ConversationHistory;
            if (saved != null && !saved.DeleteAll()) return saved.StorageNotice;
            if (cancelMusic != null) cancelMusic();
            if (saved == null) history.Clear();
            visibleMessageLimit = 80;
            RebuildMessages();
            RefreshReadyStatus();
            return "聊天记录已清空，长期记忆已保留。";
        }

        internal void VerifyMusicCommandSmoke()
        {
            if (musicCommand == null || !string.IsNullOrEmpty(settings.ApiKey)) throw new InvalidOperationException("Smoke music setup invalid");
            input.Text = "播放一首晴天"; Send();
            if (busy || request != null || history.Count != 2 || input.Text != "" || history[1].Content != "测试点歌已处理") throw new InvalidOperationException("Music command must work without API key");
        }

        internal void VerifyMusicSelectionSmoke()
        {
            if (musicCommand == null || !string.IsNullOrEmpty(settings.ApiKey) || history.Count != 0)
                throw new InvalidOperationException("Smoke candidate selection setup invalid");
            input.Text = "播放 LOVE 2000"; Send();
            if (busy || request != null || input.Text != "" || history.Count != 2 ||
                history[0].Role != "local_user" || history[0].Content != "播放 LOVE 2000" ||
                history[1].Role != "local_assistant" || !history[1].Content.Contains("回复“第一首”"))
                throw new InvalidOperationException("QQ candidate search must remain in the no-key music chat path");
            input.Text = "第一首"; Send();
            if (busy || request != null || input.Text != "" || history.Count != 4 ||
                history[2].Role != "local_user" || history[2].Content != "第一首" ||
                history[3].Role != "local_assistant" || !history[3].Content.Contains("已向 QQ 音乐发送播放请求"))
                throw new InvalidOperationException("Bare ordinal must select QQ music instead of falling through to GPT");
            foreach (ChatMessage item in history)
                if (item.Role != "local_user" && item.Role != "local_assistant")
                    throw new InvalidOperationException("Candidate selection must not add GPT conversation roles");
        }

        internal void VerifyNoKeySmoke()
        {
            if (Program.Smoke)
            {
                bool originalNight = Appearance.Night;
                for (int click = 0; click < 2; click++)
                {
                    bool before = Appearance.Night;
                    Button toggle = GptUi.FindSmokeButton(this, before ? "切换到日间配色" : "切换到夜间配色");
                    if (toggle == null) throw new InvalidOperationException("Missing theme switch");
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (Appearance.Night == before) throw new InvalidOperationException("Theme switch did not apply");
                }
                if (Appearance.Night != originalNight) throw new InvalidOperationException("Theme did not round trip");
            }
            if (!string.IsNullOrEmpty(settings.ApiKey)) throw new InvalidOperationException("Smoke settings must not include a key.");
            input.Text = "这是一条不应发送到网络的测试消息。";
            string original = input.Text; Send();
            if (busy || request != null || history.Count != 0 || input.Text != original || !status.Text.Contains("没有发送任何请求")) throw new InvalidOperationException("Missing-key protection failed.");
            Button settingsButton = GptUi.FindSmokeButton(this, "设置");
            if (settingsButton == null || !settingsButton.IsVisible || !settingsButton.IsEnabled ||
                GptUi.FindSmokeButton(this, "通话") == null || GptUi.FindSmokeButton(this, "新对话") == null ||
                GptUi.FindSmokeButton(this, "记忆") != null || GptUi.FindSmokeButton(this, "清空本机聊天记录") != null)
                throw new InvalidOperationException("Chat toolbar must open settings and have no manual memory controls.");
            settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout();
            Button modelSettings = GptUi.FindSmokeButton(this, "模型连接");
            if (SelectedCompanionTab != "settings" || modelSettings == null || !modelSettings.IsVisible)
                throw new InvalidOperationException("Settings tab did not display the model connection control.");
            modelSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = OwnedWindows.Count == 1 ? OwnedWindows[0] as GptSettingsWindow : null;
            if (editor == null || !editor.IsVisible) throw new InvalidOperationException("Chat settings button did not open settings.");
            try { editor.VerifySmoke(); } finally { editor.Close(); }
            SelectCompanionTab("chat");
            if (input.Text != original) throw new InvalidOperationException("Changing tabs lost the unsent draft.");
        }

        internal static Button ChatButton(string text, Action action, bool primary)
        {
            Button button = GptUi.Button(text, action, primary); button.FontWeight = FontWeights.SemiBold;
            button.Background = primary ? GptUi.Brush(50, 103, 151) : PanelPalette.Surface;
            button.Foreground = primary ? Brushes.White : PanelPalette.Ink;
            button.BorderBrush = primary ? GptUi.Brush(50, 103, 151) : GptUi.Brush(207, 224, 238);
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "buttonChrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content); template.VisualTree = border;
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.OpacityProperty, 0.84, "buttonChrome")); template.Triggers.Add(hover);
            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "buttonChrome")); template.Triggers.Add(focus);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(Border.OpacityProperty, 0.48, "buttonChrome")); template.Triggers.Add(disabled);
            button.Template = template; return button;
        }
    }

    internal sealed class GptSettingsWindow : Window
    {
        private readonly GptSettings settings;
        private readonly PasswordBox secret;
        private readonly TextBox model;
        private readonly CheckBox remember;
        private readonly TextBlock status;

        internal GptSettingsWindow(GptSettings current, Func<string> clearChatHistory)
        {
            settings = current;
#if DEEPSEEK
            const string provider = "DeepSeek";
#else
            const string provider = "GPT";
#endif
            Title = provider + " 设置 · 杏菜"; Width = 485; Height = 540; MinWidth = 350; MinHeight = 420;
            ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            StackPanel body = new StackPanel { Margin = new Thickness(17, 16, 17, 14) }; Content = body;
#if DEEPSEEK
            body.Children.Add(GptUi.Text("DeepSeek API Key", 13, GptChatWindow.PanelText));
#else
            body.Children.Add(GptUi.Text("OpenAI API Key", 13, GptChatWindow.PanelText));
#endif
            secret = new PasswordBox { Password = current.ApiKey ?? "", Padding = new Thickness(10, 8, 10, 8), FontSize = 14, Margin = new Thickness(0, 7, 0, 14), Background = PanelPalette.Surface, Foreground = PanelPalette.Ink, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1), MaxLength = 512 };
            body.Children.Add(secret);
            body.Children.Add(GptUi.Text("模型名称", 13, GptChatWindow.PanelText));
#if DEEPSEEK
            model = new TextBox { Text = string.IsNullOrWhiteSpace(current.Model) ? "deepseek-flash" : current.Model, Padding = new Thickness(10, 8, 10, 8), FontSize = 13, MaxLength = 100, Margin = new Thickness(0, 7, 0, 14), Background = PanelPalette.Surface, Foreground = PanelPalette.Ink, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1) };
#else
            model = new TextBox { Text = string.IsNullOrWhiteSpace(current.Model) ? "gpt-6-luna" : current.Model, Padding = new Thickness(10, 8, 10, 8), FontSize = 13, MaxLength = 100, Margin = new Thickness(0, 7, 0, 14), Background = PanelPalette.Surface, Foreground = PanelPalette.Ink, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1) };
#endif
            body.Children.Add(model);
            remember = new CheckBox { Content = "为当前 Windows 账户记住 API Key", IsChecked = current.RememberKey, Foreground = GptChatWindow.PanelText, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) }; body.Children.Add(remember);
            TextBlock storage = GptUi.Text("勾选后使用 Windows 账户加密保存在本机；不勾选则只在当前程序内存中使用。", 11, GptChatWindow.PanelQuiet); storage.LineHeight = 19; storage.Margin = new Thickness(0, 0, 0, 15); body.Children.Add(storage);
            Border note = new Border { Background = PanelPalette.Sky, CornerRadius = new CornerRadius(9), Padding = new Thickness(11, 9, 11, 9) };
#if DEEPSEEK
            const string billing = "DeepSeek API Key 与网页账号分开，使用情况以 DeepSeek 平台为准。";
#else
            const string billing = "OpenAI API 单独计费，使用情况以 OpenAI 平台为准。";
#endif
            TextBlock explanation = GptUi.Text(billing + "\n\n你在聊天中说到的称呼、明确喜好会自动加密保存在本机。联网聊天时，当前模型会收到你的消息、相关聊天记录和记忆。后台用模型整理较复杂的记忆和摘要时也会产生 API 用量。", 12, GptChatWindow.PanelQuiet);
            explanation.LineHeight = 20; note.Child = explanation; body.Children.Add(note);
            Button save = GptChatWindow.ChatButton("保存设置", Save, true); save.Margin = new Thickness(0, 14, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Stretch; body.Children.Add(save);
            status = GptUi.Text("保存设置不会发送测试请求。", 11, GptChatWindow.PanelQuiet); status.LineHeight = 18; status.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(status);
            Button clearChat = GptChatWindow.ChatButton("清空本机聊天记录", delegate {
                if (MessageBox.Show(this, "要清空当前版本的聊天记录、旧会话归档和对话摘要吗？杏菜会保留已经记住的事情。",
                    "清空聊天记录", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                    status.Text = clearChatHistory();
            }, false);
            clearChat.HorizontalAlignment = HorizontalAlignment.Left;
            clearChat.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(clearChat);
            PanelTheme.Apply(this, provider + " 模型连接", "MODEL");
        }
        private void Save()
        {
            string chosenModel = model.Text.Trim();
            if (chosenModel.Length == 0) { status.Foreground = GptUi.Brush(167, 66, 62); status.Text = "请填写模型名称。"; model.Focus(); return; }
            GptSettings updated = new GptSettings { ApiKey = secret.Password.Trim(), Model = chosenModel, RememberKey = remember.IsChecked == true };
            try
            {
                updated.Save();
                settings.ApiKey = updated.ApiKey; settings.Model = updated.Model; settings.RememberKey = updated.RememberKey;
                status.Foreground = GptUi.Blue; status.Text = string.IsNullOrWhiteSpace(settings.ApiKey) ? "已保存。尚未填写 API Key，聊天请求保持关闭。" : "已保存。关闭设置后，即可主动发送第一条消息。";
            }
            catch (Exception ex) { status.Foreground = GptUi.Brush(167, 66, 62); status.Text = "保存失败：" + ex.Message; }
        }
        internal void VerifySmoke()
        {
            if (secret == null || secret.Password.Length != 0 || model.Text.Length == 0 || !string.IsNullOrEmpty(settings.ApiKey)) throw new InvalidOperationException("GPT settings initialization failed.");
            Button clear = GptUi.FindSmokeButton(this, "清空本机聊天记录");
            if (clear == null || !clear.IsVisible || !clear.IsEnabled ||
                GptUi.FindSmokeButton(this, "添加记忆") != null || GptUi.FindSmokeButton(this, "删除选中") != null)
                throw new InvalidOperationException("Settings must offer chat history clearing without manual memory editing.");
        }
    }

    internal static class GptUi
    {
        internal static Button FindSmokeButton(DependencyObject root, string label)
        {
            Button button = root as Button;
            if (button != null && ((button.Content as string) == label || System.Windows.Automation.AutomationProperties.GetName(button) == label)) return button;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var element = child as DependencyObject;
                if (element == null) continue;
                Button found = FindSmokeButton(element, label);
                if (found != null) return found;
            }
            return null;
        }

        internal static SolidColorBrush Ink {get{return Appearance.Night ? Brush(225,235,247) : Brush(39,58,77);}}
        internal static SolidColorBrush Muted {get{return Appearance.Night ? Brush(179,198,220) : Brush(112,131,148);}}
        internal static readonly SolidColorBrush Blue = Brush(42, 115, 173);
        internal static SolidColorBrush Brush(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
        internal static TextBlock Text(string text, double size, Brush color) { return new TextBlock { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap }; }
        internal static Button Button(string label, Action clicked, bool primary)
        {
            Button button = new Button { Content = label, FontSize = 12, Padding = new Thickness(13, 8, 13, 8), Cursor = Cursors.Hand, Background = primary ? Blue : Brushes.White, Foreground = primary ? Brushes.White : Blue, BorderBrush = primary ? Blue : Brush(209, 224, 236), BorderThickness = new Thickness(1) };
            button.Click += delegate { clicked(); }; return button;
        }
    }
}
