using System;
using System.Collections.Generic;
using System.Linq;

namespace SleepMngr
{
    public static class WakeManager
    {
        private static readonly List<string> disabledDevices = new();
        private static bool mouseWakeDisabled;

        public static bool IsMouseWakeDisabled => mouseWakeDisabled;
        public static string? LastError { get; private set; }

        /// <summary>
        /// Отключает пробуждение от мыши. Только клавиатура сможет разбудить.
        /// </summary>
        public static bool DisableMouseWake()
        {
            LastError = null;

            if (!TryGetWakeArmedMouseDevices(out var mouseDevices))
                return false;

            if (mouseDevices.Count == 0)
            {
                mouseWakeDisabled = true;
                return true;
            }

            var changedThisCall = new List<string>();
            var failures = new List<string>();

            foreach (var device in mouseDevices)
            {
                var result = PowerCfgRunner.Run($"/devicedisablewake \"{device}\"");
                if (result.Success)
                {
                    if (!disabledDevices.Contains(device))
                        disabledDevices.Add(device);
                    changedThisCall.Add(device);
                }
                else
                {
                    failures.Add($"{device}: {result.DescribeFailure()}");
                }
            }

            if (failures.Count == 0)
            {
                mouseWakeDisabled = true;
                return true;
            }

            // Avoid leaving a silently partial state when only some devices could be changed.
            // Best-effort rollback; failures remain tracked so a later restore can retry them.
            foreach (var device in changedThisCall)
            {
                var rollback = PowerCfgRunner.Run($"/deviceenablewake \"{device}\"");
                if (rollback.Success)
                {
                    disabledDevices.Remove(device);
                }
                else
                {
                    failures.Add($"rollback {device}: {rollback.DescribeFailure()}");
                }
            }

            mouseWakeDisabled = disabledDevices.Count > 0;
            SetLastError("Could not disable mouse wake", failures);
            return false;
        }

        /// <summary>
        /// Восстанавливает пробуждение от мыши.
        /// </summary>
        public static bool EnableMouseWake()
        {
            LastError = null;
            var failures = new List<string>();

            foreach (var device in disabledDevices.ToList())
            {
                var result = PowerCfgRunner.Run($"/deviceenablewake \"{device}\"");
                if (result.Success)
                {
                    disabledDevices.Remove(device);
                }
                else
                {
                    failures.Add($"{device}: {result.DescribeFailure()}");
                }
            }

            mouseWakeDisabled = disabledDevices.Count > 0;

            if (failures.Count > 0)
            {
                SetLastError("Could not restore mouse wake", failures);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Получает список мышиных устройств, которые могут будить систему.
        /// </summary>
        public static List<string> GetWakeArmedMouseDevices()
        {
            return TryGetWakeArmedMouseDevices(out var devices) ? devices : new List<string>();
        }

        public static bool TryGetWakeArmedMouseDevices(out List<string> devices)
        {
            devices = new List<string>();
            if (!TryGetWakeArmedDevices(out var allWakeDevices))
                return false;

            devices = allWakeDevices.Where(IsMouseDevice).ToList();
            return true;
        }

        /// <summary>
        /// Получает все устройства, которые могут будить систему.
        /// </summary>
        public static List<string> GetWakeArmedDevices()
        {
            return TryGetWakeArmedDevices(out var devices) ? devices : new List<string>();
        }

        private static bool TryGetWakeArmedDevices(out List<string> devices)
        {
            devices = new List<string>();
            LastError = null;

            var result = PowerCfgRunner.Run("/devicequery wake_armed");
            if (!result.Success)
            {
                SetLastError($"Could not query wake-armed devices: {result.DescribeFailure()}");
                return false;
            }

            var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                if (trimmed.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (trimmed.Length > 2 && !trimmed.StartsWith("---"))
                    devices.Add(trimmed);
            }

            return true;
        }

        /// <summary>
        /// Определяет, является ли устройство мышью по его имени.
        /// </summary>
        private static bool IsMouseDevice(string deviceName)
        {
            string lower = deviceName.ToLowerInvariant();

            if (lower.Contains("mouse")) return true;
            if (lower.Contains("мышь")) return true;
            if (lower.Contains("pointing")) return true;
            if (lower.Contains("touchpad")) return true;
            if (lower.Contains("trackpad")) return true;
            if (lower.Contains("trackpoint")) return true;

            if (lower.Contains("hid") && !lower.Contains("keyboard") && !lower.Contains("клавиатур"))
            {
                if (lower.Contains("hid-compliant") || lower.Contains("hid compatible"))
                    return true;
            }

            return false;
        }

        private static void SetLastError(string message)
        {
            LastError = message;
            AppLog.Write("WakeManager", message);
        }

        private static void SetLastError(string message, List<string> details)
        {
            string combined = $"{message}: {string.Join(" | ", details)}";
            SetLastError(combined);
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
            if (!string.IsNullOrWhiteSpace(LastError))
                info += $"\nПоследняя ошибка: {LastError}";

            return info;
        }
    }
}
