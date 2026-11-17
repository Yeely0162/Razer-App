using System;
using ChromaBroadcast;
using RazerChromaWLEDConnect.Core;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>
    /// The only place the Razer SDK is touched. The callback does nothing but copy four
    /// colours into the frame slot, and it never throws back into Razer's thread.
    ///
    /// Synapse does not always send a BroadcastStatus "Live" message even when it is clearly
    /// streaming, which is why the state flips to Live as soon as a real effect arrives, and
    /// flips back when the colours stop coming.
    /// </summary>
    public sealed class ChromaBroadcastLink : IChromaBroadcastLink
    {
        // Synapse only broadcasts when colours change, so quiet stretches are normal. This is
        // just a guard for when Synapse closes without telling us.
        private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(60);

        private readonly object _sync = new object();
        private readonly object _lifecycle = new object();
        private readonly FrameSlot _slot;

        private ChromaLinkState _state = ChromaLinkState.NotInitialised;
        private bool _live;
        private bool _registered;
        private bool _initialised;
        private DateTime _lastEffectUtc = DateTime.MinValue;

        public ChromaBroadcastLink(FrameSlot slot)
        {
            this._slot = slot;
        }

        public event EventHandler StateChanged;

        public ChromaLinkState State
        {
            get
            {
                lock (this._sync)
                {
                    if (this._state == ChromaLinkState.Live
                        && (DateTime.UtcNow - this._lastEffectUtc) > StreamTimeout)
                    {
                        return ChromaLinkState.WaitingForSynapse;
                    }

                    return this._state;
                }
            }
        }

        public bool IsLive
        {
            get { return this.State == ChromaLinkState.Live; }
        }

        public string StatusText
        {
            get
            {
                switch (this.State)
                {
                    case ChromaLinkState.Live: return "Live";
                    case ChromaLinkState.WaitingForSynapse: return "Waiting for Synapse";
                    case ChromaLinkState.InvalidAppId: return "App ID rejected";
                    case ChromaLinkState.NoAppId: return "No App ID";
                    case ChromaLinkState.Failed: return "Init failed";
                    default: return "Idle";
                }
            }
        }

        public bool Start(string appId)
        {
            lock (this._lifecycle)
            {
                this.StopCore();

                if (string.IsNullOrWhiteSpace(appId))
                {
                    this.SetState(ChromaLinkState.NoAppId);
                    return false;
                }

                Guid guid;
                if (!Guid.TryParse(appId.Trim(), out guid))
                {
                    this.SetState(ChromaLinkState.InvalidAppId);
                    return false;
                }

                try
                {
                    RzResult result = RzChromaBroadcastAPI.Init(guid);
                    if (result != RzResult.Success)
                    {
                        this.SetState(ChromaLinkState.InvalidAppId);
                        return false;
                    }

                    this._initialised = true;
                    RzChromaBroadcastAPI.RegisterEventNotification(this.OnBroadcast);
                    this._registered = true;
                    this.SetState(ChromaLinkState.WaitingForSynapse);
                    return true;
                }
                catch
                {
                    this.SetState(ChromaLinkState.Failed);
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (this._lifecycle)
            {
                this.StopCore();
            }
        }

        private void StopCore()
        {
            // Only tear down what was actually set up: calling UnInit on a link that was
            // never initialised (or twice in a row) is what made repeated Apply clicks die.
            if (this._registered)
            {
                try
                {
                    RzChromaBroadcastAPI.UnRegisterEventNotification();
                }
                catch
                {
                }

                this._registered = false;
            }

            if (this._initialised)
            {
                try
                {
                    RzChromaBroadcastAPI.UnInit();
                }
                catch
                {
                }

                this._initialised = false;
            }

            lock (this._sync)
            {
                this._live = false;
                this._lastEffectUtc = DateTime.MinValue;
            }

            ChromaLinkState state = this.State;
            if (state == ChromaLinkState.Live || state == ChromaLinkState.WaitingForSynapse)
            {
                this.SetState(ChromaLinkState.NotInitialised);
            }
        }

        private RzResult OnBroadcast(
            RzChromaBroadcastType type,
            RzChromaBroadcastStatus? status,
            RzChromaBroadcastEffect? effect)
        {
            try
            {
                if (type == RzChromaBroadcastType.BroadcastEffect && effect.HasValue)
                {
                    RzChromaBroadcastEffect value = effect.Value;

                    // ChromaLink2..5 are the four zones Synapse actually drives. ChromaLink1
                    // reads black on every setup we tried, so it is deliberately not used.
                    this._slot.Publish(
                        new Rgb(value.ChromaLink2.R, value.ChromaLink2.G, value.ChromaLink2.B),
                        new Rgb(value.ChromaLink3.R, value.ChromaLink3.G, value.ChromaLink3.B),
                        new Rgb(value.ChromaLink4.R, value.ChromaLink4.G, value.ChromaLink4.B),
                        new Rgb(value.ChromaLink5.R, value.ChromaLink5.G, value.ChromaLink5.B));

                    // A frame is proof of life, whatever the status message says.
                    lock (this._sync)
                    {
                        this._lastEffectUtc = DateTime.UtcNow;
                        this._live = true;
                    }

                    this.SetState(ChromaLinkState.Live);
                }
                else if (type == RzChromaBroadcastType.BroadcastStatus)
                {
                    bool live = status.HasValue && status.Value == RzChromaBroadcastStatus.Live;

                    lock (this._sync)
                    {
                        this._live = live;
                        if (live) this._lastEffectUtc = DateTime.UtcNow;
                    }

                    this.SetState(live ? ChromaLinkState.Live : ChromaLinkState.WaitingForSynapse);
                }
            }
            catch
            {
                // Never let an exception travel back into Razer's callback thread.
            }

            return RzResult.Success;
        }

        private void SetState(ChromaLinkState state)
        {
            bool changed;

            lock (this._sync)
            {
                changed = this._state != state;
                this._state = state;
            }

            if (!changed) return;

            EventHandler handler = this.StateChanged;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            this.Stop();
        }
    }
}
