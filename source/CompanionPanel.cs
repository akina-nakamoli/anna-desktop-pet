using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace AnnaDesktopPet
{
    internal sealed partial class GptChatWindow
    {
        internal static readonly Brush PanelSky = PanelPalette.Sky;
        internal static readonly Brush TabRail = PanelPalette.Rail;
        internal static readonly Brush PanelText = PanelPalette.Ink;
        internal static readonly Brush PanelQuiet = PanelPalette.Quiet;
        private static readonly Brush PanelAccent = PanelPalette.Accent;
        internal static readonly Brush PanelLine = PanelPalette.Line;
        private ImageBrush companionPortrait;
        private Grid chatPage;
        private ContentControl pageHost;
        private FrameworkElement musicPage, settingsPage;
        private TextBlock companionName, connectionText;
        private Border connectionDot;
        private Button[] tabButtons;
        private MusicRouter panelMusicRouter;
        internal string SelectedCompanionTab { get; private set; }

        private void BuildCompanionPanel(MusicRouter music)
        {
            panelMusicRouter = music;
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(520, Math.Max(250, area.Width - 20));
            Height = Math.Min(690, Math.Max(300, area.Height - 20));
            MinWidth = Math.Min(350, Width); MinHeight = Math.Min(420, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            Background = PanelSky; Foreground = PanelText;
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14),
                UseAeroCaptionButtons = false
            });
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition());
            Content = new Border { CornerRadius = new CornerRadius(14), Background = PanelSky,
                BorderThickness = new Thickness(1), BorderBrush = PanelLine, Child = shell };
            Grid header = new Grid { Margin = new Thickness(20, 12, 10, 10), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.Handled || e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else DragMove();
            };
            BitmapSource portrait = ReadLocalImage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "杏菜Q版头像.png"), 144);
            Border face = new Border { Width = 49, Height = 49, CornerRadius = new CornerRadius(15), Background = TabRail };
            if (portrait != null) {
                companionPortrait = new ImageBrush(portrait) { Stretch = Stretch.UniformToFill };
                companionPortrait.Freeze();
                face.Background = companionPortrait;
                RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality); Icon = portrait;
            }
            header.Children.Add(face);
            StackPanel identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
            companionName = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = PanelText,
                TextTrimming = TextTrimming.CharacterEllipsis };
            identity.Children.Add(companionName);
            identity.Children.Add(new TextBlock { Text = "ANNA YANAMI", FontSize = 10, FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold, Foreground = PanelQuiet, Margin = new Thickness(1, 1, 0, 0) });
            Grid.SetColumn(identity, 1); header.Children.Add(identity);
            StackPanel chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Button themeSwitch = IconButton("切换配色", "", delegate {
                try { Appearance.ToggleTheme(); }
                catch (IOException) { SetStatus("配色保存失败，请检查配置目录。", true); }
                catch (UnauthorizedAccessException) { SetStatus("没有权限保存配色。", true); }
            }, 29);
            Action updateThemeSwitch = delegate {
                bool night = Appearance.Night;
                string label = night ? "切换到日间配色" : "切换到夜间配色";
                themeSwitch.Content = Glyph(night ? "\uE706" : "\uE708", 16);
                themeSwitch.ToolTip = label;
                AutomationProperties.SetName(themeSwitch, label);
            };
            EventHandler paletteChanged = delegate { updateThemeSwitch(); };
            themeSwitch.Loaded += delegate { PanelPalette.Sky.Changed += paletteChanged; updateThemeSwitch(); };
            themeSwitch.Unloaded += delegate { PanelPalette.Sky.Changed -= paletteChanged; };
            updateThemeSwitch();
            chrome.Children.Add(themeSwitch);
            chrome.Children.Add(IconButton("最小化", "\uE921", delegate { WindowState = WindowState.Minimized; }, 29));
            chrome.Children.Add(IconButton("关闭面板", "\uE8BB", Close, 29));
            Grid.SetColumn(chrome, 2); header.Children.Add(chrome); shell.Children.Add(header);

            Grid nav = new Grid();
            string[] keys = { "chat", "music", "settings" }, labels = { "聊一会儿", "一起听", "设置" }, icons = { "\uE8F2", "\uE8D6", "\uE713" };
            tabButtons = new Button[labels.Length];
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; nav.ColumnDefinitions.Add(new ColumnDefinition());
                Button tab = ChatButton(labels[i], delegate { SelectCompanionTab(key); }, false);
                tab.Height = 32; tab.Padding = new Thickness(2); tab.BorderThickness = new Thickness(0);
                StackPanel pair = new StackPanel { Orientation = Orientation.Horizontal };
                pair.Children.Add(Glyph(icons[i], 14));
                pair.Children.Add(new TextBlock { Text = labels[i], Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                tab.Content = pair; AutomationProperties.SetName(tab, labels[i]);
                Grid.SetColumn(tab, i); nav.Children.Add(tab); tabButtons[i] = tab;
            }
            Border navBar = new Border { Child = nav, Background = TabRail, CornerRadius = new CornerRadius(11),
                Padding = new Thickness(3), Margin = new Thickness(18, 0, 18, 2) };
            Grid.SetRow(navBar, 1); shell.Children.Add(navBar);
            chatPage = BuildChatPage();
            pageHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(18, 0, 18, 13), Focusable = false };
            Grid.SetRow(pageHost, 2); shell.Children.Add(pageHost);
            SelectCompanionTab("chat");
        }

        internal void SelectCompanionTab(string tab)
        {
            PetWindow pet = Owner as PetWindow;
            companionName.Text = pet == null || String.IsNullOrWhiteSpace(pet.CompanionName) ? "杏菜" : pet.CompanionName;
            int selected;
            if (tab == "music") {
                selected = 1;
                if (musicPage == null) musicPage = new CompanionMusicPage(panelMusicRouter);
                pageHost.Content = musicPage;
            }
            else if (tab == "settings") {
                selected = 2;
                if (SelectedCompanionTab != "settings" || settingsPage == null) settingsPage = pet == null
                    ? (FrameworkElement)ChatButton("模型连接", OpenSettings, false) : pet.CreateCompanionSettingsPage(OpenSettings);
                pageHost.Content = settingsPage;
            }
            else {
                tab = "chat"; selected = 0; pageHost.Content = chatPage;
            }
            for (int i = 0; i < tabButtons.Length; i++) {
                tabButtons[i].Background = i == selected ? PanelPalette.Surface : Brushes.Transparent;
                tabButtons[i].Foreground = i == selected ? PanelAccent : PanelQuiet;
                tabButtons[i].FontWeight = i == selected ? FontWeights.SemiBold : FontWeights.Normal;
                AutomationProperties.SetItemStatus(tabButtons[i], i == selected ? "已选中" : "");
            }
            SelectedCompanionTab = tab;
        }

        private Grid BuildChatPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition());
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid bar = new Grid { Margin = new Thickness(2, 4, 0, 4) };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DockPanel model = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            connectionDot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            model.Children.Add(connectionDot);
            connectionText = new TextBlock { Foreground = PanelQuiet, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            model.Children.Add(connectionText); bar.Children.Add(model);
            StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(IconButton("通话", "\uE717", delegate { PetWindow pet = Owner as PetWindow; if (pet != null) pet.OpenCall(); }, 31));
            fresh = IconButton("新对话", "\uE710", NewConversation, 31); tools.Children.Add(fresh);
            Grid.SetColumn(tools, 1); bar.Children.Add(tools);
            page.Children.Add(new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = PanelLine });
            progress = new ProgressBar { Height = 2, Foreground = PanelAccent, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed, IsIndeterminate = true };
            Grid.SetRow(progress, 1); page.Children.Add(progress);
            messages = new StackPanel { Margin = new Thickness(1, 12, 3, 6) };
            conversation = new ScrollViewer { Content = messages, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
            BitmapSource customBackground = ReadLocalImage(Appearance.Background, 1000);
            if (customBackground != null)
                conversation.Background = new ImageBrush(customBackground) { Stretch = Stretch.UniformToFill, Opacity = .30 };
            Grid.SetRow(conversation, 2); page.Children.Add(conversation);
            status = GptUi.Text("", 11, PanelQuiet); status.Margin = new Thickness(2, 3, 2, 4); status.LineHeight = 17;
            Grid.SetRow(status, 3); page.Children.Add(status);

            Grid compose = new Grid();
            compose.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            compose.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            compose.ColumnDefinitions.Add(new ColumnDefinition()); compose.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, MinHeight = 36, MaxHeight = 100,
                MaxLength = 4000, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent,
                Foreground = PanelText, CaretBrush = PanelAccent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetName(input, "聊天输入框");
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Send(); }
            };
            Grid.SetColumnSpan(input, 2); compose.Children.Add(input);
            TextBlock placeholder = GptUi.Text("和杏菜说点什么…", 14, PanelQuiet);
            placeholder.IsHitTestVisible = false; placeholder.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumnSpan(placeholder, 2); compose.Children.Add(placeholder);
            input.TextChanged += delegate { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            TextBlock hint = GptUi.Text("Ctrl + Enter 发送", 10, PanelQuiet); hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(hint, 1); compose.Children.Add(hint);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            stop = IconButton("停止回复和朗读", "\uE71A", Stop, 29); actions.Children.Add(stop);
            send = IconButton("发送", "\uE724", Send, 32); send.Background = PanelPalette.Action; send.Foreground = Brushes.White;
            send.Margin = new Thickness(6, 0, 0, 0); actions.Children.Add(send); Grid.SetColumn(actions, 1); Grid.SetRow(actions, 1); compose.Children.Add(actions);
            Border composer = new Border { Child = compose, Background = PanelPalette.Surface, BorderBrush = PanelLine, BorderThickness = new Thickness(1),
                Padding = new Thickness(13, 10, 8, 7), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 4, 0, 0) };
            input.IsKeyboardFocusedChanged += delegate { composer.BorderBrush = input.IsKeyboardFocused ? PanelAccent : PanelLine; };
            Grid.SetRow(composer, 4); page.Children.Add(composer);
            return page;
        }

        private FrameworkElement BuildWelcomeMessage()
        {
            StackPanel copy = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 8, 12, 12) };
            TextBlock greeting = GptUi.Text("今天想聊什么？", 22, PanelText); greeting.FontWeight = FontWeights.SemiBold; copy.Children.Add(greeting);
            if (String.IsNullOrWhiteSpace(settings.ApiKey)) {
                Button setup = ChatButton("连接聊天模型  ›", delegate { SelectCompanionTab("settings"); OpenSettings(); }, false);
                setup.HorizontalAlignment = HorizontalAlignment.Left; setup.Padding = new Thickness(10, 5, 10, 5);
                setup.Margin = new Thickness(0, 12, 0, 0); copy.Children.Add(setup);
            }
            return copy;
        }

        private static TextBlock Glyph(string glyph, double size)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        internal static Button IconButton(string label, string symbol, Action click, double size)
        {
            Button button = ChatButton(label, click, false); button.Content = Glyph(symbol, 13);
            button.Width = button.Height = size; button.Padding = new Thickness(2); button.BorderThickness = new Thickness(0);
            button.Background = Brushes.Transparent; button.Foreground = PanelQuiet; button.ToolTip = label;
            AutomationProperties.SetName(button, label); return button;
        }

        internal static BitmapSource ReadLocalImage(string path, int decodeWidth)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try {
                using (FileStream stream = File.OpenRead(path)) {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = decodeWidth; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (FileFormatException) { return null; }
        }
    }
}
