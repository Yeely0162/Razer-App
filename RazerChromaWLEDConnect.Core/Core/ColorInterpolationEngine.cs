using System;
using System.Collections.Generic;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// Turns the four native Razer anchor colours into an arbitrary number of sampling
    /// channels. The four anchors always sit at 0, 1/3, 2/3 and 1 of the palette, so a
    /// 4 channel setup reproduces the original behaviour exactly.
    /// </summary>
    public static class ColorInterpolationEngine
    {
        /// <summary>
        /// Four anchors, fed from ChromaLink2..5. ChromaLink1 stays unused on purpose:
        /// Synapse leaves it black on the setups we tested, and a permanently black anchor
        /// drags every gradient towards black.
        /// </summary>
        public const int NativeAnchorCount = 4;

        /// <summary>Position of each native anchor along the palette.</summary>
        public static readonly double[] AnchorPositions = { 0.0, 1.0 / 3.0, 2.0 / 3.0, 1.0 };

        private const double Epsilon = 1e-9;

        public static Rgb Lerp(Rgb from, Rgb to, double amount)
        {
            if (amount <= 0) return from;
            if (amount >= 1) return to;

            double r = from.R + (to.R - from.R) * amount;
            double g = from.G + (to.G - from.G) * amount;
            double b = from.B + (to.B - from.B) * amount;
            return new Rgb(Rgb.Clamp(r), Rgb.Clamp(g), Rgb.Clamp(b));
        }

        /// <summary>
        /// Samples the palette made of the four anchors at position <paramref name="position"/>
        /// (0 = first anchor, 1 = last anchor).
        /// </summary>
        public static Rgb Sample(Rgb[] anchors, double position)
        {
            if (anchors == null || anchors.Length < NativeAnchorCount) return Rgb.Black;

            if (position <= 0) return anchors[0];
            if (position >= 1) return anchors[NativeAnchorCount - 1];

            double scaled = position * (NativeAnchorCount - 1);
            int index = (int)Math.Floor(scaled);
            if (index >= NativeAnchorCount - 1) return anchors[NativeAnchorCount - 1];

            double fraction = scaled - index;
            return Lerp(anchors[index], anchors[index + 1], fraction);
        }

        /// <summary>
        /// Distributes the channels over the three anchor intervals, keeping all four native
        /// anchors and spacing the generated channels as evenly as the anchor positions allow.
        /// </summary>
        public static double[] BuildChannelPositions(int channelCount)
        {
            if (channelCount < NativeAnchorCount) channelCount = NativeAnchorCount;

            int intervals = NativeAnchorCount - 1;
            int gaps = channelCount - 1;
            int perInterval = gaps / intervals;
            int remainder = gaps % intervals;

            var positions = new List<double>(channelCount);
            for (int interval = 0; interval < intervals; interval++)
            {
                int subdivisions = perInterval + (interval < remainder ? 1 : 0);
                double start = (double)interval / intervals;
                double length = 1.0 / intervals;

                for (int step = 0; step < subdivisions; step++)
                {
                    positions.Add(start + (length * step / subdivisions));
                }
            }

            positions.Add(1.0);
            return positions.ToArray();
        }

        public static bool IsAnchorPosition(double position)
        {
            for (int i = 0; i < AnchorPositions.Length; i++)
            {
                if (Math.Abs(position - AnchorPositions[i]) < Epsilon) return true;
            }
            return false;
        }

        /// <summary>The channel counts the UI offers.</summary>
        public static readonly int[] SupportedChannelCounts = { 4, 8, 12, 16, 24, 32 };

        public static int NormalizeChannelCount(int requested)
        {
            int best = SupportedChannelCounts[0];
            int bestDelta = int.MaxValue;

            for (int i = 0; i < SupportedChannelCounts.Length; i++)
            {
                int delta = Math.Abs(SupportedChannelCounts[i] - requested);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = SupportedChannelCounts[i];
                }
            }

            return best;
        }
    }
}
