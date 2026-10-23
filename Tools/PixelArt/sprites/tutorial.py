"""Наставник обучения (группа tutorial): кот-бригадир для пузыря подсказок и круглая рамка его портрета.

tutor_cat_0/1/2 — портрет по грудь 40×40, pivot — центр: 0 — спокойная улыбка, 1 — говорит (рот открыт), 2 — моргает.
Пожилой добрый кот: серо-белая шерсть, пышные белые брови и усы, латунные очки подняты на потёртую жёлтую каску
с фонариком, красный шейный платок, синий комбинезон. Повёрнут на 3/4 вправо — к пузырю, который стоит справа.
ui_portrait_frame — круглая рамка 52×52, pivot — центр: латунное кольцо с бликом, внутри тёмно-синий диск Ø42.
Портрет ставится центром в центр рамки: силуэт уже обрезан по диску и на кольцо не заходит.
"""
from __future__ import annotations

import math
from typing import Dict, Iterable, Sequence, Set, Tuple

from pxl import Canvas

Px = Tuple[int, int]
Mask = Set[Px]

BW = BH = 40                 # холст портрета
FW = FH = 52                 # холст рамки
DISC_R = 21.0                # радиус тёмного диска рамки (и обрезки портрета)
CX = CY = 20.0               # центр портрета (совпадает с центром рамки)


# ───────────────────────── маски ─────────────────────────
def m_ellipse(cx: float, cy: float, rx: float, ry: float) -> Mask:
    return {(x, y) for y in range(BH) for x in range(BW)
            if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0}


def m_rows(rows: Sequence[str], x0: int, y0: int) -> Mask:
    return {(x0 + i, y0 + j) for j, r in enumerate(rows) for i, ch in enumerate(r) if ch not in '. '}


def m_disc() -> Mask:
    return {(x, y) for y in range(BH) for x in range(BW) if (x + 0.5 - CX) ** 2 + (y + 0.5 - CY) ** 2 <= DISC_R ** 2}


def fill(cv: Canvas, m: Iterable[Px], c: str) -> None:
    for x, y in m:
        cv.set(x, y, c)


def edges(m: Mask) -> Dict[Px, str]:
    """Пиксели маски у её края с направлениями открытых сторон 'udlr'."""
    out: Dict[Px, str] = {}
    for x, y in m:
        d = ''
        if (x, y - 1) not in m:
            d += 'u'
        if (x, y + 1) not in m:
            d += 'd'
        if (x - 1, y) not in m:
            d += 'l'
        if (x + 1, y) not in m:
            d += 'r'
        if d:
            out[(x, y)] = d
    return out


def outline(m: Mask) -> Mask:
    """Контур 1 px снаружи маски по 4-соседству."""
    out: Mask = set()
    for x, y in m:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            if q not in m:
                out.add(q)
    return out


def stamp(cv: Canvas, rows: Sequence[str], legend: Dict[str, str], x0: int, y0: int) -> None:
    for j, r in enumerate(rows):
        for i, ch in enumerate(r):
            if ch in '. ':
                continue
            cv.set(x0 + i, y0 + j, legend[ch])


def sphere(cv: Canvas, m: Mask, cx: float, cy: float, rx: float, ry: float, tones: Sequence[str],
           cuts: Sequence[float]) -> None:
    """Объём купола: нормаль эллипсоида · свет сверху-слева-спереди, ступени tones по порогам cuts."""
    lx, ly, lz = -0.55, -0.62, 0.56
    for x, y in m:
        nx = (x + 0.5 - cx) / rx
        ny = (y + 0.5 - cy) / ry
        nz = math.sqrt(max(0.0, 1.0 - nx * nx - ny * ny))
        k = nx * lx + ny * ly + nz * lz
        i = sum(1 for c in cuts if k > c)
        cv.set(x, y, tones[i])


def ring_shade(cv: Canvas, cx: float, cy: float, r_out: float, r_in: float, tones: Sequence[str],
               cuts: Sequence[float]) -> None:
    """Обод-тор (оправа очков): свет сверху-слева, внешняя кромка светится сверху-слева, внутренняя — снизу-справа."""
    lx, ly = -0.7071, -0.7071
    for y in range(cv.h):
        for x in range(cv.w):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            if not (r_in < r <= r_out):
                continue
            ux, uy = dx / max(r, 1e-6), dy / max(r, 1e-6)
            t = (r - r_in) / (r_out - r_in) * 2.0 - 1.0          # −1 внутренняя кромка … +1 внешняя
            nz = math.sqrt(max(0.0, 1.0 - t * t))
            k = (ux * lx + uy * ly) * t * 0.9 + nz * 0.55
            i = sum(1 for c in cuts if k > c)
            cv.set(x, y, tones[i])


# ───────────────────────── каска ─────────────────────────
# Ушки сквозь каску на «плечах» купола (как у котиков-шахтёров): левое светлое, правое в тени.
EAR_L = [
    'oo....',
    'olo...',
    'olpo..',
    'olppo.',
    'oflppo',
    '.offfo',
]
EAR_R = [
    '....oo',
    '...omo',
    '..oPmo',
    '.oPPmo',
    'oPPmmo',
    'ommmo.',
]
EAR_L_AT, EAR_R_AT = (8, 2), (26, 2)
LAMP = [
    '.LLLLL.',
    'LMaAaML',
    'LMa*aML',
    'LLaaaLL',
    '.LLLLL.',
]
LAMP_AT = (17, 0)
# Очки: центр и радиус линз; правая чуть меньше — голова повёрнута на 3/4 вправо.
LENS_L, LENS_R = (16.0, 8.5, 3.7), (24.6, 8.5, 3.4)
DOME = (20.0, 13.0, 12.0, 12.5)
BRIM = (20.0, 12.3, 14.2, 2.2)


def helmet() -> Canvas:
    cv = Canvas(BW, BH)
    dome = {(x, y) for (x, y) in m_ellipse(*DOME) if y <= 11}
    dcx, dcy, drx, dry = DOME
    sphere(cv, dome, dcx - 2, dcy, drx + 0.5, dry + 0.5, ['gold1', 'gold2', 'gold3', 'gold4', 'gold5'], [0.0, 0.30, 0.70, 0.93])
    brim = m_ellipse(*BRIM) - dome
    for x, y in brim:
        top = (x, y - 1) not in brim and (x, y - 1) not in dome
        cv.set(x, y, 'gold2' if y >= 13 else 'gold4' if top else 'gold3')
    for (x, y), d in edges(brim).items():
        if 'r' in d:
            cv.set(x, y, 'gold2')
    full = dome | brim
    # Ремешок очков по бокам купола.
    for x, y in dome:
        if y in (8, 9):
            cv.set(x, y, 'wood3' if y == 8 else 'wood2')
    # Потёртости: царапины на куполе, сколы на поле.
    for x, y, c in [(13, 4, 'gold2'), (14, 3, 'gold2'), (27, 6, 'gold1'), (28, 5, 'gold1'), (11, 6, 'gold2'),
                    (8, 13, 'gold1'), (9, 13, 'gold1'), (30, 12, 'gold4'), (31, 13, 'gold1')]:
        if (x, y) in full:
            cv.set(x, y, c)
    fill(cv, outline(full), 'gold0')
    # Ушки сквозь каску.
    for rows, (ex, ey) in ((EAR_L, EAR_L_AT), (EAR_R, EAR_R_AT)):
        stamp(cv, rows, {'o': 'fur_gray0', 'l': 'fur_gray4', 'f': 'fur_gray3', 'm': 'fur_gray2', 'p': 'pink2',
                         'P': 'pink1'}, ex, ey)
    stamp(cv, LAMP, {'L': 'navy1', 'M': 'navy3', 'a': 'amber4', 'A': 'amber5', '*': 'white'}, *LAMP_AT)
    # Очки-гогглы: латунный обод, голубое стекло с бликом.
    goggles = Canvas(BW, BH)
    for cx, cy, r in (LENS_L, LENS_R):
        ring_shade(goggles, cx, cy, r, r - 1.5, ['amber1', 'amber2', 'amber3', 'amber4'], [-0.25, 0.15, 0.5])
        glass = Canvas(BW, BH)
        glass.ellipse(cx, cy, r - 1.5, r - 1.5, 'blue4')
        for yy in range(BH):
            for xx in range(BW):
                if glass.opaque(xx, yy):
                    s = (xx + 0.5 - cx) + (yy + 0.5 - cy)
                    goggles.set(xx, yy, 'blue6' if s < -1.5 else 'blue3' if s > 1.0 else 'blue5')
        goggles.set(int(cx - 1.0), int(cy - 1.0), 'white')
    goggles.set(20, 8, 'amber2')
    goggles.set(20, 9, 'amber1')
    gm = {(x, y) for y in range(BH) for x in range(BW) if goggles.opaque(x, y)}
    fill(goggles, outline(gm), 'ink1')
    cv.blit(goggles, 0, 0)
    return cv


# ───────────────────────── голова ─────────────────────────
F = {'o': 'fur_gray0', 'd': 'fur_gray1', 'm': 'fur_gray2', 'f': 'fur_gray3', 'l': 'fur_gray4',
     'U': 'fur_white0', 'u': 'fur_white1', 'v': 'fur_white2', 'w': 'fur_white3', 'W': 'fur_white4',
     'e': 'ink1', 'h': 'cream4', 'n': 'pink2', 'N': 'pink3', 'q': 'ink2', 'r': 'red2', 't': 'pink2'}

# Пышные бакенбарды по бокам морды (левые; правые — зеркально, в тени).
TUFT_L = [
    '..oo.',
    '.olf.',
    'olff.',
    '.oolf',
    '..olf',
    '.olff',
    'olfff',
    '.oomf',
    '...om',
]
TUFT_L_AT = (4, 19)
BROW_L = [
    '...wWWw',
    '.wwwwwv',
    'wvu....',
    'u......',
]
BROW_R = [
    'wWWw...',
    'vwwwww.',
    '....uvw',
    '......u',
]
BROW_L_AT, BROW_R_AT = (11, 14), (25, 14)
EYE_L, EYE_R = (14, 16), (26, 16)
EYES = {
    'open': ['eee', 'hee', 'eee', 'eee'],
    'blink': ['...', '...', '...', 'eee'],
}
NOSE = ['nNn', '.n.']
NOSE_AT = (20, 20)
# Усы: два пышных крыла из-под носа, концы свисают вниз; между крыльями виден рот.
MOUSTACHE = [
    '......wWWwuwWWw......',
    '...wwWWWwwUwwWWWww...',
    '.wwWWwwwwvUvwwwwwWww.',
    'wwWwwwvvuU.Uuvvwwwwww',
    'wvvuuUUUU...UUUUuuvvw',
    'vuU...............Uuv',
    'UU.................UU',
]
MOUSTACHE_AT = (11, 21)
MOUTH_AT = (19, 25)
MOUTHS = {
    'smile': ['q...q', '.qqq.'],
    'talk': ['.qqq.', 'qrrrq', 'qrttq', '.qqq.'],
}
# Усы-вибриссы: тонкие светлые линии от щёк наружу.
WHISKERS = [((9, 23), (2, 22)), ((9, 25), (3, 27)), ((32, 23), (38, 22)), ((32, 25), (37, 27))]


def head() -> Canvas:
    cv = Canvas(BW, BH)
    face = m_ellipse(20.5, 21.5, 13.5, 9.0)
    fill(cv, face, 'fur_gray3')
    for (x, y), d in edges(face).items():
        if 'l' in d or ('u' in d and x < 18):
            cv.set(x, y, 'fur_gray4')
        elif 'r' in d or 'd' in d:
            cv.set(x, y, 'fur_gray2')
    # Тень от поля каски на лбу.
    for x, y in face:
        if y == 14:
            cv.set(x, y, 'fur_gray1')
        elif y == 15:
            cv.set(x, y, 'fur_gray2')
    # Белый подбородок под усами.
    chin = m_ellipse(21.0, 28.0, 3.5, 2.5) & face
    fill(cv, chin, 'fur_white3')
    for (x, y), d in edges(chin).items():
        if 'd' in d or 'r' in d:
            cv.set(x, y, 'fur_white1')
    tl = m_rows(TUFT_L, *TUFT_L_AT)
    rx0 = 40 - TUFT_L_AT[0] - len(TUFT_L[0])
    tr = m_rows([r[::-1] for r in TUFT_L], rx0, TUFT_L_AT[1])
    stamp(cv, TUFT_L, F, *TUFT_L_AT)
    stamp(cv, [r[::-1] for r in TUFT_L], {**F, 'l': 'fur_gray3', 'f': 'fur_gray2', 'm': 'fur_gray1'}, rx0, TUFT_L_AT[1])
    allm = face | tl | tr
    fill(cv, outline(face) - allm, 'fur_gray0')
    # Полоски табби на лбу и румянец.
    for x, y in ((19, 15), (19, 16), (21, 15), (21, 16), (21, 17), (23, 15), (23, 16)):
        cv.set(x, y, 'fur_gray2')
    for x, y in ((11, 21), (12, 21), (30, 21), (31, 21)):
        cv.set(x, y, 'pink3')
    return cv


def details(cv: Canvas, eyes: str, mouth: str) -> None:
    stamp(cv, BROW_L, F, *BROW_L_AT)
    stamp(cv, BROW_R, F, *BROW_R_AT)
    stamp(cv, EYES[eyes], F, *EYE_L)
    stamp(cv, EYES[eyes], F, *EYE_R)
    stamp(cv, MOUSTACHE, F, *MOUSTACHE_AT)
    stamp(cv, MOUTHS[mouth], F, *MOUTH_AT)
    stamp(cv, NOSE, F, *NOSE_AT)
    for (x0, y0), (x1, y1) in WHISKERS:
        cv.line(x0, y0, x1, y1, 'fur_white2')


# ───────────────────────── туловище ─────────────────────────
SCARF = [
    '..xxxxxxxxxxxxxxxxx..',
    '.xHHRRRRRRRRRRRRRRrx.',
    'xHRRRRRRRxxxxRRRRRrrx',
    '.xxRRRRRxHZHRxRRRrxx.',
    '...xxRRRxRHRrxRRrx...',
    '.....xRRRxxxxRRrx....',
    '......xRRRRRRRrx.....',
    '.......xRRRRRrx......',
    '........xRRRrx.......',
    '.........xRrx........',
    '..........xx.........',
]
SCARF_AT = (10, 28)


def body() -> Canvas:
    cv = Canvas(BW, BH)
    sh = m_ellipse(20.0, 41.5, 18.5, 11.0)
    fill(cv, sh, 'cream2')
    for (x, y), d in edges(sh).items():
        cv.set(x, y, 'cream3' if x < 20 else 'cream1')
    # Комбинезон: лямки через плечи и нагрудник.
    bib = {(x, y) for (x, y) in sh if 13 <= x <= 27 and y >= 35}
    fill(cv, bib, 'blue3')
    for k in range(9):
        for w in range(3):
            cv.set(9 + k // 2 + w, 31 + k, 'blue3' if w else 'blue4')
            cv.set(31 - k // 2 - w, 31 + k, 'blue2' if w else 'blue3')
    fill(cv, outline(sh), 'ink1')
    for x, y in ((14, 36), (26, 36)):
        cv.set(x, y, 'gold4')
        cv.set(x, y + 1, 'gold2')
    stamp(cv, SCARF, {'x': 'red1', 'R': 'red3', 'r': 'red2', 'H': 'red4', 'Z': 'red5'}, *SCARF_AT)
    return cv


def portrait(eyes: str, mouth: str) -> Canvas:
    cv = Canvas(BW, BH)
    cv.blit(body(), 0, 0)
    cv.blit(head(), 0, 0)
    details(cv, eyes, mouth)
    cv.blit(helmet(), 0, 0)
    disc = m_disc()
    for y in range(BH):
        for x in range(BW):
            if (x, y) not in disc:
                cv.set(x, y, None)
    return cv


# ───────────────────────── рамка ─────────────────────────
def portrait_frame() -> Canvas:
    """Латунное кольцо-тор (свет сверху-слева), тёмный контур, тёмно-синий диск со ступенчатой подсветкой за головой."""
    cv = Canvas(FW, FH)
    c = FW / 2.0
    for y in range(FH):
        for x in range(FW):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            r = math.hypot(dx, dy)
            if r > 26.0:
                continue
            if r <= DISC_R:
                # Ступенчатая подсветка за головой: светлее к центру и вверх-влево.
                g = math.hypot(dx + 2.0, dy + 3.0)
                cv.set(x, y, 'navy3' if g <= 11.0 else 'navy2' if g <= 17.0 else 'navy1')
            elif r <= DISC_R + 1:
                cv.set(x, y, 'ink1')
            elif r > 25.0:
                cv.set(x, y, 'ink1')
    # Кольцо 3 px: внешний ряд светится сверху-слева, внутренний — снизу-справа (выпуклый тор).
    for y in range(FH):
        for x in range(FW):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            r = math.hypot(dx, dy)
            if not (DISC_R + 1 < r <= 25.0):
                continue
            a = -(dx + dy) / (r * math.sqrt(2))          # 1 — сверху-слева, −1 — снизу-справа
            if r > 24.0:
                col = 'gold5' if a > 0.45 else 'gold4' if a > -0.3 else 'gold2'
            elif r > 23.0:
                col = 'gold4' if a > 0.6 else 'gold3' if a > -0.55 else 'gold2'
            else:
                col = 'gold1' if a > 0.3 else 'gold2' if a > -0.3 else 'gold3'
            cv.set(x, y, col)
    # Тень кольца на диске сверху-слева.
    for y in range(FH):
        for x in range(FW):
            dx, dy = x + 0.5 - c, y + 0.5 - c
            r = math.hypot(dx, dy)
            if DISC_R - 1 < r <= DISC_R and dx + dy < -6:
                cv.set(x, y, 'navy0')
    return cv


def build(reg) -> None:
    reg.add('tutor_cat_0', portrait('open', 'smile'))
    reg.add('tutor_cat_1', portrait('open', 'talk'))
    reg.add('tutor_cat_2', portrait('blink', 'smile'))
    reg.add('ui_portrait_frame', portrait_frame())
