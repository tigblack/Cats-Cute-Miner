import json,sys,collections,math
d=json.load(open(sys.argv[1]))
Z=d['Zones']
def toks(z): return z['Grid'].split(',')
z1=Z[0]; W=z1['WidthCells']; t=toks(z1)
for r in range(z1['DepthCells']):
    print(r, ' '.join((x[:1] if x=='.' else x.split(':')[0]+(x.split(':')[1][:8] if x[0]!='x' else x.split(':')[1][:8])).ljust(10) for x in t[r*W:(r+1)*W]))
# counts
z2=Z[1]; c=collections.Counter(x.split(':')[1] for x in toks(z2) if x.startswith('r:')); print('zone2 rocks',c)
R={r['Id']:r for r in d['Rocks']}
for rid in ('rockonboardingmine1','rockonboardingmine2'):
    print(rid, [(e['RewardType'] if 'RewardType' in e else e.get('RewardModel')) for e in R[rid]['RewardCycle']][:3], R[rid]['RewardCycle'][:10])
# objects per zone
mx=max(((sum(1 for x in toks(z) if x[0] in 'rsxpec'),z['Id']) for z in Z)); print('max objs',mx)
mx=max(((sum(1 for x in toks(z) if x[0] in 'rsp' ),z['Id']) for z in Z)); print('max rsp',mx)
bpz=[z['Id'] for z in Z for x in toks(z) if x.startswith('r:rockbattlepasscurrency')]; print('bp stones',len(bpz),len(set(bpz)),bpz[:3],bpz[-1])
rb=R['rockbasic']; x=1
print('D2 alt', round(rb['CoreCurrencySecondsBase']+rb['CoreCurrencySecondsMultiplier']*(1+rb['CoreCurrencySecondsGrowth']*1)**rb['CoreCurrencySecondsPower']))
