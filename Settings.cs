using System;
using System.IO;
using System.Text.Json;

namespace SleepMngr
{
    public class Settings
    {
        private static string SettingsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr");

        private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        private bool restoreLidSettingsOnStart;

        public bool RestoreLidSettingsOnStart
        {
            get => restoreLidSettingsOnStart;
            set
            {
                restoreLidSettingsOnStart = value;
                Save();
            }
        }

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<Settings>(json);
                    if (loaded != null)
                        settings.restoreLidSettingsOnStart = loaded.restoreLidSettingsOnStart;
                }
            }
            catch { }
            return settings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
