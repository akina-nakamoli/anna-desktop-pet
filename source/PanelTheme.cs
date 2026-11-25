using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace AnnaDesktopPet
{
    internal static class PanelPalette
    {
        private static readonly System.Collections.Generic.List<Action> updates = new System.Collections.Generic.List<Action>();
        internal static readonly SolidColorBrush Sky = Pair("#ECF6FC", "#152331");
        internal static readonly SolidColorBrush Rail = Pair("#D9EAF5", "#223648");
        internal static readonly SolidColorBrush Ink = Pair("#1C364C", "#E3EEF6");
        internal static readonly SolidColorBrush Quiet = Pair("#4D687C", "#ADC2D2");
        internal static readonly SolidColorBrush Accent = Pair("#296B9E", "#8DC9EF");
        internal static readonly SolidColorBrush Line = Pair("#C7DCEA", "#40576A");
        internal static readonly SolidColorBrush Surface = Pair("#FFFFFF", "#253848");
        internal static readonly SolidColorBrush Action = Pair("#356889", "#396784");
        internal static readonly SolidColorBrush Incoming = Pair("#FFFDF9", "#2A4051");
        internal static readonly SolidColorBrush Paid = Pair("#FFF1D6", "#4C3D27");
        internal static readonly SolidColorBrush Price = Pair("#775315", "#F4D48B");
        static PanelPalette()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
        }
        private static SolidColorBrush Pair(string light, string dark)
        {
            Color day = (Color)ColorConverter.ConvertFromString(light), night = (Color)ColorConverter.ConvertFromString(dark);
            var colorSource = new SolidColorBrush(Appearance.Night ? night : day);
            // WPF may freeze shared brushes while sealing styles. A binding keeps the displayed brush live.
            var brush = new SolidColorBrush();
            System.Windows.Data.BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
                new System.Windows.Data.Binding("Color") { Source = colorSource, Mode = System.Windows.Data.BindingMode.OneWay });
            updates.Add(delegate { Color next = Appearance.Night ? night : day; if (colorSource.Color != next) colorSource.Color = next; });
            return brush;
        }
        internal static void Refresh() { foreach (var update in updates) update(); }
    }

    internal static class PanelTheme
    {
        internal static void Apply(Window window, string title, string tag)
        {
            Apply(window, title, tag, true);
        }

        internal static void Apply(Window window, string title, string tag, bool wrapInCard)
        {
            FrameworkElement original = (FrameworkElement)window.Content;
            window.Content = null;
            window.FontFamily = new FontFamily("Microsoft YaHei UI");
            window.FontSize = 13;
            window.Foreground = GptChatWindow.PanelText;
            window.Background = GptChatWindow.PanelSky;
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            window.Resources["NightSky"] = PanelPalette.Sky;
            window.Resources["NightRail"] = PanelPalette.Rail;
            window.Resources["NightInk"] = PanelPalette.Ink;
            window.Resources["NightQuiet"] = PanelPalette.Quiet;
            window.Resources["NightAccent"] = PanelPalette.Accent;
            window.Resources["NightLine"] = PanelPalette.Line;
            window.Resources["NightSurface"] = PanelPalette.Surface;
            window.Resources["NightAction"] = PanelPalette.Action;
            window.Resources["NightIncoming"] = PanelPalette.Incoming;
            window.Resources["NightPaid"] = PanelPalette.Paid;
            window.Resources["NightPrice"] = PanelPalette.Price;
            window.Resources.MergedDictionaries.Add(FormResources);

            bool canResize = window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip;
            window.WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(window, new WindowChrome {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(canResize ? 6 : 0),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14), UseAeroCaptionButtons = false
            });
            Rect area = SystemParameters.WorkArea;
            double maxWidth = Math.Max(1, Math.Min(window.MaxWidth, area.Width - 12));
            double maxHeight = Math.Max(1, Math.Min(window.MaxHeight, area.Height - 12));
            window.MinWidth = Math.Min(maxWidth, Math.Max(320, window.MinWidth));
            window.MinHeight = Math.Min(maxHeight, Math.Max(250, window.MinHeight));
            window.MaxWidth = maxWidth;
            window.MaxHeight = maxHeight;
            if (!Double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, maxWidth);
            if (!Double.IsNaN(window.Height)) window.Height = Math.Min(window.Height + (wrapInCard ? 90 : 0), maxHeight);
            if (window.WindowStartupLocation == WindowStartupLocation.Manual && Double.IsNaN(window.Left) && Double.IsNaN(window.Top))
                window.WindowStartupLocation = window.Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.Children.Add(TitleBar(window, title, canResize));

            var surface = new Border { Margin = new Thickness(16, 0, 16, 15) };
            if (wrapInCard)
            {
                surface.CornerRadius = new CornerRadius(12);
                surface.Background = PanelPalette.Surface;
                surface.BorderBrush = GptChatWindow.PanelLine;
                surface.BorderThickness = new Thickness(1);
                surface.Padding = new Thickness(1);
                surface.Child = new ScrollViewer { Content = original, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            }
            else surface.Child = original;
            Grid.SetRow(surface, 1);
            layout.Children.Add(surface);
            window.Content = new Border { CornerRadius = new CornerRadius(14), Background = GptChatWindow.PanelSky,
                BorderThickness = new Thickness(1), BorderBrush = GptChatWindow.PanelLine, Child = layout };
        }

        private static Grid TitleBar(Window window, string title, bool canResize)
        {
            var header = new Grid { Margin = new Thickness(17, 9, 9, 10), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.Handled || e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
                if (e.ClickCount == 2 && canResize) ToggleMaximize(window);
                else window.DragMove();
            };

            var image = GptChatWindow.ReadLocalImage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "杏菜Q版头像.png"), 112);
            if (image != null)
            {
                var face = new Border { Width = 41, Height = 41, CornerRadius = new CornerRadius(12),
                    Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill }, Margin = new Thickness(0, 0, 10, 0) };
                RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.HighQuality);
                header.Children.Add(face);
                window.Icon = image;
            }
            var heading = new TextBlock { Text = title, Foreground = GptChatWindow.PanelText, FontSize = 20,
                FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            Grid.SetColumn(heading, 1);
            header.Children.Add(heading);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (window.ResizeMode != ResizeMode.NoResize)
                actions.Children.Add(GptChatWindow.IconButton("最小化", "\uE921", delegate { window.WindowState = WindowState.Minimized; }, 28));
            if (canResize)
            {
                var zoom = GptChatWindow.IconButton("最大化", "\uE922", delegate { ToggleMaximize(window); }, 28);
                window.StateChanged += delegate {
                    bool full = window.WindowState == WindowState.Maximized;
                    string label = full ? "还原窗口" : "最大化";
                    ((TextBlock)zoom.Content).Text = full ? "\uE923" : "\uE922";
                    zoom.ToolTip = label;
                    AutomationProperties.SetName(zoom, label);
                };
                actions.Children.Add(zoom);
            }
            actions.Children.Add(GptChatWindow.IconButton("关闭面板", "\uE8BB", window.Close, 28));
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);
            return header;
        }

        private static void ToggleMaximize(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // Keep templates scoped to the secondary windows. Local control colors and validation states take precedence.
        private static readonly ResourceDictionary FormResources = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Ink' Color='#1C364C'/>
  <SolidColorBrush x:Key='Line' Color='#C7DCEA'/>
  <SolidColorBrush x:Key='Focus' Color='#296B9E'/>
  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource NightInk}'/><Setter Property='Background' Value='{DynamicResource NightRail}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource NightLine}'/><Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='11,6'/><Setter Property='MinHeight' Value='29'/><Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
              BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' CornerRadius='8'>
        <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                          VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='.85'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource NightAccent}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='.48'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Field' TargetType='Control'>
    <Setter Property='Background' Value='{DynamicResource NightSurface}'/><Setter Property='Foreground' Value='{DynamicResource NightInk}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource NightLine}'/><Setter Property='BorderThickness' Value='1'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='MinHeight' Value='31'/>
    <Setter Property='Padding' Value='8,5'/>
  </Style>
  <ControlTemplate x:Key='TextField' TargetType='Control'>
    <Border x:Name='Frame' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
            BorderThickness='{TemplateBinding BorderThickness}'>
      <ScrollViewer x:Name='PART_ContentHost' Focusable='False' Margin='{TemplateBinding Padding}'
                    VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource NightAccent}'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='.55'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType='TextBox' BasedOn='{StaticResource Field}'>
    <Setter Property='Template' Value='{StaticResource TextField}'/><Setter Property='CaretBrush' Value='{DynamicResource NightInk}'/>
  </Style>
  <Style TargetType='PasswordBox' BasedOn='{StaticResource Field}'>
    <Setter Property='Template' Value='{StaticResource TextField}'/><Setter Property='CaretBrush' Value='{DynamicResource NightInk}'/>
  </Style>
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource NightInk}'/><Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
      <Grid Background='Transparent'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>
        <Border x:Name='Check' Width='16' Height='16' VerticalAlignment='Center' CornerRadius='4'
                Background='White' BorderBrush='#98B8D0' BorderThickness='1'>
          <Grid>
            <Path x:Name='Tick' Data='M 3,7 L 6,10 L 11,4' Stroke='White' StrokeThickness='1.6'
                  StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
            <Border x:Name='Dash' Width='8' Height='2' Background='White' Visibility='Collapsed'/>
          </Grid>
        </Border>
        <ContentPresenter Grid.Column='1' Margin='7,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='Check' Property='Background' Value='{DynamicResource NightAccent}'/>
          <Setter TargetName='Check' Property='BorderBrush' Value='{DynamicResource NightAccent}'/>
          <Setter TargetName='Tick' Property='Visibility' Value='Visible'/>
        </Trigger>
        <Trigger Property='IsChecked' Value='{x:Null}'>
          <Setter TargetName='Check' Property='Background' Value='{DynamicResource NightAccent}'/>
          <Setter TargetName='Dash' Property='Visibility' Value='Visible'/>
        </Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Check' Property='BorderBrush' Value='#12344F'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.55'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource NightInk}'/><Setter Property='Padding' Value='9,5'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
      <Border x:Name='Option' CornerRadius='4' Background='Transparent' Padding='{TemplateBinding Padding}'>
        <ContentPresenter RecognizesAccessKey='True'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsSelected' Value='True'><Setter TargetName='Option' Property='Background' Value='{DynamicResource NightRail}'/></Trigger>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Option' Property='Background' Value='{DynamicResource NightRail}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.55'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBox' BasedOn='{StaticResource Field}'>
    <Style.Triggers>
      <Trigger Property='IsEditable' Value='False'><Setter Property='Template'><Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton x:Name='OpenList' Focusable='False' ClickMode='Press'
                 IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
              <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'/></ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <Border x:Name='Frame' IsHitTestVisible='False' Background='{TemplateBinding Background}'
                    BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'>
              <DockPanel Margin='{TemplateBinding Padding}'>
                <Path DockPanel.Dock='Right' Data='M 0 0 L 4 4 L 8 0' Stroke='{DynamicResource NightQuiet}' StrokeThickness='1.4'
                      StrokeStartLineCap='Round' StrokeEndLineCap='Round' Margin='9,0,2,0' VerticalAlignment='Center'/>
                <ContentPresenter VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}'
                                  ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'/>
              </DockPanel>
            </Border>
            <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
              <Border Background='{DynamicResource NightSurface}' CornerRadius='7' BorderBrush='{DynamicResource NightLine}' BorderThickness='1' Padding='3'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <ScrollViewer CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource NightAccent}'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='.55'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter></Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>");
    }
}
