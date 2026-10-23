"""Постройки поля: шахты, ворота, выход, кузница, контейнеры (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 4).

Все базы — вид 3/4, свет сверху-слева, контур 1 px из тёмного конца рампы материала.
Наложения (колесо, огонь, створки, свет, завалы) рисуются на холсте размера базы и с тем же pivot:
сборка сама обрезает пустые поля, положение в мире совпадает с базой.
Координаты в комментариях — пиксели холста от левого ВЕРХНЕГО угла (как в PIL).
"""
from __future__ import annotations

import math
import random
from typing import Callable, Dict, List, Sequence, Tuple

from pxl import PALETTE, Canvas, with_alpha

# ───────── материалы: o — контур, s/d — тени, b — основной, l/h/hh — блики ─────────
WOOD = dict(o='wood0', k='wood1', s='wood2', d='wood3', b='wood4', l='wood5', h='wood6', hh='wood7')
# Тёмное дерево (копёр, створки): на тон ниже, чтобы постройки не сливались с табличками.
DWOOD = dict(o='wood0', k='wood0', s='wood1', d='wood2', b='wood3', l='wood4', h='wood5', hh='wood6')
STONE = dict(o='stone0', k='stone1', s='stone2', d='stone3', b='stone4', l='stone5', h='stone6', hh='stone7')
IRON = dict(o='stone0', k='stone1', s='stone1', d='stone2', b='stone3', l='stone4', h='stone5', hh='stone6')
# Камень пещеры (обрамление входов): холодный navy, как стены.
ROCK = dict(o='navy0', k='navy1', s='navy2', d='navy3', b='navy4', l='navy5', h='navy6', hh='navy7')


def rgb(name: str) -> Tuple[int, int, int]:
    return PALETTE[name]


# ───────────────────────── общие помощники ─────────────────────────
def blank(cv: Canvas) -> Canvas:
    return Canvas(cv.w, cv.h)


def put(dst: Canvas, src: Canvas, outline: str = None) -> None:
    """Обвести слой контуром (снаружи формы) и наложить на холст."""
    if outline:
        src.outline(outline)
    dst.blit(src, 0, 0)


def clip(cv: Canvas, keep: Callable[[int, int], bool]) -> None:
    """Стереть пиксели слоя вне маски keep(x, y)."""
    for y in range(cv.h):
        for x in range(cv.w):
            if cv.px[x, y][3] and not keep(x, y):
                cv.px[x, y] = (0, 0, 0, 0)


def remap(cv: Canvas, mapping: Dict[str, str], where: Callable[[int, int], bool]) -> None:
    """Заменить цвета mapping только там, где where(x, y) — для теней на части формы."""
    m = {rgb(k): v for k, v in mapping.items()}
    for y in range(cv.h):
        for x in range(cv.w):
            p = cv.px[x, y]
            if p[3] and p[:3] in m and where(x, y):
                cv.set(x, y, m[p[:3]])


def inside_poly(pts: Sequence[Tuple[float, float]], x: float, y: float) -> bool:
    c = False
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        if (y0 <= y < y1) or (y1 <= y < y0):
            if x < x0 + (y - y0) * (x1 - x0) / (y1 - y0):
                c = not c
    return c


def grain_v(cv: Canvas, x: int, y: int, w: int, h: int, c: str, seed: int, step: int = 3) -> None:
    """Волокна вертикальной доски: редкие короткие штрихи (не шум)."""
    rnd = random.Random(seed)
    for xx in range(x, x + w, step):
        yy = y + rnd.randrange(0, 4)
        while yy < y + h - 1:
            ln = rnd.randrange(2, 6)
            cv.vline(xx, yy, min(y + h - 1, yy + ln - 1), c)
            yy += ln + rnd.randrange(5, 10)


def grain_h(cv: Canvas, x: int, y: int, w: int, h: int, c: str, seed: int, step: int = 2) -> None:
    rnd = random.Random(seed)
    for yy in range(y, y + h, step):
        xx = x + rnd.randrange(0, 5)
        while xx < x + w - 1:
            ln = rnd.randrange(3, 7)
            cv.hline(xx, min(x + w - 1, xx + ln - 1), yy, c)
            xx += ln + rnd.randrange(5, 11)


def vbeam(cv: Canvas, x: int, y: int, w: int, h: int, m: Dict[str, str] = WOOD, seed: int = 0, cap: bool = True) -> None:
    """Вертикальная балка с контуром: светлая левая грань, тёмная правая, светлый торец сверху."""
    cv.frame(x, y, w, h, m['o'])
    cv.rect(x + 1, y + 1, w - 2, h - 2, m['b'])
    if w >= 5:
        cv.vline(x + 1, y + 1, y + h - 2, m['l'])
        cv.vline(x + w - 2, y + 1, y + h - 2, m['d'])
        if w >= 7:
            grain_v(cv, x + 3, y + 4, w - 5, h - 6, m['d'], seed, step=3)
    cv.hline(x + 1, x + w - 2, y + h - 2, m['d'])
    if cap:
        cv.hline(x + 1, x + w - 2, y + 1, m['h'])
        if h >= 8:
            cv.hline(x + 2, x + w - 3, y + 2, m['l'])


def hbeam(cv: Canvas, x: int, y: int, w: int, h: int, m: Dict[str, str] = WOOD, seed: int = 0) -> None:
    """Горизонтальная балка с контуром: светлая верхняя грань, тёмный низ."""
    cv.frame(x, y, w, h, m['o'])
    cv.rect(x + 1, y + 1, w - 2, h - 2, m['b'])
    cv.hline(x + 1, x + w - 2, y + 1, m['h'])
    if h >= 6:
        cv.hline(x + 1, x + w - 2, y + 2, m['l'])
    cv.hline(x + 1, x + w - 2, y + h - 2, m['d'])
    cv.vline(x + 1, y + 2, y + h - 2, m['l'])
    cv.vline(x + w - 2, y + 2, y + h - 2, m['d'])
    if h >= 6:
        grain_h(cv, x + 3, y + 3, w - 6, h - 5, m['d'], seed)


def brace(cv: Canvas, x0: float, y0: float, x1: float, y1: float, t: int, m: Dict[str, str] = WOOD) -> None:
    """Диагональная доска толщиной t (по вертикали) с бликом сверху и тенью снизу."""
    lay = blank(cv)
    lay.polygon([(x0, y0), (x1, y1), (x1, y1 + t), (x0, y0 + t)], m['b'])
    lay.shade(m['l'], m['d'], m['b'])
    put(cv, lay, m['o'])


def bolt(cv: Canvas, x: int, y: int, big: bool = False) -> None:
    """Болт/гвоздь: светлая шляпка (2×2 — с тенью справа-снизу)."""
    if big:
        cv.set(x, y, 'stone6')
        cv.set(x + 1, y, 'stone5')
        cv.set(x, y + 1, 'stone4')
        cv.set(x + 1, y + 1, 'stone2')
    else:
        cv.set(x, y, 'stone6')


def rock(cv: Canvas, cx: float, cy: float, rx: float, ry: float, m: Dict[str, str] = ROCK, seed: int = 0, moss: bool = False, n: int = 7) -> None:
    """Гранёный валун: светлая верхняя грань, боковые грани по направлению к свету (сверху-слева), контур."""
    rnd = random.Random(seed)
    a0 = rnd.uniform(0, 6.28)
    pts = []
    for i in range(n):
        a = a0 + 2 * math.pi * i / n + rnd.uniform(-0.2, 0.2)
        k = rnd.uniform(0.86, 1.04)
        pts.append((cx + rx * k * math.cos(a), cy + ry * k * math.sin(a)))
    tx, ty = cx - rx * 0.12, cy - ry * 0.3
    top = [(tx + (x - cx) * 0.62, ty + (y - cy) * 0.5) for x, y in pts]
    lay = blank(cv)
    lay.polygon(pts, m['d'])
    for y in range(lay.h):
        for x in range(lay.w):
            if not lay.opaque(x, y):
                continue
            px, py = x + 0.5, y + 0.5
            if inside_poly(top, px, py):
                edge = not inside_poly(top, px, py - 1) or not inside_poly(top, px - 1, py)
                lay.set(x, y, m['h'] if edge else m['l'])
                continue
            ang = math.atan2(py - cy, px - cx)
            lum = -0.6 * math.cos(ang) - 0.8 * math.sin(ang)
            lay.set(x, y, m['b'] if lum > 0.35 else (m['d'] if lum > -0.3 else m['s']))
    if moss and rx >= 5:
        mx, my = int(tx), int(ty - ry * 0.28)
        for i, c in enumerate(['green3', 'green4', 'green5', 'green4', 'green3']):
            if lay.opaque(mx + i - 2, my):
                lay.set(mx + i - 2, my, c)
        for i, c in enumerate(['green2', 'green3', 'green4', 'green3', 'green2']):
            if lay.opaque(mx + i - 2, my + 1):
                lay.set(mx + i - 2, my + 1, c)
    put(cv, lay, m['o'])


def thick_line(cv: Canvas, x0: float, y0: float, x1: float, y1: float, c: str, t: int = 1) -> None:
    n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
    for i in range(n + 1):
        f = i / n
        x = x0 + (x1 - x0) * f
        y = y0 + (y1 - y0) * f
        for dx in range(t):
            for dy in range(t):
                cv.set(int(math.floor(x)) + dx, int(math.floor(y)) + dy, c)


def pick_emblem(size: int, head: str, handle: str) -> Canvas:
    """Скрещённые кирки в квадрате size×size: рукояти крест-накрест, головки — дуги, выпуклые наружу.
    Дуги короче 60°, чтобы две головки не сливались в одну арку."""
    s = size
    cv = Canvas(s, s)
    for flip in (False, True):
        lay = Canvas(s, s)
        x0, y0 = 1, s - 2
        x1, y1 = s * 0.72, s * 0.28
        thick_line(lay, x0, y0, x1, y1, handle, 2 if s >= 14 else 1)
        ux, uy = x1 - x0, y1 - y0
        ln = math.hypot(ux, uy)
        ux, uy = ux / ln, uy / ln
        r = s * 0.44
        cx, cy = x1 - ux * r * 0.5, y1 - uy * r * 0.5
        base = math.atan2(uy, ux)
        span = math.radians(54)
        steps = 60
        for i in range(steps + 1):
            a = base - span + 2 * span * i / steps
            mid = 1 - abs(i - steps / 2) / (steps / 2)
            th = 2 if mid > 0.35 and s >= 12 else 1
            for k in range(th):
                rr = r - k
                lay.set(int(math.floor(cx + rr * math.cos(a))), int(math.floor(cy + rr * math.sin(a))), head)
        cv.blit(lay.flipped() if flip else lay, 0, 0)
    return cv


def lantern(cv: Canvas, x: int, y: int) -> None:
    """Подвесной фонарь 7×12, (x, y) — левый верхний угол (кольцо подвеса). Стекло светится amber."""
    rows = [
        '..ooo..',
        '..o.o..',
        '.ooooo.',
        'ommmmmo',
        'oaAAAao',
        'oaAYAao',
        'oaAYAao',
        'oaAAAao',
        'oaaaaao',
        'ommmmmo',
        '.ooooo.',
        '..ooo..',
    ]
    lay = Canvas.from_ascii(rows, {'o': 'stone0', 'm': 'stone3', 'a': 'amber3', 'A': 'amber4', 'Y': 'amber5'})
    lay.set(1, 3, 'stone5')
    lay.set(2, 3, 'stone4')
    cv.blit(lay, x, y)


def paw(cv: Canvas, x: int, y: int, c: str) -> None:
    """Отпечаток лапки 9×8: четыре пальчика и подушечка; (x, y) — левый верхний угол."""
    rows = [
        '..xx.xx..',
        '..xx.xx..',
        'xx.....xx',
        'xx.xxx.xx',
        '..xxxxx..',
        '.xxxxxxx.',
        '.xxxxxxx.',
        '..xx.xx..',
    ]
    cv.blit(Canvas.from_ascii(rows, {'x': c}), x, y)


def glow(cv: Canvas, shape: Callable[[float, float, float], bool], steps: Sequence[Tuple[float, str, int]]) -> None:
    """Ступенчатое свечение: shape(x, y, k) — точка внутри фигуры масштаба k; steps от внешней ступени к внутренней."""
    for k, c, a in steps:
        col = with_alpha(c, a)
        for y in range(cv.h):
            for x in range(cv.w):
                if shape(x + 0.5, y + 0.5, k):
                    cv.px[x, y] = col


def in_ellipse(cx: float, cy: float, rx: float, ry: float) -> Callable[[float, float, float], bool]:
    def f(x: float, y: float, k: float) -> bool:
        dx = (x - cx) / (rx * k)
        dy = (y - cy) / (ry * k)
        return dx * dx + dy * dy <= 1.0
    return f


def gem_crystal(cv: Canvas, x: int, y: int, h: int, w: int = 4) -> None:
    """Кристалл светлых тонов (тонируется в коде умножением): (x, y) — левый нижний угол, высота h."""
    lay = blank(cv)
    half = w / 2.0
    lay.polygon([(x, y + 1), (x, y - h + 2), (x + half, y - h), (x + w, y - h + 2), (x + w, y + 1)], 'cream3')
    for yy in range(lay.h):
        for xx in range(lay.w):
            if lay.opaque(xx, yy) and xx + 0.5 > x + half:
                lay.set(xx, yy, 'cream1')
    lay.set(x, y - h + 3, 'white')
    lay.set(x, y - h + 4, 'white')
    put(cv, lay, 'cream0')


def rails(cv: Canvas, y0: int, y1: int, xl0: float, xr0: float, xl1: float, xr1: float, sleepers: Sequence[int], far: bool = False) -> None:
    """Рельсы от y0 (дальний конец, колея xl0..xr0) к y1 (ближний, xl1..xr1) со шпалами.
    far=True — в глубине штольни: тусклые цвета без контура."""
    def at(y: float) -> Tuple[float, float]:
        t = (y - y0) / float(max(1, y1 - y0))
        return xl0 + (xl1 - xl0) * t, xr0 + (xr1 - xr0) * t

    sl = blank(cv)
    for sy in sleepers:
        xl, xr = at(sy)
        t = (sy - y0) / float(max(1, y1 - y0))
        ext = 2 if far else 3
        th = 1 if far and t < 0.5 else 2 if far else 3
        c0, c1 = ('ink3', 'wood1') if far else ('wood3', 'wood5')
        x0, x1 = int(round(xl - ext)), int(round(xr + ext))
        sl.rect(x0, sy, x1 - x0 + 1, th, c0)
        sl.hline(x0, x1, sy, c1)
    put(cv, sl, None if far else 'wood0')
    for y in range(y0, y1 + 1):
        xl, xr = at(y)
        for xx in (xl, xr):
            xi = int(round(xx))
            if far:
                cv.set(xi, y, 'navy3')
            else:
                cv.set(xi - 1, y, 'stone0')
                cv.set(xi, y, 'stone6')
                cv.set(xi + 1, y, 'stone3')
                cv.set(xi + 2, y, 'stone0')


# ───────────────────────── контейнеры 32×36 ─────────────────────────
CW, CH = 32, 36


def crate_body() -> Canvas:
    """Деревянный ящик доставки: крышка (вид сверху) и передняя стенка с рамой и железными уголками."""
    cv = Canvas(CW, CH)
    x0, x1 = 4, 27           # внешний контур по X
    yt, yf, yb = 10, 17, 32  # верх крышки, ребро крышки/стенки, низ
    cv.rect(x0, yt, x1 - x0 + 1, yb - yt + 1, 'wood0')
    # Крышка: доски вдоль, светлее стенки (на неё падает свет).
    cv.rect(x0 + 1, yt + 1, x1 - x0 - 1, yf - yt - 1, 'wood5')
    for yy in (yt + 3, yt + 5):
        cv.hline(x0 + 2, x1 - 2, yy, 'wood4')
    cv.hline(x0 + 1, x1 - 1, yt + 1, 'wood6')
    cv.vline(x0 + 1, yt + 1, yf - 1, 'wood6')
    cv.vline(x1 - 1, yt + 2, yf - 2, 'wood4')
    cv.hline(x0 + 1, x1 - 1, yf - 1, 'wood7')        # освещённое ребро
    cv.hline(x0 + 1, x1 - 1, yf, 'wood2')            # тень под ребром
    # Передняя стенка: рама из досок и горизонтальные филёнки.
    cv.rect(x0 + 1, yf + 1, x1 - x0 - 1, yb - yf - 1, 'wood4')
    for yy in (yf + 5, yf + 9):
        cv.hline(x0 + 4, x1 - 4, yy, 'wood2')
        cv.hline(x0 + 4, x1 - 4, yy + 1, 'wood5')
    cv.rect(x0 + 1, yf + 1, 3, yb - yf - 1, 'wood5')
    cv.vline(x0 + 1, yf + 1, yb - 1, 'wood6')
    cv.vline(x0 + 4, yf + 1, yb - 1, 'wood2')
    cv.rect(x1 - 3, yf + 1, 3, yb - yf - 1, 'wood3')
    cv.vline(x1 - 4, yf + 1, yb - 1, 'wood2')
    cv.vline(x1 - 1, yf + 1, yb - 1, 'wood2')
    cv.hline(x0 + 1, x1 - 1, yf + 1, 'wood5')
    cv.hline(x0 + 1, x1 - 1, yb - 2, 'wood3')
    cv.hline(x0 + 1, x1 - 1, yb - 1, 'wood2')
    for xx, yy in ((x0 + 1, yf + 1), (x1 - 2, yf + 1), (x0 + 1, yb - 2), (x1 - 2, yb - 2)):
        cv.rect(xx, yy, 2, 2, 'stone3')
        cv.set(xx, yy, 'stone5')
    for xx in (x0 + 2, x1 - 2):
        bolt(cv, xx, yt + 2)
    return cv


def crate_lid_strip() -> Canvas:
    """Передний край крышки — кладётся поверх того, что торчит из ящика."""
    full = crate_body()
    out = Canvas(CW, CH)
    out.blit(full.crop(0, 15, CW, 3), 0, 15)
    return out


def label_plate(cv: Canvas, x: int, y: int, w: int, h: int) -> None:
    """Светлая бумажная наклейка с тёмным краем и уголком-тенью."""
    cv.rect(x, y, w, h, 'wood1')
    cv.rect(x + 1, y + 1, w - 2, h - 2, 'cream2')
    cv.hline(x + 1, x + w - 2, y + 1, 'cream3')
    cv.vline(x + 1, y + 1, y + h - 2, 'cream3')
    cv.hline(x + 2, x + w - 2, y + h - 2, 'cream1')


def band(cv: Canvas, y: int, r: str) -> None:
    """Цветная лента поперёк передней стенки ящика (рампа r: 2 — тень, 3 — основной, 4 — блик)."""
    cv.hline(5, 26, y, f'{r}4')
    cv.hline(5, 26, y + 1, f'{r}3')
    cv.hline(5, 26, y + 2, f'{r}3')
    cv.hline(5, 26, y + 3, f'{r}2')


def coin_top(cv: Canvas, cx: float, cy: float) -> None:
    """Монетка, лежащая плашмя (вид сверху 3/4): 5×3 с ребром."""
    lay = blank(cv)
    lay.ellipse(cx, cy, 3, 1.6, 'gold4')
    lay.hline(int(cx - 2), int(cx + 1), int(cy) + 1, 'gold2')
    lay.set(int(cx - 2), int(cy) - 1, 'gold5')
    put(cv, lay, 'gold1')


def crate_cat() -> Canvas:
    cv = crate_body()
    label_plate(cv, 10, 19, 12, 11)
    paw(cv, 11, 20, 'wood2')
    # Дырочки для воздуха в крышке — внутри котик.
    for xx in (9, 13, 17, 21):
        cv.set(xx, 13, 'wood1')
        cv.set(xx + 1, 13, 'wood2')
    return cv


def crate_dynamite() -> Canvas:
    cv = crate_body()
    band(cv, 22, 'red')
    sticks = blank(cv)
    for i, sx in enumerate((9, 14, 19)):
        top = 6 if i != 1 else 4
        sticks.rect(sx, top, 4, 11, 'red3')
        sticks.vline(sx, top, top + 10, 'red4')
        sticks.vline(sx + 3, top, top + 10, 'red2')
        sticks.hline(sx, sx + 3, top, 'red5')
        sticks.hline(sx, sx + 3, top + 4, 'cream2')   # бумажная обёртка
        sticks.set(sx + 3, top + 4, 'cream0')
    put(cv, sticks, 'red0')
    # Фитили.
    for sx, top in ((9, 6), (14, 4), (19, 6)):
        cv.set(sx + 2, top - 1, 'cream0')
        cv.set(sx + 2, top - 2, 'cream1')
    cv.set(16, 1, 'amber4')
    cv.set(16, 0, 'amber5')
    cv.blit(crate_lid_strip(), 0, 0)
    # Наклейка: шашка с горящим фитилём.
    label_plate(cv, 11, 19, 10, 8)
    cv.rect(13, 22, 6, 3, 'red3')
    cv.hline(13, 18, 22, 'red4')
    cv.hline(13, 18, 24, 'red2')
    cv.set(19, 22, 'wood1')
    cv.set(20, 21, 'amber3')
    return cv


def crate_gold() -> Canvas:
    cv = crate_body()
    band(cv, 22, 'gold')
    coin = blank(cv)
    coin.ellipse(16, 23.5, 4, 4, 'gold3')
    coin.ellipse(16, 23.5, 2.6, 2.6, 'gold4')
    coin.set(15, 21, 'gold5')
    coin.set(14, 22, 'gold5')
    coin.hline(15, 17, 26, 'gold2')
    coin.vline(19, 22, 25, 'gold2')
    put(cv, coin, 'gold0')
    # Монеты горкой на крышке и в щели.
    for cx, cy in ((10, 13), (21, 12.5), (15, 12), (18, 10.5), (12, 10.5), (15.5, 9)):
        coin_top(cv, cx, cy)
    return cv


def crate_elixir() -> Canvas:
    cv = crate_body()
    band(cv, 22, 'purple')
    # Каждый кристалл — отдельным слоем со своим контуром, иначе они сливаются в одно пятно.
    for sx, h in ((8, 7), (19, 8), (13, 11)):
        cr = blank(cv)
        cr.polygon([(sx, 16), (sx, 16 - h + 2), (sx + 2.5, 16 - h), (sx + 5, 16 - h + 2), (sx + 5, 16)], 'purple4')
        for yy in range(16 - h, 17):
            for xx in (sx + 3, sx + 4):
                if cr.opaque(xx, yy):
                    cr.set(xx, yy, 'purple3')
        cr.set(sx + 1, 16 - h + 2, 'purple6')
        cr.set(sx + 1, 16 - h + 3, 'purple5')
        put(cv, cr, 'purple1')
    cv.blit(crate_lid_strip(), 0, 0)
    gem = blank(cv)
    gem.polygon([(16, 19), (20, 23.5), (16, 29), (12, 23.5)], 'purple4')
    gem.polygon([(16, 19), (16, 29), (12, 23.5)], 'purple5')
    gem.set(14, 22, 'purple6')
    gem.set(17, 26, 'purple3')
    gem.set(18, 25, 'purple3')
    put(cv, gem, 'purple1')
    return cv


def barrel_cat(fly: int = -1) -> Canvas:
    """Бочка с котиком: из открытой бочки выглядывают ушки и глазки (рыжий окрас).
    fly ≥ 0 — кадр полёта из пушки: котик зажмурился от восторга и машет поднятыми лапками (кадр 1 — лапки выше)."""
    cv = Canvas(CW, CH)
    cx = 16.0
    top, bot = 16, 32

    def hw(y: int) -> int:
        t = (y - top) / float(bot - top)
        return 9 + round(1.4 * math.sin(math.pi * t))

    body = blank(cv)
    for y in range(top, bot + 1):
        h = hw(y)
        l, r = int(cx - h), int(cx + h - 1)
        body.hline(l, r, y, 'wood4')
        body.set(l, y, 'wood5')
        body.set(l + 1, y, 'wood5')
        body.set(r, y, 'wood2')
        body.set(r - 1, y, 'wood3')
        body.set(r - 2, y, 'wood3')
        # Швы клёпок изгибаются по бочке.
        for k in (-5, 0, 5):
            xx = int(round(cx + k * h / 9.0)) - (1 if k == 0 else 0)
            if l + 1 < xx < r - 1:
                body.set(xx, y, 'wood2' if k > 0 else 'wood3')
        body.set(int(cx - 3 * h / 9.0) - 1, y, 'wood6')
    for yy in (19, 29):
        h = hw(yy)
        l, r = int(cx - h), int(cx + h - 1)
        body.hline(l, r, yy, 'stone4')
        body.hline(l, r, yy + 1, 'stone2')
        body.hline(l + 1, int(cx) - 2, yy, 'stone6')
        body.hline(r - 3, r, yy, 'stone3')
    body.hline(int(cx - 8), int(cx + 7), bot, 'wood2')
    put(cv, body, 'wood0')
    # Котик: макушка с ушками, глазки над ободом; в полёте — зажмуренные «^ ^» и открытый ротик.
    eyes = ['oLFweFFFFFFweFDo', 'oFFeeFFFFFFeeFDo', 'oFFFFFFFFFFFFFDo'] if fly < 0 else \
        ['oLFFeFFFFFFeFFDo', 'oFFeFeFFFFeFeFDo', 'oFFFFFFmmFFFFFDo']
    head = Canvas.from_ascii([
        '.o............o.',
        '.oo..........oo.',
        '.oLo........oDo.',
        '.oLpo......opDo.',
        '.oLppoooooopDDo.',
        'oLLFFFsFFsFFFFDo',
        'oLFFFFFsFsFFFFDo',
    ] + eyes, {'o': 'fur_ginger0', 'L': 'fur_ginger3', 'F': 'fur_ginger2', 'D': 'fur_ginger1', 's': 'fur_ginger1',
               'p': 'pink2', 'e': 'ink1', 'w': 'white', 'm': 'pink1'})
    # Ушки: светлая левая сторона уха, щёки чуть светлее у глаз.
    cv.blit(head, 8, 5)
    # Обод: задняя кромка — за котиком не видна, передняя — поверх головы.
    rim = blank(cv)
    rim.ellipse(16, 16, 10, 3, 'wood6')
    rim.ellipse(16, 16, 8, 2, 'wood1')
    clip(rim, lambda x, y: y >= 16)
    rim.hline(8, 23, 16, 'wood1')
    rim.hline(9, 22, 17, 'wood2')
    rim.hline(7, 24, 18, 'wood6')
    rim.hline(10, 21, 19, 'wood5')
    put(cv, rim, 'wood0')
    if fly < 0:
        # Лапки на ободе — котик держится.
        for px in (10, 19):
            lay = blank(cv)
            lay.rect(px, 15, 3, 2, 'fur_ginger3')
            lay.set(px + 2, 16, 'fur_ginger2')
            put(cv, lay, 'fur_ginger0')
        return cv
    # Полёт: лапки подняты вверх-в стороны от плеч и машут (кадр 1 — выше и шире), подушечки розовые.
    for side in (-1, 1):
        lay = blank(cv)
        sx, sy = 16 + side * 7.0, 15.0                    # плечо у края головы, над ободом
        tx, ty = 16 + side * (11.5 + fly), 7.0 - 2 * fly  # ладошка
        thick_line(lay, sx - 1, sy, tx - 1, ty, 'fur_ginger2', 2)
        lay.ellipse(tx, ty, 2.0, 2.0, 'fur_ginger3')
        lay.set(int(tx) - (1 if side < 0 else 0), int(ty) - 1, 'pink2')
        put(cv, lay, 'fur_ginger0')
    cv.blit(rim, 0, 0)   # обод снова поверх лапок
    return cv


def barrel_tnt(frame: int) -> Canvas:
    """20×22, feet: identical barrel and fuse; only the spark flickers."""
    cv = Canvas(20, 22)
    body = blank(cv)
    for y in range(9, 20):
        half = 7 if 11 <= y <= 17 else 6
        body.hline(10-half, 9+half, y, 'wood4')
        body.set(10-half, y, 'wood6')
        body.set(9+half, y, 'wood2')
        for x in (7, 11, 14):
            body.set(x, y, 'wood3' if x < 10 else 'wood2')
        body.set(5, y, 'wood5')
    body.hline(5, 14, 19, 'wood2')
    for y in (12, 17):
        body.hline(3, 16, y, 'red4')
        body.hline(3, 16, y+1, 'red2')
        body.hline(4, 8, y, 'red5')
        body.set(5, y+1, 'stone6')
        body.set(14, y+1, 'stone4')
    put(cv, body, 'wood0')
    cv.ellipse(10, 9, 7, 2, 'wood6')
    cv.ellipse(10, 9, 5, 1, 'wood1')
    # Four separately outlined sticks, staggered above the open rim.
    for x, y in ((4, 6), (7, 4), (10, 5), (13, 6)):
        cv.rect(x, y, 3, 5, 'red0')
        cv.rect(x, y+1, 2, 4, 'red3')
        cv.vline(x, y+1, 8, 'red5')
        cv.set(x+1, y, 'red4')
    cv.hline(4, 15, 10, 'wood5')
    cv.line(11, 5, 12, 3, 'wood0')
    cv.line(12, 3, 15, 3, 'cream1')
    cv.set(15, 2, 'amber2')
    if frame == 0:
        cv.hline(15, 18, 2, 'amber3')
        cv.vline(16, 0, 4, 'gold4')
        cv.set(16, 2, 'cream3')
        cv.set(18, 0, 'amber3')
    else:
        cv.line(15, 1, 17, 3, 'amber3')
        cv.line(15, 3, 17, 1, 'gold4')
        cv.set(16, 2, 'cream3')
        cv.set(18, 4, 'amber3')
    return cv


def build_containers(reg) -> None:
    piv = (CW / 2.0, 0.0)
    reg.add('barrel_cat', barrel_cat(), pivot=piv, note='бочка: ушки, глазки и лапки котика над ободом')
    for i in range(2):
        reg.add(f'barrel_fly_{i}', barrel_cat(i), pivot=piv, note='бочка в полёте из пушки: котик машет лапками, 0,1 с/кадр')
    for i in range(2):
        reg.add(f'barrel_tnt_{i}', barrel_tnt(i), pivot=(10.0, 0.0), note='20×22 dynamite barrel; spark flicker only')
    reg.add('crate_cat', crate_cat(), pivot=piv, note='ящик с котиком: наклейка с лапкой, дырочки для воздуха')
    reg.add('crate_dynamite', crate_dynamite(), pivot=piv, note='ящик динамита: красная лента, шашки из крышки')
    reg.add('crate_gold', crate_gold(), pivot=piv, note='ящик золота: золотая лента, монета, монеты на крышке')
    reg.add('crate_elixir', crate_elixir(), pivot=piv, note='ящик эликсира: фиолетовая лента, кристаллы')


# ───────────────────────── кузница 2×3: «кошачья печь» 64×106 ─────────────────────────
# Кузница 2×3 (ТЗ 6.4): footprint 64×96 снизу и 10 px над ним — прямо выше зона мержа, туда заходят только кончики
# ушей и труба. Печь — голова рыжего полосатого кота из кирпича: уши с розовым, полоски «М» на лбу, бирюзовые
# глаза-окошки со щелью зрачка (в такт огню вспыхивают), розовый нос, усы, топка в каменной арке — открытый рот;
# на шее синий ошейник с золотыми заклёпками и колокольчиком. Во дворе (нижняя клетка) — наковальня на пне с золотой
# лапкой, стопка золотых слитков и золотая кошка-талисман с поднятой лапкой на красной подушке.
FW, FH = 64, 106
HEAD_C, HEAD_R = (32.0, 45.0), (26.0, 30.0)   # голова-печь: центр и полуоси эллипса (низ — подбородок под ошейником)
HEAD_BOTTOM = 74                               # последняя строка головы
MOUTH = (21, 44, 22, 24)                       # топка-рот: x, y, w, h (арка радиуса w/2 сверху)
EYES = ((22.5, 31.5), (41.5, 31.5))            # центры глаз-окошек
EYE_R = 4.5
COLLAR_Y = 69                                  # верх ошейника по краям; к середине провисает на 3 px
BELL = (32.0, 79.0)                            # центр колокольчика
CHIMNEY = (28, 36)                             # труба между ушами по X (включительно)
CH_Y = 3                                       # верх отверстия трубы
CHIMNEY_TOP = (32, CH_Y)                       # центр отверстия трубы; дым в Unity — ObjectView.ForgeChimneyPixel
EAR_L = [(8.0, 27.0), (13.0, 2.0), (26.0, 16.0)]
EAR_L_IN = [(12.0, 22.0), (14.5, 8.0), (22.0, 16.5)]


# Рыжий кирпич (окрас рыжего кота): яркое пятно на сине-сером полу, полосы «М» на лбу — тёмный рыжий.
GINGER_BRICK = dict(o='fur_ginger0', k='fur_ginger0', s='fur_ginger1', d='fur_ginger1', b='fur_ginger2', l='fur_ginger3', h='fur_ginger3', hh='fur_ginger4')
STONE_HEAD = dict(STONE)
# Глаза: зелёно-бирюзовое стекло с щелью зрачка (у янтарных на рыжем кирпиче нет контраста).
EYE_TEAL = dict(b='teal3', l='teal4', h='teal6')
EYE_AMBER = dict(b='amber3', l='amber4', h='amber5')
FORGE_HEAD, FORGE_EYE = GINGER_BRICK, EYE_TEAL


def _mirror(pts):
    return [(FW - x, y) for x, y in pts]


def head_mask(x: float, y: float) -> bool:
    if y > HEAD_BOTTOM:
        return False
    dx = (x + 0.5 - HEAD_C[0]) / HEAD_R[0]
    dy = (y + 0.5 - HEAD_C[1]) / HEAD_R[1]
    return dx * dx + dy * dy <= 1.0


def mouth_mask(x: float, y: float) -> bool:
    mx, my, mw, mh = MOUTH
    if not (mx <= x < mx + mw and my <= y < my + mh):
        return False
    r = mw / 2.0
    if y + 0.5 < my + r:
        dx = (x + 0.5 - (mx + r)) / r
        dy = (y + 0.5 - (my + r)) / r
        return dx * dx + dy * dy <= 1.0
    return True


def masonry(cv: Canvas, x: int, y: int, w: int, h: int, m: Dict[str, str], seed: int, row_h: int = 5, wmin: int = 6, wmax: int = 10) -> None:
    """Кладка из крупных камней по залитой области: швы m['s'], верхняя кромка камня m['l'], редкие тёмные камни."""
    rnd = random.Random(seed)
    for ry in range(y, y + h, row_h):
        xx = x - rnd.randrange(0, wmax)
        while xx < x + w:
            bw = rnd.randrange(wmin, wmax + 1)
            tone = m['d'] if rnd.random() < 0.22 else (m['l'] if rnd.random() < 0.08 else None)
            for yy in range(ry, min(ry + row_h, y + h)):
                for x2 in range(max(x, xx), min(x + w, xx + bw)):
                    if not cv.opaque(x2, yy):
                        continue
                    if yy == ry + row_h - 1 or x2 == xx + bw - 1:
                        cv.set(x2, yy, m['s'])
                    elif yy == ry and x2 < xx + bw - 2:
                        cv.set(x2, yy, m['h'] if tone == m['l'] else m['l'])
                    elif tone:
                        cv.set(x2, yy, tone)
            xx += bw


def anvil(cv: Canvas, x: int, y: int) -> None:
    """Наковальня 26×13 на пне; (x, y) — левый верхний угол наковальни."""
    rows = [
        '.oooooooooooooooooo.......',
        'oHHHHHHHHHHHHHHHHHHooooo..',
        'oLLLLLLLLLLLLLLLLLLLLLLLoo',
        'oBBBBBBBBBBBBBBBBBBBBBBBBo',
        '.oDBBBBBBBBBBBBBBBBBDDoooo',
        '..ooDBBBBBBBBBBBBBDDo.....',
        '....oooSBBBBBBBDSoo.......',
        '......oSBBBBBBBDSo........',
        '......oSBBBBBBBDSo........',
        '.....oLLLLLLLLLLLLo.......',
        '....oLBBBBBBBBBBBDDo......',
        '....oDDDDDDDDDDDDDSo......',
        '....oooooooooooooooo......',
    ]
    lay = Canvas.from_ascii(rows, {'o': 'stone0', 'H': 'stone7', 'L': 'stone6', 'B': 'stone4', 'D': 'stone3', 'S': 'stone2'})
    cv.blit(lay, x, y)


def golden_cat(cv: Canvas, x: int, y: int) -> None:
    """Золотая кошка-талисман 18×24 на красной подушке, левая лапка поднята; (x, y) — левый верхний угол."""
    rows = [
        '..o........o......',
        '.oHo......oLo.....',
        '.oHHo....oLLo..oo.',
        '.oHHHooooHLLo.oHHo',
        'oHHHHHHHHLLLDooHLo',
        'oHHkHHHHHHkLDo.oLo',
        'oHHHHHpHHHHLDo.oLo',
        'oHHHHkkkHHLLDooLDo',
        '.oHHHHHHHLLLDoLLo.',
        '..ooRRRRRRRooLLDo.',
        '..oHHHHBHHHLLLDo..',
        '.oHHHHHHHHHLLLDo..',
        '.oHHHHHHHHLLLLDo..',
        'oHHHLLHHHHLLLDDDo.',
        'oHHLLHHHHLLLLDDDo.',
        'oHHHHHHHHLLLDDDDo.',
        'oHHHHHHHLLLDDDDDo.',
        '.oLLLLLLLLDDDDDo..',
        '..oooooooooooooo..',
        'ommmmmmmmmmmmmmmmo',
        'oMMMMMMMMMMMMMMMMo',
        'oMMMMMMMMMMMMMMMMo',
        '.oddddddddddddddo.',
        '..oooooooooooooo..',
    ]
    lay = Canvas.from_ascii(rows, {'o': 'gold0', 'H': 'gold5', 'L': 'gold4', 'D': 'gold3', 'k': 'gold1', 'p': 'pink2',
                                   'R': 'red3', 'B': 'gold5', 'm': 'red4', 'M': 'red3', 'd': 'red2'})
    lay.set(7, 10, 'gold2')     # язычок колокольчика на ошейнике
    cv.blit(lay, x, y)


def ingots(cv: Canvas, x: int, y: int) -> None:
    """Стопка золотых слитков 14×9: два внизу, один сверху; (x, y) — левый верхний угол."""
    lay = blank(cv)
    for bx, by in ((x, y + 4), (x + 7, y + 4), (x + 3, y)):
        lay.polygon([(bx + 1, by), (bx + 6, by), (bx + 7, by + 4), (bx, by + 4)], 'gold3')
        lay.hline(bx + 1, bx + 5, by, 'gold5')
        lay.hline(bx + 1, bx + 6, by + 1, 'gold4')
        lay.hline(bx, bx + 6, by + 4, 'gold2')
    put(cv, lay, 'gold0')


def forge_base(m: Dict[str, str] = None, glass: Dict[str, str] = None, collar_c: str = 'blue') -> Canvas:
    m = m or FORGE_HEAD
    glass = glass or FORGE_EYE
    cv = Canvas(FW, FH)
    mx, my, mw, mh = MOUTH

    # Труба между ушами: короткая, из тёмного камня, с ободом и тёмным отверстием (дым — из него).
    c0, c1 = CHIMNEY
    ch = blank(cv)
    ch.rect(c0, CH_Y + 2, c1 - c0 + 1, 16, 'stone3')
    for i, xx in enumerate(range(c0, c1 + 1)):
        ch.vline(xx, CH_Y + 3, CH_Y + 17, ['stone5', 'stone4', 'stone4', 'stone3', 'stone3', 'stone3', 'stone2', 'stone2', 'stone2'][i])
    for yy in range(CH_Y + 7, CH_Y + 17, 4):
        ch.hline(c0, c1, yy, 'stone2')
    put(cv, ch, 'stone0')
    rim = blank(cv)
    rim.ellipse((c0 + c1 + 1) / 2.0, CH_Y + 1.5, (c1 - c0 + 3) / 2.0, 2.5, 'stone6')
    rim.ellipse((c0 + c1 + 1) / 2.0, CH_Y + 1.0, (c1 - c0 - 2) / 2.0, 1.4, 'ink1')
    put(cv, rim, 'stone0')

    # Уши: камень, внутри розовые; светлая левая грань.
    for pts, inner in ((EAR_L, EAR_L_IN), (_mirror(EAR_L), _mirror(EAR_L_IN))):
        ear = blank(cv)
        ear.polygon(pts, m['b'])
        ear.polygon(inner, 'pink2')
        ear.shade(m['h'], m['d'], m['b'])
        put(cv, ear, m['o'])
        tip = blank(cv)
        tip.polygon(inner, 'pink2')
        clip(tip, lambda xx, yy: yy < inner[1][1] + 6)
        tip.fill_where(lambda xx, yy: True, 'pink3')
        cv.blit(tip, 0, 0)

    # Голова-печь: кладка, свет сверху-слева, тень справа и снизу.
    head = blank(cv)
    for y in range(FH):
        for x in range(FW):
            if head_mask(x, y):
                head.set(x, y, m['b'])
    masonry(head, 0, 12, FW, HEAD_BOTTOM - 11, m, seed=21, row_h=5, wmin=6, wmax=11)
    for y in range(FH):
        for x in range(FW):
            if not head.opaque(x, y):
                continue
            dx = (x + 0.5 - HEAD_C[0]) / HEAD_R[0]
            dy = (y + 0.5 - HEAD_C[1]) / HEAD_R[1]
            lum = -0.62 * dx - 0.78 * dy
            edge = dx * dx + dy * dy > 0.78
            if edge and lum > 0.35:
                head.set(x, y, m['h'])
            elif edge and lum < -0.25:
                head.set(x, y, m['s'] if head.get(x, y)[:3] == rgb(m['s']) else m['d'])
    # Полоски на лбу, как у полосатого кота, и розовый румянец под глазами.
    for x0, y0, x1, y1 in ((27, 18, 28, 24), (32, 17, 32, 24), (37, 18, 36, 24)):
        head.line(x0, y0, x1, y1, m['k'])
        head.line(x0 + 1, y0, x1 + 1, y1, m['s'])
    for cx in (15, 47):
        head.hline(cx, cx + 2, 40, 'pink1')
    put(cv, head, m['o'])

    # Глаза-окошки: светлая каменная оправа, янтарное стекло (свет топки), щель зрачка, блик.
    for ex, ey in EYES:
        eye = blank(cv)
        eye.ellipse(ex, ey, EYE_R + 1.2, EYE_R + 1.2, 'stone6')
        eye.ellipse(ex, ey, EYE_R, EYE_R, glass['b'])
        eye.ellipse(ex - 0.6, ey - 0.6, EYE_R - 1.6, EYE_R - 1.6, glass['l'])
        eye.vline(int(ex), int(ey - 3), int(ey + 2), 'ink1')
        eye.set(int(ex) - 2, int(ey) - 2, glass['h'])
        eye.set(int(ex) - 3, int(ey) - 1, glass['h'])
        put(cv, eye, 'stone0')
    # Нос и рот-«ω» над топкой.
    nose = blank(cv)
    nose.polygon([(29.5, 38.0), (34.5, 38.0), (32.0, 41.0)], 'pink2')
    nose.set(30, 38, 'pink3')
    put(cv, nose, 'pink0')
    # Усы: по три волоска с каждой стороны, выходят за щёки.
    for side in (-1, 1):
        for dy0, dy1 in ((-1, -3), (1, 1), (3, 5)):
            cv.line(32 + side * 12, 44 + dy0, 32 + side * 27, 44 + dy1, 'cream2')

    # Топка-рот: арка из светлых клинчатых камней, внутри тёмная глубина с тлеющими углями.
    arch = blank(cv)
    for y in range(my - 3, my + mh):
        for x in range(mx - 3, mx + mw + 3):
            if mouth_mask(x, y):
                continue
            if any(mouth_mask(x + dx, y + dy) for dx in range(-3, 4) for dy in range(-3, 4) if dx * dx + dy * dy <= 8):
                arch.set(x, y, 'stone5')
    cx0, cy0 = mx + mw / 2.0, my + mw / 2.0
    for a in range(198, 343, 24):
        ang = math.radians(a)
        for rr in range(mw // 2, mw // 2 + 4):
            xx = int(math.floor(cx0 + rr * math.cos(ang)))
            yy = int(math.floor(cy0 + rr * math.sin(ang)))
            if arch.opaque(xx, yy):
                arch.set(xx, yy, 'stone3')
    arch.shade('stone7', 'stone3', 'stone5')
    put(cv, arch, 'stone0')
    inner = blank(cv)
    for y in range(cv.h):
        for x in range(cv.w):
            if mouth_mask(x, y):
                inner.set(x, y, 'ink2')
    for y in range(my, my + 7):
        for x in range(mx, mx + mw):
            if inner.opaque(x, y):
                inner.set(x, y, 'ink1')
    inner.hline(mx, mx + mw - 1, my + mh - 1, 'red1')
    inner.hline(mx, mx + mw - 1, my + mh - 2, 'ink3')
    cv.blit(inner, 0, 0)
    # Порог топки — выступающая плита.
    hearth = blank(cv)
    hearth.rect(mx - 3, my + mh, mw + 6, 3, 'stone5')
    hearth.hline(mx - 3, mx + mw + 2, my + mh, 'stone7')
    hearth.hline(mx - 3, mx + mw + 2, my + mh + 2, 'stone3')
    put(cv, hearth, 'stone0')

    # Ошейник: лента по шее (в пределах головы), провисает к середине; золотые заклёпки и колокольчик.
    collar = blank(cv)
    half = HEAD_R[0] * math.sqrt(max(0.0, 1.0 - ((COLLAR_Y + 0.5 - HEAD_C[1]) / HEAD_R[1]) ** 2)) + 1.0
    for x in range(FW):
        dx = (x + 0.5 - HEAD_C[0]) / half
        if abs(dx) > 1.0:
            continue
        top = COLLAR_Y + int(round(3.0 * (1.0 - dx * dx)))
        collar.vline(x, top, top + 3, collar_c + '3')
        collar.set(x, top, collar_c + '4')
        collar.set(x, top + 3, collar_c + '2')
    for sx in (-12, -6, 6, 12):
        x = int(HEAD_C[0]) + sx
        collar.set(x, COLLAR_Y + 1 + int(round(3.0 * (1.0 - (sx / half) ** 2))), 'gold4')
    put(cv, collar, collar_c + '1')
    bell = blank(cv)
    bx, by = BELL
    bell.ellipse(bx, by, 4.5, 4.5, 'gold3')
    bell.ellipse(bx - 1.0, by - 1.0, 2.6, 2.6, 'gold4')
    bell.set(int(bx) - 2, int(by) - 3, 'gold5')
    bell.set(int(bx) - 3, int(by) - 2, 'gold5')
    bell.hline(int(bx) - 3, int(bx) + 3, int(by) + 1, 'gold1')
    bell.set(int(bx), int(by) + 3, 'ink1')
    put(cv, bell, 'gold0')

    # Двор (нижняя клетка): наковальня на пне с золотой лапкой, слитки, кошка-талисман.
    stump = blank(cv)
    stump.rect(5, 94, 17, 11, 'wood4')
    for xx in (8, 12, 17):
        stump.vline(xx, 96, 104, 'wood3')
    stump.vline(5, 94, 104, 'wood5')
    stump.vline(21, 94, 104, 'wood2')
    stump.hline(5, 21, 104, 'wood2')
    put(cv, stump, 'wood0')
    paw(cv, 9, 97, 'gold4')
    anvil(cv, 1, 82)
    ingots(cv, 25, 93)
    golden_cat(cv, 44, 81)
    return cv


def flame(lay: Canvas, cx: float, base: int, h: int, w: float, c: str, lean: float = 0.0) -> None:
    """Язык пламени-капля: снизу широкий, к кончику сужается; lean — наклон кончика (px)."""
    for y in range(base - h, base + 1):
        t = (y - (base - h)) / float(h)          # 0 — кончик, 1 — низ
        hw = (w / 2.0) * min(1.0, (t / 0.6) ** 0.75)
        sx = cx + lean * (1 - t) ** 2
        if hw < 0.5:
            lay.set(int(math.floor(sx)), y, c)
            continue
        lay.hline(int(round(sx - hw)), int(round(sx + hw)) - 1, y, c)


def forge_fire(frame: int) -> Canvas:
    """Огонь в топке-рту: три языка-капли (amber2→amber5), слой углей, искорки; глаза-окошки вспыхивают в такт."""
    cv = Canvas(FW, FH)
    mx, my, mw, mh = MOUTH
    base = my + mh - 2
    tongues = [[(5.0, 12, 7, -1), (11.0, 18, 9, 1), (16.5, 13, 7, 2)],
               [(5.5, 15, 7, 1), (10.5, 15, 9, -1), (16.0, 16, 7, 0)],
               [(4.5, 13, 7, 0), (11.5, 17, 9, -2), (17.0, 12, 6, 1)]][frame]
    lay = blank(cv)
    for scale, c in ((1.0, 'amber2'), (0.72, 'amber3'), (0.45, 'amber4')):
        for x, h, w, lean in tongues:
            flame(lay, mx + x, base, max(2, int(h * scale)), w * (0.6 + 0.4 * scale), c, lean * scale)
    lay.hline(mx + 1, mx + mw - 2, base, 'amber3')
    lay.hline(mx + 1, mx + mw - 2, base + 1, 'amber1')
    lay.hline(mx + 4, mx + mw - 5, base - 1, 'amber5')
    lay.hline(mx + 6, mx + mw - 7, base - 2, 'amber5')
    for k, x in enumerate(range(mx + 2, mx + mw - 2, 4)):
        lay.set(x + frame, base + 1, 'amber4' if (k + frame) % 2 else 'red3')
    edge = blank(cv)
    a2 = rgb('amber2')
    for y in range(lay.h):
        for x in range(lay.w):
            if lay.opaque(x, y) and lay.get(x, y)[:3] == a2:
                if not lay.opaque(x, y - 1) or not lay.opaque(x - 1, y) or not lay.opaque(x + 1, y):
                    edge.set(x, y, 'amber1')
    lay.blit(edge, 0, 0)
    for sx, sy in [[(mx + 3, my + 8), (mx + 14, my + 5)], [(mx + 8, my + 6), (mx + 17, my + 9)], [(mx + 5, my + 5), (mx + 13, my + 8)]][frame]:
        lay.set(sx, sy, 'amber4')
        lay.set(sx, sy + 1, 'amber2')
    clip(lay, mouth_mask)
    cv.blit(lay, 0, 0)
    # Глаза: отблеск огня в стекле — ярче в кадрах 0 и 2.
    if frame != 1:
        for ex, ey in EYES:
            for ox, oy in ((-2, 1), (-1, 2), (1, 2), (2, 1), (-2, -1)):
                cv.set(int(ex) + ox, int(ey) + oy, FORGE_EYE['h'])
    return cv


def forge_glow() -> Canvas:
    """Тёплый ореол топки (под огнём): свет в устье. Пятна на полу нет: перед топкой ошейник и двор — плёнка поверх
    кладки, наковальни и слитков читалась бы грязным пятном."""
    cv = Canvas(FW, FH)
    mx, my, mw, mh = MOUTH
    lay = blank(cv)
    glow(lay, in_ellipse(mx + mw / 2.0, my + mh - 2, 12, 18), [(1.0, 'amber1', 110), (0.65, 'amber2', 140), (0.35, 'amber3', 170)])
    clip(lay, mouth_mask)
    cv.blit(lay, 0, 0)
    return cv


def build_forge(reg) -> None:
    piv = (FW / 2.0, 0.0)
    reg.add('forge_base', forge_base(), pivot=piv, note=f'кузница 2×3 «кошачья печь»; отверстие трубы {CHIMNEY_TOP}')
    for i in range(3):
        reg.add(f'forge_fire_{i}', forge_fire(i), pivot=piv, note='огонь в топке-рту и отблеск в глазах, 0,12 с/кадр')
    reg.add('forge_glow', forge_glow(), pivot=piv, alpha=True, note='ореол топки (под огнём)')


# ───────────────────────── шахта 64×112 ─────────────────────────
SW, SH = 64, 112
WHEEL_C = (32.0, 12.0)        # центр колеса копра (на стыке пикселей 31/32 и 11/12)
WHEEL_R = 11
SH_MOUTH = (17, 41, 30, 39)   # проём штольни: x, y, w, h
SH_GROUND = 80                # линия земли у входа
CART = (21, 93, 22, 11)       # вагонетка: x, y верхнего обода, ширина, высота корпуса
SH_RAIL_TOP = (26.5, 37.5)    # колея у входа
SH_RAIL_BOT = (22.5, 41.5)    # колея у нижнего края


def shaft_mouth_mask(x: float, y: float) -> bool:
    mx, my, mw, mh = SH_MOUTH
    return mx <= x < mx + mw and my <= y < my + mh


def shaft_base() -> Canvas:
    cv = Canvas(SW, SH)
    mx, my, mw, mh = SH_MOUTH
    # Скальные плечи по бокам входа (камень пещеры): тёмная масса скалы, поверх — валуны осыпи
    # (внизу крупнее, вверху мельче), чтобы между ними был камень, а не пол.
    mass = blank(cv)
    mass.polygon([(0, 34), (6, 27), (17, 27), (17, 84), (0, 84)], 'navy1')
    mass.polygon([(64, 34), (58, 27), (47, 27), (47, 84), (64, 84)], 'navy1')
    put(cv, mass, 'navy0')
    for i, (bx, by, rx, ry, moss) in enumerate([
        (12, 30, 7, 5, False), (52, 30, 7, 5, True),
        (5, 40, 5, 6, True), (59, 41, 5, 6, False),
        (4, 54, 6, 7, False), (60, 53, 5, 7, True),
        (7, 67, 8, 7, True), (57, 66, 8, 7, False),
        (4, 78, 6, 5, False), (60, 78, 6, 5, False), (13, 81, 5, 3, False), (51, 81, 5, 3, True),
    ]):
        rock(cv, bx, by, rx, ry, ROCK, seed=i * 5 + 2, moss=moss)
    # Тёмный проём: глубина и дальняя крепь.
    dark = blank(cv)
    dark.rect(mx, my, mw, mh, 'ink2')
    dark.rect(mx + 4, my + 6, mw - 8, mh - 6, 'ink1')
    dark.rect(mx + 8, my + 10, mw - 16, mh - 16, 'ink0')
    # Пол штольни светлеет к выходу.
    dark.polygon([(mx, my + mh), (mx + 8, my + mh - 12), (mx + mw - 8, my + mh - 12), (mx + mw, my + mh)], 'navy1')
    cv.blit(dark, 0, 0)
    far = blank(cv)
    far.rect(mx + 4, my + 4, 3, mh - 14, 'wood1')
    far.rect(mx + mw - 7, my + 4, 3, mh - 14, 'wood1')
    far.rect(mx + 4, my + 4, mw - 8, 3, 'wood1')
    far.hline(mx + 4, mx + mw - 5, my + 4, 'wood2')
    far.vline(mx + 4, my + 5, my + mh - 11, 'wood2')
    cv.blit(far, 0, 0)
    # Фонарик в глубине (SH_LAMP).
    cv.set(SH_LAMP[0], SH_LAMP[1] - 1, 'stone2')
    cv.set(SH_LAMP[0], SH_LAMP[1], 'amber4')
    cv.set(SH_LAMP[0], SH_LAMP[1] + 1, 'amber2')
    cv.set(SH_LAMP[0] - 1, SH_LAMP[1] + 1, 'amber1')
    rails(cv, my + mh - 12, SH_GROUND - 1, 30, 34, SH_RAIL_TOP[0], SH_RAIL_TOP[1], [my + mh - 9, my + mh - 4], far=True)
    rails(cv, SH_GROUND, SH - 1, SH_RAIL_TOP[0], SH_RAIL_TOP[1], SH_RAIL_BOT[0], SH_RAIL_BOT[1], [SH_GROUND + 1, SH_GROUND + 8, SH_GROUND + 16, SH_GROUND + 25])
    # Упор в конце пути.
    hbeam(cv, int(SH_RAIL_BOT[0]) - 4, SH - 5, int(SH_RAIL_BOT[1] - SH_RAIL_BOT[0]) + 10, 5, WOOD, seed=9)
    cv.rect(int(SH_RAIL_BOT[0]) - 2, SH - 3, 2, 2, 'red3')
    cv.rect(int(SH_RAIL_BOT[1]) + 2, SH - 3, 2, 2, 'red3')

    # Копёр: А-образные ноги от краёв крыши к оси, перекладина, трос (колесо — наложением shaft_wheel_*).
    brace(cv, 10, 26, 29, 9, 3, DWOOD)
    brace(cv, 53, 26, 34, 9, 3, DWOOD)
    hbeam(cv, 15, 18, 34, 4, DWOOD, seed=7)
    cv.vline(int(WHEEL_C[0] + WHEEL_R - 1), int(WHEEL_C[1]) + 1, 25, 'ink2')
    cv.rect(29, 8, 6, 6, 'stone0')
    cv.rect(30, 9, 4, 4, 'stone3')
    cv.set(30, 9, 'stone5')

    # Козырёк из красных досок над входом.
    roof = blank(cv)
    roof.polygon([(4, 35), (9, 26), (55, 26), (60, 35)], 'red3')
    for yy in (29, 32):
        roof.hline(5, 59, yy, 'red2')
    roof.hline(9, 54, 27, 'red4')
    roof.hline(9, 54, 26, 'red5')
    for i, xx in enumerate(range(11, 55, 6)):
        roof.vline(xx, 28 if i % 2 else 30, 34, 'red2')
    remap(roof, {'red3': 'red2', 'red4': 'red3'}, lambda x, y: x >= 55)
    put(cv, roof, 'red0')
    # Стойки и верхняк входа.
    vbeam(cv, mx - 8, 37, 8, SH_GROUND - 37 + 1, WOOD, seed=11)
    vbeam(cv, mx + mw, 37, 8, SH_GROUND - 37 + 1, WOOD, seed=12)
    hbeam(cv, 6, 34, 52, 7, WOOD, seed=13)
    for xx in (mx - 6, mx + mw + 4):
        bolt(cv, xx, 43, big=True)
        bolt(cv, xx, 74, big=True)
    # Вывеска со скрещёнными кирками.
    sign = blank(cv)
    sign.rect(21, 24, 22, 17, 'wood6')
    sign.hline(21, 42, 24, 'wood7')
    sign.vline(21, 24, 40, 'wood7')
    sign.hline(22, 42, 40, 'wood5')
    sign.vline(42, 25, 40, 'wood5')
    put(cv, sign, 'wood1')
    cv.blit(pick_emblem(15, 'wood2', 'wood3'), 25, 25)
    for bx, by in ((22, 25), (41, 25), (22, 39), (41, 39)):
        cv.set(bx, by, 'wood4')
    # Фонарь на кронштейне слева.
    cv.hline(3, mx - 8, 42, 'wood1')
    cv.hline(3, mx - 9, 43, 'wood2')
    lantern(cv, 1, 44)
    shaft_cart(cv)
    return cv


def shaft_cart(cv: Canvas) -> None:
    """Вагонетка на рельсах: колёса на колее, деревянный кузов с железной обвязкой, открытый верх."""
    x, y, w, h = CART
    for wx in (SH_RAIL_BOT[0] + 1.5, SH_RAIL_BOT[1] - 0.5):
        wh = blank(cv)
        wh.ellipse(wx, y + h + 1, 3, 3, 'stone3')
        wh.set(int(wx) - 1, y + h, 'stone5')
        wh.set(int(wx), y + h + 1, 'stone1')
        put(cv, wh, 'stone0')
    body = blank(cv)
    body.polygon([(x, y), (x + w, y), (x + w - 2, y + h), (x + 2, y + h)], 'wood3')
    for yy in (y + 6, y + 9):
        body.hline(x + 2, x + w - 3, yy, 'wood2')
    body.hline(x + 2, x + w - 3, y + 4, 'wood4')
    body.hline(x + 2, x + w - 3, y + 7, 'wood4')
    # Железные углы.
    for yy in range(y, y + h):
        k = (yy - y) * 2 // h
        for xx in (x + k, x + k + 1, x + k + 2, x + w - 3 - k, x + w - 2 - k, x + w - 1 - k):
            if body.opaque(xx, yy):
                body.set(xx, yy, 'stone3')
        body.set(x + k, yy, 'stone4')
    # Открытый верх: обод и тёмное нутро.
    body.hline(x, x + w - 1, y, 'stone5')
    body.rect(x + 2, y + 1, w - 4, 2, 'ink2')
    body.hline(x + 2, x + w - 3, y + 1, 'ink1')
    body.hline(x + 1, x + w - 2, y + 3, 'stone4')
    body.set(x, y, 'stone6')
    put(cv, body, 'stone0')
    for yy in (y + 5, y + 9):
        bolt(cv, x + 2 + (yy - y) * 2 // h // 2, yy)
        bolt(cv, x + w - 3, yy)


def shaft_wheel(frame: int) -> Canvas:
    """Колесо копра: красный обод (в тон крыше), 4 железные спицы, поворот на 22,5° за кадр
    (цикл из 4 кадров — четверть оборота; спицы симметричны, поэтому петля бесшовная)."""
    cv = Canvas(SW, SH)
    cx, cy = WHEEL_C
    r = WHEEL_R
    ang0 = math.radians(22.5 * frame)
    lay = blank(cv)
    for y in range(lay.h):
        for x in range(lay.w):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            if d > r:
                continue
            if d > r - 2.6:
                lum = -(dx * 0.6 + dy * 0.8) / r
                lay.set(x, y, 'red4' if lum > 0.5 else ('red2' if lum < -0.45 else 'red3'))
                continue
            # Тёмный фон между спицами: на 4× тонкие спицы на фоне пола не читаются.
            lay.set(x, y, 'navy1')
            # Спицы: расстояние до двух диаметров (4 спицы) — ровная толщина 2 px под любым углом.
            for k in range(2):
                a = ang0 + k * math.pi / 2
                perp = abs(dx * math.sin(a) - dy * math.cos(a))
                if perp <= 1.0:
                    lay.set(x, y, 'stone5' if dx * 0.6 + dy * 0.8 < 0 else 'stone4')
    # Заклёпки на ободе между спицами — подчёркивают вращение.
    for k in range(4):
        a = ang0 + math.pi / 4 + k * math.pi / 2
        lay.set(int(math.floor(cx + (r - 1.3) * math.cos(a))), int(math.floor(cy + (r - 1.3) * math.sin(a))), 'red5')
    lay.rect(int(cx) - 2, int(cy) - 2, 4, 4, 'stone3')
    lay.set(int(cx) - 2, int(cy) - 2, 'stone6')
    lay.set(int(cx) - 1, int(cy) - 2, 'stone5')
    lay.set(int(cx) - 2, int(cy) - 1, 'stone5')
    lay.set(int(cx) + 1, int(cy) + 1, 'stone2')
    put(cv, lay, 'ink1')
    return cv


def shaft_rubble() -> Canvas:
    """Завал: доски крест-накрест на проёме, камни у основания и на рельсах."""
    cv = Canvas(SW, SH)
    mx, my, mw, mh = SH_MOUTH
    brace(cv, mx - 3, my + 3, mx + mw + 2, my + 24, 4, WOOD)
    brace(cv, mx - 3, my + 24, mx + mw + 2, my + 3, 4, WOOD)
    hbeam(cv, mx - 5, my + 12, mw + 10, 5, WOOD, seed=21)
    for xx in (mx - 3, mx + mw + 1):
        bolt(cv, xx, my + 14, big=True)
    for i, (bx, by, rx, ry) in enumerate([(31, 67, 8, 6), (22, 72, 7, 6), (42, 73, 7, 6), (31, 77, 8, 5), (21, 80, 5, 4), (43, 81, 5, 3), (36, 86, 3, 2)]):
        rock(cv, bx, by, rx, ry, STONE, seed=30 + i)
    return cv


def shaft_gems() -> Canvas:
    """Кристаллы в вагонетке и у входа — только светлые тона: код умножает их на цвет самоцвета шахты."""
    cv = Canvas(SW, SH)
    x, y, w, h = CART
    heap = blank(cv)
    heap.ellipse(x + w / 2.0, y + 0.5, w / 2.0 - 2, 3, 'cream2')
    heap.shade('cream3', 'cream1', 'cream2')
    put(cv, heap, 'cream0')
    for gx, gy, gh in ((x + 3, y, 7), (x + 8, y - 1, 10), (x + 13, y, 8), (x + 6, y + 1, 4), (x + 16, y + 1, 5)):
        gem_crystal(cv, gx, gy, gh, 4)
    for gx, gy, gh in ((48, 87, 6), (52, 86, 9), (56, 88, 5)):
        gem_crystal(cv, gx, gy, gh, 4)
    return cv


SH_LAMP = (36, 55)            # фонарик в глубине штольни (пиксель)


def shaft_light() -> Canvas:
    """Тёплый свет из штольни: ореол фонарика в глубине и освещённый пол с рельсами; стены остаются тёмными,
    чтобы читалась глубина. Снаружи свет веером ложится на рельсы."""
    cv = Canvas(SW, SH)
    mx, my, mw, mh = SH_MOUTH
    lx, ly = SH_LAMP
    lay = blank(cv)
    lay.rect(mx, my, mw, mh, with_alpha('amber1', 70))
    # Пол штольни: трапеция к выходу.
    glow(lay, lambda x, y, k: y >= my + mh - 13 * k and inside_poly(
        [(mx, my + mh), (mx + 8, my + mh - 13), (mx + mw - 8, my + mh - 13), (mx + mw, my + mh)], x, y),
        [(1.0, 'amber2', 120), (0.6, 'amber3', 145)])
    glow(lay, in_ellipse(lx + 0.5, ly + 1.5, 7, 9), [(1.0, 'amber2', 110), (0.6, 'amber3', 150), (0.3, 'amber4', 200)])
    clip(lay, shaft_mouth_mask)
    cv.blit(lay, 0, 0)
    spill = blank(cv)
    cxm = mx + mw / 2.0

    def fan(x: float, y: float, k: float) -> bool:
        if y < SH_GROUND or y > SH_GROUND + 20 * k:
            return False
        t = (y - SH_GROUND) / 20.0
        return abs(x - cxm) <= (mw / 2.0) * (0.45 + 0.55 * k) + t * 6
    glow(spill, fan, [(1.0, 'amber2', 80), (0.55, 'amber3', 105)])
    cv.blit(spill, 0, 0)
    return cv


# Состояния шахты (ТЗ 8.3; Docs/Visual/ART_DIRECTION.md, «Шахты»): завал — shaft_rubble; готова к активации — лента
# с бантом поперёк входа; работает — свет из штольни и колесо копра; готова выплата — вагонетка с горкой самоцветов
# и блеск; автоматизирована — табличка с котиком-управляющим на правой стойке.
RIBBON = {'R': 'red3', 'r': 'red4', 'h': 'red5', 'd': 'red2', 'K': 'gold4', 'k': 'gold3'}
RIBBON_BOW = [
    '.dRR.....RRd.',
    'dRrrR...RrrRd',
    'dRrhrRKRrhrRd',
    '.dRrrRKRrrRd.',
    '..ddR.k.Rdd..',
    '.....RdR.....',
    '....Rd.dR....',
    '...Rd...dR...',
    '...d.....d...',
]


def shaft_ribbon() -> Canvas:
    """Готова к активации: красная лента от стойки к стойке с провисом и бант с золотым узлом по центру входа."""
    cv = Canvas(SW, SH)
    mx, my, mw, mh = SH_MOUTH
    lay = blank(cv)
    x0, x1, y0 = mx - 6, mx + mw + 5, my + 15
    for x in range(x0, x1 + 1):
        t = (x - x0) / float(x1 - x0)
        y = int(round(y0 + 12.0 * t * (1 - t)))
        lay.set(x, y, 'red4')
        lay.set(x, y + 1, 'red3')
        lay.set(x, y + 2, 'red2')
    bow = Canvas.from_ascii(RIBBON_BOW, RIBBON)
    lay.blit(bow, mx + mw // 2 - bow.w // 2, y0 + 1)
    put(cv, lay, 'red0')
    for bx in (x0, x1):
        bolt(cv, bx, y0 + 1)
    return cv


def shaft_heap() -> Canvas:
    """Готова выплата: горка самоцветов над вагонеткой и россыпь у колеса — светлые тона, код красит цветом самоцвета."""
    cv = Canvas(SW, SH)
    x, y, w, h = CART
    heap = blank(cv)
    heap.ellipse(x + w / 2.0, y - 1.5, w / 2.0 - 1, 4.5, 'cream2')
    heap.shade('cream3', 'cream1', 'cream2')
    put(cv, heap, 'cream0')
    for gx, gy, gh in ((x + 1, y - 1, 8), (x + 5, y - 3, 12), (x + 10, y - 4, 14), (x + 14, y - 3, 11), (x + 18, y - 1, 7),
                       (x + 8, y, 6), (x + 13, y, 5)):
        gem_crystal(cv, gx, gy, gh, 4)
    for gx, gy, gh in ((9, 94, 6), (13, 96, 8), (5, 97, 5)):
        gem_crystal(cv, gx, gy, gh, 4)
    return cv


GLINT_AT = [(32, 78), (25, 86), (14, 89)]
GLINT_FRAMES = [
    ['.....', '.....', '..w..', '.....', '.....'],
    ['.....', '..c..', '.cwc.', '..c..', '.....'],
    ['..c..', '..w..', 'cwWwc', '..w..', '..c..'],
    ['.....', '..c..', '.cwc.', '..c..', '.....'],
]


def shaft_glint(frame: int) -> Canvas:
    """Блеск готовой выплаты над горкой: три звёздочки со сдвигом фазы, 4 кадра."""
    cv = Canvas(SW, SH)
    for k, (gx, gy) in enumerate(GLINT_AT):
        rows = GLINT_FRAMES[(frame + k * 2) % len(GLINT_FRAMES)]
        star = Canvas.from_ascii(rows, {'w': 'cream4', 'W': 'white', 'c': 'gold5'})
        cv.blit(star, gx - 2, gy - 2)
    return cv


PLAQUE = (45, 45, 14, 12)     # табличка автоматизации: x, y, ширина, высота


def shaft_plaque() -> Canvas:
    """Автоматизирована: табличка на правой стойке — силуэт котика-управляющего на кремовом поле и золотая звезда."""
    cv = Canvas(SW, SH)
    px, py, pw, ph = PLAQUE
    for sx in (px + 3, px + pw - 4):
        cv.vline(sx, py - 8, py - 1, 'stone3')
    board = blank(cv)
    board.rect(px, py, pw, ph, 'wood3')
    board.hline(px, px + pw - 1, py, 'wood5')
    board.vline(px, py, py + ph - 1, 'wood4')
    board.hline(px, px + pw - 1, py + ph - 1, 'wood2')
    board.rect(px + 2, py + 2, pw - 4, ph - 4, 'cream2')
    board.hline(px + 2, px + pw - 3, py + 2, 'cream3')
    put(cv, board, 'wood0')
    head = Canvas.from_ascii([
        'K......K',
        'KK....KK',
        'KKKKKKKK',
        'KeKKKKeK',
        'KKKpKKKK',
        '.KKKKKK.',
    ], {'K': 'ink2', 'e': 'teal5', 'p': 'pink2'})
    cv.blit(head, px + 3, py + 3)
    star = Canvas.from_ascii(['.s.', 'sSs', '.s.'], {'s': 'gold3', 'S': 'gold5'})
    cv.blit(star, px + pw - 3, py - 2)
    return cv


def build_shaft(reg) -> None:
    piv = (SW / 2.0, 0.0)
    reg.add('shaft_base', shaft_base(), pivot=piv, note='шахта 2×3; колесо всегда рисуется наложением shaft_wheel_*')
    for i in range(4):
        reg.add(f'shaft_wheel_{i}', shaft_wheel(i), pivot=piv, note='колесо копра, 0,15 с/кадр; стоит — кадр 0')
    reg.add('shaft_rubble', shaft_rubble(), pivot=piv, note='завал входа')
    reg.add('shaft_gems', shaft_gems(), pivot=piv, note='кристаллы светлых тонов: SpriteRenderer.color = цвет самоцвета')
    reg.add('shaft_light', shaft_light(), pivot=piv, alpha=True, note='тёплый свет из штольни (шахта работает)')
    reg.add('shaft_ribbon', shaft_ribbon(), pivot=piv, note='готова к активации: лента с бантом поперёк входа')
    reg.add('shaft_heap', shaft_heap(), pivot=piv, note='готова выплата: горка самоцветов (тонируется цветом самоцвета)')
    for i in range(4):
        reg.add(f'shaft_glint_{i}', shaft_glint(i), pivot=piv, note='блеск готовой выплаты, 0,12 с/кадр')
    reg.add('shaft_plaque', shaft_plaque(), pivot=piv, note='автоматизирована: табличка с котиком-управляющим')


# ───────────────────────── ворота 160×64 ─────────────────────────
GW, GH = 160, 64
G_GROUND = 58           # нижний ряд забора (контур — на 58)
G_OPEN = (35, 125)      # проём по X [от, до): две створки по 45 px
G_LEAF_TOP = 24
G_LANTERNS = ((27, 30), (125, 30))   # левый верхний угол фонарей 7×12 на больших столбах


def gate_frame() -> Canvas:
    cv = Canvas(GW, GH)
    lx, rx = G_OPEN
    # Порог в проёме.
    sill = blank(cv)
    sill.rect(lx - 1, G_GROUND - 3, rx - lx + 2, 4, 'wood2')
    sill.hline(lx - 1, rx, G_GROUND - 3, 'wood4')
    sill.hline(lx - 1, rx, G_GROUND - 2, 'wood3')
    put(cv, sill, 'wood0')
    # Боковые секции: X-раскосы, перила, крайние столбы.
    for side in (0, 1):
        x0, x1 = (0, lx - 9) if side == 0 else (rx + 8, GW - 1)
        brace(cv, x0 + 5, 30, x1 - 4, 46, 4, WOOD)
        brace(cv, x0 + 5, 46, x1 - 4, 30, 4, WOOD)
        hbeam(cv, x0 + 3, 26, x1 - x0 - 2, 6, WOOD, seed=40 + side)
        hbeam(cv, x0 + 3, 49, x1 - x0 - 2, 6, WOOD, seed=42 + side)
        ox = 0 if side == 0 else GW - 7
        vbeam(cv, ox, 21, 7, G_GROUND - 21 + 1, WOOD, seed=44 + side)
        bolt(cv, ox + 2, 28, big=True)
        bolt(cv, ox + 2, 51, big=True)
    # Большие столбы проёма, верхняя балка, подкосы.
    for side, px in ((0, lx - 8), (1, rx)):
        vbeam(cv, px, 14, 8, G_GROUND - 14 + 1, WOOD, seed=50 + side)
        bolt(cv, px + 2, 46, big=True)
        bolt(cv, px + 2, 52, big=True)
    hbeam(cv, 21, 8, 118, 8, WOOD, seed=55)
    for bx in (23, 134):
        bolt(cv, bx, 10, big=True)
    # Железные уголки на стыке столбов и балки.
    for px in (lx - 8, rx):
        cv.rect(px + 1, 16, 6, 2, 'stone3')
        cv.hline(px + 1, px + 6, 16, 'stone5')
        cv.hline(px, px + 7, 18, 'stone0')
    # Вывеска со скрещёнными кирками (светлые кирки на дереве — как на концепте).
    sign = blank(cv)
    sign.rect(62, 1, 36, 20, 'wood4')
    for yy in (7, 14):
        sign.hline(63, 96, yy, 'wood3')
    sign.hline(62, 97, 1, 'wood6')
    sign.vline(62, 1, 20, 'wood5')
    sign.hline(62, 97, 20, 'wood2')
    sign.vline(97, 2, 20, 'wood2')
    put(cv, sign, 'wood0')
    for bx, by in ((64, 3), (95, 3), (64, 17), (95, 17)):
        bolt(cv, bx, by, big=True)
    cv.blit(pick_emblem(17, 'wood7', 'wood6'), 72, 2)
    for x, y in G_LANTERNS:
        lantern(cv, x, y)
    return cv


def gate_leaf(width: int, lift: int, hinge_x: int, mirror: bool, broken: bool = False, cracked: bool = False) -> Canvas:
    """Створка из досок с двумя планками, раскосом и петлями. width — видимая ширина (поворот от зрителя),
    lift — насколько свободный край ушёл вверх (дальше от зрителя — выше на экране)."""
    cv = Canvas(GW, GH)
    full = 45
    top, bot = G_LEAF_TOP, G_GROUND - 1
    hgt = bot - top + 1
    n_planks = 6
    pw = full / float(n_planks)
    lay = blank(cv)

    def X(i: int) -> int:
        return hinge_x + i if not mirror else hinge_x - 1 - i

    def off(i: int) -> int:
        return int(round(lift * (i + 0.5) / float(width)))

    for i in range(width):
        u = (i + 0.5) / width * full
        dy = off(i)
        plank = int(u / pw)
        pu = u - plank * pw
        x = X(i)
        for v in range(hgt):
            lay.set(x, top + v - dy, 'wood4' if pu < pw * 0.25 and width > 24 else 'wood3')
        u_next = (i + 1.5) / width * full
        if int(u_next / pw) != plank and i < width - 1:
            for v in range(1, hgt - 1):
                lay.set(x, top + v - dy, 'wood2')
        lay.set(x, top - dy, 'wood5')
    # Две поперечные планки и раскос от нижней петли к верхнему свободному углу.
    for v0 in (4, hgt - 9):
        for i in range(width):
            for k in range(5):
                lay.set(X(i), top + v0 + k - off(i), 'wood6' if k == 0 else ('wood2' if k == 4 else 'wood4'))
    for i in range(width):
        t = (i + 0.5) / width
        vc = (hgt - 9) - t * (hgt - 18)
        for k in range(4):
            v = int(round(vc)) - 2 + k
            if 9 <= v < hgt - 9:
                lay.set(X(i), top + v - off(i), 'wood5' if k == 0 else ('wood2' if k == 3 else 'wood4'))
    # Железные петли у столба.
    for v0 in (5, hgt - 8):
        for i in range(min(width, max(3, width // 3))):
            lay.set(X(i), top + v0 - off(i), 'stone5')
            lay.set(X(i), top + v0 + 1 - off(i), 'stone3')
            lay.set(X(i), top + v0 + 2 - off(i), 'stone2')
        lay.set(X(1), top + v0 + 1 - off(1), 'stone6')
    # Торец створки на свободном крае — виден, когда створка повёрнута.
    if width < full:
        i = width - 1
        for v in range(hgt):
            lay.set(X(i), top + v - off(i), 'wood2')
        nx = X(i) + (1 if not mirror else -1)
        for v in range(hgt):
            lay.set(nx, top + v - off(i), 'wood5' if v == 0 else 'wood1')
    if broken:
        break_leaf(lay, X, width, top, hgt, seed=7 if mirror else 3)
    elif cracked:
        crack_leaf(lay, X, width, top, hgt)
    put(cv, lay, 'wood0')
    return cv


def crack_leaf(lay: Canvas, X: Callable[[int], int], width: int, top: int, hgt: int) -> None:
    """Повреждённая створка (до пролома): ломаные трещины по доскам и сколы на верхних торцах."""
    for pts in (((width - 4, 8), (width - 9, 13), (width - 7, 17), (width - 13, 22)),
                ((width - 3, 21), (width - 8, 25), (width - 6, 28)),
                ((6, 12), (10, 16), (9, 19))):
        # Светлый скол над тёмной трещиной — трещина читается и на 4×.
        for (i0, v0), (i1, v1) in zip(pts, pts[1:]):
            lay.line(X(i0) + 1, top + v0, X(i1) + 1, top + v1, 'wood6')
        for (i0, v0), (i1, v1) in zip(pts, pts[1:]):
            lay.line(X(i0), top + v0, X(i1), top + v1, 'ink2')
    # Сколы торцов: светлая щепа на верхней кромке.
    for i in (width - 5, width - 12, 15):
        lay.set(X(i), top, None)
        lay.set(X(i), top + 1, 'wood6')
        lay.set(X(i + 1), top, 'wood6')


def break_leaf(lay: Canvas, X: Callable[[int], int], width: int, top: int, hgt: int, seed: int) -> None:
    """Разбитая створка: рваная пробоина со щепой, отломанные верхушки досок, трещины."""
    rnd = random.Random(seed)
    # Зубчатый обломанный верх у свободного края.
    for i in range(width // 3, width):
        cut = int((i - width // 3) * 0.45) + rnd.randrange(0, 3)
        for v in range(0, cut):
            lay.set(X(i), top + v, None)
        if lay.opaque(X(i), top + cut):
            lay.set(X(i), top + cut, 'wood6')
    # Пробоина неправильной формы: многоугольник с шипами-щепками.
    hx, hy = width * 0.66, hgt * 0.45
    pts = []
    for k in range(11):
        a = 2 * math.pi * k / 11
        rr = (7.5 if k % 2 == 0 else 4.5) + rnd.uniform(-1, 1)
        pts.append((hx + rr * math.cos(a) * 0.9, hy + rr * math.sin(a)))
    for i in range(width):
        for v in range(hgt):
            if inside_poly(pts, i + 0.5, v + 0.5):
                lay.set(X(i), top + v, 'ink2')
    for i in range(width):
        for v in range(hgt):
            if lay.get(X(i), top + v)[:3] == rgb('ink2'):
                continue
            near = inside_poly(pts, i + 0.5, v - 0.5) or inside_poly(pts, i - 0.5, v + 0.5) or inside_poly(pts, i + 1.5, v + 0.5) or inside_poly(pts, i + 0.5, v + 1.5)
            if near and lay.opaque(X(i), top + v):
                lay.set(X(i), top + v, 'wood6' if v < hy else 'wood1')
    # Выбитая доска у петель: узкая щель снизу.
    for i in range(int(width * 0.2), int(width * 0.2) + 3):
        for v in range(hgt - 13, hgt - 3):
            lay.set(X(i), top + v, 'ink2')
        lay.set(X(i), top + hgt - 14, 'wood6')
    for (i0, v0, i1, v1) in ((3, 10, 9, 17), (4, 25, 11, 29), (width - 6, 26, width - 2, 31)):
        lay.line(X(i0), top + v0, X(i1), top + v1, 'wood1')


def gate_doors(state: str) -> Canvas:
    cv = Canvas(GW, GH)
    lx, rx = G_OPEN
    w, lift = {'closed': (45, 0), 'cracked': (45, 0), 'broken': (45, 0), 'open_0': (33, 4), 'open_1': (20, 7), 'open': (9, 9)}[state]
    cv.blit(gate_leaf(w, lift, lx, False, broken=state == 'broken', cracked=state == 'cracked'), 0, 0)
    cv.blit(gate_leaf(w, lift, rx, True, broken=state == 'broken', cracked=state == 'cracked'), 0, 0)
    if state in ('closed', 'cracked'):
        # Засов посередине.
        bar = blank(cv)
        bar.rect(72, 37, 16, 4, 'stone3')
        bar.hline(72, 87, 37, 'stone5')
        bar.hline(72, 87, 40, 'stone2')
        put(cv, bar, 'stone0')
        cv.rect(78, 35, 4, 8, 'stone0')
        cv.rect(79, 36, 2, 6, 'stone4')
        cv.set(79, 36, 'stone6')
    if state == 'broken':
        # Сломанный засов висит наискось, щепки на земле.
        bar = blank(cv)
        bar.polygon([(68, 39), (79, 44), (79, 47), (68, 42)], 'stone3')
        bar.shade('stone5', 'stone2', 'stone3')
        put(cv, bar, 'stone0')
        chips = blank(cv)
        for (x0, y0, x1, y1) in ((48, 61, 54, 59), (98, 59, 105, 61), (84, 61, 88, 60), (64, 62, 68, 61), (113, 61, 116, 62)):
            chips.line(x0, y0, x1, y1, 'wood5')
        put(cv, chips, 'wood0')
    return cv


def build_gate(reg) -> None:
    piv = (GW / 2.0, 0.0)
    reg.add('gate_frame', gate_frame(), pivot=piv, note='ворота 5×1: забор, столбы, фонари, вывеска')
    reg.add('gate_door_closed', gate_doors('closed'), pivot=piv, note='створки закрыты, засов')
    reg.add('gate_door_cracked', gate_doors('cracked'), pivot=piv, note='доп.: створки в трещинах (ворота повреждены, ещё стоят)')
    reg.add('gate_door_broken', gate_doors('broken'), pivot=piv, note='створки разбиты — ждут тапа')
    reg.add('gate_door_open_0', gate_doors('open_0'), pivot=piv, note='открываются, кадр 1')
    reg.add('gate_door_open_1', gate_doors('open_1'), pivot=piv, note='открываются, кадр 2')
    reg.add('gate_door_open', gate_doors('open'), pivot=piv, note='открыты: проход свободен')


# ───────── новый режим: барьер ворот на всю ширину шахты (ТЗ v2.1 §8.4) ─────────
# Закрытые ворота — граница по всей ширине: где боковой завал у ворот уже расчищен, клетку строки ворот
# закрывает пролёт забора того же рисунка, что боковые секции gate_frame (перекладины на тех же высотах,
# X-раскос), с провисшей цепью. Холсты 32×64 / 16×64, ноги на нижней кромке клетки, как у ворот.
GB_RAILS = ((26, 6), (49, 6))       # y, высота перекладин (как hbeam в gate_frame)


def _rail(cv: Canvas, y: int, h: int, x0: int = 0, x1: int = 31, seed: int = 0) -> None:
    """Перекладина без торцевого контура — стыкуется с соседним пролётом и с крайним столбом ворот."""
    m = WOOD
    cv.hline(x0, x1, y, m['o'])
    cv.hline(x0, x1, y + h - 1, m['o'])
    cv.rect(x0, y + 1, x1 - x0 + 1, h - 2, m['b'])
    cv.hline(x0, x1, y + 1, m['h'])
    cv.hline(x0, x1, y + 2, m['l'])
    cv.hline(x0, x1, y + h - 2, m['d'])
    grain_h(cv, x0 + 1, y + 3, x1 - x0 - 1, h - 5, m['d'], seed)


def _chain(cv: Canvas, x0: int, x1: int, y0: int, sag: float) -> None:
    """Провисшая цепь: звенья анфас/в профиль по очереди (как chain_hang у стен)."""
    for x in range(x0, x1 + 1):
        t = (x - x0) / float(max(1, x1 - x0))
        y = int(round(y0 + sag * 4 * t * (1 - t)))
        k = (x - x0) % 4
        if k in (0, 2):
            cv.set(x, y, 'stone6' if k == 0 else 'stone4')
            cv.set(x, y + 1, 'stone3')
        elif k == 1:
            cv.set(x, y - 1, 'stone5')
            cv.set(x, y + 1, 'stone2')
        else:
            cv.set(x, y, 'stone4')
        if k == 1:
            cv.set(x, y + 2, 'ink2')


def gate_barrier_mid() -> Canvas:
    """Пролёт забора 1 клетка, бесшовно по горизонтали: две перекладины, X-раскос, провисшая цепь."""
    cv = Canvas(32, 64)
    xb = blank(cv)
    brace(xb, 1, 31, 30, 45, 4, WOOD)
    brace(xb, 1, 45, 30, 31, 4, WOOD)
    clip(xb, lambda x, y: 31 <= y <= 49)
    cv.blit(xb, 0, 0)
    for k, (y, h) in enumerate(GB_RAILS):
        _rail(cv, y, h, seed=140 + k)
    _chain(cv, 0, 31, 33, 5.0)
    return cv


def gate_barrier_post() -> Canvas:
    """Столб пролёта 16×64 (ставится на границу клеток: у стены или между пролётами), как крайний столб ворот:
    болты на высоте перекладин, светлый торец, железный хомут с кольцом для цепи."""
    cv = Canvas(16, 64)
    vbeam(cv, 4, 21, 7, G_GROUND - 21 + 1, WOOD, seed=150)
    bolt(cv, 6, 28, big=True)
    bolt(cv, 6, 51, big=True)
    cv.rect(4, 34, 7, 2, 'stone3')
    cv.hline(4, 10, 34, 'stone5')
    cv.set(7, 36, 'stone5')
    cv.set(6, 37, 'stone4')
    cv.set(8, 37, 'stone2')
    cv.set(7, 38, 'stone2')
    return cv


def gate_barrier_broken() -> Canvas:
    """Пролёт после открытия ворот: перекладины переломлены и провисли, раскос выбит, цепь лежит на земле, щепки."""
    cv = Canvas(32, 64)
    # Обломки перекладин: левая половина свисает вниз-вправо, правая — вниз-влево.
    for k, (y, h) in enumerate(GB_RAILS):
        lay = blank(cv)
        lay.polygon([(0, y), (11, y + 2 + k), (10, y + h + 2 + k), (0, y + h - 1)], 'wood4')
        lay.polygon([(31, y), (21, y + 3), (22, y + h + 3), (31, y + h - 1)], 'wood4')
        lay.shade('wood5', 'wood2', 'wood4')
        put(cv, lay, 'wood0')
        # Рваные концы — светлая щепа.
        for x, yy in ((11, y + 3 + k), (10, y + 5 + k), (21, y + 4), (22, y + 6)):
            cv.set(x, yy, 'wood6')
    # Обломок раскоса на земле.
    lay = blank(cv)
    lay.polygon([(5, 56), (8, 53), (24, 57), (22, 60)], 'wood4')
    lay.shade('wood5', 'wood2', 'wood4')
    put(cv, lay, 'wood0')
    _chain(cv, 3, 28, 60, 1.0)
    chips = blank(cv)
    for (x0, y0, x1, y1) in ((2, 62, 6, 61), (26, 61, 30, 62), (14, 62, 17, 61)):
        chips.line(x0, y0, x1, y1, 'wood5')
    put(cv, chips, 'wood0')
    return cv


# ───────── новый режим: богатое месторождение s добывается командой (ТЗ v2.1 §8.2) ─────────
# Трещины-наложения на shaft_base по стадиям HP и завал после расчистки. Трещина рисуется только по непрозрачным
# пикселям базы; светлая кромка скола берётся из материала под ней (камень, дерево, кровля).
SHAFT_CRACKS = [
    # стадия 1: скалы плеч и вывеска
    [[(6, 46), (8, 50), (6, 54), (8, 58), (6, 61)], [(56, 38), (58, 42), (57, 46), (59, 49)], [(30, 26), (32, 30), (31, 33)],
     [(3, 70), (6, 73), (5, 77)]],
    # стадия 2: + стойки, кровля, нижние валуны
    [[(52, 60), (55, 64), (53, 68), (56, 72)], [(11, 64), (9, 68), (12, 72)], [(14, 44), (13, 50), (15, 56)],
     [(49, 46), (50, 52), (48, 58)], [(18, 28), (20, 31)], [(44, 29), (46, 32)]],
    # стадия 3: + глубокие трещины через всю скалу и верхняк входа
    [[(3, 36), (6, 40), (4, 44)], [(60, 50), (58, 54), (61, 58)], [(10, 35), (16, 37), (24, 36)], [(40, 37), (46, 36), (54, 38)],
     [(36, 24), (38, 28), (36, 31)]],
]
CRACK_LIGHT = {'navy': 'navy5', 'wood': 'wood6', 'red': 'red4', 'stone': 'stone5', 'green': 'green4'}


def shaft_crack(level: int) -> Canvas:
    base = shaft_base()
    cv = Canvas(SW, SH)
    marks = set()
    for st in range(level):
        for pl in SHAFT_CRACKS[st]:
            for (ax, ay), (bx, by) in zip(pl, pl[1:]):
                n = max(abs(bx - ax), abs(by - ay)) + 1
                for k in range(n):
                    marks.add((round(ax + (bx - ax) * k / max(1, n - 1)), round(ay + (by - ay) * k / max(1, n - 1))))
    marks |= {(x + 1, y) for x, y in marks if (x * 3 + y) % (4 if level >= 2 else 3) == 0}
    names = {v: k for k, v in PALETTE.items()}
    for x, y in marks:
        if base.opaque(x, y):
            cv.set(x, y, 'ink1')
    for x, y in marks:
        q = (x + 1, y)
        if q in marks or not base.opaque(*q):
            continue
        nm = names.get(base.get(*q)[:3], '')
        ramp = ''.join(ch for ch in nm if not ch.isdigit())
        if ramp in CRACK_LIGHT and (x + y) % 2 == 0:
            cv.set(q[0], q[1], CRACK_LIGHT[ramp])
    if level == 3:
        # Осыпь у плеч и сбитая доска кровли.
        for i, (bx, by, rx, ry) in enumerate([(4, 84, 4, 2.5), (60, 84, 4, 2.5), (14, 86, 3, 2), (50, 86, 3, 2)]):
            rock(cv, bx, by, rx, ry, ROCK, seed=600 + i)
        cv.rect(46, 29, 6, 2, 'ink2')
        cv.hline(46, 51, 31, 'red4')
    return cv


def shaft_cleared() -> Canvas:
    """После расчистки (клетки 2×3 свободны): низкая осыпь по следу копра — камни, переломленные стойки,
    обломки красной кровли и опрокинутая вагонетка. Не выше 10 px над полом — котики проходят поверх."""
    cv = Canvas(SW, SH)
    # Обломки досок и стоек.
    for (pts, m) in (([(8, 52), (12, 49), (30, 58), (27, 61)], WOOD), ([(36, 76), (40, 73), (58, 80), (55, 83)], WOOD),
                     ([(18, 92), (22, 90), (34, 95), (31, 98)], DWOOD)):
        lay = blank(cv)
        lay.polygon(pts, m['b'])
        lay.shade(m['l'], m['d'], m['b'])
        put(cv, lay, m['o'])
    for (pts) in ([(40, 44), (52, 42), (53, 46), (41, 48)], [(6, 84), (16, 82), (16, 85), (7, 87)]):
        lay = blank(cv)
        lay.polygon(pts, 'red3')
        lay.shade('red4', 'red2', 'red3')
        put(cv, lay, 'red0')
    for i, (bx, by, rx, ry) in enumerate([(14, 40, 6, 4), (22, 44, 4, 3), (50, 58, 6, 4), (44, 62, 4, 3), (12, 70, 5, 3.5),
                                          (46, 96, 6, 4), (54, 100, 4, 3), (24, 104, 5, 3)]):
        rock(cv, bx, by, rx, ry, ROCK, seed=620 + i)
    # Половина красного колеса копра.
    wh = blank(cv)
    for k in range(40):
        ang = math.pi * (0.1 + 0.8 * k / 39.0)
        for rr, c in ((7.0, 'red3'), (6.0, 'red2')):
            wh.set(int(round(34 + rr * math.cos(ang))), int(round(76 - rr * math.sin(ang) * 0.8)), c)
    wh.line(34, 76, 30, 71, 'stone4')
    wh.line(34, 76, 38, 71, 'stone4')
    wh.hline(26, 42, 77, 'stone3')
    put(cv, wh, 'red0')
    return cv


# ───────────────────────── выход 96×80 ─────────────────────────
EW, EH = 96, 80
E_OPEN = (20, 14, 56, 58)    # проём: x, y, w, h
E_GROUND = 72
E_VANISH = (48, 40)          # точка схода туннеля


def exit_mask(x: float, y: float) -> bool:
    ex, ey, ew, eh = E_OPEN
    return ex <= x < ex + ew and ey <= y < ey + eh


def exit_frames() -> List[Tuple[int, int, int, int]]:
    """Рамы крепи в глубине: (x0, y0, x1, y1) — дальше, меньше и выше низом."""
    ex, ey, ew, eh = E_OPEN
    out = []
    for inset in (7, 13, 18):
        out.append((ex + inset, ey + inset, ex + ew - 1 - inset, E_GROUND - inset * 4 // 5))
    return out


def exit_sign(cv: Canvas) -> None:
    """Подвесная табличка «?» на цепочках под верхняком."""
    for sx in (41, 54):
        for yy in range(14, 19):
            cv.set(sx, yy, 'stone2' if yy % 2 else 'stone5')
    sign = blank(cv)
    sign.rect(37, 19, 22, 15, 'wood5')
    sign.hline(37, 58, 19, 'wood6')
    sign.vline(37, 19, 33, 'wood6')
    sign.hline(37, 58, 33, 'wood3')
    sign.vline(58, 20, 33, 'wood4')
    sign.hline(38, 57, 26, 'wood4')
    put(cv, sign, 'wood0')
    q = Canvas.from_ascii([
        '.xxxx.',
        'xx..xx',
        '....xx',
        '...xx.',
        '..xx..',
        '......',
        '..xx..',
    ], {'x': 'navy2'})
    cv.blit(q, 45, 23)
    for bx, by in ((39, 21), (56, 21)):
        bolt(cv, bx, by)


def exit_base() -> Canvas:
    cv = Canvas(EW, EH)
    ex, ey, ew, eh = E_OPEN
    vx, vy = E_VANISH
    # Каменное обрамление портала (камень пещеры, как задняя стена): тёмная масса и валуны поверх.
    mass = blank(cv)
    mass.polygon([(0, 6), (8, 0), (88, 0), (96, 6), (96, 76), (84, 77), (84, 10), (12, 10), (12, 77), (0, 76)], 'navy1')
    put(cv, mass, 'navy0')
    for i, (bx, by, rx, ry, moss) in enumerate([
        (9, 11, 9, 8, True), (27, 4, 9, 5, False), (48, 3, 11, 5, True), (69, 4, 9, 5, False), (87, 11, 9, 8, False),
        (6, 29, 6, 9, False), (90, 29, 6, 9, True), (6, 48, 6, 9, True), (90, 48, 6, 9, False),
        (7, 67, 7, 6, False), (89, 67, 7, 6, True),
    ]):
        rock(cv, bx, by, rx, ry, ROCK, seed=60 + i * 2, moss=moss)
    # Темнота штольни.
    cv.rect(ex, ey, ew, eh, 'ink2')
    floor = blank(cv)
    floor.polygon([(ex, E_GROUND), (vx - 8, vy + 10), (vx + 8, vy + 10), (ex + ew, E_GROUND)], 'navy1')
    floor.polygon([(ex + 8, E_GROUND), (vx - 4, vy + 14), (vx + 4, vy + 14), (ex + ew - 8, E_GROUND)], 'navy2')
    cv.blit(floor, 0, 0)
    for k, (fx0, fy0, fx1, fy1) in enumerate(exit_frames()):
        c1, c2 = (('wood2', 'wood1'), ('wood1', 'ink3'), ('ink3', 'ink2'))[k]
        th = 3 if k == 0 else 2
        fr = blank(cv)
        fr.rect(fx0, fy0, th, fy1 - fy0, c1)
        fr.rect(fx1 - th + 1, fy0, th, fy1 - fy0, c1)
        fr.rect(fx0, fy0, fx1 - fx0 + 1, th, c1)
        fr.vline(fx1 - th + 1, fy0 + th, fy1 - 1, c2)
        fr.hline(fx0, fx1, fy0 + th - 1, c2)
        cv.blit(fr, 0, 0)
    # Самая глубина — арка темноты.
    deep = blank(cv)
    deep.rect(vx - 7, vy - 4, 14, 14, 'ink0')
    deep.ellipse(vx, vy - 4, 7, 5, 'ink0')
    cv.blit(deep, 0, 0)
    rails(cv, vy + 10, E_GROUND - 1, 46, 50, 38.5, 57.5, [vy + 13, vy + 18, vy + 25], far=True)
    rails(cv, E_GROUND, EH - 1, 38.5, 57.5, 36.5, 59.5, [E_GROUND + 2])
    # Портал: стойки, подкосы, верхняк.
    vbeam(cv, ex - 8, 10, 8, E_GROUND - 10 + 1, WOOD, seed=70)
    vbeam(cv, ex + ew, 10, 8, E_GROUND - 10 + 1, WOOD, seed=71)
    brace(cv, ex, 20, ex + 8, 13, 3, WOOD)
    brace(cv, ex + ew - 9, 13, ex + ew - 1, 20, 3, WOOD)
    hbeam(cv, 8, 6, 80, 8, WOOD, seed=72)
    for bx in (ex - 6, ex + ew + 2):
        bolt(cv, bx, 9, big=True)
        bolt(cv, bx, 66, big=True)
    exit_sign(cv)
    return cv


def exit_boards() -> Canvas:
    """Заколоченный вход: доски внахлёст, раскос, камни у основания; табличка «?» остаётся поверх."""
    cv = Canvas(EW, EH)
    ex, ey, ew, eh = E_OPEN
    for i, (y0, y1, h, seed) in enumerate(((21, 23, 6, 1), (32, 30, 6, 2), (42, 44, 6, 3), (53, 52, 6, 4))):
        brace(cv, ex - 4, y0, ex + ew + 3, y1, h, WOOD)
        bolt(cv, ex - 2, y0 + 2)
        bolt(cv, ex + ew + 1, y1 + 2)
    brace(cv, ex + 3, 56, ex + ew - 4, 19, 5, DWOOD)
    for i, (bx, by, rx, ry) in enumerate([(30, 66, 8, 6), (48, 68, 9, 6), (67, 66, 8, 6), (38, 73, 6, 4), (58, 74, 6, 4), (22, 72, 5, 4), (74, 73, 5, 4)]):
        rock(cv, bx, by, rx, ry, STONE, seed=80 + i)
    exit_sign(cv)
    return cv


def exit_glow() -> Canvas:
    """Свет в конце туннеля: ступени по рамам крепи (ярче вглубь), тёплое пятно на рельсах у входа."""
    cv = Canvas(EW, EH)
    ex, ey, ew, eh = E_OPEN
    vx, vy = E_VANISH
    frames = exit_frames()
    lay = blank(cv)
    lay.rect(ex, ey, ew, eh, with_alpha('amber1', 100))
    for (fx0, fy0, fx1, fy1), (c, a) in zip(frames, (('amber2', 120), ('amber3', 150), ('amber4', 185))):
        lay.rect(fx0, fy0, fx1 - fx0 + 1, fy1 - fy0, with_alpha(c, a))
    # Светлый конец туннеля — арка по форме дальней рамы.
    glow(lay, lambda x, y, k: (abs(x - vx) <= 6 and vy - 2 <= y <= vy + 9) or in_ellipse(vx, vy - 2, 6, 5)(x, y, k), [(1.0, 'amber5', 215)])
    cv.blit(lay, 0, 0)
    spill = blank(cv)
    glow(spill, in_ellipse(vx, E_GROUND, 30, 8), [(1.0, 'amber2', 80), (0.6, 'amber3', 105)])
    clip(spill, lambda x, y: y >= E_GROUND)
    cv.blit(spill, 0, 0)
    return cv


def build_exit(reg) -> None:
    piv = (EW / 2.0, 0.0)
    reg.add('exit_base', exit_base(), pivot=piv, note='выход 3×1: портал штольни, рельсы в темноту, табличка «?»')
    reg.add('exit_boards', exit_boards(), pivot=piv, note='заколоченный вход (табличка «?» повторена поверх досок)')
    reg.add('exit_glow', exit_glow(), pivot=piv, alpha=True, note='тёплый свет открытого выхода')


# ───────────────────────── пушка у кузницы 32×72 ─────────────────────────
# Пушка (ТЗ 7.2): из неё вылетают бочки с котиками. Footprint 1×2 справа от кузницы (64 px) и 8 px над ним, ноги (16, 0).
# Вид 3/4: ствол смотрит вверх-вправо, на зону мержа; влево пушка разворачивается отражением по X (код Unity).
# Слои на одном холсте и с одним pivot, снизу вверх: лафет (дальнее колесо, хобот, щека) → ствол (отдача) →
# ближнее колесо (2 кадра: при откате колесо проворачивается). Ствол чугунный с золотыми кольцами и лапкой:
# тёмный на сине-сером полу, не похож ни на камни, ни на кузницу.
KW, KH = 32, 72
K_B = (10.5, 51.5)         # ось ствола: центр казённой части
K_M = (26.5, 33.5)         # ось ствола: центр дульного среза (ствол ≈48° над горизонталью: пушка смотрит вбок)
K_HB, K_HM = 6.6, 5.0      # полуширина ствола у казны и у дула (перед утолщением среза)
K_SWELL = 4.0              # длина дульного утолщения, px
K_W = (18.5, 59.0)         # центр ближнего колеса
K_WR = (9.0, 10.0)         # полуоси ближнего колеса
K_FAR = (6.5, 55.0)        # центр дальнего колеса (за лафетом)
RED = dict(o='red0', s='red1', d='red2', b='red3', l='red4', h='red5', hh='red6')
# Чугун: тёмный ствол с ярким бликом — классическая пушка, читается на сине-сером полу темнотой.
IRON_DARK = dict(o='ink0', s='ink2', d='ink3', b='stone1', l='stone2', h='stone4', hh='stone5')
GOLD = dict(o='gold0', s='gold1', d='gold2', b='gold3', l='gold4', h='gold5', hh='gold5')


def cannon_axis() -> Tuple[float, float, float, float, float]:
    """Ось ствола: начало (казна), единичное направление к дулу и длина."""
    bx, by = K_B
    mx, my = K_M
    ln = math.hypot(mx - bx, my - by)
    return bx, by, (mx - bx) / ln, (my - by) / ln, ln


# Центр дульного среза — пиксель холста от левого верхнего угла (Unity: CannonView.MuzzlePixel).
CANNON_MUZZLE = (int(K_M[0]), int(K_M[1]))


def _tone(m: Dict[str, str], t: float) -> str:
    """Цилиндр при свете сверху-слева: t — поперечная координата от −1 (освещённый край) до 1 (тень)."""
    if t < -0.62:
        return m['h']
    if t < -0.2:
        return m['l']
    if t < 0.38:
        return m['b']
    if t < 0.74:
        return m['d']
    return m['s']


def cannon_barrel(body: Dict[str, str] = None) -> Canvas:
    """Ствол: конус от казны к дулу, золотые кольца, лапка на стволе, дульный срез с тёмным каналом."""
    body = body or IRON_DARK
    cv = Canvas(KW, KH)
    bx, by, dx, dy, ln = cannon_axis()
    nx, ny = -dy, dx               # поперёк оси: к теневой (правой нижней) стороне

    def half(u: float) -> float:
        if u < 0:
            return math.sqrt(max(0.0, K_HB * K_HB - u * u))     # скруглённая казна
        if u >= ln - K_SWELL:
            return K_HM + 1.4                                   # дульное утолщение
        return K_HB + (K_HM - K_HB) * u / (ln - K_SWELL)

    def ring(u: float) -> bool:
        return 0.12 * ln <= u <= 0.21 * ln or 0.56 * ln <= u <= 0.62 * ln or u >= ln - K_SWELL

    lay = blank(cv)
    for y in range(KH):
        for x in range(KW):
            px, py = x + 0.5 - bx, y + 0.5 - by
            u = px * dx + py * dy
            v = px * nx + py * ny
            if u < -K_HB or u > ln:
                continue
            w = half(u)
            if w <= 0.3 or abs(v) > w:
                continue
            lay.set(x, y, _tone(GOLD if ring(u) else body, v / w))
    # Блик вдоль ствола на освещённой стороне (между кольцами).
    for i in range(int(0.24 * ln), int(0.53 * ln)):
        x = bx + dx * i + nx * (-0.5 * half(i))
        y = by + dy * i + ny * (-0.5 * half(i))
        if lay.opaque(int(x), int(y)):
            lay.set(int(x), int(y), body['hh'])
    # Шишка казны (винград) — позади казённой части.
    kx, ky = bx - dx * (K_HB + 1.0), by - dy * (K_HB + 1.0)
    knob = blank(cv)
    knob.ellipse(kx, ky, 2.0, 2.0, GOLD['b'])
    knob.set(int(kx) - 1, int(ky) - 1, GOLD['h'])
    knob.set(int(kx), int(ky), GOLD['d'])
    lay.blit(knob, 0, 0)
    # Лапка на стволе: золотой отпечаток 5×4 между кольцами (четыре пальчика и подушечка).
    u = 0.39 * ln
    cx, cy = int(bx + dx * u + nx * 0.5), int(by + dy * u + ny * 0.5)
    for row, line in enumerate(('.x.x.', 'x...x', '.xxx.', '.xxx.')):
        for col, ch in enumerate(line):
            if ch == 'x':
                lay.set(cx - 2 + col, cy - 2 + row, GOLD['h'] if row < 2 else GOLD['l'])
    # Дульный срез: золотой обод-эллипс поперёк оси и тёмный канал (смотрит вверх-вправо, от зрителя).
    mx, my = bx + dx * ln, by + dy * ln
    for y in range(KH):
        for x in range(KW):
            px, py = x + 0.5 - mx, y + 0.5 - my
            u = px * dx + py * dy
            v = px * nx + py * ny
            rim = (u / 2.6) ** 2 + (v / (K_HM + 1.5)) ** 2
            if rim > 1.0:
                continue
            bore = (u / 1.8) ** 2 + (v / (K_HM - 0.6)) ** 2
            if bore <= 1.0:
                lay.set(x, y, 'ink2' if v > 1.5 else 'ink1')
            else:
                lay.set(x, y, GOLD['h'] if v < 0 else GOLD['l'])
    put(cv, lay, body['o'])
    return cv


def _wheel(lay: Canvas, cx: float, cy: float, rx: float, ry: float, spoke0: float, m: Dict[str, str], tire: Dict[str, str]) -> None:
    """Колесо со спицами (вид 3/4): железная шина, деревянный обод, тёмная глубина между спицами, ступица."""
    lay.ellipse(cx, cy, rx, ry, tire['b'])
    lay.ellipse(cx, cy, rx - 1.3, ry - 1.3, m['l'])
    lay.ellipse(cx, cy, rx - 2.6, ry - 2.6, m['s'])
    for k in range(6):
        a = math.radians(spoke0 + k * 60)
        x1 = cx + (rx - 2.0) * math.cos(a)
        y1 = cy + (ry - 2.0) * math.sin(a)
        n = 12
        for i in range(n + 1):
            f = i / n
            lay.set(int(math.floor(cx + (x1 - cx) * f)), int(math.floor(cy + (y1 - cy) * f)), m['b'] if a % (2 * math.pi) < math.pi else m['l'])
    # Блик шины сверху-слева, тень снизу-справа.
    for y in range(lay.h):
        for x in range(lay.w):
            ex, ey = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            r = ex * ex + ey * ey
            if r > 1.0 or r < ((rx - 1.3) / rx) ** 2 * 0.98:
                continue
            lum = -0.7 * ex - 0.7 * ey
            if lum > 0.45:
                lay.set(x, y, tire['h'])
            elif lum < -0.45:
                lay.set(x, y, tire['d'])


def cannon_base() -> Canvas:
    """Лафет: дальнее колесо (в тени), хобот до земли слева внизу, щека под стволом с железной цапфой."""
    cv = Canvas(KW, KH)
    far = blank(cv)
    _wheel(far, K_FAR[0], K_FAR[1], 5.5, 8.0, 15, dict(s='wood1', b='wood2', l='wood3'), dict(b='stone2', h='stone3', d='stone1'))
    put(cv, far, 'wood0')
    trail = blank(cv)
    trail.polygon([(1.5, 66.0), (5.0, 70.5), (19.0, 59.0), (14.5, 53.0)], 'wood4')
    trail.shade('wood5', 'wood2', 'wood4')
    trail.set(3, 67, 'stone4')            # железное кольцо на конце хобота
    trail.set(4, 67, 'stone5')
    trail.set(3, 68, 'stone2')
    put(cv, trail, 'wood0')
    cheek = blank(cv)
    cheek.polygon([(10.0, 57.0), (19.0, 47.0), (26.5, 50.0), (26.0, 58.0), (13.0, 61.0)], 'wood4')
    grain_v(cheek, 16, 51, 9, 8, 'wood3', seed=11, step=3)
    cheek.shade('wood6', 'wood2', 'wood4')
    put(cv, cheek, 'wood0')
    # Цапфа: железная накладка поверх щеки, где лежит ствол.
    cap = blank(cv)
    cap.rect(20, 48, 4, 3, 'stone4')
    cap.hline(20, 23, 48, 'stone6')
    cap.set(23, 50, 'stone2')
    put(cv, cap, 'stone0')
    return cv


def cannon_wheel(frame: int) -> Canvas:
    """Ближнее колесо: кадр 1 — спицы повёрнуты на 30° (колесо проворачивается при откате)."""
    cv = Canvas(KW, KH)
    lay = blank(cv)
    _wheel(lay, K_W[0], K_W[1], K_WR[0], K_WR[1], 30 * frame, dict(s='wood2', b='wood4', l='wood5'), dict(b='stone4', h='stone6', d='stone2'))
    hub = blank(cv)
    hub.ellipse(K_W[0], K_W[1], 2.4, 2.4, 'gold3')
    hub.set(int(K_W[0]) - 1, int(K_W[1]) - 1, 'gold5')
    hub.set(int(K_W[0]), int(K_W[1]), 'gold1')
    lay.blit(hub, 0, 0)
    put(cv, lay, 'ink1')
    return cv


def build_cannon(reg) -> None:
    piv = (KW / 2.0, 0.0)
    reg.add('cannon_base', cannon_base(), pivot=piv, note='пушка 1×2: лафет, хобот и дальнее колесо')
    reg.add('cannon_barrel', cannon_barrel(), pivot=piv, note=f'ствол (отдача — сдвиг слоя); центр дульного среза {CANNON_MUZZLE}')
    for i in range(2):
        reg.add(f'cannon_wheel_{i}', cannon_wheel(i), pivot=piv, note='ближнее колесо поверх ствола; кадр 1 — поворот при откате')


# Mortar layers share a 28×24 canvas and feet pivot (14, 0).
# Canvas axis from breech (12, 15) to muzzle (15, 4): ~15.3° right of vertical.
MORTAR_MUZZLE = (15, 4)
MORTAR_AXIS = (3 / math.sqrt(130), -11 / math.sqrt(130))


def mortar_base() -> Canvas:
    cv = Canvas(28, 24)
    bed = blank(cv)
    bed.polygon([(3, 17), (7, 14), (22, 14), (25, 17), (25, 21),
                 (22, 23), (4, 23), (2, 21)], 'wood2')
    bed.polygon([(3, 17), (7, 14), (22, 14), (24, 17)], 'wood5')
    bed.rect(3, 18, 21, 4, 'wood4')
    bed.hline(4, 23, 18, 'wood6')
    bed.hline(5, 22, 21, 'wood3')
    bed.line(9, 19, 17, 19, 'wood2')
    bed.hline(15, 20, 20, 'wood5')
    put(cv, bed, 'wood0')
    for x in (5, 20):
        cv.rect(x, 16, 3, 7, 'stone0')
        cv.rect(x, 16, 2, 6, 'stone3')
        cv.vline(x, 16, 20, 'stone5')
        cv.set(x+1, 18, 'gold4')
    cv.ellipse(13, 16, 7, 3, 'stone0')
    cv.ellipse(13, 15, 6, 2, 'stone3')
    cv.hline(9, 15, 14, 'stone5')
    return cv


def mortar_tube() -> Canvas:
    cv = Canvas(28, 24)
    body = blank(cv)
    dx, dy = MORTAR_AXIS
    # Draw directly in axis coordinates: no raster rotation or antialiasing.
    for y in range(2, 19):
        for x in range(5, 23):
            vx, vy = x - 12, y - 15
            along = vx*dx + vy*dy
            across = vx*(-dy) + vy*dx
            if -2 <= along <= math.sqrt(130) and abs(across) <= 5.4:
                col = _tone(IRON_DARK, across/5.4)
                if -0.5 <= along <= 1.5 or 8 <= along <= 10:
                    col = _tone(GOLD, across/5.4)
                body.set(x, y, col)
    put(cv, body, 'ink1')
    # Broad elliptical lip, a dark bore, and a highlight on the upper-left rim.
    cv.ellipse(15, 4, 6, 3, 'gold0')
    cv.ellipse(15, 3.5, 5, 2.5, 'gold3')
    cv.ellipse(15, 4, 4, 1.5, 'ink0')
    cv.hline(12, 16, 1, 'gold5')
    cv.hline(12, 17, 5, 'stone2')
    cv.set(11, 3, 'gold4')
    return cv


def build_mortar(reg) -> None:
    piv = (14.0, 0.0)
    reg.add('mortar_base', mortar_base(), pivot=piv, note='28×24 wooden mortar bed with iron cradle; feet')
    reg.add('mortar_tube', mortar_tube(), pivot=piv,
            note=f'28×24 recoil overlay; muzzle {MORTAR_MUZZLE} top-left; axis {MORTAR_AXIS}')


def build_rework(reg) -> None:
    """Новый режим: барьер ворот на всю ширину, стадии повреждения месторождения и завал после расчистки."""
    reg.add('gate_barrier_mid', gate_barrier_mid(), pivot=(16.0, 0.0), note='пролёт барьера закрытых ворот, 1 клетка, бесшовно')
    reg.add('gate_barrier_post', gate_barrier_post(), pivot=(8.0, 0.0), note='столб барьера: на границе клеток')
    reg.add('gate_barrier_broken', gate_barrier_broken(), pivot=(16.0, 0.0), note='пролёт барьера после открытия ворот')
    piv = (SW / 2.0, 0.0)
    for level in (1, 2, 3):
        reg.add(f'shaft_crack_{level}', shaft_crack(level), pivot=piv, note=f'трещины месторождения, стадия {level}')
    reg.add('shaft_cleared', shaft_cleared(), pivot=piv, note='след расчищенного месторождения 2×3 (пол, проходимо)')


def build(reg) -> None:
    build_containers(reg)
    build_cannon(reg)
    build_mortar(reg)
    build_forge(reg)
    build_shaft(reg)
    build_gate(reg)
    build_exit(reg)
    build_rework(reg)
