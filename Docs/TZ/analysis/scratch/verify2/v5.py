import sys; sys.path.insert(0,'..')
from common import *
for zn in ('zone1','zone5'):
    W,D,g,occ,objs=parse(Z[zn])
    print(zn,W,D)
    for r in range(D):
        row=''
        for c in range(W):
            i=occ.get((c,r))
            row+= (g[i].split(':')[0] if i is not None else '.')
        print(r,row)
    for o in objs:
        if o['k'] in 'rpec' and (zn=='zone1' or o['r']>=15): print(o['tok'],o['c'],o['r'],o['cells'])
