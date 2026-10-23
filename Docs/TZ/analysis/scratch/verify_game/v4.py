import sys
sys.path.insert(0,'/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/TZ/analysis/scratch')
from common import *
C={c['Id']:c for c in d['Cards']}
for cid in ['ca001','ca002','ca003']: print(cid,{k:C[cid][k] for k in C[cid] if k in ('Rarity','IsManager','TargetIds','MineUnlock')})
for u in d['CardUpgradeCosts'][:1]: print({k:(v[:3] if isinstance(v,list) else v) for k,v in u.items()})
R={r['Id']:r for r in d['Rocks']}
print(R['rocksoftcurrencysmall']['RewardCycle'][:5])
