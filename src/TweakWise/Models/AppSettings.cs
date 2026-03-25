using System.Collections.Generic;
using TweakWise.Managers;

namespace TweakWise.Models
{
    public class AppSettings
    {
        public string Theme { get; set; } = "System";
        public bool RunOnStartup { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool FirstRunCompleted { get; set; } = false;
        public List<NotificationData> Notifications { get; set; } = new List<NotificationData>();
    }

    public class NotificationData
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; }
        public bool HasAction { get; set; } // для демо, действие не сериализуется
    }
}