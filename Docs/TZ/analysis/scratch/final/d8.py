import json,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); G={g['Id']:g for g in d['Gacha']}; Rk={r['Id']:r for r in d['Rocks']}
cards={c['Id']:c for c in d['Cards']}; cuc={x['Rarity']:x for x in d['CardUpgradeCosts']}
z=d['Zones'][0]; W=7; t=z['Grid'].split(',')
for r in range(z['DepthCells']): print(r, ' '.join((t[r*W+c] if t[r*W+c]!='.' else '.').ljust(1)[:14].ljust(14) for c in range(W)))
z3=d['Zones'][2]['Grid'].split(','); print([x for x in z3 if x[0] in 'sep'])
need=lambda cid,L: 1+sum(cuc[cards[cid]['Rarity']]['Duplicates'][:L-1])
have=C.Counter(); allg=set()
for g in G.values():
    if g['Id'].startswith('scg_'): allg|=set(g['GuaranteedCardIds'])
firstmiss=None
for z in d['Zones']:
    n=int(z['Id'][4:]); toks=z['Grid'].split(',')
    req={}
    for x in toks:
        p=x.split(':')
        if p[0]=='s': req[p[4]]=max(req.get(p[4],0),int(p[5]))
    # requirement check BEFORE adding this zone's guaranteed? add this zone's all (upper bound)
    gain=C.Counter()
    for x in toks:
        p=x.split(':'); gid=None
        if p[0]=='s': gid=p[3]
        elif p[0] in 'pe': gid=p[3]
        elif p[0]=='r':
            for e in Rk[p[1]]['RewardCycle']:
                if e['RewardType']==1 and e['DetailedType'].startswith('scg_'): gain[e['DetailedType']]+=1
        if gid and gid.startswith('scg_'): gain[gid]+=1
    havez=have.copy()
    for gid,k in gain.items():
        g=G[gid]; cnt=g['GuaranteedCardMin'] or g['GuaranteedCardCounts']
        if gid.endswith('_exit'): continue  # exit reward comes after zone
        for c,q in zip(g['GuaranteedCardIds'],cnt): havez[c]+=q*k
    short=[(c,L,havez[c],need(c,L)) for c,L in req.items() if havez[c]<need(c,L)]
    miss=[c for c in req if c not in allg]
    if n<=16: print(z['Id'],'short',short,'miss',miss)
    if miss and not firstmiss: firstmiss=(n,miss)
    for gid,k in gain.items():
        g=G[gid]; cnt=g['GuaranteedCardMin'] or g['GuaranteedCardCounts']
        for c,q in zip(g['GuaranteedCardIds'],cnt): have[c]+=q*k
print('firstmiss',firstmiss)
print('MineUnlock ca008',cards['ca008']['MineUnlock'])
