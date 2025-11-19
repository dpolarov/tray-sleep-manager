using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Media;
using Microsoft.Win32;

namespace SleepMngr
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Timer monitorCheckTimer;
        private Timer autoSleepTimer;
        private bool hasExternalMonitor = false;
        private bool wasLidClosed = false;
        private bool isDisplayOff = false;
        private bool isLidClosed = false;
        private DateTime? displayOffTime = null;
        private Icon blueIcon;
        private Icon yellowIcon;
        private Icon darkBlueIcon;
        private Icon darkYellowIcon;
        private WorkMode currentMode = WorkMode.Auto;
        private LidActionManager lidActionManager = new LidActionManager();
        private ToolStripMenuItem statusMenuItem = null!;

        public TrayApplicationContext()
        {
            // Create colored icons
            blueIcon = IconGenerator.CreateBlueIcon();           // Авто + засыпать
            yellowIcon = IconGenerator.CreateYellowIcon();       // Авто + не засыпать
            darkBlueIcon = IconGenerator.CreateDarkBlueIcon();   // Ручной засыпать
            darkYellowIcon = IconGenerator.CreateDarkYellowIcon(); // Ручной не засыпать

            // Initialize tray icon
            trayIcon = new NotifyIcon()
            {
                Icon = blueIcon,
                ContextMenuStrip = CreateContextMenu(),
                Visible = true,
                Text = "SleepMngr"
            };

            trayIcon.DoubleClick += OnTrayIconDoubleClick;

            // Initialize timer to check monitor status
            monitorCheckTimer = new Timer();
            monitorCheckTimer.Interval = 2000; // Check every 2 seconds
            monitorCheckTimer.Tick += OnMonitorCheckTimerTick;
            monitorCheckTimer.Start();

            // Initialize timer for auto-sleep functionality
            autoSleepTimer = new Timer();
            autoSleepTimer.Interval = 1000; // Check every 1 second
            autoSleepTimer.Tick += OnAutoSleepTimerTick;
            autoSleepTimer.Start();

            // Subscribe to system events
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            // Initial check - сразу проверяем мониторы и применяем настройки
            hasExternalMonitor = MonitorDetector.HasExternalMonitor();
            CheckLidState();
            UpdateDisplayState();
            UpdatePowerState();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            statusMenuItem = new ToolStripMenuItem("Статус");
            statusMenuItem.Click += OnStatusClick;
            menu.Items.Add(statusMenuItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            // Mode submenu
            var modeItem = new ToolStripMenuItem("Режим работы");
            
            var autoModeItem = new ToolStripMenuItem("🔄 Автоматически");
            autoModeItem.Click += (s, e) => SetMode(WorkMode.Auto);
            autoModeItem.Checked = true;
            modeItem.DropDownItems.Add(autoModeItem);
            
            var alwaysPreventItem = new ToolStripMenuItem("🟠 Всегда не засыпать");
            alwaysPreventItem.Click += (s, e) => SetMode(WorkMode.AlwaysPrevent);
            modeItem.DropDownItems.Add(alwaysPreventItem);
            
            var alwaysAllowItem = new ToolStripMenuItem("🔷 Всегда засыпать");
            alwaysAllowItem.Click += (s, e) => SetMode(WorkMode.AlwaysAllow);
            modeItem.DropDownItems.Add(alwaysAllowItem);
            
            menu.Items.Add(modeItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            var restoreItem = new ToolStripMenuItem("Восстановить настройки крышки");
            restoreItem.Click += OnRestoreLidSettingsClick;
            menu.Items.Add(restoreItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            var sleepNowItem = new ToolStripMenuItem("💤 Заснуть сейчас");
            sleepNowItem.Click += OnSleepNowClick;
            menu.Items.Add(sleepNowItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            var openLogItem = new ToolStripMenuItem("📋 Открыть лог");
            openLogItem.Click += OnOpenLogClick;
            menu.Items.Add(openLogItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += OnExitClick;
            menu.Items.Add(exitItem);

            // Update checkmarks when menu opens
            menu.Opening += (s, e) => UpdateMenuCheckmarks(menu);

            return menu;
        }
        
        private void UpdateMenuCheckmarks(ContextMenuStrip menu)
        {
            // Обновляем текст статуса с текущим режимом и состоянием
            string modeText = currentMode switch
            {
                WorkMode.Auto => "🔄 Авто",
                WorkMode.AlwaysPrevent => "🟠 Не засыпать",
                WorkMode.AlwaysAllow => "🔷 Засыпать",
                _ => "?"
            };
            
            string stateText = PowerManager.IsPreventingSleep() ? "Защита активна" : "Сон разрешен";
            statusMenuItem.Text = $"Статус: {modeText} • {stateText}";
            
            var modeMenuItem = menu.Items[2] as ToolStripMenuItem;
            if (modeMenuItem != null)
            {
                foreach (ToolStripMenuItem item in modeMenuItem.DropDownItems)
                {
                    item.Checked = false;
                }
                
                int checkedIndex = (int)currentMode;
                if (checkedIndex >= 0 && checkedIndex < modeMenuItem.DropDownItems.Count)
                {
                    ((ToolStripMenuItem)modeMenuItem.DropDownItems[checkedIndex]).Checked = true;
                }
            }
        }
        
        private void SetMode(WorkMode mode)
        {
            if (currentMode != mode)
            {
                currentMode = mode;
                
                // При переключении в автоматический режим - принудительно применяем настройки
                if (mode == WorkMode.Auto)
                {
                    hasExternalMonitor = MonitorDetector.HasExternalMonitor();
                }
                
                UpdatePowerState();
            }
        }

        private void OnMonitorCheckTimerTick(object? sender, EventArgs e)
        {
            UpdateMonitorStatus();
            UpdateDisplayState();
        }

        private void OnAutoSleepTimerTick(object? sender, EventArgs e)
        {
            // Проверяем условия для автоматического сна
            // Только в автоматическом режиме
            if (currentMode != WorkMode.Auto)
                return;

            // Проверяем: крышка закрыта И дисплей выключен
            if (isLidClosed && isDisplayOff && displayOffTime.HasValue)
            {
                // Вычисляем сколько времени прошло с момента выключения дисплея
                TimeSpan elapsed = DateTime.Now - displayOffTime.Value;
                
                if (elapsed.TotalSeconds >= 10)
                {
                    // Прошло 10 секунд - отправляем в сон
                    TriggerAutoSleep();
                }
            }
        }

        private void UpdateDisplayState()
        {
            try
            {
                // Проверяем количество активных мониторов
                int activeMonitors = MonitorDetector.GetMonitorCount();
                bool currentDisplayOff = (activeMonitors == 0);

                // Если состояние изменилось
                if (currentDisplayOff != isDisplayOff)
                {
                    isDisplayOff = currentDisplayOff;
                    
                    if (isDisplayOff)
                    {
                        // Дисплей только что выключился - запоминаем время
                        displayOffTime = DateTime.Now;
                    }
                    else
                    {
                        // Дисплей включился - сбрасываем таймер
                        displayOffTime = null;
                    }
                }
            }
            catch { }
        }

        private void TriggerAutoSleep()
        {
            // Отправляем ноутбук в спящий режим
            bool success = PowerManager.GoToSleep();
            
            // Сбрасываем таймеры в любом случае
            displayOffTime = null;
            
            if (!success)
            {
                // Логируем ошибку, но не показываем пользователю
                System.Diagnostics.Debug.WriteLine("Failed to trigger auto sleep");
            }
        }

        private void UpdateMonitorStatus()
        {
            bool currentHasExternalMonitor = MonitorDetector.HasExternalMonitor();
            
            if (currentHasExternalMonitor != hasExternalMonitor)
            {
                hasExternalMonitor = currentHasExternalMonitor;
                UpdatePowerState();
            }
        }

        private void UpdatePowerState()
        {
            bool shouldPreventSleep = false;
            string modeText = "";
            Icon selectedIcon;
            
            switch (currentMode)
            {
                case WorkMode.Auto:
                    shouldPreventSleep = hasExternalMonitor;
                    if (shouldPreventSleep)
                    {
                        modeText = $"Авто - Активен ({MonitorDetector.GetMonitorCount()} монитора)";
                        selectedIcon = yellowIcon; // 🟡 Желтая - авто + не засыпать
                    }
                    else
                    {
                        modeText = "Авто - Неактивен";
                        selectedIcon = blueIcon; // 🔵 Синяя - авто + засыпать
                    }
                    break;
                    
                case WorkMode.AlwaysPrevent:
                    shouldPreventSleep = true;
                    modeText = "Всегда не засыпать";
                    selectedIcon = darkYellowIcon; // 🟤 Темно-желтая - ручной не засыпать
                    break;
                    
                case WorkMode.AlwaysAllow:
                    shouldPreventSleep = false;
                    modeText = "Всегда засыпать";
                    selectedIcon = darkBlueIcon; // 🔷 Темно-синяя - ручной засыпать
                    break;
                    
                default:
                    shouldPreventSleep = false;
                    selectedIcon = blueIcon;
                    break;
            }
            
            // Запоминаем предыдущее состояние для звука
            bool wasPreventingSleep = PowerManager.IsPreventingSleep();
            
            // Применяем настройки
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
            
            // Устанавливаем иконку в соответствии с режимом
            trayIcon.Icon = selectedIcon;
            trayIcon.Text = $"Sleep Manager - {modeText}";
            
            // Воспроизводим звук при изменении состояния
            if (wasPreventingSleep != shouldPreventSleep)
            {
                PlayStatusChangeSound(shouldPreventSleep);
            }
        }
        
        private void PlayStatusChangeSound(bool isProtectionActive)
        {
            try
            {
                if (isProtectionActive)
                {
                    // Защита включена - звук "Asterisk" (информация)
                    SystemSounds.Asterisk.Play();
                }
                else
                {
                    // Защита выключена - звук "Hand" (предупреждение)
                    SystemSounds.Hand.Play();
                }
            }
            catch
            {
                // Игнорируем ошибки воспроизведения звука
            }
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
            UpdateMonitorStatus();
            UpdateDisplayState();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            // Отслеживаем события сессии для определения состояния крышки
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    // Сессия заблокирована - может быть из-за закрытия крышки
                    // Дополнительно проверяем через задержку
                    Task.Delay(500).ContinueWith(_ => CheckLidState());
                    break;
                    
                case SessionSwitchReason.SessionUnlock:
                    // Сессия разблокирована - крышка открыта
                    isLidClosed = false;
                    displayOffTime = null;
                    break;
            }
        }

        private void CheckLidState()
        {
            try
            {
                // Проверяем количество мониторов
                int monitorCount = MonitorDetector.GetMonitorCount();
                int attachedCount = MonitorDetector.GetAttachedDisplayCount();
                
                // Случай 1: Нет активных мониторов вообще - крышка закрыта и дисплей выключен
                if (monitorCount == 0)
                {
                    isLidClosed = true;
                }
                // Случай 2: Есть внешние мониторы (физически подключено >= 2)
                // но активен только 1 - вероятно крышка закрыта
                else if (attachedCount >= 2 && monitorCount == 1)
                {
                    isLidClosed = true;
                }
                // Случай 3: Один монитор и нет внешнего - встроенный активен, крышка открыта
                else if (monitorCount == 1 && attachedCount == 1)
                {
                    isLidClosed = false;
                }
                // Случай 4: Несколько активных мониторов - работаем с открытой или закрытой крышкой
                // Не можем точно определить, считаем что крышка может быть закрыта
                else if (monitorCount > 1)
                {
                    // Если было 2+ монитора и стал 1, возможно закрыли крышку
                    // Более детальная проверка через встроенный дисплей
                    isLidClosed = !IsBuiltInDisplayActive();
                }
                else
                {
                    isLidClosed = false;
                }
            }
            catch { }
        }

        private bool IsBuiltInDisplayActive()
        {
            try
            {
                // Получаем список всех активных дисплеев
                var displays = MonitorDetector.GetActiveDisplays();
                
                // Встроенный дисплей обычно имеет имя содержащее "Generic PnP Monitor"
                // или "Дисплей с разъемом Plug and Play" или "LCD"
                foreach (var display in displays)
                {
                    string deviceString = display.DeviceString.ToLower();
                    string friendlyName = display.FriendlyName.ToLower();
                    
                    // Проверяем признаки встроенного дисплея
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
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void OnTrayIconDoubleClick(object? sender, EventArgs e)
        {
            ShowStatus();
        }

        private void OnStatusClick(object? sender, EventArgs e)
        {
            ShowStatus();
        }
        
        private void OnRestoreLidSettingsClick(object? sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Принудительно восстановить настройки крышки на 'Сон'?\n\n" +
                "Это установит действие при закрытии крышки:\n" +
                "- От сети: Сон\n" +
                "- От батареи: Сон",
                "Восстановление настроек",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            
            if (result == DialogResult.Yes)
            {
                if (lidActionManager.ForceRestoreToSleep())
                {
                    SystemSounds.Asterisk.Play();
                    MessageBox.Show(
                        "Настройки крышки успешно восстановлены.\n" +
                        "Теперь ноутбук будет засыпать при закрытии крышки.",
                        "Успешно",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    SystemSounds.Hand.Play();
                    MessageBox.Show(
                        "Не удалось восстановить настройки.\n" +
                        "Попробуйте запустить программу от имени администратора.",
                        "Ошибка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void OnOpenLogClick(object? sender, EventArgs e)
        {
            try
            {
                string logFile = PowerManager.GetLogFile();
                
                // Проверяем существует ли файл
                if (System.IO.File.Exists(logFile))
                {
                    // Открываем в Notepad
                    System.Diagnostics.Process.Start("notepad.exe", logFile);
                }
                else
                {
                    // Создаем папку и файл если их нет
                    string dir = System.IO.Path.GetDirectoryName(logFile);
                    if (!System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                    
                    System.IO.File.WriteAllText(logFile, 
                        $"Лог создан: {DateTime.Now}\r\n" +
                        $"Файл лога будет содержать информацию о попытках перехода в спящий режим.\r\n\r\n");
                    
                    MessageBox.Show(
                        $"Файл лога создан:\n{logFile}\n\n" +
                        "После использования функции 'Заснуть сейчас' здесь появится информация о попытках перехода в сон.",
                        "Лог",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    
                    System.Diagnostics.Process.Start("notepad.exe", logFile);
                }
            }
            catch (Exception ex)
            {
                SystemSounds.Hand.Play();
                MessageBox.Show(
                    $"Не удалось открыть файл лога.\n\n" +
                    $"Ошибка: {ex.Message}",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnSleepNowClick(object? sender, EventArgs e)
        {
            // Показываем предупреждение перед переходом в сон
            var result = MessageBox.Show(
                "Компьютер сейчас уйдет в спящий режим.\n\n" +
                "⚠️ Рекомендация: Закройте Slack, Teams, Chrome\n" +
                "для надежного перехода в сон.\n\n" +
                "Продолжить?",
                "Подтверждение",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            
            if (result == DialogResult.Yes)
            {
                // Даем время закрыть диалог
                System.Threading.Thread.Sleep(500);
                
                if (!PowerManager.GoToSleep())
                {
                    SystemSounds.Hand.Play();
                    string logFile = PowerManager.GetLogFile();
                    var errorResult = MessageBox.Show(
                        "Не удалось перевести компьютер в спящий режим.\n\n" +
                        "Возможные причины:\n" +
                        "- Заблокировано групповыми политиками Windows\n" +
                        "- Открыты приложения, блокирующие сон\n" +
                        "- Требуются права администратора\n\n" +
                        $"Лог ошибок сохранен в:\n{logFile}\n\n" +
                        "Открыть файл лога?",
                        "Ошибка",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Error);
                    
                    if (errorResult == DialogResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("notepad.exe", logFile);
                        }
                        catch { }
                    }
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

            string displayInfo = MonitorDetector.GetDisplaysDebugInfo();
            
            string modeText = currentMode switch
            {
                WorkMode.Auto => "🔄 Автоматически",
                WorkMode.AlwaysPrevent => "🟡 Всегда не засыпать",
                WorkMode.AlwaysAllow => "🔵 Всегда засыпать",
                _ => "Неизвестно"
            };

            string message = $"Режим работы: {modeText}\n\n" +
                           $"Внешний монитор: {(hasExternal ? "Подключен ✓" : "Не подключен ✗")}\n" +
                           $"Активных дисплеев: {activeCount}\n" +
                           $"Физически подключенных мониторов: {attachedCount}\n\n" +
                           $"{displayInfo}" +
                           $"Предотвращение сна: {(preventing ? "Активно ✓" : "Неактивно ✗")}\n" +
                           $"Настройки крышки: {(lidSettingsModified ? "Ничего не делать" : "Сон")}\n\n" +
                           $"Логика работы:\n" +
                           $"• Авто: определяется по наличию внешнего монитора\n" +
                           $"• Всегда не засыпать: крышка не влияет на работу\n" +
                           $"• Всегда засыпать: ноутбук засыпает при закрытии крышки";

            MessageBox.Show(message, "Lid Sleep Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            // Проверяем текущий статус мониторов перед выходом
            bool currentHasExternalMonitor = MonitorDetector.HasExternalMonitor();
            
            if (currentHasExternalMonitor)
            {
                // Внешний монитор подключен - оставляем "Ничего не делать"
                PowerManager.PreventSleep();
                lidActionManager.SetLidActionDoNothing();
            }
            else
            {
                // Внешнего монитора нет - восстанавливаем "Сон"
                PowerManager.AllowSleep();
                lidActionManager.RestoreLidAction();
            }

            // Cleanup
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            
            monitorCheckTimer.Stop();
            monitorCheckTimer.Dispose();
            
            autoSleepTimer.Stop();
            autoSleepTimer.Dispose();
            
            trayIcon.Visible = false;
            trayIcon.Dispose();

            // Dispose icons
            blueIcon?.Dispose();
            yellowIcon?.Dispose();
            darkBlueIcon?.Dispose();
            darkYellowIcon?.Dispose();

            Application.Exit();
        }
    }
}
