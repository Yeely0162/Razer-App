using System;
using System.Windows;
using System.Windows.Controls;
using RazerChromaWLEDConnect.ViewModels;

namespace RazerChromaWLEDConnect.Controls
{
    /// <summary>
    /// Places one tick per channel at its palette position. Because every tick is a real
    /// child element, WPF removes it as soon as the channel disappears - nothing to clean up.
    /// </summary>
    public sealed class ChannelTickPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double height = 0;

            for (int i = 0; i < this.InternalChildren.Count; i++)
            {
                UIElement child = this.InternalChildren[i];
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (child.DesiredSize.Height > height) height = child.DesiredSize.Height;
            }

            double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = this.InternalChildren.Count;
            if (count == 0) return finalSize;

            for (int i = 0; i < count; i++)
            {
                UIElement child = this.InternalChildren[i];
                var element = child as FrameworkElement;
                var channel = element == null ? null : element.DataContext as ChannelViewModel;

                double position = channel != null
                    ? channel.Position
                    : (count > 1 ? (double)i / (count - 1) : 0);

                double width = child.DesiredSize.Width;
                double height = child.DesiredSize.Height;

                double left = (position * finalSize.Width) - (width / 2);
                if (left < 0) left = 0;
                if (left > finalSize.Width - width) left = Math.Max(0, finalSize.Width - width);

                child.Arrange(new Rect(left, 0, width, height));
            }

            return finalSize;
        }
    }
}
