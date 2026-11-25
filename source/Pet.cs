using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: AssemblyTitle("杏菜桌宠 · 参考音色")]
[assembly: AssemblyDescription("杏菜桌面伙伴，支持中文 GPT 对话、日语配音与人物动作。")]
[assembly: AssemblyVersion("1.42.0.0")]
[assembly: AssemblyFileVersion("1.42.0.0")]

namespace AnnaDesktopPet
{
    internal static class Program
    {
        private static string diagnosticDataDir;
        internal static string DataDir { get { return diagnosticDataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnnaDesktopPet"); } }
        internal static bool Smoke;
        internal static bool UiCheck;
        internal static bool OpenChatOnStart;
        internal static bool MediaCheck;
        internal static bool PanelCheck;
        internal static bool ChatPreview, MusicPreview, RailPreview, VoicePreview, NarrowPreview, PreviewWithPet;
        internal static string SmokePath;
        internal static string GhostPreviewDirectory;
        internal static string Uniform2PreviewDirectory;
        internal static string Uniform2ActionsPreviewDirectory;
        internal static string PortraitConversationPreviewDirectory;
        internal static string GhostActionsPreviewDirectory;
        [STAThread]
        public static int Main(string[] args)
        {
            Smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            UiCheck = Array.IndexOf(args, "--ui-check") >= 0;
            OpenChatOnStart = Array.IndexOf(args, "--chat") >= 0;
            MediaCheck = Array.IndexOf(args, "--media-check") >= 0;
            ChatPreview = Array.IndexOf(args, "--chat-preview") >= 0;
            MusicPreview = Array.IndexOf(args, "--music-preview") >= 0;
            RailPreview = Array.IndexOf(args, "--rail-preview") >= 0;
            PreviewWithPet = Array.IndexOf(args, "--with-pet") >= 0;
            VoicePreview = Array.IndexOf(args, "--voice-preview") >= 0;
            NarrowPreview = Array.IndexOf(args, "--narrow-preview") >= 0;
            PanelCheck = Array.IndexOf(args, "--panel-check") >= 0 || ChatPreview || MusicPreview || RailPreview || VoicePreview;
            if (Smoke || MediaCheck || PanelCheck) diagnosticDataDir = Path.Combine(Path.GetTempPath(), "AnnaDesktopPet-Diagnostic", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            int outputAt = Array.IndexOf(args, "--report");
            if (outputAt >= 0 && outputAt + 1 < args.Length) SmokePath = args[outputAt + 1];
            int previewAt = Array.IndexOf(args, "--ghost-preview");
            if (Smoke && previewAt >= 0 && previewAt + 1 < args.Length) GhostPreviewDirectory = Path.GetFullPath(args[previewAt + 1]);
            int uniformAt = Array.IndexOf(args, "--uniform2-preview");
            if (Smoke && uniformAt >= 0 && uniformAt + 1 < args.Length) Uniform2PreviewDirectory = Path.GetFullPath(args[uniformAt + 1]);
            int uniformActionsAt = Array.IndexOf(args, "--uniform2-actions-preview");
            if (Smoke && uniformActionsAt >= 0 && uniformActionsAt + 1 < args.Length) Uniform2ActionsPreviewDirectory = Path.GetFullPath(args[uniformActionsAt + 1]);
            int conversationAt = Array.IndexOf(args, "--portrait-conversation-preview");
            if (Smoke && conversationAt >= 0 && conversationAt + 1 < args.Length) PortraitConversationPreviewDirectory = Path.GetFullPath(args[conversationAt + 1]);
            int actionsAt = Array.IndexOf(args, "--ghost-actions-preview");
            if (Smoke && actionsAt >= 0 && actionsAt + 1 < args.Length) GhostActionsPreviewDirectory = Path.GetFullPath(args[actionsAt + 1]);
            bool first;
            using (Mutex mutex = new Mutex(true, PanelCheck ? "Local\\AnnaDesktopPet.v1.panel-check" : MediaCheck ? "Local\\AnnaDesktopPet.v1.media-check" : "Local\\AnnaDesktopPet.v1", out first))
            {
                if (!first && !Smoke)
                {
                    MessageBox.Show("已有桌宠正在运行，本次启动的 v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(2) + " 没有替换它。\n\n请先在当前桌宠的菜单中选择“退出桌宠”，再重新打开。\n聊天窗口标题中的版本号可以确认是否已切换。", "杏菜 · 已有桌宠运行", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }
                try
                {
                    Directory.CreateDirectory(DataDir);
                    App app = new App();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        Log(e.Exception.ToString());
                        MessageBox.Show("杏菜遇到了一点问题，详细信息已保存到：\n" + Path.Combine(DataDir, "pet.log"), "杏菜 · 桌面伙伴");
                        e.Handled = true;
                        app.Shutdown(1);
                    };
                    return app.Run(new PetWindow());
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    if (Smoke && SmokePath != null) File.WriteAllText(SmokePath, "FAIL\n" + ex, System.Text.Encoding.UTF8);
                    else MessageBox.Show("无法启动桌宠：\n" + ex.Message, "杏菜 · 桌面伙伴");
                    return 1;
                }
                finally { if (first) mutex.ReleaseMutex(); }
            }
        }
        internal static void Log(string value)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                string log = Path.Combine(DataDir, "pet.log");
                if (File.Exists(log) && new FileInfo(log).Length > 150000) File.WriteAllText(log, "");
                File.AppendAllText(log, DateTime.Now.ToString("s") + " " + value + Environment.NewLine);
            }
            catch { }
        }
    }

    internal sealed class App : Application { }

    internal sealed class Settings
    {
        public string Nickname = "杏菜";
        public double X = double.NaN, Y = double.NaN;
        public int Size = 220;
        public bool OnTop = true;
        public bool Sleeping;
        private static string FileName { get { return Path.Combine(Program.DataDir, "settings.ini"); } }
        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FileName)) return s;
            foreach (string line in File.ReadAllLines(FileName))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                double d; int n; bool b;
                if (key == "name" && value.Trim().Length > 0) s.Nickname = value.Substring(0, Math.Min(12, value.Length));
                if (key == "x" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsInfinity(d)) s.X = d;
                if (key == "y" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsInfinity(d)) s.Y = d;
                if (key == "size" && int.TryParse(value, out n)) s.Size = Math.Max(150, Math.Min(340, n));
                if (key == "top" && bool.TryParse(value, out b)) s.OnTop = b;
                if (key == "sleep" && bool.TryParse(value, out b)) s.Sleeping = b;
            }
            return s;
        }
        public void Save()
        {
            if (Program.Smoke) return;
            Directory.CreateDirectory(Program.DataDir);
            File.WriteAllLines(FileName, new string[] { "name=" + Nickname.Replace("\r", "").Replace("\n", ""), "x=" + X.ToString(CultureInfo.InvariantCulture), "y=" + Y.ToString(CultureInfo.InvariantCulture), "size=" + Size, "top=" + OnTop, "sleep=" + Sleeping });
        }
    }

    internal sealed partial class PetWindow : Window
    {
        private readonly Settings settings;
        private readonly Image character;
        private readonly Grid characterLayer;
        private readonly Border bubble;
        private readonly TextBlock speech;
        private readonly TextBlock stateBadge;
        private readonly TranslateTransform badgeFloat = new TranslateTransform();
        private readonly ScaleTransform breathe = new ScaleTransform(1, 1);
        private readonly RotateTransform tilt = new RotateTransform(0);
        private readonly TranslateTransform floatAt = new TranslateTransform();
        private readonly DispatcherTimer tick = new DispatcherTimer();
        private readonly DispatcherTimer clickTimer = new DispatcherTimer();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private BitmapSource bitmap;
        private byte[] alphaPixels;
        private int pixelWidth, pixelHeight;
        private MotionFrame standingPortrait;
        private Live2DPetModel liveModel;
        private readonly LiveFaceTracking liveFace = new LiveFaceTracking(new Random());
        private readonly IdleGestures idleGestures;
        private readonly GhostGestures ghostGestures;
        private readonly Uniform2GestureDirector uniform2Director;
        private double lastLiveFrame = -1;
        private string standingPose;
        private IdleGesturePose standingTransitionFrom, standingExpression;
        private double standingTransitionStart;
        private readonly AutonomousMotion autonomous = new AutonomousMotion();
        private ContextMenu activeMenu;
        private string activePose;
        private string temporaryPose = "idle";
        private double poseUntil, poseStarted, actionVoiceDeadline;
        private long actionVoiceOperation = -1;
        private readonly SpeechSettings mediaSettings;
        private readonly SpeechService voice;
        private readonly MusicRouter localMusic;
        private PetMediaWindow mediaWindow;
        private Point pressScreen, pressWindow;
        private bool pressed, dragged;
        private double lastClickTime = -100;
        private Point lastClickScreen;
        private double nextOtterIdle;
        private int otterIdleCount;
        private double bubbleUntil, happyUntil, lastHover;
        private DateTime? focusEnd;
        private int focusTotal;
        private long focusVoiceOperation = -1;
        private bool focusVoicePending;
        private Action<string> focusVoiceSmokeHook;
        private GptChatWindow chatWindow;
        private readonly ConversationHistory chatHistory = ConversationHistory.LoadDefault();
        private readonly GptSettings gptSettings;
        private bool thinking;
        private bool waitingForReplyMotion;
        private double submittedMotionAt, briefReplyUntil = -1;
        private PortraitConversationState portraitConversation;
        private bool HasConversationPortrait { get { return liveModel != null; } }
        private TextBlock focusLabel;
        private bool closing;
        private DesktopShell desktopShell;
        private VoiceChat voiceChat;
        private CallWindow callWindow;
        internal void OpenCall()
        {
            if (closing || hiddenForGame) return;
            if (callWindow != null) { callWindow.Show(); callWindow.Activate(); return; }
            if (String.IsNullOrWhiteSpace(gptSettings.ApiKey)) { Say("请先在模型设置中填写 API Key。",6); return; }
            if (settings.Sleeping) { Say("先叫醒杏菜，再打电话吧。",6); return; }
            if (voiceChat == null) return;
            voiceChat.CancelCapture();
            OpenChat();
            callWindow = new CallWindow(this, chatWindow, voiceChat.Worker, voice);
            callWindow.Closed += delegate { callWindow = null; if (!closing && !hiddenForGame && chatWindow != null) chatWindow.Show(); };
            chatWindow.Closed += delegate { if (callWindow != null) callWindow.Close(); };
            chatWindow.Hide();
            callWindow.Show();
        }

        private ProgressBar voiceLevel;
        internal void SetVoiceLevel(bool visible,double level){if(voiceLevel!=null){voiceLevel.Visibility=visible&&!hiddenForGame?Visibility.Visible:Visibility.Collapsed;voiceLevel.Value=level*100;}SetRailRecording(visible && voiceChat != null && voiceChat.RecordingFromButton);}
        private void OpenVoiceInput(){if(voiceChat!=null)voiceChat.ShowSettings();}
        internal void ReviewVoiceTranscript(string text){if(hiddenForGame || closing)return;OpenChat();chatWindow.ReviewVoice(text);}
        internal bool ReplyAudioPlaying { get { return voice.IsSpeaking; } }
        internal void SpeakVoiceReply(string text){if(!hiddenForGame && !closing && !String.IsNullOrWhiteSpace(text))voice.Speak(text);}
        private bool hiddenForGame, loadedOnce;
        private string deferredFocusNotice;
        private void OnShellVisibilityChanged(bool hidden)
        {
            hiddenForGame = hidden;
            if (hidden)
            {
                waitingForReplyMotion = false;
                portraitConversation = PortraitConversationState.None;
                if (liveModel != null) liveModel.ResetConversationMotion();
            }
            idleGestures.Reset();
            ghostGestures.Reset();
            uniform2Director.Reset();
            if(hidden && voiceChat!=null)voiceChat.CancelCapture();
            if(hidden && callWindow!=null)callWindow.HangUp();
            clickTimer.Stop(); lastClickTime = -100;
            if (activeMenu != null) activeMenu.IsOpen = false;
            pressed = dragged = false; ReleaseMouseCapture();
            autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);
            bubble.Visibility = Visibility.Collapsed;
            actionVoiceOperation = -1; focusVoicePending = false;
            if (!hidden && !String.IsNullOrEmpty(deferredFocusNotice))
            { Say(deferredFocusNotice, 12); deferredFocusNotice = null; }
        }
        private bool changingOutfit;
        private Border dressingRoom;
        private async void ChangeOutfit()
        {
            if(closing || changingOutfit)return;
            if (Program.Smoke) { LoadCharacter(); return; }
            changingOutfit=true;
            try {
                autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);
                Say("不许偷看哦",5);
                ReadLocal("覗いちゃだめだよ！");
                PlayMotion("idle",1);
                dressingRoom=new Border { Width=System.Math.Max(100,Width*.82),Height=System.Math.Max(150,Height-100),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,8),IsHitTestVisible=false,Opacity=0 };
                var boothImage=new BitmapImage();using(var stream=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","dressing-room.png"))){boothImage.BeginInit();boothImage.CacheOption=BitmapCacheOption.OnLoad;boothImage.StreamSource=stream;boothImage.EndInit();boothImage.Freeze();}
                dressingRoom.Child=new Image {Source=boothImage,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Bottom};((Grid)Content).Children.Add(dressingRoom);
                dressingRoom.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(400)));
                var fade=new System.Windows.Media.Animation.DoubleAnimation(1,0,TimeSpan.FromMilliseconds(450));
                characterLayer.BeginAnimation(UIElement.OpacityProperty,fade);
                await System.Threading.Tasks.Task.Delay(480);
                if(closing)return;
                await System.Threading.Tasks.Task.Delay(2600);
                if(closing)return;
                LoadCharacter();
                PlayMotion("idle",3);
                dressingRoom.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(1,0,TimeSpan.FromMilliseconds(650)));
                characterLayer.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(650)));
                await System.Threading.Tasks.Task.Delay(680);
                if(!closing)Say("换好啦！",4);
            }catch(Exception){if(!closing)Say("换装未完成，请检查服装素材。",6);}
            finally {if(dressingRoom!=null){((Grid)Content).Children.Remove(dressingRoom);dressingRoom=null;}characterLayer.BeginAnimation(UIElement.OpacityProperty,null);changingOutfit=false;}
        }
        private Routine routine;
        private static readonly Color Ink = Color.FromRgb(40, 59, 76);
        private static readonly Color Blue = Color.FromRgb(48, 116, 170);
        private static readonly string[] Lines = new string[]
        {
            "在呢。今天有什么想一起做的？", "认真工作以后，要记得好好吃饭。", "我陪你把这一小段做完。", "休息一下眼睛吧，看看远处。", "今天也有在好好努力呢。", "点我一下，就算打过招呼啦。", "等忙完这件事，一起去吃点好的。", "慢慢来，我在旁边陪着你。"
        };
        private static readonly string[] LocalJapaneseLines = new string[]
        {
            "いるよ。今日は何を一緒にしようか？", "お仕事を頑張った後は、ちゃんとご飯も食べようね。", "この作業が終わるまで、そばにいるね。", "少し目を休めて、遠くを見てみよう。", "今日もよく頑張ってるね。", "クリックしてくれたら、ちゃんと挨拶するよ。", "これが終わったら、一緒においしいものを食べに行こうね。", "焦らなくて大丈夫。ここで一緒にいるよ。"
        };

        public PetWindow()
        {
            idleGestures = new IdleGestures(random);
            ghostGestures = new GhostGestures(random);
            uniform2Director = new Uniform2GestureDirector(random);
            settings = Settings.Load();
            gptSettings = GptSettings.Load();
            mediaSettings = SpeechSettings.Load();
            LoginStartup.RefreshIfEnabled();
            Appearance.OutfitChanged+=ChangeOutfit;
            Closed+=delegate {Appearance.OutfitChanged-=ChangeOutfit;};
            VoiceAutoStart.Start(mediaSettings);
            VoicePerformance.ProfileRoot = Program.DataDir;
            voice = new SpeechService(Dispatcher);
            localMusic = new MusicRouter(delegate(string message) { if (!closing) Say(message, 8); });
            localMusic.OpenMusicPanel = delegate { ShowCompanionTab("music"); };
            routine=new Routine(delegate(string text,string japanese) { if(closing || hiddenForGame || settings.Sleeping || thinking || voice.IsBusy)return; Say(text,10); ReadLocal(japanese); });
            Loaded+=delegate { routine.Start(); };
            Closed+=delegate { routine.Stop(); };
            voice.ConfigureBackend(mediaSettings.Engine, mediaSettings.Endpoint, mediaSettings.ReferenceAudio, mediaSettings.ReferenceText);
            voice.Configure(mediaSettings.VoiceId, mediaSettings.Rate, mediaSettings.Volume);
            voice.StatusChanged += OnFocusVoiceStatus;
            voice.SpeakingChanged += OnFocusVoiceSpeaking;
            Title = "杏菜 · 桌面伙伴";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = Program.UiCheck;
            ShowActivated = Program.UiCheck;
            Topmost = settings.OnTop;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Grid root = new Grid();
            Content = root;
            character = new Image();
            character.Stretch = Stretch.Uniform;
            character.VerticalAlignment = VerticalAlignment.Bottom;
            character.HorizontalAlignment = HorizontalAlignment.Center;
            character.Margin = new Thickness(22, 90, 22, 10);
            character.RenderTransformOrigin = new Point(.5, .88);
            TransformGroup transforms = new TransformGroup();
            transforms.Children.Add(breathe);
            transforms.Children.Add(tilt);
            transforms.Children.Add(floatAt);
            character.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(character, BitmapScalingMode.HighQuality);
            characterLayer = new Grid();
            characterLayer.Children.Add(character);
            root.Children.Add(characterLayer);
            stateBadge = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(91, 150, 190)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 98, 20, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false, RenderTransform = badgeFloat };
            root.Children.Add(stateBadge);

            speech = new TextBlock();
            speech.FontSize = 13;
            speech.Foreground = new SolidColorBrush(Ink);
            speech.TextWrapping = TextWrapping.Wrap;
            speech.LineHeight = 21;
            speech.TextAlignment = TextAlignment.Center;
            bubble = new Border();
            bubble.Background = new SolidColorBrush(Color.FromArgb(248, 250, 253, 255));
            bubble.BorderBrush = new SolidColorBrush(Color.FromRgb(208, 227, 239));
            bubble.BorderThickness = new Thickness(1);
            bubble.CornerRadius = new CornerRadius(17, 17, 4, 17);
            bubble.Padding = new Thickness(13, 9, 13, 9);
            bubble.Margin = new Thickness(12, 4, 12, 0);
            bubble.VerticalAlignment = VerticalAlignment.Top;
            bubble.MaxHeight = 83;
            bubble.Visibility = Visibility.Collapsed;
            bubble.Child = speech;
            root.Children.Add(bubble);
            voiceLevel=new ProgressBar{Minimum=0,Maximum=100,Width=140,Height=7,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,88,0,0),Visibility=Visibility.Collapsed,Foreground=Brushes.MediumSeaGreen};root.Children.Add(voiceLevel);

            AddActionRail(root);
            LoadCharacter();
            ApplySize();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            MouseLeftButtonDown += OnPointerDown;
            MouseMove += OnPointerMove;
            MouseLeftButtonUp += OnPointerUp;
            MouseRightButtonUp += delegate { clickTimer.Stop(); lastClickTime = -100; OpenMenu(); };
            MouseEnter += delegate
            {
                if (!settings.Sleeping && elapsed.Elapsed.TotalSeconds - lastHover > 20)
                { lastHover = elapsed.Elapsed.TotalSeconds; Cursor = Cursors.Hand; }
            };
            Closed += delegate
            {
                closing = true; tick.Stop(); clickTimer.Stop(); CompositionTarget.Rendering -= Animate;
                if (liveModel != null) liveModel.Dispose();
                if (desktopShell != null) desktopShell.Dispose();
                if (callWindow != null) callWindow.Close();
                if (voiceChat != null) voiceChat.Dispose();
                if (chatWindow != null) chatWindow.Close();
                if (mediaWindow != null) mediaWindow.Close();
                localMusic.Dispose();
                voice.Dispose();
                SavePosition();
                Program.Log("Closed normally.");
            };
            tick.Interval = TimeSpan.FromMilliseconds(200);
            tick.Tick += OnTick;
            clickTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
            clickTimer.Tick += delegate { clickTimer.Stop(); Pet(); };
            CompositionTarget.Rendering += Animate;
        }

        private void VerifyDesktopShellSmoke()
        {
            Microphone.VerifyPcmSmoke();
            double left = Left, top = Top;
            int stopped = 0, chatted = 0;
            var testPanel = new Window { Owner = this, Width = 180, Height = 100, ShowActivated = false, ShowInTaskbar = false };
            var draft = new TextBox { Text = "unsent draft survives game hide" };
            testPanel.Content = draft; testPanel.Show();
            try
            {
                using (var shell = new DesktopShell(this, delegate { chatted++; }, delegate { stopped++; }, OnShellVisibilityChanged, true))
                {
                    using (var conflict = new DesktopShell(testPanel, delegate { }, delegate { }, delegate(bool hidden) { }, true))
                    {
                        if (conflict.RegisteredCount != 0) throw new Exception("Hotkey conflict must leave the tray fallback available.");
                    }
                    shell.HideAll();
                    if (testPanel.IsVisible || IsVisible || !hiddenForGame) throw new Exception("Game hide did not hide all windows.");
                    shell.HideAll(); // Repeated requests must not discard the saved panel list.
                    focusTotal = 1; focusEnd = DateTime.Now.AddSeconds(-1); OnTick(null, EventArgs.Empty);
                    if (focusEnd.HasValue || String.IsNullOrEmpty(deferredFocusNotice)) throw new Exception("Hidden focus completion was not deferred.");
                    shell.Restore();
                    if (!testPanel.IsVisible || hiddenForGame || deferredFocusNotice != null || draft.Text != "unsent draft survives game hide") throw new Exception("Restore lost a panel, draft or deferred notice.");
                    shell.VerifyMessageDispatch();
                    if (chatted != 1 || stopped != 3 || Left != left || Top != top) throw new Exception("Shell callbacks or window position changed unexpectedly.");
                }
                using (var released = new DesktopShell(testPanel, delegate { }, delegate { }, delegate(bool hidden) { }, true))
                    if (released.RegisteredCount != 3) throw new Exception("Hotkeys were not released on dispose.");
            }
            finally { testPanel.Close(); hiddenForGame = false; }
        }

        private bool UsesDefaultUniform { get { return Appearance.Outfit == Appearance.Uniform1Portrait; } }

        private void LoadCharacter()
        {
            string path = Appearance.Asset("pet.png");
            BitmapImage img = new BitmapImage();
            using (Stream stream = File.Exists(path) ? File.OpenRead(path) : Assembly.GetExecutingAssembly().GetManifestResourceStream("AnnaDesktopPet.pet.png"))
            {
                if (stream == null) throw new FileNotFoundException("缺少角色图片，请重新下载完整桌宠程序。", path);
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = stream; img.EndInit(); img.Freeze();
            }
            bitmap = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0); bitmap.Freeze();
            idleGestures.Reset();
            ghostGestures.Reset();
            uniform2Director.Reset();
            nextOtterIdle = elapsed.Elapsed.TotalSeconds + 18; otterIdleCount = 0;
            standingPose = null;
            portraitConversation = PortraitConversationState.None;
            if (liveModel != null) { liveModel.Dispose(); liveModel = null; }
            string modelFile = Appearance.ModelManifest;
            if (Environment.Is64BitProcess && File.Exists(modelFile))
            {
                try
                {
                    liveModel = Live2DPetModel.Load(modelFile);
                    liveFace.Reset();
                    lastLiveFrame = -1;
                    Program.Log("Live2D loaded: " + modelFile);
                }
                catch (Exception ex) { Program.Log("Live2D load failed: " + ex.Message); }
            }
            standingPortrait = new MotionFrame(bitmap);
            SetFrame("idle");
            if (liveModel != null) RenderLivePose("idle", elapsed.Elapsed.TotalSeconds);
        }

        private void SetFrame(string pose)
        {
            if (activePose == pose && Object.ReferenceEquals(character.Source,standingPortrait.Image)) return;
            bitmap = standingPortrait.Image; pixelWidth = standingPortrait.Width; pixelHeight = standingPortrait.Height; alphaPixels = standingPortrait.Pixels;
            SetStandingMargin(false);
            character.Source = bitmap; activePose = pose;
        }

        private void SetStandingMargin(bool live)
        {
            // The raised hands can use the space above the portrait without rescaling
            // the character or changing where her feet land when switching poses.
            double extra = live ? settings.Size * 1.25 * Live2DPetModel.OverheadPadding / Live2DPetModel.PortraitHeight : 0;
            var margin = new Thickness(22, 90-extra, 22, 10);
            if (character.Margin != margin) character.Margin = margin;
        }

        private void ApplySize()
        {
            Width = settings.Size + 64;
            Height = settings.Size * 1.25 + 100;
            SetStandingMargin(liveModel != null && character.Source == liveModel.Frame);
            if (IsLoaded) KeepVisible();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                IntPtr handle = source.Handle;
                int style = GetWindowLong(handle, -20);
                SetWindowLong(handle, -20, Program.UiCheck ? ((style & ~0x00000080 & ~0x08000000) | 0x00040000) : (style | 0x00000080 | 0x08000000));
                source.AddHook(WindowMessage);
            }
            if (!Program.Smoke && !Program.MediaCheck && !Program.PanelCheck && !Program.UiCheck)
            {
                try { desktopShell = new DesktopShell(this, OpenChat, voice.Stop, OnShellVisibilityChanged); }
                catch (Exception ex) { Program.Log("Tray initialization failed: " + ex.Message); }
                voiceChat=new VoiceChat(this,delegate(string text){OpenChat();chatWindow.SendVoice(text);},delegate(string message){Say(message,8);},delegate {if(hiddenForGame || closing)return false;if(String.IsNullOrWhiteSpace(gptSettings.ApiKey)){Say("请先在模型设置中填写 API Key。",5);return false;}if(chatWindow!=null && !chatWindow.PrepareVoiceInput()){Say("输入框里还有未发送的文字，请先发送或清空。",5);return false;}voice.Stop();return true;},delegate {return !hiddenForGame && !closing && !String.IsNullOrWhiteSpace(gptSettings.ApiKey) && (chatWindow==null || chatWindow.CanAcceptVoice);});
            }
        }

        private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg == 0x0084)
            {
                int packed = unchecked((int)lp.ToInt64());
                Point screen = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
                if (!IsOpaqueAt(screen)) { handled = true; return new IntPtr(-1); }
            }
            return IntPtr.Zero;
        }

        private bool IsOpaqueAt(Point screen)
        {
            if (pressed) return true;
            if (HitActionRail(screen)) return true;
            if (bubble.Visibility == Visibility.Visible)
            {
                Point b = bubble.PointFromScreen(screen);
                if (b.X >= 0 && b.Y >= 0 && b.X < bubble.ActualWidth && b.Y < bubble.ActualHeight) return true;
            }
            Point p = character.PointFromScreen(screen);
            double fit = Math.Min(character.ActualWidth / pixelWidth, character.ActualHeight / pixelHeight);
            if (fit <= 0) return false;
            double ox = (character.ActualWidth - pixelWidth * fit) / 2;
            double oy = (character.ActualHeight - pixelHeight * fit) / 2;
            int x = (int)Math.Floor((p.X - ox) / fit), y = (int)Math.Floor((p.Y - oy) / fit);
            if (x < 0 || y < 0 || x >= pixelWidth || y >= pixelHeight) return false;
            int index = (y * pixelWidth + x) * 4 + 3;
            return alphaPixels[index] >= 32;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (loadedOnce) return;
            loadedOnce = true;
            Rect area = SystemParameters.WorkArea;
            Left = double.IsNaN(settings.X) ? area.Right - Width - 35 : settings.X;
            Top = double.IsNaN(settings.Y) ? area.Bottom - Height - 12 : settings.Y;
            KeepVisible();
            tick.Start();
            Say(settings.Sleeping ? "我先小憩一下。有事可以双击找我聊天。" : "我是" + settings.Nickname + "。拖动我换个位置，双击就能聊天。", 7);
            Program.Log("Started; image=" + pixelWidth + "x" + pixelHeight);
            if (!Program.Smoke && Program.OpenChatOnStart) Dispatcher.BeginInvoke(new Action(OpenChat), DispatcherPriority.ApplicationIdle);
            if (!Program.Smoke && Program.MediaCheck) Dispatcher.BeginInvoke(new Action(OpenMedia), DispatcherPriority.ApplicationIdle);
            if (!Program.Smoke && Program.PanelCheck) Dispatcher.BeginInvoke(new Action(delegate
            {
                if (Program.RailPreview || Program.PreviewWithPet) bubble.Visibility = Visibility.Collapsed;
                if (Program.ChatPreview) OpenChat();
                else if (Program.MusicPreview) ShowCompanionTab("music");
                else if (Program.VoicePreview) OpenMedia();
                else if (!Program.RailPreview) OpenPanel();
                if (!string.IsNullOrEmpty(Program.SmokePath)) Dispatcher.BeginInvoke(new Action(WritePanelPreview), DispatcherPriority.ApplicationIdle);
            }), DispatcherPriority.ApplicationIdle);
            if (Program.Smoke)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        string liveSmoke = VerifyLiveModelSmoke() + "\n" + VerifyGhostOutfitSmoke() + "\n" + VerifyUniform2OutfitSmoke() + "\n" + VerifyPortraitConversationSmoke();
                        if (pixelWidth < 1 || pixelHeight < 1 || alphaPixels.Length == 0) throw new Exception("Character resource invalid");
                        bool alpha = false, opaque = false;
                        int alphaMin = 255, alphaMax = 0, clickPixels = 0;
                        for (int i = 3; i < alphaPixels.Length; i += 4)
                        {
                            int a = alphaPixels[i];
                            if (a == 0) alpha = true; if (a > 128) opaque = true;
                            alphaMin = Math.Min(alphaMin, a); alphaMax = Math.Max(alphaMax, a); if (a >= 32) clickPixels++;
                        }
                        if (!alpha || !opaque) throw new Exception("Character requires transparent and visible pixels");
                        bubble.Visibility = Visibility.Collapsed;
                        UpdateLayout();
                        double fit = Math.Min(character.ActualWidth / pixelWidth, character.ActualHeight / pixelHeight);
                        double offsetX = (character.ActualWidth - pixelWidth * fit) / 2;
                        double offsetY = (character.ActualHeight - pixelHeight * fit) / 2;
                        int checkedSolid = 0, checkedClear = 0, adjacent = 0, acrossEdge = 0;
                        const int sampleCount = 16;
                        for (int y = 4; y < pixelHeight && (checkedSolid < sampleCount || checkedClear < sampleCount); y += 7)
                            for (int x = 4; x < pixelWidth && (checkedSolid < sampleCount || checkedClear < sampleCount); x += 7)
                            {
                                byte a = alphaPixels[(y * pixelWidth + x) * 4 + 3];
                                if ((checkedSolid < sampleCount && a >= 128) || (checkedClear < sampleCount && a == 0))
                                {
                                    Point screen = character.PointToScreen(new Point(offsetX + (x + .5) * fit, offsetY + (y + .5) * fit));
                                    // Windows receives integer screen pixels. At normal pet size several
                                    // texture pixels can share one screen pixel, particularly at hair edges.
                                    Point reachable = character.PointFromScreen(screen);
                                    int rx = (int)Math.Floor((reachable.X-offsetX)/fit);
                                    int ry = (int)Math.Floor((reachable.Y-offsetY)/fit);
                                    if (rx != x || ry != y)
                                    {
                                        adjacent++;
                                        if (rx >= 0 && ry >= 0 && rx < pixelWidth && ry < pixelHeight
                                            && (alphaPixels[(ry * pixelWidth + rx) * 4 + 3] >= 32) != (a >= 32)) acrossEdge++;
                                        continue;
                                    }
                                    // The shortcuts cover the desktop at their own circular hit targets.
                                    if (HitActionRail(screen)) continue;
                                    if (IsOpaqueAt(screen) != (a >= 128))
                                        throw new Exception(String.Format("Portrait hit mismatch at texture ({0}, {1}), screen {2}, alpha {3}",x,y,screen,a));
                                    if (a >= 128) checkedSolid++; else checkedClear++;
                                }
                            }
                        if (checkedSolid < sampleCount || checkedClear < sampleCount) throw new Exception("Alpha hit test samples missing");
                        AutonomousMotion autoTest = new AutonomousMotion();
                        double initial = autoTest.Update(24, 100, 100, 0, 500, true);
                        if (initial != 100 || autoTest.Walking) throw new Exception("Autonomous motion started too early");
                        autoTest.Update(25, 100, 100, 0, 500, true);
                        if (!autoTest.Walking) throw new Exception("Autonomous walk did not start");
                        double moved = autoTest.Update(25.1, 100, 100, 0, 500, true);
                        if (moved == 100 || moved < 0 || moved > 400) throw new Exception("Autonomous walk position invalid");
                        if (autoTest.Update(25.2, moved, 100, 0, 500, false) != moved || autoTest.Walking) throw new Exception("Autonomous walk failed to interrupt");
                        autoTest.Update(26, moved, 100, 0, 500, true);
                        if (autoTest.Walking) throw new Exception("Autonomous walk resumed too soon");
                        if (autoTest.Update(90, -50, 100, 0, 500, true) != -50 || autoTest.Walking) throw new Exception("Autonomous walk changed position outside work area");
                        AutonomousMotion busyTest = new AutonomousMotion();
                        busyTest.Update(100, 100, 100, 0, 500, false);
                        busyTest.Update(1000, 100, 100, 0, 500, false);
                        busyTest.Update(1024, 100, 100, 0, 500, true);
                        if (busyTest.Walking) throw new Exception("Autonomous walk started immediately after busy period");
                        busyTest.Update(1025, 100, 100, 0, 500, true);
                        if (!busyTest.Walking) throw new Exception("Autonomous walk failed to start after idle delay");
                        int checkedMinutes;
                        if (!TryFocusMinutes("37", out checkedMinutes) || checkedMinutes != 37 || TryFocusMinutes("0", out checkedMinutes) || TryFocusMinutes("1441", out checkedMinutes) || TryFocusMinutes("1.5", out checkedMinutes)) throw new Exception("Focus duration validation failed");
                        StartFocus(5);
                        if (!focusEnd.HasValue || focusTotal != 5) throw new Exception("Focus timer failed");
                        int alarms = 0; focusVoiceSmokeHook = delegate(string message) { if (!ContainsJapaneseKana(message)) throw new Exception("Focus voice must be Japanese"); alarms++; };
                        settings.Sleeping = true; mediaSettings.ReadLocal = false;
                        focusEnd = DateTime.Now.AddSeconds(-1); OnTick(null, EventArgs.Empty);
                        OnTick(null, EventArgs.Empty);
                        if (focusEnd.HasValue || alarms != 1) throw new Exception("Focus alarm must fire exactly once even if local speech is disabled and pet sleeps");
                        focusVoiceSmokeHook = null; settings.Sleeping = false;
                        settings.Sleeping = true; Animate(null, EventArgs.Empty); settings.Sleeping = false;
                        OpenPanel();
                        if (chatWindow == null || !chatWindow.IsVisible || chatWindow.SelectedCompanionTab != "settings") throw new Exception("Companion settings failed");
                        chatWindow.Close();
                        GptChatWindow checkChat = new GptChatWindow(this, new List<ChatMessage>(), new GptSettings(), Say, delegate(bool active) { thinking = active; }, OnAssistantReply, voice.Stop, delegate { return false; });
                        checkChat.Show(); checkChat.VerifyNoKeySmoke(); checkChat.Close();
                        GptChatWindow musicCheck = new GptChatWindow(this, new List<ChatMessage>(), new GptSettings(), Say, delegate(bool active) { }, null, null, delegate { return false; }, delegate(string text) { return System.Threading.Tasks.Task.FromResult(text == "播放一首晴天" ? "测试点歌已处理" : null); });
                        musicCheck.Show(); musicCheck.VerifyMusicCommandSmoke(); musicCheck.Close();
                        int candidateSearches = 0, candidateDispatches = 0;
                        QqTrack selectedCandidate = null;
                        QqTrack firstCandidate = new QqTrack { Id = "498054208", Title = "LOVE 2000", Artist = "遠野ひかる", Album = "LOVE 2000", Type = 0 };
                        MusicRouter candidateRouter = new MusicRouter(
                            delegate(string message) { throw new Exception("Candidate smoke must not play media"); },
                            delegate(string url) { throw new Exception("Candidate smoke must not open websites"); },
                            new string[0],
                            delegate(string query, System.Threading.CancellationToken token)
                            {
                                token.ThrowIfCancellationRequested(); candidateSearches++;
                                if (query != "LOVE 2000") throw new Exception("Unexpected candidate smoke query");
                                return System.Threading.Tasks.Task.FromResult(new List<QqTrack> {
                                    firstCandidate,
                                    new QqTrack { Id = "498054209", Title = "LOVE 2000", Artist = "別の歌手", Album = "別バージョン", Type = 0 }
                                });
                            },
                            delegate(QqTrack track, System.Threading.CancellationToken token)
                            {
                                token.ThrowIfCancellationRequested(); candidateDispatches++; selectedCandidate = track;
                                return System.Threading.Tasks.Task.FromResult(0);
                            });
                        GptChatWindow candidateChat = new GptChatWindow(this, new List<ChatMessage>(), new GptSettings(), Say, delegate(bool active) { }, null, null, delegate { return false; }, candidateRouter.Handle, candidateRouter.CancelPending);
                        try
                        {
                            candidateChat.Show(); candidateChat.VerifyMusicSelectionSmoke();
                            if (candidateSearches != 1 || candidateDispatches != 1 || !Object.ReferenceEquals(selectedCandidate, firstCandidate))
                                throw new Exception("QQ candidate smoke must dispatch the first displayed track exactly once");
                        }
                        finally { candidateChat.Close(); candidateRouter.Dispose(); }
                        RenderLivePose("idle", elapsed.Elapsed.TotalSeconds);
                        OpenMedia(); if (mediaWindow == null || !mediaWindow.IsVisible) throw new Exception("Media settings failed"); mediaWindow.VerifySmoke(); mediaWindow.Close();
                        if (Lines.Length != LocalJapaneseLines.Length) throw new Exception("Local Japanese line mapping length mismatch");
                        foreach (string action in new string[] { "wave", "happy", "eat" }) if (!ContainsJapaneseKana(ActionJapanese(action))) throw new Exception("Action voice must be Japanese");
                        if (ActionJapanese("idle") != null) throw new Exception("Idle should not trigger action speech");
                        foreach (string japaneseLine in LocalJapaneseLines) if (!ContainsJapaneseKana(japaneseLine)) throw new Exception("Local line must contain Japanese kana");
                        SpeechSettings freshMedia = new SpeechSettings();
                        if (freshMedia.VoiceLanguage != "ja" || freshMedia.Engine != "gpt-sovits" || freshMedia.Endpoint != "http://127.0.0.1:9880" || freshMedia.AutoReadReplies || freshMedia.ReadLocal) throw new Exception("Japanese media defaults invalid");
                        if (voice.IsSpeaking) throw new Exception("Smoke must never play audio");
                        string report = "PASS\nWPF window initialized\nPNG loaded: " + pixelWidth + " x " + pixelHeight + "\nAlpha range: " + alphaMin + ".." + alphaMax + "; clickable pixels >= 32: " + clickPixels + "\nAlpha hit testing: opaque hit + transparent pass-through verified\nSettings parsed without mutation\nFocus start and completion passed\nSleep animation initialized\nControl panel opened and closed\nGPT chat window initialized; missing-key send blocked; draft preserved; password settings initialized\nCurrent portrait poses initialized with the displayed model alpha cache\nJapanese local lines mapped to all Chinese interaction lines\nJapanese voice preference initialized; audio switches default off\nSpeech and motion settings initialized; no audio was played\nSmoke made no external network requests; GPT requests only on explicit user send\nNo autostart; no admin requirement\n";
                        report += String.Format("Screen-coordinate samples: {0} solid, {1} transparent; {2} projected to neighboring texture pixels, {3} across alpha edges\n",checkedSolid,checkedClear,adjacent,acrossEdge);
                        report += liveSmoke + "\n";
                        report += "Chat toolbar settings button opened settings; local chat history clear available; manual memory controls absent\n";
                        report += "Autonomous idle delay and interruption verified\n";
                        report += "QQ candidate selection in no-key chat: search -> 第一首 -> first displayed track exactly once; local roles only; mock network and playback\n";
                        VerifyActionRailSmoke();
                        report += "Five pet shortcuts: circular hit targets, transparent gaps, shared chat and music routes verified\n";
                        VerifyDesktopShellSmoke();
                        report += "Tray/hotkey checks: native registration and cleanup; hide/restore panels and position; chat restore; stop speech; conflict fallback; no audio or network\n";
                        if (Program.SmokePath != null) File.WriteAllText(Program.SmokePath, report, System.Text.Encoding.UTF8);
                        Program.Log("Smoke test passed.");
                        Close();
                    }
                    catch (Exception ex)
                    {
                        if (Program.SmokePath != null) File.WriteAllText(Program.SmokePath, "FAIL\n" + ex, System.Text.Encoding.UTF8);
                        Program.Log("Smoke test failed: " + ex);
                        Application.Current.Shutdown(1);
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        private string VerifyLiveModelSmoke()
        {
            string original = Appearance.Outfit;
            bool enabled = mediaSettings.MotionsEnabled;
            try
            {
                if (original != Appearance.Uniform1Portrait) SelectSmokeOutfit(Appearance.Uniform1Portrait);
                if (liveModel == null || !File.Exists(Appearance.ModelManifest) || liveModel.HasGlasses || liveModel.HasIndependentFlames)
                    throw new Exception("School uniform 1 did not load its current model.");
                mediaSettings.MotionsEnabled = true;
                foreach (string pose in new[] { "idle", "wave", "happy", "walk", "think", "eat", "speak", "rest", "sleep" })
                {
                    PlayMotion(pose, 2);
                    AnimateAt(poseStarted + .02);
                    AnimateAt(poseStarted + .48);
                    if (activePose != (pose == "rest" ? "sleep" : pose) ||
                        !Object.ReferenceEquals(character.Source, liveModel.Frame) ||
                        !Object.ReferenceEquals(alphaPixels, liveModel.Pixels))
                        throw new Exception("School uniform 1 model frame or hit cache missing for " + pose);
                }
                VerifyIdleGestureFrames();
                return "Live2D: school uniform 1 loaded; nine pose routes stay on the portrait and its alpha hit cache; five idle gestures and neutral return verified";
            }
            finally
            {
                poseUntil = 0;
                mediaSettings.MotionsEnabled = enabled;
                if (Appearance.Outfit != original) SelectSmokeOutfit(original);
            }
        }

        private void VerifyIdleGestureFrames()
        {
            liveModel.Render(0, 0, 0, 1, 0, new IdleGesturePose());
            byte[] neutral = (byte[])liveModel.Pixels.Clone();
            int stride = liveModel.Frame.PixelWidth * 4;
            foreach (IdleGesture kind in Enum.GetValues(typeof(IdleGesture)))
            {
                if (kind == IdleGesture.None) continue;
                IdleGesturePose pose = IdleGestures.Sample(kind, IdleGestures.Duration(kind) * .5, 1);
                liveModel.Render(0, -1, 1, 1, 0, pose);
                int end = (Live2DPetModel.OverheadPadding+(int)(Live2DPetModel.PortraitHeight*.62))*stride;
                int differences = 0;
                for (int i = 0; i < end && differences < 32; i++)
                    if (neutral[i] != liveModel.Pixels[i]) differences++;
                if (differences < 32) throw new Exception("No visible Live2D gesture in its expected region: " + kind);
                byte[] posePixels = (byte[])liveModel.Pixels.Clone();
                liveModel.Render(0, 1, -1, 1, 0, pose);
                for (int i = 0; i < posePixels.Length; i++)
                    if (posePixels[i] != liveModel.Pixels[i]) throw new Exception("Mouse changed action gaze: " + kind);
            }
            liveModel.Render(0, 0, 0, 1, 0, new IdleGesturePose());
            for (int i = 0; i < neutral.Length; i++)
                if (neutral[i] != liveModel.Pixels[i]) throw new Exception("Live2D gesture remained after return to idle");
        }

        private void OnPointerDown(object sender, MouseButtonEventArgs e)
        {
            if (Appearance.IsGhostPortrait) ghostGestures.NoteInteraction(elapsed.Elapsed.TotalSeconds);
            if (Appearance.Outfit == Appearance.Uniform2Portrait) uniform2Director.NoteInteraction(elapsed.Elapsed.TotalSeconds);
            autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); pressed = true; dragged = false;
            pressScreen = PointToScreen(e.GetPosition(this)); pressWindow = new Point(Left, Top);
            CaptureMouse(); e.Handled = true;
        }
        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = PointToScreen(e.GetPosition(this));
            PresentationSource src = PresentationSource.FromVisual(this);
            double dx = current.X - pressScreen.X, dy = current.Y - pressScreen.Y;
            if (src != null && src.CompositionTarget != null)
            { Point transformed = src.CompositionTarget.TransformFromDevice.Transform(new Point(dx, dy)); dx = transformed.X; dy = transformed.Y; }
            if (Math.Abs(dx) + Math.Abs(dy) > 5) dragged = true;
            if (dragged) { Left = pressWindow.X + dx; Top = pressWindow.Y + dy; }
        }
        private void OnPointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false; ReleaseMouseCapture();
            if (dragged)
            {
                clickTimer.Stop(); lastClickTime = -100;
                KeepVisible(); SavePosition(); Say("这个位置不错，我就在这儿陪你。", 3);
            }
            else
            {
                Point screen = PointToScreen(e.GetPosition(this));
                double time = elapsed.Elapsed.TotalSeconds;
                bool doubleClick = time - lastClickTime <= GetDoubleClickTime() / 1000.0 && Math.Abs(screen.X - lastClickScreen.X) <= 8 && Math.Abs(screen.Y - lastClickScreen.Y) <= 8;
                clickTimer.Stop();
                if (doubleClick) { lastClickTime = -100; OpenChat(); }
                else {
                    lastClickTime = time; lastClickScreen = screen; clickTimer.Start();
                }
            }
        }
        private void KeepVisible()
        {
            double x = SystemParameters.VirtualScreenLeft, y = SystemParameters.VirtualScreenTop;
            Left = Math.Max(x, Math.Min(x + SystemParameters.VirtualScreenWidth - Width, Left));
            Top = Math.Max(y, Math.Min(y + SystemParameters.VirtualScreenHeight - Height, Top));
        }
        private void SavePosition() { settings.X = Left; settings.Y = Top; settings.Save(); }
        private void Pet()
        {
            if (settings.Sleeping) { Say("嗯……再眯一会儿。右键菜单可以叫醒我。", 4); return; }
            if (TryStartGhostGesture(GhostGesture.PlayScare, elapsed.Elapsed.TotalSeconds)) return;
            happyUntil = elapsed.Elapsed.TotalSeconds + 1.2;
            Say("你好呀！", 4.5);
        }

        private bool TryStartGhostGesture(GhostGesture kind, double now)
        {
            if (liveModel == null || !Appearance.IsGhostPortrait || !mediaSettings.MotionsEnabled
                || closing || hiddenForGame || changingOutfit || settings.Sleeping || thinking || voice.IsBusy
                || (pressed && dragged) || (now < poseUntil && temporaryPose != "idle")) return false;
            return ghostGestures.Trigger(kind, now);
        }

        private bool CanPerformUniform2Nod(double now, bool audioPlaying)
        {
            return liveModel != null && Appearance.Outfit == Appearance.Uniform2Portrait && mediaSettings.MotionsEnabled
                && !closing && !hiddenForGame && !changingOutfit && !pressed && !settings.Sleeping && !audioPlaying
                && !focusEnd.HasValue && !autonomous.Walking && (activeMenu == null || !activeMenu.IsOpen)
                && (now >= poseUntil || temporaryPose == "idle");
        }

        private bool TryStartUniform2Nod(double now)
        {
            return !voice.IsBusy && CanPerformUniform2Nod(now,false) && uniform2Director.TryNod(now, idleGestures.Pose);
        }
        private void Say(string text, double seconds)
        {
            speech.Text = text;
            bubble.Visibility = Visibility.Visible;
            bubbleUntil = elapsed.Elapsed.TotalSeconds + seconds;
        }
        private void Animate(object sender, EventArgs e)
        {
            AnimateAt(elapsed.Elapsed.TotalSeconds);
        }

        private void AnimateAt(double t, Vector? pointerOverride = null, bool allowAutonomyInSmoke = false,
            PortraitPlayback? playbackOverride = null)
        {
            if (closing || hiddenForGame) return;
            PortraitPlayback playback = playbackOverride ?? voice.Playback;
            bool portrait = HasConversationPortrait;
            bool conversationAllowed = portrait && mediaSettings.MotionsEnabled && !settings.Sleeping && !changingOutfit && !(pressed && dragged);
            bool requestPending = chatWindow != null && chatWindow.HasPendingModelTurn;
            bool endedUnvoicedTurn = waitingForReplyMotion && !requestPending && !playback.Busy && t >= briefReplyUntil;
            if (!conversationAllowed || playback.Audible || endedUnvoicedTurn) waitingForReplyMotion = false;
            portraitConversation = !conversationAllowed ? PortraitConversationState.None
                : playback.Audible ? PortraitConversationState.Speaking
                : waitingForReplyMotion ? PortraitConversationState.Thinking : PortraitConversationState.None;
            if (portrait && !conversationAllowed) liveModel.ResetConversationMotion();
            if (actionVoiceOperation >= 0 && t < actionVoiceDeadline && voice.IsBusy && voice.OperationId == actionVoiceOperation && !settings.Sleeping)
                poseUntil = Math.Max(poseUntil, t + .25);
            else actionVoiceOperation = -1;
            bool chatAllowsIdle = chatWindow == null || (Appearance.Outfit == Appearance.Uniform2Portrait
                && (!chatWindow.IsVisible || chatWindow.WindowState == WindowState.Minimized));
            bool idleAllowed = (!Program.Smoke || allowAutonomyInSmoke) && mediaSettings.MotionsEnabled && mediaSettings.AutonomousEnabled
                && !pressed && !settings.Sleeping && !thinking && !voice.IsBusy && !focusEnd.HasValue
                && chatAllowsIdle && callWindow == null && mediaWindow == null
                && voiceLevel.Visibility != Visibility.Visible && (activeMenu == null || !activeMenu.IsOpen)
                && t >= poseUntil && bubble.Visibility != Visibility.Visible && !changingOutfit;
            if (Appearance.Outfit == Appearance.OtterPortrait && idleAllowed && t >= nextOtterIdle)
            {
                bool sleepy = ++otterIdleCount % 3 == 0 || (DateTime.Now.Hour < 6 && otterIdleCount % 2 == 0);
                if (sleepy) PlayMotion("otterDoze", 4);
                nextOtterIdle = t + 25;
            }
            Rect workArea = SystemParameters.WorkArea;
            Left = autonomous.Update(t, Left, Width, workArea.Left, workArea.Right, false);
            string pose = "idle";
            if (mediaSettings.MotionsEnabled)
            {
                if (pressed && dragged) pose = "idle";
                else if (settings.Sleeping) pose = "sleep";
                else if (portraitConversation == PortraitConversationState.Speaking) pose = "speak";
                else if (portraitConversation == PortraitConversationState.Thinking || (!portrait && thinking)) pose = "think";
                else if (t < poseUntil) pose = temporaryPose;
                else if (autonomous.Walking) pose = "walk";
                else if (!portrait && voice.IsSpeaking) pose = "speak";
            }
            string renderedPose=pose=="rest"?"sleep":pose;
            bool liveStanding = liveModel != null;
            if (liveStanding)
            {
                if (mediaSettings.MotionsEnabled)
                {
                    bool ordinaryIdle = idleAllowed && renderedPose == "idle" && !playback.Busy;
                    if (Appearance.IsGhostPortrait)
                    {
                        bool available = renderedPose == "idle" && !changingOutfit && !pressed && !voice.IsBusy
                            && !settings.Sleeping && !thinking && (activeMenu == null || !activeMenu.IsOpen);
                        ghostGestures.Update(t, available, idleAllowed);
                        ordinaryIdle &= !ghostGestures.Pose.Face.ControlsGaze;
                    }
                    else if (Appearance.Outfit == Appearance.Uniform2Portrait)
                    {
                        if (settings.Sleeping || changingOutfit) uniform2Director.Reset();
                        else uniform2Director.Update(t, ordinaryIdle, !endedUnvoicedTurn && CanPerformUniform2Nod(t,playback.Audible), !idleGestures.Pose.ControlsGaze);
                        ordinaryIdle &= !uniform2Director.Pose.Face.ControlsGaze;
                        if (uniform2Director.Current == Uniform2Gesture.Nod && pose == "think" && portraitConversation == PortraitConversationState.None)
                            pose = renderedPose = "idle";
                    }
                    idleGestures.Update(t, ordinaryIdle);
                }
                else { idleGestures.Reset(); ghostGestures.Reset(); uniform2Director.Reset(); }
                RenderLivePose(renderedPose, t, pointerOverride, playback.Mouth);
            }
            else
            {
                idleGestures.Reset();
                SetFrame(renderedPose);
            }
            // The front model's soles are 98% down its canvas. A vertical breath about
            // that point keeps both shoes planted throughout all standing gestures.
            Point anchor = new Point(.5, liveStanding ? (Live2DPetModel.OverheadPadding+.98*Live2DPetModel.PortraitHeight)/liveModel.Frame.PixelHeight : .88);
            if (character.RenderTransformOrigin != anchor)
                character.RenderTransformOrigin = anchor;
            stateBadge.Visibility = Visibility.Collapsed;
            character.Opacity = 1;
            characterLayer.Opacity = 1;
            breathe.ScaleX = 1; breathe.ScaleY = 1; floatAt.Y = 0; tilt.Angle = 0;
            if (!mediaSettings.MotionsEnabled || (pressed && dragged)) return;
            if (liveStanding) breathe.ScaleY = 1 + Math.Sin(t * 1.8) * .003;
            else
            {
                breathe.ScaleX = 1 + Math.Sin(t * 2) * .006;
                breathe.ScaleY = 1 + Math.Sin(t * 2) * .009;
                floatAt.Y = Math.Sin(t * 1.8) * 2; tilt.Angle = Math.Sin(t * 1.3) * .5;
            }
            if(pose=="rest") { tilt.Angle=-2;floatAt.Y=2;stateBadge.Text="休息中";stateBadge.FontSize=12;stateBadge.Visibility=Visibility.Visible; }
            if (pose == "sleep")
            {
                breathe.ScaleX = 1 + Math.Sin(t * 1.5) * .006; breathe.ScaleY = .985 + Math.Sin(t * 1.5) * .008;
                floatAt.Y = 2; tilt.Angle = -2; characterLayer.Opacity = .9; stateBadge.Text = "Z z z"; stateBadge.FontSize = 17;
            }
            if (pose == "wave") { breathe.ScaleX = breathe.ScaleY = 1; floatAt.Y = tilt.Angle = 0; }
            if (pose == "walk" && !liveStanding) { breathe.ScaleX = autonomous.Direction; breathe.ScaleY = 1; floatAt.Y = 0; tilt.Angle = 0; }
            if (pose == "happy") { floatAt.Y = -Math.Abs(Math.Sin(t * 4)) * 4; tilt.Angle = 0; stateBadge.Text = "✦"; stateBadge.FontSize = 23; }
            if (pose == "think") { if (portraitConversation == PortraitConversationState.None) tilt.Angle = Math.Sin(t * 1.8) * 1.1; stateBadge.Text = "● ● ●"; stateBadge.FontSize = 9; }
            if (pose == "eat") { breathe.ScaleX = 1 + Math.Sin(t * 8) * .008; breathe.ScaleY = 1 + Math.Sin(t * 8) * .005; tilt.Angle = Math.Sin(t * 4) * .7; }
            if (pose == "speak") { if (!liveStanding) breathe.ScaleY = 1 + Math.Abs(Math.Sin(t * 10)) * .012; stateBadge.Text = "♪"; stateBadge.FontSize = 22; }
            if (pose == "sleep" || pose == "think" || pose == "happy" || pose == "speak")
            { stateBadge.Visibility = Visibility.Visible; stateBadge.Opacity = .6 + Math.Sin(t * 2) * .25; badgeFloat.Y = -Math.Sin(t * 2) * 3; }
        }

        private IdleGesturePose PortraitExpression(string pose, double t)
        {
            IdleGesturePose target = pose == "idle" ? idleGestures.Pose : new IdleGesturePose { ControlsGaze = true };
            switch (pose)
            {
                case "sleep": case "rest": case "sit":
                    target.EyeClosure = 1; target.HeadPitch = .24; target.ListenTilt = -.18; break;
                case "think":
                    target.EyeLook = .28; target.ListenTilt = -.28; target.Daydream = .20; break;
                case "happy": case "eat":
                    target.SnackSmile = .68; target.ListenTilt = .14; break;
                case "wave":
                    target.HeadPitch = .18 + .14*Math.Sin(t*2.6); target.SnackSmile = .42; break;
                case "walk":
                    target.WeightShift = .13*Math.Sin(t*2.4); break;
            }
            if (standingPose == null) { standingPose=pose; standingExpression=target; standingTransitionStart=t-1; }
            if (pose != standingPose) { standingPose=pose; standingTransitionStart=t; standingTransitionFrom=standingExpression; }
            double blend = Math.Max(0, Math.Min(1,(t-standingTransitionStart)/.4));
            standingExpression=IdleGesturePose.Blend(standingTransitionFrom,target,blend*blend*(3-2*blend));
            return standingExpression;
        }

        private void RenderLivePose(string pose, double t, Vector? pointerOverride = null, double mouth = 0)
        {
            bool entering = character.Source != liveModel.Frame;
            if (entering) SetStandingMargin(true);
            if (entering || activePose != pose || t < lastLiveFrame || t - lastLiveFrame >= 1.0 / 60 - .001)
            {
                IdleGesturePose gesture = PortraitExpression(pose,t);
                if (pose != "idle") gesture.ControlsGaze = true;
                GhostGesturePose action = Appearance.IsGhostPortrait ? ghostGestures.Pose : new GhostGesturePose();
                var uniform2 = Appearance.Outfit == Appearance.Uniform2Portrait ? uniform2Director.Pose : new Uniform2GesturePose();
                if (portraitConversation != PortraitConversationState.None && uniform2Director.Current != Uniform2Gesture.Nod)
                    uniform2 = new Uniform2GesturePose();
                if (entering || !mediaSettings.MotionsEnabled) liveFace.Reset();
                Vector target = new Vector();
                if (mediaSettings.MotionsEnabled && !gesture.ControlsGaze && !action.Face.ControlsGaze && !uniform2.Face.ControlsGaze && !pressed)
                {
                    System.Drawing.Point screen;
                    if (pointerOverride.HasValue) target = pointerOverride.Value;
                    else if (PresentationSource.FromVisual(character) != null && GetCursorPos(out screen))
                    {
                        Point local = character.PointFromScreen(new Point(screen.X, screen.Y));
                        target = LiveFaceTracking.Target(local, character.ActualWidth, character.ActualHeight,
                                                         liveModel.Frame.PixelWidth, liveModel.Frame.PixelHeight, Live2DPetModel.OverheadPadding, liveModel.EyeHeight, liveModel.EyeCenterX);
                    }
                }
                liveModel.OtterDozeTime = pose == "otterDoze" && Appearance.Outfit == Appearance.OtterPortrait
                    && mediaSettings.MotionsEnabled && !changingOutfit && !pressed && !settings.Sleeping
                    ? Math.Max(0, t - poseStarted) : -1;
                liveModel.RenderTracked(liveFace,t,target,gesture,mouth,mediaSettings.MotionsEnabled,action,uniform2,portraitConversation);
                lastLiveFrame = t;
            }
            character.Source = bitmap = liveModel.Frame;
            pixelWidth = bitmap.PixelWidth;
            pixelHeight = bitmap.PixelHeight;
            alphaPixels = liveModel.Pixels;
            activePose = pose;
        }
        private void PlayMotion(string pose, double seconds) { autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); actionVoiceOperation = -1; temporaryPose = pose; poseStarted = elapsed.Elapsed.TotalSeconds; poseUntil = poseStarted + seconds; }
        private static bool ContainsJapaneseKana(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (char c in text) if (c >= '\u3040' && c <= '\u30ff') return true;
            return false;
        }
        private void ReadLocal(string japaneseText)
        {
            if (!Program.Smoke && !closing && !hiddenForGame && mediaSettings.ReadLocal && !settings.Sleeping && ContainsJapaneseKana(japaneseText)) { try { voice.Speak(japaneseText); } catch { Program.Log("Local Japanese speech could not start."); } }
        }
        private static string ActionJapanese(string pose)
        {
            if (pose == "wave") return "こんにちは！";
            if (pose == "happy") return "えへへ、ふふっ！";
            if (pose == "eat") return "もぐもぐ、おいしい！";
            return null;
        }
        private void SpeakAction(string pose)
        {
            string line = ActionJapanese(pose);
            if (Program.Smoke || closing || hiddenForGame || !mediaSettings.ReadLocal || settings.Sleeping || line == null || voice.IsBusy || !voice.Available) return;
            try
            {
                voice.Speak(line);
                actionVoiceOperation = voice.OperationId;
                actionVoiceDeadline = elapsed.Elapsed.TotalSeconds + 30;
            }
            catch { Program.Log("Action speech could not start."); }
        }
        private void OnAssistantReply(string japaneseSpeechText)
        {
            if (!Program.Smoke && !closing && !hiddenForGame && mediaSettings.AutoReadReplies && !settings.Sleeping && !string.IsNullOrWhiteSpace(japaneseSpeechText)) { try { voice.Speak(japaneseSpeechText); } catch { Program.Log("Reply Japanese speech could not start."); } }
        }
        internal void ReactToReply(string text)
        {
            if(settings.Sleeping || !mediaSettings.MotionsEnabled || HasConversationPortrait)return;
            string pose=System.Text.RegularExpressions.Regex.IsMatch(text,"难过|伤心|辛苦|担心")?"think":"speak";
            PlayMotion(pose,4);
        }
        private void RunMotion(string pose)
        {
            if(pose=="sit")pose="rest";
            if (pose == "sleep") { settings.Sleeping = true; voice.Stop(); }
            else { settings.Sleeping = false; PlayMotion(pose, pose=="rest"?18:pose == "eat" ? 5 : 3); SpeakAction(pose); }
            if (pose == "idle") { poseUntil = 0; voice.Stop(); }
            settings.Save();
        }
        private void ApplyMediaSettings() { voice.ConfigureBackend(mediaSettings.Engine, mediaSettings.Endpoint, mediaSettings.ReferenceAudio, mediaSettings.ReferenceText); voice.Configure(mediaSettings.VoiceId, mediaSettings.Rate, mediaSettings.Volume); if (!mediaSettings.AutoReadReplies && !mediaSettings.ReadLocal) voice.Stop(); }
        private void OpenMedia()
        {
            if (mediaWindow != null && !mediaWindow.IsVisible) mediaWindow.Show();
            if (mediaWindow != null) { if (mediaWindow.WindowState == WindowState.Minimized) mediaWindow.WindowState = WindowState.Normal; mediaWindow.Activate(); return; }
            mediaWindow = new PetMediaWindow(voice, mediaSettings, ApplyMediaSettings, RunMotion, character);
            mediaWindow.Closed += delegate { mediaWindow = null; };
            mediaWindow.Show(); mediaWindow.Activate();
        }
        private void OnTick(object sender, EventArgs e)
        {
            if (bubble.Visibility == Visibility.Visible && elapsed.Elapsed.TotalSeconds > bubbleUntil) bubble.Visibility = Visibility.Collapsed;
            if (focusEnd.HasValue && focusEnd.Value <= DateTime.Now)
            {
                focusEnd = null; happyUntil = elapsed.Elapsed.TotalSeconds + 2;
                PlayMotion("idle", 3);
                string done = "这 " + focusTotal + " 分钟辛苦啦！起来伸个懒腰，喝点水吧。";
                if (hiddenForGame) deferredFocusNotice = done;
                else { Say(done, 12); SpeakFocusReminder(focusTotal.ToString(CultureInfo.InvariantCulture) + "分間、お疲れさま！少し休んで、お水を飲もうね。"); if (!Program.Smoke) System.Media.SystemSounds.Asterisk.Play(); }
            }
            if (focusLabel != null) focusLabel.Text = FocusText();
        }
        private string FocusText()
        {
            if (!focusEnd.HasValue) return "准备好时，我们一起专注一会儿。";
            TimeSpan left = focusEnd.Value - DateTime.Now;
            return "专注中  " + Math.Max(0, (int)left.TotalMinutes).ToString("00") + ":" + Math.Max(0, left.Seconds).ToString("00");
        }
        private void StartFocus(int minutes)
        {
            if (minutes < 1 || minutes > 1440) return;
            focusTotal = minutes; focusEnd = DateTime.Now.AddMinutes(minutes);
            settings.Sleeping = false; settings.Save();
            Say("接下来 " + minutes + " 分钟，我安静陪你。时间到了叫你。", 5);
        }
        private static bool TryFocusMinutes(string text, out int minutes)
        {
            return int.TryParse((text ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) && minutes >= 1 && minutes <= 1440;
        }
        private void SpeakFocusReminder(string message)
        {
            if (Program.Smoke) { if (focusVoiceSmokeHook != null) focusVoiceSmokeHook(message); return; }
            if (closing) return;
            focusVoicePending = false;
            try
            {
                if (!voice.Available) { Say("时间到啦！语音提醒不可用，请检查日语配音设置。", 12); return; }
                voice.Speak(message);
                focusVoiceOperation = voice.OperationId; focusVoicePending = true;
                OnFocusVoiceStatus(voice.Status);
            }
            catch { focusVoicePending = false; Say("时间到啦！语音提醒未能播放，请检查日语配音设置。", 12); }
        }
        private void OnFocusVoiceSpeaking(bool active)
        {
            if (active && focusVoicePending && focusVoiceOperation == voice.OperationId) focusVoicePending = false;
        }
        private void OnFocusVoiceStatus(string status)
        {
            if (!focusVoicePending) return;
            if (focusVoiceOperation != voice.OperationId) { focusVoicePending = false; return; }
            if (string.IsNullOrEmpty(status)) return;
            string[] failures = { "无法", "未能", "失败", "中断", "超过", "过大", "错误", "无效", "不完整", "没有返回", "没有可", "请先启动" };
            foreach (string failure in failures) if (status.Contains(failure))
            { focusVoicePending = false; Say("时间到啦！语音提醒未能播放，请检查日语配音服务。", 12); return; }
        }
        private void OpenFocusInput()
        {
            foreach (Window window in OwnedWindows)
                if (window.Title == "设置专注时间") { window.Show(); window.Activate(); return; }
            Window dialog = new Window { Title = "设置专注时间", Width = 330, Height = 218, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false, Topmost = true, FontFamily = FontFamily, Background = new SolidColorBrush(Color.FromRgb(245, 249, 252)) };
            StackPanel body = new StackPanel { Margin = new Thickness(22, 18, 22, 16) }; dialog.Content = body;
            TextBlock description = Label("输入专注时长（1–1440 分钟）", 14, Ink); body.Children.Add(description);
            TextBox input = new TextBox { Text = (focusTotal > 0 ? focusTotal : 25).ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 10, 0, 4), Padding = new Thickness(8), FontSize = 14, MaxLength = 4 }; body.Children.Add(input);
            TextBlock error = Label("时间到了会用日语语音提醒。", 11, Color.FromRgb(112, 132, 148)); error.Margin = new Thickness(0, 2, 0, 9); body.Children.Add(error);
            Button start = Button("开始专注", delegate { int minutes; if (!TryFocusMinutes(input.Text, out minutes)) { error.Text = "请输入 1 到 1440 之间的整数。"; error.Foreground = Brushes.Firebrick; return; } StartFocus(minutes); dialog.Close(); }); start.IsDefault = true; body.Children.Add(start);
            PanelTheme.Apply(dialog,"一起专注一会儿","FOCUS");
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); }; dialog.Show();
        }
        private void SleepToggle()
        {
            settings.Sleeping = !settings.Sleeping; settings.Save();
            if (settings.Sleeping) voice.Stop();
            Say(settings.Sleeping ? "我先休息一下，有事可以双击找我聊天。" : "醒啦。我们接着做点什么？", 4);
        }
        private void OpenMenu()
        {
            OpenQuickMenu();
        }
        private void OpenQuickMenu()
        {
            if(activeMenu!=null)activeMenu.IsOpen=false;
            autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);
            Action[] commands={OpenChat,delegate{ShowCompanionTab("music");},Appearance.Show,OpenFocusInput,delegate{routine.ShowSettings();},delegate{RunMotion("think");},delegate{RunMotion("idle");},delegate{RunMotion("idle");},delegate{RunMotion("sleep");},delegate{RunMotion("idle");},SleepToggle,OpenPanel,OpenMedia,delegate{if(desktopShell!=null)desktopShell.ShowHelp();},delegate{mediaSettings.AutonomousEnabled=!mediaSettings.AutonomousEnabled;mediaSettings.Save();autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);},delegate{settings.OnTop=!settings.OnTop;Topmost=settings.OnTop;settings.Save();},delegate{focusEnd=null;Say("暂停一下也没关系。",4);},delegate{if(desktopShell!=null)desktopShell.HideAll();},delegate{Close();}};
            var expanded=new List<Action>(commands);expanded.Add(delegate{RunMotion("sit");});expanded.Add(OpenVoiceInput);
            activeMenu=PetQuickMenu.Create(expanded.ToArray(),settings.Sleeping,mediaSettings.AutonomousEnabled,settings.OnTop,focusEnd.HasValue);
            activeMenu.PlacementTarget=this;activeMenu.Placement=PlacementMode.MousePoint;activeMenu.IsOpen=true;
        }

        private void OpenChat() { ShowCompanionTab("chat"); }

        internal void ShowCompanionTab(string tab)
        {
            if (closing || hiddenForGame) return;
            if (chatWindow == null)
            {
                chatWindow = new GptChatWindow(this, chatHistory, gptSettings, Say, delegate(bool active) { thinking = active; }, OnAssistantReply, voice.Stop, delegate { return mediaSettings.AutoReadReplies && !settings.Sleeping; }, localMusic.Handle, localMusic.CancelPending, localMusic, null,
                    delegate { if (!Program.Smoke && !closing && !hiddenForGame) voice.BeginStream(); },
                    delegate(string sentence) { if (!Program.Smoke && !closing && !hiddenForGame) voice.AppendStream(sentence); },
                    delegate { if (!Program.Smoke && !closing && !hiddenForGame) voice.CompleteStream(); });
                PlaceCompanionPanel(chatWindow);
                BindPortraitChat(chatWindow);
                chatWindow.Closed += delegate { chatWindow = null; thinking = false; focusLabel = null; };
            }
            chatWindow.SelectCompanionTab(tab);
            if (!chatWindow.IsVisible) chatWindow.Show();
            if (chatWindow.WindowState == WindowState.Minimized) chatWindow.WindowState = WindowState.Normal;
            chatWindow.Activate();
            if (tab == "chat") chatWindow.FocusInput();
        }

        private void BindPortraitChat(GptChatWindow chat)
        {
            chat.UserMessageSubmitted += delegate {
                if (chatWindow != chat) return;
                waitingForReplyMotion = chat.HasPendingModelTurn && HasConversationPortrait && mediaSettings.MotionsEnabled;
                submittedMotionAt = elapsed.Elapsed.TotalSeconds;
                briefReplyUntil = -1;
                TryStartUniform2Nod(submittedMotionAt);
            };
            chat.ReplyMotionFinished += delegate(bool success) {
                if (chatWindow != chat) return;
                if (success && waitingForReplyMotion)
                    briefReplyUntil = submittedMotionAt + .38;
                else if (!success)
                {
                    if (waitingForReplyMotion) uniform2Director.EndChatNod(elapsed.Elapsed.TotalSeconds);
                    waitingForReplyMotion = false;
                    briefReplyUntil = -1;
                }
            };
        }

        private void OpenPanel() { ShowCompanionTab("settings"); }

        private void WritePanelPreview()
        {
            try
            {
                Window selected = Program.RailPreview ? (Window)this : Program.VoicePreview ? (Window)mediaWindow : chatWindow;
                if (selected == null || !selected.IsVisible) throw new InvalidOperationException("Panel preview unavailable.");
                if (Program.NarrowPreview) { selected.MinWidth = 0; selected.Width = Math.Min(450, SystemParameters.WorkArea.Width - 20); }
                selected.UpdateLayout();
                int width = (int)Math.Ceiling(selected.ActualWidth), height = (int)Math.Ceiling(selected.ActualHeight);
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Panel preview is empty.");
                RenderTargetBitmap render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                render.Render(selected);
                if (Program.PreviewWithPet && selected != this)
                {
                    UpdateLayout();
                    var pet = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    pet.Render(this);
                    Rect frame = new Rect(selected.Left, selected.Top, width, height);
                    frame.Union(new Rect(Left, Top, pet.PixelWidth, pet.PixelHeight));
                    var pair = new DrawingVisual();
                    using (var painter = pair.RenderOpen())
                    {
                        painter.DrawImage(render, new Rect(selected.Left - frame.Left, selected.Top - frame.Top, width, height));
                        painter.DrawImage(pet, new Rect(Left - frame.Left, Top - frame.Top, pet.PixelWidth, pet.PixelHeight));
                    }
                    render = new RenderTargetBitmap((int)Math.Ceiling(frame.Width), (int)Math.Ceiling(frame.Height), 96, 96, PixelFormats.Pbgra32);
                    render.Render(pair);
                }
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (FileStream output = File.Create(Program.SmokePath)) encoder.Save(output);
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Program.Log("Panel preview failed: " + error.Message); Application.Current.Shutdown(1); }
        }


        private static TextBlock Label(string text, double size, Color color)
        { TextBlock b = new TextBlock(); b.Text = text; b.FontSize = size; b.Foreground = new SolidColorBrush(color); b.TextWrapping = TextWrapping.Wrap; return b; }
        private static Button Button(string text, Action action)
        {
            Button b = new Button(); b.Content = text; b.FontSize = 12; b.Padding = new Thickness(9, 8, 9, 8); b.Background = Brushes.White;
            b.Foreground = new SolidColorBrush(Blue); b.BorderBrush = new SolidColorBrush(Color.FromRgb(214, 226, 235)); b.BorderThickness = new Thickness(1); b.Cursor = Cursors.Hand;
            b.Click += delegate { action(); }; return b;
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point point);
    }
}
