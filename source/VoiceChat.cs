using System;
using System.Text;
using System.Speech.Recognition;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace AnnaDesktopPet {
    internal sealed class VoiceChat : IDisposable {
        private delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct MouseData {public int X,Y;public uint Buttons,Flags,Time;public UIntPtr Extra;}
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,MouseHook callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        private readonly HwndSource source; private readonly IntPtr handle;
        private readonly Action<string> send,notice;
        private readonly Func<bool> ready;
        private readonly DispatcherTimer timer=new DispatcherTimer();
        private SpeechRecognitionEngine recognizer;
        private LocalAsr localRecorder;
        private readonly AsrWorker worker=new AsrWorker();
        internal void CancelCapture(){finishingVersion++;Cleanup();}
        internal void ShowSettings(){VoiceInputSettings.Show(delegate{return worker.Status;},delegate{return listening || finishing;});}
        private void Level(bool visible){var pet=source.RootVisual as PetWindow;if(pet!=null)pet.SetVoiceLevel(visible,localRecorder==null?0:localRecorder.Level);}
        private readonly StringBuilder text=new StringBuilder();
        private bool captured,listening,finishing,disposed;
        private readonly MouseHook callback;
        private readonly IntPtr hook;
        private DateTime started;
        private bool uncertain;
        private int finishingVersion;
        internal VoiceChat(Window owner,Action<string> result,Action<string> status,Func<bool> canStart){
            send=result;notice=status;ready=canStart;handle=new WindowInteropHelper(owner).Handle;source=HwndSource.FromHwnd(handle);
            VoiceInputSettings.Load();worker.WarmUp();
            callback=MouseMessage;hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
            if(hook==IntPtr.Zero)Program.Log("Voice mouse side-button hook unavailable.");
            timer.Interval=TimeSpan.FromMilliseconds(60);timer.Tick+=delegate{if(listening){Level(true);if(!captured || (DateTime.UtcNow-started).TotalSeconds>=30)Finish();}};
        }
        private IntPtr MouseMessage(int code,IntPtr message,IntPtr data){
            if(code>=0 && !disposed && (message.ToInt32()==0x20B || message.ToInt32()==0x20C)){
                var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
                if((mouse.Flags&1)==0 && (mouse.Buttons>>16)==1){
                    if(message.ToInt32()==0x20B && !captured && !listening && !finishing && !VoiceInputSettings.Testing && ready()){
                        captured=true;source.Dispatcher.BeginInvoke(new Action(Start));return new IntPtr(1);
                    }
                    if(message.ToInt32()==0x20C && captured){captured=false;source.Dispatcher.BeginInvoke(new Action(delegate{if(listening)Finish();}));return new IntPtr(1);}
                }
            }
            return CallNextHookEx(hook,code,message,data);
        }
        private void Start(){
            if(disposed || !captured || listening || finishing || !ready())return;
            try{localRecorder=new LocalAsr(worker);localRecorder.Start();listening=true;started=DateTime.UtcNow;timer.Start();Level(true);notice("正在听，松开侧键后用本地中文模型识别。");}catch(Exception ex){Cleanup();notice(ex.Message);}return;
        }
        private void LegacyStart(){
            try {
                RecognizerInfo chosen=null;foreach(var info in SpeechRecognitionEngine.InstalledRecognizers())if(info.Culture.Name=="zh-CN"){chosen=info;break;}
                if(chosen==null){notice("未安装 Windows 中文语音识别组件，请在系统语言设置中安装中文语音功能。");return;}
                text.Clear();uncertain=false;recognizer=new SpeechRecognitionEngine(chosen);
                recognizer.InitialSilenceTimeout=TimeSpan.FromSeconds(8);recognizer.EndSilenceTimeout=TimeSpan.FromMilliseconds(700);recognizer.EndSilenceTimeoutAmbiguous=TimeSpan.FromMilliseconds(1000);
                recognizer.LoadGrammar(new DictationGrammar());
                var commands=new GrammarBuilder(new Choices("随机播放一首音乐","随机播放一首歌","随机来一首","放我的收藏","查看收藏","收藏这首","下一首","上一首","暂停音乐","继续播放","打开QQ音乐"));commands.Culture=chosen.Culture;
                recognizer.LoadGrammar(new Grammar(commands){Name="music_commands",Priority=1,Weight=1f});
                recognizer.SpeechRecognized+=delegate(object sender,SpeechRecognizedEventArgs e){string words=e.Result.Text;float confidence=e.Result.Confidence;source.Dispatcher.BeginInvoke(new Action(delegate{if(!disposed){text.Append(words);if(confidence<.65f)uncertain=true;}}));};
                recognizer.SpeechRecognitionRejected+=delegate {source.Dispatcher.BeginInvoke(new Action(delegate{uncertain=true;}));};
                recognizer.RecognizeCompleted+=delegate{source.Dispatcher.BeginInvoke(new Action(Complete));};
                recognizer.SetInputToDefaultAudioDevice();listening=true;started=DateTime.UtcNow;recognizer.RecognizeAsync(RecognizeMode.Multiple);timer.Start();notice("正在听，松开鼠标后退侧键发送（最长30秒）。");
            }catch{Cleanup();notice("麦克风不可用，请检查 Windows 麦克风权限和默认录音设备。");}
        }
        private async void Finish(){timer.Stop();listening=false;finishing=true;int version=++finishingVersion;notice("正在用本地中文模型识别，请稍等……");await System.Threading.Tasks.Task.Delay(400);if(disposed || version!=finishingVersion)return;try{Level(false);string result=await localRecorder.Finish();if(disposed || version!=finishingVersion)return;Cleanup();if(!ready())return;if(result.Length==0)notice("没有识别到说话，请检查选定麦克风。");else if(result.Length<4){var pet=source.RootVisual as PetWindow;if(pet!=null)pet.ReviewVoiceTranscript(result);}else send(result);}catch(Exception ex){if(version!=finishingVersion)return;Cleanup();if(!disposed)notice(ex.Message);}}
        private void Complete(){string result=text.ToString().Trim();bool review=uncertain || result.Length<4;Cleanup();if(disposed)return;if(result.Length==0)notice("没听清楚，请按住鼠标后退侧键再说一次。");else if(ready()){if(review){var pet=source.RootVisual as PetWindow;if(pet!=null)pet.ReviewVoiceTranscript(result);else notice("这次没听清，请重说。");}else send(result);}}
        private void Cleanup(){timer.Stop();Level(false);listening=finishing=false;if(localRecorder!=null){localRecorder.Dispose();localRecorder=null;}if(recognizer!=null){recognizer.Dispose();recognizer=null;}}
        public void Dispose(){disposed=true;finishingVersion++;if(hook!=IntPtr.Zero)UnhookWindowsHookEx(hook);Cleanup();worker.Dispose();}
    }
}
