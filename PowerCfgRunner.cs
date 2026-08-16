using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SleepMngr
{
    internal sealed class PowerCfgResult
    {
        public bool Started { get; init; }
        public bool TimedOut { get; init; }
        public int? ExitCode { get; init; }
        public string Output { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
        public string? ExceptionMessage { get; init; }

        public bool Success => Started && !TimedOut && ExceptionMessage == null && ExitCode == 0;

        public string DescribeFailure()
        {
            if (!Started)
                return string.IsNullOrWhiteSpace(ExceptionMessage)
                    ? "powercfg.exe could not be started"
                    : $"powercfg.exe could not be started: {ExceptionMessage}";

            if (TimedOut)
                return "powercfg.exe timed out after 5 seconds";

            if (!string.IsNullOrWhiteSpace(ExceptionMessage))
                return $"powercfg.exe failed: {ExceptionMessage}";

            string details = !string.IsNullOrWhiteSpace(Error) ? Error.Trim() : Output.Trim();
            if (details.Length > 500)
                details = details.Substring(0, 500) + "...";

            return string.IsNullOrWhiteSpace(details)
                ? $"powercfg.exe exited with code {ExitCode}"
                : $"powercfg.exe exited with code {ExitCode}: {details}";
        }
    }

    internal static class PowerCfgRunner
    {
        private const int TimeoutMs = 5000;

        public static PowerCfgResult Run(string arguments)
        {
            AppLog.Write("PowerCfg", $"Command: powercfg.exe {arguments}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                    return LogResult(arguments, new PowerCfgResult { Started = false });

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(TimeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(1000);
                    }
                    catch { }

                    return LogResult(arguments, new PowerCfgResult
                    {
                        Started = true,
                        TimedOut = true,
                        Output = GetCompletedText(outputTask),
                        Error = GetCompletedText(errorTask)
                    });
                }

                return LogResult(arguments, new PowerCfgResult
                {
                    Started = true,
                    ExitCode = process.ExitCode,
                    Output = outputTask.GetAwaiter().GetResult(),
                    Error = errorTask.GetAwaiter().GetResult()
                });
            }
            catch (Exception ex)
            {
                return LogResult(arguments, new PowerCfgResult
                {
                    Started = false,
                    ExceptionMessage = ex.Message
                });
            }
        }

        private static PowerCfgResult LogResult(string arguments, PowerCfgResult result)
        {
            if (result.Success)
                AppLog.Write("PowerCfg", $"Result: success (exit 0) for {arguments}");
            else
                AppLog.Write("PowerCfg", $"Result: {result.DescribeFailure()} for {arguments}");

            return result;
        }

        private static string GetCompletedText(Task<string> task)
        {
            try
            {
                return task.Wait(500) ? task.Result : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
