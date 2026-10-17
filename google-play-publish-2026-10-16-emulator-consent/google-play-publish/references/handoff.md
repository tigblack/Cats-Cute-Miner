# Итоговые данные приложений и рекламы

После setup/изменения интеграций выводи полный перечень приложений и mediation. При update неизменной интеграции переиспользуй подтверждённый отчёт: укажи artifact/version/трек, изменённые IDs и ссылку на прежнюю таблицу; не получай все SDK keys заново ради ответа. Reuse требует подтверждённого account/package/config.

После setup/изменения интеграций выводи владельцу фактические созданные/переиспользованные идентификаторы и статусы, включая частично выполненный запуск. Для update неизменной интеграции используй текущий подтверждённый отчёт и покажи изменения. Перечитай нужные регистрации и привязки; при таймауте сначала ищи результат, не создавай новый ресурс ради отчёта. Удалённые/заменённые IDs обозначай историческими, выводи актуальные.

Сохрани отчёт `release/google-play/service-handoff.local.json` и читаемый `release/google-play/service-handoff.local.md`, исключённые из Git и архива скилла: полный набор SDK identifiers относится только к этой игре. В публичном profile оставь ссылки и безопасные статусы. Не копируй SDK keys игры в глобальные примеры. Приложенческие SDK API key/App token нужны владельцу для подключения и по его требованию показываются в итоговом ответе; административные API/OAuth tokens, cookies и пароли сервисов в ответ не выводятся. Исключение — сводка владельцу после отправки в production (ниже): по его прямому требованию в ней есть keystore alias и пароли. Account-level MAX SDK key передавай ссылкой на private runtime config, отдельно от app/ad unit IDs.

После setup/изменения интеграций итоговый ответ содержит компактную таблицу сервисов:

| Сервис | Выводимые данные |
| --- | --- |
| AppMetrica | Название, числовой dashboard app ID, SDK API key, ссылка на кабинет, binding/Android activation/server events статусы |
| Adjust | Название, организация, SDK **app token**, Android/Google Play и точный package/App ID, reporting currency, sandbox/production, session/attribution/ILRD статусы. Event tokens — если созданы/подключены |
| Meta | App ID, Android package и статус App Events; client token — private configuration ref |
| AppLovin MAX | App ID или package с явным типом идентификатора; rewarded, interstitial, banner ad unit IDs, placements; SDK config ref |
| Google Play | Developer account, Console app ID/ссылка, package, locale и сохранение карточки; AAB/versionCode/трек/opt-in, если получены |
| IAP | Согласованные product IDs/типы/награды/цены и отдельные статусы Console/игры/покупки |

После setup/изменения интеграций следом покажи **какие mediation-сети вставлены**. Для каждой: network app ID/game ID и network placement/ad unit IDs по rewarded/interstitial/banner; SDK/adapter версии; MAX binding по каждому формату; device/ILRD проверка. Если provider требует App Key для binding, укажи его private ref и статус получения, не придумывай значение.

Различай `создано в сети`, `сохранено/перечитано в MAX`, `адаптер в Android сборке`, `реклама/доход проверены на устройстве`. При частичной готовности явно перечисли работающие форматы и оставшиеся причины: установленный Mintegral adapter не означает готовую Mintegral mediation; rewarded/interstitial Pangle не доказывают banner. Отсутствующий ID — «не создан/не получен», с конкретным следующим шагом. Не подменяй Adjust app token tracker/link token, AppMetrica numeric ID UUID ключом или MAX ad unit ID идентификатором сети.

После setup/изменения интеграций финальный ответ выводит актуальные ID сервисов/включённых MAX блоков и таблицу сетей непосредственно; подробный локальный отчёт дополняет их. Неизменный update сохраняет ссылку и показывает только изменения. Заверши состоянием карточки/релиза и действительно оставшимися проверками. Не обещай production-ready работу, если проверены только конфигурация и QA сборка.

## Сводка после отправки в production

Короткий блок в чате, после таблиц выше и по актуальным данным этой игры:

- Android: package/bundle, versionName (versionCode), SHA-256 AAB, трек и статус (например, In review), opt-in internal.
- Охват: фактические ABI, проверка полного runtime, production Device catalog/новые исключения и совместимость с платформой/гео закупки (`pass`/`fail`/`unknown`). Успешный internal не заменяет эту проверку.
- Подпись: путь к keystore, **alias, store и key password**, статус Play App Signing, публичный fingerprint. Значения читай программно из private signing config и покажи только владельцу в этом сообщении; не копируй их в `profile.json`, `status.md`, отчёты, архив, логи и команды. Если пароля нет в конфиге, так и напиши, ничего не подбирай.
- Сервисы: Meta/Facebook App ID, AppMetrica app ID и SDK API key, Adjust app token и окружение, AppLovin MAX SDK key (private ref), ad unit IDs и подключённые сети, IAP product IDs.
- Наблюдатель: фаза (`review`), ссылка на `watch.json`, когда и что он сообщит.
- Отсутствующее помечай «не получено» с конкретным следующим шагом. Эта сводка заменяет повторный пересказ всего журнала, подробности остаются в `service-handoff.local.md`.
