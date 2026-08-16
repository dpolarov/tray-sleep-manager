using System;

namespace SleepMngr
{
    internal static class AppSelfTest
    {
        public static int Run()
        {
            bool originalLogging = AppLog.Enabled;

            try
            {
                // Exercise the real non-zero exit path without changing any power setting.
                var invalidCommand = PowerCfgRunner.Run("/sleepmngr-self-test-invalid-option");
                if (!invalidCommand.Started || invalidCommand.Success)
                {
                    AppLog.Write("SelfTest", "Expected invalid powercfg command to fail safely");
                    return 10;
                }

                // Simulate the common non-elevated failure deterministically. The lid
                // manager must report failure and remain usable instead of throwing.
                var accessDenied = new PowerCfgResult
                {
                    Started = true,
                    ExitCode = 5,
                    Error = "Access is denied."
                };

                var lidManager = new LidActionManager(_ => accessDenied);
                if (lidManager.SetLidActionDoNothing())
                {
                    AppLog.Write("SelfTest", "LidActionManager unexpectedly accepted an access-denied result");
                    return 20;
                }

                if (string.IsNullOrWhiteSpace(lidManager.LastError))
                {
                    AppLog.Write("SelfTest", "LidActionManager did not retain diagnostic details");
                    return 21;
                }

                // Verify that the persisted logging switch can be changed both ways.
                AppLog.SetEnabled(false);
                if (AppLog.Enabled)
                    return 30;

                AppLog.SetEnabled(true);
                if (!AppLog.Enabled)
                    return 31;

                AppLog.SetEnabled(originalLogging);

                // Read-only subsystem probes: these may return empty data on a CI VM,
                // but they must not crash the executable.
                _ = WakeManager.GetWakeArmedDevices();
                _ = MonitorDetector.GetMonitorCount();
                _ = Localization.T("Тест", "Test");

                AppLog.Write("SelfTest", "PASS: powercfg failure path and logging toggle are non-fatal");
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Write("SelfTest", $"FAIL: unhandled exception: {ex}");
                return 99;
            }
            finally
            {
                if (AppLog.Enabled != originalLogging)
                    AppLog.SetEnabled(originalLogging);
            }
        }
    }
}
