# Проверка результата

Фиксируй build versionCode, сценарий, устройство/аккаунт, результат и доказательство. Source search находит места, compile проверяет API, успешный SDK init callback — инициализацию, SDK log — наблюдение/попытку отправки, dashboard/export — получение. Установленные SDK/адаптеры и App IDs не доказывают следующие этапы. Проверяй изменённые lifecycles/деньги/награды/Android release, а не все возможные тесты движка.

Применяй также [sdk-validation.md](sdk-validation.md). Compile выполняется с активными SDK/Android defines; тесты прототипа сравнивай с настоящим bootstrap/save/UI, а не объявляй ими готовность всей игры. Отдельно проверь, что revenue каждого включённого формата не вырезан и денежный reporter не записывает необратимый `sent` до неуспешной отправки. Отсутствие дублей и доставка — разные проверяемые свойства.

## SDK и consent

Перед runtime выполни provider-by-provider и cross-SDK проходку [sdk-interactions.md](sdk-interactions.md). Итог содержит матрицу всех применимых SDK и связей с code/config/compile/init/use/delivery, конкретными failed/pending и evidence; список установленных пакетов не является аудитом интеграции.

- Clean install/relaunch: собственные package/app IDs, реальная AppMetrica activation, Adjust init/session, Meta Init → ActivateApp и успешный MAX init с native privacy result. Нет двойных bootstrap subscriptions; first launch не повторяется. Сохрани отдельный результат каждого SDK, а не общее «SDK установлены».
- Стартовое (`game_loaded`, если принято контрактом) и игровое событие отправлены и достигли AppMetrica с ожидаемыми JSON/context; для ad/IAP revenue доказан один денежный источник на получателя и отдельно доставка.
- Adjust: production config финального билда, реальные session/attribution и ILRD в правильном приложении/окружении. Meta: собственная регистрация, package/activity/key hashes сертификата установленного билда, client token/native wiring, consent-aware activation и выбранные события получены сервером/Test Events/доступным App Events отчётом. Audience Network adapter этого не подтверждает. Недоступная проверка остаётся pending с конкретным следующим шагом.
- Consent: согласие/отказ/изменение применяются SDK и сохраняются после restart; без принудительных grants Devour.
- Offline/resume/reenter не подвешивают игру и не удваивают callbacks. Analytics failure не ломает игру.

## MAX

Проверяй rewarded и только включённые optional форматы отдельно на явно настроенных test devices/Mediation Debugger. Для отключённых interstitial/banner проверь отсутствие placement init/create/load/retry/show/autorefresh; их IDs и QA не требуются и не блокируют readiness. MAX SDK продолжает обслуживать rewarded. Не кликай реальные объявления ради QA.

| Сценарий | Результат |
| --- | --- |
| MAX init/privacy → rewarded ready/load/show/reward/close | Успешные init/native privacy result; одна атомарно сохранённая награда только от реального reward callback, правильные unit/placement/correlation, подтверждённые события |
| Долгий показ/background/resume/поздний reward после close | Pause/input/audio восстанавливаются; context не теряется и не переносится на новый показ, поздний реальный reward проходит тот же durable pipeline ровно один раз |
| No fill/load fail/display fail/close без reward | Награды и successful-view события нет, реальное закрытие фиксируется отдельно; UI выходит из loading, ограниченный retry |
| Duplicate reward/save failure/restart after grant | Повторной выдачи нет, неуспешный save остаётся pending; ledger проверен с настоящим wallet/save игры |
| Включённый interstitial pause/cooldown/resume | Правильный блок, без второго показа/награды, gameplay продолжается |
| Включённый banner show/hide/rotation/safe area | Нет перекрытия управления/дублирования view, ресурсы освобождены |
| Revenue каждого включённого формата | Реальная сумма/валюта/сеть/unit/placement, одна денежная запись на получателя |
| Ошибка одного analytics sink | Остальные sinks/награды продолжаются |

Readable debug console/logs сохраняют реальные init/consent/load/show/reward/close/error/correlation и состояние durable save без tokens/receipts/личных данных. Debug UI не переключает молча consent/SDK environment и не выдаёт fake reward; это диагностика, не доказательство получения сервером.

Test ads могут не приносить реальный revenue callback. Маршрутизацию можно проверить изолированным replay, **без синтетического дохода в production**; реальное получение дохода кабинетом пометь ожидающим. Показ test ad не доказывает end-to-end ILRD.

## IAP

Сначала проверь магазин в обычном интерфейсе актуальной установленной игры: видимая точка входа → отдельная панель → каждый согласованный SKU → закрытие и восстановление паузы/ввода. Сохрани кадры входа и всех товаров, versionCode/SHA-256 проверяемого артефакта и состояния недоступности/Owned. Каталог в настройках, SDK и тесты ledger не закрывают этот gate. Продукты нельзя скрывать из панели только потому, что store connection или валидация ещё недоступны; показывай статус и отключай покупку.

На Play internal с license tester проверь availability/metadata каждого согласованного ID/type и репрезентативную покупку каждого вида. Реальные деньги автоматически не тратить.

Для Multicast отдельно проверь backend game/package, каждый SKU/type/reward, authenticated runtime catalog и purchase endpoint по [server-iap.md](server-iap.md). Локальные проверки проходят production delivery pipeline с изолированным transport: server rejection/timeout, malformed response, неправильная награда, disk failure, повтор после grant/claim/confirmation, смена аккаунта и sandbox. Grant и server IDs сохраняются атомарно; при недоступном сервере нет offline fallback и нет confirmation. Readback каталога и отказ некорректному чеку подтверждают доступность сервера, но не заменяют завершённую license-test транзакцию. Fixture не отправляет искусственные успешные покупки в live backend.

- Success: проверенная завершённая покупка, точная сохранённая награда/entitlement, confirmation/acknowledge/consume.
- Cancel/error/pending: без ранней выдачи, корректный UI/дальнейшая обработка.
- Replay/reconnect/restart в критических точках: одна выдача/денежная запись, payment восстановим.
- Restore/reinstall non-consumable: entitlement восстановлен без новой продажи; старый consumable token не выдаёт снова награду.
- Store localized price/ISO currency, sandbox отделён от production.

Новый transaction ledger/reward handling проверяй целевыми replay/restart тестами. Тест, совпадающий с regex/строками реализации, не проверяет оплату. Используй существующие тесты и необходимый минимум новых.

## AAB и Console

До загрузки проверь идентичность приложения и карточки: package, отображаемое имя во всех локалях, application/launcher activity/alias icon и roundIcon. Из итогового AAB или APK, полученного именно из него, извлеки legacy/round картинки и adaptive layers, просмотри их и сравни с утверждённой иконкой карточки. Стандартный значок движка/обёртки, незаполненный слот или неизвестный ресурс блокируют отправку. Запиши SHA-256 AAB и источника иконки, имена ресурсов и визуальный результат отдельно от listing save/readback. Изменение иконки только в Console или PlayerSettings без пересборки не исправляет уже загруженный AAB.

Final signed AAB: package/versionCode/certificate/ARM64+ARMv7/native alignment/manifest/dependencies, без debug grants/ненастроенных SDK/заглушек. Для каждой заявленной ABI проверь полный engine runtime в самом AAB и производном APK: в Unity IL2CPP нужны libunity/libmain/libil2cpp, для GameActivity ещё libgame. Несколько SDK .so не доказывают наличие игрока для этой ABI. Проверь resolver packaging exclusions после выбора архитектур. Устройство запускает ту же версию, не старый APK.

**Охват и WW-закупка:** проверь production Device catalog для целевых моделей/вариантов и гео, сохрани сравнение с предыдущим релизом и причины новых исключений. ARM64-only без принятого ограничения аудитории — fail; проверка только internal или ARM64-эмулятора — unknown для охвата закупки. Даже успешный internal не подтверждает production-исключения. В профиле обязательны фактические `supported_abis`, ссылки на ABI-инвентарь/Console coverage и отдельный `acquisition_compatibility_status`; не отмечай готовность закупки по успешному upload. Контракт — [play.md](play.md#архитектуры-abi-arm64-и-другие).

Read-back: internal, final versionCode, обработка/релиз, принятая группа/opt-in. После Play installation повтори smoke SDK/ads/IAP. Если устройства/диагностических кабинетов нет, после успешной сборки и проверки конфигурации internal можно использовать для получения QA тестеров; device/runtime статусы остаются **ожидающими**. Без необходимых подтверждений runtime-готовность не доказана. Явное поручение владельца опубликовать без прогона позволяет выполнить отправку по [шагу 11](workflow.md#11-production), сохранив непроверенные статусы.

## Gate всей listing после сохранения

Используй inventory и slot mappings из [store-assets.md](store-assets.md). Для каждой listing/локали сверяй **все** наблюдаемые поля: required/starred и conditional, всю применимую визуальную часть, Phone, 7-inch, 10-inch tablets и прочие присутствующие device categories. Evidence должен относиться к текущему manifest и **последней записи**; исправление после readback требует нового save → reload → readback. Проверь native confirmation записи, повторное открытие той же Console-карточки/локали, отсутствие errors/unsaved/pending processing, persisted text/selection и count/order/dimensions каждого набора. Unknown, пустое применимое поле или несовпадение блокируют завершение; «файлы готовы», общая библиотека assets и Ready to send for review этого не доказывают.

Необязательный stdlib gate (без browser discovery, записи в Console и изменения профиля):

```text
python3 <путь-к-скиллу>/scripts/verify_listing.py <required-fields-evidence.json>
```

Exit 1 запрещает переход в `verified`/`complete`; исправь evidence/форму и повтори проверку. Exit 0 проверяет **только полноту, согласованность переданного JSON и соответствие metadata записанным ограничениям каждого поля**, не подлинность receipts, полноту обнаружения формы или соответствие policy. Агент обязан открыть raw native evidence и вручную проверить охват inventory, происхождение dimensions/format/byte/ratio limits и достоверность изображений; false `inventory_complete` и придуманные ссылки недопустимы. Встроенные фикстуры тестов синтетические и не используются как release evidence.

Форма JSON (создаётся из реальных наблюдений, не копируется как успешный receipt):

- Root: `scope_ref` (listing/app), `locale`, `inventory_complete`, `inventory_evidence_ref`, `pending_upload`, `unsaved_changes`, `has_errors`; последние три явно `false` только после проверки. `save`, `reload`, `readback`: `{ "at": "<ISO-8601 с timezone>", "evidence_ref": "<native receipt/snapshot>" }`, save раньше reload, readback после reload.
- `device_categories`: объект с phone/tablet_7/tablet_10 и всеми дополнительно найденными категориями. Значения `{ "present": true/false/null, "applicable": true/false/null, "evidence_ref": "<native form snapshot>" }`. Present означает физическое присутствие поля и требует inventory-записи; applicable означает включение категории в текущий scope и требует проверенных assets. Присутствующая необязательная категория вне scope требует `applicability_evidence_ref` на решение о scope/условие. Не выдавай исключение из scope за отсутствие поля. Null блокирует gate.
- `fields`: непустой полный inventory. Каждый элемент: уникальные `id`, `label`, `inventory_evidence_ref`, булевы `required` (включая starred), `visual`, `applicable`, `kind` (`value`/`assets`), `status`, `readback_evidence_ref`. Conditional trigger/решение записывай рядом; исключение required поля допускается только с `conditional:true`, `condition_satisfied:false` и native evidence. `applicable:false` требует `status:not_applicable` и `applicability_evidence_ref`. Только optional nonvisual поле допускает `status:omitted` с `omission_reason`; выбранное для заполнения поле требует readback.
- `value`: `expected_value` и точный persisted `observed_value` (строка/selection/boolean/object). False — допустимый ответ декларации, не пустое поле. `visual:true` URL/video также требует заполнения/readback, если применим.
- `assets`: `device_category` для screenshot slots, `constraints_evidence_ref`, обязательный `asset_constraints`, актуальные `min_count`/`max_count`, `upload_status:complete`, `observed_count`, `expected_assets`, `observed_assets`. Ограничения берутся из фактически наблюдаемой формы и связанного native evidence в `constraints_evidence_ref`, а не из постоянных предположений о Google Play. `asset_constraints` содержит непустой `formats` (список разрешённых непустых строк, например PNG/JPEG) и положительный целый `max_bytes`. Геометрия задаётся ровно одним способом: `exact_dimensions:[width,height]` из двух положительных целых либо положительные целые `min_side`/`max_side` (`min_side <= max_side`) и непустой `allowed_aspect_ratios:[[width,height],...]` из пар положительных целых. В первом варианте min/max/ratios отсутствуют; во втором exact_dimensions отсутствует. Min/max применяются к обеим сторонам включительно, размер файла — не больше max_bytes, пропорция проверяется точно через `width * ratio_height == height * ratio_width` без float/округления; ориентации перечисляются отдельно. Missing/malformed ограничения блокируют gate.
- Оба массива assets — в порядке слотов, каждый элемент содержит native `asset_id`, положительные целые pixel `width`/`height` и `bytes` (размер файла), `format` и SHA-256 проверенного файла. Boolean, float и строка вместо целого недопустимы. Expected и observed metadata каждый проверяются по ограничениям своего поля, даже если полностью совпадают: VIDEO отклоняется для screenshot поля с `formats:["PNG","JPEG"]`. Сохраняется отдельный count gate по `min_count`/`max_count`; observed metadata должно совпасть с expected и count. Hash/размер локального файла сопоставь с Console asset ID/preview/download и сохрани доказательство. Metadata/hash нельзя вывести из CSS preview. Происхождение/universal reuse храни в asset manifest; одинаковый файл в разных категориях допустим. Ветка `applicable:false` (в том числе присутствующая optional категория вне scope) не требует assets/asset_constraints; требования к native applicability evidence сохраняются. Допустимое optional nonvisual `status:omitted` также не требует assets/ограничений.

Фрагмент схемы с **синтетическими** числами, не Google Play limits и не release receipt; для реального поля замени их наблюдениями и native ссылкой:

```json
{
  "constraints_evidence_ref": "synthetic-form-limits",
  "min_count": 1,
  "max_count": 8,
  "asset_constraints": {
    "formats": ["PNG", "JPEG"],
    "max_bytes": 8192,
    "min_side": 320,
    "max_side": 3840,
    "allowed_aspect_ratios": [[9, 16], [16, 9]]
  },
  "expected_assets": [{
    "asset_id": "synthetic-asset",
    "width": 1080,
    "height": 1920,
    "bytes": 4096,
    "format": "PNG",
    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  }],
  "observed_assets": [{
    "asset_id": "synthetic-asset",
    "width": 1080,
    "height": 1920,
    "bytes": 4096,
    "format": "PNG",
    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  }],
  "observed_count": 1,
  "upload_status": "complete"
}
```

Альтернативный синтетический `asset_constraints` для exact-size icon: `{"formats":["PNG"],"max_bytes":4096,"exact_dimensions":[512,512]}`. Размеры и bytes здесь тоже заменяются требованиями фактической формы.

Только после успешного gate и ручной проверки native evidence обновляй `completion_gate_status:passed` и статус этапа. Missing access/worker/device остаётся pending соответствующего этапа независимо от результатов других этапов.

## Запуск, FPS и экран (gate перед production)

По умолчанию требуется для каждой сборки, уходящей в production; причина — реальный реджект Google «Broken Functionality» (вылет при старте Android 16, код правки окна выполнялся до `setContentView`). Gate не разрешает запуск эмулятора: перед каждым запуском/перезапуском отдельно спроси оператора и дождись согласия по [emulator.md](emulator.md). Если владелец явно поручил публикацию без runtime-прогона, отправь в разрешённые треки без эмулятора и повторного согласования, сохранив пропущенные проверки, причину и поручение. Пропуск не означает PASS и не устраняет известный дефект.

- **Запуск:** release/R8-артефакт (тот же AAB, что пойдёт в стор, или APK из него) стартует без вылета на устройстве или эмуляторе ПК ([emulator.md](emulator.md)). Вылет разбирай по `adb logcat -b crash -d` (`FATAL EXCEPTION`), а не по догадкам. Код окна, вида и системных панелей вызывай только после `setContentView` и оборачивай в try/catch: косметика не должна ронять запуск.
- **FPS:** 2D-игра держит **не менее 30 FPS всегда**, на среднем телефоне, в течение всей сессии, без деградации со временем. До релиза: замер стоимости кадра по часам игры бота (рост метрики со временем блокирует релиз), CPU-профиль, перепись вызовов отрисовки и прогон в реальном времени со звуком. В сборке остаётся диагностика устройства: строка `MC perf fps=.. upd=.. drw=..` раз в 10 секунд и лесенка качества при FPS ниже 40. Эмулятор считается только в режиме окна с GPU ПК; headless-FPS не доказательство.
- **Экран:** игра занимает весь экран устройства от края до края, иммерсивный режим, без чёрных полос и рамок сверху и снизу и без «камерных» вырезов (растягивается на дисплей, вырез разрешён по короткому краю). Проверяется скриншотом на поддерживаемых пропорциях.
- **Реклама до production:** rewarded проверяется на устройстве в internal до production: юнит создан, loaded, показ, награда. Показ не зависит от `UMP.canRequestAds()` вне GDPR (MAX опрашивает UMP только для GDPR-геолокации). Причины отказа показа пишутся в лог (`MC rewarded unavailable ... blocker=...`), статус рекламы доступен в dev-консоли.
- Слои не подменяют друг друга: эмулятор доказывает запуск, лог и раскладку; показ рекламы, покупки и FPS на железе подтверждает устройство владельца.

## Internal QA и внешний вид магазина

Gate перед production (шаг 10 [сценария](workflow.md#10-qa-перед-production)): rewarded и однократная награда, покупка с серверной валидацией, доставка событий AppMetrica/Adjust ([analytics.md](analytics.md)), чистый лог, 2 минуты игры на этом артефакте.

Обязательный формат — rewarded; optional interstitial/banner проверяются при включении в scope. Обычные internal и production содержат live ad IDs, production SDK environments и не включают устройства в test mode автоматически. Явный рекламный QA — отдельная сборка/вручную настроенный test device, с подтверждённым SDK test mode. HUD appearance bypass сам по себе не меняет рекламный режим. Награда только после completed reward callback. Каждый новый AAB повышает одновременно versionName и versionCode; прочитай версии из фактического подписанного manifest и Console. Перенос того же AAB между треками сохраняет обе версии.

Магазин проверяется и визуально: стиль текущих игровых панелей, иконка/изображение каждого товара, различимые размеры пакетов, читаемые эффект и price CTA, отсутствие overflow на поддерживаемых пропорциях. Сохрани реальные screenshots окончательного билда и осмотри их.
