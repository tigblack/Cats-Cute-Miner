import json,re,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
gc=C.Counter(); g0=[]; objs={}; depth={}; rb={}; bpz=C.Counter(); crow=C.Counter(); maxlvl=0; toks=C.Counter(); nf=C.defaultdict(set)
shcol=C.Counter(); allz=[]
examples=['r:rockonboardingmine1:2','s:amethyst:1:scg_m002_s01:ca002:1','x:block2x3vR','p:checkpoint:5:scg_m005_c01','e:exit:1:scg_m001_exit','c:spawningcart:1']
found={e:False for e in examples}
for z in Z:
    W,D=z['WidthCells'],z['DepthCells']; t=z['Grid'].split(',')
    assert len(t)==W*D
    n=int(z['Id'][4:]); depth[n]=D
    k=C.Counter(x.split(':')[0] for x in t); gc[k['p']]+=1
    if k['p']==0: g0.append(n)
    objs[n]=sum(v for kk,v in k.items() if kk!='.')
    for i,x in enumerate(t):
        p=x.split(':'); nf[p[0]].add(len(p))
        if x in found: found[x]=True
        if p[0]=='r' and p[1]=='rockbasic': rb[n]=rb.get(n,0)+1
        if p[0]=='r' and p[1]=='rockbattlepasscurrency': bpz[n]+=1
        if p[0] in 'rspe': maxlvl=max(maxlvl,int(p[2]))
        if p[0]=='c': crow[D-i//W]+=1; cz=(n,D-i//W, i%W, p[2])
        if p[0]=='s': shcol[i%W]+=1
    toks.update(k)
print('gates',sorted(gc.items()),'zero',g0, 'sectors',sum((g+1)*c for g,c in gc.items()), 'gates total', sum(g*c for g,c in gc.items()))
print('nfields',dict(nf)); print('examples',found)
print('maxlvl',maxlvl)
mo=max(objs.values()); print('max objs',mo,[n for n in objs if objs[n]==mo], 'top5', sorted(objs.items(),key=lambda x:-x[1])[:5])
md=max(depth.values()); print('max depth',md,[n for n in depth if depth[n]==md], 'min depth', min(depth.values()))
mrb=max(rb.values()); print('rockbasic max',mrb,[n for n in rb if rb[n]==mrb], 'first',min(rb))
print('bp rocks',sum(bpz.values()),'zones',len(bpz),'first',min(bpz),'last',max(bpz),'max per zone',max(bpz.values()))
print('zones >=7 without bp', [n for n in range(7,145) if n not in bpz][:20], len([n for n in range(7,145) if n not in bpz]))
print('crow (Depth - row)',crow)
print('shaft anchor cols',shcol, 'total s',toks['s'])
print('fields set', {f:set(str(z[f]) for z in Z) for f in ('FogOfWarCheckpoint','DecoSeed','GridRowRanks')}, set(len(z['RankMultipliers']) for z in Z))
print('w', set(z['WidthCells'] for z in Z))
