using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ID648
{
    internal static class Localization
    {
        private const string SystemLanguageCode = "system";
        private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { "en", "ko", "ja", "es" };

        public static string CurrentLanguage { get; private set; } = SystemLanguageCode;

        public static string EffectiveLanguage { get; private set; } = "en";

        public static event Action? LanguageChanged;

        public static void ApplySavedOrSystemLanguage()
        {
            var saved = AppSettingsStore.SavedLanguage;
            if (string.IsNullOrEmpty(saved) || string.Equals(saved, SystemLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                ApplyLanguageInternal(GetSystemLanguageOrDefault());
                CurrentLanguage = SystemLanguageCode;
                return;
            }

            ApplyLanguageInternal(saved);
            CurrentLanguage = saved;
        }

        public static void SetLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || string.Equals(code, SystemLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                AppSettingsStore.SetLanguage(SystemLanguageCode);
                ApplyLanguageInternal(GetSystemLanguageOrDefault());
                CurrentLanguage = SystemLanguageCode;
                LanguageChanged?.Invoke();
                return;
            }

            if (!Supported.Contains(code))
            {
                code = "en";
            }

            AppSettingsStore.SetLanguage(code);
            ApplyLanguageInternal(code);
            CurrentLanguage = code;
            LanguageChanged?.Invoke();
        }

        private static void ApplyLanguageInternal(string twoLetterCode)
        {
            if (Application.Current == null)
            {
                return;
            }

            EffectiveLanguage = Supported.Contains(twoLetterCode) ? twoLetterCode : "en";

            var merged = Application.Current.Resources.MergedDictionaries;
            var existing = merged.Where(d => d.Source != null && d.Source.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var d in existing)
            {
                merged.Remove(d);
            }

            try
            {
                var uri = new Uri($"Resources/Strings.{EffectiveLanguage}.xaml", UriKind.Relative);
                var rd = new ResourceDictionary { Source = uri };
                merged.Add(rd);
            }
            catch
            {
                try
                {
                    var uri = new Uri($"Resources/Strings.en.xaml", UriKind.Relative);
                    var rd = new ResourceDictionary { Source = uri };
                    merged.Add(rd);
                }
                catch
                {
                }
            }
        }

        public static bool IsLanguageSelected(string code)
        {
            return string.Equals(CurrentLanguage, code, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSystemLanguageOrDefault()
        {
            try
            {
                var two = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
                if (Supported.Contains(two)) return two;
                two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (Supported.Contains(two)) return two;
            }
            catch
            {
            }

            return "en";
        }

        public static string GetString(string key)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is string s)
                {
                    return s;
                }
            }
            catch
            {
            }

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            var fmt = GetString(key);
            try
            {
                return string.Format(fmt, args);
            }
            catch
            {
                return fmt;
            }
        }
    }
}
