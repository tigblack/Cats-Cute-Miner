import json,sys
d=json.load(open(sys.argv[1]))
def short(o,n=1500):
    s=json.dumps(o,ensure_ascii=False)
    return s[:n]
print(list(d.keys()))
for k in ['BalanceProperties','Reinforcements','Miners','SpawningCart','CheckPoint','Spells','Exit']:
    print('==',k); print(short(d.get(k),4000))
