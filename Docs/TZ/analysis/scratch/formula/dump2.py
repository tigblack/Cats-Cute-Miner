import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print('== Deliveries')
for o in d['Deliveries']:
    print({k:v for k,v in o.items()})
print('== Cards')
for c in d['Cards']:
    print({k:v for k,v in c.items() if k not in ('Name','Description')})
