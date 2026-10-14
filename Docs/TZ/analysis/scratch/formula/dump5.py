import json,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
H=d['MiningTargetHealths']
print(list(H.keys())[:60])
for k in ['rockonboardingmine1','rockonboardingmine2','rockbasic','amethyst','citrine','checkpoint','exit']:
    v=H[k]
    print(k, type(v), (v[:14] if isinstance(v,list) else str(v)[:300]))
Z={z['Id']:z for z in d['Zones']}
for zid in ['zone1','zone2','zone3','zone4','zone5']:
    z=Z[zid]; g=z['Grid'].split(','); W=z['WidthCells']
    toks=[(i//W,i%W,t) for i,t in enumerate(g) if t!='.' and not t.startswith('x')]
    print(zid, z['DepthCells'], toks)
