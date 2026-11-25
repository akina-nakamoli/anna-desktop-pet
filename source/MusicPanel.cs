using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FilePath = System.IO.Path;
using GlyphPath = System.Windows.Shapes.Path;

namespace AnnaDesktopPet
{
    internal sealed class MusicPanel : Window
    {
        internal static Task<string> Command(string command, string knownTrackKey = null)
        {
            switch (command)
            {
                case "status": case "details": case "previous": case "next": case "pause": case "play": break;
                default: throw new ArgumentException("未知的播放操作。", "command");
            }
            string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" +
                FilePath.Combine(AppDomain.CurrentDomain.BaseDirectory, "MusicSession.ps1") + "\" -Command " + command;
            if (knownTrackKey != null && System.Text.RegularExpressions.Regex.IsMatch(knownTrackKey, "^[a-f0-9]{64}$"))
                args += " -KnownTrackKey " + knownTrackKey;
            return Task.Run(delegate {
                var start = new ProcessStartInfo("powershell.exe", args) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (var process = Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output.Trim();
                }
            });
        }

        internal MusicPanel(MusicRouter sharedRouter = null)
        {
            Title = "杏菜 · 一起听"; Width = 454; Height = 620; MinWidth = 320; MinHeight = 320;
            Background = CompanionMusicPage.Paper; FontFamily = new FontFamily("Microsoft YaHei UI");
            var router = sharedRouter ?? new MusicRouter(delegate { });
            Content = new CompanionMusicPage(router) { Margin = new Thickness(20) };
            if (sharedRouter == null) Closed += delegate { router.Dispose(); };
        }
    }

    internal sealed class CompanionMusicPage : ScrollViewer
    {
        // Only information supplied by the local player or Windows media session is displayed as playing.
        private sealed class Session
        {
            public string title { get; set; }
            public string artist { get; set; }
            public string album { get; set; }
            public string status { get; set; }
            public string key { get; set; }
            public string art { get; set; }
            public double position { get; set; }
            public double duration { get; set; }
            public bool previous { get; set; }
            public bool next { get; set; }
            public bool play { get; set; }
            public bool pause { get; set; }
        }

        private static SolidColorBrush ColorOf(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze(); return brush;
        }
        internal static readonly Brush Paper = PanelPalette.Sky;
        private static readonly Brush Ink = PanelPalette.Ink, Muted = PanelPalette.Quiet,
            Blue = PanelPalette.Action, Line = PanelPalette.Line, Frost = PanelPalette.Surface;
        private const string PlayIcon = "M8,5 L19,12 L8,19 Z";
        private const string PauseIcon = "M8,5 L8,19 M16,5 L16,19";
        private const string StarIcon = "M12,2.8 L14.85,8.65 21.3,9.6 16.65,14.15 17.75,20.6 12,17.55 6.25,20.6 7.35,14.15 2.7,9.6 9.15,8.65 Z";
        private static readonly Brush StarGold = ColorOf("#F5BB36"), StarEdge = ColorOf("#9D701D");
        private readonly MusicRouter router;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private readonly Image cover = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        private readonly TextBlock song = Text("", 20, Ink), artist = Text("", 12, Muted), source = Text("", 11, Muted),
            elapsed = Text("", 10, Muted), duration = Text("", 10, Muted), feedback = Text("", 12, Muted);
        private readonly TextBox search;
        private readonly Button previous, toggle, next, favorite, find, library;
        private readonly StackPanel rows = new StackPanel();
        private readonly Func<string, int, CancellationToken, Task<QqSearchPage>> searchCatalog;
        private readonly List<QqTrack> results = new List<QqTrack>();
        private readonly HashSet<string> shownIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly TextBlock searchSummary = Text("", 12, Muted);
        private readonly Button moreResults;
        private CancellationTokenSource searchRequest;
        private CancellationTokenSource favoriteLookup;
        private HashSet<string> favoriteIds = new HashSet<string>(StringComparer.Ordinal);
        private FavoriteSong currentFavorite;
        private bool favoritesReadable;
        private string resultQuery = "", searchProblem = "";
        private int nextPage = 1;
        private int? resultTotal;
        private bool hasMoreResults;
        private readonly ProgressBar progress = new ProgressBar {
            Height = 3, Minimum = 0, Maximum = 1, Foreground = Blue, Background = Line, BorderThickness = new Thickness(0)
        };
        private Session session;
        private string displayedTrackKey, nativeCoverKey;
        private CancellationTokenSource coverLookup;
        private DateTime nextCoverLookup;
        private bool polling, working, libraryOpen, local;

        private void AddPathSettings(StackPanel root)
        {
            root.Children.Add(new TextBlock { Text="QQ 音乐程序路径", FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,18,0,7) });
            var path = new TextBox { ToolTip="填写 QQMusic.exe 的完整路径", MinHeight=34 };
            System.Windows.Automation.AutomationProperties.SetName(path, "QQ 音乐程序路径");
            root.Children.Add(path);
            var actions = new WrapPanel { Margin=new Thickness(0,8,0,0) };
            root.Children.Add(actions);
            var browse = new Button { Content="浏览…", Margin=new Thickness(0,0,8,0) };
            var save = new Button { Content="保存路径" };
            actions.Children.Add(browse);
            actions.Children.Add(save);
            var feedback = new TextBlock { Text="保存后，杏菜点歌时可以自动打开 QQ 音乐。", Margin=new Thickness(0,8,0,0), TextWrapping=TextWrapping.Wrap };
            root.Children.Add(feedback);
            try { path.Text = QqCatalog.ReadConfiguredPath(); }
            catch (IOException) { feedback.Text = "暂时无法读取已保存的路径，可以重新填写。"; }
            catch (UnauthorizedAccessException) { feedback.Text = "无法读取已保存的路径，请检查桌宠设置目录的权限。"; }
            browse.Click += delegate
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title="选择 QQMusic.exe",
                    Filter="QQ 音乐程序 (QQMusic.exe)|QQMusic.exe",
                    CheckFileExists=true
                };
                if (picker.ShowDialog(Window.GetWindow(this)) == true) path.Text = picker.FileName;
            };
            save.Click += delegate
            {
                try
                {
                    path.Text = QqCatalog.SaveConfiguredPath(path.Text);
                    feedback.Text = "路径已保存，重新点歌即可。";
                }
                catch (InvalidOperationException) { feedback.Text = "请选择有效的 QQMusic.exe。"; }
                catch (ArgumentException) { feedback.Text = "路径格式不正确，请用「浏览」选择 QQMusic.exe。"; }
                catch (IOException) { feedback.Text = "无法保存路径，请检查桌宠设置目录。"; }
                catch (UnauthorizedAccessException) { feedback.Text = "无法保存路径，请检查桌宠设置目录的权限。"; }
            };
        }
        internal CompanionMusicPage(MusicRouter sharedRouter = null) : this(sharedRouter, QqCatalog.SearchPageAsync) { }

        internal CompanionMusicPage(MusicRouter sharedRouter, Func<string, int, CancellationToken, Task<QqSearchPage>> searchCatalog)
        {
            router = sharedRouter;
            this.searchCatalog = searchCatalog;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PanningMode = PanningMode.VerticalOnly;
            Resources[typeof(Button)] = (Style)XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
                "<Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>" +
                "<Border Name='surface' CornerRadius='24' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#73A2BF'/></Trigger>" +
                "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#356889'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Opacity' Value='.72'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='.42'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            var body = new StackPanel { Margin = new Thickness(1, 8, 5, 12) }; Content = body;
            var art = new Grid { Width = 148, Height = 148, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 14) };
            art.Clip = new RectangleGeometry(new Rect(0, 0, 148, 148), 12, 12);
            art.Children.Add(RecordSleeve()); art.Children.Add(cover); body.Children.Add(art);
            var titles = new Grid();
            titles.ColumnDefinitions.Add(new ColumnDefinition());
            titles.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel();
            source.Margin = new Thickness(0, 0, 0, 3); song.FontWeight = FontWeights.SemiBold;
            artist.Margin = new Thickness(0, 3, 0, 0);
            labels.Children.Add(source); labels.Children.Add(song); labels.Children.Add(artist); titles.Children.Add(labels);
            favorite = IconButton("收藏这首歌", StarIcon, false);
            favorite.Width = favorite.Height = 33;
            favorite.Click += async delegate { await ToggleFavorite(); };
            Grid.SetColumn(favorite, 1); titles.Children.Add(favorite); body.Children.Add(titles);
            progress.Margin = new Thickness(0, 12, 0, 4); body.Children.Add(progress);
            var times = new Grid(); elapsed.HorizontalAlignment = HorizontalAlignment.Left; duration.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(elapsed); times.Children.Add(duration); body.Children.Add(times);
            var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 12) };
            previous = IconButton("上一首", "M6,5 L6,19 M18,5 L8,12 L18,19 Z", false);
            toggle = IconButton("播放", PlayIcon, true);
            next = IconButton("下一首", "M18,5 L18,19 M6,5 L16,12 L6,19 Z", false);
            previous.Click += async delegate { await Transport("previous"); };
            toggle.Click += async delegate { await Transport(IsPlaying ? "pause" : "play"); };
            next.Click += async delegate { await Transport("next"); };
            transport.Children.Add(previous); transport.Children.Add(toggle); transport.Children.Add(next); body.Children.Add(transport);
            body.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 0, 0, 12) });
            var section = new Grid(); var sectionTitle = Text("点歌", 15, Ink); sectionTitle.FontWeight = FontWeights.SemiBold;
            sectionTitle.VerticalAlignment = VerticalAlignment.Center; section.Children.Add(sectionTitle);
            library = TextButton("我的收藏", 11); library.HorizontalAlignment = HorizontalAlignment.Right;
            library.Click += delegate {
                feedback.Text = "";
                if (!libraryOpen) { ShowFavorites(); return; }
                libraryOpen = false; library.Content = "我的收藏";
                RenderResults();
            };
            section.Children.Add(library); body.Children.Add(section);
            var searchGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition()); searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            search = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ink,
                MaxLength = 60, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(search, "歌名或歌手");
            var field = new Grid(); field.Children.Add(search);
            var hint = Text("输入歌名或歌手", 12, Muted); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center;
            field.Children.Add(hint); search.TextChanged += delegate { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            searchGrid.Children.Add(new Border { Background = Frost, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(11,6,9,6), Child = field });
            find = TextButton("搜索", 12); find.Margin = new Thickness(7,0,0,0); find.Padding = new Thickness(16,7,16,7);
            find.Background = Blue; find.Foreground = Brushes.White;
            find.Click += async delegate { await Search(); };
            search.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Search(); } };
            Grid.SetColumn(find,1); searchGrid.Children.Add(find); body.Children.Add(searchGrid);
            feedback.TextWrapping = TextWrapping.Wrap; feedback.Margin = new Thickness(1,4,0,0);
            searchSummary.Name = "CatalogSearchStatus"; searchSummary.TextWrapping = TextWrapping.Wrap;
            searchSummary.Margin = new Thickness(1,4,0,4);
            body.Children.Add(feedback); body.Children.Add(searchSummary); body.Children.Add(rows);
            moreResults = TextButton("加载更多", 12); moreResults.Name = "MoreCatalogResults";
            moreResults.Margin = new Thickness(0,7,0,1);
            moreResults.Click += async delegate { await LoadResultsPage(); };
            body.Children.Add(moreResults); AddPathSettings(body); UpdateSearchSummary();
            timer.Tick += async delegate { await Refresh(); };
            Loaded += delegate { UpdatePolling(); }; IsVisibleChanged += delegate { UpdatePolling(); };
            Unloaded += delegate { timer.Stop(); CancelSearch(); CancelFavoriteLookup(); };
            ApplySession(null, false);
        }

        private static TextBlock Text(string value, double size, Brush ink)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis };
        }
        private static FrameworkElement RecordSleeve()
        {
            var sleeve = new Canvas { Width = 148, Height = 148, Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#D4E9F1"), (Color)ColorConverter.ConvertFromString("#9CBED4"), 52) };
            var record = new Grid { Width = 128, Height = 128 };
            Canvas.SetLeft(record,10); Canvas.SetTop(record,10); sleeve.Children.Add(record);
            record.Children.Add(new Ellipse { Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#22475F"), (Color)ColorConverter.ConvertFromString("#668FA8"), 42) });
            Brush groove = ColorOf("#7394A7");
            for (int d = 118; d >= 68; d -= 6) record.Children.Add(new Ellipse { Width = d, Height = d, Stroke = groove, Opacity = .5, StrokeThickness = .65 });
            record.Children.Add(new Ellipse { Width = 49, Height = 49, Fill = ColorOf("#F2DDA9") });
            record.Children.Add(new Ellipse { Width = 38, Height = 38, Stroke = ColorOf("#B6A477"), StrokeThickness = .7 });
            record.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = ColorOf("#294961") });
            return sleeve;
        }
        private static Button TextButton(string label, double size)
        {
            return new Button { Content = label, FontSize = size, Background = Frost, Foreground = Ink, BorderBrush = Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(10,5,10,5), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        }
        private static FrameworkElement Glyph(string geometry, Brush stroke, Brush fill = null)
        {
            var box = new Grid { Width = 24, Height = 24 };
            box.Children.Add(new GlyphPath { Data = Geometry.Parse(geometry), Stroke = stroke, Fill = fill, StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            return box;
        }
        private static Button IconButton(string label, string geometry, bool main)
        {
            var button = TextButton(label,12); button.ToolTip = label; AutomationProperties.SetName(button,label);
            button.Padding = new Thickness(0); button.Width = button.Height = main ? 44 : 34;
            button.Margin = new Thickness(7,0,7,0); button.Background = main ? Blue : Frost;
            button.Content = Glyph(geometry, main ? Brushes.White : Ink); return button;
        }
        private bool IsPlaying { get { return session != null && session.status == "Playing"; } }
        private void UpdatePolling()
        {
            if (!IsLoaded || !IsVisible) CancelFavoriteLookup();
            if (!IsLoaded || !IsVisible || Program.Smoke || Program.PanelCheck)
            {
                timer.Stop();
                CancelCoverLookup();
                return;
            }
            if (timer.IsEnabled) return;
            timer.Start(); RefreshOnShow();
        }
        private async void RefreshOnShow() { await Refresh(); }
        private async Task Refresh()
        {
            if (polling || !IsLoaded || !IsVisible) return;
            polling = true;
            try
            {
                string json = await MusicPanel.Command("details", nativeCoverKey);
                if (!IsLoaded || !IsVisible) return;
                var qq = json.StartsWith("{", StringComparison.Ordinal)
                    ? new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Session>(json) : null;
                if (qq != null && !String.IsNullOrWhiteSpace(qq.title) && qq.status == "Playing")
                    ApplySession(qq, false);
                else if (router != null && !String.IsNullOrEmpty(router.LocalTitle) && (qq == null || !router.LocalPaused))
                {
                    ApplySession(new Session { title = router.LocalTitle, status = router.LocalPaused ? "Paused" : "Playing",
                        play = router.LocalPaused, pause = !router.LocalPaused, duration = router.LocalDuration, position = router.LocalSeconds }, true);
                }
                else if (qq != null)
                    ApplySession(qq, false);
                else if (json.Contains("未检测到 QQ 音乐媒体会话"))
                    ApplySession(null, false);
                else
                    ShowSessionError();
            }
            catch (Exception) { if (IsVisible) ShowSessionError(); }
            finally { polling = false; }
        }
        private static string TimeLabel(double value)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, Math.Min(value, 360000)));
            return ((int)time.TotalMinutes).ToString() + ":" + time.Seconds.ToString("00");
        }
        private void ShowSessionError()
        {
            ApplySession(null, false);
            song.Text = "暂时读不到播放状态";
            artist.Text = "可以继续点歌，或在 QQ 音乐中查看当前歌曲。";
        }
        private void CancelCoverLookup()
        {
            if (coverLookup == null) return;
            var old = coverLookup;
            coverLookup = null;
            old.Cancel();
        }
        private bool DisplayCover(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 384;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    cover.Source = image;
                    cover.Visibility = Visibility.Visible;
                    return true;
                }
            }
            catch (Exception) { return false; }
        }
        private void SyncCover(Session data)
        {
            string key = data == null ? null : data.key;
            if (key != displayedTrackKey)
            {
                CancelCoverLookup();
                displayedTrackKey = key;
                nativeCoverKey = null;
                nextCoverLookup = DateTime.MinValue;
                cover.Source = null;
                cover.Visibility = Visibility.Collapsed;
            }
            if (String.IsNullOrEmpty(key)) return;
            if (nativeCoverKey != key && !String.IsNullOrEmpty(data.art))
            {
                try
                {
                    if (DisplayCover(Convert.FromBase64String(data.art)))
                    {
                        nativeCoverKey = key;
                        CancelCoverLookup();
                        return;
                    }
                }
                catch (FormatException) { }
            }
            if (cover.Source == null && coverLookup == null && DateTime.UtcNow >= nextCoverLookup &&
                IsVisible && IsLoaded && !Program.Smoke && !Program.PanelCheck)
                LoadCatalogCover(data);
        }
        private async void LoadCatalogCover(Session track)
        {
            var lookup = new CancellationTokenSource();
            coverLookup = lookup;
            try
            {
                byte[] bytes = await QqArtwork.ResolveAsync(track.title, track.artist, track.album, lookup.Token);
                if (!lookup.IsCancellationRequested && displayedTrackKey == track.key && cover.Source == null && IsVisible)
                    DisplayCover(bytes);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* The media session and playback controls remain usable offline. */ }
            finally
            {
                if (coverLookup == lookup)
                {
                    coverLookup = null;
                    nextCoverLookup = DateTime.UtcNow.AddMinutes(1);
                }
                lookup.Dispose();
            }
        }
        private void ApplySession(Session data, bool isLocal)
        {
            if (isLocal || !SameRecording(session, data)) CancelFavoriteLookup();
            local = isLocal; session = data;
            bool active = data != null && !String.IsNullOrWhiteSpace(data.title);
            source.Text = active ? (isLocal ? "本地音乐" : "QQ 音乐") + "  ·  " + (IsPlaying ? "播放中" : data.status == "Paused" ? "已暂停" : "已连接") : "QQ 音乐";
            song.Text = active ? data.title : "还没有正在播放的歌";
            artist.Text = active ? (!String.IsNullOrWhiteSpace(data.artist) ? data.artist : isLocal ? "本地文件" : data.album ?? "")
                : "点一首想听的歌，或在 QQ 音乐中开始播放。";
            SyncCover(active && !isLocal ? data : null);
            bool hasTime = active && data.duration > 0;
            progress.Value = hasTime ? Math.Max(0, Math.Min(1, data.position / data.duration)) : 0;
            elapsed.Text = hasTime ? TimeLabel(data.position) : ""; duration.Text = hasTime ? TimeLabel(data.duration) : "";
            toggle.Content = Glyph(IsPlaying ? PauseIcon : PlayIcon, Brushes.White);
            toggle.ToolTip = IsPlaying ? "暂停" : "播放"; AutomationProperties.SetName(toggle,(string)toggle.ToolTip);
            RefreshFavoriteState();
            UpdateButtons();
        }
        private static bool SameRecording(Session left, Session right)
        {
            return left != null && right != null && left.key == right.key &&
                left.title == right.title && left.artist == right.artist && left.album == right.album;
        }
        private void RefreshFavoriteState()
        {
            List<FavoriteSong> saved;
            try { saved = MusicFavorites.Load(); favoritesReadable = true; }
            catch (Exception) { saved = new List<FavoriteSong>(); favoritesReadable = false; }
            currentFavorite = null;
            if (!local && session != null && router != null)
                currentFavorite = MusicFavorites.FindPlaying(saved, session.title, session.artist, session.album,
                    router.RequestedPlaying(session.title, session.artist, session.album));
            bool marked = currentFavorite != null;
            favorite.Content = Glyph(StarIcon, marked ? StarEdge : PanelPalette.Accent, marked ? StarGold : null);
            string action = !favoritesReadable ? "暂时无法读取本机收藏" : marked ? "取消收藏" : "收藏这首歌";
            favorite.ToolTip = action;
            AutomationProperties.SetName(favorite, action);
            AutomationProperties.SetItemStatus(favorite, !favoritesReadable ? "收藏状态未知" : marked ? "已收藏" : "未收藏");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FavoriteSong entry in saved) if (entry != null) ids.Add(entry.Id);
            if (favoriteIds.SetEquals(ids)) return;
            favoriteIds = ids;
            if (libraryOpen) ShowFavorites(favoritesReadable ? saved : null);
            else if (results.Count > 0) RenderResults();
        }
        private void CancelFavoriteLookup()
        {
            if (favoriteLookup != null) favoriteLookup.Cancel();
        }
        private async Task ToggleFavorite()
        {
            if (!favorite.IsEnabled || session == null) return;
            Session target = session;
            FavoriteSong marked = currentFavorite;
            await Perform(async delegate {
                string result;
                if (marked != null) result = MusicFavorites.RemoveById(marked.Id);
                else
                {
                    using (var lookup = new CancellationTokenSource())
                    {
                        favoriteLookup = lookup;
                        try
                        {
                            QqTrack recording = await router.ResolvePlayingAsync(target.title, target.artist, target.album, lookup.Token);
                            lookup.Token.ThrowIfCancellationRequested();
                            if (local || !SameRecording(session, target)) return "歌曲已切换。";
                            if (recording == null) return "未能确定这首歌的版本，可以搜索后点播再收藏。";
                            result = MusicFavorites.Add(recording);
                        }
                        finally { favoriteLookup = null; }
                    }
                }
                RefreshFavoriteState();
                return result;
            });
        }
        private void UpdateButtons()
        {
            previous.IsEnabled = !working && session != null && session.previous;
            next.IsEnabled = !working && session != null && session.next;
            toggle.IsEnabled = !working && session != null && (IsPlaying ? session.pause : session.play);
            favorite.IsEnabled = !working && favoritesReadable && !local && router != null && session != null &&
                !String.IsNullOrWhiteSpace(session.title) && !String.IsNullOrWhiteSpace(session.artist);
            library.IsEnabled = find.IsEnabled = !working && router != null;
            rows.IsEnabled = !working;
            moreResults.IsEnabled = !working && searchRequest == null && hasMoreResults;
        }
        private static readonly Brush PaidSurface = PanelPalette.Paid, PaidInk = PanelPalette.Price;
        private void Row(string title, string detail, Func<Task> action, QqTrack catalogTrack = null, bool fromFavorites = false)
        {
            var text = new StackPanel();
            var name = Text(title,13,Ink); name.FontWeight = FontWeights.SemiBold;
            bool marked = fromFavorites || (catalogTrack != null && favoriteIds.Contains(catalogTrack.Id));
            if (marked)
            {
                var heading = new DockPanel();
                var star = new Viewbox { Width = 15, Height = 15, Margin = new Thickness(0,0,4,0),
                    Child = Glyph(StarIcon, StarEdge, StarGold), VerticalAlignment = VerticalAlignment.Center };
                heading.Children.Add(star); heading.Children.Add(name); text.Children.Add(heading);
            }
            else text.Children.Add(name);
            if (!String.IsNullOrEmpty(detail)) text.Children.Add(Text(detail,11,Muted));
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition()); line.Children.Add(text);
            string description = "播放 " + title + (String.IsNullOrEmpty(detail) ? "" : " · " + detail);
            if (catalogTrack != null)
            {
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bool paid = catalogTrack.PaidPlayback == true;
                string rights = catalogTrack.PlaybackLabel;
                var badge = new Border { CornerRadius = new CornerRadius(7), Margin = new Thickness(6,0,0,0),
                    Padding = new Thickness(7,3,7,3), VerticalAlignment = VerticalAlignment.Center,
                    Background = paid ? PaidSurface : Paper, Child = Text(rights,11,paid ? PaidInk : Muted) };
                Grid.SetColumn(badge,1); line.Children.Add(badge);
                description += " · " + rights;
            }
            var button = TextButton(title,12); button.Content = line; button.ToolTip = description;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0,3,0,0); button.Padding = new Thickness(13,7,13,7);
            AutomationProperties.SetName(button,description);
            if (marked) AutomationProperties.SetItemStatus(button, "已收藏");
            button.Click += async delegate {
                try { await action(); }
                catch (Exception) { feedback.Text = "这首歌暂时无法打开，请刷新歌曲列表。"; }
            };
            rows.Children.Add(button);
        }
        private void ShowFavorites(List<FavoriteSong> saved = null)
        {
            libraryOpen = true; library.Content = "收起收藏"; rows.Children.Clear(); feedback.Text = "";
            UpdateSearchSummary();
            try
            {
                var favorites = saved ?? MusicFavorites.Load();
                if (favorites.Count == 0) { feedback.Text = "收藏还是空的。点歌名旁的星标即可收藏。"; return; }
                foreach (FavoriteSong entry in favorites)
                {
                    FavoriteSong chosen = entry;
                    Row(entry.Title,entry.Artist,async delegate {
                        var current = MusicFavorites.Load();
                        int index = current.FindIndex(item => item.Id == chosen.Id);
                        if (index < 0) { ShowFavorites(); return; }
                        await RunRouter("播放收藏第" + (index + 1) + "首");
                    }, fromFavorites: true);
                }
            }
            catch (Exception) { feedback.Text = "暂时无法读取收藏。"; }
        }
        private async Task Perform(Func<Task<string>> action)
        {
            if (working) return; working = true; UpdateButtons();
            feedback.Text = "正在处理…";
            try { feedback.Text = await action() ?? ""; }
            catch (OperationCanceledException) { feedback.Text = "已取消。"; }
            catch (Exception) { feedback.Text = "音乐操作未完成，请稍后重试。"; }
            finally { working = false; UpdateButtons(); }
            if (IsVisible && !Program.Smoke && !Program.PanelCheck) await Refresh();
        }
        private async Task Transport(string command)
        {
            await Perform(async delegate {
                if (local && router != null)
                    return await router.Handle(command == "pause" ? "暂停音乐" : "继续播放");
                string result = await MusicPanel.Command(command);
                return result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Length >= 3 ? "" : result;
            });
        }
        private Task RunRouter(string command)
        {
            return Perform(delegate { return router.Handle(command); });
        }
        private void CancelSearch()
        {
            var pending = searchRequest;
            if (pending == null) return;
            searchRequest = null;
            pending.Cancel();
            searchProblem = "搜索已暂停。";
            UpdateSearchSummary();
        }

        private async Task Search()
        {
            if (working || router == null) return;
            string query = search.Text.Trim(); if (query.Length == 0) { search.Focus(); return; }
            if (searchRequest != null && resultQuery == query && nextPage == 1 && !libraryOpen) return;
            CancelSearch();
            libraryOpen = false; library.Content = "我的收藏";
            resultQuery = query; nextPage = 1; resultTotal = null; hasMoreResults = true;
            searchProblem = ""; feedback.Text = ""; shownIds.Clear(); results.Clear(); rows.Children.Clear();
            await LoadResultsPage();
        }

        private async Task LoadResultsPage()
        {
            if (working || libraryOpen || searchRequest != null || !hasMoreResults) return;
            var pending = new CancellationTokenSource();
            searchRequest = pending; searchProblem = ""; UpdateSearchSummary();
            try
            {
                QqSearchPage page = await searchCatalog(resultQuery, nextPage, pending.Token);
                if (searchRequest != pending) return;
                foreach (QqTrack track in page.Tracks)
                {
                    if (!shownIds.Add(track.Id)) continue;
                    results.Add(track);
                    if (!libraryOpen) AddResult(track);
                }
                resultTotal = page.Total ?? resultTotal;
                hasMoreResults = page.HasMore; nextPage++;
            }
            catch (OperationCanceledException) { if (searchRequest == pending) searchProblem = "搜索已暂停。"; }
            catch (Exception) { if (searchRequest == pending) searchProblem = "暂时无法读取这页 QQ 音乐结果。"; }
            finally
            {
                if (searchRequest == pending) { searchRequest = null; UpdateSearchSummary(); }
                pending.Dispose();
            }
        }

        private void AddResult(QqTrack track)
        {
            string detail = track.Artist;
            if (!String.IsNullOrWhiteSpace(track.Album))
                detail = String.IsNullOrWhiteSpace(detail) ? track.Album : detail + " · " + track.Album;
            Row(track.Title, detail, delegate {
                return Perform(delegate { return router.PlaySearchResultAsync(track, CancellationToken.None); });
            }, track);
        }

        private void RenderResults()
        {
            rows.Children.Clear();
            foreach (QqTrack track in results) AddResult(track);
            UpdateSearchSummary();
        }

        private void UpdateSearchSummary()
        {
            bool show = !libraryOpen && resultQuery.Length != 0;
            searchSummary.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            moreResults.Visibility = show && hasMoreResults && (nextPage > 1 || searchProblem.Length != 0)
                ? Visibility.Visible : Visibility.Collapsed;
            moreResults.IsEnabled = !working && searchRequest == null && hasMoreResults;
            moreResults.Content = searchRequest != null ? "正在加载…" : searchProblem.Length != 0 ? "重试此页" : "加载更多";
            if (!show) return;
            if (searchProblem.Length != 0) searchSummary.Text = searchProblem;
            else if (searchRequest != null && nextPage == 1) searchSummary.Text = "正在搜索“" + resultQuery + "”…";
            else if (results.Count == 0 && !hasMoreResults) searchSummary.Text = "没有找到与“" + resultQuery + "”匹配的曲目。";
            else searchSummary.Text = "已显示 " + results.Count + " 首" + (resultTotal.HasValue && resultTotal.Value >= results.Count
                ? "，QQ 音乐共匹配 " + resultTotal.Value + " 首" : "") + "。";
        }
    }
}
