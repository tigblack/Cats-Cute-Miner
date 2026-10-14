"""Пересчёт контрольных значений Приложения C ТЗ v1.2 (рабочий профиль v1).
Запуск: python3 control_values.py <json-data.json>"""
import json, math, sys
d = json.load(open(sys.argv[1]))
F0 = lambda B, M, G, x: B + M * x + G * x * x
F1 = lambda B, M, G, x: B + M * G ** (x - 1)
F2 = lambda B, M, G, x: B * G ** (x - 1) + M * (x - 1)
def R(v, r):
    if r == 0: return v
    r = abs(r); q = v / r
    return math.copysign(math.floor(abs(q) + 0.5), q) * r
F3 = lambda B, M, G, P, Rn, x: R(B + M * (G * x) ** P, Rn)
mi = d['Miners'][0]; print('damage L1..4', [round(F1(mi['DamageBase'], mi['DamageMultiplier'], mi['DamageGrowth'], L), 4) for L in range(1, 5)])
sc = d['SpawningCart'][0]; print('forge cost', [math.ceil(F2(sc['UpgradeCostBase'], sc['UpgradeCostMultiplier'], sc['UpgradeCostGrowth'], L)) for L in range(1, 5)])
am = [m for m in d['MineShafts'] if m['Id'] == 'amethyst'][0]; print('amethyst cost', [math.ceil(F2(am['UpgradeCostBase'], 0, am['UpgradeCostGrowth'], L)) for L in range(1, 4)])
rb = [r for r in d['Rocks'] if r['Id'] == 'rockbasic'][0]
ccs = lambda L: F3(rb['CoreCurrencySecondsBase'], rb['CoreCurrencySecondsMultiplier'], rb['CoreCurrencySecondsGrowth'], rb['CoreCurrencySecondsPower'], rb['CoreCurrencySecondsRound'], L)
print('rock seconds L1..3', [ccs(L) for L in (1, 2, 3)], 'gold L1 @1.5', math.floor(ccs(1) * 1.5))
re = d['Reinforcements'][0]
print('hire cost', [math.ceil(F1(re['CostBase'], re['CostMultiplier'], re['CostGrowth'], n + 1)) for n in range(6)],
      'after 1 gate', [max(1, math.ceil(F1(0, 2, 4, n + 1) / 10)) for n in range(4)])
print('hire level by purchase#', [1 + math.floor(F0(re['LevelBase'], re['LevelMultiplier'], re['LevelGrowth'], n)) for n in range(22)])
bp = d['BalanceProperties'][0]
print('delivery delay n=0,10,30', [round(F2(bp['DeliveryDelaySecBase'], bp['DeliveryDelaySecMultiplier'], bp['DeliveryDelaySecGrowth'], n + 1), 2) for n in (0, 10, 30)])
dl = {x['Id']: x for x in d['Deliveries']}; q = dl['15']
print('delivery15 core seconds rank4,10', [F3(q['QuantityBase'], q['QuantityMultiplier'], q['QuantityGrowth'], q['QuantityPower'], q['QuantityRound'], r) for r in (4, 10)])
sp = d['Spells'][0]; print('dynamite L1..3', [round(F1(sp['EffectBase'], sp['EffectMultiplier'], sp['EffectGrowth'], L), 2) for L in (1, 2, 3)])
print('manager common L14, uncommon L12', F1(0, 1, 3, 14), F1(0, 1, 4, 12))
go = {g['GeneratorId']: g for g in d['GeneratorObjectives']}; z2 = d['Zones'][1]['RankMultipliers'][0]
g = go['amethyst']; print('amethyst elixir k=1 zone2', math.floor(F0(g['SoftCurrencyRewardBase'], g['SoftCurrencyRewardMultiplier'], 0, 1) * z2['GenObjectiveSoftCurrencyMultiplier']))
for gid in ('spawningcart', 'amethyst'):
    t = 1; out = []
    for c, m in zip(go[gid]['ObjectiveCount'][:4], go[gid]['CoreCurrencyMultiplier'][:4]):
        t += c; out.append((t, m))
    print(gid, 'thresholds', out, 'last', 1 + sum(go[gid]['ObjectiveCount']))
G = {x['Id']: x for x in d['Gacha']}; z44 = d['Zones'][43]['RankMultipliers'][0]; z1 = d['Zones'][0]['RankMultipliers'][0]
for gid in ('GachaWooden', 'GachaIron', 'GachaGolden'):
    x = G[gid]; N = round(x['BaseNumCards'] * z44['GachaCardsMultNormal'] + 1e-9)
    print(gid, 'zone44 N', N, 'U>=', N // x['UncommonWeight'], 'R>=', N // x['RareWeight'])
w = G['GachaWooden']; print('zone1 wooden N', round(w['BaseNumCards'] * z1['GachaCardsMultNormal']), 'elixir', math.floor(w['SoftCurrencyMin'] * z1['GachaSoftCurrencyMultNormal']), math.floor(w['SoftCurrencyMax'] * z1['GachaSoftCurrencyMultNormal']))
rs = [r for r in d['Rocks'] if r['Id'] == 'rocksoftcurrencysmall'][0]
print('soft small L2 zone1', max(1, math.floor(F0(rs['SoftCurrencyBase'], rs['SoftCurrencyMultiplier'], 0, 2) * z1['MiningSoftCurrencyMultiplier'])))
