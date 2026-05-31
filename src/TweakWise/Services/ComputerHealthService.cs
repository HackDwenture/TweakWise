using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TweakWise.Managers;
using TweakWise.Models;
using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace TweakWise.Services
{
    public sealed class ComputerHealthService : IComputerHealthService
    {
        private readonly SettingsManager _settingsManager;
        private readonly List<CoreModuleDefinition> _modules;
        private HashSet<string> _lastProblemFindingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ComputerHealthStatus _overallStatus;

        static ComputerHealthService()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
            }
        }

        public ComputerHealthService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _modules = BuildModuleDefinitions();
            _overallStatus = LoadLastKnownStatus();
            ApplyUnknownModuleStatuses();
        }

        public event EventHandler HealthStatusChanged;

        public ComputerHealthStatus GetOverallStatus()
        {
            return CloneStatus(_overallStatus);
        }

        public IReadOnlyList<CoreModuleDefinition> GetModules()
        {
            return _modules.Select(CloneModule).ToList();
        }

        public CoreModuleDefinition GetModule(CoreModuleId moduleId)
        {
            var module = _modules.FirstOrDefault(item => item.Id == moduleId);

            if (module == null && moduleId == CoreModuleId.PowerThermal)
                module = _modules.FirstOrDefault(item => item.Id == CoreModuleId.SystemParameters);

            return module == null ? null : CloneModule(module);
        }

        public void SnoozeFindings(IEnumerable<string> findingIds, TimeSpan duration)
        {
            _settingsManager.SnoozeHealthSignals(findingIds, duration);
        }

        public void DismissFindings(IEnumerable<string> findingIds)
        {
            _settingsManager.DismissHealthSignals(findingIds);
        }

        public async Task RefreshStatusAsync()
        {
            SetCheckingState();

            var result = await Task.Run(RunSafeHealthChecks);
            _overallStatus = result.OverallStatus;

            foreach (var moduleStatus in result.ModuleStatuses)
            {
                var module = _modules.FirstOrDefault(item => item.Id == moduleStatus.ModuleId);
                if (module != null)
                    module.Status = moduleStatus;
            }

            SaveLastKnownStatus();
            PublishProblemNotifications(result.ModuleStatuses);
            OnHealthStatusChanged();
        }

        private void SetCheckingState()
        {
            var now = DateTime.Now;
            _overallStatus = new ComputerHealthStatus
            {
                OverallStatus = HealthLevel.Checking,
                LastCheckedAt = now
            };

            foreach (var module in _modules)
            {
                module.Status = new ModuleHealthStatus
                {
                    ModuleId = module.Id,
                    Title = module.Title,
                    Status = HealthLevel.Checking,
                    LastCheckedAt = now
                };
            }

            OnHealthStatusChanged();
        }

        private HealthCheckSnapshot RunSafeHealthChecks()
        {
            var now = DateTime.Now;
            var moduleStatuses = _modules.ToDictionary(
                module => module.Id,
                module => new ModuleHealthStatus
                {
                    ModuleId = module.Id,
                    Title = module.Title,
                    Status = HealthLevel.Unknown,
                    LastCheckedAt = now
                });

            var findings = new HealthFindingAccumulator();

            CheckSystemDrive(moduleStatuses, findings);
            CheckRestartState(moduleStatuses, findings);
            CheckPerformanceState(moduleStatuses, findings);
            CheckNetworkState(moduleStatuses, findings);

            MarkUntouchedModulesAsGood(moduleStatuses);
            var overall = ApplySuppressionsAndRecalculate(moduleStatuses, findings.PendingRestart, now);

            return new HealthCheckSnapshot(overall, moduleStatuses.Values.ToList());
        }

        private static void CheckSystemDrive(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            var diskCheck = CheckSystemDrive();
            var target = moduleStatuses[CoreModuleId.Maintenance];

            if (!diskCheck.SystemDriveReady)
            {
                findings.ProblemCount++;
                SetModuleStatus(
                    target,
                    HealthLevel.Warning,
                    problems: 1,
                    finding: new ModuleHealthFinding
                    {
                        Level = HealthLevel.Warning,
                        Title = "Системный диск недоступен",
                        Description = "Проверка не смогла получить сведения о системном диске.",
                        ActionText = "Проверьте состояние накопителя и права доступа к сведениям о дисках."
                    });
                return;
            }

            if (!diskCheck.FreeBytes.HasValue || !diskCheck.TotalBytes.HasValue)
                return;

            double freeRatio = diskCheck.TotalBytes.Value == 0
                ? 0
                : diskCheck.FreeBytes.Value / (double)diskCheck.TotalBytes.Value;

            if (diskCheck.FreeBytes.Value < 1L * 1024 * 1024 * 1024)
            {
                findings.CriticalCount++;
                findings.ProblemCount++;
                SetModuleStatus(
                    target,
                    HealthLevel.Critical,
                    problems: 1,
                    finding: new ModuleHealthFinding
                    {
                        Level = HealthLevel.Critical,
                        Title = "Критически мало места на системном диске",
                        Description = "Свободно меньше 1 ГБ. Это может мешать обновлениям и стабильной работе Windows.",
                        ActionText = "Освободите место в разделе накопителей и памяти."
                    });
            }
            else if (diskCheck.FreeBytes.Value < 5L * 1024 * 1024 * 1024 || freeRatio < 0.08)
            {
                findings.ProblemCount++;
                SetModuleStatus(
                    target,
                    HealthLevel.Warning,
                    problems: 1,
                    finding: new ModuleHealthFinding
                    {
                        Level = HealthLevel.Warning,
                        Title = "Мало места на системном диске",
                        Description = $"Свободно {FormatBytes(diskCheck.FreeBytes.Value)} из {FormatBytes(diskCheck.TotalBytes.Value)}.",
                        ActionText = "Удалите временные файлы или перенесите часть данных на другой диск."
                    });
            }
            else if (freeRatio < 0.15)
            {
                findings.RecommendationCount++;
                SetModuleStatus(
                    target,
                    HealthLevel.Normal,
                    recommendations: 1,
                    finding: new ModuleHealthFinding
                    {
                        Level = HealthLevel.Normal,
                        Title = "Свободного места становится мало",
                        Description = $"Свободно {FormatBytes(diskCheck.FreeBytes.Value)} из {FormatBytes(diskCheck.TotalBytes.Value)}.",
                        ActionText = "Запланируйте очистку, чтобы не упереться в нехватку места позже."
                    });
            }
            else
            {
                SetModuleStatus(target, HealthLevel.Good);
            }
        }

        private void CheckRestartState(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            var restartCheck = CheckPendingRestart();
            AddTweakWiseRestartRequest(restartCheck);
            bool pendingRestart = restartCheck.IsPending;
            findings.PendingRestart = pendingRestart;

            if (!pendingRestart)
            {
                SetModuleStatus(moduleStatuses[CoreModuleId.SystemParameters], HealthLevel.Good);
                return;
            }

            findings.RecommendationCount++;
            SetModuleStatus(
                moduleStatuses[CoreModuleId.SystemParameters],
                HealthLevel.Normal,
                recommendations: 1,
                finding: CreatePendingRestartFinding(restartCheck));
        }

        private void AddTweakWiseRestartRequest(PendingRestartCheck restartCheck)
        {
            if (restartCheck == null || _settingsManager == null)
                return;

            if (!_settingsManager.HasActiveTweakWiseRestartRequest())
                return;

            string reason = _settingsManager.CurrentSettings.PendingRestartReason;
            AddDistinct(
                restartCheck.AppSources,
                string.IsNullOrWhiteSpace(reason) ? "изменения TweakWise" : reason);
        }

        private static void CheckNetworkState(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            bool available;
            try
            {
                available = NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                available = false;
            }

            if (available)
            {
                SetModuleStatus(moduleStatuses[CoreModuleId.Network], HealthLevel.Good);
                return;
            }

            findings.RecommendationCount++;
            SetModuleStatus(
                moduleStatuses[CoreModuleId.Network],
                HealthLevel.Normal,
                recommendations: 1,
                finding: new ModuleHealthFinding
                {
                    Level = HealthLevel.Normal,
                    Title = "Сеть недоступна",
                    Description = "Windows не сообщает о доступном сетевом подключении.",
                    ActionText = "Проверьте адаптер, Wi-Fi, кабель или состояние подключения."
                });
        }

        private static void CheckPerformanceState(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            var target = moduleStatuses[CoreModuleId.Resources];
            var performanceFindings = new List<ModuleHealthFinding>();

            try
            {
                using var temperatureService = new HardwareTemperatureService();
                var readings = temperatureService.GetTemperatures() ?? Array.Empty<TemperatureSensorReading>();

                AddTemperatureFinding(
                    performanceFindings,
                    readings,
                    "Cpu",
                    "CPU нагревается",
                    "Температура процессора приближается к зоне троттлинга.",
                    "Проверьте охлаждение, схему питания и нагрузку перед включением тяжёлых профилей.",
                    90,
                    82);

                AddTemperatureFinding(
                    performanceFindings,
                    readings,
                    "Gpu",
                    "GPU нагревается",
                    "Температура видеокарты приближается к зоне снижения частот.",
                    "Проверьте вентиляцию корпуса, драйверный профиль и тепловой запас.",
                    87,
                    80);

                float hottestPerformanceTemp = readings
                    .Where(item => item.Group == "Cpu" || item.Group == "Gpu" || item.Group == "Motherboard" || item.Group == "Other")
                    .Select(item => item.ValueCelsius)
                    .DefaultIfEmpty(0)
                    .Max();

                if (hottestPerformanceTemp >= 86)
                {
                    performanceFindings.Add(new ModuleHealthFinding
                    {
                        Level = HealthLevel.Warning,
                        Title = "Термоконтур перегружен",
                        Description = $"Самый горячий датчик показывает {HardwareTemperatureService.FormatTemperature(hottestPerformanceTemp)}.",
                        ActionText = "Проверьте кривую вентиляторов, пыль и режим питания."
                    });
                }
            }
            catch
            {
            }

            if (IsRunningOnBattery())
            {
                performanceFindings.Add(new ModuleHealthFinding
                {
                    Id = "resources.power.on-battery",
                    ModuleId = CoreModuleId.Resources,
                    Level = HealthLevel.Normal,
                    Title = "Питание от батареи",
                    Description = "Windows может ограничивать частоты и охлаждение при работе без сети.",
                    ActionText = "Для тяжёлых задач включите питание от сети или производительный профиль."
                });
            }

            AddPowerDiagnosticFindings(performanceFindings);
            AddMemoryFindings(performanceFindings);

            if (performanceFindings.Count == 0)
            {
                SetModuleStatus(target, HealthLevel.Good);
                return;
            }

            foreach (var finding in performanceFindings)
            {
                bool isProblem = finding.Level == HealthLevel.Warning || finding.Level == HealthLevel.Critical;
                if (isProblem)
                    findings.ProblemCount++;
                else
                    findings.RecommendationCount++;

                if (finding.Level == HealthLevel.Critical)
                    findings.CriticalCount++;

                SetModuleStatus(
                    target,
                    finding.Level,
                    problems: isProblem ? 1 : 0,
                    recommendations: isProblem ? 0 : 1,
                    finding: finding);
            }
        }

        private static void AddPowerDiagnosticFindings(List<ModuleHealthFinding> findings)
        {
            var requests = RunPowerCfg("/requests");
            if (requests.Success && !IsEmptyPowerCfgDiagnostic(requests.Output))
            {
                string summary = SummarizePowerCfgOutput(requests.Output, 8);
                findings.Add(new ModuleHealthFinding
                {
                    Id = "performance.setting.power.active-requests",
                    ModuleId = CoreModuleId.Resources,
                    Level = HealthLevel.Warning,
                    Title = "Активные запросы питания",
                    Description = string.IsNullOrWhiteSpace(summary)
                        ? "Обнаружены процессы, драйверы или устройства, которые сейчас блокируют сон, отключение экрана или idle-сценарии."
                        : summary,
                    ActionText = "Проверьте указанные процессы или драйверы в узле «Питание»."
                });
            }

            var wakeArmed = RunPowerCfg("/devicequery", "wake_armed");
            if (wakeArmed.Success && !string.IsNullOrWhiteSpace(wakeArmed.Output))
            {
                string summary = SummarizePowerCfgOutput(wakeArmed.Output, 8);
                if (!string.IsNullOrWhiteSpace(summary) && !IsEmptyPowerCfgDiagnostic(summary))
                {
                    findings.Add(new ModuleHealthFinding
                    {
                        Id = "performance.setting.power.wake-armed-devices",
                        ModuleId = CoreModuleId.Resources,
                        Level = HealthLevel.Normal,
                        Title = "Устройства могут будить ПК",
                        Description = summary,
                        ActionText = "Если компьютер просыпается сам, начните проверку с этих устройств в узле «Питание»."
                    });
                }
            }
        }

        private static void AddTemperatureFinding(
            List<ModuleHealthFinding> findings,
            IReadOnlyList<TemperatureSensorReading> readings,
            string group,
            string title,
            string description,
            string actionText,
            float warningThreshold,
            float recommendationThreshold)
        {
            var hottest = readings
                .Where(item => string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.ValueCelsius)
                .FirstOrDefault();

            if (hottest == null || hottest.ValueCelsius < recommendationThreshold)
                return;

            findings.Add(new ModuleHealthFinding
            {
                Id = $"resources.{group.ToLowerInvariant()}.temperature",
                ModuleId = CoreModuleId.Resources,
                Level = hottest.ValueCelsius >= warningThreshold ? HealthLevel.Warning : HealthLevel.Normal,
                Title = title,
                Description = $"{description} Сейчас: {hottest.Title} {HardwareTemperatureService.FormatTemperature(hottest.ValueCelsius)}.",
                ActionText = actionText
            });
        }

        private static void AddMemoryFindings(List<ModuleHealthFinding> findings)
        {
            try
            {
                var memory = new MemoryStatusEx();
                if (!GlobalMemoryStatusEx(memory) || memory.ullTotalPhys == 0)
                    return;

                if (memory.dwMemoryLoad >= 92)
                {
                    findings.Add(new ModuleHealthFinding
                    {
                        Id = "resources.ram.critical-load",
                        ModuleId = CoreModuleId.Resources,
                        Level = HealthLevel.Critical,
                        Title = "Оперативная память почти исчерпана",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Возможны подвисания и активная работа файла подкачки.",
                        ActionText = "Закройте тяжёлые процессы и проверьте утечки памяти."
                    });
                }
                else if (memory.dwMemoryLoad >= 78)
                {
                    findings.Add(new ModuleHealthFinding
                    {
                        Id = "resources.ram.high-load",
                        ModuleId = CoreModuleId.Resources,
                        Level = HealthLevel.Warning,
                        Title = "Оперативная память сильно загружена",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Для тяжёлых задач это уже проблема.",
                        ActionText = "Освободите память или проверьте процессы, которые постоянно удерживают RAM."
                    });
                }
                else if (memory.dwMemoryLoad >= 70)
                {
                    findings.Add(new ModuleHealthFinding
                    {
                        Id = "resources.ram.elevated-load",
                        ModuleId = CoreModuleId.Resources,
                        Level = HealthLevel.Normal,
                        Title = "Запас оперативной памяти небольшой",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Это не критично, но запас памяти сейчас ограничен.",
                        ActionText = "Перед играми, рендером или виртуальными машинами закройте ненужные тяжёлые приложения."
                    });
                }
            }
            catch
            {
            }
        }

        private static bool IsRunningOnBattery()
        {
            try
            {
                return WinForms.SystemInformation.PowerStatus.PowerLineStatus == WinForms.PowerLineStatus.Offline;
            }
            catch
            {
                return false;
            }
        }

        private static void MarkUntouchedModulesAsGood(Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses)
        {
            foreach (var status in moduleStatuses.Values)
            {
                if (status.Status == HealthLevel.Unknown)
                    status.Status = HealthLevel.Good;
            }
        }

        private ComputerHealthStatus ApplySuppressionsAndRecalculate(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            bool pendingRestart,
            DateTime checkedAt)
        {
            int problemCount = 0;
            int recommendationCount = 0;
            int criticalCount = 0;

            foreach (var status in moduleStatuses.Values)
            {
                status.Findings = status.Findings
                    .Where(finding => finding != null &&
                                      !string.IsNullOrWhiteSpace(finding.Id) &&
                                      !_settingsManager.IsHealthSignalSuppressed(finding.Id))
                    .ToList();

                status.ProblemCount = status.Findings.Count(IsProblemLevel);
                status.CriticalCount = status.Findings.Count(finding => finding.Level == HealthLevel.Critical);
                status.RecommendationCount = status.Findings.Count - status.ProblemCount;
                status.Status = status.Findings.Count == 0
                    ? HealthLevel.Good
                    : status.Findings.OrderByDescending(finding => GetSeverity(finding.Level)).First().Level;
                status.LastCheckedAt = checkedAt;

                problemCount += status.ProblemCount;
                recommendationCount += status.RecommendationCount;
                criticalCount += status.CriticalCount;
            }

            bool visiblePendingRestart = pendingRestart &&
                moduleStatuses.TryGetValue(CoreModuleId.SystemParameters, out var systemStatus) &&
                systemStatus.Findings.Any(finding => string.Equals(finding.Title, "Ожидается перезагрузка", StringComparison.OrdinalIgnoreCase));

            return new ComputerHealthStatus
            {
                OverallStatus = ResolveOverallStatus(problemCount, recommendationCount, criticalCount),
                ProblemCount = problemCount,
                RecommendationCount = recommendationCount,
                CriticalCount = criticalCount,
                PendingRestart = visiblePendingRestart,
                LastCheckedAt = checkedAt
            };
        }

        private ComputerHealthStatus LoadLastKnownStatus()
        {
            var settings = _settingsManager.CurrentSettings;
            var level = ParseHealthLevel(settings.LastHealthLevel);

            return new ComputerHealthStatus
            {
                OverallStatus = level,
                ProblemCount = settings.LastHealthProblemCount,
                RecommendationCount = settings.LastHealthRecommendationCount,
                CriticalCount = settings.LastHealthCriticalCount,
                PendingRestart = settings.PendingRestart,
                LastCheckedAt = settings.LastHealthCheckedAt
            };
        }

        private void SaveLastKnownStatus()
        {
            var settings = _settingsManager.CurrentSettings;
            settings.LastHealthLevel = _overallStatus.OverallStatus.ToString();
            settings.LastHealthProblemCount = _overallStatus.ProblemCount;
            settings.LastHealthRecommendationCount = _overallStatus.RecommendationCount;
            settings.LastHealthCriticalCount = _overallStatus.CriticalCount;
            settings.PendingRestart = _overallStatus.PendingRestart;
            settings.LastHealthCheckedAt = _overallStatus.LastCheckedAt;
            _settingsManager.SaveSettings();
        }

        private void ApplyUnknownModuleStatuses()
        {
            foreach (var module in _modules)
            {
                module.Status = new ModuleHealthStatus
                {
                    ModuleId = module.Id,
                    Title = module.Title,
                    Status = HealthLevel.Unknown,
                    LastCheckedAt = _overallStatus.LastCheckedAt
                };
            }

            if (_overallStatus.PendingRestart)
            {
                var systemModule = _modules.FirstOrDefault(item => item.Id == CoreModuleId.SystemParameters);
                SetModuleStatus(
                    systemModule?.Status,
                    HealthLevel.Normal,
                    recommendations: 1,
                    finding: CreatePendingRestartFinding());
            }
        }

        private static SystemDriveCheck CheckSystemDrive()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrWhiteSpace(root))
                    return new SystemDriveCheck(false, null, null);

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                    return new SystemDriveCheck(false, null, null);

                return new SystemDriveCheck(true, drive.AvailableFreeSpace, drive.TotalSize);
            }
            catch
            {
                return new SystemDriveCheck(false, null, null);
            }
        }

        private static PendingRestartCheck CheckPendingRestart()
        {
            var result = new PendingRestartCheck();

            if (RegistryKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                AddDistinct(result.RestartSources, "обслуживание компонентов Windows");

            if (RegistryKeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                AddDistinct(result.RestartSources, "Windows Update");

            var pendingFileOperations = ReadRegistryMultiStringValue(
                Registry.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                "PendingFileRenameOperations");

            foreach (var source in ClassifyPendingFileOperationSources(pendingFileOperations))
                AddDistinct(result.PendingFileSources, source);

            return result;
        }

        private static bool RegistryKeyExists(RegistryKey hive, string path)
        {
            try
            {
                using var key = hive.OpenSubKey(path, writable: false);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> ReadRegistryMultiStringValue(RegistryKey hive, string path, string valueName)
        {
            var values = new List<string>();

            try
            {
                using var key = hive.OpenSubKey(path, writable: false);
                var value = key?.GetValue(valueName);

                if (value is string[] items)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                            values.Add(item);
                    }
                }
                else if (value is string item && !string.IsNullOrWhiteSpace(item))
                {
                    values.Add(item);
                }
            }
            catch
            {
            }

            return values;
        }

        private static IReadOnlyList<string> ClassifyPendingFileOperationSources(IReadOnlyList<string> pendingFileOperations)
        {
            var sources = new List<string>();

            foreach (var operation in pendingFileOperations)
            {
                if (ContainsPathFragment(operation, "Microsoft Office") ||
                    ContainsPathFragment(operation, "ClickToRun") ||
                    ContainsPathFragment(operation, "Office16") ||
                    ContainsPathFragment(operation, "OFFSYM"))
                {
                    AddDistinct(sources, "Microsoft Office");
                }
                else if (ContainsPathFragment(operation, "Yandex") ||
                         ContainsPathFragment(operation, "yabroupdater"))
                {
                    AddDistinct(sources, "Yandex Browser");
                }
                else if (ContainsPathFragment(operation, "GamingServices") ||
                         ContainsPathFragment(operation, "gameplatformservices") ||
                         ContainsPathFragment(operation, "gamingservicesproxy"))
                {
                    AddDistinct(sources, "Gaming Services");
                }
                else if (ContainsPathFragment(operation, "ChromiumTemp") ||
                         ContainsPathFragment(operation, "service_update.exe"))
                {
                    AddDistinct(sources, "Chromium updater");
                }
                else if (ContainsPathFragment(operation, @"\Windows\SystemTemp\") ||
                         ContainsPathFragment(operation, @"\AppData\Local\Temp\") ||
                         ContainsPathFragment(operation, @"\Temp\") ||
                         ContainsPathFragment(operation, ".tmp"))
                {
                    AddDistinct(sources, "временные файлы установщиков");
                }
            }

            if (pendingFileOperations.Count > 0 && sources.Count == 0)
                sources.Add("неизвестные установщики или драйверы");

            return sources;
        }

        private static bool ContainsPathFragment(string value, string fragment)
        {
            return value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (!values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                values.Add(value);
        }

        private static string FormatSourceList(IReadOnlyList<string> sources)
        {
            if (sources == null || sources.Count == 0)
                return string.Empty;

            if (sources.Count <= 6)
                return string.Join(", ", sources);

            return $"{string.Join(", ", sources.Take(6))} и ещё {sources.Count - 6}";
        }

        private static ModuleHealthFinding CreatePendingRestartFinding(PendingRestartCheck restartCheck = null)
        {
            var details = new List<string>();

            if (restartCheck != null)
            {
                if (restartCheck.RestartSources.Count > 0)
                    details.Add($"системные флаги: {FormatSourceList(restartCheck.RestartSources)}");

                if (restartCheck.PendingFileSources.Count > 0)
                    details.Add($"отложенные операции файлов: {FormatSourceList(restartCheck.PendingFileSources)}");

                if (restartCheck.AppSources.Count > 0)
                    details.Add($"TweakWise: {FormatSourceList(restartCheck.AppSources)}");
            }

            string description = details.Count > 0
                ? $"Windows ожидает завершения изменений после перезагрузки. Найдено: {string.Join("; ", details)}."
                : "Windows сообщает, что часть изменений будет завершена только после перезагрузки.";

            return new ModuleHealthFinding
            {
                Level = HealthLevel.Normal,
                Title = "Ожидается перезагрузка",
                Description = description,
                ActionText = "Выполните именно «Перезагрузка». Обычное выключение с быстрым запуском может не закрыть этот флаг."
            };
        }

        private static HealthLevel ResolveOverallStatus(int problemCount, int recommendationCount, int criticalCount)
        {
            if (criticalCount > 0)
                return HealthLevel.Critical;

            if (problemCount > 0)
                return HealthLevel.Warning;

            if (recommendationCount > 0)
                return HealthLevel.Normal;

            return HealthLevel.Good;
        }

        private static void SetModuleStatus(
            ModuleHealthStatus target,
            HealthLevel status,
            int problems = 0,
            int recommendations = 0,
            ModuleHealthFinding finding = null)
        {
            if (target == null)
                return;

            if (IsMoreSevere(status, target.Status))
                target.Status = status;

            target.ProblemCount += problems;
            target.RecommendationCount += recommendations;

            if (finding != null)
            {
                finding.ModuleId = target.ModuleId;
                if (string.IsNullOrWhiteSpace(finding.Id))
                    finding.Id = BuildGeneratedFindingId(target.ModuleId, finding);

                target.Findings.Add(finding);
            }
        }

        private static string BuildGeneratedFindingId(CoreModuleId moduleId, ModuleHealthFinding finding)
        {
            string title = new string((finding.Title ?? string.Empty)
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray())
                .Trim('-');

            while (title.Contains("--", StringComparison.Ordinal))
                title = title.Replace("--", "-", StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(title))
                title = "signal";

            return $"{moduleId}.{title}";
        }

        private static CommandResult RunPowerCfg(params string[] arguments)
        {
            return RunProcess("powercfg.exe", arguments, GetPowerCfgEncoding());
        }

        private static Encoding GetPowerCfgEncoding()
        {
            try
            {
                uint codePage = GetOEMCP();
                if (codePage > 0)
                    return Encoding.GetEncoding((int)codePage);
            }
            catch
            {
            }

            return Console.OutputEncoding ?? Encoding.Default;
        }

        private static CommandResult RunProcess(string fileName, IEnumerable<string> arguments, Encoding outputEncoding = null)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (outputEncoding != null)
                {
                    startInfo.StandardOutputEncoding = outputEncoding;
                    startInfo.StandardErrorEncoding = outputEncoding;
                }

                foreach (string argument in arguments)
                    startInfo.ArgumentList.Add(argument);

                using var process = Process.Start(startInfo);
                if (process == null)
                    return CommandResult.Fail("Процесс не запустился.");

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(6000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return CommandResult.Fail("Команда превысила время ожидания.", output);
                }

                return new CommandResult(process.ExitCode == 0, output, error, process.ExitCode);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }

        private static IReadOnlyList<string> SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string SummarizePowerCfgOutput(string output, int maxLines)
        {
            var lines = SplitLines(output)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(Math.Max(1, maxLines))
                .ToList();

            return string.Join(" · ", lines);
        }

        private static bool IsEmptyPowerCfgDiagnostic(string output)
        {
            var significant = SplitLines(output)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.EndsWith(":", StringComparison.Ordinal))
                .ToList();

            if (significant.Count == 0)
                return true;

            return significant.All(line =>
                line.Equals("None.", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("Нет.", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("Нет", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("Отсутствуют", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsProblemLevel(ModuleHealthFinding finding)
        {
            return finding?.Level == HealthLevel.Attention ||
                   finding?.Level == HealthLevel.Warning ||
                   finding?.Level == HealthLevel.Critical;
        }

        private void PublishProblemNotifications(IReadOnlyList<ModuleHealthStatus> moduleStatuses)
        {
            var problemFindings = moduleStatuses
                .SelectMany(status => status.Findings.Select(finding => new { Status = status, Finding = finding }))
                .Where(item => IsProblemLevel(item.Finding))
                .ToList();

            var currentIds = new HashSet<string>(
                problemFindings.Select(item => item.Finding.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in problemFindings)
            {
                if (_lastProblemFindingIds.Contains(item.Finding.Id))
                    continue;

                AddHealthNotification(item.Status.ModuleId, item.Status.Title, item.Finding);
            }

            _lastProblemFindingIds = currentIds;
        }

        private static void AddHealthNotification(CoreModuleId moduleId, string moduleTitle, ModuleHealthFinding finding)
        {
            if (finding == null || App.SettingsManager?.CurrentSettings.ShowNotifications != true)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                string severityTitle = finding.Level == HealthLevel.Critical
                    ? "Критическая проблема"
                    : "Проблема";

                string title = $"{severityTitle}: {moduleTitle}";
                string message = string.IsNullOrWhiteSpace(finding.Description)
                    ? finding.Title
                    : $"{finding.Title}. {finding.Description}";

                if (App.NotificationManager.Notifications.Any(notification =>
                        string.Equals(notification.Title, title, StringComparison.Ordinal) &&
                        string.Equals(notification.Message, message, StringComparison.Ordinal)))
                {
                    return;
                }

                string actionTarget = string.IsNullOrWhiteSpace(finding.Id)
                    ? moduleId.ToString()
                    : $"{moduleId}|{finding.Id}";

                App.NotificationManager.AddNotification(
                    title,
                    message,
                    NotificationManager.ActionOpenCoreModule,
                    actionTarget);
            }));
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.#} {units[unitIndex]}";
        }

        private static bool IsMoreSevere(HealthLevel candidate, HealthLevel current)
        {
            return GetSeverity(candidate) >= GetSeverity(current);
        }

        private static int GetSeverity(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Unknown => 0,
                HealthLevel.Checking => 1,
                HealthLevel.Good => 2,
                HealthLevel.Normal => 3,
                HealthLevel.Attention => 4,
                HealthLevel.Warning => 5,
                HealthLevel.Critical => 6,
                _ => 0
            };
        }

        private static HealthLevel ParseHealthLevel(string value)
        {
            return Enum.TryParse(value, ignoreCase: true, out HealthLevel level)
                ? level
                : HealthLevel.Unknown;
        }

        private static ComputerHealthStatus CloneStatus(ComputerHealthStatus source)
        {
            return new ComputerHealthStatus
            {
                OverallStatus = source.OverallStatus,
                ProblemCount = source.ProblemCount,
                RecommendationCount = source.RecommendationCount,
                CriticalCount = source.CriticalCount,
                PendingRestart = source.PendingRestart,
                LastCheckedAt = source.LastCheckedAt
            };
        }

        private static CoreModuleDefinition CloneModule(CoreModuleDefinition source)
        {
            return new CoreModuleDefinition
            {
                Id = source.Id,
                Title = source.Title,
                Description = source.Description,
                ShortHint = source.ShortHint,
                Sections = source.Sections.ToList(),
                Status = new ModuleHealthStatus
                {
                    ModuleId = source.Status.ModuleId,
                    Title = source.Status.Title,
                    Status = source.Status.Status,
                    ProblemCount = source.Status.ProblemCount,
                    RecommendationCount = source.Status.RecommendationCount,
                    CriticalCount = source.Status.CriticalCount,
                    Findings = source.Status.Findings
                        .Select(finding => new ModuleHealthFinding
                        {
                            Id = finding.Id,
                            ModuleId = finding.ModuleId,
                            Level = finding.Level,
                            Title = finding.Title,
                            Description = finding.Description,
                            ActionText = finding.ActionText
                        })
                        .ToList(),
                    LastCheckedAt = source.Status.LastCheckedAt
                }
            };
        }

        private static List<CoreModuleDefinition> BuildModuleDefinitions()
        {
            return new List<CoreModuleDefinition>
            {
                new()
                {
                    Id = CoreModuleId.WindowsSetup,
                    Title = "Рабочая среда",
                    ShortHint = "Экран, Проводник, Пуск, панель задач",
                    Description = "Настройка пользовательской оболочки Windows: экран, проводник, меню Пуск, панель задач, поиск и уведомления.",
                    Sections = new List<string>
                    {
                        "Экран и визуальные эффекты",
                        "Проводник",
                        "Меню Пуск",
                        "Панель задач",
                        "Контекстное меню",
                        "Поиск",
                        "Уведомления"
                    }
                },
                new()
                {
                    Id = CoreModuleId.SystemParameters,
                    Title = "Системная конфигурация",
                    ShortHint = "Службы, обновления, приватность, питание",
                    Description = "Параметры ОС, которые влияют на стабильность, фоновые процессы, обновления, приватность и схемы питания.",
                    Sections = new List<string>
                    {
                        "Обновления и перезапуск",
                        "Службы",
                        "Автозагрузка",
                        "Фоновые процессы",
                        "Приватность и телеметрия",
                        "Питание",
                        "Восстановление"
                    }
                },
                new()
                {
                    Id = CoreModuleId.Resources,
                    Title = "Производительность и охлаждение",
                    ShortHint = "CPU, GPU, RAM, питание, охлаждение",
                    Description = "Узлы, которые напрямую влияют на скорость и тепловой режим: процессор, видеокарта, оперативная память, питание, лимиты и охлаждение.",
                    Sections = new List<string>
                    {
                        "Процессор",
                        "Видеокарта",
                        "Оперативная память",
                        "Питание и лимиты",
                        "Охлаждение и датчики",
                        "Безопасный тюнинг"
                    }
                },
                new()
                {
                    Id = CoreModuleId.Maintenance,
                    Title = "Накопители и память",
                    ShortHint = "Диски, место, кэш, файл подкачки",
                    Description = "Системный диск, дополнительные накопители, временные файлы, кэш, файл подкачки и безопасная очистка.",
                    Sections = new List<string>
                    {
                        "Системный диск",
                        "Дополнительные диски",
                        "Здоровье SSD/HDD",
                        "Временные файлы и кэш",
                        "Файл подкачки",
                        "Очистка",
                        "История обслуживания"
                    }
                },
                new()
                {
                    Id = CoreModuleId.Devices,
                    Title = "Устройства и драйверы",
                    ShortHint = "Драйверы, периферия, неизвестные устройства",
                    Description = "Оборудование и драйверный слой: проверка устройств, резервная копия драйверов, удаление драйверов и ручная установка INF.",
                    Sections = new List<string>
                    {
                        "Диспетчер устройств",
                        "Неизвестные устройства",
                        "Драйверы",
                        "Резервная копия драйверов",
                        "Удаление драйвера",
                        "Ручная установка INF",
                        "USB, Bluetooth и периферия"
                    }
                },
                new()
                {
                    Id = CoreModuleId.Network,
                    Title = "Сеть и подключение",
                    ShortHint = "Адаптеры, DNS, ping, диагностика",
                    Description = "Сетевой контур компьютера: адаптеры, DNS, доступ в интернет, задержка и быстрые исправления подключения.",
                    Sections = new List<string>
                    {
                        "Сетевые адаптеры",
                        "DNS",
                        "Проверка соединения",
                        "Ping и задержка",
                        "Сброс сети",
                        "Диагностика маршрута"
                    }
                }
            };
        }

        private void OnHealthStatusChanged()
        {
            HealthStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class HealthFindingAccumulator
        {
            public int ProblemCount { get; set; }
            public int RecommendationCount { get; set; }
            public int CriticalCount { get; set; }
            public bool PendingRestart { get; set; }
        }

        private sealed class PendingRestartCheck
        {
            public List<string> RestartSources { get; } = new List<string>();
            public List<string> PendingFileSources { get; } = new List<string>();
            public List<string> AppSources { get; } = new List<string>();
            public bool IsPending => RestartSources.Count > 0 || PendingFileSources.Count > 0 || AppSources.Count > 0;
        }

        private readonly struct HealthCheckSnapshot
        {
            public HealthCheckSnapshot(ComputerHealthStatus overallStatus, IReadOnlyList<ModuleHealthStatus> moduleStatuses)
            {
                OverallStatus = overallStatus;
                ModuleStatuses = moduleStatuses;
            }

            public ComputerHealthStatus OverallStatus { get; }
            public IReadOnlyList<ModuleHealthStatus> ModuleStatuses { get; }
        }

        private readonly struct SystemDriveCheck
        {
            public SystemDriveCheck(bool systemDriveReady, long? freeBytes, long? totalBytes)
            {
                SystemDriveReady = systemDriveReady;
                FreeBytes = freeBytes;
                TotalBytes = totalBytes;
            }

            public bool SystemDriveReady { get; }
            public long? FreeBytes { get; }
            public long? TotalBytes { get; }
        }

        private readonly struct CommandResult
        {
            public CommandResult(bool success, string output, string error, int exitCode)
            {
                Success = success;
                Output = output ?? string.Empty;
                Error = error ?? string.Empty;
                ExitCode = exitCode;
            }

            public bool Success { get; }
            public string Output { get; }
            public string Error { get; }
            public int ExitCode { get; }

            public static CommandResult Fail(string error, string output = "")
            {
                return new CommandResult(false, output, error, -1);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatusEx
        {
            public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
