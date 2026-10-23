import json,sys
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for k,v in d.items():
    t=type(v).__name__
    n=len(v) if hasattr(v,'__len__') else v
    print(k,t,n if not isinstance(v,str) else v)
print('---BP')
print(json.dumps(d['BalanceProperties'],indent=0)[:6000])
