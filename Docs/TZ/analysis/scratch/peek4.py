import json,sys
d=json.load(open(sys.argv[1]))
def short(o,n=2500):
    return json.dumps(o,ensure_ascii=False)[:n]
g={x['Id']:x for x in d['Gacha']}
print('gacha keys', list(d['Gacha'][0].keys()))
for gid in ['GachaWooden','GachaIron','GachaCrusher','scg_m001_exit','scg_m002_s01','scg_m005_c01','GachaLegendary']:
    print(gid, short(g.get(gid),1500))
print('Deliveries[0..]'); 
for x in d['Deliveries']: print(short(x,600))
go=d['GeneratorObjectives'][0]; print('GO keys', list(go.keys())); print(short({k:(v[:8] if isinstance(v,list) else v) for k,v in go.items()},1500))
print([x.get('Id', x.get('GeneratorId')) for x in d['GeneratorObjectives']])
