using System.Collections.Generic;

namespace TweakWise.Catalog
{
    public enum CatalogBadgeTone
    {
        Neutral,
        Info,
        Success,
        Warning,
        Danger
    }

    public sealed class SettingCardViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subcategory { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public string RecommendedState { get; set; } = string.Empty;
        public string RiskBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RiskTone { get; set; } = CatalogBadgeTone.Neutral;
        public string SourceBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone SourceTone { get; set; } = CatalogBadgeTone.Info;
        public string RestartBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RestartTone { get; set; } = CatalogBadgeTone.Neutral;
        public string RollbackBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RollbackTone { get; set; } = CatalogBadgeTone.Neutral;
        public string TechnicalSummary { get; set; } = string.Empty;
        public List<string> AffectedComponents { get; set; } = new List<string>();
        public List<string> TechnicalNotes { get; set; } = new List<string>();
        public string PreviewSummary { get; set; } = string.Empty;
        public string PreviewEstimatedImpact { get; set; } = string.Empty;
        public List<string> PreviewItems { get; set; } = new List<string>();
        public string ConfirmationText { get; set; } = string.Empty;
        public bool RequiresConfirmation { get; set; }
        public string RollbackSummary { get; set; } = string.Empty;
        public string ValidationHint { get; set; } = string.Empty;
        public bool IsHighlighted { get; set; }

        public bool HasLongDescription => !string.IsNullOrWhiteSpace(LongDescription);
        public bool HasTechnicalSummary => !string.IsNullOrWhiteSpace(TechnicalSummary);
        public bool HasAffectedComponents => AffectedComponents.Count > 0;
        public bool HasTechnicalNotes => TechnicalNotes.Count > 0;
        public bool HasPreviewSummary => !string.IsNullOrWhiteSpace(PreviewSummary);
        public bool HasPreviewEstimatedImpact => !string.IsNullOrWhiteSpace(PreviewEstimatedImpact);
        public bool HasPreviewItems => PreviewItems.Count > 0;
        public bool HasConfirmationText => !string.IsNullOrWhiteSpace(ConfirmationText);
        public bool HasPreview => HasPreviewSummary || HasPreviewEstimatedImpact || HasPreviewItems || HasConfirmationText;
        public bool HasRollbackSummary => !string.IsNullOrWhiteSpace(RollbackSummary);
        public bool HasValidationHint => !string.IsNullOrWhiteSpace(ValidationHint);
        public bool HasDetails => HasTechnicalSummary || HasAffectedComponents || HasTechnicalNotes || HasPreview || HasRollbackSummary || HasValidationHint;
    }

    public sealed class LocalTemplateCardViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ScopeText { get; set; } = string.Empty;
        public string AudienceText { get; set; } = string.Empty;
        public string RiskBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RiskTone { get; set; } = CatalogBadgeTone.Neutral;
        public string RestartBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RestartTone { get; set; } = CatalogBadgeTone.Neutral;
        public List<string> IncludedItems { get; set; } = new List<string>();

        public bool HasIncludedItems => IncludedItems.Count > 0;
    }
}
