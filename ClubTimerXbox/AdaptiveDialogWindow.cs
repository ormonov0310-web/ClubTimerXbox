using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubTimerXbox
{
    public abstract class AdaptiveDialogWindow : Window
    {
        protected AdaptiveDialogWindow(double preferredWidth, double minimumWidth)
        {
            Rect workArea = SystemParameters.WorkArea;
            double safeWidth = Math.Max(320, workArea.Width * 0.92);
            double safeHeight = Math.Max(300, workArea.Height * 0.88);
            Width = Math.Min(preferredWidth, safeWidth);
            MinWidth = Math.Min(minimumWidth, Width);
            MaxWidth = safeWidth;
            MinHeight = Math.Min(220, safeHeight);
            MaxHeight = safeHeight;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 28));
            Foreground = Brushes.White;
            UseLayoutRounding = true;
        }

        protected void SetAdaptiveContent(UIElement content)
        {
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(220, MaxHeight - 48),
                CanContentScroll = false
            };
        }
    }
}
