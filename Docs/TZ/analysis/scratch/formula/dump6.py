import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
GO=d['GeneratorObjectives']
print(type(GO))
items = GO if isinstance(GO,list) else [dict(Id=k,**v) if isinstance(v,dict) else {'Id':k,'v':v} for k,v in GO.items()]
for g in items:
    print({k:(v[:8] if isinstance(v,list) else v) for k,v in g.items()})
print(d['CardUpgradeCosts'] if not isinstance(d['CardUpgradeCosts'],list) else d['CardUpgradeCosts'][:2])
