using System;
using System.Linq;
using Microsoft.Win32;
using TweakWise.Models;

namespace TweakWise.Execution
{
    internal static class RegistryTweakHelpers
    {
        public static RegistryKey OpenBaseKey(RegistryTweakHive hive)
        {
            return hive switch
            {
                RegistryTweakHive.CurrentUser => Registry.CurrentUser,
                RegistryTweakHive.LocalMachine => Registry.LocalMachine,
                RegistryTweakHive.ClassesRoot => Registry.ClassesRoot,
                RegistryTweakHive.Users => Registry.Users,
                RegistryTweakHive.CurrentConfig => Registry.CurrentConfig,
                _ => Registry.CurrentUser
            };
        }

        public static RegistryValueKind ToRegistryValueKind(RegistryTweakValueKind valueKind)
        {
            return valueKind switch
            {
                RegistryTweakValueKind.String => RegistryValueKind.String,
                RegistryTweakValueKind.ExpandString => RegistryValueKind.ExpandString,
                RegistryTweakValueKind.DWord => RegistryValueKind.DWord,
                RegistryTweakValueKind.QWord => RegistryValueKind.QWord,
                RegistryTweakValueKind.MultiString => RegistryValueKind.MultiString,
                RegistryTweakValueKind.Binary => RegistryValueKind.Binary,
                _ => RegistryValueKind.Unknown
            };
        }

        public static object NormalizeValue(object value, RegistryValueKind valueKind)
        {
            if (value == null)
                return null;

            try
            {
                return valueKind switch
                {
                    RegistryValueKind.DWord => Convert.ToInt32(value),
                    RegistryValueKind.QWord => Convert.ToInt64(value),
                    RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value) ?? string.Empty,
                    RegistryValueKind.MultiString => value is string[] values ? values : new[] { Convert.ToString(value) ?? string.Empty },
                    RegistryValueKind.Binary => value is byte[] bytes ? bytes : Array.Empty<byte>(),
                    _ => value
                };
            }
            catch
            {
                return value;
            }
        }

        public static bool ValuesEqual(object left, object right, RegistryValueKind valueKind)
        {
            var normalizedLeft = NormalizeValue(left, valueKind);
            var normalizedRight = NormalizeValue(right, valueKind);

            if (normalizedLeft is byte[] leftBytes && normalizedRight is byte[] rightBytes)
                return leftBytes.SequenceEqual(rightBytes);

            if (normalizedLeft is string[] leftStrings && normalizedRight is string[] rightStrings)
                return leftStrings.SequenceEqual(rightStrings, StringComparer.Ordinal);

            return Equals(normalizedLeft, normalizedRight);
        }

        public static bool ValueExists(RegistryKey key, string valueName)
        {
            if (key == null)
                return false;

            return key
                .GetValueNames()
                .Any(name => string.Equals(name, valueName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        public static string FormatValue(object value)
        {
            if (value == null)
                return "не было";

            if (value is byte[] bytes)
                return bytes.Length == 0 ? "пустое binary-значение" : $"binary: {bytes.Length} байт";

            if (value is string[] strings)
                return string.Join(", ", strings);

            return Convert.ToString(value) ?? string.Empty;
        }
    }
}
