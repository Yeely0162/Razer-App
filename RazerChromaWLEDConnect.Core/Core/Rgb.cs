using System;

namespace RazerChromaWLEDConnect.Core
{
    /// <summary>
    /// An RGB colour with 8 bit channels. Deliberately free of any UI dependency so the
    /// whole colour pipeline can be unit tested without WPF.
    /// </summary>
    public struct Rgb : IEquatable<Rgb>
    {
        public byte R;
        public byte G;
        public byte B;

        public static readonly Rgb Black = new Rgb(0, 0, 0);

        public Rgb(byte r, byte g, byte b)
        {
            this.R = r;
            this.G = g;
            this.B = b;
        }

        public bool IsBlack
        {
            get { return this.R == 0 && this.G == 0 && this.B == 0; }
        }

        public static Rgb FromArray(int[] values)
        {
            if (values == null || values.Length < 3) return Black;
            return new Rgb(Clamp(values[0]), Clamp(values[1]), Clamp(values[2]));
        }

        public void CopyTo(int[] target)
        {
            target[0] = this.R;
            target[1] = this.G;
            target[2] = this.B;
        }

        public string Hex
        {
            get { return string.Format("#{0:X2}{1:X2}{2:X2}", this.R, this.G, this.B); }
        }

        public static byte Clamp(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)Math.Round(value);
        }

        public static Rgb Scale(Rgb colour, double factor)
        {
            return new Rgb(Clamp(colour.R * factor), Clamp(colour.G * factor), Clamp(colour.B * factor));
        }

        public bool Equals(Rgb other)
        {
            return this.R == other.R && this.G == other.G && this.B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is Rgb && this.Equals((Rgb)obj);
        }

        public override int GetHashCode()
        {
            return (this.R << 16) | (this.G << 8) | this.B;
        }

        public static bool operator ==(Rgb left, Rgb right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Rgb left, Rgb right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return this.Hex;
        }
    }
}
