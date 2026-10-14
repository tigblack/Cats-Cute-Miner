import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
print(list(d['Rocks'][0].keys()))
use=collections.defaultdict(list)
for zi,z in enumerate(Z):
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r': use[p[1]].append(zi+1)
for r in d['Rocks']:
    rc=r['RewardCycle']
    types=collections.Counter((x.get('RewardType'),x.get('DetailedType'),x.get('Quantity')) for x in rc)
    u=use[r['Id']]
    print(r['Id'], r['WidthCells'],r['DepthCells'],'mimic',r.get('MimicId'),'cyc',len(rc),'uses',len(u),'zones',(min(u),max(u)) if u else None, dict(list(types.items())[:6]))
print(json.dumps(d['Rocks'][0]['RewardCycle'][:3]))
print({k:v for k,v in d['Rocks'][0].items() if k!='RewardCycle'})
