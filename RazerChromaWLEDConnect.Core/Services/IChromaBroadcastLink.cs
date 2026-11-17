using System;

namespace RazerChromaWLEDConnect.Services
{
    public enum ChromaLinkState
    {
        NotInitialised,
        NoAppId,
        InvalidAppId,
        WaitingForSynapse,
        Live,
        Failed
    }

    /// <summary>
    /// Wraps the Razer Chroma Broadcast SDK. The implementation must never block inside the
    /// Razer callback: it only copies the four colours into the frame slot.
    /// </summary>
    public interface IChromaBroadcastLink : IDisposable
    {
        ChromaLinkState State { get; }

        string StatusText { get; }

        /// <summary>True once Razer reports the stream as live.</summary>
        bool IsLive { get; }

        /// <summary>Raised when the state changes. May come from a Razer thread.</summary>
        event EventHandler StateChanged;

        bool Start(string appId);

        void Stop();
    }
}
