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
        private readonly MotionController motions;
        private readonly ComboBox voices;
        private readonly ComboBox engine;
        private readonly TextBox endpoint, referenceAudio, referenceText;
        private readonly StackPanel cloneControls, windowsControls;
        private readonly Button sample;
        private readonly Slider rate, volume;
        private readonly CheckBox autoRead, localRead, animate;
        private readonly TextBlock status, poseLabel;
        private readonly Image poseImage;
        private bool closed;

        internal PetMediaWindow(SpeechService service, SpeechSettings current, Action applySettings, Action<string> playMotion, MotionController motionImages)
        {
            voice = service; settings = current; apply = applySettings; play = playMotion; motions = motionImages;
            Title = "日语配音与动作 · 杏菜"; ShowInTaskbar = true;
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(790, Math.Max(450, area.Width - 30)); Height = Math.Min(740, Math.Max(400, area.Height - 30));
            MinWidth = Math.Min(560, Width); MinHeight = Math.Min(480, Height); WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); Background = GptUi.Brush(238, 245, 252);
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel body = new StackPanel { Margin = new Thickness(23, 20, 23, 20) }; scroll.Content = body; Content = scroll;
            Border hero = new Border { Background = GptUi.Brush(30, 55, 90), CornerRadius = new CornerRadius(16), Padding = new Thickness(19, 15, 19, 16), Margin = new Thickness(0, 0, 0, 16) };
            StackPanel heroBody = new StackPanel(); hero.Child = heroBody;
            TextBlock tag = GptUi.Text("ANNA YANAMI  /  VOICE & MOTION", 10, GptUi.Brush(249, 210, 113)); tag.FontWeight = FontWeights.Bold; heroBody.Children.Add(tag);
            TextBlock title = GptUi.Text("让杏菜用日语陪你说话", 22, Brushes.White); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 7, 0, 0); heroBody.Children.Add(title);
            TextBlock subtitle = GptUi.Text("中文聊天，日语配音，和杏菜一起度过每一天。", 12, GptUi.Brush(209, 229, 247)); subtitle.Margin = new Thickness(0, 6, 0, 0); heroBody.Children.Add(subtitle);
            Border ribbon = new Border { Background = GptUi.Brush(246, 200, 91), Height = 4, Width = 44, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }; heroBody.Children.Add(ribbon); body.Children.Add(hero);

            Grid columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            Border speechCard = Card(); speechCard.Margin = new Thickness(0, 0, 14, 0); StackPanel controls = new StackPanel(); speechCard.Child = controls; columns.Children.Add(speechCard);
            TextBlock speechHeading = GptUi.Text("日语配音来源", 16, GptUi.Ink); speechHeading.FontWeight = FontWeights.SemiBold; controls.Children.Add(speechHeading);
            engine = new ComboBox { FontSize = 12, Padding = new Thickness(7), MinHeight = 34, Margin = new Thickness(0, 12, 0, 10) };
            engine.Items.Add(new ComboBoxItem { Content = "参考音色 · 本地 GPT-SoVITS", Tag = "gpt-sovits" });
            engine.Items.Add(new ComboBoxItem { Content = "Windows 日语声线 · 系统合成", Tag = "windows" });
            engine.SelectedIndex = settings.Engine == "windows" ? 1 : 0; controls.Children.Add(engine);
            cloneControls = new StackPanel(); controls.Children.Add(cloneControls);
            Label(cloneControls, "本地音色服务地址"); endpoint = Input(settings.Endpoint); cloneControls.Children.Add(endpoint);
            Label(cloneControls, "参考音频（WAV，单人日语对白）");
            DockPanel audioRow = new DockPanel(); Button browse = StyledButton("选择", BrowseReference, false); browse.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); audioRow.Children.Add(browse);
            referenceAudio = Input(settings.ReferenceAudio); audioRow.Children.Add(referenceAudio); cloneControls.Children.Add(audioRow);
            Label(cloneControls, "参考音频中的日语原文"); referenceText = Input(settings.ReferenceText); referenceText.AcceptsReturn = true; referenceText.TextWrapping = TextWrapping.Wrap; referenceText.MinHeight = 65; cloneControls.Children.Add(referenceText);
            TextBlock cloneNote = GptUi.Text("先启动本地音色服务，再点击试听。配套样音为处理后的候选，仍需试听核对；参考台词须与音频一致。", 11, GptUi.Muted); cloneNote.LineHeight = 18; cloneNote.Margin = new Thickness(0, 8, 0, 0); cloneControls.Children.Add(cloneNote);
            windowsControls = new StackPanel(); controls.Children.Add(windowsControls);
            TextBlock voiceHint = GptUi.Text("日语语音", 12, GptUi.Muted); voiceHint.Margin = new Thickness(0, 7, 0, 7); windowsControls.Children.Add(voiceHint);
            voices = new ComboBox { FontSize = 12, Padding = new Thickness(7), MinHeight = 34 };
            foreach (SpeechVoiceInfo v in voice.Voices) if ((v.Language ?? "").StartsWith("ja", StringComparison.OrdinalIgnoreCase)) voices.Items.Add(new ComboBoxItem { Content = v.Name + "  ·  " + v.Language, Tag = v.Id });
            if (voices.Items.Count == 0) voices.Items.Add(new ComboBoxItem { Content = "未找到 Windows 日语语音", Tag = "" });
            voices.SelectedIndex = 0;
            foreach (ComboBoxItem item in voices.Items) if ((string)item.Tag == voice.SelectedVoiceId) { voices.SelectedItem = item; break; }
            voices.IsEnabled = voice.Voices.Count > 0; windowsControls.Children.Add(voices);
            TextBlock installedNote = GptUi.Text(voice.Voices.Count == 0 ? "未找到可用的 Windows 日语语音。" : "此选项使用系统合成声线，不使用参考音频。", 11, GptUi.Muted); installedNote.LineHeight = 18; installedNote.Margin = new Thickness(0, 8, 0, 0); windowsControls.Children.Add(installedNote);

            TextBlock rateLabel = GptUi.Text("语速  " + settings.Rate.ToString("+0;-0;0"), 12, GptUi.Muted); rateLabel.Margin = new Thickness(0, 15, 0, 7); controls.Children.Add(rateLabel);
            rate = new Slider { Minimum = -5, Maximum = 5, TickFrequency = 1, IsSnapToTickEnabled = true, Value = settings.Rate }; rate.ValueChanged += delegate { rateLabel.Text = "语速  " + ((int)rate.Value).ToString("+0;-0;0"); }; controls.Children.Add(rate);
            TextBlock volumeLabel = GptUi.Text("音量  " + settings.Volume + "%", 12, GptUi.Muted); volumeLabel.Margin = new Thickness(0, 13, 0, 7); controls.Children.Add(volumeLabel);
            volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = settings.Volume }; volume.ValueChanged += delegate { volumeLabel.Text = "音量  " + (int)volume.Value + "%"; }; controls.Children.Add(volume);
            autoRead = Check("自动用日语配音 GPT 回复", settings.AutoReadReplies); autoRead.Margin = new Thickness(0, 18, 0, 9); controls.Children.Add(autoRead);
            localRead = Check("互动台词日语配音", settings.ReadLocal); localRead.Margin = new Thickness(0, 0, 0, 16); controls.Children.Add(localRead);
            StackPanel samples = new StackPanel { Orientation = Orientation.Horizontal };
            sample = StyledButton("生成并试听", Sample, true); samples.Children.Add(sample);
            Button stop = StyledButton("停止配音", delegate { voice.Stop(); }, false); stop.Margin = new Thickness(8, 0, 0, 0); samples.Children.Add(stop); controls.Children.Add(samples);

            Border motionCard = Card(); Grid.SetColumn(motionCard, 1); columns.Children.Add(motionCard); StackPanel motionBody = new StackPanel(); motionCard.Child = motionBody;
            TextBlock motionHeading = GptUi.Text("动作预览", 16, GptUi.Ink); motionHeading.FontWeight = FontWeights.SemiBold; motionBody.Children.Add(motionHeading);
            Border stage = new Border { Background = GptUi.Brush(226, 239, 250), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(4, 6, 4, 4) };
            poseImage = new Image { Source = motions.Get("idle").Image, Height = 205, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(poseImage, BitmapScalingMode.HighQuality); stage.Child = poseImage; motionBody.Children.Add(stage);
            poseLabel = GptUi.Text("待机", 12, GptUi.Muted); poseLabel.HorizontalAlignment = HorizontalAlignment.Center; poseLabel.Margin = new Thickness(0, 2, 0, 10); motionBody.Children.Add(poseLabel);
            animate = Check("开启动作与呼吸动画", settings.MotionsEnabled); animate.Margin = new Thickness(0, 0, 0, 9); motionBody.Children.Add(animate);
            WrapPanel poseButtons = new WrapPanel();
            AddPose(poseButtons, "思考", "think"); AddPose(poseButtons, "说话", "speak"); AddPose(poseButtons, "待机", "idle");
            motionBody.Children.Add(poseButtons);
            TextBlock poseNote = GptUi.Text(motions.HasPoses ? "思考会偶尔自主出现；聊天和朗读时也会显示对应状态。" : "动作素材未加载，当前使用待机形象与动态效果。", 11, GptUi.Muted); poseNote.LineHeight = 18; poseNote.Margin = new Thickness(0, 10, 0, 0); motionBody.Children.Add(poseNote);
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

            TextBlock explanation = GptUi.Text("参考音色在本机生成，无需语音 API Key。GPT 聊天仍需 OpenAI API Key；启用日语配音会同时生成中文回复和日语台词。保存设置不会发出语音请求。", 11, GptUi.Muted); explanation.LineHeight = 19; explanation.Margin = new Thickness(0, 15, 0, 12); body.Children.Add(explanation);
            Button save = StyledButton("保存日语配音与动作设置", Save, true); save.HorizontalAlignment = HorizontalAlignment.Stretch; body.Children.Add(save);
            status = GptUi.Text(voice.Status, 11, GptUi.Muted); status.LineHeight = 18; status.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(status);
            engine.SelectionChanged += delegate { UpdateEngineControls(); }; UpdateEngineControls();
            voice.StatusChanged += VoiceStatus;
            Closed += delegate { closed = true; voice.StatusChanged -= VoiceStatus; voice.Stop(); voice.ConfigureBackend(settings.Engine, settings.Endpoint, settings.ReferenceAudio, settings.ReferenceText); voice.Configure(settings.VoiceId, settings.Rate, settings.Volume); };
        }

        private static Border Card() { return new Border { Background = Brushes.White, BorderBrush = GptUi.Brush(218, 231, 242), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(17) }; }
        private static Button StyledButton(string label, Action action, bool primary)
        {
            Button button = GptUi.Button(label, action, primary); button.FontWeight = FontWeights.SemiBold;
            button.Background = primary ? GptUi.Brush(50, 103, 151) : GptUi.Brush(245, 249, 253);
            button.Foreground = primary ? Brushes.White : GptUi.Brush(42, 79, 113); button.BorderBrush = primary ? GptUi.Brush(50, 103, 151) : GptUi.Brush(207, 224, 238);
            ControlTemplate template = new ControlTemplate(typeof(Button)); FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.Name = "buttonChrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); template.VisualTree = border;
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.OpacityProperty, 0.84, "buttonChrome")); template.Triggers.Add(hover);
            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "buttonChrome")); template.Triggers.Add(focus);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(Border.OpacityProperty, 0.48, "buttonChrome")); template.Triggers.Add(disabled); button.Template = template; return button;
        }
        private static CheckBox Check(string label, bool value) { return new CheckBox { Content = GptUi.Text(label, 12, GptUi.Ink), IsChecked = value }; }
        private static void Label(Panel panel, string text) { TextBlock label = GptUi.Text(text, 11, GptUi.Muted); label.Margin = new Thickness(0, 9, 0, 6); panel.Children.Add(label); }
        private static TextBox Input(string value) { return new TextBox { Text = value ?? "", FontSize = 12, Padding = new Thickness(7), MinHeight = 32, BorderBrush = GptUi.Brush(202, 220, 236), BorderThickness = new Thickness(1) }; }
        private string ChosenEngine() { return engine.SelectedIndex == 1 ? "windows" : "gpt-sovits"; }
        private void UpdateEngineControls()
        {
            bool clone = ChosenEngine() == "gpt-sovits";
            cloneControls.Visibility = clone ? Visibility.Visible : Visibility.Collapsed;
            windowsControls.Visibility = clone ? Visibility.Collapsed : Visibility.Visible;
            sample.IsEnabled = clone || voice.Voices.Count > 0;
            sample.Content = clone ? "生成并试听" : "试听系统声音";
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
                poseImage.Source = motions.Get(pose).Image; poseLabel.Text = label; play(pose);
                if (!settings.MotionsEnabled) status.Text = "动作尚未开启。勾选「开启动作与呼吸动画」并保存后，桌宠会跟随切换。";
            }, false); button.FontSize = 11; button.Padding = new Thickness(8, 6, 8, 6); button.Margin = new Thickness(0, 0, 5, 5); buttons.Children.Add(button);
        }
        private string ChosenVoice() { ComboBoxItem item = voices.SelectedItem as ComboBoxItem; return item == null ? "" : (string)item.Tag; }
        private void Sample()
        {
            if (Program.Smoke) return;
            voice.ConfigureBackend(ChosenEngine(), endpoint.Text.Trim(), referenceAudio.Text.Trim(), referenceText.Text.Trim());
            voice.Configure(ChosenVoice(), (int)rate.Value, (int)volume.Value);
            voice.Speak("こんにちは、アンナだよ。今日も一緒に頑張ろうね。お腹がすいたら、一緒に何か食べようね。");
        }
        private void Save()
        {
            SpeechSettings chosen = new SpeechSettings { Engine = ChosenEngine(), Endpoint = endpoint.Text.Trim(), ReferenceAudio = referenceAudio.Text.Trim(), ReferenceText = referenceText.Text.Trim().Replace("\r", " ").Replace("\n", " "), VoiceId = ChosenVoice(), VoiceLanguage = "ja", Rate = (int)rate.Value, Volume = (int)volume.Value, AutoReadReplies = autoRead.IsChecked == true, ReadLocal = localRead.IsChecked == true, MotionsEnabled = animate.IsChecked == true };
            try
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
                apply(); status.Text = chosen.Engine == "gpt-sovits" ? (voice.Available ? "已保存。请启动本地音色服务后点击「生成并试听」。服务未响应时会显示提示。" : "已保存选项。参考音色还需选择干净样音并填写对应日语台词。") : "已保存 Windows 日语声线设置。";
            }
            catch (InvalidOperationException error) { status.Text = error.Message; }
            catch { status.Text = "保存失败，请检查当前用户配置文件夹是否可写。"; }
        }
        private void VoiceStatus(string text) { if (!closed) status.Text = text; }
        internal void VerifySmoke()
        {
            if (voices.Items.Count < 1 || poseImage.Source == null || autoRead == null || localRead == null || animate == null) throw new InvalidOperationException("语音与动作界面初始化失败。");
            if (engine.Items.Count != 2 || referenceText == null || endpoint == null || referenceAudio == null) throw new InvalidOperationException("参考音色设置初始化失败。");
            if (cloneControls.Visibility != (ChosenEngine() == "gpt-sovits" ? Visibility.Visible : Visibility.Collapsed)) throw new InvalidOperationException("配音来源切换失败。");
            int previous = engine.SelectedIndex; engine.SelectedIndex = 1;
            if (cloneControls.Visibility != Visibility.Collapsed || windowsControls.Visibility != Visibility.Visible) throw new InvalidOperationException("系统声线面板切换失败。");
            engine.SelectedIndex = 0;
            if (cloneControls.Visibility != Visibility.Visible || windowsControls.Visibility != Visibility.Collapsed || !sample.IsEnabled) throw new InvalidOperationException("参考音色面板切换失败。");
            engine.SelectedIndex = previous;
            if (voice.IsSpeaking) throw new InvalidOperationException("Smoke 不应播放音频。");
        }
    }
}
