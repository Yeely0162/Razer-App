using System.ComponentModel;
using System.Xml.Serialization;

namespace RazerChromaWLEDConnect.WLED
{
    /// <summary>
    /// A WLED segment as reported by the JSON API. start/stop/len/rev are the original field
    /// names so they keep deserialising from existing settings files.
    /// </summary>
    public class WLEDSegment : INotifyPropertyChanged
    {
        public int start;
        public int stop;
        public int len;
        public bool rev;

        private bool _color1;
        private bool _color2;
        private bool _color3;
        private bool _color4;
        private bool _gradient;
        private int _channelStart = 0;
        private int _channelEnd = -1;

        public bool Color1 { get { return this._color1; } set { this._color1 = value; this.OnPropertyChanged("Color1"); } }
        public bool Color2 { get { return this._color2; } set { this._color2 = value; this.OnPropertyChanged("Color2"); } }
        public bool Color3 { get { return this._color3; } set { this._color3 = value; this.OnPropertyChanged("Color3"); } }
        public bool Color4 { get { return this._color4; } set { this._color4 = value; this.OnPropertyChanged("Color4"); } }

        public bool Gradient { get { return this._gradient; } set { this._gradient = value; this.OnPropertyChanged("Gradient"); } }

        /// <summary>First palette channel used by this segment.</summary>
        public int ChannelStart
        {
            get { return this._channelStart; }
            set { this._channelStart = value; this.OnPropertyChanged("ChannelStart"); }
        }

        /// <summary>Last palette channel used by this segment. -1 falls back to Color1..4.</summary>
        public int ChannelEnd
        {
            get { return this._channelEnd; }
            set { this._channelEnd = value; this.OnPropertyChanged("ChannelEnd"); }
        }

        [XmlIgnore]
        public bool Reverse
        {
            get { return this.rev; }
            set { if (this.rev == value) return; this.rev = value; this.OnPropertyChanged("Reverse"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Absolute LED range of the segment, clamped into the strip.</summary>
        public int AbsoluteStart
        {
            get { return this.start < 0 ? 0 : this.start; }
        }

        public int AbsoluteStop
        {
            get
            {
                int stopValue = this.stop > 0 ? this.stop : this.start + this.len;
                if (stopValue <= this.AbsoluteStart) stopValue = this.AbsoluteStart + 1;
                return stopValue;
            }
        }

        /// <summary>Channel range for this segment, honouring the legacy colour flags.</summary>
        public void ResolveChannelRange(int channelCount, out int first, out int last)
        {
            first = this._channelStart;
            last = this._channelEnd;

            // Same rule as the device: the four legacy colour flags only make sense while the
            // palette itself has four channels.
            if (last < 0 && channelCount <= Core.ColorInterpolationEngine.NativeAnchorCount)
            {
                bool[] flags = { this._color1, this._color2, this._color3, this._color4 };
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
                else
                {
                    first = 0;
                    last = -1;
                }
            }

            if (first < 0) first = 0;
            if (last < 0) last = channelCount - 1;
            if (first > channelCount - 1) first = channelCount - 1;
            if (last > channelCount - 1) last = channelCount - 1;
            if (last < first) last = first;
        }
    }
}
