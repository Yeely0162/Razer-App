using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Threading;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.Mvvm;
using RazerChromaWLEDConnect.Services;
using RazerChromaWLEDConnect.WLED;

namespace RazerChromaWLEDConnect.ViewModels
{
    public enum AppSection
    {
        Overview,
        Devices,
        Mapping,
        Performance,
        Settings
    }

    /// <summary>
    /// Owns the whole studio: the Razer link, the channel palette, the device list, the render
    /// pipeline and the settings. Pages bind to this object and to its two sub view models.
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly AppSettings _settings;
        private readonly IHardwareServices _hardware;
        private readonly IChromaBroadcastLink _link;
        private readonly IDeviceDiscovery _discovery;
        private readonly IUiDispatcher _ui;

        private readonly Dictionary<RGBBase, DeviceViewModel> _deviceLookup = new Dictionary<RGBBase, DeviceViewModel>();
        private readonly object _previewLock = new object();
        private readonly ChannelInfo[] _channelBuffer = new ChannelInfo[ChromaInputManager.MaxChannels];
        private readonly string[] _anchorHex = new string[4];

        private AppSection _section = AppSection.Overview;
        private bool _syncEnabled;
        private bool _dirty;
        private long _lastMetricsTicks;
        private long _lastReconnectTicks;
        private string _channelsSignature = string.Empty;
        private int _paletteHash;
        private int _paletteVersion;
        private int _refreshQueued;

        public MainViewModel(
            AppSettings settings,
            IHardwareServices hardware,
            IChromaBroadcastLink link,
            IDeviceDiscovery discovery,
            FrameSlot slot,
            IUiDispatcher ui)
        {
            this._settings = settings;
            this._hardware = hardware;
            this._link = link;
            this._discovery = discovery;
            this._ui = ui;

            this.Slot = slot ?? new FrameSlot();
            this.Inputs = new ChromaInputManager();
            this.DeviceManager = new DeviceManager();
            this.PerformanceMonitor = new PerformanceMonitor();

            this.Pipeline = new RenderPipeline(this.Slot, this.Inputs, this.DeviceManager, this.PerformanceMonitor);
            this.Pipeline.TargetFps = settings.TargetFps <= 0 ? 60 : settings.TargetFps;
            this.Pipeline.PreviewFps = settings.PreviewFps <= 0 ? 20 : settings.PreviewFps;
            this.Pipeline.PreviewReady += this.PipelinePreviewReady;

            this.Channels = new ObservableCollection<ChannelViewModel>();
            this.DeviceList = new ObservableCollection<DeviceViewModel>();
            this.Discovered = new ObservableCollection<DiscoveredDeviceViewModel>();

            if (discovery != null)
            {
                discovery.CandidateFound += this.DiscoveryCandidateFound;
                discovery.StateChanged += this.DiscoveryStateChanged;
            }

            // Older settings stored 4 channels; the palette now needs at least the five
            // anchors, so normalise the stored value instead of trusting it blindly.
            settings.ChannelCount = ColorInterpolationEngine.NormalizeChannelCount(settings.ChannelCount);
            this.Inputs.SetChannelCount(settings.ChannelCount);
            this.RebuildChannels();

            this.Performance = new PerformanceViewModel(this.PerformanceMonitor, this.Slot, this.Pipeline);
            this.Options = new SettingsViewModel(this, hardware);

            this.NavigateCommand = new RelayCommand(this.Navigate);
            this.AddDeviceCommand = new RelayCommand(this.AddBlankDevice);
            this.RemoveDeviceCommand = new RelayCommand(this.RemoveDevice);
            this.RefreshDevicesCommand = new RelayCommand(this.RefreshDevices);
            this.ToggleSyncCommand = new RelayCommand(this.ToggleSync);
            this.StartDiscoveryCommand = new RelayCommand(this.StartDiscovery);
            this.StopDiscoveryCommand = new RelayCommand(this.StopDiscovery);
            this.AddManualDeviceCommand = new RelayCommand(this.AddManualDevice);

            if (link != null) link.StateChanged += this.LinkStateChanged;

            this.LoadDevices();
            this._syncEnabled = settings.Sync;
        }

        // ---------------------------------------------------------------- services

        public AppSettings Settings { get { return this._settings; } }

        public ChromaInputManager Inputs { get; private set; }

        public DeviceManager DeviceManager { get; private set; }

        public FrameSlot Slot { get; private set; }

        public PerformanceMonitor PerformanceMonitor { get; private set; }

        public RenderPipeline Pipeline { get; private set; }

        public PerformanceViewModel Performance { get; private set; }

        public SettingsViewModel Options { get; private set; }

        public ObservableCollection<ChannelViewModel> Channels { get; private set; }

        public ObservableCollection<DeviceViewModel> DeviceList { get; private set; }

        public RelayCommand NavigateCommand { get; private set; }

        public RelayCommand AddDeviceCommand { get; private set; }

        public RelayCommand RemoveDeviceCommand { get; private set; }

        public RelayCommand RefreshDevicesCommand { get; private set; }

        public RelayCommand ToggleSyncCommand { get; private set; }

        public RelayCommand StartDiscoveryCommand { get; private set; }

        public RelayCommand StopDiscoveryCommand { get; private set; }

        public RelayCommand AddManualDeviceCommand { get; private set; }

        /// <summary>Devices that answered mDNS but were not added yet.</summary>
        public ObservableCollection<DiscoveredDeviceViewModel> Discovered { get; private set; }

        public bool HasDiscovered { get { return this.Discovered.Count > 0; } }

        // ---------------------------------------------------------------- navigation

        public AppSection Section
        {
            get { return this._section; }
            set
            {
                if (this._section == value) return;
                this._section = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged("IsOverview");
                this.RaisePropertyChanged("IsDevices");
                this.RaisePropertyChanged("IsMapping");
                this.RaisePropertyChanged("IsPerformance");
                this.RaisePropertyChanged("IsSettings");
                this.RaisePropertyChanged("SectionTitle");
            }
        }

        public bool IsOverview { get { return this._section == AppSection.Overview; } }
        public bool IsDevices { get { return this._section == AppSection.Devices; } }
        public bool IsMapping { get { return this._section == AppSection.Mapping; } }
        public bool IsPerformance { get { return this._section == AppSection.Performance; } }
        public bool IsSettings { get { return this._section == AppSection.Settings; } }

        public string SectionTitle
        {
            get
            {
                switch (this._section)
                {
                    case AppSection.Devices: return "Devices";
                    case AppSection.Mapping: return "Mapping";
                    case AppSection.Performance: return "Performance";
                    case AppSection.Settings: return "Settings";
                    default: return "Overview";
                }
            }
        }

        // ---------------------------------------------------------------- status

        public bool SyncEnabled
        {
            get { return this._syncEnabled; }
            set
            {
                if (this._syncEnabled == value) return;
                this._syncEnabled = value;
                this._settings.Sync = value;
                this.MarkDirty();

                if (value) this.StartSync();
                else this.StopSync();

                this.RaisePropertyChanged();
                this.RaisePropertyChanged("StatusHeadline");
            }
        }

        public string ChromaStatusText
        {
            get { return this._link == null ? "Not connected" : this._link.StatusText; }
        }

        public bool IsLive
        {
            get { return this._link != null && this._link.IsLive; }
        }

        /// <summary>
        /// One sentence telling the user what to do next. The first run almost always lands
        /// on "no app id" or "app id rejected", and a bare status word does not explain that
        /// the next step is on the Settings page.
        /// </summary>
        public string StatusHint
        {
            get
            {
                if (this._link == null) return "Chroma link is not running.";

                switch (this._link.State)
                {
                    case ChromaLinkState.Live:
                        return "Receiving colours from Razer.";
                    case ChromaLinkState.WaitingForSynapse:
                        return "Synapse is connected but not streaming colours yet. Razer only sends colours while a Chroma effect is running.";
                    case ChromaLinkState.NoAppId:
                        return "Add your Razer App ID on the Settings page.";
                    case ChromaLinkState.InvalidAppId:
                        return "That App ID is not a full GUID. Paste the complete one on the Settings page.";
                    case ChromaLinkState.Failed:
                        return "Synapse rejected the connection. Check the App ID and whether it is already in use.";
                    default:
                        return "Sync is paused. Turn it on to start pushing colours.";
                }
            }
        }

        public string ChromaStatusKey
        {
            get
            {
                if (this._link == null) return "off";
                switch (this._link.State)
                {
                    case ChromaLinkState.Live: return "ok";
                    case ChromaLinkState.WaitingForSynapse: return "warn";
                    case ChromaLinkState.NotInitialised: return "off";
                    default: return "err";
                }
            }
        }

        public string StatusHeadline
        {
            get
            {
                if (!this._syncEnabled) return "Paused";
                return this.IsLive ? "Syncing" : "Waiting for Razer";
            }
        }

        // ---------------------------------------------------------------- discovery

        public bool IsDiscovering
        {
            get { return this._discovery != null && this._discovery.IsRunning; }
        }

        public string DiscoveryStatus
        {
            get
            {
                if (this._discovery == null) return "Discovery is unavailable.";
                if (this._discovery.IsRunning) return "Searching the local network for WLED controllers.";
                return this.Discovered.Count == 0
                    ? "Not searching. Start a search, or add an address by hand."
                    : this.Discovered.Count + " device(s) responded. Add the ones you want.";
            }
        }

        private string _newDeviceAddress = string.Empty;

        public string NewDeviceAddress
        {
            get { return this._newDeviceAddress; }
            set
            {
                if (this._newDeviceAddress == value) return;
                this._newDeviceAddress = value;
                this.RaisePropertyChanged();
            }
        }

        private void StartDiscovery()
        {
            if (this._discovery == null) return;
            this._discovery.Start();
            this.RaiseDiscovery();
        }

        private void StopDiscovery()
        {
            if (this._discovery == null) return;
            this._discovery.Stop();
            this.RaiseDiscovery();
        }

        private string _manualError = string.Empty;

        /// <summary>Why the last manual add was refused. Empty when there is nothing to say.</summary>
        public string ManualError
        {
            get { return this._manualError; }
            private set
            {
                if (this._manualError == value) return;
                this._manualError = value;
                this.RaisePropertyChanged();
            }
        }

        private void AddManualDevice()
        {
            string address = (this._newDeviceAddress ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                this.ManualError = "Type an address first, for example 192.168.0.100.";
                return;
            }

            IPAddress parsed;
            if (!IPAddress.TryParse(address, out parsed))
            {
                this.ManualError = "That is not a valid IPv4 or IPv6 address.";
                return;
            }

            DeviceViewModel added = this.AddDeviceByAddress(address);
            if (added == null)
            {
                this.ManualError = "That controller is already in the list.";
                return;
            }

            this.ManualError = string.Empty;
            this.NewDeviceAddress = string.Empty;
        }

        private void DiscoveryCandidateFound(object sender, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;

            if (this._ui != null && !this._ui.IsOnUiThread)
            {
                this._ui.Post(delegate { this.AcceptCandidate(address); });
                return;
            }

            this.AcceptCandidate(address);
        }

        private void AcceptCandidate(string address)
        {
            for (int i = 0; i < this.Discovered.Count; i++)
            {
                if (this.Discovered[i].Address == address) return;
            }

            for (int i = 0; i < this._settings.Instances.Count; i++)
            {
                WLEDModule existing = this._settings.Instances[i] as WLEDModule;
                if (existing != null && existing.WLEDIPAddress == address) return;
            }

            var item = new DiscoveredDeviceViewModel(address, this.AddDiscovered, this.IgnoreDiscovered);
            this.Discovered.Add(item);
            this.RaisePropertyChanged("HasDiscovered");
            this.RaiseDiscovery();
        }

        private void AddDiscovered(DiscoveredDeviceViewModel item)
        {
            if (item == null) return;

            this.AddDeviceByAddress(item.Address);
            this.Discovered.Remove(item);
            this.RaisePropertyChanged("HasDiscovered");
            this.RaiseDiscovery();
        }

        private void IgnoreDiscovered(DiscoveredDeviceViewModel item)
        {
            if (item == null) return;

            this.Discovered.Remove(item);
            this.RaisePropertyChanged("HasDiscovered");
            this.RaiseDiscovery();
        }

        private void DiscoveryStateChanged(object sender, EventArgs e)
        {
            if (this._ui != null && !this._ui.IsOnUiThread)
            {
                this._ui.Post(this.RaiseDiscovery);
                return;
            }

            this.RaiseDiscovery();
        }

        private void RaiseDiscovery()
        {
            this.RaisePropertyChanged("IsDiscovering");
            this.RaisePropertyChanged("DiscoveryStatus");
            this.RaisePropertyChanged("HasDiscovered");
        }

        // ---------------------------------------------------------------- palette

        public int ChannelCount
        {
            get { return this.Inputs.ChannelCount; }
            set
            {
                int normalised = ColorInterpolationEngine.NormalizeChannelCount(value);
                if (this.Inputs.ChannelCount == normalised) return;

                this.Inputs.SetChannelCount(normalised);
                this._settings.ChannelCount = normalised;
                this.MarkDirty();
                this.RebuildChannels();

                this.RaisePropertyChanged();
                this.RaisePropertyChanged("ChannelSummary");
                this.RaisePropertyChanged("VirtualCount");

                for (int i = 0; i < this.DeviceList.Count; i++) this.DeviceList[i].RefreshLiveValues();
            }
        }

        public int[] ChannelCountOptions { get { return ColorInterpolationEngine.SupportedChannelCounts; } }

        /// <summary>Bumped whenever any channel colour changed. Drives the palette bar repaint.</summary>
        public int PaletteVersion
        {
            get { return this._paletteVersion; }
        }

        public int VirtualCount { get { return this.Inputs.ChannelCount - ColorInterpolationEngine.NativeAnchorCount; } }

        public string ChannelSummary
        {
            get { return ColorInterpolationEngine.NativeAnchorCount + " native · " + this.VirtualCount + " virtual"; }
        }

        public string Anchor1Hex { get { return this._anchorHex[0] ?? "#000000"; } }
        public string Anchor2Hex { get { return this._anchorHex[1] ?? "#000000"; } }
        public string Anchor3Hex { get { return this._anchorHex[2] ?? "#000000"; } }
        public string Anchor4Hex { get { return this._anchorHex[3] ?? "#000000"; } }

        // ---------------------------------------------------------------- devices

        public int DeviceCount { get { return this.DeviceList.Count; } }

        public int ConnectedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < this.DeviceList.Count; i++)
                {
                    if (this.DeviceList[i].IsConnected) count++;
                }
                return count;
            }
        }

        public int TotalLedCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < this.DeviceList.Count; i++)
                {
                    if (this.DeviceList[i].Enabled) total += this.DeviceList[i].LedCount;
                }
                return total;
            }
        }

        public string DeviceSummary { get { return this.ConnectedCount + " of " + this.DeviceCount + " online"; } }

        public bool HasNoDevices { get { return this.DeviceList.Count == 0; } }

        private bool HasOfflineDevices()
        {
            for (int i = 0; i < this.DeviceList.Count; i++)
            {
                DeviceViewModel device = this.DeviceList[i];
                if (device.Enabled && !device.IsConnected) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- frame rate

        public int TargetFps
        {
            get { return this.Pipeline.TargetFps; }
            set
            {
                if (value < 1) value = 1;
                if (value > 240) value = 240;
                this.Pipeline.TargetFps = value;
                this._settings.TargetFps = value;
                this.MarkDirty();
                this.RaisePropertyChanged();
            }
        }

        public int PreviewFps
        {
            get { return this.Pipeline.PreviewFps; }
            set
            {
                if (value < 1) value = 1;
                if (value > 60) value = 60;
                this.Pipeline.PreviewFps = value;
                this._settings.PreviewFps = value;
                this.MarkDirty();
                this.RaisePropertyChanged();
            }
        }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>Called once after the window exists.</summary>
        public void Initialize()
        {
            if (!string.IsNullOrWhiteSpace(this._settings.RazerAppId) && this._link != null)
            {
                this._link.Start(this._settings.RazerAppId);
            }

            this.DeviceManager.DeviceRefreshed += this.DeviceRefreshed;
            this.DeviceManager.RefreshAllInBackground();

            if (this._syncEnabled) this.StartSync();
            this.RaiseStatus();
        }

        private void StartSync()
        {
            this.DeviceManager.RefreshAllInBackground();
            this.Pipeline.Start();
        }

        private void StopSync()
        {
            this.Pipeline.Stop();
            this.Slot.Reset();
            this.RaiseStatus();
        }

        private void ToggleSync()
        {
            this.SyncEnabled = !this.SyncEnabled;
        }

        public void ApplyAppId(string appId)
        {
            this._settings.RazerAppId = string.IsNullOrWhiteSpace(appId) ? null : appId.Trim();
            this.MarkDirty();
            this.FlushSettings();

            if (this._link != null)
            {
                this._link.Stop();
                if (!string.IsNullOrWhiteSpace(this._settings.RazerAppId))
                {
                    this._link.Start(this._settings.RazerAppId);
                }
            }

            this.RaiseStatus();
        }

        private void LoadDevices()
        {
            if (this._settings.Instances == null) this._settings.Instances = new List<RGBBase>();

            this.EnsureLenovoInstance();

            for (int i = 0; i < this._settings.Instances.Count; i++)
            {
                this.AttachDevice(this._settings.Instances[i]);
            }
        }

        /// <summary>Keeps the Lenovo keyboard support of the original app.</summary>
        private void EnsureLenovoInstance()
        {
            for (int i = 0; i < this._settings.Instances.Count; i++)
            {
                if (this._settings.Instances[i] != null && this._settings.Instances[i].DeviceKind == "Lenovo") return;
            }

            if (this._hardware == null) return;

            RGBBase keyboard = this._hardware.TryCreateLenovoKeyboard();
            if (keyboard == null) return;

            keyboard.Enabled = true;
            keyboard.Name = "Lenovo keyboard";
            this._settings.Instances.Add(keyboard);
        }

        private DeviceViewModel AttachDevice(RGBBase device)
        {
            if (device == null) return null;

            if (device.LedCount < 1) device.LedCount = 30;

            var viewModel = new DeviceViewModel(
                device,
                delegate { return this.ChannelCount; },
                this.MarkDirty,
                this.OpenDeviceUrl,
                this._ui);

            viewModel.RefreshRequested += this.DeviceRefreshRequested;

            this.DeviceManager.Add(device);
            this.DeviceList.Add(viewModel);

            lock (this._previewLock)
            {
                this._deviceLookup[device] = viewModel;
            }

            this.RaiseDeviceCounts();
            return viewModel;
        }

        public DeviceViewModel AddBlankDevice()
        {
            var device = new WLEDModule();
            device.Name = "WLED " + (this.DeviceCount + 1);
            device.Enabled = true;

            this._settings.Instances.Add(device);
            DeviceViewModel viewModel = this.AttachDevice(device);
            this.MarkDirty();
            this.DeviceManager.RefreshAllInBackground();
            return viewModel;
        }

        /// <summary>Adds a device found by mDNS or typed into the settings page.</summary>
        public DeviceViewModel AddDeviceByAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            for (int i = 0; i < this._settings.Instances.Count; i++)
            {
                WLEDModule existing = this._settings.Instances[i] as WLEDModule;
                if (existing != null && existing.WLEDIPAddress == address) return null;
            }

            var device = new WLEDModule();
            device.WLEDIPAddress = address;
            device.Name = "WLED " + address;
            device.Enabled = true;

            this._settings.Instances.Add(device);
            DeviceViewModel viewModel = this.AttachDevice(device);
            this.MarkDirty();
            this.DeviceManager.RefreshAllInBackground();
            return viewModel;
        }

        private void AddBlankDevice(object parameter)
        {
            this.AddBlankDevice();
        }

        private void RemoveDevice(object parameter)
        {
            DeviceViewModel viewModel = parameter as DeviceViewModel;
            if (viewModel == null) return;

            RGBBase device = viewModel.Device;
            try { device.Unload(); } catch { }

            viewModel.RefreshRequested -= this.DeviceRefreshRequested;
            viewModel.Detach();
            this._settings.Instances.Remove(device);
            this.DeviceManager.Remove(device);
            this.DeviceList.Remove(viewModel);

            lock (this._previewLock)
            {
                this._deviceLookup.Remove(device);
            }

            this.MarkDirty();
            this.RaiseDeviceCounts();
        }

        private void RefreshDevices()
        {
            this.DeviceManager.RefreshAllInBackground();
        }

        private void DeviceRefreshRequested(object sender, EventArgs e)
        {
            this.DeviceManager.RefreshAllInBackground();
        }

        private void OpenDeviceUrl(DeviceViewModel viewModel)
        {
            WLEDModule wled = viewModel.Device as WLEDModule;
            if (wled == null || this._hardware == null) return;

            this._hardware.OpenUrl(wled.BaseUrl);
        }

        private void RaiseDeviceCounts()
        {
            this.RaisePropertyChanged("DeviceCount");
            this.RaisePropertyChanged("ConnectedCount");
            this.RaisePropertyChanged("DeviceSummary");
            this.RaisePropertyChanged("TotalLedCount");
            this.RaisePropertyChanged("HasNoDevices");
        }

        private void DeviceRefreshed(object sender, RGBBase device)
        {
            if (device == null) return;

            DeviceViewModel viewModel = null;
            lock (this._previewLock)
            {
                this._deviceLookup.TryGetValue(device, out viewModel);
            }

            if (viewModel == null) return;
            if (this._ui != null && !this._ui.IsOnUiThread)
            {
                this._ui.Post(viewModel.RefreshLiveValues);
                return;
            }

            viewModel.RefreshLiveValues();
        }

        private void LinkStateChanged(object sender, EventArgs e)
        {
            // Razer raises this on its own thread; touching bound properties from there is
            // exactly the kind of thing that leaves the window showing a stale status.
            if (this._ui != null && !this._ui.IsOnUiThread)
            {
                this._ui.Post(this.RaiseStatus);
                return;
            }

            this.RaiseStatus();
        }

        private void RaiseStatus()
        {
            this.RaisePropertyChanged("ChromaStatusText");
            this.RaisePropertyChanged("ChromaStatusKey");
            this.RaisePropertyChanged("IsLive");
            this.RaisePropertyChanged("StatusHeadline");
            this.RaisePropertyChanged("StatusHint");
        }

        // ---------------------------------------------------------------- refreshes

        /// <summary>Called by the UI timer, roughly four times a second.</summary>
        public void Refresh()
        {
            this.RefreshChannels();

            // Status is cheap and can change at any time (Razer thread, stream timeout).
            this.RaiseStatus();

            long now = Stopwatch.GetTimestamp();
            if (this._lastMetricsTicks == 0) this._lastMetricsTicks = now;

            double elapsed = (now - this._lastMetricsTicks) / (double)Stopwatch.Frequency;
            if (elapsed < 1.0) return;

            this._lastMetricsTicks = now;
            this.Performance.Tick();

            // Reconnect: controllers that dropped off get one quiet retry every 30 seconds.
            if ((now - this._lastReconnectTicks) / (double)Stopwatch.Frequency > 30.0)
            {
                this._lastReconnectTicks = now;
                if (this.HasOfflineDevices()) this.DeviceManager.RefreshAllInBackground();
            }

            for (int i = 0; i < this.DeviceList.Count; i++) this.DeviceList[i].RefreshLiveValues();

            this.RaiseDeviceCounts();
        }

        private void RefreshFromPipeline()
        {
            Interlocked.Exchange(ref this._refreshQueued, 0);
            this.RefreshChannels();
        }

        private void RefreshChannels()
        {
            int count = this.Inputs.CopyChannels(this._channelBuffer);
            if (count <= 0) return;

            string signature = count.ToString();
            if (signature != this._channelsSignature)
            {
                this._channelsSignature = signature;
                this.RebuildChannels();
            }

            for (int i = 0; i < this.Channels.Count && i < count; i++)
            {
                this.Channels[i].Update(this._channelBuffer[i]);
            }

            int hash = count;
            for (int i = 0; i < count; i++)
            {
                Rgb colour = this._channelBuffer[i].Color;
                hash = unchecked((hash * 397) ^ colour.GetHashCode());
            }

            if (hash != this._paletteHash)
            {
                this._paletteHash = hash;
                this._paletteVersion++;
                this.RaisePropertyChanged("PaletteVersion");
            }

            bool anchorsChanged = false;
            for (int i = 0; i < ColorInterpolationEngine.NativeAnchorCount; i++)
            {
                string hex = this._channelBuffer[i].Color.Hex;
                if (this._anchorHex[i] != hex)
                {
                    this._anchorHex[i] = hex;
                    anchorsChanged = true;
                }
            }

            if (anchorsChanged)
            {
                this.RaisePropertyChanged("Anchor1Hex");
                this.RaisePropertyChanged("Anchor2Hex");
                this.RaisePropertyChanged("Anchor3Hex");
                this.RaisePropertyChanged("Anchor4Hex");
            }
        }

        private void RebuildChannels()
        {
            this.Channels.Clear();
            int count = this.Inputs.CopyChannels(this._channelBuffer);

            for (int i = 0; i < count; i++)
            {
                this.Channels.Add(new ChannelViewModel(this._channelBuffer[i]));
            }

            this._channelsSignature = count.ToString();

            // The channel count changed, so the palette layout changed too. Without this the
            // bar kept drawing the previous count's ticks until something else forced a
            // repaint - which is exactly the overlapping numbers seen in practice.
            this._paletteHash = 0;
            this._paletteVersion++;
            this.RaisePropertyChanged("PaletteVersion");
        }

        private void PipelinePreviewReady(object sender, PreviewEventArgs e)
        {
            DevicePreview[] frames = e.Frames;
            if (frames == null) return;

            // The palette follows the preview rate instead of the 4 Hz UI timer, otherwise
            // the colour bar visibly steps while the strips themselves are smooth. The post
            // is coalesced: if the UI is busy, one pending refresh is enough - queueing one
            // per preview frame is what makes the window fall behind and feel sluggish.
            if (this._ui != null && Interlocked.Exchange(ref this._refreshQueued, 1) == 0)
            {
                this._ui.Post(this.RefreshFromPipeline);
            }

            lock (this._previewLock)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    DeviceViewModel viewModel;
                    if (!this._deviceLookup.TryGetValue(frames[i].Device, out viewModel)) continue;
                    viewModel.RaisePreview(frames[i].Leds, frames[i].Count);
                }
            }
        }

        private void Navigate(object parameter)
        {
            string name = parameter as string;
            if (string.IsNullOrEmpty(name)) return;

            switch (name)
            {
                case "Devices": this.Section = AppSection.Devices; break;
                case "Mapping": this.Section = AppSection.Mapping; break;
                case "Performance": this.Section = AppSection.Performance; break;
                case "Settings": this.Section = AppSection.Settings; break;
                default: this.Section = AppSection.Overview; break;
            }
        }

        public void MarkDirty()
        {
            this._dirty = true;
        }

        /// <summary>Writes settings.xml when something changed. Called by the UI timer.</summary>
        public void FlushSettings()
        {
            if (!this._dirty) return;
            this._dirty = false;

            try
            {
                this._settings.ChannelCount = this.ChannelCount;
                this._settings.TargetFps = this.Pipeline.TargetFps;
                this._settings.PreviewFps = this.Pipeline.PreviewFps;
                this._settings.Sync = this._syncEnabled;

                if (this._hardware != null) this._hardware.SaveSettings(this._settings);
            }
            catch
            {
                this._dirty = true;
            }
        }

        public void Dispose()
        {
            this.FlushSettings();

            try { this.Pipeline.Stop(); } catch { }
            try
            {
                if (this._link != null)
                {
                    this._link.StateChanged -= this.LinkStateChanged;
                    this._link.Stop();
                }
            }
            catch { }

            try { this.DeviceManager.UnloadAll(); } catch { }
            if (this._discovery != null) this._discovery.Stop();
        }
    }
}
