// Local static server for the WebGL builds.
//   node Tools/Web/serve.cjs                      -> Builds/Web at http://localhost:8095/Cat-Miners/ (unchanged default)
//   node Tools/Web/serve.cjs --playable           -> also Builds/Playable at http://localhost:8095/Cat-Miners/playable/
//   node Tools/Web/serve.cjs --playable --port N  -> same on port N (or CATMINERS_WEB_PORT=N)
// The playable route sends Content-Encoding: gzip for gzip-compressed .unityweb/.gz files, so the browser unpacks them
// (no JS decompression fallback) and .wasm gets application/wasm for streaming compilation. The main route keeps its
// previous headers: Builds/Web relies on Unity's decompression fallback, as on GitHub Pages.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const args = process.argv.slice(2);
const option = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const root = path.resolve(__dirname, '../../Builds/Web');
const prefix = '/Cat-Miners/';
const playableRoot = path.resolve(__dirname, '../../Builds/Playable');
const playablePrefix = prefix + 'playable/';
const playable = args.includes('--playable') || process.env.CATMINERS_WEB_PLAYABLE === '1';
const port = Number(option('--port') || process.env.CATMINERS_WEB_PORT || 8095);
const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.json': 'application/json', '.css': 'text/css', '.wasm': 'application/wasm', '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml' };
const compressed = new Set(['.unityweb', '.gz']);

// Content-Type of a compressed file comes from the inner extension: x.wasm.unityweb -> application/wasm.
function contentType(file) {
  const ext = path.extname(file);
  if (!compressed.has(ext)) return types[ext];
  return types[path.extname(path.basename(file, ext))];
}

// Only gzip streams (magic 1f 8b) are marked: Brotli has no signature and needs HTTPS, so it is left to the fallback.
function isGzip(file, callback) {
  fs.open(file, 'r', (error, fd) => {
    if (error) return callback(false);
    const head = Buffer.alloc(2);
    fs.read(fd, head, 0, 2, 0, (readError, bytes) => {
      fs.close(fd, () => callback(!readError && bytes === 2 && head[0] === 0x1f && head[1] === 0x8b));
    });
  });
}

function serve(res, base, relative, encoded) {
  const file = path.resolve(base, relative);
  if (!file.startsWith(base + path.sep)) { res.writeHead(403); return res.end(); }
  fs.stat(file, (error, stat) => {
    if (error || !stat.isFile()) { res.writeHead(404); return res.end(); }
    const send = (gzip) => {
      const headers = { 'Content-Type': (encoded ? contentType(file) : types[path.extname(file)]) || 'application/octet-stream', 'Content-Length': stat.size, 'Cache-Control': 'no-store' };
      if (gzip) headers['Content-Encoding'] = 'gzip';
      res.writeHead(200, headers);
      fs.createReadStream(file).pipe(res);
    };
    if (encoded && compressed.has(path.extname(file))) isGzip(file, send);
    else send(false);
  });
}

http.createServer((req, res) => {
  const pathname = new URL(req.url, 'http://localhost').pathname;
  if (pathname === '/' || pathname === '/Cat-Miners') {
    res.writeHead(302, { Location: prefix });
    return res.end();
  }
  if (playable && pathname === playablePrefix.slice(0, -1)) {
    res.writeHead(302, { Location: playablePrefix });
    return res.end();
  }
  const inPlayable = playable && pathname.startsWith(playablePrefix);
  if (!inPlayable && !pathname.startsWith(prefix)) { res.writeHead(404); return res.end(); }
  let relative;
  try { relative = decodeURIComponent(pathname.slice((inPlayable ? playablePrefix : prefix).length)) || 'index.html'; }
  catch { res.writeHead(400); return res.end(); }
  serve(res, inPlayable ? playableRoot : root, relative, inPlayable);
}).listen(port, '127.0.0.1', () => {
  console.log(`Cat Miners: http://localhost:${port}${prefix}`);
  if (playable) console.log(`Cat Miners playable: http://localhost:${port}${playablePrefix}`);
});
