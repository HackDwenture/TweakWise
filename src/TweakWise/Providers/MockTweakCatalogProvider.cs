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
            _tweaks = BuildTweaks();
            _templates = BuildTemplates();

            WindowsInterfaceCatalogSeed.ApplyToCategories(_categories);
            WindowsInterfaceCatalogSeed.EnrichExistingTweaks(_tweaks);
            _tweaks.AddRange(WindowsInterfaceCatalogSeed.BuildAdditionalTweaks());
            _templates.AddRange(WindowsInterfaceCatalogSeed.BuildAdditionalTemplates());

            SystemCatalogSeed.ApplyToCategories(_categories);
            SystemCatalogSeed.EnrichExistingTweaks(_tweaks);
            _tweaks.AddRange(SystemCatalogSeed.BuildAdditionalTweaks());
            _templates.AddRange(SystemCatalogSeed.BuildAdditionalTemplates());

            MaintenanceCatalogSeed.ApplyToCategories(_categories);
            MaintenanceCatalogSeed.EnrichExistingTweaks(_tweaks);
            MaintenanceCatalogSeed.EnrichExistingTemplates(_templates);
            _tweaks.AddRange(MaintenanceCatalogSeed.BuildAdditionalTweaks());
            _templates.AddRange(MaintenanceCatalogSeed.BuildAdditionalTemplates());

            MonitoringPerformanceCatalogSeed.ApplyToCategories(_categories);
            MonitoringPerformanceCatalogSeed.EnrichExistingTweaks(_tweaks);
            _tweaks.AddRange(MonitoringPerformanceCatalogSeed.BuildAdditionalTweaks());
            _templates.AddRange(MonitoringPerformanceCatalogSeed.BuildAdditionalTemplates());
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
                    Title = "Интерфейс Windows",
                    Description = "Проводник, меню Пуск, панель задач и другие настройки, которые влияют на повседневный комфорт.",
                    Icon = "🪟",
                    Subcategories = new List<string> { "Проводник", "Меню Пуск", "Поиск", "Панель задач" }
                },
                new()
                {
                    Id = "System",
                    Title = "Система",
                    Description = "Конфиденциальность, обновления, питание и поведение Windows без перегруза техническими деталями.",
                    Icon = "⚙️",
                    Subcategories = new List<string> { "Конфиденциальность", "Обновления", "Питание", "Вход в систему" }
                },
                new()
                {
                    Id = "Maintenance",
                    Title = "Обслуживание",
                    Description = "Очистка, восстановление и поддержание стабильности без отдельной страницы для отката.",
                    Icon = "🧰",
                    Subcategories = new List<string> { "Очистка", "Автозагрузка", "Восстановление", "Контроль изменений" }
                },
                new()
                {
                    Id = "MonitoringPerformance",
                    Title = "Мониторинг и производительность",
                    Description = "Производительность, фоновые процессы и аппаратный мониторинг в одном смысловом разделе.",
                    Icon = "📈",
                    Subcategories = new List<string> { "Производительность", "Фоновые процессы", "Датчики", "Схемы питания" }
                }
            };
        }

        private static List<TweakDefinition> BuildTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "explorer-show-extensions",
                    Category = "WindowsInterface",
                    Subcategory = "Проводник",
                    Title = "Показывать расширения файлов",
                    ShortDescription = "Помогает быстрее отличать типы файлов и избегать случайных запусков.",
                    LongDescription = "Включает отображение расширений в Проводнике, чтобы пользователь видел полный тип файла и реже ошибался при работе с документами, архивами и исполняемыми файлами.",
                    SourceType = TweakSourceType.Registry,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Скрыты",
                    RecommendedState = "Показывать",
                    Tags = new List<string> { "featured", "beginner", "clarity" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет поведение проводника через системный параметр отображения файловых расширений.",
                        AffectedComponents = new List<string> { "Explorer", "Shell" },
                        Notes = new List<string> { "Не влияет на ассоциации файлов.", "Применяется сразу после обновления окна Проводника." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Можно вернуть скрытие расширений одним действием без перезагрузки.",
                        ValidationHint = "Откройте любую папку и проверьте, отображается ли суффикс файла."
                    }
                },
                new()
                {
                    Id = "start-disable-recommendations",
                    Category = "WindowsInterface",
                    Subcategory = "Меню Пуск",
                    Title = "Скрыть рекомендации и промо-блоки в меню Пуск",
                    ShortDescription = "Делает меню Пуск чище и понятнее для обычного пользователя.",
                    LongDescription = "Убирает рекомендательные элементы и рекламные подсказки, чтобы меню Пуск выглядело спокойнее и занималось реальными приложениями и документами.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Частично включены",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "featured", "beginner", "clean-ui" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Комбинирует настройки персонализации и поведения Start menu.",
                        AffectedComponents = new List<string> { "Start menu", "Content delivery" },
                        Notes = new List<string> { "На разных сборках Windows набор параметров может немного отличаться." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Рекомендации можно снова включить в той же карточке настройки.",
                        ValidationHint = "Откройте меню Пуск и проверьте, исчез ли блок рекомендаций."
                    }
                },
                new()
                {
                    Id = "taskbar-hide-chat",
                    Category = "WindowsInterface",
                    Subcategory = "Панель задач",
                    Title = "Убрать лишние системные кнопки с панели задач",
                    ShortDescription = "Скрывает элементы вроде Chat или Task View, если они не нужны.",
                    LongDescription = "Позволяет упростить панель задач и оставить только действительно используемые элементы, чтобы интерфейс был спокойнее и аккуратнее.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Показываются",
                    RecommendedState = "Скрыть неиспользуемые",
                    Tags = new List<string> { "clean-ui", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры панели задач и персонализации.",
                        AffectedComponents = new List<string> { "Taskbar", "Shell experience" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Каждый скрытый элемент можно вернуть отдельно.",
                        ValidationHint = "Проверьте правую и левую часть панели задач после применения."
                    }
                },
                new()
                {
                    Id = "search-disable-web-results",
                    Category = "WindowsInterface",
                    Subcategory = "Поиск",
                    Title = "Отключить веб-результаты в поиске Windows",
                    ShortDescription = "Оставляет поиск локальным и уменьшает шум в выдаче.",
                    LongDescription = "Помогает сфокусировать поиск на приложениях, файлах и настройках на этом ПК, а не на веб-результатах и внешних подсказках.",
                    SourceType = TweakSourceType.Policy,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Смешанный режим",
                    RecommendedState = "Только локальный поиск",
                    Tags = new List<string> { "privacy", "clean-ui" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Настройка влияет на Windows Search и shell search experience.",
                        AffectedComponents = new List<string> { "Windows Search", "Start search" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Откат с перезапуском оболочки",
                        RollbackSummary = "Веб-результаты возвращаются после восстановления политики и перезапуска поиска.",
                        ValidationHint = "Проверьте выдачу поиска после повторного входа или перезапуска Explorer."
                    }
                },
                new()
                {
                    Id = "privacy-reduce-telemetry",
                    Category = "System",
                    Subcategory = "Конфиденциальность",
                    Title = "Снизить фоновую телеметрию Windows",
                    ShortDescription = "Уменьшает фоновые диагностические отправки без радикального отключения системных функций.",
                    LongDescription = "Переводит сбор диагностических данных в более спокойный режим, чтобы снизить лишнюю сетевую активность и сделать настройки приватности понятнее.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Стандартный сбор",
                    RecommendedState = "Сниженный сбор",
                    Tags = new List<string> { "featured", "privacy", "pro" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Затрагивает политики, службы и параметры диагностики.",
                        AffectedComponents = new List<string> { "Connected User Experiences", "Diagnostics" },
                        Notes = new List<string> { "Некоторые корпоративные функции могут ожидать стандартные политики." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Откат профиля конфиденциальности",
                        RollbackSummary = "Все связанные параметры должны возвращаться пакетно, а не по одному.",
                        ValidationHint = "Проверьте экран диагностики и службы после перезагрузки."
                    }
                },
                new()
                {
                    Id = "updates-driver-control",
                    Category = "System",
                    Subcategory = "Обновления",
                    Title = "Сдерживать автоматическую замену драйверов через Windows Update",
                    ShortDescription = "Полезно, если на ПК уже стоят стабильные драйверы от производителя.",
                    LongDescription = "Позволяет избежать неожиданных замен драйверов при обычных обновлениях Windows, сохраняя больше контроля над рабочей конфигурацией.",
                    SourceType = TweakSourceType.Policy,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Автоматическая замена разрешена",
                    RecommendedState = "Требуется ручной контроль",
                    Tags = new List<string> { "pro", "stability" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Работает через параметры политики обновлений устройств.",
                        AffectedComponents = new List<string> { "Windows Update", "Device installation" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Откат политики",
                        RollbackSummary = "Можно вернуть стандартное поведение обновления драйверов.",
                        ValidationHint = "После отката проверьте параметры обновлений и обнаружение драйверов."
                    }
                },
                new()
                {
                    Id = "power-fast-startup",
                    Category = "System",
                    Subcategory = "Питание",
                    Title = "Настроить быстрый запуск осознанно",
                    ShortDescription = "Даёт понятный выбор между быстрым стартом и предсказуемым выключением.",
                    LongDescription = "Собирает связанную настройку питания в human-first виде: пользователь понимает компромисс между скоростью запуска и чистым завершением работы.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Включён",
                    RecommendedState = "Оставить по сценарию устройства",
                    Tags = new List<string> { "featured", "beginner", "power" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует системные параметры питания и гибридного завершения работы.",
                        AffectedComponents = new List<string> { "Power options", "Shutdown behavior" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Параметр можно переключить обратно без сложной процедуры.",
                        ValidationHint = "Проверьте состояние параметра в дополнительных настройках питания."
                    }
                },
                new()
                {
                    Id = "storage-sense-schedule",
                    Category = "Maintenance",
                    Subcategory = "Очистка",
                    Title = "Включить аккуратную автоматическую очистку",
                    ShortDescription = "Освобождает место без агрессивного удаления полезных файлов.",
                    LongDescription = "Настраивает Storage Sense в безопасном режиме: временные файлы и системный мусор очищаются автоматически, но пользователь понимает, что именно будет затронуто.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Выключено",
                    RecommendedState = "Еженедельно",
                    Tags = new List<string> { "featured", "maintenance", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры Storage Sense.",
                        AffectedComponents = new List<string> { "Storage Sense", "Temporary files" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Автоматическую очистку можно остановить или смягчить расписание.",
                        ValidationHint = "Проверьте параметры памяти устройства и расписание очистки."
                    }
                },
                new()
                {
                    Id = "startup-review",
                    Category = "Maintenance",
                    Subcategory = "Автозагрузка",
                    Title = "Отслеживать тяжёлые приложения в автозагрузке",
                    ShortDescription = "Помогает держать запуск системы под контролем без ручного поиска по нескольким окнам.",
                    LongDescription = "Собирает смысловой сценарий обслуживания: пользователь видит, какие элементы автозагрузки сильнее всего влияют на время старта и что можно выключить без риска.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Не проверено",
                    RecommendedState = "Регулярный обзор",
                    Tags = new List<string> { "maintenance", "featured", "clarity" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Объединяет startup tasks, реестр и штатные точки входа приложений.",
                        AffectedComponents = new List<string> { "Startup apps", "Task Scheduler", "Run keys" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Точечный откат",
                        RollbackSummary = "Каждое приложение в автозагрузке включается обратно отдельно.",
                        ValidationHint = "Проверьте состояние в системном списке автозагрузки."
                    }
                },
                new()
                {
                    Id = "repair-health-check",
                    Category = "Maintenance",
                    Subcategory = "Восстановление",
                    Title = "Подготовить безопасный сценарий восстановления системы",
                    ShortDescription = "Откат остаётся встроенной функцией, а не отдельной пугающей страницей.",
                    LongDescription = "Формирует основу для восстановительных действий прямо внутри разделов: перед изменением настройки можно понять, как вернуть исходное состояние и что проверить после отката.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Базовая защита",
                    RecommendedState = "Локальные rollback-метки",
                    Tags = new List<string> { "maintenance", "rollback", "pro" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Точка расширения под системные restore points и локальный журнал применённых изменений.",
                        AffectedComponents = new List<string> { "System Restore", "Local history", "Operation journal" },
                        Notes = new List<string> { "В этой сборке реализована модель и UI-контракт, без финального backend-применения." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Встроенный сценарий отката",
                        RollbackSummary = "Откат должен запускаться из карточки настройки или шаблона, а не из отдельного top-level раздела.",
                        ValidationHint = "Проверяйте наличие локальной rollback-метки рядом с применённым изменением."
                    }
                },
                new()
                {
                    Id = "visual-effects-balanced",
                    Category = "MonitoringPerformance",
                    Subcategory = "Производительность",
                    Title = "Снизить тяжёлые визуальные эффекты без ощущения сломанных окон",
                    ShortDescription = "Баланс между отзывчивостью системы и привычным внешним видом Windows.",
                    LongDescription = "Оставляет интерфейс современным, но убирает эффекты, которые чаще всего мешают на средних или слабых системах.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Стандартные эффекты",
                    RecommendedState = "Сбалансированный режим",
                    Tags = new List<string> { "featured", "performance", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Комбинация параметров анимации, прозрачности и visual effects.",
                        AffectedComponents = new List<string> { "DWM", "Explorer", "Accessibility visual effects" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Эффекты можно вернуть одним переключением профиля.",
                        ValidationHint = "Оцените отклик окон и меню сразу после применения."
                    }
                },
                new()
                {
                    Id = "background-apps-limit",
                    Category = "MonitoringPerformance",
                    Subcategory = "Фоновые процессы",
                    Title = "Ограничить лишнюю фоновую активность",
                    ShortDescription = "Уменьшает шум от фоновых приложений и даёт более стабильную производительность.",
                    LongDescription = "Собирает настройки фоновой активности, чтобы обычный пользователь видел понятный результат, а pro-пользователь мог открыть технические детали по службам и задачам.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Стандартная активность",
                    RecommendedState = "Сдержанный фон",
                    Tags = new List<string> { "performance", "privacy", "pro" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Может затрагивать фоновые задачи, scheduled tasks и часть app permissions.",
                        AffectedComponents = new List<string> { "Background apps", "Scheduled tasks", "App permissions" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Пакетный откат",
                        RollbackSummary = "Возвращает стандартное фоновое поведение приложений.",
                        ValidationHint = "После перезапуска проверьте уведомления и поведение фоновых приложений."
                    }
                },
                new()
                {
                    Id = "power-plan-performance",
                    Category = "MonitoringPerformance",
                    Subcategory = "Схемы питания",
                    Title = "Выбрать профиль питания под сценарий использования",
                    ShortDescription = "Единая точка для выбора между тишиной, балансом и максимальной отзывчивостью.",
                    LongDescription = "Вместо показа технических GUID-схем пользователь видит понятный выбор профиля питания, а детали остаются в раскрываемом состоянии.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Сбалансированный",
                    RecommendedState = "По сценарию устройства",
                    Tags = new List<string> { "performance", "beginner", "power" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Работает с power schemes и связанными параметрами CPU/GPU поведения.",
                        AffectedComponents = new List<string> { "Power plan", "Processor policy", "Battery strategy" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Можно быстро вернуть предыдущую схему питания.",
                        ValidationHint = "Проверьте текущую активную схему питания после переключения."
                    }
                }
            };
        }

        private static List<TweakTemplateDefinition> BuildTemplates()
        {
            return new List<TweakTemplateDefinition>
            {
                new()
                {
                    Id = "clean-start",
                    Title = "Чистый и спокойный Windows",
                    Description = "Минимум визуального шума и рекомендаций, больше понятных элементов интерфейса.",
                    ScopeLabel = "Главная / Интерфейс Windows",
                    Audience = "Обычный пользователь",
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    TweakIds = new List<string>
                    {
                        "explorer-show-extensions",
                        "start-disable-recommendations",
                        "taskbar-hide-chat"
                    }
                },
                new()
                {
                    Id = "quiet-background",
                    Title = "Тише в фоне",
                    Description = "Снижает лишнюю фоновую активность и делает систему предсказуемее.",
                    ScopeLabel = "Главная / Система",
                    Audience = "Обычный и pro-пользователь",
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    TweakIds = new List<string>
                    {
                        "privacy-reduce-telemetry",
                        "background-apps-limit",
                        "search-disable-web-results"
                    }
                },
                new()
                {
                    Id = "care-baseline",
                    Title = "Базовое обслуживание ПК",
                    Description = "Регулярная очистка, контроль автозагрузки и подготовка встроенного rollback-сценария.",
                    ScopeLabel = "Главная / Обслуживание",
                    Audience = "Обычный пользователь",
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    TweakIds = new List<string>
                    {
                        "storage-sense-schedule",
                        "startup-review",
                        "repair-health-check"
                    }
                }
            };
        }
    }
}
