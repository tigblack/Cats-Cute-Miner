import json,sys
d=json.load(open(sys.argv[1]))
C={c['Id']:c for c in d['Cards']}
print({k:(C[k]['Rarity'],C[k]['MineUnlock'],C[k]['TargetIds']) for k in ('ca002','ca003','ca014','ca015','ca016')})
cu={c['Rarity'] if 'Rarity' in c else c.get('Id'):c for c in d['CardUpgradeCosts']}; print(list(d['CardUpgradeCosts'][0].keys())); print(d['CardUpgradeCosts'][0])
R={r['Id']:r for r in d['Rocks']}
print([ (r, R[r]['RewardCycle']) for r in R if 'mine3' in r])
G={g['Id']:g for g in d['Gacha']}; print(G['scg_m003_r01'])
z3=d['Zones'][2]['Grid'].split(','); print([t for t in z3 if 'gacha' in t])
