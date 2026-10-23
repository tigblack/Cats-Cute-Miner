import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
G=d['Gacha']
print(list(G[0].keys()))
for g in G[:12]+[x for x in G if x['Id'].startswith('scg_m00')][:14]:
    print({k:v for k,v in g.items() if 'Soft' in k or k in ('Id','GachaType','BaseNumCards','CoreCurrency','CoreCurrencySeconds') or 'Core' in k})
