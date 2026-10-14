from common import *
for zid in ['zone1','zone2','zone3','zone4']:
    z=Z[zid]; W,D,g,occ,objs=parse(z)
    print(zid, W, D, {k:v for k,v in z.items() if k not in ('Grid','RankMultipliers')})
    for o in objs:
        if o['k']!='x': print('  ',o['k'],(o['c'],o['r']),o['tok'])
