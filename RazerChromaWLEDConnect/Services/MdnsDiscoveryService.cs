using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Tmds.MDns;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>
    /// Finds WLED devices with mDNS and only reports the ones that actually answer on /json,
    /// because _hap._tcp also matches HomeKit accessories. Validation runs off the UI thread.
    /// </summary>
    public sealed class MdnsDiscoveryService : IDeviceDiscovery
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };

        private ServiceBrowser _browser;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<string> CandidateFound;
        public event EventHandler StateChanged;

        public bool IsRunning
        {
            get { return this._browser != null; }
        }

        public void Start()
        {
            if (this._browser != null) return;

            lock (this._seen) { this._seen.Clear(); }

            try
            {
                var browser = new ServiceBrowser();
                browser.ServiceAdded += this.OnServiceAdded;
                browser.StartBrowse(new[] { "_wled._tcp", "_hap._tcp" });
                this._browser = browser;
            }
            catch
            {
                this._browser = null;
            }

            this.RaiseStateChanged();
        }

        public void Stop()
        {
            ServiceBrowser browser = this._browser;
            this._browser = null;

            if (browser != null)
            {
                try
                {
                    browser.ServiceAdded -= this.OnServiceAdded;
                    browser.StopBrowse();
                }
                catch
                {
                }
            }

            this.RaiseStateChanged();
        }

        private void OnServiceAdded(object sender, ServiceAnnouncementEventArgs e)
        {
            if (e == null || e.Announcement == null) return;
            if (e.Announcement.Addresses == null || e.Announcement.Addresses.Count == 0) return;

            string address = e.Announcement.Addresses[0].ToString();

            // _hap._tcp and _wled._tcp both fire, and both fire repeatedly. Validate each
            // address once per search instead of hammering every device with HTTP.
            lock (this._seen)
            {
                if (!this._seen.Add(address)) return;
            }

            Task.Run(() => this.ValidateAsync(address));
        }

        private async Task ValidateAsync(string address)
        {
            try
            {
                string body = await Http.GetStringAsync("http://" + address + "/json").ConfigureAwait(false);
                if (string.IsNullOrEmpty(body) || body.IndexOf("\"info\"", StringComparison.Ordinal) < 0) return;

                EventHandler<string> handler = this.CandidateFound;
                if (handler != null) handler(this, address);
            }
            catch
            {
            }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = this.StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            this.Stop();
        }
    }
}
