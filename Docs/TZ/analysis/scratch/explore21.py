import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
for zi,z in enumerate(Z):
    g=z['Grid'].split(',')
    for i,t in enumerate(g):
        if t.startswith('c:'):
            r=i//7
            if z['DepthCells']-r!=4: print('c row',z['Id'],r,z['DepthCells'])
# ca001 grants
n=0;lst=[]
for g in d['Gacha']:
    for cid,cnt in zip(g['GuaranteedCardIds'],g['GuaranteedCardCounts']):
        if cid=='ca001': n+=cnt; lst.append((g['Id'],cnt))
print('ca001',n,lst)
# non-manager cards in scripted
for g in d['Gacha']:
    for cid in g['GuaranteedCardIds']:
        c=[c for c in d['Cards'] if c['Id']==cid][0]
        if not c['IsManager']: print('nonmgr',g['Id'],cid,c['StatModifierType'])
# gates per zone
gc=collections.Counter(sum(1 for t in z['Grid'].split(',') if t.startswith('p:')) for z in Z)
print('gates',gc)
print('zones no gate',[z['Id'] for z in Z if 'p:' not in z['Grid']])
# rank-ish fields
print('Quests ranks',sorted(set(q['Rank'] for q in d['Quests'])))
print('BP keys', [k for k in d['BalanceProperties'][0]])
