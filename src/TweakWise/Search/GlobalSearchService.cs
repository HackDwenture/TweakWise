using System;
using System.Collections.Generic;
using System.Linq;
using TweakWise.Catalog;
using TweakWise.Models;
using TweakWise.Providers;

namespace TweakWise.Search
{
    public sealed class GlobalSearchService
    {
        private readonly ITweakCatalogProvider _catalogProvider;
        private List<GlobalSearchIndexEntry> _index = new List<GlobalSearchIndexEntry>();

        public GlobalSearchService(ITweakCatalogProvider catalogProvider)
        {
            _catalogProvider = catalogProvider;
            RebuildIndex();
        }

        public void RebuildIndex()
        {
            var categories = _catalogProvider.GetCategories();
            var tweaks = _catalogProvider.GetTweaks();
            var templates = _catalogProvider.GetTemplates();
            var tweakMap = tweaks.ToDictionary(tweak => tweak.Id, tweak => tweak);

            var index = new List<GlobalSearchIndexEntry>
            {
                new GlobalSearchIndexEntry
                {
                    Title = "Главная",
                    ResultTypeText = "Раздел",
                    PathText = "Раздел приложения",
                    SearchBlob = BuildSearchBlob("Главная", "Раздел приложения", "dashboard home templates overview"),
                    DefaultRank = 0,
                    IsDefaultSuggestion = true,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        PageKey = "Dashboard",
                        ResultKind = GlobalSearchResultKind.Section
                    }
                }
            };

            index.AddRange(BuildCategoryEntries(categories));
            index.AddRange(BuildSubsectionEntries(categories));
            index.AddRange(BuildTweakEntries(tweaks));
            index.AddRange(BuildTemplateEntries(templates, tweakMap));
            index.AddRange(BuildActionEntries());

            _index = index;
        }

        public IReadOnlyList<GlobalSearchResultViewModel> Search(string query, int maxResults = 8)
        {
            var tokens = SplitTokens(query);

            if (tokens.Count == 0)
            {
                return _index
                    .Where(entry => entry.IsDefaultSuggestion)
                    .OrderBy(entry => entry.DefaultRank)
                    .ThenBy(entry => entry.Title)
                    .Take(maxResults)
                    .Select(MapToViewModel)
                    .ToList();
            }

            return _index
                .Select(entry => new
                {
                    Entry = entry,
                    Score = ScoreEntry(entry, tokens)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Entry.DefaultRank)
                .ThenBy(item => item.Entry.Title)
                .Take(maxResults)
                .Select(item => MapToViewModel(item.Entry))
                .ToList();
        }

        private static IEnumerable<GlobalSearchIndexEntry> BuildCategoryEntries(IEnumerable<TweakCategoryDefinition> categories)
        {
            int rank = 10;

            foreach (var category in categories)
            {
                yield return new GlobalSearchIndexEntry
                {
                    Title = category.Title,
                    ResultTypeText = "Раздел",
                    PathText = "Раздел приложения",
                    SearchBlob = BuildSearchBlob(category.Title, category.Description, string.Join(" ", category.Subcategories)),
                    DefaultRank = rank++,
                    IsDefaultSuggestion = true,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        PageKey = GetPageKey(category.Id),
                        CategoryId = category.Id,
                        ResultKind = GlobalSearchResultKind.Section
                    }
                };
            }
        }

        private static IEnumerable<GlobalSearchIndexEntry> BuildSubsectionEntries(IEnumerable<TweakCategoryDefinition> categories)
        {
            int rank = 100;

            foreach (var category in categories)
            {
                string categoryTitle = category.Title;
                string pageKey = GetPageKey(category.Id);

                foreach (var subsection in category.Subcategories)
                {
                    yield return new GlobalSearchIndexEntry
                    {
                        Title = subsection,
                        ResultTypeText = "Секция",
                        PathText = $"{categoryTitle} → {subsection}",
                        SearchBlob = BuildSearchBlob(subsection, categoryTitle, $"{categoryTitle} {subsection}"),
                        DefaultRank = rank++,
                        NavigationTarget = new GlobalSearchNavigationTarget
                        {
                            PageKey = pageKey,
                            CategoryId = category.Id,
                            Subcategory = subsection,
                            ResultKind = GlobalSearchResultKind.Subsection
                        }
                    };
                }
            }
        }

        private static IEnumerable<GlobalSearchIndexEntry> BuildTweakEntries(IEnumerable<TweakDefinition> tweaks)
        {
            int rank = 200;

            foreach (var tweak in tweaks)
            {
                string categoryTitle = CatalogPresentationBuilder.GetCategoryTitle(tweak.Category);
                string resultTypeText = HasActionTag(tweak) ? "Действие" : "Настройка";

                yield return new GlobalSearchIndexEntry
                {
                    Title = tweak.Title,
                    ResultTypeText = resultTypeText,
                    PathText = $"{categoryTitle} → {tweak.Subcategory}",
                    SourceBadgeText = $"Источник: {CatalogPresentationBuilder.GetSourceText(tweak.SourceType)}",
                    RiskBadgeText = $"Риск: {CatalogPresentationBuilder.GetRiskText(tweak.RiskLevel)}",
                    RiskTone = GetRiskTone(tweak.RiskLevel),
                    SearchBlob = BuildSearchBlob(
                        tweak.Title,
                        resultTypeText,
                        tweak.ShortDescription,
                        tweak.LongDescription,
                        tweak.Subcategory,
                        categoryTitle,
                        string.Join(" ", tweak.Tags),
                        tweak.SourceType.ToString()),
                    DefaultRank = rank++,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        PageKey = GetPageKey(tweak.Category),
                        CategoryId = tweak.Category,
                        Subcategory = tweak.Subcategory,
                        ItemId = tweak.Id,
                        ResultKind = GlobalSearchResultKind.Setting
                    }
                };
            }
        }

        private static bool HasActionTag(TweakDefinition tweak)
        {
            return tweak.Tags.Any(tag =>
                string.Equals(tag, "action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "repair", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<GlobalSearchIndexEntry> BuildTemplateEntries(
            IEnumerable<TweakTemplateDefinition> templates,
            IReadOnlyDictionary<string, TweakDefinition> tweakMap)
        {
            int rank = 300;

            foreach (var template in templates)
            {
                string pathText = BuildTemplatePath(template);
                string pageKey = GetTemplatePageKey(template, tweakMap);

                yield return new GlobalSearchIndexEntry
                {
                    Title = template.Title,
                    ResultTypeText = "Шаблон",
                    PathText = pathText,
                    RiskBadgeText = $"Риск: {CatalogPresentationBuilder.GetRiskText(template.RiskLevel)}",
                    RiskTone = GetRiskTone(template.RiskLevel),
                    SearchBlob = BuildSearchBlob(
                        template.Title,
                        template.Description,
                        pathText,
                        template.Audience,
                        string.Join(" ", template.TweakIds)),
                    DefaultRank = rank++,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        PageKey = pageKey,
                        CategoryId = GetTemplateCategoryId(template, tweakMap),
                        ItemId = template.Id,
                        ResultKind = GlobalSearchResultKind.Template
                    }
                };
            }
        }

        private static IEnumerable<GlobalSearchIndexEntry> BuildActionEntries()
        {
            return new List<GlobalSearchIndexEntry>
            {
                new()
                {
                    Title = "Открыть настройки программы",
                    ResultTypeText = "Действие",
                    PathText = "Программа → Быстрые действия",
                    SearchBlob = BuildSearchBlob("Открыть настройки программы", "настройки theme app preferences"),
                    DefaultRank = 20,
                    IsDefaultSuggestion = true,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        ActionKey = "OpenSettings",
                        ResultKind = GlobalSearchResultKind.Action
                    }
                },
                new()
                {
                    Title = "Проверить наличие обновлений",
                    ResultTypeText = "Действие",
                    PathText = "Программа → Быстрые действия",
                    SearchBlob = BuildSearchBlob("Проверить наличие обновлений", "updates release latest"),
                    DefaultRank = 21,
                    IsDefaultSuggestion = true,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        ActionKey = "CheckUpdates",
                        ResultKind = GlobalSearchResultKind.Action
                    }
                },
                new()
                {
                    Title = "Открыть уведомления",
                    ResultTypeText = "Действие",
                    PathText = "Программа → Быстрые действия",
                    SearchBlob = BuildSearchBlob("Открыть уведомления", "notifications alerts inbox"),
                    DefaultRank = 22,
                    IsDefaultSuggestion = true,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        ActionKey = "OpenNotifications",
                        ResultKind = GlobalSearchResultKind.Action
                    }
                }
            };
        }

        private static GlobalSearchResultViewModel MapToViewModel(GlobalSearchIndexEntry entry)
        {
            return new GlobalSearchResultViewModel
            {
                Title = entry.Title,
                ResultTypeText = entry.ResultTypeText,
                PathText = entry.PathText,
                SourceBadgeText = entry.SourceBadgeText,
                SourceTone = CatalogBadgeTone.Info,
                RiskBadgeText = entry.RiskBadgeText,
                RiskTone = entry.RiskTone,
                IsDefaultSuggestion = entry.IsDefaultSuggestion,
                NavigationTarget = entry.NavigationTarget
            };
        }

        private static int ScoreEntry(GlobalSearchIndexEntry entry, IReadOnlyList<string> tokens)
        {
            int totalScore = 0;

            foreach (var token in tokens)
            {
                if (!entry.SearchBlob.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return 0;

                totalScore += ScoreSegment(entry.Title, token, 180, 120, 85);
                totalScore += ScoreSegment(entry.PathText, token, 85, 55, 35);
                totalScore += ScoreSegment(entry.ResultTypeText, token, 30, 20, 10);
                totalScore += ScoreSegment(entry.SourceBadgeText, token, 25, 15, 10);
            }

            return totalScore + Math.Max(0, 500 - entry.DefaultRank);
        }

        private static int ScoreSegment(string source, string token, int startsWithScore, int wordStartsWithScore, int containsScore)
        {
            if (string.IsNullOrWhiteSpace(source))
                return 0;

            if (source.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                return startsWithScore;

            if (source
                .Split(new[] { ' ', '→', '/', '-', ',', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
            {
                return wordStartsWithScore;
            }

            return source.Contains(token, StringComparison.OrdinalIgnoreCase) ? containsScore : 0;
        }

        private static List<string> SplitTokens(string query)
        {
            return (query ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
        }

        private static string BuildSearchBlob(params string[] parts)
        {
            return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).ToLowerInvariant();
        }

        private static string BuildTemplatePath(TweakTemplateDefinition template)
        {
            if (string.IsNullOrWhiteSpace(template.ScopeLabel))
                return "Главная → Шаблоны";

            return template.ScopeLabel.Replace(" / ", " → ");
        }

        private static string GetTemplatePageKey(
            TweakTemplateDefinition template,
            IReadOnlyDictionary<string, TweakDefinition> tweakMap)
        {
            if (template.Id.StartsWith("windows-interface-", StringComparison.OrdinalIgnoreCase))
                return "WindowsInterface";

            string categoryId = GetTemplateCategoryId(template, tweakMap);
            return string.IsNullOrWhiteSpace(categoryId) ? "Dashboard" : GetPageKey(categoryId);
        }

        private static string GetTemplateCategoryId(
            TweakTemplateDefinition template,
            IReadOnlyDictionary<string, TweakDefinition> tweakMap)
        {
            var categories = template.TweakIds
                .Where(tweakMap.ContainsKey)
                .Select(id => tweakMap[id].Category)
                .Distinct()
                .ToList();

            if (categories.Count == 1)
                return categories[0];

            return string.Empty;
        }

        private static string GetPageKey(string categoryId)
        {
            return categoryId switch
            {
                "WindowsInterface" => "WindowsInterface",
                "System" => "System",
                "Maintenance" => "Maintenance",
                "MonitoringPerformance" => "MonitoringPerformance",
                _ => "Dashboard"
            };
        }

        private static CatalogBadgeTone GetRiskTone(TweakRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                TweakRiskLevel.Low => CatalogBadgeTone.Success,
                TweakRiskLevel.Medium => CatalogBadgeTone.Warning,
                TweakRiskLevel.High => CatalogBadgeTone.Danger,
                _ => CatalogBadgeTone.Neutral
            };
        }

        private sealed class GlobalSearchIndexEntry
        {
            public string Title { get; set; } = string.Empty;
            public string ResultTypeText { get; set; } = string.Empty;
            public string PathText { get; set; } = string.Empty;
            public string SourceBadgeText { get; set; } = string.Empty;
            public string RiskBadgeText { get; set; } = string.Empty;
            public CatalogBadgeTone RiskTone { get; set; } = CatalogBadgeTone.Neutral;
            public string SearchBlob { get; set; } = string.Empty;
            public int DefaultRank { get; set; }
            public bool IsDefaultSuggestion { get; set; }
            public GlobalSearchNavigationTarget NavigationTarget { get; set; } = new GlobalSearchNavigationTarget();
        }
    }
}
