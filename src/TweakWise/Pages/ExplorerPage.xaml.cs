using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using TweakWise.Catalog;
using TweakWise.Models;
using TweakWise.Providers;

namespace TweakWise.Pages
{
    public partial class ExplorerPage : Page
    {
        private static readonly string[] SectionOrder =
        {
            "Проводник",
            "Меню Пуск",
            "Панель задач"
        };

        private readonly ITweakCatalogProvider _catalogProvider;

        public ExplorerPage()
        {
            InitializeComponent();

            _catalogProvider = App.TweakCatalogProvider ?? new MockTweakCatalogProvider();
            LoadPage();
        }

        private void LoadPage()
        {
            var tweaks = _catalogProvider
                .GetTweaksByCategory("WindowsInterface")
                .Where(tweak => SectionOrder.Contains(tweak.Subcategory))
                .ToList();

            var templates = _catalogProvider
                .GetTemplates()
                .Where(template => template.TweakIds.Any(id => tweaks.Any(tweak => tweak.Id == id)))
                .ToList();

            TemplateItemsControl.ItemsSource = CatalogPresentationBuilder.BuildTemplateCards(templates, tweaks);
            SectionsItemsControl.ItemsSource = BuildSections(tweaks);
        }

        private static List<InterfaceSectionViewModel> BuildSections(IReadOnlyList<TweakDefinition> tweaks)
        {
            var settingCards = CatalogPresentationBuilder.BuildSettingCards(tweaks).ToList();

            return SectionOrder
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

        private static string GetSectionDescription(string subcategory)
        {
            return subcategory switch
            {
                "Проводник" => "Видимость файлов, быстрый доступ и привычное поведение Проводника собраны в одном месте.",
                "Меню Пуск" => "Здесь находятся настройки, которые делают запуск приложений чище, спокойнее и понятнее.",
                "Панель задач" => "Эта группа помогает убрать лишние элементы и оставить на панели задач только нужные пользователю действия.",
                _ => "Связанные настройки интерфейса."
            };
        }

        private sealed class InterfaceSectionViewModel
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public List<SettingCardViewModel> Settings { get; set; } = new List<SettingCardViewModel>();
        }
    }
}
