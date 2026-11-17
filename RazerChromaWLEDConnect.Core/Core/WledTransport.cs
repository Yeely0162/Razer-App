using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// Owns one UDP socket per WLED device. Called only from the render thread, so it needs
    /// no locking of its own, and it reuses its packet buffers for the lifetime of the device.
    /// </summary>
    public sealed class WledTransport : IDisposable
    {
        private UdpClient _client;
        private string _address;
        private int _port;
        private byte[][] _buffers;
        private int[] _lengths;
        private int _bufferedLedCount = -1;

        public long PacketsSent { get; private set; }
        public long BytesSent { get; private set; }
        public long SendFailures { get; private set; }
        public double LastSendMicroseconds { get; private set; }
        public string LastError { get; private set; }

        public bool IsOpen
        {
            get { return this._client != null; }
        }

        public string Address
        {
            get { return this._address; }
        }

        public int Port
        {
            get { return this._port; }
        }

        /// <summary>Creates or recycles the socket for the given endpoint.</summary>
        public bool Ensure(string address, int port)
        {
            if (string.IsNullOrEmpty(address) || port <= 0 || port > 65535)
            {
                this.Close();
                this.LastError = "Invalid address or port";
                return false;
            }

            if (this._client != null && this._address == address && this._port == port) return true;

            this.Close();

            IPAddress parsed;
            if (!IPAddress.TryParse(address, out parsed))
            {
                this.LastError = "Cannot parse IP address '" + address + "'";
                return false;
            }

            try
            {
                var client = new UdpClient();
                client.Connect(new IPEndPoint(parsed, port));
                this._client = client;
                this._address = address;
                this._port = port;
                this.LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                this.SendFailures++;
                return false;
            }
        }

        /// <summary>Sends the whole strip, splitting it into as many packets as it needs.</summary>
        public bool SendStrip(Rgb[] strip, int ledCount, byte timeout)
        {
            if (this._client == null) return false;
            if (ledCount <= 0 || ledCount > strip.Length) return false;

            if (this._bufferedLedCount != ledCount)
            {
                this._buffers = WledUdpProtocol.CreateBuffers(ledCount);
                this._lengths = new int[this._buffers.Length];
                this._bufferedLedCount = ledCount;
            }

            int packets;
            try
            {
                packets = WledUdpProtocol.BuildFrames(strip, ledCount, timeout, this._buffers, this._lengths);
            }
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                this.SendFailures++;
                return false;
            }

            long started = Stopwatch.GetTimestamp();
            int bytes = 0;

            try
            {
                for (int i = 0; i < packets; i++)
                {
                    this._client.Send(this._buffers[i], this._lengths[i]);
                    bytes += this._lengths[i];
                }
            }
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                this.SendFailures++;
                this.Close();
                return false;
            }

            this.LastSendMicroseconds = (Stopwatch.GetTimestamp() - started) * 1000000.0 / Stopwatch.Frequency;
            this.PacketsSent += packets;
            this.BytesSent += bytes;
            return true;
        }

        /// <summary>Sends the JSON state datagram (switch on/off, brightness, live flag).</summary>
        public bool SendState(bool on, int brightness, bool live)
        {
            if (this._client == null) return false;

            byte[] payload = WledUdpProtocol.BuildStateJson(on, brightness, live);
            try
            {
                this._client.Send(payload, payload.Length);
                this.PacketsSent++;
                this.BytesSent += payload.Length;
                return true;
            }
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                this.SendFailures++;
                this.Close();
                return false;
            }
        }

        public void Close()
        {
            if (this._client == null) return;
            try { this._client.Close(); } catch { }
            try { this._client.Dispose(); } catch { }
            this._client = null;
        }

        public void Dispose()
        {
            this.Close();
        }
    }
}
