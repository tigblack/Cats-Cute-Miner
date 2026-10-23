#!/usr/bin/env python3
"""
Атмосфера шахты Cat Miners (синтез кодом, без сэмплов): фон пещеры и редкие события.

  amb_bed        45 с, стерео, бесшовный цикл — гул пещеры: низкий «воздух», едва слышный тон глубины, медленное дыхание.
  amb_creak_0..2 скрип деревянной крепи (трение «прилип-сорвался» через резонансы доски), 0,6–1,1 с.
  amb_drip_0..3  капля в пещере с отзвуком.
  amb_machine_0..1 далёкий механизм: глухие удары лебёдки и звяканье цепи.
  amb_pebbles_0..1 осыпаются мелкие камешки.

События короткие, моно (панораму задаёт игра: Runtime/Audio/Ambience.cs), громкость нормирована к −22 дБFS RMS —
атмосфера остаётся фоном под музыкой и звуками.

Запуск на хабе:  Tools/.venv/bin/python Tools/Audio/ambience.py --out Temp/audio/amb
"""
import argparse
import os
import sys

import numpy as np
import soundfile as sf
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
TAU = 2 * np.pi


def lp(x, f, order=2):
    return sosfilt(butter(order, f, 'lowpass', fs=SR, output='sos'), x)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, 'highpass', fs=SR, output='sos'), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'bandpass', fs=SR, output='sos'), x)


def cave_ir(seconds, seed, t60=2.2):
    rng = np.random.default_rng(seed)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = lp(rng.standard_normal(n), 3500) * np.exp(-6.91 * t / t60)
    ir[:int(0.015 * SR)] = 0
    ir[0] = 1.0
    return ir / np.sqrt(np.sum(ir ** 2))


def norm(x, rms_db=-22.0, peak_db=-3.0):
    win = int(0.02 * SR)
    m = x if x.ndim == 1 else x.mean(axis=0)
    e = np.sqrt(np.convolve(m ** 2, np.ones(win) / win, 'same'))
    act = e > e.max() * 0.1
    g = 10 ** (rms_db / 20) / max(np.sqrt(np.mean(m[act] ** 2)), 1e-9)
    peak = np.abs(x).max() * g
    if peak > 10 ** (peak_db / 20):
        g *= 10 ** (peak_db / 20) / peak
    return x * g


def fades(y, a_ms=3.0, r_ms=40.0):
    na, nr = int(a_ms * SR / 1000), int(r_ms * SR / 1000)
    y[:na] *= np.linspace(0, 1, na)
    y[-nr:] *= np.linspace(1, 0, nr)
    return y


def bed(seed=1, seconds=45.0, xfade=5.0):
    rng = np.random.default_rng(seed)
    n = int((seconds + xfade) * SR)
    t = np.arange(n) / SR
    out = np.zeros((2, n))
    for c in range(2):
        brown = np.cumsum(rng.standard_normal(n))
        brown = hp(brown, 20, 2)
        low = lp(brown, 220, 2)
        low /= np.abs(low).max()
        air = bp(rng.standard_normal(n), 500, 1400, 2) * 0.05
        breath = 0.75 + 0.25 * np.sin(TAU * (0.031 + 0.007 * c) * t + rng.uniform(0, TAU))
        hum = (0.05 * np.sin(TAU * 55 * t + c) + 0.03 * np.sin(TAU * 110.4 * t)) * (0.6 + 0.4 * np.sin(TAU * 0.05 * t))
        out[c] = (low * 0.8 + air) * breath + hum
    # бесшовный цикл: последние xfade секунд перетекают в первые (равная мощность)
    k = int(xfade * SR)
    L = int(seconds * SR)
    w = np.sin(np.linspace(0, np.pi / 2, k))
    loop = out[:, :L].copy()
    loop[:, :k] = loop[:, :k] * w + out[:, L:L + k] * w[::-1]
    return norm(loop, -26.0, -6.0)


def creak(seed):
    rng = np.random.default_rng(seed)
    dur = rng.uniform(0.6, 1.1)
    n = int(dur * SR)
    t = np.arange(n) / SR
    rate = 70 + 90 * np.abs(np.sin(np.pi * t / dur * rng.uniform(0.8, 1.6))) + rng.uniform(-10, 10)
    phase = np.cumsum(rate) / SR
    pulses = np.zeros(n)
    idx = np.nonzero(np.diff(np.floor(phase)) > 0)[0]
    pulses[idx] = rng.uniform(0.6, 1.0, len(idx))
    exc = fftconvolve(pulses, np.exp(-np.arange(int(0.004 * SR)) / (0.0008 * SR)))[:n]
    body = sum(g * bp(exc, f * 0.94, f * 1.06, 2) for f, g in ((380, 1.0), (760, 0.6), (1240, 0.35), (2100, 0.15)))
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 0.7
    y = lp(body * env, 3500)
    return norm(fades(y, 8, 80), -24.0)


def drip(seed):
    rng = np.random.default_rng(seed)
    f0 = rng.uniform(700, 1150)
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.7 * (1 - np.exp(-t / 0.01)))
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / 0.045)
    y[:int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
    wet = fftconvolve(y, cave_ir(2.0, seed + 10, 1.8))[:int(2.0 * SR)]
    out = np.zeros(int(2.0 * SR))
    out[:n] += y
    out += 0.45 * wet
    return norm(fades(lp(out, 6000), 1, 300), -26.0)


def machine(seed):
    rng = np.random.default_rng(seed)
    total = int(2.6 * SR)
    out = np.zeros(total)
    t0 = 0.0
    for k in range(int(rng.integers(2, 4))):
        i = int(t0 * SR)
        n = int(0.5 * SR)
        t = np.arange(n) / SR
        thump = np.sin(TAU * (62 + 30 * np.exp(-t / 0.03)) * t) * np.exp(-t / 0.12)
        thump += lp(rng.standard_normal(n), 400) * np.exp(-t / 0.05) * 0.3
        clank = sum(np.sin(TAU * f * t + rng.uniform(0, TAU)) * np.exp(-t / d) * g
                    for f, d, g in ((420, 0.18, 0.3), (1130, 0.09, 0.18), (1870, 0.05, 0.08)))
        y = thump + clank * rng.uniform(0.5, 1.0)
        j = min(total, i + n)
        out[i:j] += y[:j - i]
        t0 += rng.uniform(0.45, 0.75)
    out = lp(out, 1800)                      # далеко: верх приглушён
    wet = fftconvolve(out, cave_ir(2.2, seed + 20, 2.0))[:total]
    return norm(fades(out * 0.6 + wet * 0.8, 3, 400), -26.0)


def pebbles(seed):
    rng = np.random.default_rng(seed)
    total = int(1.6 * SR)
    out = np.zeros(total)
    t = 0.05
    count = int(rng.integers(9, 16))
    for k in range(count):
        n = int(0.03 * SR)
        click = bp(rng.standard_normal(n), 1200, 3200, 2) * np.exp(-np.arange(n) / (0.006 * SR))
        i = int(t * SR)
        g = rng.uniform(0.3, 1.0) * (1 - 0.5 * k / count)
        out[i:i + n] += click[:max(0, min(n, total - i))] * g
        t += rng.uniform(0.02, 0.12) * (1 + k / count)
        if t > 1.3:
            break
    wet = fftconvolve(out, cave_ir(1.6, seed + 30, 1.4))[:total]
    return norm(fades(lp(out + 0.5 * wet, 5000), 1, 200), -27.0)


def write(path, x):
    data = (x.T if x.ndim == 2 else x).astype(np.float32)
    ch = 2 if x.ndim == 2 else 1
    with sf.SoundFile(path, 'w', SR, ch, format='OGG', subtype='VORBIS', compression_level=0.4) as f:
        for i in range(0, len(data), 32768):
            f.write(data[i:i + 32768])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    items = {'amb_bed': bed()}
    for i in range(3):
        items[f'amb_creak_{i}'] = creak(100 + i)
    for i in range(4):
        items[f'amb_drip_{i}'] = drip(200 + i)
    for i in range(2):
        items[f'amb_machine_{i}'] = machine(300 + i)
    for i in range(2):
        items[f'amb_pebbles_{i}'] = pebbles(400 + i)
    for name, x in items.items():
        path = os.path.join(a.out, name + '.ogg')
        write(path, x)
        n = x.shape[-1]
        m = x if x.ndim == 1 else x.mean(axis=0)
        print(f'{name:16s} {n / SR:5.2f} с  пик {20 * np.log10(np.abs(x).max() + 1e-12):5.1f} дБ  '
              f'RMS {20 * np.log10(np.sqrt(np.mean(m ** 2)) + 1e-12):5.1f} дБ  {os.path.getsize(path) // 1024} КБ')
    return 0


if __name__ == '__main__':
    sys.exit(main())
