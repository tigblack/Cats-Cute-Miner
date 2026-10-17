#!/usr/bin/env python3
"""Reproducible collection contact sheets and the pixel check of unchanged card arts (separate from the atlas).

  python3 Tools/PixelArt/review_cards.py          — check, then sheets: Docs/Reports/factories/art/cards.png (ТЗ 2.2
                                                    §7.2), out/cards_overview.png, out/cards_states.png, out/cards_rarities.png
  python3 Tools/PixelArt/review_cards.py --check  — only the checks below (exit code 1 on any problem)
  python3 Tools/PixelArt/review_cards.py --save-before <PixelAtlasData.g.cs>
                                                  — write the baseline of previous arts (once, from the pre-2.2 atlas)

Checks (ТЗ 2.2 §7.2; every one of them fails --check):
  1) card_art.CATALOG has the 61 cards of json-data.json plus the explicitly staged demolition art ca063;
  2) ca001 and the 21 global arts drawn by sprites/card_art.py equal the pre-2.2 atlas (hash BEFORE_ATLAS): SHA-256 of
     their 48×44 RGBA pixels in BEFORE;
  3) the atlas now in PixelAtlasData.g.cs holds all current catalog arts and nothing else: every card_art_<id> is present and
     pixel-identical to what sprites/card_art.py draws now (sprites are trimmed there; the 48×44 canvas is restored from
     the pivot), and there is no card_art_* sprite without a card. A stale art means the atlas was not rebuilt: build.py.

Previous arts of the 39 factory cards come from Docs/Reports/factories/art/cards_before.png: all 61 pre-2.2 arts in
48×44 cells, 8 per row, sorted by id (written by --save-before; the 22 unchanged cells are verified against BEFORE).

Locked look as in CardView: black 60 % (CardView.LockedArtAlpha) over the whole 64×48 portrait together with the
rarity background, icon_lock (from the atlas) flush to the bottom-right corner on top of the shade.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))

from pxl import C, Registry  # noqa: E402
from sprites import card_art, cards, ui  # noqa: E402

OUT = HERE / 'out'
REPORT = ROOT / 'Docs' / 'Reports' / 'factories' / 'art'
ATLAS_CS = ROOT / 'Assets' / 'Game' / 'Scripts' / 'Runtime' / 'Pixel' / 'PixelAtlasData.g.cs'
BEFORE_PNG = REPORT / 'cards_before.png'
FONTS = (ROOT / 'Assets' / 'Game' / 'Resources' / 'Fonts' / 'PixeloidSans-Regular.ttf',
         Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'))
LOCKED_ART_ALPHA = 0.60          # CardView.LockedArtAlpha
ART_W, ART_H = 48, 44            # card_art canvas; pivot in the centre
BG_W, BG_H = 64, 48              # card_bg_<tone>; the art sits at (8, 2)
SOURCE_JSON = ROOT / 'Assets' / 'Game' / 'Data' / 'Source' / 'json-data.json'
EXPECTED_CARDS = 61              # ТЗ 2.2 rule 4: all 61 source cards stay with their IDs
STAGED_ART_IDS = {63}             # Demolition Expert art ships ahead of game-data integration
BEFORE_COLS = 8
BEFORE_ATLAS = 'dcea9cdf0d725a67'
# SHA-256 of the 48×44 RGBA pixels (transparent pixels zeroed) in the pre-2.2 atlas, PixelAtlasData.g.cs hash above.
BEFORE = {
    1: '9dd5f2548fde15f577b4ade53635b60235d50e68bcdb59febe5c0fa9a64961d2',
    26: 'db4b162e9296bb84c861b44cd35dede1004261322592fca37e5ba4b9ee7283f4',
    27: 'cdacd41687a847b86b64afe8330d05c74f51de828cad2789b6cab77d18fca775',
    28: '0dc038466e11cc2307f8edd4ad88848d2c04224afa4d6adff8b989c742209592',
    29: '3e2e54b02cd31b1451df334d67078f95650358aeacef738ce95f520d737ddf7e',
    30: 'f0685d6e8e6416814d35032c5b0c9693abebbd4b4056eb5ef11e2bbd8bcedcce',
    31: 'f1c94b5321518fedf4182955cc9e931a9c7fb055f47756a5a82156bfb148f59e',
    32: 'c24971890ea4e0e7609985ed68a81e7abd4a151bd03e58b103c1822c9d471273',
    33: 'dfa71f3e1aa5d1f92a781737198a05639a3bf20c50841e3c8972f94114f499cd',
    34: '983308d272400210f91be05509fcb37715331e90c81c7063fce21249e9e99b5d',
    35: 'fd2495b6ca34dbc6fe25b20bccb558a5be9aefd644cf347943b15b02307884dd',
    36: 'b205ac83970322db5d576d63332d63c087f2f31d8bda72bcce165bf9a230a19b',
    37: '27723763776784da75c41354a4ac2cad1faa0986605ed0cfdcde28de1313c6d5',
    38: 'e6ec6e560820470924ac9f63e2ec07fbc8721edef7e1110581cbaa1640ab0016',
    43: 'bb0ae18631506e8246f23c8d3a729c133aa4f60aa06f14d8479f0f915d04a5af',
    50: '7e86fdf129504ef957d928b52687cf023eb977195d70b77327c27b2fbfbc1472',
    51: 'a739281d1acbfb8c6840e1ed32b72231cb917c4c45e65ecc3e6d17464f252738',
    52: '9372cbef05acfa5cc39c61b0fdd54ccc498ecb61d8f28180524a325b7eee0a47',
    53: '14e1c3a64f0fc814a70a957a2f3cc696feafabd77e73258f0268c6df29aff7fa',
    54: '474d902676db495d84ab0526f28de0eec1d9fae433057d61a181f7fe71d5d0f0',
    55: '5bbc43484a0b27445826b9948e538f08d5c8b8e938c199e493f1083be284fa2a',
    62: '60027ac9d6fe9868e81cbcb12c6d4708d0fef05e4bc696aac559610aed2f1726',
}


def norm(img: Image.Image) -> Image.Image:
    """RGBA copy with fully transparent pixels zeroed (the hash ignores colour under alpha 0)."""
    img = img.convert('RGBA')
    opaque = img.getchannel('A').point(lambda v: 255 if v else 0)
    return Image.composite(img, Image.new('RGBA', img.size), opaque)


def digest(img: Image.Image) -> str:
    return hashlib.sha256(norm(img).tobytes()).hexdigest()


def read_atlas(path: Path):
    """PixelAtlasData.g.cs → (hash, atlas image, {name: (x, top, w, h, pivot_x, pivot_y)})."""
    text = path.read_text(encoding='utf-8')
    height = int(re.search(r'public const int Height = (\d+);', text).group(1))
    atlas_hash = re.search(r'public const string Hash = "(\w+)";', text).group(1)
    tail = text[text.index('public const string Png'):]
    png = ''.join(re.findall(r'"([A-Za-z0-9+/=]+)"', tail))
    atlas = Image.open(io.BytesIO(base64.b64decode(png))).convert('RGBA')
    defs = {}
    for m in re.finditer(r'new PixelSpriteDef\("(\w+)", (-?\d+), (-?\d+), (\d+), (\d+), ([-\d.e+]+)f, ([-\d.e+]+)f', text):
        x, uy, w, h = (int(v) for v in m.groups()[1:5])
        defs[m.group(1)] = (x, height - uy - h, w, h, float(m.group(6)), float(m.group(7)))
    return atlas_hash, atlas, defs


def sprite(atlas, defs, name) -> Image.Image:
    x, y, w, h, _, _ = defs[name]
    return atlas.crop((x, y, x + w, y + h))


def full_art(atlas, defs, name) -> Image.Image:
    """Card art restored to its 48×44 canvas: build.py trims it and moves the pivot, the centre (24, 22) stays."""
    x, y, w, h, px, py = defs[name]
    left, bottom = round(ART_W / 2 - px), round(ART_H / 2 + py)
    out = Image.new('RGBA', (ART_W, ART_H))
    out.paste(atlas.crop((x, y, x + w, y + h)), (left, bottom - h))
    return out


def save_before(atlas_cs: Path) -> None:
    _, atlas, defs = read_atlas(atlas_cs)
    ids = sorted(card_art.CATALOG)
    rows = (len(ids) + BEFORE_COLS - 1) // BEFORE_COLS
    sheet = Image.new('RGBA', (BEFORE_COLS * ART_W, rows * ART_H))
    for k, i in enumerate(ids):
        sheet.paste(full_art(atlas, defs, f'card_art_ca{i:03}'), ((k % BEFORE_COLS) * ART_W, (k // BEFORE_COLS) * ART_H))
    REPORT.mkdir(parents=True, exist_ok=True)
    sheet.save(BEFORE_PNG)
    print(f'[cards] previous arts of {atlas_cs.name} → {BEFORE_PNG.relative_to(ROOT)}')


def load_before():
    """{id: 48×44 previous art} from the baseline sheet, or {} if it is missing."""
    if not BEFORE_PNG.exists():
        return {}
    sheet = Image.open(BEFORE_PNG).convert('RGBA')
    ids = sorted(card_art.CATALOG)
    return {i: sheet.crop(((k % BEFORE_COLS) * ART_W, (k // BEFORE_COLS) * ART_H,
                           (k % BEFORE_COLS + 1) * ART_W, (k // BEFORE_COLS + 1) * ART_H)) for k, i in enumerate(ids)}


def source_card_ids() -> set:
    """Numeric IDs of the cards in the read-only source json-data.json ("ca062" → 62)."""
    return {int(c['Id'][2:]) for c in json.loads(SOURCE_JSON.read_text(encoding='utf-8'))['Cards']}


def check(reg: Registry, atlas_info, before):
    """All checks of the module docstring → (error lines, unchanged arts with a problem); no error lines — all good."""
    errors, bad = [], set()
    assert set(card_art.UNCHANGED) == set(BEFORE), 'BEFORE must list exactly card_art.UNCHANGED'
    catalog, source = set(card_art.CATALOG), source_card_ids()
    expected = source | STAGED_ART_IDS
    if len(source) != EXPECTED_CARDS or catalog != expected:
        errors.append(f'card_art.CATALOG: {len(catalog)} arts, json-data.json: {len(source)} cards, expected {EXPECTED_CARDS}; '
                      f'without art {sorted(expected - catalog)}, unexpected art {sorted(catalog - expected)}; '
                      f'staged arts {sorted(STAGED_ART_IDS)}')
    for i in card_art.UNCHANGED:
        art = reg.entries[f'card_art_ca{i:03}'].canvas.img
        if digest(art) != BEFORE[i]:
            errors.append(f'ca{i:03}: differs from the pre-2.2 atlas {BEFORE_ATLAS}')
            bad.add(i)
        if i in before and digest(before[i]) != BEFORE[i]:
            errors.append(f'ca{i:03}: {BEFORE_PNG.name} is not the pre-2.2 baseline')
    atlas_hash, atlas, defs = atlas_info
    missing, stale, current = [], [], 0
    for i in sorted(card_art.CATALOG):
        name = f'card_art_ca{i:03}'
        if name not in defs:
            errors.append(f'{name}: missing in {ATLAS_CS.name}')
            missing.append(f'ca{i:03}')
            bad.add(i)
            continue
        same = digest(full_art(atlas, defs, name)) == digest(reg.entries[name].canvas.img)
        current += same
        if not same and i in card_art.UNCHANGED:
            errors.append(f'ca{i:03}: differs from the atlas {atlas_hash} in {ATLAS_CS.name}')
            bad.add(i)
        elif not same:
            errors.append(f'ca{i:03}: the atlas {atlas_hash} holds an outdated art (rebuild with build.py)')
            stale.append(f'ca{i:03}')
    current_names = {f'card_art_ca{i:03}' for i in card_art.CATALOG}
    extra = sorted(name for name in defs if name.startswith('card_art_') and name not in current_names)
    for name in extra:
        errors.append(f'{name}: in {ATLAS_CS.name}, but sprites/card_art.py has no such card art')
    n = len(card_art.UNCHANGED)
    unchanged_bad = len(bad & set(card_art.UNCHANGED))
    print(f'[cards] unchanged arts: {n - unchanged_bad}/{n} '
          f'identical to the pre-2.2 atlas {BEFORE_ATLAS} and the atlas {atlas_hash}')
    if current != len(card_art.CATALOG) or extra:
        print(f'[cards] atlas {atlas_hash} holds {current}/{len(card_art.CATALOG)} current arts: stale {len(stale)} '
              f'({", ".join(stale[:5])}{"…" if len(stale) > 5 else ""}), missing {len(missing)}, extra {len(extra)} — '
              f'rebuild with build.py')
    else:
        print(f'[cards] atlas {atlas_hash} matches all {len(card_art.CATALOG)} arts')
    return errors, unchanged_bad


# ───────────────────────── sheets ─────────────────────────
def font(size):
    for path in FONTS:
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def portrait(i, art):
    """64×48 portrait as in CardView: rarity background, art centred at (8, 2)."""
    rarity, _ = card_art.CATALOG[i]
    bg = cards.portrait_bg(card_art.RARITIES[rarity - 1]).img.copy()
    bg.alpha_composite(art, (8, 2))
    return bg


def locked(img, lock):
    """CardView locked look: black LockedArtAlpha over the whole portrait, the lock flush bottom-right on top."""
    out = img.copy()
    out.alpha_composite(Image.new('RGBA', out.size, (0, 0, 0, round(255 * LOCKED_ART_ALPHA))))
    out.alpha_composite(lock, (out.width - lock.width, out.height - lock.height))
    return out


def big(img, k):
    return img.resize((img.width * k, img.height * k), Image.Resampling.NEAREST)


def factory_sheet(reg, before, lock, check_line):
    k_new, k_old = 3, 2
    f_title, f_text, f_small = font(27), font(18), font(18)
    col_new = 240
    col_old = col_new + 2 * (BG_W * k_new + 24) + 8
    col_lock = col_old + 2 * (BG_W * k_old + 8) + 24
    width = col_lock + 2 * (BG_W * k_old + 8) + 16
    row_h = BG_H * k_new + 58
    top = 150
    small_top = top + len(card_art.FAMILIES) * row_h + 16
    height = small_top + 3 * (BG_H + 8) + 70
    im = Image.new('RGB', (width, height), C('navy0')[:3])
    d = ImageDraw.Draw(im)
    d.text((16, 12), 'Cat Miners · factory cards · TZ 2.2 §7.2 · 39 new arts', font=f_title, fill=C('cream3'))
    d.text((16, 50), 'Foreman = Common, Manager = Uncommon · new art ×3 on the 64×48 portrait · previous art ×2',
           font=f_small, fill=C('cream2'))
    d.text((16, 72), 'Locked as in CardView ×2: black 60 % over the whole portrait, lock on top',
           font=f_small, fill=C('cream2'))
    d.text((16, 98), check_line, font=f_small, fill=C('green5') if 'identical' in check_line else C('red5'))
    for x, label in ((col_new, 'new: Foreman / Manager'), (col_old, 'previous'), (col_lock, 'locked')):
        d.text((x, top - 26), label, font=f_small, fill=C('gold4'))
    for n, fam in enumerate(card_art.FAMILIES):
        y = top + n * row_h
        d.line([(16, y - 6), (width - 16, y - 6)], fill=C('navy2'))
        d.text((16, y + 4), f'{n + 1}. {fam.name}', font=f_text, fill=C('cream3'))
        d.text((16, y + 28), fam.good, font=f_small, fill=C('gold4'))
        d.text((16, y + 52), fam.type, font=f_small, fill=C('navy6'))
        d.text((16, y + 76), f'{fam.fur} · {fam.helmet}', font=f_small, fill=C('navy6'))
        for j, i in enumerate((fam.common, fam.manager)):
            x = col_new + j * (BG_W * k_new + 24)
            if not i:
                d.text((x, y + 60), 'no Uncommon card', font=f_small, fill=C('navy6'))
                continue
            art = reg.entries[f'card_art_ca{i:03}'].canvas.img
            im.paste(big(portrait(i, art), k_new), (x, y))
            rarity, name = card_art.CATALOG[i]
            d.text((x, y + BG_H * k_new + 6), f'ca{i:03} · {card_art.RARITIES[rarity - 1]}', font=f_small, fill=C('gold4'))
            d.text((x, y + BG_H * k_new + 28), name, font=f_small, fill=C('cream3'))
            xo = col_old + j * (BG_W * k_old + 8)
            if i in before:
                im.paste(big(portrait(i, before[i]), k_old), (xo, y))
            else:
                d.text((xo, y + 40), 'no baseline', font=f_small, fill=C('red5'))
            xl = col_lock + j * (BG_W * k_old + 8)
            im.paste(big(locked(portrait(i, art), lock), k_old), (xl, y))
    d.text((16, small_top), 'Small-size check: all 39 portraits ×1, Foreman then Manager (one art pixel per pixel)',
           font=f_small, fill=C('gold4'))
    ids = [i for fam in card_art.FAMILIES for i in (fam.common, fam.manager) if i]
    per = (width - 32) // (BG_W + 8)
    for k, i in enumerate(ids):
        art = reg.entries[f'card_art_ca{i:03}'].canvas.img
        im.paste(portrait(i, art), (16 + (k % per) * (BG_W + 8), small_top + 30 + (k // per) * (BG_H + 8)))
    return im


def overview(reg):
    f_text, f_title = font(18), font(27)
    cols = 7
    cw, ch = 216, 205
    rows = (len(card_art.CATALOG) + cols - 1) // cols
    im = Image.new('RGB', (cols * cw + 24, rows * ch + 65), C('navy0')[:3])
    d = ImageDraw.Draw(im)
    d.text((16, 12), f'Cat Miners · {len(card_art.CATALOG)} unique cards · art ×3 / 48×44', font=f_title, fill=C('cream3'))
    for k, i in enumerate(sorted(card_art.CATALOG)):
        x, y = 12 + (k % cols) * cw, 53 + (k // cols) * ch
        bg = big(portrait(i, reg.entries[f'card_art_ca{i:03}'].canvas.img), 3)
        im.paste(bg, (x + 12, y), bg)
        rarity, name = card_art.CATALOG[i]
        d.text((x + 12, y + 147), f'ca{i:03} · {card_art.RARITIES[rarity - 1]}', font=f_text, fill=C('gold4'))
        lines = ['']
        for word in name.split():
            candidate = (lines[-1] + ' ' + word).strip()
            if d.textlength(candidate, font=f_text) > cw - 24:
                lines.append(word)
            else:
                lines[-1] = candidate
        for j, line in enumerate(lines):
            d.text((x + 12, y + 167 + j * 18), line, font=f_text, fill=C('cream3'))
    return im


def states(reg, lock):
    f_text, f_title = font(18), font(27)
    i = card_art.FAMILIES[0].common
    state = Image.new('RGB', (770, 306), C('navy0')[:3])
    d = ImageDraw.Draw(state)
    d.text((16, 12), f'ca{i:03} · {card_art.CATALOG[i][1]} · readability', font=f_title, fill=C('cream3'))
    normal = portrait(i, reg.entries[f'card_art_ca{i:03}'].canvas.img)
    for img, x, k, label in ((normal, 16, 4, 'Normal · ×4'), (locked(normal, lock), 294, 4, 'Locked · black 60 % · ×4'),
                             (normal, 620, 2, 'Grid · ×2')):
        sample = big(img, k)
        state.paste(sample, (x, 58), sample)
        d.text((x, 268), label, font=f_text, fill=C('cream3'))
    return state


def rarities():
    f_text = font(18)
    frames = Image.new('RGB', (6 * 110, 170), C('navy0')[:3])
    for j, r in enumerate(card_art.RARITIES):
        art = ui.card_frame(r).scaled(4)
        frames.paste(art, (j * 110 + 7, 5), art)
        ImageDraw.Draw(frames).text((j * 110 + 7, 141), r, font=f_text, fill=C('cream3'))
    return frames


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true',
                    help='only the checks: source cards plus staged arts, unchanged pixels, current atlas')
    ap.add_argument('--save-before', metavar='ATLAS_CS', help='write the previous-art baseline from this atlas file')
    args = ap.parse_args()
    if args.save_before:
        save_before(Path(args.save_before))
        return 0
    reg = Registry()
    card_art.build(reg)
    before = load_before()
    if not before:
        print(f'[cards] no baseline {BEFORE_PNG.relative_to(ROOT)}: previous arts are not shown')
    atlas_info = read_atlas(ATLAS_CS)
    errors, unchanged_bad = check(reg, atlas_info, before)
    for e in errors:
        print('  - ' + e)
    if args.check:
        return 1 if errors else 0
    _, atlas, defs = atlas_info
    lock = sprite(atlas, defs, 'icon_lock')
    n = len(card_art.UNCHANGED)
    if not errors:
        check_line = f'Unchanged: {n}/{n} arts (ca001 and 21 global cards) identical to the pre-2.2 atlas {BEFORE_ATLAS}'
    elif unchanged_bad:
        check_line = f'Unchanged arts: {unchanged_bad} of {n} differ, see review_cards.py --check'
    else:
        check_line = f'Atlas or card list: {len(errors)} problem(s), see review_cards.py --check'

    OUT.mkdir(exist_ok=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    sheet = factory_sheet(reg, before, lock, check_line)
    sheet.save(REPORT / 'cards.png')
    overview(reg).save(OUT / 'cards_overview.png')
    states(reg, lock).save(OUT / 'cards_states.png')
    rarities().save(OUT / 'cards_rarities.png')
    print(f'[cards] {len(card_art.FACTORY_IDS)} factory arts → {(REPORT / "cards.png").relative_to(ROOT)}; '
          f'{len(card_art.CATALOG)} illustrations → out/cards_overview.png; states → out/cards_states.png')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
