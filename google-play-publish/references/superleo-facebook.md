# SuperLeo: приложение Meta / Facebook

Контракт взят из обновления `superleo-monetization` и встроен в релизный скилл. Для «давай в стор» приложение Meta App Events этой игры обязательно. Это не шестая сеть mediation, не Audience Network и не запуск кампаний. Не создавай рекламные кабинеты, Pages и Instagram-аккаунты. Не вызывай `superleo_fb_onboard_game`: старый маршрут создаёт ресурсы сразу и обходит план/`confirm`.

Подключение и унаследованное разрешение — в [superleo.md](superleo.md). Технический `confirm: true` агент ставит сам в рамках уже принятого internal workflow. Спрашивай только если нет однозначного ad account/Page или каталог не содержит `facebook_*`. Если операций нет в развёрнутом каталоге, обновление скилла само службу не включает: используй [panel-preflight.md](panel-preflight.md) только при живом provision и иначе [service-access.md](service-access.md). `PROVISION_NOT_CONFIGURED` блокирует создание Meta, не остальные SDK.

## Найти и выбрать

1. Тот же клиент и ключ, что у монетизации. Нужны `capabilities.monetization` и операции `facebook_*` в каталоге. Личный ключ уже задаёт identity: не передавай чужой email. Поле `email` в body, если оно есть, — только контакт приложения.
2. Сопоставь игру с `projects` / `project` по проверенным platform и package. Нет карточки — отдельно подтверждённый `project_create`. Прочитай `facebook_apps`, `facebook_ad_accounts` и для полной привязки `facebook_pages`. Это чтения, `confirm` им не нужен. Ошибка или `message` при HTTP 200 и `data: []` — неудачное чтение, не пустой аккаунт. Список приложений может кэшироваться до пяти минут: отсутствие в кэше после создания или таймаута не повод создавать ещё раз.
3. Переиспользуй приложение, уже стоящее на этом проекте или совпадающее по точному store identity. Совпадение только по имени — кандидат. Несколько кандидатов или конфликт ID останавливают выбор. Бери ad account, уже выбранный на проекте, если он ещё активен. Иначе предлагай активный возвращённый аккаунт (`status: 1`) и не подменяй его другим после ошибки доступа. Page бери только из возвращённых ID и только когда сохраняешь полную конфигурацию проекта. Не выдумывай Page или Instagram Multicast.
4. Перед браузерными операциями вызови `facebook_browser_health`. Нужны `configured: true`, `ok: true`, `busy: false`. Это проверка процесса worker, не логина Dolphin и не прав FB provision token. Не отправляй credentials, CDP-адреса и команды браузера из игры. Login, checkpoint, занятый worker или отсутствующий туннель — блокер службы, не повод менять аккаунт или повторять создание.

## Порядок операций

Аргументы `superleo_monetization`. Без `confirm` это только локальный план, `providerValidated: false`.

Новое Android-приложение:

```json
{
  "operation": "facebook_create_app",
  "body": {
    "projectId": "project-id",
    "name": "Game Name",
    "platform": "android",
    "androidPackageName": "com.example.game",
    "adAccountId": "123456789"
  }
}
```

Для iOS: `platform: ios`, без `androidPackageName`, обязательны `iosBundleId` и числовой `storeId`. Префикс `act_` у ad account API нормализует сам. `force` и подмена worker отклоняются. Создание записывает App ID проекта и сохраняет client token на проекте. Оно не сохраняет выбранные Page и account mapping.

Полную привязку сохраняй тем же аккаунтом, которым создавалось приложение:

```json
{
  "operation": "facebook_project_config",
  "params": { "id": "project-id" },
  "body": {
    "facebookAppId": "APP_ID",
    "facebookAdAccountId": "123456789",
    "facebookPageId": "PAGE_ID"
  }
}
```

Если Page ещё нет, не подставляй чужой ID: сохрани App ID и оставь привязку Page ожидающей. Для уже существующего приложения сначала сохрани его подтверждённый config — refresh отвергает другой сохранённый App ID — затем:

```json
{
  "operation": "facebook_refresh_app",
  "body": {
    "projectId": "project-id",
    "appId": "APP_ID",
    "adAccountId": "123456789"
  }
}
```

Refresh читает существующее приложение и client token и авторизует указанный аккаунт. Уже авторизованный аккаунт переиспользуется. Ответы создания и refresh показывают `hasClientToken`, не значение токена. Не выводи полный config проекта. Если игре нужен client token в SDK, забери его приватным маршрутом проекта, в том числе `POST /facebook-apps/client-token` с `{projectId, appId}`, в игнорируемый config без stdout. Не встраивай provision token в игру.

## Проверка и восстановление

Проверяй MCP `isError`, `success` обёртки, native `success` и данные. HTTP 200 недостаточен. Сверь `appId`, платформу, `hasClientToken`, `clientTokenError`, `verificationWarning` и что `adAccountsOnPage` содержит выбранный ID без `act_`. Неподтверждённая привязка — частичный или неизвестный результат, не причина создать приложение заново.

`APP_EXISTS` / `existingAppId` означает переиспользовать существующее приложение. `partial: true` вместе с `appId` после ошибки значит, что приложение уже может существовать. Сохрани этот ID, перечитай проект и не передавай `force`, не меняй имя и не очищай App ID проекта.

После таймаута проверь health/lastRun, App ID проекта и список приложений. Не запускай параллельные создания. Неизвестный исход останавливает этот шаг. Refresh не чинит отсутствующую платформу: если создание остановилось на настройке платформы, сохрани ID и не объявляй платформу восстановленной.

После сохранения перечитай `project`: App ID, account и Page должны совпасть с планом. В отчёте раздели созданное или переиспользованное приложение, привязку, настройки проекта и оставшиеся блокеры. Создание приложения не доказывает установку SDK и события на устройстве.
