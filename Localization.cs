using System;
using System.Globalization;
using System.IO;

namespace SleepMngr
{
    internal enum AppLanguage
    {
        Russian,
        English
    }

    internal static class Localization
    {
        private static readonly string LanguageFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SleepMngr",
            "language.txt");

        public static AppLanguage CurrentLanguage { get; private set; } = LoadLanguage();

        public static bool IsEnglish => CurrentLanguage == AppLanguage.English;

        public static string T(string russian, string english) => IsEnglish ? english : russian;

        public static void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
            try
            {
                string? directory = Path.GetDirectoryName(LanguageFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(LanguageFile, language == AppLanguage.English ? "en" : "ru");
            }
            catch
            {
                // Language persistence is non-critical.
            }
        }

        private static AppLanguage LoadLanguage()
        {
            try
            {
                if (File.Exists(LanguageFile))
                {
                    string value = File.ReadAllText(LanguageFile).Trim();
                    if (value.Equals("en", StringComparison.OrdinalIgnoreCase))
                        return AppLanguage.English;
                    if (value.Equals("ru", StringComparison.OrdinalIgnoreCase))
                        return AppLanguage.Russian;
                }
            }
            catch
            {
                // Fall back to Windows UI language.
            }

            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Russian
                : AppLanguage.English;
        }
    }
}
