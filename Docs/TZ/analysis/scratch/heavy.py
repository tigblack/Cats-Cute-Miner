import json, sys, collections
d=json.load(open(sys.argv[1]))
rows=[]
for z in d['Zones']:
    toks=z['Grid'].split(',')
    c=collections.Counter(t.split(':')[0] for t in toks)
    rows.append((z['Id'], z['DepthCells'], c['r'], c['s'], c['x'], c['p'], sum(v for k,v in c.items() if k!='.')))
rows.sort(key=lambda r:-r[6])
print('top by object count:', rows[:5])
print('deepest:', sorted(rows,key=lambda r:-r[1])[:3])
print('total objects all zones', sum(r[6] for r in rows))
bp=d['BalanceProperties'][0]
print({k:bp[k] for k in bp if 'Cap' in k or 'Unit' in k or 'Offline' in k or 'Idle' in k})
print('Zone keys', list(d['Zones'][0].keys()))
