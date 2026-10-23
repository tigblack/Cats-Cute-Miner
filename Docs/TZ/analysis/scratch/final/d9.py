import json,re,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
raw=open(J).read()
# walk with object_pairs_hook keeping raw ints
big=[]
def walk(o,path):
    if isinstance(o,dict):
        for k,v in o.items(): walk(v,path+[k])
    elif isinstance(o,list):
        for i,v in enumerate(o): walk(v,path+[i])
    elif isinstance(o,int) and not isinstance(o,bool) and abs(o)>2**63-1: big.append(path)
d=json.loads(raw); walk(d,[])
print('ints>int64',len(big)); print(C.Counter(tuple(p[:2]) if p[0]!='MiningTargetHealths' else ('MTH',) for p in big))
print('MTH tables with big', len(set(p[1] for p in big if p[0]=='MiningTargetHealths')), len(d['MiningTargetHealths']))
print([p for p in big if p[0]!='MiningTargetHealths'][:5])
print('1e+200 literal' , '1e+200' in raw, re.findall(r'[0-9.]+[eE][+-]?[0-9]+',raw)[:10])
M=d['MiningTargetHealths']; print('lens',set(len(v) for v in M.values()))
for k,L in (('rockonboardingmine1',1),('rockonboardingmine1',2),('rockbasic',3),('amethyst',1),('checkpoint',5),('exit',1),('rockbasic',74),('sunstone',99)): print(k,L,M[k][L-1])
mx=max((max(v),k) for k,v in M.items()); print('max HP',mx)
he=[m for m in d['MineShafts'] if m['Id']=='heliodor'][0]; print('heliodor out',he['CurrencyOutputMultiplier'])
print('max output mult',max((m['CurrencyOutputMultiplier'],m['Id']) for m in d['MineShafts']))
# product of thresholds
for g in d['GeneratorObjectives']:
    p=sum(math.log10(x) for x in g['CoreCurrencyMultiplier'])
    if p>140: print('prodmult',g['GeneratorId'],round(p,1))
print('max prod', max((round(sum(math.log10(x) for x in g['CoreCurrencyMultiplier']),1),g['GeneratorId']) for g in d['GeneratorObjectives']))
# upgrade cost at level up to 1199 transitions
for m in d['MineShafts']+[dict(d['SpawningCart'][0],Id='spawningcart')]:
    lg=lambda L: math.log10(m['UpgradeCostBase'])+(L-1)*math.log10(m['UpgradeCostGrowth'])
    over=[L for L in range(1,1200) if lg(L)>math.log10(1.7976931348623157e308)]
    print(m['Id'],'max cost 1e%.1f'%lg(1199),'over double from L',over[0] if over else None)
