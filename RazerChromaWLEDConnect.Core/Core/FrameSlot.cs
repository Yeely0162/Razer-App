using System;
using System.Threading;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// Single slot between the Razer callback and the render thread. A new frame replaces the
    /// previous one instead of queueing, so a slow render loop can never fall behind and send
    /// stale colours; the number of replaced frames is kept for the performance page.
    /// </summary>
    public sealed class FrameSlot
    {
        private readonly object _sync = new object();
        private Rgb[] _anchors = new Rgb[4];
        private long _sequence;
        private long _dropped;

        public long Sequence
        {
            get { lock (this._sync) return this._sequence; }
        }

        public long DroppedFrames
        {
            get { lock (this._sync) return this._dropped; }
        }

        /// <summary>Called from the Chroma broadcast thread. Does no work beyond a copy.</summary>
        public void Publish(Rgb first, Rgb second, Rgb third, Rgb fourth)
        {
            lock (this._sync)
            {
                this._anchors[0] = first;
                this._anchors[1] = second;
                this._anchors[2] = third;
                this._anchors[3] = fourth;
                this._sequence++;
            }
        }

        /// <summary>
        /// Copies the newest frame into <paramref name="destination"/> when one arrived since
        /// <paramref name="lastSeenSequence"/>. Never allocates.
        /// </summary>
        public bool TryTake(long lastSeenSequence, Rgb[] destination, out long sequence, out long skipped)
        {
            lock (this._sync)
            {
                sequence = this._sequence;
                skipped = 0;

                if (this._sequence == lastSeenSequence) return false;

                if (lastSeenSequence > 0 && this._sequence > lastSeenSequence + 1)
                {
                    skipped = this._sequence - lastSeenSequence - 1;
                    this._dropped += skipped;
                }

                if (destination != null)
                {
                    int count = Math.Min(destination.Length, this._anchors.Length);
                    for (int i = 0; i < count; i++) destination[i] = this._anchors[i];
                }

                return true;
            }
        }

        public void Reset()
        {
            lock (this._sync)
            {
                this._sequence = 0;
                this._dropped = 0;
            }
        }
    }
}
