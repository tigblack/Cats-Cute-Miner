import json, collections, sys
d=json.load(open(sys.argv[1]))
Z=d['Zones']
dims={}
kind={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
prow_edges=collections.Counter(); erow=collections.Counter()
pockets_total=0; zones_with_pockets=[]
unreach_targets=[]
for z in Z:
    W,D=z['WidthCells'],z['DepthCells']; g=z['Grid'].split(',')
    o={}; sp=[]; objs=[]
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            cells=[(c+dx,r-dz) for dx in range(w) for dz in range(h)]
            for q in cells: o[q]=(p[0],i)
            objs.append((p[0],i,cells,t))
        elif p[0] in 'pec': sp.append((p[0],c,r,t,i))
    for k,c,r,t,i in sp:
        if k=='p':
            prow_edges[(o.get((0,r),('-',))[0],o.get((6,r),('-',))[0])]+=1
            cells=[(x,r) for x in range(1,6)]
        elif k=='e':
            erow[tuple(o.get((x,0),('-',))[0] for x in range(7))]+=1
            cells=[(x,0) for x in range(2,5)]
        else: cells=[(c,r)]
        for q in cells:
            if q in o: print('special overlaps footprint',z['Id'],k,q,o[q])
            o[q]=(k,i)
        objs.append((k,i,cells,t))
    # flood fill from c across free cells, treating rocks/shafts/gates as walls; then iteratively remove minable (r,s) objects adjacent to reached region; gates p opened when adjacent; compute whether all s, p, e get reached
    crow=[s for s in sp if s[0]=='c'][0]
    free={(x,y) for x in range(W) for y in range(D) if (x,y) not in o}
    start=(crow[1],crow[2]+1) if (crow[1],crow[2]+1) in free else (0,crow[2])
    def flood(seeds,free):
        seen=set(seeds); st=list(seeds)
        while st:
            x,y=st.pop()
            for nx,ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                if (nx,ny) in free and (nx,ny) not in seen: seen.add((nx,ny)); st.append((nx,ny))
        return seen
    # sector determination: gate rows
    gates=sorted([s[2] for s in sp if s[0]=='p'],reverse=True)
    def sector(y):
        n=0
        for gr in gates:
            if y<gr: n+=1
        return n
    reached=flood([start],free)
    # pockets: free cells in sector 0 (start sector) not reached
    sec0free={q for q in free if sector(q[1])==0}
    pockets=sec0free-reached
    allpockets=set()
    # simulate clearing
    remaining={(k,i):cells for (k,i,cells,t) in objs if k in 'rspe' and 'rockbattlepasscurrency' not in t}
    done=set()
    changed=True
    while changed:
        changed=False
        for key,cells in list(remaining.items()):
            adj=any(((x+1,y) in reached) or ((x-1,y) in reached) or ((x,y+1) in reached) or ((x,y-1) in reached) for x,y in cells)
            if adj:
                done.add(key); del remaining[key]; changed=True
                if key[0] in 'rsp':
                    # cells become free (shafts stay, but adjacent work possible; treat shaft as not walkable)
                    if key[0]=='r':
                        for q in cells: free.add(q)
                    if key[0]=='p':
                        for q in cells: free.add(q)
                reached=flood(list(reached),free)
    # pockets reached later
    unreachable=[(k,i) for (k,i) in remaining]
    if unreachable: unreach_targets.append((z['Id'],unreachable[:5],len(unreachable)))
    if pockets:
        zones_with_pockets.append((z['Id'],len(pockets),sorted(pockets)[:6]))
        pockets_total+=len(pockets)
print('p row edges (col0,col6):',prow_edges)
print('exit row patterns:',erow.most_common(5))
print('zones with initial pockets in start sector:',len(zones_with_pockets),pockets_total)
for x in zones_with_pockets[:15]: print(' ',x)
print('zones with unreachable r/s/p/e under 4-neighbour clearing from start:',len(unreach_targets))
for x in unreach_targets[:15]: print(' ',x)
