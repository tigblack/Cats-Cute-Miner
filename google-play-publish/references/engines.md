# Реализация по движку

Установи, какой проект/обёртка формирует AAB. Веб-страница/WebGL build сами по себе в Play не публикуются. Общий контракт и четыре обязательных сервиса сохраняются.

## Самостоятельный выбор интеграции

Сначала изучи ядро проекта: bootstrap, DI/service registry, события и сохранения, async/main-thread модель, платформенные мосты и существующие SDK. Определи фактические пробелы, затем выбери реализацию самостоятельно:

- **SDK уже есть и совместим:** переиспользуй его, исправь настройки/callbacks/события и внедрение в игру. Особенно в Unity наличие SDK часто опережает завершённую интеграцию.
- **Есть подходящий официальный пакет:** установи его и подключи к существующим сервисам игры, ориентируясь на состав, порядок инициализации и callbacks Devour.
- **Готовая обёртка не подходит или её нет:** напиши свой интеграционный слой, engine plugin/native bridge либо транспорт по официальному поддерживаемому API. Используй официальные native SDK там, где они обеспечивают attribution, Android lifecycle, mediation или billing, и проверь весь функциональный контракт.

Оцени совместимость движка/Android, полноту событий и revenue, работу consent, награды/pending/restore, поддержку release build и стоимость сопровождения. Выбор пакета или собственного кода — техническое решение агента; отдельного согласования для обычной обёртки не требуется. Изменение продукта, ядра или Android packaging, влияющее на UX, требует конкретного решения пользователя. Не переписывай работающую инфраструктуру ради сходства классов с Devour и не обновляй все SDK только ради номера версии. Итог — реально работающие сервисы в живой игре с проверкой на устройстве; одинаковые имена файлов не являются целью.

## Unity

Проверь ProjectVersion, manifest/lock, Assets/Plugins/Android, Gradle templates, EDM4U XML, SDK assets, bootstrap scene и настройки компонентов. SDK/scene/player settings меняй через редактор/существующий build script, не вручную scene/prefab YAML. Доступные навыки Unity CLI/package management/IAP могут помочь конкретным механикам, но не являются обязательными зависимостями этого скилла.

AppMetrica подключается UPM plugin; Adjust/Meta/MAX могут находиться в Assets/native plugins. Сначала проверь уже имеющиеся компоненты, бинарники на build-машине и dependency resolution. Недостающие SDK получай официально, сверяя совместимость и реализацию с актуальным Devour; prefab с токенами референса не переносить.

Сохрани bootstrap и существующие слои. Инициализация/callback subscriptions однократны, состояние Unity меняется main thread. Проверь IL2CPP/linker/JSON callbacks в release. Наличие com.unity.ads не заменяет MAX и не требует установки LevelPlay.

IAP 4.x/5.x имеют разные API/lifecycles: мигрируй осознанно, не вставляй 5.x callbacks в 4.x. При Multicast payments проследи purchase → verification/delivery → confirmation/recovery и analytics adapter. Сборка: signed release AAB, ARM64, штатный upload key, версии из Play, разрешённые native зависимости.

## Godot

Проверь project.godot/export presets/addons/Android custom build/Gradle/plugin manifests и версию Godot. Выбирай совместимые Android plugins всех четырёх сервисов; при отсутствии обёртки напиши минимальный Kotlin/Java plugin к официальным native SDK. Unity assemblies/.unitypackage не подходят.

Мост передаёт JSON/events, consent/init completion, ad lifecycle/revenue/reward, billing states/errors. Соблюдай Android lifecycle, не удваивай instances при resume/повороте, callbacks направляй в безопасный Godot main-thread/deferred dispatch. ID — из новой конфигурации.

Покупки реализует настоящий Google Play Billing plugin/native bridge: pending/purchased, token verification, acknowledge/consume, query purchases, durable выдача. Собери signed AAB с разрешёнными Gradle dependencies; успешная компиляция не подтверждает доход/награды.

## Веб → Android

Найди Capacitor/Cordova/native WebView/TWA или другую оболочку. Сохрани подходящую существующую. Без wrapper подготовь вариант packaging; выбор уточни, если он влияет на архитектуру/UX, параллельно делая аудит/контракт.

Нативные SDK работают в **Android host**, игра вызывает ограниченный мост. Mobile attribution не заменяется JS pixel, web page не подтверждает покупки сама. Используй совместимые plugins либо Android bridge. Для TWA подтверди возможности всех SDK и связи с игрой; иначе опиши необходимое изменение wrapper. Не обещай native ads из чистой удалённой страницы.

Bridge валидирует payload/происхождение страницы, не отдаёт server credentials, проверяет реальные callbacks. Reload/repeat/resume не удваивают покупку/награду благодаря сохранённой транзакции. Проверь back button/background/offline/safe area/ad close. Billing встроен в host; тестовое начисление не является покупкой.

## Официальные точки входа

- [AppMetrica Unity](https://github.com/appmetrica/appmetrica-unity-plugin) / [Android](https://github.com/appmetrica/appmetrica-sdk-android).
- [Adjust](https://dev.adjust.com/en/sdk/).
- [MAX networks](https://support.applovin.com/en/max/unity/preparing-mediated-networks) / [rewarded callbacks](https://developers.applovin.com/en/max/unity/ad-formats/rewarded-ads/).
- [Meta Unity](https://github.com/facebook/facebook-sdk-for-unity) / [Android](https://github.com/facebook/facebook-android-sdk).
- [Play Billing](https://developer.android.com/google/play/billing/integrate).

Сигнатуры выбирай по конкретному официальному API/исходнику/версии, а не по памяти.
