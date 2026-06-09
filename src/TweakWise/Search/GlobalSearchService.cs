using System;
using System.Collections.Generic;
using System.Linq;

namespace TweakWise.Search
{
    public sealed class GlobalSearchService
    {
        private readonly List<GlobalSearchIndexEntry> _index;

        public GlobalSearchService()
        {
            _index = BuildIndex();
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

        private static List<GlobalSearchIndexEntry> BuildIndex()
        {
            var entries = new List<GlobalSearchIndexEntry>();

            AddSection(
                entries,
                0,
                "Главная",
                "Раздел приложения",
                "Карта состояния, быстрые показатели и запуск проверки",
                "Dashboard",
                "dashboard home карта ядро проверка");

            AddSection(
                entries,
                10,
                "Рабочая среда",
                "Раздел",
                "Проводник, меню Пуск, панель задач, рабочий стол, поиск и уведомления",
                "WorkEnvironment",
                "windows interface explorer start taskbar desktop notifications");

            AddSection(
                entries,
                11,
                "Производительность и охлаждение",
                "Раздел",
                "Питание, CPU, GPU, RAM, охлаждение, датчики и безопасный тюнинг",
                "MonitoringPerformance",
                "performance cooling thermal power cpu gpu ram");

            AddSection(
                entries,
                12,
                "Устройства и драйверы",
                "Раздел",
                "Реальные устройства, драйверы, подписи, резервные копии и откаты",
                "DevicesDrivers",
                "devices drivers pnp inf rollback backup safe mode");

            AddSubsections(entries, "Рабочая среда", "WorkEnvironment", 100, new[]
            {
                "Проводник",
                "Меню Пуск",
                "Панель задач",
                "Контекстное меню",
                "Поиск Windows",
                "Рабочий стол",
                "Уведомления"
            });

            AddSubsections(entries, "Производительность и охлаждение", "MonitoringPerformance", 130, new[]
            {
                "Питание",
                "CPU и планировщик",
                "GPU и графический стек",
                "Оперативная память",
                "Охлаждение и датчики",
                "Безопасный тюнинг",
                "Расширенные параметры"
            });

            AddSubsections(entries, "Устройства и драйверы", "DevicesDrivers", 160, new[]
            {
                "Драйверы",
                "Устройства",
                "Резервные копии драйверов",
                "Откат драйверов",
                "Безопасный режим",
                "Оценка рисков"
            });

            AddAction(entries, 250, "Открыть настройки", "OpenSettings", "settings параметры тема запуск автопроверка");
            AddAction(entries, 251, "Открыть уведомления", "OpenNotifications", "notifications сообщения проблемы рекомендации");
            AddAction(entries, 252, "Проверить обновления", "CheckUpdates", "update release версия github");

            return entries;
        }

        private static void AddSection(
            List<GlobalSearchIndexEntry> entries,
            int rank,
            string title,
            string resultType,
            string description,
            string pageKey,
            string aliases)
        {
            entries.Add(new GlobalSearchIndexEntry
            {
                Title = title,
                ResultTypeText = resultType,
                PathText = "Раздел приложения",
                SearchBlob = BuildSearchBlob(title, resultType, description, pageKey, aliases),
                DefaultRank = rank,
                IsDefaultSuggestion = true,
                NavigationTarget = new GlobalSearchNavigationTarget
                {
                    PageKey = pageKey,
                    ResultKind = GlobalSearchResultKind.Section
                }
            });
        }

        private static void AddSubsections(
            List<GlobalSearchIndexEntry> entries,
            string sectionTitle,
            string pageKey,
            int startRank,
            IReadOnlyList<string> subsectionTitles)
        {
            for (int index = 0; index < subsectionTitles.Count; index++)
            {
                string title = subsectionTitles[index];
                entries.Add(new GlobalSearchIndexEntry
                {
                    Title = title,
                    ResultTypeText = "Секция",
                    PathText = $"{sectionTitle} > {title}",
                    SearchBlob = BuildSearchBlob(title, sectionTitle, pageKey),
                    DefaultRank = startRank + index,
                    NavigationTarget = new GlobalSearchNavigationTarget
                    {
                        PageKey = pageKey,
                        ResultKind = GlobalSearchResultKind.Subsection
                    }
                });
            }
        }

        private static void AddAction(
            List<GlobalSearchIndexEntry> entries,
            int rank,
            string title,
            string actionKey,
            string aliases)
        {
            entries.Add(new GlobalSearchIndexEntry
            {
                Title = title,
                ResultTypeText = "Действие",
                PathText = "Быстрое действие",
                SearchBlob = BuildSearchBlob(title, actionKey, aliases),
                DefaultRank = rank,
                IsDefaultSuggestion = true,
                NavigationTarget = new GlobalSearchNavigationTarget
                {
                    ActionKey = actionKey,
                    ResultKind = GlobalSearchResultKind.Action
                }
            });
        }

        private static GlobalSearchResultViewModel MapToViewModel(GlobalSearchIndexEntry entry)
        {
            return new GlobalSearchResultViewModel
            {
                Title = entry.Title,
                ResultTypeText = entry.ResultTypeText,
                PathText = entry.PathText,
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
                .Split(new[] { ' ', '>', '/', '-', ',', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
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

        private sealed class GlobalSearchIndexEntry
        {
            public string Title { get; set; } = string.Empty;
            public string ResultTypeText { get; set; } = string.Empty;
            public string PathText { get; set; } = string.Empty;
            public string SearchBlob { get; set; } = string.Empty;
            public int DefaultRank { get; set; }
            public bool IsDefaultSuggestion { get; set; }
            public GlobalSearchNavigationTarget NavigationTarget { get; set; } = new GlobalSearchNavigationTarget();
        }
    }
}
