import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
lv=collections.Counter(); minl={}
dims={}
for sec in ['Rocks','MineShafts','Obstructions']:
    for o in d[sec]: dims[o['Id']]=(o['WidthCells'],o['DepthCells'])
chk=collections.Counter()
for zi,z in enumerate(Z):
    W,D=7,z['DepthCells']; g=z['Grid'].split(',')
    occ={}
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0] in 'rsp e'.replace(' ','') and p[0]!='x':
            L=int(p[2]); minl[p[0]]=min(minl.get(p[0],99),L)
        if p[0] in 'rsx':
            w,h=dims[p[1]]
            for dx in range(w):
                for dz in range(h): occ[(c+dx,r-dz)]=p[0]
    for i,t in enumerate(g):
        p=t.split(':'); c,r=i%W,i//W
        if p[0]=='p':
            chk['p_left_obs']+= occ.get((0,r))=='x'; chk['p_right_obs']+= occ.get((6,r))=='x'
            chk['p_mid_free']+= all((cc,r) not in occ for cc in range(1,6)); chk['p_total']+=1
        if p[0]=='e':
            chk['e_l']+=occ.get((1,0))=='x'; chk['e_r']+=occ.get((5,0))=='x'; chk['e_mid_free']+=all((cc,0) not in occ for cc in range(2,5)); chk['e_total']+=1
            # working row under e
            chk['e_row1_any_free']+= any((cc,1) not in occ for cc in range(2,5))
print(minl); print(chk)
