import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for r in d['Rocks']:
    rc=r.get('RewardCycle') or []
    cnt=collections.Counter((e.get('RewardType'),e.get('DetailedType'),e.get('Quantity')) for e in rc)
    print({k:v for k,v in r.items() if k!='RewardCycle'}, 'cycle',len(rc), dict(cnt.most_common(6)))
    if r['Id'].startswith('rockonboarding'):
        print('   ', [(e['RewardType'],e['DetailedType'],e['Quantity']) for e in rc])
print(list(d['MiningTargetHealths'][0].keys()) if isinstance(d['MiningTargetHealths'],list) else type(d['MiningTargetHealths']))
