import json,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); Rk={r['Id']:r for r in d['Rocks']}
for r in d['Rocks']:
    rc=r['RewardCycle']; 
    print(r['Id'],'mim',r['MimicId'],r['WidthCells'],r['DepthCells'],'CCS',r['CoreCurrencySecondsBase'],r['CoreCurrencySecondsMultiplier'],'soft',r['SoftCurrencyBase'],r['SoftCurrencyMultiplier'],'len',len(rc), C.Counter((x['RewardType'],x['DetailedType']) for x in rc).most_common(6))
rb=Rk['rockbasic']['RewardCycle']
for i,x in enumerate(rb):
    if x['RewardType']==1 and i<117: print('rb early gacha',i,x['DetailedType'])
print('rb gachas positions', {k:[i for i,x in enumerate(rb) if x['DetailedType']==k] for k in ('GachaSilver','GachaGolden','GachaForged','GachaIron','GachaWooden')})
print('mine2 cycle',[(x['RewardType'],x['DetailedType'],x['Quantity']) for x in Rk['rockonboardingmine2']['RewardCycle']])
print('hard quantities',C.Counter(x['Quantity'] for r in d['Rocks'] for x in r['RewardCycle'] if x['DetailedType']=='Hard'))
print('bp quantities',C.Counter(x['Quantity'] for r in d['Rocks'] for x in r['RewardCycle'] if x['DetailedType']=='BattlePass'))
print('rock with Hard', [r['Id'] for r in d['Rocks'] if any(x['DetailedType']=='Hard' for x in r['RewardCycle'])])
# zone rock counts per type for zone1..5
for z in d['Zones'][:6]:
    c=C.Counter(t.split(':')[1] for t in z['Grid'].split(',') if t.startswith('r:'))
    print(z['Id'],dict(c))
print('z1 rm',d['Zones'][0]['RankMultipliers'][0])
print('z2 GO mult',d['Zones'][1]['RankMultipliers'][0]['GenObjectiveSoftCurrencyMultiplier'])
