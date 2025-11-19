using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Management;
using System.Drawing;

namespace SleepMngr
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
            // ПРОСТАЯ ЛОГИКА: если физически подключено 2+ монитора - есть внешний
            int totalMonitors = GetTotalMonitorCountWMI();
            
            // Если WMI показывает 2+ монитора - точно есть внешний
            if (totalMonitors >= 2)
                return true;
            
            // Дополнительная проверка через активные дисплеи
            int activeCount = GetMonitorCount();
            
            // Если больше 1 активного дисплея - точно есть внешний
            if (activeCount > 1)
                return true;
            
            // Если 1 активный, но WMI недоступен - проверяем по названию
            if (totalMonitors == 0 && activeCount == 1)
            {
                return IsExternalMonitorActive();
            }
            
            return false;
        }
        
        private static bool IsExternalMonitorActive()
        {
            try
            {
                // Получаем список всех активных дисплеев
                var displays = GetActiveDisplays();
                
                if (displays.Count != 1)
                    return false;
                
                var display = displays[0];
                string deviceString = display.DeviceString.ToLower();
                string friendlyName = display.FriendlyName.ToLower();
                
                // Признаки встроенного дисплея:
                // 1. Название совпадает с устройством (например, "\\.\DISPLAY2" == "\\.\DISPLAY2")
                if (display.DeviceString == display.DeviceName || 
                    deviceString.StartsWith("\\\\.\\display"))
                {
                    return false; // Это встроенный дисплей
                }
                
                // 2. Содержит "Generic PnP Monitor" или просто "Generic"
                if (deviceString.Contains("generic pnp monitor") ||
                    deviceString.Contains("generic monitor") ||
                    (deviceString == "generic" || friendlyName == "generic"))
                {
                    return false; // Встроенный
                }
                
                // 3. Название пустое или очень короткое (меньше 5 символов)
                if (string.IsNullOrWhiteSpace(friendlyName) || friendlyName.Length < 5)
                {
                    return false; // Встроенный
                }
                
                // Признаки внешнего монитора:
                // 1. Имеет конкретное название производителя
                bool hasManufacturer = 
                    friendlyName.Contains("dell") || friendlyName.Contains("hp") ||
                    friendlyName.Contains("samsung") || friendlyName.Contains("lg") ||
                    friendlyName.Contains("acer") || friendlyName.Contains("asus") ||
                    friendlyName.Contains("benq") || friendlyName.Contains("philips") ||
                    friendlyName.Contains("aoc") || friendlyName.Contains("viewsonic") ||
                    friendlyName.Contains("lenovo");
                
                // 2. Имеет модель с цифрами (P24h-10, U2720Q и т.д.)
                bool hasModelNumber = 
                    System.Text.RegularExpressions.Regex.IsMatch(friendlyName, @"[a-z]\d{2}") ||
                    System.Text.RegularExpressions.Regex.IsMatch(friendlyName, @"\d{2}[a-z]");
                
                return hasManufacturer || hasModelNumber;
            }
            catch
            {
                return false;
            }
        }
        
        public static int GetAttachedDisplayCount()
        {
            return GetTotalMonitorCountWMI();
        }
        
        private static int GetTotalMonitorCountWMI()
        {
            try
            {
                // Пробуем через Win32_PnPEntity - видит все устройства, даже отключенные
                int count = 0;
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Monitor' OR Caption LIKE '%Monitor%' OR Caption LIKE '%Display%')"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        try
                        {
                            string caption = device["Caption"]?.ToString() ?? "";
                            string pnpClass = device["PNPClass"]?.ToString() ?? "";
                            
                            // Исключаем виртуальные мониторы и драйверы
                            if (!string.IsNullOrEmpty(caption) && 
                                !caption.Contains("Microsoft") &&
                                !caption.Contains("Remote") &&
                                !caption.Contains("Virtual") &&
                                (pnpClass == "Monitor" || caption.ToLower().Contains("display")))
                            {
                                count++;
                            }
                        }
                        catch { }
                    }
                }
                
                // Если нашли мониторы, возвращаем
                if (count > 0)
                    return count;
                
                // Иначе пробуем через WmiMonitorID
                using (var searcher = new ManagementObjectSearcher("root\\WMI", 
                    "SELECT * FROM WmiMonitorID"))
                {
                    foreach (var monitor in searcher.Get())
                    {
                        count++;
                    }
                }
                
                // Если WMI вернул 0, используем fallback
                if (count == 0)
                {
                    return GetAttachedDisplayCountFallback();
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
            var uniqueMonitors = new HashSet<string>();
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
                    
                    // Добавляем только уникальные мониторы с непустыми названиями
                    if (!string.IsNullOrEmpty(monitor.DeviceString) && 
                        !string.IsNullOrEmpty(monitor.DeviceID))
                    {
                        // Используем DeviceID как уникальный идентификатор
                        uniqueMonitors.Add(monitor.DeviceID);
                    }
                    
                    monNum++;
                }
                
                devNum++;
            }
            
            return uniqueMonitors.Count;
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
