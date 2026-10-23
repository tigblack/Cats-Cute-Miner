"""Заводы ТЗ 2.2 (§4, §5, §11): здание завода 2×3 на месте месторождения s, кадры работы, цепи, детали труб.

Холст 64×112, ноги (32, 0) — как у shaft_base с колесом shaft_wheel_*: тот же силуэт копра (колесо на А-образной раме
сверху, кровля, фасад, двор нижней клетки), но вместо штольни — цех. Сверху вниз:
  y 0…23   колесо-маховик (стальной обод с зубцами, 4 спицы, золотая ступица) на раме и труба цеха справа;
  y 24…41  вывеска с иконкой товара goods_<good> 16×16 (главный признак завода вместе с цветом кровли);
  y 26…36  кровля (материал и цвет — по заводу);
  y 36…80  фасад: стены по заводу, два светлых окна, ворота цеха посередине, цоколь; земля — y 80;
  y 80…111 двор: конвейер из ворот в приёмный ящик, слева поддон с товаром (goods_<good>_8), справа — свой реквизит.

Состояния (ТЗ §4.3) собирает код вида (WP5) из слоёв одного холста и одного pivot:
  Locked  — factory_<good>, затемнённый SpriteRenderer.color (≈ 0,45 / 0,45 / 0,55), поверх factory_chains и
            трещины factory_chains_crack_1…3 по доле HP цепей (< 0,75 / < 0,45 / < 0,2), как shaft_crack_*;
  Working — factory_<good> + наложение factory_<good>_work_0…3 (0,15 с/кадр): колесо крутится, дым из трубы,
            конвейер везёт ящики; наложение непрозрачно закрывает колесо и ленту здания;
  Ready / Stopped — factory_<good> без наложения (колесо стоит на кадре 0).
Наложение работы одинаково у заводов с одним видом дыма: сборка атласа хранит одинаковые кадры один раз.

Детали труб (сами трубы строит код по маршруту): fpipe_cap (заглушка у завода, поверх — goods_<good>_8 по центру),
fpipe_coupling (муфта жгута), fpipe_inlet (вход на склад), fpipe_capsule / fpipe_capsule_h (партия в пути; белые тона,
код красит цветом полосы товара — PixelPalette.ByName[Color]).

Свет сверху-слева, контур 1 px — тёмный тон материала или ink1. Камня пещеры в здании нет: заводы не перекрашиваются
темами окружения (sprites/themes.py), как постройки лагеря.
"""
from __future__ import annotations

import math
import random
from typing import Callable, Dict, List, Optional, Sequence, Tuple

from pxl import Canvas

from manifest import FACTORY_WORK_FRAMES, GOODS
from sprites import goods as goods_art
from sprites.fx import _lobes
from sprites.structures import (DWOOD, IRON, WOOD, blank, bolt, brace, clip, grain_v, hbeam, lantern, masonry, put, remap,
                                vbeam)

FW, FH = 64, 112
PIVOT = (FW / 2.0, 0.0)
WHEEL_C = (32.0, 12.0)        # центр колеса (как у копра shaft_wheel_*)
WHEEL_R = 11
ROOF_PTS = [(4, 36), (9, 26), (55, 26), (60, 36)]
WALL = (5, 36, 54, 44)        # фасад: x, y, w, h (до линии земли)
GROUND = 80                   # линия земли у фасада
DOOR = (23, 50, 18, 30)       # ворота цеха: x, y, w, h (низ — на земле)
WINDOWS = ((9, 44, 10, 10), (45, 44, 10, 10))
SIGN = (21, 24, 22, 18)       # вывеска: x, y, w, h; иконка 16×16 — в (24, 25)
CHIMNEY = (48, 15, 7, 17)     # труба цеха: x, y верха, ширина, высота (над ней 15 px холста под дым)
SMOKE_AT = (51.5, 13.0)       # устье трубы
BELT = (26, 66, 12, 29)       # конвейер: x, y (в воротах), ширина, длина до ролика над ящиком
BIN = (22, 97, 20, 13)        # приёмный ящик в конце конвейера: x, y, w, h
BELT_STEP = 3                 # сдвиг ленты за кадр (px); шаг ящиков 12 = 4 кадра
BELT_PITCH = 12


def _tones(ramp: str, idx: Sequence[int]) -> Dict[str, str]:
    """Материал из рампы: o — контур, s, d — тени, b — основа, l, h — свет."""
    keys = ('o', 's', 'd', 'b', 'l', 'h')
    return {k: f'{ramp}{i}' for k, i in zip(keys, idx)}


# ───────────────────────── материалы ─────────────────────────
ROOFS = {
    'red': _tones('red', (0, 1, 2, 3, 4, 5)),
    'blue': _tones('blue', (0, 1, 2, 3, 4, 5)),
    'green': _tones('green', (0, 1, 2, 3, 4, 5)),
    'teal': _tones('teal', (0, 1, 2, 3, 4, 5)),
    'purple': _tones('purple', (0, 1, 2, 3, 4, 5)),
    'amber': _tones('amber', (0, 0, 1, 2, 3, 4)),
    'gold': _tones('gold', (0, 0, 1, 2, 3, 4)),
    'stone': _tones('stone', (0, 1, 2, 3, 4, 5)),
    'navy': _tones('navy', (0, 1, 2, 3, 4, 5)),
    'copper': _tones('fur_ginger', (0, 0, 1, 2, 3, 4)),
    'pink': _tones('pink', (0, 0, 1, 2, 3, 4)),
}
WALLS = {
    'wood': _tones('wood', (0, 2, 3, 4, 5, 6)),
    'dwood': _tones('wood', (0, 1, 2, 3, 4, 5)),
    'cardboard': _tones('fur_cream', (0, 0, 1, 2, 3, 4)),
    'navy': _tones('navy', (0, 3, 4, 5, 6, 7)),
    'blue': _tones('blue', (0, 2, 3, 4, 5, 6)),
    'brick': _tones('red', (0, 1, 2, 3, 4, 5)),
    'dbrick': _tones('red', (0, 0, 1, 2, 3, 4)),
    'stone': _tones('stone', (0, 2, 3, 4, 5, 6)),
    'steel': _tones('stone', (0, 2, 3, 4, 5, 6)),
    'bluesteel': _tones('navy', (0, 3, 4, 5, 6, 7)),
    'cream': _tones('cream', (0, 0, 0, 1, 2, 3)),
    'pink': _tones('pink', (0, 1, 2, 3, 4, 4)),
    'lilac': _tones('purple', (1, 3, 4, 5, 6, 6)),
    'amberwood': _tones('fur_cream', (0, 0, 1, 2, 3, 4)),
}
FRAME = WOOD                  # рама окон и ворот

# Вид дыма наложения работы: пар (светлый), дым (серый), копоть (тёмная) — (блик, свет, основа, тень).
SMOKES = {
    'steam': ('cream4', 'cream3', 'cream2', 'fur_white1'),
    'smoke': ('stone7', 'stone6', 'stone5', 'stone4'),
    'soot': ('stone5', 'stone4', 'stone3', 'stone2'),
}


# Завод: стены, кровля и её рисунок, дым, «горячий» цех (огонь в воротах и окнах), материал трубы, реквизит справа.
FACTORIES: Dict[str, Dict[str, object]] = {
    'milk': dict(wall='cream', wall_kind='timber', roof='blue', roof_kind='boards', smoke='steam', chimney='steel', prop='cans'),
    'wood': dict(wall='wood', wall_kind='planks', roof='green', roof_kind='boards', smoke='steam', chimney='steel', prop='logs'),
    'fish': dict(wall='navy', wall_kind='planks', roof='teal', roof_kind='tin', smoke='steam', chimney='steel', prop='net'),
    'yarn': dict(wall='pink', wall_kind='timber', roof='purple', roof_kind='tiles', smoke='steam', chimney='brick', prop='basket'),
    'catfood': dict(wall='brick', wall_kind='bricks', roof='amber', roof_kind='tin', smoke='smoke', chimney='brick', prop='sacks'),
    'boxes': dict(wall='cardboard', wall_kind='planks', roof='red', roof_kind='boards', smoke='steam', chimney='steel', prop='flatboxes'),
    'bricks': dict(wall='brick', wall_kind='bricks', roof='stone', roof_kind='tiles', smoke='soot', chimney='brick', prop='kiln', hot=True),
    'steel': dict(wall='steel', wall_kind='metal', roof='red', roof_kind='tin', smoke='soot', chimney='steel', prop='beams', hot=True, frame='iron'),
    'glass': dict(wall='stone', wall_kind='stones', roof='teal', roof_kind='tin', smoke='smoke', chimney='brick', prop='bottles', hot=True),
    'catnip': dict(wall='wood', wall_kind='greenhouse', roof='green', roof_kind='glass', smoke='steam', chimney='steel', prop='pots'),
    'toys': dict(wall='blue', wall_kind='planks', roof='red', roof_kind='tiles', smoke='steam', chimney='brick', prop='toybox'),
    'scratchers': dict(wall='dwood', wall_kind='planks', roof='copper', roof_kind='boards', smoke='steam', chimney='steel', prop='posts'),
    'cushions': dict(wall='lilac', wall_kind='timber', roof='pink', roof_kind='tiles', smoke='steam', chimney='brick', prop='pillows'),
    'bells': dict(wall='stone', wall_kind='stones', roof='gold', roof_kind='tiles', smoke='soot', chimney='brick', prop='bellpost', hot=True),
    'collars': dict(wall='wood', wall_kind='planks', roof='navy', roof_kind='tiles', smoke='steam', chimney='steel', prop='collarrack'),
    'gears': dict(wall='steel', wall_kind='metal', roof='copper', roof_kind='tin', smoke='smoke', chimney='steel', prop='biggear', frame='iron'),
    'lanterns': dict(wall='dbrick', wall_kind='bricks', roof='amber', roof_kind='tiles', smoke='smoke', chimney='brick', prop='lamppost'),
    'honey': dict(wall='amberwood', wall_kind='planks', roof='green', roof_kind='boards', smoke='steam', chimney='steel', prop='hives'),
    'silk': dict(wall='cream', wall_kind='timber', roof='purple', roof_kind='tiles', smoke='steam', chimney='brick', prop='spools'),
    'caviar': dict(wall='bluesteel', wall_kind='metal', roof='red', roof_kind='tin', smoke='steam', chimney='steel', prop='barrels', frame='iron'),
}


def _set_if(cv: Canvas, x: int, y: int, c: str) -> None:
    if cv.opaque(x, y):
        cv.set(x, y, c)


# ───────────────────────── колесо-маховик ─────────────────────────
WHEEL_TEETH = 16              # период зубцов 22,5° = шаг кадра: зубцы «стоят», вращение читается по спицам


def wheel_layer(frame: int) -> Canvas:
    """Колесо завода: стальной обод с 16 зубцами, тёмное поле, 4 светлые спицы, золотая ступица;
    поворот на 22,5° за кадр (4 кадра — четверть оборота, петля бесшовная)."""
    cv = Canvas(FW, FH)
    cx, cy = WHEEL_C
    r = WHEEL_R
    ang0 = math.radians(22.5 * frame)
    lay = blank(cv)
    for y in range(0, 26):
        for x in range(16, 48):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            a = math.atan2(dy, dx) - ang0
            tooth = math.cos(WHEEL_TEETH * a) > 0.0
            if d > r or (d > r - 1.2 and not tooth):
                continue
            lum = -(dx * 0.6 + dy * 0.8) / max(d, 0.01)
            if d > r - 3.2:
                lay.set(x, y, 'stone6' if lum > 0.5 else ('stone3' if lum < -0.45 else 'stone4'))
                continue
            lay.set(x, y, 'navy1')
            for k in range(2):
                sa = ang0 + k * math.pi / 2
                perp = abs(dx * math.sin(sa) - dy * math.cos(sa))
                if perp <= 1.0:
                    lay.set(x, y, 'stone5' if dx * 0.6 + dy * 0.8 < 0 else 'stone4')
    lay.rect(int(cx) - 2, int(cy) - 2, 4, 4, 'gold3')
    lay.set(int(cx) - 2, int(cy) - 2, 'gold5')
    lay.set(int(cx) - 1, int(cy) - 2, 'gold4')
    lay.set(int(cx) - 2, int(cy) - 1, 'gold4')
    lay.set(int(cx) + 1, int(cy) + 1, 'gold1')
    put(cv, lay, 'ink1')
    return cv


# ───────────────────────── конвейер ─────────────────────────
def belt_layer(frame: int, opaque_bin: bool = False) -> Canvas:
    """Лента конвейера от ворот цеха к приёмному ящику: резиновое полотно с поперечными рёбрами и маленькие ящики
    товара на нём; кадр сдвигает ленту на BELT_STEP px к зрителю. В воротах лента в тени."""
    cv = Canvas(FW, FH)
    bx, by, bw, bl = BELT
    y_end = BIN[1] - 2                 # ролик ленты — над приёмным ящиком, наложение работы ящик не перекрывает
    shift = (frame * BELT_STEP) % BELT_PITCH
    lay = blank(cv)
    # Боковины: стальные уголки, левый на свету.
    for y in range(by, y_end + 1):
        lay.set(bx - 1, y, 'stone5')
        lay.set(bx, y, 'stone3')
        lay.set(bx + bw - 1, y, 'stone2')
        lay.set(bx + bw, y, 'stone1')
    # Полотно и рёбра (шаг 6 px — двигаются вместе с ящиками).
    for y in range(by, y_end + 1):
        rib = (y - by - shift) % 6 == 0
        for x in range(bx + 1, bx + bw - 1):
            dark = y < GROUND - 4
            c = ('ink2' if dark else 'navy2') if not rib else ('ink3' if dark else 'navy3')
            lay.set(x, y, c)
        lay.set(bx + 1, y, 'ink3' if y < GROUND - 4 else 'navy3')
    # Ящики товара на ленте.
    for j in range(-1, 4):
        top = by + 2 + shift + j * BELT_PITCH
        crate = [(0, 'wood5'), (1, 'wood4'), (2, 'wood4'), (3, 'wood3'), (4, 'wood2')]
        for dy, c in crate:
            y = top + dy
            if y < by + 1 or y >= y_end:
                continue
            dim = y < GROUND - 4
            for x in range(bx + 3, bx + bw - 3):
                cc = c if not dim else ('wood2' if c in ('wood5', 'wood4') else 'wood1')
                lay.set(x, y, cc)
            lay.set(bx + 2, y, 'wood1')
            lay.set(bx + bw - 3, y, 'wood1')
        if by + 1 <= top - 1 < y_end:
            for x in range(bx + 3, bx + bw - 3):
                lay.set(x, top - 1, 'wood1')
    cv.blit(lay, 0, 0)
    # Ролик у ящика (торец ленты).
    cv.hline(bx - 1, bx + bw, y_end, 'stone4')
    cv.hline(bx - 1, bx + bw, y_end + 1, 'stone2')
    cv.set(bx - 1, y_end, 'stone6')
    return cv


def bin_front(cv: Canvas) -> None:
    """Приёмный ящик в конце конвейера: открытый деревянный короб с железными уголками (передняя стенка поверх ленты)."""
    x, y, w, h = BIN
    box = blank(cv)
    box.rect(x, y, w, h, 'wood4')
    box.hline(x, x + w - 1, y, 'wood6')
    box.hline(x + 1, x + w - 2, y + 1, 'wood5')
    box.hline(x, x + w - 1, y + 6, 'wood2')
    box.hline(x, x + w - 1, y + 7, 'wood5')
    box.hline(x, x + w - 1, y + h - 1, 'wood2')
    box.vline(x, y, y + h - 1, 'wood5')
    box.vline(x + w - 1, y + 1, y + h - 1, 'wood2')
    for cx, cy in ((x, y), (x + w - 2, y), (x, y + h - 2), (x + w - 2, y + h - 2)):
        box.rect(cx, cy, 2, 2, 'stone3')
        box.set(cx, cy, 'stone5')
    put(cv, box, 'wood0')
    # Табличка с лапкой на ящике.
    cv.rect(x + w // 2 - 3, y + 2, 6, 3, 'cream2')
    cv.hline(x + w // 2 - 3, x + w // 2 + 2, y + 2, 'cream3')
    cv.set(x + w // 2 - 1, y + 3, 'wood2')
    cv.set(x + w // 2, y + 3, 'wood2')


# ───────────────────────── дым ─────────────────────────
SMOKE_LIFE = 12               # кадров жизни клуба; 3 клуба со сдвигом 4 кадра — петля из 4 кадров


def smoke_layer(frame: int, style: str) -> Canvas:
    """Дым из трубы: три клуба разного возраста (frame + 4k из SMOKE_LIFE) поднимаются и сносятся вправо, растут,
    старший распадается на клочки. Петля бесшовная: в кадре 0 старший клуб исчезает, у устья рождается новый."""
    hi, lt, base, dk = SMOKES[style]
    cv = Canvas(FW, FH)
    sx, sy = SMOKE_AT
    for k in range(3):
        u = (frame + 4 * k) / float(SMOKE_LIFE)          # 0 … 11/12
        cx = sx + 6.0 * u ** 1.3
        cy = sy - 10.5 * u ** 0.7
        r = 2.0 + 3.3 * u
        lay = Canvas(FW, FH)
        if u < 0.7:
            _lobes(lay, [(cx - r * 0.5, cy + r * 0.2, r * 0.72), (cx + r * 0.5, cy + r * 0.2, r * 0.7),
                         (cx, cy - r * 0.2, r * 0.85)], lt, base, dk, hi)
        else:
            for ddx, ddy, rr in ((-r * 0.7, 0.5, r * 0.45), (r * 0.65, -0.2, r * 0.42), (0.0, -r * 0.7, r * 0.36)):
                _lobes(lay, [(cx + ddx, cy + ddy, rr)], lt, base, dk)
        cv.blit(lay, 0, 0)
    clip(cv, lambda x, y: y < CHIMNEY[1])
    return cv


# ───────────────────────── части здания ─────────────────────────
def aframe(cv: Canvas, iron: bool) -> None:
    """А-образная рама колеса: ноги от краёв кровли к оси, перекладина, опора оси (как у копра)."""
    m = IRON if iron else DWOOD
    brace(cv, 10, 26, 29, 9, 3, m)
    brace(cv, 53, 26, 34, 9, 3, m)
    hbeam(cv, 15, 18, 34, 4, m, seed=7)
    if iron:
        for x in (19, 26, 37, 44):
            cv.set(x, 19, 'stone6')
    cv.rect(29, 8, 6, 6, 'stone0')
    cv.rect(30, 9, 4, 4, 'stone3')
    cv.set(30, 9, 'stone5')
    # Приводной ремень от ступицы вниз к цеху (за вывеской).
    cv.vline(int(WHEEL_C[0] + WHEEL_R - 1), int(WHEEL_C[1]) + 1, 25, 'ink2')


def chimney(cv: Canvas, kind: str) -> None:
    """Труба цеха справа: кирпичная (швы, обод) или стальная (кольца-обручи); колпак шире ствола."""
    x, y, w, h = CHIMNEY
    lay = blank(cv)
    if kind == 'brick':
        lay.rect(x, y + 3, w, h - 3, 'red3')
        for yy in range(y + 3, y + h, 3):
            lay.hline(x, x + w - 1, yy, 'red2')
            off = 0 if (yy - y) // 3 % 2 else 3
            for xx in range(x + off, x + w, 6):
                lay.vline(xx, yy + 1, yy + 2, 'red2')
        lay.vline(x, y + 3, y + h - 1, 'red4')
        lay.vline(x + w - 1, y + 3, y + h - 1, 'red1')
        lay.rect(x - 1, y, w + 2, 3, 'stone4')
        lay.hline(x - 1, x + w, y, 'stone6')
        lay.hline(x - 1, x + w, y + 2, 'stone2')
    else:
        lay.rect(x, y + 3, w, h - 3, 'stone4')
        lay.vline(x, y + 3, y + h - 1, 'stone6')
        lay.vline(x + 1, y + 3, y + h - 1, 'stone5')
        lay.vline(x + w - 1, y + 3, y + h - 1, 'stone2')
        lay.vline(x + w - 2, y + 3, y + h - 1, 'stone3')
        for yy in (y + 8, y + 15):
            lay.hline(x, x + w - 1, yy, 'stone2')
            lay.hline(x, x + w - 1, yy + 1, 'stone6')
        lay.rect(x - 1, y, w + 2, 3, 'stone3')
        lay.hline(x - 1, x + w, y, 'stone5')
        lay.hline(x - 1, x + w, y + 2, 'stone1')
    # Тёмное устье.
    lay.hline(x + 1, x + w - 2, y, 'ink2')
    put(cv, lay, 'ink1')


def roof(cv: Canvas, ramp: str, kind: str, seed: int) -> None:
    """Кровля-трапеция над фасадом (как козырёк копра): доски, черепица рядами, гофрированная жесть или стекло."""
    m = ROOFS[ramp]
    lay = blank(cv)
    lay.polygon(ROOF_PTS, m['b'])
    if kind == 'boards':
        for yy in (29, 32):
            lay.hline(5, 59, yy, m['d'])
        for i, xx in enumerate(range(11, 55, 6)):
            lay.vline(xx, 28 if i % 2 else 30, 35, m['d'])
    elif kind == 'tiles':
        for row, yy in enumerate((28, 31, 34)):
            for xx in range(0, FW):
                if (xx + row * 2) % 4 == 0:
                    _set_if(lay, xx, yy, m['d'])
                    _set_if(lay, xx, yy - 1, m['l'])
            lay.fill_where(lambda x, y, yy=yy: y == yy + 1, m['d'])
    elif kind == 'tin':
        for xx in range(5, 60, 3):
            for yy in range(26, 37):
                _set_if(lay, xx, yy, m['d'])
                _set_if(lay, xx + 1, yy, m['l'])
    elif kind == 'glass':
        lay.fill_where(lambda x, y: True, 'teal4')
        for xx in range(8, 58, 8):
            for yy in range(26, 37):
                _set_if(lay, xx, yy, m['d'])
        lay.fill_where(lambda x, y: y == 31, m['d'])
        for x0 in range(10, 56, 8):
            _set_if(lay, x0 + 1, 28, 'teal6')
            _set_if(lay, x0 + 2, 27, 'teal6')
            _set_if(lay, x0, 33, 'teal5')
    lay.hline(9, 54, 27, m['l'])
    lay.hline(9, 54, 26, m['h'])
    remap(lay, {m['b']: m['d'], m['l']: m['b']}, lambda x, y: x >= 55)
    put(cv, lay, m['o'])


def wall(cv: Canvas, tone: str, kind: str, seed: int) -> None:
    """Фасад цеха: доски, фахверк (светлая штукатурка в деревянном каркасе), кирпич, каменная кладка, стальные листы
    с заклёпками или теплица (стёкла в раме); угловые стойки и каменный цоколь."""
    x, y, w, h = WALL
    m = WALLS[tone]
    lay = blank(cv)
    lay.rect(x, y, w, h, m['b'])
    if kind == 'planks':
        for xx in range(x + 2, x + w, 5):
            lay.vline(xx, y, y + h - 1, m['d'])
            lay.vline(xx + 1, y, y + h - 1, m['l'])
        grain_v(lay, x + 3, y + 3, w - 4, h - 4, m['d'], seed, step=5)
    elif kind == 'bricks':
        for row, yy in enumerate(range(y, y + h, 4)):
            lay.hline(x, x + w - 1, yy + 3, m['s'])
            lay.hline(x, x + w - 1, yy, m['l'])
            off = 0 if row % 2 else 4
            for xx in range(x + off, x + w, 8):
                lay.vline(xx, yy, yy + 2, m['s'])
    elif kind == 'stones':
        masonry(lay, x, y, w, h, dict(m, s=m['s'], l=m['l'], h=m['h'], d=m['d']), seed, row_h=6, wmin=7, wmax=12)
    elif kind == 'metal':
        for xx in range(x, x + w, 12):
            lay.vline(xx, y, y + h - 1, m['s'])
            lay.vline(xx + 1, y, y + h - 1, m['l'])
            for yy in range(y + 3, y + h, 6):
                lay.set(xx + 3, yy, m['h'])
                lay.set(xx + 3, yy + 1, m['d'])
        lay.hline(x, x + w - 1, y + h // 2, m['s'])
        lay.hline(x, x + w - 1, y + h // 2 + 1, m['l'])
    elif kind == 'timber':
        # Фахверк: штукатурка, тёмные брусья по контуру панелей и раскосы.
        beams = WALLS['dwood']
        for xx in (x, x + 17, x + 36, x + w - 3):
            lay.rect(xx, y, 3, h, beams['b'])
            lay.vline(xx, y, y + h - 1, beams['l'])
        for yy in (y + 4, y + 22):
            lay.rect(x, yy, w, 2, beams['b'])
            lay.hline(x, x + w - 1, yy, beams['l'])
        for (x0, y0, x1, y1) in ((x + 3, y + 22, x + 17, y + 6), (x + w - 3, y + 22, x + 39, y + 6)):
            n = max(abs(x1 - x0), abs(y1 - y0))
            for k in range(n + 1):
                px = round(x0 + (x1 - x0) * k / n)
                py = round(y0 + (y1 - y0) * k / n)
                lay.set(px, py, beams['b'])
                lay.set(px + 1, py, beams['d'])
    elif kind == 'greenhouse':
        # Теплица: стёкла в деревянной раме, за стеклом — зелень.
        lay.rect(x, y, w, h, 'teal3')
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                if (xx - x) % 9 in (0, 1) or (yy - y) % 11 in (0, 1):
                    lay.set(xx, yy, 'wood4' if (xx - x) % 9 == 0 or (yy - y) % 11 == 0 else 'wood2')
                elif (yy - y) % 11 > 6:
                    lay.set(xx, yy, 'green3' if (xx + yy) % 3 else 'green4')
                elif (xx + yy) % 7 == 0:
                    lay.set(xx, yy, 'teal5')
    lay.hline(x, x + w - 1, y, m['s'])
    lay.hline(x, x + w - 1, y + 1, m['d'])
    put(cv, lay, m['o'])
    # Угловые стойки.
    for i, px in enumerate((3, 55)):
        vbeam(cv, px, 34, 6, GROUND - 34 + 1, WOOD, seed=seed + 20 + i)
    # Цоколь.
    plinth = blank(cv)
    plinth.rect(x - 1, GROUND - 4, w + 2, 5, 'stone3')
    plinth.hline(x - 1, x + w, GROUND - 4, 'stone5')
    plinth.hline(x - 1, x + w, GROUND, 'stone1')
    rnd = random.Random(seed)
    xx = x
    while xx < x + w:
        plinth.vline(xx, GROUND - 3, GROUND - 1, 'stone1')
        xx += rnd.randrange(6, 11)
    put(cv, plinth, 'stone0')


def window(cv: Canvas, x: int, y: int, w: int, h: int, hot: bool, frame: Dict[str, str]) -> None:
    """Окно цеха: рама, переплёт крестом, тёплый (или огненный) свет внутри с бликом сверху-слева."""
    lay = blank(cv)
    glass = ('red3', 'amber2', 'amber3') if hot else ('amber2', 'amber3', 'amber4')
    lay.rect(x, y, w, h, glass[0])
    lay.rect(x + 1, y + 1, w - 2, h - 3, glass[1])
    lay.rect(x + 2, y + 1, w - 5, 3, glass[2])
    lay.vline(x + w // 2, y, y + h - 1, frame['d'])
    lay.hline(x, x + w - 1, y + h // 2, frame['d'])
    put(cv, lay, frame['o'])
    cv.hline(x - 1, x + w, y + h, frame['l'])
    cv.hline(x - 1, x + w, y + h + 1, frame['s'])
    cv.set(x + 1, y + 1, 'cream4' if not hot else 'gold5')


def door(cv: Canvas, hot: bool, frame: Dict[str, str]) -> None:
    """Ворота цеха: проём с тёмным нутром, свет в глубине (у горячих цехов — огонь печи), рама и перемычка."""
    x, y, w, h = DOOR
    lay = blank(cv)
    lay.rect(x, y, w, h, 'ink2')
    lay.rect(x + 3, y + 4, w - 6, h - 4, 'ink1')
    if hot:
        lay.rect(x + 4, y + 6, w - 8, 8, 'red2')
        lay.rect(x + 5, y + 7, w - 10, 6, 'amber2')
        lay.rect(x + 7, y + 8, w - 14, 4, 'amber4')
        lay.hline(x + 8, x + w - 9, y + 9, 'gold5')
    else:
        lay.rect(x + 5, y + 6, w - 10, 7, 'wood1')
        lay.rect(x + 6, y + 7, w - 12, 5, 'amber1')
        lay.hline(x + 7, x + w - 8, y + 8, 'amber2')
    cv.blit(lay, 0, 0)
    fr = blank(cv)
    fr.rect(x - 3, y - 3, w + 6, 4, frame['b'])
    fr.hline(x - 3, x + w + 2, y - 3, frame['h'])
    fr.hline(x - 3, x + w + 2, y, frame['d'])
    for px, light in ((x - 3, True), (x + w, False)):
        fr.rect(px, y, 3, h, frame['b'])
        fr.vline(px, y, y + h - 1, frame['l'] if light else frame['d'])
        fr.vline(px + 2, y, y + h - 1, frame['d'] if light else frame['s'])
    put(cv, fr, frame['o'])
    for bx in (x - 2, x + w + 1):
        bolt(cv, bx, y + 4)
        bolt(cv, bx, y + h - 6)


def sign(cv: Canvas, good: str) -> None:
    """Вывеска на кровле под колесом: светлая доска в раме, иконка товара goods_<good> 16×16."""
    x, y, w, h = SIGN
    lay = blank(cv)
    lay.rect(x, y, w, h, 'wood6')
    lay.hline(x, x + w - 1, y, 'wood7')
    lay.vline(x, y, y + h - 1, 'wood7')
    lay.hline(x + 1, x + w - 1, y + h - 1, 'wood5')
    lay.vline(x + w - 1, y + 1, y + h - 1, 'wood5')
    put(cv, lay, 'wood1')
    for bx, by in ((x + 1, y + 1), (x + w - 2, y + 1), (x + 1, y + h - 2), (x + w - 2, y + h - 2)):
        cv.set(bx, by, 'wood4')
    cv.blit(goods_art.goods_icon(good, 16), x + 3, y + 1)


# ───────────────────────── реквизит двора ─────────────────────────
def pallet(cv: Canvas, good: str) -> None:
    """Слева во дворе: поддон с готовым товаром (три иконки goods_<good>_8 горкой)."""
    lay = blank(cv)
    lay.rect(5, 104, 17, 3, 'wood4')
    lay.hline(5, 21, 104, 'wood6')
    lay.hline(5, 21, 106, 'wood2')
    for xx in (6, 12, 19):
        lay.rect(xx, 107, 2, 2, 'wood3')
    put(cv, lay, 'wood0')
    icon = goods_art.goods_icon(good, 8)
    for ix, iy in ((5, 96), (13, 96), (9, 89)):
        cv.blit(icon, ix, iy)


def _barrel(cv: Canvas, x: int, y: int, wood: Dict[str, str] = WOOD, hoop: str = 'stone3') -> None:
    """Бочка 9×12 (x, y — левый верх)."""
    lay = blank(cv)
    lay.ellipse(x + 4.5, y + 6, 4.5, 6, wood['b'])
    lay.rect(x, y + 2, 9, 8, wood['b'])
    lay.fill_where(lambda px, py: px <= x + 1, wood['l'])
    lay.fill_where(lambda px, py: px >= x + 7, wood['d'])
    for yy in (y + 2, y + 9):
        lay.fill_where(lambda px, py, yy=yy: py == yy, hoop)
    lay.ellipse(x + 4.5, y + 1.0, 3.6, 1.3, wood['h'])
    put(cv, lay, wood['o'])


def _sack(cv: Canvas, x: int, y: int, tone: str = 'cream', mark: Optional[str] = None) -> None:
    """Мешок 9×10 (x, y — левый верх), завязка сверху; mark — цвет метки на боку."""
    lay = blank(cv)
    lay.ellipse(x + 4.5, y + 6.0, 4.5, 4.2, f'{tone}1')
    lay.rect(x + 2, y + 1, 5, 4, f'{tone}1')
    lay.fill_where(lambda px, py: px < x + 4 and py < y + 8, f'{tone}2')
    lay.fill_where(lambda px, py: px >= x + 7 or py >= y + 9, f'{tone}0')
    lay.hline(x + 2, x + 6, y + 2, 'wood3')
    put(cv, lay, 'wood1')
    if mark:
        cv.rect(x + 3, y + 5, 3, 2, mark)


def _crate(cv: Canvas, x: int, y: int, w: int = 10, h: int = 8, m: Dict[str, str] = WOOD) -> None:
    """Ящик w×h с крышкой (вид 3/4)."""
    lay = blank(cv)
    lay.rect(x, y, w, h, m['b'])
    lay.rect(x, y, w, 3, m['l'])
    lay.hline(x, x + w - 1, y, m['h'])
    lay.hline(x, x + w - 1, y + 3, m['d'])
    lay.vline(x, y + 3, y + h - 1, m['l'])
    lay.vline(x + w - 1, y + 1, y + h - 1, m['d'])
    lay.line(x + 1, y + h - 1, x + w - 2, y + 4, m['d'])
    put(cv, lay, m['o'])


def prop(cv: Canvas, kind: str, good: str) -> None:
    """Справа во дворе: свой реквизит завода (x 43…60, y 84…110)."""
    if kind == 'cans':
        churn = Canvas.from_ascii([
            '..HS...',
            '.HSSsd.',
            '..Hsd..',
            '.HSSSd.',
            'HSSSSSd',
            'HBBBBbd',
            'HSSSSSd',
            'HSSSSSd',
            'HSSSSSd',
            '.ddddd.',
        ], {'H': 'stone7', 'S': 'stone6', 's': 'stone5', 'd': 'stone3', 'B': 'blue4', 'b': 'blue3'})
        for x, y in ((44, 98), (52, 98), (48, 90)):
            lay = blank(cv)
            lay.blit(churn, x, y)
            put(cv, lay, 'ink1')
    elif kind == 'logs':
        for i, (x, y) in enumerate(((44, 101), (51, 101), (47, 95))):
            lay = blank(cv)
            lay.ellipse(x + 3.5, y + 3.5, 3.5, 3.5, 'wood6')
            lay.ellipse(x + 3.5, y + 3.5, 2.0, 2.0, 'wood5')
            lay.set(x + 3, y + 3, 'wood3')
            put(cv, lay, 'wood2')
        cv.hline(43, 59, 109, 'wood1')
    elif kind == 'net':
        lay = blank(cv)
        for y in range(88, 104):
            for x in range(46, 59):
                if (x + y) % 3 == 0 or (x - y) % 3 == 0:
                    lay.set(x, y, 'cream1')
        put(cv, lay, None)
        cv.vline(46, 86, 106, 'wood4')
        cv.vline(58, 86, 106, 'wood3')
        cv.hline(45, 59, 86, 'wood5')
        _crate(cv, 47, 101, 11, 8, WOOD)
        for x in (49, 52, 55):
            cv.set(x, 101, 'navy7')
            cv.set(x + 1, 101, 'navy6')
    elif kind == 'basket':
        lay = blank(cv)
        lay.polygon([(44, 98), (59, 98), (57, 109), (46, 109)], 'wood4')
        for yy in (100, 103, 106):
            lay.fill_where(lambda x, y, yy=yy: y == yy, 'wood2')
        put(cv, lay, 'wood0')
        for x, y in ((46, 93), (52, 92)):
            cv.blit(goods_art.goods_icon('yarn', 8), x, y)
    elif kind == 'sacks':
        _sack(cv, 44, 98, 'cream', 'amber2')
        _sack(cv, 52, 98, 'cream', 'amber2')
        _sack(cv, 48, 90, 'cream', 'amber2')
    elif kind == 'flatboxes':
        for i in range(4):
            _crate(cv, 44 + (i % 2), 102 - i * 4, 14, 4, WALLS['cardboard'])
    elif kind == 'kiln':
        lay = blank(cv)
        lay.ellipse(51.5, 100, 7.5, 8, 'red3')
        lay.rect(44, 100, 16, 9, 'red3')
        masonry(lay, 44, 92, 16, 17, dict(WALLS['brick']), 31, row_h=4, wmin=4, wmax=6)
        put(cv, lay, 'red0')
        cv.rect(49, 101, 6, 7, 'ink2')
        cv.rect(50, 103, 4, 4, 'amber3')
        cv.hline(50, 53, 104, 'gold5')
    elif kind == 'beams':
        for i, y in enumerate((104, 100, 96)):
            lay = blank(cv)
            lay.rect(44 + (i % 2), y, 15, 3, 'stone5')
            lay.hline(44 + (i % 2), 58 + (i % 2), y, 'stone7')
            lay.hline(44 + (i % 2), 58 + (i % 2), y + 2, 'stone3')
            put(cv, lay, 'stone0')
    elif kind == 'bottles':
        _crate(cv, 44, 100, 15, 9, WOOD)
        for i, x in enumerate((45, 49, 53)):
            lay = blank(cv)
            lay.rect(x, 93, 3, 7, 'teal4')
            lay.rect(x + 1, 90, 1, 3, 'teal4')
            lay.vline(x, 94, 98, 'teal6')
            put(cv, lay, 'teal1')
    elif kind == 'pots':
        for i, x in enumerate((44, 52)):
            lay = blank(cv)
            lay.polygon([(x, 100), (x + 8, 100), (x + 7, 108), (x + 1, 108)], 'red3')
            lay.hline(x, x + 8, 100, 'red5')
            lay.fill_where(lambda px, py: px >= x + 6, 'red2')
            put(cv, lay, 'red0')
            cv.blit(goods_art.goods_icon('catnip', 8), x, 92)
    elif kind == 'toybox':
        _crate(cv, 44, 98, 15, 11, dict(WALLS['blue']))
        cv.blit(goods_art.goods_icon('toys', 8), 46, 91)
        lay = blank(cv)
        lay.ellipse(55, 95, 3, 3, 'red4')
        lay.set(54, 94, 'red6')
        put(cv, lay, 'red1')
    elif kind == 'posts':
        cv.blit(goods_art.goods_icon('scratchers', 16), 44, 92)
    elif kind == 'pillows':
        for i in range(3):
            lay = blank(cv)
            lay.rect(44 + (i % 2), 104 - i * 5, 15, 5, 'pink3')
            lay.hline(44 + (i % 2), 58 + (i % 2), 104 - i * 5, 'pink4')
            lay.hline(44 + (i % 2), 58 + (i % 2), 108 - i * 5, 'pink1')
            put(cv, lay, 'pink0')
    elif kind == 'bellpost':
        vbeam(cv, 45, 86, 3, 23, WOOD, seed=3, cap=False)
        hbeam(cv, 44, 85, 15, 3, WOOD, seed=4)
        cv.vline(54, 88, 90, 'stone3')
        cv.blit(goods_art.goods_icon('bells', 16), 46, 89)
    elif kind == 'collarrack':
        vbeam(cv, 44, 88, 3, 21, WOOD, seed=5, cap=False)
        vbeam(cv, 56, 88, 3, 21, WOOD, seed=6, cap=False)
        hbeam(cv, 43, 88, 17, 3, WOOD, seed=7)
        for x, c in ((47, 'red4'), (51, 'blue4'), (54, 'green4')):
            lay = blank(cv)
            lay.ellipse(x + 1.5, 95, 2, 3, c)
            lay.ellipse(x + 1.5, 95, 1, 2, None)
            put(cv, lay, 'ink1')
            cv.set(x + 1, 98, 'gold4')
    elif kind == 'biggear':
        lay = Canvas(FW, FH)
        goods_art.gear(lay, 51.5, 100.0, 8.0, 6.2, 2.0, 10, 0.1, ('gold5', 'gold4', 'gold3', 'gold2', 'gold1'), hole='ink2')
        put(cv, lay, 'ink1')
    elif kind == 'lamppost':
        vbeam(cv, 50, 88, 3, 21, IRON, seed=8, cap=False)
        cv.hline(46, 56, 88, 'stone3')
        for x in (45, 54):
            lantern(cv, x - 1, 89)
    elif kind == 'hives':
        for x in (44, 52):
            lay = blank(cv)
            for k, yy in enumerate((99, 102, 105)):
                lay.rect(x, yy, 8, 3, 'amber3' if k % 2 == 0 else 'amber2')
                lay.hline(x, x + 7, yy, 'amber4')
            lay.rect(x - 1, 97, 10, 2, 'wood4')
            lay.hline(x - 1, x + 8, 97, 'wood6')
            put(cv, lay, 'amber0')
            cv.rect(x + 3, 106, 2, 1, 'ink2')
        for x, y in ((47, 93), (55, 91), (51, 89)):
            cv.set(x, y, 'gold4')
            cv.set(x + 1, y, 'ink2')
            cv.set(x, y - 1, 'cream4')
    elif kind == 'spools':
        for i, x in enumerate((44, 52)):
            lay = blank(cv)
            lay.rect(x, 97, 7, 2, 'wood5')
            lay.rect(x + 1, 99, 5, 8, 'purple5')
            lay.vline(x + 1, 99, 106, 'purple6')
            lay.vline(x + 5, 99, 106, 'purple4')
            lay.rect(x, 107, 7, 2, 'wood3')
            put(cv, lay, 'wood0')
    elif kind == 'barrels':
        _barrel(cv, 44, 97)
        _barrel(cv, 52, 97)
        cv.blit(goods_art.goods_icon('caviar', 8), 49, 90)


# ───────────────────────── сборка здания ─────────────────────────
def factory_base(good: str) -> Canvas:
    """Здание завода товара good (стоит: колесо — кадр 0, лента с ящиками в положении кадра 0, без дыма)."""
    spec = FACTORIES[good]
    seed = GOODS.index(good) * 7 + 3
    hot = bool(spec.get('hot'))
    iron = spec.get('frame') == 'iron'
    frame = IRON if iron else FRAME
    cv = Canvas(FW, FH)
    aframe(cv, iron)
    chimney(cv, str(spec['chimney']))
    wall(cv, str(spec['wall']), str(spec['wall_kind']), seed)
    roof(cv, str(spec['roof']), str(spec['roof_kind']), seed)
    if spec['wall_kind'] != 'greenhouse':
        for wx, wy, ww, wh in WINDOWS:
            window(cv, wx, wy, ww, wh, hot, frame)
    door(cv, hot, frame)
    sign(cv, good)
    cv.blit(wheel_layer(0), 0, 0)
    pallet(cv, good)
    prop(cv, str(spec['prop']), good)
    cv.blit(belt_layer(0), 0, 0)
    bin_front(cv)
    return cv


def work_overlay(good: str, frame: int) -> Canvas:
    """Наложение «работает» кадра frame: колесо, дым по виду завода, лента с ящиками (заканчивается над приёмным ящиком)."""
    cv = Canvas(FW, FH)
    cv.blit(wheel_layer(frame), 0, 0)
    cv.blit(smoke_layer(frame, str(FACTORIES[good]['smoke'])), 0, 0)
    cv.blit(belt_layer(frame), 0, 0)
    return cv


# ───────────────────────── цепи ─────────────────────────
# Цепи крест-накрест через фасад от углов кровли к углам цоколя, третья — поперёк колеса (оно не крутится);
# в перекрестье — большой навесной замок.
CHAIN_LINES = [((6, 34), (58, 82)), ((58, 34), (6, 82)), ((19, 6), (45, 20))]
LOCK_C = (32, 58)


def _link_points(p0: Tuple[float, float], p1: Tuple[float, float], step: float = 3.0) -> List[Tuple[float, float, int]]:
    (x0, y0), (x1, y1) = p0, p1
    n = int(math.hypot(x1 - x0, y1 - y0) / step)
    return [(x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n, k) for k in range(n + 1)]


def chain_line(cv: Canvas, p0, p1, skip: Callable[[int], bool] = lambda k: False) -> None:
    """Звенья по отрезку: через одно — кольцо анфас (4×4 со светлой кромкой) и звено в профиль (планка 2×2)."""
    lay = blank(cv)
    for x, y, k in _link_points(p0, p1):
        if skip(k):
            continue
        xi, yi = int(round(x)), int(round(y))
        if k % 2 == 0:
            for dx, dy, c in ((-1, -2, 'stone6'), (0, -2, 'stone6'), (-2, -1, 'stone6'), (1, -1, 'stone4'),
                              (-2, 0, 'stone5'), (1, 0, 'stone3'), (-1, 1, 'stone3'), (0, 1, 'stone2')):
                lay.set(xi + dx, yi + dy, c)
        else:
            lay.rect(xi - 1, yi - 1, 2, 2, 'stone4')
            lay.set(xi - 1, yi - 1, 'stone6')
            lay.set(xi, yi, 'stone2')
    put(cv, lay, 'ink1')


def padlock(cv: Canvas, cx: int, cy: int) -> Canvas:
    """Большой навесной замок 14×16: стальная дужка, золотой корпус с бликом, скважина."""
    rows = [
        '...SSSSSSS....',
        '..SsDDDDDsS...',
        '.SsD.....DsS..',
        '.Ss.......sS..',
        '.Ss.......sS..',
        'GGGGGGGGGGGGGg',
        'GWYYYYYYYYYYYg',
        'GYYYYkkYYYYYgd',
        'GYYYkkkkYYYYgd',
        'GYYYkkkkYYYYgd',
        'GYYYYkkYYYYYgd',
        'GYYYYkkYYYYYgd',
        'GYYYYkkYYYYYgd',
        'GYYYYYYYYYYYgd',
        'gggggggggggggd',
        '.dddddddddddd.',
    ]
    lay = Canvas.from_ascii(rows, {'S': 'stone6', 's': 'stone4', 'D': 'stone3', 'G': 'gold4', 'W': 'gold5', 'Y': 'gold3',
                                   'g': 'gold2', 'd': 'gold1', 'k': 'ink2'})
    lay.outline('ink1')
    cv.blit(lay, cx - lay.w // 2, cy - lay.h // 2)
    return lay


def chains(stage: int = 0) -> Canvas:
    """factory_chains (stage 0) — целые цепи с замком."""
    cv = Canvas(FW, FH)
    for p0, p1 in CHAIN_LINES:
        chain_line(cv, p0, p1)
    # Скобы-кольца, которыми цепи вбиты в стойки.
    for x, y in ((6, 34), (58, 34), (6, 82), (58, 82)):
        cv.rect(x - 1, y - 1, 3, 3, 'stone2')
        cv.set(x - 1, y - 1, 'stone5')
    padlock(cv, *LOCK_C)
    return cv


# Повреждения цепей по стадиям 1…3: номера звеньев (по CHAIN_LINES), которые лопнули, и трещины замка.
CHAIN_BREAKS = [
    [(0, 3), (1, 13), (2, 4)],
    [(0, 9), (0, 15), (1, 4), (1, 17), (2, 7)],
    [(0, 6), (0, 19), (1, 8), (1, 21), (2, 2)],
]
LOCK_CRACKS = [
    [[(-4, 0), (-2, 2), (-3, 4), (-1, 6)]],
    [[(3, 1), (4, 3), (2, 5)], [(-5, 3), (-6, 5)]],
    [[(0, 0), (1, 2), (0, 4), (1, 7)], [(5, 5), (4, 7)]],
]


def _crack_line(cv: Canvas, pts, ox: int, oy: int, base: Canvas) -> None:
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        n = max(abs(bx - ax), abs(by - ay)) + 1
        for k in range(n):
            x = ox + round(ax + (bx - ax) * k / max(1, n - 1))
            y = oy + round(ay + (by - ay) * k / max(1, n - 1))
            if base.opaque(x, y):
                cv.set(x, y, 'ink1')
                if base.opaque(x + 1, y) and (x + y) % 2 == 0:
                    cv.set(x + 1, y, 'gold5')


def chains_crack(level: int) -> Canvas:
    """Наложение повреждений цепей стадии level (1…3) поверх factory_chains: лопнувшие звенья (тёмный разрыв поперёк
    звена и светлый скол), трещины корпуса замка; на 3-й стадии отскочила дужка, у скоб висят обрывки."""
    base = chains(0)
    cv = Canvas(FW, FH)
    lx, ly = LOCK_C
    for st in range(level):
        for line, k in CHAIN_BREAKS[st]:
            p0, p1 = CHAIN_LINES[line]
            pts = _link_points(p0, p1)
            if k >= len(pts):
                continue
            x, y, _ = pts[k]
            xi, yi = int(round(x)), int(round(y))
            for dx, dy in ((-1, -1), (0, -1), (-1, 0), (0, 0)):
                if base.opaque(xi + dx, yi + dy):
                    cv.set(xi + dx, yi + dy, 'ink1')
            cv.set(xi + 1, yi - 2, 'stone7')
            cv.set(xi - 2, yi + 1, 'stone7')
        for pl in LOCK_CRACKS[st]:
            _crack_line(cv, pl, lx, ly + 1, base)
    if level >= 3:
        # Дужка замка лопнула справа и отогнулась вверх.
        for dy in range(-7, -2):
            cv.set(lx + 3, ly + dy, 'ink1')
            cv.set(lx + 4, ly + dy, 'ink1')
        lay = blank(cv)
        lay.line(lx + 4, ly - 4, lx + 8, ly - 10, 'stone6')
        lay.line(lx + 5, ly - 4, lx + 9, ly - 10, 'stone4')
        put(cv, lay, 'ink1')
        for x, y in ((8, 86), (54, 86)):
            lay = blank(cv)
            lay.rect(x, y, 2, 3, 'stone5')
            lay.set(x, y, 'stone7')
            lay.rect(x + 1, y + 4, 2, 2, 'stone4')
            put(cv, lay, 'ink1')
    return cv


# ───────────────────────── детали труб ─────────────────────────
def pipe_cap() -> Canvas:
    """Заглушка трубы у завода 14×14: стальной фланец с болтами по углам и тёмным окном 8×8 под иконку товара
    goods_<good>_8 (окно — пиксели 3…10, центр совпадает с центром холста)."""
    cv = Canvas(14, 14)
    cv.rect(1, 1, 12, 12, 'stone4')
    cv.hline(1, 12, 1, 'stone6')
    cv.vline(1, 1, 12, 'stone5')
    cv.hline(2, 12, 12, 'stone2')
    cv.vline(12, 2, 12, 'stone2')
    cv.rect(3, 3, 8, 8, 'navy1')
    cv.hline(3, 10, 3, 'ink2')
    cv.vline(3, 3, 10, 'ink2')
    for x, y in ((2, 2), (11, 2), (2, 11), (11, 11)):
        cv.set(x, y, 'stone7')
    for x, y in ((1, 1), (12, 1), (1, 12), (12, 12)):
        cv.set(x, y, None)
    cv.outline('ink1')
    return cv


def pipe_coupling() -> Canvas:
    """Муфта жгута 12×12: стальной хомут с болтами по углам, тёмные проёмы с четырёх сторон (симметрична)."""
    rows = [
        '..KKKKKKKK..',
        '.KHSSkkSSdK.',
        'KHSbSSSSbSdK',
        'KSSSSSSSSSdK',
        'KSSSHSSsSSdK',
        'KkSSSSSSSSkK',
        'KkSSSSSSSSkK',
        'KSSSsSSdSSdK',
        'KSSSSSSSSSdK',
        'KSdbSSSSbddK',
        '.KddkkkkddK.',
        '..KKKKKKKK..',
    ]
    return Canvas.from_ascii(rows, {'K': 'ink1', 'H': 'stone6', 'S': 'stone4', 's': 'stone3', 'd': 'stone2', 'k': 'ink2',
                                    'b': 'stone7'})


def pipe_inlet() -> Canvas:
    """Вход труб на склад 16×10: стальной коллектор, сверху гнёзда для труб, обжимная лента, болты."""
    rows = [
        '.k.k.k.k.k.k.k..',
        'KHSHSHSHSHSHSHdK',
        'KHSSSSSSSSSSSSdK',
        'KSbSSSSSSSSSSbdK',
        'KGGGGGGGGGGGGGgK',
        'KgggggggggggggdK',
        'KSSSSSSSSSSSSSdK',
        'KSbSSSSSSSSSSbdK',
        'KdddddddddddddDK',
        '.KKKKKKKKKKKKKK.',
    ]
    cv = Canvas.from_ascii(rows, {'K': 'ink1', 'H': 'stone6', 'S': 'stone4', 'd': 'stone2', 'D': 'stone1', 'k': 'ink2',
                                  'b': 'stone7', 'G': 'gold3', 'g': 'gold2'})
    return cv


def pipe_capsule(horizontal: bool = False) -> Canvas:
    """Партия в пути 3×6: белые тона (код умножает на цвет полосы товара): светлый левый край, тень справа и снизу."""
    rows = [
        '.W.',
        'HWs',
        'HWs',
        'HWs',
        'HWs',
        '.S.',
    ]
    cv = Canvas.from_ascii(rows, {'H': 'white', 'W': 'fur_white3', 's': 'fur_white2', 'S': 'fur_white1'})
    if not horizontal:
        return cv
    out = Canvas(6, 3)
    for y in range(6):
        for x in range(3):
            p = cv.get(x, y)
            if p[3]:
                out.px[y, x] = p
    return out


def build(reg) -> None:
    for g in GOODS:
        reg.add(f'factory_{g}', factory_base(g), pivot=PIVOT, note=f'завод товара {g} 2×3; колесо — кадр 0')
        for i in range(FACTORY_WORK_FRAMES):
            reg.add(f'factory_{g}_work_{i}', work_overlay(g, i), pivot=PIVOT,
                    note=f'завод {g} работает: колесо, дым ({FACTORIES[g]["smoke"]}), лента; 0,15 с/кадр')
    reg.add('factory_chains', chains(0), pivot=PIVOT, note='цепи с замком поверх затемнённого здания')
    for i in range(1, 4):
        reg.add(f'factory_chains_crack_{i}', chains_crack(i), pivot=PIVOT, note=f'трещины цепей, стадия {i} (поверх factory_chains)')
    reg.add('fpipe_cap', pipe_cap(), pivot=(7.0, 7.0), note='заглушка трубы у завода; goods_<good>_8 — поверх, центр в центр')
    reg.add('fpipe_coupling', pipe_coupling(), pivot=(6.0, 6.0), note='муфта жгута труб')
    reg.add('fpipe_inlet', pipe_inlet(), pivot=(8.0, 5.0), note='вход труб на склад')
    reg.add('fpipe_capsule', pipe_capsule(False), pivot=(1.5, 3.0), note='капсула партии (вертикально), красится цветом товара')
    reg.add('fpipe_capsule_h', pipe_capsule(True), pivot=(3.0, 1.5), note='капсула партии (горизонтально), красится цветом товара')
