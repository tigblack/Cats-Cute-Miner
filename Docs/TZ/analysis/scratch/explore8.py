import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print(list(d['Cards'][0].keys()))
for c in d['Cards']:
    print({k:v for k,v in c.items() if k not in ('Description',)})
print(json.dumps(d['CardUpgradeCosts'][0]))
