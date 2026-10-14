import json, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
G={g['Id']:g for g in d['Gacha']}
R={r['Id']:r for r in d['Rocks']}
def base(gid):
    g=G[gid]; return g['BaseNumCards']+sum(g['GuaranteedCardCounts'])
map_chests=collections.Counter()
rockcount=collections.Counter()
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r':
            rc=R[p[1]]['RewardCycle']
            if len(rc)==1 and rc[0]['RewardType']==1: map_chests[rc[0]['DetailedType']]+=1
            elif p[1]=='rockbasic': rockcount['rockbasic']+=1
            else:
                for x in rc:
                    if x['RewardType']==1: map_chests[x['DetailedType']]+=1
        elif p[0]=='s': map_chests[p[3]]+=1
        elif p[0] in 'pe': map_chests[p[3]]+=1
# rockbasic cycle: assume cyclic counter global
rb=R['rockbasic']['RewardCycle']; n=rockcount['rockbasic']
for k in range(n):
    x=rb[k%len(rb)]
    if x['RewardType']==1: map_chests[x['DetailedType']]+=1
tot_map=sum(base(g)*c for g,c in map_chests.items())
fc=d['FreeGachaCycle'][0]['GachaIds']
tot_free=sum(base(g) for g in fc)
print('map chests', sum(map_chests.values()), 'base cards (unscaled)', tot_map)
print('free cycle chests',len(fc),'base cards (unscaled)',tot_free, 'hours to exhaust',len(fc)*4)
crusher=map_chests['GachaCrusher']*base('GachaCrusher')+sum(base(g)*c for g,c in map_chests.items() if g.endswith('_exit'))
print('exit chests base cards',crusher, 'share of map', round(crusher/tot_map,3))
print('top', map_chests.most_common(10))
# legendary chance in free cycle
print('soft from free cycle (mid)',sum((G[g]['SoftCurrencyMin']+G[g]['SoftCurrencyMax'])/2 for g in fc))
