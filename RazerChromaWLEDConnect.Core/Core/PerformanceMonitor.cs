using System.Threading;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>Counters accumulated between two UI refreshes.</summary>
    public struct PerformanceCounters
    {
        public long ChromaFrames;
        public long RenderFrames;
        public long Packets;
        public long Bytes;
        public double AverageSendMicroseconds;
        public long SkippedFrames;
        public long DroppedFrames;
    }

    /// <summary>
    /// Lock free counters written by the Chroma callback and the render thread, read once a
    /// second by the performance page.
    /// </summary>
    public sealed class PerformanceMonitor
    {
        private long _chromaFrames;
        private long _renderFrames;
        private long _packets;
        private long _bytes;
        private long _skipped;
        private long _sendMicroseconds;
        private long _sendSamples;

        public void OnChromaFrame()
        {
            Interlocked.Increment(ref this._chromaFrames);
        }

        public void OnRenderFrame()
        {
            Interlocked.Increment(ref this._renderFrames);
        }

        public void OnFrameSkipped(long count)
        {
            if (count <= 0) return;
            Interlocked.Add(ref this._skipped, count);
        }

        public void OnSend(int packets, int bytes, double microseconds)
        {
            if (packets > 0) Interlocked.Add(ref this._packets, packets);
            if (bytes > 0) Interlocked.Add(ref this._bytes, bytes);
            if (microseconds > 0)
            {
                Interlocked.Add(ref this._sendMicroseconds, (long)microseconds);
                Interlocked.Increment(ref this._sendSamples);
            }
        }

        public PerformanceCounters ReadAndReset()
        {
            var counters = new PerformanceCounters();
            counters.ChromaFrames = Interlocked.Exchange(ref this._chromaFrames, 0);
            counters.RenderFrames = Interlocked.Exchange(ref this._renderFrames, 0);
            counters.Packets = Interlocked.Exchange(ref this._packets, 0);
            counters.Bytes = Interlocked.Exchange(ref this._bytes, 0);
            counters.SkippedFrames = Interlocked.Exchange(ref this._skipped, 0);

            long samples = Interlocked.Exchange(ref this._sendSamples, 0);
            long total = Interlocked.Exchange(ref this._sendMicroseconds, 0);
            counters.AverageSendMicroseconds = samples > 0 ? (double)total / samples : 0;

            return counters;
        }
    }
}
