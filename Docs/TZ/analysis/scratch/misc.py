import json, sys, collections, re
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print('Rocks LB', collections.Counter((r['LeaderboardCurrencyBase'],r['LeaderboardCurrencyMultiplier'],r['LeaderboardCurrencyGrowth'],r['LeaderboardCurrencyFormulaType'],r['LeaderboardCurrencyVariance']) for r in d['Rocks']))
# BattlePass rocks placement per zone, HP
Z=d['Zones']
bp=collections.Counter(); hard=collections.Counter(); first={}
lv=collections.Counter()
for i,z in enumerate(Z):
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r' and p[1]=='rockbattlepasscurrency':
            bp[z['Id']]+=1; lv[p[2]]+=1
        if p[0]=='r' and p[1].startswith('rockhardcurrency'):
            hard[p[1]]+=1
            first.setdefault(p[1],z['Id'])
print('bp zones',len(bp),list(bp.items())[:10],'levels',lv.most_common(5))
print('hard',hard,first)
# total hard currency placed
tot=0
for z in Z:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r' and p[1]=='rockhardcurrencysmall': tot+=3
        if p[0]=='r' and p[1]=='rockhardcurrencybig': tot+=10
print('hard from rocks total',tot)
# GeneratorObjectives soft reward
for g in d['GeneratorObjectives'][:3]:
    print(g['GeneratorId'],g['SoftCurrencyRewardBase'],g['SoftCurrencyRewardMultiplier'],g['SoftCurrencyRewardGrowth'],g['SoftCurrencyRewardFormulaType'],len(g['ObjectiveCount']))
print(collections.Counter((g['SoftCurrencyRewardBase'],g['SoftCurrencyRewardMultiplier'],g['SoftCurrencyRewardGrowth'],g['SoftCurrencyRewardFormulaType']) for g in d['GeneratorObjectives']))
print([g['GeneratorId'] for g in d['GeneratorObjectives']])
print('mineshaft ids',[m['Id'] for m in d['MineShafts']])
