using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    internal static class WindowsInterfaceCatalogSeed
    {
        public static readonly IReadOnlyList<string> SectionOrder = new[]
        {
            "Проводник",
            "Меню Пуск",
            "Панель задач",
            "Контекстное меню",
            "Поиск",
            "Рабочий стол",
            "Уведомления"
        };

        public static readonly IReadOnlyList<string> LocalTemplateIds = new List<string>();

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "WindowsInterface");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
        }

        public static List<TweakDefinition> BuildAdditionalTweaks()
        {
            return new List<TweakDefinition>();
        }

        public static List<TweakTemplateDefinition> BuildAdditionalTemplates()
        {
            return new List<TweakTemplateDefinition>();
        }
    }
}
