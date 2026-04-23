using System.IO;
using System.Text.Json;

namespace ID648
{
    internal sealed class AppSettings
    {
        public string Language { get; set; } = "system";

        public bool WelcomeConfirmed { get; set; }
    }

    internal static class AppSettingsStore
    {
        private const string CompanyFolderName = "WoongSoft";
        private const string ProductFolderName = "ClipBoardBinder";
        private const string SettingsFileName = "settings.json";
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            CompanyFolderName,
            ProductFolderName,
            SettingsFileName);
        private static AppSettings _settings = Load();

        public static string SavedLanguage => string.IsNullOrWhiteSpace(_settings.Language) ? "system" : _settings.Language;

        public static bool WelcomeConfirmed => _settings.WelcomeConfirmed;

        public static void SetLanguage(string language)
        {
            _settings.Language = string.IsNullOrWhiteSpace(language) ? "system" : language;
            Save();
        }

        public static void SetWelcomeConfirmed(bool confirmed)
        {
            _settings.WelcomeConfirmed = confirmed;
            Save();
        }

        private static AppSettings Load()
        {
            try
            {
                if(!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private static void Save()
        {
            try
            {
                var folderPath = Path.GetDirectoryName(SettingsPath);
                if(string.IsNullOrWhiteSpace(folderPath))
                {
                    return;
                }

                Directory.CreateDirectory(folderPath);
                var json = JsonSerializer.Serialize(_settings,new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath,json);
            }
            catch
            {
            }
        }
    }
}
