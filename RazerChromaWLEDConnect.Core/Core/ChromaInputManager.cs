using System;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>One palette entry: either a native Razer colour or an interpolated one.</summary>
    public struct ChannelInfo
    {
        public int Index;
        public double Position;

        /// <summary>True when this channel carries a colour Razer actually sent.</summary>
        public bool IsNative;

        public Rgb Color;

        /// <summary>Slot number, 1 based. The mapping sliders use the same numbers.</summary>
        public string Label
        {
            get { return (this.Index + 1).ToString(); }
        }

        public string KindText
        {
            get { return this.IsNative ? "Razer" : "virtual"; }
        }
    }

    /// <summary>
    /// Expands the four Razer anchors into 4 to 32 palette channels. The four anchors are
    /// always present and are flagged as native; every additional channel is interpolated
    /// and flagged as virtual so the UI can never pass generated colours off as real data.
    /// </summary>
    public sealed class ChromaInputManager
    {
        public const int MaxChannels = 32;

        private readonly object _sync = new object();
        private int _channelCount = 4;
        private double[] _positions;
        private readonly Rgb[] _colors = new Rgb[MaxChannels];
        private readonly ChannelInfo[] _channels = new ChannelInfo[MaxChannels];

        public ChromaInputManager()
        {
            this._positions = ColorInterpolationEngine.BuildChannelPositions(this._channelCount);
            this.RebuildChannels();
        }

        public event EventHandler ChannelCountChanged;

        public int ChannelCount
        {
            get { lock (this._sync) return this._channelCount; }
        }

        /// <summary>Returns true when the count actually changed.</summary>
        public bool SetChannelCount(int requested)
        {
            int normalised = ColorInterpolationEngine.NormalizeChannelCount(requested);
            bool changed;

            lock (this._sync)
            {
                changed = normalised != this._channelCount;
                if (changed)
                {
                    this._channelCount = normalised;
                    this._positions = ColorInterpolationEngine.BuildChannelPositions(normalised);
                    this.RebuildChannels();
                }
            }

            if (changed)
            {
                EventHandler handler = this.ChannelCountChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            return changed;
        }

        /// <summary>Recomputes the palette from the four anchors. Called on the render thread.</summary>
        public void Update(Rgb[] anchors)
        {
            if (anchors == null || anchors.Length < ColorInterpolationEngine.NativeAnchorCount) return;

            lock (this._sync)
            {
                for (int i = 0; i < this._channelCount; i++)
                {
                    double position = this._positions[i];
                    Rgb colour = ColorInterpolationEngine.Sample(anchors, position);

                    this._colors[i] = colour;
                    this._channels[i].Color = colour;
                }
            }
        }

        private void RebuildChannels()
        {
            for (int i = 0; i < this._channelCount; i++)
            {
                this._channels[i].Index = i;
                this._channels[i].Position = this._positions[i];
                this._channels[i].IsNative = ColorInterpolationEngine.IsAnchorPosition(this._positions[i]);
                this._channels[i].Color = this._colors[i];
            }
        }

        /// <summary>Copies the current palette for the UI. Called from the UI timer.</summary>
        public int CopyChannels(ChannelInfo[] destination)
        {
            if (destination == null) return 0;

            lock (this._sync)
            {
                // Must be the *selected* channel count, not the buffer length: returning the
                // buffer length made the UI build 32 channel rows no matter what was chosen,
                // and the ones past the count kept their old positions.
                int count = Math.Min(destination.Length, this._channelCount);

                for (int i = 0; i < count; i++) destination[i] = this._channels[i];
                return count;
            }
        }

        /// <summary>The live palette buffer handed to the mapping engine.</summary>
        public Rgb[] ChannelColors
        {
            get { return this._colors; }
        }

        public int NativeCount
        {
            get
            {
                lock (this._sync)
                {
                    int natives = 0;
                    for (int i = 0; i < this._channelCount; i++) if (this._channels[i].IsNative) natives++;
                    return natives;
                }
            }
        }
    }
}
