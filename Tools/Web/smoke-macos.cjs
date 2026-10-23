// Real Chromium/WebGL smoke check, with an isolated browser profile and read-only IndexedDB evidence.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const { chromium } = require(path.join(root, 'google-play-publish-2026-10-17-complete-integrations/google-play-publish/tools/agent-browser/node_modules/playwright-core'));
const arg = name => { const at = process.argv.indexOf(name); return at < 0 ? undefined : process.argv[at + 1]; };
const label = arg('--label') || 'local';
if (!/^[a-z0-9-]+$/.test(label)) throw new Error('Invalid evidence label');
const reports = path.resolve(root, arg('--reports') || `Docs/Reports/webgl-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}`);
fs.mkdirSync(reports, { recursive: true });

async function saved(page) {
  return page.evaluate(async () => {
    const databases = await indexedDB.databases();
    const rows = [];
    for (const info of databases) {
      if (!info.name.includes('/idbfs')) continue;
      const db = await new Promise((resolve, reject) => {
        const request = indexedDB.open(info.name);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
      if (!db.objectStoreNames.contains('FILE_DATA')) { db.close(); continue; }
      await new Promise((resolve, reject) => {
        const request = db.transaction('FILE_DATA').objectStore('FILE_DATA').openCursor();
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
          const cursor = request.result;
          if (!cursor) return resolve();
          if (String(cursor.key).endsWith('/CatOutpostV2/main/state.json')) {
            const envelope = JSON.parse(new TextDecoder().decode(cursor.value.contents));
            const state = JSON.parse(envelope.Payload);
            rows.push({ commit: envelope.CommitSequence, balanceVersion: envelope.BalanceVersion,
              seed: String(state.Profile.Seed), runId: state.Run.RunId, zone: state.Profile.CurrentZone,
              ticks: state.Run.Now, playTimeMs: state.Profile.PlayTimeMs,
              targetsDestroyed: state.Run.Targets.filter(t => t.Destroyed).length,
              targetCount: state.Run.Targets.length, layout: state.Run.MapTopology?.Layout,
              mineWidth: state.Run.MapTopology?.Width, mineDepth: state.Run.MapTopology?.Depth, elixir: state.Profile.Elixir, gems: state.Profile.Gems,
              baseBranches: state.Profile.Progression.Unlocked.filter(id => id.startsWith("branch.")).sort(),
              totalRemainingHp: state.Run.Targets.reduce((sum, t) => sum + t.RemainingHpUnits, 0) });
          }
          cursor.continue();
        };
      });
      db.close();
    }
    return rows.sort((a, b) => b.commit - a.commit)[0] || null;
  });
}

(async () => {
  let server, browser;
  const failures = [], exceptions = [], requests = [], httpErrors = [];
  const report = { at: new Date().toISOString(), label, fullGame: true, headlessChromium: true, realMobileDevice: false };
  try {
    let url = arg('--url');
    if (!url) {
      const base = path.resolve(root, arg('--build-dir') || 'Builds/Web');
      const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.png': 'image/png', '.mp4': 'video/mp4' };
      server = http.createServer((req, res) => {
        const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
        const file = path.resolve(base, '.' + (pathname === '/' ? '/index.html' : pathname));
        if (!file.startsWith(base + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); return res.end(); }
        res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
        fs.createReadStream(file).pipe(res);
      });
      await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
      url = `http://127.0.0.1:${server.address().port}/`;
    }
    report.url = url;
    browser = await chromium.launch({ executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
      headless: true, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required'] });
    const context = await browser.newContext({ viewport: { width: 1100, height: 1000 }, deviceScaleFactor: 1 });
    const page = await context.newPage();
    page.on('pageerror', error => exceptions.push(error.message));
    page.on('console', message => { if (message.type() === 'error') failures.push(message.text()); });
    page.on('requestfailed', request => requests.push({ url: request.url(), reason: request.failure()?.errorText }));
    page.on('response', response => { if (response.status() >= 400) httpErrors.push({ url: response.url(), status: response.status() }); });
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 120000 });
    await page.waitForFunction(() => !!window.unityInstance, { timeout: 180000 });
    await page.bringToFront();
    await page.waitForTimeout(4000);
    report.version = await page.evaluate(() => config.productVersion);
    report.canvas = await page.locator('#unity-canvas').boundingBox();
    await page.screenshot({ path: path.join(reports, `${label}-initial.png`) });
    await page.waitForTimeout(12000);
    report.beforeReload = await saved(page);
    assert(report.beforeReload, 'No durable main save in IndexedDB');
    assert(report.beforeReload.playTimeMs > 0, 'Gameplay time did not advance');
    assert(report.beforeReload.ticks > 0, 'Simulation did not advance');
    if (process.argv.includes('--all-base-open')) {
      assert.equal(report.beforeReload.baseBranches.length, 7, 'All seven Base branches must be open in Mine 1');
      assert.equal(report.beforeReload.zone, 1);
      report.allBaseBranchesOpenFromMine1 = true;
    }
    if (process.argv.includes('--mia-two-lane')) {
      assert.equal(report.version, arg('--expected-version') || '0.1.18');
      assert.equal(report.beforeReload.targetCount, 15);
      assert.equal(report.beforeReload.layout, 'mia-two-lane-mine-2');
      assert.equal(report.beforeReload.mineWidth, 13);
      assert.equal(report.beforeReload.mineDepth, 11);
      report.miaTwoLaneMine = true;
    }
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 120000 });
    await page.waitForFunction(() => !!window.unityInstance, { timeout: 180000 });
    await page.waitForTimeout(4000);
    report.afterReload = await saved(page);
    assert.equal(report.afterReload.seed, report.beforeReload.seed);
    assert.equal(report.afterReload.runId, report.beforeReload.runId);
    assert(report.afterReload.playTimeMs >= report.beforeReload.playTimeMs, 'Gameplay time was reset');
    assert(report.afterReload.ticks >= report.beforeReload.ticks, 'Simulation progress was reset');
    report.persistenceAcrossReload = true;
    await page.screenshot({ path: path.join(reports, `${label}-reloaded.png`) });
    // A normal reload can display the offline earnings summary above the toolbar.
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
    const controls = [['store', 0.10, 0.945], ['cards', 0.26, 0.945], ['base', 0.49, 0.945], ['settings', 0.94, 0.035]];
    const board = await page.locator('#unity-canvas').boundingBox();
    for (const [name, x, y] of controls) {
      await page.mouse.click(board.x + board.width * x, board.y + board.height * y);
      await page.waitForTimeout(700);
      await page.screenshot({ path: path.join(reports, `${label}-${name}.png`) });
      await page.keyboard.press('Escape');
      await page.waitForTimeout(500);
    }
    report.uiScreenshotChecks = controls.map(([name]) => name);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(2000);
    const box = await page.locator('#unity-canvas').boundingBox();
    assert(box.width <= 390 && box.height <= 844 && box.x >= 0 && box.y >= 0, 'Canvas does not fit phone viewport');
    report.mobileViewport = { width: 390, height: 844, canvas: box };
    await page.screenshot({ path: path.join(reports, `${label}-mobile.png`) });
    report.success = true;
  } catch (error) {
    report.success = false;
    report.failure = error.message;
    process.exitCode = 1;
  } finally {
    report.consoleErrors = failures;
    report.pageExceptions = exceptions;
    report.failedRequests = requests;
    report.httpErrors = httpErrors;
    if (exceptions.length) { report.success = false; process.exitCode = 1; }
    fs.writeFileSync(path.join(reports, `${label}-browser.json`), JSON.stringify(report, null, 2) + '\n');
    if (browser) await browser.close();
    if (server) await new Promise(resolve => server.close(resolve));
    console.log(JSON.stringify({ success: report.success, failure: report.failure, version: report.version,
      persistenceAcrossReload: report.persistenceAcrossReload, consoleErrors: failures.length,
      pageExceptions: exceptions.length, evidence: path.relative(root, path.join(reports, `${label}-browser.json`)) }));
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
