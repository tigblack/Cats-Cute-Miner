#!/usr/bin/env python3
"""Генерирует Assets/Game/Resources/Outpost/factory_rules.json (ТЗ 2.2 §3.2, версия factories-1).

Правила:
  * CycleSec      — копия MineShafts.GenerationDelaySecBase исходного json-data.json (Р1), по всем 20 типам;
  * UnitsPerCycle — выпуск за цикл без карточек (стартовое значение 20 у всех типов);
  * PriceFactor   — PriceRate × CycleSec / UnitsPerCycle (точная дробь). PriceRate = 4 — калибровка §9 (WP7, 15 октября 2026):
                    медиана шахт 2–5 модели free 4,94 мин при b4 4,96 (предел 4,22–5,70); стартовый ориентир ТЗ был 5/16.
                    Перебор — Docs/Reports/balance-factories/calibration.md, отчёт — Docs/Rework/BALANCE_FACTORIES.md;
  * Color         — тон палитры <рампа><индекс> из RAMPS Tools/PixelArt/pxl.py (проверяется: рампа есть, индекс в пределах);
  * GoodId        — ID товара по приложению A ТЗ 2.2.
Порядок Goods — порядок MineShafts в JSON (номер типа 1…20). Исходный JSON не меняется (проверяется SHA-256).

Запуск из корня проекта:
  python3 Tools/Factories/gen_factory_rules.py                    # записать файл
  python3 Tools/Factories/gen_factory_rules.py --check            # сверить файл с генерацией (код 1 при расхождении)
  python3 Tools/Factories/gen_factory_rules.py --price-rate 3/8   # другой PriceRate (перебор калибровки §9 — Audit_TuneFactories)
После записи пересобрать встроенные копии ядра:
  flock Temp/gen_defaults.lock python3 Tools/gen_outpost_config_defaults.py
"""
import argparse
import ast
import hashlib
import json
import os
import sys
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, 'Assets', 'Game', 'Data', 'Source', 'json-data.json')
SOURCE_SHA256 = '8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5'
PXL = os.path.join(ROOT, 'Tools', 'PixelArt', 'pxl.py')
OUT = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Outpost', 'factory_rules.json')

VERSION = 'factories-1'
DEFAULT_PRICE_RATE = Fraction(4)  # калибровка ТЗ 2.2 §9 (было 5/16 — ориентир §3.2)
DEFAULT_UNITS = 20
PIPE_SPEED_CELLS_PER_SEC = Fraction(6)
MIN_TRANSIT_SEC = Fraction(1)

# Приложение A: тип JSON → (ID товара, тон полосы трубы и капсулы). Порядок — номер типа (как MineShafts в JSON).
GOODS = [
    ('amethyst', 'milk', 'cream4'),        # Dairy — белый
    ('citrine', 'wood', 'wood5'),          # Sawmill
    ('agate', 'fish', 'navy7'),            # Fishery — серо-голубой
    ('topaz', 'yarn', 'pink2'),            # Yarn Mill
    ('opal', 'catfood', 'amber2'),         # Cat Food Factory — оранжевый
    ('jade', 'boxes', 'fur_cream2'),       # Box Factory — картон
    ('onyx', 'bricks', 'red3'),            # Brickworks
    ('sapphire', 'steel', 'stone6'),       # Steel Mill
    ('tourmaline', 'glass', 'teal5'),      # Glassworks
    ('aquamarine', 'catnip', 'green5'),    # Catnip Farm
    ('emerald', 'toys', 'purple4'),        # Toy Factory
    ('diamond', 'scratchers', 'copper8'),  # Scratcher Works — сизаль
    ('garnet', 'cushions', 'pink3'),       # Cushion Factory
    ('heliodor', 'bells', 'gold3'),        # Bell Foundry
    ('ruby', 'collars', 'red4'),           # Collar Works
    ('alexandrite', 'gears', 'gold2'),     # Gear Works — латунь
    ('celestine', 'lanterns', 'amber4'),   # Lantern Works
    ('amber', 'honey', 'amber3'),          # Apiary
    ('moonstone', 'silk', 'purple6'),      # Silk Mill
    ('sunstone', 'caviar', 'red5'),        # Caviar Cannery
]


def fail(msg):
    raise SystemExit('gen_factory_rules: ' + msg)


def frac_text(f):
    f = Fraction(f)
    return str(f.numerator) if f.denominator == 1 else '%d/%d' % (f.numerator, f.denominator)


def load_ramps():
    """RAMPS из pxl.py без импорта модуля (pxl.py тянет PIL): литерал словаря через ast."""
    with open(PXL, 'r', encoding='utf-8') as f:
        tree = ast.parse(f.read(), PXL)
    for node in tree.body:
        target = None
        if isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name):
            target, value = node.target.id, node.value
        elif isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            target, value = node.targets[0].id, node.value
        if target == 'RAMPS':
            ramps = ast.literal_eval(value)
            if not isinstance(ramps, dict) or not ramps:
                fail('RAMPS в pxl.py не словарь')
            return ramps
    fail('в pxl.py нет RAMPS')


def split_tone(name):
    """'fur_cream2' → ('fur_cream', 2); None — имя не вида <рампа><индекс>."""
    i = len(name)
    while i > 0 and name[i - 1].isdigit():
        i -= 1
    if i == 0 or i == len(name):
        return None
    return name[:i], int(name[i:])


def build(price_rate, units):
    with open(SOURCE, 'rb') as f:
        raw = f.read()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != SOURCE_SHA256:
        fail('SHA-256 json-data.json %s ≠ %s' % (digest, SOURCE_SHA256))
    data = json.loads(raw.decode('utf-8-sig'))
    shafts = data['MineShafts']
    ids = [s['Id'] for s in shafts]
    if ids != [g[0] for g in GOODS]:
        fail('MineShafts %s не совпадают с таблицей приложения A %s' % (ids, [g[0] for g in GOODS]))
    goods_ids = [g[1] for g in GOODS]
    if len(set(goods_ids)) != len(goods_ids):
        fail('повтор GoodId')
    ramps = load_ramps()
    if price_rate <= 0:
        fail('PriceRate должен быть > 0')
    if units < 1:
        fail('UnitsPerCycle должен быть ≥ 1')

    goods = []
    for shaft, (type_id, good_id, color) in zip(shafts, GOODS):
        if shaft['WidthCells'] != 2 or shaft['DepthCells'] != 3:
            fail('%s: footprint %s×%s, ожидался 2×3' % (type_id, shaft['WidthCells'], shaft['DepthCells']))
        for k in ('GenerationDelaySecMultiplier', 'GenerationDelaySecGrowth', 'GenerationDelaySecFormulaType'):
            if shaft[k] != 0:
                fail('%s.%s = %s, ожидался 0' % (type_id, k, shaft[k]))
        base = shaft['GenerationDelaySecBase']
        if base != int(base) or base <= 0:
            fail('%s.GenerationDelaySecBase = %s: ожидалось целое > 0' % (type_id, base))
        cycle = int(base)
        tone = split_tone(color)
        if tone is None or tone[0] not in ramps or not 0 <= tone[1] < len(ramps[tone[0]]):
            fail('%s: цвет %s не тон RAMPS pxl.py' % (type_id, color))
        goods.append({
            'TypeId': type_id,
            'GoodId': good_id,
            'CycleSec': cycle,
            'UnitsPerCycle': units,
            'PriceFactor': frac_text(price_rate * cycle / units),
            'Color': color,
        })
    return goods


def render(price_rate, goods):
    lines = [
        '{',
        '  "Version": "%s",' % VERSION,
        '  "PriceRate": "%s",' % frac_text(price_rate),
        '  "PipeSpeedCellsPerSec": "%s",' % frac_text(PIPE_SPEED_CELLS_PER_SEC),
        '  "MinTransitSec": "%s",' % frac_text(MIN_TRANSIT_SEC),
        '  "Goods": [',
    ]
    for i, g in enumerate(goods):
        item = ('    { "TypeId": "%s", "GoodId": "%s", "CycleSec": %d, "UnitsPerCycle": %d, "PriceFactor": "%s", "Color": "%s" }'
                % (g['TypeId'], g['GoodId'], g['CycleSec'], g['UnitsPerCycle'], g['PriceFactor'], g['Color']))
        lines.append(item + (',' if i + 1 < len(goods) else ''))
    lines += ['  ]', '}', '']
    return '\n'.join(lines)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--price-rate', default=frac_text(DEFAULT_PRICE_RATE), help='PriceRate дробью, например 5/16')
    ap.add_argument('--units', type=int, default=DEFAULT_UNITS, help='UnitsPerCycle для всех типов')
    ap.add_argument('--check', action='store_true', help='только сверить файл с генерацией')
    args = ap.parse_args()
    price_rate = Fraction(args.price_rate)
    text = render(price_rate, build(price_rate, args.units))
    json.loads(text)  # самопроверка формата
    if args.check:
        try:
            with open(OUT, 'r', encoding='utf-8', newline='') as f:
                current = f.read()
        except FileNotFoundError:
            current = None
        if current != text:
            print('factory_rules.json differs from generator output', file=sys.stderr)
            sys.exit(1)
        print('factory_rules.json OK')
        return
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    print('written', os.path.relpath(OUT, ROOT))


if __name__ == '__main__':
    main()
