using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.ViewModels;

namespace RazerChromaWLEDConnect.Controls
{
    /// <summary>
    /// LED preview drawn as a 1 pixel tall bitmap stretched across the width. The original
    /// control rebuilt a LinearGradientBrush with two GradientStops per LED on every frame,
    /// which is what made big strips stutter; writing pixels is orders of magnitude cheaper
    /// and does not touch the visual tree at all.
    /// </summary>
    public sealed class LedStrip : Image
    {
        public static readonly DependencyProperty DeviceProperty = DependencyProperty.Register(
            "Device",
            typeof(DeviceViewModel),
            typeof(LedStrip),
            new PropertyMetadata(null, OnDeviceChanged));

        private WriteableBitmap _bitmap;
        private byte[] _pixels;
        private int _bitmapCount = -1;
        private int _lastHash;

        private Rgb[] _pending;
        private int _pendingCount;
        private bool _updateQueued;
        private DeviceViewModel _attached;

        public LedStrip()
        {
            this.Stretch = Stretch.Fill;
            this.SnapsToDevicePixels = true;
            this.MinWidth = 40;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

            this.Loaded += this.OnLoaded;
            this.Unloaded += this.OnUnloaded;
        }

        public DeviceViewModel Device
        {
            get { return (DeviceViewModel)this.GetValue(DeviceProperty); }
            set { this.SetValue(DeviceProperty, value); }
        }

        private static void OnDeviceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var strip = d as LedStrip;
            if (strip == null) return;

            strip.Detach();
            if (strip.IsLoaded) strip.Attach(strip.Device);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Attach(this.Device);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Detach();
        }

        private void Attach(DeviceViewModel viewModel)
        {
            if (viewModel == null || ReferenceEquals(this._attached, viewModel)) return;

            this._attached = viewModel;
            viewModel.PreviewUpdated += this.OnPreview;
        }

        private void Detach()
        {
            if (this._attached == null) return;

            this._attached.PreviewUpdated -= this.OnPreview;
            this._attached = null;
        }

        /// <summary>Called on the render thread. Only stores the newest frame and queues one redraw.</summary>
        private void OnPreview(object sender, LedFrameEventArgs e)
        {
            this._pending = e.Leds;
            this._pendingCount = e.Count;

            if (this._updateQueued) return;
            this._updateQueued = true;

            this.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(this.ApplyPending));
        }

        private void ApplyPending()
        {
            this._updateQueued = false;

            Rgb[] leds = this._pending;
            int count = this._pendingCount;
            this._pending = null;

            if (leds == null || count <= 0) return;
            if (!this.IsVisible || this.ActualWidth < 1) return;

            // Same picture as last time: nothing to redraw. The pipeline already avoids
            // publishing these, this is the second line of defence.
            int hash = count;
            for (int i = 0; i < count && i < leds.Length; i++)
            {
                Rgb sample = leds[i];
                hash = unchecked((hash * 31) ^ sample.GetHashCode());
            }

            if (hash == this._lastHash && this._bitmap != null) return;
            this._lastHash = hash;

            if (this._bitmap == null || this._bitmapCount != count)
            {
                this._bitmap = new WriteableBitmap(count, 1, 96, 96, PixelFormats.Bgra32, null);
                this._pixels = new byte[count * 4];
                this._bitmapCount = count;
                this.Source = this._bitmap;
            }

            byte[] pixels = this._pixels;
            int length = Math.Min(count, leds.Length);

            for (int i = 0; i < length; i++)
            {
                Rgb colour = leds[i];
                int offset = i * 4;
                pixels[offset] = colour.B;
                pixels[offset + 1] = colour.G;
                pixels[offset + 2] = colour.R;
                pixels[offset + 3] = 255;
            }

            this._bitmap.WritePixels(new Int32Rect(0, 0, count, 1), pixels, count * 4, 0);
        }
    }
}
