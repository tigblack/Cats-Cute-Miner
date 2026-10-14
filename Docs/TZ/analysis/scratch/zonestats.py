from common import *
import collections
few=[]; depth=[]; secs=[]; rows_per_sector=[]
for n,z in enumerate(ZL,1):
    W,D,g,occ,objs=parse(z)
    ns=sum(o['k']=='s' for o in objs)
    if n>=4 and ns<2: few.append((n,ns))
    gates=sorted(o['r'] for o in objs if o['k']=='p')
    c=[o for o in objs if o['k']=='c'][0]
    bounds=[-1]+gates+[D]
    rows_per_sector += [b-a-1 for a,b in zip(bounds,bounds[1:])]
    depth.append(D)
print('zones>=4 with <2 shafts:',few)
print('depth distribution', collections.Counter(depth).most_common(12))
print('rows per sector min/max', min(rows_per_sector), max(rows_per_sector), collections.Counter(rows_per_sector).most_common(10))
# shaft count per zone and first-sector shafts
cnt=collections.Counter()
for n,z in enumerate(ZL,1):
    W,D,g,occ,objs=parse(z)
    cnt[sum(o['k']=='s' for o in objs)]+=1
print('shafts per zone',sorted(cnt.items()))
# zones whose start sector has <2 shafts (deliveries gated by 2 shafts)
