"""Иконки товаров заводов (ТЗ 2.2 §3.3, §11, приложение A): goods_<good> 16×16 и goods_<good>_8 8×8, pivot — центр.

Семейство как у sprites/icons.py: заливка ASCII-картой, затем один общий контур 1 px ink1 (4-соседство — углы мягкие).
Свет сверху-слева: блик на верхней/левой кромке, тень — на нижней/правой; 3–4 тона на материал. Основной тон каждой
иконки совпадает с цветом полосы трубы товара (Color в Assets/Game/Resources/Outpost/factory_rules.json), но цвет не
единственный признак: силуэты разные (бутылка, брёвна, рыба, клубок …). Маленькая иконка 8×8 — тот же предмет
упрощённым силуэтом: она лежит на заглушке трубы fpipe_cap и во всплывающем «+N» у склада.

API для других модулей: GOODS (порядок номеров типов 1…20), goods_icon(good, size) → Canvas, GOOD_COLOR[good] — тон
полосы из конфига.
"""
from __future__ import annotations

import json
import os
from typing import Dict, List, Sequence, Tuple

from pxl import Canvas, PALETTE

from manifest import GOODS

OUTLINE = 'ink1'
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(os.path.dirname(HERE))
RULES_PATH = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Outpost', 'factory_rules.json')


def _load_colors() -> Dict[str, str]:
    """Цвет полосы трубы по товару из factory_rules.json (тот же файл читает ядро). Нет файла — пустой словарь."""
    if not os.path.exists(RULES_PATH):
        return {}
    with open(RULES_PATH, encoding='utf-8') as f:
        data = json.load(f)
    return {g['GoodId']: g['Color'] for g in data.get('Goods', [])}


GOOD_COLOR: Dict[str, str] = _load_colors()


def check_rules() -> List[str]:
    """Сверка арта с конфигом: тот же список товаров в том же порядке, цвет полосы — тон палитры pxl."""
    errors: List[str] = []
    if not os.path.exists(RULES_PATH):
        return [f'нет {os.path.relpath(RULES_PATH, ROOT)}']
    with open(RULES_PATH, encoding='utf-8') as f:
        ids = [g['GoodId'] for g in json.load(f).get('Goods', [])]
    if ids != GOODS:
        errors.append(f'товары factory_rules.json {ids} не совпадают с manifest.GOODS {GOODS}')
    for g, c in GOOD_COLOR.items():
        if c not in PALETTE:
            errors.append(f'цвет полосы {g}: {c!r} нет в палитре pxl')
    return errors


def _art(rows: Sequence[str], legend: Dict[str, str]) -> Canvas:
    return Canvas.from_ascii(rows, legend)


def _icon(art: Canvas, size: int) -> Canvas:
    """Арт по центру квадратного холста (нечётный остаток — вправо/вниз) и общий контур ink1."""
    cv = Canvas(size, size)
    cv.blit(art, (size - art.w) // 2, (size - art.h) // 2)
    cv.outline(OUTLINE)
    return cv


# ───────────────────────── легенды материалов ─────────────────────────
# Белое стекло и молоко (холодный белый fur_white), синяя крышка.
MILK = {'O': 'fur_white4', 'W': 'fur_white3', 's': 'fur_white2', 'S': 'fur_white1',
        'B': 'blue4', 'b': 'blue5', 'c': 'blue3', 'a': 'blue2'}
LOG = {'b': 'wood3', 'B': 'wood1', 'F': 'wood6', 'f': 'wood5', 'c': 'wood4', 'k': 'wood2', 'L': 'wood7'}
FISH = {'a': 'navy8', 'A': 'navy7', 'm': 'navy6', 's': 'navy5', 'd': 'navy4', 'k': 'ink1', 'w': 'cream3', 'p': 'pink2'}
YARN = {'H': 'pink4', 'L': 'pink3', 'P': 'pink2', 'p': 'pink1', 'D': 'pink0', 'n': 'stone6', 'N': 'stone4'}
FOOD = {'H': 'amber4', 'L': 'amber3', 'A': 'amber2', 'a': 'amber1', 'D': 'amber0', 'w': 'cream3', 'v': 'cream2',
        'p': 'wood1', 'r': 'red3'}
BOX = {'H': 'fur_cream4', 'L': 'fur_cream3', 'C': 'fur_cream2', 'c': 'fur_cream1', 'D': 'fur_cream0', 't': 'wood5',
       'T': 'wood4'}
BRICK = {'H': 'red5', 'L': 'red4', 'R': 'red3', 'r': 'red2', 'D': 'red1', 'm': 'stone5', 'M': 'stone3'}
STEEL = {'W': 'stone7', 'H': 'stone6', 'S': 'stone5', 's': 'stone4', 'd': 'stone3', 'D': 'stone2', 'g': 'cream4'}
GLASS = {'W': 'teal6', 'H': 'teal5', 'T': 'teal4', 't': 'teal3', 'D': 'teal2', 'g': 'white'}
NIP = {'H': 'green6', 'L': 'green5', 'G': 'green4', 'g': 'green3', 'D': 'green2', 'f': 'purple6', 'F': 'purple5',
       's': 'wood3'}
TOY = {'H': 'purple6', 'L': 'purple5', 'P': 'purple4', 'p': 'purple3', 'D': 'purple2', 'e': 'pink3', 'k': 'ink1',
       'n': 'pink2', 'w': 'cream3', 't': 'pink1'}
POST = {'H': 'copper8', 'C': 'copper7', 'c': 'copper6', 'd': 'copper5', 'T': 'fur_cream3', 't': 'fur_cream2',
        'u': 'fur_cream1', 'B': 'wood4', 'b': 'wood3', 'k': 'wood2', 'p': 'red4', 'P': 'red5'}
PILLOW = {'H': 'pink4', 'L': 'pink3', 'P': 'pink2', 'p': 'pink1', 'b': 'purple4', 'g': 'gold4', 'G': 'gold3'}
BELL = {'W': 'gold5', 'H': 'gold4', 'G': 'gold3', 'g': 'gold2', 'D': 'gold1', 'k': 'ink2', 'c': 'amber1'}
COLLAR = {'H': 'red5', 'L': 'red4', 'R': 'red3', 'r': 'red2', 'G': 'gold4', 'g': 'gold3', 'd': 'gold2', 'W': 'gold5',
          'k': 'ink2'}
GEAR = {'W': 'gold5', 'H': 'gold4', 'G': 'gold3', 'g': 'gold2', 'D': 'gold1', 'k': 'ink2', 's': 'stone5', 'S': 'stone3'}
LAMP = {'K': 'stone2', 'k': 'stone3', 's': 'stone4', 'S': 'stone5', 'Y': 'amber5', 'y': 'amber4', 'a': 'amber3',
        'A': 'amber2', 'w': 'cream4'}
HONEY = {'H': 'amber5', 'Y': 'amber4', 'A': 'amber3', 'a': 'amber2', 'D': 'amber1', 'g': 'fur_white3', 'G': 'fur_white2',
         'c': 'red3', 'C': 'red4', 'w': 'cream3', 'b': 'wood4'}
SILK = {'W': 'cream4', 'H': 'purple6', 'L': 'purple5', 'P': 'purple4', 'p': 'purple3', 'D': 'purple2', 'k': 'wood2',
        'K': 'wood4'}
CAVIAR = {'W': 'stone7', 'H': 'stone6', 'S': 'stone5', 's': 'stone4', 'd': 'stone3', 'R': 'red5', 'r': 'red4',
          'q': 'red3', 'h': 'red6', 'B': 'blue3', 'b': 'blue2', 'g': 'gold4'}


# ───────────────────────── 16×16 (арт до 14×14) ─────────────────────────
ART16: Dict[str, Tuple[List[str], Dict[str, str]]] = {
    # Бутылка молока: синяя крышка, белое стекло, синяя этикетка.
    'milk': ([
        '...bBBc...',
        '...ccca...',
        '...OWWs...',
        '..OWWWWs..',
        '.OWWWWWWs.',
        'OOWWWWWWss',
        'OWWWWWWWss',
        'bbbbbbbbBa',
        'BBBBBBBBca',
        'cccccccaaa',
        'OWWWWWWWss',
        'OWWWWWWWss',
        '.WWWWWWss.',
        '..ssssSS..',
    ], MILK),
    # Брёвна: три торца с годовыми кольцами, пирамидкой.
    'wood': ([
        '....bbbbb.....',
        '...bFFFFFb....',
        '..bFfffffFB...',
        '..bFfcccfFB...',
        '..bFfcLcfFB...',
        '..bFfcccfFB...',
        '.bbbFfffbbbbB.',
        'bFFFFbBbbFFFFB',
        'FfffffFBFfffFB',
        'FfcccfFBFcccFB',
        'FfcLcfFBFcLcFB',
        'FfcccfFBFcccFB',
        'BFfffFBkBFffFB',
        '.BBBBBk.kBBBB.',
    ], LOG),
    # Рыба (смотрит влево): светлая спина, белое брюшко, хвост-вилка.
    'fish': ([
        '..........mm..',
        '....aaaa..mAm.',
        '..aaAAAAa.mAm.',
        '.aAkAAAAAAmAm.',
        'aAAAAAmAAAAAs.',
        'pAAAAAmAAAAs..',
        '.wwwAAAAAAAAs.',
        '..wwwwAAAsmAm.',
        '....sssss.mAm.',
        '..........ms..',
    ], FISH),
    # Клубок пряжи: витки нити, свободный конец и спица.
    'yarn': ([
        '..........n.',
        '....LLLL.n..',
        '..LLHPLPLn..',
        '.LHPLLPPLN..',
        '.LPLLPLPPp..',
        'LHLLPLPPLPp.',
        'LPLPLPPLPPp.',
        'LLPLPPLPPpp.',
        'LPLPPLPPPpp.',
        '.LPPLPPPpp..',
        '.PPLPPPpppP.',
        '..ppPPppp.pP',
        '....pppp...p',
    ], YARN),
    # Мешок корма: защипнутый зубчатый верх, белая этикетка с рыжей лапкой.
    'catfood': ([
        '..LaLaLaLaL..',
        '..aDaDaDaDa..',
        '..LAAAAAAAa..',
        '.LAAAAAAAAAa.',
        '.LAwwwwwwvAa.',
        '.LAwwrwrwvAa.',
        '.LAwrwwwrvAa.',
        '.LAwwrrrwvAa.',
        '.LAwwrrrwvAa.',
        '.LAvvvvvvvAa.',
        'LAAAAAAAAAAAa',
        'LAAAAAAAAAAaa',
        '.aaaaaaaaaaD.',
    ], FOOD),
    # Картонная коробка: светлая крышка, лицевая сторона, скотч посередине.
    'boxes': ([
        '...HHHHtHHHH..',
        '..HLLLLtLLLLc.',
        '.HLLLLLtLLLLcc',
        'CCCCCCCTCCCCcc',
        'CHCCCCCTCCCCcc',
        'CCCCCCCTCCCCcc',
        'CCCCCCCTCCCCcc',
        'CCCCCCCTCCCCcc',
        'CCCCCCCTCCCCc.',
        'CCCCCCCTCCCCc.',
        'cccccccccccc..',
    ], BOX),
    # Кирпичи: кладка из трёх рядов со швами.
    'bricks': ([
        '.....HHHHHH...',
        '....HLLLLLLr..',
        '....RRRRRRRr..',
        '....RRRRRRRrD.',
        '.HHHHHHmHHHHHr',
        'HLLLLLLmLLLLLr',
        'RRRRRRRMRRRRRr',
        'RRRRRRRMRRRRRr',
        'rrrrrrrMrrrrrD',
        'HHHmHHHHHHmHHr',
        'LLLmLLLLLLmLLr',
        'RRRMRRRRRRMRRr',
        'rrrMrrrrrrMrrD',
    ], BRICK),
    # Стальные слитки пирамидкой.
    'steel': ([
        '....WWWWWW....',
        '...WHHHHHHS...',
        '...HSSSSSSs...',
        '..HSSSSSSSSd..',
        '..ddddddddddD.',
        '.WWWWWW.WWWWWW',
        'WHHHHHHSWHHHHH',
        'HSSSSSSsHSSSSs',
        'HSgSSSSsHSgSSs',
        'HSSSSSSSdSSSSd',
        'dddddddDdddddD',
    ], STEEL),
    # Лист стекла: светлая рамка, бирюзовое поле, два диагональных блика.
    'glass': ([
        '.WWWWWWWWWWW.',
        'WHHHHHHHHHHTD',
        'WHggHHHHHHHTD',
        'WHgHHHHHHgHTD',
        'WHHHHHHHgHHTD',
        'WHHHHHHgHHHTD',
        'WHHHHHgHHHHTD',
        'WHHHHgHHHHHTD',
        'WHHHgHHHHHHTD',
        'WHHHHHHHHgHTD',
        'WHHHHHHHgHHTD',
        'WTTTTTTTTTTtD',
        '.DDDDDDDDDDD.',
    ], GLASS),
    # Веточка кошачьей мяты: стебель, листья парами, сиреневые цветки на верхушке.
    'catnip': ([
        '......fF......',
        '.....fFfF.....',
        '......Fg......',
        '..HL..g..LH...',
        '.HLGG.g.GGLH..',
        '.LGGg.g.gGGL..',
        '..gg..g..gg...',
        '.....Hg.HL....',
        '...HLLg.LLLH..',
        '..HLGGg.gGGL..',
        '..LGGgg.ggGG..',
        '...gg.g...gg..',
        '......g.......',
        '......s.......',
    ], NIP),
    # Игрушечная мышка на верёвочке: фиолетовое тельце, розовое ушко, хвост-шнурок.
    'toys': ([
        '...ee.........',
        '..eeeL........',
        '..enLLLL......',
        '..LLLLLLLP....',
        '.LLkLLLLPPP...',
        'nLLLLLLLPPPp..',
        '.PPLLLLPPPPp..',
        '..ppPPPPPPpp.t',
        '....pppppp..t.',
        '..........tt..',
        '.......ttt....',
    ], TOY),
    # Когтеточка: ковровая полка, столб в канате (диагональные витки), основание; с полки свисает помпон.
    'scratchers': ([
        '..TTTTTTTT...',
        '.TttttttttT..',
        '.uuuuuuuuuu..',
        '...HCcCd..k..',
        '...HcCCd..k..',
        '...HCCcd..k..',
        '...HCcCd.ppp.',
        '...HcCCd.pPp.',
        '...HCCcd..p..',
        '...HCcCd.....',
        '...HcCCd.....',
        '.BBBBBBBBBB..',
        'BbbbbbbbbbbB.',
        '.kkkkkkkkkk..',
    ], POST),
    # Подушка: пухлые края, пуговица в центре, кисточки по углам.
    'cushions': ([
        'gG.........gG.',
        '.HHLLLLLLLLLp.',
        '.HLLLLLLLLLPp.',
        '.LLLLLLLLLLPp.',
        '..LLLLLLLLPp..',
        '..LLLLbLLLPp..',
        '..LLLLLLLLPp..',
        '.LLLLLLLLLPPp.',
        '.LLLLLLLLLPPp.',
        '.PPPPPPPPPPPp.',
        'gGppppppppppgG',
    ], PILLOW),
    # Колокольчик: ушко сверху, купол с бликом, юбка, язычок.
    'bells': ([
        '.....gg.....',
        '....g..g....',
        '....HHHG....',
        '...HWHGGg...',
        '..HWHGGGGg..',
        '..HWGGGGGg..',
        '..HHGGGGGg..',
        '.HHGGGGGGGg.',
        '.HGGGGGGGGg.',
        'HHGGGGGGGGgD',
        'GGGGGGGGGGgD',
        '.ggggggggggD',
        '.....DD.....',
    ], BELL),
    # Ошейник: красный ремешок кольцом (в перспективе), золотая пряжка и круглый жетон.
    'collars': ([
        '...LLLLLLLL...',
        '.LLRRRRRRRRRr.',
        'LRRr......rRRr',
        'LRr........rRr',
        'LRr........rRr',
        'LRRr......rRRr',
        '.HLLLGGGLLLLr.',
        '..rrrGkGrrrr..',
        '.....GGG......',
        '.....HWG......',
        '....HWGGg.....',
        '....HGGGd.....',
        '.....ggd......',
    ], COLLAR),
    # Фонарь: кольцо, железный колпак, тёплое стекло в рамке, поддон.
    'lanterns': ([
        '....kSSk....',
        '....k..k....',
        '...KsSSsK...',
        '..KsSSSSsK..',
        '..KKKKKKKK..',
        '..KwYKyYKK..',
        '..KYyKyyKa..',
        '..KyyKyaKa..',
        '..KyaKaaKA..',
        '..KyaKaAKA..',
        '..KKKKKKKK..',
        '...KsssK....',
    ], LAMP),
    # Горшочек мёда: тканевая крышка в клетку на шнурке, янтарный мёд, капля по боку.
    'honey': ([
        '..CcCcCcCc..',
        '.cCcCcCcCcC.',
        '..bbbbbbbb..',
        '..gGGGGGGg..',
        '.gYYHYYYYAa.',
        'gYHYYYYYYAAa',
        'gYHYYYYYAAAa',
        'gYYYYYYYAAAa',
        'gYYYYYYAAAaa',
        '.AAAAAAAAAaD',
        '..aaaaaaaaD.',
        '..........a.',
    ], HONEY),
    # Банка икры: открытая, серебряный ободок, оранжевая икра, синяя крышка позади.
    'caviar': ([
        '...BBBBBBB....',
        '..BbbbbbbbB...',
        '.BbgggggggbB..',
        '.WWWWWWWWWWW..',
        'WHRhRRhRRhRRs.',
        'WRrRhRrRhRrRs.',
        'WhRRrRRhRRqRs.',
        'WRRhRRrRRhRRs.',
        '.ssssssssssd..',
        '.HSSSSSSSSsd..',
        '.HSSSSSSSSsd..',
        '..dddddddd....',
    ], CAVIAR),
}


# ───────────────────────── 8×8 (арт до 6×6) ─────────────────────────
ART8: Dict[str, Tuple[List[str], Dict[str, str]]] = {
    'milk': (['.bBc..', '.OWs..', 'OWWWs.', 'BBOBa.', 'OWWWs.', '.sss..'], MILK),
    'wood': (['.bbbb.', 'bFffFB', 'bfcLfB', 'bfccfB', 'bFffFB', '.BBBB.'], LOG),
    'fish': (['.aa..m', 'aAkAmA', 'AAAAAs', 'wwAAmA', '.ss..m'], FISH),
    'yarn': (['.LLL.n', 'LHPLn.', 'LPLPp.', 'LLPPp.', '.ppp.p'], YARN),
    'catfood': (['aAAAa.', 'LAAAAa', 'LwrwAa', 'LwrwAa', 'LAAAAa', '.aaaD.'], FOOD),
    'boxes': (['.HHtH.', 'HLLtLc', 'CCCTCc', 'CHCTCc', 'CCCTCc', 'cccccc'], BOX),
    'bricks': (['.HHHH.', '.RRRRr', 'HHmHHr', 'RRMRRr', 'rrMrrD'], BRICK),
    'steel': (['.WWWW.', '.HSSs.', 'WWW.WW', 'HSSsHS', 'dddDdd'], STEEL),
    'glass': (['WWWWWD', 'WgHHTD', 'WHHgTD', 'WHgHTD', 'WTTTtD', '.DDDD.'], GLASS),
    'catnip': (['..fF..', 'HL.LH.', 'LGgGL.', '.HgL..', 'LGgGL.', '..s...'], NIP),
    'toys': (['.e....', 'enLL..', 'LkLLP.', 'nLLPPp', '.ppp.t', '....t.'], TOY),
    'scratchers': (['TTTpT.', 'tttuu.', '.HCd..', '.CHd..', '.HCd..', 'BBBBk.'], POST),
    'cushions': (['g...g.', '.LLLp.', '.LbLp.', '.LLLp.', 'gpppg.'], PILLOW),
    'bells': (['..g...', '.HHG..', '.WGGg.', 'HGGGgD', 'ggggD.', '..D...'], BELL),
    'collars': (['.LLLL.', 'Rr..rR', 'Rr..rR', '.LGGL.', '..HG..', '..gd..'], COLLAR),
    'gears': (['.H.H..', 'HGGGg.', 'GGkGgS', '.GGgSk', 'g.gSS.', '...S..'], GEAR),
    'lanterns': (['.kSk..', 'KsSsK.', 'KYyKa.', 'KyaKA.', 'KKKKK.', '.KsK..'], LAMP),
    'honey': (['CcCcC.', '.bbbb.', 'gYHYAa', 'gYYYAa', '.AAAaD', '..aaD.'], HONEY),
    'silk': (['.HH...', 'HKkHHH', 'HkkHWL', '.HHLLp', '....Pp'], SILK),
    'caviar': (['.BBBB.', 'WWWWWW', 'WRhRrs', 'WhRqRs', '.HSSd.', '.dddd.'], CAVIAR),
}


def gear(cv: Canvas, cx: float, cy: float, r_out: float, r_in: float, r_hole: float, teeth: int, phase: float,
         tones: Sequence[str], hole: str = None) -> None:
    """Шестерня кодом: зубцы — сектора между r_in и r_out, обод до r_in, отверстие r_hole (hole — цвет или прозрачное).
    tones — (блик, свет, основа, тень, глубокая тень): свет сверху-слева по направлению от центра."""
    import math
    hi, lt, base, dk, deep = tones
    for y in range(int(cy - r_out) - 1, int(cy + r_out) + 2):
        for x in range(int(cx - r_out) - 1, int(cx + r_out) + 2):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            if d > r_out:
                continue
            a = math.atan2(dy, dx) + phase
            tooth = math.cos(teeth * a) > -0.05
            if d > r_in and not tooth:
                continue
            if d <= r_hole:
                if hole:
                    cv.set(x, y, hole)
                continue
            s = -(dx + dy) / max(d, 0.01)
            if d > r_in - 1.0 or d <= r_hole + 1.0:
                c = lt if s > 0.55 else (dk if s < -0.55 else base)
                if d <= r_hole + 1.0:
                    c = dk if s > 0.3 else (lt if s < -0.3 else base)
            else:
                c = hi if s > 0.9 and d > r_in - 2.2 else base
            cv.set(x, y, c)
    for y in range(cv.h):
        for x in range(cv.w):
            p = cv.get(x, y)
            if p[3] and p[:3] == PALETTE[base] and (cv.get(x + 1, y)[3] == 0 or cv.get(x, y + 1)[3] == 0):
                cv.set(x, y, deep)


def gears16() -> Canvas:
    """Шестерни: большая латунная (8 зубцов, отверстие под вал) и малая стальная за ней справа сверху."""
    art = Canvas(14, 14)
    gear(art, 10.5, 3.5, 3.6, 2.4, 0.9, 6, 0.3, ('stone7', 'stone6', 'stone5', 'stone3', 'stone2'), hole='ink2')
    big = Canvas(14, 14)
    gear(big, 6.0, 8.0, 6.0, 4.4, 1.5, 8, 0.2, ('gold5', 'gold4', 'gold3', 'gold2', 'gold1'), hole='ink2')
    big.outline(OUTLINE)
    art.blit(big, 0, 0)
    return art


def silk16() -> Canvas:
    """Рулон шёлка лёжа (вид 3/4 сверху): светлый верх с блеском, тень снизу, торец слева — витки на картонной
    втулке, золотая лента поперёк рулона."""
    import math
    art = Canvas(14, 11)
    x0, x1, cy, r = 3.0, 12.5, 5.0, 4.6
    for y in range(art.h):
        for x in range(art.w):
            t = (y + 0.5 - cy) / r
            if abs(t) > 1.0:
                continue
            xr = x1 + 1.3 * math.sqrt(max(0.0, 1 - t * t))
            if not (x0 <= x + 0.5 <= xr):
                continue
            c = 'purple6' if t < -0.55 else ('purple5' if t < 0.1 else ('purple4' if t < 0.6 else 'purple3'))
            art.set(x, y, c)
    # Блеск: короткие светлые штрихи по верху.
    for x, y in ((6, 2), (7, 2), (8, 3), (11, 2)):
        art.set(x, y, 'cream4')
    # Лента.
    for y in range(art.h):
        for x, c in ((9, 'gold4'), (10, 'gold2')):
            if art.opaque(x, y):
                art.set(x, y, c)
    art.set(9, 1, 'gold5')
    # Торец: эллипс с витками и втулкой.
    end = Canvas(14, 11)
    end.ellipse(3.0, cy, 2.6, r, 'purple5')
    for y in range(end.h):
        for x in range(end.w):
            if not end.opaque(x, y):
                continue
            dx, dy = (x + 0.5 - 3.0) / 2.6, (y + 0.5 - cy) / r
            d = math.hypot(dx, dy)
            end.set(x, y, 'purple6' if d > 0.78 else ('purple4' if d > 0.52 else ('purple5' if d > 0.3 else 'wood2')))
    art.blit(end, 0, 0)
    return art


def goods_icon(good: str, size: int = 16) -> Canvas:
    """Иконка товара: size 16 (UI) или 8 (заглушка трубы, «+N» на поле)."""
    if good == 'gears' and size == 16:
        return _icon(gears16(), size)
    if good == 'silk' and size == 16:
        return _icon(silk16(), size)
    rows, legend = (ART16 if size == 16 else ART8)[good]
    return _icon(_art(rows, legend), size)


def build(reg) -> None:
    errors = check_rules()
    if errors:
        raise ValueError('goods: ' + '; '.join(errors))
    for g in GOODS:
        reg.add(f'goods_{g}', goods_icon(g, 16), pivot=(8.0, 8.0), note=f'товар {g}, 16×16, полоса трубы {GOOD_COLOR.get(g)}')
        reg.add(f'goods_{g}_8', goods_icon(g, 8), pivot=(4.0, 4.0), note=f'товар {g}, 8×8')
