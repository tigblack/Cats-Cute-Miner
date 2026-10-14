import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
G={g['Id']:g for g in d['Gacha']}
Z=d['Zones']
# where each gacha is referenced
ref=collections.defaultdict(list)
for zi,z in enumerate(Z):
    for i,t in enumerate(z['Grid'].split(',')):
        p=t.split(':')
        if p[0]=='s': ref[p[3]].append((zi+1,'s',p[1],p[4],p[5]))
        if p[0] in 'pe': ref[p[3]].append((zi+1,p[0]))
rocks={r['Id']:r for r in d['Rocks']}
for r in d['Rocks']:
    for x in r['RewardCycle']:
        if x['RewardType']==1: ref[x['DetailedType']].append(('rock',r['Id']))
for g in d['Gacha']:
    if g['Id'].startswith('scg'):
        rr=ref.get(g['Id'],[])
        print(g['Id'].ljust(16),'T',g['GachaType'],'mim',g['MimicId'].ljust(14),'ids',g['GuaranteedCardIds'],'min',g['GuaranteedCardMin'],'cnt',g['GuaranteedCardCounts'],'soft',g['SoftCurrencyMin'],g['SoftCurrencyMax'],'cmm',g['CardMultMax'],'bnc',g['BaseNumCards'],'ref',rr[:3])
print('mineunlock max', max(c['MineUnlock'] for c in d['Cards']))
# s tokens list early zones
for zi,z in enumerate(Z[:12]):
    for i,t in enumerate(z['Grid'].split(',')):
        if t.startswith('s:') or t.startswith('p:') or t.startswith('e:'): print(zi+1, i%7, i//7, t)
