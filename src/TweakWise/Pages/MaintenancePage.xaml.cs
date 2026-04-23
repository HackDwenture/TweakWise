using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TweakWise.Catalog;
using TweakWise.Controls;
using TweakWise.Models;
using TweakWise.Providers;
using TweakWise.Search;

namespace TweakWise.Pages
{
    public partial class MaintenancePage : Page
    {
        private readonly ITweakCatalogProvider _catalogProvider;
        private List<TweakDefinition> _allTweaks = new List<TweakDefinition>();
        private MaintenanceFilterKind _selectedFilter = MaintenanceFilterKind.All;
        private GlobalSearchNavigationTarget _pendingSearchTarget;
        private bool _isViewReady;

        public MaintenancePage()
        {
            InitializeComponent();

            _isViewReady = true;
            _catalogProvider = App.TweakCatalogProvider ?? new MockTweakCatalogProvider();
            LoadPage();
        }

        private void LoadPage()
        {
            _allTweaks = _catalogProvider
                .GetTweaksByCategory("Maintenance")
                .Where(tweak => MaintenanceCatalogSeed.SectionOrder.Contains(tweak.Subcategory))
                .ToList();

            _pendingSearchTarget = GlobalSearchNavigationStore.ConsumeForPage("Maintenance");

            var templates = _catalogProvider
                .GetTemplates()
                .Where(template => MaintenanceCatalogSeed.LocalTemplateIds.Contains(template.Id))
                .ToList();

            TemplateItemsControl.ItemsSource = CatalogPresentationBuilder.BuildTemplateCards(templates, _allTweaks);
            ApplyFilters();
        }

        private void FilterButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isViewReady)
                return;

            if (sender is not ToggleButton button || button.Tag is not string filterTag)
                return;

            _selectedFilter = filterTag switch
            {
                "Recommended" => MaintenanceFilterKind.Recommended,
                "Safe" => MaintenanceFilterKind.Safe,
                "RequiresConfirmation" => MaintenanceFilterKind.RequiresConfirmation,
                "RequiresRestart" => MaintenanceFilterKind.RequiresRestart,
                "Advanced" => MaintenanceFilterKind.Advanced,
                _ => MaintenanceFilterKind.All
            };

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (!_isViewReady ||
                TemplateItemsControl == null ||
                ResultsSummaryTextBlock == null ||
                EmptyStateBorder == null ||
                SectionsItemsControl == null)
            {
                return;
            }

            var filteredTweaks = _allTweaks
                .Where(MatchesSelectedFilter)
                .ToList();

            var sections = BuildSections(filteredTweaks, _pendingSearchTarget);
            SectionsItemsControl.ItemsSource = sections;
            SectionsItemsControl.Visibility = sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateBorder.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ResultsSummaryTextBlock.Text = BuildResultsSummary(filteredTweaks.Count);
            TryHandlePendingSearchNavigation(sections, _pendingSearchTarget);
        }

        private bool MatchesSelectedFilter(TweakDefinition tweak)
        {
            return _selectedFilter switch
            {
                MaintenanceFilterKind.Recommended => HasTag(tweak, "featured", "recommended"),
                MaintenanceFilterKind.Safe => tweak.RiskLevel == TweakRiskLevel.Low && (!tweak.RequiresConfirmation || tweak.IsReversible),
                MaintenanceFilterKind.RequiresConfirmation => tweak.RequiresConfirmation,
                MaintenanceFilterKind.RequiresRestart => tweak.RequiresRestart,
                MaintenanceFilterKind.Advanced => HasTag(tweak, "advanced", "pro"),
                _ => true
            };
        }

        private static bool HasTag(TweakDefinition tweak, params string[] tags)
        {
            return tweak.Tags.Any(tag => tags.Any(expected => string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase)));
        }

        private static List<MaintenanceSectionViewModel> BuildSections(
            IReadOnlyList<TweakDefinition> tweaks,
            GlobalSearchNavigationTarget pendingTarget)
        {
            var settingCards = CatalogPresentationBuilder.BuildSettingCards(tweaks).ToList();

            if (!string.IsNullOrWhiteSpace(pendingTarget?.ItemId))
            {
                var targetCard = settingCards.FirstOrDefault(setting =>
                    string.Equals(setting.Id, pendingTarget.ItemId, StringComparison.OrdinalIgnoreCase));

                if (targetCard != null)
                    targetCard.IsHighlighted = true;
            }

            return MaintenanceCatalogSeed.SectionOrder
                .Select(subcategory => new MaintenanceSectionViewModel
                {
                    Title = subcategory,
                    Description = GetSectionDescription(subcategory),
                    Settings = settingCards
                        .Where(setting => setting.Subcategory == subcategory)
                        .ToList()
                })
                .Where(section => section.Settings.Count > 0)
                .ToList();
        }

        private string BuildResultsSummary(int resultsCount)
        {
            string filterText = _selectedFilter switch
            {
                MaintenanceFilterKind.Recommended => "рекомендуемые",
                MaintenanceFilterKind.Safe => "безопасные",
                MaintenanceFilterKind.RequiresConfirmation => "требуют подтверждение",
                MaintenanceFilterKind.RequiresRestart => "требуют перезапуск",
                MaintenanceFilterKind.Advanced => "продвинутые",
                _ => "все"
            };

            return $"Показано {resultsCount} элементов обслуживания • фильтр: {filterText}";
        }

        private static string GetSectionDescription(string subcategory)
        {
            return subcategory switch
            {
                "Очистка файлов" => "Понятная ручная и полуавтоматическая очистка файлов без случайного удаления нужного.",
                "Системные остатки" => "Остатки обновлений, delivery cache и другие хвосты, которые разумно убирать только после предпросмотра.",
                "Удаление программ" => "Выборочная деинсталляция крупных и редко нужных программ без хаотичного удаления всего подряд.",
                "Удаление встроенных приложений" => "Осознанная работа со встроенными приложениями Windows, где технические package names спрятаны в деталях.",
                "Быстрые исправления" => "Safe repair actions для типичных проблем с сетью, обновлениями и системными компонентами.",
                "Обслуживание по расписанию" => "Спокойные сценарии напоминаний и регулярного обслуживания вместо навязчивого фонового «ускорения».",
                _ => "Связанные сценарии обслуживания."
            };
        }

        private void TryHandlePendingSearchNavigation(
            IReadOnlyList<MaintenanceSectionViewModel> sections,
            GlobalSearchNavigationTarget pendingTarget)
        {
            if (pendingTarget == null)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (pendingTarget.ResultKind == GlobalSearchResultKind.Template)
                {
                    TemplateItemsControl.BringIntoView();
                    return;
                }

                MaintenanceSectionViewModel targetSection = null;

                if (!string.IsNullOrWhiteSpace(pendingTarget.Subcategory))
                {
                    targetSection = sections.FirstOrDefault(section =>
                        string.Equals(section.Title, pendingTarget.Subcategory, StringComparison.OrdinalIgnoreCase));
                }

                if (targetSection == null && !string.IsNullOrWhiteSpace(pendingTarget.ItemId))
                {
                    targetSection = sections.FirstOrDefault(section =>
                        section.Settings.Any(setting => string.Equals(setting.Id, pendingTarget.ItemId, StringComparison.OrdinalIgnoreCase)));
                }

                if (targetSection == null)
                    return;

                SectionsItemsControl.UpdateLayout();

                if (!string.IsNullOrWhiteSpace(pendingTarget.ItemId))
                {
                    var targetCard = FindSettingCardControl(SectionsItemsControl, pendingTarget.ItemId);
                    if (targetCard != null)
                    {
                        targetCard.BringIntoView();
                        _pendingSearchTarget = null;
                        return;
                    }
                }

                if (SectionsItemsControl.ItemContainerGenerator.ContainerFromItem(targetSection) is FrameworkElement container)
                    container.BringIntoView();

                _pendingSearchTarget = null;
            }), DispatcherPriority.Background);
        }

        private static SettingCardControl FindSettingCardControl(DependencyObject parent, string itemId)
        {
            if (parent == null)
                return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int index = 0; index < childrenCount; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);

                if (child is SettingCardControl cardControl &&
                    string.Equals(cardControl.Setting?.Id, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return cardControl;
                }

                var nestedMatch = FindSettingCardControl(child, itemId);
                if (nestedMatch != null)
                    return nestedMatch;
            }

            return null;
        }

        private enum MaintenanceFilterKind
        {
            All,
            Recommended,
            Safe,
            RequiresConfirmation,
            RequiresRestart,
            Advanced
        }

        private sealed class MaintenanceSectionViewModel
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public List<SettingCardViewModel> Settings { get; set; } = new List<SettingCardViewModel>();
            public string CountText => $"{Settings.Count} шт.";
        }
    }
}
