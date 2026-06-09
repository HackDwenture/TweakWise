using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            NormalizeSettings();
        }

        private void NormalizeSettings()
        {
            CurrentSettings.Notifications ??= new System.Collections.Generic.List<NotificationData>();
            CurrentSettings.HealthSignalSuppressions ??= new System.Collections.Generic.List<HealthSignalSuppression>();
            CurrentSettings.PerformanceBackupRetentionDays = Math.Clamp(CurrentSettings.PerformanceBackupRetentionDays, 1, 30);
        }

        public void SaveSettings()
        {
            NormalizeSettings();
            CurrentSettings.PerformanceBackupRetentionDays = Math.Clamp(CurrentSettings.PerformanceBackupRetentionDays, 1, 30);
            RemoveExpiredHealthSignalSuppressions(save: false);

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

        public bool IsHealthSignalSuppressed(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
                return false;

            RemoveExpiredHealthSignalSuppressions(save: true);

            return CurrentSettings.HealthSignalSuppressions.Any(item =>
                string.Equals(item.SignalId, signalId, StringComparison.OrdinalIgnoreCase) &&
                (item.IsPermanent || item.SuppressedUntilUtc > DateTime.UtcNow));
        }

        public void SnoozeHealthSignal(string signalId, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(signalId))
                return;

            UpsertHealthSignalSuppression(signalId, permanent: false, DateTime.UtcNow.Add(duration));
        }

        public void DismissHealthSignal(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
                return;

            UpsertHealthSignalSuppression(signalId, permanent: true, suppressedUntilUtc: null);
        }

        public void SnoozeHealthSignals(System.Collections.Generic.IEnumerable<string> signalIds, TimeSpan duration)
        {
            if (signalIds == null)
                return;

            DateTime untilUtc = DateTime.UtcNow.Add(duration);
            foreach (string signalId in signalIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                UpsertHealthSignalSuppression(signalId, permanent: false, untilUtc, save: false);

            SaveSettings();
        }

        public void DismissHealthSignals(System.Collections.Generic.IEnumerable<string> signalIds)
        {
            if (signalIds == null)
                return;

            foreach (string signalId in signalIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                UpsertHealthSignalSuppression(signalId, permanent: true, suppressedUntilUtc: null, save: false);

            SaveSettings();
        }

        private void UpsertHealthSignalSuppression(string signalId, bool permanent, DateTime? suppressedUntilUtc, bool save = true)
        {
            RemoveExpiredHealthSignalSuppressions(save: false);

            var existing = CurrentSettings.HealthSignalSuppressions.FirstOrDefault(item =>
                string.Equals(item.SignalId, signalId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                CurrentSettings.HealthSignalSuppressions.Add(new HealthSignalSuppression
                {
                    SignalId = signalId.Trim(),
                    IsPermanent = permanent,
                    SuppressedUntilUtc = suppressedUntilUtc,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.IsPermanent = permanent;
                existing.SuppressedUntilUtc = suppressedUntilUtc;
            }

            if (save)
                SaveSettings();
        }

        private void RemoveExpiredHealthSignalSuppressions(bool save)
        {
            var suppressions = CurrentSettings.HealthSignalSuppressions;
            if (suppressions == null)
            {
                CurrentSettings.HealthSignalSuppressions = new System.Collections.Generic.List<HealthSignalSuppression>();
                return;
            }

            int before = suppressions.Count;
            DateTime nowUtc = DateTime.UtcNow;
            CurrentSettings.HealthSignalSuppressions = suppressions
                .Where(item => item != null &&
                               !string.IsNullOrWhiteSpace(item.SignalId) &&
                               (item.IsPermanent || item.SuppressedUntilUtc > nowUtc))
                .GroupBy(item => item.SignalId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.IsPermanent).ThenByDescending(item => item.SuppressedUntilUtc).First())
                .ToList();

            if (save && CurrentSettings.HealthSignalSuppressions.Count != before)
                SaveSettings();
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

        public void UpdateCoreTemperaturePreferences(
            bool showCpu,
            bool showGpu,
            bool showStorage,
            bool showMotherboard,
            bool showOther)
        {
            CurrentSettings.ShowCoreCpuTemperature = showCpu;
            CurrentSettings.ShowCoreGpuTemperature = showGpu;
            CurrentSettings.ShowCoreStorageTemperature = showStorage;
            CurrentSettings.ShowCoreMotherboardTemperature = showMotherboard;
            CurrentSettings.ShowCoreOtherTemperature = showOther;
            SaveSettings();
        }

        public void UpdateStartupScanPreferences(
            bool scanWorkEnvironment,
            bool scanSystemConfiguration,
            bool scanPerformance,
            bool scanStorage,
            bool scanDevices,
            bool scanNetwork)
        {
            CurrentSettings.ScanWorkEnvironmentAtStartup = scanWorkEnvironment;
            CurrentSettings.ScanSystemConfigurationAtStartup = scanSystemConfiguration;
            CurrentSettings.ScanPerformanceAtStartup = scanPerformance;
            CurrentSettings.ScanStorageAtStartup = scanStorage;
            CurrentSettings.ScanDevicesAtStartup = scanDevices;
            CurrentSettings.ScanNetworkAtStartup = scanNetwork;
            SaveSettings();
        }

        public ISet<CoreModuleId> GetStartupHealthScanModules()
        {
            var modules = new HashSet<CoreModuleId>();

            if (CurrentSettings.ScanWorkEnvironmentAtStartup)
                modules.Add(CoreModuleId.WindowsSetup);
            if (CurrentSettings.ScanPerformanceAtStartup)
                modules.Add(CoreModuleId.Resources);
            if (CurrentSettings.ScanDevicesAtStartup)
                modules.Add(CoreModuleId.Devices);

            return modules;
        }

        public void ResetApplicationState()
        {
            try
            {
                ApplyRunOnStartup(false);
            }
            catch
            {
            }

            try
            {
                if (File.Exists(_settingsPath))
                    File.Delete(_settingsPath);
            }
            catch
            {
            }

            CurrentSettings = new AppSettings();
            SettingsChanged?.Invoke();
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
