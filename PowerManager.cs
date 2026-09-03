using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
                string? dir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const int MONITOR_OFF = 2;
        private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

        [Flags]
        private enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private static bool isPreventingSleep;
        private static Timer? pendingClamshellSleepTimer;

        public static void PreventSleep()
        {
            CancelPendingClamshellSleep();

            if (isPreventingSleep)
                return;

            SetThreadExecutionState(
                EXECUTION_STATE.ES_CONTINUOUS |
                EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
            isPreventingSleep = true;
        }

        public static void AllowSleep()
        {
            bool releasedProtection = false;

            if (isPreventingSleep)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isPreventingSleep = false;
                releasedProtection = true;
            }

            if (releasedProtection && MonitorDetector.IsExternalOnlyActive())
                ScheduleClamshellSleep();
            else if (!releasedProtection)
                CancelPendingClamshellSleep();
        }

        private static void ScheduleClamshellSleep()
        {
            CancelPendingClamshellSleep();
            Log("Sleep protection released while only an external display is active; waiting 3 seconds for Windows power/lid state to settle before using the same sleep engine as Sleep now");

            var timer = new Timer { Interval = 3000 };
            pendingClamshellSleepTimer = timer;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();

                if (ReferenceEquals(pendingClamshellSleepTimer, timer))
                    pendingClamshellSleepTimer = null;

                if (isPreventingSleep)
                {
                    Log("Scheduled clamshell sleep cancelled: sleep prevention became active again");
                    return;
                }

                if (!MonitorDetector.IsExternalOnlyActive())
                {
                    Log("Scheduled clamshell sleep cancelled: display topology changed before execution");
                    return;
                }

                Log("Executing the same GoToSleep engine used by Sleep now after clamshell power-state stabilization");
                if (!GoToSleep())
                    Log("Clamshell mode sleep command reported failure");
            };
            timer.Start();
        }

        private static void CancelPendingClamshellSleep()
        {
            if (pendingClamshellSleepTimer == null)
                return;

            try
            {
                pendingClamshellSleepTimer.Stop();
                pendingClamshellSleepTimer.Dispose();
            }
            catch { }

            pendingClamshellSleepTimer = null;
        }

        public static bool IsPreventingSleep() => isPreventingSleep;

        public static bool GoToSleep()
        {
            Log("=== GoToSleep called ===");

            bool isModernStandby = IsModernStandby();
            Log($"Sleep type detected: {(isModernStandby ? "Modern Standby (S0 Low Power Idle)" : "Classic Sleep (S3)")}");

            if (isModernStandby)
            {
                Log("Modern Standby detected - will turn off display instead of calling SetSuspendState");
                Log("System will automatically enter S0 Low Power Idle when display is off");

                bool wasPreventingSleep = isPreventingSleep;
                if (wasPreventingSleep)
                {
                    Log("Releasing sleep prevention lock to allow S0 Low Power Idle");
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    isPreventingSleep = false;
                }

                bool success = TurnOffDisplay();
                if (!success && wasPreventingSleep)
                {
                    SetThreadExecutionState(
                        EXECUTION_STATE.ES_CONTINUOUS |
                        EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                        EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                    isPreventingSleep = true;
                }

                return success;
            }

            Log("Classic Sleep (S3) mode - testing all SetSuspendState methods");
            CheckSleepBlockers();
            CheckPowercfgRequests();

            bool wasPreventingSleepClassic = isPreventingSleep;
            if (wasPreventingSleepClassic)
            {
                Log("Releasing sleep prevention lock from SleepMngr");
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isPreventingSleep = false;
                Log("Waiting 2 seconds for system to process unlock...");
                System.Threading.Thread.Sleep(2000);
                Log("Lock released, system ready for sleep");
            }

            bool anySuccess = false;

            anySuccess |= TryNativeSuspend(
                "Method 1: SetSuspendState(hibernate=false, forceCritical=true, disableWakeEvent=false)",
                false, true, false);
            PauseBetweenFallbacks();

            anySuccess |= TryNativeSuspend(
                "Method 2: SetSuspendState(hibernate=false, forceCritical=false, disableWakeEvent=false)",
                false, false, false);
            PauseBetweenFallbacks();

            anySuccess |= TryNativeSuspend(
                "Method 3: SetSuspendState(hibernate=false, forceCritical=true, disableWakeEvent=true)",
                false, true, true);
            PauseBetweenFallbacks();

            anySuccess |= TryApplicationSuspend(
                "Method 4: Application.SetSuspendState(Suspend, force=true, disableWakeEvent=false)",
                true);
            PauseBetweenFallbacks();

            anySuccess |= TryApplicationSuspend(
                "Method 5: Application.SetSuspendState(Suspend, force=false, disableWakeEvent=false)",
                false);
            PauseBetweenFallbacks();

            anySuccess |= TryProcessFallback(
                "Method 6: rundll32.exe powrprof.dll,SetSuspendState Suspend",
                "rundll32.exe",
                "powrprof.dll,SetSuspendState Suspend");
            PauseBetweenFallbacks();

            anySuccess |= TryProcessFallback(
                "Method 7: rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
                "rundll32.exe",
                "powrprof.dll,SetSuspendState 0,1,0");
            PauseBetweenFallbacks();

            anySuccess |= TryProcessFallback(
                "Method 8: cmd.exe /c rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
                "cmd.exe",
                "/c rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
            PauseBetweenFallbacks();

            anySuccess |= TryProcessFallback(
                "Method 9: PowerShell with Add-Type",
                "powershell.exe",
                "-NoProfile -Command \"Add-Type -Assembly System.Windows.Forms; [System.Windows.Forms.Application]::SetSuspendState('Suspend', $false, $false)\"");
            PauseBetweenFallbacks();

            anySuccess |= TryProcessFallback(
                "Method 10: PowerShell with P/Invoke",
                "powershell.exe",
                "-NoProfile -Command \"$signature = '[DllImport(\\\"powrprof.dll\\\", SetLastError = true)] public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);'; Add-Type -MemberDefinition $signature -Name PowerManager -Namespace Win32; [Win32.PowerManager]::SetSuspendState($false, $true, $false)\"");
            PauseBetweenFallbacks();

            Log("=== ALL METHODS TESTED ===");
            Log($"Summary: anySuccess = {anySuccess}");
            Log("If computer did NOT sleep, all methods were blocked by system");

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

        private static bool TryNativeSuspend(string description, bool hibernate, bool forceCritical, bool disableWakeEvent)
        {
            try
            {
                Log($"Trying {description}");
                bool result = SetSuspendState(hibernate, forceCritical, disableWakeEvent);
                Log($"{description} result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"{description} exception: {ex.Message}");
                return false;
            }
        }

        private static bool TryApplicationSuspend(string description, bool force)
        {
            try
            {
                Log($"Trying {description}");
                Application.SetSuspendState(PowerState.Suspend, force, false);
                Log($"{description}: command executed (no exception)");
                return true;
            }
            catch (Exception ex)
            {
                Log($"{description} exception: {ex.Message}");
                return false;
            }
        }

        private static bool TryProcessFallback(string description, string fileName, string arguments)
        {
            try
            {
                Log($"Trying {description}");
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    Log($"{description}: Process.Start returned null");
                    return false;
                }

                Log($"{description}: process started (PID: {process.Id})");
                return true;
            }
            catch (Exception ex)
            {
                Log($"{description} exception: {ex.Message}");
                return false;
            }
        }

        private static void PauseBetweenFallbacks()
        {
            System.Threading.Thread.Sleep(300);
        }

        public static string GetLogFile() => logFile;

        private static bool IsModernStandby()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/a",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                bool hasS0 = output.Contains("S0", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("Low Power Idle", StringComparison.OrdinalIgnoreCase);
                bool hasS3Available =
                    (output.Contains("Standby (S3)", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("Ждущий режим (S3)", StringComparison.OrdinalIgnoreCase)) &&
                    !output.Contains("not available", StringComparison.OrdinalIgnoreCase) &&
                    !output.Contains("не поддерживает", StringComparison.OrdinalIgnoreCase);

                return hasS0 && !hasS3Available;
            }
            catch
            {
                return false;
            }
        }

        private static bool TurnOffDisplay()
        {
            try
            {
                Log("Turning off display (Modern Standby compatible)");
                bool posted = PostMessage(
                    HWND_BROADCAST,
                    WM_SYSCOMMAND,
                    new IntPtr(SC_MONITORPOWER),
                    new IntPtr(MONITOR_OFF));

                if (!posted)
                {
                    int error = Marshal.GetLastWin32Error();
                    Log($"TurnOffDisplay failed: PostMessage returned false (Win32 error {error})");
                    return false;
                }

                Log("Display off command posted asynchronously - system can enter S0 Low Power Idle without blocking SleepMngr");
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
                var blockingProcesses = new List<string>();

                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            string name = process.ProcessName.ToLowerInvariant();
                            if (name.Contains("teams") || name.Contains("zoom") ||
                                name.Contains("skype") || name.Contains("discord") ||
                                name.Contains("steam") || name.Contains("epic") ||
                                name.Contains("slack") || name.Contains("chrome") ||
                                name.Contains("firefox"))
                            {
                                blockingProcesses.Add(process.ProcessName);
                            }
                        }
                        catch { }
                    }
                }

                if (blockingProcesses.Count == 0)
                {
                    Log("No obvious blocking processes found");
                    return;
                }

                Log($"Found {blockingProcesses.Count} potentially blocking processes:");
                foreach (string name in blockingProcesses)
                    Log($"  - {name}");
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
                var result = PowerCfgRunner.Run("/requests");
                Log("=== powercfg /requests output ===");

                if (!result.Started || result.TimedOut)
                {
                    Log(result.DescribeFailure());
                    return;
                }

                string output = result.Output;
                if (string.IsNullOrWhiteSpace(output))
                {
                    Log("No active requests blocking sleep");
                    return;
                }

                string shortOutput = output.Length > 1000 ? output[..1000] + "..." : output;
                Log(shortOutput);
            }
            catch (Exception ex)
            {
                Log($"CheckPowercfgRequests error: {ex.Message}");
            }
        }
    }
}
