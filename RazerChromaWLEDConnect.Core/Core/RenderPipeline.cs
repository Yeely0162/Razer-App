using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RazerChromaWLEDConnect.Base;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>Preview data for one device, already copied so the UI can use it safely.</summary>
    public sealed class DevicePreview
    {
        public RGBBase Device;
        public Rgb[] Leds;
        public int Count;
        public long Timestamp;
    }

    public sealed class PreviewEventArgs : EventArgs
    {
        public DevicePreview[] Frames;
    }

    /// <summary>
    /// The only place colour frames are computed and sent. Runs on its own thread, takes the
    /// newest Razer frame, expands it into channels, maps every device and pushes UDP. The UI
    /// gets a throttled copy so preview smoothness never influences the light output.
    /// </summary>
    public sealed class RenderPipeline : IDisposable
    {
        private readonly FrameSlot _slot;
        private readonly ChromaInputManager _inputs;
        private readonly DeviceManager _devices;
        private readonly PerformanceMonitor _performance;

        private readonly Rgb[] _anchors = new Rgb[ColorInterpolationEngine.NativeAnchorCount];
        private readonly Rgb[] _channels = new Rgb[ChromaInputManager.MaxChannels];
        private readonly object _lifecycle = new object();

        private Thread _thread;
        private volatile bool _running;
        private long _lastSequence;
        private long _lastPreviewTicks;
        private int _lastPreviewHash;
        private readonly Dictionary<RGBBase, Rgb[]> _previewBuffers = new Dictionary<RGBBase, Rgb[]>();

        public RenderPipeline(
            FrameSlot slot,
            ChromaInputManager inputs,
            DeviceManager devices,
            PerformanceMonitor performance)
        {
            this._slot = slot;
            this._inputs = inputs;
            this._devices = devices;
            this._performance = performance;
        }

        /// <summary>Frames the render thread aims for. Light output uses this, not the preview.</summary>
        public int TargetFps { get; set; } = 60;

        /// <summary>How often preview copies are handed to the UI.</summary>
        public int PreviewFps { get; set; } = 20;

        public bool IsRunning
        {
            get { return this._running; }
        }

        /// <summary>Raised on the render thread, throttled to <see cref="PreviewFps"/>.</summary>
        public event EventHandler<PreviewEventArgs> PreviewReady;

        public void Start()
        {
            lock (this._lifecycle)
            {
                if (this._running) return;

                this._running = true;
                this._lastSequence = 0;
                this._lastPreviewTicks = 0;

                this._thread = new Thread(this.Loop);
                this._thread.IsBackground = true;
                this._thread.Name = "Chroma render";
                this._thread.Priority = ThreadPriority.AboveNormal;
                this._thread.Start();
            }
        }

        public void Stop()
        {
            Thread thread;

            lock (this._lifecycle)
            {
                if (!this._running && this._thread == null) return;
                this._running = false;
                thread = this._thread;
                this._thread = null;
            }

            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(1500); } catch { }
            }

            // Leaves live mode and releases every UDP socket.
            this._devices.UnloadAll();
        }

        private void Loop()
        {
            long ticksPerFrame = Stopwatch.Frequency / Math.Max(1, this.TargetFps);
            long next = Stopwatch.GetTimestamp();

            while (this._running)
            {
                try
                {
                    this.Tick();
                }
                catch
                {
                    // A single bad device must not kill the frame loop.
                }

                next += ticksPerFrame;
                long now = Stopwatch.GetTimestamp();
                long remaining = next - now;

                if (remaining <= 0)
                {
                    next = now;
                    continue;
                }

                int milliseconds = (int)(remaining * 1000 / Stopwatch.Frequency);
                if (milliseconds > 1) Thread.Sleep(milliseconds - 1);
                else Thread.SpinWait(200);
            }
        }

        private void Tick()
        {
            long sequence;
            long skipped;

            if (this._slot.TryTake(this._lastSequence, this._anchors, out sequence, out skipped))
            {
                this._lastSequence = sequence;
                this._performance.OnChromaFrame();

                if (skipped > 0)
                {
                    this._performance.OnFrameSkipped(skipped);
                }

                this._inputs.Update(this._anchors);
            }

            // Nothing has arrived from Razer yet: leave the strips alone instead of
            // entering live mode with an all black frame.
            if (this._lastSequence == 0) return;

            int channelCount = this._inputs.ChannelCount;
            Rgb[] source = this._inputs.ChannelColors;
            if (source.Length != this._channels.Length)
            {
                Array.Copy(source, this._channels, Math.Min(source.Length, this._channels.Length));
            }
            else
            {
                Array.Copy(source, this._channels, this._channels.Length);
            }

            RGBBase[] devices = this._devices.Snapshot();
            for (int i = 0; i < devices.Length; i++)
            {
                RGBBase device = devices[i];
                if (device == null || !device.Enabled || !device.IsConnected) continue;

                device.ApplyFrame(this._channels, channelCount);

                WLED.WLEDModule wled = device as WLED.WLEDModule;
                if (wled != null)
                {
                    this._performance.OnSend(1, 0, wled.LastSendMicroseconds);
                }
            }

            this._performance.OnRenderFrame();
            this.PublishPreview();
        }

        private void PublishPreview()
        {
            EventHandler<PreviewEventArgs> handler = this.PreviewReady;
            if (handler == null) return;

            int previewFps = this.PreviewFps <= 0 ? 20 : this.PreviewFps;
            long interval = Stopwatch.Frequency / previewFps;
            long now = Stopwatch.GetTimestamp();

            if (this._lastPreviewTicks != 0 && now - this._lastPreviewTicks < interval) return;

            // Only hand the UI a new frame when the colours actually changed. A static scene
            // costs nothing beyond the hash, which is what keeps the window light when Razer
            // is broadcasting a steady colour.
            int hash = this.ComputeChannelHash();
            bool changed = hash != this._lastPreviewHash;
            bool keepAlive = this._lastPreviewTicks == 0
                             || (now - this._lastPreviewTicks) > (Stopwatch.Frequency * 2);

            if (!changed && !keepAlive) return;

            this._lastPreviewHash = hash;
            this._lastPreviewTicks = now;

            RGBBase[] devices = this._devices.Snapshot();
            var frames = new List<DevicePreview>(devices.Length);

            for (int i = 0; i < devices.Length; i++)
            {
                RGBBase device = devices[i];
                if (device == null || device.FrameBuffer == null || device.FrameLength <= 0) continue;

                // Reuse one copy buffer per device: allocating a fresh array 20 times a second
                // for a 2000 LED strip is a steady stream of garbage and the reason the
                // preview stutters.
                Rgb[] copy;
                if (!this._previewBuffers.TryGetValue(device, out copy) || copy.Length != device.FrameLength)
                {
                    copy = new Rgb[device.FrameLength];
                    this._previewBuffers[device] = copy;
                }

                Array.Copy(device.FrameBuffer, copy, device.FrameLength);

                frames.Add(new DevicePreview
                {
                    Device = device,
                    Leds = copy,
                    Count = device.FrameLength,
                    Timestamp = now
                });
            }

            if (this._previewBuffers.Count > frames.Count + 4)
            {
                this.TrimPreviewBuffers(devices);
            }

            handler(this, new PreviewEventArgs { Frames = frames.ToArray() });
        }

        /// <summary>Drops copy buffers that belong to devices no longer in the list.</summary>
        private void TrimPreviewBuffers(RGBBase[] devices)
        {
            var alive = new HashSet<RGBBase>(devices);
            var dead = new List<RGBBase>();

            foreach (RGBBase key in this._previewBuffers.Keys)
            {
                if (!alive.Contains(key)) dead.Add(key);
            }

            for (int i = 0; i < dead.Count; i++) this._previewBuffers.Remove(dead[i]);
        }

        private int ComputeChannelHash()
        {
            int hash = this._channels.Length;
            int count = Math.Min(this._inputs.ChannelCount, this._channels.Length);

            for (int i = 0; i < count; i++)
            {
                Rgb colour = this._channels[i];
                hash = unchecked((hash * 397) ^ colour.GetHashCode());
            }

            return hash;
        }

        public void Dispose()
        {
            this.Stop();
            this._previewBuffers.Clear();
        }
    }
}
