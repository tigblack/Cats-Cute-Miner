import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
D=d['Deliveries']
for rank in range(4,11):
  for cp in (0,1):
    pool=[x for x in D if x['RankUnlock']<=rank and x['CheckPointUnlock']<=cp]
    tot=sum(x['Weight'] for x in pool); ad=sum(x['Weight'] for x in pool if x['IsAd'])
    dyn=sum(x['Weight'] for x in pool if x['RewardModel']['DetailedType']=='Dynamite')
    na=tot-ad
    print(f"rank {rank} cp {cp}: total {tot} ad {ad} ({ad/tot:.0%}) nonad {na} -> boost x{tot/na:.2f}; dynamite share of nonad {dyn/na if na else 0:.0%}")
