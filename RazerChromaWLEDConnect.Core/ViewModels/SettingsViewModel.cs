using System;
using System.Collections.ObjectModel;
using RazerChromaWLEDConnect.Mvvm;
using RazerChromaWLEDConnect.Services;

namespace RazerChromaWLEDConnect.ViewModels
{
    /// <summary>One entry of the channel count segmented control.</summary>
    public sealed class ChannelOptionViewModel : ObservableObject
    {
        private bool _selected;

        public ChannelOptionViewModel(int value, Action<int> select)
        {
            this.Value = value;
            this.SelectCommand = new RelayCommand((System.Action)delegate { select(value); });
        }

        public int Value { get; private set; }

        public string Label { get { return this.Value.ToString(); } }

        public bool IsSelected
        {
            get { return this._selected; }
            set { this.SetProperty(ref this._selected, value); }
        }

        public RelayCommand SelectCommand { get; private set; }
    }

    /// <summary>Settings page: Razer app id, channel count, frame rates, boot and discovery.</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly IHardwareServices _hardware;

        private string _razerAppId;
        public SettingsViewModel(MainViewModel main, IHardwareServices hardware)
        {
            this._main = main;
            this._hardware = hardware;

            this._razerAppId = main.Settings.RazerAppId ?? string.Empty;

            this.ApplyAppIdCommand = new RelayCommand(this.ApplyAppId);
            this.AboutCommand = new RelayCommand(this.About);
            this.ResetStatisticsCommand = new RelayCommand(this._main.Performance.ResetSkipped);

            this.ChannelOptions = new ObservableCollection<ChannelOptionViewModel>();
            this.BuildChannelOptions();
        }

        public ObservableCollection<ChannelOptionViewModel> ChannelOptions { get; private set; }

        private void BuildChannelOptions()
        {
            this.ChannelOptions.Clear();
            int[] counts = Core.ColorInterpolationEngine.SupportedChannelCounts;
            for (int i = 0; i < counts.Length; i++)
            {
                this.ChannelOptions.Add(new ChannelOptionViewModel(counts[i], this.SelectChannelCount)
                {
                    IsSelected = counts[i] == this.ChannelCount
                });
            }
        }

        private void SelectChannelCount(int value)
        {
            this.ChannelCount = value;
        }

        public RelayCommand ApplyAppIdCommand { get; private set; }
        public RelayCommand AboutCommand { get; private set; }
        public RelayCommand ResetStatisticsCommand { get; private set; }

        public string RazerAppId
        {
            get { return this._razerAppId; }
            set { this.SetProperty(ref this._razerAppId, value); }
        }

        public bool HasAppId
        {
            get { return !string.IsNullOrWhiteSpace(this._razerAppId); }
        }

        public int ChannelCount
        {
            get { return this._main.ChannelCount; }
            set
            {
                if (this._main.ChannelCount == value) return;
                this._main.ChannelCount = value;
                this.RaisePropertyChanged();

                for (int i = 0; i < this.ChannelOptions.Count; i++)
                {
                    this.ChannelOptions[i].IsSelected = this.ChannelOptions[i].Value == this._main.ChannelCount;
                }
            }
        }

        public int[] ChannelCountOptions
        {
            get { return Core.ColorInterpolationEngine.SupportedChannelCounts; }
        }

        public int TargetFps
        {
            get { return this._main.TargetFps; }
            set
            {
                if (this._main.TargetFps == value) return;
                this._main.TargetFps = value;
                this.RaisePropertyChanged();
            }
        }

        public int PreviewFps
        {
            get { return this._main.PreviewFps; }
            set
            {
                if (this._main.PreviewFps == value) return;
                this._main.PreviewFps = value;
                this.RaisePropertyChanged();
            }
        }

        public int[] FrameRateOptions { get { return new[] { 30, 60, 90, 120, 144 }; } }

        public bool RunAtBoot
        {
            get { return this._main.Settings.RunAtBoot; }
            set
            {
                if (this._main.Settings.RunAtBoot == value) return;
                this._main.Settings.RunAtBoot = value;
                this._hardware.SetStartup(value);
                this._main.MarkDirty();
                this.RaisePropertyChanged();
            }
        }

        public string VersionText { get { return "v0.1.0"; } }

        private void ApplyAppId()
        {
            this._main.ApplyAppId(this._razerAppId);
            this.RaisePropertyChanged("HasAppId");
        }

        private void About()
        {
            this._hardware.OpenUrl("https://github.com/fu-raz/Razer-Chroma-WLED-Connect-App");
        }
    }
}
