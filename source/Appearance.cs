using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace AnnaDesktopPet {
    internal static class Appearance {
        internal static string Theme="自动", Background="", Outfit="默认";
        internal static event Action OutfitChanged;
        private static string SettingsPath {get{return Path.Combine(Program.DataDir,"appearance.txt");}}
        static Appearance(){try{var lines=File.ReadAllLines(SettingsPath);Theme=lines[0];Background=lines[1];Outfit=lines[2];}catch{}}
        internal static bool Night {get{return Theme=="夜间" || Theme=="自动" && (DateTime.Now.Hour>=19 || DateTime.Now.Hour<7);}}
        internal static string Asset(string name){return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,Outfit=="默认" || Outfit=="校服1"?"assets":Path.Combine("outfits",Path.GetFileName(Outfit)),name);}
        internal static void Show(){
            string previousOutfit=Outfit;
            var window=new Window{Title="杏菜 · 换装与场景",Width=450,Height=420,Background=Brushes.AliceBlue};var root=new StackPanel{Margin=new Thickness(24)};window.Content=root;
            PanelTheme.Apply(window,"换装与场景","DRESS & SCENE");
            root.Children.Add(new TextBlock{Text="昼夜主题",Margin=new Thickness(0,18,0,5)});var theme=new ComboBox{ItemsSource=new[]{"自动","日间","夜间"},SelectedItem=Theme};root.Children.Add(theme);
            root.Children.Add(new TextBlock{Text="服装素材包",Margin=new Thickness(0,18,0,5)});var clothes=new ComboBox();clothes.Items.Add("默认");string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"outfits");if(Directory.Exists(folder))foreach(string dir in Directory.GetDirectories(folder)){if(File.Exists(Path.Combine(dir,"pet.png")) && File.Exists(Path.Combine(dir,"poses.png")))clothes.Items.Add(Path.GetFileName(dir));}clothes.SelectedItem=Outfit;if(clothes.SelectedIndex<0)clothes.SelectedIndex=0;root.Children.Add(clothes);
            string background=Background;var choose=new Button{Content="选择聊天背景图片",Margin=new Thickness(0,18,0,5),Padding=new Thickness(7)};choose.Click+=delegate{var dialog=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg"};if(dialog.ShowDialog(window)==true)background=dialog.FileName;};root.Children.Add(choose);
            var reset=new Button{Content="恢复默认背景",Padding=new Thickness(7)};reset.Click+=delegate{background="";};root.Children.Add(reset);
            root.Children.Add(new TextBlock{Text="保存服装后，杏菜会立即播放换装动作。主题和背景重新打开聊天窗口生效。自动模式：19:00～次日7:00为夜间。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
            window.Closed+=delegate { if(previousOutfit!=Outfit && OutfitChanged!=null)OutfitChanged(); };
            var save=new Button{Content="保存设置",Padding=new Thickness(8)};root.Children.Add(save);save.Click+=delegate{try{Directory.CreateDirectory(Program.DataDir);File.WriteAllLines(SettingsPath,new[]{(string)theme.SelectedItem,background,(string)clothes.SelectedItem});Theme=(string)theme.SelectedItem;Background=background;Outfit=(string)clothes.SelectedItem;window.Close();}catch{MessageBox.Show(window,"保存失败。");}};window.Show();
        }
    }
}
