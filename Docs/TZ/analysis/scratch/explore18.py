import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
hp=d['MiningTargetHealths']
for r in d['Rocks']:
    if r['MimicId']:
        print(r['Id'],'->',r['MimicId'],'hp equal',hp[r['Id']]==hp[r['MimicId']])
# slot -> mimic mapping
G={g['Id']:g for g in d['Gacha']}
slot=collections.defaultdict(collections.Counter)
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0] in 'spe':
            gid=p[3] if p[0]!='s' else p[3]
            m=G[gid]['MimicId'] or gid
            slot[p[0]][m]+=1
print(dict(slot))
# spells id
print('Spells keys',list(d['Spells'][0].keys()))
# quests rank values etc
print('Quests sample', {k:v for k,v in d['Quests'][0].items()})
