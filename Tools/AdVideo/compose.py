"""Compositor for the Cat Miners video ads: Unity footage + pixel overlays + music/SFX -> 45 s Meta-ready MP4.

Inputs (hub paths, relative to the project root):
  build/adcapture/<shot>.mp4          footage recorded by AdCaptureTests (fetched from the PC with `on-remote --fetch`;
                                      `build/` is not synced)
  Docs/Reports/ads/<shot>.json        per-shot log: frames, taps (for the paw), sound cues, marks, core events
  Temp/audio/out/*.ogg                the game's own SFX; the game sound is rebuilt from the cue log in sync
  Temp/ads/music/<track>.wav          ad music (Tools/AdVideo/ad_music.py), Temp/ads/sfx/*.wav stingers
  Tools/AdVideo/edl.py                the three edit decision lists (segments + overlays + stingers)

Usage:
  Tools/.venv/bin/python -B Tools/AdVideo/compose.py ad1 [ad2 ad3] [--stills 0.5,4,12] [--no-video] [--half]
Output: Docs/Marketing/ads/<name>.mp4 (H.264 High yuv420p 1080x1920 30 fps CFR, AAC 192k 48 kHz, -14 LUFS, faststart),
        Temp/ads/stills/<ad>_<t>.png for --stills.
"""
from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Sequence, Tuple, Union

import numpy as np
import soundfile as sf
from PIL import Image
from scipy.signal import resample_poly

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))
import overlays as ov  # noqa: E402

FFMPEG = str(next((ROOT / 'Tools/.venv/lib').glob('python3*/site-packages/imageio_ffmpeg/binaries/ffmpeg-*')))
SHOTS = ROOT / 'build' / 'adcapture'
LOGS = ROOT / 'Docs' / 'Reports' / 'ads'
MUSIC = ROOT / 'Temp' / 'ads' / 'music'
SFX = ROOT / 'Temp' / 'ads' / 'sfx'
OUT = ROOT / 'Docs' / 'Marketing' / 'ads'
STILLS = ROOT / 'Temp' / 'ads' / 'stills'
W, H, FPS, SR = 1080, 1920, 30, 48000
LENGTH = 45.0
NFRAMES = int(round(LENGTH * FPS))


# ───────────────────────── shot logs ─────────────────────────
_logs: Dict[str, dict] = {}


def log(shot: str) -> dict:
    if shot not in _logs:
        _logs[shot] = json.loads((LOGS / f'{shot}.json').read_text(encoding='utf-8-sig'))
    return _logs[shot]


def mark(shot: str, name: str, default: Optional[float] = None) -> float:
    """Seconds of the first mark/core event called `name` in the shot (marks may carry a ':suffix')."""
    for e in log(shot)['events']:
        if e['kind'] in ('mark', 'core') and (e['name'] == name or e['name'].startswith(name + ':')):
            return e['f'] / FPS
    if default is None:
        raise KeyError(f'{shot}: no mark {name}')
    return default


def shot_seconds(shot: str) -> float:
    return log(shot)['frames'] / FPS


def taps(shot: str) -> List[Tuple[float, float, float, str]]:
    """(seconds, x, y, name) of every logged tap (strike or UI) in the shot."""
    out = []
    for e in log(shot)['events']:
        if e['kind'] == 'tap':
            d = e.get('data') or {}
            if d.get('ok', True) is False and e['name'] == 'strike':
                continue
            out.append((e['f'] / FPS, float(d['x']), float(d['y']), e['name']))
    return out


# ───────────────────────── edit decision list types ─────────────────────────
View = Tuple[float, float, float]            # zoom, centre x, centre y in source px


@dataclass
class Seg:
    """One cut: output [start, end) shows `src` from src_t on. speed 0 = freeze frame at src_t.
    view(u) -> (zoom, cx, cy) with u in [0, 1] over the segment (zoom >= 1 crops the source).
    split: (src2, src2_t, top_focus, bottom_focus) shows src on top and src2 below (split screen)."""
    start: float
    end: float
    src: str
    src_t: float = 0.0
    speed: float = 1.0
    view: Optional[Callable[[float], View]] = None
    split: Optional[Tuple[str, float, float, float]] = None
    paws: bool = True
    audio: float = 1.0                      # game audio gain (0 = mute)
    hud_mask: bool = False                  # hide the game HUD bars (top 128 px, bottom bar) behind navy
    hover: Optional[Tuple[float, ...]] = None     # paw over this source point (follows the view): (x, y[, tap_t])

    @property
    def frames(self) -> int:
        return int(round((self.end - self.start) * FPS))


@dataclass
class Ov:
    """Overlay drawn on [t0, t1): kind in caption, counter, logo, wrong, zzz, end, flash, split_labels, paw, burst,
    dim, shake. `a` holds kind-specific arguments."""
    kind: str
    t0: float
    t1: float = LENGTH
    a: dict = field(default_factory=dict)


@dataclass
class Ad:
    name: str                               # output file stem
    music: str                              # Temp/ads/music/<music>.wav
    segs: List[Seg]
    ovs: List[Ov]
    sfx: List[Tuple[float, str, float]] = field(default_factory=list)   # (t, stinger, gain)
    music_gain: float = 1.0
    game_gain: float = 0.9


def zoom_to(z0: float, c0: Tuple[float, float], z1: float, c1: Tuple[float, float]) -> Callable[[float], View]:
    """Eased push-in/out between two views."""
    def f(u: float) -> View:
        e = ov.ease_in_out(u)
        return (z0 + (z1 - z0) * e, c0[0] + (c1[0] - c0[0]) * e, c0[1] + (c1[1] - c0[1]) * e)
    return f


def still(z: float, cx: float = W / 2, cy: float = H / 2) -> Callable[[float], View]:
    return lambda u: (z, cx, cy)


# ───────────────────────── video sources ─────────────────────────
class Reader:
    """Sequential decoder of one shot from a start frame (ffmpeg -> raw RGB)."""

    def __init__(self, shot: str, first: int, half: bool):
        self.shot, self.half = shot, half
        self.w, self.h = (W // 2, H // 2) if half else (W, H)
        self.pos = first
        self.frame: Optional[np.ndarray] = None
        flt = ['-vf', f'scale={self.w}:{self.h}:flags=neighbor'] if half else []
        self.p = subprocess.Popen([FFMPEG, '-v', 'quiet', '-ss', f'{first / FPS:.4f}', '-i', str(SHOTS / f'{shot}.mp4'),
                                   *flt, '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'],
                                  stdout=subprocess.PIPE, bufsize=self.w * self.h * 3 * 2)
        self.pos -= 1

    def get(self, index: int) -> np.ndarray:
        """Frame `index` (>= the previous request); past the end the last frame repeats."""
        while self.pos < index or self.frame is None:
            buf = self.p.stdout.read(self.w * self.h * 3)
            if len(buf) < self.w * self.h * 3:
                if self.frame is None:
                    raise RuntimeError(f'{self.shot}: no frames at {index}')
                break
            self.frame = np.frombuffer(buf, np.uint8).reshape(self.h, self.w, 3)
            self.pos += 1
        return self.frame

    def close(self):
        self.p.stdout.close()
        self.p.kill()
        self.p.wait()


def apply_view(img: np.ndarray, view: View, half: bool) -> np.ndarray:
    """Crop the source around (cx, cy) by `zoom` and scale back to full frame (NEAREST for integer zooms)."""
    z, cx, cy = view
    if abs(z - 1.0) < 1e-4 and abs(cx - W / 2) < 0.5 and abs(cy - H / 2) < 0.5:
        return img
    s = 0.5 if half else 1.0
    hh, ww = img.shape[:2]
    cw, ch = W / z * s, H / z * s
    x0 = min(max(cx * s - cw / 2, 0), ww - cw)
    y0 = min(max(cy * s - ch / 2, 0), hh - ch)
    im = Image.fromarray(img)
    integer = abs(z - round(z)) < 1e-3
    out = im.resize((ww, hh), Image.NEAREST if integer else Image.BILINEAR, box=(x0, y0, x0 + cw, y0 + ch))
    return np.asarray(out)


def src_index(seg: Seg, j: int) -> int:
    return int(round(seg.src_t * FPS + (j * seg.speed if seg.speed > 0 else 0)))


def tap_view_xy(seg: Seg, x: float, y: float, u: float) -> Tuple[float, float]:
    """Map a source-pixel tap to output pixels through the segment's view."""
    if seg.view is None:
        return x, y
    z, cx, cy = seg.view(u)
    cw, ch = W / z, H / z
    x0 = min(max(cx - cw / 2, 0), W - cw)
    y0 = min(max(cy - ch / 2, 0), H - ch)
    return (x - x0) * z, (y - y0) * z


HUD_TOP, HUD_BOTTOM = 128, 1920 - 220


def band(img: Image.Image, field_cy: float, h: int) -> Image.Image:
    """Split-screen band of height h: the game's top HUD bar (proof: mine, gold, cats) over a crop of the field
    centred on field_cy (source px)."""
    out = Image.new('RGBA', (W, h))
    out.paste(img.crop((0, 0, W, HUD_TOP)), (0, 0))
    fh = h - HUD_TOP
    y0 = int(min(max(HUD_TOP, field_cy - fh / 2), HUD_BOTTOM - fh)) // 4 * 4
    out.paste(img.crop((0, y0, W, y0 + fh)), (0, HUD_TOP))
    return out


def split_bands(top: Image.Image, bottom: Image.Image, top_cy: float, bottom_cy: float) -> Image.Image:
    out = Image.new('RGBA', (W, H))
    out.paste(band(top, top_cy, H // 2), (0, 0))
    out.paste(band(bottom, bottom_cy, H // 2), (0, H // 2))
    return out


def render_frame(ad: Ad, i: int, readers: Dict[Tuple[int, int], Reader], half: bool) -> Image.Image:
    t = i / FPS
    seg_i = next((k for k, s in enumerate(ad.segs) if s.start <= t + 1e-6 < s.end), None)
    if seg_i is None:
        frame = Image.new('RGBA', (W, H), ov.rgba('navy0'))
        seg = None
    else:
        seg = ad.segs[seg_i]
        j = i - int(round(seg.start * FPS))
        u = j / max(1, seg.frames - 1)

        def grab(slot: int, shot: str, start_t: float) -> np.ndarray:
            key = (seg_i, slot)
            idx = int(round(start_t * FPS + (j * seg.speed if seg.speed > 0 else 0)))
            if key not in readers:
                readers[key] = Reader(shot, int(round(start_t * FPS)), half)
            return readers[key].get(idx)

        a = grab(0, seg.src, seg.src_t)
        if seg.view is not None:
            a = apply_view(a, seg.view(u), half)
        img = Image.fromarray(a).convert('RGBA')
        if half:
            img = img.resize((W, H), Image.NEAREST)
        if seg.split is not None:
            src2, t2, f_top, f_bot = seg.split
            b = Image.fromarray(grab(1, src2, t2)).convert('RGBA')
            if half:
                b = b.resize((W, H), Image.NEAREST)
            img = split_bands(img, b, f_top, f_bot)
        if seg.hud_mask:
            ov.blit(img, Image.new('RGBA', (W, HUD_TOP), ov.rgba('navy0')), 0, 0)
        frame = img
        if seg.hover is not None:
            px, py = tap_view_xy(seg, seg.hover[0], seg.hover[1], u)
            ov.paw(frame, px, py, t - seg.hover[2] if len(seg.hover) > 2 else None)
        if seg.paws and seg.speed > 0:
            # The latest tap that has started its approach (0.25 s before landing) and is not yet 0.45 s old.
            live = [(seg.start + (ts - seg.src_t) / seg.speed, x, y) for (ts, x, y, _) in taps(seg.src)]
            live = [p for p in live if seg.start <= p[0] < seg.end and p[0] - 0.25 <= t]
            if live:
                to, x, y = max(live)
                if t - to < 0.45:
                    px, py = tap_view_xy(seg, x, y, u)
                    ov.paw(frame, px, py, t - to)
    for o in ad.ovs:
        if not (o.t0 <= t < o.t1):
            continue
        frame = draw_overlay(frame, o, t - o.t0, t)
    return frame


def draw_overlay(frame: Image.Image, o: Ov, lt: float, t: float) -> Image.Image:
    a = o.a
    k = o.kind
    exit_at = (o.t1 - o.t0 - 0.15) if a.get('exit', True) and o.t1 < LENGTH else None
    if k == 'caption':
        img = ov.caption(a['text'], lt, style=a.get('style', 'plank'), k=a.get('k', 6),
                         width_max=a.get('width', 952), exit_at=exit_at, icon=a.get('icon'))
        if img is not ov.EMPTY:
            ov.blit_center(frame, img, a.get('cx', W / 2), a.get('cy', 360))
    elif k == 'counter':
        img = ov.counter(a['value'], lt, prefix=a.get('prefix', ''), icon=a.get('icon', 'icon_coin'),
                         fs=a.get('fs', 3), exit_at=exit_at)
        if img is not ov.EMPTY:
            ov.blit_center(frame, img, a.get('cx', W / 2), a.get('cy', 640) - max(0.0, lt) * a.get('rise', 0))
    elif k == 'logo':
        ov.draw_logo(frame, lt, cx=a.get('cx', W / 2), cy=a.get('cy', 420), k=a.get('k', ov.LOGO_K))
    elif k == 'wrong':
        frame = ov.wrong_way(frame, lt, text=a.get('text', 'WRONG WAY!'), cy_x=a.get('cy_x', 700),
                             cy_banner=a.get('cy_banner', 960))
    elif k == 'zzz':
        frame = ov.zzz(frame, lt, origin=a.get('origin', (420, 1000)))
    elif k == 'dim':
        frame = frame.copy()
        frame.alpha_composite(Image.new('RGBA', frame.size, ov.rgba('ink0', int(255 * a.get('level', 0.5)))))
    elif k == 'end':
        frame = ov.end_card(lt, a.get('tagline', 'How deep can YOUR cats dig?'), button=a.get('button', 'PLAY FREE'),
                            taps=a.get('taps', ()))
        if a.get('legal'):
            img = ov.caption_static(a['legal'], 'dark', 1000, 4)
            ov.blit_center(frame, img, W / 2, a.get('legal_cy', 1112))
    elif k == 'flash':
        ov.flash(frame, lt, color=a.get('color', 'cream4'), dur=a.get('dur', 0.25), peak=a.get('peak', 0.85))
    elif k == 'split_labels':
        # Divider beam on the seam; each tag sits in the field of its band, under that band's HUD bar.
        beam = ov._divider()
        u = ov.ease_out(min(1.0, lt / 0.18))
        ov.blit(frame, beam, ov.snap(-W * (1.0 - u)), ov.snap(H / 2 - beam.height / 2))
        for text, style, icon, cy, delay in ((a.get('top', 'LEVEL 1'), a.get('top_style', 'blue'),
                                              a.get('top_icon', 'icon_cat'), a.get('top_cy', 330), 0.1),
                                             (a.get('bottom', 'LEVEL 100'), a.get('bottom_style', 'gold'),
                                              a.get('bottom_icon', 'icon_item_hat_legendary'),
                                              a.get('bottom_cy', H // 2 + 200), 0.35)):
            img = ov.animated(ov.split_tag(text, style, icon, 6), lt - delay, s0=0.5, peak=1.15)
            if img is not ov.EMPTY:
                ov.blit_center(frame, img, W / 2, cy)
    elif k == 'paw':
        ov.paw(frame, a['x'], a['y'], lt - a.get('tap', 0.3))
    elif k == 'burst':
        ov.paw_tap_burst(frame, a['x'], a['y'], lt, rate=a.get('rate', 7.0), duration=a.get('dur'))
    elif k == 'shake':
        dx, dy = ov.shake_offset(lt, dur=a.get('dur', 0.3), amp=a.get('amp', 20))
        frame = ov.apply_shake(frame, dx, dy)
    else:
        raise ValueError(k)
    return frame


# ───────────────────────── audio ─────────────────────────
def load_wav(path: Path, sr: int = SR) -> np.ndarray:
    x, r = sf.read(str(path), dtype='float32', always_2d=True)
    if x.shape[1] == 1:
        x = np.repeat(x, 2, axis=1)
    x = x[:, :2]
    if r != sr:
        g = math.gcd(r, sr)
        x = resample_poly(x, sr // g, r // g, axis=0).astype(np.float32)
    return x


# Game sound rebuilt from the shot's cue log, exactly as OutpostAudio.Play / Sfx.Create would play it:
# cue -> (clip family, volume, base pitch, pitch spread, min interval s, throttle group).
GAME_AUDIO = ROOT / 'Temp' / 'audio' / 'out'
CUES = {
    'Hit': ('hit', 0.22, 1.0, 0.04, 0.10, 'hit'),
    'PlayerStrike': ('hit', 0.34, 0.9, 0.05, 0.06, 'strike'),
    'TargetBroken': ('break', 0.42, 1.0, 0.05, 0.08, 'break'),
    'SectorOpened': ('gate', 0.65, 1.0, 0.0, 0.5, 'gate'),
    'TeamGathered': ('exit', 0.70, 1.0, 0.0, 0.5, 'exit'),
    'WorkerSpawned': ('meow', 0.50, 1.0, 0.05, 0.15, 'meow'),
    'Upgrade': ('upgrade', 0.50, 1.0, 0.0, 0.12, 'upgrade'),
    'Equip': ('chime', 0.30, 1.0, 0.03, 0.12, 'chime'),
    'Footstep': ('click', 0.045, 0.65, 0.0, 0.25, 'step'),
    'Deposit': ('coin', 0.28, 1.05, 0.03, 0.12, 'deposit'),
    'BagFull': ('coin', 0.10, 0.70, 0.0, 0.20, 'logistics'),
    '*': ('coin', 0.10, 1.15, 0.0, 0.20, 'logistics'),
    'ui:click': ('click', 0.35, 1.0, 0.02, 0.05, 'click'),
    'ui:open': ('open', 0.30, 1.0, 0.02, 0.08, 'open'),
    'ui:close': ('close', 0.28, 1.0, 0.02, 0.08, 'close'),
    'ui:denied': ('denied', 0.38, 1.0, 0.0, 0.20, 'denied'),
    # Factories (TZ 2.2; Sfx.cs): chain hits, the chain break, ready signal, automation jingle, pipe, warehouse, market.
    'Hit:chains': ('hit_chain', 0.20, 1.0, 0.04, 0.10, 'hit'),
    'PlayerStrike:chains': ('hit_chain', 0.30, 0.92, 0.05, 0.06, 'strike'),
    'ChainBreak': ('chain_break', 0.45, 1.0, 0.03, 0.15, 'chainbreak'),
    'FactoryReady': ('factory_ready', 0.22, 1.0, 0.0, 0.50, 'ready'),
    'FactoryAutomated': ('unlock', 0.55, 1.0, 0.0, 0.50, 'jingle'),
    'PipeSend': ('pipe_send', 0.26, 1.0, 0.04, 0.12, 'pipe'),
    'PipeSend:auto': ('pipe_send', 0.13, 1.0, 0.04, 0.12, 'pipe'),
    'GoodsArrive': ('goods_arrive', 0.24, 1.0, 0.04, 0.15, 'arrive'),
    'GoodsSold': ('goods_sell', 0.50, 1.0, 0.02, 0.20, 'sell'),
}
MAX_AUDIBLE_AUTOMATED = 3            # OutpostAudio.MaxAudibleAutomated: quieter auto pipe sends are skipped when crowded
_clips: Dict[str, List[np.ndarray]] = {}
_shot_audio: Dict[str, np.ndarray] = {}


def clips(family: str) -> List[np.ndarray]:
    if family not in _clips:
        files = sorted(GAME_AUDIO.glob(f'{family}_[0-9].ogg'))
        _clips[family] = [load_wav(f) for f in files]
    return _clips[family]


def shot_audio(shot: str) -> np.ndarray:
    """Stereo game-sound track of a whole shot (48 kHz), synced to its frames."""
    if shot in _shot_audio:
        return _shot_audio[shot]
    n = int((shot_seconds(shot) + 2.0) * SR)
    out = np.zeros((n, 2), np.float32)
    rng = np.random.default_rng(zlib.crc32(shot.encode()))
    next_ok: Dict[str, float] = {}
    last_variant: Dict[str, int] = {}
    for e in log(shot)['events']:
        if e['kind'] == 'sfx':
            d = e.get('data') or {}
            key = e['name'] if e['name'] in CUES else '*'
            if d.get('chains') and f"{e['name']}:chains" in CUES:
                key = f"{e['name']}:chains"
            elif e['name'] == 'PipeSend' and d.get('auto'):
                if d.get('Count', 0) > MAX_AUDIBLE_AUTOMATED:
                    continue
                key = 'PipeSend:auto'
        elif e['kind'] == 'ui':
            key = 'ui:' + e['name']
        else:
            continue
        fam, vol, pitch, spread, gap, group = CUES[key]
        t = e['f'] / FPS
        if t < next_ok.get(group, -1.0):
            continue
        next_ok[group] = t + gap
        variants = clips(fam)
        if not variants:
            continue
        k = int(rng.integers(len(variants)))
        if len(variants) > 1 and k == last_variant.get(fam):
            k = (k + 1) % len(variants)
        last_variant[fam] = k
        p = pitch * (1.0 + spread * float(rng.uniform(-1, 1)))
        clip = variants[k]
        if abs(p - 1.0) > 1e-3:                            # Unity pitch = varispeed: shorter and higher
            idx = np.arange(0, len(clip) - 1, p)
            clip = np.stack([np.interp(idx, np.arange(len(clip)), clip[:, c]) for c in range(2)], 1).astype(np.float32)
        o = int(round(t * SR))
        m = min(len(clip), n - o)
        if m > 0:
            out[o:o + m] += clip[:m] * vol
    _shot_audio[shot] = out
    return out


def mix_audio(ad: Ad) -> np.ndarray:
    n = int(LENGTH * SR)
    out = np.zeros((n, 2), np.float32)
    music = load_wav(MUSIC / f'{ad.music}.wav')[:n]
    out[:len(music)] += music * ad.music_gain
    for seg in ad.segs:
        if seg.audio <= 0 or seg.speed != 1.0:
            continue
        g = shot_audio(seg.src)
        a0 = int(round(seg.src_t * SR))
        o0, o1 = int(round(seg.start * SR)), int(round(seg.end * SR))
        piece = g[a0:a0 + (o1 - o0)].copy()
        if len(piece) == 0:
            continue
        fade = min(len(piece) // 2, int(0.012 * SR))
        ramp = np.linspace(0, 1, fade, dtype=np.float32)[:, None]
        piece[:fade] *= ramp
        piece[-fade:] *= ramp[::-1]
        out[o0:o0 + len(piece)] += piece * seg.audio * ad.game_gain
        if seg.split is not None:                           # the second band of a split screen is heard too
            g2 = shot_audio(seg.split[0])
            b0 = int(round(seg.split[1] * SR))
            p2 = g2[b0:b0 + (o1 - o0)]
            out[o0:o0 + len(p2)] += p2 * 0.7 * ad.game_gain
    for (t, name, gain) in ad.sfx:
        s = load_wav(SFX / f'{name}.wav')
        o0 = int(round(t * SR))
        if o0 >= n:
            continue
        s = s[:n - o0]
        out[o0:o0 + len(s)] += s * gain
    tail = int(0.35 * SR)                                   # everything fades out with the music's last bar
    out[-tail:] *= np.linspace(1, 0, tail, dtype=np.float32)[:, None] ** 2
    return out


def loudnorm(wav_in: Path, wav_out: Path) -> dict:
    """Two-pass EBU R128 to -14 LUFS / -1.5 dBTP (linear), 48 kHz."""
    pre = 'alimiter=limit=0.84:level=false:attack=3:release=60'
    r = subprocess.run([FFMPEG, '-hide_banner', '-nostats', '-i', str(wav_in), '-af',
                        f'{pre},loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json', '-f', 'null', '-'],
                       capture_output=True, text=True)
    js = r.stderr[r.stderr.rindex('{'):r.stderr.rindex('}') + 1]
    m = json.loads(js)
    af = (f"{pre},loudnorm=I=-14:TP=-1.5:LRA=11:measured_I={m['input_i']}:measured_TP={m['input_tp']}:"
          f"measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true")
    # loudnorm may still push a few peaks over full scale in linear mode: a final brick-wall limiter at -1.8 dBFS
    # keeps the true peak under -1 dBTP after AAC (the integrated loudness moves by < 0.1 LU).
    af += ',aresample=192000,alimiter=limit=0.81:level=false:attack=1:release=50,aresample=48000'
    subprocess.run([FFMPEG, '-hide_banner', '-v', 'error', '-y', '-i', str(wav_in), '-af', af, '-ar', str(SR),
                    '-c:a', 'pcm_s16le', str(wav_out)], check=True)
    return m


# ───────────────────────── render ─────────────────────────
def render(ad: Ad, stills: Sequence[float] = (), video: bool = True, half: bool = False,
           audio_only: bool = False) -> Optional[Path]:
    OUT.mkdir(parents=True, exist_ok=True)
    STILLS.mkdir(parents=True, exist_ok=True)
    tmp = ROOT / 'Temp' / 'ads' / 'render'
    tmp.mkdir(parents=True, exist_ok=True)
    readers: Dict[Tuple[int, int], Reader] = {}
    still_frames = {int(round(s * FPS)) for s in stills}
    if not video:
        for i in sorted(still_frames):
            readers.clear()
            fr = render_frame(ad, i, readers, half)
            fr.convert('RGB').save(STILLS / f'{ad.name}_{i / FPS:05.2f}.png')
            for r in readers.values():
                r.close()
        return None
    v_tmp = tmp / f'{ad.name}.video.mp4'
    if audio_only and v_tmp.exists():
        return mux(ad, v_tmp, tmp)
    enc = subprocess.Popen([FFMPEG, '-hide_banner', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                            '-s', f'{W}x{H}', '-r', str(FPS), '-i', '-',
                            '-c:v', 'libx264', '-preset', 'slow', '-crf', '15', '-tune', 'animation', '-bf', '0',
                            '-profile:v', 'high', '-pix_fmt', 'yuv420p', '-g', '60', '-r', str(FPS), str(v_tmp)],
                           stdin=subprocess.PIPE)
    active: Optional[int] = None
    for i in range(NFRAMES):
        t = i / FPS
        k = next((n for n, s in enumerate(ad.segs) if s.start <= t + 1e-6 < s.end), None)
        if k != active:                                    # close readers of the previous segment
            for key in [key for key in readers if key[0] != k]:
                readers.pop(key).close()
            active = k
        fr = render_frame(ad, i, readers, half).convert('RGB')
        if i in still_frames:
            fr.save(STILLS / f'{ad.name}_{t:05.2f}.png')
        enc.stdin.write(fr.tobytes())
        if i % 150 == 0:
            print(f'  {ad.name}: {t:5.1f}s', flush=True)
    enc.stdin.close()
    enc.wait()
    for r in readers.values():
        r.close()
    return mux(ad, v_tmp, tmp)


def mux(ad: Ad, v_tmp: Path, tmp: Path) -> Path:
    """Mix and normalise the audio, then mux it with the rendered picture."""
    a_raw, a_norm = tmp / f'{ad.name}.mix.wav', tmp / f'{ad.name}.norm.wav'
    sf.write(str(a_raw), mix_audio(ad), SR, subtype='FLOAT')
    loudnorm(a_raw, a_norm)
    out = OUT / f'{ad.name}.mp4'
    subprocess.run([FFMPEG, '-hide_banner', '-v', 'error', '-y', '-i', str(v_tmp), '-i', str(a_norm),
                    '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-ar', str(SR),
                    '-ac', '2', '-t', f'{LENGTH:.3f}', '-movflags', '+faststart', '-use_editlist', '0', str(out)],
                   check=True)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ads', nargs='+')
    ap.add_argument('--stills', default='')
    ap.add_argument('--no-video', action='store_true')
    ap.add_argument('--half', action='store_true', help='decode footage at half size (fast previews)')
    ap.add_argument('--audio-only', action='store_true', help='re-mix the sound onto the last rendered picture')
    args = ap.parse_args()
    import edl
    stills = [float(s) for s in args.stills.split(',') if s.strip()]
    for name in args.ads:
        ad = edl.ADS[name]()
        out = render(ad, stills, video=not args.no_video, half=args.half, audio_only=args.audio_only)
        print(name, '->', out or STILLS)


if __name__ == '__main__':
    main()
