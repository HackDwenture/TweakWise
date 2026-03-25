using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using TweakWise.Models;

namespace TweakWise.Managers
{
    public class SettingsManager
    {
        private readonly string _settingsPath;
        public AppSettings CurrentSettings { get; private set; }

        public event Action SettingsChanged;

        public SettingsManager()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TweakWise", "settings.json");
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch { }

            if (CurrentSettings == null)
                CurrentSettings = new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                string dir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
            SettingsChanged?.Invoke();
        }

        public void ChangeTheme(string themeName)
        {
            CurrentSettings.Theme = themeName;
            var app = (App)Application.Current;
            app.ChangeTheme(themeName);
            SaveSettings();
        }

        public void SetFirstRunCompleted()
        {
            CurrentSettings.FirstRunCompleted = true;
            SaveSettings();
        }
    }
}