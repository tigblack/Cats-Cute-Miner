import json,sys
d=json.load(open(sys.argv[1]))
def short(o,n=2500):
    return json.dumps(o,ensure_ascii=False)[:n]
cards={c['Id']:c for c in d['Cards']}
print('card keys', list(d['Cards'][0].keys()))
for cid in ['ca001','ca002','ca029','ca034','ca038']:
    print(cid, short(cards[cid],1200))
nonman=[c for c in d['Cards'] if not c.get('IsManager')]
from collections import Counter
print('non-manager StatModifierType', Counter(str(c.get('StatModifierType')) for c in nonman))
for c in nonman: print(c['Id'], c.get('Rarity'), c.get('StatModifierType'), c.get('ModifierBase'), c.get('ModifierMultiplier'), c.get('ModifierGrowth'), c.get('ModifierFormulaType'), c.get('TargetIds'), c.get('MineUnlock'), c.get('Name', c.get('NameKey','')))
print('CUC', short(d['CardUpgradeCosts'],3000))
