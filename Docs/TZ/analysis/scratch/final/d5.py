import json,re,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J)); G={g['Id']:g for g in d['Gacha']}
print('gacha',len(G))
print('types',{t:[g for g in G if G[g]['GachaType']==t and not g.startswith('scg_')] for t in (0,1,2,3)})
print('scg by type',C.Counter(G[g]['GachaType'] for g in G if g.startswith('scg_')), 'scg total',sum(g.startswith('scg_') for g in G))
scg=[g for g in G if g.startswith('scg_')]
print('scg ids',scg)
print('GCMin nonempty',[g for g in G if G[g]['GuaranteedCardMin']])
print('BaseNumCards scg',set(G[g]['BaseNumCards'] for g in scg))
print('CardMultMax nonzero',[g for g in G if G[g]['CardMultMax']])
print('SoftMin<Max',[g for g in G if G[g]['SoftCurrencyMin']!=G[g]['SoftCurrencyMax']])
for g in G:
    x=G[g]
    if not g.startswith('scg_'): print(' ',g,x['GachaType'],x['MimicId'],'N',x['BaseNumCards'],'W U/R/L/E',x['UncommonWeight'],x['RareWeight'],x['LegendaryWeight'],x['EventEpicWeight'],'soft',x['SoftCurrencyMin'],x['SoftCurrencyMax'],'low/mgr',x['LowCardChanceMult'],x['ManagerChanceMult'], 'gc',x['GuaranteedCardIds'])
for g in ('scg_free_01','scg_free_02','scg_m003_r01','scg_m002_s01','scg_m003_s01','scg_m003_s02'):
    x=G.get(g); print(g, x and (x['GachaType'],x['GuaranteedCardIds'],x['GuaranteedCardMin'],x['GuaranteedCardCounts'],x['SoftCurrencyMin'],x['SoftCurrencyMax'],x['MimicId']))
for i in range(1,11):
    x=G['scg_m%03d_exit'%i]; print(i,x['GuaranteedCardIds'],x['GuaranteedCardMin'],x['GuaranteedCardCounts'],x['SoftCurrencyMin'],x['SoftCurrencyMax'],x['CardMultMax'])
dc=sum(sum(c)-sum(m) for c,m in ((G['scg_m%03d_exit'%i]['GuaranteedCardCounts'],G['scg_m%03d_exit'%i]['GuaranteedCardMin']) for i in range(1,11)))
ds=sum(G['scg_m%03d_exit'%i]['SoftCurrencyMax']-G['scg_m%03d_exit'%i]['SoftCurrencyMin'] for i in range(1,11))
print('exit diff cards',dc,'elixir',ds)
fg=d['FreeGachaCycle'][0]; print({k:v for k,v in fg.items() if k!='GachaIds'}, len(fg['GachaIds']), C.Counter(fg['GachaIds']).most_common(8), fg['GachaIds'][:6])
print('mimic count gacha',sum(1 for g in G.values() if g['MimicId']), 'rocks',sum(1 for r in d['Rocks'] if r['MimicId']))
