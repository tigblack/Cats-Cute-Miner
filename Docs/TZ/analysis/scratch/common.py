import json, sys, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z={z['Id']:z for z in d['Zones']}
ZL=[Z['zone%d'%i] for i in range(1,145)]
dims={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
def parse(z):
    W,D=z['WidthCells'],z['DepthCells']; g=z['Grid'].split(',')
    occ={}; objs=[]
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            cells=[(c+dx,r-dz) for dx in range(w) for dz in range(h)]
        elif p[0]=='e': cells=[(x,r) for x in range(2,5)]
        elif p[0]=='p': cells=[(x,r) for x in range(1,6)]
        elif p[0]=='c': cells=[(c,r)]
        else: continue
        for q in cells: occ[q]=i
        objs.append(dict(k=p[0],i=i,c=c,r=r,tok=t,p=p,cells=cells))
    return W,D,g,occ,objs
