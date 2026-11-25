using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace AnnaDesktopPet {
    internal static class Appearance {
        internal const string Uniform1Portrait = "校服1";
        internal const string Uniform2Portrait = "校服2立绘";
        internal const string GhostPortrait = "幽灵娘立绘";
        internal const string OtterPortrait = "海獭服杏菜";
        internal static string Theme="自动", Background="";
        internal static string Outfit { get; private set; }
        internal static event Action OutfitChanged;
        private static string SettingsPath {get{return Path.Combine(Program.DataDir,"appearance.txt");}}
        static Appearance()
        {
            Outfit = Uniform1Portrait;
            try
            {
                var lines = File.ReadAllLines(SettingsPath);
                if (lines.Length > 0) Theme = lines[0];
                if (lines.Length > 1) Background = lines[1];
                if (lines.Length > 2) Outfit = NormalizeOutfit(lines[2]);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal static string NormalizeOutfit(string name)
        {
            switch ((name ?? "").Trim())
            {
                case "校服2": case Uniform2Portrait: return Uniform2Portrait;
                case "幽灵服": case GhostPortrait: return GhostPortrait;
                case OtterPortrait: return OtterPortrait;
                default: return Uniform1Portrait;
            }
        }
        internal static bool Night {get{return Theme=="夜间" || Theme=="自动" && (DateTime.Now.Hour>=19 || DateTime.Now.Hour<7);}}
        private static void SaveAppearance(string theme, string background, string outfit)
        {
            Directory.CreateDirectory(Program.DataDir);
            File.WriteAllLines(SettingsPath, new[] { theme, background, outfit });
            Theme = theme;
            Background = background;
            Outfit = outfit;
            PanelPalette.Refresh();
        }
        internal static void ToggleTheme()
        {
            SaveAppearance(Night ? "日间" : "夜间", Background, Outfit);
        }
        internal static string Asset(string name){return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,Outfit==Uniform1Portrait?"assets":Path.Combine("outfits",Outfit),name);}
        internal static bool IsGhostPortrait { get { return Outfit == GhostPortrait; } }
        internal static bool IsStandingPortrait { get { return Outfit == Uniform1Portrait || Outfit == Uniform2Portrait || IsGhostPortrait || Outfit == OtterPortrait; } }
        internal static string ModelManifest
        {
            get
            {
                if (Outfit == Uniform1Portrait)
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Live2D", "Anna.model3.json");
                return Asset(Path.Combine("Live2D", "Anna.model3.json"));
            }
        }
        internal static void Show()
        {
            var window = new Window { Title = "杏菜 · 换装与场景", Width = 450, SizeToContent = SizeToContent.Height };
            var form = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            window.Content = form;

            var outfitRow = new Grid();
            outfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            outfitRow.ColumnDefinitions.Add(new ColumnDefinition());
            form.Children.Add(outfitRow);
            var thumbnail = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
            outfitRow.Children.Add(new Border { Width = 76, Height = 98, CornerRadius = new CornerRadius(10),
                Background = PanelPalette.Rail, Child = thumbnail });
            var outfitFields = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(outfitFields, 1);
            outfitRow.Children.Add(outfitFields);
            outfitFields.Children.Add(FieldTitle("桌宠立绘"));
            var portraits = new[] {
                new { Id = Uniform1Portrait, Name = "校服" },
                new { Id = Uniform2Portrait, Name = "水手服" },
                new { Id = GhostPortrait, Name = "幽灵娘" },
                new { Id = OtterPortrait, Name = "海獭服" }
            };
            var clothes = new ComboBox { Name = "OutfitSelector", ItemsSource = portraits,
                DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = Outfit, MinHeight = 32 };
            System.Windows.Automation.AutomationProperties.SetName(clothes, "桌宠立绘");
            outfitFields.Children.Add(clothes);
            Action updateThumbnail = delegate {
                string selected = NormalizeOutfit(clothes.SelectedValue as string);
                string folder = selected == Uniform1Portrait ? "assets" : Path.Combine("outfits", selected);
                thumbnail.Source = GptChatWindow.ReadLocalImage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder, "pet.png"), 168);
            };
            clothes.SelectionChanged += delegate { updateThumbnail(); };
            updateThumbnail();

            var themeLabel = FieldTitle("快捷菜单配色");
            themeLabel.Margin = new Thickness(0, 16, 0, 6);
            form.Children.Add(themeLabel);
            var theme = new ComboBox { Name = "ThemeSelector", ItemsSource = new[] { "自动", "日间", "夜间" }, SelectedItem = Theme, MinHeight = 32 };
            if (theme.SelectedIndex < 0) theme.SelectedIndex = 0;
            System.Windows.Automation.AutomationProperties.SetName(theme, "快捷菜单配色");
            form.Children.Add(theme);

            var backgroundLabel = FieldTitle("聊天背景");
            backgroundLabel.Margin = new Thickness(0, 16, 0, 3);
            form.Children.Add(backgroundLabel);
            string background = Background;
            var backgroundName = GptUi.Text("", 12, GptChatWindow.PanelQuiet);
            backgroundName.TextWrapping = TextWrapping.NoWrap;
            backgroundName.TextTrimming = TextTrimming.CharacterEllipsis;
            form.Children.Add(backgroundName);
            var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            form.Children.Add(actions);
            Action updateBackgroundName = delegate {
                backgroundName.Text = String.IsNullOrWhiteSpace(background) ? "默认背景" : Path.GetFileName(background);
                backgroundName.ToolTip = String.IsNullOrWhiteSpace(background) ? null : background;
            };
            updateBackgroundName();
            var choose = GptChatWindow.ChatButton("选择图片", delegate {
                var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg" };
                if (dialog.ShowDialog(window) == true) { background = dialog.FileName; updateBackgroundName(); }
            }, false);
            choose.Margin = new Thickness(0, 0, 6, 2);
            actions.Children.Add(choose);
            actions.Children.Add(GptChatWindow.ChatButton("恢复默认", delegate { background = ""; updateBackgroundName(); }, false));
            var note = GptUi.Text("保存后立即换装，聊天背景在下次打开面板时生效。", 12, GptChatWindow.PanelQuiet);
            note.Margin = new Thickness(0, 12, 0, 12);
            form.Children.Add(note);

            var save = GptChatWindow.ChatButton("保存设置", delegate {
                string selected = (string)clothes.SelectedValue;
                bool changed = selected != Outfit;
                try {
                    SaveAppearance((string)theme.SelectedItem, background, selected);
                }
                catch (IOException) { MessageBox.Show(window, "保存失败，请检查配置目录。"); return; }
                catch (UnauthorizedAccessException) { MessageBox.Show(window, "没有权限写入配置目录。"); return; }
                window.Close();
                if (changed && OutfitChanged != null) OutfitChanged();
            }, true);
            save.Name = "SaveAppearance";
            save.IsDefault = true;
            form.Children.Add(save);
            PanelTheme.Apply(window, "换装与场景", "DRESS & SCENE");
            window.Show();
        }

        private static TextBlock FieldTitle(string text)
        {
            var label = GptUi.Text(text, 13, GptChatWindow.PanelText);
            label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 0, 0, 6);
            return label;
        }
    }
}
