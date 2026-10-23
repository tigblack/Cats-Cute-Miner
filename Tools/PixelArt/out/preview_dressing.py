"""Превью группы dressing: python3 Tools/PixelArt/out/preview_dressing.py [префикс ...] [--k N] [--out файл]
Спрайты на плитах пола (prop_sealedshaft_*, wall_* — на стене) в масштабе k; по умолчанию out/_tmp/dz.png.
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)

from PIL import Image  # noqa: E402

import pxl  # noqa: E402
from sprites import dressing, env  # noqa: E402


def main():
    args = sys.argv[1:]
    k, out = 5, os.path.join(HERE, 'out', '_tmp', 'dz.png')
    if '--k' in args:
        i = args.index('--k')
        k = int(args[i + 1])
        del args[i:i + 2]
    if '--out' in args:
        i = args.index('--out')
        out = args[i + 1]
        del args[i:i + 2]
    reg = pxl.Registry()
    dressing.build(reg)
    names = [n for n in reg.entries if not args or any(n.startswith(a) for a in args)]
    floor, wall = env.floor_tile(0), env.wall_side(0)
    cols = 8
    cell_w, cell_h = 72, 72
    rows = (len(names) + cols - 1) // cols
    img = Image.new('RGBA', (cols * cell_w * k, rows * cell_h * k), (30, 30, 40, 255))
    for idx, n in enumerate(names):
        cv = reg.entries[n].canvas
        bx, by = (idx % cols) * cell_w, (idx // cols) * cell_h
        bg = pxl.Canvas(cell_w - 4, cell_h - 4)
        on_wall = n.startswith(('prop_sealedshaft', 'wall_'))
        for ty in range(0, bg.h, 32):
            for tx in range(0, bg.w, 32):
                bg.blit(wall if on_wall else floor, tx, ty)
        if on_wall:
            for tx in range(0, bg.w, 32):
                bg.blit(floor, tx, bg.h - 8)
        x0 = (bg.w - cv.w) // 2
        y0 = bg.h - 8 - cv.h
        bg.blit(cv, x0, y0)
        img.alpha_composite(bg.scaled(k), (bx * k, by * k))
    img.save(out)
    print(out, len(names))


if __name__ == '__main__':
    main()
