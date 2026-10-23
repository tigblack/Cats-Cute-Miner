import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
z=d['Zones'][0]
print({k:(v if k!='Grid' else v[:80]) for k,v in z.items()})
print('zone144', {k:(v if k!='Grid' else v[:80]) for k,v in d['Zones'][-1].items()})
print('zone ids order', [q['Id'] for q in d['Zones'][:12]])
# RM keys var
for k in d['Zones'][0]['RankMultipliers'][0]:
    vals=[q['RankMultipliers'][0][k] for q in d['Zones']]
    print(k, vals[:12], '...', vals[-3:], 'distinct', len(set(map(str,vals))))
print('CrusherGridId', collections.Counter(q.get('CrusherGridId') for q in d['Zones']).most_common(40))
print('CrusherGrids ids', [c.get('Id') for c in d['CrusherGrids']][:40])
print('CrusherGrid keys', list(d['CrusherGrids'][0].keys()))
