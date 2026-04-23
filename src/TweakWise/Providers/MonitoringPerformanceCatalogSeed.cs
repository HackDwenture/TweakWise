using System;
using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    internal static class MonitoringPerformanceCatalogSeed
    {
        public static readonly IReadOnlyList<string> SectionOrder = new[]
        {
            "Состояние системы",
            "Температуры и датчики",
            "CPU / GPU / RAM / диск",
            "Батарея и SSD",
            "Профили производительности",
            "Вентиляторы и охлаждение",
            "Безопасный тюнинг",
            "Расширенные параметры"
        };

        public static readonly IReadOnlyList<string> LocalProfileIds = new[]
        {
            "monitor-profile-quiet",
            "monitor-profile-balance",
            "monitor-profile-performance",
            "monitor-profile-maximum"
        };

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "MonitoringPerformance");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
            UpdateTweak(tweaks, "visual-effects-balanced", tweak =>
            {
                tweak.Subcategory = "Безопасный тюнинг";
                tweak.Tags = MergeTags(tweak.Tags, "recommended", "tuning");
            });

            UpdateTweak(tweaks, "background-apps-limit", tweak =>
            {
                tweak.Subcategory = "Безопасный тюнинг";
                tweak.Title = "Сдержать лишний фон ради более ровной производительности";
                tweak.ShortDescription = "Помогает системе оставаться отзывчивой без агрессивного отключения полезных сценариев.";
                tweak.Tags = MergeTags(tweak.Tags, "tuning", "advanced");
            });

            UpdateTweak(tweaks, "power-plan-performance", tweak =>
            {
                tweak.Subcategory = "Профили производительности";
                tweak.Title = "Выбрать понятный профиль производительности";
                tweak.CurrentState = "Баланс";
                tweak.RecommendedState = "По текущему сценарию";
                tweak.Tags = MergeTags(tweak.Tags, "recommended", "profile");
            });
        }

        public static List<TweakDefinition> BuildAdditionalTweaks()
        {
            return new List<TweakDefinition>
            {
                CreateTweak(
                    id: "cooling-quiet-response",
                    subcategory: "Вентиляторы и охлаждение",
                    title: "Сделать охлаждение спокойнее в повседневной работе",
                    shortDescription: "Подходит для ежедневного сценария, когда важны тишина и ровный шум, а не максимальный разгон вентиляторов.",
                    longDescription: "Сценарий помогает выбрать более спокойное поведение охлаждения без экстремальных вмешательств: вентиляторы реагируют мягче, а система остаётся в комфортном температурном диапазоне.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартный отклик",
                    recommendedState: "Спокойное охлаждение",
                    tags: new[] { "recommended", "profile", "cooling" },
                    technicalSummary: "Работает с профильными параметрами охлаждения и поведенческими флагами управления шумом.",
                    affectedComponents: new[] { "Cooling policy", "Fan response", "Thermal comfort" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Систему можно вернуть к более агрессивному охлаждению без потери текущего профиля питания.",
                    validationHint: "После переключения оцените шум и рост температур в обычной нагрузке.",
                    notes: new[] { "Подходит ноутбукам и тихим рабочим ПК." }),

                CreateTweak(
                    id: "cooling-fast-response",
                    subcategory: "Вентиляторы и охлаждение",
                    title: "Поднимать охлаждение быстрее под тяжёлой нагрузкой",
                    shortDescription: "Уместно для длительных компиляций, рендеринга и других рабочих задач, где важнее удержать температуру и частоты.",
                    longDescription: "Вместо ручного ковыряния в BIOS или сырых профилях пользователь получает понятное действие: охлаждение реагирует быстрее, чтобы система дольше оставалась в рабочем диапазоне частот.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартная реакция",
                    recommendedState: "Более быстрый отклик",
                    tags: new[] { "advanced", "pro", "cooling" },
                    technicalSummary: "Использует профильные параметры охлаждения и связанное поведение энергопрофиля устройства.",
                    affectedComponents: new[] { "Thermal response", "Fan curve bias", "Power profile coupling" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Быстрый отклик охлаждения можно вернуть к стандартному сценарию одним переключением.",
                    validationHint: "Проверьте шум и температуру на длинной нагрузке вроде сборки проекта или экспорта.",
                    notes: new[] { "Это безопасный пользовательский тюнинг, а не экстремальная ручная кривая." }),

                CreateTweak(
                    id: "safe-boost-bias",
                    subcategory: "Безопасный тюнинг",
                    title: "Сдержать агрессивный boost ради температуры и шума",
                    shortDescription: "Полезно, если система постоянно упирается в температуру и скачет между тихим и шумным режимом.",
                    longDescription: "Сценарий аккуратно сдерживает самые резкие пики ускорения, чтобы CPU и GPU вели себя ровнее, а шум не рос рывками во время обычной работы.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартный boost",
                    recommendedState: "Более ровный отклик",
                    tags: new[] { "recommended", "tuning", "advanced" },
                    technicalSummary: "Объединяет безопасные ограничения boost-поведения и профильные параметры энергопотребления.",
                    affectedComponents: new[] { "CPU boost behavior", "GPU power balance", "Thermal spikes" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Ограничение boost можно снять и вернуть исходную отзывчивость профиля.",
                    validationHint: "Сравните температуру, шум и стабильность частот до и после изменения под одинаковой нагрузкой.",
                    notes: new[] { "Не является экстремальным андервольтом и не требует низкоуровневых хакингов." }),

                CreateTweak(
                    id: "safe-memory-pressure-balance",
                    subcategory: "Безопасный тюнинг",
                    title: "Сделать работу с памятью спокойнее под многозадачность",
                    shortDescription: "Помогает сгладить поведение системы, когда одновременно открыто много тяжёлых приложений и вкладок.",
                    longDescription: "Вместо raw параметров файла подкачки или memory manager пользователь получает понятный сценарий: меньше неожиданных рывков и больше предсказуемости под рабочую многозадачность.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Стандартный режим",
                    recommendedState: "Сбалансированное давление памяти",
                    tags: new[] { "recommended", "tuning", "memory" },
                    technicalSummary: "Использует безопасные параметры управления памятью и связанную компрессию страниц.",
                    affectedComponents: new[] { "Memory compression", "Paging behavior", "Multitasking stability" },
                    rollbackType: "Откат после перезапуска",
                    rollbackSummary: "Параметры памяти можно вернуть к стандартным после повторного входа или перезапуска.",
                    validationHint: "Откройте типичный набор приложений и проверьте плавность переключения между ними.",
                    notes: new[] { "Подходит ноутбукам и рабочим ПК без большого запаса RAM." }),

                CreateTweak(
                    id: "advanced-processor-limits",
                    subcategory: "Расширенные параметры",
                    title: "Точно настроить пределы производительности процессора",
                    shortDescription: "Для pro-сценария, когда нужен более явный контроль над тем, насколько агрессивно процессор ускоряется под нагрузкой.",
                    longDescription: "Это уже расширенный режим: можно сместить баланс между пиком производительности, температурой и временем удержания boost. Технические детали остаются внутри карточки.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Скрытые системные лимиты",
                    recommendedState: "Явный профиль под сценарий",
                    tags: new[] { "advanced", "pro", "tuning" },
                    technicalSummary: "Работает с дополнительными параметрами процессорной политики, которые обычно скрыты от обычного пользователя.",
                    affectedComponents: new[] { "Processor policy", "Boost sustain", "Energy policy" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Пределы процессора можно вернуть к штатным значениям без сброса всего профиля питания.",
                    validationHint: "Проверьте частоты, температуру и длительную нагрузку вроде рендера или сборки.",
                    notes: new[] { "Raw GUID и скрытые power settings показываются только в details." }),

                CreateTweak(
                    id: "advanced-gpu-scheduling-review",
                    subcategory: "Расширенные параметры",
                    title: "Проверить расширенный режим планирования GPU осознанно",
                    shortDescription: "Подходит pro-пользователю, который хочет понять, помогает ли расширенное планирование именно его рабочему сценарию.",
                    longDescription: "Сценарий не обещает магический прирост. Он лишь помогает аккуратно протестировать режим, который на одних системах даёт более ровный отклик, а на других почти ничего не меняет.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Стандартное планирование",
                    recommendedState: "Проверить под своей нагрузкой",
                    tags: new[] { "advanced", "pro", "gpu" },
                    technicalSummary: "Затрагивает режим аппаратного планирования GPU и связанную очередь обработки задач.",
                    affectedComponents: new[] { "GPU scheduling", "Graphics queue", "Display pipeline" },
                    rollbackType: "Откат после перезапуска",
                    rollbackSummary: "Режим можно быстро вернуть, если он не даёт выгоды на текущем железе и драйверах.",
                    validationHint: "Оцените плавность интерфейса и рабочую нагрузку GPU до и после перезапуска.",
                    notes: new[] { "Это не игровой тумблер, а аккуратная проверка для универсального использования." }),

                CreateTweak(
                    id: "advanced-storage-latency-bias",
                    subcategory: "Расширенные параметры",
                    title: "Сместить поведение диска в сторону отзывчивости",
                    shortDescription: "Помогает pro-пользователю аккуратно проверить, можно ли получить более ровный отклик системы под активную работу с файлами и проектами.",
                    longDescription: "Сценарий касается расширенных параметров кэша и фоновой активности диска. Подходит только тем, кто понимает компромисс между отзывчивостью, безопасностью и поведением накопителя.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.High,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Стандартная стратегия",
                    recommendedState: "Тестовый pro-режим",
                    tags: new[] { "advanced", "pro", "storage" },
                    technicalSummary: "Работает с расширенными параметрами очередей хранения и политиками write/read balance.",
                    affectedComponents: new[] { "Storage queue", "Write cache policy", "Background storage behavior" },
                    rollbackType: "Откат после перезапуска",
                    rollbackSummary: "Расширенные параметры диска лучше тестировать точечно и быстро возвращать, если эффект сомнителен.",
                    validationHint: "Перед применением сравните типичную работу с большими проектами и очередь диска.",
                    notes: new[] { "Технические названия политик и устройств остаются только в details." })
            };
        }

        public static List<TweakTemplateDefinition> BuildAdditionalTemplates()
        {
            return new List<TweakTemplateDefinition>
            {
                CreateTemplate(
                    id: "monitor-profile-quiet",
                    title: "Тихий",
                    description: "Для спокойной повседневной работы: меньше шума, мягче охлаждение и приоритет тишины над пиковыми частотами.",
                    scopeLabel: "Мониторинг и производительность / Профили",
                    audience: "Обычный пользователь и ноутбук",
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "power-plan-performance",
                        "cooling-quiet-response",
                        "visual-effects-balanced"
                    }),

                CreateTemplate(
                    id: "monitor-profile-balance",
                    title: "Баланс",
                    description: "Универсальный профиль для ежедневной работы: нормальный отклик системы без лишнего шума и перегрева.",
                    scopeLabel: "Мониторинг и производительность / Профили",
                    audience: "Обычный и pro-пользователь",
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "power-plan-performance",
                        "visual-effects-balanced",
                        "safe-memory-pressure-balance"
                    }),

                CreateTemplate(
                    id: "monitor-profile-performance",
                    title: "Производительность",
                    description: "Для тяжёлых рабочих задач, когда важны частоты, ровная нагрузка и более активное охлаждение.",
                    scopeLabel: "Мониторинг и производительность / Профили",
                    audience: "Pro-пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "power-plan-performance",
                        "cooling-fast-response",
                        "safe-boost-bias"
                    }),

                CreateTemplate(
                    id: "monitor-profile-maximum",
                    title: "Максимум",
                    description: "Расширенный профиль для pro-сценариев, где шум и расход энергии допустимы ради максимального рабочего запаса.",
                    scopeLabel: "Мониторинг и производительность / Профили",
                    audience: "Pro-пользователь",
                    riskLevel: TweakRiskLevel.High,
                    requiresRestart: true,
                    tweakIds: new[]
                    {
                        "power-plan-performance",
                        "cooling-fast-response",
                        "advanced-processor-limits",
                        "advanced-gpu-scheduling-review"
                    })
            };
        }

        private static TweakDefinition CreateTweak(
            string id,
            string subcategory,
            string title,
            string shortDescription,
            string longDescription,
            TweakSourceType sourceType,
            TweakRiskLevel riskLevel,
            bool requiresRestart,
            bool isReversible,
            string currentState,
            string recommendedState,
            IEnumerable<string> tags,
            string technicalSummary,
            IEnumerable<string> affectedComponents,
            string rollbackType,
            string rollbackSummary,
            string validationHint,
            IEnumerable<string> notes = null)
        {
            return new TweakDefinition
            {
                Id = id,
                Category = "MonitoringPerformance",
                Subcategory = subcategory,
                Title = title,
                ShortDescription = shortDescription,
                LongDescription = longDescription,
                SourceType = sourceType,
                RiskLevel = riskLevel,
                RequiresRestart = requiresRestart,
                IsReversible = isReversible,
                CurrentState = currentState,
                RecommendedState = recommendedState,
                Tags = tags.ToList(),
                AdvancedDetails = new TweakAdvancedDetails
                {
                    TechnicalSummary = technicalSummary,
                    AffectedComponents = affectedComponents.ToList(),
                    Notes = notes?.ToList() ?? new List<string>()
                },
                RollbackMeta = new TweakRollbackMeta
                {
                    RollbackType = rollbackType,
                    RollbackSummary = rollbackSummary,
                    ValidationHint = validationHint
                }
            };
        }

        private static TweakTemplateDefinition CreateTemplate(
            string id,
            string title,
            string description,
            string scopeLabel,
            string audience,
            TweakRiskLevel riskLevel,
            bool requiresRestart,
            IEnumerable<string> tweakIds)
        {
            return new TweakTemplateDefinition
            {
                Id = id,
                Title = title,
                Description = description,
                ScopeLabel = scopeLabel,
                Audience = audience,
                RiskLevel = riskLevel,
                RequiresRestart = requiresRestart,
                TweakIds = tweakIds.ToList()
            };
        }

        private static void UpdateTweak(
            List<TweakDefinition> tweaks,
            string tweakId,
            Action<TweakDefinition> updateAction)
        {
            var tweak = tweaks.FirstOrDefault(item => item.Id == tweakId);
            if (tweak == null)
                return;

            updateAction(tweak);
        }

        private static List<string> MergeTags(IEnumerable<string> existingTags, params string[] tagsToAdd)
        {
            return existingTags
                .Concat(tagsToAdd)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
