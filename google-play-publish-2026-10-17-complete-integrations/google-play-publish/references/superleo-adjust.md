# SuperLeo: приложение Adjust

Контракт взят из обновления `superleo-monetization`. Для «давай в стор» attribution-приложение Adjust этой игры обязательно. Это не рекламная кампания, не товар и не tracking link. `ADJUST_API_TOKEN` остаётся на сервере Superleo. Успешный список приложений не доказывает, что токен умеет создавать приложения. Не проси provider token в чате. Исторический upstream 401 в [panel-preflight.md](panel-preflight.md) не отменяет попытку `adjust_*`, если они есть в текущем каталоге.

Подключение — в [superleo.md](superleo.md). `confirm: true` агент передаёт сам для уже принятого плана. Смена шаблона, копирование чужих событий и замена уже сохранённого другого токена требуют отдельного согласия.

## Найти существующее

1. Возьми игру из репозитория и `projects` / `project`. Для Android `storeId` и `bundleId` оба равны package name. Для iOS `storeId` — числовой App Store ID, префикс `id` нормализуется, а `bundleId` — reverse-DNS. Не подставляй одно вместо другого.
2. Прочитай `adjust_settings` и `adjust_apps`. Settings показывают `useAutomationApi` и `templateAppToken`, не API credentials. Если автоматизация в панели выключена, это блокер для оператора: не меняй Admin settings и не переключай аккаунт молча. Ошибка чтения — не пустой аккаунт.
3. `adjust_find_app` принимает query `{platform, store_id}`. `adjust_app` принимает params `{token}`. Ответ finder `state: "not_found"` — настоящий provider 404, не ошибка транспорта, но одного его мало для создания. Сверь список и уже сохранённый `adjustTrackerToken` проекта: finder может не видеть старые ручные приложения, а `adjust_app` для них использует settings endpoint.
4. Совпадение проверяй по platform и store identity, не только по имени. У мультиплатформенного приложения смотри `android_package`, `ios_app_id`, `ios_bundle_id`. Платформа по умолчанию Android не означает отсутствия iOS. Конфликт разрешай выбором, точное совпадение переиспользуй.
5. По умолчанию создавай минимально: `skipTemplate: true`. `skipTemplate: false` копирует events, partners, callbacks, attribution и S2S из серверного шаблона и не копирует trackers. Такое копирование и конкретный шаблон согласуй отдельно. Неясный шаблон — блокер, не разрешение создать без него.

## Создать и дождаться

```json
{
  "operation": "adjust_create_app",
  "body": {
    "name": "Game Name",
    "platform": "android",
    "storeId": "com.example.game",
    "bundleId": "com.example.game",
    "skipTemplate": true
  }
}
```

Без `confirm: true` проверяются только локальный план и права, `providerValidated: false`. API отвергает лишние credentials, `channelSetup`, подмену шаблона, `force` и произвольные settings. Партнёры, события, ссылки, запуск job, удаление и Admin settings этим контрактом не доступны.

Перед созданием API снова читает список. `APP_EXISTS` возвращает `existingApps`: переиспользуй эти токены, не переименовывай приложение, чтобы обойти отказ. `APP_DISCOVERY_FAILED` значит, что создание не начиналось. Это не распределённая гарантия exactly-once: не запускай параллельные создания одной игры.

Результат лежит в `data` обёртки MCP. Смотри явные `state` и `completed`:

- `pending` / `running` — запрос принят или ещё идёт, это не завершение.
- `unverified` — вернулся app token без ticket; прочитай само приложение.
- `completed` — завершился конкретный ticket; приложение всё равно перечитай.
- `failed` / `unknown` — не объявляй настройку успешной и не создавай приложение заново автоматически.

Сохрани и `adjust_app_token`, и `ticket_token`, если они вернулись. Статус ticket читай через `adjust_ticket_status`, params `{ticket: ticket_token}`, с интервалом пять секунд и пределом пять минут. Остановись на завершении, ошибке, неизвестном статусе или таймауте. Pending после предела остаётся pending: сохрани ticket для следующей проверки. Чтения можно повторять, создание — нет. Вложенная ошибка в ticket помечает результат неуспешным, даже если верхний статус говорит completed. Сырой partner config намеренно не показывается.

После таймаута создания перечитай проект, список, finder, детали и известный ticket. Если исход неоднозначен, останови этот шаг. Не создавай второе приложение и не удаляй первое как откат.

## Привязать и проверить

Прочитай приложение через `adjust_app` и сверь platform, store и bundle. `adjust_app_token` — идентификатор SDK, не серверный API credential. Историческое поле проекта `adjustTrackerToken` здесь хранит именно этот app token, а не токен рекламной ссылки.

```json
{
  "operation": "project_network_ids",
  "params": { "id": "project-id" },
  "body": { "adjustTrackerToken": "returned_app_token" }
}
```

До `confirm` это тоже только план. Замена другого уже сохранённого токена требует согласия. Само создание токен в проект не записывает. После сохранения перечитай проект. Нет карточки проекта — сначала `project_create`.

В отчёте раздели создание или переиспользование, исход ticket, проверенную identity и сохранённую привязку. Установка SDK и событие на устройстве — отдельные шаги. При минимальном создании не обещай настройку партнёров.

## События и токены

Этот контракт не создаёт события: нужные игре custom events и их токены заводятся в Adjust-кабинете (импорт из приложения-образца или Add event) через agentb ([browser.md](browser.md)), через App Automation API или шаблоном, см. [analytics.md](analytics.md). Отсутствие токенов не блокирует создание/привязку приложения, SDK init и release. Adjust Purchase Verification не подключается: покупки валидирует сервер Multicast.

## Полная настройка связей для закупки

Результат setup/release/full — готовые применимые интеграции, а не только приложения, SDK keys и флаги Active. В начале сверь текущий scope игры: Axon by AppLovin, Meta, Unity Ads, Mintegral и другие уже выбранные каналы закупки; наличие сети в MAX mediation не означает автоматически выбранную закупку через неё. Если канал уже поручен, зарегистрирован или включён в плане, доведи его до конца в рамках этого поручения. Не откладывай найденный доступный пробел фразой «если будем закупаться», когда этот канал уже входит в scope. Не создавай платные кампании или новые каналы вне задачи.

Для каждого применимого канала веди одну строку профиля: own provider identity → SDK/API source → partner data sharing → ad revenue/source → IAP mapping → нужные события/параметры → link token/click URL/impression URL → connections/store/spend mapping → сохранённый readback → runtime/delivery. Каждый пункт получает `verified`, `not_applicable` с причиной либо конкретный blocker/следующий шаг. Перед завершением перечитай строки: доступные пустые/выключенные настройки исправь, неподтверждённое получение не помечай готовым. Один failed сервис не останавливает остальные строки.

| Канал | Что закрыть в Adjust до передачи в закупку |
| --- | --- |
| Axon by AppLovin (7) | Own app, account MAX SDK key, все согласованные источники/sessions/parameters, реальная покупка → In-App Checkout, tracking links, атрибуция клика и показа, account Applovin Reporting для расходов. Отдельно проверить реальный маршрут ad revenue для adROAS; IAP revenue не заменяет ad revenue. Подробности ниже. |
| Facebook/Meta (34) | Own Meta App ID; при рекламе передача **Ad revenue (from ad views)** с единственным выбранным источником `AppLovin MAX (SDK)` для MAX SDK pipeline и нужный AdImpression Mapping; Adjust как источник данных в Meta через панель/служебные операции; реальное получение. Для IAP выбери один путь: прямой Meta SDK либо Adjust purchase → fb_mobile_purchase. Прямой Meta SDK IAP не препятствует отдельной передаче ad revenue из Adjust. Не выключай все revenue только ради устранения IAP-дублей. SAN-связь проверяется по контракту Meta; обычная network tracking link требуется не для всех SAN. |
| Unity Ads (217) | Own Game ID, Active, все согласованные источники; при adROAS **Ad revenue** из MAX SDK, нужные sessions/parameters, purchase → purchase при IAP, собственные click/impression links и проверка атрибуции. Не считать строку Active достаточной при выключенной выручке или отсутствии links. |
| Mintegral (369) | Active, ad revenue из MAX SDK при рекламе, нужные sessions/parameters, purchase → purchase при IAP и собственные click/impression links. Галочка In-app revenue при пустой таблице Map your events не закрывает передачу покупок. Перечитай фактический mapping, не полагайся на старый receipt. |
| Другие выбранные каналы | Собственные IDs и актуальный partner contract, разрешённые денежные источники, mapping/link/SAN и connections по их назначению. Настройки чужой игры — read-only пример, не готовый конфиг для копирования. |

**Атрибуция:** для обычной мобильной закупки сверяй click-through и view-through с требованиями выбранной сети; для Axon [официальный минимум](https://support.applovin.com/en/growth/promoting-your-apps/track-and-optimize/adjust) — 7 дней для клика и 24 часа для показа; также проверь требуемые сетью device matching и probabilistic modeling. Для остальных каналов сверяй собственный контракт. Проверяй app → partner → конкретную link: наследование выключенного view-through не является завершённой настройкой для канала, которому нужен учёт показов. В целевой игре выстави поддерживаемые значения; отличающуюся намеренную политику или неподдерживаемое измерение запиши как исключение с причиной. Не переноси iOS probabilistic/cross-device настройки другой игры автоматически.

**Connections:** DataWorks подключает аккаунты, а не обязательно каждое приложение отдельно. Прочитай существующие подключения и app/store/spend mappings, переиспользуй нужный включённый аккаунт; не создавай дубликат для новой игры. Account-level OK не доказывает получение данных этой игры. Общие ошибки чини только в разрешённом scope; не меняй чужие игры и global credentials. При MAX API + MAX SDK источниках явно выбери источник для отчётов и пересылки, чтобы не суммировать одну выручку дважды.

**Параметры и покупки:** проверь реальный SDK caller. `addCallbackParameter` не заменяет `addPartnerParameter`; Send all parameters без partner payload не доказывает отправку контекста сети. Передавай только нужные и допустимые поля. Не заменяй полноценную серверную валидацию Multicast Adjust Purchase Verification. Branded domain, iOS, RevenueCat, uninstall measurement и антифрод другой игры включай только при применимости и реализации их зависимостей. CCPA/Limited Data Use и consent определяются политикой/пользователем, а не копированием выключенного флага соседа.

**Проверка:** реальное действие в актуальной Play-сборке → Adjust → нужный получатель; install/session, ad revenue и purchase проверяются отдельно. Если нет устройства, доступа Meta или реальной транзакции, закончи доступные настройки и оставь именно delivery pending с точной причиной. Не объявляй всю связку завершённой только по сохранённой форме. Чужие приложения не исправляй без отдельного поручения.

## Партнёры закупки: data sharing (обязательно)

Без него закупка слепая. Adjust атрибутирует установки по ссылке партнёра, но ничего не отправляет партнёру обратно. В кабинете сети тогда 0 установок и нет ROAS, а ROAS-кампании не на чем обучаться. Случай Piggy Payday, 2026-10-14: в Adjust 13 установок с Axon под ссылкой «piggy» и ad revenue из MAX, а у Axon by AppLovin для приложения стоял Data Sharing **Disabled**. Каталог панели партнёров не настраивает, поэтому делай это через agentb с сессией Adjust ([browser.md](browser.md)).

**Когда:** на шаге 4 сразу после приложения Adjust, если игра пойдёт в закупку, и обязательно до старта трафика. Отдельно — для каждого партнёра, у которого есть ссылка на эту игру (Campaign Lab → Partners → колонка «Apps with links»). Facebook/Meta и Google Ads подключаются своими интеграциями, это не этот экран.

**Axon by AppLovin** (partner id 7):
1. Campaign Lab → Partners → Axon by AppLovin → Partner-level → Data Sharing → найди игру → Edit. Прямой адрес: `https://suite.adjust.com/partners/7/partner-settings-details/<adjust_app_token>/data-sharing`.
2. «Enable data sharing» → Edit → для каждой платформы в SDK keys вставь **account MAX SDK key** (`maxSdkKey` из private runtime config; он один на аккаунт студии и совпадает со страницей соседней игры). Сверяй равенство, ключ в чат не выводи. Save → платформа становится Enabled.
3. Опции как у эталона студии (Art Puzzle World, BubbleSort, Desert Worm): **Data from all attribution sources**; Sessions; Uninstalls and reinstalls; **In-app revenue**; Parameters → Send all parameters.
4. **Выручка покупок обязательна, если в игре есть IAP.** Нет события покупки — не повод пропустить: создай его в AppView → приложение → Events → Add event (имя `purchase`, не unique, все checks Off) и сразу впиши токен в игровой конфиг (`adjustPurchaseToken`). Код шлёт его по [analytics.md](analytics.md#покупка-в-adjust-обязательно). Затем «Map event»: Adjust event `purchase (<token>)` → Network event **In-App Checkout** (стандартное событие Axon для IAP) → Apply. Save → тост «… data sharing saved».
5. Readback: перезагрузи список Data Sharing партнёра. Строка игры — **Active**, «Installs, Reattribution, Revenue, Parameter, Session, Uninstall», Events mapped ≥ 1. Без Revenue при наличии IAP шаг не закрыт. Сохрани это в профиле как отдельный статус `adjust.partner_data_sharing.<partner>`. Включение не возвращает прошлые постбэки: данные пойдут с этого момента.

### Рекламная ссылка партнёра: отдельная обязательная проверка

App token и включённый data sharing не доказывают наличие tracking link. До передачи игры в закупку проверь ссылку выбранного партнёра для этой игры. Ссылку можно подготовить в рамках настройки интеграции; создание или запуск платной кампании требует отдельного поручения.

Для Axon by AppLovin:
1. Открой `https://suite.adjust.com/partners/7/links/overview?selectedApp=<adjust_app_token>` через agentb на хабе. Дождись загрузки списка, проверь выбранное приложение и существующие ссылки, включая скрытые. Ошибка загрузки не означает пустой список. Подходящую действующую ссылку переиспользуй.
2. Если ссылки нет, нажми **New link**. Выбери собственное приложение по проверенным имени/package/app token и сеть **Axon by AppLovin**. В мастере проверь выбранную игру перед сохранением. Если data sharing уже настроен, отдельный шаг его настройки может отсутствовать.
3. На **Link structure** задай понятное имя с `AppLovin`, оставь **Single-device link** для обычной мобильной закупки и штатные макросы партнёра: campaign `{CAMPAIGN_NAME}%20({CAMPAIGN_ID})`, adgroup `{APP_ID}`, creative `{AD_NAME}`. Не подменяй макросы собственными значениями. На **User destinations** оставь App stores для обычной установки из стора; специальные deep link/redirect задавай только по задаче. Настройки атрибуции наследуй от проверенного партнёра, если другое не поручено.
4. На **Setup review** сверь приложение, партнёра, структуру ссылки и назначения, затем **Create link**. Это создаёт ссылку, а не рекламную кампанию. При таймауте сначала перечитай список ссылок; не повторяй создание вслепую.
5. После сохранения повторно открой **Link URLs** и список ссылок выбранной игры. Сохрани в профиле `providers.adjust.tracking_links.applovin`: имя, link token, полные **click URL и impression URL с макросами**, статус readback и evidence ref. SDK app token в `adjustTrackerToken` проекта не заменяй link token. Для кампании передавай оба полных URL, а не голый адрес без макросов; сохраняй штатные endpoint/макросы выбранной сети (например, Unity использует s2s.adjust.com), не переписывай их под AppLovin; не открывай tracking URL для проверки, чтобы не создавать искусственные клики.

После создания ссылки перечитай также data sharing: правильный SDK key, Enabled, источники данных и purchase mapping должны сохраниться. Отдельно зафиксируй три результата: **настройки партнёра**, **наличие ссылки**, **фактическая доставка установок/событий/выручки**. Первые два не доказывают третий. Для adROAS отдельно проверь маршрут рекламной выручки и её получение нужным сервисом; In-app revenue и purchase mapping относятся к покупкам и не заменяют эту проверку.

Если API панели не поддерживает партнёров или ссылки, продолжай через [agentb](browser.md): перенеси только сессию Adjust после согласия Chrome и работай браузером на хабе. После фактического `AUTH_REQUIRED` и сообщения владельца о новом входе обнови её через `login-hub adjust --refresh`; недостаток API панели сам по себе не повод требовать пароль или прямой provider token. Дождись загрузки содержимого настройки: заголовок кабинета без полей и таблиц не подтверждает результат.

**Расход (ad spend)** — не на этом экране. Его импортирует DataWorks → Connections → `Applovin Reporting` (account-level, Report key, колонка Ad spend = OK). Для новой игры отдельно ничего не включается. Расход приходит с задержкой около суток: $0 за сегодня у всех AppLovin-сетей — норма. Сбой — это когда вчерашний расход у других игр есть, а у этой нет. Тогда проверь Ad spend mapping и аккаунт AppLovin кампании.

**Диагностика «закупка идёт, в Adjust/сети пусто»:** Datascape с `app_token__in=<token>`, dimensions `network,campaign`, metrics `installs,clicks,impressions,network_cost,ad_revenue`. Сеть в отчёте названа по имени ссылки (например, «piggy»), а не «AppLovin»: партнёра находи поиском ссылки в Campaign Lab → Partners. Установки и клики по ссылке есть → атрибуция работает, смотри data sharing. Кликов нет → ссылку не поставили в кампанию сети. Ad revenue есть → SDK и ILRD живы, consent ни при чём.
