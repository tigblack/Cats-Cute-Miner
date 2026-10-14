import json,sys,collections,csv
d=json.load(open(sys.argv[1]))
Z=d['Zones']
rows=list(csv.DictReader(open('/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/TZ/analysis/zones_summary.csv')))
print('no gates', [r['zone'] for r in rows if r['p']=='0'])
print('max depth', max((int(r['depth']),r['zone']) for r in rows), [r['zone'] for r in rows if r['depth']=='64'])
print('sectors', sum(int(r['sectors']) for r in rows))
G={g['Id']:g for g in d['Gacha']}
print('ca062 guaranteed', [g for g in G if 'ca062' in G[g]['GuaranteedCardIds']])
# min N per zone/class
cls={0:'Normal',1:'Premium',3:'Rare'}
mn=[]
for z in Z:
    rm=z['RankMultipliers'][0]
    for g in G.values():
        if g['GachaType'] in cls and g['BaseNumCards']>0:
            v=g['BaseNumCards']*rm['GachaCardsMult'+cls[g['GachaType']]]
            if round(v)==0: mn.append((z['Id'],g['Id'],v))
print('N rounds to 0:', len(mn), mn[:5])
# which gacha are used by rocks
R={r['Id']:r for r in d['Rocks']}
used=collections.Counter()
for r in R.values():
    for e in r.get('RewardCycle') or []:
        if e['RewardType']==1: used[e['DetailedType']]+=1
print('rock gachas', dict(used))
# FreeGacha ids set
fg=d['FreeGachaCycle'][0]['GachaIds']; print('free set', collections.Counter(fg).most_common(8))
# Gacha types of zone-used free
print('free types', collections.Counter(G[x]['GachaType'] for x in fg))
# exit reward first zones vs Appendix
for zi in range(10):
    t=[x for x in Z[zi]['Grid'].split(',') if x.startswith('e:')]; print(Z[zi]['Id'], t)
