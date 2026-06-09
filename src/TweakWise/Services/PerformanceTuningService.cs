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
using System.Text;
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
        private const int MaxBackupRecords = 80;

        private const string KindPowerScheme = "PowerScheme";
        private const string KindPowerAcSetting = "PowerAcSetting";
        private const string KindPowerDcSetting = "PowerDcSetting";
        private const string KindPowerHibernation = "PowerHibernation";
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
        private const string SubButtons = "SUB_BUTTONS";
        private const string SubBattery = "SUB_BATTERY";
        private const string SubWireless = "SUB_WIFI";
        private const string SubEnergySaver = "SUB_ENERGYSAVER";

        private const string SubPciExpressGuid = "501a4d13-42af-4429-9fd1-a8218c268e20";
        private const string SubVideoGuid = "7516b95f-f776-4464-8c53-06167f40cc99";
        private const string SubSleepGuid = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        private const string SubUsbGuid = "2a737441-1930-4402-8d77-b2bebba308a3";
        private const string SubButtonsGuid = "4f971e89-eebd-4455-a8de-9e59040e7347";
        private const string SubBatteryGuid = "e73a048d-bf27-4f12-9731-8b2076e8891f";
        private const string SubWirelessGuid = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
        private const string SubEnergySaverGuid = "de830923-a562-41af-a086-e3a2c6bad2da";

        private const string ProcessorMinState = "PROCTHROTTLEMIN";
        private const string ProcessorMaxState = "PROCTHROTTLEMAX";
        private const string ProcessorBoostMode = "PERFBOOSTMODE";
        private const string ProcessorEpp = "PERFEPP";
        private const string ProcessorBoostPolicy = "PERFBOOSTPOL";
        private const string ProcessorCoreParkingMin = "CPMINCORES";
        private const string ProcessorIdleDisable = "IDLEDISABLE";
        private const string SystemCoolingPolicy = "SYSCOOLPOL";
        private const string DiskIdle = "DISKIDLE";
        private const string PciExpressAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";
        private const string GpuPreferencePolicy = "GPUPREFERENCEPOLICY";
        private const string VideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
        private const string ConsoleLockDisplayOff = "8ec4b3a5-6868-48c2-be75-4f3044be88a7";
        private const string StandbyIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
        private const string HibernateIdle = "9d7815a6-7ee4-497e-8888-515a05f02364";
        private const string HybridSleep = "94ac6d29-73ce-41a6-809f-6363ba21b47e";
        private const string WakeTimers = "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d";
        private const string UnattendedSleepTimeout = "7bc4a2f9-d8fc-4469-b07b-33eb785aaca0";
        private const string AwayModePolicy = "25dfa149-5dd1-4736-b5ab-e8a37b5b8187";
        private const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        private const string WirelessPowerMode = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
        private const string LidCloseAction = "5ca83367-6e45-459f-a27b-476b1d01c936";
        private const string PowerButtonAction = "7648efa3-dd9c-4e3e-b566-50f929386280";
        private const string SleepButtonAction = "96996bc0-ad50-47ec-923b-6f41874dd9eb";
        private const string LowBatteryLevel = "8183ba9a-e910-48da-8769-14ae6dc1170a";
        private const string CriticalBatteryLevel = "9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469";
        private const string LowBatteryAction = "bcded951-187b-4d05-bccc-f7e51960c258";
        private const string CriticalBatteryAction = "637ea02f-bbcb-4015-8e2c-a1c7b9c0b546";

        private const string PowerControlPath = @"SYSTEM\CurrentControlSet\Control\Power";
        private const string PlatformAoAcOverrideValueName = "PlatformAoAcOverride";
        private const string PowerSessionManagerPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string HibernateEnabledValueName = "HibernateEnabled";
        private const string HiberbootEnabledValueName = "HiberbootEnabled";
        private const string PowerThrottlingPath = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
        private const string PowerThrottlingOffValueName = "PowerThrottlingOff";

        private const string GraphicsDriversPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string HardwareSchedulingValueName = "HwSchMode";

        private const string MultimediaSystemProfilePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string MemoryManagementPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string DwmPath = @"SOFTWARE\Microsoft\Windows\Dwm";

        private static readonly Regex GuidRegex = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        private static readonly HashSet<string> PowerNodeSubgroupGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SubVideoGuid,
            SubSleepGuid,
            SubUsbGuid,
            SubPciExpressGuid,
            SubButtonsGuid,
            SubBatteryGuid,
            SubWirelessGuid,
            SubEnergySaverGuid
        };

        private static readonly HashSet<string> CuratedPowerSettingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            VideoIdle,
            ConsoleLockDisplayOff,
            StandbyIdle,
            HibernateIdle,
            HybridSleep,
            WakeTimers,
            UnattendedSleepTimeout,
            AwayModePolicy,
            UsbSelectiveSuspend,
            PciExpressAspm,
            WirelessPowerMode,
            LidCloseAction,
            PowerButtonAction,
            SleepButtonAction,
            LowBatteryLevel,
            CriticalBatteryLevel,
            LowBatteryAction,
            CriticalBatteryAction
        };

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
        private IReadOnlyList<DiscoveredPowerSetting> _discoveredPowerSettingsCache;
        private DateTime _discoveredPowerSettingsCacheUtc;

        static PerformanceTuningService()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
            }
        }

        public PerformanceTuningService(
            SettingsManager settingsManager,
            Func<IReadOnlyList<TemperatureSensorReading>> temperatureReader)
        {
            _settingsManager = settingsManager;
            _temperatureReader = temperatureReader;
            _backupPath = GetBackupPath();
            PruneBackups();
        }

        public IReadOnlyList<PerformanceTuningItem> BuildItemsForNode(string nodeKey)
        {
            var items = new List<PerformanceTuningItem>();

            switch (nodeKey)
            {
                case "Power":
                    items.Add(CreatePowerPlanItem());
                    items.Add(CreatePowerSourceItem());
                    items.Add(CreateAcpiSleepStatesItem(16));
                    items.Add(CreatePowerRequestsItem(17));
                    items.Add(CreateWakeArmedDevicesItem(18));
                    items.Add(CreatePowerBatteryStatusItem(20));
                    items.Add(CreateHibernationMasterItem(22));
                    items.Add(CreateFastStartupItem(24));
                    items.Add(CreatePowerThrottlingItem(26));
                    items.Add(CreateModernStandbyOverrideItem(28));
                    items.Add(CreateDisplayIdleItem(30, dc: false));
                    items.Add(CreateDisplayIdleItem(31, dc: true));
                    items.Add(CreateConsoleLockDisplayIdleItem(40));
                    items.Add(CreateSleepIdleItem(50, dc: false));
                    items.Add(CreateSleepIdleItem(51, dc: true));
                    items.Add(CreateHibernateIdleItem(60, dc: false));
                    items.Add(CreateHibernateIdleItem(61, dc: true));
                    items.Add(CreateHybridSleepItem(70, dc: false));
                    items.Add(CreateHybridSleepItem(71, dc: true));
                    items.Add(CreateWakeTimersItem(80, dc: false));
                    items.Add(CreateWakeTimersItem(81, dc: true));
                    items.Add(CreateUnattendedSleepTimeoutItem(90, dc: false));
                    items.Add(CreateUnattendedSleepTimeoutItem(91, dc: true));
                    items.Add(CreateAwayModePolicyItem(100, dc: false));
                    items.Add(CreateLidCloseActionItem(110, dc: false));
                    items.Add(CreateLidCloseActionItem(111, dc: true));
                    items.Add(CreatePowerButtonActionItem(120, dc: false));
                    items.Add(CreatePowerButtonActionItem(121, dc: true));
                    items.Add(CreateSleepButtonActionItem(130, dc: false));
                    items.Add(CreateSleepButtonActionItem(131, dc: true));
                    items.Add(CreatePciExpressAspmItem(140, dc: false));
                    items.Add(CreatePciExpressAspmItem(141, dc: true));
                    items.Add(CreateUsbSelectiveSuspendItem(150, dc: false));
                    items.Add(CreateUsbSelectiveSuspendItem(151, dc: true));
                    items.Add(CreateWirelessPowerModeItem(160, dc: false));
                    items.Add(CreateWirelessPowerModeItem(161, dc: true));
                    items.Add(CreateLowBatteryLevelItem(170));
                    items.Add(CreateCriticalBatteryLevelItem(180));
                    items.Add(CreateLowBatteryActionItem(190));
                    items.Add(CreateCriticalBatteryActionItem(200));
                    AppendDiscoveredPowerSettings(items, 400);
                    break;

                case "Cpu":
                    items.Add(CreateCpuLoadItem(5));
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
                    items.Add(CreateGpuLoadItem(5));
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
                    items.Add(CreateCoolingSensorAvailabilityItem(12));
                    items.Add(CreateSystemCoolingPolicyItem(20));
                    items.Add(CreateProcessorMaxStateItem("Тепловой лимит CPU", 30, true));
                    items.Add(CreateProcessorEppItem(40, true));
                    items.Add(CreateProcessorIdleDisableItem(50));
                    break;
            }

            ApplySectionMetadata(nodeKey, items);
            foreach (var item in items)
                UpdateApplyState(item);

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

            if (item.SettingId.Contains("acpi", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("requests", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("wake-armed", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("modern-standby", StringComparison.OrdinalIgnoreCase))
            {
                ApplySection(item, "Платформа, ACPI и прошивка", "Реальные данные powercfg, ACPI/BIOS и системные политики, которые влияют на питание глубже обычных настроек Windows.");
                return;
            }

            if (item.SettingId.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                IsSubgroup(item, SubVideoGuid, SubVideo))
            {
                ApplySection(item, "Экран", "Таймауты дисплея и скрытые параметры экрана в текущей схеме питания.");
                return;
            }

            if (item.SettingId.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("hibernate", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("hiberboot", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("fast-startup", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("wake", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("away", StringComparison.OrdinalIgnoreCase) ||
                IsSubgroup(item, SubSleepGuid, SubSleep))
            {
                ApplySection(item, "Сон и пробуждение", "Переход в сон, гибернация, таймеры пробуждения и скрытые idle-таймауты Windows.");
                return;
            }

            if (item.SettingId.Contains("button", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("lid", StringComparison.OrdinalIgnoreCase) ||
                IsSubgroup(item, SubButtonsGuid, SubButtons))
            {
                ApplySection(item, "Кнопки и крышка", "Действия кнопки питания, кнопки сна и крышки ноутбука.");
                return;
            }

            if (item.SettingId.Contains("battery", StringComparison.OrdinalIgnoreCase) ||
                IsSubgroup(item, SubBatteryGuid, SubBattery))
            {
                ApplySection(item, "Батарея", "Пороговые уровни и действия Windows при низком или критическом заряде.");
                return;
            }

            if (item.SettingId.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("pcie", StringComparison.OrdinalIgnoreCase) ||
                item.SettingId.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
                IsSubgroup(item, SubUsbGuid, SubUsb) ||
                IsSubgroup(item, SubPciExpressGuid, SubPciExpress) ||
                IsSubgroup(item, SubWirelessGuid, SubWireless) ||
                IsSubgroup(item, SubEnergySaverGuid, SubEnergySaver))
            {
                ApplySection(item, "Питание устройств", "Энергосбережение USB, PCI Express, беспроводного адаптера и системного Energy Saver в текущей схеме.");
                return;
            }

            if (item.SettingId.Contains("power-throttling", StringComparison.OrdinalIgnoreCase))
            {
                ApplySection(item, "Скрытые политики питания", "Системные политики Windows, которые обычно меняют через реестр или гайды по производительности.");
                return;
            }

            ApplySection(item, "Скрытые параметры питания", "Автоматически найденные powercfg-параметры текущей схемы без CPU, GPU и дисковых настроек.");
        }

        private static bool IsSubgroup(PerformanceTuningItem item, string guid, string alias)
        {
            return string.Equals(item.PowerSubgroupAlias, guid, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.PowerSubgroupAlias, alias, StringComparison.OrdinalIgnoreCase);
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
                ? $"Проверка пройдена. Риск: {NormalizeRisk(item.RiskLabel)}. Точка отката будет создана только если значение действительно изменится."
                : "Проверка выполнена. Это диагностический блок без прямого изменения.";

            item.SetStatus(message, isWarning: false);
            return PerformanceTuningResult.Ok(message, item.RequiresRestart, item.RestartReason);
        }

        public PerformanceTuningResult Apply(PerformanceTuningItem item)
        {
            if (item == null)
                return PerformanceTuningResult.Fail("Не удалось определить параметр для применения.");

            var requestedValue = CaptureRequestedValue(item);
            RefreshItem(item);
            RestoreRequestedValue(item, requestedValue);

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
                KindPowerDcSetting => ApplyPowerAcSetting(item),
                KindPowerHibernation => ApplyPowerHibernation(item),
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
                nameof(PerformanceBackupKind.PowerDcSetting) => RollbackPowerAcSetting(backup),
                nameof(PerformanceBackupKind.PowerHibernation) => RollbackPowerHibernation(backup),
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

        public PerformanceTuningResult ClearBackup(PerformanceTuningItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.SettingId))
                return PerformanceTuningResult.Fail("Не удалось определить параметр для удаления бэкапа.");

            if (GetBackup(item.SettingId) == null)
            {
                item.CanRollback = false;
                item.SetStatus("Для этого параметра нет сохранённой точки отката.", isWarning: false);
                return PerformanceTuningResult.Ok("Для этого параметра нет сохранённой точки отката.");
            }

            RemoveBackup(item.SettingId);
            item.CanRollback = false;
            item.SetStatus("Точка отката удалена. Текущие настройки не изменялись.", isWarning: false);
            return PerformanceTuningResult.Ok("Точка отката удалена. Текущие настройки не изменялись.");
        }

        public static int DeleteAllBackups()
        {
            string path = GetBackupPath();
            int count = 0;

            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    count = JsonSerializer.Deserialize<List<PerformanceSettingBackupRecord>>(json)?.Count ?? 0;
                    File.Delete(path);
                }
            }
            catch
            {
            }

            return count;
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
                case KindPowerDcSetting:
                    FillPowerAcSettingState(item);
                    break;
                case KindPowerHibernation:
                    FillPowerHibernationState(item);
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
            UpdateApplyState(item);
        }

        private static void UpdateApplyState(PerformanceTuningItem item)
        {
            if (item == null)
                return;

            item.RequiresElevationWarning = item.RequiresElevation && !IsAdministrator();
            item.CanApply = item.ShowApplyAction &&
                            item.IsSupported &&
                            (!item.RequiresElevation || IsAdministrator());
        }

        private static RequestedPerformanceValue CaptureRequestedValue(PerformanceTuningItem item)
        {
            return new RequestedPerformanceValue
            {
                SelectedOptionValue = item.SelectedOption?.Value,
                SelectedOptionLabel = item.SelectedOption?.Label,
                ToggleValue = item.ToggleValue,
                NumericValue = item.NumericValue
            };
        }

        private static void RestoreRequestedValue(PerformanceTuningItem item, RequestedPerformanceValue requested)
        {
            if (item == null || requested == null)
                return;

            if (item.IsCombo && !string.IsNullOrWhiteSpace(requested.SelectedOptionValue))
            {
                var selected = item.Options.FirstOrDefault(option =>
                    string.Equals(option.Value, requested.SelectedOptionValue, StringComparison.OrdinalIgnoreCase));

                if (selected == null)
                {
                    selected = new PerformanceTuningOption(
                        string.IsNullOrWhiteSpace(requested.SelectedOptionLabel) ? requested.SelectedOptionValue : requested.SelectedOptionLabel,
                        requested.SelectedOptionValue,
                        "Выбранное значение");
                    item.Options.Add(selected);
                }

                item.SelectedOption = selected;
                return;
            }

            if (item.IsToggle)
            {
                item.ToggleValue = requested.ToggleValue;
                return;
            }

            if (item.IsSlider)
            {
                item.NumericValue = item.Maximum > item.Minimum
                    ? Math.Clamp(requested.NumericValue, item.Minimum, item.Maximum)
                    : requested.NumericValue;
            }
        }

        private static bool IsPowerSettingOperation(string operationKind)
        {
            return string.Equals(operationKind, KindPowerAcSetting, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(operationKind, KindPowerDcSetting, StringComparison.OrdinalIgnoreCase);
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

        private PerformanceTuningItem CreateAcpiSleepStatesItem(int order)
        {
            var item = CreateBaseItem(
                "power.acpi-sleep-states",
                "Режимы сна ACPI/BIOS",
                "Показывает реальные состояния сна, которые прошивка и ACPI-драйвер отдали Windows. Если S3, гибернация или Modern Standby недоступны, здесь будет причина от powercfg.",
                "powercfg /a",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "PowerSleepStates";
            FillPowerSleepStatesState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerRequestsItem(int order)
        {
            var item = CreateBaseItem(
                "power.active-requests",
                "Активные запросы питания",
                "Показывает процессы, драйверы или устройства, которые прямо сейчас запрещают сон, отключение экрана или idle-сценарии.",
                "powercfg /requests",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "PowerRequests";
            FillPowerRequestsState(item);
            return item;
        }

        private PerformanceTuningItem CreateWakeArmedDevicesItem(int order)
        {
            var item = CreateBaseItem(
                "power.wake-armed-devices",
                "Устройства с правом пробуждения",
                "Показывает устройства, которым Windows разрешила будить ПК. Это помогает найти причину самопроизвольных пробуждений без перехода в диспетчер устройств.",
                "powercfg wake_armed",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "PowerWakeArmed";
            FillPowerWakeArmedState(item);
            return item;
        }

        private PerformanceTuningItem CreateHibernationMasterItem(int order)
        {
            var item = CreateBaseItem(
                "power.hibernate-master",
                "Гибернация Windows",
                "Включает или отключает системную поддержку гибернации через powercfg. Нужна для гибернации, гибридного сна и некоторых сценариев быстрого запуска.",
                "powercfg",
                PerformanceSettingControlKind.Toggle,
                order);

            item.OperationKind = KindPowerHibernation;
            item.RequiresElevation = true;
            item.RiskLabel = "средний";
            item.EnabledText = "Гибернация включена";
            item.DisabledText = "Гибернация отключена";
            item.Recommendation = "Если в Windows пропали пункты гибернации или гибридного сна, включите этот параметр. Если нужен минимум занятого места на диске, отключите.";
            FillPowerHibernationState(item);
            return item;
        }

        private PerformanceTuningItem CreateModernStandbyOverrideItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "power.modern-standby-override",
                "Отключить Modern Standby (S0)",
                "Глубокий параметр ACPI/прошивки: PlatformAoAcOverride может запретить Modern Standby на системах, где производитель оставил совместимый путь. Не все BIOS/UEFI уважают этот ключ.",
                "HKLM",
                Registry.LocalMachine,
                PowerControlPath,
                PlatformAoAcOverrideValueName,
                order,
                enabledValue: 0,
                disabledValue: 1,
                defaultValue: 1,
                risk: "высокий");

            item.RegistryDeleteWhenDisabled = true;
            item.RequiresRestart = true;
            item.RestartReason = "изменение ACPI/Modern Standby применяется только после перезагрузки";
            item.EnabledText = "Modern Standby отключается override-ключом";
            item.DisabledText = "Используется поведение OEM/Windows";
            item.Recommendation = "Включайте только если знаете, что устройство некорректно работает с S0 Modern Standby. Если после перезагрузки сон пропал или стал нестабильным, выполните откат.";
            FillRegistryDwordState(item);
            return item;
        }

        private PerformanceTuningItem CreateFastStartupItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "power.fast-startup",
                "Быстрый запуск Windows",
                "Скрытый параметр гибернационного запуска: Windows сохраняет часть ядра при завершении работы. Иногда мешает драйверам и полному холодному старту.",
                "HKLM",
                Registry.LocalMachine,
                PowerSessionManagerPath,
                HiberbootEnabledValueName,
                order,
                enabledValue: 1,
                disabledValue: 0,
                defaultValue: 1,
                risk: "низкий");

            item.EnabledText = "Быстрый запуск включён";
            item.DisabledText = "Быстрый запуск отключён";
            item.RequiresRestart = true;
            item.RestartReason = "Изменение быстрого запуска применяется после следующего полного завершения работы или перезагрузки.";
            item.Recommendation = "Если после выключения не сбрасываются драйверы, USB, Wi-Fi или питание устройств, отключите быстрый запуск.";
            FillRegistryDwordState(item);
            return item;
        }

        private PerformanceTuningItem CreatePowerThrottlingItem(int order)
        {
            var item = CreateRegistryToggleItem(
                "power.power-throttling-off",
                "Отключить Power Throttling",
                "Скрытая системная политика Windows. Значение включает запрет энерготроттлинга фоновых задач через HKLM, без перехода в внешние настройки.",
                "HKLM",
                Registry.LocalMachine,
                PowerThrottlingPath,
                PowerThrottlingOffValueName,
                order,
                enabledValue: 1,
                disabledValue: 0,
                defaultValue: 0,
                risk: "средний");

            item.EnabledText = "Power Throttling отключён";
            item.DisabledText = "Power Throttling разрешён";
            item.RequiresRestart = true;
            item.RestartReason = "Политика Power Throttling применяется стабильнее после перезапуска Windows.";
            item.Recommendation = "Используйте для рабочих станций от сети, если фоновые задачи не должны замедляться. На ноутбуке это может повысить расход батареи.";
            FillRegistryDwordState(item);
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

        private PerformanceTuningItem CreateCpuLoadItem(int order)
        {
            var item = CreateBaseItem(
                "cpu.load",
                "Текущая загрузка CPU",
                "Считывает реальную суммарную загрузку процессора через системные счётчики Windows без изменения параметров.",
                "системный счётчик",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "CpuLoad";
            FillCpuLoadState(item);
            return item;
        }

        private PerformanceTuningItem CreateGpuLoadItem(int order)
        {
            var item = CreateBaseItem(
                "gpu.load",
                "Текущая загрузка GPU",
                "Считывает загрузку графических движков через Windows Performance Counters. Если драйвер не публикует счётчики, блок будет отмечен как недоступный.",
                "счётчики GPU",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "GpuLoad";
            FillGpuLoadState(item);
            return item;
        }

        private PerformanceTuningItem CreateCoolingSensorAvailabilityItem(int order)
        {
            var item = CreateBaseItem(
                "cooling.sensor-availability",
                "Доступность датчиков",
                "Проверяет, какие реальные температурные датчики доступны приложению. Недоступность датчика сама по себе не считается проблемой.",
                "датчики",
                PerformanceSettingControlKind.ReadOnly,
                order);

            item.OperationKind = KindReadOnly;
            item.ReadOnlyKind = "TemperatureAvailability";
            FillTemperatureAvailabilityState(item);
            return item;
        }

        private PerformanceTuningItem CreatePciExpressAspmItem(int order, bool dc)
        {
            var item = CreatePowerComboItem(
                dc ? "power.pcie-aspm-dc" : "power.pcie-aspm-ac",
                dc ? "PCI Express Link State от батареи" : "PCI Express Link State от сети",
                "Управляет энергосбережением PCIe в текущей схеме питания. Отключение может снизить задержки устройств, но повышает расход энергии.",
                PciExpressAspm,
                order,
                dc ? "средний" : "низкий",
                SubPciExpressGuid,
                dc);

            item.Options.Add(new PerformanceTuningOption("Отключено", "0", "Максимальная отзывчивость PCIe."));
            item.Options.Add(new PerformanceTuningOption("Умеренно", "1", "Баланс."));
            item.Options.Add(new PerformanceTuningOption("Максимальное энергосбережение", "2", "Меньше расход, возможны задержки."));
            item.Recommendation = dc
                ? "На батарее обычно оставляют энергосбережение. Если есть обрывы устройств, проверьте «Отключено»."
                : "Для производительного режима от сети чаще используют «Отключено».";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateDiskIdleItem(int order)
        {
            var item = CreateBaseItem(
                "power.disk-idle-ac",
                "Отключение диска от сети",
                "Задаёт таймаут отключения накопителя в текущей схеме питания. Этот пункт оставлен для совместимости, но в блоке питания больше не показывается.",
                "powercfg AC",
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
            item.Recommendation = "Этот параметр относится к накопителям и не выводится в узле питания.";
            FillPowerAcSettingState(item);
            return item;
        }

        private static void EnableTimeoutShortcut(PerformanceTuningItem item, double enabledValue, string enableText = "Включить типовое значение")
        {
            item.ShowSliderShortcuts = true;
            item.QuickDisableText = "Отключить / никогда";
            item.QuickEnableText = enableText;
            item.QuickEnableValue = enabledValue;
        }

        private PerformanceTuningItem CreateDisplayIdleItem(int order, bool dc)
        {
            var item = CreatePowerSliderItem(
                dc ? "power.display-idle-dc" : "power.display-idle-ac",
                dc ? "Отключение экрана от батареи" : "Отключение экрана от сети",
                "Задаёт таймаут выключения дисплея в текущей схеме питания. 0 минут означает «никогда».",
                VideoIdle,
                order,
                0,
                180,
                " мин",
                "низкий",
                SubVideoGuid,
                dc);

            item.PowerValueScale = 60;
            item.NumericStep = 5;
            EnableTimeoutShortcut(item, dc ? 5 : 15);
            item.Recommendation = dc
                ? "На батарее короткий таймаут экономит заряд. Кнопка отключения выставляет 0 минут — экран не будет гаснуть по таймауту."
                : "Для стационарной работы обычно удобно 10-30 минут. Кнопка отключения выставляет 0 минут — это режим «никогда».";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateConsoleLockDisplayIdleItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.display-lock-timeout-ac",
                "Экран блокировки: отключение дисплея",
                "Скрытый таймаут выключения дисплея на экране блокировки. В обычных параметрах Windows часто не отображается.",
                ConsoleLockDisplayOff,
                order,
                0,
                60,
                " мин",
                "низкий",
                SubVideoGuid,
                dc: false);

            item.PowerValueScale = 60;
            item.NumericStep = 1;
            EnableTimeoutShortcut(item, 5);
            item.Recommendation = "Если экран слишком быстро гаснет после Win+L, измените это значение. 0 минут отключает таймаут экрана блокировки.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateSleepIdleItem(int order, bool dc)
        {
            var item = CreatePowerSliderItem(
                dc ? "power.sleep-idle-dc" : "power.sleep-idle-ac",
                dc ? "Переход в сон от батареи" : "Переход в сон от сети",
                "Определяет, через сколько минут простоя Windows переведёт компьютер в сон. 0 минут означает «никогда».",
                StandbyIdle,
                order,
                0,
                240,
                " мин",
                dc ? "средний" : "низкий",
                SubSleepGuid,
                dc);

            item.PowerValueScale = 60;
            item.NumericStep = 5;
            EnableTimeoutShortcut(item, dc ? 15 : 30);
            item.Recommendation = dc
                ? "На батарее слишком большое значение быстрее разряжает ноутбук. 0 минут отключает автоматический сон."
                : "Для рабочих станций и долгих задач лучше не ставить слишком короткий таймаут. 0 минут отключает автоматический сон.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateHibernateIdleItem(int order, bool dc)
        {
            var item = CreatePowerSliderItem(
                dc ? "power.hibernate-idle-dc" : "power.hibernate-idle-ac",
                dc ? "Гибернация от батареи" : "Гибернация от сети",
                "Определяет таймаут перехода в гибернацию в текущей схеме питания. 0 минут означает «никогда».",
                HibernateIdle,
                order,
                0,
                360,
                " мин",
                "низкий",
                SubSleepGuid,
                dc);

            item.PowerValueScale = 60;
            item.NumericStep = 10;
            EnableTimeoutShortcut(item, dc ? 60 : 120);
            item.Recommendation = dc
                ? "На батарее гибернация защищает от полной разрядки при долгом простое. 0 минут отключает таймаут гибернации."
                : "Если компьютер выполняет долгие задачи без участия пользователя, используйте 0 или значение больше таймаута сна.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateHybridSleepItem(int order, bool dc)
        {
            var item = CreatePowerToggleItem(
                dc ? "power.hybrid-sleep-dc" : "power.hybrid-sleep-ac",
                dc ? "Гибридный сон от батареи" : "Гибридный сон от сети",
                "Сохраняет состояние в память и на диск перед сном. Это повышает устойчивость к потере питания, но может замедлить переход в сон.",
                HybridSleep,
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "низкий",
                subgroupAlias: SubSleepGuid,
                dc: dc);

            item.Recommendation = dc
                ? "На ноутбуках обычно достаточно обычного сна/гибернации, но поведение зависит от производителя."
                : "Для настольного ПК гибридный сон обычно полезен.";
            return item;
        }

        private PerformanceTuningItem CreateWakeTimersItem(int order, bool dc)
        {
            var item = CreatePowerComboItem(
                dc ? "power.wake-timers-dc" : "power.wake-timers-ac",
                dc ? "Таймеры пробуждения от батареи" : "Таймеры пробуждения от сети",
                "Разрешает задачам Windows будить компьютер по расписанию. Это реальный powercfg-параметр текущей схемы.",
                WakeTimers,
                order,
                "средний",
                SubSleepGuid,
                dc);

            item.Options.Add(new PerformanceTuningOption("Отключено", "0", "ПК не будет просыпаться по таймерам задач."));
            item.Options.Add(new PerformanceTuningOption("Включено", "1", "Windows и приложения могут будить ПК."));
            item.Options.Add(new PerformanceTuningOption("Только важные таймеры", "2", "Компромисс для системных задач."));
            item.Recommendation = dc
                ? "На батарее лучше отключать, чтобы ноутбук не просыпался в сумке."
                : "Если компьютер сам просыпается ночью, начните с отключения таймеров.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateUnattendedSleepTimeoutItem(int order, bool dc)
        {
            var item = CreatePowerSliderItem(
                dc ? "power.unattended-sleep-dc" : "power.unattended-sleep-ac",
                dc ? "Сон после автоматического пробуждения от батареи" : "Сон после автоматического пробуждения от сети",
                "Скрытый таймаут возврата в сон после автоматического пробуждения. Помогает, когда ПК просыпается для обслуживания и не засыпает обратно.",
                UnattendedSleepTimeout,
                order,
                0,
                120,
                " мин",
                "средний",
                SubSleepGuid,
                dc);

            item.PowerValueScale = 60;
            item.NumericStep = 1;
            EnableTimeoutShortcut(item, 5);
            item.Recommendation = "Обычно 2-10 минут достаточно. 0 отключает этот таймаут.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateAwayModePolicyItem(int order, bool dc)
        {
            var item = CreatePowerToggleItem(
                dc ? "power.away-mode-dc" : "power.away-mode-ac",
                dc ? "Away Mode от батареи" : "Away Mode от сети",
                "Скрытая политика: компьютер выглядит выключенным, но продолжает выполнять медиа- или фоновые задачи. Используйте только если понимаете сценарий.",
                AwayModePolicy,
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: "средний",
                subgroupAlias: SubSleepGuid,
                dc: dc);

            item.Recommendation = "На обычном ПК чаще оставляют выключенным, чтобы сон действительно снижал расход энергии.";
            return item;
        }

        private PerformanceTuningItem CreateUsbSelectiveSuspendItem(int order, bool dc)
        {
            var item = CreatePowerToggleItem(
                dc ? "power.usb-selective-suspend-dc" : "power.usb-selective-suspend-ac",
                dc ? "Выборочное приостановление USB от батареи" : "Выборочное приостановление USB от сети",
                "Позволяет Windows временно отключать неактивные USB-устройства. Отключение может помочь при обрывах USB-аудио, VR, геймпадов или внешних устройств.",
                UsbSelectiveSuspend,
                order,
                enabledValue: 1,
                disabledValue: 0,
                risk: dc ? "средний" : "низкий",
                subgroupAlias: SubUsbGuid,
                dc: dc);

            item.Recommendation = dc
                ? "На батарее лучше оставить включённым, если нет реальных обрывов устройств."
                : "Если USB-устройства работают стабильно, оставьте включённым. Отключайте только при обрывах или задержках.";
            return item;
        }

        private PerformanceTuningItem CreateWirelessPowerModeItem(int order, bool dc)
        {
            var item = CreatePowerComboItem(
                dc ? "power.wireless-mode-dc" : "power.wireless-mode-ac",
                dc ? "Беспроводной адаптер от батареи" : "Беспроводной адаптер от сети",
                "Режим энергосбережения Wi-Fi-адаптера в текущей схеме питания.",
                WirelessPowerMode,
                order,
                dc ? "средний" : "низкий",
                SubWirelessGuid,
                dc);

            item.Options.Add(new PerformanceTuningOption("Максимальная производительность", "0", "Минимум энергосбережения, стабильнее задержки."));
            item.Options.Add(new PerformanceTuningOption("Низкое энергосбережение", "1", "Небольшая экономия."));
            item.Options.Add(new PerformanceTuningOption("Среднее энергосбережение", "2", "Баланс."));
            item.Options.Add(new PerformanceTuningOption("Максимальное энергосбережение", "3", "Экономит заряд, может повысить задержки."));
            item.Recommendation = dc
                ? "На батарее используйте баланс или энергосбережение, если не важна минимальная задержка сети."
                : "От сети для стабильности обычно выбирают максимальную производительность.";
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateLidCloseActionItem(int order, bool dc)
        {
            var item = CreatePowerActionComboItem(
                dc ? "power.lid-action-dc" : "power.lid-action-ac",
                dc ? "Закрытие крышки от батареи" : "Закрытие крышки от сети",
                "Действие Windows при закрытии крышки ноутбука.",
                LidCloseAction,
                order,
                SubButtonsGuid,
                dc);

            item.Recommendation = dc
                ? "На батарее безопаснее сон или гибернация."
                : "Для ноутбука на док-станции можно выбрать «Не требуется действие», если охлаждение не перекрывается.";
            return item;
        }

        private PerformanceTuningItem CreatePowerButtonActionItem(int order, bool dc)
        {
            var item = CreatePowerActionComboItem(
                dc ? "power.power-button-action-dc" : "power.power-button-action-ac",
                dc ? "Действие кнопки питания от батареи" : "Действие кнопки питания от сети",
                "Действие Windows при нажатии аппаратной кнопки питания.",
                PowerButtonAction,
                order,
                SubButtonsGuid,
                dc);

            item.Recommendation = dc
                ? "На батарее обычно безопаснее сон или гибернация."
                : "Для защиты от случайного выключения часто выбирают сон или запрос штатного завершения.";
            return item;
        }

        private PerformanceTuningItem CreateSleepButtonActionItem(int order, bool dc)
        {
            var item = CreatePowerActionComboItem(
                dc ? "power.sleep-button-action-dc" : "power.sleep-button-action-ac",
                dc ? "Действие кнопки сна от батареи" : "Действие кнопки сна от сети",
                "Действие Windows при нажатии аппаратной кнопки сна, если она есть.",
                SleepButtonAction,
                order,
                SubButtonsGuid,
                dc);

            item.Recommendation = "Оставьте «Сон», если аппаратная кнопка реально используется для быстрого ухода в спящий режим.";
            return item;
        }

        private PerformanceTuningItem CreatePowerActionComboItem(
            string settingId,
            string title,
            string description,
            string settingAlias,
            int order,
            string subgroupAlias,
            bool dc)
        {
            var item = CreatePowerComboItem(settingId, title, description, settingAlias, order, "низкий", subgroupAlias, dc);
            item.Options.Add(new PerformanceTuningOption("Не требуется действие", "0", "Windows игнорирует событие."));
            item.Options.Add(new PerformanceTuningOption("Сон", "1", "Быстрое засыпание."));
            item.Options.Add(new PerformanceTuningOption("Гибернация", "2", "Сохраняет состояние на диск."));
            item.Options.Add(new PerformanceTuningOption("Завершение работы", "3", "Полное выключение."));
            FillPowerAcSettingState(item);
            return item;
        }

        private PerformanceTuningItem CreateLowBatteryLevelItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.battery-low-level-dc",
                "Низкий уровень батареи",
                "Порог заряда, при котором Windows показывает предупреждение о низком заряде.",
                LowBatteryLevel,
                order,
                1,
                100,
                "%",
                "низкий",
                SubBatteryGuid,
                dc: true);

            item.Recommendation = "Обычно 10-20% достаточно, чтобы успеть подключить питание.";
            return item;
        }

        private PerformanceTuningItem CreateCriticalBatteryLevelItem(int order)
        {
            var item = CreatePowerSliderItem(
                "power.battery-critical-level-dc",
                "Критический уровень батареи",
                "Порог заряда, при котором Windows выполняет критическое действие батареи.",
                CriticalBatteryLevel,
                order,
                1,
                100,
                "%",
                "средний",
                SubBatteryGuid,
                dc: true);

            item.Recommendation = "Не ставьте слишком низко: ноутбук может не успеть корректно уйти в гибернацию.";
            return item;
        }

        private PerformanceTuningItem CreateLowBatteryActionItem(int order)
        {
            var item = CreatePowerActionComboItem(
                "power.battery-low-action-dc",
                "Действие при низком заряде",
                "Что Windows делает при достижении низкого уровня батареи.",
                LowBatteryAction,
                order,
                SubBatteryGuid,
                dc: true);

            item.RiskLabel = "средний";
            item.Recommendation = "Для низкого уровня обычно достаточно уведомления/бездействия; критическое действие настраивается отдельно.";
            return item;
        }

        private PerformanceTuningItem CreateCriticalBatteryActionItem(int order)
        {
            var item = CreatePowerActionComboItem(
                "power.battery-critical-action-dc",
                "Действие при критическом заряде",
                "Что Windows делает при достижении критического уровня батареи.",
                CriticalBatteryAction,
                order,
                SubBatteryGuid,
                dc: true);

            item.RiskLabel = "высокий";
            item.Recommendation = "Обычно безопаснее гибернация. «Не требуется действие» может привести к потере данных при разрядке.";
            return item;
        }

        private void AppendDiscoveredPowerSettings(List<PerformanceTuningItem> items, int firstOrder)
        {
            var discovered = GetDiscoveredPowerSettings();
            int order = firstOrder;
            var existingKeys = new HashSet<string>(items
                .Where(item => !string.IsNullOrWhiteSpace(item.PowerSettingAlias))
                .Select(item => BuildPowerSettingItemKey(item.PowerSubgroupAlias, item.PowerSettingAlias, item.OperationKind)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var setting in discovered)
            {
                if (!PowerNodeSubgroupGuids.Contains(NormalizeGuid(setting.SubgroupGuid)))
                    continue;

                if (CuratedPowerSettingGuids.Contains(NormalizeGuid(setting.SettingGuid)))
                    continue;

                if (setting.CurrentAcIndex.HasValue)
                {
                    var item = CreateDiscoveredPowerSettingItem(setting, dc: false, order++);
                    string key = BuildPowerSettingItemKey(item.PowerSubgroupAlias, item.PowerSettingAlias, item.OperationKind);
                    if (existingKeys.Add(key))
                        items.Add(item);
                }

                if (setting.CurrentDcIndex.HasValue)
                {
                    var item = CreateDiscoveredPowerSettingItem(setting, dc: true, order++);
                    string key = BuildPowerSettingItemKey(item.PowerSubgroupAlias, item.PowerSettingAlias, item.OperationKind);
                    if (existingKeys.Add(key))
                        items.Add(item);
                }
            }
        }

        private PerformanceTuningItem CreateDiscoveredPowerSettingItem(DiscoveredPowerSetting setting, bool dc, int order)
        {
            var controlKind = ResolveDiscoveredControlKind(setting);
            string modeText = dc ? "от батареи" : "от сети";
            string title = $"{setting.DisplaySettingName} {modeText}";

            var item = CreateBaseItem(
                $"power.dynamic.{NormalizeGuid(setting.SettingGuid)}.{(dc ? "dc" : "ac")}",
                title,
                $"Автоматически найденный powercfg-параметр текущей схемы: {setting.DisplaySubgroupName}. GUID настройки: {setting.SettingGuid}.",
                dc ? "powercfg DC" : "powercfg AC",
                controlKind,
                order);

            item.OperationKind = dc ? KindPowerDcSetting : KindPowerAcSetting;
            item.PowerSubgroupAlias = setting.SubgroupGuid;
            item.PowerSettingAlias = setting.SettingGuid;
            item.SearchKeywords = $"{setting.SubgroupGuid} {setting.SettingGuid} {setting.SubgroupName} {setting.SettingName}";
            item.RiskLabel = "средний";
            item.Recommendation = "Это скрытый или дополнительный параметр Windows из текущей схемы питания. Перед изменением TweakWise сохраняет бэкап для отката.";

            if (controlKind == PerformanceSettingControlKind.Combo)
            {
                foreach (var option in setting.Options)
                    item.Options.Add(new PerformanceTuningOption(option.Label, option.Value, option.Hint));
            }
            else if (controlKind == PerformanceSettingControlKind.Toggle)
            {
                item.EnabledValue = 1;
                item.DisabledValue = 0;
                item.EnabledText = "Включено";
                item.DisabledText = "Отключено";
            }
            else
            {
                item.Minimum = setting.Minimum ?? 0;
                item.Maximum = setting.Maximum.HasValue && setting.Maximum.Value > item.Minimum
                    ? setting.Maximum.Value
                    : Math.Max(item.Minimum + 1, 100);
                item.NumericStep = setting.Increment.HasValue && setting.Increment.Value > 0
                    ? setting.Increment.Value
                    : 1;
                item.ValueUnit = string.Empty;
            }

            FillPowerAcSettingState(item);
            return item;
        }

        private static PerformanceSettingControlKind ResolveDiscoveredControlKind(DiscoveredPowerSetting setting)
        {
            if (setting.Options.Count > 0)
                return PerformanceSettingControlKind.Combo;

            if (setting.Minimum == 0 && setting.Maximum == 1)
                return PerformanceSettingControlKind.Toggle;

            return PerformanceSettingControlKind.Slider;
        }

        private static string BuildPowerSettingItemKey(string subgroup, string setting, string kind)
        {
            return $"{NormalizeGuid(subgroup)}|{NormalizeGuid(setting)}|{kind}";
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
            string subgroupAlias = SubProcessor,
            bool dc = false)
        {
            var item = CreateBaseItem(settingId, title, description, dc ? "powercfg DC" : "powercfg AC", PerformanceSettingControlKind.Slider, order);
            item.OperationKind = dc ? KindPowerDcSetting : KindPowerAcSetting;
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
            string subgroupAlias = SubProcessor,
            bool dc = false)
        {
            var item = CreateBaseItem(settingId, title, description, dc ? "powercfg DC" : "powercfg AC", PerformanceSettingControlKind.Combo, order);
            item.OperationKind = dc ? KindPowerDcSetting : KindPowerAcSetting;
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
            string subgroupAlias = SubProcessor,
            bool dc = false)
        {
            var item = CreateBaseItem(settingId, title, description, dc ? "powercfg DC" : "powercfg AC", PerformanceSettingControlKind.Toggle, order);
            item.OperationKind = dc ? KindPowerDcSetting : KindPowerAcSetting;
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

            if (IsPowerSettingOperation(item.OperationKind))
            {
                bool useDc = string.Equals(item.OperationKind, KindPowerDcSetting, StringComparison.OrdinalIgnoreCase);
                long targetValue = ResolvePowerTargetValue(item);

                if (string.Equals(item.PowerSettingAlias, ProcessorMinState, StringComparison.OrdinalIgnoreCase))
                {
                    var max = QueryPowerSetting(SubProcessor, ProcessorMaxState);
                    long? maxIndex = useDc ? max.CurrentDcIndex : max.CurrentAcIndex;
                    if (max.Success && maxIndex.HasValue && targetValue > maxIndex.Value)
                        return PerformanceTuningResult.Fail("Минимальное состояние CPU не может быть выше текущего максимального состояния CPU.");
                }

                if (string.Equals(item.PowerSettingAlias, ProcessorMaxState, StringComparison.OrdinalIgnoreCase))
                {
                    var min = QueryPowerSetting(SubProcessor, ProcessorMinState);
                    long? minIndex = useDc ? min.CurrentDcIndex : min.CurrentAcIndex;
                    if (min.Success && minIndex.HasValue && targetValue < minIndex.Value)
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
                case "PowerSleepStates":
                    FillPowerSleepStatesState(item);
                    break;
                case "PowerRequests":
                    FillPowerRequestsState(item);
                    break;
                case "PowerWakeArmed":
                    FillPowerWakeArmedState(item);
                    break;
                case "MemoryLoad":
                    FillMemoryLoadState(item);
                    break;
                case "Temperature":
                    FillTemperatureState(item);
                    break;
                case "CpuLoad":
                    FillCpuLoadState(item);
                    break;
                case "GpuLoad":
                    FillGpuLoadState(item);
                    break;
                case "TemperatureAvailability":
                    FillTemperatureAvailabilityState(item);
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
                item.SignalLevel = item.IsPriority ? HealthLevel.Normal : HealthLevel.Good;
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
                item.SignalLevel = onBattery ? HealthLevel.Normal : HealthLevel.Good;
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
                item.SignalLevel = item.IsPriority ? HealthLevel.Normal : HealthLevel.Good;
            }
            catch
            {
                item.CurrentValue = "Windows не предоставила данные батареи";
                item.Recommendation = "Это возможно на настольных ПК, виртуальных машинах или устройствах с ограниченным ACPI-драйвером.";
            }
        }

        private void FillPowerSleepStatesState(PerformanceTuningItem item)
        {
            var result = RunPowerCfg("/a");
            if (!result.Success)
            {
                item.CurrentValue = "Не удалось прочитать состояния сна";
                item.SetStatus($"powercfg /a завершился с ошибкой: {BuildCommandError(result)}", isWarning: true);
                return;
            }

            string summary = SummarizePowerCfgOutput(result.Output, 6);
            item.CurrentValue = string.IsNullOrWhiteSpace(summary)
                ? "Windows не вернула список состояний сна"
                : summary;
            item.Recommendation = "Если нужный режим сна отсутствует, причина обычно в BIOS/UEFI, ACPI-драйвере, драйвере видеокарты или политике Modern Standby. Универсально включить отсутствующий BIOS-режим из Windows нельзя, но соседние параметры помогут проверить гибернацию и S0 override.";
            item.IsSupported = true;
        }

        private void FillPowerRequestsState(PerformanceTuningItem item)
        {
            var result = RunPowerCfg("/requests");
            if (!result.Success)
            {
                item.CurrentValue = "Не удалось прочитать активные запросы питания";
                item.SetStatus($"powercfg /requests завершился с ошибкой: {BuildCommandError(result)}", isWarning: true);
                return;
            }

            string summary = SummarizePowerCfgOutput(result.Output, 8);
            bool clean = IsEmptyPowerCfgDiagnostic(result.Output);
            item.CurrentValue = clean ? "Активных блокирующих запросов не обнаружено" : summary;
            item.Recommendation = clean
                ? "Сейчас процессы и драйверы не блокируют сон или отключение экрана через powercfg requests."
                : "Если сон или экран не выключаются, проверьте указанные процессы/драйверы и уберите причину, а не отключайте сон глобально.";
            item.IsPriority = !clean;
            item.SignalLevel = clean ? HealthLevel.Good : HealthLevel.Warning;
            item.IsSupported = true;
        }

        private void FillPowerWakeArmedState(PerformanceTuningItem item)
        {
            var result = RunPowerCfg("/devicequery", "wake_armed");
            if (!result.Success)
            {
                item.CurrentValue = "Не удалось прочитать устройства пробуждения";
                item.SetStatus($"powercfg /devicequery wake_armed завершился с ошибкой: {BuildCommandError(result)}", isWarning: true);
                return;
            }

            string summary = SummarizePowerCfgOutput(result.Output, 8);
            item.CurrentValue = string.IsNullOrWhiteSpace(summary)
                ? "Устройства с правом пробуждения не найдены"
                : summary;
            item.Recommendation = string.IsNullOrWhiteSpace(summary)
                ? "Самопроизвольные пробуждения, если они есть, вероятнее вызваны таймерами, драйверами или BIOS, а не устройствами с wake-правом."
                : "Если ПК сам просыпается, начните с этих устройств. Для универсального безопасного применения отключение wake-прав лучше делать точечно, когда будет отдельный список устройств.";
            item.IsPriority = !string.IsNullOrWhiteSpace(summary);
            item.SignalLevel = item.IsPriority ? HealthLevel.Normal : HealthLevel.Good;
            item.IsSupported = true;
        }

        private void FillPowerAcSettingState(PerformanceTuningItem item)
        {
            bool useDc = string.Equals(item.OperationKind, KindPowerDcSetting, StringComparison.OrdinalIgnoreCase);
            var state = QueryPowerSetting(item.PowerSubgroupAlias, item.PowerSettingAlias);
            long? currentIndex = useDc ? state.CurrentDcIndex : state.CurrentAcIndex;
            string modeText = useDc ? "от батареи" : "от сети";

            if (!state.Success || !currentIndex.HasValue)
            {
                item.IsSupported = false;
                item.CurrentValue = $"Параметр {modeText} недоступен";
                string details = string.IsNullOrWhiteSpace(state.Error) ? string.Empty : $" Подробности powercfg: {state.Error.Trim()}";
                item.IsPriority = false;
                item.SignalLevel = HealthLevel.Good;
                item.Recommendation = $"Этот параметр не считается проблемой: на части ПК производитель, ACPI/BIOS или текущая схема питания не отдают его Windows. Возможные причины: режим не поддерживается устройством, скрыт OEM-драйвером или отсутствует в активной схеме.{details}";
                item.SetStatus("Недоступно на этой конфигурации. Можно попробовать другую схему питания, запуск от администратора, обновление драйверов чипсета/питания или восстановление схем командой powercfg -restoredefaultschemes.", isWarning: false);
                return;
            }

            item.IsSupported = true;
            long current = currentIndex.Value;

            if (item.IsSlider)
            {
                double scale = item.PowerValueScale <= 0 ? 1 : item.PowerValueScale;
                double displayValue = current / scale;

                if (state.Minimum.HasValue)
                    item.Minimum = Math.Max(item.Minimum, state.Minimum.Value / scale);

                if (state.Maximum.HasValue && item.Maximum > 0)
                    item.Maximum = Math.Min(item.Maximum, state.Maximum.Value / scale);

                if (item.Maximum <= item.Minimum)
                    item.Maximum = item.Minimum + Math.Max(1, item.NumericStep);

                item.NumericValue = Math.Clamp(displayValue, item.Minimum, item.Maximum);
                item.CurrentValue = $"{modeText}: {displayValue:0}{item.ValueUnit}";
            }
            else if (item.IsToggle)
            {
                item.ToggleValue = current == item.EnabledValue;
                item.CurrentValue = $"{modeText}: {(item.ToggleValue ? item.EnabledText : item.DisabledText)}";
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

                item.CurrentValue = $"{modeText}: {item.SelectedOption.Label}";
            }

            item.StatusMessage = string.Empty;
            item.IsPriority = IsPowerItemPriority(item, current);
        }

        private void FillPowerHibernationState(PerformanceTuningItem item)
        {
            var state = ReadRegistryDword(Registry.LocalMachine, PowerControlPath, HibernateEnabledValueName);
            bool enabled = state.Exists && state.Value != 0;

            item.IsSupported = true;
            item.ToggleValue = enabled;
            item.CurrentValue = enabled ? item.EnabledText : item.DisabledText;
            item.StatusMessage = state.Exists
                ? string.Empty
                : "Windows не вернула значение HibernateEnabled в реестре. Применение всё равно будет выполнено через powercfg /hibernate.";
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
                item.ToggleValue = current == item.EnabledValue;
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

            if (memory.dwMemoryLoad >= 92)
            {
                item.Recommendation = "ОЗУ почти заполнена. Это уже критичный уровень для тяжёлых задач: закройте лишние процессы и проверьте утечки памяти.";
                item.IsPriority = true;
                item.SignalLevel = HealthLevel.Critical;
            }
            else if (memory.dwMemoryLoad >= 78)
            {
                item.Recommendation = "Память сильно загружена. Для игр, рендера и виртуальных машин это проблема: освободите RAM или проверьте тяжёлые процессы.";
                item.IsPriority = true;
                item.SignalLevel = HealthLevel.Warning;
            }
            else if (memory.dwMemoryLoad >= 70)
            {
                item.Recommendation = "Запас памяти небольшой. Перед тяжёлыми задачами лучше закрыть лишние приложения.";
                item.IsPriority = true;
                item.SignalLevel = HealthLevel.Normal;
            }
            else
            {
                item.Recommendation = "Запас памяти выглядит нормальным.";
                item.IsPriority = false;
                item.SignalLevel = HealthLevel.Good;
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
            if (hottest.ValueCelsius >= 92)
                item.SignalLevel = HealthLevel.Critical;
            else if (hottest.ValueCelsius >= 82)
                item.SignalLevel = HealthLevel.Warning;
            else if (hottest.ValueCelsius >= 75)
                item.SignalLevel = HealthLevel.Normal;
            else
                item.SignalLevel = HealthLevel.Good;

            item.IsPriority = item.SignalLevel != HealthLevel.Good;
            item.Recommendation = hottest.ValueCelsius >= 90
                ? "Температура высокая: проверьте пыль, вентиляцию корпуса и режим питания."
                : hottest.ValueCelsius >= 82
                    ? "Температура близка к зоне ограничений. Снизьте boost/EPP или включите активную политику охлаждения."
                    : hottest.ValueCelsius >= 75
                        ? "Температура повышена, но ещё не критична. Перед длительной нагрузкой проверьте вентиляцию и профиль питания."
                        : "Температурный запас выглядит нормальным.";
        }

        private void FillTemperatureAvailabilityState(PerformanceTuningItem item)
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

            if (readings.Count == 0)
            {
                item.IsSupported = false;
                item.CurrentValue = "Датчики не найдены";
                item.Recommendation = "Это не ошибка Windows. Возможные причины: производитель закрыл датчики, драйвер ACPI/чипсета не отдаёт данные, приложение запущено без доступа к аппаратному мониторингу или включена переменная TW_SUPPRESS_HARDWARE_MONITORING.";
                item.SignalLevel = HealthLevel.Good;
                item.IsPriority = false;
                item.SetStatus("Недоступно на этой конфигурации или в текущем режиме запуска. Проверьте права администратора, драйверы чипсета/видеокарты и отключите TW_SUPPRESS_HARDWARE_MONITORING, если она задана.", isWarning: false);
                return;
            }

            var groups = readings
                .GroupBy(reading => reading.Group)
                .Select(group => $"{TranslateSensorGroup(group.Key)}: {group.Count()}")
                .ToList();

            item.IsSupported = true;
            item.CurrentValue = string.Join(" · ", groups);
            item.Recommendation = "Датчики доступны. Для оценки охлаждения TweakWise использует самые горячие значения CPU, GPU, платы и других опубликованных контроллеров.";
            item.SignalLevel = HealthLevel.Good;
            item.IsPriority = false;
            item.StatusMessage = string.Empty;
        }

        private void FillCpuLoadState(PerformanceTuningItem item)
        {
            var usage = TryReadCpuUsagePercent();
            if (!usage.HasValue)
            {
                item.IsSupported = false;
                item.CurrentValue = "Не удалось прочитать загрузку CPU";
                item.Recommendation = "Windows не вернула системное время процессора. Это возможно при ограниченных правах или в некоторых виртуальных средах.";
                item.SetStatus("Диагностический счётчик недоступен. Это не считается проблемой производительности.", isWarning: false);
                return;
            }

            item.IsSupported = true;
            item.CurrentValue = $"{usage.Value:0}%";
            item.SignalLevel = usage.Value >= 95 ? HealthLevel.Normal : HealthLevel.Good;
            item.IsPriority = item.SignalLevel != HealthLevel.Good;
            item.Recommendation = usage.Value >= 95
                ? "CPU сейчас занят почти полностью. Если это происходит без тяжёлой задачи, проверьте процессы в диспетчере задач и фоновые службы."
                : "Текущая загрузка CPU не выглядит проблемной.";
            item.StatusMessage = string.Empty;
        }

        private void FillGpuLoadState(PerformanceTuningItem item)
        {
            var load = TryReadGpuUsagePercent();
            if (!load.HasValue)
            {
                item.IsSupported = false;
                item.CurrentValue = "Счётчики GPU недоступны";
                item.Recommendation = "Windows публикует GPU Performance Counters не на всех драйверах и не во всех виртуальных средах. Это не проблема, если игры и графические приложения работают штатно.";
                item.SetStatus("Недоступно на этой конфигурации: драйвер GPU не отдал счётчики Utilization Percentage.", isWarning: false);
                return;
            }

            item.IsSupported = true;
            item.CurrentValue = $"{load.Value:0}%";
            item.SignalLevel = load.Value >= 95 ? HealthLevel.Normal : HealthLevel.Good;
            item.IsPriority = item.SignalLevel != HealthLevel.Good;
            item.Recommendation = load.Value >= 95
                ? "GPU сейчас почти полностью загружен. Если тяжёлой графической задачи нет, проверьте фоновые приложения, запись экрана и браузерное аппаратное ускорение."
                : "Текущая загрузка GPU не выглядит проблемной.";
            item.StatusMessage = string.Empty;
        }

        private PerformanceTuningResult ApplyPowerPlan(PerformanceTuningItem item)
        {
            if (item.SelectedOption == null || string.IsNullOrWhiteSpace(item.SelectedOption.Value))
                return PerformanceTuningResult.Fail("Выберите схему питания.");

            string targetGuid = item.SelectedOption.Value.Trim();
            var active = GetActivePowerPlan();
            if (active == null)
                return PerformanceTuningResult.Fail("Не удалось прочитать текущую схему питания через powercfg.");

            if (string.Equals(active.Guid, targetGuid, StringComparison.OrdinalIgnoreCase))
                return PerformanceTuningResult.Ok($"Схема «{active.Name}» уже активна. Изменения не требуются.");

            bool backupSaved = SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.PowerScheme),
                Value = active.Guid,
                Label = active.Name,
                CreatedAtUtc = DateTime.UtcNow
            }, targetGuid);

            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/setactive", targetGuid);
            if (!result.Success)
            {
                if (backupSaved)
                    RemoveBackup(item.SettingId);

                return PerformanceTuningResult.Fail($"Windows не применила схему питания: {BuildCommandError(result)}");
            }

            InvalidatePowerRuntimeCache();
            var applied = GetActivePowerPlan();
            if (applied == null || !string.Equals(applied.Guid, targetGuid, StringComparison.OrdinalIgnoreCase))
            {
                string actual = applied == null ? "не удалось прочитать активную схему" : $"активна «{applied.Name}»";
                return PerformanceTuningResult.Fail($"Команда выполнена, но Windows не подтвердила смену схемы: {actual}.");
            }

            string backupText = backupSaved ? " Точка отката сохранена." : " Новая точка отката не создавалась.";
            return PerformanceTuningResult.Ok($"Активная схема питания: «{applied.Name}».{backupText}");
        }

        private PerformanceTuningResult ApplyPowerAcSetting(PerformanceTuningItem item)
        {
            bool useDc = string.Equals(item.OperationKind, KindPowerDcSetting, StringComparison.OrdinalIgnoreCase);
            var state = QueryPowerSetting(item.PowerSubgroupAlias, item.PowerSettingAlias);
            long? currentIndex = useDc ? state.CurrentDcIndex : state.CurrentAcIndex;
            if (!state.Success || !currentIndex.HasValue)
                return PerformanceTuningResult.Fail("Не удалось прочитать текущее значение powercfg перед применением.");

            long targetValue = ResolvePowerTargetValue(item);
            if (currentIndex.Value == targetValue)
            {
                string alreadyModeText = useDc ? "от батареи" : "от сети";
                return PerformanceTuningResult.Ok($"{item.Title}: значение {FormatAppliedValue(item, targetValue)} уже установлено ({alreadyModeText}).");
            }

            bool backupSaved = SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = useDc ? nameof(PerformanceBackupKind.PowerDcSetting) : nameof(PerformanceBackupKind.PowerAcSetting),
                SubgroupAlias = item.PowerSubgroupAlias,
                SettingAlias = item.PowerSettingAlias,
                Value = currentIndex.Value.ToString(CultureInfo.InvariantCulture),
                Label = FormatPowerBackupLabel(item, currentIndex.Value),
                CreatedAtUtc = DateTime.UtcNow
            }, targetValue.ToString(CultureInfo.InvariantCulture));

            string scheme = GetCurrentPowerSchemeArgument();
            InvalidatePowerRuntimeCache();
            var setResult = RunPowerCfg(
                useDc ? "/setdcvalueindex" : "/setacvalueindex",
                scheme,
                item.PowerSubgroupAlias,
                item.PowerSettingAlias,
                targetValue.ToString(CultureInfo.InvariantCulture));

            if (!setResult.Success)
            {
                if (backupSaved)
                    RemoveBackup(item.SettingId);

                return PerformanceTuningResult.Fail($"Windows не изменила параметр powercfg: {BuildCommandError(setResult)}");
            }

            var commitResult = RunPowerCfg("/setactive", scheme);
            if (!commitResult.Success)
                return PerformanceTuningResult.Fail($"Значение записано, но Windows не активировала обновлённую схему: {BuildCommandError(commitResult)}");

            InvalidatePowerRuntimeCache();
            var updated = QueryPowerSetting(item.PowerSubgroupAlias, item.PowerSettingAlias);
            long? appliedIndex = useDc ? updated.CurrentDcIndex : updated.CurrentAcIndex;
            if (!updated.Success || !appliedIndex.HasValue || appliedIndex.Value != targetValue)
            {
                string actual = appliedIndex.HasValue ? FormatAppliedValue(item, appliedIndex.Value) : "не прочитано";
                return PerformanceTuningResult.Fail($"Команда выполнена, но повторная проверка не подтвердила значение {FormatAppliedValue(item, targetValue)}. Сейчас: {actual}.");
            }

            string modeText = useDc ? "от батареи" : "от сети";
            string backupText = backupSaved ? " Точка отката сохранена." : " Новая точка отката не создавалась.";
            return PerformanceTuningResult.Ok($"{item.Title}: применено значение {FormatAppliedValue(item, targetValue)} ({modeText}).{backupText}");
        }

        private PerformanceTuningResult ApplyPowerHibernation(PerformanceTuningItem item)
        {
            var state = ReadRegistryDword(Registry.LocalMachine, PowerControlPath, HibernateEnabledValueName);
            int previousValue = state.Exists ? state.Value : 0;
            bool targetEnabled = item.ToggleValue;

            if ((previousValue != 0) == targetEnabled)
                return PerformanceTuningResult.Ok($"{item.Title}: значение уже установлено. Изменения не требуются.");

            bool backupSaved = SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.PowerHibernation),
                Value = previousValue.ToString(CultureInfo.InvariantCulture),
                Label = previousValue != 0 ? item.EnabledText : item.DisabledText,
                CreatedAtUtc = DateTime.UtcNow
            }, targetEnabled ? "1" : "0");

            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/hibernate", targetEnabled ? "on" : "off");
            if (!result.Success)
            {
                if (backupSaved)
                    RemoveBackup(item.SettingId);

                return PerformanceTuningResult.Fail($"Windows не изменила режим гибернации: {BuildCommandError(result)}");
            }

            InvalidatePowerRuntimeCache();
            var verify = ReadRegistryDword(Registry.LocalMachine, PowerControlPath, HibernateEnabledValueName);
            if (((verify.Exists && verify.Value != 0) != targetEnabled))
                return PerformanceTuningResult.Fail("Команда выполнена, но повторное чтение реестра не подтвердило изменение гибернации.");

            string backupText = backupSaved ? " Точка отката сохранена." : " Новая точка отката не создавалась.";
            return PerformanceTuningResult.Ok($"{item.Title}: {(targetEnabled ? item.EnabledText : item.DisabledText)}.{backupText}");
        }

        private PerformanceTuningResult ApplyRegistryDword(PerformanceTuningItem item)
        {
            var current = ReadRegistryDword(item.RegistryHive, item.RegistryPath, item.RegistryValueName);
            int targetValue = ResolveRegistryTargetValue(item);
            bool shouldDelete = item.RegistryDeleteWhenDisabled && item.IsToggle && !item.ToggleValue;

            if (shouldDelete && !current.Exists)
                return PerformanceTuningResult.Ok($"{item.Title}: значение уже отсутствует. Изменения не требуются.");

            if (!shouldDelete && current.Exists && current.Value == targetValue)
                return PerformanceTuningResult.Ok($"{item.Title}: значение уже установлено. Изменения не требуются.");

            bool backupSaved = SaveBackup(new PerformanceSettingBackupRecord
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

            try
            {
                using var key = item.RegistryHive.CreateSubKey(item.RegistryPath, writable: true);
                if (key == null)
                    return PerformanceTuningResult.Fail("Не удалось открыть ветку реестра для записи.");

                if (shouldDelete)
                    key.DeleteValue(item.RegistryValueName, throwOnMissingValue: false);
                else
                    key.SetValue(item.RegistryValueName, targetValue, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                if (backupSaved)
                    RemoveBackup(item.SettingId);

                return PerformanceTuningResult.Fail($"Не удалось записать реестр: {ex.Message}");
            }

            var updated = ReadRegistryDword(item.RegistryHive, item.RegistryPath, item.RegistryValueName);
            bool applied = shouldDelete
                ? !updated.Exists
                : updated.Exists && updated.Value == targetValue;

            if (!applied)
                return PerformanceTuningResult.Fail("Запись выполнена, но повторное чтение реестра не подтвердило изменение.");

            string backupText = backupSaved ? " Точка отката сохранена." : " Новая точка отката не создавалась.";
            return PerformanceTuningResult.Ok(
                $"{item.Title}: применено.{backupText}",
                item.RequiresRestart,
                item.RestartReason);
        }

        private PerformanceTuningResult ApplyMemoryCompression(PerformanceTuningItem item)
        {
            var read = RunPowerShell("try { [bool]((Get-MMAgent).MemoryCompression) } catch { Write-Error $_.Exception.Message; exit 1 }");
            if (!read.Success)
                return PerformanceTuningResult.Fail($"Не удалось сохранить состояние MMAgent: {BuildCommandError(read)}");

            bool oldValue = read.Output.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0;
            bool targetValue = item.ToggleValue;

            if (oldValue == targetValue)
                return PerformanceTuningResult.Ok($"{item.Title}: значение уже установлено. Изменения не требуются.");

            bool backupSaved = SaveBackup(new PerformanceSettingBackupRecord
            {
                SettingId = item.SettingId,
                Kind = nameof(PerformanceBackupKind.MemoryCompression),
                Value = oldValue ? "true" : "false",
                Label = oldValue ? "включено" : "отключено",
                CreatedAtUtc = DateTime.UtcNow
            }, targetValue ? "true" : "false");

            string command = targetValue
                ? "Enable-MMAgent -MemoryCompression"
                : "Disable-MMAgent -MemoryCompression";

            var result = RunPowerShell(command);
            if (!result.Success)
            {
                if (backupSaved)
                    RemoveBackup(item.SettingId);

                return PerformanceTuningResult.Fail($"MMAgent не применил изменение: {BuildCommandError(result)}");
            }

            var verify = RunPowerShell("try { [bool]((Get-MMAgent).MemoryCompression) } catch { Write-Error $_.Exception.Message; exit 1 }");
            if (verify.Success)
            {
                bool verifiedValue = verify.Output.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0;
                if (verifiedValue != targetValue)
                    return PerformanceTuningResult.Fail("Команда выполнена, но повторная проверка MMAgent не подтвердила изменение.");
            }

            string backupText = backupSaved ? " Точка отката сохранена." : " Новая точка отката не создавалась.";
            return PerformanceTuningResult.Ok(
                $"Сжатие памяти изменено. Перезагрузка рекомендуется.{backupText}",
                requiresRestart: true,
                restartReason: item.RestartReason);
        }

        private PerformanceTuningResult RollbackPowerPlan(PerformanceSettingBackupRecord backup)
        {
            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/setactive", backup.Value);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось вернуть схему питания: {BuildCommandError(result)}");

            InvalidatePowerRuntimeCache();
            return PerformanceTuningResult.Ok($"Схема питания возвращена: «{backup.Label}».");
        }

        private PerformanceTuningResult RollbackPowerAcSetting(PerformanceSettingBackupRecord backup)
        {
            bool useDc = string.Equals(backup.Kind, nameof(PerformanceBackupKind.PowerDcSetting), StringComparison.OrdinalIgnoreCase);
            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg(useDc ? "/setdcvalueindex" : "/setacvalueindex", "SCHEME_CURRENT", backup.SubgroupAlias, backup.SettingAlias, backup.Value);
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось вернуть powercfg: {BuildCommandError(result)}");

            var commitResult = RunPowerCfg("/setactive", "SCHEME_CURRENT");
            if (!commitResult.Success)
                return PerformanceTuningResult.Fail($"Значение возвращено, но Windows не активировала обновлённую схему: {BuildCommandError(commitResult)}");

            InvalidatePowerRuntimeCache();
            string modeText = useDc ? "от батареи" : "от сети";
            return PerformanceTuningResult.Ok($"Параметр {modeText} возвращён к значению «{backup.Label}».");
        }

        private PerformanceTuningResult RollbackPowerHibernation(PerformanceSettingBackupRecord backup)
        {
            bool enable = string.Equals(backup.Value, "1", StringComparison.OrdinalIgnoreCase);
            InvalidatePowerRuntimeCache();
            var result = RunPowerCfg("/hibernate", enable ? "on" : "off");
            if (!result.Success)
                return PerformanceTuningResult.Fail($"Не удалось вернуть режим гибернации: {BuildCommandError(result)}");

            return PerformanceTuningResult.Ok($"Гибернация возвращена: {(enable ? "включена" : "отключена")}.");
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
                _discoveredPowerSettingsCache = null;
            }
        }

        private IReadOnlyList<DiscoveredPowerSetting> GetDiscoveredPowerSettings()
        {
            lock (_runtimeCacheSync)
            {
                if (_discoveredPowerSettingsCache != null && DateTime.UtcNow - _discoveredPowerSettingsCacheUtc <= RuntimeCacheLifetime)
                    return _discoveredPowerSettingsCache;
            }

            string scheme = GetCurrentPowerSchemeArgument();
            var result = RunPowerCfg("/qh", scheme);
            if (!result.Success)
                result = RunPowerCfg("/query", scheme);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return Array.Empty<DiscoveredPowerSetting>();

            var settings = new List<DiscoveredPowerSetting>();
            string currentSubgroupGuid = string.Empty;
            string currentSubgroupName = string.Empty;
            DiscoveredPowerSetting current = null;
            long? pendingOptionIndex = null;

            foreach (string rawLine in SplitLines(result.Output))
            {
                string line = rawLine.Trim();
                if (IsPowerSubgroupLine(line))
                {
                    currentSubgroupGuid = ExtractFirstGuid(line);
                    currentSubgroupName = ExtractNameFromParentheses(line);
                    current = null;
                    pendingOptionIndex = null;
                    continue;
                }

                if (IsPowerSettingLine(line))
                {
                    string settingGuid = ExtractFirstGuid(line);
                    if (string.IsNullOrWhiteSpace(settingGuid))
                        continue;

                    current = new DiscoveredPowerSetting
                    {
                        SubgroupGuid = currentSubgroupGuid,
                        SubgroupName = currentSubgroupName,
                        SettingGuid = settingGuid,
                        SettingName = ExtractNameFromParentheses(line)
                    };
                    settings.Add(current);
                    pendingOptionIndex = null;
                    continue;
                }

                if (current == null)
                    continue;

                if (IsMinimumLine(line) && TryReadHexValue(line, out long minimum))
                {
                    current.Minimum = minimum;
                    continue;
                }

                if (IsMaximumLine(line) && TryReadHexValue(line, out long maximum))
                {
                    current.Maximum = maximum;
                    continue;
                }

                if (IsIncrementLine(line) && TryReadHexValue(line, out long increment))
                {
                    current.Increment = increment;
                    continue;
                }

                if (IsPossibleSettingIndexLine(line) && TryReadHexValue(line, out long optionIndex))
                {
                    pendingOptionIndex = optionIndex;
                    continue;
                }

                if (IsPossibleSettingNameLine(line) && pendingOptionIndex.HasValue)
                {
                    string label = ExtractValueAfterColon(line);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        current.Options.Add(new PerformanceTuningOption(
                            label.Trim(),
                            pendingOptionIndex.Value.ToString(CultureInfo.InvariantCulture),
                            "Значение из powercfg"));
                    }

                    pendingOptionIndex = null;
                    continue;
                }

                if (IsCurrentAcLine(line) && TryReadHexValue(line, out long currentAc))
                {
                    current.CurrentAcIndex = currentAc;
                    continue;
                }

                if (IsCurrentDcLine(line) && TryReadHexValue(line, out long currentDc))
                {
                    current.CurrentDcIndex = currentDc;
                    continue;
                }
            }

            lock (_runtimeCacheSync)
            {
                _discoveredPowerSettingsCache = settings;
                _discoveredPowerSettingsCacheUtc = DateTime.UtcNow;
            }

            return settings;
        }

        private static bool IsPowerSubgroupLine(string line)
        {
            return line.IndexOf("Subgroup GUID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("GUID подгруппы", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Подгруппа GUID", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPowerSettingLine(string line)
        {
            return line.IndexOf("Power Setting GUID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("GUID настройки питания", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Настройка питания GUID", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMinimumLine(string line)
        {
            return line.IndexOf("Minimum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Миним", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMaximumLine(string line)
        {
            return line.IndexOf("Maximum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Максим", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsIncrementLine(string line)
        {
            return line.IndexOf("Increment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Шаг", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Приращ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPossibleSettingIndexLine(string line)
        {
            return line.IndexOf("Possible Setting Index", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Индекс возмож", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Возможное значение индекса", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPossibleSettingNameLine(string line)
        {
            return line.IndexOf("Possible Setting Friendly Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Friendly Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Понятное имя", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("Название возмож", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCurrentAcLine(string line)
        {
            return (line.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("AC Power", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (line.IndexOf("AC", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("Possible", StringComparison.OrdinalIgnoreCase) < 0) ||
                   line.IndexOf("от сети", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("переменного тока", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCurrentDcLine(string line)
        {
            return (line.IndexOf("Current", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("DC Power", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (line.IndexOf("DC", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("Possible", StringComparison.OrdinalIgnoreCase) < 0) ||
                   line.IndexOf("от батареи", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("от аккумулятора", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("постоянного тока", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractFirstGuid(string line)
        {
            var match = GuidRegex.Match(line ?? string.Empty);
            return match.Success ? match.Value : string.Empty;
        }

        private static string NormalizeGuid(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string ExtractValueAfterColon(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            int index = line.IndexOf(':');
            return index >= 0 && index + 1 < line.Length
                ? line[(index + 1)..].Trim()
                : line.Trim();
        }

        private string GetCurrentPowerSchemeArgument()
        {
            return GetActivePowerPlan()?.Guid ?? "SCHEME_CURRENT";
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

            string scheme = GetCurrentPowerSchemeArgument();
            var result = RunPowerCfg("/query", scheme, subgroupAlias, settingAlias);
            if (!result.Success || !ContainsSettingOutput(result.Output, settingAlias))
                result = RunPowerCfg("/qh", scheme, subgroupAlias, settingAlias);

            if (!result.Success)
            {
                var discoveredFallback = QueryPowerSettingFromDiscovered(subgroupAlias, settingAlias);
                if (discoveredFallback.Success)
                {
                    lock (_runtimeCacheSync)
                        _powerSettingCache[cacheKey] = new PowerSettingCacheEntry(discoveredFallback, DateTime.UtcNow);

                    return discoveredFallback.Clone();
                }

                return PowerSettingQueryResult.Fail(result.Error);
            }

            if (!ContainsSettingOutput(result.Output, settingAlias))
            {
                var discoveredFallback = QueryPowerSettingFromDiscovered(subgroupAlias, settingAlias);
                if (discoveredFallback.Success)
                {
                    lock (_runtimeCacheSync)
                        _powerSettingCache[cacheKey] = new PowerSettingCacheEntry(discoveredFallback, DateTime.UtcNow);

                    return discoveredFallback.Clone();
                }

                return PowerSettingQueryResult.Fail("Параметр не найден в текущей схеме.");
            }

            TryReadPowerSettingCurrentAcIndex(result.Output, out long currentAc);
            TryReadPowerSettingCurrentDcIndex(result.Output, out long currentDc);
            TryReadPowerSettingBound(result.Output, true, out long minimum);
            TryReadPowerSettingBound(result.Output, false, out long maximum);

            var query = new PowerSettingQueryResult
            {
                Success = true,
                CurrentAcIndex = currentAc >= 0 ? currentAc : null,
                CurrentDcIndex = currentDc >= 0 ? currentDc : null,
                Minimum = minimum >= 0 ? minimum : null,
                Maximum = maximum >= 0 ? maximum : null
            };

            if (!query.CurrentAcIndex.HasValue && !query.CurrentDcIndex.HasValue)
            {
                var discoveredFallback = QueryPowerSettingFromDiscovered(subgroupAlias, settingAlias);
                if (discoveredFallback.Success)
                    query = discoveredFallback;
            }

            lock (_runtimeCacheSync)
                _powerSettingCache[cacheKey] = new PowerSettingCacheEntry(query, DateTime.UtcNow);

            return query.Clone();
        }

        private PowerSettingQueryResult QueryPowerSettingFromDiscovered(string subgroupAlias, string settingAlias)
        {
            string subgroup = NormalizePowerSubgroupIdentifier(subgroupAlias);
            string setting = NormalizeGuid(settingAlias);

            var discovered = GetDiscoveredPowerSettings()
                .FirstOrDefault(candidate =>
                    string.Equals(NormalizeGuid(candidate.SubgroupGuid), subgroup, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeGuid(candidate.SettingGuid), setting, StringComparison.OrdinalIgnoreCase));

            if (discovered == null)
                return PowerSettingQueryResult.Fail("Параметр не найден в полном списке powercfg /qh.");

            return new PowerSettingQueryResult
            {
                Success = true,
                CurrentAcIndex = discovered.CurrentAcIndex,
                CurrentDcIndex = discovered.CurrentDcIndex,
                Minimum = discovered.Minimum,
                Maximum = discovered.Maximum
            };
        }

        private static string NormalizePowerSubgroupIdentifier(string value)
        {
            string normalized = NormalizeGuid(value);
            return normalized switch
            {
                "sub_video" => SubVideoGuid,
                "sub_sleep" => SubSleepGuid,
                "sub_usb" => SubUsbGuid,
                "sub_pciexpress" => SubPciExpressGuid,
                "sub_buttons" => SubButtonsGuid,
                "sub_battery" => SubBatteryGuid,
                "sub_wifi" => SubWirelessGuid,
                "sub_energysaver" => SubEnergySaverGuid,
                _ => normalized
            };
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
                if (!IsCurrentAcLine(line))
                    continue;

                if (TryReadHexValue(line, out value))
                    return true;
            }

            return false;
        }

        private static bool TryReadPowerSettingCurrentDcIndex(string output, out long value)
        {
            value = -1;
            foreach (string line in SplitLines(output))
            {
                if (!IsCurrentDcLine(line))
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
            string source = line ?? string.Empty;
            var match = Regex.Match(source, @"0x([0-9a-fA-F]+)");
            if (match.Success)
            {
                value = Convert.ToInt64(match.Groups[1].Value, 16);
                return true;
            }

            match = Regex.Match(source, @"(?<![A-Za-z0-9])([0-9]+)(?![A-Za-z0-9])");
            if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            return false;
        }

        private static string ExtractNameFromParentheses(string value)
        {
            var match = Regex.Match(value ?? string.Empty, @"\((?<name>[^)]*)\)");
            return match.Success ? match.Groups["name"].Value : string.Empty;
        }

        private static CommandResult RunPowerCfg(params string[] arguments)
        {
            return RunProcess("powercfg.exe", arguments, GetPowerCfgEncoding());
        }

        private static double? TryReadCpuUsagePercent()
        {
            if (!TryReadSystemTimes(out var idle1, out var kernel1, out var user1))
                return null;

            Thread.Sleep(160);

            if (!TryReadSystemTimes(out var idle2, out var kernel2, out var user2))
                return null;

            ulong idle = idle2 - idle1;
            ulong kernel = kernel2 - kernel1;
            ulong user = user2 - user1;
            ulong total = kernel + user;
            if (total == 0 || total < idle)
                return null;

            return Math.Clamp((total - idle) * 100d / total, 0, 100);
        }

        private static bool TryReadSystemTimes(out ulong idle, out ulong kernel, out ulong user)
        {
            idle = 0;
            kernel = 0;
            user = 0;

            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                return false;

            idle = ToUInt64(idleTime);
            kernel = ToUInt64(kernelTime);
            user = ToUInt64(userTime);
            return true;
        }

        private static double? TryReadGpuUsagePercent()
        {
            string command = @"
$samples = (Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop).CounterSamples |
    Where-Object { $_.InstanceName -notmatch '_engtype_copy|_engtype_video' }
$sum = ($samples | Measure-Object CookedValue -Sum).Sum
if ($null -eq $sum) { $sum = 0 }
[Math]::Round([Math]::Min(100, [Math]::Max(0, $sum)), 1)
";

            var result = RunPowerShell(command);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                return null;

            var first = SplitLines(result.Output).LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
                return Math.Clamp(invariant, 0, 100);

            if (double.TryParse(first, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
                return Math.Clamp(current, 0, 100);

            return null;
        }

        private static string TranslateSensorGroup(string group)
        {
            return group switch
            {
                "Cpu" => "CPU",
                "Gpu" => "GPU",
                "Storage" => "накопители",
                "Motherboard" => "плата",
                "Other" => "прочее",
                _ => string.IsNullOrWhiteSpace(group) ? "прочее" : group
            };
        }

        private static ulong ToUInt64(FileTime value)
        {
            return ((ulong)value.HighDateTime << 32) | value.LowDateTime;
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
                .OrderBy(record => record.CreatedAtUtc)
                .FirstOrDefault(record => string.Equals(record.SettingId, settingId, StringComparison.OrdinalIgnoreCase));
        }

        private bool SaveBackup(PerformanceSettingBackupRecord record, string targetValue = null)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SettingId))
                return false;

            if (!string.IsNullOrWhiteSpace(targetValue) &&
                string.Equals(record.Value, targetValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var backups = LoadBackups();
            bool alreadyHasBackup = backups.Any(item =>
                string.Equals(item.SettingId, record.SettingId, StringComparison.OrdinalIgnoreCase));

            if (alreadyHasBackup)
            {
                SaveBackups(backups);
                return false;
            }

            backups.Add(record);
            SaveBackups(backups);
            return true;
        }

        private void RemoveBackup(string settingId)
        {
            var backups = LoadBackups()
                .Where(item => !string.Equals(item.SettingId, settingId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveBackups(backups);
        }

        private void PruneBackups()
        {
            var backups = LoadBackups();
            if (backups.Count > 0)
                SaveBackups(backups);
        }

        private List<PerformanceSettingBackupRecord> LoadBackups()
        {
            try
            {
                if (!File.Exists(_backupPath))
                    return new List<PerformanceSettingBackupRecord>();

                string json = File.ReadAllText(_backupPath);
                var backups = JsonSerializer.Deserialize<List<PerformanceSettingBackupRecord>>(json);
                return CompactBackups(backups).ToList();
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

                var compactBackups = CompactBackups(backups);
                string json = JsonSerializer.Serialize(compactBackups, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_backupPath, json);
            }
            catch
            {
            }
        }

        private IReadOnlyList<PerformanceSettingBackupRecord> CompactBackups(IReadOnlyList<PerformanceSettingBackupRecord> backups)
        {
            DateTime cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(GetBackupRetentionDays());

            return (backups ?? Array.Empty<PerformanceSettingBackupRecord>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.SettingId))
                .Where(record => record.CreatedAtUtc >= cutoffUtc)
                .GroupBy(record => record.SettingId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(record => record.CreatedAtUtc).First())
                .OrderByDescending(record => record.CreatedAtUtc)
                .Take(MaxBackupRecords)
                .OrderBy(record => record.CreatedAtUtc)
                .ToList();
        }

        private int GetBackupRetentionDays()
        {
            return Math.Clamp(_settingsManager?.CurrentSettings?.PerformanceBackupRetentionDays ?? 30, 1, 30);
        }

        private static string GetBackupPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TweakWise",
                BackupFileName);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint LowDateTime;
            public uint HighDateTime;
        }

        private sealed class DiscoveredPowerSetting
        {
            public string SubgroupGuid { get; set; } = string.Empty;
            public string SubgroupName { get; set; } = string.Empty;
            public string SettingGuid { get; set; } = string.Empty;
            public string SettingName { get; set; } = string.Empty;
            public long? Minimum { get; set; }
            public long? Maximum { get; set; }
            public long? Increment { get; set; }
            public long? CurrentAcIndex { get; set; }
            public long? CurrentDcIndex { get; set; }
            public List<PerformanceTuningOption> Options { get; } = new List<PerformanceTuningOption>();

            public string DisplaySubgroupName => string.IsNullOrWhiteSpace(SubgroupName) ? "Powercfg" : SubgroupName.Trim();
            public string DisplaySettingName => string.IsNullOrWhiteSpace(SettingName) ? SettingGuid : SettingName.Trim();
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
            public long? CurrentDcIndex { get; set; }
            public long? Minimum { get; set; }
            public long? Maximum { get; set; }

            public PowerSettingQueryResult Clone()
            {
                return new PowerSettingQueryResult
                {
                    Success = Success,
                    Error = Error,
                    CurrentAcIndex = CurrentAcIndex,
                    CurrentDcIndex = CurrentDcIndex,
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

        private sealed class RequestedPerformanceValue
        {
            public string SelectedOptionValue { get; set; }
            public string SelectedOptionLabel { get; set; }
            public bool ToggleValue { get; set; }
            public double NumericValue { get; set; }
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
        private bool _statusIsWarning;
        private bool _requiresElevationWarning;
        private HealthLevel _signalLevel = HealthLevel.Good;

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
        public string SignalId { get; set; } = string.Empty;
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
        public HealthLevel SignalLevel
        {
            get => _signalLevel;
            set
            {
                if (_signalLevel == value)
                    return;

                _signalLevel = value;
                OnPropertyChanged(nameof(SignalLevel));
                OnPropertyChanged(nameof(HasActiveSignal));
                OnPropertyChanged(nameof(SignalActionText));
                OnPropertyChanged(nameof(SignalKindText));
            }
        }

        public bool RequiresElevationWarning
        {
            get => _requiresElevationWarning;
            set
            {
                if (_requiresElevationWarning == value)
                    return;

                _requiresElevationWarning = value;
                OnPropertyChanged(nameof(RequiresElevationWarning));
            }
        }
        public bool RequiresRestart { get; set; }
        public bool ShowApplyAction { get; set; }
        public bool ShowSliderShortcuts { get; set; }
        public double QuickEnableValue { get; set; } = 1;
        public string QuickEnableText { get; set; } = "Включить";
        public string QuickDisableText { get; set; } = "Отключить";
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
                OnPropertyChanged(nameof(HasActiveSignal));
                OnPropertyChanged(nameof(SignalActionText));
                OnPropertyChanged(nameof(SignalKindText));
            }
        }

        public bool StatusIsWarning
        {
            get => _statusIsWarning;
            set
            {
                if (_statusIsWarning == value)
                    return;

                _statusIsWarning = value;
                OnPropertyChanged(nameof(StatusIsWarning));
                OnPropertyChanged(nameof(HasActiveSignal));
                OnPropertyChanged(nameof(SignalActionText));
                OnPropertyChanged(nameof(SignalKindText));
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
                OnPropertyChanged(nameof(IsUnavailable));
            }
        }

        public bool IsUnavailable => !IsSupported;

        public bool IsPriority
        {
            get => _isPriority;
            set
            {
                if (_isPriority == value)
                    return;

                _isPriority = value;
                OnPropertyChanged(nameof(IsPriority));
                OnPropertyChanged(nameof(HasActiveSignal));
                OnPropertyChanged(nameof(SignalActionText));
                OnPropertyChanged(nameof(SignalKindText));
            }
        }

        public bool IsToggle => ControlKind == PerformanceSettingControlKind.Toggle;
        public bool IsCombo => ControlKind == PerformanceSettingControlKind.Combo;
        public bool IsSlider => ControlKind == PerformanceSettingControlKind.Slider;
        public bool HasSliderShortcuts => IsSlider && ShowSliderShortcuts;
        public bool HasCurrentValue => !string.IsNullOrWhiteSpace(CurrentValue);
        public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);
        public bool HasRisk => !string.IsNullOrWhiteSpace(RiskLabel);
        public string RiskBadgeLabel => FormatRiskBadgeLabel(RiskLabel);
        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
        public bool HasActiveSignal =>
            SignalLevel == HealthLevel.Normal ||
            SignalLevel == HealthLevel.Attention ||
            SignalLevel == HealthLevel.Warning ||
            SignalLevel == HealthLevel.Critical ||
            IsPriority ||
            (StatusIsWarning && HasStatusMessage);
        public string SignalActionText => "Игнорировать";
        private static string FormatRiskBadgeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            return trimmed.StartsWith("Риск:", StringComparison.CurrentCultureIgnoreCase) ||
                   trimmed.StartsWith("Risk:", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"Риск: {trimmed}";
        }

        public string SignalKindText
        {
            get
            {
                return SignalLevel switch
                {
                    HealthLevel.Critical => "критично",
                    HealthLevel.Warning => "проблема",
                    HealthLevel.Attention => "внимание",
                    HealthLevel.Normal => "рекомендация",
                    _ => IsPriority ? "рекомендация" : string.Empty
                };
            }
        }

        public void SetStatus(string message, bool isWarning)
        {
            bool hasMessage = !string.IsNullOrWhiteSpace(message);
            StatusIsWarning = hasMessage && isWarning;
            if (hasMessage && isWarning)
            {
                SignalLevel = HealthLevel.Warning;
            }
            else if (!hasMessage && !IsPriority)
            {
                SignalLevel = HealthLevel.Good;
            }

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
        PowerDcSetting,
        PowerHibernation,
        RegistryDword,
        MemoryCompression
    }
}
