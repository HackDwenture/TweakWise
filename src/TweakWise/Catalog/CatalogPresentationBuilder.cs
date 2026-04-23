using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Catalog
{
    public static class CatalogPresentationBuilder
    {
        public static IReadOnlyList<SettingCardViewModel> BuildSettingCards(IEnumerable<TweakDefinition> tweaks)
        {
            return tweaks
                .Select(BuildSettingCard)
                .ToList();
        }

        public static SettingCardViewModel BuildSettingCard(TweakDefinition tweak)
        {
            return new SettingCardViewModel
            {
                Id = tweak.Id,
                Category = tweak.Category,
                Subcategory = tweak.Subcategory,
                Title = tweak.Title,
                ShortDescription = tweak.ShortDescription,
                LongDescription = tweak.LongDescription,
                CurrentState = tweak.CurrentState,
                RecommendedState = tweak.RecommendedState,
                RiskBadgeText = $"Риск: {GetRiskText(tweak.RiskLevel)}",
                RiskTone = GetRiskTone(tweak.RiskLevel),
                SourceBadgeText = $"Источник: {GetSourceText(tweak.SourceType)}",
                SourceTone = CatalogBadgeTone.Info,
                RestartBadgeText = tweak.RequiresRestart ? "Потребуется перезапуск" : "Без перезапуска",
                RestartTone = tweak.RequiresRestart ? CatalogBadgeTone.Warning : CatalogBadgeTone.Neutral,
                RollbackBadgeText = tweak.IsReversible ? "Можно откатить" : "Откат ограничен",
                RollbackTone = tweak.IsReversible ? CatalogBadgeTone.Success : CatalogBadgeTone.Danger,
                TechnicalSummary = tweak.AdvancedDetails?.TechnicalSummary ?? string.Empty,
                AffectedComponents = tweak.AdvancedDetails?.AffectedComponents?.ToList() ?? new List<string>(),
                TechnicalNotes = tweak.AdvancedDetails?.Notes?.ToList() ?? new List<string>(),
                PreviewSummary = tweak.PreviewMeta?.Summary ?? string.Empty,
                PreviewEstimatedImpact = tweak.PreviewMeta?.EstimatedImpact ?? string.Empty,
                PreviewItems = tweak.PreviewMeta?.SampleItems?.ToList() ?? new List<string>(),
                ConfirmationText = tweak.PreviewMeta?.ConfirmationHint ?? string.Empty,
                RequiresConfirmation = tweak.RequiresConfirmation,
                RollbackSummary = tweak.RollbackMeta?.RollbackSummary ?? string.Empty,
                ValidationHint = tweak.RollbackMeta?.ValidationHint ?? string.Empty
            };
        }

        public static IReadOnlyList<LocalTemplateCardViewModel> BuildTemplateCards(
            IEnumerable<TweakTemplateDefinition> templates,
            IEnumerable<TweakDefinition> tweaks)
        {
            var tweakMap = tweaks.ToDictionary(tweak => tweak.Id, tweak => tweak);

            return templates.Select(template =>
            {
                var includedItems = template.TweakIds
                    .Where(tweakMap.ContainsKey)
                    .Select(id => tweakMap[id].Title)
                    .ToList();

                return new LocalTemplateCardViewModel
                {
                    Id = template.Id,
                    Title = template.Title,
                    Description = template.Description,
                    ScopeText = template.ScopeLabel,
                    AudienceText = $"Для кого: {template.Audience}",
                    RiskBadgeText = $"Риск: {GetRiskText(template.RiskLevel)}",
                    RiskTone = GetRiskTone(template.RiskLevel),
                    RestartBadgeText = template.RequiresRestart ? "Возможен перезапуск" : "Без обязательного перезапуска",
                    RestartTone = template.RequiresRestart ? CatalogBadgeTone.Warning : CatalogBadgeTone.Neutral,
                    IncludedItems = includedItems
                };
            }).ToList();
        }

        public static string GetCategoryTitle(string categoryId)
        {
            return categoryId switch
            {
                "WindowsInterface" => "Интерфейс Windows",
                "System" => "Система",
                "Maintenance" => "Обслуживание",
                "MonitoringPerformance" => "Мониторинг и производительность",
                _ => categoryId
            };
        }

        public static string GetRiskText(TweakRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                TweakRiskLevel.Low => "низкий",
                TweakRiskLevel.Medium => "умеренный",
                TweakRiskLevel.High => "высокий",
                _ => "не определён"
            };
        }

        public static string GetSourceText(TweakSourceType sourceType)
        {
            return sourceType switch
            {
                TweakSourceType.Windows => "Windows",
                TweakSourceType.Registry => "реестр",
                TweakSourceType.Policy => "политика",
                TweakSourceType.Service => "служба",
                TweakSourceType.Task => "задача",
                _ => "смешанный источник"
            };
        }

        private static CatalogBadgeTone GetRiskTone(TweakRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                TweakRiskLevel.Low => CatalogBadgeTone.Success,
                TweakRiskLevel.Medium => CatalogBadgeTone.Warning,
                TweakRiskLevel.High => CatalogBadgeTone.Danger,
                _ => CatalogBadgeTone.Neutral
            };
        }
    }
}
