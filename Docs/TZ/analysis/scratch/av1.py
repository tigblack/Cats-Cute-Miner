import json,sys
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
print(list(J.keys()), len(J.keys()))
G={g['Id']:g for g in J['Gacha']}
for k in ['scg_m001_exit','scg_m003_exit','scg_m010_exit','scg_m002_s01','scg_m003_s01','scg_m003_r01','scg_m002_exit','GachaCrusher','GachaIron','GachaWooden','GachaRare','GachaLegendary']:
    g=G.get(k); 
    if g: print(k,{kk:v for kk,v in g.items() if kk not in ('Id',)})
scg=[k for k in G if k.startswith('scg')]
print('scg count',len(scg))
nonempty=[k for k in scg if G[k].get('GuaranteedCardMin')]
print('min nonempty',nonempty)
other=[k for k in scg if k not in nonempty]
print('others SoftMin==Max', all(G[k].get('SoftCurrencyMin')==G[k].get('SoftCurrencyMax') for k in other), 'CardMultMax0', all(G[k].get('CardMultMax',0)==0 for k in other))
