import sys
sys.path.insert(0,'/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/TZ/analysis/scratch')
from common import *
depths=collections.Counter(z['DepthCells'] for z in ZL); print('depth', sorted(depths.items()))
sec_h=collections.Counter(); minfree=[]; overlap=0
for z in ZL:
    W,D,g,occ,objs=parse(z)
    c=[o for o in objs if o['k']=='c'][0]
    cf=[(1,c['r']),(2,c['r']),(1,c['r']-1),(2,c['r']-1)]
    for q in cf:
        if q in occ and occ[q]!=c['i']: overlap+=1
    gates=sorted(o['r'] for o in objs if o['k']=='p')
    bounds=[0]+[r for r in gates]+[D]
    # sector heights: between boundaries
    rows=sorted(gates)
    edges=[-1]+rows+[D]
    hs=[edges[i+1]-edges[i]-1 for i in range(len(edges)-1)]
    for h in hs: sec_h[h]+=1
    # start sector: rows below last gate
    lo=(rows[-1]+1) if rows else 0
    free=sum(1 for y in range(lo,D) for x in range(W) if (x,y) not in occ and (x,y) not in cf)
    minfree.append((free,z['Id']))
print('c2x2 overlaps',overlap)
print('sector heights',sorted(sec_h.items()))
print('min free start sector (initial, excl c2x2)',sorted(minfree)[:5])
