"""Self-test and preview sheets for Tools/AdVideo/overlays.py.

Renders PNG previews of every overlay on real game screenshots into Temp/ads/overlay_preview/ (hub-only
scratch, not synced), checks palette compliance of the static overlays, and times the per-frame cost.

    Tools/.venv/bin/python -B Tools/AdVideo/overlay_preview.py            # everything
    Tools/.venv/bin/python -B Tools/AdVideo/overlay_preview.py logo end   # only some sheets

Sheets: captions, pop, paw, split, wrong, zzz, counter, logo, end, button, flash, contact
(contact = thumbnails of all PNGs; it runs last). Extra argument 'check' runs the checks only:
palette/binary alpha of static overlays, x4 text == size-36 render, paw fingertip position (black-box,
pressed / hovering / mirrored), safe-zone layout of the end card, WRONG WAY and all 30 script captions at the
default caption size, and per-frame timing (end card over all 113 frames at 30 fps, caption first frame).
"""
from __future__ import annotations

import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import overlays as ov  # noqa: E402

ROOT = ov.ROOT
OUT = ROOT / 'Temp' / 'ads' / 'overlay_preview'
BG_HUD = ROOT / 'Docs' / 'Reports' / 'english-ui' / '01-hud.png'
BG_MINE = ROOT / 'Docs' / 'Reports' / 'topology-wide' / 'screenshots' / 'mine-048-active.png'
LABEL = (240, 230, 210, 255)


def bg(path: Path) -> Image.Image:
    return Image.open(path).convert('RGBA')


def save(img: Image.Image, name: str) -> Path:
    OUT.mkdir(parents=True, exist_ok=True)
    p = OUT / name
    img.save(p)
    print('  wrote', p.relative_to(ROOT), img.size)
    return p


def safe_guides(frame: Image.Image) -> Image.Image:
    """Thin magenta lines at the Reels safe zone (y 250 / 1350, x 64 / 1016) for layout checks."""
    f = frame.copy()
    d = ImageDraw.Draw(f)
    for y in (ov.SAFE_TOP, ov.SAFE_BOTTOM):
        d.line([(0, y), (ov.W, y)], fill=(255, 0, 255, 255), width=2)
    for x in (ov.SAFE_X, ov.W - ov.SAFE_X):
        d.line([(x, 0), (x, ov.H)], fill=(255, 0, 255, 255), width=2)
    return f


def strip(frames, labels, scale: float = 1.0, pad: int = 8) -> Image.Image:
    """Horizontal strip of equally sized images with captions under them."""
    fw, fh = frames[0].size
    fw, fh = int(fw * scale), int(fh * scale)
    sh = Image.new('RGBA', (len(frames) * (fw + pad) + pad, fh + 30 + pad), (20, 22, 36, 255))
    d = ImageDraw.Draw(sh)
    for i, (f, lab) in enumerate(zip(frames, labels)):
        im = f if scale == 1.0 else f.resize((fw, fh), Image.LANCZOS)
        sh.alpha_composite(im, (pad + i * (fw + pad), pad))
        d.text((pad + i * (fw + pad) + 4, pad + fh + 6), lab, fill=LABEL)
    return sh


# ───────────────────────── sheets ─────────────────────────
CAPTIONS = [                       # (style, text, k); k = None -> default CAPTION_K (6 = 42 px caps)
    ('plank', 'It starts with *2 cats*...', None),
    ('toast', 'Mine *100*: *1.2M* gold', None),
    ('bubble', 'Tap to help them dig!', None),
    ('red', 'WRONG WAY!', None),
    ('green', 'Let the cats do it!', None),
    ('plank', 'How deep can *YOUR* cats dig? Collect all *61* cat cards', None),
    ('gold', 'New cats to collect', None),
    ('dark', 'Secondary line at k = 5 (35 px caps)', 5),
]

# Every caption line of Docs/Marketing/AD_SCRIPTS.md (for the layout check).
SCRIPT_CAPTIONS = [
    'It starts with 2 cats…', 'Tap to help them dig!', 'Hire more cats', 'Upgrade everything',
    'Break into new tunnels', 'Forge legendary gear', 'Mine cleared!', 'Mine 100: 1.2M gold', 'New cats to collect',
    'How deep can YOUR cats dig?', 'Mining by hand be like…', 'WRONG WAY!', 'Let the cats do it!',
    'They carry the gold home', 'You just upgrade', 'Close the game…', '…they keep digging!',
    'Skip time, get gold', 'Your cats never stop', 'No tapping needed', 'Opening a MYTHICAL chest…',
    '6 NEW CATS!', 'Level them up', 'Collect all 61 cat cards', 'Chests hide in every mine', 'NEW card!',
    'Cards make cats stronger', 'Which chest is next?', 'Lucky!', 'Which cat will you get?',
]


def sheet_captions():
    for name, path in (('captions_hud.png', BG_HUD), ('captions_mine.png', BG_MINE)):
        f = bg(path)
        y = 300
        for style, text, k in CAPTIONS:
            img = ov.caption(text, 1.0, style=style, k=k or ov.CAPTION_K)
            ov.blit_center(f, img, ov.W / 2, y + img.height / 2)
            y += img.height + 28
        save(safe_guides(f), name)
    # 1:1 crop to check pixel crispness.
    f = bg(BG_MINE)
    ov.draw_caption(f, 'Mine *100*: *1.2M* gold', 1.0, cy=300, style='toast')
    ov.draw_caption(f, 'Let the cats do it!', 1.0, cy=480, style='green')
    save(f.crop((40, 200, 1040, 600)), 'captions_crop_1to1.png')


def sheet_pop():
    f0 = bg(BG_HUD).crop((0, 200, 1080, 600))
    ts = [0.0, 0.04, 0.08, 0.12, 0.16, 0.2, 1.0, 1.05, 1.1, 1.2]
    frames = []
    for t in ts:
        f = f0.copy()
        img = ov.caption('Hire more cats', t, style='plank', exit_at=1.0)
        if img is not ov.EMPTY:
            ov.blit_center(f, img, 540, 200)
        frames.append(f)
    save(strip(frames, [f't={t:.2f}' for t in ts], scale=0.3), 'caption_pop_strip.png')


def sheet_paw():
    base = bg(BG_HUD)
    tip = (536, 1100)
    ts = [-0.4, -0.05, 0.0, 0.08, 0.16, 0.24, 0.35]
    frames, labels = [], []
    for t in ts:
        f = base.copy()
        ov.paw(f, *tip, t)
        c = f.crop((tip[0] - 120, tip[1] - 120, tip[0] + 260, tip[1] + 260))
        d = ImageDraw.Draw(c)
        d.line([(120 - 10, 120), (120 + 10, 120)], fill=(255, 0, 255, 255))   # target cross
        d.line([(120, 120 - 10), (120, 120 + 10)], fill=(255, 0, 255, 255))
        frames.append(c)
        labels.append(f't={t:+.2f}')
    # Rapid tapping burst (8 taps/s) frames.
    for t in (0.0, 0.03, 0.06, 0.09, 0.13, 0.19):
        f = base.copy()
        ov.paw_tap_burst(f, *tip, t, rate=8)
        frames.append(f.crop((tip[0] - 120, tip[1] - 120, tip[0] + 260, tip[1] + 260)))
        labels.append(f'burst {t:.2f}')
    # Flipped near the right edge.
    f = base.copy()
    ov.paw(f, 1000, 1100, 0.05)
    frames.append(f.crop((700, 980, 1080, 1360)))
    labels.append('auto-flip x=1000')
    save(strip(frames[:7], labels[:7]), 'paw_strip.png')
    save(strip(frames[7:], labels[7:]), 'paw_burst_strip.png')


def sheet_split():
    top = bg(BG_HUD)
    bottom = bg(BG_MINE)
    for t in (0.06, 0.2, 0.45, 1.0):
        f = ov.split_screen(top, bottom, top_focus=0.4, bottom_focus=0.55)
        ov.split_labels(f, t)
        if t == 1.0:
            save(safe_guides(f), 'split_t1.00.png')
            save(f.crop((0, 700, 1080, 1220)), 'split_crop_1to1.png')
        else:
            save(f.resize((540, 960), Image.LANCZOS), f'split_t{t:.2f}_half.png')


def sheet_wrong():
    frames = []
    ts = [0.03, 0.08, 0.15, 0.3, 1.0]
    for t in ts:
        frames.append(ov.wrong_way(bg(BG_HUD), t))
    save(safe_guides(frames[-1]), 'wrong_way_t1.00.png')
    save(strip(frames, [f't={t:.2f}' for t in ts], scale=0.3), 'wrong_way_strip.png')


def sheet_zzz():
    ts = [0.2, 0.6, 1.2, 2.0, 3.0]
    frames = [ov.zzz(bg(BG_MINE), t) for t in ts]
    save(frames[3], 'zzz_t2.00.png')
    save(strip(frames, [f't={t:.2f}' for t in ts], scale=0.3), 'zzz_strip.png')


def sheet_counter():
    f = bg(BG_MINE)
    y = 330
    for v, t in ((1.2e6, 1.0), (65800, 1.0), (999, 1.0), (4.56e9, 1.0), (1.2e6, 0.05), (1.2e6, 0.12)):
        img = ov.counter(v, t)
        ov.blit_center(f, img, 540, y)
        y += 170
    ov.blit_center(f, ov.counter(250, 1.0, icon='icon_gem', ramp='teal'), 540, y)
    save(safe_guides(f), 'counter.png')
    save(f.crop((140, 250, 940, 600)), 'counter_crop_1to1.png')
    print('  format_number:', [ov.format_number(v) for v in (0, 7, 12.34, 999, 1000, 1234, 12345, 123456,
                                                             1.2e6, 999999, 4.56e9, 1e12, 1.5e15, 2.5e17)])


def sheet_logo():
    lg = ov.logo()
    pad = 64
    f = Image.new('RGBA', (lg.width + 2 * pad, lg.height + 2 * pad), ov.rgba('navy0'))
    f.alpha_composite(lg, (pad, pad))
    print('  logo size', lg.size, 'art', ov.logo_art().size)
    save(f, 'logo_1to1.png')


def sheet_end():
    ts = [0.0, 0.1, 0.6, 1.0, 1.6, 1.65, 3.0, 3.7]
    frames = []
    for t in ts:
        fr = ov.end_card(t)
        frames.append(fr)
        if t in (0.1, 0.6, 1.6, 3.0):
            save(fr, f'end_card_t{t:.2f}.png')
    save(safe_guides(frames[-1]), 'end_card_t3.70_guides.png')
    save(frames[-1].crop((0, 960, 1080, 1440)), 'end_card_crop_1to1.png')
    save(strip(frames, [f't={t:.2f}' for t in ts], scale=0.25), 'end_card_strip.png')
    # Other taglines from the scripts (ads 2 and 3) and no paw (taps=()).
    alt = [ov.end_card(3.7, 'No *tapping* needed', taps=()), ov.end_card(3.7, 'Which cat will *you* get?')]
    save(strip(alt, ['ad 2 tagline, taps=()', 'ad 3 tagline'], scale=0.5), 'end_card_taglines.png')


def sheet_button():
    """PLAY FREE close-ups across the end card: pop, beat pulse, shine sweep, paw approach, press."""
    ts = [0.98, 1.0, 1.04, 1.08, 1.3, 1.4, 1.5, 1.6, 1.68, 1.8, 3.02]
    frames = [ov.end_card(t).crop((150, 1150, 1030, 1470)) for t in ts]
    save(strip(frames, [f't={t:.2f}' for t in ts], scale=0.5), 'end_button_strip.png')


def sheet_flash():
    """Flash modes and shake on the HUD screenshot."""
    frames, labels = [], []
    for mode, col in (('alpha', 'cream4'), ('dither', 'cream4'), ('dither', 'gold4')):
        for t in (0.0, 0.06, 0.12):
            frames.append(ov.flash(bg(BG_HUD), t, color=col, mode=mode))
            labels.append(f'{mode} {col} t={t:.2f}')
    dx, dy = ov.shake_offset(0.05, seed=1)
    frames.append(ov.apply_shake(bg(BG_HUD), dx, dy))
    labels.append(f'shake ({dx},{dy})')
    save(strip(frames, labels, scale=0.2), 'flash_strip.png')


def sheet_contact():
    files = sorted(p for p in OUT.glob('*.png') if p.name != 'contact_sheet.png')
    thumbs = []
    for p in files:
        im = Image.open(p).convert('RGBA')
        im.thumbnail((360, 360), Image.LANCZOS)
        thumbs.append((p.name, im))
    cols = 5
    cw, ch = 372, 392
    rows = (len(thumbs) + cols - 1) // cols
    sh = Image.new('RGBA', (cols * cw, rows * ch), (20, 22, 36, 255))
    d = ImageDraw.Draw(sh)
    for i, (n, im) in enumerate(thumbs):
        x, y = (i % cols) * cw + 6, (i // cols) * ch + 6
        sh.alpha_composite(im, (x, y))
        d.text((x, y + 364), n, fill=LABEL)
    save(sh, 'contact_sheet.png')


# ───────────────────────── checks ─────────────────────────
def check_palette():
    """Static overlays must use palette colours only (opaque pixels) and binary alpha."""
    pal = np.array(list(ov.PAL.values()), np.int32)
    items = {
        'logo': ov.logo_art(),
        'x_stamp': ov.x_stamp_art(),
        'pixel_text': ov.pixel_text('Mine *100*: *1.2M* gold', k=1, outline=True),
        'counter': ov.counter_static('+1.2M', k=1, icon_k=1),
        'split_tag': ov.split_tag('LEVEL 100', 'gold', 'icon_item_hat_legendary', 1),
        'button': ov._button_art('PLAY FREE', 174, 38, False),
        'end_bg': ov._background(1.3),
    }
    for st in ov.CAPTION_STYLES:
        items[f'caption:{st}'] = ov.caption_static('Tap to *help* them dig!', st, 952, 4)
    bad = 0
    for name, im in items.items():
        a = np.asarray(im).reshape(-1, 4).astype(np.int32)
        alpha_ok = np.isin(a[:, 3], (0, 255)).all()
        op = a[a[:, 3] == 255][:, :3]
        cols = np.unique(op, axis=0)
        off = [tuple(c) for c in cols if not (pal == c).all(axis=1).any()]
        if off or not alpha_ok:
            bad += 1
            print(f'  PALETTE FAIL {name}: off-palette {off[:5]} alpha_binary={alpha_ok}')
    print(f'  palette check: {len(items) - bad}/{len(items)} OK')


def check_text_equivalence():
    """Size-9 render scaled x4 must equal a direct size-36 render (art.md finding)."""
    from PIL import ImageFont
    f36 = ImageFont.truetype(str(ov.FONT_DIR / 'PixeloidSans-Bold.ttf'), 36)
    txt = 'CAT MINERS 1.2M'
    a = np.asarray(ov.pixel_text(txt, k=4, shadow=False))[..., 3] > 0
    m = Image.new('L', (a.shape[1] + 40, 60))
    d = ImageDraw.Draw(m)
    d.fontmode = '1'
    d.text((0, -ov.GLYPH_TOP * 4), txt, font=f36, fill=255)
    b = np.asarray(m) > 127
    cols = np.nonzero(b.any(0))[0]
    b = b[:a.shape[0], cols[0]:cols[0] + a.shape[1]]
    print('  text x4 == size 36 render:', b.shape == a.shape and bool((a == b).all()))


def check_paw():
    """Black-box fingertip test: render the paw alone, recover its scale and position from the opaque bbox
    and check which hand art pixel lies under the target. Pressed (lift 0): the target must be inside art
    pixel (2, 2) and within 1 px of its centre; hovering: the same point shifted by lift x 8 px down-right
    (down-left when mirrored)."""
    hw, hh = ov.sprite(ov.HAND).size
    cases = [(536, 1100, 0.05, None), (536, 1100, 0.15, None), (537, 1101, 0.0, None), (1000, 1100, 0.05, None),
             (1000, 1100, None, None), (536, 1100, None, None), (536, 1100, -0.05, None), (100, 400, 0.1, None)]
    tip = ov.button_tip()
    cases.append((tip[0], tip[1], 0.05, False))
    worst = 0.0
    for x, y, t, flip in cases:
        f = Image.new('RGBA', (ov.W, ov.H))
        ov.paw(f, x, y, t, flip=flip, ring=False)
        a = np.asarray(f)[..., 3] > 0
        ys, xs = np.nonzero(a)
        l, r, tp, b = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        sx, sy = (r - l) / hw, (b - tp) / hh
        mirrored = flip if flip is not None else x > ov.W - (hw + 2) * ov.HERO
        off = ov.paw_lift(t) * ov.HERO
        gx, gy = x + (-off if mirrored else off), y + off           # where the fingertip should be
        cx = (r - ov.HAND_TIP[0] * sx) if mirrored else (l + ov.HAND_TIP[0] * sx)
        cy = tp + ov.HAND_TIP[1] * sy
        ax = int((gx + 0.5 - l) / sx)
        ay = int((gy + 0.5 - tp) / sy)
        if mirrored:
            ax = hw - 1 - ax
        err = max(abs(cx - (gx + 0.5)), abs(cy - (gy + 0.5)))
        worst = max(worst, err)
        ok = (ax, ay) == (2, 2) and err <= 1.0
        print(f'  paw target ({x},{y}) t={t} mirrored={bool(mirrored)} lift={off}px: art px under fingertip '
              f'point = ({ax},{ay}), centre error {err:.2f} px -> {"OK" if ok else "FAIL"}')
    print(f'  paw worst fingertip error: {worst:.2f} px')


def _bbox_at(img: Image.Image, cx: float, cy: float):
    x0, y0 = ov.snap(cx - img.width / 2), ov.snap(cy - img.height / 2)
    bb = img.getbbox()
    return x0 + bb[0], y0 + bb[1], x0 + bb[2], y0 + bb[3]


def _safe(name, box, x_only=False):
    x0, y0, x1, y1 = box
    ok_x = x0 >= ov.SAFE_X and x1 <= ov.W - ov.SAFE_X
    ok_y = x_only or (y0 >= ov.SAFE_TOP and y1 <= ov.SAFE_BOTTOM)
    print(f'  {name:34s} x {x0:4d}..{x1:4d}  y {y0:4d}..{y1:4d}  {"OK" if ok_x and ok_y else "OUTSIDE SAFE ZONE"}')
    return ok_x and ok_y


def check_layout():
    """Safe-zone and size checks: end-card elements (incl. throb/joy extremes), every script caption at the
    default k (cap height, line count, plate width), WRONG WAY at the compositor's default positions."""
    print(f'  caption cap height: k={ov.CAPTION_K} -> {ov.CAP_H * ov.CAPTION_K} px (>= 36 required)')
    allok = True
    allok &= _safe('end: logo', _bbox_at(ov.logo(), ov.W / 2, ov.END_LOGO_Y))
    tl = ov.caption_static(ov.END_TAGLINE, 'plank')
    allok &= _safe('end: tagline (2 lines)', _bbox_at(tl, ov.W / 2, ov.END_TAGLINE_Y))
    g = 2
    btn = ov.up(ov._button_art('PLAY FREE', ov.END_BUTTON_ART[0] + 2 * g, ov.END_BUTTON_ART[1] + 2 * g, False), 4)
    allok &= _safe('end: PLAY FREE at max throb', _bbox_at(btn, ov.W / 2, ov.END_BUTTON_Y))
    k = ov.END_CAT_K
    for fur, gear, x, dy, left in ov.END_CATS:
        for fr, lift in (('idle0', 0), ('joy2', 6), ('spawn2', 7)):
            im = ov.sprite_k_cat(fur, fr, gear, left, k)
            fx = (40 - ov.CAT_FEET[0]) if left else ov.CAT_FEET[0]
            x0, y0 = ov.snap(x - fx * k), ov.snap(ov.END_CATS_FEET_Y + dy - ov.CAT_FEET[1] * k) - lift * k
            bb = im.getbbox()
            box = (x0 + bb[0], y0 + bb[1], x0 + bb[2], y0 + bb[3])
            if fr == 'idle0':
                allok &= _safe(f'end: cat {fur}/{gear}', box)
            elif box[1] < _bbox_at(tl, ov.W / 2, ov.END_TAGLINE_Y)[3]:
                print(f'  note: cat {fur} {fr} reaches y={box[1]} under the tagline plate (drawn behind it)')
    tl_box = _bbox_at(tl, ov.W / 2, ov.END_TAGLINE_Y)
    lg_box = _bbox_at(ov.logo(), ov.W / 2, ov.END_LOGO_Y)
    print(f'  end: gaps logo->tagline {tl_box[1] - lg_box[3]} px, tagline->button '
          f'{_bbox_at(btn, ov.W / 2, ov.END_BUTTON_Y)[1] - tl_box[3]} px')
    worst_lines = 0
    for text in SCRIPT_CAPTIONS:
        img = ov.caption_static(text, 'plank')
        n = len(ov.wrap_balanced(text, (ov.CAPTION_W - 2 * 13 * 4 - 3) // ov.CAPTION_K))
        worst_lines = max(worst_lines, n)
        if img.width > ov.CAPTION_W or n > 2:
            allok = False
            print(f'  caption too wide/tall: {text!r} {img.size} lines={n}')
    print(f'  {len(SCRIPT_CAPTIONS)} script captions at k={ov.CAPTION_K}: max {worst_lines} lines, '
          f'widest plate {max(ov.caption_static(c, "plank").width for c in SCRIPT_CAPTIONS)} px (<= {ov.CAPTION_W})')
    allok &= _safe('wrong_way X (cy 700)', _bbox_at(ov.big_x(), ov.W / 2, 700))
    allok &= _safe('wrong_way banner (cy 960)', _bbox_at(ov.caption_static('WRONG WAY!', 'red', k=8, icon=False),
                                                          ov.W / 2, 960))
    print('  layout:', 'all OK' if allok else 'PROBLEMS above')


def check_timing():
    hud = bg(BG_HUD)
    frozen = bg(BG_HUD)

    def bench(name, fn, n=20):
        fn(0.5)
        t0 = time.perf_counter()
        for i in range(n):
            fn(0.1 + i * 3.6 / n)
        print(f'  {name:28s} {1000 * (time.perf_counter() - t0) / n:6.1f} ms/frame')

    # End card exactly as the compositor renders it: 113 consecutive frames at 30 fps after one warm-up frame.
    ov.end_card(0.0)
    ts = [i / 30 for i in range(113)]
    times = []
    for t in ts:
        t0 = time.perf_counter()
        ov.end_card(t)
        times.append(1000 * (time.perf_counter() - t0))
    print(f'  end_card 113 frames @30fps    mean {np.mean(times):5.1f}  max {np.max(times):5.1f} ms/frame')
    # Caption: first (uncached) frame and steady state of a fresh text.
    ov.caption_static.cache_clear()
    t0 = time.perf_counter()
    ov.draw_caption(hud.copy(), 'Chests hide in *every* mine', 0.0)
    first = 1000 * (time.perf_counter() - t0)
    t0 = time.perf_counter()
    for i in range(30):
        ov.draw_caption(hud.copy(), 'Chests hide in *every* mine', i / 30)
    mean = 1000 * (time.perf_counter() - t0) / 30
    print(f'  caption (incl. frame copy)    first {first:5.1f}  mean {mean:5.1f} ms/frame')
    bench('caption+paw+counter on frame', lambda t: (
        ov.draw_counter(ov.paw(ov.draw_caption(hud.copy(), 'Tap to *help* them dig!', t), 540, 1100, t - 1),
                        1.2e6, t)))
    bench('wrong_way', lambda t: ov.wrong_way(frozen, t))
    bench('zzz', lambda t: ov.zzz(hud, t))
    bench('split_screen+labels', lambda t: ov.split_labels(ov.split_screen(hud, hud), t))
    bench('flash (alpha) on copy', lambda t: ov.flash(hud.copy(), t * 0.05))
    bench('flash (dither) on copy', lambda t: ov.flash(hud.copy(), t * 0.05, mode='dither'))


SHEETS = {
    'captions': sheet_captions, 'pop': sheet_pop, 'paw': sheet_paw, 'split': sheet_split,
    'wrong': sheet_wrong, 'zzz': sheet_zzz, 'counter': sheet_counter, 'logo': sheet_logo,
    'end': sheet_end, 'button': sheet_button, 'flash': sheet_flash, 'contact': sheet_contact,
}


def main(argv):
    names = argv or list(SHEETS)
    for n in names:
        if n in SHEETS:
            print(n)
            SHEETS[n]()
    if not argv or 'check' in argv:
        print('checks')
        check_palette()
        check_text_equivalence()
        check_paw()
        check_layout()
        check_timing()


if __name__ == '__main__':
    main(sys.argv[1:])
