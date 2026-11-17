using System;
using RazerChromaWLEDConnect.Mvvm;

namespace RazerChromaWLEDConnect.ViewModels
{
    /// <summary>
    /// A controller found on the network that the user has not accepted yet. Discovery only
    /// shows devices; nothing is added to the device list until Add is pressed.
    /// </summary>
    public sealed class DiscoveredDeviceViewModel : ObservableObject
    {
        public DiscoveredDeviceViewModel(string address, Action<DiscoveredDeviceViewModel> add, Action<DiscoveredDeviceViewModel> ignore)
        {
            this.Address = address;
            this.AddCommand = new RelayCommand((Action)delegate { add(this); });
            this.IgnoreCommand = new RelayCommand((Action)delegate { ignore(this); });
        }

        public string Address { get; private set; }

        public string Name
        {
            get { return "WLED " + this.Address; }
        }

        public RelayCommand AddCommand { get; private set; }

        public RelayCommand IgnoreCommand { get; private set; }
    }
}
