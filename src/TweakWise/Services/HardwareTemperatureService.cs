using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using TweakWise.Models;

namespace TweakWise.Services
{
    public sealed class HardwareTemperatureService : IDisposable
    {
        private Computer _computer;
        private readonly HashSet<string> _faultedHardwareKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool _suppressHardwareBackend;
        private readonly bool _skipUnsafeHardwareUpdates;
        private readonly bool _forceLocalBackend;
        private bool _disposed;
        private bool _isOpen;

        public HardwareTemperatureService()
            : this(forceLocalBackend: false)
        {
        }

        internal HardwareTemperatureService(bool forceLocalBackend)
        {
            _forceLocalBackend = forceLocalBackend;
            _suppressHardwareBackend = forceLocalBackend ? false : ShouldSuppressLibreHardwareMonitor();
            _skipUnsafeHardwareUpdates = forceLocalBackend ? false : ShouldSkipUnsafeHardwareUpdates();
        }

        public IReadOnlyList<TemperatureSensorReading> GetTemperatures()
        {
            if (_disposed)
                return Array.Empty<TemperatureSensorReading>();

            if (!_forceLocalBackend && HardwareMonitorSafety.ShouldUseIsolatedTemperatureProbe())
                return TemperatureProbeRunner.ReadTemperaturesFromIsolatedProcess();

            if (_suppressHardwareBackend || !EnsureComputerOpened())
                return Array.Empty<TemperatureSensorReading>();

            try
            {
                var hardwareItems = GetRootHardware().ToList();

                foreach (var hardware in hardwareItems)
                {
                    try
                    {
                        UpdateHardwareRecursive(hardware);
                    }
                    catch
                    {
                        if (hardware != null)
                            _faultedHardwareKeys.Add(GetHardwareKey(hardware));
                    }
                }

                return hardwareItems
                    .SelectMany(FlattenHardware)
                    .SelectMany(ReadTemperatureSensors)
                    .OrderBy(sensor => GetGroupOrder(sensor.Group))
                    .ThenBy(sensor => sensor.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Array.Empty<TemperatureSensorReading>();
            }
        }

        private bool EnsureComputerOpened()
        {
            if (_isOpen)
                return true;

            try
            {
                _computer ??= CreateComputer();
                _computer.Open();
                _isOpen = true;
                return true;
            }
            catch
            {
                _isOpen = false;
                return false;
            }
        }

        private static bool ShouldSuppressLibreHardwareMonitor() => HardwareMonitorSafety.IsHardwareBackendSuppressed();

        private static bool ShouldSkipUnsafeHardwareUpdates() => HardwareMonitorSafety.ShouldSkipUnsafeHardwareUpdates();

        private static Computer CreateComputer()
        {
            return new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsMemoryEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false
            };
        }

        private IReadOnlyList<IHardware> GetRootHardware()
        {
            if (_computer == null || !_isOpen)
                return Array.Empty<IHardware>();

            try
            {
                return (_computer.Hardware ?? Array.Empty<IHardware>())
                    .Where(hardware => hardware != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<IHardware>();
            }
        }

        private IEnumerable<IHardware> FlattenHardware(IHardware hardware)
        {
            if (hardware == null)
                yield break;

            yield return hardware;

            foreach (var child in GetSafeSubHardware(hardware))
            {
                foreach (var nested in FlattenHardware(child))
                    yield return nested;
            }
        }

        private IEnumerable<TemperatureSensorReading> ReadTemperatureSensors(IHardware hardware)
        {
            if (hardware == null)
                yield break;

            string group = ClassifyGroup(hardware);
            string hardwareName = GetHardwareName(hardware);

            foreach (var sensor in GetSafeSensors(hardware))
            {
                if (sensor == null)
                    continue;

                if (!TryGetSensorType(sensor, out var sensorType) || sensorType != SensorType.Temperature)
                    continue;

                if (!TryGetSensorValue(sensor, out float value))
                    continue;

                string sensorName = GetSensorName(sensor);
                string title = BuildTitle(group, hardwareName, sensorName);

                yield return new TemperatureSensorReading
                {
                    Id = BuildStableId(group, hardwareName, sensorName),
                    Title = title,
                    Group = group,
                    ValueCelsius = value,
                    HardwareName = hardwareName,
                    SensorName = sensorName
                };
            }
        }

        [DebuggerNonUserCode]
        [DebuggerStepThrough]
        private void UpdateHardwareRecursive(IHardware hardware)
        {
            if (hardware == null)
                return;

            if (_skipUnsafeHardwareUpdates)
            {
                foreach (var child in GetSafeSubHardware(hardware))
                    UpdateHardwareRecursive(child);

                return;
            }

            string hardwareKey = GetHardwareKey(hardware);

            if (ShouldUpdateHardware(hardware) && !_faultedHardwareKeys.Contains(hardwareKey))
            {
                try
                {
                    hardware.Update();
                }
                catch
                {
                    _faultedHardwareKeys.Add(hardwareKey);
                    return;
                }
            }

            foreach (var child in GetSafeSubHardware(hardware))
                UpdateHardwareRecursive(child);
        }

        private static bool ShouldUpdateHardware(IHardware hardware)
        {
            if (!TryGetHardwareType(hardware, out var hardwareType))
                return false;

            return hardwareType == HardwareType.Cpu ||
                   hardwareType == HardwareType.GpuNvidia ||
                   hardwareType == HardwareType.GpuAmd ||
                   hardwareType == HardwareType.GpuIntel;
        }

        private static IReadOnlyList<IHardware> GetSafeSubHardware(IHardware hardware)
        {
            if (hardware == null)
                return Array.Empty<IHardware>();

            try
            {
                return (hardware.SubHardware ?? Array.Empty<IHardware>())
                    .Where(child => child != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<IHardware>();
            }
        }

        private static IReadOnlyList<ISensor> GetSafeSensors(IHardware hardware)
        {
            if (hardware == null)
                return Array.Empty<ISensor>();

            try
            {
                return (hardware.Sensors ?? Array.Empty<ISensor>())
                    .Where(sensor => sensor != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<ISensor>();
            }
        }

        private static bool TryGetHardwareType(IHardware hardware, out HardwareType hardwareType)
        {
            hardwareType = default;
            if (hardware == null)
                return false;

            try
            {
                hardwareType = hardware.HardwareType;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSensorType(ISensor sensor, out SensorType sensorType)
        {
            sensorType = default;
            if (sensor == null)
                return false;

            try
            {
                sensorType = sensor.SensorType;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSensorValue(ISensor sensor, out float value)
        {
            value = 0;
            if (sensor == null)
                return false;

            try
            {
                if (!sensor.Value.HasValue)
                    return false;

                value = sensor.Value.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetHardwareName(IHardware hardware)
        {
            if (hardware == null)
                return string.Empty;

            try
            {
                return hardware.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSensorName(ISensor sensor)
        {
            if (sensor == null)
                return string.Empty;

            try
            {
                return sensor.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetHardwareKey(IHardware hardware)
        {
            string type = TryGetHardwareType(hardware, out var hardwareType) ? hardwareType.ToString() : "Unknown";
            string name = GetHardwareName(hardware);
            return $"{type}:{name}";
        }

        private static string BuildTitle(string group, string hardwareName, string sensorName)
        {
            string shortName = (sensorName ?? string.Empty).Trim();
            string hardware = (hardwareName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(shortName))
                shortName = group;

            if (group == "Storage" && !string.IsNullOrWhiteSpace(hardware))
                return hardware;

            if (shortName.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0)
                return "CPU Package";

            if (shortName.IndexOf("hot spot", StringComparison.OrdinalIgnoreCase) >= 0)
                return "GPU Hot Spot";

            if (shortName.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0 && group == "Gpu")
                return "GPU Core";

            return shortName;
        }

        private static string BuildStableId(string group, string hardwareName, string sensorName)
        {
            string raw = $"{group}:{hardwareName}:{sensorName}".ToLowerInvariant();
            return new string(raw.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        }

        private static string ClassifyGroup(IHardware hardware)
        {
            if (hardware == null)
                return "Other";

            string type = TryGetHardwareType(hardware, out var hardwareType) ? hardwareType.ToString() : string.Empty;
            string name = GetHardwareName(hardware);
            string combined = $"{type} {name}";

            if (combined.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("processor", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Cpu";

            if (combined.IndexOf("gpu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("radeon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("geforce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("nvidia", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Gpu";

            if (combined.IndexOf("storage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("nvme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("ssd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("hdd", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Storage";

            if (combined.IndexOf("motherboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("superio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("chipset", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Motherboard";

            return "Other";
        }

        private static int GetGroupOrder(string group)
        {
            return group switch
            {
                "Cpu" => 0,
                "Gpu" => 1,
                "Motherboard" => 2,
                "Storage" => 3,
                _ => 4
            };
        }

        public static string FormatTemperature(float value)
        {
            return Math.Round(value).ToString("0", CultureInfo.CurrentCulture) + "°C";
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_isOpen)
                    _computer?.Close();
            }
            catch
            {
            }
        }
    }
}
