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

## Партнёры закупки: data sharing (обязательно)

Без него закупка слепая. Adjust атрибутирует установки по ссылке партнёра, но ничего не отправляет партнёру обратно. В кабинете сети тогда 0 установок и нет ROAS, а ROAS-кампании не на чем обучаться. Случай Piggy Payday, 2026-10-14: в Adjust 13 установок с Axon под ссылкой «piggy» и ad revenue из MAX, а у Axon by AppLovin для приложения стоял Data Sharing **Disabled**. Каталог панели партнёров не настраивает, поэтому делай это через agentb с сессией Adjust ([browser.md](browser.md)).

**Когда:** на шаге 4 сразу после приложения Adjust, если игра пойдёт в закупку, и обязательно до старта трафика. Отдельно — для каждого партнёра, у которого есть ссылка на эту игру (Campaign Lab → Partners → колонка «Apps with links»). Facebook/Meta и Google Ads подключаются своими интеграциями, это не этот экран.

**Axon by AppLovin** (partner id 7):
1. Campaign Lab → Partners → Axon by AppLovin → Partner-level → Data Sharing → найди игру → Edit. Прямой адрес: `https://suite.adjust.com/partners/7/partner-settings-details/<adjust_app_token>/data-sharing`.
2. «Enable data sharing» → Edit → для каждой платформы в SDK keys вставь **account MAX SDK key** (`maxSdkKey` из private runtime config; он один на аккаунт студии и совпадает со страницей соседней игры). Сверяй равенство, ключ в чат не выводи. Save → платформа становится Enabled.
3. Опции как у эталона студии (Art Puzzle World, BubbleSort, Desert Worm): **Data from all attribution sources**; Sessions; Uninstalls and reinstalls; **In-app revenue**; Parameters → Send all parameters.
4. **Выручка покупок обязательна, если в игре есть IAP.** Нет события покупки — не повод пропустить: создай его в AppView → приложение → Events → Add event (имя `purchase`, не unique, все checks Off) и сразу впиши токен в игровой конфиг (`adjustPurchaseToken`). Код шлёт его по [analytics.md](analytics.md#покупка-в-adjust-обязательно). Затем «Map event»: Adjust event `purchase (<token>)` → Network event **In-App Checkout** (стандартное событие Axon для IAP) → Apply. Save → тост «… data sharing saved».
5. Readback: перезагрузи список Data Sharing партнёра. Строка игры — **Active**, «Installs, Reattribution, Revenue, Parameter, Session, Uninstall», Events mapped ≥ 1. Без Revenue при наличии IAP шаг не закрыт. Сохрани это в профиле как отдельный статус `adjust.partner_data_sharing.<partner>`. Включение не возвращает прошлые постбэки: данные пойдут с этого момента.

**Расход (ad spend)** — не на этом экране. Его импортирует DataWorks → Connections → `Applovin Reporting` (account-level, Report key, колонка Ad spend = OK). Для новой игры отдельно ничего не включается. Расход приходит с задержкой около суток: $0 за сегодня у всех AppLovin-сетей — норма. Сбой — это когда вчерашний расход у других игр есть, а у этой нет. Тогда проверь Ad spend mapping и аккаунт AppLovin кампании.

**Диагностика «закупка идёт, в Adjust/сети пусто»:** Datascape с `app_token__in=<token>`, dimensions `network,campaign`, metrics `installs,clicks,impressions,network_cost,ad_revenue`. Сеть в отчёте названа по имени ссылки (например, «piggy»), а не «AppLovin»: партнёра находи поиском ссылки в Campaign Lab → Partners. Установки и клики по ссылке есть → атрибуция работает, смотри data sharing. Кликов нет → ссылку не поставили в кампанию сети. Ad revenue есть → SDK и ILRD живы, consent ни при чём.
