import json,sys
J=json.load(open(sys.argv[1]))
cuc=J['CardUpgradeCosts']
print(type(cuc))
for c in (cuc if isinstance(cuc,list) else cuc.values()):
    print({k:v for k,v in c.items()})
print()
cards=J['Cards']
print(list(cards[0].keys()))
for c in cards:
    print(c.get('Id'), c.get('Rarity'), c.get('IsManager'), c.get('MineUnlock'), c.get('StatModifierType'), c.get('TargetIds'), c.get('ModifierBase'), c.get('ModifierMultiplier'), c.get('ModifierGrowth'), c.get('ModifierFormulaType'))
