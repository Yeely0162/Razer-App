using System;
using System.Management;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>
    /// Turns the strips off when Windows suspends and back on when it resumes, like the
    /// original app did. WMI events arrive on their own thread, so raise them as they are.
    /// </summary>
    public sealed class PowerEventWatcher : IDisposable
    {
        private ManagementEventWatcher _watcher;

        public event EventHandler Suspending;
        public event EventHandler Resumed;

        public void Start()
        {
            if (this._watcher != null) return;

            try
            {
                var query = new WqlEventQuery("Win32_PowerManagementEvent");
                this._watcher = new ManagementEventWatcher(new ManagementScope("root\\CIMV2"), query);
                this._watcher.EventArrived += this.OnEventArrived;
                this._watcher.Start();
            }
            catch
            {
                this._watcher = null;
            }
        }

        private void OnEventArrived(object sender, EventArrivedEventArgs e)
        {
            if (e == null || e.NewEvent == null) return;

            try
            {
                object value = e.NewEvent.Properties["EventType"].Value;
                if (value == null) return;

                string type = value.ToString();

                if (type == "4")
                {
                    EventHandler handler = this.Suspending;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
                else if (type == "7" || type == "18")
                {
                    EventHandler handler = this.Resumed;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (this._watcher == null) return;

            try
            {
                this._watcher.EventArrived -= this.OnEventArrived;
                this._watcher.Stop();
                this._watcher.Dispose();
            }
            catch
            {
            }

            this._watcher = null;
        }
    }
}
