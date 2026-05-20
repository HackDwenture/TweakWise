using System;
using Microsoft.Win32;
using TweakWise.Models;

namespace TweakWise.Execution
{
    public sealed class RegistryTweakStateReader : ITweakStateReader
    {
        public bool CanRead(TweakDefinition tweak)
        {
            return tweak?.Execution?.IsSupported == true;
        }

        public TweakStateReadResult ReadState(TweakDefinition tweak)
        {
            if (!CanRead(tweak))
                return TweakStateReadResult.Fail("Для этой настройки пока нет безопасного чтения состояния.");

            try
            {
                bool allOperationsMatch = true;

                foreach (var operation in tweak.Execution.RegistryOperations)
                {
                    RegistryKey baseKey = RegistryTweakHelpers.OpenBaseKey(operation.Hive);
                    using RegistryKey key = baseKey.OpenSubKey(operation.SubKeyPath, writable: false);

                    bool valueExists = RegistryTweakHelpers.ValueExists(key, operation.ValueName);
                    object currentValue = valueExists ? key.GetValue(operation.ValueName) : null;
                    var expectedKind = RegistryTweakHelpers.ToRegistryValueKind(operation.ValueKind);
                    object expectedValue = RegistryTweakHelpers.NormalizeValue(operation.TargetValue, expectedKind);

                    bool matches = operation.OperationKind switch
                    {
                        RegistryTweakOperationKind.SetValue => valueExists && RegistryTweakHelpers.ValuesEqual(currentValue, expectedValue, expectedKind),
                        RegistryTweakOperationKind.DeleteValue => !valueExists,
                        _ => false
                    };

                    if (!matches)
                        allOperationsMatch = false;
                }

                string appliedState = string.IsNullOrWhiteSpace(tweak.Execution.AppliedStateLabel)
                    ? tweak.RecommendedState
                    : tweak.Execution.AppliedStateLabel;

                string notAppliedState = string.IsNullOrWhiteSpace(tweak.Execution.NotAppliedStateLabel)
                    ? tweak.CurrentState
                    : tweak.Execution.NotAppliedStateLabel;

                return TweakStateReadResult.Ok(allOperationsMatch ? appliedState : notAppliedState);
            }
            catch (Exception ex)
            {
                return TweakStateReadResult.Fail(
                    "Не удалось прочитать текущее состояние настройки.",
                    ex.Message);
            }
        }
    }
}
