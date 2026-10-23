// One-shot client for the already installed agent Chrome MCP; no new browser/profile/server install.
// node chrome-mcp-call.mjs <request.local.json>; never put credentials in a request/log.
import {readFileSync} from 'node:fs';
import {join, dirname, resolve} from 'node:path';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';

const request = JSON.parse(readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
const packageFile = join(process.env.LOCALAPPDATA, 'npm-cache', '_npx', '15c61037b1978c83', 'node_modules', 'chrome-devtools-mcp', 'package.json');
const pkg = JSON.parse(readFileSync(packageFile, 'utf8'));
const bin = typeof pkg.bin === 'string' ? pkg.bin : pkg.bin['chrome-devtools-mcp'];
const child = spawn(process.execPath, [resolve(dirname(packageFile), bin), '--browserUrl', 'http://127.0.0.1:9333', '--no-usage-statistics', '--redactNetworkHeaders'], {stdio:['pipe','pipe','pipe'], windowsHide:true});
const pending = new Map();
let next = 1;
const rpc = (method, params) => new Promise((resolveRpc, reject) => {
  const id = next++;
  const timer = setTimeout(() => {pending.delete(id); reject(new Error('MCP request timed out: '+method));}, 45000);
  pending.set(id, {resolve:resolveRpc,reject,timer});
  child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');
});
createInterface({input:child.stdout}).on('line', line => {
  let message; try {message=JSON.parse(line);} catch {return;}
  const p = pending.get(message.id); if (!p) return;
  clearTimeout(p.timer); pending.delete(message.id);
  if (message.error) p.reject(new Error(JSON.stringify(message.error))); else p.resolve(message.result);
});
// stderr may contain browser/network diagnostics; do not export it into reports.
child.stderr.resume();
child.on('error', error => { for (const p of pending.values()) {clearTimeout(p.timer);p.reject(error);} pending.clear(); });
const safeText = result => (result.content ?? []).filter(c=>c.type==='text').map(c=>c.text).join('\n');
try {
  await rpc('initialize',{protocolVersion:'2024-11-05',capabilities:{},clientInfo:{name:'CatsCuteMinerRelease',version:'1'}});
  child.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
  if (request.schema) {
    const result = await rpc('tools/list',{});
    console.log(JSON.stringify(result.tools.filter(t=>request.schema.includes(t.name))));
  } else {
    for (const action of request.requests ?? [request]) {
    const args = {...(action.arguments ?? {})};
    if (action.pageUrlIncludes) {
      const pages = await rpc('tools/call',{name:'list_pages',arguments:{}});
      const matches = safeText(pages).split('\n').filter(line=>line.includes(action.pageUrlIncludes) && /^\d+:/.test(line.trim()));
      if (matches.length !== 1) throw new Error('Expected one task page with the supplied URL; read list_pages first.');
      args.pageId = Number(matches[0].trim().match(/^(\d+):/)[1]);
    }
    const result = await rpc('tools/call',{name:action.tool,arguments:args});
    console.log(JSON.stringify({tool:action.tool,page:action.pageUrlIncludes,text:safeText(result).slice(0,action.outputLimit ?? 16000)}));
    if (result.isError) process.exitCode=1;
    }
  }
} catch (error) {console.error(error.message); process.exitCode=1;}
finally {child.stdin.end();child.kill();}
