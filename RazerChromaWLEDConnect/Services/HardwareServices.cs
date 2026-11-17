using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using RazerChromaWLEDConnect.Base;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>Registry, shell and the Lenovo keyboard: everything that needs Windows or HID.</summary>
    public sealed class HardwareServices : IHardwareServices
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "RazerChromaWLEDConnect";

        public bool IsStartupRegistered
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        return key != null && key.GetValue(AppName) != null;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;

                    if (enabled)
                    {
                        string executable = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(AppName, "\"" + executable + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
            }
        }

        public RGBBase TryCreateLenovoKeyboard()
        {
            try
            {
                var keyboard = new LenovoKeyboard();
                return keyboard.IsConnected ? keyboard : null;
            }
            catch
            {
                return null;
            }
        }

        public void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null) return;
            settings.Save(new[] { typeof(LenovoKeyboard) });
        }
    }
}
