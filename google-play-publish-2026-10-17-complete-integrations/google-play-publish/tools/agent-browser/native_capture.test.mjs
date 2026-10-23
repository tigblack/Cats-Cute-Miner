import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { captureNativeSessions } from './native_capture.mjs';

const ENDPOINT = 'ws://127.0.0.1:9222/devtools/browser/owner-approved';
const ORIGIN = 'https://panel.example.com';
const SECRET = 'credential-do-not-leak';
const config = { panel: { domains: ['example.com'], origins: [ORIGIN], check: `${ORIGIN}/dashboard` } };
const page = (targetId, url = `${ORIGIN}/home`) => ({ type: 'page', targetId, url, title: SECRET });
const state = (tag, authenticated = true, origin = ORIGIN) => ({
  origin, localStorage: [{ name: 'local', value: tag }],
  sessionStorage: authenticated ? [{ name: 'token', value: tag }] : [],
});

function fakeTransport(handler, { open = true } = {}) {
  const instances = [];
  class FakeWebSocket extends EventTarget {
    constructor(endpoint) {
      super();
      assert.equal(endpoint, ENDPOINT);
      this.readyState = 0;
      this.sent = [];
      this.listeners = new Map();
      this.closeCount = 0;
      instances.push(this);
      if (open) queueMicrotask(() => {
        if (this.readyState !== 0) return;
        this.readyState = 1;
        this.dispatchEvent(new Event('open'));
      });
    }
    addEventListener(type, fn) {
      const set = this.listeners.get(type) || new Set();
      set.add(fn);
      this.listeners.set(type, set);
      super.addEventListener(type, fn);
    }
    removeEventListener(type, fn) {
      this.listeners.get(type)?.delete(fn);
      super.removeEventListener(type, fn);
    }
    send(text) {
      const message = JSON.parse(text);
      this.sent.push(message);
      handler(message, this);
    }
    reply(message, result, extra = {}) {
      if (this.readyState !== 1) return;
      this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify({
        id: message.id, ...(message.sessionId ? { sessionId: message.sessionId } : {}), result, ...extra,
      }) }));
    }
    close() {
      this.closeCount++;
      this.readyState = 3;
      this.dispatchEvent(new Event('close'));
    }
  }
  return { WebSocket: FakeWebSocket, instances };
}

function fixture({ targets = [], cookies = [], evaluate, intercept, open } = {}) {
  const transport = fakeTransport((message, socket) => {
    if (intercept?.(message, socket)) return;
    switch (message.method) {
      case 'Browser.getVersion': socket.reply(message, { product: 'Chrome/155' }); break;
      case 'Target.getTargets': socket.reply(message, { targetInfos: targets }); break;
      case 'Storage.getCookies': socket.reply(message, { cookies }); break;
      case 'Target.attachToTarget':
        assert.equal(message.params.flatten, true);
        socket.reply(message, { sessionId: `session-${message.params.targetId}` }); break;
      case 'Target.detachFromTarget': socket.reply(message, {}); break;
      case 'Target.createTarget': socket.reply(message, { targetId: 'temporary' }); break;
      case 'Target.closeTarget': socket.reply(message, { success: true }); break;
      case 'Runtime.evaluate': {
        assert.equal(message.params.returnByValue, true);
        assert.equal(message.params.awaitPromise, true);
        assert.ok(message.params.timeout > 0);
        const result = evaluate ? evaluate(message, socket) : state(SECRET);
        if (result !== undefined) socket.reply(message, { result: { value: result } });
        break;
      }
      default: assert.fail(`Unexpected CDP method: ${message.method}`);
    }
  }, { open });
  const options = { WebSocket: transport.WebSocket, waitMs: 100, commandTimeoutMs: 40,
    pageTimeoutMs: 35, cleanupTimeoutMs: 15 };
  return {
    ...transport, options,
    capture: (selected = config) => captureNativeSessions(ENDPOINT, selected, options),
    sent: () => transport.instances[0].sent,
    clean: () => {
      assert.equal(transport.instances[0].readyState, 3);
      assert.equal(transport.instances[0].closeCount, 1);
      assert.equal([...transport.instances[0].listeners.values()].reduce((n, set) => n + set.size, 0), 0);
    },
  };
}

test('only exact selected page targets are attached; an unrelated frozen tab is ignored', async () => {
  const f = fixture({ targets: [
    page('selected'), page('unrelated', 'https://private.example.net/secret'),
    page('lookalike', 'https://panel.example.com.evil.test/'),
    { ...page('worker'), type: 'service_worker' },
    page('scheme', 'http://panel.example.com/'), page('port', 'https://panel.example.com:444/'),
  ] });
  const result = await f.capture();
  assert.deepEqual(f.sent().filter(m => m.method === 'Target.attachToTarget').map(m => m.params.targetId), ['selected']);
  assert.deepEqual(result.sessions.panel.origins, [state(SECRET)]);
  assert.deepEqual(result.warnings, []);
  assert.deepEqual(f.sent().slice(0, 3).map(m => m.method), ['Browser.getVersion', 'Target.getTargets', 'Storage.getCookies']);
  assert.ok(f.sent().every(m => !['Target.setAutoAttach', 'Browser.close', 'Page.navigate', 'Target.activateTarget'].includes(m.method)));
  assert.ok(!JSON.stringify(result.warnings).includes('private.example.net'));
  f.clean();
});

test('frozen selected page evaluation times out and falls back to another matching tab', async () => {
  const f = fixture({ targets: [page('older'), page('frozen')], evaluate: message => {
    if (message.sessionId === 'session-frozen') return undefined;
    return state('older-auth');
  } });
  const start = Date.now();
  const result = await f.capture();
  assert.ok(Date.now() - start < 1000);
  assert.deepEqual(result.sessions.panel.origins, [state('older-auth')]);
  assert.deepEqual(result.warnings.map(w => w.code), ['PAGE_STORAGE_UNAVAILABLE']);
  assert.deepEqual(f.sent().filter(m => m.method === 'Target.detachFromTarget').map(m => m.params.sessionId),
    ['session-frozen', 'session-older']);
  assert.equal(f.sent().filter(m => m.method === 'Target.createTarget').length, 0);
  f.clean();
});

test('frozen attach is bounded, with no auto-attach and a working older-tab fallback', async () => {
  const f = fixture({ targets: [page('older'), page('frozen')], intercept: message =>
    message.method === 'Target.attachToTarget' && message.params.targetId === 'frozen' });
  const result = await f.capture();
  assert.equal(result.sessions.panel.origins.length, 1);
  assert.equal(result.warnings.length, 1);
  f.clean();
});

test('newest nonempty sessionStorage wins, keeping coherent localStorage from that tab', async () => {
  const f = fixture({ targets: [page('old-auth'), page('recent-auth'), page('new-empty')], evaluate: message => {
    const tag = message.sessionId.replace('session-', '');
    return state(tag, tag !== 'new-empty');
  } });
  const { sessions } = await f.capture();
  assert.deepEqual(sessions.panel.origins, [state('recent-auth')]);
  assert.deepEqual(f.sent().filter(m => m.method === 'Target.attachToTarget').map(m => m.params.targetId), ['new-empty', 'recent-auth']);
  f.clean();
});

test('all-empty tabs preserve one coherent newest state and emit an authentication warning', async () => {
  const f = fixture({ targets: [page('old'), page('new')], evaluate: m => state(m.sessionId, false) });
  const result = await f.capture();
  assert.deepEqual(result.sessions.panel.origins, [state('session-new', false)]);
  assert.deepEqual(result.warnings.map(w => w.code), ['EMPTY_SESSION_STORAGE']);
  f.clean();
});

test('cookie domain matching rejects substring/lookalike domains and isolates each service', async () => {
  const domains = ['example.com', '.example.com', 'child.example.com', '.child.example.com',
    'notexample.com', 'example.com.evil.test', 'evil-example.com', 'other.test'];
  const f = fixture({ targets: [page('panel'), page('other', 'https://other.test/')],
    cookies: domains.map(domain => ({ name: 'cookie', value: SECRET, domain, path: '/', size: 111 })),
    evaluate: m => state(SECRET, true, m.sessionId === 'session-other' ? 'https://other.test' : ORIGIN),
  });
  const { sessions } = await f.capture({ ...config, other: { domains: ['other.test'], origins: ['https://other.test'] } });
  assert.deepEqual(sessions.panel.cookies.map(c => c.domain), domains.slice(0, 4));
  assert.deepEqual(sessions.other.cookies.map(c => c.domain), ['other.test']);
  assert.ok(sessions.panel.cookies.every(c => !('size' in c)));
  assert.deepEqual(sessions.other.origins.map(o => o.origin), ['https://other.test']);
  f.clean();
});

test('a response for the wrong flattened session cannot resolve the evaluation', async () => {
  const f = fixture({ targets: [page('selected')], intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    socket.reply(message, { result: { value: state('wrong') } }, { sessionId: 'unrelated-session' });
    socket.reply(message, { result: { value: state('correct') } });
    return true;
  } });
  assert.deepEqual((await f.capture()).sessions.panel.origins, [state('correct')]);
  f.clean();
});

test('navigation after enumeration is checked inside evaluation before storage is read', async () => {
  let reads = 0;
  const forbidden = new Proxy({}, { ownKeys() { reads++; throw Error(SECRET); } });
  const f = fixture({ targets: [page('navigated')], intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    vm.runInNewContext(message.params.expression, {
      location: { origin: 'https://evil.test' }, localStorage: forbidden, sessionStorage: forbidden,
    }).then(value => socket.reply(message, { result: { value } }));
    return true;
  } });
  const result = await f.capture();
  assert.equal(reads, 0);
  assert.deepEqual(result.sessions.panel.origins, []);
  assert.equal(result.warnings[0].code, 'PAGE_STORAGE_UNAVAILABLE');
  assert.equal(f.sent().filter(m => m.method === 'Target.closeTarget').length, 0);
  f.clean();
});

test('untrusted evaluation origin and malformed storage are discarded without leaking raw results', async () => {
  for (const value of [state(SECRET, true, 'https://evil.test'),
    { ...state(SECRET), sessionStorage: [{ name: 'token', value: { secret: SECRET } }] }]) {
    const f = fixture({ targets: [page('selected')], evaluate: () => value });
    const result = await f.capture();
    assert.deepEqual(result.sessions.panel.origins, []);
    assert.ok(!JSON.stringify(result.warnings).includes(SECRET));
    f.clean();
  }
});

test('temporary target uses only an allowed URL, waits for readiness, and is detached then closed', async () => {
  let reads = 0;
  const f = fixture({ targets: [page('unrelated', 'https://private.test')], intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    const location = { origin: 'null' }, document = { readyState: 'loading' };
    const storage = { token: SECRET };
    const wrapped = new Proxy(storage, { ownKeys: target => { reads++; return Reflect.ownKeys(target); },
      get: (target, key) => key === 'getItem' ? name => target[name] : target[key] });
    vm.runInNewContext(message.params.expression, {
      location, document, localStorage: wrapped, sessionStorage: wrapped,
      setTimeout: callback => setTimeout(() => {
        assert.equal(reads, 0);
        location.origin = ORIGIN;
        document.readyState = 'complete';
        callback();
      }, 1),
    }).then(value => socket.reply(message, { result: { value } }));
    return true;
  } });
  const result = await f.capture();
  assert.equal(result.sessions.panel.origins[0].sessionStorage[0].value, SECRET);
  assert.deepEqual(f.sent().find(m => m.method === 'Target.createTarget').params, { url: `${ORIGIN}/dashboard`, background: true });
  assert.deepEqual(f.sent().slice(-2).map(m => m.method), ['Target.detachFromTarget', 'Target.closeTarget']);
  assert.deepEqual(f.sent().find(m => m.method === 'Target.closeTarget').params, { targetId: 'temporary' });
  f.clean();
});

test('temporary target closes after attach/evaluation failures; unsafe check URL falls back to exact origin', async () => {
  for (const method of ['Target.attachToTarget', 'Runtime.evaluate']) {
    const f = fixture({ intercept: (message, socket) => {
      if (message.method !== method) return false;
      socket.reply(message, undefined, { error: { message: SECRET, data: SECRET } });
      return true;
    } });
    const result = await f.capture({ panel: { ...config.panel, check: 'https://evil.test/secret' } });
    assert.equal(f.sent().find(m => m.method === 'Target.createTarget').params.url, ORIGIN);
    assert.equal(f.sent().at(-1).method, 'Target.closeTarget');
    assert.deepEqual(result.sessions.panel.origins, []);
    assert.ok(!JSON.stringify(result).includes(SECRET));
    f.clean();
  }
});

test('temporary evaluation timeout still closes only the created target', async () => {
  const f = fixture({ evaluate: () => undefined });
  const result = await f.capture();
  assert.deepEqual(result.sessions.panel.origins, []);
  assert.deepEqual(f.sent().filter(m => m.method === 'Target.closeTarget').map(m => m.params.targetId), ['temporary']);
  f.clean();
});

test('denial, handshake timeout, closed socket and transport failure reject safely and clean listeners', async () => {
  for (const mode of ['denied', 'timeout', 'closed', 'error', 'root-timeout']) {
    const f = fixture({ intercept: (message, socket) => {
      if (message.method === 'Browser.getVersion') {
        if (mode === 'denied') socket.reply(message, undefined, { error: { message: SECRET } });
        else if (mode === 'closed') socket.close();
        else if (mode === 'error') socket.dispatchEvent(new Event('error'));
        else if (mode === 'root-timeout') socket.reply(message, {});
        return true;
      }
      if (mode === 'root-timeout' && message.method === 'Target.getTargets') return true;
      return false;
    } });
    f.options.waitMs = 30;
    await assert.rejects(f.capture(), error => {
      assert.ok(!String(error).includes(SECRET));
      assert.match(error.message, /^Native capture: (COMMAND_FAILED|COMMAND_TIMEOUT|CONNECTION_CLOSED|CONNECTION_FAILED)$/);
      return true;
    });
    f.clean();
  }
});

test('connection open is bounded and constructor exceptions are sanitized', async () => {
  const f = fixture({ open: false });
  f.options.waitMs = 20;
  await assert.rejects(f.capture(), { code: 'CONSENT_TIMEOUT' });
  f.clean();
  await assert.rejects(captureNativeSessions(ENDPOINT, config, {
    WebSocket: class { constructor() { throw Error(SECRET); } },
  }), error => error.code === 'CONNECTION_FAILED' && !String(error).includes(SECRET));
});

test('invalid endpoints/configuration are rejected before opening a socket', async () => {
  for (const endpoint of ['http://127.0.0.1:9222', 'ws://evil.test:9222/devtools/browser/id',
    `${ENDPOINT}?token=${SECRET}`, 'ws://127.0.0.1:99999/devtools/browser/id']) {
    await assert.rejects(captureNativeSessions(endpoint, config), { code: 'INVALID_ENDPOINT' });
  }
  for (const origins of [['https://panel.example.com/'], ['https://panel.example.com/path'], ['file://'], []]) {
    await assert.rejects(captureNativeSessions(ENDPOINT, { panel: { domains: ['example.com'], origins } }), { code: 'INVALID_SERVICES' });
  }
});

test('native consent handshake uses waitMs rather than the shorter command timeout', async () => {
  const f = fixture({ targets: [page('selected')], intercept: (message, socket) => {
    if (message.method !== 'Browser.getVersion') return false;
    setTimeout(() => socket.reply(message, { product: 'Chrome/155' }), 30);
    return true;
  } });
  f.options.waitMs = 150;
  f.options.commandTimeoutMs = 10;
  assert.equal((await f.capture()).sessions.panel.origins.length, 1);
  f.clean();
});

test('temporary page redirected outside the origin never reads storage and is closed on deadline', async () => {
  let reads = 0;
  const forbidden = new Proxy({}, { ownKeys() { reads++; throw Error(SECRET); } });
  const f = fixture({ intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    vm.runInNewContext(message.params.expression, {
      location: { origin: 'https://accounts.example.net' }, document: { readyState: 'complete' },
      localStorage: forbidden, sessionStorage: forbidden, setTimeout,
    }).then(value => socket.reply(message, { result: { value } }));
    return true;
  } });
  const result = await f.capture();
  assert.equal(reads, 0);
  assert.deepEqual(result.sessions.panel.origins, []);
  assert.equal(f.sent().at(-1).method, 'Target.closeTarget');
  f.clean();
});

test('socket closure rejects an in-flight page request promptly and reports only safe warnings', async () => {
  const f = fixture({ targets: [page('selected')], intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    socket.close();
    return true;
  } });
  f.options.pageTimeoutMs = 5000;
  const start = Date.now();
  const result = await f.capture();
  assert.ok(Date.now() - start < 1000);
  assert.deepEqual(result.sessions.panel.origins, []);
  assert.equal(result.warnings[0].code, 'PAGE_STORAGE_UNAVAILABLE');
  assert.ok(!JSON.stringify(result.warnings).includes(SECRET));
  f.clean();
});

test('evaluation exception details never appear in warning text', async () => {
  const f = fixture({ targets: [page('selected')], intercept: (message, socket) => {
    if (message.method !== 'Runtime.evaluate') return false;
    socket.reply(message, { exceptionDetails: { text: SECRET, exception: { description: SECRET } },
      result: { value: state(SECRET) } });
    return true;
  } });
  const result = await f.capture();
  assert.deepEqual(result.sessions.panel.origins, []);
  assert.ok(!JSON.stringify(result).includes(SECRET));
  f.clean();
});

test('failed temporary close is bounded and reported without propagating protocol errors', async () => {
  const f = fixture({ intercept: (message, socket) => {
    if (message.method !== 'Target.closeTarget') return false;
    socket.reply(message, { success: false, reason: SECRET });
    return true;
  } });
  const result = await f.capture();
  assert.equal(result.warnings.at(-1).code, 'TEMPORARY_TARGET_CLEANUP_FAILED');
  assert.ok(!JSON.stringify(result.warnings).includes(SECRET));
  f.clean();
});
