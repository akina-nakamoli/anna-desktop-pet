using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
namespace AnnaDesktopPet {
    internal static class PanelTheme {
        internal static void Apply(Window window,string title,string tag) {
            bool night=Appearance.Night;
            Brush ink=new SolidColorBrush(night?Color.FromRgb(225,235,247):Color.FromRgb(39,58,77));
            Brush card=new SolidColorBrush(night?Color.FromRgb(39,55,77):Colors.White);
            window.FontFamily=new FontFamily("Microsoft YaHei UI");window.FontSize=13;window.Foreground=ink;
            window.Background=new SolidColorBrush(night?Color.FromRgb(20,29,46):Color.FromRgb(231,241,250));
            window.MinWidth=340;window.MinHeight=300;window.Height=System.Math.Max(window.Height+100,360);window.MaxHeight=SystemParameters.WorkArea.Height;window.MaxWidth=SystemParameters.WorkArea.Width;
            var original=window.Content as FrameworkElement;window.Content=null;
            var layout=new Grid {Margin=new Thickness(18)};layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());window.Content=layout;
            var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="ANNA YANAMI  /  "+tag,Foreground=new SolidColorBrush(Color.FromRgb(250,207,107)),FontSize=10,FontWeight=FontWeights.Bold});heading.Children.Add(new TextBlock{Text=title,Foreground=Brushes.White,FontSize=23,FontWeight=FontWeights.Bold,Margin=new Thickness(0,7,0,0)});
            layout.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(30,55,90)),CornerRadius=new CornerRadius(15),Padding=new Thickness(18,15,18,17),Margin=new Thickness(0,0,0,14),Child=heading});
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=original};
            var surface=new Border{Background=card,CornerRadius=new CornerRadius(15),BorderBrush=new SolidColorBrush(Color.FromRgb(204,222,237)),BorderThickness=new Thickness(1),Child=scroll};Grid.SetRow(surface,1);layout.Children.Add(surface);
            if(original!=null)StyleTree(original,ink,night);
            var buttonStyle=(Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='#356B9B'/><Setter Property='BorderBrush' Value='#6395BC'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,9'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.85'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            window.Resources[typeof(Button)]=buttonStyle;
        }
        private static void StyleTree(DependencyObject node,Brush ink,bool night){
            var text=node as TextBlock;if(text!=null)text.Foreground=ink;
            var check=node as CheckBox;if(check!=null)check.Foreground=ink;
            var field=node as TextBox;if(field!=null){field.Background=new SolidColorBrush(night?Color.FromRgb(26,40,59):Color.FromRgb(247,251,255));field.Foreground=ink;field.BorderBrush=new SolidColorBrush(Color.FromRgb(151,181,207));field.Padding=new Thickness(8);}
            var button=node as Button;if(button!=null){button.ClearValue(Control.BackgroundProperty);button.ClearValue(Control.ForegroundProperty);button.ClearValue(Control.BorderBrushProperty);}
            foreach(object child in LogicalTreeHelper.GetChildren(node)){var d=child as DependencyObject;if(d!=null)StyleTree(d,ink,night);}
        }
    }
}
