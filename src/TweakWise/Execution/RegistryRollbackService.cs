using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using TweakWise.Models;

namespace TweakWise.Execution
{
    public sealed class RegistryRollbackService : ITweakRollbackService
    {
        private readonly Dictionary<string, RegistryRollbackRecord> _rollbackRecords = new Dictionary<string, RegistryRollbackRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TweakExecutionHistoryItem> _history = new List<TweakExecutionHistoryItem>();

        public bool HasRollback(string tweakId)
        {
            return !string.IsNullOrWhiteSpace(tweakId) && _rollbackRecords.ContainsKey(tweakId);
        }

        public void SaveRollback(TweakDefinition tweak, RegistryRollbackRecord rollbackRecord)
        {
            if (tweak == null || rollbackRecord == null || string.IsNullOrWhiteSpace(tweak.Id))
                return;

            _rollbackRecords[tweak.Id] = rollbackRecord;
            _history.Insert(0, new TweakExecutionHistoryItem
            {
                TweakId = tweak.Id,
                Title = tweak.Title,
                AppliedAt = rollbackRecord.CreatedAt,
                Message = "Настройка применена. Откат доступен.",
                RequiresRestart = tweak.RequiresRestart,
                RollbackAvailable = true
            });
        }

        public TweakExecutionResult Rollback(TweakDefinition tweak)
        {
            if (tweak == null || string.IsNullOrWhiteSpace(tweak.Id))
                return TweakExecutionResult.Fail("Не удалось определить настройку для отката.");

            if (!_rollbackRecords.TryGetValue(tweak.Id, out var rollbackRecord))
                return TweakExecutionResult.Fail("Для этой настройки пока нет сохранённого отката.");

            try
            {
                foreach (var rollbackValue in rollbackRecord.Values)
                {
                    RegistryKey baseKey = RegistryTweakHelpers.OpenBaseKey(rollbackValue.Hive);
                    using RegistryKey key = rollbackValue.ValueExisted
                        ? baseKey.CreateSubKey(rollbackValue.SubKeyPath, writable: true)
                        : baseKey.OpenSubKey(rollbackValue.SubKeyPath, writable: true);

                    if (key == null)
                        continue;

                    if (rollbackValue.ValueExisted)
                    {
                        key.SetValue(
                            rollbackValue.ValueName,
                            RegistryTweakHelpers.NormalizeValue(rollbackValue.OldValue, rollbackValue.OldValueKind),
                            rollbackValue.OldValueKind);
                    }
                    else if (rollbackValue.AllowValueDeleteWhenMissing && RegistryTweakHelpers.ValueExists(key, rollbackValue.ValueName))
                    {
                        key.DeleteValue(rollbackValue.ValueName, throwOnMissingValue: false);
                    }
                }

                _rollbackRecords.Remove(tweak.Id);
                _history.Insert(0, new TweakExecutionHistoryItem
                {
                    TweakId = tweak.Id,
                    Title = tweak.Title,
                    AppliedAt = DateTime.Now,
                    Message = "Откат выполнен.",
                    RequiresRestart = tweak.RequiresRestart,
                    RollbackAvailable = false
                });

                return new TweakExecutionResult
                {
                    Success = true,
                    RequiresRestart = tweak.RequiresRestart,
                    Message = tweak.RequiresRestart
                        ? "Откат выполнен. Для полного эффекта может потребоваться перезапуск Explorer или вход в систему."
                        : "Откат выполнен.",
                    RollbackAvailable = false
                };
            }
            catch (Exception ex)
            {
                return TweakExecutionResult.Fail(
                    "Не удалось выполнить откат. Старые значения сохранены для повторной попытки.",
                    ex.Message);
            }
        }

        public IReadOnlyList<TweakExecutionHistoryItem> GetHistory()
        {
            return _history.ToList();
        }
    }
}
