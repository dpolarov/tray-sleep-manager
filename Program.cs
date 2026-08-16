using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SleepMngr
{
    static class Program
    {
        private static Mutex? _mutex;
        private const string MutexName = "Global\\SleepMngr_SingleInstance";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
                return AppSelfTest.Run();

            bool ownsMutex = false;

            try
            {
                _mutex = new Mutex(true, MutexName, out ownsMutex);

                if (!ownsMutex)
                {
                    _mutex.Dispose();
                    _mutex = null;

                    KillExistingInstances();
                    Thread.Sleep(500);

                    _mutex = new Mutex(true, MutexName, out ownsMutex);
                    if (!ownsMutex)
                    {
                        _mutex.Dispose();
                        _mutex = null;
                        return 0;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Register before TrayApplicationContext subscribes to the same
                // Windows event. Event handlers run in subscription order, so the
                // WMI cache is invalidated before the tray UI re-checks monitors.
                MonitorDetector.StartWatchingDisplayChanges();
                try
                {
                    Application.Run(new TrayApplicationContext());
                }
                finally
                {
                    MonitorDetector.StopWatchingDisplayChanges();
                }

                return 0;
            }
            finally
            {
                if (_mutex != null)
                {
                    if (ownsMutex)
                    {
                        try { _mutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }

                    _mutex.Dispose();
                    _mutex = null;
                }
            }
        }

        private static void KillExistingInstances()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    using (process)
                    {
                        if (process.Id == currentProcess.Id)
                            continue;

                        try
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
