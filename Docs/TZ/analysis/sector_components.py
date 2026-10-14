import json, collections, sys
d=json.load(open(sys.argv[1]))
Z=d['Zones']
dims={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
compdist=collections.Counter(); bad=[]
sectors_per_zone=collections.Counter()
minfree=collections.Counter()
for z in Z:
    W,D=z['WidthCells'],z['DepthCells']; g=z['Grid'].split(',')
    o=set(); gates=[]; e=None
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            for dx in range(w):
                for dz in range(h): o.add((c+dx,r-dz))
        elif p[0]=='p': gates.append(r); [o.add((x,r)) for x in range(1,6)]
        elif p[0]=='e': [o.add((x,0)) for x in range(2,5)]
        elif p[0]=='c': o.add((c,r))
    bounds=[-1]+sorted(gates)+[D]
    sectors_per_zone[len(gates)+1]+=1
    for a,b in zip(bounds,bounds[1:]):
        free={(x,y) for x in range(W) for y in range(a+1,b) if (x,y) not in o}
        comps=0; seen=set()
        for s in free:
            if s in seen: continue
            comps+=1; st=[s]; seen.add(s)
            while st:
                x,y=st.pop()
                for q in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                    if q in free and q not in seen: seen.add(q); st.append(q)
        compdist[comps]+=1
        if comps!=1: bad.append((z['Id'],a,b,comps))
print('free components per sector',compdist)
print('bad',bad[:10])
print('sectors per zone',sorted(sectors_per_zone.items()))
