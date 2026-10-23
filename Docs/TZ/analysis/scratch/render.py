import json,sys
d=json.load(open(sys.argv[1]))
size={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: size[o['Id']]=(o['WidthCells'],o['DepthCells'])
def render(z):
    W,D=z['WidthCells'],z['DepthCells']
    g=[['.']*W for _ in range(D)]
    toks=z['Grid'].split(',')
    for i,t in enumerate(toks):
        if t=='.': continue
        c,r=i%W,i//W
        p=t.split(':'); k=p[0]
        if k in 'rsx':
            w,h=size[p[1]]
            ch={'r':'r','s':'S','x':'#'}[k]
            for dx in range(w):
                for dz in range(h):
                    g[r-dz][c+dx]=ch
            g[r][c]=ch.upper() if k!='x' else '#'
        else:
            g[r][c]=k.upper()
    return g
for zid in sys.argv[2:]:
    z=[x for x in d['Zones'] if x['Id']==zid][0]
    g=render(z)
    print(zid, z['DepthCells'])
    for r,row in enumerate(g): print('%2d'%r, ''.join(row))
