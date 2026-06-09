namespace TweakWise.Search
{
    public enum GlobalSearchResultKind
    {
        Section,
        Subsection,
        Action
    }

    public sealed class GlobalSearchNavigationTarget
    {
        public string PageKey { get; set; } = string.Empty;
        public string ActionKey { get; set; } = string.Empty;
        public GlobalSearchResultKind ResultKind { get; set; } = GlobalSearchResultKind.Section;
    }

    public sealed class GlobalSearchResultViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string ResultTypeText { get; set; } = string.Empty;
        public string PathText { get; set; } = string.Empty;
        public bool IsDefaultSuggestion { get; set; }
        public GlobalSearchNavigationTarget NavigationTarget { get; set; } = new GlobalSearchNavigationTarget();
    }
}
