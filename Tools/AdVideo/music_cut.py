"""Re-arranges the 45 s ad music beds (ad_music.py: 24 bars of 1.875 s at 128 BPM) into 30 s and 60 s versions by
splicing whole bars, so every section keeps its own sound: hook, verse, build, drop, breakdown, final hit.

Each splice lands on a downbeat with a 12 ms equal-power crossfade from the previous bar's end into the next
section's own pickup, so nothing clicks and the new section starts exactly on the bar line.

  t1_progress_30, t3_chest_30 (16 bars): hook 1-2, verse 3-6, build 11-12, drop 13-18, final hit 23-24
  t1_progress_60 (32 bars): hook, verse, build, drop, breakdown (bars 1-22), build 11-12, drop 13-18, final 23-24

Usage (project root, hub): Tools/.venv/bin/python -B Tools/AdVideo/music_cut.py
Reads and writes Temp/ads/music/<track>.wav (48 kHz stereo, 24-bit).
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[2]
MUSIC = ROOT / 'Temp' / 'ads' / 'music'
SR = 48000
BAR = int(round(1.875 * SR))                    # 90,000 samples
XFADE = int(0.012 * SR)

# output -> (source bed, [(first bar, end bar), ...]) with 0-based bars, end exclusive
CUTS = {
    't1_progress_30': ('t1_progress', [(0, 6), (10, 18), (22, 24)]),
    't3_chest_30': ('t3_chest', [(0, 6), (10, 18), (22, 24)]),
    't1_progress_60': ('t1_progress', [(0, 22), (10, 18), (22, 24)]),
}


def splice(x: np.ndarray, pieces) -> np.ndarray:
    out = []
    ramp = np.sin(np.linspace(0.0, np.pi / 2, XFADE))[:, None]
    for k, (b0, b1) in enumerate(pieces):
        piece = x[b0 * BAR:b1 * BAR].copy()
        if k > 0:
            pickup = x[b0 * BAR - XFADE:b0 * BAR]
            prev = out[-1]
            prev[-XFADE:] = prev[-XFADE:] * ramp[::-1] + pickup * ramp
        out.append(piece)
    return np.concatenate(out)


def main():
    for name, (src, pieces) in CUTS.items():
        x, sr = sf.read(str(MUSIC / f'{src}.wav'), always_2d=True)
        assert sr == SR and len(x) == 24 * BAR, (src, sr, len(x))
        y = splice(x, pieces)
        bars = sum(b1 - b0 for b0, b1 in pieces)
        assert len(y) == bars * BAR
        sf.write(str(MUSIC / f'{name}.wav'), y, SR, subtype='PCM_24')
        print(f'{name}: {bars} bars = {len(y) / SR:.3f} s from {src}')


if __name__ == '__main__':
    main()
