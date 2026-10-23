import json,collections
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
c=collections.Counter()
for r in J['Rocks']:
    for e in r['RewardCycle']:
        k=(e['RewardType'], e['DetailedType'] if e['RewardType']!=1 else 'Gacha*', e['Quantity'])
        c[k]+=1
for k,v in sorted(c.items(),key=str): print(k,v)
print([ (r['Id'],len(r['RewardCycle'])) for r in J['Rocks'] if any(e['DetailedType'] in ('Hard','BattlePass') for e in r['RewardCycle'])])
print(set(tuple(sorted(e.keys())) for r in J['Rocks'] for e in r['RewardCycle']))
for d in J['Deliveries']: print(d['Id'],d['RewardModel'],d['QuantityBase'],d['QuantityMultiplier'],d['QuantityGrowth'],d['QuantityPower'],d['QuantityRound'],d['QuantityFormulaType'],d['Weight'],d['MaxDupes'],d['RankUnlock'],d['CheckPointUnlock'],d['IsAd'])
