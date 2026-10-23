import json,collections,math
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
G={g['Id']:g for g in J['Gacha']}
bp=J['BalanceProperties'][0]
cards=J['Cards']
def cand(r,rank): return [c for c in cards if c['Rarity']==r and c['MineUnlock']<=rank]
split={1:bp['CommonStackSplitAt'],2:bp['UncommonStackSplitAt'],3:bp['RareStackSplitAt'],4:bp['EventEpicStackSplitAt'],5:None}
cls={0:'Normal',1:'Premium',3:'Rare'}
half=[];short=[]
for zi,z in enumerate(J['Zones']):
    rank=zi+1; rm=z['RankMultipliers'][0]
    for gid,g in G.items():
        if g['GachaType']==2 or g['BaseNumCards']==0: continue
        m=rm['GachaCardsMult'+cls[g['GachaType']]]
        v=g['BaseNumCards']*m
        if abs(v-math.floor(v)-0.5)<1e-9: half.append((rank,gid,v))
        N=round(v)
        w={2:g['UncommonWeight'],3:g['RareWeight'],4:g['EventEpicWeight'],5:g['LegendaryWeight']}
        cnt={r:(math.ceil(N/w[r]) if w[r] else 0) for r in w}  # max possible
        cnt[1]=N-sum(math.floor(N/w[r]) for r in w if w[r])
        for r,n in cnt.items():
            if n<=0: continue
            sp=split[r]
            if sp is None: need=1 if n>0 else 0; 
            else:
                need=1
                for j,t in enumerate(sp):
                    if n>=t: need=bp['CardStackCounts'][j]
            k=len(cand(r,rank))
            if need>k: short.append((rank,gid,r,n,need,k))
print('half cases',len(half),half[:10])
print('stack>candidates cases',len(short))
seen=set()
for s in short:
    key=(s[1],s[2])
    if key not in seen: seen.add(key); print(s)
print('---reachable')
fg=J['FreeGachaCycle'][0]['GachaIds']
print('Royal idx',[i for i,g in enumerate(fg) if g=='GachaRoyal'],'Legendary idx',[i for i,g in enumerate(fg) if g=='GachaLegendary'],'Golden idx',[i for i,g in enumerate(fg) if g=='GachaGolden'])
minr={'GachaWooden':4,'GachaIron':4,'GachaForged':4,'GachaSilver':4,'GachaGolden':4,'GachaRoyal':4,'GachaMythical':100,'GachaLegendary':4,'GachaCrusher':11}
for s in short:
    if s[1] in minr and s[0]>=minr[s[1]]: print(s)
