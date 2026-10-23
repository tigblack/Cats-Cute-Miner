import json,math
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
M={m['Id']:m for m in d['MineShafts']}
s=M['sunstone']; print({k:v for k,v in s.items() if 'Upgrade' in k or 'Currency' in k or 'Delay' in k})
lb,lg=math.log10(s['UpgradeCostBase']),math.log10(s['UpgradeCostGrowth'])
for t in (200,308): print(t, (t-lb)/lg+1)
print([math.ceil(72*1.43**(k)) for k in range(4)],[math.ceil(512*1.46**k) for k in range(3)])
def ft3(b,m,g,p,r,x):
    v=b+m*(g*x)**p
    return round(v/abs(r))*abs(r) if r else v
print([ft3(10,635,0.2,1.15,15,x) for x in (1,2,3)], [10+635*(0.2*x)**1.15 for x in (1,2,3)])
print([750+300*2.1**(x-1) for x in (1,2,3)])
for x in range(1,20): print(x, 0.4+0.125*x, end='; ')
print()
GO={g['Id']:g for g in d['GeneratorObjectives']}
print(list(GO.keys())[:3]); g=GO.get('spawningcart') or d['GeneratorObjectives'][0]; print({k:(v[:6] if isinstance(v,list) else v) for k,v in g.items()})
print(sum(g['ObjectiveCount']))
