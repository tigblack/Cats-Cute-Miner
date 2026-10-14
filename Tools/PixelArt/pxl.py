"""Мини-библиотека пиксель-арта Cat Miners: палитра, холст, примитивы, реестр спрайтов.

Все спрайты игры рисуются кодом (модули sprites/*.py), упаковываются в один атлас (build.py)
и встраиваются в Unity как C#-файл с PNG в base64 (PNG не синхронизируются хабом).

Соглашения (подробно — Docs/Art/PIXEL_STYLE_GUIDE.md):
  * 32 пикселя арта на клетку поля; UI — 4 единицы Canvas (1080×1920) на пиксель арта;
  * координаты холста — как в PIL: (0, 0) — левый верхний угол, y растёт вниз;
  * pivot и border спрайта — в соглашении Unity: от левого НИЖНЕГО угла, в пикселях;
  * цвета только из PALETTE (проверяется сборкой); полупрозрачность — только у спрайтов с alpha=True;
  * свет сверху-слева, контур 1 px — тёмный оттенок своего материала (не чистый чёрный).
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass, field
from typing import Callable, Dict, Iterable, List, Optional, Sequence, Tuple, Union

from PIL import Image

RGB = Tuple[int, int, int]
ColorLike = Union[str, RGB, Tuple[int, int, int, int], None]


def _hex(h: str) -> RGB:
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


# ───────────────────────── Палитра ─────────────────────────
# Рампы от тёмного к светлому. Имена коротко: <рампа><индекс>. Новые цвета добавлять только сюда
# (и в Docs/Art/PIXEL_STYLE_GUIDE.md), чтобы весь арт оставался в одной гамме.
RAMPS: Dict[str, List[str]] = {
    # Контур и самые тёмные тени.
    'ink': ['#07060f', '#0b0a14', '#1a1424', '#271c2e'],
    # Холодный камень пещеры и тёмные панели UI (тёмно-синий/индиго).
    'navy': ['#0c0f22', '#141a36', '#1d2549', '#27325c', '#34416e', '#46557f', '#5b6a92', '#7886a8', '#9ba6c2'],
    # Плиты пола.
    'slate': ['#232a45', '#2a3150', '#363f60', '#434d6e', '#50597a', '#5e6888', '#707a98', '#8790ac'],
    # Добываемые камни (серо-фиолетовый гранит).
    'stone': ['#1e1c2c', '#2c2a3e', '#3b3a52', '#4d4c66', '#63637e', '#7d7d98', '#9a9ab2', '#bcbcd0'],
    # Дерево: опоры, ворота, ящики, таблички.
    'wood': ['#2b1612', '#45231c', '#5f3426', '#7d4a31', '#9c6540', '#bb8556', '#d6a873', '#ecc99a'],
    # Тёплый свет: огонь, фонари, свечение.
    'amber': ['#6b2c12', '#a84b18', '#d9771f', '#f2a636', '#fbd06a', '#fff0b0'],
    # Золото: монеты, самородки, главная кнопка.
    'gold': ['#5c3a0e', '#8a5a12', '#c98d1c', '#f2c230', '#ffe36a', '#fff6c4'],
    # Эликсир и аметист.
    'purple': ['#1e0d3a', '#2a1350', '#48208a', '#6b34c4', '#9459ea', '#bf8ff8', '#e6d2ff'],
    # Самоцветы (твёрдая валюта), бирюза.
    'teal': ['#0a2428', '#0d3236', '#146058', '#1f9478', '#3ccc9a', '#8ff0c8', '#d4fff0'],
    # Синий: комбинезоны, вода, синие кнопки.
    'blue': ['#0a1638', '#0e1f4a', '#18357a', '#2655ad', '#3d7ad6', '#6aa6f0', '#a8d2ff', '#e0f0ff'],
    # Красный: динамит, предупреждения, крыши.
    'red': ['#2a0a12', '#3e0f1a', '#741f2a', '#b0333a', '#e05a4a', '#ff9070', '#ffc8b0'],
    # Зелёный: мох, зелёные кнопки, «готово».
    'green': ['#11200f', '#1a2e1e', '#2c4a26', '#45702e', '#6a9a3a', '#9cc85a', '#d0ec8a'],
    # Розовый: носы, ушки, щёки.
    'pink': ['#6a2440', '#a04060', '#e07a96', '#ffb4c4', '#ffe0e8'],
    # Светлые: текст, блики, мордочки.
    'cream': ['#8c7c68', '#c8b8a0', '#e8dcc4', '#fdf6e3', '#ffffff'],
    # Оформления окружения (Docs/Visual/ART_DIRECTION.md, «Окружение»; перекраска — sprites/themes.py).
    # Кристальная пещера: камень стен — глубокий сине-бирюзовый, плиты пола — холодный серо-голубой.
    'cave': ['#08131c', '#0c1c29', '#112838', '#173548', '#1f4459', '#29546b', '#376880', '#4b7f96', '#6699ad'],
    'frost': ['#1f2a35', '#25313d', '#2e3a47', '#384553', '#43515f', '#505e6d', '#606f7d', '#768491'],
    # Медная выработка: порода стен — тёмная красно-бурая (глубже и спокойнее штольни), пол — тёплый серо-бурый.
    # Блестящая медь (шестерни, трубы) — рампа fur_ginger.
    'copper': ['#140c0c', '#1e1210', '#2a1814', '#381f19', '#47281f', '#583226', '#6c3f2f', '#844f3a', '#a0654a'],
    'rust': ['#2a1d22', '#33242a', '#3f2e33', '#4c383b', '#5a4446', '#6a5352', '#7c6460', '#907870'],
    # Окрасы шерсти (5 тонов, контур берётся из ink): рыжий, серый, чёрный, белый, кремовый, коричневый.
    'fur_ginger': ['#5a2410', '#9a4a1c', '#d8762a', '#f2a456', '#fcd29a'],
    'fur_gray': ['#2e2e3a', '#545466', '#7c7c90', '#a6a6b8', '#d0d0dc'],
    'fur_black': ['#16161f', '#24243a', '#363650', '#4c4c68', '#6a6a88'],
    'fur_white': ['#8a8aa0', '#b8b8c8', '#dcdce6', '#f4f4f8', '#ffffff'],
    'fur_cream': ['#6e5236', '#a8845a', '#d4b484', '#ecd8b0', '#fff4dc'],
    'fur_brown': ['#3a2216', '#5e3a24', '#855634', '#ae7a4c', '#d2a472'],
}

PALETTE: Dict[str, RGB] = {}
for _ramp, _colors in RAMPS.items():
    for _i, _c in enumerate(_colors):
        PALETTE[f'{_ramp}{_i}'] = _hex(_c)
PALETTE['white'] = (255, 255, 255)
PALETTE['black'] = (0, 0, 0)
PALETTE_SET = set(PALETTE.values())


def C(c: ColorLike) -> Optional[Tuple[int, int, int, int]]:
    """Цвет из имени палитры ('navy3'), hex ('#ffcc00'), RGB или RGBA. None — прозрачный."""
    if c is None:
        return None
    if isinstance(c, str):
        if c.startswith('#'):
            r, g, b = _hex(c)
            return (r, g, b, 255)
        if c not in PALETTE:
            raise KeyError(f'нет цвета {c!r} в палитре')
        r, g, b = PALETTE[c]
        return (r, g, b, 255)
    if len(c) == 3:
        return (c[0], c[1], c[2], 255)
    return tuple(c)  # type: ignore[return-value]


def ramp(name: str) -> List[str]:
    """Имена цветов рампы от тёмного к светлому: ramp('wood') → ['wood0', … 'wood7']."""
    return [f'{name}{i}' for i in range(len(RAMPS[name]))]


def with_alpha(c: ColorLike, a: int) -> Tuple[int, int, int, int]:
    r, g, b, _ = C(c)
    return (r, g, b, a)


# ───────────────────────── Холст ─────────────────────────
class Canvas:
    """RGBA-холст в пикселях арта. (0, 0) — левый верхний угол."""

    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        self.px = self.img.load()

    # --- базовые операции ---
    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.w and 0 <= y < self.h

    def set(self, x: int, y: int, c: ColorLike) -> None:
        x, y = int(x), int(y)
        if not self.inside(x, y):
            return
        col = C(c)
        self.px[x, y] = col if col is not None else (0, 0, 0, 0)

    def get(self, x: int, y: int) -> Tuple[int, int, int, int]:
        if not self.inside(x, y):
            return (0, 0, 0, 0)
        return self.px[x, y]

    def opaque(self, x: int, y: int) -> bool:
        return self.get(x, y)[3] > 0

    def copy(self) -> 'Canvas':
        c = Canvas(self.w, self.h)
        c.img = self.img.copy()
        c.px = c.img.load()
        return c

    # --- фигуры ---
    def rect(self, x: int, y: int, w: int, h: int, c: ColorLike) -> None:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.set(xx, yy, c)

    def frame(self, x: int, y: int, w: int, h: int, c: ColorLike) -> None:
        """Рамка 1 px по периметру прямоугольника."""
        for xx in range(x, x + w):
            self.set(xx, y, c)
            self.set(xx, y + h - 1, c)
        for yy in range(y, y + h):
            self.set(x, yy, c)
            self.set(x + w - 1, yy, c)

    def hline(self, x0: int, x1: int, y: int, c: ColorLike) -> None:
        for x in range(min(x0, x1), max(x0, x1) + 1):
            self.set(x, y, c)

    def vline(self, x: int, y0: int, y1: int, c: ColorLike) -> None:
        for y in range(min(y0, y1), max(y0, y1) + 1):
            self.set(x, y, c)

    def line(self, x0: int, y0: int, x1: int, y1: int, c: ColorLike) -> None:
        """Линия Брезенхэма (без сглаживания)."""
        x0, y0, x1, y1 = int(x0), int(y0), int(x1), int(y1)
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            self.set(x0, y0, c)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def ellipse(self, cx: float, cy: float, rx: float, ry: float, c: ColorLike) -> None:
        """Залитый эллипс с центром (cx, cy). Полуцелые центры дают чётную ширину."""
        for y in range(int(math.floor(cy - ry)) - 1, int(math.ceil(cy + ry)) + 2):
            for x in range(int(math.floor(cx - rx)) - 1, int(math.ceil(cx + rx)) + 2):
                dx = (x + 0.5 - cx) / max(rx, 0.01)
                dy = (y + 0.5 - cy) / max(ry, 0.01)
                if dx * dx + dy * dy <= 1.0:
                    self.set(x, y, c)

    def polygon(self, pts: Sequence[Tuple[float, float]], c: ColorLike) -> None:
        """Залитый многоугольник (правило чёт-нечет по центрам пикселей)."""
        ys = [p[1] for p in pts]
        n = len(pts)
        for y in range(int(math.floor(min(ys))), int(math.ceil(max(ys))) + 1):
            yc = y + 0.5
            xs = []
            for i in range(n):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % n]
                if (y0 <= yc < y1) or (y1 <= yc < y0):
                    xs.append(x0 + (yc - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                for x in range(int(math.ceil(xs[i] - 0.5)), int(math.floor(xs[i + 1] - 0.5)) + 1):
                    self.set(x, y, c)

    def fill_where(self, pred: Callable[[int, int], bool], c: ColorLike, only_opaque: bool = True) -> None:
        """Перекрасить пиксели, для которых pred(x, y) истинно (по умолчанию — только непрозрачные)."""
        for y in range(self.h):
            for x in range(self.w):
                if only_opaque and not self.opaque(x, y):
                    continue
                if pred(x, y):
                    self.set(x, y, c)

    def recolor(self, mapping: Dict[ColorLike, ColorLike]) -> 'Canvas':
        """Замена цветов (палитровая подмена: окрасы, цвета касок). Возвращает новый холст."""
        m = {C(k)[:3]: C(v) for k, v in mapping.items()}
        out = self.copy()
        for y in range(self.h):
            for x in range(self.w):
                p = out.px[x, y]
                if p[3] and p[:3] in m:
                    nv = m[p[:3]]
                    out.px[x, y] = (nv[0], nv[1], nv[2], p[3])
        return out

    # --- обработка ---
    def outline(self, c: ColorLike = 'ink1', diagonal: bool = False, inside: bool = False) -> None:
        """Контур 1 px вокруг непрозрачной формы. inside=True — по краю самой формы (не расширяя её)."""
        src = [[self.opaque(x, y) for x in range(self.w)] for y in range(self.h)]
        nb = [(1, 0), (-1, 0), (0, 1), (0, -1)]
        if diagonal:
            nb += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
        marks = []
        for y in range(self.h):
            for x in range(self.w):
                if inside:
                    if src[y][x] and any(not (0 <= x + dx < self.w and 0 <= y + dy < self.h) or not src[y + dy][x + dx] for dx, dy in nb):
                        marks.append((x, y))
                elif not src[y][x] and any(0 <= x + dx < self.w and 0 <= y + dy < self.h and src[y + dy][x + dx] for dx, dy in nb):
                    marks.append((x, y))
        for x, y in marks:
            self.set(x, y, c)

    def shade(self, light: ColorLike, dark: ColorLike, base: ColorLike, highlight_edge: bool = True) -> None:
        """Простое объёмное затенение формы: верх-лево светлее, низ-право темнее (по границе формы)."""
        src = [[self.get(x, y)[:3] == C(base)[:3] and self.opaque(x, y) for x in range(self.w)] for y in range(self.h)]
        for y in range(self.h):
            for x in range(self.w):
                if not src[y][x]:
                    continue
                up = y - 1 < 0 or not self.opaque(x, y - 1)
                left = x - 1 < 0 or not self.opaque(x - 1, y)
                down = y + 1 >= self.h or not self.opaque(x, y + 1)
                right = x + 1 >= self.w or not self.opaque(x + 1, y)
                if highlight_edge and (up or left):
                    self.set(x, y, light)
                elif down or right:
                    self.set(x, y, dark)

    def blit(self, other: 'Canvas', x: int, y: int) -> None:
        """Наложить другой холст (альфа-смешение по непрозрачным пикселям)."""
        self.img.alpha_composite(other.img, (int(x), int(y)))
        self.px = self.img.load()

    def flipped(self) -> 'Canvas':
        out = Canvas(self.w, self.h)
        out.img = self.img.transpose(Image.FLIP_LEFT_RIGHT)
        out.px = out.img.load()
        return out

    def crop(self, x: int, y: int, w: int, h: int) -> 'Canvas':
        out = Canvas(w, h)
        out.img = self.img.crop((x, y, x + w, y + h))
        out.px = out.img.load()
        return out

    def dither(self, x: int, y: int, w: int, h: int, c: ColorLike, density: float = 0.5, seed: int = 0, only_opaque: bool = True) -> None:
        """Упорядоченный дизеринг (Байер 4×4) цветом c с заданной плотностью."""
        bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                if only_opaque and not self.opaque(xx, yy):
                    continue
                if (bayer[(yy + seed) % 4][(xx + seed) % 4] + 0.5) / 16.0 < density:
                    self.set(xx, yy, c)

    def speckle(self, colors: Sequence[ColorLike], density: float, seed: int, region: Optional[Tuple[int, int, int, int]] = None) -> None:
        """Случайные одиночные пиксели (текстура камня/дерева) по непрозрачным пикселям."""
        rnd = random.Random(seed)
        x0, y0, w, h = region or (0, 0, self.w, self.h)
        for yy in range(y0, y0 + h):
            for xx in range(x0, x0 + w):
                if self.opaque(xx, yy) and rnd.random() < density:
                    self.set(xx, yy, rnd.choice(list(colors)))

    @staticmethod
    def from_ascii(rows: Sequence[str], legend: Dict[str, ColorLike]) -> 'Canvas':
        """Холст из ASCII-рисунка: каждый символ — пиксель; '.' и ' ' — прозрачные, если не заданы в legend."""
        h = len(rows)
        w = max(len(r) for r in rows)
        cv = Canvas(w, h)
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch in legend:
                    cv.set(x, y, legend[ch])
                elif ch not in '. ':
                    raise KeyError(f'символ {ch!r} нет в legend')
        return cv

    def scaled(self, k: int) -> Image.Image:
        return self.img.resize((self.w * k, self.h * k), Image.NEAREST)


# ───────────────────────── Реестр спрайтов ─────────────────────────
@dataclass
class SpriteEntry:
    name: str
    canvas: Canvas
    pivot: Tuple[float, float]               # пиксели от левого нижнего угла (Unity)
    border: Tuple[int, int, int, int] = (0, 0, 0, 0)  # left, bottom, right, top — 9-slice
    group: str = 'misc'
    alpha: bool = False                      # разрешены полупрозрачные пиксели (тени, свечение)
    extrude: bool = False                    # тайлы пола/стен: повторить край в отступ атласа (без швов)
    note: str = ''


class Registry:
    def __init__(self) -> None:
        self.entries: Dict[str, SpriteEntry] = {}
        self.group = 'misc'

    def add(self, name: str, canvas: Canvas, pivot: Optional[Tuple[float, float]] = None,
            border: Tuple[int, int, int, int] = (0, 0, 0, 0), alpha: bool = False, extrude: bool = False, note: str = '') -> SpriteEntry:
        """Зарегистрировать спрайт. pivot по умолчанию — центр. Имя — snake_case, уникальное."""
        if name in self.entries:
            raise ValueError(f'спрайт {name!r} уже зарегистрирован (группа {self.entries[name].group})')
        if not name.replace('_', '').isalnum() or name.lower() != name:
            raise ValueError(f'имя {name!r}: только строчные латинские буквы, цифры и _')
        if pivot is None:
            pivot = (canvas.w / 2.0, canvas.h / 2.0)
        e = SpriteEntry(name, canvas, pivot, border, self.group, alpha, extrude, note)
        self.entries[name] = e
        return e


def pivot_feet(cv: Canvas, feet_from_bottom: int = 0) -> Tuple[float, float]:
    """Pivot «ноги»: центр по X, на feet_from_bottom пикселей выше нижнего края."""
    return (cv.w / 2.0, float(feet_from_bottom))


def tiled_preview(cv: Canvas, nx: int, ny: int) -> Canvas:
    """Проверка бесшовности тайла: копии nx×ny."""
    out = Canvas(cv.w * nx, cv.h * ny)
    for j in range(ny):
        for i in range(nx):
            out.blit(cv, i * cv.w, j * cv.h)
    return out
