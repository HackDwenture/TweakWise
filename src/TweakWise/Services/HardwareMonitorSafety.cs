using System;
using System.Diagnostics;

namespace TweakWise.Services
{
    internal static class HardwareMonitorSafety
    {
        public static bool IsHardwareBackendSuppressed()
        {
            string suppressHardware = Environment.GetEnvironmentVariable("TW_SUPPRESS_HARDWARE_MONITORING") ?? string.Empty;
            return IsTruthy(suppressHardware);
        }

        public static bool ShouldSkipUnsafeHardwareUpdates()
        {
            string skipUnsafeUpdate = Environment.GetEnvironmentVariable("TW_SKIP_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            if (IsTruthy(skipUnsafeUpdate))
                return true;

            string allowUnsafeUpdate = Environment.GetEnvironmentVariable("TW_ALLOW_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            if (IsTruthy(allowUnsafeUpdate))
                return false;

            return Debugger.IsAttached;
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
