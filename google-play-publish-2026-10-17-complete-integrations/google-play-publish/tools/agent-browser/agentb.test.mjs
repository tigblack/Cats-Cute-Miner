import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, readdirSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const cli = fileURLToPath(new URL('./agentb.mjs', import.meta.url));

// Подменён только SSH-канал; настоящий CLI валидирует и сохраняет переданные данные.
for (const scenario of ['approved', 'approved-project', 'denied', 'denied-exit', 'wrong-domain', 'wrong-origin', 'invalid-tab-storage']) {
  test(`hub session transfer: ${scenario}`, { skip: process.platform === 'win32' }, () => {
    const approved = scenario === 'approved' || scenario === 'approved-project';
    const denied = scenario === 'denied' || scenario === 'denied-exit';
    const home = mkdtempSync(path.join(tmpdir(), 'agentb-transfer-'));
    try {
      const bin = path.join(home, 'bin');
      mkdirSync(bin);
      writeFileSync(path.join(bin, 'on-remote'), `#!${process.execPath}
import fs from 'node:fs';
if (process.env.TEST_SCENARIO === 'approved-project') {
  if (process.argv.includes('--pc-home') || !process.argv.includes('/project with spaces/agentb.mjs') || !process.argv.includes('AGENTB_HOME=/project with spaces/private')) process.exit(90);
} else if (!process.argv.includes('--pc-home')) process.exit(90);
if (process.argv.includes('firerange')) process.exit(90);
const transfer = process.argv.includes('_transfer-sessions');
fs.appendFileSync(process.env.AGENTB_HOME + '/calls', transfer ? 'transfer\\n' : 'consent\\n');
const mode = process.env.TEST_SCENARIO;
if (!transfer) { console.log(JSON.stringify(['denied','denied-exit'].includes(mode) ? {ok:false,error:'denied'} : {ok:true,saved:{adjust:{cookies:1}}})); if (mode === 'denied-exit') process.exitCode = 1; }
else console.log(JSON.stringify({ok:true,sessions:{adjust:{cookies:[{name:'session',value:'private-sentinel',domain:mode === 'wrong-domain' ? '.facebook.com' : '.adjust.com',path:'/',expires:-1,httpOnly:true,secure:true,sameSite:'Lax'}],origins:[{origin:mode === 'wrong-origin' ? 'https://facebook.com' : 'https://suite.adjust.com',localStorage:[{name:'local-session',value:'private-sentinel'}],sessionStorage:[{name:'tab-session',value:mode === 'invalid-tab-storage' ? 123 : 'private-tab-sentinel'}]}]}}}));
`, { mode: 0o700 });
      const result = spawnSync(process.execPath, [cli, 'login-hub', 'adjust', '--no-check'], {
        env: { ...process.env, AGENTB_HOME: home, TEST_SCENARIO: scenario, AGENTB_REMOTE_TOOL: scenario === 'approved-project' ? '/project with spaces/agentb.mjs' : '', AGENTB_REMOTE_HOME: scenario === 'approved-project' ? '/project with spaces/private' : '', PATH: bin + path.delimiter + process.env.PATH },
        encoding: 'utf8', timeout: 10000,
      });
      assert.equal(result.status, approved ? 0 : 1, result.stderr);
      assert.ok(!result.stdout.includes('private-sentinel'));
      assert.ok(!result.stdout.includes('private-tab-sentinel'));
      if (denied) assert.equal(JSON.parse(result.stdout).error, 'denied');
      const calls = readFileSync(path.join(home, 'calls'), 'utf8');
      if (denied) assert.equal(calls, 'consent\n');
      else assert.equal(calls, 'consent\ntransfer\n');
      const session = path.join(home, 'sessions', JSON.parse(result.stdout).sessionId || 'absent', 'adjust.json');
      if (approved) {
        assert.equal(JSON.parse(readFileSync(session, 'utf8')).cookies[0].value, 'private-sentinel');
        assert.equal(statSync(session).mode & 0o777, 0o600);
        assert.equal(JSON.parse(result.stdout).destination, 'hub');
        const reused = spawnSync(process.execPath, [cli, 'login-hub', 'adjust', '--no-check'], {
          env: { ...process.env, AGENTB_HOME: home, PATH: bin + path.delimiter + process.env.PATH }, encoding: 'utf8', timeout: 10000,
        });
        assert.equal(reused.status, 0, reused.stdout + reused.stderr);
        assert.deepEqual(JSON.parse(reused.stdout).reused, ['adjust']);
        assert.equal(readFileSync(path.join(home, 'calls'), 'utf8'), calls, 'reuse must not contact the PC');
        const probe = path.join(home, 'verify-storage.mjs');
        writeFileSync(probe, `import assert from 'node:assert/strict';
export default async ({ page, context }) => {
  await context.route('**/*', route => route.fulfill({contentType:'text/html',body:'<!doctype html><title>Storage test</title>'}));
  await page.goto('https://suite.adjust.com/');
  assert.equal(await page.evaluate(() => localStorage.getItem('local-session')), 'private-sentinel');
  assert.equal(await page.evaluate(() => sessionStorage.getItem('tab-session')), 'private-tab-sentinel');
  await page.goto('https://unrelated.example/');
  assert.equal(await page.evaluate(() => sessionStorage.getItem('tab-session')), null);
  await page.goto('https://suite.adjust.com/');
  await page.evaluate(() => {
    localStorage.setItem('local-session','renewed-local');
    sessionStorage.setItem('tab-session','renewed-tab');
  });
  return {storageRestored:true,otherOriginsIsolated:true};
}`);
        const restored = spawnSync(process.execPath, [cli, 'run', probe, '--service', 'adjust'], {
          env: { ...process.env, AGENTB_HOME: home }, encoding: 'utf8', timeout: 20000,
        });
        assert.equal(restored.status, 0, restored.stdout + restored.stderr);
        assert.equal(JSON.parse(restored.stdout).result.storageRestored, true);
        assert.ok(!restored.stdout.includes('private-tab-sentinel'));
        const refreshed = JSON.parse(readFileSync(session, 'utf8'));
        assert.equal(refreshed.origins[0].localStorage[0].value, 'renewed-local');
        assert.equal(refreshed.origins[0].sessionStorage[0].value, 'renewed-tab');
        const rejectedRefresh = spawnSync(process.execPath, [cli, 'login-hub', 'adjust', '--refresh', '--no-check'], {
          env: { ...process.env, AGENTB_HOME: home, TEST_SCENARIO: 'denied', PATH: bin + path.delimiter + process.env.PATH }, encoding: 'utf8', timeout: 10000,
        });
        assert.equal(rejectedRefresh.status, 1, rejectedRefresh.stdout);
        assert.equal(JSON.parse(rejectedRefresh.stdout).ok, false);
        const differentChat = spawnSync(process.execPath, [cli, 'sessions', '--session', 'other-chat'], {
          env: { ...process.env, AGENTB_HOME: home }, encoding: 'utf8', timeout: 10000,
        });
        assert.deepEqual(JSON.parse(differentChat.stdout).saved, {});
        const clearOther = spawnSync(process.execPath, [cli, 'session-clear', '--session', 'other-chat'], {
          env: { ...process.env, AGENTB_HOME: home }, encoding: 'utf8', timeout: 10000,
        });
        assert.equal(clearOther.status, 0);
        assert.ok(statSync(session).isFile(), 'clearing another chat must preserve this chat');
        const meta = path.join(path.dirname(session), '.session.json');
        writeFileSync(meta, JSON.stringify({expiresAt:'2000-01-01T00:00:00.000Z'}));
        const expired = spawnSync(process.execPath, [cli, 'sessions'], {
          env: { ...process.env, AGENTB_HOME: home }, encoding: 'utf8', timeout: 10000,
        });
        assert.deepEqual(JSON.parse(expired.stdout).saved, {});
        assert.deepEqual(readdirSync(path.join(home,'sessions')), [], 'expired credentials must be removed');
      } else assert.ok(!readdirSync(home).includes('sessions'));
    } finally { rmSync(home, { recursive: true, force: true }); }
  });
}

test('read-only install check on the hub does not create a browser profile', { skip: process.platform === 'win32' }, () => {
  const home = mkdtempSync(path.join(tmpdir(), 'agentb-install-'));
  try {
    const result = spawnSync(process.execPath, [cli, 'install'], {
      env: { ...process.env, AGENTB_HOME: home }, encoding: 'utf8', timeout: 10000,
    });
    assert.equal(result.status, 1);
    assert.equal(JSON.parse(result.stdout).ok, false);
    assert.deepEqual(readdirSync(home), []);
  } finally { rmSync(home, { recursive: true, force: true }); }
});
