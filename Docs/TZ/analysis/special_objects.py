import json, collections, sys
d=json.load(open(sys.argv[1]))
Z=d['Zones']
dims={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
def occ(z):
    W,D=z['WidthCells'],z['DepthCells']; g=z['Grid'].split(',')
    o={}; sp=[]
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            for dx in range(w):
                for dz in range(h): o[(c+dx,r-dz)]=p[0]
        elif p[0] in 'pec': sp.append((p[0],c,r,t))
    return W,D,o,sp
free=collections.defaultdict(lambda: None)
for z in Z:
    W,D,o,sp=occ(z)
    for k,c,r,t in sp:
        fs=set()
        for rr in range(D):
            for cc in range(W):
                if (cc,rr) not in o: fs.add((cc-c,rr-r))
        # restrict to window
        fs={x for x in fs if -3<=x[1]<=3}
        free[k]=fs if free[k] is None else free[k]&fs
for k in 'epc':
    print(k,'always-free rel offsets (dx,drow):',sorted(free[k],key=lambda x:(x[1],x[0])))
# zone1 show map
def show(z):
    W,D,o,sp=occ(z); g=z['Grid'].split(',')
    for r in range(D):
        line=''
        for c in range(W):
            t=g[r*W+c].split(':')
            if t[0]!='.': line+=t[0].upper()
            elif (c,r) in o: line+=o[(c,r)]
            else: line+='.'
        print(f'{r:3d} {line}')
for zid in ['zone1','zone2','zone5','zone10']:
    z=[q for q in Z if q['Id']==zid][0]; print(zid); show(z)
