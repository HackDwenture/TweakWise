using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TweakWise.Managers;
using TweakWise.Models;
using WinForms = System.Windows.Forms;

namespace TweakWise.Services
{
    public sealed class PerformanceTuningService
    {
        private const string BackupFileName = "performance-backups.json";

        private const string KindPowerScheme = "PowerScheme";
        private const string KindPowerAcSetting = "PowerAcSetting";
        private const string KindRegistryDword = "RegistryDword";
        private const string KindMemoryCompression = "MemoryCompression";
        private const string KindReadOnly = "ReadOnly";

        private const string SubProcessor = "SUB_PROCESSOR";
        private const string SubDisk = "SUB_DISK";
        private const string SubPciExpress = "SUB_PCIEXPRESS";
        private const string SubGraphics = "SUB_GRAPHICS";
        private const string SubVideo = "SUB_VIDEO";
        private const string SubSleep = "SUB_SLEEP";
        private const string SubUsb = "SUB_USB";

        private const string ProcessorMinState = "PROCTHROTTLEMIN";
        private const string ProcessorMaxState = "PROCTHROTTLEMAX";
        private const string ProcessorBoostMode = "PERFBOOSTMODE";
        private const string ProcessorEpp = "PERFEPP";
        private const string ProcessorBoostPolicy = "PERFBOOSTPOL";
        private const string ProcessorCoreParkingMin = "CPMINCORES";
        private const string ProcessorIdleDisable = "IDLEDISABLE";
        private const string SystemCoolingPolicy = "SYSCOOLPOL";
        private const string DiskIdle = "DISKIDLE";
        private const string PciExpressAspm = "ASPM";
        private const string GpuPreferencePolicy = "GPUPREFERENCEPOLICY";
        private const string VideoIdle = "VIDEOIDLE";
        private const string StandbyIdle = "STANDBYIDLE";
        private const string HibernateIdle = "HIBERNATEIDLE";
        private const string HybridSleep = "HYBRIDSLEEP";
        private const string UsbSelectiveSuspend = "USBSELECTIVE";

        private const string GraphicsDriversPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string HardwareSchedulingValueName = "HwSchMode";

        private const string MultimediaSystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string MemoryManagementPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string DwmPath = @"SOFTWARE\Microsoft\Windows\Dwm";

        private static readonly Regex GuidRegex = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        private readonly SettingsManager _settingsManager;
        private readonly Func<IReadOnlyList<TemperatureSensorReading>> _temperatureReader;
        private readonly string _backupPath;
        private readonly object _runtimeCacheSync = new object();
        private readonly Dictionary<string, PowerSettingCacheEntry> _powerSettingCache = new Dictionary<string, PowerSettingCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan RuntimeCacheLifetime = TimeSpan.FromSeconds(25);
        private IReadOnlyList<PowerPlanInfo> _powerPlansCache;
        private DateTime _powerPlansCacheUtc;
        private PowerPlanInfo _activePowerPlanCache;
        private DateTime _activePowerPlanCacheUtc;

        public PerformanceTuningService(
            SettingsManager settingsManager,
            Func<IReadOnlyList<TemperatureSensorReading>> temperatureReader)
        {
            _settingsManager = settingsManager;
            _temperatureReader = temperatureReader;
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TweakWise",
                BackupFileName);
        }

        public IReadOnlyList<PerformanceTuningItem> BuildItemsForNode(string nodeKey)
        {
            var items = new List<PerformanceTuningItem>();

            switch (nodeKey)
            {
                case "Power":
                    items.Add(CreatePowerPlanItem());
                    items.Add(CreatePowerSourceItem());
                    items.Add(CreatePowerBatteryStatusItem(20));
                    items.Add(CreateProcessorMinStateItem(30));
                    items.Add(CreateProcessorMaxStateItem("Максимальное состояние CPU от сети", 40, false));
                    items.Add(CreateProcessorBoostModeItem(50));
                    items.Add(CreateProcessorEppItem(60, false));
                    items.Add(CreateSystemCoolingPolicyItem(70));
                    items.Add(CreatePciExpressAspmItem(80));
                    items.Add(CreateDiskIdleItem(90));
                    items.Add(CreateDisplayIdleItem(100));
                    items.Add(CreateSleepIdleItem(110));
                    items.Add(CreateHibernateIdleItem(120));
                    items.Add(CreateHybridSleepItem(130));
                    items.Add(CreateUsbSelectiveSuspendItem(140));
                    break;

                case "Cpu":
                    items.Add(CreateProcessorMaxStateItem("Максимальное состояние CPU", 10, false));
                    items.Add(CreateProcessorMinStateItem(20));
                    items.Add(CreateProcessorBoostModeItem(30));
                    items.Add(CreateProcessorEppItem(40, false));
                    items.Add(CreateProcessorBoostPolicyItem(50));
                    items.Add(CreateCoreParkingItem(60));
                    items.Add(CreateProcessorIdleDisableItem(70));
                    items.Add(CreateSystemResponsivenessItem(80));
                    items.Add(CreateNetworkThrottlingItem(90));
                    items.Add(CreateTemperatureItem("cpu.temperatures", "Температура CPU", "Cpu", 100));
                    break;

                case "Gpu":
                    items.Add(CreateHardwareSchedulingItem(10));
                    items.Add(CreateGpuPreferencePolicyItem(20));
                    items.Add(CreateGameDvrItem(30));
                    items.Add(CreateAppCaptureItem(40));
                    items.Add(CreateTdrDelayItem(50));
                    items.Add(CreateMpoItem(60));
                    items.Add(CreateTemperatureItem("gpu.temperatures", "Температура GPU", "Gpu", 70));
                    break;

                case "Ram":
                    items.Add(CreateMemoryLoadItem(10));
                    items.Add(CreateMemoryCompressionItem(20));
                    items.Add(CreateClearPageFileItem(30));
                    items.Add(CreateDisablePagingExecutiveItem(40));
                    items.Add(CreateLargeSystemCacheItem(50));
                    break;

                case "Cooling":
                    items.Add(CreateThermalOverviewItem(10));
                    items.Add(CreateSystemCoolingPolicyItem(20));
                    items.Add(CreateProcessorMaxStateItem("Тепловой лимит CPU", 30, true));
                    items.Add(CreateProcessorEppItem(40, true));
                    items.Add(CreateProcessorIdleDisableItem(50));
                    break;
            }

            ApplySectionMetadata(nodeKey, items);

            return items
                .OrderByDescending(item => item.IsPriority)
                .ThenBy(item => item.Order)
                .ToList();
        }

        private static void ApplySectionMetadata(string nodeKey, IReadOnlyList<PerformanceTuningItem> items)
        {
            if (items == null)
                return;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                item.SearchKeywords = string.Join(" ", new[]
                {
                    item.SettingId,
                    item.PowerSubgroupAlias,
                    item.PowerSettingAlias,
                    item.RegistryPath,
                    item.RegistryValueName
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

                switch (nodeKey)
                {
                    case "Power":
                        ApplyPowerSection(item);
                        break;
                    case "Cpu":
                        ApplySection(
                            item,
                            item.SettingId.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase)
                                ? "Процессор"
                                : item.SettingId.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                                    ? "Температуры"
                                    : "Системный профиль",
                            item.SettingId.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase)
                                ? "Частоты, boost, парковка ядер и параметры отклика CPU."
                                : "Системные параметры, влияющие на поведение приложений под нагрузкой.");
                        break;
                    case "Gpu":
                        ApplySection(
                            item,
                            item.SettingId.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                                ? "Температуры"
                                : item.SettingId.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase)
                                    ? "Графическая подсистема"
                                    : "Графический стек Windows",
                            "Параметры Windows и драйверного слоя, доступные универсально без привязки к конкретному производителю GPU.");
                        break;
                    case "Ram":
                        ApplySection(
                            item,
                            item.SettingId == "ram.load" ? "Диагностика памяти" : "Управление памятью",
                            "Параметры памяти Windows, которые требуют аккуратной проверки перед применением.");
                        break;
                    case "Cooling":
                        ApplySection(
                            item,
                            item.SettingId.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
                            item.SettingId.Contains("thermal", StringComparison.OrdinalIgnoreCase)
                                ? "Тепловое состояние"
                                : "Политика охлаждения",
                            "Настройки, влияющие на баланс температуры, шума и производительности.");
                        break;
                    default:
                        ApplySection(item, "Параметры", "Доступные проверки и действия для выбранного узла.");
                        break;
                }
            }
        }

        private static void ApplyPowerSection(PerformanceTuningItem item)
        {
            if (item.SettingId is "power.active-plan" or "power.source" or "power.battery-status")
            {
                ApplySection(item, "Состояние питания", "Текущая схема, источник питания и состояние батареи.");
                return;
            }

            if (item.SettingId.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase))
            {
                ApplySection(item, "Процессор от сети", "Границы частот, boost и энергопредпочтение CPU при питании от сети.");
                return;
            }

            if (item.SettingId.StartsWith("cooling.", StringComparison.OrdinalIgnoreCase))
            {
                ApplySection(item, "Охлаждение", "Политика, которая определяет, что Windows делает сначала: повышает охлаждение или снижает частоты.");
                return;
            }

            if (item.SettingId.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("hibernate", StringComparison.OrdinalIgnoreCase))
            {
                ApplySection(item, "Экран, сон и гибернация", "Таймауты простоя, которые могут прерывать долгие задачи или экономить энергию.");
                return;
            }

            ApplySection(item, "Устройства и накопители", "Питание PCIe, USB и дисков в текущей схеме Windows.");
        }

        private static void ApplySection(PerformanceTuningItem item, string title, string description)
        {
            item.SectionTitle = title;
            item.SectionDescription = description;
        }

        public PerformanceTuningResult Analyze(PerformanceTuningItem item)
        {
            if (item == null)
                return PerformanceTuningResult.Fail("Не удалось определить параметр для проверки.");

            RefreshItem(item);

            if (!item.IsSupported)
                return PerformanceTuningResult.Fail(item.StatusMessage);

            if (item.RequiresElevation && !IsAdministrator())
            {
                return PerformanceTuningResult.Fail(
                    "Для применения нужны права администратора. После обновления manifest TweakWise должен запрашивать их при запуске.");
            }

            string message = item.ShowApplyAction
                ? $"Проверка пройдена. Риск: {NormalizeRisk(item.RiskLabel)}. Перед применением будет сохранён бэкап текущего значения."
                : "Проверка выполнена. Это диагностический блок без прямого изменения.";

            item.SetStatus(message, isWarning: false);
            return PerformanceTuningResult.Ok(message, item.RequiresRestart, item.RestartReason);
        }

        public PerformanceTuningResult Apply(PerformanceTuningItem item)
        {
            if (item == null)
                return PerformanceTuningResult.Fail("Не удалось определить параметр для применения.");

            RefreshItem(item);

            var validation = ValidateBeforeApply(item);
            if (!validation.Success)
            {
                item.SetStatus(validation.Message, isWarning: true);
                return validation;
            }

            PerformanceTuningResult result = item.OperationKind switch
            {
                KindPowerScheme => ApplyPowerPlan(item),
                KindPowerAcSetting => ApplyPowerAcSetting(item),
                KindRegistryDword => ApplyRegistryDword(item),
                KindMemoryCompression => ApplyMemoryCompression(item),
                _ => PerformanceTuningResult.Fail("У этого блока нет прямого действия применения.")
            };

            RefreshItem(item);

            if (result.RequiresRestart)
                _settingsManager?.MarkPendingRestart(result.RestartReason);

            item.SetStatus(result.Message, !result.Success);
            return result;
        }

        public PerformanceTuningResult Rollback(PerformanceTuningItem item)
        {
            if (item == null)
                return PerformanceTuningResult.Fail("Не удалось определить параметр для отката.");

            var backup = GetBackup(item.SettingId);
            if (backup == null)
            {
                item.SetStatus("Для этого параметра пока нет сохранённого бэкапа.", isWarning: true);
                return PerformanceTuningResult.Fail("Для этого параметра пока нет сохранённого бэкапа.");
            }

            PerformanceTuningResult result = backup.Kind switch
            {
                nameof(PerformanceBackupKind.PowerScheme) => RollbackPowerPlan(backup),
                nameof(PerformanceBackupKind.PowerAcSetting) => RollbackPowerAcSetting(backup),
                nameof(PerformanceBackupKind.RegistryDword) => RollbackRegistryDword(backup),
                nameof(PerformanceBackupKind.MemoryCompression) => RollbackMemoryCompression(backup),
                _ => PerformanceTuningResult.Fail("Тип бэкапа не поддерживается.")
            };

            if (result.Success)
                RemoveBackup(item.SettingId);

            RefreshItem(item);

            if (result.RequiresRestart)
                _settingsManager?.MarkPendingRestart(result.RestartReason);

            item.SetStatus(result.Message, !result.Success);
            return result;
        }

        private void RefreshItem(PerformanceTuningItem item)
        {
            if (item == null)
                return;

            switch (item.OperationKind)
            {
                case KindPowerScheme:
                    FillPowerPlanState(item);
                    break;
                case KindPowerAcSetting:
                    FillPowerAcSettingState(item);
                    break;
                case KindRegistryDword:
                    FillRegistryDwordState(item);
                    break;
                case KindMemoryCompression:
                    FillMemoryCompressionState(item);
                    break;
                case KindReadOnly:
                    FillReadOnlyState(item);
                    break;
            }

            item.CanRollback = GetBackup(item.SettingId) != null;
            item.CanApply = item.ShowApplyAction &&
                            item.IsSupported &&
                            (!item.RequiresElevation || IsAdministrator());
        }

        private PerformanceTuningItem CreateBaseItem(
            string settingId,
            string title,
            string description,
            string channel,
            PerformanceSettingControlKind controlKind,
            int order)
        {
            var item = new PerformanceTuningItem
            {
                SettingId = settingId,
                Title = title,
                Description = description,
                ChannelLabel = channel,
                ControlKind = controlKind,
                Order = order,
                IsSupported = true,
                ShowApplyAction = controlKind == PerformanceSettingControlKind.Toggle ||
                                  controlKind == PerformanceSettingControlKind.Combo ||
                                  controlKind == PerformanceSettingControlKind.Slider,
                ApplyButtonText = "Применить",
                OperationKind = KindReadOnly,
                RiskLabel = "низкий"
            };

            item.CanRollback = GetBackup(settingId) != null;
            return item;
        }

        private PerformanceTuningItem CreatePowerPlanItem()
        {
            var item = CreateBaseItem(
                "power.active-plan",
                "Активная схема питания",
                "Меняет текущую схему Windows через powercfg. Предыдущая схема сохраняется для отката.",
                "powercfg",
                PerformanceSettingControlKind.Combo,
                10);

            item.OperationKind = KindPowerScheme;
            item.RiskLabel = "низкий";
            item.Recommendation = "Для максимальной скорости выбирайте производительный профиль при питании от сети и нормальных температурах.";
            FillPowerPlanState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerSourceItem()
        {
            var item = CreateBaseItem(
                "power.source",
                "Источник питания",
                "Показывает, работает ли устройство от сети или батареи. На батарее производительные профили часто ограничиваются производителем.",
                "диагностика",
                PerformanceSettingControlKind.ReadOnly,
                15);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "PowerSource";
            FillPowerSourceState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerBatteryStatusItem(int order)
        {
            var item = CreateBaseItem(
                "power.battery-status",
                "Состояние батареи",
                "Показывает заряд, состояние батареи и примерное оставшееся время, если Windows предоставляет эти данные.",
                "диагностика",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "PowerBatteryStatus";
            FillPowerBatteryStatusState(item);
            return item;
        }

        private PerformanceTuningItem CreateProcessorMinStateItem(int order)
        {
            var item = CreatePowerSliderItem(
                "cpu.min-state-ac",
                "Минимальное состояние CPU",
                "Нижняя граница частоты процессора от сети. Высокое значение ускоряет отклик, но повышает нагрев и расход энергии.",
                ProcessorMinState,
                order,
                0,
                100,
                "%",
                "средний");

            item.Recommendation = "Для повседневной работы обычно достаточно 5-10%. 100% держит CPU активным почти постоянно.";
            return item;
        }

        private PerformanceTuningItem CreateProcessorMaxStateItem(string title, int order, bool thermalMode)
        {
            var item = CreatePowerSliderItem(
                "cpu.max-state-ac",
                title,
                thermalMode
                    ? "Ограничивает верхний предел CPU от сети. Это снижает нагрев ценой пиковой производительности."
                    : "Верхняя граница частоты CPU от сети. 100% оставляет максимум производительности.",
                ProcessorMaxState,
                order,
                5,
                100,
                "%",
                thermalMode ? "средний" : "низкий");

            item.Recommendation = thermalMode
                ? "При перегреве начните с 95%. Значения ниже 80% лучше использовать только как временную диагностику."
                : "Для максимальной скорости оставьте 100%. Для тихой работы попробуйте 95-99%.";
            return item;
        }

        private PerformanceTuningItem CreateProcessorBoostModeItem(int order)
        {
            var item = CreatePowerComboItem(
                "cpu.boost-mode-ac",
                "Режим ускорения CPU",
                "Скрытый параметр powercfg, который управляет поведением турбо-ускорения процессора.",
                ProcessorBoostMode,
                order,
                "средний");

            item.Options.Add(new PerformanceTuningOption("Отключено", "0", "Меньше нагрев, ниже пиковые частоты."));
            item.Options.Add(new PerformanceTuningOption("Включено", "1", "Стандартное ускорение Windows."));
            item.Options.Add(new PerformanceTuningOption("Агрессивно", "2", "Быстрее поднимает частоты, сильнее греет."));
            item.Options.Add(new PerformanceTuningOption("Эффективно", "3", "Осторожнее расходует энергию."));
            item.Options.Add(new PerformanceTuningOption("Эффективно агрессивно", "4", "Баланс скорости и эффективности."));
            item.Options.Add(new PerformanceTuningOption("Агрессивно при гарантированном", "5", "Высокий риск нагрева на ноутбуках."));
            item.Options.Add(new PerformanceTuningOption("Эффективно агрессивно при гарантированном", "6", "Продвинутый OEM-режим."));
            item.Recommendation = "Для производительности обычно подходит «Агрессивно». При перегреве переходите на «Эффективно» или снижайте лимит CPU.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateProcessorEppItem(int order, bool thermalMode)
        {
            var item = CreatePowerSliderItem(
                thermalMode ? "cpu.epp-ac.cooling" : "cpu.epp-ac",
                thermalMode ? "Предпочтение эффективности CPU" : "Energy Performance Preference",
                "Скрытый параметр CPU: 0 означает максимальную производительность, 100 - максимальную экономию и меньший нагрев.",
                ProcessorEpp,
                order,
                0,
                100,
                "%",
                thermalMode ? "средний" : "высокий");

            item.Recommendation = thermalMode
                ? "Если система греется, попробуйте 25-50. Для максимальной скорости ставьте ближе к 0."
                : "0 даёт самый агрессивный отклик, но может повысить шум и температуру.";
            return item;
        }

        private PerformanceTuningItem CreateProcessorBoostPolicyItem(int order)
        {
            var item = CreatePowerSliderItem(
                "cpu.boost-policy-ac",
                "Политика усиления CPU",
                "Скрытый процентный параметр powercfg, который влияет на агрессивность усиления производительности.",
                ProcessorBoostPolicy,
                order,
                0,
                100,
                "%",
                "высокий");

            item.Recommendation = "100% - максимальная агрессия. Если появляются нагрев и шум, снижайте постепенно.";
            return item;
        }

        private PerformanceTuningItem CreateCoreParkingItem(int order)
        {
            var item = CreatePowerSliderItem(
                "cpu.core-parking-min-ac",
                "Минимум активных ядер",
                "Скрытый параметр парковки ядер. Чем выше значение, тем больше ядер остаётся готовыми к работе.",
                ProcessorCoreParkingMin,
                order,
                0,
                100,
                "%",
                "высокий");

            item.Recommendation = "100% уменьшает задержки, но повышает фоновой нагрев. Для ноутбуков безопаснее 10-50%.";
            return item;
        }

        private PerformanceTuningItem CreateProcessorIdleDisableItem(int order)
        {
            var item = CreatePowerToggleItem(
                "cpu.disable-idle-ac",
                "Отключить простои CPU",
                "Очень агрессивный скрытый параметр. CPU перестаёт уходить в глубокие idle-состояния: отклик выше, нагрев и расход резко выше.",
                ProcessorIdleDisable,
                order,
                1,
                0,
                "высокий");

            item.EnabledText = "Простои CPU отключены";
            item.DisabledText = "Простои CPU разрешены";
            item.Recommendation = "Используйте только для диагностики задержек. На ноутбуках может быстро поднять температуру.";
            return item;
        }

        private PerformanceTuningItem CreateSystemCoolingPolicyItem(int order)
        {
            var item = CreatePowerComboItem(
                "cooling.system-policy-ac",
                "Политика охлаждения",
                "Скрытый powercfg-параметр: активная политика повышает обороты охлаждения перед снижением частот, пассивная сначала снижает частоты.",
                SystemCoolingPolicy,
                order,
                "средний");

            item.Options.Add(new PerformanceTuningOption("Пассивная", "0", "Тише, но раньше снижает частоты."));
            item.Options.Add(new PerformanceTuningOption("Активная", "1", "Лучше держит производительность, может быть шумнее."));
            item.Recommendation = "Для производительности и охлаждения обычно лучше активная политика.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreatePciExpressAspmItem(int order)
        {
            var item = CreatePowerComboItem(
                "power.pcie-aspm-ac",
                "PCI Express Link State",
                "Управляет энергосбережением PCIe от сети. Отключение может снизить задержки устройств и GPU, но увеличит расход энергии.",
                PciExpressAspm,
                order,
                "средний",
                SubPciExpress);

            item.Options.Add(new PerformanceTuningOption("Отключено", "0", "Максимальная отзывчивость PCIe."));
            item.Options.Add(new PerformanceTuningOption("Умеренно", "1", "Баланс."));
            item.Options.Add(new PerformanceTuningOption("Максимальное энергосбережение", "2", "Меньше расход, возможны задержки."));
            item.Recommendation = "Для производительных режимов обычно выбирают «Отключено».";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateDiskIdleItem(int order)
        {
            var item = CreateBaseItem(
                "power.disk-idle-ac",
                "Отключение диска от сети",
                "Задаёт таймаут отключения накопителя в текущей схеме питания. 0 минут означает «никогда».",
                "powercfg",
                PerformanceSettingControlKind.Slider,
                order);

            item.OperationKind = KindPowerAcSetting;
            item.PowerSubgroupAlias = SubDisk;
            item.PowerSettingAlias = DiskIdle;
            item.PowerValueScale = 60;
            item.Minimum = 0;
            item.Maximum = 120;
            item.NumericStep = 5;
            item.ValueUnit = " мин";
            item.RiskLabel = "низкий";
            item.Recommendation = "Для стационарного ПК и игр можно поставить 0, чтобы диск не засыпал во время нагрузки.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateDisplayIdleItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.display-idle-ac",
                "Отключение экрана от сети",
                "Задаёт таймаут выключения дисплея в текущей схеме питания. 0 минут означает «никогда».",
                VideoIdle,
                order,
                0,
                180,
                " мин",
                "низкий",
                SubVideo);

            item.PowerValueScale = 60;
            item.NumericStep = 5;
            item.Recommendation = "Для стационарной работы обычно удобно 10-30 минут. Для диагностики или презентаций можно временно поставить 0.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateSleepIdleItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.sleep-idle-ac",
                "Переход в сон от сети",
                "Определяет, через сколько минут простоя Windows переведёт компьютер в сон при питании от сети. 0 минут означает «никогда».",
                StandbyIdle,
                order,
                0,
                240,
                " мин",
                "низкий",
                SubSleep);

            item.PowerValueScale = 60;
            item.NumericStep = 5;
            item.Recommendation = "Для рабочих станций и длительных задач лучше не ставить слишком короткий таймаут, чтобы процессы не прерывались.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateHibernateIdleItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.hibernate-idle-ac",
                "Гибернация от сети",
                "Определяет таймаут перехода в гибернацию в текущей схеме питания. На некоторых ПК параметр скрыт или отключён производителем.",
                HibernateIdle,
                order,
                0,
                360,
                " мин",
                "низкий",
                SubSleep);

            item.PowerValueScale = 60;
            item.NumericStep = 10;
            item.Recommendation = "Если компьютер выполняет долгие задачи без участия пользователя, используйте 0 или значение больше таймаута сна.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateHybridSleepItem(int order)
        {
            var item = CreatePowerToggleItem(
                "power.hybrid-sleep-ac",
                "Гибридный спящий режим",
                "Сохраняет состояние в память и на диск перед сном. Это повышает устойчивость к потере питания, но может замедлить переход в сон.",
                HybridSleep,
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "низкий",
                subgroupAlias: SubSleep);

            item.Recommendation = "Для настольного ПК гибридный сон обычно полезен. Для ноутбука ориентируйтесь на поведение производителя и скорость выхода из сна.";
            return item;
        }

        private PerformanceTuningItem CreateUsbSelectiveSuspendItem(int order)
        {
            var item = CreatePowerToggleItem(
                "power.usb-selective-suspend-ac",
                "Выборочное приостановление USB",
                "Позволяет Windows временно отключать неактивные USB-устройства. Отключение может помочь при обрывах USB-аудио, VR, геймпадов или внешних накопителей.",
                UsbSelectiveSuspend,
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "средний",
                subgroupAlias: SubUsb);

            item.Recommendation = "Если USB-устройства работают стабильно, оставьте включённым. Отключайте только при реальных обрывах или задержках.";
            return item;
        }

        private PerformanceTuningItem CreateGpuPreferencePolicyItem(int order)
        {
            var item = CreatePowerComboItem(
                "gpu.preference-policy-ac",
                "Политика выбора GPU",
                "Powercfg-параметр графической подсистемы. На части систем доступен только выбор по умолчанию или экономичный GPU.",
                GpuPreferencePolicy,
                order,
                "низкий",
                SubGraphics);

            item.Options.Add(new PerformanceTuningOption("По умолчанию", "0", "Windows и драйвер выбирают режим."));
            item.Options.Add(new PerformanceTuningOption("Низкое энергопотребление", "1", "Может снижать расход, но не всегда подходит для игр."));
            item.Recommendation = "Если нужен максимум GPU, оставьте «По умолчанию» и настраивайте профиль драйвера.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateSystemResponsivenessItem(int order)
        {
            var item = CreateRegistrySliderItem(
                "cpu.system-responsiveness",
                "MMCSS SystemResponsiveness",
                "Скрытый системный профиль мультимедиа. Меньшее значение отдаёт больше ресурсов активной задаче, но может ухудшить фоновые процессы.",
                "HKLM",
                Registry.LocalMachine,
                MultimediaSystemProfilePath,
                "SystemResponsiveness",
                order,
                0,
                100,
                "%",
                "высокий",
                defaultValue: 20);

            item.RequiresRestart = true;
            item.RestartReason = "изменение профиля MMCSS";
            item.Recommendation = "Для игр часто используют 0-10. Если звук, запись или фоновые задачи ведут себя хуже, откатите значение.";
            return item;
        }

        private PerformanceTuningItem CreateNetworkThrottlingItem(int order)
        {
            var item = CreateRegistryComboItem(
                "cpu.network-throttling",
                "NetworkThrottlingIndex",
                "Скрытый MMCSS-параметр сетевого throttling. Отключение может помочь задержкам, но меняет поведение сетевого стека.",
                "HKLM",
                Registry.LocalMachine,
                MultimediaSystemProfilePath,
                "NetworkThrottlingIndex",
                order,
                "высокий",
                defaultValue: 10);

            item.Options.Add(new PerformanceTuningOption("Windows default: 10", "10", "Стандартное ограничение Windows."));
            item.Options.Add(new PerformanceTuningOption("Отключить throttling", "0xFFFFFFFF", "Агрессивный режим для задержек."));
            item.RequiresRestart = true;
            item.RestartReason = "изменение сетевого throttling MMCSS";
            item.Recommendation = "Если после отключения появились проблемы сети или стриминга, верните default.";
            FillRegistryDwordState(item);
            return item;
        }

        private PerformanceTuningItem CreateHardwareSchedulingItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "gpu.hardware-scheduling",
                "Аппаратное планирование GPU",
                "Системный режим планирования видеокарты. Изменение пишется в HKLM и применяется после перезагрузки.",
                "HKLM",
                Registry.LocalMachine,
                GraphicsDriversPath,
                HardwareSchedulingValueName,
                order,
                enabledValue: 2,
                disabledValue: 1,
                risk: "средний",
                defaultValue: 1);

            item.RequiresRestart = true;
            item.RestartReason = "изменение аппаратного планирования GPU";
            item.EnabledText = "Включено";
            item.DisabledText = "Отключено или по умолчанию";
            item.Recommendation = "Если после включения появились фризы или проблемы драйвера, откатите и перезагрузите ПК.";
            return item;
        }

        private PerformanceTuningItem CreateGameDvrItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "gpu.game-dvr",
                "Game DVR",
                "Отключает системную запись игровых клипов через GameConfigStore. Это снижает лишний фон в игровых сценариях.",
                "HKCU",
                Registry.CurrentUser,
                @"System\GameConfigStore",
                "GameDVR_Enabled",
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "низкий",
                defaultValue: 1);

            item.EnabledText = "Запись Game DVR разрешена";
            item.DisabledText = "Запись Game DVR отключена";
            item.Recommendation = "Для чистой производительности обычно отключают. Если нужны клипы Xbox/Game Bar, оставьте включённым.";
            return item;
        }

        private PerformanceTuningItem CreateAppCaptureItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "gpu.app-capture",
                "Фоновый захват игр",
                "Управляет AppCaptureEnabled в профиле пользователя. Это внутренний тумблер, без перехода в Windows Settings.",
                "HKCU",
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "AppCaptureEnabled",
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "низкий",
                defaultValue: 1);

            item.EnabledText = "Захват включён";
            item.DisabledText = "Захват отключён";
            item.Recommendation = "Отключайте, если не используете запись экрана и хотите убрать лишние фоновые хуки.";
            return item;
        }

        private PerformanceTuningItem CreateTdrDelayItem(int order)
        {
            var item = CreateRegistrySliderItem(
                "gpu.tdr-delay",
                "TDR Delay драйвера GPU",
                "Время ожидания драйвера видеокарты перед сбросом. Большее значение может помочь тяжёлым GPU-задачам, но способно дольше держать зависший драйвер.",
                "HKLM",
                Registry.LocalMachine,
                GraphicsDriversPath,
                "TdrDelay",
                order,
                2,
                60,
                " сек",
                "высокий",
                defaultValue: 2);

            item.RequiresRestart = true;
            item.RestartReason = "изменение TDR Delay GPU";
            item.Recommendation = "Не повышайте без причины. Для диагностики обычно хватает 8-10 секунд.";
            return item;
        }

        private PerformanceTuningItem CreateMpoItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "gpu.disable-mpo",
                "Отключить MPO",
                "Multiplane Overlay может влиять на мерцания, оверлеи и задержки на некоторых драйверах. Тумблер задаёт OverlayTestMode.",
                "HKLM",
                Registry.LocalMachine,
                DwmPath,
                "OverlayTestMode",
                order,
                enabledValue: 5,
                disabledValue: 0,
                risk: "высокий",
                defaultValue: 0);

            item.RegistryDeleteWhenDisabled = true;
            item.RequiresRestart = true;
            item.RestartReason = "изменение DWM Multiplane Overlay";
            item.EnabledText = "MPO отключён";
            item.DisabledText = "MPO в режиме Windows";
            item.Recommendation = "Используйте при проблемах с оверлеями/мерцанием. Если стало хуже, откатите.";
            return item;
        }

        private PerformanceTuningItem CreateMemoryLoadItem(int order)
        {
            var item = CreateBaseItem(
                "ram.load",
                "Текущая загрузка ОЗУ",
                "Реальная загрузка физической памяти. Если ОЗУ почти заполнена, сначала ищите тяжёлые процессы.",
                "диагностика",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "MemoryLoad";
            FillMemoryLoadState(item);
            return item;
        }

        private PerformanceTuningItem CreateMemoryCompressionItem(int order)
        {
            var item = CreateBaseItem(
                "ram.memory-compression",
                "Сжатие памяти Windows",
                "MMAgent может сжимать часть данных в ОЗУ, чтобы реже обращаться к файлу подкачки.",
                "MMAgent",
                PerformanceSettingControlKind.Toggle,
                order);

            item.OperationKind = KindMemoryCompression;
            item.RequiresElevation = true;
            item.RequiresRestart = true;
            item.RestartReason = "изменение сжатия памяти Windows";
            item.RiskLabel = "средний";
            item.EnabledText = "Сжатие включено";
            item.DisabledText = "Сжатие отключено";
            item.ApplyButtonText = "Применить и перезагрузить позже";
            item.Recommendation = "Обычно лучше оставить включённым. Отключайте только для проверки конкретной проблемы.";
            FillMemoryCompressionState(item);
            return item;
        }

        private PerformanceTuningItem CreateClearPageFileItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "ram.clear-pagefile",
                "Очищать файл подкачки при выключении",
                "Стирает pagefile при завершении работы. Это повышает приватность, но может сильно замедлить выключение.",
                "HKLM",
                Registry.LocalMachine,
                MemoryManagementPath,
                "ClearPageFileAtShutdown",
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "средний",
                defaultValue: 0);

            item.RequiresRestart = true;
            item.RestartReason = "изменение политики очистки pagefile";
            item.EnabledText = "Очистка включена";
            item.DisabledText = "Очистка отключена";
            item.Recommendation = "Для производительности выключения обычно держат отключённым.";
            return item;
        }

        private PerformanceTuningItem CreateDisablePagingExecutiveItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "ram.disable-paging-executive",
                "Держать ядро и драйверы в RAM",
                "DisablePagingExecutive просит Windows не выгружать часть ядра и драйверов в файл подкачки. Может помочь на системах с большим объёмом RAM.",
                "HKLM",
                Registry.LocalMachine,
                MemoryManagementPath,
                "DisablePagingExecutive",
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "высокий",
                defaultValue: 0);

            item.RequiresRestart = true;
            item.RestartReason = "изменение политики памяти ядра";
            item.EnabledText = "Ядро удерживается в RAM";
            item.DisabledText = "Стандартная политика Windows";
            item.Recommendation = "Не включайте на ПК с малым объёмом RAM. Если появились ошибки драйверов, откатите.";
            return item;
        }

        private PerformanceTuningItem CreateLargeSystemCacheItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "ram.large-system-cache",
                "LargeSystemCache",
                "Смещает поведение кэша памяти в сторону серверного профиля. На обычном ПК может мешать играм и интерактивным задачам.",
                "HKLM",
                Registry.LocalMachine,
                MemoryManagementPath,
                "LargeSystemCache",
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "высокий",
                defaultValue: 0);

            item.RequiresRestart = true;
            item.RestartReason = "изменение системного кэша памяти";
            item.EnabledText = "Серверный кэш включён";
            item.DisabledText = "Обычный кэш Windows";
            item.Recommendation = "Для игровых и рабочих станций обычно лучше оставить отключённым.";
            return item;
        }

        private PerformanceTuningItem CreateThermalOverviewItem(int order)
        {
            var item = CreateBaseItem(
                "cooling.overview",
                "Температурный контур",
                "Собирает доступные датчики CPU, GPU и платы. Если производитель не отдаёт датчик, TweakWise не подменяет его фейком.",
                "датчики",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "Temperature";
            FillTemperatureState(item);
            return item;
        }

        private PerformanceTuningItem CreateTemperatureItem(string settingId, string title, string group, int order)
        {
            var item = CreateBaseItem(
                settingId,
                title,
                "Считывает реальные доступные датчики. На некоторых ПК часть датчиков закрыта производителем.",
                "датчики",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "Temperature";
            item.SensorGroup = group;
            FillTemperatureState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerSliderItem(
            string settingId,
            string title,
            string description,
            string settingAlias,
            int order,
            double minimum,
            double maximum,
            string unit,
            string risk,
            string subgroupAlias = SubProcessor)
        {
            var item = CreateBaseItem(settingId, title, description, "powercfg", PerformanceSettingControlKind.Slider, order);
            item.OperationKind = KindPowerAcSetting;
            item.PowerSubgroupAlias = subgroupAlias;
            item.PowerSettingAlias = settingAlias;
            item.Minimum = minimum;
            item.Maximum = maximum;
            item.NumericStep = 1;
            item.ValueUnit = unit;
            item.RiskLabel = risk;
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerComboItem(
            string settingId,
            string title,
            string description,
            string settingAlias,
            int order,
            string risk,
            string subgroupAlias = SubProcessor)
        {
            var item = CreateBaseItem(settingId, title, description, "powercfg", PerformanceSettingControlKind.Combo, order);
            item.OperationKind = KindPowerAcSetting;
            item.PowerSubgroupAlias = subgroupAlias;
            item.PowerSettingAlias = settingAlias;
            item.RiskLabel = risk;
            return item;
        }

        private PerformanceTuningItem CreatePowerToggleItem(
            string settingId,
            string title,
            string description,
            string settingAlias,
            int order,
            int enabledValue,
            int disabledValue,
            string risk,
            string subgroupAlias = SubProcessor)
        {
            var item = CreateBaseItem(settingId, title, description, "powercfg", PerformanceSettingControlKind.Toggle, order);
            item.OperationKind = KindPowerAcSetting;
            item.PowerSubgroupAlias = subgroupAlias;
            item.PowerSettingAlias = settingAlias;
            item.EnabledValue = enabledValue;
            item.DisabledValue = disabledValue;
            item.RiskLabel = risk;
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateRegistryToggleItem(
            string settingId,
            string title,
            string description,
            string channel,
            RegistryKey hive,
            string path,
            string valueName,
            int order,
            int enabledValue,
            int disabledValue,
            string risk,
            int defaultValue)
        {
            var item = CreateBaseItem(settingId, title, description, channel, PerformanceSettingControlKind.Toggle, order);
            item.OperationKind = KindRegistryDword;
            item.RegistryHiveName = channel;
            item.RegistryHive = hive;
            item.RegistryPath = path;
            item.RegistryValueName = valueName;
            item.EnabledValue = enabledValue;
            item.DisabledValue = disabledValue;
            item.DefaultDwordValue = defaultValue;
            item.RequiresElevation = string.Equals(channel, "HKLM", StringComparison.OrdinalIgnoreCase);
            item.RiskLabel = risk;
            FillRegistryDwordState(item);
            return item;
        }

        private PerformanceTuningItem CreateRegistrySliderItem(
            string settingId,
            string title,
            string description,
            string channel,
            RegistryKey hive,
            string path,
            string valueName,
            int order,
            double minimum,
            double maximum,
            string unit,
            string risk,
            int defaultValue)
        {
            var item = CreateBaseItem(settingId, title, description, channel, PerformanceSettingControlKind.Slider, order);
            item.OperationKind = KindRegistryDword;
            item.RegistryHiveName = channel;
            item.RegistryHive = hive;
            item.RegistryPath = path;
            item.RegistryValueName = valueName;
            item.DefaultDwordValue = defaultValue;
            item.Minimum = minimum;
            item.Maximum = maximum;
            item.NumericStep = 1;
            item.ValueUnit = unit;
            item.RequiresElevation = string.Equals(channel, "HKLM", StringComparison.OrdinalIgnoreCase);
            item.RiskLabel = risk;
            FillRegistryDwordState(item);
            return item;
        }

        private PerformanceTuningItem CreateRegistryComboItem(
            string settingId,
            string title,
            string description,
            string channel,
            RegistryKey hive,
            string path,
            string valueName,
            int order,
            string risk,
            int defaultValue)
        {
            var item = CreateBaseItem(settingId, title, description, channel, PerformanceSettingControlKind.Combo, order);
            item.OperationKind = KindRegistryDword;
            item.RegistryHiveName = channel;
            item.RegistryHive = hive;
            item.RegistryPath = path;
            item.RegistryValueName = valueName;
            item.DefaultDwordValue = defaultValue;
            item.RequiresElevation = string.Equals(channel, "HKLM", StringComparison.OrdinalIgnoreCase);
            item.RiskLabel = risk;
            return item;
        }

        private PerformanceTuningResult ValidateBeforeApply(PerformanceTuningItem item)
        {
            if (!item.IsSupported)
                return PerformanceTuningResult.Fail(item.StatusMessage);

            if (item.RequiresElevation && !IsAdministrator())
                return PerformanceTuningResult.Fail("Приложение не запущено с правами администратора, поэтому запись заблокирована.");

            if (item.OperationKind == KindPowerAcSetting)
            {
                long targetValue = ResolvePowerTargetValue(item);

                if (string.Equals(item.PowerSettingAlias, ProcessorMinState, StringComparison.OrdinalIgnoreCase))
                {
                    var max = QueryPowerSetting(SubProcessor, ProcessorMaxState);
                    if (max.Success && max.CurrentAcIndex.HasValue && targetValue > max.CurrentAcIndex.Value)
                        return PerformanceTuningResult.Fail("Минимальное состояние CPU не может быть выше текущего максимального состояния CPU.");
                }

                if (string.Equals(item.PowerSettingAlias, ProcessorMaxState, StringComparison.OrdinalIgnoreCase))
                {
                    var min = QueryPowerSetting(SubProcessor, ProcessorMinState);
                    if (min.Success && min.CurrentAcIndex.HasValue && targetValue < min.CurrentAcIndex.Value)
                        return PerformanceTuningResult.Fail("Максимальное состояние CPU не может быть ниже текущего минимального состояния CPU.");
                }
            }

            return PerformanceTuningResult.Ok("Проверка пройдена.");
        }

        private void FillReadOnlyState(PerformanceTuningItem item)
        {
            switch (item.ReadOnlyKind)
            {
                case "PowerSource":
                    FillPowerSourceState(item);
                    break;
                case "PowerBatteryStatus":
                    FillPowerBatteryStatusState(item);
                    break;
                case "MemoryLoad":
                    FillMemoryLoadState(item);
                    break;
                case "Temperature":
                    FillTemperatureState(item);
                    break;
            }
        }

        private void FillPowerPlanState(PerformanceTuningItem item)
        {
            item.Options.Clear();

            var plans = GetPowerPlans();
            foreach (var plan in plans)
                item.Options.Add(new PerformanceTuningOption(plan.Name, plan.Guid, plan.IsActive ? "Текущая схема" : string.Empty));

            var active = plans.FirstOrDefault(plan => plan.IsActive) ?? plans.FirstOrDefault();
            if (active != null)
            {
                item.SelectedOption = item.Options.FirstOrDefault(option => option.Value == active.Guid);
                item.CurrentValue = active.Name;
                item.StatusMessage = string.Empty;
                item.IsSupported = true;
                item.IsPriority = active.Name.IndexOf("quiet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  active.Name.IndexOf("eco", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else
            {
                item.CurrentValue = "Не удалось прочитать схемы питания";
                item.IsSupported = false;
                item.SetStatus("Windows не вернула список схем питания через powercfg.", isWarning: true);
            }
        }

        private void FillPowerSourceState(PerformanceTuningItem item)
        {
            try
            {
                var status = WinForms.SystemInformation.PowerStatus;
                bool onBattery = status.PowerLineStatus == WinForms.PowerLineStatus.Offline;
                string charge = status.BatteryLifePercent >= 0
                    ? $"{Math.Round(status.BatteryLifePercent * 100)}%"
                    : "неизвестно";

                item.CurrentValue = onBattery
                    ? $"Питание от батареи, заряд {charge}"
                    : "Питание от сети";
                item.Recommendation = onBattery
                    ? "Для производительных профилей подключите питание от сети."
                    : "Ограничений из-за батареи сейчас не видно.";
                item.IsPriority = onBattery;
            }
            catch
            {
                item.CurrentValue = "Не удалось определить источник питания";
            }
        }

        private void FillPowerBatteryStatusState(PerformanceTuningItem item)
        {
            try
            {
                var status = WinForms.SystemInformation.PowerStatus;
                bool hasBattery = status.BatteryChargeStatus != WinForms.BatteryChargeStatus.NoSystemBattery;
                if (!hasBattery)
                {
                    item.CurrentValue = "Батарея не обнаружена";
                    item.Recommendation = "Для стационарного ПК это нормально. Настройки от сети остаются применимыми.";
                    item.IsSupported = true;
                    return;
                }

                string charge = status.BatteryLifePercent >= 0
                    ? $"{Math.Round(status.BatteryLifePercent * 100)}%"
                    : "заряд неизвестен";

                string remaining = status.BatteryLifeRemaining >= 0
                    ? FormatDuration(TimeSpan.FromSeconds(status.BatteryLifeRemaining))
                    : "время не сообщается";

                item.CurrentValue = $"{charge} · {TranslateBatteryStatus(status.BatteryChargeStatus)} · осталось: {remaining}";
                item.Recommendation = status.PowerLineStatus == WinForms.PowerLineStatus.Offline
                    ? "При работе от батареи Windows и прошивка ноутбука могут ограничивать производительность независимо от выбранной схемы."
                    : "Батарея обнаружена, сейчас устройство работает от сети.";
                item.IsPriority = status.PowerLineStatus == WinForms.PowerLineStatus.Offline;
            }
            catch
            {
                item.CurrentValue = "Windows не предоставила данные батареи";
                item.Recommendation = "Это возможно на настольных ПК, виртуальных машинах или устройствах с ограниченным ACPI-драйвером.";
            }
        }

        private void FillPowerAcSettingState(PerformanceTuningItem item)
        {
            var state = QueryPowerSetting(item.PowerSubgroupAlias, item.PowerSettingAlias);
            if (!state.Success || !state.CurrentAcIndex.HasValue)
            {
                item.IsSupported = false;
                item.CurrentValue = "Параметр недоступен";
                item.SetStatus("Текущая схема питания, прошивка или драйвер ACPI скрывают этот параметр. Можно попробовать другую схему питания, обновить драйверы чипсета/питания или оставить пункт без изменения.", isWarning: true);
                return;
            }

            item.IsSupported = true;
            long current = state.CurrentAcIndex.Value;

            if (item.IsSlider)
            {
                double scale = item.PowerValueScale <= 0 ? 1 : item.PowerValueScale;
                double displayValue = current / scale;

                if (state.Minimum.HasValue)
                    item.Minimum = Math.Max(item.Minimum, state.Minimum.Value / scale);

                if (state.Maximum.HasValue && item.Maximum > 0)
                    item.Maximum = Math.Min(item.Maximum, state.Maximum.Value / scale);

                item.NumericValue = Math.Clamp(displayValue, item.Minimum, item.Maximum);
                item.CurrentValue = $"{displayValue:0}{item.ValueUnit}";
            }
            else if (item.IsToggle)
            {
                item.ToggleValue = current == item.EnabledValue;
                item.CurrentValue = item.ToggleValue ? item.EnabledText : item.DisabledText;
            }
            else if (item.IsCombo)
            {
                string value = current.ToString(CultureInfo.InvariantCulture);
                item.SelectedOption = item.Options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
                if (item.SelectedOption == null)
                {
                    item.SelectedOption = new PerformanceTuningOption($"Значение {value}", value, "Текущее значение Windows");
                    item.Options.Add(item.SelectedOption);
                }

                item.CurrentValue = item.SelectedOption.Label;
            }

            item.StatusMessage = string.Empty;
            item.IsPriority = IsPowerItemPriority(item, current);
        }

        private void FillRegistryDwordState(PerformanceTuningItem item)
        {
            var state = ReadRegistryDword(item.RegistryHive, item.RegistryPath, item.RegistryValueName);
            int current = state.Exists ? state.Value : item.DefaultDwordValue;

            item.IsSupported = item.RegistryHive != null;
            if (!item.IsSupported)
            {
                item.CurrentValue = "Ветка реестра недоступна";
                item.SetStatus("Не удалось определить ветку реестра для параметра.", isWarning: true);
                return;
            }

            if (item.IsToggle)
            {
                item.ToggleValue = state.Exists && current == item.EnabledValue;
                item.CurrentValue = item.ToggleValue ? item.EnabledText : item.DisabledText;
            }
            else if (item.IsSlider)
            {
                item.NumericValue = Math.Clamp(current, item.Minimum, item.Maximum);
                item.CurrentValue = $"{current}{item.ValueUnit}";
            }
            else if (item.IsCombo)
            {
                string value = FormatDwordValue(current);
                item.SelectedOption = item.Options.FirstOrDefault(option =>
                    ParseDwordValue(option.Value) == current);

                if (item.SelectedOption == null)
                {
                    item.SelectedOption = new PerformanceTuningOption(value, value);
                    item.Options.Add(item.SelectedOption);
                }

                item.CurrentValue = item.SelectedOption.Label;
            }

            if (!state.Exists)
                item.CurrentValue += " (значение отсутствует, используется default)";

            item.StatusMessage = string.Empty;
        }

        private void FillMemoryCompressionState(PerformanceTuningItem item)
        {
            var result = RunPowerShell("try { [bool]((Get-MMAgent).MemoryCompression) } catch { Write-Error $_.Exception.Message; exit 1 }");
            if (!result.Success)
            {
                item.CurrentValue = "Нужны права администратора";
                item.IsSupported = true;
                item.SetStatus("MMAgent не разрешил чтение без прав администратора.", isWarning: true);
                return;
            }

            bool enabled = result.Output.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0;
            item.ToggleValue = enabled;
            item.CurrentValue = enabled ? item.EnabledText : item.DisabledText;
            item.IsSupported = true;
            item.StatusMessage = string.Empty;
        }

        private void FillMemoryLoadState(PerformanceTuningItem item)
        {
            var memory = ReadMemoryStatus();
            if (memory == null || memory.ullTotalPhys == 0)
            {
                item.CurrentValue = "Не удалось прочитать память";
                return;
            }

            double usedGb = (memory.ullTotalPhys - memory.ullAvailPhys) / 1024d / 1024d / 1024d;
            double totalGb = memory.ullTotalPhys / 1024d / 1024d / 1024d;
            item.CurrentValue = $"{memory.dwMemoryLoad}% занято · {usedGb:0.0} из {totalGb:0.0} ГБ";

            if (memory.dwMemoryLoad >= 90)
            {
                item.Recommendation = "ОЗУ почти заполнена. Сначала закройте тяжёлые процессы, затем меняйте системные параметры.";
                item.IsPriority = true;
            }
            else if (memory.dwMemoryLoad >= 78)
            {
                item.Recommendation = "Память заметно загружена. Перед производительными профилями лучше освободить RAM.";
                item.IsPriority = true;
            }
            else
            {
                item.Recommendation = "Запас памяти выглядит нормальным.";
                item.IsPriority = false;
            }
        }

        private void FillTemperatureState(PerformanceTuningItem item)
        {
            IReadOnlyList<TemperatureSensorReading> readings;
            try
            {
                readings = _temperatureReader?.Invoke() ?? Array.Empty<TemperatureSensorReading>();
            }
            catch
            {
                readings = Array.Empty<TemperatureSensorReading>();
            }

            if (!string.IsNullOrWhiteSpace(item.SensorGroup))
            {
                readings = readings
                    .Where(reading => string.Equals(reading.Group, item.SensorGroup, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                readings = readings
                    .Where(reading => reading.Group == "Cpu" ||
                                      reading.Group == "Gpu" ||
                                      reading.Group == "Motherboard" ||
                                      reading.Group == "Other")
                    .ToList();
            }

            if (readings.Count == 0)
            {
                item.CurrentValue = "Датчики не доступны";
                item.Recommendation = "Это зависит от производителя платы, ноутбука и драйверов мониторинга.";
                return;
            }

            var hottest = readings.OrderByDescending(reading => reading.ValueCelsius).First();
            item.CurrentValue = $"{hottest.Title}: {HardwareTemperatureService.FormatTemperature(hottest.ValueCelsius)}";
            item.IsPriority = hottest.ValueCelsius >= 82;
            item.Recommendation = hottest.ValueCelsius >= 90
                ? "Температура высокая: проверьте пыль, вентиляцию корпуса и режим питания."
                : hottest.ValueCelsius >= 82
                    ? "Температура близка к зоне ограничений. Снизьте boost/EPP или включите активную политику охлаждения."
                    : "Температурный запас выглядит нормальным.";
        }

        private PerformanceTuningResult ApplyPowerPlan(PerformanceTuningItem item)
        {
            if (item.SelectedOption == null || string.IsNullOrWhiteSpace(item.SelectedOption.Value))
                return PerformanceTuningResult.Fail("Выберите схему питания.");

            var active = GetActivePowerPlan();
            if (active == null)
                return PerformanceTuningResult.Fail("Не удалось сохранить текущую схему питания для бэкапа.");

            SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.PowerScheme),
                Value = active.Guid,
                Label = active.Name,
                CreatedAtUtc = DateTime.UtcNow
            });

            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/setactive", item.SelectedOption.Value);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Windows не применила схему питания: {BuildCommandError(result)}");

            return PerformanceTuningResult.Ok($"Схема питания изменена на «{item.SelectedOption.Label}». Бэкап сохранён.");
        }

        private PerformanceTuningResult ApplyPowerAcSetting(PerformanceTuningItem item)
        {
            var state = QueryPowerSetting(item.PowerSubgroupAlias, item.PowerSettingAlias);
            if (!state.Success || !state.CurrentAcIndex.HasValue)
                return PerformanceTuningResult.Fail("Не удалось сохранить текущее значение powercfg для бэкапа.");

            SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.PowerAcSetting),
                SubgroupAlias = item.PowerSubgroupAlias,
                SettingAlias = item.PowerSettingAlias,
                Value = state.CurrentAcIndex.Value.ToString(CultureInfo.InvariantCulture),
                Label = FormatPowerBackupLabel(item, state.CurrentAcIndex.Value),
                CreatedAtUtc = DateTime.UtcNow
            });

            long targetValue = ResolvePowerTargetValue(item);
            InvalidatePowerRuntimeCache();
            var setResult = RunPowerCfg(
                "/setacvalueindex",
                "SCHEME_CURRENT",
                item.PowerSubgroupAlias,
                item.PowerSettingAlias,
                targetValue.ToString(CultureInfo.InvariantCulture));

            if (!setResult.Success)
                return PerformanceTuningResult.Fail($"Windows не изменила параметр powercfg: {BuildCommandError(setResult)}");

            RunPowerCfg("/setactive", "SCHEME_CURRENT");
            return PerformanceTuningResult.Ok($"{item.Title}: применено значение {FormatAppliedValue(item, targetValue)}. Бэкап сохранён.");
        }

        private PerformanceTuningResult ApplyRegistryDword(PerformanceTuningItem item)
        {
            var current = ReadRegistryDword(item.RegistryHive, item.RegistryPath, item.RegistryValueName);
            SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.RegistryDword),
                RegistryHive = item.RegistryHiveName,
                RegistryPath = item.RegistryPath,
                RegistryValueName = item.RegistryValueName,
                RegistryValueExisted = current.Exists,
                RegistryDwordValue = current.Value,
                Label = current.Exists ? FormatDwordValue(current.Value) : "значение отсутствовало",
                CreatedAtUtc = DateTime.UtcNow
            });

            int targetValue = ResolveRegistryTargetValue(item);

            try
            {
                using var key = item.RegistryHive.CreateSubKey(item.RegistryPath, writable: true);
                if (key == null)
                    return PerformanceTuningResult.Fail("Не удалось открыть ветку реестра для записи.");

                if (item.RegistryDeleteWhenDisabled && item.IsToggle && !item.ToggleValue)
                    key.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
                else
                    key.SetValue(item.RegistryValueName, targetValue, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                return PerformanceTuningResult.Fail($"Не удалось записать реестр: {ex.Message}");
            }

            return PerformanceTuningResult.Ok(
                $"{item.Title}: применено. Бэкап сохранён.",
                item.RequiresRestart,
                item.RestartReason);
        }

        private PerformanceTuningResult ApplyMemoryCompression(PerformanceTuningItem item)
        {
            var read = RunPowerShell("try { [bool]((Get-MMAgent).MemoryCompression) } catch { Write-Error $_.Exception.Message; exit 1 }");
            if (!read.Success)
                return PerformanceTuningResult.Fail($"Не удалось сохранить состояние MMAgent: {BuildCommandError(read)}");

            bool oldValue = read.Output.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0;
            SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.MemoryCompression),
                Value = oldValue ? "true" : "false",
                Label = oldValue ? "включено" : "отключено",
                CreatedAtUtc = DateTime.UtcNow
            });

            string command = item.ToggleValue
                ? "Enable-MMAgent -MemoryCompression"
                : "Disable-MMAgent -MemoryCompression";

            var result = RunPowerShell(command);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"MMAgent не применил изменение: {BuildCommandError(result)}");

            return PerformanceTuningResult.Ok(
                "Сжатие памяти изменено. Перезагрузка рекомендуется.",
                requiresRestart: true,
                restartReason: item.RestartReason);
        }

        private PerformanceTuningResult RollbackPowerPlan(PerformanceSettingBackupRecord backup)
        {
            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/setactive", backup.Value);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось вернуть схему питания: {BuildCommandError(result)}");

            return PerformanceTuningResult.Ok($"Схема питания возвращена: «{backup.Label}».");
        }

        private PerformanceTuningResult RollbackPowerAcSetting(PerformanceSettingBackupRecord backup)
        {
            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/setacvalueindex", "SCHEME_CURRENT", backup.SubgroupAlias, backup.SettingAlias, backup.Value);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось вернуть powercfg: {BuildCommandError(result)}");

            RunPowerCfg("/setactive", "SCHEME_CURRENT");
            return PerformanceTuningResult.Ok($"Параметр возвращён к значению «{backup.Label}».");
        }

        private PerformanceTuningResult RollbackRegistryDword(PerformanceSettingBackupRecord backup)
        {
            var hive = GetRegistryHive(backup.RegistryHive);
            if (hive == null)
                return PerformanceTuningResult.Fail("Не удалось определить hive для отката.");

            try
            {
                using var key = hive.CreateSubKey(backup.RegistryPath, writable: true);
                if (key == null)
                    return PerformanceTuningResult.Fail("Не удалось открыть ветку реестра для отката.");

                if (backup.RegistryValueExisted)
                    key.SetValue(backup.RegistryValueName, backup.RegistryDwordValue, RegistryValueKind.DWord);
                else
                    key.DeleteValue(backup.RegistryValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                return PerformanceTuningResult.Fail($"Не удалось откатить реестр: {ex.Message}");
            }

            return PerformanceTuningResult.Ok("Параметр реестра возвращён из бэкапа. Если это системная настройка, перезагрузка может быть нужна.");
        }

        private PerformanceTuningResult RollbackMemoryCompression(PerformanceSettingBackupRecord backup)
        {
            bool enable = string.Equals(backup.Value, "true", StringComparison.OrdinalIgnoreCase);
            var result = RunPowerShell(enable ? "Enable-MMAgent -MemoryCompression" : "Disable-MMAgent -MemoryCompression");
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось откатить MMAgent: {BuildCommandError(result)}");

            return PerformanceTuningResult.Ok(
                $"Сжатие памяти возвращено: {backup.Label}.",
                requiresRestart: true,
                restartReason: "откат сжатия памяти Windows");
        }

        private long ResolvePowerTargetValue(PerformanceTuningItem item)
        {
            if (item.IsToggle)
                return item.ToggleValue ? item.EnabledValue : item.DisabledValue;

            if (item.IsCombo && item.SelectedOption != null)
                return ParseDwordValueAsLong(item.SelectedOption.Value);

            double scale = item.PowerValueScale <= 0 ? 1 : item.PowerValueScale;
            return (long)Math.Round(item.NumericValue * scale);
        }

        private int ResolveRegistryTargetValue(PerformanceTuningItem item)
        {
            if (item.IsToggle)
                return item.ToggleValue ? item.EnabledValue : item.DisabledValue;

            if (item.IsCombo && item.SelectedOption != null)
                return ParseDwordValue(item.SelectedOption.Value);

            return (int)Math.Round(item.NumericValue);
        }

        private bool IsPowerItemPriority(PerformanceTuningItem item, long current)
        {
            if (string.Equals(item.PowerSettingAlias, ProcessorMaxState, StringComparison.OrdinalIgnoreCase))
                return current == 100 && GetHottestTemperature("Cpu") >= 82;

            if (string.Equals(item.PowerSettingAlias, SystemCoolingPolicy, StringComparison.OrdinalIgnoreCase))
                return current == 0 && GetHottestTemperature("Cpu") >= 78;

            if (string.Equals(item.PowerSettingAlias, PciExpressAspm, StringComparison.OrdinalIgnoreCase))
                return current > 0;

            return false;
        }

        private void InvalidatePowerRuntimeCache()
        {
            lock (_runtimeCacheSync)
            {
                _powerSettingCache.Clear();
                _powerPlansCache = null;
                _activePowerPlanCache = null;
            }
        }

        private IReadOnlyList<PowerPlanInfo> GetPowerPlans()
        {
            lock (_runtimeCacheSync)
            {
                if (_powerPlansCache != null && DateTime.UtcNow - _powerPlansCacheUtc <= RuntimeCacheLifetime)
                    return _powerPlansCache;
            }

            var active = GetActivePowerPlan();
            var result = RunPowerCfg("/list");
            if (!result.Success)
                return Array.Empty<PowerPlanInfo>();

            var plans = new List<PowerPlanInfo>();
            foreach (string line in SplitLines(result.Output))
            {
                var guidMatch = GuidRegex.Match(line);
                if (!guidMatch.Success)
                    continue;

                string guid = guidMatch.Value;
                string name = ExtractNameFromParentheses(line);
                bool isActive = line.Contains("*") ||
                                string.Equals(active?.Guid, guid, StringComparison.OrdinalIgnoreCase);

                plans.Add(new PowerPlanInfo(guid, string.IsNullOrWhiteSpace(name) ? guid : name.Trim(), isActive));
            }

            lock (_runtimeCacheSync)
            {
                _powerPlansCache = plans;
                _powerPlansCacheUtc = DateTime.UtcNow;
            }

            return plans;
        }

        private PowerPlanInfo GetActivePowerPlan()
        {
            lock (_runtimeCacheSync)
            {
                if (_activePowerPlanCache != null && DateTime.UtcNow - _activePowerPlanCacheUtc <= RuntimeCacheLifetime)
                    return _activePowerPlanCache;
            }

            var result = RunPowerCfg("/getactivescheme");
            if (!result.Success)
                return null;

            var guidMatch = GuidRegex.Match(result.Output);
            if (!guidMatch.Success)
                return null;

            string name = ExtractNameFromParentheses(result.Output);
            var plan = new PowerPlanInfo(guidMatch.Value, string.IsNullOrWhiteSpace(name) ? guidMatch.Value : name.Trim(), true);

            lock (_runtimeCacheSync)
            {
                _activePowerPlanCache = plan;
                _activePowerPlanCacheUtc = DateTime.UtcNow;
            }

            return plan;
        }

        private PowerSettingQueryResult QueryPowerSetting(string subgroupAlias, string settingAlias)
        {
            string cacheKey = $"{subgroupAlias}|{settingAlias}";
            lock (_runtimeCacheSync)
            {
                if (_powerSettingCache.TryGetValue(cacheKey, out var cached) &&
                    DateTime.UtcNow - cached.CreatedAtUtc <= RuntimeCacheLifetime)
                {
                    return cached.Result.Clone();
                }
            }

            var result = RunPowerCfg("/query", "SCHEME_CURRENT", subgroupAlias, settingAlias);
            if (!result.Success || !ContainsSettingOutput(result.Output, settingAlias))
                result = RunPowerCfg("/qh", "SCHEME_CURRENT", subgroupAlias, settingAlias);

            if (!result.Success)
                return PowerSettingQueryResult.Fail(result.Error);

            if (!ContainsSettingOutput(result.Output, settingAlias))
                return PowerSettingQueryResult.Fail("Параметр не найден в текущей схеме.");

            TryReadPowerSettingCurrentAcIndex(result.Output, out long currentAc);
            TryReadPowerSettingBound(result.Output, true, out long minimum);
            TryReadPowerSettingBound(result.Output, false, out long maximum);

            var query = new PowerSettingQueryResult
            {
                Success = true,
                CurrentAcIndex = currentAc >= 0 ? currentAc : null,
                Minimum = minimum >= 0 ? minimum : null,
                Maximum = maximum >= 0 ? maximum : null
            };

            lock (_runtimeCacheSync)
                _powerSettingCache[cacheKey] = new PowerSettingCacheEntry(query, DateTime.UtcNow);

            return query.Clone();
        }

        private static bool ContainsSettingOutput(string output, string alias)
        {
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return output.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   output.IndexOf("GUID настройки питания", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   output.IndexOf("Power Setting GUID", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadPowerSettingCurrentAcIndex(string output, out long value)
        {
            value = -1;
            foreach (string line in SplitLines(output))
            {
                bool isAcLine = line.IndexOf("от сети", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                line.IndexOf("AC Power", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isAcLine)
                    continue;

                if (TryReadHexValue(line, out value))
                    return true;
            }

            return false;
        }

        private static bool TryReadPowerSettingBound(string output, bool minimum, out long value)
        {
            value = -1;
            foreach (string line in SplitLines(output))
            {
                bool matches = minimum
                    ? line.IndexOf("Минимальная", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      line.IndexOf("Minimum", StringComparison.OrdinalIgnoreCase) >= 0
                    : line.IndexOf("Максимальная", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      line.IndexOf("Maximum", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!matches)
                    continue;

                if (TryReadHexValue(line, out value))
                    return true;
            }

            return false;
        }

        private static bool TryReadHexValue(string line, out long value)
        {
            value = -1;
            var match = Regex.Match(line ?? string.Empty, @"0x([0-9a-fA-F]+)");
            if (!match.Success)
                return false;

            value = Convert.ToInt64(match.Groups[1].Value, 16);
            return true;
        }

        private static string ExtractNameFromParentheses(string value)
        {
            var match = Regex.Match(value ?? string.Empty, @"\((?<name>[^)]*)\)");
            return match.Success ? match.Groups["name"].Value : string.Empty;
        }

        private static CommandResult RunPowerCfg(params string[] arguments)
        {
            return RunProcess("powercfg.exe", arguments);
        }

        private static CommandResult RunPowerShell(string command)
        {
            return RunProcess(
                "powershell.exe",
                new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    command
                });
        }

        private static CommandResult RunProcess(string fileName, IEnumerable<string> arguments)
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

                foreach (string argument in arguments)
                    startInfo.ArgumentList.Add(argument);

                using var process = Process.Start(startInfo);
                if (process == null)
                    return CommandResult.Fail("Процесс не запустился.");

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(8000))
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

        private static string BuildCommandError(CommandResult result)
        {
            string details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            return string.IsNullOrWhiteSpace(details) ? $"код {result.ExitCode}" : details.Trim();
        }

        private static IReadOnlyList<string> SplitLines(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizeRisk(string risk)
        {
            return string.IsNullOrWhiteSpace(risk) ? "не указан" : risk;
        }

        private static string TranslateBatteryStatus(WinForms.BatteryChargeStatus status)
        {
            if (status.HasFlag(WinForms.BatteryChargeStatus.Charging))
                return "заряжается";

            if (status.HasFlag(WinForms.BatteryChargeStatus.High))
                return "высокий заряд";

            if (status.HasFlag(WinForms.BatteryChargeStatus.Low))
                return "низкий заряд";

            if (status.HasFlag(WinForms.BatteryChargeStatus.Critical))
                return "критический заряд";

            return "состояние неизвестно";
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1)
                return $"{(int)value.TotalHours} ч {value.Minutes} мин";

            return $"{Math.Max(0, value.Minutes)} мин";
        }

        private static string FormatPowerBackupLabel(PerformanceTuningItem item, long value)
        {
            if (item.PowerValueScale > 1)
                return $"{value / item.PowerValueScale:0}{item.ValueUnit}";

            if (item.IsCombo)
            {
                var option = item.Options.FirstOrDefault(candidate =>
                    ParseDwordValueAsLong(candidate.Value) == value);
                if (option != null)
                    return option.Label;
            }

            return $"{value}{item.ValueUnit}";
        }

        private static string FormatAppliedValue(PerformanceTuningItem item, long value)
        {
            if (item.IsCombo)
            {
                var option = item.Options.FirstOrDefault(candidate =>
                    ParseDwordValueAsLong(candidate.Value) == value);
                if (option != null)
                    return $"«{option.Label}»";
            }

            if (item.IsToggle)
                return value == item.EnabledValue ? item.EnabledText : item.DisabledText;

            if (item.PowerValueScale > 1)
                return $"{value / item.PowerValueScale:0}{item.ValueUnit}";

            return $"{value}{item.ValueUnit}";
        }

        private static int ParseDwordValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return unchecked((int)Convert.ToUInt32(raw[2..], 16));

            return int.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static long ParseDwordValueAsLong(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(raw[2..], 16);

            return long.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static string FormatDwordValue(int value)
        {
            return value == -1
                ? "0xFFFFFFFF"
                : value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private float GetHottestTemperature(string group)
        {
            try
            {
                return (_temperatureReader?.Invoke() ?? Array.Empty<TemperatureSensorReading>())
                    .Where(reading => string.Equals(reading.Group, group, StringComparison.OrdinalIgnoreCase))
                    .Select(reading => reading.ValueCelsius)
                    .DefaultIfEmpty(0)
                    .Max();
            }
            catch
            {
                return 0;
            }
        }

        private static MemoryStatusEx ReadMemoryStatus()
        {
            try
            {
                var memory = new MemoryStatusEx();
                return GlobalMemoryStatusEx(memory) ? memory : null;
            }
            catch
            {
                return null;
            }
        }

        private static RegistryDwordState ReadRegistryDword(RegistryKey hive, string path, string valueName)
        {
            try
            {
                using var key = hive?.OpenSubKey(path, writable: false);
                object value = key?.GetValue(valueName);
                return value is int intValue
                    ? new RegistryDwordState(true, intValue)
                    : new RegistryDwordState(false, 0);
            }
            catch
            {
                return new RegistryDwordState(false, 0);
            }
        }

        private static RegistryKey GetRegistryHive(string hive)
        {
            return hive?.ToUpperInvariant() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => null
            };
        }

        private PerformanceSettingBackupRecord GetBackup(string settingId)
        {
            return LoadBackups()
                .OrderByDescending(record => record.CreatedAtUtc)
                .FirstOrDefault(record => string.Equals(record.SettingId, settingId, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveBackup(PerformanceSettingBackupRecord record)
        {
            var backups = LoadBackups()
                .Where(item => !string.Equals(item.SettingId, record.SettingId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            backups.Add(record);
            SaveBackups(backups);
        }

        private void RemoveBackup(string settingId)
        {
            var backups = LoadBackups()
                .Where(item => !string.Equals(item.SettingId, settingId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveBackups(backups);
        }

        private List<PerformanceSettingBackupRecord> LoadBackups()
        {
            try
            {
                if (!File.Exists(_backupPath))
                    return new List<PerformanceSettingBackupRecord>();

                string json = File.ReadAllText(_backupPath);
                return JsonSerializer.Deserialize<List<PerformanceSettingBackupRecord>>(json) ?? new List<PerformanceSettingBackupRecord>();
            }
            catch
            {
                return new List<PerformanceSettingBackupRecord>();
            }
        }

        private void SaveBackups(IReadOnlyList<PerformanceSettingBackupRecord> backups)
        {
            try
            {
                string dir = Path.GetDirectoryName(_backupPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(backups, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_backupPath, json);
            }
            catch
            {
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        private sealed class CommandResult
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

        private sealed class PowerPlanInfo
        {
            public PowerPlanInfo(string guid, string name, bool isActive)
            {
                Guid = guid;
                Name = name;
                IsActive = isActive;
            }

            public string Guid { get; }
            public string Name { get; }
            public bool IsActive { get; }
        }

        private sealed class PowerSettingQueryResult
        {
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
            public long? CurrentAcIndex { get; set; }
            public long? Minimum { get; set; }
            public long? Maximum { get; set; }

            public PowerSettingQueryResult Clone()
            {
                return new PowerSettingQueryResult
                {
                    Success = Success,
                    Error = Error,
                    CurrentAcIndex = CurrentAcIndex,
                    Minimum = Minimum,
                    Maximum = Maximum
                };
            }

            public static PowerSettingQueryResult Fail(string error)
            {
                return new PowerSettingQueryResult
                {
                    Success = false,
                    Error = error ?? string.Empty
                };
            }
        }

        private sealed class PowerSettingCacheEntry
        {
            public PowerSettingCacheEntry(PowerSettingQueryResult result, DateTime createdAtUtc)
            {
                Result = result.Clone();
                CreatedAtUtc = createdAtUtc;
            }

            public PowerSettingQueryResult Result { get; }
            public DateTime CreatedAtUtc { get; }
        }

        private sealed class RegistryDwordState
        {
            public RegistryDwordState(bool exists, int value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public int Value { get; }
        }

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

    public sealed class PerformanceTuningItem : INotifyPropertyChanged
    {
        private PerformanceTuningOption _selectedOption;
        private bool _toggleValue;
        private double _numericValue;
        private string _currentValue = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _canApply;
        private bool _canRollback;
        private bool _isSupported = true;
        private bool _isPriority;

        public event PropertyChangedEventHandler PropertyChanged;

        public string SettingId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ChannelLabel { get; set; } = string.Empty;
        public string SectionTitle { get; set; } = string.Empty;
        public string SectionDescription { get; set; } = string.Empty;
        public string SearchKeywords { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string RiskLabel { get; set; } = string.Empty;
        public string ApplyButtonText { get; set; } = "Применить";
        public string SensorGroup { get; set; } = string.Empty;
        public string ReadOnlyKind { get; set; } = string.Empty;
        public string OperationKind { get; set; } = string.Empty;
        public string PowerSubgroupAlias { get; set; } = string.Empty;
        public string PowerSettingAlias { get; set; } = string.Empty;
        public double PowerValueScale { get; set; } = 1;
        public string ValueUnit { get; set; } = string.Empty;
        public RegistryKey RegistryHive { get; set; }
        public string RegistryHiveName { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string RegistryValueName { get; set; } = string.Empty;
        public int EnabledValue { get; set; } = 1;
        public int DisabledValue { get; set; } = 0;
        public int DefaultDwordValue { get; set; } = 0;
        public bool RegistryDeleteWhenDisabled { get; set; }
        public string EnabledText { get; set; } = "Включено";
        public string DisabledText { get; set; } = "Отключено";
        public string RestartReason { get; set; } = string.Empty;
        public int Order { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double NumericStep { get; set; } = 1;
        public bool RequiresElevation { get; set; }
        public bool RequiresRestart { get; set; }
        public bool ShowApplyAction { get; set; }
        public PerformanceSettingControlKind ControlKind { get; set; }
        public ObservableCollection<PerformanceTuningOption> Options { get; } = new ObservableCollection<PerformanceTuningOption>();

        public PerformanceTuningOption SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (_selectedOption == value)
                    return;

                _selectedOption = value;
                OnPropertyChanged(nameof(SelectedOption));
            }
        }

        public bool ToggleValue
        {
            get => _toggleValue;
            set
            {
                if (_toggleValue == value)
                    return;

                _toggleValue = value;
                OnPropertyChanged(nameof(ToggleValue));
            }
        }

        public double NumericValue
        {
            get => _numericValue;
            set
            {
                double rounded = Math.Round(value);
                if (Math.Abs(_numericValue - rounded) < 0.1)
                    return;

                _numericValue = rounded;
                OnPropertyChanged(nameof(NumericValue));
                OnPropertyChanged(nameof(NumericValueText));
            }
        }

        public string NumericValueText => string.IsNullOrWhiteSpace(ValueUnit)
            ? $"{NumericValue:0}"
            : $"{NumericValue:0}{ValueUnit}";

        public string CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue == value)
                    return;

                _currentValue = value ?? string.Empty;
                OnPropertyChanged(nameof(CurrentValue));
                OnPropertyChanged(nameof(HasCurrentValue));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value ?? string.Empty;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool CanApply
        {
            get => _canApply;
            set
            {
                if (_canApply == value)
                    return;

                _canApply = value;
                OnPropertyChanged(nameof(CanApply));
            }
        }

        public bool CanRollback
        {
            get => _canRollback;
            set
            {
                if (_canRollback == value)
                    return;

                _canRollback = value;
                OnPropertyChanged(nameof(CanRollback));
            }
        }

        public bool IsSupported
        {
            get => _isSupported;
            set
            {
                if (_isSupported == value)
                    return;

                _isSupported = value;
                OnPropertyChanged(nameof(IsSupported));
            }
        }

        public bool IsPriority
        {
            get => _isPriority;
            set
            {
                if (_isPriority == value)
                    return;

                _isPriority = value;
                OnPropertyChanged(nameof(IsPriority));
            }
        }

        public bool IsToggle => ControlKind == PerformanceSettingControlKind.Toggle;
        public bool IsCombo => ControlKind == PerformanceSettingControlKind.Combo;
        public bool IsSlider => ControlKind == PerformanceSettingControlKind.Slider;
        public bool HasCurrentValue => !string.IsNullOrWhiteSpace(CurrentValue);
        public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);
        public bool HasRisk => !string.IsNullOrWhiteSpace(RiskLabel);
        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

        public void SetStatus(string message, bool isWarning)
        {
            StatusMessage = message ?? string.Empty;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class PerformanceTuningOption
    {
        public PerformanceTuningOption(string label, string value, string hint = "")
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            Hint = hint ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }
        public string Hint { get; }

        public override string ToString()
        {
            return Label;
        }
    }

    public sealed class PerformanceTuningResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresRestart { get; set; }
        public string RestartReason { get; set; } = string.Empty;

        public static PerformanceTuningResult Ok(string message, bool requiresRestart = false, string restartReason = "")
        {
            return new PerformanceTuningResult
            {
                Success = true,
                Message = message,
                RequiresRestart = requiresRestart,
                RestartReason = restartReason
            };
        }

        public static PerformanceTuningResult Fail(string message)
        {
            return new PerformanceTuningResult
            {
                Success = false,
                Message = message
            };
        }
    }

    public sealed class PerformanceSettingBackupRecord
    {
        public string SettingId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SubgroupAlias { get; set; } = string.Empty;
        public string SettingAlias { get; set; } = string.Empty;
        public string RegistryHive { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string RegistryValueName { get; set; } = string.Empty;
        public bool RegistryValueExisted { get; set; }
        public int RegistryDwordValue { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public enum PerformanceSettingControlKind
    {
        ReadOnly,
        Toggle,
        Combo,
        Slider,
        Link
    }

    public enum PerformanceBackupKind
    {
        PowerScheme,
        PowerAcSetting,
        RegistryDword,
        MemoryCompression
    }
}
