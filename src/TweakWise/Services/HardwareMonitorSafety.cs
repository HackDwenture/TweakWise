using System;

namespace TweakWise.Services
{
    internal static class HardwareMonitorSafety
    {
        public const string TemperatureProbeArgument = "--tw-temperature-probe";
        private const string TemperatureProbeProcessVariable = "TW_TEMPERATURE_PROBE_PROCESS";

        public static bool IsHardwareBackendSuppressed()
        {
            string suppressHardware = Environment.GetEnvironmentVariable("TW_SUPPRESS_HARDWARE_MONITORING") ?? string.Empty;
            if (IsTruthy(suppressHardware))
                return true;

            if (IsTemperatureProbeProcess())
                return false;

            string allowHardware = Environment.GetEnvironmentVariable("TW_ALLOW_HARDWARE_MONITORING") ?? string.Empty;
            if (IsTruthy(allowHardware))
                return false;

            return true;
        }

        public static bool ShouldUseIsolatedTemperatureProbe()
        {
            if (IsTemperatureProbeProcess())
                return false;

            string suppressHardware = Environment.GetEnvironmentVariable("TW_SUPPRESS_HARDWARE_MONITORING") ?? string.Empty;
            if (IsTruthy(suppressHardware))
                return false;

            string directHardware = Environment.GetEnvironmentVariable("TW_ALLOW_DIRECT_HARDWARE_MONITORING") ?? string.Empty;
            return !IsTruthy(directHardware);
        }

        public static bool ShouldSkipUnsafeHardwareUpdates()
        {
            string skipUnsafeUpdate = Environment.GetEnvironmentVariable("TW_SKIP_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            if (IsTruthy(skipUnsafeUpdate))
                return true;

            if (IsTemperatureProbeProcess())
                return false;

            string allowUnsafeUpdate = Environment.GetEnvironmentVariable("TW_ALLOW_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            if (IsTruthy(allowUnsafeUpdate) && !IsHardwareBackendSuppressed())
                return false;

            return true;
        }

        public static bool IsTemperatureProbeProcess()
        {
            return IsTruthy(Environment.GetEnvironmentVariable(TemperatureProbeProcessVariable) ?? string.Empty);
        }

        public static void MarkTemperatureProbeProcess()
        {
            Environment.SetEnvironmentVariable(TemperatureProbeProcessVariable, "1");
        }

        public static bool IsTemperatureProbeArgument(string argument)
        {
            return string.Equals(argument, TemperatureProbeArgument, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
