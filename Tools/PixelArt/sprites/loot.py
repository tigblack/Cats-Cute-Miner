"""Добыча с камней (группа loot): кучка монет, кристаллы эликсира, самоцвет и тёплый ореол под ними.

Добыча лежит на клетке поля, пока игрок не соберёт её тапом. Каждый предмет повторяет иконку своей валюты в HUD
(монета, капля-кристалл, огранённый самоцвет) — по тапу он улетает в счётчик и узнаётся сразу.
loot_gold / loot_elixir / loot_hard — холст 20×18, pivot «ноги» (10, 0): нижний ряд — точка касания пола.
loot_glow — alpha-ореол 28×12, pivot — центр; ставится под добычу центром на 3 px выше её ног.
Контур ink1, как у иконок; свет сверху-слева.
"""
from __future__ import annotations

from typing import Dict, Optional, Sequence, Set, Tuple

from pxl import Canvas, with_alpha

LW, LH = 20, 18
PIVOT = (LW / 2.0, 0.0)
OUT = 'ink1'

Px = Tuple[int, int]


def _stamp(cv: Canvas, rows: Sequence[str], legend: Dict[str, str], x0: int, y0: int) -> None:
    for j, r in enumerate(rows):
        for i, ch in enumerate(r):
            if ch in '. ':
                continue
            cv.set(x0 + i, y0 + j, legend[ch])


def _mask(cv: Canvas) -> Set[Px]:
    return {(x, y) for y in range(cv.h) for x in range(cv.w) if cv.opaque(x, y)}


def _outline(cv: Canvas, c: str = OUT) -> None:
    """Контур 1 px снаружи силуэта по 4-соседству."""
    m = _mask(cv)
    for x, y in m:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            if q not in m:
                cv.set(q[0], q[1], c)


# ───────────────────────── монеты ─────────────────────────
# Монеты как в HUD: оранжевый обод (amber3 сверху-слева, amber2 снизу-справа), жёлтое поле с бликом.
def _ell(cx: float, cy: float, rx: float, ry: float) -> Set[Px]:
    out: Set[Px] = set()
    for y in range(LH):
        for x in range(LW):
            if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                out.add((x, y))
    return out


def _rim(m: Set[Px]) -> Set[Px]:
    return {(x, y) for (x, y) in m if any(q not in m for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))}


def coin_flat(cv: Canvas, cx: float, cy: float, rx: float = 4.5, ry: float = 2.0) -> None:
    """Монета плашмя: поле-эллипс в оранжевом ободе, под ним золотое ребро (свет слева) и тёмная щель."""
    face = _ell(cx, cy, rx, ry)
    for k in (2, 1):
        for x, y in {(x, y + k) for (x, y) in face} - face:
            u = (x + 0.5 - cx) / rx
            if k == 2:
                cv.set(x, y, 'amber1')
            else:
                cv.set(x, y, 'gold4' if u < -0.45 else 'amber2' if u > 0.5 else 'gold3')
    rim = _rim(face)
    for x, y in face:
        if (x, y) in rim:
            up = (x + 0.5 - cx) / rx * 0.6 + (y + 0.5 - cy) / ry < 0
            cv.set(x, y, 'amber3' if up else 'amber2')
        else:
            cv.set(x, y, 'gold5' if (x + 0.5 - cx) < -rx * 0.35 and y + 0.5 < cy else 'gold4' if y + 0.5 < cy else 'gold3')


def stack(cx: float, bottom: float, n: int, rx: float = 4.5, ry: float = 2.0) -> Canvas:
    """Столбик из n монет: видно поле верхней и рёбра остальных; общий контур."""
    cv = Canvas(LW, LH)
    for i in range(n):
        coin_flat(cv, cx, bottom - 2 * i, rx, ry)
    _outline(cv)
    return cv


def coin_up(cv: Canvas, cx: float, cy: float, rx: float = 3.5, ry: float = 3.5) -> None:
    """Монета на ребре, лицом к зрителю (rx < ry — повёрнута боком): обод, выпуклое поле, ребро справа."""
    face = _ell(cx, cy, rx, ry)
    edge = {(x + 1, y) for (x, y) in face} - face
    for x, y in edge:
        cv.set(x, y, 'amber1')
    rim = _rim(face)
    inner = face - rim
    for x, y in face:
        s = (x + 0.5 - cx) / rx + (y + 0.5 - cy) / ry
        if (x, y) in rim:
            cv.set(x, y, 'amber3' if s < 0 else 'amber2')
        else:
            cv.set(x, y, 'gold4' if s < -0.6 else 'gold2' if s > 0.7 else 'gold3')
    hl = min(inner, key=lambda q: (q[0] + q[1], q[1]), default=None)
    if hl:
        cv.set(hl[0], hl[1], 'gold5')


def _coin_up(cx: float, cy: float, rx: float, ry: float) -> Canvas:
    """Монета на ребре отдельным слоем со своим контуром — отделяется от столбика за ней."""
    cv = Canvas(LW, LH)
    coin_up(cv, cx, cy, rx, ry)
    _outline(cv)
    return cv


def loot_gold() -> Canvas:
    """Кучка монет: столбик из трёх сзади, монета на ребре справа, две раскатились впереди; искорка-блик."""
    cv = Canvas(LW, LH)
    cv.blit(stack(7.0, 10.0, 3), 0, 0)
    cv.blit(_coin_up(13.5, 10.0, 3.5, 3.5), 0, 0)
    cv.blit(stack(5.0, 15.0, 1, 4.0), 0, 0)
    cv.blit(stack(14.0, 15.0, 1, 4.0), 0, 0)
    for x, y, c in ((17, 1, 'gold5'), (16, 2, 'gold5'), (18, 2, 'gold5'), (17, 3, 'gold5'), (17, 2, 'cream4')):
        cv.set(x, y, c)
    return cv


# ───────────────────────── эликсир ─────────────────────────
# Капля-кристалл как иконка эликсира: острый верх, левая грань светлая, правая тёмная, чаша снизу.
DROP_BIG = [
    '....65....',
    '...6654...',
    '...6554...',
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
DROP_SMALL = [
    '..65..',
    '.6654.',
    '.6554.',
    '665443',
    '554443',
    '.4433.',
    '..32..',
]
DROP_TINY = [
    '.5.',
    '654',
    '443',
    '.3.',
]


def _purple(rows: Sequence[str], x: int, y: int, glint: Optional[Tuple[int, int]] = None) -> Canvas:
    cv = Canvas(LW, LH)
    _stamp(cv, rows, {str(i): f'purple{i}' for i in range(7)}, x, y)
    if glint:
        cv.set(x + glint[0], y + glint[1], 'cream4')
    _outline(cv)
    return cv


def loot_elixir() -> Canvas:
    """Большая капля-кристалл эликсира, рядом поменьше и совсем маленькая — россыпь на полу."""
    cv = Canvas(LW, LH)
    cv.blit(_purple(DROP_BIG, 6, 4, (3, 3)), 0, 0)
    cv.blit(_purple(DROP_SMALL, 2, 10, (2, 2)), 0, 0)
    cv.blit(_purple(DROP_TINY, 15, 13), 0, 0)
    return cv


# ───────────────────────── самоцвет ─────────────────────────
GEM_BIG = [
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
GEM_SMALL = [
    '.5664.',
    '555443',
    '.5443.',
    '..43..',
]


def _teal(rows: Sequence[str], x: int, y: int, glint: Optional[Tuple[int, int]] = None) -> Canvas:
    cv = Canvas(LW, LH)
    _stamp(cv, rows, {str(i): f'teal{i}' for i in range(7)}, x, y)
    if glint:
        cv.set(x + glint[0], y + glint[1], 'white')
    _outline(cv)
    return cv


def loot_hard() -> Canvas:
    """Огранённый бирюзовый самоцвет (как иконка твёрдой валюты) и осколок поменьше рядом."""
    cv = Canvas(LW, LH)
    cv.blit(_teal(GEM_BIG, 2, 6, (4, 1)), 0, 0)
    cv.blit(_teal(GEM_SMALL, 13, 13), 0, 0)
    return cv


# ───────────────────────── ореол ─────────────────────────
GLOW_W, GLOW_H = 28, 12
GLOW_STEPS = [(14.0, 6.0, 'amber3', 34), (11.5, 4.8, 'amber3', 58), (8.5, 3.6, 'amber4', 88), (5.5, 2.4, 'amber5', 124)]


def loot_glow() -> Canvas:
    """Тёплое пятно света на полу под добычей: 4 вложенных эллипса, у каждого своя альфа (без градиента)."""
    cv = Canvas(GLOW_W, GLOW_H)
    cx, cy = GLOW_W / 2.0, GLOW_H / 2.0
    for rx, ry, col, a in GLOW_STEPS:
        for y in range(GLOW_H):
            for x in range(GLOW_W):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                    cv.set(x, y, with_alpha(col, a))
    return cv


# ───────────────────────── груз: мешочки с монетами (новый режим, ТЗ §7) ─────────────────────────
# Мешочек появляется у цели по мере добычи и лежит, пока котик его не подберёт (тап его не собирает).
# Мешковина как у декоративного sack (env.py), но контур ink1, как у добычи, и золото в горловине и на заплатке:
# груз читается как «монеты в пути», а не как декор стены. Свет сверху-слева.
SACK_LEG = {'o': 'ink1', 'x': 'cream0', 'y': 'cream1', 'z': 'cream2', 'm': 'wood4', 'n': 'wood2',
            'G': 'gold4', 'g': 'gold3', 'd': 'gold2', 'C': 'gold3', 'c': 'gold2', 'W': 'gold5'}
SACK = [
    '...oooooo...',
    '..oGWgGgdo..',
    '...oyzzyo...',
    '....oyyo....',
    '...ommmno...',
    '....oyyo....',
    '...oyzzyo...',
    '..oyzzyyyo..',
    '.oyzzyyyyxo.',
    '.oyzyyCCyxo.',
    'oyzyyyCcyyxo',
    'oyyyyyyyyxxo',
    'oyyyyyyyxxxo',
    '.oxxxxxxxxo.',
    '..oooooooo..',
]
SACK_SMALL = [
    '..oooooo..',
    '..oGWgdo..',
    '...oyyo...',
    '..ommmno..',
    '...oyzo...',
    '..oyzzyo..',
    '.oyzyyyxo.',
    'oyzyyCCyxo',
    'oyyyyCcyxo',
    'oyyyyyyxxo',
    '.oxxxxxxo.',
    '..oooooo..',
]


def _sack(rows: Sequence[str] = SACK, lean: int = 0) -> Canvas:
    """Мешочек отдельным слоем со своим контуром; lean = ±1 — горловина и верёвка сдвинуты (мешок завалился)."""
    cv = Canvas.from_ascii(rows, SACK_LEG)
    if lean:
        out = Canvas(cv.w + 1, cv.h)
        neck = len(rows) // 2
        for y in range(cv.h):
            dx = lean if y < neck else 0
            for x in range(cv.w):
                p = cv.px[x, y]
                if p[3]:
                    out.px[x + dx + (1 if lean < 0 else 0), y] = p
        return out
    return cv


def _on(w: int, h: int, items: Sequence[Tuple[Canvas, int, int]]) -> Canvas:
    cv = Canvas(w, h)
    for c, x, y in items:
        cv.blit(c, x, y)
    return cv


def _loose_coins(cv: Canvas, pts: Sequence[Tuple[int, int]]) -> None:
    """Раскатившиеся монетки плашмя у мешков 4×2 (поле и ребро), каждая со своим контуром; (x, y) — левый верх поля."""
    for x, y in pts:
        lay = Canvas(cv.w, cv.h)
        lay.hline(x, x + 3, y, 'gold4')
        lay.set(x, y, 'gold5')
        lay.set(x + 3, y, 'gold3')
        lay.hline(x, x + 3, y + 1, 'amber2')
        lay.set(x + 3, y + 1, 'amber1')
        _outline(lay)
        cv.blit(lay, 0, 0)


def cargo_sack_1() -> Canvas:
    """Один мешочек 12×15 на холсте 16×16, ноги (8, 0)."""
    return _on(16, 16, [(_sack(), 2, 1)])


def cargo_sack_2() -> Canvas:
    """Два мешочка: большой сзади-слева, маленький завалился вправо; 20×16, ноги (10, 0)."""
    return _on(20, 16, [(_sack(), 1, 1), (_sack(SACK_SMALL, 1), 9, 4)])


def cargo_heap_s() -> Canvas:
    """Небольшая куча: три мешочка и пара монеток; 25×19, ноги (12, 0)."""
    cv = _on(25, 19, [(_sack(), 6, 0), (_sack(SACK_SMALL, -1), 0, 6), (_sack(SACK_SMALL, 1), 13, 6)])
    _loose_coins(cv, [(1, 16), (20, 16)])
    return cv


def cargo_heap_l() -> Canvas:
    """Большая куча (много несданного груза): пять мешочков горкой, монеты вокруг; 33×25, ноги (16, 0)."""
    cv = _on(33, 25, [(_sack(), 4, 1), (_sack(), 15, 0), (_sack(SACK_SMALL, -1), 0, 11), (_sack(), 9, 8),
                      (_sack(SACK_SMALL, 1), 20, 11)])
    _loose_coins(cv, [(2, 22), (22, 22), (28, 20)])
    for x, y, c in ((27, 2, 'gold5'), (26, 3, 'gold5'), (28, 3, 'gold5'), (27, 4, 'gold5'), (27, 3, 'cream4')):
        cv.set(x, y, c)
    return cv


CARGO_GLOW_STEPS = [(14.0, 6.0, 'gold3', 30), (11.5, 4.8, 'gold3', 52), (8.5, 3.6, 'gold4', 80), (5.5, 2.4, 'gold5', 110)]


def cargo_glow() -> Canvas:
    """Золотистое пятно под грузом (тусклее, чем loot_glow): мешочки видны на любом полу; центр, ставить на 3 px выше ног."""
    cv = Canvas(GLOW_W, GLOW_H)
    cx, cy = GLOW_W / 2.0, GLOW_H / 2.0
    for rx, ry, col, a in CARGO_GLOW_STEPS:
        for y in range(GLOW_H):
            for x in range(GLOW_W):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                    cv.set(x, y, with_alpha(col, a))
    return cv


# Появление мешочка у цели: облачко пыли расходится кольцом, золотые искры разлетаются; 3 кадра по 0,06 с.
POP = [
    [
        '................',
        '................',
        '................',
        '................',
        '.......Y........',
        '......YWY.......',
        '.......Y........',
        '................',
        '....p......p....',
        '...ppP....pPp...',
        '..pPPp....pPPp..',
        '...pp..pp..pp...',
        '......pPPp......',
        '.......pp.......',
        '................',
        '................',
    ],
    [
        '................',
        '..Y.........Y...',
        '.YWY.......YWY..',
        '..Y.....Y...Y...',
        '.......YWY......',
        '........Y.......',
        '................',
        '.p............p.',
        'pPp..........pPp',
        '.pp..........pp.',
        '................',
        '..p..........p..',
        '.pPp........pPp.',
        '..pp..pp.pp.pp..',
        '.....pPppPPp....',
        '......pp..p.....',
    ],
    [
        'Y..............Y',
        '................',
        '................',
        '.......y........',
        '................',
        '................',
        '................',
        'p..............p',
        '................',
        '................',
        '................',
        '................',
        'p..............p',
        '................',
        '..p..........p..',
        '.....p....p.....',
    ],
]
POP_LG = {'Y': 'gold4', 'W': 'cream4', 'y': 'gold3', 'p': 'cream1', 'P': 'cream2'}


def sack_pop(i: int) -> Canvas:
    return Canvas.from_ascii(POP[i], POP_LG)


def build(reg) -> None:
    reg.add('loot_gold', loot_gold(), pivot=PIVOT, note='кучка монет, ноги (10, 0)')
    reg.add('loot_elixir', loot_elixir(), pivot=PIVOT, note='капли-кристаллы эликсира, ноги (10, 0)')
    reg.add('loot_hard', loot_hard(), pivot=PIVOT, note='самоцвет, ноги (10, 0)')
    reg.add('loot_glow', loot_glow(), alpha=True, note='ореол под добычей, центр')
    # Груз нового режима: мешочки с монетами на земле (ТЗ §7.2), ноги — нижний центр.
    for name, fn in (('cargo_sack_1', cargo_sack_1), ('cargo_sack_2', cargo_sack_2), ('cargo_heap_s', cargo_heap_s),
                     ('cargo_heap_l', cargo_heap_l)):
        cv = fn()
        reg.add(name, cv, pivot=(float(cv.w // 2), 0.0), note='груз на земле: ' + (fn.__doc__ or '').split(':')[0])
    reg.add('cargo_glow', cargo_glow(), alpha=True, note='пятно под грузом, центр (на 3 px выше ног груза)')
    for i in range(3):
        reg.add(f'fx_sack_pop_{i}', sack_pop(i), note='появление мешочка у цели, 0,06 с/кадр, центр')
