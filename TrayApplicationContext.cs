using System;
using System.Drawing;
using System.Windows.Forms;
using System.Media;
using Microsoft.Win32;

namespace LidSleepManager
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Timer monitorCheckTimer;
        private bool hasExternalMonitor = false;
        private bool wasLidClosed = false;
        private Icon blueIcon;
        private Icon yellowIcon;
        private Icon darkBlueIcon;
        private Icon darkYellowIcon;
        private WorkMode currentMode = WorkMode.Auto;
        private LidActionManager lidActionManager = new LidActionManager();

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
                Text = "Lid Sleep Manager"
            };

            trayIcon.DoubleClick += OnTrayIconDoubleClick;

            // Initialize timer to check monitor status
            monitorCheckTimer = new Timer();
            monitorCheckTimer.Interval = 2000; // Check every 2 seconds
            monitorCheckTimer.Tick += OnMonitorCheckTimerTick;
            monitorCheckTimer.Start();

            // Subscribe to system events
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // Initial check - сразу проверяем мониторы и применяем настройки
            hasExternalMonitor = MonitorDetector.HasExternalMonitor();
            UpdatePowerState();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            var statusItem = new ToolStripMenuItem("Статус");
            statusItem.Click += OnStatusClick;
            menu.Items.Add(statusItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            // Mode submenu
            var modeItem = new ToolStripMenuItem("Режим работы");
            
            var autoModeItem = new ToolStripMenuItem("🔄 Автоматически");
            autoModeItem.Click += (s, e) => SetMode(WorkMode.Auto);
            autoModeItem.Checked = true;
            modeItem.DropDownItems.Add(autoModeItem);
            
            var alwaysPreventItem = new ToolStripMenuItem("🟡 Всегда не засыпать");
            alwaysPreventItem.Click += (s, e) => SetMode(WorkMode.AlwaysPrevent);
            modeItem.DropDownItems.Add(alwaysPreventItem);
            
            var alwaysAllowItem = new ToolStripMenuItem("🔵 Всегда засыпать");
            alwaysAllowItem.Click += (s, e) => SetMode(WorkMode.AlwaysAllow);
            modeItem.DropDownItems.Add(alwaysAllowItem);
            
            menu.Items.Add(modeItem);
            
            menu.Items.Add(new ToolStripSeparator());
            
            var restoreItem = new ToolStripMenuItem("Восстановить настройки крышки");
            restoreItem.Click += OnRestoreLidSettingsClick;
            menu.Items.Add(restoreItem);
            
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
            trayIcon.Text = $"Lid Sleep Manager - {modeText}";
            
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
            
            monitorCheckTimer.Stop();
            monitorCheckTimer.Dispose();
            
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
