using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace AnnaDesktopPet {
    internal sealed class MusicPanel : Window {
        private TextBlock status;
        private bool busy;
        private DispatcherTimer timer;
        internal static Task<string> Command(string command) {
            return Task.Run(delegate {
                var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MusicSession.ps1") + "\" -Command " + command) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, StandardOutputEncoding=System.Text.Encoding.UTF8 };
                using(var p=Process.Start(start)) { string text=p.StandardOutput.ReadToEnd(); p.WaitForExit(); return text.Trim(); }
            });
        }
        internal MusicPanel() {
            Title="杏菜 · 正在播放"; Width=390; Height=245; Background=new SolidColorBrush(Color.FromRgb(235,244,252));
            var root=new StackPanel { Margin=new Thickness(22) }; Content=root;
            root.Children.Add(new TextBlock { Text="ANNA YANAMI / MUSIC", Foreground=Brushes.SteelBlue, FontWeight=FontWeights.Bold });
            status=new TextBlock { Text="正在读取 QQ 音乐…", Margin=new Thickness(0,18,0,18), TextWrapping=TextWrapping.Wrap, FontSize=17 }; root.Children.Add(status);
            var buttons=new WrapPanel(); root.Children.Add(buttons);
            string[] labels={"上一首","暂停","继续","下一首"}, commands={"previous","pause","play","next"};
            for(int i=0;i<labels.Length;i++) { string cmd=commands[i]; var b=new Button { Content=labels[i],Padding=new Thickness(9),Margin=new Thickness(3) }; b.Click+=async delegate { await Refresh(cmd); }; buttons.Children.Add(b); }
            timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) }; timer.Tick+=async delegate { await Refresh("status"); }; Loaded+=async delegate { timer.Start(); await Refresh("status"); }; Closed+=delegate { timer.Stop(); };
            PanelTheme.Apply(this,"杏菜的音乐时间","MUSIC");
        }
        private async Task Refresh(string cmd) { if(busy || !IsVisible)return; busy=true; try { status.Text=await Command(cmd); } catch(Exception) { status.Text="暂时无法读取 QQ 音乐。"; } finally {busy=false;} }
    }
}
