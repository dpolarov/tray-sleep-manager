using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LidSleepManager
{
    public class LidActionManager
    {
        private string? originalLidActionAC = null;
        private string? originalLidActionDC = null;
        private bool isModified = false;
        private bool originalsSaved = false;

        public bool SetLidActionDoNothing()
        {
            try
            {
                // Сохраняем оригинальные настройки только один раз
                if (!originalsSaved)
                {
                    originalLidActionAC = GetCurrentLidAction(true);
                    originalLidActionDC = GetCurrentLidAction(false);
                    originalsSaved = true;
                }

                // Устанавливаем "Ничего не делать" (0) при закрытии крышки
                // Для питания от сети (AC)
                RunPowerCfg("/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0");
                // Для питания от батареи (DC)
                RunPowerCfg("/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0");
                // Применяем изменения
                RunPowerCfg("/setactive SCHEME_CURRENT");

                isModified = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RestoreLidAction()
        {
            if (!isModified)
                return true; // Ничего не было изменено

            if (originalLidActionAC == null || originalLidActionDC == null)
            {
                // Если не удалось сохранить оригинальные настройки, 
                // устанавливаем "Сон" (1) как безопасное значение по умолчанию
                originalLidActionAC = "1";
                originalLidActionDC = "1";
            }

            try
            {
                // Восстанавливаем оригинальные настройки
                RunPowerCfg($"/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {originalLidActionAC}");
                RunPowerCfg($"/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {originalLidActionDC}");
                RunPowerCfg("/setactive SCHEME_CURRENT");

                isModified = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string? GetCurrentLidAction(bool isAC)
        {
            try
            {
                string output = RunPowerCfg("/query SCHEME_CURRENT SUB_BUTTONS LIDACTION");
                
                // Ищем текущее значение в выводе
                string pattern = isAC 
                    ? @"Current AC Power Setting Index:\s*(0x[0-9a-fA-F]+)" 
                    : @"Current DC Power Setting Index:\s*(0x[0-9a-fA-F]+)";
                
                var match = Regex.Match(output, pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    // Конвертируем hex в decimal
                    string hexValue = match.Groups[1].Value;
                    int decimalValue = Convert.ToInt32(hexValue, 16);
                    return decimalValue.ToString();
                }
                
                // Пробуем альтернативный паттерн (без 0x)
                pattern = isAC 
                    ? @"Current AC Power Setting Index:\s*([0-9]+)" 
                    : @"Current DC Power Setting Index:\s*([0-9]+)";
                
                match = Regex.Match(output, pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch { }
            
            // Возвращаем "1" (сон) как безопасное значение по умолчанию
            return "1";
        }

        private string RunPowerCfg(string arguments)
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

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    return string.Empty;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }

        public bool IsModified => isModified;
        
        public bool ForceRestoreToSleep()
        {
            try
            {
                // Принудительно устанавливаем "Сон" (1) при закрытии крышки
                RunPowerCfg("/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1");
                RunPowerCfg("/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1");
                RunPowerCfg("/setactive SCHEME_CURRENT");
                
                isModified = false;
                originalsSaved = false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
