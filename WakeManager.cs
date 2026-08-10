using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SleepMngr
{
    public static class WakeManager
    {
        private static List<string> disabledDevices = new List<string>();
        private static bool mouseWakeDisabled = false;

        public static bool IsMouseWakeDisabled => mouseWakeDisabled;

        /// <summary>
        /// Отключает пробуждение от мыши. Только клавиатура сможет разбудить.
        /// </summary>
        public static bool DisableMouseWake()
        {
            try
            {
                var mouseDevices = GetWakeArmedMouseDevices();
                
                if (mouseDevices.Count == 0)
                {
                    mouseWakeDisabled = true;
                    return true;
                }

                foreach (var device in mouseDevices)
                {
                    if (RunPowerCfg($"/devicedisablewake \"{device}\""))
                    {
                        if (!disabledDevices.Contains(device))
                            disabledDevices.Add(device);
                    }
                }

                mouseWakeDisabled = true;
                return disabledDevices.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Восстанавливает пробуждение от мыши.
        /// </summary>
        public static bool EnableMouseWake()
        {
            try
            {
                foreach (var device in disabledDevices)
                {
                    RunPowerCfg($"/deviceenablewake \"{device}\"");
                }

                disabledDevices.Clear();
                mouseWakeDisabled = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Получает список мышиных устройств, которые могут будить систему.
        /// </summary>
        public static List<string> GetWakeArmedMouseDevices()
        {
            var allWakeDevices = GetWakeArmedDevices();
            return allWakeDevices.Where(IsMouseDevice).ToList();
        }

        /// <summary>
        /// Получает все устройства, которые могут будить систему.
        /// </summary>
        public static List<string> GetWakeArmedDevices()
        {
            var devices = new List<string>();
            try
            {
                string output = RunPowerCfgOutput("/devicequery wake_armed");
                if (string.IsNullOrWhiteSpace(output))
                    return devices;

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    // Пропускаем пустые строки и заголовки
                    if (string.IsNullOrEmpty(trimmed))
                        continue;
                    if (trimmed.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Устройства обычно содержат буквы и не являются сообщениями об ошибке
                    if (trimmed.Length > 2 && !trimmed.StartsWith("---"))
                        devices.Add(trimmed);
                }
            }
            catch { }
            return devices;
        }

        /// <summary>
        /// Определяет, является ли устройство мышью по его имени.
        /// </summary>
        private static bool IsMouseDevice(string deviceName)
        {
            string lower = deviceName.ToLowerInvariant();
            
            // Прямые совпадения с мышью
            if (lower.Contains("mouse")) return true;
            if (lower.Contains("мышь")) return true;
            if (lower.Contains("pointing")) return true;
            if (lower.Contains("touchpad")) return true;
            if (lower.Contains("trackpad")) return true;
            if (lower.Contains("trackpoint")) return true;
            
            // HID-совместимые устройства ввода (часто мыши)
            // Но НЕ keyboard
            if (lower.Contains("hid") && !lower.Contains("keyboard") && !lower.Contains("клавиатур"))
            {
                // HID-compliant device без уточнения — скорее мышь
                if (lower.Contains("hid-compliant") || lower.Contains("hid compatible"))
                    return true;
            }

            return false;
        }

        private static bool RunPowerCfg(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return false;

                    process.WaitForExit(5000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string RunPowerCfgOutput(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return string.Empty;

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    return output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Возвращает отладочную информацию о wake-устройствах.
        /// </summary>
        public static string GetDebugInfo()
        {
            var allWake = GetWakeArmedDevices();
            var mice = GetWakeArmedMouseDevices();
            
            string info = $"Wake-устройства ({allWake.Count}):\n";
            foreach (var d in allWake)
            {
                bool isMouse = mice.Contains(d);
                info += $"  {(isMouse ? "🖱️" : "⌨️")} {d}\n";
            }
            
            if (disabledDevices.Count > 0)
            {
                info += $"\nОтключённые ({disabledDevices.Count}):\n";
                foreach (var d in disabledDevices)
                    info += $"  ❌ {d}\n";
            }
            
            info += $"\nМышь будит: {(mouseWakeDisabled ? "НЕТ" : "ДА")}";
            return info;
        }
    }
}
