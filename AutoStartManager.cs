using System;
using Microsoft.Win32;

namespace SleepMngr
{
    internal static class AutoStartManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SleepMngr";

        public static string LastError { get; private set; } = string.Empty;

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                    return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    AppLog.Write("Autostart", $"Could not read autostart state: {ex.Message}");
                    return false;
                }
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                LastError = string.Empty;

                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null)
                    return Fail("Could not open the current-user Run registry key.");

                if (enabled)
                {
                    string? executablePath = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(executablePath))
                        return Fail("Could not determine the current executable path.");

                    string command = $"\"{executablePath}\"";
                    key.SetValue(ValueName, command, RegistryValueKind.String);

                    string? savedValue = key.GetValue(ValueName) as string;
                    if (!string.Equals(savedValue, command, StringComparison.OrdinalIgnoreCase))
                        return Fail("The autostart registry value could not be verified after writing.");

                    AppLog.Write("Autostart", $"Enabled: {command}");
                    return true;
                }

                key.DeleteValue(ValueName, throwOnMissingValue: false);
                if (key.GetValue(ValueName) != null)
                    return Fail("The autostart registry value could not be removed.");

                AppLog.Write("Autostart", "Disabled");
                return true;
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static bool Fail(string message)
        {
            LastError = message;
            AppLog.Write("Autostart", $"Failed: {message}");
            return false;
        }
    }
}
