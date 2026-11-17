using RazerChromaWLEDConnect.Base;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>
    /// The few things the UI layer owns because they need Windows or third party hardware
    /// libraries: registry, shell, file dialogs and the Lenovo keyboard.
    /// </summary>
    public interface IHardwareServices
    {
        bool IsStartupRegistered { get; }
        void SetStartup(bool enabled);

        /// <summary>Creates a Lenovo keyboard device, or null when none is attached.</summary>
        RGBBase TryCreateLenovoKeyboard();

        void OpenUrl(string url);

        /// <summary>Persists settings including the device types the UI layer owns.</summary>
        void SaveSettings(AppSettings settings);
    }
}
