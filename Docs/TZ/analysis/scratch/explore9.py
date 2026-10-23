import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
GO=d['GeneratorObjectives']
print(list(GO[0].keys()))
for g in GO:
    s={k:(v if not isinstance(v,list) else (len(v), v[:6])) for k,v in g.items()}
    print(s)
