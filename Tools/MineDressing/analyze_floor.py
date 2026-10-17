#!/usr/bin/env python3
"""Floor statistics per mine (read-only): free floor, wall-adjacent cells, factories near walls.
Used to pick the 25-mine sample and the dressing densities. Writes nothing unless --out is given."""
import json, sys, argparse
from pathlib import Path
from collections import deque
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Topology'))
import topology_check as tc
from build_topology import source_targets, SOURCE, OVERRIDES

def stats():
    data = json.loads(SOURCE.read_text())
    overrides = json.loads(OVERRIDES.read_text())
    topo = json.loads((ROOT / 'Assets/Game/Resources/Outpost/mine_topology.json').read_text())
    rows = []
    for n, zone in enumerate(data['Zones'], 1):
        targets, _ = source_targets(data, zone, overrides['Zones'].get(zone['Id']))
        snap = topo['Zones'][zone['Id']]
        m = tc.TopologyMap(snap, targets)
        W, D = m.width, m.depth
        mine_cells = [c for c in range(W * D) if m.playable[c]]
        occ = set(c for t in m.targets if t['kind'] != 'p' for c in t['cells'])
        gate = set(c for c in range(W * D) if m.gate_order[c] >= 0)
        free = [c for c in mine_cells if c not in occ and c not in gate]
        def rock(c, r):
            return not (0 <= c < W and 0 <= r < D) or not m.playable[r * W + c]
        wall_adj = [c for c in free if any(rock(c % W + dc, c // W + dr) for dc, dr in ((1,0),(-1,0),(0,1),(0,-1)))]
        kinds = {}
        for t in m.targets:
            kinds[t['kind']] = kinds.get(t['kind'], 0) + 1
        # factory: min manhattan distance from footprint to a rock cell (0 = touches a wall)
        fdist = []
        for t in m.targets:
            if t['kind'] != 's':
                continue
            best = 99
            cells = t['cells']
            for c in cells:
                col, row = c % W, c // W
                for rad in range(0, 8):
                    hit = False
                    for dc in range(-rad - 1, rad + 2):
                        for dr in range(-rad - 1, rad + 2):
                            if abs(dc) + abs(dr) != rad + 1:
                                continue
                            if rock(col + dc, row + dr) and (col + dc, row + dr) != (col, row):
                                hit = True
                    if hit:
                        best = min(best, rad)
                        break
            fdist.append(best)
        branches = len({p['Branch'] for p in snap['Placements']} - {0})
        rows.append(dict(zone=n, id=zone['Id'], layout=snap['Layout'], w=W, d=D, gates=len(snap['Gates']), branches=branches,
                         floor=len(mine_cells), occupied=len(occ), free=len(free), wallAdjFree=len(wall_adj),
                         r=kinds.get('r', 0), x=kinds.get('x', 0), s=kinds.get('s', 0), p=kinds.get('p', 0),
                         factoryWallDist=fdist))
    return rows

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--out')
    a = ap.parse_args()
    rows = stats()
    if a.out:
        Path(a.out).write_text(json.dumps(rows, indent=1))
    print('zone layout            WxD    g br floor occ  free wadj  r   x  s  fwall')
    for r in rows:
        print(f"{r['zone']:4d} {r['layout'][:18]:18s} {r['w']:2d}x{r['d']:<3d} {r['gates']:2d} {r['branches']:2d} {r['floor']:5d} {r['occupied']:4d} {r['free']:5d} {r['wallAdjFree']:4d} {r['r']:4d} {r['x']:3d} {r['s']:2d} {r['factoryWallDist']}")
