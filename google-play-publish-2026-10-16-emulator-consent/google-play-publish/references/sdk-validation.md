# Проверка интеграции при практическом запуске

Сначала карта функций и связей [sdk-interactions.md](sdk-interactions.md); для полного аудита обязательны все применимые строки карты, а не только сервис с жалобой. Этот файл задаёт runtime/evidence gates, карта — конкретные контракты и стыки, которые нужно пройти до объявления интеграции завершённой.

## Функциональные gates SDK (Unity / Godot / web-обёртка)

Этот контракт обязателен независимо от движка. Имена API ниже обозначают функции SDK; реализацию выбирай по установленной версии и платформе, детали движка читай по необходимости в [engines.md](engines.md). Не переносить Unity-only API в Godot/web; провайдер в веб-обёртке должен реально подключаться к Android runtime.

Для каждого сервиса фиксируй отдельно наличие SDK/адаптера, compile/native packaging, собственную конфигурацию, успешную runtime инициализацию, вызовы реальной функции и получение сервером. Evidence: package, versionName/versionCode, SHA-256 установленного артефакта, устройство, время, production/явный QA, результат/ссылка на sanitized log и кабинетный readback. `unknown`, `pending`, `failed`, отсутствие устройства/кабинета не закрывают применимый gate. Проверь только включённые функции и изменённые критические lifecycles; открытый пункт имеет один конкретный следующий шаг, без бесконечного общего аудита.

| SDK и назначение | Условия готовности и доказательства |
| --- | --- |
| AppMetrica — игровая аналитика и денежный доход (основная аналитика; при сбое серверов SDK и события всё равно внедряются, доставка — `waiting`, не блокер, [analytics.md](analytics.md)) | Собственный SDK API key привязан к нужному приложению/package; реальный bootstrap активирует SDK, а не просто хранит config. На установленном билде стартовое и репрезентативное игровое custom событие отправлены с правильным JSON/context и реально получены этим приложением в кабинете/export. Очередь/offline/relaunch не ломают игру; consent соблюдён. |
| Adjust — attribution, sessions и ILRD | Собственный app token/package, event tokens и custom events по [analytics.md](analytics.md) (`reward_granted`, `time_spent`, `purchase` и др. приходят в кабинет), production environment итогового release (internal тоже), реальный успешный init и session, полученная attribution в нужном приложении/окружении. MAX ILRD приходит из настоящего revenue callback: сумма/валюта, format/network/unit/placement соответствуют показу и получению Adjust. Init-log без session/attribution или тестовый replay без реального ILRD не завершает сервис; недоступную attribution-проверку оставь pending. |
| Facebook/Meta App Events — активация и выбранные продуктовые события | Своя зарегистрированная Meta app, Android package и launcher activity, корректные key hashes сертификата установленного билда (для Play — App Signing, для явно выбранного QA — его сертификат), App ID/client token wiring в итоговом native config. Consent-aware Init успешно завершается, затем ActivateApp на корректном lifecycle и выбранные реальные события. Проверь manifest auto-init/auto-events/advertiser collection и flags, чтобы они не обходили consent и не дублировали ручную отправку. Реальное получение activation и выбранных событий проверяется на сервере/в Test Events либо другом доступном App Events отчёте именно этой app; один локальный LogEvent не доказательство. Meta Audience Network — рекламный adapter, он не заменяет App Events. |
| AppLovin MAX — mediation и rewarded | Правильные собственные app/package, account SDK key и ad unit IDs/форматы/placements; resolved adapters и настройки включённых сетей. Успешный initialization callback и фактический native privacy/CMP result, а не вызов Initialize или наличие URL. По этому результату применены реальные privacy settings без forced grants. Ready/load/show/reward/error/close/revenue проходят нижеописанный lifecycle на устройстве. |
| Multicast IAP — серверное подтверждение и доставка покупки | Обязателен для денежных товаров: registration/readback → authenticated runtime catalog/order → настоящий receipt → server validation → точная durable награда/ledger → claim/confirmation/recovery по [server-iap.md](server-iap.md). Отдельно реальная license-test транзакция; установленный IAP SDK, SKU, локальная RSA, mock success или negative server response не закрывают этот gate. |

### Один денежный источник

Для **каждого получателя** запиши источник специализированного ad и IAP revenue: auto SDK/adapter, manual client либо backend. На один impression/проверенную новую покупку работает только один выбранный источник; отключи конкурирующие auto/manual/backend маршруты, проверь реальные подписки итоговой сборки. Custom `ad_revenue`/`purchase` описывают игровые действия и не заменяют денежные API. Используй реальную сумму/ISO currency, callback/transaction correlation; restore, sandbox, replay и отказ не создают новую продажу. Отдельно доказать отсутствие дублей и фактическое получение. Без удалённой idempotency не обещай сетевой exactly-once: укажи проверенную границу, не теряй отправку ранним `sent` marker.

### MAX: включённые форматы и rewarded lifecycle

- Rewarded обязателен. Interstitial/banner **необязательны для релиза**: явно исключённые/отключённые пользователем форматы не требуют IDs, load/show/QA и не блокируют готовность. Проверяй отсутствие их placement initialization, create/preload/retry/autorefresh/show и фоновых вызовов из bootstrap/таймеров; наличие adapter для rewarded допустимо. Включённые optional форматы требуют собственного lifecycle/revenue gate. Не включай их снова из-за старой конфигурации или эталона Devour.
- После успешного init/native privacy result загрузка даёт наблюдаемые ready/load/error состояния. Show допускается только при SDK readiness и подходящем игровом контексте. До Show сохрани контекст конкретной попытки: unit/placement, correlation, согласованная награда/получатель; повторное нажатие не создаёт параллельного показа или двойной подписки.
- Display, hidden/close, display failure, revenue и reward — разные факты. Успешный просмотр (`watched` в контракте Devour) подтверждается реальным SDK reward callback; закрытие без него имеет отдельное фактическое состояние/событие, не `watched`. Согласуй форму событий текущей игры, не выдавай придуманные имена за уже отправленные или полученные. Получение reward и успешное сохранение награды тоже фиксируй отдельно.
- Награда выдаётся только от реального коррелированного reward callback. Изменение игрового wallet/entitlement и marker выдачи сохраняются атомарно и однократно; duplicate callback, повторное открытие UI и restart не дают второй награды. Close, таймер, debug button, display/revenue callback не выдают награду.
- Проверь долгую рекламу, уход в background, возврат/смену сцены и callback после закрытия UI. Gameplay/input/audio pause восстанавливаются по реальному fullscreen/app lifecycle; watchdog снимает зависшее UI состояние по контракту, но не объявляет просмотр успешным и не стирает ожидающий reward. Контекст завершённого показа сохраняется до безопасной обработки позднего callback с ограниченным retention; callback старого показа не назначается новому по одному unit ID.
- Поздний валидный reward обрабатывается тем же durable pipeline даже после close; предварительное close-without-reward наблюдение получает коррелированное уточнение, без второго successful-view события/гранта. При save failure грант остаётся pending и повторяется безопасно. Не обещай восстановить callback после убийства процесса, если SDK/сохранение этого не обеспечивают; фиксируй ограничение и проверенный результат. No-fill/load/display error/close без reward завершают loading, не дают награду, retry ограничен. Ошибка analytics sink не ломает выдачу или остальные sinks.

### Диагностика и явный QA

Debug console полезна как источник читаемых sanitized lifecycle logs: время, SDK/этап, init/consent status, format/unit/placement, безопасная correlation, reward observed/saved/pending, error code, очередь/отправка. Не выводи SDK/access/client tokens, purchase tokens, receipts, auth headers, идентификаторы рекламного устройства или личные данные. Лог показывает наблюдение/попытку, кабинет показывает получение; UI не создаёт доказательство сам.

Developer QA не переключает молча live SDK environments/consent, не регистрирует устройство автоматически в MAX test devices и не имитирует reward в реальном wallet/телеметрии. Production и обычный internal используют live IDs/production config; явно запрошенный QA — отдельный build/device mode с подтверждением native test mode. Изолированный fixture проверяет обработчик, но не изображает successful SDK callback, покупку или доставку в live сервис. При неизвестном результате сохраняй pending и следующий шаг.

## Подписи API и живое подключение

Проверяй адаптеры с активными Android/SDK compilation symbols против реально выбранных assemblies/пакетов. Компиляция файла, содержимое которого вырезано `#if`, не подтверждает совместимость. Компиляция против кешированных SDK Devour полезна для проверки API, но отдельно требуется импорт этих SDK в целевой проект, Unity compile, Android build и работа bootstrap/callbacks.

Если SDK отсутствует, можно подготовить код/проектные настройки независимо от доступа к кабинетам. При этом статус остаётся подготовкой: выключенный флаг, незаполненный config, невызываемый bridge и сборка одной библиотеки не подтверждают внедрение в игру. Не отмечай весь сервис готовым, если обязательный формат или revenue callback оставлен пустым. В trial можно явно оставить непроверенную часть, но она остаётся обязательной работой релиза.

## Unity: воспроизводимый compile на ПК

Для промежуточной проверки Unity API при занятом редакторе можно использовать его Roslyn compiler. Определи пути редактора и реальных SDK assemblies на ПК, подготовь response file: `/nostdlib+`, `/target:library`, активные defines проекта и `UNITY_ANDROID`, выходная DLL и явные `/reference:` для `Data/NetStandard/ref/2.1.0`, `Data/Managed/UnityEngine` и выбранных SDK. Meta legacy assembly в проверенном Devour также потребовала `Data/NetStandard/compat/2.1.0/shims/netfx`. Это рецепт для данного API profile, не универсальный набор библиотек всех версий Unity.

Запуск: `dotnet <editor>/Data/DotNetSdkRoslyn/csc.dll @<response-file>`. Сохрани exit code, warnings, активные defines и список выбранных assemblies. Editor-скомпилированный MAX наследует `MaxSdkUnityEditor`: define в компилируемом bridge не перекомпилирует reference DLL под Android. Такой результат полезен для сигнатур; native platform API, resolver и release отдельно проверяются полноценной Unity Android сборкой. Deprecated методы исправляй по текущему Integration Manager/API, предупреждение не скрывай.

До запуска dotnet в изолированной проверке задай `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` и task-specific `DOTNET_CLI_HOME`/`NUGET_PACKAGES`. Для теста без package dependencies не добавляй сетевые feeds. Подтверди наличие toolchain inputs именно на ПК: синхронизация может отфильтровывать `.csproj` и бинарники. Служебные inputs создавай в изолированном дереве на ПК, синхронизируемые игровые исходники меняй локально. Тестовый scratch не должен конкурировать с sync; отказ файловой записи — проверяемый сбой, не доказательство Android persistence.

До Android build проверь существование выбранных сцен и используемых assets/native plugins **на build-машине**, включая сцены из custom build entry point. Текстовый snapshot без сцены годится для compile, но не для выпуска.

## Реальное согласие

Целевая аудитория и общие ссылки/контакты уже заданы в [правилах студии](../SKILL.md#правила-студии): все игры 16+, не детские. Регионы и существующий CMP/consent flow выведи из проекта и принятых решений; неизвестные региональные решения оформи конкретным предложением. Проверь pre-init поведение всех SDK: Android manifest providers, Meta auto-init/auto App Events/advertiser ID flags, AppMetrica collection, Adjust и privacy-настройки MAX/адаптеров по официальным контрактам выбранных версий. `SetHasUserConsent(false)` одной сети не управляет остальными SDK и не доказывает прекращение сбора.

Согласие, отказ и изменение должны действительно распространяться на соответствующие сервисы, сохраняться и применяться после restart до событий, которым требуется этот выбор. Не подставляй forced grant; не подменяй CMP простым boolean, если выбранный SDK/региональный поток требует иной информации. SDK initialization сам по себе не служит доказательством корректного consent flow.

## AppMetrica Unity 6.7 + MAX

Проверено по исходнику установленного plugin Devour, пути относительно пакета `io.appmetrica.analytics`:

- `Editor/AppMetricaSettingsWindow.cs`: настройки **Enable Auto Detection Of Features**, **AdRevenue AutoCollection Networks → AppLovin**, **Apply**.
- `Editor/Features/AppLovinAdRevenueV8.cs`: feature define `APPMETRICA_FEATURES_ADREVENUE_APPLOVIN_V8`, автоматическое включение при наличии MaxSdk.
- `Runtime/Features/AdRevenue/AppLovin/V8/AppLovinAdRevenueHandler.cs`: before-scene подписки на revenue callbacks rewarded, interstitial, banner и другие форматы. Вызывает `ReportAutoCollectedAdRevenue`, передаёт unit/network/placement/precision/type и сумму USD.

Практический выбор:

- **AppMetrica automatic:** сохраняй модуль авто-сбора и убирай ручную специализированную отправку AppMetrica из своего handler. Custom `ad_revenue` и отправка Adjust выполняются своим обработчиком.
- **AppMetrica manual:** через штатные настройки плагина отключи соответствующий авто-модуль и убедись после resolver/compile, что он не подписан в итоговой сборке. Автообнаружение может включить его снова: проверить только один flag без состояния resolver недостаточно. Не правь файлы Library/PackageCache как постоянный способ настройки.

`RevenueAutoTrackingEnabled=false` не решает этот выбор. Для другой версии плагина заново проверь её источник/API. По каждому включённому формату подтверди выбранный маршрут; отсутствие устойчивого impression ID не является основанием молча отказаться от banner revenue. Используй один callback/автоисточник, соответствующую correlation или штатную нативную реализацию и запиши границы доказанной дедупликации.

## Награды, доход и ошибки

Грант награды привязан к успешному reward callback и контексту конкретного показа. Show/close и расчёт денежного дохода имеют отдельные состояния. Не стирай контекст успешной награды только потому, что UI уже закрыт, не выдавай награду от revenue callback и не выдавай второй раз после повторного delivery. Проверяй порядок callbacks, которым действительно обязан следовать выбранный SDK, и повтор/restart сценарии по игровой модели сохранения. [MAX callback reference](https://support.applovin.com/en/max/unity/ad-formats/rewarded-ads).

Награда и marker выдачи должны сохраняться согласованно с настоящим wallet/save игры. Отдельный тестовый ledger, не связанный с ним, подтверждает алгоритм прототипа, а не игровую атомарность. Проверка сбоя сохранения должна подтверждать отсутствие ранней confirmation/потери возможности повторно обработать покупку.

Не сохраняй `sent=true` перед неуспешной денежной отправкой только ради прохождения теста «без дублей»: это может молча терять revenue. Однократная попытка отправки не является гарантией доставки. Учитывай штатную очередь SDK и семантику ошибок, изолируй sinks друг от друга, а дополнительную собственную очередь делай только с определённой моделью retry/idempotency. Не обещай exactly-once между процессом игры и удалённым сервисом без поддержки устойчивых ID/серверной дедупликации. Не дедуплицируй разные показы только по одинаковой сумме, creative или ad unit.

Для каждого включённого формата опиши, чем коррелируются показ и callbacks, сколько живёт его контекст и как обрабатываются поздние/повторные callbacks. В MAX 8.4.2 проверенный `AdInfo` не содержит универсального impression ID; creative/сумма/unit не заменяют его. Fullscreen local show ID применим только с подтверждённой привязкой callbacks, banner имеет отдельный lifecycle. При отсутствии server idempotency запиши предел гарантии; доход всех включённых форматов должен отправляться выбранным штатным callback/модулем.

События сохраняют форму согласованного контракта. Например, Devour `rewarded` имеет payload `{"loaded":""}`/`{"watched":"placement"}`; общий `{"state":"loaded"}` — изменение аналитического контракта, его надо явно документировать/совместить с потребителями. Проверяй shape/context на реальных точках вызова, включая startup и игровую прогрессию.

## Обязательная проверка основной игры

Проверь установленные пакеты именно целевого проекта, bootstrap в его стартовой сцене, реальные методы прогрессии/экономики, рекламную кнопку и сохранение награды. Не ограничивайся примером, тестовым bridge или записью ключей. Сначала закончи независимые части даже при отказе регистрации другого сервиса.

Условия показа и награда берутся из согласованных правил текущей игры; параметры Piggy Payday не являются общими правилами студии. Reward callback должен быть единственным источником награды, её ID и изменение сохранения сохраняются атомарно. Закрытие, ошибка, revenue, повтор callback и старый стол проверяются отдельно.

MAX native consent flow настраивается через API настроек установленной версии; Unity 8.6.6 хранит его в `AppLovinInternalSettings`, SDK key — в `AppLovinSettings`. Не выдумывай методы runtime. Общие URL студии: https://multicastgames.com/policy и https://multicastgames.com/termsofuse. Для Google UMP дополнительно проверь фактический app ID и опубликованную CMP message; наличие двух URL не подтверждает готовность CMP.

[Официальный MAX consent flow](https://support.applovin.com/en/max/unity/overview/terms-and-privacy-policy-flow).

## Unity 6: проверенные особенности установки

- MAX Android 13.6.4 требует minSdk 24 (Android 7.0); сверяй minSdk по фактическому AAR/merged manifest, не подменяй `tools:overrideLibrary`.
- Adjust 5.8.1 через UPM: его предпроцессор ожидает default manifest в `Assets/Adjust`. При UPM-установке заранее создай корректный custom `Assets/Plugins/Android/AndroidManifest.xml` из шаблона установленного Unity с выбранным Activity/GameActivity и включи соответствующий PlayerSetting. SDK дополнит разрешения; не копируй старый manifest с `debuggable=true`.
- Meta 18.1.1: проверь import compatibility desktop DLL. Legacy Any flags могут включать x86/x64 Windows плагины в Android. Исправляй через `PluginImporter` API, сохраняя GUID и правильную платформу/CPU.
- Для Android native dependencies используй шаблоны Gradle именно установленного Unity и EDM. При установленной современной SDK не запускай legacy `tools/bin/sdkmanager` с новым JDK; не меняй глобальный JDK на старый только ради resolver.
- Диалоги resolver/build и domain reload могут отложить или отменить `delayCall`. Различай «команда поставлена в очередь» и реально начатую/завершённую сборку. Проверяй активный проект перед каждым изменением; MCP другого открытого проекта не является подходящим target.

Эти наблюдения относятся к протестированным версиям; при обновлении проверяй фактический код/артефакт.

## Unity IAP: проверенные границы Editor и Android

- Unity IAP 5.4.4 включает `SecurityStub` в Editor: его `CrossPlatformValidator` не подтверждает настоящую RSA-проверку. Проверяй подписи против официальной runtime Security assembly/Android; для Editor prebuild валидируй структуру публичного Play RSA ключа и его источник отдельно. Не выключай release guard из-за Editor stub.
- Публичный licensing RSA ключ берётся из **этого** приложения Play Console. Upload certificate, SDK key и MCP credential его не заменяют. Сохрани доказательство package/read-back и хеш публичного ключа; сами чеки и purchase tokens не выводи.
- Версия UPM-обёртки не доказывает native Billing Library. Например, установленная 5.4.4 объявляет Billing 9.0.0 в `IAPAndroidDependencies.cs`; старый Gradle предыдущей сборки всё ещё может содержать 6.2.1. Проверь resolver и dependency нового итогового артефакта.
- При `RevenueAutoTrackingEnabled=false` покупки требуют самостоятельного денежного `ReportRevenue` с **реальной** ценой/ISO currency из магазина, проверенной транзакцией и одним источником доставки. Custom `iap_delivery` не является денежным доходом. Restore и callback replay не создают новую продажу.
- Пауза billing UI должна завершаться при отмене, deferred, ошибке и возврате приложения. Отдельные watchdog/retry нужны для молча зависшего Connect, загрузки товаров и purchases fetch; durable grant/ledger сохраняются до ConfirmPurchase.
- Сохранённый локальный ledger защищает повтор доставки в текущей установке, но не даёт серверную защиту от refund/revoke, переустановки или replay на другом устройстве. Если нужен серверный контракт, используй реальную доступную службу, а не выдуманный endpoint.

Эмулятор на ПК допускается только после отдельного согласия оператора на каждый запуск/перезапуск; он используется для проверки запуска, раскладки, локальных lifecycle logs и предварительного smoke по [emulator.md](emulator.md); не выдавай его за телефон владельца. Финальные показы рекламы, награды, покупки и доставку SDK подтверждай на реальном устройстве с актуальной Play-установкой; без неё эти gates ожидают проверки. Эмулятор без Play Store не проверяет Billing, а локальный mock не заменяет license-test транзакцию. 4 KB эмулятор не подтверждает запуск на 16 KB устройстве: ELF/ZIP проверки остаются отдельными. Не перезапускай личный браузер или занятый Editor ради устройства.

## Проверенные ограничения callbacks и сохранения

- Если игровое сохранение награды не удалось, successful reward callback оставляет ожидающий грант; retry должен получить подтверждение durable save, прежде чем помечать его выданным. Повтор callback и retry не прибавляют награду второй раз. При невозможности записать вообще никакие данные и закрытии процесса memory-only очередь восстановить нельзя; это ограничение фиксируется отдельно.
- Late/unmatched hidden или display-failed не назначай текущему показу только по ID рекламного блока: они могут относиться к предыдущему показу. Используй реальный контекст SDK. Отдельно проверь Android no-fill/display-failure без placement, чтобы интерфейс не зависал.
- Таймер eligibility interstitial хранится отдельно от общего прогресса/QA-clock и считает только согласованное реальное gameplay. Recording/cheats/pause не должны ускорять рекламный порог. Конкретные placement и секунды выбираются по правилам текущей игры.
- Статус видимости баннера меняется после успешного вызова SDK, а не заранее. Ошибка show/hide оставляет возможность retry; layout проверяется на устройстве.
- Development smoke APK допускает независимую проверку установленных SDK при недоступной регистрации провайдера. Final release должен отвергать отсутствующий обязательный token/app ID; не выпускать silent-skip configuration как завершённую интеграцию.
