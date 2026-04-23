using System.Collections.Generic;
using System.Linq;
using TweakWise.Models;

namespace TweakWise.Providers
{
    internal static class SystemCatalogSeed
    {
        public static readonly IReadOnlyList<string> SectionOrder = new[]
        {
            "Приватность и телеметрия",
            "Обновления",
            "Автозагрузка и фоновые процессы",
            "Службы",
            "Питание",
            "Встроенные приложения",
            "Драйверы и устройства",
            "Сеть",
            "Поведение системы"
        };

        public static readonly IReadOnlyList<string> LocalTemplateIds = new[]
        {
            "system-basic-privacy",
            "system-less-background",
            "system-balance",
            "system-performance",
            "system-laptop"
        };

        public static void ApplyToCategories(List<TweakCategoryDefinition> categories)
        {
            var category = categories.FirstOrDefault(item => item.Id == "System");
            if (category == null)
                return;

            category.Subcategories = SectionOrder.ToList();
        }

        public static void EnrichExistingTweaks(List<TweakDefinition> tweaks)
        {
            MoveAndTag(tweaks, "privacy-reduce-telemetry", "Приватность и телеметрия", "recommended", "advanced");
            MoveAndTag(tweaks, "updates-driver-control", "Обновления", "recommended", "advanced");
            MoveAndTag(tweaks, "power-fast-startup", "Питание", "recommended");
        }

        public static List<TweakDefinition> BuildAdditionalTweaks()
        {
            return new List<TweakDefinition>
            {
                CreateTweak(
                    id: "privacy-disable-ad-id",
                    subcategory: "Приватность и телеметрия",
                    title: "Отключить рекламный идентификатор приложений",
                    shortDescription: "Убирает лишнюю персонализацию рекламы и подсказок внутри встроенных приложений.",
                    longDescription: "Делает поведение системы спокойнее для обычного пользователя: приложения меньше опираются на рекламный профиль, а интерфейс реже подстраивается под маркетинговые сценарии.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Персонализация включена",
                    recommendedState: "Персонализация отключена",
                    tags: new[] { "recommended", "privacy", "beginner" },
                    technicalSummary: "Меняет системный параметр рекламного идентификатора для приложений Windows.",
                    affectedComponents: new[] { "Advertising ID", "Microsoft Store apps", "Personalized tips" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Персонализацию можно вернуть тем же переключателем без потери данных приложений.",
                    validationHint: "Проверьте раздел приватности Windows и убедитесь, что персонализированные подсказки стали реже.",
                    notes: new[] { "Не влияет на обычную локальную рекламу внутри браузера.", "Подходит как безопасная базовая настройка приватности." }),

                CreateTweak(
                    id: "privacy-limit-activity-history",
                    subcategory: "Приватность и телеметрия",
                    title: "Не сохранять лишнюю историю активности между устройствами",
                    shortDescription: "Оставляет локальную работу предсказуемой и не отправляет историю сценариев в облако без необходимости.",
                    longDescription: "Если вы не используете синхронизацию рабочего сценария между несколькими устройствами, эта настройка помогает сократить лишнюю историю активности и сделать поведение аккаунта более приватным.",
                    sourceType: TweakSourceType.Policy,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "История может синхронизироваться",
                    recommendedState: "Только локальная активность",
                    tags: new[] { "privacy", "recommended", "hidden" },
                    technicalSummary: "Ограничивает синхронизацию Timeline и связанных облачных сценариев активности.",
                    affectedComponents: new[] { "Activity History", "Timeline", "Cloud sync" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Синхронизацию можно вернуть без сброса локальной истории устройства.",
                    validationHint: "Проверьте параметры приватности и убедитесь, что история активности не отправляется между устройствами.",
                    notes: new[] { "Полезно для одиночного ПК или ноутбука.", "Технические названия политик остаются только внутри детали." }),

                CreateTweak(
                    id: "updates-predictable-restarts",
                    subcategory: "Обновления",
                    title: "Сделать перезапуски после обновлений предсказуемее",
                    shortDescription: "Снижает шанс внезапного перезапуска во время работы или вечером после короткого простоя.",
                    longDescription: "Собирает параметры активных часов и поведения авто-перезапуска в понятный сценарий: обновления продолжают приходить, но система реже вмешивается в неподходящий момент.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартное расписание",
                    recommendedState: "Предсказуемые перезапуски",
                    tags: new[] { "recommended", "beginner", "stability" },
                    technicalSummary: "Использует штатные параметры Windows Update для активных часов и уведомлений о перезапуске.",
                    affectedComponents: new[] { "Windows Update", "Restart scheduling", "Active hours" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Поведение перезапусков можно вернуть к стандартному, если нужен полностью автоматический режим.",
                    validationHint: "Проверьте раздел обновлений и расписание активных часов после применения.",
                    notes: new[] { "Подходит большинству обычных пользователей.", "Не блокирует сами обновления безопасности." }),

                CreateTweak(
                    id: "startup-essential-only",
                    subcategory: "Автозагрузка и фоновые процессы",
                    title: "Оставить в автозагрузке только действительно нужное",
                    shortDescription: "Помогает запускать систему быстрее и не теряться в списке приложений, стартующих вместе с Windows.",
                    longDescription: "Вместо работы с несколькими техническими списками пользователь видит один понятный сценарий: в автозагрузке остаются только те приложения, которые нужны сразу после входа в систему.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Есть лишние элементы",
                    recommendedState: "Только важные приложения",
                    tags: new[] { "recommended", "beginner", "clarity" },
                    technicalSummary: "Объединяет штатный список Startup Apps, ярлыки автозагрузки и стандартные точки входа приложений.",
                    affectedComponents: new[] { "Startup Apps", "Startup folder", "Run entries" },
                    rollbackType: "Точечный откат",
                    rollbackSummary: "Каждое приложение можно вернуть в автозагрузку отдельно, без отмены остальных изменений.",
                    validationHint: "Сравните список автозагрузки до и после применения и проверьте время старта системы.",
                    notes: new[] { "Не трогает драйверы и критически важные службы.", "Хорошая точка входа для обычного пользователя." }),

                CreateTweak(
                    id: "background-maintenance-window",
                    subcategory: "Автозагрузка и фоновые процессы",
                    title: "Перенести тяжёлое фоновое обслуживание на удобное время",
                    shortDescription: "Снижает шанс, что индексатор, диагностика или обслуживание начнут шуметь в разгар работы.",
                    longDescription: "Настройка помогает сделать фоновые задачи предсказуемее: они остаются доступными системе, но чаще запускаются в более спокойные окна, когда нагрузка не мешает обычной работе.",
                    sourceType: TweakSourceType.Task,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартное расписание",
                    recommendedState: "Более спокойное окно обслуживания",
                    tags: new[] { "advanced", "pro", "background" },
                    technicalSummary: "Влияет на системные задачи обслуживания и связанные триггеры фоновой активности.",
                    affectedComponents: new[] { "Scheduled maintenance", "Task Scheduler", "Background diagnostics" },
                    rollbackType: "Пакетный откат",
                    rollbackSummary: "Расписание фонового обслуживания можно вернуть к штатному поведению одним действием.",
                    validationHint: "Проверьте окно автоматического обслуживания и нагрузку системы в обычные рабочие часы.",
                    notes: new[] { "Подходит для ПК, который активно используется днём.", "Технические имена задач остаются скрыты в карточке." }),

                CreateTweak(
                    id: "services-diagnostics-calm",
                    subcategory: "Службы",
                    title: "Перевести диагностические службы в спокойный режим",
                    shortDescription: "Уменьшает лишнюю сервисную активность, не отключая полностью базовую диагностику Windows.",
                    longDescription: "Подходит тем, кто хочет меньше фонового шума и телеметрии, но не готов полностью ломать системные сценарии поддержки и обслуживания.",
                    sourceType: TweakSourceType.Service,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Стандартная активность",
                    recommendedState: "Спокойный режим служб",
                    tags: new[] { "advanced", "privacy", "recommended" },
                    technicalSummary: "Затрагивает службы диагностики и связанные сервисные зависимости Windows.",
                    affectedComponents: new[] { "Connected User Experiences", "Diagnostic services", "Telemetry pipeline" },
                    rollbackType: "Откат с перезапуском служб",
                    rollbackSummary: "Исходный режим служб можно вернуть без ручного восстановления каждой зависимости.",
                    validationHint: "После применения проверьте список служб и общую фоновую активность системы.",
                    notes: new[] { "Не предназначено для корпоративной диагностики с жёсткими требованиями.", "Служебные имена и startup type скрыты в technical details." }),

                CreateTweak(
                    id: "services-on-demand-gaming",
                    subcategory: "Службы",
                    title: "Держать игровые сервисы по требованию, если вы ими не пользуетесь",
                    shortDescription: "Убирает лишний фон на рабочих ПК, где не нужен игровой стек Microsoft.",
                    longDescription: "Сценарий полезен для рабочих станций и ноутбуков, на которых Xbox-сервисы и сопутствующие фоновые процессы не используются ежедневно.",
                    sourceType: TweakSourceType.Service,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Запускаются автоматически",
                    recommendedState: "По требованию",
                    tags: new[] { "hidden", "advanced", "pro" },
                    technicalSummary: "Меняет поведение служб игрового стека и связанных фоновых компонентов Microsoft.",
                    affectedComponents: new[] { "Gaming services", "Xbox integration", "Background sign-in helpers" },
                    rollbackType: "Откат с перезапуском служб",
                    rollbackSummary: "Автоматический старт игровых сервисов можно вернуть, если снова нужны облачные функции и подписки.",
                    validationHint: "Проверьте запуск игровых приложений после возврата или изменения режима.",
                    notes: new[] { "Подходит не всем: если используются Game Pass или Xbox-app, лучше оставить стандартный режим." }),

                CreateTweak(
                    id: "power-lid-action-smart",
                    subcategory: "Питание",
                    title: "Сделать закрытие крышки ноутбука предсказуемым",
                    shortDescription: "Позволяет выбрать понятный сценарий для сна, гибернации или продолжения работы с внешним монитором.",
                    longDescription: "Особенно полезно на ноутбуках: вместо технических профилей питания пользователь получает одно понятное решение, как должно вести себя устройство при закрытии крышки.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Стандартное поведение",
                    recommendedState: "По сценарию устройства",
                    tags: new[] { "recommended", "beginner", "laptop" },
                    technicalSummary: "Использует системные параметры действия при закрытии крышки для батареи и питания от сети.",
                    affectedComponents: new[] { "Power options", "Lid close action", "Battery behavior" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Поведение крышки можно быстро вернуть к стандартному профилю питания.",
                    validationHint: "Проверьте реакцию ноутбука при закрытии крышки от батареи и от сети.",
                    notes: new[] { "Особенно удобно для ноутбуков с внешним монитором.", "Не влияет на саму схему питания целиком." }),

                CreateTweak(
                    id: "apps-hide-consumer-suggestions",
                    subcategory: "Встроенные приложения",
                    title: "Скрыть рекомендации предустановленных приложений",
                    shortDescription: "Убирает лишние промо-предложения и делает систему спокойнее сразу после установки.",
                    longDescription: "Полезно, если вы хотите видеть Windows как рабочую систему, а не как витрину приложений и предложений, которые появляются в фоне или в меню.",
                    sourceType: TweakSourceType.Policy,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Рекомендации возможны",
                    recommendedState: "Не показывать",
                    tags: new[] { "recommended", "clean-ui", "privacy" },
                    technicalSummary: "Ограничивает consumer experience и промо-рекомендации встроенных приложений и системных сценариев.",
                    affectedComponents: new[] { "Consumer experience", "Suggested apps", "Microsoft Store promotions" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Рекомендации можно вернуть без переустановки приложений и без сброса профиля.",
                    validationHint: "Проверьте меню Пуск и системные подсказки после повторного входа в систему.",
                    notes: new[] { "Хорошо сочетается с более чистым интерфейсом Windows.", "Не удаляет сами приложения." }),

                CreateTweak(
                    id: "apps-limit-background-access",
                    subcategory: "Встроенные приложения",
                    title: "Ограничить фоновую активность встроенных приложений",
                    shortDescription: "Сдерживает приложения, которые любят обновляться и просыпаться без явной пользы для пользователя.",
                    longDescription: "Сценарий особенно полезен на ноутбуках и менее мощных ПК: система остаётся функциональной, но встроенные приложения реже тратят ресурсы в фоне без явного запроса.",
                    sourceType: TweakSourceType.Mixed,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Фоновая активность разрешена",
                    recommendedState: "Только нужные приложения",
                    tags: new[] { "advanced", "pro", "background" },
                    technicalSummary: "Объединяет permissions background apps и часть системных поведенческих флагов для встроенных приложений.",
                    affectedComponents: new[] { "Background apps", "App permissions", "Store apps" },
                    rollbackType: "Пакетный откат",
                    rollbackSummary: "Ограничения можно снять и вернуть стандартное поведение встроенных приложений.",
                    validationHint: "После перезапуска проверьте уведомления и синхронизацию тех приложений, которые должны работать в фоне.",
                    notes: new[] { "Лучше применять осознанно, если вы знаете, каким приложениям нужен фон.", "Технические имена пакетов остаются скрытыми." }),

                CreateTweak(
                    id: "devices-stop-online-metadata",
                    subcategory: "Драйверы и устройства",
                    title: "Не подтягивать лишние карточки и значки устройств из интернета",
                    shortDescription: "Снижает лишний сетевой шум и делает поведение новых устройств более предсказуемым.",
                    longDescription: "Если вы предпочитаете более спокойную и приватную настройку системы, можно уменьшить автоматическую загрузку онлайн-метаданных для устройств без потери базовой работы драйверов.",
                    sourceType: TweakSourceType.Policy,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Онлайн-метаданные разрешены",
                    recommendedState: "Локальный режим",
                    tags: new[] { "recommended", "privacy", "devices" },
                    technicalSummary: "Влияет на загрузку online device metadata и оформление некоторых карточек устройств.",
                    affectedComponents: new[] { "Device metadata", "Device setup", "Shell device cards" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Онлайн-метаданные можно вернуть без переустановки уже работающих драйверов.",
                    validationHint: "Проверьте поведение нового устройства после его повторного подключения.",
                    notes: new[] { "Не заменяет политику установки драйверов.", "Полезно для более приватного сценария." }),

                CreateTweak(
                    id: "devices-manual-helper-apps",
                    subcategory: "Драйверы и устройства",
                    title: "Не ставить вспомогательные приложения для устройств без запроса",
                    shortDescription: "Сохраняет ручной контроль над дополнительными утилитами для принтеров, гарнитур и другой периферии.",
                    longDescription: "Базовые драйверы продолжают работать, но система реже тянет лишние надстройки и магазины устройств, если вы этого не хотите.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Помощники могут устанавливаться",
                    recommendedState: "Только по запросу",
                    tags: new[] { "hidden", "advanced", "devices" },
                    technicalSummary: "Влияет на поведение установки сопутствующих приложений и дополнительных компонентов для новых устройств.",
                    affectedComponents: new[] { "Device setup", "Companion apps", "Peripheral onboarding" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Автоматическую установку можно вернуть, если она нужна для конкретного оборудования.",
                    validationHint: "Подключите новое устройство и проверьте, предлагает ли система дополнительные приложения.",
                    notes: new[] { "Подходит тем, кто предпочитает ставить утилиты производителя вручную." }),

                CreateTweak(
                    id: "network-limit-delivery-sharing",
                    subcategory: "Сеть",
                    title: "Не раздавать обновления другим ПК без необходимости",
                    shortDescription: "Убирает лишнюю отдачу трафика и делает обновления спокойнее на домашней сети.",
                    longDescription: "Подходит для ноутбуков, ограниченных тарифов и сетей, где не хочется тратить трафик на раздачу обновлений соседним устройствам без явного согласия.",
                    sourceType: TweakSourceType.Policy,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Раздача может использоваться",
                    recommendedState: "Только этот ПК",
                    tags: new[] { "recommended", "privacy", "network" },
                    technicalSummary: "Настраивает Delivery Optimization и режимы обмена пакетами обновлений.",
                    affectedComponents: new[] { "Delivery Optimization", "Windows Update network traffic" },
                    rollbackType: "Откат с перезапуском сети",
                    rollbackSummary: "Сетевой обмен пакетами обновлений можно вернуть к стандартному режиму после перезапуска служб обновления.",
                    validationHint: "Проверьте параметры оптимизации доставки и активность сети во время загрузки обновлений.",
                    notes: new[] { "Особенно полезно на ограниченном или мобильном интернете." }),

                CreateTweak(
                    id: "network-keep-connection-stable",
                    subcategory: "Сеть",
                    title: "Не усыплять сеть слишком агрессивно",
                    shortDescription: "Помогает избежать обрывов на ноутбуках и рабочих станциях, которые долго остаются подключёнными.",
                    longDescription: "Настройка делает поведение сетевого адаптера более предсказуемым: меньше неожиданных пробуждений и отвалов после сна, если соединение важно для работы.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Агрессивное энергосбережение",
                    recommendedState: "Стабильное подключение",
                    tags: new[] { "recommended", "laptop", "network" },
                    technicalSummary: "Работает с системными параметрами энергосбережения сетевого адаптера и профиля питания.",
                    affectedComponents: new[] { "Network adapter", "Power management", "Sleep resume" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Более жёсткое энергосбережение можно вернуть, если приоритетом является максимальная экономия батареи.",
                    validationHint: "Проверьте сеть после сна и длительного простоя устройства.",
                    notes: new[] { "Хорошо сочетается с ноутбучным шаблоном." }),

                CreateTweak(
                    id: "behavior-hide-lockscreen-tips",
                    subcategory: "Поведение системы",
                    title: "Убрать советы и промо на экране блокировки",
                    shortDescription: "Делает первый экран Windows спокойнее и убирает лишние рекомендации.",
                    longDescription: "Если устройство используется как рабочий инструмент, советы и промо на экране блокировки обычно только отвлекают и не несут реальной пользы.",
                    sourceType: TweakSourceType.Policy,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    isReversible: true,
                    currentState: "Советы могут показываться",
                    recommendedState: "Только фон и уведомления",
                    tags: new[] { "recommended", "clean-ui", "hidden" },
                    technicalSummary: "Ограничивает контент экрана блокировки, связанный с советами и промо-подсказками.",
                    affectedComponents: new[] { "Lock screen", "Windows spotlight tips", "Promotional content" },
                    rollbackType: "Мгновенный откат",
                    rollbackSummary: "Советы и подсказки можно вернуть без сброса персонализации экрана блокировки.",
                    validationHint: "Заблокируйте устройство и проверьте, исчезли ли советы и промо-элементы.",
                    notes: new[] { "Не влияет на обычный фон и уведомления безопасности." }),

                CreateTweak(
                    id: "behavior-stop-auto-reopen",
                    subcategory: "Поведение системы",
                    title: "Не открывать лишние приложения повторно после входа",
                    shortDescription: "Помогает возвращаться в чистую рабочую сессию, а не в набор случайно восстановленных окон.",
                    longDescription: "Полезно тем, кто предпочитает контролировать свой стартовый сценарий после перезагрузки и не хочет, чтобы система автоматически восстанавливала всё подряд.",
                    sourceType: TweakSourceType.Windows,
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: true,
                    isReversible: true,
                    currentState: "Приложения могут восстанавливаться",
                    recommendedState: "Ручной контроль",
                    tags: new[] { "recommended", "clarity", "beginner" },
                    technicalSummary: "Меняет системный поведенческий флаг повторного открытия приложений после входа и перезапуска.",
                    affectedComponents: new[] { "Sign-in behavior", "Restart apps", "Session restore" },
                    rollbackType: "Откат после повторного входа",
                    rollbackSummary: "Автоматическое восстановление окон можно вернуть через ту же настройку поведения системы.",
                    validationHint: "После повторного входа проверьте, восстановились ли ранее открытые приложения.",
                    notes: new[] { "Подходит тем, кто любит чистый старт рабочего дня." })
            };
        }

        public static List<TweakTemplateDefinition> BuildAdditionalTemplates()
        {
            return new List<TweakTemplateDefinition>
            {
                CreateTemplate(
                    id: "system-basic-privacy",
                    title: "Базовая приватность",
                    description: "Спокойный базовый набор для тех, кто хочет меньше лишней телеметрии и рекламы без жёстких радикальных отключений.",
                    scopeLabel: "Система / Приватность",
                    audience: "Обычный и pro-пользователь",
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: true,
                    tweakIds: new[]
                    {
                        "privacy-reduce-telemetry",
                        "privacy-disable-ad-id",
                        "privacy-limit-activity-history",
                        "network-limit-delivery-sharing"
                    }),

                CreateTemplate(
                    id: "system-less-background",
                    title: "Меньше фона",
                    description: "Сдерживает лишние фоновые процессы, обслуживание и сервисный шум, чтобы система была тише и предсказуемее.",
                    scopeLabel: "Система / Фоновая активность",
                    audience: "Обычный и pro-пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    tweakIds: new[]
                    {
                        "startup-essential-only",
                        "background-maintenance-window",
                        "services-diagnostics-calm",
                        "apps-limit-background-access"
                    }),

                CreateTemplate(
                    id: "system-balance",
                    title: "Баланс",
                    description: "Комфортный системный профиль без лишней агрессии: обновления остаются понятными, питание предсказуемым, а фон спокойнее.",
                    scopeLabel: "Система / Баланс",
                    audience: "Обычный пользователь",
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "updates-predictable-restarts",
                        "power-fast-startup",
                        "startup-essential-only",
                        "behavior-stop-auto-reopen"
                    }),

                CreateTemplate(
                    id: "system-performance",
                    title: "Производительность",
                    description: "Больше контроля над фоном, службами и автозапуском, когда важны отзывчивость и меньший системный шум.",
                    scopeLabel: "Система / Производительность",
                    audience: "Pro-пользователь",
                    riskLevel: TweakRiskLevel.Medium,
                    requiresRestart: true,
                    tweakIds: new[]
                    {
                        "startup-essential-only",
                        "background-maintenance-window",
                        "services-on-demand-gaming",
                        "apps-limit-background-access"
                    }),

                CreateTemplate(
                    id: "system-laptop",
                    title: "Ноутбук",
                    description: "Собирает более спокойное поведение сна, крышки, сети и обновлений под повседневный ноутбучный сценарий.",
                    scopeLabel: "Система / Ноутбук",
                    audience: "Обычный пользователь",
                    riskLevel: TweakRiskLevel.Low,
                    requiresRestart: false,
                    tweakIds: new[]
                    {
                        "power-lid-action-smart",
                        "network-keep-connection-stable",
                        "updates-predictable-restarts",
                        "behavior-stop-auto-reopen"
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
                Category = "System",
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

        private static void MoveAndTag(
            List<TweakDefinition> tweaks,
            string tweakId,
            string subcategory,
            params string[] tags)
        {
            var tweak = tweaks.FirstOrDefault(item => item.Id == tweakId);
            if (tweak == null)
                return;

            tweak.Subcategory = subcategory;

            foreach (var tag in tags)
            {
                if (!tweak.Tags.Contains(tag))
                    tweak.Tags.Add(tag);
            }
        }
    }
}
