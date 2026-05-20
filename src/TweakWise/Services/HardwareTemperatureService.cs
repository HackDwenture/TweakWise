using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using TweakWise.Models;

namespace TweakWise.Services
{
    public sealed class HardwareTemperatureService : IDisposable
    {
        private readonly Computer _computer;
        private bool _disposed;

        public HardwareTemperatureService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsMemoryEnabled = true,
                IsControllerEnabled = true
            };

            try
            {
                _computer.Open();
            }
            catch
            {
            }
        }

        public IReadOnlyList<TemperatureSensorReading> GetTemperatures()
        {
            if (_disposed)
                return Array.Empty<TemperatureSensorReading>();

            try
            {
                foreach (var hardware in _computer.Hardware)
                    UpdateHardwareRecursive(hardware);

                return _computer.Hardware
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

        private static IEnumerable<TemperatureSensorReading> ReadTemperatureSensors(IHardware hardware)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                    continue;

                string group = ClassifyGroup(hardware);
                string title = BuildTitle(group, hardware.Name, sensor.Name);
                float value = sensor.Value.Value;

                yield return new TemperatureSensorReading
                {
                    Id = BuildStableId(group, hardware.Name, sensor.Name),
                    Title = title,
                    Group = group,
                    ValueCelsius = value,
                    HardwareName = hardware.Name ?? string.Empty,
                    SensorName = sensor.Name ?? string.Empty
                };
            }
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
            string type = hardware.HardwareType.ToString();
            string name = hardware.Name ?? string.Empty;
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
                "Storage" => 2,
                "Motherboard" => 3,
                _ => 4
            };
        }

        private static IEnumerable<IHardware> FlattenHardware(IHardware hardware)
        {
            yield return hardware;

            foreach (var child in hardware.SubHardware)
            {
                foreach (var nested in FlattenHardware(child))
                    yield return nested;
            }
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            hardware.Update();

            foreach (var child in hardware.SubHardware)
                UpdateHardwareRecursive(child);
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
                _computer.Close();
            }
            catch
            {
            }
        }
    }
}
