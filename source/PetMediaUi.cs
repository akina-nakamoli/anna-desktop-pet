using System.Threading;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed class PetMediaWindow : Window
    {
        private readonly SpeechService voice;
        private readonly SpeechSettings settings;
        private readonly Action apply;
        private readonly Action<string> play;
        private readonly TextBox endpoint, referenceAudio, referenceText;
        private readonly StackPanel cloneControls;
        private readonly Button sample;
        private readonly Slider rate, volume;
        private readonly CheckBox autoRead, localRead, animate;
        private readonly TextBlock status, poseLabel;
        private readonly Image poseImage;
        private bool closed;
        private CancellationTokenSource referenceSetup;
        private readonly TextBlock referenceSetupStatus;

        internal PetMediaWindow(SpeechService service, SpeechSettings current, Action applySettings, Action<string> playMotion, Image petImage)
        {
            voice = service; settings = current; apply = applySettings; play = playMotion;
            Title = "日语配音与动作 · 杏菜"; ShowInTaskbar = true;
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(790, Math.Max(450, area.Width - 30)); Height = Math.Min(740, Math.Max(400, area.Height - 30));
            MinWidth = Math.Min(360, Width); MinHeight = Math.Min(420, Height); WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel body = new StackPanel { Margin = new Thickness(0, 0, 5, 2) }; scroll.Content = body; Content = scroll;

            Grid columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            Border speechCard = Card(); speechCard.Margin = new Thickness(0, 0, 14, 0); StackPanel controls = new StackPanel(); speechCard.Child = controls; columns.Children.Add(speechCard);
            TextBlock speechHeading = GptUi.Text("日语配音来源", 16, GptChatWindow.PanelText); speechHeading.FontWeight = FontWeights.SemiBold; controls.Children.Add(speechHeading);
            controls.Children.Add(GptUi.Text("杏菜参考音色 · 本地 GPT-SoVITS", 14, GptChatWindow.PanelText));
            cloneControls = new StackPanel(); controls.Children.Add(cloneControls);
            Button configureReference = StyledButton("配置杏菜参考音色", ConfigureReference, true);
            cloneControls.Children.Add(configureReference);
            referenceSetupStatus = GptUi.Text(System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voice-engine", "GPT-SoVITS", "anna-japanese-runtime.txt")) ? "已内置杏菜日语配音，打开桌宠时会自动加载。可点击「生成并试听」检查声音。" : "首次使用请运行发布包里的「首次安装杏菜配音.cmd」。已有引擎可在这里选择 api_v2.py。", 11, GptUi.Muted);
            referenceSetupStatus.TextWrapping = TextWrapping.Wrap;
            referenceSetupStatus.Margin = new Thickness(0, 6, 0, 5);
            cloneControls.Children.Add(referenceSetupStatus);
            Label(cloneControls, "本地音色服务地址"); endpoint = Input(settings.Endpoint); cloneControls.Children.Add(endpoint);
            Label(cloneControls, "参考音频（WAV，单人日语对白）");
            DockPanel audioRow = new DockPanel(); Button browse = StyledButton("选择", BrowseReference, false); browse.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); audioRow.Children.Add(browse);
            referenceAudio = Input(settings.ReferenceAudio); audioRow.Children.Add(referenceAudio); cloneControls.Children.Add(audioRow);
            Label(cloneControls, "参考音频中的日语原文"); referenceText = Input(settings.ReferenceText); referenceText.AcceptsReturn = true; referenceText.TextWrapping = TextWrapping.Wrap; referenceText.MinHeight = 65; cloneControls.Children.Add(referenceText);
            TextBlock cloneNote = GptUi.Text("先启动本地音色服务，再点击试听。配套样音为处理后的候选，仍需试听核对；参考台词须与音频一致。", 11, GptChatWindow.PanelQuiet); cloneNote.LineHeight = 18; cloneNote.Margin = new Thickness(0, 8, 0, 0); cloneControls.Children.Add(cloneNote);

            TextBlock rateLabel = GptUi.Text("语速  " + settings.Rate.ToString("+0;-0;0"), 12, GptChatWindow.PanelQuiet); rateLabel.Margin = new Thickness(0, 15, 0, 7); controls.Children.Add(rateLabel);
            rate = new Slider { Minimum = -5, Maximum = 5, TickFrequency = 1, IsSnapToTickEnabled = true, Value = settings.Rate }; rate.ValueChanged += delegate { rateLabel.Text = "语速  " + ((int)rate.Value).ToString("+0;-0;0"); }; controls.Children.Add(rate);
            TextBlock volumeLabel = GptUi.Text("音量  " + settings.Volume + "%", 12, GptChatWindow.PanelQuiet); volumeLabel.Margin = new Thickness(0, 13, 0, 7); controls.Children.Add(volumeLabel);
            volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = settings.Volume }; volume.ValueChanged += delegate { volumeLabel.Text = "音量  " + (int)volume.Value + "%"; }; controls.Children.Add(volume);
            autoRead = Check("自动用日语配音聊天回复", settings.AutoReadReplies); autoRead.Margin = new Thickness(0, 18, 0, 9); controls.Children.Add(autoRead);
            localRead = Check("互动台词日语配音", settings.ReadLocal); localRead.Margin = new Thickness(0, 0, 0, 16); controls.Children.Add(localRead);
            WrapPanel samples = new WrapPanel();
            sample = StyledButton("生成并试听", Sample, true); samples.Children.Add(sample);
            Button emotions = StyledButton("情绪样音", delegate
            {
                try { var editor = new EmotionVoiceWindow(voice, settings) { Owner = this }; editor.ShowDialog(); }
                catch (Exception error) { status.Text = error.Message; }
            }, false);
            emotions.Margin = new Thickness(8,0,0,0); samples.Children.Add(emotions);
            Button stop = StyledButton("停止配音", delegate { if (referenceSetup != null) referenceSetup.Cancel(); voice.Stop(); }, false); stop.Margin = new Thickness(8, 0, 0, 0); samples.Children.Add(stop); controls.Children.Add(samples);

            Border motionCard = Card(); Grid.SetColumn(motionCard, 1); columns.Children.Add(motionCard); StackPanel motionBody = new StackPanel(); motionCard.Child = motionBody;
            TextBlock motionHeading = GptUi.Text("动作预览", 16, GptChatWindow.PanelText); motionHeading.FontWeight = FontWeights.SemiBold; motionBody.Children.Add(motionHeading);
            Border stage = new Border { Background = PanelPalette.Rail, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(4, 6, 4, 4) };
            poseImage = new Image { Height = 205, Stretch = Stretch.Uniform };
            poseImage.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Source") { Source=petImage, Mode=System.Windows.Data.BindingMode.OneWay });
            RenderOptions.SetBitmapScalingMode(poseImage, BitmapScalingMode.HighQuality); stage.Child = poseImage; motionBody.Children.Add(stage);
            poseLabel = GptUi.Text("待机", 12, GptChatWindow.PanelQuiet); poseLabel.HorizontalAlignment = HorizontalAlignment.Center; poseLabel.Margin = new Thickness(0, 2, 0, 10); motionBody.Children.Add(poseLabel);
            animate = Check("开启动作与呼吸动画", settings.MotionsEnabled); animate.Margin = new Thickness(0, 0, 0, 9); motionBody.Children.Add(animate);
            WrapPanel poseButtons = new WrapPanel();
            AddPose(poseButtons, "思考", "think"); AddPose(poseButtons, "说话", "speak"); AddPose(poseButtons, "待机", "idle");
            motionBody.Children.Add(poseButtons);
            string motionHint = Appearance.IsGhostPortrait
                ? "右键开启「自主活动」，幽灵娘会在待机时和鬼火互动。单击桌宠可让她假装吓你。"
                : Appearance.Outfit == Appearance.Uniform2Portrait ? "开启「自主活动」，杏菜偶尔会偷瞄，安静久了会打瞌睡。你发出聊天消息时，她也会点头回应。"
                : "在右键菜单开启“自主活动”后，杏菜会在待机时偶尔做小动作。";
            TextBlock poseNote = GptUi.Text(motionHint, 11, GptChatWindow.PanelQuiet); poseNote.LineHeight = 18; poseNote.Margin = new Thickness(0, 10, 0, 0); motionBody.Children.Add(poseNote);
            columns.SizeChanged += delegate
            {
                bool narrow = columns.ActualWidth < 590;
                columns.ColumnDefinitions.Clear(); columns.RowDefinitions.Clear();
                if (narrow)
                {
                    columns.ColumnDefinitions.Add(new ColumnDefinition());
                    columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(speechCard, 0); Grid.SetRow(speechCard, 0); speechCard.Margin = new Thickness(0, 0, 0, 12);
                    Grid.SetColumn(motionCard, 0); Grid.SetRow(motionCard, 1);
                }
                else
                {
                    columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
                    columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetColumn(speechCard, 0); Grid.SetRow(speechCard, 0); speechCard.Margin = new Thickness(0, 0, 14, 0);
                    Grid.SetColumn(motionCard, 1); Grid.SetRow(motionCard, 0);
                }
            };
            body.Children.Add(columns);

            TextBlock explanation = GptUi.Text("参考音色在本机生成。开启回复配音后，聊天模型会同时生成中文回复和日语台词。", 11, GptChatWindow.PanelQuiet); explanation.LineHeight = 19; explanation.Margin = new Thickness(0, 15, 0, 12); body.Children.Add(explanation);
            Button save = StyledButton("保存日语配音与动作设置", Save, true); save.HorizontalAlignment = HorizontalAlignment.Stretch; body.Children.Add(save);
            status = GptUi.Text(voice.Status, 11, GptChatWindow.PanelQuiet); status.LineHeight = 18; status.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(status);
            sample.Content = "生成并试听";
            voice.StatusChanged += VoiceStatus;
            Closed += delegate { closed = true; if (referenceSetup != null) referenceSetup.Cancel(); voice.StatusChanged -= VoiceStatus; voice.Stop(); voice.ConfigureBackend(settings.Engine, settings.Endpoint, settings.ReferenceAudio, settings.ReferenceText); voice.Configure(settings.VoiceId, settings.Rate, settings.Volume); };
            PanelTheme.Apply(this, "日语配音与动作", "VOICE & MOTION", false);
        }

        private static Border Card() { return new Border { Background = PanelPalette.Surface, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(17) }; }
        private static Button StyledButton(string label, Action action, bool primary)
        {
            return GptChatWindow.ChatButton(label, action, primary);
        }
        private static CheckBox Check(string label, bool value) { return new CheckBox { Content = GptUi.Text(label, 12, GptChatWindow.PanelText), IsChecked = value }; }
        private static void Label(Panel panel, string text) { TextBlock label = GptUi.Text(text, 11, GptChatWindow.PanelQuiet); label.Margin = new Thickness(0, 9, 0, 6); panel.Children.Add(label); }
        private static TextBox Input(string value) { return new TextBox { Text = value ?? "", FontSize = 12, Padding = new Thickness(7), MinHeight = 32, BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1) }; }
        private async void ConfigureReference()
        {
            if (Program.Smoke || referenceSetup != null) return;
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 GPT-SoVITS 文件夹中的 api_v2.py",
                Filter = "GPT-SoVITS API (api_v2.py)|api_v2.py",
                FileName = "api_v2.py",
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) != true) return;
            string directory = System.IO.Path.GetDirectoryName(picker.FileName);
            var defaults = new SpeechSettings();
            string address = endpoint.Text.Trim();
            var cancellation = new CancellationTokenSource();
            referenceSetup = cancellation;
            cloneControls.IsEnabled = sample.IsEnabled = false;
            bool saved = false;
            try
            {
                GptSovitsClient.ValidateConfiguration(address, defaults.ReferenceAudio, defaults.ReferenceText);
                Uri service = GptSovitsClient.TtsUri(address);
                if (service.Scheme != "http" || (service.Host != "127.0.0.1" && service.Host != "localhost") || service.Port < 1024)
                    throw new InvalidOperationException("自动配置请使用 http://127.0.0.1:9880，或填写同一地址的其他本地端口（1024–65535）。");
                int port = service.Port;
                referenceSetupStatus.Text = "正在检查整合包和模型，然后启动本地音色服务……";
                await VoiceAutoStart.PrepareReferenceAsync(directory, port, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                referenceSetupStatus.Text = "服务已连接，正在用随包的杏菜样音合成试听……";
                voice.ConfigureBackend("gpt-sovits", address, defaults.ReferenceAudio, defaults.ReferenceText);
                voice.Configure("", (int)rate.Value, (int)volume.Value);
                bool played = await voice.TryReferenceSampleAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!played) throw new InvalidOperationException(voice.Status);

                VoiceAutoStart.RememberReferenceEngine(directory);
                var chosen = ReadSettings();
                chosen.ReferenceAudio = defaults.ReferenceAudio;
                chosen.ReferenceText = defaults.ReferenceText;
                chosen.AutoReadReplies = true;
                SaveSettings(chosen);
                saved = true;
                referenceAudio.Text = chosen.ReferenceAudio;
                referenceText.Text = chosen.ReferenceText;
                autoRead.IsChecked = true;
                referenceSetupStatus.Text = "试听已完成，杏菜参考音色已用于聊天回复。";
            }
            catch (OperationCanceledException) { if (!closed) referenceSetupStatus.Text = "配置已取消。"; }
            catch (Exception error) { if (!closed) referenceSetupStatus.Text = error.Message; }
            finally
            {
                referenceSetup = null;
                cancellation.Dispose();
                if (!closed)
                {
                    cloneControls.IsEnabled = true;
                    sample.IsEnabled = true;
                    if (!saved) apply();
                }
            }
        }
        private SpeechSettings ReadSettings()
        {
            return new SpeechSettings { Engine = "gpt-sovits", Endpoint = endpoint.Text.Trim(), ReferenceAudio = referenceAudio.Text.Trim(), ReferenceText = referenceText.Text.Trim().Replace("\r", " ").Replace("\n", " "), VoiceId = "", VoiceLanguage = "ja", Rate = (int)rate.Value, Volume = (int)volume.Value, AutoReadReplies = autoRead.IsChecked == true, ReadLocal = localRead.IsChecked == true, MotionsEnabled = animate.IsChecked == true, AutonomousEnabled = settings.AutonomousEnabled };
        }

        private void SaveSettings(SpeechSettings chosen)
        {
            if (chosen.Engine == "gpt-sovits")
            {
                GptSovitsClient.TtsUri(chosen.Endpoint);
                if (chosen.AutoReadReplies || chosen.ReadLocal) GptSovitsClient.ValidateConfiguration(chosen.Endpoint, chosen.ReferenceAudio, chosen.ReferenceText);
            }
            chosen.Save();
            settings.Engine = chosen.Engine; settings.Endpoint = chosen.Endpoint; settings.ReferenceAudio = chosen.ReferenceAudio; settings.ReferenceText = chosen.ReferenceText;
            settings.VoiceId = chosen.VoiceId; settings.Rate = chosen.Rate; settings.Volume = chosen.Volume;
            settings.VoiceLanguage = "ja";
            settings.AutoReadReplies = chosen.AutoReadReplies; settings.ReadLocal = chosen.ReadLocal; settings.MotionsEnabled = chosen.MotionsEnabled;
            apply();
        }
        private void BrowseReference()
        {
            Microsoft.Win32.OpenFileDialog picker = new Microsoft.Win32.OpenFileDialog { Title = "选择日语参考音频", Filter = "WAV 音频 (*.wav)|*.wav", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) referenceAudio.Text = picker.FileName;
        }
        private void AddPose(WrapPanel buttons, string label, string pose)
        {
            Button button = StyledButton(label, delegate
            {
                poseLabel.Text = label; play(pose);
                if (!settings.MotionsEnabled) status.Text = "动作尚未开启。勾选「开启动作与呼吸动画」并保存后，桌宠会跟随切换。";
            }, false); button.FontSize = 11; button.Padding = new Thickness(8, 6, 8, 6); button.Margin = new Thickness(0, 0, 5, 5); buttons.Children.Add(button);
        }
        private void Sample()
        {
            if (Program.Smoke || referenceSetup != null) return;
            voice.ConfigureBackend("gpt-sovits", endpoint.Text.Trim(), referenceAudio.Text.Trim(), referenceText.Text.Trim());
            voice.Configure("", (int)rate.Value, (int)volume.Value);
            voice.Speak("こんにちは、アンナだよ。今日も一緒に頑張ろうね。お腹がすいたら、一緒に何か食べようね。");
        }
        private void Save()
        {
            if (referenceSetup != null) return;
            SpeechSettings chosen = new SpeechSettings { Engine = "gpt-sovits", Endpoint = endpoint.Text.Trim(), ReferenceAudio = referenceAudio.Text.Trim(), ReferenceText = referenceText.Text.Trim().Replace("\r", " ").Replace("\n", " "), VoiceId = "", VoiceLanguage = "ja", Rate = (int)rate.Value, Volume = (int)volume.Value, AutoReadReplies = autoRead.IsChecked == true, ReadLocal = localRead.IsChecked == true, MotionsEnabled = animate.IsChecked == true };
            try
            {
                chosen.AutonomousEnabled = settings.AutonomousEnabled;
                if (chosen.Engine == "gpt-sovits")
                {
                    GptSovitsClient.TtsUri(chosen.Endpoint);
                    if (chosen.AutoReadReplies || chosen.ReadLocal) GptSovitsClient.ValidateConfiguration(chosen.Endpoint, chosen.ReferenceAudio, chosen.ReferenceText);
                }
                chosen.Save();
                settings.Engine = chosen.Engine; settings.Endpoint = chosen.Endpoint; settings.ReferenceAudio = chosen.ReferenceAudio; settings.ReferenceText = chosen.ReferenceText;
                settings.VoiceId = chosen.VoiceId; settings.Rate = chosen.Rate; settings.Volume = chosen.Volume;
                settings.VoiceLanguage = "ja";
                settings.AutoReadReplies = chosen.AutoReadReplies; settings.ReadLocal = chosen.ReadLocal; settings.MotionsEnabled = chosen.MotionsEnabled;
                apply(); status.Text = chosen.Engine == "gpt-sovits" ? (voice.Available ? "已保存。请启动本地音色服务后点击「生成并试听」。服务未响应时会显示提示。" : "已保存选项。参考音色还需选择干净样音并填写对应日语台词。") : "已保存日语声线设置。";
            }
            catch (InvalidOperationException error) { status.Text = error.Message; }
            catch { status.Text = "保存失败，请检查当前用户配置文件夹是否可写。"; }
        }
        private void VoiceStatus(string text) { if (!closed) status.Text = text; }
        internal void VerifySmoke()
        {
            if (poseImage.Source == null || autoRead == null || localRead == null || animate == null) throw new InvalidOperationException("语音与动作界面初始化失败。");
            if (voice.IsSpeaking) throw new InvalidOperationException("Smoke 不应播放音频。");
        }
    }
}
