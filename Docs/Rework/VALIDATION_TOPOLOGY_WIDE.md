# Проверка больших ветвящихся шахт

11 октября 2026. Версия `wide-branching-mines-2`, все 144 шахты.
Код, маски проходимости, размещения объектов и камера применены в проекте Unity.
Описание реализации: [MINE_TOPOLOGY.md](MINE_TOPOLOGY.md).

## Результат

- Ширина массивов — 25–53 клетки, глубина — 12–76; фактический горизонтальный
  размах проходов во всех шахтах составляет не менее 18 клеток. В кампании
  900 камер и 138 обратных галерей. Есть отдельные боковые ветви, тупики и
  соединения; постоянная порода внутри карты сохраняется после расчистки.
- Камера показывает область шириной около десяти клеток. Мышь и палец
  перемещают карту по X/Z; перетаскивание не вызывает удар при отпускании.
  Проверены инерция, отмена касания, колесо, границы и возврат к базе по обеим осям.
- Сохранённая прямоугольная шахта или `branching-mines-1` переносится на широкий
  план после проверки достижимости, сохранности содержимого и архива main/bak.
  HP, груз, золото, улучшения, открытые ворота, карточки, экипировка и награды
  сохраняются. Котики и выпущенный груз возвращаются к входу, пути назначаются
  заново. Начисление золота остаётся обычной разгрузкой.

Исходник `Assets/Game/Data/Source/json-data.json` не менялся. SHA-256:
`8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5`.
Состав и баланс целей проверены по исходным ID, типам, секторам, HP и порциям.

## Проверки

| Проверка | Результат | Артефакт |
|---|---|---|
| Ядро: все планы, доставка A, кампания C, ворота, сохранения | 155/155 | [core-tests.log](../Reports/topology-wide/core-tests.log) |
| Ядро: миграция с сумками/грузом, завершённая шахта, несовместимый источник/план | 8/8 | [migration-core-tests.log](../Reports/topology-wide/migration-core-tests.log) |
| Unity EditMode: топология, миграция, конфигурации | 212/212 | [editmode-results.xml](../Reports/topology-wide/editmode-results.xml) |
| Unity PlayMode: камера и реальные события InputSystem мыши/пальца | 10/10 | [exploration-results.xml](../Reports/topology-wide/exploration-results.xml) |
| Unity PlayMode: контроллер, сохранения, офлайн, ранняя/средняя/поздняя шахты | 28/28 | [runtime-results.xml](../Reports/topology-wide/runtime-results.xml) |
| Unity PlayMode: миграция после последней проверки достижимости | 5/5 | [migration-runtime-results.xml](../Reports/topology-wide/migration-runtime-results.xml) |
| Unity CLI: геометрия и A всех шахт, последовательная C, контрольные стратегии с карточками/экипировкой | 144 A + 144 C + 84 стратегии; ошибок 0 | [summary.json](../Reports/topology-wide/validator/summary.json) |

Первоначальный полный PlayMode-прогон дал 85 успешных сценариев, два сбоя и
14 исключённых Explicit-генераторов. Два теста ожидали прежние условия:
сохранение прямоугольника и расчистку первой шахты за 15 минут. Ожидания
обновлены: широкая карта с прежним составом и достаточный интервал для проверки
повторной записи офлайна. В повторном прогоне всей группы `OutpostRuntimeTests`
оба сценария прошли. Дополнительно исправлено продолжение загрузки после ошибки
записи миграции карточек при недоступном архиве геометрии. Артефакт исходного
полного прогона сохранён: [playmode-full-initial-results.xml](../Reports/topology-wide/playmode-full-initial-results.xml).

Сравнение политики A использует один seed 31337, пустую экипировку, отсутствие
карточек и покупок. Итоговое время всей кампании составляет **1,1222** от
прежнего; максимальное отношение для отдельной шахты — **1,4903**. Проверки
предела 1,25 для кампании и 1,5 для отдельной шахты сохранены. Количество порций
и доставленное золото совпадают для каждой из 144 шахт.
[travel-comparison.json](../Reports/topology-wide/travel-comparison.json).

Отдельный Unity-валидатор использует seed 2101001, настоящие правила карточек и
заработанную экипировку. Инварианты проверяются после каждой отметки событий и
сериализации между шахтами. Его SHA-256 каталога топологии совпадает с текущим
`mine_topology.json`; итог — ноль ошибок во всех 144 шахтах и 84 дополнительных
сценариях A/B/C. [Отчёты валидатора](../Reports/topology-wide/validator/README.md).

Все сцены и сохранения тестов изолированы в временных каталогах. Пользовательский
Editor не закрывался; для Unity использована копия `build/ci`. Проверки ввода
используют настоящие события InputSystem в сцене, без физического телефона.

## Изображения из Unity

Кадры получены из сцены `CoreGame`, в отдельном Debug-профиле. Симуляция
продвигалась политикой C, без выдачи ресурсов. Полный план снят со скрытым HUD
и увеличенной областью обзора; боковые камеры сняты в обычном игровом масштабе.
Это проверка геометрии и представления, а не запись прохождения с обычными часами.

| Шахта | Размер | Полный очищенный план | Ветка в игровом масштабе |
|---|---|---|---|
| 1 | 29 × 21 | [Карта](../Reports/topology-wide/screenshots/mine-001-cleared.png) | [Слева](../Reports/topology-wide/screenshots/mine-001-left.png) |
| 48 | 31 × 38 | [Карта](../Reports/topology-wide/screenshots/mine-048-cleared.png) | [Справа](../Reports/topology-wide/screenshots/mine-048-right.png) |
| 144 | 49 × 57 | [Карта](../Reports/topology-wide/screenshots/mine-144-cleared.png) | [Справа](../Reports/topology-wide/screenshots/mine-144-right.png) |

Метаданные с количеством целей, доставок и ворот:
[screenshots.json](../Reports/topology-wide/screenshots/screenshots.json).
Размеры всей кампании: [layout-summary.json](../Reports/topology-wide/layout-summary.json).

## Повторение

```powershell
powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode EditMode -Target Ci -TestFilter 'MineTopology|OutpostConfigTests'
powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter 'OutpostRuntimeTests|MineTopologyRuntimeTests'
powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter 'CameraExplorationTests|OutpostExplorationInputTests'
powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode Validate -Target Ci
```
