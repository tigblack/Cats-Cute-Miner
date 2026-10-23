import json,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
q=d['Quests'][0]; print(json.dumps(q)[:600])
a=d['Achievements'][0]; print(json.dumps(a)[:700])
