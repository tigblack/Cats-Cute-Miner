import json,collections,math
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
Z={z['Id']:z for z in J['Zones']}
n=0;zs=set()
for z in J['Zones']:
    for i,t in enumerate(z['Grid'].split(',')):
        if t.startswith('r:rockbattlepasscurrency'): n+=1; zs.add(int(z['Id'][4:]))
        if z['Id']=='zone144' and i==0: print('z144 idx0',t)
print('bp rocks',n,min(zs),max(zs),len(zs))
BP=J['BalanceProperties'][0]
print({k:v for k,v in BP.items() if 'AntiCheat' in k or 'Max' in k})
H=J['MiningTargetHealths']; print(type(H), 'rockbattlepasscurrency' in (H if isinstance(H,dict) else {h['Id']:1 for h in H}))
# GeneratorObjectives
for g in J['GeneratorObjectives']:
    oc=g['ObjectiveCount']; s=0; th=[]
    for x in oc: s+=x; th.append(1+s)
    print(g['GeneratorId'],len(oc),sum(oc),oc[:4],th[:5],th[-1],all(t%10==0 for t in th), g['CoreCurrencyMultiplier'][:4], {k:v for k,v in g.items() if k.startswith('SoftCurrencyReward')}, round(sum(math.log10(m) for m in g['CoreCurrencyMultiplier'])))
