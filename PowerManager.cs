using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;

namespace SleepMngr
{
    public class PowerManager
    {
        private static string logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr",
            "sleep_log.txt");
        
        private static void Log(string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(logFile);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                    
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                File.AppendAllText(logFile, logMessage + Environment.NewLine);
                Debug.WriteLine(logMessage);
            }
            catch { }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const int MONITOR_OFF = 2;
        
        private static IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [FlagsAttribute]
        private enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private static bool isPreventingSleep = false;

        public static void PreventSleep()
        {
            if (!isPreventingSleep)
            {
                SetThreadExecutionState(
                    EXECUTION_STATE.ES_CONTINUOUS |
                    EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                isPreventingSleep = true;
            }
        }

        public static void AllowSleep()
        {
            if (isPreventingSleep)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isPreventingSleep = false;
            }
        }

        public static bool IsPreventingSleep()
        {
            return isPreventingSleep;
        }

        public static bool GoToSleep()
        {
            Log("=== GoToSleep called ===");
            
            // Проверяем тип спящего режима
            bool isModernStandby = IsModernStandby();
            Log($"Sleep type detected: {(isModernStandby ? "Modern Standby (S0 Low Power Idle)" : "Classic Sleep (S3)")}");
            
            // Для Modern Standby используем выключение дисплея
            if (isModernStandby)
            {
                Log("Modern Standby detected - will turn off display instead of calling SetSuspendState");
                Log("System will automatically enter S0 Low Power Idle when display is off");
                
                // Снимаем блокировку чтобы система могла войти в S0
                bool wasPreventingSleep = isPreventingSleep;
                if (wasPreventingSleep)
                {
                    Log("Releasing sleep prevention lock to allow S0 Low Power Idle");
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    isPreventingSleep = false;
                }
                
                // Выключаем дисплей
                bool success = TurnOffDisplay();
                
                if (!success && wasPreventingSleep)
                {
                    // Восстанавливаем блокировку если не сработало
                    SetThreadExecutionState(
                        EXECUTION_STATE.ES_CONTINUOUS |
                        EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                        EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                    isPreventingSleep = true;
                }
                
                return success;
            }
            
            // Классический режим - тестируем все методы
            Log("Classic Sleep (S3) mode - testing all SetSuspendState methods");
            
            // Проверяем что может блокировать сон
            CheckSleepBlockers();
            
            // ВАЖНО: Полностью снимаем блокировку от самой программы
            bool wasPreventingSleepClassic = isPreventingSleep;
            if (wasPreventingSleepClassic)
            {
                Log("Releasing sleep prevention lock from SleepMngr");
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isPreventingSleep = false;
                
                // КРИТИЧНО: Ждем чтобы система обработала снятие блокировки
                Log("Waiting 2 seconds for system to process unlock...");
                System.Threading.Thread.Sleep(2000);
                Log("Lock released, system ready for sleep");
            }
            
            bool anySuccess = false;
            
            // Метод 1: SetSuspendState(false, true, false) - forceCritical=true
            try
            {
                Log("Trying Method 1: SetSuspendState(hibernate=false, forceCritical=true, disableWakeEvent=false)");
                bool result = SetSuspendState(false, true, false);
                Log($"Method 1 result: {result}");
                if (result) anySuccess = true;
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 1 exception: {ex.Message}");
            }
            
            // Метод 2: SetSuspendState(false, false, false) - обычный
            try
            {
                Log("Trying Method 2: SetSuspendState(hibernate=false, forceCritical=false, disableWakeEvent=false)");
                bool result = SetSuspendState(false, false, false);
                Log($"Method 2 result: {result}");
                if (result) anySuccess = true;
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 2 exception: {ex.Message}");
            }
            
            // Метод 3: SetSuspendState(false, true, true) - forceCritical + disableWakeEvent
            try
            {
                Log("Trying Method 3: SetSuspendState(hibernate=false, forceCritical=true, disableWakeEvent=true)");
                bool result = SetSuspendState(false, true, true);
                Log($"Method 3 result: {result}");
                if (result) anySuccess = true;
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 3 exception: {ex.Message}");
            }
            
            // Метод 4: Application.SetSuspendState с force=true
            try
            {
                Log("Trying Method 4: Application.SetSuspendState(Suspend, force=true, disableWakeEvent=false)");
                Application.SetSuspendState(PowerState.Suspend, true, false);
                Log("Method 4: Command executed (no exception)");
                anySuccess = true;
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 4 exception: {ex.Message}");
            }
            
            // Метод 5: Application.SetSuspendState с force=false
            try
            {
                Log("Trying Method 5: Application.SetSuspendState(Suspend, force=false, disableWakeEvent=false)");
                Application.SetSuspendState(PowerState.Suspend, false, false);
                Log("Method 5: Command executed (no exception)");
                anySuccess = true;
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 5 exception: {ex.Message}");
            }

            // Метод 6: rundll32.exe с Suspend
            try
            {
                Log("Trying Method 6: rundll32.exe powrprof.dll,SetSuspendState Suspend");
                var psi = new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "powrprof.dll,SetSuspendState Suspend",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    Log($"Method 6: Process started (PID: {process.Id})");
                    anySuccess = true;
                }
                else
                {
                    Log("Method 6: Process.Start returned null");
                }
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 6 exception: {ex.Message}");
            }
            
            // Метод 7: rundll32.exe с числовыми параметрами 0,1,0
            try
            {
                Log("Trying Method 7: rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
                var psi = new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "powrprof.dll,SetSuspendState 0,1,0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    Log($"Method 7: Process started (PID: {process.Id})");
                    anySuccess = true;
                }
                else
                {
                    Log("Method 7: Process.Start returned null");
                }
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 7 exception: {ex.Message}");
            }
            
            // Метод 8: cmd.exe + rundll32
            try
            {
                Log("Trying Method 8: cmd.exe /c rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    Log($"Method 8: Process started (PID: {process.Id})");
                    anySuccess = true;
                }
                else
                {
                    Log("Method 8: Process.Start returned null");
                }
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 8 exception: {ex.Message}");
            }

            // Метод 9: PowerShell с Add-Type
            try
            {
                Log("Trying Method 9: PowerShell with Add-Type");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Add-Type -Assembly System.Windows.Forms; [System.Windows.Forms.Application]::SetSuspendState('Suspend', $false, $false)\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    Log($"Method 9: Process started (PID: {process.Id})");
                    anySuccess = true;
                }
                else
                {
                    Log("Method 9: Process.Start returned null");
                }
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 9 exception: {ex.Message}");
            }
            
            // Метод 10: PowerShell с прямым P/Invoke
            try
            {
                Log("Trying Method 10: PowerShell with P/Invoke");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"$signature = '[DllImport(\\\"powrprof.dll\\\", SetLastError = true)] public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);'; Add-Type -MemberDefinition $signature -Name PowerManager -Namespace Win32; [Win32.PowerManager]::SetSuspendState($false, $true, $false)\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    Log($"Method 10: Process started (PID: {process.Id})");
                    anySuccess = true;
                }
                else
                {
                    Log("Method 10: Process.Start returned null");
                }
                System.Threading.Thread.Sleep(300);
            }
            catch (Exception ex) 
            { 
                Log($"Method 10 exception: {ex.Message}");
            }

            Log("=== ALL METHODS TESTED ===");
            Log($"Summary: anySuccess = {anySuccess}");
            Log("If computer did NOT sleep, all methods were blocked by system");
            
            // Восстанавливаем блокировку если была
            if (wasPreventingSleepClassic)
            {
                Log("Restoring sleep prevention lock");
                SetThreadExecutionState(
                    EXECUTION_STATE.ES_CONTINUOUS |
                    EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                isPreventingSleep = true;
            }
            
            return anySuccess;
        }
        
        public static string GetLogFile()
        {
            return logFile;
        }
        
        private static bool IsModernStandby()
        {
            try
            {
                // Проверяем через powercfg /a
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/a",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                
                var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    // Если есть S0 Low Power Idle и нет S3 - это Modern Standby
                    bool hasS0 = output.Contains("S0") || output.Contains("Low Power Idle");
                    bool hasS3 = output.Contains("Ждущий режим (S3)") && !output.Contains("не поддерживает");
                    
                    return hasS0 && !hasS3;
                }
            }
            catch { }
            
            return false;
        }
        
        private static bool TurnOffDisplay()
        {
            try
            {
                Log("Turning off display (Modern Standby compatible)");
                SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(MONITOR_OFF));
                Log("Display off command sent - system will enter S0 Low Power Idle automatically");
                return true;
            }
            catch (Exception ex)
            {
                Log($"TurnOffDisplay failed: {ex.Message}");
                return false;
            }
        }
        
        private static void CheckSleepBlockers()
        {
            try
            {
                Log("=== Checking for sleep blockers ===");
                
                // Проверяем через GetSystemPowerStatus
                Log("Checking system power status...");
                
                // Проверяем активные процессы которые могут блокировать
                var blockingProcesses = new List<string>();
                
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        string name = proc.ProcessName.ToLower();
                        // Известные процессы блокирующие сон
                        if (name.Contains("teams") || name.Contains("zoom") || 
                            name.Contains("skype") || name.Contains("discord") ||
                            name.Contains("steam") || name.Contains("epic") ||
                            name.Contains("slack") || name.Contains("chrome") ||
                            name.Contains("firefox"))
                        {
                            blockingProcesses.Add(proc.ProcessName);
                        }
                    }
                    catch { }
                }
                
                if (blockingProcesses.Count > 0)
                {
                    Log($"Found {blockingProcesses.Count} potentially blocking processes:");
                    foreach (var p in blockingProcesses)
                    {
                        Log($"  - {p}");
                    }
                }
                else
                {
                    Log("No obvious blocking processes found");
                }
            }
            catch (Exception ex)
            {
                Log($"CheckSleepBlockers error: {ex.Message}");
            }
        }
        
        private static void CheckPowercfgRequests()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/requests",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    Log("=== powercfg /requests output ===");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        // Записываем только первые 1000 символов чтобы не переполнить лог
                        string shortOutput = output.Length > 1000 ? output.Substring(0, 1000) + "..." : output;
                        Log(shortOutput);
                    }
                    else
                    {
                        Log("No active requests blocking sleep");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"CheckPowercfgRequests error: {ex.Message}");
            }
        }
    }
}
