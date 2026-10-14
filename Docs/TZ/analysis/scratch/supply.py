import json,sys
from collections import Counter,defaultdict
d=json.load(open(sys.argv[1]))
g={x['Id']:x for x in d['Gacha']}
rk={r['Id']:r for r in d['Rocks']}
cards={c['Id']:c for c in d['Cards']}
cuc={c['Rarity']:c for c in d['CardUpgradeCosts']}
def need(cid,L):
    r=cards[cid]['Rarity']; return 1+sum(cuc[r]['Duplicates'][:L-1])
def add(sup,gid,usemin):
    x=g.get(gid)
    if not x or not x['GuaranteedCardIds']: return
    mins=x['GuaranteedCardMin']
    for i,cid in enumerate(x['GuaranteedCardIds']):
        n = mins[i] if (usemin and mins) else x['GuaranteedCardCounts'][i]
        sup[cid]+=n
for usemin in (True,False):
    sup=Counter(); print('=== exit rewards as', 'MIN' if usemin else 'COUNTS')
    for z in d['Zones'][:12]:
        toks=z['Grid'].split(',')
        exit_gid=None; reqs=[]
        rocks=Counter()
        for t in toks:
            p=t.split(':')
            if p[0]=='s': add(sup,p[3],usemin); reqs.append((p[4],int(p[5])))
            elif p[0]=='p': add(sup,p[3],usemin)
            elif p[0]=='e': exit_gid=p[3]
            elif p[0]=='r': rocks[p[1]]+=1
        for rid,n in rocks.items():
            cyc=rk[rid]['RewardCycle']
            if rid.startswith('rockonboarding'):
                for i in range(n):
                    e=cyc[i%len(cyc)]
                    if e['RewardType']==1: add(sup,e['DetailedType'],usemin)
        res=[(c,L,sup[c],need(c,L),'OK' if sup[c]>=need(c,L) else 'SHORT') for c,L in reqs]
        print(z['Id'], res)
        if exit_gid: add(sup,exit_gid,usemin)
