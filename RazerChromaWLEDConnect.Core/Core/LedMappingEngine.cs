using System;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>How one WLED segment should be filled.</summary>
    public struct SegmentMappingSpec
    {
        /// <summary>Absolute first LED of the segment (inclusive).</summary>
        public int Start;

        /// <summary>Absolute LED after the segment (exclusive).</summary>
        public int Stop;

        public int ChannelStart;
        public int ChannelEnd;
        public bool Gradient;
        public bool Reverse;
    }

    /// <summary>
    /// Fills a device LED buffer from the channel palette. Everything works on absolute LED
    /// indices, so segments that do not start at LED 0, segments with gaps and reversed
    /// segments all land where the user sees them in WLED.
    /// </summary>
    public static class LedMappingEngine
    {
        public static void Clear(Rgb[] output)
        {
            if (output == null) return;
            Array.Clear(output, 0, output.Length);
        }

        /// <summary>
        /// Spreads channels [channelStart..channelEnd] over the absolute LED window
        /// [outputStart..outputEnd). LEDs outside the window are left untouched.
        /// </summary>
        public static void RenderWindow(
            Rgb[] channels,
            int channelStart,
            int channelEnd,
            bool gradient,
            bool reverse,
            Rgb[] output,
            int outputStart,
            int outputEnd)
        {
            if (channels == null || output == null || channels.Length == 0) return;

            int from = Math.Max(0, outputStart);
            int to = Math.Min(output.Length, outputEnd);
            int length = to - from;
            if (length <= 0) return;

            int first = Math.Max(0, Math.Min(channelStart, channels.Length - 1));
            int last = Math.Min(channelEnd, channels.Length - 1);

            // Nothing selected: leave the window black instead of inventing a colour.
            if (last < first) return;

            int span = last - first + 1;

            if (span == 1)
            {
                Fill(output, from, to, channels[first], reverse);
                return;
            }

            if (gradient)
            {
                double step = (length > 1) ? (double)(span - 1) / (length - 1) : 0;
                for (int i = 0; i < length; i++)
                {
                    double position = i * step;
                    int index = (int)position;
                    if (index > span - 2) index = span - 2;
                    if (index < 0) index = 0;

                    double fraction = position - index;
                    Rgb colour = ColorInterpolationEngine.Lerp(
                        channels[first + index],
                        channels[first + index + 1],
                        fraction);

                    output[reverse ? (to - 1 - i) : (from + i)] = colour;
                }
            }
            else
            {
                int band = (int)Math.Ceiling((double)length / span);
                if (band < 1) band = 1;

                for (int i = 0; i < length; i++)
                {
                    int index = i / band;
                    if (index > span - 1) index = span - 1;

                    output[reverse ? (to - 1 - i) : (from + i)] = channels[first + index];
                }
            }
        }

        /// <summary>
        /// Per segment mode: clears the buffer and paints every segment at its absolute
        /// position. Segments may overlap, later specs win.
        /// </summary>
        public static void RenderSegments(
            Rgb[] channels,
            SegmentMappingSpec[] specs,
            int specCount,
            Rgb[] output)
        {
            if (output == null) return;
            Clear(output);
            if (channels == null || specs == null) return;

            int count = Math.Min(specCount, specs.Length);
            for (int i = 0; i < count; i++)
            {
                SegmentMappingSpec spec = specs[i];
                RenderWindow(
                    channels,
                    spec.ChannelStart,
                    spec.ChannelEnd,
                    spec.Gradient,
                    spec.Reverse,
                    output,
                    spec.Start,
                    spec.Stop);
            }
        }

        private static void Fill(Rgb[] output, int from, int to, Rgb colour, bool reverse)
        {
            for (int i = from; i < to; i++)
            {
                output[i] = colour;
            }
        }
    }
}
