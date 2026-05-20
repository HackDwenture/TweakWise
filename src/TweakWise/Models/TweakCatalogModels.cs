using System.Collections.Generic;

namespace TweakWise.Models
{
    public class TweakDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subcategory { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public TweakSourceType SourceType { get; set; } = TweakSourceType.Windows;
        public TweakRiskLevel RiskLevel { get; set; } = TweakRiskLevel.Low;
        public bool RequiresRestart { get; set; }
        public bool RequiresConfirmation { get; set; }
        public bool IsReversible { get; set; } = true;
        public string CurrentState { get; set; } = "Не определено";
        public string RecommendedState { get; set; } = "Рекомендуемое значение";
        public List<string> Tags { get; set; } = new List<string>();
        public TweakAdvancedDetails AdvancedDetails { get; set; } = new TweakAdvancedDetails();
        public TweakPreviewMeta PreviewMeta { get; set; } = new TweakPreviewMeta();
        public TweakRollbackMeta RollbackMeta { get; set; } = new TweakRollbackMeta();
        public TweakExecutionDefinition Execution { get; set; } = new TweakExecutionDefinition();
    }

    public class TweakAdvancedDetails
    {
        public string TechnicalSummary { get; set; } = string.Empty;
        public List<string> AffectedComponents { get; set; } = new List<string>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class TweakRollbackMeta
    {
        public string RollbackType { get; set; } = string.Empty;
        public string RollbackSummary { get; set; } = string.Empty;
        public string ValidationHint { get; set; } = string.Empty;
    }

    public class TweakPreviewMeta
    {
        public string Summary { get; set; } = string.Empty;
        public string EstimatedImpact { get; set; } = string.Empty;
        public List<string> SampleItems { get; set; } = new List<string>();
        public string ConfirmationHint { get; set; } = string.Empty;
    }

    public class TweakExecutionDefinition
    {
        public TweakExecutionKind Kind { get; set; } = TweakExecutionKind.None;
        public bool IsDangerous { get; set; }
        public bool RequiresElevation { get; set; }
        public string AppliedStateLabel { get; set; } = string.Empty;
        public string NotAppliedStateLabel { get; set; } = string.Empty;
        public List<RegistryTweakOperationDefinition> RegistryOperations { get; set; } = new List<RegistryTweakOperationDefinition>();

        public bool IsSupported => Kind == TweakExecutionKind.Registry && RegistryOperations.Count > 0;
    }

    public class RegistryTweakOperationDefinition
    {
        public RegistryTweakOperationKind OperationKind { get; set; } = RegistryTweakOperationKind.SetValue;
        public RegistryTweakHive Hive { get; set; } = RegistryTweakHive.CurrentUser;
        public string SubKeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public RegistryTweakValueKind ValueKind { get; set; } = RegistryTweakValueKind.DWord;
        public object TargetValue { get; set; } = 0;
        public bool AllowValueDeleteOnRollbackWhenMissing { get; set; } = true;
    }

    public class TweakCategoryDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public List<string> Subcategories { get; set; } = new List<string>();
    }

    public class TweakTemplateDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ScopeLabel { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public TweakRiskLevel RiskLevel { get; set; } = TweakRiskLevel.Low;
        public bool RequiresRestart { get; set; }
        public List<string> TweakIds { get; set; } = new List<string>();
    }

    public enum TweakSourceType
    {
        Windows,
        Registry,
        Policy,
        Service,
        Task,
        Mixed
    }

    public enum TweakRiskLevel
    {
        Low,
        Medium,
        High
    }

    public enum TweakExecutionKind
    {
        None,
        Registry
    }

    public enum RegistryTweakOperationKind
    {
        SetValue,
        DeleteValue
    }

    public enum RegistryTweakHive
    {
        CurrentUser,
        LocalMachine,
        ClassesRoot,
        Users,
        CurrentConfig
    }

    public enum RegistryTweakValueKind
    {
        String,
        ExpandString,
        DWord,
        QWord,
        MultiString,
        Binary
    }
}
