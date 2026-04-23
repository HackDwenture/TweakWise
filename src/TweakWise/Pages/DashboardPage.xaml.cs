using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using TweakWise.Catalog;
using TweakWise.Models;
using TweakWise.Providers;

namespace TweakWise.Pages
{
    public partial class DashboardPage : Page
    {
        private readonly ITweakCatalogProvider _catalogProvider;

        public DashboardPage()
        {
            InitializeComponent();

            _catalogProvider = App.TweakCatalogProvider ?? new MockTweakCatalogProvider();
            LoadDashboard();
        }

        private void LoadDashboard()
        {
            var categories = _catalogProvider.GetCategories();
            var tweaks = _catalogProvider.GetTweaks();
            var templates = _catalogProvider.GetTemplates();

            SummaryItemsControl.ItemsSource = BuildSummaryItems(categories, tweaks, templates);
            TemplatesItemsControl.ItemsSource = CatalogPresentationBuilder.BuildTemplateCards(templates, tweaks);
            FeaturedTweaksItemsControl.ItemsSource = CatalogPresentationBuilder.BuildSettingCards(
                tweaks.Where(tweak => tweak.Tags.Contains("featured")));
            CategoriesItemsControl.ItemsSource = BuildCategoryCards(categories, tweaks);
        }

        private static List<SummaryItemViewModel> BuildSummaryItems(
            IReadOnlyList<TweakCategoryDefinition> categories,
            IReadOnlyList<TweakDefinition> tweaks,
            IReadOnlyList<TweakTemplateDefinition> templates)
        {
            return new List<SummaryItemViewModel>
            {
                new() { Value = categories.Count.ToString(), Label = "смысловых раздела в целевой структуре" },
                new() { Value = tweaks.Count.ToString(), Label = "настроек уже описаны typed-моделью" },
                new() { Value = tweaks.Count(tweak => tweak.IsReversible).ToString(), Label = "настроек поддерживают обратимость" },
                new() { Value = templates.Count.ToString(), Label = "шаблона доступны с Главной" }
            };
        }

        private static List<CategoryCardViewModel> BuildCategoryCards(
            IReadOnlyList<TweakCategoryDefinition> categories,
            IReadOnlyList<TweakDefinition> tweaks)
        {
            return categories.Select(category =>
            {
                var categoryTweaks = tweaks
                    .Where(tweak => tweak.Category == category.Id)
                    .ToList();

                string sourceMix = string.Join(
                    " • ",
                    categoryTweaks
                        .Select(tweak => CatalogPresentationBuilder.GetSourceText(tweak.SourceType))
                        .Distinct());

                return new CategoryCardViewModel
                {
                    Icon = category.Icon,
                    Title = category.Title,
                    Description = category.Description,
                    Subcategories = $"Внутри: {string.Join(" • ", category.Subcategories)}",
                    Stats = $"{categoryTweaks.Count} настроек • {categoryTweaks.Count(tweak => tweak.RequiresRestart)} с перезапуском • {categoryTweaks.Count(tweak => tweak.RiskLevel == TweakRiskLevel.High)} высокого риска",
                    SourceMix = $"Источники: {sourceMix}"
                };
            }).ToList();
        }

        private sealed class SummaryItemViewModel
        {
            public string Value { get; set; }
            public string Label { get; set; }
        }

        private sealed class CategoryCardViewModel
        {
            public string Icon { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string Subcategories { get; set; }
            public string Stats { get; set; }
            public string SourceMix { get; set; }
        }
    }
}
