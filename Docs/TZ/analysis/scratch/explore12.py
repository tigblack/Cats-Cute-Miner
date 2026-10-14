import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for g in d['Gacha']:
    if not g['Id'].startswith('scg'):
        print({k:v for k,v in g.items()})
# GuaranteedCardMin nonempty
ne=[g['Id'] for g in d['Gacha'] if g['GuaranteedCardMin']]
print('Min nonempty',len(ne),ne)
print('ids nonempty & min empty',len([g for g in d['Gacha'] if g['GuaranteedCardIds'] and not g['GuaranteedCardMin']]))
print('len mismatch',[g['Id'] for g in d['Gacha'] if g['GuaranteedCardMin'] and len(g['GuaranteedCardMin'])!=len(g['GuaranteedCardIds'])])
print('min>cnt',[g['Id'] for g in d['Gacha'] if any(a>b for a,b in zip(g['GuaranteedCardMin'],g['GuaranteedCardCounts']))])
hp=d['MiningTargetHealths']
for k,l in [('rockonboardingmine1',1),('rockonboardingmine1',2),('rockbasic',3),('amethyst',1),('checkpoint',5),('exit',1)]:
    print(k,l,hp[k][l-1])
print('exit hp first 12',hp['exit'][:12]); print('checkpoint first 12',hp['checkpoint'][:12])
print('rockbasic first 10',hp['rockbasic'][:10]); print('rockonb1',hp['rockonboardingmine1'][:5])
print('amethyst',hp['amethyst'][:5],'citrine',hp['citrine'][:5])
print('max hp', max(max(v) for v in hp.values()))
