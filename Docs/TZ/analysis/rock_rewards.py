import json, collections, sys
d=json.load(open(sys.argv[1]))
R=d['Rocks']
for r in R:
    rc=r['RewardCycle']
    print(r['Id'],'mimic=',r['MimicId'] or '-',r['WidthCells'],r['DepthCells'],'CCS',r['CoreCurrencySecondsFormulaType'],r['CoreCurrencySecondsBase'],r['CoreCurrencySecondsMultiplier'],r['CoreCurrencySecondsGrowth'],r['CoreCurrencySecondsPower'],r['CoreCurrencySecondsRound'],'SC',r['SoftCurrencyBase'],r['SoftCurrencyMultiplier'],r['SoftCurrencyGrowth'],r['SoftCurrencyFormulaType'],'cycle',len(rc),collections.Counter((x['RewardType'],x['DetailedType'],x['Quantity']) for x in rc).most_common(6))
print('RewardType/Detailed overall in rocks',collections.Counter((x['RewardType'],x['DetailedType']) for r in R for x in r['RewardCycle']))
print('rockbasic first 20',[(x['RewardType'],x['DetailedType'],x['Quantity']) for x in [r for r in R if r['Id']=='rockbasic'][0]['RewardCycle'][:20]])
# which rocks used in zones
used=collections.Counter()
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r': used[p[1]]+=1
print('rock usage',used)
print('rocks in data unused', [r['Id'] for r in R if r['Id'] not in used])
