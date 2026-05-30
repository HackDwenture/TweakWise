using System;
using System.Diagnostics;

namespace TweakWise.Services
{
    internal static class HardwareMonitorSafety
    {
        public static bool IsHardwareBackendSuppressed()
        {
            string suppressHardware = Environment.GetEnvironmentVariable("TW_SUPPRESS_HARDWARE_MONITORING") ?? string.Empty;
            if (IsTruthy(suppressHardware))
                return true;

            string allowDebugHardware = Environment.GetEnvironmentVariable("TW_ALLOW_HARDWARE_DEBUG") ?? string.Empty;
            if (IsTruthy(allowDebugHardware))
                return false;

#if DEBUG
            return Debugger.IsAttached;
#else
            return false;
#endif
        }

        public static bool ShouldSkipUnsafeHardwareUpdates()
        {
            string allowUnsafeUpdate = Environment.GetEnvironmentVariable("TW_ALLOW_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            string allowDebugTelemetry = Environment.GetEnvironmentVariable("TW_ALLOW_HARDWARE_DEBUG") ?? string.Empty;

            if (IsTruthy(allowUnsafeUpdate) || IsTruthy(allowDebugTelemetry))
                return false;

#if DEBUG
            return Debugger.IsAttached;
#else
            return false;
#endif
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
