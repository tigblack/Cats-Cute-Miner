#!/usr/bin/env python3
"""Рекламные playable Cat Miners: short, medium, long — каждый одним HTML без внешних запросов (Docs/Playable/PLAYABLE_ADS.md).

Спрайты — из генератора пиксель-арта проекта (Tools/PixelArt, тот же реестр, что атлас Unity): в компактный атлас попадает только
то, что нужно playable (котики — слоями, как в CatView: тело, ботинки, рюкзак, груз, шляпа, кирка, мешочек). Шрифт — Nunito
ExtraBold (подмножество, woff2). Код и стили — Tools/Playable/src. Лимит рекламных сетей — 2–5 МБ на файл; сборка печатает размеры.

  Tools/.venv/bin/python Tools/Playable/build_playable.py [--android URL] [--ios URL] [--only short]

Выход: release/playables/cat-miners-{short,medium,long}.html и index.html (страница просмотра трёх версий).
"""
from __future__ import annotations

import argparse
import base64
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
PIXEL = os.path.join(ROOT, 'Tools', 'PixelArt')
sys.path.insert(0, PIXEL)

from PIL import Image  # noqa: E402

import build as pixel_build  # noqa: E402
import pxl  # noqa: E402

OUT = os.path.join(ROOT, 'release', 'playables')
SRC = os.path.join(HERE, 'src')
FONT = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Fonts', 'Nunito-ExtraBold.ttf')
ANDROID_URL = 'https://play.google.com/store/apps/details?id=com.multicastgames.catscuteminer'
VARIANTS = ['short', 'medium', 'long']

# ───────── что берём из реестра ─────────
FURS = ['ginger', 'gray', 'white', 'black', 'cream']
KINDS = ['common', 'rare', 'epic', 'legendary']
CAT_FRAMES = ([f'idle{i}' for i in range(6)] + ['blink'] + [f'walk{i}' for i in range(8)] +
              ['mine_ready', 'mine_ready2', 'mine_antic', 'mine_raise', 'mine_top', 'mine_strike', 'mine_impact', 'mine_recoil',
               'mine_return'] + [f'carry{i}' for i in range(8)] + [f'unload{i}' for i in range(4)] + [f'joy{i}' for i in range(6)] +
              [f'spawn{i}' for i in range(8)])
PICK_FRAMES = [f for f in CAT_FRAMES if not f.startswith(('carry', 'unload'))]

WORLD = (
    ['floor_0', 'floor_1', 'floor_2', 'floor_3', 'floor_gate', 'wall_top', 'wall_top_1', 'wall_top_2', 'wall_top_3', 'wall_top_edge',
     'wall_bottom', 'wall_edge_l_0', 'wall_edge_r_0', 'wall_side_0', 'wall_side_1', 'wall_side_2', 'wall_side_3', 'beam_post',
     'lantern_0', 'lantern_1', 'wall_brace_0', 'wall_sign_0', 'wall_rack_0', 'exit_base', 'exit_boards', 'exit_glow',
     'camp_edge_t', 'camp_edge_b', 'camp_floor_0', 'camp_floor_1', 'camp_floor_2', 'camp_floor_3', 'camp_ground_0', 'camp_ground_1',
     'camp_lantern_0', 'camp_lantern_1', 'camp_tent', 'camp_sign', 'depot_base', 'depot_chute', 'depot_chute_active', 'depot_glow',
     'depot_crate_0', 'depot_crate_1', 'depot_crate_2', 'depot_crate_3', 'warehouse_full', 'hire_board_0', 'bench_0', 'rack_0',
     'wardrobe_closed', 'mortar_base', 'mortar_tube', 'barrel_tnt_0', 'barrel_tnt_1', 'barrel_fly_0', 'barrel_fly_1',
     'gate_frame', 'gate_door_closed', 'gate_door_cracked', 'gate_door_broken', 'gate_door_open',
     'shadow_xs', 'shadow_s', 'shadow_m', 'shadow_l', 'shadow_xl', 'hp_frame', 'hp_fill', 'select_ring', 'loot_gold', 'loot_glow',
     'cargo_sack_1', 'cargo_sack_2', 'rock_crack_1', 'rock_crack_2', 'rubble_debris_0', 'rubble_debris_1', 'rubble_debris_2']
    + [f'rock_plain_{i}{s}' for i in range(3) for s in ('', '_d1', '_d2', '_d3')]
    + [f'rock_{k}{s}' for k in ('gems_small', 'gems_big', 'chest_wooden', 'chest_iron', 'chest_golden', 'chest_legendary')
       for s in ('', '_d1', '_d2', '_d3')]
    + [f'fx_{n}_{i}' for n, k in (('boom', 5), ('coin', 4), ('hit', 3), ('impact', 3), ('puff', 4), ('ring', 4), ('smoke', 4),
                                    ('spark', 3), ('strike_pick', 4), ('sack_pop', 2), ('chain_break', 4)) for i in range(k)]
    + ['fx_glow_lantern', 'fx_glow_warm', 'fx_glow_white', 'fx_gem']
    + [f'prop_{n}' for n in ('minecart_0', 'minecart_1', 'support_0', 'support_1', 'reel_0', 'tower_0', 'statue_0', 'winch_0')]
    + [f'decal_{n}' for n in ('pebbles_0', 'pebbles_1', 'pebbles_2', 'paint_0', 'paws_0', 'stain_0', 'board_0')]
)
DEBRIS = {'planks': 4, 'crates': 3, 'rubble': 3, 'scrap': 3, 'cardboard': 4, 'yarn': 3, 'catfurn': 3, 'sacks': 4}
for _fam, _n in DEBRIS.items():
    for _i in range(_n):
        WORLD += [f'debris_{_fam}_{_i}', f'debris_{_fam}_{_i}_d1', f'debris_{_fam}_{_i}_d2']
    WORLD.append(f'debris_{_fam}_rest')

CARDS = {'ca035': 'Sledgehammer', 'ca036': 'Hiring Poster', 'ca037': 'Gold Vein', 'ca038': 'Golden Helmet', 'ca034': 'Golden Cart',
         'ca053': "Miner's Lantern", 'ca054': 'Rubble Blaster', 'ca031': 'Lucky Paw', 'ca027': 'Courier Cat', 'ca062': 'Treasure Map'}
UI_SPRITES = (['icon_coin', 'icon_cat_big', 'icon_cat', 'icon_branch_team', 'icon_branch_pickaxes', 'icon_branch_boots',
               'icon_branch_bags', 'icon_chest_wooden', 'icon_chest_golden', 'icon_chest_legendary', 'icon_cards', 'icon_check',
               'tutor_cat_0', 'tutor_cat_1', 'ui_card_frame_rare', 'ui_card_frame_epic', 'ui_card_frame_legendary',
               'ui_card_frame_majestic', 'fx_gem', 'barrel_tnt_0'] + [f'card_art_{c}' for c in CARDS])


def cat_sprites():
    names = []
    for f in CAT_FRAMES:
        names += [f'cat_body_{fur}_{f}' for fur in FURS]
        names += [f'cat_{layer}_{kind}_{f}' for layer in ('boots', 'pack', 'hat') for kind in KINDS]
        names += [f'cat_load_{s}_{f}' for s in (1, 2, 3)]
    for f in PICK_FRAMES:
        names += [f'cat_pick_{kind}_{f}' for kind in KINDS]
    names += [f'cat_sack_{f}' for f in ('unload0', 'unload1', 'unload2')]
    return names


# ───────── рука-указатель подсказок (в стиле игры: контур ink, перчатка cream) ─────────
HAND = [
    '....oo..........',
    '...owwo.........',
    '...ow1o.........',
    '...ow1o.........',
    '...ow1ooo.......',
    '...ow1ow1oo.....',
    '.oo.ow1ow1ooo...',
    'ow1oow1ow1ow1o..',
    'ow11ow11w11w1so.',
    '.ow1w111111111so',
    '.ow11111111111so',
    '..ow1111111111so',
    '...ow111111111o.',
    '....ow1111111so.',
    '.....ow111111o..',
    '.....owwwwwwso..',
    '......oooooo....',
]


def hand_canvas() -> pxl.Canvas:
    return pxl.Canvas.from_ascii(HAND, {'o': 'ink1', 'w': 'cream4', '1': 'cream3', 's': 'cream1'})


def png_bytes(img: Image.Image) -> bytes:
    """PNG с точной палитрой, если цветов (с альфой) не больше 256 — пиксель-арт игры почти всегда; иначе RGBA."""
    img = img.convert('RGBA')
    colors = img.getcolors(maxcolors=256)
    buf = io.BytesIO()
    if colors is not None:
        palette = [c for _, c in colors]
        index = {c: i for i, c in enumerate(palette)}
        p = Image.new('P', img.size)
        p.putdata([index[px] for px in img.getdata()])
        flat = []
        for r, g, b, a in palette:
            flat += [r, g, b]
        p.putpalette(flat + [0] * (768 - len(flat)))
        p.save(buf, 'PNG', optimize=True, transparency=bytes(a for _, _, _, a in palette))
    else:
        img.save(buf, 'PNG', optimize=True)
    return buf.getvalue()


def data_url(img: Image.Image) -> str:
    return 'data:image/png;base64,' + base64.b64encode(png_bytes(img)).decode('ascii')


def trim(entry):
    """Обрезка прозрачных полей; anchor (ax, ay) — точка pivot от левого ВЕРХНЕГО угла (pivot у реестра — от нижнего левого)."""
    img = entry.canvas.img
    bbox = img.getbbox()
    w, h = img.size
    px, py = entry.pivot
    if not bbox:
        return img, px, h - py
    l, t, r, b = bbox
    return img.crop(bbox), px - l, (h - py) - t


def pack(items, width=1024, pad=1):
    """Полочная упаковка по убыванию высоты; одинаковые картинки — одно место атласа."""
    uniq, alias = {}, {}
    for name, img, ax, ay in items:
        key = (img.size, img.tobytes())
        if key not in uniq:
            uniq[key] = (name, img)
        alias[name] = (key, ax, ay)
    order = sorted(uniq.items(), key=lambda kv: (-kv[1][1].size[1], -kv[1][1].size[0], kv[1][0]))
    x = y = shelf = 0
    pos = {}
    for key, (name, img) in order:
        w, h = img.size
        if x + w + pad > width:
            x, y, shelf = 0, y + shelf, 0
        pos[key] = (x, y)
        x += w + pad
        shelf = max(shelf, h + pad)
    height = y + shelf
    atlas = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    for key, (name, img) in uniq.items():
        atlas.paste(img, pos[key])
    table = {}
    for name, (key, ax, ay) in alias.items():
        (x, y), (w, h) = pos[key], uniq[key][1].size
        table[name] = [x, y, w, h, round(ax, 1), round(ay, 1)]
    return atlas, table


def font_data_url() -> str:
    from fontTools import subset
    from fontTools.ttLib import TTFont
    text = ''.join(chr(c) for c in range(32, 127)) + '★•×→←↑↓…’—–·%'
    opts = subset.Options()
    opts.flavor = 'woff2'
    opts.layout_features = ['kern', 'liga']
    font = TTFont(FONT)
    sub = subset.Subsetter(opts)
    sub.populate(text=text)
    sub.subset(font)
    buf = io.BytesIO()
    font.flavor = 'woff2'
    font.save(buf)
    return 'data:font/woff2;base64,' + base64.b64encode(buf.getvalue()).decode('ascii')


def app_icon(reg) -> Image.Image:
    """Иконка для баннера и концовки: тёплая плашка, котик-наставник в каске."""
    cv = pxl.Canvas(48, 48)
    cv.rect(0, 0, 48, 48, 'amber3')
    for y in range(48):
        cv.hline(0, 47, y, 'amber4' if y < 18 else ('amber3' if y < 34 else 'amber2'))
    cv.frame(0, 0, 48, 48, 'wood1')
    cat = reg.entries['tutor_cat_0'].canvas
    cv.blit(cat, (48 - cat.w) // 2, 48 - cat.h - 2)
    return cv.img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--android', default=ANDROID_URL)
    ap.add_argument('--ios', default='', help='ссылка App Store; пусто — та же, что Android')
    ap.add_argument('--only', choices=VARIANTS)
    args = ap.parse_args()

    reg = pixel_build.load_modules(pixel_build.MODULES)
    world = WORLD + cat_sprites()
    missing = [n for n in world + UI_SPRITES if n not in reg.entries]
    if missing:
        print('[playable] нет спрайтов:', ', '.join(missing[:40]), '…' if len(missing) > 40 else '')
        world = [n for n in world if n in reg.entries]
    items = []
    for n in world:
        img, ax, ay = trim(reg.entries[n])
        items.append((n, img, ax, ay))
    hand = hand_canvas()
    items.append(('ui_hand', hand.img, 3, 0))
    atlas, table = pack(items)
    atlas_url = data_url(atlas)
    ui = {n: data_url(trim(reg.entries[n])[0]) for n in UI_SPRITES if n in reg.entries}
    ui['hand'] = data_url(hand.img)
    ui['app_icon'] = data_url(app_icon(reg))
    font = font_data_url()
    with open(os.path.join(SRC, 'style.css'), encoding='utf-8') as f:
        css = f.read().replace('__FONT__', font)
    with open(os.path.join(SRC, 'game.js'), encoding='utf-8') as f:
        js = f.read()
    with open(os.path.join(SRC, 'shell.html'), encoding='utf-8') as f:
        shell = f.read()
    os.makedirs(OUT, exist_ok=True)
    print(f'[playable] атлас {atlas.size[0]}×{atlas.size[1]}, {len(table)} спрайтов, PNG {len(atlas_url) * 3 // 4 // 1024} КБ')
    sizes = {}
    for v in VARIANTS:
        if args.only and v != args.only:
            continue
        cfg = {'variant': v, 'android': args.android, 'ios': args.ios or args.android, 'cards': CARDS,
               'spr': table, 'ui': ui, 'atlas': atlas_url}
        html = (shell.replace('__TITLE__', 'Cat Miners')
                .replace('__CSS__', css)
                .replace('__CONFIG__', json.dumps(cfg, separators=(',', ':')))
                .replace('__GAME__', js))
        path = os.path.join(OUT, f'cat-miners-{v}.html')
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(html)
        sizes[v] = len(html.encode('utf-8'))
        print(f'[playable] {v}: {os.path.relpath(path, ROOT)} — {sizes[v] / 1024 / 1024:.2f} МБ')
    with open(os.path.join(SRC, 'index.html'), encoding='utf-8') as f:
        index = f.read()
    with open(os.path.join(OUT, 'index.html'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(index.replace('__ICON__', ui['app_icon']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
