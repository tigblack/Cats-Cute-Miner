# HTML5 playable студии

## Референс и выбор фрагмента

Студийные примеры находятся **на ПК пользователя**: `Z:\Repos\puzzleplayables`. Прочитай его `AGENTS.md`, `README.md`, `playable_rules.json`, требования выбранных сетей и похожие ручные примеры. Порядок доверия репозитория: требования сети → ручной эталон → Unity Exporter. На снимке 2026-10-09: `PuzzleCity/unity/` — Unity-exported пример; `BubbleSort/build/`, `ScrollPuzzle/BonusLevelVariants/` — CTA/lifecycle; `playable_builder.html` и `scripts/` — генератор (`npm run generate` в scripts). Legacy `pack_playables.ps1` удаляет и пересоздаёт `packed/`: используй собственный выходной каталог проекта, не запускай удаление в студийном референсе. При отсутствии ПК используй контракт ниже, но отметь отсутствие свежей проверки требований сети.

Найди фрагмент, в котором за 15–45 секунд игрок понимает core loop и получает результат. Для Piggy Payday поручен **первый стол**: движение молотком, удары по PocketChange, трещины/осколки, монеты, квота стола. Для другой игры выбери её первый понятный уровень/пазл/бой по осмотру, не копируй механику Piggy. Перенеси реальные ресурсы, палитру/камеру, управление, условия цели. Уменьшение квоты/темпа допустимо для короткого demo, но записывается в manifest; не обещай отсутствующие функции. Playable должен принимать настоящий touch/drag, а не проигрывать видео на фоне кнопки Install.

## Реализация и ресурсы

Сначала оцени проект и уже имеющийся playable/exporter. Сжатый HTML5 перенос минимального core loop часто укладывается лучше полного Unity WebGL player; выбор агента зависит от кода и бюджета. Переиспользуй официальные движки/библиотеки с лицензиями, локально bundle dependencies; не подключай CDN, удалённые fonts или gameplay fetch. В Unity процедурные meshes/sprites/animation можно экспортировать из Editor без изменения main scene/save. Не переиспользуй прототип с другой версией механики. В standalone preview нет Android SDK, аналитики, billing, настоящих показов рекламы или секретов кабинетов.

## Сетевые варианты (проверяй актуальные требования при доставке)

| Сеть | Артефакт и CTA | Особенности |
| --- | --- | --- |
| AppLovin | Один inline HTML, до 5 MB; `mraid.open()` по студийному контракту без hardcoded store URL | MRAID ready/viewable/state, assets inline/base64, пауза вне показа; landing задаётся кампанией |
| Unity Ads | Inline HTML; `unityads.playable.install()` и предусмотренные контрактом fallback | MRAID ready/viewableChange/stateChange; не открывай URL автоматически |
| Mintegral | ZIP с корневым `index.html`, inline resources; `window.install()` | Hooks `gameReady`, `gameStart`, `gameEnd`, `gameRetry`, `gameClose`; `gameReady` после готовности assets, sound first gesture |
| Google Ads | ZIP до 5 MB, до 512 файлов; `ExitApi.exit()` | Официальный разрешённый ExitApi bridge; нет произвольного `window.open` в ad-версии |

Этот снимок не заменяет кабинетную валидацию. Если сеть не выбрана, подготовь standalone preview и варианты под реально применяемые сети студии, явно назови набор. Не объявляй Google вариант offline без оговорки о разрешённом platform bridge. У каждой сети собственный жизненный цикл, общий switch не должен игнорировать эти различия. Рассчитывай размер финального HTML/ZIP после embedding, не исходных ресурсов.

Официальная сверка 2026-10-09: [AppLovin creative specs](https://support.applovin.com/en/growth/promoting-your-apps/welcome-to-applovin/creative-specs-and-guidelines) требует MRAID v2.0 и `mraid.open()`; [Unity playable specs](https://docs.unity.com/en-us/grow/acquire/creatives/playable/specifications) и [CTA/bootstrap](https://docs.unity.com/en-us/grow/acquire/creatives/playable/best-practices) — один inline index до 5 MB, MRAID viewability и явный CTA через `mraid.open(url)`. Не подставляй Unity URL-сигнатуру в AppLovin-эталон автоматически.

## UX и жизненный цикл

Масштабирование portrait/landscape и safe areas. Видимые короткие английские инструкции, подсказка жеста прекращается после взаимодействия. AudioContext/звук активируются первым пользовательским жестом; есть mute. На `document.visibilitychange` и сетевых hidden/viewability/close — пауза gameplay и звук, без catch-up скачка при resume. End card возникает от результата или описанного demo timeout, не маскирует отсутствие механики. Replay сбрасывает деньги/прогресс/эффекты и корректно возобновляет сцену. CTA вызывается только явным нажатием CTA: мимо кнопки/проигрыш/таймер/автостарт не открывает магазин. Standalone preview использует безопасный preview hook; сетевой вариант использует свой API, не выдуманный универсальный install.

## Проверка и выдача

Собери в `release/marketing/playable/`: standalone HTML, отдельные network HTML/ZIP, manifest, source/build instructions. Не публикуй автоматически и не меняй studio reference. Manifest содержит сегмент/реальные и сокращённые правила, происхождение ресурсов/лицензии, ориентации, package/store URL, SDK bridge/CTA по сети, размеры/SHA-256, тесты.

Проверь в настоящем браузере mobile touch/desktop/landscape: старт, drag/удары/награда, достижение цели, end/replay, mute, suspend/resume, отсутствие console errors. Тест offline блокирует внешние requests (исключая явно разрешённый platform bridge); mock сетевого API проверяет **реальный CTA и lifecycle** без открытия магазина. Проверь отсутствие CTA при обычной игре, ZIP path traversal/корневой index, final byte budgets, успешное открытие распакованного пакета. Статический поиск строк SDK или файла index не доказывает работу CTA. Назови неподтверждённые условия сети отдельно; preview PASS не означает принятие рекламным кабинетом.

Помощник для файлов: `python3 <skill>/scripts/verify_marketing.py --playables <network.html> <network.zip> --videos <01.mp4> <02.mp4> <03.mp4> --output <project>/release/marketing/artifact-checks.json`. Проверяет SHA/размеры/ZIP index и безопасность путей; при установленном ffprobe — video/audio streams. Его PASS не заменяет playback, визуальный просмотр, touch и callback-тесты.
