import json,sys,collections,math
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
GO={g['GeneratorId']:g for g in d['GeneratorObjectives']}
Z=d['Zones']
first={}
for zi,z in enumerate(Z):
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='s' and p[1] not in first: first[p[1]]=zi+1
cards={}
for c in d['Cards']:
    for t in c['TargetIds']: cards.setdefault(t,[]).append((c['Id'],c['Rarity'],c['MineUnlock']))
for m in d['MineShafts']+[dict(d['SpawningCart'][0],Id='spawningcart',WidthCells='-',DepthCells='-')]:
    keys=['GenerationDelaySecBase','GenerationDelaySecMultiplier','GenerationDelaySecGrowth','GenerationDelaySecFormulaType','CurrencyType','CurrencyOutputBase','CurrencyOutputMultiplier','CurrencyOutputGrowth','CurrencyOutputFormulaType','UpgradeCostBase','UpgradeCostMultiplier','UpgradeCostGrowth','UpgradeCostFormulaType']
    v=[m[k] for k in keys]
    lc=math.log10(m['UpgradeCostBase'])+1199*math.log10(m['UpgradeCostGrowth'])
    pm=sum(math.log10(x) for x in GO[m['Id']]['CoreCurrencyMultiplier'])
    print(m['Id'].ljust(12),m['WidthCells'],m['DepthCells'],v,'first zone',first.get(m['Id']),'cards',cards.get(m['Id']),'log10cost1200 %.0f'%lc,'log10prodMult %.0f'%pm)
print(list(d['Obstructions'][0].keys()))
print([(o['Id'],o['WidthCells'],o['DepthCells']) for o in d['Obstructions']])
