import json,collections
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
Z={z['Id']:z for z in J['Zones']}
R={r['Id']:r for r in J['Rocks']}
G={g['Id']:g for g in J['Gacha']}
for zn in ['zone1','zone2','zone3']:
    toks=[t for t in Z[zn]['Grid'].split(',') if t!='.']
    c=collections.Counter(t if t[0] in 'spec' else ':'.join(t.split(':')[:2]) for t in toks if t[0]!='x')
    print(zn,dict(c))
for rid in ['rockonboardingmine1','rockonboardingmine2','rockonboardingmine3','rocksoftcurrencysmall','rockonboardinggachawoodenmine3']:
    if rid in R: print(rid,R[rid].get('MimicId'),[ (e['RewardType'],e['DetailedType'],e['Quantity']) for e in R[rid]['RewardCycle']])
# all cycles gacha content zone2/3
for rid in ['rockonboardingmine2','rockonboardingmine3']:
    for e in R[rid]['RewardCycle']:
        if e['RewardType']==1: print(rid,e['DetailedType'],G[e['DetailedType']]['GuaranteedCardIds'],G[e['DetailedType']]['GuaranteedCardCounts'])
print(J['CardUpgradeCosts'][0])
