using System;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
namespace AnnaDesktopPet {
    internal static class LoginStartup {
        private const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName="AnnaDesktopPet";
        private static bool Enabled(){using(var key=Registry.CurrentUser.OpenSubKey(KeyPath))return key!=null && key.GetValue(ValueName)!=null;}
        private static void Set(bool enabled){
            if(Program.Smoke)return;
            using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)){
                if(key==null)throw new InvalidOperationException("无法打开当前用户启动设置。");
                if(enabled)key.SetValue(ValueName,"\""+Process.GetCurrentProcess().MainModule.FileName+"\"",RegistryValueKind.String);
                else key.DeleteValue(ValueName,false);
            }
        }
        internal static void RefreshIfEnabled(){if(Program.Smoke || Program.MediaCheck || Program.PanelCheck || Program.UiCheck)return;try{if(Enabled())Set(true);}catch{Program.Log("Login startup entry could not be refreshed.");}}
        internal static void AddSwitch(Panel panel){
            var toggle=new CheckBox {Content="开机自启动（当前 Windows 用户）",Margin=new Thickness(0,10,0,3),FontSize=12,Foreground=GptUi.Ink};
            bool updating=false;
            try{toggle.IsChecked=Enabled();}catch{toggle.IsEnabled=false;toggle.ToolTip="无法读取 Windows 登录启动设置。";}
            toggle.ToolTip="勾选后随当前用户登录 Windows 启动；取消勾选立即移除启动项。";
            RoutedEventHandler changed=delegate{
                if(updating)return;bool desired=toggle.IsChecked==true;
                try{Set(desired);}
                catch{updating=true;toggle.IsChecked=!desired;updating=false;MessageBox.Show("开机自启动设置未能保存，请检查 Windows 权限。","杏菜 · 启动设置");}
            };
            toggle.Checked+=changed;toggle.Unchecked+=changed;panel.Children.Add(toggle);
        }
    }
}
