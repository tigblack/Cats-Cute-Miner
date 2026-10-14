import sys; sys.path.insert(0,'..')
from common import *
for x in d['Gacha']:
    if 'ca001' in x['GuaranteedCardIds']:
        i=x['GuaranteedCardIds'].index('ca001'); print(x['Id'],x['GuaranteedCardCounts'][i],x['GuaranteedCardMin'])
# who requires ca002 in zone2..3
for zn in ('zone2','zone3','zone4'):
    print(zn,[t for t in Z[zn]['Grid'].split(',') if t[:1] in 'spe'])
for r in d['Rocks']:
    if r['Id'] in ('rockbasic','rockonboardingmine2','rockonboardingmine5'):
        rc=r['RewardCycle']; print(r['Id'],len(rc),rc[:3])
import collections
rb=[r for r in d['Rocks'] if r['Id']=='rockbasic'][0]
print(collections.Counter((x['RewardType'],x['DetailedType']) for x in rb['RewardCycle']))
print({k:v for k,v in rb.items() if k!='RewardCycle'})
