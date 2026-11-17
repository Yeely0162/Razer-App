using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.WLED;

namespace RazerChromaWLEDConnect
{
    /// <summary>
    /// settings.xml model. Field names are unchanged from the original app, new options were
    /// only appended, so an existing settings.xml keeps working as it is.
    /// </summary>
    [Serializable]
    [XmlRoot("AppSettings")]
    public class AppSettings
    {
        public string RazerAppId = null;
        public bool Sync = false;
        public bool RunAtBoot = false;

        /// <summary>4, 8, 12, 16, 24 or 32.</summary>
        public int ChannelCount = 4;

        /// <summary>Colour frames sent to the devices each second.</summary>
        public int TargetFps = 60;

        /// <summary>Preview refreshes each second. Independent from TargetFps.</summary>
        public int PreviewFps = 20;

        [XmlArray]
        [XmlArrayItem("Instances")]
        public List<RGBBase> Instances = new List<RGBBase>();

        [XmlIgnore]
        public string SettingsPath { get; set; }

        public AppSettings()
        {
            this.Instances = new List<RGBBase>();
        }

        public static string DefaultSettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml"); }
        }

        /// <summary>
        /// Loads settings.xml. <paramref name="extraTypes"/> lets the WPF project add the
        /// hardware specific device types it owns, like the Lenovo keyboard.
        /// </summary>
        public static AppSettings Load(string path = null, Type[] extraTypes = null)
        {
            string file = string.IsNullOrEmpty(path) ? DefaultSettingsPath : path;
            AppSettings settings = null;

            if (File.Exists(file))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(AppSettings), extraTypes ?? new Type[0]);
                    using (var stream = new StreamReader(file))
                    {
                        settings = serializer.Deserialize(stream) as AppSettings;
                    }
                }
                catch
                {
                    settings = null;
                }
            }

            if (settings == null) settings = new AppSettings();
            if (settings.Instances == null) settings.Instances = new List<RGBBase>();
            settings.SettingsPath = file;
            return settings;
        }

        public void Save(Type[] extraTypes = null)
        {
            string file = string.IsNullOrEmpty(this.SettingsPath) ? DefaultSettingsPath : this.SettingsPath;

            var serializer = new XmlSerializer(typeof(AppSettings), extraTypes ?? new Type[0]);
            using (var writer = new StreamWriter(file))
            {
                serializer.Serialize(writer, this);
            }
        }
    }
}
