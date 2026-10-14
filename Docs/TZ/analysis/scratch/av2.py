import json,collections
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
G={g['Id']:g for g in J['Gacha']}
# ca001 in scripted
tot=0;n=0
for k,g in G.items():
    if 'ca001' in g['GuaranteedCardIds']:
        i=g['GuaranteedCardIds'].index('ca001'); c=g['GuaranteedCardCounts'][i]; tot+=c;n+=1; print(k,c,g['GuaranteedCardMin'][i] if g['GuaranteedCardMin'] else None)
print('ca001',n,tot)
C={c['Id']:c for c in J['Cards']}
print(C['ca001'])
for cid in ['ca002','ca003','ca026','ca029','ca030','ca034','ca038','ca050','ca013','ca060']:
    print(cid,{k:v for k,v in C[cid].items() if k not in ('Id',)})
mu=[c.get('MineUnlock') for c in J['Cards']]
print('MineUnlock range',min(mu),max(mu))
print('CheckPoint',J['CheckPoint'])
print('Spells',J['Spells'])
print('FGC',{k:(v if not isinstance(v,list) else (len(v),v[:3])) for k,v in J['FreeGachaCycle'][0].items()} if isinstance(J['FreeGachaCycle'],list) else J['FreeGachaCycle'].keys())
