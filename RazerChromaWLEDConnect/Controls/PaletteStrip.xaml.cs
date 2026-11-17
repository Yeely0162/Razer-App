using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace RazerChromaWLEDConnect.Controls
{
    public partial class PaletteStrip : UserControl
    {
        public static readonly DependencyProperty ChannelsProperty = DependencyProperty.Register(
            "Channels",
            typeof(IEnumerable),
            typeof(PaletteStrip),
            new PropertyMetadata(null));

        public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
            "Revision",
            typeof(int),
            typeof(PaletteStrip),
            new PropertyMetadata(0));

        public static readonly DependencyProperty ActiveStartProperty = DependencyProperty.Register(
            "ActiveStart",
            typeof(int),
            typeof(PaletteStrip),
            new PropertyMetadata(0));

        public static readonly DependencyProperty ActiveEndProperty = DependencyProperty.Register(
            "ActiveEnd",
            typeof(int),
            typeof(PaletteStrip),
            new PropertyMetadata(0));

        public PaletteStrip()
        {
            this.InitializeComponent();
        }

        public IEnumerable Channels
        {
            get { return (IEnumerable)this.GetValue(ChannelsProperty); }
            set { this.SetValue(ChannelsProperty, value); }
        }

        public int Revision
        {
            get { return (int)this.GetValue(RevisionProperty); }
            set { this.SetValue(RevisionProperty, value); }
        }

        public int ActiveStart
        {
            get { return (int)this.GetValue(ActiveStartProperty); }
            set { this.SetValue(ActiveStartProperty, value); }
        }

        public int ActiveEnd
        {
            get { return (int)this.GetValue(ActiveEndProperty); }
            set { this.SetValue(ActiveEndProperty, value); }
        }
    }
}
