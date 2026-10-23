import json,re,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); raw=open(J).read()
# Cards
cards=d['Cards']
print('cards',len(cards),'mgr',sum(c['IsManager'] for c in cards))
mg=[c for c in cards if c['IsManager']]
print('mgr rarity',C.Counter(c['Rarity'] for c in mg))
print('mgr growth by rarity',C.Counter((c['Rarity'],c['ModifierGrowth'],c['ModifierBase'],c['ModifierMultiplier'],c['ModifierFormulaType'],c['StatModifierType']) for c in mg))
print('mgr ids', sorted(c['Id'] for c in mg)[:3], sorted(c['Id'] for c in mg)[-3:])
nm=[c for c in cards if not c['IsManager']]
for c in sorted(nm,key=lambda c:c['StatModifierType']): print(' ',c['Id'],'r',c['Rarity'],'code',c['StatModifierType'],'FT',c['ModifierFormulaType'],c['ModifierBase'],c['ModifierMultiplier'],c['ModifierGrowth'],c.get('ModifierPower'),c.get('ModifierRound'),'MU',c['MineUnlock'],c['TargetIds'])
print('codes nm',C.Counter(c['StatModifierType'] for c in nm))
print('rarity all',C.Counter(c['Rarity'] for c in cards))
print('cuc',[(x['Rarity'],len(x['Duplicates']),len(x['SoftCurrency']),x['Duplicates'][:2],x['SoftCurrency'][:2]) for x in d['CardUpgradeCosts']])
