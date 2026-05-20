using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    internal static class MaintenanceCatalogSeed
    {
        public static readonly IReadOnlyList<string> SectionOrder = new[]
        {
            "Очистка файлов",
            "Системные остатки",
            "Удаление программ",
            "Удаление встроенных приложений",
            "Быстрые исправления",
            "Обслуживание по расписанию"
        };

        public static readonly IReadOnlyList<string> LocalTemplateIds = new List<string>();

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "Maintenance");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
        }

        public static void EnrichExistingTemplates(List<TweakTemplateDefinition> templates)
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
