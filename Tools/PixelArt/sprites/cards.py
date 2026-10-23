"""Карты коллекции (группа cards): портреты карт для экрана «Карты» (ТЗ 10.1, 13.1), фоны портретов по редкости,
лента эффекта, мелкие иконки экрана.

Портрет — холст 48×44 пикселя арта, рисуется в окне карты 1:1 (как рамка карты), поверх фона card_bg_* 64×48.
Управляющий шахтой — котик-шахтёр по пояс держит самоцвет: слои card_cat (голова и комбинезон), card_cat_helmet
(Common) или card_cat_hat (Uncommon: шляпа со скрещёнными кирками), card_gem (светлые тона — код красит цветом
самоцвета шахты), card_cat_paws (лапки поверх камня). Остальные карты — предмет card_item_<ключ> по коду эффекта.
Свет сверху-слева, контур — тёмный тон своего материала.
"""
from __future__ import annotations

import math
from typing import Dict, List, Tuple

from pxl import Canvas

import sprites.cats as cats
from sprites.structures import blank, clip, paw, put, rock, thick_line, ROCK

PW, PH = 48, 44           # холст портрета
BW, BH = 64, 48           # фон портрета в окне карты (ячейка от 72 px: узкие экраны 9:21)


# ───────────────────────── фоны портретов ─────────────────────────
# Редкость → (тень, основа, свет, блик): Common — сине-фиолетовый, как на референсе; Uncommon — бирюзовый.
BG_TONES = {
    'common': ('navy1', 'navy2', 'purple2', 'purple3'),
    'uncommon': ('teal0', 'teal1', 'teal2', 'teal3'),
    'rare': ('blue0', 'blue1', 'blue2', 'blue3'),
    'epic': ('purple0', 'purple1', 'purple2', 'purple3'),
    'legendary': ('wood0', 'wood1', 'amber1', 'amber2'),
    'majestic': ('pink0', 'pink1', 'cream1', 'cream3'),
}


def portrait_bg(tone: str) -> Canvas:
    """Фон портрета: скруглённый прямоугольник, свет из центра ступенями (без градиента), тёмная кайма."""
    dark, base, light, hi = BG_TONES[tone]
    cv = Canvas(BW, BH)
    cx, cy = BW / 2.0, BH * 0.46
    for y in range(BH):
        for x in range(BW):
            corner = min(x, BW - 1 - x) + min(y, BH - 1 - y)
            if corner < 2:
                continue
            dx, dy = (x + 0.5 - cx) / (BW * 0.5), (y + 0.5 - cy) / (BH * 0.5)
            r = math.hypot(dx, dy)
            c = hi if r < 0.38 else (light if r < 0.66 else base)
            cv.set(x, y, c)
    # Кайма: верх светлее (свет сверху), низ и бока — тень.
    for x in range(2, BW - 2):
        cv.set(x, 0, dark)
        cv.set(x, BH - 1, dark)
        cv.set(x, 1, light)
    for y in range(2, BH - 2):
        cv.set(0, y, dark)
        cv.set(BW - 1, y, dark)
    for x, y in ((1, 1), (BW - 2, 1), (1, BH - 2), (BW - 2, BH - 2)):
        cv.set(x, y, dark)
    # Редкие звёздочки фона.
    for x, y in ((8, 9), (60, 12), (12, 38), (58, 40), (35, 6)):
        cv.set(x, y, hi)
    return cv


def tag_ribbon() -> Canvas:
    """Лента эффекта под портретом (9-slice 10×9): тёмная основа, светлая верхняя кромка."""
    cv = Canvas(10, 9)
    cv.rect(0, 0, 10, 9, 'ink1')
    cv.rect(1, 1, 8, 7, 'navy1')
    cv.hline(1, 8, 1, 'navy3')
    cv.hline(1, 8, 7, 'ink2')
    return cv


def icon_card_small() -> Canvas:
    """Маленькая карта 7×9 у шкалы копий."""
    cv = Canvas(7, 9)
    cv.rect(0, 0, 7, 9, 'ink1')
    cv.rect(1, 1, 5, 7, 'cream3')
    cv.rect(2, 2, 3, 5, 'blue3')
    cv.set(1, 1, 'white')
    return cv


def icon_sort() -> Canvas:
    """Сортировка 16×16: три ползунка с ручками на разной высоте."""
    cv = Canvas(16, 16)
    lay = blank(cv)
    for i, knob in enumerate((10, 4, 8)):
        y = 3 + i * 5
        lay.rect(2, y, 12, 2, 'cream3')
        lay.hline(2, 13, y + 1, 'cream1')
        lay.rect(knob - 1, y - 1, 4, 4, 'gold4')
        lay.set(knob - 1, y - 1, 'gold5')
        lay.hline(knob - 1, knob + 2, y + 2, 'gold2')
    put(cv, lay, 'ink1')
    return cv


# ───────────────────────── котик-управляющий ─────────────────────────
HEAD_AT = (12, 4)          # начало рамки головы 24×20 (как у котиков на поле)


def _head_master() -> Canvas:
    """Голова с мордочкой (радостные глаза, открытый ротик) — ролями cats, как у котиков на поле."""
    cv = Canvas(PW, PH)
    hx, hy = HEAD_AT
    cats.stamp(cv, cats.HEAD, cats.L_FUR, hx, hy)
    (dl, el), (dr, er) = cats.EYES['happy']
    cats.stamp(cv, el, cats.L_FUR, hx + cats.EYE_L[0] + dl, hy + cats.EYE_L[1])
    cats.stamp(cv, er, cats.L_FUR, hx + cats.EYE_R[0] + dr, hy + cats.EYE_R[1])
    cats.stamp(cv, cats.MOUTHS['open'], cats.L_FUR, hx + cats.MOUTH_AT[0], hy + cats.MOUTH_AT[1])
    return cv


def card_cat() -> Canvas:
    """Котик по пояс: рубашка, комбинезон с лямками и пуговицами, руки от плеч к самоцвету; голова — поверх."""
    cv = Canvas(PW, PH)
    body = blank(cv)
    # Плечи и комбинезон: трапеция от плеч к низу портрета.
    body.polygon([(15, 23), (33, 23), (37, 44), (11, 44)], 'blue3')
    body.polygon([(15, 23), (19, 23), (16, 44), (11, 44)], 'blue4')
    body.polygon([(30, 23), (33, 23), (37, 44), (32, 44)], 'blue2')
    # Воротник рубашки под подбородком.
    body.polygon([(19, 22), (29, 22), (26, 27), (22, 27)], 'cream3')
    body.set(24, 26, 'cream1')
    # Лямки и пуговицы.
    body.line(18, 24, 20, 33, 'blue1')
    body.line(30, 24, 28, 33, 'blue1')
    body.set(20, 30, 'gold4')
    body.set(28, 30, 'gold4')
    put(cv, body, 'blue0')
    # Руки (мех) от плеч вниз к лапкам у камня.
    for side in (-1, 1):
        arm = blank(cv)
        x_out = 24 + side * 12
        arm.polygon([(x_out - side * 1, 23), (x_out + side * 1.5, 25), (24 + side * 9, 34), (24 + side * 6, 35), (24 + side * 7, 27)], 'fur_ginger2')
        arm.shade('fur_ginger3', 'fur_ginger1', 'fur_ginger2')
        put(cv, arm, 'fur_ginger0')
    cv.blit(cats.paint(_head_master(), cats.fur_map('ginger')), 0, 0)
    return cv


def card_cat_helmet() -> Canvas:
    """Жёлтая каска с фонариком (как на поле); уши проходят сквозь неё."""
    cv = Canvas(PW, PH)
    hx, hy = HEAD_AT
    cats.stamp(cv, cats.HELMET, cats.L_HELM, hx, hy)
    head = Canvas(PW, PH)
    cats.stamp(head, cats.HEAD, cats.L_FUR, hx, hy)
    for x0, y0, x1, y1 in cats.EAR_BOXES:
        for y in range(hy + y0, hy + y1):
            for x in range(hx + x0, hx + x1):
                if head.opaque(x, y):
                    cv.set(x, y, None)
    return cats.paint(cv, cats.helmet_map('yellow'))


def card_cat_hat() -> Canvas:
    """Шляпа старшего управляющего: широкие поля, лента, скрещённые кирки на тулье; уши — в прорезях полей."""
    cv = Canvas(PW, PH)
    hx, hy = HEAD_AT
    lay = blank(cv)
    # Тулья с вмятиной сверху.
    lay.polygon([(hx + 6, hy + 8), (hx + 7, hy - 1), (hx + 11, hy + 1), (hx + 13, hy - 1), (hx + 17, hy + 1), (hx + 18, hy + 8)], 'wood4')
    lay.hline(hx + 7, hx + 17, hy + 6, 'red3')
    lay.hline(hx + 7, hx + 17, hy + 7, 'red2')
    # Поля: широкий эллипс, загнутые края.
    lay.ellipse(hx + 12, hy + 9.5, 17, 3.2, 'wood4')
    lay.ellipse(hx + 12, hy + 8.8, 13, 1.6, 'wood5')
    lay.shade('wood6', 'wood2', 'wood4')
    put(cv, lay, 'wood0')
    # Скрещённые кирки (золото) на тулье.
    for flip in (False, True):
        x0, x1 = (hx + 9, hx + 15) if not flip else (hx + 15, hx + 9)
        cv.line(x0, hy + 5, x1, hy + 1, 'gold4')
    cv.hline(hx + 8, hx + 10, hy + 1, 'gold3')
    cv.hline(hx + 14, hx + 16, hy + 1, 'gold3')
    # Уши торчат над полями: вырезаем шляпу по форме ушей выше строки полей.
    head = Canvas(PW, PH)
    cats.stamp(head, cats.HEAD, cats.L_FUR, hx, hy)
    for x0, y0, x1, y1 in cats.EAR_BOXES:
        for y in range(hy + y0, min(hy + y1, hy + 7)):
            for x in range(hx + x0, hx + x1):
                if head.opaque(x, y):
                    cv.set(x, y, None)
    return cv


GEM_C = (24.0, 35.0)


def card_gem() -> Canvas:
    """Огранённый самоцвет в лапках: светлые тона (cream/white/navy8) — Image.color красит его цветом шахты."""
    cv = Canvas(PW, PH)
    cx, cy = GEM_C
    lay = blank(cv)
    top = [(cx - 9, cy - 3), (cx - 5, cy - 8), (cx + 5, cy - 8), (cx + 9, cy - 3)]
    body = [(cx - 9, cy - 3), (cx + 9, cy - 3), (cx, cy + 8)]
    lay.polygon(top + [(cx + 9, cy - 3)], 'cream3')
    lay.polygon(body, 'cream1')
    # Грани: свет слева-сверху, тень справа.
    lay.polygon([(cx - 5, cy - 8), (cx, cy - 8), (cx - 2, cy - 3), (cx - 9, cy - 3)], 'cream4')
    lay.polygon([(cx - 9, cy - 3), (cx - 2, cy - 3), (cx, cy + 8)], 'cream2')
    lay.polygon([(cx + 2, cy - 3), (cx + 9, cy - 3), (cx, cy + 8)], 'navy8')
    lay.hline(int(cx - 8), int(cx + 8), int(cy - 3), 'white')
    lay.set(int(cx - 4), int(cy - 7), 'white')
    lay.set(int(cx - 3), int(cy - 7), 'white')
    put(cv, lay, 'navy5')
    return cv


def card_cat_paws() -> Canvas:
    """Лапки держат камень с боков (поверх него)."""
    master = Canvas(PW, PH)
    for x, y in ((12, 31), (30, 31)):
        cats.stamp(master, ['.oooo.', 'owwwwo', 'owwwvo', 'owwvvo', '.oooo.'], cats.L_FUR, x, y)
    return cats.paint(master, cats.fur_map('ginger'))


# ───────────────────────── кузница ─────────────────────────
def card_forge() -> Canvas:
    """Маленькая «кошачья печь»: голова рыжего кота из кирпича, бирюзовые глаза, огонь во рту, синий ошейник."""
    cv = Canvas(PW, PH)
    cx, cy = 24.0, 23.0
    # Уши.
    for side in (-1, 1):
        ear = blank(cv)
        ear.polygon([(cx + side * 13, cy - 4), (cx + side * 11, cy - 19), (cx + side * 3, cy - 11)], 'fur_ginger2')
        ear.polygon([(cx + side * 11, cy - 7), (cx + side * 10, cy - 15), (cx + side * 6, cy - 11)], 'pink2')
        put(cv, ear, 'fur_ginger0')
    head = blank(cv)
    head.ellipse(cx, cy, 16, 15, 'fur_ginger2')
    for y in range(int(cy - 15), int(cy + 15), 4):        # кладка
        for x in range(int(cx - 16), int(cx + 16)):
            if head.opaque(x, y):
                head.set(x, y, 'fur_ginger1')
        off = 2 if (y // 4) % 2 else 6
        for x in range(int(cx - 16) + off, int(cx + 16), 8):
            for yy in range(y + 1, y + 4):
                if head.opaque(x, yy):
                    head.set(x, yy, 'fur_ginger1')
    head.shade('fur_ginger3', 'fur_ginger1', 'fur_ginger2', highlight_edge=True)
    for x0, x1 in ((22, 23), (24, 25), (26, 27)):         # полоски «М»
        head.line(x0, int(cy - 13), x1, int(cy - 9), 'fur_ginger0')
    put(cv, head, 'fur_ginger0')
    for ex in (cx - 6, cx + 6):                             # глаза
        eye = blank(cv)
        eye.ellipse(ex, cy - 4, 3.2, 3.2, 'teal3')
        eye.ellipse(ex - 0.5, cy - 4.5, 1.8, 1.8, 'teal5')
        eye.vline(int(ex), int(cy - 6), int(cy - 2), 'ink1')
        put(cv, eye, 'stone0')
    cv.set(int(cx) - 1, int(cy), 'pink2')
    cv.set(int(cx), int(cy), 'pink2')
    mouth = blank(cv)                                      # топка-рот с огнём
    mouth.ellipse(cx, cy + 7, 7, 6, 'stone5')
    mouth.ellipse(cx, cy + 7.5, 5, 4.5, 'ink2')
    for x, h in ((cx - 3, 4), (cx, 6), (cx + 3, 3)):
        mouth.vline(int(x), int(cy + 11 - h), int(cy + 11), 'amber3')
        mouth.set(int(x), int(cy + 11 - h), 'amber5')
    mouth.hline(int(cx - 4), int(cx + 4), int(cy + 11), 'amber4')
    put(cv, mouth, 'stone0')
    collar = blank(cv)                                     # ошейник и колокольчик
    collar.rect(int(cx - 11), int(cy + 13), 23, 3, 'blue3')
    collar.hline(int(cx - 11), int(cx + 11), int(cy + 13), 'blue4')
    collar.ellipse(cx, cy + 18, 2.6, 2.6, 'gold3')
    collar.set(int(cx) - 1, int(cy + 17), 'gold5')
    put(cv, collar, 'blue0')
    return cv


# ───────────────────────── предметы (по коду эффекта) ─────────────────────────
def _ball(lay: Canvas, cx: float, cy: float, rx: float, ry: float, ramp: Tuple[str, str, str, str]) -> None:
    """Объёмный эллипс: тень, основа, свет, блик (свет сверху-слева)."""
    dark, base, light, hi = ramp
    lay.ellipse(cx, cy, rx, ry, base)
    for y in range(lay.h):
        for x in range(lay.w):
            if lay.get(x, y)[:3] != _rgb(base):
                continue
            dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            if dx * dx + dy * dy > 1.0:
                continue
            lum = -0.6 * dx - 0.8 * dy
            if lum > 0.75:
                lay.set(x, y, hi)
            elif lum > 0.3:
                lay.set(x, y, light)
            elif lum < -0.45:
                lay.set(x, y, dark)


def _rgb(name: str):
    from pxl import PALETTE
    return PALETTE[name]


GOLD = ('gold2', 'gold3', 'gold4', 'gold5')


def item_golden_cart() -> Canvas:
    cv = Canvas(PW, PH)
    nug = blank(cv)
    for x, y, r in ((17, 15, 4), (24, 13, 5), (31, 15, 4), (21, 18, 4), (28, 18, 4)):
        _ball(nug, x, y, r, r - 0.6, GOLD)
    put(cv, nug, 'gold0')
    cart = blank(cv)
    cart.polygon([(8, 19), (40, 19), (36, 33), (12, 33)], 'gold3')
    cart.rect(7, 18, 34, 3, 'gold4')
    cart.hline(7, 40, 18, 'gold5')
    for x in (16, 24, 32):
        cart.vline(x, 22, 31, 'gold2')
    cart.hline(11, 37, 30, 'gold2')
    for x in (10, 38):
        cart.set(x, 23, 'gold5')
    put(cv, cart, 'gold0')
    for wx in (15, 33):
        wheel = blank(cv)
        _ball(wheel, wx, 35, 4, 4, ('stone1', 'stone3', 'stone5', 'stone6'))
        wheel.set(wx, 35, 'stone0')
        put(cv, wheel, 'stone0')
    return cv


def item_pocket_watch() -> Canvas:
    cv = Canvas(PW, PH)
    ring = blank(cv)
    ring.ellipse(24, 8, 3, 3, 'gold4')
    ring.ellipse(24, 8, 1.5, 1.5, None)
    ring.rect(22, 10, 5, 3, 'gold3')
    put(cv, ring, 'gold0')
    case = blank(cv)
    _ball(case, 24, 26, 14, 14, GOLD)
    case.ellipse(24, 26, 11, 11, 'cream4')
    case.ellipse(24, 26, 10, 10, 'cream3')
    for a in range(0, 360, 30):
        r = 8.5
        x = 24 + r * math.cos(math.radians(a))
        y = 26 + r * math.sin(math.radians(a))
        case.set(int(round(x - 0.5)), int(round(y - 0.5)), 'ink2' if a % 90 else 'ink1')
    case.line(24, 26, 24, 19, 'ink1')     # минутная
    case.line(24, 26, 29, 23, 'ink1')     # часовая
    case.set(24, 26, 'red3')
    put(cv, case, 'gold0')
    return cv


def item_golden_helmet() -> Canvas:
    cv = Canvas(PW, PH)
    lay = blank(cv)
    _ball(lay, 24, 28, 16, 14, GOLD)
    clip(lay, lambda x, y: y <= 28)
    lay.rect(6, 28, 37, 4, 'gold3')
    lay.hline(6, 42, 28, 'gold5')
    lay.hline(6, 42, 31, 'gold1')
    lay.hline(10, 38, 22, 'gold2')
    put(cv, lay, 'gold0')
    lamp = blank(cv)
    lamp.ellipse(24, 19, 5, 5, 'stone3')
    lamp.ellipse(24, 19, 3.5, 3.5, 'amber4')
    lamp.ellipse(23.5, 18.5, 1.8, 1.8, 'amber5')
    lamp.set(22, 17, 'white')
    put(cv, lamp, 'stone0')
    for x0, y0, x1, y1 in ((24, 12, 24, 8), (18, 13, 15, 10), (30, 13, 33, 10)):
        cv.line(x0, y0, x1, y1, 'gold5')
    return cv


def item_recruit_poster() -> Canvas:
    """Код 4 (уровень найма): плакат «Ищем шахтёров» — мордочка котика и зелёная стрелка вверх."""
    cv = Canvas(PW, PH)
    paper = blank(cv)
    paper.rect(10, 6, 28, 33, 'cream3')
    paper.vline(10, 6, 38, 'cream4')
    paper.hline(10, 37, 38, 'cream1')
    paper.set(37, 6, 'cream2')
    put(cv, paper, 'wood0')
    for x in (12, 35):
        cv.rect(x, 5, 2, 2, 'red3')
    face = blank(cv)
    face.polygon([(16, 15), (17, 9), (21, 13)], 'fur_ginger2')
    face.polygon([(32, 15), (31, 9), (27, 13)], 'fur_ginger2')
    face.ellipse(24, 17, 8, 6, 'fur_ginger2')
    face.set(21, 16, 'ink1')
    face.set(27, 16, 'ink1')
    face.set(24, 18, 'pink2')
    put(cv, face, 'fur_ginger0')
    arrow = blank(cv)
    arrow.polygon([(24, 24), (30, 30), (27, 30), (27, 35), (21, 35), (21, 30), (18, 30)], 'green4')
    arrow.hline(20, 28, 30, 'green5')
    put(cv, arrow, 'green1')
    return cv


def item_lucky_paw() -> Canvas:
    """Код 5 (шанс крита): счастливая лапка — подушечки и искры."""
    cv = Canvas(PW, PH)
    lay = blank(cv)
    _ball(lay, 24, 27, 11, 10, ('cream1', 'cream3', 'cream4', 'white'))
    for x, y in ((14, 14), (20, 10), (28, 10), (34, 14)):
        _ball(lay, x, y, 3.6, 4, ('cream1', 'cream3', 'cream4', 'white'))
    put(cv, lay, 'fur_brown1')
    pads = blank(cv)
    pads.ellipse(24, 28, 6, 5, 'pink2')
    for x, y in ((14, 15), (20, 11), (28, 11), (34, 15)):
        pads.ellipse(x, y, 1.8, 2, 'pink2')
    pads.set(22, 26, 'pink3')
    cv.blit(pads, 0, 0)
    for x, y in ((40, 22), (8, 26), (38, 34)):
        cv.set(x, y, 'gold5')
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            cv.set(x + dx, y + dy, 'gold4')
    return cv


def item_sledgehammer() -> Canvas:
    """Код 6 (сила крита): кувалда — тяжёлая железная головка на рукояти."""
    cv = Canvas(PW, PH)
    handle = blank(cv)
    thick_line(handle, 13, 38, 28, 17, 'wood4', 3)
    put(cv, handle, 'wood0')
    head = blank(cv)
    head.polygon([(22, 6), (38, 20), (32, 27), (16, 13)], 'stone4')
    head.polygon([(22, 6), (38, 20), (36, 22), (20, 8)], 'stone6')
    head.polygon([(32, 27), (16, 13), (18, 11), (34, 25)], 'stone2')
    put(cv, head, 'stone0')
    return cv


def item_gate_bell() -> Canvas:
    """Код 8 (таймер бесплатного котика за ворота): золотой колокольчик с красным бантом."""
    cv = Canvas(PW, PH)
    bell = blank(cv)
    bell.polygon([(16, 32), (18, 18), (22, 12), (26, 12), (30, 18), (32, 32), (36, 35), (12, 35)], 'gold3')
    bell.shade('gold5', 'gold2', 'gold3')
    bell.hline(12, 36, 33, 'gold4')
    put(cv, bell, 'gold0')
    cv.ellipse(24, 37, 2.6, 2.4, 'gold2')
    bow = blank(cv)
    bow.polygon([(24, 10), (17, 6), (17, 13)], 'red4')
    bow.polygon([(24, 10), (31, 6), (31, 13)], 'red3')
    bow.ellipse(24, 10, 2, 2, 'red4')
    put(cv, bow, 'red1')
    return cv


def item_gold_vein() -> Canvas:
    """Код 11 (золото с камней): валун пещеры с золотой жилой и самородками."""
    cv = Canvas(PW, PH)
    rock(cv, 24, 25, 17, 14, ROCK, seed=7, moss=False, n=8)
    for x0, y0, x1, y1 in ((12, 22, 20, 28), (20, 28, 30, 24), (30, 24, 36, 30)):
        cv.line(x0, y0, x1, y1, 'gold4')
    for x, y in ((14, 18), (28, 18), (33, 27), (19, 30)):
        cv.rect(x, y, 3, 2, 'gold4')
        cv.set(x, y, 'gold5')
        cv.set(x + 2, y + 1, 'gold2')
    return cv


def item_fish_parcel() -> Canvas:
    """Код 13 (доставки): посылка — ящик с рыбкой, торчащей из-под крышки."""
    cv = Canvas(PW, PH)
    fish = blank(cv)
    fish.ellipse(30, 12, 8, 4, 'blue4')
    fish.polygon([(36, 12), (42, 7), (42, 17)], 'blue3')
    fish.hline(24, 35, 13, 'blue5')
    fish.set(25, 11, 'ink1')
    put(cv, fish, 'blue1')
    box = blank(cv)
    box.rect(8, 16, 32, 22, 'wood4')
    box.rect(6, 14, 36, 4, 'wood5')
    box.hline(6, 41, 14, 'wood6')
    for x in (16, 24, 32):
        box.vline(x, 18, 37, 'wood3')
    box.rect(19, 24, 10, 7, 'cream3')
    box.hline(20, 27, 26, 'ink2')
    box.hline(20, 25, 28, 'ink2')
    put(cv, box, 'wood0')
    return cv


def item_big_dynamite() -> Canvas:
    """Код 22 (урон динамита): связка из трёх шашек с горящим фитилём."""
    cv = Canvas(PW, PH)
    sticks = blank(cv)
    for x in (14, 21, 28):
        sticks.rect(x, 14, 7, 24, 'red3')
        sticks.vline(x, 14, 37, 'red4')
        sticks.vline(x + 6, 14, 37, 'red2')
        sticks.hline(x, x + 6, 14, 'red5')
    for y in (20, 31):
        sticks.rect(13, y, 23, 3, 'wood5')
        sticks.hline(13, 35, y + 2, 'wood3')
    put(cv, sticks, 'red1')
    cv.line(25, 13, 29, 7, 'ink2')
    for x, y, c in ((30, 5, 'gold5'), (31, 6, 'amber4'), (29, 4, 'amber4'), (32, 4, 'gold4'), (28, 6, 'gold4')):
        cv.set(x, y, c)
    return cv


def item_clover() -> Canvas:
    """Код 23 (шанс легендарной карты): четырёхлистный клевер."""
    cv = Canvas(PW, PH)
    stem = blank(cv)
    thick_line(stem, 24, 24, 29, 39, 'green3', 2)
    put(cv, stem, 'green1')
    leaves = blank(cv)
    for dx, dy in ((0, -9), (-9, 0), (9, 0), (0, 9)):
        cx, cy = 24 + dx, 22 + dy
        leaves.ellipse(cx - (2.6 if dx == 0 else 0), cy - (2.6 if dy == 0 else 0), 4.6, 4.6, 'green4')
        leaves.ellipse(cx + (2.6 if dx == 0 else 0), cy + (2.6 if dy == 0 else 0), 4.6, 4.6, 'green4')
    leaves.shade('green5', 'green3', 'green4')
    leaves.ellipse(24, 22, 3, 3, 'green4')
    put(cv, leaves, 'green1')
    cv.ellipse(24, 22, 1.4, 1.4, 'green6')
    return cv


def item_fish() -> Canvas:
    cv = Canvas(PW, PH)
    lay = blank(cv)
    _ball(lay, 21, 23, 13, 8, ('blue2', 'blue3', 'blue4', 'blue6'))
    lay.polygon([(32, 23), (42, 14), (42, 32)], 'blue3')
    lay.polygon([(18, 15), (24, 10), (27, 16)], 'blue4')
    put(cv, lay, 'blue0')
    cv.ellipse(13, 21, 2.2, 2.2, 'cream4')
    cv.set(13, 21, 'ink1')
    cv.line(17, 18, 17, 28, 'blue2')
    return cv


def item_yarn() -> Canvas:
    cv = Canvas(PW, PH)
    lay = blank(cv)
    _ball(lay, 24, 24, 13, 13, ('pink1', 'pink2', 'pink3', 'pink4'))
    for a0 in (200, 235, 270):
        for t in range(0, 140, 4):
            a = math.radians(a0 + t)
            r = 13 - abs(t - 70) / 12.0
            x = 24 + r * math.cos(a) * 0.9
            y = 24 + r * math.sin(a) * 0.55 + (a0 - 235) / 10.0
            lay.set(int(x), int(y), 'pink1')
    put(cv, lay, 'pink0')
    cv.line(34, 34, 40, 38, 'pink2')
    cv.line(40, 38, 43, 36, 'pink2')
    return cv


def item_milk_bowl() -> Canvas:
    cv = Canvas(PW, PH)
    bowl = blank(cv)
    bowl.ellipse(24, 26, 17, 5, 'blue4')
    bowl.polygon([(7, 26), (41, 26), (36, 37), (12, 37)], 'blue3')
    bowl.ellipse(24, 26, 15, 3.6, 'cream4')
    bowl.ellipse(22, 25.5, 9, 1.8, 'white')
    bowl.hline(12, 36, 33, 'blue5')
    put(cv, bowl, 'blue1')
    cv.ellipse(24, 31, 3, 1.6, 'cream3')
    return cv


def item_catnip() -> Canvas:
    cv = Canvas(PW, PH)
    pot = blank(cv)
    pot.polygon([(14, 26), (34, 26), (31, 39), (17, 39)], 'amber1')
    pot.rect(13, 24, 22, 4, 'amber2')
    pot.hline(13, 34, 24, 'amber3')
    put(cv, pot, 'red1')
    leaves = blank(cv)
    for x, y, rx, ry in ((18, 18, 4, 6), (24, 13, 4, 7), (30, 18, 4, 6), (21, 21, 3, 3), (27, 21, 3, 3)):
        leaves.ellipse(x, y, rx, ry, 'green4')
    leaves.shade('green5', 'green3', 'green4')
    for x, y in ((24, 8), (18, 13), (30, 13)):
        leaves.set(x, y, 'purple4')
        leaves.set(x, y - 1, 'purple5')
    put(cv, leaves, 'green1')
    return cv


def item_toy_mouse() -> Canvas:
    cv = Canvas(PW, PH)
    tail = blank(cv)
    for t in range(0, 60):
        a = t / 60.0
        x = 34 + 8 * a
        y = 30 - 10 * math.sin(a * math.pi)
        tail.set(int(x), int(y), 'pink2')
    cv.blit(tail, 0, 0)
    body = blank(cv)
    _ball(body, 22, 28, 13, 8, ('stone3', 'stone5', 'stone6', 'stone7'))
    body.ellipse(18, 18, 4, 4, 'stone5')
    body.ellipse(18, 18, 2.2, 2.2, 'pink2')
    put(cv, body, 'stone1')
    cv.set(12, 26, 'ink1')
    cv.set(9, 29, 'pink2')
    cv.set(10, 29, 'pink2')
    return cv


def item_cat_bed() -> Canvas:
    cv = Canvas(PW, PH)
    bed = blank(cv)
    bed.ellipse(24, 27, 19, 10, 'red3')
    bed.ellipse(24, 25, 15, 6, 'red4')
    bed.ellipse(24, 27, 13, 5.5, 'cream3')
    bed.ellipse(22, 26, 8, 2.6, 'cream4')
    bed.shade('red5', 'red2', 'red3')
    put(cv, bed, 'red1')
    paw(cv, 20, 23, 'cream1')
    return cv


def item_scratcher() -> Canvas:
    cv = Canvas(PW, PH)
    base = blank(cv)
    base.rect(10, 34, 28, 6, 'purple3')
    base.hline(10, 37, 34, 'purple4')
    base.hline(10, 37, 39, 'purple1')
    put(cv, base, 'purple0')
    post = blank(cv)
    post.rect(19, 8, 10, 27, 'wood5')
    for y in range(9, 34, 2):
        post.hline(19, 28, y, 'wood4')
    post.vline(19, 8, 34, 'wood6')
    post.vline(28, 8, 34, 'wood3')
    post.rect(16, 5, 16, 4, 'purple3')
    post.hline(16, 31, 5, 'purple4')
    put(cv, post, 'wood0')
    cv.line(31, 9, 34, 18, 'cream2')
    ball = blank(cv)
    _ball(ball, 34, 21, 3, 3, ('red2', 'red3', 'red4', 'red5'))
    put(cv, ball, 'red1')
    return cv


def item_collar() -> Canvas:
    cv = Canvas(PW, PH)
    ring = blank(cv)
    ring.ellipse(24, 22, 17, 12, 'red3')
    ring.ellipse(24, 21, 12, 7, None)
    ring.shade('red4', 'red2', 'red3')
    for x in (12, 18, 30, 36):
        ring.set(x, 28 if x in (18, 30) else 25, 'gold4')
    put(cv, ring, 'red1')
    bell = blank(cv)
    _ball(bell, 24, 36, 5, 5, GOLD)
    bell.hline(20, 28, 37, 'gold1')
    bell.set(24, 39, 'ink1')
    put(cv, bell, 'gold0')
    return cv


def item_lantern() -> Canvas:
    cv = Canvas(PW, PH)
    lay = blank(cv)
    lay.ellipse(24, 6, 3, 3, 'stone4')
    lay.ellipse(24, 6, 1.6, 1.6, None)
    lay.rect(16, 9, 16, 4, 'stone4')
    lay.rect(17, 13, 14, 20, 'amber4')
    lay.rect(19, 15, 10, 16, 'amber5')
    lay.rect(22, 17, 4, 10, 'cream4')
    for x in (17, 30):
        lay.vline(x, 13, 32, 'stone3')
    lay.vline(24, 13, 32, 'stone3')
    lay.rect(15, 33, 18, 4, 'stone4')
    lay.hline(15, 32, 33, 'stone6')
    lay.hline(16, 31, 9, 'stone6')
    put(cv, lay, 'stone0')
    return cv


def item_treasure_map() -> Canvas:
    """Код 26 (смысл в данных неизвестен): карта сокровищ — свиток пергамента с пунктиром от лапки к красному кресту."""
    cv = Canvas(PW, PH)
    lay = blank(cv)
    lay.polygon([(10, 9), (38, 8), (39, 35), (11, 36)], 'cream2')
    for y in range(lay.h):
        for x in range(lay.w):
            if lay.get(x, y)[:3] != _rgb('cream2'):
                continue
            if x + y < 24:
                lay.set(x, y, 'cream3')
            elif x + y > 64:
                lay.set(x, y, 'cream1')
    # Рулоны свитка слева и справа.
    for x0 in (7, 37):
        lay.rect(x0, 7, 4, 30, 'cream1')
        lay.vline(x0, 7, 36, 'cream3')
        lay.vline(x0 + 3, 7, 36, 'cream0')
        for y0 in (5, 37):
            lay.rect(x0, y0, 4, 2, 'wood3')
    put(cv, lay, 'wood1')
    # Пунктир пути от лапки к кресту.
    pts = [(15, 31), (18, 26), (23, 27), (27, 21), (31, 17)]
    k = 0
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(abs(x1 - x0), abs(y1 - y0))
        for i in range(n):
            if k % 3 != 2:
                cv.set(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n), 'red3')
            k += 1
    for d in range(5):
        cv.set(31 + d, 11 + d, 'red4')
        cv.set(35 - d, 11 + d, 'red4')
    cv.set(33, 13, 'red5')
    paw = Canvas.from_ascii(['.o.o.', 'o...o', '.ooo.', '.ooo.'], {'o': 'wood2'})
    cv.blit(paw, 12, 29)
    # Горы и пещера на карте.
    cv.line(17, 17, 20, 13, 'stone3')
    cv.line(20, 13, 23, 17, 'stone3')
    cv.line(21, 17, 24, 14, 'stone2')
    cv.line(24, 14, 26, 17, 'stone2')
    return cv


# Ключ предмета → функция. Ключи — контракт с PresentationCatalog.CardItem (Unity).
ITEMS = {
    'golden_cart': item_golden_cart,       # 10 — доход всех построек
    'pocket_watch': item_pocket_watch,     # 7 — бесплатный котик раньше
    'golden_helmet': item_golden_helmet,   # 1 — лимит котиков
    'recruit_poster': item_recruit_poster, # 4 — уровень найма
    'lucky_paw': item_lucky_paw,           # 5 — шанс крита
    'sledgehammer': item_sledgehammer,     # 6 — сила крита
    'gate_bell': item_gate_bell,           # 8 — таймер за ворота
    'gold_vein': item_gold_vein,           # 11 — золото с камней
    'fish_parcel': item_fish_parcel,       # 13 — доставки
    'big_dynamite': item_big_dynamite,     # 22 — урон динамита
    'clover': item_clover,                 # 23 — шанс легендарной
    'fish': item_fish,                     # 3
    'yarn': item_yarn,                     # 12
    'milk_bowl': item_milk_bowl,           # 15
    'catnip': item_catnip,                 # 16
    'toy_mouse': item_toy_mouse,           # 19
    'cat_bed': item_cat_bed,               # 20
    'scratcher': item_scratcher,           # 21
    'collar': item_collar,                 # 24
    'lantern': item_lantern,               # 25
    'treasure_map': item_treasure_map,     # 26
}


def build(reg) -> None:
    for tone in BG_TONES:
        reg.add(f'card_bg_{tone}', portrait_bg(tone), note='фон портрета карты 64×48 по редкости')
    reg.add('ui_card_tag', tag_ribbon(), border=(3, 3, 3, 3), note='лента эффекта под портретом, 9-slice')
    reg.add('icon_card_small', icon_card_small(), note='маленькая карта 7×9 у шкалы копий')
    reg.add('icon_sort', icon_sort(), note='сортировка карт 16×16')
    reg.add('card_cat', card_cat(), note='портрет управляющего: котик по пояс (голова, комбинезон)')
    reg.add('card_cat_helmet', card_cat_helmet(), note='каска управляющего (Common)')
    reg.add('card_cat_hat', card_cat_hat(), note='шляпа старшего управляющего (Uncommon) со скрещёнными кирками')
    reg.add('card_gem', card_gem(), note='самоцвет в лапках, светлые тона: Image.color = цвет самоцвета шахты')
    reg.add('card_cat_paws', card_cat_paws(), note='лапки поверх самоцвета')
    reg.add('card_forge', card_forge(), note='портрет карты кузницы: маленькая «кошачья печь»')
    for key, fn in ITEMS.items():
        reg.add(f'card_item_{key}', fn(), note='портрет карты-предмета 48×44')
