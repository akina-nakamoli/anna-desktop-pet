using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
namespace AnnaDesktopPet {
    internal static class PetQuickMenu {
        internal static ContextMenu Create(Action[] commands,bool sleeping,bool autonomous,bool top,bool focusing) {
            bool night=Appearance.Theme=="夜间";
            var menu=new ContextMenu {Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=12};
            var style=(Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Foreground' Value='#244260'/><Setter Property='Background' Value='#ECF4FB'/><Setter Property='BorderBrush' Value='#D3E2EF'/><Setter Property='Padding' Value='10,9'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#D5E8F7'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='#F2BD57'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");menu.Resources[typeof(Button)]=style;
            menu.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><ItemsPresenter/></ControlTemplate>");
            var body=new StackPanel {Width=252,Margin=new Thickness(12)};
            body.Children.Add(new TextBlock { Text="快捷操作", FontSize=15, FontWeight=FontWeights.SemiBold, Foreground=night?Brushes.White:GptUi.Brush(47,80,103), Margin=new Thickness(4,1,0,5) });
            Func<string,int,Button> button=delegate(string label,int index){var b=new Button{Content=label,Margin=new Thickness(3),MinHeight=35};b.Click+=delegate{menu.IsOpen=false;commands[index]();};return b;};
            var chat=button("和杏菜聊聊    Ctrl+Alt+C",0);chat.Background=new SolidColorBrush(Color.FromRgb(63,116,154));chat.Foreground=Brushes.White;chat.Margin=new Thickness(3,4,3,4);body.Children.Add(chat);
            var tiles=new WrapPanel();string[] labels={"一起听","换装与场景","专注时间","作息问候"};int[] ids={1,2,3,4};for(int i=0;i<labels.Length;i++){var b=button(labels[i],ids[i]);b.Width=120;tiles.Children.Add(b);}body.Children.Add(tiles);
            var rest=button(sleeping?"叫醒杏菜":"让杏菜小憩",10);body.Children.Add(rest);
            body.Children.Add(Section("偏好与工具",night));var tools=new WrapPanel();string[] toolNames={"桌宠设置","日语配音与动作","麦克风与识别","快捷键说明"};int[] toolIds={11,12,20,13};for(int i=0;i<toolNames.Length;i++)tools.Children.Add(button(toolNames[i],toolIds[i]));body.Children.Add(tools);
            var switches=new WrapPanel {Margin=new Thickness(5,10,0,7)};var auto=new CheckBox{Content="自主活动",IsChecked=autonomous,Margin=new Thickness(0,0,22,0),Foreground=night?Brushes.White:Brushes.DarkSlateBlue};auto.Click+=delegate{commands[14]();};switches.Children.Add(auto);var pin=new CheckBox{Content="保持置顶",IsChecked=top,Foreground=night?Brushes.White:Brushes.DarkSlateBlue};pin.Click+=delegate{commands[15]();};switches.Children.Add(pin);body.Children.Add(switches);
            if(focusing)body.Children.Add(button("结束当前专注",16));
            var footer=new WrapPanel{Margin=new Thickness(0,9,0,0)};var hide=button("收起 · Ctrl+Alt+H",17);hide.Width=160;footer.Children.Add(hide);var exit=button("退出",18);exit.Width=80;footer.Children.Add(exit);body.Children.Add(footer);
            var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-24)};
            var card=new Border{Background=new SolidColorBrush(night?Color.FromRgb(25,38,57):Color.FromRgb(232,243,250)),CornerRadius=new CornerRadius(17),BorderBrush=new SolidColorBrush(Color.FromRgb(149,180,207)),BorderThickness=new Thickness(1),Child=scroll};
            var item=new MenuItem{Header=card,StaysOpenOnClick=true,Padding=new Thickness(0)};item.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='MenuItem'><ContentPresenter ContentSource='Header'/></ControlTemplate>");menu.Items.Add(item);return menu;
        }
        private static TextBlock Section(string text,bool night){return new TextBlock{Text=text,FontSize=11,FontWeight=FontWeights.Bold,Foreground=night?Brushes.LightSteelBlue:Brushes.SlateGray,Margin=new Thickness(5,14,0,5)};}
    }
}
