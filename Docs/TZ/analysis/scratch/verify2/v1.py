import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
print(list(d.keys()))
bp=d['BalanceProperties']; bp=bp[0] if isinstance(bp,list) else bp
print({k:v for k,v in bp.items() if not isinstance(v,(list,dict))})
print({k:v for k,v in bp.items() if isinstance(v,(list,dict))})
g={x['Id']:x for x in d['Gacha']}
for k in ['GachaWooden','GachaIron','GachaCrusher','scg_m001_exit','scg_m002_s01','GachaForged']:
    print(k, {a:b for a,b in g[k].items()})
cards=d['Cards']
print(collections.Counter(c['Rarity'] for c in cards))
print(collections.Counter((c['Rarity'],c['IsManager']) for c in cards))
print(cards[0].keys())
for c in cards:
    if not c['IsManager']: print(c['Id'],c['Rarity'],c.get('StatModifierType'),c.get('ModifierFormulaType'),c.get('ModifierBase'),c.get('ModifierMultiplier'),c.get('ModifierGrowth'),c.get('TargetIds'),c.get('MineUnlock'))
