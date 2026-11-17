using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.Mvvm;

namespace RazerChromaWLEDConnect.ViewModels
{
    /// <summary>One swatch in the Chroma input palette.</summary>
    public sealed class ChannelViewModel : ObservableObject
    {
        private ChannelInfo _info;

        public ChannelViewModel(ChannelInfo info)
        {
            this._info = info;
        }

        public int Index { get { return this._info.Index; } }

        /// <summary>C1..C32 for a native Razer colour, V1..V28 for an interpolated one.</summary>
        public string Label { get { return this._info.Label; } }

        public bool IsNative { get { return this._info.IsNative; } }

        public bool IsVirtual { get { return !this._info.IsNative; } }

        public string KindText { get { return this._info.KindText; } }

        public double Position { get { return this._info.Position; } }

        public string Hex { get { return this._info.Color.Hex; } }

        public Rgb Color { get { return this._info.Color; } }

        /// <summary>Short label used next to the swatch, e.g. "33%".</summary>
        public string PositionText
        {
            get { return ((int)System.Math.Round(this._info.Position * 100)) + "%"; }
        }

        public void Update(ChannelInfo info)
        {
            bool colourChanged = this._info.Color != info.Color;
            bool layoutChanged = this._info.IsNative != info.IsNative
                                 || this._info.Index != info.Index
                                 || System.Math.Abs(this._info.Position - info.Position) > 0.0001;

            this._info = info;

            if (colourChanged)
            {
                this.RaisePropertyChanged("Color");
                this.RaisePropertyChanged("Hex");
            }

            if (layoutChanged)
            {
                this.RaisePropertyChanged("Index");
                this.RaisePropertyChanged("Label");
                this.RaisePropertyChanged("IsNative");
                this.RaisePropertyChanged("IsVirtual");
                this.RaisePropertyChanged("KindText");
                this.RaisePropertyChanged("Position");
                this.RaisePropertyChanged("PositionText");
            }
        }
    }
}
