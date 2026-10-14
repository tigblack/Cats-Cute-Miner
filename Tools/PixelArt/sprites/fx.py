"""Эффекты и оверлеи мира: тени, искры, пыль, монетки, кольцо объединения, дым, взрыв, ореолы,
полоса HP, пузырь статуса, уголки выделения.

Правила (Docs/Art/PIXEL_STYLE_GUIDE.md): частицы «гаснут» формой, а не прозрачностью — кадр за кадром облачко
редеет и распадается на клочки; полупрозрачны только тени и ореолы (alpha=True), ступенями, без плавного градиента.
Свет сверху-слева: у клубов блик сверху-слева, тень снизу-справа; контур — тёмный тон своего материала.
"""
from __future__ import annotations

import math
from typing import Dict, List, Optional, Sequence, Tuple

from pxl import C, Canvas, with_alpha

N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))


# ───────────────────────── помощники ─────────────────────────
def _mask_ellipse(w: int, h: int, cx: float, cy: float, rx: float, ry: float) -> List[List[bool]]:
    m = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            dx = (x + 0.5 - cx) / max(rx, 0.01)
            dy = (y + 0.5 - cy) / max(ry, 0.01)
            if dx * dx + dy * dy <= 1.0:
                m[y][x] = True
    return m


def _erode(m: List[List[bool]]) -> List[List[bool]]:
    h, w = len(m), len(m[0])
    out = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if m[y][x] and all(0 <= x + dx < w and 0 <= y + dy < h and m[y + dy][x + dx] for dx, dy in N4):
                out[y][x] = True
    return out


def _ring(w: int, h: int, rx: float, ry: float, thick: int = 1) -> List[List[bool]]:
    """Кольцо-эллипс толщиной thick (силуэт минус эрозия): линия без разрывов на любом радиусе."""
    outer = _mask_ellipse(w, h, w / 2.0, h / 2.0, rx, ry)
    inner = outer
    for _ in range(thick):
        inner = _erode(inner)
    return [[outer[y][x] and not inner[y][x] for x in range(w)] for y in range(h)]


def _lobes(cv: Canvas, lobes: Sequence[Tuple[float, float, float]], light: str, base: str, dark: str,
           hi: Optional[str] = None, dark_t: float = 0.55) -> None:
    """Клубящееся облако из кругов (x, y, r), от дальних к ближним: каждый круг — со своим бликом сверху-слева
    и тенью снизу-справа, поэтому силуэт читается «пухлым», как в мультяшных взрывах."""
    for cx, cy, r in lobes:
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                d = math.hypot(dx, dy)
                if d > r:
                    continue
                s = (dx + dy) / max(r, 0.01)  # −√2 … √2: верх-лево → низ-право
                if hi and s < -0.95 and d > r * 0.45:
                    c = hi
                elif s < -0.35:
                    c = light
                elif s > dark_t and d > r * 0.5:
                    c = dark
                else:
                    c = base
                cv.set(x, y, c)


def _put(cv: Canvas, pts: Sequence[Tuple[int, int]], c: Optional[str]) -> None:
    for x, y in pts:
        cv.set(x, y, c)


def _from(rows: Sequence[str], legend: Dict[str, str]) -> Canvas:
    return Canvas.from_ascii(rows, legend)


# ───────────────────────── служебные ─────────────────────────
def px_white() -> Canvas:
    cv = Canvas(1, 1)
    cv.set(0, 0, 'white')
    return cv


SHADOW_EDGE_A = 78   # ≈ 31 % — мягкий край
SHADOW_CORE_A = 104  # ≈ 41 % — ядро; в среднем ≈ 35 %


def shadow(w: int, h: int) -> Canvas:
    """Овальная тень ink0: светлая кромка 1–2 px и чуть темнее ядро (две ступени альфы, без градиента)."""
    cv = Canvas(w, h)
    outer = _mask_ellipse(w, h, w / 2.0, h / 2.0, w / 2.0, h / 2.0)
    core = _mask_ellipse(w, h, w / 2.0, h / 2.0, w / 2.0 - max(2.0, w * 0.08), h / 2.0 - 1.0)
    for y in range(h):
        for x in range(w):
            if core[y][x]:
                cv.set(x, y, with_alpha('ink0', SHADOW_CORE_A))
            elif outer[y][x]:
                cv.set(x, y, with_alpha('ink0', SHADOW_EDGE_A))
    return cv


# ───────────────────────── искры ─────────────────────────
SPARK_LG = {'W': 'cream4', 'Y': 'gold5', 'G': 'gold4', 'g': 'gold3', 'a': 'amber3'}
SPARKS = [
    [
        '.......',
        '.......',
        '...G...',
        '..GWG..',
        '...G...',
        '.......',
        '.......',
    ],
    [
        '.......',
        '...g...',
        '...Y...',
        '.gYWYg.',
        '...Y...',
        '...g...',
        '.......',
    ],
    [
        '...a...',
        '...G...',
        '..gYg..',
        'aGYWYGa',
        '..gYg..',
        '...G...',
        '...a...',
    ],
    [
        '.......',
        '.g...g.',
        '.......',
        '...Y...',
        '.......',
        '.g...g.',
        '.......',
    ],
]


# ───────────────────────── пыль ─────────────────────────
PUFF = ('cream3', 'cream2', 'stone6', 'stone5')  # блик, тело, тень, нижняя кромка


def puff(i: int) -> Canvas:
    """Облачко пыли 12×12: растёт (0→2) и рассыпается на клочки (3). Низ клубов темнее — «оседает»."""
    cv = Canvas(12, 12)
    hi, base, dark, edge = PUFF
    if i == 0:
        _lobes(cv, [(4.6, 7.2, 2.5), (7.4, 7.2, 2.5), (6.0, 5.4, 2.7)], hi, base, dark)
    elif i == 1:
        _lobes(cv, [(3.5, 7.5, 2.6), (8.5, 7.5, 2.6), (6.0, 5.0, 3.0), (6.0, 8.2, 2.6)], hi, base, dark)
    elif i == 2:
        _lobes(cv, [(2.6, 7.0, 2.6), (9.4, 7.0, 2.6), (4.2, 3.8, 2.6), (8.0, 3.6, 2.4), (6.0, 8.6, 2.5)], hi, base, dark)
        # середина редеет: облако «раскрывается»
        for x, y in [(5, 5), (6, 5), (5, 6), (6, 6), (7, 6), (6, 7)]:
            cv.set(x, y, None)
    else:
        _lobes(cv, [(2.0, 6.6, 1.9), (10.0, 6.4, 1.9), (4.0, 2.2, 1.8), (8.6, 2.0, 1.6), (6.4, 10.0, 1.8)], hi, base, dark)
    # нижняя кромка каждого клуба — тон темнее (читается на светлом полу)
    marks = []
    for y in range(cv.h):
        for x in range(cv.w):
            if cv.opaque(x, y) and not cv.opaque(x, y + 1):
                marks.append((x, y))
    for x, y in marks:
        if cv.get(x, y)[:3] != C(hi)[:3]:
            cv.set(x, y, edge)
    return cv


# ───────────────────────── монетка ─────────────────────────
COIN_LG = {'K': 'ink1', 'a': 'amber3', 'A': 'amber2', 'b': 'amber1', 'g': 'gold3', 'G': 'gold4', 'W': 'gold5', 'd': 'gold2'}
COINS = [
    [  # лицом: оранжевый обод, жёлтое поле с бликом, как монета HUD
        '..KKKK..',
        '.KaaaaK.',
        'KaGWGgAK',
        'KaGgggAK',
        'KaGggdAK',
        'KagdddAK',
        '.KAAAbK.',
        '..KKKK..',
    ],
    [  # три четверти: поле сузилось, ребро справа
        '..KKKK..',
        '.KGGaAK.',
        '.KWgdAK.',
        '.KGgdAK.',
        '.KgddAK.',
        '.KgddAK.',
        '.KAAbbK.',
        '..KKKK..',
    ],
    [  # ребром
        '...KK...',
        '..KGAK..',
        '..KGAK..',
        '..KGAK..',
        '..KGAK..',
        '..KGAK..',
        '..KGAK..',
        '...KK...',
    ],
    [  # три четверти с другой стороны: ребро слева
        '..KKKK..',
        '.KaGGgK.',
        '.KAWggK.',
        '.KAGgdK.',
        '.KAggdK.',
        '.KAgddK.',
        '.KbAAAK.',
        '..KKKK..',
    ],
]


# ───────────────────────── кольцо объединения ─────────────────────────
def merge_ring(i: int) -> Canvas:
    """Кольцо 32×16 расходится от центра: яркое и толстое → тонкое золото → пунктир (гаснет формой)."""
    cv = Canvas(32, 16)
    spec = [  # rx, ry, толщина, внешний, внутренний
        (7.0, 3.5, 2, 'cream4', 'gold5'),
        (11.0, 5.5, 2, 'gold5', 'gold4'),
        (14.0, 7.0, 1, 'gold4', 'gold4'),
        (16.0, 8.0, 1, 'gold3', 'gold3'),
    ][i]
    rx, ry, t, c_out, c_in = spec
    outer = _ring(32, 16, rx, ry, 1)
    full = _ring(32, 16, rx, ry, t)
    for y in range(16):
        for x in range(32):
            if not full[y][x]:
                continue
            if i == 3 and ((x // 2) + (y // 2)) % 2:
                continue  # пунктир последнего кадра
            cv.set(x, y, c_out if outer[y][x] else c_in)
    # искорки на кольце во 2-м кадре
    if i == 1:
        for x, y in [(4, 8), (27, 7), (16, 0)]:
            if cv.inside(x, y):
                cv.set(x, y, 'cream4')
    return cv


# ───────────────────────── дым ─────────────────────────
SMOKE_LG = {'h': 'navy8', 'b': 'navy7', 'd': 'navy6'}
# Клуб дыма из трубы 10×10 нарисован вручную: на таком размере круги-примитивы дают рваный край.
# Растёт (0→2) и распадается на клочки без блика (3); код поднимает клуб вверх и сносит вбок.
SMOKES = [
    [
        '..........',
        '..........',
        '....hh....',
        '...hbbb...',
        '..hbbbbd..',
        '..hbbbbd..',
        '...bbdd...',
        '..........',
        '..........',
        '..........',
    ],
    [
        '..........',
        '...hh.....',
        '..hbbhhh..',
        '.hbbbbbbd.',
        '.hbbbbbbd.',
        '.hbbbbbbd.',
        '..bbdbbd..',
        '...dd.dd..',
        '..........',
        '..........',
    ],
    [
        '..hhh.....',
        '.hbbbhhh..',
        'hbbbbbbbh.',
        'hbbbbbbbbd',
        'hbbbbbbbbd',
        '.bbbbbbbbd',
        'hbbbbbbbd.',
        '.bbdbbbdd.',
        '..dd.ddd..',
        '..........',
    ],
    [
        '.....bb...',
        '....bbbd..',
        '....bbdd..',
        '.....dd...',
        '..........',
        '.bb.......',
        'bbbd...bb.',
        'bbdd..bbbd',
        '.dd...bbdd',
        '.......dd.',
    ],
]


# ───────────────────────── взрыв ─────────────────────────
FIRE = dict(hi='gold5', light='gold4', base='amber3', dark='amber2', edge='red3')
SMOKE_B = dict(hi='cream2', light='navy8', base='navy7', dark='navy6', edge='navy4')


def boom(i: int) -> Canvas:
    """Милый мультяшный взрыв 32×32, 5 кадров: вспышка → огненный клуб → клуб темнеет в дым → кольцо дыма → клочки."""
    cv = Canvas(32, 32)
    c = 16.0
    if i == 0:
        # вспышка: белое ядро в золотой кайме и лучи-звезда; лучи только по осям и диагоналям —
        # чистые пиксельные линии без «лесенки»
        for k in range(8):
            ax, ay = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)][k]
            n = 14 if k % 2 == 0 else 10
            for r in range(6, n + 1):
                col = 'gold5' if r < n - 2 else 'gold4'
                cv.set(int(c + ax * r) - (1 if ax < 0 else 0), int(c + ay * r) - (1 if ay < 0 else 0), col)
        flash = Canvas(32, 32)
        flash.ellipse(c, c, 7.5, 7.5, 'gold5')
        flash.ellipse(c - 0.5, c - 0.5, 5.5, 5.5, 'cream4')
        cv.blit(flash, 0, 0)
    elif i == 1:
        lobes = [(10.5, 18.5, 6.0), (21.5, 18.5, 6.0), (16.0, 11.0, 6.5), (11.0, 12.5, 5.0), (21.0, 12.5, 5.0),
                 (16.0, 21.0, 6.0), (16.0, 16.0, 7.0)]
        _lobes(cv, lobes, FIRE['light'], FIRE['base'], FIRE['dark'], FIRE['hi'])
        core = Canvas(32, 32)
        _lobes(core, [(14.5, 14.0, 3.6), (17.5, 15.5, 3.0)], 'cream4', 'gold5', 'gold4')
        cv.blit(core, 0, 0)
        cv.outline(FIRE['edge'])
        # отлетающие искры
        for x, y in [(3, 6), (28, 5), (2, 25), (29, 26), (16, 1)]:
            _put(cv, [(x, y)], 'gold5')
    elif i == 2:
        smoke_l = [(9.0, 18.0, 6.0), (23.0, 18.0, 6.0), (16.0, 9.5, 6.5), (9.5, 11.0, 5.0), (22.5, 11.0, 5.0),
                   (16.0, 22.5, 6.0)]
        _lobes(cv, smoke_l, SMOKE_B['light'], SMOKE_B['base'], SMOKE_B['dark'], SMOKE_B['hi'])
        fire = Canvas(32, 32)
        _lobes(fire, [(16.0, 16.0, 6.0), (13.0, 14.0, 3.5), (19.0, 18.0, 3.5)], 'gold4', 'amber3', 'amber2')
        fire.outline('amber1')
        cv.blit(fire, 0, 0)
        cv.outline(SMOKE_B['edge'])
        for x, y in [(1, 4), (30, 3), (1, 28), (30, 28)]:
            _put(cv, [(x, y)], 'gold4')
    elif i == 3:
        # кольцо дыма: клубы по окружности, в центре пусто
        ring = []
        for k in range(9):
            a = -math.pi / 2 + k * 2 * math.pi / 9
            ring.append((c + math.cos(a) * 10.5, c + math.sin(a) * 10.0, 4.2 - 0.3 * (k % 2)))
        _lobes(cv, ring, SMOKE_B['light'], SMOKE_B['base'], SMOKE_B['dark'], SMOKE_B['hi'])
        cv.outline(SMOKE_B['edge'])
        for x, y in [(16, 16), (13, 18), (19, 14)]:
            _put(cv, [(x, y)], 'amber3')
    else:
        # клочки дыма разлетаются и тают: мелкие клубы без блика, контур светлее
        bits = [(5.5, 7.5, 3.0), (26.0, 6.5, 2.8), (3.5, 22.5, 2.6), (27.5, 23.5, 3.0), (16.0, 3.0, 2.4), (15.5, 28.5, 2.4)]
        _lobes(cv, bits, SMOKE_B['light'], SMOKE_B['base'], SMOKE_B['dark'])
        cv.outline(SMOKE_B['edge'])
    return cv


# ───────────────────────── ореолы ─────────────────────────
GLOWS = {
    # цвет кольца (снаружи → внутрь) и альфа ступеней
    'warm': ['amber2', 'amber3', 'amber3', 'amber4'],
    'purple': ['purple3', 'purple4', 'purple4', 'purple5'],
    'white': ['cream4', 'cream4', 'cream4', 'cream4'],
    # друзы оформлений (sprites/themes.py): бирюза кристальной пещеры, синий азурит медной выработки
    'teal': ['teal3', 'teal4', 'teal4', 'teal5'],
    'blue': ['blue3', 'blue4', 'blue4', 'blue5'],
}
GLOW_STEPS = [(16.0, 34), (12.5, 56), (9.0, 82), (5.5, 112)]


def glow(kind: str) -> Canvas:
    """Ступенчатый радиальный ореол 32×32: 4 концентрических круга, у каждого своя альфа (без градиента)."""
    cv = Canvas(32, 32)
    for (r, a), col in zip(GLOW_STEPS, GLOWS[kind]):
        m = _mask_ellipse(32, 32, 16.0, 16.0, r, r)
        for y in range(32):
            for x in range(32):
                if m[y][x]:
                    cv.set(x, y, with_alpha(col, a))
    return cv


# Ореол фонаря на опоре: фонарь висит вплотную к кромке поля, между ним и столбом опоры ~12 px стены — ореол узкий
# и высокий, целиком на стене (правый край холста = кромка поля), центр — на уровне фонаря.
GLOW_LANTERN_W, GLOW_LANTERN_H = 14, 32
GLOW_LANTERN_STEPS = [(7.0, 16.0, 40), (6.5, 13.0, 70), (5.5, 10.5, 104), (4.5, 8.5, 140)]


def glow_lantern() -> Canvas:
    """Узкий ступенчатый ореол фонаря 14×32: 4 вложенных эллипса тёплых тонов, у каждого своя альфа (без градиента)."""
    w, h = GLOW_LANTERN_W, GLOW_LANTERN_H
    cv = Canvas(w, h)
    for (rx, ry, a), col in zip(GLOW_LANTERN_STEPS, GLOWS['warm']):
        m = _mask_ellipse(w, h, w / 2.0, h / 2.0, rx, ry)
        for y in range(h):
            for x in range(w):
                if m[y][x]:
                    cv.set(x, y, with_alpha(col, a))
    return cv


# ───────────────────────── оверлеи: HP, пузырь, уголки ─────────────────────────
HP_FRAME = [
    '.KKKKKKKK.',
    'KnnnnnnnnK',
    'KNNNNNNNNK',
    'KNNNNNNNNK',
    'KNNNNNNNNK',
    '.KKKKKKKK.',
]
HP_FILL = [
    'GGGG',
    'aaaa',
    'aaaa',
    'AAAA',
]
BUBBLE = [
    '..kkkkkkkkkkkk..',
    '.kWWWWWWWWWWWWk.',
    'kWWwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kWwwwwwwwwwwwwvk',
    'kwwwwwwwwwwwwvvk',
    '.kvvvvvvvvvvvvk.',
    '..kkkkkkkkkkkk..',
]
BUBBLE_TAIL = [
    'kvvvvk',
    '.kvvk.',
    '.kvk..',
    '..k...',
]
BUBBLE_LG = {'k': 'ink2', 'W': 'cream4', 'w': 'cream3', 'v': 'cream2'}


def select_bracket() -> Canvas:
    """Золотые уголки выделения 16×16 (border 6): Г-образные плечи 2 px, контур ink1, середина пустая."""
    cv = Canvas(16, 16)
    gold = set()
    for (x0, y0, sx, sy) in [(1, 1, 1, 1), (14, 1, -1, 1), (1, 14, 1, -1), (14, 14, -1, -1)]:
        for k in range(4):
            for t in range(2):
                gold.add((x0 + sx * k, y0 + sy * t))
                gold.add((x0 + sx * t, y0 + sy * k))
    for x, y in gold:
        cv.set(x, y, 'gold3')
    for x, y in gold:
        up, left = (x, y - 1) not in gold, (x - 1, y) not in gold
        down, right = (x, y + 1) not in gold, (x + 1, y) not in gold
        if up or left:
            cv.set(x, y, 'gold5' if (up and left) else 'gold4')
        elif down or right:
            cv.set(x, y, 'gold2')
    cv.outline('ink1')
    return cv


# ───────────────────────── дополнительные ─────────────────────────
HIT_LG = {'W': 'cream4', 'c': 'cream3', 'Y': 'gold5', 'G': 'gold4'}
HITS = [
    [  # удар кирки: белая вспышка-«звёздочка»
        '....W....',
        '....W....',
        '.W..W..W.',
        '..WWWWW..',
        'WWWWWWWWW',
        '..WWWWW..',
        '.W..W..W.',
        '....W....',
        '....W....',
    ],
    [
        '.Y..c..Y.',
        '..Y.c.Y..',
        '.........',
        '.c..W..c.',
        'cc.WWW.cc',
        '.c..W..c.',
        '.........',
        '..Y.c.Y..',
        '.Y..c..Y.',
    ],
    [
        'G.......G',
        '.........',
        '....c....',
        '.........',
        '..c...c..',
        '.........',
        '....c....',
        '.........',
        'G.......G',
    ],
]
ELIXIR_FX = [
    '..56..',
    '.6654.',
    '.6544.',
    '665443',
    '654433',
    '.4433.',
    '..32..',
]
GEM_FX = [
    '.5664.',
    '556443',
    '.5443.',
    '..43..',
]


def _small_icon(rows: Sequence[str], ramp: str) -> Canvas:
    lg = {str(i): f'{ramp}{i}' for i in range(7)}
    art = _from(rows, lg)
    cv = Canvas(art.w + 2, art.h + 2)
    cv.blit(art, 1, 1)
    cv.outline('ink1')
    return cv


# ───────────────────────── контакт удара ─────────────────────────
def impact(i: int) -> Canvas:
    """Контактный акцент удара кирки 11×11, 3 кадра по 0,04 с: яркая точка с лучами → кольцо лучей → искры по краям.
    Маленький и короткий: частые удары многих котиков не превращают поле в вспышки."""
    cv = Canvas(11, 11)
    c = 5
    if i == 0:
        for d in range(1, 4):
            for x, y in ((c + d, c), (c - d, c), (c, c + d), (c, c - d)):
                cv.set(x, y, 'cream4' if d == 1 else 'gold5')
        cv.set(c, c, 'white')
        for x, y in ((c + 1, c + 1), (c - 1, c - 1), (c + 1, c - 1), (c - 1, c + 1)):
            cv.set(x, y, 'gold4')
    elif i == 1:
        for d in (3, 4):
            for x, y in ((c + d, c), (c - d, c), (c, c + d), (c, c - d)):
                cv.set(x, y, 'gold5' if d == 3 else 'gold4')
        for x, y in ((c + 2, c + 2), (c - 2, c - 2), (c + 2, c - 2), (c - 2, c + 2)):
            cv.set(x, y, 'gold4')
    else:
        for x, y in ((c + 5, c), (c - 5, c), (c, c + 5), (c, c - 5), (c + 3, c + 3), (c - 3, c - 3)):
            cv.set(x, y, 'gold3')
    return cv


# ───────────────────────── магическая кирка игрока ─────────────────────────
def strike_pick(i: int) -> Canvas:
    """Появление → замах → контакт → отдача; позы нарисованы отдельно, без поворота.

    Холст 32×32: контакт — центр пикселя (10, 28), pivot Unity (10.5, 3.5).
    Невидимый хват / торец рукояти всегда (26, 15), на 16 px вправо и 13 px вверх.
    Контур острия касается (10, 28) при ударе, (10, 24) при отдаче.
    """
    cv = Canvas(32, 32)
    # Узел головы и отдельный многоугольник её дуги: острые концы, толстая середина.
    hub, shape, glint = [
        ((14, 4), [(22, 2), (18, 1), (14, 2), (11, 5), (9, 9),
                   (9, 14), (11, 11), (13, 8), (16, 5), (19, 3)], (13, 4)),
        ((7, 13), [(3, 5), (6, 7), (9, 11), (9, 15), (6, 19),
                   (2, 21), (4, 17), (6, 14), (6, 10)], (7, 10)),
        ((9, 21), [(3, 16), (7, 16), (11, 19), (13, 23),
                   (12, 26), (10, 28), (10, 24), (8, 21), (6, 18)], (8, 18)),
        ((8, 18), [(2, 13), (6, 13), (10, 16), (12, 20),
                   (12, 22), (10, 24), (9, 21), (7, 18), (5, 15)], (7, 15)),
    ][i]
    dim = 1 if i == 3 else 0
    # Две деревянные грани и светлая верхняя кромка; хват не сдвигается между кадрами.
    hx, hy = hub
    cv.line(hx, hy + 1, 26, 16, f'wood{3 - dim}')
    cv.line(hx, hy, 26, 15, f'wood{5 - dim}')
    cv.line(hx, hy - 1, 25, 14, f'wood{6 - dim}')
    head = Canvas(32, 32)
    head.polygon(shape, 'gold3')
    # Свет по экрану сверху-слева, независимо от позы.
    for y in range(32):
        for x in range(32):
            if head.opaque(x, y):
                light = not head.opaque(x, y - 1) or not head.opaque(x - 1, y)
                dark = not head.opaque(x, y + 1) or not head.opaque(x + 1, y)
                cv.set(x, y, f'gold{(4 if light else 2 if dark else 3) - dim}')
    cv.set(*glint, f'gold{5 - dim}')
    cv.outline('ink1')
    # Самый кончик контура — точный контактный пиксель; ниже него ничего нет.
    if i in (2, 3):
        tip_y = 28 if i == 2 else 24
        cv.set(10, tip_y, f'gold{4 - dim}')
    if i == 0:
        for x, y, c in [(8, 3, 'gold5'), (24, 4, 'cream4'),
                        (6, 11, 'gold4'), (16, 10, 'gold5')]:
            cv.set(x, y, c)
    elif i == 1:
        _put(cv, [(3, 10), (2, 11), (2, 12), (2, 13)], 'gold5')
    elif i == 2:
        # Всего пять ярких пикселей: большой контактный FX добавит игровой код.
        cv.set(10, 28, 'cream4')
        _put(cv, [(9, 28), (11, 28), (10, 27), (10, 29)], 'gold5')
    return cv


# ───────────────────────── залп пушки ─────────────────────────
def muzzle(i: int) -> Canvas:
    """Залп из дула 16×16, 3 кадра по 0,05 с: вспышка-звезда → огненный клуб с белым ядром → клуб дыма.
    Дальше дым продолжают обычные fx_smoke (Effects.Smoke)."""
    cv = Canvas(16, 16)
    c = 8.0
    if i == 0:
        for k in range(8):
            ax, ay = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)][k]
            n = 7 if k % 2 == 0 else 5
            for r in range(4, n + 1):
                cv.set(int(c + ax * r) - (1 if ax < 0 else 0), int(c + ay * r) - (1 if ay < 0 else 0), 'gold5' if r < n else 'gold4')
        flash = Canvas(16, 16)
        flash.ellipse(c, c, 4.0, 4.0, 'gold5')
        flash.ellipse(c - 0.5, c - 0.5, 2.6, 2.6, 'cream4')
        cv.blit(flash, 0, 0)
    elif i == 1:
        _lobes(cv, [(8.0, 9.5, 4.0), (4.8, 8.5, 3.2), (11.2, 8.5, 3.2), (8.0, 5.0, 3.4)], FIRE['light'], FIRE['base'], FIRE['dark'], FIRE['hi'])
        core = Canvas(16, 16)
        _lobes(core, [(7.5, 7.5, 2.4)], 'cream4', 'gold5', 'gold4')
        cv.blit(core, 0, 0)
        cv.outline(FIRE['edge'])
    else:
        _lobes(cv, [(4.5, 9.0, 3.2), (11.5, 9.0, 3.2), (8.0, 5.0, 3.6), (8.0, 10.5, 3.0)], SMOKE_B['light'], SMOKE_B['base'], SMOKE_B['dark'], SMOKE_B['hi'])
        for x, y in [(7, 8), (8, 8), (8, 9), (7, 7)]:
            cv.set(x, y, None)   # середина редеет: клуб распадается
        cv.outline(SMOKE_B['edge'])
    return cv


def build(reg) -> None:
    reg.add('px_white', px_white(), pivot=(0.5, 0.5), note='белый пиксель: затемнение, полосы; цвет задаёт код')
    for name, (w, h) in {'shadow_xs': (12, 5), 'shadow_s': (20, 7), 'shadow_m': (28, 9), 'shadow_l': (60, 16), 'shadow_xl': (150, 18)}.items():
        reg.add(name, shadow(w, h), alpha=True)
    for i, rows in enumerate(SPARKS):
        reg.add(f'fx_spark_{i}', _from(rows, SPARK_LG), pivot=(3.5, 3.5))
    for i in range(4):
        reg.add(f'fx_puff_{i}', puff(i))
    for i, rows in enumerate(COINS):
        reg.add(f'fx_coin_{i}', _from(rows, COIN_LG))
    for i in range(4):
        reg.add(f'fx_ring_{i}', merge_ring(i))
    for i, rows in enumerate(SMOKES):
        reg.add(f'fx_smoke_{i}', _from(rows, SMOKE_LG))
    for i in range(5):
        reg.add(f'fx_boom_{i}', boom(i))
    for i in range(3):
        reg.add(f'fx_muzzle_{i}', muzzle(i), note='залп пушки, 0,05 с/кадр; центр — у дула')
    for i in range(3):
        reg.add(f'fx_impact_{i}', impact(i), note='контакт удара кирки, 0,04 с/кадр, центр')
    for i in range(4):
        reg.add(f'fx_strike_pick_{i}', strike_pick(i), pivot=(10.5, 3.5),
                note='магическая кирка игрока: появление, замах, удар, отдача; 0–1: 0,05 с, 2–3: 0,08 с; pivot — контакт')
    for kind in GLOWS:
        reg.add(f'fx_glow_{kind}', glow(kind), alpha=True)
    reg.add('fx_glow_lantern', glow_lantern(), alpha=True)
    reg.add('hp_frame', _from(HP_FRAME, {'K': 'ink1', 'n': 'navy0', 'N': 'navy1'}), border=(3, 3, 3, 3))
    reg.add('hp_fill', _from(HP_FILL, {'G': 'gold4', 'a': 'amber3', 'A': 'amber2'}), border=(1, 1, 1, 1))
    reg.add('bubble_world', _from(BUBBLE, BUBBLE_LG), border=(5, 5, 5, 5))
    reg.add('bubble_world_tail', _from(BUBBLE_TAIL, BUBBLE_LG), pivot=(2.5, 0.0))
    reg.add('select_bracket', select_bracket(), border=(6, 6, 6, 6))
    # дополнительные: удар кирки и летящие к HUD эликсир/самоцвет
    for i, rows in enumerate(HITS):
        reg.add(f'fx_hit_{i}', _from(rows, HIT_LG))
    reg.add('fx_elixir', _small_icon(ELIXIR_FX, 'purple'))
    reg.add('fx_gem', _small_icon(GEM_FX, 'teal'))
