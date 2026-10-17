#!/usr/bin/env node
// agentb — браузер для агента без MCP. Каждый вызов: свежий headless Chrome с сохранёнными сессиями,
// одно действие или сценарий, один JSON в stdout, браузер закрывается. Обрыв моста не доказывает отмену действия.
//
//   install               проверка штатного подключения Chrome 144+ (расширение не требуется)
//   login a,b             штатное согласие Chrome → cookies и хранилища выбранных кабинетов
//   login-hub a,b         переиспользовать входы этого чата; --refresh явно запрашивает передачу с ПК
//   session-clear         удалить входы текущей рабочей сессии
//   login-window a        отдельное окно агентского Chrome, владелец входит сам (если перенос не принят)
//   sessions [--check]    какие сессии сохранены и живы ли
//   inspect --url URL     заголовки/кнопки/поля/выборы/диалоги страницы компактным JSON
//   text --url URL [--selector CSS] [--max N]
//   run FILE [--url URL] [--set K=V] [--set-file K=PATH]
//       .mjs  export default async ({ page, context, args, settle, inspect }) => result
//       .js   тело `async () => {...}` для страницы; __K__ заменяется значением как есть (JSON)
//       .json профиль формы для scenarios/console_run.js; {{k}} заменяется строкой
//   upload --url URL --file PATH [--chooser TEXT | --input CSS] [--wait-text T] [--wait-ms N] [--click TEXT]...
// Общие флаги: --show (видимое окно), --timeout MS (действие, 15000), --service a,b (по умолчанию все сохранённые).
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFile, execFileSync, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { createHash } from 'node:crypto';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SERVICES = JSON.parse(fs.readFileSync(path.join(HERE, 'services.json'), 'utf8'));
const HOME = process.env.AGENTB_HOME
  || path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), '.local', 'share'), 'AgentBrowser');
const SESSION_ROOT = path.join(HOME, 'sessions');
const SESSION_MS = 8 * 60 * 60 * 1000;
let SESS, scope, sessionId;
const RUNS = path.join(HERE, '.runs');
const CDP_PORT = 9334;
const REPEAT = new Set(['set', 'set-file', 'click']);
const FLAGS = new Set(['show', 'check', 'no-check', 'refresh']);

const err = (msg, extra) => Object.assign(new Error(msg), { extra });

function parseArgs(argv) {
  const pos = [], opt = { set: [], 'set-file': [], click: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith('--')) { pos.push(a); continue; }
    const k = a.slice(2);
    if (FLAGS.has(k)) { opt[k] = true; continue; }
    const v = argv[++i];
    if (v === undefined) throw err(`--${k} без значения`);
    if (REPEAT.has(k)) opt[k].push(v); else opt[k] = v;
  }
  return { pos, opt };
}

const kv = s => {
  const i = s.indexOf('=');
  if (i < 1) throw err(`ожидается K=V: ${s}`);
  return [s.slice(0, i), s.slice(i + 1)];
};

// Значения для подстановки: --set как есть, --set-file — содержимое файла.
async function values(opt) {
  const out = opt.set.map(kv);
  for (const [k, f] of opt['set-file'].map(kv)) out.push([k, fs.readFileSync(await waitFile(f), 'utf8').trim()]);
  return out;
}

// Файл, только что записанный на хабе, доезжает синком за ~1 с.
async function waitFile(f, ms = 10000) {
  const abs = path.resolve(f), end = Date.now() + ms;
  while (!fs.existsSync(abs)) {
    if (Date.now() > end) throw err(`нет файла: ${abs}`);
    await new Promise(r => setTimeout(r, 250));
  }
  return abs;
}

const names = list => {
  const out = (list || '').split(',').map(s => s.trim()).filter(Boolean);
  if (!out.length) throw err('укажи кабинеты: ' + Object.keys(SERVICES).join(','));
  for (const n of out) if (!SERVICES[n]) throw err(`неизвестный кабинет ${n}; есть: ${Object.keys(SERVICES).join(',')}`);
  return out;
};
const hostOf = u => { try { return new URL(u).hostname; } catch { return ''; } };
const serviceOf = url => Object.keys(SERVICES).find(n =>
  SERVICES[n].domains.some(d => hostOf(url) === d || hostOf(url).endsWith('.' + d)));
const loggedOut = (n, url) => new RegExp(SERVICES[n].loggedOut).test(url);
const sessFile = n => path.join(SESS, n + '.json');

function configureSession(opt) {
  scope = opt.session || process.env.AGENTB_SESSION_ID || process.env.CODEX_THREAD_ID || process.env.CODEX_SESSION_ID || 'standalone';
  if (!/^[a-zA-Z0-9_-]{1,128}$/.test(scope)) throw err('некорректный ID рабочей сессии');
  sessionId = createHash('sha256').update(scope).digest('hex').slice(0, 24);
  SESS = path.join(SESSION_ROOT, sessionId);
  // shortcut: CLI не получает событие закрытия чата; session-clear либо очистка при следующем запуске после 8 часов.
  for (const dir of fs.existsSync(SESSION_ROOT) ? fs.readdirSync(SESSION_ROOT, { withFileTypes: true }) : []) {
    if (!dir.isDirectory() || !/^[a-f0-9]{24}$/.test(dir.name)) continue;
    const folder = path.join(SESSION_ROOT, dir.name), meta = path.join(folder, '.session.json');
    if (fs.existsSync(meta) && Date.parse(JSON.parse(fs.readFileSync(meta, 'utf8')).expiresAt) <= Date.now()) {
      fs.rmSync(folder, { recursive: true });
    }
  }
}

function sessionExpiry() {
  const meta = path.join(SESS, '.session.json');
  return fs.existsSync(meta) ? JSON.parse(fs.readFileSync(meta, 'utf8')).expiresAt : null;
}

function saveSession(n, data) {
  if (!Array.isArray(data.cookies) || !Array.isArray(data.origins)
    || data.cookies.some(c => typeof c.name !== 'string' || typeof c.value !== 'string'
      || typeof c.domain !== 'string' || !SERVICES[n].domains.some(d => c.domain.replace(/^\./, '') === d || c.domain.endsWith('.' + d)))
    || data.origins.some(o => !SERVICES[n].origins.includes(o.origin) || !Array.isArray(o.localStorage)
      || o.localStorage.some(v => typeof v.name !== 'string' || typeof v.value !== 'string')
      || o.sessionStorage !== undefined && (!Array.isArray(o.sessionStorage)
        || o.sessionStorage.some(v => typeof v.name !== 'string' || typeof v.value !== 'string')))) {
    throw err(`некорректные данные сессии ${n}`);
  }
  fs.mkdirSync(SESS, { recursive: true, mode: 0o700 });
  if (!sessionExpiry()) {
    try {
      fs.writeFileSync(path.join(SESS, '.session.json'), JSON.stringify({ expiresAt: new Date(Date.now() + SESSION_MS).toISOString() }), { mode: 0o600, flag: 'wx' });
    } catch (e) { if (e.code !== 'EEXIST') throw e; }
  }
  const s = { savedAt: new Date().toISOString(), cookies: data.cookies || [], origins: data.origins || [] };
  const temp = path.join(SESS, `${n}.${process.pid}.tmp`);
  try {
    fs.writeFileSync(temp, JSON.stringify(s), { mode: 0o600 });
    fs.renameSync(temp, sessFile(n));
  } finally { fs.rmSync(temp, { force: true }); }
  return { cookies: s.cookies.length, storage: s.origins.length };
}

function loadState(list) {
  const pick = list ? names(list) : Object.keys(SERVICES).filter(n => fs.existsSync(sessFile(n)));
  const st = { cookies: [], origins: [] };
  for (const n of pick) {
    if (!fs.existsSync(sessFile(n))) throw err(`нет входа ${n} в этой рабочей сессии: agentb login-hub ${n}`, { code: 'SESSION_REQUIRED', service: n });
    const s = JSON.parse(fs.readFileSync(sessFile(n), 'utf8'));
    st.cookies.push(...s.cookies);
    st.origins.push(...s.origins);
  }
  return st;
}

async function launch(opt) {
  return chromium.launch({
    channel: process.env.AGENTB_CHANNEL || 'chrome',
    headless: !opt.show,
    args: ['--disable-blink-features=AutomationControlled'],
  });
}

async function newContext(browser, opt, state) {
  const context = await browser.newContext({
    storageState: { cookies: state.cookies, origins: state.origins.map(({ sessionStorage, ...origin }) => origin) },
    locale: 'en-US',
    viewport: { width: 1440, height: 1000 },
    // Headless иначе представляется HeadlessChrome, и кабинеты это замечают.
    userAgent: opt.show ? undefined
      : `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${browser.version()} Safari/537.36`,
  });
  const tabs = state.origins.filter(o => o.sessionStorage?.length);
  if (tabs.length) await context.addInitScript(origins => {
    const saved = origins.find(o => o.origin === location.origin);
    for (const { name, value } of saved?.sessionStorage || []) sessionStorage.setItem(name, value);
  }, tabs.map(({ origin, sessionStorage }) => ({ origin, sessionStorage })));
  context.setDefaultTimeout(+opt.timeout || 15000);
  context.setDefaultNavigationTimeout(60000);
  return context;
}

// SPA кабинетов не доходят до networkidle: ждём load и пока текст страницы перестанет меняться.
async function settle(page, ms = 10000) {
  await page.waitForLoadState('load', { timeout: ms }).catch(() => {});
  const end = Date.now() + ms;
  let last = -1, stable = 0;
  while (Date.now() < end && stable < 3) {
    const n = await page.evaluate(() => document.body?.innerText.length || 0).catch(() => 0);
    stable = n > 0 && n === last ? stable + 1 : 0;
    last = n;
    await page.waitForTimeout(300);
  }
}

async function go(page, url) {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await settle(page);
  const n = serviceOf(url);
  if (n && loggedOut(n, page.url())) throw err(`вход ${n} истёк: agentb login-hub ${n} --refresh`, { code: 'AUTH_REQUIRED', service: n, url: page.url() });
}

async function persistContext(context, previous, list) {
  const state = await context.storageState();
  for (const origin of state.origins) {
    const page = context.pages().find(p => hostOf(p.url()) && new URL(p.url()).origin === origin.origin);
    origin.sessionStorage = page
      ? await page.evaluate(() => Object.entries(sessionStorage).map(([name, value]) => ({ name, value })))
      : previous.origins.find(o => o.origin === origin.origin)?.sessionStorage || [];
  }
  for (const n of list) saveSession(n, {
    cookies: state.cookies.filter(c => SERVICES[n].domains.some(d => c.domain.replace(/^\./, '') === d || c.domain.endsWith('.' + d))),
    origins: state.origins.filter(o => SERVICES[n].origins.includes(o.origin)),
  });
}

async function failShot(page) {
  fs.mkdirSync(RUNS, { recursive: true });
  const f = path.join(RUNS, `fail-${Date.now()}.png`);
  return page.screenshot({ path: f }).then(() => f, () => null);
}

async function withPage(opt, fn) {
  const state = loadState(opt.service);
  const browser = await launch(opt);
  try {
    const page = await (await newContext(browser, opt, state)).newPage();
    try {
      if (opt.url) await go(page, opt.url);
      const result = await fn(page, page.context());
      await persistContext(page.context(), state, opt.service ? names(opt.service) : Object.keys(SERVICES).filter(n => fs.existsSync(sessFile(n))));
      return result;
    } catch (e) {
      e.extra = { url: page.url(), shot: await failShot(page), ...e.extra };
      throw e;
    }
  } finally {
    await browser.close();
  }
}

// Замена скриншоту и a11y-дереву: только то, по чему можно действовать.
const INSPECT = () => {
  const vis = e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
  const t = s => (s || '').replace(/\s+/g, ' ').trim().slice(0, 80);
  const nameOf = e => {
    const own = t(e.getAttribute('aria-label') || e.innerText || e.placeholder);
    if (own) return own;
    const lab = e.id && document.querySelector(`label[for="${CSS.escape(e.id)}"]`);
    if (lab) return t(lab.innerText);
    const wrap = e.closest('label,material-radio,mat-radio-button,material-checkbox,mat-checkbox,.mdc-form-field');
    return wrap ? t(wrap.innerText) : t(e.name);
  };
  const on = e => e.checked === true || e.getAttribute('aria-checked') === 'true';
  const q = s => [...document.querySelectorAll(s)].filter(vis);
  const uniq = a => [...new Set(a.filter(Boolean))];
  return {
    url: location.href,
    title: document.title,
    headings: uniq(q('h1,h2,h3,[role=heading]').map(nameOf)).slice(0, 30),
    buttons: uniq(q('button,[role=button],[role=tab],[role=menuitem],a[href]').map(e =>
      nameOf(e) + (e.disabled || e.getAttribute('aria-disabled') === 'true' ? ' [disabled]' : ''))).slice(0, 100),
    fields: q('input:not([type=radio]):not([type=checkbox]):not([type=hidden]):not([type=file]),textarea,select,[role=combobox]')
      .map(e => ({ name: nameOf(e), value: t(e.value), type: e.type || e.getAttribute('role') })).slice(0, 60),
    choices: q('input[type=radio],input[type=checkbox],[role=radio],[role=checkbox]')
      .map(e => (on(e) ? '[x] ' : '[ ] ') + nameOf(e)).slice(0, 100),
    files: document.querySelectorAll('input[type=file]').length,
    dialogs: q('[role=dialog],[role=alertdialog]').map(e => t(e.innerText)).slice(0, 3),
  };
};

// --- команды ---

async function inspect(_, opt) {
  if (!opt.url) throw err('inspect: нужен --url');
  return withPage(opt, async page => ({ page: await page.evaluate(INSPECT) }));
}

async function text(_, opt) {
  if (!opt.url) throw err('text: нужен --url');
  return withPage(opt, async page => {
    const s = await page.evaluate(sel => (document.querySelector(sel) || document.body).innerText, opt.selector || 'body');
    const max = +opt.max || 4000, clean = s.replace(/\n\s*\n+/g, '\n').trim();
    return { url: page.url(), text: clean.slice(0, max), truncated: clean.length > max };
  });
}

function loadProfile(file, vals) {
  let src = fs.readFileSync(file, 'utf8');
  for (const [k, v] of vals) src = src.split(`{{${k}}}`).join(JSON.stringify(v).slice(1, -1));
  const left = [...new Set(src.match(/\{\{\w+\}\}/g) || [])];
  if (left.length) throw err(`не заданы ${left.join(', ')} (--set k=v)`);
  const p = JSON.parse(src), kinds = ['radio', 'check', 'fill', 'click', 'wait'], bad = [];
  if (!Array.isArray(p.steps) || !p.steps.length) bad.push('steps: нужен непустой список');
  (p.steps || []).forEach((s, i) => {
    if (kinds.filter(k => k in s).length !== 1) bad.push(`шаг ${i}: ровно один из ${kinds}`);
    if ('fill' in s && !('value' in s)) bad.push(`шаг ${i}: fill требует value`);
  });
  if (!p.expect?.length && !p.forbid?.length) bad.push('expect/forbid: без проверки результата успех не доказан');
  if (bad.length) throw err(bad.join('; '));
  return p;
}

async function run([file], opt) {
  if (!file) throw err('run: нужен файл сценария');
  const abs = await waitFile(file), vals = await values(opt), ext = path.extname(abs);
  if (ext === '.mjs') {
    const mod = await import(pathToFileURL(abs).href + '?t=' + Date.now());
    const args = Object.fromEntries(vals);
    return withPage(opt, async (page, context) => ({
      result: await mod.default({ page, context, args, settle: (ms) => settle(page, ms), inspect: () => page.evaluate(INSPECT) }),
    }));
  }
  let body;
  if (ext === '.json') {
    const p = loadProfile(abs, vals);
    opt.url ||= p.url;
    body = fs.readFileSync(path.join(HERE, 'scenarios', 'console_run.js'), 'utf8').split('__PROFILE__').join(JSON.stringify(p));
  } else {
    body = fs.readFileSync(abs, 'utf8');
    for (const [k, v] of vals) body = body.split(`__${k}__`).join(v);
    const left = [...new Set(body.match(/__[A-Z][A-Z0-9_]*__/g) || [])];
    if (left.length) throw err(`не заданы ${left.join(', ')} (--set-file K=файл.json)`);
  }
  if (!opt.url) throw err('run: нужен --url (страница, на которой выполнить скрипт)');
  return withPage(opt, async page => ({ result: await page.evaluate(`(${body.trim().replace(/;$/, '')})()`) }));
}

async function upload(_, opt) {
  if (!opt.url || !opt.file) throw err('upload: нужны --url и --file');
  const file = path.resolve(opt.file);
  if (!fs.existsSync(file)) throw err(`нет файла на ПК: ${file}`);
  return withPage(opt, async page => {
    if (opt.chooser) {
      const [fc] = await Promise.all([
        page.waitForEvent('filechooser'),
        page.getByRole('button', { name: opt.chooser }).first().click(),
      ]);
      await fc.setFiles(file);
    } else {
      await page.locator(opt.input || 'input[type=file]').first().setInputFiles(file);
    }
    if (opt['wait-text']) await page.getByText(opt['wait-text']).first().waitFor({ timeout: +opt['wait-ms'] || 600000 });
    for (const c of opt.click) {
      await page.getByRole('button', { name: c, exact: true }).first().click();
      await settle(page, 5000);
    }
    return { uploaded: path.basename(file), bytes: fs.statSync(file).size, page: await page.evaluate(INSPECT) };
  });
}

async function checkSessions(list, opt) {
  const browser = await launch(opt), out = {};
  try {
    for (const n of list) {
      if (!fs.existsSync(sessFile(n))) { out[n] = 'нет сессии'; continue; }
      const state = loadState(n), context = await newContext(browser, opt, state);
      const page = await context.newPage();
      try {
        const response = await page.goto(SERVICES[n].check, { waitUntil: 'domcontentloaded' });
        await settle(page, 6000);
        out[n] = response && !response.ok() ? `недоступен: HTTP ${response.status()}`
          : loggedOut(n, page.url()) || await page.locator('input[type=password]').first().isVisible() ? 'не действует'
          : 'требует проверки кабинета';
        await persistContext(context, state, [n]);
      } catch (e) {
        out[n] = 'ошибка: ' + e.message.split('\n')[0];
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
  return out;
}

async function sessions(_, opt) {
  const saved = {};
  for (const n of Object.keys(SERVICES)) {
    if (!fs.existsSync(sessFile(n))) continue;
    const s = JSON.parse(fs.readFileSync(sessFile(n), 'utf8'));
    saved[n] = { savedAt: s.savedAt, cookies: s.cookies.length };
  }
  return { sessionId, expiresAt: sessionExpiry(), saved, ...(opt.check ? { check: await checkSessions(Object.keys(saved), opt) } : {}) };
}

function sessionClear() {
  fs.rmSync(SESS, { recursive: true, force: true });
  return { sessionId, cleared: true };
}

function chromeExe() {
  if (process.env.AGENTB_CHROME) return process.env.AGENTB_CHROME;
  if (process.platform === 'darwin') return '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  if (process.platform !== 'win32') return 'google-chrome';
  const roots = [process.env.PROGRAMFILES, process.env['PROGRAMFILES(X86)'], process.env.LOCALAPPDATA].filter(Boolean);
  return roots.map(r => path.join(r, 'Google', 'Chrome', 'Application', 'chrome.exe')).find(f => fs.existsSync(f)) || 'chrome.exe';
}

// on-remote работает в session 0 без рабочего стола: окно оттуда никто не увидит. Тогда открываем
// через разовую задачу планировщика с /IT — она стартует в сессии вошедшего пользователя.
function openVisible(exe, args) {
  if (process.platform !== 'win32' || process.env.SESSIONNAME) {
    spawn(exe, args, { detached: true, stdio: 'ignore' }).on('error', () => {}).unref();
    return;
  }
  fs.mkdirSync(HOME, { recursive: true });
  const cmd = path.join(HOME, 'open.cmd');
  fs.writeFileSync(cmd, `@start "" "${exe}" ${args.map(a => `"${a}"`).join(' ')}\r\n`);
  execFileSync('schtasks', ['/create', '/tn', 'agentb-open', '/tr', `"${cmd}"`, '/sc', 'once', '/st', '00:00', '/it', '/f'], { stdio: 'ignore' });
  execFileSync('schtasks', ['/run', '/tn', 'agentb-open'], { stdio: 'ignore' });
}

// Chrome 144+ сам запрашивает согласие владельца при подключении к этому endpoint.
// DevToolsActivePort содержит только адрес, cookies/пароли из файлов профиля не читаются.
function ownerEndpoint() {
  if (process.platform !== 'win32') throw err('Для передачи с ПК запускай login-hub на хабе');
  const file = path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'User Data', 'DevToolsActivePort');
  if (!fs.existsSync(file)) throw err('Штатное подключение Chrome не включено (chrome://inspect/#remote-debugging)');
  const [port, route] = fs.readFileSync(file, 'utf8').trim().split(/\r?\n/);
  if (!/^\d+$/.test(port) || +port < 1 || +port > 65535 || !/^\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(route)) {
    throw err('Некорректный локальный endpoint Chrome');
  }
  return `ws://127.0.0.1:${port}${route}`;
}

async function login([list], opt) {
  const want = names(list);
  process.stderr.write(`Подтверди штатный запрос Chrome: передача сессий ${want.join(', ')}${opt.destination === 'hub' ? ' на твой хаб' : ' браузеру агента на ПК'}.\n`);
  const browser = await chromium.connectOverCDP(ownerEndpoint(), { timeout: (+opt.wait || 180) * 1000, noDefaults: true });
  const saved = {}, warnings = [];
  try {
    const context = browser.contexts()[0];
    for (const n of want) {
      const origins = [];
      for (const origin of SERVICES[n].origins) {
        let page = context.pages().find(p => { try { return new URL(p.url()).origin === origin; } catch { return false; } });
        const temporary = !page;
        try {
          if (!page) {
            page = await context.newPage();
            await page.goto(new URL(SERVICES[n].check).origin === origin ? SERVICES[n].check : origin, { waitUntil: 'domcontentloaded', timeout: 30000 });
            await settle(page, 5000);
          }
          if (new URL(page.url()).origin === origin) {
            origins.push({ origin, ...await page.evaluate(() => ({
              localStorage: Object.entries(localStorage).map(([name, value]) => ({ name, value })),
              sessionStorage: Object.entries(sessionStorage).map(([name, value]) => ({ name, value })),
            })) });
          }
        } catch {
          warnings.push({ service: n, origin, error: 'хранилища страницы недоступны; cookies сохраняются отдельно' });
        } finally { if (temporary && page) await page.close(); }
      }
      const own = d => SERVICES[n].domains.some(x => d.replace(/^\./, '') === x || d.endsWith('.' + x));
      saved[n] = saveSession(n, { cookies: (await context.cookies()).filter(c => own(c.domain)), origins });
    }
  } finally {
    // connectOverCDP: close отсоединяет клиент, Chrome владельца остаётся открыт.
    await browser.close();
  }
  return { saved, warnings, ...(opt['no-check'] ? {} : { check: await checkSessions(want, opt) }) };
}

// Вызывается только дочерним процессом login-hub; его stdout с cookies не выводится в чат.
function transferSessions([list]) {
  const want = names(list);
  const sessions = Object.fromEntries(want.map(n => [n,
    JSON.parse(fs.readFileSync(sessFile(n), 'utf8')),
  ]));
  for (const n of want) fs.rmSync(sessFile(n));
  return { sessions };
}

async function loginHub([list], opt) {
  if (process.platform === 'win32') throw err('login-hub запускается на хабе');
  const want = names(list);
  const missing = opt.refresh ? want : want.filter(n => !fs.existsSync(sessFile(n)));
  if (!missing.length) return {
    ...await sessions([], { ...opt, check: !!opt.check && !opt['no-check'] }),
    reused: want, transferred: [], destination: 'hub',
  };
  const remote = async args => {
    const { stdout } = await promisify(execFile)('on-remote', [
      '--pc-home', 'node', '.agents/skills/google-play-publish/tools/agent-browser/agentb.mjs', ...args, '--session', scope,
    ], { maxBuffer: 16 * 1024 * 1024, timeout: ((+opt.wait || 900) + 30) * 1000 });
    const result = JSON.parse(stdout.trim());
    if (!result.ok) throw err(result.error || 'ошибка agentb на ПК');
    return result;
  };
  // Отказ при --refresh не превращается в успешный ответ из старого кэша.
  const capture = await remote(['login', missing.join(','), '--destination', 'hub', '--no-check', '--wait', String(+opt.wait || 900)]);
  const result = await remote(['_transfer-sessions', missing.join(',')]);
  const saved = {};
  for (const n of missing) {
    const state = result.sessions?.[n];
    if (!state || !Array.isArray(state.cookies) || !Array.isArray(state.origins)) throw err(`неполная передача ${n}`);
  }
  for (const n of missing) saved[n] = saveSession(n, result.sessions[n]);
  return { sessionId, expiresAt: sessionExpiry(), saved, reused: want.filter(n => !missing.includes(n)), transferred: missing, warnings: capture.warnings || [], destination: 'hub', ...(opt['no-check'] ? {} : { check: await checkSessions(want, opt) }) };
}

// Запасной вход: обычный Chrome (без флагов автоматизации, Google его пускает) со своим профилем.
// Владелец входит руками, агент забирает cookies через CDP. Профиль сохраняется, повторный вход быстрее.
async function loginWindow([list], opt) {
  const want = names(list), n = want[0];
  openVisible(chromeExe(), [
    `--user-data-dir=${path.join(HOME, 'login-profile')}`, `--remote-debugging-port=${CDP_PORT}`,
    '--no-first-run', '--no-default-browser-check', SERVICES[n].check,
  ]);
  const end = Date.now() + (+opt.wait || 600) * 1000;
  let browser, closed = false;
  try {
    while (!browser) {
      if (Date.now() > end) throw err('Chrome агента не поднялся');
      browser = await chromium.connectOverCDP(`http://127.0.0.1:${CDP_PORT}`).catch(() => null);
      if (!browser) await new Promise(r => setTimeout(r, 500));
    }
    browser.on('disconnected', () => { closed = true; });
    const context = browser.contexts()[0];
    while (Date.now() < end) {
      if (closed) throw err('окно закрыто до входа');
      const ok = context.pages().some(p => serviceOf(p.url()) === n && !loggedOut(n, p.url()));
      if (ok) {
        await new Promise(r => setTimeout(r, 3000));
        const state = await context.storageState();
        const out = {};
        for (const m of want) {
          const own = d => SERVICES[m].domains.some(x => d.replace(/^\./, '') === x || d.endsWith('.' + x));
          out[m] = saveSession(m, {
            cookies: state.cookies.filter(c => own(c.domain)),
            origins: state.origins.filter(o => SERVICES[m].origins.includes(o.origin)),
          });
        }
        return { saved: out, check: await checkSessions(want, opt) };
      }
      await new Promise(r => setTimeout(r, 2000));
    }
    throw err('вход не выполнен за отведённое время');
  } finally {
    if (browser && !closed) {
      await (await browser.newBrowserCDPSession()).send('Browser.close').catch(() => {});
    }
  }
}

async function install() {
  ownerEndpoint();
  return { ready: true, method: 'chrome-native-consent', extensionRequired: false };
}

const COMMANDS = { install, login, 'login-hub': loginHub, 'session-clear': sessionClear, '_transfer-sessions': transferSessions, 'login-window': loginWindow, sessions, inspect, text, run, upload };

try {
  const { pos: [cmd, ...rest], opt } = parseArgs(process.argv.slice(2));
  configureSession(opt);
  if (!COMMANDS[cmd]) throw err(`команды: ${Object.keys(COMMANDS).join(', ')}; справка в шапке agentb.mjs`);
  process.stdout.write(JSON.stringify({ ok: true, ...(await COMMANDS[cmd](rest, opt)) }) + '\n');
} catch (e) {
  process.stdout.write(JSON.stringify({ ok: false, error: String(e?.message || e).split('\n')[0], ...e?.extra }) + '\n');
  process.exitCode = 1;
}
