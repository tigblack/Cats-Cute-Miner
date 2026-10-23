"""Мусор dressing в масштабе поля: python3 Tools/PixelArt/out/preview_debris.py [--k N] [--out файл] [--stages]
Каждая база debris_<семейство>_<n> на плитах пола с рамкой клетки 32×32 (footprint) и камнем rock_plain_0 для сравнения высоты;
--stages добавляет стадии _d1/_d2. По умолчанию out/_tmp/debris.png.
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)

from PIL import Image, ImageDraw  # noqa: E402

import pxl  # noqa: E402
from sprites import dressing, env, rocks  # noqa: E402


def main():
    args = sys.argv[1:]
    k, out, stages = 4, os.path.join(HERE, 'out', '_tmp', 'debris.png'), '--stages' in args
    if '--k' in args:
        k = int(args[args.index('--k') + 1])
    if '--out' in args:
        out = args[args.index('--out') + 1]
    reg = pxl.Registry()
    for m in (env, rocks, dressing):
        m.build(reg)
    names = ['rock_plain_0']
    for fam in dressing.FAMILIES:
        for i in range(getattr(dressing, 'VARIANTS', {}).get(fam, 3)):
            names.append(f'debris_{fam}_{i}')
            if stages:
                names += [f'debris_{fam}_{i}_d1', f'debris_{fam}_{i}_d2']
    cols = 8
    cell_w, cell_h = 72, 64
    rows = (len(names) + cols - 1) // cols
    img = Image.new('RGBA', (cols * cell_w * k, rows * cell_h * k), (30, 30, 40, 255))
    floor = env.floor_tile(0)
    for idx, n in enumerate(names):
        if n not in reg.entries:
            continue
        cv = reg.entries[n].canvas
        bx, by = (idx % cols) * cell_w, (idx // cols) * cell_h
        bg = pxl.Canvas(cell_w - 4, cell_h - 4)
        for ty in range(0, bg.h, 32):
            for tx in range(0, bg.w, 32):
                bg.blit(floor, tx, ty)
        x0 = (bg.w - cv.w) // 2
        bg.blit(cv, x0, bg.h - 2 - cv.h)
        tile = bg.scaled(k)
        d = ImageDraw.Draw(tile)
        fw = 64 if cv.w > 40 else 32
        fx = (bg.w - fw) // 2
        d.rectangle([fx * k, (bg.h - 2 - 32) * k, (fx + fw) * k - 1, (bg.h - 2) * k - 1], outline=(255, 210, 90, 255))
        d.text((4, 4), n.replace('debris_', ''), fill=(240, 230, 210, 255))
        img.alpha_composite(tile, (bx * k, by * k))
    img.save(out)
    print(out, len(names))


if __name__ == '__main__':
    main()
