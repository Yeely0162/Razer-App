using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.Mvvm;
using RazerChromaWLEDConnect.Services;
using RazerChromaWLEDConnect.WLED;

namespace RazerChromaWLEDConnect.ViewModels
{
    public sealed class LedFrameEventArgs : EventArgs
    {
        public Rgb[] Leds;
        public int Count;
    }

    /// <summary>UI projection of one device. All writes go straight to the model.</summary>
    public sealed class DeviceViewModel : ObservableObject
    {
        private readonly RGBBase _device;
        private readonly Func<int> _channelCount;
        private readonly Action _settingsChanged;
        private readonly Action<DeviceViewModel> _openUrl;
        private readonly IUiDispatcher _ui;

        public DeviceViewModel(RGBBase device, Func<int> channelCount, Action settingsChanged, Action<DeviceViewModel> openUrl, IUiDispatcher ui)
        {
            this._ui = ui;
            this._device = device;
            this._channelCount = channelCount;
            this._settingsChanged = settingsChanged;
            this._openUrl = openUrl;

            device.PropertyChanged += this.DevicePropertyChanged;

            this.OpenCommand = new RelayCommand(this.OpenInBrowser);
            this.UseAllLedsCommand = new RelayCommand(this.UseAllLeds);
            this.UseAllChannelsCommand = new RelayCommand(this.UseAllChannels);

            this.Segments = new ObservableCollection<SegmentViewModel>();
            this.RebuildSegments();
        }

        public ObservableCollection<SegmentViewModel> Segments { get; private set; }

        private void RebuildSegments()
        {
            this.Segments.Clear();

            WLEDModule wled = this._device as WLEDModule;
            if (wled == null || wled.Segments == null) return;

            for (int i = 0; i < wled.Segments.Count; i++)
            {
                this.Segments.Add(new SegmentViewModel(wled.Segments[i], i, delegate { return this.ChannelCount; }, this._settingsChanged));
            }
        }

        public RelayCommand OpenCommand { get; private set; }

        /// <summary>Resets the physical window to the whole strip.</summary>
        public RelayCommand UseAllLedsCommand { get; private set; }

        /// <summary>Resets the mapping to the whole palette.</summary>
        public RelayCommand UseAllChannelsCommand { get; private set; }

        /// <summary>Raised on the render thread with a copy of the LED buffer.</summary>
        public event EventHandler<LedFrameEventArgs> PreviewUpdated;

        /// <summary>Raised when the device was switched back on and should reconnect.</summary>
        public event EventHandler RefreshRequested;

        public RGBBase Device { get { return this._device; } }

        public string Kind { get { return this._device.DeviceKind; } }

        public string Name
        {
            get { return string.IsNullOrEmpty(this._device.Name) ? this._device.DeviceKind : this._device.Name; }
            set
            {
                if (this._device.Name == value) return;
                this._device.Name = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("DisplayName");
                this._settingsChanged();
            }
        }

        public string DisplayName { get { return this.Name; } }

        public string AddressText
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                if (wled == null) return "HID keyboard";
                return wled.WLEDIPAddress + ":" + wled.WLEDPort;
            }
        }

        public string MacAddress
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                return wled == null ? string.Empty : wled.MacAddress;
            }
        }

        public bool CanOpenInBrowser { get { return this._device is WLEDModule; } }

        /// <summary>Unique per device so the two mode radio buttons group correctly.</summary>
        public string GroupName
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                if (wled != null && !string.IsNullOrEmpty(wled.GroupName)) return wled.GroupName;
                return this._device.GetHashCode().ToString();
            }
        }

        public bool Enabled
        {
            get { return this._device.Enabled; }
            set
            {
                if (this._device.Enabled == value) return;
                this._device.Enabled = value;
                if (!value)
                {
                    this._device.Unload();
                    this._device.IsConnected = false;
                }
                else
                {
                    EventHandler handler = this.RefreshRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                }

                this.RaisePropertyChanged();
                this.RaiseStatus();
                this._settingsChanged();
            }
        }

        public bool IsConnected { get { return this._device.IsConnected; } }

        public int LedCount { get { return this._device.LedCount; } }

        public double Brightness
        {
            get { return this._device.Brightness; }
            set
            {
                if (Math.Abs(this._device.Brightness - value) < 0.5) return;
                this._device.Brightness = value;
                this.RaisePropertyChanged();
                this._settingsChanged();
            }
        }

        public double BrightnessMax { get { return this._device.BrightnessMax; } }

        public int LedStart
        {
            get { return this._device.EffectiveLedStart; }
            set
            {
                int clamped = Math.Max(0, Math.Min(value, this._device.LedCount - 1));
                if (this._device.LedStart == clamped) return;
                this._device.LedStart = clamped;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("LedRangeText");
                this.RaisePropertyChanged("MappingSummary");
                this._settingsChanged();
            }
        }

        public int LedEnd
        {
            get { return this._device.EffectiveLedEnd; }
            set
            {
                int clamped = Math.Max(this._device.EffectiveLedStart + 1, Math.Min(value, this._device.LedCount));
                if (this._device.EffectiveLedEnd == clamped) return;
                this._device.LedEnd = clamped;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("LedRangeText");
                this.RaisePropertyChanged("MappingSummary");
                this._settingsChanged();
            }
        }

        public int ChannelCount { get { return this._channelCount(); } }

        public int ChannelStart
        {
            get
            {
                int first;
                int last;
                this._device.ResolveChannelRange(this.ChannelCount, out first, out last);
                return first + 1;
            }
            set
            {
                int clamped = Math.Max(1, Math.Min(value, this.ChannelCount));
                this._device.ChannelStart = clamped - 1;
                if (this._device.ChannelEnd >= 0 && this._device.ChannelEnd < this._device.ChannelStart)
                {
                    this._device.ChannelEnd = this._device.ChannelStart;
                }
                this.RaiseChannelProperties();
                this._settingsChanged();
            }
        }

        public int ChannelEnd
        {
            get
            {
                int first;
                int last;
                this._device.ResolveChannelRange(this.ChannelCount, out first, out last);
                return last + 1;
            }
            set
            {
                int clamped = Math.Max(1, Math.Min(value, this.ChannelCount));
                this._device.ChannelEnd = clamped - 1;
                if (this._device.ChannelStart < 0) this._device.ChannelStart = 0;
                if (this._device.ChannelEnd < this._device.ChannelStart)
                {
                    this._device.ChannelStart = this._device.ChannelEnd;
                }
                this.RaiseChannelProperties();
                this._settingsChanged();
            }
        }

        public string ChannelRangeText { get { return "C" + this.ChannelStart + "..C" + this.ChannelEnd; } }

        /// <summary>One line summary used in card headers.</summary>
        public string MappingSummary
        {
            get { return this.LedRangeText + " LEDs · " + this.ChannelRangeText; }
        }

        public string LedRangeText
        {
            get { return this.LedStart + "-" + (this.LedEnd - 1); }
        }

        private void UseAllLeds()
        {
            this._device.LedStart = 0;
            this._device.LedEnd = -1;
            this.RaisePropertyChanged("LedStart");
            this.RaisePropertyChanged("LedEnd");
            this.RaisePropertyChanged("LedRangeText");
            this.RaisePropertyChanged("MappingSummary");
            this._settingsChanged();
        }

        private void UseAllChannels()
        {
            this._device.ChannelStart = 0;
            this._device.ChannelEnd = this.ChannelCount - 1;
            this.RaiseChannelProperties();
            this.RaisePropertyChanged("MappingSummary");
            this._settingsChanged();
        }

        public bool Gradient
        {
            get { return this._device.Gradient; }
            set
            {
                if (this._device.Gradient == value) return;
                this._device.Gradient = value;
                this.RaisePropertyChanged();
                this._settingsChanged();
            }
        }

        public bool Reverse
        {
            get { return this._device.Reverse; }
            set
            {
                if (this._device.Reverse == value) return;
                this._device.Reverse = value;
                this.RaisePropertyChanged();
                this._settingsChanged();
            }
        }

        public bool WholeStrip
        {
            get { return this._device.ColorTypeStrip; }
            set
            {
                if (!value) return;
                this._device.ColorTypeStrip = true;
                this._device.ColorTypeSegment = false;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("PerSegment");
                this._settingsChanged();
            }
        }

        public bool PerSegment
        {
            get { return this._device.ColorTypeSegment; }
            set
            {
                if (!value) return;
                this._device.ColorTypeStrip = false;
                this._device.ColorTypeSegment = true;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("WholeStrip");
                this._settingsChanged();
            }
        }

        public bool HasSegments
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                return wled != null && wled.HasSegments;
            }
        }

        public string StatusText
        {
            get
            {
                if (!this._device.Enabled) return "Disabled";
                return this._device.IsConnected ? "Live" : "Offline";
            }
        }

        public string StatusKey
        {
            get
            {
                if (!this._device.Enabled) return "off";
                return this._device.IsConnected ? "ok" : "warn";
            }
        }

        public long PacketsSent
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                return wled == null ? 0 : wled.PacketsSent;
            }
        }

        public double LatencyMicroseconds
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                return wled == null ? 0 : wled.LastSendMicroseconds;
            }
        }

        public string LastError
        {
            get
            {
                WLEDModule wled = this._device as WLEDModule;
                return wled == null ? string.Empty : wled.LastError;
            }
        }

        public void OpenInBrowser()
        {
            if (this._openUrl != null) this._openUrl(this);
        }

        public void RaisePreview(Rgb[] leds, int count)
        {
            EventHandler<LedFrameEventArgs> handler = this.PreviewUpdated;
            if (handler != null) handler(this, new LedFrameEventArgs { Leds = leds, Count = count });
        }

        /// <summary>Called by the one second UI refresh.</summary>
        public void RefreshLiveValues()
        {
            this.RaisePropertyChanged("PacketsSent");
            this.RaisePropertyChanged("LatencyMicroseconds");
            this.RaisePropertyChanged("LastError");
            this.RaisePropertyChanged("IsConnected");
            this.RaisePropertyChanged("LedCount");
            this.RaisePropertyChanged("MappingSummary");
            this.RaisePropertyChanged("ChannelRangeText");
            this.RaiseStatus();
        }

        public void Detach()
        {
            this._device.PropertyChanged -= this.DevicePropertyChanged;
        }

        private void DevicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Device.Load runs on the thread pool. Rebuilding the segment collection from
            // there is what used to crash the window while a device was refreshing.
            if (this._ui != null && !this._ui.IsOnUiThread)
            {
                this._ui.Post(delegate { this.DevicePropertyChanged(sender, e); });
                return;
            }

            if (e.PropertyName == "IsConnected" || e.PropertyName == "LedCount")
            {
                this.RaiseStatus();
                this.RaisePropertyChanged("IsConnected");
                this.RaisePropertyChanged("LedCount");
            }
            else if (e.PropertyName == "Name")
            {
                this.RaisePropertyChanged("Name");
                this.RaisePropertyChanged("DisplayName");
            }
            else if (e.PropertyName == "Segments")
            {
                this.RaisePropertyChanged("HasSegments");
                this.RebuildSegments();
                this.RaisePropertyChanged("HasSegments");
            }
        }

        private void RaiseStatus()
        {
            this.RaisePropertyChanged("StatusText");
            this.RaisePropertyChanged("StatusKey");
        }

        private void RaiseChannelProperties()
        {
            this.RaisePropertyChanged("ChannelStart");
            this.RaisePropertyChanged("ChannelEnd");
            this.RaisePropertyChanged("ChannelRangeText");
            this.RaisePropertyChanged("MappingSummary");
        }
    }
}

namespace RazerChromaWLEDConnect.ViewModels
{
    /// <summary>One WLED segment in the mapping editor.</summary>
    public sealed class SegmentViewModel : ObservableObject
    {
        private readonly WLEDSegment _segment;
        private readonly int _index;
        private readonly Func<int> _channelCount;
        private readonly Action _settingsChanged;

        public SegmentViewModel(WLEDSegment segment, int index, Func<int> channelCount, Action settingsChanged)
        {
            this._segment = segment;
            this._index = index;
            this._channelCount = channelCount;
            this._settingsChanged = settingsChanged;
        }

        public string Title
        {
            get { return "Segment " + (this._index + 1) + " · LED " + this._segment.AbsoluteStart + "–" + (this._segment.AbsoluteStop - 1); }
        }

        public string LengthText
        {
            get { return (this._segment.AbsoluteStop - this._segment.AbsoluteStart) + " LEDs"; }
        }

        public int ChannelStart
        {
            get
            {
                int first;
                int last;
                this._segment.ResolveChannelRange(this._channelCount(), out first, out last);
                return first + 1;
            }
            set
            {
                int clamped = System.Math.Max(1, System.Math.Min(value, this._channelCount()));
                this._segment.ChannelStart = clamped - 1;
                if (this._segment.ChannelEnd < 0) this._segment.ChannelEnd = this._channelCount() - 1;
                if (this._segment.ChannelEnd < this._segment.ChannelStart) this._segment.ChannelEnd = this._segment.ChannelStart;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged("ChannelEnd");
                this._settingsChanged();
            }
        }

        public int ChannelEnd
        {
            get
            {
                int first;
                int last;
                this._segment.ResolveChannelRange(this._channelCount(), out first, out last);
                return last + 1;
            }
            set
            {
                int clamped = System.Math.Max(1, System.Math.Min(value, this._channelCount()));
                this._segment.ChannelEnd = clamped - 1;
                if (this._segment.ChannelStart < 0) this._segment.ChannelStart = 0;
                if (this._segment.ChannelStart > this._segment.ChannelEnd) this._segment.ChannelStart = this._segment.ChannelEnd;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged("ChannelStart");
                this._settingsChanged();
            }
        }

        public bool Gradient
        {
            get { return this._segment.Gradient; }
            set
            {
                if (this._segment.Gradient == value) return;
                this._segment.Gradient = value;
                this.RaisePropertyChanged();
                this._settingsChanged();
            }
        }

        public bool Reverse
        {
            get { return this._segment.Reverse; }
            set
            {
                if (this._segment.Reverse == value) return;
                this._segment.Reverse = value;
                this.RaisePropertyChanged();
                this._settingsChanged();
            }
        }
    }
}
