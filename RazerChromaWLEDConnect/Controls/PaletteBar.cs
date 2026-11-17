using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.ViewModels;

namespace RazerChromaWLEDConnect.Controls
{
    /// <summary>
    /// The palette drawn as pixels rather than as a GradientBrush.
    ///
    /// A brush built from N stops kept showing a mixture of the previous frame and the new
    /// one, which is why the bar used to repaint in visible chunks and disagree with the
    /// live swatches. Writing pixels means one frame is computed end to end and appears at
    /// once: it can never be half old and half new. It also interpolates between channels
    /// per pixel, so the ramp is continuous instead of N flat bands.
    /// </summary>
    public sealed class PaletteBar : FrameworkElement
    {
        private const int BitmapWidth = 512;

        private static readonly Brush Surface = Frozen("#0E1013");
        private static readonly Brush ActiveFill = Frozen("#38FFFFFF");
        private static readonly Brush ActiveEdge = Frozen("#44D62C");
        private static readonly Pen BorderPen = FrozenPen(Frozen("#26FFFFFF"), 1.0);

        private readonly Rgb[] _colours = new Rgb[ChromaInputManager.MaxChannels];
        private readonly double[] _positions = new double[ChromaInputManager.MaxChannels];
        private readonly byte[] _pixels = new byte[BitmapWidth * 4];
        private WriteableBitmap _bitmap;
        private INotifyCollectionChanged _collection;

        public static readonly DependencyProperty ChannelsProperty = DependencyProperty.Register(
            "Channels",
            typeof(IEnumerable),
            typeof(PaletteBar),
            new FrameworkPropertyMetadata(null, OnChannelsChanged));

        public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
            "Revision",
            typeof(int),
            typeof(PaletteBar),
            new FrameworkPropertyMetadata(0, OnRevisionChanged));

        public static readonly DependencyProperty ActiveStartProperty = DependencyProperty.Register(
            "ActiveStart",
            typeof(int),
            typeof(PaletteBar),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ActiveEndProperty = DependencyProperty.Register(
            "ActiveEnd",
            typeof(int),
            typeof(PaletteBar),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public PaletteBar()
        {
            this.SnapsToDevicePixels = true;
            this.IsHitTestVisible = false;
        }

        public IEnumerable Channels
        {
            get { return (IEnumerable)this.GetValue(ChannelsProperty); }
            set { this.SetValue(ChannelsProperty, value); }
        }

        public int Revision
        {
            get { return (int)this.GetValue(RevisionProperty); }
            set { this.SetValue(RevisionProperty, value); }
        }

        public int ActiveStart
        {
            get { return (int)this.GetValue(ActiveStartProperty); }
            set { this.SetValue(ActiveStartProperty, value); }
        }

        public int ActiveEnd
        {
            get { return (int)this.GetValue(ActiveEndProperty); }
            set { this.SetValue(ActiveEndProperty, value); }
        }

        private static void OnChannelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var bar = d as PaletteBar;
            if (bar == null) return;

            if (bar._collection != null) bar._collection.CollectionChanged -= bar.OnCollectionChanged;

            bar._collection = e.NewValue as INotifyCollectionChanged;
            if (bar._collection != null) bar._collection.CollectionChanged += bar.OnCollectionChanged;

            bar.InvalidateVisual();
        }

        private static void OnRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var bar = d as PaletteBar;
            if (bar != null) bar.InvalidateVisual();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            this.InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = this.ActualWidth;
            double height = this.ActualHeight;
            if (width < 6 || height < 3) return;

            var area = new Rect(0, 0, width, height);
            double radius = Math.Min(6, height / 2);

            dc.DrawRoundedRectangle(Surface, BorderPen, area, radius, radius);

            int count = this.ReadChannels();
            if (count == 0) return;

            this.RenderGradient(count);

            dc.PushClip(new RectangleGeometry(area, radius, radius));
            dc.DrawImage(this._bitmap, area);

            int start = this.ActiveStart;
            int end = this.ActiveEnd;

            if (start > 0 && end >= start)
            {
                int first = Math.Min(count - 1, start - 1);
                int last = Math.Min(count - 1, end - 1);

                if (first >= 0 && last >= first)
                {
                    double x1 = this._positions[first] * width;
                    double x2 = this._positions[last] * width;
                    double span = Math.Max(2, x2 - x1);

                    dc.DrawRectangle(ActiveFill, null, new Rect(x1, 0, span, height));
                    dc.DrawRectangle(ActiveEdge, null, new Rect(x1, height - 3, span, 3));
                }
            }

            dc.Pop();
        }

        /// <summary>
        /// Fills the pixel strip by interpolating between the two channels that surround each
        /// pixel, so neighbours blend into one continuous ramp.
        /// </summary>
        private void RenderGradient(int count)
        {
            if (this._bitmap == null)
            {
                this._bitmap = new WriteableBitmap(BitmapWidth, 1, 96, 96, PixelFormats.Bgra32, null);
            }

            byte[] pixels = this._pixels;
            int segment = 0;

            for (int x = 0; x < BitmapWidth; x++)
            {
                double position = (double)x / (BitmapWidth - 1);

                while (segment < count - 2 && position > this._positions[segment + 1]) segment++;

                Rgb from = this._colours[segment];
                Rgb to = count > 1 ? this._colours[segment + 1] : from;

                double span = this._positions[segment + 1] - this._positions[segment];
                double t = span > 1e-9 ? (position - this._positions[segment]) / span : 0;
                if (t < 0) t = 0;
                if (t > 1) t = 1;

                int offset = x * 4;
                pixels[offset] = Rgb.Clamp(from.B + (to.B - from.B) * t);
                pixels[offset + 1] = Rgb.Clamp(from.G + (to.G - from.G) * t);
                pixels[offset + 2] = Rgb.Clamp(from.R + (to.R - from.R) * t);
                pixels[offset + 3] = 255;
            }

            this._bitmap.WritePixels(new Int32Rect(0, 0, BitmapWidth, 1), pixels, BitmapWidth * 4, 0);
        }

        private int ReadChannels()
        {
            if (this.Channels == null) return 0;

            int count = 0;
            foreach (object item in this.Channels)
            {
                if (count >= this._colours.Length) break;

                var channel = item as ChannelViewModel;
                if (channel == null) continue;

                this._colours[count] = channel.Color;
                this._positions[count] = channel.Position;
                count++;
            }

            return count;
        }

        private static SolidColorBrush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
