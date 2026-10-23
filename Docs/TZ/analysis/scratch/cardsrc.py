import json, sys, collections, re
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
cards={c['Id']:c for c in d['Cards']}
where=collections.defaultdict(set)
def walk(o,path):
    if isinstance(o,dict):
        for k,v in o.items(): walk(v,path+'.'+k if path else k)
    elif isinstance(o,list):
        for x in o: walk(x,path)
    elif isinstance(o,str):
        for m in re.findall(r'\bca0\d\d\b',o):
            where[m].add(path.split('.')[0])
for k,v in d.items():
    if k in ('Cards',): continue
    walk(v,k)
for cid,c in sorted(cards.items()):
    print(cid,'R',c['Rarity'],'Mgr',c['IsManager'],'SMT',c['StatModifierType'],sorted(where.get(cid,[])))
# RewardType values across all reward models
rt=collections.Counter()
def walk2(o,path):
    if isinstance(o,dict):
        if 'RewardType' in o and 'DetailedType' in o:
            rt[(path.split('.')[0],o['RewardType'],o['DetailedType'] if not o['DetailedType'].startswith('ca0') else 'caXXX')]+=1
        for k,v in o.items(): walk2(v,path+'.'+k if path else k)
    elif isinstance(o,list):
        for x in o: walk2(x,path)
for k,v in d.items(): walk2(v,k)
agg=collections.defaultdict(set)
for (sec,t,dt),n in rt.items():
    agg[t].add(dt if t!=1 else 'gacha')
for t in sorted(agg): print('RewardType',t,sorted(agg[t])[:20])
secs=collections.defaultdict(set)
for (sec,t,dt),n in rt.items(): secs[t].add(sec)
for t in sorted(secs): print('RewardType',t,'in',sorted(secs[t]))
