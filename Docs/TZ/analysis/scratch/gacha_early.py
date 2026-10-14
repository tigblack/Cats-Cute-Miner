from common import *
G={g['Id']:g for g in d['Gacha']}
print(list(G['scg_m001_exit'].keys()))
for gid in ['scg_m001_exit','scg_m002_s01','scg_m002_exit','scg_m003_s01','scg_m003_s02','scg_m003_exit','scg_m004_s01','scg_m004_exit','scg_m005_c01','GachaIron','GachaForged','GachaCrusher','GachaWooden','scg_free_01']:
    g=G.get(gid)
    if g: print(gid, {k:v for k,v in g.items() if v not in (0,None,[],'',0.0)})
