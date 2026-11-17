using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RazerChromaWLEDConnect.Base;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// Holds the device list. The UI thread edits it, the render thread reads an immutable
    /// snapshot so a device can be added or removed without locking the frames.
    /// </summary>
    public sealed class DeviceManager
    {
        private readonly object _sync = new object();
        private readonly List<RGBBase> _devices = new List<RGBBase>();
        private RGBBase[] _snapshot = new RGBBase[0];
        private int _refreshing;

        /// <summary>Raised on the thread that changed the list.</summary>
        public event EventHandler DevicesChanged;

        /// <summary>Raised from the thread pool when a device finished refreshing.</summary>
        public event EventHandler<RGBBase> DeviceRefreshed;

        public int Count
        {
            get { lock (this._sync) return this._devices.Count; }
        }

        public RGBBase[] Snapshot()
        {
            return Volatile.Read(ref this._snapshot);
        }

        public RGBBase[] ToArray()
        {
            lock (this._sync) return this._devices.ToArray();
        }

        public void Add(RGBBase device)
        {
            if (device == null) return;
            lock (this._sync)
            {
                this._devices.Add(device);
                this.RefreshSnapshotLocked();
            }
            this.RaiseChanged();
        }

        public bool Remove(RGBBase device)
        {
            bool removed;
            lock (this._sync)
            {
                removed = this._devices.Remove(device);
                if (removed) this.RefreshSnapshotLocked();
            }
            if (removed) this.RaiseChanged();
            return removed;
        }

        public void Replace(IEnumerable<RGBBase> devices)
        {
            lock (this._sync)
            {
                this._devices.Clear();
                if (devices != null) this._devices.AddRange(devices);
                this.RefreshSnapshotLocked();
            }
            this.RaiseChanged();
        }

        private void RefreshSnapshotLocked()
        {
            Volatile.Write(ref this._snapshot, this._devices.ToArray());
        }

        private void RaiseChanged()
        {
            EventHandler handler = this.DevicesChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Refreshes every device on the thread pool. A WLED box that stopped answering costs
        /// a two second HTTP timeout, which used to freeze the window; here it just finishes
        /// late in the background.
        /// </summary>
        public bool IsRefreshing
        {
            get { return Volatile.Read(ref this._refreshing) != 0; }
        }

        public void RefreshAllInBackground(Action whenDone = null)
        {
            // One refresh at a time: a controller that stopped answering costs a two second
            // timeout, and the reconnect timer must never stack those up.
            if (Interlocked.Exchange(ref this._refreshing, 1) == 1) return;

            RGBBase[] devices = this.Snapshot();

            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < devices.Length; i++)
                    {
                        RGBBase device = devices[i];
                        try
                        {
                            device.Load();
                        }
                        catch
                        {
                            device.IsConnected = false;
                        }

                        EventHandler<RGBBase> handler = this.DeviceRefreshed;
                        if (handler != null) handler(this, device);
                    }
                }
                finally
                {
                    Volatile.Write(ref this._refreshing, 0);
                    if (whenDone != null) whenDone();
                }
            });
        }

        public void UnloadAll()
        {
            RGBBase[] devices = this.Snapshot();
            for (int i = 0; i < devices.Length; i++)
            {
                try { devices[i].Unload(); } catch { }
            }
        }
    }
}
