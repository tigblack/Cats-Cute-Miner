import json, sys, collections, re
P=sys.argv[1]
# parse ints as python int to keep exactness
d=json.load(open(P), parse_int=int, parse_float=float)
cnt=collections.Counter(); cnt53=collections.Counter(); ex={}
def norm(path):
    return re.sub(r'\[\d+\]','[]',path)
def walk(o,p):
    if isinstance(o,dict):
        for k,v in o.items(): walk(v,p+'.'+k)
    elif isinstance(o,list):
        for i,v in enumerate(o): walk(v,p+f'[{i}]')
    elif isinstance(o,int) and not isinstance(o,bool):
        if abs(o)>2**63-1: cnt[norm(p)]+=1; ex.setdefault(norm(p),(p,o))
        if abs(o)>2**53: cnt53[norm(p)]+=1
walk(d,'$')
print('> int64 by path:')
for k,v in cnt.most_common(): print(' ',v,k,ex[k])
print('> 2^53 by path:')
for k,v in cnt53.most_common(): print(' ',v,k)
# also floats huge
cntf=collections.Counter()
def walkf(o,p):
    if isinstance(o,dict):
        for k,v in o.items(): walkf(v,p+'.'+k)
    elif isinstance(o,list):
        for i,v in enumerate(o): walkf(v,p+f'[{i}]')
    elif isinstance(o,float) and abs(o)>1e18: cntf[norm(p)]+=1
walkf(d,'$')
print('floats >1e18 by path:')
for k,v in cntf.most_common(): print(' ',v,k)
