"""Оформление выработки (группа dressing): постоянные препятствия, разрушаемый мусор, декали пола и декор стен.

Постоянные препятствия (prop_*) — тяжёлые, «вросшие»: каменные подпятники, железные полосы, болты, толстый брус.
Разрушаемые (debris_*) — рыхлые, низкие, в обломках: доски, ящики, щебень, металлолом, коробки, пряжа,
кошачья мебель, мешки. У каждого базового debris_* есть стадии _d1 (треть отломана) и _d2 (остался огрызок),
у каждого семейства — след на полу debris_<семейство>_rest. Декали пола (decal_*) плоские и тихие;
декор стены (wall_*) рисуется на лицевой грани скальной клетки над полом.

Все холсты — с pivot «ноги» (w/2, 0): нижняя кромка холста = нижняя кромка footprint. Вид 3/4, свет сверху-слева,
контур 1 px — тёмный конец рампы материала. Холсты и имена — контракт с кодом Unity (manifest.py, группа dressing).
Координаты в комментариях — пиксели холста от левого ВЕРХНЕГО угла.
"""
from __future__ import annotations

import math
import random
from typing import Dict, List, Optional, Sequence, Tuple

from pxl import PALETTE, RAMPS, Canvas, with_alpha
from sprites.rocks import G, NAVY_PEB, pile, pebble
from sprites.structures import (DWOOD, IRON, ROCK, STONE, WOOD, blank, bolt, brace, grain_h, grain_v, hbeam, paw, put,
                                vbeam)

# ───────── материалы ─────────
RUST = dict(o='rust0', k='rust1', s='rust2', d='rust3', b='rust4', l='rust5', h='rust6', hh='rust7')
COPPER = dict(o='fur_ginger0', k='fur_ginger0', s='fur_ginger1', d='fur_ginger1', b='fur_ginger2', l='fur_ginger3',
              h='fur_ginger4', hh='fur_ginger4')
CARD = dict(o='wood1', k='wood2', s='wood3', d='wood4', b='wood5', l='wood6', h='wood7', hh='cream3')
SISAL = dict(o='wood1', k='wood2', s='wood3', d='cream0', b='cream1', l='cream2', h='cream3', hh='cream3')
BURLAP = dict(o='wood1', k='wood2', s='wood3', d='cream0', b='cream1', l='cream2', h='cream2', hh='cream3')
SLATE_PAL = dict(crev='slate0', shade='slate2', body='slate3', mid='slate4', top='slate5', rim='slate6', glint='slate7', out='ink2')
RUBBLE_STONE = dict(crev='stone0', shade='stone2', body='stone3', mid='stone4', top='stone5', rim='stone6', glint='stone6', out='stone0')
RUBBLE_NAVY = dict(crev='navy1', shade='navy3', body='navy4', mid='navy5', top='navy6', rim='navy7', glint='navy7', out='navy0')
YARN = {'red': ('red1', 'red2', 'red3', 'red4', 'red5'), 'blue': ('blue1', 'blue2', 'blue3', 'blue4', 'blue5'),
        'purple': ('purple1', 'purple2', 'purple3', 'purple4', 'purple5')}

# ───────── палитра: имя ↔ цвет, сдвиг тона по рампе ─────────
NAME_OF: Dict[Tuple[int, int, int], str] = {}
for _n, _c in PALETTE.items():
    if _n not in ('white', 'black'):
        NAME_OF.setdefault(_c, _n)


def _split(name: str) -> Tuple[str, int]:
    i = len(name)
    while name[i - 1].isdigit():
        i -= 1
    return name[:i], int(name[i:])


def tone_of(rgb, k: int, absolute: bool = False) -> str:
    """Тон той же рампы: со сдвигом k (absolute=False) или с индексом k."""
    name = NAME_OF.get(tuple(rgb[:3]))
    if name is None:
        return 'ink2'
    r, i = _split(name)
    n = len(RAMPS[r])
    j = k if absolute else i + k
    return f'{r}{max(0, min(n - 1, j))}'


# ───────────────────────── общие помощники ─────────────────────────
def box(cv: Canvas, x: int, y: int, w: int, top: int, front: int, m: Dict[str, str], planks: int = 0, seed: int = 0) -> None:
    """Брусок/ящик в виде 3/4: верхняя грань (rows y…y+top−1) светлая, передняя ниже. Контур снаружи."""
    lay = blank(cv)
    lay.rect(x, y, w, top, m['l'])
    lay.hline(x, x + w - 1, y, m['h'])
    lay.vline(x, y, y + top - 1, m['h'])
    if top >= 3:
        lay.hline(x + 1, x + w - 1, y + top - 1, m['hh'])
    y2 = y + top
    lay.rect(x, y2, w, front, m['b'])
    lay.hline(x, x + w - 1, y2, m['d'])
    lay.vline(x, y2 + 1, y2 + front - 1, m['l'])
    lay.vline(x + w - 1, y2, y2 + front - 1, m['d'])
    lay.hline(x, x + w - 1, y2 + front - 1, m['d'])
    if planks:
        step = max(3, front // (planks + 1))
        for k in range(1, planks + 1):
            yy = y2 + k * step
            if yy < y2 + front - 2:
                lay.hline(x + 1, x + w - 2, yy, m['s'])
    if front >= 6 and w >= 8:
        grain_h(lay, x + 2, y2 + 2, w - 4, front - 4, m['d'], seed, step=3)
    put(cv, lay, m['o'])


def slab(cv: Canvas, x: int, y: int, w: int, top: int, front: int, m: Dict[str, str] = STONE, seed: int = 0,
         seam: int = 7, moss: bool = False) -> None:
    """Каменный подпятник из тёсаных блоков: светлая верхняя грань, передняя со швами кладки."""
    lay = blank(cv)
    lay.rect(x, y, w, top, m['l'])
    lay.hline(x, x + w - 1, y, m['h'])
    y2 = y + top
    lay.rect(x, y2, w, front, m['d'])
    lay.hline(x, x + w - 1, y2, m['b'])
    lay.vline(x, y2, y2 + front - 1, m['b'])
    lay.hline(x, x + w - 1, y2 + front - 1, m['s'])
    rnd = random.Random(seed)
    xx = x + rnd.randrange(3, seam)
    while xx < x + w - 3:
        lay.vline(xx, y2 + 1, y2 + front - 2, m['s'])
        lay.set(xx + 1, y2 + 1, m['b'])
        lay.vline(xx, y, y + top - 1, m['b'])
        xx += seam + rnd.randrange(-1, 2)
    if moss:
        for i in range(rnd.randrange(3, 6)):
            mx = x + rnd.randrange(1, w - 2)
            lay.set(mx, y, 'green4')
            lay.set(mx + 1, y, 'green3')
            lay.set(mx, y2, 'green3')
    put(cv, lay, m['o'])


def band(cv: Canvas, x: int, y: int, w: int, rivets: Sequence[int] = (), m: Dict[str, str] = IRON) -> None:
    """Железная полоса 2 px поверх бруса с заклёпками (x заклёпок относительно x)."""
    cv.hline(x, x + w - 1, y, m['h'])
    cv.hline(x, x + w - 1, y + 1, m['d'])
    for r in rivets:
        cv.set(x + r, y, 'stone7')
        cv.set(x + r, y + 1, m['s'])


def board(cv: Canvas, x0: float, y0: float, x1: float, y1: float, t: float, m: Dict[str, str] = WOOD,
          th: int = 2, split0: bool = False, split1: bool = False, seed: int = 0, nail: Optional[float] = None) -> None:
    """Доска, лежащая на полу (вид 3/4): верхняя грань-полоса от (x0, y0) до (x1, y1) шириной t, под ней торец th px.
    split* — расщеплённый конец (зубцы)."""
    dx, dy = x1 - x0, y1 - y0
    ln = math.hypot(dx, dy) or 1.0
    ux, uy = dx / ln, dy / ln
    nx, ny = -uy, ux
    if ny < 0:
        nx, ny = -nx, -ny
    rnd = random.Random(seed)

    def end(px, py, sgn, split):
        if not split:
            return [(px, py), (px + nx * t, py + ny * t)]
        pts = []
        k = 4
        for i in range(k + 1):
            f = i / k
            out = (rnd.uniform(1.0, 3.2) if i % 2 else rnd.uniform(-0.6, 0.4)) * sgn
            pts.append((px + nx * t * f + ux * out, py + ny * t * f + uy * out))
        return pts

    a = end(x0, y0, -1, split0)
    b = end(x1, y1, 1, split1)
    poly = a[:1] + b[:1] + b[1:] + list(reversed(a[1:]))
    poly = [a[0]] + b + list(reversed(a[1:]))
    lay = blank(cv)
    side = [(px, py + th) for px, py in poly]
    lay.polygon(side, m['s'])
    lay.polygon(poly, m['b'])
    lay.shade(m['l'], m['d'], m['b'])
    # волокно вдоль доски
    if ln > 8:
        for f in (0.25, 0.6):
            gx0, gy0 = x0 + nx * t * 0.5 + ux * ln * f, y0 + ny * t * 0.5 + uy * ln * f
            for i in range(int(ln * 0.22)):
                px, py = int(gx0 + ux * i), int(gy0 + uy * i)
                if lay.get(px, py)[:3] == PALETTE[m['b']]:
                    lay.set(px, py, m['d'])
    if nail is not None:
        px, py = int(x0 + ux * ln * nail + nx * t * 0.4), int(y0 + uy * ln * nail + ny * t * 0.4)
        lay.set(px, py, 'stone6')
        lay.set(px + 1, py, 'stone2')
    put(cv, lay, m['o'])


def upright_board(cv: Canvas, x: int, y: int, w: int, h: int, m: Dict[str, str] = WOOD, jag_top: bool = True,
                  lean: float = 0.0, seed: int = 0) -> None:
    """Доска стоймя/наклонно (вид спереди): светлый левый край, тёмный правый, рваный верх."""
    rnd = random.Random(seed)
    top = []
    for i in range(w + 1):
        top.append((x + i + lean * h, y + (rnd.randint(0, 3) if jag_top else 0)))
    poly = top + [(x + w, y + h), (x, y + h)]
    lay = blank(cv)
    lay.polygon(poly, m['b'])
    lay.shade(m['l'], m['d'], m['b'])
    grain_v(lay, x + 2, y + 3, max(1, w - 3), h - 4, m['d'], seed, step=3)
    put(cv, lay, m['o'])


def wheel(cv: Canvas, cx: float, cy: float, r: float, m: Dict[str, str] = IRON, spokes: bool = True, out: str = 'ink2') -> None:
    """Колесо вагонетки (вид сбоку): обод с бликом сверху-слева, ступица, спицы."""
    lay = blank(cv)
    lay.ellipse(cx, cy, r, r, m['d'])
    lay.ellipse(cx, cy, r - 1.4, r - 1.4, m['s'])
    for y in range(lay.h):
        for x in range(lay.w):
            p = lay.get(x, y)
            if p[3] and p[:3] == PALETTE[m['d']]:
                u, v = x + 0.5 - cx, y + 0.5 - cy
                if u + v < -r * 0.5:
                    lay.set(x, y, m['h'])
                elif u + v < 0:
                    lay.set(x, y, m['l'])
    if spokes and r >= 4:
        for a in (0.4, 0.4 + math.pi / 2, 0.4 + math.pi, 0.4 + 3 * math.pi / 2):
            for k in range(1, int(r - 1)):
                lay.set(int(cx + math.cos(a) * k), int(cy + math.sin(a) * k), m['b'])
    lay.set(int(cx), int(cy), m['hh'])
    lay.set(int(cx) - 1, int(cy), m['b'])
    put(cv, lay, out)


def rust_spots(cv: Canvas, seed: int, density: float = 0.06, region=None, ramps=('rust', 'stone')) -> None:
    """Пятна ржавчины: редкие гнёзда 2–4 px (тёмная медь с тёмным краем) только по металлу (рампы rust/stone).
    density — доля площади металла, на которую приходится одно гнездо на ~40 px (не шум)."""
    rnd = random.Random(seed)
    x0, y0, w, h = region or (0, 0, cv.w, cv.h)
    metal = []
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            p = cv.get(x, y)
            if not p[3]:
                continue
            nm = NAME_OF.get(p[:3], '')
            r, i = _split(nm) if nm else ('', 0)
            if r in ramps and 2 <= i <= 6:
                metal.append((x, y))
    n = int(len(metal) * density / 6)
    ok = set(metal)
    for _ in range(n):
        x, y = rnd.choice(metal)
        cells = [(x, y), (x + 1, y)] + ([(x, y + 1)] if rnd.random() < 0.6 else []) + ([(x - 1, y + 1)] if rnd.random() < 0.3 else [])
        for k, q in enumerate(cells):
            if q in ok:
                cv.set(q[0], q[1], 'fur_ginger2' if k == 0 and rnd.random() < 0.5 else 'fur_ginger1')
        if (x, y + 2) in ok and rnd.random() < 0.5:
            cv.set(x, y + 2, 'rust3')


def streak(cv: Canvas, x: int, y: int, n: int) -> None:
    """Потёк ржавчины вниз от заклёпки."""
    for k in range(n):
        if cv.opaque(x, y + k):
            cv.set(x, y + k, 'fur_ginger1' if k < n - 1 else 'rust3')


def rope_post(cv: Canvas, x: int, y: int, w: int, h: int, seed: int = 0) -> None:
    """Столбик, обмотанный сизалем: косые витки, светлые слева, тени справа; местами разлохмачено."""
    lay = blank(cv)
    lay.rect(x, y, w, h, SISAL['b'])
    for yy in range(y, y + h):
        for xx in range(x, x + w):
            ph = (yy - y) * 2 - (xx - x)
            c = SISAL['l'] if ph % 4 in (0, 1) else SISAL['b']
            if ph % 4 == 3:
                c = SISAL['d']
            if xx == x:
                c = SISAL['h'] if ph % 4 != 3 else SISAL['l']
            elif xx >= x + w - 2:
                c = SISAL['d'] if ph % 4 != 3 else SISAL['s']
            lay.set(xx, yy, c)
    rnd = random.Random(seed)
    for _ in range(max(1, h // 10)):
        fy = y + rnd.randrange(2, max(3, h - 2))
        lay.set(x - 1, fy, SISAL['l'])
        lay.set(x + w, fy + 1, SISAL['d'])
    put(cv, lay, SISAL['o'])


def yarn_ball(cv: Canvas, cx: float, cy: float, r: float, col: str, seed: int = 0) -> None:
    """Клубок пряжи: шар с дугами нитей, блик сверху-слева."""
    o, s, b, l, h = YARN[col]
    lay = blank(cv)
    lay.ellipse(cx, cy, r, r, b)
    lay.shade(l, s, b)
    rnd = random.Random(seed)
    for k in range(3):
        a0 = rnd.uniform(0, math.pi)
        rr = r * rnd.uniform(0.45, 0.85)
        oy = rnd.uniform(-r * 0.3, r * 0.3)
        for i in range(10):
            a = a0 + i * 0.28
            px, py = cx + math.cos(a) * rr, cy + oy + math.sin(a) * rr * 0.55
            if lay.opaque(int(px), int(py)):
                lay.set(int(px), int(py), s if py > cy else l)
    lay.set(int(cx - r * 0.45), int(cy - r * 0.5), h)
    put(cv, lay, o)


def thread(cv: Canvas, pts: Sequence[Tuple[float, float]], c: str) -> None:
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        cv.line(int(ax), int(ay), int(bx), int(by), c)


def crack_line(cv: Canvas, x: int, y: int, steps: int, seed: int, dirx: int = 0) -> None:
    """Трещина: ломаная вниз тёмным тоном своего материала, светлая кромка справа."""
    rnd = random.Random(seed)
    for _ in range(steps):
        p = cv.get(x, y)
        if not p[3]:
            break
        cv.set(x, y, tone_of(p, 0, absolute=True))
        q = cv.get(x + 1, y)
        if q[3] and rnd.random() < 0.5:
            cv.set(x + 1, y, tone_of(q, 1))
        y += 1
        x += rnd.choice((-1, 0, 0, 1)) + (dirx if rnd.random() < 0.4 else 0)


# ───────────────────────── повреждение мусора ─────────────────────────
def wreck(base: Canvas, level: int, seed: int) -> Canvas:
    """Стадия разрушения обломков: 1 — отбиты куски по силуэту, трещины, щепки у подножия; 2 — верх снесён
    неровной линией, остаётся огрызок, больше осколков. Цвета краёв и осколков берутся из рамп самого спрайта."""
    cv = base.copy()
    rnd = random.Random(seed)
    W, H = cv.w, cv.h
    opq = {(x, y) for y in range(H) for x in range(W) if cv.opaque(x, y)}
    if not opq:
        return cv
    xs = sorted({x for x, _ in opq})
    top = min(y for _, y in opq)
    bot = max(y for _, y in opq)
    tops = {x: min(y for xx, y in opq if xx == x) for x in xs}
    x_lo, x_hi = xs[0], xs[-1]
    keep_y = bot - 2
    removed = {}

    def kill(x, y):
        if (x, y) in opq and (x, y) not in removed and y < keep_y:
            removed[(x, y)] = cv.get(x, y)
            cv.set(x, y, None)

    def carve(cx, cy, r):
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x + 0.5 - cx) ** 2 + ((y + 0.5 - cy) * 1.15) ** 2 <= r * r:
                    kill(x, y)

    span = [x for x in xs if x_lo + 2 <= x <= x_hi - 2] or xs
    if level == 1:
        for k in range(3):
            x = rnd.choice(span)
            r = rnd.uniform(2.4, 3.6) * max(1.0, (x_hi - x_lo) / 28.0) ** 0.5
            carve(x + 0.5, tops[x] + r * 0.45, r)
        side = x_lo if rnd.random() < 0.5 else x_hi
        mid = (top + bot) // 2
        carve(side + 0.5, mid, 3.0)
    else:
        h = bot - top
        cut = top + h * 0.58
        off = 0.0
        for x in range(x_lo, x_hi + 1):
            off = max(-3.0, min(3.0, off + rnd.uniform(-1.4, 1.4)))
            c = cut + off
            for y in range(top, int(c)):
                kill(x, y)
        for k in range(2):
            x = rnd.choice(span)
            col = [y for (xx, y) in opq if xx == x and (x, y) not in removed]
            if col:
                carve(x + 0.5, min(col) + 1, rnd.uniform(2.0, 3.0))
    # отвалившиеся островки (меньше 8 px) — тоже убрать
    rest = {(x, y) for (x, y) in opq if (x, y) not in removed}
    seen = set()
    for p in sorted(rest):
        if p in seen:
            continue
        comp, stack = [], [p]
        seen.add(p)
        while stack:
            q = stack.pop()
            comp.append(q)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (q[0] + dx, q[1] + dy)
                if n in rest and n not in seen:
                    seen.add(n)
                    stack.append(n)
        if len(comp) < 8:
            for q in comp:
                removed[q] = cv.get(*q)
                cv.set(q[0], q[1], None)
    # новый край: контур тёмным тоном своей рампы, под отбитым верхом — светлый скол
    edge, lit = set(), set()
    for (x, y) in removed:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            if cv.opaque(*q) and q not in removed:
                edge.add(q)
                if dy == 1:
                    lit.add((q[0], q[1] + 1))
    colors = {q: cv.get(*q) for q in edge | lit}
    for q in lit:
        if q not in edge and cv.opaque(*q):
            cv.set(q[0], q[1], tone_of(colors[q], 2))
    for q in edge:
        cv.set(q[0], q[1], tone_of(colors[q], 0, absolute=True))
    # трещины от края вглубь
    edges = sorted(edge)
    for k in range(2 if level == 1 else 3):
        if not edges:
            break
        x, y = rnd.choice(edges)
        crack_line(cv, x, y + 1, rnd.randint(3, 6), seed + 31 * k, rnd.choice((-1, 1)))
    # осколки у подножия: цвета отбитых пикселей
    pool = [c for c in removed.values() if c[3]]
    if pool:
        n = 3 if level == 1 else 5
        tries = 0
        while n and tries < 200:
            tries += 1
            w, h = rnd.choice(((2, 1), (3, 1), (2, 2), (3, 2)))
            x = rnd.randint(max(1, x_lo - 4), min(W - w - 2, x_hi + 3))
            y = rnd.randint(max(1, bot - 6), min(H - h - 2, bot))
            area = [(x + i, y + j) for i in range(-1, w + 1) for j in range(-1, h + 1)]
            if any(cv.opaque(*a) for a in area):
                continue
            c = rnd.choice(pool)
            cv.rect(x, y, w, h, tone_of(c, 0))
            cv.hline(x, x + w - 1, y, tone_of(c, 1))
            cv.set(x + w - 1, y + h - 1, tone_of(c, -1))
            for i in range(-1, w + 1):
                cv.set(x + i, y + h, tone_of(c, 0, absolute=True))
            cv.vline(x - 1, y, y + h - 1, tone_of(c, 0, absolute=True))
            cv.vline(x + w, y, y + h - 1, tone_of(c, 0, absolute=True))
            cv.hline(x, x + w - 1, y - 1, tone_of(c, 0, absolute=True))
            n -= 1
    return cv


# ═════════════════════════ A. ПОСТОЯННЫЕ ПРЕПЯТСТВИЯ ═════════════════════════
def rails_h(cv: Canvas, x0: int, x1: int, yf: int, yn: int, sleepers: Sequence[int], bent: Optional[int] = None,
            snapped: Sequence[int] = ()) -> None:
    """Короткий отрезок рельсов поперёк экрана: шпалы, дальний и ближний рельс (железо с ржавчиной).
    bent — x, где ближний рельс обломан и загнут вверх; snapped — шпалы, сломанные пополам."""
    for sx in sleepers:
        lay = blank(cv)
        if sx in snapped:
            lay.rect(sx, yf - 1, 4, 3, DWOOD['b'])
            lay.rect(sx + 1, yf + 3, 4, yn - yf, DWOOD['b'])
        else:
            lay.rect(sx, yf - 1, 4, yn - yf + 3, DWOOD['b'])
        lay.shade(DWOOD['l'], DWOOD['d'], DWOOD['b'])
        put(cv, lay, 'wood0')
    for yy, xe in ((yf, x1), (yn, bent if bent is not None else x1)):
        lay = blank(cv)
        lay.hline(x0, xe, yy, 'stone5')
        lay.hline(x0, xe, yy + 1, 'rust3')
        if yy == yn and bent is not None:
            for k in range(1, 5):
                lay.set(xe + k, yy - k // 2, 'stone5')
                lay.set(xe + k, yy - k // 2 + 1, 'rust3')
        put(cv, lay, 'ink2')
    rust_spots(cv, 7 + x0, 0.12, (x0, yf, x1 - x0 + 1, yn - yf + 2), ramps=('rust',))


def cart(cv: Canvas, ox: float, oy: float, ang: float = 0.0, wheels=(True, True), seed: int = 0, load: str = '') -> None:
    """Ржавая вагонетка в виде 3/4, нарисованная многоугольниками в системе (ox, oy) с поворотом ang:
    верх кузова — проём (видна внутренняя задняя стенка), передний борт сужается книзу, рёбра и заклёпки, колёса."""
    ca, sa = math.cos(ang), math.sin(ang)

    def P(x, y):
        return (ox + x * ca - y * sa, oy + x * sa + y * ca)

    def Q(pts):
        return [P(*p) for p in pts]

    m = RUST
    lay = blank(cv)
    lay.polygon(Q([(-19, -6), (19, -6), (19, -2), (-19, -2)]), 'ink3')                # рама
    lay.polygon(Q([(-23, -18), (23, -18), (19.5, -5), (-19.5, -5)]), m['b'])           # передний борт
    lay.polygon(Q([(-23, -27), (23, -27), (23, -18), (-23, -18)]), m['l'])             # кромка-обод
    lay.polygon(Q([(-20.5, -25.5), (20.5, -25.5), (20.5, -19.5), (-20.5, -19.5)]), m['k'])   # проём
    lay.polygon(Q([(-20.5, -25.5), (20.5, -25.5), (20.5, -23), (-20.5, -23)]), m['d'])  # задняя стенка изнутри
    for y in range(lay.h):
        for x in range(lay.w):
            p = lay.get(x, y)
            if not p[3]:
                continue
            # локальные координаты пикселя
            dx, dy = x + 0.5 - ox, y + 0.5 - oy
            lx, ly = dx * ca + dy * sa, -dx * sa + dy * ca
            if p[:3] == PALETTE[m['b']]:
                if lx < -17:
                    lay.set(x, y, m['l'])
                elif lx > 16:
                    lay.set(x, y, m['d'])
                elif ly > -8:
                    lay.set(x, y, m['d'])
                elif ly < -16.5:
                    lay.set(x, y, m['s'])
            elif p[:3] == PALETTE[m['l']]:
                if ly < -26 or lx < -22:
                    lay.set(x, y, m['hh'])
                elif lx > 21.5 or ly > -18.8:
                    lay.set(x, y, m['b'])
    # рёбра жёсткости и пояс
    for rx in (-13, 0, 13):
        a, b = P(rx, -17), P(rx * 0.9, -6)
        lay.line(int(a[0]), int(a[1]), int(b[0]), int(b[1]), m['s'])
        a, b = P(rx - 1, -17), P(rx * 0.9 - 1, -6)
        lay.line(int(a[0]), int(a[1]), int(b[0]), int(b[1]), m['h'])
    a, b = P(-21, -12), P(21, -12)
    lay.line(int(a[0]), int(a[1]), int(b[0]), int(b[1]), m['s'])
    # медные заклёпки по ободу
    for rx in (-20, -8, 6, 18):
        px, py = P(rx, -22 + 0.5 + 3.5)
        px, py = P(rx, -18.5)
        lay.set(int(px), int(py), 'fur_ginger3')
        streak(lay, int(px), int(py) + 2, 3)
    put(cv, lay, m['o'])
    rust_spots(cv, seed, 0.07, ramps=('rust',))
    for i, on in enumerate(wheels):
        wx = -11 if i == 0 else 11
        px, py = P(wx, -1)
        if on:
            wheel(cv, px, py, 4.5, IRON)
        else:
            cv.rect(int(px) - 1, int(py) - 1, 3, 2, 'stone2')
            cv.set(int(px) - 1, int(py) - 1, 'stone5')
            cv.set(int(px) + 2, int(py), 'ink2')


def minecart(v: int) -> Canvas:
    cv = Canvas(64, 48)
    if v == 0:
        # Пустая, без правого колеса: осела вправо на обломке рельса.
        rails_h(cv, 3, 50, 40, 44, (6, 16, 26, 36, 45), bent=40, snapped=(45,))
        cart(cv, 30, 42, ang=0.07, wheels=(True, False), seed=11)
        # колесо отвалилось — у правого борта подложен камень
        pile(cv, [G(51, 42.5, 4, 3, 931, cap=1)], RUBBLE_NAVY)
    elif v == 1:
        rails_h(cv, 2, 61, 40, 44, (4, 14, 24, 34, 44, 54), snapped=(54,))
        # груз: плиты, гнутый рельс, шестерня, рукоять кирки
        load = blank(cv)
        load.polygon([(12, 20), (20, 13), (27, 16), (24, 21)], 'stone4')
        load.polygon([(26, 20), (31, 11), (38, 12), (38, 20)], 'rust5')
        load.polygon([(37, 19), (42, 14), (50, 16), (50, 21)], 'stone3')
        for pts, c in ((((12, 20), (20, 13), (27, 16), (24, 21)), 'stone4'), (((26, 20), (31, 11), (38, 12), (38, 20)), 'rust5'),
                       (((37, 19), (42, 14), (50, 16), (50, 21)), 'stone3')):
            pass
        load.shade('stone6', 'stone2', 'stone4')
        load.shade('rust7', 'rust3', 'rust5')
        load.shade('stone5', 'stone1', 'stone3')
        put(cv, load, 'ink2')
        cv.ellipse(45, 15, 3.5, 3.5, 'fur_ginger2')
        for a in range(8):
            cv.set(int(45 + math.cos(a * 0.785) * 4.2), int(15 + math.sin(a * 0.785) * 4.2), 'fur_ginger1')
        cv.set(44, 14, 'fur_ginger4')
        cv.set(45, 15, 'fur_ginger0')
        # рукоять кирки торчит влево-вверх, железная головка
        brace(cv, 13, 6, 30, 19, 2, WOOD)
        lay = blank(cv)
        lay.polygon([(8, 9), (12, 3), (15, 4), (13, 8), (17, 10), (16, 12)], 'stone4')
        lay.shade('stone6', 'stone2', 'stone4')
        put(cv, lay, 'stone0')
        cart(cv, 31, 42, 0.0, (True, True), seed=12)
        # передний край груза поверх обода
        cv.hline(16, 22, 17, 'stone5')
    else:
        # Опрокинута: правый конец задран на валун, груз рассыпан влево.
        pile(cv, [G(53, 33, 9, 9, 941, n=9, facets=3, fseed=4, cap=4, glint=True)], RUBBLE_NAVY)
        cart(cv, 27, 44, ang=-0.36, wheels=(True, True), seed=13)
        pile(cv, [G(57, 43, 5, 3.5, 942, cap=2), G(47, 44.5, 3.5, 2.5, 943, cap=1)], RUBBLE_NAVY)
        # рассыпанный хлам
        board(cv, 2, 42, 12, 40, 3, DWOOD, split1=True, seed=5)
        lay = blank(cv)
        lay.polygon([(4, 36), (11, 34), (12, 38), (6, 39)], 'rust5')
        lay.shade('rust7', 'rust3', 'rust5')
        put(cv, lay, 'rust0')
        pebble(cv, 14, 43, 3, 2, NAVY_PEB)
    return cv


def winch() -> Canvas:
    """Ручная лебёдка: барабан с тросом между двумя брусьями на каменном подпятнике, рукоять справа."""
    cv = Canvas(32, 44)
    slab(cv, 1, 35, 30, 3, 5, STONE, seed=3)
    # стойки — толстый брус, железные полосы
    for x in (3, 22):
        vbeam(cv, x, 10, 7, 27, WOOD, seed=x)
        band(cv, x, 16, 7, (1, 5))
        band(cv, x, 30, 7, (1, 5))
    hbeam(cv, 1, 7, 30, 5, DWOOD, seed=2)
    bolt(cv, 5, 9)
    bolt(cv, 25, 9)
    # барабан с тросом
    lay = blank(cv)
    lay.rect(10, 17, 12, 13, 'stone2')
    for y in range(17, 30):
        c = 'ink3' if y % 2 else 'stone2'
        lay.hline(10, 21, y, c)
        lay.set(10, y, 'stone4' if y % 2 == 0 else 'stone2')
        lay.set(11, y, 'stone3' if y % 2 == 0 else 'ink3')
    lay.hline(10, 21, 17, 'stone4')
    put(cv, lay, 'ink1')
    # щёки барабана — медь
    for x in (9, 22):
        cv.vline(x, 15, 31, 'fur_ginger2')
    cv.vline(9, 15, 22, 'fur_ginger3')
    cv.vline(22, 24, 31, 'fur_ginger1')
    # рукоять: железное плечо и деревянная ручка
    cv.rect(29, 21, 2, 2, 'stone5')
    cv.line(30, 22, 30, 29, 'stone3')
    cv.set(31, 22, 'stone1')
    cv.rect(29, 29, 3, 4, 'wood5')
    cv.vline(31, 29, 32, 'wood3')
    cv.hline(29, 31, 33, 'wood1')
    cv.hline(29, 31, 28, 'wood1')
    # трос свисает и лежит кольцом у подпятника
    cv.vline(15, 31, 35, 'ink3')
    cv.vline(16, 31, 34, 'stone3')
    for px, py, c in ((14, 36, 'ink3'), (13, 36, 'stone3'), (17, 36, 'ink3'), (18, 35, 'stone3')):
        cv.set(px, py, c)
    rust_spots(cv, 21, 0.05, ramps=('stone',))
    return cv


def drill() -> Canvas:
    """Бурильная установка на салазках: моторный кожух слева, решётчатая мачта бура справа, бур уходит в грунт."""
    cv = Canvas(64, 52)
    # салазки: два бруса с загнутым носом, железная оковка
    for y in (44, 47):
        lay = blank(cv)
        lay.polygon([(3, y), (58, y), (61, y - 3), (62, y - 2), (59, y + 3), (3, y + 3)], 'wood4')
        lay.shade('wood5', 'wood2', 'wood4')
        put(cv, lay, 'wood0')
        cv.hline(52, 58, y + 2, 'stone4')
    # палуба
    box(cv, 5, 38, 52, 3, 4, RUST, seed=4)
    # мачта: две стойки и раскосы
    mx0, mx1 = 40, 50
    for x in (mx0, mx1):
        lay = blank(cv)
        lay.rect(x, 3, 3, 36, 'rust5')
        lay.vline(x, 3, 38, 'rust6')
        lay.vline(x + 2, 3, 38, 'rust3')
        put(cv, lay, 'rust0')
    for y in range(8, 36, 7):
        cv.line(mx0 + 3, y, mx1 - 1, y + 6, 'rust4')
        cv.line(mx0 + 3, y + 1, mx1 - 1, y + 7, 'rust2')
        cv.hline(mx0 + 3, mx1 - 1, y, 'rust3')
    # шкив на макушке
    wheel(cv, 46, 3, 3.5, COPPER, spokes=False, out='fur_ginger0')
    hbeam(cv, 38, 5, 17, 3, RUST)
    # трос шкива к буру
    cv.vline(46, 8, 30, 'ink3')
    # головка бура и сам бур
    box(cv, 42, 28, 8, 2, 5, IRON)
    lay = blank(cv)
    lay.polygon([(43, 36), (49, 36), (47, 50), (45, 50)], 'stone4')
    for y in range(36, 51):
        for x in range(42, 51):
            if lay.opaque(x, y) and (x + y) % 4 == 0:
                lay.set(x, y, 'stone2')
            elif lay.opaque(x, y) and (x + y) % 4 == 1:
                lay.set(x, y, 'stone6')
    put(cv, lay, 'stone0')
    # моторный кожух: ящик с решёткой, медная труба
    box(cv, 7, 20, 28, 5, 14, RUST, seed=9)
    for y in range(28, 36, 2):
        cv.hline(11, 22, y, 'rust1')
        cv.hline(11, 22, y + 1, 'rust5')
    for x in (9, 32):
        for y in (26, 36):
            cv.set(x, y, 'fur_ginger3')
    # плашка с номером-лапкой
    cv.rect(25, 28, 7, 6, 'cream1')
    cv.frame(25, 28, 7, 6, 'cream0')
    paw_small(cv, 26, 29, 'red3')
    # медная выхлопная труба
    lay = blank(cv)
    lay.rect(12, 9, 3, 12, 'fur_ginger2')
    lay.vline(12, 9, 20, 'fur_ginger3')
    lay.vline(14, 9, 20, 'fur_ginger1')
    lay.rect(11, 8, 5, 2, 'fur_ginger3')
    put(cv, lay, 'fur_ginger0')
    # шланг от мотора к мачте
    thread(cv, [(35, 30), (38, 33), (41, 31)], 'ink3')
    rust_spots(cv, 33, 0.06, ramps=('rust',))
    for x, y in ((18, 21), (30, 22), (6, 39)):
        streak(cv, x, y, 3)
    return cv


def paw_small(cv: Canvas, x: int, y: int, c: str) -> None:
    """Лапка 5×4: три пальчика и подушечка."""
    for px, py in ((x, y), (x + 2, y), (x + 4, y), (x + 1, y + 2), (x + 2, y + 2), (x + 3, y + 2), (x + 2, y + 3), (x + 1, y + 3), (x + 3, y + 3)):
        cv.set(px, py, c)


def sealed_shaft(v: int) -> Canvas:
    """Заколоченный боковой штрек в лицевой грани скалы: тёмный проём, обвязка из бруса с железными полосами,
    доски на гвоздях с тёмными щелями; вариант 1 — красный крест краской и табличка «лапкам вход воспрещён»."""
    cv = Canvas(32, 32)
    # тёмный проём с аркой над верхняком
    lay = blank(cv)
    lay.rect(7, 6, 18, 26, 'ink0')
    lay.ellipse(16, 6, 9, 5, 'ink0')
    lay.hline(8, 23, 9, 'navy0')
    put(cv, lay, 'navy1')
    cv.ellipse(16, 4, 7, 3, 'ink0')
    # обвязка: стойки и верхняк
    vbeam(cv, 2, 6, 6, 26, DWOOD, seed=1)
    vbeam(cv, 24, 6, 6, 26, DWOOD, seed=2)
    hbeam(cv, 0, 4, 32, 5, WOOD, seed=3)
    for x in (2, 24):
        band(cv, x, 14, 6, (1, 4))
        band(cv, x, 24, 6, (1, 4))
    for x in (3, 27, 15):
        bolt(cv, x, 5, True)
    # доски поперёк с щелями: в щелях темнота штрека
    for y, dx in ((11, 0), (17, 1), (23, -1)):
        lay = blank(cv)
        lay.polygon([(7, y + (1 if dx < 0 else 0)), (25, y + (1 if dx > 0 else 0)), (25, y + 3 + (1 if dx > 0 else 0)),
                     (7, y + 3 + (1 if dx < 0 else 0))], 'wood4')
        lay.shade('wood6', 'wood2', 'wood4')
        grain_h(lay, 9, y + 1, 14, 2, 'wood3', y, step=2)
        put(cv, lay, 'wood0')
        for x in (8, 23):
            cv.set(x, y + 1, 'stone6')
    brace(cv, 8, 12, 24, 24, 2, DWOOD)
    if v == 1:
        for (ax, ay, bx, by) in ((9, 11, 22, 27), (22, 11, 9, 27)):
            n = max(abs(bx - ax), abs(by - ay))
            for i in range(n + 1):
                px = round(ax + (bx - ax) * i / n)
                py = round(ay + (by - ay) * i / n)
                for dx in (0, 1):
                    if NAME_OF.get(cv.get(px + dx, py)[:3], '').startswith('wood') and (px + py + dx) % 5:
                        cv.set(px + dx, py, 'red4' if dx == 0 else 'red3')
        # табличка на верхняке: перечёркнутая лапка
        lay = blank(cv)
        lay.rect(10, 0, 12, 8, 'cream2')
        lay.hline(10, 21, 0, 'cream3')
        lay.vline(21, 1, 7, 'cream1')
        put(cv, lay, 'wood1')
        paw_small(cv, 14, 2, 'ink3')
        cv.line(11, 1, 20, 6, 'red3')
        cv.line(12, 1, 21, 6, 'red2')
    else:
        cv.set(12, 21, 'ink1')
    slab(cv, 4, 29, 24, 1, 1, ROCK, seed=4, seam=9)
    return cv


def reel(v: int) -> Canvas:
    """Деревянная кабельная катушка с тёмным тросом: 0 — стоит на ребре (клинья), 1 — лежит, конец троса на полу."""
    cv = Canvas(32, 40)
    if v == 0:
        # дальняя щека и трос между щеками
        lay = blank(cv)
        lay.ellipse(19, 22, 12, 12, 'wood2')
        put(cv, lay, 'wood0')
        lay = blank(cv)
        lay.polygon([(8, 14), (22, 10), (27, 16), (14, 21)], 'ink3')
        for k in range(0, 14, 2):
            lay.line(9 + k, 13 - k // 4 + 0, 14 + k, 20 - k // 3, 'stone2')
        put(cv, lay, 'ink1')
        # ближняя щека: доски, железный обод, ступица
        lay = blank(cv)
        lay.ellipse(14.5, 25, 12.5, 12.5, 'wood4')
        for y in range(lay.h):
            for x in range(lay.w):
                if lay.opaque(x, y):
                    u, w_ = x + 0.5 - 14.5, y + 0.5 - 25
                    d = math.hypot(u, w_)
                    if d > 11:
                        lay.set(x, y, 'stone5' if u + w_ < -4 else ('stone3' if u + w_ < 6 else 'stone2'))
                    elif (x - 2) % 6 == 0:
                        lay.set(x, y, 'wood3')
                    elif u + w_ < -9:
                        lay.set(x, y, 'wood5')
                    elif u + w_ > 9:
                        lay.set(x, y, 'wood3')
        lay.ellipse(14.5, 25, 3.5, 3.5, 'stone3')
        lay.ellipse(14.5, 25, 1.5, 1.5, 'ink2')
        lay.set(13, 23, 'stone6')
        for a in range(4):
            lay.set(int(14.5 + math.cos(a * 1.57 + 0.4) * 7), int(25 + math.sin(a * 1.57 + 0.4) * 7), 'stone6')
        put(cv, lay, 'wood0')
        # клинья под катушкой
        for x0, flip in ((3, False), (22, True)):
            lay = blank(cv)
            pts = [(x0, 39), (x0 + 7, 39), (x0 + 7, 34)] if not flip else [(x0, 34), (x0, 39), (x0 + 7, 39)]
            lay.polygon(pts, 'wood4')
            lay.shade('wood6', 'wood2', 'wood4')
            put(cv, lay, 'wood0')
        bolt(cv, 9, 37)
        bolt(cv, 24, 37)
    else:
        # лежит: верхняя щека — эллипс, под ней намотка троса, нижняя щека выглядывает
        lay = blank(cv)
        lay.ellipse(16, 30, 14, 6.5, 'wood2')
        lay.rect(4, 17, 24, 13, 'ink3')
        for y in range(18, 31, 2):
            lay.hline(4, 27, y, 'stone2')
        lay.vline(4, 17, 30, 'stone3')
        lay.vline(27, 17, 30, 'ink2')
        put(cv, lay, 'wood0')
        cv.hline(3, 28, 31, 'wood3')
        cv.hline(5, 26, 33, 'wood1')
        lay = blank(cv)
        lay.ellipse(16, 15, 15, 7.5, 'wood5')
        for y in range(lay.h):
            for x in range(lay.w):
                if lay.opaque(x, y):
                    u, w_ = (x + 0.5 - 16) / 15, (y + 0.5 - 15) / 7.5
                    d = math.hypot(u, w_)
                    if d > 0.86:
                        lay.set(x, y, 'stone5' if w_ < 0 and u < 0.4 else 'stone3')
                    elif (x + 2 * y) % 7 == 0 and d < 0.8:
                        lay.set(x, y, 'wood4')
                    elif u + w_ < -0.6:
                        lay.set(x, y, 'wood6')
        lay.ellipse(16, 15, 3.5, 2, 'ink2')
        lay.hline(14, 17, 14, 'stone3')
        put(cv, lay, 'wood0')
        bolt(cv, 8, 12)
        bolt(cv, 23, 18)
        # конец троса уходит на пол с крюком
        lay = blank(cv)
        thread(lay, [(27, 26), (30, 30), (28, 34), (22, 37), (17, 38)], 'ink3')
        thread(lay, [(27, 25), (31, 30), (29, 34), (23, 36)], 'stone3')
        put(cv, lay, 'ink1')
        cv.rect(14, 37, 3, 2, 'stone5')
        cv.set(13, 37, 'stone3')
        cv.set(13, 36, 'stone5')
    return cv


def support(v: int) -> Canvas:
    """Усиленная крепь у стены: толстые стойки на каменных подпятниках, верхняк, укосины, полосы и болты.
    Лицевая грань стоек остаётся чистой — на неё крепятся трубы."""
    cv = Canvas(32, 56)
    if v == 0:
        # одна тяжёлая рама: две стойки, верхняк, угловые укосины
        for x in (1, 22):
            vbeam(cv, x, 8, 9, 42, WOOD, seed=x + 1)
        hbeam(cv, 0, 3, 32, 7, DWOOD, seed=4)
        brace(cv, 10, 10, 15, 15, 3, DWOOD)
        brace(cv, 17, 15, 22, 10, 3, DWOOD)
        for x in (1, 22):
            band(cv, x, 14, 9, (2, 6))
            band(cv, x, 40, 9, (2, 6))
        for x in (2, 13, 18, 28):
            bolt(cv, x, 5, True)
        for x in (0, 21):
            slab(cv, x, 48, 11, 2, 5, STONE, seed=x, seam=20)
    elif v == 1:
        # спаренная стойка с подкосом
        vbeam(cv, 4, 4, 8, 46, WOOD, seed=5)
        vbeam(cv, 12, 8, 7, 42, DWOOD, seed=6)
        for y in (10, 24, 38):
            band(cv, 4, y, 15, (2, 7, 12))
        brace(cv, 18, 20, 28, 46, 4, DWOOD)
        bolt(cv, 21, 25, True)
        slab(cv, 1, 48, 30, 2, 5, STONE, seed=7, seam=8, moss=True)
        hbeam(cv, 2, 0, 19, 5, DWOOD, seed=8)
        bolt(cv, 5, 2)
        bolt(cv, 16, 2)
    else:
        # рама с висящим инструментом: стойка, консоль-верхняк, подкос, на крюке лопата
        vbeam(cv, 3, 6, 10, 44, WOOD, seed=9)
        hbeam(cv, 1, 4, 30, 6, DWOOD, seed=10)
        brace(cv, 13, 22, 26, 10, 3, DWOOD)
        band(cv, 3, 16, 10, (2, 7))
        band(cv, 3, 36, 10, (2, 7))
        bolt(cv, 5, 6, True)
        bolt(cv, 27, 6, True)
        # крюк и лопата
        cv.vline(24, 10, 13, 'stone3')
        cv.set(25, 13, 'stone3')
        lay = blank(cv)
        lay.rect(23, 14, 2, 18, 'wood5')
        lay.vline(24, 14, 31, 'wood3')
        lay.polygon([(20, 32), (28, 32), (27, 40), (24, 42), (21, 40)], 'stone4')
        lay.shade('stone6', 'stone2', 'stone4')
        put(cv, lay, 'ink2')
        cv.set(23, 34, 'stone6')
        slab(cv, 1, 48, 15, 2, 5, STONE, seed=11, seam=7)
        # ведро у подпятника — железное, приклёпано
        box(cv, 19, 46, 9, 2, 6, IRON)
        cv.hline(20, 26, 50, 'fur_ginger2')
    return cv


def hopper(v: int) -> Canvas:
    """Тяжёлый стальной бункер: 0 — воронка на ногах с жёлобом; 1 — клёпаный бак с крышкой."""
    cv = Canvas(64, 60)
    if v == 0:
        # задние ноги
        for x in (16, 44):
            vbeam(cv, x, 20, 4, 30, IRON, cap=False)
        # крестовые связи
        for a, b in (((20, 32), (44, 48)), ((44, 32), (20, 48))):
            cv.line(a[0], a[1], b[0], b[1], 'stone1')
            cv.line(a[0], a[1] + 1, b[0], b[1] + 1, 'stone3')
        # воронка: обод, внутренняя стенка, конус
        lay = blank(cv)
        lay.polygon([(7, 12), (57, 12), (40, 34), (24, 34)], 'stone4')
        put(cv, lay, 'stone0')
        for y in range(12, 35):
            for x in range(0, 64):
                if cv.get(x, y)[:3] == PALETTE['stone4']:
                    t = (x - 7 - (y - 12) * 17 / 22) / max(1, (50 - (y - 12) * 34 / 22))
                    cv.set(x, y, 'stone5' if t < 0.18 else ('stone3' if t > 0.78 else 'stone4'))
        for y in (18, 26):
            for x in range(0, 64):
                if cv.get(x, y)[3] and cv.get(x, y)[:3] != PALETTE['stone0']:
                    cv.set(x, y, 'stone2')
                    if x % 5 == 0:
                        cv.set(x, y - 1, 'stone6')
        box(cv, 5, 6, 54, 6, 3, STONE)
        cv.rect(9, 8, 46, 3, 'ink2')
        cv.hline(9, 54, 8, 'stone1')
        # руда внутри — тёмная, без золота
        cv.hline(14, 30, 9, 'navy4')
        cv.hline(36, 48, 9, 'navy3')
        # предупредительная полоса по ободу
        for x in range(6, 59):
            if (x // 3) % 2 == 0:
                cv.set(x, 13, 'amber3')
                cv.set(x, 14, 'amber2')
            else:
                cv.set(x, 13, 'ink2')
                cv.set(x, 14, 'ink1')
        # жёлоб вправо-вниз
        lay = blank(cv)
        lay.polygon([(29, 33), (37, 33), (55, 42), (52, 46), (31, 38)], 'rust4')
        lay.shade('rust6', 'rust2', 'rust4')
        put(cv, lay, 'rust0')
        cv.line(33, 35, 52, 43, 'rust1')
        # передние ноги на подпятниках
        for x in (9, 50):
            vbeam(cv, x, 22, 5, 30, IRON, cap=False)
            cv.set(x + 1, 30, 'stone6')
            cv.set(x + 1, 44, 'stone6')
            slab(cv, x - 3, 51, 11, 2, 5, STONE, seed=x, seam=20)
        for x in (14, 25, 37, 48):
            cv.set(x, 21, 'stone6')
        rust_spots(cv, 41, 0.05, (0, 12, 64, 40))
        for x in (19, 41):
            streak(cv, x, 16, 4)
    else:
        # бак: клёпаные листы, крышка приоткрыта, деревянная лестница, лапка-трафарет
        slab(cv, 4, 50, 56, 3, 6, STONE, seed=12, seam=9, moss=True)
        lay = blank(cv)
        lay.rect(8, 16, 46, 35, 'rust4')
        put(cv, lay, 'rust0')
        for y in range(16, 51):
            cv.set(8, y, 'rust6')
            cv.set(9, y, 'rust5')
            cv.set(53, y, 'rust2')
            cv.set(52, y, 'rust3')
        for x in (23, 38):
            cv.vline(x, 16, 50, 'rust2')
            cv.vline(x + 1, 16, 50, 'rust5')
        for y in (27, 39):
            cv.hline(9, 52, y, 'rust2')
            cv.hline(9, 52, y + 1, 'rust5')
        for y in (18, 29, 41, 48):
            for x in range(11, 52, 4):
                if cv.get(x, y)[:3] != PALETTE['rust2']:
                    cv.set(x, y, 'rust7')
        cv.hline(9, 52, 49, 'rust3')
        # трафарет: лапка и полоса
        paw(cv, 27, 30, 'cream2')
        cv.set(29, 31, 'cream1')
        # крышка: толстая плита, задрана слева
        lay = blank(cv)
        lay.polygon([(6, 12), (56, 8), (57, 15), (6, 18)], 'stone4')
        lay.shade('stone6', 'stone2', 'stone4')
        put(cv, lay, 'stone0')
        cv.line(8, 15, 55, 11, 'stone3')
        for x in (12, 30, 50):
            cv.set(x, 13 - (x - 12) // 12, 'stone7')
        # петли
        cv.rect(14, 17, 4, 2, 'stone2')
        cv.rect(44, 15, 4, 2, 'stone2')
        # лестница справа
        for x in (55, 60):
            cv.vline(x, 18, 50, 'wood4')
            cv.vline(x + 1, 18, 50, 'wood2')
        for y in range(22, 50, 6):
            cv.hline(55, 61, y, 'wood5')
            cv.hline(56, 60, y + 1, 'wood1')
        for x in (54, 62):
            cv.vline(x, 18, 50, 'wood0')
        cv.hline(54, 62, 17, 'wood0')
        # кран-вентиль снизу
        cv.rect(14, 44, 4, 3, 'fur_ginger2')
        cv.set(14, 44, 'fur_ginger4')
        cv.frame(13, 43, 6, 5, 'fur_ginger0')
        rust_spots(cv, 51, 0.06, (8, 16, 46, 35), ramps=('rust',))
        for x in (13, 33, 47):
            streak(cv, x, 19, 5)
    return cv


def tower(v: int) -> Canvas:
    """Старая кошачья когтеточка на тяжёлом основании, укреплённая шахтным брусом."""
    cv = Canvas(32, 64)
    slab(cv, 0, 56, 32, 2, 6, STONE, seed=13 + v, seam=8)
    if v == 0:
        box(cv, 2, 48, 28, 3, 5, DWOOD, planks=0, seed=1)
        rope_post(cv, 5, 18, 6, 30, seed=1)
        rope_post(cv, 20, 8, 6, 40, seed=2)
        # подкосы из бруса
        brace(cv, 1, 44, 6, 32, 3, DWOOD)
        brace(cv, 25, 32, 30, 44, 3, DWOOD)
        bolt(cv, 4, 37)
        bolt(cv, 27, 37)
        # нижняя площадка с ковриком
        box(cv, 1, 26, 30, 4, 3, WOOD, seed=2)
        cv.rect(4, 27, 14, 2, 'red3')
        cv.hline(4, 17, 27, 'red4')
        # верхняя площадка-лежанка
        box(cv, 15, 3, 16, 4, 3, WOOD, seed=3)
        cv.rect(17, 4, 12, 2, 'purple3')
        cv.hline(17, 28, 4, 'purple4')
        # верхушка левого столбика
        box(cv, 3, 15, 10, 2, 2, WOOD)
        # клубок на нитке
        cv.vline(7, 33, 39, 'cream1')
        yarn_ball(cv, 7.5, 41.5, 2.5, 'red', 3)
        band(cv, 20, 22, 6, (2,))
    else:
        box(cv, 3, 47, 26, 3, 6, DWOOD, seed=4)
        rope_post(cv, 12, 6, 8, 41, seed=4)
        # домик-ниша
        box(cv, 2, 30, 14, 3, 10, WOOD, planks=0, seed=5)
        cv.ellipse(9, 38.5, 3, 3.5, 'ink1')
        cv.rect(6, 38, 7, 4, 'ink1')
        cv.set(7, 37, 'ink3')
        # площадки вправо и влево
        box(cv, 15, 20, 16, 3, 3, WOOD, seed=6)
        cv.rect(17, 21, 11, 1, 'blue3')
        box(cv, 6, 2, 20, 4, 3, WOOD, seed=7)
        cv.rect(9, 3, 14, 2, 'red3')
        cv.hline(9, 22, 3, 'red4')
        # крестовина из бруса на основании
        brace(cv, 18, 46, 30, 30, 3, DWOOD)
        brace(cv, 1, 46, 2, 44, 2, DWOOD)
        band(cv, 12, 14, 8, (2, 5))
        bolt(cv, 27, 33)
        # мышка на верёвочке
        cv.vline(26, 26, 31, 'cream1')
        cv.rect(25, 32, 3, 2, 'stone5')
        cv.set(28, 33, 'pink2')
        cv.set(25, 32, 'stone6')
    return cv


def statue_cat() -> Canvas:
    """Каменная сидящая кошка на постаменте: силуэт ушей и хвоста, мох на макушке и уступах."""
    cv = Canvas(32, 56)
    slab(cv, 2, 44, 28, 3, 9, ROCK, seed=21, seam=9, moss=True)
    # гравировка лапки на постаменте
    paw(cv, 12, 48, 'navy2')
    # кошка: туловище-груша, голова с ушами, хвост обвивает лапки
    lay = blank(cv)
    lay.ellipse(16, 34, 9.5, 10, 'stone4')
    lay.ellipse(16, 16, 7.5, 7, 'stone4')
    lay.polygon([(9, 13), (10, 4), (15, 10)], 'stone4')
    lay.polygon([(17, 10), (22, 4), (23, 13)], 'stone4')
    lay.rect(12, 21, 9, 6, 'stone4')
    lay.rect(9, 40, 15, 4, 'stone4')
    for y in range(lay.h):
        for x in range(lay.w):
            if lay.opaque(x, y):
                u = x - 16
                if u < -5 and y > 22:
                    lay.set(x, y, 'stone5')
                elif u > 5:
                    lay.set(x, y, 'stone3')
    lay.shade('stone6', 'stone2', 'stone4', True)
    put(cv, lay, 'stone0')
    # хвост
    lay = blank(cv)
    lay.polygon([(22, 42), (28, 40), (29, 36), (27, 34), (26, 38), (21, 40)], 'stone3')
    put(cv, lay, 'stone0')
    # мордочка: высеченные глаза, нос, линия лап
    cv.set(13, 16, 'stone1')
    cv.set(19, 16, 'stone1')
    cv.hline(12, 13, 15, 'stone2')
    cv.hline(19, 20, 15, 'stone2')
    cv.set(16, 18, 'stone2')
    cv.vline(16, 34, 42, 'stone2')
    cv.set(11, 7, 'stone2')
    cv.set(20, 7, 'stone2')
    cv.hline(12, 20, 25, 'stone2')
    # красный платок-бандана на шее: тёплый акцент
    lay = blank(cv)
    lay.polygon([(10, 21), (22, 21), (22, 24), (16, 30), (10, 24)], 'red3')
    lay.shade('red4', 'red2', 'red3')
    put(cv, lay, 'red1')
    cv.set(13, 23, 'red5')
    # трещина и мох
    crack_line(cv, 21, 31, 5, 3)
    for x, y in ((10, 9), (11, 10), (14, 10), (15, 10), (17, 9), (8, 30), (9, 29), (21, 6)):
        cv.set(x, y, 'green4' if (x + y) % 2 else 'green3')
    return cv


def statue_paw() -> Canvas:
    """Резной камень-лапка: подушечка и четыре пальчика из одного камня на низком основании."""
    cv = Canvas(32, 36)
    slab(cv, 3, 29, 26, 2, 5, ROCK, seed=23, seam=11)
    lay = blank(cv)
    lay.ellipse(16, 22, 9, 7, 'stone4')
    for cx, cy in ((7, 13), (12.5, 8), (19.5, 8), (25, 13)):
        lay.ellipse(cx, cy, 3.6, 4.2, 'stone4')
    lay.shade('stone6', 'stone2', 'stone4', True)
    for y in range(lay.h):
        for x in range(lay.w):
            p = lay.get(x, y)
            if p[3] and p[:3] == PALETTE['stone4'] and x > 20 and y > 20:
                lay.set(x, y, 'stone3')
    put(cv, lay, 'stone0')
    # торцевая толщина камня — видна снизу
    for x in range(8, 25):
        if cv.opaque(x, 29):
            cv.set(x, 29, 'stone2')
    cv.set(13, 18, 'stone6')
    for x, y in ((12, 5), (13, 5), (24, 10), (9, 17)):
        cv.set(x, y, 'green4')
    cv.set(12, 6, 'green3')
    return cv


# ═════════════════════════ B. РАЗРУШАЕМЫЕ ОБЛОМКИ ═════════════════════════
def planks(v: int) -> Canvas:
    cv = Canvas(32, 36)
    if v == 0:
        # кучка обломков досок вповалку
        board(cv, 3, 28, 22, 26, 4, DWOOD, split1=True, seed=1)
        board(cv, 9, 31, 29, 29, 4, WOOD, split0=True, seed=2, nail=0.7)
        board(cv, 5, 23, 20, 19, 3, WOOD, split0=True, split1=True, seed=3)
        board(cv, 14, 24, 27, 25, 3, DWOOD, split1=True, seed=4)
    elif v == 1:
        # две доски крест-накрест и третья с гвоздями
        board(cv, 3, 20, 26, 30, 4, DWOOD, split1=True, seed=5)
        board(cv, 5, 30, 27, 19, 4, WOOD, split0=True, seed=6, nail=0.3)
        board(cv, 18, 31, 30, 32, 3, WOOD, split0=True, seed=7)
        cv.set(14, 24, 'stone6')
        cv.set(14, 23, 'stone4')
    else:
        # расщеплённые доски, привалены к обломку
        board(cv, 2, 30, 24, 31, 4, DWOOD, split1=True, seed=8)
        upright_board(cv, 9, 16, 5, 14, WOOD, lean=0.25, seed=9)
        upright_board(cv, 15, 18, 5, 12, DWOOD, lean=-0.2, seed=10)
        board(cv, 18, 27, 30, 23, 3, WOOD, split0=True, seed=11)
        cv.set(12, 20, 'stone6')
    return cv


def crate_box(cv: Canvas, x: int, y: int, w: int, top: int, front: int, m=DWOOD, seed: int = 0, broken: str = '') -> None:
    """Ящик в обломках: доски крышки, филёнки, уголки; broken — 'corner' (выбит угол), 'panel' (проломлена доска)."""
    box(cv, x, y, w, top, front, m, planks=0, seed=seed)
    y2 = y + top
    for k in range(1, 3):
        yy = y + k * top // 3
        cv.hline(x + 1, x + w - 2, yy, m['b'])
    for yy in (y2 + front // 3, y2 + 2 * front // 3):
        cv.hline(x + 2, x + w - 3, yy, m['s'])
        cv.hline(x + 2, x + w - 3, yy + 1, m['l'])
    cv.vline(x + 2, y2 + 1, y2 + front - 2, m['s'])
    cv.vline(x + w - 3, y2 + 1, y2 + front - 2, m['s'])
    for xx in (x, x + w - 2):
        cv.rect(xx, y2 + front - 2, 2, 2, 'stone3')
        cv.set(xx, y2 + front - 2, 'stone5')
    rnd = random.Random(seed)
    if broken == 'corner':
        lay = cv.copy()
        for j in range(5):
            for i in range(6 - j):
                cv.set(x + w - 1 - i, y + j - 1, None)
        cv.rect(x + w - 6, y, 3, 2, 'ink1')
        cv.line(x + w - 7, y + 1, x + w - 2, y + top + 1, m['o'])
        cv.set(x + w - 6, y + top + 2, m['hh'])
    elif broken == 'panel':
        px = x + w // 2 - 3
        cv.rect(px, y2 + 2, 6, front - 4, 'ink1')
        cv.set(px, y2 + 2, m['o'])
        for i in range(0, 6, 2):
            cv.set(px + i, y2 + 2 + rnd.randint(1, 3), m['l'])
            cv.set(px + i + 1, y2 + front - 4 - rnd.randint(0, 2), m['d'])
        cv.hline(px - 1, px + 6, y2 + front // 3, m['l'])
    crack_line(cv, x + rnd.randint(4, w - 5), y2 + 1, front - 3, seed)


def crates(v: int) -> Canvas:
    if v == 0:
        cv = Canvas(32, 40)
        crate_box(cv, 6, 14, 20, 7, 15, DWOOD, seed=1, broken='corner')
        board(cv, 1, 36, 13, 37, 3, WOOD, split1=True, seed=2)
        board(cv, 21, 34, 30, 36, 2, DWOOD, split0=True, seed=3)
    elif v == 1:
        cv = Canvas(32, 40)
        crate_box(cv, 3, 21, 24, 6, 13, DWOOD, seed=4, broken='panel')
        crate_box(cv, 8, 9, 15, 5, 7, WOOD, seed=5)
        cv.rect(12, 11, 7, 1, 'cream1')
        board(cv, 22, 37, 30, 35, 2, WOOD, split1=True, seed=6)
    else:
        cv = Canvas(64, 44)
        crate_box(cv, 2, 20, 18, 6, 14, DWOOD, seed=7, broken='corner')
        crate_box(cv, 21, 14, 20, 7, 18, WOOD, seed=8, broken='panel')
        crate_box(cv, 43, 23, 18, 5, 12, DWOOD, seed=9)
        crate_box(cv, 46, 13, 11, 4, 6, WOOD, seed=10)
        board(cv, 8, 41, 22, 40, 3, WOOD, split1=True, seed=11)
        board(cv, 38, 40, 50, 42, 2, DWOOD, split0=True, seed=12)
        upright_board(cv, 40, 22, 4, 16, DWOOD, lean=-0.15, seed=13)
    return cv


def rubble(v: int) -> Canvas:
    """Щебень: плоская куча многих мелких колотых камней разных пород, без золота; внутри — обломок доски."""
    if v == 2:
        cv = Canvas(64, 40)
        pile(cv, [G(14, 27, 6, 4.5, 401, n=6), G(27, 25, 7, 5, 402, n=5, facets=2, fseed=3), G(40, 26, 6, 4, 403, n=6),
                  G(52, 28, 6, 4.5, 404, n=5)], RUBBLE_NAVY, base=36)
        board(cv, 30, 30, 50, 22, 3, DWOOD, split0=True, split1=True, seed=21)
        pile(cv, [G(8, 33, 5.5, 3.5, 405, n=6), G(19, 32, 6, 4, 406, n=5, facets=2, fseed=5), G(33, 33, 7, 4, 407, n=6),
                  G(46, 33, 5, 3.5, 408, n=5), G(57, 34, 5, 3, 409, n=6)], RUBBLE_STONE, base=37)
        pile(cv, [G(26, 36.5, 4, 2.2, 410, n=5), G(13, 37, 3.5, 2, 411, n=5), G(50, 37.5, 4, 2, 412, n=5)], SLATE_PAL, base=39)
        cv.rect(36, 19, 6, 4, 'stone4')
        for x, y, s in ((25, 23, 6), (44, 25, 4), (20, 30, 3)):
            crack_line(cv, x, y, s, x)
        return cv
    cv = Canvas(32, 36)
    if v == 0:
        pile(cv, [G(10, 22, 6, 4.5, 421, n=6), G(21, 21, 6.5, 5, 422, n=5, facets=2, fseed=2)], RUBBLE_NAVY, base=32)
        pile(cv, [G(6, 29, 5, 3.5, 423, n=5), G(16, 28, 6, 4, 424, n=6), G(26, 29, 5, 3.5, 425, n=5)], RUBBLE_STONE, base=33)
        pile(cv, [G(11, 33, 4, 2.2, 426, n=5), G(21, 33.5, 3.5, 2, 427, n=5)], SLATE_PAL, base=35)
        crack_line(cv, 20, 18, 4, 1)
        crack_line(cv, 16, 26, 3, 2)
    else:
        board(cv, 4, 20, 22, 27, 3, DWOOD, split1=True, seed=31)
        pile(cv, [G(23, 21, 6, 4.5, 431, n=5), G(8, 24, 5, 4, 432, n=6)], RUBBLE_STONE, base=32)
        pile(cv, [G(15, 29, 7, 4, 433, n=6, facets=2, fseed=7), G(26, 29.5, 4.5, 3, 434, n=5), G(5, 30, 4, 3, 435, n=5)], RUBBLE_NAVY, base=33)
        pile(cv, [G(19, 33.5, 3.5, 2, 436, n=5), G(9, 34, 3, 1.8, 437, n=5)], SLATE_PAL, base=35)
        crack_line(cv, 14, 27, 4, 4)
    return cv


def scrap(v: int) -> Canvas:
    if v == 0:
        cv = Canvas(32, 36)
        # гнутый рельс
        lay = blank(cv)
        thread(lay, [(2, 30), (12, 28), (20, 22), (29, 19)], 'stone5')
        thread(lay, [(2, 31), (12, 29), (20, 23), (29, 20)], 'rust3')
        thread(lay, [(2, 32), (12, 30), (20, 24), (29, 21)], 'rust2')
        put(cv, lay, 'ink2')
        # колесо лежит на боку: эллипс, спицы, ступица
        lay = blank(cv)
        lay.ellipse(15, 27, 10, 6, 'stone3')
        lay.ellipse(15, 27, 8, 4.4, 'ink3')
        lay.ellipse(15, 26.5, 2.2, 1.6, 'stone4')
        for a in range(6):
            ang = a * math.pi / 3 + 0.2
            for k in range(2, 8):
                lay.set(int(15 + math.cos(ang) * k), int(27 + math.sin(ang) * k * 0.55), 'stone3')
        for y in range(lay.h):
            for x in range(lay.w):
                p = lay.get(x, y)
                if p[3] and p[:3] == PALETTE['stone3'] and (x - 15) * 0.6 + (y - 27) < -2:
                    lay.set(x, y, 'stone5')
        put(cv, lay, 'ink2')
        # обод снизу — толщина колеса
        for x in range(7, 24):
            if cv.opaque(x, 33) and not cv.opaque(x, 34):
                cv.set(x, 34, 'rust1')
        cv.set(14, 25, 'stone6')
        rust_spots(cv, 61, 0.1)
    elif v == 1:
        cv = Canvas(32, 36)
        # плоские листы железа: острые углы, торец 1 px, ряды заклёпочных дыр
        STEEL = dict(o='ink2', s='stone2', d='stone4', b='stone5', l='stone6', h='stone7')
        RPL = dict(o='ink2', s='stone1', d='stone2', b='stone3', l='stone4', h='stone5')
        for pts, m in (([(8, 18), (22, 16), (23, 21), (9, 23)], RPL), ([(2, 25), (17, 22), (18, 27), (3, 30)], STEEL),
                       ([(14, 29), (28, 26), (29, 31), (15, 34)], RPL)):
            lay = blank(cv)
            lay.polygon([(x, y + 1) for x, y in pts], m['s'])
            lay.polygon(pts, m['b'])
            for y in range(lay.h):
                for x in range(lay.w):
                    if lay.get(x, y)[:3] == PALETTE[m['b']]:
                        if not lay.opaque(x, y - 1):
                            lay.set(x, y, m['h'])
                        elif lay.get(x, y + 1)[:3] == PALETTE[m['s']]:
                            lay.set(x, y, m['d'])
            put(cv, lay, m['o'])
        for x0, y0, x1 in ((10, 20, 20), (4, 27, 15), (16, 31, 26)):
            for x in range(x0, x1, 4):
                yy = y0 - (x - x0) // 6
                cv.set(x, yy, 'ink2')
        rust_spots(cv, 64, 0.35, ramps=('stone',))
        # болты и гайки россыпью
        for x, y in ((25, 18), (4, 33), (29, 33), (2, 21)):
            cv.rect(x, y, 2, 2, 'stone5')
            cv.set(x, y, 'stone7')
            cv.set(x + 2, y + 1, 'ink2')
            cv.hline(x, x + 1, y + 2, 'ink2')
        cv.ellipse(23, 33, 2, 1.5, 'fur_ginger3')
        cv.set(23, 33, 'ink2')
    else:
        cv = Canvas(64, 40)
        # куча рельсов: штанги крест-накрест, обломки шпал
        board(cv, 4, 34, 22, 36, 4, DWOOD, split1=True, seed=41)
        board(cv, 40, 33, 58, 31, 4, DWOOD, split0=True, seed=42)
        for (a, b, c) in (((3, 30), (40, 22), 0), ((14, 34), (60, 28), 1), ((8, 25), (44, 33), 2), ((22, 20), (58, 25), 3),
                          ((26, 36), (50, 36), 4)):
            lay = blank(cv)
            ax, ay = a
            bx, by = b
            thread(lay, [(ax, ay), (bx, by)], 'stone6')
            thread(lay, [(ax, ay + 1), (bx, by + 1)], 'rust4')
            thread(lay, [(ax, ay + 2), (bx, by + 2)], 'rust2')
            lay.rect(int(bx) - 1, int(by) - 1, 3, 4, 'rust3')
            put(cv, lay, 'ink2')
        lay = blank(cv)
        lay.polygon([(30, 19), (37, 16), (40, 22), (33, 24)], 'rust5')
        lay.shade('rust7', 'rust3', 'rust5')
        put(cv, lay, 'rust0')
        wheel(cv, 20, 25, 4, IRON)
        rust_spots(cv, 63, 0.1)
        for x, y in ((47, 37), (12, 37)):
            cv.rect(x, y, 2, 2, 'stone4')
            cv.set(x, y, 'stone6')
    return cv


def cardbox(cv: Canvas, x: int, y: int, w: int, top: int, front: int, sag: int = 0, seed: int = 0, stamp: bool = True,
            flap: str = '') -> None:
    """Картонная коробка (вид 3/4): светлая крышка с лентой, перед с пятном-«лапкой», смятая просадка sag."""
    lay = blank(cv)
    pts = [(x, y + sag), (x + w // 2, y + sag // 2 + 1), (x + w, y), (x + w, y + top + front), (x, y + top + front)]
    lay.polygon(pts, CARD['b'])
    put(cv, lay, CARD['o'])
    y2 = y + top
    for xx in range(x, x + w):
        f = (xx - x) / max(1, w)
        ty = int(y + sag * (1 - f) if f < 0.5 else y + sag * (1 - f))
        for yy in range(ty, y2):
            if cv.get(xx, yy)[:3] == PALETTE[CARD['b']]:
                cv.set(xx, yy, CARD['l'] if yy > ty else CARD['h'])
    cv.hline(x, x + w - 1, y2, CARD['d'])
    cv.vline(x + w - 1, y2, y2 + front - 1, CARD['d'])
    cv.vline(x, y2 + 1, y2 + front - 1, CARD['l'])
    cv.hline(x, x + w - 1, y2 + front - 1, CARD['s'])
    # лента
    cx = x + w // 2
    for yy in range(y, y2 + 3):
        if cv.opaque(cx, yy) and cv.get(cx, yy)[:3] != PALETTE[CARD['o']]:
            cv.set(cx, yy, 'cream2')
            if cv.get(cx + 1, yy)[:3] != PALETTE[CARD['o']]:
                cv.set(cx + 1, yy, 'cream1')
    if stamp:
        paw_small(cv, x + 3, y2 + 3, 'wood3')
    # заломы
    rnd = random.Random(seed)
    for _ in range(2):
        px = x + rnd.randint(3, w - 4)
        cv.line(px, y2 + 2, px + rnd.choice((-2, 2)), y2 + front - 2, CARD['d'])
    if flap == 'left':
        lay = blank(cv)
        lay.polygon([(x, y2 - 1), (x - 4, y2 - 6), (x + 6, y - 4), (x + 8, y + sag)], CARD['l'])
        lay.shade(CARD['h'], CARD['d'], CARD['l'])
        put(cv, lay, CARD['o'])


def cat_hole(cv: Canvas, x: int, y: int) -> None:
    """Дырка в форме кошачьей головы с ушами (тёмная), 7×6."""
    rows = ['o...o', 'oo.oo', 'ooooo', 'ooooo', '.ooo.']
    for j, r in enumerate(rows):
        for i, ch in enumerate(r):
            if ch == 'o':
                cv.set(x + i, y + j, 'ink1')
    cv.set(x + 1, y + 2, 'ink3')
    cv.hline(x, x + 4, y + 5, CARD['l'])


def cardboard(v: int) -> Canvas:
    cv = Canvas(32, 40)
    if v == 0:
        # смятая коробка на боку и сплющенный лист
        board(cv, 2, 35, 18, 37, 4, CARD, seed=51)
        cardbox(cv, 7, 18, 19, 6, 13, sag=4, seed=52)
    elif v == 1:
        # две коробки, верхняя съехала, у нижней — дыра «кошачья голова»
        cardbox(cv, 3, 21, 23, 6, 14, sag=2, seed=53, flap='left')
        cat_hole(cv, 16, 31)
        cardbox(cv, 12, 11, 14, 5, 8, sag=3, seed=54, stamp=False)
    else:
        # совсем сплющенные коробки стопкой и откинутый клапан
        board(cv, 3, 30, 28, 28, 6, CARD, seed=55)
        board(cv, 5, 24, 24, 23, 5, CARD, split1=True, seed=56)
        cardbox(cv, 17, 22, 13, 4, 9, sag=3, seed=57, stamp=False)
        paw_small(cv, 9, 31, 'wood3')
        paw_small(cv, 11, 25, 'wood4')
    return cv


def yarn(v: int) -> Canvas:
    cv = Canvas(32, 36)
    if v == 0:
        # спутанный моток верёвки: кольца одно на другом
        for cx, cy, rx, ry in ((13, 27, 10, 5), (18, 25, 9, 4.5), (15, 29, 8, 4)):
            lay = blank(cv)
            lay.ellipse(cx, cy, rx, ry, 'wood5')
            lay.ellipse(cx, cy, rx - 2.2, ry - 1.8, None)
            for y in range(lay.h):
                for x in range(lay.w):
                    if lay.opaque(x, y):
                        lay.set(x, y, 'cream1' if (x + y) % 3 == 0 else ('wood6' if y < cy else 'wood4'))
            put(cv, lay, 'wood1')
        thread(cv, [(24, 28), (28, 31), (30, 34)], 'wood5')
        thread(cv, [(4, 25), (2, 21), (5, 18)], 'wood5')
        cv.set(5, 17, 'cream2')
    elif v == 1:
        # гнездо из пряжи с клубками
        lay = blank(cv)
        lay.ellipse(16, 28, 13, 5.5, 'wood3')
        lay.ellipse(16, 27, 10, 3.5, 'wood1')
        put(cv, lay, 'wood0')
        for x in range(4, 29, 2):
            cv.set(x, 30 + (x % 3), 'wood5')
            cv.set(x + 1, 24 + (x % 2), 'wood4')
        yarn_ball(cv, 11, 23, 4.5, 'red', 1)
        yarn_ball(cv, 20, 22, 4, 'blue', 2)
        yarn_ball(cv, 16, 27, 3.5, 'purple', 3)
        thread(cv, [(24, 25), (27, 29), (24, 32), (29, 34)], 'blue4')
        thread(cv, [(7, 25), (3, 29), (6, 33)], 'red4')
    else:
        # спутанный ком разноцветной пряжи: петли разных цветов, один клубок укатился
        rnd = random.Random(7)
        for k in range(9):
            col = ('red', 'blue', 'purple')[k % 3]
            o, sd, b_, l, h = YARN[col]
            cx, cy = 13 + rnd.uniform(-6, 6), 27 + rnd.uniform(-3, 3)
            rx, ry = rnd.uniform(3, 6), rnd.uniform(2, 3.5)
            lay = blank(cv)
            lay.ellipse(cx, cy, rx, ry, b_)
            lay.ellipse(cx + 0.5, cy, rx - 1.3, ry - 1.2, None)
            lay.shade(l, sd, b_)
            put(cv, lay, o)
        yarn_ball(cv, 10, 22, 3.5, 'red', 4)
        yarn_ball(cv, 26, 31, 3, 'blue', 5)
        thread(cv, [(20, 29), (23, 33), (25, 33)], 'blue4')
        thread(cv, [(3, 30), (1, 33)], 'red4')
    return cv


def catfurn(v: int) -> Canvas:
    cv = Canvas(32, 44)
    if v == 0:
        # сломанная когтеточка-доска: две половинки, сизаль клочьями
        for x0, y0, x1, y1 in ((2, 30, 15, 25), (14, 36, 30, 31)):
            lay = blank(cv)
            lay.polygon([(x0, y0), (x1, y1), (x1, y1 + 6), (x0, y0 + 6)], SISAL['b'])
            for y in range(lay.h):
                for x in range(lay.w):
                    if lay.opaque(x, y):
                        lay.set(x, y, SISAL['l'] if (x + y * 2) % 4 < 2 else SISAL['d'])
            side = blank(cv)
            side.polygon([(x0, y0 + 6), (x1, y1 + 6), (x1, y1 + 8), (x0, y0 + 8)], 'wood3')
            side.blit(lay, 0, 0)
            put(cv, side, 'wood1')
        board(cv, 15, 24, 18, 36, 2, DWOOD, split0=True, split1=True, seed=61)
        upright_board(cv, 21, 20, 6, 12, CARD, lean=0.2, seed=62)
        for x, y in ((14, 29), (16, 31), (12, 28)):
            cv.set(x, y, SISAL['h'])
    elif v == 1:
        # развалившийся кошачий домик: крыша съехала, круглая дверка
        box(cv, 4, 23, 22, 4, 14, CARD, seed=63)
        cv.ellipse(12, 33, 3.5, 4, 'ink1')
        cv.rect(9, 33, 7, 4, 'ink1')
        cv.set(10, 31, 'ink3')
        lay = blank(cv)
        lay.polygon([(1, 22), (14, 12), (29, 18), (27, 24), (14, 20), (3, 26)], 'red3')
        lay.shade('red4', 'red2', 'red3')
        put(cv, lay, 'red0')
        cv.line(4, 22, 14, 15, 'red5')
        crack_line(cv, 22, 27, 9, 5)
        board(cv, 20, 39, 31, 41, 3, CARD, split0=True, seed=64)
        paw_small(cv, 18, 30, 'wood3')
    else:
        # опрокинутая лежанка, наружу лезет набивка
        lay = blank(cv)
        lay.ellipse(15, 31, 12, 7, 'pink2')
        lay.ellipse(17, 30, 7, 4, 'purple3')
        for y in range(lay.h):
            for x in range(lay.w):
                p = lay.get(x, y)
                if p[3] and p[:3] == PALETTE['pink2'] and (x - 15) + (y - 31) * 1.6 < -8:
                    lay.set(x, y, 'pink3')
                elif p[3] and p[:3] == PALETTE['pink2'] and (x - 15) + (y - 31) * 1.6 > 8:
                    lay.set(x, y, 'pink1')
        put(cv, lay, 'pink0')
        for cx, cy, r in ((23, 26, 3), (26, 30, 2.5), (20, 24, 2), (7, 37, 2.5), (28, 36, 2)):
            lay = blank(cv)
            lay.ellipse(cx, cy, r, r * 0.8, 'cream3')
            lay.shade('cream4', 'cream1', 'cream3')
            put(cv, lay, 'cream0')
        crack_line(cv, 10, 27, 6, 6)
    return cv


def sack_lump(cv: Canvas, cx: float, cy: float, rx: float, ry: float, seed: int = 0, tie: bool = True) -> None:
    """Обвисший мешок из мешковины: неровный ком, верёвка на горловине, заплатка."""
    lay = blank(cv)
    lay.ellipse(cx, cy, rx, ry, BURLAP['b'])
    lay.ellipse(cx - rx * 0.3, cy - ry * 0.5, rx * 0.7, ry * 0.6, BURLAP['b'])
    for y in range(lay.h):
        for x in range(lay.w):
            if lay.opaque(x, y):
                u, w_ = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
                if u + w_ < -0.9:
                    lay.set(x, y, BURLAP['l'])
                elif u + w_ > 0.7:
                    lay.set(x, y, BURLAP['d'])
                if (x * 3 + y * 5) % 11 == 0:
                    lay.set(x, y, BURLAP['d'])
    put(cv, lay, BURLAP['o'])
    if tie:
        tx, ty = int(cx + rx * 0.7), int(cy - ry * 0.4)
        cv.rect(tx, ty, 3, 3, BURLAP['l'])
        cv.hline(tx - 1, tx + 3, ty + 1, 'wood4')
        cv.set(tx + 4, ty + 2, 'wood4')
        cv.frame(tx - 1, ty - 1, 5, 5, BURLAP['o']) if False else None


def sacks(v: int) -> Canvas:
    cv = Canvas(32, 36)
    if v == 0:
        sack_lump(cv, 11, 26, 9, 6, 1)
        sack_lump(cv, 21, 29, 8, 5, 2)
        pebble(cv, 4, 31, 4, 3, NAVY_PEB)
        pebble(cv, 15, 20, 3, 2, NAVY_PEB)
        cv.rect(18, 26, 3, 3, 'cream0')
        cv.set(18, 26, 'wood5')
    elif v == 1:
        # спутанные сухие корни: тонкие пряди от узла-комля, отростки, без заливок
        lay = blank(cv)
        rnd = random.Random(9)
        for k in range(11):
            a0 = -math.pi * (0.05 + 0.9 * k / 10)
            x, y = 16.0, 28.0
            pts = [(x, y)]
            ln = rnd.uniform(9, 15)
            for i in range(5):
                a0 += rnd.uniform(-0.5, 0.5)
                x += math.cos(a0) * ln / 5 * 1.4
                y += math.sin(a0) * ln / 5 * 0.9 + 0.4
                pts.append((x, y))
            thread(lay, pts, 'wood4' if k % 2 else 'wood3')
            if k % 3 == 0:
                ex, ey = pts[3]
                thread(lay, [(ex, ey), (ex + rnd.choice((-3, 3)), ey + 2)], 'wood3')
        for y in range(lay.h - 1, 0, -1):
            for x in range(lay.w):
                if lay.opaque(x, y - 1) and not lay.opaque(x, y):
                    lay.set(x, y, 'wood1')
        lay.ellipse(16, 28, 4, 2.5, 'wood3')
        lay.hline(13, 18, 27, 'wood5')
        lay.set(14, 26, 'wood6')
        lay.outline('wood0') if False else None
        cv.blit(lay, 0, 0)
        cv.frame(12, 26, 9, 5, 'wood1') if False else None
        for x, y in ((6, 24), (25, 22), (10, 31), (22, 30)):
            cv.set(x, y, 'wood6')
        pebble(cv, 23, 31, 4, 3, NAVY_PEB)
    else:
        # старая солома гнездом
        lay = blank(cv)
        lay.ellipse(16, 28, 13, 5.5, 'wood5')
        rnd = random.Random(11)
        for k in range(36):
            x, y = rnd.uniform(4, 28), rnd.uniform(23, 33)
            dx = rnd.choice((-3, -2, 2, 3))
            lay.line(int(x), int(y), int(x + dx), int(y + rnd.choice((-1, 0, 1))), rnd.choice(('wood6', 'wood7', 'cream1', 'wood4')))
        for x in range(lay.w):
            for y in range(lay.h):
                if lay.opaque(x, y) and y > 31:
                    lay.set(x, y, 'wood3')
        put(cv, lay, 'wood1')
        for k in range(5):
            x = rnd.randint(4, 27)
            cv.line(x, 22, x + rnd.choice((-2, 2)), 19, 'wood6')
    return cv


# ───────── следы на полу после разрушения (32×32, плоско) ─────────
def rest(family: str) -> Canvas:
    cv = Canvas(32, 32)
    if family == 'planks':
        board(cv, 4, 22, 16, 21, 2, DWOOD, split1=True, seed=71, th=1)
        board(cv, 15, 27, 27, 25, 2, WOOD, split0=True, seed=72, th=1)
        for x, y in ((9, 27), (23, 19), (20, 22)):
            cv.set(x, y, 'wood5')
            cv.set(x + 1, y, 'wood2')
    elif family == 'crates':
        board(cv, 3, 20, 19, 21, 3, DWOOD, split1=True, seed=73, th=1)
        board(cv, 13, 26, 28, 24, 3, WOOD, split0=True, seed=74, th=1, nail=0.5)
        cv.rect(6, 26, 2, 2, 'stone3')
        cv.set(6, 26, 'stone5')
    elif family == 'rubble':
        pile(cv, [G(9, 22, 3.5, 2.2, 501, n=5), G(20, 25, 4, 2.5, 502, n=5), G(14, 28, 3, 1.8, 503, n=5)], RUBBLE_STONE, base=30)
        pebble(cv, 25, 19, 3, 2, NAVY_PEB)
        pebble(cv, 4, 27, 3, 2, NAVY_PEB)
    elif family == 'scrap':
        lay = blank(cv)
        lay.polygon([(5, 22), (13, 20), (14, 24), (7, 26)], 'rust4')
        lay.shade('rust6', 'rust2', 'rust4')
        put(cv, lay, 'rust0')
        thread(cv, [(16, 27), (27, 24)], 'stone5')
        thread(cv, [(16, 28), (27, 25)], 'rust2')
        for x, y in ((22, 19), (9, 29)):
            cv.rect(x, y, 2, 2, 'stone4')
            cv.set(x, y, 'stone6')
            cv.set(x + 1, y + 1, 'ink2')
    elif family == 'cardboard':
        board(cv, 4, 22, 22, 20, 6, CARD, seed=75, th=1)
        paw_small(cv, 9, 21, 'wood3')
        board(cv, 18, 28, 27, 27, 3, CARD, split0=True, seed=76, th=1)
    elif family == 'yarn':
        thread(cv, [(4, 24), (9, 21), (14, 25), (19, 22), (24, 26), (28, 23)], 'red3')
        thread(cv, [(6, 28), (12, 27), (17, 29), (25, 28)], 'blue4')
        yarn_ball(cv, 22, 20, 2.5, 'purple', 9)
    elif family == 'catfurn':
        board(cv, 4, 24, 15, 22, 4, SISAL, seed=77, th=1)
        lay = blank(cv)
        for cx, cy in ((21, 26), (24, 22)):
            lay.ellipse(cx, cy, 2.5, 1.8, 'cream3')
        lay.shade('cream4', 'cream1', 'cream3')
        put(cv, lay, 'cream0')
        board(cv, 9, 29, 20, 29, 2, CARD, split1=True, seed=78, th=1)
    elif family == 'sacks':
        lay = blank(cv)
        lay.polygon([(5, 24), (14, 21), (19, 25), (10, 28)], BURLAP['b'])
        lay.shade(BURLAP['l'], BURLAP['d'], BURLAP['b'])
        put(cv, lay, BURLAP['o'])
        for x, y, dx in ((18, 20, 3), (21, 27, -3), (24, 23, 3), (13, 29, 2)):
            cv.line(x, y, x + dx, y + 1, 'wood6')
    return cv


# ═════════════════════════ C. ДЕКАЛИ ПОЛА ═════════════════════════
def decal_pebbles(v: int) -> Canvas:
    cv = Canvas(32, 32)
    sets = [((6, 9, 3, 2), (20, 14, 4, 3), (13, 24, 3, 2)),
            ((24, 6, 3, 2), (8, 18, 3, 2), (11, 20, 2, 2), (22, 26, 4, 3)),
            ((15, 11, 4, 3), (5, 27, 3, 2), (26, 20, 2, 2))]
    for x, y, w, h in sets[v]:
        pebble(cv, x, y, w, h, SLATE_PEB)
    return cv


SLATE_PEB = dict(crev='slate1', shade='slate3', body='slate5', mid='slate5', top='slate6', rim='slate7', out='slate1')


def decal_paint(v: int) -> Canvas:
    """Выцветшая краска на плите: стрелка или крест — упорядоченный дизеринг, не сплошная заливка."""
    cv = Canvas(32, 32)
    mask = Canvas(32, 32)
    if v == 0:
        mask.rect(13, 13, 6, 13, 'white')
        mask.polygon([(8, 14), (16, 5), (24, 14)], 'white')
        col = 'amber3'
    else:
        for i in range(-1, 2):
            mask.line(8 + i, 8, 23 + i, 23, 'white')
            mask.line(23 + i, 8, 8 + i, 23, 'white')
        col = 'red3'
    bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
    for y in range(32):
        for x in range(32):
            if mask.opaque(x, y) and (bayer[y % 4][x % 4] + 0.5) / 16 < 0.62:
                cv.set(x, y, col)
    return cv


def decal_paws() -> Canvas:
    cv = Canvas(32, 32)
    for x, y in ((5, 23), (12, 17), (17, 11), (24, 5)):
        for px, py in ((0, 0), (2, -1), (4, 0)):
            cv.set(x + px, y + py, 'slate6')
        cv.rect(x + 1, y + 2, 3, 2, 'slate6')
        cv.set(x + 2, y + 4, 'slate6')
        cv.set(x + 1, y + 2, 'slate7')
    return cv


def decal_stain(v: int) -> Canvas:
    """Пятна с полупрозрачностью: 0 — тёмное масляное (радужный отлив), 1 — сырая лужа с бликом."""
    cv = Canvas(32, 32)
    rnd = random.Random(80 + v)
    blobs = [(16, 18, 8, 5), (11, 15, 4, 3), (21, 21, 5, 3), (9, 21, 3, 2)] if v == 0 else \
        [(15, 17, 9, 5), (22, 14, 4, 3), (8, 20, 4, 2.5)]
    mask = Canvas(32, 32)
    for cx, cy, rx, ry in blobs:
        mask.ellipse(cx, cy, rx, ry, 'white')
    col = 'ink0' if v == 0 else 'navy1'
    for y in range(32):
        for x in range(32):
            if mask.opaque(x, y):
                edge = any(not mask.opaque(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                cv.set(x, y, with_alpha(col, 120 if edge else 150))
    if v == 0:
        for x, y, c in ((13, 16, 'purple2'), (14, 16, 'teal2'), (19, 19, 'purple2')):
            cv.set(x, y, with_alpha(c, 150))
    else:
        for x, y in ((11, 15), (12, 15), (13, 16), (19, 14)):
            cv.set(x, y, with_alpha('blue5', 140))
        for x, y in ((3, 25), (28, 18)):
            cv.set(x, y, with_alpha('navy1', 120))
    return cv


def decal_board() -> Canvas:
    cv = Canvas(32, 32)
    board(cv, 4, 17, 27, 22, 4, WOOD, split1=True, seed=91, th=1, nail=0.2)
    return cv


# ═════════════════════════ D. ДЕКОР СТЕН ═════════════════════════
def wall_brace() -> Canvas:
    """Крестовая распорка из бруса на скале: две доски крест-накрест, стальная накладка в перекрестье, анкеры."""
    cv = Canvas(32, 32)
    brace(cv, 2, 3, 27, 24, 5, WOOD)
    brace(cv, 2, 24, 27, 3, 5, WOOD)
    cv.rect(12, 12, 8, 7, 'stone3')
    cv.frame(12, 12, 8, 7, 'stone0')
    cv.hline(13, 18, 13, 'stone5')
    for x, y in ((14, 14), (17, 16)):
        bolt(cv, x, y, True)
    for x, y in ((3, 6), (26, 6), (3, 27), (26, 27)):
        cv.rect(x, y, 3, 3, 'stone2')
        cv.set(x, y, 'stone6')
        cv.set(x + 1, y + 1, 'stone4')
        cv.set(x + 3, y + 2, 'fur_ginger1')
    return cv


def wall_sign() -> Canvas:
    """Деревянная табличка с нарисованной лапкой, висит на двух гвоздях."""
    cv = Canvas(32, 32)
    for x in (10, 21):
        cv.line(x, 4, 16 if x == 10 else 16, 1, 'ink3')
    cv.set(16, 1, 'stone6')
    box(cv, 6, 6, 20, 2, 14, WOOD, planks=1, seed=95)
    paw(cv, 12, 11, 'cream3')
    cv.set(14, 15, 'cream2')
    for x, y in ((8, 9), (23, 9), (8, 19), (23, 19)):
        cv.set(x, y, 'stone6')
    return cv


PICK_HEAD = [
    '..ooo.....ooo..',
    '.ohhhoooooolso.',
    'ohllllllllllsso',
    'olsooooooooosso',
    'oso.........oso',
    'oo...........oo',
]


def wall_rack() -> Canvas:
    """Стойка для инструмента: брус на анкерах, две кирки висят головками вверх на колышках."""
    cv = Canvas(32, 32)
    hbeam(cv, 1, 1, 30, 5, WOOD, seed=97)
    for x in (3, 27):
        bolt(cv, x, 3, True)
    for hx, y0 in ((7, 11), (23, 12)):
        lay = blank(cv)
        lay.rect(hx, y0 + 2, 2, 19, 'wood5')
        lay.vline(hx + 1, y0 + 2, y0 + 20, 'wood3')
        lay.set(hx, y0 + 20, 'wood4')
        put(cv, lay, 'wood0')
        head = Canvas.from_ascii(PICK_HEAD, {'o': 'ink2', 'h': 'stone7', 'l': 'stone5', 's': 'stone3'})
        cv.blit(head, hx - 6, y0 - 1)
        cv.rect(hx - 1, y0, 4, 3, 'fur_ginger2')
        cv.set(hx - 1, y0, 'fur_ginger4')
        cv.set(hx + 2, y0 + 2, 'fur_ginger1')
        # шнурок на колышек
        cv.vline(hx, 6, y0 - 1, 'ink3')
        cv.vline(hx + 1, 6, y0 - 2, 'cream0')
    return cv


# ═════════════════════════ сборка ═════════════════════════
FAMILIES = ['planks', 'crates', 'rubble', 'scrap', 'cardboard', 'yarn', 'catfurn', 'sacks']
DEBRIS = {'planks': planks, 'crates': crates, 'rubble': rubble, 'scrap': scrap, 'cardboard': cardboard, 'yarn': yarn,
          'catfurn': catfurn, 'sacks': sacks}


def build(reg) -> None:
    def feet(cv):
        return (cv.w / 2.0, 0.0)

    def add(name, cv, **kw):
        reg.add(name, cv, pivot=feet(cv), **kw)

    for i in range(3):
        add(f'prop_minecart_{i}', minecart(i), note='вагонетка 2×1 (постоянное препятствие)')
    add('prop_winch_0', winch(), note='ручная лебёдка 1×1')
    add('prop_drill_0', drill(), note='буровая на салазках 2×1')
    for i in range(2):
        add(f'prop_sealedshaft_{i}', sealed_shaft(i), note='заколоченный штрек на лицевой грани скалы')
        add(f'prop_reel_{i}', reel(i), note='кабельная катушка 1×1')
    for i in range(3):
        add(f'prop_support_{i}', support(i), note='усиленная крепь у стены 1×1; лицевая грань стойки под трубы')
    for i in range(2):
        add(f'prop_hopper_{i}', hopper(i), note='стальной бункер 2×1')
        add(f'prop_tower_{i}', tower(i), note='кошачья когтеточка-ориентир 1×1')
    add('prop_statue_0', statue_cat(), note='каменная кошка на постаменте')
    add('prop_statue_1', statue_paw(), note='камень-лапка')
    for fam in FAMILIES:
        for i in range(3):
            base = DEBRIS[fam](i)
            name = f'debris_{fam}_{i}'
            add(name, base, note='разрушаемый мусор')
            for level in (1, 2):
                add(f'{name}_d{level}', wreck(base, level, 7000 + 97 * FAMILIES.index(fam) + 13 * i + level),
                    note=f'стадия разрушения {level}')
        add(f'debris_{fam}_rest', rest(fam), note='след на полу после разрушения')
    for i in range(3):
        add(f'decal_pebbles_{i}', decal_pebbles(i))
    add('decal_paint_0', decal_paint(0))
    add('decal_paint_1', decal_paint(1))
    add('decal_paws_0', decal_paws())
    add('decal_stain_0', decal_stain(0), alpha=True)
    add('decal_stain_1', decal_stain(1), alpha=True)
    add('decal_board_0', decal_board())
    add('wall_brace_0', wall_brace())
    add('wall_sign_0', wall_sign())
    add('wall_rack_0', wall_rack())
