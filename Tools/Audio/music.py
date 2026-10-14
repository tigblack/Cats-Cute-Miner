#!/usr/bin/env python3
"""
Оригинальная фоновая музыка Cat Miners: синтез инструментов и аранжировка кодом (без сэмплов и сторонних материалов).

Треки (Docs/Visual/ART_DIRECTION.md, раздел 9; Docs/Audio/SOUNDS.md, «Музыка»):
  music_warm    «Тёплая штольня»     — 82 BPM, фа мажор, 48 тактов ≈ 2:20. Маримба, щипковые, челеста, тёплый пэд,
                                        мягкий бас, редкая перкуссия (шейкер, мягкая бочка, деревянный щелчок).
  music_crystal «Кристальная глубина» — 72 BPM, ре лидийский, 44 такта ≈ 2:27. FM-колокольчики, стеклянный пэд,
                                        челеста-искры, арфа, глубокий бас, капли.

Каждый трек — бесшовный цикл: рендерится цикл и хвост (отзвуки, реверберация), хвост складывается в начало. Последний
такт гармонически ведёт к первому. Мастер — стерео 44,1 кГц; громкость ≈ −17 дБFS RMS, пик не выше −1 дБFS, верх
мягко срезан (без резких высоких частот).

Запуск на хабе:  Tools/.venv/bin/python Tools/Audio/music.py --out Temp/audio/music [warm] [crystal]
Нужны numpy, scipy, soundfile (venv Tools/.venv). Результат детерминирован (фиксированные зёрна).
"""
import argparse
import os
import sys

import numpy as np
import soundfile as sf
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
TAU = 2 * np.pi


def hz(m: float) -> float:
    return 440.0 * 2 ** ((m - 69) / 12.0)


def lp(x, f, order=2):
    return sosfilt(butter(order, f, 'lowpass', fs=SR, output='sos'), x)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, 'highpass', fs=SR, output='sos'), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'bandpass', fs=SR, output='sos'), x)


def ramp_in(y, ms):
    n = max(1, int(ms * SR / 1000))
    n = min(n, len(y))
    y[:n] *= np.linspace(0.0, 1.0, n)
    return y


def fade_out(y, seconds):
    n = min(len(y), max(1, int(seconds * SR)))
    y[-n:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, n)))
    return y


# ───────────────────────── инструменты (моно, амплитуда ≈ 1 при vel = 1) ─────────────────────────

def marimba(f, dur, vel, rng):
    """Маримба: брусок с обертонами 1 : 3,93 : 9,03 (верхние гаснут быстро), мягкий удар колотушки."""
    tau = float(np.clip(0.9 * (440.0 / f) ** 0.4, 0.35, 1.7))
    n = int((tau * 4.5) * SR)
    t = np.arange(n) / SR
    y = (np.sin(TAU * f * t) * np.exp(-t / tau)
         + 0.20 * np.sin(TAU * f * 3.93 * t + 0.3) * np.exp(-t / (tau * 0.16))
         + 0.05 * np.sin(TAU * f * 9.03 * t + 1.1) * np.exp(-t / (tau * 0.05)))
    click = lp(rng.standard_normal(int(0.012 * SR)), 2500) * np.exp(-np.arange(int(0.012 * SR)) / (0.003 * SR))
    y[:len(click)] += 0.12 * click
    return ramp_in(y, 1.5) * vel


def pluck(f, dur, vel, rng, decay=0.55, bright=1.0):
    """Щипок (пиццикато, арфа): гармоники 1/n^1.3, верхние гаснут быстрее; decay — длина звона."""
    tau = float(np.clip(decay * (220.0 / f) ** 0.3, 0.12, 2.2))
    n = int(tau * 5 * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    for k in range(1, 11):
        if f * k > 6500:
            break
        amp = 1.0 / k ** (1.3 / bright)
        y += amp * np.sin(TAU * f * k * t * (1 + 0.0004 * k) + rng.uniform(0, TAU)) * np.exp(-t * (1.0 / tau) * (1 + 0.55 * (k - 1)))
    return ramp_in(y * 0.55, 1.0) * vel


def celesta(f, dur, vel, rng):
    """Челеста: колокольчик с обертонами 1, 2, 3, 4,16; мягкий молоточек."""
    tau = float(np.clip(1.2 * (523.0 / f) ** 0.35, 0.4, 1.8))
    n = int(tau * 5 * SR)
    t = np.arange(n) / SR
    y = (np.sin(TAU * f * t) * np.exp(-t / tau)
         + 0.28 * np.sin(TAU * 2 * f * t + 0.5) * np.exp(-t / (tau * 0.45))
         + 0.07 * np.sin(TAU * 3 * f * t + 1.0) * np.exp(-t / (tau * 0.25))
         + 0.04 * np.sin(TAU * 4.16 * f * t + 2.0) * np.exp(-t / (tau * 0.15)))
    return ramp_in(y * 0.7, 2.5) * vel


def pad(f, dur, vel, rng, attack=0.6, release=1.2, bright=2.2):
    """Тёплый пэд: три слегка расстроенных голоса, гармоники 1/n с мягким срезом, медленные атака и затухание."""
    n = int((dur + release) * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    vib = 1 + 0.0015 * np.sin(TAU * 4.3 * t) * np.clip((t - 0.8) / 1.0, 0, 1)
    for cents in (-7.0, 0.0, 6.0):
        fd = f * 2 ** (cents / 1200.0)
        ph = TAU * fd * np.cumsum(vib) / SR
        for k in range(1, 9):
            if fd * k > 5000:
                break
            y += (1.0 / k) * np.exp(-(k - 1) / bright) * np.sin(k * ph + rng.uniform(0, TAU))
    env = np.minimum(1.0, t / attack)
    rel = t > dur
    env[rel] *= np.exp(-(t[rel] - dur) / (release / 4.0))
    return y * env * 0.16 * vel


def glass(f, dur, vel, rng, attack=0.18, release=1.4):
    """Стеклянный тон: синус с октавой и дуодецимой, медленная атака, лёгкое тремоло."""
    n = int((dur + release) * SR)
    t = np.arange(n) / SR
    y = np.sin(TAU * f * t) + 0.28 * np.sin(TAU * 2 * f * t + 0.4) + 0.06 * np.sin(TAU * 3 * f * t + 0.9)
    env = np.minimum(1.0, t / attack) * (1 + 0.05 * np.sin(TAU * 4.8 * t))
    env *= np.exp(-t / 6.0)
    rel = t > dur
    env[rel] *= np.exp(-(t[rel] - dur) / (release / 4.0))
    return y * env * 0.55 * vel


def bell(f, dur, vel, rng):
    """FM-колокольчик: модулятор 3,5 : 1 (неровный, «стеклянный» спектр), индекс модуляции быстро гаснет."""
    tau = float(np.clip(2.4 * (660.0 / f) ** 0.3, 1.0, 3.2))
    n = int(tau * 4 * SR)
    t = np.arange(n) / SR
    index = 1.6 * np.exp(-t / 0.5) + 0.25
    y = np.sin(TAU * f * t + index * np.sin(TAU * 3.5 * f * t))
    y += 0.25 * np.sin(TAU * 2.0 * f * t) * np.exp(-t / (tau * 0.4))
    return ramp_in(y * np.exp(-t / tau) * 0.6, 3.0) * vel


def bass(f, dur, vel, rng, attack=0.012, release=0.18):
    """Мягкий бас: синус с 2-й и 3-й гармониками, затухание к 0,7, короткое отпускание."""
    n = int((dur + release) * SR)
    t = np.arange(n) / SR
    y = np.sin(TAU * f * t) + 0.22 * np.sin(TAU * 2 * f * t) + 0.06 * np.sin(TAU * 3 * f * t)
    env = np.minimum(1.0, t / attack) * (0.7 + 0.3 * np.exp(-t / 0.35))
    rel = t > dur
    env[rel] *= np.exp(-(t[rel] - dur) / (release / 4.0))
    return y * env * 0.75 * vel


def shaker(vel, rng):
    n = int(0.09 * SR)
    t = np.arange(n) / SR
    y = bp(rng.standard_normal(n), 2200, 5200)
    env = np.minimum(1.0, t / 0.012) * np.exp(-t / 0.025)
    return y * env * 0.35 * vel


def kick(vel, rng):
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    f = 52 + 48 * np.exp(-t / 0.03)
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / 0.14)
    return ramp_in(y, 2.0) * 0.9 * vel


def rim(vel, rng):
    """Деревянный щелчок (woodblock): два затухающих тона и щелчок."""
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    y = (np.sin(TAU * 820 * t) + 0.6 * np.sin(TAU * 1310 * t)) * np.exp(-t / 0.022)
    y[:int(0.004 * SR)] += lp(rng.standard_normal(int(0.004 * SR)), 3000) * 0.3
    return ramp_in(y * 0.5, 0.5) * vel


def drop(vel, rng):
    """Капля: короткий тон с быстрым подъёмом высоты и мягким звоном."""
    f0 = rng.uniform(900, 1300)
    n = int(0.22 * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.55 * (1 - np.exp(-t / 0.012)))
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / 0.05)
    return ramp_in(y * 0.5, 1.0) * vel


INSTR = {'marimba': marimba, 'pluck': pluck, 'harp': lambda f, d, v, r: pluck(f, d, v, r, decay=1.1, bright=1.2),
         'celesta': celesta, 'pad': pad, 'glasspad': lambda f, d, v, r: pad(f, d, v, r, attack=1.2, release=2.0, bright=1.6),
         'glass': glass, 'bell': bell, 'bass': bass, 'subbass': lambda f, d, v, r: bass(f, d, v, r, attack=0.12, release=0.6)}
PERC = {'shaker': shaker, 'kick': kick, 'rim': rim, 'drop': drop}


# ───────────────────────── реверберация и сведение ─────────────────────────

def reverb_ir(seconds, seed, decay_low=2.4, decay_mid=1.9, decay_high=0.9, predelay=0.022):
    """Синтетический отклик зала: шум по трём полосам с разным временем затухания (верх гаснет быстрее), ранние отражения."""
    rng = np.random.default_rng(seed)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = np.zeros(n)
    for (lo, hi), t60 in (((40, 500), decay_low), ((500, 3000), decay_mid), ((3000, 9000), decay_high)):
        band = bp(rng.standard_normal(n), lo, hi, 2)
        ir += band * np.exp(-6.91 * t / t60)
    pre = int(predelay * SR)
    ir = np.concatenate([np.zeros(pre), ir])[:n]
    for d, g in ((0.011, 0.5), (0.019, 0.35), (0.029, 0.28), (0.041, 0.2)):
        k = int(d * SR)
        ir[k] += g * (1 if rng.random() < 0.5 else -1)
    return ir / np.sqrt(np.sum(ir ** 2))


class Mix:
    """Сведение: сухая стереошина и шина посыла на реверберацию."""

    def __init__(self, seconds, seed):
        self.n = int(seconds * SR)
        self.dry = np.zeros((2, self.n))
        self.send = np.zeros((2, self.n))
        self.rng = np.random.default_rng(seed)

    def add(self, y, start, pan=0.0, gain=1.0, send=0.25):
        i = int(round(start * SR))
        if i >= self.n or len(y) == 0:
            return
        j = min(self.n, i + len(y))
        y = y[:j - i] * gain
        a = (pan + 1) * np.pi / 4          # равная мощность
        l, r = np.cos(a), np.sin(a)
        self.dry[0, i:j] += y * l
        self.dry[1, i:j] += y * r
        self.send[0, i:j] += y * l * send
        self.send[1, i:j] += y * r * send

    def render(self, reverb_seconds=3.2, wet=0.55, seed=7, **ir_kw):
        irl = reverb_ir(reverb_seconds, seed, **ir_kw)
        irr = reverb_ir(reverb_seconds, seed + 1, **ir_kw)
        wl = fftconvolve(self.send[0], irl)[:self.n]
        wr = fftconvolve(self.send[1], irr)[:self.n]
        out = self.dry + wet * np.vstack([wl, wr])
        # верх мягко срезан, низ ниже 30 Гц убран
        out = np.vstack([lp(hp(out[c], 30, 2), 11000, 2) for c in range(2)])
        return out


def master(x, rms_db=-17.0, peak_db=-1.0):
    """Громкость по RMS звучащей части, мягкое ограничение пиков (tanh выше −4 дБ)."""
    mono = x.mean(axis=0)
    win = int(0.05 * SR)
    e = np.sqrt(np.convolve(mono ** 2, np.ones(win) / win, 'same'))
    act = e > e.max() * 0.05
    rms = np.sqrt(np.mean(mono[act] ** 2))
    x = x * (10 ** (rms_db / 20) / max(rms, 1e-9))
    knee = 10 ** (-4 / 20)
    over = np.abs(x) > knee
    x[over] = np.sign(x[over]) * (knee + (1 - knee) * np.tanh((np.abs(x[over]) - knee) / (1 - knee)))
    peak = np.abs(x).max()
    cap = 10 ** (peak_db / 20)
    if peak > cap:
        x *= cap / peak
    return x


def fold_loop(x, loop_n):
    """Бесшовный цикл: хвост после конца цикла (отзвуки нот и реверберация) складывается в начало."""
    out = x[:, :loop_n].copy()
    tail = x[:, loop_n:]
    k = min(tail.shape[1], loop_n)
    out[:, :k] += tail[:, :k]
    return out


# ───────────────────────── партитура ─────────────────────────

class Score:
    """Ноты в тактах и долях: add(bar, beat, dur, midi, inst, vel, pan, send). Гуманизация: время ±6 мс, громкость ±7 %."""

    def __init__(self, bpm, bars, seed):
        self.beat = 60.0 / bpm
        self.bars = bars
        self.length = bars * 4 * self.beat
        self.notes = []
        self.rng = np.random.default_rng(seed)

    def add(self, bar, beat, dur, midi, inst, vel, pan=0.0, send=0.25, human=True):
        t = ((bar - 1) * 4 + beat) * self.beat
        if human:
            t += self.rng.uniform(-0.006, 0.006)
            vel *= self.rng.uniform(0.93, 1.07)
        self.notes.append((max(0.0, t), dur * self.beat, midi, inst, vel, pan, send))

    def melody(self, bar, notes, inst, vel, pan=0.0, send=0.3, transpose=0):
        for beat, dur, midi in notes:
            self.add(bar, beat, dur, midi + transpose, inst, vel * (1.06 if beat in (0, 2) else 1.0), pan, send)

    def render(self, tail=6.0, seed=11, **rev):
        mix = Mix(self.length + tail, seed)
        for t, dur, midi, inst, vel, pan, send in self.notes:
            if inst in PERC:
                y = PERC[inst](vel, mix.rng)
            else:
                y = INSTR[inst](hz(midi), dur, vel, mix.rng)
            mix.add(y, t, pan, 1.0, send)
        x = mix.render(**rev)
        return fold_loop(x, int(round(self.length * SR)))


# ───────────────────────── «Тёплая штольня» ─────────────────────────
# Аккорды: голоса пэда, корень баса, четыре звука арпеджио (маримба играет их на октаву ниже).
WARM_CHORDS = {
    'Fmaj7': ([53, 57, 60, 64], 41, [65, 69, 72, 76]),
    'Dm7': ([50, 57, 60, 65], 38, [62, 65, 69, 72]),
    'Bbmaj7': ([58, 62, 65, 69], 46, [62, 65, 69, 70]),
    'C': ([55, 60, 64, 67], 48, [60, 64, 67, 72]),
    'Csus4': ([55, 60, 65, 67], 48, [60, 65, 67, 72]),
    'C7': ([55, 58, 64, 67], 48, [60, 64, 67, 70]),
    'Am7': ([52, 57, 60, 67], 45, [64, 67, 69, 72]),
    'Gm7': ([53, 58, 62, 67], 43, [62, 65, 67, 70]),
    'Dm': ([50, 57, 62, 65], 38, [62, 65, 69, 74]),
    'Bb': ([53, 58, 62, 65], 46, [62, 65, 70, 74]),
    'F': ([53, 57, 60, 65], 41, [65, 69, 72, 77]),
}
WARM_FORM = (
    ['Fmaj7', 'Dm7', 'Bbmaj7', 'Csus4'] +                                   # 1–4 вступление
    ['Fmaj7', 'Dm7', 'Bbmaj7', 'C', 'Fmaj7', 'Am7', 'Gm7', 'C7'] +          # 5–12 тема A (маримба)
    ['Fmaj7', 'Dm7', 'Bbmaj7', 'C', 'Fmaj7', 'Am7', 'Gm7', 'C7'] +          # 13–20 тема A' (челеста)
    ['Dm', 'Bb', 'F', 'C', 'Dm', 'Bb', 'Gm7', 'C'] +                        # 21–28 B (пиццикато)
    ['Bbmaj7', 'Am7', 'Gm7', 'Csus4'] +                                     # 29–32 разрежение
    ['Fmaj7', 'Dm7', 'Bbmaj7', 'C', 'Fmaj7', 'Am7', 'Gm7', 'C7'] +          # 33–40 тема A'' (маримба + челеста)
    ['Fmaj7', 'Dm7', 'Bbmaj7', 'C', 'Fmaj7', 'Dm7', 'Bbmaj7', 'Csus4']      # 41–48 завершение, ведёт к началу
)
THEME_A = [
    [(0, 1.5, 69), (1.5, 0.5, 72), (2, 1, 69), (3, 1, 67)],
    [(0, 1, 65), (1, 1, 69), (2, 1.5, 74), (3.5, 0.5, 72)],
    [(0, 1, 74), (1, 0.5, 72), (1.5, 0.5, 70), (2, 1, 69), (3, 1, 65)],
    [(0, 1.5, 67), (1.5, 0.5, 69), (2, 2, 67)],
    [(0, 1.5, 69), (1.5, 0.5, 72), (2, 1, 77), (3, 1, 76)],
    [(0, 1, 74), (1, 1, 72), (2, 1.5, 69), (3.5, 0.5, 67)],
    [(0, 1, 70), (1, 0.5, 69), (1.5, 0.5, 67), (2, 1, 65), (3, 1, 67)],
    [(0, 3, 65)],
]
THEME_A2 = [
    [(0, 1, 81), (1, 0.5, 84), (1.5, 0.5, 81), (2, 1, 79), (3, 1, 77)],
    [(0, 1.5, 77), (1.5, 0.5, 81), (2, 2, 86)],
    [(0, 1, 86), (1, 0.5, 84), (1.5, 0.5, 82), (2, 1, 81), (3, 1, 77)],
    [(0, 1, 79), (1, 1, 76), (2, 2, 79)],
    [(0, 1.5, 81), (1.5, 0.5, 84), (2, 1, 88), (3, 1, 86)],
    [(0, 1, 84), (1, 1, 81), (2, 1.5, 79), (3.5, 0.5, 76)],
    [(0, 1, 82), (1, 0.5, 81), (1.5, 0.5, 79), (2, 1, 77), (3, 1, 76)],
    [(0, 2, 77), (2, 1, 79), (3, 1, 76)],
]
THEME_B = [
    [(0, .5, 74), (.5, .5, 77), (1, 1, 81), (2, .5, 79), (2.5, .5, 77), (3, 1, 76)],
    [(0, .5, 74), (.5, .5, 77), (1, 1, 82), (2, .5, 81), (2.5, .5, 79), (3, 1, 77)],
    [(0, .5, 72), (.5, .5, 77), (1, 1.5, 81), (2.5, .5, 79), (3, 1, 77)],
    [(0, 1.5, 76), (1.5, .5, 74), (2, 2, 72)],
    [(0, .5, 74), (.5, .5, 77), (1, 1, 81), (2, .5, 79), (2.5, .5, 77), (3, 1, 74)],
    [(0, .5, 74), (.5, .5, 77), (1, 1, 82), (2, 1, 84), (3, 1, 82)],
    [(0, 1, 81), (1, 1, 79), (2, 1, 77), (3, 1, 74)],
    [(0, 2, 76), (2, 2, 72)],
]
BREAK = [
    [(0, 1, 77), (1, 1, 81), (2, 2, 84)],
    [(0, 1, 76), (1, 1, 79), (2, 2, 84)],
    [(0, 1, 74), (1, 1, 77), (2, 2, 82)],
    [(0, 2, 79)],
]
OUTRO_TAG = {45: [(0, 1, 84), (1, 1, 81), (2, 2, 77)], 46: [(0, 1, 79), (1, 1, 76), (2, 2, 74)], 47: [(0, 2, 77)]}


def warm():
    s = Score(82, 48, seed=1)
    ost_a = [0, 1, 2, 1, 3, 1, 2, 1]
    ost_b = [0, 2, 1, 2, 3, 2, 1, 2]
    ost_vel = [0.42, 0.28, 0.34, 0.26, 0.38, 0.27, 0.33, 0.26]
    for i, name in enumerate(WARM_FORM):
        bar = i + 1
        pad_notes, root, arp = WARM_CHORDS[name]
        section = ('intro' if bar <= 4 else 'A' if bar <= 12 else 'A2' if bar <= 20 else 'B' if bar <= 28
                   else 'break' if bar <= 32 else 'A3' if bar <= 40 else 'outro')
        # пэд: аккорд на весь такт (в разрежении — громче, это главный голос)
        pv = 0.42 if section == 'break' else 0.3
        for k, m in enumerate(pad_notes):
            s.add(bar, 0, 4.05, m, 'pad', pv, pan=(-0.35 + 0.23 * k), send=0.45, human=False)
        # бас: корень на 1, квинта или корень на 3; в разрежении — целые ноты
        if section == 'break':
            s.add(bar, 0, 3.8, root, 'bass', 0.5, send=0.1)
        else:
            s.add(bar, 0, 1.5, root, 'bass', 0.62, send=0.08)
            s.add(bar, 2, 1.3, root + (7 if bar % 2 else 0), 'bass', 0.5, send=0.08)
            if section in ('B', 'A3') and bar % 2 == 0:
                s.add(bar, 3.5, 0.45, root + 12, 'bass', 0.32, send=0.08)
        # остинато маримбы восьмыми (на октаву ниже арпеджио), кроме разрежения
        if section != 'break':
            pattern = ost_b if section == 'B' else ost_a
            soft = 0.75 if section in ('intro', 'outro') else 1.0
            for k in range(8):
                s.add(bar, k * 0.5, 0.5, arp[pattern[k]] - 12, 'marimba', ost_vel[k] * soft * 0.8, pan=0.25, send=0.22)
        # мелодии
        if section == 'A':
            s.melody(bar, THEME_A[bar - 5], 'marimba', 0.85, pan=-0.1, send=0.3)
        elif section == 'A2':
            s.melody(bar, THEME_A2[bar - 13], 'celesta', 0.62, pan=0.05, send=0.4)
            # маримба подголоском — секстой ниже на сильных долях
            for beat, dur, m in THEME_A2[bar - 13]:
                if beat in (0, 2):
                    s.add(bar, beat, dur, m - 21, 'marimba', 0.32, pan=-0.3, send=0.25)
        elif section == 'B':
            s.melody(bar, THEME_B[bar - 21], 'pluck', 0.8, pan=0.15, send=0.32)
            if bar in (24, 28):
                s.melody(bar, THEME_B[bar - 21], 'celesta', 0.25, pan=-0.2, send=0.5, transpose=12)
        elif section == 'break':
            s.melody(bar, BREAK[bar - 29], 'celesta', 0.55, pan=0.1, send=0.55)
        elif section == 'A3':
            s.melody(bar, THEME_A[bar - 33], 'marimba', 0.8, pan=-0.1, send=0.3)
            if bar >= 37:
                s.melody(bar, THEME_A[bar - 33], 'celesta', 0.3, pan=0.25, send=0.45, transpose=12)
            # подголосок щипком на слабых долях
            for k, beat in enumerate((1.5, 2.5, 3.5)):
                s.add(bar, beat, 0.4, arp[(k + bar) % 4], 'pluck', 0.28, pan=0.4, send=0.3)
        elif section == 'outro' and bar in OUTRO_TAG:
            s.melody(bar, OUTRO_TAG[bar], 'celesta', 0.5, pan=0.1, send=0.5)
        # перкуссия: редкая и тихая
        if section in ('A2', 'B', 'A3'):
            for beat in (0.5, 1.5, 2.5, 3.5):
                s.add(bar, beat, 0.1, 0, 'shaker', 0.2 if beat in (1.5, 3.5) else 0.13, pan=0.35, send=0.12)
            s.add(bar, 0, 0.3, 0, 'kick', 0.32, send=0.05)
            if section == 'B':
                s.add(bar, 2, 0.3, 0, 'kick', 0.2, send=0.05)
            if section in ('B', 'A3') and bar % 2 == 0:
                s.add(bar, 3, 0.1, 0, 'rim', 0.22, pan=-0.3, send=0.25)
        elif section == 'outro' and bar <= 44:
            for beat in (1.5, 3.5):
                s.add(bar, beat, 0.1, 0, 'shaker', 0.1, pan=0.35, send=0.12)
    return s.render(reverb_seconds=3.0, wet=0.5, decay_low=2.2, decay_mid=1.7, decay_high=0.8)


# ───────────────────────── «Кристальная глубина» ─────────────────────────
CRYSTAL_CHORDS = {
    'Dmaj7': ([50, 57, 61, 66], 38, [62, 66, 69, 73]),
    'E/D': ([52, 56, 59, 64], 38, [64, 68, 71, 76]),
    'Bm7': ([54, 59, 62, 69], 47, [59, 62, 66, 69]),
    'Gmaj7': ([55, 59, 62, 66], 43, [67, 71, 74, 78]),
    'Em7': ([52, 55, 59, 62], 40, [64, 67, 71, 74]),
    'A6': ([57, 61, 64, 66], 45, [61, 64, 66, 69]),
    'Asus2': ([57, 59, 64, 69], 45, [59, 64, 69, 71]),
    'F#m7': ([54, 57, 61, 64], 42, [61, 64, 66, 69]),
    'D/F#': ([54, 57, 62, 66], 42, [62, 66, 69, 74]),
}
CRYSTAL_FORM = (
    ['Dmaj7', 'E/D', 'Dmaj7', 'E/D'] +                                        # 1–4 вступление
    ['Dmaj7', 'E/D', 'Dmaj7', 'E/D', 'Bm7', 'Gmaj7', 'Em7', 'A6'] +            # 5–12 тема (колокольчики)
    ['Dmaj7', 'E/D', 'F#m7', 'Bm7', 'Gmaj7', 'D/F#', 'Em7', 'Asus2'] +         # 13–20 тема' (стекло + эхо)
    ['Bm7', 'Gmaj7', 'D/F#', 'Em7', 'Bm7', 'Gmaj7', 'Asus2', 'A6'] +           # 21–28 B (ниже, подвижнее)
    ['Gmaj7', 'E/D', 'Gmaj7', 'Asus2'] +                                      # 29–32 мост
    ['Dmaj7', 'E/D', 'Dmaj7', 'E/D', 'Bm7', 'Gmaj7', 'Em7', 'A6'] +            # 33–40 тема'' (с терциями)
    ['Dmaj7', 'E/D', 'Dmaj7', 'E/D']                                          # 41–44 завершение, ведёт к началу
)
C_THEME = [
    [(0, 1, 78), (1, 1, 81), (2, 2, 85)],
    [(0, 1.5, 83), (1.5, 0.5, 80), (2, 2, 76)],
    [(0, 1, 78), (1, 1, 81), (2, 1, 86), (3, 1, 85)],
    [(0, 3, 83)],
    [(0, 1, 86), (1, 1, 85), (2, 1, 83), (3, 1, 78)],
    [(0, 1.5, 79), (1.5, 0.5, 81), (2, 2, 83)],
    [(0, 1, 76), (1, 1, 79), (2, 1, 83), (3, 1, 81)],
    [(0, 2, 81), (2, 1, 76), (3, 1, 73)],
]
C_THEME2 = [
    [(0, 2, 78), (2, 2, 85)],
    [(0, 2, 83), (2, 1, 80), (3, 1, 83)],
    [(0, 2, 81), (2, 2, 85)],
    [(0, 3, 86)],
    [(0, 1, 83), (1, 1, 86), (2, 2, 90)],
    [(0, 2, 85), (2, 2, 81)],
    [(0, 1, 79), (1, 1, 83), (2, 2, 86)],
    [(0, 4, 83)],
]
C_B = [
    [(0, .5, 74), (.5, .5, 78), (1, 1, 81), (2, 1, 78), (3, 1, 74)],
    [(0, .5, 74), (.5, .5, 78), (1, 1, 83), (2, 1, 81), (3, 1, 79)],
    [(0, 1.5, 78), (1.5, .5, 76), (2, 2, 74)],
    [(0, 1, 76), (1, 1, 79), (2, 1, 83), (3, 1, 79)],
    [(0, .5, 74), (.5, .5, 78), (1, 1, 81), (2, 1, 86), (3, 1, 85)],
    [(0, 2, 83), (2, 1, 81), (3, 1, 79)],
    [(0, 1, 81), (1, 1, 83), (2, 2, 88)],
    [(0, 2, 85), (2, 2, 78)],
]
C_BRIDGE = [[(0, 4, 79)], [(0, 4, 80)], [(0, 4, 83)], [(0, 4, 81)]]
C_OUTRO = {41: [(0, 1, 78), (1, 1, 81), (2, 2, 85)], 42: [(0, 4, 83)]}


def crystal():
    s = Score(72, 44, seed=2)
    rng = np.random.default_rng(5)
    for i, name in enumerate(CRYSTAL_FORM):
        bar = i + 1
        pad_notes, root, arp = CRYSTAL_CHORDS[name]
        section = ('intro' if bar <= 4 else 'A' if bar <= 12 else 'A2' if bar <= 20 else 'B' if bar <= 28
                   else 'bridge' if bar <= 32 else 'A3' if bar <= 40 else 'outro')
        for k, m in enumerate(pad_notes):
            s.add(bar, 0, 4.1, m, 'glasspad', 0.36 if section in ('bridge', 'intro', 'outro') else 0.28,
                  pan=(-0.4 + 0.27 * k), send=0.55, human=False)
        # бас в 2-й октаве (73–123 Гц) с гармониками: слышен и в динамике телефона, не забирает запас громкости
        s.add(bar, 0, 3.9, root, 'subbass', 0.42, send=0.05, human=False)
        # искры челесты шестнадцатыми — во вступлении, мосту, завершении и редко в теме''
        if section in ('intro', 'bridge', 'outro') or (section == 'A3' and bar % 2 == 1):
            idx = 0
            for k in range(16):
                if rng.random() < 0.55:
                    idx = (idx + rng.integers(-1, 2)) % 4
                    s.add(bar, k * 0.25, 0.25, arp[idx] + 12, 'celesta', rng.uniform(0.1, 0.18),
                          pan=float(rng.uniform(-0.7, 0.7)), send=0.6)
        # арфа восьмыми вверх по аккорду — движение в теме' и B
        if section in ('A2', 'B'):
            for k in range(8):
                s.add(bar, k * 0.5, 0.5, arp[k % 4] - (12 if k < 4 else 0), 'harp', 0.26 if k % 2 == 0 else 0.2,
                      pan=0.3, send=0.35)
        if section == 'A':
            s.melody(bar, C_THEME[bar - 5], 'bell', 0.62, pan=-0.05, send=0.45)
        elif section == 'A2':
            s.melody(bar, C_THEME2[bar - 13], 'glass', 0.5, pan=0.1, send=0.5)
            for beat, dur, m in C_THEME2[bar - 13]:
                s.add(bar, beat + 1.5, 0.5, m + 12, 'bell', 0.16, pan=-0.45, send=0.6)   # эхо колокольчиком
        elif section == 'B':
            s.melody(bar, C_B[bar - 21], 'bell', 0.58, pan=0.0, send=0.42)
            s.add(bar, 0, 0.3, 0, 'kick', 0.18, send=0.05)
            for beat in (0.5, 1.5, 2.5, 3.5):
                s.add(bar, beat, 0.1, 0, 'shaker', 0.09, pan=0.4, send=0.15)
        elif section == 'bridge':
            s.melody(bar, C_BRIDGE[bar - 29], 'glass', 0.45, pan=0.0, send=0.6)
        elif section == 'A3':
            s.melody(bar, C_THEME[bar - 33], 'bell', 0.6, pan=-0.05, send=0.45)
            for beat, dur, m in C_THEME[bar - 33]:
                s.add(bar, beat, dur, m - 4 if (m - 4) % 12 not in (1, 6) else m - 3, 'glass', 0.18, pan=0.3, send=0.5)
        elif section == 'outro' and bar in C_OUTRO:
            s.melody(bar, C_OUTRO[bar], 'bell', 0.45, pan=0.1, send=0.55)
        # капли: 1–3 за такт в случайных местах, тихо, по сторонам
        for _ in range(int(rng.integers(1, 4))):
            s.add(bar, float(rng.uniform(0, 4)), 0.1, 0, 'drop', float(rng.uniform(0.08, 0.16)),
                  pan=float(rng.choice([-0.75, -0.5, 0.5, 0.75])), send=0.7, human=False)
    return s.render(reverb_seconds=3.8, wet=0.62, decay_low=3.0, decay_mid=2.4, decay_high=1.1)


TRACKS = {'warm': ('music_warm', warm), 'crystal': ('music_crystal', crystal)}


def analyse(x, name):
    mono = x.mean(axis=0)
    peak = 20 * np.log10(np.abs(x).max() + 1e-12)
    rms = 20 * np.log10(np.sqrt(np.mean(mono ** 2)) + 1e-12)
    X = np.abs(np.fft.rfft(mono)) ** 2
    f = np.fft.rfftfreq(len(mono), 1 / SR)
    hi = X[f > 8000].sum() / X.sum() * 100
    cen = (f * np.sqrt(X)).sum() / np.sqrt(X).sum()
    # стык цикла: скачок между последним и первым сэмплом и громкость по 50 мс по обе стороны
    jump = np.abs(x[:, 0] - x[:, -1]).max()
    w = int(0.05 * SR)
    end_rms = 20 * np.log10(np.sqrt(np.mean(x[:, -w:] ** 2)) + 1e-12)
    start_rms = 20 * np.log10(np.sqrt(np.mean(x[:, :w] ** 2)) + 1e-12)
    # провалы: минимальная громкость окна 0,5 с
    win = int(0.5 * SR)
    seg = [np.sqrt(np.mean(mono[i:i + win] ** 2)) for i in range(0, len(mono) - win, win)]
    floor = 20 * np.log10(min(seg) + 1e-12)
    print(f'{name}: {len(mono) / SR:6.1f} с, пик {peak:5.1f} дБ, RMS {rms:5.1f} дБ, центроид {cen / 1000:4.2f} кГц, '
          f'>8 кГц {hi:4.2f} %, стык: скачок {jump:.4f}, 50 мс до/после {end_rms:5.1f}/{start_rms:5.1f} дБ, '
          f'тише всего 0,5 с: {floor:5.1f} дБ')


def write_ogg(path, x, compression):
    """OGG Vorbis блоками: запись длинного файла одним вызовом в libsndfile 1.2 падает."""
    data = x.T.astype(np.float32)
    with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS', compression_level=compression) as f:
        for i in range(0, len(data), 32768):
            f.write(data[i:i + 32768])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('only', nargs='*')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    for key, (name, fn) in TRACKS.items():
        if a.only and key not in a.only:
            continue
        x = master(fn())
        analyse(x, name)
        # игровой файл: OGG Vorbis стерео; мастер — OGG высокого качества (рендер детерминирован, исходник — этот скрипт)
        write_ogg(os.path.join(a.out, name + '.ogg'), x, 0.45)
        write_ogg(os.path.join(a.out, name + '_master.ogg'), x, 0.0)
        print(f'  → {name}.ogg {os.path.getsize(os.path.join(a.out, name + ".ogg")) // 1024} КБ')
    return 0


if __name__ == '__main__':
    sys.exit(main())
