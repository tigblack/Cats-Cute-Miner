import json, sys, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for x in d['Deliveries']:
    print(json.dumps(x))
f=d['FreeGachaCycle'][0]
print('free cycle len',len(f['GachaIds']),collections.Counter(f['GachaIds']))
print('last 10',f['GachaIds'][-10:])
