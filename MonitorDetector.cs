using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Management;
using System.Drawing;
using Microsoft.Win32;

namespace SleepMngr
{
    public class MonitorDetector
    {
        // WMI is relatively expensive and this detector is polled every two seconds.
        // Cache WMI-derived data briefly, but invalidate it immediately on a Windows
        // display-settings event so plug/unplug changes do not wait for the TTL.
        private static readonly object CacheLock = new();
        private static readonly TimeSpan WmiCacheDuration = TimeSpan.FromSeconds(10);
        private static int? cachedTotalMonitorCount;
        private static DateTime totalMonitorCountCachedAtUtc = DateTime.MinValue;
        private static List<string>? cachedMonitorFriendlyNames;
        private static DateTime monitorFriendlyNamesCachedAtUtc = DateTime.MinValue;
        private static ActiveConnectionState? cachedActiveConnectionState;
        private static DateTime activeConnectionStateCachedAtUtc = DateTime.MinValue;
        private static int cacheGeneration;
        private static bool displayChangeWatcherStarted;

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

        private readonly struct ActiveConnectionState
        {
            public ActiveConnectionState(bool dataAvailable, bool hasExternal, bool hasInternal)
            {
                DataAvailable = dataAvailable;
                HasExternal = hasExternal;
                HasInternal = hasInternal;
            }

            public bool DataAvailable { get; }
            public bool HasExternal { get; }
            public bool HasInternal { get; }
        }

        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

        // D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY values used by WmiMonitorConnectionParams.
        private const uint VIDEO_OUTPUT_LVDS = 6;
        private const uint VIDEO_OUTPUT_DISPLAYPORT_EMBEDDED = 11;
        private const uint VIDEO_OUTPUT_UDI_EMBEDDED = 13;
        private const uint VIDEO_OUTPUT_INTERNAL = 0x80000000;
        private const uint VIDEO_OUTPUT_UNINITIALIZED = 0xFFFFFFFE;

        public static void StartWatchingDisplayChanges()
        {
            lock (CacheLock)
            {
                if (displayChangeWatcherStarted)
                    return;

                try
                {
                    SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                    displayChangeWatcherStarted = true;
                }
                catch
                {
                    // The 10-second cache still works if SystemEvents is unavailable.
                }
            }
        }

        public static void StopWatchingDisplayChanges()
        {
            lock (CacheLock)
            {
                if (!displayChangeWatcherStarted)
                    return;

                try
                {
                    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                }
                catch { }

                displayChangeWatcherStarted = false;
            }
        }

        public static void InvalidateWmiCache()
        {
            lock (CacheLock)
            {
                cacheGeneration++;
                cachedTotalMonitorCount = null;
                totalMonitorCountCachedAtUtc = DateTime.MinValue;
                cachedMonitorFriendlyNames = null;
                monitorFriendlyNamesCachedAtUtc = DateTime.MinValue;
                cachedActiveConnectionState = null;
                activeConnectionStateCachedAtUtc = DateTime.MinValue;
            }
        }

        private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            InvalidateWmiCache();
        }

        public static bool HasExternalMonitor()
        {
            // Prefer Windows' connection technology. This correctly handles clamshell
            // mode where the built-in panel disappears and only one external display
            // remains active (for example HDMI/DisplayPort).
            ActiveConnectionState connectionState = GetActiveConnectionState();
            if (connectionState.DataAvailable)
            {
                if (connectionState.HasExternal)
                    return true;

                // If Windows explicitly reports only an internal active connection,
                // do not let stale/phantom PnP monitor records create a false positive.
                if (connectionState.HasInternal && GetMonitorCount() == 1)
                    return false;
            }

            int totalMonitors = GetTotalMonitorCountWMI();
            int activeCount = GetMonitorCount();

            if (activeCount > 1)
                return true;

            // WMI/PnP counts can be 1 in clamshell mode. Always classify the single
            // active display instead of doing so only when WMI returned zero.
            if (activeCount == 1 && IsExternalMonitorActive())
                return true;

            // Keep the old count-based behavior only as a fallback when connection
            // technology is unavailable and no single-display classification worked.
            return !connectionState.DataAvailable && totalMonitors >= 2;
        }

        public static bool IsExternalOnlyActive()
        {
            if (GetMonitorCount() != 1)
                return false;

            ActiveConnectionState connectionState = GetActiveConnectionState();
            if (connectionState.DataAvailable &&
                (connectionState.HasExternal || connectionState.HasInternal))
            {
                return connectionState.HasExternal && !connectionState.HasInternal;
            }

            return IsExternalMonitorActive();
        }

        private static ActiveConnectionState GetActiveConnectionState()
        {
            int generation;
            DateTime nowUtc = DateTime.UtcNow;

            lock (CacheLock)
            {
                if (cachedActiveConnectionState.HasValue &&
                    nowUtc - activeConnectionStateCachedAtUtc < WmiCacheDuration)
                {
                    return cachedActiveConnectionState.Value;
                }

                generation = cacheGeneration;
            }

            ActiveConnectionState state = QueryActiveConnectionStateWmi();

            lock (CacheLock)
            {
                if (generation == cacheGeneration)
                {
                    cachedActiveConnectionState = state;
                    activeConnectionStateCachedAtUtc = DateTime.UtcNow;
                }
            }

            return state;
        }

        private static ActiveConnectionState QueryActiveConnectionStateWmi()
        {
            try
            {
                bool sawActiveConnection = false;
                bool hasExternal = false;
                bool hasInternal = false;

                var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) };
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT Active, VideoOutputTechnology FROM WmiMonitorConnectionParams");
                searcher.Options = options;

                using var results = searcher.Get();
                foreach (ManagementObject monitor in results)
                {
                    using (monitor)
                    {
                        try
                        {
                            bool active = monitor["Active"] is bool value && value;
                            if (!active)
                                continue;

                            sawActiveConnection = true;
                            uint technology = Convert.ToUInt32(monitor["VideoOutputTechnology"]);

                            if (IsInternalOutputTechnology(technology))
                            {
                                hasInternal = true;
                            }
                            else if (technology != VIDEO_OUTPUT_UNINITIALIZED)
                            {
                                // OTHER (-1), VGA, DVI, HDMI, external DisplayPort,
                                // Miracast, indirect wired displays, etc. are external.
                                hasExternal = true;
                            }
                        }
                        catch { }
                    }
                }

                return new ActiveConnectionState(sawActiveConnection, hasExternal, hasInternal);
            }
            catch
            {
                return new ActiveConnectionState(false, false, false);
            }
        }

        private static bool IsInternalOutputTechnology(uint technology)
        {
            return technology == VIDEO_OUTPUT_LVDS ||
                   technology == VIDEO_OUTPUT_DISPLAYPORT_EMBEDDED ||
                   technology == VIDEO_OUTPUT_UDI_EMBEDDED ||
                   technology == VIDEO_OUTPUT_INTERNAL;
        }
        
        private static bool IsExternalMonitorActive()
        {
            try
            {
                var displays = GetActiveDisplays();
                if (displays.Count != 1)
                    return false;

                var display = displays[0];
                string deviceString = display.DeviceString.ToLowerInvariant();
                string friendlyName = display.FriendlyName.ToLowerInvariant();

                // Known internal-panel EDID vendor prefixes. These names frequently
                // contain model digits too, so the generic model-number heuristic must
                // not classify them as external.
                string[] internalPrefixes = { "boe", "auo", "cmn", "cso", "lgd", "sdc", "shp", "inx" };
                foreach (string prefix in internalPrefixes)
                {
                    if (friendlyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Common external monitor names and EDID vendor prefixes. PHL is the
                // EDID manufacturer code used by many Philips monitors.
                string[] externalMarkers =
                {
                    "dell", "del ", "hp ", "hwp", "samsung", "sam ", "lg ", "gsm",
                    "acer", "acr", "asus", "aci", "benq", "bnq", "philips", "phl",
                    "aoc", "viewsonic", "vsc", "lenovo", "len ", "msi"
                };

                foreach (string marker in externalMarkers)
                {
                    if (friendlyName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                        deviceString.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                bool genericDevice =
                    string.IsNullOrWhiteSpace(deviceString) ||
                    deviceString.Contains("generic pnp monitor") ||
                    deviceString.Contains("generic monitor") ||
                    deviceString.StartsWith("\\\\.\\display");

                bool genericFriendlyName =
                    string.IsNullOrWhiteSpace(friendlyName) ||
                    friendlyName == "generic" ||
                    friendlyName.Contains("generic pnp monitor");

                if (genericDevice && genericFriendlyName)
                    return false;

                bool hasModelNumber =
                    System.Text.RegularExpressions.Regex.IsMatch(friendlyName, @"[a-z]\d{2}") ||
                    System.Text.RegularExpressions.Regex.IsMatch(friendlyName, @"\d{2}[a-z]");

                return hasModelNumber;
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
            int generation;
            DateTime nowUtc = DateTime.UtcNow;

            lock (CacheLock)
            {
                if (cachedTotalMonitorCount.HasValue &&
                    nowUtc - totalMonitorCountCachedAtUtc < WmiCacheDuration)
                {
                    return cachedTotalMonitorCount.Value;
                }

                generation = cacheGeneration;
            }

            int count = QueryTotalMonitorCountWMI();

            lock (CacheLock)
            {
                // Do not repopulate the cache with stale data if a display-change
                // event invalidated it while the WMI query was running.
                if (generation == cacheGeneration)
                {
                    cachedTotalMonitorCount = count;
                    totalMonitorCountCachedAtUtc = DateTime.UtcNow;
                }
            }

            return count;
        }

        private static int QueryTotalMonitorCountWMI()
        {
            try
            {
                // Пробуем через Win32_PnPEntity - видит все устройства, даже отключенные
                int count = 0;
                var wmiOptions = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) };
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Monitor' OR Caption LIKE '%Monitor%' OR Caption LIKE '%Display%')"))
                {
                    searcher.Options = wmiOptions;
                    using var results = searcher.Get();
                    foreach (ManagementObject device in results)
                    {
                        using (device)
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
                }
                
                // Если нашли мониторы, возвращаем
                if (count > 0)
                    return count;
                
                // Иначе пробуем через WmiMonitorID
                using (var searcher = new ManagementObjectSearcher("root\\WMI", 
                    "SELECT * FROM WmiMonitorID"))
                {
                    searcher.Options = wmiOptions;
                    using var results = searcher.Get();
                    foreach (ManagementObject monitor in results)
                    {
                        using (monitor)
                        {
                            count++;
                        }
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
            int generation;
            DateTime nowUtc = DateTime.UtcNow;

            lock (CacheLock)
            {
                if (cachedMonitorFriendlyNames != null &&
                    nowUtc - monitorFriendlyNamesCachedAtUtc < WmiCacheDuration)
                {
                    return new List<string>(cachedMonitorFriendlyNames);
                }

                generation = cacheGeneration;
            }

            List<string> names = QueryAllMonitorFriendlyNamesWMI();

            lock (CacheLock)
            {
                if (generation == cacheGeneration)
                {
                    cachedMonitorFriendlyNames = new List<string>(names);
                    monitorFriendlyNamesCachedAtUtc = DateTime.UtcNow;
                }
            }

            return names;
        }

        private static List<string> QueryAllMonitorFriendlyNamesWMI()
        {
            var names = new List<string>();
            
            try
            {
                var nameOptions = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) };
                using (var searcher = new ManagementObjectSearcher("root\\WMI", 
                    "SELECT * FROM WmiMonitorID"))
                {
                    searcher.Options = nameOptions;
                    using var results = searcher.Get();
                    foreach (ManagementObject monitor in results)
                    {
                        using (monitor)
                        {
                            // Получаем UserFriendlyName
                            var userFriendlyName = monitor["UserFriendlyName"] as ushort[];
                            if (userFriendlyName != null && userFriendlyName.Length > 0)
                            {
                                var nameBuilder = new StringBuilder();
                                foreach (ushort c in userFriendlyName)
                                {
                                    if (c == 0) break;
                                    nameBuilder.Append((char)c);
                                }
                                
                                string name = nameBuilder.ToString();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    names.Add(name.Trim());
                                }
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
