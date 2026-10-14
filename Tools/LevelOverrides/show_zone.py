#!/usr/bin/env python3
"""Печать занятости зоны: якоря и footprint (r/s/x) поверх исходной сетки, опционально с override.

python3 Tools/LevelOverrides/show_zone.py 1 [--overrides Assets/Game/Resources/Outpost/level_overrides.json]
"""
import argparse
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def tables(d):
    return ({e['Id']: e for e in d['Rocks']}, {e['Id']: e for e in d['MineShafts']}, {e['Id']: e for e in d['Obstructions']})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('zone', type=int)
    ap.add_argument('--source', default=os.path.join(ROOT, 'Assets/Game/Data/Source/json-data.json'))
    ap.add_argument('--overrides')
    a = ap.parse_args()
    d = json.load(open(a.source, encoding='utf-8'))
    R, S, O = tables(d)
    z = d['Zones'][a.zone - 1]
    w, h = z['WidthCells'], z['DepthCells']
    toks = z['Grid'].split(',')
    extra = []
    if a.overrides:
        ov = json.load(open(a.overrides, encoding='utf-8'))['Zones'].get(z['Id'])
        if ov:
            for c in ov.get('AddTokens', []):
                extra.append((c['Row'] * w + c['Column'], c['Token']))
    occ = ['.'] * (w * h)
    for idx, t in list(enumerate(toks)) + extra:
        if t == '.':
            continue
        f = t.split(':')
        kind = f[0]
        size = {'r': R, 's': S, 'x': O}.get(kind)
        col, row = idx % w, idx // w
        if size is None:
            if occ[idx] == '.':
                occ[idx] = kind
            continue
        e = size[f[1]]
        for dx in range(e['WidthCells']):
            for dz in range(e['DepthCells']):
                cell = (row - dz) * w + col + dx
                occ[cell] = kind.upper() if (dx, dz) == (0, 0) else kind
    print(z['Id'], w, 'x', h)
    for r in range(h):
        print('%2d ' % r + ' '.join(occ[r * w:(r + 1) * w]))


if __name__ == '__main__':
    main()
