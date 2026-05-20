using System;
using System.Collections.Generic;

namespace TweakWise.Models
{
    public enum HealthLevel
    {
        Unknown,
        Good,
        Normal,
        Attention,
        Warning,
        Critical,
        Checking
    }

    public enum CoreModuleId
    {
        WindowsSetup = 0,
        SystemParameters = 1,
        Resources = 2,
        Maintenance = 3,
        Devices = 4,
        Network = 5,
        PowerThermal = 6,

        Performance = Resources,
        Diagnostics = Devices
    }

    public sealed class ComputerHealthStatus
    {
        public HealthLevel OverallStatus { get; set; } = HealthLevel.Unknown;
        public int ProblemCount { get; set; }
        public int RecommendationCount { get; set; }
        public int CriticalCount { get; set; }
        public bool PendingRestart { get; set; }
        public DateTime? LastCheckedAt { get; set; }
    }

    public sealed class ModuleHealthStatus
    {
        public CoreModuleId ModuleId { get; set; }
        public string Title { get; set; } = string.Empty;
        public HealthLevel Status { get; set; } = HealthLevel.Unknown;
        public int ProblemCount { get; set; }
        public int RecommendationCount { get; set; }
        public List<ModuleHealthFinding> Findings { get; set; } = new List<ModuleHealthFinding>();
        public DateTime? LastCheckedAt { get; set; }
    }

    public sealed class ModuleHealthFinding
    {
        public HealthLevel Level { get; set; } = HealthLevel.Normal;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
    }

    public sealed class CoreModuleDefinition
    {
        public CoreModuleId Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ShortHint { get; set; } = string.Empty;
        public List<string> Sections { get; set; } = new List<string>();
        public ModuleHealthStatus Status { get; set; } = new ModuleHealthStatus();
    }
}
