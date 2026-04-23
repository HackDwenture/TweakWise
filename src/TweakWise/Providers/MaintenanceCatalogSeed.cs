using System;
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

        public static readonly IReadOnlyList<string> LocalTemplateIds = new[]
        {
            "care-baseline",
            "maintenance-deep-clean",
            "maintenance-after-update",
            "maintenance-free-space"
        };

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "Maintenance");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
            UpdateTweak(tweaks, "storage-sense-schedule", tweak =>
            {
                tweak.Subcategory = "Очистка файлов";
                tweak.Tags = MergeTags(tweak.Tags, "recommended", "safe", "cleanup");
                tweak.PreviewMeta = CreatePreview(
                    summary: "Будут удаляться временные файлы и другие безопасные остатки, которые Windows умеет чистить штатно.",
                    estimatedImpact: "Обычно освобождает от 300 МБ до 4 ГБ, в зависимости от того, как давно не запускалась очистка.",
                    sampleItems: new[]
                    {
                        "временные файлы приложений",
                        "корзина старше заданного срока",
                        "часть безопасных системных временных данных"
                    },
                    confirmationHint: "Перед первым запуском полезно посмотреть список категорий очистки и оставить только те, которые действительно нужны.");
            });

            UpdateTweak(tweaks, "startup-review", tweak =>
            {
                tweak.Subcategory = "Обслуживание по расписанию";
                tweak.Title = "Запланировать регулярный обзор автозагрузки и тяжёлого фона";
                tweak.ShortDescription = "Помогает не копить лишние автозапуски и фоновые процессы, которые появляются со временем.";
                tweak.LongDescription = "Вместо хаотичной ручной проверки пользователь получает спокойный сценарий обслуживания: раз в неделю или месяц посмотреть, что стало лишним в автозагрузке и фоновой активности.";
                tweak.Tags = MergeTags(tweak.Tags, "recommended", "action", "schedule");
                tweak.PreviewMeta = CreatePreview(
                    summary: "Будут показаны самые тяжёлые точки автозагрузки и фоновые элементы, которые стоит пересмотреть.",
                    estimatedImpact: "Может сократить время старта системы и уменьшить фоновые просадки без удаления программ.",
                    sampleItems: new[]
                    {
                        "приложения с тяжёлым стартом",
                        "планировщик задач для апдейтеров",
                        "дублирующиеся фоновые помощники"
                    },
                    confirmationHint: "Перед выключением каждого элемента нужен явный просмотр, чтобы не задеть действительно полезный фон.");
            });

            UpdateTweak(tweaks, "repair-health-check", tweak =>
            {
                tweak.Subcategory = "Быстрые исправления";
                tweak.Title = "Проверить и восстановить системные компоненты";
                tweak.ShortDescription = "Безопасный сценарий для ситуаций, когда Windows ведёт себя нестабильно после сбоев или обновлений.";
                tweak.LongDescription = "Собирает базовое восстановление компонентов в одно понятное действие: сначала проверка, затем аккуратная попытка восстановить повреждённые системные файлы и хранилище компонентов.";
                tweak.SourceType = TweakSourceType.Mixed;
                tweak.RequiresConfirmation = true;
                tweak.CurrentState = "Не запускалось";
                tweak.RecommendedState = "Проверка по необходимости";
                tweak.Tags = MergeTags(tweak.Tags, "advanced", "action", "repair", "confirmation");
                tweak.AdvancedDetails = new TweakAdvancedDetails
                {
                    TechnicalSummary = "Использует штатные средства проверки целостности и восстановления системных компонентов Windows.",
                    AffectedComponents = new List<string> { "System File Checker", "Component Store", "Windows servicing" },
                    Notes = new List<string>
                    {
                        "Raw command names и ключи остаются только внутри technical details.",
                        "Это безопаснее типичных «ускорителей ПК», потому что опирается на штатные механики Windows."
                    }
                };
                tweak.PreviewMeta = CreatePreview(
                    summary: "Сначала будет проверена целостность системных файлов и хранилища компонентов, затем система предложит безопасное восстановление, если найдёт повреждения.",
                    estimatedImpact: "Освобождение места не является основной целью; действие нацелено на стабильность и может занять от нескольких минут до часа.",
                    sampleItems: new[]
                    {
                        "системные файлы Windows",
                        "хранилище компонентов",
                        "связанные файлы обслуживания"
                    },
                    confirmationHint: "Перед запуском стоит закрыть тяжёлые приложения и понимать, что процесс может идти долго.");
                tweak.RollbackMeta = new TweakRollbackMeta
                {
                    RollbackType = "Откат ограничен",
                    RollbackSummary = "После восстановления компоненты не всегда можно вернуть в предыдущее повреждённое состояние, поэтому нужен просмотр и подтверждение перед запуском.",
                    ValidationHint = "После завершения проверьте стабильность обновлений, системных окон и отсутствие ошибок обслуживания."
                };
            });
        }

        public static void EnrichExistingTemplates(List<TweakTemplateDefinition> templates)
        {
            var template = templates.FirstOrDefault(item => item.Id == "care-baseline");
            if (template == null)
                return;

            template.Title = "Быстрая чистка";
            template.Description = "Спокойный базовый сценарий: безопасная очистка файлов, обзор автозагрузки и понятные шаги без агрессивного удаления.";
            template.ScopeLabel = "Обслуживание / Быстрая чистка";
            template.Audience = "Обычный пользователь";
            template.RiskLevel = TweakRiskLevel.Low;
            template.RequiresRestart = false;
            template.TweakIds = new List<string>
            {
                "storage-sense-schedule",
                "clean-downloads-review",
                "startup-review"
            };
        }

        public static List<TweakDefinition> BuildAdditionalTweaks()
        {
            return new List<TweakDefinition>
            {
                CreateTweak(
                    id: "clean-downloads-review",
                    subcategory: "Очистка файлов",
                    title: "Проверить папку Загрузки перед очисткой",
                    shortDescription: "Подходит, когда нужно освободить место, но не хочется случайно потерять важные установщики и документы.",
                    longDescription: "Это не агрессивная автоочистка: система сначала показывает, что именно давно лежит в Загрузках и сколько места это занимает, а уже потом предлагает удалить явно лишнее.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: false,
                    currentState: "Проверка не запускалась",
                    recommendedState: "Ручной просмотр перед удалением",
                    tags: new[] { "recommended", "safe", "cleanup", "confirmation" },
                    technicalSummary: "Использует штатный просмотр содержимого пользовательских папок и категории временного хранения.",
                    affectedComponents: new[] { "Downloads folder", "Temporary installers", "User files" },
                    previewSummary: "Перед применением будет показан список старых и крупных файлов, которые давно не открывались и занимают место.",
                    previewEstimatedImpact: "Обычно от 500 МБ до 20 ГБ, если папка Загрузки давно не разбиралась.",
                    previewItems: new[] { "старые установщики", "дубликаты архивов", "скачанные образы и видео" },
                    confirmationHint: "Подтверждение обязательно, потому что откат для удалённых пользовательских файлов ограничен.",
                    rollbackType: "Откат ограничен",
                    rollbackSummary: "После удаления из Загрузок полное восстановление возможно только при наличии Корзины или внешней копии.",
                    validationHint: "Проверьте свободное место на системном диске и убедитесь, что нужные файлы не были отмечены к удалению.",
                    notes: new[] { "Подходит как безопасная ручная чистка без «магических ускорителей».", "Raw команд и путей в основном UI нет." }),

                CreateTweak(
                    id: "clean-large-cache-folders",
                    subcategory: "Очистка файлов",
                    title: "Показать крупные кэши приложений перед очисткой",
                    shortDescription: "Помогает найти, какие приложения разрослись из-за временных кэшей и логов.",
                    longDescription: "Сценарий предназначен для понятной ручной чистки: пользователь видит крупные временные папки, примерный объём и решает, нужно ли удаление прямо сейчас.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: false,
                    currentState: "Не просматривалось",
                    recommendedState: "Чистить выборочно",
                    tags: new[] { "advanced", "cleanup", "confirmation", "space" },
                    technicalSummary: "Объединяет системные временные каталоги и крупные кэши приложений, которые часто не видны обычному пользователю.",
                    affectedComponents: new[] { "Temp folders", "App caches", "Installer remnants" },
                    previewSummary: "Будут показаны крупные временные каталоги и кэши приложений, которые безопасно просмотреть перед удалением.",
                    previewEstimatedImpact: "Часто освобождает от 1 до 8 ГБ, но итог зависит от браузеров, лаунчеров и обновляторов.",
                    previewItems: new[] { "кэши браузеров", "временные каталоги установщиков", "логи и остатки апдейтеров" },
                    confirmationHint: "Подтверждение обязательно, потому что часть приложений может заново строить кэш при следующем запуске.",
                    rollbackType: "Откат ограничен",
                    rollbackSummary: "Удалённые кэши обычно восстанавливаются только повторным запуском приложений, но не мгновенным откатом.",
                    validationHint: "После очистки запустите основные приложения и убедитесь, что они спокойно пересобрали кэш.",
                    notes: new[] { "Лучше не трогать активные каталоги приложений во время их работы." }),

                CreateTweak(
                    id: "cleanup-old-update-leftovers",
                    subcategory: "Системные остатки",
                    title: "Убрать остатки после крупных обновлений Windows",
                    shortDescription: "Полезно после больших апдейтов, когда система уже работает стабильно, а место на диске нужно вернуть.",
                    longDescription: "Сценарий аккуратно помогает освободить место после обновлений: сначала показывается, какие системные остатки занимают диск, а затем можно решить, готовы ли вы отказаться от короткого окна быстрого отката.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: false,
                    currentState: "Остатки могут храниться",
                    recommendedState: "Удалить после проверки стабильности",
                    tags: new[] { "recommended", "confirmation", "space", "cleanup" },
                    technicalSummary: "Использует штатные категории очистки Windows Update и хранилища предыдущих установок.",
                    affectedComponents: new[] { "Windows Update leftovers", "Previous installation files", "Cleanup manager" },
                    previewSummary: "Будет показан примерный объём системных остатков после обновления и то, что исчезнет возможность быстрого локального отката.",
                    previewEstimatedImpact: "Обычно освобождает от 3 до 25 ГБ после крупных версионных обновлений Windows.",
                    previewItems: new[] { "папка предыдущей установки Windows", "кэш обновлений", "остатки servicing files" },
                    confirmationHint: "Подтверждение обязательно: после удаления прошлой установки быстрый откат на предыдущую сборку будет ограничен.",
                    rollbackType: "Откат ограничен",
                    rollbackSummary: "Если остатки предыдущей установки удалены, быстрый возврат к прошлой версии системы уже не гарантируется.",
                    validationHint: "Применяйте только после того, как убедились, что новая версия Windows работает стабильно несколько дней.",
                    notes: new[] { "Это clean maintenance действие, а не агрессивная «оптимизация»." }),

                CreateTweak(
                    id: "cleanup-delivery-cache",
                    subcategory: "Системные остатки",
                    title: "Очистить кэш доставки обновлений",
                    shortDescription: "Возвращает место, если кэш обновлений разросся и больше не нужен системе.",
                    longDescription: "Подходит для случаев, когда Windows уже обновилась, а остатки кэша доставки только занимают место и не дают заметной пользы.",
                    sourceType: TweakSourceType.Service,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Кэш хранится",
                    recommendedState: "Очистить после завершения обновлений",
                    tags: new[] { "safe", "confirmation", "space", "cleanup" },
                    technicalSummary: "Затрагивает кэш Delivery Optimization и связанные сервисные остатки загрузки обновлений.",
                    affectedComponents: new[] { "Delivery Optimization cache", "Update download cache" },
                    previewSummary: "Будет очищен локальный кэш уже скачанных пакетов обновлений, если они больше не используются активными установками.",
                    previewEstimatedImpact: "Чаще всего от 500 МБ до 5 ГБ.",
                    previewItems: new[] { "кэш пакетов обновлений", "временные фрагменты доставки" },
                    confirmationHint: "Перед очисткой стоит убедиться, что в фоне не идёт загрузка или установка обновлений.",
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "При необходимости кэш снова сформируется автоматически при следующих обновлениях.",
                    validationHint: "Проверьте свободное место и убедитесь, что Windows Update не находится в активной установке.",
                    notes: new[] { "Это безопаснее, чем ручное удаление системных каталогов." }),

                CreateTweak(
                    id: "uninstall-large-programs-review",
                    subcategory: "Удаление программ",
                    title: "Показать крупные и давно неиспользуемые программы",
                    shortDescription: "Помогает освободить место без угадывания, что именно давно стало лишним.",
                    longDescription: "Сценарий показывает крупные программы и примерный объём, который можно вернуть, если вы ими давно не пользовались. Удаление происходит только после явного просмотра и подтверждения.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: false,
                    currentState: "Список не проверялся",
                    recommendedState: "Просмотр перед удалением",
                    tags: new[] { "recommended", "safe", "confirmation", "space" },
                    technicalSummary: "Работает со штатным списком установленных программ и их метаданными по размеру и дате использования.",
                    affectedComponents: new[] { "Installed apps list", "Uninstall entries", "Storage usage" },
                    previewSummary: "Перед применением будет показан список самых крупных и редко используемых программ вместе с примерным объёмом освобождения.",
                    previewEstimatedImpact: "От нескольких сотен мегабайт до десятков гигабайт, если на ПК много старых игр, IDE или медиа-наборов.",
                    previewItems: new[] { "старые игры", "неиспользуемые редакторы", "дублирующиеся утилиты производителей" },
                    confirmationHint: "Удаление каждой программы требует отдельного подтверждения, потому что откат зависит от её собственного установщика.",
                    rollbackType: "Откат ограничен",
                    rollbackSummary: "Большинство программ можно вернуть только повторной установкой из исходного дистрибутива.",
                    validationHint: "Проверьте, не зависят ли от удаляемой программы драйверы, плагины или рабочие файлы.",
                    notes: new[] { "Основной UI показывает только human-first смысл, а не сырые uninstall-команды." }),

                CreateTweak(
                    id: "uninstall-duplicate-launchers",
                    subcategory: "Удаление программ",
                    title: "Найти дублирующиеся лаунчеры и вспомогательные панели",
                    shortDescription: "Полезно, если на ПК накопились апдейтеры, лаунчеры и панели производителей, которые редко нужны ежедневно.",
                    longDescription: "Такие программы часто шумят в фоне, стартуют вместе с системой и дублируют друг друга. Сценарий позволяет просмотреть их как группу перед удалением.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: false,
                    currentState: "Не анализировалось",
                    recommendedState: "Выборочный просмотр",
                    tags: new[] { "advanced", "confirmation", "cleanup" },
                    technicalSummary: "Объединяет записи установленных программ, автозагрузки и фоновых помощников производителей.",
                    affectedComponents: new[] { "Launcher apps", "Updater helpers", "Vendor panels" },
                    previewSummary: "Будут собраны программы, которые дублируют друг друга по роли и чаще всего работают в фоне без ежедневной пользы.",
                    previewEstimatedImpact: "Обычно возвращает немного места, но сильнее влияет на чистоту интерфейса и фоновую активность.",
                    previewItems: new[] { "старые лаунчеры игр", "панели производителей устройств", "апдейтеры без явной пользы" },
                    confirmationHint: "Подтверждение обязательно: некоторые панели могут управлять драйверами, периферией или подсветкой.",
                    rollbackType: "Откат ограничен",
                    rollbackSummary: "Возврат зависит от возможности повторной установки нужного лаунчера или панели.",
                    validationHint: "Перед удалением проверьте, не используется ли приложение для обновления драйверов, мыши, клавиатуры или ноутбучных функций.",
                    notes: new[] { "Технические названия uninstall strings и scheduled tasks скрыты в деталях." }),

                CreateTweak(
                    id: "builtin-remove-consumer-apps",
                    subcategory: "Удаление встроенных приложений",
                    title: "Скрыть или удалить ненужные потребительские встроенные приложения",
                    shortDescription: "Подходит тем, кто хочет более рабочий и спокойный Windows без лишних развлекательных плиток и предустановок.",
                    longDescription: "Сценарий показывает список встроенных приложений, которые редко нужны на рабочем ПК. Пользователь сначала видит, что именно будет затронуто, а затем подтверждает удаление выборочно.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Стандартный набор",
                    recommendedState: "Только нужные приложения",
                    tags: new[] { "recommended", "advanced", "confirmation", "space" },
                    technicalSummary: "Работает с пакетом встроенных приложений и штатными механизмами удаления пользовательских Appx-пакетов.",
                    affectedComponents: new[] { "Built-in apps", "Provisioned packages", "Start suggestions" },
                    previewSummary: "Перед применением будет показан список встроенных приложений, которые можно убрать без ущерба для базовой работы Windows.",
                    previewEstimatedImpact: "Обычно освобождает от 300 МБ до 2 ГБ, но главный эффект — меньше визуального шума.",
                    previewItems: new[] { "развлекательные предустановки", "редко нужные клиентские приложения", "дублирующие потребительские утилиты" },
                    confirmationHint: "Подтверждение обязательно: даже безопасные встроенные приложения лучше убирать осознанно и выборочно.",
                    rollbackType: "Пакетный откат",
                    rollbackSummary: "Часть приложений можно вернуть через Microsoft Store или штатное восстановление пакетов.",
                    validationHint: "После удаления проверьте меню Пуск и убедитесь, что для вашей работы не исчезли нужные клиентские приложения.",
                    notes: new[] { "Raw package names показываются только в technical details." }),

                CreateTweak(
                    id: "builtin-keep-only-essential-tools",
                    subcategory: "Удаление встроенных приложений",
                    title: "Оставить только базовые встроенные инструменты",
                    shortDescription: "Более строгий сценарий для тех, кто хочет почти рабочую заготовку Windows без лишнего набора встроенных приложений.",
                    longDescription: "Это более продвинутый режим: система показывает, какие встроенные приложения считаются второстепенными, и предлагает оставить только действительно базовые инструменты.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.High,
                    requiresRestart: true,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Полный набор приложений",
                    recommendedState: "Только основа",
                    tags: new[] { "advanced", "confirmation", "pro" },
                    technicalSummary: "Затрагивает пользовательские и provisioned Appx-пакеты, которые могут быть восстановлены отдельным действием.",
                    affectedComponents: new[] { "Appx packages", "Provisioned apps", "Start menu inventory" },
                    previewSummary: "Перед применением будет показан список встроенных приложений, которые можно убрать ради более чистой рабочей системы.",
                    previewEstimatedImpact: "Обычно освобождает до нескольких гигабайт и уменьшает шум в меню Пуск.",
                    previewItems: new[] { "предустановленные UWP-приложения", "дополнительные медиа-клиенты", "часть необязательных утилит" },
                    confirmationHint: "Подтверждение строго обязательно: это продвинутый сценарий, который лучше применять только при ясном понимании набора приложений.",
                    rollbackType: "Пакетный откат",
                    rollbackSummary: "Вернуть набор можно, но для части приложений понадобится повторная регистрация или установка через Store.",
                    validationHint: "После применения проверьте поиск, меню Пуск и доступность нужных рабочих приложений.",
                    notes: new[] { "Подходит не всем; для большинства пользователей лучше ограничиться выборочным удалением." }),

                CreateTweak(
                    id: "repair-reset-network-stack",
                    subcategory: "Быстрые исправления",
                    title: "Сбросить сетевой стек безопасным сценарием",
                    shortDescription: "Полезно, когда сеть ведёт себя странно после обновления, VPN, драйверов или экспериментов с настройками.",
                    longDescription: "Сценарий объединяет базовое восстановление сети в одно понятное действие: сброс сокетов, сетевых параметров и обновление кэшей без хаотичных ручных команд.",
                    sourceType: TweakSourceType.Task,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Не запускалось",
                    recommendedState: "Только при проблемах с сетью",
                    tags: new[] { "action", "repair", "confirmation", "recommended" },
                    technicalSummary: "Использует штатные команды сетевого восстановления и очищает ключевые параметры пользовательского сетевого стека.",
                    affectedComponents: new[] { "TCP/IP stack", "Winsock", "DNS cache", "Network profiles" },
                    previewSummary: "Будут сброшены сетевые параметры и кэши, после чего системе может понадобиться повторный вход в сеть или перезагрузка.",
                    previewEstimatedImpact: "Место на диске почти не меняется; эффект направлен на восстановление стабильной сетевой работы.",
                    previewItems: new[] { "сетевые профили", "DNS-кэш", "часть временных параметров подключения" },
                    confirmationHint: "Подтверждение обязательно: после сброса может понадобиться заново подключить VPN, прокси или Wi‑Fi-профили.",
                    rollbackType: "Откат частичный",
                    rollbackSummary: "Базовые настройки сети восстанавливаются автоматически, но отдельные пользовательские сетевые профили иногда нужно настроить снова.",
                    validationHint: "После перезагрузки проверьте интернет, локальную сеть, VPN и DNS-разрешение сайтов.",
                    notes: new[] { "Raw netsh/PowerShell команды остаются только в technical details." }),

                CreateTweak(
                    id: "repair-reset-update-components",
                    subcategory: "Быстрые исправления",
                    title: "Сбросить компоненты обновления Windows",
                    shortDescription: "Подходит, когда обновления застряли, постоянно выдают ошибки или уходят в бесконечные ретраи.",
                    longDescription: "Собирает типичный сценарий восстановления Windows Update в чистую последовательность: остановка связанных компонентов, очистка кэшей и возврат служб в рабочее состояние.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Не запускалось",
                    recommendedState: "Только при явных проблемах с обновлениями",
                    tags: new[] { "action", "repair", "confirmation", "advanced" },
                    technicalSummary: "Работает с Windows Update services, servicing cache и связанными временными папками обновления.",
                    affectedComponents: new[] { "Windows Update", "Servicing cache", "Background transfer services" },
                    previewSummary: "Будут очищены временные компоненты обновления и сброшены зависшие служебные состояния, мешающие нормальной установке.",
                    previewEstimatedImpact: "Иногда возвращает от 500 МБ до 2 ГБ, но главная цель — вернуть обновления в рабочий режим.",
                    previewItems: new[] { "кэш Windows Update", "очереди фоновой загрузки", "временные servicing-файлы" },
                    confirmationHint: "Подтверждение обязательно: во время сценария не стоит параллельно запускать обычную проверку обновлений.",
                    rollbackType: "Откат частичный",
                    rollbackSummary: "Кэш обновлений восстановится автоматически, но история и промежуточные загруженные пакеты могут быть сброшены.",
                    validationHint: "После перезагрузки повторно проверьте Windows Update и убедитесь, что установка ошибок прекратилась.",
                    notes: new[] { "Сырые имена служб и папок не показываются в основном UI." }),

                CreateTweak(
                    id: "repair-package-manager",
                    subcategory: "Быстрые исправления",
                    title: "Починить менеджер пакетов и источники приложений",
                    shortDescription: "Полезно, если магазин приложений или пакетная установка начали работать нестабильно.",
                    longDescription: "Сценарий аккуратно проверяет и восстанавливает основные компоненты пакетного менеджера, чтобы установка и обновление приложений снова были предсказуемыми.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: true,
                    isReversible: true,
                    currentState: "Не запускалось",
                    recommendedState: "Только при проблемах с установкой пакетов",
                    tags: new[] { "action", "repair", "confirmation", "safe" },
                    technicalSummary: "Проверяет компоненты пакетного менеджера, кэш Store и базовые источники приложений.",
                    affectedComponents: new[] { "Package manager", "Store cache", "Application sources" },
                    previewSummary: "Будут обновлены служебные компоненты пакетного менеджера и очищены повреждённые кэши источников.",
                    previewEstimatedImpact: "На объём диска почти не влияет; действие направлено на восстановление установки и обновления приложений.",
                    previewItems: new[] { "кэш магазина", "список источников пакетов", "служебные записи установки" },
                    confirmationHint: "Перед запуском стоит закрыть Store и фоновые установщики, чтобы восстановление прошло чище.",
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Источники и кэши заново сформируются автоматически, если потребуется возврат к обычной работе.",
                    validationHint: "Проверьте установку или обновление любого тестового приложения после завершения сценария.",
                    notes: new[] { "Это safe repair action, а не сторонний оптимизатор." }),

                CreateTweak(
                    id: "schedule-monthly-deep-clean",
                    subcategory: "Обслуживание по расписанию",
                    title: "Запланировать ежемесячную глубокую чистку",
                    shortDescription: "Создаёт спокойный ритм обслуживания, чтобы не копить большие хвосты на системном диске.",
                    longDescription: "Подходит для домашних ПК и ноутбуков: не нужно вспоминать о чистке вручную, но при этом важные удаления всё равно проходят через понятный просмотр и подтверждение.",
                    sourceType: TweakSourceType.Task,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: false,
                    isReversible: true,
                    currentState: "Не запланировано",
                    recommendedState: "Раз в месяц",
                    tags: new[] { "recommended", "schedule", "action" },
                    technicalSummary: "Создаёт сценарий регулярной проверки безопасных категорий очистки и обзорных задач обслуживания.",
                    affectedComponents: new[] { "Scheduled maintenance", "Cleanup reminders", "Storage review" },
                    previewSummary: "По расписанию будут запускаться безопасные сценарии обзора очистки и освобождения места без автоматического удаления пользовательских данных.",
                    previewEstimatedImpact: "Главный эффект — меньше накопленного мусора и более предсказуемое свободное место на диске.",
                    previewItems: new[] { "напоминание о чистке", "обзор больших временных данных", "проверка системных остатков" },
                    confirmationHint: "Если сценарий включает ручные удаления, система всё равно должна спрашивать подтверждение перед применением.",
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Расписание можно отключить одним действием без побочных эффектов.",
                    validationHint: "Проверьте, что задача появилась в вашем сценарии обслуживания и не конфликтует с рабочими часами.",
                    notes: new[] { "Это центр обслуживания, а не навязчивый фоновый «ускоритель»." }),

                CreateTweak(
                    id: "schedule-post-update-check",
                    subcategory: "Обслуживание по расписанию",
                    title: "После крупных обновлений напоминать о проверке остатков и ошибок",
                    shortDescription: "Помогает не забывать про очистку хвостов и быстрые проверки после больших апдейтов Windows.",
                    longDescription: "Сценарий не делает ничего агрессивного сам по себе: он только предлагает в нужный момент посмотреть остатки обновлений, свободное место и быстрые исправления, если появились симптомы.",
                    sourceType: TweakSourceType.Task,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    requiresConfirmation: false,
                    isReversible: true,
                    currentState: "Не настроено",
                    recommendedState: "После крупных обновлений",
                    tags: new[] { "recommended", "schedule", "action" },
                    technicalSummary: "Использует событие завершения крупных обновлений как триггер для безопасного напоминания о review-сценарии.",
                    affectedComponents: new[] { "Post-update maintenance", "Scheduled reminders", "Cleanup follow-up" },
                    previewSummary: "После крупных обновлений система предложит просмотреть остатки прошлой установки, кэши обновлений и состояние обслуживания.",
                    previewEstimatedImpact: "Помогает раньше замечать лишние хвосты и ошибки обновления, но ничего не удаляет автоматически.",
                    previewItems: new[] { "остатки прошлой сборки", "кэш обновлений", "быстрые repair-действия при ошибках" },
                    confirmationHint: "Само напоминание не требует подтверждения, но каждое действие внутри него запускается осознанно.",
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Триггер можно отключить в любой момент без влияния на уже установленные обновления.",
                    validationHint: "Проверьте, что напоминание не мешает обычным рабочим сценариям после апдейтов.",
                    notes: new[] { "Подходит как clean maintenance привычка после Windows Update." })
            };
        }

        public static List<TweakTemplateDefinition> BuildAdditionalTemplates()
        {
            return new List<TweakTemplateDefinition>
            {
                CreateTemplate(
                    id: "maintenance-deep-clean",
                    title: "Глубокая чистка",
                    description: "Собирает более серьёзный, но всё ещё осознанный сценарий: кэши, остатки обновлений и выборочная ручная чистка крупных папок.",
                    scopeLabel: "Обслуживание / Глубокая чистка",
                    audience: "Обычный и pro-пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "clean-large-cache-folders",
                        "cleanup-old-update-leftovers",
                        "cleanup-delivery-cache"
                    }),

                CreateTemplate(
                    id: "maintenance-after-update",
                    title: "После обновления",
                    description: "Помогает привести систему в порядок после крупного апдейта: проверить хвосты, кэш и базовую целостность компонентов.",
                    scopeLabel: "Обслуживание / После обновления",
                    audience: "Обычный и pro-пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    tweakIds: new[]
                    {
                        "cleanup-old-update-leftovers",
                        "cleanup-delivery-cache",
                        "repair-health-check",
                        "schedule-post-update-check"
                    }),

                CreateTemplate(
                    id: "maintenance-free-space",
                    title: "Освободить место",
                    description: "Сценарий для системного диска, когда нужно вернуть пространство без дешёвой магии и без хаотичного удаления нужных вещей.",
                    scopeLabel: "Обслуживание / Освободить место",
                    audience: "Обычный пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "clean-downloads-review",
                        "clean-large-cache-folders",
                        "uninstall-large-programs-review",
                        "builtin-remove-consumer-apps"
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
            bool requiresConfirmation,
            bool isReversible,
            string currentState,
            string recommendedState,
            IEnumerable<string> tags,
            string technicalSummary,
            IEnumerable<string> affectedComponents,
            string previewSummary,
            string previewEstimatedImpact,
            IEnumerable<string> previewItems,
            string confirmationHint,
            string rollbackType,
            string rollbackSummary,
            string validationHint,
            IEnumerable<string> notes = null)
        {
            return new TweakDefinition
            {
                Id = id,
                Category = "Maintenance",
                Subcategory = subcategory,
                Title = title,
                ShortDescription = shortDescription,
                LongDescription = longDescription,
                SourceType = sourceType,
                RiskLevel = riskLevel,
                RequiresRestart = requiresRestart,
                RequiresConfirmation = requiresConfirmation,
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
                PreviewMeta = CreatePreview(previewSummary, previewEstimatedImpact, previewItems, confirmationHint),
                RollbackMeta = new TweakRollbackMeta
                {
                    RollbackType = rollbackType,
                    RollbackSummary = rollbackSummary,
                    ValidationHint = validationHint
                }
            };
        }

        private static TweakPreviewMeta CreatePreview(
            string summary,
            string estimatedImpact,
            IEnumerable<string> sampleItems,
            string confirmationHint)
        {
            return new TweakPreviewMeta
            {
                Summary = summary,
                EstimatedImpact = estimatedImpact,
                SampleItems = sampleItems?.ToList() ?? new List<string>(),
                ConfirmationHint = confirmationHint
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
