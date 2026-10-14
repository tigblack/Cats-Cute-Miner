import math, json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
def R(v, r):
    if r == 0: return v
    r = abs(r); q = v / r
    return math.copysign(math.floor(abs(q) + 0.5), q) * r
B,M,G,P,Rn=10,635,0.2,1.15,15
for x in (0,1,2,3): print(x,'alt (1+Gx)',R(B+M*(1+G*x)**P,Rn),'base (Gx)',R(B+M*(G*x)**P,Rn))
# D-6: elixir per threshold with arg = threshold level vs k
for g in d['GeneratorObjectives'][:4]:
    t=1;r=[]
    for k,c in enumerate(g['ObjectiveCount'][:6],1):
        t+=c; a=g['SoftCurrencyRewardBase']+g['SoftCurrencyRewardMultiplier']*k; b=g['SoftCurrencyRewardBase']+g['SoftCurrencyRewardMultiplier']*t
        r.append(round(b/a,1))
    print(g['GeneratorId'],r)
# late: last thresholds ratio
for gid in ('sunstone','garnet','amethyst','spawningcart'):
    g=[x for x in d['GeneratorObjectives'] if x['GeneratorId']==gid][0]; t=1; k=0
    for c in g['ObjectiveCount']: t+=c; k+=1
    a=g['SoftCurrencyRewardBase']+g['SoftCurrencyRewardMultiplier']*k; b=g['SoftCurrencyRewardBase']+g['SoftCurrencyRewardMultiplier']*t
    print(gid,'last k',k,'T',t,'ratio',round(b/a,1))
# D-1 alt: x instead of x-1
print('forge first upgrade alt', math.ceil(72*1.43))
# E-06/E-08
print(2400-60,2400-120)
# premium/rare gacha mults min
Z=d['Zones']; print('min mults', {k:min(z['RankMultipliers'][0][k] for z in Z) for k in Z[0]['RankMultipliers'][0] if 'Cards' in k})
print('z44',{k:v for k,v in Z[43]['RankMultipliers'][0].items() if 'Cards' in k})
