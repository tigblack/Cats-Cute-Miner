import json, collections, sys
d=json.load(open(sys.argv[1]))
hp=d['MiningTargetHealths']
for t,l in [('rockonboardingmine1',1),('rockonboardingmine1',2),('rockbasic',3),('amethyst',1),('checkpoint',5),('exit',1)]:
    print('HP',t,l,hp[t][l-1])
print('HP checkpoint first',hp['checkpoint'][:8],'exit first',hp['exit'][:5],'max values',max(max(v) for v in hp.values()))
C=d['Cards']
print('cards',len(C),'managers',sum(c['IsManager'] for c in C))
print('rarity',collections.Counter(c['Rarity'] for c in C))
print('StatModifierType',collections.Counter((c['StatModifierType'],c['IsManager']) for c in C))
print('formula types',collections.Counter(c['ModifierFormulaType'] for c in C))
for c in C:
    if not c['IsManager']: print(' nonmgr',c['Id'],c['Rarity'],c['MineUnlock'],c['StatModifierType'],c['ModifierBase'],c['ModifierMultiplier'],c['ModifierGrowth'],c['ModifierPower'],c['ModifierRound'],c['ModifierFormulaType'],c['TargetIds'])
mg=[c for c in C if c['IsManager']]
print('mgr sample',[ (c['Id'],c['Rarity'],c['MineUnlock'],c['ModifierBase'],c['ModifierMultiplier'],c['ModifierGrowth'],c['ModifierFormulaType'],c['TargetIds']) for c in mg[:5]])
print('mgr rarities',collections.Counter(c['Rarity'] for c in mg),'mgr targets count',collections.Counter(len(c['TargetIds']) for c in mg))
for cu in d['CardUpgradeCosts']: print('CUC',cu['Rarity'],len(cu['Duplicates']),len(cu['SoftCurrency']),cu['Duplicates'][:4],cu['SoftCurrency'][:4])
# required card levels in s tokens
req=collections.Counter(); reqcards=collections.defaultdict(set); mismatch=[]
cards={c['Id']:c for c in C}
shaftcards=collections.defaultdict(set)
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='s':
            req[int(p[5])]+=1; reqcards[p[4]].add(int(p[5])); shaftcards[p[1]].add(p[4])
            c=cards[p[4]]
            if not c['IsManager']: mismatch.append(('notmanager',t))
            if p[1] not in c['TargetIds']: mismatch.append(('target',t,c['TargetIds']))
print('required card levels',sorted(req.items()))
print('mismatch',mismatch[:5],len(mismatch))
print('shaft->cards',{k:sorted(v) for k,v in shaftcards.items()})
mxr=max(req); print('max req',mxr)
for c in mg:
    if c['Id'] in reqcards: pass
print('managers never required by s tokens', [c['Id'] for c in mg if c['Id'] not in reqcards])
print('mgr card req by rarity', collections.Counter((cards[k]['Rarity'],max(v)) for k,v in reqcards.items()))
