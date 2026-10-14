import json, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
CR={c['CrushableType']:c['Reward'] for c in d['CrushableRewards']}
for c in d['CrusherGrids'][:34]:
    toks=c['Grid'].split(',')
    head=toks[:4]; body=toks[4:]
    cnt=collections.Counter(t.split(':')[0] for t in body)
    cm=sum(CR.get(int(t.split(':')[0]),{}).get('CardMult',0) for t in body if t.split(':')[0].isdigit())
    sm=sum(CR.get(int(t.split(':')[0]),{}).get('SoftCurrencyMult',0) for t in body if t.split(':')[0].isdigit())
    hc=sum(CR.get(int(t.split(':')[0]),{}).get('HardCurrency',0) for t in body if t.split(':')[0].isdigit())
    if c['Id'] in (1,2,5,10,11,20,33,34): print(c['Id'],head,dict(cnt),'sumCardMult',round(cm,4),'sumSoftMult',round(sm,4),'hard',hc)
