import json,collections
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
Z=J['Zones']; zid=lambda z:int(z['Id'][4:])
Z.sort(key=zid)
first={}
firstrock={}
rbcount={}
for z in Z:
    toks=z['Grid'].split(',')
    for t in toks:
        p=t.split(':')
        if p[0]=='s': first.setdefault(p[1],zid(z))
        if p[0]=='r': firstrock.setdefault(p[1],zid(z))
    rbcount[zid(z)]=sum(1 for t in toks if t.startswith('r:rockbasic:'))
mg=collections.Counter((c['Rarity'],c['ModifierBase'],c['ModifierMultiplier'],c['ModifierGrowth'],c['ModifierFormulaType']) for c in J['Cards'] if c['IsManager'])
print(mg)
for c in J['Cards']:
    if c['IsManager'] and c['Rarity']==1:
        t=c['TargetIds'][0]; print(c['Id'],t,c['MineUnlock'],first.get(t), 'OK' if first.get(t)==c['MineUnlock'] else 'DIFF')
for c in J['Cards']:
    if c['IsManager'] and c['Rarity']==2:
        t=c['TargetIds'][0]; print('r2',c['Id'],t,c['MineUnlock'],first.get(t))
print('rockbattlepass first',firstrock.get('rockbattlepasscurrency'))
BPp=J['BalanceProperties'][0]; print('BP',[(k,v) for k,v in BPp.items() if 'Deliver' in k or 'BattlePass' in k or 'Spawn' in k])
print('rockbasic first',firstrock['rockbasic'],'max',max(rbcount.values()),max(rbcount,key=rbcount.get),'total',sum(rbcount.values()))
cum=0
for k in sorted(rbcount):
    cum+=rbcount[k]
    if cum>=500: print('cum500 at zone',k);break
R={r['Id']:r for r in J['Rocks']}
rc=R['rockbasic']['RewardCycle']
for i,e in enumerate(rc):
    if i>=117 and (e.get('DetailedType') not in ('Core','Soft')): print(i,e)
