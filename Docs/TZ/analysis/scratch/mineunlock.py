import json, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
C={c['Id']:c for c in d['Cards']}
first={}; firstlvl={}
shaftfirst={}
for zi,z in enumerate(d['Zones'],1):
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='s':
            first.setdefault(p[4],zi); firstlvl.setdefault(p[4],int(p[5]))
            shaftfirst.setdefault(p[1],zi)
rows=[]
for cid,zi in sorted(first.items(),key=lambda x:x[1]):
    c=C[cid]; rows.append((cid,c['Rarity'],c['MineUnlock'],zi,firstlvl[cid],c['TargetIds']))
bad=[r for r in rows if r[2]>r[3]]
print('cards with MineUnlock > first zone requiring them:',len(bad))
for r in rows[:45]: print(r)
print('shaft first zone',shaftfirst)
