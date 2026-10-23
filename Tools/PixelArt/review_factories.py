#!/usr/bin/env python3
"""Сводные листы арта заводов для владельца (ТЗ 2.2 §11): Docs/Reports/factories/art/*.png.

  python3 Tools/PixelArt/review_factories.py [--out DIR]

Листы (рисуются из тех же функций sprites/*.py, что и атлас; атлас не пишется):
  factories.png  — 20 заводов в четырёх состояниях на плитах пола: Locked (затемнение как в коде вида + цепи + полоса HP),
                   Working (кадр работы + полоса цикла), Ready (полная полоса и кнопка «Deliver ×20» с иконкой товара),
                   Stopped (здание без анимации); ×2;
  factories_anim.png — кадры работы 0…3 для трёх видов дыма, стадии трещин цепей, разлёт цепей fx_chain_break_*; ×3;
  goods.png      — иконки товаров 16×16 и 8×8 (×6; на тёмной и светлой панели — 16×16 ×1, 8×8 ×2 и ×1), полоса трубы
                   и капсула цвета товара;
  pipes.png      — детали труб ×6, заглушки со всеми иконками, капсулы всех цветов, пример трассы с жгутом и муфтой до
                   склада во дворе лагеря, склад пустой / с товаром;
  icons.png      — новые иконки UI ×6 рядом с соседними (icon_store, icon_depot, icon_shaft, icon_lock) и ×2.
Цвета состояний, которых нет в спрайтах (затемнение Locked, окраска капсулы), повторяют умножение цвета в Unity.
"""
from __future__ import annotations

import argparse
import os
import sys
from typing import Tuple

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import pxl  # noqa: E402
from manifest import GOODS  # noqa: E402
from sprites import base as base_art  # noqa: E402
from sprites import env as env_art  # noqa: E402
from sprites import factories as F  # noqa: E402
from sprites import fx as fx_art  # noqa: E402
from sprites import goods as goods_art  # noqa: E402
from sprites import icons as icons_art  # noqa: E402

OUT = os.path.join(ROOT, 'Docs', 'Reports', 'factories', 'art')
BG = (40, 40, 52, 255)
TEXT = (230, 230, 240, 255)
LOCKED_TINT = (0.45, 0.45, 0.55)
NAMES = {
    'milk': 'Dairy · Milk', 'wood': 'Sawmill · Wood', 'fish': 'Fishery · Fish', 'yarn': 'Yarn Mill · Yarn',
    'catfood': 'Cat Food Factory · Cat Food', 'boxes': 'Box Factory · Boxes', 'bricks': 'Brickworks · Bricks',
    'steel': 'Steel Mill · Steel', 'glass': 'Glassworks · Glass', 'catnip': 'Catnip Farm · Catnip',
    'toys': 'Toy Factory · Toys', 'scratchers': 'Scratcher Works · Scratching Posts', 'cushions': 'Cushion Factory · Cushions',
    'bells': 'Bell Foundry · Bells', 'collars': 'Collar Works · Collars', 'gears': 'Gear Works · Gears',
    'lanterns': 'Lantern Works · Lanterns', 'honey': 'Apiary · Honey', 'silk': 'Silk Mill · Silk',
    'caviar': 'Caviar Cannery · Caviar',
}


def rgb(name: str) -> Tuple[int, int, int]:
    return pxl.PALETTE[name]


def tinted(cv: pxl.Canvas, k: Tuple[float, float, float]) -> pxl.Canvas:
    """Умножение цвета, как SpriteRenderer.color в Unity (для листа; в атласе таких цветов нет)."""
    out = pxl.Canvas(cv.w, cv.h)
    for y in range(cv.h):
        for x in range(cv.w):
            r, g, b, a = cv.px[x, y]
            if a:
                out.px[x, y] = (int(r * k[0]), int(g * k[1]), int(b * k[2]), a)
    return out


def floor_patch(w_cells: int, h_cells: int) -> pxl.Canvas:
    cv = pxl.Canvas(w_cells * 32, h_cells * 32)
    for j in range(h_cells):
        for i in range(w_cells):
            cv.blit(env_art.floor_tile((i + 2 * j) % 4), i * 32, j * 32)
    return cv


def bar(cv: pxl.Canvas, x: int, y: int, w: int, frac: float, fill: str) -> None:
    """Полоса над зданием (для листа): рамка ink1, поле navy0, заливка fill."""
    cv.rect(x, y, w, 4, 'ink1')
    cv.rect(x + 1, y + 1, w - 2, 2, 'navy0')
    n = int(round((w - 2) * frac))
    if n:
        cv.rect(x + 1, y + 1, n, 2, fill)
        cv.hline(x + 1, x + n, y + 1, 'gold5' if fill == 'gold4' else ('green6' if fill.startswith('green') else 'red5'))


def state_canvas(good: str, state: str) -> pxl.Canvas:
    """Завод в состоянии state на плитах пола 2×4 клетки (верхняя клетка — запас под колесо)."""
    cv = floor_patch(2, 4)
    oy = cv.h - F.FH
    b = F.factory_base(good)
    if state == 'locked':
        cv.blit(tinted(b, LOCKED_TINT), 0, oy)
        cv.blit(F.chains(0), 0, oy)
        cv.blit(F.chains_crack(1), 0, oy)
        bar(cv, 12, oy - 6, 40, 0.62, 'red4')
    elif state == 'working':
        cv.blit(b, 0, oy)
        cv.blit(F.work_overlay(good, 1), 0, oy)
        bar(cv, 12, oy - 6, 40, 0.45, 'gold4')
    elif state == 'ready':
        cv.blit(b, 0, oy)
        bar(cv, 12, oy - 6, 40, 1.0, 'green4')
        # Кнопка «Deliver ×20» (макет; в игре — PixelUi над заводом).
        bx, by = 8, oy + 44
        cv.rect(bx, by, 48, 16, 'green1')
        cv.rect(bx + 1, by + 1, 46, 14, 'green4')
        cv.hline(bx + 1, bx + 46, by + 1, 'green5')
        cv.hline(bx + 1, bx + 46, by + 14, 'green3')
        cv.blit(goods_art.goods_icon(good, 16), bx + 1, by)
    else:
        cv.blit(b, 0, oy)
    return cv


def label(d: ImageDraw.ImageDraw, x: int, y: int, text: str) -> None:
    d.text((x, y), text, fill=TEXT)


def sheet_factories(path: str) -> None:
    S = 2
    states = ['locked', 'working', 'ready', 'stopped']
    cw, ch = 64 * S + 12, 128 * S + 26
    cols = 10
    img = Image.new('RGBA', (cols * cw + 90, 2 * (len(states) * ch + 30) + 20), BG)
    d = ImageDraw.Draw(img)
    for half in range(2):
        y0 = 10 + half * (len(states) * ch + 30)
        for si, st in enumerate(states):
            label(d, 6, y0 + si * ch + ch // 2, st.capitalize())
            for ci in range(cols):
                g = GOODS[half * cols + ci]
                x = 90 + ci * cw
                y = y0 + si * ch
                img.alpha_composite(state_canvas(g, st).scaled(S), (x, y))
                if st == 'ready':
                    d.text((x + 8 * S + 34, y + (128 - 112 + 44) * S + 8), 'x20', fill=(20, 40, 20, 255))
                if si == 0:
                    pass
                if si == len(states) - 1:
                    label(d, x, y + 128 * S + 4, NAMES[g].split(' · ')[0])
    img.save(path)


def sheet_anim(path: str) -> None:
    S = 3
    img = Image.new('RGBA', (4 * (64 * S + 10) + 140, 4 * (112 * S + 24) + 140), BG)
    d = ImageDraw.Draw(img)
    rows = [('milk', 'steam'), ('glass', 'smoke'), ('steel', 'soot')]
    for r, (g, style) in enumerate(rows):
        y = 10 + r * (112 * S + 24)
        label(d, 6, y + 40, f'work 0..3\n{style}\n({g})')
        for i in range(4):
            cv = F.factory_base(g)
            cv.blit(F.work_overlay(g, i), 0, 0)
            img.alpha_composite(cv.scaled(S), (140 + i * (64 * S + 10), y))
    y = 10 + 3 * (112 * S + 24)
    label(d, 6, y + 40, 'chains\n0 / crack 1..3\n(Locked)')
    for i in range(4):
        cv = tinted(F.factory_base('wood'), LOCKED_TINT)
        cv.blit(F.chains(0), 0, 0)
        if i:
            cv.blit(F.chains_crack(i), 0, 0)
        img.alpha_composite(cv.scaled(S), (140 + i * (64 * S + 10), y))
    y += 112 * S + 24
    label(d, 6, y + 30, 'fx_chain_break\n0..3 (x4)')
    for i in range(4):
        img.alpha_composite(fx_art.chain_break(i).scaled(4), (140 + i * (32 * 4 + 20), y))
    img.save(path)


def multiplied(cv: pxl.Canvas, color: str) -> pxl.Canvas:
    r, g, b = rgb(color)
    return tinted(cv, (r / 255.0, g / 255.0, b / 255.0))


def sheet_goods(path: str) -> None:
    S = 6
    cw, chh = 16 * S + 30, 16 * S + 8 * S + 36 + 80
    cols = 10
    img = Image.new('RGBA', (cols * cw + 20, 2 * chh + 20), BG)
    d = ImageDraw.Draw(img)
    for i, g in enumerate(GOODS):
        x = 12 + (i % cols) * cw
        y = 10 + (i // cols) * chh
        img.alpha_composite(goods_art.goods_icon(g, 16).scaled(S), (x, y))
        img.alpha_composite(goods_art.goods_icon(g, 8).scaled(S), (x, y + 16 * S + 6))
        # ×2 на тёмной (navy1) и светлой (cream2) панели: 16×16 и 8×8.
        for k, panel in enumerate(('navy1', 'cream2')):
            px, py = x + 8 * S + 8, y + 16 * S + 6 + k * 25
            img.paste(rgb(panel) + (255,), (px, py, px + 58, py + 22))
            img.alpha_composite(goods_art.goods_icon(g, 16).scaled(1), (px + 3, py + 3))
            img.alpha_composite(goods_art.goods_icon(g, 8).scaled(2), (px + 22, py + 3))
            img.alpha_composite(goods_art.goods_icon(g, 8).scaled(1), (px + 44, py + 7))
        # Полоса трубы (контур + цвет) и капсула цвета товара.
        color = goods_art.GOOD_COLOR.get(g, 'cream4')
        yy = y + 16 * S + 8 * S + 16
        pipe = pxl.Canvas(16, 3)
        pipe.hline(0, 15, 0, 'ink1')
        pipe.hline(0, 15, 1, color)
        pipe.hline(0, 15, 2, 'ink1')
        img.alpha_composite(pipe.scaled(S), (x, yy))
        img.alpha_composite(multiplied(F.pipe_capsule(True), color).scaled(S), (x + 5 * S, yy))
        label(d, x, yy + 3 * S + 4, f'{g}\n{color}')
    img.save(path)


def _pipe_v(cv: pxl.Canvas, x: int, y0: int, y1: int, color: str) -> None:
    """Труба 3 px (контур ink1 — полоса цвета товара — контур), вертикальный отрезок; x — левый край."""
    for y in range(min(y0, y1), max(y0, y1) + 1):
        cv.set(x, y, 'ink1')
        cv.set(x + 1, y, color)
        cv.set(x + 2, y, 'ink1')


def _pipe_h(cv: pxl.Canvas, x0: int, x1: int, y: int, color: str) -> None:
    for x in range(min(x0, x1), max(x0, x1) + 1):
        cv.set(x, y, 'ink1')
        cv.set(x, y + 1, color)
        cv.set(x, y + 2, 'ink1')


def _bundle_h(cv: pxl.Canvas, x0: int, x1: int, y: int, colors) -> None:
    """Жгут: общий контур и полоски всех товаров подряд (по 1 px)."""
    for x in range(min(x0, x1), max(x0, x1) + 1):
        cv.set(x, y, 'ink1')
        for k, c in enumerate(colors):
            cv.set(x, y + 1 + k, c)
        cv.set(x, y + 1 + len(colors), 'ink1')


def _bundle_v(cv: pxl.Canvas, x: int, y0: int, y1: int, colors) -> None:
    for y in range(min(y0, y1), max(y0, y1) + 1):
        cv.set(x, y, 'ink1')
        for k, c in enumerate(colors):
            cv.set(x + 1 + k, y, c)
        cv.set(x + 1 + len(colors), y, 'ink1')


def route_demo() -> pxl.Canvas:
    """Пример трассы (макет для владельца, раскладку труб строит код WP5): три завода, у каждого заглушка с иконкой
    товара и своя труба 3 px с шагом 4 px вдоль прохода; в общем проходе трубы сводятся муфтой в жгут с полосками
    всех товаров; жгут доходит до площадки лагеря и декоративным отрезком спускается во вход склада."""
    W, H = 9 * 32, 9 * 32
    cv = pxl.Canvas(W, H)
    for j in range(6):
        for i in range(9):
            cv.blit(env_art.floor_tile((i + 3 * j) % 4), i * 32, j * 32)
    for i in range(9):
        cv.blit(base_art.camp_edge_t(), i * 32, 6 * 32)
        cv.blit(base_art.camp_floor(i % 4), i * 32, 7 * 32)
        cv.blit(base_art.camp_ground(i % 2), i * 32, 8 * 32)
    goods3 = ['milk', 'wood', 'fish']
    colors = [goods_art.GOOD_COLOR[g] for g in goods3]
    xs = [40, 44, 48]                      # трубы вдоль левой стены прохода: шаг 4 px
    caps_y = [24, 40, 56]
    joint_y = 150
    for k, g in enumerate(goods3):
        _pipe_v(cv, xs[k], caps_y[k] + 7, joint_y, colors[k])
        cap = F.pipe_cap()
        cap.blit(goods_art.goods_icon(g, 8), 3, 3)
        cv.blit(cap, xs[k] + 1 - 7, caps_y[k] - 7)
        cv.blit(multiplied(F.pipe_capsule(False), colors[k]), xs[k], 92 + k * 14)
    wx = 8 * 32 + 16
    wh = base_art.warehouse(True)
    wy = 9 * 32 - wh.h + 8
    inlet_y = wy + base_art.WAREHOUSE_INLET[1]
    _bundle_h(cv, xs[0] + 6, wx - 2, joint_y + 1, colors)
    _bundle_v(cv, wx - 2, joint_y + 1, inlet_y - 4, colors)
    cv.blit(F.pipe_coupling(), xs[1] - 4, joint_y - 4)
    cv.blit(F.pipe_coupling(), wx - 6, joint_y - 3)
    cv.blit(wh, wx - 16, wy)
    cv.blit(F.pipe_inlet(), wx - 8, inlet_y - 5)
    return cv


def sheet_pipes(path: str) -> None:
    S = 6
    img = Image.new('RGBA', (1400, 1300), BG)
    d = ImageDraw.Draw(img)
    x = 12
    for name, cv in (('fpipe_cap', F.pipe_cap()), ('fpipe_coupling', F.pipe_coupling()), ('fpipe_inlet', F.pipe_inlet()),
                     ('fpipe_capsule', F.pipe_capsule(False)), ('fpipe_capsule_h', F.pipe_capsule(True))):
        img.alpha_composite(cv.scaled(S), (x, 12))
        label(d, x, 12 + cv.h * S + 4, f'{name} {cv.w}x{cv.h}')
        x += max(cv.w * S, 120) + 20
    # Заглушки со всеми иконками ×4.
    y = 150
    label(d, 12, y, 'fpipe_cap + goods_<good>_8 (x4)')
    for i, g in enumerate(GOODS):
        cap = F.pipe_cap()
        cap.blit(goods_art.goods_icon(g, 8), 3, 3)
        img.alpha_composite(cap.scaled(4), (12 + i * 66, y + 16))
    # Капсулы всех цветов ×6 на отрезке трубы.
    y = 260
    label(d, 12, y, 'capsule x multiply(Color) on its pipe stripe (x6)')
    for i, g in enumerate(GOODS):
        color = goods_art.GOOD_COLOR[g]
        seg = pxl.Canvas(3, 14)
        for yy in range(14):
            seg.set(0, yy, 'ink1')
            seg.set(1, yy, color)
            seg.set(2, yy, 'ink1')
        seg.blit(multiplied(F.pipe_capsule(False), color), 0, 4)
        img.alpha_composite(seg.scaled(S), (12 + i * 66, y + 16))
    # Пример трассы ×2 и склад ×4.
    y = 380
    label(d, 12, y, 'route mock-up (x3): caps with goods icons, 3 px pipes 4 px apart, couplings, bundle, Warehouse inlet')
    img.alpha_composite(route_demo().scaled(3), (12, y + 16))
    label(d, 900, y, 'warehouse_empty / warehouse_full (x4)')
    img.alpha_composite(base_art.warehouse(False).scaled(4), (900, y + 16))
    img.alpha_composite(base_art.warehouse(True).scaled(4), (900 + 140, y + 16))
    img.save(path)


def sheet_icons(path: str) -> None:
    S = 6
    names = ['icon_market', 'icon_auto', 'icon_deliver', 'icon_factory', 'icon_chain', 'icon_store', 'icon_depot', 'icon_shaft',
             'icon_lock']
    extra = {'icon_depot': icons_art.icon_depot, 'icon_store': icons_art.icon_store}
    img = Image.new('RGBA', (len(names) * (16 * S + 24) + 20, 16 * S + 120), BG)
    d = ImageDraw.Draw(img)
    for i, n in enumerate(names):
        fn = icons_art.ICONS.get(n) or extra[n]
        cv = fn()
        x = 12 + i * (16 * S + 24)
        img.alpha_composite(cv.scaled(S), (x, 10))
        img.paste(rgb('navy1') + (255,), (x, 16 * S + 20, x + 40, 16 * S + 56))
        img.alpha_composite(cv.scaled(2), (x + 4, 16 * S + 22))
        label(d, x, 16 * S + 64, n + ('\n(new)' if i < 5 else ''))
    img.save(path)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=OUT)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    sheet_factories(os.path.join(a.out, 'factories.png'))
    sheet_anim(os.path.join(a.out, 'factories_anim.png'))
    sheet_goods(os.path.join(a.out, 'goods.png'))
    sheet_pipes(os.path.join(a.out, 'pipes.png'))
    sheet_icons(os.path.join(a.out, 'icons.png'))
    for n in ('factories', 'factories_anim', 'goods', 'pipes', 'icons'):
        print(f'[review] {os.path.relpath(os.path.join(a.out, n + ".png"), ROOT)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
