import json,sys
J=json.load(open(sys.argv[1]))
print(list(J.keys()))
go=J['GeneratorObjectives']
print(type(go), len(go))
for g in (go if isinstance(go,list) else go.values()):
    gid=g.get('Id') or g.get('GeneratorId')
    if gid in ('spawningcart','amethyst','citrine','agate'):
        oc=g['ObjectiveCount'] if 'ObjectiveCount' in g else None
        print(gid, {k:(v if not isinstance(v,list) else f'list{len(v)}') for k,v in g.items()})
