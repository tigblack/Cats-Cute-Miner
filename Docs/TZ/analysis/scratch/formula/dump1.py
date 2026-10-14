import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
def short(o,skip=()):
    return {k:v for k,v in o.items() if not isinstance(v,(list,dict)) or (isinstance(v,list) and len(v)<12)} 
for sec in ['BalanceProperties','Reinforcements','SpawningCart','CheckPoint','Exit','Miners','Spells']:
    v=d[sec]
    print('==',sec)
    if isinstance(v,list):
        for o in v: print(short(o))
    else: print(short(v))
print('== MineShafts')
for o in d['MineShafts']:
    print({k:v for k,v in o.items() if not isinstance(v,list) or len(v)<6})
