#!/usr/bin/env python3
"""Assemble Unity card evidence on Linux; transfer binaries through the remote PC bridge."""
import argparse
import base64
import io
import json
from pathlib import Path
import subprocess
import time
import zipfile
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SCRATCH = ROOT / 'Temp/card-evidence'
OUT = SCRATCH / 'out'
PC_ROOT = r'C:\Unity Projects\Cats Cute Miner'
REPORT = r'Docs\Reports\outpost'


def remote(command):
    return subprocess.check_output(['on-remote', 'powershell', '-NoProfile', '-Command', command], cwd=ROOT)


def download_zip():
    # One binary transfer for all source renders, including the recording manifest.
    archive_dir = PC_ROOT + r'\Temp\card-evidence'
    archive = archive_dir + r'\sources.zip'
    remote(f"New-Item -ItemType Directory -Force -Path '{archive_dir}' | Out-Null")
    screenshots = PC_ROOT + '\\' + REPORT + r'\screenshots'
    frames = PC_ROOT + '\\' + REPORT + r'\recording\cards'
    remote(f"$files = @((Get-ChildItem '{screenshots}\\cards-page-*.png'), (Get-ChildItem '{screenshots}\\cards-pages-*.json'), (Get-ChildItem '{screenshots}\\card-ca003-state-*.png'), (Get-Item '{frames}')); Compress-Archive -Force -Path $files.FullName -DestinationPath '{archive}'")
    raw = remote(f"[Convert]::ToBase64String([IO.File]::ReadAllBytes('{archive}'))")
    data = base64.b64decode(raw)
    (SCRATCH / 'sources.zip').write_bytes(data)
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for member in z.infolist():
            target = SCRATCH / 'sources' / member.filename.replace('\\', '/')
            if member.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(z.read(member))


def stitch(source, scenario):
    meta = json.loads((source / f'cards-pages-{scenario}.json').read_text(encoding='utf-8-sig'))
    x, y, w, v = (meta[k] for k in ('x', 'y', 'width', 'height'))
    scale = v / meta['V']
    h = round(meta['H'] * scale)
    first = Image.open(source / f'cards-page-{scenario}-0.png').convert('RGB')
    result = Image.new('RGB', (first.width, y + h), first.getpixel((0, y)))
    result.paste(first.crop((0, 0, first.width, y)), (0, 0))
    # Map each viewport to content pixels. Overlapping final page contributes only its unseen tail.
    end = 0
    for k, offset in enumerate(meta['offsets']):
        start = round(offset * scale)
        page = Image.open(source / f'cards-page-{scenario}-{k}.png').convert('RGB')
        skip = max(0, end - start)
        take = min(v - skip, h - end)
        if take > 0:
            # Crop the exact viewport; retain the tall image's original side gutters.
            result.paste(page.crop((x, y + skip, x + w, y + skip + take)), (x, y + end))
            end += take
    assert end == h, (scenario, end, h)
    assert len(set(meta['cardIds'])) == 61
    path = OUT / f'cards-collection-{scenario}.png'
    result.save(path)
    return {'file': path.name, 'width': result.width, 'height': result.height, 'cards': 61}


def comparison(source):
    panels = [Image.open(source / f'card-ca003-state-{s}.png').convert('RGB').resize((540, 960), Image.Resampling.LANCZOS)
              for s in ('locked', 'available', 'owned')]
    fontpath = Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')
    header = 38 if fontpath.exists() else 0
    result = Image.new('RGB', (1620, 960 + header), '#191b29')
    for i, panel in enumerate(panels):
        result.paste(panel, (540 * i, header))
    if header:
        draw = ImageDraw.Draw(result)
        font = ImageFont.truetype(str(fontpath), 23)
        for i, title in enumerate(('Закрыта (шахта 2)', 'Доступна (шахта 3)', 'Получена')):
            draw.text((540 * i + 270, 18), title, font=font, fill='white', anchor='mm')
    path = OUT / 'card-ca003-states.png'
    result.save(path)
    return {'file': path.name, 'width': result.width, 'height': result.height}


def recording(source):
    manifest_path = next(source.rglob('frames.json'))
    entries = json.loads(manifest_path.read_text(encoding='utf-8-sig'))['frames']
    # Thin only the time-lapse if needed; carry removed display time into the preceding retained frame.
    frames, durations, selected = [], [], []
    mining_count = 0
    for entry in entries:
        if entry['chapter'] == 'mining-x20':
            mining_count += 1
            if mining_count % 2 == 0 and len(entries) > 120:
                durations[-1] += entry['durationMs']
                continue
        selected.append(entry)
        frame = Image.open(manifest_path.parent / entry['file']).convert('RGB').resize((540, 960), Image.Resampling.LANCZOS)
        # Exact balances from the captured live profile make the 50-elixir spend readable
        # even when the game detail window covers the collection balance header.
        fontpath = Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')
        if fontpath.exists():
            draw = ImageDraw.Draw(frame)
            draw.rectangle((0, 930, 540, 960), fill='#191b29')
            font = ImageFont.truetype(str(fontpath), 15)
            label = f"Ур. {entry['level']}   •   Копии: {entry['copies']}   •   Эликсир: {int(entry['elixir']):,}".replace(',', ' ')
            draw.text((10, 936), label, fill='white', font=font)
        frames.append(frame)
        durations.append(entry['durationMs'])
    path = OUT / 'cards-obtain-upgrade.gif'
    # One adaptive palette keeps static HUD colours identical between frames. No dithering
    # preserves pixel art and lets GIF delta compression retain those unchanged regions.
    samples = [frame for frame, duration in zip(frames, durations)
               for _ in range(max(1, min(30, round(duration / 100))))]
    swatches = Image.new('RGB', (135 * 10, 240 * ((len(samples) + 9) // 10)))
    for i, frame in enumerate(samples):
        swatches.paste(frame.resize((135, 240), Image.Resampling.NEAREST), ((i % 10) * 135, (i // 10) * 240))
    # Reserve half the adaptive palette for details, so rare purple elixir/art
    # and the green upgrade button retain their colours alongside the mine scene.
    details = [f for f, e in zip(frames, selected) if e['chapter'] in ('owned-detail', 'debug-grant-upgrade-ready', 'upgrade')]
    detail_swatches = Image.new('RGB', (270 * len(details), 480))
    for i, frame in enumerate(details):
        detail_swatches.paste(frame.resize((270, 480), Image.Resampling.NEAREST), (i * 270, 0))
    palette = Image.new('P', (1, 1))
    palette.putpalette(swatches.quantize(colors=128).getpalette()[:384] + detail_swatches.quantize(colors=128).getpalette()[:384])
    frames = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=1)
    while path.stat().st_size > 8 * 1024 * 1024:
        # Keep story beats; thin only mining frames and preserve total display time.
        removable = [i for i, e in enumerate(selected) if e['chapter'] == 'mining-x20'][1::2]
        assert removable, 'GIF exceeds 8 MiB after thinning the time-lapse'
        for i in reversed(removable):
            durations[i - 1] += durations[i]
            del frames[i], durations[i], selected[i]
        frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True, disposal=1)
    gif = Image.open(path)
    total = 0
    for i in range(gif.n_frames):
        gif.seek(i)
        total += gif.info['duration']
    # Review exports target meaningful chapters, not arbitrary GIF indices.
    for chapter in ('first-delivery', 'exit-chest-results', 'debug-grant-upgrade-ready', 'upgrade'):
        choices = [e for e in selected if e['chapter'] == chapter]
        entry = choices[-1] if chapter != 'exit-chest-results' else choices[0]
        Image.open(manifest_path.parent / entry['file']).resize((540, 960), Image.Resampling.LANCZOS).save(SCRATCH / f'review-{chapter}.png')
    return {'file': path.name, 'width': 540, 'height': 960, 'frames': gif.n_frames, 'durationMs': total, 'bytes': path.stat().st_size}


def upload(path):
    relay = ROOT / 'Docs/Reports/outpost/screenshots' / (path.name + '.b64')
    relay.parent.mkdir(parents=True, exist_ok=True)
    relay.write_text(base64.b64encode(path.read_bytes()).decode('ascii'))
    try:
        time.sleep(3)
        destination = PC_ROOT + '\\' + REPORT + '\\screenshots\\' + path.name
        size = remote(f"[IO.File]::WriteAllBytes('{destination}', [Convert]::FromBase64String([IO.File]::ReadAllText('{destination}.b64'))); (Get-Item '{destination}').Length")
        assert int(size.strip()) == path.stat().st_size
    finally:
        relay.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--local', action='store_true', help='Reuse previously downloaded sources')
    parser.add_argument('--no-upload', action='store_true')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    if not args.local:
        download_zip()
    source = SCRATCH / 'sources'
    reports = [stitch(source, s) for s in ('zone1', 'zone3', 'zone10', 'states')]
    reports += [comparison(source), recording(source)]
    for report in reports:
        path = OUT / report['file']
        report.setdefault('bytes', path.stat().st_size)
        report['pcPath'] = PC_ROOT + '\\' + REPORT + '\\screenshots\\' + path.name
        if not args.no_upload:
            upload(path)
    (OUT / 'evidence-summary.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(reports, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
