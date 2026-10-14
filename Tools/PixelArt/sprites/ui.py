"""UI-кит Cat Miners: 9-slice панели, кнопки, ячейки, шкалы, карточки, элементы обучения.

На экране 1 пиксель арта = 4 единицы Canvas (эталон 1080×1920). Образец — HUD концепта
(Docs/Art/Concepts/style-exploration-2026-10-06/04_pixel_art.png): тёмно-синие панели со светлой фаской
и тонким тёмным контуром, деревянная табличка с гвоздиками, золотая кнопка «Нанять» с бликом сверху и губой снизу.

Правило 9-slice: всё, что не повторяется, живёт внутри border; середина и полосы между углами однородны,
иначе при растяжении Image.Type=Sliced появятся «полосы». _check_slice проверяет это при сборке.
Слоистые рамки строятся «очисткой» формы по 4-соседству: каждый следующий слой — контур оставшейся формы,
срез угла у него на пиксель меньше, поэтому фаски выходят ровной лесенкой без ручной правки углов.
"""
from __future__ import annotations

from typing import Dict, List, Optional, Sequence, Set, Tuple, Union

from pxl import C, Canvas, Registry

Px = Tuple[int, int]
Spec = Union[None, str, Dict[str, str]]


# ───────────────────────── форма и слои ─────────────────────────
def _cuts(c: Union[int, Sequence[int]]) -> List[int]:
    """Профиль среза угла: сколько пикселей отрезать в строках 0, 1, … от угла. Число — фаска 45°."""
    if isinstance(c, int):
        return [c - i for i in range(c)]
    return list(c)


def shape(w: int, h: int, c: Union[int, Sequence[int]] = 0, x0: int = 0, y0: int = 0) -> Set[Px]:
    """Прямоугольник w×h со срезанными углами (одинаковый профиль во всех четырёх углах)."""
    prof = _cuts(c)
    out: Set[Px] = set()
    for y in range(h):
        for x in range(w):
            dy = min(y, h - 1 - y)
            dx = min(x, w - 1 - x)
            if dy < len(prof) and dx < prof[dy]:
                continue
            out.add((x0 + x, y0 + y))
    return out


def peel(mask: Set[Px]) -> List[Dict[Px, str]]:
    """Слои формы снаружи внутрь (контур по 4-соседству) с направлениями открытых сторон 'udlr'."""
    rem = set(mask)
    layers: List[Dict[Px, str]] = []
    while rem:
        layer: Dict[Px, str] = {}
        for (x, y) in rem:
            d = ''
            if (x, y - 1) not in rem:
                d += 'u'
            if (x, y + 1) not in rem:
                d += 'd'
            if (x - 1, y) not in rem:
                d += 'l'
            if (x + 1, y) not in rem:
                d += 'r'
            if d:
                layer[(x, y)] = d
        layers.append(layer)
        rem -= set(layer)
    return layers


def bev(t: str, l: Optional[str] = None, r: Optional[str] = None, b: Optional[str] = None, **corners: str) -> Dict[str, str]:
    """Цвета слоя по сторонам (свет сверху-слева). Углы tl/tr/bl/br — пиксели, открытые на две стороны."""
    spec = {'t': t, 'l': l or t, 'r': r or (b or t), 'b': b or (r or t)}
    spec.update(corners)
    return spec


def _pick(spec: Spec, d: str) -> Optional[str]:
    if spec is None or isinstance(spec, str):
        return spec
    u, dn, l, r = 'u' in d, 'd' in d, 'l' in d, 'r' in d
    for flag, key in ((u and l, 'tl'), (u and r, 'tr'), (dn and l, 'bl'), (dn and r, 'br')):
        if flag and key in spec:
            return spec[key]
    if u:
        return spec['t']
    if dn:
        return spec['b']
    if l:
        return spec['l']
    return spec['r']


def paint(cv: Canvas, mask: Set[Px], specs: Sequence[Spec], body: Optional[str]) -> None:
    """Залить форму слоями: specs[i] — цвет i-го слоя снаружи, глубже — body (None — оставить прозрачным)."""
    for i, layer in enumerate(peel(mask)):
        spec = specs[i] if i < len(specs) else body
        for (x, y), d in layer.items():
            col = _pick(spec, d)
            if col is not None:
                cv.set(x, y, col)


def plate(w: int, h: int, c, specs: Sequence[Spec], body: Optional[str]) -> Canvas:
    cv = Canvas(w, h)
    paint(cv, shape(w, h, c), specs, body)
    return cv


def dots(cv: Canvas, pts: Sequence[Px], col: str, mirror: bool = True) -> None:
    """Пиксели, заданные для левого верхнего угла; mirror — повторить зеркально во всех четырёх углах."""
    for x, y in pts:
        cv.set(x, y, col)
        if mirror:
            cv.set(cv.w - 1 - x, y, col)
            cv.set(x, cv.h - 1 - y, col)
            cv.set(cv.w - 1 - x, cv.h - 1 - y, col)


def _check_slice(name: str, cv: Canvas, border: Tuple[int, int, int, int]) -> None:
    """Полосы между углами и середина 9-slice должны быть однородны вдоль направления растяжения."""
    l, b, r, t = border
    x0, x1 = l, cv.w - r          # растягиваемые столбцы [x0, x1)
    y0, y1 = t, cv.h - b          # растягиваемые строки [y0, y1) (в координатах холста y вниз)
    bad = []
    for y in range(cv.h):
        if x1 - x0 > 1 and (l or r):
            row = {cv.get(x, y) for x in range(x0, x1)}
            if len(row) > 1:
                bad.append(f'строка {y}')
    for x in range(cv.w):
        # Border (l,0,r,0) — 3-slice только по ширине: высота задаётся родной, по вертикали не тянем.
        if y1 - y0 > 1 and (t or b):
            col = {cv.get(x, y) for y in range(y0, y1)}
            if len(col) > 1:
                bad.append(f'столбец {x}')
    if bad:
        raise ValueError(f'{name}: неоднородная растягиваемая зона 9-slice ({", ".join(bad[:6])})')


def _add9(reg: Registry, name: str, cv: Canvas, border: Tuple[int, int, int, int], note: str = '', strict: bool = True) -> None:
    if strict:
        _check_slice(name, cv, border)
    reg.add(name, cv, border=border, note=note)


# ───────────────────────── панели ─────────────────────────
def panel_dark() -> Canvas:
    """HUD-панель: тёмное тело navy1, светлая фаска в 2 px, тёмная внутренняя кромка, контур navy0."""
    return plate(16, 16, 3, [
        'navy0',
        bev('navy6', 'navy5', 'navy4', 'navy3', tr='navy5', bl='navy4'),
        bev('navy4', 'navy4', 'navy3', 'navy3'),
        'navy0',
    ], 'navy1')


def panel_inset() -> Canvas:
    """Утопленная строка: тень сверху-слева, светлая кромка снизу-справа (свет падает на нижний край ямки)."""
    return plate(12, 12, 2, [
        bev('navy0', 'navy0', 'navy3', 'navy4', tr='navy1', bl='navy1'),
        bev('navy0', 'navy1', 'navy1', 'navy1'),
    ], 'navy1')


def panel_light() -> Canvas:
    """Пергамент для подсказок: тело cream2, блик cream3, тень снизу cream1/cream0, тёплый контур wood1."""
    return plate(16, 16, 3, [
        'wood1',
        bev('cream4', 'cream3', 'cream1', 'cream0', tr='cream2', bl='cream1'),
        bev('cream3', 'cream2', 'cream2', 'cream1'),
    ], 'cream2')


BRACKET = [
    'OOOO',
    'OhmO',
    'OhdO',
    'OndO',
    'ONdO',
    'OOOO',
]


def panel_wood() -> Canvas:
    """Табличка «Шахта N»: толстая рамка из светлых досок, тёмное тело, светлые скобы с гвоздями.

    Сама табличка занимает строки 1…h-2: скобы выступают на 1 px за верхнюю и нижнюю кромку, как в концепте.
    """
    w, h = 24, 16
    cv = Canvas(w, h)
    paint(cv, shape(w, h - 2, 2, 0, 1), [
        'wood0',
        bev('wood6', 'wood5', 'wood3', 'wood3', tr='wood5', bl='wood4'),
        bev('wood5', 'wood4', 'wood3', 'wood2'),
        bev('wood4', 'wood4', 'wood3', 'wood2'),
        'wood1',
    ], 'wood2')
    legend = {'O': 'wood0', 'h': 'wood7', 'm': 'wood6', 'd': 'wood5', 'n': 'stone6', 'N': 'stone3'}
    top = Canvas.from_ascii(BRACKET, legend)
    bot = Canvas.from_ascii(BRACKET[:1] + list(reversed(BRACKET[1:-1])) + BRACKET[-1:], legend)
    for bx in (2, w - 6):
        cv.blit(top, bx, 0)
        cv.blit(bot, bx, h - 6)
    return cv


def window() -> Canvas:
    """Рамка окна: толстый деревянный кант с золотой полосой и золотыми накладками на углах, тело navy2."""
    w = h = 32
    cv = plate(w, h, 4, [
        'ink1',
        bev('wood6', 'wood5', 'wood3', 'wood2', tr='wood4', bl='wood3'),
        bev('wood5', 'wood4', 'wood3', 'wood2'),
        bev('wood4', 'wood4', 'wood3', 'wood2'),
        bev('gold4', 'gold3', 'gold2', 'gold1', tr='gold3', bl='gold2'),
        bev('wood2', 'wood2', 'wood1', 'wood1'),
        'ink2',
        bev('navy0', 'navy1', 'navy2', 'navy3'),
    ], 'navy2')
    # Золотые уголки-накладки поверх дерева: тот же объём, тоны дерева → тоны золота.
    # Тёмная сторона дерева уходит в gold2, а не в gold1: иначе нижние накладки выглядят грязно-оливковыми.
    to_gold = {'wood6': 'gold5', 'wood5': 'gold4', 'wood4': 'gold3', 'wood3': 'gold2', 'wood2': 'gold2'}
    rgb = {C(k)[:3]: v for k, v in to_gold.items()}
    for y in range(h):
        for x in range(w):
            dx, dy = min(x, w - 1 - x), min(y, h - 1 - y)
            if dx <= 6 and dy <= 6 and (dx <= 3 or dy <= 3):
                p = cv.get(x, y)
                if p[3] and p[:3] in rgb:
                    cv.set(x, y, rgb[p[:3]])
    # Торцы накладок — тёмная риска, чтобы золото не сливалось с деревом; заклёпка в углу.
    for k in range(1, 4):
        dots(cv, [(7, k), (k, 7)], 'wood1')
    dots(cv, [(2, 2)], 'gold5')
    dots(cv, [(3, 3)], 'gold1')
    return cv


def socket() -> Canvas:
    """Гнездо под кнопку (как рамка вокруг «Нанять» в концепте): светлая фаска navy, тёмная «яма» внутри.
    Кнопка ставится с отступом 3 px — при нажатии в зазоре видна яма."""
    return plate(16, 16, 3, [
        'navy0',
        bev('navy6', 'navy5', 'navy4', 'navy3', tr='navy5', bl='navy4'),
        bev('navy4', 'navy4', 'navy3', 'navy3'),
    ], 'ink1')


def window_header() -> Canvas:
    """Деревянная планка-заголовок: доска wood3 с бликом, золотые скобы с гвоздями на концах."""
    w, h = 32, 14
    cv = Canvas(w, h)
    m = shape(w, h, 2)
    paint(cv, m, [
        'wood0',
        bev('wood6', 'wood5', 'wood3', 'wood1', tr='wood5', bl='wood2'),
        bev('wood5', 'wood4', 'wood3', 'wood2'),
        bev('wood4', 'wood4', 'wood3', 'wood3'),
    ], 'wood3')
    # Золотые скобы на концах доски (внутри border 8).
    for bx in (3, w - 6):
        for y in range(1, h - 1):
            cv.set(bx, y, 'gold3')
            cv.set(bx + 1, y, 'gold3')
            cv.set(bx + 2, y, 'gold2')
        cv.hline(bx, bx + 2, 1, 'gold4')
        cv.hline(bx, bx + 2, h - 2, 'gold1')
        cv.set(bx + 1, 2, 'gold5')
        cv.set(bx + 1, h - 3, 'gold1')
    return cv


# ───────────────────────── кнопки ─────────────────────────
BTN = {
    # Как «Нанять» в концепте: контур, светлая линия сверху, светлая полоса 2 px и левый кант, тело,
    # тень снизу (тон темнее и холоднее), губа-торец 2 px, правый кант в тон тени. Золотая кнопка —
    # оранжево-золотая (тело amber3, полоса gold3), как на концепте, а не лимонная.
    #           контур    линия     полоса    тело      тень      губа
    'gold':  ('wood0', 'gold4', 'gold3', 'amber3', 'amber2', 'amber1'),
    'blue':  ('blue0', 'blue6', 'blue5', 'blue4', 'blue3', 'blue2'),
    'green': ('green0', 'green6', 'green5', 'green4', 'green3', 'green2'),
    'red':   ('red0', 'red6', 'red5', 'red4', 'red3', 'red2'),
    'dark':  ('ink1', 'navy6', 'navy4', 'navy3', 'navy2', 'navy1'),
    'disabled': ('navy0', 'slate5', 'slate4', 'slate3', 'slate2', 'slate1'),
}
# Искорка в левом верхнем углу лица (блик на золотой кнопке концепта); у тёмной и недоступной — нет.
BTN_SPARK = {'gold': 'gold5', 'red': 'red6', 'green': 'green6', 'blue': 'blue7'}


def button(w: int, h: int, cols, pressed: bool, spark: Optional[str] = None, cut: int = 3, lip: int = 2) -> Canvas:
    """Кнопка: контур, лицо (линия-блик, светлая полоса 2 px, светлый кант слева, тень внизу), губа lip px.

    Нажатая — губы нет, контур целиком на 1 px ниже сверху и на 1 px выше снизу: лицо той же высоты,
    его центр сдвинут ровно на 1 px вниз — столько же, сколько PixelUi сдвигает текст.
    У нажатой блик гаснет на тон, светлая полоса исчезает: кнопка выглядит вдавленной.
    """
    out, line, band, body, shd, lipc = cols
    cv = Canvas(w, h)
    top, hh, lip = (1, h - 2, 0) if pressed else (0, h, lip)
    m = shape(w, hh, cut, 0, top)
    edge = peel(m)[0]
    for (x, y) in edge:
        cv.set(x, y, out)
    inner = m - set(edge)
    face_bottom = top + hh - 2 - lip
    face = {p for p in inner if p[1] <= face_bottom}
    rim_l = band
    if pressed:
        line, band, rim_l = band, body, body
    paint(cv, face, [bev(line, rim_l, shd, shd, tl=line, tr=line, bl=shd, br=shd)], body)
    face_edge = peel(face)[0]
    for (x, y) in face:
        if (x, y) in face_edge:
            continue
        if top + 2 <= y <= top + 3:
            cv.set(x, y, band)          # светлая полоса сверху (в пределах верхнего border)
        elif y == face_bottom - 1:
            cv.set(x, y, shd)           # вторая строка тени: лицо «скругляется» книзу
    for (x, y) in inner - face:
        cv.set(x, y, lipc)
    if spark and not pressed:
        for (x, y) in face_edge:
            if x + y - top == cut:      # пиксель среза в левом верхнем углу лица
                cv.set(x, y, spark)
        cv.set(cut, top + 2, spark)
    return cv


def slot(pressed: bool) -> Canvas:
    """Квадратная ячейка-кнопка под крупную иконку (сундук, карточки в концепте): светлая рамка navy8…navy4,
    тёмная кромка, тело navy2, губа navy3. Нажатая устроена как у кнопок: без губы, лицо ниже на 1 px."""
    w = h = 20
    cv = Canvas(w, h)
    top, hh, lip = (1, h - 2, 0) if pressed else (0, h, 2)
    m = shape(w, hh, 3, 0, top)
    layers = peel(m)
    for p in layers[0]:
        cv.set(p[0], p[1], 'navy0')
    inner = m - set(layers[0])
    face_bottom = top + hh - 2 - lip
    face = {p for p in inner if p[1] <= face_bottom}
    paint(cv, face, [
        bev('navy8', 'navy7', 'navy5', 'navy5', tr='navy7', bl='navy6'),
        bev('navy7', 'navy6', 'navy4', 'navy4'),
        'navy0',
        bev('navy1', 'navy1', 'navy2', 'navy2', bl='navy1'),
    ], 'navy2')
    for (x, y) in inner - face:
        cv.set(x, y, 'navy3')
    return cv


# ───────────────────────── шкалы, таблетки, бейджи ─────────────────────────
def bar_frame() -> Canvas:
    """Рамка шкалы: контур navy0, светлая кромка, тёмный жёлоб ink1; заливка ставится с отступом 2 px."""
    return plate(12, 8, 2, [
        'navy0',
        bev('navy6', 'navy5', 'navy4', 'navy4'),
        bev('ink1', 'ink1', 'navy0', 'navy0'),
    ], 'navy0')


def bar_fill(top: str, hl: str, body: str, shd: str) -> Canvas:
    """Заливка шкалы: столбцы одинаковые, поэтому годится любая ширина (даже меньше border)."""
    cv = Canvas(6, 6)
    cv.rect(0, 0, 6, 6, body)
    cv.hline(0, 5, 0, top)
    cv.hline(0, 5, 1, hl)
    cv.hline(0, 5, 5, shd)
    return cv


def pill() -> Canvas:
    """Тёмная «таблетка» подписи/таймера, концы срезаны под 45°."""
    return plate(16, 12, 4, [
        'navy0',
        bev('navy6', 'navy5', 'navy4', 'navy3', tr='navy5', bl='navy4'),
        'navy0',
    ], 'navy1')


def badge_red() -> Canvas:
    rows = [
        '..OOOOO..',
        '.OHhRRRO.',
        'OHhRRRRRO',
        'OhRRRRRRO',
        'ORRRRRRSO',
        'ORRRRRRSO',
        'ORRRRRSSO',
        '.OSSSSSO.',
        '..OOOOO..',
    ]
    return Canvas.from_ascii(rows, {'O': 'red0', 'H': 'red6', 'h': 'red5', 'R': 'red4', 'S': 'red3'})


def toast() -> Canvas:
    """Всплывающее сообщение: тёмное тело, светлая фаска, плотный контур ink1 и «толщина» снизу."""
    w = h = 16
    cv = Canvas(w, h)
    m = shape(w, h - 1, 3)
    paint(cv, m, [
        'ink1',
        bev('navy7', 'navy6', 'navy4', 'navy4', tr='navy6', bl='navy5'),
        'navy0',
    ], 'navy2')
    # Нижняя «толщина» плашки (на 1 px ниже контура) — тост визуально приподнят над полем.
    for x in range(w):
        if cv.opaque(x, h - 2):
            cv.set(x, h - 1, 'ink1')
    for (x, y), d in peel(m)[0].items():
        if 'd' in d:
            cv.set(x, y, 'navy0')
    return cv


# ───────────────────────── карточки ─────────────────────────
PAW = [
    '..hP...hP..',
    '.hPPo.hPPo.',
    '.PPPo.PPPo.',
    '..oo...oo..',
    'hP.......hP',
    'PPo.hhh.PPo',
    '.o.hPPPo.o.',
    '..hPPPPPo..',
    '..PPPPPPo..',
    '..oPPPPPo..',
    '...ooooo...',
]


def paw(light: str = 'cream3', mid: str = 'cream2', dark: str = 'cream1') -> Canvas:
    """Лапка 11×11: четыре пальчика дугой и подушечка (блик слева-сверху, тень справа-снизу)."""
    return Canvas.from_ascii(PAW, {'h': light, 'P': mid, 'o': dark})


def card_back(with_paw: bool = True) -> Canvas:
    """Рубашка карточки: синяя, кремовая кайма, тонкая внутренняя рамка, лапка в центре (иконка «карточки» концепта)."""
    w, h = 24, 32
    cv = plate(w, h, 2, [
        'ink1',
        bev('cream3', 'cream3', 'cream1', 'cream1', tr='cream2', bl='cream2'),
        bev('cream2', 'cream2', 'cream1', 'cream0'),
        'blue1',
        bev('blue2', 'blue2', 'blue3', 'blue3'),
        bev('blue5', 'blue5', 'blue4', 'blue4'),
    ], 'blue3')
    if with_paw:
        p = paw()
        cv.blit(p, (w - p.w) // 2, (h - p.h) // 2)
    return cv


RARITY = {
    #              контур    светлый  средний  тёмный   камушек
    'common':    ('ink1', 'navy8', 'navy7', 'navy5', 'cream3'),
    'uncommon':  ('green0', 'green6', 'green5', 'green3', 'green6'),
    'rare':      ('blue0', 'blue6', 'blue5', 'blue3', 'blue7'),
    'epic':      ('purple0', 'purple5', 'purple4', 'purple2', 'purple6'),
    'legendary': ('wood0', 'gold5', 'gold3', 'gold1', 'gold5'),
    'majestic':  ('pink0', 'cream4', 'cream2', 'pink1', 'gold5'),
}


# Уголок-накладка карточки 4×4 (левый верхний; остальные — зеркально): кайма + камушек.
CARD_CORNER = [
    '....',
    '.MM.',
    '.MGg',
    '..gs',
]


def card_frame(rarity: str) -> Canvas:
    """Лицевая рамка карточки: кайма цвета редкости, тёмное окно под арт (середина однородна), камушки в углах."""
    out, lt, mid, dk, gem = RARITY[rarity]
    w, h = 24, 32
    cv = plate(w, h, 2, [
        out,
        bev(lt, lt, mid, dk, tr=mid, bl=mid),
        bev(mid, mid, dk, dk),
        'ink2',
        bev('navy1', 'navy1', 'navy2', 'navy3'),
    ], 'navy2')
    # Камушек редкости на углу окна (внутри border 6): чем реже, тем крупнее украшение.
    big = rarity in ('rare', 'epic', 'legendary', 'majestic')
    for fx in (False, True):
        for fy in (False, True):
            pts = [(3, 3, gem), (4, 3, mid), (3, 4, mid)]
            if big:
                pts += [(4, 4, dk), (2, 3, lt), (3, 2, lt)]
            for (x, y, c) in pts:
                cv.set(w - 1 - x if fx else x, h - 1 - y if fy else y, c)
    if rarity in ('legendary', 'majestic'):
        # Золотая искорка по верхней кромке углов.
        dots(cv, [(5, 1)], 'gold5')
    return cv


# ───────────────────────── обучение и мелочи ─────────────────────────
def toggle(on: bool) -> Canvas:
    w, h = 20, 10
    cv = Canvas(w, h)
    track = ('green0', 'green3', 'green4', 'green2') if on else ('navy0', 'slate3', 'slate2', 'slate1')
    out, lt, body, dk = track
    paint(cv, shape(w, h, (3, 1, 1)), [out, bev(dk, dk, lt, lt)], body)
    knob = Canvas.from_ascii([
        '..OOOO..',
        '.OHHCCO.',
        'OHHCCCCO',
        'OHCCCCSO',
        'OCCCCCSO',
        'OCCCCSSO',
        '.OSSSSO.',
        '..OOOO..',
    ], {'O': 'ink2', 'H': 'cream4', 'C': 'cream3', 'S': 'cream1'})
    cv.blit(knob, w - 9 if on else 1, 1)
    return cv


def bubble() -> Canvas:
    """Речевой пузырь обучения: тело cream3, мягкие скруглённые углы, тень снизу cream1, контур ink2."""
    return plate(24, 20, (4, 2, 1, 1), [
        'ink2',
        bev('cream4', 'cream4', 'cream2', 'cream1', tr='cream3', bl='cream2'),
    ], 'cream3')


def bubble_tail() -> Tuple[Canvas, Tuple[float, float]]:
    """Хвостик вниз. Строки 0–1 перекрывают нижние строки пузыря (тень и контур): тело продолжает пузырь."""
    rows = [
        'CCCCCCCC',
        'OCCCCCSO',
        '.OCCCSO.',
        '..OCSO..',
        '...OSO..',
        '....O...',
    ]
    cv = Canvas.from_ascii(rows, {'O': 'ink2', 'C': 'cream3', 'S': 'cream2'})
    return cv, (4.5, 0.0)


def focus_frame() -> Canvas:
    """Золотые уголки фокуса: L-скобы толщиной 2 px с контуром wood0; середина и стороны прозрачные."""
    corner = Canvas.from_ascii([
        'OOOOOO',
        'OSHHHO',
        'OHGGGO',
        'OHGOOO',
        'OHGO..',
        'OOOO..',
    ], {'O': 'wood0', 'S': 'gold5', 'H': 'gold4', 'G': 'gold2'})
    cv = Canvas(16, 16)
    flip_v = Canvas(6, 6)
    flip_v.img = corner.img.transpose(1)  # FLIP_TOP_BOTTOM
    flip_v.px = flip_v.img.load()
    cv.blit(corner, 0, 0)
    cv.blit(corner.flipped(), 10, 0)
    cv.blit(flip_v, 0, 10)
    cv.blit(flip_v.flipped(), 10, 10)
    return cv


def divider() -> Canvas:
    """Разделитель-канавка: тёмная линия и светлый скат под ней, концы сужаются."""
    return Canvas.from_ascii([
        '.DDDDDD.',
        'LLLLLLLL',
        '.SSSSSS.',
    ], {'D': 'navy0', 'L': 'navy5', 'S': 'navy3'})


def dashed_frame() -> Canvas:
    """Пунктирная рамка 20×20 без «+». Штрихи и промежутки по 2 px, углы скруглены; растягиваемая полоса
    (8 px) — ровно два периода штриха, поэтому в режиме Tiled рамка стыкуется без шва."""
    w = h = 20
    cv = Canvas(w, h)
    col = 'navy6'
    for i in (2, 3, 4, 7, 8, 11, 12, 15, 16, 17):
        cv.set(i, 1, col)
        cv.set(i, h - 2, col)
        cv.set(1, i, col)
        cv.set(w - 2, i, col)
    # Пиксели (1,1) и зеркальные не рисуются — уголок скруглён.
    return cv


def dashed_slot() -> Canvas:
    """Пустая ячейка UI: пунктирная рамка и «+» 6×6 толщиной 2 px в центре (как slot_marker на поле)."""
    cv = dashed_frame()
    for k in range(7, 13):
        for t in (9, 10):
            cv.set(k, t, 'navy6')
            cv.set(t, k, 'navy6')
    return cv


def divider_v() -> Canvas:
    """Вертикальная перегородка сегментов HUD-панели: тёмная кромка, светлая фаска, тёмная кромка.
    Ставится от внутренней тёмной кромки ui_panel_dark (3 px от края) до такой же снизу."""
    cv = Canvas(4, 8)
    for y in range(8):
        cv.set(0, y, 'navy0')
        cv.set(1, y, 'navy5')
        cv.set(2, y, 'navy4')
        cv.set(3, y, 'navy0')
    return cv


def slider_knob() -> Canvas:
    """Ручка ползунка 10×14: золотая плашка с риской, свет сверху-слева, губа снизу."""
    rows = [
        '.oooooooo.',
        'oHHHHHHHGo',
        'oHGGGGGGFo',
        'oHGGGGGGFo',
        'oHGGkkGGFo',
        'oHGGkkGGFo',
        'oHGGkkGGFo',
        'oHGGkkGGFo',
        'oHGGGGGGFo',
        'oHGGGGGGFo',
        'oFFFFFFFFo',
        'oDDDDDDDDo',
        '.oooooooo.',
    ]
    return Canvas.from_ascii(rows, {'o': 'gold0', 'H': 'gold5', 'G': 'gold4', 'F': 'gold3', 'D': 'gold2', 'k': 'gold2'})


def build(reg: Registry) -> None:
    _add9(reg, 'ui_panel_dark', panel_dark(), (5, 5, 5, 5))
    _add9(reg, 'ui_panel_wood', panel_wood(), (6, 6, 6, 6))
    _add9(reg, 'ui_panel_inset', panel_inset(), (4, 4, 4, 4))
    _add9(reg, 'ui_panel_light', panel_light(), (5, 5, 5, 5))
    _add9(reg, 'ui_window', window(), (10, 10, 10, 10))
    _add9(reg, 'ui_window_header', window_header(), (8, 4, 8, 4))
    for s in ('gold', 'blue', 'green', 'red', 'dark'):
        _add9(reg, f'ui_btn_{s}', button(16, 16, BTN[s], False, BTN_SPARK.get(s)), (6, 6, 6, 6))
        _add9(reg, f'ui_btn_{s}_pressed', button(16, 16, BTN[s], True, BTN_SPARK.get(s)), (6, 6, 6, 6))
    _add9(reg, 'ui_btn_disabled', button(16, 16, BTN['disabled'], False), (6, 6, 6, 6))
    _add9(reg, 'ui_socket', socket(), (6, 6, 6, 6))
    _add9(reg, 'ui_slot', slot(False), (6, 6, 6, 6))
    _add9(reg, 'ui_slot_pressed', slot(True), (6, 6, 6, 6))
    _add9(reg, 'ui_bar_frame', bar_frame(), (4, 3, 4, 3))
    _add9(reg, 'ui_bar_fill_gold', bar_fill('gold5', 'gold4', 'gold3', 'gold2'), (2, 2, 2, 2))
    _add9(reg, 'ui_bar_fill_green', bar_fill('green6', 'green5', 'green4', 'green3'), (2, 2, 2, 2))
    _add9(reg, 'ui_bar_fill_purple', bar_fill('purple6', 'purple5', 'purple4', 'purple3'), (2, 2, 2, 2))
    _add9(reg, 'ui_bar_fill_red', bar_fill('red6', 'red5', 'red4', 'red3'), (2, 2, 2, 2))
    _add9(reg, 'ui_pill', pill(), (6, 5, 6, 5))
    reg.add('ui_badge_red', badge_red())
    _add9(reg, 'ui_toast', toast(), (6, 6, 6, 6))
    # Лапка в середине рубашки: показывать Simple в родном/целом масштабе; для свободного размера —
    # ui_card_back_plain (Sliced) + ui_card_paw поверх по центру.
    _add9(reg, 'ui_card_back', card_back(), (6, 6, 6, 6), strict=False)
    _add9(reg, 'ui_card_back_plain', card_back(False), (6, 6, 6, 6))
    reg.add('ui_card_paw', paw())
    for r in RARITY:
        _add9(reg, f'ui_card_frame_{r}', card_frame(r), (6, 6, 6, 6))
    reg.add('ui_slider_knob', slider_knob())
    reg.add('ui_toggle_on', toggle(True))
    reg.add('ui_toggle_off', toggle(False))
    _add9(reg, 'ui_bubble', bubble(), (7, 7, 7, 7))
    tail, tail_pivot = bubble_tail()
    reg.add('ui_bubble_tail', tail, pivot=tail_pivot)
    _add9(reg, 'ui_focus_frame', focus_frame(), (6, 6, 6, 6))
    _add9(reg, 'ui_divider', divider(), (3, 0, 3, 0))
    _add9(reg, 'ui_divider_v', divider_v(), (0, 2, 0, 2))
    # «+» в середине: ячейку показывать в родном размере (или Simple в целом масштабе);
    # для произвольного размера — ui_dashed_frame (Tiled) и иконка «+» поверх.
    _add9(reg, 'ui_dashed_slot', dashed_slot(), (6, 6, 6, 6), strict=False)
    reg.add('ui_dashed_frame', dashed_frame(), border=(6, 6, 6, 6))
