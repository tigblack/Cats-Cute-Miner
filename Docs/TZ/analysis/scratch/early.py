import json,sys
from collections import Counter
d=json.load(open(sys.argv[1]))
g={x['Id']:x for x in d['Gacha']}
rk={r['Id']:r for r in d['Rocks']}
for z in d['Zones'][:12]:
    toks=z['Grid'].split(',')
    out=[]
    rc=Counter()
    for i,t in enumerate(toks):
        if t=='.' : continue
        p=t.split(':')
        if p[0] in 's p e c':
            out.append((i%7,i//7,t))
        if p[0]=='r': rc[p[1]+':'+p[2]]+=1
    print(z['Id'], z['DepthCells'], out)
    print('   rocks', dict(rc))
    for (_,_,t) in out:
        p=t.split(':')
        rid = p[3] if p[0] in 'spe' and len(p)>3 else None
        if rid and rid in g:
            x=g[rid]; print('     ',rid, x['GuaranteedCardIds'], x['GuaranteedCardMin'], x['GuaranteedCardCounts'], x['SoftCurrencyMin'], x['SoftCurrencyMax'], 'CMM',x['CardMultMax'], 'Base',x['BaseNumCards'])
