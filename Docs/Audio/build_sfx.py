#!/usr/bin/env python3
"""
Сборка игровых звуков из исходников CC0 (см. SOUNDS.md, CREDITS.md).

Исходные паки скачиваются на ПК в build/audio-src/ и копируются на хаб в Temp/audio/src/ (бинарники не синкаются).
Скрипт режет, фильтрует, сводит слои и нормирует громкость, пишет OGG Vorbis моно 44,1 кГц в --out.
Готовые файлы кладутся на ПК в Assets/Game/Resources/Audio/ (SOUNDS.md, «Как заменить звук»).

Запуск на хабе:  Tools/.venv/bin/python Docs/Audio/build_sfx.py --src Temp/audio/src --out Temp/audio/out [имена…]
Нужны numpy, scipy, soundfile (venv Tools/.venv, только на хабе).

Раскладка --src (как распакованы архивы из CREDITS.md):
  impact-sounds/, rpg-audio/, music-jingles/   — паки Kenney (внутри Audio/…);
  rd-rpg/, rd-break/                           — rubberduck 80 CC0 RPG SFX и 75 CC0 breaking/falling/hit;
  spring/<имя>.wav                             — Spring Spring «Various Sound Effects»;
  meow/bsb_<номер>.flac, meow/Meow.ogg         — BigSoundBank (Joseph Sardin) и IgnasD.
"""
import argparse, os, sys
import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfilt, resample_poly

SR = 44100

# Короткие имена паков → каталоги внутри --src.
K_IMPACT = "impact-sounds/Audio"
K_RPG = "rpg-audio/Audio"
K_JINGLE = "music-jingles/Audio"
RD_RPG = "rd-rpg"
RD_BREAK = "rd-break"
SPRING = "spring"
MEOW = "meow"


# ───────────── примитивы ─────────────

def load(src, rel, start=None, end=None):
    """Моно 44,1 кГц, при необходимости — вырезка [start, end) в секундах исходника."""
    x, sr = sf.read(os.path.join(src, rel), always_2d=True)
    x = x.mean(axis=1)
    if start is not None or end is not None:
        a = int((start or 0) * sr)
        b = int(end * sr) if end is not None else len(x)
        x = x[a:b]
    if sr != SR:
        g = np.gcd(sr, SR)
        x = resample_poly(x, SR // g, sr // g)
    return x.astype(np.float64)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, "highpass", fs=SR, output="sos"), x)


def lp(x, f, order=2):
    return sosfilt(butter(order, f, "lowpass", fs=SR, output="sos"), x)


def pitch(x, semitones):
    """Сдвиг высоты пересэмплированием (меняет и длительность — как у AudioSource.pitch)."""
    if semitones == 0:
        return x
    ratio = 2 ** (semitones / 12.0)
    up, down = 1000, int(round(1000 * ratio))
    g = np.gcd(up, down)
    return resample_poly(x, up // g, down // g)


def strip_lead(x, db=-45.0, keep_ms=2.0):
    """Убрать тишину в начале: звук должен начинаться в кадр события."""
    thr = np.max(np.abs(x)) * 10 ** (db / 20)
    i = int(np.argmax(np.abs(x) > thr))
    return x[max(0, i - int(keep_ms * SR / 1000)):]


def shape(x, length=None, fade_from=None, fade_in_ms=1.5):
    """Обрезка до length секунд с плавным (косинусным) затуханием от fade_from; короткая атака без щелчка."""
    x = x.copy()
    n_in = max(1, int(fade_in_ms * SR / 1000))
    x[:n_in] *= np.linspace(0, 1, n_in)
    if length is not None:
        x = x[:int(length * SR)]
        if len(x) < int(length * SR):
            x = np.pad(x, (0, int(length * SR) - len(x)))
    if fade_from is not None:
        a = int(fade_from * SR)
        n = len(x) - a
        if n > 0:
            x[a:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, n)))
    else:
        n_out = int(0.01 * SR)
        x[-n_out:] *= np.linspace(1, 0, n_out)
    return x


def mix(*layers):
    """layers: (сигнал, задержка_с, усиление_дБ)."""
    n = max(int(d * SR) + len(s) for s, d, g in layers)
    out = np.zeros(n)
    for s, d, g in layers:
        a = int(d * SR)
        out[a:a + len(s)] += s * 10 ** (g / 20)
    return out


def active_rms_db(x):
    win = int(0.01 * SR)
    e = np.sqrt(np.convolve(x * x, np.ones(win) / win, "same"))
    act = e > np.max(e) * 0.1  # −20 дБ от максимума огибающей
    return 20 * np.log10(np.sqrt(np.mean(x[act] ** 2)) + 1e-12)


def normalize(x, rms_db=-18.0, peak_db=-1.0):
    """Громкость по RMS звучащей части, с ограничением пика: таблица громкостей в Sfx задаёт микс."""
    g = 10 ** ((rms_db - active_rms_db(x)) / 20)
    peak = np.max(np.abs(x)) * g
    cap = 10 ** (peak_db / 20)
    if peak > cap:
        g *= cap / peak
    return x * g


# ───────────── рецепты ─────────────
# Каждый рецепт: имя файла → функция(src) → сигнал. Имена — контракт с Sfx.cs (Resources/Audio/<имя>).

def r_hit(n):
    # Кирка по камню: только «щелчок» удара; гулкий низ (<180 Гц, гудит ~0,5 с) срезан, хвост укорочен до 0,2 с,
    # чтобы удары многих котиков не сливались в гул.
    def f(src):
        x = strip_lead(load(src, f"{K_IMPACT}/impactMining_00{n}.ogg"))
        x = lp(hp(x, 180, 2), 7000)
        return shape(x, 0.20, 0.03)
    return f


def r_break(n, pebbles):
    # Камень рассыпается: удар-«треск» (тот же камень, укороченный) + россыпь камешков, приглушённая сверху.
    def f(src):
        crack = shape(lp(hp(strip_lead(load(src, f"{K_IMPACT}/impactMining_00{n}.ogg")), 120, 2), 6000), 0.16, 0.04)
        peb = shape(lp(hp(strip_lead(load(src, pebbles)), 150), 5000), 0.42, 0.25)
        return shape(mix((crack, 0, 0), (peb, 0.025, -3)), 0.45, 0.30)
    return f


def r_coin(n, interval=5):
    # Монетка: два «динь» лёгкого металла, второй выше на interval полутонов (классическое «ба-динь»), без резкого верха.
    def f(src):
        t = strip_lead(load(src, f"{K_IMPACT}/impactMetal_light_00{n}.ogg"))
        a = shape(lp(t, 6000, 4), 0.16, 0.06)
        # Фильтр после сдвига высоты: иначе верхний обертон уезжает выше 6 кГц и «звенит».
        b = shape(lp(pitch(t, interval), 6000, 4), 0.26, 0.08)
        return shape(mix((a, 0, -4), (b, 0.07, 0)), 0.32, 0.22)
    return f


def r_chime(semi):
    # Эликсир/кристаллы: мягкий колокольчик (тональный, ~740 Гц), без металлического «динь» монеты.
    def f(src):
        x = lp(hp(strip_lead(load(src, f"{SPRING}/small_amber.wav")), 250), 7000)
        return shape(pitch(x, semi), 0.5, 0.18)
    return f


def r_payout(coins):
    # Сбор выплаты: горсть монет (приглушённая сверху) + «динь» монетки поверх.
    def f(src):
        c = shape(lp(hp(strip_lead(load(src, coins)), 300), 5000, 4), 0.5, 0.3)
        t = strip_lead(load(src, f"{K_IMPACT}/impactMetal_light_001.ogg"))
        t = shape(lp(pitch(t, 5), 6000, 4), 0.3, 0.08)
        return shape(mix((c, 0, 0), (t, 0.04, -5)), 0.55, 0.35)
    return f


def r_meow(rel, start=None, end=None, semi=0.0, length=None):
    # Мяу: вырезан один короткий слог, срезан низкий шум помещения, верх смягчён.
    def f(src):
        x = load(src, rel, start, end)
        x = lp(hp(x, 320, 2), 9000)
        x = pitch(strip_lead(x, -35.0), semi)
        L = length or len(x) / SR
        return shape(x, L, max(0.0, L - 0.06), fade_in_ms=4)
    return f


def r_merge(src):
    # Объединение: «буп» (два котика схлопнулись) + искристый колокольчик чуть позже.
    pop = shape(lp(strip_lead(load(src, f"{SPRING}/bup.wav")), 6000), 0.14, 0.08)
    ch = lp(hp(strip_lead(load(src, f"{SPRING}/big_amber.wav")), 300), 9000)
    ch = shape(ch, 0.6, 0.2)
    return shape(mix((pop, 0, 0), (ch, 0.05, -2)), 0.65, 0.4)


def r_jingle(rel, length, fade_from, lowpass=9000):
    # Короткие джинглы Kenney (пиццикато / стил-драм): только обрезка хвоста и мягкий верх.
    def f(src):
        x = lp(strip_lead(load(src, f"{K_JINGLE}/{rel}")), lowpass)
        return shape(x, length, fade_from, fade_in_ms=2)
    return f


def r_click(src):
    # Кнопка: короткий мягкий «буп» чуть выше по тону — без щелчка-клика и без верха.
    x = lp(strip_lead(load(src, f"{SPRING}/bup.wav")), 5000)
    return shape(pitch(x, 3), 0.10, 0.05)


def r_open(src):
    # Открытие панели: мягкий «шурх» ткани, тёплый (верх срезан), короткий.
    x = lp(hp(strip_lead(load(src, f"{K_RPG}/cloth2.ogg")), 250), 4000)
    return shape(x, 0.24, 0.10, fade_in_ms=12)


def r_close(src):
    # Закрытие панели: глухой мягкий «хлоп» закрытой книги.
    x = lp(hp(strip_lead(load(src, f"{K_RPG}/bookClose.ogg"), -30.0), 150), 4000)
    return shape(x, 0.18, 0.06)


def r_denied(src):
    # Нельзя: двойной деревянный «тук-тук», второй ниже — мягко и понятно, без зуммера.
    a = shape(strip_lead(load(src, f"{K_IMPACT}/impactWood_light_000.ogg")), 0.09, 0.03)
    b = shape(pitch(strip_lead(load(src, f"{K_IMPACT}/impactWood_light_002.ogg")), -3), 0.11, 0.03)
    return lp(hp(mix((a, 0, 0), (b, 0.11, -1)), 150), 5000)


def r_dynamite(src):
    # Динамит: мягкий «поп» + глухой короткий бум (низ оставлен в пределах слышимого телефоном) + немного камешков.
    pop = shape(lp(strip_lead(load(src, f"{SPRING}/bup.wav")), 4000), 0.14, 0.08)
    pop = pitch(pop, -4)
    boom = load(src, f"{SPRING}/dull_explosion.wav")
    boom = shape(lp(hp(strip_lead(boom), 60, 2), 3500), 0.75, 0.2)
    peb = shape(lp(hp(strip_lead(load(src, f"{RD_BREAK}/bfh1_rock_falling_02.ogg")), 200), 5000), 0.45, 0.25)
    return shape(mix((pop, 0, -2), (boom, 0.02, 0), (peb, 0.12, -9)), 0.8, 0.45)


def r_cannon(semi):
    # Пушка у кузницы выстрелила бочкой: мягкий глухой «пумф» — «буп» на октаву ниже, короткий приглушённый хлопок
    # и шорох вылетающей бочки. Без резкого верха и без долгого гула: залп звучит при каждом найме.
    def f(src):
        pop = shape(pitch(lp(strip_lead(load(src, f"{SPRING}/bup.wav")), 3000), -7 + semi), 0.22, 0.10)
        boom = shape(lp(hp(strip_lead(load(src, f"{SPRING}/dull_explosion.wav")), 70, 2), 1800), 0.30, 0.08)
        swish = shape(pitch(lp(hp(strip_lead(load(src, f"{K_RPG}/cloth2.ogg")), 600), 5000), 4), 0.20, 0.06, fade_in_ms=15)
        return shape(mix((pop, 0, 0), (boom, 0.005, -5), (swish, 0.05, -6)), 0.45, 0.25)
    return f


def r_land(rel, semi):
    # Бочка из пушки приземлилась: деревянный «тук» ниже тоном (тяжелее) + мягкий глухой хлопок для веса.
    def f(src):
        knock = shape(pitch(strip_lead(load(src, f"{K_IMPACT}/{rel}")), semi), 0.16, 0.05)
        body = shape(lp(hp(strip_lead(load(src, f"{K_RPG}/bookClose.ogg"), -30.0), 90), 2500), 0.18, 0.06)
        return shape(lp(hp(mix((knock, 0, 0), (body, 0.0, -6)), 120), 5000), 0.2, 0.1)
    return f


def r_select(src):
    # Выбор котика или объекта: очень короткий мягкий «тик» — «буп» на квинту выше, без верха.
    x = lp(strip_lead(load(src, f"{SPRING}/bup.wav")), 4000)
    return shape(pitch(x, 7), 0.07, 0.03)


def r_hit_crystal(semi, seed):
    # Удар по самоцветам и эликсирным кристаллам: глухой удар кирки (−8 дБ) и стеклянный «тинь» из неровных обертонов
    # 1 : 2,76 : 5,4 с быстрым затуханием; верх срезан на 6 кГц.
    def f(src):
        rng = np.random.default_rng(seed)
        knock = shape(lp(hp(strip_lead(load(src, f"{K_IMPACT}/impactMining_00{seed % 3}.ogg")), 180, 2), 5000), 0.14, 0.04)
        n = int(0.32 * SR)
        t = np.arange(n) / SR
        f0 = 1180 * 2 ** (semi / 12)
        tink = sum(g * np.sin(2 * np.pi * f0 * r * t + rng.uniform(0, 6.28)) * np.exp(-t / d)
                   for r, g, d in ((1.0, 1.0, 0.09), (2.76, 0.35, 0.05), (5.4, 0.12, 0.025)))
        tink[:int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
        return lp(shape(mix((knock, 0, -8), (tink, 0.002, -2)), 0.32, 0.12), 6000, 4)
    return f


def r_hit_wood(rel, semi):
    # Удар по дереву (створки ворот, доски выхода, завал шахты): лёгкий деревянный удар ниже тоном и глухое «тело».
    def f(src):
        knock = shape(pitch(strip_lead(load(src, f"{K_IMPACT}/{rel}")), semi), 0.16, 0.05)
        body = shape(lp(hp(strip_lead(load(src, f"{K_RPG}/bookClose.ogg"), -30.0), 90), 1800), 0.16, 0.05)
        return lp(hp(mix((knock, 0, 0), (body, 0.0, -7)), 110), 4500)
    return f


def r_hit_metal(rel, semi):
    # Удар по сундуку в породе и жетону: металлический «дзинь» ниже тоном, верх мягкий, хвост короткий.
    def f(src):
        x = shape(pitch(strip_lead(load(src, f"{K_IMPACT}/{rel}")), semi), 0.22, 0.06)
        k = shape(lp(hp(strip_lead(load(src, f"{K_IMPACT}/impactMining_001.ogg")), 180, 2), 4000), 0.12, 0.04)
        return lp(mix((x, 0, -2), (k, 0, -6)), 5500, 4)
    return f


# ───────────── оружие котика (CatLook, круг из 8 уровней) ─────────────
# Кирка звучит по материалу цели (hit_*, hit_crystal_*, …); у остального оружия — свой звук. Моторные инструменты —
# короткие (0,3–0,4 с), без резкого верха: их слышно часто и сразу у нескольких котиков.

def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], "bandpass", fs=SR, output="sos"), x)


def _env(n, attack_ms, decay_s):
    t = np.arange(n) / SR
    e = np.exp(-t / decay_s)
    na = max(1, int(attack_ms * SR / 1000))
    e[:na] *= np.linspace(0, 1, na)
    return e


def _knock(src, n, semi=0.0, hp_hz=180, lp_hz=6000, length=0.2, fade=0.07):
    x = lp(hp(strip_lead(load(src, f"{K_IMPACT}/impactMining_00{n}.ogg")), hp_hz, 2), lp_hz)
    return shape(pitch(x, semi), length, fade)


def r_weapon_hammer(variant):
    # Молот: тяжёлый глухой удар — удар кирки ниже на 5–6 полутонов, «тело» ≈85 Гц с падающей высотой.
    def f(src):
        knock = _knock(src, (0, 3)[variant], -5 - variant, 120, 4500, 0.32, 0.12)
        n = int(0.3 * SR)
        t = np.arange(n) / SR
        body = np.sin(2 * np.pi * np.cumsum(85 + 45 * np.exp(-t / 0.02)) / SR) * _env(n, 1, 0.07)
        return shape(lp(mix((knock, 0, 0), (body, 0, -5)), 5000), 0.3, 0.2)
    return f


def r_weapon_crusher(variant):
    # Дробилка: зубья барабана — три быстрых сухих удара через 35 мс по убыванию и хруст камня.
    def f(src):
        hits = [_knock(src, k, 2 + variant, 200, 6000, 0.09, 0.03) for k in (1, 2, 4)]
        n = int(0.22 * SR)
        grit = bp(np.random.default_rng(40 + variant).standard_normal(n), 900, 3500) * _env(n, 2, 0.06)
        return shape(lp(mix((hits[0], 0, 0), (hits[1], 0.035, -3), (hits[2], 0.07, -6), (grit, 0.0, -14)), 6500), 0.22, 0.16)
    return f


def r_weapon_saw(variant):
    # Циркулярная пила: короткий «вжик» — мотор (пилообразный тон ≈170→210 Гц, верх срезан), визг диска по камню
    # (полоса шума 1,8–3,8 кГц с дрожанием 38 Гц) и укус зубьев.
    def f(src):
        n = int(0.32 * SR)
        t = np.arange(n) / SR
        f0 = 170 + 40 * (1 - np.exp(-t / 0.06)) + 6 * variant
        ph = 2 * np.pi * np.cumsum(f0) / SR
        motor = lp(sum(np.sin(k * ph) / k for k in range(1, 9)), 1800) * _env(n, 8, 0.16)
        rng = np.random.default_rng(50 + variant)
        scream = bp(rng.standard_normal(n), 1800, 3800) * (0.6 + 0.4 * np.sin(2 * np.pi * 38 * t)) * _env(n, 15, 0.1)
        bite = _knock(src, 1, 0, 300, 5000, 0.1, 0.04)
        return shape(lp(mix((motor, 0, -2), (scream, 0.01, -10), (bite, 0.0, -8)), 6000), 0.32, 0.24)
    return f


def r_weapon_drill(variant):
    # Бур-машина: вой мотора (≈520→560 Гц, вибрато), низкий гул ≈70 Гц, хруст сверла (шум 1–3 кГц, пульсация 55 Гц).
    def f(src):
        n = int(0.34 * SR)
        t = np.arange(n) / SR
        f0 = 520 + 40 * (1 - np.exp(-t / 0.05)) + 9 * np.sin(2 * np.pi * 9 * t) + 15 * variant
        ph = 2 * np.pi * np.cumsum(f0) / SR
        whine = (np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)) * _env(n, 12, 0.2)
        hum = np.sin(2 * np.pi * 70 * t) * _env(n, 10, 0.18)
        rng = np.random.default_rng(60 + variant)
        grind = bp(rng.standard_normal(n), 1000, 3000) * (0.55 + 0.45 * np.sign(np.sin(2 * np.pi * 55 * t))) * _env(n, 20, 0.12)
        return shape(lp(mix((whine, 0, -4), (hum, 0, -8), (grind, 0.02, -11)), 4500), 0.34, 0.26)
    return f


def r_weapon_dualpick(variant):
    # Две кирки: два удара подряд через 70 мс, второй чуть выше — «тук-тук».
    def f(src):
        a = _knock(src, (0, 1)[variant], 0, 180, 7000, 0.18, 0.06)
        b = _knock(src, (3, 4)[variant], 2, 180, 7000, 0.18, 0.06)
        return mix((a, 0, 0), (b, 0.07, -2))
    return f


def r_weapon_chainsaw(variant):
    # Бензопила: рокот двигателя (импульсы ≈95 Гц с дрожанием высоты, мягко срезан), цепь по камню (шум 1,5–3,5 кГц,
    # пульсация 48 Гц).
    def f(src):
        n = int(0.38 * SR)
        t = np.arange(n) / SR
        f0 = 92 + 10 * np.sin(2 * np.pi * 7 * t) + 30 * (1 - np.exp(-t / 0.08)) + 4 * variant
        ph = 2 * np.pi * np.cumsum(f0) / SR
        engine = lp(np.tanh(3 * np.sin(ph)), 1200) * _env(n, 10, 0.22)
        rng = np.random.default_rng(70 + variant)
        chain = bp(rng.standard_normal(n), 1500, 3500) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 48 * t))) * _env(n, 20, 0.15)
        return shape(lp(mix((engine, 0, 0), (chain, 0.02, -12)), 5000), 0.38, 0.28)
    return f


def r_weapon_laser(variant):
    # Лазерган: мягкий «пиу» — синус 1500→450 Гц за ≈0,15 с с лёгкой FM-дрожью, тихий шорох вспышки; верх срезан на
    # 5 кГц, без резкости.
    def f(src):
        n = int(0.26 * SR)
        t = np.arange(n) / SR
        f0 = 450 + 1050 * np.exp(-t / 0.05) + 30 * variant
        ph = 2 * np.pi * np.cumsum(f0) / SR
        tone = np.sin(ph + 0.6 * np.sin(2 * np.pi * 55 * t)) * _env(n, 2, 0.09)
        overtone = 0.15 * np.sin(2 * ph) * _env(n, 2, 0.05)
        rng = np.random.default_rng(80 + variant)
        zap = bp(rng.standard_normal(n), 2500, 6000) * _env(n, 1, 0.015)
        return shape(lp(mix((tone, 0, 0), (overtone, 0, 0), (zap, 0, -14)), 5000), 0.26, 0.18)
    return f


RECIPES = {
    "hit_0": r_hit(0), "hit_1": r_hit(1), "hit_2": r_hit(3), "hit_3": r_hit(4),
    "break_0": r_break(2, f"{RD_BREAK}/bfh1_rock_falling_07.ogg"),
    "break_1": r_break(0, f"{RD_BREAK}/bfh1_rock_falling_02.ogg"),
    "break_2": r_break(4, f"{RD_RPG}/stones_04.ogg"),
    "coin_0": r_coin(1, 4), "coin_1": r_coin(1, 5), "coin_2": r_coin(4, 5),
    "chime_0": r_chime(0), "chime_1": r_chime(2),
    "payout_0": r_payout(f"{RD_RPG}/item_coins_02.ogg"), "payout_1": r_payout(f"{RD_RPG}/item_coins_03.ogg"),
    "meow_0": r_meow(f"{MEOW}/bsb_1895.flac", 0.58, 0.82),
    "meow_1": r_meow(f"{MEOW}/bsb_1899.flac", 0.0, 0.44),
    "meow_2": r_meow(f"{MEOW}/bsb_1894.flac", 1.44, 1.70),
    "meow_3": r_meow(f"{MEOW}/Meow.ogg", 0.0, 0.45),
    "merge_0": r_merge,
    "upgrade_0": r_jingle("Pizzicato jingles/jingles_PIZZI08.ogg", 0.55, 0.3),
    "unlock_0": r_jingle("Pizzicato jingles/jingles_PIZZI10.ogg", 0.80, 0.5),
    "chest_0": r_jingle("Pizzicato jingles/jingles_PIZZI16.ogg", 0.46, 0.25),
    "gate_0": r_jingle("Steel jingles/jingles_STEEL10.ogg", 0.95, 0.6),
    "exit_0": r_jingle("Steel jingles/jingles_STEEL04.ogg", 0.95, 0.6),
    "click_0": r_click, "open_0": r_open, "close_0": r_close, "denied_0": r_denied,
    "dynamite_0": r_dynamite,
    "cannon_0": r_cannon(0), "cannon_1": r_cannon(-1),
    "land_0": r_land("impactWood_light_000.ogg", -3), "land_1": r_land("impactWood_light_002.ogg", -4),
    "select_0": r_select,
    "hit_crystal_0": r_hit_crystal(0, 0), "hit_crystal_1": r_hit_crystal(2, 1), "hit_crystal_2": r_hit_crystal(-2, 2),
    "hit_wood_0": r_hit_wood("impactWood_light_000.ogg", -2), "hit_wood_1": r_hit_wood("impactWood_light_002.ogg", -3),
    "hit_wood_2": r_hit_wood("impactWood_light_000.ogg", -5),
    "hit_metal_0": r_hit_metal("impactMetal_light_001.ogg", -5), "hit_metal_1": r_hit_metal("impactMetal_light_004.ogg", -6),
    **{f"weapon_{name}_{v}": fn(v) for name, fn in (("hammer", r_weapon_hammer), ("crusher", r_weapon_crusher),
                                                     ("saw", r_weapon_saw), ("drill", r_weapon_drill),
                                                     ("dualpick", r_weapon_dualpick), ("chainsaw", r_weapon_chainsaw),
                                                     ("laser", r_weapon_laser)) for v in (0, 1)},
}

# Целевая громкость (RMS звучащей части, дБFS): все файлы примерно равны по громкости,
# микс задаётся таблицей громкостей в Sfx.cs.
TARGET_RMS = -18.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("only", nargs="*")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    for name, fn in RECIPES.items():
        if a.only and name not in a.only:
            continue
        x = normalize(fn(a.src), TARGET_RMS)
        path = os.path.join(a.out, name + ".ogg")
        # compression_level 0.3 ≈ Vorbis q0.7: файлы маленькие, Unity всё равно перекодирует при импорте.
        sf.write(path, x.astype(np.float32), SR, format="OGG", subtype="VORBIS", compression_level=0.3)
        print(f"{name:12s} {len(x) / SR:5.2f}s  {os.path.getsize(path):6d} B")


if __name__ == "__main__":
    sys.exit(main())
