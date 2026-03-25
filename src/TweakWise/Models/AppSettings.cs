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
        public bool MinimizeToTrayOnClose { get; set; } = false;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool FirstRunCompleted { get; set; } = false;
        public string LastNotifiedUpdateVersion { get; set; } = string.Empty;
        public string LastNotifiedReleaseCommit { get; set; } = string.Empty;
        public List<NotificationData> Notifications { get; set; } = new List<NotificationData>();
    }

    public class NotificationData
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; }
        public bool HasAction { get; set; }
    }
}
