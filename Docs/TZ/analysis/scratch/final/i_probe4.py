import json,collections,math
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
src=collections.defaultdict(set)
rocks={r['Id']:r for r in J['Rocks']}
# rocks per zone
rockzones=collections.defaultdict(set)
for zi,z in enumerate(J['Zones']):
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r': rockzones[p[1]].add(zi+1)
        if p[0] in 'spe' and len(p)>3: src['token:'+p[0]].add(p[3])
for rid,r in rocks.items():
    for e in r['RewardCycle']:
        if e.get('RewardType')==1:
            mz=min(rockzones[rid]) if rockzones[rid] else None
            src[e.get('DetailedType')].add(('rock',rid,mz))
fg=J['FreeGachaCycle'][0]['GachaIds']
print('free gacha non-scg', collections.Counter(g for g in fg if not g.startswith('scg')), 'first nonscg idx', next((i for i,g in enumerate(fg) if not g.startswith('scg')),None))
for d in J['Deliveries']:
    rm=d['RewardModel']; 
    if rm.get('RewardType')==1: print('delivery gacha',d['Id'],rm)
for k,v in src.items():
    if not k.startswith('token'): print(k, sorted(v,key=lambda x:(x[2] or 0))[:4])
print(J['Rocks'][0]['RewardCycle'][:2])
