## 19.10.2026 — обновлённый подписанный AAB0.1.2(3) собран

По поручению «сделай обновленный ААВ» в клиент вписаны действительные опубликованные privacy/terms; C# и Python используют точные разрешённые страницы с правильными ролями, без разрешения чужих сайтов/общих16+ документов/query/fragment. Age/consent и no-ads сохранены. Нативные настройки применяются в изолированной release-копии и при обычной сборке; local-artifact-only не использован.

Свежая Play Console показала единственный занятый код2. Выбраны0.1.2/code3. **14 Python guard-тестов и16 Unity PlayMode прошли**; XML `evidence/updated-aab-playmode-20261019.xml`. Стандартный guarded builder завершился exit0: подтвердил подпись прежним upload key, свежий receipt/hash и legalConfigured=true. Готовый файл на Mac: `/Users/arthuri/Unity Projects/Cats-Cute-Miner-main/release/google-play/builds/cat-miners-0.1.2-3-noads.aab`.

После окончания сборки соединение Mac снова перестало отвечать, включая минимальную команду(20s timeout) и передачу(45s timeout). Финальный manifest/Billing/ABI/16KB инспектор запущен, но результата пока нет; получение receipt/hash и копия AAB на хаб не завершены. SHA/размер новой сборки не выдуманы; signature verified — по успешному guarded wrapper. Владельцу отправлен запрос восстановить соединение; новых Unity build/upload/release/device/emulator не запускалось. Pending local sessions: inspection7333, help73429, safe progress12409. Доказательство: `evidence/aab-update-preparation-20261019.json`. Повторять уже успешную сборку из-за соединения не требуется.

## 19.10.2026 18:49 UTC — проверенный владельцем AAB сверен с Google Play

Владелец сообщил, что AAB собран, протестирован и рекламы в нём нет; принято как его подтверждение этого теста, не как отдельное доказательство покупки/событий. Свежая Console: Alpha0.1.1(2) уже Available to testers on Google Play, Production Inactive; новая privacy URL —1 change not yet submitted for review.

Скачан Original file именно артефакта4860242342446858927,0.1.1(2).77 794 443 байта, SHA-256 `00894758c49ecf3bc8e9ca2c5ccb189fb9f10aa4ab3b6a71c477e418dd8dddc1` точно совпал с настоящим Mac builder receipt r9. Подтверждены AAB/manifest entries и ARMv7+ARM64 в самом ZIP. Console также показывает Target36, API26+,17 090 устройств, Supports16KB; это не заменяет runtime QA.

В matching builder receipt **legalConfigured=false**,localArtifactOnly=true. Следовательно оставшаяся правка конкретна: опубликованные позже privacy/terms не попали в этот AAB; LegalValid удерживает кнопки документов и optional SDK initialization. Не требуется заново доказывать отсутствие рекламы; требуется обновить фактические URLs и разрешить точные страницы своего Hub сайта, затем новый AAB/проверка ссылок, eligible analytics и покупки и production с новой privacy metadata. Новых build/upload/release/Submit в этой проверке не было. Доказательство: `evidence/release-next-step-confirmed-20261019.json`.

## 19.10.2026 18:34 UTC — документы приняты и опубликованы, support подтверждён

Владелец поручил пункт1 («1. делай») и явно подтвердил: «Я — беру процесс на себя; почта работает, доступ к исполнению есть». Это заменяет прежний неподтверждённый статус. Финальные Privacy Policy, Terms и страница запросов данных приняты; внутренний SUPPORT_PROCESS.md фиксирует ответственного и обработку запросов, включая покупки/обязательное хранение. Реальных обращений агент не отправлял и удаление конкретного игрока не проверял.

Штатным publish_site опубликован отдельный сайт https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/ с privacy/, terms/ и delete-data/. Четыре публичных HTML доступны без входа с HTTP200; SHA-256 совпадают с локальными. В публичной папке нет трекеров, форм, приватных конфигов/регламентов. Multicastgames.com не менялся. Название документов соответствует фактическому Cat Miners: Idle Tycoon;13+, всеunder16/unknown защищены, первый релиз без рекламы,6 IAP.

В собственном Play Console новая privacy URL сохранена вместо общей16+ policy, reload подтвердил значение. Send for review/Publish не выполнялись. Остался клиентский шаг: runtime URLs пока пусты, GameUrl и Python preflight разрешают только multicastgames.com. Нужны точные разрешённые URLs собственного опубликованного сайта, финальный AAB и Android QA; текущая установленная сборка сама не обновлялась. Доказательства: `evidence/legal-publication-20261019.json`, `evidence/privacy-console-url-20261019.json`; активный manifest `Docs/Legal/CatsCuteMiner/publication-release.json`.

## 19.10.2026 18:21 UTC — все6 инапов созданы и активны в Google Play

По прямому поручению владельца «1. добавь инапы» штатная форма SuperLeo import загрузила ранее согласованный JSON, hash `a62ad331822e84b6621a081df965a3501e9865934712ae9100f41076e8b8735b`. Все6 результатов Google Play OK + backend OK. Повторный API readback и собственная Play Console показывают ровно6 товаров, у каждого1 active purchase option. Дополнительно карточка gems_handful подтверждает default Buy/Active,174 страны покупки; app distribution не менялась.

Пакеты160/$1.99,500/$5.99,1200/$11.99,2500/$22.99,6500/$59.99,14000/$119.99. Программно сверены реальные Google SKU/цены с согласованным импортом и клиентским store-5; серверные consumable типы/Google mapping/rewards совпадают. Другие store mappings пусты до/после. Свой authenticated runtime catalog от14.10 переиспользуется при тех же gameId/package/SKU/типах/наградах. Нового AAB/upload/release не было, реклама остаётся отключена. Реальная license-test покупка, выдача/confirmation/recovery на Android ещё не выполнены, статус waiting_for_device. Доказательство: `evidence/iap-active-current-20261019.json`.

## 19.10.2026 18:12 UTC — что осталось до production

Повторно прочитаны Dashboard, Publishing overview, Production и One-time products. На проверке только Closed Alpha2(0.1.1); Production Inactive, Create new release доступен. Каталог Play после дополнительного readback явно показывает **No results**. Прежнее сообщение о недостающем BILLING исчезло, доступно создание товаров; runtime Billing этим не проверен. Backend6 товаров зарегистрированы ранее, Play activation/покупки ещё не подтверждены.

В исходном runtime config `privacyUrl` и `termsUrl` по-прежнему пусты; GameSdkRuntime требует LegalValid для запуска AppMetrica/Adjust/Meta. Конфигурация загруженного bundle отдельно не проверялась. Остаток: принятие/hosting документов и фактический процесс поддержки; рабочие URL и финальный клиент;6 Play-продуктов/реальные license-purchase/server reward/confirmation; итоговая проверка AAB и Android runtime/аналитики; отдельный production release/Google review. Реклама отложена по решению владельца. Внешних изменений/публикации нет. Доказательство: `evidence/launch-remaining-20261019.json`.

## 19.10.2026 17:59 UTC — текущий статус Play проверен

Под `sargsyan@multicastgames.com` в MULTICAST GAMES проверено своё приложение4974963942177850884. После scoped refresh Google по умолчанию открыл tigblack1/McArthur; выбран уже авторизованный сотрудник, текущий индекс Google-аккаунта — `/u/1/`. Это переключение входа, не перенос игры и не изменение аккаунта разработчика.

**Internal активен:** First Release, версия0.1.1/код2, Available to internal testers; Console указывает Oct19 3:15PM. Есть дополнительный пустой draft, он не отменяет доступный релиз. Выбраны оба прежних списка: License testers27 и Test32. Штатная кнопка Copy link вернула `https://play.google.com/apps/internaltest/4701668636091023018`.

**Closed testing Alpha:** тот же код2, In review (Console Oct19 5:55PM),177 стран. Publishing overview подтверждает Changes in review для Alpha/Resume track. **Production Inactive**, публичного production-релиза нет. Для internal поиск Play Store не используется: opt-in → ссылка магазина под тем же Google-аккаунтом тестера. Установка на телефоне владельца не проверялась. Hash загруженного bundle с локальным AAB не сравнивался; никаких upload/release/правок списков/вступления в тест в этой проверке не было. Доказательство: `evidence/release-status-current-20261019.json`, подробные readbacks рядом.

## 19.10.2026 — подписанный AAB0.1.1(2) собран локально

По прямому запросу владельца собран Release-format AAB без рекламы, IL2CPP, ARMv7+ARM64. Guarded builder завершился exit0, подтвердил подпись существующим upload key и совпадение SHA-256 с receipt. Файл на Mac: `release/google-play/builds/cat-miners-0.1.1-2-noads-r9.aab`,77 794 443 байта. Полный SHA пока не получен на хаб.

Исправлены реальные build blockers: два LFS pointer вместо PNG иконки/фона; исходники partial SDK в release gate; встроенные Editor test defines; переносимый AndroidPlayer путь; служебный Meta editor debug-keystore prerequisite (изолирован от реальной upload-подписи). Runtime consent/LegalValid и отсутствие рекламы сохранены. Явный local-artifact-only позволяет собрать файл с пустыми legal URL, не принимая документы и не открывая публикацию. Публикация/SDK runtime не объявлены готовыми.

После успешной сборки SSH к Mac перестал отвечать, затем reverse tunnel3049 оказался отключён. Заключительная проверка содержимого AAB, чтение receipt/hash и передача файла на хаб не выполнены. Попытка friendly-copy не запустилась: действителен только исходный путь r9. Никакой upload/release/device/emulator не запускался. Доказательство: `evidence/aab-local-build-20261019.json`.

## 19.10.2026 — Play/SuperLeo снова доступны, статус инапов перечитан

После подтверждения владельца agentb успешно перенёс только Play/SuperLeo сессии через штатное согласие Chrome. Собственный проект пакета найден в SuperLeo под sargsyan@multicastgames.com; в Enabled игре gm_cats_cute_miner видны все6 consumable-продуктов с верными Google mappings и наградами. Это backend-регистрация, не активация Google Play.

В Google Play перечитаны реальные страницы приложения4974963942177850884: One-time products пуст, сообщение требует BILLING permission и предлагает Upload a new APK; Internal testing — Inactive/No releases; Latest releases and bundles — No releases/None. Ошибка доступа снята; создание товаров ждёт настоящего Billing internal release. Новых внешних записей/upload/покупок не было. Подтверждение процесса поддержки, принятие/hosting документов и реальные legal URL остаются нерешёнными. Доказательство: `evidence/iap-console-access-20261019.json`.

## 19.10.2026 — инапы: сверка и подготовка создания в Play

Повторный запрос владельца добавить инапы. Все шесть ранее согласованных расходуемых пакетов совпадают по ID/количеству/цене между store-5, PaymentRules и зарегистрированным backend-каталогом. Подготовлен `iap-import.prepared.json`; он не отправлен. Товары в Google Play ещё не созданы и не активированы. Установлены Unity IAP5.2.1 и декларация Billing8.3.0; реальный native Billing artifact не проверен. Текущие исходники с IAP прошли Android C# и18 PlayMode/75 EditMode в no-ads проверке ранее19.10; это не проверка покупки.

Исправлен ложный конфликт preflight с AssetImportWorkerHW открытого основного Unity;10 процессных/подписных guard-тестов прошли. На Mac все Android toolchains на месте, конфликтующих Unity процессов0. Единственный текущий preflight blocker — пустые game-specific legal URLs. Подтверждение процесса поддержки, принятие и hosting документов запрошены у владельца. Обновление истёкших Play/SuperLeo сессий ждёт штатного разрешения Chrome. После этого: настоящая Billing AAB/internal → создание/активация шести товаров → license-purchase/server/durable/recovery/device QA. Доказательство: `evidence/iap-preparation-20261019.json`. Нового AAB/upload/release/платежа не было; реклама остаётся отключена.

## 19.10.2026 — текущий первый релиз БЕЗ РЕКЛАМЫ

Последнее прямое решение владельца: «Убери сейчас рекламу из игры. Первый релиз без нее. Потом второй подключим». Заменяет rewarded-scope18.10. В профиле `ads.enabled=false`, `enabled_formats=[]`, `required_formats=[]`, `adapters=[]`; MAX/provider, mediation, сети, рекламный native CMP, показы/награды/revenue QA **отложены до второго релиза, не являются блокерами первого и не отмечены complete**. Собственный rewarded unit `f27564b55995aa89` и ссылки на прежние key evidence сохранены для будущего; ключи не выводились и backend не менялся.

Юридические Privacy/Terms, локальные review HTML/ZIP и publication-plan обновлены под отсутствие рекламы для всех возрастов. Аудитория13+ включает13–15; все до16/unknown защищены. Шесть consumable IAP и опциональные AppMetrica/Adjust/Meta для подходящих16+ сохранены. Бразилия остаётся исключена, dev-консоли нет, кампания не запускается.

**Реальный процесс поддержки/родительских запросов, доступа, хранения и удаления НЕ подтверждён. Документы НЕ приняты и НЕ опубликованы; действующие legal URLs пусты.** Сохраняются принятие/hosting отдельных документов13+, сверка Console (No ads, Advertising ID по финальному SDK inventory, Data safety, возраст, App access), финальные source/native SDK inventory/manifest/Billing/ABI/16KB/signing/version checks, подписанный AAB/internal, Play-каталог, реальные покупки/серверная выдача/recovery, защита возраста и optional analytics/device/dashboard QA. Снятие рекламных требований не закрывает эти этапы.

MAX удалён из UPM/ссылок сборки/Android-зависимостей и defines. Настройки сохранены вне Assets в `Tools/GooglePlay/DeferredMaxSdk`; обычный Editor следует no-ads scope, рекламные команды недоступны. Проверено на Mac: **18/18 PlayMode**, **75/75 EditMode**, компиляция Android player C# без MaxSdk.Scripts.dll и с Unity IAP. Отдельный no-ads source gate прошёл. Доказательство: `evidence/no-ads-first-release-20261019.json`. Это проверки исходников и Editor: нативный AAB не собран, не загружен и на устройстве не проверен.

Ниже сохранена история. Требования включить MAX/rewarded/native advertising CMP и прежние6+ заменены текущим scope; исторические результаты не объявляются свежими проверками.

---

## 18.10.2026 — исправлен конфликт MAX MaxUserData

Удалены идентичные UPM дубликаты `Assets/MaxSdk/Scripts/MaxUserData.cs` и `.meta`, из-за которых Unity игнорировал пакетный тип и выдавал CS0246 в MaxSdkAndroid/MaxSdkUnityEditor. SDK8.6.6 и настройки ключа сохранены; оставшихся GUID-конфликтов MAX нет. Свежие компиляции Editor и Android player C# прошли без ошибок. Открытому GUI запрошен ручной Assets → Refresh: macOS отклонил assistive access для автоматического нажатия. Доказательство: `evidence/unity-max-guid-fix-20261018.json`.

## 18.10.2026 — ключ MAX получен и применён

Клиентский ключ владельца сохранён в runtime config и официальном AppLovinSettings.asset через публичный Unity SDK API. Совпадение проверено в тестовой копии и основном проекте на Mac. Ключ в отчёте не выводится; подтверждение — `evidence/max-client-key-config-20261018.json`. Реальные рекламные запросы не выполнялись. Нативный consent flow и показ по-прежнему ждут действительных принятых legal URL, финального Android-билда и устройства.

## 18.10.2026 — MAX rewarded восстановлен, ожидание SDK Key

Владелец включил рекламу. В проект возвращены официальный MAX8.6.6 (Android13.6.4), ссылка MaxSdk.Scripts и Android CATMINERS_MAX; C# player API с MAX успешно скомпилирован.75 тестов приватности/бонусов/доставки покупок прошли. Существующий rewarded f27564b55995aa89 своей игры прочитан через SuperLeo: включён, AppLovin Network/Exchange включены; его сети, лимиты и floor не менялись. SDK Key отсутствует и запрошен у владельца. Принятые legal URL и нативный CMP ещё не настроены; на устройстве показ/награда/revenue не проверены, AAB не собран. AdMob provisioning вновь упал на серверном воркере; последующий каталог своей записи не содержит. Прочие дополнительные сети пока не зарегистрированы для игры. Доказательство: `evidence/max-reenable-20261018.json`.

## 18.10.2026 — бесплатные тайм-скипы убраны

По запросу владельца store-5 не выдаёт стартовые 3 коротких и 1 средний скип. StoreVersion2 при загрузке удаляет остаток прежнего набора; покупки за гемы сразу применяют скип и не хранились в запасе. Цены50/150/350 и длительности10/30/60 минут сохранены. Unity-playable сохраняет платные карточки без бесплатной выдачи. Проверки на Mac:67 EditMode (магазин/сохранения),8 PlayMode (магазин/покупка/видимость/игровое время), все прошли. Доказательство: `evidence/no-free-timeskips-20261018.json`. Новый Android-билд ещё не собран/не установлен.

## 18.10.2026 — AppMetrica: своя запись и SDK-ключ настроены

Вход через Chrome подтверждён. Исходная ссылка4712257 относится к Art Puzzle World, её ключи не использовались. Создана Cat Miners: Idle Tycoon (6374386, аккаунт tigblack2016); SDK-specific client UUID из её настроек сохранён в runtime config и сверен по SHA-256 на Mac и в тестовой копии. Post API key не копировался. UTC; маскировка полных IP ЕС включена. Жанр/ссылка стора пока не заданы. Приватность: 10/10 существующих EditMode-тестов прошли. Подтверждение: `evidence/appmetrica-own-config-20261018.json`. Инициализация ждёт действительных принятых legal URL и согласия16+; до16/неизвестный возраст защищены. Нативный запуск, реальные события/доход в кабинете ещё не проверены.

## 18.10.2026 — сессия восстановлена, первый ключ Mac создан

После согласия владельца Play снова доступен. Повторный readback своего приложения подтвердил отсутствие зарегистрированного upload certificate. Создан и проверен первый upload.jks в `/Users/arthuri/MulticastSigning/com.multicastgames.catscuteminer/first-upload-20261018/`; секретный JSON рядом, оба файла mode600, вне проекта. Новое публичное доказательство: `evidence/upload-signing-macos-20261018.json`. Активная ссылка профиля переключена; историческое Windows-доказательство сохранено. Проверка подписи сборщика приняла новый receipt. Google-managed app signing key не менялся. AAB не собран/не загружен, upload certificate ещё не зарегистрирован в Play. Ожидание: принятие и публичные URL документов13+, реальный процесс поддержки, оставшиеся provider/device gates.

## 18.10.2026 — подготовка первого ключа: ожидание обновления сессии

Скрипт создания проверен; шесть тестов ограничений подписи прошли. На Mac скрипт совпадает по SHA-256, целевой каталог отсутствует. Ключи и секреты не создавались. Повторная проверка Console перенаправила на Signed out; штатное обновление через Chrome завершилось CONSENT_TIMEOUT. Перед созданием ключа требуется успешный вход и повторная сверка сертификата загрузки. Unity/Android доступны; финальную сборку блокируют также непринятые документы 13+.

## 18.10.2026 — уточнение владельца: аудитория 13+

Целевые группы: 13–15, 16–17, 18+. Прежний выбор 6+ заменён последним уточнением владельца. Наблюдавшийся черновик Console 13+ соответствует этому решению; новых записей в Console и отправки на проверку нет. Защита до 16 лет и при неизвестном возрасте сохранена. Черновики документов обновлены для 13+, но принятие, процесс поддержки и публикация остаются неподтверждёнными. Возрастной рейтинг контента этим не менялся.

# Все три кабинета подключены — 18.10.2026

После подтверждения владельцем выполнена scoped-передача Adjust/SuperLeo. Новый native_capture устранил зависание старого connectOverCDP; 28 тестов и реальная проверка отдельного Chrome с зависшей посторонней вкладкой прошли (43 мс). Штатное согласие Chrome сохранено. Секреты и cookies в отчёты не включались.

Проверены настоящие данные: Google Play собственная карточка, Adjust `cdbdt3hkj56o` (Cats Cute Miner, USD, 53 видимых события), SuperLeo под `sargsyan@multicastgames.com` роль user — единственный проект пакета `com.multicastgames.catscuteminer`, ID `e4e954f1-59a9-479a-821f-be7eed9aff47`, Meta `5614484342110411`, Adjust binding совпадает. Это проверка входа/чтения, не init/delivery SDK или права произвольной записи. `evidence/browser-services-ready-macos-20261018.json`.

В актуальной Console `/keymanagement` раздел Upload key certificate сообщает: «Certificate fingerprints will be shown here after you upload your first app bundle». Первый upload certificate ещё не зарегистрирован; Google-managed app signing key уже существует и не изменялся. Подготовка первого локального upload key на Mac возможна без сброса ключей Console; прежний Windows key не перезаписывается. `evidence/google-play-signing-current-20261018.json`.

Родительский процесс остаётся НЕ подтверждённым, документы review copy не приняты. Запрошен новый выбор scope: сохранить согласованные6+ и дождаться процесса либо явно изменить игру, документы и релиз на16+. До ответа сохраняются6+ и защита under16/unknown. Действующая общая policy студии §9 —16+, в текущем черновике Console13+. Возраст/декларации не менялись; AAB не отправлялся.

---

# Chrome подключён; кабинет Play проверен — 18.10.2026

Владелец включил штатную отладку Chrome; после согласия сессии переданы через agentb. Google Play принимает сессию: actor `sargsyan@multicastgames.com`, developer `8879742639795815518`, app `4974963942177850884`, package совпадает. В Adjust фактически `AUTH_REQUIRED`, в SuperLeo видна форма Email Address / Access Studio. Обе страницы открыты на Mac, запрошен обычный вход; Google Play повторно подключать не нужно.

Текущий черновик Console отличается от сохранённых решений: имя **Cat Miners: Idle Tycoon**, аудитория **13 and older**, Privacy Policy **https://multicastgames.com/policy**. Это наблюдение кабинета, не новое решение владельца. Пользовательский выбор 6+ не менялся. Publishing overview показывает Changes not yet submitted for review и заблокированную отправку до завершения dashboard. Изменений деклараций и отправки в этой сессии не выполнялось. Данные: `evidence/browser-service-access-macos-20261018.json`, `evidence/console-publishing-readback-macos-20261018.json`.

Владелец ответил: фактический процесс обработки родительских запросов **пока не подтверждён**, местонахождение ключей **не знает**. Документы не приняты и не опубликованы. Spotlight по двум известным именам в /Users/arthuri совпадений не дал; это не полный поиск диска. Подпись в новом интерфейсе Console пока не подтверждена, новый ключ не создавался. AAB/release не отправлялись.

---

# Продолжение выпуска на Mac — 18.10.2026

Пользователь явно поручил выпустить всю игру с пакетом `google-play-publish-2026-10-17-complete-integrations`. Режим `release`: internal, затем production по необходимым проверкам. Новый запрос не отменяет прежние решения владельца: аудитория 6+, защита всех младше 16 лет и неизвестного возраста, выпуск без MAX/рекламы, шесть согласованных IAP, исключение Бразилии, только лёгкий журнал. Кампании закупки не запускаются. Дополнительное согласие на запуск эмулятора не получено; эмулятор не запускался.

Фактически выполнено:

- Обе предоставленные папки Google Drive доступны: три существующих MP4 и три HTML playable. Ссылки и локальные SHA-256 playable сохранены в `evidence/publication-resume-20261018.json`; равенство скачанных байтов Drive локальным файлам и приём рекламными сетями не проверены.
- Android Build Support, SDK/API 36, NDK и JDK на Mac **установлены и выбраны** для Unity **6000.6.4f1**. PlaybackEngines находится рядом с Unity.app. `evidence/macos-preflight-20261018.json`. Старый Windows preflight не используется как доказательство готовности Mac.
- В изолированной Unity-копии прошли **247/247 EditMode-проверок** платежей, приватности/наград и mine dressing; свежая компиляция Android player C# прошла без MAX/AppLovin assemblies. Это не нативная сборка и не device QA. `evidence/macos-release-checks-20261018.json` и соответствующие XML/JSON.
- Добавлены `Tools/GooglePlay/preflight-macos.sh`, `build-aab-macos.sh`, `google_play_macos.py`. В общем Unity builder исправлены путь AndroidPlayer и имена java/keytool/jarsigner для macOS, сохранены legal/provider/signing guards и ARMv7+ARM64. Обёртка не переписывает C# в зеркале. Подписанный AAB ещё не создавался.
- `agentb` из предоставленного пакета поддерживает штатный Chrome endpoint macOS и запуск конкретного файла пакета через мост. Зависимости установлены на хабе и Mac, **7/7** тестов передачи/изоляции/отказов прошли. Реальные сессии провайдеров в текущем чате не передавались.

Ожидания, запрошенные у владельца:

1. Включить `chrome://inspect/#remote-debugging` и иметь входы Play Console, Adjust и SuperLeo в этом Chrome. Read-only `agentb install` на Mac возвращает отсутствие native endpoint. Согласие Chrome должен подтвердить владелец.
2. Указать пути к прежним `upload.jks` и `signing.secret.json` на Mac. Известное evidence относится к `C:\Users\arutt\MulticastSigning\...`; в четырёх проверенных местах Mac файлов нет. Это не полный поиск всего диска. Новый ключ не генерировался.
3. Подтвердить фактическую обработку родительских запросов студией и принять подготовленные отдельные Privacy Policy/Terms. Оба предложенных публичных адреса игры в браузере возвращают **404**, runtime URL пусты. Документы остаются review copy; публикация юридических утверждений без подтверждённых фактов не выполнялась.

Доступ к кабинету, версии в Console, публичные документы и прежняя подпись нужны перед финальной AAB-сборкой. Загрузки AAB, internal release, opt-in и отправки production в этой сессии не было. Исторические записи ниже сохраняются и не являются текущим readback кабинетов.

---

# Название и подготовка AppLovin — 16.10.2026

Название для магазина: **Cat Miner: Idle Tycoon** (22 символа), короткое **Cat Miner**. ProductName, локализация playable, текущий ТЗ, legal review pages/ZIP, текст магазина, баннер и новый30s ролик обновлены. Package `com.multicastgames.catscuteminer`, SKU, сохранения Android и существующие service IDs прежние. Название и новый feature graphic16.10.2026 в16:17UTC применены через **Review → Save**. После отдельной загрузки проверены новое имя в Dashboard и Publishing overview, поля и6изображений карточки. AI-декларация отмечает только иконку и баннер;4реальных скриншота не помечены. Предыдущее Save as draft сохраняло поля редактора, но Dashboard оставался со старым именем: этот недостающий шаг исправлен. Отправки на app review/production не было. `evidence/branding-console-applied-current.json`.

Поручено **подготовить AppLovin/Axon привлечение без создания/запуска кампании и расходов**. Страны и дневной бюджет USD намеренно оставлены пустыми. Рекламный ролик и endcard с новым названием загружены в собственную библиотеку SuperLeo; иконка переиспользуется. Собственная ссылка Adjust `25sohm3z` создана один раз после проверки существующих, click/impression URL перечитаны после reload и сохранены со штатными макросами. Data sharing Axon всё ещё Android Disabled: нужен собственный подтверждённый account SDK key через разрешённую панель/приватный конфиг, затем purchase mapping и privacy/eligibility проверка. SDK app token не подменяет token ссылки. Ни платного трафика, ни тестовых tracking-кликов не было.

Архив: `release/advertising/applovin/CatMiner_AppLovin_Preparation.zip`; просмотр: `release/advertising/applovin/index.html`. Это готовые локальные материалы и draft с пустыми полями, а не принятая платформой или готовая к трафику кампания. Модерация creative set, live store, AAB/device, child legal и остальные gates ещё не закрыты.

Применён установленный `/home/claude-sargsyan-tinote/.agents/skills/google-play-publish/SKILL.md`, источник — новая версия emulator-consent. Входы SuperLeo/Adjust перенесены после штатного согласия Chrome и проверены. Последующие browser-действия через agentb, MAX/Meta только через панель. Эмулятор не запускался и отдельного согласия нет. Выбор6+, защита всех under16/unknown, no-MAX/ads, Brazil excluded и только журнал сохраняются.

# Первый выпуск без MAX — решение владельца16.10.2026

MAX и реклама отложены пользователем («Пока давай без MAX»). Пакет UPM, ссылка MaxSdk.Scripts и нативная Maven-зависимость исключены; рекламный runtime закрыт отсутствующим CATMINERS_MAX, AdsEnabled=false. На Android скрыты рекламные плитки, офлайн×2 и периодические рекламные бонусы, из согласий убрана реклама; ранее заработанные durable rewards не теряются. Гемы/IAP/сундуки и исключение Бразилии сохранены.

Unity Configure и Android player C# прошли, MAX/AppLovin assemblies отсутствуют.13/13 выбранных PlayMode-тестов прошли, включая сброс старого ads consent и отсутствие поддельных reward callbacks. Console Ads=No и AdvertisingID=No сохранены и перечитаны; описание обновлено, те же6assets назначены. MAX SDK key, mediation, CMP и Axon sharing больше не блокируют этот выпуск. Evidence: `evidence/no-max-release-current.json`.

Дополнительный UI-аудит: на Android без рекламы отключено и само расписание рекламных бонусов (BonusRules=null); приватность Adjust/Meta и выдача уже заработанных наград сохранены. Android player C# после уточнения снова прошёл.

Остались действующие отдельные юридические страницы игры/детский процесс, завершение деклараций и reviewer access, затем подписанный AAB/internal и device/IAP/provider QA. Нативный артефакт ещё не создан; C# compile не заменяет проверку merged manifest/16KB/устройства.

Ниже сохранена история до решения выпустить без MAX; её требования рекламы заменены этим разделом и profile.json.

---

# Продолжение публикации — 16.10.2026

Явное поручение владельца продолжить публикацию возобновляет режим `release`; старый `publication_hold` снят в profile.json. Фактические gates сборки/устройства/кабинетов сохраняются. Выбор аудитории от 14.10 (mixed, включая младше13) остаётся в силе и имеет приоритет над новым студийным шаблоном16+.

Подтверждено в этом продолжении:

- ПК: Unity6000.3.11f1, SDK/API36, NDK27.2.12479018 и JDK17.0.9 доступны; подключённых Android-устройств0. Evidence: `evidence/pc-preflight-2026-10-16.json`.
- Существующий upload key проверен через keytool, не заменялся. Секреты вне проекта/синхронизации, только публичное evidence `evidence/upload-signing.json`.
- Настройки Unity применены и перечитаны: API36, ARMv7+ARM64, IL2CPP; все18 Android icon slots и default icon заполнены. `evidence/launcher-icons.json`, `evidence/native-project-configuration.json`. Это ещё не merged-manifest/launcher proof финального AAB.
- Подключено52 игровых события к независимому Adjust mirror; отдельные purchase revenue и MAX ILRD сохранены. Добавлены ограниченные очередь/MC log и минутный foreground timer. `events.required.json` содержит только реально испускаемые имена; 16.10 создан собственный Adjust `cdbdt3hkj56o` в USD без шаблона, все52 токена зеркала и отдельный purchase `fv23mx` созданы, перечитаны и внесены в игру. `evidence/adjust-dashboard-current.json`, `adjust-tokens.json`. Доставка с устройства ещё не проверена. Защита детей/неизвестного возраста и независимые согласия сохранены.
- Настоящая Android player C# компиляция прошла; три новых Unity-теста токенов/таймера прошли. `evidence/android-player-api-compile.json`, `evidence/telemetry-source.json`. Финальные native/Gradle/16KB и device/dashboard проверки не выполнены.
- Отдельные Privacy Policy/Terms сверены с клиентским purchase/privacy/reset кодом, подготовлены HTML и ZIP для просмотра: `Docs/Legal/CatsCuteMiner/cats-cute-miner-legal-review.zip`, хеш в `evidence/legal-review-current.json`. Страницы проверены на настольном и мобильном экране, локальные ссылки исправны, JS/форм/трекеров нет. Это review copy, не действующие опубликованные документы. Обе ссылки добавлены на возрастной экран и в защищённые настройки при наличии действительных URL; описание согласия Adjust уточнено до game activity/sessions/attribution. Android player C# после правки прошёл повторно.
- Найдена и исправлена причина невидимого агентского Chrome: старый WMI-запуск создавал его в служебной Windows-сессии0. Тот же выделенный профиль запущен через Interactive scheduled task в рабочей сессии1; обычный Chrome не затронут. На рабочем столе Windows создан ярлык `Cats Cute Miner - Google Play`, исправлен автозапуск. `Tools/ReleaseSdk/show-agent-chrome.ps1`, `evidence/browser-access-current.json`.
- SuperLeo авторизован как sargsyan@multicastgames.com; перечитаны карточка своей игры, Meta ID и действующий MAX rewarded. Списки AdMob/Unity/Mintegral/Pangle доступны, собственных записей пока нет; MAX содержит AppLovin Network/Exchange без этих mediated networks. `evidence/provider-readback-current.json`.
- Иконка и feature graphic созданы built-in image_gen и экспортированы в точные размеры, промпты/masters сохранены. Новый текст страницы учитывает заводы и62 карточки. `Docs/Store/GooglePlay/`. Четыре геймплейных кадра пересняты и просмотрены, raw1080×1920 + обновлённые по требованиям Console варианты с подписями1080×1920 (игровой viewport990×1760, отдельный заголовок160px); на кадре экипировки — действительные предметы каталога в временном QA-профиле. Архив `Docs/Store/GooglePlay/cats-cute-miner-store-en-US.zip`. Это Editor captures, не снимки Android/планшета. В Console сохранены и перечитаны en-US title/short/full, icon512, feature1024×500 и четыре captioned phone screenshots в порядке crew/upgrades/cards/equipment. Simulation и контакты студии сохранены и перечитаны после reload. `evidence/store-listing-current.json`. Дополнительные tablet/PC/XR assets не назначались. AI asset declaration: текущий UI показывает guidance, checkbox не обнаружен в asset details/review; проверить перед submission.

Фактические ожидания:

1. Google Play и Adjust авторизованы. Приложение Play создано: ID `4974963942177850884`, package `com.multicastgames.catscuteminer`; доступ создания фактически подтверждён, `evidence/play-registration-current.json`. SuperLeo авторизован. MAX авторизован как `develop@multicastgames.com`, но Account → Keys показывает только Report/Ad Review Key: SDK Key и меню управления блоками отсутствуют. Владелец сообщил, что полный доступ должен быть. Повторная проверка исходного dash.applovin.com, меню аккаунта, свежего server GET /o/v1/menu без cache и прямой Ad Units ссылки из официальной документации подтвердила: SDK-key grants отсутствуют, меню блоков отсутствует, сервер iframe управления возвращает302 в отчёты. `evidence/max-permissions-current.json`. Нужна проверка назначения MAX прав администратором; 2FA завершена и больше не является ожиданием. `evidence/browser-access-current.json`.
2. Own Adjust app/events и project binding завершены. Остались MAX account SDK key/mediation и Axon data sharing: у собственного приложения Android Disabled, Edit требует пустое поле SDK keys; диалог просмотрен и отменён без сохранения. `evidence/adjust-axon-current.json`. AppMetrica как недоступный сервис не блокирует.
3. Для детской аудитории отдельный review package подготовлен; владелец повторно дал `/policy`, его §7–8 (хранение по целям и юридическим обязанностям, удаление/анонимизация, запрос через поддержку) внесены в отдельный текст без выдуманного числа дней/лет. §9 исходной policy всё ещё16+, поэтому сам URL не подставляется в игру как действительная детская политика. Применение процесса к детским записям, окончательный список провайдеров и размещение отдельных страниц игры остаются открытыми. `Docs/Legal/CatsCuteMiner/publication-plan.json`, `evidence/legal-review-current.json`.
4. Делегированная задача завершена: Ads/Government/Financial/Health/IARC сохранены и перечитаны; App access ранее сохранён No, но новая форма включает one-time products и требует исправления до review. Factual Yes-инструкции подготовлены, Add требует подтверждения полного доступа к платному содержимому; без такого доступа checkbox не отмечен и исправление не сохранено. IARC: ESRB Everyone, PEGI3, Германия12+, АвстралияMature, Бразилия18+; certificate пока «-». Advertising ID=Yes, Advertising or marketing отдельно сохранено и перечитано. Аудитория6+ выбрана в wizard, но финальное подтверждение детского соответствия не сделано: платные случайные сундуки доступны за покупаемые гемы, для Бразилии нужен отдельный продуктовый/региональный выбор. Владелец выбрал исключить Бразилию из первого выпуска, сохранив механику. Production Countries сохранены и перечитаны: Targeted177, Not targeted1, единственная исключённая строка Brazil. `evidence/countries-current.json`. Финальное подтверждение детского соответствия по-прежнему ожидает действительных документов игры и финального SDK/native/device сравнения. `evidence/console-followup-current.json`, `evidence/regional-ratings-current.json`.
5. На ПК нет подключённого Android и эмулятора/AVD, `evidence/device-environment-current.json`. AAB/internal/Play IAP/opt-in/production пока отсутствуют. Запланирована0.1.1(2), выше локальной0.1.0(1); непосредственно перед сборкой повторно сверить все треки/local manifests. Подписанная финальная сборка ожидает настоящие MAX/mediation и действительные game-specific legal URLs. QA устройства/кабинетов остаётся отдельным этапом после артефакта.

- Internal testers: оба существующих непустых списка License testers (auto-generated),27 и Test,32 сохранены и перечитаны после reload. Сумма не означает59 уникальных людей. License testing проверен отдельно: те же списки уже выбраны, RESPOND_NORMALLY, настройки аккаунта не менял. Track4701668636091023018 неактивен, opt-in пока disabled, AAB/release отсутствует.

Владелец выбрал продолжить выпуск с журналом; защищённая dev-консоль не добавляется.

Ниже сохранён исторический отчёт14.10; его прежние ограничения публикации и статусы регистрации заменяются текущим profile.json и evidence выше.

---

# Подготовка Google Play — 14.10.2026

Пользователь подтвердил Android/iOS ID `com.multicastgames.catscuteminer`, mixed audience с детьми младше 13 лет, нейтральный экран возраста, детский режим без MAX/Meta/рекламы и отдельные документы игры. Приняты шесть расходуемых SKU текущего store-3, точные цены/награды, USD, все доступные страны и автоматические цены Google. Приложения сервисов и backend-регистрация разрешены. Загрузка/выпуск AAB требуют отдельного подтверждения готового артефакта.

## Фактически создано и перечитано

- Проект панели: e4e954f1-59a9-479a-821f-be7eed9aff47.
- Multicast game: gm_cats_cute_miner. Все шесть товаров зарегистрированы и перечитаны. Authenticated guest `/api/products/get` подтвердил Consumable, Google SKU и gems × точное количество, nestedItems=null. Реального платежа пока не было.
- MAX Android rewarded: f27564b55995aa89, активен. Mediation пока не настроен, SDK account key ожидается. Interstitial/banner пока не включены.
- Meta: 5614484342110411, привязка проекта подтверждена; собственный client token получен и подключён к клиентской конфигурации. Meta не смогла верифицировать ещё отсутствующий Play package. Key hashes, тест событий и доставка ожидаются.

## Локальная подготовка

Установлены официальные AppMetrica 6.7.0, Adjust 5.8.1, MAX 8.4.2, Unity IAP 5.2.1/Billing8.3.0, EDM4U1.2.187, Meta18.1.1. Meta binaries доставлены на ПК с проверкой hashes. Первый SDK-only Unity Editor compile прошёл (147с); это не Android player/API/runtime проверка. Новый runtime/детский режим и устойчивая rewarded-выдача внедрены; 22 выбранных PC core tests прошли. SDK release-gate PackageInfo ambiguity исправлена; новое полное Unity подтверждение ещё требуется.

Подготовлены preflight, guarded signed AAB builder и конфигуратор принятой identity/native privacy. На ПК установлены восемь официальных компонентов SDK/NDK/OpenJDK для Unity6000.3.11f1; preflight подтвердил инструменты и API36. Android SDK license acceptance скриптом не выполнялась. Конфигуратор успешно выполнен настоящим Unity: package Android/iOS принят, API36, ARM64, IL2CPP, Meta startup/events/advertising ID выключены, AppMetrica automatic MAX revenue выключен. Evidence: native-project-configuration.json. При начальном импорте EDM ругался на ещё не созданные Gradle templates; шаблоны конфигуратором созданы, настоящее native dependency resolution ещё не проверено. Версия 0.1.0(1) не менялась. AndroidPlayerApiCheck/Mode CheckAndroidApi успешно выполнен настоящим Unity после окончания изменений денежной аналитики: Android player C# SDK/IAP ветки скомпилированы (android-player-api-compile.json, google-play-android-api.log). Это не Gradle/AAB/16KB/device доказательство.

Новый денежный Unity IAP→Multicast→durable ledger flow внедрён; 102 выбранных платежных core tests прошли. Полный прогон ядра завершился 2530 passed/2 failed: только два старых ожидания schema1. Они исправлены на schema2; затронутые OutpostConfigTests/OutpostRewardTests перечитаны и прошли 106/106. Изменение формата задокументировано в ТЗ §17. Полный прогон повторно не выполнялся. Source gates отделены от последующих платежных/device доказательств, чтобы не блокировать саму billing prerequisite сборку. Денежная аналитика подтверждённых новых production покупок реализована и прошла 52/52 выбранных теста, затем настоящий Android player compile. Amount/currency — свежие Google Product metadata перед checkout; отдельные durable markers каждого провайдера, никакого sandbox/restore/replay/детских событий. Не обещает remote exactly-once: ambiguous dispatch не пересылается. Adjust purchase event token ожидает создания собственного приложения/event. Детали: Docs/Reports/google-play-manual-iap-revenue.md. Реальной покупки/доставки событий пока не было.

Локальные Privacy Policy/Terms drafts: Docs/Legal/CatsCuteMiner/. Они не опубликованы и требуют окончательного contact/data/retention/readback. Общие страницы студии остаются 16+ и не используются как утверждённые документы детской игры.

Подготовлен локальный английский текст карточки Docs/Store/GooglePlay/listing.en-US.draft.json: title15, short78, full1104 символа; лимиты сверены с официальной документацией Google, загрузки не было. Требуются реальный Console field inventory/graphics/readback и финальный device UI. ТЗ §2/§10.5/§13 обновлено: Android реальные SDK/покупки, защищённый возраст, точные SKU без бонуса, серверная выдача, настоящие reward callbacks и ledger; тестовый магазин остаётся только в редакторе/браузере. Исторический пункт «SDK пока нет» больше не объявляет scope Android.

## Реальные блокеры доступа

Chrome подключён через установленный chrome-devtools MCP после включения пользователем переключателя отладки. DevToolsActivePort найден, list_pages выполнен, соединение проверено. Пользовательский inspect tab1 сохранён; две вкладки задачи: 2 — Adjust login, 3 — собственные права пользователя в Google Play. В следующем turn сначала list_pages и переиспользование этих вкладок; не создавать дубликаты. Личный Chrome не перезапускался, cookies/credentials не экспортировались.

AppMetrica: прямые .com и .ru кабинеты показывают техническую недоступность сервиса; .ru ссылается на https://yandex.ru/company/news/09-10-2026-01. Panel read работает, функции create в нём нет. Нужен восстановленный штатный кабинет; создание не выполнялось. Adjust и MAX в личном Chrome показывают login, авторизации ещё нет. Adjust automation settings панели по-прежнему Unauthorized; прямой кабинет нужен для собственного USD приложения и event, без чужого template. SDK account key MAX ещё не прочитан.

Google Play: штатный вход подтверждён как sargsyan@multicastgames.com, MULTICAST GAMES, developer8879742639795815518, текущий u/0. Доступны 7 приложений; своей игры среди доступных нет (это не полная проверка всей организации). Create disabled с текстом «Для создания новых приложений нужно разрешение». Собственная карточка прав прочитана: account-level checkboxes unchecked+disabled, изменение требует owner/admin; настройки доступа не изменялись. Нужны account-level read-all и создание/редактирование проектов, затем права собственной игры на listing/IAP, декларации правил, тестовые выпуски и testers. Пользователю задан один pending запрос: войти Adjust/MAX в этом Chrome и получить необходимые права Play от владельца/администратора. Полное evidence: browser-service-access.json. Все ранее принятые разрешения на создание приложений/каталога сохраняются, повторное согласование не нужно.

AdMob apps читаются, но provision завершился upstream connection-closed. После ошибки перечитаны apps и проект: нового app/binding нет; retry не делался, статус исхода неопределён до worker reconciliation.

Не создана карточка Play, не загружен/выпущен AAB, не создан/активирован Play-каталог и не выдан opt-in. AppMetrica/Adjust регистрации, mediation, подпись, native manifest/16KB и реальные device/dashboard/purchase gates остаются открытыми.


## 2026-10-19 — обновлённый AAB проверен на Mac

0.1.2 (3), 77,793,257 байт, SHA256 `13e3712a7f121ac340309432b75a162f799982a1e8dde08e2a836f073ea0c74b`. Подпись проверена guarded builder; legalConfigured=true, localArtifactOnly=false. Исправленная проверка actual DEX class definitions завершилась exit0: Billing8.3.0, обе ARM ABI, ARM64 ELF16KB, без MAX/Google AD_ID, Meta automatic flags=false. Первоначальный substring detector давал ложное срабатывание на optional AppLovin references; добавлены3проверки и все прошли, builder14/14 и Unity16/16. Доказательство: `release/google-play/evidence/aab-0.1.2-3-verification-20261019.json`.

AAB полностью сохранён на Mac: `/Users/arthuri/Unity Projects/Cats-Cute-Miner-main/release/google-play/builds/cat-miners-0.1.2-3-noads.aab`. Копирование через SSH оборвалось; на хабе только `.aab.partial`, готовой ссылки скачивания пока нет. Не пересобирать. Новый AAB не загружен в Google Play, запуск на устройстве и реальные покупки не проверялись.


## 2026-10-20 — запуск без экранов возраста и privacy

По поручению владельца NeutralAgeFlow.Begin сразу продолжает запуск, без создания модального компонента и без обязательного сохранения privacy-файла. Новые/нечитаемые/незавершённые записи получают unknown-age protected defaults: все необязательные провайдеры выключены. Ранее завершённые настройки сохраняются; защищённые возрастные группы и устаревший Ads=true нормализуются безопасно. Ручные Settings → Privacy choices и ссылки на документы доступны.

Проверка: 29/29 PlayMode, отдельная Android player compilation exit0. Доказательство: `release/google-play/evidence/startup-without-prompts-20261020.json`. Новый AAB в этом изменении не собирался и не загружался. Ранее собранный0.1.2(3) остаётся прежним; для следующего выпуска нужны новая сборка и соответствующая правка описания первого запуска в публичных документах.


## 2026-10-20 — новый AAB0.1.3(4), только одна актуальная сборка

По поручению владельца собран `/Users/arthuri/Unity Projects/Cats-Cute-Miner-main/release/google-play/builds/cat-miners-0.1.3-4-noads.aab`, 77792408 байт, SHA256 `8679875cf4d5cba1cd0663790e086b7ca3aa941ee5160ca7423e08ffd9e30a5d`. Полная копия на хабе проверена по тому же hash. Подпись совпала с действующим upload-сертификатом, Billing8.3.0, ARMv7+ARM64 и ARM64 ELF16KB прошли; MAX/Google AD_ID отсутствуют. В release mirror именно проверенная версия NeutralAgeFlow без стартовых экранов.29 PlayMode, Android player compile и23 Python проверки прошли.

Включена `artifact-retention.json`: macOS wrapper сначала проверяет подпись и native manifest/Billing/ABI/ELF, затем сопоставляет SHA/package/code с receipt; только после этого удаляет более старые game AAB/partial в release builds. При неуспехе старые файлы сохраняются; same/newer code и symlinks запрещают очистку. Удалены прежние2AAB и partial на Mac; на хабе также удалена старая Console download копия. В проекте оставлен один AAB0.1.3(4), подписи/ключи и диагностические доказательства сохранены. Доказательство: `release/google-play/evidence/aab-0.1.3-4-complete-20261020.json`.

Новый AAB не загружался и не выпускался в Google Play. До store release остаются проверка свободного versionCode, соответствующее описание первого запуска в публичных документах и runtime/device проверки.


## 2026-10-20 — владелец сообщил об отправке AAB на review

Владелец сообщил: «залил ААВ для релиза и отправил на ревью». Факт записан как owner-reported, без новой записи в Console и без утверждения о проверенных versionCode/track/апруве. Доказательство: `release/google-play/evidence/owner-reported-review-submission-20261020.json`. Новых сборок или повторной отправки не выполнялось. Остаются фактическое состояние review, соответствующее новому запуску описание в публичных документах и device/purchase/provider проверки; у новых unknown-age игроков optional analytics выключена по умолчанию.


## 2026-10-20 — нет AppMetrica данных на новой установке

Владелец сообщил об отсутствии данных/событий; на скриншоте собственное приложение6374386. Проверена privacy-ветка именно AAB0.1.3(4), source hash совпал с build mirror receipt: новая установка получает Unknown, Protected=true и все optional provider flags=false. Initialize выходит до AppMetrica.Activate; ConsentMask=0 блокирует custom events. Текущий ручной экран protected choices также не даёт unknown-age установке добровольно выбрать eligible preferences. Поэтому существующий сценарий не может отправлять optional events новой установки. Это диагноз исходников, не device log или dashboard-delivery proof. Доказательство: `release/google-play/evidence/appmetrica-new-install-diagnosis-20261020.json`. Код/consent/сборка в рамках диагностики не менялись; защита13+/under16/unknown сохраняется.


## 2026-10-20 — AAB0.1.4(5), добровольное включение аналитики в настройках

Собран/подписан/скопирован `/Users/arthuri/Unity Projects/Cats-Cute-Miner-main/release/google-play/builds/cat-miners-0.1.4-5-noads.aab`, 77794998 байт, SHA256 `be10e6b2ae4aaf0518725f51b284e8bcaad218a7a880f7d01caf8d783334b759`. Native manifest/Billing8.3/ARMv7+ARM64/ARM64 ELF16KB без MAX/Google AD_ID прошли; source hashes совпали с проверенными исходниками и release mirror. Старый0.1.3(4)AAB удалён; один актуальный AAB на Mac и хабе.

Новая unknown-age установка может добровольно зайти в Settings → Analytics & privacy и выбрать нейтральную возрастную группу; established age не заменяется.16+ само по себе не включает SDK, независимые переключатели действуют лишь после durable Save choices. Cancel/ошибка записи не применяют draft. До согласия события не backfill; после сохранённого разрешения и при background вызывается AppMetrica.SendEventsBuffer только для уже инициализированного eligible провайдера.54 Unity проверки(36 PlayMode/18 EditMode),23Python и Android player compiler прошли. Собственный AppMetrica key hash совпал с ранее аутентифицированным dashboard приложения6374386.

Фактическое описание startup/settings в Privacy/Terms/Data requests обновлено по сохраняющимся полномочиям владельца;4staticHTML опубликованы штатным publish_site, HTTP200 и точные hashes проверены. Изменения относятся к0.1.3/0.1.4 и сохраняют13+/support/noads/6IAP scope. Доказательства: `release/google-play/evidence/aab-0.1.4-5-complete-20261020.json`, `release/google-play/evidence/legal-publication-20261020.json`.

Фактическая доставка с Android/в кабинет ещё не проверена: после установки обновления нужно явно включить AppMetrica для eligible16+, сохранить, поиграть и свернуть; default без этого остаётся защищённым. Никакого нового upload/review/release или запуска устройства/эмулятора агент не выполнял.

## 2026-10-20 — проверка текущего APK в Android-эмуляторе

По отдельным согласиям владельца установлен изолированный SDK/AVD на Mac. Первая попытка завершилась до Android из-за неполного SDK; после исправления и второго явного согласия Android API35 ARM64 загрузился. Установлен APK, полученный из существующего подписанного AAB0.1.4(5), без пересборки игры. После проверки эмулятор закрыт, повторных автоматических запусков нет.

Через реальный интерфейс успешно заменена существующая protected Unknown запись на допустимую16+ с независимым AppMetrica=true и остальными провайдерами=false. Файл сохраняется после холодного перезапуска приложения. Нативная конфигурация содержит правильный ключ и data_sending_enabled=true, sessions_auto_tracking_enabled=true. В SDK переданы consent_chosen, currency_earned, store_opened и два time_spent; после перезапуска — session_started, game_loaded, mine_started. Ошибок вызова AppMetrica и падений в проверке не наблюдалось. Покупки этим тестом не проверялись.

Последний readback кабинета6374386 в16:50UTC: Events/Audience/Engagement остаются нулевыми, HTTP200 без ошибок, app ID и дата запроса проверены. Пустая нативная очередь и report_request_id=10 не доказывают accepted: SDKv7.12.0 удаляет записи также при HTTP400. Фактический код ответа ingestion не зафиксирован. Ограниченный режим и задержки AppMetrica подтверждены официальной страницей, но причина конкретного случая ещё не установлена; успешную серверную доставку не отмечать PASS.

Доказательство: `release/google-play/evidence/appmetrica-emulator-audit-complete-20261020.json`. Локально подготовлен, никому не отправлен `release/google-play/evidence/appmetrica-support-draft-20261020.txt`. Воспроизводимая ошибка интеграции игры не найдена; исходники игры и релизный AAB не изменены. Следующая проверка — серверный приём/отклонение запросов либо диагностический runtime с прямым наблюдением ответа SDK.

## AppMetrica: диагностический APK — 20.10.2026

Добавлены безопасные логи MC-AM на инициализацию, Java-проверку активации, ReportEvent и SendEventsBuffer. Отдельный локальный Development APK 0.1.4 (5) содержит read-only консоль AM logs, фильтр/копирование и разбор нативного Event sent. Подробные нативные логи включены только для диагностического флага; Play AAB защищён от этого флага. Ключ повторно совпал с SDK-полем приложения 6374386 и проверен внутри APK. 46 PlayMode-проверок и компиляция Android API без диагностического флага прошли. Исходный релизный AAB не изменён.

Новый APK собран, подписан debug-ключом, проверен. Runtime/визуальная проверка и подтверждение нативного приёма сервером пока pending: требуется отдельное согласие на новый запуск эмулятора. Receipt: `release/google-play/evidence/appmetrica-diagnostic-prelaunch-20261020.json`. Инструкция: `Tools/GooglePlay/APPMETRICA_DIAGNOSTICS.md`.

## AppMetrica 0.1.5 (6): автоматический запуск — 20.10.2026

В проверенной 0.1.4 нативный SDK подтвердил приём событий, включая 3 time_spent; данные появились в кабинете (на 17:26 UTC: 376 событий, 9 типов, 2 пользователя). Консоль и фильтр проверены, эмулятор закрыт. Receipt: `release/google-play/evidence/appmetrica-diagnostic-complete-20261020.json`.

По новому поручению владельца в 0.1.5 базовая AppMetrica запускается автоматически без открытия Privacy Policy, выбора возраста или ручного включения. Сохранены явное отключение, старые сохранённые отказы и отдельные возрастные/optional-гейты остальных SDK и денежного revenue. Рекламные идентификаторы отключаются до Activate; location/app-open/crash/automatic revenue выключены. Политика, условия и страница запросов обновлены и опубликованы; live HTML совпал с исходниками. 96 проверок и компиляция production Android API прошли.

APK и подписанный AAB 0.1.5 (6) собраны. AAB прошёл manifest/Billing/ARMv7+ARM64/16 KB ELF/подпись; старый AAB удалён, в папке builds один новый. Runtime чистой установки с автоматическим запуском пока pending: запрошено отдельное согласие на новый запуск эмулятора. В Google Play не загружено. Receipt: `release/google-play/evidence/appmetrica-automatic-prelaunch-20261020.json`.

## AppMetrica 0.1.5 (6): чистая установка проверена — 20.10.2026

После отдельного согласия владельца выполнен один запуск эмулятора на Mac и чистая установка диагностического APK 0.1.5 (6). До открытия настроек или Privacy Policy отсутствовал файл выбора пользователя; AppMetrica активировалась автоматически. Нативный SDK подтвердил принятый сервером ответ для session_started, game_loaded, mine_started, chest_opened, currency_earned и time_spent. Первый time_spent передан после минуты активной игры. consent_chosen на автоматическом старте не отправлялся. Ключ совпал с проверенным SDK-полем приложения6374386.

В реальном интерфейсе дополнительно сохранено отключение AppMetrica без выбора возраста. После холодного перезапуска приложения отключение сохранилось: инициализаций и передач событий в SDK нет. Эмулятор после проверки закрыт, повторного запуска VM не было.

Events/Audience/Engagement кабинета6374386 на17:58UTC содержат данные:411 событий,10 типов,3 пользователя,5 сессий. Эти итоги включают предыдущие QA-прогоны; отдельного фильтра по версии0.1.5 не применялось. Серверный приём событий новой установки подтверждён собственными нативными логами. Production AAB в этом прогоне не запускался; проверки покупок не выполнялись.

96 Unity проверок пройдены ранее. После QA повторно совпали SHA256 AAB и проверенных исходников. В builds по-прежнему только `cat-miners-0.1.5-6-noads.aab`; загрузка в Play не выполнялась. Receipt: `release/google-play/evidence/appmetrica-automatic-complete-20261020.json`.

## AppMetrica 0.1.6 (7): минуты, улучшения, товары и источники сундуков — 20.10.2026

Добавлено `playtime_minutes`: одна запись с числовым minutes=1 за полную минуту активной игры. Прежний секундный time_spent сохранён только для optional-получателей, чтобы не дублировать новый AppMetrica тик. `card_upgraded` передаёт count=1, card_id и уровни до/после; `goods_sold` — точное long quantity проданных единиц, отдельно от золота.

Сундуки разделены на `store_chest_opened` и `arena_chest_opened`. Учебная карта — отдельный `tutorial_card_granted`; копии карт в сундуке не увеличивают число открытий. Сундуки офлайна/тайм-скипа передаются агрегатом count=N только после успешного сохранения результата. Для общего числа проданных единиц нужно суммировать quantity, для сундуков — count. Обычная колонка числа событий не суммирует эти параметры. Правила и API-метрики: `Docs/Reports/APPMETRICA_GAMEPLAY_METRICS.md`.

75 PlayMode +4 EditMode проверок и компиляция production Android API прошли. Подписанный AAB0.1.6(7), SHA256 `b4bdf167d2bd4f4ade6dc271d94a3ebc67beb06181a794f6585aac4f191f8139`, 77811837 байт: manifest/Billing8.3/ARMv7+ARM64/16 KB ELF/подпись прошли; новые строки и поля присутствуют в фактическом metadata артефакта. Источники совпали с build mirror. Старый0.1.5 AAB удалён после проверки, на Mac и хабе остался только новый0.1.6.

Автоматический запуск SDK подтверждён предыдущим прогоном0.1.5; доставку новых метрик0.1.6 на Android и в кабинет пока не проверяли. Новый эмулятор не запускался, версия в Console не проверялась, загрузка в Play не выполнялась. Receipt: `release/google-play/evidence/appmetrica-gameplay-metrics-20261020.json`.


## Шахта 7: около 6 часов с обычными улучшениями; AAB 0.1.7 (8) — 20.10.2026

Владелец уточнил: около 6 часов должна занимать вся середина шахты 7 с обычными улучшениями. Добавлен локальный профиль b7: строки 10–16, 59 целей r/x/s, включая 4 завода, HP ×225 без умножения груза, монет и цен улучшений. Начало/финал и остальные шахты этой локальной настройкой не изменены. Для шахты 7 разрешены 6 часов офлайна; другие сохраняют 3 часа. Уже начатые сохранённые шахты сохраняют свой снимок баланса.

В настоящей сцене CoreGame при SimSpeed=100 середина пройдена за 6 ч 12 мин 55 с, вся шахта — за 6 ч 13 мин 37 с. Использован заработанный профиль после 1–6, без платных ускорителей. Полная событийная Core-кампания ботом C завершила 144/144 шахты без ошибок, включая save/reload, переходы и инварианты; вся шахта 7 — 5 ч 55 мин 50 с. Вся кампания не рендерилась в Unity на ×100. На раннем выходе 6 часов офлайна расчистили 20/59 целей; владелец подтвердил, что полного пассивного прохождения не требуется. Повторный UTC не дублирует награду; часовой тайм-скип списывает 350 QA-гемов. Реальная денежная транзакция не тестировалась.

111 EditMode +65 PlayMode проверок и production Android API compilation прошли. AAB0.1.7(8), 77809882 байт, SHA256 `ee6057fb40408ac6b3653e94d08c53a94b22919e4c2a7f43a47f19eaaf6b5059`: подпись, manifest, Billing8.3, ARMv7+ARM64, ARM64 ELF16KB без MAX/AD_ID прошли. Новая версия баланса и прежние имена gameplay-метрик найдены в фактическом IL2CPP metadata; исходники совпали с build mirror. Старый AAB0.1.6 удалён после проверки; на Mac и хабе один актуальный AAB.

Эмулятор и устройство в этом обновлении не запускались. Доставка новых gameplay-метрик на Android и в кабинет остаётся pending. Console не проверялась, AAB не загружался. Доказательство: `release/google-play/evidence/mine7-six-hour-balance-20261020.json`; подробности: `Docs/Rework/MINE7_SIX_HOUR_PACING.md`.
