import json,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); cards={c['Id']:c for c in d['Cards']}
first={}; maxreq=C.defaultdict(int)
for z in d['Zones']:
    n=int(z['Id'][4:])
    for x in z['Grid'].split(','):
        p=x.split(':')
        if p[0]=='s':
            first.setdefault(p[4],n); maxreq[p[4]]=max(maxreq[p[4]],int(p[5]))
            assert p[1] in cards[p[4]]['TargetIds'] and cards[p[4]]['IsManager']
eq=[c for c in first if cards[c]['MineUnlock']==first[c]]; ne=[(c,cards[c]['MineUnlock'],first[c]) for c in first if cards[c]['MineUnlock']!=first[c]]
print('required mgr',len(first),'eq',len(eq),'ne',ne)
print('maxreq by rarity', C.Counter((cards[c]['Rarity'],maxreq[c]) for c in maxreq))
print('MineUnlock range',min(c['MineUnlock'] for c in d['Cards']),max(c['MineUnlock'] for c in d['Cards']))
print('targets per shaft', C.Counter(t for c in d['Cards'] if c['IsManager'] for t in c['TargetIds']))
# rank fields
print('Quests rank',max(q['Rank'] for q in d['Quests']), 'Deliv RU', max(q['RankUnlock'] for q in d['Deliveries']), 'Bundles MinRank', max(b['MinRank'] for b in d['Bundles']))
print('wheel', d['RewardWheel'][0]['ActivationRank'])
