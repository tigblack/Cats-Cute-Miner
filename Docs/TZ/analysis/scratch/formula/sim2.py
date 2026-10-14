# zones 1..12 with checkpoint bonuses (research guess: profit x2, hire cost /10, timer -60s, cap +1) - rough bot
import sim
from sim import *
def run2(zid, prof, maxT=20000, K=2):
    gold=25; cats=[]; n=0; forgeL=1; t=0; cap=10; disc=1; pm=1
    tg=zone_targets(zid)
    for x in tg: x['hp']=hp(x['id'],x['lvl']); x['done']=False
    shafts=[]; ev={}
    fr=lambda: 3*forgeL*thr_mult('spawningcart',forgeL)*pm/2
    sr=lambda s: MS[s['id']]['CurrencyOutputMultiplier']*s['L']*thr_mult(s['id'],s['L'])*pm/MS[s['id']]['GenerationDelaySecBase']
    rate=lambda: fr()+sum(sr(s) for s in shafts)
    while t<maxT:
        while True:
            opts=[]
            if len(cats)<cap: opts.append(('hire',ft1(0,2,4,n+prof['hire_off'])/disc))
            opts.append(('forge',ft2a(72,0,1.43,forgeL-1+prof['up_off'])))
            for s in shafts: opts.append(('shaft',ft2a(MS[s['id']]['UpgradeCostBase'],0,MS[s['id']]['UpgradeCostGrowth'],s['L']-1+prof['up_off']),s))
            o=min(opts,key=lambda o:o[1])
            if o[1]>gold: break
            gold-=o[1]
            if o[0]=='hire': cats.append(1+math.floor(ft0(0.4,0.125,0,n))); n+=1
            elif o[0]=='forge': forgeL+=1
            else: o[2]['L']+=1
        cats.sort(); m=True
        while m:
            m=False
            for i in range(len(cats)-1):
                if cats[i]==cats[i+1]: v=cats[i]; del cats[i:i+2]; cats.append(v+1); cats.sort(); m=True; break
        rem=[x for x in tg if not x['done']]
        if not rem: break
        order=sorted([x for x in rem if x['kind']!='e'],key=lambda x:x['hp'])+[x for x in rem if x['kind']=='e']
        cs=sorted(cats,reverse=True); i=0
        for x in order:
            for k in range(K):
                if i>=len(cs): break
                x['hp']-=ft1(0,1,2.1,cs[i]-prof['dmg_off']); i+=1
            if x['hp']<=0:
                x['done']=True
                if x['kind']=='r':
                    r=ROCKSEC[x['id']]
                    if r['CoreCurrencySecondsFormulaType']==3:
                        gold+=prof['ft3'](10,635,0.2,1.15,15,x['lvl']+prof['rock_off'])*rate()
                elif x['kind']=='s': shafts.append(dict(id=x['id'],L=1))
                elif x['kind']=='p': cap+=1; disc*=10; pm*=2; ev.setdefault('cp',[]).append(t)
            if i>=len(cs): break
        gold+=rate(); t+=1
    return dict(z=zid,T=t,hires=n,maxcat=max(cats) if cats else 0,ncat=len(cats),forgeL=forgeL,shafts=[(s['id'],s['L']) for s in shafts],cp=ev.get('cp'))
for name in ['P1 dmg2.1^L, FT3a x=L','P2 dmg2.1^(L-1), FT3a x=L','P4 dmg2.1^(L-1), FT3a x=L-1']:
    print('##',name)
    for i in range(1,13):
        print('  ',run2('zone%d'%i,P[name]))
