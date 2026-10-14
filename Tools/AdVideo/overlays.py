"""Pixel-perfect overlay toolkit for the Cat Miners video ads (PIL + numpy, game art and palette only).

Every overlay is composed on the game's own art grid (atlas crops, 9-slices, Pixeloid Sans at 1 font px)
and scaled once with NEAREST by an integer factor, so the pixels stay crisp after the H.264 encode.
The only non-integer scaling is in short pops / slams (<= 0.22 s, as the game's own pops); looping motion
(the PLAY FREE beat throb, Z growth) stays on the pixel grid (9-slice re-sizing, integer size steps).

References: Docs/Marketing/research/art.md (atlas, palette, font and UI-scale rules) and
Docs/Marketing/AD_SCRIPTS.md (128 BPM grid, Reels safe zone y = 250..1350, side margins >= 64 px).

Usage (any working directory; run Python with -B so no __pycache__ lands in the synced tree):

    import sys; sys.path.insert(0, '<project>/Tools/AdVideo')
    import overlays as ov
    frame = Image.open('shot.png').convert('RGBA')                  # 1080x1920 game footage frame
    ov.draw_caption(frame, 'Tap to *help* them dig!', t - 7.5, cy=400)  # pop-in caption, *gold* words
    ov.paw(frame, 540, 1100, t - 8.2)                               # tutorial paw, tap lands at t = 8.2 s
    ov.draw_counter(frame, 1.2e6, t - 31.0, cx=540, cy=640)         # '+1.2M' with a coin
    frame = ov.wrong_way(frozen, t - 3.75)                          # full-frame "WRONG WAY!" effect
    frame = ov.zzz(frame, t - 18.75)                                # "close the game" dim + beat Zs
    end = ov.end_card(t - 41.25, 'How deep can *YOUR* cats dig?')   # whole 1080x1920 end-card frame
    # (end_card's paw taps PLAY FREE at taps=END_TAPS = 1.6 s and 3.0 s; taps=() draws no paw)

Conventions
  * t is seconds since the element started; negative t means "not visible yet" (empty image / no-op).
  * draw_* / paw* / flash draw onto the given RGBA frame in place and also return it; wrong_way, zzz,
    end_card and apply_shake return a new frame; the other functions return RGBA images.
  * Images returned by cached builders are shared: copy them before modifying.
  * Positions are screen px of the 1080x1920 frame; overlay top-left corners snap to multiples of 4.
  * Colours are palette names from Tools/PixelArt/pxl.py ('gold3', 'navy0', ...) or RGB(A) tuples.
  * Markup: '*word*' switches the highlight colour (gold3 on cream text) on and off.
  * Captions default to k = 6 (Pixeloid caps 7 font px -> 42 px, readable at a glance on a phone) and a
    952 px max plate width (64 px side margins); use k = 5 (35 px caps) only for secondary lines.
  * Reels safe zone for key elements: y 250..1350, x 64..1016 (the end card is laid out inside it).

Self-test and preview sheets: Tools/AdVideo/overlay_preview.py.
"""
from __future__ import annotations

import json
import math
import sys
import zlib
from functools import lru_cache
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple, Union

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# ───────────────────────── Paths and constants ─────────────────────────
HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
PIXELART = ROOT / 'Tools' / 'PixelArt'
ATLAS_PNG = PIXELART / 'out' / 'atlas.png'
MANIFEST_JSON = PIXELART / 'out' / 'manifest.json'
FONT_DIR = ROOT / 'Assets' / 'Game' / 'Resources' / 'Fonts'

# The palette lives in Tools/PixelArt/pxl.py (single source of truth). Import it without writing bytecode
# next to it and without leaving Tools/PixelArt on sys.path.
_added = str(PIXELART) not in sys.path
_dwb = sys.dont_write_bytecode
sys.dont_write_bytecode = True
if _added:
    sys.path.insert(0, str(PIXELART))
try:
    import pxl  # noqa: E402
finally:
    if _added:
        sys.path.remove(str(PIXELART))
    sys.dont_write_bytecode = _dwb

PAL: Dict[str, Tuple[int, int, int]] = pxl.PALETTE

W, H = 1080, 1920            # ad frame size
UI = 4                       # screen px per art px for UI (PixelArt.UiPixel)
HERO = 8                     # hero items: tutorial paw, end-card cats
SAFE_TOP, SAFE_BOTTOM, SAFE_X = 250, 1350, 64   # Reels safe zone for key elements
BPM = 128
BEAT = 60.0 / BPM            # 0.46875 s
BAR = 4 * BEAT               # 1.875 s

Color = Union[str, Tuple[int, ...]]
FLIP = Image.Transpose.FLIP_LEFT_RIGHT


def rgba(c: Color, a: int = 255) -> Tuple[int, int, int, int]:
    """Palette name or RGB(A) tuple -> RGBA tuple (alpha overrides only for names and RGB)."""
    if isinstance(c, str):
        r, g, b = PAL[c]
        return (r, g, b, a)
    if len(c) == 3:
        return (c[0], c[1], c[2], a)
    return tuple(c)  # type: ignore[return-value]


def snap(v: float, g: int = 4) -> int:
    """Round to the nearest multiple of g (UI grid; even offsets keep yuv420 chroma clean)."""
    return int(math.floor(v / g + 0.5)) * g


def ease_out(u: float) -> float:
    u = min(1.0, max(0.0, u))
    return 1.0 - (1.0 - u) ** 3


def ease_in_out(u: float) -> float:
    u = min(1.0, max(0.0, u))
    return u * u * (3.0 - 2.0 * u)


def _hash01(*xs: float) -> float:
    """Deterministic pseudo-random number in [0, 1) from a few values (stable across runs)."""
    h = 0x9E3779B9
    for x in xs:
        h = (h ^ (int(x * 1000003) & 0xFFFFFFFF)) * 0x85EBCA6B & 0xFFFFFFFF
        h ^= h >> 13
    return (h & 0xFFFFFF) / float(0x1000000)


# ───────────────────────── Atlas ─────────────────────────
@lru_cache(maxsize=1)
def _atlas():
    sprites = json.loads(MANIFEST_JSON.read_text(encoding='utf-8'))['sprites']
    img = Image.open(ATLAS_PNG).convert('RGBA')
    img.load()
    return img, sprites


def has_sprite(name: str) -> bool:
    return name in _atlas()[1]


@lru_cache(maxsize=None)
def sprite(name: str) -> Image.Image:
    """Trimmed atlas crop of a sprite, art px (shared, do not modify)."""
    img, m = _atlas()
    x, y, w, h = m[name]['rect']
    return img.crop((x, y, x + w, y + h))


def pivot(name: str) -> Tuple[float, float]:
    """Sprite pivot in top-down px of its trimmed crop (manifest stores it from the bottom-left)."""
    px, py = _atlas()[1][name]['pivot']
    return px, sprite(name).height - py


@lru_cache(maxsize=None)
def _flipped(name: str) -> Image.Image:
    return sprite(name).transpose(FLIP)


def place(dst: Image.Image, name: str, ax: float, ay: float, flip: bool = False) -> None:
    """Draw a sprite onto an art-px canvas with its pivot at (ax, ay) (top-down coordinates)."""
    im = _flipped(name) if flip else sprite(name)
    px, py = pivot(name)
    if flip:
        px = im.width - px
    blit(dst, im, round(ax - px), round(ay - py))


@lru_cache(maxsize=1024)
def nine(name: str, w: int, h: int) -> Image.Image:
    """9-slice of an atlas sprite at w x h art px (edges and centre stretched with NEAREST, as in Unity)."""
    s = sprite(name)
    sw, sh = s.size
    l, b, r, t = _atlas()[1][name]['border']
    w, h = max(int(w), l + r), max(int(h), t + b)
    out = Image.new('RGBA', (w, h))
    for sx0, sx1, dx0, dx1 in ((0, l, 0, l), (l, sw - r, l, w - r), (sw - r, sw, w - r, w)):
        for sy0, sy1, dy0, dy1 in ((0, t, 0, t), (t, sh - b, t, h - b), (sh - b, sh, h - b, h)):
            if sx1 > sx0 and sy1 > sy0 and dx1 > dx0 and dy1 > dy0:
                out.paste(s.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
    return out


def up(img: Image.Image, k: int) -> Image.Image:
    """Integer NEAREST upscale (the only scaling used for static overlays)."""
    k = int(k)
    return img if k == 1 else img.resize((img.width * k, img.height * k), Image.NEAREST)


@lru_cache(maxsize=2048)
def sprite_k(name: str, k: int, flip: bool = False) -> Image.Image:
    """Atlas sprite scaled by integer k (shared, do not modify)."""
    return up(_flipped(name) if flip else sprite(name), k)


@lru_cache(maxsize=256)
def recolor(name: str, mapping: Tuple[Tuple[str, str], ...]) -> Image.Image:
    """Sprite with palette colours swapped, e.g. (('red4', 'cream3'), ...); stays inside the palette."""
    a = np.array(sprite(name))
    out = a.copy()
    for src, dst in mapping:
        sel = np.all(a[..., :3] == PAL[src], axis=-1) & (a[..., 3] > 0)
        out[sel, :3] = PAL[dst]
    return Image.fromarray(out, 'RGBA')


# ───────────────────────── Compositing helpers ─────────────────────────
def blit(dst: Image.Image, src: Image.Image, x: float, y: float) -> Image.Image:
    """alpha_composite src onto dst at (x, y), clipping at every edge (negative coordinates are fine)."""
    x, y = int(x), int(y)
    if x >= dst.width or y >= dst.height or x + src.width <= 0 or y + src.height <= 0:
        return dst
    if x < 0 or y < 0:
        src = src.crop((max(0, -x), max(0, -y), src.width, src.height))
        x, y = max(0, x), max(0, y)
    dst.alpha_composite(src, (x, y))
    return dst


def blit_center(dst: Image.Image, src: Image.Image, cx: float, cy: float, g: int = 4) -> Image.Image:
    """Blit src centred at (cx, cy); the top-left corner snaps to a multiple of g."""
    return blit(dst, src, snap(cx - src.width / 2, g), snap(cy - src.height / 2, g))


def scale_about(img: Image.Image, s: float) -> Image.Image:
    """Non-integer NEAREST scale, only for short pops (PIXEL_STYLE_GUIDE allows <= 0.25 s)."""
    if abs(s - 1.0) < 1e-3:
        return img
    w, h = max(1, round(img.width * s)), max(1, round(img.height * s))
    return img.resize((w, h), Image.NEAREST)


EMPTY = Image.new('RGBA', (1, 1))


def pop_scale(t: float, dur: float = 0.2, s0: float = 0.6, peak: float = 1.08) -> float:
    """Pop-in: s0 -> peak (ease-out, first 60 %) -> 1.0 over dur seconds; 1.0 afterwards."""
    if t >= dur:
        return 1.0
    if t <= 0:
        return s0
    tp = dur * 0.6
    if t < tp:
        return s0 + (peak - s0) * ease_out(t / tp)
    return peak + (1.0 - peak) * ease_in_out((t - tp) / (dur - tp))


def exit_scale(t_out: float, dur: float = 0.15, peak: float = 1.06) -> float:
    """Pop-out: 1.0 -> peak -> 0 over dur seconds after the exit starts (t_out = time since exit)."""
    if t_out <= 0:
        return 1.0
    if t_out >= dur:
        return 0.0
    u = t_out / dur
    if u < 0.3:
        return 1.0 + (peak - 1.0) * ease_out(u / 0.3)
    return peak * (1.0 - ease_in_out((u - 0.3) / 0.7))


def animated(img: Image.Image, t: float, exit_at: Optional[float] = None, dur: float = 0.2,
             s0: float = 0.6, peak: float = 1.08) -> Image.Image:
    """Apply the standard pop-in (and optional pop-out at exit_at) to a static overlay image."""
    if t < 0:
        return EMPTY
    s = pop_scale(t, dur, s0, peak)
    if exit_at is not None and t >= exit_at:
        s *= exit_scale(t - exit_at)
        if s < 0.05:
            return EMPTY
    return scale_about(img, s)


# Bool-mask helpers (art px or font px grids).
def _shift(m: np.ndarray, dx: int, dy: int) -> np.ndarray:
    """Shift a bool mask by (dx, dy) inside the same shape, filling with False."""
    out = np.zeros_like(m)
    h, w = m.shape
    xs0, xs1 = max(0, -dx), min(w, w - dx)
    ys0, ys1 = max(0, -dy), min(h, h - dy)
    if xs1 > xs0 and ys1 > ys0:
        out[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx] = m[ys0:ys1, xs0:xs1]
    return out


def _dilate(m: np.ndarray, diagonal: bool = True) -> np.ndarray:
    o = m.copy()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        o |= _shift(m, dx, dy)
    if diagonal:
        for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
            o |= _shift(m, dx, dy)
    return o


def _paint(shape: Tuple[int, int], layers: Sequence[Tuple[np.ndarray, Color]]) -> Image.Image:
    """RGBA image from (mask, colour) layers, later layers on top."""
    out = np.zeros(shape + (4,), np.uint8)
    for m, c in layers:
        out[m] = rgba(c)
    return Image.fromarray(out, 'RGBA')


# ───────────────────────── Text (Pixeloid Sans, 1 font px grid) ─────────────────────────
GLYPH_TOP = 2      # PIL draws the cap top of Pixeloid size 9 on row 2
GLYPH_H = 9        # cap height 7 + descender 2 (font px)
CAP_H = 7
LINE_PITCH = 11    # baseline-to-baseline, font px (ascent 9 + descent 2)


@lru_cache(maxsize=2)
def _font(bold: bool) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT_DIR / f'PixeloidSans-{"Bold" if bold else "Regular"}.ttf'), 9)


def _parse(markup: str) -> List[Tuple[str, bool]]:
    """'Mine *100*' -> [(char, highlighted), ...]; '*' toggles the highlight."""
    out, hi = [], False
    for ch in markup:
        if ch == '*':
            hi = not hi
        else:
            out.append((ch, hi))
    return out


def _unparse(chars: Sequence[Tuple[str, bool]]) -> str:
    s, hi = [], False
    for ch, h in chars:
        if ch != ' ' and h != hi:
            s.append('*')
            hi = h
        s.append(ch)
    if hi:
        s.append('*')
    return ''.join(s)


def plain(markup: str) -> str:
    return markup.replace('*', '')


def text_width(text: str, bold: bool = True) -> int:
    """Advance width of (markup) text in font px."""
    return int(math.ceil(_font(bold).getlength(plain(text))))


@lru_cache(maxsize=4096)
def _line(markup: str, bold: bool) -> Tuple[np.ndarray, np.ndarray]:
    """One markup line at font px: (ink mask, highlight mask), GLYPH_H rows, trimmed to the ink columns."""
    chars = _parse(markup)
    txt = ''.join(c for c, _ in chars)
    f = _font(bold)
    w = int(math.ceil(f.getlength(txt))) + 2
    im = Image.new('L', (max(1, w), GLYPH_H))
    d = ImageDraw.Draw(im)
    d.fontmode = '1'                        # no anti-aliasing: exact pixel font
    d.text((0, -GLYPH_TOP), txt, font=f, fill=255)
    mask = np.asarray(im) > 127
    hi = np.zeros_like(mask)
    i = 0
    while i < len(chars):                   # colour columns between glyph advances of each highlighted run
        j = i
        while j < len(chars) and chars[j][1] == chars[i][1]:
            j += 1
        if chars[i][1]:
            hi[:, round(f.getlength(txt[:i])):round(f.getlength(txt[:j]))] = True
        i = j
    cols = np.nonzero(mask.any(axis=0))[0]
    if len(cols) == 0:                      # blank line keeps its advance width
        z = np.zeros((GLYPH_H, max(1, int(f.getlength(txt)))), bool)
        z.flags.writeable = False
        return z, z
    a, b = cols[0], cols[-1] + 1
    mask, hi = mask[:, a:b].copy(), (hi & mask)[:, a:b].copy()
    mask.flags.writeable = hi.flags.writeable = False
    return mask, hi


def _block(lines: Sequence[str], bold: bool, align: str = 'center',
           pitch: int = LINE_PITCH) -> Tuple[np.ndarray, np.ndarray]:
    """Stack markup lines into one (ink, highlight) mask pair at font px."""
    rows = [_line(l, bold) for l in lines]
    w = max(m.shape[1] for m, _ in rows)
    h = (len(rows) - 1) * pitch + GLYPH_H
    ink = np.zeros((h, w), bool)
    hil = np.zeros((h, w), bool)
    for i, (m, hm) in enumerate(rows):
        x = 0 if align == 'left' else (w - m.shape[1] if align == 'right' else (w - m.shape[1]) // 2)
        ink[i * pitch:i * pitch + GLYPH_H, x:x + m.shape[1]] |= m
        hil[i * pitch:i * pitch + GLYPH_H, x:x + m.shape[1]] |= hm
    return ink, hil


def wrap_text(text: str, max_w: int, bold: bool = True) -> List[str]:
    """Greedy word wrap of markup text to max_w font px; '\\n' forces a break; highlight survives breaks."""
    f = _font(bold)
    out: List[str] = []
    for para in text.split('\n'):
        words, cur = [], []
        for ch, h in _parse(para):
            if ch == ' ':
                words.append(cur)
                cur = []
            else:
                cur.append((ch, h))
        words.append(cur)
        line: List[Tuple[str, bool]] = []
        for wd in words:
            cand = line + ([(' ', False)] if line else []) + wd
            if line and f.getlength(''.join(c for c, _ in cand)) > max_w:
                out.append(_unparse(line))
                line = list(wd)
            else:
                line = cand
        out.append(_unparse(line))
    return out


def wrap_balanced(text: str, max_w: int, bold: bool = True) -> List[str]:
    """Word wrap with the same line count as the greedy wrap but lines as even as possible (no widows)."""
    lines = wrap_text(text, max_w, bold)
    if len(lines) <= 1:
        return lines
    lo, hi = 1, max_w
    while lo < hi:                       # smallest width that still gives the same number of lines
        mid = (lo + hi) // 2
        if len(wrap_text(text, mid, bold)) <= len(lines):
            hi = mid
        else:
            lo = mid + 1
    return wrap_text(text, lo, bold)


def _text_art(ink: np.ndarray, hil: np.ndarray, color: Color, hi: Color, shadow: Optional[Color],
              outline: Optional[Color]) -> Image.Image:
    """Colour a text mask at font px: optional 8-neighbour outline, 1 px drop shadow straight down."""
    p = 1 if outline else 0
    h, w = ink.shape
    shape = (h + 2 * p + (1 if shadow else 0), w + 2 * p)
    m = np.zeros(shape, bool)
    m[p:p + h, p:p + w] = ink
    hm = np.zeros(shape, bool)
    hm[p:p + h, p:p + w] = hil
    layers = []
    body = m
    if outline:
        body = _dilate(m)
    if shadow:
        layers.append((_shift(body, 0, 1) & ~body, shadow))
    if outline:
        layers.append((body & ~m, outline))
    layers.append((m & ~hm, color))
    layers.append((hm, hi))
    return _paint(shape, layers)


@lru_cache(maxsize=1024)
def pixel_text(text: str, color: Color = 'cream3', k: int = 4, bold: bool = True, shadow: bool = True,
               outline: bool = False, hi: Color = 'gold3', align: str = 'center', pitch: int = LINE_PITCH,
               shadow_color: Color = 'ink1', outline_color: Color = 'ink1') -> Image.Image:
    """Game-style text: Pixeloid at 1 font px, NEAREST x k (k=4 -> size 36, k=5 -> 45 ...).

    Supports markup ('Mine *100*: *1.2M* gold' -> starred words in `hi`) and '\\n' line breaks.
    Shadow = ink1, 1 font px straight down (UiKit rule); outline = 8-neighbour ink1 ring for floating text.
    Returns a shared RGBA image (copy before modifying)."""
    ink, hil = _block(text.split('\n'), bold, align, pitch)
    return up(_text_art(ink, hil, color, hi, shadow_color if shadow else None,
                        outline_color if outline else None), k)


def rich_text(markup: str, color: Color = 'cream3', k: int = 4, hi: Color = 'gold3', **kw) -> Image.Image:
    """Alias of pixel_text that documents the markup: 'Mine *100*: *1.2M* gold'."""
    return pixel_text(markup, color, k, hi=hi, **kw)


# Fancy "title" text: thick letters (fs art px per font px) with a vertical colour ramp, top bevel,
# extruded underside, dark outline and ink drop shadow. Used by the logo, counters and split tags.
RAMPS = {
    #          bevel     ramp rows (fraction of cap height -> colour)                          extrude
    'gold':   ('gold5', ((0.0, 'gold4'), (0.28, 'gold3'), (0.6, 'amber3'), (0.86, 'amber2')), 'amber1'),
    'cream':  ('cream4', ((0.0, 'cream4'), (0.28, 'cream3'), (0.6, 'cream2'), (0.86, 'cream1')), 'navy4'),
    'silver': ('cream4', ((0.0, 'stone7'), (0.3, 'stone6'), (0.62, 'stone5'), (0.86, 'stone4')), 'stone2'),
    'red':    ('red6', ((0.0, 'red5'), (0.3, 'red4'), (0.62, 'red3'), (0.86, 'red2')), 'red1'),
    'green':  ('green6', ((0.0, 'green6'), (0.3, 'green5'), (0.62, 'green4'), (0.86, 'green3')), 'green2'),
    'teal':   ('teal6', ((0.0, 'teal5'), (0.3, 'teal4'), (0.62, 'teal3'), (0.86, 'teal2')), 'teal1'),
}


# Pixeloid's own '+' is a 3x3 dot-sized cross on cap rows 2..4; inside a thick title outline it reads as a
# bullet ('·1.2M'). Title text uses this bold plus instead: 6x6 font px, 2 px arms like the Bold stems.
_BIG_PLUS = np.array([[0, 0, 1, 1, 0, 0]] * 2 + [[1, 1, 1, 1, 1, 1]] * 2 + [[0, 0, 1, 1, 0, 0]] * 2, bool)
_BIG_PLUS_TOP = 1          # cap rows 1..6 (sits on the baseline like the digits)


def _title_line(text: str, bold: bool = True) -> np.ndarray:
    """One plain line at font px (GLYPH_H rows) with every '+' drawn as _BIG_PLUS (1 px letter gap)."""
    if '+' not in text:
        return _line(text, bold)[0]
    space = int(round(_font(bold).getlength(' ')))
    parts: List[np.ndarray] = []
    for i, seg in enumerate(text.split('+')):
        if i:
            p = np.zeros((GLYPH_H, _BIG_PLUS.shape[1]), bool)
            p[_BIG_PLUS_TOP:_BIG_PLUS_TOP + _BIG_PLUS.shape[0]] = _BIG_PLUS
            parts.append(p)
        core = seg.strip(' ')
        lead, trail = len(seg) - len(seg.lstrip(' ')), len(seg) - len(seg.rstrip(' '))
        if lead:
            parts.append(np.zeros((GLYPH_H, lead * space - 1), bool))
        if core:
            parts.append(_line(core, bold)[0])
        if trail:
            parts.append(np.zeros((GLYPH_H, trail * space - 1), bool))
    gap = np.zeros((GLYPH_H, 1), bool)
    out: List[np.ndarray] = []
    for p in parts:
        if out:
            out.append(gap)
        out.append(p)
    return np.concatenate(out, axis=1)


@lru_cache(maxsize=256)
def fancy_text(text: str, fs: int = 2, ramp: str = 'gold', outline: Color = 'wood0',
               outline2: Optional[Color] = None, extrude: int = 2, shadow: Optional[Color] = 'ink1',
               bevel: bool = True, bold: bool = True) -> Image.Image:
    """Title text at ART px (scale it with up(img, k)). One line; fs = art px per font px.
    '+' is drawn as a bold 6x6 plus (the font's own plus is too small under a thick outline)."""
    ink = _title_line(plain(text), bold)
    m0 = np.kron(ink, np.ones((fs, fs), bool))
    bevel_c, rows, ext_c = RAMPS[ramp]
    p = 1 + (1 if outline2 else 0)
    h, w = m0.shape
    shape = (h + 2 * p + extrude + (1 if shadow else 0), w + 2 * p)
    m = np.zeros(shape, bool)
    m[p:p + h, p:p + w] = m0
    ext = np.zeros(shape, bool)
    for i in range(1, extrude + 1):
        ext |= _shift(m, 0, i)
    ext &= ~m
    sil = m | ext
    ol = _dilate(sil) & ~sil
    full = sil | ol
    layers: List[Tuple[np.ndarray, Color]] = []
    ol2 = None
    if outline2:
        ol2 = _dilate(full) & ~full
        full = full | ol2
    if shadow:
        layers.append((_shift(full, 0, 1) & ~full, shadow))
    if ol2 is not None:
        layers.append((ol2, outline2))
    layers.append((ol, outline))
    layers.append((ext, ext_c))
    yy = np.arange(shape[0])[:, None] - p
    cap = CAP_H * fs
    for frac, c in rows:                     # ramp rows top -> bottom (later rows overwrite)
        layers.append((m & (yy >= int(round(frac * cap))), c))
    if bevel:                                # top-lit bevel: 1 px highlight on every upward-facing edge
        layers.append((m & ~_shift(m, 0, 1), bevel_c))
    return _paint(shape, layers)


# ───────────────────────── Numbers (port of Core/Numbers/NumberFormat.cs) ─────────────────────────
def _trim_zeros(s: str) -> str:
    if '.' not in s:
        return s
    s = s.rstrip('0')
    return s[:-1] if s.endswith('.') else s


def _suffix(group: int) -> Optional[str]:
    short = ['', 'K', 'M', 'B', 'T']
    if group < 0:
        return None
    if group < len(short):
        return short[group]
    if group * 3 >= 308:
        return None
    i = group - len(short)
    if i >= 26 * 26:
        return None
    return chr(ord('a') + i // 26) + chr(ord('a') + i % 26)


def format_number(value: float) -> str:
    """Exactly like NumberFormat.Format: 3 significant digits truncated down, K/M/B/T then aa, ab..."""
    if value == 0:
        return '0'
    if value < 0:
        return '-' + format_number(-value)
    e = int(math.floor(math.log10(value)))
    mant = value / (10.0 ** e)
    if mant >= 10.0:
        mant /= 10.0
        e += 1
    elif mant < 1.0:
        mant *= 10.0
        e -= 1
    if e < 3:
        if value >= 100 or abs(value - round(value)) < 1e-9:
            return str(int(math.floor(value + 1e-9)))
        if value >= 10:
            return _trim_zeros(f'{value:.1f}')
        return _trim_zeros(f'{value:.2f}')
    group = e // 3
    suf = _suffix(group) if e < 308 else None
    if suf is None:
        m = min(999.0, math.floor(mant * 100.0 + 1e-9)) / 100.0
        return f'{m:.2f}e{e}'
    digits = e - group * 3
    scaled = mant * 10 ** digits
    dec = 2 - digits
    trunc = math.floor(scaled * 10 ** dec + 1e-9) / 10 ** dec
    return _trim_zeros(f'{trunc:.{dec}f}') + suf


def roll_value(v0: float, v1: float, t: float, dur: float = 1.0) -> float:
    """Counter roll from v0 to v1 over dur s (log-space ease-out when both are positive: big numbers fly)."""
    u = ease_out(t / dur) if dur > 0 else 1.0
    if v0 > 0 and v1 > 0:
        return 10 ** (math.log10(v0) + (math.log10(v1) - math.log10(v0)) * u)
    return v0 + (v1 - v0) * u


# ───────────────────────── 1-2. Captions ─────────────────────────
# Caption plates are game 9-slices at UI scale (4 px per art px); the text uses its own integer k.
# pad = (x, y) inner padding in art px, lip = art px of the button's bottom lip (text sits above it).
CAPTION_STYLES: Dict[str, dict] = {
    'plank':  dict(panel='ui_window_header', ink='cream3', hi='gold3', shadow=True, pad=(13, 5), lip=0),
    'toast':  dict(panel='ui_toast', ink='cream3', hi='gold3', shadow=True, pad=(9, 6), lip=0),
    'dark':   dict(panel='ui_panel_dark', ink='cream3', hi='gold3', shadow=True, pad=(8, 6), lip=0),
    'window': dict(panel='ui_window', ink='cream3', hi='gold3', shadow=True, pad=(13, 11), lip=0),
    'bubble': dict(panel='ui_bubble', ink='wood0', hi='red3', shadow=False, pad=(8, 6), lip=0,
                   tail='ui_bubble_tail'),
    'red':    dict(panel='ui_btn_red', ink='cream3', hi='gold4', shadow=True, pad=(9, 5), lip=2,
                   icon='icon_close', icon_map=(('red6', 'cream4'), ('red4', 'cream3'), ('red3', 'cream1'))),
    'green':  dict(panel='ui_btn_green', ink='cream3', hi='gold4', shadow=True, pad=(9, 5), lip=2,
                   icon='icon_check', icon_map=(('green6', 'cream4'), ('green5', 'cream3'), ('green4', 'cream1'))),
    'gold':   dict(panel='ui_btn_gold', ink='wood0', hi='red3', shadow=False, pad=(9, 5), lip=2),
    'blue':   dict(panel='ui_btn_blue', ink='cream3', hi='gold4', shadow=True, pad=(9, 5), lip=2),
}
# Main captions: k = 6 -> 42 px cap height (>= 36 px reads at a glance on a phone); k = 5 (35 px) only for
# secondary lines. Default width keeps the plate inside the 64 px side margins.
CAPTION_K = 6
CAPTION_W = W - 2 * SAFE_X                     # 952 px


@lru_cache(maxsize=256)
def caption_static(text: str, style: str = 'plank', width_max: int = CAPTION_W, k: int = CAPTION_K,
                   icon: Optional[bool] = None, min_w: int = 0) -> Image.Image:
    """Static caption plate (no animation). The plate's centre is the image centre, so blit_center()
    centres the plate even when the bubble tail or the badge icon sticks out. Shared image.

    Text wraps (balanced) to fit width_max px including the plate; icon=False hides the style's icon
    (red: white X, green: white check), icon=True adds a star to styles without one."""
    st = CAPTION_STYLES[style]
    P = UI
    padx, pady = st['pad']
    icon_name = st.get('icon') if icon is not False else None
    if icon is True and not icon_name:
        icon_name = 'icon_star'
    ic = None
    if icon_name:
        ic = up(recolor(icon_name, st.get('icon_map', ())), 8 if k >= 6 else 6)   # ~2 cap heights tall
    icon_w = (ic.width + 3 * P) if ic else 0
    lines = wrap_balanced(text, (width_max - 2 * padx * P - icon_w - (P - 1)) // k)   # plate <= width_max
    ink, hil = _block(lines, True)
    txt = up(_text_art(ink, hil, st['ink'], st['hi'], 'ink1' if st['shadow'] else None, None), k)
    vis_h = ((len(lines) - 1) * LINE_PITCH + CAP_H + (1 if st['shadow'] else 0)) * k   # caps + shadow rows
    pw = max(min_w // P, math.ceil((txt.width + icon_w) / P) + 2 * padx)
    ph = math.ceil(vis_h / P) + 2 * pady + st['lip']
    plate = up(nine(st['panel'], pw, ph), P).copy()
    face_h = (ph - st['lip']) * P                                  # button face above the lip
    gx = snap((plate.width - txt.width - icon_w) / 2, 2)           # icon + text centred as one group
    blit(plate, txt, gx + icon_w, snap(face_h / 2 - vis_h / 2, 2))
    # Overhangs (bubble tail below, badge icon above/below) -> canvas symmetric around the plate.
    tail = sprite_k(st['tail'], P, True) if st.get('tail') else None
    ext_v = 0
    if tail is not None:
        ext_v = tail.height - 2 * P
    if ic is not None:
        ext_v = max(ext_v, snap((ic.height - face_h) / 2 + P, 2))
    out = Image.new('RGBA', (plate.width, plate.height + 2 * ext_v))
    blit(out, plate, 0, ext_v)
    if tail is not None:                                           # tail 12 art px from the left edge
        blit(out, tail, 12 * P, ext_v + plate.height - 2 * P)
    if ic is not None:
        blit(out, ic, gx, ext_v + snap(face_h / 2 - ic.height / 2, 2))
    return out


def caption(text: str, t_since_start: float, style: str = 'plank', width_max: int = CAPTION_W,
            k: int = CAPTION_K, exit_at: Optional[float] = None, icon: Optional[bool] = None) -> Image.Image:
    """Animated caption: pop-in 0.6 -> 1.08 -> 1.0 over 0.2 s, static afterwards, optional pop-out at exit_at
    (seconds since start). Styles: plank, toast, dark, window, bubble, red, green, gold, blue."""
    return animated(caption_static(text, style, width_max, k, icon), t_since_start, exit_at)


def draw_caption(frame: Image.Image, text: str, t_since_start: float, cx: float = W / 2, cy: float = 400,
                 **kw) -> Image.Image:
    """caption() centred at (cx, cy) on the frame."""
    img = caption(text, t_since_start, **kw)
    if img is not EMPTY:
        blit_center(frame, img, cx, cy)
    return frame


# ───────────────────────── 3. Tutorial paw ─────────────────────────
HAND = 'icon_hand_big'
# Fingertip = centre of canvas pixel (2, 2) (TutorialHint.cs: (2.5 - 12, 12 - 2.5) from the 24x24 centre).
# The trimmed crop starts at canvas (0, 0): canvas pivot (12, 12) minus trimmed pivot (12, 10) from the
# bottom gives a 0 px left and 2 px bottom trim, so crop px == canvas px.
HAND_TIP = (2.5, 2.5)
PRESS_SCALE = 0.9


def paw_lift(t: Optional[float]) -> int:
    """Hand offset (in hand art px, x8 = screen px) around a tap at t = 0, mirroring TutorialHint's
    press loop [2,2,2,2,2,1,0,0,1,2] at 0.1 s steps: 2 = hovering 16 px down-right, 0 = pressed."""
    if t is None or t < -0.1 or t >= 0.3:
        return 2
    if t < 0.0 or t >= 0.2:
        return 1
    return 0


def _draw_hand(frame: Image.Image, x: float, y: float, lift: int, flip: bool, k: int = HERO,
               scale: float = 1.0) -> None:
    im = sprite_k(HAND, k, flip)
    tipx = (sprite(HAND).width - HAND_TIP[0]) if flip else HAND_TIP[0]
    tipy = HAND_TIP[1]
    if abs(scale - 1.0) > 1e-3:
        im = scale_about(im, scale)
    s = im.width / sprite(HAND).width                       # actual screen px per art px after the press
    sx = -1 if flip else 1
    off = lift * k
    blit(frame, im, round(x - tipx * s + off * sx), round(y - tipy * s + off))


def _draw_ring(frame: Image.Image, x: float, y: float, t: float, k: int = UI) -> None:
    """Tap burst at the fingertip: white hit star (0-0.15 s) + expanding fx_ring (0-0.28 s)."""
    if t < 0:
        return
    i = int(t / 0.07)
    if i < 4:
        n = f'fx_ring_{i}'
        im = sprite_k(n, k)
        px, py = pivot(n)
        blit(frame, im, round(x - px * k), round(y - py * k))
    j = int(t / 0.05)
    if j < 3:
        n = f'fx_hit_{j}'
        im = sprite_k(n, k)
        px, py = pivot(n)
        blit(frame, im, round(x - px * k), round(y - py * k))


def paw(frame: Image.Image, x: float, y: float, t_since_tap: Optional[float] = None,
        flip: Optional[bool] = None, ring: bool = True, k: int = HERO) -> Image.Image:
    """Tutorial paw (icon_hand_big at x8) with the fingertip exactly at (x, y).

    t_since_tap: time relative to the tap (None = idle hover). Before the tap the hand hovers 16 px
    down-right, steps in at -0.1 s, presses at 0..0.2 s (scale 0.9 around the fingertip + fx_ring burst),
    then releases. flip=None mirrors the hand automatically near the right edge (as the game does)."""
    if flip is None:
        flip = x > W - (sprite(HAND).width + 2) * k
    lift = paw_lift(t_since_tap)
    if ring and t_since_tap is not None and 0 <= t_since_tap < 0.3:
        _draw_ring(frame, x, y, t_since_tap)
    _draw_hand(frame, x, y, lift, flip, k, PRESS_SCALE if lift == 0 else 1.0)
    return frame


def paw_path(t: float, p0: Tuple[float, float], p1: Tuple[float, float], t0: float, t1: float
             ) -> Tuple[float, float]:
    """Paw position gliding from p0 (at t0) to p1 (at t1) with ease-in-out, snapped to 4 px."""
    u = ease_in_out((t - t0) / (t1 - t0)) if t1 > t0 else 1.0
    return snap(p0[0] + (p1[0] - p0[0]) * u), snap(p0[1] + (p1[1] - p0[1]) * u)


def burst_tap_times(t0: float, t1: float, rate: float = 8.0) -> List[float]:
    """Times of the individual taps of paw_tap_burst between t0 and t1 (for tap SFX)."""
    n = int(math.floor((t1 - t0) * rate + 1e-9))
    return [t0 + i / rate for i in range(n + 1) if t0 + i / rate < t1]


def paw_tap_burst(frame: Image.Image, x: float, y: float, t: float, rate: float = 8.0,
                  duration: Optional[float] = None, jitter: int = 12, flip: Optional[bool] = None,
                  ring: bool = True, k: int = HERO, seed: int = 0) -> Image.Image:
    """Frantic rapid tapping around (x, y): `rate` taps per second starting at t = 0 (tap i at i / rate).
    Each tap lands at a small deterministic jitter (multiples of 4 px) so it looks furious.
    After `duration` the paw goes back to hovering. Use burst_tap_times() for the tap sounds."""
    if flip is None:
        flip = x > W - (sprite(HAND).width + 2) * k
    if t < 0 or (duration is not None and t >= duration):
        _draw_hand(frame, x, y, 2, flip, k)
        return frame
    period = 1.0 / rate
    n = int(t // period)
    u = (t - n * period) / period
    jx = snap((_hash01(seed, n, 1) * 2 - 1) * jitter)
    jy = snap((_hash01(seed, n, 2) * 2 - 1) * jitter)
    tx, ty = x + jx, y + jy
    lift = 0 if u < 0.45 else (1 if u < 0.7 else 2)
    if ring:
        _draw_ring(frame, tx, ty, u * period)
    _draw_hand(frame, tx, ty, lift, flip, k, PRESS_SCALE if lift == 0 else 1.0)
    return frame


# ───────────────────────── 4. Counter ─────────────────────────
@lru_cache(maxsize=512)
def counter_static(text: str, icon: Optional[str] = 'icon_coin', k: int = UI, fs: int = 3, icon_k: int = HERO,
                   ramp: str = 'gold') -> Image.Image:
    """Big number label: [icon] + title text (fancy ramp, thick outline). Shared image."""
    word = up(fancy_text(text, fs=fs, ramp=ramp, outline='wood0', outline2='ink1', extrude=2), k)
    if not icon:
        return word
    ic = sprite_k(icon, icon_k)
    gap = 2 * k
    h = max(word.height, ic.height)
    out = Image.new('RGBA', (ic.width + gap + word.width, h))
    blit(out, ic, 0, (h - ic.height) // 2)
    blit(out, word, ic.width + gap, snap((h - word.height) / 2 + k, 2))
    return out


def counter(value: Union[float, str], t: float = 1.0, prefix: str = '+', icon: Optional[str] = 'icon_coin',
            k: int = UI, fs: int = 3, icon_k: int = HERO, ramp: str = 'gold',
            exit_at: Optional[float] = None) -> Image.Image:
    """'+1.2M' with a coin, number formatted like the game. Pops in at t = 0 (call again with t reset to 0
    whenever the value bumps for a fresh pop). Strings are used as-is (prefix still applied)."""
    text = prefix + (value if isinstance(value, str) else format_number(value))
    return animated(counter_static(text, icon, k, fs, icon_k, ramp), t, exit_at, s0=0.5, peak=1.15)


def draw_counter(frame: Image.Image, value: Union[float, str], t: float, cx: float = W / 2, cy: float = 600,
                 rise: float = 0.0, **kw) -> Image.Image:
    """counter() centred at (cx, cy); rise = px per second the label floats up (0 = static)."""
    img = counter(value, t, **kw)
    if img is not EMPTY:
        blit_center(frame, img, cx, cy - max(0.0, t) * rise)
    return frame


# ───────────────────────── 5. Split screen ─────────────────────────
def split_screen(top: Image.Image, bottom: Image.Image, y_div: int = H // 2, top_focus: float = 0.5,
                 bottom_focus: float = 0.5) -> Image.Image:
    """Two 1080x1920 frames -> one: the top half shows a band of `top` around top_focus (fraction of the
    source height), the bottom half a band of `bottom` around bottom_focus. Bands snap to 4 px."""
    out = Image.new('RGBA', (W, H), rgba('navy0'))
    for src, y0, y1, focus in ((top, 0, y_div, top_focus), (bottom, y_div, H, bottom_focus)):
        src = src.convert('RGBA')
        hh = y1 - y0
        sy = snap(min(max(0, focus * src.height - hh / 2), src.height - hh))
        out.paste(src.crop((0, sy, W, sy + hh)), (0, y0))
    return out


@lru_cache(maxsize=16)
def _divider(width_art: int = W // UI, height_art: int = 12) -> Image.Image:
    """Full-width wooden beam (ui_window_header 9-slice) with extra gold bracket bands copied from the
    sprite's own bracket columns (3..5), clear of the centre where the tags sit."""
    beam = nine('ui_window_header', width_art, height_art).copy()
    band = beam.crop((3, 0, 6, height_art))
    for x in (36, width_art - 39):
        beam.paste(band, (x, 0))
    return up(beam, UI)


@lru_cache(maxsize=64)
def split_tag(text: str, style: str = 'gold', icon: Optional[str] = None, k: int = 6) -> Image.Image:
    """Tag plate for split screens: button 9-slice + optional icon + bold label. Shared image."""
    st = CAPTION_STYLES[style]
    P = UI
    txt = pixel_text(text, st['ink'], k, shadow=st['shadow'])
    ic = sprite_k(icon, 6) if icon else None
    icon_w = (ic.width + 3 * P) if ic else 0
    vis_h = (CAP_H + (1 if st['shadow'] else 0)) * k
    pw = math.ceil((txt.width + icon_w) / P) + 2 * 8
    ph = max(math.ceil(vis_h / P) + 2 * 5, (math.ceil(ic.height / P) + 4) if ic else 0) + st['lip']
    plate = up(nine(st['panel'], pw, ph), P).copy()
    face_h = (ph - st['lip']) * P
    x = snap((plate.width - txt.width - icon_w) / 2, 2)
    if ic:
        blit(plate, ic, x, snap(face_h / 2 - ic.height / 2, 2))
        x += icon_w
    blit(plate, txt, x, snap(face_h / 2 - vis_h / 2, 2))
    return plate


def split_labels(frame: Image.Image, t: float = 1.0, top: str = 'LEVEL 1', bottom: str = 'LEVEL 100',
                 y_div: int = H // 2, top_style: str = 'blue', bottom_style: str = 'gold',
                 top_icon: Optional[str] = 'icon_cat', bottom_icon: Optional[str] = 'icon_item_hat_legendary',
                 k: int = 6) -> Image.Image:
    """Split-screen dressing: wooden divider beam at y_div + two tags hugging it (top tag above, bottom tag
    below). The beam slides in at t = 0, the top tag pops at 0.1 s, the bottom tag at 0.35 s (sparkles)."""
    beam = _divider()
    if t >= 0:
        u = ease_out(t / 0.18)
        bx = snap(-W * (1.0 - u))
        blit(frame, beam, bx, snap(y_div - beam.height / 2))
    tt = split_tag(top, top_style, top_icon, k)
    bt = split_tag(bottom, bottom_style, bottom_icon, k)
    gap = 4 * UI
    img = animated(tt, t - 0.1)
    if img is not EMPTY:
        blit_center(frame, img, W / 2, y_div - beam.height / 2 - gap - tt.height / 2)
    t2 = t - 0.35
    img = animated(bt, t2, s0=0.5, peak=1.15)
    if img is not EMPTY:
        cy = y_div + beam.height / 2 + gap + bt.height / 2
        blit_center(frame, img, W / 2, cy)
        _sparkles(frame, W / 2, cy, bt.width / 2 + 24, bt.height / 2 + 16, t2, n=6, seed=7)
    return frame


def _sparkles(frame: Image.Image, cx: float, cy: float, rx: float, ry: float, t: float, n: int = 6,
              seed: int = 0, k: int = UI, period: float = 0.9) -> None:
    """Twinkling fx_spark_* glints scattered on an ellipse around (cx, cy); each loops with its own phase."""
    if t < 0:
        return
    for i in range(n):
        a = 2 * math.pi * (i / n + _hash01(seed, i) * 0.15)
        ph = _hash01(seed, i, 3) * period
        u = ((t + ph) % period) / period
        fr = int(u * 6)
        if fr >= 4:
            continue
        name = f'fx_spark_{[0, 1, 2, 1][fr]}'
        x = cx + math.cos(a) * rx
        y = cy + math.sin(a) * ry
        blit_center(frame, sprite_k(name, k), x, y)


# ───────────────────────── 6. WRONG WAY and ZZZ ─────────────────────────
_dim_cache: Dict[tuple, Image.Image] = {}


def dim_frame(frame: Image.Image, desat: float = 0.85, dim: float = 0.5, tint: Color = 'ink0') -> Image.Image:
    """Desaturated, darkened copy of a frame (freeze-frame look). Cached by an exact content hash (crc32 of
    all pixels, ~3 ms), so a frozen frame re-read every video frame is processed once, and moving footage
    can never get a stale copy. Uncached cost ~10 ms (PIL blends, no float arrays)."""
    rgb = frame.convert('RGB')
    key = (frame.size, zlib.crc32(rgb.tobytes()), desat, dim, tint)
    hit = _dim_cache.get(key)
    if hit is not None:
        return hit
    gray = rgb.convert('L').convert('RGB')                 # ITU-R 601 luma, like the old float version
    out = Image.blend(rgb, gray, desat)
    out = Image.blend(out, Image.new('RGB', rgb.size, rgba(tint)[:3]), dim).convert('RGBA')
    if len(_dim_cache) > 4:
        _dim_cache.clear()
    _dim_cache[key] = out
    return out


@lru_cache(maxsize=1)
def x_stamp_art(n: int = 26, half_w: float = 3.0, ext: int = 2) -> Image.Image:
    """Big red 'fail' X at art px, drawn in the material of the game's icon_close (red6 light left edges,
    red4 body, red3 dark right edges, ink1 outline) at twice its resolution, plus the title-text treatment
    (red5 top bevel, red2 extruded underside). icon_close itself at x28 is too blocky for a hero stamp.
    n = stroke box in art px, half_w = half stroke width, ext = extrusion depth. Result: 30 x 32 art px."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    r2 = math.sqrt(2.0)
    cap = 2.2                                           # flat stroke ends, perpendicular to the stroke
    s1 = (np.abs(xx - yy) / r2 < half_w) & ((xx + yy) / r2 > cap) & ((xx + yy) / r2 < n * r2 - cap)
    s2 = (np.abs(xx + yy - n) / r2 < half_w) & ((xx - yy + n) / r2 > cap) & ((xx - yy + n) / r2 < n * r2 - cap)
    p = 2
    shape = (n + 2 * p + ext, n + 2 * p)
    body = np.zeros(shape, bool)
    body[p:p + n, p:p + n] = s1 | s2
    ex = np.zeros(shape, bool)
    for i in range(1, ext + 1):
        ex |= _shift(body, 0, i)
    ex &= ~body
    ol = _dilate(body | ex) & ~(body | ex)
    return _paint(shape, [(ol, 'ink1'), (ex, 'red2'), (body, 'red4'),
                          (body & ~_shift(body, -1, 0), 'red3'),      # right edges
                          (body & ~_shift(body, 0, 1), 'red5'),       # top edges
                          (body & ~_shift(body, 1, 0), 'red6')])      # left edges


def big_x(k: int = 14) -> Image.Image:
    """The fail-stamp X (x_stamp_art) scaled x k (k = 14 -> 420 x 448 px, ~39 % of the frame width)."""
    return up(x_stamp_art(), k)


def wrong_way(frame: Image.Image, t: float, text: str = 'WRONG WAY!', cy_x: int = 700, cy_banner: int = 960,
              k: int = 8, x_k: int = 14) -> Image.Image:
    """Freeze-frame fail beat on a copy of `frame` (t = seconds since the freeze):
    instant desaturate + dim, red flash, giant red X (x_k = 14 -> 420 x 448 px) slams in with screen shake and
    an impact burst, red banner (k = 8 -> 56 px caps) pops at 0.12 s and overlaps the X's feet like a stamp
    lock-up (defaults: X 476..924, banner 904..1016)."""
    dx, dy = shake_offset(t, dur=0.35, amp=20, seed=3)
    out = dim_frame(frame).copy()
    if 0 <= t < 0.18:                                     # short red hit flash
        a = int(150 * (1 - t / 0.18))
        out.alpha_composite(Image.new('RGBA', out.size, rgba('red3', a)))
    if t >= 0:
        s = 1.0
        if t < 0.16:                                      # slam: big -> slightly small -> 1.0
            s = 2.2 - 1.3 * ease_out(t / 0.1) if t < 0.1 else 0.9 + 0.1 * ease_in_out((t - 0.1) / 0.06)
        x = scale_about(big_x(x_k), s)
        blit_center(out, x, W / 2 + dx, cy_x + dy)
        if 0.08 <= t < 0.38:                              # impact stars burst out of the X as it lands
            _impact_burst(out, W / 2 + dx, cy_x + dy, t - 0.08, x.width * 0.5)
    img = caption(text, t - 0.12, style='red', k=k, icon=False)
    if img is not EMPTY:
        blit_center(out, img, W / 2 + dx, cy_banner + dy)
    return out


def _impact_burst(frame: Image.Image, cx: float, cy: float, t: float, r0: float, n: int = 8,
                  dur: float = 0.3, k: int = HERO) -> None:
    """n white fx_hit stars flying out from radius r0 around (cx, cy) over dur s (game hit frames 0-2)."""
    if t < 0 or t >= dur:
        return
    u = t / dur
    fr = min(2, int(u * 3))
    im = sprite_k(f'fx_hit_{fr}', k)
    r = r0 * (0.75 + 0.45 * ease_out(u))
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        blit_center(frame, im, cx + math.cos(a) * r, cy + math.sin(a) * r)


@lru_cache(maxsize=8)
def _z_letter(fs: int) -> Image.Image:
    """Sleepy Z: cream title letter (fs art px per font px) with a thin navy0 outline, x4."""
    return up(fancy_text('Z', fs=fs, ramp='cream', outline='navy0', extrude=1), UI)


def zzz(frame: Image.Image, t: float, origin: Tuple[float, float] = (400, 960), fade: float = 0.6,
        max_dim: float = 0.78, every: float = BEAT, life: float = BAR, travel: Tuple[float, float] = (280, 640),
        color: Color = 'navy0') -> Image.Image:
    """'Close the game...' sleep beat on a copy of `frame`: the screen fades to dark navy over `fade` s
    (dimming, up to max_dim) and a Z pops at `origin` on every beat (first one on beat 1), drifting up-right
    by `travel` px over `life` s while growing 72 -> 100 -> 128 -> 156 px on the beats (integer steps only),
    wobbling sideways, then popping out. Consecutive Zs are travel_y * every / life = 160 px apart, so even
    the two biggest never overlap."""
    out = frame.convert('RGBA').copy()
    if t < 0:
        return out
    a = int(255 * max_dim * ease_in_out(t / fade))
    out.alpha_composite(Image.new('RGBA', out.size, rgba(color, a)))
    n_max = int(t / every)
    for i in range(max(0, n_max - int(life / every) - 2), n_max + 1):
        age = t - (i + 1) * every                         # Z number i pops on beat i + 1
        if age < 0 or age >= life:
            continue
        u = age / life
        img = _z_letter((2, 3, 4, 5)[min(3, int(u * 4 + 1e-6))])
        if age < 0.15:
            img = scale_about(img, pop_scale(age, 0.15))
        elif u > 0.9:
            img = scale_about(img, 1.0 - (u - 0.9) / 0.1 * 0.9)
        x = origin[0] + travel[0] * u + 28 * math.sin(age * 5.0 + i)
        y = origin[1] - travel[1] * u
        blit_center(out, img, x, y)
    return out


# ───────────────────────── Cats ─────────────────────────
CAT_FEET = (20, 36)              # feet pivot on the 40x40 cat canvas (top-down)
# Game timings (OutpostCatView.cs): idle 0.22 s per frame; joy/spawn frames with a vertical lift (art px).
IDLE_STEP = 0.22
JOY_TIMES = (0.06, 0.06, 0.1, 0.07, 0.25, 0.15)
JOY_LIFT = (0, 3, 6, 0, 1, 0)
SPAWN_TIMES = (0.08, 0.06, 0.1, 0.08, 0.08, 0.12, 0.18, 0.25)
SPAWN_LIFT = (0, 3, 7, 4, 0, 0, 0, 0)


@lru_cache(maxsize=1024)
def cat_sprite(fur: str = 'ginger', frame: str = 'idle0', gear: str = 'base', hat: Optional[str] = None,
               pick: Optional[str] = None, load: int = 0) -> Image.Image:
    """Cat in team equipment on its 40x40 art canvas (feet at CAT_FEET). gear = rarity for boots/pack;
    hat/pick default to gear. Layer order as OutpostCatView. Shared image."""
    hat = hat or gear
    pick = pick or gear
    c = Image.new('RGBA', (40, 40))
    names = [f'cat_body_{fur}_{frame}', f'cat_boots_{gear}_{frame}', f'cat_pack_{gear}_{frame}']
    if load:
        names.append(f'cat_load_{load}_{frame}')
    names += [f'cat_hat_{hat}_{frame}', f'cat_pick_{pick}_{frame}', f'cat_sack_{frame}']
    for n in names:
        if has_sprite(n):
            place(c, n, *CAT_FEET)
    return c


def _step(times: Sequence[float], t: float) -> int:
    """Index of the frame shown at t for a one-shot animation with per-frame durations; -1 when over."""
    acc = 0.0
    for i, d in enumerate(times):
        acc += d
        if t < acc:
            return i
    return -1


def cat_pose(t: float, spawn_at: Optional[float] = None, joys: Sequence[float] = (), phase: float = 0.0
             ) -> Tuple[Optional[str], int]:
    """(frame name, lift art px) of a cat at time t: spawn one-shot, joy one-shots at `joys`, idle otherwise.
    Frame None = not spawned yet."""
    if spawn_at is not None:
        if t < spawn_at:
            return None, 0
        i = _step(SPAWN_TIMES, t - spawn_at)
        if i >= 0:
            return f'spawn{i}', SPAWN_LIFT[i]
    for j in sorted(joys, reverse=True):
        if t >= j:
            i = _step(JOY_TIMES, t - j)
            if i >= 0:
                return f'joy{i}', JOY_LIFT[i]
            break
    n = int((t + phase) / IDLE_STEP)
    blink = _hash01(phase, n // 14) < 0.5 and n % 14 == 7
    return ('blink' if blink else f'idle{n % 6}'), 0


# ───────────────────────── 7. Logo ─────────────────────────
@lru_cache(maxsize=1)
def logo_art() -> Image.Image:
    """'CAT MINERS' logo at art px (scale with logo(k)). Composition per art.md section 3:
    Pixeloid Bold letters (2 art px per font px) in the gold-button ramp with a wood0 outline, extruded
    amber underside and ink1 drop shadow; 'MINERS' on a gold-bracketed wooden plank, 'CAT' above it with
    crossed legendary pickaxes; two miner cats sit on the plank ends; coin, gem and sparkle accents."""
    cw, ch = 140, 96
    c = Image.new('RGBA', (cw, ch))
    cat_w = fancy_text('CAT', fs=2, outline2='ink1')
    min_w = fancy_text('MINERS', fs=2, outline2='ink1')
    mid = cw // 2
    pl_w, pl_h = min_w.width + 12, min_w.height + 6        # plank around 'MINERS'
    pl_x, pl_y = mid - pl_w // 2, 60
    cat_y = pl_y - cat_w.height + 5                           # 'CAT' overlaps the plank's top edge
    # Crossed legendary pickaxes behind 'CAT': an X centred on the word, heads up and out.
    pk = 'icon_item_pickaxe_legendary_big'
    place(c, pk, mid + 8, cat_y - 4)
    place(c, pk, mid - 8, cat_y - 4, flip=True)
    blit(c, nine('ui_window_header', pl_w, pl_h), pl_x, pl_y)
    # Cats sitting on the plank ends, facing the title.
    left = cat_sprite('gray', 'idle0', 'epic')
    right = cat_sprite('ginger', 'idle0', 'legendary').transpose(FLIP)
    blit(c, left, pl_x + 8 - CAT_FEET[0], pl_y + 2 - CAT_FEET[1])
    blit(c, right, pl_x + pl_w - 8 - CAT_FEET[0], pl_y + 2 - CAT_FEET[1])
    blit(c, min_w, mid - min_w.width // 2, pl_y + (pl_h - min_w.height) // 2 + 1)
    blit(c, cat_w, mid - cat_w.width // 2, cat_y)
    # Accents: coin and gem hanging on the plank's lower corners, glints on the corners of 'CAT' and on the
    # coin. (No glint on the 'S' of MINERS: its vertical stroke turned the S into a '$'.)
    place(c, 'icon_coin', pl_x + 5, pl_y + pl_h - 1)
    place(c, 'icon_gem', pl_x + pl_w - 5, pl_y + pl_h - 1)
    place(c, 'fx_spark_2', mid - cat_w.width // 2 + 4, cat_y + 3)
    place(c, 'fx_spark_1', mid - min_w.width // 2 + 3, pl_y + pl_h - 7)
    place(c, 'fx_spark_1', mid + cat_w.width // 2 - 4, cat_y + 3)
    bbox = c.getbbox()
    return c.crop(bbox) if bbox else c


LOGO_K = 6                       # 124 x 71 art px x 6 = 744 x 426 px; even factor = no chroma smear in yuv420


@lru_cache(maxsize=8)
def logo(k: int = LOGO_K) -> Image.Image:
    """'CAT MINERS' logo scaled x k (k = 6 -> 744 x 426 px). Shared image."""
    return up(logo_art(), k)


def draw_logo(frame: Image.Image, t: float, cx: float = W / 2, cy: float = 500, k: int = LOGO_K) -> Image.Image:
    """Logo centred at (cx, cy) with a pop-in at t = 0 (0.5 -> 1.1 -> 1.0 over 0.22 s)."""
    img = animated(logo(k), t, s0=0.5, peak=1.1, dur=0.22)
    if img is not EMPTY:
        blit_center(frame, img, cx, cy)
    return frame


# ───────────────────────── 8. End card ─────────────────────────
# Layout (all inside the Reels safe zone y 250..1350, x 64..1016):
#   logo 268..694 | tagline plate (k = 6, up to 2 lines) 720..876 | PLAY FREE 906..1050 | disclosure ~1112
#   (compose.py) | cats ~1200..1420 (decoration, may sit under the Reels interface)
END_TAPS = (1.6, 3.0)                     # paw taps on PLAY FREE (end-card seconds)
END_LOGO_Y = 481                          # logo top-left snaps to y = 268
END_TAGLINE_Y = 798
END_CATS_FEET_Y = 1420                     # decorative row under the call to action (may sit under Reels UI)
END_BUTTON_Y = 978                        # button 906..1050 (898..1058 at the peak of the beat throb)
END_BUTTON_ART = (172, 36)                # button size in art px (x4 = 688 x 144 px)
END_CAT_K = LOGO_K                        # same pixel size as the cats on the logo
# (fur, gear rarity, feet x, feet y offset, facing left); drawn in this order (back to front). Three cats
# with clear gaps (38 art px wide each = 228 px): the classic yellow-helmet miner seen in the footage,
# a blue rare cat and the crowned legendary hero in front.
END_CATS = (
    ('ginger', 'base', 270, 0, False),
    ('white', 'rare', 810, 0, True),
    ('black', 'legendary', 540, 8, False),
)
END_TAGLINE = 'How deep can *YOUR* cats dig?'


@lru_cache(maxsize=1)
def _bg_maps() -> Tuple[np.ndarray, np.ndarray]:
    """Static maps of the end-card background at UI art px (270 x 480): angle around the logo centre and
    light level (0 = navy0 edge, 1 = navy1 ring, 2 = navy2 core) with ordered-dithered ring borders."""
    h, w = H // UI, W // UI
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = xx + 0.5 - w / 2, yy + 0.5 - END_LOGO_Y / UI
    ang = np.arctan2(dy, dx) % (2 * np.pi)
    bay = np.tile(_BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w]
    r = np.hypot(dx, dy) + (bay - 0.5) * 10.0          # 10 art px dither band at each ring border
    level = (r < 150).astype(np.uint8) + (r < 92).astype(np.uint8)
    return ang.astype(np.float32), level


_BG_LUT = np.array([PAL[c] for c in ('navy0', 'navy1', 'navy1', 'navy2', 'navy2', 'navy3')], np.uint8)


def _background(t: float, n: int = 12, speed: float = 10.0) -> Image.Image:
    """End-card backdrop at x4: lit navy core behind the logo, darker rings outwards (dithered borders),
    n sunburst wedges one tone lighter, turning at `speed` degrees per second. Palette colours only."""
    ang, level = _bg_maps()
    per = 2 * np.pi / n
    a0 = math.radians((round(t * speed * 4) / 4.0) % 360)   # quantised to 1/4 degree
    wedge = ((ang - a0) % per) < per / 2
    art = _BG_LUT[level * 2 + wedge]
    return Image.fromarray(art, 'RGB').resize((W, H), Image.NEAREST).convert('RGBA')


@lru_cache(maxsize=16)
def _button_art(text: str, w: int, h: int, pressed: bool) -> Image.Image:
    """Gold CTA button at art px with its label (wood0, Bold, fs = 2 art px per font px, no shadow).
    The label is centred on the face above the 2 px lip; pressed = darker sprite, label 1 px lower."""
    name = 'ui_btn_gold_pressed' if pressed else 'ui_btn_gold'
    b = nine(name, w, h).copy()
    ink, _ = _block([text], True)
    m = np.kron(ink, np.ones((2, 2), bool))
    lab = _paint(m.shape, [(m, 'wood0')])
    lip = 2
    blit(b, lab, (w - lab.width) // 2, (h - lip - CAP_H * 2) // 2 - 1 + (1 if pressed else 0))
    return b


def _button_shine(img: Image.Image, u: float) -> Image.Image:
    """Diagonal pixel shine band (gold5) sweeping across the button face at art px, u in [0, 1]."""
    a = np.array(img)
    h, w = a.shape[:2]
    face = np.all(a[..., :3] == PAL['amber3'], axis=-1) | np.all(a[..., :3] == PAL['gold3'], axis=-1)
    yy, xx = np.mgrid[0:h, 0:w]
    pos = -h + (w + 2 * h) * u
    band = (xx + yy * 0.6 - pos)
    sel = face & (band >= 0) & (band < 3)
    sel2 = face & (band >= 4) & (band < 5)
    a[sel, :3] = PAL['gold5']
    a[sel2, :3] = PAL['gold4']
    return Image.fromarray(a, 'RGBA')


def button_throb(t: float) -> int:
    """Beat throb of the CTA in art px per side: 0, 1, 2, 2, 1, 0 over the first 0.2 s of every beat."""
    bt = t % BEAT
    if bt >= 0.2:
        return 0
    return int(round(2 * math.sin(math.pi * bt / 0.2)))


def play_button(t: float, text: str = 'PLAY FREE', taps: Sequence[float] = END_TAPS,
                w: int = END_BUTTON_ART[0], h: int = END_BUTTON_ART[1], k: int = UI) -> Image.Image:
    """Animated PLAY FREE button (x4): pops in at t = 0, throbs on every beat, shine sweep every bar,
    pressed sprite for 0.15 s after each tap in `taps` (times relative to the same clock as t).
    The throb re-slices the 9-slice 1-2 art px bigger on each side instead of scaling the image, so the
    label never leaves the pixel grid (only the 0.22 s pop-in uses a non-integer scale)."""
    if t < 0:
        return EMPTY
    pressed = any(0 <= t - tp < 0.15 for tp in taps)
    g = button_throb(t) if t >= 0.22 and not pressed else 0
    art = _button_art(text, w + 2 * g, h + 2 * g, pressed)
    su = ((t - 0.3) % BAR) / 0.6
    if 0 <= su < 1 and t > 0.3:
        art = _button_shine(art, su)
    img = up(art, k)
    if t < 0.22:
        return scale_about(img, pop_scale(t, 0.22, 0.5, 1.12))
    return img


def button_tip(text: str = 'PLAY FREE') -> Tuple[int, int]:
    """Where the end-card paw taps: on the button face right of the label, so the label stays readable."""
    label_w = _line(text, True)[0].shape[1] * 2 * UI
    return snap(W / 2 + label_w / 2 + 44), END_BUTTON_Y + 12


@lru_cache(maxsize=1)
def _twinkle_points(n: int = 26) -> List[Tuple[int, int, float]]:
    pts = []
    i = 0
    while len(pts) < n:
        x = snap(SAFE_X / 2 + _hash01(11, i, 1) * (W - SAFE_X))
        y = snap(120 + _hash01(11, i, 2) * 1640)
        i += 1
        if 220 < y < 1380 and abs(x - W / 2) < 420:   # keep clear of the logo/tagline/cats/button stack
            continue
        pts.append((x, y, _hash01(11, i, 3)))
    return pts


def end_card(t: float, tagline: str = END_TAGLINE, button: str = 'PLAY FREE',
             taps: Sequence[float] = END_TAPS, cats: Sequence[tuple] = END_CATS, flash_at_start: bool = True,
             paw_from: Tuple[int, int] = (1180, 1720)) -> Image.Image:
    """Full 1080x1920 end-card frame for the last 3.75 s (t = seconds since the end card started).

    Timeline: 0.00 logo pops with a white downbeat flash and sparkles; cats spawn 0.15-0.33 and then idle
    (game frame timings) with one cheer each on their own beat; tagline pops at 1 beat (0.47 s); PLAY FREE
    pops at 2 beats (0.94 s), throbs on every beat and gets a shine sweep every bar; the paw glides in from
    the bottom-right (1.10-1.48 s), taps the button right of its label at `taps` (1.6 s and 3.0 s; button
    pressed 0.15 s, sparkle burst) and hovers in between; all cats cheer right after each tap.
    taps=() -> no paw. Tagline: markup allowed ('*YOUR*' = gold), keep it to 2 lines (<= ~45 characters).
    Z-order: background, logo, cats, tagline, button, paw, flash (text is never covered by a jumping cat).
    Layout: logo 268-694, tagline 720-876, button 906-1050 (Reels safe zone); cats ~1200-1420 below."""
    t = max(0.0, t)
    frame = _background(t)
    for x, y, ph in _twinkle_points():                    # background twinkles
        u = ((t + ph * 2.0) % 2.0) / 2.0
        fr = int(u * 10)
        if fr < 4:
            blit_center(frame, sprite_k(f'fx_spark_{[0, 1, 2, 1][fr]}', UI), x, y)
    draw_logo(frame, t, W / 2, END_LOGO_Y)
    if t < 0.6:
        _sparkles(frame, W / 2, END_LOGO_Y, 420, 200, t, n=10, seed=5, k=HERO, period=0.6)
    # Cats: spawn one by one, idle, cheer on their own beat and right after every paw tap.
    k = END_CAT_K
    for i, (fur, gear, x, dy, face_left) in enumerate(cats):
        spawn_at = 0.15 + 0.09 * i
        joys = [tp + 0.05 for tp in taps] + [spawn_at + 1.1 + BEAT * ((i * 3) % 5)]
        fr, lift = cat_pose(t, spawn_at, joys, phase=i * 0.37)
        if fr is None:
            continue
        fy = END_CATS_FEET_Y + dy
        blit_center(frame, sprite_k('shadow_m', k), x, fy)
        feet_x = (40 - CAT_FEET[0]) if face_left else CAT_FEET[0]
        blit(frame, sprite_k_cat(fur, fr, gear, face_left, k), snap(x - feet_x * k),
             snap(fy - CAT_FEET[1] * k) - lift * k)
    draw_caption(frame, tagline, t - BEAT, W / 2, END_TAGLINE_Y, style='plank', k=CAPTION_K, width_max=CAPTION_W)
    # Button.
    bimg = play_button(t - 2 * BEAT, button, [tp - 2 * BEAT for tp in taps])
    if bimg is not EMPTY:
        blit_center(frame, bimg, W / 2, END_BUTTON_Y)
        for tp in taps:
            if 0 <= t - tp < 0.6:
                _sparkles(frame, W / 2, END_BUTTON_Y, 400, 110, t - tp, n=8, seed=int(tp * 10), k=HERO,
                          period=0.6)
    # Paw: glides in, taps, hovers (lifted), taps again.
    if taps:
        tip = button_tip(button)
        t_in = taps[0] - 0.5
        if t >= t_in:
            if t < taps[0] - 0.12:
                px, py = paw_path(t, paw_from, tip, t_in, taps[0] - 0.12)
            else:
                px, py = tip
            near = min(taps, key=lambda tp: abs(t - tp))
            paw(frame, px, py, t - near, flip=False)
    if flash_at_start:
        flash(frame, t, color='cream4', dur=0.22, peak=0.85)
    return frame


@lru_cache(maxsize=512)
def sprite_k_cat(fur: str, frame: str, gear: str, flip: bool, k: int = HERO) -> Image.Image:
    img = up(cat_sprite(fur, frame, gear), k)
    return img.transpose(FLIP) if flip else img


# ───────────────────────── 9. Flash and shake ─────────────────────────
_BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) / 16.0


@lru_cache(maxsize=1)
def _bayer_screen(cell: int = UI) -> np.ndarray:
    """Ordered-dither threshold for the whole frame at UI art px (4x4 Bayer tiled, cell px per art px)."""
    tw, th = W // cell, H // cell
    tile = np.tile(_BAYER4, (th // 4 + 1, tw // 4 + 1))[:th, :tw]
    return np.kron(tile, np.ones((cell, cell), np.float32))


def flash(frame: Image.Image, t: float, color: Color = 'cream4', dur: float = 0.25, peak: float = 0.9,
          mode: str = 'alpha') -> Image.Image:
    """Transition flash on the frame at t = 0 decaying to nothing at dur. color: 'cream4' (white) or
    'gold4'/'gold5' (gold). mode='alpha' = smooth glow (recommended), 'dither' = pixel-art ordered-dither
    dissolve (a full-frame 4 px checker is the worst case for H.264: use it only for a few frames)."""
    if t < 0 or t >= dur:
        return frame
    level = peak * (1.0 - t / dur) ** 2
    if level <= 0:
        return frame
    if mode == 'dither':
        m = _bayer_screen() < level
        a = (m * 255).astype(np.uint8)
        over = Image.new('RGBA', frame.size, rgba(color))
        over.putalpha(Image.fromarray(a, 'L'))
    else:
        over = Image.new('RGBA', frame.size, rgba(color, int(255 * level)))
    frame.alpha_composite(over)
    return frame


def shake_offset(t: float, dur: float = 0.3, amp: float = 24, freq: float = 28.0, seed: int = 0
                 ) -> Tuple[int, int]:
    """Decaying camera shake (dx, dy), multiples of 4 px; (0, 0) outside 0 <= t < dur."""
    if t < 0 or t >= dur:
        return 0, 0
    decay = (1.0 - t / dur) ** 2
    n = int(t * freq)
    dx = (_hash01(seed, n, 1) * 2 - 1) * amp * decay
    dy = (_hash01(seed, n, 2) * 2 - 1) * amp * decay
    return snap(dx), snap(dy)


def apply_shake(frame: Image.Image, dx: int, dy: int, fill: Color = 'navy0') -> Image.Image:
    """New frame shifted by (dx, dy); uncovered edges filled with `fill`."""
    if dx == 0 and dy == 0:
        return frame
    out = Image.new('RGBA', frame.size, rgba(fill))
    out.paste(frame.convert('RGBA'), (int(dx), int(dy)))
    return out


__all__ = [
    'W', 'H', 'UI', 'HERO', 'SAFE_TOP', 'SAFE_BOTTOM', 'SAFE_X', 'BEAT', 'BAR', 'PAL', 'EMPTY',
    'rgba', 'snap', 'sprite', 'sprite_k', 'nine', 'up', 'place', 'blit', 'blit_center', 'recolor',
    'pixel_text', 'rich_text', 'fancy_text', 'wrap_text', 'wrap_balanced', 'format_number', 'roll_value',
    'caption', 'caption_static', 'draw_caption', 'CAPTION_STYLES',
    'paw', 'paw_path', 'paw_tap_burst', 'burst_tap_times', 'paw_lift',
    'counter', 'counter_static', 'draw_counter',
    'split_screen', 'split_labels', 'split_tag',
    'dim_frame', 'big_x', 'x_stamp_art', 'wrong_way', 'zzz',
    'cat_sprite', 'cat_pose', 'logo', 'logo_art', 'draw_logo', 'play_button', 'button_throb', 'button_tip',
    'end_card', 'END_TAGLINE', 'CAPTION_K', 'CAPTION_W', 'LOGO_K',
    'flash', 'shake_offset', 'apply_shake', 'pop_scale', 'exit_scale', 'animated',
]
