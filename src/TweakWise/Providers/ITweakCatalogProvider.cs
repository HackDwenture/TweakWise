using System.Collections.Generic;
using TweakWise.Models;

namespace TweakWise.Providers
{
    public interface ITweakCatalogProvider
    {
        IReadOnlyList<TweakCategoryDefinition> GetCategories();
        IReadOnlyList<TweakDefinition> GetTweaks();
        IReadOnlyList<TweakDefinition> GetTweaksByCategory(string categoryId);
        IReadOnlyList<TweakTemplateDefinition> GetTemplates();
    }
}
