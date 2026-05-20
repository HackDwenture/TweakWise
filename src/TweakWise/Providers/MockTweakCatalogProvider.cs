using System;
using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    public sealed class MockTweakCatalogProvider : ITweakCatalogProvider
    {
        private readonly List<TweakCategoryDefinition> _categories;
        private readonly List<TweakDefinition> _tweaks;
        private readonly List<TweakTemplateDefinition> _templates;

        public MockTweakCatalogProvider()
        {
            _categories = BuildCategories();
            _tweaks = new List<TweakDefinition>();
            _templates = new List<TweakTemplateDefinition>();

            WindowsInterfaceCatalogSeed.ApplyToCategories(_categories);
            SystemCatalogSeed.ApplyToCategories(_categories);
            MaintenanceCatalogSeed.ApplyToCategories(_categories);
            MonitoringPerformanceCatalogSeed.ApplyToCategories(_categories);
        }

        public IReadOnlyList<TweakCategoryDefinition> GetCategories() => _categories;

        public IReadOnlyList<TweakDefinition> GetTweaks() => _tweaks;

        public IReadOnlyList<TweakDefinition> GetTweaksByCategory(string categoryId)
        {
            return _tweaks
                .Where(tweak => string.Equals(tweak.Category, categoryId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<TweakTemplateDefinition> GetTemplates() => _templates;

        private static List<TweakCategoryDefinition> BuildCategories()
        {
            return new List<TweakCategoryDefinition>
            {
                new()
                {
                    Id = "WindowsInterface",
                    Title = "Интерфейс",
                    Description = "Настройки внешнего вида и поведения интерфейса Windows.",
                    Icon = "▣"
                },
                new()
                {
                    Id = "System",
                    Title = "Система",
                    Description = "Системные настройки Windows, сгруппированные по смыслу.",
                    Icon = "⚙"
                },
                new()
                {
                    Id = "Maintenance",
                    Title = "Обслуживание",
                    Description = "Действия обслуживания, очистки и восстановления.",
                    Icon = "□"
                },
                new()
                {
                    Id = "MonitoringPerformance",
                    Title = "Мониторинг",
                    Description = "Состояние системы, показатели железа и профили производительности.",
                    Icon = "▥"
                }
            };
        }
    }
}
