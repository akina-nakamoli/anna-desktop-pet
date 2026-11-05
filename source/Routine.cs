using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace AnnaDesktopPet {
    internal sealed class RoutineSettings {
        public bool Enabled=true;
        public int Evening=22, QuietStart=23, QuietEnd=8, Interval=120;
        public string Greeted="";
        public DateTime LastReminder=DateTime.MinValue;
    }
    internal sealed class Routine {
        private RoutineSettings settings;
        private readonly DispatcherTimer timer=new DispatcherTimer();
        private readonly Action<string,string> notify;
        private string PathName {get{return Path.Combine(Program.DataDir,"routine.json");}}
        internal Routine(Action<string,string> callback) {
            notify=callback; settings=new RoutineSettings();
            try { if(File.Exists(PathName))settings=new JavaScriptSerializer().Deserialize<RoutineSettings>(File.ReadAllText(PathName,Encoding.UTF8))??new RoutineSettings(); }catch { }
            timer.Interval=TimeSpan.FromSeconds(30); timer.Tick+=delegate { Check(); };
        }
        internal void Start() { if(Program.Smoke)return; timer.Start(); Check(); }
        internal void Stop() {timer.Stop();}
        private void Save() { if(Program.Smoke)return; Directory.CreateDirectory(Program.DataDir); string temp=PathName+".tmp"; File.WriteAllText(temp,new JavaScriptSerializer().Serialize(settings),Encoding.UTF8); if(File.Exists(PathName))File.Replace(temp,PathName,null);else File.Move(temp,PathName); }
        internal static bool Quiet(int hour,int start,int end) { return start==end || (start<end ? hour>=start && hour<end : hour>=start || hour<end); }
        private void Check() {
            if(!settings.Enabled)return;
            DateTime now=DateTime.Now;
            if(Quiet(now.Hour,settings.QuietStart,settings.QuietEnd))return;
            try {
                string day=now.ToString("yyyy-MM-dd");
                if(settings.Greeted!=day) {
                    settings.Greeted=day; settings.LastReminder=now; Save();
                    notify(now.Hour<12?"早上好！今天也一起加油吧。":now.Hour<18?"下午好，今天过得怎么样？":"晚上好！忙完也记得照顾自己哦。",now.Hour<12?"おはよう！今日も一緒に頑張ろうね。":now.Hour<18?"こんにちは！今日はどんな一日？":"こんばんは！無理しないでね。"); return;
                }
                if(now.Hour>=settings.Evening && (now-settings.LastReminder).TotalMinutes>=settings.Interval) {
                    settings.LastReminder=now; Save(); notify("已经不早啦，休息一下眼睛，也准备早点睡吧。","もう遅いよ。目を休めて、そろそろ寝る準備をしようね。");
                }
            }catch { Program.Log("Routine reminder could not be saved."); }
        }
        internal void ShowSettings() {
            var window=new Window {Title="杏菜 · 作息问候",Width=420,Height=450,ResizeMode=ResizeMode.NoResize,Background=new SolidColorBrush(Color.FromRgb(235,244,252))};
            var root=new StackPanel {Margin=new Thickness(24)}; window.Content=root;
            root.Children.Add(new TextBlock {Text="每天的一点问候",FontSize=23,FontWeight=FontWeights.Bold,Foreground=Brushes.MidnightBlue,Margin=new Thickness(0,0,0,18)});
            var enabled=new CheckBox {Content="开启每日首次问候和晚间提醒",IsChecked=settings.Enabled};root.Children.Add(enabled);
            string[] labels={"晚间提醒开始（0～23 时）","提醒间隔（15～720 分钟）","安静开始（0～23 时）","安静结束（0～23 时）"};
            int[] values={settings.Evening,settings.Interval,settings.QuietStart,settings.QuietEnd}; var fields=new TextBox[4];
            for(int i=0;i<4;i++){root.Children.Add(new TextBlock {Text=labels[i],Margin=new Thickness(0,12,0,4)});fields[i]=new TextBox {Text=values[i].ToString(),Padding=new Thickness(5)};root.Children.Add(fields[i]);}
            root.Children.Add(new TextBlock {Text="安静时段不弹出问候、不配音；起止相同表示全天安静。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)});
            var save=new Button {Content="保存设置",Padding=new Thickness(8)};root.Children.Add(save);
            save.Click+=delegate {int[] n=new int[4];for(int i=0;i<4;i++){if(!Int32.TryParse(fields[i].Text,out n[i]) || n[i]<(i==1?15:0) || n[i]>(i==1?720:23)){MessageBox.Show(window,"请按标注范围填写整数。");return;}} var previous=settings; settings=new RoutineSettings {Enabled=enabled.IsChecked==true,Evening=n[0],Interval=n[1],QuietStart=n[2],QuietEnd=n[3],Greeted=previous.Greeted,LastReminder=DateTime.Now};try{Save();window.Close();}catch{settings=previous;MessageBox.Show(window,"保存失败，请检查配置目录。");}};
            PanelTheme.Apply(window,"每天的一点问候","DAILY LIFE");
            window.Show();
        }
    }
}
