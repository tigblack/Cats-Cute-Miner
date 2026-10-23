"""База-лагерь нового режима (ТЗ v2.1 §5, §8.3, §16): настил площадки, приёмный склад с двумя местами разгрузки,
доска найма, верстак инструментов, стеллаж сумок, вагонетка, шкаф экипировки и декор лагеря.

Раскладка (клетки относительно площадки; площадка — строки pad0…pad2 под картой, ряд построек — строки st0…st2 ниже):
  pad0: camp_edge_t ×7 (порог из бруса, стык с плитами карты)
  pad1: camp_floor_* ×7; места разгрузки depot_slot в столбцах 2 и 4 (наклейка пола), спавн — столбец 3
  pad2: camp_edge_b ×7 (край настила со ступенькой); очередь котиков; желоба depot_chute в столбцах 2 и 4
  st0…st2: camp_ground_* ×7 (утоптанная земля двора); постройки стоят ногами на нижней кромке st1, передний ряд
           (ящики, вагонетка, фонари, вывеска) — на нижней кромке st2. Ни один спрайт не поднимается выше st0,
           поэтому очередь котиков в pad2 ничем не перекрыта.
Постройки — вид 3/4, свет сверху-слева, контур 1 px из тёмного конца рампы материала (как sprites/structures.py).
Уровни (_0, _1, _2) — один холст и один pivot: Unity меняет спрайт целиком. Наложения (свет, открытый люк)
нарисованы на холсте своей базы и с тем же pivot. Координаты в комментариях — от левого ВЕРХНЕГО угла холста.
Постройки и настил не перекрашиваются темами окружения (sprites/themes.py): лагерь тёплый в любой шахте.
"""
from __future__ import annotations

import math
import random
from typing import Dict, List, Sequence, Tuple

import pxl
from pxl import Canvas, with_alpha
from sprites.structures import (DWOOD, IRON, STONE, WOOD, blank, bolt, brace, clip, coin_top, glow, grain_h, grain_v,
                                hbeam, in_ellipse, inside_poly, lantern, paw, pick_emblem, put, remap, thick_line, vbeam)

T = 32


def _pts(cv: Canvas, pts: Sequence[Tuple[int, int]], c: str) -> None:
    for x, y in pts:
        cv.set(x, y, c)


# ───────────────────────── настил площадки ─────────────────────────
# Доски поперёк экрана по 8 px: светлая кромка, тело, щель. Настил светлый (средняя яркость ≈ 105, как плиты карты
# slate4–5 и выше): тёмные контуры котиков и тень shadow_s на нём читаются так же, как на плитах. Щель между досками —
# wood3 (не wood0), стыки короткие: настил — спокойный фон, а не полосатый «забор» за котиками.
PLANK = dict(gap='wood3', lit='wood5', body='wood4', grain='wood3', joint='wood3', end_lit='wood6', nail='stone5',
             nail_sh='wood3', worn='wood6')
# Стыки (x щели) по доскам j = 0…3 для каждого варианта плитки: разные, чтобы сетка настила не читалась «обоями».
JOINTS = [(6, 22, 14, 29), (19, 3, 26, 11), (11, 27, 4, 20), (24, 14, 30, 8)]


def _plank_row(cv: Canvas, y0: int, joint: int, seed: int, p: Dict[str, str] = None) -> None:
    """Одна доска высотой 8 px от y0 (щель — нижний ряд); joint — x щели стыка (-1 — без стыка)."""
    p = p or PLANK
    cv.hline(0, T - 1, y0, p['lit'])
    cv.rect(0, y0 + 1, T, 6, p['body'])
    cv.hline(0, T - 1, y0 + 7, p['gap'])
    grain_h(cv, 0, y0 + 3, T, 1, p['grain'], seed, step=3)
    # Потёртости светлой кромки (свет сверху-слева): редкие блики wood6.
    rnd = random.Random(700 + seed)
    for _ in range(2):
        x = rnd.randrange(0, T - 3)
        cv.hline(x, x + rnd.randrange(1, 3), y0, p['worn'])
    if joint >= 0:
        # Короткий стык: щель только по телу доски, торец справа освещён.
        cv.vline(joint, y0 + 1, y0 + 6, p['joint'])
        cv.vline((joint + 1) % T, y0 + 1, y0 + 5, p['end_lit'])
        # Гвоздь у конца доски справа от стыка.
        nx = (joint + 3) % T
        cv.set(nx, y0 + 3, p['nail'])
        cv.set(nx, y0 + 4, p['nail_sh'])


def camp_floor(v: int) -> Canvas:
    """Плитка настила 32×32 (бесшовно во все стороны): 4 доски, стыки вразбежку; варианты — сучок, заплатка, следы лапок."""
    cv = Canvas(T, T)
    for j in range(4):
        _plank_row(cv, j * 8, JOINTS[v][j], seed=31 * v + j)
    if v == 1:
        # Сучок: колечко на тон темнее с бликом сверху-слева.
        _pts(cv, [(15, 11), (16, 11), (14, 12), (17, 12), (15, 13), (16, 13)], 'wood3')
        _pts(cv, [(15, 12), (16, 12)], 'wood2')
        cv.set(14, 11, 'wood5')
    elif v == 2:
        # Заменённая доска: на тон светлее, с двумя гвоздями посередине.
        for y in range(17, 23):
            for x in range(4, 13):
                cv.set(x, y, 'wood5' if y > 17 else 'wood6')
        cv.vline(3, 17, 22, 'wood3')
        cv.vline(13, 17, 22, 'wood3')
        cv.hline(4, 12, 22, 'wood4')
        _pts(cv, [(5, 19), (11, 19)], 'stone5')
        _pts(cv, [(5, 20), (11, 20)], 'wood3')
    elif v == 3:
        # Пыльные следы лапок по настилу (тусклые: не спорят с котиками).
        for px, py in ((6, 3), (19, 19)):
            _pts(cv, [(px, py), (px + 2, py - 1), (px + 4, py)], 'wood3')
            _pts(cv, [(px + 1, py + 2), (px + 2, py + 2), (px + 3, py + 2), (px + 2, py + 3)], 'wood3')
    return cv


def camp_edge_t() -> Canvas:
    """Верхний ряд площадки: порог из толстого бруса (стык с плитами карты) и три доски. Бесшовно по горизонтали."""
    cv = Canvas(T, T)
    # Брус порога: тень от плит карты, светлая верхняя грань, торцевые болты; брус темнее настила — рамка площадки.
    cv.hline(0, T - 1, 0, 'wood0')
    cv.hline(0, T - 1, 1, 'wood5')
    cv.rect(0, 2, T, 3, 'wood3')
    cv.hline(0, T - 1, 5, 'wood2')
    cv.hline(0, T - 1, 6, 'wood1')
    cv.hline(0, T - 1, 7, 'wood2')
    grain_h(cv, 0, 3, T, 2, 'wood2', 77, step=2)
    for bx in (7, 23):
        bolt(cv, bx, 3, big=True)
    for j in range(1, 4):
        _plank_row(cv, j * 8, JOINTS[0][j], seed=90 + j)
    return cv


def camp_edge_b() -> Canvas:
    """Нижний ряд площадки: три доски, узкая доска-кромка и передняя обвязка со стойками (ступенька во двор).
    Ноги котика в очереди (FeetY −10 px → строка 26) стоят на светлой кромке, а не на тёмной обвязке."""
    cv = Canvas(T, T)
    for j in range(3):
        _plank_row(cv, j * 8, JOINTS[2][j], seed=120 + j)
    # Доска-кромка (строки 24–27) со светлым скруглённым краем.
    cv.hline(0, T - 1, 24, 'wood5')
    cv.rect(0, 25, T, 2, 'wood4')
    cv.hline(0, T - 1, 27, 'wood6')
    grain_h(cv, 0, 25, T, 1, 'wood3', 127, step=2)
    # Обвязка (передняя грань в тени) — строки 28–30, торцы стоек под настилом.
    cv.hline(0, T - 1, 28, 'wood2')
    cv.hline(0, T - 1, 29, 'wood2')
    cv.hline(0, T - 1, 30, 'wood1')
    for sx in (2, 18):
        cv.rect(sx, 28, 3, 3, 'wood3')
        cv.vline(sx, 28, 30, 'wood4')
        cv.set(sx + 1, 29, 'stone5')
    cv.hline(0, T - 1, 31, 'rust1')            # контактная тень на земле двора
    return cv


def camp_ground(v: int) -> Canvas:
    """Утоптанная земля двора под постройками (тёплая серо-бурая рампа rust): бесшовно, редкие камешки и колеи."""
    cv = Canvas(T, T)
    cv.rect(0, 0, T, T, 'rust3')
    cv.dither(0, 0, T, T, 'rust2', density=0.12, seed=v)
    rnd = random.Random(400 + v)
    for _ in range(4):
        x, y = rnd.randrange(1, T - 3), rnd.randrange(1, T - 3)
        cv.set(x, y, 'rust5')
        cv.set(x + 1, y, 'rust4')
        cv.set(x, y + 1, 'rust4')
        cv.set(x + 1, y + 1, 'rust1')
    if v == 1:
        # Камешек побольше.
        _pts(cv, [(20, 9), (21, 9), (19, 10)], 'rust6')
        _pts(cv, [(20, 10), (21, 10), (22, 10), (20, 11)], 'rust5')
        _pts(cv, [(21, 11), (22, 11), (23, 10)], 'rust2')
        _pts(cv, [(20, 12), (21, 12), (22, 12)], 'rust1')
    return cv


# ───────────────────────── приёмный склад 96×64 ─────────────────────────
DW, DH = 96, 64
D_GROUND = 63
D_HOPPERS = (16, 80)          # центры воронок приёма над столбцами 2 и 4 (x холста)
D_SPOUTS = (16, 80)           # жёлоба выдачи над ящиками depot_crate_* в переднем ряду
ROOF = dict(o='red0', d='red2', b='red3', l='red4', h='red5')


def shingle_roof(cv: Canvas, pts: Sequence[Tuple[float, float]], y_rows: Sequence[int], seed: int, right_shade_x: int) -> None:
    """Крыша из красных досок-гонта: ряды через y_rows, вертикальные швы вразбежку, правый скат в тени."""
    lay = blank(cv)
    lay.polygon(pts, ROOF['b'])
    ys = [p[1] for p in pts]
    top, bot = int(min(ys)), int(max(ys))
    rnd = random.Random(seed)
    for yy in y_rows:
        lay.fill_where(lambda x, y, yy=yy: y == yy, ROOF['d'])
        lay.fill_where(lambda x, y, yy=yy: y == yy + 1, ROOF['l'])
    for i, yy in enumerate([top] + list(y_rows)):
        nxt = ([*y_rows, bot + 1])[i] if i < len(y_rows) + 1 else bot
        off = rnd.randrange(0, 6)
        for xx in range(off, cv.w, 6):
            for y in range(yy + 2, nxt):
                if lay.opaque(xx, y):
                    lay.set(xx, y, ROOF['d'])
    lay.fill_where(lambda x, y: y == top, ROOF['h'])
    remap(lay, {ROOF['b']: ROOF['d'], ROOF['l']: ROOF['b']}, lambda x, y: x >= right_shade_x)
    put(cv, lay, ROOF['o'])


def hopper(cv: Canvas, cx: int, top: int, coins: bool = True) -> None:
    """Воронка приёма груза на коньке крыши: дощатый короб-раструб (сужается к крыше) с железным обручем,
    сверху — открытый приёмник с тёмным нутром и монетами. cx — центр, top — верх обода."""
    body = blank(cv)
    body.polygon([(cx - 9, top + 4), (cx + 9, top + 4), (cx + 5, top + 12), (cx - 5, top + 12)], 'wood4')
    body.fill_where(lambda x, y: x >= cx + 3, 'wood3')
    body.fill_where(lambda x, y: x <= cx - 6, 'wood5')
    for xx in (cx - 3, cx + 2):
        body.fill_where(lambda x, y, xx=xx: x == xx and y > top + 4, 'wood2')
    body.fill_where(lambda x, y: y == top + 8, 'stone4')
    body.fill_where(lambda x, y: y == top + 9, 'stone2')
    put(cv, body, 'wood0')
    rim = blank(cv)
    rim.rect(cx - 10, top, 21, 5, 'wood5')
    rim.hline(cx - 10, cx + 10, top, 'wood6')
    rim.vline(cx + 10, top + 1, top + 4, 'wood3')
    rim.hline(cx - 9, cx + 10, top + 4, 'wood3')
    rim.rect(cx - 8, top + 1, 17, 3, 'ink2')
    rim.hline(cx - 8, cx + 8, top + 1, 'ink1')
    put(cv, rim, 'wood0')
    if coins:
        for dx, c in ((-5, 'gold3'), (-4, 'gold4'), (-1, 'gold2'), (2, 'gold4'), (3, 'gold3'), (6, 'gold2')):
            cv.set(cx + dx, top + 3, c)
        cv.set(cx - 4, top + 2, 'gold5')
        cv.set(cx + 2, top + 2, 'gold3')


def coin_sign(cv: Canvas, x: int, y: int, w: int, h: int) -> None:
    """Вывеска склада: светлая доска, большая монета с лапкой, болты по углам."""
    sign = blank(cv)
    sign.rect(x, y, w, h, 'wood6')
    sign.hline(x, x + w - 1, y, 'wood7')
    sign.vline(x, y, y + h - 1, 'wood7')
    sign.hline(x + 1, x + w - 1, y + h - 1, 'wood5')
    sign.vline(x + w - 1, y + 1, y + h - 1, 'wood5')
    put(cv, sign, 'wood1')
    for bx, by in ((x + 1, y + 1), (x + w - 2, y + 1), (x + 1, y + h - 2), (x + w - 2, y + h - 2)):
        cv.set(bx, by, 'wood4')
    cx, cy, r = x + w / 2.0, y + h / 2.0, min(w, h) / 2.0 - 2
    coin = blank(cv)
    coin.ellipse(cx, cy, r, r, 'amber3')
    coin.fill_where(lambda px, py: (px + 0.5 - cx) + (py + 0.5 - cy) >= 0, 'amber2')
    coin.ellipse(cx - 0.5, cy - 0.5, r - 1.6, r - 1.6, 'gold3')
    coin.fill_where(lambda px, py: coin.get(px, py)[:3] == pxl.PALETTE['gold3'] and (px + 0.5 - cx) + (py + 0.5 - cy) < -r * 0.9, 'gold4')
    put(cv, coin, 'gold0')
    paw(cv, int(round(cx - 4.5)), int(round(cy - 4)), 'gold2')
    cv.set(int(cx - r + 2), int(cy - r + 3), 'gold5')


def spout(cv: Canvas, cx: int, y: int) -> None:
    """Выдача монет: окошко в стене и дощатый лоток, расширяющийся к зрителю (наклон вниз), по дну катятся монеты.
    cx — центр, y — верх окошка; кромка лотка — на y + 15."""
    hole = blank(cv)
    hole.rect(cx - 6, y, 13, 6, 'ink2')
    hole.hline(cx - 6, cx + 6, y, 'ink1')
    put(cv, hole, 'wood1')
    cv.hline(cx - 7, cx + 7, y - 2, 'wood5')
    cv.hline(cx - 7, cx + 7, y - 1, 'wood2')
    tr = blank(cv)
    tr.polygon([(cx - 6, y + 5), (cx + 7, y + 5), (cx + 9, y + 14), (cx - 8, y + 14)], 'wood1')
    tr.fill_where(lambda x, yy: yy >= y + 13, 'wood4')
    # Борта лотка: левый на свету, правый в тени.
    for yy in range(y + 5, y + 15):
        t = (yy - y - 5) / 9.0
        xl = int(round(cx - 6 - 2 * t))
        xr = int(round(cx + 6 + 2 * t))
        tr.set(xl, yy, 'wood5')
        tr.set(xl + 1, yy, 'wood4')
        tr.set(xr, yy, 'wood2')
        tr.set(xr - 1, yy, 'wood3')
    tr.hline(cx - 8, cx + 8, y + 14, 'wood5')
    put(cv, tr, 'wood0')
    cv.hline(cx - 8, cx + 8, y + 15, 'stone4')
    cv.hline(cx - 8, cx - 3, y + 15, 'stone6')
    for x, yy, c in ((cx - 2, y + 7, 'gold3'), (cx - 1, y + 7, 'gold4'), (cx + 2, y + 9, 'gold3'), (cx + 3, y + 9, 'gold2'),
                     (cx - 3, y + 11, 'gold4'), (cx - 2, y + 11, 'gold3'), (cx + 1, y + 12, 'gold4'), (cx + 2, y + 12, 'gold3')):
        cv.set(x, yy, c)
    cv.set(cx - 1, y + 6, 'gold5')


def depot_base() -> Canvas:
    """Приёмный склад 3 клетки: каменный цоколь, дощатый фасад на столбах, красная крыша, две воронки приёма над
    местами разгрузки (столбцы 2 и 4), вывеска с монетой и лапкой, окно конторы с весами, жёлоба выдачи над ящиками."""
    cv = Canvas(DW, DH)
    # Фасад: вертикальные доски между столбами.
    wall = blank(cv)
    wall.rect(4, 26, 88, 32, 'wood3')
    for xx in range(6, 92, 5):
        wall.vline(xx, 27, 57, 'wood2')
        wall.vline(xx + 1, 27, 57, 'wood4')
    grain_v(wall, 8, 30, 82, 26, 'wood2', 5, step=5)
    wall.hline(4, 91, 27, 'wood1')       # тень под крышей
    wall.hline(4, 91, 28, 'wood2')
    put(cv, wall, 'wood0')
    # Каменный цоколь.
    base = blank(cv)
    base.rect(2, 56, 92, 7, 'stone3')
    base.hline(2, 93, 56, 'stone5')
    base.hline(2, 93, 62, 'stone1')
    rnd = random.Random(9)
    x = 2
    for row, yy in enumerate((57, 60)):
        x = 2 + (row * 5)
        while x < 94:
            w = rnd.randrange(7, 12)
            base.vline(x, yy, yy + 2, 'stone1')
            base.set(x + 1, yy, 'stone4')
            x += w
        base.hline(2, 93, yy + 2 if row == 0 else 62, 'stone2' if row == 0 else 'stone1')
    put(cv, base, 'stone0')
    # Окно конторы: тёплый свет, прилавок, весы с монетами.
    win = blank(cv)
    wx, wy, ww, wh = 36, 36, 24, 15
    win.rect(wx, wy, ww, wh, 'amber2')
    win.rect(wx + 1, wy + 1, ww - 2, 6, 'amber3')
    win.rect(wx + 3, wy + 2, ww - 6, 3, 'amber4')
    win.vline(wx + ww // 2, wy, wy + 8, 'wood1')
    put(cv, win, 'wood0')
    # Весы на прилавке: стойка, коромысло, чаши; на левой — монеты.
    cv.vline(48, wy + 4, wy + 11, 'stone2')
    cv.hline(42, 54, wy + 5, 'stone3')
    cv.set(48, wy + 3, 'stone5')
    for px in (42, 54):
        cv.vline(px, wy + 6, wy + 8, 'stone2')
        cv.hline(px - 2, px + 2, wy + 9, 'stone4')
        cv.hline(px - 1, px + 1, wy + 10, 'stone2')
    _pts(cv, [(41, wy + 8), (42, wy + 8), (43, wy + 8)], 'gold4')
    cv.set(42, wy + 7, 'gold5')
    # Прилавок.
    hbeam(cv, 33, 49, 30, 5, WOOD, seed=3)
    # Наличник и навес над окном.
    hbeam(cv, 33, 32, 30, 4, WOOD, seed=4)
    # Столбы.
    for i, px in enumerate((1, 28, 62, 89)):
        vbeam(cv, px, 22, 6, 36, WOOD, seed=20 + i)
        bolt(cv, px + 2, 30, big=True)
        bolt(cv, px + 2, 48, big=True)
    # Жёлоба выдачи над ящиками.
    for sx in D_SPOUTS:
        spout(cv, sx, 38)
    # Крыша.
    shingle_roof(cv, [(6, 9), (90, 9), (95, 24), (0, 24)], (13, 18), seed=4, right_shade_x=88)
    # Брус под крышей.
    hbeam(cv, 0, 22, 96, 5, WOOD, seed=6)
    for bx in (3, 92):
        bolt(cv, bx, 23)
    # Воронки приёма над местами разгрузки.
    for hx in D_HOPPERS:
        hopper(cv, hx, 0)
    # Вывеска с монетой по центру крыши.
    coin_sign(cv, 36, 5, 24, 20)
    # Фонари на столбах у окна конторы.
    lantern(cv, 27, 31)
    lantern(cv, 62, 31)
    return cv


def depot_glow() -> Canvas:
    """Свет приёма (идёт разгрузка): тёплый ореол окна конторы и отблеск в воронках и жёлобах."""
    cv = Canvas(DW, DH)
    glow(cv, in_ellipse(48, 43, 20, 13), [(1.0, 'amber3', 40), (0.7, 'amber3', 70), (0.42, 'amber4', 100)])
    for hx in D_HOPPERS:
        glow(cv, in_ellipse(hx + 0.5, 3.5, 11, 6), [(1.0, 'gold4', 50), (0.6, 'gold5', 90)])
    for sx in D_SPOUTS:
        glow(cv, in_ellipse(sx + 0.5, 47, 10, 8), [(1.0, 'gold4', 45), (0.55, 'gold5', 80)])
    return cv


# ───────────────────────── место разгрузки (наклейка пола 32×32) ─────────────────────────
# Котик на месте разгрузки (ноги на FeetY −10 px → строка 26 клетки) закрывает почти весь люк: строки 6…25 и 27 из
# 32 столбцов. Поэтому признак «занято» вынесен ниже линии ног — на планку с фонариками (строки 27–30) и на жёлоб
# depot_chute_active в ряду под местом; полупрозрачная тень shadow_s планку только притеняет. Разметки-уголков
# (как у выделения select_ring) нет: рама люка — железные полосы заподлицо с настилом.
SLOT_LAMPS = (6, 15, 24)      # x левого пикселя линз сигнальной планки (линза 2×2)


def _slot_strip(cv: Canvas, active: bool) -> None:
    """Сигнальная планка под линией ног (строки 27–30): железная полоса с тремя линзами; active — линзы горят."""
    cv.rect(4, 27, 24, 4, 'stone3')
    cv.hline(4, 27, 27, 'stone5')
    cv.hline(4, 27, 30, 'stone1')
    cv.vline(4, 27, 30, 'stone4')
    cv.vline(27, 28, 30, 'stone2')
    for x in (5, 26):
        cv.set(x, 29, 'stone6')                # заклёпки по краям
    for lx in SLOT_LAMPS:
        if active:
            cv.rect(lx, 28, 2, 2, 'amber4')
            cv.set(lx, 28, 'cream4')
            cv.set(lx + 1, 29, 'amber3')
            cv.set(lx - 1, 28, 'amber2')
            cv.set(lx + 2, 28, 'amber2')
            cv.set(lx, 27, 'amber3')
            cv.set(lx + 1, 27, 'amber3')
        else:
            cv.rect(lx, 28, 2, 2, 'navy2')
            cv.set(lx, 28, 'navy4')
            cv.set(lx + 1, 29, 'navy1')
    if active:
        # Тёплая полоска света между линзами — «приём идёт».
        for x0, x1 in ((9, 13), (18, 22)):
            cv.hline(x0, x1, 29, 'amber2')


def depot_slot(active: bool = False) -> Canvas:
    """Люк приёма в настиле: железная рама заподлицо с досками, крышка с монетой; ниже линии ног — сигнальная планка.
    active — место занято (резерв или обслуживание): крышка откинута, в жёлобе монеты, линзы планки горят."""
    cv = Canvas(T, T)
    # Рама люка: железная полоса 2 px, светлая кромка сверху-слева, тёмная снизу-справа; без чёрного контура.
    cv.rect(5, 5, 22, 21, 'stone4')
    cv.hline(5, 26, 5, 'stone6')
    cv.vline(5, 5, 25, 'stone5')
    cv.hline(6, 26, 25, 'stone2')
    cv.vline(26, 6, 25, 'stone2')
    for x, y in ((6, 6), (25, 6), (6, 24), (25, 24)):
        cv.set(x, y, 'stone7' if y == 6 else 'stone6')   # заклёпки по углам
    if not active:
        lid = blank(cv)
        lid.rect(7, 7, 18, 17, 'wood3')
        for yy in (11, 15, 19):
            lid.hline(7, 24, yy, 'wood2')
        lid.hline(7, 24, 7, 'wood5')
        lid.vline(7, 7, 23, 'wood4')
        lid.hline(7, 24, 23, 'wood1')
        lid.vline(24, 8, 23, 'wood2')
        put(cv, lid, None)
        # Монета на крышке.
        coin = blank(cv)
        coin.ellipse(16, 15.5, 4.5, 4.5, 'gold3')
        coin.fill_where(lambda x, y: (x - 15.5) + (y - 15) > 2, 'gold2')
        coin.fill_where(lambda x, y: (x - 15.5) + (y - 15) < -3, 'gold4')
        put(cv, coin, 'gold1')
        cv.set(15, 14, 'gold5')
        # Петли и кольцо-ручка.
        for hx in (10, 20):
            cv.rect(hx, 6, 3, 2, 'stone5')
            cv.set(hx, 6, 'stone7')
        _pts(cv, [(15, 21), (16, 21), (14, 22), (17, 22)], 'stone6')
    else:
        pit = blank(cv)
        pit.rect(7, 7, 18, 17, 'ink1')
        pit.rect(8, 11, 16, 13, 'ink0')
        pit.hline(7, 24, 7, 'wood1')
        pit.hline(7, 24, 8, 'wood2')   # верх жёлоба виден в глубине
        pit.hline(8, 23, 9, 'wood1')
        put(cv, pit, None)
        for x, y, c in ((11, 13, 'gold3'), (12, 13, 'gold4'), (18, 15, 'gold3'), (19, 15, 'gold2'), (15, 18, 'gold4'),
                        (16, 18, 'gold3'), (10, 20, 'gold2'), (21, 21, 'gold2')):
            cv.set(x, y, c)
        cv.set(15, 17, 'gold5')
        # Откинутая крышка вверх (лежит на настиле над люком, видна торцом).
        lid = blank(cv)
        lid.rect(8, 1, 16, 3, 'wood3')
        lid.hline(8, 23, 1, 'wood5')
        lid.hline(8, 23, 3, 'wood2')
        put(cv, lid, 'wood1')
        # Тёплый отсвет по раме.
        _pts(cv, [(6, 6), (7, 6), (8, 6), (25, 6), (6, 24), (25, 24)], 'amber4')
        cv.hline(9, 22, 6, 'amber3')
    _slot_strip(cv, active)
    return cv


def depot_chute(active: bool = False) -> Canvas:
    """Жёлоб от люка к воронке склада через нижний ряд площадки (наклейка пола 32×32, бесшовно по вертикали):
    узкий деревянный лоток в настиле с железными скобами; по дну скатываются монеты.
    active — место над жёлобом занято: по дну бежит тёплый свет и больше монет (сигнал виден, даже если котик закрыл люк)."""
    cv = Canvas(T, T)
    lay = blank(cv)
    lay.rect(10, 0, 12, T, 'wood3')
    lay.vline(10, 0, T - 1, 'wood5')
    lay.vline(11, 0, T - 1, 'wood4')
    lay.vline(20, 0, T - 1, 'wood2')
    lay.vline(21, 0, T - 1, 'wood3')
    lay.rect(13, 0, 6, T, 'amber1' if active else 'wood2')
    lay.vline(13, 0, T - 1, 'amber0' if active else 'wood1')
    if active:
        lay.vline(15, 0, T - 1, 'amber2')
        lay.vline(16, 0, T - 1, 'amber2')
    for yy in (4, 20):
        lay.hline(10, 21, yy, 'stone4')
        lay.hline(10, 21, yy + 1, 'stone2')
        lay.set(10, yy, 'stone6')
    cv.blit(lay, 0, 0)
    cv.vline(9, 0, T - 1, 'wood1')
    cv.vline(22, 0, T - 1, 'wood1')
    coins = ((15, 9), (16, 26)) + (((14, 13), (16, 1), (15, 17), (14, 29)) if active else ())
    for x, y in coins:
        cv.set(x, y, 'gold4')
        cv.set(x + 1, y, 'gold3')
        cv.set(x, y + 1, 'gold2')
    if active:
        for x, y in ((15, 9), (14, 13), (16, 26)):
            cv.set(x, y, 'gold5')
    return cv


# ───────────────────────── ящик с монетами 32×32 (передний ряд) ─────────────────────────
CR_X0, CR_X1, CR_YT, CR_YF, CR_YB = 4, 27, 9, 15, 30     # края ящика: x, верх обвода, верх передней стенки, низ


def crate_front() -> Canvas:
    """Передняя стенка открытого ящика: две доски, железные уголки, наклейка с монетой (кладётся поверх монет)."""
    cv = Canvas(T, T)
    x0, x1, yf, yb = CR_X0, CR_X1, CR_YF, CR_YB
    box = blank(cv)
    box.rect(x0, yf, x1 - x0 + 1, yb - yf + 1, 'wood4')
    box.hline(x0, x1, yf, 'wood6')
    box.hline(x0 + 1, x1 - 1, yf + 7, 'wood2')
    box.hline(x0 + 1, x1 - 1, yf + 8, 'wood5')
    box.hline(x0, x1, yb - 1, 'wood3')
    box.hline(x0, x1, yb, 'wood2')
    box.vline(x0, yf, yb, 'wood5')
    box.vline(x1, yf + 1, yb, 'wood2')
    for xx, yy in ((x0, yf), (x1 - 1, yf), (x0, yb - 1), (x1 - 1, yb - 1)):
        box.rect(xx, yy, 2, 2, 'stone3')
        box.set(xx, yy, 'stone5')
    put(cv, box, 'wood0')
    cv.rect(13, 18, 6, 4, 'wood1')
    cv.rect(14, 19, 4, 2, 'cream2')
    _pts(cv, [(15, 19), (16, 19)], 'gold3')
    cv.set(15, 20, 'gold4')
    cv.set(16, 20, 'gold2')
    return cv


def depot_crate(fill: int) -> Canvas:
    """Открытый ящик под жёлобом выдачи: 0 — пустой, 1 — монеты на дне, 2 — почти полный, 3 — с горкой и блеском."""
    cv = Canvas(T, T)
    x0, x1, yt, yf, yb = CR_X0, CR_X1, CR_YT, CR_YF, CR_YB
    # Нутро (вид сверху 3/4): обвод стенок, тёмное дно.
    box = blank(cv)
    box.rect(x0, yt, x1 - x0 + 1, yf - yt + 1, 'wood2')
    box.rect(x0 + 2, yt + 1, x1 - x0 - 3, yf - yt - 1, 'wood1')
    box.rect(x0 + 3, yt + 3, x1 - x0 - 5, yf - yt - 3, 'ink2')
    box.hline(x0, x1, yt, 'wood5')
    box.vline(x0, yt, yf, 'wood5')
    box.vline(x0 + 1, yt + 1, yf, 'wood4')
    box.vline(x1, yt, yf, 'wood3')
    box.vline(x1 - 1, yt + 1, yf, 'wood3')
    put(cv, box, 'wood0')
    if fill >= 1:
        lvl = {1: yf - 1, 2: yt + 3, 3: yt + 1}[fill]
        cv.rect(x0 + 2, lvl, x1 - x0 - 3, yf - lvl, 'gold3')
        cv.hline(x0 + 2, x1 - 2, lvl, 'gold4')
        coins = [(9, lvl + 0.5), (14, lvl + 1), (20, lvl + 0.5), (24, lvl + 1)]
        if fill >= 2:
            coins += [(11.5, lvl - 1), (18, lvl - 1), (22.5, lvl - 0.5)]
        if fill == 3:
            mound = blank(cv)
            mound.ellipse(16, yt - 0.5, 9, 3.5, 'gold3')
            mound.fill_where(lambda x, y: y <= yt - 3, 'gold4')
            mound.fill_where(lambda x, y: x >= 21 and y >= yt - 1, 'gold2')
            put(cv, mound, 'gold1')
            coins += [(12, yt - 3), (16, yt - 4), (20, yt - 2.5), (14, yt - 1.5), (18.5, yt - 1)]
        for cx, cy in coins:
            coin_top(cv, cx, cy)
    cv.blit(crate_front(), 0, 0)
    if fill == 3:
        for x, y, c in ((25, 2, 'gold5'), (24, 3, 'gold5'), (26, 3, 'gold5'), (25, 4, 'gold5'), (25, 3, 'cream4')):
            cv.set(x, y, c)
    return cv


# ───────────────────────── постройки 1×2 (32×64, ноги на нижней кромке st1) ─────────────────────────
BW, BH = 32, 64
B_GROUND = 63


def small_roof(cv: Canvas, x0: int, x1: int, y: int, h: int = 6, seed: int = 1) -> None:
    """Навес из красного гонта над постройкой 1×2: свес по бокам на 1 px, правый край в тени."""
    shingle_roof(cv, [(x0 + 2, y), (x1 - 2, y), (x1, y + h), (x0, y + h)], (y + 3,), seed=seed, right_shade_x=x1 - 3)


def notice(cv: Canvas, x: int, y: int, kind: int) -> None:
    """Объявление о найме 7×8: бумага, булавка; kind 0 — лапка, 1 — мордочка котика, 2 — монета (оплата)."""
    lay = blank(cv)
    lay.rect(x, y, 7, 8, 'cream2')
    lay.hline(x, x + 6, y, 'cream3')
    lay.vline(x, y, y + 7, 'cream3')
    lay.hline(x + 1, x + 6, y + 7, 'cream1')
    lay.vline(x + 6, y + 1, y + 7, 'cream1')
    put(cv, lay, 'wood1')
    if kind == 0:
        _pts(cv, [(x + 2, y + 2), (x + 4, y + 2), (x + 1, y + 4), (x + 5, y + 4)], 'pink2')
        _pts(cv, [(x + 2, y + 5), (x + 3, y + 5), (x + 4, y + 5), (x + 3, y + 4)], 'pink1')
    elif kind == 1:
        _pts(cv, [(x + 1, y + 2), (x + 5, y + 2), (x + 1, y + 3), (x + 2, y + 3), (x + 3, y + 3), (x + 4, y + 3), (x + 5, y + 3),
                  (x + 1, y + 4), (x + 3, y + 4), (x + 5, y + 4), (x + 2, y + 5), (x + 3, y + 5), (x + 4, y + 5)], 'fur_ginger2')
        _pts(cv, [(x + 2, y + 4), (x + 4, y + 4)], 'ink1')
    else:
        _pts(cv, [(x + 3, y + 2), (x + 2, y + 3), (x + 4, y + 3), (x + 3, y + 4), (x + 2, y + 4), (x + 4, y + 4), (x + 3, y + 5)], 'gold3')
        cv.set(x + 3, y + 3, 'gold4')
        _pts(cv, [(x + 2, y + 6), (x + 3, y + 6), (x + 4, y + 6)], 'cream0')
    cv.set(x + 3, y, 'red4')                   # булавка
    cv.set(x + 3, y - 1, 'red2')


def hire_board(tier: int) -> Canvas:
    """Доска найма: два столба, навес, доска с объявлениями (лапка, мордочка, монета). Уровень — больше объявлений:
    0 — два, 1 — четыре, 2 — шесть и золотая розетка «лучшая команда», колокольчик под навесом."""
    cv = Canvas(BW, BH)
    for i, px in enumerate((3, 25)):
        vbeam(cv, px, 18, 4, B_GROUND - 18 + 1, WOOD, seed=60 + i)
    # Доска: рама из бруса и светлое поле.
    board = blank(cv)
    board.rect(1, 20, 30, 26, 'wood5')
    for yy in (26, 33, 40):
        board.hline(2, 29, yy, 'wood4')
    board.hline(1, 30, 20, 'wood6')
    board.vline(1, 20, 45, 'wood6')
    board.hline(2, 30, 45, 'wood3')
    board.vline(30, 21, 45, 'wood3')
    put(cv, board, 'wood0')
    hbeam(cv, 0, 16, 32, 5, WOOD, seed=62)
    # Табличка-шапка с мордочкой котика.
    head = Canvas.from_ascii([
        '.c.....c.',
        '.cc...cc.',
        '.ccccccc.',
        'cckcccckc',
        'ccccppccc',
        '.ccccccc.',
    ], {'c': 'cream3', 'k': 'ink1', 'p': 'pink2'})
    plate = blank(cv)
    plate.rect(10, 6, 12, 10, 'wood3')
    plate.hline(10, 21, 6, 'wood4')
    put(cv, plate, 'wood0')
    small_roof(cv, 0, 31, 9, 7, seed=3 + tier)
    plate2 = blank(cv)
    plate2.rect(10, 11, 12, 8, 'wood3')
    plate2.hline(10, 21, 11, 'wood4')
    plate2.vline(21, 12, 18, 'wood2')
    put(cv, plate2, 'wood0')
    cv.blit(head, 12, 12)
    spots = [(4, 24, 0), (19, 23, 1), (11, 27, 2), (4, 35, 1), (20, 34, 0), (12, 37, 0)]
    for x, y, k in spots[:(2, 4, 6)[tier]]:
        notice(cv, x, y, k)
    if tier == 2:
        # Золотая розетка с лентами — «команда в сборе».
        ros = Canvas.from_ascii([
            '.ggg.',
            'gGYGg',
            'gYWYg',
            'gGYGg',
            '.ggg.',
            '.r.r.',
            'rr.rr',
        ], {'g': 'gold2', 'G': 'gold3', 'Y': 'gold4', 'W': 'gold5', 'r': 'red3'})
        r2 = blank(cv)
        r2.blit(ros, 24, 38)
        put(cv, r2, 'ink1')
        # Колокольчик под навесом.
        cv.vline(5, 21, 22, 'stone3')
        bell = blank(cv)
        bell.rect(4, 23, 3, 3, 'gold3')
        bell.set(4, 23, 'gold4')
        bell.hline(3, 7, 26, 'gold2')
        put(cv, bell, 'gold0')
    # Ящичек с листовками у столба (уровень 1+).
    if tier >= 1:
        bx = blank(cv)
        bx.rect(8, 55, 9, 7, 'wood4')
        bx.hline(8, 16, 55, 'wood6')
        bx.hline(8, 16, 58, 'wood2')
        bx.rect(9, 52, 7, 3, 'cream2')
        bx.hline(9, 15, 52, 'cream3')
        put(cv, bx, 'wood0')
    return cv


def _hammer(cv: Canvas, x: int, y: int) -> None:
    """Молоток лежит на верстаке: рукоять вправо, головка слева; (x, y) — левый верх головки."""
    lay = blank(cv)
    lay.hline(x + 4, x + 10, y + 2, 'wood5')
    lay.hline(x + 4, x + 10, y + 3, 'wood3')
    lay.rect(x, y, 4, 6, 'stone4')
    lay.vline(x, y, y + 5, 'stone6')
    lay.vline(x + 3, y, y + 5, 'stone2')
    put(cv, lay, 'ink1')


def _hammer_v(cv: Canvas, x: int, y: int) -> None:
    """Молоток на стенде: головка сверху, рукоять вниз; (x, y) — левый верх головки."""
    lay = blank(cv)
    lay.rect(x, y, 6, 3, 'stone4')
    lay.hline(x, x + 5, y, 'stone6')
    lay.hline(x, x + 5, y + 2, 'stone2')
    lay.vline(x + 2, y + 3, y + 10, 'wood5')
    lay.vline(x + 3, y + 3, y + 10, 'wood3')
    put(cv, lay, 'ink1')


def _wrench_v(cv: Canvas, x: int, y: int) -> None:
    """Гаечный ключ на стенде: два зева, стальная ручка; (x, y) — левый верх."""
    lay = blank(cv)
    lay.rect(x, y, 4, 3, 'stone5')
    lay.rect(x + 1, y, 2, 1, None)
    lay.vline(x + 1, y + 3, y + 9, 'stone5')
    lay.vline(x + 2, y + 3, y + 9, 'stone3')
    lay.rect(x, y + 10, 4, 3, 'stone4')
    lay.rect(x + 1, y + 12, 2, 1, None)
    put(cv, lay, 'ink1')


def _anvil_small(cv: Canvas, x: int, y: int) -> None:
    """Маленькая наковальня 10×6 на верстаке; (x, y) — левый верх."""
    lay = Canvas.from_ascii([
        'HHHHHHHHH.',
        'LLLLLLLLLL',
        '.BBBBBBDD.',
        '...BBBD...',
        '..LBBBDD..',
        '..DDDDDS..',
    ], {'H': 'stone6', 'L': 'stone5', 'B': 'stone4', 'D': 'stone3', 'S': 'stone2'})
    lay2 = blank(cv)
    lay2.blit(lay, x, y)
    put(cv, lay2, 'ink1')


def _pick_icon(cv: Canvas, x: int, y: int, head: Sequence[str], handle: Sequence[str] = ('wood5', 'wood3')) -> None:
    """Кирка 11×11: рукоять по диагонали, головка-дуга; head — (свет, основа, тень)."""
    lay = blank(cv)
    for i in range(9):
        lay.set(x + 1 + i, y + 10 - i, handle[0])
        lay.set(x + 2 + i, y + 10 - i, handle[1])
    arc = [(0, 3), (1, 2), (2, 1), (3, 1), (4, 0), (5, 0), (6, 0), (7, 1), (8, 1), (9, 2), (10, 3)]
    for i, (dx, dy) in enumerate(arc):
        lay.set(x + dx, y + dy, head[0] if i < 6 else head[1])
        lay.set(x + dx, y + dy + 1, head[1] if i < 6 else head[2])
    put(cv, lay, 'ink1')


def bench(tier: int) -> Canvas:
    """Верстак инструментов: столешница на ножках, под ней полка. Уровень — больше инструментов:
    0 — молоток на столешнице и кирка у ножки; 1 — стенд с молотком, киркой и ключом, наковальня, ящик с инструментами;
    2 — на стенде золотая кирка, фонарь под балкой, на столешнице — вторая кирка с бирюзовой головкой."""
    cv = Canvas(BW, BH)
    if tier >= 1:
        for px in (2, 27):
            vbeam(cv, px, 10, 3, 32, DWOOD, seed=70 + px, cap=False)
        peg = blank(cv)
        peg.rect(4, 12, 24, 26, 'wood2')
        peg.hline(4, 27, 12, 'wood3')
        for yy in range(15, 37, 4):
            for xx in range(6, 27, 4):
                peg.set(xx, yy, 'wood1')
        put(cv, peg, 'wood0')
        hbeam(cv, 1, 8, 30, 4, DWOOD, seed=73)
        _hammer_v(cv, 5, 15)
        _wrench_v(cv, 23, 15)
        if tier == 2:
            _pick_icon(cv, 10, 17, ('gold5', 'gold3', 'gold1'))
            cv.set(21, 14, 'gold5')
            cv.set(20, 15, 'gold4')
            cv.set(22, 15, 'gold4')
        else:
            _pick_icon(cv, 10, 17, ('stone6', 'stone4', 'stone2'))
        # Крючки под инструменты.
        for hx in (7, 15, 24):
            cv.set(hx, 14, 'stone5')
    for i, px in enumerate((3, 26)):
        vbeam(cv, px, 42, 4, B_GROUND - 42 + 1, WOOD, seed=80 + i, cap=False)
    hbeam(cv, 3, 55, 27, 3, WOOD, seed=82)
    top = blank(cv)
    top.rect(1, 38, 30, 4, 'wood5')
    top.hline(1, 30, 38, 'wood6')
    top.rect(1, 42, 30, 3, 'wood3')
    top.hline(1, 30, 44, 'wood2')
    grain_h(top, 3, 39, 26, 2, 'wood4', 84, step=2)
    put(cv, top, 'wood0')
    if tier == 0:
        _hammer(cv, 5, 34)
        _pick_icon(cv, 18, 44, ('stone6', 'stone4', 'stone2'))
    else:
        _anvil_small(cv, 3, 33)
        tb = blank(cv)
        tb.rect(7, 50, 14, 5, 'red3')
        tb.hline(7, 20, 50, 'red4')
        tb.hline(7, 20, 52, 'red2')
        tb.rect(12, 48, 4, 2, 'stone3')
        tb.set(12, 48, 'stone5')
        put(cv, tb, 'red0')
        if tier == 1:
            _hammer(cv, 17, 34)
        else:
            _pick_icon(cv, 17, 28, ('teal5', 'teal4', 'teal2'))
            lantern(cv, 22, 1)
    return cv


def bag_sack(cv: Canvas, x: int, y: int, w: int = 8, h: int = 9, hang: bool = False, coins: bool = False, tint: str = 'cream') -> None:
    """Мешок-сумка w×h: мешковина, верёвка-завязка, блик слева; hang — подвешен за петлю; coins — монеты в горловине."""
    lay = blank(cv)
    body = Canvas(cv.w, cv.h)
    body.ellipse(x + w / 2.0, y + h * 0.62, w / 2.0, h * 0.42, f'{tint}1')
    body.rect(x + 2, y + 2, w - 4, h // 2, f'{tint}1')
    body.fill_where(lambda px, py: px < x + w * 0.38 and py < y + h - 2, f'{tint}2')
    body.fill_where(lambda px, py: px >= x + w - 2 or py >= y + h - 1, f'{tint}0')
    lay.blit(body, 0, 0)
    lay.hline(x + 2, x + w - 3, y + 2, 'wood4')     # завязка
    lay.set(x + w // 2, y + 3, 'wood3')
    lay.hline(x + 3, x + w - 4, y, f'{tint}1')       # горловина
    lay.hline(x + 3, x + w - 4, y + 1, f'{tint}2')
    if coins:
        lay.hline(x + 3, x + w - 4, y, 'gold4')
        lay.set(x + 3, y + 1, 'gold3')
    put(cv, lay, 'wood1')
    if hang:
        cv.vline(x + w // 2, y - 3, y - 1, 'wood4')
        cv.set(x + w // 2, y - 4, 'wood2')


def backpack(cv: Canvas, x: int, y: int) -> None:
    """Рюкзак 9×11 на крючке: синий клапан, кожаные ремни, латунная пряжка."""
    lay = blank(cv)
    lay.rect(x, y + 2, 9, 9, 'blue3')
    lay.rect(x + 1, y + 1, 7, 1, 'blue3')
    lay.vline(x, y + 2, y + 10, 'blue4')
    lay.vline(x + 8, y + 2, y + 10, 'blue2')
    lay.hline(x, x + 8, y + 10, 'blue2')
    lay.rect(x, y + 1, 9, 4, 'blue4')
    lay.hline(x + 1, x + 7, y + 1, 'blue5')
    lay.hline(x, x + 8, y + 5, 'blue2')
    lay.vline(x + 4, y + 4, y + 7, 'wood3')
    lay.set(x + 4, y + 6, 'gold4')
    lay.rect(x + 2, y + 8, 5, 2, 'blue4')
    put(cv, lay, 'blue0')
    cv.vline(x + 4, y - 2, y, 'wood4')


def rope_coil(cv: Canvas, x: int, y: int) -> None:
    lay = blank(cv)
    lay.ellipse(x + 4.5, y + 2.5, 4.5, 2.5, 'wood5')
    lay.ellipse(x + 4.5, y + 2.5, 2.5, 1.2, 'wood3')
    lay.set(x + 4, y + 2, 'wood1')
    lay.hline(x + 1, x + 3, y + 1, 'wood6')
    put(cv, lay, 'wood1')


def rack(tier: int) -> Canvas:
    """Стеллаж сумок: стойки, верхняя перекладина с крючками, две полки. Уровень — больше и больше сумок:
    0 — одна сумка на крючке и мешок на полке; 1 — три на крючках, моток верёвки, мешки; 2 — полный стеллаж и рюкзак."""
    cv = Canvas(BW, BH)
    for i, px in enumerate((2, 26)):
        vbeam(cv, px, 8, 4, B_GROUND - 8 + 1, WOOD, seed=90 + i)
    # Задняя стенка между стойками — тёмные доски (стеллаж читается объёмным).
    back = blank(cv)
    back.rect(6, 12, 20, 48, 'wood1')
    for xx in (11, 16, 21):
        back.vline(xx, 12, 59, 'wood0')
    cv.blit(back, 0, 0)
    hbeam(cv, 0, 8, 32, 4, WOOD, seed=93)
    for yy in (33, 50):
        shelf = blank(cv)
        shelf.rect(2, yy, 28, 3, 'wood4')
        shelf.hline(2, 29, yy, 'wood6')
        shelf.hline(2, 29, yy + 2, 'wood2')
        put(cv, shelf, 'wood0')
    # Крючки на верхней перекладине.
    hooks = (9, 16, 23)
    for hx in hooks:
        cv.set(hx, 12, 'stone5')
        cv.set(hx, 13, 'stone3')
    hung = {0: [9], 1: [9, 16, 23], 2: [9, 16]}[tier]
    for hx in hung:
        bag_sack(cv, hx - 4, 17, 8, 10, hang=True)
    if tier == 2:
        backpack(cv, 19, 16)
    # Средняя полка.
    if tier == 0:
        bag_sack(cv, 7, 23 + 1, 9, 9)
    else:
        bag_sack(cv, 5, 23 + 1, 9, 9, coins=tier == 2)
        rope_coil(cv, 17, 28)
        if tier == 2:
            bag_sack(cv, 15, 22, 8, 9)
            rope_coil(cv, 17, 28)
    # Нижняя полка/пол.
    floor_bags = {0: [], 1: [(6, 40, 9, 10)], 2: [(4, 40, 9, 10), (14, 41, 8, 9), (20, 40, 8, 10)]}[tier]
    for bx, by, bw, bh in floor_bags:
        bag_sack(cv, bx, by, bw, bh)
    if tier >= 1:
        bag_sack(cv, 9, 53, 10, 10, coins=True)
    return cv


# ───────────────────────── вагонетка 32×32 (передний ряд) ─────────────────────────
def cart(loaded: int) -> Canvas:
    """Вагонетка лагеря на коротком отрезке пути: дощатый кузов, железная обвязка, два колеса спереди.
    0 — пустая (старая), 1 — гружёная мешками с монетами (транспорт улучшен)."""
    cv = Canvas(T, T)
    # Шпалы и рельсы под колёсами (путь уходит вглубь — к складу).
    for yy in (27, 30):
        cv.rect(3, yy, 26, 2, 'wood2')
        cv.hline(3, 28, yy, 'wood3')
    for rx in (9, 22):
        cv.vline(rx, 25, 31, 'stone5')
        cv.vline(rx + 1, 25, 31, 'stone2')
    if loaded:
        for bx, by, bw, bh in ((5, 5, 9, 10), (17, 4, 9, 11), (11, 2, 9, 10)):
            bag_sack(cv, bx, by, bw, bh, coins=True)
    body = blank(cv)
    body.polygon([(3, 11), (29, 11), (27, 25), (5, 25)], 'wood3')
    for yy in (16, 20):
        body.fill_where(lambda x, y, yy=yy: y == yy, 'wood2')
        body.fill_where(lambda x, y, yy=yy: y == yy + 1, 'wood4')
    body.hline(3, 29, 11, 'stone5')
    body.hline(4, 28, 12, 'stone3')
    if not loaded:
        body.rect(5, 12, 22, 3, 'ink2')
        body.hline(5, 26, 12, 'ink1')
        body.hline(4, 28, 15, 'stone4')
    body.fill_where(lambda x, y: y >= 13 and (x <= 6 or x >= 25), 'stone3')
    body.fill_where(lambda x, y: y >= 13 and x <= 5, 'stone4')
    body.hline(5, 27, 24, 'stone2')
    put(cv, body, 'stone0')
    for x, y in ((5, 15), (26, 15), (6, 22), (25, 22)):
        bolt(cv, x, y)
    # Паспорт вагонетки — лапка.
    paw(cv, 12, 15, 'wood1') if not loaded else paw(cv, 12, 15, 'gold2')
    for wx in (9.5, 22.5):
        wh = blank(cv)
        wh.ellipse(wx, 26.5, 3, 3, 'stone3')
        wh.set(int(wx) - 1, 25, 'stone5')
        wh.set(int(wx), 26, 'stone1')
        put(cv, wh, 'stone0')
    return cv


# ───────────────────────── шкаф экипировки 32×64 ─────────────────────────
def wardrobe(open_: bool) -> Canvas:
    """Шкаф постоянной экипировки: резной фронтон с золотой каской, две дверцы с ручками-лапками, ножки.
    open — дверцы распахнуты: внутри тёплый свет, каска на полке, кирка, рюкзак и ботинки."""
    cv = Canvas(BW, BH)
    x0, x1, yt, yb = 4, 27, 12, 60
    body = blank(cv)
    body.rect(x0, yt, x1 - x0 + 1, yb - yt + 1, 'wood3')
    body.vline(x0, yt, yb, 'wood4')
    body.vline(x1, yt, yb, 'wood2')
    body.hline(x0, x1, yb, 'wood2')
    put(cv, body, 'wood0')
    # Ножки.
    for fx in (x0 + 1, x1 - 2):
        cv.rect(fx, yb + 1, 2, 2, 'wood2')
        cv.set(fx, yb + 1, 'wood4')
        cv.set(fx - 1, yb + 2, 'wood0')
        cv.set(fx + 2, yb + 2, 'wood0')
    # Фронтон-арка с каской.
    ped = blank(cv)
    ped.polygon([(x0 - 1, yt + 1), (x0 + 3, yt - 4), (x1 - 3, yt - 4), (x1 + 1, yt + 1)], 'wood4')
    ped.ellipse(16, yt - 4, 7, 4, 'wood4')
    ped.fill_where(lambda x, y: y <= yt - 6, 'wood5')
    ped.fill_where(lambda x, y: y == yt or y == yt + 1, 'wood2')
    put(cv, ped, 'wood0')
    helm = Canvas.from_ascii([
        '..yyy..',
        '.yYYyy.',
        'yYyaayy',
        'yyyaayy',
        'ddddddd',
    ], {'y': 'gold3', 'Y': 'gold5', 'a': 'amber4', 'd': 'gold1'})
    hl = blank(cv)
    hl.blit(helm, 13, yt - 9)
    put(cv, hl, 'gold0')
    if not open_:
        for dx0, dx1 in ((x0 + 2, 15), (16, x1 - 2)):
            door = blank(cv)
            door.rect(dx0, yt + 4, dx1 - dx0 + 1, yb - yt - 6, 'wood4')
            door.hline(dx0, dx1, yt + 4, 'wood5')
            door.vline(dx0, yt + 4, yb - 3, 'wood5')
            door.hline(dx0, dx1, yb - 3, 'wood2')
            door.vline(dx1, yt + 5, yb - 3, 'wood2')
            # Филёнки.
            for py0, py1 in ((yt + 7, yt + 22), (yt + 26, yb - 6)):
                door.rect(dx0 + 2, py0, dx1 - dx0 - 3, py1 - py0, 'wood3')
                door.hline(dx0 + 2, dx1 - 2, py0, 'wood2')
                door.vline(dx0 + 2, py0, py1 - 1, 'wood2')
                door.hline(dx0 + 3, dx1 - 1, py1, 'wood5')
            put(cv, door, None)
        cv.vline(15, yt + 4, yb - 3, 'wood1')
        cv.vline(16, yt + 4, yb - 3, 'wood1')
        # Ручки-лапки (золото).
        for hx in (13, 18):
            _pts(cv, [(hx, yt + 23), (hx + 1, yt + 23), (hx, yt + 24), (hx + 1, yt + 24)], 'gold3')
            cv.set(hx, yt + 23, 'gold5')
            cv.set(hx + (1 if hx > 15 else 0), yt + 22, 'gold4')
        # Замочная скважина.
        cv.set(15, yt + 27, 'ink2')
        cv.set(16, yt + 27, 'ink2')
    else:
        inner = blank(cv)
        inner.rect(x0 + 2, yt + 3, x1 - x0 - 3, yb - yt - 5, 'wood1')
        glow(inner, in_ellipse(16, yt + 14, 10, 14), [(1.0, 'wood2', 255), (0.6, 'amber1', 255)])
        clip(inner, lambda x, y: x0 + 2 <= x <= x1 - 2 and yt + 3 <= y <= yb - 3)
        cv.blit(inner, 0, 0)
        # Полка с каской, кирка, рюкзак, ботинки.
        cv.hline(x0 + 2, x1 - 2, yt + 13, 'wood4')
        cv.hline(x0 + 2, x1 - 2, yt + 14, 'wood2')
        hel = Canvas.from_ascii([
            '..yyy..',
            '.yYyyy.',
            'yyyaayy',
            'ddddddd',
        ], {'y': 'gold3', 'Y': 'gold4', 'a': 'amber4', 'd': 'gold1'})
        h2 = blank(cv)
        h2.blit(hel, 12, yt + 9)
        put(cv, h2, 'ink1')
        _pick_icon(cv, 7, yt + 17, ('stone6', 'stone4', 'stone2'))
        backpack(cv, 18, yt + 19)
        boots = Canvas.from_ascii([
            'bb..bb.',
            'bb..bb.',
            'bbb.bbb',
            'ddd.ddd',
        ], {'b': 'wood4', 'd': 'wood1'})
        b2 = blank(cv)
        b2.blit(boots, 11, yb - 9)
        put(cv, b2, 'ink1')
        cv.hline(x0 + 2, x1 - 2, yb - 4, 'wood4')
        # Распахнутые дверцы — узкие створки по бокам (повёрнуты к зрителю).
        for dx in (0, x1 + 1):
            door = blank(cv)
            door.polygon([(dx, yt + 3), (dx + 4, yt + 5), (dx + 4, yb - 1), (dx, yb - 4)], 'wood4')
            door.fill_where(lambda x, y, dx=dx: x == dx + (0 if dx == 0 else 3), 'wood5' if dx == 0 else 'wood2')
            put(cv, door, 'wood0')
            cv.set(dx + 2, yt + 25, 'gold4')
    return cv


# ───────────────────────── декор лагеря ─────────────────────────
def camp_lantern(frame: int) -> Canvas:
    """Фонарь на столбике 16×40 (передний ряд лагеря): каменная пятка, столбик, кованый кронштейн, фонарь;
    кадр 1 — притушенный (мерцание, как lantern_* у стен)."""
    cv = Canvas(16, 40)
    base = blank(cv)
    base.rect(4, 35, 8, 4, 'stone3')
    base.hline(4, 11, 35, 'stone5')
    base.hline(4, 11, 38, 'stone1')
    put(cv, base, 'stone0')
    vbeam(cv, 6, 14, 4, 22, WOOD, seed=5, cap=False)
    cv.hline(5, 10, 14, 'stone4')
    cv.hline(5, 10, 15, 'stone2')
    lay = Canvas.from_ascii([
        '...oo...',
        '..o..o..',
        '.oKKKKo.',
        'oJJJJJJo',
        'oJ1221Jo',
        'oK2342Ko',
        'oK3453Ko',
        'oK2342Ko',
        'oJ1221Jo',
        'oJJJJJJo',
        '.oKKKKo.',
        '..oJJo..',
    ], {'o': 'ink1', 'J': 'stone2', 'K': 'stone3',
        **({'1': 'amber1', '2': 'amber2', '3': 'amber3', '4': 'amber4', '5': 'amber5'} if frame == 0 else
           {'1': 'amber1', '2': 'amber1', '3': 'amber2', '4': 'amber3', '5': 'amber4'})})
    lay.set(1, 4, 'stone5')
    cv.blit(lay, 4, 2)
    return cv


def camp_sign() -> Canvas:
    """Вывеска лагеря на двух столбиках 32×32: доска с лапкой и монеткой, гвоздики, тень под доской."""
    cv = Canvas(T, T)
    for px in (6, 23):
        vbeam(cv, px, 14, 3, 17, WOOD, seed=px, cap=False)
    board = blank(cv)
    board.rect(1, 4, 30, 15, 'wood5')
    board.hline(1, 30, 4, 'wood6')
    board.vline(1, 4, 18, 'wood6')
    board.hline(2, 30, 18, 'wood3')
    board.vline(30, 5, 18, 'wood3')
    board.hline(2, 29, 11, 'wood4')
    put(cv, board, 'wood0')
    paw(cv, 6, 7, 'wood2')
    coin = blank(cv)
    coin.ellipse(22, 11, 4, 4, 'gold3')
    coin.fill_where(lambda x, y: (x - 21.5) + (y - 10.5) > 1.5, 'gold2')
    put(cv, coin, 'gold0')
    cv.set(21, 9, 'gold5')
    for bx in (2, 29):
        cv.set(bx, 5, 'stone5')
    return cv


def camp_tent() -> Canvas:
    """Палатка-шатёр 2×1 (64×48, доп. декор): полосатый тент, откинутый полог, внутри — спальный мешок и фонарик."""
    cv = Canvas(64, 48)
    tent = blank(cv)
    tent.polygon([(4, 44), (32, 6), (60, 44)], 'cream2')
    # Полосы тента.
    tent.fill_where(lambda x, y: int(math.floor((x + 0.5 - 32) / max(1.0, y - 5.0) * 3.2)) % 2 == 0 and y > 8, 'red3')
    tent.fill_where(lambda x, y: x >= 33 and tent.get(x, y)[:3] == pxl.PALETTE['red3'], 'red2')
    tent.fill_where(lambda x, y: x >= 33 and tent.get(x, y)[:3] == pxl.PALETTE['cream2'], 'cream1')
    put(cv, tent, 'red0')
    door = blank(cv)
    door.polygon([(22, 44), (32, 18), (42, 44)], 'ink2')
    door.polygon([(27, 44), (32, 30), (37, 44)], 'wood1')
    cv.blit(door, 0, 0)
    # Откинутый полог.
    flap = blank(cv)
    flap.polygon([(32, 18), (22, 44), (17, 44)], 'cream3')
    flap.fill_where(lambda x, y: y > 38, 'cream2')
    put(cv, flap, 'red0')
    cv.set(31, 36, 'amber4')
    cv.set(32, 36, 'amber3')
    # Колышки и растяжки.
    for x0, x1 in ((4, 0), (60, 63)):
        cv.line(x0, 40, x1, 46, 'cream0')
    cv.vline(32, 2, 7, 'wood3')
    cv.set(33, 2, 'red4')
    cv.set(34, 3, 'red4')
    cv.set(33, 3, 'red3')
    return cv


# ───────────────────────── склад товаров 32×64 (ТЗ 2.2 §6.1) ─────────────────────────
# Склад (Warehouse) стоит в столбце baseCol + 4 заднего ряда двора, рядом со стеллажом: ноги на нижней кромке st1, как
# у остальных построек 1×2. Трубы заводов приходят сверху (из площадки) в стальной коллектор на коньке: точка входа —
# пиксель WAREHOUSE_INLET холста (от левого верхнего угла), туда код ставит fpipe_inlet. Тап по складу открывает рынок,
# поэтому на фронтоне — вывеска: ящик товара с монетой. Пустой — ворота открыты, внутри пустые полки; с товаром —
# ящики, мешки и бочонок разных цветов в проёме и у стены.
WAREHOUSE_INLET = (16, 4)


def _wh_crate(cv: Canvas, x: int, y: int, w: int, h: int, tone: str) -> None:
    """Ящик товара w×h: крышка светлее, доска поперёк, контур — тёмный тон рампы."""
    lay = blank(cv)
    lay.rect(x, y, w, h, f'{tone}3')
    lay.hline(x, x + w - 1, y, f'{tone}5')
    lay.hline(x, x + w - 1, y + 1, f'{tone}4')
    lay.vline(x + w - 1, y + 1, y + h - 1, f'{tone}2')
    if h >= 6:
        lay.hline(x + 1, x + w - 2, y + h // 2 + 1, f'{tone}2')
    put(cv, lay, 'ink1')


def warehouse(full: bool) -> Canvas:
    """Склад товаров: коллектор труб на коньке, двускатная крыша из красного гонта, фронтон с вывеской (ящик и монета),
    дощатые стены на каменном цоколе, распахнутые ворота; full — проём заставлен товаром."""
    cv = Canvas(BW, BH)
    ix, iy = WAREHOUSE_INLET
    # Коллектор: стальной короб с воронкой, в него сверху входят трубы.
    col = blank(cv)
    col.rect(ix - 5, iy, 10, 6, 'stone4')
    col.hline(ix - 5, ix + 4, iy, 'stone6')
    col.vline(ix - 5, iy, iy + 5, 'stone5')
    col.vline(ix + 4, iy + 1, iy + 5, 'stone2')
    col.rect(ix - 3, iy + 6, 6, 4, 'stone3')
    col.vline(ix - 3, iy + 6, iy + 9, 'stone5')
    col.hline(ix - 5, ix + 4, iy + 3, 'gold3')
    put(cv, col, 'ink1')
    # Стены.
    wall = blank(cv)
    wall.rect(2, 22, 28, 38, 'wood3')
    for xx in range(4, 30, 4):
        wall.vline(xx, 22, 59, 'wood2')
        wall.vline(xx + 1, 22, 59, 'wood4')
    wall.hline(2, 29, 22, 'wood1')
    put(cv, wall, 'wood0')
    # Цоколь.
    base = blank(cv)
    base.rect(1, 59, 30, 5, 'stone3')
    base.hline(1, 30, 59, 'stone5')
    base.hline(1, 30, 63, 'stone1')
    for xx in (6, 13, 21, 27):
        base.vline(xx, 60, 62, 'stone1')
    put(cv, base, 'stone0')
    # Двускатная крыша.
    roof = blank(cv)
    roof.polygon([(-1, 24), (16, 9), (32, 24)], 'red3')
    for yy in (14, 18, 22):
        roof.fill_where(lambda x, y, yy=yy: y == yy, 'red2')
        roof.fill_where(lambda x, y, yy=yy: y == yy + 1, 'red4')
    remap(roof, {'red3': 'red2', 'red4': 'red3'}, lambda x, y: x >= 17)
    put(cv, roof, 'red0')
    # Фронтон: вывеска — ящик товара с золотой монетой (склад товаров и рынок).
    sign = blank(cv)
    sign.rect(9, 14, 14, 10, 'wood6')
    sign.hline(9, 22, 14, 'wood7')
    sign.hline(10, 22, 23, 'wood4')
    sign.vline(22, 15, 23, 'wood4')
    put(cv, sign, 'wood1')
    box = Canvas.from_ascii([
        'HHHHHHH.',
        'LbbbbbLd',
        'LbLbbLbd',
        'LbbLLbbd',
        'LbLbbLbd',
        'dddddddd',
    ], {'H': 'wood5', 'L': 'wood4', 'b': 'wood3', 'd': 'wood1'})
    cv.blit(box, 10, 16)
    _pts(cv, [(19, 16), (20, 16), (18, 17), (21, 17), (18, 18), (21, 18), (19, 19), (20, 19)], 'gold2')
    _pts(cv, [(19, 17), (20, 17), (19, 18)], 'gold4')
    cv.set(20, 18, 'gold3')
    # Ворота: проём, рама, распахнутые створки.
    dx0, dx1, dy0 = 7, 24, 30
    pit = blank(cv)
    pit.rect(dx0, dy0, dx1 - dx0 + 1, 59 - dy0, 'ink2')
    pit.rect(dx0 + 2, dy0 + 3, dx1 - dx0 - 3, 59 - dy0 - 3, 'wood1')
    pit.hline(dx0 + 2, dx1 - 2, dy0 + 12, 'wood2')     # полки в глубине
    pit.hline(dx0 + 2, dx1 - 2, dy0 + 20, 'wood2')
    cv.blit(pit, 0, 0)
    if full:
        for x, y, w, h, tone in ((dx0 + 2, 50, 7, 9, 'wood'), (dx0 + 9, 52, 7, 7, 'blue'), (dx0 + 4, 44, 6, 6, 'red'),
                                 (dx0 + 11, 45, 6, 7, 'green'), (dx0 + 7, 38, 6, 6, 'purple')):
            _wh_crate(cv, x, y, w, h, tone)
        bag_sack(cv, dx1 - 6, 50, 7, 9)
        # Бочонок мёда у правой створки.
        lay = blank(cv)
        lay.rect(dx1 - 4, 37, 4, 6, 'amber3')
        lay.vline(dx1 - 4, 37, 42, 'amber4')
        lay.hline(dx1 - 4, dx1 - 1, 39, 'stone3')
        put(cv, lay, 'amber0')
    fr = blank(cv)
    fr.rect(dx0 - 2, dy0 - 2, dx1 - dx0 + 5, 3, 'wood5')
    fr.hline(dx0 - 2, dx1 + 2, dy0 - 2, 'wood6')
    fr.vline(dx0 - 2, dy0, 58, 'wood5')
    fr.vline(dx0 - 1, dy0, 58, 'wood4')
    fr.vline(dx1 + 1, dy0, 58, 'wood3')
    fr.vline(dx1 + 2, dy0, 58, 'wood2')
    put(cv, fr, 'wood0')
    for sx, light in ((0, True), (28, False)):
        leaf = blank(cv)
        leaf.polygon([(sx, dy0 - 1), (sx + 4, dy0 + 1), (sx + 4, 59), (sx, 58)], 'wood4')
        leaf.fill_where(lambda x, y, sx=sx: x == sx + (0 if light else 3), 'wood5' if light else 'wood2')
        leaf.fill_where(lambda x, y: y in (dy0 + 9, dy0 + 19), 'wood2')
        put(cv, leaf, 'wood0')
    for bx in (dx0 - 1, dx1 + 1):
        bolt(cv, bx, dy0 + 3)
        bolt(cv, bx, 54)
    if full:
        # Ящик и мешок у стены снаружи.
        _wh_crate(cv, 1, 54, 6, 6, 'wood')
        _wh_crate(cv, 25, 53, 6, 7, 'teal')
    return cv


# ───────────────────────── регистрация ─────────────────────────
def build(reg: pxl.Registry) -> None:
    for v in range(4):
        reg.add(f'camp_floor_{v}', camp_floor(v), extrude=True, note='настил площадки базы (доски), бесшовно')
    reg.add('camp_edge_t', camp_edge_t(), extrude=True, note='верхний ряд площадки: порог-брус у плит карты')
    reg.add('camp_edge_b', camp_edge_b(), extrude=True, note='нижний ряд площадки: кромка настила и ступенька во двор')
    for v in range(2):
        reg.add(f'camp_ground_{v}', camp_ground(v), extrude=True, note='земля двора под постройками, бесшовно')
    feet3 = (DW / 2.0, 0.0)
    reg.add('depot_base', depot_base(), pivot=feet3, note='приёмный склад 3×2 (столбцы 2–4, st0–st1); воронки над столбцами 2 и 4')
    reg.add('depot_glow', depot_glow(), pivot=feet3, alpha=True, note='свет приёма во время разгрузки (поверх depot_base)')
    reg.add('depot_slot', depot_slot(False), note='место разгрузки свободно: люк в настиле, линзы планки под линией ног погашены; центр клетки')
    reg.add('depot_slot_active', depot_slot(True), note='место разгрузки занято: люк открыт, монеты в жёлобе, линзы планки (строки 27–30) горят')
    reg.add('depot_chute', depot_chute(), extrude=True, note='доп.: жёлоб от люка к складу в нижнем ряду площадки')
    reg.add('depot_chute_active', depot_chute(True), extrude=True,
            note='доп.: жёлоб под занятым местом разгрузки — свет и монеты по дну (вместе с depot_slot_active)')
    feet1 = (T / 2.0, 0.0)
    for f in range(4):
        reg.add(f'depot_crate_{f}', depot_crate(f), pivot=feet1, note=f'ящик с монетами перед складом, заполнение {f}/3')
    feetb = (BW / 2.0, 0.0)
    for t in range(3):
        reg.add(f'hire_board_{t}', hire_board(t), pivot=feetb, note=f'доска найма, уровень оформления {t} (размер команды)')
    for t in range(3):
        reg.add(f'bench_{t}', bench(t), pivot=feetb, note=f'верстак инструментов, уровень оформления {t} (кирки)')
    for t in range(3):
        reg.add(f'rack_{t}', rack(t), pivot=feetb, note=f'стеллаж сумок, уровень оформления {t} (сумки)')
    for t in range(2):
        reg.add(f'cart_{t}', cart(t), pivot=feet1, note=('вагонетка пустая' if t == 0 else 'вагонетка с мешками') + ' (транспорт)')
    reg.add('wardrobe_closed', wardrobe(False), pivot=feetb, note='шкаф экипировки закрыт')
    reg.add('wardrobe_open', wardrobe(True), pivot=feetb, note='шкаф экипировки открыт (окно «Экипировка», смена комплекта)')
    for f in range(2):
        reg.add(f'camp_lantern_{f}', camp_lantern(f), pivot=(8.0, 0.0), note='фонарь на столбике, мерцание 0,3–0,8 с')
    reg.add('camp_sign', camp_sign(), pivot=feet1, note='вывеска лагеря с лапкой и монетой')
    reg.add('camp_tent', camp_tent(), pivot=(32.0, 0.0), note='доп.: палатка 2×1 (декор, если есть место)')
    reg.add('warehouse_empty', warehouse(False), pivot=feetb,
            note=f'склад товаров (столбец baseCol + 4, задний ряд), пусто; вход труб — пиксель {WAREHOUSE_INLET} холста')
    reg.add('warehouse_full', warehouse(True), pivot=feetb, note='склад товаров с товаром на складе')
