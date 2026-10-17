# Аналитика: AppMetrica + зеркало в Adjust

**AppMetrica — основная продуктовая аналитика**: все события игры с полным JSON-контекстом, денежный доход и ad revenue по [sdk-validation.md](sdk-validation.md) и [devour.md](devour.md). **Adjust — зеркало** тех же событий плюс attribution, сессии (retention, DAU, время сессий) и ILRD. Сбой серверов AppMetrica (так было в октябре 2026) не повод её выкидывать: SDK, активация и события внедряются всегда, а проверка доставки в кабинете получает `waiting` и релиз не блокирует (`analytics.appmetrica_status = "waiting"`). Adjust при этом должен быть полным сам по себе. Серверную валидацию покупок ведёт Multicast ([server-iap.md](server-iap.md)); **Adjust Purchase Verification не подключай**.

## Принцип: зеркало аналитического слоя

В игре уже есть один аналитический слой (в Piggy Payday `GameTelemetry.Emit` → `Send(name, json)`). Adjust подключается **рядом с AppMetrica и Meta в том же `Send`** и повторяет события под теми же именами:

- Каждое имя события игры получает свой Adjust event token. Параметры события уходят как callback-параметры (плоские строки).
- Список токенов лежит в конфиге игры (`adjustEvents: [{name, token}]`). **Событие без токена не отправляется**, поэтому объём регулируется конфигом, без правки кода.
- Adjust-приёмник независим от AppMetrica: отправка идёт, даже если AppMetrica не активна, а `Send` не зависает на очереди.
- Не зеркалируются: `ad_revenue` (доход рекламы идёт через ILRD, `AdjustAdRevenue` из настоящего MAX revenue callback), `playtime_seconds_event` (тот же минутный тик). `playtime_event` отправляется как `time_spent`.
- Денежные покупки: событие `purchase` с revenue, ISO currency и deduplication id (id транзакции) остаётся отдельным токеном и отдельным кодом, в `adjustEvents` его не дублируй.

Ограничения Adjust, о которых нужно знать ([Add events](https://help.adjust.com/en/article/add-events)): callback-параметры попадают в raw data и callbacks, но не в Datascape, поэтому в дашборде видно число событий, а разрез по параметру (например по номеру стола) только в raw data. Всё, что нужно разрезать в дашборде, должно быть отдельным именем события. Не создавай события на то, что SDK измеряет сам: установки, клики, сессии. Серверные callbacks для частых событий (`ad_requested`, `quota_progress`, `time_spent`) не настраивай.

## Набор для новой игры

Берётся из аналитического слоя игры; если слоя нет, создай его и заведи минимум по группам. Piggy Payday: 50 событий, полный список в `assets/events.example.json`.

| Группа | События |
| --- | --- |
| Жизненный цикл | `game_loaded`, `session_started`, `user_consent`, `time_spent` (одно событие на каждую полную минуту foreground-времени; число событий = минуты игры) |
| Реклама | `ad_requested`, `ad_loaded`, `ad_load_failed`, `ad_displayed`, `ad_display_failed`, `ad_show_failed`, `ad_clicked`, `ad_hidden`, `ad_completed`, `ad_incomplete`, `ad_reward_saved` (награда выдана и сохранена), `ad_revenue_unavailable`, а также событие самой награды игры (`energy_refilled`, `rewarded_*`) |
| Прогресс | обучение (`tutorial_started`, `tutorial_completed`, шаги), квесты, уровни/столы/зоны, забеги, боссы, квоты, выбор снаряжения |
| Экономика | `currency_spent`, `currency_earned`, события покупок за игровую валюту и улучшений |
| Покупки | `iap_attempt`, `iap_delivery`, `iap_deferred`, `iap_error`, `iap_restore` (и отдельное `purchase` с revenue) |

`ad_completed` — сам reward callback SDK, `ad_reward_saved` — награда выдана и записана в durable save: проверяй их отдельно, закрытие без награды даёт `ad_incomplete`. `time_spent` даёт около 60 событий в час на играющего: Adjust пишет, что число событий не ограничено, но условия тарифа уточни до релиза; если объём станет проблемой, оставь пустым токен у самых частых событий или шли `time_spent` раз в 2–5 минут.

## Шаблон обёртки в коде

[assets/AdjustEventMirror.cs.txt](../assets/AdjustEventMirror.cs.txt) — шаблон для Unity C# (адаптирован из Piggy Payday, отдельно не компилировался): `Configure(tokens)`, `Send(name, data)` с переименованием и пропусками, deduplication id из `event_id`, не более 24 callback-параметров. Если аналитический слой уже есть, не копируй шаблон, а повтори его логику внутри существующего `Send`: добавь `adjustEvents` в конфиг игры и учти Adjust как самостоятельный приёмник в «готовности измерения». Отсутствие токенов не должно быть блокером релиза.

## Как завести event tokens быстро

Операции каталога панели события не создают ([superleo-adjust.md](superleo-adjust.md)). Маршруты, от быстрого:

1. **Импорт в кабинете (одно действие).** Adjust AppView → All apps → приложение → Events → **Import events**: из приложения-образца студии с этим набором либо из CSV (колонки и формат возьми из списка, скачанного с образца). Токены Adjust выдаёт сам. Делается через agentb ([browser.md](browser.md)) с сессией Adjust.
2. **Add event циклом (проверено, основной путь).** Events → Add event, по одному на имя из конфига; готовые сценарии agentb `scenarios/adjust_events_create.js` и `scenarios/adjust_events_read.js` (50 событий примерно за 3 минуты). *Unique* включай только для событий «один раз на устройство», по умолчанию выключено. Импорт из образца подходит, только если в образце ровно ваши имена, иначе в приложение попадёт чужой набор.
3. **App Automation API.** `POST https://automate.adjust.com/app-automation/events` ([документация](https://dev.adjust.com/en/api/app-automation-api/events-endpoint/)) создаёт события списком для app token, совпадение по имени. Нужен API token Adjust из профиля кабинета; он хранится только в секретном хранилище, в чат не вставляется. Доступность зависит от тарифа.
4. Шаблон при создании приложения (`skipTemplate:false` в `adjust_create_app`) копирует события сервера, только для нового приложения.

Таблицу Events читай `agentb run scenarios/adjust_events_read.js` (имя, токен) в `adjust-tokens.json` и впиши токены командой `scripts/fill_adjust_tokens.py` ([workflow.md](workflow.md#токены-adjust-внутри-шага-6-до-финальной-сборки)). Токены не секретны, но принадлежат приложению этой игры; они лежат в `adjustEvents` игрового конфига рядом с `adjustAppToken` и `adjustPurchaseToken`. Конфиг попадает в билд, поэтому события заработают только в сборке, сделанной после записи токенов. Нет доступа к кабинету → токены `waiting_for_access`, релиз продолжается: события с пустым токеном не уходят.

## Проверка

Для каждого события с токеном: срабатывание в реальном действии игры, отправка (лог `MC`), получение в кабинете или export Adjust для этого app token в production-окружении. `ad_reward_saved` проверь отдельно от `ad_displayed`. Callback-параметры проверяй в raw data, не в Datascape. Отправка без подтверждённого получения остаётся `pending`, не `verified`.
