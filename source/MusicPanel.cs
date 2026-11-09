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
            Title="杏菜 · 音乐控制"; Width=460; Height=365; Background=new SolidColorBrush(Color.FromRgb(235,244,252));
            var root=new StackPanel { Margin=new Thickness(22) }; Content=root;
            root.Children.Add(new TextBlock { Text="ANNA YANAMI / MUSIC", Foreground=Brushes.SteelBlue, FontWeight=FontWeights.Bold });
            status=new TextBlock { Text="正在读取 QQ 音乐…", Margin=new Thickness(0,18,0,18), TextWrapping=TextWrapping.Wrap, FontSize=17 }; root.Children.Add(status);
            var buttons=new WrapPanel(); root.Children.Add(buttons);
            string[] labels={"上一首","暂停","继续","下一首"}, commands={"previous","pause","play","next"};
            for(int i=0;i<labels.Length;i++) { string cmd=commands[i]; var b=new Button { Content=labels[i],Padding=new Thickness(9),Margin=new Thickness(3) }; b.Click+=async delegate { await Refresh(cmd); }; buttons.Children.Add(b); }
            AddPathSettings(root);
            timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) }; timer.Tick+=async delegate { await Refresh("status"); }; Loaded+=async delegate { timer.Start(); await Refresh("status"); }; Closed+=delegate { timer.Stop(); };
            PanelTheme.Apply(this,"杏菜的音乐时间","MUSIC");
        }
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
                if (picker.ShowDialog(this) == true) path.Text = picker.FileName;
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
        private async Task Refresh(string cmd) { if(busy || !IsVisible)return; busy=true; try { status.Text=await Command(cmd); } catch(Exception) { status.Text="暂时无法读取 QQ 音乐。"; } finally {busy=false;} }
    }
}
