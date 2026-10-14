import json, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
def tally(models,acc):
    for m in models:
        acc[(m['RewardType'],m['DetailedType'] if m['RewardType'] in (0,2,4,9) else ('gacha' if m['RewardType']==1 else str(m['RewardType'])))]+=m['Quantity']
q=collections.Counter()
for x in d['Quests']: tally(x['Rewards'],q)
print('Quests total',dict(q))
a=collections.Counter()
for x in d['Achievements']:
    for r in x['Rewards']: tally(r,a)
print('Achievements total',dict(a))
print('Quest sample',[ (x['Rank'],x['CriteriaType'],x['Quantities']) for x in d['Quests'][:5]])
print('Achievement types',collections.Counter(x['Type'] for x in d['Achievements']))
print('store currency codes',collections.Counter(s['CurrencyCode'] for s in d['StoreItems']))
print('store item types',collections.Counter(s['StoreItemType'] for s in d['StoreItems']))
print('BP bonus', json.dumps(d['BattlePassBonusRewards'][:3]))
# rock usage of premium chests
R=collections.Counter()
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r' and p[1] in ('rockgacharoyal','rockgachamythical','rockgachalegendary','rockgachagolden'): R[p[1]]+=1
print(R)
