import json, sys
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
bp=d['BalanceProperties'][0]
for k,v in bp.items():
    s=json.dumps(v)
    print(k,'=',s[:600])
print()
for sec in ['Miners','Reinforcements','SpawningCart','CheckPoint','Spells','CostConversions','FreeGachaCycle','RewardWheel','CrushableRewards','CardUpgradeCosts']:
    print('==',sec)
    for e in d[sec]:
        print(json.dumps(e)[:900])
