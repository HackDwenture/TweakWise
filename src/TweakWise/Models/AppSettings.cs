using System.Collections.Generic;

namespace TweakWise.Models
{
    public class AppSettings
    {
        public string Theme { get; set; } = "System";
        public bool RunOnStartup { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool ShowTrayTemperature { get; set; } = false;
        public bool ShowCoreCpuTemperature { get; set; } = true;
        public bool ShowCoreGpuTemperature { get; set; } = true;
        public bool ShowCoreStorageTemperature { get; set; } = true;
        public bool ShowCoreMotherboardTemperature { get; set; } = true;
        public bool ShowCoreOtherTemperature { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool FirstRunCompleted { get; set; } = false;
        public bool PendingRestart { get; set; } = false;
        public int PerformanceBackupRetentionDays { get; set; } = 30;
        public System.DateTime? PendingRestartMarkedAtUtc { get; set; }
        public string PendingRestartReason { get; set; } = string.Empty;
        public string LastHealthLevel { get; set; } = "Unknown";
        public int LastHealthProblemCount { get; set; } = 0;
        public int LastHealthRecommendationCount { get; set; } = 0;
        public int LastHealthCriticalCount { get; set; } = 0;
        public System.DateTime? LastHealthCheckedAt { get; set; }
        public string LastNotifiedUpdateVersion { get; set; } = string.Empty;
        public string LastNotifiedReleaseCommit { get; set; } = string.Empty;
        public List<NotificationData> Notifications { get; set; } = new List<NotificationData>();
        public List<HealthSignalSuppression> HealthSignalSuppressions { get; set; } = new List<HealthSignalSuppression>();
    }

    public class NotificationData
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; }
        public bool HasAction { get; set; }
    }

    public class HealthSignalSuppression
    {
        public string SignalId { get; set; } = string.Empty;
        public bool IsPermanent { get; set; }
        public System.DateTime? SuppressedUntilUtc { get; set; }
        public System.DateTime CreatedAtUtc { get; set; } = System.DateTime.UtcNow;
    }
}
