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
[assembly: AssemblyVersion("1.37.0.0")]
[assembly: AssemblyFileVersion("1.37.0.0")]

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
        internal static bool ChatPreview, VoicePreview, NarrowPreview;
        internal static string SmokePath;
        [STAThread]
        public static int Main(string[] args)
        {
            Smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            UiCheck = Array.IndexOf(args, "--ui-check") >= 0;
            OpenChatOnStart = Array.IndexOf(args, "--chat") >= 0;
            MediaCheck = Array.IndexOf(args, "--media-check") >= 0;
            ChatPreview = Array.IndexOf(args, "--chat-preview") >= 0;
            VoicePreview = Array.IndexOf(args, "--voice-preview") >= 0;
            NarrowPreview = Array.IndexOf(args, "--narrow-preview") >= 0;
            PanelCheck = Array.IndexOf(args, "--panel-check") >= 0 || ChatPreview || VoicePreview;
            if (MediaCheck || PanelCheck) diagnosticDataDir = Path.Combine(Path.GetTempPath(), "AnnaDesktopPet-Diagnostic", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            int outputAt = Array.IndexOf(args, "--report");
            if (outputAt >= 0 && outputAt + 1 < args.Length) SmokePath = args[outputAt + 1];
            bool first;
            using (Mutex mutex = new Mutex(true, PanelCheck ? "Local\\AnnaDesktopPet.v1.panel-check" : MediaCheck ? "Local\\AnnaDesktopPet.v1.media-check" : "Local\\AnnaDesktopPet.v1", out first))
            {
                if (!first && !Smoke) return 0;
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

    internal sealed class PetWindow : Window
    {
        private readonly Settings settings;
        private readonly Image character;
        private readonly Image previousCharacter;
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
        private MotionController motions;
        private readonly AutonomousMotion autonomous = new AutonomousMotion();
        private ContextMenu activeMenu;
        private double nextAutonomousGesture = 45;
        private string activePose;
        private MotionFrame activeMotionFrame, previousMotionFrame;
        private double transitionStart, transitionDuration;
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
        private double bubbleUntil, happyUntil, lastHover;
        private DateTime? focusEnd;
        private int focusTotal;
        private long focusVoiceOperation = -1;
        private bool focusVoicePending;
        private Action<string> focusVoiceSmokeHook;
        private Window panel;
        private GptChatWindow chatWindow;
        private readonly List<ChatMessage> chatHistory = new List<ChatMessage>();
        private readonly GptSettings gptSettings;
        private bool thinking;
        private TextBlock focusLabel;
        private bool closing;
        private DesktopShell desktopShell;
        private VoiceChat voiceChat;
        private ProgressBar voiceLevel;
        internal void SetVoiceLevel(bool visible,double level){if(voiceLevel!=null){voiceLevel.Visibility=visible&&!hiddenForGame?Visibility.Visible:Visibility.Collapsed;voiceLevel.Value=level*100;}}
        private void OpenVoiceInput(){if(voiceChat!=null)voiceChat.ShowSettings();}
        internal void ReviewVoiceTranscript(string text){if(hiddenForGame || closing)return;OpenChat();chatWindow.ReviewVoice(text);}
        internal void SpeakVoiceReply(string text){if(!hiddenForGame && !closing && !String.IsNullOrWhiteSpace(text))voice.Speak(text);}
        private bool hiddenForGame, loadedOnce;
        private string deferredFocusNotice;
        private void OnShellVisibilityChanged(bool hidden)
        {
            hiddenForGame = hidden;
            if(hidden && voiceChat!=null)voiceChat.CancelCapture();
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
        private bool anchoredWaveTransform;
        private double waveScaleX=1,waveScaleY=1,waveFloat,waveTilt;
        private Border dressingRoom;
        private async void ChangeOutfit()
        {
            if(closing || changingOutfit)return;
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
                activeMotionFrame=null;previousMotionFrame=null;previousCharacter.Opacity=0;
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
            settings = Settings.Load();
            gptSettings = GptSettings.Load();
            mediaSettings = SpeechSettings.Load();
            LoginStartup.RefreshIfEnabled();
            Appearance.OutfitChanged+=ChangeOutfit;
            Closed+=delegate {Appearance.OutfitChanged-=ChangeOutfit;};
            VoiceAutoStart.Start(mediaSettings);
            voice = new SpeechService(Dispatcher);
            localMusic = new MusicRouter(delegate(string message) { if (!closing) Say(message, 8); });
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
            previousCharacter = new Image { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(22, 90, 22, 10), RenderTransformOrigin = new Point(.5, .88), RenderTransform = transforms, Opacity = 0, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(previousCharacter, BitmapScalingMode.HighQuality);
            characterLayer = new Grid();
            characterLayer.Children.Add(previousCharacter);
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
                if (desktopShell != null) desktopShell.Dispose();
                if (voiceChat != null) voiceChat.Dispose();
                if (panel != null) panel.Close();
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
            if(Appearance.Outfit=="校服2"){
                string layers=Appearance.Asset("rest-layers");
                if(Directory.Exists(layers))try{
                    bitmap=RestPoseLayers.Load(layers,new MotionFrame(bitmap)).Compose().Image;
                    Program.Log("School2 rest: composed from four original-art layers.");
                }catch(Exception ex){Program.Log("Rest layers fallback: "+ex.Message);}
            }
            if(Appearance.Outfit!="默认" && Appearance.Outfit!="校服1") {
                BitmapImage standard=new BitmapImage();
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AnnaDesktopPet.pet.png")){standard.BeginInit();standard.CacheOption=BitmapCacheOption.OnLoad;standard.StreamSource=stream;standard.EndInit();standard.Freeze();}
                MotionFrame canonical=new MotionFrame(standard);
                bitmap=MotionController.Normalize(new MotionFrame(bitmap),canonical,canonical.SubjectBounds()).Image;
            }
            motions = new MotionController(bitmap);
            SetFrame("idle");
        }

        private void SetFrame(string pose)
        {
            SetFrame(pose, motions.Get(pose));
        }
        private void SetFrame(string pose, MotionFrame frame)
        {
            if(pose=="wave" && motions.HasWaveTrial && previousMotionFrame!=null){previousMotionFrame=null;previousCharacter.Opacity=0;previousCharacter.Source=null;}
            if (activePose == pose && Object.ReferenceEquals(activeMotionFrame, frame)) return;
            bool poseChanged = activePose != pose;
            // Crossfade only the large arm motions. Blink and mouth frames are short and
            // blending those would soften their detail; a full pose change uses a longer fade.
            bool armFrameChanged = !poseChanged && (pose == "wave" || pose == "happy" || pose == "walk" || pose=="sit") && !(pose=="wave" && motions.HasWaveTrial);
            if (!Program.Smoke && activeMotionFrame != null && (poseChanged || armFrameChanged) && mediaSettings.MotionsEnabled && !(pressed && dragged) && !(pose=="wave" && motions.HasWaveTrial))
            {
                previousMotionFrame = activeMotionFrame; previousCharacter.Source = previousMotionFrame.Image;
                transitionStart = elapsed.Elapsed.TotalSeconds;
                transitionDuration = poseChanged ? .20 : (pose == "wave" ? .045 : (pose == "walk" ? .035 : .06));
            }
            activeMotionFrame = frame;
            bitmap = frame.Image; pixelWidth = frame.Width; pixelHeight = frame.Height; alphaPixels = frame.Pixels;
            character.Source = bitmap; activePose = pose;
        }

        private void ApplySize()
        {
            Width = settings.Size + 64;
            Height = settings.Size * 1.25 + 100;
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
                voiceChat=new VoiceChat(this,delegate(string text){OpenChat();chatWindow.SendVoice(text);},delegate(string message){Say(message,8);},delegate {if(hiddenForGame || closing)return false;if(chatWindow!=null && !chatWindow.CanAcceptVoice){Say("请先发送或清空输入框，并等待当前回复完成。",5);return false;}if(String.IsNullOrWhiteSpace(gptSettings.ApiKey)){Say("请先在 GPT 设置中填写 API Key。",5);return false;}voice.Stop();return true;});
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
            if (alphaPixels[index] >= 32) return true;
            if (previousMotionFrame == null || previousCharacter.Opacity <= 0.1) return false;
            int oldX = x * previousMotionFrame.Width / pixelWidth, oldY = y * previousMotionFrame.Height / pixelHeight;
            return previousMotionFrame.Pixels[(oldY * previousMotionFrame.Width + oldX) * 4 + 3] >= 32;
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
                if (Program.ChatPreview) OpenChat();
                else if (Program.VoicePreview) OpenMedia();
                else OpenPanel();
                if (!string.IsNullOrEmpty(Program.SmokePath)) Dispatcher.BeginInvoke(new Action(WritePanelPreview), DispatcherPriority.ApplicationIdle);
            }), DispatcherPriority.ApplicationIdle);
            if (Program.Smoke)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
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
                        double fit = Math.Min(character.ActualWidth / pixelWidth, character.ActualHeight / pixelHeight);
                        double offsetX = (character.ActualWidth - pixelWidth * fit) / 2;
                        double offsetY = (character.ActualHeight - pixelHeight * fit) / 2;
                        bool checkedSolid = false, checkedClear = false;
                        for (int y = 4; y < pixelHeight && (!checkedSolid || !checkedClear); y += 15)
                            for (int x = 4; x < pixelWidth && (!checkedSolid || !checkedClear); x += 15)
                            {
                                byte a = alphaPixels[(y * pixelWidth + x) * 4 + 3];
                                if ((!checkedSolid && a >= 128) || (!checkedClear && a == 0))
                                {
                                    Point screen = character.PointToScreen(new Point(offsetX + (x + .5) * fit, offsetY + (y + .5) * fit));
                                    bool hit = IsOpaqueAt(screen);
                                    if (a >= 128) { if (!hit) throw new Exception("Opaque alpha hit test failed"); checkedSolid = true; }
                                    else { if (hit) throw new Exception("Transparent alpha hit test failed"); checkedClear = true; }
                                }
                            }
                        if (!checkedSolid || !checkedClear) throw new Exception("Alpha hit test samples missing");
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
                        OpenPanel(); if (panel == null || !panel.IsVisible) throw new Exception("Panel failed"); panel.Close();
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
                        foreach (string pose in MotionController.PoseNames)
                        {
                            SetFrame(pose);
                            MotionFrame frame = motions.Get(pose); Int32Rect bounds = frame.SubjectBounds();
                            if (!object.ReferenceEquals(alphaPixels, frame.Pixels) || bounds.Width <= 0 || bounds.Height <= 0) throw new Exception("Pose alpha cache failed: " + pose);
                        }
                        if (motions.HasWaveAnimation)
                        {
                            MotionFrame first = motions.GetAt("wave", 0), second = motions.GetAt("wave", .3);
                            if (Object.ReferenceEquals(first, second)) throw new Exception("Wave animation frames did not advance");
                            double baseline = MotionController.LowerBodyCenterX(first);
                            int foot = first.SubjectBounds().Y + first.SubjectBounds().Height;
                            foreach (double frameTime in new double[] { .12, .23, .34 })
                            {
                                MotionFrame candidate = motions.GetAt("wave", frameTime);
                                Int32Rect b = candidate.SubjectBounds();
                                if (Math.Abs(MotionController.LowerBodyCenterX(candidate) - baseline) > 4 || Math.Abs(b.Y + b.Height - foot) > 2)
                                    throw new Exception("Wave frame lower body or feet are not aligned");
                            }
                            bool previousEnabled = mediaSettings.MotionsEnabled;
                            mediaSettings.MotionsEnabled = true; PlayMotion("wave", 3); Animate(null, EventArgs.Empty);
                            if (breathe.ScaleX != 1 || breathe.ScaleY != 1 || floatAt.Y != 0 || tilt.Angle != 0) throw new Exception("Wave body transforms are not fixed");
                            poseUntil = 0; mediaSettings.MotionsEnabled = previousEnabled;
                            SetFrame("wave", second);
                            if (!Object.ReferenceEquals(alphaPixels, second.Pixels) || pixelWidth != second.Width || pixelHeight != second.Height) throw new Exception("Animated frame hit test cache not updated");
                            fit = Math.Min(character.ActualWidth / pixelWidth, character.ActualHeight / pixelHeight);
                            offsetX = (character.ActualWidth - pixelWidth * fit) / 2;
                            offsetY = (character.ActualHeight - pixelHeight * fit) / 2;
                            bool animatedSolid = false, animatedClear = false;
                            for (int y = 4; y < pixelHeight && (!animatedSolid || !animatedClear); y += 15)
                                for (int x = 4; x < pixelWidth && (!animatedSolid || !animatedClear); x += 15)
                                {
                                    byte a = alphaPixels[(y * pixelWidth + x) * 4 + 3];
                                    if ((!animatedSolid && a >= 128) || (!animatedClear && a == 0))
                                    {
                                        Point screen = character.PointToScreen(new Point(offsetX + (x + .5) * fit, offsetY + (y + .5) * fit));
                                        bool hit = IsOpaqueAt(screen);
                                        if (a >= 128) { if (!hit) throw new Exception("Animated opaque hit test failed"); animatedSolid = true; }
                                        else { if (hit) throw new Exception("Animated transparent hit test failed"); animatedClear = true; }
                                    }
                                }
                            if (!animatedSolid || !animatedClear) throw new Exception("Animated hit test samples missing");
                            // Exercise the live transition path without sending audio or network requests.
                            bool oldSmoke = Program.Smoke, oldEnabled = mediaSettings.MotionsEnabled;
                            try
                            {
                                Program.Smoke = false; mediaSettings.MotionsEnabled = true;
                                SetFrame("wave", first); SetFrame("wave", second);
                                VerifyTransitionComposition(first, second);
                                if (!Object.ReferenceEquals(previousMotionFrame, first) || transitionDuration <= 0 || transitionDuration >= 1.0 / 9)
                                    throw new Exception("Arm frame transition was not prepared correctly");
                            }
                            finally { Program.Smoke = oldSmoke; mediaSettings.MotionsEnabled = oldEnabled; previousMotionFrame = null; previousCharacter.Opacity = 0; previousCharacter.Source = null; }
                        }
                        if (motions.HasWalkAnimation)
                        {
                            int count = motions.WalkFrameCount; double rate = motions.WalkFramesPerSecond;
                            if (count != 4 && count != 8) throw new Exception("Unsupported walking frame count");
                            MotionFrame firstStep = motions.GetAt("walk", .1 / rate), previousStep = null;
                            for (int i = 0; i < count; i++)
                            {
                                MotionFrame step = motions.GetAt("walk", (i + .1) / rate); Int32Rect bounds = step.SubjectBounds();
                                if (previousStep != null && Object.ReferenceEquals(previousStep, step)) throw new Exception("Walking frame did not advance");
                                if (bounds.Height < 10 || bounds.X <= 0 || (count == 8 && bounds.Y <= 0) || bounds.X + bounds.Width >= step.Width || bounds.Y + bounds.Height >= step.Height) throw new Exception("Walking frame blank or cropped: " + i + " " + bounds.ToString() + " canvas=" + step.Width + "x" + step.Height);
                                previousStep = step;
                            }
                            if (!Object.ReferenceEquals(firstStep, motions.GetAt("walk", (count + .1) / rate))) throw new Exception("Walking frame cycle invalid");
                        }
                        if (motions.HasBlinkAnimation && Object.ReferenceEquals(motions.GetAt("idle", .5), motions.GetAt("idle", .14))) throw new Exception("Blink did not advance");
                        if (motions.HasSpeechAnimation && Object.ReferenceEquals(motions.GetAt("speak", 0), motions.GetAt("speak", .35))) throw new Exception("Mouth animation did not advance");
                        if (motions.HasHappyAnimation && Object.ReferenceEquals(motions.GetAt("happy", 0), motions.GetAt("happy", .3))) throw new Exception("Happy animation did not advance");
                        if (motions.HasBlinkAnimation) VerifyAnchoredFrames("idle", new double[] { .5, .05, .14 });
                        if (motions.HasSpeechAnimation) VerifyAnchoredFrames("speak", new double[] { 0, .18, .35, .51 });
                        if (motions.HasHappyAnimation) VerifyAnchoredFrames("happy", new double[] { 0, .15, .3, .44 });
                        SetFrame("idle");
                        OpenMedia(); if (mediaWindow == null || !mediaWindow.IsVisible) throw new Exception("Media settings failed"); mediaWindow.VerifySmoke(); mediaWindow.Close();
                        if (Lines.Length != LocalJapaneseLines.Length) throw new Exception("Local Japanese line mapping length mismatch");
                        foreach (string action in new string[] { "wave", "happy", "eat" }) if (!ContainsJapaneseKana(ActionJapanese(action))) throw new Exception("Action voice must be Japanese");
                        if (ActionJapanese("idle") != null) throw new Exception("Idle should not trigger action speech");
                        foreach (string japaneseLine in LocalJapaneseLines) if (!ContainsJapaneseKana(japaneseLine)) throw new Exception("Local line must contain Japanese kana");
                        SpeechSettings freshMedia = new SpeechSettings();
                        if (freshMedia.VoiceLanguage != "ja" || freshMedia.Engine != "gpt-sovits" || freshMedia.Endpoint != "http://127.0.0.1:9880" || freshMedia.AutoReadReplies || freshMedia.ReadLocal) throw new Exception("Japanese media defaults invalid");
                        if (voice.IsSpeaking) throw new Exception("Smoke must never play audio");
                        string report = "PASS\nWPF window initialized\nPNG loaded: " + pixelWidth + " x " + pixelHeight + "\nAlpha range: " + alphaMin + ".." + alphaMax + "; clickable pixels >= 32: " + clickPixels + "\nAlpha hit testing: opaque hit + transparent pass-through verified\nSettings parsed without mutation\nFocus start and completion passed\nSleep animation initialized\nControl panel opened and closed\nGPT chat window initialized; missing-key send blocked; draft preserved; password settings initialized\nSix pose states initialized with current bitmap alpha cache; " + motions.Status + "\nAnimation resources: wave=" + motions.HasWaveAnimation + "; blink=" + motions.HasBlinkAnimation + "; speak=" + motions.HasSpeechAnimation + "\nJapanese local lines mapped to all Chinese interaction lines\nJapanese voice preference initialized; audio switches default off\nSpeech and motion settings initialized; no audio was played\nSmoke made no external network requests; GPT requests only on explicit user send\nNo autostart; no admin requirement\n";
                        report += "Walk animation resource: " + motions.HasWalkAnimation + "; frames=" + motions.WalkFrameCount + "; fps=" + motions.WalkFramesPerSecond + "\nAutonomous idle delay and interruption verified\n"; report += "Happy animation resource: " + motions.HasHappyAnimation + "\nWave feet alignment and fixed body transforms verified\nWPF transition alpha verified at five progress values for normal and sleep opacity\n";
                        report += "QQ candidate selection in no-key chat: search -> 第一首 -> first displayed track exactly once; local roles only; mock network and playback\n";
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

        private void OnPointerDown(object sender, MouseButtonEventArgs e)
        {
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
                else { lastClickTime = time; lastClickScreen = screen; clickTimer.Start(); }
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
            happyUntil = elapsed.Elapsed.TotalSeconds + 1.2;
            Say("你好呀！", 4.5);
        }
        private void Say(string text, double seconds)
        {
            speech.Text = text;
            bubble.Visibility = Visibility.Visible;
            bubbleUntil = elapsed.Elapsed.TotalSeconds + seconds;
        }
        private void Animate(object sender, EventArgs e)
        {
            if (closing || hiddenForGame) return;
            double t = elapsed.Elapsed.TotalSeconds;
            if (actionVoiceOperation >= 0 && t < actionVoiceDeadline && voice.IsBusy && voice.OperationId == actionVoiceOperation && !settings.Sleeping)
                poseUntil = Math.Max(poseUntil, t + .25);
            else actionVoiceOperation = -1;
            bool autoAllowed = !Program.Smoke && mediaSettings.MotionsEnabled && mediaSettings.AutonomousEnabled
                && !pressed && !IsMouseOver && !settings.Sleeping && !thinking && !voice.IsBusy && !focusEnd.HasValue
                && chatWindow == null && mediaWindow == null && panel == null && (activeMenu == null || !activeMenu.IsOpen)
                && t >= poseUntil && bubble.Visibility != Visibility.Visible && !LifeActivity.Busy && !changingOutfit;
            if (!autoAllowed) nextAutonomousGesture = Math.Max(nextAutonomousGesture, t + 25);
            else if (t >= nextAutonomousGesture && motions.HasPoses)
            {
                PlayMotion("think", 5);
                nextAutonomousGesture = t + 150 + random.Next(150);
                autoAllowed = false;
            }
            Rect workArea = SystemParameters.WorkArea;
            Left = autonomous.Update(t, Left, Width, workArea.Left, workArea.Right, false);
            string pose = "idle";
            if (mediaSettings.MotionsEnabled)
            {
                if (pressed && dragged) pose = "idle";
                else if (settings.Sleeping) pose = "sleep";
                else if (thinking) pose = "think";
                else if (t < poseUntil) pose = temporaryPose;
                else if (autonomous.Walking) pose = "walk";
                else if (voice.IsSpeaking) pose = "speak";
            }
            string renderedPose=pose=="rest"?(motions.HasLifeAnimation?"sit":"sleep"):pose;
            bool anchoredWaveNow=pose=="wave" && motions.HasWaveTrial;
            if(anchoredWaveNow && !anchoredWaveTransform){waveScaleX=breathe.ScaleX;waveScaleY=breathe.ScaleY;waveFloat=floatAt.Y;waveTilt=tilt.Angle;}
            anchoredWaveTransform=anchoredWaveNow;
            SetFrame(renderedPose, mediaSettings.MotionsEnabled && !(pressed && dragged) ? motions.GetAt(renderedPose, (pose == "wave" || pose == "happy" || renderedPose=="sit") ? t - poseStarted : pose == "walk" ? t - autonomous.StartedAt : t) : motions.Get(renderedPose));
            stateBadge.Visibility = Visibility.Collapsed;
            character.Opacity = 1;
            characterLayer.Opacity = 1;
            breathe.ScaleX = 1; breathe.ScaleY = 1; floatAt.Y = 0; tilt.Angle = 0;
            if (!mediaSettings.MotionsEnabled || (pressed && dragged)) { previousMotionFrame = null; previousCharacter.Opacity = 0; return; }
            breathe.ScaleX = 1 + Math.Sin(t * 2) * .006;
            breathe.ScaleY = 1 + Math.Sin(t * 2) * .009;
            floatAt.Y = Math.Sin(t * 1.8) * 2; tilt.Angle = Math.Sin(t * 1.3) * .5;
            if(pose=="rest") { tilt.Angle=-2;floatAt.Y=2;stateBadge.Text="休息中";stateBadge.FontSize=12;stateBadge.Visibility=Visibility.Visible; }
            if (pose == "sleep")
            {
                breathe.ScaleX = 1 + Math.Sin(t * 1.5) * .006; breathe.ScaleY = .985 + Math.Sin(t * 1.5) * .008;
                floatAt.Y = 2; tilt.Angle = -2; characterLayer.Opacity = .9; stateBadge.Text = "Z z z"; stateBadge.FontSize = 17;
            }
            // The hand is animated by the sprite frames. Keep the torso and feet fixed.
            if (pose == "wave") { breathe.ScaleX = anchoredWaveNow?waveScaleX:1; breathe.ScaleY = anchoredWaveNow?waveScaleY:1; floatAt.Y = anchoredWaveNow?waveFloat:0; tilt.Angle = anchoredWaveNow?waveTilt:0; }
            if (pose == "walk") { breathe.ScaleX = autonomous.Direction; breathe.ScaleY = 1; floatAt.Y = 0; tilt.Angle = 0; }
            if (pose == "happy") { floatAt.Y = -Math.Abs(Math.Sin(t * 4)) * 4; tilt.Angle = 0; stateBadge.Text = "✦"; stateBadge.FontSize = 23; }
            if (pose == "think") { tilt.Angle = Math.Sin(t * 1.8) * 1.1; stateBadge.Text = "● ● ●"; stateBadge.FontSize = 9; }
            if (pose == "eat") { breathe.ScaleX = 1 + Math.Sin(t * 8) * .008; breathe.ScaleY = 1 + Math.Sin(t * 8) * .005; tilt.Angle = Math.Sin(t * 4) * .7; }
            if (pose == "speak") { breathe.ScaleY = 1 + Math.Abs(Math.Sin(t * 10)) * .012; stateBadge.Text = "♪"; stateBadge.FontSize = 22; }
            if (pose == "sleep" || pose == "think" || pose == "happy" || pose == "speak")
            { stateBadge.Visibility = Visibility.Visible; stateBadge.Opacity = .6 + Math.Sin(t * 2) * .25; badgeFloat.Y = -Math.Sin(t * 2) * 3; }
            if (previousMotionFrame != null)
            {
                double blend = Math.Max(0, Math.Min(1, (t - transitionStart) / transitionDuration));
                blend = blend * blend * (3 - 2 * blend);
                double opacity = characterLayer.Opacity;
                ApplyTransition(previousCharacter, character, characterLayer, blend, opacity);
                if (blend >= 1) { previousCharacter.Opacity = 0; previousCharacter.Source = null; previousMotionFrame = null; }
            }
        }

        internal static void ApplyTransition(Image previous, Image current, Grid layer, double blend, double opacity)
        {
            // Keep the lower frame fully visible under the fading-in frame. Fading both
            // makes overlapping pixels partially transparent in a transparent window.
            layer.Opacity = opacity;
            previous.Opacity = 1;
            current.Opacity = blend;
        }
        private void PlayMotion(string pose, double seconds) { autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); actionVoiceOperation = -1; temporaryPose = pose; poseStarted = elapsed.Elapsed.TotalSeconds; poseUntil = poseStarted + seconds; }
        private static void VerifyTransitionComposition(MotionFrame first, MotionFrame second)
        {
            if (first.Width != second.Width || first.Height != second.Height) throw new Exception("Transition frame sizes differ");
            Grid layer = new Grid { Width = first.Width, Height = first.Height };
            Image previous = new Image { Source = first.Image, Stretch = Stretch.Fill };
            Image current = new Image { Source = second.Image, Stretch = Stretch.Fill };
            layer.Children.Add(previous); layer.Children.Add(current);
            Size size = new Size(first.Width, first.Height); layer.Measure(size); layer.Arrange(new Rect(size)); layer.UpdateLayout();
            foreach (double opacity in new double[] { 1.0, .9 })
                foreach (double blend in new double[] { .1, .25, .5, .75, .9 })
                {
                    ApplyTransition(previous, current, layer, blend, opacity);
                    RenderTargetBitmap render = new RenderTargetBitmap(first.Width, first.Height, 96, 96, PixelFormats.Pbgra32);
                    render.Render(layer);
                    byte[] pixels = new byte[first.Width * first.Height * 4]; render.CopyPixels(pixels, first.Width * 4, 0);
                    long total = 0, count = 0;
                    for (int i = 3; i < pixels.Length; i += 4)
                        if (first.Pixels[i] >= 250 && second.Pixels[i] >= 250) { total += pixels[i]; count++; }
                    if (count < 10000 || (double)total / count < 250 * opacity - 4)
                        throw new Exception("Character became transparent during a transition: opacity=" + opacity + ", blend=" + blend);
                }
        }
        private void VerifyAnchoredFrames(string pose, double[] times)
        {
            MotionFrame first = motions.GetAt(pose, times[0]);
            double baseline = MotionController.LowerBodyCenterX(first);
            Int32Rect firstBounds = first.SubjectBounds();
            int foot = firstBounds.Y + firstBounds.Height;
            foreach (double time in times)
            {
                MotionFrame current = motions.GetAt(pose, time);
                Int32Rect bounds = current.SubjectBounds();
                if (Math.Abs(MotionController.LowerBodyCenterX(current) - baseline) > 5 || Math.Abs(bounds.Y + bounds.Height - foot) > 2)
                    throw new Exception(pose + " animation lower body or feet are not aligned");
            }
        }
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
            if(settings.Sleeping || !mediaSettings.MotionsEnabled)return;
            string pose=System.Text.RegularExpressions.Regex.IsMatch(text,"难过|伤心|辛苦|担心")?"think":"speak";
            PlayMotion(pose,4);
        }
        private void RunMotion(string pose)
        {
            if(pose=="sit" && !motions.HasLifeAnimation){Say("坐下动画已为校服2和幽灵服准备好，先换装试试吧。",6);return;}
            if (pose == "sleep") { settings.Sleeping = true; voice.Stop(); }
            else { settings.Sleeping = false; PlayMotion(pose, pose=="sit"?18:pose == "eat" ? 5 : 3); SpeakAction(pose); }
            if (pose == "idle") { poseUntil = 0; voice.Stop(); }
            settings.Save();
        }
        private void ApplyMediaSettings() { voice.ConfigureBackend(mediaSettings.Engine, mediaSettings.Endpoint, mediaSettings.ReferenceAudio, mediaSettings.ReferenceText); voice.Configure(mediaSettings.VoiceId, mediaSettings.Rate, mediaSettings.Volume); if (!mediaSettings.AutoReadReplies && !mediaSettings.ReadLocal) voice.Stop(); }
        private void OpenMedia()
        {
            if (mediaWindow != null && !mediaWindow.IsVisible) mediaWindow.Show();
            if (mediaWindow != null) { if (mediaWindow.WindowState == WindowState.Minimized) mediaWindow.WindowState = WindowState.Normal; mediaWindow.Activate(); return; }
            mediaWindow = new PetMediaWindow(voice, mediaSettings, ApplyMediaSettings, RunMotion, motions);
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
            Action[] commands={OpenChat,delegate{new MusicPanel().Show();},Appearance.Show,OpenFocusInput,delegate{routine.ShowSettings();},delegate{RunMotion("think");},delegate{RunMotion("idle");},delegate{RunMotion("idle");},delegate{RunMotion("sleep");},delegate{RunMotion("idle");},SleepToggle,OpenPanel,OpenMedia,delegate{if(desktopShell!=null)desktopShell.ShowHelp();},delegate{mediaSettings.AutonomousEnabled=!mediaSettings.AutonomousEnabled;mediaSettings.Save();autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);},delegate{settings.OnTop=!settings.OnTop;Topmost=settings.OnTop;settings.Save();},delegate{focusEnd=null;Say("暂停一下也没关系。",4);},delegate{if(desktopShell!=null)desktopShell.HideAll();},delegate{Close();}};
            var expanded=new List<Action>(commands);expanded.Add(delegate{RunMotion("sit");});expanded.Add(OpenVoiceInput);
            activeMenu=PetQuickMenu.Create(expanded.ToArray(),settings.Sleeping,mediaSettings.AutonomousEnabled,settings.OnTop,focusEnd.HasValue);
            activeMenu.PlacementTarget=this;activeMenu.Placement=PlacementMode.MousePoint;activeMenu.IsOpen=true;
        }
        private void LegacyMenu()
        {
            ContextMenu menu = new ContextMenu();
            if (desktopShell != null)
            {
                menu.Items.Add(Item("隐藏桌宠与面板 · Ctrl+Alt+H", desktopShell.HideAll));
                menu.Items.Add(Item("托盘与快捷键说明", desktopShell.ShowHelp));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(Item("换装与场景", Appearance.Show));
            menu.Items.Add(Item("作息问候设置", delegate { routine.ShowSettings(); }));
            menu.Items.Add(Item("正在播放 · 音乐控制", delegate { new MusicPanel().Show(); }));
            autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); activeMenu = menu;
            menu.FontFamily = FontFamily; menu.FontSize = 13;
            menu.Items.Add(Item("和杏菜聊天 · GPT", OpenChat));
            menu.Items.Add(Item("打开小面板", OpenPanel));
            menu.Items.Add(Item("日语配音与动作", OpenMedia));
            MenuItem actionsMenu = new MenuItem { Header = "动作" };
            actionsMenu.Items.Add(Item("思考", delegate { RunMotion("think"); }));
            actionsMenu.Items.Add(Item("恢复待机", delegate { RunMotion("idle"); }));
            menu.Items.Add(actionsMenu);
            menu.Items.Add(Item(settings.Sleeping ? "叫醒杏菜" : "让杏菜小憩", SleepToggle));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("自定义专注时间…", OpenFocusInput));
            if (focusEnd.HasValue) menu.Items.Add(Item("结束专注", delegate { focusEnd = null; Say("暂停一下也没关系。", 4); }));
            menu.Items.Add(new Separator());
            MenuItem autoItem = Item("自主活动", delegate { mediaSettings.AutonomousEnabled = !mediaSettings.AutonomousEnabled; mediaSettings.Save(); autonomous.Interrupt(elapsed.Elapsed.TotalSeconds); }); autoItem.IsCheckable = true; autoItem.IsChecked = mediaSettings.AutonomousEnabled; menu.Items.Add(autoItem);
            MenuItem top = Item("保持置顶", delegate { settings.OnTop = !settings.OnTop; Topmost = settings.OnTop; settings.Save(); }); top.IsCheckable = true; top.IsChecked = settings.OnTop; menu.Items.Add(top);
            menu.Items.Add(Item("退出桌宠", delegate { Close(); }));
            menu.PlacementTarget = this; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
        }
        private static MenuItem Item(string label, Action action)
        { MenuItem item = new MenuItem(); item.Header = label; item.Click += delegate { action(); }; return item; }

        private void OpenChat()
        {
            if (chatWindow != null) { if (!chatWindow.IsVisible) chatWindow.Show(); if (chatWindow.WindowState == WindowState.Minimized) chatWindow.WindowState = WindowState.Normal; chatWindow.Activate(); chatWindow.FocusInput(); return; }
            chatWindow = new GptChatWindow(this, chatHistory, gptSettings, Say, delegate(bool active) { thinking = active; }, OnAssistantReply, voice.Stop, delegate { return mediaSettings.AutoReadReplies; }, localMusic.Handle, localMusic.CancelPending);
            chatWindow.Closed += delegate { chatWindow = null; thinking = false; };
            chatWindow.Show(); chatWindow.Activate();
        }

        private void OpenPanel()
        {
            if (panel != null) { if (!panel.IsVisible) panel.Show(); if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal; panel.Activate(); return; }
            panel = new Window();
            panel.Title = "杏菜 · 小面板"; panel.Width = 390; panel.Height = 730;
            panel.ResizeMode = ResizeMode.NoResize; panel.WindowStartupLocation = WindowStartupLocation.Manual;
            panel.ShowInTaskbar = false; panel.Topmost = true; panel.Owner = this;
            panel.FontFamily = FontFamily; panel.Background = PanelBrush(238, 245, 252);
            Rect area = SystemParameters.WorkArea;
            panel.Width = Math.Min(panel.Width, Math.Max(250, area.Width - 24)); panel.Height = Math.Min(panel.Height, area.Height - 24);
            panel.Left = Math.Max(area.Left, Math.Min(area.Right - panel.Width, Left - 20));
            panel.Top = Math.Max(area.Top, Math.Min(area.Bottom - panel.Height, Top - 80));
            StackPanel body = new StackPanel { Margin = new Thickness(16, 15, 16, 18) };
            panel.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            Border hero = new Border { Background = PanelBrush(30, 55, 90), CornerRadius = new CornerRadius(16), Height = 145, ClipToBounds = true, Margin = new Thickness(0, 0, 0, 13) };
            Grid heroGrid = new Grid(); heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) }); hero.Child = heroGrid;
            StackPanel heroText = new StackPanel { Margin = new Thickness(17, 18, 2, 12) }; heroGrid.Children.Add(heroText);
            TextBlock tag = GptUi.Text("ANNA YANAMI  /  杏菜", 10, PanelBrush(249, 210, 113)); tag.FontWeight = FontWeights.Bold; heroText.Children.Add(tag);
            TextBlock heading = GptUi.Text(settings.Nickname + "在这里", 23, Brushes.White); heading.FontWeight = FontWeights.Bold; heading.Margin = new Thickness(0, 8, 0, 5); heroText.Children.Add(heading);
            TextBlock sub = GptUi.Text("今天也一起加油吧！", 12, PanelBrush(209, 229, 247)); sub.Margin = new Thickness(0, 0, 0, 9); heroText.Children.Add(sub);
            Border ribbon = new Border { Background = PanelBrush(246, 200, 91), Height = 4, Width = 44, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left }; heroText.Children.Add(ribbon);
            Border portrait = new Border { Background = PanelBrush(198, 224, 244), ClipToBounds = true, CornerRadius = new CornerRadius(42, 0, 0, 0), Margin = new Thickness(0, 10, 0, 0) }; Grid.SetColumn(portrait, 1); heroGrid.Children.Add(portrait);
            BitmapSource portraitSource = motions.Get("idle").Image;
            int px = portraitSource.PixelWidth * 18 / 100, pw = portraitSource.PixelWidth * 64 / 100, ph = portraitSource.PixelHeight * 58 / 100;
            CroppedBitmap portraitCrop = new CroppedBitmap(portraitSource, new Int32Rect(px, 0, pw, ph)); portraitCrop.Freeze();
            Image portraitImage = new Image { Source = portraitCrop, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Stretch }; RenderOptions.SetBitmapScalingMode(portraitImage, BitmapScalingMode.HighQuality); portrait.Child = portraitImage;
            body.Children.Add(hero);

            Border quick = PanelCard(); StackPanel quickBody = new StackPanel(); quick.Child = quickBody;
            quickBody.Children.Add(PanelSection("陪伴时光", "CHAT & VOICE"));
            Button chat = PanelButton("和杏菜聊天  →", OpenChat, true); chat.Margin = new Thickness(0, 10, 0, 7); quickBody.Children.Add(chat);
            Button media = PanelButton("日语配音与动作", OpenMedia, false); quickBody.Children.Add(media); body.Children.Add(quick);
            quickBody.Children.Add(PanelButton("语音输入与麦克风",OpenVoiceInput,false));

            Border timerCard = PanelCard(); timerCard.Margin = new Thickness(0, 10, 0, 0); StackPanel timerBody = new StackPanel(); timerCard.Child = timerBody;
            timerBody.Children.Add(PanelSection("专注一下", "FOCUS TIME"));
            focusLabel = Label(FocusText(), 12, Ink); focusLabel.Margin = new Thickness(0, 9, 0, 9); timerBody.Children.Add(focusLabel);
            Grid timerButtons = new Grid(); timerButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); timerButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            Button custom = PanelButton("设置专注时间", OpenFocusInput, true); custom.Margin = new Thickness(0, 0, 7, 0); timerButtons.Children.Add(custom);
            Button stop = PanelButton("结束", delegate { focusEnd = null; Say("休息一下吧。", 4); }, false); Grid.SetColumn(stop, 1); timerButtons.Children.Add(stop); timerBody.Children.Add(timerButtons);
            body.Children.Add(timerCard);

            Border settingsCard = PanelCard(); settingsCard.Margin = new Thickness(0, 10, 0, 0); StackPanel settingsBody = new StackPanel(); settingsCard.Child = settingsBody;
            settingsBody.Children.Add(PanelSection("桌宠设置", "SETTINGS"));
            TextBlock nameLabel = Label("称呼", 11, Color.FromRgb(99, 122, 145)); nameLabel.Margin = new Thickness(0, 10, 0, 5); settingsBody.Children.Add(nameLabel);
            Grid nameRow = new Grid(); nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            TextBox name = new TextBox { Text = settings.Nickname, MaxLength = 12, Padding = new Thickness(8, 7, 8, 7), FontSize = 13, BorderBrush = PanelBrush(202, 220, 236), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center }; nameRow.Children.Add(name);
            Button rename = PanelButton("修改", delegate { string n = name.Text.Trim(); if (n.Length == 0) return; settings.Nickname = n; settings.Save(); heading.Text = n + "在这里"; Say("好，那就叫我" + n + "。", 4); }, false); rename.Margin = new Thickness(7, 0, 0, 0); Grid.SetColumn(rename, 1); nameRow.Children.Add(rename); settingsBody.Children.Add(nameRow);
            TextBlock sizeText = Label("桌宠大小", 11, Color.FromRgb(99, 122, 145)); sizeText.Margin = new Thickness(0, 12, 0, 2); settingsBody.Children.Add(sizeText);
            Slider slider = new Slider { Minimum = 150, Maximum = 340, Value = settings.Size, Margin = new Thickness(0, 6, 0, 8), TickFrequency = 10, IsSnapToTickEnabled = true }; slider.ValueChanged += delegate { settings.Size = (int)slider.Value; ApplySize(); SavePosition(); }; settingsBody.Children.Add(slider);
            CheckBox top = new CheckBox { Content = "保持在其他窗口上方", IsChecked = settings.OnTop, FontSize = 12, Foreground = new SolidColorBrush(Ink), Margin = new Thickness(0, 3, 0, 2) }; top.Checked += delegate { Topmost = true; settings.OnTop = true; settings.Save(); }; top.Unchecked += delegate { Topmost = false; settings.OnTop = false; settings.Save(); }; settingsBody.Children.Add(top); body.Children.Add(settingsCard);

            LoginStartup.AddSwitch(settingsBody);
            Grid actions = new Grid { Margin = new Thickness(0, 12, 0, 0) }; actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
            Button sleep = null; sleep = PanelButton(settings.Sleeping ? "叫醒杏菜" : "小憩一下", delegate { SleepToggle(); sleep.Content = settings.Sleeping ? "叫醒杏菜" : "小憩一下"; }, false); sleep.Margin = new Thickness(0, 0, 5, 0); actions.Children.Add(sleep);
            Button exit = PanelButton("退出桌宠", delegate { Close(); }, false); exit.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(exit, 1); actions.Children.Add(exit); body.Children.Add(actions);
            TextBlock help = Label("单击互动  ·  拖动移动  ·  双击聊天", 10, Color.FromRgb(118, 139, 159)); help.HorizontalAlignment = HorizontalAlignment.Center; help.Margin = new Thickness(0, 13, 0, 0); body.Children.Add(help);
            panel.Closed += delegate { panel = null; focusLabel = null; };
            panel.Show(); panel.Activate();
        }

        private void WritePanelPreview()
        {
            try
            {
                Window selected = Program.ChatPreview ? (Window)chatWindow : Program.VoicePreview ? (Window)mediaWindow : panel;
                if (selected == null || !selected.IsVisible) throw new InvalidOperationException("Panel preview unavailable.");
                if (Program.NarrowPreview) { selected.MinWidth = 0; selected.Width = Math.Min(450, SystemParameters.WorkArea.Width - 20); }
                selected.UpdateLayout();
                int width = (int)Math.Ceiling(selected.ActualWidth), height = (int)Math.Ceiling(selected.ActualHeight);
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Panel preview is empty.");
                RenderTargetBitmap render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                render.Render(selected);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (FileStream output = File.Create(Program.SmokePath)) encoder.Save(output);
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Program.Log("Panel preview failed: " + error.Message); Application.Current.Shutdown(1); }
        }

        private static SolidColorBrush PanelBrush(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
        private static Border PanelCard() { return new Border { Background = Brushes.White, BorderBrush = PanelBrush(218, 231, 242), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 12, 14, 13) }; }
        private static Grid PanelSection(string title, string caption)
        {
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock text = GptUi.Text(title, 14, PanelBrush(32, 62, 93)); text.FontWeight = FontWeights.Bold; row.Children.Add(text);
            TextBlock small = GptUi.Text(caption, 9, PanelBrush(126, 155, 182)); small.FontWeight = FontWeights.SemiBold; small.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(small, 1); row.Children.Add(small); return row;
        }
        private static Button PanelButton(string label, Action action, bool primary)
        {
            Button button = Button(label, action); button.FontSize = 12; button.FontWeight = FontWeights.SemiBold; button.Padding = new Thickness(11, 8, 11, 8);
            button.Background = primary ? PanelBrush(50, 103, 151) : PanelBrush(245, 249, 253); button.Foreground = primary ? Brushes.White : PanelBrush(42, 79, 113); button.BorderBrush = primary ? PanelBrush(50, 103, 151) : PanelBrush(207, 224, 238);
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); template.VisualTree = border;
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.OpacityProperty, 0.84)); template.Triggers.Add(hover);
            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2))); template.Triggers.Add(focus);
            button.Template = template; return button;
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
    }
}
