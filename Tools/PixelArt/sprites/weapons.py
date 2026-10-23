"""Оружие котика-шахтёра (Docs/Visual/ART_DIRECTION.md, «Котики»): восемь видов по уровню внутри круга из 8 уровней.

  1 pick      кирка                  — классический замах (кадры mine_*)
  2 hammer    молот                  — замах
  3 crusher   дробилка (барабан с зубьями на рукояти) — замах, барабан проворачивается
  4 saw       циркулярная пила       — работа «с упора» (кадры tool_*), искры у диска
  5 drill     бур-машина             — с упора, пыль у сверла
  6 dualpick  две кирки              — замах обеими
  7 chainsaw  бензопила              — с упора, искры у шины
  8 laser     лазерган               — с упора, луч и вспышка

Оружие рисуется кодом по оси кадра (Pose.pick: торец рукояти → верх под головкой) на холсте котика 40×40: для каждого
пикселя считаются координаты вдоль оси (s) и поперёк (t, «вверх» относительно оружия), формы задаются в этих
координатах. Свет — сверху-слева по экрану: у каждой части пиксели на верхней/левой кромке светлее, на нижней/правой
темнее; общий контур — тёмный тон. Лапки (рисуются в слое тела) вырезаются, поэтому лапки держат рукоять поверх неё.
"""
from __future__ import annotations

import math
from typing import Callable, Dict, List, Optional, Sequence, Set, Tuple

from pxl import Canvas

CW, CH = 40, 40
Mask = Set[Tuple[int, int]]

WEAPONS = ['pick', 'hammer', 'crusher', 'saw', 'drill', 'dualpick', 'chainsaw', 'laser']
# Работают «с упора»: свои кадры добычи tool_* (держат инструмент перед собой), без замаха над головой.
TOOLS = {'saw', 'drill', 'chainsaw', 'laser'}
# Кадры с эффектом инструмента (искры, пыль, луч).
FIRE_FRAMES = {'tool_strike', 'tool_impact'}

# (основа, свет, тень) — тона палитры по материалам.
STEEL = ('stone5', 'stone7', 'stone3')
STEEL_LIGHT = ('stone6', 'cream4', 'stone4')
DARK_STEEL = ('stone3', 'stone5', 'stone1')
WOOD = ('wood4', 'wood6', 'wood2')
RUBBER = ('ink3', 'navy3', 'ink1')
RED = ('red3', 'red4', 'red2')
YELLOW = ('gold3', 'gold4', 'gold1')
ORANGE = ('amber2', 'amber3', 'amber1')
WHITE = ('cream3', 'cream4', 'cream1')
OUTLINE = 'ink1'


class Frame:
    """Локальная система оружия: начало — торец рукояти, u — вдоль оружия, n — «вверх» (поворот u на −90° по экрану)."""

    def __init__(self, butt: Tuple[float, float], top: Tuple[float, float]):
        self.bx, self.by = butt[0] + 0.5, butt[1] + 0.5
        dx, dy = top[0] - butt[0], top[1] - butt[1]
        d = math.hypot(dx, dy) or 1.0
        self.ux, self.uy = dx / d, dy / d
        self.nx, self.ny = self.uy, -self.ux
        self.axis = d

    def st(self, x: int, y: int) -> Tuple[float, float]:
        qx, qy = x + 0.5 - self.bx, y + 0.5 - self.by
        return qx * self.ux + qy * self.uy, qx * self.nx + qy * self.ny


Shape = Callable[[float, float], bool]


def rect(s0: float, s1: float, t0: float, t1: float) -> Shape:
    return lambda s, t: s0 <= s <= s1 and t0 <= t <= t1


def disc(sc: float, tc: float, r: float) -> Shape:
    return lambda s, t: (s - sc) ** 2 + (t - tc) ** 2 <= r * r


def mask_of_shape(fr: Frame, shape: Shape) -> Mask:
    m: Mask = set()
    for y in range(CH):
        for x in range(CW):
            s, t = fr.st(x, y)
            if shape(s, t):
                m.add((x, y))
    return m


def paint_part(cv: Canvas, m: Mask, ramp: Tuple[str, str, str]) -> None:
    """Часть оружия: основа, свет на верхней/левой кромке, тень на нижней/правой (свет сверху-слева по экрану)."""
    base, light, dark = ramp
    for x, y in m:
        up, left = (x, y - 1) not in m, (x - 1, y) not in m
        down, right = (x, y + 1) not in m, (x + 1, y) not in m
        c = base
        if up or left:
            c = light
        elif down or right:
            c = dark
        cv.set(x, y, c)


def assemble(parts: Sequence[Tuple[Mask, Tuple[str, str, str]]], outline: str = OUTLINE) -> Canvas:
    cv = Canvas(CW, CH)
    for m, ramp in parts:
        paint_part(cv, m, ramp)
    cv.outline(outline)
    return cv


def tool_length(fr: Frame, lo: float = 11.0, hi: float = 17.0) -> float:
    """Длина инструмента: по оси кадра (ракурс), но в пределах lo…hi пикселей."""
    return max(lo, min(hi, fr.axis + 2.0))


# ───────────────────────── ударное оружие (замах) ─────────────────────────
def hammer(fr: Frame, frame: str) -> Canvas:
    L = fr.axis
    handle = mask_of_shape(fr, rect(0, L - 1.5, -0.6, 0.6))
    band = mask_of_shape(fr, rect(L - 2.6, L - 1.4, -1.4, 1.4))
    head = mask_of_shape(fr, rect(L - 1.6, L + 2.6, -4.6, 4.6))
    face = mask_of_shape(fr, rect(L - 1.6, L + 2.6, -4.6, -3.4)) | mask_of_shape(fr, rect(L - 1.6, L + 2.6, 3.4, 4.6))
    return assemble([(handle, WOOD), (band, YELLOW), (head - face, STEEL), (face, DARK_STEEL)])


def crusher(fr: Frame, frame: str, phase: int) -> Canvas:
    """Дробилка: стальная рукоять, жёлтый барабан на оси с восемью стальными зубьями; барабан проворачивается по кадрам."""
    L = fr.axis
    sc = L - 1.5
    handle = mask_of_shape(fr, rect(0, sc - 3.0, -0.7, 0.7))
    yoke = mask_of_shape(fr, rect(sc - 3.6, sc - 2.4, -2.6, 2.6))
    drum = mask_of_shape(fr, disc(sc, 0, 4.0))
    rot = phase * math.pi / 8

    def tooth(s: float, t: float) -> bool:
        r = math.hypot(s - sc, t)
        if not 3.6 <= r <= 5.9:
            return False
        a = (math.atan2(t, s - sc) - rot) % (math.pi / 4)
        return min(a, math.pi / 4 - a) < 0.26 * (6.2 - r) / 2.4
    teeth = mask_of_shape(fr, tooth)
    axle = mask_of_shape(fr, disc(sc, 0, 1.2))
    return assemble([(handle, DARK_STEEL), (yoke, DARK_STEEL), (teeth, STEEL_LIGHT), (drum, YELLOW), (axle, DARK_STEEL)])


# ───────────────────────── инструменты (с упора) ─────────────────────────
def saw(fr: Frame, frame: str, phase: int) -> Canvas:
    """Циркулярная пила: чёрная задняя ручка, красный корпус с передней ручкой, большой стальной диск с тёмными зубьями
    спереди; красный кожух закрывает только верхнюю заднюю четверть диска. Диск проворачивается по кадрам."""
    L = tool_length(fr, 12.0, 17.0)
    bc = (L - 4.6, -0.6)
    R = 5.2
    grip = mask_of_shape(fr, rect(0, 4.2, -1.0, 1.0))
    body = mask_of_shape(fr, rect(3.4, bc[0] - 1.0, -1.4, 2.2))
    knob = mask_of_shape(fr, disc(bc[0] - 2.4, 3.2, 1.3))
    blade = mask_of_shape(fr, disc(bc[0], bc[1], R))
    guard = mask_of_shape(fr, lambda s, t: (s - bc[0]) ** 2 + (t - bc[1]) ** 2 <= (R + 0.7) ** 2
                          and t >= bc[1] + 1.4 and s <= bc[0] + 0.8)
    rot = phase * 0.4

    def teeth_fn(s: float, t: float) -> bool:
        r = math.hypot(s - bc[0], t - bc[1])
        if not R - 1.3 <= r <= R:
            return False
        a = (math.atan2(t - bc[1], s - bc[0]) - rot) % (math.pi / 5)
        return a < math.pi / 10
    teeth = mask_of_shape(fr, teeth_fn)
    hub = mask_of_shape(fr, disc(bc[0], bc[1], 1.3))
    blade = blade - guard
    cv = assemble([(grip, RUBBER), (body, RED), (knob, RUBBER), (blade - teeth - hub, STEEL_LIGHT),
                   ((teeth & blade), DARK_STEEL), (guard, RED), (hub, YELLOW)])
    if frame in FIRE_FRAMES:
        sparks(cv, fr, bc[0] + R * 0.7, bc[1] - R * 0.8, phase)
    return cv


def drill(fr: Frame, frame: str, phase: int) -> Canvas:
    """Бур-машина: жёлтый корпус с пистолетной рукоятью, патрон и длинное спиральное сверло."""
    L = tool_length(fr, 12.0, 17.0)
    grip = mask_of_shape(fr, rect(1.0, 3.6, -4.6, -0.2))
    body = mask_of_shape(fr, rect(0, 8.2, -0.4, 3.4))
    chuck = mask_of_shape(fr, rect(8.0, 10.0, -1.5, 1.9))
    bit_shape = rect(10.0, L + 1.0, -0.9, 1.3)
    bit = mask_of_shape(fr, bit_shape)
    tip = mask_of_shape(fr, lambda s, t: L + 1.0 < s <= L + 2.6 and abs(t - 0.2) <= 1.1 - (s - L - 1.0) * 0.6)
    cv = assemble([(grip, RUBBER), (body, YELLOW), (chuck, DARK_STEEL), (bit | tip, STEEL)])
    # Спираль сверла: тёмные диагональные полосы, сдвигаются по кадрам (сверло крутится).
    for x, y in bit:
        s, t = fr.st(x, y)
        if (s + t * 0.9 + phase * 0.7) % 2.4 < 0.9:
            cv.set(x, y, 'stone3')
    if frame in FIRE_FRAMES:
        dust(cv, fr, L + 2.8, 0.2, phase)
    return cv


def chainsaw(fr: Frame, frame: str, phase: int) -> Canvas:
    """Бензопила: оранжевый мотор с чёрной задней ручкой и дугой сверху, стальная шина с цепью."""
    L = tool_length(fr, 12.0, 17.5)
    grip = mask_of_shape(fr, rect(0, 2.8, -1.0, 1.0))
    engine = mask_of_shape(fr, rect(2.0, 7.8, -2.4, 2.8))
    loop = mask_of_shape(fr, lambda s, t: 3.0 <= s <= 7.2 and 2.6 <= t <= 4.6 and not (3.9 <= s <= 6.3 and t <= 3.7))
    bar = mask_of_shape(fr, lambda s, t: (7.4 <= s <= L and abs(t) <= 1.4) or (s - L) ** 2 + t * t <= 1.4 ** 2)
    cv = assemble([(grip, RUBBER), (loop, RUBBER), (engine, ORANGE), (bar, STEEL)])
    # Цепь: зубья по кромке шины через пиксель, бегут по кадрам.
    for x, y in bar:
        s, t = fr.st(x, y)
        if abs(t) >= 0.7 and int(s + phase) % 2 == 0:
            cv.set(x, y, 'stone2')
    if frame in FIRE_FRAMES:
        sparks(cv, fr, L + 1.6, 0.0, phase)
    return cv


def laser(fr: Frame, frame: str, phase: int) -> Canvas:
    """Лазерган: белый корпус с бирюзовой полосой и светящимся окошком, тёмная рукоять, ствол с кольцами, красный
    излучатель; в кадрах удара — луч и вспышка."""
    L = tool_length(fr, 12.0, 15.0)
    grip = mask_of_shape(fr, rect(1.0, 3.6, -4.4, -0.2))
    body = mask_of_shape(fr, rect(0, 9.2, -0.4, 3.0))
    stripe = mask_of_shape(fr, rect(1.5, 8.0, 1.0, 1.8))
    window = mask_of_shape(fr, disc(5.8, 1.3, 0.9))
    barrel = mask_of_shape(fr, rect(9.0, L - 1.0, -0.4, 1.6))
    rings = mask_of_shape(fr, lambda s, t: 9.0 <= s <= L - 1.0 and -0.4 <= t <= 1.6 and int(s) % 3 == 0)
    emitter = mask_of_shape(fr, rect(L - 1.2, L + 0.6, -0.9, 2.1))
    cv = assemble([(grip, ('navy2', 'navy4', 'navy1')), (body, WHITE), (barrel, DARK_STEEL), (emitter, ('red4', 'red5', 'red3'))])
    for x, y in stripe:
        cv.set(x, y, 'teal4')
    for x, y in window:
        cv.set(x, y, 'teal6')
    for x, y in rings & barrel:
        cv.set(x, y, 'stone5')
    if frame in FIRE_FRAMES:
        # Вспышка у излучателя и начало луча; само попадание игра показывает вспышкой на цели (Effects.Laser).
        beam(cv, fr, L + 0.8, 0.6, frame == 'tool_impact')
    return cv


# ───────────────────────── эффекты инструментов ─────────────────────────
def sparks(cv: Canvas, fr: Frame, s0: float, t0: float, phase: int) -> None:
    """Искры у режущей кромки: 4–5 ярких пикселей веером вперёд."""
    pts = [(0.0, 0.0), (1.5, 1.2), (2.4, -0.6), (1.2, -1.8), (3.2, 0.8)]
    for k, (ds, dt) in enumerate(pts):
        if (k + phase) % 5 == 4:
            continue
        s, t = s0 + ds, t0 + dt
        x = int(math.floor(fr.bx + s * fr.ux + t * fr.nx))
        y = int(math.floor(fr.by + s * fr.uy + t * fr.ny))
        if 0 <= x < CW and 0 <= y < CH and not cv.opaque(x, y):
            cv.set(x, y, 'gold5' if k % 2 == 0 else 'cream4')


def dust(cv: Canvas, fr: Frame, s0: float, t0: float, phase: int) -> None:
    """Каменная пыль у сверла: несколько светлых крупинок."""
    pts = [(0.0, 0.0), (1.0, 1.5), (1.6, -1.2), (2.8, 0.4), (0.6, -2.4)]
    for k, (ds, dt) in enumerate(pts):
        s, t = s0 + ds, t0 + dt
        x = int(math.floor(fr.bx + s * fr.ux + t * fr.nx))
        y = int(math.floor(fr.by + s * fr.uy + t * fr.ny))
        if 0 <= x < CW and 0 <= y < CH and not cv.opaque(x, y):
            cv.set(x, y, 'stone6' if k % 2 == 0 else 'cream2')


def beam(cv: Canvas, fr: Frame, s0: float, t0: float, long: bool) -> None:
    """Луч лазера: светлая сердцевина и красная кайма до края холста, у излучателя — вспышка."""
    length = 12.0 if long else 7.0
    for y in range(CH):
        for x in range(CW):
            if cv.opaque(x, y):
                continue
            s, t = fr.st(x, y)
            if s0 <= s <= s0 + length:
                d = abs(t - t0)
                if d <= 0.45:
                    cv.set(x, y, 'cream4')
                elif d <= 1.1:
                    cv.set(x, y, 'red4')
            elif (s - s0) ** 2 + (t - t0) ** 2 <= 2.2 ** 2:
                cv.set(x, y, 'pink3')


def draw(name: str, butt: Tuple[int, int], top: Tuple[int, int], frame: str, phase: int,
         pick_fn: Callable[[Tuple[int, int], Tuple[int, int]], Canvas]) -> Canvas:
    """Оружие name в кадре frame по оси butt → top; pick_fn рисует кирку (общая с прежней оснасткой cats.py)."""
    fr = Frame(butt, top)
    if name == 'pick':
        return pick_fn(butt, top)
    if name == 'dualpick':
        # Вторая кирка — в другой лапке: торец чуть левее и ниже, ось повёрнута на 20° «назад»; рисуется под первой.
        dx, dy = top[0] - butt[0], top[1] - butt[1]
        a = math.radians(-20)
        rx, ry = dx * math.cos(a) - dy * math.sin(a), dx * math.sin(a) + dy * math.cos(a)
        b2 = (butt[0] - 2, butt[1] + 1)
        t2 = (int(round(b2[0] + rx * 0.92)), int(round(b2[1] + ry * 0.92)))
        cv = pick_fn(b2, t2)
        cv.blit(pick_fn(butt, top), 0, 0)
        return cv
    if name == 'hammer':
        return hammer(fr, frame)
    if name == 'crusher':
        return crusher(fr, frame, phase)
    if name == 'saw':
        return saw(fr, frame, phase)
    if name == 'drill':
        return drill(fr, frame, phase)
    if name == 'chainsaw':
        return chainsaw(fr, frame, phase)
    if name == 'laser':
        return laser(fr, frame, phase)
    raise KeyError(name)
