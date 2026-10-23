import json, collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
G={g['Id']:g for g in d['Gacha']}
R={r['Id']:r for r in d['Rocks']}
def base(gid):
    g=G[gid]; return g['BaseNumCards']+sum(g['GuaranteedCardCounts'])
chests=collections.Counter(); ctr=collections.Counter()
for z in d['Zones']:
    for t in z['Grid'].split(','):
        p=t.split(':')
        if p[0]=='r':
            rc=R[p[1]]['RewardCycle']; x=rc[ctr[p[1]]%len(rc)]; ctr[p[1]]+=1
            if x['RewardType']==1: chests[x['DetailedType']]+=1
        elif p[0] in 'spe': chests[p[3]]+=1
tot=sum(base(g)*c for g,c in chests.items())
ex=sum(base(g)*c for g,c in chests.items() if g=='GachaCrusher' or g.endswith('_exit'))
fc=d['FreeGachaCycle'][0]['GachaIds']; free=sum(base(g) for g in fc)
print('map chests',sum(chests.values()),'base cards',tot,'exit',ex,round(ex/tot,3),'free',free,round(free/tot,3))
ee=[g for g in G if G[g]['EventEpicWeight']>0]; print('EventEpic>0 gachas',ee,'on map:',{g:chests[g] for g in ee})
# soft from exits min vs max
mn=sum(G[g]['SoftCurrencyMin'] for g in chests if g.endswith('_exit')); mx=sum(G[g]['SoftCurrencyMax'] for g in chests if g.endswith('_exit'))
print('scg exit soft min sum',mn,'max sum',mx)
gm=sum(sum(G[g]['GuaranteedCardMin']) for g in chests if g.endswith('_exit')); gc=sum(sum(G[g]['GuaranteedCardCounts']) for g in chests if g.endswith('_exit'))
print('scg exit guaranteed min',gm,'counts',gc)
