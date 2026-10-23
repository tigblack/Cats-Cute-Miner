"""Первая сессия (группа firstsession, Docs/Rework/FIRST_SESSION.md): Mia, её клетка и замок, иконки новых разделов.

  mia_portrait_0/1/2/3 — портрет по грудь 40×40 (как tutor_cat, pivot — центр, обрезан по диску ui_portrait_frame):
      0 — улыбка, 1 — говорит, 2 — моргает, 3 — испугана (клетка). Молодая кошка: белая шерсть, рыжее пятно на левом ухе и
      щеке, большие глаза, жёлтая каска с розовой лентой и цветком, розовый платок, сиреневый комбинезон (как в поле,
      cat_body_mia_*).
  icon_mia — мордочка Mia 16×16 для блока цели HUD и фишек; marker_rescue — метка над клеткой (пузырь с сердечком).
  rescue_cage_back / rescue_cage_front_0…3 — клетка 1×1 слоями (задняя стенка и пол, перед с дверцей, открывание в 4 кадра):
      котик внутри рисуется между слоями; prop_rescuecage_0 — та же клетка закрытой одним спрайтом (запасной вид).
  debris_cagelock_0 (+ _d1, _d2 и след _rest) — замок клетки: доски крест-накрест на стойках, цепь и латунный навесной замок
      (цель добычи на подходе к клетке; по ней бьют котики, а не по Mia).
  icon_sell_goods (+ _big) — ящик, полный товаров заводов (Sell Goods справа на нижней панели); icon_branch_bays — ветка
      Loading Bays (два места разгрузки и «+»); icon_lock_small — маленький замок для угла закрытой кнопки.

Все холсты — палитра pxl, свет сверху-слева, контур 1 px — тёмный тон материала.
"""
from __future__ import annotations

import math
from typing import Dict, Sequence, Set, Tuple

from pxl import C, Canvas

from sprites.dressing import band, board, wreck
from sprites.goods import goods_icon
from sprites.structures import DWOOD, IRON, STONE, WOOD, blank, bolt, put, vbeam
from sprites.tutorial import BH, BW, CX, CY, DISC_R, edges, fill, m_disc, m_ellipse, outline, sphere, stamp

OUTLINE = 'ink1'
Px = Tuple[int, int]
Mask = Set[Px]


# ═════════════════════════ Mia: портрет ═════════════════════════
# Ушки сквозь каску: левое рыжее (пятно окраса), правое белое в тени.
EAR_L = [
    'oo....',
    'oGo...',
    'oGpo..',
    'oGppo.',
    'oggppo',
    '.oggGo',
]
EAR_R = [
    '....oo',
    '...owo',
    '..oPwo',
    '.oPPwo',
    'oPPwwo',
    'owwwo.',
]
EAR_L_AT, EAR_R_AT = (8, 3), (26, 3)
LAMP = [
    '.LLLLL.',
    'LMaAaML',
    'LMa*aML',
    'LLaaaLL',
    '.LLLLL.',
]
LAMP_AT = (17, 1)
DOME = (20.0, 13.5, 11.5, 12.0)
BRIM = (20.0, 12.8, 13.6, 2.1)
# Цветок на ленте каски (справа): розовые лепестки, золотая серединка.
FLOWER = [
    '.pp.',
    'pPPp',
    'pPgP',
    '.pp.',
]
FLOWER_AT = (25, 6)


def mia_helmet() -> Canvas:
    cv = Canvas(BW, BH)
    dome = {(x, y) for (x, y) in m_ellipse(*DOME) if y <= 12}
    dcx, dcy, drx, dry = DOME
    sphere(cv, dome, dcx - 2, dcy, drx + 0.5, dry + 0.5, ['gold1', 'gold2', 'gold3', 'gold4', 'gold5'], [0.0, 0.30, 0.70, 0.93])
    brim = m_ellipse(*BRIM) - dome
    for x, y in brim:
        top = (x, y - 1) not in brim and (x, y - 1) not in dome
        cv.set(x, y, 'gold2' if y >= 14 else 'gold4' if top else 'gold3')
    for (x, y), d in edges(brim).items():
        if 'r' in d:
            cv.set(x, y, 'gold2')
    full = dome | brim
    # Розовая лента вокруг купола.
    for x, y in dome:
        if y in (9, 10):
            cv.set(x, y, 'pink3' if y == 9 else 'pink2')
    fill(cv, outline(full), 'gold0')
    for rows, (ex, ey) in ((EAR_L, EAR_L_AT), (EAR_R, EAR_R_AT)):
        stamp(cv, rows, {'o': 'fur_ginger0', 'G': 'fur_ginger3', 'g': 'fur_ginger2', 'p': 'pink2', 'P': 'pink1',
                         'w': 'fur_white3'} if rows is EAR_L else
              {'o': 'fur_white0', 'w': 'fur_white2', 'p': 'pink2', 'P': 'pink1'}, ex, ey)
    stamp(cv, LAMP, {'L': 'navy1', 'M': 'navy3', 'a': 'amber4', 'A': 'amber5', '*': 'white'}, *LAMP_AT)
    stamp(cv, FLOWER, {'p': 'pink2', 'P': 'pink3', 'g': 'gold4'}, *FLOWER_AT)
    fm = {(FLOWER_AT[0] + i, FLOWER_AT[1] + j) for j, r in enumerate(FLOWER) for i, ch in enumerate(r) if ch != '.'}
    for q in outline(fm):
        if not cv.opaque(*q) or cv.get(*q)[:3] in (C('gold3')[:3], C('gold4')[:3], C('gold5')[:3], C('gold2')[:3]):
            cv.set(q[0], q[1], 'pink0')
    return cv


F = {'o': 'fur_white0', 'd': 'fur_white1', 'm': 'fur_white2', 'f': 'fur_white3', 'l': 'fur_white4',
     'G': 'fur_ginger3', 'g': 'fur_ginger2', 'h': 'fur_ginger1',
     'e': 'ink1', 'E': 'ink2', 'c': 'cream4', 'n': 'pink2', 'N': 'pink3', 'q': 'ink2', 'r': 'red2', 't': 'pink2',
     'k': 'pink3', 'b': 'fur_white1'}
# Большие глаза (4×5) с двумя бликами и ресничкой наружу.
EYES = {
    'open': ['.ee.', 'ecee', 'eeee', 'eece', '.ee.'],
    'blink': ['....', '....', '....', 'eeee', '.ee.'],
    'worried': ['.ee.', 'ecee', 'eeee', 'eece', '.ee.'],
}
EYE_L, EYE_R = (12, 16), (24, 16)
LASH_L, LASH_R = [(11, 16), (10, 15)], [(28, 16), (29, 15)]
BROWS_WORRIED = [((12, 13), (15, 14)), ((27, 13), (24, 14))]
NOSE = ['nNn', '.n.']
NOSE_AT = (19, 21)
MOUTH_AT = (18, 23)
MOUTHS = {
    'smile': ['q...q', '.q.q.', '..q..'],
    'talk': ['.qqq.', 'qrttq', '.qqq.'],
    'worried': ['.....', '.qqq.', 'q...q'],
}
WHISKERS = [((8, 22), (2, 21)), ((8, 24), (3, 26)), ((32, 22), (38, 21)), ((32, 24), (37, 26))]


def mia_head(expr: str) -> Canvas:
    cv = Canvas(BW, BH)
    face = m_ellipse(20.5, 20.5, 12.5, 8.5)
    fill(cv, face, 'fur_white3')
    for (x, y), d in edges(face).items():
        if 'l' in d or ('u' in d and x < 18):
            cv.set(x, y, 'fur_white4')
        elif 'r' in d or 'd' in d:
            cv.set(x, y, 'fur_white1')
    # Рыжее пятно: левая сторона лба и щека под ухом.
    patch = {(x, y) for (x, y) in face if (x - 10) ** 2 / 30.0 + (y - 15) ** 2 / 14.0 <= 1.0}
    for x, y in patch:
        cv.set(x, y, 'fur_ginger3' if (x, y - 1) not in face or x < 11 else 'fur_ginger2')
    # Тень от поля каски.
    for x, y in face:
        if y == 13:
            cv.set(x, y, 'fur_white1' if (x, y) not in patch else 'fur_ginger1')
    # Румянец.
    for x, y in ((10, 21), (11, 21), (29, 21), (30, 21)):
        cv.set(x, y, 'pink3')
    fill(cv, outline(face), 'fur_white0')
    # Глаза, реснички, брови.
    eyes = 'blink' if expr == 'blink' else ('worried' if expr == 'worried' else 'open')
    stamp(cv, EYES[eyes], F, *EYE_L)
    stamp(cv, EYES[eyes], F, *EYE_R)
    if eyes != 'blink':
        for x, y in LASH_L + LASH_R:
            cv.set(x, y, 'ink1')
    if expr == 'worried':
        for (x0, y0), (x1, y1) in BROWS_WORRIED:
            cv.line(x0, y0, x1, y1, 'fur_ginger1' if x0 < 20 else 'fur_white0')
        cv.set(30, 19, 'blue5')
        cv.set(30, 20, 'blue6')
    stamp(cv, NOSE, F, *NOSE_AT)
    mouth = 'talk' if expr == 'talk' else ('worried' if expr == 'worried' else 'smile')
    stamp(cv, MOUTHS[mouth], F, *MOUTH_AT)
    for (x0, y0), (x1, y1) in WHISKERS:
        cv.line(x0, y0, x1, y1, 'fur_white2')
    return cv


SCARF = [
    '..xxxxxxxxxxxxxxx..',
    '.xHPPPPPPPPPPPPPpx.',
    'xHPPPPPPPxxxPPPPppx',
    '.xxPPPPPxHZHxPPPpx.',
    '...xxPPPxPHPpxPpx..',
    '.....xPPxxxxPPpx...',
    '......xPPPPPPpx....',
    '.......xPPPPpx.....',
    '........xPPpx......',
    '.........xpx.......',
    '..........x........',
]
SCARF_AT = (11, 28)


def mia_body() -> Canvas:
    cv = Canvas(BW, BH)
    sh = m_ellipse(20.0, 41.5, 18.0, 11.0)
    fill(cv, sh, 'fur_white3')
    for (x, y), d in edges(sh).items():
        cv.set(x, y, 'fur_white4' if x < 20 else 'fur_white1')
    bib = {(x, y) for (x, y) in sh if 13 <= x <= 27 and y >= 35}
    fill(cv, bib, 'purple3')
    for k in range(9):
        for w in range(3):
            cv.set(9 + k // 2 + w, 31 + k, 'purple3' if w else 'purple4')
            cv.set(31 - k // 2 - w, 31 + k, 'purple2' if w else 'purple3')
    fill(cv, outline(sh), 'ink1')
    for x, y in ((14, 36), (26, 36)):
        cv.set(x, y, 'gold4')
        cv.set(x, y + 1, 'gold2')
    stamp(cv, SCARF, {'x': 'pink0', 'P': 'pink2', 'p': 'pink1', 'H': 'pink3', 'Z': 'pink4'}, *SCARF_AT)
    return cv


def mia_portrait(expr: str) -> Canvas:
    cv = Canvas(BW, BH)
    cv.blit(mia_body(), 0, 0)
    cv.blit(mia_head(expr), 0, 0)
    cv.blit(mia_helmet(), 0, 0)
    disc = m_disc()
    for y in range(BH):
        for x in range(BW):
            if (x, y) not in disc:
                cv.set(x, y, None)
    return cv


# ═════════════════════════ Mia: иконка и метка ═════════════════════════
MIA_ICON = [
    'G...........w',
    'GG.........ww',
    'GpG.......wpw',
    'GppGGwwwwwppw',
    'GGGGwwwwwwwwd',
    'GGeewwwwweewd',
    'GgecwwwwwecwD',
    'Gwwwwwnnwwwwd',
    '.kwwwwqqwwwk.',
    '..wwwwwwwww..',
    '...PPPPPPP...',
]


def icon_mia() -> Canvas:
    """Мордочка Mia 16×16: белая с рыжим левым ухом, большие глаза, розовый румянец и платок, цветок у уха."""
    art = Canvas.from_ascii(MIA_ICON, {'G': 'fur_ginger3', 'g': 'fur_ginger2', 'w': 'fur_white3', 'd': 'fur_white1',
                                       'D': 'fur_white0', 'p': 'pink2', 'e': 'ink1', 'c': 'cream4', 'n': 'pink3', 'q': 'ink2',
                                       'k': 'pink3', 'P': 'pink2'})
    cv = Canvas(16, 16)
    cv.blit(art, 1, 3)
    cv.outline(OUTLINE)
    flower = Canvas(16, 16)
    for x, y, c in ((12, 1, 'pink3'), (13, 1, 'pink2'), (11, 2, 'pink2'), (12, 2, 'gold4'), (13, 2, 'pink3'), (12, 3, 'pink2')):
        flower.set(x, y, c)
    flower.outline('pink0')
    cv.blit(flower, 0, 0)
    return cv


def marker_rescue() -> Canvas:
    """Метка цели над клеткой: светлый пузырь с хвостиком вниз и розовым сердечком (помощь нужна здесь)."""
    cv = Canvas(18, 20)
    bub = blank(cv)
    bub.ellipse(9.0, 7.5, 7.5, 6.8, 'cream3')
    bub.polygon([(7, 12), (11, 12), (9, 17)], 'cream3')
    bub.fill_where(lambda x, y: bub.opaque(x, y) and (x + y > 20 or y > 11), 'cream1')
    bub.fill_where(lambda x, y: bub.opaque(x, y) and x + y < 9 and y < 6, 'cream4')
    put(cv, bub, 'ink1')
    heart = ['.HH.HH.', 'HhHHHHH', 'HHHHHHH', '.HHHHH.', '..HHH..', '...H...']
    stamp(cv, heart, {'H': 'pink2', 'h': 'pink4'}, 5, 4)
    for x, y in ((6, 5), (6, 6)):
        cv.set(x, y, 'pink3')
    return cv


# ═════════════════════════ клетка ═════════════════════════
CAGE_W, CAGE_H = 32, 50
BAR_X = (4, 9, 14, 19, 24, 28)


def cage_back() -> Canvas:
    """Пол клетки (каменная плита), задняя стенка из прутьев в тени и крыша-брус: котик садится перед ними."""
    cv = Canvas(CAGE_W, CAGE_H)
    # Каменная плита пола.
    lay = blank(cv)
    lay.rect(1, 40, 30, 9, 'stone3')
    lay.hline(1, 30, 40, 'stone5')
    lay.hline(1, 30, 41, 'stone4')
    lay.hline(1, 30, 48, 'stone2')
    put(cv, lay, 'stone0')
    # Задняя стенка: тёмный проём и прутья в тени.
    cv.rect(3, 12, 26, 28, 'navy1')
    for x in BAR_X:
        cv.vline(x, 12, 39, 'stone1')
        cv.vline(x + 1, 12, 39, 'stone2')
    cv.hline(3, 28, 24, 'stone1')
    # Задний брус крыши.
    lay = blank(cv)
    lay.rect(1, 6, 30, 6, 'wood2')
    lay.hline(1, 30, 6, 'wood4')
    lay.hline(1, 30, 11, 'wood1')
    put(cv, lay, 'wood0')
    # Солома на полу.
    for x, y in ((6, 38), (7, 37), (12, 39), (20, 38), (21, 39), (25, 37)):
        cv.set(x, y, 'gold3')
        cv.set(x + 1, y, 'gold2')
    return cv


def cage_front(open_k: int) -> Canvas:
    """Перед клетки: угловые стойки, нижняя и верхняя обвязка с заклёпками, крыша; дверца из прутьев с петлями слева —
    open_k 0 закрыта, 1–3 открывается (поворот к зрителю), 3 — распахнута влево, проём свободен."""
    cv = Canvas(CAGE_W, CAGE_H)
    # Крыша: толстый брус с бликом и болтами, над ним козырёк.
    lay = blank(cv)
    lay.polygon([(0, 4), (31, 4), (29, 0), (2, 0)], 'wood4')
    lay.hline(2, 29, 0, 'wood6')
    put(cv, lay, 'wood0')
    band(cv, 0, 4, 32, (3, 15, 27), IRON)
    # Угловые стойки.
    for x in (0, 28):
        vbeam(cv, x, 7, 4, 37, IRON, cap=False)
    # Нижняя обвязка.
    band(cv, 0, 42, 32, (3, 15, 27), IRON)
    door_x0, door_x1, door_y0, door_y1 = 4, 27, 8, 41
    if open_k == 0:
        bars = BAR_X[1:5]
        for x in bars:
            cv.vline(x, door_y0, door_y1, 'stone4')
            cv.vline(x + 1, door_y0, door_y1, 'stone3')
            cv.set(x, door_y0, 'stone6')
        cv.hline(door_x0, door_x1, 24, 'stone3')
        cv.hline(door_x0, door_x1, 25, 'stone2')
        # Петли слева, ручка справа.
        for y in (12, 34):
            cv.rect(3, y, 3, 2, 'stone5')
            cv.set(3, y, 'stone6')
        cv.rect(24, 22, 3, 5, 'amber2')
        cv.set(24, 22, 'amber4')
    else:
        # Дверца поворачивается на петлях слева: видимая ширина уменьшается, прутья сближаются.
        width = {1: 15, 2: 8, 3: 4}[open_k]
        x_end = door_x0 + width
        n = 4 if open_k < 3 else 2
        for i in range(n):
            x = door_x0 + 1 + round(i * (width - 2) / max(1, n - 1))
            cv.vline(x, door_y0 - (open_k > 1), door_y1 + (open_k > 1), 'stone4')
        cv.hline(door_x0, x_end, 24, 'stone3')
        cv.hline(door_x0, x_end, door_y0, 'stone4')
        cv.hline(door_x0, x_end, door_y1, 'stone2')
        for y in (12, 34):
            cv.rect(3, y, 3, 2, 'stone5')
    return cv


def cage_closed() -> Canvas:
    cv = cage_back()
    cv.blit(cage_front(0), 0, 0)
    return cv


# ═════════════════════════ замок клетки (цель) ═════════════════════════
def cage_lock() -> Canvas:
    """Заблокированная дверь: две стойки, доски крест-накрест, тяжёлая цепь восьмёркой и латунный навесной замок по центру."""
    cv = Canvas(32, 40)
    for x in (2, 25):
        vbeam(cv, x, 6, 5, 32, DWOOD, seed=x)
    board(cv, 3, 12, 29, 32, 4, WOOD, seed=11, nail=0.6)
    board(cv, 3, 32, 29, 12, 4, DWOOD, seed=12, nail=0.6)
    # Цепь: звенья вдоль диагоналей.
    links = [(6, 15), (9, 18), (12, 21), (19, 21), (22, 18), (25, 15), (8, 28), (11, 25), (21, 25), (24, 28)]
    for i, (x, y) in enumerate(links):
        lay = blank(cv)
        if i % 2 == 0:
            lay.rect(x - 1, y - 1, 3, 2, 'stone5')
            lay.set(x, y - 1, 'stone6')
        else:
            lay.rect(x - 1, y - 1, 2, 3, 'stone4')
            lay.set(x - 1, y - 1, 'stone6')
        put(cv, lay, 'stone1')
    # Навесной замок: дужка и корпус.
    lay = blank(cv)
    lay.ellipse(16.0, 19.0, 4.0, 4.0, 'stone4')
    lay.ellipse(16.0, 19.0, 2.2, 2.2, None)
    lay.fill_where(lambda x, y: y > 20, None)
    put(cv, lay, 'stone0')
    body = blank(cv)
    body.rect(10, 21, 12, 10, 'gold3')
    body.hline(10, 21, 21, 'gold5')
    body.vline(10, 21, 30, 'gold4')
    body.hline(10, 21, 30, 'gold1')
    body.vline(21, 22, 30, 'gold2')
    put(cv, body, 'gold0')
    cv.rect(15, 24, 2, 3, 'ink1')
    cv.set(15, 27, 'ink1')
    cv.set(16, 27, 'ink1')
    cv.set(12, 22, 'gold5')
    # Каменная плита у подножия.
    lay = blank(cv)
    lay.rect(1, 36, 30, 4, 'stone3')
    lay.hline(1, 30, 36, 'stone5')
    put(cv, lay, 'stone0')
    return cv


def cage_lock_rest() -> Canvas:
    """След на полу после взлома: обломок доски, звенья цепи и раскрытый замок."""
    cv = Canvas(32, 32)
    board(cv, 4, 23, 17, 21, 3, WOOD, split1=True, seed=81, th=1)
    for x, y in ((19, 26), (22, 25), (25, 27)):
        lay = blank(cv)
        lay.rect(x - 1, y - 1, 3, 2, 'stone5')
        put(cv, lay, 'stone1')
    lay = blank(cv)
    lay.rect(9, 26, 7, 5, 'gold3')
    lay.hline(9, 15, 26, 'gold5')
    put(cv, lay, 'gold0')
    cv.line(10, 25, 11, 22, 'stone4')
    cv.line(11, 22, 13, 21, 'stone4')
    return cv


# ═════════════════════════ иконки разделов ═════════════════════════
def _crate(cv: Canvas, x: int, y: int, w: int, h: int) -> None:
    """Открытый деревянный ящик (вид чуть сверху): борт, доски фасада, тень внутри."""
    lay = blank(cv)
    lay.rect(x, y, w, h, 'wood4')
    lay.hline(x, x + w - 1, y, 'wood6')
    lay.hline(x, x + w - 1, y + 1, 'wood5')
    for yy in range(y + 3, y + h - 1, 3):
        lay.hline(x + 1, x + w - 2, yy, 'wood3')
    lay.vline(x, y + 1, y + h - 1, 'wood5')
    lay.vline(x + w - 1, y + 1, y + h - 1, 'wood2')
    lay.hline(x, x + w - 1, y + h - 1, 'wood2')
    put(cv, lay, 'wood0')
    cv.set(x + 1, y + h - 2, 'stone5')
    cv.set(x + w - 2, y + h - 2, 'stone5')


def icon_sell_goods(big: bool = False) -> Canvas:
    """Sell Goods: ящик, полный товаров заводов — бутылка молока, рыба, клубок пряжи и брёвнышко торчат над бортом."""
    if not big:
        cv = Canvas(22, 20)
        goods = [('milk', 2, 1), ('fish', 12, 0), ('wood', 10, 4), ('yarn', 5, 5)]
        for g, gx, gy in goods:
            cv.blit(goods_icon(g, 8), gx, gy)
        _crate(cv, 1, 10, 20, 10)
        return cv
    cv = Canvas(26, 24)
    goods = [('wood', 14, 4), ('milk', 3, 1), ('fish', 16, 0), ('yarn', 8, 5), ('catfood', 2, 6)]
    for g, gx, gy in goods:
        cv.blit(goods_icon(g, 8), gx, gy)
    _crate(cv, 1, 12, 24, 12)
    return cv


def icon_branch_bays() -> Canvas:
    """Ветка Loading Bays: два люка склада рядом (места разгрузки) и зелёный «+» — ещё одно место."""
    cv = Canvas(16, 16)
    for x in (0, 7):
        lay = blank(cv)
        lay.rect(x + 1, 6, 7, 8, 'wood3')
        lay.rect(x + 2, 7, 5, 5, 'navy1')
        lay.hline(x + 1, x + 7, 6, 'wood5')
        lay.hline(x + 1, x + 7, 13, 'wood2')
        put(cv, lay, OUTLINE)
        cv.rect(x + 3, 8, 3, 3, 'gold3')
        cv.set(x + 3, 8, 'gold5')
    plus = blank(cv)
    plus.rect(11, 0, 2, 6, 'green5')
    plus.rect(9, 2, 6, 2, 'green5')
    plus.set(11, 0, 'green6')
    put(cv, plus, 'green1')
    return cv


def icon_lock_small() -> Canvas:
    """Маленький замок 9×10 для угла закрытой кнопки: железная дужка и латунный корпус."""
    cv = Canvas(9, 10)
    lay = blank(cv)
    lay.rect(2, 0, 5, 1, 'stone5')
    lay.rect(2, 0, 1, 4, 'stone5')
    lay.rect(6, 0, 1, 4, 'stone4')
    lay.rect(1, 4, 7, 6, 'gold3')
    lay.hline(1, 7, 4, 'gold5')
    lay.hline(1, 7, 9, 'gold1')
    put(cv, lay, OUTLINE)
    cv.set(4, 6, 'ink1')
    cv.set(4, 7, 'ink1')
    return cv


def build(reg) -> None:
    for i, expr in enumerate(('smile', 'talk', 'blink', 'worried')):
        reg.add(f'mia_portrait_{i}', mia_portrait(expr), note='портрет Mia 40×40 (рамка ui_portrait_frame)')
    reg.add('icon_mia', icon_mia(), note='мордочка Mia 16×16')
    reg.add('marker_rescue', marker_rescue(), pivot=(9.0, 0.0), note='метка цели над клеткой')
    feet = (CAGE_W / 2.0, 0.0)
    reg.add('rescue_cage_back', cage_back(), pivot=feet, note='клетка: пол и задняя стенка')
    for k in range(4):
        reg.add(f'rescue_cage_front_{k}', cage_front(k), pivot=feet, note=f'клетка: перед с дверцей, кадр {k}')
    reg.add('prop_rescuecage_0', cage_closed(), pivot=feet, note='клетка закрытая (запасной вид препятствия)')
    lock = cage_lock()
    reg.add('debris_cagelock_0', lock, pivot=(lock.w / 2.0, 0.0), note='замок клетки (цель)')
    for level in (1, 2):
        reg.add(f'debris_cagelock_0_d{level}', wreck(lock, level, 9100 + level), pivot=(lock.w / 2.0, 0.0), note=f'замок: стадия {level}')
    rest = cage_lock_rest()
    reg.add('debris_cagelock_rest', rest, pivot=(rest.w / 2.0, 0.0), note='след взломанного замка')
    reg.add('icon_sell_goods', icon_sell_goods(False), note='Sell Goods: ящик с товарами')
    reg.add('icon_sell_goods_big', icon_sell_goods(True), note='Sell Goods: ящик с товарами, крупно')
    reg.add('icon_branch_bays', icon_branch_bays(), note='ветка Loading Bays')
    reg.add('icon_lock_small', icon_lock_small(), note='маленький замок для угла кнопки')
