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
    public partial class WindowsInterfacePage : Page
    {
        private readonly ITweakCatalogProvider _catalogProvider;
        private List<TweakDefinition> _allTweaks = new List<TweakDefinition>();
        private SettingsFilterKind _selectedFilter = SettingsFilterKind.All;
        private GlobalSearchNavigationTarget _pendingSearchTarget;
        private bool _isViewReady;

        public WindowsInterfacePage()
        {
            InitializeComponent();

            _isViewReady = true;
            _catalogProvider = App.TweakCatalogProvider ?? new MockTweakCatalogProvider();
            LoadPage();
        }

        private void LoadPage()
        {
            _allTweaks = _catalogProvider
                .GetTweaksByCategory("WindowsInterface")
                .Where(tweak => WindowsInterfaceCatalogSeed.SectionOrder.Contains(tweak.Subcategory))
                .ToList();

            _pendingSearchTarget = GlobalSearchNavigationStore.ConsumeForPage("WindowsInterface");

            var templates = _catalogProvider
                .GetTemplates()
                .Where(template => WindowsInterfaceCatalogSeed.LocalTemplateIds.Contains(template.Id))
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
                "Recommended" => SettingsFilterKind.Recommended,
                "Hidden" => SettingsFilterKind.Hidden,
                "Safe" => SettingsFilterKind.Safe,
                "RequiresRestart" => SettingsFilterKind.RequiresRestart,
                "Advanced" => SettingsFilterKind.Advanced,
                _ => SettingsFilterKind.All
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
                SettingsFilterKind.Recommended => HasTag(tweak, "featured", "recommended"),
                SettingsFilterKind.Hidden => HasTag(tweak, "hidden"),
                SettingsFilterKind.Safe => tweak.RiskLevel == TweakRiskLevel.Low && tweak.IsReversible,
                SettingsFilterKind.RequiresRestart => tweak.RequiresRestart,
                SettingsFilterKind.Advanced => HasTag(tweak, "advanced", "pro"),
                _ => true
            };
        }

        private static bool HasTag(TweakDefinition tweak, params string[] tags)
        {
            return tweak.Tags.Any(tag => tags.Any(expected => string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase)));
        }

        private static List<InterfaceSectionViewModel> BuildSections(
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

            return WindowsInterfaceCatalogSeed.SectionOrder
                .Select(subcategory => new InterfaceSectionViewModel
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
                SettingsFilterKind.Recommended => "рекомендованные",
                SettingsFilterKind.Hidden => "скрытые",
                SettingsFilterKind.Safe => "безопасные",
                SettingsFilterKind.RequiresRestart => "требуют перезапуск",
                SettingsFilterKind.Advanced => "продвинутые",
                _ => "все"
            };

            return $"Показано {resultsCount} настроек • фильтр: {filterText}";
        }

        private static string GetSectionDescription(string subcategory)
        {
            return subcategory switch
            {
                "Проводник" => "Видимость файлов, стартовое окно, плотность списка и другие повседневные настройки Проводника.",
                "Меню Пуск" => "Настройки, которые делают запуск приложений чище, компактнее и спокойнее.",
                "Панель задач" => "Управление положением кнопок и отключение лишних элементов на панели задач.",
                "Контекстное меню" => "Ускорение повседневных действий через более короткое и полезное меню правой кнопки.",
                "Поиск" => "Локальный поиск, приоритет результатов и снижение визуального шума в поисковой панели.",
                "Рабочий стол" => "Вид рабочего стола, системные значки и компактность повседневного окружения.",
                "Уведомления" => "Баннеры, подсказки и правила тишины, чтобы уведомления меньше отвлекали.",
                _ => "Связанные настройки интерфейса."
            };
        }

        private void TryHandlePendingSearchNavigation(
            IReadOnlyList<InterfaceSectionViewModel> sections,
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

                InterfaceSectionViewModel targetSection = null;

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
                {
                    container.BringIntoView();
                }

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

        private enum SettingsFilterKind
        {
            All,
            Recommended,
            Hidden,
            Safe,
            RequiresRestart,
            Advanced
        }

        private sealed class InterfaceSectionViewModel
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public List<SettingCardViewModel> Settings { get; set; } = new List<SettingCardViewModel>();
            public string CountText => $"{Settings.Count} шт.";
        }
    }
}
