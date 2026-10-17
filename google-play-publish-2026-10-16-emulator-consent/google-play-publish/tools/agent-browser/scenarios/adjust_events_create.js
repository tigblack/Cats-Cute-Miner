// Создаёт события в Adjust: AppView -> приложение -> Events -> Add event, по одному имени.
// Запуск: agentb run scenarios/adjust_events_create.js --url https://suite.adjust.com/apps/<app_token>/events --set-file NAMES=<names.json>.
// Вкладка должна быть на https://suite.adjust.com/apps/<app_token>/events. Замени __NAMES__ на JSON-массив имён.
// Пачка до ~25 имён на вызов (около 1.5 с на событие). Проверено на Piggy Payday 2026-10-14: 50 событий, 0 ошибок.
// Дубликат Adjust не принимает: форма остаётся открытой, скрипт нажимает Cancel и отмечает имя как failed (событие уже есть).
async () => {
  const names = __NAMES__;
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const waitFor = async (fn, ms = 8000) => { const t = Date.now(); while (Date.now() - t < ms) { const v = fn(); if (v) return v; await sleep(150); } return null; };
  const button = text => [...document.querySelectorAll('button')].find(b => b.innerText.trim() === text);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
  const results = [];
  for (const name of names) {
    if (!location.pathname.endsWith('/events')) { results.push({ name, error: 'not on events page: ' + location.pathname }); break; }
    const add = await waitFor(() => button('Add event'));
    if (!add) { results.push({ name, error: 'no Add event button' }); break; }
    add.click();
    const input = await waitFor(() => document.querySelector('input[name="name"]'));
    if (!input) { results.push({ name, error: 'no form' }); break; }
    setter.call(input, name);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await sleep(300);
    const submit = button('Add event');
    if (!submit || submit.disabled) { results.push({ name, error: 'submit disabled' }); button('Cancel')?.click(); await sleep(500); continue; }
    submit.click();
    const done = await waitFor(() => location.pathname.endsWith('/events')
      && document.body.innerText.split('\n').some(line => line.trim() === name));
    if (done) results.push({ name, ok: true });
    else { results.push({ name, error: 'not created (already exists?)' }); button('Cancel')?.click(); await sleep(600); }
  }
  return { created: results.filter(r => r.ok).length, failed: results.filter(r => !r.ok) };
}
