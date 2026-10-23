# Публикация Cat Miners в браузере

Текущий публичный адрес: **https://sites.aimulticast.org/sargsyan/cat-miners/**.
Имя сайта для следующих обновлений — **`cat-miners`**. Владелец выбрал этот хостинг 20 октября 2026.

Опубликована полная игра **0.1.18** с балансом `outpost-2.2-b8`, сценой `CoreGame` и всей кампанией.
Когда шахта завершена и доступен переход, автоматически открывается «Go to the next mine?» с выбором Stay here / Next mine. Выбор остаться закрывает окно; зелёная кнопка повторно открывает его. [Поведение и проверка](Rework/NEXT_MINE_CHOICE.md).
Приветствие Мии «Thanks for the rescue!» показывается при первом открытии магазина после спасения на две реальные секунды и больше не повторяется, включая перезагрузку игры; портрет остаётся.
Нажатие на предприятие открывает окно его менеджера: копии, нужный уровень автоматизации и цена улучшения в эликсире. После снятия цепей — Activate и полноэкранное открытие сундука. Первые семь шахт гарантируют достаточно копий до требования автоматизации; затем помощь уменьшается. [Правила и проверки](Rework/FACTORY_ACTIVATION.md).
Исправлена цена Hire Cat: отменена скидка 99% в шахтах с финальным завалом, в том числе в уже начатых. В проверке Mine 3 найм с ранга 4 стоит 2354 вместо 24. [Причина и проверки](Rework/HIRING_ECONOMY.md).
Финальный завал теперь 4×2 клетки, собран из родной пиксельной графики; подход шириной 4 клетки содержит два ряда обычных блоков. Рабочие позиции в один соседний ряд, один кот на клетку. После спасения Bruno цель переключается на зачистку всей шахты и доставку груза. [Правила и проверки](Rework/COMPACT_FINAL.md).
Это обычная WebGL Release-сборка, без ограничителя рекламного playable и без меню разработчика.
Mine 1: два ряда камней и короткая правая ветка с досками и завалом, всего 15 разрушаемых целей. Оборудование из родного пиксельного набора размещено в нишах с полом и тенями; закрытые кнопки нижней панели затемнены: крупный золотой замок занимает всю область иконки, подпись остаётся видна. После открытия возвращается обычная иконка. [Проверка оформления](Rework/SCENERY_INTEGRATION.md). В HUD показаны золото, эликсир и гемы; [подробности и проверка](Rework/MIA_CORRIDOR.md). Начатые шахты сохраняют собственную геометрию; новый первый уровень можно посмотреть в приватном окне.

Все семь улучшений Base открыты с начала Mine 1, включая прежние профили — [проверка](Rework/BASE_ALL_OPEN.md). Обновление также включает спасение Bruno, Equipment после Mine 2 и новый обрушенный вход без кристаллов, и обновлённое размещение статичного оборудования вокруг карты; [снимки и проверка](Rework/COLLAPSED_MINE.md).

Прогресс хранится в IndexedDB этого браузера на этом адресе. Сохранения Android автоматически сюда не переносятся.
Реклама, симуляция платных покупок и рекламных наград отключены; покупки за заработанные гемы доступны.
AppMetrica, Adjust, Meta и Google Play Billing в браузере не запускаются: текущие интеграции рассчитаны на Android.

## Текущая сборка и проверка на Mac

Unity **6000.6.4f1**, установленный WebGL Build Support. Из каталога проекта:

```bash
on-remote python3 Tools/Web/build-macos.py --version 0.1.18 --output Builds/Web-large-locks-0118 --reports Docs/Reports/webgl-large-locks-20261022
on-remote node Tools/Web/smoke-macos.cjs --label local --build-dir Builds/Web-large-locks-0118 --reports Docs/Reports/webgl-large-locks-20261022 --all-base-open --mia-two-lane
on-remote node Tools/Web/smoke-macos.cjs --label public --url https://sites.aimulticast.org/sargsyan/cat-miners/ --reports Docs/Reports/webgl-large-locks-20261022 --all-base-open --mia-two-lane
```

Сборка выполняется в `build/web-ci-macos` с собственной Library. Исходный открытый проект и Android AAB не меняются.
Выходной каталог должен быть новым: для следующего обновления задайте другой `--output` и такой же `--build-dir`
локальному smoke-тесту. Предыдущая рабочая сборка сохраняется до проверки новой.

В копии проекта перед сборкой удаляются ключи/токены нативных SDK и отключается `TestPurchases`
в ресурсе магазина и его встроенном резервном JSON. Готовые файлы, включая распакованные gzip,
проверяются на отсутствие исходных SDK-ключей. В публикацию идут только файлы веб-сборки.

Доказательства текущего обновления — `Docs/Reports/webgl-large-locks-20261022/`:
`verified-build.json`, `local-browser.json`, `public-browser.json`, `public-http.json`, `publication.json` и снимки интерфейса.
Локальная проверка запускает настоящий Chromium/WebGL, проверяет движение симуляции, запись IndexedDB,
сохранение профиля и прогресса после перезагрузки и размещение canvas в области 390×844.
Снимки магазина, карточек и настроек дополнительно проверяются визуально.
Размер экрана телефона в Chromium не заменяет испытание на физическом телефоне.

## Текущая публикация

Для обновления используйте штатный `publish_site` с папкой проверенной сборки и именем `cat-miners`.
Передайте с Mac недостающие сгенерированные файлы через `on-remote --fetch`, затем сверьте все SHA-256
с `verified-build.json`. Ключи, приватные файлы, отчёты и исходники не включаются в публичную папку.
После публикации дождитесь готовности `hub-site status cat-miners`, проверьте HTTP и запустите публичный smoke-тест.
Публикация под тем же именем сохраняет адрес игры и пространство сохранений браузера.

## Прежняя публикация на Windows / GitHub Pages

Исторический репозиторий: https://github.com/tigblack/Cat-Miners.
Прежний адрес: https://tigblack.github.io/Cat-Miners/. В этом обновлении он не менялся.
Остальные команды ниже описывают прежний процесс; для текущего сайта используется публикация выше.

## Сборка

Unity 6000.3.11f1 с установленным WebGL Build Support. Из каталога проекта:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode BuildWeb -Target Ci
```

Сборка проходит в изолированной копии `build/ci`, чтобы открытый редактор не мешал работе. Готовые файлы появляются в `Builds/Web`, отчёт — `Docs/Reports/build-web.json`, лог — `Docs/Reports/unity-buildweb.log`.

Сцена — `CoreGame`, сборка Release без инструментов разработчика. Шаблон `Assets/WebGLTemplates/CatMiners/index.html` сохраняет вертикальный формат игры, подстраивается под окно браузера и содержит кнопку полного экрана. Пути файлов относительные, поэтому игра работает в подкаталоге `/Cat-Miners/`.

Для GitHub Pages используется Gzip с Decompression Fallback: файлам не нужны специальные серверные заголовки. Имена файлов содержат хеши, `.nojekyll` отключает преобразование готовых файлов сайта. Отдельный файл должен быть меньше 100 MiB.

`autoSyncPersistentDataPath: true` включает сохранение файлов игры в IndexedDB браузера. Прогресс принадлежит конкретному браузеру и адресу сайта. Запись в IndexedDB асинхронная; нельзя рассчитывать на `OnApplicationQuit` при закрытии вкладки. Игра сохраняется регулярно и после значимых команд.

## Проверка перед публикацией

```powershell
node Tools/Web/serve.cjs
```

Открыть http://localhost:8095/Cat-Miners/. Проверить загрузку, нажатия по камням, окна экипировки и карточек, сохранение прогресса после обновления страницы и изменение размера окна.

Playable (`Builds/Playable`, сборка `Tools/unity.ps1 -Mode BuildPlayable`) открывается тем же сервером с флагом `--playable`: http://localhost:8095/Cat-Miners/playable/ (основная игра остаётся на `/Cat-Miners/`). Для playable файлы `.unityweb`/`.gz` в Gzip отдаются с `Content-Encoding: gzip` и типом по внутреннему расширению (`application/wasm`, `application/javascript`); основная сборка отдаётся как раньше, через Decompression Fallback. `--port <N>` или `CATMINERS_WEB_PORT` меняют порт.

```powershell
node Tools/Web/serve.cjs --playable
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Web/start_serve.ps1 -Playable   # в фоне, лог Docs/Reports/web-serve.log
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Web/check_serve.ps1 -Port 8095 -Paths /Cat-Miners/playable/
```

Сборку playable в фоне (переживает обрыв удалённой сессии) запускает `Tools/Web/start_build_playable.ps1`, ход — `Docs/Reports/build-playable-run.log` (последняя строка `EXIT <код>`).

## GitHub Pages

`Tools/Web/github.ps1` использует GitHub CLI из `Library/WebPublishing/cli/bin/gh.exe`. Данные входа находятся в исключённом из синхронизации каталоге `Library/WebPublishing/auth`; публикация копирует только вебсборку.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Web/github.ps1 auth login --hostname github.com --git-protocol https --web
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Web/Publish-GitHubPages.ps1
```

Необязательный `-MessageFile <файл>` задаёт сообщение коммита (например, со строкой Co-Authored-By); без него — «Publish Cat Miners WebGL build». Скрипт проверяет успешность сборки и доступ к репозиторию, обновляет отдельную рабочую копию `Builds/WebRepository`, отправляет обычный коммит в `main`, включает Pages из `main` → `/(root)` и устанавливает ссылку сайта в About репозитория. Исходный Unity-проект продолжает находиться в рабочем каталоге разработчика.

Отчёт публикации: `Docs/Reports/github-pages-publication.json`. После отправки нужно дождаться завершения публикации GitHub и проверить игру по публичному адресу.

Официальные инструкции: [Unity: развёртывание Web](https://docs.unity3d.com/6000.3/Documentation/Manual/webgl-deploying.html), [Unity: настройка вебшаблона и сохранений](https://docs.unity3d.com/6000.3/Documentation/Manual/web-templates-build-configuration.html), [GitHub: источник публикации Pages](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).
