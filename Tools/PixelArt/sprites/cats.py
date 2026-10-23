"""Котики-шахтёры (группа cats): тело по окрасам с отметинами, каски восьми цветов, оружие восьми видов,
кольцо выделения, бейджи уровня, цифры.

Все слои котика рисуются на холсте 40×40 с pivot (20, 4) (ноги на 4 px выше низа холста) и смотрят вправо.
Мастер-рисунок делается «ролями» (ключевые цвета вне палитры), потом перекрашивается в окрас и цвет каски — у всех
вариантов одни и те же формы, поэтому слои совпадают в каждом кадре. Оружие (sprites/weapons.py) рисуется по оси кадра
(Pose.pick) сразу цветами палитры; лапки вырезаются из него — они держат рукоять поверх.

Прокачка (решение владельца от 8 октября 2026): оружие — по уровню внутри круга из 8 уровней (кирка, молот, дробилка,
циркулярная пила, бур-машина, две кирки, бензопила, лазерган), цвет каски — новый на каждом круге.

Кадры (manifest.CAT_FRAMES, ART_DIRECTION.md «Анимация котиков»): дыхание idle0–5, моргание blink/blink_d, редкое
потягивание stretch0–9, ходьба walk0–7 (смена опорных лап, покачивание корпуса и хвоста), добыча замахом mine_* (стойка,
вдох, подготовка, замах, верхняя точка, удар, отдача, возврат), добыча с упора tool_* (пила, бур, бензопила, лазер:
держит инструмент перед собой, подаёт вперёд, отдача), перенос drag0–3 (болтает лапками), появление spawn0–7 (присел,
прыжок, в воздухе, падение, приземление, оглядывается, радуется), радость joy0–5. Высоту прыжков задаёт код (CatView),
кадры дают только форму тела.

Новый режим (Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md §7, §10, §16): кадры с грузом в конце CAT_FRAMES — ходьба
с полной сумкой carry0–7 (тяжелее покачивание, лапки на лямках), подбор порции pickup0–3, разгрузка unload0–3; и слои
экипировки команды по редкостям base/common/rare/epic/legendary для кадров manifest.EQUIP_FRAMES: обувь
cat_boots_*, рюкзак cat_pack_*, груз в рюкзаке cat_load_<1..3>_*, головной убор cat_hat_*, кирка cat_pick_*
(PICK_FRAMES: в carry/pickup/unload кирки нет — убрана в рюкзак) и мешочек в лапках cat_sack_* (SACK_FRAMES).
Старые слои (каска цвета круга, оружие круга) остаются только для прежних 62 кадров.
"""
from __future__ import annotations

import math
from typing import Dict, List, Optional, Sequence, Set, Tuple

from pxl import PALETTE_SET, C, Canvas

from manifest import (CAT_FRAMES, EQUIP_FRAMES, EQUIP_RARITIES, FURS, HELMET_COLORS, LEGACY_FRAMES, LOAD_LEVELS,
                      PICK_FRAMES, SACK_FRAMES, WEAPONS, weapon_frames)
from sprites import weapons

CW, CH = 40, 40
PIVOT = (20.0, 4.0)

ROLES = [
    # шерсть и мордочка
    'fo', 'f1', 'f2', 'f3', 'f4', 'fs', 'mz', 'mzs', 'ear', 'chk', 'eye', 'eyeh', 'nose', 'mouth', 'tongue',
    # комбинезон и рубашка
    'bo', 'b1', 'b2', 'b3', 'b4', 'sh', 'shs', 'btn',
    # каска
    'ho', 'h1', 'h2', 'h3', 'h4', 'h5', 'band', 'lampo', 'glass', 'glassl', 'glassw', 'glassd',
    # кирка: рукоять и головка
    'wo', 'w2', 'po', 'p1', 'p2', 'p3', 'p4',
    # отметины окраса: пятна A и B (калико, «пойнты», проточина)
    'pa1', 'pa2', 'pa3', 'pa4', 'pb1', 'pb2', 'pb3', 'pb4',
    # детали каски по ступени: полоса, заклёпки, самоцвет и его блик, звезда
    'hs', 'hr', 'hg', 'hgl', 'hst',
]
KEY: Dict[str, Tuple[int, int, int]] = {r: (3 + 4 * i, 251, 7) for i, r in enumerate(ROLES)}
assert not (set(KEY.values()) & PALETTE_SET)

Mask = Set[Tuple[int, int]]


def stamp(cv: Canvas, rows: Sequence[str], legend: Dict[str, str], x: int, y: int) -> None:
    """Нарисовать ASCII-часть ролями: символ → роль; '.' — пропуск (не стирает)."""
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch in '. ':
                continue
            cv.set(x + i, y + j, KEY[legend[ch]])


def mask_of(cv: Canvas) -> Mask:
    return {(x, y) for y in range(cv.h) for x in range(cv.w) if cv.opaque(x, y)}


def stamp_mask(rows: Sequence[str], x: int, y: int) -> Mask:
    return {(x + i, y + j) for j, row in enumerate(rows) for i, ch in enumerate(row) if ch not in '. '}


# ───────────────────────── части тела ─────────────────────────
L_FUR = {'o': 'fo', 'd': 'f1', 'm': 'f2', 'f': 'f3', 'l': 'f4', 's': 'fs', 'w': 'mz', 'v': 'mzs', 'p': 'ear',
         'k': 'chk', 'e': 'eye', 'h': 'eyeh', 'n': 'nose', 'q': 'mouth', 't': 'tongue',
         'O': 'bo', 'D': 'b1', 'S': 'b2', 'B': 'b3', 'L': 'b4', 'c': 'sh', 'C': 'shs', 'y': 'btn'}

# Голова и каска рисуются в общей рамке 24×20 (её начало задаёт кадр). Каска — строки 0..10,
# мордочка видна под полем каски (строки 11..18: тень от поля, глаза, нос, рот), строка 19 — контур подбородка.
# Пропорции как на концепте: голова с каской ≈ 2/3 роста, туловище короткое.
HEAD = [
    '........................',
    '..oo................oo..',
    '..olo..............omo..',
    '..olpo............opmo..',
    '..olppo..........oppmo..',
    '.oflppo..........oppfmo.',
    '.offffo..........offfmo.',
    '..offfooooooooooooofmo..',
    '..offffffffffffffffffmo.',
    '..offffffffffffffffffmo.',
    '..offffffffffffffffffmo.',
    '..ommmmmmmmmmmmmmmmmmdo.',
    '..offffffffffffffffffmo.',
    '..osfffffffffffffffffso.',
    '..offfffffffwwwwfffffmo.',
    '..ofkkffwwwwwwwwwwffkko.',
    '..osfwwwwwwwwwwwwwwwwso.',
    '...owwwwwwwwwwwwwwwwmo..',
    '....omvwwwwwwwwwwwwvo...',
    '.....oooooooooooooooo...',
]
# Уши проходят сквозь каску: в этих прямоугольниках каска вырезается по форме уха (слой тела ниже каски).
EAR_BOXES = [(0, 0, 8, 7), (16, 0, 24, 7)]
# Центр стекла фонарика в рамке головы (для кода: блик/луч фонаря).
LAMP_CENTER = (14.5, 4.5)
# Глаза и рот (от начала рамки головы). Мордочка сдвинута на 1 px вправо — поворот 3/4.
EYE_L, EYE_R = (8, 12), (16, 12)
EYES = {
    'open': ((0, ['he', 'ee', 'ee']), (0, ['he', 'ee', 'ee'])),
    'blink': ((-1, ['....', '....', 'eeee']), (-1, ['....', '....', 'eeee'])),
    'happy': ((-1, ['.ee.', 'e..e']), (-1, ['.ee.', 'e..e'])),
    'wide': ((-1, ['.ee', 'ehe', 'eee']), (-1, ['.ee', 'ehe', 'eee'])),
    # сосредоточенный прищур (добыча)
    'focus': ((-1, ['....', 'eeee', '.ee.']), (-1, ['....', 'eeee', '.ee.'])),
    # взгляд вперёд по ходу (ходьба, оглядывается) и назад
    'look_r': ((1, ['he', 'ee', 'ee']), (1, ['he', 'ee', 'ee'])),
    'look_l': ((-1, ['eh', 'ee', 'ee']), (-1, ['eh', 'ee', 'ee'])),
}
MOUTH_AT = (10, 15)
MOUTHS = {
    'smile': ['..nn..', '.qqqq.', '..tt..'],
    'open': ['..nn..', '.qqqq.', '.qttq.'],
    'grit': ['..nn..', '..qq..', '......'],
    # кошачья «ω»: довольный, мурлычет
    'w': ['..nn..', '.q..q.', '..qq..'],
    # зевок при потягивании
    'yawn': ['..nn..', '.qqqq.', 'qttttq', '.qqqq.'],
}

# Туловище 15×8: воротник рубашки, комбинезон с лямками и пуговицами; низ — ножки к лапкам (FEET).
TORSO = [
    '..ooooooooooo..',
    '.OLcccccccCcBO.',
    'OLByccccCcByBDO',
    'OLBBBBBBBBBBBDO',
    'OLBBBBBBBBBBDDO',
    'OBBBBBBBBBBBDDO',
    '.OBBBBBSBBBDDO.',
    '..OSSSO.OSSDO..',
]
# Потягивание и прыжок: туловище длиннее на строку; приземление и замах снизу: короче на строку.
TORSO_KINDS = {
    'normal': TORSO,
    'tall': TORSO[:4] + [TORSO[3]] + TORSO[4:],
    'squash': TORSO[:4] + TORSO[5:],
}
# Строка пояса в туловище каждого вида (снаряжение).
BELT_ROW = {'normal': 4, 'tall': 5, 'squash': 3}
LEGS_ROW = TORSO[-1]

# Лапки ног: левая 5 px, правая 6 px (вид 3/4). Стоя они стоят от feet_at со сдвигом (2, 0) и (8, 0).
FOOT_L = ['owwwo', 'owwvo', '.ooo.']
FOOT_R = ['owwwwo', 'owwwvo', '.oooo.']
# Ножки болтаются (перенос, падение): длиннее на строку.
DANGLE = ['owwwo', 'owwwo', 'owwvo', '.ooo.']
# Поджатые в прыжке.
TUCK = ['owwo', 'owvo', '.oo.']
FEET_PLACES = {
    'stand': ((2, 0), (8, 0), FOOT_L, FOOT_R),
    'crouch': ((1, 0), (9, 0), FOOT_L, FOOT_R),
    'dangle': ((2, 0), (8, 0), DANGLE, DANGLE),
    'tuck': ((3, -1), (8, -1), TUCK, TUCK),
}

TAILS = {
    # хвост-«крючок»: от бедра вниз-влево, вверх, кончик загнут наружу
    'curl': [
        '.ooo......',
        'owwwo.....',
        'owffo.....',
        '.ooffo....',
        '...osfo...',
        '...offo...',
        '..osfmo...',
        '..offmo...',
        '..osfmoooo',
        '...offmmmo',
        '....ooooo.',
    ],
    'up': [
        '.ooo......',
        'owwwo.....',
        'owffo.....',
        '.ooffo....',
        '...osfo...',
        '...offo...',
        '...osfo...',
        '..offmo...',
        '..osfmo...',
        '..offmoooo',
        '...osfmmmo',
        '....ooooo.',
    ],
    'low': [
        'oo.......',
        'owo......',
        'owfo.....',
        '.ofsoooo.',
        '.osffmmmo',
        '..oommmmo',
        '....oooo.',
    ],
    'hang': [
        '......oo.',
        '.....ofmo',
        '....osmo.',
        '...offo..',
        '..osmo...',
        '..offo...',
        '..osmo...',
        '..owwo...',
        '..owwo...',
        '...oo....',
    ],
    # хвост чуть качнулся: кончик на пиксель ближе к спине (дыхание, ходьба)
    'wag': [
        '..ooo.....',
        '.owwwo....',
        '.owffo....',
        '..offo....',
        '...osfo...',
        '...offo...',
        '..osfmo...',
        '..offmo...',
        '..osfmoooo',
        '...offmmmo',
        '....ooooo.',
    ],
    # свисающий хвост качнулся вправо (перенос)
    'hang2': [
        '......oo.',
        '.....ofmo',
        '....osmo.',
        '....offo.',
        '...osmo..',
        '...offo..',
        '...osmo..',
        '...owwo..',
        '...owwo..',
        '....oo...',
    ],
}

PAW = ['.oo.', 'owwo', 'owvo', '.oo.']

# Ручки (мех) по кадрам: ASCII в координатах холста от указанного угла. Лапки (PAW) ставятся отдельно — поверх кирки.
ARMS = {
    # обе лапки держат рукоять у животика; рука задней лапки — короткий обрубок, чтобы грудка комбинезона читалась
    'hold': ((13, 27), [
        'oo..........oo.',
        'olo........ofmo',
        'ofo........ofmo',
        '.o.............',
    ]),
    # замах: лапки у левого плеча, кирка заведена за голову
    'raise': ((11, 25), [
        '..ooo',
        '.olffo',
        '.offo.',
        '..oo..',
    ]),
    # удар: руки вытянуты вперёд-вправо
    'strike': ((22, 25), [
        '..oooo..',
        '.offffo.',
        '..ooffmo',
        '....oooo',
    ]),
    # котика несут: ручки свисают по бокам туловища
    'dangle': ((9, 25), [
        '...oo..............oo..',
        '..olfo............ofmo.',
        '..offo............ofmo.',
        '..ofo..............omo.',
    ]),
    # радость: лапки подняты по бокам головы (рисуются за головой)
    'up': ((8, 19), [
        '.ooo.....................ooo',
        'oflfo...................ofmo',
        '.offfo.................ofmo.',
        '..offfo...............ofmo..',
        '...offfo.............ofmo...',
        '....oooo............ooo.....',
    ]),
    # ── новый режим ──
    # ходьба с грузом: локти в стороны, лапки сжимают лямки рюкзака на груди (лапки — PAW поверх лямок)
    'carry': ((12, 27), [
        'oo..............oo',
        'ofo............ofo',
        'ofo............omo',
        '.o..............o.',
    ]),
    # подбор: обе руки тянутся вперёд-вниз к мешочку на полу
    'reach': ((22, 28), [
        '.oooo....',
        'offffoo..',
        '.oofffmo.',
        '...ooofmo',
        '.....oooo',
    ]),
    # разгрузка: рука заведена через левое плечо за голову к горловине рюкзака (рисуется за головой)
    'back': ((4, 14), [
        '..ooo....',
        '.oflfo...',
        '..offfo..',
        '...offfo.',
        '....offfo',
        '.....offo',
        '......ooo',
    ]),
    # разгрузка: обе руки вытянуты вперёд-вправо ниже мордочки, несут мешочек к складу
    'toss': ((23, 26), [
        '..ooooo..',
        '.offfffo.',
        '..oofffmo',
        '....ooooo',
    ]),
}


class Pose:
    """Кадр котика: положения частей, выражение мордочки и геометрия кирки (рукоять: торец → верх под головкой).
    lfoot / rfoot — сдвиг левой и правой лапки ноги (x вперёд по взгляду, y < 0 — поднята), torso_kind — normal/tall/squash.
    Если туловище выше ножек, промежуток заполняется строкой ножек (котик вытянулся, а не оторвался от лапок)."""

    def __init__(self, head=(8, 6), torso=(13, 25), torso_kind='normal', feet='stand', feet_at=(13, 33),
                 lfoot=(0, 0), rfoot=(0, 0), tail='curl', tail_at=(4, 21), arms='hold', arms_dy=0,
                 paws=((18, 29), (24, 27)), eyes='open', mouth='smile', pick=((19, 31), (34, 26)), pick_half=5.5,
                 pick_behind_head=False):
        self.head, self.torso, self.torso_kind = head, torso, torso_kind
        self.feet, self.feet_at, self.lfoot, self.rfoot = feet, feet_at, lfoot, rfoot
        self.tail, self.tail_at, self.arms, self.arms_dy, self.paws = tail, tail_at, arms, arms_dy, paws
        self.eyes, self.mouth, self.pick, self.pick_behind_head = eyes, mouth, pick, pick_behind_head
        # полудлина головки кирки: в замахе кирка развёрнута к зрителю и короче (ракурс)
        self.pick_half = pick_half
        # поднятые лапки уходят за голову: видны только кончики рук и сами лапки (и рука, заведённая через плечо)
        self.arms_behind_head = arms in ('up', 'back')


def _sh(pt, dx, dy):
    return (pt[0] + dx, pt[1] + dy)


def base(dy: int = 0, dx: int = 0, **kw) -> Pose:
    """Стойка (idle0) со смещением всего, что выше ножек, на dy (вниз +) и лапок с киркой на dx (качание при ходьбе)."""
    d = dict(head=(8, 6 + dy), torso=(13, 25 + dy), tail_at=(4, 21 + dy), arms_dy=dy,
             paws=((18 + dx, 29 + dy), (24 + dx, 27 + dy)), pick=((19 + dx, 31 + dy), (34 + dx, 26 + dy)))
    d.update(kw)
    return Pose(**d)


# Ходьба: (покачивание корпуса, левая лапка, правая лапка, хвост) — контакт, опускание, проход, подъём для каждой ноги.
WALK = [
    (0, (1, 0), (-1, 0), 'curl'), (1, (1, 0), (-1, -1), 'curl'), (0, (0, 0), (0, -2), 'wag'), (-1, (-1, 0), (1, -1), 'wag'),
    (0, (-1, 0), (1, 0), 'curl'), (1, (-1, -1), (1, 0), 'curl'), (0, (0, -2), (0, 0), 'wag'), (-1, (1, -1), (-1, 0), 'wag'),
]

CROUCH = dict(head=(8, 8), torso=(13, 27), torso_kind='squash', feet='crouch', tail='low', tail_at=(5, 27))
LAUNCH = dict(head=(8, 4), torso=(13, 23), torso_kind='tall', arms='up', arms_dy=-1, paws=((7, 16), (32, 16)),
              tail='up', tail_at=(4, 19), pick=((34, 24), (34, 6)), pick_half=5.0)
AIR = dict(head=(8, 5), torso=(13, 24), feet='tuck', feet_at=(13, 33), arms='up', paws=((7, 17), (32, 17)),
           tail='up', tail_at=(4, 20), pick=((33, 22), (33, 4)), pick_half=5.0)
DRAG = dict(feet='dangle', tail='hang', tail_at=(5, 29), arms='dangle', paws=((10, 29), (27, 29)), eyes='wide',
            pick=((27, 28), (31, 36)))

POSES: Dict[str, Pose] = {
    # дыхание: вдох — всё выше ножек опускается на 1 px; хвост покачивается
    'idle0': base(0),
    'idle1': base(0, tail='up', tail_at=(4, 20)),
    'idle2': base(1, tail='up', tail_at=(4, 21)),
    'idle3': base(1, tail='wag', tail_at=(4, 22)),
    'idle4': base(0, tail='wag'),
    'idle5': base(0, mouth='w'),
    'blink': base(0, eyes='blink'),
    'blink_d': base(1, eyes='blink'),
    # потягивание: присел, лапки вверх, вытянулся с зевком, подержался, опустил лапки, встряхнул головой
    'stretch0': base(1, eyes='blink'),
    'stretch1': Pose(arms='up', arms_dy=3, paws=((9, 22), (30, 22)), eyes='blink', pick=((32, 31), (32, 13)), pick_half=5.0),
    'stretch2': Pose(head=(8, 4), torso=(13, 23), torso_kind='tall', arms='up', arms_dy=-1, paws=((7, 16), (32, 16)),
                     eyes='blink', mouth='yawn', tail='up', tail_at=(4, 19), pick=((34, 30), (34, 6)), pick_half=5.0),
    'stretch3': Pose(head=(8, 3), torso=(13, 22), torso_kind='tall', arms='up', arms_dy=-2, paws=((6, 15), (33, 15)),
                     eyes='blink', mouth='yawn', tail='up', tail_at=(4, 18), pick=((35, 29), (35, 5)), pick_half=5.0),
    'stretch4': Pose(head=(8, 3), torso=(13, 22), torso_kind='tall', arms='up', arms_dy=-2, paws=((6, 15), (33, 15)),
                     eyes='blink', mouth='yawn', tail='wag', tail_at=(4, 19), pick=((35, 29), (35, 5)), pick_half=5.0),
    'stretch5': Pose(head=(8, 4), torso=(13, 23), torso_kind='tall', arms='up', arms_dy=-1, paws=((7, 16), (32, 16)),
                     eyes='blink', tail='up', tail_at=(4, 19), pick=((34, 30), (34, 6)), pick_half=5.0),
    'stretch6': base(0, eyes='blink', mouth='w'),
    'stretch7': base(1, eyes='happy', mouth='w'),
    'stretch8': base(0, head=(7, 6), eyes='look_l'),
    'stretch9': base(0, head=(9, 6), eyes='look_r'),
    # ходьба: кирка в лапках, взгляд вперёд
    **{f'walk{i}': base(b, dx=lf[0], lfoot=lf, rfoot=rf, tail=t, tail_at=(4, 21 + b), eyes='look_r')
       for i, (b, lf, rf, t) in enumerate(WALK)},
    # добыча: стойка (дыхание) → подготовка (присел) → замах → верхняя точка → удар → отдача → возврат
    'mine_ready': base(0, paws=((19, 29), (25, 26)), pick=((20, 31), (33, 19)), eyes='focus', mouth='w'),
    'mine_ready2': base(1, tail='up', tail_at=(4, 21), paws=((19, 30), (25, 27)), pick=((20, 32), (33, 20)),
                        eyes='focus', mouth='w'),
    'mine_antic': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((21, 29), (25, 25)), pick=((22, 31), (30, 14)),
                       eyes='focus', mouth='grit'),
    'mine_raise': Pose(head=(7, 6), arms='raise', paws=((12, 23), (13, 25)), pick=((15, 29), (10, 8)),
                       pick_half=5.0, pick_behind_head=True, tail='low', tail_at=(5, 26), eyes='focus', mouth='grit'),
    'mine_top': Pose(head=(7, 6), arms='raise', arms_dy=-1, paws=((9, 22), (10, 24)), pick=((13, 28), (3, 11)),
                     pick_half=4.5, pick_behind_head=True, tail='low', tail_at=(5, 26), eyes='focus', mouth='grit'),
    'mine_strike': Pose(head=(9, 7), torso=(14, 25), arms='strike', paws=((23, 26), (26, 28)), eyes='blink',
                        mouth='open', pick=((22, 27), (34, 33))),
    'mine_impact': Pose(head=(10, 8), torso=(14, 26), torso_kind='squash', feet='crouch', arms='strike', arms_dy=1,
                        paws=((24, 27), (27, 29)), pick=((23, 28), (35, 34)), eyes='blink', mouth='grit',
                        tail='low', tail_at=(5, 26)),
    'mine_recoil': Pose(head=(9, 6), torso=(14, 25), arms='strike', arms_dy=-1, paws=((23, 25), (26, 26)),
                        pick=((22, 26), (34, 29)), eyes='wide'),
    'mine_return': base(0, paws=((19, 29), (25, 26)), pick=((20, 30), (33, 23)), eyes='focus', mouth='w'),
    # перенос: ножки болтаются по очереди, хвост раскачивается
    'drag0': Pose(**DRAG),
    'drag1': Pose(**{**DRAG, 'tail': 'hang2', 'pick': ((27, 28), (30, 36))}, lfoot=(0, -1), mouth='open'),
    'drag2': Pose(**DRAG, mouth='w'),
    'drag3': Pose(**{**DRAG, 'tail': 'hang2', 'pick': ((27, 28), (32, 36))}, rfoot=(0, -1), mouth='open'),
    # появление из бочки: присел → прыжок → в воздухе → падает → приземлился → встал → оглядывается → радуется
    'spawn0': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((18, 31), (24, 29)), pick=((19, 33), (34, 28)), eyes='blink'),
    'spawn1': Pose(**LAUNCH, eyes='wide', mouth='open'),
    'spawn2': Pose(**AIR, eyes='happy', mouth='open'),
    'spawn3': Pose(feet='dangle', arms='up', arms_dy=2, paws=((8, 20), (31, 20)), eyes='wide', tail='up',
                   tail_at=(4, 20), pick=((32, 25), (32, 7)), pick_half=5.0),
    'spawn4': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((16, 31), (26, 29)), pick=((17, 33), (34, 29)), eyes='happy'),
    'spawn5': base(0, eyes='wide'),
    'spawn6': base(0, head=(9, 6), eyes='look_r'),
    'spawn7': base(0, eyes='happy', mouth='open'),
    # радость: присел → прыжок → в воздухе → приземлился с поднятыми лапками → ликует → успокоился
    'joy0': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((18, 31), (24, 29)), pick=((19, 33), (34, 28)), eyes='happy'),
    'joy1': Pose(**LAUNCH, eyes='happy', mouth='open'),
    'joy2': Pose(**AIR, eyes='happy', mouth='open'),
    'joy3': Pose(**{**CROUCH, 'tail': 'up', 'tail_at': (4, 23)}, arms='up', arms_dy=3, paws=((7, 20), (32, 20)),
                 eyes='happy', mouth='open', pick=((33, 25), (33, 7)), pick_half=5.0),
    'joy4': Pose(head=(8, 5), torso=(13, 24), feet_at=(13, 32), tail='low', tail_at=(5, 25), arms='up',
                 paws=((7, 17), (32, 17)), eyes='happy', mouth='open', pick=((33, 22), (33, 4)), pick_half=5.0),
    'joy5': base(0, eyes='happy', mouth='w'),
    # добыча с упора (пила, бур, бензопила, лазер): инструмент перед собой → оттянул → прицелился → подал вперёд →
    # упёрся (эффект инструмента) → отдача → стойка. Ось оружия — от задней рукояти вперёд, к цели.
    'tool_ready': base(0, paws=((19, 29), (25, 27)), pick=((18, 29), (30, 27)), eyes='focus', mouth='w'),
    'tool_ready2': base(1, tail='up', tail_at=(4, 21), paws=((19, 30), (25, 28)), pick=((18, 30), (30, 28)),
                        eyes='focus', mouth='w'),
    'tool_antic': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((18, 30), (24, 28)), pick=((16, 31), (28, 28)),
                       eyes='focus', mouth='grit'),
    'tool_raise': base(0, paws=((19, 28), (25, 26)), pick=((18, 28), (30, 24)), eyes='focus', mouth='grit'),
    'tool_top': base(-1, paws=((19, 27), (25, 25)), pick=((18, 27), (30, 23)), eyes='focus', mouth='grit'),
    'tool_strike': Pose(head=(9, 7), torso=(14, 25), arms='strike', paws=((23, 26), (26, 28)), eyes='focus',
                        mouth='open', pick=((21, 28), (33, 29))),
    'tool_impact': Pose(head=(10, 8), torso=(14, 26), torso_kind='squash', feet='crouch', arms='strike', arms_dy=1,
                        paws=((24, 27), (27, 29)), pick=((22, 29), (34, 30)), eyes='blink', mouth='grit',
                        tail='low', tail_at=(5, 26)),
    'tool_recoil': Pose(head=(9, 6), torso=(14, 25), arms='strike', arms_dy=-1, paws=((23, 25), (26, 26)),
                        pick=((21, 27), (33, 27)), eyes='wide'),
    'tool_return': base(0, paws=((19, 29), (25, 27)), pick=((18, 29), (30, 27)), eyes='focus', mouth='w'),
}

# ── новый режим: груз (ТЗ §7, §16) ──
# Ходьба с полной сумкой: шаг тот же (WALK), но корпус ниже на 1 px и качается на 2 — тяжело; голова подана вперёд
# на 1 px; локти в стороны, лапки сжимают лямки на груди; взгляд сосредоточенный. Кирка убрана (слоя кирки нет).
CARRY_BOB = [1, 2, 1, 0, 1, 2, 1, 0]
for _i, (_b, _lf, _rf, _t) in enumerate(WALK):
    _c = CARRY_BOB[_i]
    POSES[f'carry{_i}'] = base(_c, head=(9, 6 + _c), lfoot=_lf, rfoot=_rf, tail=_t, tail_at=(4, 21 + _c), arms='carry',
                               paws=((15, 26 + _c), (21, 25 + _c)), eyes='focus', mouth='w')
POSES.update({
    # подбор порции (цикл на каждую порцию): присел и тянется к мешочку → схватил → закинул через плечо в рюкзак →
    # выпрямился
    'pickup0': Pose(**CROUCH, arms='reach', paws=((26, 30), (30, 31)), eyes='look_r', mouth='w'),
    'pickup1': Pose(**{**CROUCH, 'head': (9, 9), 'torso': (14, 28)}, arms='reach', arms_dy=1, paws=((26, 31), (31, 32)),
                    eyes='focus', mouth='grit'),
    'pickup2': Pose(head=(7, 6), arms='back', paws=((4, 11), (7, 13)), tail='low', tail_at=(5, 26), eyes='blink',
                    mouth='grit'),
    'pickup3': base(1, eyes='happy', mouth='w'),
    # разгрузка у склада (цикл на каждую порцию): достал мешочек из рюкзака через плечо → держит его перед собой →
    # присел и опустил вниз, к ногам (там, ниже места разгрузки, — жёлоб depot_chute и воронка склада) → выпрямился
    # довольный. Зачисление ведёт симуляция, кадры его не определяют (ТЗ §7.3).
    'unload0': Pose(head=(7, 6), arms='back', paws=((4, 11), (7, 13)), tail='low', tail_at=(5, 26), eyes='look_l',
                    mouth='w'),
    'unload1': base(0, arms='hold', arms_dy=-1, paws=((19, 25), (24, 25)), eyes='focus', mouth='open'),
    'unload2': Pose(**CROUCH, arms='hold', arms_dy=2, paws=((18, 29), (25, 29)), eyes='happy', mouth='open'),
    'unload3': base(1, eyes='happy', mouth='open'),
})
assert set(POSES) == set(CAT_FRAMES), set(POSES) ^ set(CAT_FRAMES)


class Parts:
    """Маски частей кадра (для отметин окраса и вырезов снаряжения)."""

    def __init__(self) -> None:
        self.head: Mask = set()
        self.tail: Mask = set()
        self.arms: Mask = set()     # ручки и лапки
        self.paws: Mask = set()     # только лапки (держат кирку)
        self.feet: Mask = set()


def head_layer(p: Pose) -> Canvas:
    """Голова кадра с глазами и ртом (без каски)."""
    cv = Canvas(CW, CH)
    hx, hy = p.head
    stamp(cv, HEAD, L_FUR, hx, hy)
    (dl, el), (dr, er) = EYES[p.eyes]
    stamp(cv, el, L_FUR, hx + EYE_L[0] + dl, hy + EYE_L[1])
    stamp(cv, er, L_FUR, hx + EYE_R[0] + dr, hy + EYE_R[1])
    stamp(cv, MOUTHS[p.mouth], L_FUR, hx + MOUTH_AT[0], hy + MOUTH_AT[1])
    return cv


def feet_stamps(p: Pose) -> List[Tuple[Sequence[str], int, int]]:
    (lx, ly), (rx, ry), lrows, rrows = FEET_PLACES[p.feet]
    fx, fy = p.feet_at
    return [(lrows, fx + lx + p.lfoot[0], fy + ly + p.lfoot[1]), (rrows, fx + rx + p.rfoot[0], fy + ry + p.rfoot[1])]


def body_master(p: Pose, parts: Optional[Parts] = None) -> Canvas:
    """Тело кадра ролями: хвост, туловище (с ножками до лапок), лапки ног, голова, ручки и лапки."""
    parts = parts if parts is not None else Parts()
    cv = Canvas(CW, CH)
    stamp(cv, TAILS[p.tail], L_FUR, *p.tail_at)
    parts.tail = stamp_mask(TAILS[p.tail], *p.tail_at)
    rows = TORSO_KINDS[p.torso_kind]
    tx, ty = p.torso
    stamp(cv, rows, L_FUR, tx, ty)
    # Котик вытянулся (туловище выше обычного над ножками): промежуток — строка ножек.
    feet_top = min(y for _, _, y in feet_stamps(p))
    for y in range(ty + len(rows), feet_top):
        stamp(cv, [LEGS_ROW], L_FUR, tx, y)
    for rows_f, x, y in feet_stamps(p):
        stamp(cv, rows_f, L_FUR, x, y)
        parts.feet |= stamp_mask(rows_f, x, y)
    (ax, ay), arm_rows = ARMS[p.arms]
    if p.arms_behind_head:
        stamp(cv, arm_rows, L_FUR, ax, ay + p.arms_dy)
    cv.blit(head_layer(p), 0, 0)
    parts.head = stamp_mask(HEAD, *p.head)
    if not p.arms_behind_head:
        stamp(cv, arm_rows, L_FUR, ax, ay + p.arms_dy)
    parts.arms = stamp_mask(arm_rows, ax, ay + p.arms_dy)
    for px, py in p.paws:
        stamp(cv, PAW, L_FUR, px, py)
        parts.paws |= stamp_mask(PAW, px, py)
    parts.arms |= parts.paws
    return cv


# ───────────────────────── отметины окраса ─────────────────────────
FUR_ROLES = ('f1', 'f2', 'f3', 'f4', 'fs')
TO_A = {'f1': 'pa1', 'f2': 'pa2', 'f3': 'pa3', 'f4': 'pa4', 'fs': 'pa2'}
TO_B = {'f1': 'pb1', 'f2': 'pb2', 'f3': 'pb3', 'f4': 'pb4', 'fs': 'pb2'}


def _recolor(cv: Canvas, where, mapping: Dict[str, str]) -> None:
    keys = {KEY[k]: KEY[v] for k, v in mapping.items()}
    for y in range(cv.h):
        for x in range(cv.w):
            px = cv.px[x, y]
            if px[3] and px[:3] in keys and where(x, y):
                nv = keys[px[:3]]
                cv.px[x, y] = (nv[0], nv[1], nv[2], 255)


def marking(master: Canvas, fur: str, p: Pose, parts: Parts) -> Canvas:
    """Отметины окраса на копии мастера ролями: проточина (чёрный, коричневый), пятна калико (белый), «пойнты» (кремовый).
    Координаты областей — от начала рамки головы (видимая часть мордочки — строки 11–18, уши — 0–7)."""
    cv = master.copy()
    hx, hy = p.head

    def head_box(x0, y0, x1, y1):
        return lambda x, y: (x, y) in parts.head and x0 <= x - hx <= x1 and y0 <= y - hy <= y1

    if fur in ('black', 'brown'):
        # белая проточина между глаз до носа
        _recolor(cv, head_box(12, 11, 13, 14), TO_A)
    elif fur == 'white':
        # калико: рыжее пятно на левом ухе и щеке, чёрное — на правом ухе; хвост рыжий
        _recolor(cv, head_box(0, 0, 9, 14), TO_A)
        _recolor(cv, head_box(15, 0, 23, 8), TO_B)
        _recolor(cv, lambda x, y: (x, y) in parts.tail, TO_A)
    elif fur == 'cream':
        # «пойнты»: тёмные уши, хвост, лапки и маска у носа
        _recolor(cv, head_box(0, 0, 8, 7), TO_B)
        _recolor(cv, head_box(15, 0, 23, 7), TO_B)
        _recolor(cv, lambda x, y: (x, y) in parts.tail, TO_B)
        _recolor(cv, lambda x, y: (x, y) in parts.feet or (x, y) in parts.paws, {'mz': 'pb3', 'mzs': 'pb2'})
        _recolor(cv, head_box(10, 14, 15, 16), {'mz': 'pb4'})
    return cv


# ───────────────────────── каска ─────────────────────────
L_HELM = {'O': 'ho', 'D': 'h1', 'S': 'h2', 'B': 'h3', 'L': 'h4', 'H': 'h5', 'N': 'band',
          'R': 'lampo', 'G': 'glass', 'g': 'glassl', 'W': 'glassw', 'A': 'glassd',
          'T': 'hs', 'r': 'hr', 'J': 'hg', 'j': 'hgl', 'X': 'hst'}
HELMET = [
    '........OOOOOOOO........',
    '......OOHHLLBBBBOO......',
    '.....OHLLBBBBRRRBSO.....',
    '....OHLBBBBBRWWgRSSO....',
    '...OHLBBBBBRgWWgGRSSO...',
    '...OLBBBBBBRggggGRSSO...',
    '..OLBBBBBBBBRGGARSSSSO..',
    '..ONNNNNNNNNNRRRNNNNNO..',
    '.OLLLLBBBBBBBBBBBBBBSSO.',
    'OLBBBBBBBBBBBBBBBBBBBSSO',
    '.OODDDDDDDDDDDDDDDDDDOO.',
]


def helmet_master(p: Pose) -> Canvas:
    """Каска кадра с фонариком; уши головы проходят сквозь прорези. Цвет — круг уровней (helmet_map)."""
    cv = Canvas(CW, CH)
    hx, hy = p.head
    stamp(cv, HELMET, L_HELM, hx, hy)
    head = Canvas(CW, CH)
    stamp(head, HEAD, L_FUR, hx, hy)
    for x0, y0, x1, y1 in EAR_BOXES:
        for y in range(hy + y0, hy + y1):
            for x in range(hx + x0, hx + x1):
                if head.opaque(x, y):
                    cv.set(x, y, None)
    return cv


# ───────────────────────── кирка ─────────────────────────
def _crescent(P: Tuple[float, float], u: Tuple[float, float], half: float = 5.5, bend: float = 1.6,
              thick: float = 2.8, off: float = 0.3, blade: float = 0.0) -> Mask:
    """Головка кирки: дуга поперёк рукояти, концы загнуты назад к рукояти (как у настоящей кирки).
    thick 2.8 — середина дуги в 3 px заливки: тоньше головка читается на полу только как тёмный контур.
    blade > 0 — кайло-тесло: конец t < 0 не сужается, а расширяется до ширины blade и срезан плоским лезвием."""
    ux, uy = u
    n = math.hypot(ux, uy)
    ux, uy = ux / n, uy / n
    nx, ny = -uy, ux
    pts = []
    for i in range(81):
        t = -1 + 2 * i / 80
        if blade and t < -0.86:
            continue
        cx = P[0] + nx * t * half - ux * bend * t * t + ux * off
        cy = P[1] + ny * t * half - uy * bend * t * t + uy * off
        r = max(0.55, thick * math.sqrt(max(0.0, 1 - t * t))) / 2
        if blade and t < 0:
            # лезвие расширяется к концу (как у тесла): от толщины середины до blade
            r = max(r, (thick + (blade - thick) * min(1.0, -t / 0.8)) / 2)
        pts.append((cx, cy, r))
    m: Mask = set()
    for y in range(CH):
        for x in range(CW):
            qx, qy = x + 0.5, y + 0.5
            for cx, cy, r in pts:
                if (qx - cx) ** 2 + (qy - cy) ** 2 <= r * r:
                    m.add((x, y))
                    break
    return m


def pick_master(p: Pose, front: Mask, behind: Mask, scale: float = 1.0, thick: float = 2.8, blade: float = 0.0,
                edge: bool = False) -> Canvas:
    """Кирка кадра. Слой кирки лежит выше тела и каски, поэтому «загораживание» вырезается здесь же:
    front — лапки, сжимающие рукоять; behind — голова и каска, если кирка заведена за голову.
    scale и thick — размер головки (по умолчанию — обычная кирка); blade — плоское лезвие на верхнем конце
    (_crescent); edge — заточенная кромка: крайний ряд лезвия (≈1 px) ролью блика p4."""
    (bx, by), (tx, ty) = p.pick
    handle = Canvas(CW, CH)
    handle.line(bx, by, tx, ty, KEY['w2'])
    handle.outline(KEY['wo'])
    head = Canvas(CW, CH)
    P = (tx + 0.5, ty + 0.5)
    half = p.pick_half * scale
    m = _crescent(P, (tx - bx, ty - by), half=half, bend=1.6 * scale, thick=thick, blade=blade)
    for x, y in m:
        up, left = (x, y - 1) not in m, (x - 1, y) not in m
        down, right = (x, y + 1) not in m, (x + 1, y) not in m
        role = 'p2'
        if up or left:
            role = 'p3'
        elif down or right:
            role = 'p1'
        head.set(x, y, KEY[role])
    lit = [q for q in m if head.get(*q)[:3] == KEY['p3']]
    if lit:
        gx, gy = min(lit, key=lambda q: (q[0] + q[1], q[1]))
        head.set(gx, gy, KEY['p4'])
    if edge:
        # кромка лезвия: пиксели головки у самого конца t < 0 (по нормали к рукояти)
        ux, uy = tx - bx, ty - by
        ln = math.hypot(ux, uy)
        nx, ny = -uy / ln, ux / ln
        tt = {q: (q[0] + 0.5 - P[0]) * nx + (q[1] + 0.5 - P[1]) * ny for q in m}
        t_end = min(tt.values())
        for q, t in tt.items():
            if t < t_end + 1.3:
                head.set(q[0], q[1], KEY['p4'])
    head.outline(KEY['po'])
    cv = Canvas(CW, CH)
    cv.blit(handle, 0, 0)
    cv.blit(head, 0, 0)
    for x, y in front | behind:
        cv.set(x, y, None)
    return cv


# ───────────────────────── палитры ─────────────────────────
def _ramp(name: str) -> List[str]:
    return [f'{name}{i}' for i in range(5)]


def fur_map(fur: str) -> Dict[str, str]:
    r = _ramp(f'fur_{fur}')
    m = {'fo': r[0], 'f1': r[1], 'f2': r[2], 'f3': r[3], 'f4': r[4], 'fs': r[2],
         'mz': 'cream3', 'mzs': 'cream2', 'ear': 'pink2', 'chk': 'pink3',
         'eye': 'ink1', 'eyeh': 'cream4', 'nose': 'pink2', 'mouth': 'ink2', 'tongue': 'pink2',
         'bo': 'blue1', 'b1': 'blue2', 'b2': 'blue2', 'b3': 'blue3', 'b4': 'blue4', 'sh': 'cream3', 'shs': 'cream1', 'btn': 'gold3'}
    # пятна по умолчанию — цвет самой шерсти
    for k in range(1, 5):
        m[f'pa{k}'] = r[k]
        m[f'pb{k}'] = r[k]
    # полоски (табби) только у рыжего, серого и коричневого
    if fur in ('black', 'white', 'cream'):
        m['fs'] = r[3]
    # у светлых окрасов свой нижний тон слишком светлый для контура на плитах пола
    if fur == 'white':
        m.update(fo='fur_gray0')
        ginger, black = _ramp('fur_ginger'), _ramp('fur_black')
        for k in range(1, 5):
            m[f'pa{k}'] = ginger[k]
            m[f'pb{k}'] = black[k]
    if fur == 'cream':
        m.update(fo='fur_brown0')
        brown = _ramp('fur_brown')
        for k in range(1, 5):
            m[f'pb{k}'] = brown[k]
    if fur in ('black', 'brown'):
        white = _ramp('fur_white')
        for k in range(1, 5):
            m[f'pa{k}'] = white[k]
        if fur == 'black':
            # «смокинг»: белоснежная мордочка, подбородок и лапки
            m.update(mz='fur_white3', mzs='fur_white1')
    return m


# Mia — спасённая в шахте 1 (первая сессия, Docs/Rework/FIRST_SESSION.md): белая шерсть с рыжими пятнами на обоих ушах и
# хвосте, сиреневый комбинезон и розовый шейный платок вместо воротника рубашки — узнаётся среди котиков в синих комбинезонах
# и под любым головным убором команды. Те же формы и кадры, что у всех окрасов (cat_body_mia_*).
MIA_FUR = 'mia'


def mia_map() -> Dict[str, str]:
    m = fur_map('white')
    ginger = _ramp('fur_ginger')
    for k in range(1, 5):
        m[f'pb{k}'] = ginger[k]
    m.update(bo='purple1', b1='purple2', b2='purple2', b3='purple3', b4='purple4', sh='pink3', shs='pink1', btn='gold4')
    return m


# Каска: контур, тень поля, тень, основа, свет, блик.
HELMET_RAMPS = {
    'yellow': ['gold0', 'gold1', 'gold2', 'gold3', 'gold4', 'gold5'],
    'green': ['green1', 'green2', 'green3', 'green4', 'green5', 'green6'],
    'blue': ['blue1', 'blue2', 'blue3', 'blue4', 'blue5', 'blue6'],
    'purple': ['purple1', 'purple2', 'purple3', 'purple4', 'purple5', 'purple6'],
    'red': ['red1', 'red2', 'red3', 'red4', 'red5', 'red6'],
    'teal': ['teal1', 'teal2', 'teal3', 'teal4', 'teal5', 'teal6'],
    'pink': ['pink0', 'pink1', 'pink1', 'pink2', 'pink3', 'pink4'],
    'white': ['fur_gray0', 'fur_white0', 'fur_white1', 'fur_white3', 'fur_white4', 'white'],
    'gold': ['gold0', 'gold2', 'gold3', 'gold4', 'gold5', 'cream4'],
}


def helmet_map(colour: str) -> Dict[str, str]:
    r = HELMET_RAMPS[colour]
    return {'ho': r[0], 'h1': r[1], 'h2': r[2], 'h3': r[3], 'h4': r[4], 'h5': r[5], 'band': 'navy2',
            'lampo': 'navy1', 'glass': 'amber4', 'glassl': 'amber5', 'glassw': 'white', 'glassd': 'amber3',
            'hs': 'cream4', 'hr': 'cream4', 'hg': 'teal4', 'hgl': 'teal6', 'hst': 'gold5'}


# Кирка (первое оружие круга, а в «двух кирках» — обе): железная головка на деревянной рукояти.
# Головка: контур, тёмная кромка (низ-право), тело, светлая кромка (верх-лево), блик; рукоять: контур, дерево.
PICK_IRON = {'wo': 'wood1', 'w2': 'wood4', 'po': 'navy1', 'p1': 'navy5', 'p2': 'navy7', 'p3': 'navy8', 'p4': 'cream4'}


def paint(master: Canvas, mapping: Dict[str, str]) -> Canvas:
    return master.recolor({KEY[k]: v for k, v in mapping.items()})


# ───────────────────────── снаряжение команды (новый режим) ─────────────────────────
# ТЗ §10.1: один комплект на всю команду — головной убор, кирка, рюкзак, обувь; редкости common/rare/epic/legendary
# и base — вид пустого слота. Окрас эти слои не перекрашивает, поэтому они рисуются сразу цветами палитры (ASCII → цвет).
# Всё, что лежит за котиком (рюкзак, груз), вырезается по маске тела: голова, туловище, ручки и хвост остаются поверх,
# а лямки рюкзака лежат на груди поверх комбинезона. Лапки (слой тела) вырезаются из всего, что выше тела.
# Порядок слоёв в CatView снизу вверх: тень 0, кольцо 1, тело 2, обувь 3, рюкзак 4, груз 5, головной убор 6,
# кирка 7, мешочек в лапках 8.
def stamp_pal(cv: Canvas, rows: Sequence[str], legend: Dict[str, str], x: int, y: int) -> None:
    """ASCII сразу цветами палитры: символ → цвет; '.' — пропуск (не стирает)."""
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch in '. ':
                continue
            cv.set(x + i, y + j, legend[ch])


def _cut(cv: Canvas, m: Mask) -> Canvas:
    for x, y in m:
        cv.set(x, y, None)
    return cv


def _check_rows(name: str, rows: Sequence[str]) -> None:
    assert len({len(r) for r in rows}) == 1, (name, [len(r) for r in rows])


# ── головной убор: прорези для ушей как у каски (уши тела торчат сквозь убор — «открытый силуэт ушей», ТЗ §10.1) ──
# Нижние строки 8–10 у всех уборов — поле, как у каски: под ним на мордочке нарисована тень (HEAD, строка 11).
# Украшения над куполом — не выше 3 строк над рамкой головы (в потягивании голова на y = 3).
HAT_COMMON = [  # кожаная шапка: низкая мягкая тулья со швом, ремешок с латунной пряжкой, козырёк вперёд-вправо
    '..........................',
    '..........................',
    '.........OOOOOOO..........',
    '.......OOLLLBBBBOO........',
    '.....OOLLBBBBBTBBSOO......',
    '....OLLBBBBBBBTBBBSSO.....',
    '...OLBBBBBBBBBTBBBBSSO....',
    '..ONNNNNNNNNNRrRNNNNNNO...',
    '.OLLLLLBBBBBBBBBBBBBSSSOO.',
    'OLBBBBBBBBBBBBBBBBBBBBSSSO',
    '.OODDDDDDDDDDDDDDDDDDDDOO.',
]
HAT_RARE = [  # шлем из синей стали (рампа blue, как у кирки, рюкзака и обуви rare): стальной гребень, крупный фонарь
    # с холодным светом в стальной оправе, стальные заклёпки по ободу
    '..........OOOO..........',
    '.........OckkqO.........',
    '........OOckkqOO........',
    '......OOHHkckqBBOO......',
    '.....OHLLBckqRRRRRO.....',
    '....OHLBBBkqRWWWgRRO....',
    '...OHLBBBBkRgWWWggGRO...',
    '...OLBBBBBBRgggggGGRO...',
    '..OLBBBBBBBBRGGGGARSSO..',
    '..ONrNNNrNNNNRRRRRNrNO..',
    '.OLLLLBBBBBBBBBBBBBBSSO.',
    'OLBBBBBBBBBBBBBBBBBBBSSO',
    '.OODDDDDDDDDDDDDDDDDDOO.',
]
HAT_EPIC = [  # шлем с кристаллом-фонарём: высокий светящийся кристалл в золотой оправе, осколки по бокам
    '.............OO.........',
    '............OWjO........',
    '...........OjWGAO.......',
    '........OOOjjGGAOOO.....',
    '......OOHHOjWGGGAOSO....',
    '.....OHLLBOjGGGAAOSO....',
    '....OHLBBBOjGGGAAOSSO...',
    '...OHLBBBBBOjGGAORSSO...',
    '...OLBBBBBBRROGAORRSSO..',
    '..OLBBBBBBBBRRORRRSSSSO.',
    '..ONNNJNNNNNNRRRNNNJNNO.',
    '.OLLLLBBBBBBBBBBBBBBSSO.',
    'OLBBBBBBBBBBBBBBBBBBBSSO',
    '.OODDDDDDDDDDDDDDDDDDOO.',
]
# Корона (не шлем): узкий обод на шерсти лба — поля нет, бока лба и уши открыты; четыре высоких зубца со светлыми
# кончиками, между ними — красная бархатная шапочка (не «золото на золоте»); на ободе бирюза, рубин с белым бликом и
# аметист. Зубцы стоят между ушами (x 7…16 рамки головы): прорези ушей их не режут.
HAT_LEGENDARY = [
    '..........O..O..........',
    '.........OXOOXO.........',
    '.........OYOOYO.........',
    '.......O.OLuVBO.O.......',
    '......OXOOLuVBOOXO......',
    '......OYuVLVVBuVYO......',
    '......OLuBLVVBSVBO......',
    '......OLBBLVVBSSBO......',
    '......OLBBLVvBSSBO......',
    '......OLBBLvvBSSBOO.....',
    '.....OHHHLLWRLLLLLBO....',
    '.....OBBTtBRRBBpPBSO....',
    '.....OSSTTSRrSSPPSSO....',
    '......OOOOOOOOOOOOO.....',
]
# Рисунок, сдвиг его начала от рамки головы (dx, dy) и цвета. base — прежняя жёлтая каска с фонариком (вид пустого
# слота; совпадает с cat_helmet_yellow_* пиксель в пиксель, атлас хранит её один раз).
HATS = {
    'base': (HELMET, (0, 0), {'O': 'gold0', 'D': 'gold1', 'S': 'gold2', 'B': 'gold3', 'L': 'gold4', 'H': 'gold5',
                              'N': 'navy2', 'R': 'navy1', 'G': 'amber4', 'g': 'amber5', 'W': 'white', 'A': 'amber3'}),
    'common': (HAT_COMMON, (0, 0), {'O': 'wood0', 'D': 'wood1', 'S': 'wood2', 'B': 'wood3', 'L': 'wood4', 'H': 'wood5',
                                    'T': 'wood2', 'N': 'wood1', 'R': 'gold2', 'r': 'gold4'}),
    'rare': (HAT_RARE, (0, -2), {'O': 'navy0', 'D': 'blue1', 'S': 'blue2', 'B': 'blue3', 'L': 'blue4', 'H': 'blue5',
                                 'N': 'stone3', 'r': 'stone7', 'R': 'stone4', 'G': 'blue6', 'g': 'blue7', 'W': 'white',
                                 'A': 'blue5', 'c': 'stone7', 'k': 'stone6', 'q': 'stone4'}),
    'epic': (HAT_EPIC, (0, -3), {'O': 'purple0', 'D': 'purple1', 'S': 'purple2', 'B': 'purple3', 'L': 'purple4',
                                 'H': 'purple5', 'N': 'purple1', 'R': 'gold3', 'J': 'teal5', 'G': 'teal4', 'j': 'teal6',
                                 'W': 'white', 'A': 'teal3'}),
    'legendary': (HAT_LEGENDARY, (0, -3), {'O': 'amber0', 'S': 'gold2', 'B': 'gold3', 'L': 'gold4', 'H': 'gold5',
                                           'Y': 'gold5', 'X': 'cream4', 'V': 'red2', 'v': 'red1', 'u': 'red3',
                                           'R': 'red3', 'r': 'red5', 'T': 'teal4', 't': 'teal6', 'P': 'purple4',
                                           'p': 'purple6', 'W': 'white'}),
}
for _r, (_rows, _, _) in HATS.items():
    _check_rows(f'hat {_r}', _rows)


def _ear_cut(cv: Canvas, p: Pose) -> None:
    """Прорези для ушей: в прямоугольниках EAR_BOXES убор вырезается по форме ушей головы."""
    hx, hy = p.head
    head = Canvas(CW, CH)
    stamp(head, HEAD, L_FUR, hx, hy)
    for x0, y0, x1, y1 in EAR_BOXES:
        for y in range(hy + y0, hy + y1):
            for x in range(hx + x0, hx + x1):
                if head.opaque(x, y):
                    cv.set(x, y, None)


def hat_layer(p: Pose, rarity: str, parts: Parts) -> Canvas:
    rows, (dx, dy), legend = HATS[rarity]
    cv = Canvas(CW, CH)
    hx, hy = p.head
    stamp_pal(cv, rows, legend, hx + dx, hy + dy)
    _ear_cut(cv, p)
    return _cut(cv, parts.paws)


# ── рюкзак: за спиной слева (котик смотрит вправо), видна часть левее головы и туловища; лямки — на груди ──
# Рисунок 14 шириной ставится от начала туловища (tx − 12 + dx, ty − 13): верх рюкзака — на уровне низа ушей, видимая
# часть — левее головы (верх с горловиной и отделкой) и полоска левее хвоста. Горловина у всех редкостей на одной
# строке (PACK_LIP), поэтому одно наложение груза подходит к любому рюкзаку. Чем выше редкость, тем рюкзак богаче
# отделкой (у rare — фонарь сбоку, у epic — кристаллы, у legendary — золото и лапка) и шире влево.
PACK_AT = (-12, -13)
PACK_LIP = 1             # строка верхнего края горловины в рисунке рюкзака
PACK_BASE = [  # котомка из мешковины, горловина стянута шнурком
    '..............',
    '...oooooooo...',
    '..oyzzzzyyxo..',
    '...ommmmmmo...',
    '..oyzzyyyyyo..',
    '.oyzzyyyyyyxo.',
    '.ozyyyyyyyyxo.',
    '.ozyyyyyyyxxo.',
    '.oyyyyyyyyxxo.',
    '.oyyyyyyyyxxo.',
    '.oyyyyyyyxxxo.',
    '.oyyyyyyyxxxo.',
    '..oxxxxxxxxo..',
    '...oooooooo...',
]
PACK_COMMON = [  # кожаный рюкзак: валик горловины, ремешок с латунной пряжкой, боковой карман
    '..............',
    '...ooooooooo..',
    '..oDDDDDDDDDo.',
    '.oHLLLLLLLLLBo',
    '.oSSSSSSSSSSSo',
    'oLLBRBBBBBBBDo',
    'oLBBRBBBBBBBDo',
    'oLBrRrBBBBBBDo',
    'oLBBRBBBBBBBDo',
    'oLBBRBBBBBBBDo',
    'ooooooBBBBBBDo',
    'oLLBBoBBBBBDDo',
    'oLBBSoBBBBBDDo',
    '.oSSoBBBBBDDDo',
    '..ooSSSSSDDDo.',
    '....oooooooo..',
]
PACK_RARE = [  # синий брезент со стальными уголками; сбоку висит фонарь (навеска)
    '..............',
    '...ooooooooo..',
    '..oDDDDDDDDDo.',
    '.ofFLLLLLLLFfo',
    '.oSSSSSSSSSSSo',
    'oLLBBRBBBBBBDo',
    'oLBBBRBBBBBBDo',
    'oLBBrRrBBBBBDo',
    'oLBBBRBBBBBBDo',
    'oLBBBRBBBBBBDo',
    'oLBBBBBBBBBBDo',
    'oBBBBBBBBBBDDo',
    '.oFBBBBBBBDDFo',
    '.ofFSSSSSDDFfo',
    '..oooooooooo..',
]
PACK_EPIC = [  # фиолетовый рюкзак: золотая окантовка, бирюзовая застёжка; из бока растут кристаллы (навеска)
    '..............',
    '...ooooooooo..',
    '..oDDDDDDDDDo.',
    '.oYYYYYYYYYYYo',
    '.oLSSSSSSSSSSo',
    'oLLBBBRBBBBBDo',
    'oLBBBBRBBBBBDo',
    'oLBBBrJrBBBBDo',
    'oLBBBBRBBBBBDo',
    'oLBBBBRBBBBBDo',
    'oLBBBBBBBBBBDo',
    'oBBBBBBBBBBDDo',
    '.oBBBBBBBBDDDo',
    '.oYYYYYYYYYYYo',
    '..oDDDDDDDDDo.',
    '...ooooooooo..',
]
PACK_LEGENDARY = [  # королевский рюкзак (шире): красная кожа, золотой обруч горловины с рубином, золотая лапка, уголки
    '...............',
    '...oooooooooo..',
    '..oDDDDDDDDDDo.',
    '.oYHYYYYJYYYYYo',
    '.oYSSSSSSSSSSYo',
    'oLLBBBBBBBBBBDo',
    'oLBBYBYBBBBBBDo',
    'oLBYBYBYBBBBBDo',
    'oLBBYYYBBBBBBDo',
    'oLBBYYYBBBBBBDo',
    'oLBBBBBBBBBBBDo',
    'oBBBBBBBBBBBDDo',
    'oYBBBBBBBBBBDYo',
    '.oYSSSSSSSSDYo.',
    '..oYYYYYYYYYo..',
    '...ooooooooo...',
]
# Навеска сбоку рюкзака (рисуется поверх него): фонарь у rare, друза кристаллов у epic. (рисунок, столбец, строка)
LANTERN = [
    '.o..',
    'oSSo',
    'oWgo',
    'oggo',
    'oAAo',
    'oSSo',
    '.oo.',
]
CRYSTALS = [
    '.q....',
    'qWq.q.',
    'qJjqWq',
    'qJJqJq',
    '.qJJq.',
    '..qq..',
]
_SACKCLOTH = {'o': 'wood1', 'm': 'wood4', 'x': 'cream0', 'y': 'cream1', 'z': 'cream2'}
# рисунок, сдвиг по x, цвета, навеска, лямки (тёмная, светлая, пряжка)
PACKS = {
    'base': (PACK_BASE, 0, _SACKCLOTH, None, ('wood2', 'wood4', None)),
    'common': (PACK_COMMON, 0, {'o': 'wood0', 'D': 'wood1', 'S': 'wood2', 'B': 'wood3', 'L': 'wood4', 'H': 'wood5',
                                'R': 'wood1', 'r': 'gold4'}, None, ('wood1', 'wood4', 'gold3')),
    'rare': (PACK_RARE, 0, {'o': 'navy0', 'D': 'blue1', 'S': 'blue2', 'B': 'blue3', 'L': 'blue4', 'H': 'blue5',
                            'R': 'navy2', 'r': 'stone7', 'f': 'stone4', 'F': 'stone6'},
             (LANTERN, -1, 5, {'o': 'navy0', 'S': 'stone6', 'W': 'amber5', 'g': 'amber4', 'A': 'amber2'}),
             ('stone3', 'stone6', 'cream4')),
    'epic': (PACK_EPIC, 0, {'o': 'purple0', 'D': 'purple1', 'S': 'purple2', 'B': 'purple3', 'L': 'purple4',
                            'H': 'purple5', 'Y': 'gold3', 'R': 'purple1', 'r': 'gold4', 'J': 'teal5'},
             (CRYSTALS, -2, 6, {'q': 'teal1', 'J': 'teal4', 'j': 'teal5', 'W': 'teal6'}),
             ('purple1', 'purple4', 'teal5')),
    'legendary': (PACK_LEGENDARY, -1, {'o': 'red0', 'D': 'red1', 'S': 'red2', 'B': 'red3', 'L': 'red4', 'H': 'gold5',
                                       'Y': 'gold4', 'J': 'red5'}, None, ('gold1', 'gold4', 'red5')),
}
for _r, (_rows, *_x) in PACKS.items():
    _check_rows(f'pack {_r}', _rows)
_check_rows('lantern', LANTERN)
_check_rows('crystals', CRYSTALS)
# Лямки на груди: столбцы туловища (ближняя — правое плечо котика, дальняя — левое) и строки сверху вниз.
STRAP_COLS = ((3, 4), (9, 10))
STRAP_ROWS = (1, 2, 3)


def pack_layer(p: Pose, rarity: str, body: Mask, parts: Parts) -> Canvas:
    rows, dx, legend, extra, (s_dark, s_light, buckle) = PACKS[rarity]
    tx, ty = p.torso
    x0, y0 = tx + PACK_AT[0] + dx, ty + PACK_AT[1]
    cv = Canvas(CW, CH)
    stamp_pal(cv, rows, legend, x0, y0)
    if extra:
        e_rows, ex, ey, e_legend = extra
        stamp_pal(cv, e_rows, e_legend, x0 + ex, y0 + ey)
    _cut(cv, body)
    # лямки поверх комбинезона (под ручками и лапками): светлая кромка слева, тёмная справа
    for c0, c1 in STRAP_COLS:
        for r in STRAP_ROWS:
            cv.set(tx + c0, ty + r, s_light)
            cv.set(tx + c1, ty + r, s_dark)
    if buckle:
        cv.set(tx + STRAP_COLS[0][0], ty + STRAP_ROWS[1], buckle)
        cv.set(tx + STRAP_COLS[1][0], ty + STRAP_ROWS[1], buckle)
    return _cut(cv, parts.arms)


# ── груз: мешочки-порции торчат из горловины рюкзака (один рисунок для всех редкостей рюкзака) ──
# Тот же мешочек, что лежит на поле (loot.cargo_sack_*) и в лапках (cat_sack_*): мешковина cream1–2, контур ink1,
# в горловине золото gold3–5, верёвка wood4. Уровни различаются числом мешочков и высотой над горловиной:
# 1 — один мешочек на 5 строк, 2 — два на 8, 3 — три на 10 и монетки, сползающие по боку рюкзака (перегруз).
# Мешочки стоят на ободе горловины (PACK_LIP): обод рюкзака лежит перед ними. Между грузом и любым головным убором
# кадра — минимум 1 px прозрачного или контура (load_layer вырезает зазор), чтобы груз не сливался с полем каски.
LOAD_SACK = [
    '.ooooo.',
    'oGWgGdo',
    '.ozyyo.',
    '.omnmo.',
    'oyzzyxo',
    'ozyyyxo',
    'oyyyyxo',
    'oyyyxxo',
    'oyyyxxo',
    'oyyxxxo',
]
# Мешочки по порядку рисования (задний первым): (столбец от начала рюкзака + 1, строк над горловиной).
# Мешочки сдвинуты к левому краю рюкзака: правее (x ≥ 9 холста) начинаются ухо и поле убора.
LOADS = {
    1: [(-1, 5)],
    2: [(0, 8), (-2, 5)],
    3: [(1, 10), (-2, 8), (-1, 5)],
}
# Перегруз: монетки на боку рюкзака под горловиной (столбец, строка от горловины).
LOAD_SPILL = [(0, 2), (1, 5)]
_LOAD = {'o': 'ink1', 'x': 'cream0', 'y': 'cream1', 'z': 'cream2', 'm': 'wood4', 'n': 'wood2',
         'G': 'gold4', 'g': 'gold3', 'd': 'gold2', 'W': 'gold5'}
_COINS = {'o': 'gold0', 'G': 'gold3', 'L': 'gold4', 'H': 'gold5', 'y': 'cream1', 'z': 'cream2', 'm': 'wood4'}
_check_rows('load sack', LOAD_SACK)


def _dilate(m: Mask) -> Mask:
    return {(x + dx, y + dy) for x, y in m for dx in (-1, 0, 1) for dy in (-1, 0, 1)}


def load_layer(p: Pose, n: int, body: Mask, parts: Parts, hats: Sequence[Canvas] = ()) -> Canvas:
    """Груз в рюкзаке: n мешочков над горловиной; вырезан по телу (голова, ручки, хвост поверх) и с зазором до уборов."""
    tx, ty = p.torso
    cv = Canvas(CW, CH)
    x0 = tx + PACK_AT[0] + 1
    lip = ty + PACK_AT[1] + PACK_LIP
    for col, k in LOADS[n]:
        sack = Canvas(CW, CH)
        stamp_pal(sack, LOAD_SACK[:k], _LOAD, x0 + col, lip - k)
        cv.blit(sack, 0, 0)
    if n == 3:
        for col, row in LOAD_SPILL:
            coin = Canvas(CW, CH)
            stamp_pal(coin, ['.oo.', 'oWGo', 'ogdo', '.oo.'], _LOAD, x0 + col - 1, lip + row)
            cv.blit(coin, 0, 0)
    _cut(cv, body)
    # Зазор до головного убора: пиксели груза вплотную к любому убору кадра стираются, край получает контур.
    gap: Mask = set()
    for h in hats:
        gap |= _dilate(mask_of(h))
    rim = {(x + dx, y + dy) for x, y in gap for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))} - gap
    _cut(cv, gap)
    for x, y in rim:
        if cv.opaque(x, y):
            cv.set(x, y, 'ink1')
    return _cut(cv, parts.arms)


# ── обувь: по форме лапок ног (силуэт тела не меняется) + манжета на строку выше ──
# Цвета: контур, подошва, тень, основа, свет, манжета, деталь манжеты/носка.
BOOTS = {
    # base — босые лапки: только тканевая обмотка-«носок» у щиколотки
    'base': None,
    'common': dict(o='wood0', sole='wood1', D='wood2', B='wood3', L='wood5', cuff='wood2', cuffl='wood4', toe=None),
    'rare': dict(o='navy0', sole='ink2', D='blue2', B='blue3', L='blue5', cuff='stone5', cuffl='stone7', toe=('stone6', 'cream4')),
    'epic': dict(o='purple0', sole='purple1', D='purple2', B='purple3', L='purple5', cuff='purple2', cuffl='teal5', toe=None),
    'legendary': dict(o='gold0', sole='gold1', D='gold2', B='gold3', L='gold5', cuff='cream2', cuffl='cream4', toe=('red3', 'red5')),
}


def boots_layer(p: Pose, rarity: str, parts: Parts) -> Canvas:
    cv = Canvas(CW, CH)
    spec = BOOTS[rarity]
    for rows, x0, y0 in feet_stamps(p):
        w, h = len(rows[0]), len(rows)
        if spec is None:
            # обмотка: верхняя строка лапки — бежевая ткань с тёмным краем
            for i, ch in enumerate(rows[0]):
                if ch in 'wv':
                    cv.set(x0 + i, y0, 'cream1' if i < w - 2 else 'cream0')
            continue
        # манжета: строка над лапкой во всю ширину лапки
        for i in range(w):
            c = spec['o'] if i in (0, w - 1) else (spec['cuffl'] if i == 1 else spec['cuff'])
            cv.set(x0 + i, y0 - 1, c)
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch in '. ':
                    continue
                if ch == 'o':
                    c = spec['sole'] if j == h - 1 else spec['o']
                elif j == h - 2:
                    c = spec['D']
                elif i == 1 and j == 0:
                    c = spec['L']
                else:
                    c = spec['B']
                cv.set(x0 + i, y0 + j, c)
        if spec['toe']:
            # носок справа (по взгляду): стальной/рубиновый колпачок с бликом
            dark, light = spec['toe']
            cv.set(x0 + w - 2, y0 + h - 2, dark)
            cv.set(x0 + w - 3, y0 + h - 2, dark)
            cv.set(x0 + w - 2, y0 + h - 3, light)
    return _cut(cv, parts.arms)


# ── кирка команды: прежняя оснастка pick_master, головка и рукоять по редкости ──
PICKS = {
    # цвета ролей, масштаб головки, толщина головки, обойма под головкой (None — без неё), лезвие тесла (0 — нет),
    # заточенная кромка, обмотка рукояти (цвет или None)
    'base': (PICK_IRON, 1.0, 2.8, None, 0.0, False, None),
    # кованое кайло-тесло: светлая стальная головка, сзади острый клюв, спереди широкое плоское лезвие со светлой
    # заточкой (cream4); ясеневая рукоять, медная обойма и светлая обмотка — заметно лучше простой кирки base
    'common': ({'wo': 'wood1', 'w2': 'wood3', 'po': 'stone1', 'p1': 'stone4', 'p2': 'stone6', 'p3': 'stone7', 'p4': 'cream4'},
               1.08, 3.0, 'fur_ginger2', 4.6, True, 'wood5'),
    # синяя сталь, стальная обойма
    'rare': ({'wo': 'wood0', 'w2': 'wood3', 'po': 'blue0', 'p1': 'blue3', 'p2': 'blue4', 'p3': 'blue6', 'p4': 'blue7'},
             1.08, 3.0, 'stone6', 0.0, False, None),
    # фиолетовый кристалл на тёмной рукояти, золотая обойма
    'epic': ({'wo': 'ink2', 'w2': 'navy4', 'po': 'purple0', 'p1': 'purple3', 'p2': 'purple4', 'p3': 'purple5', 'p4': 'purple6'},
             1.12, 3.2, 'gold3', 0.0, False, None),
    # золотая головка крупнее со светлой (cream4) кромкой — отделяется от золотой короны, когда кирка над головой;
    # красная рукоять, светлая золотая обойма
    'legendary': ({'wo': 'red1', 'w2': 'red3', 'po': 'gold0', 'p1': 'gold2', 'p2': 'gold3', 'p3': 'cream4', 'p4': 'white'},
                  1.2, 3.3, 'gold5', 0.0, False, None),
}


def pick_layer(p: Pose, rarity: str, front: Mask, behind: Mask) -> Canvas:
    """Кирка команды: pick_master с цветами и размером редкости; обойма — 2 px рукояти сразу под головкой,
    обмотка — 2 px рукояти через пиксель ниже обоймы."""
    mapping, scale, thick, collar, blade, edge, wrap = PICKS[rarity]
    cv = paint(pick_master(p, front, behind, scale, thick, blade, edge), mapping)
    if collar or wrap:
        (bx, by), (tx, ty) = p.pick
        line = Canvas(CW, CH)
        line.line(bx, by, tx, ty, 'white')
        pts = sorted(mask_of(line), key=lambda q: (q[0] - tx) ** 2 + (q[1] - ty) ** 2)
        handle = C(mapping['w2'])
        seq = [q for q in pts if cv.get(*q) == handle]
        for i, q in enumerate(seq[:2]):
            if collar:
                cv.set(q[0], q[1], collar)
        if wrap:
            for q in seq[3:5]:
                cv.set(q[0], q[1], wrap)
    return cv


# ── мешочек-порция в лапках (подбор и разгрузка) ──
SACK = [
    '...oo...',
    '..oHLo..',
    '...mm...',
    '.ozzyo..',
    'ozyyyyo.',
    'ozyyyyxo',
    'oyyyyxxo',
    '.oxxxxo.',
    '..oooo..',
]
_check_rows('sack', SACK)
# Верхний левый угол мешочка по кадрам (лапки держат горловину или бока).
# Разгрузка: в unload1 мешочек висит перед грудкой (x +0…+7 от ног), в unload2 опущен к ногам: низ мешочка на 4 px
# ниже линии ног (строка 39 холста) — котик роняет его вниз, к жёлобу под местом разгрузки, а не вбок.
SACK_AT = {'pickup1': (26, 27), 'pickup2': (1, 7), 'unload0': (1, 7), 'unload1': (19, 25), 'unload2': (20, 31)}


def sack_layer(fr: str, parts: Parts) -> Canvas:
    cv = Canvas(CW, CH)
    stamp_pal(cv, SACK, {**_COINS, 'x': 'cream0', 'o': 'wood1'}, *SACK_AT[fr])
    return _cut(cv, parts.paws)


# ───────────────────────── кольцо, бейджи, цифры ─────────────────────────
def select_ring() -> Canvas:
    """Золотое кольцо под выбранным котиком: эллипс на полу, передняя дуга толще (вид сверху под углом)."""
    w, h = 28, 12
    cv = Canvas(w, h)
    cx, cy = w / 2, h / 2
    for y in range(h):
        for x in range(w):
            dx, dy = (x + 0.5 - cx), (y + 0.5 - cy)
            outer = (dx / 13.6) ** 2 + (dy / 5.6) ** 2 <= 1
            inner = (dx / 11.4) ** 2 + ((dy + 0.6) / 3.6) ** 2 <= 1
            if outer and not inner:
                # верх дуги светлее (свет сверху-слева), низ — передняя стенка кольца темнее
                c = 'gold4' if dy < 0 else 'gold3'
                if dy < 0 and dx < -4:
                    c = 'gold5'
                if dy >= 3.5:
                    c = 'gold2'
                cv.set(x, y, c)
    return cv


BADGE = [
    '...ooooooo...',
    '..oLLLLLLLo..',
    '.oLfffffffDo.',
    'oLfffffffffDo',
    'oLfffffffffDo',
    'oLfffffffffDo',
    'oLfffffffffDo',
    'oLfffffffffDo',
    'oLfffffffffDo',
    'oLfffffffffDo',
    '.oLfffffffDo.',
    '..oDDDDDDDo..',
    '...ooooooo...',
]


def badge(rows: Sequence[str], rim: Tuple[str, str]) -> Canvas:
    return Canvas.from_ascii(rows, {'o': 'ink1', 'L': rim[0], 'D': rim[1], 'f': 'navy1'})


def badge_wide(extra: int) -> List[str]:
    """Бейдж для двузначного уровня: средняя колонка повторяется."""
    return [r[:6] + r[6] * (1 + extra) + r[7:] for r in BADGE]


DIGITS = {
    '0': ['.###.', '##.##', '##.##', '##.##', '##.##', '##.##', '.###.'],
    '1': ['..##.', '.###.', '..##.', '..##.', '..##.', '..##.', '.####'],
    '2': ['.###.', '##.##', '...##', '..##.', '.##..', '##...', '#####'],
    '3': ['####.', '...##', '...##', '.###.', '...##', '...##', '####.'],
    '4': ['...##', '..###', '.#.##', '#..##', '#####', '...##', '...##'],
    '5': ['#####', '##...', '####.', '...##', '...##', '##.##', '.###.'],
    '6': ['.###.', '##...', '##...', '####.', '##.##', '##.##', '.###.'],
    '7': ['#####', '...##', '..##.', '..##.', '.##..', '.##..', '.##..'],
    '8': ['.###.', '##.##', '##.##', '.###.', '##.##', '##.##', '.###.'],
    '9': ['.###.', '##.##', '##.##', '.####', '...##', '...##', '.###.'],
}


def build_equipment(reg, fr: str, p: Pose, b: Canvas, parts: Parts) -> None:
    """Слои экипировки команды для кадра нового режима (manifest.EQUIP_FRAMES)."""
    body = mask_of(b)
    hats = {r: hat_layer(p, r, parts) for r in EQUIP_RARITIES}
    for r in EQUIP_RARITIES:
        reg.add(f'cat_boots_{r}_{fr}', boots_layer(p, r, parts), pivot=PIVOT)
        reg.add(f'cat_pack_{r}_{fr}', pack_layer(p, r, body, parts), pivot=PIVOT)
        reg.add(f'cat_hat_{r}_{fr}', hats[r], pivot=PIVOT)
    for n in LOAD_LEVELS:
        reg.add(f'cat_load_{n}_{fr}', load_layer(p, n, body, parts, list(hats.values())), pivot=PIVOT)
    if fr in PICK_FRAMES:
        # кирка за головой вырезается по голове и по самому крупному силуэту всех уборов (слой кирки один на любой убор)
        behind = set()
        if p.pick_behind_head:
            behind = mask_of(head_layer(p))
            for h in hats.values():
                behind |= mask_of(h)
            behind -= parts.paws
        for r in EQUIP_RARITIES:
            reg.add(f'cat_pick_{r}_{fr}', pick_layer(p, r, parts.paws, behind), pivot=PIVOT)
    if fr in SACK_FRAMES:
        reg.add(f'cat_sack_{fr}', sack_layer(fr, parts), pivot=PIVOT)


def build(reg) -> None:
    for fr in CAT_FRAMES:
        p = POSES[fr]
        parts = Parts()
        b = body_master(p, parts)
        for fur in FURS:
            reg.add(f'cat_body_{fur}_{fr}', paint(marking(b, fur, p, parts), fur_map(fur)), pivot=PIVOT)
        # Mia: пятна как у белого окраса, но оба рыжие, сиреневый комбинезон и розовый платок (mia_map).
        reg.add(f'cat_body_{MIA_FUR}_{fr}', paint(marking(b, 'white', p, parts), mia_map()), pivot=PIVOT)
        if fr in LEGACY_FRAMES:
            h = helmet_master(p)
            for colour in HELMET_COLORS:
                reg.add(f'cat_helmet_{colour}_{fr}', paint(h, helmet_map(colour)), pivot=PIVOT)
            behind = (mask_of(head_layer(p)) | mask_of(h)) - parts.paws if p.pick_behind_head else set()
            cut = parts.paws | behind
            phase = CAT_FRAMES.index(fr)

            def pick_fn(butt, top, p=p):
                q = Pose.__new__(Pose)
                q.__dict__.update(p.__dict__)
                q.pick = (butt, top)
                return paint(pick_master(q, set(), set()), PICK_IRON)
            for w in WEAPONS:
                if fr not in weapon_frames(w):
                    continue
                k = weapons.draw(w, p.pick[0], p.pick[1], fr, phase, pick_fn)
                for x, y in cut:
                    k.set(x, y, None)
                reg.add(f'cat_weapon_{w}_{fr}', k, pivot=PIVOT)
        if fr in EQUIP_FRAMES:
            build_equipment(reg, fr, p, b, parts)
    reg.add('select_ring', select_ring())
    reg.add('badge_level', badge(BADGE, ('cream3', 'cream1')))
    reg.add('badge_level_hi', badge(BADGE, ('gold4', 'gold2')))
    # двузначный уровень: цифры ставятся по центру бейджа со сдвигом ±3 px
    reg.add('badge_level_wide', badge(badge_wide(6), ('cream3', 'cream1')))
    reg.add('badge_level_wide_hi', badge(badge_wide(6), ('gold4', 'gold2')))
    for d, rows in DIGITS.items():
        reg.add(f'digit_{d}', Canvas.from_ascii(rows, {'#': 'cream3'}))
