import sys
sys.path.insert(0,'/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/TZ/analysis/scratch')
from common import *
G={g['Id']:g for g in d['Gacha']}
R={r['Id']:r for r in d['Rocks']}
for zi in (1,2,3):
    z=ZL[zi-1]; W,D,g,occ,objs=parse(z)
    print('zone',zi,'D',D)
    for r in range(D):
        print('  ',r, ' '.join((t.split(':')[0]+(t.split(':')[1][-6:] if t[0] in 'rsx' else '') if t!='.' else '.').ljust(8) for t in g[r*W:(r+1)*W]))
    toks=[o['tok'] for o in objs if o['k'] in 'spe']; print(toks)
    rocks=collections.Counter(o['p'][1] for o in objs if o['k']=='r'); print(rocks)
def show(gid):
    x=G[gid]; print(gid,{k:v for k,v in x.items() if k in ('GuaranteedCardIds','GuaranteedCardCounts','GuaranteedCardMin','SoftCurrencyMin','SoftCurrencyMax','BaseNumCards','MimicId','CardMultMax','GachaType','CoreCurrencySeconds','HardCurrency')})
for gid in ['scg_m001_exit','scg_m002_s01','scg_m002_exit','scg_m003_s01','scg_m003_s02','scg_m003_r01','scg_m003_exit']:
    if gid in G: show(gid)
print([k for k in G['scg_m002_exit'].keys()])
for rid in ['rockonboardingmine1','rockonboardingmine2','rockonboardingmine3']:
    rc=R[rid]['RewardCycle']; print(rid,len(rc),[ (e['RewardType'],e['DetailedType']) for e in rc])
