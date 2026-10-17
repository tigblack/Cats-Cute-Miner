# Data safety: стандартные ответы студии (стек MAX + Mintegral + Unity Ads + AppMetrica + Adjust)

Подтверждено владельцем 2026-10-13 (Rabbit Gunner); для другого набора SDK сверь с их декларациями.

- **Шифрование при передаче: Yes.** Владелец подтвердил для AppLovin MAX и Mintegral (публичных заявлений у вендоров нет); Unity Ads заявляет Yes в [документации](https://docs.unity.com/ads/en-us/manual/GoogleDataSafety).
- **Аккаунты:** создания аккаунта нет. **Удаление данных:** запрос по https://multicastgames.com/policy.
- **Unity «Other app activity»:** заявлять как собираемое и обязательное всегда, независимо от Acquire Optimization (Unity: данные об использовании собираются только при ней, но перезаявка безопасна).
- **Собираемые типы и цели** (все collected + shared, required, т. к. сбор рекламными SDK нельзя отключить):
  - Device or other IDs, Approximate location, Purchase history, Performance diagnostics: App functionality, Analytics, Advertising, Fraud prevention/security
  - User interaction (Page views/taps): Analytics, Advertising, Fraud prevention/security
  - Other app activity: Analytics, Advertising
  - Other performance data: Analytics, Fraud prevention/security
  - Crash logs: Analytics
  - User account/ID: App functionality, Fraud prevention/security
- Готовый CSV и его проверка: `release/google-play/data-safety-prepared*.{csv,json}` предыдущего проекта — шаблон, не копируй вслепую.

## Способ отправки
1. **API** (`scripts/play_data_safety.py submit`) — одним запросом, но нужен OAuth-токен androidpublisher с правом на приложение; у студии его по умолчанию нет.
2. **Браузер** (рабочий путь без токена): Console → App content → Data safety → Edit. Один проход: сначала все типы и цели, затем Preview → Save → один раз перечитать App content. Не сохраняй промежуточные черновики и не делай скриншотов между шагами; проверяй текстом страницы (`evaluate_script`).

## Рабочая процедура в браузере (проверена на Rabbit Gunner, 2026-10-13; весь проход ≈ 10 вызовов)
Одна вкладка Console на `.../app-content/data-privacy-security`, мастер из 5 шагов; все действия — `evaluate_script`, без скриншотов.
1. **Шаг 2:** collects = Yes; шифрование = Yes; «My app does not allow users to create an account»; ссылка на удаление данных. Радио ищи в блоке вопроса (подпись «Yes» повторяется).
2. **Шаг 3 (типы):** секции раскрываются по одной (аккордеон; `Show` у другой секции сворачивает предыдущую). Метки в Console: Approximate location; User IDs; Purchase history; App interactions; **«Other actions»** (= Other app activity / игровые действия Unity); Crash logs; Diagnostics; Other app performance data; Device or other IDs. Прогресс — счётчики `N/M data types selected`.
3. **Шаг 4:** `agentb run scenarios/data_safety_dialogs.js --set-file PLAN=<plan.json>` (метки целей полностью); шаги 2–5 мастера, если между ними нет Save, делай одним `.mjs` ([browser.md](browser.md)) — все диалоги за один вызов. Кнопка `Start` в шапке мастера — это Import from CSV, не диалог типа; в таблице типов ищи кнопку по строке.
4. **Шаг 5:** в Preview сверь: шифрование, ссылка удаления, перечень категорий; затем `Save` (не `Save as draft`) и закрой «Go to Publishing overview?» кнопкой «Not now»: отправка на ревью — отдельное решение. Перечитай `app-content/overview`: «You're all caught up».
