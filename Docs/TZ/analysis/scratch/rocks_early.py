from common import *
R={r['Id']:r for r in d['Rocks']}
print(list(R['rockbasic'].keys()))
for rid in ['rockonboardingmine1','rockonboardingmine2','rockonboardingmine3','rocksoftcurrencysmall','rockonboardinggachawoodenmine3']:
    r=R[rid]
    print(rid, {k:v for k,v in r.items() if k!='RewardCycle'})
    print('   cycle:', [(c.get('RewardType'),c.get('DetailedType'),c.get('Quantity')) for c in r['RewardCycle']][:40])
G={g['Id']:g for g in d['Gacha']}
for gid in sorted(G):
    if gid.startswith('scg_m00'): 
        g=G[gid]; print(gid, g['MimicId'], g['GuaranteedCardIds'], g['GuaranteedCardCounts'], g['SoftCurrencyMin'])
C={c['Id']:c for c in d['Cards']}
print(list(C['ca001'].keys()))
for cid in ['ca001','ca002','ca003','ca014','ca015','ca026','ca029','ca034','ca038']:
    print(cid,{k:v for k,v in C[cid].items() if v not in (None,[],'')})
print(d['CardUpgradeCosts'][0])
