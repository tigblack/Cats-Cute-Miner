import json, collections, sys
d=json.load(open(sys.argv[1]))
G=d['Gacha']; gid={g['Id'] for g in G}
cards={c['Id'] for c in d['Cards']}
for g in G:
    print(g['Id'],'T',g['GachaType'],'mim',g['MimicId'] or '-','CMM',g['CardMultMax'],'GIds',g['GuaranteedCardIds'],'GMin',g['GuaranteedCardMin'],'GCnt',g['GuaranteedCardCounts'],'SC',g['SoftCurrencyMin'],g['SoftCurrencyMax'],'LB',g['LeaderboardCurrency'],'N',g['BaseNumCards'],'W',g['UncommonWeight'],g['RareWeight'],g['LegendaryWeight'],g['EventEpicWeight'],'LCM',g['LowCardChanceMult'],'MCM',g['ManagerChanceMult'])
refs=set()
for r in d['Rocks']:
    for x in r['RewardCycle']:
        if x['RewardType']==1: refs.add(x['DetailedType'])
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='s': refs.add(p[3])
        if p[0] in 'pe': refs.add(p[3])
print('missing gacha refs',refs-gid)
print('gacha never referenced by zones/rocks',sorted(gid-refs))
bad=[(g['Id'],c) for g in G for c in g['GuaranteedCardIds'] if c not in cards]
print('bad guaranteed cards',bad)
print('len mismatch',[(g['Id'],len(g['GuaranteedCardIds']),len(g['GuaranteedCardMin']),len(g['GuaranteedCardCounts'])) for g in G if not (len(g['GuaranteedCardIds'])==len(g['GuaranteedCardCounts']))])
print('min empty but ids nonempty',[g['Id'] for g in G if g['GuaranteedCardIds'] and not g['GuaranteedCardMin']])
print('GachaType',collections.Counter(g['GachaType'] for g in G))
