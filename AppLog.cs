using System;
using System.Diagnostics;
using System.IO;

namespace SleepMngr
{
    internal static class AppLog
    {
        private static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr");

        private static readonly string LogFile = Path.Combine(AppDataDirectory, "sleep_log.txt");
        private static readonly string LoggingSettingFile = Path.Combine(AppDataDirectory, "logging.txt");

        private static bool enabled = LoadEnabled();

        public static bool Enabled => enabled;
        public static string FilePath => LogFile;

        public static void SetEnabled(bool value)
        {
            if (enabled == value)
                return;

            // Record the transition itself when disabling. When enabling, persist the
            // setting first and then write the first line into the newly enabled log.
            if (!value)
                Write("Logging", "Logging disabled");

            enabled = value;
            SaveEnabled(value);

            if (value)
                Write("Logging", "Logging enabled");
        }

        public static void Write(string source, string message)
        {
            if (!enabled)
                return;

            try
            {
                Directory.CreateDirectory(AppDataDirectory);

                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [{source}] {message}";
                File.AppendAllText(LogFile, logMessage + Environment.NewLine);
                Debug.WriteLine(logMessage);
            }
            catch
            {
                // Diagnostics must never break the tray application.
            }
        }

        private static bool LoadEnabled()
        {
            try
            {
                if (!File.Exists(LoggingSettingFile))
                    return false;

                string value = File.ReadAllText(LoggingSettingFile).Trim();
                return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void SaveEnabled(bool value)
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                File.WriteAllText(LoggingSettingFile, value ? "1" : "0");
            }
            catch
            {
                // A settings write failure must not affect application behavior.
            }
        }
    }
}
