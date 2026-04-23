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

        public static readonly IReadOnlyList<string> LocalTemplateIds = new[]
        {
            "windows-interface-clean",
            "windows-interface-explorer",
            "windows-interface-start",
            "windows-interface-minimal"
        };

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "WindowsInterface");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
            AddTags(tweaks, "explorer-show-extensions", "recommended");
            AddTags(tweaks, "start-disable-recommendations", "recommended");
            AddTags(tweaks, "taskbar-hide-chat", "recommended");
            AddTags(tweaks, "search-disable-web-results", "hidden", "advanced", "recommended");
        }

        public static List<TweakDefinition> BuildAdditionalTweaks()
        {
            var tweaks = new List<TweakDefinition>();
            tweaks.AddRange(BuildExplorerTweaks());
            tweaks.AddRange(BuildStartMenuTweaks());
            tweaks.AddRange(BuildTaskbarTweaks());
            tweaks.AddRange(BuildContextMenuTweaks());
            tweaks.AddRange(BuildSearchTweaks());
            tweaks.AddRange(BuildDesktopTweaks());
            tweaks.AddRange(BuildNotificationTweaks());
            return tweaks;
        }

        public static List<TweakTemplateDefinition> BuildAdditionalTemplates()
        {
            return new List<TweakTemplateDefinition>
            {
                new()
                {
                    Id = "windows-interface-clean",
                    Title = "Чистый интерфейс",
                    Description = "Убирает лишние промо-блоки, виджеты и подсказки, чтобы Windows выглядел спокойнее и чище.",
                    ScopeLabel = "Интерфейс Windows",
                    Audience = "Обычный пользователь",
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    TweakIds = new List<string>
                    {
                        "start-disable-recommendations",
                        "taskbar-hide-chat",
                        "taskbar-hide-widgets",
                        "search-hide-highlights",
                        "notifications-hide-tips"
                    }
                },
                new()
                {
                    Id = "windows-interface-explorer",
                    Title = "Удобный Проводник",
                    Description = "Делает Проводник понятнее: полезные детали видны сразу, а лишние шаги исчезают.",
                    ScopeLabel = "Интерфейс Windows / Проводник",
                    Audience = "Обычный и pro-пользователь",
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    TweakIds = new List<string>
                    {
                        "explorer-show-extensions",
                        "explorer-show-hidden-items",
                        "explorer-open-this-pc",
                        "explorer-compact-view"
                    }
                },
                new()
                {
                    Id = "windows-interface-start",
                    Title = "Компактный Пуск",
                    Description = "Собирает настройки, которые делают меню Пуск короче, спокойнее и ближе к рабочему сценарию.",
                    ScopeLabel = "Интерфейс Windows / Меню Пуск",
                    Audience = "Обычный пользователь",
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    TweakIds = new List<string>
                    {
                        "start-disable-recommendations",
                        "start-compact-layout",
                        "start-hide-recent-apps"
                    }
                },
                new()
                {
                    Id = "windows-interface-minimal",
                    Title = "Минимум отвлекающих элементов",
                    Description = "Снижает шум от поиска, панели задач и уведомлений, чтобы внимание оставалось на работе.",
                    ScopeLabel = "Интерфейс Windows / Фокус",
                    Audience = "Обычный и pro-пользователь",
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    TweakIds = new List<string>
                    {
                        "search-disable-web-results",
                        "notifications-focus-hours",
                        "notifications-reduce-banners",
                        "taskbar-hide-chat",
                        "taskbar-hide-widgets"
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildExplorerTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "explorer-show-hidden-items",
                    Category = "WindowsInterface",
                    Subcategory = "Проводник",
                    Title = "Показывать скрытые папки и служебные файлы",
                    ShortDescription = "Полезно, когда нужно быстро найти системные папки, профиль пользователя или служебные данные приложений.",
                    LongDescription = "Открывает доступ к скрытым элементам прямо в Проводнике, чтобы не приходилось искать их обходными путями через командную строку или сторонние утилиты.",
                    SourceType = TweakSourceType.Registry,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Скрыты",
                    RecommendedState = "Показывать по необходимости",
                    Tags = new List<string> { "hidden", "recommended", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет стандартный режим отображения скрытых файлов и папок в оболочке Explorer.",
                        AffectedComponents = new List<string> { "Explorer", "Folder options" },
                        Notes = new List<string> { "Служебные файлы станут видимыми во всех папках.", "Лучше не удалять скрытые файлы без понимания их назначения." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Скрытые элементы можно снова убрать из вида одним переключением.",
                        ValidationHint = "Откройте системную папку и проверьте, видны ли скрытые элементы."
                    }
                },
                new()
                {
                    Id = "explorer-open-this-pc",
                    Category = "WindowsInterface",
                    Subcategory = "Проводник",
                    Title = "Открывать Проводник сразу на \"Этот компьютер\"",
                    ShortDescription = "Удобнее, если вы чаще работаете с дисками и папками, а не с лентой последних документов.",
                    LongDescription = "Меняет стартовую точку Проводника на более практичный экран с дисками, устройствами и основными папками, чтобы навигация начиналась с привычного места.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Главная страница",
                    RecommendedState = "\"Этот компьютер\"",
                    Tags = new List<string> { "recommended", "clarity", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет стартовое представление окна Explorer без изменения структуры библиотек.",
                        AffectedComponents = new List<string> { "Explorer start view" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Можно вернуть стартовую страницу Проводника обратно на главный экран.",
                        ValidationHint = "Откройте новый экземпляр Проводника и проверьте первый экран."
                    }
                },
                new()
                {
                    Id = "explorer-compact-view",
                    Category = "WindowsInterface",
                    Subcategory = "Проводник",
                    Title = "Сделать список файлов компактнее",
                    ShortDescription = "Помогает видеть больше файлов на экране без лишних отступов.",
                    LongDescription = "Переводит Проводник в более плотный режим отображения, чтобы длинные списки файлов и папок занимали меньше вертикального пространства.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Стандартные отступы",
                    RecommendedState = "Компактный вид",
                    Tags = new List<string> { "recommended", "clean-ui", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры внешнего вида списка в Explorer.",
                        AffectedComponents = new List<string> { "Explorer list view" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Плотность списка можно вернуть к стандартной без побочных эффектов.",
                        ValidationHint = "Откройте папку с длинным списком файлов и оцените плотность строк."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildStartMenuTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "start-compact-layout",
                    Category = "WindowsInterface",
                    Subcategory = "Меню Пуск",
                    Title = "Сделать меню Пуск компактнее",
                    ShortDescription = "Уменьшает визуальный шум и оставляет больше места под реальные приложения.",
                    LongDescription = "Переключает меню Пуск в более сжатый режим, чтобы важные элементы были ближе друг к другу и не терялись среди пустого пространства.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Обычная раскладка",
                    RecommendedState = "Компактнее",
                    Tags = new List<string> { "recommended", "clean-ui", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры представления Start menu.",
                        AffectedComponents = new List<string> { "Start menu layout" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Компактность можно снять тем же переключением без потери закреплённых приложений.",
                        ValidationHint = "Откройте меню Пуск и сравните плотность элементов после применения."
                    }
                },
                new()
                {
                    Id = "start-hide-recent-apps",
                    Category = "WindowsInterface",
                    Subcategory = "Меню Пуск",
                    Title = "Скрыть недавно добавленные приложения",
                    ShortDescription = "Убирает лишний служебный список, если вы не используете его как основной сценарий.",
                    LongDescription = "Помогает держать верхнюю часть меню Пуск более спокойной и освобождает место под закреплённые элементы и поиск.",
                    SourceType = TweakSourceType.Policy,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Показываются",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "hidden", "recommended" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет правила отображения блока недавно добавленных приложений в Start menu.",
                        AffectedComponents = new List<string> { "Start menu", "App discovery" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Список можно вернуть без потери истории установок.",
                        ValidationHint = "После установки нового приложения проверьте, появляется ли блок в меню Пуск."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildTaskbarTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "taskbar-hide-widgets",
                    Category = "WindowsInterface",
                    Subcategory = "Панель задач",
                    Title = "Убрать виджеты с панели задач",
                    ShortDescription = "Полезно, если нужен спокойный интерфейс без лишних точек входа в новости и внешние ленты.",
                    LongDescription = "Отключает виджеты на панели задач, чтобы в повседневной работе оставались только действительно нужные кнопки и индикаторы.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Включены",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "recommended", "clean-ui", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Работает через штатные параметры shell и taskbar experience.",
                        AffectedComponents = new List<string> { "Widgets", "Taskbar shell" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Виджеты можно вернуть без влияния на остальные элементы панели задач.",
                        ValidationHint = "Посмотрите, исчезла ли иконка виджетов после применения."
                    }
                },
                new()
                {
                    Id = "taskbar-left-alignment",
                    Category = "WindowsInterface",
                    Subcategory = "Панель задач",
                    Title = "Вернуть левое выравнивание кнопок",
                    ShortDescription = "Подходит тем, кто хочет более привычное положение кнопки Пуск и закреплённых приложений.",
                    LongDescription = "Перемещает основные элементы панели задач к левому краю, чтобы интерфейс напоминал более привычный сценарий работы и быстрее считывался периферийным зрением.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "По центру",
                    RecommendedState = "По левому краю",
                    Tags = new List<string> { "clarity", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры выравнивания taskbar icons.",
                        AffectedComponents = new List<string> { "Taskbar layout" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Выравнивание можно в любой момент вернуть к центральному режиму.",
                        ValidationHint = "Сверьте положение кнопки Пуск и закреплённых приложений после изменения."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildContextMenuTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "contextmenu-classic-menu",
                    Category = "WindowsInterface",
                    Subcategory = "Контекстное меню",
                    Title = "Сразу открывать полное контекстное меню",
                    ShortDescription = "Убирает дополнительный шаг \"Показать дополнительные параметры\" и ускоряет частые действия.",
                    LongDescription = "Возвращает более полный вид контекстного меню по умолчанию, чтобы команды для архивов, терминала, Git и других утилит были видны сразу.",
                    SourceType = TweakSourceType.Registry,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Сокращённое меню",
                    RecommendedState = "Полное меню сразу",
                    Tags = new List<string> { "hidden", "advanced", "recommended", "pro" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет способ вызова shell context menu и может потребовать перезапуск Explorer.",
                        AffectedComponents = new List<string> { "Shell context menu", "Explorer" },
                        Notes = new List<string> { "Особенно заметно на Windows 11.", "После изменения может потребоваться перезапуск оболочки." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Откат с перезапуском оболочки",
                        RollbackSummary = "Можно вернуть сокращённое меню стандартным способом и заново перезапустить Explorer.",
                        ValidationHint = "Щёлкните правой кнопкой по файлу и проверьте, нужно ли дополнительно раскрывать меню."
                    }
                },
                new()
                {
                    Id = "contextmenu-remove-share",
                    Category = "WindowsInterface",
                    Subcategory = "Контекстное меню",
                    Title = "Убрать лишний пункт \"Поделиться\"",
                    ShortDescription = "Полезно, если вы почти не используете встроенный сценарий отправки и хотите сократить меню.",
                    LongDescription = "Убирает редко используемый пункт из контекстного меню, чтобы важные действия были ближе и меню выглядело короче.",
                    SourceType = TweakSourceType.Registry,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Показывается",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "hidden", "clean-ui" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Затрагивает shell entry для команды обмена.",
                        AffectedComponents = new List<string> { "Shell context menu" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Пункт можно вернуть обратно без влияния на другие команды меню.",
                        ValidationHint = "Щёлкните правой кнопкой по файлу и проверьте состав пунктов меню."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildSearchTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "search-hide-highlights",
                    Category = "WindowsInterface",
                    Subcategory = "Поиск",
                    Title = "Скрыть яркие подборки и подсказки в поиске",
                    ShortDescription = "Делает поиск спокойнее и уменьшает количество визуального шума перед выдачей.",
                    LongDescription = "Убирает развлекательные подборки, карточки и дополнительные подсказки, если поиск нужен в первую очередь для приложений, файлов и параметров.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Показываются",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "hidden", "recommended", "clean-ui" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Влияет на поисковую оболочку и дополнительные визуальные элементы Search home.",
                        AffectedComponents = new List<string> { "Search home", "Taskbar search" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Карточки и подсказки можно вернуть без влияния на индексацию и локальный поиск.",
                        ValidationHint = "Откройте поиск Windows и проверьте, видны ли подборки на стартовом экране."
                    }
                },
                new()
                {
                    Id = "search-prioritize-local-results",
                    Category = "WindowsInterface",
                    Subcategory = "Поиск",
                    Title = "Поднимать локальные результаты выше всего остального",
                    ShortDescription = "Помогает быстрее находить приложения и параметры, не отвлекаясь на второстепенные блоки.",
                    LongDescription = "Настраивает поиск так, чтобы локальные приложения, документы и системные параметры были заметнее и удобнее для повседневного сценария.",
                    SourceType = TweakSourceType.Mixed,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Стандартная выдача",
                    RecommendedState = "Приоритет локальных результатов",
                    Tags = new List<string> { "recommended", "clarity" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Комбинирует поведенческие параметры поиска и настройки представления выдачи.",
                        AffectedComponents = new List<string> { "Windows Search", "Search ranking" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Порядок акцентов в выдаче можно вернуть к стандартному сценарию.",
                        ValidationHint = "Введите название приложения и сравните выдачу до и после изменения."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildDesktopTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "desktop-show-system-icons",
                    Category = "WindowsInterface",
                    Subcategory = "Рабочий стол",
                    Title = "Вернуть на рабочий стол важные системные значки",
                    ShortDescription = "Полезно, если нужен быстрый доступ к \"Этот компьютер\", \"Корзине\" и другим базовым точкам входа.",
                    LongDescription = "Собирает привычные системные значки на рабочем столе, чтобы основные действия были доступны без лишней навигации по параметрам Windows.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Часть значков скрыта",
                    RecommendedState = "Показать важные значки",
                    Tags = new List<string> { "recommended", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует штатные параметры значков рабочего стола.",
                        AffectedComponents = new List<string> { "Desktop icons", "Shell personalization" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Любой из системных значков можно снова скрыть без влияния на файлы пользователя.",
                        ValidationHint = "Посмотрите, появились ли нужные системные значки на рабочем столе."
                    }
                },
                new()
                {
                    Id = "desktop-hide-shortcut-arrows",
                    Category = "WindowsInterface",
                    Subcategory = "Рабочий стол",
                    Title = "Убрать стрелки у ярлыков",
                    ShortDescription = "Подходит тем, кто хочет более чистый вид рабочего стола и понимает, что это именно визуальное изменение.",
                    LongDescription = "Скрывает стандартные стрелки на ярлыках, чтобы значки выглядели аккуратнее, но при этом не меняет сами типы файлов и поведение ярлыков.",
                    SourceType = TweakSourceType.Registry,
                    RiskLevel = TweakRiskLevel.Medium,
                    RequiresRestart = true,
                    IsReversible = true,
                    CurrentState = "Показываются",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "hidden", "advanced", "clean-ui" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Меняет визуальное представление ярлыков в оболочке Explorer.",
                        AffectedComponents = new List<string> { "Desktop shell", "Explorer icons" },
                        Notes = new List<string> { "Это влияет только на внешний вид ярлыков.", "Для применения обычно нужен перезапуск оболочки." }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Откат с перезапуском оболочки",
                        RollbackSummary = "Стрелки можно вернуть стандартным способом после перезапуска Explorer.",
                        ValidationHint = "Посмотрите на ярлыки на рабочем столе после перезапуска оболочки."
                    }
                }
            };
        }

        private static IEnumerable<TweakDefinition> BuildNotificationTweaks()
        {
            return new List<TweakDefinition>
            {
                new()
                {
                    Id = "notifications-reduce-banners",
                    Category = "WindowsInterface",
                    Subcategory = "Уведомления",
                    Title = "Сделать баннеры уведомлений менее навязчивыми",
                    ShortDescription = "Снижает визуальное давление от всплывающих баннеров, не отключая уведомления полностью.",
                    LongDescription = "Помогает оставить центр уведомлений полезным, но уменьшает количество внезапных баннеров, которые отвлекают в рабочем сценарии.",
                    SourceType = TweakSourceType.Windows,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Стандартные баннеры",
                    RecommendedState = "Спокойный режим",
                    Tags = new List<string> { "recommended", "beginner" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Затрагивает параметры баннеров и поведения всплывающих уведомлений.",
                        AffectedComponents = new List<string> { "Notification banners", "Action Center" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Интенсивность баннеров можно вернуть к стандартной без потери истории уведомлений.",
                        ValidationHint = "Проверьте появление нового уведомления во время работы."
                    }
                },
                new()
                {
                    Id = "notifications-focus-hours",
                    Category = "WindowsInterface",
                    Subcategory = "Уведомления",
                    Title = "Автоматически включать тихие часы в важные моменты",
                    ShortDescription = "Снижает отвлекающие уведомления во время игр, презентаций и фокусной работы.",
                    LongDescription = "Переключает систему на более спокойный режим в сценариях, когда всплывающие баннеры чаще мешают, чем помогают.",
                    SourceType = TweakSourceType.Task,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Частично отключены",
                    RecommendedState = "Включать автоматически",
                    Tags = new List<string> { "recommended", "clarity", "advanced" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Использует системные сценарии Focus Assist и связанные поведенческие триггеры.",
                        AffectedComponents = new List<string> { "Focus Assist", "Notification rules" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Автоматические правила можно отключить, сохранив обычные уведомления.",
                        ValidationHint = "Проверьте режим фокуса в параметрах уведомлений и сценариях автоматического включения."
                    }
                },
                new()
                {
                    Id = "notifications-hide-tips",
                    Category = "WindowsInterface",
                    Subcategory = "Уведомления",
                    Title = "Убрать советы и рекламные подсказки Windows",
                    ShortDescription = "Полезно, если вы хотите видеть только реальные системные и приложенческие уведомления.",
                    LongDescription = "Отключает советы, приветственные сообщения и рекламные подсказки Windows, чтобы центр уведомлений оставался рабочим инструментом, а не витриной рекомендаций.",
                    SourceType = TweakSourceType.Policy,
                    RiskLevel = TweakRiskLevel.Low,
                    RequiresRestart = false,
                    IsReversible = true,
                    CurrentState = "Показываются",
                    RecommendedState = "Скрыть",
                    Tags = new List<string> { "hidden", "clean-ui", "recommended" },
                    AdvancedDetails = new TweakAdvancedDetails
                    {
                        TechnicalSummary = "Влияет на содержимое системных подсказок и промо-уведомлений оболочки.",
                        AffectedComponents = new List<string> { "Shell tips", "System notifications" }
                    },
                    RollbackMeta = new TweakRollbackMeta
                    {
                        RollbackType = "Мгновенный откат",
                        RollbackSummary = "Советы и подсказки можно вернуть в том же разделе без влияния на уведомления приложений.",
                        ValidationHint = "Понаблюдайте, исчезли ли советы Windows из центра уведомлений."
                    }
                }
            };
        }

        private static void AddTags(List<TweakDefinition> tweaks, string tweakId, params string[] tags)
        {
            var tweak = tweaks.FirstOrDefault(item => item.Id == tweakId);
            if (tweak == null)
                return;

            foreach (var tag in tags)
            {
                if (!tweak.Tags.Contains(tag))
                    tweak.Tags.Add(tag);
            }
        }
    }
}
