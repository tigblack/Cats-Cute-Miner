import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
GO=d['GeneratorObjectives']
for g in GO:
    c=g['ObjectiveCount']; lv=1; th=[]
    for x in c: lv+=x; th.append(lv)
    print(g['GeneratorId'], 'sum',sum(c),'thr first',th[:8],'last',th[-3:], 'all mult10', all(t%10==0 for t in th))
print(list(d['Deliveries'][0].keys()))
for x in d['Deliveries']: print(x)
print(json.dumps(d['FreeGachaCycle'][0])[:400])
