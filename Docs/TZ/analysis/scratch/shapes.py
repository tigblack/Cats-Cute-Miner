import json, sys, collections
P=sys.argv[1]
dups=[]
def hook(pairs):
    seen=set()
    for k,_ in pairs:
        if k in seen: dups.append(k)
        seen.add(k)
    return dict(pairs)
d=json.load(open(P), object_pairs_hook=hook)
print('duplicate keys:', len(dups), dups[:10])
for k,v in d.items():
    t=type(v).__name__
    extra=''
    if isinstance(v,list): extra=f'len={len(v)} elem={type(v[0]).__name__ if v else None}'
    if isinstance(v,dict): extra=f'keys={len(v)} sample={list(v)[:3]}'
    print(f'{k:32s} {t:6s} {extra}')
# find dict-shaped objects (keys that are IDs) deeper: objects with >20 keys
big=[]
def walk(o,p,depth=0):
    if isinstance(o,dict):
        if len(o)>15 and depth>0: big.append((p,len(o),list(o)[:4]))
        for k,v in o.items(): walk(v,p+'.'+k,depth+1)
    elif isinstance(o,list):
        for i,v in enumerate(o[:1]): walk(v,p+'[0]',depth+1)
walk(d,'$')
print('objects with many keys:')
for b in big[:30]: print(' ',b)
# null fields count by path
nulls=collections.Counter()
import re
def wn(o,p):
    if isinstance(o,dict):
        for k,v in o.items(): wn(v,p+'.'+k)
    elif isinstance(o,list):
        for v in o: wn(v,p+'[]')
    elif o is None: nulls[p]+=1
wn(d,'$')
print('null-valued paths (top 25):')
for k,v in nulls.most_common(25): print(' ',v,k)
