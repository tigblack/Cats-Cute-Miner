#!/usr/bin/env python3
"""
Original music beds and post-production stingers for the three 45 s Cat Miners video ads (Meta, 9:16).

All three tracks sit on the shared grid of Docs/Marketing/AD_SCRIPTS.md: 128 BPM, beat = 0.46875 s,
bar = 1.875 s, 24 bars = 45.000 s, key D major. Form (bars are 1-based):
    1-2 hook (energy from the first frame, no fade-in)     3-10 verse groove
    11-12 build (riser, snare roll, filter sweep)          13-20 drop (2-bar hook A / A')
    21-22 breakdown                                         23 final hit at 41.25 s, D chord rings, faded by 45.0 s

  t1_progress.wav  ad 1 "Level 1 -> Level 100": bright and rising, a new layer every two verse bars, triumphant drop.
  t2_idle.wav      ad 2 "Stop tapping!": clumsy oom-pah hook; at 3.30 s the record of that very oom-pah music is
                   grabbed and scratched (seamless hand-off, then a full stop), a "wrong" stab at 3.75 s and a
                   sad-trombone "oops" (G F# F E), hard groove entry at 5.625 s (chill-hop, upbeat), sleepy
                   low-passed bars 11-12, big rise into the drop at 22.5 s.
  t3_chest.wav     ad 3 "Mythical chest": Dm brass "dun!" on the first frame, ticking clock, heartbeat, tremolo
                   strings and a riser, silence for the last eighth note, magical reveal hit at 3.75 s, playful
                   adventure groove, drop at 22.5 s.

Stingers (Temp/ads/sfx, 48 kHz WAV, peak -3 dBFS): whoosh, pop, scratch, buzzer, ding, sparkle, impact, tick,
riser, zzz, cash.

How it works: the instruments, filters and reverb impulse of Tools/Audio/music.py are imported (that module is not
edited; it is switched to 48 kHz by setting music.SR, which its functions read at call time). Its Score / Mix /
master / fold_loop are NOT used: tracks are rendered linearly here. New in this file: punchy kick (100-150 Hz body
plus click, audible on phones), snare, clap, hats, crash, toms, riser, impact, record scratch, pulse/saw leads,
supersaw stabs and pads, e-piano, kazoo, tuba; five buses (drums, bass, music, lead, fx) with sidechain ducking
from the kick, swept filters, ping-pong echo on the lead, a short reverb (wet 0.25) and gates for hard stops.
The master is a gain to about -16 LUFS integrated plus a 4x-oversampled lookahead peak limiter
(true peak <= -2 dBTP); no tanh clipping. Loudness is measured with ffmpeg ebur128.

Usage (project root, hub):
    Tools/.venv/bin/python -B Tools/AdVideo/ad_music.py              # t1 t2 t3 sfx + self-check
    Tools/.venv/bin/python -B Tools/AdVideo/ad_music.py t2 sfx       # only some parts (t1 t2 t3 sfx)
    Tools/.venv/bin/python -B Tools/AdVideo/ad_music.py --check      # only re-measure the WAVs already rendered
Options: --out DIR (default Temp/ads; music -> DIR/music, stingers -> DIR/sfx), --lufs -16, --tp -2.
Outputs: music/t1_progress.wav, music/t2_idle.wav, music/t3_chest.wav (2,160,000 samples, stereo, 24-bit),
music/markers.json (cue times in seconds per track, plus measured loudness), sfx/*.wav.
Rendering is deterministic (fixed seeds); a full run takes about 25 s.
Review (plots + numbers, nobody can listen on the hub): Tools/AdVideo/ad_music_review.py.
"""
import argparse
import json
import re
import subprocess
import sys
import time
import zlib
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.ndimage import minimum_filter1d, uniform_filter1d
from scipy.interpolate import PchipInterpolator
from scipy.signal import butter, lfilter, oaconvolve, resample_poly, sosfilt, sosfiltfilt

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools' / 'Audio'))
import music as M  # noqa: E402  (Tools/Audio/music.py: instruments, filters, reverb impulse)

SR = 48000
M.SR = SR                      # music.py functions read the module-global SR at call time -> they now run at 48 kHz
TAU = 2 * np.pi
hz = M.hz

FFMPEG = ROOT / 'Tools/.venv/lib/python3.12/site-packages/imageio_ffmpeg/binaries/ffmpeg-linux-x86_64-v7.0.2'

# ───────────────────────── time grid ─────────────────────────
BPM = 128
BEAT = 60.0 / BPM              # 0.46875 s
BAR = 4 * BEAT                 # 1.875 s
BARS = 24
LENGTH = BARS * BAR            # 45.0 s
N = int(round(LENGTH * SR))    # 2,160,000 samples
FADE_START = 44.3              # cosine fade of the whole mix, silent at 45.000 s
REVERB_WET = 0.25


def at(bar, beat=0.0):
    """Seconds of (1-based bar, 0-based beat); beat may exceed 4."""
    return ((bar - 1) * 4 + beat) * BEAT


def section_of(bar):
    return ('hook' if bar <= 2 else 'verse' if bar <= 10 else 'build' if bar <= 12 else 'drop' if bar <= 20
            else 'break' if bar <= 22 else 'end')


SECTIONS = [('hook', 1, 2), ('verse', 3, 10), ('build', 11, 12), ('drop', 13, 20), ('breakdown', 21, 22),
            ('end', 23, 24)]

# ───────────────────────── small DSP helpers ─────────────────────────


def tt(n):
    return np.arange(n) / SR


def phase_of(freq):
    """Phase (rad) of an oscillator with instantaneous frequency `freq` (Hz array)."""
    return TAU * np.cumsum(freq) / SR


def undb(d):
    return 10 ** (d / 20)


def sat(y, drive):
    """Gentle tanh saturation of ONE instrument (adds harmonics so low sounds read on phone speakers)."""
    return np.tanh(drive * y) / np.tanh(drive)


def tail_fade(y, seconds=0.01):
    n = min(y.shape[-1], max(1, int(seconds * SR)))
    y[..., -n:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, n)))
    return y


def head_fade(y, seconds=0.002):
    n = min(y.shape[-1], max(1, int(seconds * SR)))
    y[..., :n] *= np.linspace(0.0, 1.0, n)
    return y


def adsr(t, dur, a=0.005, d=0.12, s=0.7, r=0.1):
    """Attack (linear), decay to sustain (exponential), release after `dur` (about -43 dB after r seconds)."""
    env = np.where(t < a, t / max(a, 1e-6), s + (1 - s) * np.exp(-(t - a) / max(d, 1e-4)))
    rel = t > dur
    env[rel] *= np.exp(-(t[rel] - dur) / (r / 5))
    return env


def additive(f, n, amp, fmax=9000.0, mult=None, detune=(0.0,), rng=None):
    """Band-limited oscillator: sum of amp(k) * sin(k * phase) for harmonics below fmax.

    amp(k) may return a scalar or a per-sample array (time-varying filter); `mult` is an optional per-sample
    frequency multiplier (vibrato, scoops, droops); `detune` lists voices in cents (averaged)."""
    t = tt(n)
    y = np.zeros(n)
    for cents in detune:
        fd = f * 2 ** (cents / 1200.0)
        ph = TAU * fd * (np.cumsum(mult) / SR if mult is not None else t)
        for k in range(1, max(1, int(min(fmax, 0.45 * SR) / fd)) + 1):
            a = amp(k)
            if np.ndim(a) == 0 and a == 0:
                continue
            y += a * np.sin(k * ph + (rng.uniform(0, TAU) if rng is not None else 0.0))
    return y / len(detune)


def auto_filter(x, kind, cutoff, block=256, order=2, width=1.6):
    """Time-varying Butterworth filter: coefficients re-designed every `block` samples, state carried over.

    kind: 'lowpass' / 'highpass' / 'bandpass' (band = cutoff/width .. cutoff*width); cutoff: per-sample Hz array."""
    x2 = np.atleast_2d(x)
    n = x2.shape[1]
    out = np.empty_like(x2, dtype=float)
    zi = None
    for i in range(0, n, block):
        fc = float(cutoff[min(n - 1, i + block // 2)])
        if kind == 'bandpass':
            wn = [max(20.0, fc / width), min(0.45 * SR, fc * width)]
        else:
            wn = min(0.45 * SR, max(20.0, fc))
        sos = butter(order, wn, kind, fs=SR, output='sos')
        if zi is None:
            zi = np.zeros((sos.shape[0], x2.shape[0], 2))
        out[:, i:i + block], zi = sosfilt(sos, x2[:, i:i + block], axis=-1, zi=zi)
    return out if np.ndim(x) == 2 else out[0]


def log_curve(points):
    """Piecewise log-linear curve through (time_s, value) points -> f(t_array)."""
    ts = np.array([p[0] for p in points], float)
    vs = np.log(np.array([p[1] for p in points], float))
    return lambda t: np.exp(np.interp(t, ts, vs))


def region_filter(t0, t1, kind, curve, xfade=0.02):
    """Post-process step: swept filter on [t0, t1) of a bus, 20 ms cross-fades in and out of the region."""
    def fn(x):
        i0, i1 = max(0, int(t0 * SR)), min(N, int(t1 * SR))
        seg = x[:, i0:i1]
        y = auto_filter(seg, kind, curve(tt(i1 - i0) + i0 / SR))
        k = int(xfade * SR)
        w = np.ones(i1 - i0)
        w[:k] = np.linspace(0, 1, k)
        w[-k:] = np.linspace(1, 0, k)
        x = x.copy()
        x[:, i0:i1] = seg * (1 - w) + y * w
        return x
    return fn


def gate(t_off, t_on, fade_off=0.008, fade_on=0.004):
    """Post-process step: silence a bus (dry, echo and reverb) from t_off to t_on; short cosine ramps."""
    g = np.ones(N)
    a, b = int(round(t_off * SR)), int(round(t_on * SR))
    k = int(fade_off * SR)
    g[a - k:a] = 0.5 * (1 + np.cos(np.linspace(0, np.pi, k)))
    g[a:b] = 0.0
    k2 = int(fade_on * SR)
    g[b - k2:b] = 0.5 * (1 - np.cos(np.linspace(0, np.pi, k2)))     # fully open again exactly at t_on
    return lambda x: x * g


def pingpong(x, delay, fb, taps=4):
    """Stereo ping-pong echo (wet only), darkened with a 4.5 kHz low-pass."""
    out = np.zeros_like(x)
    d = int(round(delay * SR))
    for k in range(1, taps + 1):
        s = k * d
        if s >= x.shape[1]:
            break
        src = x[::-1] if k % 2 else x
        out[:, s:] += fb ** k * src[:, :x.shape[1] - s]
    return np.vstack([M.lp(out[c], 4500) for c in range(2)])


# ───────────────────────── pitched instruments: fn(f_hz, dur_s, vel, rng) -> mono ─────────────────────────

def lead(f, dur, vel, rng, duty=0.25, fc=3400.0, release=0.09):
    """Chiptune-ish but warm pulse lead: band-limited pulse, two voices +-6 cents, gentle low-pass, late vibrato."""
    n = int((dur + release) * SR)
    t = tt(n)
    vib = 1 + 0.0035 * np.sin(TAU * 5.5 * t) * np.clip((t - 0.15) / 0.2, 0, 1)
    amp = lambda k: (2 / (np.pi * k)) * np.sin(np.pi * k * duty) / (1 + (k * f / fc) ** 2)
    y = additive(f, n, amp, 11000, vib, (-6.0, 6.0), rng)
    return M.ramp_in(y * adsr(t, dur, 0.004, 0.14, 0.72, release) * 1.3, 1.0) * vel


def supersaw(f, dur, vel, rng, a=0.003, d=0.18, s=0.35, r=0.12, fc0=7000.0, fc1=1600.0, fdec=0.12,
             detune=(-13.0, -4.0, 5.0, 12.0), fmax=9000.0):
    """Detuned saw stack with a filter envelope (fc0 -> fc1, time constant fdec): stabs, pads, brass."""
    n = int((dur + r) * SR)
    t = tt(n)
    if fc0 == fc1:
        amp = lambda k: (1.0 / k) / (1 + (k * f / fc0) ** 2)
    else:
        fc = fc1 + (fc0 - fc1) * np.exp(-t / fdec)
        amp = lambda k: (1.0 / k) / (1 + (k * f / fc) ** 2)
    y = additive(f, n, amp, fmax, None, detune, rng)
    return y * adsr(t, dur, a, d, s, r) * 0.9 * vel


def epiano(f, dur, vel, rng, release=0.3):
    """FM electric piano (Rhodes-like): 1:1 pair with decaying index, an octave-modulated pair, a short tine ping."""
    n = int((dur + release) * SR)
    t = tt(n)
    idx = 1.5 * np.exp(-t / 0.22) + 0.3
    y = np.sin(TAU * f * t + idx * np.sin(TAU * f * t))
    y += 0.5 * np.sin(TAU * f * 1.0025 * t + 0.6 * idx * np.sin(TAU * 2 * f * t))
    if 14 * f < 0.4 * SR:
        y += 0.12 * np.sin(TAU * 14 * f * t) * np.exp(-t / 0.02)
    tau = float(np.clip(1.4 * (262 / f) ** 0.4, 0.5, 2.5))
    env = np.exp(-t / tau) * np.minimum(1, t / 0.002)
    rel = t > dur
    env[rel] *= np.exp(-(t[rel] - dur) / (release / 5))
    return y * env * 0.6 * vel


def kazoo(f, dur, vel, rng, bend=0.0, release=0.06, wobble=0.010):
    """Comic kazoo / bassoon-ish reed: 32 % pulse with a pitch scoop, wobbly vibrato, three formants.
    bend: semitones of droop over the note; wobble: vibrato depth (0.03 = the wide 'waaah' of a sad trombone,
    faded in over 0.08-0.23 s)."""
    n = int((dur + release) * SR)
    t = tt(n)
    scoop = 2 ** (-0.55 * np.exp(-t / 0.04) / 12)
    depth = wobble if wobble <= 0.012 else 0.010 + (wobble - 0.010) * np.clip((t - 0.08) / 0.15, 0, 1)
    wob = 1 + depth * np.sin(TAU * 6.3 * t + rng.uniform(0, TAU))
    droop = 2 ** (bend * np.clip(t / max(dur, 1e-3), 0, 1) ** 2 / 12)
    y = additive(f, n, lambda k: (2 / (np.pi * k)) * np.sin(np.pi * k * 0.32), 7500, scoop * wob * droop,
                 (0.0, 11.0), rng)
    y = 0.3 * y + 1.3 * M.bp(y, 480, 950) + 0.9 * M.bp(y, 1150, 1750) + 0.35 * M.bp(y, 2600, 3300)
    return sat(y * adsr(t, dur, 0.018, 0.1, 0.85, release), 1.3) * 0.9 * vel


def tuba(f, dur, vel, rng, release=0.07):
    """Bouncy staccato tuba for the oom-pah hook: starts a semitone flat ('bwomp'), short decay."""
    n = int((dur + release) * SR)
    t = tt(n)
    bend = 2 ** (-1.0 * np.exp(-t / 0.035) / 12)
    y = additive(f, n, lambda k: 1.0 / k ** 1.5 if k <= 10 else 0, 2500, bend, (0.0,), rng)
    return sat(M.lp(y * adsr(t, dur, 0.012, 0.09, 0.45, release), 1400), 1.5) * 0.9 * vel


def synth_bass(f, dur, vel, rng, release=0.05, bright=1.0):
    """Drop bass: sine sub plus plucky filtered saw harmonics, saturated (harmonics at 100-400 Hz for phones)."""
    n = int((dur + release) * SR)
    t = tt(n)
    fc = (320 + 1500 * np.exp(-t / 0.07)) * bright
    harm = additive(f, n, lambda k: 0 if k == 1 else (1.0 / k) / (1 + (k * f / fc) ** 2), 3500, None, (0.0,), rng)
    y = (0.7 * np.sin(TAU * f * t) + 1.1 * harm) * adsr(t, dur, 0.003, 0.25, 0.75, release)
    return sat(M.ramp_in(y, 1.0), 1.6) * 0.8 * vel


def pizz_bass(f, dur, vel, rng, release=0.06):
    """Bouncy pizzicato bass (music.pluck) with a sine sub, cut after the written length."""
    y = M.pluck(f, dur, 1.0, rng, decay=0.35, bright=1.1)
    t = tt(len(y))
    y = y + 0.8 * np.sin(TAU * f * t) * np.exp(-t / 0.25)
    rel = t > dur
    y[rel] *= np.exp(-(t[rel] - dur) / (release / 5))
    y = y[:int((dur + release) * SR)]
    return sat(y, 1.3) * 0.75 * vel


def strings_trem(f, dur, vel, rng):
    """Tremolo strings for suspense: music.pad (bright) with 16th-note amplitude tremolo locked to the grid."""
    y = M.pad(f, dur, 1.0, rng, attack=0.2, release=0.3, bright=3.5)
    t = tt(len(y))
    y *= 1 - 0.65 * (0.5 - 0.5 * np.cos(TAU * (4 / BEAT) * t))
    return y * 3.0 * vel


def tom(f, dur, vel, rng):
    """Pitched tom / timpani-ish drum: falling sine plus a short stick noise; decays to about -40 dB, then fades
    (it used to be cut at 0.5 s while still at -20 dB: a click on every tom)."""
    n = int(0.9 * SR)
    t = tt(n)
    y = np.sin(phase_of(f * (1 + 0.5 * np.exp(-t / 0.03)))) * np.exp(-t / 0.2)
    y += 0.25 * M.bp(rng.standard_normal(n), 400, 3000) * np.exp(-t / 0.03)
    return tail_fade(sat(M.ramp_in(y, 0.5), 1.4), 0.15) * 0.8 * vel


INSTR = {
    # from Tools/Audio/music.py
    'marimba': M.marimba, 'pluck': M.pluck, 'celesta': M.celesta, 'bell': M.bell, 'glass': M.glass,
    'pad': M.pad, 'glasspad': M.INSTR['glasspad'], 'bass_soft': M.bass,
    'bright_pluck': lambda f, d, v, r: M.pluck(f, d, v, r, decay=0.4, bright=1.6),
    # new
    'lead': lead,
    'square': lambda f, d, v, r: lead(f, d, v, r, duty=0.5, fc=2400.0),
    'thin': lambda f, d, v, r: lead(f, d, v, r, duty=0.125, fc=5000.0, release=0.05),
    'stab': supersaw,
    'saw_pad': lambda f, d, v, r: supersaw(f, d, v, r, a=0.03, d=1.5, s=0.75, r=0.6, fc0=2600.0, fc1=2600.0,
                                           detune=(-10.0, 0.0, 9.0), fmax=5000.0),
    'brass': lambda f, d, v, r: supersaw(f, d, v, r, a=0.02, d=0.3, s=0.6, r=0.12, fc0=700.0, fc1=3000.0,
                                         fdec=0.06, fmax=7000.0),
    'epiano': epiano, 'kazoo': kazoo, 'kazoo_droop': lambda f, d, v, r: kazoo(f, d, v, r, bend=-3.0),
    'kazoo_sad': lambda f, d, v, r: kazoo(f, d, v, r, bend=-1.0, release=0.1, wobble=0.032),
    'tuba': tuba, 'synth_bass': synth_bass, 'round_bass': lambda f, d, v, r: synth_bass(f, d, v, r, bright=0.6),
    'pizz_bass': pizz_bass, 'strings': strings_trem, 'tom': tom,
}


# ───────────────────────── percussion and effects: fn(vel, rng) -> mono or (2, n) ─────────────────────────

def kick(vel, rng, body_hz=125.0, sub_hz=50.0, decay=0.13, body=1.2, click=1.0, length=0.45):
    """Punchy kick: falling sub (50 Hz), a strong 100-150 Hz body that phones can play, a noise + 2.8 kHz click.
    About 40 % of the energy sits at 40-100 Hz and 50 % at 100-150 Hz (printed by the self-check)."""
    n = int(length * SR)
    t = tt(n)
    sub = np.sin(phase_of(sub_hz + 70 * np.exp(-t / 0.035) + 140 * np.exp(-t / 0.006))) * np.exp(-t / decay)
    tone = np.sin(phase_of(body_hz * (1 + 0.25 * np.exp(-t / 0.02)))) * np.exp(-t / 0.1)
    nc = int(0.006 * SR)
    cl = 0.5 * np.sin(TAU * 2800 * t) * np.exp(-t / 0.003)
    cl[:nc] += M.hp(rng.standard_normal(nc), 1500) * np.exp(-np.arange(nc) / (0.0015 * SR))
    y = sat(sub + body * tone + 0.5 * click * cl, 1.6)
    return tail_fade(M.ramp_in(y, 0.5), 0.02) * vel


def heart(vel, rng):
    """Muffled heartbeat thump (one 'lub'; the 'dub' is a second, softer hit)."""
    y = kick(1.0, rng, body_hz=105.0, sub_hz=48.0, decay=0.08, body=1.8, click=0.0, length=0.35)
    return M.lp(y, 900) * 1.2 * vel


def snare(vel, rng):
    n = int(0.32 * SR)
    t = tt(n)
    body = (np.sin(phase_of(185 * (1 + 0.35 * np.exp(-t / 0.012)))) * np.exp(-t / 0.055)
            + 0.45 * np.sin(TAU * 330 * t + 0.6) * np.exp(-t / 0.035))
    nz = rng.standard_normal(n)
    rattle = M.bp(nz, 1800, 9000) * np.exp(-t / 0.11) + 0.5 * M.hp(nz, 5000) * np.exp(-t / 0.05)
    return tail_fade(M.ramp_in((0.75 * body + 0.9 * rattle) * 0.8, 0.3)) * vel


def clap(vel, rng):
    """Clap: four noise micro-bursts 9 ms apart, then a short diffuse tail."""
    n = int(0.35 * SR)
    t = tt(n)
    env = np.zeros(n)
    for d in (0.0, 0.009, 0.018):
        m = t >= d
        env[m] += np.exp(-(t[m] - d) / 0.0045)
    m = t >= 0.027
    env[m] += 0.9 * np.exp(-(t[m] - 0.027) / 0.11)
    y = M.bp(rng.standard_normal(n), 900, 5200) * env
    return tail_fade(M.ramp_in(y * 0.9, 0.2)) * vel


HAT_F = (205.3, 304.4, 369.6, 522.7, 540.0, 800.0)      # 808-style square-wave cluster


def metal(n, rng, mult=1.0):
    t = tt(n)
    return sum(np.sign(np.sin(TAU * f * mult * t + rng.uniform(0, TAU))) for f in HAT_F) / len(HAT_F)


def hat(vel, rng, open_=False):
    n = int((0.45 if open_ else 0.09) * SR)
    t = tt(n)
    y = 0.6 * metal(n, rng) + 0.6 * rng.standard_normal(n)
    y = M.hp(M.bp(y, 6500, 16000), 7000)
    y *= np.exp(-t / (0.16 if open_ else 0.022))
    return tail_fade(M.ramp_in(y * 0.55, 0.2)) * vel


def crash(vel, rng, length=2.6):
    """Stereo crash cymbal: two decorrelated noise + metal layers, high-passed, ~0.7 s decay."""
    n = int(length * SR)
    t = tt(n)
    out = []
    for _ in range(2):
        y = 0.5 * metal(n, rng, 1.7) + rng.standard_normal(n)
        y = M.hp(y, 3500) * np.exp(-t / 0.7) + 0.6 * M.bp(rng.standard_normal(n), 1500, 6000) * np.exp(-t / 0.05)
        out.append(y * np.minimum(1, t / 0.002))
    return tail_fade(np.vstack(out) * 0.32, 0.4) * vel


def tick(vel, rng, tone=3200.0):
    """Clock tick: two short sine pings, a click and a small wooden body."""
    n = int(0.08 * SR)
    t = tt(n)
    y = (np.sin(TAU * tone * t) * np.exp(-t / 0.004) + 0.6 * np.sin(TAU * tone * 1.47 * t) * np.exp(-t / 0.003)
         + 0.35 * np.sin(TAU * 900 * t) * np.exp(-t / 0.008))
    nc = int(0.003 * SR)
    y[:nc] += 0.6 * M.hp(rng.standard_normal(nc), 2000)
    return tail_fade(M.ramp_in(y * 0.6, 0.2)) * vel


def impact(vel, rng, length=1.6):
    """Big low hit: sub sweep 150 -> 32 Hz, a 95-150 Hz punch (phones), a kick, crunchy noise and a mid boom."""
    n = int(length * SR)
    t = tt(n)
    sub = np.sin(phase_of(32 + 40 * np.exp(-t / 0.35) + 80 * np.exp(-t / 0.02))) * np.exp(-t / 0.6)
    punch = np.sin(phase_of(95 * (1 + 0.6 * np.exp(-t / 0.03)))) * np.exp(-t / 0.22)
    k = np.zeros(n)
    kk = kick(1.0, rng, body_hz=110.0, sub_hz=48.0, decay=0.3, length=min(length, 0.8))
    k[:len(kk)] = kk
    nz = rng.standard_normal(n)
    crunch = (0.7 * M.lp(nz, 3000) * np.exp(-t / 0.05) + 0.5 * M.bp(nz, 150, 700) * np.exp(-t / 0.25)
              + 0.4 * M.lp(nz, 300) * np.exp(-t / 0.35))
    y = sat(0.8 * sub + 0.6 * punch + 0.7 * k + crunch, 1.8)
    return tail_fade(M.ramp_in(y, 0.3), min(0.4, length / 3)) * vel


PERC = {
    'kick': kick, 'heart': heart, 'snare': snare, 'clap': clap, 'hat': hat,
    'ohat': lambda v, r: hat(v, r, open_=True), 'crash': crash, 'shaker': M.shaker, 'rim': M.rim,
    'tick': tick, 'tock': lambda v, r: tick(v, r, tone=2100.0), 'impact': impact,
}


def riser(dur, rng, f_lo=250.0, f_hi=6500.0, tone=(50, 86), curve=2.0):
    """Stereo riser: noise through a rising band-pass plus a rising saw-ish tone; ends abruptly (4 ms fade)."""
    n = int(dur * SR)
    t = tt(n)
    x = t / dur
    centre = f_lo * (f_hi / f_lo) ** x
    noise = auto_filter(rng.standard_normal((2, n)), 'bandpass', centre, block=512, width=1.5)
    f_tone = hz(tone[0]) * (hz(tone[1]) / hz(tone[0])) ** x
    vib = 1 + 0.006 * np.sin(TAU * np.cumsum(3 + 9 * x) / SR)
    ph = TAU * np.cumsum(f_tone * vib) / SR
    tone_sig = sum(np.sin(k * ph) / k for k in range(1, 6))
    y = (0.9 * noise + 0.35 * tone_sig) * (x ** curve)
    return tail_fade(y, 0.004)


def reverse_cymbal(length, rng):
    """Reversed crash swelling into the next downbeat (place it to end there)."""
    y = crash(1.0, rng, length=length + 0.2)[:, :int(length * SR)][:, ::-1].copy()
    return head_fade(tail_fade(y, 0.003), 0.05)


def scratch(vel, rng, dur=0.45, src=None, src_t=0.8, xfade=0.0):
    """Record scratch: a 'record' read back with a hand-moved playback speed. It plays at normal speed (the music
    is still running), is yanked forward, dragged back, pushed forward once more and stops dead at `dur`.

    src: the record, mono or (2, n) at SR (default: a bright synthetic D chord, used for the stinger); reading starts
    at src_t seconds. xfade > 0: the output starts xfade seconds BEFORE the grab, at normal speed, fading in with a
    raised cosine that is the exact complement of gate()'s fade-out, so a track hands its own music over to the
    scratch without a seam. Level follows |speed| ** 0.6 (1 at normal speed: no level jump at the hand-off). Once
    the hand moves the record the sound is band-passed (zero-phase, 150 Hz - 7 kHz), so speeds near zero do not
    thump and 2.8x speed does not alias. Returns (2, n) for a stereo src, else mono."""
    k = int(round(xfade * SR))
    n = k + int(round(dur * SR))
    t = (np.arange(n) - k) / SR                               # 0 = the hand grabs the record
    times = [0.0, 0.04, 0.11, 0.19, 0.27, 0.34, dur]          # playback speed key points (1 = normal)
    speed = np.where(t < 0, 1.0, PchipInterpolator(times, [1.0, 2.4, 0.0, -2.8, 0.0, 2.0, 0.0])(np.maximum(t, 0)))
    if src is None:
        src_n = int(2.0 * SR)
        src = sum(additive(hz(m), src_n, lambda h: 1.0 / h, 5000, None, (0.0,), rng) for m in (62, 66, 69, 74)) / 4
        src = src + 0.35 * M.bp(rng.standard_normal(src_n), 300, 3000)
    mono = np.ndim(src) == 1
    src = np.atleast_2d(np.asarray(src, float))
    pos = src_t * SR - k + np.concatenate([[0.0], np.cumsum(speed[:-1])])
    idx = np.arange(src.shape[1])
    read = lambda s_: np.vstack([np.interp(pos, idx, c) for c in s_])
    band = sosfiltfilt(butter(4, 7000, 'lowpass', fs=SR, output='sos'), src, axis=-1)
    moved = sosfiltfilt(butter(2, 150, 'highpass', fs=SR, output='sos'), read(band), axis=-1)
    w = np.clip(t / 0.03, 0, 1)
    w = w * w * (3 - 2 * w)                                   # raw music -> band-passed scratch over 30 ms
    y = (read(src) * (1 - w) + moved * w) * np.minimum(1.6, np.abs(speed) ** 0.6)
    i = int(src_t * SR)
    ref = np.sqrt(np.mean(src[:, max(0, i - 4800):max(1, i)] ** 2)) + 1e-4
    nc = int(0.004 * SR)                                      # needle grab, relative to the record's level
    y[:, k:k + nc] += 1.5 * ref * M.hp(rng.standard_normal(nc), 1000) * np.exp(-np.arange(nc) / (0.001 * SR))
    if k:
        y[:, :k] *= 0.5 * (1 - np.cos(np.linspace(0, np.pi, k)))
    else:
        y = head_fade(y, 0.002)
    y = tail_fade(y, 0.01) * vel
    return y[0] if mono else y


def wrong_stab(dur, vel, rng, notes=(38, 50, 56, 61, 63), cutoff=2600.0, floor=700.0, thud=0.5):
    """Dissonant brassy 'wrong!' stab: D + G# + C# + D# saws, wah low-pass closing, pitch sagging, low thud."""
    n = int(dur * SR)
    t = tt(n)
    droop = 2 ** (-0.8 * np.clip((t - 0.12) / dur, 0, 1) ** 1.5 / 12)
    y = sum(additive(hz(m), n, lambda k: 1.0 / k, 4000, droop, (-8.0, 8.0), rng) for m in notes) / len(notes)
    y = auto_filter(y, 'lowpass', floor + (cutoff - floor) * np.exp(-t / 0.22), order=4)
    y = y * np.minimum(1, t / 0.006) * np.exp(-t / (0.32 * dur / 0.9 + 0.05))
    th = kick(0.7, rng, body_hz=95.0, sub_hz=45.0, decay=0.15, click=0.3)
    m = min(n, len(th))
    y[:m] += thud * th[:m]
    return tail_fade(y * 2.2, min(0.15, dur / 4)) * vel


def ping(f, n, decay=0.11):
    """Tiny glassy ping: sine plus an inharmonic 2.756x partial."""
    t = tt(n)
    y = np.sin(TAU * f * t) * np.exp(-t / decay)
    if f * 2.756 < 0.45 * SR:
        y += 0.3 * np.sin(TAU * f * 2.756 * t) * np.exp(-t / 0.04)
    return M.ramp_in(y, 0.5)


def sparkle(dur, vel, rng, count=28, lo=86, hi=103):
    """Magical shimmer (stereo): random D-major-pentatonic pings, denser at the start, over an airy noise shimmer."""
    n = int(dur * SR)
    out = np.zeros((2, n))
    pent = [m for m in range(lo, hi + 1) if m % 12 in (2, 4, 6, 9, 11)]
    for t0 in np.sort(dur * 0.8 * rng.random(count) ** 1.6):
        y = ping(hz(int(rng.choice(pent))), int(0.5 * SR))
        a = (rng.uniform(-0.8, 0.8) + 1) * np.pi / 4
        i = int(t0 * SR)
        j = min(n, i + len(y))
        lvl = rng.uniform(0.4, 1.0) * (1 - 0.6 * t0 / dur)
        out[0, i:j] += y[:j - i] * np.cos(a) * lvl
        out[1, i:j] += y[:j - i] * np.sin(a) * lvl
    t = tt(n)
    shimmer = M.hp(rng.standard_normal((2, n)), 7000) * np.exp(-t / (dur * 0.35)) * (0.6 + 0.4 * np.sin(TAU * 14 * t))
    out += 0.12 * shimmer * np.minimum(1, t / 0.01)
    return tail_fade(out * 0.5, min(0.25, dur / 3)) * vel


def crackle(dur, rng, rate=12.0):
    """Vinyl crackle bed (stereo): sparse band-passed pops plus faint hiss."""
    n = int(dur * SR)
    out = np.zeros((2, n))
    for c in range(2):
        k = rng.poisson(rate * dur)
        out[c, rng.integers(0, n, k)] = rng.uniform(0.2, 1.0, k) * rng.choice([-1, 1], k)
        out[c] = M.bp(out[c], 900, 7000) * 1.5 + 0.015 * M.lp(M.hp(rng.standard_normal(n), 1500), 6000)
    return head_fade(tail_fade(out, 0.3), 0.3)


# ───────────────────────── render caches (deterministic) ─────────────────────────
_NOTE_CACHE = {}
_PERC_CACHE = {}


def note_audio(inst, midi, dur):
    """Rendered note at vel = 1 (instruments are linear in vel); one fixed-seed render per (inst, pitch, length)."""
    key = (inst, round(float(midi), 3), round(float(dur), 4))
    y = _NOTE_CACHE.get(key)
    if y is None:
        y = INSTR[inst](hz(midi), dur, 1.0, np.random.default_rng(zlib.crc32(repr(key).encode())))
        y = tail_fade(np.array(y, float), 0.008)        # pads, bells, bass used to stop at -33..-41 dB: clicks
        _NOTE_CACHE[key] = y
    return y


def perc_audio(inst, variant):
    """Four round-robin variants per drum sound (different noise), rendered once at vel = 1."""
    key = (inst, variant)
    y = _PERC_CACHE.get(key)
    if y is None:
        y = PERC[inst](1.0, np.random.default_rng(zlib.crc32(repr(key).encode())))
        y = tail_fade(np.array(y, float), 0.004)        # music.shaker stopped at -41 dB
        _PERC_CACHE[key] = y
    return y


# ───────────────────────── harmony (D major) ─────────────────────────
# name: (voicing for pads / stabs, bass root, arpeggio tones)
CH = {
    'D': ([62, 66, 69, 74], 38, [62, 66, 69, 74]),
    'A': ([61, 64, 69, 73], 45, [61, 64, 69, 73]),
    'Asus': ([62, 64, 69, 74], 45, [62, 64, 69, 74]),
    'Bm': ([62, 66, 71, 74], 35, [59, 62, 66, 71]),
    'G': ([62, 67, 71, 74], 43, [62, 67, 71, 74]),
    'C': ([60, 64, 67, 72], 36, [60, 64, 67, 72]),
    'Dm': ([50, 62, 65, 69, 74], 38, [62, 65, 69, 74]),
    'Bb': ([46, 62, 65, 70, 74], 34, [62, 65, 70, 74]),
    'A7': ([45, 61, 64, 67, 69], 33, [61, 64, 67, 69]),
    'Dmaj7': ([62, 66, 69, 73], 38, [62, 66, 69, 73]),
    'Bm7': ([62, 66, 69, 71], 35, [59, 62, 66, 69]),
    'Em7': ([62, 64, 67, 71], 40, [64, 67, 71, 74]),
    'A13': ([61, 66, 67, 71], 45, [61, 64, 67, 71]),
    'A7sus': ([62, 64, 67, 71], 45, [62, 64, 67, 71]),
    'Dadd9': ([62, 64, 66, 69], 38, [62, 66, 69, 76]),
}
D_FINAL = [50, 57, 62, 66, 69, 74]           # final D chord (pads)
D_TOP = [74, 78, 81, 86]                     # final D chord (bells)
D_PENT_UP = [74, 76, 78, 81, 83, 86, 88, 90, 93, 95, 98]

# bass patterns: (beat, length in beats, semitones above the root)
BASS_4 = [(b, 0.8, 0) for b in range(4)]
BASS_8 = [(b * 0.5, 0.42, 0) for b in range(8)]
BASS_8_OCT = [(b * 0.5, 0.42, 0 if b % 2 == 0 else 12) for b in range(8)]
BASS_FUNK = [(0, 0.6, 0), (0.75, 0.25, 12), (1.5, 0.4, 0), (2.5, 0.4, 7), (3, 0.4, 12), (3.5, 0.4, 7)]
BASS_BOUNCE = [(b * 0.5, 0.3, (0, 12, 7, 12)[b % 4]) for b in range(8)]
ARP_UP = [0, 1, 2, 3, 2, 1, 2, 3]
ARP_16 = [0, 1, 2, 3]


# ───────────────────────── mixer ─────────────────────────
BUSES = ('drums', 'bass', 'music', 'lead', 'fx')
BUS_GAIN = {'drums': 1.0, 'bass': 0.8, 'music': 1.0, 'lead': 0.9, 'fx': 1.0}
DUCK = {'bass': 0.6, 'music': 0.35, 'lead': 0.15}           # sidechain depth from the kick (and big hits)


class Bus:
    """One stereo channel: dry signal and a reverb send, both (2, N)."""

    def __init__(self):
        self.dry = np.zeros((2, N))
        self.send = np.zeros((2, N))

    def add(self, y, t, pan=0.0, gain=1.0, send=0.0):
        i = int(round(t * SR))
        if y.ndim == 1:
            a = (float(np.clip(pan, -1, 1)) + 1) * np.pi / 4          # equal-power pan
            y = np.vstack([y * np.cos(a), y * np.sin(a)])
        if i < 0:
            y, i = y[:, -i:], 0
        if i >= N or y.shape[1] == 0:
            return
        j = min(N, i + y.shape[1])
        seg = y[:, :j - i] * gain
        self.dry[:, i:j] += seg
        if send:
            self.send[:, i:j] += seg * send


def sidechain_shape(hits, attack=0.004, hold=0.015, release=0.16):
    """0..1 ducking curve: max over hits of vel * (fast dip, short hold, exponential recovery)."""
    s = np.zeros(N)
    L = int((attack + hold + 5 * release) * SR)
    t = tt(L)
    shape = np.where(t < attack, t / attack, np.where(t < attack + hold, 1.0, np.exp(-(t - attack - hold) / release)))
    pre = int(0.002 * SR)
    for t0, v in hits:
        i = int(round(t0 * SR)) - pre
        a, b = max(0, i), min(N, i + L)
        if b > a:
            s[a:b] = np.maximum(s[a:b], min(1.0, v) * shape[a - i:b - i])
    return s


class Song:
    """Arrangement on the bar/beat grid, rendered linearly into five buses."""

    def __init__(self, name, seed):
        self.name = name
        self.seed = seed
        self.rng = np.random.default_rng(seed)
        self.buses = {b: Bus() for b in BUSES}
        self.kicks = []                 # (time, vel) driving the sidechain
        self.post = []                  # (bus names, fn(x) -> x), applied after reverb and ducking, in order
        self.echo = {'lead': (0.75 * BEAT, 0.33, 0.22)}      # dotted-eighth ping-pong
        self.count = {}
        self.markers = {}

    def place(self, bus, y, t, pan=0.0, gain=1.0, send=0.0):
        self.buses[bus].add(y, t, pan, gain, send)

    def note(self, bus, bar, beat, beats, midi, inst, vel, pan=0.0, send=0.15, human=0.004):
        t = at(bar, beat)
        if human:
            t += self.rng.uniform(-human, human)
            vel *= self.rng.uniform(0.94, 1.06)
        self.place(bus, note_audio(inst, midi, beats * BEAT) * vel, max(0.0, t), pan, 1.0, send)

    def melody(self, bus, bar, notes, inst, vel, pan=0.0, send=0.2, transpose=0, human=0.003):
        for beat, beats, m in notes:
            self.note(bus, bar, beat, beats, m + transpose, inst, vel * (1.08 if beat in (0, 2) else 1.0), pan, send,
                      human)

    def chord(self, bus, bar, beat, beats, chord, inst, vel, spread=0.6, send=0.2, human=0.003):
        midis = list(CH[chord][0]) if isinstance(chord, str) else list(chord)
        for i, m in enumerate(midis):
            pan = spread * (i / (len(midis) - 1) - 0.5) if len(midis) > 1 else 0.0
            self.note(bus, bar, beat, beats, m, inst, vel, pan, send, human)

    def arp(self, bar, chord, inst, idx, step, vel, bus='music', pan=0.0, send=0.2, octave=0, start=0.0, end=4.0):
        tones = CH[chord][2]
        k, b = 0, start
        while b < end - 1e-9:
            m = tones[idx[k % len(idx)] % len(tones)] + 12 * octave
            self.note(bus, bar, b, step, m, inst, vel * (1.12 if abs(b - round(b)) < 1e-9 else 0.9), pan, send)
            k, b = k + 1, b + step

    def bassline(self, bar, root, pattern, inst, vel, human=0.002):
        for beat, beats, off in pattern:
            self.note('bass', bar, beat, beats, root + off, inst, vel * (1.0 if beat == int(beat) else 0.85), 0.0,
                      0.0, human)

    def hit(self, bus, bar, beat, inst, vel, pan=0.0, send=0.0, human=0.0):
        k = self.count.get(inst, 0)
        self.count[inst] = k + 1
        t = at(bar, beat) + (self.rng.uniform(-human, human) if human else 0.0)
        self.place(bus, perc_audio(inst, k % 4) * vel, max(0.0, t), pan, 1.0, send)

    def kick(self, bar, beat, vel=1.0, inst='kick'):
        self.hit('drums', bar, beat, inst, vel)
        self.kicks.append((at(bar, beat), vel))

    def roll(self, bar, segs, v0, v1, inst='snare', pan=0.0):
        """Snare roll: segs = [(beat_from, beat_to, step)], velocity rising v0 -> v1."""
        beats = []
        for b0, b1, step in segs:
            b = b0
            while b < b1 - 1e-9:
                beats.append(b)
                b += step
        for i, b in enumerate(beats):
            x = i / max(1, len(beats) - 1)
            self.hit('drums', bar, b, inst, v0 + (v1 - v0) * x ** 1.3, pan, send=0.12)

    def render(self, highpass=True):
        """Buses -> echo -> reverb -> sidechain -> post steps -> sum -> 25 Hz high-pass -> end fade.
        highpass=False skips the 25 Hz high-pass (for a mix that is placed back into another song's bus)."""
        ir = [M.reverb_ir(1.5, self.seed + c, decay_low=1.2, decay_mid=1.0, decay_high=0.5, predelay=0.012)
              for c in (0, 1)]
        duck = sidechain_shape(self.kicks)
        stems, mix = {}, np.zeros((2, N))
        for name, bus in self.buses.items():
            y = bus.dry
            if name in self.echo:
                d, fb, amt = self.echo[name]
                y = y + amt * pingpong(bus.dry, d, fb)
            if np.any(bus.send):
                y = y + REVERB_WET * np.vstack([oaconvolve(bus.send[c], ir[c])[:N] for c in (0, 1)])
            if DUCK.get(name):
                y = y * (1.0 - DUCK[name] * duck)
            for names, fn in self.post:
                if name in names:
                    y = fn(y)
            y = y * BUS_GAIN[name]
            stems[name] = y
            mix += y
        if highpass:
            mix = np.vstack([M.hp(mix[c], 25, 2) for c in (0, 1)])
        i0 = int(FADE_START * SR)
        mix[:, i0:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, N - i0)))
        return mix, stems


# ───────────────────────── shared arrangement helpers ─────────────────────────

def drums_four(s, bar, kick=1.0, clap=0.8, snare=0.0):
    for b in range(4):
        s.kick(bar, b, kick)
    for b in (1, 3):
        if clap:
            s.hit('drums', bar, b, 'clap', clap, pan=0.05, send=0.15)
        if snare:
            s.hit('drums', bar, b, 'snare', snare, send=0.12)


def hats(s, bar, vel, step=0.5, swing=0.0, open_vel=0.0, pan=0.25):
    """Closed hats in 8ths or 16ths (accent on the off-beat 8th); optional swing (beats) and open hats off-beat."""
    accents16 = (0.55, 0.32, 1.0, 0.32)
    for i in range(int(round(4 / step))):
        b = i * step
        pos = int(round((b % 1) / 0.25)) % 4
        v = vel * (accents16[pos] if step < 0.5 else (0.6 if pos == 0 else 1.0))
        sw = swing if pos == 2 else swing / 2 if pos in (1, 3) else 0.0
        if open_vel and pos == 2:
            s.hit('drums', bar, b + sw, 'ohat', open_vel, pan=-pan, send=0.05)
        else:
            s.hit('drums', bar, b + sw, 'hat', v, pan=pan, human=0.002)


def build_fx(s, start_bar, bars, gain=0.55, end_early=0.0):
    """Riser from the downbeat of start_bar to the drop (optionally ending `end_early` seconds before it)."""
    dur = bars * BAR - end_early
    s.place('fx', riser(dur, s.rng), at(start_bar), gain=gain)


def final_hit(s, bell_inst='bell'):
    """Bar 23 downbeat (41.25 s): impact, crash, kick, stab, then a sustained D chord that rings into the fade."""
    s.hit('fx', 23, 0, 'impact', 0.9)
    s.kicks.append((at(23), 1.0))
    s.hit('drums', 23, 0, 'crash', 1.0)
    s.kick(23, 0, 1.0)
    s.chord('music', 23, 0, 0.5, D_FINAL[1:], 'stab', 0.3, human=0)
    s.chord('music', 23, 0, 7.5, D_FINAL, 'saw_pad', 0.17, send=0.4, human=0)
    s.chord('music', 23, 0, 7.5, D_FINAL[1:], 'pad', 0.3, send=0.45, human=0)
    s.chord('lead', 23, 0, 6.0, D_TOP, bell_inst, 0.3, spread=0.8, send=0.45, human=0)
    s.note('bass', 23, 0, 6.0, 38, 'synth_bass', 0.75, human=0)
    s.note('bass', 23, 0, 7.0, 38, 'bass_soft', 0.5, human=0)
    # bar 24: a soft celesta arpeggio on top of the ringing chord
    for i, m in enumerate([74, 78, 81, 86, 90]):
        s.note('lead', 24, i * 0.5, 2.5, m, 'celesta', 0.24, pan=-0.4 + 0.2 * i, send=0.5)


def drop_entry(s, bar, impact_vel=0.5):
    s.hit('drums', bar, 0, 'crash', 1.0)
    if impact_vel:
        s.hit('fx', bar, 0, 'impact', impact_vel)
        s.kicks.append((at(bar), 1.0))


# ───────────────────────── t1: "Level 1 -> Level 100" ─────────────────────────
T1_FORM = ['D', 'A'] + ['D', 'Bm', 'G', 'A'] * 2 + ['G', 'Asus'] + ['D', 'A', 'Bm', 'G'] * 2 + ['G', 'Asus', 'D', 'D']
T1_HOOK = [   # hook A over D | A, answer A' over Bm | G (chorus = A A' A A')
    [(0, .5, 74), (.75, .5, 78), (1.5, .75, 81), (2.5, .5, 78), (3, .5, 81), (3.5, .5, 83)],
    [(0, 1, 85), (1, .5, 83), (1.5, .5, 81), (2, .75, 76), (2.75, .75, 78), (3.5, .5, 76)],
    [(0, .5, 74), (.75, .5, 78), (1.5, .75, 81), (2.5, .5, 78), (3, .5, 81), (3.5, .5, 83)],
    [(0, 1, 86), (1, .5, 85), (1.5, .5, 83), (2, 1, 81), (3, .5, 79), (3.5, .5, 76)],
]
T1_VERSE = {
    7: [(0, 1.5, 78), (1.5, .5, 76), (2, 1, 74), (3, 1, 69)],
    8: [(0, 1.5, 74), (1.5, .5, 76), (2, 2, 78)],
    9: [(0, 1, 79), (1, 1, 78), (2, 1, 74), (3, 1, 71)],
    10: [(0, 2, 73), (2, 1, 76), (3, 1, 81)],
}
T1_RUN = [(2 + 0.25 * i, 0.25, m) for i, m in enumerate([69, 71, 73, 74, 76, 78, 79, 81])]


def track_t1():
    """Bright, optimistic, rising: hook preview, verse grows one layer every 2 bars, triumphant drop."""
    s = Song('t1_progress', 101)
    for bar in range(1, BARS + 1):
        ch = T1_FORM[bar - 1]
        root = CH[ch][1]
        sec = section_of(bar)
        if sec == 'hook':                               # drop preview from the first frame
            if bar == 1:
                s.hit('drums', 1, 0, 'crash', 0.75)
            drums_four(s, bar, kick=0.95, clap=0.7)
            hats(s, bar, 0.32)
            s.bassline(bar, root, BASS_8_OCT, 'synth_bass', 0.72)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.26, send=0.3)
            for b in (0.5, 1.5, 2.5, 3.5):
                s.chord('music', bar, b, 0.4, ch, 'stab', 0.45, send=0.2)
            s.arp(bar, ch, 'bright_pluck', ARP_UP, 0.5, 0.26, pan=0.35, send=0.2, octave=1)
            s.melody('lead', bar, T1_HOOK[bar - 1], 'lead', 0.88)
            s.melody('lead', bar, T1_HOOK[bar - 1], 'bell', 0.15, pan=0.3, send=0.35, transpose=12)
        elif sec == 'verse':                            # levels 0..3: kick+shaker+bass+marimba, +4/4+clap+pad, ...
            lvl = (bar - 3) // 2
            if bar == 3:
                s.hit('drums', bar, 0, 'crash', 0.4)
            if lvl == 0:
                s.kick(bar, 0, 0.8)
                s.kick(bar, 2, 0.75)
                for b in np.arange(0, 4, 0.5):
                    s.hit('drums', bar, b, 'shaker', 1.0 if b % 1 else 0.65, pan=0.3)
                s.bassline(bar, root, BASS_4, 'synth_bass', 0.55)
            else:
                drums_four(s, bar, kick=0.82 + 0.04 * lvl, clap=0.5 + 0.08 * lvl)
                hats(s, bar, 0.28 + 0.03 * lvl, step=0.25 if lvl >= 3 else 0.5, open_vel=0.3 if lvl >= 3 else 0)
                s.bassline(bar, root, BASS_8_OCT if lvl >= 2 else BASS_8, 'synth_bass', 0.62 + 0.04 * lvl)
            s.arp(bar, ch, 'marimba', ARP_UP, 0.5, 0.28 + 0.02 * lvl, pan=0.25, send=0.25, octave=1)
            if lvl >= 1:
                s.chord('music', bar, 0, 4, ch, 'pad', 0.1 + 0.03 * lvl, send=0.35)
            if lvl >= 2:
                s.melody('lead', bar, T1_VERSE[bar], 'bell', 0.42, pan=-0.1, send=0.3)
            if lvl >= 3:
                s.arp(bar, ch, 'thin', ARP_16, 0.25, 0.16, bus='lead', pan=0.4, send=0.15, octave=1)
        elif sec == 'build':                            # riser, snare roll, 16th arps, high-pass sweep
            if bar == 11:
                build_fx(s, 11, 2, gain=0.4)
                for b in range(4):
                    s.kick(bar, b, 0.85)
                s.roll(bar, [(0, 4, 1.0)], 0.3, 0.45)
                s.bassline(bar, root, BASS_8, 'synth_bass', 0.66)
            else:
                for b in np.arange(0, 4, 0.5):
                    s.kick(bar, b, 0.72 + 0.06 * b)
                s.roll(bar, [(0, 2, 0.5), (2, 3, 0.25), (3, 4, 0.125)], 0.4, 0.85)
                s.bassline(bar, root, BASS_4[:2], 'synth_bass', 0.6)
                s.melody('lead', bar, T1_RUN, 'lead', 0.5)
            hats(s, bar, 0.3, step=0.25)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.2, send=0.3)
            s.arp(bar, ch, 'marimba', ARP_16, 0.25, 0.38, pan=0.25, send=0.25)
        elif sec == 'drop':                             # full energy; bars 17-20 add bell octave + 16th arps
            k = bar - 13
            if bar in (13, 17):
                drop_entry(s, bar, 0.55 if bar == 13 else 0.0)
            drums_four(s, bar, kick=1.0, clap=0.8, snare=0.45)
            hats(s, bar, 0.3, step=0.25, open_vel=0.32)
            s.bassline(bar, root, BASS_8_OCT, 'synth_bass', 0.85)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.34, send=0.3)
            for b in (0.5, 1.5, 2.5, 3.5):
                s.chord('music', bar, b, 0.4, ch, 'stab', 0.6, send=0.2)
            s.arp(bar, ch, 'bright_pluck', ARP_UP, 0.5, 0.32, pan=0.35, send=0.2, octave=1)
            s.melody('lead', bar, T1_HOOK[k % 4], 'lead', 0.95)
            s.melody('lead', bar, T1_HOOK[k % 4], 'bell', 0.18 if bar < 17 else 0.3, pan=0.3, send=0.35,
                     transpose=12)
            if bar >= 17:
                s.arp(bar, ch, 'thin', ARP_16, 0.25, 0.14, bus='lead', pan=-0.4, send=0.15, octave=1)
        elif sec == 'break':                            # no kick: pads, bell hook, roll + reverse crash into 41.25
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.1, send=0.4)
            s.chord('music', bar, 0, 4, ch, 'pad', 0.14, send=0.4)
            s.note('bass', bar, 0, 3.8, root, 'synth_bass', 0.5)
            hats(s, bar, 0.22)
            s.arp(bar, ch, 'marimba', ARP_UP, 0.5, 0.25, pan=0.25, send=0.3)
            if bar == 21:
                s.melody('lead', bar, T1_HOOK[0], 'bell', 0.4, send=0.35)
            else:
                s.melody('lead', bar, [(0, 1, 81), (1, 1, 79), (2, 2, 76)], 'bell', 0.36, send=0.35)
                s.roll(bar, [(2, 3, 0.25), (3, 4, 0.125)], 0.25, 0.8)
                s.place('fx', reverse_cymbal(BAR, s.rng), at(23) - BAR, gain=0.5)
                build_fx(s, 22, 1, gain=0.3)
        elif bar == 23:
            final_hit(s)
    s.post.append((('music', 'bass'), region_filter(18.70, 22.50, 'highpass',
                                                    log_curve([(18.70, 20), (18.75, 30), (22.40, 700), (22.5, 700)]))))
    s.markers = {'hook_start': 0.0, 'verse_start': at(3), 'build_start': at(11), 'drop': at(13),
                 'breakdown': at(21), 'final_hit': at(23), 'fade_start': FADE_START, 'end': LENGTH}
    return s


# ───────────────────────── t2: "Stop tapping!" ─────────────────────────
T2_FORM = (['D', 'A', '-'] + ['Dmaj7', 'Bm7', 'Em7', 'A13', 'Dmaj7', 'Bm7', 'Em7'] + ['A13', 'A7sus']
           + ['Dmaj7', 'Bm7', 'Em7', 'A13'] * 2 + ['Em7', 'A7sus', 'Dadd9', 'Dadd9'])
T2_KAZOO = [   # plodding oom-pah tune (bars 1-2); the last note lands just before the scratch
    [(0, .9, 62), (1, .9, 66), (2, .9, 69), (3, .45, 66), (3.5, .45, 64)],
    [(0, .9, 61), (1, .9, 64), (2, 1.6, 57)],          # still sounding when the needle is yanked at 3.30 s
]
T2_TUBA = [[(0, .6, 38), (2, .6, 33)], [(0, .6, 33), (2, 1.4, 40)]]
T2_PAH = [[57, 62, 66], [57, 61, 64]]
T2_HOOK = [   # A over Dmaj7 | Bm7, A' (a step lower) over Em7 | A13
    [(0, .5, 81), (.5, .5, 78), (1, .5, 81), (1.5, 1, 85), (2.75, .25, 83), (3, .5, 81), (3.5, .5, 78)],
    [(0, .75, 74), (.75, .75, 78), (1.5, .5, 81), (2, .5, 83), (2.5, .5, 81), (3, 1, 78)],
    [(0, .5, 79), (.5, .5, 76), (1, .5, 79), (1.5, 1, 83), (2.75, .25, 81), (3, .5, 79), (3.5, .5, 76)],
    [(0, .75, 73), (.75, .75, 76), (1.5, .5, 78), (2, 1, 79), (3, .5, 78), (3.5, .5, 76)],
]
T2_VERSE = {
    6: [(.5, .5, 71), (1, .5, 74), (1.5, 1, 76), (3, .5, 74), (3.5, .5, 71)],
    7: [(0, 1.5, 73), (2, .5, 76), (2.5, .5, 78), (3, 1, 76)],
    8: [(.5, .5, 69), (1, .5, 73), (1.5, 1, 74), (3, .5, 78), (3.5, .5, 76)],
    9: [(0, 1.5, 74), (2, .5, 73), (2.5, .5, 71), (3, 1, 69)],
    10: [(0, .5, 67), (.5, .5, 71), (1, .5, 74), (1.5, 1.5, 76), (3, 1, 74)],
}
T2_LULLABY = {11: [(0, 1, 81), (1, 1, 78), (2, 2, 73)], 12: [(0, 1, 79), (1, 1.5, 76)]}
T2_SCRATCH = 3.30
T2_XFADE = 0.006                       # the music fades out over 6 ms while the scratch of the same music fades in
T2_SWING = 0.07
# 'oops' after the wrong stab: a sad-trombone 'wah wah wah waaah' (G F# F E, the last one drooping and wobbling)
T2_OOPS = [(1.5, .45, 67, 'kazoo', 1.0), (2.0, .45, 66, 'kazoo', 0.95), (2.5, .45, 65, 'kazoo', 0.95),
           (3.0, .95, 64, 'kazoo_sad', 1.1)]


def t2_oompah(s, bar):
    """Bars 1-2: comically clumsy / plodding oom-pah (sloppy timing on purpose)."""
    if bar == 1:
        s.hit('drums', 1, 0, 'crash', 0.55)
    for b, beats, m in T2_TUBA[bar - 1]:
        s.note('bass', bar, b, beats, m, 'tuba', 1.1, human=0.012 if b else 0)
        s.kick(bar, b, 0.85)
    for b in (1, 3) if bar == 1 else (1,):
        s.chord('music', bar, b, 0.35, T2_PAH[bar - 1], 'bright_pluck', 0.6, human=0.018)
        s.hit('drums', bar, b, 'snare', 0.35, human=0.015, send=0.1)
    for b in np.arange(0.5, 4 if bar == 1 else 3, 1.0):
        s.hit('drums', bar, b, 'rim', 0.4, pan=0.3, human=0.015)
    s.melody('lead', bar, T2_KAZOO[bar - 1], 'kazoo', 1.05, human=0.018 if bar > 1 else 0)


def t2_record():
    """The oom-pah bars rendered on their own with the track's seed: the random draws of bars 1-2 come first in both
    songs, so this mix is sample-identical to the track before 3.30 s and is the 'record' that gets scratched."""
    s = Song('t2_idle', 202)
    for bar in (1, 2):
        t2_oompah(s, bar)
    return s.render(highpass=False)[0]          # the track's own 25 Hz high-pass is applied once, after the sum


def t2_groove_drums(s, bar, vel=1.0):
    """Upbeat boom-bap: kick 1, a+2, &3; snare (+ clap) on 2 and 4; swung 8th hats with a ghost 16th."""
    for b, v in ((0, 0.95), (1.75, 0.5), (2.5, 0.85)):
        s.kick(bar, b, v * vel)
    for b in (1, 3):
        s.hit('drums', bar, b, 'snare', 0.8 * vel, send=0.12)
        s.hit('drums', bar, b, 'clap', 0.35 * vel, send=0.15)
    hats(s, bar, 0.42 * vel, swing=T2_SWING)
    s.hit('drums', bar, 3.75, 'hat', 0.15 * vel, pan=0.25)


def track_t2():
    """Clumsy oom-pah -> scratch + stop -> 'wrong' stab + oops -> groove -> sleepy -> rise -> drop -> final hit."""
    s = Song('t2_idle', 202)
    for bar in range(1, BARS + 1):
        ch = T2_FORM[bar - 1]
        sec = section_of(bar)
        if bar <= 2:
            t2_oompah(s, bar)
        elif bar == 3:                                  # silence except the 'wrong' stab and the oops motif
            s.place('fx', wrong_stab(1.4, 0.85, s.rng), at(3))
            for b, beats, m, inst, v in T2_OOPS:      # as loud as the kazoo hook (it was 12 dB under the groove)
                s.note('fx', 3, b, beats, m, inst, v, send=0.2, human=0)
        elif sec == 'verse':                            # bars 4-10 groove, entering hard at 5.625 s
            root = CH[ch][1]
            if bar == 4:
                s.hit('drums', 4, 0, 'crash', 1.0)
                s.hit('fx', 4, 0, 'impact', 0.35)
                s.chord('music', 4, 0, 0.6, ch, 'stab', 0.3, human=0)
            t2_groove_drums(s, bar)
            s.bassline(bar, root, BASS_FUNK, 'round_bass', 0.8)
            s.chord('music', bar, 0, 1.4, ch, 'epiano', 0.5, send=0.25, human=0 if bar == 4 else 0.004)
            s.chord('music', bar, 1.5 + T2_SWING, 0.4, ch, 'epiano', 0.28, send=0.25)
            s.chord('music', bar, 2.5 + T2_SWING, 1.0, ch, 'epiano', 0.42, send=0.25)
            if bar <= 5:
                s.arp(bar, ch, 'marimba', [0, 2, 1, 3, 2, 1, 3, 2], 0.5, 0.3, pan=0.3, send=0.25)
            else:
                s.melody('lead', bar, T2_VERSE[bar], 'square', 0.6, pan=-0.05, send=0.2)
        elif sec == 'build':                            # sleepy night (low-passed), then a big rise
            root = CH[ch][1]
            s.chord('music', bar, 0, 4, ch, 'epiano', 0.45, send=0.35)
            s.chord('music', bar, 0, 4, ch, 'pad', 0.18, send=0.4)
            s.note('bass', bar, 0, 3.6, root, 'bass_soft', 0.6)
            s.melody('lead', bar, T2_LULLABY[bar], 'celesta', 0.4, send=0.5)
            if bar == 11:
                s.kick(bar, 0, 0.5)
                s.hit('drums', bar, 2, 'rim', 0.22, pan=-0.3, send=0.3)
            else:
                build_fx(s, 12, 1, gain=0.7)
                s.kick(bar, 0, 0.45)
                for b in np.arange(2, 4, 0.5):
                    s.kick(bar, b, 0.55 + 0.15 * (b - 2))
                s.roll(bar, [(1, 2, 0.5), (2, 3, 0.25), (3, 4, 0.125)], 0.25, 1.0)
        elif sec == 'drop':
            root = CH[ch][1]
            k = bar - 13
            if bar in (13, 17):
                drop_entry(s, bar, 0.55 if bar == 13 else 0.0)
            for b in range(4):
                s.kick(bar, b, 1.0)
            for b in (1, 3):
                s.hit('drums', bar, b, 'snare', 0.7, send=0.12)
                s.hit('drums', bar, b, 'clap', 0.6, send=0.15)
            hats(s, bar, 0.36, swing=T2_SWING, open_vel=0.28)
            for b in (0.25, 1.25, 2.25, 3.25):
                s.hit('drums', bar, b + T2_SWING / 2, 'hat', 0.14, pan=0.25)
            s.bassline(bar, root, BASS_FUNK, 'synth_bass', 0.85)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.1, send=0.3)
            for b, beats, v in ((0, 1.0, 0.5), (1.5, 0.5, 0.36), (2.5, 1.0, 0.45), (3.5, 0.5, 0.32)):
                s.chord('music', bar, b + (T2_SWING if b % 1 else 0), beats, ch, 'epiano', v, send=0.25)
            s.melody('lead', bar, T2_HOOK[k % 4], 'lead', 0.82)
            if bar >= 17:                               # comic callback: the kazoo doubles the hook an octave down
                s.melody('lead', bar, T2_HOOK[k % 4], 'kazoo', 0.32, pan=-0.3, send=0.15, transpose=-12)
                s.melody('lead', bar, T2_HOOK[k % 4], 'celesta', 0.2, pan=0.35, send=0.35, transpose=12)
        elif sec == 'break':
            root = CH[ch][1]
            s.chord('music', bar, 0, 4, ch, 'epiano', 0.48, send=0.35)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.14, send=0.4)
            s.note('bass', bar, 0, 3.8, root, 'round_bass', 0.5)
            hats(s, bar, 0.22, swing=T2_SWING)
            if bar == 21:
                s.melody('lead', bar, T2_HOOK[2], 'bell', 0.42, send=0.35)
            else:
                s.melody('lead', bar, [(0, 1, 79), (1, 1, 76), (2, 2, 74)], 'bell', 0.4, send=0.35)
                s.roll(bar, [(2, 3, 0.25), (3, 4, 0.125)], 0.25, 0.8)
                s.place('fx', reverse_cymbal(BAR, s.rng), at(23) - BAR, gain=0.5)
                build_fx(s, 22, 1, gain=0.3)
        elif bar == 23:
            final_hit(s)
    # the scratch grabs the oom-pah music itself: at 3.30 s the musical buses fade out over T2_XFADE while the
    # scratch, reading the same mix from the same sample at normal speed, fades in with the complementary curve
    record = scratch(1.0, s.rng, src=t2_record(), src_t=T2_SCRATCH, xfade=T2_XFADE)
    s.place('fx', record, T2_SCRATCH - T2_XFADE)
    s.place('fx', crackle(at(23) - at(4), s.rng), at(4), gain=0.05)
    musical = ('drums', 'bass', 'music', 'lead')
    s.post.append((musical, gate(T2_SCRATCH, at(4), fade_off=T2_XFADE)))
    s.post.append((('music', 'drums', 'bass'), region_filter(18.60, 22.50, 'lowpass', log_curve(
        [(18.60, 20000), (18.74, 20000), (19.0, 650), (20.9, 650), (22.40, 20000), (22.5, 20000)]))))
    s.markers = {'hook_start': 0.0, 'scratch': T2_SCRATCH, 'stop': T2_SCRATCH,
                 'scratch_end': round(T2_SCRATCH - T2_XFADE + record.shape[-1] / SR, 4), 'wrong_stab': at(3),
                 'oops': at(3, T2_OOPS[0][0]), 'groove_in': at(4), 'build_start': at(11), 'sleepy_start': at(11),
                 'rise_start': at(12), 'drop': at(13), 'breakdown': at(21), 'final_hit': at(23),
                 'fade_start': FADE_START, 'end': LENGTH}
    return s


# ───────────────────────── t3: "Mythical chest" ─────────────────────────
T3_FORM = ['Dm', 'Bb'] + ['D', 'C', 'G', 'A'] * 2 + ['G', 'Asus'] + ['D', 'G', 'Bm', 'A'] * 2 + ['G', 'Asus', 'D', 'D']
T3_HOOK = [   # heroic dotted hook: A over D | G, A' over Bm | A
    [(0, .75, 74), (.75, .25, 76), (1, .5, 78), (1.5, .5, 81), (2, .75, 86), (2.75, .25, 85), (3, 1, 81)],
    [(0, .75, 83), (.75, .25, 81), (1, .5, 79), (1.5, .5, 74), (2, 1.5, 79), (3.5, .5, 81)],
    [(0, .75, 74), (.75, .25, 76), (1, .5, 78), (1.5, .5, 81), (2, .75, 86), (2.75, .25, 85), (3, 1, 83)],
    [(0, .75, 85), (.75, .25, 83), (1, .5, 81), (1.5, .5, 76), (2, 1, 81), (3, .5, 83), (3.5, .5, 85)],
]
T3_VERSE = {
    5: [(0, .5, 71), (.5, .5, 74), (1, 1, 79), (2, .5, 78), (2.5, .5, 76), (3, 1, 74)],
    6: [(0, 1.5, 73), (1.5, .5, 76), (2, 2, 81)],
    7: [(0, .5, 78), (.5, .5, 76), (1, .5, 74), (1.5, .5, 76), (2, 1, 78), (3, 1, 81)],
    8: [(0, 1.5, 79), (1.5, .5, 76), (2, 1, 72), (3, 1, 76)],
    9: [(0, .5, 74), (.5, .5, 79), (1, 1, 83), (2, .5, 81), (2.5, .5, 79), (3, 1, 78)],
    10: [(0, 2, 76), (2, 1, 81), (3, 1, 85)],
}
T3_SILENCE = at(3) - BEAT / 2          # 3.515625 s: the last eighth note before the reveal is silent


def t3_heartbeat(s, bar, beats, vel):
    for b in beats:
        s.hit('drums', bar, b, 'heart', vel)
        s.hit('drums', bar, b + 0.36, 'heart', vel * 0.6)
        s.kicks.append((at(bar, b), 0.6 * vel))


def reveal(s, bar, big=True):
    """Magical reveal on a downbeat: impact, crash, celesta glissando, sparkle, bell chord."""
    t0 = at(bar)
    s.hit('fx', bar, 0, 'impact', 1.0 if big else 0.6)
    s.kicks.append((t0, 1.0))
    s.hit('drums', bar, 0, 'crash', 0.9)
    for i, m in enumerate(D_PENT_UP):
        s.place('fx', note_audio('celesta', m, 0.3) * (0.32 + 0.02 * i), t0 + 0.03 * i, pan=-0.6 + 0.12 * i,
                send=0.4)
    s.place('fx', sparkle(1.8, 1.0, s.rng), t0 + 0.05, gain=0.5 if big else 0.35, send=0.3)
    s.chord('fx', bar, 0, 3.0, D_TOP, 'bell', 0.35 if big else 0.25, spread=0.8, send=0.4, human=0)


def track_t3():
    """Suspense (clock, heartbeat, tremolo strings, riser) -> silent eighth -> reveal -> adventure groove -> drop."""
    s = Song('t3_chest', 303)
    for bar in range(1, BARS + 1):
        ch = T3_FORM[bar - 1]
        root = CH[ch][1]
        sec = section_of(bar)
        if bar <= 2:
            if bar == 1:
                s.hit('fx', 1, 0, 'impact', 0.5)            # low boom on the very first frame
                # ... plus a Dm brass 'dun!' and a timpani-like tom, so phones (no output below ~200 Hz) hear it too
                s.chord('music', 1, 0, 0.75, [50, 57, 62, 65, 69], 'brass', 0.75, spread=0.5, send=0.3, human=0)
                s.note('drums', 1, 0, 1.0, 38, 'tom', 0.75, send=0.25, human=0)
                s.place('fx', riser(T3_SILENCE, s.rng, f_lo=180.0, f_hi=5000.0, tone=(38, 74), curve=1.6), 0.0,
                        gain=0.4)
                for b in np.arange(0, 4, 0.5):
                    s.hit('drums', 1, b, 'tick' if b % 1 == 0 else 'tock', 0.65, pan=0.2)
                t3_heartbeat(s, 1, (0, 2), 0.9)
                s.chord('music', 1, 0, 4.1, 'Dm', 'strings', 0.5, send=0.3, human=0)
                s.note('bass', 1, 0, 4.0, 38, 'bass_soft', 0.3, human=0)
            else:                                           # clock doubles, heart races, harmony leans to A7
                for b in np.arange(0, 3.5, 0.5):
                    s.hit('drums', 2, b, 'tick' if b % 1 == 0 else 'tock', 0.6, pan=0.2)
                for b in np.arange(2.25, 3.5, 0.5):
                    s.hit('drums', 2, b, 'tick', 0.4, pan=-0.2)
                t3_heartbeat(s, 2, (0, 1, 2, 3), 0.9)
                s.chord('music', 2, 0, 2.05, 'Bb', 'strings', 0.48, send=0.3, human=0)
                s.chord('music', 2, 2, 1.5, 'A7', 'strings', 0.52, send=0.3, human=0)
                s.note('bass', 2, 0, 2.0, 34, 'bass_soft', 0.3, human=0)
                s.note('bass', 2, 2, 1.5, 33, 'bass_soft', 0.34, human=0)
        elif sec == 'verse':                                # playful adventure: pizz bass, pluck ostinato, toms
            if bar == 3:
                reveal(s, 3)
                s.chord('music', 3, 0, 4, 'D', 'glasspad', 0.3, send=0.45, human=0)
            s.kick(bar, 0, 0.9)
            s.kick(bar, 2, 0.85)
            if bar % 2 == 0:
                s.kick(bar, 2.75, 0.45)
            for b in (1, 3):
                s.hit('drums', bar, b, 'snare', 0.8, send=0.15)
                s.hit('drums', bar, b, 'clap', 0.35, send=0.15)
            for i in range(16):
                s.hit('drums', bar, i * 0.25, 'shaker', 0.32 if i % 2 else 0.2, pan=0.35)
            hats(s, bar, 0.22, pan=-0.2)
            if bar in (6, 10):                              # tom fills
                for i, m in enumerate((52, 50, 47, 43) if bar == 6 else (52, 52, 50, 50, 47, 47, 43, 43)):
                    s.note('drums', bar, (3 if bar == 6 else 2) + i * 0.25, 0.25, m, 'tom', 0.6 + 0.05 * i,
                           pan=0.4 - 0.1 * i, send=0.1)
            s.bassline(bar, root, BASS_BOUNCE, 'pizz_bass', 1.0)
            s.bassline(bar, root, [(0, 1.5, 0), (2, 1.5, 0)], 'round_bass', 0.35)
            s.arp(bar, ch, 'bright_pluck', ARP_UP, 0.5, 0.46, pan=0.3, send=0.25, octave=1 if bar >= 7 else 0)
            s.arp(bar, ch, 'marimba', [3, 2, 1, 2], 1.0, 0.2, pan=-0.3, send=0.25)
            if bar >= 5:
                s.chord('music', bar, 0, 4, ch, 'pad', 0.14, send=0.4)
                s.melody('lead', bar, T3_VERSE[bar], 'square', 0.72, send=0.2)
                if bar >= 9:
                    s.melody('lead', bar, T3_VERSE[bar], 'celesta', 0.22, pan=0.3, send=0.4, transpose=12)
        elif sec == 'build':                                # toms, strings callback, clock returns, roll, silent 8th
            if bar == 11:
                build_fx(s, 11, 2, end_early=BEAT / 2)
                for b in range(4):
                    s.kick(bar, b, 0.85)
                for i in range(8):
                    s.note('drums', 11, i * 0.5, 0.5, 50 if i % 2 == 0 else 45, 'tom', 0.45 + 0.03 * i, send=0.1)
            else:
                for b in np.arange(0, 3.5, 0.5):
                    s.kick(bar, b, 0.75 + 0.05 * b)
                s.roll(bar, [(0, 2, 0.5), (2, 3, 0.25), (3, 3.5, 0.125)], 0.45, 1.0)
                for b in np.arange(0, 3.5, 0.25):
                    s.hit('drums', 12, b, 'tick', 0.35, pan=0.3)
            s.chord('music', bar, 0, 4, ch, 'strings', 0.4, send=0.3)
            s.bassline(bar, root, BASS_8 if bar == 11 else BASS_8[:6], 'synth_bass', 0.65)
            s.arp(bar, ch, 'bright_pluck', ARP_16, 0.25, 0.3, pan=0.3, send=0.25, octave=1)
        elif sec == 'drop':
            k = bar - 13
            if bar == 13:
                reveal(s, 13, big=False)
            elif bar == 17:
                drop_entry(s, bar, 0.0)
            drums_four(s, bar, kick=1.0, clap=0.75, snare=0.5)
            hats(s, bar, 0.3, step=0.25, open_vel=0.3)
            if bar in (16, 20):
                for i, m in enumerate((52, 50, 47, 43)):
                    s.note('drums', bar, 3 + i * 0.25, 0.25, m, 'tom', 0.7, pan=0.4 - 0.2 * i, send=0.1)
            s.bassline(bar, root, BASS_8_OCT, 'synth_bass', 0.82)
            s.chord('music', bar, 0, 4, ch, 'saw_pad', 0.22, send=0.3)
            for b, beats in ((0, 0.6), (0.75, 0.6), (1.5, 0.8), (2.5, 0.4), (3, 0.8)):
                s.chord('music', bar, b, beats, ch, 'brass', 0.42, send=0.2)
            s.arp(bar, ch, 'bright_pluck', ARP_UP, 0.5, 0.26, pan=0.35, send=0.2, octave=1)
            s.melody('lead', bar, T3_HOOK[k % 4], 'lead', 0.82)
            s.melody('lead', bar, T3_HOOK[k % 4], 'bell', 0.24, pan=-0.3, send=0.35, transpose=12)
            if bar >= 17:
                s.chord('music', bar, 0, 4, ch, 'strings', 0.22, send=0.3)
                s.arp(bar, ch, 'celesta', ARP_16, 0.25, 0.14, bus='lead', pan=0.45, send=0.3, octave=1)
        elif sec == 'break':
            s.chord('music', bar, 0, 4, ch, 'strings', 0.3, send=0.35)
            s.chord('music', bar, 0, 4, ch, 'glasspad', 0.15, send=0.4)
            s.note('bass', bar, 0, 3.8, root, 'round_bass', 0.5)
            for b in np.arange(0, 4, 0.5):
                s.hit('drums', bar, b, 'tick' if b % 1 == 0 else 'tock', 0.4, pan=0.2)
            if bar == 21:
                s.melody('lead', bar, T3_HOOK[1], 'bell', 0.38, send=0.35)
            else:
                s.melody('lead', bar, [(0, 1, 81), (1, 1, 79), (2, 2, 76)], 'bell', 0.35, send=0.35)
                s.roll(bar, [(2, 3, 0.25), (3, 4, 0.125)], 0.25, 0.8)
                s.place('fx', reverse_cymbal(BAR, s.rng), at(23) - BAR, gain=0.5)
                build_fx(s, 22, 1, gain=0.3)
        elif bar == 23:
            final_hit(s)
            s.place('fx', sparkle(2.0, 1.0, s.rng), at(23) + 0.05, gain=0.35, send=0.3)
    s.post.append((BUSES, gate(T3_SILENCE, at(3), fade_off=0.01)))
    s.post.append((BUSES, gate(at(13) - BEAT / 2, at(13), fade_off=0.01)))       # mirrors the intro before the drop
    s.post.append((('music', 'bass'), region_filter(18.70, 22.50, 'highpass',
                                                    log_curve([(18.70, 20), (18.75, 30), (22.2, 650), (22.5, 650)]))))
    s.markers = {'hook_start': 0.0, 'silence_start': T3_SILENCE, 'reveal_hit': at(3), 'build_start': at(11),
                 'pre_drop_silence': at(13) - BEAT / 2, 'drop': at(13), 'breakdown': at(21), 'final_hit': at(23),
                 'fade_start': FADE_START, 'end': LENGTH}
    return s


TRACKS = {'t1': ('t1_progress', track_t1), 't2': ('t2_idle', track_t2), 't3': ('t3_chest', track_t3)}


# ───────────────────────── loudness: measurement and master ─────────────────────────

def ffmpeg_exe():
    if FFMPEG.exists():
        return str(FFMPEG)
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def ebur128(x=None, path=None):
    """Integrated loudness (LUFS), loudness range (LU) and true peak (dBTP) from ffmpeg's ebur128 filter."""
    cmd = [ffmpeg_exe(), '-hide_banner', '-nostats']
    if path is not None:
        cmd += ['-i', str(path)]
        data = None
    else:
        cmd += ['-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-']
        data = np.ascontiguousarray(x.T, dtype='<f4').tobytes()
    cmd += ['-af', 'ebur128=peak=true', '-f', 'null', '-']
    out = subprocess.run(cmd, input=data, capture_output=True).stderr.decode(errors='replace')
    summary = out[out.rfind('Summary:'):]
    num = lambda key: float(re.search(key + r':\s+(-?[\d.]+|-inf)', summary).group(1))
    return {'I': num('I'), 'LRA': num('LRA'), 'TP': num('Peak')}


def limiter(x, ceiling, lookahead=0.003, release=0.08, oversample=4):
    """Lookahead peak limiter on the 4x-oversampled (true) peak. The gain curve never exceeds what any peak needs:
    min-filter + moving average of equal width (attack), exponential release in 32-sample blocks."""
    n = x.shape[1]
    up = resample_poly(x, oversample, 1, axis=1)
    pk = np.abs(up).max(axis=0)[:n * oversample].reshape(n, oversample).max(axis=1)
    need = np.minimum(1.0, ceiling / np.maximum(pk, 1e-9))
    if need.min() >= 1.0:
        limiter.last_gain = np.ones(n)
        return x
    w = 2 * int(lookahead * SR) + 1
    g = uniform_filter1d(minimum_filter1d(need, w), w)
    B = 32
    nb = -(-n // B)
    gb = np.pad(g, (0, nb * B - n), constant_values=1.0).reshape(nb, B).min(axis=1)
    a = float(np.exp(-B / (release * SR)))
    rel = np.empty(nb)
    cur = 1.0
    for i in range(nb):
        cur = min(gb[i], 1.0 - (1.0 - cur) * a)
        rel[i] = cur
    g = np.minimum(g, np.repeat(rel, B)[:n])
    g = uniform_filter1d(minimum_filter1d(g, B + 1), B + 1)
    limiter.last_gain = g
    return x * g


def master(x, lufs=-16.0, tp=-2.0):
    """Gain to the target integrated loudness, then the true-peak limiter; iterate until both hold."""
    m = ebur128(x)
    gain = lufs - m['I']
    ceiling = tp - 0.4
    for _ in range(6):
        y = limiter(x * undb(gain), undb(ceiling))
        m = ebur128(y)
        if abs(m['I'] - lufs) <= 0.05 and m['TP'] <= tp:
            break
        gain += lufs - m['I']
        if m['TP'] > tp:
            ceiling -= m['TP'] - tp + 0.1
    gr = -20 * np.log10(np.maximum(limiter.last_gain, 1e-9))
    m['limiter_max_gr_db'] = round(float(gr.max()), 2)
    m['limiter_pct_over_1db'] = round(float(100 * np.mean(gr > 1.0)), 2)
    return y, m


# ───────────────────────── stingers ─────────────────────────

def sfx_whoosh(rng):
    dur = 0.35
    n = int(dur * SR)
    t = tt(n)
    x = t / dur
    centre = log_curve([(0, 350), (0.55 * dur, 3200), (dur, 1100)])(t)
    y = auto_filter(rng.standard_normal((2, n)), 'bandpass', centre, block=128, width=1.5)
    y += 0.4 * auto_filter(rng.standard_normal((2, n)), 'lowpass', centre / 3, block=128)
    y *= np.sin(np.pi * x ** 0.8) ** 2
    a = (0.3 + 0.4 * x) * np.pi / 2                 # pans left -> right
    return np.vstack([y[0] * np.cos(a), y[1] * np.sin(a)])


def sfx_pop(rng):
    n = int(0.12 * SR)
    t = tt(n)
    ph = phase_of(320 + 700 * (1 - np.exp(-t / 0.018)))
    y = (np.sin(ph) * np.exp(-t / 0.028) + 0.3 * np.sin(2 * ph) * np.exp(-t / 0.02)) * np.minimum(1, t / 0.002)
    nc = int(0.0015 * SR)
    y[:nc] += 0.3 * M.hp(rng.standard_normal(nc), 2000)
    return y


def sfx_buzzer(rng):
    """Short 'wrong' stab: D3 G#3 C#4 D4, mid-focused so phones play it, low-passed so it is not harsh."""
    return M.lp(wrong_stab(0.6, 1.0, rng, notes=(50, 56, 61, 62), cutoff=1800.0, floor=600.0, thud=0.25), 2500)


def sfx_ding(rng):
    """Bright 'ding': D6 bell with A6 / D7 partials and a glassy ping, struck and decaying (about 0.2 s time constant)."""
    n = int(0.9 * SR)
    y = np.zeros(n)
    for m, v in ((86, 1.0), (93, 0.25), (98, 0.18)):
        b = note_audio('bell', m, 0.9)[:n]
        y[:len(b)] += v * b
    y += 0.15 * ping(hz(98), n, 0.06)
    return tail_fade(y * np.exp(-tt(n) / 0.22), 0.15)


def sfx_zzz(rng):
    n = int(1.2 * SR)
    y = np.zeros(n)
    for t0, m in ((0.0, 81), (0.3, 78), (0.6, 74)):
        nt = n - int(t0 * SR)
        f = hz(m) * 2 ** (-0.3 * np.clip(tt(nt) / 0.6, 0, 1) / 12)       # each note sags a little
        tone = (np.sin(phase_of(f)) + 0.2 * np.sin(2 * phase_of(f))) * np.minimum(1, tt(nt) / 0.03)
        tone *= np.exp(-tt(nt) / 0.35) * (1 + 0.15 * np.sin(TAU * 5 * tt(nt)))
        y[int(t0 * SR):] += tone * (1.0 - 0.15 * (m < 80))
    return tail_fade(M.lp(y, 2500), 0.3)


def sfx_cash(rng):
    """Coin cascade: a register 'cha' burst, then metallic coin dings that speed up and thin out (stereo)."""
    dur = 0.8
    n = int(dur * SR)
    out = np.zeros((2, n))
    t = tt(n)
    cha = M.bp(rng.standard_normal(n), 2000, 8000) * np.exp(-t / 0.025) * 0.5
    out += cha
    t0 = 0.02
    k = 0
    while t0 < dur - 0.15 and k < 16:
        f = rng.uniform(1900, 3600)
        nn = int(0.3 * SR)
        tc = tt(nn)
        coin = sum(a * np.sin(TAU * f * r * tc + rng.uniform(0, TAU)) * np.exp(-tc / d)
                   for r, a, d in ((1.0, 1.0, 0.12), (2.76, 0.5, 0.06), (5.40, 0.25, 0.03)) if f * r < 0.45 * SR)
        coin = M.ramp_in(coin, 0.3) * (1 - 0.04 * k)
        a = (rng.uniform(-0.7, 0.7) + 1) * np.pi / 4
        i = int(t0 * SR)
        j = min(n, i + nn)
        out[0, i:j] += coin[:j - i] * np.cos(a)
        out[1, i:j] += coin[:j - i] * np.sin(a)
        t0 += rng.uniform(0.025, 0.06) * (1 + 0.08 * k)
        k += 1
    return tail_fade(out, 0.15)


STINGERS = {
    'whoosh': sfx_whoosh,
    'pop': sfx_pop,
    'scratch': lambda r: scratch(1.0, r),
    'buzzer': sfx_buzzer,
    'ding': sfx_ding,
    'sparkle': lambda r: sparkle(1.0, 1.0, r),
    'impact': lambda r: impact(1.0, r, length=1.2),
    'tick': lambda r: tick(1.0, r),
    'riser': lambda r: riser(2.0, r),
    'zzz': sfx_zzz,
    'cash': sfx_cash,
}


def render_stingers(out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    print('stingers ->', out_dir)
    for i, (name, fn) in enumerate(STINGERS.items()):
        y = np.asarray(fn(np.random.default_rng(900 + i)), float)
        y = head_fade(tail_fade(y.copy(), 0.006), 0.0005)
        y *= undb(-3.0) / np.abs(y).max()
        sf.write(out_dir / f'{name}.wav', y.T, SR, subtype='PCM_24')
        ch = 1 if y.ndim == 1 else y.shape[0]
        rms = 20 * np.log10(np.sqrt(np.mean(y ** 2)) + 1e-12)
        print(f'  {name:8s} {y.shape[-1] / SR:5.3f} s  {"mono  " if ch == 1 else "stereo"}  peak -3.0 dBFS  '
              f'RMS {rms:6.1f} dBFS')


# ───────────────────────── self-check ─────────────────────────

def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(np.square(x))) + 1e-12)


def k_weight(x):
    """ITU-R BS.1770 K-weighting (pre-filter shelf + RLB high-pass; coefficients are the 48 kHz ones)."""
    x = lfilter([1.53512485958697, -2.69169618940638, 1.19839281085285], [1, -1.69065929318241, 0.73248077421585],
                x, axis=-1)
    return lfilter([1.0, -2.0, 1.0], [1, -1.99004745483398, 0.99007225036621], x, axis=-1)


def loudness_db(z, t0, t1):
    """BS.1770 loudness (LUFS, ungated) of a K-weighted stereo window: a per-bar 'how loud does it feel'."""
    seg = z[:, int(t0 * SR):int(t1 * SR)]
    return -0.691 + 10 * np.log10(np.mean(seg ** 2, axis=1).sum() + 1e-15)


def window_db(x, t0, t1):
    return rms_db(x[:, int(t0 * SR):int(t1 * SR)])


def band_share_db(x, lo, hi, t0=0.0, t1=LENGTH):
    """Energy in [lo, hi] Hz relative to the total energy of the window (dB)."""
    mono = x.mean(axis=0)[int(t0 * SR):int(t1 * SR)] if x.ndim == 2 else x
    p = np.abs(np.fft.rfft(mono)) ** 2
    f = np.fft.rfftfreq(len(mono), 1 / SR)
    return 10 * np.log10(p[(f >= lo) & (f <= hi)].sum() / p.sum() + 1e-15)


def centroid(x, t0, t1):
    mono = x.mean(axis=0)[int(t0 * SR):int(t1 * SR)]
    p = np.abs(np.fft.rfft(mono)) ** 2
    f = np.fft.rfftfreq(len(mono), 1 / SR)
    return float((f * p).sum() / p.sum())


def check_track(key, x):
    """Print the shape checks for one rendered track; returns the per-bar RMS list (dBFS)."""
    z = k_weight(x)
    bars = [(window_db(x, at(b), at(b + 1)), loudness_db(z, at(b), at(b + 1))) for b in range(1, BARS + 1)]
    print(f'  {key}: sections (RMS dBFS / spectral centroid):  ' + '  '.join(
        f'{name} {window_db(x, at(a), at(b + 1)):5.1f}/{centroid(x, at(a), at(b + 1)) / 1000:4.2f}k'
        for name, a, b in SECTIONS))
    print(f'      kick band 100-150 Hz share of the drop: {band_share_db(x, 100, 150, at(13), at(21)):5.1f} dB, '
          f'40-100 Hz {band_share_db(x, 40, 100, at(13), at(21)):5.1f} dB, '
          f'>8 kHz {band_share_db(x, 8000, 24000, at(13), at(21)):5.1f} dB')
    if key == 't2':
        print('      t2 detail: ' + ', '.join(f'{a:.3f}-{b:.3f} {window_db(x, a, b):6.1f}' for a, b in (
            (2.80, 3.29), (3.31, 3.74), (3.76, 4.20), (4.20, 4.65), (4.70, 5.60), (5.63, 6.50), (20.0, 20.6),
            (21.6, 22.4), (22.5, 24.0))))
    if key == 't3':
        print('      t3 detail: ' + ', '.join(f'{a:.3f}-{b:.3f} {window_db(x, a, b):6.1f}' for a, b in (
            (3.00, 3.50), (3.52, 3.745), (3.75, 4.20), (22.0, 22.26), (22.27, 22.495), (22.5, 23.0))))
    return bars


def kick_report():
    y = kick(1.0, np.random.default_rng(1))
    p = np.abs(np.fft.rfft(y, 1 << 16)) ** 2
    f = np.fft.rfftfreq(1 << 16, 1 / SR)
    share = lambda lo, hi: 100 * p[(f >= lo) & (f < hi)].sum() / p.sum()
    print(f'  kick alone: energy 40-100 Hz {share(40, 100):4.1f} %, 100-150 Hz {share(100, 150):4.1f} %, '
          f'150-400 Hz {share(150, 400):4.1f} %, >2 kHz {share(2000, 24000):4.1f} %, '
          f'spectral peak {f[np.argmax(p)]:.0f} Hz')


def print_bar_table(table):
    """Per-bar energy: RMS (dBFS) and K-weighted loudness (LUFS, ungated) for every rendered track."""
    keys = list(table)
    print('  per-bar energy, RMS dBFS / loudness LUFS')
    print('   bar  start  ' + ''.join(f'{k:>16s}' for k in keys))
    for b in range(BARS):
        cells = ''.join(f'   {table[k][b][0]:6.1f} /{table[k][b][1]:6.1f}' for k in keys)
        print(f'   {b + 1:3d} {at(b + 1):6.3f}  {cells}   {section_of(b + 1)}')


# ───────────────────────── main ─────────────────────────

def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('parts', nargs='*', help='t1 t2 t3 sfx (default: all)')
    ap.add_argument('--out', default=str(ROOT / 'Temp' / 'ads'))
    ap.add_argument('--lufs', type=float, default=-16.0)
    ap.add_argument('--tp', type=float, default=-2.0)
    ap.add_argument('--check', action='store_true', help='only re-measure the rendered WAVs')
    a = ap.parse_args()
    parts = a.parts or ['t1', 't2', 't3', 'sfx']
    music_dir, sfx_dir = Path(a.out) / 'music', Path(a.out) / 'sfx'
    music_dir.mkdir(parents=True, exist_ok=True)
    t_start = time.time()

    markers_path = music_dir / 'markers.json'
    markers = json.loads(markers_path.read_text()) if markers_path.exists() else {}
    markers['grid'] = {'bpm': BPM, 'beat_s': BEAT, 'bar_s': BAR, 'bars': BARS, 'length_s': LENGTH,
                       'sample_rate': SR, 'samples': N, 'key': 'D major',
                       'sections': {name: [at(a_), at(b_ + 1)] for name, a_, b_ in SECTIONS}}
    table = {}
    for key, (name, fn) in TRACKS.items():
        if key not in parts:
            continue
        path = music_dir / f'{name}.wav'
        if a.check:
            if not path.exists():
                continue
            x = sf.read(path, always_2d=True)[0].T
            print(f'{name}: {x.shape[1]} samples')
        else:
            t0 = time.time()
            song = fn()
            mix, _ = song.render()
            x, m = master(mix, a.lufs, a.tp)
            sf.write(path, x.T, SR, subtype='PCM_24')
            markers[name] = {'file': path.name, **{k: round(v, 6) for k, v in song.markers.items()}}
            print(f'{name}: rendered in {time.time() - t0:4.1f} s; limiter max gain reduction '
                  f'{m["limiter_max_gr_db"]:.1f} dB, {m["limiter_pct_over_1db"]:.2f} % of samples over 1 dB')
        m = ebur128(path=path)                      # measured on the file as written
        info = sf.info(path)
        print(f'  {path.name}: {info.frames} samples = {info.frames / info.samplerate:.6f} s, {info.channels} ch, '
              f'{info.samplerate} Hz, {info.subtype};  ebur128: I {m["I"]:.1f} LUFS, LRA {m["LRA"]:.1f} LU, '
              f'true peak {m["TP"]:.1f} dBTP')
        markers.setdefault(name, {'file': path.name})['measured'] = m
        table[key] = check_track(key, x)
    if table:
        kick_report()
        print_bar_table(table)
        markers_path.write_text(json.dumps(markers, indent=2))
        print('markers ->', markers_path)
    if 'sfx' in parts and not a.check:
        render_stingers(sfx_dir)
    print(f'total {time.time() - t_start:.1f} s')
    return 0


if __name__ == '__main__':
    sys.exit(main())
