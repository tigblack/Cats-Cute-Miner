const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../Builds/Web');
const artifacts = path.resolve(__dirname, '../../Docs/Reports/github-pages/update-2026-10-13-settings');
const names = new Set(['local-settings.png', 'local-reset.png', 'public-settings.png', 'public-reset.png', 'public-new-game.png', 'local-new-game.png', 'public-initial.png', 'local-initial.png']);
const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.wasm': 'application/wasm', '.css': 'text/css', '.png': 'image/png' };
http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
  res.setHeader('Access-Control-Allow-Private-Network', 'true');
  res.setHeader('Cache-Control', 'no-store');
  if (req.method === 'OPTIONS') { res.writeHead(204); return res.end(); }
  const pathname = new URL(req.url, 'http://localhost').pathname;
  if (req.method === 'POST' && pathname.startsWith('/evidence/')) {
    const name = pathname.slice('/evidence/'.length);
    if (!names.has(name)) { res.writeHead(404); return res.end(); }
    const parts = []; let bytes = 0;
    req.on('data', part => { bytes += part.length; if (bytes >= 4000000) { req.destroy(); return; } parts.push(part); });
    req.on('end', () => {
      const image = Buffer.concat(parts);
      if (!image.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) { res.writeHead(400); return res.end(); }
      fs.mkdirSync(artifacts, { recursive: true });
      fs.writeFileSync(path.join(artifacts, name), image);
      res.writeHead(204); res.end();
    });
    return;
  }
  if (req.method !== 'GET') { res.writeHead(405); return res.end(); }
  if (pathname === '/') { res.writeHead(302, { Location: '/Cat-Miners/' }); return res.end(); }
  if (!pathname.startsWith('/Cat-Miners/')) { res.writeHead(404); return res.end(); }
  let file;
  try { file = path.resolve(root, decodeURIComponent(pathname.slice('/Cat-Miners/'.length)) || 'index.html'); }
  catch { res.writeHead(400); return res.end(); }
  if (!file.startsWith(root + path.sep)) { res.writeHead(403); return res.end(); }
  fs.stat(file, (error, stat) => {
    if (error || !stat.isFile()) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Content-Length': stat.size });
    fs.createReadStream(file).pipe(res);
  });
}).listen(8097, '127.0.0.1', () => console.log('Settings evidence: http://localhost:8097/Cat-Miners/'));
