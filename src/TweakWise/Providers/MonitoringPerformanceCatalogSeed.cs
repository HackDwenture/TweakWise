using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    internal static class MonitoringPerformanceCatalogSeed
    {
        public static readonly IReadOnlyList<string> SectionOrder = new[]
        {
            "Питание и лимиты",
            "CPU и планировщик",
            "GPU и графический стек",
            "Оперативная память",
            "Охлаждение и датчики",
            "Безопасный тюнинг",
            "Расширенные параметры"
        };

        public static readonly IReadOnlyList<string> LocalProfileIds = new List<string>();

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "MonitoringPerformance");
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
