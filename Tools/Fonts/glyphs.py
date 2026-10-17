#!/usr/bin/env python3
"""Крупно: похожие знаки кандидатов шрифта (1 I l |, 0 O, 5 S, 8 B)."""
import sys
from PIL import Image, ImageDraw, ImageFont
out = sys.argv[1]
specs = sys.argv[2:]
img = Image.new('RGB', (1400, 140 * len(specs) + 20), (20, 26, 54))
d = ImageDraw.Draw(img)
y = 10
for spec in specs:
    label, path, w = spec.split('|')
    f = ImageFont.truetype(path, 96)
    if w:
        try: f.set_variation_by_axes([int(w)])
        except Exception: pass
    lf = ImageFont.truetype(path, 24)
    d.text((10, y), label, fill=(255, 200, 100), font=lf)
    d.text((260, y), "1 I l |  0 O  5 S  8 B", fill=(253, 246, 227), font=f)
    y += 140
img.save(out)
