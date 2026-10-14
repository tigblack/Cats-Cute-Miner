"""Генерирует zones_summary.csv — эталонные значения по всем 144 зонам для автотестов импорта.
Запуск: python3 zones_summary.py <json-data.json> > zones_summary.csv"""
import json, sys, csv
d = json.load(open(sys.argv[1]))
dims = {}
for sec in ('Rocks', 'MineShafts', 'Obstructions'):
    for o in d[sec]:
        dims[o['Id']] = (o['WidthCells'], o['DepthCells'])
w = csv.writer(sys.stdout, lineterminator='\n')
w.writerow(['zone', 'width', 'depth', 'r', 's', 'p', 'x', 'occupied_cells_rsx', 'exit_anchor', 'exit_level', 'exit_gacha',
            'cart_anchor', 'gate_rows', 'gate_levels', 'sectors', 'shafts(id:level:card:cardLevel)'])
for z in d['Zones']:
    W, D = z['WidthCells'], z['DepthCells']
    cnt = {'r': 0, 's': 0, 'p': 0, 'x': 0}
    occ = 0; gates = []; shafts = []; e = c = None
    for i, t in enumerate(z['Grid'].split(',')):
        p = t.split(':'); col, row = i % W, i // W
        k = p[0]
        if k in cnt: cnt[k] += 1
        if k in 'rsx' and k != '.':
            a, b = dims[p[1]]; occ += a * b
        if k == 's': shafts.append(f'{p[1]}:{p[2]}:{p[4]}:{p[5]}')
        if k == 'p': gates.append((row, int(p[2])))
        if k == 'e': e = (col, row, int(p[2]), p[3])
        if k == 'c': c = (col, row)
    gates.sort()
    w.writerow([z['Id'], W, D, cnt['r'], cnt['s'], cnt['p'], cnt['x'], occ, f'{e[0]};{e[1]}', e[2], e[3],
                f'{c[0]};{c[1]}', ';'.join(str(g[0]) for g in gates), ';'.join(str(g[1]) for g in gates),
                len(gates) + 1, ' '.join(shafts)])
