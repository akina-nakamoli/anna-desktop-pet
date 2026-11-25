using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AnnaDesktopPet
{
    internal sealed partial class PetWindow
    {
        internal string CompanionName { get { return settings.Nickname; } }

        private void PlaceCompanionPanel(Window window)
        {
            Rect work = SystemParameters.WorkArea;
            if (PresentationSource.FromVisual(this) != null)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
                var bounds = screen.WorkingArea;
                Point a = PointFromScreen(new Point(bounds.Left, bounds.Top));
                Point b = PointFromScreen(new Point(bounds.Right, bounds.Bottom));
                work = new Rect(Left + a.X, Top + a.Y, b.X - a.X, b.Y - a.Y);
            }
            double availableWidth = Math.Max(1, work.Width - 16), availableHeight = Math.Max(1, work.Height - 16);
            window.MinWidth = Math.Min(window.MinWidth, availableWidth);
            window.MinHeight = Math.Min(window.MinHeight, availableHeight);
            window.Width = Math.Min(window.Width, availableWidth);
            window.Height = Math.Min(window.Height, availableHeight);
            double onLeft = Left - window.Width - 8, onRight = Left + ActualWidth + 8;
            bool left = onLeft >= work.Left + 8 || (onRight + window.Width > work.Right - 8 && Left - work.Left >= work.Right - Left - ActualWidth);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Math.Max(work.Left + 8, Math.Min(work.Right - window.Width - 8, left ? onLeft : onRight));
            window.Top = Math.Max(work.Top + 8, Math.Min(work.Bottom - window.Height - 8, Top + ActualHeight - window.Height));
        }

        internal FrameworkElement CreateCompanionSettingsPage(Action openModelSettings)
        {
            var body = new StackPanel { Margin = new Thickness(2, 1, 8, 12) };
            var general = SettingsGroup(body, "桌宠外观");
            var name = new TextBox {
                Text = settings.Nickname, MaxLength = 12, Width = 100, Padding = new Thickness(7, 4, 7, 4),
                VerticalContentAlignment = VerticalAlignment.Center, Foreground = SettingsInk, Background = PanelPalette.Surface,
                BorderBrush = PanelPalette.Line, BorderThickness = new Thickness(1)
            };
            AutomationProperties.SetName(name, "桌宠称呼");
            var edit = new StackPanel { Orientation = Orientation.Horizontal };
            edit.Children.Add(name);
            edit.Children.Add(SettingsButton("保存", delegate {
                string updated = name.Text.Trim();
                if (updated.Length == 0) { name.Text = settings.Nickname; return; }
                settings.Nickname = updated;
                settings.Save();
                if (chatWindow != null) chatWindow.SelectCompanionTab("settings");
                Say("好，那就叫我" + updated + "。", 4);
            }));
            SettingsRow(general, "称呼", edit);
            var size = new Slider { Minimum = 150, Maximum = 340, Value = settings.Size, Width = 134,
                TickFrequency = 10, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(size, "桌宠大小");
            size.ValueChanged += delegate { settings.Size = (int)size.Value; ApplySize(); SavePosition(); };
            SettingsRow(general, "桌宠大小", size);
            SettingsRow(general, "立绘与场景", SettingsButton("选择立绘", Appearance.Show));
            var daily = SettingsGroup(body, "陪伴与声音");
            SettingsRow(daily, "聊天模型", SettingsButton("模型连接", openModelSettings));
            SettingsRow(daily, "配音与动作", SettingsButton("配音设置", OpenMedia));
            SettingsRow(daily, "语音输入", SettingsButton("麦克风设置", OpenVoiceInput));
            SettingsRow(daily, "作息问候", SettingsButton("调整作息", delegate { routine.ShowSettings(); }));
            var focusControls = new StackPanel { Orientation = Orientation.Horizontal };
            focusControls.Children.Add(SettingsButton("设置时长", OpenFocusInput));
            focusControls.Children.Add(SettingsButton("结束", delegate { if (focusEnd.HasValue) { focusEnd = null; Say("休息一下吧。", 4); } }));
            SettingsRow(daily, "专注计时", focusControls);
            focusLabel = GptUi.Text(FocusText(), 11, SettingsMuted);
            focusLabel.Margin = new Thickness(0, 1, 0, 8);
            daily.Children.Add(focusLabel);
            var behavior = SettingsGroup(body, "桌面行为");
            var top = new CheckBox { Content = "保持置顶", IsChecked = settings.OnTop, Foreground = SettingsInk, Margin = new Thickness(0, 5, 0, 6) };
            top.Click += delegate { Topmost = settings.OnTop = top.IsChecked == true; settings.Save(); };
            behavior.Children.Add(top);
            var auto = new CheckBox { Content = "自主活动", IsChecked = mediaSettings.AutonomousEnabled, Foreground = SettingsInk, Margin = new Thickness(0, 4, 0, 3) };
            auto.Click += delegate { mediaSettings.AutonomousEnabled = auto.IsChecked == true; mediaSettings.Save(); autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); };
            behavior.Children.Add(auto);
            LoginStartup.AddSwitch(behavior);
            foreach (var child in behavior.Children) { var check = child as CheckBox; if (check != null) check.Foreground = SettingsInk; }
            var sleep = SettingsButton(settings.Sleeping ? "叫醒杏菜" : "让杏菜小憩", null);
            sleep.Click += delegate { SleepToggle(); sleep.Content = settings.Sleeping ? "叫醒杏菜" : "让杏菜小憩"; };
            SettingsRow(behavior, "休息状态", sleep);
            SettingsRow(behavior, "桌宠程序", SettingsButton("退出桌宠", Close));
            return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private static readonly Brush SettingsInk = PanelPalette.Ink;
        private static readonly Brush SettingsMuted = PanelPalette.Quiet;

        private static StackPanel SettingsGroup(Panel parent, string label)
        {
            var heading = GptUi.Text(label, 13, SettingsInk);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(2, 10, 0, 7);
            parent.Children.Add(heading);
            var rows = new StackPanel();
            parent.Children.Add(new Border { Background = PanelPalette.Surface, BorderBrush = PanelPalette.Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(13, 4, 13, 5), Child = rows });
            return rows;
        }

        private static void SettingsRow(Panel rows, string title, FrameworkElement control)
        {
            var line = new Grid { MinHeight = 40 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = GptUi.Text(title, 13, SettingsInk);
            label.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(label);
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(control);
            if (rows.Children.Count > 0) rows.Children.Add(new Border { Background = PanelPalette.Line, Height = 1 });
            rows.Children.Add(line);
        }

        private static Button SettingsButton(string text, Action action)
        {
            var button = GptChatWindow.ChatButton(text, delegate { if (action != null) action(); }, false);
            button.Foreground = SettingsInk;
            button.Background = PanelPalette.Rail;
            button.BorderBrush = PanelPalette.Line;
            button.Padding = new Thickness(9, 4, 9, 4);
            button.Margin = new Thickness(4, 0, 0, 0);
            button.MinHeight = 27;
            button.FontSize = 12;
            AutomationProperties.SetName(button, text);
            return button;
        }
    }
}
