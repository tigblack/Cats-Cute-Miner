import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
rb=[r for r in d['Rocks'] if r['Id']=='rockbasic'][0]['RewardCycle']
pos=collections.defaultdict(list)
for i,x in enumerate(rb): pos[(x['RewardType'],x['DetailedType'],x['Quantity'])].append(i)
for k,v in pos.items(): print(k,len(v),'first idx',v[0],'idxs',v[:12])
Z=d['Zones']
per=[]; cum=0
for zi,z in enumerate(Z):
    n=sum(1 for t in z['Grid'].split(',') if t.startswith('r:rockbasic:'))
    cum+=n; per.append((zi+1,n,cum))
print('max per zone',max(per,key=lambda x:x[1]))
print([p for p in per if p[2]>0][:12])
print('total rockbasic',cum, 'cycles', cum/500)
# rock levels of rockbasic in zone 6
