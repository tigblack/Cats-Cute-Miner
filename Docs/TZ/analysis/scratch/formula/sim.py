# Rough greedy-bot simulation of zone1..3 under candidate working profiles (NOT original algorithms)
from fprof import *
import math, sys
Z={z['Id']:z for z in d['Zones']}
ROCKSEC={r['Id']:r for r in d['Rocks']}
MS={m['Id']:m for m in d['MineShafts']}
def zone_targets(zid):
    g=Z[zid]['Grid'].split(','); out=[]
    for t in g:
        p=t.split(':')
        if p[0]=='r': out.append(dict(kind='r',id=p[1],lvl=int(p[2])))
        elif p[0]=='s': out.append(dict(kind='s',id=p[1],lvl=int(p[2]),card=p[4],clvl=int(p[5])))
        elif p[0]=='e': out.append(dict(kind='e',id='exit',lvl=int(p[2])))
        elif p[0]=='p': out.append(dict(kind='p',id='checkpoint',lvl=int(p[2])))
    return out
def run(zid, prof, cards=None, maxT=7200, K=2, verbose=False):
    cards=cards or {}
    dmgoff=prof['dmg_off']; gold=d['BalanceProperties']['StartingCoreCurrency'] if isinstance(d['BalanceProperties'],dict) else 25
    cats=[]; n=0; forgeL=1; t=0; log=[]
    tg=zone_targets(zid)
    for x in tg:
        x['hp']=hp(x['id'],x['lvl']); x['done']=False
    shafts=[]  # active
    rockgold=0; firsts={}
    def forge_rate():
        m=thr_mult('spawningcart',forgeL)*prof['card_mult'](cards.get('ca001',0))
        return 3*forgeL*m/2
    def shaft_rate(s):
        m=thr_mult(s['id'],s['L'])*prof['card_mult'](cards.get(s['mgr'],0))
        return MS[s['id']]['CurrencyOutputMultiplier']*s['L']*m/MS[s['id']]['GenerationDelaySecBase']
    def reward_rate():
        r=forge_rate()+sum(shaft_rate(s) for s in shafts if s['auto'])
        return max(r, d['BalanceProperties'].get('NoGeneratorsCurrencyRate',1.5) if isinstance(d['BalanceProperties'],dict) else 1.5)
    nextfree=2400
    while t<maxT:
        # spending: cheapest affordable first
        while True:
            opts=[]
            if len(cats)<10: opts.append(('hire',ft1(0,2,4,n+prof['hire_off'])))
            opts.append(('forge',ft2a(72,0,1.43,forgeL-1+prof['up_off'])))
            for s in shafts: opts.append(('shaft',ft2a(MS[s['id']]['UpgradeCostBase'],0,MS[s['id']]['UpgradeCostGrowth'],s['L']-1+prof['up_off']),s))
            opts.sort(key=lambda o:o[1])
            o=opts[0]
            if o[1]>gold: break
            gold-=o[1]
            if o[0]=='hire':
                lv=1+math.floor(ft0(0.4,0.125,0,n)); cats.append(lv); n+=1
                if 'hire1' not in firsts: firsts['hire1']=t
            elif o[0]=='forge': forgeL+=1
            else: o[2]['L']+=1
        # free cat
        if t>=nextfree and len(cats)<10:
            cats.append(max(1,1+math.floor(ft0(0.4,0.125,0,n))-1)); nextfree=t+2400
        # merge
        cats.sort()
        merged=True
        while merged:
            merged=False
            for i in range(len(cats)-1):
                if cats[i]==cats[i+1]:
                    v=cats[i]; del cats[i:i+2]; cats.append(v+1); cats.sort(); merged=True; break
        # assign: strongest cats to remaining targets (exit last), K per target
        rem=[x for x in tg if not x['done']]
        order=sorted([x for x in rem if x['kind']!='e'], key=lambda x:x['hp'])+[x for x in rem if x['kind']=='e']
        if not order: break
        cs=sorted(cats,reverse=True); i=0
        for x in order:
            for k in range(K):
                if i>=len(cs): break
                x['hp']-=ft1(0,1,2.1,cs[i]-dmgoff); i+=1
            if x['hp']<=0 and not x['done']:
                x['done']=True
                if x['kind']=='r':
                    r=ROCKSEC[x['id']]
                    if r['CoreCurrencySecondsFormulaType']==3:
                        sec=prof['ft3'](r['CoreCurrencySecondsBase'],r['CoreCurrencySecondsMultiplier'],r['CoreCurrencySecondsGrowth'],r['CoreCurrencySecondsPower'],r['CoreCurrencySecondsRound'],x['lvl']+prof['rock_off'])
                        g_=sec*reward_rate(); gold+=g_; rockgold+=g_
                        if 'rock1' not in firsts: firsts['rock1']=(t,sec,round(g_,1))
                elif x['kind']=='s':
                    shafts.append(dict(id=x['id'],L=1,mgr=x['card'],auto=prof['auto'](zid,x),ph=0))
                elif x['kind']=='e':
                    firsts['exit']=t
            if i>=len(cs): break
        # income
        gold+=forge_rate()
        for s in shafts: gold+=shaft_rate(s)   # active player collects manual shafts immediately
        t+=1
    return dict(zone=zid,T=t,firsts=firsts,hires=n,cats=sorted(cats),forgeL=forgeL,shafts=[(s['id'],s['L']) for s in shafts],gold=round(gold),rockgold=round(rockgold))
base=dict(dmg_off=0,hire_off=0,up_off=0,ft3=ft3a,rock_off=0,card_mult=lambda L:1,auto=lambda z,x: z!='zone2')
P={
 'P1 dmg2.1^L, FT3a x=L':base,
 'P2 dmg2.1^(L-1), FT3a x=L':dict(base,dmg_off=1),
 'P3 dmg2.1^L, FT3b x=L-1':dict(base,ft3=ft3b,rock_off=-1),
 'P4 dmg2.1^(L-1), FT3a x=L-1':dict(base,dmg_off=1,rock_off=-1),
 'P5 dmg2.1^(L-1), FT3b x=L-1':dict(base,dmg_off=1,ft3=ft3b,rock_off=-1),
 'P6 P2 + hire x=n+1, up x=L':dict(base,dmg_off=1,hire_off=1,up_off=1),
}
for name,pr in P.items():
    print('##',name)
    for z in ['zone1','zone2','zone3']:
        print('  ',run(z,pr))
