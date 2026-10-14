import json, sys, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
print(json.dumps(Z[0]['RankMultipliers'],indent=0)[:3000])
keys=list(Z[0]['RankMultipliers'][0].keys())
print(keys)
for k in keys:
    vals=[z['RankMultipliers'][0].get(k) for z in Z]
    uniq=len(set(json.dumps(v) for v in vals))
    print(k, 'z1',json.dumps(vals[0])[:80],'z5',json.dumps(vals[4])[:80],'z10',json.dumps(vals[9])[:80],'z144',json.dumps(vals[143])[:80],'uniq',uniq)
print('CrusherGridId', collections.Counter(z['CrusherGridId'] for z in Z).most_common(40))
cg={c['Id'] for c in d['CrusherGrids']}
print('crusher ids',sorted(cg))
print('zones crusher ids not in grids',{z['CrusherGridId'] for z in Z}-cg)
print('first zones crusher', [(z['Id'],z['CrusherGridId']) for z in Z[:14]])
