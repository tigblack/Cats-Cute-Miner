# Cats Cute Miner → Google Play

Подготовка начата 14.10.2026 по `google-play-publish/SKILL.md`.

Перед внешними изменениями, созданием продуктов, загрузкой AAB и выпуском требуется подтверждение пользователя. Production пока не поручен. Основной трек — internal.

## Найдено в проекте

- Unity 6000.3.11f1. На ПК Editor находится в `C:\Unity\Editor\Unity.exe`.
- Текущий package — `com.catminers.prototype`; постоянный идентификатор ожидает выбора пользователя. Предложен `com.multicastgames.catscuteminer`.
- Версия проекта 0.1.0 (1); окончательная версия определяется после чтения всех треков Console.
- Каталог `store-3`: шесть денежных расходуемых пакетов кристаллов, без бонуса первой покупки. Таблица для согласования — `iap-catalog.proposed.json`.
- В каталоге включён `TestPurchases`. Это препятствие для store-релиза: симуляция не должна попадать в Android release. Работающие WebGL/playable сценарии сохраняются отдельно.
- SDK AppMetrica/Adjust/Meta/MAX и реальный Billing не найдены в текущем manifest. Наличие встроенного `com.unity.modules.unityanalytics` не подтверждает ни один из обязательных сервисов.
- Панель доступна под предоставленным аккаунтом; чтения AppMetrica, Adjust, Meta, MAX и Play lookup успешны. Создание и runtime-доставка ещё не проверены.

## Внедрение

Официальные SDK подключаются к существующему игровому bootstrap. События прогрессии и экономики направляются в AppMetrica; Adjust получает session/attribution и MAX ILRD; Meta получает согласованный минимальный набор App Events. Реальное согласие управляет сбором; отсутствие настройки одного сервиса не выключает остальные.

Rewarded реализуется через MAX. Начисление происходит только после callback награды, с сохранением и защитой от повторов. Остальные форматы требуют существующих продуктовых правил. Стандартные сети скилла: AdMob, Unity Ads, Mintegral, Pangle; создание блоков и настройка mediation фиксируются отдельно.

Unity IAP 5.2.1 проверен в официальном registry: в changelog 5.2.0 указан Google Billing 8.3.0. Денежные покупки требуют Multicast: guest auth → order → реальный receipt → server validation → durable grant/ledger → claim и confirmation. Приватный registry Multicast вернул 401; допустим ограниченный runtime bridge по проверенному `references/server-iap.md`. Доступ к registry не запрашивается как обязательное условие, если этот bridge покрывает контракт.

Для SDK конфигурации используются исключительно собственные приложения этой игры. Ключи Devour, management credentials и фиктивные токены не переносятся в клиент.

## Проверки

Требования текущего Android релиза: target API 36+, Billing 8+, ARM64, 16 KB native compatibility. Источники:

- https://support.google.com/googleplay/android-developer/answer/11926878
- https://developer.android.com/google/play/billing/deprecation-faq
- https://developer.android.com/guide/practices/page-sizes

Компиляция, собственная конфигурация, runtime инициализация, фактическое применение, устройство и кабинет имеют отдельные gates. До реальных результатов они не считаются завершёнными. Полный перечень — `manifests/` и `profile.json`.
