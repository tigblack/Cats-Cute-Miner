import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
D=d['Deliveries']
print(D[0].keys())
for x in D: print({k:v for k,v in x.items()})
tot=sum(x['Weight'] for x in D); ad=sum(x['Weight'] for x in D if x['IsAd']); dyn=sum(x['Weight'] for x in D if x['RewardType']==9)
print(tot,ad,tot-ad,dyn,dyn/(tot-ad))
print({k:(v if not isinstance(v,list) else (len(v),v[:5])) for k,v in d['FreeGachaCycle'].items()} if isinstance(d['FreeGachaCycle'],dict) else d['FreeGachaCycle'][0].keys())
