using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
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

        public bool HasFreshStatus(IEnumerable<CoreModuleId> modulesToScan, TimeSpan maxAge)
        {
            return HasFreshStatus(NormalizeRequestedModules(modulesToScan), maxAge);
        }

        public async Task EnsureStatusAsync(IEnumerable<CoreModuleId> modulesToScan, TimeSpan maxAge)
        {
            var requestedModules = NormalizeRequestedModules(modulesToScan);
            if (requestedModules != null && requestedModules.Count == 0)
                return;

            if (HasFreshStatus(requestedModules, maxAge))
                return;

            await _refreshGate.WaitAsync();
            try
            {
                if (HasFreshStatus(requestedModules, maxAge))
                    return;

                await RefreshStatusCoreAsync(requestedModules);
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public Task RefreshStatusAsync()
        {
            return RefreshStatusAsync(null);
        }

        public async Task RefreshStatusAsync(IEnumerable<CoreModuleId> modulesToScan)
        {
            var requestedModules = NormalizeRequestedModules(modulesToScan);
            if (requestedModules != null && requestedModules.Count == 0)
                return;

            await _refreshGate.WaitAsync();
            try
            {
                await RefreshStatusCoreAsync(requestedModules);
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task RefreshStatusCoreAsync(ISet<CoreModuleId> requestedModules)
        {
            SetCheckingState(requestedModules);

            var result = await Task.Run(() => RunSafeHealthChecks(requestedModules));
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

        private bool HasFreshStatus(ISet<CoreModuleId> modulesToScan, TimeSpan maxAge)
        {
            DateTime now = DateTime.Now;
            foreach (var module in _modules)
            {
                if (!ShouldScanModule(modulesToScan, module.Id) || IsUnavailableModule(module.Id))
                    continue;

                var status = module.Status;
                if (status == null ||
                    status.Status == HealthLevel.Unknown ||
                    status.Status == HealthLevel.Checking ||
                    !status.LastCheckedAt.HasValue)
                {
                    return false;
                }

                if (maxAge > TimeSpan.Zero && now - status.LastCheckedAt.Value > maxAge)
                    return false;
            }

            return true;
        }

        private void SetCheckingState(ISet<CoreModuleId> modulesToScan)
        {
            var now = DateTime.Now;
            _overallStatus = new ComputerHealthStatus
            {
                OverallStatus = HealthLevel.Checking,
                LastCheckedAt = now
            };

            foreach (var module in _modules)
            {
                if (!ShouldScanModule(modulesToScan, module.Id) || IsUnavailableModule(module.Id))
                    continue;

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

        private HealthCheckSnapshot RunSafeHealthChecks(ISet<CoreModuleId> modulesToScan)
        {
            var now = DateTime.Now;
            var moduleStatuses = _modules.ToDictionary(
                module => module.Id,
                module => ShouldScanModule(modulesToScan, module.Id)
                    ? new ModuleHealthStatus
                    {
                        ModuleId = module.Id,
                        Title = module.Title,
                        Status = HealthLevel.Unknown,
                        LastCheckedAt = now
                    }
                    : CloneModuleStatus(module.Status, module));

            var findings = new HealthFindingAccumulator();

            if (ShouldScanModule(modulesToScan, CoreModuleId.WindowsSetup))
                CheckWindowsSetupState(moduleStatuses, findings);
            if (ShouldScanModule(modulesToScan, CoreModuleId.Resources))
                CheckPerformanceState(moduleStatuses, findings);
            if (ShouldScanModule(modulesToScan, CoreModuleId.Devices))
                CheckDevicesDriversState(moduleStatuses, findings);

            MarkUntouchedModulesAsGood(moduleStatuses, modulesToScan);
            MarkUnavailableModulesAsNotChecked(moduleStatuses);

            var overall = ApplySuppressionsAndRecalculate(moduleStatuses, pendingRestart: false, now, modulesToScan);

            return new HealthCheckSnapshot(overall, moduleStatuses.Values.ToList());
        }

        private static ISet<CoreModuleId> NormalizeRequestedModules(IEnumerable<CoreModuleId> modulesToScan)
        {
            if (modulesToScan == null)
                return null;

            var normalized = new HashSet<CoreModuleId>();
            foreach (var moduleId in modulesToScan)
            {
                if (moduleId == CoreModuleId.PowerThermal)
                    normalized.Add(CoreModuleId.Resources);
                else
                    normalized.Add(moduleId);
            }

            return normalized.Count == 0 ? new HashSet<CoreModuleId>() : normalized;
        }

        private static bool ShouldScanModule(ISet<CoreModuleId> modulesToScan, CoreModuleId moduleId)
        {
            return modulesToScan == null || modulesToScan.Contains(moduleId);
        }

        private static ModuleHealthStatus CloneModuleStatus(ModuleHealthStatus source, CoreModuleDefinition module)
        {
            if (source == null)
            {
                return new ModuleHealthStatus
                {
                    ModuleId = module.Id,
                    Title = module.Title,
                    Status = HealthLevel.Unknown
                };
            }

            return new ModuleHealthStatus
            {
                ModuleId = module.Id,
                Title = string.IsNullOrWhiteSpace(source.Title) ? module.Title : source.Title,
                Status = source.Status,
                ProblemCount = source.ProblemCount,
                RecommendationCount = source.RecommendationCount,
                CriticalCount = source.CriticalCount,
                Findings = source.Findings?
                    .Where(finding => finding != null)
                    .Select(finding => new ModuleHealthFinding
                    {
                        Id = finding.Id,
                        ModuleId = finding.ModuleId,
                        Level = finding.Level,
                        Title = finding.Title,
                        Description = finding.Description,
                        ActionText = finding.ActionText
                    })
                    .ToList() ?? new List<ModuleHealthFinding>(),
                LastCheckedAt = source.LastCheckedAt
            };
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

        private static void CheckDevicesDriversState(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            if (!moduleStatuses.TryGetValue(CoreModuleId.Devices, out var target))
                return;

            try
            {
                var snapshot = new DeviceDriverDiagnosticsService()
                    .GetOrScanAsync(CancellationToken.None, TimeSpan.FromSeconds(30))
                    .GetAwaiter()
                    .GetResult() ?? new DeviceDriverDiagnosticsSnapshot();

                var visibleFindings = (snapshot.Findings ?? new List<DeviceDriverFinding>())
                    .Where(item => item != null && item.Level != HealthLevel.Good)
                    .OrderByDescending(item => GetSeverity(item.Level))
                    .ThenBy(item => item.Title ?? string.Empty)
                    .Take(6)
                    .ToList();

                if (visibleFindings.Count == 0)
                {
                    SetModuleStatus(target, HealthLevel.Good);
                    return;
                }

                foreach (var deviceFinding in visibleFindings)
                {
                    bool isProblem = deviceFinding.Level == HealthLevel.Attention || deviceFinding.Level == HealthLevel.Warning || deviceFinding.Level == HealthLevel.Critical;
                    if (isProblem)
                        findings.ProblemCount++;
                    else
                        findings.RecommendationCount++;

                    SetModuleStatus(
                        target,
                        deviceFinding.Level,
                        problems: isProblem ? 1 : 0,
                        recommendations: isProblem ? 0 : 1,
                        finding: new ModuleHealthFinding
                        {
                            Id = string.IsNullOrWhiteSpace(deviceFinding.Id) ? $"devices.{deviceFinding.Title}" : deviceFinding.Id,
                            Level = deviceFinding.Level,
                            Title = deviceFinding.Title,
                            Description = deviceFinding.Description,
                            ActionText = deviceFinding.ActionText
                        });
                }
            }
            catch
            {
                findings.RecommendationCount++;
                SetModuleStatus(
                    target,
                    HealthLevel.Normal,
                    recommendations: 1,
                    finding: new ModuleHealthFinding
                    {
                        Id = "devices.diagnostics-unavailable",
                        Level = HealthLevel.Normal,
                        Title = "Диагностика устройств недоступна",
                        Description = "Проверка устройств и драйверов не смогла получить данные WMI/CIM в фоновом режиме.",
                        ActionText = "Откройте раздел устройств и драйверов и повторите диагностику вручную."
                    });
            }
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

        private static void CheckWindowsSetupState(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            HealthFindingAccumulator findings)
        {
            var target = moduleStatuses[CoreModuleId.WindowsSetup];
            var environmentFindings = new List<ModuleHealthFinding>();

            int? transparency = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency");
            if (transparency != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.display.transparency",
                    Level = HealthLevel.Normal,
                    Title = "Прозрачность интерфейса включена",
                    Description = "Windows использует прозрачные поверхности. Это не ошибка, но на слабых системах и при долгой работе может добавлять визуальный шум.",
                    ActionText = "В узле «Экран» можно отключить прозрачность интерфейса."
                });
            }

            string minAnimate = ReadRegistryStringValue(
                Registry.CurrentUser,
                @"Control Panel\Desktop\WindowMetrics",
                "MinAnimate");
            if (!string.Equals(minAnimate, "0", StringComparison.OrdinalIgnoreCase))
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.display.window-animation",
                    Level = HealthLevel.Normal,
                    Title = "Анимация окон включена",
                    Description = "Сворачивание и разворачивание окон анимируется. Это штатно, но не всем подходит для быстрой рабочей среды.",
                    ActionText = "В узле «Экран» можно отключить анимацию окон."
                });
            }

            int? titleAccent = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\DWM",
                "ColorPrevalence");
            if (titleAccent.HasValue && titleAccent != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.display.title-accent",
                    Level = HealthLevel.Normal,
                    Title = "Акцент на заголовках окон включён",
                    Description = "Windows окрашивает заголовки окон акцентным цветом. Для спокойной рабочей среды этот эффект можно отключить.",
                    ActionText = "В узле «Экран» можно вернуть нейтральные заголовки окон."
                });
            }

            int? hideFileExt = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "HideFileExt");
            if (hideFileExt != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.explorer.hide-file-extensions",
                    Level = HealthLevel.Normal,
                    Title = "Расширения файлов скрыты",
                    Description = "Проводник скрывает расширения известных типов файлов. Из-за этого сложнее отличить документ от исполняемого файла.",
                    ActionText = "В узле «Проводник» включите отображение расширений файлов."
                });
            }

            int? syncProviderNotifications = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowSyncProviderNotifications");
            if (syncProviderNotifications != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.explorer.sync-provider-notifications",
                    Level = HealthLevel.Normal,
                    Title = "Предложения Проводника включены",
                    Description = "Проводник может показывать системные предложения и информационные блоки Microsoft. Это не ошибка, но часть пользователей отключает их для более спокойной рабочей среды.",
                    ActionText = "В узле «Проводник» проверьте, нужны ли такие предложения в ежедневной работе."
                });
            }

            int? separateProcess = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "SeparateProcess");
            if (separateProcess != 1)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.explorer.separate-process",
                    Level = HealthLevel.Normal,
                    Title = "Проводник работает в общем процессе",
                    Description = "Окна Проводника могут использовать общий процесс оболочки. Отдельный процесс повышает устойчивость при сбоях отдельных окон.",
                    ActionText = "В узле «Проводник» можно включить отдельный процесс для окон Проводника."
                });
            }

            int? hiddenFilesMode = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Hidden");
            if (hiddenFilesMode.HasValue && hiddenFilesMode != 2)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.explorer.hidden-files",
                    Level = HealthLevel.Normal,
                    Title = "Скрытые файлы отображаются",
                    Description = "Проводник показывает скрытые элементы. Это удобно для администрирования, но для обычной рабочей среды может создавать лишний визуальный шум.",
                    ActionText = "В узле «Проводник» можно вернуть стандартное скрытие системных элементов."
                });
            }

            int? startRecommendations = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_IrisRecommendations");
            if (startRecommendations != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.start.recommendations",
                    Level = HealthLevel.Normal,
                    Title = "Рекомендации в меню Пуск включены",
                    Description = "Windows может показывать в меню Пуск рекомендации и недавние элементы. Это не ошибка, но часть пользователей отключает этот блок для более чистого меню.",
                    ActionText = "В узле «Пуск» проверьте, нужен ли этот блок в рабочей среде."
                });
            }

            int? startTrackDocs = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_TrackDocs");
            if (startTrackDocs != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.start.track-docs-enabled",
                    Level = HealthLevel.Normal,
                    Title = "Недавние элементы в Пуске включены",
                    Description = "Windows может показывать недавно открытые файлы в меню Пуск, списках переходов и Проводнике. Это удобно, но не всегда подходит для аккуратной или приватной рабочей среды.",
                    ActionText = "В узле «Пуск» проверьте отображение недавних элементов."
                });
            }

            int? startLayout = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_Layout");
            if (startLayout != 1)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.start.more-pins",
                    Level = HealthLevel.Normal,
                    Title = "Пуск использует стандартную компоновку",
                    Description = "В меню Пуск может оставаться больше места под рекомендательный блок. Для рабочей среды чаще удобнее компактная схема с большим числом закреплений.",
                    ActionText = "В узле «Пуск» можно включить компоновку с большим числом закреплений."
                });
            }

            int? taskbarWidgets = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarDa");
            if (taskbarWidgets > 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.taskbar.widgets-enabled",
                    Level = HealthLevel.Normal,
                    Title = "Виджеты включены на панели задач",
                    Description = "Панель задач содержит системный блок виджетов. Если он не используется, его можно убрать, чтобы освободить место и уменьшить визуальный шум.",
                    ActionText = "В узле «Панель задач» проверьте системные кнопки и закрепления."
                });
            }

            int? taskbarChat = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarMn");
            if (taskbarChat > 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.taskbar.chat-enabled",
                    Level = HealthLevel.Normal,
                    Title = "Кнопка чата включена на панели задач",
                    Description = "Windows показывает кнопку чата или Teams на панели задач. Если она не нужна, её можно скрыть.",
                    ActionText = "В узле «Панель задач» проверьте лишние системные элементы."
                });
            }

            int? taskbarSearchMode = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "SearchboxTaskbarMode");
            if (taskbarSearchMode.HasValue && taskbarSearchMode != 1)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.taskbar.search-mode",
                    Level = HealthLevel.Normal,
                    Title = "Поиск занимает много места на панели задач",
                    Description = "Панель задач может показывать широкую строку поиска вместо компактной кнопки. Это уменьшает полезное место для закреплённых приложений.",
                    ActionText = "В узле «Панель задач» можно включить компактный вид поиска."
                });
            }

            int? taskbarAlignment = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarAl");
            if (taskbarAlignment != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.taskbar.left-align",
                    Level = HealthLevel.Normal,
                    Title = "Значки панели задач выровнены по центру",
                    Description = "Центральное выравнивание выглядит современно, но для некоторых рабочих сценариев левое расположение быстрее и предсказуемее.",
                    ActionText = "В узле «Панель задач» можно переключить значки влево."
                });
            }

            int? bingSearch = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "BingSearchEnabled");
            int? cortanaConsent = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "CortanaConsent");
            int? disableSearchBoxSuggestions = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer",
                "DisableSearchBoxSuggestions");
            if (disableSearchBoxSuggestions != 1)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.search.web-policy",
                    Level = HealthLevel.Normal,
                    Title = "Веб-подсказки поиска разрешены политикой",
                    Description = "Windows Search может смешивать локальные результаты с веб-подсказками. Это удобно не всем и иногда делает поиск менее предсказуемым.",
                    ActionText = "В узле «Поиск» можно отключить веб-подсказки."
                });
            }

            if (bingSearch != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.search.bing-search",
                    Level = HealthLevel.Normal,
                    Title = "Bing включён в поиске Windows",
                    Description = "Поиск может добавлять онлайн-результаты Bing к локальным файлам и приложениям.",
                    ActionText = "В узле «Поиск» можно оставить только локальные результаты."
                });
            }

            if (cortanaConsent > 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.search.cortana-consent",
                    Level = HealthLevel.Normal,
                    Title = "Онлайн-компонент поиска разрешён",
                    Description = "Пользовательское согласие разрешает онлайн-компоненты системного поиска.",
                    ActionText = "В узле «Поиск» можно сбросить согласие для онлайн-компонента."
                });
            }

            int? searchLocation = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "AllowSearchToUseLocation");
            if (searchLocation != 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.search.location",
                    Level = HealthLevel.Normal,
                    Title = "Поиск может использовать геопозицию",
                    Description = "Windows Search может учитывать геопозицию для подсказок. Это не ошибка, но для приватной рабочей среды параметр обычно отключают.",
                    ActionText = "В узле «Поиск» можно отключить использование геопозиции."
                });
            }

            string snapActive = ReadRegistryStringValue(
                Registry.CurrentUser,
                @"Control Panel\Desktop",
                "WindowArrangementActive");
            if (string.Equals(snapActive, "0", StringComparison.OrdinalIgnoreCase))
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.windows.snap",
                    Level = HealthLevel.Warning,
                    Title = "Привязка окон отключена",
                    Description = "Windows не будет привязывать окна к краям экрана. Это может мешать удобной работе с несколькими окнами.",
                    ActionText = "В узле «Окна» можно включить привязку окон."
                });
            }

            int? snapFlyout = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "EnableSnapAssistFlyout");
            if (snapFlyout.HasValue && snapFlyout != 1)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.windows.snap-flyout",
                    Level = HealthLevel.Normal,
                    Title = "Подсказки привязки окон отключены",
                    Description = "Панель Snap Layouts не появляется при работе с окнами. Это может замедлять раскладку нескольких приложений на экране.",
                    ActionText = "В узле «Окна» можно включить подсказки привязки."
                });
            }

            int? altTabEdge = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "MultiTaskingAltTabFilter");
            if (altTabEdge != 3)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.windows.alt-tab-edge",
                    Level = HealthLevel.Normal,
                    Title = "Alt+Tab может показывать вкладки браузера",
                    Description = "В переключателе задач могут появляться вкладки Microsoft Edge. При большом числе вкладок список окон становится менее предсказуемым.",
                    ActionText = "В узле «Окна» можно оставить в Alt+Tab только окна."
                });
            }

            int? toastEnabled = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
                "ToastEnabled");
            if (toastEnabled == 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.notifications.toast-disabled",
                    Level = HealthLevel.Warning,
                    Title = "Системные уведомления отключены",
                    Description = "Windows сообщает, что toast-уведомления отключены. Из-за этого часть важных событий может не отображаться пользователю.",
                    ActionText = "В узле «Уведомления» проверьте, должны ли системные события показываться."
                });
            }

            int? globalToasts = ReadRegistryDwordValue(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                "NOC_GLOBAL_SETTING_TOASTS_ENABLED");
            if (globalToasts == 0)
            {
                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = "workenv.notifications.global-toasts",
                    Level = HealthLevel.Warning,
                    Title = "Глобальные уведомления отключены",
                    Description = "Общий переключатель уведомлений Windows выключен. Из-за этого часть системных и пользовательских событий может не появляться.",
                    ActionText = "В узле «Уведомления» можно вернуть общий вывод уведомлений."
                });
            }

            void AddDwordEnvironmentFinding(
                RegistryKey hive,
                string path,
                string valueName,
                int expectedValue,
                string id,
                HealthLevel level,
                string title,
                string description,
                string actionText)
            {
                int? value = ReadRegistryDwordValue(hive, path, valueName);
                if (value.HasValue && value.Value == expectedValue)
                    return;

                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = id,
                    Level = level,
                    Title = title,
                    Description = description,
                    ActionText = actionText
                });
            }

            void AddStringEnvironmentFinding(
                RegistryKey hive,
                string path,
                string valueName,
                string expectedValue,
                string id,
                HealthLevel level,
                string title,
                string description,
                string actionText)
            {
                string value = ReadRegistryStringValue(hive, path, valueName);
                if (string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase))
                    return;

                environmentFindings.Add(new ModuleHealthFinding
                {
                    Id = id,
                    Level = level,
                    Title = title,
                    Description = description,
                    ActionText = actionText
                });
            }

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0,
                "workenv.display.apps-dark-mode",
                HealthLevel.Normal,
                "Светлая тема приложений включена",
                "Приложения Windows используют светлую тему. Для единой тёмной рабочей среды параметр можно переключить напрямую из TweakWise.",
                "В узле «Экран» можно включить тёмную тему приложений.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme",
                0,
                "workenv.display.system-dark-mode",
                HealthLevel.Normal,
                "Системная тема светлая",
                "Оболочка Windows использует светлую тему. Если нужна единая тёмная схема, параметр можно изменить без перехода в настройки Windows.",
                "В узле «Экран» можно включить тёмную системную тему.");

            AddStringEnvironmentFinding(
                Registry.CurrentUser,
                @"Control Panel\Desktop",
                "MenuShowDelay",
                "120",
                "workenv.display.menu-delay",
                HealthLevel.Normal,
                "Меню открываются с обычной задержкой",
                "Системная задержка открытия меню может быть выше оптимальной для быстрой рабочей среды.",
                "В узле «Экран» можно уменьшить задержку открытия меню.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "LaunchTo",
                1,
                "workenv.explorer.launch-to-this-pc",
                HealthLevel.Normal,
                "Проводник открывает Быстрый доступ",
                "Проводник может стартовать в Быстром доступе вместо раздела Этот компьютер. Для рабочего сценария часто удобнее сразу видеть диски.",
                "В узле «Проводник» можно переключить стартовую страницу.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "NavPaneExpandToCurrentFolder",
                1,
                "workenv.explorer.expand-current-folder",
                HealthLevel.Normal,
                "Текущая папка не раскрывается в навигации",
                "Область навигации Проводника не синхронизируется с текущим путём. Это может замедлять работу с вложенными папками.",
                "В узле «Проводник» можно включить раскрытие текущей папки.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "UseCompactMode",
                1,
                "workenv.explorer.compact-mode",
                HealthLevel.Normal,
                "Проводник использует крупные отступы",
                "Файлы и папки отображаются менее компактно, из-за чего на экран помещается меньше элементов.",
                "В узле «Проводник» можно включить компактный режим.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_TrackProgs",
                0,
                "workenv.start.track-programs",
                HealthLevel.Normal,
                "Пуск учитывает историю запуска программ",
                "Windows может использовать историю запуска приложений для персонализации Пуска. Это не всегда подходит для приватной рабочей среды.",
                "В узле «Пуск» можно отключить историю запуска программ.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "HideRecentlyAddedApps",
                1,
                "workenv.start.recent-apps",
                HealthLevel.Normal,
                "Недавно добавленные приложения показываются в Пуске",
                "Меню Пуск может показывать отдельный блок новых приложений. Для чистого рабочего меню его можно скрыть.",
                "В узле «Пуск» можно скрыть недавно добавленные приложения.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "Start_AccountNotifications",
                0,
                "workenv.start.account-notifications",
                HealthLevel.Normal,
                "Уведомления аккаунта в Пуске включены",
                "Меню Пуск может показывать подсказки и уведомления аккаунта Microsoft. Это добавляет лишние сигналы в рабочую среду.",
                "В узле «Пуск» можно отключить уведомления аккаунта.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarBadges",
                0,
                "workenv.taskbar.badges",
                HealthLevel.Normal,
                "Бейджи приложений на панели задач включены",
                "Приложения могут показывать счётчики на панели задач. Для спокойной рабочей среды бейджи можно отключить.",
                "В узле «Панель задач» можно скрыть бейджи приложений.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowTaskViewButton",
                0,
                "workenv.taskbar.task-view",
                HealthLevel.Normal,
                "Кнопка представления задач включена",
                "Отдельная кнопка представления задач занимает место на панели, при этом Win+Tab остаётся доступным.",
                "В узле «Панель задач» можно скрыть кнопку представления задач.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\People",
                "PeopleBand",
                0,
                "workenv.taskbar.people",
                HealthLevel.Normal,
                "Блок Люди включён на панели задач",
                "Устаревший блок Люди может занимать место или оставаться в параметрах оболочки.",
                "В узле «Панель задач» можно отключить блок Люди.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "ConnectedSearchUseWeb",
                0,
                "workenv.search.connected-web",
                HealthLevel.Normal,
                "Подключённый веб-поиск разрешён",
                "Windows Search может обращаться к веб-источникам при локальном поиске. Это делает результаты менее предсказуемыми.",
                "В узле «Поиск» можно отключить подключённый веб-поиск.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "ConnectedSearchUseWebOverMeteredConnections",
                0,
                "workenv.search.web-metered",
                HealthLevel.Normal,
                "Веб-поиск разрешён через лимитное подключение",
                "Системный поиск может выполнять веб-запросы даже при лимитном подключении.",
                "В узле «Поиск» можно запретить веб-запросы через лимитные подключения.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "IsDeviceSearchHistoryEnabled",
                0,
                "workenv.search.device-history",
                HealthLevel.Normal,
                "История поиска на устройстве включена",
                "Windows может хранить локальную историю поиска. Для приватной рабочей среды её можно отключить.",
                "В узле «Поиск» можно отключить историю поиска на устройстве.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "IsMSACloudSearchEnabled",
                0,
                "workenv.search.msa-cloud",
                HealthLevel.Normal,
                "Облачный поиск Microsoft включён",
                "Поиск может обращаться к данным личного Microsoft-аккаунта. Это не всегда нужно в рабочей среде.",
                "В узле «Поиск» можно отключить облачный поиск Microsoft.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search",
                "IsAADCloudSearchEnabled",
                0,
                "workenv.search.aad-cloud",
                HealthLevel.Normal,
                "Облачный поиск организации включён",
                "Поиск может обращаться к рабочей или учебной учётной записи. В личном сценарии это может быть лишним.",
                "В узле «Поиск» можно отключить облачный поиск организации.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings",
                "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK",
                0,
                "workenv.notifications.lock-screen",
                HealthLevel.Normal,
                "Уведомления разрешены на экране блокировки",
                "Обычные уведомления могут быть видны до входа в систему. Для приватной среды их можно скрыть.",
                "В узле «Уведомления» можно скрыть уведомления на экране блокировки.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer",
                "DisableNotificationCenter",
                0,
                "workenv.notifications.center-policy",
                HealthLevel.Warning,
                "Центр уведомлений отключён политикой",
                "Пользовательская политика может отключить центр уведомлений. Из-за этого часть системных событий сложнее увидеть.",
                "В узле «Уведомления» можно вернуть доступ к центру уведомлений.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "SnapAssist",
                1,
                "workenv.windows.snap-assist",
                HealthLevel.Normal,
                "Snap Assist отключён",
                "После привязки окна Windows не предлагает выбрать соседнее окно для заполнения свободной области.",
                "В узле «Окна» можно включить Snap Assist.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "SnapFill",
                1,
                "workenv.windows.snap-fill",
                HealthLevel.Normal,
                "Автозаполнение привязки отключено",
                "Windows может не предлагать удобное заполнение свободного пространства при работе с привязанными окнами.",
                "В узле «Окна» можно включить автозаполнение привязки.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "JointResize",
                1,
                "workenv.windows.joint-resize",
                HealthLevel.Normal,
                "Совместное изменение размеров окон отключено",
                "Соседние привязанные окна могут не изменять размер вместе, что делает раскладку менее удобной.",
                "В узле «Окна» можно включить совместное изменение размеров.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "EnableSnapBar",
                1,
                "workenv.windows.snap-bar",
                HealthLevel.Normal,
                "Верхняя панель привязки отключена",
                "Snap Bar сверху экрана может быть недоступен, если параметр выключен в оболочке Windows.",
                "В узле «Окна» можно включить верхнюю панель привязки.");


            AddStringEnvironmentFinding(
                Registry.CurrentUser,
                @"Control Panel\Desktop",
                "FontSmoothing",
                "2",
                "workenv.display.font-smoothing",
                HealthLevel.Normal,
                "Сглаживание шрифтов отключено",
                "Текст в классических окнах и элементах оболочки может выглядеть грубее. Для рабочей среды лучше оставить сглаживание включённым.",
                "В узле «Экран» можно включить сглаживание шрифтов.");

            AddStringEnvironmentFinding(
                Registry.CurrentUser,
                @"Control Panel\Desktop",
                "DragFullWindows",
                "1",
                "workenv.display.drag-full-windows",
                HealthLevel.Normal,
                "Содержимое окна скрывается при перетаскивании",
                "При перемещении окна Windows может показывать только контур. Это мешает точной раскладке рабочего пространства.",
                "В узле «Экран» можно включить показ содержимого при перетаскивании.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowStatusBar",
                1,
                "workenv.explorer.show-status-bar",
                HealthLevel.Normal,
                "Строка состояния Проводника скрыта",
                "Без строки состояния сложнее быстро видеть количество элементов и сведения о выделении.",
                "В узле «Проводник» можно включить строку состояния.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowInfoTip",
                1,
                "workenv.explorer.info-tips",
                HealthLevel.Normal,
                "Подсказки файлов отключены",
                "Проводник не показывает быстрые сведения о файлах и папках при наведении.",
                "В узле «Проводник» можно включить информационные подсказки.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "AutoCheckSelect",
                0,
                "workenv.explorer.checkboxes",
                HealthLevel.Normal,
                "Флажки выбора элементов включены",
                "Постоянные флажки могут добавлять лишний визуальный шум в Проводнике.",
                "В узле «Проводник» можно отключить флажки выбора элементов.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer",
                "NoNewAppAlert",
                1,
                "workenv.start.app-suggestions",
                HealthLevel.Normal,
                "Предложения приложений в Пуске разрешены",
                "Меню Пуск может показывать дополнительные предложения приложений. Для чистой рабочей среды их можно отключить.",
                "В узле «Пуск» можно отключить предложения приложений.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Policies\Microsoft\Windows\CloudContent",
                "DisableWindowsConsumerFeatures",
                1,
                "workenv.start.disable-spotlight",
                HealthLevel.Normal,
                "Потребительские подсказки Windows разрешены",
                "Windows может показывать советы и потребительские предложения в оболочке.",
                "В узле «Пуск» можно отключить потребительские подсказки Windows.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarSmallIcons",
                1,
                "workenv.taskbar.small-icons",
                HealthLevel.Normal,
                "Панель задач использует обычный размер значков",
                "Компактный размер может освободить место на панели задач, если параметр поддерживается системой.",
                "В узле «Панель задач» можно включить маленькие значки.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarGlomLevel",
                0,
                "workenv.taskbar.combine-buttons",
                HealthLevel.Normal,
                "Группировка кнопок панели задач изменена",
                "Если группировка отключена, панель быстрее переполняется при большом количестве окон.",
                "В узле «Панель задач» можно вернуть группировку кнопок.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "DisablePreviewDesktop",
                0,
                "workenv.windows.aero-peek",
                HealthLevel.Normal,
                "Aero Peek отключён",
                "Предварительный просмотр рабочего стола и окон может быть недоступен.",
                "В узле «Окна» можно включить Aero Peek.");

            AddDwordEnvironmentFinding(
                Registry.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer",
                "NoWindowMinimizingShortcuts",
                0,
                "workenv.windows.aero-shake",
                HealthLevel.Normal,
                "Встряхивание окна отключено политикой",
                "Aero Shake может быть недоступен для быстрого сворачивания остальных окон.",
                "В узле «Окна» можно вернуть встряхивание окна.");

            if (environmentFindings.Count == 0)
            {
                SetModuleStatus(target, HealthLevel.Good);
                return;
            }

            foreach (var finding in environmentFindings)
            {
                bool isProblem = IsProblemLevel(finding);
                if (isProblem)
                    findings.ProblemCount++;
                else
                    findings.RecommendationCount++;

                SetModuleStatus(
                    target,
                    finding.Level,
                    problems: isProblem ? 1 : 0,
                    recommendations: isProblem ? 0 : 1,
                    finding: finding);
            }
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

        private static void MarkUntouchedModulesAsGood(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            ISet<CoreModuleId> modulesToScan)
        {
            foreach (var pair in moduleStatuses)
            {
                if (!ShouldScanModule(modulesToScan, pair.Key) || IsUnavailableModule(pair.Key))
                    continue;

                if (pair.Value.Status == HealthLevel.Unknown)
                    pair.Value.Status = HealthLevel.Good;
            }
        }

        private static void MarkUnavailableModulesAsNotChecked(Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses)
        {
            foreach (var moduleId in new[] { CoreModuleId.SystemParameters, CoreModuleId.Maintenance, CoreModuleId.Network })
            {
                if (!moduleStatuses.TryGetValue(moduleId, out var status))
                    continue;

                status.Status = HealthLevel.Unknown;
                status.ProblemCount = 0;
                status.RecommendationCount = 0;
                status.CriticalCount = 0;
                status.Findings = new List<ModuleHealthFinding>();
                status.LastCheckedAt = null;
            }
        }

        private static bool IsUnavailableModule(CoreModuleId moduleId)
        {
            return moduleId == CoreModuleId.SystemParameters ||
                   moduleId == CoreModuleId.Maintenance ||
                   moduleId == CoreModuleId.Network;
        }

        private ComputerHealthStatus ApplySuppressionsAndRecalculate(
            Dictionary<CoreModuleId, ModuleHealthStatus> moduleStatuses,
            bool pendingRestart,
            DateTime checkedAt,
            ISet<CoreModuleId> modulesToScan)
        {
            int problemCount = 0;
            int recommendationCount = 0;
            int criticalCount = 0;

            foreach (var status in moduleStatuses.Values)
            {
                if (IsUnavailableModule(status.ModuleId))
                {
                    status.Findings = new List<ModuleHealthFinding>();
                    status.ProblemCount = 0;
                    status.RecommendationCount = 0;
                    status.CriticalCount = 0;
                    status.Status = HealthLevel.Unknown;
                    status.LastCheckedAt = null;
                    continue;
                }

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
                if (ShouldScanModule(modulesToScan, status.ModuleId))
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
                PendingRestart = false,
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
            settings.PendingRestart = false;
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

        private static int? ReadRegistryDwordValue(RegistryKey hive, string path, string valueName)
        {
            try
            {
                using var key = hive.OpenSubKey(path, writable: false);
                var value = key?.GetValue(valueName);

                return value switch
                {
                    int intValue => intValue,
                    uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
                    long longValue when longValue >= int.MinValue && longValue <= int.MaxValue => (int)longValue,
                    string text when int.TryParse(text, out int parsed) => parsed,
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ReadRegistryStringValue(RegistryKey hive, string path, string valueName)
        {
            try
            {
                using var key = hive.OpenSubKey(path, writable: false);
                return Convert.ToString(key?.GetValue(valueName)) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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
                    ShortHint = "Будет добавлено в следующих обновлениях",
                    Description = "Раздел временно отключён и будет добавлен в следующих обновлениях TweakWise.",
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
                    ShortHint = "Будет добавлено в следующих обновлениях",
                    Description = "Раздел временно отключён и будет добавлен в следующих обновлениях TweakWise.",
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
                    ShortHint = "Будет добавлено в следующих обновлениях",
                    Description = "Раздел временно отключён и будет добавлен в следующих обновлениях TweakWise.",
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
