import sys; sys.path.insert(0,'..')
sys.path.insert(0,'/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/TZ/analysis/scratch')
from common import *
import json
print('Reinf', json.dumps(d['Reinforcements'])[:800])
print('Cart', json.dumps(d['SpawningCart'])[:1200])
print('Spells', json.dumps(d['Spells'])[:600])
print('CheckPoint', json.dumps(d['CheckPoint'])[:600])
for x in d['Deliveries']:
    print({k:v for k,v in x.items() if k not in ()})
bp=d['BalanceProperties']
print({k:v for k,v in bp.items() if 'Deliver' in k or 'Spawn' in k or 'Unit' in k or 'Offline' in k})
m=[m for m in d['Miners']][0]; print(m)
