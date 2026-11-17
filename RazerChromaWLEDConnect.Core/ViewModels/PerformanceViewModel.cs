using System;
using System.Diagnostics;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.Mvvm;

namespace RazerChromaWLEDConnect.ViewModels
{
    /// <summary>Numbers for the performance page, refreshed once a second.</summary>
    public sealed class PerformanceViewModel : ObservableObject
    {
        private readonly PerformanceMonitor _monitor;
        private readonly FrameSlot _slot;
        private readonly RenderPipeline _pipeline;

        private long _lastChromaSequence;
        private long _lastSampleTicks;

        private double _outputFps;
        private double _chromaFps;
        private double _packetsPerSecond;
        private double _kilobytesPerSecond;
        private double _latencyMicroseconds;
        private long _skippedFrames;

        public PerformanceViewModel(PerformanceMonitor monitor, FrameSlot slot, RenderPipeline pipeline)
        {
            this._monitor = monitor;
            this._slot = slot;
            this._pipeline = pipeline;
        }

        public double OutputFps { get { return this._outputFps; } }

        public double ChromaFps { get { return this._chromaFps; } }

        public double PacketsPerSecond { get { return this._packetsPerSecond; } }

        public double KilobytesPerSecond { get { return this._kilobytesPerSecond; } }

        public double LatencyMicroseconds { get { return this._latencyMicroseconds; } }

        public long SkippedFrames { get { return this._skippedFrames; } }

        public int TargetFps { get { return this._pipeline.TargetFps; } }

        public string OutputFpsText { get { return this._outputFps.ToString("0"); } }

        public string ChromaFpsText { get { return this._chromaFps.ToString("0"); } }

        public string PacketsText { get { return this._packetsPerSecond.ToString("0"); } }

        public string BandwidthText { get { return this._kilobytesPerSecond.ToString("0.0") + " KB/s"; } }

        public string LatencyText
        {
            get
            {
                if (this._latencyMicroseconds <= 0) return "-";
                if (this._latencyMicroseconds < 1000) return this._latencyMicroseconds.ToString("0") + " us";
                return (this._latencyMicroseconds / 1000.0).ToString("0.00") + " ms";
            }
        }

        public string SkippedText { get { return this._skippedFrames.ToString(); } }

        public string LoadText
        {
            get
            {
                if (this.TargetFps <= 0) return "-";
                double percent = (this._outputFps / this.TargetFps) * 100.0;
                return percent.ToString("0") + "% of target";
            }
        }

        /// <summary>Call from a one second timer.</summary>
        public void Tick()
        {
            PerformanceCounters counters = this._monitor.ReadAndReset();

            long now = Stopwatch.GetTimestamp();
            double elapsed = 1.0;

            if (this._lastSampleTicks != 0)
            {
                elapsed = (now - this._lastSampleTicks) / (double)Stopwatch.Frequency;
                if (elapsed <= 0.05) elapsed = 0.05;
            }

            this._lastSampleTicks = now;

            long sequence = this._slot.Sequence;
            long chromaDelta = sequence - this._lastChromaSequence;
            if (this._lastChromaSequence == 0 || chromaDelta < 0) chromaDelta = 0;
            this._lastChromaSequence = sequence;

            this._outputFps = counters.RenderFrames / elapsed;
            this._chromaFps = chromaDelta / elapsed;
            this._packetsPerSecond = counters.Packets / elapsed;
            this._kilobytesPerSecond = counters.Bytes / elapsed / 1024.0;
            this._latencyMicroseconds = counters.AverageSendMicroseconds;
            this._skippedFrames += counters.SkippedFrames;

            this.RaisePropertyChanged("OutputFps");
            this.RaisePropertyChanged("OutputFpsText");
            this.RaisePropertyChanged("ChromaFps");
            this.RaisePropertyChanged("ChromaFpsText");
            this.RaisePropertyChanged("PacketsPerSecond");
            this.RaisePropertyChanged("PacketsText");
            this.RaisePropertyChanged("KilobytesPerSecond");
            this.RaisePropertyChanged("BandwidthText");
            this.RaisePropertyChanged("LatencyMicroseconds");
            this.RaisePropertyChanged("LatencyText");
            this.RaisePropertyChanged("SkippedFrames");
            this.RaisePropertyChanged("SkippedText");
            this.RaisePropertyChanged("LoadText");
            this.RaisePropertyChanged("TargetFps");
        }

        public void ResetSkipped()
        {
            this._skippedFrames = 0;
            this.RaisePropertyChanged("SkippedFrames");
            this.RaisePropertyChanged("SkippedText");
        }
    }
}
