using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal sealed class CallWindow : Window
    {
        internal static bool Active { get; private set; }
        private readonly AsrWorker recognizer;
        private readonly SpeechService speech;
        private readonly GptChatWindow chat;
        private readonly CallAudio audio = new CallAudio();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly TextBlock state, duration, mine, anna;
        private readonly ProgressBar meter;
        private readonly Button connect, mute;
        private readonly CheckBox interrupt;
        private Microphone microphone;
        private bool muted, processing, connecting, closed, interrupted;
        private int session, turn, captureVersion;
        private DateTime began, listenAfter;

        internal CallWindow(Window owner, GptChatWindow conversation, AsrWorker worker, SpeechService voice)
        {
            Owner = owner; chat = conversation; recognizer = worker; speech = voice;
            Title = "和杏菜通话"; Width = 440; Height = 670; MinWidth = 380; MinHeight = 530;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = PanelPalette.Sky;
            Foreground = PanelPalette.Ink;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            var layout = new Grid { Margin = new Thickness(28) };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = new Border { Background = PanelPalette.Surface, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = layout } };
            PanelTheme.Apply(this, Title, "", false);
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            layout.Children.Add(body);
            string portrait = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "杏菜Q版头像.png");
            var avatar = new Border { Width = 120, Height = 120, CornerRadius = new CornerRadius(60),
                Background = PanelPalette.Rail, HorizontalAlignment = HorizontalAlignment.Center };
            if(File.Exists(portrait)) {
                var image = BitmapFrame.Create(new Uri(portrait));
                RenderOptions.SetBitmapScalingMode(avatar, BitmapScalingMode.HighQuality);
                avatar.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            body.Children.Add(avatar);
            body.Children.Add(new TextBlock { Text = "八奈见杏菜", Foreground = PanelPalette.Ink, FontSize = 25, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,20,0,12) });
            duration = new TextBlock { Text = "00:00", Foreground = PanelPalette.Ink, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(duration);
            state = new TextBlock { Text = "准备好就拨给我吧", Foreground = PanelPalette.Quiet, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,12,0,0) };
            body.Children.Add(state);
            var captions = new StackPanel { Margin = new Thickness(0,15,0,24) };
            Grid.SetRow(captions,1); layout.Children.Add(captions);
            mine = Caption(""); anna = Caption("");
            mine.Foreground = PanelPalette.Quiet; anna.Foreground = PanelPalette.Ink;
            mine.TextAlignment = anna.TextAlignment = TextAlignment.Center;
            captions.Children.Add(mine); captions.Children.Add(anna);
            var footer = new StackPanel();
            Grid.SetRow(footer,2); layout.Children.Add(footer);
            meter = new ProgressBar { Minimum = 0, Maximum = 100, Height = 3, Foreground = PanelPalette.Accent, Background = PanelPalette.Line, BorderThickness = new Thickness(0), Margin = new Thickness(0,0,0,16) };
            footer.Children.Add(meter);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            footer.Children.Add(buttons);
            connect = PhoneButton("接通",false);
            mute = PhoneButton("静音",false); mute.IsEnabled = false;
            var stop = PhoneButton("打断",false);
            var hangup = PhoneButton("挂断",true);
            buttons.Children.Add(connect); buttons.Children.Add(mute); buttons.Children.Add(hangup);
            var options = new Expander { Header = "通话设置", Foreground = PanelPalette.Ink, Margin = new Thickness(0,18,0,0) };
            var controls = new StackPanel();
            interrupt = new CheckBox { Content = "耳机插话 · 说话时打断杏菜", Foreground = PanelPalette.Ink, Margin = new Thickness(0,8,0,8) };
            controls.Children.Add(interrupt); controls.Children.Add(stop);
            controls.Children.Add(new TextBlock { Text = "外放请关闭插话。静音或挂断会停止录音。", Foreground = PanelPalette.Quiet, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
            options.Content = controls; footer.Children.Add(options);
            connect.Click += async delegate { await Connect(); };
            mute.Click += delegate
            {
                muted = !muted; mute.Content = muted ? "取消静音" : "静音";
                captureVersion++;
                audio.Reset();
                if (microphone != null) { microphone.Dispose(); microphone = null; }
                state.Text = muted ? "麦克风已静音" : "正在听你说话";
            };
            stop.Click += delegate { if (Active) { chat.PrepareVoiceInput(); speech.Stop(); audio.Reset(); state.Text = "已打断，请继续说"; } };
            hangup.Click += delegate { HangUp(); };
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += Tick;
            speech.StatusChanged += SpeechStatus;
            Closing += delegate { HangUp(); closed = true; speech.StatusChanged -= SpeechStatus; };

        }

        private static Button PhoneButton(string text,bool hangup)
        {
            var button = new Button { Content = text, Width = 78, Height = 38, Margin = new Thickness(4,0,4,0),
                Foreground = Brushes.White, Background = hangup ? new SolidColorBrush(Color.FromRgb(173,58,79)) : PanelPalette.Action,
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            var template = new ControlTemplate(typeof(Button));
            var circle = new FrameworkElementFactory(typeof(Border));
            circle.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
            circle.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Button.BackgroundProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            circle.AppendChild(content); template.VisualTree = circle; button.Template = template;
            return button;
        }

        private static TextBlock Caption(string text)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, MaxHeight = 140, Margin = new Thickness(0,14,0,4) };
        }

        private async Task Connect()
        {
            if (Active || connecting || closed) return;
            if (!chat.CanAcceptVoice) { state.Text = "请先结束当前回复，并发送或清空聊天草稿。"; return; }
            if (!speech.Available) { state.Text = "请先在日语配音设置里完成音色配置。"; return; }
            int version = ++session;
            connecting = true; connect.IsEnabled = false;
            state.Text = "正在准备本地语音识别……";
            try
            {
                if (Program.Smoke) throw new InvalidOperationException("自检不连接麦克风。");
                await recognizer.ReadyAsync();
                if (closed || version != session) return;
                VoiceInputSettings.Load();
                microphone = new Microphone(); microphone.Start(LocalAsr.Device);
                Active = true; began = DateTime.UtcNow; muted = false;
                connect.Content = "通话中"; mute.IsEnabled = true; mute.Content = "静音";
                state.Text = "正在听你说话"; timer.Start();
            }
            catch (Exception error)
            {
                if (version == session)
                {
                    if (microphone != null) { microphone.Dispose(); microphone = null; }
                    if (!closed) state.Text = error.Message;
                }
            }
            finally { if (version == session) { connecting = false; if (!closed) connect.IsEnabled = !Active; } }
        }

        private void Tick(object sender, EventArgs args)
        {
            if (!Active || closed) return;
            try
            {
                TimeSpan elapsed = DateTime.UtcNow - began;
                duration.Text = ((int)elapsed.TotalMinutes).ToString("00") + ":" + elapsed.Seconds.ToString("00");
                if (!String.IsNullOrWhiteSpace(chat.CallAssistantText)) anna.Text = "杏菜  " + chat.CallAssistantText;
                if (muted) { meter.Value = 0; return; }
                if (microphone == null) { microphone = new Microphone(); microphone.Start(LocalAsr.Device); }
                meter.Value = microphone.Level * 100;
                byte[] pcm = microphone.TakePcm();
                bool responding = !chat.CanAcceptVoice || speech.IsBusy;
                if (responding && interrupt.IsChecked != true) listenAfter = DateTime.UtcNow.AddMilliseconds(500);
                if (processing || (interrupt.IsChecked != true && DateTime.UtcNow < listenAfter)) { audio.Reset(); return; }
                byte[] segment = audio.Feed(pcm);
                if (interrupt.IsChecked == true && (audio.HasSpeech || segment != null) && !interrupted)
                {
                    if (!chat.PrepareVoiceInput()) { audio.Reset(); return; }
                    speech.Stop(); interrupted = true;
                }
                if (segment != null) { interrupted = false; Process(segment, session); }
                else if (!responding && !audio.HasSpeech) state.Text = "正在听你说话";
            }
            catch (Exception error) { HangUp(); state.Text = error.Message; }
        }

        private async void Process(byte[] pcm, int version)
        {
            processing = true;
            int ownedTurn = ++turn;
            int capture = captureVersion;
            string path = Path.Combine(Program.DataDir, "call-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                state.Text = "正在识别……";
                Directory.CreateDirectory(Program.DataDir); CallAudio.SaveWave(path, pcm);
                string text = await recognizer.Recognize(path);
                if (!Active || closed || version != session || capture != captureVersion || muted) return;
                if (String.IsNullOrWhiteSpace(text)) { state.Text = "没有听清，请再说一次"; return; }
                mine.Text = "你  " + text;
                if (!chat.CanAcceptVoice) { state.Text = "聊天输入框还有草稿，请先发送或清空。"; return; }
                state.Text = "杏菜正在回复……";
                // Capture may resume while the model responds so headset users can interrupt.
                processing = false;
                await chat.SubmitCallAsync(text);
                if (Active && version == session) state.Text = chat.CallStatus;
            }
            catch (Exception error) { if (!closed && Active && version == session) state.Text = error.Message; }
            finally
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (version == session && ownedTurn == turn) processing = false;
            }
        }

        internal void HangUp()
        {
            bool hadCall = Active || connecting;
            Active = false; session++; processing = false; connecting = false; timer.Stop(); audio.Reset();
            if (microphone != null) { microphone.Dispose(); microphone = null; }
            if (hadCall) { chat.PrepareVoiceInput(); speech.Stop(); }
            meter.Value = 0; mute.IsEnabled = false; connect.IsEnabled = true; connect.Content = "重新拨打";
            state.Text = "通话已结束";
        }

        private void SpeechStatus(string message)
        {
            if (Active && !closed) state.Text = muted ? "麦克风已静音 · " + message : message;
        }
    }
}
