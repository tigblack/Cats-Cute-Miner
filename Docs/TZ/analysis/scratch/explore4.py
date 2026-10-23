import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z=d['Zones']
print('array order numeric?', [q['Id'] for q in Z]==[f'zone{i}' for i in range(1,145)])
alltok=collections.Counter()
ex=['r:rockonboardingmine1:2','s:amethyst:1:scg_m002_s01:ca002:1','x:block2x3vR','p:checkpoint:5:scg_m005_c01','e:exit:1:scg_m001_exit','c:spawningcart:1']
found={e:[] for e in ex}
clev=collections.Counter(); elev=[]; plev=[]
cids=collections.Counter(); eids=collections.Counter(); pids=collections.Counter()
for zi,z in enumerate(Z):
    g=z['Grid'].split(',')
    for i,t in enumerate(g):
        if t in found and len(found[t])<3: found[t].append((z['Id'],i%7,i//7))
        p=t.split(':')
        if p[0]=='c': clev[p[2]]+=1; cids[p[1]]+=1
        if p[0]=='e': elev.append((zi+1,int(p[2]))); eids[p[1]]+=1
        if p[0]=='p': plev.append((zi+1,int(p[2]))); pids[p[1]]+=1
print(found)
print('c levels',clev,'cids',cids,'eids',eids,'pids',pids)
print('e levels first 20',elev[:20],'last',elev[-5:])
print('e level == zone?', sum(1 for a,b in elev if a==b), 'monotonic', all(elev[i][1]<=elev[i+1][1] for i in range(len(elev)-1)))
print('p levels first', plev[:15])
print('HP keys', sorted(d['MiningTargetHealths'].keys()))
print('spawningcart in HP', 'spawningcart' in d['MiningTargetHealths'])
