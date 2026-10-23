import json,sys,math
d=json.load(open(sys.argv[1]))
go={g['GeneratorId']:g for g in d['GeneratorObjectives']}
for gid in ('onyx','garnet','sunstone'):
    t=1;o=[]
    for c in go[gid]['ObjectiveCount'][:3]: t+=c;o.append(t)
    print(gid,o)
res=[]
for m in d['MineShafts']:
    lg=math.log10(m['UpgradeCostBase'])+(1199)*math.log10(m['UpgradeCostGrowth'])
    over=None
    for L in range(1,1200):
        if math.log10(m['UpgradeCostBase'])+(L-1)*math.log10(m['UpgradeCostGrowth'])>math.log10(1.797e308): over=L;break
    res.append((m['Id'],round(lg,1),over))
print([r for r in res if r[2]]); print(len([r for r in res if r[2]]))
print(sorted(r[1] for r in res)[-1])
print('order', [m['Id'] for m in d['MineShafts']])
