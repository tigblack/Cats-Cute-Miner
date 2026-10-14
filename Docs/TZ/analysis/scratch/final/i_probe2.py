import json,collections,math
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
cards=J['Cards']
print('SortingWeight values',collections.Counter(c['SortingWeight'] for c in cards).most_common(8))
byr=collections.defaultdict(list)
for c in cards: byr[c['Rarity']].append((c['MineUnlock'],c['Id'],c['StatModifierType']))
for r in sorted(byr): 
    l=sorted(byr[r]); print('rarity',r,len(l),'minUnlock',l[0][0],'unlocks',[x[0] for x in l])
# non-scripted gacha
for g in J['Gacha']:
    if not g['Id'].startswith('scg_'):
        print(g['Id'],g['GachaType'],g['BaseNumCards'],'U',g['UncommonWeight'],'R',g['RareWeight'],'L',g['LegendaryWeight'],'E',g['EventEpicWeight'],'Low',g['LowCardChanceMult'],'Mgr',g['ManagerChanceMult'],'soft',g['SoftCurrencyMin'],g['SoftCurrencyMax'],'gc',g['GuaranteedCardIds'])
