using System;
using System.Collections.Generic;

namespace TweakWise.Models
{
    public enum TelemetryStatus
    {
        Ok,
        Attention,
        Critical
    }

    public class TelemetryMetric
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PrimaryValue { get; set; } = string.Empty;
        public string SecondaryValue { get; set; } = string.Empty;
        public int UsagePercent { get; set; }
        public TelemetryStatus Status { get; set; } = TelemetryStatus.Ok;
        public List<int> HistoryPercentages { get; set; } = new List<int>();
        public string HelpText { get; set; } = string.Empty;
    }

    public class TelemetrySnapshot
    {
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public string OverallStateTitle { get; set; } = string.Empty;
        public string OverallStateDetail { get; set; } = string.Empty;
        public TelemetryStatus OverallStatus { get; set; } = TelemetryStatus.Ok;
        public string ActiveProfileId { get; set; } = string.Empty;
        public string ActiveProfileTitle { get; set; } = string.Empty;
        public string CoolingSummary { get; set; } = string.Empty;
        public List<TelemetryMetric> UsageMetrics { get; set; } = new List<TelemetryMetric>();
        public List<TelemetryMetric> TemperatureMetrics { get; set; } = new List<TelemetryMetric>();
        public List<TelemetryMetric> HealthMetrics { get; set; } = new List<TelemetryMetric>();
        public List<TelemetryMetric> FanMetrics { get; set; } = new List<TelemetryMetric>();
    }
}
