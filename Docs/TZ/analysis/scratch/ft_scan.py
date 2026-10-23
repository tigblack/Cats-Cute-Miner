import json,sys
from collections import defaultdict
d=json.load(open(sys.argv[1]))
skip={'CrusherGrids','Zones','StoreItems','Bundles','Achievements','ProgressiveOffers','Quests','MultiOffers','ProgressiveOffersABTest','MultiOffersAbTest','BundleReplacementsAbTest','BattlePassRegions','BattlePassBonusRewards','RewardWheel','CostConversions','CrushableRewards'}
res=defaultdict(set)
def walk(o,path,sec):
    if isinstance(o,dict):
        for k,v in o.items():
            if k.endswith('FormulaType'):
                pre=k[:-len('FormulaType')]
                if isinstance(v,list):
                    for i,ft in enumerate(v):
                        tup=tuple((s, o.get(pre+s)[i] if isinstance(o.get(pre+s),list) else None) for s in ['Base','Multiplier','Growth','Power','Round'] if (pre+s) in o)
                        res[(ft,sec,pre)].add(tup)
                else:
                    tup=tuple((s,o.get(pre+s)) for s in ['Base','Multiplier','Growth','Power','Round'] if (pre+s) in o)
                    res[(v,sec,pre)].add(tup)
        for k,v in o.items():
            if k not in ('RewardCycle',): walk(v,path+'.'+k,sec)
    elif isinstance(o,list):
        for x in o: walk(x,path,sec)
for k,v in d.items():
    if k in skip: continue
    walk(v,k,k)
for key in sorted(res, key=lambda x:(x[0],x[1],x[2])):
    vals=sorted(res[key])
    print(key, len(vals), vals[:6])
