/* Cat Miners playable (short / medium / long). Один файл без зависимостей: canvas 2D + DOM-интерфейс.
   Docs/Playable/PLAYABLE_ADS.md. Конфигурация — window.CM (сборка Tools/Playable/build_playable.py). */
(function () {
  'use strict';
  const CFG = window.CM;
  const VARIANT = CFG.variant;
  const SPR = CFG.spr;
  const UIMG = CFG.ui;
  const T = 32;

  // ───────────────────────── утилиты ─────────────────────────
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const easeOut = (t) => 1 - (1 - t) * (1 - t);
  function rng(seed) {
    let a = seed >>> 0;
    return function () {
      a = (a + 0x6d2b79f5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function hash(a, b, c) {
    let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263) + Math.imul(c | 0, 1274126177)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return (h ^ (h >>> 16)) >>> 0;
  }
  function fmt(n) {
    n = Math.floor(n);
    if (n < 1000) return String(n);
    const units = ['K', 'M', 'B', 'T'];
    let u = -1;
    let v = n;
    while (v >= 1000 && u < units.length - 1) { v /= 1000; u++; }
    let t = v < 10 ? v.toFixed(2) : v < 100 ? v.toFixed(1) : Math.floor(v).toString();
    if (t.indexOf('.') >= 0) t = t.replace(/\.?0+$/, '');
    return t + units[u];
  }
  const $ = (id) => document.getElementById(id);
  function el(tag, cls, parent, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  }
  function uimg(name) { return UIMG[name] ? 'url(' + UIMG[name] + ')' : 'none'; }
  function setText(e, t) { if (e.textContent !== t) e.textContent = t; }

  // ───────────────────────── рекламные сети ─────────────────────────
  const isIOS = () => /iPad|iPhone|iPod/.test(navigator.userAgent) || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
  const storeUrl = () => (isIOS() ? CFG.ios : CFG.android);
  let lastCta = -1e9;
  const Net = {
    paused: false,
    onPause: null,
    cta() {
      const now = performance.now();
      if (now - lastCta < 500) return;
      lastCta = now;
      Sfx.click();
      if (TEST) { G.ctaCount = (G.ctaCount || 0) + 1; return; }
      const w = window;
      const url = storeUrl();
      try {
        if (typeof w.install === 'function' && typeof w.gameReady === 'function') { w.install(); return; }
        if (w.mraid && w.mraid.getState && w.mraid.open) { w.mraid.open(url); return; }
        if (w.dapi && w.dapi.openStoreUrl) { w.dapi.openStoreUrl(); return; }
        if (w.FbPlayableAd && w.FbPlayableAd.onCTAClick) { w.FbPlayableAd.onCTAClick(); return; }
        if (w.ScPlayableAd && w.ScPlayableAd.onCTAClick) { w.ScPlayableAd.onCTAClick(); return; }
        if (w.ExitApi && w.ExitApi.exit) { w.ExitApi.exit(); return; }
        if (typeof w.openAppStore === 'function') { w.openAppStore(); return; }
      } catch (e) { /* сеть без API — обычная ссылка */ }
      const opened = window.open(url, '_blank');
      if (!opened || matchMedia('(pointer: coarse)').matches) location.href = url;
    },
    end() { try { if (typeof window.gameEnd === 'function') window.gameEnd(); } catch (e) { /* нет */ } },
    /** Старт, когда реклама готова и видна (mraid / dapi); иначе сразу. */
    boot(start) {
      const w = window;
      let started = false;
      const go = () => { if (!started) { started = true; start(); } };
      const pause = (p) => { Net.paused = p; if (Net.onPause) Net.onPause(p); };
      try { if (typeof w.gameReady === 'function') w.gameReady(); } catch (e) { /* нет */ }
      if (w.mraid && w.mraid.getState) {
        const m = w.mraid;
        const ready = () => {
          const view = () => (m.isViewable ? m.isViewable() : true);
          if (view()) go();
          m.addEventListener && m.addEventListener('viewableChange', (v) => { if (v) go(); pause(!v); });
        };
        if (m.getState() === 'loading') m.addEventListener('ready', ready); else ready();
        return;
      }
      if (w.dapi && w.dapi.isReady) {
        const d = w.dapi;
        const ready = () => {
          d.removeEventListener && d.removeEventListener('ready', ready);
          if (d.isViewable()) go();
          d.addEventListener('viewableChange', (e) => { if (e.isViewable) go(); pause(!e.isViewable); });
          d.addEventListener('audioVolumeChange', (v) => Sfx.setNetVolume(v));
          if (d.getAudioVolume) Sfx.setNetVolume(d.getAudioVolume());
        };
        if (d.isReady()) ready(); else d.addEventListener('ready', ready);
        return;
      }
      go();
    },
  };
  document.addEventListener('visibilitychange', () => { if (Net.onPause) Net.onPause(document.hidden); });
  // Mintegral вызывает эти функции сам; игра стартует без них, поэтому достаточно пустых обработчиков
  if (typeof window.gameStart !== 'function') window.gameStart = function () {};
  if (typeof window.gameClose !== 'function') window.gameClose = function () {};

  // ───────────────────────── звук (WebAudio, без файлов) ─────────────────────────
  const Sfx = (() => {
    let ac = null, master = null, musicGain = null, muted = false, netVol = 1, musicOn = false, nextNote = 0, step = 0;
    const NOTES = [523.25, 659.25, 783.99, 659.25, 587.33, 698.46, 880.0, 698.46, 523.25, 659.25, 783.99, 1046.5, 987.77, 783.99, 659.25, 587.33];
    function unlock() {
      if (!ac) {
        const C = window.AudioContext || window.webkitAudioContext;
        if (!C) return;
        ac = new C();
        master = ac.createGain();
        master.gain.value = muted ? 0 : 0.55 * netVol;
        master.connect(ac.destination);
        musicGain = ac.createGain();
        musicGain.gain.value = 0.05;
        musicGain.connect(master);
      }
      if (ac.state === 'suspended') ac.resume();
      if (!musicOn) { musicOn = true; nextNote = ac.currentTime + 0.1; }
    }
    function tone(freq, dur, type, vol, slide, delay) {
      if (!ac || muted) return;
      const t = ac.currentTime + (delay || 0);
      const o = ac.createOscillator();
      const g = ac.createGain();
      o.type = type || 'square';
      o.frequency.setValueAtTime(freq, t);
      if (slide) o.frequency.exponentialRampToValueAtTime(Math.max(30, freq * slide), t + dur);
      g.gain.setValueAtTime(vol || 0.2, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + dur);
      o.connect(g); g.connect(master);
      o.start(t); o.stop(t + dur + 0.02);
    }
    function noise(dur, vol, freq, q, delay) {
      if (!ac || muted) return;
      const t = ac.currentTime + (delay || 0);
      const n = Math.max(1, Math.floor(ac.sampleRate * dur));
      const buf = ac.createBuffer(1, n, ac.sampleRate);
      const d = buf.getChannelData(0);
      for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / n);
      const s = ac.createBufferSource();
      s.buffer = buf;
      const f = ac.createBiquadFilter();
      f.type = 'bandpass';
      f.frequency.value = freq || 1200;
      f.Q.value = q || 1.2;
      const g = ac.createGain();
      g.gain.value = vol || 0.3;
      s.connect(f); f.connect(g); g.connect(master);
      s.start(t);
    }
    function tickMusic() {
      if (!ac || !musicOn || muted) return;
      while (nextNote < ac.currentTime + 0.25) {
        const f = NOTES[step % NOTES.length];
        const o = ac.createOscillator();
        const g = ac.createGain();
        o.type = 'triangle';
        o.frequency.value = f / 2;
        g.gain.setValueAtTime(0.0001, nextNote);
        g.gain.exponentialRampToValueAtTime(1, nextNote + 0.02);
        g.gain.exponentialRampToValueAtTime(0.001, nextNote + 0.32);
        o.connect(g); g.connect(musicGain);
        o.start(nextNote); o.stop(nextNote + 0.35);
        if (step % 4 === 0) {
          const b = ac.createOscillator();
          const bg = ac.createGain();
          b.type = 'sine';
          b.frequency.value = f / 4;
          bg.gain.setValueAtTime(0.8, nextNote);
          bg.gain.exponentialRampToValueAtTime(0.001, nextNote + 0.5);
          b.connect(bg); bg.connect(musicGain);
          b.start(nextNote); b.stop(nextNote + 0.55);
        }
        step++;
        nextNote += 0.19;
      }
    }
    function apply() { if (master) master.gain.value = muted ? 0 : 0.55 * netVol; }
    return {
      unlock, tickMusic,
      toggle() { muted = !muted; apply(); return muted; },
      get muted() { return muted; },
      setNetVolume(v) { netVol = typeof v === 'number' ? clamp(v, 0, 1) : 1; apply(); },
      suspend(p) { if (ac) { if (p) ac.suspend(); else ac.resume(); } },
      hit() { noise(0.07, 0.22, 900, 1.4); tone(140, 0.08, 'square', 0.08, 0.6); },
      wood() { noise(0.06, 0.25, 1800, 2.2); tone(260, 0.05, 'triangle', 0.06, 0.7); },
      metal() { tone(1400, 0.09, 'square', 0.05, 0.8); noise(0.05, 0.12, 4000, 3); },
      tap() { noise(0.05, 0.3, 700, 1); tone(110, 0.1, 'square', 0.14, 0.5); },
      coin() { tone(1318, 0.06, 'square', 0.06); tone(1760, 0.08, 'square', 0.05, 1, 0.05); },
      cash() { tone(988, 0.05, 'square', 0.05); tone(1319, 0.05, 'square', 0.05, 1, 0.04); tone(1976, 0.12, 'square', 0.05, 1, 0.08); },
      boom() { noise(0.6, 0.55, 180, 0.6); tone(70, 0.5, 'sine', 0.5, 0.4); noise(0.3, 0.3, 900, 0.8, 0.05); },
      chest() { [523, 659, 784, 1047, 1319].forEach((f, i) => tone(f, 0.16, 'square', 0.07, 1, i * 0.07)); },
      up() { tone(440, 0.07, 'square', 0.07, 1.6); tone(880, 0.1, 'square', 0.06, 1.3, 0.06); },
      mew() { tone(680, 0.16, 'sine', 0.12, 1.35); tone(900, 0.14, 'sine', 0.08, 0.75, 0.13); },
      gate() { noise(0.35, 0.4, 500, 0.7); tone(90, 0.35, 'sawtooth', 0.12, 0.5); },
      done() { [784, 988, 1175, 1568].forEach((f, i) => tone(f, 0.18, 'square', 0.07, 1, i * 0.09)); },
      click() { tone(900, 0.04, 'square', 0.05); },
      fire() { noise(0.15, 0.4, 300, 0.8); tone(160, 0.15, 'square', 0.12, 0.5); },
    };
  })();

  // ───────────────────────── атлас ─────────────────────────
  const atlas = new Image();
  let ctx = null;
  function spr(name, x, y, flip, alpha, scale) {
    const s = SPR[name];
    if (!s) return;
    const k = scale || 1;
    if (alpha != null && alpha < 1) ctx.globalAlpha = alpha;
    if (!flip && k === 1) {
      ctx.drawImage(atlas, s[0], s[1], s[2], s[3], Math.round(x - s[4]), Math.round(y - s[5]), s[2], s[3]);
    } else {
      ctx.save();
      ctx.translate(Math.round(x), Math.round(y));
      ctx.scale(flip ? -k : k, k);
      ctx.drawImage(atlas, s[0], s[1], s[2], s[3], -s[4], -s[5], s[2], s[3]);
      ctx.restore();
    }
    if (alpha != null && alpha < 1) ctx.globalAlpha = 1;
  }
  /** Тайл целиком в прямоугольник (как TileBatch Unity: весь спрайт растягивается). */
  function tile(c, name, x, y, w, h) {
    const s = SPR[name];
    if (s) c.drawImage(atlas, s[0], s[1], s[2], s[3], x, y, w, h);
  }

  // ───────────────────────── шахта ─────────────────────────
  const DEBRIS = {
    planks: { tier: 0.5, n: 3, wide: 3, chip: '#c88a4a', sfx: 'wood' },
    crates: { tier: 1, n: 2, wide: 2, chip: '#b07a40', sfx: 'wood' },
    cardboard: { tier: 0.5, n: 3, wide: 3, chip: '#d7a86a', sfx: 'wood' },
    sacks: { tier: 0.5, n: 3, wide: 3, chip: '#e8d6a8', sfx: 'wood' },
    yarn: { tier: 1, n: 3, wide: -1, chip: '#d8463e', sfx: 'wood' },
    catfurn: { tier: 1, n: 3, wide: -1, chip: '#c88a4a', sfx: 'wood' },
    rubble: { tier: 1.5, n: 2, wide: 2, chip: '#8a94b0', sfx: 'hit' },
    scrap: { tier: 1.5, n: 2, wide: 2, chip: '#b06a3a', sfx: 'metal' },
  };
  const DEBRIS_KEYS = Object.keys(DEBRIS);
  const WIDE = DEBRIS_KEYS.filter((k) => DEBRIS[k].wide >= 0);

  /**
   * Шахта из ASCII: '#' порода, '.' пол, 'E' выход, 'o' руда, 'g'/'G' самоцветы, 'c'/'i'/'C'/'L' сундуки (wooden, iron,
   * golden, legendary), 'd' мусор, 'b' завал 2×1 (с соседней '_'), 'p' постоянное препятствие, '=' ворота (две клетки).
   */
  function buildMine(rows, opts) {
    const R = rows.length, C = rows[0].length;
    const r0 = rng(opts.seed || 7);
    const M = {
      cols: C, rows: R, floor: new Array(C * R).fill(false), sector: new Array(C * R).fill(0), occ: new Array(C * R).fill(-1),
      targets: [], gates: [], props: [], exit: -1, opened: 0, sectors: 1, hpScale: opts.hp || 1, valueScale: opts.value || 1,
      chestCards: opts.chestCards || [], number: opts.number || 1, darkness: [], lamps: [], decals: [], rests: [],
    };
    const gateRows = [];
    for (let r = 0; r < R; r++) if (rows[r].indexOf('=') >= 0) gateRows.push(r);
    M.sectors = gateRows.length + 1;
    for (let r = 0; r < R; r++) {
      let s = 0;
      for (const g of gateRows) if (r < g) s++;
      for (let c = 0; c < C; c++) {
        const ch = rows[r][c];
        const i = r * C + c;
        M.floor[i] = ch !== '#' && ch !== 'p';
        M.sector[i] = s;
        if (ch === 'p') M.props.push({ col: c, row: r, name: ['prop_support_0', 'prop_minecart_0', 'prop_reel_0', 'prop_tower_0'][hash(c, r, 3) % 4] });
        if (ch === 'p') M.floor[i] = false;
      }
    }
    const cardQueue = (opts.chestCards || []).slice();
    let chestN = 0;
    for (let r = 0; r < R; r++) {
      for (let c = 0; c < C; c++) {
        const ch = rows[r][c];
        const i = r * C + c;
        if (ch === 'E') M.exit = i;
        if (ch === '=' && (c === 0 || rows[r][c - 1] !== '=')) {
          addTarget(M, { kind: 'gate', col: c, row: r, w: 2, hp: 70, units: 0, sector: M.sector[i] });
          M.gates.push(M.targets.length - 1);
        } else if (ch === 'o') {
          addTarget(M, { kind: 'ore', col: c, row: r, w: 1, base: 'rock_plain_' + (hash(c, r, M.number) % 3), hp: 10, units: 3 });
        } else if (ch === 'g' || ch === 'G') {
          addTarget(M, { kind: 'gem', col: c, row: r, w: 1, base: ch === 'g' ? 'rock_gems_small' : 'rock_gems_big', hp: ch === 'g' ? 14 : 20, units: ch === 'g' ? 4 : 6, value: 2.5 });
        } else if ('ciCL'.indexOf(ch) >= 0) {
          const tier = { c: 'wooden', i: 'iron', C: 'golden', L: 'legendary' }[ch];
          addTarget(M, { kind: 'chest', col: c, row: r, w: 1, base: 'rock_chest_' + tier, hp: tier === 'wooden' ? 18 : 26, units: 5, value: 2, card: cardQueue[chestN++] || null, tier });
        } else if (ch === 'b' && rows[r][c + 1] === '_') {
          const fam = WIDE[hash(c, r, M.number + 17) % WIDE.length];
          const f = DEBRIS[fam];
          addTarget(M, { kind: 'debris', col: c, row: r, w: 2, base: 'debris_' + fam + '_' + f.wide, fam, hp: 14 * f.tier + 4, units: 3, value: 0.8 });
        } else if (ch === 'd' || ch === 'b') {
          const fam = DEBRIS_KEYS[hash(c, r, M.number + 11) % DEBRIS_KEYS.length];
          const f = DEBRIS[fam];
          addTarget(M, { kind: 'debris', col: c, row: r, w: 1, base: 'debris_' + fam + '_' + (hash(c, r, 5) % f.n), fam, hp: 9 * f.tier + 2, units: 2, value: 0.7 });
        }
      }
    }
    for (let s = 0; s < M.sectors; s++) M.darkness.push(s === 0 ? 0 : 0.86);
    decorate(M, r0);
    return M;
  }
  function addTarget(M, t) {
    t.id = M.targets.length;
    t.cells = [];
    for (let dx = 0; dx < t.w; dx++) t.cells.push(t.row * M.cols + t.col + dx);
    if (t.sector == null) t.sector = M.sector[t.cells[0]];
    t.max = t.hp = Math.max(1, Math.round(t.hp * M.hpScale));
    t.unitValue = Math.max(1, Math.round(5 * (t.value || 1) * M.valueScale));
    t.released = 0;
    t.alive = true;
    t.hitT = 0;
    t.flip = t.w === 1 && (hash(t.col, t.row, 41) & 1) === 1;
    for (const i of t.cells) M.occ[i] = t.id;
    M.targets.push(t);
  }
  function decorate(M, r) {
    const C = M.cols;
    const isFloor = (c, rr) => c >= 0 && c < C && rr >= 0 && rr < M.rows && M.floor[rr * C + c];
    const used = [];
    for (let rr = M.rows - 1; rr >= 0; rr--) {
      for (let c = 0; c < C; c++) {
        if (!isFloor(c, rr)) continue;
        const left = !isFloor(c - 1, rr), right = !isFloor(c + 1, rr);
        if (!left && !right) continue;
        if (hash(c, rr, M.number + 3) % 4 !== 0) continue;
        if (used.some((u) => Math.abs(u[0] - c) + Math.abs(u[1] - rr) < 4)) continue;
        used.push([c, rr]);
        M.lamps.push({ x: left ? c * T - 6 : (c + 1) * T + 6, y: (rr + 1) * T, flip: right && !left, phase: r() * 6 });
      }
    }
    for (let i = 0; i < C * M.rows; i++) {
      if (!M.floor[i] || hash(i, M.number, 77) % 100 > 12) continue;
      M.decals.push({ i, name: ['decal_pebbles_0', 'decal_pebbles_1', 'decal_pebbles_2', 'decal_paint_0', 'decal_paws_0', 'decal_stain_0', 'decal_board_0'][hash(i, 5, 9) % 7] });
    }
  }

  // ───────────────────────── мир: координаты, лагерь, фон ─────────────────────────
  let SIDE = 4; // клеток фона по бокам шахты — растёт под ширину экрана (ensureSides)
  const TOP = 3, CAMP = 5;
  let M = null; // текущая шахта
  let bg = null; // статичный фон (тайлы, края, лагерь, декор)
  const camp = { props: [], intake: 0, mortar: null };
  function worldW() { return M.cols * T; }
  function worldH() { return (M.rows + CAMP) * T; }
  function campRow() { return M.rows; }
  function cellX(i) { return (i % M.cols) * T + T / 2; }
  function cellY(i) { return Math.floor(i / M.cols) * T + T / 2; }
  /** Сетка навигации: шахта + три настила лагеря. */
  function navRows() { return M.rows + 3; }

  function buildBackground() {
    const W = (M.cols + SIDE * 2) * T, H = (M.rows + CAMP + TOP + 2) * T;
    bg = document.createElement('canvas');
    bg.width = W; bg.height = H;
    const g = bg.getContext('2d');
    g.imageSmoothingEnabled = false;
    const ox = SIDE * T, oy = TOP * T;
    const C = M.cols, R = M.rows;
    const tunnel = (c, r) => c >= 0 && c < C && r >= 0 && r < R && M.floor[r * C + c];
    const campWalk = (c, r) => c >= 0 && c < C && r >= R && r < R + 3;
    const open = (c, r) => tunnel(c, r) || campWalk(c, r);
    for (let r = -TOP; r < R + CAMP + 2; r++) {
      for (let c = -SIDE; c < C + SIDE; c++) {
        const x = ox + c * T, y = oy + r * T;
        const h = hash(c, r, M.number);
        let name;
        if (campWalk(c, r)) name = r === R ? 'camp_edge_t' : r === R + 1 ? ['camp_floor_0', 'camp_floor_1', 'camp_floor_2', 'camp_floor_3'][h % 4] : 'camp_edge_b';
        else if (c >= 0 && c < C && r >= R + 3 && r < R + CAMP) name = ['camp_ground_0', 'camp_ground_1'][h % 2];
        else if (tunnel(c, r)) { const v = h % 10; name = 'floor_' + (v < 4 ? 0 : v < 7 ? 1 : v < 9 ? 2 : 3); }
        else name = ['wall_top', 'wall_top', 'wall_top_1', 'wall_top_2', 'wall_top_3'][h % 5];
        tile(g, name, x, y, T, T);
        if (open(c, r) || (c >= 0 && c < C && r >= R + 3 && r < R + CAMP)) continue;
        // кромки скалы над полом и тень на пол — как RockCutawayEdges Unity
        if (open(c - 1, r)) { tile(g, 'wall_edge_r_0', x, y, 4, T); shade(g, x - 2, y, 2, T); }
        if (open(c + 1, r)) { tile(g, 'wall_edge_l_0', x + T - 4, y, 4, T); shade(g, x + T, y, 2, T); }
        if (open(c, r + 1)) { tile(g, 'wall_top_edge', x, y + T - 6, T, 6); shade(g, x, y + T, T, 3); }
        if (open(c, r - 1)) { tile(g, 'wall_bottom', x, y, T, 3); shade(g, x, y - 2, T, 2); }
      }
    }
    // нижняя стена лагеря
    for (let c = -SIDE; c < C + SIDE; c++) tile(g, 'wall_bottom', ox + c * T, oy + (R + CAMP) * T, T, 4);
    // пятна пола
    ctx = g;
    for (const d of M.decals) spr(d.name, ox + cellX(d.i), oy + Math.floor(d.i / C) * T + T);
    // опоры с фонарями
    for (const l of M.lamps) spr('beam_post', ox + l.x, oy + l.y, l.flip);
    // выход в задней стене
    if (M.exit >= 0) {
      spr('exit_base', ox + cellX(M.exit), oy + 2);
    }
    ctx = null;
    for (const t of M.rests) drawRest(t);
  }
  /** Фон закрывает экран по бокам целиком: в альбомной ориентации и на планшетах шахта уже экрана. */
  function ensureSides() {
    const left = view.ox / view.scale, right = (view.w - view.ox) / view.scale - M.cols * T;
    const need = Math.max(4, Math.ceil(Math.max(left, right) / T) + 1);
    if (!bg || need > SIDE) { SIDE = Math.max(SIDE, need); buildBackground(); }
  }
  function shade(g, x, y, w, h) {
    g.fillStyle = 'rgba(10,8,18,0.32)';
    g.fillRect(x, y, w, h);
  }
  /** Обломки разбитой цели впечатываются в фон; список хранится, чтобы перерисовать их при пересборке фона. */
  function stampRest(t) {
    M.rests.push(t);
    drawRest(t);
  }
  function drawRest(t) {
    if (!bg) return;
    const g = bg.getContext('2d');
    const prev = ctx;
    ctx = g;
    const name = t.kind === 'debris' ? 'debris_' + t.fam + '_rest' : 'rubble_debris_' + (hash(t.col, t.row, 2) % 3);
    for (const i of t.cells) spr(name, SIDE * T + cellX(i), TOP * T + Math.floor(i / M.cols) * T + T, (hash(i, 1, 1) & 1) === 1);
    ctx = prev;
  }

  function buildCamp() {
    const C = M.cols, R = M.rows;
    const mid = Math.floor(C / 2);
    camp.intake = (R + 1) * C + mid;
    camp.props = [
      { name: 'depot_base', x: (mid + 0.5) * T, y: (R + 5) * T - 2 },
      { name: 'warehouse_full', x: 1.0 * T, y: (R + 5) * T - 2 },
      { name: 'hire_board_0', x: (C - 1.0) * T, y: (R + 5) * T - 4 },
      { name: 'camp_lantern_0', x: (mid - 1.9) * T, y: (R + 3) * T, lamp: true },
      { name: 'camp_lantern_1', x: (mid + 2.9) * T, y: (R + 3) * T, lamp: true },
    ];
    if (C >= 10) camp.props.push({ name: 'camp_tent', x: (C - 2.7) * T, y: (R + 5) * T - 2 }, { name: 'bench_0', x: (mid - 2.8) * T, y: (R + 5) * T - 4 });
    camp.mortar = { x: 2.6 * T, y: (R + 3.85) * T, kick: 0 };
  }

  // ───────────────────────── навигация ─────────────────────────
  let navDirty = true;
  function passable(i) {
    const R = M.rows, C = M.cols;
    const r = Math.floor(i / C);
    if (r >= R) return r < R + 3;
    return M.floor[i] && M.occ[i] < 0;
  }
  const queue = [];
  function bfs(from) {
    const N = M.cols * navRows();
    const dist = new Int16Array(N).fill(-1);
    const prev = new Int32Array(N).fill(-1);
    if (from < 0 || from >= N) return { dist, prev };
    queue.length = 0;
    queue.push(from);
    dist[from] = 0;
    const C = M.cols;
    for (let h = 0; h < queue.length; h++) {
      const i = queue[h];
      const c = i % C, r = (i / C) | 0;
      const nb = [c > 0 ? i - 1 : -1, c < C - 1 ? i + 1 : -1, r > 0 ? i - C : -1, i + C < N ? i + C : -1];
      for (const n of nb) {
        if (n < 0 || dist[n] >= 0 || !passable(n)) continue;
        dist[n] = dist[i] + 1;
        prev[n] = i;
        queue.push(n);
      }
    }
    return { dist, prev };
  }
  function pathTo(field, to) {
    const p = [];
    for (let i = to; i >= 0 && field.dist[i] > 0; i = field.prev[i]) p.push(i);
    return p.reverse();
  }
  function workCells(t) {
    const C = M.cols, out = [];
    for (const i of t.cells) {
      const c = i % C, r = (i / C) | 0;
      const nb = [c > 0 ? i - 1 : -1, c < C - 1 ? i + 1 : -1, r > 0 ? i - C : -1, i + C];
      for (const n of nb) if (n >= 0 && t.cells.indexOf(n) < 0 && out.indexOf(n) < 0 && passable(n)) out.push(n);
    }
    return out;
  }
  function attackable(t) {
    if (!t.alive) return false;
    if (t.kind === 'gate') return t.sector === M.opened && sectorLeft(M.opened) === 0;
    return t.sector <= M.opened;
  }
  function sectorLeft(s) {
    let n = 0;
    for (const t of M.targets) if (t.alive && t.kind !== 'gate' && t.sector === s) n++;
    return n;
  }
  function targetsLeft() {
    let n = 0;
    for (const t of M.targets) if (t.alive) n++;
    return n;
  }

  // ───────────────────────── состояние игры ─────────────────────────
  const G = {
    gold: 0, shownGold: 0, earned: 0, time: 0, playTime: 0, ended: false, cats: [], fx: [], flying: [], texts: [], barrel: null,
    lv: { team: 0, pick: 0, boots: 0, bags: 0 }, bonus: { dmg: 1, speed: 1, gold: 1, bag: 0, cats: 0 }, chests: 0, mines: 0,
    camera: { y: 0, target: 0, drag: false, shake: 0, user: 0, focus: null }, idle: 0, catSeq: 0, prices: { team: 12, pick: 8, boots: 10, bags: 10 },
    pickKind: 'common', hatKind: 'common', packKind: 'common', bootsKind: 'common', tapDmg: 1.4, hitInterval: 0.85, baseDmg: 1,
    maxCats: 10, stats: { taps: 0, booms: 0, gates: 0 }, growth: { team: 1.6, pick: 1.5, boots: 1.5, bags: 1.55 }, pickStep: 1.3,
    tierStep: 3, maxBags: 99,
  };
  const CAT_CAP = 16;
  const KIND_ORDER = ['common', 'rare', 'epic', 'legendary'];
  function catDmg() { return G.baseDmg * Math.pow(G.pickStep, G.lv.pick) * G.bonus.dmg; }
  /** Сапоги: убывающая отдача, предел +160 % — котики не разгоняются до мелькания. */
  function bootsBonus(lv) { return 1.6 * (1 - Math.pow(0.94, lv)); }
  function catSpeed() { return 3.1 * (1 + bootsBonus(G.lv.boots)) * G.bonus.speed; }
  function bagCap() { return 4 + G.lv.bags + G.bonus.bag; }
  /** Цена следующего уровня: уровни и цены общие на весь забег (long не сбрасывает их между шахтами). */
  function price(kind) { return Math.ceil(G.prices[kind] * Math.pow(G.growth[kind], G.lv[kind])); }
  /** Ветка упёрлась в предел: команда собрана, сумки максимальные. */
  function isMax(k) { return k === 'team' ? G.cats.length >= G.maxCats : k === 'bags' ? G.lv.bags >= G.maxBags : false; }
  function fmtMul(x) { return x < 10 ? x.toFixed(1) : x < 1000 ? String(Math.round(x)) : fmt(x); }

  // ───────────────────────── котики ─────────────────────────
  const FURS = ['ginger', 'gray', 'white', 'black', 'cream'];
  const MINE_FRAMES = ['mine_ready', 'mine_ready2', 'mine_antic', 'mine_raise', 'mine_top', 'mine_strike', 'mine_impact', 'mine_recoil', 'mine_return'];
  function addCat(cell, spawn) {
    const id = ++G.catSeq;
    const cat = {
      id, fur: FURS[(id - 1) % FURS.length], cell, x: cellX(cell), y: cellY(cell) + 9, state: 'choose', path: null, pi: 0,
      target: null, work: -1, bag: 0, bagValue: 0, face: id % 2 ? 1 : -1, anim: Math.random() * 3, swing: 0, spawn: spawn ? 0.67 : 0,
      joy: 0, think: Math.random() * 0.3, unloadT: 0,
    };
    G.cats.push(cat);
    return cat;
  }
  function reserved(cell, except) {
    for (const c of G.cats) if (c !== except && c.work === cell && (c.state === 'walk' || c.state === 'mine')) return true;
    return false;
  }
  function assigned(t, except) {
    let n = 0;
    for (const c of G.cats) if (c !== except && c.target === t) n++;
    return n;
  }
  function chooseTask(cat) {
    if (cat.bag >= bagCap() || (cat.bag > 0 && !anyWork())) return goHome(cat);
    const field = bfs(cat.cell);
    let best = null, bestWork = -1, bestScore = 1e9;
    for (const t of M.targets) {
      if (!attackable(t)) continue;
      const n = assigned(t, cat);
      if (n >= 3) continue;
      for (const w of workCells(t)) {
        if (w !== cat.cell && field.dist[w] < 0) continue;
        if (reserved(w, cat)) continue;
        const d = w === cat.cell ? 0 : field.dist[w];
        const score = d + n * 2.5 + (t.kind === 'gate' ? 0 : 0);
        if (score < bestScore) { bestScore = score; best = t; bestWork = w; }
      }
    }
    if (!best) {
      if (cat.bag > 0) return goHome(cat);
      cat.state = 'idle';
      cat.think = 0.4 + Math.random() * 0.4;
      return;
    }
    cat.target = best;
    cat.work = bestWork;
    if (bestWork === cat.cell) { beginMining(cat); return; }
    cat.path = pathTo(field, bestWork);
    cat.pi = 0;
    cat.state = 'walk';
  }
  function anyWork() {
    for (const t of M.targets) if (attackable(t)) return true;
    return false;
  }
  function goHome(cat) {
    cat.target = null;
    cat.work = -1;
    if (cat.cell === camp.intake) { cat.state = 'unload'; cat.unloadT = 0; return; }
    const field = bfs(cat.cell);
    cat.path = pathTo(field, camp.intake);
    cat.pi = 0;
    cat.state = cat.path.length ? 'home' : 'idle';
    if (!cat.path.length) cat.think = 0.5;
  }
  function beginMining(cat) {
    cat.state = 'mine';
    cat.swing = 0;
    const t = cat.target;
    const tx = (t.col + t.w / 2) * T;
    if (Math.abs(tx - cat.x) > 4) cat.face = tx > cat.x ? 1 : -1;
  }
  function updateCat(cat, dt) {
    cat.anim += dt;
    if (cat.spawn > 0) { cat.spawn -= dt; return; }
    if (cat.joy > 0) { cat.joy -= dt; if (cat.joy > 0) return; }
    switch (cat.state) {
      case 'choose': chooseTask(cat); break;
      case 'idle':
        cat.think -= dt;
        if (cat.think <= 0) cat.state = 'choose';
        break;
      case 'walk':
      case 'home': {
        if (cat.state === 'walk' && (!cat.target || !cat.target.alive)) { cat.state = 'choose'; break; }
        const next = cat.path[cat.pi];
        if (next == null) { arrive(cat); break; }
        if (!passable(next)) { cat.state = cat.state === 'home' ? 'choose' : 'choose'; break; }
        const tx = cellX(next), ty = cellY(next) + 9;
        const dx = tx - cat.x, dy = ty - cat.y;
        const d = Math.hypot(dx, dy);
        const v = catSpeed() * T * dt * (cat.bag > 0 ? 0.9 : 1);
        if (Math.abs(dx) > 1) cat.face = dx > 0 ? 1 : -1;
        if (d <= v) {
          cat.x = tx; cat.y = ty; cat.cell = next; cat.pi++;
          if (cat.pi >= cat.path.length) arrive(cat);
        } else { cat.x += (dx / d) * v; cat.y += (dy / d) * v; }
        break;
      }
      case 'mine': {
        const t = cat.target;
        if (!t || !t.alive) { cat.target = null; cat.work = -1; cat.state = 'choose'; break; }
        const before = cat.swing;
        cat.swing += dt / G.hitInterval;
        if (before < 0.68 && cat.swing >= 0.68) hit(t, catDmg(), cat, false);
        if (cat.swing >= 1) {
          cat.swing = 0;
          if (cat.bag >= bagCap()) goHome(cat);
          else if (!t.alive) cat.state = 'choose';
        }
        break;
      }
      case 'unload': {
        cat.unloadT += dt;
        if (cat.unloadT >= 0.42) {
          if (cat.bagValue > 0) {
            const sx = worldToScreenX(cat.x), sy = worldToScreenY(cat.y - 18);
            flyCoins(sx, sy, cat.bagValue, Math.min(5, 1 + Math.floor(cat.bag / 2)));
          }
          cat.bag = 0; cat.bagValue = 0;
          cat.state = 'choose';
        }
        break;
      }
    }
  }
  function arrive(cat) {
    if (cat.state === 'home') { cat.state = 'unload'; cat.unloadT = 0; return; }
    if (cat.target && cat.target.alive) beginMining(cat); else cat.state = 'choose';
  }
  function catFrame(cat) {
    if (cat.spawn > 0) return 'spawn' + clamp(Math.floor((0.67 - cat.spawn) / 0.084), 0, 7);
    if (cat.joy > 0) return 'joy' + (Math.floor(cat.anim * 10) % 6);
    switch (cat.state) {
      case 'walk': case 'home': {
        const f = Math.floor(cat.anim * 12) % 8;
        return (cat.bag > 0 ? 'carry' : 'walk') + f;
      }
      case 'mine': return MINE_FRAMES[clamp(Math.floor(cat.swing * 9), 0, 8)];
      case 'unload': return 'unload' + clamp(Math.floor(cat.unloadT / 0.105), 0, 3);
      default: {
        const t = cat.anim % 4;
        if (t > 3.82) return 'blink';
        return (cat.bag > 0 ? 'carry0' : 'idle' + (Math.floor(cat.anim * 7) % 6));
      }
    }
  }
  function drawCat(cat) {
    const f = catFrame(cat);
    const x = cat.x, y = cat.y, flip = cat.face < 0;
    spr('shadow_s', x, y + 1);
    spr('cat_body_' + cat.fur + '_' + f, x, y, flip);
    spr('cat_boots_' + G.bootsKind + '_' + f, x, y, flip);
    spr('cat_pack_' + G.packKind + '_' + f, x, y, flip);
    if (cat.bag > 0 && !f.startsWith('unload')) spr('cat_load_' + clamp(Math.ceil((cat.bag / bagCap()) * 3), 1, 3) + '_' + f, x, y, flip);
    spr('cat_hat_' + G.hatKind + '_' + f, x, y, flip);
    if (!f.startsWith('carry') && !f.startsWith('unload')) spr('cat_pick_' + G.pickKind + '_' + f, x, y, flip);
    if (f === 'unload0' || f === 'unload1' || f === 'unload2') spr('cat_sack_' + f, x, y, flip);
  }

  // ───────────────────────── удары, награды ─────────────────────────
  function hit(t, dmg, cat, byTap) {
    if (!t.alive) return;
    t.hp -= dmg;
    t.hitT = 0.14;
    const cx = (t.col + t.w / 2) * T, cy = (t.row + 1) * T - 12;
    const chip = t.kind === 'debris' ? DEBRIS[t.fam].chip : t.kind === 'gem' ? '#c070ff' : t.kind === 'chest' ? '#e0b040' : t.kind === 'gate' ? '#a0703a' : '#7d86a6';
    for (let k = 0; k < (byTap ? 7 : 4); k++) G.fx.push({ chip, x: cx + (Math.random() - 0.5) * 14, y: cy, vx: (Math.random() - 0.5) * 110, vy: -60 - Math.random() * 90, t: 0, life: 0.55, s: 2 + (Math.random() * 2 | 0) });
    G.fx.push({ frames: ['fx_hit_0', 'fx_hit_1', 'fx_hit_2'], fps: 22, x: cx + (Math.random() - 0.5) * 8, y: cy + 4, t: 0 });
    const sfx = t.kind === 'debris' ? DEBRIS[t.fam].sfx : t.kind === 'gate' ? 'wood' : t.kind === 'chest' ? 'wood' : 'hit';
    if (byTap || Math.random() < 0.6) Sfx[sfx]();
    const frac = clamp(1 - t.hp / t.max, 0, 1);
    const due = t.hp <= 0 ? t.units : Math.floor(frac * t.units);
    const fresh = Math.max(0, due - t.released);
    t.released += fresh;
    if (fresh > 0) {
      const value = Math.round(fresh * t.unitValue * G.bonus.gold);
      if (cat) { cat.bag += fresh; cat.bagValue += value; }
      else if (byTap) flyCoins(worldToScreenX(cx), worldToScreenY(cy), value, Math.min(6, fresh + 1));
    }
    if (byTap) G.texts.push({ text: '-' + fmt(Math.max(1, dmg)), x: cx, y: cy - 14, t: 0, color: '#ffe27a' });
    if (t.hp <= 0) destroy(t, cat);
  }
  function destroy(t, cat) {
    t.alive = false;
    for (const i of t.cells) M.occ[i] = -1;
    navDirty = true;
    const cx = (t.col + t.w / 2) * T, cy = (t.row + 1) * T - 10;
    G.fx.push({ frames: ['fx_puff_0', 'fx_puff_1', 'fx_puff_2', 'fx_puff_3'], fps: 14, x: cx, y: cy, t: 0, scale: t.w > 1 ? 1.6 : 1.1 });
    if (t.kind !== 'gate') stampRest(t);
    if (t.kind === 'gate') openGate(t);
    if (t.kind === 'chest') { G.chests++; Director.onChest(t); }
    Director.onDestroy(t, cat);
    if (targetsLeft() === 0) Director.onCleared();
  }
  function openGate(t) {
    Sfx.gate();
    G.stats.gates++;
    M.opened = Math.max(M.opened, t.sector + 1);
    G.camera.shake = 0.35;
    toast('SECTOR OPEN!', 'big');
    for (const c of G.cats) if (c.state === 'idle') c.state = 'choose';
  }
  function tapDamage() { return Math.max(catDmg() * G.tapDmg, 1); }

  // ───────────────────────── динамит ─────────────────────────
  function fireDynamite(at) {
    const m = camp.mortar;
    if (!m || G.barrel) return null;
    const target = at != null ? at : bestBlastCell();
    if (target < 0) return null;
    Sfx.fire();
    m.kick = 0.25;
    G.fx.push({ frames: ['fx_smoke_0', 'fx_smoke_1', 'fx_smoke_2', 'fx_smoke_3'], fps: 10, x: m.x + 4, y: m.y - 22, t: 0 });
    G.barrel = { state: 'fly', x0: m.x + 4, y0: m.y - 20, x1: cellX(target), y1: cellY(target) + 10, t: 0, dur: 1.0, cell: target, ground: 0 };
    return G.barrel;
  }
  function bestBlastCell() {
    let best = -1, bestN = 0;
    const C = M.cols;
    for (let i = 0; i < C * M.rows; i++) {
      if (!M.floor[i] || M.sector[i] > M.opened) continue;
      let n = 0;
      for (const t of M.targets) {
        if (!t.alive || t.kind === 'gate' || t.kind === 'chest' || t.sector > M.opened) continue;
        const d = Math.hypot((t.col + t.w / 2) - (i % C + 0.5), t.row - ((i / C) | 0));
        if (d <= 1.9) n++;
      }
      if (n > bestN) { bestN = n; best = i; }
    }
    return best;
  }
  function explode() {
    const b = G.barrel;
    if (!b || b.state !== 'ground') return false;
    b.state = 'boom';
    Sfx.boom();
    G.stats.booms++;
    G.camera.shake = 0.6;
    G.fx.push({ frames: ['fx_boom_0', 'fx_boom_1', 'fx_boom_2', 'fx_boom_3', 'fx_boom_4'], fps: 13, x: b.x1, y: b.y1 - 12, t: 0, scale: 3.2 });
    G.fx.push({ frames: ['fx_ring_0', 'fx_ring_1', 'fx_ring_2', 'fx_ring_3'], fps: 12, x: b.x1, y: b.y1 - 10, t: 0, scale: 3.5 });
    for (let k = 0; k < 26; k++) {
      const a = Math.random() * Math.PI * 2, v = 120 + Math.random() * 180;
      G.fx.push({ chip: k % 3 ? '#ffcf4a' : '#ff8030', x: b.x1, y: b.y1 - 12, vx: Math.cos(a) * v, vy: Math.sin(a) * v - 80, t: 0, life: 0.8, s: 3 });
    }
    const bx = b.x1 / T, by = (b.y1 - 10) / T;
    const big = Math.max(...M.targets.filter((t) => t.kind === 'ore').map((t) => t.max), 10) * 2.6;
    for (const t of M.targets) {
      if (!t.alive || t.kind === 'gate' || t.sector > M.opened) continue;
      const d = Math.hypot(t.col + t.w / 2 - bx, t.row + 0.5 - by);
      if (d > 2.1) continue;
      const dmg = t.kind === 'chest' ? t.max * 0.5 : big * (d < 1.2 ? 1 : 0.6);
      const before = t.released;
      t.hp -= dmg;
      t.hitT = 0.2;
      const due = t.hp <= 0 ? t.units : Math.floor(clamp(1 - t.hp / t.max, 0, 1) * t.units);
      const fresh = Math.max(0, due - before);
      t.released += fresh;
      if (fresh > 0) {
        const cx = (t.col + t.w / 2) * T, cy = (t.row + 1) * T - 12;
        flyCoins(worldToScreenX(cx), worldToScreenY(cy), Math.round(fresh * t.unitValue * G.bonus.gold * 1.5), Math.min(4, fresh));
      }
      if (t.hp <= 0) destroy(t, null);
    }
    for (const c of G.cats) if (Math.hypot(c.x - b.x1, c.y - b.y1) < 6 * T) c.joy = 0.9;
    later(0.6, () => { if (G.barrel === b) G.barrel = null; });
    Director.onBoom();
    return true;
  }

  // ───────────────────────── эффекты, монеты ─────────────────────────
  function flyCoins(sx, sy, value, n) {
    if (value <= 0) return;
    const dst = goldTarget();
    n = Math.max(1, n | 0);
    const each = Math.floor(value / n);
    for (let k = 0; k < n; k++) {
      const v = k === n - 1 ? value - each * (n - 1) : each;
      G.flying.push({ x0: sx + (Math.random() - 0.5) * 18, y0: sy + (Math.random() - 0.5) * 10, x1: dst.x, y1: dst.y, t: -k * 0.06, dur: 0.55 + Math.random() * 0.2, v, arc: -60 - Math.random() * 60 });
    }
    G.earned += value;
  }
  function goldTarget() {
    const r = $('goldi').getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }
  function addGold(v) {
    G.gold += v;
    const p = $('gold');
    p.classList.remove('bump');
    void p.offsetWidth;
    p.classList.add('bump');
  }

  // ───────────────────────── камера ─────────────────────────
  let view = { w: 0, h: 0, dpr: 1, scale: 1, ox: 0, oy: 0, top: 0, bottom: 0 };
  function layout() {
    const cv = $('cv');
    const dpr = Math.min(window.devicePixelRatio || 1, 2.5);
    // размер — по body (fixed, inset 0), как у интерфейса: в части WebView и в headless innerHeight меньше видимой области
    const box = document.body.getBoundingClientRect();
    const w = Math.round(box.width) || window.innerWidth, h = Math.round(box.height) || window.innerHeight;
    if (w < 2 || h < 2) return; // контейнер рекламы ещё 0×0 — размер придёт позже (проверка раз в 250 мс)
    cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr);
    cv.style.width = w + 'px'; cv.style.height = h + 'px';
    const top = $('top').getBoundingClientRect().bottom + 4;
    const dock = $('dock'), ctaBox = $('cta').getBoundingClientRect();
    const dockBox = dock.children.length ? dock.getBoundingClientRect() : null;
    // портрет: шахта над панелью улучшений; альбом: слева от колонки улучшений и магазина
    const land = w > h;
    const bottom = land ? 6 : h - (dockBox ? dockBox.top : ctaBox.top) + 4;
    const right = land ? w - Math.min(ctaBox.left, dockBox ? dockBox.left : w) + 6 : 0;
    // единица интерфейса (--u в CSS) — по кнопке звука шириной 10.5u
    const u = $('mute').getBoundingClientRect().width / 10.5 || w / 100;
    view = { w, h, dpr, top, bottom, right, u, scale: 1, ox: 0, oy: 0 };
    if (!M) return;
    const availW = w - right;
    const fitW = availW / ((M.cols + 0.6) * T);
    const inner = h - top - bottom;
    const fitAll = inner / (worldH() + 10);
    // short — вся шахта на экране; medium и long — по ширине, высокая шахта прокручивается (не мельче 15 строк на экран)
    view.scale = VARIANT === 'short' ? Math.min(fitW, fitAll) : Math.min(fitW, Math.max(fitAll, inner / (15 * T)));
    view.ox = (availW - M.cols * T * view.scale) / 2;
    ensureSides();
    clampCamera(true);
  }
  function maxScroll() { return Math.max(0, worldH() + 10 - (view.h - view.top - view.bottom) / view.scale); }
  function clampCamera(snap) {
    const ms = maxScroll();
    G.camera.target = clamp(G.camera.target, 0, ms);
    if (snap) G.camera.y = G.camera.target;
    G.camera.y = clamp(G.camera.y, 0, ms);
  }
  function worldOriginY() {
    const visible = (view.h - view.top - view.bottom) / view.scale;
    if (worldH() <= visible) return view.top + (visible - worldH()) * view.scale * 0.5;
    return view.top - G.camera.y * view.scale;
  }
  function worldToScreenX(x) { return view.ox + x * view.scale; }
  function worldToScreenY(y) { return worldOriginY() + y * view.scale; }
  function screenToWorld(sx, sy) { return { x: (sx - view.ox) / view.scale, y: (sy - worldOriginY()) / view.scale }; }
  /** Камера следует за фронтом работ (самая нижняя строка, где котики добывают) или за целью подсказки — длинные шахты. */
  function followFront(dt) {
    if (maxScroll() <= 0 || G.camera.drag) return;
    if (G.camera.user > 0) { G.camera.user -= dt; return; }
    const visible = (view.h - view.top - view.bottom) / view.scale;
    let goal;
    if (G.camera.focus != null) goal = G.camera.focus - visible * 0.5;
    else {
      // центр работ — медиана добывающих котиков; если все в пути — самая нижняя доступная цель
      const ys = [];
      for (const c of G.cats) if (c.state === 'mine') ys.push(c.y);
      let y;
      if (ys.length) { ys.sort((a, b) => a - b); y = ys[ys.length >> 1]; } else {
        y = -1;
        for (const t of M.targets) if (attackable(t)) y = Math.max(y, (t.row + 1) * T);
        if (y < 0) y = worldH();
      }
      goal = y - visible * 0.55;
    }
    G.camera.target = clamp(goal, 0, maxScroll());
    G.camera.y = lerp(G.camera.y, G.camera.target, 1 - Math.pow(0.04, dt));
  }

  // ───────────────────────── отрисовка ─────────────────────────
  function render() {
    const cv = $('cv');
    if (!ctx) { ctx = cv.getContext('2d'); }
    const c = ctx;
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.imageSmoothingEnabled = false;
    c.fillStyle = '#141a33';
    c.fillRect(0, 0, cv.width, cv.height);
    if (!M || !bg) return;
    const sh = G.camera.shake > 0 ? G.camera.shake * 8 : 0;
    const shx = sh ? (Math.random() - 0.5) * sh : 0, shy = sh ? (Math.random() - 0.5) * sh : 0;
    const s = view.scale * view.dpr;
    c.setTransform(s, 0, 0, s, Math.round((view.ox + shx) * view.dpr), Math.round((worldOriginY() + shy) * view.dpr));
    c.imageSmoothingEnabled = false;
    c.drawImage(bg, -SIDE * T, -TOP * T);
    // объекты по глубине (нижний край)
    const items = [];
    for (const t of M.targets) if (t.alive) items.push({ y: (t.row + 1) * T, t });
    for (const p of M.props) items.push({ y: (p.row + 1) * T, p });
    for (const p of camp.props) items.push({ y: p.y, cp: p });
    for (const cat of G.cats) items.push({ y: cat.y, cat });
    if (camp.mortar) items.push({ y: camp.mortar.y, mortar: camp.mortar });
    items.sort((a, b) => a.y - b.y);
    for (const it of items) {
      if (it.t) drawTarget(it.t);
      else if (it.cat) drawCat(it.cat);
      else if (it.p) { spr('shadow_m', (it.p.col + 0.5) * T, (it.p.row + 1) * T - 2); spr(it.p.name, (it.p.col + 0.5) * T, (it.p.row + 1) * T); }
      else if (it.cp) spr(it.cp.name, it.cp.x, it.cp.y);
      else if (it.mortar) drawMortar(it.mortar);
    }
    drawLamps();
    drawBarrel();
    drawDarkness();
    // эффекты
    for (const f of G.fx) {
      if (f.chip) { c.fillStyle = f.chip; c.fillRect(Math.round(f.x), Math.round(f.y), f.s, f.s); continue; }
      const k = Math.min(f.frames.length - 1, Math.floor(f.t * f.fps));
      spr(f.frames[k], f.x, f.y, false, 1, f.scale || 1);
    }
    // полосы HP
    for (const t of M.targets) {
      if (!t.alive || t.hp >= t.max || t.sector > M.opened) continue;
      const w = t.w > 1 ? 40 : 24, x = Math.round((t.col + t.w / 2) * T - w / 2), y = Math.round((t.row + 1) * T + 1);
      c.fillStyle = '#120f22'; c.fillRect(x - 1, y - 1, w + 2, 5);
      c.fillStyle = '#3a2f5c'; c.fillRect(x, y, w, 3);
      c.fillStyle = t.kind === 'gate' ? '#ff7a4a' : '#ffd040'; c.fillRect(x, y, Math.max(1, Math.round(w * t.hp / t.max)), 3);
    }
    // всплывающие числа
    c.textAlign = 'center';
    c.font = '900 13px Nunito, sans-serif';
    for (const tx of G.texts) {
      const a = 1 - tx.t / 0.9;
      c.globalAlpha = clamp(a, 0, 1);
      c.fillStyle = '#2a1c10';
      c.fillText(tx.text, tx.x + 1, tx.y - tx.t * 26 + 1);
      c.fillStyle = tx.color;
      c.fillText(tx.text, tx.x, tx.y - tx.t * 26);
    }
    c.globalAlpha = 1;
    // летящие монеты — в экранных координатах
    c.setTransform(view.dpr, 0, 0, view.dpr, 0, 0);
    for (const m of G.flying) {
      if (m.t < 0) continue;
      const k = easeOut(clamp(m.t / m.dur, 0, 1));
      const x = lerp(m.x0, m.x1, k), y = lerp(m.y0, m.y1, k) + Math.sin(k * Math.PI) * m.arc;
      const f = 'fx_coin_' + (Math.floor((m.t + m.x0) * 14) % 4);
      spr(f, x, y, false, 1, 2.2);
    }
  }
  function drawTarget(t) {
    const x = (t.col + t.w / 2) * T + (t.hitT > 0 ? Math.sin(t.hitT * 90) * 1.5 : 0);
    const y = (t.row + 1) * T;
    if (t.kind === 'gate') { drawGate(t); return; }
    spr(t.w > 1 ? 'shadow_l' : 'shadow_m', x, y - 3);
    const frac = t.hp / t.max;
    let name = t.base;
    if (t.kind === 'debris') name = t.base + (frac > 0.66 ? '' : frac > 0.33 ? '_d1' : '_d2');
    else name = t.base + (frac > 0.75 ? '' : frac > 0.5 ? '_d1' : frac > 0.25 ? '_d2' : '_d3');
    if (!SPR[name]) name = t.base;
    spr(name, x, y, t.flip);
    if (t.kind === 'chest' && t.sector <= M.opened) {
      const p = (Math.sin(G.time * 4 + t.col) + 1) * 0.5;
      spr('loot_glow', x, y - 4, false, 0.25 + p * 0.35);
    }
    if (t.hitT > 0) { ctx.globalCompositeOperation = 'lighter'; spr(name, x, y, t.flip, t.hitT * 3); ctx.globalCompositeOperation = 'source-over'; }
  }
  function drawGate(t) {
    const x = (t.col + 1) * T, y = (t.row + 1) * T;
    const frac = t.hp / t.max;
    const door = frac > 0.66 ? 'gate_door_closed' : frac > 0.33 ? 'gate_door_cracked' : 'gate_door_broken';
    ctx.save();
    ctx.translate(Math.round(x), Math.round(y - T / 2));
    const s1 = SPR['gate_frame'], s2 = SPR[door];
    if (s1) ctx.drawImage(atlas, s1[0], s1[1], s1[2], s1[3], -T, -T / 2 - 4, 2 * T, T + 6);
    if (s2) ctx.drawImage(atlas, s2[0], s2[1], s2[2], s2[3], -T + 3, -T / 2 + 2, 2 * T - 6, T - 4);
    ctx.restore();
    if (attackable(t)) {
      const p = (Math.sin(G.time * 6) + 1) * 0.5;
      ctx.strokeStyle = 'rgba(255,220,90,' + (0.35 + p * 0.5) + ')';
      ctx.lineWidth = 2;
      ctx.strokeRect(t.col * T + 1, t.row * T + 1, 2 * T - 2, T - 2);
    }
  }
  function drawMortar(m) {
    spr('mortar_base', m.x, m.y);
    spr('mortar_tube', m.x + 2, m.y - 6 + (m.kick > 0 ? m.kick * 8 : 0));
  }
  function drawLamps() {
    for (const l of M.lamps) {
      const f = Math.sin(G.time * 5 + l.phase) * 0.12 + 0.88;
      spr('fx_glow_lantern', l.x + (l.flip ? -4 : 4), l.y - 26, false, 0.55 * f, 1.8);
      spr(Math.floor(G.time * 4 + l.phase) % 2 ? 'lantern_1' : 'lantern_0', l.x + (l.flip ? -4 : 4), l.y - 18);
    }
    for (const p of camp.props) if (p.lamp) spr('fx_glow_warm', p.x, p.y - 22, false, 0.5 + Math.sin(G.time * 4) * 0.1, 1.6);
    if (M.exit >= 0 && targetsLeft() === 0) spr('exit_glow', cellX(M.exit), 6, false, 0.8 + Math.sin(G.time * 5) * 0.2);
  }
  function drawBarrel() {
    const b = G.barrel;
    if (!b) return;
    if (b.state === 'fly') {
      const k = clamp(b.t / b.dur, 0, 1);
      const x = lerp(b.x0, b.x1, k), y = lerp(b.y0, b.y1, k) - Math.sin(k * Math.PI) * 4.2 * T;
      spr(Math.floor(b.t * 10) % 2 ? 'barrel_fly_1' : 'barrel_fly_0', x, y, false, 1, 1.4);
    } else if (b.state === 'ground') {
      const blink = Math.floor(G.time * 6) % 2;
      const pulse = 1 + Math.sin(G.time * 8) * 0.06;
      spr('shadow_m', b.x1, b.y1 - 1);
      spr(blink ? 'barrel_tnt_1' : 'barrel_tnt_0', b.x1, b.y1, false, 1, 1.5 * pulse);
      ctx.strokeStyle = 'rgba(255,90,60,' + (0.4 + blink * 0.4) + ')';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(b.x1, b.y1 - 12, 22 + Math.sin(G.time * 8) * 3, 0, Math.PI * 2);
      ctx.stroke();
    }
  }
  /** Закрытые сектора — туман на всю ширину фона с мягким краем у ворот; сундуки за воротами просвечивают. */
  function drawDarkness() {
    const C = M.cols;
    const x0 = -SIDE * T, x1 = (C + SIDE) * T;
    for (let s = 1; s < M.sectors; s++) {
      const a = M.darkness[s];
      if (a <= 0.01) continue;
      let lo = M.rows, hi = -1;
      for (let r = 0; r < M.rows; r++) if (M.sector[r * C] === s) { lo = Math.min(lo, r); hi = Math.max(hi, r); }
      if (hi < 0) continue;
      const y0 = lo === 0 ? -TOP * T : lo * T, y1 = (hi + 1) * T;
      const fog = 'rgba(9,7,20,' + a + ')';
      ctx.fillStyle = fog;
      ctx.fillRect(x0, y0, x1 - x0, y1 - y0 - 10);
      const g = ctx.createLinearGradient(0, y1 - 10, 0, y1 + 8);
      g.addColorStop(0, fog);
      g.addColorStop(1, 'rgba(9,7,20,0)');
      ctx.fillStyle = g;
      ctx.fillRect(x0, y1 - 10, x1 - x0, 18);
    }
    for (const t of M.targets) {
      if (!t.alive || t.kind !== 'chest' || t.sector <= M.opened) continue;
      const p = (Math.sin(G.time * 3 + t.col) + 1) * 0.5;
      spr('loot_glow', (t.col + 0.5) * T, (t.row + 1) * T - 4, false, (0.3 + p * 0.4) * M.darkness[t.sector], 1.3);
    }
  }

  // ───────── таймеры игрового времени: идут вместе с ускорением теста и стоят на паузе рекламы ─────────
  const timers = [];
  function later(sec, fn) { timers.push({ at: G.time + sec, fn }); }
  function runTimers() {
    if (!timers.length) return;
    const due = timers.filter((x) => G.time >= x.at).sort((a, b) => a.at - b.at);
    for (const x of due) timers.splice(timers.indexOf(x), 1);
    for (const x of due) x.fn();
  }

  // ───────────────────────── обновление ─────────────────────────
  function update(dt) {
    G.time += dt;
    runTimers();
    if (!G.ended) G.playTime += dt;
    G.idle += dt;
    if (G.camera.shake > 0) G.camera.shake = Math.max(0, G.camera.shake - dt);
    if (camp.mortar && camp.mortar.kick > 0) camp.mortar.kick = Math.max(0, camp.mortar.kick - dt);
    for (let s = 1; s < M.sectors; s++) if (s <= M.opened && M.darkness[s] > 0) M.darkness[s] = Math.max(0, M.darkness[s] - dt * 1.4);
    for (const t of M.targets) if (t.hitT > 0) t.hitT = Math.max(0, t.hitT - dt);
    if (navDirty) { navDirty = false; for (const c of G.cats) if (c.state === 'idle') c.state = 'choose'; }
    for (const cat of G.cats) updateCat(cat, dt);
    const b = G.barrel;
    if (b && b.state === 'fly') {
      b.t += dt;
      if (b.t >= b.dur) {
        b.state = 'ground';
        b.groundT = G.time;
        G.fx.push({ frames: ['fx_puff_0', 'fx_puff_1', 'fx_puff_2', 'fx_puff_3'], fps: 14, x: b.x1, y: b.y1 - 4, t: 0 });
        Sfx.wood();
        Director.onBarrelLanded();
      }
    }
    for (let k = G.fx.length - 1; k >= 0; k--) {
      const f = G.fx[k];
      f.t += dt;
      if (f.chip) {
        f.vy += 420 * dt; f.x += f.vx * dt; f.y += f.vy * dt;
        if (f.t >= f.life) G.fx.splice(k, 1);
      } else if (f.t * f.fps >= f.frames.length) G.fx.splice(k, 1);
    }
    for (let k = G.texts.length - 1; k >= 0; k--) { G.texts[k].t += dt; if (G.texts[k].t > 0.9) G.texts.splice(k, 1); }
    for (let k = G.flying.length - 1; k >= 0; k--) {
      const m = G.flying[k];
      m.t += dt;
      if (m.t >= m.dur) { addGold(m.v); if (k % 3 === 0) Sfx.coin(); G.flying.splice(k, 1); }
    }
    if (G.shownGold !== G.gold) {
      const diff = G.gold - G.shownGold;
      G.shownGold = Math.abs(diff) < 1 ? G.gold : G.shownGold + diff * Math.min(1, dt * 10);
      $('goldv').textContent = fmt(G.shownGold);
    }
    followFront(dt);
    Director.update(dt);
    Hud.update(dt);
  }

  // ───────────────────────── интерфейс ─────────────────────────
  const Hud = (() => {
    const btns = {};
    let questEl = null;
    let quest = null;
    let hintTarget = null;
    let toastQ = [];
    const UP = {
      team: { icon: 'icon_branch_team', name: 'Hire Cat', info: () => G.cats.length + ' → ' + (G.cats.length + 1) + ' cats' },
      pick: { icon: 'icon_branch_pickaxes', name: 'Pickaxe', info: () => 'Damage ×' + fmtMul(Math.pow(G.pickStep, G.lv.pick + 1) * G.bonus.dmg) },
      boots: { icon: 'icon_branch_boots', name: 'Boots', info: () => 'Speed +' + Math.round(bootsBonus(G.lv.boots + 1) * 100) + '%' },
      bags: { icon: 'icon_branch_bags', name: 'Bags', info: () => 'Bag ' + (bagCap() + 1) },
    };
    function setup(list) {
      const dock = $('dock');
      dock.innerHTML = '';
      for (const k in btns) delete btns[k];
      dock.className = list.length ? 'n' + list.length : 'empty';
      for (const k of list) {
        const b = el('button', 'up', dock);
        b.dataset.k = k;
        const ic = el('i', 'ic', b);
        ic.style.backgroundImage = uimg(UP[k].icon);
        const tx = el('div', 'tx', b);
        el('b', 'nm', tx, UP[k].name);
        el('span', 'inf', tx, '');
        const pr = el('div', 'pr', b);
        el('i', 'ic coin', pr);
        el('span', 'v', pr, '0');
        el('div', 'lv', b, '');
        b.addEventListener('pointerdown', (e) => { e.stopPropagation(); Sfx.unlock(); buy(k); });
        btns[k] = b;
      }
    }
    function buy(k) {
      if (G.ended) return;
      if (isMax(k)) { toast(k === 'team' ? 'Team is full!' : 'Max level!'); return; }
      const p = price(k);
      if (G.gold < p) { const b = btns[k]; b.classList.remove('no'); void b.offsetWidth; b.classList.add('no'); Sfx.click(); return; }
      G.gold -= p;
      G.shownGold = G.gold;
      $('goldv').textContent = fmt(G.gold);
      G.lv[k]++;
      if (G.lvMine) G.lvMine[k]++;
      G.idle = 0;
      Sfx.up();
      const b = btns[k];
      b.classList.remove('pop'); void b.offsetWidth; b.classList.add('pop');
      if (k === 'team') {
        const cat = addCat(camp.intake, true);
        cat.joy = 0;
        Sfx.mew();
        toast('NEW CAT!');
      }
      if (k === 'pick') {
        const tier = Math.min(3, Math.floor(G.lv.pick / G.tierStep));
        const kind = KIND_ORDER[Math.max(KIND_ORDER.indexOf(G.pickKind), tier)];
        if (kind !== G.pickKind) { G.pickKind = kind; toast(kind.toUpperCase() + ' PICKAXE!', 'big'); for (const c of G.cats) c.joy = 0.8; }
      }
      if (k === 'boots') G.bootsKind = KIND_ORDER[Math.max(KIND_ORDER.indexOf(G.bootsKind), Math.min(3, Math.floor(G.lv.boots / G.tierStep)))];
      if (k === 'bags') G.packKind = KIND_ORDER[Math.max(KIND_ORDER.indexOf(G.packKind), Math.min(3, Math.floor(G.lv.bags / G.tierStep)))];
      Director.onBuy(k);
    }
    function update() {
      for (const k in btns) {
        const b = btns[k];
        const p = price(k);
        const full = isMax(k);
        setText(b.querySelector('.v'), full ? 'MAX' : fmt(p));
        setText(b.querySelector('.inf'), full ? (k === 'team' ? G.cats.length + ' cats' : 'Bag ' + bagCap()) : UP[k].info());
        setText(b.querySelector('.lv'), k === 'team' ? '' : 'Lv ' + (G.lv[k] + 1));
        b.classList.toggle('ok', !full && G.gold >= p);
        b.classList.toggle('max', full);
      }
      setText($('catsv'), String(G.cats.length));
      updateHint();
      if (quest) {
        const p = Math.min(quest.need, quest.get());
        const txt = quest.pct ? Math.floor((p / quest.need) * 100) + '%' : p + '/' + quest.need;
        if (questEl.qc.textContent !== txt) {
          questEl.qc.textContent = txt;
          questEl.qc.classList.remove('bump'); void questEl.qc.offsetWidth; questEl.qc.classList.add('bump');
        }
      }
    }
    function setQuest(q) {
      quest = q;
      const box = $('quest');
      if (!q) { box.classList.add('hidden'); return; }
      if (!questEl) questEl = { qi: box.querySelector('.qi'), qn: box.querySelector('.qn'), qs: box.querySelector('.qs'), qc: box.querySelector('.qc') };
      box.classList.remove('hidden', 'done');
      box.classList.remove('in'); void box.offsetWidth; box.classList.add('in');
      questEl.qi.style.backgroundImage = uimg(q.icon || 'icon_check');
      questEl.qn.textContent = q.title;
      questEl.qs.textContent = q.text;
      questEl.qc.textContent = '';
    }
    function questDone() {
      const box = $('quest');
      box.classList.add('done');
      toast('QUEST COMPLETE!', 'quest');
      Sfx.done();
    }
    function setHint(h) { hintTarget = h; }
    function updateHint() {
      const hand = $('hand');
      const bubble = $('hint');
      const h = hintTarget;
      G.camera.focus = null;
      if (!h || G.ended || (h.idle != null && G.idle < h.idle)) { hand.classList.add('hidden'); bubble.classList.add('hidden'); return; }
      let x, y;
      if (h.dom) {
        const r = (typeof h.dom === 'string' ? (btns[h.dom] || $(h.dom)) : h.dom).getBoundingClientRect();
        x = r.left + r.width * 0.55; y = r.top + r.height * 0.55;
      } else if (h.world) {
        const p = h.world();
        if (!p) { hand.classList.add('hidden'); bubble.classList.add('hidden'); return; }
        G.camera.focus = p.y;
        x = worldToScreenX(p.x); y = worldToScreenY(p.y);
      }
      if (x == null) { hand.classList.add('hidden'); } else {
        hand.classList.remove('hidden');
        hand.style.transform = 'translate(' + Math.round(x) + 'px,' + Math.round(y) + 'px)';
      }
      if (h.text) {
        bubble.classList.remove('hidden');
        const sp = bubble.querySelector('span');
        if (sp.textContent !== h.text) sp.textContent = h.text;
        // пузырь над целью (рука рисуется ниже пальца); нет места над ней — под рукой; не на квесте и не на панели
        const u = view.u, bh = bubble.offsetHeight;
        const q = $('quest');
        const minTop = (q.classList.contains('hidden') ? view.top : q.getBoundingClientRect().bottom) + 1.5 * u;
        let top = y == null ? minTop : y - 5 * u - bh;
        if (y != null && top < minTop) top = y + 17 * u;
        top = clamp(top, minTop, view.h - view.bottom - bh - 2 * u);
        // по горизонтали — центр области шахты; кнопка в правой колонке (альбом) — пузырь слева от руки
        let cx = (view.w - view.right) / 2;
        if (view.right > 0 && h.dom && x != null) {
          const bw = bubble.offsetWidth;
          cx = clamp(x - bw / 2 - 6 * u, bw / 2 + 2 * u, view.w - bw / 2 - 2 * u);
          top = clamp(y - bh / 2, minTop, view.h - bh - 2 * u);
        }
        bubble.style.left = Math.round(cx) + 'px';
        bubble.style.top = Math.round(top) + 'px';
      } else bubble.classList.add('hidden');
    }
    function toastNow(text, kind) {
      const box = $('toasts');
      const t = el('div', 'toast ' + (kind || ''), box, text);
      setTimeout(() => t.remove(), kind === 'big' || kind === 'quest' ? 1500 : 1100);
    }
    return {
      setup, update, setQuest, questDone, setHint, buy,
      toast(text, kind) {
        if (TEST && SPEED > 10) return; // на ускоренных снимках очередь надписей (реальное время) копилась бы стопкой
        const now = performance.now();
        toastQ = toastQ.filter((q) => now - q < 400);
        const delay = toastQ.length * 380;
        toastQ.push(now + delay);
        setTimeout(() => toastNow(text, kind), delay);
      },
    };
  })();
  function toast(text, kind) { Hud.toast(text, kind); }

  function showCard(cardId, rarity, bonusText, onDone) {
    const box = $('modal');
    box.innerHTML = '';
    box.classList.remove('hidden');
    el('div', 'rays', box);
    const wrap = el('div', 'reveal', box);
    const chest = el('div', 'chest shake', wrap);
    chest.style.backgroundImage = uimg(rarity === 'legendary' || rarity === 'majestic' ? 'icon_chest_legendary' : 'icon_chest_golden');
    Sfx.chest();
    let done = false;
    const finish = () => { if (done) return; done = true; box.classList.add('hidden'); box.innerHTML = ''; onDone && onDone(); };
    later(0.75, () => {
      chest.remove();
      const card = el('div', 'card ' + rarity, wrap);
      card.style.backgroundImage = uimg('ui_card_frame_' + rarity);
      const art = el('i', 'art', card);
      art.style.backgroundImage = uimg('card_art_' + cardId);
      el('div', 'rar', wrap, rarity.toUpperCase());
      el('div', 'cname', wrap, CFG.cards[cardId] || 'New card');
      el('div', 'cbonus', wrap, bonusText);
      el('div', 'tapc', wrap, 'TAP TO COLLECT');
      G.cardT = G.time;
      Sfx.done();
      box.onpointerdown = (e) => { e.stopPropagation(); finish(); if (VARIANT === 'short') Net.cta(); };
      later(VARIANT === 'short' ? 2.6 : 3.2, finish);
    });
  }

  function showEnd(title, sub, stats) {
    if (G.ended) return;
    G.ended = true;
    Hud.setHint(null);
    const box = $('end');
    box.innerHTML = '';
    box.classList.remove('hidden');
    el('div', 'fade', box);
    const card = el('div', 'ecard', box);
    el('div', 'rays', card);
    const icon = el('i', 'eicon', card);
    icon.style.backgroundImage = uimg('app_icon');
    el('div', 'etitle', card, 'CAT MINERS');
    el('div', 'esub', card, sub || title);
    if (stats && stats.length) {
      const st = el('div', 'estats', card);
      for (const [ic, v, label] of stats) {
        const s = el('div', 'st', st);
        const i = el('i', 'ic', s);
        i.style.backgroundImage = uimg(ic);
        el('b', '', s, v);
        el('span', '', s, label);
      }
    }
    el('div', 'emore', card, '144 mines • 62 cards\nfactories • legendary gear');
    const b = el('button', 'btn gold ebtn', card, 'PLAY NOW');
    b.addEventListener('click', (e) => { e.stopPropagation(); Net.cta(); });
    box.addEventListener('click', () => Net.cta());
    Sfx.done();
    Net.end();
  }

  // ───────────────────────── сценарии ─────────────────────────
  const Director = (() => {
    let D = null;
    return {
      set(d) { D = d; },
      update(dt) { D && D.update && D.update(dt); },
      onDestroy(t, cat) { D && D.destroy && D.destroy(t, cat); },
      onChest(t) { D && D.chest && D.chest(t); },
      onBoom() { D && D.boom && D.boom(); },
      onBarrelLanded() { D && D.landed && D.landed(); },
      onCleared() { D && D.cleared && D.cleared(); },
      onBuy(k) { D && D.buy && D.buy(k); },
      onTap(t, hitSomething) { return D && D.tap ? D.tap(t, hitSomething) : false; },
    };
  })();

  function startMine(rowsArt, opts) {
    M = buildMine(rowsArt, opts);
    G.cats = [];
    G.fx = [];
    G.barrel = null;
    buildCamp();
    bg = null; // фон новой шахты строит layout → ensureSides, уже зная ширину экрана
    navDirty = true;
    layout();
    G.camera.y = G.camera.target = maxScroll();
    $('mine').textContent = 'Mine ' + M.number;
  }
  function spawnTeam(n, cells) {
    for (let k = 0; k < n; k++) {
      const cell = cells && cells[k] != null ? cells[k] : camp.intake + ((k % 5) - 2);
      addCat(cell, false);
    }
  }
  function chestCardBonus(id) {
    switch (id) {
      case 'ca035': G.bonus.dmg *= 2; G.pickKind = 'legendary'; return 'Legendary pickaxes · damage ×2';
      case 'ca036': {
        if (G.maxCats >= CAT_CAP) { G.bonus.dmg *= 1.25; return 'Team spirit · damage +25%'; }
        G.maxCats = Math.min(CAT_CAP, G.maxCats + 2);
        for (let k = 0; k < 2 && G.cats.length < G.maxCats; k++) addCat(camp.intake, true);
        return '+2 cats join the team';
      }
      case 'ca037': G.bonus.gold *= 1.5; return 'Gold from every rock +50%';
      case 'ca038': G.bonus.speed *= 1.3; G.hatKind = 'legendary'; return 'Golden helmets · speed +30%';
      case 'ca034': G.bonus.bag += 2; G.packKind = 'epic'; return 'Bigger bags · +2 sacks';
      case 'ca053': G.bonus.dmg *= 1.35; return 'Damage +35%';
      case 'ca054': G.dynamitePower = (G.dynamitePower || 1) * 1.5; return 'Dynamite blast +50%';
      case 'ca031': G.bonus.gold *= 1.25; return 'Gold +25%';
      case 'ca027': G.bonus.speed *= 1.2; G.bootsKind = 'rare'; return 'Cats run +20% faster';
      case 'ca062': G.bonus.gold *= 1.6; return 'Treasure! Gold +60%';
      default: G.bonus.dmg *= 1.2; return 'Damage +20%';
    }
  }
  const RARITY = { ca035: 'legendary', ca036: 'legendary', ca037: 'legendary', ca038: 'legendary', ca034: 'legendary', ca053: 'epic', ca054: 'epic', ca031: 'rare', ca027: 'rare', ca062: 'majestic' };
  function openChestReveal(t, after) {
    const id = t.card || 'ca053';
    const text = chestCardBonus(id);
    showCard(id, RARITY[id] || 'rare', text, after);
    for (const c of G.cats) c.joy = 1.0;
  }

  // ── SHORT: 2–3 тапа до магазина. Золотая лихорадка: динамит, сундук с легендарной картой, финал. ──
  function directorShort() {
    const art = [
      '####E####',
      '#gooCoog#',
      '#oGoooGo#',
      '#ooogooo#',
      '#odooodo#',
      '#ooGoooo#',
      '#ogoooGo#',
      '#oodoodo#',
      '#o..o..o#',
      '#.......#',
      '###...###',
      '###...###',
    ];
    startMine(art, { number: 7, seed: 9, hp: 3, value: 40, chestCards: ['ca035'] });
    G.prices = { team: 1e9, pick: 1e9, boots: 1e9, bags: 1e9 };
    G.baseDmg = 1;
    G.pickKind = 'rare'; G.hatKind = 'rare'; G.packKind = 'common'; G.bootsKind = 'rare';
    spawnTeam(7, [9 * 9 + 1, 9 * 9 + 2, 9 * 9 + 4, 9 * 9 + 6, 9 * 9 + 7, 8 * 9 + 2, 8 * 9 + 5]);
    Hud.setup([]);
    G.gold = 1250; G.shownGold = G.gold;
    let stage = 'intro', taps = 0, t = 0, armedAt = 0, chestAt = 0, chest = M.targets.find((x) => x.kind === 'chest');
    toast('GOLD RUSH!', 'big');
    function goChest() {
      stage = 'chest';
      chestAt = t;
      Hud.setHint({ world: () => (chest && chest.alive ? { x: (chest.col + 0.5) * T, y: (chest.row + 0.7) * T } : null), text: 'Open the golden chest!' });
    }
    return {
      update(dt) {
        t += dt;
        if (stage === 'intro' && t > 0.7) { fireDynamite(); stage = 'flying'; }
        // зритель не тапает — сцена всё равно доигрывается: взрыв через 4,5 с, сундук через 4 с после подсказки
        if (stage === 'armed' && t - armedAt > 4.5) { explode(); }
        if (stage === 'chest' && t - chestAt > 4 && G.idle > 4) { openChest(); }
      },
      landed() {
        stage = 'armed';
        armedAt = t;
        Hud.setHint({ world: () => (G.barrel ? { x: G.barrel.x1, y: G.barrel.y1 - 8 } : null), text: 'Tap the dynamite!' });
      },
      boom() { stage = 'afterboom'; Hud.setHint(null); later(0.9, goChest); },
      tap() {
        taps++;
        if (stage === 'flying') return true;
        if (stage === 'armed') { explode(); return true; }
        if (stage === 'chest') { openChest(); return true; }
        if (stage === 'reveal' || stage === 'end' || taps >= 3) { Net.cta(); if (!G.ended) end(); return true; }
        return true;
      },
      chest(tt) {
        stage = 'reveal';
        Hud.setHint(null);
        openChestReveal(tt, () => { for (const c of G.cats) c.joy = 0.8; later(0.7, end); });
      },
      cleared() {},
    };
    function openChest() {
      if (!chest || !chest.alive) return;
      G.idle = 0;
      hit(chest, chest.hp + 1, null, true);
    }
    function end() {
      stage = 'end';
      testLog('short end @' + G.playTime.toFixed(1) + 's taps=' + taps + ' cta=' + (G.ctaCount || 0));
      showEnd('Strike it rich!', 'Strike it rich with your cat crew!', [['icon_coin', fmt(G.gold), 'gold'], ['icon_cat_big', String(G.cats.length), 'cats'], ['icon_cards', '1', 'legendary card']]);
    }
  }

  // ── MEDIUM: одна шахта целиком (~2–3 минуты): квесты ведут через найм, кирку, динамит, сундук, ворота. ──
  const MEDIUM_ART = [
    '####E####',
    '#gooLoog#',
    '#oGoodGo#',
    '#oogob_o#',
    '#odooogo#',
    '##ooooo##',
    '###..####',
    '###==####',
    '###oo####',
    '#cdooob_#',
    '#oogoooo#',
    '#oob_odo#',
    '##ooooo##',
    '###oo####',
    '###do####',
  ];
  function fixArt(art) {
    // '_' — вторая клетка завала 2×1; символы за пределами ширины обрезаются
    const w = art[0].length;
    return art.map((r) => (r + '#'.repeat(w)).slice(0, w));
  }
  function questDirector(quests, onAllDone) {
    let qi = -1, cur = null, wait = 0;
    function next() {
      qi++;
      cur = quests[qi] || null;
      Hud.setQuest(cur);
      Hud.setHint(cur && cur.hint ? cur.hint : null);
      if (cur && cur.start) cur.start();
      if (!cur && onAllDone) onAllDone();
    }
    return {
      begin() { next(); },
      update(dt) {
        if (!cur) return;
        if (wait > 0) { wait -= dt; if (wait <= 0) next(); return; }
        if (cur.get() >= cur.need) {
          Hud.questDone();
          if (cur.reward) { const r = cur.reward(); if (r) flyCoins(view.w / 2, view.top + 60, r, 6); }
          Hud.setHint(null);
          wait = 1.1;
        }
      },
      get current() { return cur; },
    };
  }
  function nearestTarget(filter) {
    let best = null, bd = 1e9;
    const field = bfs(camp.intake);
    for (const t of M.targets) {
      if (!attackable(t) || (filter && !filter(t))) continue;
      for (const w of workCells(t)) {
        const d = field.dist[w];
        if (d >= 0 && d < bd) { bd = d; best = t; }
      }
    }
    return best;
  }
  function targetPoint(t) { return t && t.alive ? { x: (t.col + t.w / 2) * T, y: (t.row + 0.7) * T } : null; }

  function directorMedium() {
    startMine(fixArt(MEDIUM_ART), { number: 1, seed: 3, hp: 0.7, value: 1, chestCards: ['ca036', 'ca035'] });
    G.prices = { team: 14, pick: 9, boots: 10, bags: 10 };
    spawnTeam(2);
    Hud.setup(['team', 'pick']);
    const tapped = { n: 0 }, done = { boom: 0 };
    let tapTarget = null, boomAt = 0;
    const firstChest = () => M.targets.find((t) => t.kind === 'chest' && t.tier === 'wooden');
    const gate = () => M.targets[M.gates[0]];
    const total = M.targets.length;
    const qd = questDirector([
      { title: 'First strike', text: 'Tap the rocks!', icon: 'icon_coin', need: 5, get: () => tapped.n,
        hint: { world: () => { if (!tapTarget || !tapTarget.alive) tapTarget = nearestTarget((t) => t.kind === 'ore'); return targetPoint(tapTarget); }, text: 'Tap rocks to mine gold!' } },
      { title: 'Bigger team', text: 'Hire a cat', icon: 'icon_branch_team', need: 1, get: () => G.lv.team,
        hint: { dom: 'team', text: 'Hire another miner!' }, start: () => { if (G.gold < price('team')) G.gold = Math.max(G.gold, price('team')); } },
      { title: 'Sharper picks', text: 'Upgrade the pickaxe', icon: 'icon_branch_pickaxes', need: 2, get: () => G.lv.pick, hint: { dom: 'pick', text: 'Upgrade the pickaxe!', idle: 1.5 } },
      { title: 'Kaboom!', text: 'Blast the dynamite', icon: 'barrel_tnt_0', need: 1, get: () => done.boom,
        start: () => { boomAt = t + 0.4; },
        hint: { world: () => (G.barrel && G.barrel.state === 'ground' ? { x: G.barrel.x1, y: G.barrel.y1 - 8 } : null), text: 'Tap the dynamite!' } },
      { title: 'Treasure', text: 'Open the chest', icon: 'icon_chest_wooden', need: 1, get: () => G.chests,
        hint: { world: () => targetPoint(firstChest()), text: 'A chest! Tap it!', idle: 2 } },
      { title: 'Next sector', text: 'Break the gate', icon: 'icon_check', need: 1, get: () => G.stats.gates,
        hint: { world: () => { const g = gate(); return g && attackable(g) ? { x: (g.col + 1) * T, y: (g.row + 0.6) * T } : null; }, text: 'Break the gate!', idle: 1 } },
      { title: 'Clear Mine 1', text: 'Mine everything', icon: 'icon_coin', need: total, pct: true, get: () => total - targetsLeft(),
        hint: { world: () => targetPoint(M.targets.find((t) => t.kind === 'chest' && t.alive && attackable(t))), text: 'The legendary chest!', idle: 4 } },
    ], () => {});
    let t = 0, ended = false, begun = false;
    return {
      update(dt) {
        t += dt;
        if (t > 0.3 && !begun) { begun = true; qd.begin(); }
        qd.update(dt);
        // динамит квеста «Kaboom!»: пока бочка не вылетела, пробуем снова; без целей для взрыва квест засчитывается
        if (boomAt > 0 && t >= boomAt && !G.barrel) { boomAt = 0; if (!fireDynamite()) done.boom = 1; }
        if (G.barrel && G.barrel.state === 'ground' && G.idle > 8) explode();
      },
      tap(target) { if (target) tapped.n++; return false; },
      boom() { done.boom++; },
      chest(tt) { openChestReveal(tt); },
      buy() {},
      cleared() {
        if (ended) return;
        ended = true;
        testLog('medium cleared @' + Math.round(G.playTime) + 's cats=' + G.cats.length + ' pick=' + G.lv.pick + ' taps=' + G.stats.taps + ' earned=' + fmt(G.earned));
        toast('MINE CLEARED!', 'big');
        Sfx.done();
        for (const c of G.cats) c.joy = 1.4;
        later(1.6, () => showEnd('Mine 1 cleared!', 'Mine 1 cleared! 143 more mines are waiting.', [['icon_coin', fmt(G.earned), 'gold mined'], ['icon_cat_big', String(G.cats.length), 'cats'], ['icon_cards', String(G.chests), 'cards']]));
      },
    };
  }

  // ── LONG: до 30 минут, шахта за шахтой; карточки сундуков — навсегда, улучшения — копятся. ──
  function genMineArt(k, r) {
    const cols = k >= 6 ? 11 : 9;
    const rows = Math.min(30, 12 + 2 * (k - 1));
    const g = [];
    for (let y = 0; y < rows; y++) g.push(new Array(cols).fill('#'));
    const sx = (cols >> 1) - 1;
    for (let y = 0; y < rows; y++) { g[y][sx] = 'o'; g[y][sx + 1] = 'o'; }
    const sectors = k === 1 ? 1 : k <= 4 ? 2 : 3;
    const gateRows = [];
    for (let s = 1; s < sectors; s++) gateRows.push(Math.round(rows * (1 - s / sectors)));
    let side = r() < 0.5 ? -1 : 1;
    for (let y = rows - 3; y >= 2; y -= 4 + (r() < 0.5 ? 1 : 0)) {
      const h = r() < 0.5 ? 2 : 3;
      if (gateRows.some((gr) => gr >= y - h - 1 && gr <= y + 1)) { side = -side; continue; }
      const x0 = side < 0 ? 1 : sx + 2, x1 = side < 0 ? sx - 1 : cols - 2;
      for (let yy = y - h + 1; yy <= y; yy++) for (let x = x0; x <= x1; x++) g[yy][x] = 'o';
      side = -side;
    }
    for (const gr of gateRows) { for (let x = 0; x < cols; x++) if (x !== sx && x !== sx + 1) g[gr][x] = '#'; g[gr][sx] = '='; g[gr][sx + 1] = '='; g[gr - 1][sx] = '.'; g[gr - 1][sx + 1] = '.'; }
    g[0][sx] = 'E';
    // содержимое: сундук в каждом секторе, самоцветы, мусор, завалы 2×1
    const bounds = [rows, ...gateRows, -1];
    for (let s = 0; s < sectors; s++) {
      const lo = bounds[s + 1] + 1, hi = bounds[s] - 1;
      const cells = [];
      for (let y = lo; y <= hi; y++) for (let x = 0; x < cols; x++) if (g[y][x] === 'o') cells.push([x, y]);
      cells.sort((a, b) => Math.abs(b[0] - sx - 0.5) - Math.abs(a[0] - sx - 0.5) || a[1] - b[1]);
      if (cells.length) { const [x, y] = cells[0]; g[y][x] = s === sectors - 1 ? (k >= 8 ? 'L' : 'C') : k >= 5 ? 'i' : 'c'; }
    }
    for (let y = 1; y < rows - 1; y++) {
      if (g[y][sx] === 'o' && g[y][sx + 1] === 'o' && r() < 0.14 && !gateRows.some((gr) => Math.abs(gr - y) <= 1)) { g[y][sx] = 'b'; g[y][sx + 1] = '_'; continue; }
      for (let x = 0; x < cols; x++) {
        if (g[y][x] !== 'o') continue;
        const p = r();
        g[y][x] = p < 0.07 ? (r() < 0.3 ? 'G' : 'g') : p < 0.3 ? 'd' : 'o';
      }
    }
    return g.map((row) => row.join(''));
  }
  const LONG_CARDS = ['ca053', 'ca027', 'ca031', 'ca036', 'ca054', 'ca034', 'ca037', 'ca038', 'ca035', 'ca062', 'ca053', 'ca031', 'ca037', 'ca035', 'ca038', 'ca036'];
  function directorLong() {
    let k = 0, cardI = 0, ended = false, boomTimer = 40;
    const MAX_MINES = 15, LIMIT = 30 * 60;
    // Забег: золото, уровни и цены копятся; ценность руды растёт ×1.6 за шахту, цены — ×1.2–1.7 за уровень,
    // поэтому на каждую шахту приходится несколько покупок в каждой ветке, а не всплеск дешёвых уровней.
    G.prices = { team: 25, pick: 20, boots: 24, bags: 24 };
    G.growth = { team: 1.7, pick: 1.2, boots: 1.22, bags: 1.3 };
    G.pickStep = 1.12;
    G.tierStep = 8;
    G.maxCats = 12;
    G.maxBags = 16;
    let qd = null;
    function nextMine() {
      k++;
      const r = rng(1000 + k * 77);
      const art = genMineArt(k, r);
      // Темп: HP подстраивается под силу команды на входе в шахту — шахта длится ~1,5–4 минуты, а не растягивается.
      const dps = Math.max(1, G.cats.length || 2) * catDmg() / G.hitInterval * 0.5;
      const cardsHere = [];
      for (const row of art) for (const ch of row) if ('ciCL'.indexOf(ch) >= 0) cardsHere.push(LONG_CARDS[cardI++ % LONG_CARDS.length]);
      const team = G.cats.length || 2;
      const targetSec = 60 + 10 * Math.min(k, 12);
      const cellsCount = art.join('').replace(/[#.E=_]/g, '').length;
      const hp = Math.max(1, (dps * targetSec) / (cellsCount * 11));
      const value = Math.pow(1.6, k - 1);
      startMine(art, { number: k, seed: k * 13, hp, value, chestCards: cardsHere });
      G.lvMine = { team: 0, pick: 0, boots: 0, bags: 0 };
      spawnTeam(team);
      boomTimer = 35;
      testLog('mine ' + k + ' @' + Math.round(G.playTime) + 's cats=' + team + ' pick=' + G.lv.pick + ' boots=' + G.lv.boots + ' bags=' + G.lv.bags + ' gold=' + fmt(G.gold) + ' targets=' + M.targets.length);
      toast('MINE ' + k, 'big');
      const total = M.targets.length;
      const quests = [];
      if (k === 1) quests.push({ title: 'First strike', text: 'Tap the rocks!', icon: 'icon_coin', need: 5, get: () => G.stats.taps,
        hint: { world: () => targetPoint(nearestTarget((t) => t.kind === 'ore')), text: 'Tap rocks to mine gold!' } });
      if (k === 1) quests.push({ title: 'Bigger team', text: 'Hire a cat', icon: 'icon_branch_team', need: 1, get: () => G.lvMine.team, hint: { dom: 'team', text: 'Hire another miner!', idle: 1 } });
      if (k === 2) quests.push({ title: 'Faster feet', text: 'Upgrade boots', icon: 'icon_branch_boots', need: 1, get: () => G.lvMine.boots, hint: { dom: 'boots', text: 'New boots — faster cats!', idle: 2 } });
      if (k === 3) quests.push({ title: 'Heavy loads', text: 'Upgrade bags', icon: 'icon_branch_bags', need: 1, get: () => G.lvMine.bags, hint: { dom: 'bags', text: 'Bigger bags carry more gold!', idle: 2 } });
      quests.push({ title: 'Treasure hunt', text: 'Open chests', icon: 'icon_chest_golden', need: cardsHere.length, get: () => M.targets.filter((t) => t.kind === 'chest' && !t.alive).length,
        hint: { world: () => targetPoint(M.targets.find((t) => t.kind === 'chest' && attackable(t))), text: 'Chest! Tap it!', idle: 5 } });
      quests.push({ title: 'Clear Mine ' + k, text: 'Mine everything', icon: 'icon_coin', need: total, pct: true, get: () => total - targetsLeft(),
        hint: { world: () => { const g = M.gates.map((i) => M.targets[i]).find((t) => attackable(t)); return g ? { x: (g.col + 1) * T, y: (g.row + 0.6) * T } : null; }, text: 'Break the gate!', idle: 2 } });
      qd = questDirector(quests, null);
      qd.begin();
    }
    G.lv = { team: 0, pick: 0, boots: 0, bags: 0 };
    Hud.setup(['team', 'pick', 'boots', 'bags']);
    nextMine();
    return {
      update(dt) {
        if (qd) qd.update(dt);
        if (!ended && G.playTime >= LIMIT) finish();
        boomTimer -= dt;
        if (boomTimer <= 0 && !G.barrel && !ended) { if (fireDynamite()) toast('DYNAMITE!'); boomTimer = 55; }
        if (G.barrel && G.barrel.state === 'ground') {
          Hud.setHint(G.idle > 1.5 ? { world: () => (G.barrel && G.barrel.state === 'ground' ? { x: G.barrel.x1, y: G.barrel.y1 - 8 } : null), text: 'Tap the dynamite!' } : (qd && qd.current ? qd.current.hint : null));
          if (G.idle > 10) explode();
        }
      },
      tap() { return false; },
      boom() { if (qd && qd.current) Hud.setHint(qd.current.hint || null); },
      chest(tt) { openChestReveal(tt); },
      buy() {},
      cleared() {
        if (ended) return;
        toast('MINE ' + k + ' CLEARED!', 'big');
        Sfx.done();
        for (const c of G.cats) c.joy = 1.4;
        G.mines = k;
        if (k >= MAX_MINES) { later(1.5, finish); return; }
        later(2.4, () => { if (!ended) nextMine(); });
      },
    };
    function finish() {
      if (ended) return;
      ended = true;
      testLog('END @' + Math.round(G.playTime) + 's mine=' + k + ' cleared=' + G.mines + ' cats=' + G.cats.length + ' pick=' + G.lv.pick + ' boots=' + G.lv.boots + ' bags=' + G.lv.bags + ' earned=' + fmt(G.earned));
      showEnd('Great run!', 'You reached Mine ' + k + '! The real adventure is just beginning.', [['icon_coin', fmt(G.earned), 'gold mined'], ['icon_cat_big', String(G.cats.length), 'cats'], ['icon_cards', String(G.chests), 'cards']]);
    }
  }

  // ───────────────────────── ввод ─────────────────────────
  function setupInput() {
    const cv = $('cv');
    let down = null;
    cv.addEventListener('pointerdown', (e) => {
      Sfx.unlock();
      down = { x: e.clientX, y: e.clientY, cy: G.camera.y, t: performance.now(), moved: false };
      G.camera.drag = true;
    });
    window.addEventListener('pointermove', (e) => {
      if (!down) return;
      const dy = e.clientY - down.y;
      if (Math.abs(dy) > 9 || Math.abs(e.clientX - down.x) > 9) down.moved = true;
      if (down.moved && maxScroll() > 0) {
        G.camera.y = clamp(down.cy - dy / view.scale, 0, maxScroll());
        G.camera.target = G.camera.y;
        G.camera.user = 3;
      }
    });
    window.addEventListener('pointerup', (e) => {
      G.camera.drag = false;
      if (!down) return;
      const d = down;
      down = null;
      if (d.moved) return;
      onTap(e.clientX, e.clientY);
    });
    // магазин — по всей нижней плашке (иконка, название, кнопка)
    $('cta').addEventListener('click', (e) => { e.stopPropagation(); Net.cta(); });
    $('cta').addEventListener('pointerdown', (e) => e.stopPropagation());
    $('mute').addEventListener('pointerdown', (e) => { e.stopPropagation(); Sfx.unlock(); $('mute').classList.toggle('off', Sfx.toggle()); });
    for (const ev of ['gesturestart', 'gesturechange', 'contextmenu', 'dblclick']) document.addEventListener(ev, (e) => e.preventDefault());
    document.addEventListener('touchmove', (e) => { if (e.cancelable) e.preventDefault(); }, { passive: false });
  }
  function onTap(sx, sy) {
    if (G.ended) { Net.cta(); return; }
    G.idle = 0;
    const w = screenToWorld(sx, sy);
    // динамит
    const b = G.barrel;
    if (b && b.state === 'ground' && Math.hypot(w.x - b.x1, w.y - (b.y1 - 10)) < 26) { explode(); Director.onTap(null, true); return; }
    const col = Math.floor(w.x / T), row = Math.floor((w.y + 6) / T);
    let target = null;
    for (const dy of [0, -1]) {
      const r = row + dy;
      if (col < 0 || col >= M.cols || r < 0 || r >= M.rows) continue;
      const id = M.occ[r * M.cols + col];
      if (id >= 0 && M.targets[id].alive) { target = M.targets[id]; break; }
    }
    if (Director.onTap(target, !!target)) return;
    if (target && target.sector <= M.opened && (target.kind !== 'gate' || attackable(target))) {
      G.stats.taps++;
      Sfx.tap();
      G.fx.push({ frames: ['fx_strike_pick_0', 'fx_strike_pick_1', 'fx_strike_pick_2', 'fx_strike_pick_3'], fps: 18, x: w.x + 6, y: w.y, t: 0, scale: 1.4 });
      G.fx.push({ frames: ['fx_impact_0', 'fx_impact_1', 'fx_impact_2'], fps: 20, x: (target.col + target.w / 2) * T, y: (target.row + 1) * T - 12, t: 0, scale: 1.5 });
      hit(target, tapDamage(), null, true);
    } else if (target && target.sector > M.opened) toast('Break the gate first!');
    else if (target && target.kind === 'gate') toast('Clear this sector first!');
  }

  // ───────── тестовый режим (#test=скорость): автоигрок для снимков сборки; в обычном запуске выключен ─────────
  const TEST = /[#&]test(?:=([\d.]+))?/.exec(location.hash);
  const SPEED = TEST ? parseFloat(TEST[1]) || 1 : 1;
  if (TEST) {
    // &vh=высота: headless-Chrome считает страницу на ~110 px ниже окна и растягивает её только при снимке —
    // фиксируем высоту страницы и единицу интерфейса, чтобы снимок совпадал с телефоном этого размера
    const vh = /[#&]vh=(\d+)/.exec(location.hash);
    if (vh) {
      const H = +vh[1];
      document.body.style.bottom = 'auto';
      document.body.style.height = H + 'px';
      const W = innerWidth;
      document.documentElement.style.setProperty('--u', (W > H ? Math.min(H, 0.462 * W) : Math.min(W, 0.462 * H)) / 100 + 'px');
      // CSS-анимации в виртуальном времени headless застывают на середине — на снимках элементы в конечном виде
      el('style', '', document.head, '*,*::before,*::after{animation:none!important;transition:none!important}');
    }
    const dbg = el('div', '', document.body);
    dbg.style.cssText = 'position:fixed;left:4px;bottom:4px;z-index:99;font:11px monospace;color:#ff6;background:rgba(0,0,0,.7);max-width:96vw;white-space:pre-wrap';
    window.addEventListener('error', (e) => { dbg.textContent += 'ERR ' + e.message + ' @' + e.lineno + ':' + e.colno + '\n'; });
    setInterval(() => { dbg.dataset.s = innerWidth + 'x' + innerHeight + ' view=' + view.w + 'x' + view.h + ' s=' + view.scale.toFixed(2) + ' top=' + Math.round(view.top) + ' bot=' + Math.round(view.bottom) + ' t=' + G.time.toFixed(1) + ' play=' + G.playTime.toFixed(0) + ' mine=' + (M ? M.number : 0) + ' left=' + (M ? targetsLeft() : 0) + ' cats=' + G.cats.length + ' gold=' + Math.round(G.gold) + ' cta=' + (G.ctaCount || 0) + (G.ended ? ' END' : ''); if (!/ERR/.test(dbg.textContent)) dbg.textContent = dbg.dataset.s; }, 250);
  }
  // Автоигрок: быстрый (тап каждые 0,35 с, покупка сразу) и &bot=casual (тап раз в 1,2 с, покупка через 3 с, динамит через 2,5 с).
  const CASUAL = /[#&]bot=casual/.test(location.hash), NOBOT = /[#&]bot=none/.test(location.hash);
  let autoT = 0;
  const okSince = {};
  function autoPlay(dt) {
    if (NOBOT) return;
    autoT += dt;
    if (autoT < (CASUAL ? 1.2 : 0.35)) return;
    autoT = 0;
    const modal = $('modal');
    if (!modal.classList.contains('hidden') && modal.onpointerdown) { if (G.time - G.cardT > (CASUAL ? 2 : 1.2)) modal.onpointerdown({ stopPropagation() {} }); return; }
    if (G.ended) return;
    const b = G.barrel;
    if (VARIANT === 'short') {
      if (b && b.state === 'ground') { onTap(worldToScreenX(b.x1), worldToScreenY(b.y1 - 10)); return; }
      const ch = M.targets.find((t) => t.kind === 'chest' && t.alive);
      if (ch && G.stats.booms > 0 && G.time > 4.2) { onTap(worldToScreenX((ch.col + 0.5) * T), worldToScreenY((ch.row + 0.6) * T)); }
      return;
    }
    if (b && b.state === 'ground' && (!CASUAL || G.time - b.groundT > 2.5)) { onTap(worldToScreenX(b.x1), worldToScreenY(b.y1 - 10)); return; }
    for (const k of ['team', 'pick', 'boots', 'bags']) {
      const btn = document.querySelector('.up[data-k="' + k + '"]');
      if (!btn || !btn.classList.contains('ok')) { okSince[k] = 0; continue; }
      if (!okSince[k]) okSince[k] = G.time;
      if (!CASUAL || G.time - okSince[k] > 3) { okSince[k] = 0; Hud.buy(k); return; }
    }
    const t = nearestTarget(null);
    if (t) onTap(worldToScreenX((t.col + t.w / 2) * T), worldToScreenY((t.row + 0.6) * T));
  }
  /** Журнал теста: строки в скрытом <pre id="testlog"> (читается через --dump-dom); &log=1 — показать на экране. */
  function testLog(line) {
    if (!TEST) return;
    let pre = $('testlog');
    if (!pre) {
      pre = el('pre', '', document.body);
      pre.id = 'testlog';
      pre.style.cssText = 'position:fixed;left:4px;top:40%;z-index:99;font:11px monospace;color:#9f9;background:rgba(0,0,0,.75);margin:0;white-space:pre-wrap;max-width:96vw' + (/[#&]log=1/.test(location.hash) ? '' : ';display:none');
    }
    pre.textContent += line + '\n';
  }

  // ───────────────────────── запуск ─────────────────────────
  function boot() {
    $('cta').querySelector('.appicon').style.backgroundImage = uimg('app_icon');
    $('hand').style.backgroundImage = uimg('hand');
    $('hint').querySelector('.tutor').style.backgroundImage = uimg('tutor_cat_1');
    $('goldi').style.backgroundImage = uimg('icon_coin');
    $('catsi').style.backgroundImage = uimg('icon_cat_big');
    document.documentElement.style.setProperty('--coin', uimg('icon_coin'));
    setupInput();
    const d = VARIANT === 'short' ? directorShort() : VARIANT === 'medium' ? directorMedium() : directorLong();
    Director.set(d);
    layout();
    window.addEventListener('resize', () => setTimeout(layout, 30));
    // контейнеры рекламы (mraid, WebView) часто меняют размер без события resize — сверяем раз в 250 мс
    setInterval(() => { const b = document.body.getBoundingClientRect(); if (Math.round(b.width) !== view.w || Math.round(b.height) !== view.h) layout(); }, 250);
    window.addEventListener('orientationchange', () => [60, 300, 700].forEach((ms) => setTimeout(layout, ms)));
    let last = performance.now();
    let paused = false, rafSeen = false;
    Net.onPause = (p) => { paused = p; Sfx.suspend(p); last = performance.now(); };
    function step(now) {
      const dt = Math.min(0.05, Math.max(0, now - last) / 1000);
      last = now;
      if (paused) return;
      for (let k = 0; k < SPEED; k++) { update(dt); if (TEST) autoPlay(dt); }
      render();
      Sfx.tickMusic();
    }
    function frame(now) { rafSeen = true; requestAnimationFrame(frame); step(performance.now()); }
    requestAnimationFrame(frame);
    // Таймер на случай, когда кадры не приходят (снимки headless, часть WebView): тот же шаг 30 раз в секунду.
    setInterval(() => { if (!rafSeen) step(performance.now()); rafSeen = false; }, 33);
  }
  atlas.onload = () => {
    const go = () => Net.boot(boot);
    if (document.fonts && document.fonts.load) document.fonts.load('900 16px Nunito').then(go, go); else go();
  };
  atlas.src = CFG.atlas;
})();
