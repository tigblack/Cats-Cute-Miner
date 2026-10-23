"""Добываемые камни и препятствия (группа rocks): валуны с рудой, кристаллами, сундуками и жетоном,
наложения трещин, автотайлы каменных массивов, одиночные препятствия, столбы у ворот и водопад.

Минимальные камни — серо-фиолетовый гранит (рампа stone) с рудой; препятствия — тёмно-синие пещерные глыбы
(рампа navy) без руды, с мхом. Кристаллы, самородки и цветные жетоны на клетках поля — только у добываемых
камней (rock_*): у препятствий (block_*) их нет, иначе препятствие читается как цель. Свет сверху-слева,
контур — тёмный конец рампы материала.
Холсты камней и одиночных препятствий 32×40 (клетка + 8 px сверху), pivot «ноги» (16, 0).
Макеты в контексте: Temp/pixel_rocks/scene.py (там же проверка бесшовности массивов).
"""
from __future__ import annotations

import math
import random
from typing import Dict, List, Optional

from pxl import Canvas

RW, RH = 32, 40          # холст камня: клетка 32×32 + 8 px над ней
BASE = 36                # нижний ряд тела валуна (контур — строка ниже); ниже — камешки у подножия

Mask = List[List[bool]]


def _outline_mask(cv: Canvas, m: Mask, c) -> None:
    """Контур 1 px снаружи маски (только по 4 соседям — без «лесенки» по диагонали)."""
    h, w = len(m), len(m[0])
    for y in range(h):
        for x in range(w):
            if m[y][x]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                xx, yy = x + dx, y + dy
                if 0 <= xx < w and 0 <= yy < h and m[yy][xx]:
                    cv.set(x, y, c)
                    break


# ───────────────────── куча глыб (общий рендер камней) ─────────────────────
def poly_pts(cx: float, cy: float, rx: float, ry: float, n: int = 8, seed: int = 0, jit: float = 0.16, rot: float = 0.3,
             narrow: float = 0.0):
    """Неровный многоугольник вокруг эллипса: «колотая» глыба с плоскими гранями.
    narrow > 0 сужает верх (купол-«стог», как у камней концепта)."""
    r = random.Random(seed)
    pts = []
    for i in range(n):
        a = rot + 2 * math.pi * i / n
        k = 1.0 + r.uniform(-jit, jit)
        sx = 1.0 - narrow * max(0.0, -math.sin(a))
        pts.append((cx + math.cos(a) * rx * k * sx, cy + math.sin(a) * ry * k))
    return pts


def shape_mask(w: int, h: int, shape, base: Optional[int] = None) -> Mask:
    if shape[0] == 'mask':      # готовая маска (часть глыбы, например половина расколотого валуна)
        m = shape[1]
        return [[m[y][x] and (base is None or y <= base) for x in range(w)] for y in range(h)]
    tmp = Canvas(w, h)
    if shape[0] == 'poly':
        tmp.polygon(shape[1], 'white')
    else:
        tmp.ellipse(shape[1], shape[2], shape[3], shape[4], 'white')
    return [[tmp.opaque(x, y) and (base is None or y <= base) for x in range(w)] for y in range(h)]


# Роли тонов глыбы: щель, тень, тело, переход, макушка, кромка-блик.
STONE_PAL = dict(crev='stone0', shade='stone1', body='stone2', mid='stone3', top='stone4', rim='stone5', glint='stone6', out='stone0')


def pile(cv: Canvas, chunks, pal=STONE_PAL, base: Optional[int] = None, outline: bool = True, union: Optional[Mask] = None) -> Mask:
    """Куча глыб: глыбы рисуются от задних к передним, у каждой своя освещённая макушка (свет сверху-слева),
    тень снизу-справа и тёмная щель там, где она перекрывает уже нарисованные глыбы.
    Ключи глыбы: shape, cap (толщина светлой макушки), facets/fseed (плоские грани со сколами), glint (блик
    у макушки), ao (тёмные нижние ряды), shade_th (порог тени), base (обрезка снизу)."""
    w, h = cv.w, cv.h
    if union is None:
        union = [[False] * w for _ in range(h)]
    for ch in chunks:
        m = shape_mask(w, h, ch['shape'], ch.get('base', base))
        xs = [x for y in range(h) for x in range(w) if m[y][x]]
        ys = [y for y in range(h) for x in range(w) if m[y][x]]
        if not xs:
            continue
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        cx, cy = (x0 + x1 + 1) / 2.0, (y0 + y1 + 1) / 2.0
        rx, ry = max(1.0, (x1 - x0 + 1) / 2.0), max(1.0, (y1 - y0 + 1) / 2.0)
        cap = ch.get('cap', max(2, round((y1 - y0 + 1) * 0.34)))
        top_of = {}
        for x in range(x0, x1 + 1):
            col = [y for y in range(y0, y1 + 1) if m[y][x]]
            if col:
                top_of[x] = col[0]
        # щель: кольцо вокруг глыбы по уже нарисованному камню
        for y in range(h):
            for x in range(w):
                if m[y][x] or not union[y][x]:
                    continue
                if any(0 <= x + dx < w and 0 <= y + dy < h and m[y + dy][x + dx] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    cv.set(x, y, pal['crev'])
        # грани: внутри глыбы плоскости чуть светлее/темнее, на границе — тонкая линия скола
        fac = ch.get('facets', 0)
        rnd = random.Random(ch.get('fseed', 7))
        seeds = []
        for i in range(fac):
            for _ in range(40):
                sx, sy = rnd.uniform(x0, x1 + 1), rnd.uniform(y0, y1 + 1)
                if m[int(sy)][int(sx)]:
                    break
            # смещение тона грани: верхне-левые плоскости светлее, нижне-правые темнее (+ немного случайности)
            sc = -((sx - cx) / rx * 0.6 + (sy - cy) / ry) + rnd.uniform(-0.35, 0.35)
            seeds.append((sx, sy, 1 if sc > 0.55 else (-1 if sc < -0.45 else 0)))
        owner = {}
        if seeds:
            for y in range(y0, y1 + 1):
                for x in range(x0, x1 + 1):
                    if m[y][x]:
                        owner[(x, y)] = min(range(len(seeds)), key=lambda i: (x + 0.5 - seeds[i][0]) ** 2 + ((y + 0.5 - seeds[i][1]) * 1.3) ** 2)
        order = ['crev', 'shade', 'body', 'mid', 'top', 'rim']
        glint = ch.get('glint', False)
        ao = ch.get('ao', 2)
        for y in range(h):
            for x in range(w):
                if not m[y][x]:
                    continue
                up_out = y == 0 or not m[y - 1][x]
                left_out = x == 0 or not m[y][x - 1]
                dn_out = y + 1 >= h or not m[y + 1][x]
                rt_out = x + 1 >= w or not m[y][x + 1]
                u = (x + 0.5 - cx) / rx
                v = (y + 0.5 - cy) / ry
                depth = y - top_of[x]
                if up_out and u < 0.55:
                    role = 'rim'
                elif glint and 1 <= depth <= 2 and -0.7 < u < -0.05 and v < -0.2:
                    role = 'glint'
                elif left_out and v < 0.1:
                    role = 'top' if depth < cap else 'mid'
                elif depth < cap:
                    role = 'top' if u < 0.45 else 'mid'
                elif depth == cap and u < 0.2:
                    role = 'mid'
                elif dn_out or rt_out or u * 0.7 + v > ch.get('shade_th', 0.8) or y > y1 - ao:
                    role = 'shade'
                else:
                    role = 'body'
                if role == 'glint':
                    cv.set(x, y, pal.get('glint', pal['rim']))
                    union[y][x] = True
                    continue
                t = order.index(role)
                if seeds and role not in ('rim',):
                    o = owner[(x, y)]
                    t += seeds[o][2]
                    # линия скола: нижняя/правая сторона границы граней — на тон темнее
                    for dx, dy in ((-1, 0), (0, -1)):
                        q = (x + dx, y + dy)
                        if q in owner and owner[q] != o and seeds[owner[q]][2] >= seeds[o][2]:
                            t -= 1
                            break
                t = max(1, min(5, t))
                cv.set(x, y, pal[order[t]])
                union[y][x] = True
    if outline:
        _outline_mask(cv, union, pal['out'])
    return union


PEB_PAL = dict(crev='stone0', shade='stone2', body='stone3', mid='stone3', top='stone4', rim='stone5', out='stone0')


def pebble(cv: Canvas, x: int, y: int, w: int, h: int, pal=PEB_PAL) -> None:
    """Камешек w×h с контуром; (x, y) — левый верхний угол тела. Светлее валуна, чтобы читался у подножия."""
    sub = Canvas(cv.w, cv.h)
    pile(sub, [dict(shape=('ell', x + w / 2.0, y + h / 2.0, w / 2.0, h / 2.0), cap=1, ao=0)], pal)
    cv.blit(sub, 0, 0)


NUG_BIG = ['..a..', '.aba.', 'abcba', '.aba.', '..d..']
NUG_MID = ['.a.', 'aba', '.d.']


def nugget(cv: Canvas, x: int, y: int, big: bool = True, sparkle: bool = False) -> None:
    """Золотой самородок-«звёздочка»: (x, y) — центр."""
    pat = NUG_BIG if big else NUG_MID
    leg = {'a': 'gold3', 'b': 'gold4', 'c': 'gold5' if sparkle else 'gold4', 'd': 'gold2'}
    r = len(pat) // 2
    for j, row in enumerate(pat):
        for i, ch in enumerate(row):
            if ch in leg:
                cv.set(x - r + i, y - r + j, leg[ch])
    # тень самородка вниз-вправо — тёмное золото
    cv.set(x + r, y + 1, 'gold1')
    if big:
        cv.set(x + 1, y + 2, 'gold1')


def fleck(cv: Canvas, x: int, y: int) -> None:
    """Золотая крапинка 1–2 px (только по камню)."""
    if cv.opaque(x, y):
        cv.set(x, y, 'gold2')
    if cv.opaque(x + 1, y) and (x + y) % 2 == 0:
        cv.set(x + 1, y, 'gold1')


def G(cx, cy, rx, ry, seed=0, n=8, jit=0.14, rot=0.3, narrow=0.0, **kw):
    """Глыба-многоугольник для pile()."""
    d = dict(shape=('poly', poly_pts(cx, cy, rx, ry, n, seed, jit, rot, narrow)))
    d.update(kw)
    return d


PLAIN = [
    # Купол-«стог»: макушка по центру, у подножия — россыпь.
    dict(chunks=[G(16, 24.5, 12.5, 13.5, 1, n=12, jit=0.07, narrow=0.36, facets=6, fseed=1, cap=7, glint=True),
                 G(7.5, 34, 4.5, 2.5, 2), G(24.5, 34, 5, 2.5, 3)],
         nug=[(12, 19, True, True), (20, 26, True, False), (14, 30, False, False), (20, 16, False, False), (8, 26, False, False)],
         fl=[(16, 13), (9, 22), (24, 22), (17, 23), (11, 28), (25, 29), (19, 32)],
         peb=[(1, 33, 6, 5), (11, 35, 5, 4), (25, 34, 5, 4)]),
    # Высокий «зуб»: вершина смещена влево, справа плечо.
    dict(chunks=[G(14.5, 23.5, 11, 14.5, 5, n=11, jit=0.08, rot=0.1, narrow=0.42, facets=6, fseed=2, cap=7, glint=True),
                 G(24, 30.5, 6, 5.5, 6, facets=2, fseed=5)],
         nug=[(12, 17, True, True), (17, 27, True, False), (8, 27, False, False), (24, 30, False, False), (18, 20, False, False)],
         fl=[(9, 21), (15, 12), (20, 24), (12, 32), (6, 31), (26, 27), (15, 24)],
         peb=[(1, 33, 6, 5), (19, 35, 6, 4), (27, 34, 4, 4)]),
    # Двугорбый: две макушки, правая выше.
    dict(chunks=[G(10.5, 26, 8.5, 11, 9, n=9, rot=0.0, narrow=0.32, facets=3, fseed=6, cap=4, glint=True),
                 G(20.5, 24, 9.5, 13, 11, n=11, narrow=0.38, facets=4, fseed=3, cap=6, glint=True)],
         nug=[(20, 19, True, True), (9, 27, True, False), (15, 32, False, False), (24, 29, False, False), (12, 22, False, False)],
         fl=[(6, 25), (17, 15), (25, 21), (19, 26), (7, 32), (13, 34), (23, 33)],
         peb=[(1, 33, 5, 5), (25, 34, 6, 4), (10, 35, 5, 4)]),
]


def rock_plain(i: int) -> Canvas:
    p = PLAIN[i]
    cv = Canvas(RW, RH)
    pile(cv, p['chunks'], base=BASE)
    for x, y in p.get('fl', []):
        fleck(cv, x, y)
    for x, y, big, sp in p['nug']:
        nugget(cv, x, y, big, sp)
    for x, y, w, h in p['peb']:
        pebble(cv, x, y, w, h)
    return cv


# ───────────────────── кристаллы и самоцветы ─────────────────────
ELIXIR = ('purple1', 'purple3', 'purple4', 'purple5', 'purple6')   # контур, правая грань, левая грань, кромка, блик
GEMS = ('teal1', 'teal2', 'teal3', 'teal4', 'teal6')


def crystal(cv: Canvas, bx: float, by: int, w: int, h: int, tip_dx: float = 0, tip: Optional[int] = None, pal=ELIXIR,
            spark: bool = False) -> None:
    """Кристалл-призма: основание по центру (bx, by) шириной w, острие на h выше и сдвинуто на tip_dx.
    Левая грань светлая, по оси — блик, правая грань темнее, контур — тёмный тон рампы."""
    out, dark, mid, light, hi = pal
    sub = Canvas(cv.w, cv.h)
    tip = tip if tip is not None else max(2, (w + 1) // 2)
    tx, ty = bx + tip_dx, by - h
    hw = w / 2.0
    pts = [(bx - hw, by + 1), (tx - hw, ty + tip), (tx, ty), (tx + hw, ty + tip), (bx + hw, by + 1)]
    sub.polygon(pts, mid)
    for y in range(sub.h):
        row = [x for x in range(sub.w) if sub.opaque(x, y)]
        if not row:
            continue
        t = (y + 0.5 - ty) / max(1.0, (by + 1 - ty))
        ridge = int(round(tx + (bx - tx) * t - 0.5 - w * 0.08))
        xl, xr = min(row), max(row)
        for x in row:
            if x == ridge:
                c = hi if t < 0.6 else light       # ребро: блик к острию, ниже — просто свет
            elif x < ridge:
                c = light if x == xl and t < 0.75 else mid
            else:
                c = dark                            # правая грань в тени
            sub.set(x, y, c)
    sub.outline(out)
    cv.blit(sub, 0, 0)
    if spark:
        sx, sy = int(round(tx - 1)), int(ty + tip + 1)
        cv.set(sx, sy, hi)


def cut_gem(cv: Canvas, x: int, y: int, pal=GEMS) -> None:
    """Огранённый самоцвет 7×6 (вид спереди-сверху): (x, y) — левый верхний угол."""
    out, dark, mid, light, hi = pal
    g = Canvas.from_ascii([
        '.oooo..',
        'ohhlmo.',
        'ohllmmo',
        'olmmmdo',
        '.oddo..',
        '..oo...',
    ], {'o': out, 'h': hi, 'l': light, 'm': mid, 'd': dark})
    cv.blit(g, x, y)


def _embed(cv: Canvas, dome: Canvas, lip, shadow: str = 'stone1') -> None:
    """Вернуть породу купола поверх оснований кристаллов внутри эллипса lip (cx, cy, rx, ry):
    кристаллы «вырастают» из камня без отдельной глыбы-ободка. Над кромкой — тёмная щель."""
    cx, cy, rx, ry = lip
    m = shape_mask(cv.w, cv.h, ('ell', cx, cy, rx, ry))
    for y in range(cv.h):
        for x in range(cv.w):
            if m[y][x] and dome.opaque(x, y):
                p = dome.get(x, y)
                cv.px[x, y] = p
    for y in range(1, cv.h):
        for x in range(cv.w):
            if m[y][x] and not m[y - 1][x] and dome.opaque(x, y) and cv.get(x, y - 1) != dome.get(x, y - 1):
                cv.set(x, y, shadow)


def shards(cv: Canvas, pts, pal) -> None:
    """Мелкие осколки кристалла в теле камня (2×3, без контура): подсказывают тип камня и снизу."""
    out, dark, mid, light, hi = pal
    for x, y in pts:
        cv.set(x, y, light)
        cv.set(x + 1, y, mid)
        cv.set(x, y + 1, mid)
        cv.set(x + 1, y + 1, dark)
        cv.set(x, y - 1, hi)


def crystal_rock(kind: str, big: bool) -> Canvas:
    """Камень с кристаллами: купол породы, из макушки растут призмы, основания утоплены в камень."""
    cv = Canvas(RW, RH)
    dome = Canvas(RW, RH)
    pal = ELIXIR if kind == 'elixir' else GEMS
    el = kind == 'elixir'
    if big:
        pile(dome, [G(16, 24.5, 12.5, 13, 21, n=12, jit=0.07, narrow=0.36, facets=5, fseed=8, cap=6, glint=True)], base=BASE)
        cv.blit(dome, 0, 0)
        if el:
            crystal(cv, 10.5, 28, 5, 12, -3, pal=pal)
            crystal(cv, 21.5, 28, 5, 11, 3, pal=pal)
            crystal(cv, 16, 29, 7, 21, 0, pal=pal, spark=True)
        else:
            crystal(cv, 10, 28, 6, 10, -2, tip=3, pal=pal)
            crystal(cv, 22, 28, 6, 12, 2, tip=3, pal=pal)
            crystal(cv, 16, 29, 8, 19, 0, tip=4, pal=pal, spark=True)
        _embed(cv, dome, (16, 33, 11, 6))
        shards(cv, ((9, 31), (19, 33)), pal)
        crystal(cv, 24.5, 32, 3, 5, 2, tip=2, pal=pal)
        for px, py, pw, ph in ((1, 33, 6, 5), (10, 35, 5, 4), (19, 35, 5, 4), (26, 34, 5, 4)):
            pebble(cv, px, py, pw, ph)
    else:
        pile(dome, [G(16, 25, 12.5, 12.5, 24, n=12, jit=0.07, narrow=0.34, facets=5, fseed=9, cap=6, glint=True)], base=BASE)
        cv.blit(dome, 0, 0)
        if el:
            crystal(cv, 14, 26, 5, 13, -1, pal=pal, spark=True)
            crystal(cv, 19.5, 27, 4, 8, 2, pal=pal)
        else:
            crystal(cv, 13.5, 26, 6, 12, -1, tip=3, pal=pal, spark=True)
            crystal(cv, 19.5, 27, 5, 7, 2, tip=2, pal=pal)
        _embed(cv, dome, (16, 31, 10.5, 6))
        shards(cv, ((10, 30), (21, 31), (15, 33)), pal)
        for px, py, pw, ph in ((1, 33, 6, 5), (11, 35, 5, 4), (25, 34, 5, 4)):
            pebble(cv, px, py, pw, ph)
    return cv


# ───────────────────── сундуки ─────────────────────
# Материал: body — (контур, тень, тело, свет, блик); band — оковка (тень, тело, свет); lock — (тень, тело, свет, скважина);
# rivet — заклёпки; star — искры легендарного сундука.
CHEST_MATS: Dict[str, dict] = {
    'wooden': dict(body=('wood0', 'wood2', 'wood3', 'wood4', 'wood5'), band=('stone1', 'stone3', 'stone5'),
                   lock=('gold1', 'gold3', 'gold4', 'wood0'), rivet='stone5', plank=True),
    'iron': dict(body=('fur_gray0', 'fur_gray1', 'fur_gray2', 'fur_gray3', 'fur_gray4'), band=('fur_gray0', 'fur_gray1', 'fur_gray3'),
                 lock=('stone1', 'stone4', 'stone6', 'ink1'), rivet='fur_gray4', plank=False),
    'forged': dict(body=('ink1', 'ink3', 'stone1', 'stone2', 'stone3'), band=('ink2', 'stone1', 'stone3'),
                   lock=('amber0', 'amber2', 'amber3', 'ink1'), rivet='amber3', plank=False, seam='amber1'),
    'silver': dict(body=('fur_white0', 'fur_white1', 'fur_white2', 'fur_white3', 'fur_white4'), band=('cream0', 'cream1', 'cream2'),
                   lock=('blue2', 'blue4', 'blue6', 'blue1'), rivet='cream3', plank=False, out='stone2'),
    'golden': dict(body=('gold0', 'gold2', 'gold3', 'gold4', 'gold5'), band=('gold1', 'gold2', 'gold4'),
                   lock=('red1', 'red3', 'red5', 'red0'), rivet='gold5', plank=False),
    'royal': dict(body=('purple0', 'purple2', 'purple3', 'purple4', 'purple5'), band=('gold1', 'gold3', 'gold4'),
                  lock=('gold1', 'gold3', 'gold5', 'purple0'), rivet='gold4', plank=False),
    'mythical': dict(body=('teal0', 'teal2', 'teal3', 'teal4', 'teal5'), band=('gold1', 'gold3', 'gold4'),
                     lock=('gold1', 'gold3', 'gold5', 'teal0'), rivet='gold4', plank=False),
    'legendary': dict(body=('red0', 'red2', 'red3', 'red4', 'amber4'), band=('gold1', 'gold3', 'gold4'),
                      lock=('gold1', 'gold4', 'gold5', 'red0'), rivet='gold5', plank=False, star=True),
}
CHEST_ORDER = ['wooden', 'iron', 'forged', 'silver', 'golden', 'royal', 'mythical', 'legendary']


def chest(cv: Canvas, mat: dict, x0: int = 5, y0: int = 17, w: int = 22) -> None:
    """Сундук с полукруглой крышкой (вид 3/4): x0, y0 — левый верхний угол крышки; ширина w, высота 20.
    Крышка: верхняя грань светлая, передняя — тон тела, по низу — металлическая кромка; замок на стыке."""
    out, dk, md, lt, hi = mat['body']
    b0, b1, b2 = mat['band']
    l0, l1, l2, kh = mat['lock']
    sub = Canvas(cv.w, cv.h)
    x1 = x0 + w - 1
    seam, bot = y0 + 8, y0 + 19
    # крышка
    for k in range(8):
        inset = 3 if k == 0 else (1 if k == 1 else 0)
        c = hi if k == 0 else (lt if k < 4 else md)
        sub.hline(x0 + inset, x1 - inset, y0 + k, c)
    sub.hline(x0 + 1, x0 + 7, y0 + 1, hi)
    sub.hline(x0 + 1, x1 - 1, y0 + 4, md)
    sub.hline(x0, x1, y0 + 3, lt)
    sub.set(x1, y0 + 2, md)
    sub.set(x1, y0 + 3, md)
    for y in range(y0 + 4, seam):
        sub.set(x1, y, dk)
    sub.hline(x0, x1, y0 + 7, b1)                     # металлическая кромка крышки
    sub.hline(x0, x0 + 2, y0 + 7, b2)
    sub.hline(x0, x1, seam, out)                      # щель под крышкой
    # корпус
    sub.rect(x0, seam + 1, w, bot - seam, md)
    sub.hline(x0, x1, seam + 1, lt)
    if mat.get('plank'):
        sub.hline(x0, x1, seam + 5, dk)
        sub.hline(x0, x1, seam + 6, lt)
    elif mat.get('seam'):
        sub.hline(x0 + 1, x1 - 1, seam + 5, mat['seam'])
    sub.vline(x1, seam + 1, bot, dk)
    sub.hline(x0, x1, bot - 1, dk)
    sub.hline(x0, x1, bot, b0)
    if mat.get('seam'):
        sub.hline(x0 + 1, x1 - 1, seam, mat['seam'])
    # оковка: вертикальные полосы у краёв
    for bxs in (x0 + 2, x1 - 4):
        for y in range(y0 + 1, bot + 1):
            if not sub.opaque(bxs, y):
                continue
            sub.set(bxs, y, b2)
            sub.set(bxs + 1, y, b1)
            sub.set(bxs + 2, y, b0)
        sub.set(bxs + 1, y0 + 3, mat['rivet'])
        sub.set(bxs + 1, seam + 3, mat['rivet'])
        sub.set(bxs + 1, bot - 2, mat['rivet'])
    # уголки внизу
    for cx_ in (x0, x1 - 1):
        sub.rect(cx_, bot - 2, 2, 3, b1)
    sub.set(x0, bot - 2, b2)
    # замок: пластина 5×6 на стыке крышки и корпуса, скважина — вертикальная прорезь
    lock = Canvas.from_ascii([
        '.ooo.',
        'ohhlo',
        'ohklo',
        'olklo',
        'olllo',
        '.ooo.',
    ], {'o': l0, 'l': l1, 'h': l2, 'k': kh})
    lx = x0 + (w - 5) // 2
    for j in range(6):
        for i in range(5):
            pcol = lock.get(i, j)
            if pcol[3]:
                sub.px[lx + i, seam - 3 + j] = pcol
    sub.outline(out)
    cv.blit(sub, 0, 0)
    if mat.get('star'):
        for sx, sy, big in ((x1, y0 - 1, True), (x0 - 1, seam + 4, False)):
            cv.set(sx, sy, 'cream4')
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                cv.set(sx + dx, sy + dy, 'gold5')
            if big:
                for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)):
                    cv.set(sx + dx, sy + dy, 'gold4')


def rock_chest(tier: str) -> Canvas:
    """Сундук, вросший в каменную россыпь: глыба за правым боком, камешки у подножия."""
    cv = Canvas(RW, RH)
    pile(cv, [G(25.5, 31, 5, 5, 32, facets=2, fseed=12, cap=2)], base=BASE)
    chest(cv, CHEST_MATS[tier])
    pebble(cv, 1, 33, 6, 5)
    pebble(cv, 22, 35, 6, 4)
    pebble(cv, 8, 36, 4, 3)
    return cv


# ───────────────────── пропуск (жетон-звезда) ─────────────────────
STAR = [
    '.....o.....',
    '....oho....',
    '....ohmo...',
    'oooohhmoooo',
    'ohhhhmmmmdo',
    '.ohhmmmmdo.',
    '..ommmmdo..',
    '..ommodmo..',
    '.ommo.odmo.',
    '.ooo...ooo.',
]


def rock_pass() -> Canvas:
    """Валун с синей звездой-жетоном (валюта сезона): звезда утоплена в макушку, рядом искорки."""
    cv = Canvas(RW, RH)
    pile(cv, [G(16, 24.5, 12.5, 13, 41, n=12, jit=0.07, narrow=0.36, facets=5, fseed=13, cap=6, glint=True), G(7.5, 34, 4.5, 2.5, 42),
              G(24.5, 34, 5, 2.5, 43)], base=BASE)
    star = Canvas.from_ascii(STAR, {'o': 'blue1', 'h': 'blue6', 'm': 'blue4', 'd': 'blue3'})
    sx, sy = 10, 18
    cv.blit(star, sx, sy)
    cv.set(sx + 5, sy + 1, 'blue7')
    cv.set(sx + 2, sy + 4, 'blue7')
    cv.set(sx + 3, sy + 4, 'blue7')
    for px, py in ((23, 15), (7, 16)):
        cv.set(px, py, 'blue7')
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            cv.set(px + dx, py + dy, 'blue5')
    for px, py, pw, ph in ((2, 34, 5, 4), (11, 35, 5, 4), (26, 34, 5, 4)):
        pebble(cv, px, py, pw, ph)
    return cv


# ───────────────────── дополнительно: россыпь камешков ─────────────────────
def rock_debris(i: int) -> Canvas:
    """Россыпь из 2–3 камешков (как в пустых клетках концепта): декор пустой клетки или след разбитого камня."""
    cv = Canvas(RW, RH)
    if i == 0:
        pile(cv, [G(19, 28, 5.5, 4.5, 201, cap=2), G(11, 31, 5, 4, 202, cap=2), G(17, 33.5, 6, 3, 203, cap=2)], PEB_PAL, base=BASE)
    else:
        pile(cv, [G(13, 30, 6, 5, 204, cap=2), G(22, 33, 4, 3, 205, cap=2)], PEB_PAL, base=BASE)
    return cv


# ───────────────────── трещины (наложение на любой rock_*) ─────────────────────
CRACK_1 = [
    [(14, 16), (15, 18), (13, 20), (14, 22), (12, 25), (13, 27)],
    [(14, 22), (16, 23), (18, 23), (19, 26)],
    [(15, 18), (17, 17)],
]
CRACK_2 = CRACK_1 + [
    [(21, 15), (20, 18), (22, 20), (21, 23), (23, 26), (22, 29)],
    [(22, 20), (24, 21)],
    [(9, 23), (10, 26), (8, 28)],
    [(13, 27), (11, 30)],
]
CRACK_WIDE = [(14, 22), (13, 23), (21, 23), (22, 24)]          # где трещина расходится в 2 px
CRACK_CHIPS = [(17, 26), (11, 30), (23, 17)]                    # выбоины 2×2 (только crack_2)


def crack(level: int) -> Canvas:
    """Трещины в центральной части тела камня (x 8…25, y 15…31): тёмная линия скола (ink1) и светлая кромка
    справа-снизу (свет сверху-слева попадает на нижний край скола). crack_2 содержит все линии crack_1."""
    cv = Canvas(RW, RH)
    lines = CRACK_1 if level == 1 else CRACK_2
    for pl in lines:
        for (ax, ay), (bx, by) in zip(pl, pl[1:]):
            cv.line(ax, ay, bx, by, 'ink1')
    if level == 2:
        for x, y in CRACK_WIDE:
            cv.set(x + 1, y, 'ink1')
        for x, y in CRACK_CHIPS:
            cv.rect(x, y, 2, 2, 'ink1')
            cv.set(x + 2, y + 1, 'stone5')
            cv.set(x, y + 2, 'stone4')
            cv.set(x + 1, y + 2, 'stone5')
    dark = {(x, y) for y in range(cv.h) for x in range(cv.w) if cv.opaque(x, y) and cv.get(x, y)[:3] == (11, 10, 20)}
    for x, y in sorted(dark):
        if (x + 1, y) not in dark and (x + 1, y + 1) not in dark and (x + y) % 2 == 0:
            cv.set(x + 1, y, 'stone5')
    return cv


# ───────────────────── стадии повреждения камня (силуэт меняется) ─────────────────────
CRACK_3 = CRACK_2 + [
    [(18, 14), (19, 17), (17, 20), (18, 22)],
    [(10, 19), (12, 21), (11, 24)],
    [(24, 28), (21, 30), (22, 33)],
]
STONE_RGB = None


def _stone_like(rgb) -> bool:
    """Пиксель камня (а не сундука, кристалла или жетона): тона stone/navy/slate — трещины только по ним."""
    global STONE_RGB
    if STONE_RGB is None:
        from pxl import PALETTE
        STONE_RGB = {PALETTE[k] for k in PALETTE if k.startswith(('stone', 'navy', 'slate'))}
    return rgb in STONE_RGB


def damaged(base: Canvas, level: int, seed: int, stone_only: bool = False) -> Canvas:
    """Камень на стадии повреждения 1–3 (ART_DIRECTION.md, «Камни»): 1 — трещины; 2 — трещины глубже и сколы по
    верхнему краю (силуэт меньше); 3 — верхушка откушена, тёмные провалы, осыпь камешков у подножия. Трещины рисуются
    только по телу камня (у сундуков и жетона — только по камню вокруг), новые края сколов получают контур."""
    cv = base.copy()
    rng = random.Random(seed)
    W, H = cv.w, cv.h
    cols = [x for x in range(W) if any(cv.opaque(x, y) for y in range(H))]
    if not cols:
        return cv
    tops = {x: min(y for y in range(H) if cv.opaque(x, y)) for x in cols}
    x_lo, x_hi = min(cols), max(cols)
    removed = set()

    def carve(cx: float, cy: float, r: float) -> None:
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r and cv.opaque(x, y) and y < BASE - 2:
                    cv.set(x, y, None)
                    removed.add((x, y))

    if level >= 2:
        span = [x for x in cols if x_lo + 4 <= x <= x_hi - 4]
        for k in range(1 if level == 2 else 2):
            x = rng.choice(span)
            carve(x + 0.5, tops[x] + 0.5, 1.7 if level == 2 else 2.3)
    if level == 3:
        # «откушенное» плечо: верх снижен на 3–4 px на 6–8 столбцах слева или справа
        w = rng.randint(6, 8)
        x0 = x_lo + 2 if rng.random() < 0.5 else x_hi - 2 - w
        for x in range(x0, x0 + w):
            if x not in tops:
                continue
            depth = 4 - abs(x - (x0 + w / 2)) * 0.35
            for y in range(tops[x], tops[x] + max(1, int(round(depth)))):
                if cv.opaque(x, y):
                    cv.set(x, y, None)
                    removed.add((x, y))
    # контур по новым краям сколов
    edge = set()
    for x, y in removed:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            if q not in removed and cv.opaque(*q):
                edge.add(q)
    for x, y in edge:
        cv.set(x, y, 'stone0')
    # трещины
    body = {(x, y) for y in range(H) for x in range(W) if cv.opaque(x, y)}
    inner = {(x, y) for x, y in body if all((x + dx, y + dy) in body for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}
    lines = CRACK_1 if level == 1 else (CRACK_2 if level == 2 else CRACK_3)
    marks = set()
    for pl in lines:
        for (ax, ay), (bx, by) in zip(pl, pl[1:]):
            n = max(abs(bx - ax), abs(by - ay)) + 1
            for i in range(n):
                x = round(ax + (bx - ax) * i / max(1, n - 1))
                y = round(ay + (by - ay) * i / max(1, n - 1))
                marks.add((x, y))
    if level >= 2:
        marks |= {(x + 1, y) for x, y in CRACK_WIDE}
    for x, y in marks:
        if (x, y) in inner and (not stone_only or _stone_like(cv.get(x, y)[:3])):
            cv.set(x, y, 'ink1')
            if (x + 1, y) in inner and (x + 1, y) not in marks and (x + y) % 2 == 0 and (not stone_only or _stone_like(cv.get(x + 1, y)[:3])):
                cv.set(x + 1, y, 'stone5')
    if level == 3:
        # тёмные провалы по трещинам и осыпь у подножия
        for x, y in ((14, 23), (19, 27), (11, 29)):
            if (x, y) in inner and (x + 1, y + 1) in inner and (not stone_only or _stone_like(cv.get(x, y)[:3])):
                cv.rect(x, y, 2, 2, 'ink1')
        for px, py, pw, ph in ((3, 35, 4, 3), (25, 35, 5, 3)):
            pebble(cv, px, py, pw, ph)
    return cv


# ───────────────────── препятствия: тёмные пещерные глыбы ─────────────────────
NAVY_PAL = dict(crev='navy1', shade='navy2', body='navy3', mid='navy4', top='navy5', rim='navy6', glint='navy6', out='navy0')
FACE = 9          # высота передней стенки-обрыва у нижнего ряда массива
MOSS = ('green2', 'green3', 'green4', 'green5')

# Раскладка валунов в клетке 32×32 (повторяется с периодом 32): cx, cy, rx, ry, seed, мох.
# Размеры разные и валуны заходят в соседние клетки, чтобы повтор узора не читался «обоями».
MASS_LAYOUT = [
    (9, 10.5, 10.5, 9.5, 1, True),
    (25.5, 7.5, 8, 7.5, 2, False),
    (30.5, 18.5, 4.5, 4.5, 3, False),
    (4, 26, 6.5, 6.5, 4, False),
    (20.5, 25, 10.5, 8.5, 5, False),
]


def _fit(c: float, r: float, lo: float, hi: float):
    """Вписать отрезок [c−r, c+r] в [lo, hi]: сначала чуть уменьшить (до 25 %), остальное — сдвиг."""
    if c - r < lo:
        over = lo - (c - r)
        sh = min(over * 0.5, r * 0.25)
        r -= sh
        c += over - sh
    if c + r > hi:
        over = c + r - hi
        sh = min(over * 0.5, r * 0.25)
        r -= sh
        c -= over - sh
    return c, r


def _margin(seed: int, side: int) -> int:
    """Отступ валуна от открытого края массива (1…4 px): неровный, «комковатый» силуэт."""
    return 1 + (seed * 5 + side * 3) % 4


def moss_cap(cv: Canvas, cx: float, top: int, w: int, seed: int, h: int = 4) -> None:
    """Пятно мха на макушке валуна: неровный комок (светлый верх, тёмный низ), одна-две свисающие пряди.
    Красится только по уже непрозрачным пикселям (по камню)."""
    r = random.Random(seed)
    x0 = int(round(cx - w / 2.0))
    for i in range(w):
        x = x0 + i
        y = top
        while y < top + 8 and not cv.opaque(x, y):
            y += 1
        if not cv.opaque(x, y):
            continue
        t = abs(i - (w - 1) / 2.0) / max(1.0, w / 2.0)
        d = max(1, int(round(h * (1.0 - t * t))) + (1 if r.random() < 0.3 else 0) - (1 if r.random() < 0.2 else 0))
        for k in range(d):
            if not cv.opaque(x, y + k):
                break
            if k == 0:
                c = 'green5' if t < 0.35 and i < w * 0.5 else 'green4'
            elif k == d - 1:
                c = 'green2'
            else:
                c = 'green3'
            cv.set(x, y + k, c)
        if 0.2 < t < 0.7 and r.random() < 0.22 and cv.opaque(x, y + d + 1):
            cv.set(x, y + d, 'green2')
            cv.set(x, y + d + 1, 'green1')


def cliff_face(cv: Canvas, x0: int, x1: int, y0: int, y1: int, seed: int = 0) -> None:
    """Передняя стенка-обрыв (вертикальная грань массива): x0…x1, y0…y1 включительно.
    Узор периодичен по x с шагом 32, чтобы нижние тайлы стыковались без швов."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            cv.set(x, y, 'navy2')
    cracks = [(3, 2, 7), (9, 4, 8), (16, 1, 5), (21, 3, 8), (27, 2, 6)]   # x в клетке, начало, конец
    for x in range(x0, x1 + 1):
        k = x % 32
        for cx_, a, b in cracks:
            if k == cx_:
                for y in range(y0 + a, min(y1, y0 + b) + 1):
                    cv.set(x, y, 'navy1')
            elif k == cx_ - 1:
                for y in range(y0 + a, min(y1, y0 + b) + 1):
                    cv.set(x, y, 'navy3')
        cv.set(x, y0, 'navy3')
        cv.set(x, y1, 'navy1')
        if k in (6, 7, 18, 24, 25):
            cv.set(x, y1 - 1, 'navy1')
    # торцы обрыва: слева светлее, справа темнее
    for y in range(y0, y1 + 1):
        cv.set(x0, y, 'navy3')
        cv.set(x1, y, 'navy1')


def render_mass(W: int, H: int) -> Canvas:
    """Массив W×H клеток: щели, обрыв снизу, валуны по периодической раскладке, мох, контур внутри холста.
    Корректировки валунов зависят только от открытых сторон их клетки, поэтому тайлы, вырезанные из любого
    массива, совпадают попиксельно (см. Temp/pixel_rocks/mock.py — проверка 2×2, 3×2, 2×3, 1×2, 2×1, 4×4)."""
    MW, MH = W * 32, H * 32
    cv = Canvas(MW, MH)
    fy = MH - FACE          # верх обрыва
    ins = 6                 # щели не доходят до открытых краёв: между крайними валунами виден пол
    cv.rect(ins, ins, MW - 2 * ins, fy - ins + 2, 'navy1')
    cliff_face(cv, 1, MW - 2, fy, MH - 2)
    items = []
    for j in range(H):
        for i in range(W):
            for cx, cy, rx, ry, sd, moss in MASS_LAYOUT:
                gx, gy = i * 32 + cx, j * 32 + cy
                gx, rx2 = _fit(gx, rx, float(_margin(sd, 0)), MW - float(_margin(sd, 1)))
                gy, ry2 = _fit(gy, ry, float(_margin(sd, 2)), fy + 5.0 - _margin(sd, 3))
                # мох растёт только на верхней кромке массива (как на концепте) — меньше заметен повтор
                items.append((gy, gx, rx2, ry2, sd, moss and j == 0))
    items.sort(key=lambda t: (round(t[0], 2), round(t[1], 2)))
    union = [[cv.opaque(x, y) and y >= fy for x in range(MW)] for y in range(MH)]
    mossy = []
    for gy, gx, rx, ry, sd, moss in items:
        pile(cv, [G(gx, gy, rx, ry, sd, n=10, jit=0.09, rot=0.2, cap=max(2, int(ry * 0.55)), glint=True)], NAVY_PAL, outline=False,
             union=union)
        if moss:
            mossy.append((gx, gy, rx, ry, sd))
    for gx, gy, rx, ry, sd in mossy:
        moss_cap(cv, gx - rx * 0.2, int(gy - ry), int(rx * 1.2), sd)
    allm = [[cv.opaque(x, y) for x in range(MW)] for y in range(MH)]
    _outline_mask(cv, allm, 'navy0')
    return cv


def mass_tiles():
    """Автотайлы: 9 из массива 3×3, вертикальные из 1×3, горизонтальные из 3×1."""
    out = {}
    m = render_mass(3, 3)
    names = [['tl', 't', 'tr'], ['l', 'c', 'r'], ['bl', 'b', 'br']]
    for j in range(3):
        for i in range(3):
            out['block_' + names[j][i]] = m.crop(i * 32, j * 32, 32, 32)
    v = render_mass(1, 3)
    for j, n in enumerate(('vt', 'vm', 'vb')):
        out['block_' + n] = v.crop(0, j * 32, 32, 32)
    hz = render_mass(3, 1)
    for i, n in enumerate(('hl', 'hm', 'hr')):
        out['block_' + n] = hz.crop(i * 32, 0, 32, 32)
    return out


def navy_pile(cv: Canvas, chunks, base: int = BASE) -> Mask:
    for ch in chunks:
        ch.setdefault('glint', True)
    return pile(cv, chunks, NAVY_PAL, base=base)


def split_boulder(cv: Canvas) -> None:
    """Расколотый валун: один купол, разрезанный ломаной трещиной; правая половина осела на 1 px вниз-вправо.
    Щель — контур navy0 между половинами (сверху 2 px, книзу 1 px); у каждой половины свой свет сверху-слева,
    поэтому кромка правой половины у трещины светлая, а левая половина у трещины в тени."""
    W, H = cv.w, cv.h
    dome = shape_mask(W, H, ('poly', poly_pts(16, 25.5, 12.5, 10.5, n=11, seed=57, jit=0.07, rot=0.3, narrow=0.22)), BASE)
    zig = [(18, 12), (15.5, 19), (18, 25), (16, 31), (17, 38)]   # трещина: x по ломаной сверху вниз

    def zx(y: float) -> float:
        for (x0, y0), (x1, y1) in zip(zig, zig[1:]):
            if y0 <= y <= y1:
                return x0 + (x1 - x0) * (y - y0) / (y1 - y0)
        return zig[0][0] if y < zig[0][1] else zig[-1][0]

    left = [[dome[y][x] and x + 0.5 < zx(y) for x in range(W)] for y in range(H)]
    right = [[False] * W for _ in range(H)]
    for y in range(1, H):
        for x in range(1, W):
            sx, sy = x - 1, y - 1       # осевшая половина
            if dome[sy][sx] and y <= BASE and sx + 0.5 >= zx(sy) + (2 if sy < 22 else 1):
                right[y][x] = True
    navy_pile(cv, [dict(shape=('mask', left), cap=4, facets=3, fseed=57),
                   dict(shape=('mask', right), cap=3, facets=2, fseed=58),
                   G(27, 34, 4, 2.5, 64)])
    pebble(cv, 2, 33, 6, 4, NAVY_PEB)
    pebble(cv, 11, 35, 4, 3, NAVY_PEB)
    # тонкая шапка мха на левой половине (как у других препятствий — мох, а не руда)
    moss_cap(cv, 11.5, 14, 9, 23, h=2)


MUSH_BIG = [
    '..ooooo..',
    '.ohhpcpo.',
    'ohcpppppo',
    'opppcppdo',
    'odddddddo',
    '.oossso..',
    '..ossto..',
    '..oooo...',
]
MUSH_SMALL = [
    '.oooo.',
    'ohpcpo',
    'oppppo',
    'oddddo',
    '.osto.',
    '.oooo.',
]
MUSH_LEG = {'o': 'pink0', 'h': 'pink4', 'c': 'cream3', 'p': 'pink2', 'd': 'pink1', 's': 'cream2', 't': 'cream1'}


def mushroom(cv: Canvas, x: int, y: int, big: bool = True) -> None:
    """Розовый грибок с кремовыми крапинками: (x, y) — низ ножки по центру."""
    g = Canvas.from_ascii(MUSH_BIG if big else MUSH_SMALL, MUSH_LEG)
    cv.blit(g, x - g.w // 2, y - g.h + 1)


def plank(cv: Canvas) -> None:
    """Обломок доски наискось с рваным концом и гвоздём."""
    sub = Canvas(cv.w, cv.h)
    sub.polygon([(5, 18), (9, 15), (25, 29), (21, 32)], 'wood4')
    for y in range(sub.h):
        for x in range(sub.w):
            if not sub.opaque(x, y):
                continue
            if not sub.opaque(x, y - 1) or not sub.opaque(x - 1, y):
                sub.set(x, y, 'wood5')
            elif not sub.opaque(x, y + 1) or not sub.opaque(x + 1, y):
                sub.set(x, y, 'wood2')
    # рваный конец и волокна
    for x, y in ((6, 16), (5, 17), (7, 15)):
        sub.set(x, y, None)
    sub.line(10, 18, 20, 27, 'wood3')
    sub.set(15, 21, 'stone5')
    sub.set(16, 22, 'stone2')
    sub.outline('wood0')
    cv.blit(sub, 0, 0)


def block_single(v: str) -> Canvas:
    """Одиночные препятствия 1×1 (тёмный пещерный камень, без руды): v1…v6."""
    cv = Canvas(RW, RH)
    if v == '1':      # куча валунов
        navy_pile(cv, [G(16, 19, 8, 7, 51, n=9), G(9, 29, 8, 7, 52, n=9), G(23, 29.5, 8, 6.5, 53, n=9)])
        moss_cap(cv, 14, 12, 8, 5, h=3)
        pebble(cv, 13, 35, 5, 3, NAVY_PEB)
    elif v == '2':    # сталагмиты
        stalagmite(cv, 12, 35, 15, 30)
        stalagmite(cv, 23, 35, 10, 17)
        navy_pile(cv, [G(6, 34, 5, 3, 54), G(18, 35, 5, 2.5, 55), G(28, 35, 4, 2.5, 50)])
    elif v == '3':    # замшелый валун
        navy_pile(cv, [G(16, 25, 13, 11, 56, n=11, jit=0.06, narrow=0.2, cap=5)])
        moss_cap(cv, 14, 13, 22, 9, h=6)
        pebble(cv, 2, 33, 6, 4, NAVY_PEB)
        pebble(cv, 25, 34, 5, 3, NAVY_PEB)
    elif v == '4':    # расколотый валун (без кристаллов: кристаллы на клетке поля — признак добываемого камня)
        split_boulder(cv)
    elif v == '5':    # россыпь с обломком доски
        navy_pile(cv, [G(22, 24, 7, 6, 59), G(9, 29, 7.5, 6, 60)])
        plank(cv)
        navy_pile(cv, [G(18, 33, 7, 3.5, 61), G(27, 32, 4.5, 4, 62)])
        pebble(cv, 2, 34, 5, 3, NAVY_PEB)
    elif v == '6':    # валун с грибами
        navy_pile(cv, [G(15, 26, 12.5, 10.5, 63, n=11, jit=0.07, narrow=0.25, cap=4), G(26, 32, 5, 4, 64)])
        mushroom(cv, 11, 18, True)
        mushroom(cv, 18, 17, False)
        mushroom(cv, 26, 30, False)
        pebble(cv, 2, 33, 6, 4, NAVY_PEB)
    return cv


def block_single_wall(side: str) -> Canvas:
    """Валун с грибами, прижатый к стене: со стороны стены масса выше и выходит к краю холста."""
    cv = Canvas(RW, RH)
    if side == 'l':
        navy_pile(cv, [G(5, 20, 8, 15, 71, n=10, jit=0.06), G(16, 27, 11, 9.5, 72, n=11, jit=0.07), G(26, 33, 5.5, 3.5, 73)])
        mushroom(cv, 15, 19, True)
        mushroom(cv, 6, 6, False)
        mushroom(cv, 25, 31, False)
    else:
        # своя раскладка (а не отражение), чтобы свет оставался сверху-слева
        navy_pile(cv, [G(27, 20, 8, 15, 74, n=10, jit=0.06), G(16, 27, 11, 9.5, 75, n=11, jit=0.07), G(6, 33, 5.5, 3.5, 76)])
        mushroom(cv, 15, 19, True)
        mushroom(cv, 25, 6, False)
        mushroom(cv, 7, 31, False)
    return cv


NAVY_PEB = dict(crev='navy0', shade='navy2', body='navy3', mid='navy4', top='navy5', rim='navy6', out='navy0')


def stalagmite(cv: Canvas, bx: int, by: int, w: int, h: int) -> None:
    """Сталагмит: широкий конус со скруглённой макушкой и кольцами натёков; свет слева, тень справа."""
    sub = Canvas(cv.w, cv.h)
    hw = w / 2.0
    sub.polygon([(bx - hw, by + 1), (bx - hw * 0.62, by - h * 0.45), (bx - 1.5, by - h + 1.5), (bx - 0.5, by - h),
                 (bx + 0.5, by - h), (bx + 1.5, by - h + 1.5), (bx + hw * 0.62, by - h * 0.45), (bx + hw, by + 1)], 'navy3')
    for y in range(sub.h):
        row = [x for x in range(sub.w) if sub.opaque(x, y)]
        if not row:
            continue
        xl, xr = min(row), max(row)
        span = xr - xl + 1
        ring = (by - y) % 6 == 4 and span >= 4
        for x in row:
            f = (x - xl + 0.5) / span
            if f < 0.2:
                c = 'navy5'
            elif f < 0.45:
                c = 'navy4'
            elif f < 0.75:
                c = 'navy3'
            else:
                c = 'navy2'
            if ring:
                c = {'navy5': 'navy4', 'navy4': 'navy3', 'navy3': 'navy2', 'navy2': 'navy1'}[c]
            elif (by - y) % 6 == 5 and span >= 4 and f < 0.45:
                c = 'navy6'
            sub.set(x, y, c)
    sub.set(bx - 1, by - h, 'navy6')
    sub.set(bx - 1, by - h + 1, 'navy6')
    sub.outline('navy0')
    cv.blit(sub, 0, 0)


# ───────────────────── столбы у ворот ─────────────────────
# Высоты забора ворот (structures.gate_frame, холст 160×64, низ = низ клетки): крайний столб — до 42 px,
# перекладины — 32…37 и 9…14 px над низом клетки. На холсте столба (56 px) это строки 55 − h.
GATE_RAILS_H = ((32, 37), (9, 14))
GATE_POST_TOP_H = 42


def _stone_block(cv: Canvas, x0: int, y0: int, x1: int, y1: int, seed: int) -> None:
    """Тёсаный камень кладки: скруглённые углы, свет на верхней и левой кромке, тень снизу-справа, пара сколов."""
    r = random.Random(seed)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            corner = (x in (x0, x1)) and (y in (y0, y1))
            if corner:
                continue
            if y == y0:
                c = 'navy5' if x < x1 - 1 else 'navy4'
            elif x == x0:
                c = 'navy4' if y < y1 - 1 else 'navy3'
            elif y == y1 or x == x1:
                c = 'navy2'
            else:
                c = 'navy3'
            cv.set(x, y, c)
    cv.set(x0 + 1, y0 + 1, 'navy6')
    if x1 - x0 > 5:
        cv.set(x0 + 2, y0 + 1, 'navy4')
    for _ in range(2):
        if x1 - x0 > 4 and y1 - y0 > 3:
            cx, cy = r.randint(x0 + 2, x1 - 2), r.randint(y0 + 2, y1 - 2)
            cv.set(cx, cy, 'navy2')
            cv.set(cx - 1, cy - 1, 'navy4')


def wall_gate(side: str) -> Canvas:
    """Каменный столб у края строки ворот (32×56, ноги): кладка из тёсаных блоков, сверху плита-навершие с мхом.
    Со стороны ворот столб доходит до края клетки на всю высоту забора — к нему вплотную встаёт крайний столб забора;
    на высоте перекладин — железные скобы. Со стороны стены у подножия — пещерные валуны."""
    cv = Canvas(32, 56)
    gate_right = side == 'l'           # левый столб: ворота справа
    x0, x1 = (2, 31) if gate_right else (0, 29)
    top = 10
    cv.rect(x0, top, x1 - x0 + 1, 55 - top, 'navy1')     # раствор
    courses = [(top, 9), (top + 9, 8), (top + 17, 10), (top + 27, 9), (top + 36, 9)]
    widths = [[11, 19], [7, 12, 11], [14, 16], [9, 10, 11], [12, 18]]
    for k, ((y, hgt), ws) in enumerate(zip(courses, widths)):
        if not gate_right:
            ws = list(reversed(ws))
        xx = x0
        for i, w in enumerate(ws):
            xe = min(x1, xx + w - 1) if i < len(ws) - 1 else x1
            _stone_block(cv, xx, y, xe, min(54, y + hgt - 2), 100 + k * 7 + i)
            xx = xe + 2
    cv.hline(x0, x1, 55, 'navy1')
    # навершие: плита шире столба (кроме стороны ворот), видна светлая верхняя грань
    cx0, cx1 = (x0 - 1, 31) if gate_right else (0, x1 + 1)
    cv.rect(cx0, 3, cx1 - cx0 + 1, 7, 'navy4')
    cv.hline(cx0 + 1, cx1 - 1, 3, 'navy6')
    cv.rect(cx0, 4, cx1 - cx0 + 1, 2, 'navy5')
    cv.hline(cx0, cx1, 7, 'navy3')
    cv.hline(cx0, cx1, 8, 'navy2')
    cv.hline(cx0, cx1, 9, 'navy1')
    cv.set(cx0 + 2, 4, 'navy6')
    moss_cap(cv, 11 if gate_right else 21, 3, 11, 3, h=3)
    m = [[cv.opaque(x, y) for x in range(cv.w)] for y in range(cv.h)]
    _outline_mask(cv, m, 'navy0')
    cv.hline(0, 31, 55, None)
    for x in range(32):
        if cv.opaque(x, 54):
            cv.set(x, 55, 'navy0')
    # со стороны стены — валуны, сливающиеся со стеной пещеры
    wall = Canvas(32, 56)
    if gate_right:
        navy_pile(wall, [G(3, 44, 5.5, 7, 81, n=9), G(5, 51.5, 7, 4, 82, n=9)], base=54)
    else:
        navy_pile(wall, [G(28, 44, 5.5, 7, 83, n=9), G(26, 51.5, 7, 4, 84, n=9)], base=54)
    cv.blit(wall, 0, 0)
    # железные скобы под перекладины забора (со стороны ворот)
    for lo, hi in GATE_RAILS_H:
        yy = 55 - (lo + hi) // 2
        xs = range(27, 32) if gate_right else range(0, 5)
        for x in xs:
            cv.set(x, yy - 1, 'fur_gray3')
            cv.set(x, yy, 'fur_gray2')
            cv.set(x, yy + 1, 'fur_gray0')
        rx = 28 if gate_right else 3
        cv.set(rx, yy, 'fur_gray4')
    return cv


# ───────────────────── водопад ─────────────────────
WX0, WX1 = 5, 26          # столбцы воды (включительно); по краям — мокрые камни берега


def _bank(cv: Canvas, y0: int = 0, y1: int = 31) -> None:
    """Берега: столбцы 0…4 и 27…31 — мокрый тёмный камень с бликами; период 16 по вертикали."""
    for y in range(y0, y1 + 1):
        k = y % 16
        for x in range(0, WX0):
            bump = 1 if 4 <= k <= 11 else 0
            if x > 2 + bump:
                c = 'navy1'
            elif x == 2 + bump:
                c = 'navy2'
            else:
                c = 'navy3' if x < 2 else 'navy2'
            if k == 4 and x <= 3:
                c = 'navy5'
            if k in (5, 6) and x <= 2:
                c = 'navy4'
            if k == 0:
                c = 'navy1'
            cv.set(x, y, c)
        for x in range(WX1 + 1, 32):
            xr = 31 - x
            kk = (y + 8) % 16
            bump = 1 if 4 <= kk <= 11 else 0
            if xr > 2 + bump:
                c = 'navy1'
            elif xr == 2 + bump:
                c = 'navy2'
            else:
                c = 'navy3'
            if kk == 4 and xr <= 3:
                c = 'navy4'
            if kk == 0:
                c = 'navy1'
            cv.set(x, y, c)


def _streaks(f: int):
    """Полосы воды по столбцам: (столбец, период, [(начало, длина, цвет)]) — сдвиг кадра f = f·период/3."""
    r = random.Random(17)
    cols = []
    for x in range(WX0, WX1 + 1):
        per = 16 if r.random() < 0.6 else 32
        segs = []
        n = 1 if per == 16 else 2
        for _ in range(n):
            st = r.randrange(per)
            ln = r.choice((3, 4, 5, 6, 8))
            c = r.choice(('blue5', 'blue5', 'blue6', 'blue3', 'blue3', 'blue7'))
            segs.append((st, ln, c))
        cols.append((x, per, segs))
    return cols


def water(cv: Canvas, f: int, y0: int = 0, y1: int = 31) -> None:
    for y in range(y0, y1 + 1):
        for x in range(WX0, WX1 + 1):
            cv.set(x, y, 'blue4')
    for x, per, segs in _streaks(0):
        sh = round(f * per / 3.0)
        for st, ln, c in segs:
            for k in range(ln):
                yy0 = (st + sh + k) % per
                for rep in range(0, 32, per):
                    yy = yy0 + rep
                    if y0 <= yy <= y1:
                        cv.set(x, yy, c)
    # кромки потока: пена у левого берега, тень у правого
    for y in range(y0, y1 + 1):
        cv.set(WX0, y, 'blue6' if (y + f * 5) % 8 < 5 else 'cream3')
        cv.set(WX1, y, 'blue2')
        cv.set(WX1 - 1, y, 'blue3' if (y + f * 3) % 7 else cv.get(WX1 - 1, y))


def waterfall_frame(f: int) -> Canvas:
    cv = Canvas(32, 32)
    _bank(cv)
    water(cv, f)
    return cv


def waterfall_top() -> Canvas:
    """Верх водопада: тёмная скала с широкой трещиной; вода переливается через нижнюю кромку трещины
    пенной полосой и уходит вниз во всю ширину потока (стык с waterfall_* по нижнему краю)."""
    cv = Canvas(32, 32)
    _bank(cv, 12, 31)
    water(cv, 0, 13, 31)
    rock = Canvas(32, 32)
    navy_pile(rock, [G(5, 6, 6.5, 6, 91, n=9), G(16, 4.5, 7.5, 4.5, 93, n=9), G(27, 6, 6.5, 6, 92, n=9)], base=12)
    cv.blit(rock, 0, 0)
    # трещина: тёмная щель во всю ширину потока, над ней нависает скала
    cv.rect(WX0, 9, WX1 - WX0 + 1, 3, 'ink1')
    cv.hline(WX0 + 1, WX1 - 1, 11, 'blue2')
    cv.hline(WX0 - 1, WX1 + 1, 8, 'navy0')
    cv.set(WX0 - 1, 9, 'navy0')
    cv.set(WX1 + 1, 9, 'navy0')
    # перелив: светлая вода на кромке и пена
    cv.hline(WX0, WX1, 12, 'blue6')
    for x in range(WX0, WX1 + 1):
        if x % 3 != 1:
            cv.set(x, 13, 'cream3')
        if x % 4 == 0:
            cv.set(x, 14, 'blue7')
        if x % 5 == 2:
            cv.set(x, 12, 'cream4')
    for x, y in ((7, 15), (12, 16), (18, 15), (23, 16), (15, 17), (9, 18), (21, 18)):
        cv.set(x, y, 'blue7')
    return cv


def waterfall_pool(f: int) -> Canvas:
    """Низ водопада: поток падает в чашу, пена и брызги (2 кадра), спереди — каменный бортик."""
    cv = Canvas(32, 32)
    _bank(cv, 0, 31)
    water(cv, 0, 0, 13)
    # чаша
    pool = Canvas(32, 32)
    pool.ellipse(16, 21, 14, 7, 'blue3')
    for y in range(32):
        for x in range(32):
            if pool.opaque(x, y) and y >= 12:
                cv.set(x, y, 'blue3')
    # рябь
    for i, (x, y, l) in enumerate(((6, 22, 4), (20, 23, 5), (10, 25, 6), (17, 19, 3), (24, 20, 3))):
        dx = (1 if f else 0) * (1 if i % 2 else -1)
        cv.hline(x + dx, x + dx + l - 1, y, 'blue5')
    # пена: облако у места падения
    foam = [(5, 12, 4), (10, 13, 5), (16, 12, 5), (22, 13, 4), (8, 15, 3), (14, 15, 4), (20, 15, 4), (12, 17, 3), (18, 17, 3)]
    for i, (x, y, w) in enumerate(foam):
        xx = x + ((i + f) % 2)
        cv.hline(xx, xx + w - 1, y, 'cream3')
        cv.hline(xx + 1, xx + w - 2, y - 1, 'cream4' if (i + f) % 3 == 0 else 'blue7')
        cv.set(xx + w - 1, y + 1, 'blue6')
    for x, y in ((4, 9), (26, 10), (7, 18) if f else (6, 17), (25, 17) if f else (24, 18), (15, 20) if f else (17, 20)):
        cv.set(x, y, 'blue7')
    # каменный бортик спереди
    rim = Canvas(32, 32)
    navy_pile(rim, [G(5, 28, 6, 4, 95), G(15, 29.5, 7, 3, 96), G(26, 28, 6.5, 4, 97)], base=30)
    cv.blit(rim, 0, 0)
    return cv


# ───────────────────── новый режим: расчистка препятствий (ТЗ v2.1 §8.2, §12.4) ─────────────────────
# Препятствия x получили HP: глыбы трескаются, водопад иссякает, столбы ворот крошатся. Наложения трещин рисуются
# по центру клетки (там у любой глыбы тело), цвет скола — светлый тон navy: темы окружения перекрашивают его сами.
BLOCK_CRACK_1 = [
    [(15, 7), (16, 10), (14, 12), (15, 15), (13, 18)],
    [(15, 15), (18, 16), (20, 19)],
]
BLOCK_CRACK_2 = BLOCK_CRACK_1 + [
    [(22, 6), (21, 9), (23, 12), (22, 15)],
    [(13, 18), (10, 20), (11, 23)],
    [(20, 19), (19, 23), (21, 25)],
    [(9, 11), (11, 13)],
]
BLOCK_CRACK_3 = BLOCK_CRACK_2 + [
    [(18, 4), (19, 7)],
    [(7, 16), (9, 18), (8, 21)],
    [(24, 18), (25, 21), (23, 24)],
]
BLOCK_HOLES = [(14, 13), (19, 20), (11, 20)]          # выбоины 2×2 (стадия 2 — первые две, 3 — все)


def block_crack(level: int) -> Canvas:
    """Наложение трещин 32×32 на любую клетку препятствия (block_*, block_single_*), ноги (16, 0) — как у тайлов:
    1 — пара трещин; 2 — сеть трещин и выбоины; 3 — глубокие провалы, отколотые куски и осыпь камешков у основания.
    Тёмная линия скола ink1, светлая кромка справа-снизу navy6 (свет сверху-слева попадает на нижний край скола)."""
    cv = Canvas(32, 32)
    lines = (BLOCK_CRACK_1, BLOCK_CRACK_2, BLOCK_CRACK_3)[level - 1]
    marks = set()
    for pl in lines:
        for (ax, ay), (bx, by) in zip(pl, pl[1:]):
            n = max(abs(bx - ax), abs(by - ay)) + 1
            for k in range(n):
                marks.add((round(ax + (bx - ax) * k / max(1, n - 1)), round(ay + (by - ay) * k / max(1, n - 1))))
    if level >= 2:
        marks |= {(16, 10), (14, 13), (16, 11)}
    for x, y in marks:
        cv.set(x, y, 'ink1')
    for x, y in sorted(marks):
        if (x + 1, y) not in marks and (x + y) % 2 == 0:
            cv.set(x + 1, y, 'navy6')
        if (x, y + 1) not in marks and (x + y) % 3 == 0:
            cv.set(x, y + 1, 'navy5')
    if level >= 2:
        for x, y in BLOCK_HOLES[:2 if level == 2 else 3]:
            cv.rect(x, y, 2, 2, 'ink1')
            cv.set(x + 2, y + 1, 'navy6')
            cv.set(x, y + 2, 'navy5')
            cv.set(x + 1, y + 2, 'navy6')
    if level == 3:
        # Отколотые куски: тёмные провалы с освещённой нижней кромкой.
        for x0, y0, w, h in ((19, 9, 4, 3), (8, 14, 3, 3)):
            cv.rect(x0, y0, w, h, 'navy0')
            cv.hline(x0, x0 + w - 1, y0, 'ink1')
            cv.hline(x0 - 1, x0 + w, y0 + h, 'navy6')
        # Осыпь у основания клетки.
        for px, py, pw, ph in ((3, 27, 5, 3), (24, 26, 5, 4), (13, 28, 4, 3)):
            pebble(cv, px, py, pw, ph, NAVY_PEB)
    return cv


def rubble_debris(i: int) -> Canvas:
    """След расчищенного препятствия 32×32 (ноги (16, 0), на уровне пола, проходимо): тёмные пещерные камешки,
    клочок мха; вариант 2 — с обломком доски. Темы перекрашивают navy и мох."""
    cv = Canvas(32, 32)
    if i == 0:
        pile(cv, [G(11, 22, 5, 3.5, 301, cap=2), G(20, 25, 6, 3.5, 302, cap=2), G(14, 27.5, 4.5, 2.5, 303, cap=1)], NAVY_PEB, base=29)
        pebble(cv, 25, 18, 3, 2, NAVY_PEB)
        moss_cap(cv, 11, 18, 5, 4, h=1)
    elif i == 1:
        pile(cv, [G(18, 21, 4.5, 3, 304, cap=2), G(10, 26, 6, 3.5, 305, cap=2), G(23, 27, 5, 3, 306, cap=2)], NAVY_PEB, base=29)
        pebble(cv, 4, 19, 3, 2, NAVY_PEB)
        pebble(cv, 27, 21, 3, 2, NAVY_PEB)
    else:
        sub = Canvas(32, 32)
        sub.polygon([(6, 25), (8, 22), (22, 26), (21, 29)], 'wood3')
        sub.shade('wood4', 'wood2', 'wood3')
        sub.set(7, 23, None)
        sub.set(15, 25, 'stone5')
        sub.outline('wood0')
        pile(cv, [G(23, 22, 5, 3.5, 307, cap=2)], NAVY_PEB, base=29)
        cv.blit(sub, 0, 0)
        pile(cv, [G(10, 28, 4.5, 2.5, 308, cap=1)], NAVY_PEB, base=30)
        pebble(cv, 26, 27, 3, 2, NAVY_PEB)
    return cv


BED_STONES = [(9, 3, 3.5, 2.5), (17, 2, 4, 2.2), (23, 6, 2.5, 2), (12, 10, 4.5, 2.5), (21, 13, 3.5, 2.5), (8, 17, 2.5, 2),
              (15, 18, 3.5, 2.2), (23, 21, 3, 2.5), (10, 24, 4, 2.5), (18, 27, 4.5, 2.2), (24, 29, 2.5, 1.8), (7, 30, 2.5, 1.8)]


def _bed(cv: Canvas, wet: float, seed: int) -> None:
    """Русло водопада без воды (x WX0…WX1): тёмное ложе из плоских окатанных плит, бесшовно по вертикали
    (плиты у краёв повторяются со сдвигом 32); wet — доля мокрых бликов и луж."""
    for y in range(32):
        for x in range(WX0, WX1 + 1):
            cv.set(x, y, 'navy1')
    for cx, cy, rx, ry in BED_STONES:
        for dy in (-32, 0, 32):
            st = Canvas(32, 32)
            st.ellipse(cx, cy + dy, rx, ry, 'navy3')
            for y in range(32):
                for x in range(32):
                    if not st.opaque(x, y):
                        continue
                    up = not st.opaque(x, y - 1) if y > 0 else (cy + dy - ry > -0.5)
                    if (x + 0.5 - cx) / rx + (y + 0.5 - cy - dy) / ry < -0.6:
                        st.set(x, y, 'navy4')
                    elif (x + 0.5 - cx) / rx + (y + 0.5 - cy - dy) / ry > 0.9:
                        st.set(x, y, 'navy2')
            for y in range(32):
                for x in range(WX0 + 1, WX1):
                    if st.opaque(x, y):
                        cv.set(x, y, st.get(x, y)[:3])
    # Тень у правого берега, свет у левого.
    for y in range(32):
        cv.set(WX0, y, 'navy2')
        cv.set(WX1, y, 'navy0')
    rnd = random.Random(seed)
    if wet > 0:
        for _ in range(int(6 * wet) + 1):
            x, y = rnd.randrange(WX0 + 2, WX1 - 3), rnd.randrange(1, 30)
            cv.hline(x, x + rnd.randrange(1, 4), y, 'blue2')
            cv.set(x, y, 'blue3')
        for _ in range(int(5 * wet)):
            cv.set(rnd.randrange(WX0 + 1, WX1), rnd.randrange(0, 32), 'blue5')


def _stream(cv: Canvas, x0: int, x1: int, f: int = 0) -> None:
    """Поток воды между x0 и x1 включительно (тот же рисунок, что у waterfall_*), с пеной по левой кромке."""
    tmp = Canvas(32, 32)
    water(tmp, f)
    for y in range(32):
        for x in range(x0, x1 + 1):
            cv.set(x, y, tmp.get(x, y)[:3] if tmp.opaque(x, y) else 'blue4')
        cv.set(x0, y, 'blue6' if (y + f * 5) % 8 < 5 else 'cream3')
        cv.set(x1, y, 'blue2')


def waterfall_clear(stage: int) -> Canvas:
    """Расчистка водопада по стадиям (заменяет waterfall_* во всех клетках колонны, бесшовно по вертикали):
    0 — поток сужается, в русло падают камни; 1 — тонкая струя по центру, мокрое ложе; 2 — ручеёк и лужи;
    3 — сухое русло с осыпью (финальный вид, проходимо)."""
    cv = Canvas(32, 32)
    _bank(cv)
    _bed(cv, (1.0, 1.0, 0.8, 0.25)[stage], 500 + stage)
    if stage == 0:
        _stream(cv, WX0 + 3, WX1 - 4)
    elif stage == 1:
        _stream(cv, 12, 19)
    elif stage == 2:
        for y in range(32):
            x = 15 + (1 if (y // 6) % 2 else 0)
            cv.set(x, y, 'blue5')
            cv.set(x + 1, y, 'blue3')
            if y % 7 == 3:
                cv.set(x - 1, y, 'blue6')
    # Камни в русле: с каждой стадией их больше (обрушенная перемычка, которая держала воду).
    stones = [(8, 9, 3.5, 2.5, 511), (23, 25, 3.5, 2.5, 512), (10, 26, 3, 2, 513), (22, 8, 3, 2, 514), (16, 18, 3.5, 2.5, 515)]
    n = (2, 3, 4, 5)[stage]
    sub = Canvas(32, 32)
    pile(sub, [G(x, y, rx, ry, sd, cap=2) for x, y, rx, ry, sd in stones[:n]], NAVY_PEB)
    cv.blit(sub, 0, 0)
    if stage == 3:
        for px, py in ((13, 5), (20, 14), (7, 19), (18, 29)):
            pebble(cv, px, py, 3, 2, NAVY_PEB)
    return cv


def waterfall_top_dry() -> Canvas:
    """Верх водопада после расчистки: трещина в скале пересохла (тёмная щель, потёки), ниже — сухое русло."""
    cv = Canvas(32, 32)
    _bank(cv, 12, 31)
    full = Canvas(32, 32)
    _bed(full, 0.25, 531)
    for y in range(13, 32):
        for x in range(WX0, WX1 + 1):
            cv.set(x, y, full.get(x, y)[:3])
    rock = Canvas(32, 32)
    navy_pile(rock, [G(5, 6, 6.5, 6, 91, n=9), G(16, 4.5, 7.5, 4.5, 93, n=9), G(27, 6, 6.5, 6, 92, n=9)], base=12)
    cv.blit(rock, 0, 0)
    cv.rect(WX0, 9, WX1 - WX0 + 1, 3, 'ink1')
    cv.hline(WX0 - 1, WX1 + 1, 8, 'navy0')
    cv.set(WX0 - 1, 9, 'navy0')
    cv.set(WX1 + 1, 9, 'navy0')
    cv.hline(WX0, WX1, 12, 'navy3')
    # Сухие потёки под кромкой.
    for x in (8, 13, 19, 24):
        cv.vline(x, 13, 15 + (x % 3), 'navy2')
    for x, y in ((11, 13), (21, 14)):
        cv.set(x, y, 'blue3')
    return cv


def waterfall_pool_dry() -> Canvas:
    """Чаша водопада после расчистки: сухое дно с осыпью и последней лужицей, каменный бортик спереди."""
    cv = Canvas(32, 32)
    _bank(cv, 0, 31)
    full = Canvas(32, 32)
    _bed(full, 0.25, 532)
    for y in range(0, 14):
        for x in range(WX0, WX1 + 1):
            cv.set(x, y, full.get(x, y)[:3])
    pool = Canvas(32, 32)
    pool.ellipse(16, 21, 14, 7, 'navy2')
    for y in range(32):
        for x in range(32):
            if pool.opaque(x, y) and y >= 12:
                cv.set(x, y, 'navy2' if y > 15 else 'navy1')
    cv.ellipse(19, 22, 5, 1.6, 'blue2')
    cv.hline(16, 20, 21, 'blue3')
    cv.set(17, 21, 'blue5')
    sub = Canvas(32, 32)
    pile(sub, [G(9, 17, 3.5, 2.5, 541, cap=2), G(24, 16, 3, 2, 542, cap=1), G(12, 23, 2.5, 2, 543, cap=1)], NAVY_PEB)
    cv.blit(sub, 0, 0)
    rim = Canvas(32, 32)
    navy_pile(rim, [G(5, 28, 6, 4, 95), G(15, 29.5, 7, 3, 96), G(26, 28, 6.5, 4, 97)], base=30)
    cv.blit(rim, 0, 0)
    return cv


def wall_gate_damaged(side: str, level: int) -> Canvas:
    """Столб у ворот на стадии расчистки: 1 — трещины по кладке и сколотое навершие; 2 — выпавший блок, глубокие
    трещины, осыпь у подножия. Скобы под перекладины забора целы (забор держится за столб до открытия ворот)."""
    cv = wall_gate(side)
    gate_right = side == 'l'
    lines_1 = [[(12, 14), (13, 18), (11, 21), (12, 24)], [(20, 30), (18, 33), (19, 36)], [(9, 41), (11, 44), (10, 48)]]
    lines_2 = lines_1 + [[(16, 22), (19, 25), (18, 29), (21, 31)], [(6, 31), (8, 34), (7, 38)], [(17, 46), (15, 50), (16, 53)]]
    lines = lines_1 if level == 1 else lines_2
    if not gate_right:
        lines = [[(31 - x, y) for x, y in pl] for pl in lines]
    marks = set()
    for pl in lines:
        for (ax, ay), (bx, by) in zip(pl, pl[1:]):
            n = max(abs(bx - ax), abs(by - ay)) + 1
            for k in range(n):
                marks.add((round(ax + (bx - ax) * k / max(1, n - 1)), round(ay + (by - ay) * k / max(1, n - 1))))
    wide = {(x + 1, y) for x, y in marks if (x + y) % 3 == 0}
    for x, y in marks | wide:
        if cv.opaque(x, y):
            cv.set(x, y, 'ink1')
    for x, y in marks | wide:
        if (x + 1, y) not in marks | wide and cv.opaque(x + 1, y):
            cv.set(x + 1, y, 'navy5')
    # Сколотое навершие: угол плиты со стороны стены отколот ступенькой, по новому краю — контур.
    removed = set()
    depth = 3 if level == 1 else 5
    for k in range(depth):
        w = (depth - k) + 2
        xs = range(1, 1 + w) if gate_right else range(31 - w, 31)
        for x in xs:
            y = 3 + k
            if cv.opaque(x, y):
                cv.set(x, y, None)
                removed.add((x, y))
    for x, y in removed:
        for dx, dy in ((1, 0), (-1, 0), (0, 1)):
            q = (x + dx, y + dy)
            if q not in removed and cv.opaque(*q):
                cv.set(q[0], q[1], 'navy0')
    for x in range(32):
        if (x, 2) in removed or ((x, 3) in removed and cv.get(x, 2)[:3] == pxl_rgb('navy0')):
            cv.set(x, 2, None)
    if level == 2:
        # Выпавший блок: тёмная ниша с освещённой нижней кромкой.
        bx0 = 8 if gate_right else 15
        cv.rect(bx0, 28, 9, 7, 'ink2')
        cv.rect(bx0 + 1, 29, 7, 5, 'navy0')
        cv.hline(bx0, bx0 + 8, 35, 'navy5')
        cv.vline(bx0 + 8, 29, 34, 'navy2')
        # Осыпь у подножия.
        sub = Canvas(32, 56)
        px = 14 if gate_right else 10
        pile(sub, [G(px, 53, 4, 2.5, 521, cap=1), G(px + 7, 54, 3, 2, 522, cap=1)], NAVY_PEB)
        cv.blit(sub, 0, 0)
    return cv


def pxl_rgb(name: str):
    from pxl import PALETTE
    return PALETTE[name]


def build(reg) -> None:
    feet = (RW / 2.0, 0.0)
    for i in range(3):
        reg.add(f'rock_plain_{i}', rock_plain(i), pivot=feet)
    for kind in ('elixir', 'gems'):
        reg.add(f'rock_{kind}_small', crystal_rock(kind, False), pivot=feet)
        reg.add(f'rock_{kind}_big', crystal_rock(kind, True), pivot=feet)
    for t in CHEST_ORDER:
        reg.add(f'rock_chest_{t}', rock_chest(t), pivot=feet)
    reg.add('rock_pass', rock_pass(), pivot=feet)
    reg.add('rock_crack_1', crack(1), pivot=feet)
    reg.add('rock_crack_2', crack(2), pivot=feet)
    # стадии повреждения каждого камня: _d1 (трещины), _d2 (сколы), _d3 (откушен, осыпь)
    bases = [f'rock_plain_{i}' for i in range(3)] + [f'rock_{k}_{z}' for k in ('elixir', 'gems') for z in ('small', 'big')] + \
            [f'rock_chest_{t}' for t in CHEST_ORDER] + ['rock_pass']
    for k, name in enumerate(bases):
        base = reg.entries[name].canvas
        stone_only = name.startswith(('rock_chest_', 'rock_pass'))
        for level in (1, 2, 3):
            reg.add(f'{name}_d{level}', damaged(base, level, 1000 + 17 * k + level, stone_only), pivot=feet,
                    note=f'стадия повреждения {level}')
    reg.add('rock_debris_0', rock_debris(0), pivot=feet, note='доп.: россыпь камешков — декор/след разбитого камня')
    reg.add('rock_debris_1', rock_debris(1), pivot=feet, note='доп.: россыпь камешков, вариант')
    tile_feet = (16.0, 0.0)
    for name, cv in mass_tiles().items():
        reg.add(name, cv, pivot=tile_feet)
    for v in ('1', '2', '3', '4', '5', '6'):
        reg.add(f'block_single_{v}', block_single(v), pivot=feet)
    reg.add('block_single_6l', block_single_wall('l'), pivot=feet)
    reg.add('block_single_6r', block_single_wall('r'), pivot=feet)
    reg.add('wall_gate_l', wall_gate('l'), pivot=(16.0, 0.0))
    reg.add('wall_gate_r', wall_gate('r'), pivot=(16.0, 0.0))
    for f in range(3):
        reg.add(f'waterfall_{f}', waterfall_frame(f), pivot=tile_feet, extrude=True)
    reg.add('waterfall_top', waterfall_top(), pivot=tile_feet)
    reg.add('waterfall_pool_0', waterfall_pool(0), pivot=tile_feet)
    reg.add('waterfall_pool_1', waterfall_pool(1), pivot=tile_feet)
    # Новый режим: стадии расчистки препятствий (x с HP, ТЗ v2.1 §8.2).
    for level in (1, 2, 3):
        reg.add(f'block_crack_{level}', block_crack(level), pivot=tile_feet,
                note=f'трещины поверх любой клетки препятствия, стадия {level} (HP < 0,75 / 0,45 / 0,2)')
    for stage in range(4):
        reg.add(f'waterfall_clear_{stage}', waterfall_clear(stage), pivot=tile_feet, extrude=True,
                note=f'расчистка водопада, стадия {stage} (3 — сухое русло)')
    for side in ('l', 'r'):
        for level in (1, 2):
            reg.add(f'wall_gate_{side}_d{level}', wall_gate_damaged(side, level), pivot=(16.0, 0.0),
                    note=f'столб у ворот, стадия повреждения {level}')
    reg.add('waterfall_top_dry', waterfall_top_dry(), pivot=tile_feet, note='верх водопада после расчистки (к waterfall_clear_3)')
    reg.add('waterfall_pool_dry', waterfall_pool_dry(), pivot=tile_feet, note='чаша водопада после расчистки (к waterfall_clear_3)')
    for i in range(3):
        reg.add(f'rubble_debris_{i}', rubble_debris(i), pivot=tile_feet, note='след расчищенного препятствия (пол)')
