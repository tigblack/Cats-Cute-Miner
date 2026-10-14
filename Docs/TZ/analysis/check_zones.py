import json, collections, sys
d=json.load(open(sys.argv[1]))
Z=d['Zones']
rocks={r['Id']:r for r in d['Rocks']}
shafts={r['Id']:r for r in d['MineShafts']}
obs={r['Id']:r for r in d['Obstructions']}
hp=d['MiningTargetHealths']
gacha={g['Id']:g for g in d['Gacha']}
cards={c['Id']:c for c in d['Cards']}
print('HP tables',len(hp), collections.Counter(len(v) for v in hp.values()))
print('HP keys',sorted(hp.keys()))
ws=set(z['WidthCells'] for z in Z); ds=[z['DepthCells'] for z in Z]
print('widths',ws,'depth min/max',min(ds),max(ds))
print('FogOfWar',collections.Counter(z['FogOfWarCheckpoint'] for z in Z),'GridRowRanks',collections.Counter(str(z['GridRowRanks']) for z in Z),'RM len',collections.Counter(len(z['RankMultipliers']) for z in Z))
print('DecoSeed',collections.Counter(z['DecoSeed'] for z in Z).most_common(5),'CrusherGridId',sorted(collections.Counter(z['CrusherGridId'] for z in Z).items())[:40])
toks=collections.Counter(); tokfields=collections.defaultdict(collections.Counter)
problems=[]
dirs={'+c-r':(1,-1),'+c+r':(1,1),'-c-r':(-1,-1),'-c+r':(-1,1)}
dirres={k:[0,0] for k in dirs}
specials=collections.defaultdict(list)
appA=[]
maxlvl=collections.defaultdict(int)
for zi,z in enumerate(Z):
    W,D=z['WidthCells'],z['DepthCells']
    g=z['Grid'].split(',')
    if len(g)!=W*D: problems.append((z['Id'],'len',len(g),W*D))
    cnt=collections.Counter()
    anchors=[]
    for i,t in enumerate(g):
        p=t.split(':'); k=p[0]; toks[k]+=1; tokfields[k][len(p)]+=1
        col,row=i%W,i//W
        cnt[k]+=1
        if k in 'rsx':
            if k=='r': o=rocks.get(p[1])
            elif k=='s': o=shafts.get(p[1])
            else: o=obs.get(p[1])
            if o is None: problems.append((z['Id'],i,'missing ref',t)); continue
            anchors.append((col,row,o['WidthCells'],o['DepthCells'],t))
            if k in 'rs':
                if p[1] not in hp: problems.append((z['Id'],i,'nohp',t))
                else:
                    lv=int(p[2]); maxlvl[p[1]]=max(maxlvl[p[1]],lv)
                    if not(1<=lv<=len(hp[p[1]])): problems.append((z['Id'],i,'hp range',t))
            if k=='s':
                if p[3] not in gacha: problems.append((z['Id'],i,'gacha',t))
                if p[4] not in cards: problems.append((z['Id'],i,'card',t))
        elif k in 'pec':
            specials[k].append((z['Id'],col,row,D,t))
            if k in 'pe':
                if p[1] not in hp: problems.append((z['Id'],i,'nohp',t))
                else:
                    lv=int(p[2]); maxlvl[p[1]]=max(maxlvl[p[1]],lv)
                    if not(1<=lv<=len(hp[p[1]])): problems.append((z['Id'],i,'hp range',t))
                if p[3] not in gacha: problems.append((z['Id'],i,'gacha',t))
            anchors.append((col,row,1,1,t))
    if cnt['e']!=1 or cnt['c']!=1: problems.append((z['Id'],'e/c',cnt['e'],cnt['c']))
    appA.append((z['Id'],W,D,cnt['r'],cnt['s'],cnt['p'],cnt['x']))
    for dn,(sx,sy) in dirs.items():
        occ={}
        ov=oob=0
        for (c,r,w,h,t) in anchors:
            if t[0] in 'pec': continue
            for dx in range(w):
                for dz in range(h):
                    cc=c+sx*dx; rr=r+sy*dz
                    if not(0<=cc<W and 0<=rr<D): oob+=1; continue
                    if (cc,rr) in occ: ov+=1
                    occ[(cc,rr)]=t
        dirres[dn][0]+=ov; dirres[dn][1]+=oob
print('tokens',toks); print('fieldcounts',{k:dict(v) for k,v in tokfields.items()})
print('problems',problems[:20],len(problems))
print('direction results (overlaps,oob)',dirres)
for k in 'pec':
    print(k,'cols',collections.Counter(s[1] for s in specials[k]),'rows',collections.Counter(s[2] for s in specials[k]).most_common(8))
print('c row vs depth', collections.Counter(s[3]-1-s[2] for s in specials['c']))
print('e row', collections.Counter((s[1],s[2]) for s in specials['e']))
for a in appA[:10]: print(a)
print('maxlvl',dict(maxlvl))
json.dump(appA,open('/tmp/cm_appA.json','w'))
