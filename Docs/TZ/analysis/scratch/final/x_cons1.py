import json,sys,collections
d=json.load(open(sys.argv[1]))
bp=d['BalanceProperties'][0]
print('BP keys:', sorted(bp.keys()))
z=d['Zones'][0]; print('Zone keys', sorted(z.keys())); print('RankMult keys', sorted(z['RankMultipliers'][0].keys()))
print('Card keys', sorted(d['Cards'][0].keys()))
for c in d['Cards']:
    if c['Id'] in ('ca029','ca034','ca038','ca062','ca001'):
        print({k:v for k,v in c.items() if k not in ('Name',)})
cnt=collections.Counter(c['StatModifierType'] for c in d['Cards']); print('codes',cnt)
print('rarity6',[c['Id'] for c in d['Cards'] if c['Rarity']==6])
nonapp=[c['Id'] for c in d['Cards'] if c['StatModifierType'] not in (9,10,7,1)]; print('nonapplied',len(nonapp),nonapp)
print('managers ids', sorted(c['Id'] for c in d['Cards'] if c.get('IsManager'))[:3], sorted(c['Id'] for c in d['Cards'] if c.get('IsManager'))[-3:])
print('CheckPoint', d['CheckPoint'])
for k in ('BalanceProperties','Reinforcements','SpawningCart','CheckPoint','Miners','Spells','FreeGachaCycle'):
    print(k, type(d[k]).__name__, len(d[k]))
print('Deliveries sample', d['Deliveries'][0])
print('deliv types', [(x['Id'],x['RewardModel']['RewardType'] if 'RewardModel' in x else None, x['RewardModel'].get('DetailedType') if 'RewardModel' in x else None, x.get('IsAd'), x.get('MaxDupes'), x.get('RankUnlock'), x.get('CheckPointUnlock')) for x in d['Deliveries']])
