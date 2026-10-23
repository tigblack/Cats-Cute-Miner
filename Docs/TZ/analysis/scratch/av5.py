import json,collections
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
rt=collections.defaultdict(set)
def walk(o,sec):
    if isinstance(o,dict):
        if 'RewardType' in o: rt[o['RewardType']].add((sec,o.get('DetailedType') if o['RewardType'] in (0,4,9) else '...'))
        for v in o.values(): walk(v,sec)
    elif isinstance(o,list):
        for v in o: walk(v,sec)
for k,v in J.items(): walk(v,k)
for t in sorted(rt): print(t, sorted({s for s,_ in rt[t]}), sorted({d for _,d in rt[t] if d})[:8])
R={r['Id']:r for r in J['Rocks']}
for rid,r in R.items():
    print(rid, {k:v for k,v in r.items() if k.startswith('SoftCurrency') or k.startswith('CoreCurrencySeconds')})
rm=J['Zones'][0]['RankMultipliers'][0]; print(rm)
Z=sorted(J['Zones'],key=lambda z:int(z['Id'][4:]))
for f in rm:
    vals=[z['RankMultipliers'][0][f] for z in Z]; print(f,min(vals),max(vals),vals[0],vals[-1])
print('CT',collections.Counter(m['CurrencyType'] for m in J['MineShafts']))
