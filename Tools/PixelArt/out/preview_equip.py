#!/usr/bin/env python3
"""Превью экипировки котика нового режима (только хаб, PNG не синхронизируются).

Собирает слои из реестра sprites/cats.py в порядке CatView: тело → обувь → рюкзак → груз → убор → кирка → мешочек.
  python3 Tools/PixelArt/out/preview_equip.py
Пишет Tools/PixelArt/out/preview_equip_*.png.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PIX = os.path.dirname(HERE)
sys.path.insert(0, PIX)
from PIL import Image, ImageDraw  # noqa: E402

import build  # noqa: E402
import manifest  # noqa: E402
import pxl  # noqa: E402

BG = pxl.PALETTE['slate3'] + (255,)
TXT = (240, 230, 210, 255)
R = manifest.EQUIP_RARITIES


def cat(E, fur, fr, hat='base', pack='base', boots='base', pick='base', load=0):
    im = Image.new('RGBA', (40, 40), (0, 0, 0, 0))
    names = [f'cat_body_{fur}_{fr}', f'cat_boots_{boots}_{fr}', f'cat_pack_{pack}_{fr}']
    if load:
        names.append(f'cat_load_{load}_{fr}')
    names += [f'cat_hat_{hat}_{fr}', f'cat_pick_{pick}_{fr}', f'cat_sack_{fr}']
    for n in names:
        if n in E:
            im.alpha_composite(E[n].canvas.img, (0, 0))
    return im


def save(sh, name, k):
    sh.resize((sh.width * k, sh.height * k), Image.NEAREST).save(os.path.join(HERE, name))


def main():
    E = build.load_modules(['cats']).entries
    cell = 44
    # 1. Редкости (весь комплект одной редкости) × окрасы, idle0, груз 2.
    furs = manifest.FURS
    sh = Image.new('RGBA', (60 + cell * len(R), 14 + cell * len(furs)), BG)
    d = ImageDraw.Draw(sh)
    for j, r in enumerate(R):
        d.text((60 + j * cell, 2), r[:9], fill=TXT)
    for i, f in enumerate(furs):
        d.text((2, 14 + i * cell + 16), f, fill=TXT)
        for j, r in enumerate(R):
            sh.alpha_composite(cat(E, f, 'idle0', r, r, r, r, 2 if i % 2 else 0), (60 + j * cell + 2, 14 + i * cell + 2))
    save(sh, 'preview_equip_rarities.png', 4)
    # 2. Слоты по отдельности: строка на слот, столбец на редкость (рыжий, idle0).
    slots = ['hat', 'pack', 'boots', 'pick']
    sh = Image.new('RGBA', (60 + cell * len(R), 14 + cell * (len(slots) + 1)), BG)
    d = ImageDraw.Draw(sh)
    for j, r in enumerate(R):
        d.text((60 + j * cell, 2), r[:9], fill=TXT)
    for i, s in enumerate(slots):
        d.text((2, 14 + i * cell + 16), s, fill=TXT)
        for j, r in enumerate(R):
            sh.alpha_composite(cat(E, 'ginger', 'idle0', **{s: r}), (60 + j * cell + 2, 14 + i * cell + 2))
    i = len(slots)
    d.text((2, 14 + i * cell + 16), 'load', fill=TXT)
    for j, n in enumerate([0, 1, 2, 3]):
        sh.alpha_composite(cat(E, 'gray', 'idle0', load=n), (60 + j * cell + 2, 14 + i * cell + 2))
    save(sh, 'preview_equip_slots.png', 4)
    # 3. Все кадры нового режима: два комплекта.
    frames = manifest.EQUIP_FRAMES
    cols, cw, ch = 11, 46, 52
    nrows = (len(frames) + cols - 1) // cols
    for tag, fur, r, load in (('common', 'ginger', 'common', 2), ('legendary', 'black', 'legendary', 3),
                              ('base', 'white', 'base', 1)):
        sh = Image.new('RGBA', (cols * cw, nrows * ch), BG)
        d = ImageDraw.Draw(sh)
        for k, fr in enumerate(frames):
            x, y = (k % cols) * cw, (k // cols) * ch
            sh.alpha_composite(cat(E, fur, fr, r, r, r, r, load), (x + 3, y + 1))
            d.text((x + 2, y + 41), fr.replace('mine_', 'm_')[:9], fill=TXT)
        save(sh, f'preview_equip_frames_{tag}.png', 3)
    # 4. Кадры с грузом крупно: carry/pickup/unload, груз 0..3.
    frames = manifest.NEW_FRAMES
    sh = Image.new('RGBA', (len(frames) * 42, 4 * 42), BG)
    for k, fr in enumerate(frames):
        for n in range(4):
            sh.alpha_composite(cat(E, 'ginger', fr, 'rare', ['base', 'common', 'rare', 'epic'][n], 'rare', load=n),
                               (k * 42 + 1, n * 42 + 1))
    save(sh, 'preview_equip_carry.png', 4)


def zoom(argv):
    """zoom <fur> <rarity> <load> <frame...>: крупно (×8) выбранные кадры → preview_equip_zoom.png."""
    E = build.load_modules(['cats']).entries
    fur, r, load, frames = argv[0], argv[1], int(argv[2]), argv[3:]
    sh = Image.new('RGBA', (len(frames) * 42, 42), BG)
    for k, fr in enumerate(frames):
        sh.alpha_composite(cat(E, fur, fr, r, r, r, r, load), (k * 42 + 1, 1))
    save(sh, 'preview_equip_zoom.png', 8)


def grid(argv):
    """grid <fur> <rarity> <load> <frame>: один кадр ×16 с сеткой и номерами столбцов/строк → preview_equip_grid.png."""
    E = build.load_modules(['cats']).entries
    fur, r, load, fr = argv[0], argv[1], int(argv[2]), argv[3]
    k = 16
    im = Image.new('RGBA', (40, 40), BG)
    im.alpha_composite(cat(E, fur, fr, r, r, r, r, load))
    big = Image.new('RGBA', (40 * k + 24, 40 * k + 24), BG)
    big.paste(im.resize((40 * k, 40 * k), Image.NEAREST), (24, 24))
    d = ImageDraw.Draw(big)
    for i in range(41):
        c = (90, 100, 130, 255) if i % 5 else (200, 120, 120, 255)
        d.line([(24 + i * k, 24), (24 + i * k, 24 + 40 * k)], fill=c)
        d.line([(24, 24 + i * k), (24 + 40 * k, 24 + i * k)], fill=c)
        if i % 5 == 0 and i < 40:
            d.text((24 + i * k + 2, 4), str(i), fill=TXT)
            d.text((2, 24 + i * k + 2), str(i), fill=TXT)
    big.save(os.path.join(HERE, 'preview_equip_grid.png'))


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'grid':
        grid(sys.argv[2:])
        sys.exit(0)
    if len(sys.argv) > 1 and sys.argv[1] == 'zoom':
        zoom(sys.argv[2:])
    else:
        main()
