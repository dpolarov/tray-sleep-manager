using System;
using System.Diagnostics;
using System.IO;

namespace SleepMngr
{
    internal static class AppLog
    {
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr",
            "sleep_log.txt");

        public static void Write(string source, string message)
        {
            try
            {
                string? directory = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [{source}] {message}";
                File.AppendAllText(LogFile, logMessage + Environment.NewLine);
                Debug.WriteLine(logMessage);
            }
            catch
            {
                // Diagnostics must never break the tray application.
            }
        }
    }
}
