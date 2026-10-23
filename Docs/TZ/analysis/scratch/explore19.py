import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for c in d['CrushableRewards']: print(c)
print(d['CrusherGrids'][0]['Grid'][:300])
print('RewardWheel', json.dumps(d['RewardWheel'][0])[:600])
print('CostConversions', json.dumps(d['CostConversions'][0])[:600])
