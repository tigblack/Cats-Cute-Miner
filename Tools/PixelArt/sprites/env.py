"""Окружение поля: плиты пола, стены пещеры вокруг поля, декор стен, метка пустой клетки зоны мержа.

Ориентир — концепт Docs/Art/Concepts/style-exploration-2026-10-06/04_pixel_art.png:
спокойный пол из крупных плит slate с тёмной затиркой (сетка 7 столбцов читается сразу),
тёмно-синие валуны стен с холодными бликами и редким оливковым мхом, тёплые фонари и дерево как точки интереса.

Стены рисуются «валунами» в мировых координатах и вырезаются тайлами 32×32. Валуны рамки (SIDE_FRAME)
пересекают края тайла и общие для всех стен; каждый тайл края/угла включает ровно те валуны соседей,
что заходят к нему через край. Поэтому раскладка
  строки < −1: wall_top*; строка −1: wall_corner_tl | wall_top_edge ×7 | wall_corner_tr (дальше — wall_top*);
  строки 0…Depth−1: wall_side_* … wall_edge_l_* | пол ×7 | wall_edge_r_* … wall_side_*;
  строка Depth: wall_corner_bl | wall_bottom ×7 | wall_corner_br (дальше — wall_side_*); ниже — wall_side_*
стыкуется без швов при любом выборе вариантов (проверено сравнением с единым рендером мира).
"""
from __future__ import annotations

import math
from typing import Dict, List, Optional, Sequence, Tuple

import pxl
from pxl import Canvas

T = 32  # пикселей арта на клетку

# Свет сверху-слева и «от зрителя»: x вправо, y вниз по экрану, z к камере.
_L = (-0.62, -0.78, 0.55)
_LN = math.sqrt(sum(c * c for c in _L))
LIGHT = (_L[0] / _LN, _L[1] / _LN, _L[2] / _LN)


def _r(name: str, i: int) -> str:
    """Тон рампы с ограничением индекса: _r('navy', 9) → 'navy8'."""
    n = len(pxl.RAMPS[name])
    return f'{name}{max(0, min(n - 1, i))}'


# ───────────────────────── Валуны (стены) ─────────────────────────
class Rock:
    """Валун: неровный суперэллипс с центром (cx, cy) и полуосями (rx, ry), мировые пиксели.

    base — базовый тон рампы navy (меньше — глубже/темнее); layer — слой (больше — поверх, ближе к полю);
    moss — уровень мха по верхушке (None — без мха; от −1 до 1: чем больше, тем ниже спускается мох);
    lumps — неровность силуэта; spark — одиночные блики (смещения от центра).
    """

    def __init__(self, cx: float, cy: float, rx: float, ry: float, base: int = 3, layer: int = 0,
                 moss: Optional[float] = None, lumps: float = 0.07, seed: int = 0,
                 spark: Sequence[Tuple[int, int]] = (), crack: Sequence[Tuple[int, int]] = ()):
        self.cx, self.cy, self.rx, self.ry = cx, cy, rx, ry
        self.base, self.layer, self.moss, self.lumps = base, layer, moss, lumps
        self.seed = seed if seed else int(cx * 7 + cy * 13 + rx * 31) % 97 + 1
        self.spark = list(spark)
        self.crack = list(crack)  # ломаная грани (смещения от центра): тёмная линия скола

    def at(self, dx: float, dy: float) -> 'Rock':
        return Rock(self.cx + dx, self.cy + dy, self.rx, self.ry, self.base, self.layer, self.moss, self.lumps,
                    self.seed, self.spark, self.crack)

    def mirrored(self, axis2: float = T) -> 'Rock':
        """Зеркально по X (axis2 = удвоенная ось); свет остаётся сверху-слева — перерисовывается, а не отражается."""
        return Rock(axis2 - self.cx, self.cy, self.rx, self.ry, self.base, self.layer, self.moss, self.lumps,
                    self.seed + 50, [(-dx, dy) for dx, dy in self.spark], [(-dx, dy) for dx, dy in self.crack])


def copies(rocks: Sequence[Rock], dxs: Sequence[int] = (0,), dys: Sequence[int] = (0,)) -> List[Rock]:
    return [r.at(dx, dy) for r in rocks for dx in dxs for dy in dys]


TORUS = (-T, 0, T)


class Scene:
    """Отрисованная область мира: холст + для каждого пикселя валун (или None — щель)."""

    def __init__(self, cv: Canvas, owner: List[List[Optional[Rock]]], x0: int, y0: int):
        self.cv, self.owner, self.x0, self.y0 = cv, owner, x0, y0


def render(rocks: Sequence[Rock], x0: int, y0: int, w: int, h: int, bg: str = 'navy0',
           outline: str = 'navy1', ramp: str = 'navy', margin: int = 2) -> Scene:
    """Кладка валунов в области [x0, x0+w) × [y0, y0+h) мира (с полем margin для корректного контура)."""
    W, H = w + 2 * margin, h + 2 * margin
    ox, oy = x0 - margin, y0 - margin
    owner: List[List[Optional[Rock]]] = [[None] * W for _ in range(H)]
    local: List[List[Optional[Tuple[float, float, float]]]] = [[None] * W for _ in range(H)]
    order = sorted(rocks, key=lambda r: (r.layer, r.cy + r.ry, r.cx))
    rank = {id(r): k for k, r in enumerate(order)}
    for r in order:
        ph2, ph3 = r.seed * 0.7, r.seed * 1.3
        for y in range(int(math.floor(r.cy - r.ry)) - 2, int(math.ceil(r.cy + r.ry)) + 2):
            yy = y - oy
            if not 0 <= yy < H:
                continue
            for x in range(int(math.floor(r.cx - r.rx)) - 2, int(math.ceil(r.cx + r.rx)) + 2):
                xx = x - ox
                if not 0 <= xx < W:
                    continue
                u = (x + 0.5 - r.cx) / r.rx
                v = (y + 0.5 - r.cy) / r.ry
                th = math.atan2(v, u)
                k = 1.0 + r.lumps * math.cos(2 * th + ph2) + r.lumps * 0.8 * math.cos(3 * th + ph3)
                d = (abs(u) ** 2.3 + abs(v) ** 2.3) ** (1 / 2.3) / k
                if d <= 1.0:
                    owner[yy][xx] = r
                    local[yy][xx] = (u / k, v / k, d)
    big = Canvas(W, H)
    for yy in range(H):
        for xx in range(W):
            r = owner[yy][xx]
            if r is None:
                big.set(xx, yy, bg)
                continue
            edge = False
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx_, ny_ = xx + dx, yy + dy
                if not (0 <= nx_ < W and 0 <= ny_ < H):
                    continue
                q = owner[ny_][nx_]
                if q is r:
                    continue
                if q is None or rank[id(q)] < rank[id(r)]:
                    edge = True
                    break
            if edge:
                big.set(xx, yy, outline)
                continue
            u, v, d = local[yy][xx]
            # Плосковатая верхушка и скруглённые бока: нормаль наклоняется к краю сильнее, чем в центре.
            f = 0.2 + 0.8 * d ** 1.4
            nx, ny = u * f, v * f
            nz = math.sqrt(max(0.04, 1.0 - nx * nx - ny * ny))
            inten = nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]
            # Плоская верхушка (inten ≈ 0.5) — base+1, кромка к свету — base+2/+3, низ и правый бок — base/base−1.
            b = r.base
            if inten > 0.9:
                t = b + 3
            elif inten > 0.63:
                t = b + 2
            elif inten > 0.36:
                t = b + 1
            elif inten > -0.05:
                t = b
            else:
                t = b - 1
            col = _r(ramp, t)
            if r.moss is not None:
                # Координаты относительно валуна: копии валуна (повтор через край) получают тот же мох.
                x, y = xx + ox - int(math.floor(r.cx)), yy + oy - int(math.floor(r.cy))
                wob = 0.14 * math.sin(x * 1.9 + r.seed) + 0.09 * math.sin(x * 0.7 + 2.0 + r.seed)
                if v < r.moss + wob:
                    col = 'green4' if inten > 0.72 else ('green3' if inten > 0.3 else 'green2')
                    if inten > 0.93:
                        col = 'green5'
                elif v < r.moss + wob + 0.18 and (x + y) % 2 == 0:
                    col = 'green2'  # рваная кромка мха
            big.set(xx, yy, col)
    # Сколы: тёмная линия грани, под ней (справа-снизу) — светлый край.
    oc = pxl.C(outline)
    for r in rocks:
        for (ax, ay), (bx, by) in zip(r.crack, r.crack[1:]):
            pts: List[Tuple[int, int]] = []
            fl = math.floor  # floor, а не int: у копий за левым/верхним краем координаты отрицательные
            x_a, y_a, x_b, y_b = fl(r.cx + ax), fl(r.cy + ay), fl(r.cx + bx), fl(r.cy + by)
            n = max(abs(x_b - x_a), abs(y_b - y_a), 1)
            for i in range(n + 1):
                pts.append((round(x_a + (x_b - x_a) * i / n), round(y_a + (y_b - y_a) * i / n)))
            for px_, py_ in pts:
                xx, yy = px_ - ox, py_ - oy
                if 0 <= xx < W and 0 <= yy < H and owner[yy][xx] is r and big.get(xx, yy) != oc:
                    big.set(xx, yy, _r(ramp, r.base - 1))
                    if 0 <= xx + 1 < W and owner[yy][xx + 1] is r and big.get(xx + 1, yy) != oc and (px_ + 1, py_) not in pts:
                        big.set(xx + 1, yy, _r(ramp, r.base + 2))
    # Одиночные блики на освещённой грани.
    for r in rocks:
        for dx, dy in r.spark:
            xx, yy = math.floor(r.cx + dx) - ox, math.floor(r.cy + dy) - oy
            if 0 <= xx < W and 0 <= yy < H and owner[yy][xx] is r:
                big.set(xx, yy, _r(ramp, r.base + 3))
    cv = big.crop(margin, margin, w, h)
    own = [row[margin:margin + w] for row in owner[margin:margin + h]]
    return Scene(cv, own, x0, y0)


def torus_tile(rocks: Sequence[Rock]) -> Canvas:
    """Тайл, бесшовный во все стороны: валуны повторяются через края."""
    return render(copies(rocks, TORUS, TORUS), 0, 0, T, T).cv


# Рамка: валуны, пересекающие края тайла (общие для всех стен). Средние, чтобы главным в тайле был крупный
# валун варианта — так решётка повтора 32 px не бросается в глаза.
SIDE_FRAME: List[Rock] = [
    Rock(2.0, 2.0, 9.5, 8.0, base=3, spark=[(-5, -3)]),     # угол — виден во всех четырёх углах
    Rock(19.0, 31.0, 8.0, 6.0, base=3),                     # верх/низ
    Rock(31.0, 17.5, 6.0, 7.5, base=3),                     # лево/право
]
# Внутренности вариантов wall_side (не ближе 2 px к краям тайла).
SIDE_INNER: List[List[Rock]] = [
    [Rock(15.5, 16.0, 11.0, 9.0, base=3, layer=1, spark=[(-6, -5)], crack=[(1, -8), (2, -4)]),
     Rock(26.0, 6.5, 3.5, 3.0, base=3, layer=1)],
    [Rock(12.0, 15.0, 8.0, 8.0, base=3, layer=1, spark=[(-4, -5)]),
     Rock(23.0, 20.5, 6.0, 6.5, base=2, layer=1),
     Rock(22.0, 8.0, 3.5, 3.0, base=3, layer=1)],
    [Rock(16.0, 14.0, 10.5, 8.0, base=3, layer=1, moss=-0.4, spark=[(-6, -2)]),
     Rock(9.0, 25.0, 4.0, 3.0, base=3, layer=1)],
    [Rock(14.0, 18.0, 9.0, 7.0, base=3, layer=1, spark=[(-5, -4)], crack=[(4, -6), (5, -3)]),
     Rock(24.0, 9.0, 4.5, 4.0, base=2, layer=1),
     Rock(6.0, 27.0, 2.5, 2.0, base=3, layer=2)],
]
# Задняя стена: внутренние валуны глубже тоном (base 2) — верх экрана темнее и не спорит с HUD.
TOP_INNERS: List[List[Rock]] = [
    [Rock(15.0, 16.0, 10.5, 8.5, base=2, layer=1, crack=[(1, -8), (2, -4)]), Rock(25.5, 8.0, 3.5, 3.0, base=2, layer=1),
     Rock(7.0, 26.0, 3.5, 2.5, base=2, layer=1)],
    [Rock(12.0, 14.0, 8.0, 7.5, base=2, layer=1, spark=[(-4, -4)]), Rock(22.0, 20.0, 7.0, 6.5, base=2, layer=1),
     Rock(24.0, 7.0, 3.5, 3.0, base=2, layer=1)],
    [Rock(16.0, 15.0, 9.5, 9.0, base=2, layer=1, spark=[(-5, -5)]), Rock(7.0, 25.0, 4.0, 3.0, base=2, layer=1)],
    [Rock(13.0, 17.0, 8.0, 8.0, base=2, layer=1, crack=[(2, -7), (3, -3)]), Rock(23.0, 10.0, 5.0, 5.0, base=2, layer=1),
     Rock(24.0, 24.0, 4.0, 3.5, base=2, layer=1)],
]
TOP_INNER = TOP_INNERS[0]


def wall_side(v: int) -> Canvas:
    return torus_tile(SIDE_FRAME + SIDE_INNER[v])


def wall_top(v: int = 0) -> Canvas:
    """Задняя стена над строкой 0: та же рамка, глубже тоном внутри — стыкуется с wall_side со всех сторон."""
    return torus_tile(SIDE_FRAME + TOP_INNERS[v])


def _frame_left_of(x_edge: int) -> List[Rock]:
    """Валуны заливки слева от вертикального края x_edge (тайл стены [x_edge−32, x_edge)), повторённые по Y."""
    return copies(SIDE_FRAME + SIDE_INNER[0], (x_edge - T,), TORUS) + \
        [r for r in copies(SIDE_FRAME, (x_edge,), TORUS) if r.cx - r.rx < x_edge + 1]


def _frame_right_of(x_edge: int) -> List[Rock]:
    """Валуны заливки справа от края x_edge (тайл стены [x_edge, x_edge+32)), повторённые по Y."""
    return copies(SIDE_FRAME + SIDE_INNER[0], (x_edge,), TORUS) + \
        [r for r in copies(SIDE_FRAME, (x_edge - T,), TORUS) if r.cx + r.rx > x_edge - 1]


# ── край стены у поля ──
# Левый край (столбец −1): x = 0…31, поле начинается справа (x = 32). «Губа» — светлые валуны у пола.
EDGE_SHARED: List[Rock] = [
    Rock(21.0, 0.0, 9.0, 7.5, base=4, layer=2, spark=[(-5, -3)]),
]
EDGE_INNER: List[List[Rock]] = [
    [Rock(20.0, 17.0, 9.5, 9.0, base=4, layer=2, spark=[(-6, -4)], crack=[(1, -8), (2, -4)]),
     Rock(11.0, 12.0, 4.5, 4.5, base=3, layer=1),
     Rock(29.0, 27.5, 2.5, 2.0, base=4, layer=3)],
    [Rock(23.0, 13.0, 7.0, 5.5, base=4, layer=2, moss=-0.45),
     Rock(19.0, 23.5, 8.5, 5.5, base=4, layer=2, spark=[(-5, -2)]),
     Rock(10.0, 17.0, 4.5, 6.0, base=3, layer=1),
     Rock(29.0, 7.0, 2.5, 2.0, base=4, layer=3)],
]
FLOOR_X = 28  # от этой колонки (у левого края) непокрытые губой пиксели — пол в тени стены


def _floor_strip(sc: Scene, is_floor, shadow_dirs: Sequence[Tuple[int, int]], lit: str, dark: str,
                 grout_rows: Sequence[int] = (), grout_cols: Sequence[int] = ()) -> None:
    """Полоса пола у основания стены: пиксели, где is_floor(x, y), перекрашиваются в пол с контактной тенью."""
    cv = sc.cv
    mask = [[is_floor(x, y) for x in range(cv.w)] for y in range(cv.h)]
    for y in range(cv.h):
        for x in range(cv.w):
            if not mask[y][x]:
                continue
            near = False
            for dx, dy in shadow_dirs:
                xx, yy = x + dx, y + dy
                if 0 <= xx < cv.w and 0 <= yy < cv.h and not mask[yy][xx]:
                    near = True
            c = dark if near else lit
            if y in grout_rows or x in grout_cols:
                c = 'navy2'
            cv.set(x, y, c)


def wall_edge_l(v: int) -> Canvas:
    rocks = _frame_left_of(0) + copies(EDGE_SHARED, (0,), TORUS) + EDGE_INNER[v]
    sc = render(rocks, 0, 0, T, T)

    def is_floor(x: int, y: int) -> bool:
        r = sc.owner[y][x]
        return x >= FLOOR_X and (r is None or r.layer < 2)
    # Свет слева: стена отбрасывает тень вправо — у камня пол темнее.
    _floor_strip(sc, is_floor, [(-1, 0), (-2, 0), (0, -1), (-1, -1)], 'slate1', 'slate0', grout_rows=(T - 1,),
                 grout_cols=(T - 1,))
    return sc.cv


def wall_edge_r(v: int) -> Canvas:
    mir = [r.mirrored() for r in copies(EDGE_SHARED, (0,), TORUS) + EDGE_INNER[v]]
    rocks = _frame_right_of(T) + mir
    sc = render(rocks, 0, 0, T, T)

    def is_floor(x: int, y: int) -> bool:
        r = sc.owner[y][x]
        return x <= T - 1 - FLOOR_X and (r is None or r.layer < 2)
    # Свет слева падает на губу правой стены: пол у неё светлый, только контактная тень 1 px.
    _floor_strip(sc, is_floor, [(1, 0), (0, -1)], 'slate2', 'slate1', grout_rows=(T - 1,))
    return sc.cv


# ── основание задней стены (строка −1) и грунт под полем ──
TOP_EDGE_SHARED: List[Rock] = [Rock(0.0, 22.0, 6.5, 6.0, base=4, layer=2, spark=[(-3, -3)])]
TOP_EDGE_INNER: List[Rock] = [Rock(14.0, 19.5, 9.5, 8.5, base=4, layer=2, spark=[(-5, -5)], crack=[(2, -7), (3, -3)]),
                              Rock(24.5, 24.0, 4.5, 4.0, base=4, layer=2),
                              Rock(20.5, 27.0, 2.5, 2.0, base=4, layer=3),
                              Rock(23.0, 11.0, 5.5, 5.0, base=3, layer=1)]
TOP_FLOOR_Y = 28


def _frame_above(y_edge: int) -> List[Rock]:
    return copies(SIDE_FRAME + TOP_INNER, TORUS, (y_edge - T,)) + \
        [r for r in copies(SIDE_FRAME, TORUS, (y_edge,)) if r.cy - r.ry < y_edge + 1]


def _frame_below(y_edge: int) -> List[Rock]:
    return copies(SIDE_FRAME + SIDE_INNER[0], TORUS, (y_edge,)) + \
        [r for r in copies(SIDE_FRAME, TORUS, (y_edge - T,)) if r.cy + r.ry > y_edge - 1]


def wall_top_edge() -> Canvas:
    rocks = _frame_above(0) + copies(TOP_EDGE_SHARED, TORUS) + TOP_EDGE_INNER
    sc = render(rocks, 0, 0, T, T)

    def is_floor(x: int, y: int) -> bool:
        r = sc.owner[y][x]
        return y >= TOP_FLOOR_Y and (r is None or r.layer < 2)
    _floor_strip(sc, is_floor, [(0, -1), (0, -2), (-1, 0), (-1, -1)], 'slate1', 'slate0', grout_rows=(T - 1,),
                 grout_cols=(T - 1,))
    return sc.cv


BOTTOM_SHARED: List[Rock] = [Rock(31.5, 12.5, 6.0, 7.0, base=4, layer=2, spark=[(-3, -4)])]
BOTTOM_INNER: List[Rock] = [Rock(14.0, 13.5, 10.0, 9.5, base=4, layer=2, spark=[(-6, -5)], crack=[(1, -8), (2, -4)]),
                            Rock(25.0, 8.5, 3.5, 3.5, base=4, layer=3),
                            Rock(23.0, 23.0, 6.0, 5.0, base=3, layer=1),
                            Rock(5.0, 6.5, 2.5, 2.0, base=4, layer=3)]
BOTTOM_FLOOR_Y = 7


def wall_bottom() -> Canvas:
    rocks = _frame_below(T) + copies(BOTTOM_SHARED, TORUS) + BOTTOM_INNER
    sc = render(rocks, 0, 0, T, T)

    def is_floor(x: int, y: int) -> bool:
        r = sc.owner[y][x]
        return y <= BOTTOM_FLOOR_Y and (r is None or r.layer < 2)
    # Валуны перед полом: над ними на полу — только контактная тень.
    _floor_strip(sc, is_floor, [(0, 1)], 'slate3', 'slate1', grout_cols=(T - 1,))
    return sc.cv


# ── углы рамки: стыкуют губу боковой стены с основанием задней стены / грунтом ──
# Каждый угол собран из тех же общих валунов, что и его соседи, поэтому все четыре края совпадают:
# tl/tr — строка −1 у столбцов −1/7 (сосед справа/слева — wall_top_edge, снизу — wall_edge_*, сверху — wall_top);
# bl/br — строка Depth (сосед справа/слева — wall_bottom, сверху — wall_edge_*, снизу — wall_side_*).
CORNER_INNER: Dict[str, List[Rock]] = {
    'tl': [Rock(15.0, 14.0, 10.0, 9.0, base=4, layer=2, spark=[(-6, -4)], crack=[(1, -8), (2, -4)]),
           Rock(26.0, 25.0, 3.0, 2.5, base=4, layer=3)],
    'tr': [Rock(17.0, 14.0, 10.0, 9.0, base=4, layer=2, spark=[(-5, -5)], moss=-0.55),
           Rock(6.0, 25.5, 3.0, 2.5, base=4, layer=3)],
    'bl': [Rock(15.0, 17.0, 10.5, 9.5, base=4, layer=2, spark=[(-6, -5)], crack=[(2, -8), (3, -4)]),
           Rock(27.0, 4.5, 2.5, 2.0, base=4, layer=3)],
    'br': [Rock(17.0, 17.0, 10.5, 9.5, base=4, layer=2, spark=[(-6, -5)]),
           Rock(5.0, 5.0, 2.5, 2.0, base=4, layer=3)],
}


def wall_corner(kind: str) -> Canvas:
    edge_r = [r.mirrored() for r in EDGE_SHARED]
    if kind == 'tl':
        rocks = _frame_above(0) + _frame_left_of(0) + copies(TOP_EDGE_SHARED, (T,)) + copies(EDGE_SHARED, (0,), (T,))
        floor = lambda x, y: x >= FLOOR_X and y >= TOP_FLOOR_Y  # noqa: E731
        dirs, lit, dark = [(-1, 0), (-2, 0), (0, -1), (0, -2), (-1, -1)], 'slate1', 'slate0'
    elif kind == 'tr':
        rocks = _frame_above(0) + _frame_right_of(T) + copies(TOP_EDGE_SHARED, (0,)) + copies(edge_r, (0,), (T,))
        floor = lambda x, y: x <= T - 1 - FLOOR_X and y >= TOP_FLOOR_Y  # noqa: E731
        dirs, lit, dark = [(0, -1), (0, -2), (1, 0)], 'slate1', 'slate0'
    elif kind == 'bl':
        rocks = _frame_below(T) + _frame_left_of(0) + copies(EDGE_SHARED, (0,), (0,)) + copies(BOTTOM_SHARED, (0,))
        floor = lambda x, y: x >= FLOOR_X and y <= BOTTOM_FLOOR_Y  # noqa: E731
        dirs, lit, dark = [(-1, 0), (-2, 0), (0, 1)], 'slate1', 'slate0'
    else:
        rocks = _frame_below(T) + _frame_right_of(T) + copies(edge_r, (0,), (0,)) + copies(BOTTOM_SHARED, (-T,))
        floor = lambda x, y: x <= T - 1 - FLOOR_X and y <= BOTTOM_FLOOR_Y  # noqa: E731
        dirs, lit, dark = [(1, 0), (0, 1)], 'slate2', 'slate1'
    sc = render(rocks + CORNER_INNER[kind], 0, 0, T, T)

    def is_floor(x: int, y: int) -> bool:
        r = sc.owner[y][x]
        return floor(x, y) and (r is None or r.layer < 2)
    _floor_strip(sc, is_floor, dirs, lit, dark, grout_rows=(T - 1,), grout_cols=(T - 1,))
    return sc.cv


# ───────────────────────── Пол ─────────────────────────
# Плита 31×31 (x, y = 0…30) с фаской, затирка 1 px справа и снизу (x = 31, y = 31): у соседних плит шов
# получается одинарным, как в концепте. Узлы затирки — «крестик» navy1 на перекрестье.
GROUT = 'navy2'
KNOT = 'navy1'


def floor_base() -> Canvas:
    cv = Canvas(T, T)
    cv.rect(0, 0, T, T, GROUT)
    cv.rect(0, 0, T - 1, T - 1, 'slate4')
    cv.hline(0, T - 3, 0, 'slate5')
    cv.vline(0, 0, T - 3, 'slate5')
    cv.hline(1, T - 2, T - 2, 'slate3')
    cv.vline(T - 2, 1, T - 2, 'slate3')
    for x, y in ((T - 1, T - 1), (T - 2, T - 1), (T - 1, T - 2), (0, T - 1), (T - 1, 0)):
        cv.set(x, y, KNOT)
    return cv


def _pts(cv: Canvas, pts: Sequence[Tuple[int, int]], c: str) -> None:
    for x, y in pts:
        cv.set(x, y, c)


def _stain(cv: Canvas, x: int, y: int, shape: Sequence[str]) -> None:
    """Пятно-потёртость на грани плиты: '#' — slate3, '+' — slate5 (блик), только поверх грани slate4."""
    face = pxl.PALETTE['slate4']
    for j, row in enumerate(shape):
        for i, ch in enumerate(row):
            if ch in '#+' and cv.get(x + i, y + j)[:3] == face:
                cv.set(x + i, y + j, 'slate3' if ch == '#' else 'slate5')


def _dent(cv: Canvas, x: int, y: int) -> None:
    """Выбоинка 2×2: тень сверху-слева, свет снизу-справа (свет сверху-слева)."""
    cv.set(x, y, 'slate2')
    cv.set(x + 1, y, 'slate3')
    cv.set(x, y + 1, 'slate3')
    cv.set(x + 1, y + 1, 'slate5')


def floor_tile(v: int) -> Canvas:
    """Варианты отличаются мелочами (выбоинка, камешек, трещина, скол), чтобы сетка оставалась спокойной."""
    cv = floor_base()
    if v == 0:
        # Самая чистая плита — основной вариант (чаще остальных).
        _stain(cv, 18, 18, ['.##.', '####', '.###'])
    elif v == 1:
        _stain(cv, 5, 6, ['.##', '###', '##.'])
        # Камешек: блик сверху-слева, тень снизу-справа и падающая тень на плиту.
        peb = Canvas.from_ascii([
            '.hs.',
            'hssd',
            '.ddx',
            '..xx',
        ], {'h': 'slate6', 's': 'slate5', 'd': 'slate3', 'x': 'slate2'})
        cv.blit(peb, 21, 20)
        cv.set(18, 23, 'slate5')
        cv.set(19, 24, 'slate3')
    elif v == 2:
        # Волосяная трещина от правого края.
        _pts(cv, [(29, 9), (28, 10), (27, 10), (26, 11), (25, 12), (24, 12), (23, 13), (22, 14)], 'slate2')
        _pts(cv, [(28, 11), (26, 12), (24, 13)], 'slate5')
        _stain(cv, 6, 19, ['###.', '.###'])
    else:
        # Сколотый левый нижний угол.
        _pts(cv, [(0, 26), (0, 27), (1, 27), (0, 28), (1, 28), (2, 28), (0, 29), (1, 29), (2, 29), (3, 29),
                  (0, 30), (1, 30), (2, 30), (3, 30), (4, 30)], 'slate2')
        _pts(cv, [(0, 25), (1, 26), (2, 27), (3, 28), (4, 29), (5, 30)], 'slate3')
        _pts(cv, [(1, 25), (2, 26), (3, 27)], 'slate5')
        _stain(cv, 16, 6, ['.###', '###.', '.#..'])
        _dent(cv, 22, 21)
    return cv


def floor_gate() -> Canvas:
    """Порог строки ворот: два ряда тёмной брусчатки вразбежку (по 16 px) — бесшовно по горизонтали."""
    cv = Canvas(T, T)
    cv.rect(0, 0, T, T, GROUT)
    for y0, hh, off in ((0, 15, 0), (16, 15, 8)):
        for k in range(-1, 3):
            x0 = k * 16 + off
            for xx in range(x0, x0 + 15):
                if not 0 <= xx < T:
                    continue
                for yy in range(y0, y0 + hh):
                    cv.set(xx, yy, 'slate3')
                cv.set(xx, y0, 'slate4')
                cv.set(xx, y0 + hh - 1, 'slate2')
            if 0 <= x0 < T:
                cv.vline(x0, y0, y0 + hh - 2, 'slate4')
            if 0 <= x0 + 14 < T:
                cv.vline(x0 + 14, y0 + 1, y0 + hh - 1, 'slate2')
    # Потёртости от колёс: светлые царапины.
    _pts(cv, [(5, 6), (6, 6), (7, 7), (21, 5), (22, 5), (12, 22), (13, 22), (14, 23), (27, 21), (28, 21)], 'slate4')
    _pts(cv, [(9, 10), (25, 10), (4, 25), (19, 26)], 'slate2')
    for x, y in ((T - 1, T - 1), (T - 1, 15)):
        cv.set(x, y, KNOT)
    return cv


# ───────────────────────── Метка клетки мержа ─────────────────────────
def slot_marker(color: str = 'navy8', a: int = 160) -> Canvas:
    """Пунктирный скруглённый квадрат с «+», как пустые ячейки внизу концепта.

    Отступ 3 px от грани плиты (плита x, y = 0…30, центр — пиксель 15), линии 1 px.
    """
    cv = Canvas(T, T)
    c = pxl.with_alpha(color, a)
    x0, y0, x1, y1 = 3, 3, 27, 27
    for (cx, cy, sx, sy) in ((x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)):
        for dx, dy in ((2, 0), (3, 0), (1, 1), (0, 2), (0, 3)):
            cv.set(cx + sx * dx, cy + sy * dy, c)
    # Прямые участки по 17 px: штрихи по 3 через 2.
    pattern = [0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0]
    for k, on in enumerate(pattern):
        if on:
            cv.set(x0 + 4 + k, y0, c)
            cv.set(x0 + 4 + k, y1, c)
            cv.set(x0, y0 + 4 + k, c)
            cv.set(x1, y0 + 4 + k, c)
    cv.hline(11, 19, 15, c)
    cv.vline(15, 11, 19, c)
    return cv


# ───────────────────────── Декор ─────────────────────────
WOOD = {'o': 'wood0', 'd': 'wood1', 's': 'wood2', 'm': 'wood3', 'b': 'wood4', 'l': 'wood5', 'h': 'wood6', 'w': 'wood7'}
# Железо (фонари, цепи, обручи): нейтральный stone — отделяется от синих стен.
IRON = {'O': 'ink1', 'I': 'stone1', 'J': 'stone2', 'K': 'stone3', 'L': 'stone5', 'M': 'stone6'}


def _legend(*ds: Dict[str, str], **extra: str) -> Dict[str, str]:
    out: Dict[str, str] = {}
    for d in ds:
        out.update(d)
    out.update(extra)
    return out


def _pad(cv: Canvas, k: int) -> Canvas:
    out = Canvas(cv.w + 2 * k, cv.h + 2 * k)
    out.blit(cv, k, k)
    return out


def _outlined(cv: Canvas, c: str) -> Canvas:
    """Поле 1 px и контур цветом c вокруг формы (контур не упирается в край холста)."""
    out = _pad(cv, 1)
    out.outline(c)
    return out


# Стойка крепи: опора 8 px, оголовок, кронштейн вправо с крюком для фонаря.
BEAM_W, BEAM_H = 20, 50
BEAM_POST_CX = 6          # центр стойки до поля контура (pivot по X = BEAM_POST_CX + 1)
BEAM_HOOK = (16, 14)      # низ крюка до поля контура (x, y от верха); на готовом холсте — (17, 15)


def beam_post() -> Canvas:
    w, h = BEAM_W, BEAM_H
    cv = Canvas(w, h)
    x0 = BEAM_POST_CX - 4
    # Стойка.
    cv.rect(x0, 5, 8, h - 5, 'wood3')
    cv.vline(x0, 5, h - 1, 'wood5')
    cv.vline(x0 + 1, 5, h - 1, 'wood4')
    cv.vline(x0 + 6, 5, h - 1, 'wood2')
    cv.vline(x0 + 7, 5, h - 1, 'wood1')
    for y in range(9, h - 3, 9):
        cv.vline(x0 + 3, y, y + 3, 'wood2')
        cv.set(x0 + 4, y + 5, 'wood4')
    cv.set(x0 + 4, 25, 'wood1')
    cv.set(x0 + 5, 25, 'wood2')
    cv.set(x0 + 4, 24, 'wood5')
    # Оголовок — торец поперечного бруса.
    cv.rect(x0 - 1, 0, 10, 6, 'wood4')
    cv.hline(x0 - 1, x0 + 8, 0, 'wood6')
    cv.vline(x0 - 1, 0, 5, 'wood5')
    cv.hline(x0, x0 + 8, 5, 'wood2')
    cv.vline(x0 + 8, 1, 5, 'wood2')
    cv.set(x0 + 2, 2, 'wood3')
    cv.set(x0 + 5, 3, 'wood3')
    # Кронштейн: горизонтальный брусок и укосина.
    cv.rect(x0 + 8, 8, 9, 3, 'wood4')
    cv.hline(x0 + 8, x0 + 16, 8, 'wood6')
    cv.hline(x0 + 8, x0 + 16, 10, 'wood2')
    for i in range(6):
        cv.set(x0 + 8 + i, 16 - i, 'wood3')
        cv.set(x0 + 8 + i, 17 - i, 'wood2')
        cv.set(x0 + 9 + i, 16 - i, 'wood4')
    # Крюк.
    hx, hy = BEAM_HOOK
    cv.vline(hx, 11, hy - 1, 'stone3')
    # Железные скобы с заклёпками.
    for y in (12, 40):
        cv.rect(x0, y, 8, 2, 'stone3')
        cv.hline(x0, x0 + 7, y, 'stone5')
        cv.set(x0 + 1, y + 1, 'stone1')
        cv.set(x0 + 6, y + 1, 'stone1')
    # Камень-подпятник.
    cv.rect(x0 - 1, h - 3, 10, 3, 'navy4')
    cv.hline(x0 - 1, x0 + 8, h - 3, 'navy6')
    cv.vline(x0 + 8, h - 2, h - 1, 'navy3')
    cv = _outlined(cv, 'wood0')
    # Крюк поверх контура — железный (координаты уже с полем 1 px).
    cv.set(hx + 1, hy + 1, 'stone2')
    cv.set(hx, hy + 1, 'stone2')
    return cv


def lantern(frame: int) -> Canvas:
    """Железный фонарь 10×17 с янтарным окошком; frame 1 — чуть притушенный (мерцание)."""
    rows = [
        '....OO....',
        '...O..O...',
        '...OKKO...',
        '..OKLLKO..',
        '.OKKKKKKO.',
        'OJJJJJJJJO',
        'OJ123321JO',
        'OK234432KO',
        'OK345543KO',
        'OK345543KO',
        'OK234432KO',
        'OJ123321JO',
        'OJJJJJJJJO',
        '.OKKKKKKO.',
        '..OJJJJO..',
        '...OIIO...',
        '....OO....',
    ]
    if frame == 0:
        glow = {'1': 'amber1', '2': 'amber2', '3': 'amber3', '4': 'amber4', '5': 'amber5'}
    else:
        glow = {'1': 'amber1', '2': 'amber1', '3': 'amber2', '4': 'amber3', '5': 'amber4'}
    cv = Canvas.from_ascii(rows, _legend(IRON, glow))
    # Перекладины окошка (тень от рамки) — по центру.
    for y in (6, 11):
        cv.set(4, y, 'stone2')
        cv.set(5, y, 'stone2')
    cv.set(2, 6, 'stone5')
    return cv


_PURPLES = {pxl.PALETTE[f'purple{i}'] for i in range(2, 7)}


def crystal_cluster(v: int) -> Canvas:
    """Друза фиолетовых кристаллов на камне, 16×16: левая грань светлая, правая тёмная, кончики острые."""
    cv = Canvas(16, 16)
    base = Canvas(16, 16)
    base.ellipse(8, 13.5, 7.0, 3.0, 'navy4')
    base.shade('navy6', 'navy3', 'navy4')
    cv.blit(base, 0, 0)

    def crystal(cx: int, top: int, bottom: int, half: int, lean: int) -> None:
        for y in range(top, bottom + 1):
            t = y - top
            sh = (lean * (bottom - y) + (bottom - top) // 2) // max(1, bottom - top)
            span = min(half, t)
            for xx in range(cx - span, cx + span):
                col = 'purple5' if xx < cx else 'purple3'
                if xx == cx - span:
                    col = 'purple6' if t < 3 else 'purple5'
                if xx == cx and t >= 2:
                    col = 'purple4'
                cv.set(xx + sh, y, col)

    if v == 0:
        crystal(5, 5, 12, 2, -2)
        crystal(12, 7, 12, 2, 2)
        crystal(9, 1, 12, 3, 0)
    else:
        crystal(5, 3, 12, 3, -1)
        crystal(11, 6, 12, 2, 1)
        crystal(14, 9, 12, 1, 1)
    cv.set(8 if v == 0 else 4, 3 if v == 0 else 5, 'purple6')
    cv = _outlined(cv, 'purple1')
    # Контур основания — тёмный камень, а не фиолет.
    for y in range(13, cv.h):
        for x in range(cv.w):
            if cv.get(x, y)[:3] == pxl.PALETTE['purple1'] and not (cv.opaque(x, y - 1) and cv.get(x, y - 1)[:3] in _PURPLES):
                cv.set(x, y, 'navy1')
    return cv


def banner_paw() -> Canvas:
    """Знамя на древке: синее полотнище (насыщеннее камня — читается на стене) с золотой каймой и кремовой лапкой."""
    w, h = 14, 28
    cv = Canvas(w, h)
    cv.rect(6, 2, 2, h - 2, 'wood3')
    cv.vline(6, 2, h - 1, 'wood5')
    cloth = Canvas(w, h)
    cloth.rect(2, 3, 10, 15, 'blue2')
    for i in range(4):
        cloth.hline(2, 6 - i, 18 + i, 'blue2')
        cloth.hline(7 + i, 11, 18 + i, 'blue2')
    # Кайма по контуру полотнища (кроме верха), затем складки.
    cloth.fill_where(lambda x, y: y > 3 and any(not cloth.opaque(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1))),
                     'gold2')
    cloth.fill_where(lambda x, y: x == 3 and 4 <= y <= 18, 'blue3')
    cloth.fill_where(lambda x, y: x == 4 and 5 <= y <= 16 and y % 3 == 0, 'blue3')
    cloth.fill_where(lambda x, y: x == 10 and 4 <= y <= 18, 'blue1')
    cv.blit(cloth, 0, 0)
    cv.hline(2, 11, 3, 'blue1')
    paw = Canvas.from_ascii([
        '.c.c.',
        'c...c',
        '.ccc.',
        'ccccc',
        '.ccc.',
    ], {'c': 'cream3'})
    cv.blit(paw, 4, 9)
    cv.set(4, 13, 'cream2')
    cv.rect(0, 1, w, 2, 'wood4')
    cv.hline(0, w - 1, 1, 'wood6')
    cv.set(w - 1, 2, 'wood2')
    cv.set(0, 1, 'wood5')
    return _outlined(cv, 'ink2')


def barrel() -> Canvas:
    rows = [
        '..oooooooo..',
        '.odmmmmmmdo.',
        'odssmmmmssdo',
        'odbbbbbbbbdo',
        'oOKKKKKKKJOo',
        'ohlbmbbmbsmo',
        'ohlbmbbmbsmo',
        'ohlbmbbmbsmo',
        'oOKKKKKKKJOo',
        'ohlbmbbmbsmo',
        'oblbmbbmbsmo',
        '.oddddddddo.',
        '..oooooooo..',
    ]
    cv = Canvas.from_ascii(rows, _legend(WOOD, IRON))
    cv.set(3, 4, 'stone5')
    cv.set(3, 8, 'stone5')
    return cv


def crate() -> Canvas:
    rows = [
        'oooooooooooo',
        'ohhhhhhhhhlo',
        'olbbbbbbbbmo',
        'oooooooooooo',
        'ohwlllllllbo',
        'olhmbbbbblmo',
        'olbhmbbbbbmo',
        'olbbhmbbbbmo',
        'olbbbhmbbbmo',
        'olbbbbhmbbmo',
        'olbbbbbhmbmo',
        'obmmmmmmhmso',
        'oooooooooooo',
    ]
    cv = Canvas.from_ascii(rows, WOOD)
    for x, y, c in ((1, 4, 'stone5'), (10, 4, 'stone3'), (1, 11, 'stone3'), (10, 11, 'stone3')):
        cv.set(x, y, c)
    return cv


def sack() -> Canvas:
    """Мешок из мешковины, перевязанный верёвкой, 12×14."""
    rows = [
        '.....oo.....',
        '....oyzo....',
        '....oyyo....',
        '...oommoo...',
        '....oyyo....',
        '...oyzzyo...',
        '..oyzzyyyo..',
        '.oyzzyyyyxo.',
        '.oyzyyyyyxo.',
        'oyzyyyyyyyxo',
        'oyyyyyyyyxxo',
        'oyyyyyyyxxxo',
        '.oxxxxxxxxo.',
        '..oooooooo..',
    ]
    cv = Canvas.from_ascii(rows, {'o': 'wood1', 'm': 'wood4', 'x': 'cream0', 'y': 'cream1', 'z': 'cream2'})
    # Заплатка.
    _pts(cv, [(6, 8), (7, 8), (6, 9), (7, 9)], 'cream0')
    cv.set(6, 8, 'wood5')
    return cv


def minecart_ore() -> Canvas:
    """Вагонетка 22×18: железный обод и уголки, дощатые борта, горка золотой руды, два колеса."""
    w, h = 22, 18
    cv = Canvas(w, h)
    ore = Canvas(w, h)
    ore.ellipse(11, 7, 8.5, 4.5, 'gold2')
    ore.shade('gold4', 'gold1', 'gold2')
    for x, y in ((6, 5), (10, 3), (14, 4), (9, 6), (16, 6), (12, 7)):
        ore.set(x, y, 'gold5')
        ore.set(x + 1, y, 'gold4')
        ore.set(x, y + 1, 'gold3')
    ore.outline('gold0')
    cv.blit(ore, 0, 0)
    for y in range(7, 15):
        inset = (y - 7) // 4
        cv.hline(1 + inset, w - 2 - inset, y, 'wood3')
    for y in (10, 13):
        cv.hline(2, w - 3, y, 'wood2')
    cv.hline(1, w - 2, 7, 'stone5')
    cv.hline(1, w - 2, 8, 'stone3')
    cv.hline(2, w - 3, 14, 'stone2')
    cv.vline(2, 9, 13, 'wood5')
    cv.vline(3, 9, 13, 'wood4')
    cv.vline(w - 4, 9, 13, 'wood2')
    for x in (6, 15):
        cv.vline(x, 8, 14, 'stone3')
        cv.set(x, 9, 'stone6')
        cv.set(x, 12, 'stone1')
    for x in (5, 15):
        cv.ellipse(x + 0.5, 15.5, 2.5, 2.5, 'stone1')
        cv.set(x, 15, 'stone4')
        cv.set(x + 1, 15, 'stone3')
        cv.set(x, 14, 'stone2')
    return _outlined(cv, 'ink2')


def rails_v() -> Canvas:
    """Рельсы 16×32 вверх по экрану: шпалы через 8 px, бесшовно по вертикали, фон прозрачный."""
    cv = Canvas(16, T)
    for y0 in (2, 10, 18, 26):
        cv.rect(1, y0, 14, 3, 'wood3')
        cv.hline(1, 14, y0, 'wood4')
        cv.hline(1, 14, y0 + 2, 'wood1')
        cv.set(1, y0 + 1, 'wood4')
        cv.set(14, y0 + 1, 'wood2')
        cv.set(6, y0 + 1, 'wood2')
    for x in (3, 11):
        cv.vline(x - 1, 0, T - 1, 'ink2')
        cv.vline(x, 0, T - 1, 'stone6')
        cv.vline(x + 1, 0, T - 1, 'stone3')
        cv.vline(x + 2, 0, T - 1, 'ink2')
    return cv


def mushrooms() -> Canvas:
    """Светящиеся бирюзовые грибы, 13×10."""
    rows = [
        '...oooo......',
        '..ohhgfo.....',
        '.ohgggffo....',
        '.offfffeo.oo.',
        '..oosso.oghfo',
        '....so..offeo',
        '....so...oso.',
        '....so...oso.',
        '..ossssoossso',
        '..ooooooooooo',
    ]
    return Canvas.from_ascii(rows, {'o': 'teal1', 'h': 'teal6', 'g': 'teal5', 'f': 'teal4', 'e': 'teal3', 's': 'cream1'})


def sign_question() -> Canvas:
    """Деревянная табличка на столбике с синим «?», 14×18."""
    w, h = 14, 18
    cv = Canvas(w, h)
    cv.rect(6, 9, 2, 9, 'wood3')
    cv.vline(6, 9, h - 1, 'wood4')
    cv.rect(0, 0, w, 10, 'wood5')
    cv.hline(0, w - 1, 0, 'wood6')
    cv.vline(0, 0, 9, 'wood6')
    cv.hline(1, w - 1, 9, 'wood3')
    cv.vline(w - 1, 1, 9, 'wood4')
    cv.hline(1, w - 2, 5, 'wood4')
    q = Canvas.from_ascii([
        '.qqq.',
        'qq.qq',
        '...qq',
        '..qq.',
        '.....',
        '..qq.',
    ], {'q': 'navy3'})
    cv.blit(q, 4, 2)
    cv.set(1, 1, 'stone4')
    cv.set(w - 2, 1, 'stone4')
    return _outlined(cv, 'wood0')


def chain_hang() -> Canvas:
    """Висящая цепь 4×24: звенья анфас (кольцо 4×4) и в профиль (планка 2×2) по очереди; сталь светлее стены."""
    rows = []
    for _ in range(4):
        rows += ['.LK.', 'L..J', 'K..J', '.JI.', '.LJ.', '.KI.']
    return Canvas.from_ascii(rows[:24], {'L': 'stone6', 'K': 'stone4', 'J': 'stone3', 'I': 'stone2'})


# ───────────────────────── Регистрация ─────────────────────────
def build(reg: pxl.Registry) -> None:
    for v in range(4):
        reg.add(f'floor_{v}', floor_tile(v), extrude=True)
    reg.add('floor_gate', floor_gate(), extrude=True)
    reg.add('slot_marker', slot_marker(), alpha=True)
    reg.add('slot_marker_hi', slot_marker('gold4', 210), alpha=True, note='клетка мержа под перетаскиваемым котиком')
    for v in range(4):
        reg.add(f'wall_side_{v}', wall_side(v), extrude=True)
    for v in range(2):
        reg.add(f'wall_edge_l_{v}', wall_edge_l(v), extrude=True)
        reg.add(f'wall_edge_r_{v}', wall_edge_r(v), extrude=True)
    reg.add('wall_top', wall_top(0), extrude=True)
    for v in range(1, 4):
        reg.add(f'wall_top_{v}', wall_top(v), extrude=True, note='доп. варианты задней стены (стыкуются с wall_top)')
    reg.add('wall_top_edge', wall_top_edge(), extrude=True)
    reg.add('wall_bottom', wall_bottom(), extrude=True)
    for k in ('tl', 'tr', 'bl', 'br'):
        reg.add(f'wall_corner_{k}', wall_corner(k), extrude=True, note='угол рамки поля (см. отчёт env)')

    def feet(name: str, cv: Canvas, **kw) -> None:
        reg.add(name, cv, pivot=pxl.pivot_feet(cv), **kw)

    reg.add('beam_post', beam_post(), pivot=(BEAM_POST_CX + 1.0, 0.0))
    feet('lantern_0', lantern(0))
    feet('lantern_1', lantern(1))
    feet('crystal_cluster_a', crystal_cluster(0))
    feet('crystal_cluster_b', crystal_cluster(1))
    feet('banner_paw', banner_paw())
    feet('barrel', barrel())
    feet('crate', crate())
    feet('sack', sack())
    feet('minecart_ore', minecart_ore())
    reg.add('rails_v', rails_v(), extrude=True)
    feet('mushrooms', mushrooms())
    feet('sign_question', sign_question())
    feet('chain_hang', chain_hang())
