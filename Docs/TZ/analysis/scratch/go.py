import json,sys
d=json.load(open(sys.argv[1]))
for g in d['GeneratorObjectives']:
    oc=g['ObjectiveCount']; m=g['CoreCurrencyMultiplier']
    print(g['GeneratorId'], len(oc), len(m), sum(oc), oc[:6], m[:8], 'soft', g['SoftCurrencyRewardBase'], g['SoftCurrencyRewardMultiplier'])
z=d['Zones']
print('GenObjSoftMult', [ (z[i]['Id'], z[i]['RankMultipliers'][0]['GenObjectiveSoftCurrencyMultiplier'], z[i]['RankMultipliers'][0]['MiningSoftCurrencyMultiplier']) for i in [0,1,2,4,9,19,49,99,143]])
# MineUnlock and rarities
from collections import Counter
print(Counter((c['Rarity'],c['IsManager']) for c in d['Cards']))
print(sorted(set(c['MineUnlock'] for c in d['Cards'])))
# rocks reward cycles summary
for r in d['Rocks']:
    rc=r['RewardCycle']; 
    print(r['Id'], 'mimic',r['MimicId'], 'len',len(rc), Counter(x['DetailedType'] for x in rc).most_common(5), 'soft',r['SoftCurrencyBase'],r['SoftCurrencyMultiplier'])
