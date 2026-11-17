using HidApiAdapter;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.Core;

namespace RazerChromaWLEDConnect
{
    /// <summary>
    /// Lenovo Legion 5 Pro keyboard (2020/2021). Kept from the original app: 4 zones, sent as
    /// a 33 byte HID feature report.
    /// </summary>
    public class LenovoKeyboard : RGBBase
    {
        private static readonly int[] VendorProductA = { 0x048d, 0xc965 };
        private static readonly int[] VendorProductB = { 0x048d, 0xc955 };

        private HidDevice _hidDevice;

        public LenovoKeyboard()
        {
            this.LedCount = 4;
            this.Brightness = 2;
            this.Name = "Lenovo keyboard";
            this.ColorTypeStrip = true;

            var devices = HidDeviceManager.GetManager().SearchDevices(0, 0);
            for (int i = 0; i < devices.Count; i++)
            {
                HidDevice device = devices[i];
                device.Connect();

                if (!Matches(device))
                {
                    device.Disconnect();
                    continue;
                }

                this._hidDevice = device;
                this.IsConnected = true;
                break;
            }
        }

        private static bool Matches(HidDevice device)
        {
            if (device.UsagePage != 0xff89 || device.Usage != 0x00cc) return false;

            return (device.VendorId == VendorProductA[0] && device.ProductId == VendorProductA[1])
                   || (device.VendorId == VendorProductB[0] && device.ProductId == VendorProductB[1]);
        }

        public override string DeviceKind
        {
            get { return "Lenovo"; }
        }

        public override double BrightnessMax
        {
            get { return 2; }
        }

        protected override void PushFrame()
        {
            if (!this.Enabled || !this.IsConnected) return;
            if (this.FrameBuffer == null || this.FrameLength < 4) return;

            var report = new byte[33];
            report[0] = 0xcc;
            report[1] = 0x16;
            report[2] = 0x01;
            report[3] = 1;
            report[4] = 2;

            for (int i = 0; i < 4; i++)
            {
                Rgb colour = this.FrameBuffer[i];
                int offset = 5 + (i * 3);
                report[offset] = colour.R;
                report[offset + 1] = colour.G;
                report[offset + 2] = colour.B;
            }

            try
            {
                if (this._hidDevice != null && this._hidDevice.IsConnected)
                {
                    this._hidDevice.SendFeatureReport(report);
                }
            }
            catch
            {
                this.IsConnected = false;
            }
        }

        public override void Load()
        {
            if (!this.Enabled) return;
            if (this._hidDevice == null) return;

            try
            {
                if (!this._hidDevice.IsConnected) this._hidDevice.Connect();
                this.IsConnected = this._hidDevice.IsConnected;
            }
            catch
            {
                this.IsConnected = false;
            }
        }

        public override void Unload()
        {
            if (this.FrameBuffer != null)
            {
                for (int i = 0; i < this.FrameBuffer.Length; i++) this.FrameBuffer[i] = Rgb.Black;
            }

            this.IsConnected = false;
            try
            {
                if (this._hidDevice != null) this._hidDevice.Disconnect();
            }
            catch
            {
            }
        }
    }
}
