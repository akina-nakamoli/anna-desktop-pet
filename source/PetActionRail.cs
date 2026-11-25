using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AnnaDesktopPet
{
    internal sealed partial class PetWindow
    {
        private readonly StackPanel actionRail = new StackPanel();
        private Button railMicrophone;
        private bool railRecording;
        private static readonly Brush RailInk = new SolidColorBrush(Color.FromRgb(51, 94, 122));
        private static readonly Brush RailPaper = new SolidColorBrush(Color.FromArgb(246, 241, 249, 254));
        private static readonly Brush RailLine = new SolidColorBrush(Color.FromRgb(171, 203, 221));
        private static readonly Brush RailActive = new SolidColorBrush(Color.FromRgb(45, 108, 156));

        private void AddActionRail(Grid root)
        {
            actionRail.VerticalAlignment = VerticalAlignment.Top;
            actionRail.HorizontalAlignment = HorizontalAlignment.Left;
            actionRail.UseLayoutRounding = true;
            Panel.SetZIndex(actionRail, 4);
            root.Children.Add(actionRail);
            ControlTemplate template = RailTemplate();
            AddRailButton(template, "聊天", "M20 11c0 4-3.6 7-8.2 7-1.1 0-2.3-.2-3.3-.5L4 19l1.2-3.9A6.3 6.3 0 0 1 3.8 11c0-3.9 3.5-7 8-7s8.2 3.1 8.2 7z M8 10h8 M8 13h5",
                delegate { ShowCompanionTab("chat"); });
            railMicrophone = AddRailButton(template, "开始语音输入", "M9 6a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0z M5.5 10.5v.5a6.5 6.5 0 0 0 13 0v-.5 M12 17.5V21 M9 21h6",
                delegate { if (voiceChat != null) voiceChat.ToggleButtonRecording(); });
            AddRailButton(template, "语音通话", "M7.4 3.4l3 4.4-2.2 2.4c1.4 2.6 3.2 4.4 5.8 5.7l2.3-2.2 4.1 2.6c.7.6.6 1.3.2 2.1-.8 1.9-2.5 2.7-4.4 2.2C9.7 19 4.4 13.1 3.2 7.3c-.4-1.9.6-3.3 2.3-4.1.8-.3 1.4-.4 1.9.2z",
                OpenCall);
            AddRailButton(template, "一起听", "M9 17.6V6.7l10-2.2v11.2 M9 9.5l10-2.2 M9 17.5c0 1.4-1.3 2.5-3.1 2.5S3 19.2 3 18s1.3-2.4 3-2.4c1.6 0 3 .8 3 1.9z M19 15.5c0 1.4-1.3 2.5-3.1 2.5S13 17.2 13 16s1.3-2.4 3-2.4c1.6 0 3 .8 3 1.9z",
                delegate { ShowCompanionTab("music"); });
            AddRailButton(template, "更多操作", "M5 12h.5 M11.75 12h.5 M18.5 12h.5", OpenQuickMenu);
            SizeChanged += delegate { PlaceActionRail(); };
            LocationChanged += delegate { PlaceActionRail(); };
            Loaded += delegate { PlaceActionRail(); };
        }

        private static ControlTemplate RailTemplate()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            var disc = new FrameworkElementFactory(typeof(Ellipse));
            disc.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            disc.SetValue(Shape.StrokeProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            disc.SetValue(Shape.StrokeThicknessProperty, 1.0);
            grid.AppendChild(disc);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = grid };
            foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
            {
                var trigger = new Trigger { Property = property, Value = true };
                trigger.Setters.Add(new Setter(Control.BorderBrushProperty, RailActive));
                trigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
                template.Triggers.Add(trigger);
            }
            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(199, 226, 245))));
            template.Triggers.Add(pressedTrigger);
            return template;
        }

        private Button AddRailButton(ControlTemplate template, string label, string path, Action action)
        {
            var glyph = new Path { Data = Geometry.Parse(path), Stroke = RailInk, StrokeThickness = label == "更多操作" ? 3.3 : 1.8,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var tile = new Canvas { Width = 24, Height = 24 };
            tile.Children.Add(glyph);
            var icon = new Viewbox { Width = 16, Height = 16, Child = tile, IsHitTestVisible = false };
            var button = new Button { Template = template, Content = icon, Cursor = Cursors.Hand, Width = 29, Height = 29,
                Margin = new Thickness(0, 0, 0, 4), FocusVisualStyle = null };
            var hint = new ToolTip { Content = label, PlacementTarget = button };
            button.ToolTip = hint;
            button.IsKeyboardFocusedChanged += delegate
            {
                if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
                    hint.IsOpen = button.IsKeyboardFocused;
                else if (!button.IsKeyboardFocused && !button.IsMouseOver)
                    hint.IsOpen = false;
            };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, RailPaper));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, RailLine));
            button.Style = style;
            AutomationProperties.SetName(button, label);
            ToolTipService.SetInitialShowDelay(button, 250);
            ToolTipService.SetPlacement(button, PlacementMode.Left);
            button.Click += delegate
            {
                if (closing || hiddenForGame) return;
                clickTimer.Stop();
                lastClickTime = -100;
                autonomous.Interrupt(elapsed.Elapsed.TotalSeconds);
                action();
            };
            actionRail.Children.Add(button);
            return button;
        }

        private void SetRailRecording(bool recording)
        {
            if (railMicrophone == null || railRecording == recording) return;
            railRecording = recording;
            string label = recording ? "结束录音并识别" : "开始语音输入";
            ((ToolTip)railMicrophone.ToolTip).Content = label;
            AutomationProperties.SetName(railMicrophone, label);
            if (recording) railMicrophone.Background = new SolidColorBrush(Color.FromRgb(180, 221, 250));
            else railMicrophone.ClearValue(Control.BackgroundProperty);
        }

        private void PlaceActionRail()
        {
            if (actionRail.Children.Count == 0 || ActualWidth < 1 || ActualHeight < 1) return;
            double diameter = Math.Round(Math.Max(26, Math.Min(31, settings.Size * .12)));
            double right = Math.Min(ActualWidth - diameter - 4, ActualWidth / 2 + settings.Size * .27 + 10);
            double left = right;
            if (PresentationSource.FromVisual(this) != null)
            {
                var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                Point limit = PointFromScreen(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
                Point first = PointFromScreen(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                if ((limit.X - ActualWidth < 12 || limit.X - right - diameter < 14)
                    && ActualWidth - right - diameter >= first.X)
                    left = ActualWidth - right - diameter;
            }
            // The visible portrait always starts at y=90, including models with overhead padding.
            double top = Math.Min(90 + settings.Size * .055, ActualHeight - (diameter + 4) * actionRail.Children.Count - 4);
            var margin = new Thickness(Math.Round(left), Math.Max(0, Math.Round(top)), 0, 0);
            if (margin != actionRail.Margin) actionRail.Margin = margin;
            foreach (Button button in actionRail.Children)
            {
                if (button.Width != diameter) button.Width = button.Height = diameter;
                var side = left == right ? PlacementMode.Right : PlacementMode.Left;
                if (ToolTipService.GetPlacement(button) != side) ToolTipService.SetPlacement(button, side);
            }
        }

        private bool HitActionRail(Point screen)
        {
            if (hiddenForGame || !actionRail.IsVisible || actionRail.ActualWidth <= 0) return false;
            Point point = actionRail.PointFromScreen(screen);
            if (point.X < 0 || point.X > actionRail.ActualWidth || point.Y < 0 || point.Y > actionRail.ActualHeight) return false;
            foreach (Button button in actionRail.Children)
            {
                Point local = actionRail.TranslatePoint(point, button);
                double radius = button.ActualWidth * .5;
                double x = local.X - radius, y = local.Y - radius;
                if (x*x + y*y <= radius*radius) return true;
            }
            return false;
        }

        private void VerifyActionRailSmoke()
        {
            UpdateLayout();
            if (actionRail.Children.Count != 5 || !actionRail.IsVisible) throw new Exception("Missing pet action rail");
            foreach (Button button in actionRail.Children)
            {
                double r = button.ActualWidth * .5;
                if (r < 12 || !IsOpaqueAt(button.PointToScreen(new Point(r,r)))
                    || HitActionRail(button.PointToScreen(new Point(0,0)))
                    || HitActionRail(button.PointToScreen(new Point(r,button.ActualHeight + 2))))
                    throw new Exception("Circular pet shortcut hit area is incorrect");
            }
            var chatButton = (Button)actionRail.Children[0];
            var musicButton = (Button)actionRail.Children[3];
            chatButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var window = chatWindow;
            if (window == null || !window.IsVisible || window.SelectedCompanionTab != "chat") throw new Exception("Chat rail shortcut failed");
            musicButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (chatWindow != window || !window.IsVisible || window.SelectedCompanionTab != "music") throw new Exception("Music rail shortcut failed");
            window.Close();
        }
    }
}
