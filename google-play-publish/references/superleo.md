# SuperLeo MCP внутри релизного скилла

Этот пакет включает адаптированные клиент и контракты `superleo-monetization`, переданного разработчиком студии. Вторая папка/второй SKILL.md не нужны. Главные правила и состояние остаются в [../SKILL.md](../SKILL.md) и [state.md](state.md): обязательны четыре SDK; internal уже разрешён релизной командой; IAP-каталог согласуется; production отдельно. MCP автоматизирует административные операции панели, а SDK/адаптеры и реальное внедрение в игру выполняет агент по [engines.md](engines.md).

Если личного подключения или права нужного provider действия не хватает, запроси его сам по [service-access.md](service-access.md). Отсутствующий MCP метод не отменяет создание AppMetrica/Adjust: после запроса доступа продолжи через официальный API/кабинет, если панель не покрывает операцию. SDK key не заменяет доступ управления; не поручай создание приложений владельцу.

## Подключение без браузерного исследования

Предпочти готовое подключение SuperLeo MCP к нужному аккаунту. Если его нет, bundled `scripts/superleo.mjs` работает на Node.js 18+ без npm-зависимостей и без обязательной установки MCP-сервера. Инсталляция/включение сервера в MultiCode остаётся штатной операцией инструментов интеграций, не ручной правкой агентских конфигов.

Без готового MCP-подключения сначала открой предоставленную панель (основной UI: https://dostupai.aimulticast.org/) и используй штатный вход текущего сотрудника. Browser workflow не требует MCP-ключа. Успешный вход подтверждает только сессию; права создания/настройки и upstream доступы проверяются для нужной операции. Сначала используй реальные формы и уже доступные подключения, затем запрашивай конкретное недостающее право.

На 13.10.2026 в `/settings` этого UI реально наблюдался раздел **API и MCP-доступ** с адресом `/mcp` и указанием «Для выпуска или перевыпуска ключа обратитесь к администратору». Это не инструкция искать меню Admin: оно отсутствовало у проверенного аккаунта, хотя его отображаемая роль называлась «Администратор». Отдельная карточка **API Keys → Manage** disabled/Coming Soon не является выдачей MCP-ключа. Не давай ненаблюдавшийся путь Admin → Users и не делай получение ключа обязательным начальным шагом. Запрос к оператору нужен, когда выбран MCP-маршрут и ключа действительно нет; доступный UI маршрут продолжается независимо.

Персональный `slmcp_…` key задаёт identity/capabilities; не передавай другой email для смены этой identity. Административные credentials провайдеров остаются на backend. SDK client key и MCP management key имеют разное назначение; не копируй management-конфиг в игру. Если оператор уже сохранил `APPLOVIN_SDK_KEY` в `assets/.env.sdk.local` или отдельном private game config, прочитай это поле программно без stdout и перенеси только его в реальный SDK consumer после проверки account match. Форма для локального заполнения — [../assets/.env.sdk.local.example](../assets/.env.sdk.local.example); `superleo.mjs` этот SDK-файл не загружает.

Используй уже предоставленный private config, не проси ключ повторно. Клиент принимает `SUPERLEO_URL` и `SUPERLEO_API_KEY` как данные, без shell evaluation. Выбор файла:

1. Явный `--config <private-file>` — единственный источник, без fallback при ошибке.
2. Без параметра: `assets/.env`, затем `assets/.env.superleo` относительно установленного скилла, затем `release/google-play/superleo.local.env`, затем `.env.superleo` в корне игры.
3. Первый существующий файл выбирается целиком; ошибка/invalid config не переключает клиента на другую учётку. Файлы не объединяются.

Создавай config из [../assets/.env.superleo.example](../assets/.env.superleo.example), только если нужного файла ещё нет. Private файлы должны быть ignored/untracked и доступны владельцу (0600 на Linux, подходящий ACL на Windows). `assets/.gitignore` входит в пакет; для per-game файлов добавь соответствующее правило проекта. Не помещай ключ в request JSON, CLI-аргумент, чат или отчёт. При копировании/ZIP исключай actual `.env*` и SDK private config независимо от gitignore.

В примере endpoint `https://dostupai.aimulticast.org/mcp`; владелец также использует `https://dostupai-mur.up.railway.app/mcp`. Это возможные **разные подключения**, не автоматический fallback. Ключ отправляется только выбранному URL; клиент запрещает redirects. Не меняй URL того же ключа на другой сервер без соответствующего предоставленного подключения. После `access` проверь identity и `capabilities.monetization`; role=root сам по себе не доказывает права admin или нужное project scope.

```text
node <skill>/scripts/superleo.mjs access --config <private-file>
node <skill>/scripts/superleo.mjs catalog --config <private-file>
node <skill>/scripts/superleo.mjs call release/google-play/request.json --config <private-file>
```

`request.json` содержит только реальные tool arguments, например `{"operation":"projects"}`. Для методов: `operation`, path IDs в `params`, фильтры в `query`, native payload в `body`, технический `confirm` при необходимости. Сначала изучи текущий runtime catalog; не выдумывай operation/поля по похожему названию. При новом/неописанном create schema бери фактическую форму/документацию backend, а не догадку.

SDK-config/project/connections/IAP ответы могут содержать другие credentials. Для них используй полный private response, извлекая нужное поле программно без stdout:

```text
node <skill>/scripts/superleo.mjs call release/google-play/request.json --config <private-file> --private-output release/google-play/superleo-response-<unique-id>.local.json
```

Добавь ignore `release/google-play/superleo-*.local.json` до записи. Private mode сохраняет JSON (0600 на Linux) и выводит только путь/признак ошибки. Existing output запрещён до сетевого вызова: выбирай новый путь; это не повод повторять уже выполненный write. Публичный вывод маскирует token/key/secret поля и MCP key, но полные ответы всё равно не нужно включать в отчёт. CLI exit1 учитывает MCP `isError`, native `success:false` и поэлементный `ok:false`. Exit0 означает отсутствие распознанной ошибки, а не готовность приложения: проверить план/исполнение и read-back обязан агент.

## Идентичность и разрешённые записи

Скилл переносим между корпоративными пользователями: каждый запускает его со своим подключением. Другая почта/actor сама по себе нормальна и не требует согласования. Прочитай `projects`, затем точный `project` по platform+package. Сохрани endpoint, actor и project ID отдельно для каждого подключения. 404 прежнего ID означает, что нужно найти карточку в текущем подключении, а не требовать прежнюю учётку. Перечитай provider resources по package и переиспользуй совпадающие MAX/AppMetrica/etc. При отсутствии карточки создай её в текущем предоставленном корпоративном аккаунте в рамках уже разрешённого релиза. Не создавай заново сами provider apps/ad units лишь из-за другой карточки панели. Только фактический конфликт организации/package или недостаточные права требуют уточнения; независимые этапы продолжаются.

Для разрешённой игры при подтверждённом account scope отсутствие карточки допускает `project_create`: `{name, platform:"android", androidPackageName}`; iOS требует настоящий numeric App Store ID, не bundle ID. Карточка не создаёт автоматически все provider apps или Multicast game.

Запрос write без `confirm` возвращает **план**, а не provider dry-run (`providerValidated:false`). Проверяй план/фактическое выполнение отдельно. Уже принятое поручение на этот internal workflow покрывает зависимые write шаги; агент сам передаёт `confirm:true` и точные `confirmation` строки. Не запрашивай заново разрешение для каждого ID, подписи/загрузки/внутреннего выпуска. Согласовываются неизвестные продуктовые/правовые решения и изменения scope, а IAP — только после принятого каталога. Технический параметр не разрешает production, чужой проект, кампании закупки трафика или изменения global OAuth credentials.

## Маршруты

- Рекламные приложения/placements, bidding, сохранение MAX mediation: [superleo-ads.md](superleo-ads.md). AdMob, Unity Ads, Mintegral, Pangle — стандартный mediation набор панели для этого скилла; explicit project configuration имеет приоритет. Он не заменяет AppMetrica/Adjust/Meta. Агент обязан настроить каждую сеть и её official adapter либо сохранить конкретный provider failure; только неизвестные category/audience сведения требуют решения пользователя.
- IAP import в Google Play + Multicast backend: [superleo-iap.md](superleo-iap.md). Применяй, если выбранная архитектура игры использует этот backend. Import активирует товары и может менять mappings/цены; проверяй оба target результата. Наш runtime purchase/grant/receipt контракт остаётся в [play.md](play.md).
- Создание Play страницы, signed AAB staging, draft/adoption, testers и internal release: [superleo-play.md](superleo-play.md). Каталог backend и `play_browser_health.onboarding.commands` проверяются отдельно. Worker должен реально поддерживать выбранные команды и инструменты AAB. Сам MCP не собирает/не переносит AAB; агент строит настоящую игру и использует доступный разрешённый transport к browser host с повторной SHA-256 проверкой.
- AppMetrica: `appmetrica_apps`, `appmetrica_categories`, `appmetrica_app` с `params:{id}`, `appmetrica_sdk_config` с `params:{id}`, `appmetrica_create_app`. Создание и readback проверены на реальном новом приложении 2026-10-09. Body плоский: `{name,category,time_zone_name,hide_address,gdpr_agreement_accepted}`; backend сам оборачивает его в `application`. Сначала прочитай категории. Timezone и privacy бери из решения проекта, не копируй чужое принятие GDPR. `created:false` вместе с `needsSelection:true` и `existingIds` — конфликт имени, а не успех: выбери существующий ID, не переименовывай приложение, чтобы продавить создание. `created:true, verified:false` — частичный результат: сохрани ID и перечитай его, не создавай второе. Параллельные создания и слепой retry после таймаута запрещены. Проверенный numeric ID запиши через `project_network_ids`, body `{appMetricaAppId:"123"}`, предварительно прочитав проект; замена другого ID требует согласия. `appmetrica_sdk_config` возвращает только клиентские `id` и `sdkKey`: сохрани их приватно и сверь с runtime config. OAuth и `import_token` в игру не встраивай. UUID SDK key не подставляй вместо numeric app ID.
- Adjust: [superleo-adjust.md](superleo-adjust.md). Операции текущего каталога: `adjust_settings`, `adjust_apps`, `adjust_find_app`, `adjust_app`, `adjust_create_app`, `adjust_ticket_status`. Поле проекта `adjustTrackerToken` здесь хранит SDK app token, не токен рекламной ссылки. Обновление коллеги заменяет снимок от 2026-10-08, где этих операций не было. Если их нет в развёрнутом каталоге или upstream по-прежнему отказывает, запроси доступ и продолжи прямым кабинетом по [service-access.md](service-access.md); уже полученный собственный token переиспользуй.
- Meta App Events: [superleo-facebook.md](superleo-facebook.md). Операции: `facebook_apps`, `facebook_ad_accounts`, `facebook_pages`, `facebook_browser_health`, `facebook_create_app`, `facebook_refresh_app`, `facebook_project_config`. Это не Audience Network и не кампания. Не вызывай `superleo_fb_onboard_game`. Ключ MCP не чинит `PROVISION_NOT_CONFIGURED`: восстанови службу или иди в прямой доступ, не останавливая остальные интеграции. Браузерная форма из [panel-preflight.md](panel-preflight.md) остаётся запасным путём, когда `facebook_*` в каталоге нет.

## Read-back и продолжение

Транспорт, backend wrapper и каждый provider/per-SKU результат проверяются отдельно. Пустой список с provider error/message не означает отсутствие ресурсов. После write сохрани actual IDs и перечитай только нужные сущности. Нет глобальной транзакции/автоматического rollback: не удаляй ресурсы и не перезапускай весь импорт ради одного failed target.

После таймаута/no response сначала сверяй resource state/receipt/worker journal по устойчивому request ID. Не меняй requestId/body и не очищай journal, чтобы повтор прошёл. Private responses и профили остаются для продолжения; секреты и payloadы provider credentials не попадают в общий пакет.

Внутри единого скилла последовательно выполняй: identity/catalog → provider resources → SDK/adapters/игровые callbacks/аналитика → Android → необходимый billing-enabled internal bootstrap → согласованные IAP/runtime → финальный AAB/internal → устройство и реальные события/покупки. Часть шагов можно выполнить параллельно при независимых зависимостях. Скилл требует живое подтверждение результата каждого сервиса, а не наличия ID или успешной сборки.

Отдельно фиксируй возможность подготовить/выпустить internal и подтверждение всего runtime. Отсутствие устройства не запрещает подготовку или разрешённый internal для тестеров; проверки SDK/рекламы/дохода/покупок остаются ожидающими по [verification.md](verification.md). Ошибка FB/Play-службы блокирует конкретный внешний шаг, а не аудит, SDK-код, IAP-контракт, локальную сборку и другие независимые операции. Если доступ к обслуживанию backend уже предоставлен, выполни разрешённое исправление; иначе передай точную ошибку и требуемую настройку без повторного запроса разрешения на сам релиз.
