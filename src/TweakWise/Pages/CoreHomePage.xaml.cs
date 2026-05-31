using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;
using TweakWise.Controls;
using TweakWise.Managers;
using TweakWise.Models;
using TweakWise.Services;
using WindowsPoint = System.Windows.Point;

namespace TweakWise.Pages
{
    public partial class CoreHomePage : Page
    {
        private readonly IComputerHealthService _healthService = App.ComputerHealthService;
        private readonly SystemCleanupService _cleanupService = new SystemCleanupService();
        private readonly DispatcherTimer _refreshTimer;
        private readonly object _temperatureServiceSync = new object();
        private HardwareTemperatureService _temperatureService;
        private readonly ObservableCollection<SystemStatViewModel> _systemStats = new ObservableCollection<SystemStatViewModel>();
        private readonly ObservableCollection<QuickDiagnosticViewModel> _quickDiagnostics = new ObservableCollection<QuickDiagnosticViewModel>();
        private readonly ObservableCollection<CleanupTargetViewModel> _cleanupTargets = new ObservableCollection<CleanupTargetViewModel>();
        private CancellationTokenSource _cleanupCts;
        private const double ConnectionLineDashOffset = 140;
        private bool _detailsOpened;
        private bool _isPageActive;
        private bool _cleanupBusy;
        private bool _refreshBusy;
        private bool _isAnalyzingCleanup;
        private bool _cleanupAnalysisQueued;
        private bool _cleanupAnalysisQueuedWithBusy;
        private long _lastCleanupEstimateBytes;
        private SystemSnapshot _latestSnapshot = SystemSnapshot.Empty;
        private CleanupProfile _currentCleanupProfile = CleanupProfile.Safe;
        private readonly object _cpuSync = new object();
        private ulong _lastIdleTicks;
        private ulong _lastKernelTicks;
        private ulong _lastUserTicks;
        private bool _hasCpuBaseline;
        private bool _suppressCleanupSelectionChanged;
        private bool _temperatureRefreshRunning;
        private int _temperatureRefreshVersion;
        private double _lastCpuUsagePercent;
        private CoreDetailMode _selectedCoreDetailMode = CoreDetailMode.Monitor;

        public CoreHomePage()
        {
            InitializeComponent();

            SystemStatsItemsControl.ItemsSource = _systemStats;
            QuickDiagnosticsItemsControl.ItemsSource = _quickDiagnostics;
            CleanupTargetsItemsControl.ItemsSource = _cleanupTargets;

            InitializeCleanupTargets();
            ApplyCleanupProfile(CleanupProfile.Safe, updateSummary: false, triggerAnalyze: false);

            Loaded += CoreHomePage_Loaded;
            Unloaded += CoreHomePage_Unloaded;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        private async void CoreHomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = true;
            _healthService.HealthStatusChanged += HealthService_HealthStatusChanged;
            RenderHealthStatus();
            await RefreshTemperaturesAsync();
            await RefreshSystemSnapshotAsync();
            await RefreshCoreDiagnosticsAsync();
            await AnalyzeCleanupAsync(showBusy: false);
            UpdateHomeActionDock();
            if (_isPageActive)
                _refreshTimer.Start();
        }

        private void CoreHomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = false;
            _healthService.HealthStatusChanged -= HealthService_HealthStatusChanged;
            _refreshTimer.Stop();
            _temperatureRefreshVersion++;
            _cleanupCts?.Cancel();
            _cleanupCts?.Dispose();
            _cleanupCts = null;
            lock (_temperatureServiceSync)
            {
                _temperatureService?.Dispose();
                _temperatureService = null;
            }
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (!_isPageActive || _cleanupBusy || _refreshBusy)
                return;

            _refreshBusy = true;
            try
            {
                await RefreshTemperaturesAsync();
                await RefreshSystemSnapshotAsync();
                await RefreshCoreDiagnosticsAsync();
            }
            finally
            {
                _refreshBusy = false;
            }
        }

        private void HealthService_HealthStatusChanged(object sender, EventArgs e)
        {
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                RenderHealthStatus();
                await RefreshCoreDiagnosticsAsync();
            }));
        }

        private void RenderHealthStatus()
        {
            var overall = _healthService.GetOverallStatus();
            var modules = _healthService.GetModules();

            MainCore.Status = overall.OverallStatus;
            MainCore.ProblemCount = overall.ProblemCount;
            MainCore.RecommendationCount = overall.RecommendationCount;
            MainCore.CriticalCount = overall.CriticalCount;

            ExpandedCore.Status = overall.OverallStatus;
            ExpandedCore.ProblemCount = overall.ProblemCount;
            ExpandedCore.RecommendationCount = overall.RecommendationCount;
            ExpandedCore.CriticalCount = overall.CriticalCount;

            HeaderStatusTextBlock.Text = BuildHeaderStatus(overall);
            HeaderCountersTextBlock.Text = overall == null
                ? "Нет данных"
                : $"{overall.ProblemCount} проблем · {overall.RecommendationCount} рекомендаций";
            LastCheckTextBlock.Text = overall.LastCheckedAt.HasValue
                ? $"Последняя проверка: {overall.LastCheckedAt.Value:dd.MM.yyyy HH:mm}"
                : "Последняя проверка ещё не выполнялась";

            ApplyModuleToNode(WindowsSetupNode, modules, CoreModuleId.WindowsSetup);
            ApplyModuleToNode(SystemParametersNode, modules, CoreModuleId.SystemParameters);
            ApplyModuleToNode(ResourcesNode, modules, CoreModuleId.Resources);
            ApplyModuleToNode(StorageNode, modules, CoreModuleId.Maintenance);
            ApplyModuleToNode(DevicesNode, modules, CoreModuleId.Devices);
            ApplyModuleToNode(NetworkNode, modules, CoreModuleId.Network);

            ApplyModuleToConnectionLine(WindowsSetupActiveLine, modules, CoreModuleId.WindowsSetup);
            ApplyModuleToConnectionLine(SystemParametersActiveLine, modules, CoreModuleId.SystemParameters);
            ApplyModuleToConnectionLine(ResourcesActiveLine, modules, CoreModuleId.Resources);
            ApplyModuleToConnectionLine(StorageActiveLine, modules, CoreModuleId.Maintenance);
            ApplyModuleToConnectionLine(DevicesActiveLine, modules, CoreModuleId.Devices);
            ApplyModuleToConnectionLine(NetworkActiveLine, modules, CoreModuleId.Network);
        }

        private static string BuildHeaderStatus(ComputerHealthStatus overall)
        {
            return overall.OverallStatus switch
            {
                HealthLevel.Good => "Компьютер: в норме",
                HealthLevel.Normal => "Компьютер: есть рекомендации",
                HealthLevel.Attention => "Компьютер: требуется внимание",
                HealthLevel.Warning => "Компьютер: есть проблемы",
                HealthLevel.Critical => "Компьютер: критическое состояние",
                HealthLevel.Checking => "Компьютер: выполняется проверка",
                _ => "Компьютер: нет данных"
            };
        }

        private static void ApplyModuleToNode(CoreModuleNodeControl node, IReadOnlyList<CoreModuleDefinition> modules, CoreModuleId moduleId)
        {
            var module = modules.FirstOrDefault(item => item.Id == moduleId);
            if (module == null)
                return;

            node.ModuleId = module.Id;
            node.Title = module.Title;
            node.Hint = module.ShortHint;
            node.Status = module.Status.Status;
            node.ProblemCount = module.Status.ProblemCount;
            node.RecommendationCount = module.Status.RecommendationCount;
            node.Tag = module.Id;
        }

        private static void ApplyModuleToConnectionLine(Line line, IReadOnlyList<CoreModuleDefinition> modules, CoreModuleId moduleId)
        {
            var module = modules.FirstOrDefault(item => item.Id == moduleId);
            if (module == null)
                return;

            line.SetResourceReference(Shape.StrokeProperty, GetStatusBrushKey(module.Status.Status));
        }

        private static string GetStatusBrushKey(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Good => "CoreGoodBrush",
                HealthLevel.Normal => "CoreNormalBrush",
                HealthLevel.Attention => "CoreAttentionBrush",
                HealthLevel.Warning => "CoreWarningBrush",
                HealthLevel.Critical => "CoreCriticalBrush",
                _ => "CoreUnknownBrush"
            };
        }

        private async Task RefreshTemperaturesAsync()
        {
            if (!_isPageActive || TemperatureDockTextBlock == null)
                return;

            var visibleGroups = GetVisibleTemperatureGroups();
            if (visibleGroups.Count == 0)
            {
                TemperatureDockTextBlock.Text = "Температуры: отключены в настройках";
                return;
            }

            if (_temperatureRefreshRunning)
                return;

            _temperatureRefreshRunning = true;
            int refreshVersion = ++_temperatureRefreshVersion;

            try
            {
                var temperatureService = EnsureTemperatureService();
                if (temperatureService == null)
                {
                    TemperatureDockTextBlock.Text = "Температуры: сервис датчиков недоступен";
                    return;
                }

                var readings = await Task.Run(() => temperatureService.GetTemperatures());
                if (!_isPageActive || refreshVersion != _temperatureRefreshVersion)
                    return;

                var visibleReadings = readings
                    .Where(item => item != null && visibleGroups.Contains(item.Group))
                    .GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(item => item.ValueCelsius).First())
                    .OrderBy(item => GetGroupOrder(item.Group))
                    .ToList();

                TemperatureDockTextBlock.Text = visibleReadings.Count == 0
                    ? "Температуры: датчики не обнаружены или не поддерживаются"
                    : "Температуры: " + string.Join(" · ", visibleReadings.Select(item => $"{GetGroupTitle(item.Group)} {HardwareTemperatureService.FormatTemperature(item.ValueCelsius)}"));
            }
            catch
            {
                if (_isPageActive)
                    TemperatureDockTextBlock.Text = "Температуры: не удалось прочитать датчики";
            }
            finally
            {
                _temperatureRefreshRunning = false;
            }
        }

        private HardwareTemperatureService EnsureTemperatureService()
        {
            lock (_temperatureServiceSync)
            {
                if (_temperatureService != null)
                    return _temperatureService;

                try
                {
                    _temperatureService = new HardwareTemperatureService();
                }
                catch
                {
                    _temperatureService = null;
                }

                return _temperatureService;
            }
        }

        private HashSet<string> GetVisibleTemperatureGroups()
        {
            var settings = App.SettingsManager.CurrentSettings;
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (settings.ShowCoreCpuTemperature)
                groups.Add("Cpu");
            if (settings.ShowCoreGpuTemperature)
                groups.Add("Gpu");
            if (settings.ShowCoreStorageTemperature)
                groups.Add("Storage");
            if (settings.ShowCoreMotherboardTemperature)
                groups.Add("Motherboard");
            if (settings.ShowCoreOtherTemperature)
                groups.Add("Other");

            return groups;
        }

        private static int GetGroupOrder(string group)
        {
            return group switch
            {
                "Cpu" => 0,
                "Gpu" => 1,
                "Storage" => 2,
                "Motherboard" => 3,
                _ => 4
            };
        }

        private static string GetGroupTitle(string group)
        {
            return group switch
            {
                "Cpu" => "CPU",
                "Gpu" => "GPU",
                "Storage" => "Диски",
                "Motherboard" => "Плата",
                _ => "Прочее"
            };
        }

        private async Task RefreshSystemSnapshotAsync()
        {
            var snapshot = await Task.Run(CaptureSystemSnapshot);
            if (!_isPageActive)
                return;

            _latestSnapshot = snapshot;
            ApplySystemSnapshot(snapshot);
            UpdateSelectedCleanupSummary();
            UpdateHomeActionDock();
        }

        private SystemSnapshot CaptureSystemSnapshot()
        {
            var memory = GetMemoryStatus();
            var systemDrive = GetSystemDriveInfo();
            int processCount;
            try
            {
                processCount = Process.GetProcesses().Length;
            }
            catch
            {
                processCount = 0;
            }

            long totalMemory = (long)memory.ullTotalPhys;
            long availableMemory = (long)memory.ullAvailPhys;
            double memoryUsedPercent = totalMemory <= 0
                ? 0
                : Math.Clamp((1 - availableMemory / (double)totalMemory) * 100, 0, 100);

            double diskUsedPercent = 0;
            long freeDisk = 0;
            long totalDisk = 0;
            if (systemDrive != null)
            {
                freeDisk = systemDrive.AvailableFreeSpace;
                totalDisk = systemDrive.TotalSize;
                diskUsedPercent = totalDisk <= 0 ? 0 : Math.Clamp((1 - freeDisk / (double)totalDisk) * 100, 0, 100);
            }

            return new SystemSnapshot(
                SampleCpuUsage(),
                memoryUsedPercent,
                totalMemory,
                availableMemory,
                diskUsedPercent,
                freeDisk,
                totalDisk,
                processCount,
                TimeSpan.FromMilliseconds(Environment.TickCount64),
                _lastCleanupEstimateBytes);
        }

        private void ApplySystemSnapshot(SystemSnapshot snapshot)
        {
            _systemStats.Clear();
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "CPU",
                Value = $"{snapshot.CpuUsagePercent:0}%",
                Description = snapshot.CpuUsagePercent >= 85 ? "Высокая загрузка. Стоит проверить фоновые задачи." : "Текущая загрузка процессора.",
                Percent = snapshot.CpuUsagePercent
            });
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "Память",
                Value = $"{snapshot.MemoryUsedPercent:0}%",
                Description = $"Свободно {SystemCleanupService.FormatBytes(snapshot.AvailableMemoryBytes)} из {SystemCleanupService.FormatBytes(snapshot.TotalMemoryBytes)}.",
                Percent = snapshot.MemoryUsedPercent
            });
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "Системный диск",
                Value = $"{snapshot.DiskUsedPercent:0}%",
                Description = snapshot.TotalDiskBytes > 0
                    ? $"Свободно {SystemCleanupService.FormatBytes(snapshot.FreeDiskBytes)}."
                    : "Не удалось определить системный диск.",
                Percent = snapshot.DiskUsedPercent
            });
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "Процессы",
                Value = snapshot.ProcessCount.ToString(),
                Description = snapshot.ProcessCount >= 220 ? "Фоновая активность повышена." : "Активные процессы в текущей сессии.",
                Percent = Math.Clamp(snapshot.ProcessCount / 3.0, 0, 100)
            });
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "Сессия",
                Value = FormatUptime(snapshot.Uptime),
                Description = snapshot.Uptime.TotalDays >= 7 ? "Длительная сессия. Перезапуск может вернуть отзывчивость." : "Аптайм с момента последней загрузки.",
                Percent = Math.Clamp(snapshot.Uptime.TotalHours / 72.0 * 100, 0, 100)
            });
            _systemStats.Add(new SystemStatViewModel
            {
                Icon = "",
                Title = "Очистка",
                Value = snapshot.CleanupEstimateBytes > 0 ? SystemCleanupService.FormatBytes(snapshot.CleanupEstimateBytes) : "0 Б",
                Description = snapshot.CleanupEstimateBytes > 0 ? "Потенциал выбранного профиля очистки." : "Сначала оцените или выберите другой профиль.",
                Percent = Math.Clamp(snapshot.CleanupEstimateBytes / (1024d * 1024d * 1024d) * 24, 0, 100)
            });

            MonitorMiniValueTextBlock.Text = $"CPU {snapshot.CpuUsagePercent:0}% · RAM {snapshot.MemoryUsedPercent:0}%";
            CleanupMiniValueTextBlock.Text = snapshot.CleanupEstimateBytes > 0
                ? $"Можно освободить {SystemCleanupService.FormatBytes(snapshot.CleanupEstimateBytes)}"
                : "Оценка не выполнена";
        }

        private Task RefreshCoreDiagnosticsAsync()
        {
            var overall = _healthService.GetOverallStatus();
            var snapshot = _latestSnapshot;
            _quickDiagnostics.Clear();

            _quickDiagnostics.Add(new QuickDiagnosticViewModel
            {
                Icon = "",
                Title = "Состояние",
                Value = BuildCompactHealthValue(overall),
                Description = BuildHealthDiagnosticDescription(overall),
                Percent = GetHealthPressurePercent(overall)
            });

            _quickDiagnostics.Add(new QuickDiagnosticViewModel
            {
                Icon = "",
                Title = "Ресурсы",
                Value = $"CPU {snapshot.CpuUsagePercent:0}%",
                Description = snapshot.MemoryUsedPercent >= 85
                    ? $"Память загружена на {snapshot.MemoryUsedPercent:0}%. Возможны подтормаживания."
                    : $"Память занята на {snapshot.MemoryUsedPercent:0}%. Система работает стабильно.",
                Percent = Math.Max(snapshot.CpuUsagePercent, snapshot.MemoryUsedPercent)
            });

            _quickDiagnostics.Add(new QuickDiagnosticViewModel
            {
                Icon = "",
                Title = "Диск и очистка",
                Value = snapshot.FreeDiskBytes > 0 ? SystemCleanupService.FormatBytes(snapshot.FreeDiskBytes) : "нет данных",
                Description = snapshot.CleanupEstimateBytes > 0
                    ? $"По текущему профилю можно освободить около {SystemCleanupService.FormatBytes(snapshot.CleanupEstimateBytes)}."
                    : "Свободное место в норме. Профиль очистки можно оценить вручную.",
                Percent = Math.Max(snapshot.DiskUsedPercent, Math.Clamp(snapshot.CleanupEstimateBytes / (1024d * 1024d * 1024d) * 20, 0, 100))
            });

            _quickDiagnostics.Add(new QuickDiagnosticViewModel
            {
                Icon = "",
                Title = "Стабильность",
                Value = overall.PendingRestart ? "нужен рестарт" : FormatUptime(snapshot.Uptime),
                Description = overall.PendingRestart
                    ? "Есть изменения, ожидающие полноценной перезагрузки."
                    : snapshot.Uptime.TotalDays >= 7
                        ? "Сессия длится уже давно. Плановый перезапуск может помочь."
                        : "Критичных признаков деградации по сессии не обнаружено.",
                Percent = overall.PendingRestart ? 92 : Math.Clamp(snapshot.Uptime.TotalHours / 72.0 * 100, 0, 100)
            });

            DiagnosticsMiniValueTextBlock.Text = BuildCompactHealthValue(overall);
            QuickDiagnosticsSummaryTextBlock.Text = "Сводные сигналы по состоянию узлов, ресурсам системы, свободному месту и признакам, которые требуют внимания в первую очередь.";
            CleanupHintTextBlock.Text = ShouldShowCleanupRecommendation()
                ? $"Сейчас рекомендуется очистка. Выбранный сценарий может освободить около {SystemCleanupService.FormatBytes(_lastCleanupEstimateBytes)}."
                : "Выберите уровень очистки, оцените объём и при необходимости запустите сценарий прямо отсюда.";
            LastCleanupStatusTextBlock.Text = BuildLastCleanupText();
            return Task.CompletedTask;
        }

        private static string BuildHealthDiagnosticDescription(ComputerHealthStatus overall)
        {
            if (overall == null)
                return "Нет данных о состоянии узлов.";

            if (overall.CriticalCount > 0)
                return $"Есть {overall.CriticalCount} критических сигнала. Начните с подсвеченных узлов вокруг ядра.";
            if (overall.ProblemCount > 0)
                return $"Обнаружено {overall.ProblemCount} проблем и {overall.RecommendationCount} рекомендаций.";
            if (overall.RecommendationCount > 0)
                return $"Критических проблем нет, но есть {overall.RecommendationCount} рекомендации.";
            return "Проверенные узлы не показывают тревожных сигналов.";
        }

        private static DriveInfo GetSystemDriveInfo()
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return DriveInfo.GetDrives().FirstOrDefault(drive =>
                    drive.IsReady &&
                    string.Equals(drive.RootDirectory.FullName, root, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string BuildCompactHealthValue(ComputerHealthStatus overall)
        {
            if (overall == null)
                return "нет данных";

            if (overall.CriticalCount > 0)
                return $"{overall.CriticalCount} критично";

            if (overall.ProblemCount > 0)
                return $"{overall.ProblemCount} проблем";

            if (overall.RecommendationCount > 0)
                return $"{overall.RecommendationCount} рекомендаций";

            return "в норме";
        }

        private static double GetHealthPressurePercent(ComputerHealthStatus overall)
        {
            if (overall == null)
                return 10;

            if (overall.CriticalCount > 0)
                return 100;

            if (overall.ProblemCount > 0)
                return Math.Min(92, 52 + overall.ProblemCount * 12);

            if (overall.RecommendationCount > 0)
                return Math.Min(62, 25 + overall.RecommendationCount * 8);

            return 12;
        }

        private async Task AnalyzeCleanupAsync(bool showBusy)
        {
            if (_isAnalyzingCleanup)
            {
                _cleanupAnalysisQueued = true;
                _cleanupAnalysisQueuedWithBusy |= showBusy;
                return;
            }

            _isAnalyzingCleanup = true;
            bool currentShowBusy = showBusy;

            try
            {
                do
                {
                    _cleanupAnalysisQueued = false;
                    _cleanupAnalysisQueuedWithBusy = false;

                    var targets = _cleanupTargets.Select(item => item.Target).ToList();
                    if (targets.Count == 0)
                        return;

                    if (currentShowBusy)
                        SetCleanupBusy(true, "Оцениваем объём очистки");

                    try
                    {
                        var states = await _cleanupService.AnalyzeAsync(targets, CancellationToken.None);
                        if (!_isPageActive)
                            return;

                        foreach (var state in states)
                        {
                            var target = _cleanupTargets.FirstOrDefault(item => string.Equals(item.Id, state.Id, StringComparison.OrdinalIgnoreCase));
                            target?.ApplyState(state);
                        }

                        _lastCleanupEstimateBytes = _cleanupTargets.Where(item => item.IsSelected).Sum(item => item.EstimatedBytes);
                        _latestSnapshot = _latestSnapshot.WithCleanupEstimate(_lastCleanupEstimateBytes);
                        UpdateSelectedCleanupSummary();
                        UpdateHomeActionDock();
                        LastCleanupStatusTextBlock.Text = BuildLastCleanupText();
                        CleanupHintTextBlock.Text = ShouldShowCleanupRecommendation()
                            ? $"Рекомендуется плановая очистка. Текущий сценарий может освободить около {SystemCleanupService.FormatBytes(_lastCleanupEstimateBytes)}."
                            : "Выберите профиль очистки, оцените объём и при необходимости запустите сценарий обслуживания.";
                    }
                    finally
                    {
                        if (currentShowBusy)
                            SetCleanupBusy(false);
                    }

                    currentShowBusy = _cleanupAnalysisQueuedWithBusy;
                }
                while (_cleanupAnalysisQueued && _isPageActive);
            }
            finally
            {
                _isAnalyzingCleanup = false;
            }
        }

        private void UpdateSelectedCleanupSummary()
        {
            int selectedCount = _cleanupTargets.Count(item => item.IsSelected);
            string profileName = GetCleanupProfileTitle(_currentCleanupProfile);
            string bytesText = _lastCleanupEstimateBytes > 0
                ? SystemCleanupService.FormatBytes(_lastCleanupEstimateBytes)
                : "почти ничего";

            if (selectedCount == 0)
            {
                CleanupSummaryTextBlock.Text = "Профиль отключён. Выберите один из уровней или включите отдельные пункты вручную.";
                CleanupMiniValueTextBlock.Text = "Профиль отключён";
                return;
            }

            string guide = _currentCleanupProfile switch
            {
                CleanupProfile.Safe => "Безопасный уровень очищает только очевидные временные файлы и кэши.",
                CleanupProfile.Advanced => "Расширенный уровень добавляет дополнительные кэши, которые восстановятся автоматически.",
                CleanupProfile.Maximum => "Максимальный уровень включает все доступные пункты, включая корзину.",
                CleanupProfile.Custom => "Выбран пользовательский набор пунктов очистки.",
                _ => "Сценарий очистки готов к запуску."
            };

            CleanupSummaryTextBlock.Text = $"Профиль: {profileName}. Выбрано пунктов: {selectedCount}. Потенциал — {bytesText}. {guide}";
            CleanupMiniValueTextBlock.Text = _lastCleanupEstimateBytes > 0
                ? $"Потенциал {bytesText}"
                : $"{profileName}: требуется оценка";
        }

        private void UpdateHomeActionDock()
        {
            HomeActionDock.Visibility = Visibility.Collapsed;
            PublishCleanupRecommendationNotificationIfNeeded();
            HomeActionTitleTextBlock.Text = "Плановая очистка";
            HomeActionDescriptionTextBlock.Text = string.Empty;

            CleanupRecommendationBadge.Visibility = Visibility.Collapsed;
            CleanupRecommendationActionsPanel.Visibility = Visibility.Collapsed;
            CleanupRecommendationTextBlock.Text = string.Empty;
        }

        private bool ShouldShowCleanupRecommendation()
        {
            if (_lastCleanupEstimateBytes < 250L * 1024 * 1024)
            {
                return false;
            }

            var settings = App.SettingsManager.CurrentSettings;
            if (settings.CoreCleanupRecommendationDismissedUntilUtc.HasValue &&
                settings.CoreCleanupRecommendationDismissedUntilUtc.Value > DateTime.UtcNow)
            {
                return false;
            }

            return !settings.LastCoreCleanupCompletedAtUtc.HasValue ||
                   settings.LastCoreCleanupCompletedAtUtc.Value <= DateTime.UtcNow.AddDays(-7);
        }

        private void PublishCleanupRecommendationNotificationIfNeeded()
        {
            if (!ShouldShowCleanupRecommendation() ||
                App.SettingsManager?.CurrentSettings.ShowNotifications != true ||
                App.NotificationManager == null)
            {
                return;
            }

            var settings = App.SettingsManager.CurrentSettings;
            DateTime nowUtc = DateTime.UtcNow;
            if (settings.LastCoreCleanupRecommendationNotifiedAtUtc.HasValue &&
                settings.LastCoreCleanupRecommendationNotifiedAtUtc.Value > nowUtc.AddDays(-7))
            {
                return;
            }

            string estimate = SystemCleanupService.FormatBytes(_lastCleanupEstimateBytes);
            App.NotificationManager.RemoveByTitle("Плановая очистка");
            App.NotificationManager.AddNotification(
                "Плановая очистка",
                $"Накоплены временные данные: около {estimate}. Откройте расширенное ядро, чтобы оценить и запустить очистку.",
                NotificationManager.ActionOpenCoreHome);

            settings.LastCoreCleanupRecommendationNotifiedAtUtc = nowUtc;
            App.SettingsManager.SaveSettings();
        }

        private string BuildLastCleanupText()
        {
            var settings = App.SettingsManager.CurrentSettings;
            return settings.LastCoreCleanupCompletedAtUtc.HasValue
                ? $"Последняя очистка: {settings.LastCoreCleanupCompletedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
                : "Очистка ещё не запускалась.";
        }

        private void InitializeCleanupTargets()
        {
            _cleanupTargets.Clear();
            foreach (var target in _cleanupService.BuildTargets())
            {
                _cleanupTargets.Add(new CleanupTargetViewModel(target)
                {
                    IsSelected = target.IsQuickDefault
                });
            }
        }

        private void ApplyCleanupProfile(CleanupProfile profile, bool updateSummary = true, bool triggerAnalyze = true)
        {
            var selectedIds = GetCleanupProfileTargetIds(profile);
            _suppressCleanupSelectionChanged = true;
            try
            {
                foreach (var target in _cleanupTargets)
                    target.IsSelected = selectedIds.Contains(target.Id);
            }
            finally
            {
                _suppressCleanupSelectionChanged = false;
            }

            _currentCleanupProfile = profile;
            UpdateCleanupProfileButtons();

            if (updateSummary)
                UpdateSelectedCleanupSummary();

            if (triggerAnalyze)
                _ = AnalyzeCleanupAsync(showBusy: false);
        }

        private HashSet<string> GetCleanupProfileTargetIds(CleanupProfile profile)
        {
            var quick = _cleanupTargets.Where(item => item.Target.IsQuickDefault).Select(item => item.Id);
            var advancedExtra = new[] { "directx-cache" };
            var maximumExtra = new[] { "recycle-bin" };

            return profile switch
            {
                CleanupProfile.Safe => quick.ToHashSet(StringComparer.OrdinalIgnoreCase),
                CleanupProfile.Advanced => quick.Concat(advancedExtra).ToHashSet(StringComparer.OrdinalIgnoreCase),
                CleanupProfile.Maximum => _cleanupTargets.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
                CleanupProfile.Disabled => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                _ => _cleanupTargets.Where(item => item.IsSelected).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            };
        }

        private void UpdateCleanupProfileButtons()
        {
            SafeCleanupProfileButton.IsChecked = _currentCleanupProfile == CleanupProfile.Safe;
            AdvancedCleanupProfileButton.IsChecked = _currentCleanupProfile == CleanupProfile.Advanced;
            MaximumCleanupProfileButton.IsChecked = _currentCleanupProfile == CleanupProfile.Maximum;
            DisableCleanupProfileButton.IsChecked = _currentCleanupProfile == CleanupProfile.Disabled;
        }

        private CleanupProfile DetermineCleanupProfileFromSelection()
        {
            var selected = _cleanupTargets.Where(item => item.IsSelected).Select(item => item.Id).OrderBy(item => item).ToArray();
            bool EqualProfile(CleanupProfile profile) => selected.SequenceEqual(GetCleanupProfileTargetIds(profile).OrderBy(item => item), StringComparer.OrdinalIgnoreCase);

            if (EqualProfile(CleanupProfile.Disabled))
                return CleanupProfile.Disabled;
            if (EqualProfile(CleanupProfile.Safe))
                return CleanupProfile.Safe;
            if (EqualProfile(CleanupProfile.Advanced))
                return CleanupProfile.Advanced;
            if (EqualProfile(CleanupProfile.Maximum))
                return CleanupProfile.Maximum;
            return CleanupProfile.Custom;
        }

        private static string GetCleanupProfileTitle(CleanupProfile profile)
        {
            return profile switch
            {
                CleanupProfile.Safe => "Безопасно",
                CleanupProfile.Advanced => "Расширенно",
                CleanupProfile.Maximum => "Максимум",
                CleanupProfile.Disabled => "Выключено",
                CleanupProfile.Custom => "Свой набор",
                _ => "Очистка"
            };
        }

        private async void AnalyzeCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeCleanupAsync(showBusy: true);
            await RefreshCoreDiagnosticsAsync();
        }

        private async void RunCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _cleanupTargets.Where(item => item.IsSelected).Select(item => item.Target).ToList();
            if (selected.Count == 0)
            {
                CleanupSummaryTextBlock.Text = "Для запуска очистки выберите хотя бы один пункт.";
                return;
            }

            SetCleanupBusy(true, "Подготавливаем очистку");
            try
            {
                _cleanupCts?.Cancel();
                _cleanupCts?.Dispose();
                _cleanupCts = new CancellationTokenSource();

                var progress = new Progress<string>(name => SetCleanupBusy(true, $"Очищаем: {name}"));
                var result = await _cleanupService.CleanAsync(selected, progress, _cleanupCts.Token);

                CleanupSummaryTextBlock.Text = result.FreedBytes > 0
                    ? $"Очистка завершена: освобождено {SystemCleanupService.FormatBytes(result.FreedBytes)}, удалено файлов: {result.DeletedFiles}, пропущено занятых файлов: {result.SkippedFiles}."
                    : $"Очистка завершена. Подходящих свободных файлов почти не найдено, пропущено занятых файлов: {result.SkippedFiles}.";

                var settings = App.SettingsManager.CurrentSettings;
                settings.LastCoreCleanupCompletedAtUtc = DateTime.UtcNow;
                settings.CoreCleanupRecommendationDismissedUntilUtc = DateTime.UtcNow.AddDays(7);
                App.SettingsManager.SaveSettings();

                await AnalyzeCleanupAsync(showBusy: false);
                await _healthService.RefreshStatusAsync();
                await RefreshSystemSnapshotAsync();
                await RefreshCoreDiagnosticsAsync();
            }
            catch (OperationCanceledException)
            {
                CleanupSummaryTextBlock.Text = "Очистка отменена.";
            }
            finally
            {
                SetCleanupBusy(false);
            }
        }

        private void SetCleanupBusy(bool isBusy, string message = null)
        {
            _cleanupBusy = isBusy;
            AnalyzeCleanupButton.IsEnabled = !isBusy;
            RunCleanupButton.IsEnabled = !isBusy;

            if (!string.IsNullOrWhiteSpace(message))
                CleanupProgressTextBlock.Text = message;

            if (isBusy)
            {
                CleanupBusyOverlay.Visibility = Visibility.Visible;
                CleanupBusyOverlay.Opacity = 0;
                AnimateOpacity(CleanupBusyOverlay, 1, 180);
                StartCleanupLoadingSquares();
            }
            else
            {
                AnimateOpacity(CleanupBusyOverlay, 0, 180, () => CleanupBusyOverlay.Visibility = Visibility.Collapsed);
                StopCleanupLoadingSquares();
            }
        }

        private FrameworkElement[] GetCleanupLoadingSquares()
        {
            return new FrameworkElement[]
            {
                CleanupLoadingSquareA,
                CleanupLoadingSquareB,
                CleanupLoadingSquareC,
                CleanupLoadingSquareD
            };
        }

        private void StartCleanupLoadingSquares()
        {
            var squares = GetCleanupLoadingSquares();
            for (int index = 0; index < squares.Length; index++)
            {
                var square = squares[index];
                if (square == null)
                    continue;

                var scale = new ScaleTransform(0.86, 0.86);
                square.RenderTransform = scale;
                square.Opacity = 0.32;

                var beginTime = TimeSpan.FromMilliseconds(index * 130);
                var opacity = new DoubleAnimation(0.32, 1, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = beginTime,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                var size = new DoubleAnimation(0.86, 1.08, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = beginTime,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                square.BeginAnimation(UIElement.OpacityProperty, opacity);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, size.Clone());
            }
        }

        private void StopCleanupLoadingSquares()
        {
            foreach (var square in GetCleanupLoadingSquares())
            {
                if (square == null)
                    continue;

                square.BeginAnimation(UIElement.OpacityProperty, null);
                if (square.RenderTransform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
            }
        }

        private void CleanupProfileToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == SafeCleanupProfileButton)
                ApplyCleanupProfile(CleanupProfile.Safe);
            else if (sender == AdvancedCleanupProfileButton)
                ApplyCleanupProfile(CleanupProfile.Advanced);
            else if (sender == MaximumCleanupProfileButton)
                ApplyCleanupProfile(CleanupProfile.Maximum);
            else if (sender == DisableCleanupProfileButton)
                ApplyCleanupProfile(CleanupProfile.Disabled);
        }

        private async void CleanupTargetCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressCleanupSelectionChanged)
                return;

            _currentCleanupProfile = DetermineCleanupProfileFromSelection();
            UpdateCleanupProfileButtons();
            UpdateSelectedCleanupSummary();
            await AnalyzeCleanupAsync(showBusy: false);
            await RefreshCoreDiagnosticsAsync();
        }

        private void CoreDetailTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button &&
                button.Tag is string tag &&
                Enum.TryParse(tag, ignoreCase: true, out CoreDetailMode mode))
            {
                SelectCoreDetailPanel(mode, animate: true);
            }
        }

        private void SelectCoreDetailPanel(CoreDetailMode mode, bool animate)
        {
            _selectedCoreDetailMode = mode;

            CoreMonitorPanel.Visibility = mode == CoreDetailMode.Monitor ? Visibility.Visible : Visibility.Collapsed;
            CoreDiagnosticsPanel.Visibility = mode == CoreDetailMode.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
            CoreCleanupPanel.Visibility = mode == CoreDetailMode.Cleanup ? Visibility.Visible : Visibility.Collapsed;

            SetCoreDetailTabState(DetailsMonitorCard, mode == CoreDetailMode.Monitor);
            SetCoreDetailTabState(DetailsDiagnosticsCard, mode == CoreDetailMode.Diagnostics);
            SetCoreDetailTabState(DetailsCleanupCard, mode == CoreDetailMode.Cleanup);

            if (animate)
            {
                CoreDetailWorkbench.Opacity = Math.Max(CoreDetailWorkbench.Opacity, 0.7);
                AnimateOpacity(CoreDetailWorkbench, 1, 180);
            }
        }

        private void SetCoreDetailTabState(System.Windows.Controls.Button button, bool selected)
        {
            if (button == null)
                return;

            button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, selected ? "CoreLineActiveBrush" : "CoreNodeBorderBrush");
            if (selected)
            {
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CoreNodeBackgroundBrush");
            }
            else
            {
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ContentBackground");
            }
        }

        private void MainCore_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowCoreDetails();
        }

        private void ExpandedCore_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            HideCoreDetails();
        }

        private void ModuleNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                OpenModule(node.ModuleId);
        }

        private void ModuleNode_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                AnimateConnectionLine(node.ModuleId, true, LineFlowDirection.CoreToCard);
        }

        private void ModuleNode_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                AnimateConnectionLine(node.ModuleId, false, LineFlowDirection.CoreToCard);
        }

        private void MainCore_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateAllConnectionLines(true, LineFlowDirection.CardToCore);
        }

        private void MainCore_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateAllConnectionLines(false, LineFlowDirection.CardToCore);
        }

        private void OpenModule(CoreModuleId moduleId)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.OpenModuleWorkspace(moduleId);
        }

        private async void CheckSystemButton_Click(object sender, RoutedEventArgs e)
        {
            await _healthService.RefreshStatusAsync();
            RenderHealthStatus();
            await RefreshTemperaturesAsync();
            await RefreshSystemSnapshotAsync();
            await RefreshCoreDiagnosticsAsync();
            await AnalyzeCleanupAsync(showBusy: true);
        }

        private void OpenCleanupRecommendationButton_Click(object sender, RoutedEventArgs e)
        {
            ShowCoreDetails();
            SelectCoreDetailPanel(CoreDetailMode.Cleanup, animate: true);
        }

        private void DismissCleanupRecommendationButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.SettingsManager.CurrentSettings;
            settings.CoreCleanupRecommendationDismissedUntilUtc = DateTime.UtcNow.AddDays(7);
            App.SettingsManager.SaveSettings();
            UpdateHomeActionDock();
            _ = RefreshCoreDiagnosticsAsync();
        }

        private void ShowCoreDetails()
        {
            if (_detailsOpened)
                return;

            _detailsOpened = true;
            CoreDetailsLayer.Visibility = Visibility.Visible;

            AnimateOpacity(MapLayer, 0, 260);
            AnimateOpacity(TemperatureDock, 0, 220);
            AnimateScale(MapScale, 0.95, 340, new CubicEase { EasingMode = EasingMode.EaseInOut });

            CoreDetailsLayer.Opacity = 0;
            CoreDetailsScale.ScaleX = 0.88;
            CoreDetailsScale.ScaleY = 0.88;
            SelectCoreDetailPanel(_selectedCoreDetailMode, animate: false);
            AnimateOpacity(CoreDetailsLayer, 1, 420);
            AnimateScale(CoreDetailsScale, 1, 520, new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut });

            AnimateCardReveal(DetailsHeaderCard, HeaderCardTranslate, 0, -18, 40);
            AnimateCardReveal(DetailsMonitorCard, MonitorCardTranslate, -26, 0, 90);
            AnimateCardReveal(DetailsDiagnosticsCard, DiagnosticsCardTranslate, -26, 0, 125);
            AnimateCardReveal(DetailsCleanupCard, CleanupCardTranslate, -26, 0, 160);
            AnimateCardReveal(CoreDetailWorkbench, WorkbenchCardTranslate, 26, 0, 120);
            _ = AnalyzeCleanupAsync(showBusy: false);
        }

        private void HideCoreDetails()
        {
            if (!_detailsOpened)
                return;

            _detailsOpened = false;
            AnimateOpacity(MapLayer, 1, 320);
            AnimateOpacity(TemperatureDock, 0.96, 260);
            AnimateScale(MapScale, 1, 320, new CubicEase { EasingMode = EasingMode.EaseInOut });
            AnimateOpacity(CoreDetailsLayer, 0, 260, () => CoreDetailsLayer.Visibility = Visibility.Collapsed);
            AnimateScale(CoreDetailsScale, 0.88, 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        }

        private static void AnimateCardReveal(FrameworkElement element, TranslateTransform transform, double fromX, double fromY, int delayMs)
        {
            if (element == null || transform == null)
                return;

            element.Opacity = 0;
            transform.X = fromX;
            transform.Y = fromY;

            var opacityAnimation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(340))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(OpacityProperty, opacityAnimation);

            var xAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var yAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.XProperty, xAnimation);
            transform.BeginAnimation(TranslateTransform.YProperty, yAnimation);
        }

        private void AnimateAllConnectionLines(bool show, LineFlowDirection direction)
        {
            AnimateConnectionLine(CoreModuleId.WindowsSetup, show, direction);
            AnimateConnectionLine(CoreModuleId.SystemParameters, show, direction);
            AnimateConnectionLine(CoreModuleId.Resources, show, direction);
            AnimateConnectionLine(CoreModuleId.Maintenance, show, direction);
            AnimateConnectionLine(CoreModuleId.Devices, show, direction);
            AnimateConnectionLine(CoreModuleId.Network, show, direction);
        }

        private void AnimateConnectionLine(CoreModuleId moduleId, bool show, LineFlowDirection direction)
        {
            var line = GetConnectionLine(moduleId);
            if (line == null)
                return;

            ApplyConnectionLineDirection(line, moduleId, direction);

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var opacity = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 190 : 160))
            {
                EasingFunction = ease
            };
            line.BeginAnimation(OpacityProperty, opacity);

            if (!show)
                return;

            line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            line.StrokeDashOffset = ConnectionLineDashOffset;

            var flow = new DoubleAnimation(ConnectionLineDashOffset, 0, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            line.BeginAnimation(Shape.StrokeDashOffsetProperty, flow);
        }

        private static void ApplyConnectionLineDirection(Line line, CoreModuleId moduleId, LineFlowDirection direction)
        {
            var points = GetConnectionLinePoints(moduleId);
            WindowsPoint start = direction == LineFlowDirection.CardToCore ? points.CardPoint : points.CorePoint;
            WindowsPoint end = direction == LineFlowDirection.CardToCore ? points.CorePoint : points.CardPoint;

            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
        }

        private static ConnectionLinePoints GetConnectionLinePoints(CoreModuleId moduleId)
        {
            return moduleId switch
            {
                CoreModuleId.WindowsSetup => new ConnectionLinePoints(new WindowsPoint(550, 116), new WindowsPoint(550, 284)),
                CoreModuleId.SystemParameters => new ConnectionLinePoints(new WindowsPoint(340, 207), new WindowsPoint(482, 317)),
                CoreModuleId.Maintenance => new ConnectionLinePoints(new WindowsPoint(340, 427), new WindowsPoint(467, 392)),
                CoreModuleId.Network => new ConnectionLinePoints(new WindowsPoint(550, 512), new WindowsPoint(550, 456)),
                CoreModuleId.Resources => new ConnectionLinePoints(new WindowsPoint(760, 207), new WindowsPoint(618, 317)),
                CoreModuleId.Devices => new ConnectionLinePoints(new WindowsPoint(760, 427), new WindowsPoint(633, 392)),
                _ => new ConnectionLinePoints(new WindowsPoint(), new WindowsPoint())
            };
        }

        private Line GetConnectionLine(CoreModuleId moduleId)
        {
            return moduleId switch
            {
                CoreModuleId.WindowsSetup => WindowsSetupActiveLine,
                CoreModuleId.SystemParameters => SystemParametersActiveLine,
                CoreModuleId.Resources => ResourcesActiveLine,
                CoreModuleId.Maintenance => StorageActiveLine,
                CoreModuleId.Devices => DevicesActiveLine,
                CoreModuleId.Network => NetworkActiveLine,
                _ => null
            };
        }

        private static void AnimateOpacity(UIElement element, double target, int milliseconds, Action completed = null)
        {
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            if (completed != null)
                animation.Completed += (sender, args) => completed();

            element.BeginAnimation(OpacityProperty, animation);
        }

        private static void AnimateScale(ScaleTransform scale, double target, int milliseconds, IEasingFunction easing = null)
        {
            easing ??= new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var animationX = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            var animationY = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animationX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animationY);
        }

        private double SampleCpuUsage()
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                return _lastCpuUsagePercent;

            ulong idle = ToUInt64(idleTime);
            ulong kernel = ToUInt64(kernelTime);
            ulong user = ToUInt64(userTime);

            lock (_cpuSync)
            {
                if (!_hasCpuBaseline)
                {
                    _lastIdleTicks = idle;
                    _lastKernelTicks = kernel;
                    _lastUserTicks = user;
                    _hasCpuBaseline = true;
                    return _lastCpuUsagePercent;
                }

                ulong idleDelta = idle - _lastIdleTicks;
                ulong kernelDelta = kernel - _lastKernelTicks;
                ulong userDelta = user - _lastUserTicks;
                ulong totalDelta = kernelDelta + userDelta;

                _lastIdleTicks = idle;
                _lastKernelTicks = kernel;
                _lastUserTicks = user;

                if (totalDelta == 0)
                    return _lastCpuUsagePercent;

                _lastCpuUsagePercent = Math.Clamp((1d - idleDelta / (double)totalDelta) * 100d, 0d, 100d);
                return _lastCpuUsagePercent;
            }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays} д {uptime.Hours} ч";
            return $"{uptime.Hours} ч {uptime.Minutes} мин";
        }

        private static MEMORYSTATUSEX GetMemoryStatus()
        {
            var memoryStatus = new MEMORYSTATUSEX();
            GlobalMemoryStatusEx(memoryStatus);
            return memoryStatus;
        }

        private static ulong ToUInt64(FILETIME fileTime)
        {
            return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private sealed class SystemStatViewModel
        {
            public string Icon { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public double Percent { get; set; }
        }

        private sealed class QuickDiagnosticViewModel
        {
            public string Icon { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public double Percent { get; set; }
        }

        private sealed class CleanupTargetViewModel : INotifyPropertyChanged
        {
            private bool _isSelected;
            private long _estimatedBytes;
            private string _estimateText = "Оценка ещё не выполнена";

            public CleanupTargetViewModel(SystemCleanupTarget target)
            {
                Target = target;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public SystemCleanupTarget Target { get; }
            public string Id => Target.Id;
            public string Title => Target.Title;
            public string Description => Target.Description;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    OnPropertyChanged();
                }
            }

            public long EstimatedBytes
            {
                get => _estimatedBytes;
                private set
                {
                    if (_estimatedBytes == value)
                        return;

                    _estimatedBytes = value;
                    OnPropertyChanged();
                }
            }

            public string EstimateText
            {
                get => _estimateText;
                private set
                {
                    if (string.Equals(_estimateText, value, StringComparison.Ordinal))
                        return;

                    _estimateText = value;
                    OnPropertyChanged();
                }
            }

            public void ApplyState(SystemCleanupTargetState state)
            {
                if (state == null)
                    return;

                EstimatedBytes = state.EstimatedBytes;
                EstimateText = state.Message;
            }

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private readonly struct SystemSnapshot
        {
            public static SystemSnapshot Empty => new SystemSnapshot(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, 0);

            public SystemSnapshot(double cpuUsagePercent, double memoryUsedPercent, long totalMemoryBytes, long availableMemoryBytes, double diskUsedPercent, long freeDiskBytes, long totalDiskBytes, int processCount, TimeSpan uptime, long cleanupEstimateBytes)
            {
                CpuUsagePercent = cpuUsagePercent;
                MemoryUsedPercent = memoryUsedPercent;
                TotalMemoryBytes = totalMemoryBytes;
                AvailableMemoryBytes = availableMemoryBytes;
                DiskUsedPercent = diskUsedPercent;
                FreeDiskBytes = freeDiskBytes;
                TotalDiskBytes = totalDiskBytes;
                ProcessCount = processCount;
                Uptime = uptime;
                CleanupEstimateBytes = cleanupEstimateBytes;
            }

            public double CpuUsagePercent { get; }
            public double MemoryUsedPercent { get; }
            public long TotalMemoryBytes { get; }
            public long AvailableMemoryBytes { get; }
            public double DiskUsedPercent { get; }
            public long FreeDiskBytes { get; }
            public long TotalDiskBytes { get; }
            public int ProcessCount { get; }
            public TimeSpan Uptime { get; }
            public long CleanupEstimateBytes { get; }

            public SystemSnapshot WithCleanupEstimate(long cleanupEstimateBytes)
                => new SystemSnapshot(CpuUsagePercent, MemoryUsedPercent, TotalMemoryBytes, AvailableMemoryBytes, DiskUsedPercent, FreeDiskBytes, TotalDiskBytes, ProcessCount, Uptime, cleanupEstimateBytes);
        }

        private enum CoreDetailMode
        {
            Monitor,
            Diagnostics,
            Cleanup
        }

        private enum CleanupProfile
        {
            Safe,
            Advanced,
            Maximum,
            Disabled,
            Custom
        }

        private enum LineFlowDirection
        {
            CardToCore,
            CoreToCard
        }

        private readonly struct ConnectionLinePoints
        {
            public ConnectionLinePoints(WindowsPoint cardPoint, WindowsPoint corePoint)
            {
                CardPoint = cardPoint;
                CorePoint = corePoint;
            }

            public WindowsPoint CardPoint { get; }
            public WindowsPoint CorePoint { get; }
        }
    }
}
