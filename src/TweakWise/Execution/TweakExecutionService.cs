using System;
using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;
using TweakWise.Providers;

namespace TweakWise.Execution
{
    public sealed class TweakExecutionService : ITweakExecutionService
    {
        private readonly ITweakCatalogProvider _catalogProvider;
        private readonly ITweakStateReader _stateReader;
        private readonly ITweakApplier _applier;
        private readonly ITweakRollbackService _rollbackService;

        public TweakExecutionService(
            ITweakCatalogProvider catalogProvider,
            ITweakStateReader stateReader,
            ITweakApplier applier,
            ITweakRollbackService rollbackService)
        {
            _catalogProvider = catalogProvider;
            _stateReader = stateReader;
            _applier = applier;
            _rollbackService = rollbackService;
        }

        public bool IsSupported(string tweakId)
        {
            var tweak = FindTweak(tweakId);
            return tweak != null && _applier.CanApply(tweak);
        }

        public bool CanRollback(string tweakId)
        {
            return _rollbackService.HasRollback(tweakId);
        }

        public TweakStateReadResult ReadState(string tweakId)
        {
            var tweak = FindTweak(tweakId);
            if (tweak == null)
                return TweakStateReadResult.Fail("Настройка не найдена.");

            if (!_stateReader.CanRead(tweak))
                return TweakStateReadResult.Fail("Для этой настройки пока нет безопасного чтения состояния.");

            var result = _stateReader.ReadState(tweak);
            if (result.Success)
                tweak.CurrentState = result.CurrentState;

            return result;
        }

        public TweakExecutionResult Preview(string tweakId)
        {
            return Apply(tweakId, new TweakExecutionOptions { DryRun = true });
        }

        public TweakExecutionResult Apply(string tweakId, TweakExecutionOptions options)
        {
            var tweak = FindTweak(tweakId);
            if (tweak == null)
                return TweakExecutionResult.Fail("Настройка не найдена.");

            if (!_applier.CanApply(tweak))
                return TweakExecutionResult.Fail("Эта настройка пока недоступна в текущей версии.");

            try
            {
                string oldState = tweak.CurrentState;
                var result = _applier.Apply(tweak, options ?? new TweakExecutionOptions());

                if (!result.Success || options?.DryRun == true)
                    return result;

                var state = ReadState(tweak.Id);
                if (state.Success)
                {
                    result.OldValue = string.IsNullOrWhiteSpace(result.OldValue) ? oldState : result.OldValue;
                    result.NewValue = state.CurrentState;
                }

                result.RollbackAvailable = _rollbackService.HasRollback(tweak.Id);
                return result;
            }
            catch (Exception ex)
            {
                return TweakExecutionResult.Fail(
                    "Не удалось применить настройку.",
                    ex.Message);
            }
        }

        public TweakExecutionResult Rollback(string tweakId)
        {
            var tweak = FindTweak(tweakId);
            if (tweak == null)
                return TweakExecutionResult.Fail("Настройка не найдена.");

            var result = _rollbackService.Rollback(tweak);
            if (!result.Success)
                return result;

            var state = ReadState(tweak.Id);
            if (state.Success)
                result.NewValue = state.CurrentState;

            result.RollbackAvailable = _rollbackService.HasRollback(tweak.Id);
            return result;
        }

        public IReadOnlyList<TweakExecutionHistoryItem> GetHistory()
        {
            return _rollbackService.GetHistory();
        }

        private TweakDefinition FindTweak(string tweakId)
        {
            if (string.IsNullOrWhiteSpace(tweakId))
                return null;

            return _catalogProvider
                .GetTweaks()
                .FirstOrDefault(tweak => string.Equals(tweak.Id, tweakId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
