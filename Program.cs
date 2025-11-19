using System;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;

namespace SleepMngr
{
    static class Program
    {
        private static Mutex? _mutex = null;
        private const string MutexName = "Global\\SleepMngr_SingleInstance";

        [STAThread]
        static void Main()
        {
            // Проверка на единственный экземпляр
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Программа уже запущена - закрываем старый экземпляр
                KillExistingInstances();
                
                // Ждем немного чтобы старый процесс закрылся
                Thread.Sleep(500);
                
                // Создаем новый mutex
                _mutex = new Mutex(true, MutexName, out createdNew);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());

            // Освобождаем mutex при выходе
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }

        private static void KillExistingInstances()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    // Не убиваем текущий процесс
                    if (process.Id != currentProcess.Id)
                    {
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
