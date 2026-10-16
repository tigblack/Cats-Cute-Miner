"""Иконки интерфейса: 16×16 (крупные *_big — 24×24), pivot — центр.

Единое семейство: заливка рисуется ASCII-картой или примитивами, затем один общий контур 1 px ink1
(4-соседство — углы получаются мягко скруглёнными). Свет сверху-слева: блик на верхней/левой кромке,
тень — на нижней/правой; 3–4 тона на материал. Цифры в ASCII-картах — индекс тона «основной» рампы иконки
(mat), буквы — дополнительные материалы из легенды конкретной иконки.
"""
from __future__ import annotations

import math
from typing import Dict, List, Optional, Sequence, Tuple

from pxl import C, Canvas, RAMPS

OUTLINE = 'ink1'

# Общие буквы для всех карт: чернила (внутренние линии, глазки, замочные скважины).
COMMON: Dict[str, str] = {'K': 'ink1', 'k': 'ink2', 'j': 'ink3'}


# ───────────────────────── помощники ─────────────────────────
def _legend(mat: Optional[str], extra: Optional[Dict[str, str]] = None) -> Dict[str, str]:
    lg: Dict[str, str] = dict(COMMON)
    if mat:
        for i in range(len(RAMPS[mat])):
            lg[str(i)] = f'{mat}{i}'
    if extra:
        lg.update(extra)
    return lg


def _art(rows: Sequence[str], mat: Optional[str] = None, extra: Optional[Dict[str, str]] = None) -> Canvas:
    return Canvas.from_ascii(rows, _legend(mat, extra))


def _place(art: Canvas, size: int, dx: int = 0, dy: int = 0) -> Canvas:
    """Арт по центру квадратного холста (нечётный остаток — вправо/вниз) + сдвиг."""
    cv = Canvas(size, size)
    cv.blit(art, (size - art.w) // 2 + dx, (size - art.h) // 2 + dy)
    return cv


def _outlined(cv: Canvas, color: str = OUTLINE) -> Canvas:
    cv.outline(color)
    return cv


def _icon(rows: Sequence[str], mat: Optional[str] = None, extra: Optional[Dict[str, str]] = None,
          size: int = 16, dx: int = 0, dy: int = 0) -> Canvas:
    return _outlined(_place(_art(rows, mat, extra), size, dx, dy))


def _layer(dst: Canvas, src: Canvas, x: int = 0, y: int = 0) -> None:
    """Слой со своим контуром поверх уже готового (контур отделяет предметы друг от друга)."""
    dst.blit(src, x, y)


def _same(cv: Canvas, x: int, y: int, rgb) -> bool:
    return cv.opaque(x, y) and cv.get(x, y)[:3] == rgb


def _edge_shade(cv: Canvas, base: str, light: Optional[str], dark: Optional[str]) -> None:
    """Объём по силуэту: пиксели base у верхней/левой кромки — light, у нижней/правой — dark.
    В отличие от Canvas.shade, кромкой считается любой непрозрачный сосед другого цвета не того же base,
    только прозрачный — так внутренние детали не дают ложных бликов."""
    rgb = C(base)[:3]
    marks = []
    for y in range(cv.h):
        for x in range(cv.w):
            if not _same(cv, x, y, rgb):
                continue
            up, left = not cv.opaque(x, y - 1), not cv.opaque(x - 1, y)
            down, right = not cv.opaque(x, y + 1), not cv.opaque(x + 1, y)
            if light and (up or left):
                marks.append((x, y, light))
            elif dark and (down or right):
                marks.append((x, y, dark))
    for x, y, c in marks:
        cv.set(x, y, c)


def _runs_shade(cv: Canvas, base: str, light: str, dark: str) -> None:
    """Штрихи (галочка, крестик): в каждой горизонтальной серии base левый пиксель — light, правый — dark."""
    rgb = C(base)[:3]
    for y in range(cv.h):
        x = 0
        while x < cv.w:
            if _same(cv, x, y, rgb):
                x0 = x
                while x < cv.w and _same(cv, x, y, rgb):
                    x += 1
                x1 = x - 1
                if x1 - x0 >= 1:
                    cv.set(x0, y, light)
                    cv.set(x1, y, dark)
            else:
                x += 1


def _disc(d: int, rim_light: str, rim_mid: str, rim_dark: str, face: str) -> Canvas:
    """Круг диаметра d: обод 1 px (свет сверху-слева, тень снизу-справа) и заливка face."""
    cv = Canvas(d, d)
    r = d / 2.0
    cv.ellipse(r, r, r, r, face)
    ring = []
    for y in range(d):
        for x in range(d):
            if cv.opaque(x, y) and any(not cv.opaque(x + ox, y + oy) for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                ring.append((x, y))
    for x, y in ring:
        s = (x + 0.5 - r) + (y + 0.5 - r)
        cv.set(x, y, rim_light if s < -r * 0.45 else rim_dark if s > r * 0.45 else rim_mid)
    return cv


def _put(cv: Canvas, pts: Sequence[Tuple[int, int]], c: str) -> None:
    for x, y in pts:
        cv.set(x, y, c)


# ───────────────────────── валюта ─────────────────────────
def _coin_art(d: int = 14, face: int = 9) -> Canvas:
    """Монета как в HUD концепта: оранжевый обод (amber3 сверху-слева, amber2 снизу-справа, ребро amber1 снизу),
    жёлтое выпуклое поле со смещением на полпикселя вверх-влево — обод снизу-справа толще, монета объёмная."""
    cv = Canvas(d, d)
    r = d / 2.0
    cv.ellipse(r, r, r, r, 'amber3')
    for y in range(d):
        for x in range(d):
            if cv.opaque(x, y) and (x + 0.5 - r) + (y + 0.5 - r) >= 0:
                cv.set(x, y, 'amber2')
    # ребро монеты по нижней кромке
    for x in range(2, d - 1):
        for y in range(d - 1, -1, -1):
            if cv.opaque(x, y):
                cv.set(x, y, 'amber1')
                break
    f = Canvas(d, d)
    off = 0.5 if d >= 10 else 0.0
    f.ellipse(r - off, r - off, face / 2.0, face / 2.0, 'gold3')
    for y in range(d):
        for x in range(d):
            if not f.opaque(x, y):
                continue
            edge = any(not f.opaque(x + ox, y + oy) for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            s = (x + 0.5 - r) + (y + 0.5 - r)
            cv.set(x, y, 'gold4' if edge and s < -1 else 'gold2' if edge and s > 0 else 'gold3')
    # блик на поле
    fx0 = int(r - off - face / 2.0 + 0.5)
    if face >= 7:
        _put(cv, [(fx0 + 1, fx0 + 2), (fx0 + 2, fx0 + 1)], 'gold5')
    return cv


def icon_coin() -> Canvas:
    return _outlined(_place(_coin_art(14), 16))


def icon_elixir() -> Canvas:
    # Капля-кристалл эликсира как в HUD концепта: острый верх, грани — светлая левая, средняя, тёмная правая;
    # нижняя «чаша» отделена гранью; блик — белая искра у верхней-левой грани.
    rows = [
        '....65....',
        '...6654...',
        '...6554...',
        '..665544..',
        '..665544..',
        '.66554443.',
        '.66554443.',
        '6665544433',
        '6655544433',
        '5555444433',
        '4554444333',
        '.44444333.',
        '..433332..',
        '...3322...',
    ]
    cv = _icon(rows, 'purple')
    cv.set(4, 4, 'cream4')
    return cv


def icon_gem() -> Canvas:
    rows = [
        '...56666644...',
        '..5566666443..',
        '.556666664443.',
        '55555554444443',
        '.555554444333.',
        '..5555444333..',
        '...45544433...',
        '....454433....',
        '.....4433.....',
        '......43......',
    ]
    cv = _icon(rows, 'teal', dy=0)
    return cv


# ───────────────────────── котик ─────────────────────────
CAT_EXTRA = {'w': 'cream3', 'W': 'cream4', 'v': 'cream2', 'p': 'pink2', 'n': 'pink3', 'm': 'red3'}


def icon_cat() -> Canvas:
    rows = [
        '.3..........2.',
        '.33........32.',
        '.3p3......3p2.',
        '.3pp333333pp2.',
        '34333333333332',
        '34333233233322',
        '3333k3333k3322',
        '333k3k33k3k322',
        '3pwwwwnnwwwwp2',
        '3wwwwkmmkwwww2',
        '3wwwwwkkwwwww2',
        '.2wwwwwwwwww2.',
        '..22vvvvvv22..',
    ]
    return _icon(rows, 'fur_ginger', CAT_EXTRA)


# ───────────────────────── настройки ─────────────────────────
def icon_gear() -> Canvas:
    rows = [
        '.....7777.....',
        '.77..7777..77.',
        '.777777777777.',
        '..7777777777..',
        '..7777777777..',
        '777777..777777',
        '77777....77777',
        '77777....77777',
        '777777..777777',
        '..7777777777..',
        '..7777777777..',
        '.777777777777.',
        '.77..7777..77.',
        '.....7777.....',
    ]
    cv = _place(_art(rows, 'navy'), 16)
    _edge_shade(cv, 'navy7', 'navy8', 'navy5')
    return _outlined(cv)


SPEAKER = [
    '......4.',
    '.....44.',
    '....433.',
    '4444333.',
    '4333333.',
    '4333333.',
    '4333333.',
    '3222222.',
    '....222.',
    '.....22.',
    '......2.',
]


def _speaker_with(right: Sequence[str], legend: Dict[str, str]) -> Canvas:
    rows = [SPEAKER[i] + right[i] for i in range(len(SPEAKER))]
    return _icon(rows, 'cream', legend)


def icon_sound_on() -> Canvas:
    waves = [
        '......',
        '...4..',
        '....4.',
        '.4..3.',
        '..3.3.',
        '..3.3.',
        '..3.3.',
        '.3..3.',
        '....3.',
        '...3..',
        '......',
    ]
    return _speaker_with(waves, {})


# Красный крестик «выключено» (буквы, чтобы не пересекаться с цифрами основной рампы иконки).
X_LEGEND = {'X': 'red5', 'x': 'red4', 'y': 'red3'}
X_SMALL = [
    'Xx..Xx',
    '.XxXx.',
    '..Xx..',
    '.xyxy.',
    'xy..xy',
]


def icon_sound_off() -> Canvas:
    pad = ['......'] * 3
    xr = pad + X_SMALL + pad
    rows = [SPEAKER[i] + xr[i] for i in range(len(SPEAKER))]
    return _icon(rows, 'cream', X_LEGEND)


def _phone() -> List[str]:
    return [
        '888887',
        '822236',
        '823226',
        '822226',
        '822226',
        '822226',
        '822226',
        '822226',
        '822226',
        '866666',
        '876566',
        '766665',
    ]


def icon_vibro_on() -> Canvas:
    ph = _phone()
    left = ['....', '....', '....', '..c.', 'c.c.', 'c.c.', 'c.c.', 'c.c.', '..c.', '....', '....', '....']
    right = ['....', '....', '....', '.c..', '.c.c', '.c.c', '.c.c', '.c.c', '.c..', '....', '....', '....']
    rows = [left[i] + ph[i] + right[i] for i in range(len(ph))]
    return _icon(rows, 'navy', {'c': 'cream3'})


def icon_vibro_off() -> Canvas:
    ph = _phone()
    xr = ['......'] * 4 + X_SMALL + ['......'] * 3
    rows = ['.' + ph[i] + '.' + xr[i] for i in range(len(ph))]
    return _icon(rows, 'navy', dict(X_LEGEND, c='cream3'))


# ───────────────────────── кнопки HUD ─────────────────────────
CHEST_EXTRA = {'A': 'gold1', 'B': 'gold2', 'C': 'gold3', 'D': 'gold4', 'E': 'gold5'}


def icon_chest() -> Canvas:
    rows = [
        '..DC555555CB..',
        '.4DC555555CB3.',
        '54DC444444CB32',
        '43DC333333CB32',
        'DDDDDCEEDCDDDB',
        'BBBBBCDDCBBBBA',
        '44DC4CkkC4CB32',
        '43DC3CkkC3CB32',
        '33DC3BCCB3CB22',
        '33DC333333CB22',
        '22DC222222CB21',
        '.1BA111111BA1.',
    ]
    return _icon(rows, 'wood', CHEST_EXTRA)


def _card_color(u: int, v: int, w: int, h: int) -> str:
    """Тон пикселя карточки по локальным координатам: светлая кайма, синяя рубашка с внутренним бликом/тенью."""
    if u == 0 or v == 0:
        return 'cream3'
    if u == w - 1 or v == h - 1:
        return 'cream1'
    if u == 1 or v == 1:
        return 'blue4'
    if u == w - 2 or v == h - 2:
        return 'blue2'
    return 'blue3'


def _card(w: int, h: int, deg: float = 0.0) -> Canvas:
    """Карточка w×h, повёрнутая на deg градусов (deg > 0 — верх уходит влево; поворот «нарисован» растеризацией).
    Кайма считается по силуэту уже после поворота — линия получается сплошной, без разрывов на ступеньках."""
    if not deg:
        cv = Canvas(w, h)
        for v in range(h):
            for u in range(w):
                cv.set(u, v, _card_color(u, v, w, h))
        return cv
    a = math.radians(deg)
    ca, sa = math.cos(a), math.sin(a)
    bw = int(math.ceil(abs(w * ca) + abs(h * sa))) + 2
    bh = int(math.ceil(abs(w * sa) + abs(h * ca))) + 2
    cv = Canvas(bw, bh)
    for y in range(bh):
        for x in range(bw):
            px, py = x + 0.5 - bw / 2.0, y + 0.5 - bh / 2.0
            u = px * ca + py * sa + w / 2.0
            v = -px * sa + py * ca + h / 2.0
            if 0 <= u < w and 0 <= v < h:
                cv.set(x, y, 'blue3')
    rim, inner = [], []
    for y in range(bh):
        for x in range(bw):
            if not cv.opaque(x, y):
                continue
            up, left = not cv.opaque(x, y - 1), not cv.opaque(x - 1, y)
            down, right = not cv.opaque(x, y + 1), not cv.opaque(x + 1, y)
            if up or left or down or right:
                rim.append((x, y, 'cream3' if (up or left) else 'cream2'))
    for x, y, c in rim:
        cv.set(x, y, c)
    blue = C('blue3')[:3]
    for y in range(bh):
        for x in range(bw):
            if not _same(cv, x, y, blue):
                continue
            n_up = cv.get(x, y - 1)[:3] in (C('cream3')[:3], C('cream2')[:3])
            n_left = cv.get(x - 1, y)[:3] in (C('cream3')[:3], C('cream2')[:3])
            n_down = cv.get(x, y + 1)[:3] in (C('cream3')[:3], C('cream2')[:3])
            n_right = cv.get(x + 1, y)[:3] in (C('cream3')[:3], C('cream2')[:3])
            if n_up or n_left:
                inner.append((x, y, 'blue4'))
            elif n_down or n_right:
                inner.append((x, y, 'blue2'))
    for x, y, c in inner:
        cv.set(x, y, c)
    return cv


PAW_5 = [
    '.c.c.',
    'c...c',
    '..c..',
    '.ccc.',
    '.ccc.',
]
def _cards(size: int, cw: int, ch: int, cards: Sequence[Tuple[float, float, float]], paw: Sequence[str]) -> Canvas:
    """Веер карточек как на кнопке концепта. cards — (угол, центр x, центр y) от дальней к передней;
    deg < 0 — верх карточки уходит влево. Лапка — на передней. Композиция центрируется по габариту."""
    big = Canvas(size + 20, size + 20)
    for k, (deg, cx, cy) in enumerate(cards):
        c = _card(cw, ch, deg)
        if k == len(cards) - 1:
            art = _art(paw, None, {'c': 'cream3'})
            c.blit(art, (c.w - art.w) // 2, (c.h - art.h) // 2 + 1)
        lay = Canvas(c.w + 2, c.h + 2)
        lay.blit(c, 1, 1)
        lay.outline(OUTLINE)
        _layer(big, lay, int(10 + cx - lay.w / 2), int(10 + cy - lay.h / 2))
    l, t, r, b = big.img.getbbox()
    cv = Canvas(size, size)
    cv.blit(big.crop(l, t, r - l, b - t), (size - (r - l)) // 2, (size - (b - t)) // 2)
    return _prune(cv)

def icon_cards() -> Canvas:
    return _cards(16, 8, 11, [(-18, 6, 8), (0, 10, 8)], PAW_5)


def _dynamite_stick(h: int, w: int = 7, paper: bool = True) -> Canvas:
    """Шашка: цилиндр (свет слева), светлый торец сверху, бумажная лента ближе к низу (paper)."""
    cv = Canvas(w, h)
    body = ['red5', 'red5', 'red4', 'red4', 'red3', 'red3', 'red2'] if w == 7 else ['red5', 'red4', 'red4', 'red3', 'red3', 'red2']
    paper_c = ['cream3', 'cream3', 'cream3', 'cream2', 'cream2', 'cream1', 'cream1'] if w == 7 else ['cream3', 'cream3', 'cream2', 'cream2', 'cream1', 'cream1']
    for y in range(h):
        for x in range(w):
            cv.set(x, y, body[x])
    cv.hline(1, w - 2, 0, 'red6')
    cv.set(0, 0, 'red5')
    cv.set(w - 1, 0, 'red4')
    cv.hline(1, w - 2, 1, 'red5')
    cv.set(w - 1, 1, 'red3')
    cv.set(w // 2, 0, 'red3')
    if paper:
        by = h - 4
        for x in range(w):
            cv.set(x, by, paper_c[x])
            cv.set(x, by + 1, paper_c[min(w - 1, x + 1)])
    for x in range(w):
        cv.set(x, h - 1, ['red3', 'red3', 'red2', 'red2', 'red2', 'red1', 'red1'][x])
    return cv


def icon_dynamite() -> Canvas:
    cv = Canvas(16, 16)
    cv.blit(_dynamite_stick(11), 3, 4)
    _put(cv, [(6, 3), (7, 2), (8, 2), (9, 1)], 'wood2')
    _outlined(cv)
    _spark(cv, 11, 1, big=False)
    _put(cv, [(13, 3), (13, 0)], 'gold4')
    return cv


def icon_clock() -> Canvas:
    cv = Canvas(16, 16)
    body = _disc(12, 'gold4', 'gold3', 'gold2', 'cream3')
    # циферблат: тень сверху-слева внутри обода (обод выступает)
    for y in range(12):
        for x in range(12):
            if body.get(x, y)[:3] == C('cream3')[:3]:
                if not _same(body, x, y - 1, C('cream3')[:3]) or not _same(body, x - 1, y, C('cream3')[:3]):
                    body.set(x, y, 'cream2')
    # деления 12/3/6/9 и стрелки
    _put(body, [(6, 2), (9, 6), (2, 6), (6, 9)], 'cream1')
    _put(body, [(6, 3), (6, 4), (6, 5), (7, 5), (8, 5)], 'ink2')
    body.set(6, 6, 'red3')
    cv.blit(body, 2, 3)
    # кнопка-заводка и ушко
    cv.rect(7, 1, 2, 2, 'gold3')
    cv.set(7, 1, 'gold4')
    cv.set(8, 2, 'gold2')
    _outlined(cv)
    return cv


def _arrow_up_art(mat: str, head_w: int = 14, head_h: int = 7, shaft_w: int = 6, shaft_h: int = 7) -> Canvas:
    w = head_w
    cv = Canvas(w, head_h + shaft_h)
    mid = w / 2.0
    light, l2, base, dark = f'{mat}5' if mat == 'gold' else f'{mat}6', f'{mat}4' if mat == 'gold' else f'{mat}5', f'{mat}3' if mat == 'gold' else f'{mat}4', f'{mat}2' if mat == 'gold' else f'{mat}3'
    for y in range(head_h):
        half = (y + 1) * (w / 2.0) / head_h
        for x in range(w):
            if abs(x + 0.5 - mid) <= half:
                cv.set(x, y, l2 if x < mid else base)
    sx = (w - shaft_w) // 2
    for y in range(head_h, head_h + shaft_h):
        for x in range(sx, sx + shaft_w):
            cv.set(x, y, l2 if x < mid else base)
    _edge_shade(cv, l2, light, base)
    _edge_shade(cv, base, None, dark)
    # нижняя кромка «плеч» стрелки — в тени
    for x in range(w):
        if x < sx or x >= sx + shaft_w:
            if cv.opaque(x, head_h - 1):
                cv.set(x, head_h - 1, dark)
    return cv


def icon_arrow_up() -> Canvas:
    return _outlined(_place(_arrow_up_art('gold'), 16))


def icon_upgrade() -> Canvas:
    return _outlined(_place(_arrow_up_art('green', 12, 6, 6, 6), 16))


def icon_lock() -> Canvas:
    # Навесной замок: стальная дужка, золотой корпус, скважина утоплена (тень gold2 сверху-слева, кромка gold4 снизу-справа).
    rows = [
        '....8887....',
        '...88..76...',
        '..87....76..',
        '..87....76..',
        '..87....76..',
        'DDDDDDDDDDDC',
        'DCCCBBBBCCCB',
        'DCCBkkkkDCCB',
        'DCCBkkkkDCCB',
        'DCCCBkkDCCCB',
        'DCCCBkkDCCCB',
        'DCCCCDDCCCCB',
        'BBBBBBBBBBBA',
    ]
    return _icon(rows, 'navy', CHEST_EXTRA)


def icon_check() -> Canvas:
    rows = [
        '...........555',
        '..........555.',
        '.........555..',
        '........555...',
        '5......555....',
        '55....555.....',
        '555..555......',
        '.555555.......',
        '..5555........',
        '...55.........',
    ]
    cv = _place(_art(rows, 'green'), 16)
    _runs_shade(cv, 'green5', 'green6', 'green4')
    return _outlined(cv)


def icon_plus() -> Canvas:
    rows = [
        '....3333....',
        '....3333....',
        '....3333....',
        '....3333....',
        '333333333333',
        '333333333333',
        '333333333333',
        '333333333333',
        '....3333....',
        '....3333....',
        '....3333....',
        '....3333....',
    ]
    cv = _place(_art(rows, 'cream'), 16)
    _edge_shade(cv, 'cream3', 'cream4', 'cream1')
    return _outlined(cv)


def icon_close() -> Canvas:
    rows = [
        '444......444',
        '4444....4444',
        '.4444..4444.',
        '..44444444..',
        '...444444...',
        '....4444....',
        '....4444....',
        '...444444...',
        '..44444444..',
        '.4444..4444.',
        '4444....4444',
        '444......444',
    ]
    cv = _place(_art(rows, 'red'), 16)
    _runs_shade(cv, 'red4', 'red6', 'red3')
    return _outlined(cv)


def icon_star() -> Canvas:
    rows = [
        '......44......',
        '.....4433.....',
        '.....4433.....',
        '....444333....',
        '44444443333333',
        '.444444333333.',
        '..4444433333..',
        '...444443333..',
        '...44443333...',
        '..4444433333..',
        '..4443..4333..',
        '.443......433.',
        '.43........33.',
    ]
    cv = _place(_art(rows, 'gold'), 16)
    _edge_shade(cv, 'gold4', 'gold5', None)
    _edge_shade(cv, 'gold3', None, 'gold2')
    return _outlined(cv)


def icon_pickaxe() -> Canvas:
    # Рукоять по диагонали снизу-слева вверх-вправо; стальная голова — дуга с центром у хвата,
    # толстая в середине и острая к концам. Тон грани — по тому, смотрит ли она к свету (вверх-влево).
    cv = Canvas(16, 16)
    for i in range(11):
        x, y = 2 + i, 14 - i
        cv.set(x, y, 'wood5')
        cv.set(x + 1, y, 'wood3')
    cv.set(2, 14, 'wood3')
    cv.set(3, 14, 'wood2')
    head = Canvas(16, 16)
    cx, cy, rad = 3.0, 13.0, 9.5
    for y in range(16):
        for x in range(16):
            dx, dy = x + 0.5 - cx, cy - (y + 0.5)
            d = math.hypot(dx, dy)
            th = math.degrees(math.atan2(dy, dx))
            if not -4 <= th <= 94:
                continue
            t = 0.8 + 2.8 * math.sin(math.radians(min(90.0, max(0.0, th)) * 2.0))
            if abs(d - rad) > t / 2.0:
                continue
            outer, inner = d > rad + 0.5, d < rad - 0.5
            if (outer and th > 45) or (inner and th < 45):
                c = 'navy8'
            elif outer or inner:
                c = 'navy5'
            else:
                c = 'navy7'
            head.set(x, y, c)
    cv.blit(head, 0, 0)
    return _outlined(cv)


# ───────────────────────── объекты поля ─────────────────────────
def icon_forge() -> Canvas:
    # Кузнечная печь: труба слева, купол из камня, арка топки с огнём (жар — gold5 в центре).
    rows = [
        '.666..........',
        '.545..........',
        '.545..........',
        '.545.66666....',
        '.54565555555..',
        '6555555555554.',
        '65444444444443',
        '65545kkkk55543',
        '6554kFhhFk5443',
        '6654khHHhk5443',
        '6444khHHhk4443',
        '6554kFhhFk5433',
        '6554kffffk5433',
        '33333333333332',
    ]
    return _icon(rows, 'stone', {'f': 'amber1', 'F': 'amber2', 'h': 'amber3', 'H': 'gold5'})


def icon_gate() -> Canvas:
    # Забор-ворота: два столба, перекладины, косой крест и золотой замок в перекрестье (сектор закрыт).
    rows = [
        '.6..........6.',
        '654........654',
        '65455555555654',
        '65433333333654',
        '654w......w654',
        '654.w....w.654',
        '654..w..w..654',
        '654...GD...654',
        '654...gB...654',
        '654..w..w..654',
        '654.w....w.654',
        '654w......w654',
        '65455555555654',
        '54333333333543',
    ]
    return _icon(rows, 'wood', {'w': 'wood5', 'G': 'gold5', 'D': 'gold4', 'g': 'gold3', 'B': 'gold2'})


def icon_exit() -> Canvas:
    # Вход в штольню: деревянная рама, темнота внутри, рельсы уходят вглубь, вдали — огонёк.
    rows = [
        '66666666666665',
        '54444444444443',
        '.654kkkkkk654.',
        '.654kkkkkk654.',
        '.654kkkakk654.',
        '.654kjjjjk654.',
        '.654jjrrjj654.',
        '.654nnrrnn654.',
        '.654nrssrn654.',
        '.654nrnnrn654.',
        '.654rssssr654.',
        '.654rnnnnr654.',
        '.654rssssr654.',
        '.543r....r543.',
    ]
    return _icon(rows, 'wood', {'r': 'navy7', 's': 'wood3', 'n': 'navy1', 'a': 'amber4'})


def icon_shaft() -> Canvas:
    # Копёр в цветах шахты на поле: красное колесо со спицами на А-образной деревянной раме,
    # под ним красная крыша-трапеция и тёмный проём с дальней крепью. Слои (рама, колесо, крыша) —
    # каждый со своим контуром, чтобы колесо отделялось от рамы, а рама — от крыши.
    cv = Canvas(16, 16)
    legs = Canvas(16, 16)
    legs.line(6, 3, 1, 10, 'wood5')
    legs.line(7, 3, 2, 10, 'wood3')
    legs.line(7, 3, 12, 10, 'wood4')
    legs.line(8, 3, 13, 10, 'wood3')
    legs.hline(4, 10, 7, 'wood4')
    _layer(cv, _outlined(legs))
    wheel = Canvas(16, 16)
    wheel.blit(_art(SHAFT_WHEEL, None, SHAFT_WHEEL_LEGEND), 4, 0)
    _layer(cv, _outlined(wheel))
    _layer(cv, _outlined(_art(SHAFT_FRONT, None, SHAFT_FRONT_LEGEND)))
    return cv


# ───────────────────────── служебные ─────────────────────────
def icon_info() -> Canvas:
    cv = Canvas(16, 16)
    cv.blit(_disc(14, 'blue5', 'blue4', 'blue3', 'blue4'), 1, 1)
    cv.rect(7, 3, 2, 2, 'cream3')
    cv.rect(7, 6, 2, 6, 'cream3')
    cv.set(6, 6, 'cream3')
    cv.rect(6, 12, 4, 1, 'cream2')
    cv.set(8, 11, 'cream2')
    cv.set(8, 4, 'cream2')
    return _outlined(cv)


def icon_warning() -> Canvas:
    rows = [
        '......44......',
        '.....4433.....',
        '.....4433.....',
        '....44kk33....',
        '....44kk33....',
        '...444kk333...',
        '...444kk333...',
        '..4444kk3333..',
        '..4444333333..',
        '.44444kk33333.',
        '.44444kk33333.',
        '43333333333332',
        '22222222222222',
    ]
    cv = _place(_art(rows, 'gold'), 16)
    _edge_shade(cv, 'gold4', 'gold5', None)
    return _outlined(cv)


def _paw_art(w: int, h: int, mat: str = 'pink') -> Canvas:
    """Отпечаток лапки: четыре пальчика и подушечка, каждый со своим контуром."""
    cv = Canvas(w, h)
    blobs: List[Tuple[float, float, float, float]]
    if w <= 14:
        blobs = [(4.5, 2.5, 1.6, 2.2), (9.5, 2.5, 1.6, 2.2), (1.5, 6.5, 1.5, 2.0), (12.5, 6.5, 1.5, 2.0), (7.0, 10.0, 4.0, 3.2)]
    else:
        blobs = [(w * 0.36, h * 0.18, 2.2, 2.8), (w * 0.64, h * 0.18, 2.2, 2.8), (w * 0.12, h * 0.46, 2.0, 2.6),
                 (w * 0.88, h * 0.46, 2.0, 2.6), (w * 0.5, h * 0.72, 5.0, 4.2)]
    for cx, cy, rx, ry in blobs:
        b = Canvas(w, h)
        b.ellipse(cx, cy, rx, ry, f'{mat}3')
        _edge_shade(b, f'{mat}3', f'{mat}4', f'{mat}2')
        cv.blit(b, 0, 0)
    return cv


def icon_paw() -> Canvas:
    cv = Canvas(16, 16)
    cv.blit(_paw_art(14, 14), 1, 1)
    return _outlined(cv)


def icon_hand() -> Canvas:
    # Указатель обучения — кошачья лапка: «пальчик» вытянут к левому верхнему углу (кончик около пикселя (2, 2)
    # холста, т. е. (−5.5, +5.5) от центра), поджатые пальчики с розовыми подушечками, синий манжет комбинезона.
    cv = _place(_art(HAND_ROWS, None, {'w': 'cream3', 'b': 'blue4'}), 16)
    _edge_shade(cv, 'cream3', 'cream4', 'cream2')
    _edge_shade(cv, 'blue4', 'blue5', 'blue3')
    o = 1  # сдвиг арта на холсте
    detail = {
        (8, 5): 'cream1', (8, 4): 'cream1', (11, 5): 'cream1', (12, 6): 'cream1', (12, 7): 'cream2',  # складки пальчиков
        (3, 7): 'cream1', (4, 8): 'cream1',  # основание вытянутого пальчика
        (9, 6): 'pink3', (11, 7): 'pink3',  # подушечки поджатых пальчиков
        (6, 9): 'pink3', (7, 9): 'pink3', (8, 9): 'pink3', (6, 10): 'pink3', (7, 10): 'pink2', (8, 10): 'pink2',  # ладошка
    }
    for (x, y), c in detail.items():
        cv.set(x + o, y + o, c)
    return _outlined(cv)


def icon_income() -> Canvas:
    cv = Canvas(16, 16)
    coin = Canvas(13, 13)
    coin.blit(_coin_art(11, 7), 1, 1)
    coin.outline(OUTLINE)
    cv.blit(coin, 0, 3)
    arrow = Canvas(9, 9)
    arrow.blit(_arrow_up_art('green', 7, 4, 3, 3), 1, 1)
    arrow.outline(OUTLINE)
    cv.blit(arrow, 7, 0)
    return cv


# ───────────────────────── крупные 24×24 ─────────────────────────
def icon_chest_big() -> Canvas:
    # Сундук кнопки HUD (как в концепте): купольная крышка с досками, золотые оковки по краям,
    # золотой обод и накладка замка с тёмной скважиной.
    rows = [
        '....DC6666666666CB....',
        '..55DC5555555555CB44..',
        '.555DC5555555555CB444.',
        '5544DC4444444444CB4433',
        '4433DC3333333333CB3332',
        '5444DC4444444444CB4433',
        '4444DC4444444444CB4332',
        '3333DC3AAAAAAAA3CB3322',
        'DDDDDDDADDDDDDADDDDDDB',
        'BBBBBBBADCkkCBABBBBBBA',
        '5444DC4ADkkkkBA4CB4432',
        '5444DC4ADCkkCBA4CB4432',
        '5444DC4ADCkkCBA4CB4432',
        '4333DC3ABBBBBBA3CB3321',
        '5444DC4AAAAAAAA4CB4432',
        '5444DC4444444444CB4432',
        '4333DC3333333333CB3321',
        '4444DC4444444444CB4321',
        '3222BA2222222222BA2211',
    ]
    return _icon(rows, 'wood', CHEST_EXTRA, size=24)


def icon_cards_big() -> Canvas:
    return _cards(24, 12, 17, [(-20, 7, 12), (-9, 10, 11.5), (0, 14, 12)], PAW_7)


def icon_dynamite_big() -> Canvas:
    # Связка из трёх шашек: боковые позади, средняя впереди и выше; верёвка-обвязка, фитиль с искрой.
    cv = Canvas(24, 24)
    for sx, sy in [(2, 8), (15, 8), (8, 6)]:
        st = Canvas(9, 18)
        st.blit(_dynamite_stick(15, paper=False), 1, 1)
        st.outline(OUTLINE)
        cv.blit(st, sx - 1, sy - 1)
    rope = Canvas(24, 24)
    for x in range(2, 22):
        if cv.opaque(x, 15) and cv.get(x, 15)[:3] != C(OUTLINE)[:3]:
            rope.set(x, 15, 'wood5')
            rope.set(x, 16, 'wood3')
    cv.blit(rope, 0, 0)
    _put(cv, [(11, 5), (11, 4), (12, 3), (13, 2), (14, 2)], 'wood2')
    _outlined(cv)
    _spark(cv, 16, 2, big=True)
    _put(cv, [(19, 5), (13, 0), (20, 1)], 'gold4')
    return cv


def icon_cat_big() -> Canvas:
    # Мордочка рыжего котика 22×19 (+контур): ушки с розовым, полоски на лбу, счастливые глазки-дуги,
    # кремовая мордочка с носиком и открытым ротиком, румяные щёчки — как котик в HUD концепта.
    rows = [
        '.3..................2.',
        '.33................22.',
        '.343..............322.',
        '.3pp3............3pp2.',
        '.3ppp3..........3ppp2.',
        '.3pp33333333333333pp2.',
        '3443333333333333333322',
        '3443333333223333333322',
        '3433333323333233333322',
        '3433333333333333333322',
        '3433kk3333333333kk3322',
        '333k33k33333333k33k322',
        '3333333333333333333322',
        '33ppwwwwwwnnwwwwwwpp22',
        '3wwwwwwwkkmmkkwwwwwww2',
        '3wwwwwwwwkmmkwwwwwwww2',
        '.3wwwwwwwwkkwwwwwwww2.',
        '..2wwwwwwwwwwwwwwww2..',
        '...22vvvvvvvvvvvv22...',
    ]
    return _icon(rows, 'fur_ginger', CAT_EXTRA, size=24)


# Колесо копра 7×7 (как shaft_wheel на поле): красный обод — R свет сверху-слева, r основа, d тень снизу-справа;
# между спицами тёмный фон n, спицы S (верх/лево, на свету) и s (низ/право, в тени), ступица h.
SHAFT_WHEEL = [
    '..RRr..',
    '.RnSnr.',
    'RnnSnnd',
    'RSShssd',
    'rnnsnnd',
    '.rnsnd.',
    '..ddd..',
]
SHAFT_WHEEL_LEGEND = {'R': 'red4', 'r': 'red3', 'd': 'red2', 'n': 'navy1', 'S': 'stone5', 's': 'stone4', 'h': 'stone6'}
# Крыша-трапеция из красных досок над входом, стойки (W/x и w/x) и проём: k — устье, y — дальняя крепь, K — глубина.
SHAFT_FRONT = ['................'] * 9 + [
    '...RRRRRRRRR....',
    '..Rrrrrrrrrrrd..',
    '.rddddddddddddd.',
    '..Wxkyyyyyykwx..',
    '..WxkyKKKKykwx..',
    '..WxkyKKKKykwx..',
    '................',
]
SHAFT_FRONT_LEGEND = {'R': 'red4', 'r': 'red3', 'd': 'red2', 'W': 'wood5', 'w': 'wood4', 'x': 'wood3',
                      'y': 'wood2', 'k': 'ink2', 'K': 'ink1'}


def _spark(cv: Canvas, x: int, y: int, big: bool = False) -> None:
    """Искра на фитиле: белый центр, янтарные лучи, золотые точки-отлёты."""
    _put(cv, [(x, y - 1), (x, y + 1), (x - 1, y), (x + 1, y)], 'amber4')
    if big:
        _put(cv, [(x, y - 2), (x + 2, y), (x - 2, y), (x, y + 2)], 'amber3')
    cv.set(x, y, 'cream4')


HAND_ROWS = [
    '..ww..........',
    '.wwww.........',
    'wwwwww........',
    '.wwwwww.......',
    '..wwwwww.ww...',
    '...wwwwwwwwww.',
    '...wwwwwwwwwww',
    '..wwwwwwwwwwww',
    '..wwwwwwwwwwww',
    '..wwwwwwwwwwwb',
    '...wwwwwwwwbbb',
    '....wwwwwwbbbb',
    '......wwwbbbb.',
    '........bbb...',
]


def _prune(cv: Canvas, color: str = OUTLINE) -> Canvas:
    """Убрать «висячие» пиксели контура (у которых нет цветного соседа) — следы углов задних слоёв."""
    rgb = C(color)[:3]
    for _ in range(2):
        kill = []
        for y in range(cv.h):
            for x in range(cv.w):
                if not _same(cv, x, y, rgb):
                    continue
                nb = [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
                if not any(cv.opaque(a, b) and cv.get(a, b)[:3] != rgb for a, b in nb):
                    kill.append((x, y))
        for x, y in kill:
            cv.set(x, y, None)
    return cv


def icon_hand_big() -> Canvas:
    # Крупная лапка-указатель 24×24 для обучения (дополнительный спрайт): кончик пальчика около пикселя (2, 2)
    # холста, т. е. (−9.5, +9.5) от центра.
    cv = Canvas(24, 24)
    fur = Canvas(24, 24)
    for y in range(24):
        for x in range(24):
            if abs(x - y) <= 2 and 4 <= x + y <= 22 and x >= 1 and y >= 1:
                fur.set(x, y, 'cream3')
    _put(fur, [(1, 3), (3, 1), (2, 2)], 'cream3')
    fur.ellipse(13.5, 13.5, 8.0, 7.6, 'cream3')
    for cx, cy in ((11.5, 6.6), (16.0, 7.4), (19.6, 10.2)):
        fur.ellipse(cx, cy, 2.4, 2.3, 'cream3')
    for y in range(24):
        for x in range(24):
            if fur.opaque(x, y) and x + y >= 35:
                fur.set(x, y, 'blue4')
    _edge_shade(fur, 'cream3', 'cream4', 'cream2')
    _edge_shade(fur, 'blue4', 'blue5', 'blue3')
    for x in range(24):
        for y in range(24):
            if fur.opaque(x, y) and x + y == 35:
                fur.set(x, y, 'blue5')
    # складки между пальчиками и у основания вытянутого
    _put(fur, [(13, 6), (13, 7), (13, 8)], 'cream1')
    _put(fur, [(18, 8), (17, 9), (17, 10)], 'cream1')
    _put(fur, [(5, 11), (6, 12), (7, 12)], 'cream1')
    # подушечки
    for bx, by in ((10, 7), (15, 8), (19, 11)):
        _put(fur, [(bx, by), (bx + 1, by)], 'pink3')
        _put(fur, [(bx, by + 1), (bx + 1, by + 1)], 'pink2')
    fur.ellipse(12.5, 15.5, 3.2, 2.4, 'pink3')
    _put(fur, [(10, 14), (11, 14)], 'pink4')
    _put(fur, [(12, 17), (13, 17), (14, 17), (15, 16)], 'pink2')
    _put(fur, [(3, 3), (4, 3), (3, 4)], 'cream4')
    cv.blit(fur, 0, 0)
    return _outlined(cv)


PAW_7 = [
    '.cc.cc.',
    '.cc.cc.',
    'c.....c',
    'c.ccc.c',
    '.ccccc.',
    '.ccccc.',
    '..ccc..',
]


ARROW_DIRS = {
    'up': (0, -1), 'down': (0, 1), 'left': (-1, 0), 'right': (1, 0),
    'up_left': (-1, -1), 'up_right': (1, -1), 'down_left': (-1, 1), 'down_right': (1, 1),
}


def _arrow_dir(mat: str, d: Tuple[int, int]) -> Canvas:
    """Стрелка индикатора в одном из 8 направлений (поворот спрайтов запрещён — каждое направление нарисовано).
    Силуэт: прямые — из стрелки «вверх» отражением/транспонированием, диагональные — треугольник-голова в углу
    и косой стержень. Свет всегда сверху-слева: грань вдоль оси со стороны света светлее, кромки — блик/тень."""
    n = 14
    mask = set()
    if d[0] == 0 or d[1] == 0:
        up = _arrow_up_art('gold')
        pts = [(x, y) for y in range(up.h) for x in range(up.w) if up.opaque(x, y)]
        for x, y in pts:
            if d == (0, -1):
                mask.add((x, y))
            elif d == (0, 1):
                mask.add((x, n - 1 - y))
            elif d == (-1, 0):
                mask.add((y, x))
            else:
                mask.add((n - 1 - y, x))
    else:
        tmp = Canvas(n, n)
        tmp.polygon([(14.0, 0.0), (4.6, 0.0), (14.0, 9.4)], 'gold3')  # голова в правом верхнем углу
        tmp.polygon([(0.0, 11.2), (8.4, 2.8), (11.2, 5.6), (2.8, 14.0), (0.0, 14.0)], 'gold3')  # стержень
        pts = [(x, y) for y in range(n) for x in range(n) if tmp.opaque(x, y)]
        for x, y in pts:
            mask.add((x if d[0] > 0 else n - 1 - x, y if d[1] < 0 else n - 1 - y))
    hi, l2, base, dark = (f'{mat}5', f'{mat}4', f'{mat}3', f'{mat}2') if mat == 'gold' else (f'{mat}6', f'{mat}5', f'{mat}4', f'{mat}3')
    cv = Canvas(n, n)
    # ось стрелки проходит через центр; сторона оси, обращённая к свету (−1, −1), — светлая грань
    nx, ny = -d[1], d[0]
    sgn = 1 if (nx * -1 + ny * -1) > 0 else -1
    c = (n - 1) / 2.0
    for x, y in mask:
        side = ((x - c) * nx + (y - c) * ny) * sgn
        cv.set(x, y, l2 if side >= -0.01 else base)
    _edge_shade(cv, l2, hi, base)
    _edge_shade(cv, base, None, dark)
    return cv


def _arrow_icon(d: str, mat: str = 'gold') -> Canvas:
    return _outlined(_place(_arrow_dir(mat, ARROW_DIRS[d]), 16))


def icon_music() -> Canvas:
    """Музыка: две восьмые ноты с перемычкой (золото)."""
    rows = [
        '....55555555',
        '....54444445',
        '....53333335',
        '....5......5',
        '....5......5',
        '....5......5',
        '....5......5',
        '.3335...33335',
        '322345.322345',
        '3222345322234',
        '.33335..33335',
    ]
    rows = [r.replace('5', '4') if i > 2 else r for i, r in enumerate(rows)]
    return _icon(rows, 'gold')


def icon_ambience() -> Canvas:
    """Атмосфера пещеры: капля над волной (бирюза)."""
    rows = [
        '.....4.....',
        '....454....',
        '...45534...',
        '...45333...',
        '..4533332..',
        '..4333332..',
        '...33332...',
        '....222....',
        '...........',
        '22.....22..',
        '.2233322.22',
        '...222..22.',
    ]
    return _icon(rows, 'teal')


def icon_effects() -> Canvas:
    """Эффекты: большая искра и две маленькие (золото)."""
    rows = [
        '.....5.......',
        '.....5.......',
        '....454....4.',
        '.5544344..434',
        '..44333444.4.',
        '...433334....',
        '..44333444...',
        '.5544344.....',
        '....454...4..',
        '.....5...434.',
        '.....5....4..',
    ]
    return _icon(rows, 'gold')


# ───────────────────────── экипировка нового режима (ТЗ v2.1 §10) ─────────────────────────
# 16 предметов: слот × редкость. Рисунок задан в координатах 16×16 и растеризуется в масштабе k (1 — icon_item_*,
# 2 — icon_item_*_big 32×32 для окна «Экипировка»): у крупной иконки те же формы, но контур и блики по 1 px.
# Цвет редкости — как у рамок ui_card_frame_* (sprites/ui.py, RARITY): обычная — железо и натуральные материалы
# (серебристые акценты navy/stone), редкая — синяя сталь, эпическая — аметист, легендарная — золото с самоцветом.
SLOTS = ['pickaxe', 'hat', 'backpack', 'boots']
RARITIES = ['common', 'rare', 'epic', 'legendary']
# Материал: (тень, основа, свет, блик).
MATS: Dict[str, Tuple[str, str, str, str]] = {
    'iron': ('stone2', 'stone4', 'stone5', 'stone6'),
    'steel': ('blue2', 'blue4', 'blue5', 'blue6'),
    'amethyst': ('purple2', 'purple3', 'purple4', 'purple6'),
    'gold': ('gold1', 'gold3', 'gold4', 'gold5'),
    'wood': ('wood2', 'wood3', 'wood5', 'wood6'),
    'dwood': ('wood1', 'wood2', 'wood3', 'wood4'),
    'leather': ('wood2', 'wood4', 'wood5', 'wood6'),
    'canvas': ('cream0', 'cream1', 'cream2', 'cream3'),
    'cloth_blue': ('blue1', 'blue3', 'blue4', 'blue5'),
    'cloth_purple': ('purple1', 'purple2', 'purple3', 'purple4'),
    'cloth_gold': ('amber1', 'amber2', 'gold3', 'gold4'),
    'red': ('red1', 'red2', 'red3', 'red4'),
    'teal': ('teal2', 'teal3', 'teal4', 'teal6'),
    'fur': ('cream1', 'cream2', 'cream3', 'cream4'),
    'ghost': ('navy3', 'navy4', 'navy5', 'navy6'),
    # кованая сталь кирки common (как слой cat_pick_common_*: stone4/6/7 и заточка cream4)
    'forged': ('stone4', 'stone6', 'stone7', 'cream4'),
    'copper': ('fur_ginger1', 'fur_ginger2', 'fur_ginger3', 'fur_ginger4'),
    'wrap': ('wood3', 'wood5', 'wood6', 'wood7'),
    # шлем rare — те же тона, что у слоя cat_hat_rare_* (B blue3, L blue4, H blue5)
    'steel_hat': ('blue2', 'blue3', 'blue4', 'blue5'),
    'steel_gray': ('stone3', 'stone5', 'stone6', 'stone7'),
    'velvet': ('red1', 'red2', 'red3', 'red4'),
    'leather_pack': ('wood2', 'wood3', 'wood4', 'wood5'),
    'red_pack': ('red2', 'red3', 'red4', 'red5'),
}


class Vec:
    """Растеризация фигур в координатах 16×16 на холсте 16·k: многоугольник, эллипс, толстый отрезок."""

    def __init__(self, k: int):
        self.k = k
        self.n = 16 * k

    def canvas(self) -> Canvas:
        return Canvas(self.n, self.n)

    def poly(self, cv: Canvas, pts: Sequence[Tuple[float, float]], c: str) -> None:
        cv.polygon([(x * self.k, y * self.k) for x, y in pts], c)

    def ell(self, cv: Canvas, cx: float, cy: float, rx: float, ry: float, c: str) -> None:
        cv.ellipse(cx * self.k, cy * self.k, rx * self.k, ry * self.k, c)

    def rect(self, cv: Canvas, x: float, y: float, w: float, h: float, c: str) -> None:
        k = self.k
        cv.rect(int(round(x * k)), int(round(y * k)), max(1, int(round(w * k))), max(1, int(round(h * k))), c)

    def bar(self, cv: Canvas, x0: float, y0: float, x1: float, y1: float, w: float, mat: Tuple[str, str, str, str]) -> None:
        """Толстый отрезок (рукоять): левая/верхняя сторона — свет, правая/нижняя — тень."""
        k = self.k
        ax, ay, bx, by = x0 * k, y0 * k, x1 * k, y1 * k
        dx, dy = bx - ax, by - ay
        ln = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / ln, dx / ln          # нормаль
        if nx + ny > 0:
            nx, ny = -nx, -ny               # нормаль к свету (вверх-влево)
        hw = w * k / 2.0
        for y in range(cv.h):
            for x in range(cv.w):
                px, py = x + 0.5 - ax, y + 0.5 - ay
                t = (px * dx + py * dy) / (ln * ln)
                if not -0.02 <= t <= 1.02:
                    continue
                d = px * nx + py * ny
                if abs(d) <= hw:
                    rel = d / hw if hw else 0
                    cv.set(x, y, mat[2] if rel > 0.25 else (mat[0] if rel < -0.45 else mat[1]))


def _shade_mat(cv: Canvas, mat: Tuple[str, str, str, str], rim: bool = True) -> None:
    """Объём по силуэту для пикселей основы mat[1]: свет у верхней/левой кромки, тень у нижней/правой; rim — блик."""
    _edge_shade(cv, mat[1], mat[2], mat[0])
    if rim:
        rgb = C(mat[2])[:3]
        marks = [(x, y) for y in range(cv.h) for x in range(cv.w)
                 if _same(cv, x, y, rgb) and not cv.opaque(x, y - 1) and not cv.opaque(x - 1, y)]
        for x, y in marks:
            cv.set(x, y, mat[3])


def _sparkle(cv: Canvas, x: int, y: int, k: int) -> None:
    """Звёздочка-блеск легендарного предмета (центр (x, y))."""
    arms = 1 if k == 1 else 2
    for d in range(1, arms + 1):
        for dx, dy in ((d, 0), (-d, 0), (0, d), (0, -d)):
            cv.set(x + dx, y + dy, 'gold5' if d == arms else 'cream4')
    cv.set(x, y, 'white')


def _gem(cv: Canvas, x: int, y: int, k: int, mat: Tuple[str, str, str, str]) -> None:
    """Самоцвет-вставка: ромб 3×3 (k = 1) или 5×5 (k = 2) с бликом; (x, y) — центр."""
    r = k
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if abs(dx) + abs(dy) <= r:
                cv.set(x + dx, y + dy, mat[2] if dx + dy < 0 else (mat[0] if dx + dy > 0 else mat[1]))
    cv.set(x - (r - 1 if r > 1 else 0), y - (r - 1 if r > 1 else 1) + (0 if r > 1 else 1), mat[3])
    if r == 1:
        cv.set(x, y - r, mat[3])


# ── кирка ──
PICK = {
    # обычная — кованое кайло-тесло, как слой cat_pick_common_*: светлая сталь, широкое плоское лезвие со светлой
    # заточкой на верхнем конце, медная обойма и светлая обмотка рукояти (не повторяет HUD-иконку icon_pickaxe)
    'common': dict(head='forged', handle='wood', grip='wrap', gem=None, crystal=False, blade=True, ferrule='copper'),
    'rare': dict(head='steel', handle='wood', grip='red', gem=None, crystal=False, blade=False, ferrule='iron'),
    'epic': dict(head='amethyst', handle='dwood', grip='cloth_gold', gem=None, crystal=True, blade=False, ferrule='gold'),
    'legendary': dict(head='gold', handle='dwood', grip='red', gem='teal', crystal=False, blade=False, ferrule='gold'),
}


def item_pickaxe(rar: str, k: int) -> Canvas:
    """Кирка: рукоять по диагонали снизу-слева вверх-вправо, головка — дуга с центром у хвата (как icon_pickaxe).
    Обычная — кайло-тесло: верхний конец расширяется в плоское лезвие с заточкой; редкая — синяя сталь и обмотка
    рукояти; эпическая — аметистовая головка с гранями и острыми кристальными концами; легендарная — золотая
    головка с бирюзовым камнем и блеском."""
    d = PICK[rar]
    V = Vec(k)
    cv = V.canvas()
    head_m, handle_m = MATS[d['head']], MATS[d['handle']]
    hand = V.canvas()
    V.bar(hand, 2.2, 14.0, 12.4, 3.8, 1.9, handle_m)
    if d['grip']:
        g = MATS[d['grip']]
        for t in (0.08, 0.2, 0.32):
            gx, gy = 2.2 + (12.4 - 2.2) * t, 14.0 + (3.8 - 14.0) * t
            gl = V.canvas()
            V.bar(gl, gx - 0.45, gy + 0.45, gx + 0.45, gy - 0.45, 2.6, g)
            hand.blit(gl, 0, 0)
    cv.blit(hand, 0, 0)
    head = V.canvas()
    cx, cy, rad = 3.0 * k, 13.0 * k, 9.5 * k
    span = (-6, 96) if d['crystal'] else ((-4, 90) if d['blade'] else (-4, 94))
    for y in range(V.n):
        for x in range(V.n):
            dx, dy = x + 0.5 - cx, cy - (y + 0.5)
            dd = math.hypot(dx, dy)
            th = math.degrees(math.atan2(dy, dx))
            if not span[0] <= th <= span[1]:
                continue
            prof = math.sin(math.radians(min(90.0, max(0.0, th)) * 2.0))
            t = (0.8 + 2.8 * prof) * k
            if d['crystal']:
                t = (0.6 + 3.4 * prof ** 0.7) * k
            if d['blade'] and th > 45:
                # лезвие тесла: от середины головки к верхнему концу не сужается, а расширяется
                t = max(t, (2.9 + 1.0 * (th - 45) / 45) * k)
            if abs(dd - rad) > t / 2.0:
                continue
            outer, inner = dd > rad + t / 2.0 - 1.0, dd < rad - t / 2.0 + 1.0
            if (outer and th > 45) or (inner and th < 45):
                c = head_m[3] if not d['blade'] else head_m[2]
            elif outer or inner:
                c = head_m[0]
            else:
                c = head_m[2] if not d['blade'] else head_m[1]
            if d['crystal'] and not (outer or inner) and 12 < th < 78:
                # Грани кристалла: ребро-блик по середине, нижняя грань темнее.
                c = head_m[3] if abs(dd - rad) < 0.5 * k else (head_m[1] if dd < rad else head_m[2])
            if d['blade'] and th > span[1] - 7.0 / k:
                c = head_m[3]           # заточенная кромка лезвия
            head.set(x, y, c)
    cv.blit(head, 0, 0)
    # Обойма головки на рукояти.
    fx, fy = int(round(cx + rad * math.cos(math.radians(45)) - 0.5 * k)), int(round(cy - rad * math.sin(math.radians(45)) - 0.5 * k))
    ferr = MATS[d['ferrule']]
    for yy in range(fy - k, fy + k + 1):
        for xx in range(fx - k, fx + k + 1):
            if abs(xx - fx) + abs(yy - fy) <= k:
                cv.set(xx, yy, ferr[1] if xx + yy > fx + fy else ferr[2])
    if d['gem']:
        _gem(cv, fx, fy, k, MATS[d['gem']])
    _outlined(cv)
    if rar == 'legendary':
        _sparkle(cv, 13 * k, 12 * k if k == 1 else 25, k)
    return cv


# ── головной убор ──
def _hat_ears(cv: Canvas, V: 'Vec', k: int) -> None:
    """Кончики ушей позади убора: кремовая шерсть, розовое нутро (ТЗ: открытый силуэт ушей)."""
    for side in (-1, 1):
        ear = V.canvas()
        ex = 8 + side * 3.7
        V.poly(ear, [(ex - 1.7, 7.0), (ex + 1.7, 7.0), (ex + side * 0.9, 2.4)], 'cream2')
        V.poly(ear, [(ex - 0.7, 6.6), (ex + 0.9, 6.6), (ex + side * 0.7, 4.2)], 'pink2')
        ear.fill_where(lambda x, y, ex=ex: ear.get(x, y)[:3] == C('cream2')[:3] and x < ex * k - 0.5 * k, 'cream3')
        ear.outline(OUTLINE)
        cv.blit(ear, 0, 0)


def _hat_dome(V: 'Vec', k: int, mat: Tuple[str, str, str, str], cy: float = 10.6, rx: float = 5.2, ry: float = 5.9,
              band: Optional[Tuple[str, str, str, str]] = None) -> Canvas:
    """Купол убора с бликом-дугой слева и рантом по нижнему краю."""
    dome = V.canvas()
    V.ell(dome, 8, cy, rx, ry, mat[1])
    clip(dome, lambda x, y: y < cy * k)
    _shade_mat(dome, mat)
    for y in range(dome.h):
        for x in range(dome.w):
            if dome.opaque(x, y) and abs(math.hypot((x + 0.5) / k - 7.2, ((y + 0.5) / k - (cy - 0.8)) * 0.95) - (ry - 2.2)) < 0.5 / k + 0.25 \
                    and (x + 0.5) / k < 6.8 and (y + 0.5) / k < cy - 2.2:
                dome.set(x, y, mat[3])
    if band:
        for x in range(dome.w):
            col = [y for y in range(dome.h) if dome.opaque(x, y)]
            if col:
                for t in range(k):
                    dome.set(x, col[-1] - t, band[2] if x < 8 * k else band[1])
    dome.outline(OUTLINE)
    return dome


def _hat_brim(V: 'Vec', k: int, mat: Tuple[str, str, str, str], cx: float = 8.0, rx: float = 7.4) -> Canvas:
    brim = V.canvas()
    V.ell(brim, cx, 11.2, rx, 1.9, mat[1])
    clip(brim, lambda x, y: y >= 10.4 * k)
    _shade_mat(brim, mat, rim=False)
    brim.outline(OUTLINE)
    return brim


def item_hat(rar: str, k: int) -> Canvas:
    """Головной убор — по силуэту и главным цветам слоя cat_hat_<редкость>_* (sprites/cats.py), уши всегда открыты.
    Обычный — мягкая кожаная шапка: низкая тулья со швом, ремешок с латунной пряжкой, козырёк вперёд-вправо, без
    фонаря. Редкий — шлем из синей стали (blue3–5) со стальным гребнем, крупным фонарём холодного света и стальными
    заклёпками. Эпический — фиолетовый шлем с высоким бирюзовым кристаллом в золотой оправе. Легендарный — золотая
    корона без поля: четыре зубца, красная бархатная шапочка, бирюза, рубин с белым бликом и аметист на ободе."""
    V = Vec(k)
    cv = V.canvas()
    _hat_ears(cv, V, k)
    if rar == 'common':
        cv.blit(_hat_dome(V, k, MATS['leather'], cy=10.8, rx=5.5, ry=5.3), 0, 0)
        # Шов тульи и ремешок с пряжкой.
        seam = V.canvas()
        for t in range(9):
            sx, sy = 8.4 + 0.1 * t, 5.9 + 0.4 * t
            seam.set(int(sx * k), int(sy * k), 'wood2')
        cv.blit(seam, 0, 0)
        strap = V.canvas()
        V.rect(strap, 3.4, 9.3, 9.2, 1.3, 'wood1')
        clip(strap, lambda x, y: cv.opaque(x, y))
        cv.blit(strap, 0, 0)
        buckle = V.canvas()
        V.rect(buckle, 6.8, 9.0, 2.2, 1.9, 'gold3')
        buckle.fill_where(lambda x, y: x < 7.4 * k and y < 9.6 * k, 'gold5')
        V.rect(buckle, 7.5, 9.6, 0.8, 0.7, 'wood1')
        buckle.outline('gold1')
        cv.blit(buckle, 0, 0)
        cv.blit(_hat_brim(V, k, MATS['dwood'], cx=9.0, rx=7.0), 0, 0)
        return cv
    if rar == 'rare':
        crest = V.canvas()
        V.poly(crest, [(6.3, 7.0), (6.9, 2.4), (8.4, 1.8), (9.5, 7.0)], 'stone6')
        crest.fill_where(lambda x, y: x < 7.4 * k, 'stone7')
        crest.fill_where(lambda x, y: x >= 8.8 * k, 'stone4')
        crest.outline(OUTLINE)
        cv.blit(crest, 0, 0)
        cv.blit(_hat_dome(V, k, MATS['steel_hat'], band=MATS['steel_gray']), 0, 0)
        cv.blit(_hat_brim(V, k, MATS['steel_hat']), 0, 0)
        # Стальные заклёпки по ободу.
        for rx_ in (3.6, 5.6, 11.6):
            cv.set(int(rx_ * k), int(10.1 * k) - (k - 1), 'stone7')
        lamp = V.canvas()
        V.ell(lamp, 9.0, 7.6, 2.4, 2.2, 'stone4')
        _shade_mat(lamp, MATS['steel_gray'], rim=False)
        V.ell(lamp, 9.0, 7.6, 1.45, 1.35, 'blue6')
        lamp.fill_where(lambda x, y: lamp.get(x, y)[:3] == C('blue6')[:3] and x + y < 16.4 * k, 'blue7')
        lamp.outline(OUTLINE)
        cv.blit(lamp, 0, 0)
        cv.set(int(8.4 * k), int(7.0 * k), 'white')
        return cv
    if rar == 'epic':
        cv.blit(_hat_dome(V, k, MATS['amethyst'], band=MATS['gold']), 0, 0)
        cv.blit(_hat_brim(V, k, MATS['cloth_purple']), 0, 0)
        # Высокий кристалл-фонарь над куполом в золотой оправе.
        cr = V.canvas()
        V.poly(cr, [(7.0, 8.4), (7.3, 3.6), (8.5, 0.9), (9.7, 3.6), (10.0, 8.4)], 'teal4')
        cr.fill_where(lambda x, y: x < 8.5 * k, 'teal5')
        cr.fill_where(lambda x, y: x >= 9.2 * k, 'teal3')
        for y in range(cr.h):
            x = int(8.5 * k) - 1 if k > 1 else int(8.0 * k)
            if cr.opaque(x, y) and y < 6.5 * k:
                cr.set(x, y, 'teal6')
        cr.outline(OUTLINE)
        cv.blit(cr, 0, 0)
        mount = V.canvas()
        V.rect(mount, 6.4, 8.2, 4.2, 1.6, 'gold3')
        mount.fill_where(lambda x, y: y < 8.8 * k, 'gold4')
        mount.outline(OUTLINE)
        cv.blit(mount, 0, 0)
        return cv
    # legendary: корона
    vel = V.canvas()
    V.ell(vel, 8, 9.6, 4.6, 5.6, 'red2')
    clip(vel, lambda x, y: y < 9.6 * k)
    _shade_mat(vel, MATS['velvet'])
    vel.outline(OUTLINE)
    cv.blit(vel, 0, 0)
    crown = V.canvas()
    for sx, top in ((4.2, 4.8), (6.8, 2.2), (9.2, 2.2), (11.8, 4.8)):
        V.poly(crown, [(sx - 1.0, 9.6), (sx, top), (sx + 1.0, 9.6)], 'gold3')
    V.rect(crown, 3.2, 9.0, 9.6, 3.2, 'gold3')
    _shade_mat(crown, MATS['gold'])
    crown.outline(OUTLINE)
    for sx, top in ((4.2, 4.8), (6.8, 2.2), (9.2, 2.2), (11.8, 4.8)):
        crown.set(int(sx * k), int(top * k) + (1 if k == 1 else 1), 'cream4')
    # Самоцветы на ободе: бирюза, рубин с белым бликом, аметист.
    for gx, mat in ((5.0, MATS['teal']), (8.0, MATS['red']), (11.0, ('purple2', 'purple3', 'purple4', 'purple6'))):
        _gem(crown, int(gx * k), int(10.6 * k), k, mat)
    crown.set(int(8.0 * k) - (1 if k > 1 else 0), int(10.6 * k) - 1, 'white')
    cv.blit(crown, 0, 0)
    _sparkle(cv, 13 * k + (0 if k == 1 else 1), 3 * k, k)
    return cv


def inside(pts: Sequence[Tuple[float, float]], x: float, y: float) -> bool:
    c = False
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        if (y0 <= y < y1) or (y1 <= y < y0):
            if x < x0 + (y - y0) * (x1 - x0) / (y1 - y0):
                c = not c
    return c


def clip(cv: Canvas, keep) -> None:
    for y in range(cv.h):
        for x in range(cv.w):
            if cv.px[x, y][3] and not keep(x, y):
                cv.px[x, y] = (0, 0, 0, 0)


# ── рюкзак ──
def item_backpack(rar: str, k: int) -> Canvas:
    """Рюкзак анфас: скруглённый корпус, клапан с пряжкой, передний карман со своим клапаном, ремни, петля сверху.
    Главный цвет — как у слоя cat_pack_<редкость>_* (sprites/cats.py). Обычный — кожаный (wood3) с латунной
    пряжкой; редкий — синий брезент, латунная пряжка; эпический — фиолетовый со скаткой бирюзового одеяла сверху
    и золотыми пряжками; легендарный — красная кожа (red3) с золотым клапаном-ободом, рубином, золотой лапкой
    на корпусе, золотой окантовкой низа и блеском."""
    V = Vec(k)
    cv = V.canvas()
    body_m = MATS[{'common': 'leather_pack', 'rare': 'cloth_blue', 'epic': 'cloth_purple', 'legendary': 'red_pack'}[rar]]
    flap_m = MATS[{'common': 'dwood', 'rare': 'leather', 'epic': 'amethyst', 'legendary': 'red'}[rar]]
    strap_m = MATS['gold'] if rar == 'legendary' else MATS['leather']
    loop_m = MATS['dwood'] if rar == 'legendary' else strap_m
    buckle = MATS['gold']
    top = 3.4 if rar != 'epic' else 4.6
    loop = V.canvas()
    V.ell(loop, 8, top + 0.2, 2.3, 1.9, loop_m[1])
    V.ell(loop, 8, top + 0.4, 1.1, 0.9, 'white')
    loop.fill_where(lambda x, y: loop.get(x, y)[:3] == (255, 255, 255), None)
    loop.outline(OUTLINE)
    cv.blit(loop, 0, 0)
    body = V.canvas()
    V.rect(body, 2.6, top + 2.0, 10.8, 12.2 - top, body_m[1])
    V.ell(body, 8, top + 2.2, 5.4, 1.8, body_m[1])
    clip(body, lambda x, y: not ((x < 3.4 * k or x >= 12.6 * k) and y >= 14.2 * k - 0.5))
    _shade_mat(body, body_m)
    body.outline(OUTLINE)
    cv.blit(body, 0, 0)
    if rar == 'legendary':
        # Золотая окантовка низа и золотая лапка на корпусе (как на слое рюкзака).
        trim = V.canvas()
        V.rect(trim, 2.6, 13.0, 10.8, 1.0, 'gold3')
        clip(trim, lambda x, y: cv.opaque(x, y) and cv.get(x, y)[:3] != C(OUTLINE)[:3])
        trim.fill_where(lambda x, y: x < 5 * k, 'gold4')
        cv.blit(trim, 0, 0)
        paw = V.canvas()
        V.ell(paw, 8.0, 11.9, 1.9, 1.3, 'gold4')
        for tx_, ty_ in ((5.9, 10.4), (7.2, 9.6), (8.8, 9.6), (10.1, 10.4)):
            V.ell(paw, tx_, ty_, 0.5 if k == 1 else 0.7, 0.5 if k == 1 else 0.7, 'gold4')
        paw.fill_where(lambda x, y: x + y < 17.6 * k, 'gold5')
        paw.fill_where(lambda x, y: x + y > 21.0 * k, 'gold3')
        cv.blit(paw, 0, 0)
    else:
        # Передний карман: рамка темнее, свой клапан.
        pocket = V.canvas()
        V.rect(pocket, 4.4, 9.4, 7.2, 4.2, body_m[1])
        _shade_mat(pocket, body_m, rim=False)
        V.rect(pocket, 4.4, 9.4, 7.2, 1.4, flap_m[1])
        pocket.fill_where(lambda x, y: pocket.get(x, y)[:3] == C(flap_m[1])[:3] and y < 9.9 * k and x < 11 * k, flap_m[2])
        pocket.outline(body_m[0])
        cv.blit(pocket, 0, 0)
    flap = V.canvas()
    V.rect(flap, 2.6, top + 2.0, 10.8, 2.6, flap_m[1])
    V.ell(flap, 8, top + 2.2, 5.4, 1.6, flap_m[1])
    V.poly(flap, [(6.2, top + 4.4), (9.8, top + 4.4), (8.0, top + 6.0)], flap_m[1])
    _shade_mat(flap, flap_m)
    if rar == 'legendary':
        # Золотой обод по нижнему краю клапана (горловина слоя рюкзака — золотая).
        for x in range(flap.w):
            col = [y for y in range(flap.h) if flap.opaque(x, y)]
            if col:
                for t in range(k):
                    flap.set(x, col[-1] - t, 'gold4' if x < 8 * k else 'gold3')
                flap.set(x, col[0], 'gold5' if x < 8 * k else 'gold4')
    flap.outline(OUTLINE)
    cv.blit(flap, 0, 0)
    # Ремни от клапана к низу кармана (у легендарного — золотые, по бокам от лапки).
    for sx in ((5.0, 10.2) if rar != 'legendary' else (3.6, 11.6)):
        st = V.canvas()
        V.rect(st, sx, top + 4.2, 0.85, 13.6 - top - 4.2, strap_m[1])
        st.fill_where(lambda x, y: True, strap_m[1])
        cv.blit(st, 0, 0)
    bx, by = int(round(8 * k - 0.5)), int(round((top + 5.0) * k))
    if rar == 'legendary':
        _gem(cv, bx, by, k, MATS['red'])
    else:
        b2 = V.canvas()
        V.rect(b2, 7.3, top + 4.5, 1.5, 1.4, buckle[1])
        b2.fill_where(lambda x, y: x < 7.9 * k, buckle[3])
        cv.blit(b2, 0, 0)
    if rar == 'epic':
        roll = V.canvas()
        V.ell(roll, 8, 4.0, 5.8, 1.7, MATS['teal'][1])
        _shade_mat(roll, MATS['teal'])
        for xx in (4.8, 10.6):
            V.rect(roll, xx, 2.4, 0.8 if k == 1 else 0.6, 3.2, MATS['leather'][1])
        roll.outline(OUTLINE)
        cv.blit(roll, 0, 0)
    if rar == 'legendary':
        _sparkle(cv, 13 * k + (0 if k == 1 else 1), 3 * k, k)
    return cv


# ── обувь ──
def item_boots(rar: str, k: int) -> Canvas:
    """Пара рабочих ботинок в профиль (носки вправо, дальний ботинок темнее и выше).
    Обычные — коричневая кожа; редкие — синие со стальным носком; эпические — фиолетовые с меховой опушкой и
    золотыми пряжками; легендарные — золотые с белым крылышком у голенища (скорость)."""
    V = Vec(k)
    cv = V.canvas()
    m = MATS[{'common': 'leather', 'rare': 'cloth_blue', 'epic': 'amethyst', 'legendary': 'gold'}[rar]]
    sole = MATS['dwood'] if rar != 'legendary' else MATS['leather']

    def boot(dx: float, dy: float, far: bool) -> Canvas:
        b = V.canvas()
        mm = tuple([m[0], m[0], m[1], m[2]]) if far else m
        V.rect(b, 3.0 + dx, 2.6 + dy, 5.0, 8.0, mm[1])
        V.poly(b, [(3.0 + dx, 9.0 + dy), (10.2 + dx, 9.0 + dy), (12.0 + dx, 10.6 + dy), (12.2 + dx, 12.4 + dy), (3.0 + dx, 12.4 + dy)], mm[1])
        _shade_mat(b, mm, rim=not far)
        V.rect(b, 2.8 + dx, 12.2 + dy, 9.6, 1.2, sole[0] if far else sole[1])
        if rar == 'rare':
            toe = V.canvas()
            V.poly(toe, [(9.0 + dx, 9.0 + dy), (10.2 + dx, 9.0 + dy), (12.0 + dx, 10.6 + dy), (12.2 + dx, 12.2 + dy), (9.0 + dx, 12.2 + dy)],
                   'stone4')
            _shade_mat(toe, MATS['iron'], rim=False)
            b.blit(toe, 0, 0)
        if rar == 'epic':
            V.rect(b, 2.6 + dx, 2.0 + dy, 5.8, 1.8, 'cream2')
            b.fill_where(lambda x, y: b.get(x, y)[:3] == C('cream2')[:3] and x < (4.0 + dx) * k, 'cream3')
            for yy in (6.0, 8.6):
                V.rect(b, 7.0 + dx, yy + dy, 1.2, 0.9 if k == 1 else 0.75, 'gold4')
        else:
            V.rect(b, 2.8 + dx, 2.2 + dy, 5.4, 1.3, mm[0] if far else (m[0] if rar != 'legendary' else 'gold2'))
        # Шнуровка.
        if rar in ('common', 'rare') and not far:
            for yy in (4.6, 6.4, 8.2):
                V.rect(b, 7.2 + dx, yy + dy, 0.8, 0.8 if k == 1 else 0.6, 'cream2')
        b.outline(OUTLINE)
        clip(b, lambda x, y: x < V.n)
        return b

    ox = 0.6 if rar != 'legendary' else 1.2
    cv.blit(boot(ox + 1.7, -1.6, True), 0, 0)
    cv.blit(boot(ox - 0.6, 0.9, False), 0, 0)
    if rar == 'legendary':
        wing = V.canvas()
        V.poly(wing, [(4.2, 5.0), (1.4, 2.4), (1.6, 4.0), (0.8, 4.4), (1.6, 5.8), (1.0, 6.6), (3.0, 7.6), (4.2, 7.6)], 'cream3')
        wing.fill_where(lambda x, y: y > 6.4 * k, 'cream2')
        wing.fill_where(lambda x, y: y < 4.4 * k, 'cream4')
        wing.outline(OUTLINE)
        cv.blit(wing, 0, 0)
        _sparkle(cv, 14 * k - (1 if k == 1 else 2), 3 * k, k)
    return cv


ITEM_FN = {'pickaxe': item_pickaxe, 'hat': item_hat, 'backpack': item_backpack, 'boots': item_boots}


def item_slot(slot: str) -> Canvas:
    """Пустой слот экипировки: силуэт обычного предмета в тусклых тонах navy (заглушка ячейки), контур navy1."""
    cv = ITEM_FN[slot]('common', 1)
    pal = MATS['ghost']
    table = {}
    for y in range(cv.h):
        for x in range(cv.w):
            p = cv.get(x, y)
            if p[3]:
                lum = 0.3 * p[0] + 0.59 * p[1] + 0.11 * p[2]
                if p[:3] == C(OUTLINE)[:3]:
                    cv.set(x, y, 'navy1')
                else:
                    cv.set(x, y, pal[0] if lum < 70 else pal[1] if lum < 120 else pal[2] if lum < 175 else pal[3])
    return cv


# ───────────────────────── ветки базы и индикаторы нового режима ─────────────────────────
HEAD_SMALL = [
    'L........B',
    'LL......BB',
    'LpL....BpB',
    'LppBBBBBpB',
    'LBBBBBBBBD',
    'BBkBBBBkBD',
    'BBBBBBBBBD',
    'BwwwnnwwwD',
    '.wwwwwwww.',
]


def _head(fur: str) -> Canvas:
    r = f'fur_{fur}'
    cv = Canvas.from_ascii(HEAD_SMALL, {'L': f'{r}3', 'B': f'{r}2', 'D': f'{r}1', 'p': 'pink2', 'k': 'ink1', 'w': 'cream3',
                                        'n': 'pink3'})
    out = Canvas(cv.w + 2, cv.h + 2)
    out.blit(cv, 1, 1)
    out.outline(OUTLINE)
    return out


def icon_branch_team() -> Canvas:
    """Ветка «Команда»: два котика — серый сзади, рыжий впереди."""
    cv = Canvas(16, 16)
    cv.blit(_head('gray'), 4, 0)
    cv.blit(_head('ginger'), 0, 5)
    return cv


def icon_branch_pickaxes() -> Canvas:
    """Ветка «Кирки» (сила удара команды): две скрещённые железные кирки и искра удара."""
    a = item_pickaxe('common', 1)
    cv = Canvas(16, 16)
    cv.blit(a.flipped(), 0, 0)
    cv.blit(a, 0, 0)
    _put(cv, [(7, 0), (7, 2), (6, 1), (8, 1)], 'gold4')
    cv.set(7, 1, 'cream4')
    return cv


BOOT_RUN = [
    '......66665..',
    '......5444k..',
    '......5444...',
    '......54w3...',
    '......5444...',
    '......54w33..',
    '......544433.',
    '......5444433',
    '......5444443',
    '......2222222',
]


def icon_branch_boots() -> Canvas:
    """Ветка «Обувь» (скорость): ботинок и полоски движения позади."""
    cv = _place(_art(BOOT_RUN, 'wood', {'w': 'cream2', 'k': 'wood2'}), 16, 1, 1)
    _outlined(cv)
    for x0, x1, y in ((2, 5, 6), (0, 4, 9), (3, 5, 12)):
        cv.hline(x0, x1, y, 'cream3')
        cv.set(x0, y, 'cream1')
    return cv


def _sack_art(small: bool = False) -> Canvas:
    from sprites.loot import SACK, SACK_SMALL, SACK_LEG
    return Canvas.from_ascii(SACK_SMALL if small else SACK, SACK_LEG)


def icon_branch_bags() -> Canvas:
    """Ветка «Сумки» (вместимость): большой мешок, полный монет, и монетка сверху."""
    cv = Canvas(16, 16)
    cv.blit(_sack_art(), 2, 1)
    coin = Canvas(16, 16)
    coin.blit(_coin_art(6, 4), 9, 0)
    coin.outline(OUTLINE)
    cv.blit(coin, 0, 0)
    return cv


def icon_sack() -> Canvas:
    """Груз в пути (статус доставки, не кошелёк): мешочек с монетами и полоски движения — его несут на базу."""
    cv = Canvas(16, 16)
    cv.blit(_sack_art(True), 6, 2)
    for x0, x1, y in ((1, 4, 6), (0, 4, 9), (2, 4, 12)):
        cv.hline(x0, x1, y, 'cream3')
        cv.set(x0, y, 'cream1')
    return cv


CRATE_OPEN = [
    '444444444444',
    '4kkkkkkkkkk2',
    '5BCCBCCBCCB3',
    '544444444443',
    '5DDDDDDDDDD3',
    '544444444443',
    '533333333332',
    '522222222221',
]


def icon_branch_unload() -> Canvas:
    """Ветка «Разгрузка» (скорость сдачи): монеты сыплются в открытый ящик склада."""
    cv = Canvas(16, 16)
    crate = Canvas(16, 16)
    crate.blit(_art(CRATE_OPEN, 'wood', {'B': 'gold2', 'C': 'gold4', 'D': 'wood2'}), 2, 7)
    crate.outline(OUTLINE)
    cv.blit(crate, 0, 0)
    for x, y in ((4, 1), (9, 3), (12, 0)):
        c = Canvas(16, 16)
        c.blit(_coin_art(4, 2), x, y)
        c.outline(OUTLINE)
        cv.blit(c, 0, 0)
    return cv


def icon_branch_value() -> Canvas:
    """Ветка «Оценка добычи» (стоимость порций): монета и увеличительное стекло с бликом поверх её края."""
    cv = Canvas(16, 16)
    coin = Canvas(16, 16)
    coin.blit(_coin_art(11, 7), 1, 1)
    coin.outline(OUTLINE)
    cv.blit(coin, 0, 0)
    lens = Canvas(16, 16)
    lens.ellipse(10.5, 10.5, 3.4, 3.4, 'stone5')
    lens.ellipse(10.5, 10.5, 2.2, 2.2, 'blue6')
    lens.fill_where(lambda x, y: lens.get(x, y)[:3] == C('blue6')[:3] and x + y > 21, 'blue5')
    lens.fill_where(lambda x, y: lens.get(x, y)[:3] == C('stone5')[:3] and x + y > 22, 'stone3')
    lens.set(9, 9, 'white')
    for i in range(2):
        lens.set(13 + i, 13 + i, 'wood4')
        lens.set(14 + i, 13 + i, 'wood2')
    lens.outline(OUTLINE)
    cv.blit(lens, 0, 0)
    return cv


def icon_parts() -> Canvas:
    """«Детали» (улучшение предметов): железная шестерёнка, латунная гайка и болт."""
    cv = Canvas(16, 16)
    g = Canvas(16, 16)
    cx, cy = 6.0, 6.5
    for y in range(16):
        for x in range(16):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx) % (math.pi / 4)
            rout = 5.6 if abs(a - math.pi / 8) < 0.2 else 4.0
            if r <= rout and r > 1.4:
                light = -(dx * 0.62 + dy * 0.78) / max(r, 0.01)
                g.set(x, y, 'stone6' if light > 0.5 else ('stone4' if light > -0.3 else 'stone2'))
    g.outline(OUTLINE)
    cv.blit(g, 0, 0)
    nut = Canvas.from_ascii([
        '.5543.',
        '544332',
        '43kk32',
        '43kk22',
        '433221',
        '.3221.',
    ], _legend('gold'))
    n2 = Canvas(16, 16)
    n2.blit(nut, 9, 9)
    n2.outline(OUTLINE)
    cv.blit(n2, 0, 0)
    return cv


AD_TV_ART = [
    '...a......a...',
    '....a....a....',
    '.....a..a.....',
    '......kk......',
    '44444444444443',
    '43LLttttt33332',
    '4LLtWttttu3G32',
    '4LttWWtttu3g32',
    '4tttWWWttu3332',
    '4tttWWtttu3G32',
    '4tttWttttu3k32',
    '43uuuuuuu33k32',
    '22222222222222',
    '..2........2..',
]


def icon_ad_tv() -> Canvas:
    """Награда за просмотр рекламы: красный ретро-телевизор с антеннами, на экране — светлый треугольник «play»."""
    legend = {'a': 'stone6', 'L': 'teal4', 't': 'teal3', 'u': 'teal2', 'W': 'cream3', 'G': 'gold4', 'g': 'gold2'}
    return _icon(AD_TV_ART, 'red', legend)


DEPOT_ART = [
    '.WWWWW..WWWWW.',
    '.WkgkW..WkgkW.',
    '..wxw....wxw..',
    'RRRRRRRRRRRRRR',
    'rrrrrrrrrrrrrd',
    'dddddddddddddd',
    '5444.GGGG.4443',
    '544.GYYYYG.443',
    '544GYYggYYG443',
    '544GYgYYgYG443',
    '544.GYYYYG.443',
    '5444.GGGG.4443',
    'ssssssssssssss',
]


def icon_depot() -> Canvas:
    """Приёмный склад: две воронки приёма на коньке, красная кровля, дощатый фасад с большой монетой, цоколь."""
    legend = {'W': 'wood5', 'k': 'ink2', 'g': 'gold3', 'w': 'wood4', 'x': 'wood2', 'R': 'red4', 'r': 'red3', 'd': 'red2',
              '5': 'wood5', '4': 'wood4', '3': 'wood2', 'G': 'amber2', 'Y': 'gold4', 's': 'stone3'}
    rows = [r.replace('.', '4') if 6 <= i <= 11 else r for i, r in enumerate(DEPOT_ART)]
    cv = _icon(rows, None, legend)
    return cv


BASE_ART = [
    '.....w........',
    '.....wpp......',
    '.....wppp.....',
    '.....w...RR...',
    '.....w..RrrR..',
    '.....w.RrrrrR.',
    '....RRRrrrrrrR',
    '...RrrrrrrrrrR',
    '..Rrrrrrrrrrdd',
    '.RRRRRRRRRRRdd',
    '..f55kk5555e..',
    '..f5kaak5l5e..',
    '..f5kaak555e..',
    '..f5kkkk555e..',
    '.ssssssssssss.',
]


def icon_base() -> Canvas:
    """База (кнопка «К базе»): домик лагеря с красной крышей, флажок с розовой лапкой, тёплая дверь, фонарь."""
    legend = {'w': 'wood5', 'p': 'pink2', 'R': 'red4', 'r': 'red3', 'd': 'red2', 'f': 'wood5', '5': 'wood4', 'e': 'wood2',
              'k': 'wood1', 'a': 'amber4', 'l': 'amber5', 's': 'stone3'}
    return _icon(BASE_ART, None, legend)


WARDROBE_ART = [
    '....455554....',
    '..4455YY5544..',
    '.333333333333.',
    '4K5kkkkkkkk2K3',
    '4K51kyYyk112K3',
    '4K51yyyyy112K3',
    '4K5444444442K3',
    '4K5sSSs11112K3',
    '4K5115BBBBb2K3',
    '4K5115BYBBb2K3',
    '4K5151BBBBb2K3',
    '4K5511bbbbd2K3',
    '.222222222222.',
    '..22......22..',
]


def icon_equipment() -> Canvas:
    """Экипировка (кнопка нижней панели): распахнутый шкаф экипировки, как wardrobe_open в лагере — каска на полке,
    кирка и синий рюкзак внутри. Силуэт шкафа не похож ни на один головной убор icon_item_hat_*."""
    legend = {'Y': 'gold4', 'y': 'gold3', 'S': 'stone6', 's': 'stone4', 'B': 'blue4', 'b': 'blue3', 'd': 'blue2'}
    return _icon(WARDROBE_ART, 'wood', legend)


# ───────────────────────── магазин ─────────────────────────
def icon_store() -> Canvas:
    """Shop stall with a red-and-cream awning and a teal gem on its wooden counter."""
    cv = Canvas(16, 16)
    cv.rect(3, 6, 1, 8, 'wood5')
    cv.rect(12, 6, 1, 8, 'wood3')
    cv.rect(3, 11, 10, 3, 'wood4')
    cv.hline(3, 12, 11, 'wood5')
    cv.hline(3, 12, 13, 'wood2')
    cv.vline(7, 12, 13, 'wood3')
    cv.vline(10, 12, 13, 'wood3')
    for x in range(2, 14):
        red = ((x - 2) // 2) % 2 == 0
        cv.vline(x, 3, 5, 'red4' if red else 'cream3')
        cv.set(x, 2, 'red5' if red else 'cream4')
        cv.set(x, 6, 'red3' if red else 'cream2')
    cv.blit(_art(['.65.', '5543', '.43.'], 'teal'), 6, 8)
    return _outlined(cv)


def _store_gem(small: bool = False) -> Canvas:
    """Facets follow icon_gem: pale crown, bright left face, dark right tip."""
    rows = ['.664.', '55443', '.543.', '..3..'] if small else [
        '.56664.', '5566443', '5554433', '.55433.', '..443..', '...3...']
    art = _art(rows, 'teal')
    cv = Canvas(art.w + 2, art.h + 2)
    cv.blit(art, 1, 1)
    return _outlined(cv)


def _store_gems(cv: Canvas, positions: Sequence[Tuple[int, int]], small: bool = True) -> None:
    gem = _store_gem(small)
    for x, y in positions:
        cv.blit(gem, x, y)


def store_gems_handful() -> Canvas:
    """Four loose faceted teal gems."""
    cv = Canvas(24, 24)
    _store_gems(cv, [(8, 4), (3, 10), (12, 10), (8, 15)], small=False)
    return cv


def store_gems_sack() -> Canvas:
    """An open burlap sack overflowing with teal gems."""
    cv = Canvas(24, 24)
    sack = Canvas(24, 24)
    sack.polygon([(7, 9), (17, 9), (20, 17), (18, 21), (6, 21), (4, 17)], 'wood4')
    _edge_shade(sack, 'wood4', 'wood6', 'wood3')
    sack.line(7, 14, 6, 18, 'wood5')
    sack.line(17, 14, 18, 19, 'wood3')
    cv.blit(_outlined(sack), 0, 0)
    _store_gems(cv, [(9, 1), (5, 5), (13, 5), (7, 8), (12, 8)])
    cv.hline(6, 17, 12, 'wood6')
    cv.hline(7, 16, 13, 'wood3')
    return cv


def store_gems_bag() -> Canvas:
    """A leather satchel stuffed with gems, with straps and a metal buckle."""
    cv = Canvas(24, 24)
    bag = Canvas(24, 24)
    bag.rect(5, 9, 14, 12, 'wood4')
    bag.rect(3, 12, 2, 6, 'wood3')
    bag.rect(19, 12, 2, 6, 'wood2')
    _edge_shade(bag, 'wood4', 'wood5', 'wood2')
    bag.rect(7, 15, 10, 5, 'wood3')
    bag.hline(7, 16, 15, 'wood5')
    bag.vline(8, 10, 19, 'wood2')
    bag.vline(15, 10, 19, 'wood2')
    bag.rect(10, 14, 4, 4, 'stone6')
    bag.rect(11, 15, 2, 2, 'wood2')
    bag.set(10, 14, 'cream3')
    cv.blit(_outlined(bag), 0, 0)
    _store_gems(cv, [(8, 1), (4, 5), (13, 4), (4, 8), (10, 7), (15, 8)])
    return cv


def _store_crate_front(cv: Canvas, x: int, y: int, w: int, h: int) -> None:
    front = Canvas(24, 24)
    front.rect(x, y, w, h, 'wood4')
    front.hline(x, x + w - 1, y, 'wood6')
    front.hline(x, x + w - 1, y + h - 1, 'wood2')
    front.vline(x + w - 1, y + 1, y + h - 2, 'wood3')
    for yy in range(y + 3, y + h - 1, 3):
        front.hline(x + 1, x + w - 2, yy, 'wood2')
    front.vline(x + 1, y + 1, y + h - 2, 'wood5')
    front.vline(x + w - 3, y + 1, y + h - 2, 'wood5')
    _put(front, [(x + 1, y + 1), (x + w - 3, y + h - 2)], 'stone6')
    cv.blit(_outlined(front), 0, 0)


def store_gems_crate() -> Canvas:
    """An open plank crate full of gems, with loose gems in front."""
    cv = Canvas(24, 24)
    _store_gems(cv, [(8, 1), (4, 5), (13, 4), (1, 8), (8, 8), (16, 8)])
    _store_crate_front(cv, 3, 12, 18, 8)
    _store_gems(cv, [(2, 17), (14, 18)])
    return cv


def _store_wheel(cv: Canvas, x: int, y: int, wooden: bool = False) -> None:
    wheel = _disc(5, 'wood5' if wooden else 'stone5',
                  'wood3' if wooden else 'navy3', 'wood2' if wooden else 'navy1', 'ink2')
    padded = Canvas(7, 7)
    padded.blit(wheel, 1, 1)
    _outlined(padded)
    padded.set(3, 3, 'wood6' if wooden else 'stone6')
    cv.blit(padded, x, y)


def store_gems_cart() -> Canvas:
    """A steel mine cart with dark wheels and a high heap of teal gems."""
    cv = Canvas(24, 24)
    _store_wheel(cv, 4, 17)
    _store_wheel(cv, 14, 17)
    _store_gems(cv, [(9, 0), (6, 3), (12, 3), (2, 6), (8, 6), (15, 6),
                     (0, 9), (5, 9), (11, 9), (17, 9)])
    body = Canvas(24, 24)
    body.polygon([(2, 12), (22, 12), (19, 19), (5, 19)], 'stone4')
    _edge_shade(body, 'stone4', 'stone6', 'navy2')
    body.hline(2, 21, 12, 'stone7')
    body.hline(5, 18, 18, 'navy2')
    body.vline(7, 14, 17, 'stone5')
    body.vline(16, 14, 17, 'navy3')
    _put(body, [(4, 14), (19, 14)], 'cream3')
    cv.blit(_outlined(body), 0, 0)
    return cv


def store_gems_pile() -> Canvas:
    """A pyramid of many small faceted gems with single-pixel sparkles."""
    cv = Canvas(24, 24)
    _store_gems(cv, [(9, 2), (6, 6), (12, 6), (3, 10), (9, 10), (15, 10),
                     (1, 14), (6, 14), (12, 14), (17, 14), (3, 18), (9, 18), (15, 18)])
    _put(cv, [(4, 5), (19, 4), (21, 11)], 'cream4')
    return cv


def _store_bottle(large: bool = False) -> Canvas:
    """Corked glass with purple liquid and a pale top-left reflection."""
    if not large:
        return _icon(['..ww...', '..Ww...', '..tt...', '..tL...', '.L66t..',
                      'L6554t.', 'L5544t.', 't5443t.', '.t33t..'], 'purple',
                     {'w': 'wood3', 'W': 'wood5', 't': 'teal3', 'L': 'cream3'}, size=11)
    cv = Canvas(18, 22)
    cv.rect(7, 1, 4, 3, 'wood3')
    cv.hline(7, 10, 1, 'wood5')
    cv.rect(7, 4, 4, 5, 'teal3')
    cv.vline(7, 4, 7, 'cream3')
    cv.ellipse(9, 14, 7, 6, 'teal3')
    cv.ellipse(9, 14, 6, 5, 'purple4')
    cv.rect(4, 10, 10, 2, 'purple5')
    cv.ellipse(11, 16, 3, 3, 'purple3')
    cv.vline(4, 12, 15, 'cream3')
    cv.set(5, 11, 'cream4')
    cv.hline(6, 11, 18, 'purple3')
    return _outlined(cv)


def store_elixir_bottle() -> Canvas:
    """One large round-bottom glass bottle of purple elixir with a wood cork."""
    cv = Canvas(24, 24)
    cv.blit(_store_bottle(True), 3, 1)
    return cv


def store_elixir_box() -> Canvas:
    """A wooden box holding four corked purple elixir bottles."""
    cv = Canvas(24, 24)
    bottle = _store_bottle()
    for x, y in [(0, 2), (11, 1), (5, 5), (13, 6)]:
        cv.blit(bottle, x, y)
    _store_crate_front(cv, 3, 14, 18, 7)
    return cv


def store_elixir_wagon() -> Canvas:
    """A wooden wagon loaded with purple bottles, with wheels and a handle."""
    cv = Canvas(24, 24)
    _store_wheel(cv, 3, 17, wooden=True)
    _store_wheel(cv, 13, 17, wooden=True)
    handle = Canvas(24, 24)
    handle.line(18, 16, 22, 11, 'wood5')
    cv.blit(_outlined(handle), 0, 0)
    bottle = _store_bottle()
    for x, y in [(0, 2), (9, 1), (4, 5), (11, 5)]:
        cv.blit(bottle, x, y)
    _store_crate_front(cv, 2, 14, 18, 5)
    return cv


def _store_watch(d: int = 14) -> Canvas:
    cv = Canvas(d + 2, d + 5)
    body = _disc(d, 'gold4', 'gold3', 'gold2', 'cream3')
    mid = d // 2
    face = C('cream3')[:3]
    for y in range(d):
        for x in range(d):
            if _same(body, x, y, face) and (not _same(body, x, y - 1, face) or not _same(body, x - 1, y, face)):
                body.set(x, y, 'cream2')
    _put(body, [(mid, 2), (d - 3, mid), (mid, d - 3), (2, mid)], 'cream1')
    body.vline(mid, 3, mid - 1, 'ink1')
    body.hline(mid + 1, d - 4, mid - 1, 'ink1')
    body.set(mid, mid, 'red3')
    cv.blit(body, 1, 4)
    cv.rect(mid, 1, 3, 3, 'gold3')
    cv.hline(mid, mid + 2, 1, 'gold4')
    cv.set(mid + 2, 3, 'gold2')
    return _outlined(cv)


def store_skip_1h() -> Canvas:
    """One gold stopwatch: one-hour time skip."""
    cv = Canvas(24, 24)
    cv.blit(_store_watch(16), 3, 1)
    return cv


def store_skip_4h() -> Canvas:
    """Two overlapping gold stopwatches: four-hour time skip."""
    cv = Canvas(24, 24)
    cv.blit(_store_watch(), 7, 0)
    cv.blit(_store_watch(), 1, 5)
    return cv


def store_skip_12h() -> Canvas:
    """Three fanned gold stopwatches: twelve-hour time skip."""
    cv = Canvas(24, 24)
    watch = _store_watch(12)
    cv.blit(watch, 0, 2)
    cv.blit(watch, 10, 1)
    cv.blit(watch, 5, 7)
    return cv


# ───────────────────────── заводы и рынок (ТЗ 2.2 §6.2, §8, §11) ─────────────────────────
MARKET_ART = [
    '......Gg......',
    '.HHHHHHHHHHHH.',
    '.sssssWwsssss.',
    '..k...Ww...k..',
    '.k.k..Ww..k.k.',
    'k...k.Ww.k...k',
    '.YYY..Ww..BBB.',
    'YyYYy.Ww.BbbBc',
    'YYYyy.Ww.bbbbc',
    'PPPPP.Ww.PPPPP',
    '.ppp..Ww..ppp.',
    '......Ww......',
    '....LLLLLL....',
    '...dddddddd...',
]


def icon_market() -> Canvas:
    """Рынок (кнопка Market): рычажные весы — на левой чаше золотая монета, на правой ящик товара. Не прилавок
    (прилавок с навесом — icon_store) и не склад с монетой (icon_depot)."""
    legend = {'G': 'gold5', 'g': 'gold3', 'H': 'stone6', 's': 'stone3', 'W': 'wood5', 'w': 'wood3', 'k': 'stone4',
              'Y': 'gold3', 'y': 'gold4', 'B': 'wood6', 'b': 'wood4', 'c': 'wood2', 'P': 'stone5', 'p': 'stone3',
              'L': 'wood5', 'd': 'wood2'}
    cv = _icon(MARKET_ART, None, legend)
    cv.set(2, 8, 'gold5')
    return cv


AUTO_ART = [
    '....LLLLL.....',
    '..LLGGGGGLL...',
    '.LGG.....GGL..',
    '.LG.......GL..',
    'LG.......LGGGL',
    'LG........GGG.',
    'LG.........G..',
    '..G.........gd',
    '.ggg........gd',
    'gggdg.......gd',
    '..gd.......gd.',
    '..gdd.....ddd.',
    '...ddddddddd..',
    '.....ddddd....',
]


def icon_auto() -> Canvas:
    """Автодоставка (значок AUTO у полосы завода): две зелёные стрелки по кругу по часовой — верхняя приходит справа
    наконечником вниз, нижняя слева наконечником вверх (знак «работает само»)."""
    return _icon(AUTO_ART, None, {'L': 'green5', 'G': 'green4', 'g': 'green3', 'd': 'green2'})


def icon_deliver() -> Canvas:
    """Доставить (кнопка Deliver): деревянный ящик товара и золотая стрелка вправо — «отправить по трубе»."""
    cv = Canvas(16, 16)
    cv.blit(_art([
        '666666666.',
        '5555555553',
        '4444444443',
        '3333333332',
        '4442444443',
        '4444244443',
        '4444424443',
        '4444442443',
        '4444444243',
        '2222222222',
    ], 'wood'), 1, 5)
    cv.blit(_art([
        '....G..',
        '....GG.',
        'GGGGGgG',
        'ggggggd',
        '....gd.',
        '....d..',
    ], None, {'G': 'gold4', 'g': 'gold3', 'd': 'gold2'}), 9, 1)
    return _outlined(cv)


FACTORY_ICON = [
    '..0000.....mm.',
    '.0n1n10....ss.',
    '.01g110....ss.',
    '.0n1n10....ss.',
    '..0000.....ss.',
    '...ff......ss.',
    '..RRRRRRRRRRRR',
    '.RrrrrrrrrrrRd',
    'WWWWWWWWWWWWWw',
    'WaWW.kk.WWaWWw',
    'WAWW.kk.WWAWWw',
    'WWWWkhhkWWWWWw',
    'WWWWkhhkWWWWWw',
    'ssssssssssssss',
]


def icon_factory() -> Canvas:
    """Завод (сведения цели вместо icon_shaft): колесо-шестерня на стойке, синяя кровля, кирпичный цех с тёплыми
    окнами и воротами, стальная труба справа — как здание factory_<good> на поле."""
    legend = {'0': 'stone4', '1': 'stone6', 'n': 'navy1', 'g': 'gold4', 'f': 'wood3', 'm': 'stone6', 's': 'stone4',
              'R': 'blue4', 'r': 'blue3', 'd': 'blue2', 'W': 'red3', 'w': 'red2', 'a': 'amber4', 'A': 'amber3',
              'k': 'ink2', 'h': 'amber2'}
    return _icon(FACTORY_ICON, None, legend)


def icon_chain() -> Canvas:
    """Цепи завода (Locked, HP цепей): четыре стальных звена по диагонали — кольцо анфас и звено в профиль по очереди."""
    import math as _m
    cv = Canvas(16, 16)
    ux, uy = 1 / _m.sqrt(2), -1 / _m.sqrt(2)          # ось цепи: снизу-слева вверх-вправо
    for k, t in enumerate((-6.6, -2.2, 2.2, 6.6)):
        cx, cy = 8.0 + ux * t, 8.0 + uy * t
        face = k % 2 == 0
        lay = Canvas(16, 16)
        for y in range(16):
            for x in range(16):
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                al = dx * ux + dy * uy                   # вдоль цепи
                ac = -dx * uy + dy * ux                  # поперёк
                if face:
                    e_out = (al / 3.3) ** 2 + (ac / 2.4) ** 2
                    e_in = (al / 1.7) ** 2 + (ac / 0.9) ** 2
                    if e_out > 1.0 or e_in < 1.0:
                        continue
                else:
                    if (al / 3.3) ** 2 + (ac / 1.15) ** 2 > 1.0:
                        continue
                lum = -(dx * 0.6 + dy * 0.8)
                lay.set(x, y, 'stone7' if lum > 0.9 else ('stone4' if lum < -0.9 else 'stone6'))
        lay.outline(OUTLINE)
        cv.blit(lay, 0, 0)
    return cv


STORE_ICONS = (
    ('icon_store', icon_store),
    ('store_gems_handful', store_gems_handful), ('store_gems_sack', store_gems_sack),
    ('store_gems_bag', store_gems_bag), ('store_gems_crate', store_gems_crate),
    ('store_gems_cart', store_gems_cart), ('store_gems_pile', store_gems_pile),
    ('store_elixir_bottle', store_elixir_bottle), ('store_elixir_box', store_elixir_box),
    ('store_elixir_wagon', store_elixir_wagon), ('store_skip_1h', store_skip_1h),
    ('store_skip_4h', store_skip_4h), ('store_skip_12h', store_skip_12h),
)


ICONS = {
    'icon_coin': icon_coin, 'icon_elixir': icon_elixir, 'icon_gem': icon_gem, 'icon_cat': icon_cat, 'icon_gear': icon_gear,
    'icon_sound_on': icon_sound_on, 'icon_sound_off': icon_sound_off, 'icon_vibro_on': icon_vibro_on, 'icon_vibro_off': icon_vibro_off,
    'icon_chest': icon_chest, 'icon_cards': icon_cards, 'icon_dynamite': icon_dynamite, 'icon_clock': icon_clock,
    'icon_arrow_up': icon_arrow_up, 'icon_lock': icon_lock, 'icon_check': icon_check, 'icon_plus': icon_plus,
    'icon_close': icon_close, 'icon_star': icon_star, 'icon_pickaxe': icon_pickaxe, 'icon_upgrade': icon_upgrade,
    'icon_forge': icon_forge, 'icon_gate': icon_gate, 'icon_exit': icon_exit, 'icon_shaft': icon_shaft,
    'icon_info': icon_info, 'icon_warning': icon_warning, 'icon_paw': icon_paw, 'icon_hand': icon_hand, 'icon_income': icon_income,
    'icon_chest_big': icon_chest_big, 'icon_cards_big': icon_cards_big, 'icon_dynamite_big': icon_dynamite_big, 'icon_cat_big': icon_cat_big,
    # дополнительные
    'icon_hand_big': icon_hand_big,
    # настройки звука и вида
    'icon_music': icon_music, 'icon_ambience': icon_ambience, 'icon_effects': icon_effects,
    # стрелки индикаторов за краем экрана во всех 8 направлениях (вместо поворота спрайта)
    **{f'icon_arrow_{k}': (lambda k=k: _arrow_icon(k)) for k in ARROW_DIRS if k != 'up'},
    # заводы и рынок (ТЗ 2.2)
    'icon_market': icon_market, 'icon_auto': icon_auto, 'icon_deliver': icon_deliver, 'icon_factory': icon_factory,
    'icon_chain': icon_chain,
}


def build(reg) -> None:
    for name, fn in ICONS.items():
        cv = fn()
        reg.add(name, cv, pivot=(cv.w / 2.0, cv.h / 2.0))
    # Экипировка нового режима: 16 предметов (16×16 и крупные 32×32) и пустые слоты.
    for slot in SLOTS:
        for rar in RARITIES:
            reg.add(f'icon_item_{slot}_{rar}', ITEM_FN[slot](rar, 1), pivot=(8.0, 8.0), note=f'предмет {slot}.{rar}')
            reg.add(f'icon_item_{slot}_{rar}_big', ITEM_FN[slot](rar, 2), pivot=(16.0, 16.0), note=f'предмет {slot}.{rar}, 32×32')
        reg.add(f'icon_slot_{slot}', item_slot(slot), pivot=(8.0, 8.0), note=f'пустой слот {slot}')
    for name, fn in (('icon_branch_team', icon_branch_team), ('icon_branch_pickaxes', icon_branch_pickaxes),
                     ('icon_branch_boots', icon_branch_boots), ('icon_branch_bags', icon_branch_bags),
                     ('icon_branch_unload', icon_branch_unload), ('icon_branch_value', icon_branch_value),
                     ('icon_parts', icon_parts), ('icon_sack', icon_sack), ('icon_depot', icon_depot), ('icon_base', icon_base),
                     ('icon_equipment', icon_equipment), ('icon_ad_tv', icon_ad_tv)):
        reg.add(name, fn(), pivot=(8.0, 8.0), note=(fn.__doc__ or '').split('.')[0])
    for name, fn in STORE_ICONS:
        cv = fn()
        reg.add(name, cv, pivot=(cv.w / 2.0, cv.h / 2.0), note=fn.__doc__ or '')
    # Сундуки по уровню для окна награды: тот же рисунок, что у rock_chest_* на поле (rocks.chest), без каменной россыпи.
    from sprites import rocks
    for tier in rocks.CHEST_ORDER:
        cv = Canvas(26, 24)
        rocks.chest(cv, rocks.CHEST_MATS[tier], x0=2, y0=2, w=22)
        reg.add(f'icon_chest_{tier}', cv, pivot=(13.0, 12.0))
