using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed class GptChatWindow : Window
    {
        private readonly IList<ChatMessage> history;
        private readonly GptSettings settings;
        private readonly Action<string, double> preview;
        private readonly Action<bool> thinking;
        private readonly Action<string> assistantReply;
        private readonly Action stopSpeech;
        private readonly Func<bool> wantsJapaneseSpeech;
        private readonly Func<string, Task<string>> musicCommand;
        private readonly Action cancelMusic;
        private readonly StackPanel messages;
        private readonly ScrollViewer conversation;
        private readonly TextBox input;
        private readonly TextBlock status;
        private readonly Button send, stop, fresh, configure;
        private readonly ProgressBar progress;
        private CancellationTokenSource request;
        private bool busy, closing;
        private bool voiceInput;
        internal bool CanAcceptVoice {get{return !busy && !closing && String.IsNullOrWhiteSpace(input.Text);}}
        internal void ReviewVoice(string text){if(!CanAcceptVoice)return;input.Text=text;SetStatus("识别结果不确定，请修改后点击发送，或清空输入框重新说。",true);FocusInput();}
        internal void SendVoice(string text){if(!CanAcceptVoice)return;input.Text=text;voiceInput=true;Send();}

        internal GptChatWindow(Window pet, IList<ChatMessage> savedHistory, GptSettings savedSettings, Action<string, double> petPreview, Action<bool> petThinking, Action<string> onAssistantReply, Action stopReplySpeech, Func<bool> wantsJapaneseReply, Func<string, Task<string>> localMusic = null, Action stopMusicSearch = null)
        {
            Owner = pet;
            history = savedHistory; settings = savedSettings; preview = petPreview; thinking = petThinking;
            assistantReply = onAssistantReply;
            stopSpeech = stopReplySpeech;
            wantsJapaneseSpeech = wantsJapaneseReply; musicCommand = localMusic; cancelMusic = stopMusicSearch;
#if DEEPSEEK
            Title = "和杏菜聊天 · DeepSeek";
#else
            Title = "和杏菜聊天 · GPT";
#endif
            Rect workArea = SystemParameters.WorkArea;
            Width = Math.Min(680, Math.Max(250, workArea.Width - 30)); Height = Math.Min(750, Math.Max(300, workArea.Height - 30));
            MinWidth = Math.Min(520, Width); MinHeight = Math.Min(580, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Background = Appearance.Night ? GptUi.Brush(20,29,46) : GptUi.Brush(231, 241, 250);
            Grid backdrop = new Grid(); Content = backdrop;
            BitmapSource art = LoadChatBackground();
            if (art != null)
            {
                Image artwork = new Image { Source = art, Stretch = Stretch.UniformToFill, Opacity = Appearance.Night ? 0.25 : 0.82, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(artwork, BitmapScalingMode.HighQuality); backdrop.Children.Add(artwork);
                backdrop.Children.Add(new Border { Background = Appearance.Night ? new SolidColorBrush(Color.FromArgb(150,20,29,46)) : new SolidColorBrush(Color.FromArgb(88, 234, 244, 252)), IsHitTestVisible = false });
            }
            Grid root = new Grid(); root.Margin = new Thickness(22, 19, 22, 17); backdrop.Children.Add(root);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel titles = new StackPanel();
            TextBlock tag = GptUi.Text("ANNA YANAMI  /  杏菜", 10, GptUi.Brush(250, 207, 107)); tag.FontWeight = FontWeights.Bold; titles.Children.Add(tag);
            TextBlock title = GptUi.Text("和杏菜聊聊", 23, Brushes.White); title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0, 5, 0, 0); titles.Children.Add(title);
            TextBlock subtitle = GptUi.Text("中文聊天 · 可选日语配音", 12, GptUi.Brush(203, 226, 245)); subtitle.Margin = new Thickness(0, 5, 0, 0); titles.Children.Add(subtitle); header.Children.Add(titles);
            StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            fresh = ChatButton("新对话", NewConversation, false); tools.Children.Add(fresh);
            Button memories = ChatButton("记忆", OpenMemory, false); memories.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(memories);
            configure = ChatButton("设置", OpenSettings, false); configure.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(configure);
            Grid.SetColumn(tools, 1); header.Children.Add(tools);
            Border headerCard = new Border { Background = GptUi.Brush(30, 55, 90), CornerRadius = new CornerRadius(15), Padding = new Thickness(17, 13, 17, 14), Margin = new Thickness(0, 0, 0, 12), Child = header }; root.Children.Add(headerCard);

            progress = new ProgressBar { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed, Foreground = GptUi.Brush(73, 141, 190), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(progress, 1); root.Children.Add(progress);
            messages = new StackPanel { Margin = new Thickness(0, 12, 10, 12) };
            conversation = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = messages, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
            Grid.SetRow(conversation, 2); root.Children.Add(conversation);

            Border inputCard = new Border { Background = CardBrush(245), BorderBrush = GptUi.Brush(210, 225, 237), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0) };
            Grid inputGrid = new Grid(); inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); inputCard.Child = inputGrid;
            input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, MinHeight = 68, MaxHeight = 150, MaxLength = 4000, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = GptUi.Ink, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(2), ToolTip = "输入想说的话，Ctrl + Enter 发送" };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Send(); } }; inputGrid.Children.Add(input);
            Grid inputTools = new Grid { Margin = new Thickness(0, 10, 0, 0) }; inputTools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); inputTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock hint = GptUi.Text("Ctrl + Enter 发送 · Enter 换行", 11, GptUi.Muted); hint.VerticalAlignment = VerticalAlignment.Center; inputTools.Children.Add(hint);
            StackPanel sendTools = new StackPanel { Orientation = Orientation.Horizontal };
            stop = ChatButton("停止", Stop, false); stop.ToolTip = "停止当前请求和朗读"; sendTools.Children.Add(stop);
            send = ChatButton("发送", Send, true); send.MinWidth = 84; send.Margin = new Thickness(8, 0, 0, 0); sendTools.Children.Add(send); Grid.SetColumn(sendTools, 1); inputTools.Children.Add(sendTools);
            Grid.SetRow(inputTools, 1); inputGrid.Children.Add(inputTools); Grid.SetRow(inputCard, 3); root.Children.Add(inputCard);
            status = GptUi.Text("", 11, GptUi.Ink); status.LineHeight = 18; status.Margin = new Thickness(0); Border statusCard = new Border { Background = CardBrush(235), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 0), Child = status }; Grid.SetRow(statusCard, 4); root.Children.Add(statusCard);
            Closing += delegate
            {
                closing = true;
                if (cancelMusic != null) cancelMusic();
                StopSpeech();
                if (request != null) { request.Cancel(); preview("这次先停在这里，想聊时再发给我。", 4); }
                thinking(false);
            };
            Loaded += delegate { input.Focus(); };
            RebuildMessages(); RefreshReadyStatus();
        }

        internal void FocusInput() { if (input.IsEnabled) { input.Focus(); Keyboard.Focus(input); } }

        private void RebuildMessages()
        {
            messages.Children.Clear();
            if (history.Count == 0)
            {
                Border welcome = new Border { Background = CardBrush(232), BorderBrush = GptUi.Brush(225, 234, 242), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(20), Margin = new Thickness(0, 8, 0, 18), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
                StackPanel body = new StackPanel(); welcome.Child = body;
                TextBlock greeting = GptUi.Text("今天想聊什么？", 20, GptUi.Ink); greeting.FontWeight = FontWeights.SemiBold; body.Children.Add(greeting);
                TextBlock detail = GptUi.Text("随便聊聊，整理一个想法，或者一起想想晚饭吃什么。", 13, GptUi.Muted); detail.LineHeight = 23; detail.Margin = new Thickness(0, 10, 0, 12); body.Children.Add(detail);
                if (string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    TextBlock setup = GptUi.Text("还没有配置 API Key。你可以先打开右上角「设置」，准备好后再填写。", 13, GptUi.Blue); setup.LineHeight = 23; body.Children.Add(setup);
                }
                else body.Children.Add(GptUi.Text("对话仅保留在当前程序内存中，退出后清空。", 12, GptUi.Muted));
                messages.Children.Add(welcome);
            }
            foreach (ChatMessage item in history) AddBubble(item.Role, item.Content);
            ScrollToEnd();
        }

        private void AddBubble(string role, string text)
        {
            bool user = role == "user" || role == "local_user";
            StackPanel block = new StackPanel { HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 490, Margin = new Thickness(user ? 40 : 0, 0, user ? 0 : 40, 17) };
            TextBlock label = GptUi.Text(user ? "你" : (role == "local_assistant" ? "杏菜 · 音乐" : "杏菜 · GPT"), 11, GptUi.Ink); label.HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left; label.Margin = new Thickness(4, 0, 4, 5); label.Background = CardBrush(220); block.Children.Add(label);
            Border card = new Border { Background = user ? GptUi.Blue : CardBrush(245), BorderBrush = user ? GptUi.Blue : GptUi.Brush(223, 234, 242), BorderThickness = new Thickness(1), CornerRadius = user ? new CornerRadius(15, 4, 15, 15) : new CornerRadius(4, 15, 15, 15), Padding = new Thickness(13, 10, 13, 10) };
            TextBox copyable = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = user ? Brushes.White : GptUi.Ink, FontSize = 14, Padding = new Thickness(0), IsReadOnlyCaretVisible = false, Focusable = true };
            card.Child = copyable; block.Children.Add(card); messages.Children.Add(block);
        }

        private void ScrollToEnd() { Dispatcher.BeginInvoke(new Action(delegate { if (!closing) conversation.ScrollToEnd(); })); }
        private void RefreshReadyStatus()
        {
            SetStatus(string.IsNullOrWhiteSpace(settings.ApiKey) ? "等待配置 API Key · 尚未连接 GPT" : "准备好了 · " + settings.Model + " · 对话仅保存在本次运行的内存中", false);
        }
        private void SetStatus(string text, bool error) { status.Text = text; status.Foreground = error ? GptUi.Brush(167, 66, 62) : GptUi.Muted; }
        private void SetBusy(bool active)
        {
            busy = active; input.IsEnabled = !active; send.IsEnabled = !active; stop.IsEnabled = true; fresh.IsEnabled = !active; configure.IsEnabled = !active;
            progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed; thinking(active);
        }

        private async void Send()
        {
            if (busy || closing) return;
            StopSpeech();
            string userText = input.Text.Trim();
            bool fromVoice=voiceInput;voiceInput=false;
            if (userText.Length == 0) { SetStatus("先写一点想说的话吧。", false); input.Focus(); return; }
            if (musicCommand != null)
            {
                string musicResult;
                try
                {
                    Task<string> pendingMusic = musicCommand(userText);
                    if (!pendingMusic.IsCompleted) SetBusy(true);
                    musicResult = await pendingMusic;
                }
                catch (Exception) { musicResult = "音乐操作未能完成，请重试。"; }
                finally { if (busy && request == null && !closing) SetBusy(false); }
                if (closing) return;
                if (musicResult != null)
                {
                    history.Add(new ChatMessage("local_user", userText)); history.Add(new ChatMessage("local_assistant", musicResult));
                    while (history.Count > 20) history.RemoveAt(0);
                    input.Clear(); RebuildMessages(); SetStatus("音乐点播 · 搜索词发送至 QQ 音乐 · 不消耗 GPT 用量", false);
                    preview(musicResult.Length > 35 ? musicResult.Substring(0, 35) + "…" : musicResult, 8); input.Focus(); return;
                }
            }
            if (string.IsNullOrWhiteSpace(settings.ApiKey)) { SetStatus("还没有 API Key。请先打开右上角「设置」填写；本次没有发送任何请求。", true); return; }
            bool japaneseSpeech = fromVoice || wantsJapaneseSpeech != null && wantsJapaneseSpeech();
            CancellationTokenSource activeRequest = new CancellationTokenSource(); request = activeRequest;
            SetBusy(true); SetStatus("杏菜正在思考……可以随时点击「停止」。", false);
            RebuildMessages(); AddBubble("user", userText); ScrollToEnd();
            preview("我正在想你的问题……", 180);
            try
            {
                GptReply reply = await new GptClient().SendReplyAsync(settings, history, userText, japaneseSpeech, activeRequest.Token);
                if (closing) return;
                activeRequest.Token.ThrowIfCancellationRequested();
                string result = reply == null ? null : reply.DisplayText;
                if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("GPT 没有返回可显示的文字，请重试。");
                history.Add(new ChatMessage("user", userText)); history.Add(new ChatMessage("assistant", result));
                while (history.Count > 20) history.RemoveAt(0);
                input.Clear(); RebuildMessages();
                string replyStatus = "已收到回复 · " + settings.Model + " · 保留最近 10 轮对话";
                string learned=PetMemory.Observe(userText);
                if(learned!=null)replyStatus += "\n杏菜记住了："+learned+"（可在记忆面板删除）";
                if (!string.IsNullOrWhiteSpace(reply.SpeechNotice)) replyStatus += "\n" + reply.SpeechNotice;
                SetStatus(replyStatus, false);
                string shortText = result.Replace("\r", " ").Replace("\n", " ").Trim();
                if (shortText.Length > 30) shortText = shortText.Substring(0, 30) + "…";
                preview(shortText, 12);
                PetWindow petOwner=Owner as PetWindow; if(petOwner!=null)petOwner.ReactToReply(result);
                if(fromVoice && petOwner!=null)petOwner.SpeakVoiceReply(reply.JapaneseSpeechText);
                else if (japaneseSpeech && assistantReply != null && !string.IsNullOrWhiteSpace(reply.JapaneseSpeechText)) assistantReply(reply.JapaneseSpeechText);
            }
            catch (OperationCanceledException)
            {
                if (!closing)
                {
                    RebuildMessages();
                    SetStatus(activeRequest.IsCancellationRequested ? "已停止。本次消息没有加入对话，输入内容已保留。" : "请求超时，输入内容已保留，可以重试。", !activeRequest.IsCancellationRequested);
                    preview("这次先停在这里，想聊时再发给我。", 4);
                }
            }
            catch (Exception ex)
            {
                if (!closing)
                {
                    RebuildMessages();
                    string error = ex.Message ?? "未知错误";
                    if (!string.IsNullOrEmpty(settings.ApiKey)) error = error.Replace(settings.ApiKey, "[已隐藏密钥]");
                    if (error.Length > 450) error = error.Substring(0, 450);
                    SetStatus("发送失败：" + error + "\n输入内容已保留，可以修改设置后重试。", true);
                    preview("消息没发成功，可以在聊天窗口查看原因。", 5);
                }
            }
            finally
            {
                if (request == activeRequest) request = null;
                activeRequest.Dispose();
                if (!closing) { SetBusy(false); input.Focus(); }
            }
        }

        private void StopSpeech() { if (stopSpeech != null) { try { stopSpeech(); } catch { } } }
        private void Stop()
        {
            StopSpeech();
            if (cancelMusic != null) cancelMusic();
            if (request != null) { request.Cancel(); stop.IsEnabled = false; SetStatus("正在停止……", false); }
            else SetStatus("已请求停止搜索和日语配音；已发送的点播请在 QQ 音乐中停止。", false);
        }
        private void NewConversation()
        {
            if (busy) return; StopSpeech(); if (cancelMusic != null) cancelMusic(); history.Clear(); input.Clear(); RebuildMessages(); RefreshReadyStatus(); input.Focus();
        }
        private void OpenSettings()
        {
            if (busy) return;
            foreach (Window window in OwnedWindows)
                if (window is GptSettingsWindow) { window.Show(); window.Activate(); return; }
            GptSettingsWindow editor = new GptSettingsWindow(settings); editor.Owner = this;
            editor.Closed += delegate { if (!closing) { RebuildMessages(); RefreshReadyStatus(); FocusInput(); } };
            editor.Show();
        }

        private void OpenMemory()
        {
            if (busy) return;
            Window editor = new Window { Title = "杏菜的记忆", Owner = this, Width = 440, Height = 460, MinWidth = 360, MinHeight = 340,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, Background = GptUi.Brush(238, 245, 252) };
            StackPanel body = new StackPanel { Margin = new Thickness(20) };
            body.Children.Add(GptUi.Text("只保存你主动添加的内容。聊天时会把这些记忆发送给 GPT。", 12, GptUi.Muted));
            ListBox list = new ListBox { Margin = new Thickness(0, 12, 0, 10), MinHeight = 230, MaxHeight = 260, FontSize = 13 };
            Action reload = delegate { list.Items.Clear(); foreach (string item in PetMemory.Items()) list.Items.Add(item); };
            reload(); body.Children.Add(list);
            TextBox entry = new TextBox { MaxLength = 200, MinHeight = 34, Padding = new Thickness(8, 5, 8, 5), ToolTip = "输入一条希望杏菜记住的偏好或事实" };
            body.Children.Add(entry);
            TextBlock result = GptUi.Text("每条最多 200 字，最多 30 条。", 11, GptUi.Muted); result.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(result);
            WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button add = ChatButton("添加记忆", delegate { string value = entry.Text.Trim(); if (value.Length == 0) { result.Text = "先输入要记住的内容。"; return; } result.Text = PetMemory.Handle("记住：" + value); reload(); entry.Clear(); }, true);
            Button remove = ChatButton("删除选中", delegate { string value = list.SelectedItem as string; if (value == null) { result.Text = "请先选中一条记忆。"; return; } result.Text = PetMemory.Handle("忘记：" + value); reload(); }, false);
            remove.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(add); actions.Children.Add(remove); body.Children.Add(actions);
            editor.Content = body; editor.ShowDialog();
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
            if (!string.IsNullOrEmpty(settings.ApiKey)) throw new InvalidOperationException("Smoke settings must not include a key.");
            input.Text = "这是一条不应发送到网络的测试消息。";
            string original = input.Text; Send();
            if (busy || request != null || history.Count != 0 || input.Text != original || !status.Text.Contains("没有发送任何请求")) throw new InvalidOperationException("Missing-key protection failed.");
            GptSettingsWindow editor = new GptSettingsWindow(settings); editor.Owner = this; editor.Show();
            editor.VerifySmoke(); editor.Close();
        }

        private static SolidColorBrush CardBrush(byte alpha) { return Appearance.Night ? new SolidColorBrush(Color.FromArgb(alpha,45,61,82)) : new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)); }

        private static BitmapSource LoadChatBackground()
        {
            try
            {
                string path = String.IsNullOrEmpty(Appearance.Background) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "chat-background.png") : Appearance.Background;
                using (Stream stream = File.Exists(path) ? File.OpenRead(path) : Assembly.GetExecutingAssembly().GetManifestResourceStream("AnnaDesktopPet.chat-background.png"))
                {
                    if (stream == null) return null;
                    BitmapImage image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
                }
            }
            catch { return null; }
        }

        private static Button ChatButton(string text, Action action, bool primary)
        {
            Button button = GptUi.Button(text, action, primary); button.FontWeight = FontWeights.SemiBold;
            button.Background = primary ? GptUi.Brush(50, 103, 151) : GptUi.Brush(245, 249, 253);
            button.Foreground = primary ? Brushes.White : GptUi.Brush(42, 79, 113);
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

        internal GptSettingsWindow(GptSettings current)
        {
            settings = current;
#if DEEPSEEK
            Title = "DeepSeek 设置 · 杏菜"; Width = 495; Height = 545; MinWidth = 440; MinHeight = 500;
#else
            Title = "GPT 设置 · 杏菜"; Width = 495; Height = 545; MinWidth = 440; MinHeight = 500;
#endif
            ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei UI"); Background = GptUi.Brush(245, 249, 253);
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel body = new StackPanel { Margin = new Thickness(25, 22, 25, 20) }; scroll.Content = body; Content = scroll;
#if DEEPSEEK
            TextBlock heading = GptUi.Text("把 DeepSeek 接到杏菜身边", 22, GptUi.Ink); heading.FontWeight = FontWeights.SemiBold; body.Children.Add(heading);
#else
            TextBlock heading = GptUi.Text("把 GPT 接到杏菜身边", 22, GptUi.Ink); heading.FontWeight = FontWeights.SemiBold; body.Children.Add(heading);
#endif
            TextBlock subtitle = GptUi.Text("现在没有 API Key 也没关系，稍后再填写即可。", 12, GptUi.Muted); subtitle.LineHeight = 21; subtitle.Margin = new Thickness(0, 8, 0, 19); body.Children.Add(subtitle);
#if DEEPSEEK
            body.Children.Add(GptUi.Text("DeepSeek API Key", 13, GptUi.Ink));
#else
            body.Children.Add(GptUi.Text("OpenAI API Key", 13, GptUi.Ink));
#endif
            secret = new PasswordBox { Password = current.ApiKey ?? "", Padding = new Thickness(10, 8, 10, 8), FontSize = 14, Margin = new Thickness(0, 7, 0, 14), Background = Brushes.White, BorderBrush = GptUi.Brush(205, 221, 234), BorderThickness = new Thickness(1), MaxLength = 512 };
            body.Children.Add(secret);
            body.Children.Add(GptUi.Text("模型名称", 13, GptUi.Ink));
#if DEEPSEEK
            model = new TextBox { Text = string.IsNullOrWhiteSpace(current.Model) ? "deepseek-flash" : current.Model, Padding = new Thickness(10, 8, 10, 8), FontSize = 13, MaxLength = 100, Margin = new Thickness(0, 7, 0, 14), Background = Brushes.White, BorderBrush = GptUi.Brush(205, 221, 234), BorderThickness = new Thickness(1) };
#else
            model = new TextBox { Text = string.IsNullOrWhiteSpace(current.Model) ? "gpt-6-luna" : current.Model, Padding = new Thickness(10, 8, 10, 8), FontSize = 13, MaxLength = 100, Margin = new Thickness(0, 7, 0, 14), Background = Brushes.White, BorderBrush = GptUi.Brush(205, 221, 234), BorderThickness = new Thickness(1) };
#endif
            body.Children.Add(model);
            remember = new CheckBox { Content = "为当前 Windows 账户记住 API Key", IsChecked = current.RememberKey, Foreground = GptUi.Ink, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) }; body.Children.Add(remember);
            TextBlock storage = GptUi.Text("勾选后使用 Windows 账户加密保存在本机；不勾选则只在当前程序内存中使用。", 11, GptUi.Muted); storage.LineHeight = 19; storage.Margin = new Thickness(0, 0, 0, 15); body.Children.Add(storage);
            Border note = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(13), BorderBrush = GptUi.Brush(222, 233, 242), BorderThickness = new Thickness(1) };
#if DEEPSEEK
            TextBlock explanation = GptUi.Text("DeepSeek API Key 与网页账号分开，使用情况以 DeepSeek 平台为准。\n\n聊天会发送你输入的文字、最近 10 轮对话及你主动保存的记忆；不读取屏幕或本地文件。", 12, GptUi.Muted); explanation.LineHeight = 20; note.Child = explanation; body.Children.Add(note);
#else
            TextBlock explanation = GptUi.Text("OpenAI API 单独计费，ChatGPT 订阅不等同于 API 额度。\n\n聊天会发送你输入的文字、最近 10 轮对话及你主动保存的记忆；不读取屏幕或本地文件。", 12, GptUi.Muted); explanation.LineHeight = 20; note.Child = explanation; body.Children.Add(note);
#endif
            Button save = GptUi.Button("保存设置", Save, true); save.Margin = new Thickness(0, 17, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Stretch; body.Children.Add(save);
            status = GptUi.Text("保存设置不会发送测试请求。", 11, GptUi.Muted); status.LineHeight = 18; status.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(status);
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
        }
    }

    internal static class GptUi
    {
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
