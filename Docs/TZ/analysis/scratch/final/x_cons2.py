import json,sys,collections
d=json.load(open(sys.argv[1]))
G={g['Id']:g for g in d['Gacha']}
print('Gacha keys', sorted(d['Gacha'][0].keys()))
print('scg count', sum(1 for g in G if g.startswith('scg_')), [g for g in G if not g.startswith('scg_')])
print('scg with GCMin', sum(1 for g in G.values() if g['Id'].startswith('scg_') and g.get('GuaranteedCardMin')))
print('GachaType2', sum(1 for g in G.values() if g['GachaType']==2))
for gid in ('GachaWooden','scg_free_01','scg_free_02','scg_m002_s01','scg_m002_exit','scg_m003_exit'):
    g=G[gid]; print(gid,{k:g[k] for k in g if k not in ('Id',)})
bp=d['BalanceProperties'][0]
print('stack', bp['CardStackCounts'], bp['CommonStackSplitAt'], bp['UncommonStackSplitAt'], bp['RareStackSplitAt'], bp['EventEpicStackSplitAt'])
fg=d['FreeGachaCycle'][0]; print('FGC keys', {k:(v if not isinstance(v,list) else len(v)) for k,v in fg.items()})
