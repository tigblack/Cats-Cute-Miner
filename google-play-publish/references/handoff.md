# Итоговые данные приложений и рекламы

После работы выводи владельцу фактические созданные/переиспользованные идентификаторы и статусы. Это обязательный результат, включая частично выполненный запуск. Перечитай нужные регистрации и привязки; при таймауте сначала ищи результат, не создавай новый ресурс ради отчёта. Удалённые/заменённые IDs обозначай историческими, выводи актуальные.

Сохрани отчёт `release/google-play/service-handoff.local.json` и читаемый `release/google-play/service-handoff.local.md`, исключённые из Git и архива скилла: полный набор SDK identifiers относится только к этой игре. В публичном profile оставь ссылки и безопасные статусы. Не копируй SDK keys игры в глобальные примеры. Приложенческие SDK API key/App token нужны владельцу для подключения и по его требованию показываются в итоговом ответе; административные API/OAuth tokens, cookies, пароли и ключи подписи в ответ не выводятся. Account-level MAX SDK key передавай ссылкой на private runtime config, отдельно от app/ad unit IDs.

Итоговый ответ содержит компактную таблицу сервисов:

| Сервис | Выводимые данные |
| --- | --- |
| AppMetrica | Название, числовой dashboard app ID, SDK API key, ссылка на кабинет, binding/Android activation/server events статусы |
| Adjust | Название, организация, SDK **app token**, Android/Google Play и точный package/App ID, reporting currency, sandbox/production, session/attribution/ILRD статусы. Event tokens — если созданы/подключены |
| Meta | App ID, Android package и статус App Events; client token — private configuration ref |
| AppLovin MAX | App ID или package с явным типом идентификатора; rewarded, interstitial, banner ad unit IDs, placements; SDK config ref |
| Google Play | Developer account, Console app ID/ссылка, package, locale и сохранение карточки; AAB/versionCode/трек/opt-in, если получены |
| IAP | Согласованные product IDs/типы/награды/цены и отдельные статусы Console/игры/покупки |

Следом обязательно покажи **какие mediation-сети вставлены**. Для каждой: network app ID/game ID и network placement/ad unit IDs по rewarded/interstitial/banner; SDK/adapter версии; MAX binding по каждому формату; device/ILRD проверка. Если provider требует App Key для binding, укажи его private ref и статус получения, не придумывай значение.

Различай `создано в сети`, `сохранено/перечитано в MAX`, `адаптер в Android сборке`, `реклама/доход проверены на устройстве`. При частичной готовности явно перечисли работающие форматы и оставшиеся причины: установленный Mintegral adapter не означает готовую Mintegral mediation; rewarded/interstitial Pangle не доказывают banner. Отсутствующий ID — «не создан/не получен», с конкретным следующим шагом. Не подменяй Adjust app token tracker/link token, AppMetrica numeric ID UUID ключом или MAX ad unit ID идентификатором сети.

Финальный ответ выводит актуальные ID сервисов/трёх MAX блоков и таблицу сетей непосредственно; подробный локальный отчёт дополняет их. Заверши состоянием карточки/релиза и действительно оставшимися проверками. Не обещай production-ready работу, если проверены только конфигурация и QA сборка.
