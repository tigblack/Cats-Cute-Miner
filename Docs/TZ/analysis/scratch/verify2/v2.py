import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z={z['Id']:z for z in d['Zones']}
print({k:v for k,v in Z['zone1'].items() if k!='Grid'})
print(Z['zone144']['RankMultipliers'])
g={x['Id']:x for x in d['Gacha']}
for k in ['GachaUncommon','GachaLegendary','GachaEpic','GachaRare','GachaRoyal','GachaMythical','GachaSilver','GachaGolden']:
    x=g[k]; print(k,x['GachaType'],x['BaseNumCards'],x['UncommonWeight'],x['RareWeight'],x['LegendaryWeight'],x['EventEpicWeight'],x['SoftCurrencyMin'],x['SoftCurrencyMax'])
print(d['CardUpgradeCosts'])
print(d['CheckPoint']); print(d['Exit']); print(d['Reinforcements']); print(d['SpawningCart']); print(d['Spells'])
print(d['Miners'])
