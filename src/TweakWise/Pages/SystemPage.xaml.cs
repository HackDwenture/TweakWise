using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace TweakWise.Pages
{
    public partial class SystemPage : Page
    {
        private const string UnsupportedText = "Не поддерживается";

        private readonly DispatcherTimer _refreshTimer;
        private readonly Computer _computer;

        public SystemPage()
        {
            InitializeComponent();

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true
            };
            _computer.Open();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (_, _) => RefreshSystemInfo();

            Loaded += SystemPage_Loaded;
            Unloaded += SystemPage_Unloaded;
        }

        private void SystemPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshSystemInfo();
            _refreshTimer.Start();
        }

        private void SystemPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
        }

        private void RefreshSystemInfo()
        {
            UpdateHardwareTree();

            var hardware = GetHardware().ToList();
            var allSensors = hardware.SelectMany(GetSensors).ToList();
            var allTemperatureSensors = allSensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature)
                .OrderBy(sensor => GetTemperaturePriority(sensor))
                .ThenBy(sensor => sensor.Name)
                .ToList();

            var cpuHardware = hardware.FirstOrDefault(item => item.HardwareType == HardwareType.Cpu);
            var gpuHardware = hardware.FirstOrDefault(item =>
                item.HardwareType == HardwareType.GpuNvidia ||
                item.HardwareType == HardwareType.GpuAmd ||
                item.HardwareType == HardwareType.GpuIntel);
            var storageHardware = hardware.Where(item => item.HardwareType == HardwareType.Storage).ToList();

            var memory = GetMemoryStatus();
            var usedMemory = memory.ullTotalPhys > memory.ullAvailPhys
                ? memory.ullTotalPhys - memory.ullAvailPhys
                : 0;

            CpuNameTextBlock.Text = cpuHardware?.Name ?? UnsupportedText;
            CpuTemperatureTextBlock.Text = $"Температура: {FormatSensorValue(GetPreferredSensor(GetSensors(cpuHardware), SensorType.Temperature, "Package", "Tctl/Tdie", "CCD", "Core Max", "Core"))}";
            CpuLoadTextBlock.Text = $"Загрузка: {FormatSensorValue(GetPreferredSensor(GetSensors(cpuHardware), SensorType.Load, "Total", "CPU Total"))}";
            CpuClockTextBlock.Text = $"Частота: {FormatSensorValue(GetPreferredSensor(GetSensors(cpuHardware), SensorType.Clock, "Core Average", "Effective Clock", "Core #1", "Bus Speed"))}";
            CpuPowerTextBlock.Text = $"Потребление: {FormatSensorValue(GetPreferredSensor(GetSensors(cpuHardware), SensorType.Power, "Package", "CPU Package"))}";

            GpuNameTextBlock.Text = gpuHardware?.Name ?? UnsupportedText;
            GpuTemperatureTextBlock.Text = $"Температура: {FormatSensorValue(GetPreferredSensor(GetSensors(gpuHardware), SensorType.Temperature, "Hot Spot", "Core", "GPU Core", "Memory"))}";
            GpuLoadTextBlock.Text = $"Загрузка: {FormatSensorValue(GetPreferredSensor(GetSensors(gpuHardware), SensorType.Load, "Core", "D3D", "GPU Core"))}";
            GpuClockTextBlock.Text = $"Частота ядра: {FormatSensorValue(GetPreferredSensor(GetSensors(gpuHardware), SensorType.Clock, "Core", "Graphics", "GPU Core"))}";
            GpuMemoryTextBlock.Text = $"Видеопамять: {BuildGpuMemoryText(gpuHardware)}";
            GpuFanTextBlock.Text = $"Вентилятор GPU: {FormatSensorValue(GetPreferredSensor(GetSensors(gpuHardware), SensorType.Fan, "Fan"))}";

            MemorySummaryTextBlock.Text = memory.ullTotalPhys == 0
                ? UnsupportedText
                : $"ОЗУ: {FormatBytes(usedMemory)} / {FormatBytes(memory.ullTotalPhys)} ({memory.dwMemoryLoad}%)";
            StorageSummaryTextBlock.Text = BuildStorageSummary(storageHardware);
            TopStorageTemperatureTextBlock.Text = $"Температура накопителей: {BuildTopStorageTemperature(allTemperatureSensors)}";
            UptimeTextBlock.Text = $"Аптайм: {FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64))}";
            LastRefreshTextBlock.Text = $"Обновлено: {DateTime.Now:HH:mm:ss}";

            var fanSensors = allSensors.Where(sensor => sensor.SensorType == SensorType.Fan).ToList();
            FanSummaryTextBlock.Text = BuildFanSummary(fanSensors);
            FanControlStatusTextBlock.Text = BuildFanControlStatus(allSensors);
            GpuOverclockStatusTextBlock.Text = BuildGpuOverclockStatus(gpuHardware);
            GpuTuningStatusTextBlock.Text = BuildGpuTuningStatus(gpuHardware, allSensors);

            bool tuningSupported = IsManualTuningSupported(gpuHardware, allSensors);
            ApplyGpuTuningButton.IsEnabled = tuningSupported;
            ResetGpuTuningButton.IsEnabled = tuningSupported;
            GpuCoreOffsetTextBox.IsEnabled = tuningSupported;
            GpuMemoryOffsetTextBox.IsEnabled = tuningSupported;
            GpuFanTargetTextBox.IsEnabled = tuningSupported;

            SystemDetailsItemsControl.ItemsSource = BuildSystemDetails(cpuHardware, gpuHardware, memory);
            StorageItemsControl.ItemsSource = BuildStorageItems(storageHardware, allTemperatureSensors);
            CpuSensorItemsControl.ItemsSource = BuildSensorEntries(cpuHardware, SensorScope.Cpu);
            GpuSensorItemsControl.ItemsSource = BuildSensorEntries(gpuHardware, SensorScope.Gpu);
            CoolingSensorItemsControl.ItemsSource = BuildCoolingEntries(allSensors, storageHardware);
            TemperatureSensorItemsControl.ItemsSource = BuildTemperatureEntries(allTemperatureSensors);
        }

        private IEnumerable<IHardware> GetHardware()
        {
            return _computer.Hardware.SelectMany(FlattenHardware);
        }

        private IEnumerable<IHardware> FlattenHardware(IHardware hardware)
        {
            yield return hardware;

            foreach (var child in hardware.SubHardware)
            {
                foreach (var subHardware in FlattenHardware(child))
                    yield return subHardware;
            }
        }

        private IEnumerable<ISensor> GetSensors(IHardware hardware)
        {
            if (hardware == null)
                return Enumerable.Empty<ISensor>();

            var directSensors = hardware.Sensors ?? Array.Empty<ISensor>();
            var subSensors = hardware.SubHardware?.SelectMany(GetSensors) ?? Enumerable.Empty<ISensor>();
            return directSensors.Concat(subSensors);
        }

        private void UpdateHardwareTree()
        {
            foreach (var hardware in _computer.Hardware)
                UpdateHardwareRecursive(hardware);
        }

        private void UpdateHardwareRecursive(IHardware hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware)
                UpdateHardwareRecursive(child);
        }

        private List<InfoRow> BuildSystemDetails(IHardware cpuHardware, IHardware gpuHardware, MemoryStatusEx memory)
        {
            return new List<InfoRow>
            {
                new() { Label = "Устройство", Value = Environment.MachineName },
                new() { Label = "Пользователь", Value = Environment.UserName },
                new() { Label = "Windows", Value = GetWindowsVersionText() },
                new() { Label = "Архитектура", Value = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit" },
                new() { Label = "Права", Value = IsAdministrator() ? "Администратор" : "Обычный пользователь" },
                new() { Label = "Процессор", Value = cpuHardware?.Name ?? UnsupportedText },
                new() { Label = "Потоки CPU", Value = Environment.ProcessorCount.ToString() },
                new() { Label = "Материнская плата", Value = GetMotherboardText() },
                new() { Label = "BIOS", Value = GetBiosVersionText() },
                new() { Label = "Видеокарта", Value = gpuHardware?.Name ?? UnsupportedText },
                new() { Label = "ОЗУ", Value = memory.ullTotalPhys == 0 ? UnsupportedText : FormatBytes(memory.ullTotalPhys) }
            };
        }

        private List<InfoRow> BuildStorageItems(List<IHardware> storageHardware, List<ISensor> allTemperatureSensors)
        {
            var driveRows = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new InfoRow
                {
                    Label = $"{drive.Name} {drive.DriveFormat}",
                    Value = $"Свободно {FormatBytes((ulong)drive.AvailableFreeSpace)} из {FormatBytes((ulong)drive.TotalSize)}"
                })
                .ToList();

            foreach (var storage in storageHardware)
            {
                var sensors = GetSensors(storage).ToList();
                var temp = FormatSensorValue(GetPreferredSensor(sensors, SensorType.Temperature, "Temperature", "Assembly"));
                var load = FormatSensorValue(GetPreferredSensor(sensors, SensorType.Load, "Used Space", "Activity", "Total Activity"));

                driveRows.Add(new InfoRow
                {
                    Label = storage.Name,
                    Value = $"Температура: {temp}; Активность/занятость: {load}"
                });
            }

            var extraStorageTemps = allTemperatureSensors
                .Where(IsLikelyStorageTemperature)
                .Where(sensor => !driveRows.Any(row => row.Label.Contains(sensor.Hardware?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                .Take(4)
                .Select(sensor => new InfoRow
                {
                    Label = sensor.Hardware?.Name ?? sensor.Name,
                    Value = $"Температура: {FormatSensorValue(sensor)}"
                });

            driveRows.AddRange(extraStorageTemps);

            if (driveRows.Count == 0)
            {
                driveRows.Add(new InfoRow
                {
                    Label = "Накопители",
                    Value = UnsupportedText
                });
            }

            return driveRows;
        }

        private List<SensorRow> BuildSensorEntries(IHardware hardware, SensorScope scope)
        {
            var sensors = GetSensors(hardware).ToList();
            var rows = new List<SensorRow>();

            if (scope == SensorScope.Cpu)
            {
                rows.Add(BuildSensorRow("Температура Package", GetPreferredSensor(sensors, SensorType.Temperature, "Package", "Tctl/Tdie", "CCD")));
                rows.Add(BuildSensorRow("Нагрузка", GetPreferredSensor(sensors, SensorType.Load, "Total", "CPU Total")));
                rows.Add(BuildSensorRow("Частота", GetPreferredSensor(sensors, SensorType.Clock, "Core Average", "Effective Clock", "Bus Speed")));
                rows.Add(BuildSensorRow("Потребление", GetPreferredSensor(sensors, SensorType.Power, "Package", "CPU Package")));
                rows.Add(BuildSensorRow("Ядро / CCD", GetPreferredSensor(sensors, SensorType.Temperature, "Core #1", "CCD #1", "Core Max")));
            }
            else
            {
                rows.Add(BuildSensorRow("Температура ядра", GetPreferredSensor(sensors, SensorType.Temperature, "Hot Spot", "Core", "GPU Core")));
                rows.Add(BuildSensorRow("Температура памяти", GetPreferredSensor(sensors, SensorType.Temperature, "Memory", "VRAM")));
                rows.Add(BuildSensorRow("Нагрузка GPU", GetPreferredSensor(sensors, SensorType.Load, "Core", "D3D", "GPU Core")));
                rows.Add(BuildSensorRow("Частота ядра", GetPreferredSensor(sensors, SensorType.Clock, "Core", "Graphics", "GPU Core")));
                rows.Add(BuildSensorRow("Частота памяти", GetPreferredSensor(sensors, SensorType.Clock, "Memory")));
                rows.Add(BuildSensorRow("Память занята", GetPreferredSensor(sensors, SensorType.SmallData, "Memory Used", "GPU Memory Used")));
                rows.Add(BuildSensorRow("Потребление", GetPreferredSensor(sensors, SensorType.Power, "Power", "Board")));
            }

            if (rows.All(row => row.Value == UnsupportedText))
            {
                return new List<SensorRow>
                {
                    new() { Label = "Датчики", Value = UnsupportedText }
                };
            }

            return rows;
        }

        private List<SensorRow> BuildCoolingEntries(List<ISensor> allSensors, List<IHardware> storageHardware)
        {
            var rows = allSensors
                .Where(sensor => sensor.SensorType == SensorType.Fan)
                .Take(6)
                .Select(sensor => new SensorRow
                {
                    Label = sensor.Name,
                    Value = FormatSensorValue(sensor)
                })
                .ToList();

            foreach (var storage in storageHardware.Take(4))
            {
                rows.Add(new SensorRow
                {
                    Label = $"{storage.Name} температура",
                    Value = FormatSensorValue(GetPreferredSensor(GetSensors(storage), SensorType.Temperature, "Temperature", "Assembly"))
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new SensorRow
                {
                    Label = "Охлаждение",
                    Value = UnsupportedText
                });
            }

            return rows;
        }

        private List<SensorRow> BuildTemperatureEntries(List<ISensor> temperatureSensors)
        {
            if (temperatureSensors.Count == 0)
            {
                return new List<SensorRow>
                {
                    new() { Label = "Температуры", Value = UnsupportedText }
                };
            }

            return temperatureSensors
                .Take(20)
                .Select(sensor => new SensorRow
                {
                    Label = BuildTemperatureLabel(sensor),
                    Value = FormatSensorValue(sensor)
                })
                .ToList();
        }

        private static string BuildTemperatureLabel(ISensor sensor)
        {
            string category = IsLikelyCpuTemperature(sensor) ? "CPU"
                : IsLikelyGpuTemperature(sensor) ? "GPU"
                : IsLikelyStorageTemperature(sensor) ? "Диск"
                : "Система";

            return $"{category}: {GetHardwareLabel(sensor.Hardware)} / {sensor.Name}";
        }

        private static int GetTemperaturePriority(ISensor sensor)
        {
            if (IsLikelyCpuTemperature(sensor))
                return 0;

            if (IsLikelyGpuTemperature(sensor))
                return 1;

            if (IsLikelyStorageTemperature(sensor))
                return 2;

            return 3;
        }

        private static bool IsLikelyCpuTemperature(ISensor sensor)
        {
            string source = $"{sensor.Hardware?.Name} {sensor.Name} {sensor.Hardware?.HardwareType}";
            return source.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("CCD", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("Tctl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyGpuTemperature(ISensor sensor)
        {
            string source = $"{sensor.Hardware?.Name} {sensor.Name} {sensor.Hardware?.HardwareType}";
            return source.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("VRAM", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("Graphics", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyStorageTemperature(ISensor sensor)
        {
            if (sensor.Hardware?.HardwareType == HardwareType.Storage)
                return true;

            string source = $"{sensor.Hardware?.Name} {sensor.Name}";
            string[] tokens =
            {
                "SSD", "HDD", "NVME", "NVMe", "Drive", "Disk", "Samsung", "WDC", "WD ", "Kingston",
                "Crucial", "Seagate", "Toshiba", "ADATA", "Micron", "SanDisk", "M.2"
            };

            return tokens.Any(token => source.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetHardwareLabel(IHardware hardware)
        {
            if (hardware == null)
                return "Датчик";

            if (!string.IsNullOrWhiteSpace(hardware.Name))
                return hardware.Name;

            return hardware.HardwareType.ToString();
        }

        private static SensorRow BuildSensorRow(string label, ISensor sensor)
        {
            return new SensorRow
            {
                Label = label,
                Value = FormatSensorValue(sensor)
            };
        }

        private static ISensor GetPreferredSensor(IEnumerable<ISensor> sensors, SensorType sensorType, params string[] preferredTokens)
        {
            var filtered = sensors?.Where(sensor => sensor.SensorType == sensorType).ToList() ?? new List<ISensor>();

            foreach (var token in preferredTokens)
            {
                var match = filtered.FirstOrDefault(sensor => sensor.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return match;
            }

            return filtered.FirstOrDefault();
        }

        private static string BuildGpuMemoryText(IHardware gpuHardware)
        {
            var sensors = gpuHardware == null ? new List<ISensor>() : gpuHardware.SubHardware.SelectMany(GetSubSensors).Concat(gpuHardware.Sensors).ToList();
            var used = GetPreferredSensor(sensors, SensorType.SmallData, "Memory Used", "GPU Memory Used");
            var total = GetPreferredSensor(sensors, SensorType.SmallData, "Memory Total", "GPU Memory Total");

            if (used?.Value != null && total?.Value != null)
                return $"{used.Value:0.#} / {total.Value:0.#} MB";

            if (used?.Value != null)
                return $"{used.Value:0.#} MB";

            return UnsupportedText;
        }

        private static IEnumerable<ISensor> GetSubSensors(IHardware hardware)
        {
            if (hardware == null)
                return Enumerable.Empty<ISensor>();

            return hardware.Sensors.Concat(hardware.SubHardware.SelectMany(GetSubSensors));
        }

        private static string BuildStorageSummary(List<IHardware> storageHardware)
        {
            var readyDrives = DriveInfo.GetDrives().Where(drive => drive.IsReady).ToList();
            if (storageHardware.Count == 0 && readyDrives.Count == 0)
                return UnsupportedText;

            var driveSummary = readyDrives.Count == 0
                ? string.Empty
                : $"{readyDrives.Count} логических диска";

            var hardwareSummary = storageHardware.Count == 0
                ? string.Empty
                : $"{storageHardware.Count} аппаратных накопителя";

            return string.Join(" • ", new[] { driveSummary, hardwareSummary }.Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private static string BuildTopStorageTemperature(List<ISensor> temperatureSensors)
        {
            var temperatures = temperatureSensors
                .Where(IsLikelyStorageTemperature)
                .Select(sensor => $"{GetHardwareLabel(sensor.Hardware)}: {FormatSensorValue(sensor)}")
                .Take(3)
                .ToList();

            return temperatures.Count == 0 ? UnsupportedText : string.Join(" | ", temperatures);
        }

        private static string BuildFanSummary(List<ISensor> fanSensors)
        {
            if (fanSensors.Count == 0)
                return "Скорости вентиляторов: Не поддерживается";

            return $"Скорости вентиляторов: {string.Join(" | ", fanSensors.Take(3).Select(sensor => $"{sensor.Name}: {FormatSensorValue(sensor)}"))}";
        }

        private static string BuildFanControlStatus(List<ISensor> allSensors)
        {
            bool hasControlSensors = allSensors.Any(sensor => sensor.SensorType == SensorType.Control);
            return hasControlSensors
                ? "Контроллеры или управляющие каналы обнаружены. В этой версии доступна безопасная диагностика, а запись параметров включается только при подтверждённом backend."
                : "Не поддерживается: материнская плата, EC-контроллер или драйвер не дают универсальный доступ к ручному управлению вентиляторами.";
        }

        private static string BuildGpuOverclockStatus(IHardware gpuHardware)
        {
            return gpuHardware == null
                ? "Не поддерживается: совместимая видеокарта с доступными датчиками не обнаружена."
                : "Параметры мониторинга доступны. Прямой разгон частот требует vendor-specific backend и не может быть безопасно включён универсально для всех ПК.";
        }

        private static bool IsManualTuningSupported(IHardware gpuHardware, List<ISensor> allSensors)
        {
            if (gpuHardware == null)
                return false;

            return allSensors.Any(sensor => sensor.SensorType == SensorType.Control);
        }

        private static string BuildGpuTuningStatus(IHardware gpuHardware, List<ISensor> allSensors)
        {
            if (gpuHardware == null)
                return "Не поддерживается: GPU с доступным backend не найден.";

            return allSensors.Any(sensor => sensor.SensorType == SensorType.Control)
                ? "Обнаружены управляющие каналы. Интерфейс готов, но запись частот и вентиляторов ограничена безопасным режимом этой сборки."
                : "Не поддерживается: библиотека видит датчики GPU, но не получает безопасный write-доступ к частотам и вентиляторам.";
        }

        private static string FormatSensorValue(ISensor sensor)
        {
            if (sensor?.Value == null)
                return UnsupportedText;

            return sensor.SensorType switch
            {
                SensorType.Temperature => $"{sensor.Value:0.#} °C",
                SensorType.Fan => $"{sensor.Value:0} RPM",
                SensorType.Load => $"{sensor.Value:0.#} %",
                SensorType.Clock => $"{sensor.Value:0} MHz",
                SensorType.Control => $"{sensor.Value:0.#} %",
                SensorType.Power => $"{sensor.Value:0.#} W",
                SensorType.Data => $"{sensor.Value:0.#} GB",
                SensorType.SmallData => $"{sensor.Value:0.#} MB",
                SensorType.Throughput => $"{sensor.Value:0.#} MB/s",
                _ => sensor.Value.Value.ToString("0.##")
            };
        }

        private static string GetWindowsVersionText()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string productName = key?.GetValue("ProductName")?.ToString() ?? "Windows";
                string displayVersion = key?.GetValue("DisplayVersion")?.ToString();
                string build = key?.GetValue("CurrentBuild")?.ToString();

                string result = productName;
                if (!string.IsNullOrWhiteSpace(displayVersion))
                    result += $" {displayVersion}";
                if (!string.IsNullOrWhiteSpace(build))
                    result += $" (build {build})";

                return result;
            }
            catch
            {
                return Environment.OSVersion.VersionString;
            }
        }

        private static string GetMotherboardText()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var manufacturer = key?.GetValue("BaseBoardManufacturer")?.ToString();
                var product = key?.GetValue("BaseBoardProduct")?.ToString();

                var result = string.Join(" ", new[] { manufacturer, product }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return string.IsNullOrWhiteSpace(result) ? UnsupportedText : result;
            }
            catch
            {
                return UnsupportedText;
            }
        }

        private static string GetBiosVersionText()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var vendor = key?.GetValue("BIOSVendor")?.ToString();
                var version = key?.GetValue("BIOSVersion")?.ToString();
                var releaseDate = key?.GetValue("BIOSReleaseDate")?.ToString();

                var result = string.Join(" ", new[] { vendor, version, releaseDate }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return string.IsNullOrWhiteSpace(result) ? UnsupportedText : result;
            }
            catch
            {
                return UnsupportedText;
            }
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

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays} д {uptime.Hours} ч {uptime.Minutes} мин";

            return $"{uptime.Hours} ч {uptime.Minutes} мин";
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double value = bytes;
            int suffixIndex = 0;

            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:0.#} {suffixes[suffixIndex]}";
        }

        private void RefreshSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            RefreshSystemInfo();
        }

        private void ApplyGpuTuning_Click(object sender, RoutedEventArgs e)
        {
            App.DialogManager?.Show(
                Application.Current.MainWindow,
                "Параметры GPU",
                "Ручное применение пока ограничено",
                "В этой сборке включён мониторинг и детекция поддержки, но универсальное безопасное применение offset/частот ещё не реализовано для всех GPU одинаково.",
                AppDialogKind.Info);
        }

        private void ResetGpuTuning_Click(object sender, RoutedEventArgs e)
        {
            GpuCoreOffsetTextBox.Text = string.Empty;
            GpuMemoryOffsetTextBox.Text = string.Empty;
            GpuFanTargetTextBox.Text = string.Empty;
        }

        private void OpenTaskManager_Click(object sender, RoutedEventArgs e) => OpenTool("taskmgr.exe");
        private void OpenResourceMonitor_Click(object sender, RoutedEventArgs e) => OpenTool("resmon.exe");
        private void OpenDeviceManager_Click(object sender, RoutedEventArgs e) => OpenTool("devmgmt.msc");
        private void OpenSystemInformation_Click(object sender, RoutedEventArgs e) => OpenTool("msinfo32.exe");
        private void OpenSystemSettings_Click(object sender, RoutedEventArgs e) => OpenTool("ms-settings:about");

        private void OpenTool(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.DialogManager?.Show(
                    Application.Current.MainWindow,
                    "Ошибка запуска",
                    "Не удалось открыть системный инструмент.",
                    ex.Message,
                    AppDialogKind.Error);
            }
        }

        private static MemoryStatusEx GetMemoryStatus()
        {
            var status = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(status))
                return new MemoryStatusEx();

            return status;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

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

        private sealed class InfoRow
        {
            public string Label { get; set; }
            public string Value { get; set; }
        }

        private sealed class SensorRow
        {
            public string Label { get; set; }
            public string Value { get; set; }
        }

        private enum SensorScope
        {
            Cpu,
            Gpu
        }
    }
}
