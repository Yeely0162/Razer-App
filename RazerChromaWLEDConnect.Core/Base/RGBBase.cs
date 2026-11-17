using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.WLED;

namespace RazerChromaWLEDConnect.Base
{
    /// <summary>
    /// Common state for everything that can receive a colour frame: WLED controllers today,
    /// other RGB sources later. Persisted members keep their original names so existing
    /// settings.xml files keep loading.
    /// </summary>
    [Serializable]
    [XmlInclude(typeof(WLEDModule))]
    [XmlRoot("RGBBase")]
    public abstract class RGBBase : INotifyPropertyChanged
    {
        private int _type;
        private bool _enabled;
        private bool _isConnected;
        private int _ledCount = 30;
        private bool _gradient = true;
        private double _brightness = 255;
        private bool _led1;
        private bool _led2;
        private bool _led3;
        private bool _led4;
        private bool _colorTypeStrip = true;
        private bool _colorTypeSegment;
        private int _ledStart;
        private int _ledEnd = -1;
        private int _channelStart;
        private int _channelEnd = -1;
        private bool _reverse;
        private string _name = string.Empty;

        [XmlIgnore]
        public Rgb[] FrameBuffer { get; protected set; }

        [XmlIgnore]
        public int FrameLength { get; protected set; }

        public virtual int Type
        {
            get { return this._type; }
            set { this._type = value; this.OnPropertyChanged("Type"); }
        }

        public bool Enabled
        {
            get { return this._enabled; }
            set { if (this._enabled == value) return; this._enabled = value; this.OnPropertyChanged("Enabled"); }
        }

        public bool IsConnected
        {
            get { return this._isConnected; }
            set { if (this._isConnected == value) return; this._isConnected = value; this.OnPropertyChanged("IsConnected"); }
        }

        /// <summary>Number of LEDs the device reports.</summary>
        public int LedCount
        {
            get { return this._ledCount; }
            set
            {
                if (value < 1) value = 1;
                if (this._ledCount == value) return;
                this._ledCount = value;
                this.InvalidateBuffer();
                this.OnPropertyChanged("LedCount");
            }
        }

        public bool Gradient
        {
            get { return this._gradient; }
            set { if (this._gradient == value) return; this._gradient = value; this.OnPropertyChanged("Gradient"); }
        }

        public double Brightness
        {
            get { return this._brightness; }
            set { if (this._brightness == value) return; this._brightness = value; this.OnPropertyChanged("Brightness"); }
        }

        // Legacy per colour selection. Only used when no explicit channel range is stored,
        // so old settings.xml files map exactly like they used to.
        public bool Led1 { get { return this._led1; } set { this._led1 = value; this.OnPropertyChanged("Led1"); } }
        public bool Led2 { get { return this._led2; } set { this._led2 = value; this.OnPropertyChanged("Led2"); } }
        public bool Led3 { get { return this._led3; } set { this._led3 = value; this.OnPropertyChanged("Led3"); } }
        public bool Led4 { get { return this._led4; } set { this._led4 = value; this.OnPropertyChanged("Led4"); } }

        public bool ColorTypeStrip
        {
            get { return this._colorTypeStrip; }
            set { if (this._colorTypeStrip == value) return; this._colorTypeStrip = value; this.OnPropertyChanged("ColorTypeStrip"); }
        }

        public bool ColorTypeSegment
        {
            get { return this._colorTypeSegment; }
            set { if (this._colorTypeSegment == value) return; this._colorTypeSegment = value; this.OnPropertyChanged("ColorTypeSegment"); }
        }

        /// <summary>First LED of the physical window this device drives. -1 keeps the whole strip.</summary>
        public int LedStart
        {
            get { return this._ledStart; }
            set { if (this._ledStart == value) return; this._ledStart = value; this.OnPropertyChanged("LedStart"); this.OnPropertyChanged("LedRangeText"); }
        }

        /// <summary>Exclusive end of the physical window. -1 means LedCount.</summary>
        public int LedEnd
        {
            get { return this._ledEnd; }
            set { if (this._ledEnd == value) return; this._ledEnd = value; this.OnPropertyChanged("LedEnd"); this.OnPropertyChanged("LedRangeText"); }
        }

        /// <summary>First palette channel this device maps. -1 falls back to the legacy Led1..4 flags.</summary>
        public int ChannelStart
        {
            get { return this._channelStart; }
            set { if (this._channelStart == value) return; this._channelStart = value; this.OnPropertyChanged("ChannelStart"); this.OnPropertyChanged("ChannelRangeText"); }
        }

        /// <summary>Last palette channel this device maps. -1 means the last available channel.</summary>
        public int ChannelEnd
        {
            get { return this._channelEnd; }
            set { if (this._channelEnd == value) return; this._channelEnd = value; this.OnPropertyChanged("ChannelEnd"); this.OnPropertyChanged("ChannelRangeText"); }
        }

        /// <summary>Runs the palette from the far end of the strip back.</summary>
        public bool Reverse
        {
            get { return this._reverse; }
            set { if (this._reverse == value) return; this._reverse = value; this.OnPropertyChanged("Reverse"); }
        }

        public string Name
        {
            get { return this._name; }
            set { if (this._name == value) return; this._name = value; this.OnPropertyChanged("Name"); }
        }

        [XmlIgnore]
        public virtual string DeviceKind { get { return "RGB"; } }

        /// <summary>Upper bound of the brightness slider for this device.</summary>
        [XmlIgnore]
        public virtual double BrightnessMax { get { return 255; } }

        [XmlIgnore]
        public string LedRangeText
        {
            get { return this.EffectiveLedStart + "-" + (this.EffectiveLedEnd - 1); }
        }

        [XmlIgnore]
        public string ChannelRangeText
        {
            get
            {
                if (this.ChannelEnd < 0) return "auto";
                return "C" + (this.ChannelStart + 1) + "..C" + (this.ChannelEnd + 1);
            }
        }

        public int EffectiveLedStart
        {
            get
            {
                int start = this._ledStart;
                if (start < 0) start = 0;
                if (start >= this._ledCount) start = this._ledCount - 1;
                return start;
            }
        }

        public int EffectiveLedEnd
        {
            get
            {
                int end = this._ledEnd;
                if (end < 0 || end > this._ledCount) end = this._ledCount;
                if (end <= this.EffectiveLedStart) end = this.EffectiveLedStart + 1;
                return end;
            }
        }

        /// <summary>
        /// Resolves the channel range for the given palette size, honouring the legacy
        /// Led1..Led4 checkboxes when no explicit range was stored.
        /// </summary>
        public void ResolveChannelRange(int channelCount, out int first, out int last)
        {
            first = this._channelStart;
            last = this._channelEnd;

            // The Led1..Led4 flags describe the four original colours. They may only narrow
            // the range while the palette still *is* those four colours; once the user picks
            // 8..32 channels the flags must not silently pin the device to the first four.
            bool legacy = this._channelEnd < 0 && channelCount <= ColorInterpolationEngine.NativeAnchorCount;

            if (legacy)
            {
                bool[] flags = { this._led1, this._led2, this._led3, this._led4 };
                int min = -1;
                int max = -1;
                for (int i = 0; i < flags.Length; i++)
                {
                    if (!flags[i]) continue;
                    if (min < 0) min = i;
                    max = i;
                }

                if (min >= 0)
                {
                    first = min;
                    last = max;
                }
                // No flags set: fall through to the whole palette, which is the sane
                // default for a device added from the new UI.
            }

            if (first < 0) first = 0;
            if (last < 0) last = channelCount - 1;
            if (first > channelCount - 1) first = channelCount - 1;
            if (last > channelCount - 1) last = channelCount - 1;
            if (last < first) last = first;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void InvalidateBuffer()
        {
            this.FrameBuffer = new Rgb[this._ledCount];
            this.FrameLength = this._ledCount;
        }

        /// <summary>Called by the render thread. Must stay allocation free once buffers exist.</summary>
        public void ApplyFrame(Rgb[] channels, int channelCount)
        {
            if (channels == null || channelCount <= 0) return;
            if (this.FrameBuffer == null || this.FrameBuffer.Length != this._ledCount) this.InvalidateBuffer();

            // Clear first: the LED window can move, and gaps between segments must be black.
            LedMappingEngine.Clear(this.FrameBuffer);
            this.RenderIntoBuffer(channels, channelCount);
            this.PushFrame();
        }

        protected virtual void RenderIntoBuffer(Rgb[] channels, int channelCount)
        {
            int first;
            int last;
            this.ResolveChannelRange(channelCount, out first, out last);
            LedMappingEngine.RenderWindow(
                channels, first, last, this._gradient, this._reverse,
                this.FrameBuffer, this.EffectiveLedStart, this.EffectiveLedEnd);
        }

        protected virtual void PushFrame()
        {
        }

        public virtual void TurnOn()
        {
        }

        public virtual void TurnOff()
        {
        }

        public virtual void Load()
        {
        }

        public virtual void Unload()
        {
        }

        public virtual void Reload()
        {
            this.Unload();
            this.Load();
        }
    }
}
