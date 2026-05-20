using System.Collections.Generic;
using TweakWise.Models;

namespace TweakWise.Execution
{
    public interface ITweakStateReader
    {
        bool CanRead(TweakDefinition tweak);
        TweakStateReadResult ReadState(TweakDefinition tweak);
    }

    public interface ITweakApplier
    {
        bool CanApply(TweakDefinition tweak);
        TweakExecutionResult Apply(TweakDefinition tweak, TweakExecutionOptions options);
    }

    public interface ITweakRollbackService
    {
        bool HasRollback(string tweakId);
        void SaveRollback(TweakDefinition tweak, RegistryRollbackRecord rollbackRecord);
        TweakExecutionResult Rollback(TweakDefinition tweak);
        IReadOnlyList<TweakExecutionHistoryItem> GetHistory();
    }

    public interface ITweakExecutionService
    {
        bool IsSupported(string tweakId);
        bool CanRollback(string tweakId);
        TweakStateReadResult ReadState(string tweakId);
        TweakExecutionResult Preview(string tweakId);
        TweakExecutionResult Apply(string tweakId, TweakExecutionOptions options);
        TweakExecutionResult Rollback(string tweakId);
        IReadOnlyList<TweakExecutionHistoryItem> GetHistory();
    }
}
