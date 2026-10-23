import json,sys,math
J=json.load(open(sys.argv[1]))
G={g['Id']:g for g in J['Gacha']}
# fandom wiki Chests table: name -> (cards, {rarity: at least})
wiki={'GachaWooden':(24,{'U':3}),'GachaIron':(48,{'U':6}),'GachaForged':(68,{'U':11}),'GachaSilver':(170,{'U':11}),
'GachaGolden':(102,{'U':25,'R':2}),'GachaRoyal':(127,{'U':31,'R':12}),'GachaRare':(10,{'R':10}),'GachaEpic':(6,{'E':6}),
'GachaLongEpic':(11,{'E':11}),'GachaLegendary':(1,{'L':1}),'GachaMythical':(380,{'U':76,'R':38,'L':1})}
multkey={0:'GachaCardsMultNormal',1:'GachaCardsMultPremium',2:None,3:'GachaCardsMultRare'}
zones=sorted(J['Zones'],key=lambda z:int(z['Id'][4:]))
match=[]
for z in zones:
    rm=z['RankMultipliers'][0]; ok=True
    for gid,(n,_) in wiki.items():
        g=G[gid]; k=multkey[g['GachaType']]; m=rm[k] if k else 1
        if round(g['BaseNumCards']*m)!=n: ok=False;break
    if ok: match.append(z['Id'])
print('zones where round(BaseNumCards*mult)==wiki for all chests:',match)
for gid,(n,mins) in wiki.items():
    g=G[gid]
    w={'U':g['UncommonWeight'],'R':g['RareWeight'],'L':g['LegendaryWeight'],'E':g['EventEpicWeight']}
    calc={r:(math.floor(n/w[r]) if w[r] else None) for r in mins}
    print(gid,'type',g['GachaType'],'base',g['BaseNumCards'],'wiki cards',n,'ratio %.3f'%(n/g['BaseNumCards']),'wiki min',mins,'floor(n/weight)',calc, 'OK' if calc==mins else 'DIFF')
