// Читает таблицу Events в Adjust: имя -> токен. Запуск: agentb run scenarios/adjust_events_read.js --url <страница Events>.
// Вкладка на https://suite.adjust.com/apps/<app_token>/events. Таблица виртуализована (видно ~18 строк),
// поэтому скрипт прокручивает список и накапливает пары. Результат сохрани как release/google-play/adjust-tokens.json
// и впиши командой scripts/fill_adjust_tokens.py. Проверь, что число пар равно числу событий (+ purchase).
async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const acc = {};
  const collect = () => {
    const lines = document.body.innerText.split('\n').map(s => s.trim()).filter(Boolean);
    for (let i = 0; i + 1 < lines.length; i++)
      if (/^[a-z][a-z0-9_]+$/.test(lines[i]) && /^[a-z0-9]{6}$/.test(lines[i + 1]) && lines[i] !== lines[i + 1]) acc[lines[i]] = lines[i + 1];
  };
  const scroller = [...document.querySelectorAll('*')]
    .filter(e => e.scrollHeight > e.clientHeight + 20 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)).pop();
  if (scroller) {
    scroller.scrollTop = 0; await sleep(400);
    for (let i = 0; i < 120; i++) {
      collect();
      const before = scroller.scrollTop;
      scroller.scrollTop = before + 150;
      await sleep(250);
      if (scroller.scrollTop === before) { await sleep(400); break; }
    }
  }
  collect();
  return { count: Object.keys(acc).length, tokens: acc };
}
