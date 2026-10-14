# Самостоятельный референс Devour

Изучен **2026-10-08**, `Z:\Repos\devour\Devour.Unity` на Windows-ПК. HEAD: `6d9fc23b1e61f0d5203a8dc70cce20467a6e8147`; рабочее дерево могло содержать изменения. Снимок описывает наблюдавшийся код, а не runtime-проверку Devour. Ключи/ID приложения в пакет не включены.

Devour служит образцом сразу на трёх уровнях: состав официальных SDK/адаптеров, логика их работы и внедрение в живую игру. При переносе сравнивай bootstrap, маршрутизацию событий, revenue/reward callbacks, платежный flow и связь с состоянием игрока. Выбирай готовый SDK или свою интеграцию по ядру текущего проекта; внешний вид/названия классов и полное копирование зависимостей Devour не обязательны.

## Источники и версии

| Компонент | Наблюдавшийся источник/версия |
| --- | --- |
| AppMetrica Unity | manifest/lock: `io.appmetrica.analytics`, официальный Git, tag `v6.7.0` |
| MAX Unity | `Assets/MaxSdk/Scripts/MaxSdk.cs`: `8.4.2` |
| MAX Android | `Assets/MaxSdk/AppLovin/Editor/Dependencies.xml`: `13.4.0` |
| Adjust Android | `Assets/Adjust/Native/Editor/Dependencies.xml`: `5.0.0` |
| Facebook Unity wrapper | AAR `facebook-android-wrapper-18.0.0.aar`; имя не подтверждает версии всех транзитивных компонентов |
| Unity IAP | `com.unity.purchasing`: `5.2.1` |
| Multicast payments | `com.multicastgames.payments`: `1.5.0` |
| Multicast → Unity IAP | `com.multicastgames.payments.unity-iap`: `1.0.7` |
| Multicast → AppMetrica IAP | `com.multicastgames.payments.appmetrica`: `1.0.9` |

Registry Multicast: `https://npm.api-multicastgames.org`, scope `com.multicastgames`. Используй при доступе/совместимости; отсутствие доступа не позволяет заменить серверную обработку фиктивной. Обёртки нового проекта не обязаны зависеть от GameContext, Morpeh, UniMob, Odin или Firebase.

В MAX референса есть Android adapters: Bigo Ads, Pangle/ByteDance, Meta Audience Network, Fyber/DT Exchange, Google, Google Ad Manager, ironSource, Mintegral, Unity Ads, Vungle/Liftoff. Это инвентарь, **не обязательный список** для каждой игры; набор выбирается в панели. Не ставь второй mediation SDK поверх MAX.

## AppMetrica: слой событий

Пути относительно `Devour.Unity`:

- `Assets/Codebase/Analytics/BaseAnalyticsEvent.cs`: событие с `Name`.
- `Assets/Codebase/Analytics/IAnalyticsWrapper.cs`: `Send<TEvent>`.
- `Assets/Codebase/Integrations/AppMetrica/AppMetricaAnalyticsWrapper.cs`: Newtonsoft JSON → `AppMetrica.ReportEvent(eventData.Name, jsonData)`.
- `Assets/Codebase/Analytics/UserContextEventBase.cs`: `days_since_install`, `playtime_minutes`, `last_opened_island` из состояния игрока.
- `Assets/Codebase/Integrations/AppMetrica/AppMetricaActivator.cs`: before-scene activation, first-launch marker, crash reporting, location tracking выключен, `RevenueAutoTrackingEnabled=false` для ручного IAP revenue.

Переноси смысл слоя и форму JSON. `Name` — имя отправки, не поле payload. В новом проекте `last_opened_island` свяжи с миром/зоной или документируй замену; не выдумывай острова. Verbose logs, flush каждого события и безусловную передачу данных не переносить в release без проверки режима/согласия.

| Событие | Проверенная форма |
| --- | --- |
| `game_loaded` | `{}` при готовности игры |
| `user_consent` | `gdpr_applies`, `has_user_consent`, `do_not_sell`, `att_authorized`, из реального состояния; Android не объявляй iOS ATT-authorized |
| `rewarded` | Одно динамическое поле: `{"loaded":""}`, `{"displayed":"placement"}`, `{"watched":"placement"}` |
| `rewarded_paid` | Поля пользовательского контекста, на revenue callback |
| `ad_revenue` | Контекст + `total_impressions`, `total_revenue`, `total_ecpm`, `revenue`, `density`, `ad_type` |
| `banner` | Динамическое поле состояния; класс не доказывает подключение |
| `banner_revenue` | Контекст + accumulated impressions/revenue/eCPM и revenue показа; класс не доказывает подключение |
| `purchase` | Контекст + `key`, `transaction_id`, `product_id` после доставки |
| `playtime_event` | `playTime` (минуты) |
| `playtime_seconds_event` | `seconds` |

В `rewarded` найдены `loaded`, `load_failed`, `displayed`, `display_failed`, `clicked`, `hidden`, `watched`, `total`, `paid`. `watched` — reward callback, `paid` — revenue callback; они не взаимозаменяемы. Примеры формы: `../assets/events.example.json`. Дополнительные события Devour покрывают progression, shop, quests, offers, active/AFK playtime; переносится подход, а не вся тематическая номенклатура.

## Реклама и revenue

`Assets/Codebase/Integrations/Ads/AdsInitialize.cs` подписывается на **rewarded** callbacks. Поиск `Assets/Codebase` не обнаружил `MaxSdkCallbacks.Interstitial`, `MaxSdkCallbacks.Banner`, `ShowInterstitial`, `CreateBanner`, `ShowBanner`. Rewarded и включённые optional форматы новой игры проверяются отдельно; классы BannerEvent/BannerRevenueEvent не являются готовой реализацией.

На rewarded revenue callback код:

1. Увеличивает impressions, суммирует `adInfo.Revenue`, вычисляет `total_ecpm = total_revenue / total_impressions * 1000`, отправляет custom `ad_revenue`.
2. Передаёт Adjust `AdjustAdRevenue("applovin_max_sdk")`, revenue/`USD`, format/network/placement/creative метаданные; дополнительно отправляет event token этой игры.
3. Вызывает `AppMetrica.ReportAdRevenue(new AdRevenue(...))` с ID блока, сетью/placement.
4. Дополнительно сообщает доход Firebase, который не входит в обязательный набор студии.

Для новой игры используй **ID фактического callback**, соответствующий format/placement. Не подставляй rewarded ID для баннера/интера и creative ID вместо placement ID. Заполняй штатные Adjust поля network/unit/placement выбранной API-версии. Event tokens получай для новой игры.

AppMetrica Unity 6.5+ автоматически собирает ad revenue ряда сетей, включая AppLovin. `RevenueAutoTrackingEnabled=false` относится к **покупкам** и не доказывает отключение **ad revenue**. Ручной вызов Devour рядом с автосбором — предмет проверки дублей. Выбери один источник специализированного дохода; custom `ad_revenue` можно сохранить. Источники: [AppMetrica examples](https://appmetrica.yandex.com/docs/en/sdk/unity/analytics/unity-operations), [официальный plugin](https://github.com/appmetrica/appmetrica-unity-plugin).

Нативные callbacks не выпускают исключения через native boundary. Devour использует try/catch в revenue handler и Unity main-thread MAX events. В новом проекте обеспечь подходящий main-thread dispatch, отсутствие двойных подписок и независимость analytics sinks: ошибка одного не отменяет награду или остальные отправки.

## Consent, Meta и Adjust

`Assets/Codebase/UserConsent/MaxUserConsentService.cs` ждёт MAX initialization/consent flow и отправляет `user_consent`, но Android-ветка принудительно задаёт `SetHasUserConsent(true)`/`SetDoNotSell(false)`. `FacebookInitializer.cs` вызывает FB.Init/ActivateApp и безусловно включает advertiser tracking/ID collection. В `AdjustInitializer.cs` часть consent-настроек закомментирована. Firebase тоже содержит безусловный grant.

**Не воспроизводи принудительные grants.** Сохрани последовательность инициализации, применяя реальный выбор пользователя и поддерживаемые API. Проверь начальное согласие, отказ и изменение. Meta App Events не заменяется mediation adapter.

## Покупки

- `Assets/Codebase/In-apps/IAPProductsConfig.cs`: ProductKey/inAppValue, не полная таблица store IDs/цен.
- `Assets/Codebase/In-apps/UnityIAPManager.cs`: lookup конфигурации, не завершённый store flow.
- `Assets/Codebase/Services/Purchases/PurchaseProcessingService.cs`: IPaymentsService.Purchase, продолжение interrupted payments, reward через backend/mail, custom purchase; Firebase revenue пропускается для sandbox.
- Дополнительно 12.10.2026 изучены runtime sources Backend 1.6.2 / Payments 1.5.0 из PackageCache: server guest auth, product/order API, `BackendPayments.Approve` для оригинального Google receipt JSON, payment/reward IDs, sandbox и reward inbox. Проверенный контракт и перенос в другие движки — [server-iap.md](server-iap.md). Фактический AppMetrica adapter/revenue delivery проверяется отдельно.

Не копируй валютные наборы, VIP points, no-ads правила, passes/SKU без принятого каталога. Пакет не доказывает проверку/confirmation транзакции. Денежный IAP revenue отличается от custom purchase: нужны фактическая цена, валюта и проверенная транзакция; restore/replay не создают новую продажу.

## Граница переноса и готовности

Функциональный контракт всех движков — [sdk-validation.md](sdk-validation.md), сценарии evidence — [verification.md](verification.md). Этот снимок не подтверждает успешную activation AppMetrica, sessions/attribution/ILRD Adjust, Meta Init/ActivateApp/серверное получение или MAX init/native privacy/result показа новой игры. Проверяй их отдельно на её установленном артефакте и в собственных кабинетах. Client token/key hashes/IDs Devour не переносить; Meta App Events и Audience Network не взаимозаменяемы.

Rewarded обязателен; interstitial/banner необязательны и при отключении пользователем не инициализируют placements, не load/show и не блокируют релиз. `watched` относится только к реальному reward callback; close без reward — отдельное наблюдение. Долгий показ/background/resume/late callback требуют устойчивой correlation и атомарного сохранения награды, а не переноса debug grants или таймера успеха. SDK inventory и найденные классы сами по себе не закрывают эти gates.
