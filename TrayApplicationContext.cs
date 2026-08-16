using System;
using System.Drawing;
using System.Media;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SleepMngr
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly Timer monitorCheckTimer;
        private readonly Timer autoSleepTimer;
        private readonly Icon blueIcon;
        private readonly Icon yellowIcon;
        private readonly Icon darkBlueIcon;
        private readonly Icon darkYellowIcon;
        private readonly LidActionManager lidActionManager = new LidActionManager();

        private bool hasExternalMonitor;
        private bool wasLidClosed;
        private bool isDisplayOff;
        private bool isLidClosed;
        private DateTime? displayOffTime;
        private WorkMode currentMode = WorkMode.Auto;
        private ToolStripMenuItem statusMenuItem = null!;
        private ToolStripMenuItem mouseWakeMenuItem = null!;
        private bool autoSleepTriggered;
        private bool wasClamshellMode;
        private DateTime? externalDisconnectedTime;
        private bool isUpdatingMonitors;
        private bool isExiting;

        public TrayApplicationContext()
        {
            blueIcon = IconGenerator.CreateBlueIcon();
            yellowIcon = IconGenerator.CreateYellowIcon();
            darkBlueIcon = IconGenerator.CreateDarkBlueIcon();
            darkYellowIcon = IconGenerator.CreateDarkYellowIcon();

            trayIcon = new NotifyIcon
            {
                Icon = blueIcon,
                Visible = true,
                Text = "SleepMngr"
            };
            trayIcon.ContextMenuStrip = CreateContextMenu();
            trayIcon.DoubleClick += OnTrayIconDoubleClick;

            monitorCheckTimer = new Timer { Interval = 2000 };
            monitorCheckTimer.Tick += OnMonitorCheckTimerTick;
            monitorCheckTimer.Start();

            autoSleepTimer = new Timer { Interval = 1000 };
            autoSleepTimer.Tick += OnAutoSleepTimerTick;
            autoSleepTimer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            hasExternalMonitor = MonitorDetector.HasExternalMonitor();
            CheckLidState();
            UpdateDisplayState();
            UpdatePowerState();
        }

        private static string T(string russian, string english) => Localization.T(russian, english);

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            statusMenuItem = new ToolStripMenuItem(T("Статус", "Status"));
            statusMenuItem.Click += OnStatusClick;
            menu.Items.Add(statusMenuItem);
            menu.Items.Add(new ToolStripSeparator());

            var modeItem = new ToolStripMenuItem(T("Режим работы", "Operating mode"));
            var autoModeItem = new ToolStripMenuItem(T("🔄 Автоматически", "🔄 Automatic"));
            autoModeItem.Click += (_, _) => SetMode(WorkMode.Auto);
            modeItem.DropDownItems.Add(autoModeItem);

            var alwaysPreventItem = new ToolStripMenuItem(T("🟠 Всегда не засыпать", "🟠 Always prevent sleep"));
            alwaysPreventItem.Click += (_, _) => SetMode(WorkMode.AlwaysPrevent);
            modeItem.DropDownItems.Add(alwaysPreventItem);

            var alwaysAllowItem = new ToolStripMenuItem(T("🔷 Всегда засыпать", "🔷 Always allow sleep"));
            alwaysAllowItem.Click += (_, _) => SetMode(WorkMode.AlwaysAllow);
            modeItem.DropDownItems.Add(alwaysAllowItem);
            menu.Items.Add(modeItem);

            menu.Items.Add(new ToolStripSeparator());

            var restoreItem = new ToolStripMenuItem(T("Восстановить настройки крышки", "Restore lid settings"));
            restoreItem.Click += OnRestoreLidSettingsClick;
            menu.Items.Add(restoreItem);

            mouseWakeMenuItem = new ToolStripMenuItem(T("🖱️ Мышь не будит", "🖱️ Mouse does not wake"))
            {
                CheckOnClick = true,
                Checked = WakeManager.IsMouseWakeDisabled
            };
            mouseWakeMenuItem.Click += OnMouseWakeClick;
            menu.Items.Add(mouseWakeMenuItem);

            var languageItem = new ToolStripMenuItem(T("🌐 Язык", "🌐 Language"));
            var russianItem = new ToolStripMenuItem("Русский")
            {
                Checked = Localization.CurrentLanguage == AppLanguage.Russian
            };
            russianItem.Click += (_, _) => SetLanguage(AppLanguage.Russian);
            languageItem.DropDownItems.Add(russianItem);

            var englishItem = new ToolStripMenuItem("English")
            {
                Checked = Localization.CurrentLanguage == AppLanguage.English
            };
            englishItem.Click += (_, _) => SetLanguage(AppLanguage.English);
            languageItem.DropDownItems.Add(englishItem);
            menu.Items.Add(languageItem);

            menu.Items.Add(new ToolStripSeparator());

            var sleepNowItem = new ToolStripMenuItem(T("💤 Заснуть сейчас", "💤 Sleep now"));
            sleepNowItem.Click += OnSleepNowClick;
            menu.Items.Add(sleepNowItem);

            menu.Items.Add(new ToolStripSeparator());

            var openLogItem = new ToolStripMenuItem(T("📋 Открыть лог", "📋 Open log"));
            openLogItem.Click += OnOpenLogClick;
            menu.Items.Add(openLogItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem(T("Выход", "Exit"));
            exitItem.Click += OnExitClick;
            menu.Items.Add(exitItem);

            menu.Opening += (_, _) => UpdateMenuCheckmarks(menu);
            UpdateMenuCheckmarks(menu);
            return menu;
        }

        private void SetLanguage(AppLanguage language)
        {
            if (Localization.CurrentLanguage == language)
                return;

            Localization.SetLanguage(language);
            var previousMenu = trayIcon.ContextMenuStrip;
            trayIcon.ContextMenuStrip = CreateContextMenu();
            previousMenu?.Dispose();
            UpdatePowerState();
        }

        private void UpdateMenuCheckmarks(ContextMenuStrip menu)
        {
            string modeText = currentMode switch
            {
                WorkMode.Auto => T("🔄 Авто", "🔄 Auto"),
                WorkMode.AlwaysPrevent => T("🟠 Не засыпать", "🟠 Prevent sleep"),
                WorkMode.AlwaysAllow => T("🔷 Засыпать", "🔷 Allow sleep"),
                _ => "?"
            };

            string stateText = PowerManager.IsPreventingSleep()
                ? T("Защита активна", "Protection active")
                : T("Сон разрешен", "Sleep allowed");
            statusMenuItem.Text = $"{T("Статус", "Status")}: {modeText} • {stateText}";
            mouseWakeMenuItem.Checked = WakeManager.IsMouseWakeDisabled;

            if (menu.Items.Count > 2 && menu.Items[2] is ToolStripMenuItem modeMenuItem)
            {
                foreach (ToolStripMenuItem item in modeMenuItem.DropDownItems)
                    item.Checked = false;

                int checkedIndex = (int)currentMode;
                if (checkedIndex >= 0 && checkedIndex < modeMenuItem.DropDownItems.Count)
                    ((ToolStripMenuItem)modeMenuItem.DropDownItems[checkedIndex]).Checked = true;
            }
        }

        private void SetMode(WorkMode mode)
        {
            if (currentMode == mode)
                return;

            currentMode = mode;
            if (mode == WorkMode.Auto)
                hasExternalMonitor = MonitorDetector.HasExternalMonitor();

            UpdatePowerState();
        }

        private void OnMonitorCheckTimerTick(object? sender, EventArgs e)
        {
            UpdateMonitorStatus();
            UpdateDisplayState();
            CheckLidState();
        }

        private void OnAutoSleepTimerTick(object? sender, EventArgs e)
        {
            if (currentMode != WorkMode.Auto || autoSleepTriggered)
                return;

            if (isLidClosed && isDisplayOff && displayOffTime.HasValue &&
                (DateTime.Now - displayOffTime.Value).TotalSeconds >= 10)
            {
                TriggerAutoSleep();
                return;
            }

            if (externalDisconnectedTime.HasValue && !hasExternalMonitor &&
                (DateTime.Now - externalDisconnectedTime.Value).TotalSeconds >= 3)
            {
                externalDisconnectedTime = null;
                TriggerAutoSleep();
            }
        }

        private void UpdateDisplayState()
        {
            try
            {
                bool currentDisplayOff = MonitorDetector.GetMonitorCount() == 0;
                if (currentDisplayOff == isDisplayOff)
                    return;

                isDisplayOff = currentDisplayOff;
                displayOffTime = isDisplayOff ? DateTime.Now : null;
            }
            catch { }
        }

        private void TriggerAutoSleep()
        {
            if (autoSleepTriggered || isExiting)
                return;

            autoSleepTriggered = true;
            displayOffTime = null;

            if (!PowerManager.GoToSleep())
                System.Diagnostics.Debug.WriteLine("Failed to trigger auto sleep");
        }

        private void UpdateMonitorStatus()
        {
            if (isUpdatingMonitors)
                return;

            isUpdatingMonitors = true;
            try
            {
                bool currentHasExternalMonitor = MonitorDetector.HasExternalMonitor();

                if (currentHasExternalMonitor)
                    wasClamshellMode = MonitorDetector.GetMonitorCount() <= 1;

                if (currentHasExternalMonitor == hasExternalMonitor)
                    return;

                bool wasExternal = hasExternalMonitor;
                hasExternalMonitor = currentHasExternalMonitor;
                UpdatePowerState();

                if (currentHasExternalMonitor)
                {
                    autoSleepTriggered = false;
                    externalDisconnectedTime = null;
                }

                if (wasExternal && !currentHasExternalMonitor && currentMode == WorkMode.Auto && wasClamshellMode)
                    externalDisconnectedTime = DateTime.Now;
            }
            finally
            {
                isUpdatingMonitors = false;
            }
        }

        private void UpdatePowerState()
        {
            bool shouldPreventSleep;
            string modeText;
            Icon selectedIcon;

            switch (currentMode)
            {
                case WorkMode.Auto:
                    shouldPreventSleep = hasExternalMonitor;
                    if (shouldPreventSleep)
                    {
                        int count = MonitorDetector.GetMonitorCount();
                        modeText = T($"Авто - Активен ({count} монитора)", $"Auto - Active ({count} monitor(s))");
                        selectedIcon = yellowIcon;
                    }
                    else
                    {
                        modeText = T("Авто - Неактивен", "Auto - Inactive");
                        selectedIcon = blueIcon;
                    }
                    break;

                case WorkMode.AlwaysPrevent:
                    shouldPreventSleep = true;
                    modeText = T("Всегда не засыпать", "Always prevent sleep");
                    selectedIcon = darkYellowIcon;
                    break;

                case WorkMode.AlwaysAllow:
                    shouldPreventSleep = false;
                    modeText = T("Всегда засыпать", "Always allow sleep");
                    selectedIcon = darkBlueIcon;
                    break;

                default:
                    shouldPreventSleep = false;
                    modeText = string.Empty;
                    selectedIcon = blueIcon;
                    break;
            }

            bool wasPreventingSleep = PowerManager.IsPreventingSleep();
            if (shouldPreventSleep)
            {
                PowerManager.PreventSleep();
                lidActionManager.SetLidActionDoNothing();
            }
            else
            {
                PowerManager.AllowSleep();
                lidActionManager.RestoreLidAction();
            }

            trayIcon.Icon = selectedIcon;
            trayIcon.Text = $"Sleep Manager - {modeText}";

            if (wasPreventingSleep != shouldPreventSleep)
                PlayStatusChangeSound(shouldPreventSleep);
        }

        private static void PlayStatusChangeSound(bool isProtectionActive)
        {
            try
            {
                if (isProtectionActive)
                    SystemSounds.Asterisk.Play();
                else
                    SystemSounds.Hand.Play();
            }
            catch { }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    wasLidClosed = true;
                    break;
                case PowerModes.Resume:
                    if (wasLidClosed)
                    {
                        wasLidClosed = false;
                        UpdateMonitorStatus();
                    }
                    break;
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (isExiting)
                return;

            UpdateMonitorStatus();
            UpdateDisplayState();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (isExiting)
                return;

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    var timer = new Timer { Interval = 500 };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        timer.Dispose();
                        if (!isExiting)
                            CheckLidState();
                    };
                    timer.Start();
                    break;

                case SessionSwitchReason.SessionUnlock:
                    isLidClosed = false;
                    displayOffTime = null;
                    autoSleepTriggered = false;
                    break;
            }
        }

        private void CheckLidState()
        {
            try
            {
                int monitorCount = MonitorDetector.GetMonitorCount();
                int attachedCount = MonitorDetector.GetAttachedDisplayCount();

                if (monitorCount == 0)
                    isLidClosed = true;
                else if (attachedCount >= 2 && monitorCount == 1)
                    isLidClosed = true;
                else if (monitorCount == 1 && attachedCount == 1)
                    isLidClosed = false;
                else if (monitorCount > 1)
                    isLidClosed = !IsBuiltInDisplayActive();
                else
                    isLidClosed = false;
            }
            catch { }
        }

        private static bool IsBuiltInDisplayActive()
        {
            try
            {
                foreach (var display in MonitorDetector.GetActiveDisplays())
                {
                    string deviceString = display.DeviceString.ToLowerInvariant();
                    string friendlyName = display.FriendlyName.ToLowerInvariant();
                    if (deviceString.Contains("generic") ||
                        deviceString.Contains("pnp") ||
                        deviceString.Contains("lcd") ||
                        friendlyName.Contains("lcd") ||
                        friendlyName.Contains("laptop") ||
                        display.DeviceName.Contains("DISPLAY1"))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void OnTrayIconDoubleClick(object? sender, EventArgs e) => ShowStatus();
        private void OnStatusClick(object? sender, EventArgs e) => ShowStatus();

        private void OnRestoreLidSettingsClick(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                T(
                    "Принудительно восстановить настройки крышки на 'Сон'?\n\nЭто установит действие при закрытии крышки:\n- От сети: Сон\n- От батареи: Сон",
                    "Force lid-close settings back to 'Sleep'?\n\nThis will set the lid-close action to:\n- Plugged in: Sleep\n- On battery: Sleep"),
                T("Восстановление настроек", "Restore settings"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            if (lidActionManager.ForceRestoreToSleep())
            {
                SystemSounds.Asterisk.Play();
                MessageBox.Show(
                    T(
                        "Настройки крышки успешно восстановлены.\nТеперь ноутбук будет засыпать при закрытии крышки.",
                        "Lid settings restored successfully.\nThe laptop will now sleep when the lid is closed."),
                    T("Успешно", "Success"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                SystemSounds.Hand.Play();
                MessageBox.Show(
                    T(
                        "Не удалось восстановить настройки.\nПопробуйте запустить программу от имени администратора.",
                        "Could not restore the settings.\nTry running the application as administrator."),
                    T("Ошибка", "Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnMouseWakeClick(object? sender, EventArgs e)
        {
            bool shouldDisable = mouseWakeMenuItem.Checked;
            if (shouldDisable)
            {
                var mice = WakeManager.GetWakeArmedMouseDevices();
                if (mice.Count == 0)
                {
                    MessageBox.Show(
                        T(
                            "Не найдено мышиных устройств, которые могут будить систему.\n\nВозможно, мышь уже отключена от пробуждения.",
                            "No mouse devices capable of waking the system were found.\n\nMouse wake may already be disabled."),
                        T("Информация", "Information"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    WakeManager.DisableMouseWake();
                    return;
                }

                if (WakeManager.DisableMouseWake())
                    SystemSounds.Asterisk.Play();
                else
                {
                    mouseWakeMenuItem.Checked = false;
                    SystemSounds.Hand.Play();
                    MessageBox.Show(
                        T(
                            "Не удалось отключить пробуждение мышью.\nПопробуйте запустить программу от имени администратора.",
                            "Could not disable mouse wake.\nTry running the application as administrator."),
                        T("Ошибка", "Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            else if (WakeManager.EnableMouseWake())
            {
                SystemSounds.Asterisk.Play();
            }
            else
            {
                mouseWakeMenuItem.Checked = true;
                SystemSounds.Hand.Play();
            }
        }

        private void OnOpenLogClick(object? sender, EventArgs e)
        {
            try
            {
                string logFile = PowerManager.GetLogFile();
                if (!System.IO.File.Exists(logFile))
                {
                    string? directory = System.IO.Path.GetDirectoryName(logFile);
                    if (!string.IsNullOrEmpty(directory))
                        System.IO.Directory.CreateDirectory(directory);

                    System.IO.File.WriteAllText(
                        logFile,
                        T(
                            $"Лог создан: {DateTime.Now}\r\nФайл лога будет содержать информацию о попытках перехода в спящий режим.\r\n\r\n",
                            $"Log created: {DateTime.Now}\r\nThis log contains information about attempts to enter sleep mode.\r\n\r\n"));

                    MessageBox.Show(
                        T(
                            $"Файл лога создан:\n{logFile}\n\nПосле использования функции 'Заснуть сейчас' здесь появится информация о попытках перехода в сон.",
                            $"Log file created:\n{logFile}\n\nAfter using 'Sleep now', sleep attempts will be recorded here."),
                        T("Лог", "Log"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{logFile}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SystemSounds.Hand.Play();
                MessageBox.Show(
                    T($"Не удалось открыть файл лога.\n\nОшибка: {ex.Message}", $"Could not open the log file.\n\nError: {ex.Message}"),
                    T("Ошибка", "Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnSleepNowClick(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                T(
                    "Компьютер сейчас уйдет в спящий режим.\n\n⚠️ Рекомендация: Закройте Slack, Teams, Chrome\nдля надежного перехода в сон.\n\nПродолжить?",
                    "The computer will enter sleep mode now.\n\n⚠️ Recommendation: close Slack, Teams, and Chrome\nfor a more reliable transition to sleep.\n\nContinue?"),
                T("Подтверждение", "Confirmation"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            if (!PowerManager.GoToSleep())
            {
                SystemSounds.Hand.Play();
                string logFile = PowerManager.GetLogFile();
                var errorResult = MessageBox.Show(
                    T(
                        $"Не удалось перевести компьютер в спящий режим.\n\nВозможные причины:\n- Заблокировано групповыми политиками Windows\n- Открыты приложения, блокирующие сон\n- Требуются права администратора\n\nЛог ошибок сохранен в:\n{logFile}\n\nОткрыть файл лога?",
                        $"Could not put the computer to sleep.\n\nPossible reasons:\n- Blocked by Windows Group Policy\n- Applications are preventing sleep\n- Administrator rights are required\n\nError log saved to:\n{logFile}\n\nOpen the log file?"),
                    T("Ошибка", "Error"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error);

                if (errorResult == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "notepad.exe",
                            Arguments = $"\"{logFile}\"",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private void ShowStatus()
        {
            int activeCount = MonitorDetector.GetMonitorCount();
            int attachedCount = MonitorDetector.GetAttachedDisplayCount();
            bool hasExternal = MonitorDetector.HasExternalMonitor();
            bool preventing = PowerManager.IsPreventingSleep();
            bool lidSettingsModified = lidActionManager.IsModified;

            string modeText = currentMode switch
            {
                WorkMode.Auto => T("🔄 Автоматически", "🔄 Automatic"),
                WorkMode.AlwaysPrevent => T("🟡 Всегда не засыпать", "🟡 Always prevent sleep"),
                WorkMode.AlwaysAllow => T("🔵 Всегда засыпать", "🔵 Always allow sleep"),
                _ => T("Неизвестно", "Unknown")
            };

            var sb = new StringBuilder();
            sb.AppendLine($"{T("Режим работы", "Operating mode")}: {modeText}");
            sb.AppendLine();
            sb.AppendLine($"{T("Внешний монитор", "External monitor")}: {(hasExternal ? T("Подключен ✓", "Connected ✓") : T("Не подключен ✗", "Not connected ✗"))}");
            sb.AppendLine($"{T("Активных дисплеев", "Active displays")}: {activeCount}");
            sb.AppendLine($"{T("Физически подключенных мониторов", "Physically connected monitors")}: {attachedCount}");
            sb.AppendLine();

            var displays = MonitorDetector.GetActiveDisplays();
            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                sb.AppendLine($"{T("Дисплей", "Display")} {i + 1}: {display.FriendlyName}");
                sb.AppendLine($"  {T("Разрешение", "Resolution")}: {display.Bounds}");
                sb.AppendLine($"  {T("Основной", "Primary")}: {(display.IsPrimary ? T("Да", "Yes") : T("Нет", "No"))}");
            }

            if (displays.Count > 0)
                sb.AppendLine();

            sb.AppendLine($"{T("Предотвращение сна", "Sleep prevention")}: {(preventing ? T("Активно ✓", "Active ✓") : T("Неактивно ✗", "Inactive ✗"))}");
            sb.AppendLine($"{T("Настройки крышки", "Lid settings")}: {(lidSettingsModified ? T("Ничего не делать", "Do nothing") : T("Сон", "Sleep"))}");
            sb.AppendLine();
            sb.AppendLine(T("Логика работы:", "How it works:"));
            sb.AppendLine(T("• Авто: определяется по наличию внешнего монитора", "• Auto: determined by the presence of an external monitor"));
            sb.AppendLine(T("• Всегда не засыпать: крышка не влияет на работу", "• Always prevent sleep: closing the lid does not put the laptop to sleep"));
            sb.AppendLine(T("• Всегда засыпать: ноутбук засыпает при закрытии крышки", "• Always allow sleep: the laptop sleeps when the lid is closed"));

            MessageBox.Show(sb.ToString(), "Lid Sleep Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            if (isExiting)
                return;

            isExiting = true;

            monitorCheckTimer.Stop();
            autoSleepTimer.Stop();

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            bool currentHasExternalMonitor = MonitorDetector.HasExternalMonitor();
            if (currentHasExternalMonitor)
            {
                PowerManager.PreventSleep();
                lidActionManager.SetLidActionDoNothing();
            }
            else
            {
                PowerManager.AllowSleep();
                lidActionManager.RestoreLidAction();
            }

            if (WakeManager.IsMouseWakeDisabled)
                WakeManager.EnableMouseWake();

            trayIcon.DoubleClick -= OnTrayIconDoubleClick;
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();

            monitorCheckTimer.Tick -= OnMonitorCheckTimerTick;
            autoSleepTimer.Tick -= OnAutoSleepTimerTick;
            monitorCheckTimer.Dispose();
            autoSleepTimer.Dispose();

            blueIcon.Dispose();
            yellowIcon.Dispose();
            darkBlueIcon.Dispose();
            darkYellowIcon.Dispose();

            Application.Exit();
        }
    }
}
