using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace AnnaDesktopPet {
    internal static class VoiceInputSettings {
        private static Window window;
        private static Microphone test;
        internal static bool Testing {get{return test!=null;}}
        private static string FileName {get{return Path.Combine(Program.DataDir,"microphone.txt");}}
        internal static void Load(){if(Program.Smoke)return;try{int device;if(Int32.TryParse(File.ReadAllText(FileName),out device)){LocalAsr.Device=-1;foreach(string name in Microphone.Devices())if(name.StartsWith(device+" · "))LocalAsr.Device=device;}}catch{}}
        internal static void Show(Func<string> status,Func<bool> recording){
            if(window!=null){window.Show();window.Activate();return;}
            window=new Window{Title="杏菜 · 语音输入与麦克风",Width=480,Height=490};
            var body=new StackPanel{Margin=new Thickness(20)};window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            body.Children.Add(new TextBlock{Text="选择说话用的麦克风",Margin=new Thickness(0,0,0,8)});
            string[] devices=Microphone.Devices();var combo=new ComboBox{ItemsSource=devices,SelectedIndex=0,Padding=new Thickness(5)};for(int i=1;i<devices.Length;i++)if(devices[i].StartsWith(LocalAsr.Device+" · "))combo.SelectedIndex=i;body.Children.Add(combo);
            var info=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,12)};body.Children.Add(info);
            var engineButton=new Button{Content="选择中文语音识别引擎",Padding=new Thickness(10)};
            body.Children.Add(engineButton);
            var engineNotice=new TextBlock{Text="请选择含中文识别模型的引擎目录中的 api_v2.py。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)};
            body.Children.Add(engineNotice);
            engineButton.Click+=delegate{
                if(recording() || Testing){engineNotice.Text="请先结束录音、识别或麦克风测试。";return;}
                var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择中文语音识别引擎的 api_v2.py",Filter="引擎入口 (api_v2.py)|api_v2.py",CheckFileExists=true};
                if(dialog.ShowDialog(window)!=true)return;
                try{
                    AsrWorker.RegisterInputEngine(Path.GetDirectoryName(dialog.FileName));
                    engineNotice.Text="已保存中文识别引擎。请退出并重新打开桌宠，再按住侧键说话。";
                }catch(Exception ex){engineNotice.Text=ex.Message;}
            };

            var meter=new ProgressBar{Minimum=0,Maximum=100,Height=12};body.Children.Add(meter);
            var label=new TextBlock{Text="音量指示：尚未开启测试",Margin=new Thickness(0,6,0,12)};body.Children.Add(label);
            var toggle=new Button{Content="测试麦克风（只显示音量，不保存录音）",Padding=new Thickness(10)};body.Children.Add(toggle);
            toggle.Click+=delegate{
                if(test!=null){test.Dispose();test=null;toggle.Content="测试麦克风（只显示音量，不保存录音）";return;}
                if(recording()){label.Text="正在录音或识别，请结束后再测试。";return;}
                try{int id=combo.SelectedIndex==0?-1:Int32.Parse(((string)combo.SelectedItem).Split(' ')[0]);test=new Microphone();test.Start(id);toggle.Content="停止麦克风测试";label.Text="请说话，观察音量条；测试最多持续30秒。";}catch(Exception ex){if(test!=null)test.Dispose();test=null;label.Text=ex.Message;}
            };
            var started=DateTime.UtcNow;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{info.Text=status()+"\n模型只加载一次，音频在本机处理。按住后退侧键说话，松开识别。";meter.Value=test==null?0:test.Level*100;if(test==null)started=DateTime.UtcNow;else if((DateTime.UtcNow-started).TotalSeconds>=30){test.Dispose();test=null;toggle.Content="测试麦克风（只显示音量，不保存录音）";label.Text="测试结束，麦克风已关闭。";}};
            var save=new Button{Content="保存麦克风选择",Margin=new Thickness(0,14,0,0),Padding=new Thickness(10)};body.Children.Add(save);save.Click+=delegate{if(recording()){label.Text="请结束当前录音后再保存。";return;}try{int id=combo.SelectedIndex==0?-1:Int32.Parse(((string)combo.SelectedItem).Split(' ')[0]);Directory.CreateDirectory(Program.DataDir);File.WriteAllText(FileName,id.ToString());LocalAsr.Device=id;window.Close();}catch{label.Text="保存失败，请检查配置目录。";}};
            window.Closed+=delegate{timer.Stop();if(test!=null){test.Dispose();test=null;}window=null;};window.IsVisibleChanged+=delegate{if(window!=null && !window.IsVisible && test!=null){test.Dispose();test=null;toggle.Content="测试麦克风（只显示音量，不保存录音）";}};
            PanelTheme.Apply(window,"语音输入与麦克风","VOICE INPUT");timer.Start();window.Show();
        }
    }
}
