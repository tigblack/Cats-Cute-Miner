import json, sys, math
d=json.load(open(sys.argv[1]))
go={g['Id']:g for g in d['GeneratorObjectives']} if 'Id' in d['GeneratorObjectives'][0] else None
print('GO keys', list(d['GeneratorObjectives'][0].keys()))
print('MS keys', list(d['MineShafts'][0].keys()))
for m in d['MineShafts']+d['SpawningCart']:
    i=m.get('Id','spawningcart')
    b=m['UpgradeCostBase']; g=m['UpgradeCostGrowth']
    l1200=math.log10(b)+1199*math.log10(g) if b>0 else None
    # level where cost exceeds double max
    lim=None
    if b>0 and g>1:
        lim=1+ (308.25-math.log10(b))/math.log10(g)
    print(f"{i:14s} out={m['CurrencyOutputMultiplier']:<12g} costB={b:<10g} g={g} ft={m.get('UpgradeCostFormulaType')} log10cost@1200={l1200:.1f} overflowLvl~{lim:.0f}" )
print(json.dumps(d['BalanceProperties'][0].get('AntiCheatSettings')))
