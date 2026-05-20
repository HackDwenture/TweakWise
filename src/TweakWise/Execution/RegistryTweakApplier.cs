using System;
using System.Collections.Generic;
using Microsoft.Win32;
using TweakWise.Models;

namespace TweakWise.Execution
{
    public sealed class RegistryTweakApplier : ITweakApplier
    {
        private readonly ITweakRollbackService _rollbackService;

        public RegistryTweakApplier(ITweakRollbackService rollbackService)
        {
            _rollbackService = rollbackService;
        }

        public bool CanApply(TweakDefinition tweak)
        {
            return tweak?.Execution?.IsSupported == true;
        }

        public TweakExecutionResult Apply(TweakDefinition tweak, TweakExecutionOptions options)
        {
            options ??= new TweakExecutionOptions();

            if (!CanApply(tweak))
                return TweakExecutionResult.Fail("Эта настройка пока недоступна для безопасного применения.");

            if (tweak.Execution.IsDangerous && !options.DangerousChangeConfirmed)
                return TweakExecutionResult.Fail("Эта настройка требует явного подтверждения перед применением.");

            try
            {
                var rollbackRecord = CaptureRollbackState(tweak);

                if (options.DryRun)
                {
                    return new TweakExecutionResult
                    {
                        Success = true,
                        RequiresRestart = tweak.RequiresRestart,
                        Message = "Предпросмотр готов. Реестр не изменён.",
                        OldValue = FormatRollbackValues(rollbackRecord),
                        NewValue = tweak.RecommendedState,
                        RollbackAvailable = false
                    };
                }

                var appliedOperations = new List<RegistryRollbackValue>();

                try
                {
                    foreach (var operation in tweak.Execution.RegistryOperations)
                    {
                        var rollbackValue = rollbackRecord.Values[appliedOperations.Count];
                        ApplyOperation(operation);
                        appliedOperations.Add(rollbackValue);
                    }
                }
                catch
                {
                    RestoreAppliedValues(appliedOperations);
                    throw;
                }

                _rollbackService.SaveRollback(tweak, rollbackRecord);

                return new TweakExecutionResult
                {
                    Success = true,
                    RequiresRestart = tweak.RequiresRestart,
                    Message = tweak.RequiresRestart
                        ? "Настройка применена. Для полного эффекта может потребоваться перезапуск Explorer или вход в систему."
                        : "Настройка применена.",
                    OldValue = FormatRollbackValues(rollbackRecord),
                    NewValue = tweak.RecommendedState,
                    RollbackAvailable = true
                };
            }
            catch (Exception ex)
            {
                return TweakExecutionResult.Fail(
                    "Не удалось применить настройку. Изменения не были завершены.",
                    ex.Message);
            }
        }

        private static RegistryRollbackRecord CaptureRollbackState(TweakDefinition tweak)
        {
            var record = new RegistryRollbackRecord
            {
                TweakId = tweak.Id,
                Title = tweak.Title,
                CreatedAt = DateTime.Now
            };

            foreach (var operation in tweak.Execution.RegistryOperations)
            {
                RegistryKey baseKey = RegistryTweakHelpers.OpenBaseKey(operation.Hive);
                using RegistryKey key = baseKey.OpenSubKey(operation.SubKeyPath, writable: false);
                bool keyExisted = key != null;
                bool valueExisted = RegistryTweakHelpers.ValueExists(key, operation.ValueName);
                object oldValue = valueExisted ? key.GetValue(operation.ValueName) : null;
                RegistryValueKind oldValueKind = valueExisted ? key.GetValueKind(operation.ValueName) : RegistryValueKind.Unknown;

                record.Values.Add(new RegistryRollbackValue
                {
                    Hive = operation.Hive,
                    SubKeyPath = operation.SubKeyPath,
                    ValueName = operation.ValueName,
                    KeyExisted = keyExisted,
                    ValueExisted = valueExisted,
                    OldValue = oldValue,
                    OldValueKind = oldValueKind,
                    AllowValueDeleteWhenMissing = operation.AllowValueDeleteOnRollbackWhenMissing
                });
            }

            return record;
        }

        private static void ApplyOperation(RegistryTweakOperationDefinition operation)
        {
            RegistryKey baseKey = RegistryTweakHelpers.OpenBaseKey(operation.Hive);
            using RegistryKey key = baseKey.CreateSubKey(operation.SubKeyPath, writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть раздел реестра для записи.");

            if (operation.OperationKind == RegistryTweakOperationKind.DeleteValue)
            {
                key.DeleteValue(operation.ValueName, throwOnMissingValue: false);
                return;
            }

            var valueKind = RegistryTweakHelpers.ToRegistryValueKind(operation.ValueKind);
            object targetValue = RegistryTweakHelpers.NormalizeValue(operation.TargetValue, valueKind);
            key.SetValue(operation.ValueName, targetValue, valueKind);
        }

        private static void RestoreAppliedValues(IReadOnlyList<RegistryRollbackValue> values)
        {
            foreach (var value in values)
            {
                RegistryKey baseKey = RegistryTweakHelpers.OpenBaseKey(value.Hive);
                using RegistryKey key = value.ValueExisted
                    ? baseKey.CreateSubKey(value.SubKeyPath, writable: true)
                    : baseKey.OpenSubKey(value.SubKeyPath, writable: true);

                if (key == null)
                    continue;

                if (value.ValueExisted)
                {
                    key.SetValue(
                        value.ValueName,
                        RegistryTweakHelpers.NormalizeValue(value.OldValue, value.OldValueKind),
                        value.OldValueKind);
                }
                else if (value.AllowValueDeleteWhenMissing && RegistryTweakHelpers.ValueExists(key, value.ValueName))
                {
                    key.DeleteValue(value.ValueName, throwOnMissingValue: false);
                }
            }
        }

        private static string FormatRollbackValues(RegistryRollbackRecord rollbackRecord)
        {
            if (rollbackRecord.Values.Count == 0)
                return string.Empty;

            return RegistryTweakHelpers.FormatValue(rollbackRecord.Values[0].OldValue);
        }
    }
}
