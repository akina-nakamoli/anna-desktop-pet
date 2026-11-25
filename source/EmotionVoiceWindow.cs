using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AnnaDesktopPet
{
    internal sealed class EmotionVoiceWindow : Window
    {
        private readonly TextBox[] paths = new TextBox[3], transcripts = new TextBox[3];
        private readonly string[] ids = { "calm", "happy", "tease" };
        private readonly SpeechService voice;
        private readonly SpeechSettings current;
        internal EmotionVoiceWindow(SpeechService service, SpeechSettings settings)
        {
            voice = service; current = settings;
            Title = "杏菜 · 情绪配音"; Width = 505; Height = 610; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var body = new StackPanel { Margin = new Thickness(18) }; Content = body;
            body.Children.Add(GptUi.Text("为每种语气选择参考音频，并填写对应的日语原文。留空时沿用默认样音。", 12, GptChatWindow.PanelQuiet));
            var saved = VoicePerformance.Load();
            string[] names = { "平静", "开心", "轻微吐槽" };
            for (int i = 0; i < ids.Length; i++)
            {
                int at = i;
                if (i > 0) body.Children.Add(new Border { Height = 1, Background = GptUi.Brush(218, 232, 241), Margin = new Thickness(0,14,0,0) });
                body.Children.Add(new TextBlock { Text = names[i], FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,14,0,4) });
                var sourceNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = GptChatWindow.PanelQuiet, FontSize = 11, Margin = new Thickness(0,0,0,8) };
                body.Children.Add(sourceNote);
                body.Children.Add(new TextBlock { Text = "参考 WAV", FontSize = 11 });
                var row = new DockPanel();
                var browse = new Button { Content = "选择 WAV", Margin = new Thickness(6,0,0,0) };
                DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
                paths[i] = new TextBox { ToolTip = "情绪参考样音路径" }; row.Children.Add(paths[i]); body.Children.Add(row);
                body.Children.Add(new TextBlock { Text = "音频中的日语原文", FontSize = 11, Margin = new Thickness(0,6,0,0) });
                transcripts[i] = new TextBox { ToolTip = "样音中的准确日语原文", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,6) };
                body.Children.Add(transcripts[i]);
                string[] profile;
                if (saved.TryGetValue(ids[i], out profile) && profile != null && profile.Length == 2)
                { paths[i].Text = profile[0]; transcripts[i].Text = profile[1]; }
                else { paths[i].Text = current.ReferenceAudio; transcripts[i].Text = current.ReferenceText; }
                Action refresh = delegate
                {
                    bool defaultVoice = String.IsNullOrWhiteSpace(paths[at].Text) ||
                        (String.Equals(paths[at].Text.Trim(), current.ReferenceAudio, StringComparison.OrdinalIgnoreCase) &&
                         transcripts[at].Text.Trim() == current.ReferenceText.Trim());
                    sourceNote.Text = defaultVoice ? (at == 0 ? "当前使用默认参考样音。" : "当前共用默认样音，尚未配置独立情绪录音。") : "使用你选择的样音，请确认日语原文与音频一致。";
                };
                paths[i].TextChanged += delegate { refresh(); };
                transcripts[i].TextChanged += delegate { refresh(); };
                refresh();
                browse.Click += delegate
                {
                    var picker = new Microsoft.Win32.OpenFileDialog { Filter = "WAV 音频|*.wav", CheckFileExists = true };
                    if (picker.ShowDialog(this) == true) paths[at].Text = picker.FileName;
                };
                var preview = new Button { Content = "试听" + names[i], HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 80 };
                preview.Click += delegate
                {
                    string audio = String.IsNullOrWhiteSpace(paths[at].Text) ? current.ReferenceAudio : paths[at].Text.Trim();
                    string reference = String.IsNullOrWhiteSpace(paths[at].Text) ? current.ReferenceText : transcripts[at].Text.Trim();
                    voice.ConfigureBackend("gpt-sovits", current.Endpoint, audio, reference);
                    voice.Configure(current.VoiceId, current.Rate + (at == 1 ? 1 : at == 2 ? -1 : 0), current.Volume);
                    voice.Speak(new[] { "そうなんだ。もう少し聞かせて。", "やった！それ、楽しみだね。", "ちょっと、それは言いすぎじゃない？" }[at]);
                };
                body.Children.Add(preview);
            }
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,8) };
            body.Children.Add(status);
            Action<string> update = delegate(string value) { status.Text = value; };
            voice.StatusChanged += update;
            var save = new Button { Content = "保存情绪样音", Margin = new Thickness(0,8,0,0),
                Background = GptUi.Brush(50, 103, 151), Foreground = System.Windows.Media.Brushes.White };
            body.Children.Add(save);
            save.Click += delegate
            {
                try
                {
                    var profiles = new Dictionary<string, string[]>();
                    for (int i = 0; i < ids.Length; i++)
                        if ((!String.IsNullOrWhiteSpace(paths[i].Text) || !String.IsNullOrWhiteSpace(transcripts[i].Text)) &&
                            !(String.Equals(paths[i].Text.Trim(), current.ReferenceAudio, StringComparison.OrdinalIgnoreCase) &&
                              transcripts[i].Text.Trim() == current.ReferenceText.Trim()))
                            profiles[ids[i]] = new[] { paths[i].Text.Trim(), transcripts[i].Text.Trim() };
                    VoicePerformance.Save(profiles); status.Text = profiles.Count == 0 ? "已保存，三种语气暂时共用当前默认样音。" : "已保存，自定义样音会用于对应语气，其余使用默认样音。";
                }
                catch (Exception error) { status.Text = error.Message; }
            };
            Closed += delegate
            {
                voice.StatusChanged -= update; voice.Stop();
                voice.ConfigureBackend(current.Engine, current.Endpoint, current.ReferenceAudio, current.ReferenceText);
                voice.Configure(current.VoiceId, current.Rate, current.Volume);
            };
            PanelTheme.Apply(this, "情绪配音", "VOICE EXPRESSION");
        }
    }
}
