using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public sealed class SettingCardViewModel : INotifyPropertyChanged
    {
        private string _currentState = string.Empty;
        private string _applyButtonText = "Применить";
        private bool _canApply;
        private bool _rollbackAvailable;
        private string _executionStatusMessage = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subcategory { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public string CurrentState
        {
            get => _currentState;
            set => SetProperty(ref _currentState, value);
        }

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
        public bool IsExecutionSupported { get; set; }

        public string ApplyButtonText
        {
            get => _applyButtonText;
            set => SetProperty(ref _applyButtonText, value);
        }

        public bool CanApply
        {
            get => _canApply;
            set => SetProperty(ref _canApply, value);
        }

        public bool RollbackAvailable
        {
            get => _rollbackAvailable;
            set => SetProperty(ref _rollbackAvailable, value);
        }

        public string ExecutionStatusMessage
        {
            get => _executionStatusMessage;
            set
            {
                if (SetProperty(ref _executionStatusMessage, value))
                    OnPropertyChanged(nameof(HasExecutionStatus));
            }
        }

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
        public bool HasExecutionStatus => !string.IsNullOrWhiteSpace(ExecutionStatusMessage);

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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
