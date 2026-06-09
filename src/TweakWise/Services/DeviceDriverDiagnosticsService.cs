using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TweakWise.Models;

namespace TweakWise.Services
{
    public enum DeviceDriverDashboardMode
    {
        Drivers,
        Devices
    }

    public sealed class DeviceDriverDiagnosticsSnapshot
    {
        public int InstalledDriverCount { get; set; }
        public int OldDriverCount { get; set; }
        public int UnsignedDriverCount { get; set; }
        public int MicrosoftFallbackDriverCount { get; set; }
        public int DeviceCount { get; set; }
        public int ProblemDeviceCount { get; set; }
        public int UnknownDeviceCount { get; set; }
        public int PrinterCount { get; set; }
        public int OfflinePrinterCount { get; set; }
        public bool DriverBackupExists { get; set; }
        public string DriverSummary { get; set; } = string.Empty;
        public string DeviceSummary { get; set; } = string.Empty;
        public List<DeviceDriverFinding> Findings { get; set; } = new List<DeviceDriverFinding>();
        public List<DeviceDriverGroupSnapshot> DriverGroups { get; set; } = new List<DeviceDriverGroupSnapshot>();
        public List<DeviceDriverGroupSnapshot> DeviceGroups { get; set; } = new List<DeviceDriverGroupSnapshot>();

        public IEnumerable<DeviceDriverFinding> GetFindings(DeviceDriverDashboardMode mode)
        {
            return (Findings ?? new List<DeviceDriverFinding>())
                .Where(item => item != null && item.Mode == mode)
                .OrderByDescending(item => GetSeverity(item.Level))
                .ThenBy(item => item.Title ?? string.Empty);
        }

        public IEnumerable<DeviceDriverGroupSnapshot> GetGroups(DeviceDriverDashboardMode mode)
        {
            var groups = mode == DeviceDriverDashboardMode.Drivers ? DriverGroups : DeviceGroups;
            return (groups ?? new List<DeviceDriverGroupSnapshot>())
                .Where(item => item != null)
                .OrderByDescending(item => GetSeverity(item.Level))
                .ThenBy(item => item.Order)
                .ThenBy(item => item.Title ?? string.Empty);
        }

        private static int GetSeverity(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Critical => 5,
                HealthLevel.Warning => 4,
                HealthLevel.Attention => 3,
                HealthLevel.Normal => 2,
                HealthLevel.Good => 1,
                _ => 0
            };
        }
    }

    public sealed class DeviceDriverFinding
    {
        public string Id { get; set; } = string.Empty;
        public DeviceDriverDashboardMode Mode { get; set; }
        public HealthLevel Level { get; set; } = HealthLevel.Normal;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public string TargetGroupId { get; set; } = string.Empty;
        public string TargetItemId { get; set; } = string.Empty;
    }

    public sealed class DeviceDriverGroupSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public DeviceDriverDashboardMode Mode { get; set; }
        public int Order { get; set; }
        public HealthLevel Level { get; set; } = HealthLevel.Good;
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int ProblemCount { get; set; }
        public int RecommendationCount { get; set; }
        public List<DeviceDriverInventoryItem> Items { get; set; } = new List<DeviceDriverInventoryItem>();
    }

    public sealed class DeviceDriverInventoryItem
    {
        public string Id { get; set; } = string.Empty;
        public DeviceDriverInventoryItemKind Kind { get; set; } = DeviceDriverInventoryItemKind.Driver;
        public HealthLevel Level { get; set; } = HealthLevel.Good;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string MetaText { get; set; } = string.Empty;
        public string RiskText { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string InfName { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public string SearchQuery { get; set; } = string.Empty;
        public List<DeviceDriverInventoryAction> Actions { get; set; } = new List<DeviceDriverInventoryAction>();
    }

    public enum DeviceDriverInventoryItemKind
    {
        Driver,
        Device,
        Printer,
        Tool
    }

    public enum DeviceDriverInventoryActionKind
    {
        SearchOnline,
        InstallInf,
        RollbackFromBackup,
        BackupDrivers,
        EnableDevice,
        DisableDevice,
        RestartDevice,
        ScanDevices,
        OpenDeviceManager,
        OpenPrinterQueue,
        EnableSafeMode,
        DisableSafeMode
    }

    public sealed class DeviceDriverInventoryAction
    {
        public DeviceDriverInventoryActionKind Kind { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string InfName { get; set; } = string.Empty;
        public string SearchQuery { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
        public string RiskText { get; set; } = string.Empty;
        public bool RequiresAdmin { get; set; }
        public bool IsDestructive { get; set; }
    }

    public sealed class DeviceDriverOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public static DeviceDriverOperationResult Ok(string message)
        {
            return new DeviceDriverOperationResult { Success = true, Message = message ?? string.Empty };
        }

        public static DeviceDriverOperationResult Fail(string message)
        {
            return new DeviceDriverOperationResult { Success = false, Message = message ?? "Операция не выполнена." };
        }
    }

    public sealed class DeviceDriverDiagnosticsService
    {
        private const int CommandTimeoutMs = 9000;
        private static readonly TimeSpan DefaultSnapshotCacheAge = TimeSpan.FromMinutes(8);
        private static readonly object SnapshotCacheSync = new object();
        private static DeviceDriverDiagnosticsSnapshot _cachedSnapshot;
        private static DateTime _cachedSnapshotAtUtc = DateTime.MinValue;
        private static Task<DeviceDriverDiagnosticsSnapshot> _activeScanTask;

        public Task<DeviceDriverDiagnosticsSnapshot> ScanAsync(CancellationToken cancellationToken)
        {
            return ScanAsync(cancellationToken, forceRefresh: true);
        }

        public Task<DeviceDriverDiagnosticsSnapshot> GetOrScanAsync(CancellationToken cancellationToken, TimeSpan maxAge)
        {
            return ScanAsync(cancellationToken, forceRefresh: false, maxAge);
        }

        public async Task<DeviceDriverDiagnosticsSnapshot> ScanAsync(
            CancellationToken cancellationToken,
            bool forceRefresh,
            TimeSpan? maxAge = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scanTask = GetOrStartScanTask(forceRefresh, maxAge ?? DefaultSnapshotCacheAge);
            return await scanTask.WaitAsync(cancellationToken);
        }

        private static Task<DeviceDriverDiagnosticsSnapshot> GetOrStartScanTask(bool forceRefresh, TimeSpan maxAge)
        {
            lock (SnapshotCacheSync)
            {
                if (!forceRefresh && TryGetCachedSnapshotLocked(maxAge, out var cachedSnapshot))
                    return Task.FromResult(cachedSnapshot);

                if (_activeScanTask != null && !_activeScanTask.IsCompleted)
                    return _activeScanTask;

                _activeScanTask = Task.Run(() =>
                {
                    var snapshot = Scan(CancellationToken.None) ?? new DeviceDriverDiagnosticsSnapshot();
                    StoreCachedSnapshot(snapshot);
                    return snapshot;
                });

                return _activeScanTask;
            }
        }

        private static bool TryGetCachedSnapshotLocked(TimeSpan maxAge, out DeviceDriverDiagnosticsSnapshot snapshot)
        {
            snapshot = null;
            if (_cachedSnapshot == null)
                return false;

            if (maxAge > TimeSpan.Zero && DateTime.UtcNow - _cachedSnapshotAtUtc > maxAge)
                return false;

            snapshot = _cachedSnapshot;
            return true;
        }

        private static void StoreCachedSnapshot(DeviceDriverDiagnosticsSnapshot snapshot)
        {
            lock (SnapshotCacheSync)
            {
                _cachedSnapshot = snapshot;
                _cachedSnapshotAtUtc = DateTime.UtcNow;
            }
        }

        private static DeviceDriverDiagnosticsSnapshot Scan(CancellationToken cancellationToken)
        {
            var snapshot = new DeviceDriverDiagnosticsSnapshot();

            try
            {
                var drivers = QueryDrivers(cancellationToken);
                var devices = QueryDevices(cancellationToken);
                var printers = QueryPrinters(cancellationToken);

                snapshot.InstalledDriverCount = drivers.Count;
                snapshot.OldDriverCount = drivers.Count(IsOldNonMicrosoftDriver);
                snapshot.UnsignedDriverCount = drivers.Count(driver => driver.IsSigned == false);
                snapshot.MicrosoftFallbackDriverCount = drivers.Count(IsMicrosoftFallbackDriver);
                snapshot.DeviceCount = devices.Count;
                snapshot.ProblemDeviceCount = devices.Count(device => device.ConfigManagerErrorCode.GetValueOrDefault() != 0);
                snapshot.UnknownDeviceCount = devices.Count(IsUnknownDevice);
                snapshot.PrinterCount = printers.Count;
                snapshot.OfflinePrinterCount = printers.Count(IsOfflinePrinter);
                snapshot.DriverBackupExists = Directory.Exists(GetDriverBackupRoot()) && Directory.EnumerateFileSystemEntries(GetDriverBackupRoot()).Any();
                snapshot.DriverSummary = BuildDriverSummary(snapshot);
                snapshot.DeviceSummary = BuildDeviceSummary(snapshot);
                snapshot.Findings.AddRange(BuildFindings(snapshot, drivers, devices, printers).Where(item => item != null));
                snapshot.DriverGroups = BuildDriverGroups(drivers).ToList();
                snapshot.DeviceGroups = BuildDeviceGroups(devices, printers).ToList();
            }
            catch (Exception ex)
            {
                snapshot.DriverSummary = "Диагностика драйверов не завершилась";
                snapshot.DeviceSummary = "Диагностика устройств не завершилась";
                snapshot.Findings.Add(new DeviceDriverFinding
                {
                    Id = "devices.scan.failed",
                    Mode = DeviceDriverDashboardMode.Drivers,
                    Level = HealthLevel.Warning,
                    Title = "Диагностика прервана",
                    Description = "Не удалось получить сведения о драйверах и устройствах без участия стандартных окон Windows.",
                    ActionText = ex.Message
                });
            }

            EnsureFallbackGroups(snapshot);

            return snapshot;
        }

        private static List<DriverRecord> QueryDrivers(CancellationToken cancellationToken)
        {
            string script = "$ErrorActionPreference='SilentlyContinue'; Get-CimInstance Win32_PnPSignedDriver | Select-Object DeviceName,DeviceID,DeviceClass,Manufacturer,DriverProviderName,DriverVersion,DriverDate,InfName,IsSigned | ConvertTo-Json -Compress -Depth 3";
            var drivers = ReadJsonItems(RunPowerShell(script, cancellationToken))
                .Select(ParseDriver)
                .Where(item => !string.IsNullOrWhiteSpace(item.DeviceName) || !string.IsNullOrWhiteSpace(item.InfName))
                .ToList();

            if (drivers.Count > 0)
                return drivers;

            return QueryPnpUtilDrivers(cancellationToken);
        }

        private static List<DriverRecord> QueryPnpUtilDrivers(CancellationToken cancellationToken)
        {
            string output = RunProcess("cmd.exe", "/d /c \"chcp 65001 >nul & pnputil.exe /enum-drivers\"", cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
                return new List<DriverRecord>();

            var result = new List<DriverRecord>();
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in output.Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    AddPnpUtilDriver(result, current);
                    current.Clear();
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                string key = NormalizeText(line.Substring(0, separator));
                string value = NormalizeText(line.Substring(separator + 1));
                current[key] = value;
            }

            AddPnpUtilDriver(result, current);
            return result;
        }

        private static void AddPnpUtilDriver(List<DriverRecord> result, Dictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
                return;

            string infName = FirstNotEmpty(GetPnpValue(values, "Published Name", "Опубликованное имя"), GetPnpValue(values, "Published Name "));
            string provider = GetPnpValue(values, "Driver Package Provider", "Поставщик пакета драйвера", "Provider Name", "Поставщик");
            string className = GetPnpValue(values, "Class", "Класс", "Driver Package Class");
            string versionAndDate = GetPnpValue(values, "Driver Version And Date", "Версия и дата драйвера", "Driver Version");

            DateTime? date = null;
            string version = versionAndDate;
            var dateMatch = Regex.Match(versionAndDate ?? string.Empty, @"(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{2,4})\s+(?<version>.+)$");
            if (dateMatch.Success)
            {
                if (DateTime.TryParse(dateMatch.Groups["date"].Value, out var parsed))
                    date = parsed;
                version = dateMatch.Groups["version"].Value;
            }

            result.Add(new DriverRecord
            {
                DeviceName = FirstNotEmpty(GetPnpValue(values, "Original Name", "Исходное имя"), infName),
                DeviceClass = className,
                Manufacturer = provider,
                Provider = provider,
                Version = version,
                InfName = infName,
                DriverDate = date,
                IsSigned = true
            });
        }

        private static string GetPnpValue(Dictionary<string, string> values, params string[] keys)
        {
            foreach (string key in keys ?? Array.Empty<string>())
            {
                if (values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static List<DeviceRecord> QueryDevices(CancellationToken cancellationToken)
        {
            string script = "$ErrorActionPreference='SilentlyContinue'; Get-CimInstance Win32_PnPEntity | Select-Object Name,PNPDeviceID,PNPClass,ConfigManagerErrorCode,Status,Service,ClassGuid,HardwareID | ConvertTo-Json -Compress -Depth 4";
            return ReadJsonItems(RunPowerShell(script, cancellationToken))
                .Select(ParseDevice)
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.PnpDeviceId))
                .ToList();
        }

        private static List<PrinterRecord> QueryPrinters(CancellationToken cancellationToken)
        {
            string script = "$ErrorActionPreference='SilentlyContinue'; Get-CimInstance Win32_Printer | Select-Object Name,DriverName,PortName,WorkOffline,Default,PrinterStatus | ConvertTo-Json -Compress -Depth 2";
            return ReadJsonItems(RunPowerShell(script, cancellationToken))
                .Select(ParsePrinter)
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToList();
        }

        private static string RunPowerShell(string script, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return string.Empty;

            string preparedScript = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false; $OutputEncoding = [Console]::OutputEncoding; " + (script ?? string.Empty);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(preparedScript));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    return string.Empty;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(CommandTimeoutMs) || cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return string.Empty;
                }

                string output = outputTask.GetAwaiter().GetResult();
                _ = errorTask.GetAwaiter().GetResult();
                return output ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string RunProcess(string fileName, string arguments, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return string.Empty;

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    return string.Empty;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(CommandTimeoutMs) || cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return string.Empty;
                }

                string output = outputTask.GetAwaiter().GetResult();
                _ = errorTask.GetAwaiter().GetResult();
                return output ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IReadOnlyList<JsonElement> ReadJsonItems(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<JsonElement>();

            int start = json.IndexOfAny(new[] { '[', '{' });
            if (start > 0)
                json = json.Substring(start);

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    return new[] { document.RootElement.Clone() };
            }
            catch (JsonException)
            {
            }

            return Array.Empty<JsonElement>();
        }

        private static DriverRecord ParseDriver(JsonElement element)
        {
            return new DriverRecord
            {
                DeviceName = GetString(element, "DeviceName"),
                DeviceId = GetString(element, "DeviceID"),
                Manufacturer = GetString(element, "Manufacturer"),
                Provider = GetString(element, "DriverProviderName"),
                DeviceClass = GetString(element, "DeviceClass"),
                Version = GetString(element, "DriverVersion"),
                InfName = GetString(element, "InfName"),
                DriverDate = GetDate(element, "DriverDate"),
                IsSigned = GetBool(element, "IsSigned")
            };
        }

        private static DeviceRecord ParseDevice(JsonElement element)
        {
            return new DeviceRecord
            {
                Name = GetString(element, "Name"),
                PnpDeviceId = GetString(element, "PNPDeviceID"),
                PnpClass = GetString(element, "PNPClass"),
                Status = GetString(element, "Status"),
                Service = GetString(element, "Service"),
                ClassGuid = GetString(element, "ClassGuid"),
                HardwareId = GetStringArray(element, "HardwareID").FirstOrDefault() ?? string.Empty,
                ConfigManagerErrorCode = GetInt(element, "ConfigManagerErrorCode")
            };
        }

        private static PrinterRecord ParsePrinter(JsonElement element)
        {
            return new PrinterRecord
            {
                Name = GetString(element, "Name"),
                DriverName = GetString(element, "DriverName"),
                PortName = GetString(element, "PortName"),
                WorkOffline = GetBool(element, "WorkOffline"),
                IsDefault = GetBool(element, "Default"),
                PrinterStatus = GetInt(element, "PrinterStatus")
            };
        }

        private static IEnumerable<DeviceDriverFinding> BuildFindings(DeviceDriverDiagnosticsSnapshot snapshot, List<DriverRecord> drivers, List<DeviceRecord> devices, List<PrinterRecord> printers)
        {
            var unsignedDriverTarget = FindDriverTarget(drivers, driver => driver.IsSigned == false);
            var oldDriverTarget = FindDriverTarget(drivers, IsOldNonMicrosoftDriver);
            var microsoftFallbackTarget = FindDriverTarget(drivers, IsMicrosoftFallbackDriver);
            var problemDeviceTarget = FindDeviceTarget(devices, device => device.ConfigManagerErrorCode.GetValueOrDefault() != 0);
            var unknownDeviceTarget = FindDeviceTarget(devices, IsUnknownDevice);
            var offlinePrinterTarget = FindPrinterTarget(printers, IsOfflinePrinter);
            var genericPrinterTarget = FindPrinterTarget(printers, IsGenericPrinterDriver);

            if (snapshot.UnsignedDriverCount > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "drivers.unsigned",
                    Mode = DeviceDriverDashboardMode.Drivers,
                    Level = HealthLevel.Warning,
                    TargetGroupId = unsignedDriverTarget.GroupId,
                    TargetItemId = unsignedDriverTarget.ItemId,
                    Title = "Есть неподписанные драйверы",
                    Description = $"Обнаружено {snapshot.UnsignedDriverCount} драйверов без цифровой подписи. Такие пакеты нужно проверять перед обновлением или откатом.",
                    ActionText = "Откройте раздел драйверов, чтобы посмотреть INF, производителя и источник пакета."
                };
            }

            if (snapshot.OldDriverCount > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "drivers.old",
                    Mode = DeviceDriverDashboardMode.Drivers,
                    Level = HealthLevel.Normal,
                    TargetGroupId = oldDriverTarget.GroupId,
                    TargetItemId = oldDriverTarget.ItemId,
                    Title = "Есть старые драйверы",
                    Description = $"Найдено {snapshot.OldDriverCount} сторонних драйверов старше трёх лет. Их можно вынести в список кандидатов на обновление.",
                    ActionText = "Откройте раздел драйверов, чтобы сверить версию, дату и производителя."
                };
            }

            if (snapshot.MicrosoftFallbackDriverCount > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "drivers.microsoft-fallback",
                    Mode = DeviceDriverDashboardMode.Drivers,
                    Level = HealthLevel.Normal,
                    TargetGroupId = microsoftFallbackTarget.GroupId,
                    TargetItemId = microsoftFallbackTarget.ItemId,
                    Title = "Используются базовые драйверы Microsoft",
                    Description = $"У {snapshot.MicrosoftFallbackDriverCount} устройств найден базовый поставщик Microsoft. Иногда это нормально, но для GPU, чипсета и периферии лучше проверить драйвер производителя.",
                    ActionText = "Откройте раздел драйверов и проверьте устройства с базовым поставщиком Microsoft."
                };
            }

            if (!snapshot.DriverBackupExists)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "drivers.backup-missing",
                    Mode = DeviceDriverDashboardMode.Drivers,
                    Level = HealthLevel.Normal,
                    TargetGroupId = "drivers.tools",
                    TargetItemId = "drivers.tools.backup",
                    Title = "Бэкап драйверов ещё не создан",
                    Description = "Локальная папка резервных копий драйверов TweakWise пока пустая. Перед обновлением или удалением драйвера нужен экспорт пакетов.",
                    ActionText = "Перед изменением драйверов создайте резервную копию пакетов."
                };
            }

            if (snapshot.ProblemDeviceCount > 0)
            {
                var sample = devices.FirstOrDefault(device => device.ConfigManagerErrorCode.GetValueOrDefault() != 0);
                yield return new DeviceDriverFinding
                {
                    Id = "devices.problem",
                    Mode = DeviceDriverDashboardMode.Devices,
                    Level = HealthLevel.Warning,
                    TargetGroupId = problemDeviceTarget.GroupId,
                    TargetItemId = problemDeviceTarget.ItemId,
                    Title = "Есть устройства с ошибками",
                    Description = sample == null
                        ? $"Система сообщает о {snapshot.ProblemDeviceCount} устройствах с ошибками PnP."
                        : $"Система сообщает о {snapshot.ProblemDeviceCount} устройствах с ошибками PnP. Пример: {sample.Name}, код {sample.ConfigManagerErrorCode.GetValueOrDefault()}.",
                    ActionText = "Откройте раздел устройств, чтобы посмотреть код, Hardware ID и привязанный драйвер."
                };
            }

            if (snapshot.UnknownDeviceCount > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "devices.unknown",
                    Mode = DeviceDriverDashboardMode.Devices,
                    Level = HealthLevel.Warning,
                    TargetGroupId = unknownDeviceTarget.GroupId,
                    TargetItemId = unknownDeviceTarget.ItemId,
                    Title = "Есть неизвестные устройства",
                    Description = $"Обнаружено {snapshot.UnknownDeviceCount} устройств без понятного имени или класса. Для них нужен подбор драйвера по Hardware ID.",
                    ActionText = "Откройте раздел устройств и используйте VEN/DEV, VID/PID или ACPI ID для подбора драйвера."
                };
            }

            if (snapshot.OfflinePrinterCount > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "devices.printer-offline",
                    Mode = DeviceDriverDashboardMode.Devices,
                    Level = HealthLevel.Normal,
                    TargetGroupId = offlinePrinterTarget.GroupId,
                    TargetItemId = offlinePrinterTarget.ItemId,
                    Title = "Принтеры не в сети",
                    Description = $"Найдено {snapshot.OfflinePrinterCount} принтеров со статусом offline или неготовности. Для них можно показывать порт, драйвер и очередь.",
                    ActionText = "Откройте раздел устройств, чтобы посмотреть порт, драйвер и состояние принтера."
                };
            }

            var ippPrinters = printers.Count(IsGenericPrinterDriver);
            if (ippPrinters > 0)
            {
                yield return new DeviceDriverFinding
                {
                    Id = "devices.printer-generic-driver",
                    Mode = DeviceDriverDashboardMode.Devices,
                    Level = HealthLevel.Normal,
                    TargetGroupId = genericPrinterTarget.GroupId,
                    TargetItemId = genericPrinterTarget.ItemId,
                    Title = "Есть принтеры с универсальным драйвером",
                    Description = $"У {ippPrinters} принтеров используется универсальный или классовый драйвер. Для печати это допустимо, но фирменный драйвер может дать больше функций.",
                    ActionText = "Откройте раздел устройств, чтобы посмотреть текущий драйвер принтера."
                };
            }
        }

        private static IEnumerable<DeviceDriverGroupSnapshot> BuildDriverGroups(List<DriverRecord> drivers)
        {
            var groups = (drivers ?? new List<DriverRecord>())
                .Where(item => item != null)
                .Select(driver => new
                {
                    Category = ClassifyDriver(driver),
                    Item = CreateDriverInventoryItem(driver)
                })
                .GroupBy(item => item.Category.Id)
                .Select(group => CreateGroup(
                    DeviceDriverDashboardMode.Drivers,
                    group.First().Category,
                    group.Select(item => item.Item).ToList()));

            foreach (var group in groups)
                yield return group;

            yield return CreateDriverToolsGroup();
        }

        private static IEnumerable<DeviceDriverGroupSnapshot> BuildDeviceGroups(List<DeviceRecord> devices, List<PrinterRecord> printers)
        {
            var deviceItems = (devices ?? new List<DeviceRecord>())
                .Where(item => item != null)
                .Select(device => new
                {
                    Category = ClassifyDevice(device),
                    Item = CreateDeviceInventoryItem(device)
                });

            var printerItems = (printers ?? new List<PrinterRecord>())
                .Where(item => item != null)
                .Select(printer => new
                {
                    Category = DeviceCategoryMap.Printers,
                    Item = CreatePrinterInventoryItem(printer)
                });

            var groups = deviceItems.Concat(printerItems)
                .GroupBy(item => item.Category.Id)
                .Select(group => CreateGroup(
                    DeviceDriverDashboardMode.Devices,
                    group.First().Category,
                    group.Select(item => item.Item).ToList()));

            foreach (var group in groups)
                yield return group;

            yield return CreateDeviceToolsGroup();
        }

        private static DeviceDriverInventoryItem CreateDriverInventoryItem(DriverRecord driver)
        {
            HealthLevel level = HealthLevel.Good;
            string actionText = "Готов к проверке";

            if (driver.IsSigned == false)
            {
                level = HealthLevel.Warning;
                actionText = "Проверить подпись";
            }
            else if (IsOldNonMicrosoftDriver(driver))
            {
                level = HealthLevel.Normal;
                actionText = "Проверить обновление";
            }
            else if (IsMicrosoftFallbackDriver(driver))
            {
                level = HealthLevel.Normal;
                actionText = "Сверить с производителем";
            }

            string version = string.IsNullOrWhiteSpace(driver.Version) ? "версия неизвестна" : driver.Version;
            string date = driver.DriverDate.HasValue ? driver.DriverDate.Value.ToString("dd.MM.yyyy") : "дата неизвестна";
            string provider = FirstNotEmpty(driver.Provider, driver.Manufacturer, "поставщик неизвестен");
            string instanceId = driver.DeviceId ?? string.Empty;
            string searchQuery = BuildDriverSearchQuery(driver);
            string riskText = BuildDriverRiskText(driver);
            var actions = new List<DeviceDriverInventoryAction>
            {
                CreateAction(DeviceDriverInventoryActionKind.SearchOnline, "Найти драйвер", driver.DeviceName, searchQuery),
                CreateAction(DeviceDriverInventoryActionKind.InstallInf, "Установить INF", driver.DeviceName, searchQuery, infName: driver.InfName, requiresAdmin: true),
                CreateAction(DeviceDriverInventoryActionKind.RollbackFromBackup, "Откат из бэкапа", driver.DeviceName, searchQuery, infName: driver.InfName, requiresAdmin: true, isDestructive: true, riskText: riskText)
            };

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                actions.Add(CreateAction(DeviceDriverInventoryActionKind.RestartDevice, "Перезапустить", driver.DeviceName, searchQuery, instanceId, driver.InfName, requiresAdmin: true, riskText: riskText));
                if (CanDisableDevice(driver.DeviceClass, driver.DeviceName))
                    actions.Add(CreateAction(DeviceDriverInventoryActionKind.DisableDevice, "Отключить", driver.DeviceName, searchQuery, instanceId, driver.InfName, requiresAdmin: true, isDestructive: true, riskText: riskText));
            }

            return new DeviceDriverInventoryItem
            {
                Id = FirstNotEmpty(BuildDriverItemId(driver), Guid.NewGuid().ToString("N")),
                Kind = DeviceDriverInventoryItemKind.Driver,
                Level = level,
                Title = FirstNotEmpty(driver.DeviceName, driver.InfName, "Безымянный драйвер"),
                Description = provider,
                MetaText = $"{version} · {date} · {FirstNotEmpty(driver.InfName, "INF не указан")}",
                RiskText = riskText,
                ActionText = actionText,
                InstanceId = instanceId,
                InfName = driver.InfName ?? string.Empty,
                HardwareId = instanceId,
                SearchQuery = searchQuery,
                Actions = actions
            };
        }

        private static DeviceDriverInventoryItem CreateDeviceInventoryItem(DeviceRecord device)
        {
            int errorCode = device.ConfigManagerErrorCode.GetValueOrDefault();
            bool unknown = IsUnknownDevice(device);
            HealthLevel level = errorCode != 0 || unknown ? HealthLevel.Warning : HealthLevel.Good;
            string actionText = errorCode != 0
                ? $"Код PnP {errorCode}"
                : unknown
                    ? "Подобрать драйвер"
                    : "Работает штатно";
            string instanceId = device.PnpDeviceId ?? string.Empty;
            string hardwareId = FirstNotEmpty(device.HardwareId, device.PnpDeviceId);
            string riskText = BuildDeviceRiskText(device);
            string searchQuery = BuildDeviceSearchQuery(device);
            var actions = new List<DeviceDriverInventoryAction>
            {
                CreateAction(DeviceDriverInventoryActionKind.SearchOnline, unknown ? "Подобрать драйвер" : "Найти драйвер", device.Name, searchQuery, instanceId, searchQuery: searchQuery),
                CreateAction(DeviceDriverInventoryActionKind.InstallInf, "Установить INF", device.Name, searchQuery, instanceId, requiresAdmin: true)
            };

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                actions.Add(CreateAction(DeviceDriverInventoryActionKind.EnableDevice, "Включить", device.Name, searchQuery, instanceId, requiresAdmin: true, riskText: riskText));
                actions.Add(CreateAction(DeviceDriverInventoryActionKind.RestartDevice, "Перезапустить", device.Name, searchQuery, instanceId, requiresAdmin: true, riskText: riskText));
                if (CanDisableDevice(device.PnpClass, device.Name))
                    actions.Add(CreateAction(DeviceDriverInventoryActionKind.DisableDevice, "Отключить", device.Name, searchQuery, instanceId, requiresAdmin: true, isDestructive: true, riskText: riskText));
            }

            return new DeviceDriverInventoryItem
            {
                Id = FirstNotEmpty(BuildDeviceItemId(device), Guid.NewGuid().ToString("N")),
                Kind = DeviceDriverInventoryItemKind.Device,
                Level = level,
                Title = FirstNotEmpty(device.Name, "Неизвестное устройство"),
                Description = FirstNotEmpty(device.PnpClass, device.Service, device.ClassGuid, "класс не определён"),
                MetaText = FirstNotEmpty(device.Status, "статус неизвестен") + " · " + ShortenHardwareId(hardwareId),
                RiskText = riskText,
                ActionText = actionText,
                InstanceId = instanceId,
                HardwareId = hardwareId,
                SearchQuery = searchQuery,
                Actions = actions
            };
        }

        private static DeviceDriverInventoryItem CreatePrinterInventoryItem(PrinterRecord printer)
        {
            bool offline = IsOfflinePrinter(printer);
            return new DeviceDriverInventoryItem
            {
                Id = FirstNotEmpty(BuildPrinterItemId(printer), Guid.NewGuid().ToString("N")),
                Kind = DeviceDriverInventoryItemKind.Printer,
                Level = offline ? HealthLevel.Normal : HealthLevel.Good,
                Title = FirstNotEmpty(printer.Name, "Принтер без имени"),
                Description = FirstNotEmpty(printer.DriverName, "драйвер не определён"),
                MetaText = $"{FirstNotEmpty(printer.PortName, "порт не указан")} · {(printer.IsDefault == true ? "по умолчанию" : "обычный")}",
                RiskText = "Риск: низкий — действие ограничено очередью печати и драйвером принтера.",
                ActionText = offline ? "Проверить очередь" : "Готов к печати",
                SearchQuery = $"{printer.Name} {printer.DriverName} driver",
                Actions = new List<DeviceDriverInventoryAction>
                {
                    CreateAction(DeviceDriverInventoryActionKind.OpenPrinterQueue, "Очередь печати", printer.Name, printer.DriverName, printerName: printer.Name),
                    CreateAction(DeviceDriverInventoryActionKind.SearchOnline, "Найти драйвер", printer.Name, $"{printer.Name} {printer.DriverName} driver")
                }
            };
        }

        private static (string GroupId, string ItemId) FindDriverTarget(IEnumerable<DriverRecord> drivers, Func<DriverRecord, bool> predicate)
        {
            var driver = (drivers ?? Enumerable.Empty<DriverRecord>())
                .Where(item => item != null)
                .FirstOrDefault(item => predicate?.Invoke(item) == true);

            if (driver == null)
                return (string.Empty, string.Empty);

            return (ClassifyDriver(driver).Id, BuildDriverItemId(driver));
        }

        private static (string GroupId, string ItemId) FindDeviceTarget(IEnumerable<DeviceRecord> devices, Func<DeviceRecord, bool> predicate)
        {
            var device = (devices ?? Enumerable.Empty<DeviceRecord>())
                .Where(item => item != null)
                .FirstOrDefault(item => predicate?.Invoke(item) == true);

            if (device == null)
                return (string.Empty, string.Empty);

            return (ClassifyDevice(device).Id, BuildDeviceItemId(device));
        }

        private static (string GroupId, string ItemId) FindPrinterTarget(IEnumerable<PrinterRecord> printers, Func<PrinterRecord, bool> predicate)
        {
            var printer = (printers ?? Enumerable.Empty<PrinterRecord>())
                .Where(item => item != null)
                .FirstOrDefault(item => predicate?.Invoke(item) == true);

            if (printer == null)
                return (string.Empty, string.Empty);

            return (DeviceCategoryMap.Printers.Id, BuildPrinterItemId(printer));
        }

        private static string BuildDriverItemId(DriverRecord driver)
        {
            return FirstNotEmpty(driver?.InfName, driver?.DeviceId, driver?.DeviceName);
        }

        private static string BuildDeviceItemId(DeviceRecord device)
        {
            return FirstNotEmpty(device?.PnpDeviceId, device?.HardwareId, device?.Name);
        }

        private static string BuildPrinterItemId(PrinterRecord printer)
        {
            return FirstNotEmpty(printer?.Name, printer?.DriverName);
        }

        private static DeviceDriverGroupSnapshot CreateDriverToolsGroup()
        {
            var category = new DeviceDriverCategory("drivers.tools", 0, "\uE90F", "Инструменты драйверов", "Установка INF, резервная копия, откат из бэкапа и управление безопасным режимом.");
            var items = new List<DeviceDriverInventoryItem>
            {
                CreateToolItem(
                    "drivers.tools.install",
                    "Установить драйвер из INF",
                    "Запускает pnputil /add-driver /install для выбранного INF-пакета.",
                    "Риск: средний — устанавливайте только пакет от производителя устройства или из доверенного бэкапа.",
                    DeviceDriverInventoryActionKind.InstallInf,
                    "Выбрать INF",
                    requiresAdmin: true),
                CreateToolItem(
                    "drivers.tools.backup",
                    "Создать резервную копию драйверов",
                    "Экспортирует установленные драйверные пакеты в папку TweakWise через pnputil /export-driver.",
                    "Риск: низкий — операция только читает и копирует пакеты драйверов.",
                    DeviceDriverInventoryActionKind.BackupDrivers,
                    "Создать бэкап",
                    requiresAdmin: true),
                CreateToolItem(
                    "drivers.tools.rollback",
                    "Откатить драйвер из бэкапа",
                    "Позволяет выбрать INF из резервной копии и установить его через pnputil.",
                    "Риск: средний — перед откатом проверьте устройство, производителя и версию INF.",
                    DeviceDriverInventoryActionKind.RollbackFromBackup,
                    "Выбрать INF бэкапа",
                    requiresAdmin: true,
                    isDestructive: true),
                CreateToolItem(
                    "drivers.tools.safe-mode-on",
                    "Включить загрузку Safe Mode",
                    "Задаёт safeboot minimal для текущей записи загрузчика через bcdedit.",
                    "Риск: высокий — после применения следующая загрузка пойдёт в безопасный режим, отключите флаг после диагностики.",
                    DeviceDriverInventoryActionKind.EnableSafeMode,
                    "Включить Safe Mode",
                    requiresAdmin: true,
                    isDestructive: true),
                CreateToolItem(
                    "drivers.tools.safe-mode-off",
                    "Отключить загрузку Safe Mode",
                    "Удаляет safeboot из текущей записи загрузчика через bcdedit.",
                    "Риск: средний — используйте после завершения диагностики в безопасном режиме.",
                    DeviceDriverInventoryActionKind.DisableSafeMode,
                    "Отключить Safe Mode",
                    requiresAdmin: true)
            };

            return CreateGroup(DeviceDriverDashboardMode.Drivers, category, items);
        }

        private static DeviceDriverGroupSnapshot CreateDeviceToolsGroup()
        {
            var category = new DeviceDriverCategory("devices.tools", 0, "\uE90F", "Инструменты устройств", "Повторное сканирование PnP, диспетчер устройств, установка INF и Safe Mode для диагностики.");
            var items = new List<DeviceDriverInventoryItem>
            {
                CreateToolItem(
                    "devices.tools.scan",
                    "Повторно просканировать устройства",
                    "Запускает pnputil /scan-devices, чтобы Windows заново обнаружила PnP-устройства.",
                    "Риск: низкий — Windows только пересканирует подключённые устройства.",
                    DeviceDriverInventoryActionKind.ScanDevices,
                    "Сканировать",
                    requiresAdmin: true),
                CreateToolItem(
                    "devices.tools.manager",
                    "Открыть диспетчер устройств",
                    "Открывает штатный devmgmt.msc для ручной проверки свойств, отката и событий устройства.",
                    "Риск: низкий — открывается стандартная оснастка Windows.",
                    DeviceDriverInventoryActionKind.OpenDeviceManager,
                    "Открыть"),
                CreateToolItem(
                    "devices.tools.install",
                    "Установить драйвер для устройства",
                    "Позволяет выбрать INF-пакет и установить его через pnputil.",
                    "Риск: средний — выбирайте INF под конкретное устройство и разрядность Windows.",
                    DeviceDriverInventoryActionKind.InstallInf,
                    "Выбрать INF",
                    requiresAdmin: true),
                CreateToolItem(
                    "devices.tools.safe-mode-on",
                    "Загрузка Safe Mode для диагностики",
                    "Включает безопасный режим для следующей загрузки, если драйвер мешает обычному запуску.",
                    "Риск: высокий — заранее убедитесь, что знаете пароль локальной учётной записи.",
                    DeviceDriverInventoryActionKind.EnableSafeMode,
                    "Включить Safe Mode",
                    requiresAdmin: true,
                    isDestructive: true),
                CreateToolItem(
                    "devices.tools.safe-mode-off",
                    "Отключить Safe Mode",
                    "Снимает флаг безопасного режима после диагностики.",
                    "Риск: средний — возвращает обычную загрузку Windows.",
                    DeviceDriverInventoryActionKind.DisableSafeMode,
                    "Отключить Safe Mode",
                    requiresAdmin: true)
            };

            return CreateGroup(DeviceDriverDashboardMode.Devices, category, items);
        }

        private static DeviceDriverInventoryItem CreateToolItem(
            string id,
            string title,
            string description,
            string riskText,
            DeviceDriverInventoryActionKind actionKind,
            string actionLabel,
            bool requiresAdmin = false,
            bool isDestructive = false)
        {
            return new DeviceDriverInventoryItem
            {
                Id = id,
                Kind = DeviceDriverInventoryItemKind.Tool,
                Level = isDestructive ? HealthLevel.Normal : HealthLevel.Good,
                Title = title,
                Description = description,
                MetaText = "Системное действие через Windows",
                RiskText = riskText,
                ActionText = "Действие выполняется через штатные инструменты Windows.",
                Actions = new List<DeviceDriverInventoryAction>
                {
                    CreateAction(actionKind, actionLabel, title, description, requiresAdmin: requiresAdmin, isDestructive: isDestructive, riskText: riskText)
                }
            };
        }

        private static DeviceDriverInventoryAction CreateAction(
            DeviceDriverInventoryActionKind kind,
            string label,
            string title,
            string description = "",
            string instanceId = "",
            string infName = "",
            string searchQuery = "",
            string printerName = "",
            bool requiresAdmin = false,
            bool isDestructive = false,
            string riskText = "")
        {
            return new DeviceDriverInventoryAction
            {
                Kind = kind,
                Label = label,
                Title = FirstNotEmpty(title, label),
                Description = description ?? string.Empty,
                InstanceId = instanceId ?? string.Empty,
                InfName = infName ?? string.Empty,
                SearchQuery = FirstNotEmpty(searchQuery, description, title),
                PrinterName = printerName ?? string.Empty,
                RequiresAdmin = requiresAdmin,
                IsDestructive = isDestructive,
                RiskText = riskText ?? string.Empty
            };
        }

        private static DeviceDriverGroupSnapshot CreateGroup(
            DeviceDriverDashboardMode mode,
            DeviceDriverCategory category,
            List<DeviceDriverInventoryItem> items)
        {
            items ??= new List<DeviceDriverInventoryItem>();
            var orderedItems = items
                .Where(item => item != null)
                .OrderByDescending(item => GetSeverity(item.Level))
                .ThenBy(item => item.Title)
                .ToList();

            HealthLevel level = orderedItems
                .Select(item => item.Level)
                .DefaultIfEmpty(HealthLevel.Good)
                .OrderByDescending(GetSeverity)
                .First();

            int problems = orderedItems.Count(item => IsProblemLevel(item.Level));
            int recommendations = orderedItems.Count(item => item.Level == HealthLevel.Normal);

            return new DeviceDriverGroupSnapshot
            {
                Id = category.Id,
                Mode = mode,
                Order = category.Order,
                Level = level,
                Icon = category.Icon,
                Title = category.Title,
                Description = category.Description,
                Summary = BuildGroupSummary(orderedItems.Count, problems, recommendations),
                ItemCount = orderedItems.Count,
                ProblemCount = problems,
                RecommendationCount = recommendations,
                Items = orderedItems
            };
        }

        private static void EnsureFallbackGroups(DeviceDriverDiagnosticsSnapshot snapshot)
        {
            snapshot.DriverGroups ??= new List<DeviceDriverGroupSnapshot>();
            snapshot.DeviceGroups ??= new List<DeviceDriverGroupSnapshot>();
        }

        private static DeviceDriverCategory ClassifyDriver(DriverRecord driver)
        {
            string source = JoinSearchText(driver.DeviceName, driver.DeviceId, driver.DeviceClass, driver.Manufacturer, driver.Provider, driver.InfName);

            if (ContainsAny(source, "audio", "sound", "realtek", "hdaudio", "media", "аудио", "звук"))
                return DriverCategoryMap.Audio;
            if (ContainsAny(source, "display", "graphics", "video", "nvidia", "amd", "radeon", "intel(r) graphics", "видео", "граф"))
                return DriverCategoryMap.Graphics;
            if (ContainsAny(source, "bluetooth", "bt_", "bth", "блютуз"))
                return DriverCategoryMap.Bluetooth;
            if (ContainsAny(source, "net", "network", "wi-fi", "wifi", "wireless", "ethernet", "bluetooth", "qualcomm", "realtek pcie", "сеть"))
                return DriverCategoryMap.Network;
            if (ContainsAny(source, "usb", "usbhub", "usb\\", "xhci"))
                return DriverCategoryMap.Usb;
            if (ContainsAny(source, "camera", "webcam", "image", "avstream", "сенсор", "sensor"))
                return DriverCategoryMap.Media;
            if (ContainsAny(source, "printer", "print", "canon", "hp", "epson", "brother", "печать", "принтер"))
                return DriverCategoryMap.Print;
            if (ContainsAny(source, "hid", "keyboard", "mouse", "touchpad", "synaptics", "elan", "input", "клав", "мыш"))
                return DriverCategoryMap.Input;
            if (ContainsAny(source, "disk", "storage", "nvme", "sata", "raid", "scsi", "контроллер хранения", "накоп"))
                return DriverCategoryMap.Storage;
            if (ContainsAny(source, "security", "tpm", "smartcard", "credential", "biometric", "fingerprint"))
                return DriverCategoryMap.Security;
            if (ContainsAny(source, "chipset", "system", "pci", "acpi", "firmware", "intel", "amd", "чипсет", "система"))
                return DriverCategoryMap.System;

            return DriverCategoryMap.Other;
        }

        private static DeviceDriverCategory ClassifyDevice(DeviceRecord device)
        {
            string source = JoinSearchText(device.Name, device.PnpClass, device.Service, device.PnpDeviceId, device.ClassGuid);

            if (IsUnknownDevice(device))
                return DeviceCategoryMap.Unknown;
            if (ContainsAny(source, "printer", "print", "печать", "принтер"))
                return DeviceCategoryMap.Printers;
            if (ContainsAny(source, "usb", "hid", "keyboard", "mouse", "touch", "input", "клав", "мыш"))
                return DeviceCategoryMap.Usb;
            if (ContainsAny(source, "audio", "sound", "media", "bluetooth", "camera", "image", "аудио", "звук", "камера"))
                return DeviceCategoryMap.Media;
            if (ContainsAny(source, "net", "network", "wi-fi", "wifi", "wireless", "ethernet", "сеть"))
                return DeviceCategoryMap.Network;
            if (ContainsAny(source, "disk", "storage", "volume", "nvme", "sata", "накоп", "диск"))
                return DeviceCategoryMap.Storage;
            if (ContainsAny(source, "display", "monitor", "graphics", "video", "граф", "монитор"))
                return DeviceCategoryMap.Display;
            if (ContainsAny(source, "system", "pci", "acpi", "processor", "firmware", "система"))
                return DeviceCategoryMap.System;

            return DeviceCategoryMap.Other;
        }

        private static string BuildGroupSummary(int total, int problems, int recommendations)
        {
            if (problems > 0)
                return $"{total} элементов · {problems} требуют внимания";

            if (recommendations > 0)
                return $"{total} элементов · {recommendations} рекомендаций";

            return $"{total} элементов · в норме";
        }

        private static string BuildDriverSearchQuery(DriverRecord driver)
        {
            return JoinSearchText(
                FirstNotEmpty(driver.DeviceName, driver.DeviceClass),
                driver.Manufacturer,
                driver.Provider,
                driver.InfName,
                driver.Version,
                "driver");
        }

        private static string BuildDeviceSearchQuery(DeviceRecord device)
        {
            return JoinSearchText(
                FirstNotEmpty(device.HardwareId, device.PnpDeviceId),
                device.Name,
                device.PnpClass,
                "driver");
        }

        private static string BuildDriverRiskText(DriverRecord driver)
        {
            string source = JoinSearchText(driver.DeviceName, driver.DeviceClass, driver.Provider, driver.Manufacturer);

            if (ContainsAny(source, "storage", "disk", "nvme", "sata", "raid", "scsi", "acpi", "system", "firmware", "chipset", "pci"))
                return "Риск: высокий — драйвер влияет на загрузку, питание, шину PCI/ACPI или накопители.";

            if (ContainsAny(source, "display", "graphics", "video", "net", "network", "wi-fi", "wifi", "ethernet", "bluetooth"))
                return "Риск: средний — возможна временная потеря изображения, сети или беспроводных устройств.";

            if (ContainsAny(source, "keyboard", "mouse", "hid", "touchpad", "input"))
                return "Риск: средний — можно временно потерять устройство ввода.";

            if (driver.IsSigned == false)
                return "Риск: высокий — пакет без цифровой подписи нужно проверять перед установкой или откатом.";

            return "Риск: низкий — действие относится к отдельному драйверному пакету, но перед изменением нужен бэкап.";
        }

        private static string BuildDeviceRiskText(DeviceRecord device)
        {
            string source = JoinSearchText(device.Name, device.PnpClass, device.Service, device.PnpDeviceId, device.ClassGuid);

            if (ContainsAny(source, "system", "processor", "firmware", "acpi", "pci", "root", "volume", "disk", "storage", "nvme", "sata", "raid"))
                return "Риск: высокий — отключение может нарушить загрузку, питание, накопители или системную шину.";

            if (ContainsAny(source, "display", "graphics", "monitor", "net", "network", "wi-fi", "wifi", "ethernet", "bluetooth"))
                return "Риск: средний — можно временно потерять изображение, сеть или беспроводную связь.";

            if (ContainsAny(source, "keyboard", "mouse", "hid", "touchpad", "input"))
                return "Риск: средний — можно временно потерять устройство ввода.";

            if (IsUnknownDevice(device))
                return "Риск: средний — сначала проверьте Hardware ID и источник драйвера.";

            return "Риск: низкий — устройство не похоже на критичное, но отключение всё равно требует подтверждения.";
        }

        private static bool CanDisableDevice(string deviceClass, string deviceName)
        {
            string source = JoinSearchText(deviceClass, deviceName);

            if (ContainsAny(source, "system", "processor", "firmware", "acpi", "pci", "root", "volume", "disk", "storage", "nvme", "sata", "raid"))
                return false;

            return true;
        }

        private static string ShortenHardwareId(string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(hardwareId))
                return "Hardware ID не указан";

            return hardwareId.Length <= 44 ? hardwareId : hardwareId.Substring(0, 44) + "...";
        }

        private static bool IsOldNonMicrosoftDriver(DriverRecord driver)
        {
            if (!driver.DriverDate.HasValue)
                return false;

            if (ContainsAny(driver.Provider, "Microsoft") || ContainsAny(driver.Manufacturer, "Microsoft"))
                return false;

            return driver.DriverDate.Value < DateTime.Now.AddYears(-3);
        }

        private static bool IsMicrosoftFallbackDriver(DriverRecord driver)
        {
            if (!ContainsAny(driver.Provider, "Microsoft"))
                return false;

            return ContainsAny(driver.DeviceName, "display", "graphics", "video", "audio", "camera", "chipset", "bluetooth", "printer") ||
                   ContainsAny(driver.Manufacturer, "NVIDIA", "AMD", "Intel", "Realtek", "Qualcomm", "Broadcom", "Canon", "HP", "Epson", "Brother");
        }

        private static bool IsUnknownDevice(DeviceRecord device)
        {
            if (device.ConfigManagerErrorCode.GetValueOrDefault() != 0 && string.IsNullOrWhiteSpace(device.Service))
                return true;

            return ContainsAny(device.Name, "unknown", "неизвест") || ContainsAny(device.PnpDeviceId, "VEN_0000", "DEV_0000", "VID_0000", "PID_0000");
        }

        private static bool IsOfflinePrinter(PrinterRecord printer)
        {
            if (printer.WorkOffline == true)
                return true;

            int status = printer.PrinterStatus.GetValueOrDefault(3);
            return status != 0 && status != 3;
        }

        private static bool IsGenericPrinterDriver(PrinterRecord printer)
        {
            return ContainsAny(printer?.DriverName, "IPP", "Class Driver", "Microsoft");
        }

        private static string BuildDriverSummary(DeviceDriverDiagnosticsSnapshot snapshot)
        {
            if (snapshot.InstalledDriverCount == 0)
                return "Данные о драйверах ещё не получены";

            return $"{snapshot.InstalledDriverCount} драйверов · {snapshot.OldDriverCount} старых · {snapshot.UnsignedDriverCount} без подписи";
        }

        private static string BuildDeviceSummary(DeviceDriverDiagnosticsSnapshot snapshot)
        {
            if (snapshot.DeviceCount == 0)
                return "Данные об устройствах ещё не получены";

            return $"{snapshot.DeviceCount} устройств · {snapshot.ProblemDeviceCount} с ошибками · {snapshot.PrinterCount} принтеров";
        }

        public static string GetDriverBackupRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TweakWise", "DriverBackups");
        }

        public static string CreateDriverBackupFolder()
        {
            string folder = Path.Combine(GetDriverBackupRoot(), DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static DeviceDriverOperationResult SearchOnline(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return DeviceDriverOperationResult.Fail("Нет Hardware ID, INF или названия для поиска.");

            return OpenShellTarget($"https://www.google.com/search?q={Uri.EscapeDataString(query.Trim())}", "Открыт поиск драйвера в браузере.");
        }

        public static DeviceDriverOperationResult OpenDeviceManager()
        {
            return RunShellProcess("devmgmt.msc", string.Empty, "Открыт диспетчер устройств.", waitForExit: false);
        }

        public static DeviceDriverOperationResult OpenPrinterQueue(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return DeviceDriverOperationResult.Fail("Не удалось определить имя принтера.");

            return RunShellProcess(
                "rundll32.exe",
                $"printui.dll,PrintUIEntry /o /n {Quote(printerName)}",
                "Открыта очередь печати.",
                waitForExit: false);
        }

        public static DeviceDriverOperationResult InstallInf(string infPath)
        {
            if (string.IsNullOrWhiteSpace(infPath) || !File.Exists(infPath))
                return DeviceDriverOperationResult.Fail("INF-файл не найден.");

            return RunShellProcess(
                "pnputil.exe",
                $"/add-driver {Quote(infPath)} /install",
                "Команда установки INF отправлена в pnputil.",
                requiresAdmin: true);
        }

        public static DeviceDriverOperationResult BackupDrivers(string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(targetFolder))
                targetFolder = CreateDriverBackupFolder();
            else
                Directory.CreateDirectory(targetFolder);

            return RunShellProcess(
                "pnputil.exe",
                $"/export-driver * {Quote(targetFolder)}",
                $"Экспорт драйверов запущен в папку: {targetFolder}",
                requiresAdmin: true);
        }

        public static DeviceDriverOperationResult EnableDevice(string instanceId)
        {
            return RunPnpDeviceCommand("Enable-PnpDevice", instanceId, "Команда включения устройства отправлена Windows.");
        }

        public static DeviceDriverOperationResult DisableDevice(string instanceId)
        {
            return RunPnpDeviceCommand("Disable-PnpDevice", instanceId, "Команда отключения устройства отправлена Windows.");
        }

        public static DeviceDriverOperationResult RestartDevice(string instanceId)
        {
            return RunPnpDeviceCommand("Restart-PnpDevice", instanceId, "Команда перезапуска устройства отправлена Windows.");
        }

        public static DeviceDriverOperationResult ScanDevices()
        {
            return RunShellProcess(
                "pnputil.exe",
                "/scan-devices",
                "Сканирование PnP-устройств запущено.",
                requiresAdmin: true);
        }

        public static DeviceDriverOperationResult EnableSafeMode()
        {
            return RunShellProcess(
                "bcdedit.exe",
                "/set {current} safeboot minimal",
                "Safe Mode включён для текущей записи загрузчика. После диагностики отключите его в TweakWise.",
                requiresAdmin: true);
        }

        public static DeviceDriverOperationResult DisableSafeMode()
        {
            return RunShellProcess(
                "bcdedit.exe",
                "/deletevalue {current} safeboot",
                "Флаг Safe Mode удалён из текущей записи загрузчика.",
                requiresAdmin: true);
        }

        private static DeviceDriverOperationResult RunPnpDeviceCommand(string commandName, string instanceId, string successMessage)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return DeviceDriverOperationResult.Fail("Instance ID устройства не определён.");

            string command = $"{commandName} -InstanceId {ToPowerShellLiteral(instanceId)} -Confirm:$false";
            return RunShellProcess(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command {Quote(command)}",
                successMessage,
                requiresAdmin: true);
        }

        private static DeviceDriverOperationResult OpenShellTarget(string target, string successMessage)
        {
            return RunShellProcess(target, string.Empty, successMessage, waitForExit: false);
        }

        private static DeviceDriverOperationResult RunShellProcess(
            string fileName,
            string arguments,
            string successMessage,
            bool requiresAdmin = false,
            bool waitForExit = true)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = true,
                    WindowStyle = waitForExit ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                };

                if (requiresAdmin)
                    startInfo.Verb = "runas";

                using var process = Process.Start(startInfo);
                if (process == null)
                    return DeviceDriverOperationResult.Fail("Процесс не запустился.");

                if (waitForExit)
                {
                    if (!process.WaitForExit(60000))
                        return DeviceDriverOperationResult.Fail("Команда запущена, но не завершилась за 60 секунд. Проверьте окно UAC или системную оснастку.");

                    if (process.ExitCode != 0)
                        return DeviceDriverOperationResult.Fail($"Команда завершилась с кодом {process.ExitCode}.");
                }

                return DeviceDriverOperationResult.Ok(successMessage);
            }
            catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
            {
                return DeviceDriverOperationResult.Fail("Операция отменена пользователем в UAC.");
            }
            catch (Exception ex)
            {
                return DeviceDriverOperationResult.Fail(ex.Message);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string ToPowerShellLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\u0000", string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return tokens.Any(token => source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsProblemLevel(HealthLevel level)
        {
            return level == HealthLevel.Attention || level == HealthLevel.Warning || level == HealthLevel.Critical;
        }

        private static int GetSeverity(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Critical => 6,
                HealthLevel.Warning => 5,
                HealthLevel.Attention => 4,
                HealthLevel.Normal => 3,
                HealthLevel.Good => 2,
                HealthLevel.Checking => 1,
                _ => 0
            };
        }

        private static string FirstNotEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;

            return (values ?? Array.Empty<string>())
                .Select(NormalizeText)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string JoinSearchText(params string[] values)
        {
            return string.Join(" ", (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
                ? NormalizeText(value.ToString())
                : string.Empty;
        }

        private static IReadOnlyList<string> GetStringArray(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                return Array.Empty<string>();

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(item => NormalizeText(item.ToString()))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();
            }

            string scalar = NormalizeText(value.ToString());
            return string.IsNullOrWhiteSpace(scalar) ? Array.Empty<string>() : new[] { scalar };
        }

        private static bool? GetBool(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            return bool.TryParse(value.ToString(), out bool result) ? result : null;
        }

        private static int? GetInt(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return int.TryParse(value.ToString(), out int result) ? result : null;
        }

        private static DateTime? GetDate(JsonElement element, string property)
        {
            string value = GetString(element, property);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, out var parsed))
                return parsed;

            return null;
        }

        private sealed class DriverRecord
        {
            public string DeviceName { get; set; } = string.Empty;
            public string DeviceId { get; set; } = string.Empty;
            public string DeviceClass { get; set; } = string.Empty;
            public string Manufacturer { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string InfName { get; set; } = string.Empty;
            public DateTime? DriverDate { get; set; }
            public bool? IsSigned { get; set; }
        }

        private sealed class DeviceRecord
        {
            public string Name { get; set; } = string.Empty;
            public string PnpDeviceId { get; set; } = string.Empty;
            public string PnpClass { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Service { get; set; } = string.Empty;
            public string ClassGuid { get; set; } = string.Empty;
            public string HardwareId { get; set; } = string.Empty;
            public int? ConfigManagerErrorCode { get; set; }
        }

        private sealed class PrinterRecord
        {
            public string Name { get; set; } = string.Empty;
            public string DriverName { get; set; } = string.Empty;
            public string PortName { get; set; } = string.Empty;
            public bool? WorkOffline { get; set; }
            public bool? IsDefault { get; set; }
            public int? PrinterStatus { get; set; }
        }

        private sealed class DeviceDriverCategory
        {
            public DeviceDriverCategory(string id, int order, string icon, string title, string description)
            {
                Id = id;
                Order = order;
                Icon = icon;
                Title = title;
                Description = description;
            }

            public string Id { get; }
            public int Order { get; }
            public string Icon { get; }
            public string Title { get; }
            public string Description { get; }
        }

        private static class DriverCategoryMap
        {
            public static readonly DeviceDriverCategory Tools = new DeviceDriverCategory("drivers.tools", 0, "\uE90F", "Инструменты драйверов", "Установка INF, резервная копия, откат из бэкапа и Safe Mode");
            public static readonly DeviceDriverCategory Audio = new DeviceDriverCategory("drivers.audio", 10, "\uE8D6", "Звук", "Аудиокарты, кодеки и виртуальные аудиоустройства");
            public static readonly DeviceDriverCategory Graphics = new DeviceDriverCategory("drivers.graphics", 20, "\uE7F4", "Графика", "GPU, дисплеи и видеовывод");
            public static readonly DeviceDriverCategory Network = new DeviceDriverCategory("drivers.network", 30, "\uE839", "Сеть", "Ethernet, Wi-Fi и сетевые адаптеры");
            public static readonly DeviceDriverCategory Bluetooth = new DeviceDriverCategory("drivers.bluetooth", 35, "\uE702", "Bluetooth", "Bluetooth-радио, HID over GATT и беспроводная периферия");
            public static readonly DeviceDriverCategory Usb = new DeviceDriverCategory("drivers.usb", 38, "\uE88E", "USB", "USB-контроллеры, хабы и классовые USB-драйверы");
            public static readonly DeviceDriverCategory Print = new DeviceDriverCategory("drivers.print", 40, "\uE749", "Печать", "Принтеры, очереди и классовые драйверы");
            public static readonly DeviceDriverCategory Input = new DeviceDriverCategory("drivers.input", 50, "\uE765", "Ввод", "Клавиатуры, мыши, HID и тачпады");
            public static readonly DeviceDriverCategory Media = new DeviceDriverCategory("drivers.media", 55, "\uE8D6", "Камеры и медиа", "Камеры, захват видео, сенсоры и мультимедийные устройства");
            public static readonly DeviceDriverCategory Storage = new DeviceDriverCategory("drivers.storage", 60, "\uE958", "Накопители", "NVMe, SATA, RAID и контроллеры хранения");
            public static readonly DeviceDriverCategory Security = new DeviceDriverCategory("drivers.security", 65, "\uE72E", "Безопасность", "TPM, смарт-карты, биометрия и компоненты учётных данных");
            public static readonly DeviceDriverCategory System = new DeviceDriverCategory("drivers.system", 70, "\uE950", "Система", "Чипсет, PCI, ACPI и системные устройства");
            public static readonly DeviceDriverCategory Other = new DeviceDriverCategory("drivers.other", 900, "\uE9CE", "Другие", "Драйверы, которые не удалось уверенно отнести к группе");
        }

        private static class DeviceCategoryMap
        {
            public static readonly DeviceDriverCategory Tools = new DeviceDriverCategory("devices.tools", 0, "\uE90F", "Инструменты устройств", "Сканирование PnP, диспетчер устройств, установка INF и Safe Mode");
            public static readonly DeviceDriverCategory Printers = new DeviceDriverCategory("devices.printers", 10, "\uE749", "Принтеры", "Принтеры, драйверы печати, порты и очереди");
            public static readonly DeviceDriverCategory Usb = new DeviceDriverCategory("devices.usb", 20, "\uE88E", "USB и HID", "Периферия, хабы и устройства ввода");
            public static readonly DeviceDriverCategory Media = new DeviceDriverCategory("devices.media", 30, "\uE8D6", "Медиа", "Звук, камеры, Bluetooth и мультимедиа");
            public static readonly DeviceDriverCategory Network = new DeviceDriverCategory("devices.network", 40, "\uE839", "Сеть", "Сетевые адаптеры и беспроводные устройства");
            public static readonly DeviceDriverCategory Storage = new DeviceDriverCategory("devices.storage", 50, "\uE958", "Накопители", "Диски, тома и контроллеры хранения");
            public static readonly DeviceDriverCategory Display = new DeviceDriverCategory("devices.display", 60, "\uE7F4", "Дисплеи", "Мониторы, GPU и видеовывод");
            public static readonly DeviceDriverCategory System = new DeviceDriverCategory("devices.system", 70, "\uE950", "Системные", "Системные, PCI, ACPI и firmware-устройства");
            public static readonly DeviceDriverCategory Unknown = new DeviceDriverCategory("devices.unknown", 80, "\uE783", "Неизвестные", "Устройства без понятного класса или привязанного драйвера");
            public static readonly DeviceDriverCategory Other = new DeviceDriverCategory("devices.other", 900, "\uE9CE", "Другие", "Устройства, которые не удалось уверенно отнести к группе");
        }
    }
}
