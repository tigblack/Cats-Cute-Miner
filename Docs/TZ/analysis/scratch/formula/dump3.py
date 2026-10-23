import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z={z['Id']:z for z in d['Zones']}
z=Z['zone1']
print({k:v for k,v in z.items() if k not in ('Grid',)})
rm=[Z['zone%d'%i]['RankMultipliers'][0] for i in range(1,145)]
keys=list(rm[0].keys())
for k in keys:
    vals=[r[k] for r in rm]
    print(k, vals[:12], '...', vals[-3:])
