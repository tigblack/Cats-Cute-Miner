import json,sys
J=json.load(open(sys.argv[1]))
G={g['GeneratorId']:g for g in J['GeneratorObjectives']}
# Wiki main-mine tables (fandom Forge / Amethyst pages)
wiki_forge={10:2,20:2,30:2,40:10,50:2,60:2,70:2,80:100,90:2,100:2,110:2,120:1000,130:2,140:2,150:2,160:10000,170:2,180:2,190:2,200:1000,
 210:2,220:2,230:2,240:10000,250:2,260:2,270:2,280:1000,290:2,300:2,310:2,320:10000,330:2,340:2,350:2,360:1000,370:2,380:2,390:2,400:10000,
 410:2,420:2,430:2,440:1000,450:2,460:2,470:2,480:10000,490:2,500:2}
wiki_am={10:2,20:2,40:2,60:200,80:30,100:4,120:2,140:2,160:2,180:20000,200:3000,220:400,240:2,260:2,280:2,300:30000,320:4000,340:500,360:2,380:2,400:2,420:40000,440:5000,460:600}
def thresholds(gid,start=1):
    g=G[gid]; lvl=start; out={}
    for c,m in zip(g['ObjectiveCount'],g['CoreCurrencyMultiplier']):
        lvl+=c; out[lvl]=m
    return out
for gid,w in (('spawningcart',wiki_forge),('amethyst',wiki_am)):
    t=thresholds(gid)
    keys=sorted(k for k in t if k<=500)
    print(gid,'JSON thresholds<=500:',[(k,t[k]) for k in keys])
    mism=[(k,w.get(k),t.get(k)) for k in sorted(set(w)|set(keys)) if w.get(k)!=t.get(k)]
    print(' mismatches (lvl,wiki,json):',mism)
    print(' first ObjectiveCount', G[gid]['ObjectiveCount'][:12], 'mult', G[gid]['CoreCurrencyMultiplier'][:12])
# citrine & agate first values (wiki only has event tables)
for gid in ('citrine','agate'):
    t=thresholds(gid); print(gid,[(k,t[k]) for k in sorted(t)][:12])
