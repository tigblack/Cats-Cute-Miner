import json,collections,math
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
G={g['Id']:g for g in J['Gacha']}; C={c['Id']:c for c in J['Cards']}
cards=collections.defaultdict(list)
for k,g in G.items():
    if k.startswith('scg_m'):
        for c in g['GuaranteedCardIds']: cards[c].append(k)
for c in sorted(cards): print(c,C[c]['IsManager'],C[c]['StatModifierType'],cards[c])
print('non-monotonic', [g['GeneratorId'] for g in J['GeneratorObjectives'] if any(b<a for a,b in zip(g['ObjectiveCount'],g['ObjectiveCount'][1:]))])
print('stat codes',collections.Counter(c['StatModifierType'] for c in J['Cards']))
# FT checks
fts=collections.defaultdict(list)
def walk(o,path):
    if isinstance(o,dict):
        for k,v in o.items():
            if k.endswith('FormulaType') and isinstance(v,int):
                p=k[:-len('FormulaType')]
                vals=tuple(o.get(p+s) for s in ['Base','Multiplier','Growth','Power','Round'])
                fts[v].append((path+'.'+p,vals))
            walk(v,path+'.'+k)
    elif isinstance(o,list):
        for i,v in enumerate(o): walk(v,path+(('['+str(o[0].get('Id'))+']') if isinstance(o[0],dict) and False else '[]'))
for k in ['Miners','Reinforcements','SpawningCart','MineShafts','Cards','Spells','Deliveries','Rocks','GeneratorObjectives']: walk(J[k],k)
for ft in sorted(fts):
    c=collections.Counter((p,v) for p,v in fts[ft])
    print('FT',ft,len(fts[ft]))
    seen=set()
    for (p,v),n in c.items():
        key=(p.split('.')[-1],v)
        if v[0]==None and v[1]==None: continue
        if key in seen: continue
        seen.add(key)
        if len(seen)<40: print('   ',p,v,n)
