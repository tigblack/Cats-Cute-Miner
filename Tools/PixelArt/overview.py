#!/usr/bin/env python3
"""Обзорные листы визуального прохода (Docs/Visual/sprites на ПК): окрасы × оружие котиков (круг из 8 уровней), все
кадры котика с киркой и с бензопилой, цвета касок,
стадии повреждения камней, три оформления окружения на макете поля, состояния шахты, пара шестерён.

Запуск на хабе: Tools/.venv/bin/python Tools/PixelArt/overview.py [--out Tools/PixelArt/out/overview]
PNG не синхронизируются с ПК: перенос — tar + base64 + Tools/Audio/unpack_transfer.ps1 (Docs/Audio/SOUNDS.md).
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from PIL import Image, ImageChops, ImageDraw  # noqa: E402

import build  # noqa: E402
import manifest  # noqa: E402
import pxl  # noqa: E402
from sprites import themes  # noqa: E402

BG = pxl.PALETTE['slate3'] + (255,)
TXT = (240, 230, 210, 255)
T = 32


def cat(E, fur, weapon, colour, frame):
    """Котик: тело окраса, каска цвета круга, оружие; у инструмента кадры добычи mine_* заменяются кадрами tool_*."""
    if weapon in manifest.TOOLS and frame.startswith('mine_'):
        frame = frame.replace('mine_', 'tool_')
    im = Image.new('RGBA', (40, 40), (0, 0, 0, 0))
    for layer in (f'cat_body_{fur}_{frame}', f'cat_helmet_{colour}_{frame}', f'cat_weapon_{weapon}_{frame}'):
        if layer in E:
            im.alpha_composite(E[layer].canvas.img, (0, 0))
    return im


def cats_sheets(E, out):
    furs, weapons = manifest.FURS, manifest.WEAPONS
    cell = 46
    # Окрасы × оружие уровней 1–8 (каска первого круга) и строка «уровни 9–16» — тот же круг в каске второго цвета.
    sheet = Image.new('RGBA', (64 + cell * len(weapons), 20 + cell * (len(furs) + 1)), BG)
    d = ImageDraw.Draw(sheet)
    for j, w in enumerate(weapons):
        d.text((64 + j * cell + 2, 4), f'{j + 1} {w[:7]}', fill=TXT)
    rows = [(f, manifest.HELMET_COLORS[0], f) for f in furs] + [('gray', manifest.HELMET_COLORS[1], 'lv 9-16')]
    for i, (fur, colour, label) in enumerate(rows):
        d.text((4, 20 + i * cell + 16), label, fill=TXT)
        for j, w in enumerate(weapons):
            sheet.alpha_composite(cat(E, fur, w, colour, 'idle0'), (64 + j * cell + 3, 20 + i * cell + 3))
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(os.path.join(out, 'cats_coats_x_weapons.png'))
    # Каски всех кругов.
    sh = Image.new('RGBA', (len(manifest.HELMET_COLORS) * cell, 64), BG)
    d = ImageDraw.Draw(sh)
    for k, colour in enumerate(manifest.HELMET_COLORS):
        sh.alpha_composite(cat(E, 'ginger', 'pick', colour, 'idle0'), (k * cell + 3, 2))
        d.text((k * cell + 2, 46), f'{8 * k + 1}-{8 * k + 8}', fill=TXT)
    sh.resize((sh.width * 4, sh.height * 4), Image.NEAREST).save(os.path.join(out, 'cats_helmet_cycles.png'))
    # Все кадры: кирка (замах) и бензопила (с упора).
    frames = [f for f in manifest.CAT_FRAMES if not f.startswith('tool_')]
    cols, cw, ch = 9, 62, 54
    nrows = (len(frames) + cols - 1) // cols
    for fur, weapon, colour in (('ginger', 'pick', 'yellow'), ('black', 'chainsaw', 'blue')):
        sh = Image.new('RGBA', (cols * cw, nrows * ch), BG)
        d = ImageDraw.Draw(sh)
        for k, fr in enumerate(frames):
            x, y = (k % cols) * cw, (k // cols) * ch
            sh.alpha_composite(cat(E, fur, weapon, colour, fr), (x + 4, y + 2))
            label = fr.replace('mine_', 'tool_') if weapon in manifest.TOOLS and fr.startswith('mine_') else fr
            d.text((x + 2, y + 42), label[:10], fill=TXT)
        sh.resize((sh.width * 3, sh.height * 3), Image.NEAREST).save(os.path.join(out, f'cat_frames_{fur}_{weapon}.png'))


def rocks_sheet(E, out):
    bases = sorted({n[:-3] for n in E if n.startswith('rock_') and n[-3:] in ('_d1', '_d2', '_d3')})
    sh = Image.new('RGBA', (4 * 40 + 10, len(bases) * 46), BG)
    for i, b in enumerate(bases):
        for k, n in enumerate([b, b + '_d1', b + '_d2', b + '_d3']):
            im = E[n].canvas.img
            sh.alpha_composite(im, (5 + k * 40 + (36 - im.width) // 2, i * 46 + 44 - im.height))
    sh.resize((sh.width * 4, sh.height * 4), Image.NEAREST).save(os.path.join(out, 'rocks_damage_stages.png'))


def themes_sheet(E, out):
    """Макет поля 6×9 со стенами, препятствиями, воротами, декором, камнями и котиками в трёх оформлениях."""
    W, D, SIDE, TOP, BOT, gate_row = 6, 9, 2, 2, 1, 5

    def tile(c, r, sfx):
        h = (c * 7349 + r * 1301) & 0xffff

        def t(n):
            return n + sfx if n + sfx in E else n
        inc = 0 <= c < W
        if r < 0:
            if inc:
                return t('wall_top_edge') if r == -1 else t(['wall_top', 'wall_top_1', 'wall_top_2', 'wall_top_3'][h % 4])
            if r == -1 and c == -1:
                return t('wall_corner_tl')
            if r == -1 and c == W:
                return t('wall_corner_tr')
            return t(f'wall_side_{h % 4}')
        if r >= D:
            if inc:
                return t('wall_bottom')
            if r == D and c == -1:
                return t('wall_corner_bl')
            if r == D and c == W:
                return t('wall_corner_br')
            return t(f'wall_side_{h % 4}')
        if c == -1:
            return t(f'wall_edge_l_{h % 2}')
        if c == W:
            return t(f'wall_edge_r_{h % 2}')
        if not inc:
            return t(f'wall_side_{h % 4}')
        if r == gate_row:
            return t('floor_gate')
        k = h % 10
        return t(f'floor_{0 if k < 4 else (1 if k < 7 else (2 if k < 9 else 3))}')

    def scene(theme):
        sfx = '' if theme == 'warm' else '_' + theme
        img = Image.new('RGBA', ((W + 2 + 2 * SIDE) * T, (D + TOP + BOT) * T), (0, 0, 0, 255))
        ox, oy = (SIDE + 1) * T, TOP * T
        for r in range(-TOP, D + BOT):
            for c in range(-1 - SIDE, W + 1 + SIDE):
                img.alpha_composite(E[tile(c, r, sfx)].canvas.img, (ox + c * T, oy + r * T))

        def put(name, x, y_feet, themed=True):
            n = name + sfx if themed and (name + sfx) in E else name
            e = E[n]
            px, py = e.pivot
            img.alpha_composite(e.canvas.img, (int(ox + x - px), int(oy + y_feet - (e.canvas.h - py))))
        for dc, dr, n in ((0, 0, 'block_tl'), (1, 0, 'block_tr'), (0, 1, 'block_bl'), (1, 1, 'block_br')):
            img.alpha_composite(E[n + sfx if n + sfx in E else n].canvas.img, (ox + (4 + dc) * T, oy + (1 + dr) * T))
        put('block_single_3', 0.5 * T, 4 * T)
        put('wall_gate_l', -0.5 * T + 16, (gate_row + 1) * T)
        put('wall_gate_r', (W + 0.5) * T - 16, (gate_row + 1) * T)
        for c, r, n in ((1, 1, 'rock_plain_0'), (2, 2, 'rock_gems_big'), (3, 1, 'rock_elixir_small'), (1, 7, 'rock_plain_1'),
                        (4, 7, 'rock_chest_iron'), (3, 6, 'rock_plain_2_d2')):
            put(n, c * T + 16, (r + 1) * T, False)
        for c, r, fur, weapon, colour in ((2, 3, 'ginger', 'pick', 'yellow'), (3, 3, 'gray', 'saw', 'yellow'),
                                          (0, 6, 'black', 'laser', 'blue'), (2, 7, 'brown', 'hammer', 'yellow')):
            frame = 'tool_ready' if weapon in manifest.TOOLS else 'mine_ready'
            for n in (f'cat_body_{fur}_{frame}', f'cat_helmet_{colour}_{frame}', f'cat_weapon_{weapon}_{frame}'):
                put(n, c * T + 16, (r + 1) * T - 4, False)
        put('beam_post', -20, 3 * T)
        put('lantern_0', -8, 2 * T + 18, False)
        put(themes.GLOW[theme], W * T + 16, 4 * T + 26, False)
        put('crystal_cluster_a', W * T + 12, 4 * T + 28)
        put('mushrooms', -9, 7 * T + 26)
        put('sign_question', W * T + 11, T - 2)
        if theme == 'copper':
            put('pipe_copper', -14, 5 * T + 30, False)
            put('gear_copper_0', W * T + 16, 8 * T + 20, False)
            put('gear_copper_2', W * T + 16, 8 * T + 1, False)
        if theme == 'crystal':
            put('fx_glow_teal', -14, 8 * T + 14, False)
            put('crystal_spire', -14, 8 * T + 30, False)
        return img

    imgs = [scene(t) for t in ('warm', 'crystal', 'copper')]
    w, h = imgs[0].size
    sheet = Image.new('RGBA', (w * 3 + 16, h), (0, 0, 0, 255))
    for i, im in enumerate(imgs):
        sheet.alpha_composite(im, (i * (w + 8), 0))
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(out, 'environment_themes.png'))


def shaft_sheet(E, out):
    gem = (120, 220, 160)

    def comp(names, theme=''):
        im = Image.new('RGBA', (64, 112), pxl.PALETTE['slate3'] + (255,))
        for n in names:
            layer = E[n + theme if (n + theme) in E else n].canvas.img
            if n in ('shaft_gems', 'shaft_heap'):
                layer = ImageChops.multiply(layer, Image.new('RGBA', layer.size, gem + (255,)))
            im.alpha_composite(layer, (0, 0))
        return im
    states = [
        ['shaft_base', 'shaft_gems', 'shaft_rubble', 'shaft_wheel_0'],
        ['shaft_base', 'shaft_gems', 'shaft_ribbon', 'shaft_wheel_0'],
        ['shaft_base', 'shaft_light', 'shaft_gems', 'shaft_wheel_1'],
        ['shaft_base', 'shaft_light', 'shaft_gems', 'shaft_heap', 'shaft_glint_2', 'shaft_wheel_0'],
        ['shaft_base', 'shaft_light', 'shaft_gems', 'shaft_plaque', 'shaft_wheel_2'],
    ]
    ims = [comp(n) for n in states] + [comp(states[2], '_crystal'), comp(states[4], '_copper')]
    sheet = Image.new('RGBA', (len(ims) * 70, 112), (0, 0, 0, 255))
    for i, im in enumerate(ims):
        sheet.alpha_composite(im, (i * 70, 0))
    sheet.resize((sheet.width * 4, sheet.height * 4), Image.NEAREST).save(os.path.join(out, 'shaft_states.png'))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(HERE, 'out', 'overview'))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    E = build.load_modules(build.MODULES).entries
    cats_sheets(E, a.out)
    rocks_sheet(E, a.out)
    themes_sheet(E, a.out)
    shaft_sheet(E, a.out)
    print('[overview] →', a.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
