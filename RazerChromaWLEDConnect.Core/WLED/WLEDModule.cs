using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Serialization;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.Core;

namespace RazerChromaWLEDConnect.WLED
{
    /// <summary>
    /// A WLED controller. All colour work happens in the render thread; the HTTP refresh runs
    /// on the thread pool so the UI never waits on a device that stopped answering.
    /// </summary>
    public class WLEDModule : RGBBase
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        private string _wledIpAddress = string.Empty;
        private int _wledPort = 21324;
        private string _macAddress = string.Empty;
        private string _groupName;
        private List<WLEDSegment> _segments;

        private readonly WledTransport _transport = new WledTransport();
        private SegmentMappingSpec[] _specs = new SegmentMappingSpec[0];
        private Rgb[] _brightnessBuffer;
        private uint _lastChecksum;
        private long _lastSendTicks;
        private bool _liveModeEntered;
        private DateTime _lastRefreshUtc = DateTime.MinValue;

        public WLEDModule()
        {
            this._groupName = Guid.NewGuid().ToString();
        }

        public string WLEDIPAddress
        {
            get { return this._wledIpAddress; }
            set { if (this._wledIpAddress == value) return; this._wledIpAddress = value; this._transport.Close(); this.OnPropertyChanged("WLEDIPAddress"); }
        }

        public int WLEDPort
        {
            get { return this._wledPort; }
            set { if (this._wledPort == value) return; this._wledPort = value; this._transport.Close(); this.OnPropertyChanged("WLEDPort"); }
        }

        public string MacAddress
        {
            get { return this._macAddress; }
            set { if (this._macAddress == value) return; this._macAddress = value; this.OnPropertyChanged("MacAddress"); }
        }

        public string GroupName
        {
            get { return this._groupName; }
            set { if (this._groupName == value) return; this._groupName = value; this.OnPropertyChanged("GroupName"); }
        }

        public List<WLEDSegment> Segments
        {
            get { return this._segments; }
            set { this._segments = value; this.OnPropertyChanged("Segments"); this.OnPropertyChanged("HasSegments"); }
        }

        public bool IsOn;

        /// <summary>Seconds WLED keeps the last received frame before it leaves live mode.</summary>
        public byte LiveTimeout { get; set; } = 255;

        [XmlIgnore]
        public override string DeviceKind { get { return "WLED"; } }

        [XmlIgnore]
        public bool HasSegments
        {
            get { return this._segments != null && this._segments.Count > 1; }
        }

        [XmlIgnore]
        public long PacketsSent { get { return this._transport.PacketsSent; } }

        [XmlIgnore]
        public long BytesSent { get { return this._transport.BytesSent; } }

        [XmlIgnore]
        public double LastSendMicroseconds { get { return this._transport.LastSendMicroseconds; } }

        [XmlIgnore]
        public string LastError { get { return this._transport.LastError; } }

        [XmlIgnore]
        public string BaseUrl { get { return "http://" + this._wledIpAddress; } }

        public override void Load()
        {
            if (string.IsNullOrEmpty(this._wledIpAddress))
            {
                this.IsConnected = false;
                return;
            }

            try
            {
                string body = Http.GetStringAsync(this.BaseUrl + "/json").GetAwaiter().GetResult();
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    JsonElement root = document.RootElement;

                    JsonElement info;
                    if (root.TryGetProperty("info", out info))
                    {
                        int count = ReadInt(info, "leds", "count", this.LedCount);
                        if (count > 0 && count != this.LedCount) this.LedCount = count;

                        int udpPort = ReadInt(info, "udpport", -1);
                        if (udpPort > 0) this.WLEDPort = udpPort;

                        string mac = ReadString(info, "mac");
                        if (!string.IsNullOrEmpty(mac)) this.MacAddress = mac;

                        string name = ReadString(info, "name");
                        if (!string.IsNullOrEmpty(name)) this.Name = name;
                    }

                    JsonElement state;
                    if (root.TryGetProperty("state", out state))
                    {
                        JsonElement segments;
                        if (state.TryGetProperty("seg", out segments) && segments.ValueKind == JsonValueKind.Array)
                        {
                            this.MergeSegments(segments);
                        }
                    }
                }

                this.IsConnected = true;
            }
            catch
            {
                this.IsConnected = false;
                this._transport.Close();
            }

            this._lastRefreshUtc = DateTime.UtcNow;
        }

        private void MergeSegments(JsonElement segments)
        {
            var parsed = new List<WLEDSegment>();
            foreach (JsonElement element in segments.EnumerateArray())
            {
                var segment = new WLEDSegment();
                segment.start = ReadInt(element, "start", 0);
                segment.stop = ReadInt(element, "stop", 0);
                segment.len = ReadInt(element, "len", segment.stop - segment.start);
                segment.rev = ReadBool(element, "rev");
                parsed.Add(segment);
            }

            // Keep the user's colour selections when the segment layout did not change.
            if (this._segments != null && this._segments.Count == parsed.Count)
            {
                for (int i = 0; i < parsed.Count; i++)
                {
                    WLEDSegment existing = this._segments[i];
                    parsed[i].Color1 = existing.Color1;
                    parsed[i].Color2 = existing.Color2;
                    parsed[i].Color3 = existing.Color3;
                    parsed[i].Color4 = existing.Color4;
                    parsed[i].Gradient = existing.Gradient;
                    parsed[i].ChannelStart = existing.ChannelStart;
                    parsed[i].ChannelEnd = existing.ChannelEnd;
                }
            }

            this.Segments = parsed;
        }

        private static int ReadInt(JsonElement element, string name, int fallback)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return fallback;
            int parsed;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed) ? parsed : fallback;
        }

        private static int ReadInt(JsonElement element, string name, string nested, int fallback)
        {
            JsonElement child;
            if (!element.TryGetProperty(name, out child)) return fallback;
            return ReadInt(child, nested, fallback);
        }

        private static string ReadString(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool ReadBool(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return false;
            return value.ValueKind == JsonValueKind.True;
        }

        protected override void RenderIntoBuffer(Rgb[] channels, int channelCount)
        {
            if (this.ColorTypeSegment && this._segments != null && this._segments.Count > 0)
            {
                if (this._specs.Length != this._segments.Count) this._specs = new SegmentMappingSpec[this._segments.Count];

                for (int i = 0; i < this._segments.Count; i++)
                {
                    WLEDSegment segment = this._segments[i];
                    int first;
                    int last;
                    segment.ResolveChannelRange(channelCount, out first, out last);

                    this._specs[i].Start = segment.AbsoluteStart;
                    this._specs[i].Stop = segment.AbsoluteStop;
                    this._specs[i].ChannelStart = first;
                    this._specs[i].ChannelEnd = last;
                    this._specs[i].Gradient = segment.Gradient;
                    this._specs[i].Reverse = segment.rev;
                }

                LedMappingEngine.RenderSegments(channels, this._specs, this._specs.Length, this.FrameBuffer);
                return;
            }

            base.RenderIntoBuffer(channels, channelCount);
        }

        protected override void PushFrame()
        {
            if (!this.Enabled || !this.IsConnected) return;
            if (!this._transport.Ensure(this._wledIpAddress, this._wledPort))
            {
                this.IsConnected = false;
                return;
            }

            if (!this._liveModeEntered || !this.IsOn)
            {
                // Brightness is applied to the payload instead of the controller: many WLED
                // builds ignore "bri" while realtime data is streaming, which is why the
                // slider used to do nothing at all.
                this._transport.SendState(true, 255, true);
                this._liveModeEntered = true;
                this.IsOn = true;
                this._lastChecksum = 0;
            }

            Rgb[] payload = this.BuildBrightnessAdjustedPayload();
            uint checksum = ComputeChecksum(payload, this.FrameLength);
            long now = Environment.TickCount64;
            bool changed = checksum != this._lastChecksum;
            bool keepAlive = now - this._lastSendTicks > 5000;

            if (!changed && !keepAlive) return;

            if (this._transport.SendStrip(payload, this.FrameLength, this.LiveTimeout))
            {
                this._lastChecksum = checksum;
                this._lastSendTicks = now;
            }
            else
            {
                this.IsConnected = false;
                this._liveModeEntered = false;
            }
        }

        /// <summary>
        /// Returns the frame to send, scaled by the device brightness when it is below full.
        /// Reuses one buffer so a dimmed strip does not allocate every frame.
        /// </summary>
        private Rgb[] BuildBrightnessAdjustedPayload()
        {
            double brightness = this.Brightness;
            if (brightness >= 254.5) return this.FrameBuffer;

            if (brightness < 0) brightness = 0;
            double factor = brightness / 255.0;

            if (this._brightnessBuffer == null || this._brightnessBuffer.Length != this.FrameLength)
            {
                this._brightnessBuffer = new Rgb[this.FrameLength];
            }

            for (int i = 0; i < this.FrameLength; i++)
            {
                this._brightnessBuffer[i] = Rgb.Scale(this.FrameBuffer[i], factor);
            }

            return this._brightnessBuffer;
        }

        private static uint ComputeChecksum(Rgb[] buffer, int length)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < length; i++)
            {
                Rgb colour = buffer[i];
                hash = (hash ^ colour.R) * 16777619u;
                hash = (hash ^ colour.G) * 16777619u;
                hash = (hash ^ colour.B) * 16777619u;
            }
            return hash;
        }

        public override void TurnOn()
        {
            if (!this.Enabled) return;
            if (!this._transport.Ensure(this._wledIpAddress, this._wledPort)) return;
            this._transport.SendState(true, (int)Math.Round(this.Brightness), true);
            this._liveModeEntered = true;
            this.IsOn = true;
        }

        public override void TurnOff()
        {
            if (!this._transport.IsOpen) return;

            var black = new Rgb[this.LedCount];
            this._transport.SendStrip(black, black.Length, 1);
            this._transport.SendState(false, (int)Math.Round(this.Brightness), false);
            this._transport.Close();

            this.IsOn = false;
            this._liveModeEntered = false;
            this._lastChecksum = 0;
        }

        public override void Unload()
        {
            this.TurnOff();
            this._transport.Close();
            this._liveModeEntered = false;
        }

        /// <summary>Re-reads the controller, used by the discovery service and by reconnects.</summary>
        public void Refresh()
        {
            this.Load();
        }

        public bool NeedsRefresh
        {
            get { return (DateTime.UtcNow - this._lastRefreshUtc).TotalSeconds > 30; }
        }
    }
}
