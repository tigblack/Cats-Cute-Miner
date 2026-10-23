#!/usr/bin/env python3
"""Сравнительный лист кандидатов шрифта UI: латиница, кириллица, цифры и похожие знаки (0/O, 1/I/l, 5/S)."""
import sys
from PIL import Image, ImageDraw, ImageFont

LINES = [
    ("0O 1Il| 5S 2Z 8B 6G  1.25K  -12%  +3.4M  x2  12/40", 30),
    ("Mine 12 · Pickaxes Lv 7 · Upgrade 1.20K gold", 30),
    ("Шахта 12 · Кирки ур. 7 · Улучшить за 1,20 тыс.", 30),
    ("Cards  Equipment  Market  Store  Base", 44),
    ("Unlocks at mine 15 · Damage +8.4% · Elixir 350", 22),
]

def render(cands, out):
    W, rowh = 1400, 0
    blocks = []
    for label, path, axes in cands:
        h = 40 + sum(int(s * 1.45) for _, s in LINES) * 2
        blocks.append((label, path, axes, h))
    H = sum(b[3] for b in blocks) + 20
    img = Image.new('RGB', (W, H), (20, 26, 54))
    d = ImageDraw.Draw(img)
    y = 10
    label_font = ImageFont.truetype(cands[0][1], 22)
    for label, path, axes, h in blocks:
        d.text((10, y), label, fill=(255, 200, 100), font=label_font)
        y += 34
        for weight in (axes or [None]):
            for text, size in LINES:
                f = ImageFont.truetype(path, size)
                if weight is not None:
                    try:
                        f.set_variation_by_axes([weight] if not isinstance(weight, list) else weight)
                    except Exception as e:
                        pass
                d.text((20, y), text, fill=(253, 246, 227), font=f)
                y += int(size * 1.45)
        y += 6
    img.save(out)
    print(out, img.size)

if __name__ == '__main__':
    out = sys.argv[1]
    cands = []
    for spec in sys.argv[2:]:
        label, path, axes = spec.split('|')
        ax = [int(a) if ',' not in a else [int(x) for x in a.split(',')] for a in axes.split(';')] if axes else None
        cands.append((label, path, ax))
    render(cands, out)
