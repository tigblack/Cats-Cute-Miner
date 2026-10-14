import json,re,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print('Miners',{k:v for k,v in d['Miners'][0].items()})
print('Reinf',d['Reinforcements'][0])
print('Cart',d['SpawningCart'][0])
print('CP',d['CheckPoint'][0])
print('Spells',d['Spells'][0])
bp=d['BalanceProperties'][0]
print({k:v for k,v in bp.items() if k not in ('StackableAdsSettings','WelcomeBackAdSettings','WeeklyOffer','BattlePass','LteCurrencyPerZone')})
print('BP keys', bp['BattlePass'].keys() if isinstance(bp['BattlePass'],dict) else type(bp['BattlePass']))
print('BP activ', {k:v for k,v in bp['BattlePass'].items() if 'Rank' in k} if isinstance(bp['BattlePass'],dict) else '')
print('rocks FT', C.Counter((r['SoftCurrencyFormulaType'],r['SoftCurrencyGrowth']) for r in d['Rocks']))
print('ms', C.Counter((m['WidthCells'],m['DepthCells'],m['CurrencyOutputFormulaType'],m['CurrencyOutputBase'],m['CurrencyOutputGrowth'],m['UpgradeCostFormulaType'],m['UpgradeCostMultiplier']) for m in d['MineShafts']))
am=[m for m in d['MineShafts'] if m['Id']=='amethyst'][0]; print(am)
print('GO soft', C.Counter((g['SoftCurrencyRewardFormulaType'],g['SoftCurrencyRewardGrowth']) for g in d['GeneratorObjectives']))
print('GO sums', set(sum(g['ObjectiveCount']) for g in d['GeneratorObjectives']), C.Counter(len(g['ObjectiveCount']) for g in d['GeneratorObjectives']))
for g in d['GeneratorObjectives']:
    t=1;ts=[]
    for c in g['ObjectiveCount'][:4]: t+=c; ts.append(t)
    print(' ',g['GeneratorId'],ts, g['CoreCurrencyMultiplier'][:5], g['SoftCurrencyRewardBase'],g['SoftCurrencyRewardMultiplier'])
