# Публикация Cat Miners в браузере

Репозиторий вебверсии: https://github.com/tigblack/Cat-Miners.
Адрес игры после публикации: https://tigblack.github.io/Cat-Miners/.

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
