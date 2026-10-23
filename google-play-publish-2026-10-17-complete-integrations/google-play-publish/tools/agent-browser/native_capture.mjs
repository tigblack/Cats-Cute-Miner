// Scoped, in-memory capture through Chrome's owner-approved browser WebSocket.
// No discovery, profile reads, persistence, CLI, global auto-attach or page mutation.
const failure = code => Object.assign(new Error(`Native capture: ${code}`), { code });
const duration = (value, fallback) => {
  if (value === undefined) return fallback;
  if (!Number.isSafeInteger(value) || value <= 0 || value > 2147483647) throw failure('INVALID_TIMEOUT');
  return value;
};
const originOf = url => { try { return new URL(url).origin; } catch { return null; } };
const domainMatches = (domain, allowed) => typeof domain === 'string'
  && allowed.some(base => domain.replace(/^\./, '').toLowerCase() === base
    || domain.toLowerCase().endsWith(`.${base}`));
const storageEntries = entries => Array.isArray(entries)
  && entries.every(item => item && typeof item.name === 'string' && typeof item.value === 'string');

function validate(endpoint, config) {
  // Accept precisely the trusted ownerEndpoint route, without a discovery request.
  if (typeof endpoint !== 'string'
    || !/^ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(endpoint)) {
    throw failure('INVALID_ENDPOINT');
  }
  const port = Number(/^ws:\/\/127\.0\.0\.1:(\d+)/.exec(endpoint)[1]);
  if (port < 1 || port > 65535) throw failure('INVALID_ENDPOINT');
  if (!config || typeof config !== 'object' || Array.isArray(config) || !Object.keys(config).length) {
    throw failure('INVALID_SERVICES');
  }
  return Object.entries(config).map(([name, service]) => {
    if (!service || !Array.isArray(service.origins) || !service.origins.length
      || service.origins.some(origin => typeof origin !== 'string'
        || !/^https?:\/\//.test(origin) || originOf(origin) !== origin)
      || !Array.isArray(service.domains) || !service.domains.length
      || service.domains.some(domain => typeof domain !== 'string'
        || !/^[a-z0-9]+(?:[.-][a-z0-9]+)*$/i.test(domain))) throw failure('INVALID_SERVICES');
    return { name, origins: [...new Set(service.origins)], domains: service.domains.map(d => d.toLowerCase()), check: service.check };
  });
}

function connection(endpoint, WebSocketImpl) {
  let socket;
  try { socket = new WebSocketImpl(endpoint); } catch { throw failure('CONNECTION_FAILED'); }
  let nextId = 0, stopped = false, opened = socket.readyState === 1;
  const pending = new Map();
  const openWaiters = new Set();
  const failAll = code => {
    stopped = true;
    for (const waiter of openWaiters) waiter.reject(failure(code));
    openWaiters.clear();
    for (const request of pending.values()) request.reject(failure(code));
    pending.clear();
  };
  const onOpen = () => {
    opened = true;
    for (const waiter of openWaiters) waiter.resolve();
    openWaiters.clear();
  };
  const onClose = () => failAll('CONNECTION_CLOSED');
  const onError = () => failAll('CONNECTION_FAILED');
  const onMessage = event => {
    let message;
    try { message = JSON.parse(event.data); } catch { return; }
    const request = pending.get(message?.id);
    // Flattened responses must belong to the exact session that sent the command.
    if (!request || message.sessionId !== request.sessionId) return;
    if (message.error) request.reject(failure('COMMAND_FAILED'));
    else request.resolve(message.result);
  };
  const listeners = { open: onOpen, close: onClose, error: onError, message: onMessage };
  for (const [type, listener] of Object.entries(listeners)) socket.addEventListener(type, listener);
  return {
    async open(timeout) {
      if (stopped || socket.readyState > 1) throw failure('CONNECTION_CLOSED');
      if (opened) return;
      await new Promise((resolve, reject) => {
        const timer = setTimeout(() => waiter.reject(failure('CONSENT_TIMEOUT')), timeout);
        const waiter = {
          resolve: () => { clearTimeout(timer); openWaiters.delete(waiter); resolve(); },
          reject: error => { clearTimeout(timer); openWaiters.delete(waiter); reject(error); },
        };
        openWaiters.add(waiter);
      });
    },
    request(method, params = {}, sessionId, timeout = 5000) {
      if (stopped || socket.readyState !== 1) return Promise.reject(failure('CONNECTION_CLOSED'));
      return new Promise((resolve, reject) => {
        const id = ++nextId;
        const finish = callback => value => { clearTimeout(timer); pending.delete(id); callback(value); };
        const timer = setTimeout(() => pending.get(id)?.reject(failure('COMMAND_TIMEOUT')), timeout);
        pending.set(id, { sessionId, resolve: finish(resolve), reject: finish(reject) });
        try { socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })); }
        catch { pending.get(id)?.reject(failure('CONNECTION_FAILED')); }
      });
    },
    dispose() {
      failAll('CONNECTION_CLOSED');
      for (const [type, listener] of Object.entries(listeners)) socket.removeEventListener(type, listener);
      try { if (socket.readyState < 2) socket.close(); } catch { /* Never propagate transport details. */ }
    },
  };
}

function expressionFor(origin, temporary, timeout) {
  // Checks precede every storage read, including after a temporary page navigates.
  return `(async () => {
    const expected = ${JSON.stringify(origin)};
    const deadline = Date.now() + ${timeout};
    ${temporary ? `while (location.origin !== expected || document.readyState === 'loading') {
      if (Date.now() >= deadline) return null;
      await new Promise(resolve => setTimeout(resolve, 50));
    }` : ''}
    if (location.origin !== expected) return null;
    const read = storage => Object.keys(storage).map(name => ({ name, value: storage.getItem(name) }));
    const state = { origin: location.origin, localStorage: read(localStorage), sessionStorage: read(sessionStorage) };
    return location.origin === expected ? state : null;
  })()`;
}

/**
 * captureNativeSessions(ownerEndpoint(), selectedServicesConfig, options?)
 * Resolves { sessions: { [service]: { cookies, origins } }, warnings } in memory.
 * Caller retains owner consent messaging and validated private persistence.
 * options: WebSocket (constructor injection), waitMs (consent, default 180000),
 * commandTimeoutMs (5000), pageTimeoutMs (5000), cleanupTimeoutMs (1000).
 * Root/connection failures throw sanitized errors; page failures yield safe warnings.
 */
export async function captureNativeSessions(endpoint, selectedServicesConfig, options = {}) {
  const services = validate(endpoint, selectedServicesConfig);
  const waitMs = duration(options.waitMs, 180000);
  const commandMs = duration(options.commandTimeoutMs, 5000);
  const pageMs = duration(options.pageTimeoutMs, 5000);
  const cleanupMs = duration(options.cleanupTimeoutMs, 1000);
  const WebSocketImpl = options.WebSocket ?? globalThis.WebSocket;
  if (typeof WebSocketImpl !== 'function') throw failure('WEBSOCKET_UNAVAILABLE');
  const client = connection(endpoint, WebSocketImpl);
  const warnings = [];
  const warn = (service, origin, code) => warnings.push({ service, origin, code });
  try {
    const consentDeadline = Date.now() + waitMs;
    await client.open(waitMs);
    // The first root command also waits for Chrome's native Allow prompt.
    await client.request('Browser.getVersion', {}, undefined, Math.max(1, consentDeadline - Date.now()));
    const { targetInfos } = await client.request('Target.getTargets', {}, undefined, commandMs) || {};
    if (!Array.isArray(targetInfos)) throw failure('INVALID_TARGETS');
    const { cookies } = await client.request('Storage.getCookies', {}, undefined, commandMs) || {};
    if (!Array.isArray(cookies)) throw failure('INVALID_COOKIES');

    const readTarget = async (targetId, origin, temporary) => {
      const deadline = Date.now() + pageMs;
      const remaining = () => Math.max(1, deadline - Date.now());
      let sessionId;
      try {
        ({ sessionId } = await client.request('Target.attachToTarget', { targetId, flatten: true }, undefined, remaining()) || {});
        if (typeof sessionId !== 'string' || !sessionId) throw failure('INVALID_ATTACHMENT');
        const result = await client.request('Runtime.evaluate', {
          expression: expressionFor(origin, temporary, remaining()),
          returnByValue: true, awaitPromise: true, timeout: remaining(),
        }, sessionId, remaining());
        const value = result?.result?.value;
        if (result?.exceptionDetails || value?.origin !== origin
          || !storageEntries(value?.localStorage) || !storageEntries(value?.sessionStorage)) {
          throw failure('STORAGE_UNAVAILABLE');
        }
        // Rebuild just the validated shape; never merge storage between tabs.
        return { origin, localStorage: value.localStorage.map(({ name, value }) => ({ name, value })),
          sessionStorage: value.sessionStorage.map(({ name, value }) => ({ name, value })) };
      } finally {
        if (sessionId) await client.request('Target.detachFromTarget', { sessionId }, undefined, cleanupMs).catch(() => {});
      }
    };

    const sessions = Object.create(null);
    for (const service of services) {
      const origins = [];
      for (const origin of service.origins) {
        // CDP exposes no reliable activation timestamp. Reverse discovery order is
        // a best-effort newest-first preference; authenticated storage wins.
        const candidates = targetInfos.filter(target => target?.type === 'page'
          && typeof target.targetId === 'string' && originOf(target.url) === origin).reverse();
        let chosen;
        for (const target of candidates) {
          try {
            const state = await readTarget(target.targetId, origin, false);
            chosen ??= state;
            if (state.sessionStorage.length) { chosen = state; break; }
          } catch { warn(service.name, origin, 'PAGE_STORAGE_UNAVAILABLE'); }
        }
        if (!candidates.length) {
          let createdId;
          try {
            const url = originOf(service.check) === origin ? service.check : origin;
            ({ targetId: createdId } = await client.request('Target.createTarget', { url, background: true }, undefined, commandMs) || {});
            if (typeof createdId !== 'string' || !createdId) throw failure('INVALID_TARGET');
            chosen = await readTarget(createdId, origin, true);
          } catch { warn(service.name, origin, 'PAGE_STORAGE_UNAVAILABLE'); }
          finally {
            if (createdId) {
              try {
                const result = await client.request('Target.closeTarget', { targetId: createdId }, undefined, cleanupMs);
                if (result?.success !== true) throw failure('TEMPORARY_TARGET_CLEANUP_FAILED');
              }
              catch { warn(service.name, origin, 'TEMPORARY_TARGET_CLEANUP_FAILED'); }
            }
          }
        }
        if (chosen) {
          origins.push(chosen);
          if (!chosen.sessionStorage.length) warn(service.name, origin, 'EMPTY_SESSION_STORAGE');
        }
      }
      sessions[service.name] = {
        cookies: cookies.filter(cookie => cookie && typeof cookie.name === 'string' && typeof cookie.value === 'string'
          && domainMatches(cookie.domain, service.domains)).map(cookie => {
          const copy = {};
          for (const key of ['name', 'value', 'domain', 'path', 'expires', 'httpOnly', 'secure', 'sameSite']) {
            if (cookie[key] !== undefined) copy[key] = cookie[key];
          }
          return copy;
        }),
        origins,
      };
    }
    return { sessions, warnings };
  } finally { client.dispose(); }
}
