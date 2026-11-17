using System;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>mDNS discovery of WLED controllers, implemented in the UI layer.</summary>
    public interface IDeviceDiscovery : IDisposable
    {
        bool IsRunning { get; }

        /// <summary>Raised with a candidate address. May come from a background thread.</summary>
        event EventHandler<string> CandidateFound;

        event EventHandler StateChanged;

        void Start();

        void Stop();
    }
}
