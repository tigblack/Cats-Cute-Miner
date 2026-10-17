# Документы Cat Miner: Idle Tycoon

Владелец повторно подтвердил 16.10.2026 смешанную аудиторию с детьми младше 13 лет и отдельные документы игры. Этот выбор имеет приоритет над правилом 16+ скилла публикации. Контакты и общие страницы студии не менялись.

Подготовлены английские Privacy Policy и Terms, статические HTML-страницы и архив `cats-cute-miner-legal-review.zip`. Тексты описывают действительный клиентский код: защищённый режим до 16 лет и при неизвестном возрасте, раздельные согласия, передачу оригинального Google Play чека серверу Multicast, восстановление незавершённых покупок, сохранение платёжных записей и архивов при сбросе прогресса. Проверка доступа к покупкам не названа проверенным родительским согласием. В игре обе ссылки доступны до выбора возраста и в настройках защищённого режима, когда действительные URL настроены.

Страницы пока помечены **Review copy**, не опубликованы и не внесены в runtime config как действующие документы. На них нет JS, форм, cookies или трекеров. Проверены отображение на настольном и узком мобильном экране и внутренние ссылки. Текущие хеши и предложенные, ещё не проверенные публичные адреса находятся в `publication-plan.json`:

- `https://multicastgames.com/cats-cute-miner/privacy/`
- `https://multicastgames.com/cats-cute-miner/terms/`

16.10.2026 владелец повторно предоставил https://multicastgames.com/policy как источник правил студии. Разделы 7–8 подтверждают хранение по целям обработки и юридическим/налоговым/учётным обязанностям, последующее удаление/анонимизацию и обращения через поддержку. Фиксированный срок в днях или годах там не указан и в текст игры не добавлялся. Общий возрастной пункт 16+ в отдельную политику игры не переносится.

Для принятия и размещения текстов остаются фактические сведения со стороны студии:

- Кто обрабатывает обращения родителей через `support@multicastgames.com`, как находит гостевые платёжные записи и выполняет удаление у Multicast и получателей данных. В проверенном клиенте автоматического endpoint удаления нет.
- Применение опубликованных студией критериев хранения к детским платёжным записям и исполнение родительского запроса. Студийная policy описывает сервисы 16+; одной ссылки недостаточно для проверки работы нового детского процесса на сервере.
- Окончательный список активных SDK и рекламных сетей после настройки сервисов. Настройка для взрослых не снимает защиту детей.

Поручение продолжать выпуск сохраняется. Повторного разрешения на техническую подготовку не требуется; неизвестные сведения о реальной обработке данных нельзя заменить предположением. Нужен доступ к размещению на сайте студии либо ответственный за эти две страницы. После подтверждения этих фактов необходимо установить дату действия, убрать метки review, разместить окончательные страницы, проверить публичные HTTPS-адреса и записать их в игру и Console. Затем сверить поведение итогового Android-артефакта, включая native SDK initialization.

Сборка локального пакета: `python3 Tools/GooglePlay/build-legal-site.py` (нужен `markdown-it-py`). Исходники — `privacy-policy.draft.md`, `terms-of-use.draft.md`; HTML генерируется в `site/`.

Источники конфликта и требований:

- https://multicastgames.com/policy — §9: общая политика студии рассчитана на 16+; §7–8: критерии хранения и контакт для запросов.
- https://multicastgames.com/termsofuse — §1: общие условия рассчитаны на 16+.
- https://support.google.com/googleplay/android-developer/answer/11043825?hl=en — Families, нейтральный возрастной экран и данные детей.
- https://support.google.com/googleplay/android-developer/answer/9867159?hl=en — требования Families.
- https://www.ftc.gov/business-guidance/resources/childrens-online-privacy-protection-rule-six-step-compliance-plan-your-business — детские данные, хранение и удаление.

Проверка исходников: `NeutralAgeFlow`, `GameSdkRuntime`, `PrivateGuestSession`, `AuthenticatedPaymentBackend`, `PaymentDelivery`, `GooglePaymentsRuntime`, `ParentPurchaseGate`, `OutpostController.Save`. Проверка Android player C# после правки ссылок прошла 16.10.2026; это не подтверждение native/Gradle, устройства или серверного удаления.

Release decision 16 October 2026: user deferred MAX. The first Android release excludes MAX and ads for all ages; draft documents reflect this. Brazil remains excluded from public distribution; paid gem chest mechanics remain unchanged. Existing service registrations are retained for a possible future release, not activated or deleted.
