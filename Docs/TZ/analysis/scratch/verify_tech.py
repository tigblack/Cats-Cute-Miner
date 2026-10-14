import json, sys, math
P=sys.argv[1]
d=json.load(open(P))
for k in ['BalanceProperties','Reinforcements','SpawningCart','CheckPoint','Exit','Miners']:
    v=d.get(k); print(k, type(v).__name__, len(v) if hasattr(v,'__len__') else v)
print('GridRowRanks null', sum(1 for z in d['Zones'] if z['GridRowRanks'] is None))
print('StoreItems', len(d['StoreItems']), 'ProductMetadata null', sum(1 for s in d['StoreItems'] if s.get('ProductMetadata', 'MISSING') is None))
rb=d['MiningTargetHealths']['rockbasic']
kinds=[]
for i,v in enumerate(rb):
    if isinstance(v,float): kinds.append('f')
    elif abs(v)>2**63-1: kinds.append('L')
    elif abs(v)>2**53: kinds.append('5')
    else: kinds.append('i')
import itertools
pos=0
for k,g in itertools.groupby(kinds):
    n=len(list(g)); print(k, pos, pos+n-1); pos+=n
print('rb73',rb[73],'rb83',rb[83])
print('heliodor', [m['CurrencyOutputMultiplier'] for m in d['MineShafts'] if m['Id']=='heliodor'])
# crusher grids bytes
s=json.dumps(d['CrusherGrids'],separators=(',',':'))
raw=open(P,'rb').read()
print('crusher compact bytes', len(s), 'file', len(raw))
i=raw.find(b'"CrusherGrids"'); print('CrusherGrids at', i)
# ObjectiveCount sums and products
for g in d['GeneratorObjectives']:
    pr=sum(math.log10(x) for x in g['CoreCurrencyMultiplier'] if x>0)
    print(g['GeneratorId'], sum(g['ObjectiveCount']), len(g['ObjectiveCount']), round(pr,1))
print('Miners', {k:v for k,v in d['Miners'][0].items() if 'Delay' in k or 'Attack' in k or 'Crit' in k})
print('maxHP', max(max(t) for t in d['MiningTargetHealths'].values()))
print('all HP finite', all(math.isfinite(float(x)) for t in d['MiningTargetHealths'].values() for x in t), len(d['MiningTargetHealths']))
