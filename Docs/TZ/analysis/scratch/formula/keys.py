import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
found=collections.defaultdict(set)
def walk(o,path):
    if isinstance(o,dict):
        for k,v in o.items():
            if any(s in k for s in ('Second','Core','Offline','Hour','Minute')):
                found[path+'.'+k].add(json.dumps(v)[:80])
            walk(v,path+'.'+k)
    elif isinstance(o,list):
        for x in o[:400]: walk(x,path+'[]')
for k,v in d.items():
    if k in ('Zones',): continue
    walk(v,k)
for k,v in sorted(found.items()):
    print(k, len(v), sorted(v)[:6])
print(json.dumps(d['RewardWheel'])[:1500])
print(json.dumps(d['CrushableRewards'])[:800])
