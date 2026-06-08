using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TweakWise.Models;

namespace TweakWise.Services
{
    internal static class TemperatureProbeRunner
    {
        private static readonly object ProbeSync = new object();
        private static IReadOnlyList<TemperatureSensorReading> _lastReadings = Array.Empty<TemperatureSensorReading>();
        private static DateTime _lastProbeUtc = DateTime.MinValue;
        private static DateTime _retryAfterUtc = DateTime.MinValue;

        public static bool IsProbeRequest(string[] args)
        {
            return args?.Any(HardwareMonitorSafety.IsTemperatureProbeArgument) == true;
        }

        public static int RunProbeAndWriteResult()
        {
            HardwareMonitorSafety.MarkTemperatureProbeProcess();

            try
            {
                using var service = new HardwareTemperatureService(forceLocalBackend: true);
                var readings = service.GetTemperatures() ?? Array.Empty<TemperatureSensorReading>();
                WriteStandardOutput(JsonSerializer.Serialize(readings));
                return 0;
            }
            catch
            {
                WriteStandardOutput("[]");
                return 2;
            }
        }

        public static IReadOnlyList<TemperatureSensorReading> ReadTemperaturesFromIsolatedProcess()
        {
            lock (ProbeSync)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastProbeUtc).TotalSeconds < 2)
                    return _lastReadings;

                if (now < _retryAfterUtc)
                    return _lastReadings;

                _lastProbeUtc = now;

                try
                {
                    string executablePath = GetExecutablePath();
                    if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                        return _lastReadings;

                    using var process = StartProbeProcess(executablePath);
                    if (process == null)
                        return _lastReadings;

                    if (!process.WaitForExit(4500))
                    {
                        TryKill(process);
                        _retryAfterUtc = DateTime.UtcNow.AddSeconds(8);
                        return _lastReadings;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();

                    if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                    {
                        _retryAfterUtc = DateTime.UtcNow.AddSeconds(8);
                        return _lastReadings;
                    }

                    var readings = ParseReadings(output);
                    _lastReadings = readings;
                    _retryAfterUtc = readings.Count == 0 && process.ExitCode != 0
                        ? DateTime.UtcNow.AddSeconds(8)
                        : DateTime.MinValue;

                    return _lastReadings;
                }
                catch
                {
                    _retryAfterUtc = DateTime.UtcNow.AddSeconds(8);
                    return _lastReadings;
                }
            }
        }

        private static Process StartProbeProcess(string executablePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = HardwareMonitorSafety.TemperatureProbeArgument,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.Environment["TW_TEMPERATURE_PROBE_PROCESS"] = "1";
            startInfo.Environment["TW_ALLOW_HARDWARE_MONITORING"] = "1";
            startInfo.Environment["TW_ALLOW_UNSAFE_HARDWARE_UPDATE"] = "1";

            return Process.Start(startInfo);
        }

        private static IReadOnlyList<TemperatureSensorReading> ParseReadings(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return Array.Empty<TemperatureSensorReading>();

            int start = output.IndexOf('[', StringComparison.Ordinal);
            int end = output.LastIndexOf(']');
            if (start < 0 || end < start)
                return Array.Empty<TemperatureSensorReading>();

            string json = output.Substring(start, end - start + 1);
            return JsonSerializer.Deserialize<List<TemperatureSensorReading>>(json) ?? new List<TemperatureSensorReading>();
        }

        private static void WriteStandardOutput(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "[]");
            using var output = Console.OpenStandardOutput();
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }

        private static string GetExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                return Environment.ProcessPath;

            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }
}
