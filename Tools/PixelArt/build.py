#!/usr/bin/env python3
"""Сборка пиксельного атласа Cat Miners.

  python3 Tools/PixelArt/build.py            — все модули: проверка, упаковка, превью, C#-файл для Unity
  python3 Tools/PixelArt/build.py --only cats — только модуль sprites/cats.py: проверка и превью (атлас и C# не пишутся)
  python3 Tools/PixelArt/build.py --check    — только проверка (палитра, прозрачность, обязательные имена)
  python3 Tools/PixelArt/build.py --allow-missing — записать атлас из того, что есть (недостающее в Unity — заглушки)

Выход:
  Tools/PixelArt/out/atlas.png             — атлас (на хабе; PNG не синхронизируется)
  Tools/PixelArt/out/sheet_<группа>.png    — превью группы ×4 с подписями
  Tools/PixelArt/out/manifest.json         — имена, размеры, pivot, border
  Assets/Game/Scripts/Runtime/Pixel/PixelAtlasData.g.cs — PNG атласа в base64 и таблица спрайтов

Модуль спрайтов — файл sprites/<имя>.py с функцией build(reg: pxl.Registry).
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import importlib
import io
import json
import os
import sys
from typing import Dict, List, Tuple

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import pxl  # noqa: E402
from manifest import REQUIRED  # noqa: E402

OUT = os.path.join(HERE, 'out')
CS_PATH = os.path.join(ROOT, 'Assets', 'Game', 'Scripts', 'Runtime', 'Pixel', 'PixelAtlasData.g.cs')
MODULES = ['env', 'rocks', 'structures', 'base', 'themes', 'cats', 'ui', 'icons', 'fx', 'tutorial', 'loot', 'cards', 'card_art',
           'goods', 'factories']
PAD = 2
ATLAS_W = 1024


def load_modules(only: List[str]) -> pxl.Registry:
    reg = pxl.Registry()
    for m in only:
        path = os.path.join(HERE, 'sprites', m + '.py')
        if not os.path.exists(path):
            print(f'[pixel] модуль sprites/{m}.py ещё не создан — пропуск')
            continue
        mod = importlib.import_module('sprites.' + m)
        reg.group = m
        mod.build(reg)
    return reg


def check(reg: pxl.Registry, require_all: bool) -> List[str]:
    errors: List[str] = []
    for e in reg.entries.values():
        cv = e.canvas
        bad = set()
        for y in range(cv.h):
            for x in range(cv.w):
                r, g, b, a = cv.px[x, y]
                if a == 0:
                    continue
                if a != 255 and not e.alpha:
                    errors.append(f'{e.name}: полупрозрачный пиксель ({x},{y}) a={a}, а alpha=False')
                    break
                if (r, g, b) not in pxl.PALETTE_SET:
                    bad.add('#%02x%02x%02x' % (r, g, b))
        if bad:
            errors.append(f'{e.name}: цвета вне палитры: {", ".join(sorted(bad)[:6])}{" …" if len(bad) > 6 else ""}')
        if cv.w > 512 or cv.h > 512:
            errors.append(f'{e.name}: слишком большой спрайт {cv.w}×{cv.h}')
        l, b, r, t = e.border
        if l + r > cv.w or b + t > cv.h:
            errors.append(f'{e.name}: border {e.border} больше размера {cv.w}×{cv.h}')
        if not cv.img.getbbox():
            errors.append(f'{e.name}: пустой спрайт')
    if require_all:
        for name in REQUIRED:
            if name not in reg.entries:
                errors.append(f'нет обязательного спрайта {name!r} ({REQUIRED[name]})')
    return errors


def trimmed(reg: pxl.Registry) -> pxl.Registry:
    """Обрезка прозрачных полей (кроме 9-slice и тайлов) с пересчётом pivot: положение в мире не меняется."""
    out = pxl.Registry()
    for e in reg.entries.values():
        if e.extrude or e.border != (0, 0, 0, 0):
            out.entries[e.name] = e
            continue
        bbox = e.canvas.img.getbbox()
        if not bbox or bbox == (0, 0, e.canvas.w, e.canvas.h):
            out.entries[e.name] = e
            continue
        l, t, r, b = bbox
        cv = e.canvas.crop(l, t, r - l, b - t)
        pivot = (e.pivot[0] - l, e.pivot[1] - (e.canvas.h - b))
        out.entries[e.name] = pxl.SpriteEntry(e.name, cv, pivot, e.border, e.group, e.alpha, e.extrude, e.note)
    return out


def _key(e: pxl.SpriteEntry) -> Tuple:
    """Ключ одинакового спрайта: пиксели, размер, pivot, border, extrude. Одинаковые слои (каска при той же позиции головы,
    снаряжение при той же позиции туловища) получают один прямоугольник атласа под разными именами."""
    return (e.canvas.w, e.canvas.h, e.pivot, e.border, e.extrude, e.canvas.img.tobytes())


def pack(reg: pxl.Registry) -> Tuple[Image.Image, Dict[str, Tuple[int, int, int, int]]]:
    """Полочная упаковка по убыванию высоты с отступом PAD; у extrude-спрайтов отступ заполняется краем.
    Одинаковые спрайты пакуются один раз (_key); ширина атласа — 1024 или 2048, если в 1024 по высоте не помещается."""
    unique: Dict[Tuple, pxl.SpriteEntry] = {}
    alias: Dict[str, Tuple] = {}
    for e in sorted(reg.entries.values(), key=lambda e: e.name):
        k = _key(e)
        unique.setdefault(k, e)
        alias[e.name] = k
    items = sorted(unique.values(), key=lambda e: (-e.canvas.h, -e.canvas.w, e.name))
    for atlas_w in (ATLAS_W, 2 * ATLAS_W):
        x = y = shelf_h = 0
        placed: Dict[str, Tuple[int, int, int, int]] = {}
        for e in items:
            w, h = e.canvas.w + 2 * PAD, e.canvas.h + 2 * PAD
            if x + w > atlas_w:
                x = 0
                y += shelf_h
                shelf_h = 0
            placed[e.name] = (x + PAD, y + PAD, e.canvas.w, e.canvas.h)
            x += w
            shelf_h = max(shelf_h, h)
        height = y + shelf_h
        if height <= atlas_w:
            break
    atlas_h = 64
    while atlas_h < height:
        atlas_h *= 2
    atlas = Image.new('RGBA', (atlas_w, atlas_h), (0, 0, 0, 0))
    pos = {name: placed[unique[k].name] for name, k in alias.items()}
    for e in items:
        px, py, w, h = placed[e.name]
        if e.extrude:
            # Края повторяются в отступ: при дробном масштабе соседний тексель не даёт шва.
            src = e.canvas.img
            big = Image.new('RGBA', (w + 2 * PAD, h + 2 * PAD))
            big.paste(src.resize((w + 2 * PAD, h + 2 * PAD), Image.NEAREST), (0, 0))
            for k in range(PAD):
                big.paste(src.crop((0, 0, w, 1)), (PAD, k))
                big.paste(src.crop((0, h - 1, w, h)), (PAD, h + PAD + k))
                big.paste(src.crop((0, 0, 1, h)), (k, PAD))
                big.paste(src.crop((w - 1, 0, w, h)), (w + PAD + k, PAD))
            big.paste(src, (PAD, PAD))
            atlas.paste(big, (px - PAD, py - PAD))
        else:
            atlas.paste(e.canvas.img, (px, py))
    return atlas, pos


def sheet(entries: List[pxl.SpriteEntry], scale: int = 4) -> Image.Image:
    """Превью группы: каждый спрайт ×scale на клетчатом фоне с подписью и pivot."""
    cols_w = 980 * 2
    x = y = row_h = 0
    placed = []
    for e in entries:
        w = max(e.canvas.w * scale, 8 * len(e.name) + 8) + 16
        h = e.canvas.h * scale + 30
        if x + w > cols_w:
            x = 0
            y += row_h
            row_h = 0
        placed.append((e, x, y))
        x += w
        row_h = max(row_h, h)
    img = Image.new('RGBA', (cols_w, max(1, y + row_h)), (40, 40, 52, 255))
    d = ImageDraw.Draw(img)
    for e, x0, y0 in placed:
        w, h = e.canvas.w * scale, e.canvas.h * scale
        for cy in range(0, h, 8):
            for cx in range(0, w, 8):
                c = (70, 70, 86, 255) if (cx // 8 + cy // 8) % 2 else (58, 58, 72, 255)
                d.rectangle([x0 + 8 + cx, y0 + 4 + cy, x0 + 8 + min(cx + 7, w - 1), y0 + 4 + min(cy + 7, h - 1)], fill=c)
        img.alpha_composite(e.canvas.scaled(scale), (x0 + 8, y0 + 4))
        pvx = x0 + 8 + int(e.pivot[0] * scale)
        pvy = y0 + 4 + h - int(e.pivot[1] * scale)
        d.line([pvx - 3, pvy, pvx + 3, pvy], fill=(255, 60, 60, 255))
        d.line([pvx, pvy - 3, pvx, pvy + 3], fill=(255, 60, 60, 255))
        d.text((x0 + 8, y0 + 8 + h), f'{e.name} {e.canvas.w}x{e.canvas.h}', fill=(230, 230, 240, 255))
    return img


def write_cs(atlas: Image.Image, pos: Dict[str, Tuple[int, int, int, int]], reg: pxl.Registry) -> str:
    buf = io.BytesIO()
    atlas.save(buf, format='PNG', optimize=True)
    data = buf.getvalue()
    b64 = base64.b64encode(data).decode('ascii')
    digest = hashlib.sha256(data).hexdigest()[:16]
    aw, ah = atlas.size
    lines = [
        '// <auto-generated>',
        '// Сгенерировано Tools/PixelArt/build.py — не редактировать вручную. Исходники спрайтов: Tools/PixelArt/sprites/*.py.',
        '// </auto-generated>',
        'namespace CatMiners.Runtime',
        '{',
        '    public static partial class PixelAtlasData',
        '    {',
        f'        public const int Width = {aw};',
        f'        public const int Height = {ah};',
        f'        public const string Hash = "{digest}";',
        '',
        '        /// <summary>Имя, прямоугольник в атласе (x, y от левого нижнего угла), pivot и border в пикселях.</summary>',
        '        public static readonly PixelSpriteDef[] Sprites =',
        '        {',
    ]
    for name in sorted(pos):
        e = reg.entries[name]
        x, y, w, h = pos[name]
        uy = ah - y - h
        l, b, r, t = e.border
        lines.append(f'            new PixelSpriteDef("{name}", {x}, {uy}, {w}, {h}, {e.pivot[0]:g}f, {e.pivot[1]:g}f, {l}, {b}, {r}, {t}),')
    lines += ['        };', '', '        /// <summary>PNG атласа в base64.</summary>', '        public const string Png =']
    chunk = 1600
    parts = [b64[i:i + chunk] for i in range(0, len(b64), chunk)] or ['']
    for i, p in enumerate(parts):
        lines.append(f'            "{p}"' + (';' if i == len(parts) - 1 else ' +'))
    lines += ['    }', '}', '']
    text = '\n'.join(lines)
    os.makedirs(os.path.dirname(CS_PATH), exist_ok=True)
    with open(CS_PATH, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return digest


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', nargs='*', help='модули для проверки и превью (без записи атласа)')
    ap.add_argument('--check', action='store_true', help='только проверка')
    ap.add_argument('--allow-missing', action='store_true', help='записать атлас, даже если не хватает обязательных спрайтов (в Unity — заглушки)')
    args = ap.parse_args()
    import gen_palette
    gen_palette.main()
    os.makedirs(OUT, exist_ok=True)
    partial = bool(args.only)
    modules = args.only if partial else MODULES
    reg = load_modules(modules)
    errors = check(reg, require_all=not partial and not args.allow_missing)
    if partial:
        # Обязательные имена своих групп: префиксы из manifest.REQUIRED_GROUPS.
        from manifest import REQUIRED_GROUPS
        for m in modules:
            for name in REQUIRED_GROUPS.get(m, []):
                if name not in reg.entries:
                    errors.append(f'[{m}] нет обязательного спрайта {name!r} ({REQUIRED[name]})')
    for g in sorted({e.group for e in reg.entries.values()}):
        entries = sorted([e for e in reg.entries.values() if e.group == g], key=lambda e: e.name)
        sheet(entries).save(os.path.join(OUT, f'sheet_{g}.png'))
        print(f'[pixel] {g}: {len(entries)} спрайтов → out/sheet_{g}.png')
    if errors:
        print(f'[pixel] ОШИБКИ ({len(errors)}):')
        for e in errors:
            print('  - ' + e)
    if args.check or partial:
        return 1 if errors else 0
    if errors:
        print('[pixel] атлас не записан: исправьте ошибки')
        return 1
    reg = trimmed(reg)
    atlas, pos = pack(reg)
    atlas.save(os.path.join(OUT, 'atlas.png'))
    digest = write_cs(atlas, pos, reg)
    manifest = {n: {'rect': pos[n], 'pivot': reg.entries[n].pivot, 'border': reg.entries[n].border, 'group': reg.entries[n].group}
                for n in sorted(pos)}
    with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8') as f:
        json.dump({'atlas': atlas.size, 'hash': digest, 'sprites': manifest}, f, ensure_ascii=False, indent=1)
    distinct = len({tuple(v) for v in pos.values()})
    print(f'[pixel] атлас {atlas.size[0]}×{atlas.size[1]}, {len(pos)} спрайтов ({distinct} разных), hash {digest} → {os.path.relpath(CS_PATH, ROOT)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
