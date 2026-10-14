import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
G={g['Id']:g for g in d['Gacha']}
print(list(d['Gacha'][0].keys()))
for gid in ['GachaWooden','GachaIron','GachaCrusher','scg_m001_exit','scg_m002_s01','scg_m005_c01','scg_m005_d01','scg_m003_r01','scg_m010_exit']:
    print('---',gid); print(json.dumps(G[gid]))
