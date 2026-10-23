import sys; sys.path.insert(0,'..')
from common import *
for x in d['Gacha']:
    if x['EventEpicWeight'] or x['GachaType']!=2: print(x['Id'],x['GachaType'],x['BaseNumCards'],x['UncommonWeight'],x['RareWeight'],x['LegendaryWeight'],x['EventEpicWeight'],x['MimicId'])
print([(c['Id'],c['MineUnlock'],c['TargetIds']) for c in d['Cards'] if c['IsManager']][:8])
f=d['FreeGachaCycle']; print(type(f), (f.keys() if isinstance(f,dict) else f[0].keys()))
print(d['CrushableRewards'][:3])
