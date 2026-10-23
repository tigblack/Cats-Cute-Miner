"""Лист снимков playable: Tools/.venv/bin/python Tools/Playable/sheet.py out.png a.png b.png … (каждый кадр — в половину, подпись — имя файла)."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

out, *frames = sys.argv[1:]
ims = [Image.open(f).convert("RGB") for f in frames]
ims = [im.resize((im.width // 2, im.height // 2), Image.LANCZOS) for im in ims]
gap, label = 10, 26
sheet = Image.new("RGB", (sum(im.width for im in ims) + gap * (len(ims) - 1), max(im.height for im in ims) + label), (20, 20, 30))
d = ImageDraw.Draw(sheet)
x = 0
for f, im in zip(frames, ims):
    sheet.paste(im, (x, label))
    d.text((x + 6, 6), Path(f).stem, fill=(255, 220, 120))
    x += im.width + gap
sheet.save(out)
print(out, sheet.size)
