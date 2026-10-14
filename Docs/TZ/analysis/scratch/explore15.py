import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
R={r['Id']:r for r in d['Rocks']}
def s(x): return {'Core':'C','Soft':'S','Hard':'H%d'%x['Quantity']}.get(x['DetailedType'],x['DetailedType'])
for k in ['rockonboardingmine1','rockonboardingmine2','rockonboardingmine3','rockonboardingmine4','rockonboardingmine5']:
    print(k,len(R[k]['RewardCycle']),' '.join(s(x) for x in R[k]['RewardCycle']))
print('rockbasic first 40',' '.join(s(x) for x in R['rockbasic']['RewardCycle'][:40]))
# levels of onboarding rocks in zones
Z=d['Zones']
for zi in range(6):
    c=collections.Counter(t for t in Z[zi]['Grid'].split(',') if t.startswith('r:'))
    print(zi+1, sorted(c.items()))
