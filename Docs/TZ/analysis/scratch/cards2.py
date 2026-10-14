import json, sys, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
C=d['Cards']
print(collections.Counter((c['IsManager'],c['Rarity']) for c in C))
for c in C:
    if not c['IsManager']:
        print(c['Id'],'R',c['Rarity'],'SMT',c['StatModifierType'],'B',c['ModifierBase'],'M',c['ModifierMultiplier'],'G',c['ModifierGrowth'],'P',c['ModifierPower'],'Rd',c['ModifierRound'],'FT',c['ModifierFormulaType'],'T',c['TargetIds'],'MU',c['MineUnlock'],'SW',c['SortingWeight'])
print(collections.Counter(c['StatModifierType'] for c in C))
# manager MineUnlock
print('manager MineUnlock', sorted(c['MineUnlock'] for c in C if c['IsManager']))
