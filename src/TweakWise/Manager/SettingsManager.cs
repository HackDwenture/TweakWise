using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using TweakWise.Models;
using Application = System.Windows.Application;

namespace TweakWise.Managers
{
    public class SettingsManager
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegistryValueName = "TweakWise";

        private readonly string _settingsPath;

        public AppSettings CurrentSettings { get; private set; }

        public event Action SettingsChanged;

        public SettingsManager()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TweakWise",
                "settings.json");

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
            catch
            {
            }

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
            catch
            {
            }

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

        public void UpdateShellPreferences(
            bool runOnStartup,
            bool autoCheckUpdates,
            bool showNotifications,
            bool showTrayTemperature,
            bool minimizeToTrayOnClose,
            bool startMinimizedToTray)
        {
            bool startupRegistrationChanged =
                CurrentSettings.RunOnStartup != runOnStartup ||
                CurrentSettings.StartMinimizedToTray != startMinimizedToTray;

            CurrentSettings.RunOnStartup = runOnStartup;
            CurrentSettings.AutoCheckUpdates = autoCheckUpdates;
            CurrentSettings.ShowNotifications = showNotifications;
            CurrentSettings.ShowTrayTemperature = showTrayTemperature;
            CurrentSettings.MinimizeToTrayOnClose = minimizeToTrayOnClose;
            CurrentSettings.StartMinimizedToTray = startMinimizedToTray;

            if (startupRegistrationChanged)
                ApplyRunOnStartup(runOnStartup);

            SaveSettings();
        }

        public void MarkPendingRestart(string reason)
        {
            CurrentSettings.PendingRestart = true;
            CurrentSettings.PendingRestartMarkedAtUtc = DateTime.UtcNow;
            CurrentSettings.PendingRestartReason = string.IsNullOrWhiteSpace(reason)
                ? "изменения TweakWise"
                : reason.Trim();

            SaveSettings();
        }

        public bool HasActiveTweakWiseRestartRequest()
        {
            var markedAt = CurrentSettings.PendingRestartMarkedAtUtc;
            if (!markedAt.HasValue)
                return false;

            DateTime bootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (markedAt.Value <= bootUtc)
            {
                ClearTweakWiseRestartRequest();
                return false;
            }

            return true;
        }

        public void ClearTweakWiseRestartRequest()
        {
            CurrentSettings.PendingRestartMarkedAtUtc = null;
            CurrentSettings.PendingRestartReason = string.Empty;
            SaveSettings();
        }

        public void ApplySavedSystemSettings()
        {
            ApplyRunOnStartup(CurrentSettings.RunOnStartup);
        }

        private void ApplyRunOnStartup(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, true)
                    ?? Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath);

                if (key == null)
                    return;

                if (enabled)
                {
                    string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        string launchArguments = CurrentSettings.StartMinimizedToTray ? " --tray-start" : string.Empty;
                        key.SetValue(RunRegistryValueName, $"\"{exePath}\"{launchArguments}");
                    }
                }
                else
                {
                    key.DeleteValue(RunRegistryValueName, false);
                }
            }
            catch
            {
            }
        }
    }
}
