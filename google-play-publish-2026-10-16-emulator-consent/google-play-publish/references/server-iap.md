# Серверная валидация покупок Multicast

Этот этап обязателен, когда игра студии продаёт денежные товары. Три разных результата: товары магазина; регистрация game/products в Multicast; вызов runtime validation из игры. `iap_import`/`iap_save_product` закрывают регистрацию, но не подключают покупку в клиенте автоматически.

## Регистрация через SuperLeo

1. Прочитай `iap_status`, `iap_games`, `iap_game_config`, `iap_products` по [superleo-iap.md](superleo-iap.md). Сопоставь собственные Android/iOS IDs с проектом, включая PlayerSettings, wrapper или экспорт Godot. `gameId` — серверный ключ, не Play numeric app ID. Переиспользуй точное совпадение.
2. Если игра отсутствует, выполни `iap_save_game` с реальными IDs из проекта. Backend требует оба platform ID. Не считай Android-only ошибку поводом пропустить backend или выдумать iOS ID. Если в проекте уже есть собственный iOS bundle ID, используй его; если нет — запроси только этот недостающий идентификатор.
3. Для уже созданных Play SKU используй backend-only `iap_save_product`, сохраняя принятую таблицу: `productSku`, `productType` (0 consumable / 1 non-consumable), `googlePlaySku`, существующие другие store mappings, `rewards[{sku,quantity}]`. Не меняй цены/активацию и не повторяй полный import ради регистрации на сервере. При паузе публикации эти backend действия и локальная интеграция могут продолжаться в разрешённом scope; Google store writes остаются на паузе.
4. Перечитай game config и каждый товар. Затем проверь каталог через **authenticated player API**, а не только admin API. Награды и типы должны совпадать с игрой. Не включай optional backend Adjust reporting, если тот же денежный источник уже отправляется клиентом, без решения о дедупликации.

## Подтверждённый runtime контракт

Снимок проверен 12.10.2026 по `Z:\Repos\devour\Devour.Unity\Library\PackageCache\com.multicastgames.backend@…\Runtime`: `BackendApi.cs`, `BackendPayments.cs`, `BackendPaymentsService.cs`, `BackendAuthGuest.cs`, `BackendRewardInboxService.cs`. Версии: Backend **1.6.2**, Payments **1.5.0**, Unity IAP adapter **1.0.7**. Backend production URL: `https://backend.api-multicastgames.org`.

Предпочитай совместимый установленный SDK. Если его framework dependencies не подходят текущему проекту, реализуй ограниченный мост по фактически проверенному контракту. Registry `https://npm.api-multicastgames.org` приватный: используй уже предоставленный UPM доступ, PackageCache или эталон на ПК. Отсутствие Linux-копии Windows-референса не означает отсутствия SDK. Для другого deployment/версии перечитай контракт; не выдумывай endpoints.

| Действие | Runtime endpoint и payload/response |
| --- | --- |
| Гостевая сессия | `POST /api/auth/login/guest`, `{deviceId,gameId,createProfile:true}` → `{credentials:{userId,authToken,refreshToken}}` |
| Refresh | `POST /api/auth/refresh`, `{refreshToken,gameId}` → `{userId,authToken}` |
| Каталог | `GET /api/products/get` → `{products:[{productSku,googlePlaySku,productType,rewards}]}`; runtime type — `Consumable` / `NonConsumable` |
| Перед открытием billing | `POST /api/orders/create`, `{productSku}` → `{orderId,rewardItems}` |
| Google Play validation | `POST /api/payment/google-play`, `{receipt: originalSignedJson}` → `{paymentId,rewardId,environment}` |
| Выданные сервером награды | `POST /api/rewards/get-all-unclaimed` → `{rewards:[{rewardId,reason,source,items}]}` |
| Claim | `POST /api/rewards/set-claimed`, `{rewardId}` |

Authenticated endpoints используют **игровой** `Authorization: Bearer authToken`. Гостевой device ID — устойчивый случайный UUID, не рекламный ID. Refresh хранится приватно на устройстве; management MCP key, OAuth/service-account JSON и приватные Play credentials в клиент не входят. При timeout/5xx refresh не создавай нового игрока: сессия могла сохраниться. При 401 обнови сессию один раз; постоянный отказ не превращается в бесконечный цикл.

Google receipt в SDK: wrapper `Store:"GooglePlay"`, `Payload` содержит `json`/`signature`; серверу передаётся **исходная строка `Payload.json`**. Локальная RSA-проверка дополнительно связывает package, SKU, purchase state, quantity и token. Не пересобирай чек из присланного клиентом SKU/token и не отправляй `rewardsOverride` для обычных согласованных товаров. Dev/fake-store payment endpoint не используется в Android release. При `environment:"sandbox"` награда может тестироваться, но денежный revenue исключается.

## Google Play checkout до сборки

Проверяй native Billing, Unity IAP и любой собственный bridge по API фактически установленной Billing Library. Каталог ACTIVE и успешный products query не доказывают, что checkout собран корректно.

- `launchBillingFlow` выполняется на UI thread, с актуальными `ProductDetails`, правильным типом/SKU, выбранным доступным purchase option/offer token и реальными store price/currency. Не используй долгоживущий cache как единственный источник checkout: stale details могут приводить к отказу. Проверь пустые/unfetched товары и controlled refresh.
- Если используется `setObfuscatedProfileId`, Google требует также `setObfuscatedAccountId`. Account ID — стабильный непрозрачный идентификатор игрока, profile ID — профиля; orderId не заменяет постоянную identity. Обязательность, длину, формат и mapping order→receipt сверяй с реальным backend/адаптером. Не меняй ассоциацию заказа хешированием наугад и не передавай PII/auth token.
- Различай результат запуска, асинхронный store callback, настоящий PENDING receipt и ошибку/таймаут server validation. До подтверждённого PURCHASED и серверной валидации нет гранта/confirmation. Ошибка старта/отказ без транзакции должны позволять повторить покупку; неопределённый результат сначала сверяется с ownership, а не стирается.
- Проверь случай «ошибка callback → успешный пустой queryPurchases → повтор того же SKU», restart и поздний pending→purchased. Не оставляй бессрочный pending только из-за сохранённого checkout intent; при сетевой ошибке ownership query не удаляй реальные pending/delivery записи. Снятие блокировки не требует удаления сохранения игрока.
- Safe diagnostics сохраняет этап (launch/callback/query/validation/claim/confirmation), код и безопасную причину; UI отличает отмену, отказ и ожидание. Реальная оплата не подписана «демо». И код возврата, и watchdog возвращают pause/input и допускают recovery; watchdog не объявляет транзакцию отменённой без доказательства.
- Минимальная локальная проверка проходит настоящий adapter/pipeline: параметры account/profile и offer; отмена; отказ launch; отказ callback без покупки; PENDING→PURCHASED; пустой ownership; ошибка ownership; повтор/restart/save failure. Stubs должны проверять используемые SDK-инварианты: fixture, принимающий любой builder, не доказывает правильность параметров. Сигнатуры сверяются с настоящим SDK, магазин — реальной license-test покупкой.

Internal и production поддерживают IAP; трек не является объяснением ошибки оплаты. License testing даёт тестовые способы оплаты, а участие в internal само по себе не делает покупку бесплатной. Источник установки/подпись полезны для диагностики sideload, но не отменяют поиск дефекта checkout при установке из Play.

Официальные контракты: [Billing integration](https://developer.android.com/google/play/billing/integrate), [BillingFlowParams.Builder](https://developer.android.com/reference/com/android/billingclient/api/BillingFlowParams.Builder), [Billing testing](https://developer.android.com/google/play/billing/test). Перепроверь их при изменении версии SDK.

## Доставка и восстановление

- Новый платёж: server order → platform billing → pending receipt → authenticated server validation → точная серверная награда → durable delivery → claim/Google confirmation. Успешный HTTP без payment/reward ID или неправильная награда не являются одобрением.
- Сохраняй награду, устойчивый receipt/token ledger и server payment/reward IDs **одной транзакцией сохранения**. В standalone client сохраняй их до claim/consume/acknowledge, чтобы сбой между этими шагами не терял награду. SDK с полноценным backend inbox может подтверждать магазин до клиентской выдачи только если награда остаётся надёжно доступной через серверный inbox.
- Повтор использует тот же payment/reward и сохранённый ledger. Не выдавай consumable повторно при другом серверном ID/игроке, отсутствии unclaimed reward и отсутствии своей durable delivery записи. Claim/confirmation retry не создаёт новую продажу. Legacy client-only ledger мигрирует без повторной выдачи уже полученной награды.
- Серверный отказ, malformed response, timeout/offline или disk failure оставляют покупку pending, без неподтверждённой выдачи и без confirmation. Retry ограничен backoff. Не подставляй локальный RSA как offline fallback.
- Restore non-consumable требует свежей серверной проверки фактического receipt, без replay consumables и revenue. Отложенная покупка не выдаётся. При смене store account/уничтожении объекта старые async callbacks не подтверждают чужую текущую покупку.
- Guest identity и purchase validation сами по себе не являются cloud save, cross-device account linking или RTDN refund handling. Эти возможности отмечай отдельно по реально реализованному серверному контракту.

## Проверка и отчёт

Сохраняй отдельно: admin registration/readback; runtime authentication/catalog; local pipeline tests; фактическую license-test покупку на установленном билде. Локальный transport fixture проверяет reject/timeout/mismatch/save failure/replay/claim failure/account change/restore/sandbox через настоящий delivery pipeline. Проверка каталога живого сервера и negative receipt подтверждает доступность и rejection, а не успешную покупку. Не отправляй искусственные успешные платежи/revenue в live сервер.

Для device verification требуются payment/reward IDs и сохранённая выдача, затем Google confirmation, повторный запуск без дубля и отсутствие нового revenue при restore. Нет устройства — `waiting_for_device`; код и локальные тесты могут быть завершены, но device gate не закрывается. При публикационном hold не загружай новый билд даже в internal. Никакие токены, чеки или секреты не входят в переносимый архив скилла.
