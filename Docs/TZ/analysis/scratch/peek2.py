import json,sys
d=json.load(open(sys.argv[1]))
def short(o,n=2500):
    return json.dumps(o,ensure_ascii=False)[:n]
ms={m['Id']:m for m in d['MineShafts']}
print('MS amethyst', short(ms['amethyst']))
print('MS ids', list(ms))
rk={r['Id']:r for r in d['Rocks']}
print('Rock ids', list(rk))
r=dict(rk['rockbasic']); rc=r.pop('RewardCycle'); print('rockbasic', short(r)); print('cycle first 12', short(rc[:12]))
r=dict(rk['rockonboardingmine1']); print('onb1', short(r))
print('Obstr sample', short(d['Obstructions'][0]))
z=d['Zones'][0]; print('Zone keys', [k for k in z]); zz={k:v for k,v in z.items() if k!='Grid'}; print(short(zz,3000))
