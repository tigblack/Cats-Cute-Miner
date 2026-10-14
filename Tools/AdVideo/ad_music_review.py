#!/usr/bin/env python3
"""
Review tool for the ad music beds and stingers written by Tools/AdVideo/ad_music.py.

Nobody can listen on the hub, so this script turns the WAVs into numbers and pictures:
  * numeric checks per track: length, sample peak, clipped samples, DC offset, ffmpeg ebur128 (I / LRA / true peak),
    mono fold-down loudness, L/R correlation, energy above 8 kHz, first/last samples, the level of the final fade;
  * phone-speaker model (4th-order 300 Hz high-pass, 12 kHz low-pass): integrated loudness lost on a phone and the
    per-section loudness that is left (the hook should not vanish on a phone);
  * downbeats vs the 1.875 s bar grid (spectral-flux onset closest to every bar line, bias-calibrated on the kick);
  * all onsets vs the 16th-note grid (offset histogram);
  * click search: 1 ms high-frequency bursts that are not on a 16th-note grid line (swing allowed) are listed;
  * cue windows (t2 scratch / stop / stab / groove, t3 silence / reveal / pre-drop silence, final hit, fade);
  * stingers: length, peak, RMS, DC, first/last samples, clicks.
PNG figures (open them and look):
  <track>_spec.png      log-frequency spectrogram with bar lines and section names
  <track>_beats.png     per-beat loudness (K-weighted) and per-band energy (sub, kick, low-mid, mid, presence, air)
  <track>_cues.png      waveform and spectrogram zooms around the cue points
  onsets.png            onset offsets from the 16th-note grid, downbeat offsets per bar (all tracks)
  ltas.png              long-term average spectrum of each section (drop vs verse vs breakdown)
  stingers.png          waveform + spectrogram of every stinger

Usage (project root, hub):
    Tools/.venv/bin/python -B Tools/AdVideo/ad_music_review.py                 # all tracks + stingers
    Tools/.venv/bin/python -B Tools/AdVideo/ad_music_review.py t2 --no-plots   # numbers only for one track
Options: --dir DIR (default Temp/ads; reads DIR/music and DIR/sfx, writes DIR/music/review).
"""
import argparse
import json
import sys
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import soundfile as sf  # noqa: E402
from scipy.ndimage import maximum_filter1d, median_filter, uniform_filter1d  # noqa: E402
from scipy.signal import butter, find_peaks, sosfilt, stft  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ad_music as A  # noqa: E402  (grid constants, ebur128, K-weighting, the kick used for calibration)

SR, BEAT, BAR, N = A.SR, A.BEAT, A.BAR, A.N
TRACKS = {'t1': 't1_progress', 't2': 't2_idle', 't3': 't3_chest'}
BANDS = [('sub 30-80', 30, 80), ('kick 100-150', 100, 150), ('low-mid 150-500', 150, 500),
         ('mid 0.5-2k', 500, 2000), ('presence 2-6k', 2000, 6000), ('air 8k+', 8000, 20000)]
SECTION_TINT = {'hook': '#f2e6d0', 'verse': '#ffffff', 'build': '#e8eef7', 'drop': '#fbe3e3', 'breakdown': '#eef5ea',
                'end': '#f0f0f0'}


def db(x):
    return 20 * np.log10(np.maximum(x, 1e-12))


def load(path):
    x, sr = sf.read(path, always_2d=True)
    assert sr == SR, f'{path}: {sr} Hz'
    return x.T


def shade_sections(ax):
    for name, a, b in A.SECTIONS:
        ax.axvspan(A.at(a), A.at(b + 1), color=SECTION_TINT[name], zorder=0)
        ax.text((A.at(a) + A.at(b + 1)) / 2, 1.0, name, transform=ax.get_xaxis_transform(), ha='center', va='bottom',
                fontsize=8, color='#555')


# ───────────────────────── onsets ─────────────────────────
HOP = 64


def onset_envelope(mono):
    """Log-magnitude spectral flux (1024-point Hann, hop 64 = 1.33 ms); value k belongs to time k * HOP / SR."""
    f, t, Z = stft(mono, SR, nperseg=1024, noverlap=1024 - HOP, boundary='even', padded=True)
    mag = np.log1p(1000 * np.abs(Z))
    flux = np.maximum(0, np.diff(mag, axis=1)).sum(axis=0)
    return np.concatenate([[0], flux])


_BIAS = None


def flux_bias():
    """Seconds between a kick's true start and the flux peak the detector reports (measured once on real kicks)."""
    global _BIAS
    if _BIAS is None:
        x = np.zeros(4 * SR)
        k = A.kick(1.0, np.random.default_rng(3))
        for t0 in (0.5, 1.5, 2.5):
            i = int(t0 * SR)
            x[i:i + len(k)] += k
        env = onset_envelope(x)
        found = [np.argmax(env[int((t0 - 0.05) * SR / HOP):int((t0 + 0.05) * SR / HOP)]) * HOP / SR + t0 - 0.05
                 for t0 in (0.5, 1.5, 2.5)]
        _BIAS = float(np.mean(np.array(found) - np.array([0.5, 1.5, 2.5])))
    return _BIAS


def onsets(x):
    """Onset times (s, bias-corrected) and strengths from adaptive peak picking on the flux envelope."""
    env = onset_envelope(x.mean(axis=0))
    base = median_filter(env, size=int(0.25 * SR / HOP)) + 0.15 * env.std()
    pk, pr = find_peaks(env, height=base, distance=int(0.045 * SR / HOP))
    return pk * HOP / SR - flux_bias(), env[pk] / env.max(), env


def grid_offset(t, step):
    return (t + step / 2) % step - step / 2


def downbeat_offsets(env):
    """For every bar line: offset (ms) and strength (0..1) of the flux peak closest to the bar line among the peaks
    within +-60 ms that reach half the strongest one (a 32nd-note snare roll right before a hit is not the hit)."""
    out = []
    w = int(0.06 * SR / HOP)
    for b in range(1, A.BARS + 1):
        c = int((A.at(b) + flux_bias()) * SR / HOP)
        a0 = max(0, c - w)
        seg = env[a0:c + w + 1]
        pk, _ = find_peaks(np.concatenate([[0], seg, [0]]))
        pk = pk - 1
        pk = pk[seg[pk] >= 0.5 * seg.max()] if len(pk) else np.array([int(np.argmax(seg))])
        k = int(pk[np.argmin(np.abs(a0 + pk - c))])
        out.append(((a0 + k - c) * HOP / SR * 1000, float(seg[k] / env.max())))
    return out


# ───────────────────────── clicks ─────────────────────────
def hf_bursts(x, swing=0.0, thresh_db=14.0):
    """1 ms windows whose >9 kHz peak jumps thresh_db above the median of the surrounding 60 ms and that are
    farther than 6 ms from any 16th-note grid line (and its swung position). Returns [(t, jump_db)]."""
    sos = butter(4, 9000, 'highpass', fs=SR, output='sos')
    hp = np.abs(sosfilt(sos, x, axis=-1)).max(axis=0)
    w = int(0.001 * SR)
    nb = len(hp) // w
    e = hp[:nb * w].reshape(nb, w).max(axis=1)
    med = median_filter(e, size=61) + 1e-6
    jump = db(e / med)
    cand = np.where(jump > thresh_db)[0]
    out = []
    six = BEAT / 4
    for i in cand:
        t = i * w / SR
        offs = [abs(grid_offset(t, six)), abs(grid_offset(t - swing * BEAT, six)),
                abs(grid_offset(t - swing * BEAT / 2, six))]
        if min(offs) > 0.006 and e[i] > 10 ** (-60 / 20):
            out.append((round(t, 4), round(float(jump[i]), 1), round(float(db(e[i])), 1)))
    merged = []
    for c in out:
        if merged and c[0] - merged[-1][0] < 0.01:
            if c[1] > merged[-1][1]:
                merged[-1] = c
        else:
            merged.append(c)
    return merged


def step_discontinuities(x, k=8.0, swing=0.0, grid=True):
    """Samples where |x[n+1]-x[n]| exceeds k times the local (5 ms) RMS of the first difference and 1e-3 absolute.
    grid=True drops the ones within 3 ms after a 16th-note line (or its swung position): designed drum attacks."""
    d = np.abs(np.diff(x, axis=-1)).max(axis=0)
    loc = np.sqrt(uniform_filter1d(d ** 2, int(0.005 * SR))) + 1e-9
    idx = np.where((d > k * loc) & (d > 1e-3))[0]
    out = []
    six = BEAT / 4
    for i in idx:
        t = i / SR
        if grid and min((t - sh) % six for sh in (0.0, swing * BEAT, swing * BEAT / 2)) < 0.003:
            continue
        out.append((round(t, 5), round(float(d[i]), 4)))
    return out[:50]


# ───────────────────────── numeric checks ─────────────────────────
def window_db(x, t0, t1):
    seg = x[:, int(t0 * SR):int(t1 * SR)]
    return round(float(db(np.sqrt(np.mean(seg ** 2)))), 1)


def band_db(x, lo, hi, t0, t1):
    mono = x.mean(axis=0)[int(t0 * SR):int(t1 * SR)]
    p = np.abs(np.fft.rfft(mono)) ** 2
    f = np.fft.rfftfreq(len(mono), 1 / SR)
    return round(float(10 * np.log10(p[(f >= lo) & (f < hi)].sum() / p.sum() + 1e-15)), 1)


PHONE_HP = butter(4, 300, 'highpass', fs=SR, output='sos')
PHONE_LP = butter(2, 12000, 'lowpass', fs=SR, output='sos')


def phone(x):
    """Crude phone-speaker model: almost nothing below 300 Hz, soft roll-off at the top."""
    return sosfilt(PHONE_LP, sosfilt(PHONE_HP, x, axis=-1), axis=-1)


def track_numbers(key, x, markers):
    mono = x.mean(axis=0)
    st = A.ebur128(x)
    mo = A.ebur128(np.vstack([mono, mono]))
    ph = phone(x)
    zp = A.k_weight(ph)
    corr = []
    for name, a, b in A.SECTIONS:
        l, r = x[0, int(A.at(a) * SR):int(A.at(b + 1) * SR)], x[1, int(A.at(a) * SR):int(A.at(b + 1) * SR)]
        corr.append((name, round(float(np.corrcoef(l, r)[0, 1]), 2)))
    res = {
        'samples': x.shape[1], 'seconds': x.shape[1] / SR,
        'sample_peak_dbfs': round(float(db(np.abs(x).max())), 2),
        'clipped_samples': int((np.abs(x) >= 0.999).sum()),
        'dc': [float(f'{v:.2e}') for v in x.mean(axis=1)],
        'ebur128': st, 'mono_fold_I': mo['I'], 'mono_minus_stereo_LU': round(mo['I'] - st['I'], 1),
        'lr_correlation': corr,
        'air_8k_share_drop_db': band_db(x, 8000, 24000, A.at(13), A.at(21)),
        'kick_100_150_share_drop_db': band_db(x, 100, 150, A.at(13), A.at(21)),
        'first_sample': [float(f'{v:.2e}') for v in x[:, 0]], 'last_sample': [float(f'{v:.2e}') for v in x[:, -1]],
        'last_50ms_dbfs': window_db(x, 44.95, 45.0), 'last_200ms_dbfs': window_db(x, 44.8, 45.0),
        'first_100ms_dbfs': window_db(x, 0.0, 0.1),
        'phone_I_minus_full_LU': round(A.ebur128(ph)['I'] - st['I'], 1),
        'phone_section_LUFS': [(name, round(float(A.loudness_db(zp, A.at(a), A.at(b + 1))), 1))
                               for name, a, b in A.SECTIONS] + [('first 0.5 s', round(float(A.loudness_db(zp, 0, 0.5)), 1))],
    }
    t = markers.get(TRACKS[key], {})
    cues = {'pre_final_hit': window_db(x, 40.9, 41.24), 'final_hit_0.3s': window_db(x, 41.25, 41.55)}
    if key == 't2':
        cues.update({'oom-pah 2.80-3.29': window_db(x, 2.80, 3.29), 'scratch 3.31-3.74': window_db(x, 3.31, 3.74),
                     'stab 3.75-4.20': window_db(x, 3.75, 4.20), 'oops 4.45-5.60': window_db(x, 4.45, 5.60),
                     'groove 5.63-6.50': window_db(x, 5.63, 6.50)})
    if key == 't3':
        cues.update({'suspense 3.0-3.5': window_db(x, 3.0, 3.5), 'silence 3.52-3.745': window_db(x, 3.52, 3.745),
                     'reveal 3.75-4.2': window_db(x, 3.75, 4.2), 'pre-drop silence': window_db(x, 22.27, 22.495),
                     'drop 22.5-23': window_db(x, 22.5, 23.0)})
    res['cues_rms_dbfs'] = cues
    res['markers'] = {k: v for k, v in t.items() if k not in ('file', 'measured')}
    return res


# ───────────────────────── figures ─────────────────────────
def spectrogram(ax, mono, t0=0.0, nfft=4096, hop=512, fmin=30, fmax=20000, vmin=-85):
    f, t, Z = stft(mono, SR, nperseg=nfft, noverlap=nfft - hop)      # scipy scales a sine of amplitude a to a / 2
    S = db(np.abs(Z) * 2)
    keep = (f >= fmin) & (f <= fmax)
    ax.pcolormesh(t + t0, f[keep], S[keep], shading='auto', cmap='Greys', vmin=vmin, vmax=vmin + 65,
                  rasterized=True)
    ax.set_yscale('log')
    ax.set_ylim(fmin, fmax)
    ax.set_ylabel('Hz')


def fig_spec(key, x, out):
    fig, ax = plt.subplots(figsize=(22, 6.5))
    spectrogram(ax, x.mean(axis=0))
    for b in range(1, A.BARS + 2):
        ax.axvline(A.at(b), color='#c0392b' if b in (3, 11, 13, 21, 23) else '#2e86c1', lw=0.8 if b % 2 else 0.4,
                   alpha=0.8)
    for name, a, b in A.SECTIONS:
        ax.text(A.at(a) + 0.05, 17000, name, color='#c0392b', fontsize=9, va='top')
    ax.set_xlim(0, 45)
    ax.set_xticks(np.arange(0, 45.1, BAR))
    ax.set_xticklabels([f'{v:.2f}' for v in np.arange(0, 45.1, BAR)], fontsize=7, rotation=90)
    ax.set_title(f'{TRACKS[key]}: spectrogram (mono), bar lines every 1.875 s (red = section starts)')
    fig.tight_layout()
    fig.savefig(out / f'{key}_spec.png', dpi=80)
    plt.close(fig)


def per_beat(x):
    z = A.k_weight(x)
    nbeats = int(round(A.LENGTH / BEAT))
    lufs, bands = [], {b[0]: [] for b in BANDS}
    mono = x.mean(axis=0)
    for i in range(nbeats):
        a, b = int(i * BEAT * SR), int((i + 1) * BEAT * SR)
        lufs.append(-0.691 + 10 * np.log10(np.mean(z[:, a:b] ** 2, axis=1).sum() + 1e-15))
        p = np.abs(np.fft.rfft(mono[a:b])) ** 2 / (b - a) ** 2 * 2
        f = np.fft.rfftfreq(b - a, 1 / SR)
        for name, lo, hi in BANDS:
            bands[name].append(10 * np.log10(p[(f >= lo) & (f < hi)].sum() + 1e-15))
    return np.arange(nbeats) * BEAT, np.array(lufs), {k: np.array(v) for k, v in bands.items()}


def fig_beats(key, x, out):
    t, lufs, bands = per_beat(x)
    fig, axs = plt.subplots(2, 1, figsize=(22, 9), sharex=True)
    ax = axs[0]
    shade_sections(ax)
    ax.step(t, lufs, where='post', color='#1f4e79', lw=1.6)
    for b in range(1, A.BARS + 2):
        ax.axvline(A.at(b), color='#bbb', lw=0.5)
    ax.set_ylabel('per-beat loudness, LUFS (ungated)')
    ax.set_ylim(-45, -8)
    ax.grid(axis='y', color='#ddd', lw=0.5)
    ax.set_title(f'{TRACKS[key]}: per-beat K-weighted loudness', pad=14)
    ax = axs[1]
    shade_sections(ax)
    colors = ['#08306b', '#2171b5', '#6baed6', '#41ab5d', '#e6550d', '#a50f15']
    for (name, _, _), c in zip(BANDS, colors):
        ax.step(t, bands[name], where='post', color=c, lw=1.2, label=name)
    ax.set_ylabel('band energy per beat, dB')
    ax.set_ylim(-80, -10)
    ax.legend(loc='lower left', ncol=6, fontsize=8)
    ax.grid(axis='y', color='#ddd', lw=0.5)
    ax.set_xlim(0, 45)
    ax.set_xticks(np.arange(0, 45.1, BAR))
    ax.set_xlabel('s (ticks = bar lines)')
    fig.tight_layout()
    fig.savefig(out / f'{key}_beats.png', dpi=72)
    plt.close(fig)


def cue_windows(key):
    common = [('drop 22.5', 21.9, 23.4), ('final hit 41.25 / fade', 40.6, 45.0)]
    if key == 't1':
        return [('hook start', 0.0, 0.8), ('verse 3.75', 3.3, 4.6), ('build 18.75 (hp sweep)', 18.3, 19.6)] + common
    if key == 't2':
        return [('scratch 3.30 (zoom)', 3.27, 3.33), ('oom-pah end / scratch 3.30', 2.6, 3.9),
                ('stab 3.75 / oops', 3.6, 5.7),
                ('groove in 5.625', 5.3, 6.6), ('sleepy 18.75', 18.3, 19.6)] + common
    return [('hook start', 0.0, 0.8), ('silence 3.516 / reveal 3.75', 3.0, 4.6), ('build 18.75', 18.3, 19.6)] \
        + [('pre-drop silence 22.266 / drop', 21.9, 23.4), common[1]]


def fig_cues(key, x, out):
    wins = cue_windows(key)
    fig, axs = plt.subplots(2, len(wins), figsize=(4.2 * len(wins), 7), gridspec_kw={'height_ratios': [1, 1.4]})
    for j, (name, t0, t1) in enumerate(wins):
        seg = x[:, int(t0 * SR):int(t1 * SR)]
        tt = t0 + np.arange(seg.shape[1]) / SR
        ax = axs[0, j]
        ax.plot(tt, seg[0], color='#1f4e79', lw=0.4)
        ax.plot(tt, seg[1], color='#c0392b', lw=0.4, alpha=0.6)
        for b in np.arange(0, 45.01, BEAT / 2):
            if t0 <= b <= t1:
                ax.axvline(b, color='#999' if abs(b / BEAT - round(b / BEAT)) > 1e-6 else '#333',
                           lw=0.5 if abs(b / BAR - round(b / BAR)) > 1e-6 else 1.4, alpha=0.6)
        ax.set_ylim(-0.85, 0.85)
        ax.set_title(name, fontsize=9)
        ax.set_xlim(t0, t1)
        spectrogram(axs[1, j], seg.mean(axis=0), t0, nfft=1024, hop=128)
        axs[1, j].set_xlim(t0, t1)
    fig.suptitle(f'{TRACKS[key]}: cue zooms (L blue, R red; thick line = bar, thin = beat / 8th)')
    fig.tight_layout()
    fig.savefig(out / f'{key}_cues.png', dpi=70)
    plt.close(fig)


def fig_onsets(results, out):
    fig, axs = plt.subplots(2, 1, figsize=(16, 8))
    colors = {'t1': '#1f4e79', 't2': '#e6550d', 't3': '#41ab5d'}
    for key, r in results.items():
        offs = grid_offset(r['onsets'], BEAT / 4) * 1000
        axs[0].hist(offs, bins=np.arange(-60, 61, 2), histtype='step', lw=1.6, color=colors[key],
                    label=f'{key} ({len(offs)} onsets)')
        d = np.array(r['downbeats'])
        axs[1].plot(np.arange(1, A.BARS + 1), d[:, 0], 'o-', color=colors[key], lw=1, ms=4 + 8 * 0, label=key)
    axs[0].set_xlabel('onset offset from the nearest 16th-note grid line, ms')
    axs[0].legend()
    axs[0].set_title('all detected onsets vs the 16th grid (t2 swing puts some at +16 / +33 ms by design)')
    axs[1].axhline(0, color='#333', lw=0.6)
    axs[1].set_xticks(range(1, A.BARS + 1))
    axs[1].set_xlabel('bar')
    axs[1].set_ylabel('strongest onset near the bar line, ms')
    axs[1].set_ylim(-65, 65)
    axs[1].legend()
    axs[1].grid(color='#ddd', lw=0.5)
    fig.tight_layout()
    fig.savefig(out / 'onsets.png', dpi=72)
    plt.close(fig)


def fig_ltas(tracks, out):
    fig, axs = plt.subplots(1, len(tracks), figsize=(6 * len(tracks), 4.5), sharey=True)
    axs = np.atleast_1d(axs)
    for ax, (key, x) in zip(axs, tracks.items()):
        for (name, a, b), c in zip([('hook', 1, 2), ('verse', 3, 10), ('drop', 13, 20), ('breakdown', 21, 22)],
                                   ['#a50f15', '#6baed6', '#08306b', '#41ab5d']):
            seg = x.mean(axis=0)[int(A.at(a) * SR):int(A.at(b + 1) * SR)]
            f, t, Z = stft(seg, SR, nperseg=8192)
            p = (np.abs(Z) ** 2).mean(axis=1)
            # 1/6-octave smoothing on a log axis
            fc = np.geomspace(25, 20000, 140)
            sm = [p[(f >= c / 2 ** (1 / 12)) & (f < c * 2 ** (1 / 12))].mean() if np.any(
                (f >= c / 2 ** (1 / 12)) & (f < c * 2 ** (1 / 12))) else np.nan for c in fc]
            ax.semilogx(fc, 10 * np.log10(np.array(sm) + 1e-20), color=c, lw=1.4, label=name)
        ax.axvspan(100, 150, color='#ffe9a8', zorder=0)
        ax.set_title(f'{TRACKS[key]} long-term spectrum (yellow = 100-150 Hz)')
        ax.set_xlim(25, 20000)
        ax.grid(color='#ddd', lw=0.5)
        ax.legend(fontsize=8)
        ax.set_xlabel('Hz')
    axs[0].set_ylabel('dB (relative)')
    fig.tight_layout()
    fig.savefig(out / 'ltas.png', dpi=72)
    plt.close(fig)


def stinger_numbers(path):
    x = load(path)
    return {'seconds': round(x.shape[1] / SR, 3), 'channels': x.shape[0],
            'peak_dbfs': round(float(db(np.abs(x).max())), 2), 'rms_dbfs': round(float(db(np.sqrt(np.mean(x ** 2)))), 1),
            'dc': float(f'{np.abs(x.mean(axis=1)).max():.1e}'),
            'first': float(f'{np.abs(x[:, 0]).max():.1e}'), 'last': float(f'{np.abs(x[:, -1]).max():.1e}'),
            'last_5ms_dbfs': window_db(x, x.shape[1] / SR - 0.005, x.shape[1] / SR),
            'steps': step_discontinuities(x, grid=False)[:5]}


def fig_stingers(paths, out):
    fig, axs = plt.subplots(2, len(paths), figsize=(2.6 * len(paths), 5.5))
    for j, p in enumerate(paths):
        x = load(p)
        t = np.arange(x.shape[1]) / SR
        axs[0, j].plot(t, x[0], color='#1f4e79', lw=0.4)
        if x.shape[0] > 1:
            axs[0, j].plot(t, x[1], color='#c0392b', lw=0.4, alpha=0.6)
        axs[0, j].set_ylim(-0.75, 0.75)
        axs[0, j].set_title(p.stem, fontsize=9)
        spectrogram(axs[1, j], x.mean(axis=0), nfft=1024, hop=128)
        if j:
            axs[1, j].set_ylabel('')
    fig.tight_layout()
    fig.savefig(out / 'stingers.png', dpi=70)
    plt.close(fig)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('parts', nargs='*', help='t1 t2 t3 sfx (default: all)')
    ap.add_argument('--dir', default=str(A.ROOT / 'Temp' / 'ads'))
    ap.add_argument('--no-plots', action='store_true')
    a = ap.parse_args()
    parts = a.parts or ['t1', 't2', 't3', 'sfx']
    music, sfx = Path(a.dir) / 'music', Path(a.dir) / 'sfx'
    out = music / 'review'
    out.mkdir(parents=True, exist_ok=True)
    markers = json.loads((music / 'markers.json').read_text()) if (music / 'markers.json').exists() else {}
    print(f'onset detector bias (calibrated on the kick): {flux_bias() * 1000:+.1f} ms')
    loaded, results = {}, {}
    for key, name in TRACKS.items():
        if key not in parts or not (music / f'{name}.wav').exists():
            continue
        x = load(music / f'{name}.wav')
        loaded[key] = x
        r = track_numbers(key, x, markers)
        on, strength, env = onsets(x)
        r['onsets'] = on
        r['downbeats'] = downbeat_offsets(env)
        offs = grid_offset(on[strength > 0.08], BEAT / 4) * 1000
        r['onset_offsets_ms'] = {'n': int(len(offs)), 'median': round(float(np.median(offs)), 1),
                                 'within_5ms_pct': round(float(100 * np.mean(np.abs(offs) <= 5)), 1),
                                 'beyond_12ms': int(np.sum(np.abs(offs) > 12))}
        r['hf_bursts_off_grid'] = hf_bursts(x, swing=A.T2_SWING if key == 't2' else 0.0)
        r['step_discontinuities_off_grid'] = step_discontinuities(x, swing=A.T2_SWING if key == 't2' else 0.0)
        results[key] = r
        print(f'\n== {name} ==')
        for k, v in r.items():
            if k in ('onsets',):
                continue
            if k == 'downbeats':
                print('  downbeats (bar: ms, strength): ' + '  '.join(f'{i + 1}:{o:+.0f}/{s:.2f}'
                                                                      for i, (o, s) in enumerate(v)))
                continue
            print(f'  {k}: {v}')
        if not a.no_plots:
            fig_spec(key, x, out)
            fig_beats(key, x, out)
            fig_cues(key, x, out)
    if results and not a.no_plots:
        fig_onsets(results, out)
        fig_ltas(loaded, out)
    if 'sfx' in parts and sfx.exists():
        paths = sorted(sfx.glob('*.wav'))
        print('\n== stingers ==')
        for p in paths:
            print(f'  {p.stem:8s} {stinger_numbers(p)}')
        if not a.no_plots and paths:
            fig_stingers(paths, out)
    print('\nfigures ->', out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
