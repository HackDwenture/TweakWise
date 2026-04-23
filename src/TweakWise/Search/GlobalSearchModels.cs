using TweakWise.Catalog;

namespace TweakWise.Search
{
    public enum GlobalSearchResultKind
    {
        Section,
        Subsection,
        Setting,
        Template,
        Action
    }

    public sealed class GlobalSearchNavigationTarget
    {
        public string PageKey { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Subcategory { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ActionKey { get; set; } = string.Empty;
        public GlobalSearchResultKind ResultKind { get; set; } = GlobalSearchResultKind.Section;
    }

    public sealed class GlobalSearchResultViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string ResultTypeText { get; set; } = string.Empty;
        public string PathText { get; set; } = string.Empty;
        public string SourceBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone SourceTone { get; set; } = CatalogBadgeTone.Info;
        public string RiskBadgeText { get; set; } = string.Empty;
        public CatalogBadgeTone RiskTone { get; set; } = CatalogBadgeTone.Neutral;
        public bool IsDefaultSuggestion { get; set; }
        public GlobalSearchNavigationTarget NavigationTarget { get; set; } = new GlobalSearchNavigationTarget();

        public bool HasSourceBadge => !string.IsNullOrWhiteSpace(SourceBadgeText);
        public bool HasRiskBadge => !string.IsNullOrWhiteSpace(RiskBadgeText);
    }

    public static class GlobalSearchNavigationStore
    {
        private static GlobalSearchNavigationTarget _pendingTarget;

        public static void SetPending(GlobalSearchNavigationTarget target)
        {
            _pendingTarget = target;
        }

        public static GlobalSearchNavigationTarget ConsumeForPage(string pageKey)
        {
            if (_pendingTarget == null || _pendingTarget.PageKey != pageKey)
                return null;

            var target = _pendingTarget;
            _pendingTarget = null;
            return target;
        }

        public static void Clear()
        {
            _pendingTarget = null;
        }
    }
}
