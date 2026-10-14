import json,sys
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for k in ['Miners','Reinforcements','SpawningCart','CheckPoint','Spells','Exit']:
    print('---',k); print(json.dumps(d[k],indent=0))
print('--- amethyst'); print(json.dumps([m for m in d['MineShafts'] if m['Id']=='amethyst'][0],indent=0))
