import json,sys
d=json.load(open(sys.argv[1]))
H=d['MiningTargetHealths']
print(type(H), (list(H.keys())[:5] if isinstance(H,dict) else json.dumps(H[0])[:300]))
def tab(i):
    if isinstance(H,dict): return H[i]
    for x in H:
        if x.get('Id')==i or x.get('TargetId')==i: return x
t=tab('rockbasic'); print(json.dumps(t)[:400])
