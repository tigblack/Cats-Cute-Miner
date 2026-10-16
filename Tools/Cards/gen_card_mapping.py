#!/usr/bin/env python3
"""Генерирует таблицу Docs/Rework/CARD_EFFECT_MAPPING.md (раздел «Все 61 карточка») из исходного json-data.json,
Assets/Game/Resources/Outpost/card_rules.json и словаря имён Loc.Cards.cs. Запуск из корня: python3 Tools/Cards/gen_card_mapping.py
Без ключей печатает таблицу в stdout; --write вставляет её в документ между маркерами <!-- cards:begin --> и <!-- cards:end -->;
--check сверяет таблицу документа с данными (код 1 — устарела).
"""
import json, os, re, sys
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
src = json.load(open(os.path.join(ROOT, 'Assets/Game/Data/Source/json-data.json'), encoding='utf-8'))
rules = json.load(open(os.path.join(ROOT, 'Assets/Game/Resources/Outpost/card_rules.json'), encoding='utf-8'))
loc = open(os.path.join(ROOT, 'Assets/Game/Scripts/Core/Text/Loc.Cards.cs'), encoding='utf-8').read()
names = dict(re.findall(r'd\.Add\("cards\.name\.(ca\d+)", "([^"]+)"\);', loc))
costs = {c['Rarity']: c for c in src['CardUpgradeCosts']}
RAR = {1: 'Common', 2: 'Uncommon', 3: 'Rare', 4: 'Epic', 5: 'Legendary', 6: 'Majestic'}
FT = {0: 'F-0 B+M·x+G·x²', 1: 'F-1 B+M·G^(x−1)', 2: 'F-2', 3: 'F-3'}
WHOLE = {'TeamCap', 'StartWorkers', 'Capacity', 'OfflineHours', 'ChestCards'}
WHERE = {
    'TargetValue': 'TeamStats.CoinValueFor → OutpostSimulation.ReleaseCargo (цена при выпуске)',
    'TargetDamage': 'TeamStats.DamageUnitsFor → OutpostSimulation.HitTarget',
    'UnloadRate': 'TeamStats.UnloadRate/UnloadTicks → склад (фаза 4)',
    'GlobalValue': 'TeamStats.FinalValue → цена новых порций',
    'HireDiscount': 'TeamUpgradeService.Quote/NextCost («Команда»)',
    'TeamCap': 'TeamUpgradeService (cap «Команды»), OutpostInvariants',
    'StartWorkers': 'StageProgressionService.CreateRun (Run.ExtraWorkers)',
    'CritChance': 'TeamStats.CritChancePpm → HitTarget (детерминированный бросок)',
    'CritPower': 'TeamStats.CritMultiplier → HitTarget',
    'GateDamage': 'TeamStats.DamageUnitsFor(\'p\') → HitTarget',
    'KindValue': 'TeamStats.CoinValueFor (вид цели) → ReleaseCargo',
    'MoveSpeed': 'TeamStats.FinalMoveSpeed/EdgeTicks → движение (фаза 1, остаток ребра сохраняется)',
    'KindDamage': 'TeamStats.DamageUnitsFor (вид цели) → HitTarget',
    'ChestLuck': 'CardChestService.Open (шансы Rare+)',
    'Capacity': 'TeamStats.Capacity → сумки',
    'PickupSpeed': 'TeamStats.PickupTicks → подбор (фаза 3)',
    'GlobalDamage': 'TeamStats.FinalDamage → все удары',
    'BranchDiscount': 'TeamUpgradeService.Quote/NextCost (ветки кроме «Команды»)',
    'OfflineHours': 'OfflineJob (предел офлайна)',
    'TapDamage': 'TeamStats.TapDamageUnits → OutpostSimulation.ExecuteStrike',
    'ChestElixir': 'CardChestService.Open (эликсир сундука)',
    'ChestCards': 'CardChestService.Open (+случайные карты)',
    'CompletionParts': 'StageProgressionService.ApplyCompletion (детали)',
    'FactoryOutput': 'FactoryMath.UnitsPerCycle — единственная функция выпуска floor(UnitsPerCycle × (1 + Σ Output)), '
                     'Σ по типу TargetIds — CardService.FactoryOutputBonus; фиксируется в конце цикла (шаг 4б), '
                     'читается OutpostSimulation.FactoryUnitsPerCycle; не в TeamStats',
}
RULE = {'LegacyConfirmed': 'подтверждён прежним кодом, адаптирован', 'LegacyAssumed': 'предположение старого ТЗ, адаптирован',
        'CatMiners': 'новое правило Cat Miners'}

def frac(s):
    return Fraction(s)

def fmt(kind, v):
    if kind in WHOLE:
        return str(int(v))
    if kind == 'CritPower':
        return '×' + str(float(2 + v)).rstrip('0').rstrip('.').replace('.', ',')
    p = v * 100
    return (str(int(p)) if p.denominator == 1 else str(float(p)).replace('.', ',')) + '%'

def effect(card):
    e = rules['Effects'].get(card['Id'])
    if e is None and card['IsManager']:
        e = rules['Effects']['manager-%d' % card['Rarity']]
    return e

rows = []
for c in sorted(src['Cards'], key=lambda c: c['Id']):
    e = effect(c)
    maxl = len(costs[c['Rarity']]['Duplicates']) + 1
    base, per = frac(e['Base']), frac(e['PerLevel'])
    v1 = base
    vmax = base + per * (maxl - 1)
    if e['Kind'] in WHOLE:
        v1, vmax = Fraction(int(v1)), Fraction(int(vmax))
    kind = e['Kind'] + ('·' + e['TargetKind'] if e.get('TargetKind') else '')
    src_formula = '%s %s; B=%g M=%g G=%g' % (c['StatModifierType'], FT.get(c['ModifierFormulaType'], c['ModifierFormulaType']),
                                             c['ModifierBase'], c['ModifierMultiplier'], c['ModifierGrowth'])
    target = ', '.join(c['TargetIds']) if c['TargetIds'] else '—'
    rows.append('| `%s` | %s | %s | %d | %s | %s | %s | %s | %s + %s·(L−1) | %s / %s (L%d) | %s | %s |' % (
        c['Id'], names.get(c['Id'], '?'), RAR[c['Rarity']], c['MineUnlock'], target, 'да' if c['IsManager'] else '—',
        src_formula, kind, e['Base'], e['PerLevel'], fmt(e['Kind'], v1), fmt(e['Kind'], vmax), maxl,
        RULE[e['Rule']], WHERE[e['Kind']]))

head = ('| ID | Имя | Редкость | Шахта | TargetIds | Manager | Исходный код и формула | Новый эффект | Формула значения | L1 / макс. | Происхождение | Где применяется |\n'
        '|---|---|---|---:|---|---|---|---|---|---|---|---|')
table = head + '\n' + '\n'.join(rows)

DOC = os.path.join(ROOT, 'Docs/Rework/CARD_EFFECT_MAPPING.md')
BEGIN, END = '<!-- cards:begin -->', '<!-- cards:end -->'


def splice(doc):
    i, j = doc.find(BEGIN), doc.find(END)
    if i < 0 or j < i:
        raise SystemExit(DOC + ': нет маркеров ' + BEGIN + ' / ' + END)
    return doc[:i + len(BEGIN)] + '\n' + table + '\n' + doc[j:]


if '--write' in sys.argv or '--check' in sys.argv:
    with open(DOC, encoding='utf-8', newline='') as f:
        doc = f.read()
    new = splice(doc)
    if '--check' in sys.argv:
        if new != doc:
            print('CARD_EFFECT_MAPPING.md: таблица устарела — python3 Tools/Cards/gen_card_mapping.py --write')
            sys.exit(1)
        print('CARD_EFFECT_MAPPING.md: таблица актуальна')
    else:
        with open(DOC, 'w', encoding='utf-8', newline='') as f:
            f.write(new)
        print('written', os.path.relpath(DOC, ROOT))
else:
    print(table)
