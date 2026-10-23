import json,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); dims={}
for s in ('Rocks','MineShafts','Obstructions'):
    for o in d[s]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
ex=C.Counter(); exz=C.Counter(); gw=C.Counter(); gwz=set(); exfree=[]
for z in d['Zones']:
    W=7; t=z['Grid'].split(','); occ={}
    for i,x in enumerate(t):
        p=x.split(':'); c,r=i%W,i//W
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            for dx in range(w):
                for dz in range(h): occ[(c+dx,r-dz)]=p[0]+(':'+p[1] if p[0]=='r' and 'battle' in p[1] else '')
    row=tuple(occ.get((c,1),'.') for c in (2,3,4)); ex[row]+=1
    if 'x' in row: exz[z['Id']]+=1
    if all(v=='x' for v in row): exfree.append(z['Id'])
    for i,x in enumerate(t):
        if x.startswith('p:'):
            rp=i//W; row=tuple(occ.get((c,rp+1),'.') for c in range(1,6)); gw[row.count('x')]+=1
            if row.count('x')==5: gwz.add(z['Id'])
print('exit work row patterns',ex.most_common(10)); print('zones with x in exit work row',len(exz), list(exz)[:10]); print('all x',exfree)
print('gate work row: number of x among 5',gw, 'fully blocked',gwz)
