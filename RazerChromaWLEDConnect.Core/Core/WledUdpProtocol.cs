using System;
using System.Text;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// Builds the realtime UDP payloads understood by WLED 0.11+.
    ///
    ///   DRGB   [0x02][timeout][R][G][B]...              up to 490 LEDs, index 0 implied
    ///   DNRGB  [0x04][timeout][startHi][startLo][R][G][B]...  up to 490 LEDs per packet,
    ///          every packet carries the absolute index of its first LED.
    ///
    /// A strip of 490 LEDs or less therefore fits in one DRGB packet, anything longer has
    /// to be split into DNRGB packets. Nothing here caps the LED count artificially: the
    /// number of packets simply grows with the strip.
    /// </summary>
    public static class WledUdpProtocol
    {
        public const byte DrgbHeader = 0x02;
        public const byte DnrgbHeader = 0x04;

        /// <summary>Largest number of LEDs one DRGB or DNRGB packet may carry.</summary>
        public const int MaxLedsPerPacket = 490;

        /// <summary>Largest packet we ever build: 4 byte header plus 490 RGB triples.</summary>
        public const int MaxPacketBytes = 4 + (MaxLedsPerPacket * 3);

        public static bool RequiresDnrgb(int ledCount)
        {
            return ledCount > MaxLedsPerPacket;
        }

        public static int PacketCount(int ledCount)
        {
            if (ledCount <= 0) return 0;
            if (ledCount <= MaxLedsPerPacket) return 1;
            return (ledCount + MaxLedsPerPacket - 1) / MaxLedsPerPacket;
        }

        /// <summary>
        /// Writes a DRGB packet covering <paramref name="count"/> LEDs starting at
        /// <paramref name="offset"/>. Returns the packet length in bytes.
        /// </summary>
        public static int BuildDrgb(Rgb[] leds, int offset, int count, byte timeout, byte[] buffer)
        {
            if (leds == null) throw new ArgumentNullException("leds");
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (count < 0 || count > MaxLedsPerPacket)
                throw new ArgumentOutOfRangeException("count", "DRGB carries at most " + MaxLedsPerPacket + " LEDs");
            if (offset < 0 || offset + count > leds.Length)
                throw new ArgumentOutOfRangeException("offset", "LED range is outside the source array");

            int length = 2 + (count * 3);
            if (buffer.Length < length)
                throw new ArgumentException("Buffer is too small for the DRGB packet", "buffer");

            buffer[0] = DrgbHeader;
            buffer[1] = timeout;

            int target = 2;
            for (int i = 0; i < count; i++)
            {
                Rgb colour = leds[offset + i];
                buffer[target++] = colour.R;
                buffer[target++] = colour.G;
                buffer[target++] = colour.B;
            }

            return length;
        }

        /// <summary>
        /// Writes one DNRGB packet for <paramref name="count"/> LEDs starting at the absolute
        /// LED index <paramref name="startIndex"/>. Returns the packet length in bytes.
        /// </summary>
        public static int BuildDnrgb(Rgb[] leds, int offset, int count, int startIndex, byte timeout, byte[] buffer)
        {
            if (leds == null) throw new ArgumentNullException("leds");
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (count < 0 || count > MaxLedsPerPacket)
                throw new ArgumentOutOfRangeException("count", "DNRGB carries at most " + MaxLedsPerPacket + " LEDs per packet");
            if (startIndex < 0 || startIndex > 0xFFFF)
                throw new ArgumentOutOfRangeException("startIndex", "DNRGB start index must fit in 16 bits");
            if (offset < 0 || offset + count > leds.Length)
                throw new ArgumentOutOfRangeException("offset", "LED range is outside the source array");

            int length = 4 + (count * 3);
            if (buffer.Length < length)
                throw new ArgumentException("Buffer is too small for the DNRGB packet", "buffer");

            buffer[0] = DnrgbHeader;
            buffer[1] = timeout;
            buffer[2] = (byte)((startIndex >> 8) & 0xFF);
            buffer[3] = (byte)(startIndex & 0xFF);

            int target = 4;
            for (int i = 0; i < count; i++)
            {
                Rgb colour = leds[offset + i];
                buffer[target++] = colour.R;
                buffer[target++] = colour.G;
                buffer[target++] = colour.B;
            }

            return length;
        }

        /// <summary>
        /// Splits a full strip into ready to send packets. Reuses the caller's buffers so a
        /// running strip does not allocate per frame. Returns the number of packets written.
        /// </summary>
        public static int BuildFrames(Rgb[] leds, int ledCount, byte timeout, byte[][] buffers, int[] lengths)
        {
            if (leds == null) throw new ArgumentNullException("leds");
            if (buffers == null) throw new ArgumentNullException("buffers");
            if (lengths == null) throw new ArgumentNullException("lengths");
            if (ledCount < 0 || ledCount > leds.Length)
                throw new ArgumentOutOfRangeException("ledCount");

            int packets = PacketCount(ledCount);
            if (packets == 0) return 0;
            if (buffers.Length < packets) throw new ArgumentException("Not enough packet buffers", "buffers");
            if (lengths.Length < packets) throw new ArgumentException("Not enough length slots", "lengths");

            if (packets == 1)
            {
                lengths[0] = BuildDrgb(leds, 0, ledCount, timeout, buffers[0]);
                return 1;
            }

            for (int packet = 0; packet < packets; packet++)
            {
                int startIndex = packet * MaxLedsPerPacket;
                int count = Math.Min(MaxLedsPerPacket, ledCount - startIndex);
                lengths[packet] = BuildDnrgb(leds, startIndex, count, startIndex, timeout, buffers[packet]);
            }

            return packets;
        }

        /// <summary>Allocates the packet buffers for a strip of the given size.</summary>
        public static byte[][] CreateBuffers(int ledCount)
        {
            int packets = PacketCount(ledCount);
            if (packets <= 0) packets = 1;

            var buffers = new byte[packets][];
            for (int i = 0; i < packets; i++)
            {
                buffers[i] = new byte[MaxPacketBytes];
            }
            return buffers;
        }

        /// <summary>
        /// The JSON state datagram WLED accepts on its UDP port to switch live mode on or off.
        /// </summary>
        public static byte[] BuildStateJson(bool on, int brightness, bool live)
        {
            if (brightness < 0) brightness = 0;
            if (brightness > 255) brightness = 255;

            string json = string.Concat(
                "{\"on\":", on ? "true" : "false",
                ",\"bri\":", brightness.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ",\"live\":", live ? "true" : "false",
                "}");

            return Encoding.UTF8.GetBytes(json);
        }
    }
}
