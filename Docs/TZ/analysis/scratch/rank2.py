import json, sys, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print('Quests Rank',sorted(collections.Counter(q['Rank'] for q in d['Quests']).items()))
print('Bundles MinRank',sorted(collections.Counter(b['MinRank'] for b in d['Bundles']).items()))
for s in ['ProgressiveOffers','MultiOffers']:
    print(s,[(o['MinRank'],o['MaxRank']) for o in d[s]])
print(json.dumps(d['Quests'][0])[:500])
print(json.dumps(d['Achievements'][0])[:500])
print(json.dumps(d['Bundles'][0])[:800])
# Look for "Rank" anywhere in Zones? GridRowRanks
print(set(json.dumps(z['GridRowRanks']) for z in d['Zones']))
# RankMultipliers monotonic? find zones where GachaCardsMultNormal changes
Z=d['Zones']
v=[z['RankMultipliers'][0]['GachaCardsMultNormal'] for z in Z]
print('GachaCardsMultNormal first 20',v[:20])
print('MiningSoftCurrencyMultiplier first 20',[z['RankMultipliers'][0]['MiningSoftCurrencyMultiplier'] for z in Z[:20]])
