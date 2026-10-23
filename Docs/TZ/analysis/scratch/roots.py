import json, sys
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for k,v in d.items():
    if isinstance(v,list):
        keys=set()
        for e in v:
            if isinstance(e,dict): keys|=set(e.keys())
        print(f"{k}: list[{len(v)}] keys={sorted(keys)}")
    elif isinstance(v,dict):
        print(f"{k}: dict keys={sorted(v.keys())[:80]} n={len(v)}")
    else:
        print(f"{k}: {type(v).__name__} = {str(v)[:100]}")
