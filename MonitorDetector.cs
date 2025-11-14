using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Management;
using System.Drawing;

namespace LidSleepManager
{
    public class MonitorDetector
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
        
        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
        
        private const int ENUM_CURRENT_SETTINGS = -1;

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
            
            // Если 1 дисплей - проверяем несколько условий
            if (activeCount == 1)
            {
                // Проверка 1: Если единственный активный дисплей - это DISPLAY2 или выше,
                // значит это внешний монитор (встроенный обычно DISPLAY1)
                var screens = Screen.AllScreens;
                if (screens.Length > 0)
                {
                    string deviceName = screens[0].DeviceName;
                    // Извлекаем номер дисплея (например, из "\\.\DISPLAY2" получаем "2")
                    if (deviceName.Contains("DISPLAY"))
                    {
                        // Ищем позицию "DISPLAY" и берем число после него
                        int displayIndex = deviceName.IndexOf("DISPLAY");
                        if (displayIndex >= 0)
                        {
                            string numStr = deviceName.Substring(displayIndex + 7); // "DISPLAY".Length = 7
                            if (int.TryParse(numStr, out int displayNum) && displayNum > 1)
                            {
                                // Активен DISPLAY2 или выше - это внешний монитор
                                return true;
                            }
                        }
                    }
                }
                
                // Проверка 2: Проверяем сколько всего мониторов подключено через WMI
                int totalMonitors = GetTotalMonitorCountWMI();
                
                // Если физически подключено >= 2 монитора, значит один из них внешний
                if (totalMonitors >= 2)
                    return true;
            }
            
            return false;
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
            
            // Получаем все названия мониторов из WMI заранее
            var monitorNames = GetAllMonitorFriendlyNames();
            int monitorIndex = 0;

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

                // Получаем реальное физическое разрешение через EnumDisplaySettings
                string resolution = GetActualResolution(deviceName);
                
                // Получаем дружественное название монитора из списка
                string friendlyName = monitorIndex < monitorNames.Count ? monitorNames[monitorIndex] : displayName;

                displays.Add(new DisplayInfo
                {
                    DeviceName = deviceName,
                    DeviceString = displayName,
                    FriendlyName = friendlyName,
                    IsPrimary = isPrimary,
                    IsActive = true,
                    Bounds = resolution
                });
                
                monitorIndex++;
            }

            return displays;
        }
        
        private static string GetActualResolution(string deviceName)
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                
                if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    return $"{dm.dmPelsWidth}x{dm.dmPelsHeight}";
                }
            }
            catch { }
            
            // Fallback - используем Screen.Bounds (может быть масштабированным)
            var screen = Array.Find(Screen.AllScreens, s => s.DeviceName == deviceName);
            if (screen != null)
            {
                return $"{screen.Bounds.Width}x{screen.Bounds.Height}";
            }
            
            return "Неизвестно";
        }
        
        private static List<string> GetAllMonitorFriendlyNames()
        {
            var names = new List<string>();
            
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", 
                    "SELECT * FROM WmiMonitorID"))
                {
                    foreach (ManagementObject monitor in searcher.Get())
                    {
                        // Получаем UserFriendlyName
                        var userFriendlyName = monitor["UserFriendlyName"] as ushort[];
                        if (userFriendlyName != null && userFriendlyName.Length > 0)
                        {
                            string name = "";
                            foreach (ushort c in userFriendlyName)
                            {
                                if (c == 0) break;
                                name += (char)c;
                            }
                            
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                names.Add(name.Trim());
                            }
                        }
                    }
                }
            }
            catch { }
            
            return names;
        }

        public class DisplayInfo
        {
            public string DeviceName { get; set; } = "";
            public string DeviceString { get; set; } = "";
            public string FriendlyName { get; set; } = "";
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
                
                // Показываем дружественное название если оно есть
                if (!string.IsNullOrEmpty(display.FriendlyName) && display.FriendlyName != display.DeviceString)
                {
                    sb.AppendLine($"  Модель: {display.FriendlyName}");
                    sb.AppendLine($"  Техническое название: {display.DeviceString}");
                }
                else
                {
                    sb.AppendLine($"  Название: {display.DeviceString}");
                }
                
                sb.AppendLine($"  Разрешение: {display.Bounds}");
                sb.AppendLine($"  Основной: {(display.IsPrimary ? "Да" : "Нет")}");
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
    }
}
