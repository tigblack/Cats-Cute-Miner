"""Обязательные спрайты атласа — контракт между художественными модулями (sprites/*.py) и кодом Unity.

Имя → назначение, размер холста (ширина×высота в пикселях арта), pivot и border.
Обозначения pivot: «ноги» = (w/2, 0) — нижний центр холста совпадает с нижней кромкой footprint объекта;
«центр» = (w/2, h/2). Наложения (overlay) рисуются на холсте того же размера, что и база, и регистрируются
с тем же pivot: сборка обрезает пустые поля сама, положение в мире не меняется.
Код Unity обращается к спрайтам только по этим именам (PixelArt.World/Ui). Дополнительные спрайты разрешены.
"""
from typing import Dict, List

REQUIRED: Dict[str, str] = {}
REQUIRED_GROUPS: Dict[str, List[str]] = {}


def _req(group: str, names, desc: str) -> None:
    if isinstance(names, str):
        names = [names]
    for n in names:
        REQUIRED[n] = desc
        REQUIRED_GROUPS.setdefault(group, []).append(n)


# Окрасы котика с отметинами (ARTS: ART_DIRECTION.md, «Котики»): рыжий и серый полосатые, чёрный «смокинг», белый
# с пятнами (калико), кремовый с тёмными «пойнтами», коричневый полосатый с белой проточиной. Окрас — индивидуальность,
# не класс: сила котика — только уровень.
FURS = ['ginger', 'gray', 'black', 'white', 'cream', 'brown']
# Прокачка котика (решение владельца от 8 октября 2026; CatLook в Unity): оружие — по уровню внутри круга из 8
# уровней (1 кирка, 2 молот, 3 дробилка, 4 циркулярная пила, 5 бур-машина, 6 две кирки, 7 бензопила, 8 лазерган),
# каска — новый цвет на каждом круге (уровни 1–8, 9–16, 17–24, …).
WEAPONS = ['pick', 'hammer', 'crusher', 'saw', 'drill', 'dualpick', 'chainsaw', 'laser']
TOOLS = ['saw', 'drill', 'chainsaw', 'laser']          # работают «с упора»: свои кадры добычи tool_*
HELMET_COLORS = ['yellow', 'blue', 'red', 'green', 'purple', 'teal', 'pink', 'white']
# Кадры котика по состояниям (CatView): дыхание 6, моргание 2, потягивание 10, ходьба 8, добыча замахом 9 (кирка,
# молот, дробилка, две кирки) и добыча с упора 9 (пила, бур, бензопила, лазер), перенос 4, появление 8, радость 6.
MINE_FRAMES = ['mine_ready', 'mine_ready2', 'mine_antic', 'mine_raise', 'mine_top', 'mine_strike', 'mine_impact',
               'mine_recoil', 'mine_return']
TOOL_FRAMES = [f.replace('mine_', 'tool_') for f in MINE_FRAMES]
# Новый режим (Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md §6, §7, §16): ходьба с полной сумкой carry0–7, подбор
# мешочка с пола pickup0–3, разгрузка на складе unload0–3. Добавлены В КОНЕЦ CAT_FRAMES: индексы прежних 62 кадров
# (CatLook в Unity) не меняются.
CARRY_FRAMES = [f'carry{i}' for i in range(8)]
PICKUP_FRAMES = [f'pickup{i}' for i in range(4)]
UNLOAD_FRAMES = [f'unload{i}' for i in range(4)]
NEW_FRAMES = CARRY_FRAMES + PICKUP_FRAMES + UNLOAD_FRAMES
LEGACY_FRAMES = ([f'idle{i}' for i in range(6)] + ['blink', 'blink_d'] + [f'stretch{i}' for i in range(10)] +
                 [f'walk{i}' for i in range(8)] + MINE_FRAMES +
                 [f'drag{i}' for i in range(4)] + [f'spawn{i}' for i in range(8)] + [f'joy{i}' for i in range(6)] +
                 TOOL_FRAMES)
CAT_FRAMES = LEGACY_FRAMES + NEW_FRAMES


def weapon_frames(weapon: str):
    """Кадры оружия: у ударного нет кадров tool_*, у инструмента — кадров mine_* (их игра не показывает).
    Кадры нового режима (NEW_FRAMES) старое оружие не получает: там кирка команды — слой cat_pick_*."""
    skip = MINE_FRAMES if weapon in TOOLS else TOOL_FRAMES
    return [f for f in LEGACY_FRAMES if f not in skip]


# Экипировка команды нового режима (ТЗ §10.1): один комплект на всех котиков, 4 слота × 4 редкости; base — вид пустого
# слота (без бонуса). Слои рисуются на том же холсте 40×40 с pivot (20, 4), что и тело.
EQUIP_RARITIES = ['base', 'common', 'rare', 'epic', 'legendary']
# Заполнение сумки (наложение на рюкзак, не зависит от редкости рюкзака): 1 — несколько монет, 2 — полная,
# 3 — с горкой через край.
LOAD_LEVELS = [1, 2, 3]
# Кадры, которые показывает котик нового режима: дыхание, моргание, потягивание, ходьба, добыча замахом, появление
# (найм на базе), радость и новые кадры с грузом. Кадры tool_* и drag* новый режим не использует.
EQUIP_FRAMES = ([f'idle{i}' for i in range(6)] + ['blink', 'blink_d'] + [f'stretch{i}' for i in range(10)] +
                [f'walk{i}' for i in range(8)] + MINE_FRAMES + [f'spawn{i}' for i in range(8)] +
                [f'joy{i}' for i in range(6)] + NEW_FRAMES)
# Кирка команды — в лапках во всех кадрах, где тело держит рукоять. В carry/pickup/unload лапки заняты лямками
# и мешочком: кирка убрана в рюкзак, слоя нет (в Unity — null, как у оружия без кадра).
PICK_FRAMES = [f for f in EQUIP_FRAMES if f not in NEW_FRAMES]
# Мешочек-порция в лапках (подбор: схватил с пола, закинул через плечо; разгрузка: достал из рюкзака, несёт вперёд,
# бросает в склад). В остальных кадрах подбора и разгрузки лапки пусты.
SACK_FRAMES = ['pickup1', 'pickup2', 'unload0', 'unload1', 'unload2']
CHESTS = ['wooden', 'iron', 'forged', 'silver', 'golden', 'royal', 'mythical', 'legendary']

# ───────── env: пол, стены, декор окружения ─────────
_req('env', [f'floor_{i}' for i in range(4)], 'плита пола 32×32, бесшовная, extrude; швы-«затирка» по краям плиты (сетка клеток читается)')
_req('env', 'floor_gate', 'пол строки ворот 32×32, extrude: порог из тёмного камня/досок')
_req('env', 'slot_marker', 'пустая клетка зоны мержа 32×32, центр, alpha: пунктирный скруглённый квадрат с «+» (как в концепте)')
_req('env', [f'wall_side_{i}' for i in range(4)], 'заливка стены пещеры за краем поля 32×32, бесшовная во все стороны, extrude')
_req('env', ['wall_edge_l_0', 'wall_edge_l_1'], 'край левой стены 32×32 (столбец −1), камень с выступом к полу справа, бесшовный по вертикали, extrude')
_req('env', ['wall_edge_r_0', 'wall_edge_r_1'], 'край правой стены 32×32 (столбец 7), зеркально левому, extrude')
_req('env', 'wall_top', 'задняя стена над строкой 0, 32×32, бесшовная по горизонтали, extrude')
_req('env', 'wall_top_edge', 'кромка между задней стеной и строкой 0, 32×32, бесшовная по горизонтали, extrude')
_req('env', 'wall_bottom', 'грунт ниже последней строки, 32×32, бесшовный, extrude')
_req('env', 'beam_post', 'деревянная опора на стене, ~8×48, ноги')
_req('env', ['lantern_0', 'lantern_1'], 'подвесной фонарь (2 кадра мерцания), ~10×16, ноги = точка подвеса снизу')
_req('env', ['crystal_cluster_a', 'crystal_cluster_b'], 'друза фиолетовых кристаллов на стене, ~16×16, ноги')
_req('env', 'banner_paw', 'висящее знамя с лапкой/киркой, ~14×28, ноги')
_req('env', ['barrel', 'crate', 'sack'], 'бочка / ящик / мешок на стене, ~14×14, ноги')
_req('env', 'minecart_ore', 'вагонетка с золотой рудой, ~22×18, ноги')
_req('env', 'rails_v', 'вертикальный отрезок рельсов 16×32, бесшовный по вертикали, extrude')
_req('env', 'mushrooms', 'светящиеся грибы, ~12×10, ноги')
_req('env', 'sign_question', 'деревянная табличка с «?», ~14×18, ноги')
_req('env', 'chain_hang', 'висящая цепь ~4×24, ноги = верхняя точка крепления снизу')

# ───────── rocks: добываемые камни и препятствия ─────────
_req('rocks', [f'rock_plain_{i}' for i in range(3)], 'серый валун с золотыми самородками, холст 32×40, ноги; тело в нижних ~28 px')
_req('rocks', ['rock_elixir_small', 'rock_elixir_big'], 'камень с фиолетовыми кристаллами (эликсир), 32×40, ноги')
_req('rocks', ['rock_gems_small', 'rock_gems_big'], 'камень с бирюзовыми самоцветами (твёрдая валюта), 32×40, ноги')
_req('rocks', [f'rock_chest_{c}' for c in CHESTS], 'сундук в каменной россыпи, материал сундука по уровню, 32×40, ноги')
_req('rocks', 'rock_pass', 'валун с синим жетоном-звездой (валюта сезона), 32×40, ноги')
_req('rocks', ['rock_crack_1', 'rock_crack_2'], 'наложение трещин на любой rock_* (HP < 2/3 и < 1/3), 32×40, ноги')
_req('rocks', ['block_tl', 'block_t', 'block_tr', 'block_l', 'block_c', 'block_r', 'block_bl', 'block_b', 'block_br'],
     'автотайлы каменного массива ≥2×2, 32×32, ноги; нижний ряд — с передней стенкой-обрывом')
_req('rocks', ['block_vt', 'block_vm', 'block_vb'], 'массив шириной 1: верх/середина/низ, 32×32, ноги')
_req('rocks', ['block_hl', 'block_hm', 'block_hr'], 'массив высотой 1: лево/середина/право, 32×32, ноги')
_req('rocks', [f'block_single_{i}' for i in range(1, 7)] + ['block_single_6l', 'block_single_6r'],
     'одиночные препятствия 1×1 (варианты v1…v6, v6L/v6R прижаты к стене), 32×40, ноги')
_req('rocks', ['wall_gate_l', 'wall_gate_r'], 'каменные столбы у краёв строки ворот (wall1x1vL/vR), стыкуются с забором ворот, 32×56, ноги')
_req('rocks', ['waterfall_0', 'waterfall_1', 'waterfall_2'], 'кадры воды водопада 32×32, бесшовные по вертикали, extrude')
_req('rocks', 'waterfall_top', 'верх водопада (вода выходит из трещины), 32×32, ноги')
_req('rocks', ['waterfall_pool_0', 'waterfall_pool_1'], 'низ водопада с пеной (2 кадра), 32×32, ноги')

# ───────── structures: шахты, ворота, выход, кузница, контейнеры ─────────
_req('structures', 'shaft_base', 'шахта 2×3: копёр/вход в штольню, рельсы, площадка; холст 64×112, ноги (32, 0)')
_req('structures', [f'shaft_wheel_{i}' for i in range(4)], 'наложение: колесо копра, 4 кадра вращения, холст 64×112, ноги')
_req('structures', 'shaft_rubble', 'наложение: завал (доски крест-накрест, камни) на входе, холст 64×112, ноги')
_req('structures', 'shaft_gems', 'наложение: куча кристаллов светлых тонов cream/white — тонируется цветом самоцвета шахты, холст 64×112, ноги')
_req('structures', 'shaft_light', 'наложение alpha: тёплый свет из штольни (шахта работает), холст 64×112, ноги')
_req('structures', 'gate_frame', 'ворота 5×1: столбы, забор, вывеска со скрещёнными кирками; холст 160×64, ноги (80, 0)')
_req('structures', ['gate_door_closed', 'gate_door_broken', 'gate_door_open_0', 'gate_door_open_1', 'gate_door_open'],
     'наложение: створки (закрыты / разбиты / открываются ×2 / открыты), холст 160×64, ноги')
_req('structures', 'exit_base', 'выход 3×1: арка штольни с рельсами в темноту и табличкой «?»; холст 96×80, ноги (48, 0)')
_req('structures', 'exit_boards', 'наложение: заколоченный вход (доски, камни), холст 96×80, ноги')
_req('structures', 'exit_glow', 'наложение alpha: тёплый свет открытого выхода, холст 96×80, ноги')
_req('structures', 'forge_base', 'кузница 2×3 «кошачья печь»: голова рыжего кота из кирпича (уши, глаза-окошки, усы, топка-рот, ошейник с колокольчиком), во дворе наковальня, слитки и золотая кошка-талисман; холст 64×106, ноги (32, 0)')
_req('structures', ['forge_fire_0', 'forge_fire_1', 'forge_fire_2'], 'наложение: огонь в топке и отблеск в глазах, 3 кадра, холст 64×106, ноги')
_req('structures', 'forge_glow', 'наложение alpha: тёплый ореол топки, холст 64×106, ноги')
_req('structures', 'barrel_cat', 'бочка бесплатного котика с торчащими ушками, 32×36, ноги')
_req('structures', ['barrel_fly_0', 'barrel_fly_1'], 'бочка в полёте из пушки: котик зажмурился и машет лапками, 2 кадра, 32×36, ноги')
_req('structures', 'cannon_base', 'пушка 1×2 у правого края: лафет, хобот, дальнее колесо; ствол ≈48° вверх-вправо (в игре отражена — смотрит влево); холст 32×72, ноги (16, 0)')
_req('structures', 'cannon_barrel', 'наложение: чугунный ствол с золотыми кольцами и лапкой (отдача — сдвиг слоя), холст 32×72, ноги')
_req('structures', ['cannon_wheel_0', 'cannon_wheel_1'], 'наложение: ближнее колесо поверх ствола, кадр 1 — поворот спиц при откате, холст 32×72, ноги')
_req('structures', ['crate_cat', 'crate_dynamite', 'crate_gold', 'crate_elixir'], 'ящик доставки с меткой содержимого, 32×36, ноги')

# ───────── cards: экран «Карты» (портреты карт, фоны, мелкие иконки) ─────────
_req('cards', [f'card_bg_{t}' for t in ['common', 'uncommon', 'rare', 'epic', 'legendary', 'majestic']], 'фон портрета карты по редкости, 64×48')
_req('cards', 'ui_card_tag', 'лента эффекта под портретом карты, 9-slice 10×9, border 3')
_req('cards', 'icon_card_small', 'маленькая карта 7×9 у шкалы копий')
_req('cards', 'icon_sort', 'сортировка карт: три ползунка, 16×16')
_req('cards', ['card_cat', 'card_cat_helmet', 'card_cat_hat', 'card_gem', 'card_cat_paws'],
     'портрет управляющего по слоям на холсте 48×44 (центр — опора): котик по пояс, каска или шляпа, самоцвет (светлый, красится кодом), лапки')
_req('cards', 'card_forge', 'портрет карты кузницы: маленькая «кошачья печь», 48×44')
_req('cards', [f'card_item_{k}' for k in ['golden_cart', 'pocket_watch', 'golden_helmet', 'recruit_poster', 'lucky_paw', 'sledgehammer',
                                           'gate_bell', 'gold_vein', 'fish_parcel', 'big_dynamite', 'clover', 'fish', 'yarn', 'milk_bowl',
                                           'catnip', 'toy_mouse', 'cat_bed', 'scratcher', 'collar', 'lantern', 'treasure_map']],
     'портрет карты-предмета по коду эффекта, 48×44')

# ───────── cats: котик-шахтёр по слоям ─────────
_req('cats', [f'cat_body_{f}_{fr}' for f in FURS for fr in CAT_FRAMES],
     'котик (шерсть с отметинами окраса, мордочка, комбинезон, лапки, хвост) смотрит вправо; холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_helmet_{c}_{fr}' for c in HELMET_COLORS for fr in LEGACY_FRAMES],
     'наложение: каска с фонариком и прорезями для ушей, цвет — круг из 8 уровней, холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_weapon_{w}_{fr}' for w in WEAPONS for fr in weapon_frames(w)],
     'наложение: оружие уровня (кирка, молот, дробилка, циркулярная пила, бур-машина, две кирки, бензопила, лазерган), холст 40×40, ноги (20, 4)')
# Экипировка команды (новый режим, ТЗ §10.1, §16). Порядок слоёв в CatView снизу вверх: тень 0, кольцо 1, тело 2,
# обувь 3, рюкзак 4, груз 5, головной убор 6, кирка 7, мешочек в лапках 8. Все — холст 40×40, ноги (20, 4).
_req('cats', [f'cat_boots_{r}_{fr}' for r in EQUIP_RARITIES for fr in EQUIP_FRAMES],
     'наложение: обувь поверх лапок ног (base — тканевые обмотки, common — кожаные ботинки, rare — стальной носок, '
     'epic — фиолетовые с кристаллом, legendary — золотые), холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_pack_{r}_{fr}' for r in EQUIP_RARITIES for fr in EQUIP_FRAMES],
     'наложение: рюкзак за спиной (слева, котик смотрит вправо) и лямки на груди; base — тканевая котомка, дальше '
     'больше и богаче; хвост, голова и лапки поверх, холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_load_{n}_{fr}' for n in LOAD_LEVELS for fr in EQUIP_FRAMES],
     'наложение: груз в рюкзаке (1 — несколько монет, 2 — полный завязанный мешок, 3 — горка монет через край); '
     'один для всех редкостей рюкзака, холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_hat_{r}_{fr}' for r in EQUIP_RARITIES for fr in EQUIP_FRAMES],
     'наложение: головной убор с открытыми ушами (base — жёлтая каска, common — кожаная шапка с очками, rare — '
     'стальной шлем с фонарём, epic — шлем с кристаллом-фонарём, legendary — золотой шлем-корона), холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_pick_{r}_{fr}' for r in EQUIP_RARITIES for fr in PICK_FRAMES],
     'наложение: кирка команды в лапках (base — железная, common — дерево и железо, rare — синяя сталь, epic — '
     'фиолетовый кристалл, legendary — золотая, крупнее); лапки вырезаны, холст 40×40, ноги (20, 4)')
_req('cats', [f'cat_sack_{fr}' for fr in SACK_FRAMES],
     'наложение: мешочек-порция с монетами в лапках (подбор, разгрузка); лапки вырезаны, холст 40×40, ноги (20, 4)')
_req('cats', 'select_ring', 'кольцо выделения котика (эллипс, золото), ~28×12, центр')
_req('cats', ['badge_level', 'badge_level_hi'], 'бейдж уровня (тёмный шестиугольник со светлой каймой; hi — золотая кайма), ~13×13, центр')
_req('cats', [f'digit_{i}' for i in range(10)], 'цифра 5×7 светлая (cream3), без контура, центр')

# ───────── ui: 9-slice элементы интерфейса (на экране ×4) ─────────
_req('ui', 'ui_panel_dark', 'тёмно-синяя панель с фаской и тонкой светлой кромкой, 16×16, border 5')
_req('ui', 'ui_panel_wood', 'деревянная табличка с гвоздиками (заголовки), 24×16, border 6')
_req('ui', 'ui_panel_inset', 'утопленная тёмная область (строки списков), 12×12, border 4')
_req('ui', 'ui_panel_light', 'светлая «пергаментная» панель (подсказки обучения), 16×16, border 5')
_req('ui', 'ui_window', 'рамка окна (тёмное тело, деревянно-золотой кант), 32×32, border 10')
_req('ui', 'ui_window_header', 'деревянная планка-заголовок окна, 32×14, border (8,4,8,4)')
for _s in ['gold', 'blue', 'green', 'red', 'dark']:
    _req('ui', [f'ui_btn_{_s}', f'ui_btn_{_s}_pressed'], f'кнопка {_s}: обычная / нажатая (без нижней губы), 16×16, border 6')
_req('ui', 'ui_btn_disabled', 'кнопка недоступна (серо-синяя, приглушённая), 16×16, border 6')
_req('ui', ['ui_slot', 'ui_slot_pressed'], 'квадратная ячейка-кнопка под иконку (сундук, чертежи, динамит), 20×20, border 6')
_req('ui', 'ui_bar_frame', 'рамка шкалы, 12×8, border (4,3,4,3)')
_req('ui', ['ui_bar_fill_gold', 'ui_bar_fill_green', 'ui_bar_fill_purple', 'ui_bar_fill_red'], 'заливка шкалы с бликом сверху, 6×6, border 2')
_req('ui', 'ui_pill', 'тёмная «таблетка» (таймер, подписи), 16×12, border (6,5,6,5)')
_req('ui', 'ui_badge_red', 'красный кружок-уведомление, 9×9, центр, без border')
_req('ui', 'ui_toast', 'панель всплывающего сообщения, 16×16, border 6')
_req('ui', 'ui_card_back', 'рубашка карточки (синяя, лапка), 24×32, border 6')
_req('ui', [f'ui_card_frame_{r}' for r in ['common', 'uncommon', 'rare', 'epic', 'legendary', 'majestic']],
     'рамка лицевой стороны карточки по редкости (серый, зелёный, синий, фиолетовый, золотой), окно под арт, 24×32, border 6')
_req('ui', ['ui_toggle_on', 'ui_toggle_off'], 'переключатель (вкл — зелёный, выкл — серый), 20×10, центр')
_req('ui', 'ui_bubble', 'речевой пузырь обучения (светлый), 24×20, border 7')
_req('ui', 'ui_bubble_tail', 'хвостик пузыря вниз, 8×6, ноги = кончик')
_req('ui', 'ui_focus_frame', 'золотые уголки фокуса обучения, 16×16, border 6, середина прозрачная')
_req('ui', 'ui_divider', 'горизонтальный разделитель, 8×3, border (3,0,3,0)')
_req('ui', 'ui_dashed_slot', 'пунктирная пустая ячейка с «+» для UI, 20×20, border 6')

# ───────── icons: 16×16 (крупные _big — 24×24), центр ─────────
_req('icons', [f'icon_{n}' for n in [
    'coin', 'elixir', 'gem', 'cat', 'gear', 'sound_on', 'sound_off', 'vibro_on', 'vibro_off', 'chest', 'cards', 'dynamite',
    'clock', 'arrow_up', 'lock', 'check', 'plus', 'close', 'star', 'pickaxe', 'upgrade', 'forge', 'gate', 'exit', 'shaft',
    'info', 'warning', 'paw', 'hand', 'income']], 'иконка 16×16, центр')
_req('icons', ['icon_chest_big', 'icon_cards_big', 'icon_dynamite_big', 'icon_cat_big'], 'крупная иконка 24×24 для нижних кнопок HUD, центр')

# ───────── fx: эффекты, тени, оверлеи мира ─────────
_req('fx', 'px_white', 'белый пиксель 1×1 (затемнение секторов, полосы)')
_req('fx', ['shadow_xs', 'shadow_s', 'shadow_m', 'shadow_l', 'shadow_xl'],
     'alpha-тень эллипс (xs ~12×5 высоко летящая бочка, s ~20×7 котик, m ~28×9 камень, l ~60×16 кузница/шахта, xl ~150×18 ворота), центр')
_req('fx', [f'fx_spark_{i}' for i in range(4)], 'искорка-звёздочка 7×7, 4 кадра, центр')
_req('fx', [f'fx_puff_{i}' for i in range(4)], 'облачко пыли 12×12, 4 кадра, центр')
_req('fx', [f'fx_coin_{i}' for i in range(4)], 'вращающаяся монетка 8×8, 4 кадра, центр')
_req('fx', [f'fx_ring_{i}' for i in range(4)], 'расходящееся кольцо объединения, 32×16, 4 кадра, центр')
_req('fx', [f'fx_smoke_{i}' for i in range(4)], 'дым из трубы 10×10, 4 кадра, центр')
_req('fx', [f'fx_boom_{i}' for i in range(5)], 'милый взрыв динамита 32×32, 5 кадров, центр')
_req('fx', [f'fx_muzzle_{i}' for i in range(3)], 'залп пушки 16×16: вспышка, огненный клуб, дым; 3 кадра, центр')
_req('fx', [f'fx_impact_{i}' for i in range(3)], 'контакт удара кирки 11×11: точка с лучами, кольцо лучей, искры; 3 кадра, центр')
_req('fx', [f'fx_strike_pick_{i}' for i in range(4)], 'магическая золотая кирка игрока 32×32: появление, замах, удар, отдача; pivot (10.5, 3.5) — контакт')
_req('fx', ['fx_glow_warm', 'fx_glow_purple', 'fx_glow_white'], 'alpha-ореол 32×32 ступенчатый радиальный, центр')
_req('fx', 'fx_glow_lantern', 'alpha-ореол фонаря на опоре 14×32: узкий вертикальный, целиком на стене, центр')
_req('fx', ['hp_frame', 'hp_fill'], 'полоса HP над целью: рамка 10×6 border 3; заливка 4×4 border 1 (жёлто-оранжевая)')
_req('fx', ['bubble_world', 'bubble_world_tail'], 'пузырь статуса над объектом (светлый, тёмный контур) 16×14 border 5; хвостик 6×4, ноги = кончик')
_req('fx', 'select_bracket', 'золотые уголки выделения объекта, 16×16, border 6, середина прозрачная')

# ───────── дополнительные имена, к которым обращается код (с запасным спрайтом в коде, но лучше с родным) ─────────
_req('env', ['wall_top_1', 'wall_top_2', 'wall_top_3'], 'варианты задней стены 32×32, бесшовные по горизонтали, extrude')
_req('env', ['wall_corner_tl', 'wall_corner_tr', 'wall_corner_bl', 'wall_corner_br'], 'углы рамки поля 32×32 (строки −1 и Depth, столбцы −1 и 7), extrude')
_req('env', 'slot_marker_hi', 'пустая клетка зоны мержа под несомым котиком (золотой пунктир), 32×32, центр, alpha')
_req('structures', 'gate_door_cracked', 'наложение: створки с трещинами (ворота повреждены, но ещё не разрушены), холст 160×64, ноги')
_req('ui', ['ui_divider_v', 'ui_dashed_frame', 'ui_card_paw', 'ui_card_back_plain'],
     'вертикальный разделитель; пунктирная рамка (Tiled); лапка рубашки карточки; рубашка без лапки (свободный размер)')
_req('icons', 'icon_hand_big', 'крупная лапка-указатель обучения 24×24, центр')
_req('icons', ['icon_music', 'icon_ambience', 'icon_effects'], 'иконки настроек: музыка, атмосфера, эффекты, 16×16, центр')
_req('ui', 'ui_slider_knob', 'ручка ползунка громкости 10×13, центр')
_req('icons', [f'icon_arrow_{d}' for d in ['down', 'left', 'right', 'up_left', 'up_right', 'down_left', 'down_right']],
     'стрелки к объектам за краем экрана по направлениям, 16×16, центр')
_req('icons', [f'icon_chest_{c}' for c in CHESTS], 'сундук по уровню для окна награды (как rock_chest_*), 26×24, центр')

# ───────── tutorial: наставник обучения ─────────
_req('tutorial', ['tutor_cat_0', 'tutor_cat_1', 'tutor_cat_2'],
     'кот-наставник для пузыря обучения (портрет по грудь): 0 — спокойно, 1 — говорит (рот открыт), 2 — моргает; 40×40, центр')
_req('tutorial', 'ui_portrait_frame', 'круглая рамка портрета наставника (золотой кант, тёмно-синее поле внутри 40×40), 52×52, центр')

# ───────── loot: добыча с камней (собирается тапом) ─────────
_req('loot', ['loot_gold', 'loot_elixir', 'loot_hard'], 'кучка добычи на клетке: монеты / капля-кристалл эликсира / бирюзовый самоцвет; ~20×18, ноги (низ по центру)')
_req('loot', 'loot_glow', 'alpha-ореол под добычей (тёплый, ступенчатый) 28×12, центр')

# ═════════ новый режим (Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md §5, §7, §8, §10, §16) ═════════
# Имена и раскладка — Docs/Rework/ART_NAMES.md. Необязательные (есть в атласе, но код может обойтись без них):
# camp_tent, depot_chute, fx_sack_pop_0…2 и варианты тем *_crystal/*_copper (PixelTheme берёт базовый спрайт).
EQUIP_SLOTS = ['pickaxe', 'hat', 'backpack', 'boots']    # слоты экипировки команды (ТЗ §10.1), порядок как в окне
ITEM_RARITIES = ['common', 'rare', 'epic', 'legendary']  # редкости предметов (base — пустой слот, иконка icon_slot_*)
BRANCHES = ['team', 'pickaxes', 'boots', 'bags', 'unload', 'value']  # ветки улучшений базы (ТЗ §5.2)

# ───────── base: лагерь под картой (площадка pad0…pad2 и двор st0…st2, см. sprites/base.py) ─────────
_req('base', [f'camp_floor_{i}' for i in range(4)], 'настил площадки базы (доски), 32×32, бесшовный, extrude, центр; строка pad1')
_req('base', ['camp_edge_t', 'camp_edge_b'], 'верхний ряд площадки (порог-брус у плит карты) / нижний ряд (кромка настила со ступенькой, очередь котиков), 32×32, extrude, центр')
_req('base', ['camp_ground_0', 'camp_ground_1'], 'утоптанная земля двора под постройками (строки st0…st2), 32×32, бесшовная, extrude, центр')
_req('base', 'depot_base', 'приёмный склад 3×2 (столбцы 2–4, ноги на нижней кромке st1), воронки над столбцами 2 и 4; холст 96×64, ноги (48, 0)')
_req('base', 'depot_glow', 'наложение alpha: свет приёма во время разгрузки поверх depot_base, холст 96×64, ноги (48, 0)')
_req('base', ['depot_slot', 'depot_slot_active'], 'место разгрузки (люк в настиле; active — люк открыт, монеты в жёлобе), наклейка пола 32×32, центр клетки')
_req('base', [f'depot_crate_{i}' for i in range(4)], 'ящик с монетами перед складом, заполнение 0…3 из 3, 32×32, ноги (16, 0)')
_req('base', [f'hire_board_{i}' for i in range(3)], 'доска найма, уровень оформления 0…2 (размер команды), 32×64, ноги (16, 0)')
_req('base', [f'bench_{i}' for i in range(3)], 'верстак инструментов, уровень оформления 0…2 (ветка «Кирки»), 32×64, ноги (16, 0)')
_req('base', [f'rack_{i}' for i in range(3)], 'стеллаж сумок, уровень оформления 0…2 (ветка «Сумки»), 32×64, ноги (16, 0)')
_req('base', ['wardrobe_closed', 'wardrobe_open'], 'шкаф экипировки закрыт / открыт (окно «Экипировка»), 32×64, ноги (16, 0)')
_req('base', ['cart_0', 'cart_1'], 'вагонетка пустая / с мешками (транспорт), 32×32, ноги (16, 0)')
_req('base', ['camp_lantern_0', 'camp_lantern_1'], 'фонарь на столбике, 2 кадра мерцания 0,3–0,8 с, 16×40, ноги (8, 0)')
_req('base', 'camp_sign', 'вывеска лагеря с лапкой и монетой, 32×32, ноги (16, 0)')

# ───────── rocks: стадии расчистки препятствий (ТЗ §8.2) ─────────
_req('rocks', [f'block_crack_{i}' for i in range(1, 4)], 'наложение трещин на любую клетку block_*/block_single_* (HP < 0,75 / 0,45 / 0,2), 32×32, ноги (16, 0)')
_req('rocks', [f'rubble_debris_{i}' for i in range(3)], 'след расчищенного препятствия на полу (проходимо), 32×32, ноги (16, 0)')
_req('rocks', [f'wall_gate_{s}_d{i}' for s in 'lr' for i in (1, 2)], 'каменный столб у ворот, стадии повреждения 1 и 2 (к wall_gate_l/r), 32×56, ноги (16, 0)')
_req('rocks', [f'waterfall_clear_{i}' for i in range(4)], 'вода водопада при расчистке, стадии 0…3 (3 — сухое русло), 32×32, бесшовные по вертикали, extrude, ноги (16, 0)')
_req('rocks', ['waterfall_top_dry', 'waterfall_pool_dry'], 'верх и чаша водопада после расчистки (к waterfall_clear_3), 32×32, ноги (16, 0)')

# ───────── structures: месторождение и барьер ворот (ТЗ §8.3, §8.4) ─────────
_req('structures', [f'shaft_crack_{i}' for i in range(1, 4)], 'наложение: трещины месторождения 2×3, стадии 1…3, холст 64×112, ноги (32, 0)')
_req('structures', 'shaft_cleared', 'след расчищенного месторождения 2×3 (пол, проходимо), холст 64×112, ноги (32, 0)')
_req('structures', ['gate_barrier_mid', 'gate_barrier_broken'], 'пролёт барьера ворот на 1 клетку (закрыт / разбит после открытия), бесшовный по горизонтали, 32×64, ноги (16, 0)')
_req('structures', 'gate_barrier_post', 'столб барьера на границе клеток (со стороны стены), 16×64, ноги (8, 0)')

# ───────── loot: груз на земле (ТЗ §7) ─────────
_req('loot', ['cargo_sack_1', 'cargo_sack_2'], 'груз на земле: один мешочек 16×16, ноги (8, 0) / два мешочка 20×16, ноги (10, 0)')
_req('loot', ['cargo_heap_s', 'cargo_heap_l'], 'груз на земле: небольшая куча 25×19, ноги (12, 0) / большая куча 33×25, ноги (16, 0)')
_req('loot', 'cargo_glow', 'alpha-пятно под грузом 28×12, центр (на 3 px выше ног груза)')

# ───────── icons: экипировка, ветки базы, статусы (16×16, центр; _big — 32×32, центр) ─────────
_req('icons', [f'icon_item_{s}_{r}' for s in EQUIP_SLOTS for r in ITEM_RARITIES], 'предмет экипировки: слот × редкость (цвет редкости как у ui_card_frame_*), 16×16, центр')
_req('icons', [f'icon_item_{s}_{r}_big' for s in EQUIP_SLOTS for r in ITEM_RARITIES], 'крупная иконка предмета для окна «Экипировка», 32×32, центр')
_req('icons', [f'icon_slot_{s}' for s in EQUIP_SLOTS], 'пустой слот экипировки (силуэт предмета), 16×16, центр')
_req('icons', [f'icon_branch_{b}' for b in BRANCHES], 'ветка улучшений базы: команда / кирки / обувь / сумки / разгрузка / оценка добычи, 16×16, центр')
_req('icons', ['icon_parts', 'icon_sack', 'icon_depot', 'icon_base', 'icon_equipment'],
     'детали (валюта улучшения предметов) / груз в пути / приёмный склад / кнопка «К базе» / кнопка «Экипировка», 16×16, центр')
_req('icons', 'icon_ad_tv', 'кнопка награды за рекламу («Double income» в окне «Пока вас не было»): ретро-телевизор с «play», 16×16, центр')
_req('icons', 'icon_store', 'кнопка магазина: деревянный прилавок с полосатым навесом, 16×16, центр')
_req('icons', ['store_gems_handful', 'store_gems_sack', 'store_gems_bag', 'store_gems_crate',
               'store_gems_cart', 'store_gems_pile'], 'магазин: наборы бирюзовых самоцветов, 24×24, центр')
_req('icons', ['store_elixir_bottle', 'store_elixir_box', 'store_elixir_wagon'],
     'магазин: бутылка / ящик / повозка фиолетового эликсира, 24×24, центр')
_req('icons', ['store_skip_1h', 'store_skip_4h', 'store_skip_12h'],
     'магазин: пропуск времени на 1 / 4 / 12 часов, золотые секундомеры, 24×24, центр')

_req('card_art', [f'card_art_ca{i:03}' for i in list(range(1, 61)) + [62]],
     'уникальные запечённые сцены карт 48×44 (sprites/card_art.py): 39 карточек заводов — котик-работник у своего цеха '
     '(Foreman) и с готовым товаром (Manager), ТЗ 2.2 §7.2; ca001 и 21 глобальная карточка — котики-специалисты и предметы; '
     'только непрозрачные цвета RAMPS, без тонировки в игре')

# ═════════ ТЗ 2.2: заводы вместо месторождений (Docs/TZ/Cat_Miners_Factories_TZ_v2.2.md §11) ═════════
# Товары по номеру типа завода 1…20 (порядок MineShafts JSON и строк Goods в factory_rules.json; sprites/goods.py сверяет
# список с конфигом при сборке).
GOODS = ['milk', 'wood', 'fish', 'yarn', 'catfood', 'boxes', 'bricks', 'steel', 'glass', 'catnip',
         'toys', 'scratchers', 'cushions', 'bells', 'collars', 'gears', 'lanterns', 'honey', 'silk', 'caviar']
FACTORY_WORK_FRAMES = 4

# ───────── factories: завод 2×3 на месте месторождения s (sprites/factories.py) ─────────
_req('factories', [f'factory_{g}' for g in GOODS],
     'здание завода 2×3: силуэт копра shaft_base с колесом (стоит — кадр колеса 0), вывеска с иконкой товара; '
     'холст 64×112, ноги (32, 0). Locked — код затемняет здание (SpriteRenderer.color) и кладёт factory_chains поверх')
_req('factories', [f'factory_{g}_work_{i}' for g in GOODS for i in range(FACTORY_WORK_FRAMES)],
     'наложение «завод работает» поверх factory_<good>: колесо крутится, дым из трубы, конвейер у двери; 4 кадра по 0,15 с; '
     'холст 64×112, ноги (32, 0), порядок здания + 1')
_req('factories', 'factory_chains', 'наложение: цепи крест-накрест через фасад и колесо, большой замок; холст 64×112, ноги (32, 0)')
_req('factories', [f'factory_chains_crack_{i}' for i in range(1, 4)],
     'наложение поверх factory_chains (как shaft_crack_*): трещины звеньев и замка по HP цепей < 0,75 / 0,45 / 0,2; '
     'холст 64×112, ноги (32, 0)')
_req('factories', 'fpipe_coupling', 'муфта жгута труб (стальной хомут с болтами, симметричный), 12×12, центр')
_req('factories', 'fpipe_cap', 'заглушка трубы у завода: фланец с окном 8×8 под goods_<good>_8 (иконка поверх, центры совпадают), 14×14, центр')
_req('factories', 'fpipe_inlet', 'вход труб на склад: стальной коллектор с гнёздами сверху, 16×10, центр')
_req('factories', 'fpipe_capsule', 'партия в пути (вертикальная труба): капсула белых тонов — код красит цветом товара, 3×6, центр')
_req('factories', 'fpipe_capsule_h', 'партия в пути (горизонтальная труба): та же капсула 6×3, центр')

# ───────── goods: иконки товаров (sprites/goods.py) ─────────
_req('goods', [f'goods_{g}' for g in GOODS], 'иконка товара для UI (рынок, «Deliver ×N», стрелка у края, «+N»), 16×16, центр')
_req('goods', [f'goods_{g}_8' for g in GOODS], 'малая иконка товара (заглушка трубы fpipe_cap, «+N» на поле), 8×8, центр')

# ───────── base: склад товаров во дворе лагеря ─────────
_req('base', ['warehouse_empty', 'warehouse_full'],
     'склад товаров (Warehouse) в столбце baseCol + 4 заднего ряда: пустой / с товаром; вход труб сверху (центр fpipe_inlet в (16, 4) '
     'холста от левого верхнего угла); 32×64, ноги (16, 0)')

# ───────── fx: разлёт цепей ─────────
_req('fx', [f'fx_chain_break_{i}' for i in range(4)], 'разлёт звеньев и замка при открытии завода, 4 кадра по 0,07 с, 32×32, центр')

# ───────── icons: заводы и рынок ─────────
_req('icons', 'icon_market', 'кнопка Market: весы с монетой и ящиком товара (не прилавок — он у icon_store), 16×16, центр')
_req('icons', 'icon_auto', 'значок AUTO у полосы завода: две зелёные стрелки по кругу, 16×16, центр')
_req('icons', 'icon_deliver', 'кнопка Deliver: ящик и золотая стрелка «отправить», 16×16, центр')
_req('icons', 'icon_factory', 'завод в сведениях цели (вместо icon_shaft): цех с шестернёй и трубой, 16×16, центр')
_req('icons', 'icon_chain', 'цепи завода (Locked, HP цепей): три стальных звена, 16×16, центр')
