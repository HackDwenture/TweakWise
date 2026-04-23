using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TweakWise.Catalog;
using TweakWise.Models;
using TweakWise.Providers;

namespace TweakWise.Pages
{
    public partial class MonitoringPerformancePage : Page
    {
        private readonly ITweakCatalogProvider _catalogProvider;
        private readonly ITelemetryProvider _telemetryProvider;
        private readonly DispatcherTimer _refreshTimer;

        private List<TweakDefinition> _allTweaks = new List<TweakDefinition>();
        private List<TweakTemplateDefinition> _allProfiles = new List<TweakTemplateDefinition>();
        private MonitoringTabKind _selectedTab = MonitoringTabKind.Overview;
        private bool _isViewReady;

        public MonitoringPerformancePage()
        {
            InitializeComponent();

            _catalogProvider = App.TweakCatalogProvider ?? new MockTweakCatalogProvider();
            _telemetryProvider = new MockTelemetryProvider();
            _refreshTimer = new DispatcherTimer
            {
                Interval = _telemetryProvider.RefreshInterval
            };

            _refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += MonitoringPerformancePage_Loaded;
            Unloaded += MonitoringPerformancePage_Unloaded;

            _isViewReady = true;
            LoadPage();
        }

        private void LoadPage()
        {
            _allTweaks = _catalogProvider
                .GetTweaksByCategory("MonitoringPerformance")
                .Where(tweak => MonitoringPerformanceCatalogSeed.SectionOrder.Contains(tweak.Subcategory))
                .ToList();

            _allProfiles = _catalogProvider
                .GetTemplates()
                .Where(template => MonitoringPerformanceCatalogSeed.LocalProfileIds.Contains(template.Id))
                .ToList();

            ProfileSettingsItemsControl.ItemsSource = BuildSettingsForSection("Профили производительности");
            CoolingSettingsItemsControl.ItemsSource = BuildSettingsForSection("Вентиляторы и охлаждение");
            SafeTuningItemsControl.ItemsSource = BuildSettingsForSection("Безопасный тюнинг");
            AdvancedSettingsItemsControl.ItemsSource = BuildSettingsForSection("Расширенные параметры");

            ApplySelectedTab();
            RefreshTelemetry();
        }

        private void MonitoringPerformancePage_Loaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Start();
        }

        private void MonitoringPerformancePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshTelemetry();
        }

        private void RefreshTelemetry()
        {
            var snapshot = _telemetryProvider.GetSnapshot();

            LastRefreshTextBlock.Text = $"Обновлено: {snapshot.CapturedAt:HH:mm:ss} • mock refresh каждые {(int)_telemetryProvider.RefreshInterval.TotalSeconds} сек.";
            CurrentViewDescriptionTextBlock.Text = GetViewDescription(_selectedTab, snapshot);

            OverviewCardsItemsControl.ItemsSource = BuildOverviewCards(snapshot);
            OverviewMetricsItemsControl.ItemsSource = BuildMetricCards(snapshot.UsageMetrics);
            TemperatureMetricsItemsControl.ItemsSource = BuildMetricCards(snapshot.TemperatureMetrics);
            HardwareUsageItemsControl.ItemsSource = BuildMetricCards(snapshot.UsageMetrics);
            HealthMetricsItemsControl.ItemsSource = BuildMetricCards(snapshot.HealthMetrics);
            FanMetricsItemsControl.ItemsSource = BuildMetricCards(snapshot.FanMetrics);
            ProfileItemsControl.ItemsSource = BuildProfileCards(snapshot);

            ActiveProfileSummaryTextBlock.Text = $"Активный профиль сейчас: {snapshot.ActiveProfileTitle}";
            ActiveProfileDetailsTextBlock.Text = $"{snapshot.OverallStateDetail} Охлаждение: {snapshot.CoolingSummary}.";
        }

        private IReadOnlyList<SettingCardViewModel> BuildSettingsForSection(string subcategory)
        {
            return CatalogPresentationBuilder.BuildSettingCards(
                _allTweaks.Where(tweak => string.Equals(tweak.Subcategory, subcategory, StringComparison.OrdinalIgnoreCase)));
        }

        private void TabButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isViewReady)
                return;

            if (sender is not ToggleButton button || button.Tag is not string tag)
                return;

            _selectedTab = tag switch
            {
                "Hardware" => MonitoringTabKind.Hardware,
                "Profiles" => MonitoringTabKind.Profiles,
                "Tuning" => MonitoringTabKind.Tuning,
                "Pro" => MonitoringTabKind.Pro,
                _ => MonitoringTabKind.Overview
            };

            ApplySelectedTab();
            RefreshTelemetry();
        }

        private void ApplySelectedTab()
        {
            OverviewPanel.Visibility = _selectedTab == MonitoringTabKind.Overview ? Visibility.Visible : Visibility.Collapsed;
            HardwarePanel.Visibility = _selectedTab == MonitoringTabKind.Hardware ? Visibility.Visible : Visibility.Collapsed;
            ProfilesPanel.Visibility = _selectedTab == MonitoringTabKind.Profiles ? Visibility.Visible : Visibility.Collapsed;
            TuningPanel.Visibility = _selectedTab == MonitoringTabKind.Tuning ? Visibility.Visible : Visibility.Collapsed;
            ProPanel.Visibility = _selectedTab == MonitoringTabKind.Pro ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<OverviewCardViewModel> BuildOverviewCards(TelemetrySnapshot snapshot)
        {
            var hottestSensor = snapshot.TemperatureMetrics
                .OrderByDescending(metric => metric.UsagePercent)
                .FirstOrDefault();

            var battery = snapshot.HealthMetrics.FirstOrDefault(metric => metric.Id == "battery-health");
            var ssd = snapshot.HealthMetrics.FirstOrDefault(metric => metric.Id == "ssd-health");

            return new List<OverviewCardViewModel>
            {
                new()
                {
                    Title = "Состояние системы",
                    ValueText = snapshot.OverallStateTitle,
                    Description = snapshot.OverallStateDetail,
                    StatusText = GetStatusText(snapshot.OverallStatus),
                    StatusTone = GetStatusTone(snapshot.OverallStatus)
                },
                new()
                {
                    Title = "Самая горячая точка",
                    ValueText = hottestSensor?.PrimaryValue ?? "Нет данных",
                    Description = hottestSensor == null
                        ? "Сенсоры пока не доступны."
                        : $"{hottestSensor.Title} • {hottestSensor.SecondaryValue}",
                    StatusText = hottestSensor == null ? "OK" : GetStatusText(hottestSensor.Status),
                    StatusTone = hottestSensor == null ? CatalogBadgeTone.Success : GetStatusTone(hottestSensor.Status)
                },
                new()
                {
                    Title = "Активный профиль",
                    ValueText = snapshot.ActiveProfileTitle,
                    Description = $"Текущий сценарий работы и охлаждения. {snapshot.CoolingSummary}",
                    StatusText = GetStatusText(snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Attention : TelemetryStatus.Ok),
                    StatusTone = GetStatusTone(snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Attention : TelemetryStatus.Ok)
                },
                new()
                {
                    Title = "Батарея и SSD",
                    ValueText = $"{battery?.PrimaryValue ?? "Нет данных"} • {ssd?.PrimaryValue ?? "Нет данных"}",
                    Description = $"{battery?.SecondaryValue ?? string.Empty} {(string.IsNullOrWhiteSpace(battery?.SecondaryValue) ? string.Empty : "•")} {ssd?.SecondaryValue ?? string.Empty}".Trim(),
                    StatusText = GetStatusText(GetWorstStatus(battery?.Status ?? TelemetryStatus.Ok, ssd?.Status ?? TelemetryStatus.Ok)),
                    StatusTone = GetStatusTone(GetWorstStatus(battery?.Status ?? TelemetryStatus.Ok, ssd?.Status ?? TelemetryStatus.Ok))
                }
            };
        }

        private List<MetricCardViewModel> BuildMetricCards(IEnumerable<TelemetryMetric> metrics)
        {
            return metrics.Select(metric => new MetricCardViewModel
            {
                Id = metric.Id,
                Title = metric.Title,
                ValueText = metric.PrimaryValue,
                SecondaryText = metric.SecondaryValue,
                UsagePercent = metric.UsagePercent,
                StatusText = GetStatusText(metric.Status),
                StatusTone = GetStatusTone(metric.Status),
                HistoryBars = metric.HistoryPercentages
                    .Select(value => new HistoryBarViewModel
                    {
                        Height = Math.Max(8, Math.Round(value * 0.48))
                    })
                    .ToList()
            }).ToList();
        }

        private List<ProfileCardViewModel> BuildProfileCards(TelemetrySnapshot snapshot)
        {
            var tweakMap = _allTweaks.ToDictionary(tweak => tweak.Id, tweak => tweak);

            return _allProfiles
                .Select(template =>
                {
                    bool isActive = string.Equals(template.Id, snapshot.ActiveProfileId, StringComparison.OrdinalIgnoreCase);
                    var highlights = template.TweakIds
                        .Where(tweakMap.ContainsKey)
                        .Select(id => tweakMap[id].Title)
                        .Take(3)
                        .ToList();

                    return new ProfileCardViewModel
                    {
                        Id = template.Id,
                        Title = template.Title,
                        Description = template.Description,
                        TargetText = GetProfileTargetText(template.Id),
                        NoiseText = GetProfileNoiseText(template.Id),
                        PowerText = GetProfilePowerText(template.Id),
                        ThermalText = GetProfileThermalText(template.Id),
                        StatusText = isActive ? "OK" : GetStatusText(GetProfileStatus(template.Id, snapshot)),
                        StatusTone = isActive ? CatalogBadgeTone.Success : GetStatusTone(GetProfileStatus(template.Id, snapshot)),
                        IsActive = isActive,
                        Highlights = highlights
                    };
                })
                .ToList();
        }

        private string GetViewDescription(MonitoringTabKind tab, TelemetrySnapshot snapshot)
        {
            return tab switch
            {
                MonitoringTabKind.Hardware => "Фокус на железе: температуры, загрузка основных узлов, здоровье батареи и SSD, скорость вентиляторов.",
                MonitoringTabKind.Profiles => $"Сейчас активен профиль «{snapshot.ActiveProfileTitle}». Здесь можно сравнить тихий, сбалансированный и более агрессивные сценарии без показа сырых power GUID.",
                MonitoringTabKind.Tuning => "Только применимые пользовательские настройки: профили, охлаждение и безопасный тюнинг без экстремальных обещаний.",
                MonitoringTabKind.Pro => "Расширенный слой для pro: больше контроля над CPU, GPU и хранилищем, но технические детали остаются в раскрываемых карточках.",
                _ => "Общий ежедневный обзор: ключевые статусы системы, базовые метрики и текущий профиль производительности."
            };
        }

        private static TelemetryStatus GetWorstStatus(TelemetryStatus first, TelemetryStatus second)
        {
            if (first == TelemetryStatus.Critical || second == TelemetryStatus.Critical)
                return TelemetryStatus.Critical;

            if (first == TelemetryStatus.Attention || second == TelemetryStatus.Attention)
                return TelemetryStatus.Attention;

            return TelemetryStatus.Ok;
        }

        private static string GetStatusText(TelemetryStatus status)
        {
            return status switch
            {
                TelemetryStatus.Attention => "Attention",
                TelemetryStatus.Critical => "Critical",
                _ => "OK"
            };
        }

        private static CatalogBadgeTone GetStatusTone(TelemetryStatus status)
        {
            return status switch
            {
                TelemetryStatus.Attention => CatalogBadgeTone.Warning,
                TelemetryStatus.Critical => CatalogBadgeTone.Danger,
                _ => CatalogBadgeTone.Success
            };
        }

        private static TelemetryStatus GetProfileStatus(string profileId, TelemetrySnapshot snapshot)
        {
            return profileId switch
            {
                "monitor-profile-maximum" => snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Attention : TelemetryStatus.Ok,
                "monitor-profile-performance" => snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Attention : TelemetryStatus.Ok,
                "monitor-profile-quiet" => snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Critical : TelemetryStatus.Ok,
                _ => snapshot.OverallStatus == TelemetryStatus.Critical ? TelemetryStatus.Attention : TelemetryStatus.Ok
            };
        }

        private static string GetProfileTargetText(string profileId)
        {
            return profileId switch
            {
                "monitor-profile-quiet" => "Подходит для тишины, офиса, браузера и спокойной фоновой работы.",
                "monitor-profile-performance" => "Для тяжёлых рабочих задач, длинных сборок, рендера и расчётов.",
                "monitor-profile-maximum" => "Для краткого pro-режима, когда шум и расход энергии допустимы ради максимального запаса.",
                _ => "Универсальный ежедневный сценарий между тишиной, отзывчивостью и стабильными температурами."
            };
        }

        private static string GetProfileNoiseText(string profileId)
        {
            return profileId switch
            {
                "monitor-profile-quiet" => "Минимум шума",
                "monitor-profile-performance" => "Слышно под нагрузкой",
                "monitor-profile-maximum" => "Шум не приоритет",
                _ => "Умеренный"
            };
        }

        private static string GetProfilePowerText(string profileId)
        {
            return profileId switch
            {
                "monitor-profile-quiet" => "Экономнее",
                "monitor-profile-performance" => "Выше расход",
                "monitor-profile-maximum" => "Максимальный расход",
                _ => "Сбалансированно"
            };
        }

        private static string GetProfileThermalText(string profileId)
        {
            return profileId switch
            {
                "monitor-profile-quiet" => "Цель: тихое и прохладное поведение в повседневной работе",
                "monitor-profile-performance" => "Цель: удерживать частоты под длинной рабочей нагрузкой",
                "monitor-profile-maximum" => "Цель: приоритет производительности над шумом и температурой",
                _ => "Цель: ровный повседневный баланс между температурой и откликом"
            };
        }

        private enum MonitoringTabKind
        {
            Overview,
            Hardware,
            Profiles,
            Tuning,
            Pro
        }

        private sealed class OverviewCardViewModel
        {
            public string Title { get; set; }
            public string ValueText { get; set; }
            public string Description { get; set; }
            public string StatusText { get; set; }
            public CatalogBadgeTone StatusTone { get; set; }
        }

        private sealed class MetricCardViewModel
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string ValueText { get; set; }
            public string SecondaryText { get; set; }
            public int UsagePercent { get; set; }
            public string StatusText { get; set; }
            public CatalogBadgeTone StatusTone { get; set; }
            public List<HistoryBarViewModel> HistoryBars { get; set; } = new List<HistoryBarViewModel>();
        }

        private sealed class HistoryBarViewModel
        {
            public double Height { get; set; }
        }

        private sealed class ProfileCardViewModel
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string TargetText { get; set; }
            public string NoiseText { get; set; }
            public string PowerText { get; set; }
            public string ThermalText { get; set; }
            public string StatusText { get; set; }
            public CatalogBadgeTone StatusTone { get; set; }
            public bool IsActive { get; set; }
            public List<string> Highlights { get; set; } = new List<string>();
        }
    }
}
