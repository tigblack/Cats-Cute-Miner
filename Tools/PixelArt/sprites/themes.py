"""Оформления окружения (Docs/Visual/ART_DIRECTION.md, «Окружение»).

Три совместимых оформления шахты: тёплая деревянная штольня (базовый арт sprites/env.py и препятствия sprites/rocks.py),
прохладная кристальная пещера (суффикс _crystal) и глубокая медная выработка (_copper). Оформление выбирает игра по
диапазонам зон (Assets/Game/Resources/Visual/environment_themes.json) — это только вид: карты и награды те же.

Варианты тем — перекраска базовых спрайтов по таблице рамп: формы, швы тайлов, pivot и сетка не меняются, меняются
камень стен, плиты пола, мох и кристаллы. Камни добычи, постройки, котики и UI не перекрашиваются: геймплей читается
одинаково во всех шахтах. Свои детали тем: медные трубы с вентилем и медленные шестерни выработки, высокие друзы пещеры.
Игра берёт вариант темы, если он есть в атласе (Runtime/Visual/PixelTheme.cs).
"""
import math
from typing import Dict, List

import pxl
from pxl import Canvas

# Перекраска рамп: имя цвета → имя цвета. Рампы одной длины переходят ступень в ступень.
def _ramp(src: str, dst: str, offset: int = 0) -> Dict[str, str]:
    n = len(pxl.RAMPS[dst])
    return {f'{src}{i}': f'{dst}{min(n - 1, max(0, i + offset))}' for i in range(len(pxl.RAMPS[src]))}


THEMES: Dict[str, Dict[str, str]] = {
    # Холодная пещера: сине-бирюзовый камень, серо-голубые плиты, светящийся бирюзовый лишайник и бирюзовые друзы.
    'crystal': {**_ramp('navy', 'cave'), **_ramp('slate', 'frost'), **_ramp('green', 'teal'), **_ramp('purple', 'teal')},
    # Медная выработка: красно-бурая порода, тёплый серо-бурый пол, бирюзовая патина вместо мха и синие друзы азурита.
    'copper': {**_ramp('navy', 'copper'), **_ramp('slate', 'rust'), **_ramp('green', 'teal', -1), **_ramp('purple', 'blue', 1)},
}

# Что перекрашивается: пол, стены, углы, кромки, порог ворот, препятствия-скалы, столбы ворот, декор стен с камнем
# или мхом (подпятник опоры, табличка, друзы, грибы) и скальные части построек — плечи входа шахты и фон колеса
# копра, скала выхода. Сами постройки (дерево, железо, кровля) не меняются.
PREFIXES = ('floor_', 'wall_', 'block_', 'waterfall_', 'beam_post', 'sign_question', 'crystal_cluster_', 'mushrooms',
            'shaft_base', 'shaft_wheel_', 'exit_base', 'exit_boards',
            # новый режим: стадии расчистки препятствий и месторождения, след расчистки (камень пещеры и мох)
            'rubble_debris_', 'shaft_crack_', 'shaft_cleared')
GROUPS = ('env', 'rocks', 'structures')
# Ореол друз по теме (sprites/fx.py, GLOWS): фиолетовый аметист, бирюза пещеры, синий азурит.
GLOW = {'warm': 'fx_glow_purple', 'crystal': 'fx_glow_teal', 'copper': 'fx_glow_blue'}


def recolor(cv: Canvas, table: Dict[str, str]) -> Canvas:
    rgb = {pxl.PALETTE[a]: pxl.PALETTE[b] for a, b in table.items()}
    out = cv.copy()
    for y in range(cv.h):
        for x in range(cv.w):
            r, g, b, a = cv.px[x, y]
            if a == 0:
                continue
            to = rgb.get((r, g, b))
            if to is not None:
                out.px[x, y] = (to[0], to[1], to[2], a)
    return out


# ───────────────────────── Детали медной выработки ─────────────────────────
GEAR = 22                 # холст шестерни
GEAR_FRAMES = 8           # 8 зубцов и 4 спицы: период 90°, кадр — 11,25°
GEAR_R_OUT, GEAR_R_IN, GEAR_R_RIM, GEAR_R_HUB = 10.4, 8.2, 6.2, 2.4


def gear(frame: int) -> Canvas:
    """Медная шестерня на стене 22×22 (блестящая медь — рампа fur_ginger), кадр поворота frame (0…7, по 11,25°): 8 зубцов, обод, 4 спицы и ступица
    с латунной осью. Свет сверху-слева, контур — тёмная медь; поворачивается медленно (BoardView, «Механизмы»)."""
    cv = Canvas(GEAR, GEAR)
    c = GEAR / 2.0
    turn = frame * (math.pi / 2) / GEAR_FRAMES
    tooth = 2 * math.pi / 8
    for y in range(GEAR):
        for x in range(GEAR):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            r = math.hypot(dx, dy)
            a = (math.atan2(dy, dx) - turn) % tooth
            # Зубец — по центру своего сектора (симметричен): пара, сдвинутая на ползубца, зацепляется зубец во впадину.
            outer = GEAR_R_OUT if abs(a - tooth / 2) < tooth * 0.23 else GEAR_R_IN
            if r > outer:
                continue
            # Спицы: 4 полосы шириной ~2,4 px между ободом и ступицей.
            sa = (math.atan2(dy, dx) - turn) % (math.pi / 2)
            spoke = min(sa, math.pi / 2 - sa) * r < 1.25
            if GEAR_R_HUB < r < GEAR_R_RIM - 1.6 and not spoke:
                continue                                  # окна между спицами — видна стена
            light = -(dx * 0.62 + dy * 0.78) / max(r, 0.01)
            if r <= GEAR_R_HUB:
                col = 'gold4' if r < 1.2 else ('gold3' if light > 0 else 'gold2')
            elif r > GEAR_R_IN - 0.4:                       # зубцы
                col = 'fur_ginger4' if light > 0.55 else ('fur_ginger3' if light > -0.2 else 'fur_ginger2')
            elif r > GEAR_R_RIM - 1.6:                      # обод
                col = 'fur_ginger3' if light > 0.3 else ('fur_ginger2' if light > -0.4 else 'fur_ginger1')
            else:                                           # спицы
                col = 'fur_ginger2' if light > 0 else 'fur_ginger1'
            cv.set(x, y, col)
    cv.outline('copper0')
    return cv


def pipe() -> Canvas:
    """Медная труба вдоль стены 12×30: два фланца с заклёпками, красный вентиль и потёки патины."""
    w, h = 12, 30
    cv = Canvas(w, h)
    x0 = 3
    for y in range(h):
        cv.set(x0, y, 'fur_ginger4')
        cv.set(x0 + 1, y, 'fur_ginger3')
        cv.set(x0 + 2, y, 'fur_ginger3')
        cv.set(x0 + 3, y, 'fur_ginger2')
        cv.set(x0 + 4, y, 'fur_ginger1')
    for fy in (3, 21):
        cv.rect(x0 - 2, fy, 9, 3, 'fur_ginger2')
        cv.hline(x0 - 2, x0 + 6, fy, 'fur_ginger4')
        cv.hline(x0 - 2, x0 + 6, fy + 2, 'fur_ginger1')
        cv.set(x0 - 1, fy + 1, 'gold4')
        cv.set(x0 + 5, fy + 1, 'gold3')
    # Потёки патины под фланцами.
    for x, y0, n in ((x0 + 1, 24, 3), (x0 + 3, 6, 2), (x0 + 2, 10, 1)):
        for k in range(n):
            cv.set(x, y0 + k, 'teal3' if k == 0 else 'teal2')
    # Вентиль: шток вправо и красное колесо.
    cv.hline(x0 + 5, x0 + 6, 13, 'stone4')
    wheel = [
        '.rRr.',
        'r.R.r',
        'RRhRR',
        'r.R.r',
        '.rRr.',
    ]
    cv.blit(Canvas.from_ascii(wheel, {'r': 'red2', 'R': 'red3', 'h': 'gold4'}), x0 + 4, 11)
    cv.outline('copper0')
    return cv


# ───────────────────────── Детали кристальной пещеры ─────────────────────────
def spire() -> Canvas:
    """Высокая бирюзовая друза пещеры 18×30 на каменном основании: три кристалла, левая грань светлая, правая тёмная."""
    w, h = 18, 30
    cv = Canvas(w, h)
    base = Canvas(w, h)
    base.ellipse(9, h - 3.5, 8.0, 3.0, 'cave4')
    base.shade('cave6', 'cave3', 'cave4')
    cv.blit(base, 0, 0)

    def crystal(cx: int, top: int, bottom: int, half: int, lean: int) -> None:
        for y in range(top, bottom + 1):
            t = y - top
            sh = (lean * (bottom - y) + (bottom - top) // 2) // max(1, bottom - top)
            span = min(half, (t + 1) // 2 + 1)
            for xx in range(cx - span, cx + span):
                col = 'teal5' if xx < cx else 'teal3'
                if xx == cx - span:
                    col = 'teal6' if t < 5 else 'teal5'
                if xx == cx and t >= 3:
                    col = 'teal4'
                cv.set(xx + sh, y, col)

    crystal(5, 10, h - 4, 3, -3)
    crystal(13, 13, h - 4, 3, 3)
    crystal(9, 1, h - 4, 4, 0)
    cv.set(8, 3, 'teal6')
    cv.set(4, 12, 'teal6')
    cv.outline('teal1')
    for y in range(h - 5, h):
        for x in range(w):
            if cv.get(x, y)[:3] == pxl.PALETTE['teal1'] and not cv.get(x, y - 1)[:3] in _TEALS:
                cv.set(x, y, 'cave1')
    return cv


_TEALS = {pxl.PALETTE[f'teal{i}'] for i in range(2, 7)}


# ───────────────────────── Регистрация ─────────────────────────
def _bases(reg: pxl.Registry) -> List[pxl.SpriteEntry]:
    """Базовые спрайты для перекраски: из реестра сборки (env, rocks и structures собраны раньше) или собранные
    заново (частичная сборка --only themes)."""
    src = reg
    if not all(n in reg.entries for n in ('floor_0', 'block_c', 'shaft_base')):
        import importlib
        src = pxl.Registry()
        for m in GROUPS:
            src.group = m
            importlib.import_module('sprites.' + m).build(src)
    out = []
    for name, e in list(src.entries.items()):
        if e.group not in GROUPS or not name.startswith(PREFIXES):
            continue
        if name.endswith(('_crystal', '_copper')):
            continue
        out.append(e)
    return out


def build(reg: pxl.Registry) -> None:
    bases = _bases(reg)
    for theme, table in THEMES.items():
        for e in bases:
            cv = recolor(e.canvas, table)
            if cv.img.tobytes() == e.canvas.img.tobytes():
                continue                     # в спрайте нет перекрашиваемых цветов — тема берёт базовый
            reg.add(f'{e.name}_{theme}', cv, pivot=e.pivot, border=e.border, alpha=e.alpha, extrude=e.extrude,
                    note=f'оформление «{theme}»: {e.name}')
    for i in range(GEAR_FRAMES):
        cv = gear(i)
        reg.add(f'gear_copper_{i}', cv, note='шестерня медной выработки, 0,2 с/кадр; центр — ось')
    cv = pipe()
    reg.add('pipe_copper', cv, pivot=pxl.pivot_feet(cv), note='медная труба на стене выработки; опора — низ')
    cv = spire()
    reg.add('crystal_spire', cv, pivot=pxl.pivot_feet(cv), note='высокая друза кристальной пещеры; опора — низ')
