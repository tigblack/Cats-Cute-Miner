import json,sys
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for k,v in d.items():
    if isinstance(v,list): print(k,len(v), (list(v[0].keys())[:40] if v and isinstance(v[0],dict) else ''))
    elif isinstance(v,dict): print(k,'dict',len(v), list(v.keys())[:5])
    else: print(k,repr(v)[:60])
