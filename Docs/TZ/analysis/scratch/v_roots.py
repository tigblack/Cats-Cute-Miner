import json,sys
d=json.load(open(sys.argv[1]))
T=open(sys.argv[2]).read()
for k,v in d.items():
    n=len(v) if isinstance(v,(list,dict)) else v
    print(k, type(v).__name__, n if not isinstance(v,str) else v, 'inTZ' if k in T else 'NOT')
bp=d['BalanceProperties']
print('---BP')
for k,v in bp.items():
    print(k, (json.dumps(v)[:80]), 'inTZ' if k in T else 'NOT')
