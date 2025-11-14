using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Management;

namespace LidSleepManager
{
    public class MonitorDetector
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

        public static bool HasExternalMonitor()
        {
            // Используем количество активных дисплеев
            int activeCount = GetMonitorCount();
            
            // Если больше 1 дисплея - точно есть внешний
            if (activeCount > 1)
                return true;
            
            // Если 1 дисплей - проверяем сколько всего мониторов подключено
            // через WMI (включая неактивные)
            int totalMonitors = GetTotalMonitorCountWMI();
            
            // Если физически подключено >= 2 монитора, значит один из них внешний
            // (даже если встроенный сейчас выключен из-за закрытой крышки)
            return totalMonitors >= 2;
        }
        
        public static int GetAttachedDisplayCount()
        {
            return GetTotalMonitorCountWMI();
        }
        
        private static int GetTotalMonitorCountWMI()
        {
            try
            {
                int count = 0;
                using (var searcher = new ManagementObjectSearcher("root\\WMI", 
                    "SELECT * FROM WmiMonitorID"))
                {
                    foreach (var monitor in searcher.Get())
                    {
                        count++;
                    }
                }
                return count;
            }
            catch
            {
                // Если WMI недоступен, fallback на старый метод
                return GetAttachedDisplayCountFallback();
            }
        }
        
        private static int GetAttachedDisplayCountFallback()
        {
            // Считаем все мониторы через EnumDisplayDevices (включая неактивные)
            var allDevices = new List<string>();
            uint devNum = 0;
            
            // Перечисляем видеоадаптеры
            while (true)
            {
                DISPLAY_DEVICE adapter = new DISPLAY_DEVICE();
                adapter.cb = Marshal.SizeOf(adapter);
                
                if (!EnumDisplayDevices(null, devNum, ref adapter, 0))
                    break;
                
                // Для каждого адаптера перечисляем мониторы
                uint monNum = 0;
                while (true)
                {
                    DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                    monitor.cb = Marshal.SizeOf(monitor);
                    
                    if (!EnumDisplayDevices(adapter.DeviceName, monNum, ref monitor, 0))
                        break;
                    
                    if (!string.IsNullOrEmpty(monitor.DeviceString))
                    {
                        allDevices.Add(monitor.DeviceString);
                    }
                    
                    monNum++;
                }
                
                devNum++;
            }
            
            return allDevices.Count;
        }

        public static int GetMonitorCount()
        {
            // Используем Screen.AllScreens - самый надежный способ
            return Screen.AllScreens.Length;
        }

        public static List<DisplayInfo> GetActiveDisplays()
        {
            var displays = new List<DisplayInfo>();
            var screens = Screen.AllScreens;

            foreach (var screen in screens)
            {
                // Получаем детальную информацию через EnumDisplayDevices
                string deviceName = screen.DeviceName;
                string displayName = deviceName;
                bool isPrimary = screen.Primary;

                // Пытаемся получить более читаемое имя
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(dd);
                
                uint devNum = 0;
                while (EnumDisplayDevices(null, devNum, ref dd, 0))
                {
                    if (dd.DeviceName == deviceName && 
                        (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    {
                        // Получаем имя монитора
                        DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
                        monitor.cb = Marshal.SizeOf(monitor);
                        if (EnumDisplayDevices(dd.DeviceName, 0, ref monitor, 0))
                        {
                            displayName = monitor.DeviceString;
                        }
                        break;
                    }
                    devNum++;
                }

                displays.Add(new DisplayInfo
                {
                    DeviceName = deviceName,
                    DeviceString = displayName,
                    IsPrimary = isPrimary,
                    IsActive = true,
                    Bounds = $"{screen.Bounds.Width}x{screen.Bounds.Height}"
                });
            }

            return displays;
        }

        public class DisplayInfo
        {
            public string DeviceName { get; set; } = "";
            public string DeviceString { get; set; } = "";
            public bool IsPrimary { get; set; }
            public bool IsActive { get; set; }
            public string Bounds { get; set; } = "";
        }
        
        public static string GetDisplaysDebugInfo()
        {
            var displays = GetActiveDisplays();
            var sb = new StringBuilder();
            
            sb.AppendLine($"Всего активных дисплеев: {displays.Count}");
            sb.AppendLine();
            
            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                sb.AppendLine($"Дисплей {i + 1}:");
                sb.AppendLine($"  Устройство: {display.DeviceName}");
                sb.AppendLine($"  Название: {display.DeviceString}");
                sb.AppendLine($"  Разрешение: {display.Bounds}");
                sb.AppendLine($"  Основной: {(display.IsPrimary ? "Да" : "Нет")}");
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
    }
}
