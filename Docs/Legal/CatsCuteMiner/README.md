# Документы Cat Miners: Idle Tycoon

Документы приняты по поручению владельца19.10.2026; актуальная фактическая редакция опубликована20.10.2026 для `com.multicastgames.catscuteminer`, аудитории13+, первого релиза без рекламы и6 расходуемых покупок.

- Privacy Policy: https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/privacy/
- Terms of Use: https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/terms/
- Запросы на доступ/исправление/удаление: https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/delete-data/

Финальные исходники: `privacy-policy.md`, `terms-of-use.md`, `delete-data.md`. Действующий receipt и SHA-256: `adoption-current.json`; исходное подтверждение владельца: `adoption-20261019.json`. Ответственный за support@multicastgames.com — владелец проекта; он подтвердил работающую почту и доступ к исполнению запросов. Внутренний регламент: `SUPPORT_PROCESS.md`. Реальный кейс удаления при подготовке документов не выполнялся.

Сайт —4 статических HTML, без трекеров, форм и приватных файлов. Текущая публикация/HTTP200/точные hashes: `release/google-play/evidence/legal-publication-20261020.json`. Предыдущая редакция сохранена в `history/20261019/`. `public-site/` опубликован штатным publish_site; HTTP200 и точные hashes каждого документа подтверждены в `release/google-play/evidence/legal-publication-20261019.json`. Студийный multicastgames.com не менялся. В Play Console новая privacy URL сохранена и подтверждена после reload; отдельная отправка на review не выполнялась.

Файлы `*.draft.md`, `site/`, review ZIP и `publication-plan.json` — историческая review-версия. Актуальный manifest — `publication-release.json`. Команда `python3 Tools/GooglePlay/build-legal-site.py --release` рендерит финальные страницы только при совпадении источников с receipt; сама не публикует.

В исходниках установлены опубликованные privacy/terms URLs и точный allowlist этих страниц. Подписанный AAB0.1.2(3) собран и проверен на Mac: Billing8.3.0, обе ARM ABI, ARM64 ELF16KB, без MAX/Google AD_ID. Доказательство: `release/google-play/evidence/aab-0.1.2-3-verification-20261019.json`. Передача полной копии на хаб ранее оборвалась. Проверка покупок и доставки аналитики на устройстве остаётся отдельным шагом.

20.10.2026 по поручению владельца в исходниках убраны автоматические экраны выбора возраста и privacy при запуске. Новые/повреждённые/незавершённые записи получают unknown-age protection с выключенными необязательными провайдерами; прежние завершённые настройки сохраняются. Ручные настройки privacy и ссылки на документы остаются.29 PlayMode проверок прошли, Android player compilation — exit0. Это изменение ещё не входит в AAB0.1.2(3). До следующего выпуска нужно согласовать описание первого запуска в Privacy Policy/Terms с новым поведением и опубликовать соответствующую редакцию; опубликованные19.10 документы здесь не перезаписывались.

Обновление20.10: AAB0.1.3(4) уже собран с этим изменением, подписан и прошёл native проверки. Полная копия доступна на Mac и хабе, SHA256 `8679875cf4d5cba1cd0663790e086b7ca3aa941ee5160ca7423e08ffd9e30a5d`. По поручению владельца прежние локальные AAB удалены; в release/google-play/builds остаётся один актуальный AAB. Доказательство: `release/google-play/evidence/aab-0.1.3-4-complete-20261020.json`. В Google Play новая версия не загружалась; правка публичного описания первого запуска остаётся шагом до store release.


Редакция20.10 описывает немедленный защищённый старт с0.1.3 и добровольный путь Settings → Analytics & privacy с0.1.4. Возраст13+, защита under16/unknown, поддержка,6IAP и отсутствие рекламы сохранены. Никакое изменение возрастной группы само по себе не включает аналитику; нужны отдельные переключатели и сохранение.36 PlayMode и18 EditMode проверок прошли, Android player compilation — exit0.
