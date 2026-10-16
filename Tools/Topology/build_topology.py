#!/usr/bin/env python3
"""Compile immutable source content into branching, reproducible mine masks.

No economy data is generated here. Target IDs, types, footprints and sectors come
from the original JSON and the existing content override. Only positions change.
Coordinates are built from the entrance upward, then converted to game rows.

wide-branching-mines-3 (TZ 2.2 §4.1): an opened factory (former deposit "s") keeps its
footprint as a building. compile_zone() still builds the wide-branching-mines-2 geometry
unchanged; a fix step (fix_zone) is applied only to mines that fail the validator with
standing factories (topology_check.py, the Python copy of OutpostReachability). It first
swaps factories with r/x blocks of the same sector (rearrangement), then fills empty cut-off
floor pockets, then carves a passage through rock in the same sector. IDs, footprints,
sectors, gates and the exit never change. Unchanged mines are compared with the previous
mine_topology.json without the Version field. Report: Docs/Reports/topology/factories.json.

Usage: python3 Tools/Topology/build_topology.py [--check]
  --check  regenerate in memory and compare with the files on disk; nothing is written.
"""
import argparse
import collections
import copy
import hashlib
import json
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import topology_check as tc  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/Game/Data/Source/json-data.json'
OVERRIDES = ROOT / 'Assets/Game/Resources/Outpost/level_overrides.json'
OUT = ROOT / 'Assets/Game/Resources/Outpost/mine_topology.json'
GENERATION_REPORT = ROOT / 'Docs/Reports/topology/generation.json'
FACTORIES_REPORT = ROOT / 'Docs/Reports/topology/factories.json'
VERSION = 'wide-branching-mines-3'
# Geometry of compile_zone(); the fix step for standing factories turns it into VERSION.
BASE_VERSION = 'wide-branching-mines-2'


def source_targets(data, zone, override):
    sizes = {x['Id']: (x['WidthCells'], x['DepthCells'])
             for key in ('Rocks', 'MineShafts', 'Obstructions') for x in data[key]}
    tokens = zone['Grid'].split(',')
    for a in (override or {}).get('AddTokens', []):
        i = a['Row'] * zone['WidthCells'] + a['Column']
        assert tokens[i] == '.'
        tokens[i] = a['Token']
    gates = sorted((i for i, t in enumerate(tokens) if t.startswith('p:')),
                   key=lambda i: (-(i // zone['WidthCells']), i))
    # Source sectors use the bottom row of a footprint, exactly as the adapter.
    targets = []
    for i, token in enumerate(tokens):
        if token[0] not in 'rsxp':
            continue
        parts = token.split(':')
        w, h = (1, 1) if parts[0] == 'p' else sizes[parts[1]]
        sector = gates.index(i) if parts[0] == 'p' else sum(g // zone['WidthCells'] > i // zone['WidthCells'] for g in gates)
        targets.append(dict(id=i, kind=parts[0], w=w, h=h, sector=sector,
                            level=int(parts[2]) if parts[0] in 'rsp' else 0))
    return targets, gates


def compile_zone(data, zone, override, number):
    targets, gates = source_targets(data, zone, override)
    rng = random.Random(0xCA7B2000 + number * 7919 + zone['DecoSeed'])
    width = 25 + 2 * ((number * 5) % 3) + 6 * min(4, (number - 1) // 30)
    floor, sectors, occupied, main_floor = set(), {}, set(), set()
    placements, barriers, branch_ids = {}, [], {}
    rooms, loops, junctions = [], [], []
    center = width // 2
    y, spine = 0, center - 1
    family = ('forked-veins', 'winding-galleries', 'ore-archipelago', 'braided-tunnels')[number % 4]

    def carve(x, yy, sector):
        assert 0 <= x < width and yy >= 0
        assert (x, yy) not in sectors or sectors[(x, yy)] == sector
        floor.add((x, yy))
        sectors[(x, yy)] = sector

    def line(x0, y0, x1, y1, sector, main=False):
        cells = [(x, y0) for x in range(min(x0, x1), max(x0, x1) + 1)]
        cells += [(x1, yy) for yy in range(min(y0, y1), max(y0, y1) + 1)]
        for x, yy in cells:
            carve(x, yy, sector)
            if main:
                main_floor.add((x, yy))

    def trunk(x0, y0, x1, y1, sector):
        # Both lanes turn together; cats can walk past each other without collision.
        for lane in (0, 1):
            line(x0 + lane, y0, x1 + lane, y1, sector, True)

    for sector in range(len(gates) + 1):
        pending = [t for t in targets if t['sector'] == sector and t['kind'] != 'p']
        intro_targets = []
        if number == 1 and sector == 0:
            intro_targets = sorted((t for t in pending if t['kind'] == 'r' and t['w'] == t['h'] == 1),
                                   key=lambda t: (t['level'], -t['id']))[:8]
            pending = [t for t in pending if t not in intro_targets]
        rng.shuffle(pending)
        pending.sort(key=lambda t: (-t['w'] * t['h'], -t['w']))
        previous_rooms = {}
        trunk(spine, y, spine, y + 1, sector)
        y += 2
        module = 0
        while pending:
            # The main route bends through rock between independent work branches.
            wander = 2
            next_spine = center - 1 + rng.randint(-wander, wander)
            trunk(spine, y - 1, next_spine, y, sector)
            spine = next_spine
            sides = rng.sample((0, 1), 2)
            if module and family != 'forked-veins' and rng.random() < 0.22:
                sides = sides[:1]
            actual_top = y + 1
            allocated_this_module = 0
            module_rooms = []
            for side in sides:
                if not pending:
                    break
                rw = rng.randint(5, 7) if number <= 18 else rng.randint(7, 10)
                rh = rng.randint(4, 6) if number <= 18 else rng.randint(5, 8)
                distance = rng.randint(2, 3 if number <= 12 else max(3, width // 5))
                if side == 0:
                    x1 = max(rw, spine - distance - 1)
                    x0 = max(1, x1 - rw + 1)
                    x1 = min(x1, spine - 2)
                    aisle = x1
                else:
                    x0 = min(width - rw - 1, spine + 2 + distance)
                    x0 = max(x0, spine + 3)
                    x1 = min(width - 2, x0 + rw - 1)
                    aisle = x0
                bottom = y + rng.randint(1, 3)
                reserved = {(aisle, yy) for yy in range(bottom, bottom + rh)}
                room_occupied, allocated = set(), []
                for t in list(pending):
                    if t['w'] > x1 - x0 or t['h'] > rh:
                        continue
                    found = None
                    # Large deposits go towards the end of a branch, away from its clear approach aisle.
                    xs = list(range(x0, x1 - t['w'] + 2))
                    if side == 1:
                        xs.reverse()
                    for yy in range(bottom, bottom + rh - t['h'] + 1):
                        for xx in xs:
                            cells = {(xx + dx, yy + dy) for dx in range(t['w']) for dy in range(t['h'])}
                            if not cells & (room_occupied | reserved):
                                found = xx, yy, cells
                                break
                        if found:
                            break
                    if found:
                        xx, yy, cells = found
                        placements[t['id']] = (xx, yy)
                        room_occupied |= cells
                        allocated.append(t)
                        pending.remove(t)
                if not allocated:
                    continue
                top = max(yy for xx, yy in room_occupied)
                top = max(bottom + 2, top)
                door = min(top, bottom + rng.randint(0, 2))
                occupied |= room_occupied
                # Small irregular chambers remain separated by thick permanent rock.
                for yy in range(bottom, top + 1):
                    for xx in range(x0, x1 + 1):
                        corner = xx in (x0, x1) and yy in (bottom, top)
                        if corner and (xx, yy) not in room_occupied and xx != aisle:
                            continue
                        carve(xx, yy, sector)
                # A dogleg from a junction into the side gallery, then up the chamber's approach edge.
                branch_y = y + rng.randint(0, 1)
                takeoff = spine if side == 0 else spine + 1
                bend = aisle + (1 if side == 0 else -1)
                line(takeoff, branch_y, bend, branch_y, sector)
                line(bend, branch_y, bend, door, sector)
                line(bend, door, aisle, door, sector)
                room = dict(sector=sector, side=side, left=x0, right=x1,
                            aisle=aisle, bottom=bottom, top=top, door=door, targets=len(allocated))
                rooms.append(room)
                module_rooms.append(room)
                junctions.append((takeoff, branch_y))
                for target in allocated:
                    branch_ids[target['id']] = len(rooms)
                prev = previous_rooms.get(side)
                if number >= 25 and prev and rng.random() < (0.6 if family == 'braided-tunnels' else 0.35):
                    outer = 1 if side == 0 else width - 2
                    lower, upper = prev['top'] + 1, bottom - 1
                    line(prev['aisle'], prev['top'], prev['aisle'], lower, sector)
                    line(prev['aisle'], lower, outer, lower, sector)
                    line(outer, lower, outer, upper, sector)
                    line(outer, upper, aisle, upper, sector)
                    line(aisle, upper, aisle, bottom, sector)
                    loops.append((outer, lower, upper))
                previous_rooms[side] = room
                actual_top = max(actual_top, top + 2)
                allocated_this_module += len(allocated)
            if not allocated_this_module:
                raise RuntimeError(f"Cannot place footprints in {zone['Id']}: {pending}")
            if family == 'winding-galleries' and module % 2 == 1 or family == 'ore-archipelago' and module % 2 == 0:
                follow = module_rooms[rng.randrange(len(module_rooms))]
                follow_x = follow['aisle'] if follow['side'] == 0 else follow['aisle'] - 1
                trunk(spine, y, spine, y + 1, sector)
                trunk(spine, y + 1, follow_x, actual_top, sector)
                # A second gallery rejoins the branch above its chamber. The
                # long scenic route remains explorable; hauling can use the
                # shorter connection instead of compulsory left/right detours.
                trunk(spine, y + 1, spine, actual_top, sector)
                trunk(follow_x, actual_top, spine, actual_top, sector)
                if abs(follow_x - spine) >= 3:
                    loops.append((follow_x, y + 1, actual_top))
            else:
                trunk(spine, y, spine, actual_top, sector)
            y = actual_top + 1
            module += 1
        trunk(spine, y - 1, spine, y, sector)
        if sector < len(gates):
            gate_id = gates[sector]
            placements[gate_id] = (spine, y)
            barriers.append((gate_id, [(spine, y), (spine + 1, y)]))
            y += 1
        else:
            exit_xy = (spine, y)
            y += 1
        if intro_targets:
            candidates = sorted(main_floor - occupied, key=lambda c: (c[1], c[0]))
            for target, cell in zip(intro_targets, candidates):
                placements[target['id']] = cell
                occupied.add(cell)
    depth = max(yy for xx, yy in floor) + 1
    result = dict(Version=VERSION, ZoneId=zone['Id'], Layout=f"{family}-{number:03d}",
                  Width=width, Depth=depth, BranchCrowdingPenalty=8,
                  FloorRows=[''.join('.' if (x, depth-r-1) in floor else '#' for x in range(width)) for r in range(depth)],
                  SectorRows=[''.join(str(sectors[(x, depth-r-1)]) if (x, depth-r-1) in floor else '#' for x in range(width)) for r in range(depth)],
                  ExitColumn=exit_xy[0], ExitRow=depth-exit_xy[1]-1,
                  Placements=[dict(TargetId=t['id'], Column=placements[t['id']][0], Row=depth-placements[t['id']][1]-1, Branch=branch_ids.get(t['id'], 0)) for t in targets],
                  Gates=[dict(TargetId=i, Cells=sorted((depth-yy-1)*width+x for x, yy in cells)) for i, cells in barriers])
    stats = dict(zone=number, sourceDepth=zone['DepthCells'], depth=depth, width=width, layout=family,
                 targets=len(targets), chambers=len(rooms), loops=len(loops), junctions=len(set(junctions)),
                 floorCells=len(floor), rockCells=width*depth-len(floor),
                 horizontalSpan=max(x for x, yy in floor)-min(x for x, yy in floor)+1)
    chambers = [dict(branch=k + 1, sector=r['sector'], columns=[r['left'], r['right']],
                     rows=[depth - r['top'] - 1, depth - r['bottom'] - 1]) for k, r in enumerate(rooms)]
    return result, stats, chambers


# ───────────────────────────── Fix step for standing factories (TZ 2.2 §4.1) ─────────────────────────────

WEIGHT_NO_ROUTE = 10      # factory without a free neighbour connected to IntakeCell
WEIGHT_NO_WORK = 10       # target without a reachable work position in the final map
WEIGHT_BYPASS = 1000      # cell reachable behind a closed gate (must never happen)
WEIGHT_EXIT = 100
WEIGHT_WAVE = 5           # per target left by the wave clearance
MAX_FIX_STEPS = 60


def measure(m):
    """Violations of the final-map case, counted per item (fast: G + 2 BFS, no wave simulation)."""
    blocked = m.factory_cells
    cut, bypass = set(), 0
    for opened in range(m.gate_count + 1):
        seen = m.reach(blocked, opened)
        for cell in range(m.count):
            if not m.playable[cell] or cell in blocked:
                continue
            closed = m.gate_order[cell] >= opened
            ahead = m.sector[cell] > opened
            if seen[cell] and (closed or ahead):
                bypass += 1
            elif not seen[cell] and not closed and not ahead:
                cut.add(cell)
    seen = m.reach(blocked, m.gate_count)
    no_work = [t['id'] for t in m.targets if not any(seen[c] for c in t['work'])]
    no_route = []
    for t in m.targets:
        if t['kind'] != 's':
            continue
        own = set(t['cells'])
        if not any(seen[n] and n not in blocked for c in t['cells'] for n in m.neighbours(c) if n not in own):
            no_route.append(t['id'])
    score = (WEIGHT_NO_ROUTE * len(no_route) + WEIGHT_NO_WORK * len(no_work) + len(cut) + WEIGHT_BYPASS * bypass +
             (0 if seen[m.exit] else WEIGHT_EXIT))
    return dict(score=score, cut=cut, bypass=bypass, no_work=no_work, no_route=no_route, exit=seen[m.exit])


def wave_penalty(m):
    """Targets left by the wave clearance (case 1) - 0 when it passes."""
    problems, _ = tc.check_waves(m)
    if not problems:
        return 0
    text = problems[0]
    left = text.split(':')[-1].split() if 'unreachable targets' in text else ['?']
    return WEIGHT_WAVE * max(1, len(left))


def total_score(level, targets):
    m = tc.TopologyMap(level, targets)
    info = measure(m)
    return info['score'] + wave_penalty(m), m, info


def placement_of(level, target_id):
    for p in level['Placements']:
        if p['TargetId'] == target_id:
            return p
    raise KeyError(target_id)


def swap_candidates(level, m, info, exact):
    """Swap a factory with a 2x3 block of the same sector covered only by whole r/x footprints (exact exchange: the
    free floor stays where it was) or, when exact is False, partly by empty floor. The block's targets move into the
    factory's old cells keeping their relative layout."""
    kinds = {t['id']: t for t in m.targets}
    width = m.width
    factories = [t for t in m.targets if t['kind'] == 's']
    involved = set(info['no_route'])
    for t in factories:
        own = set(t['cells'])
        if any(n in info['cut'] for c in t['cells'] for n in m.neighbours(c) if n not in own):
            involved.add(t['id'])
    branches = {placement_of(level, i)['Branch'] for i in involved}
    for t in factories:
        if placement_of(level, t['id'])['Branch'] in branches and placement_of(level, t['id'])['Branch'] > 0:
            involved.add(t['id'])
    fw, fh = 2, 3
    for fid in sorted(involved):
        f = kinds[fid]
        fp = placement_of(level, fid)
        own = set(f['cells'])
        for row in range(fh - 1, m.depth):
            for col in range(0, width - fw + 1):
                if (col, row) == (fp['Column'], fp['Row']):
                    continue
                cells = [m.cell(col + dx, row - dy) for dy in range(fh) for dx in range(fw)]
                if any(c in own for c in cells):
                    continue
                ok, moved = True, set()
                for c in cells:
                    if not m.playable[c] or m.sector[c] != f['sector'] or m.gate_order[c] >= 0:
                        ok = False
                        break
                    occ = m.occupant[c]
                    if occ >= 0:
                        if kinds[occ]['kind'] == 's':
                            ok = False
                            break
                        moved.add(occ)
                    elif exact:
                        ok = False
                        break
                if not ok:
                    continue
                block = set(cells)
                if any(not set(kinds[o]['cells']) <= block for o in moved):
                    continue
                yield fid, (col, row), sorted(moved)


def apply_swap(level, fid, anchor, moved):
    level = copy.deepcopy(level)
    fp = placement_of(level, fid)
    old = (fp['Column'], fp['Row'])
    old_branch = fp['Branch']
    branches = [placement_of(level, o)['Branch'] for o in moved]
    if branches:
        fp['Branch'] = collections.Counter(branches).most_common(1)[0][0]
    fp['Column'], fp['Row'] = anchor
    for o in moved:
        p = placement_of(level, o)
        p['Column'] = p['Column'] - anchor[0] + old[0]
        p['Row'] = p['Row'] - anchor[1] + old[1]
        p['Branch'] = old_branch
    return level, old


def try_swap(level, targets, current, m, info, log, exact):
    ranked = []
    for fid, anchor, moved in swap_candidates(level, m, info, exact):
        candidate, old = apply_swap(level, fid, anchor, moved)
        cm = tc.TopologyMap(candidate, targets)
        ci = measure(cm)
        if ci['score'] >= current:
            continue
        distance = abs(anchor[0] - old[0]) + abs(anchor[1] - old[1])
        ranked.append((ci['score'], distance, fid, anchor[1] * m.width + anchor[0], candidate, cm, anchor, old, moved))
    ranked.sort(key=lambda r: r[:4])
    for score, distance, fid, _, candidate, cm, anchor, old, moved in ranked[:8]:
        full = score + wave_penalty(cm)
        if full < current:
            log['swaps'].append(dict(factory=fid, fromColumn=old[0], fromRow=old[1], toColumn=anchor[0], toRow=anchor[1],
                                     movedTargets=moved, exact=exact))
            return candidate
    return None


def set_cell(level, cell, floor, sector):
    width = level['Width']
    row, col = divmod(cell, width)
    f, s = level['FloorRows'][row], level['SectorRows'][row]
    level['FloorRows'][row] = f[:col] + ('.' if floor else '#') + f[col + 1:]
    level['SectorRows'][row] = s[:col] + (str(sector) if floor else '#') + s[col + 1:]


def components(m, cells):
    left, result = set(cells), []
    while left:
        start = min(left)
        comp, queue = {start}, collections.deque([start])
        left.discard(start)
        while queue:
            c = queue.popleft()
            for n in m.neighbours(c):
                if n in left:
                    left.discard(n)
                    comp.add(n)
                    queue.append(n)
        result.append(sorted(comp))
    return result


def try_fill(level, m, info, log):
    """Empty floor pockets cut off by a standing factory become rock (no target, gate or exit inside)."""
    filled = []
    for comp in components(m, info['cut']):
        if any(m.occupant[c] >= 0 or m.gate_order[c] >= 0 or c == m.exit for c in comp):
            continue
        filled.extend(comp)
    if not filled:
        return None
    level = copy.deepcopy(level)
    for c in filled:
        set_cell(level, c, False, None)
    log['filled'].extend(filled)
    return level


def carvable(m, cell, sector):
    row, col = divmod(cell, m.width)
    if m.playable[cell] or not (1 <= col <= m.width - 2 and 1 <= row <= m.depth - 2):
        return False
    for n in m.neighbours(cell):
        if m.playable[n] and (m.sector[n] != sector or m.gate_order[n] >= 0):
            return False
    return True


def carve_path(m, sources, sector):
    """0-1 BFS: rock cells cost 1 (carvable only), floor of the same sector costs 0; ends on a cell reachable from the
    base with gates up to this sector open. Returns the rock cells to carve or None."""
    blocked = m.factory_cells
    reach = m.reach(blocked, sector)
    inf = 1 << 30
    dist = [inf] * m.count
    parent = [-1] * m.count
    queue = collections.deque()

    def free(cell):
        return m.playable[cell] and cell not in blocked and m.sector[cell] == sector and m.gate_order[cell] < 0

    for c in sorted(sources):
        if c in blocked:
            continue
        cost = 0 if free(c) else (1 if carvable(m, c, sector) else None)
        if cost is None or dist[c] <= cost:
            continue
        dist[c] = cost
        (queue.appendleft if cost == 0 else queue.append)(c)
    best = None
    while queue:
        c = queue.popleft()
        if reach[c] and m.playable[c] and c not in blocked:
            best = c
            break
        for n in m.neighbours(c):
            if n in blocked:
                continue
            if free(n):
                cost = 0
            elif carvable(m, n, sector):
                cost = 1
            else:
                continue
            if dist[c] + cost < dist[n]:
                dist[n] = dist[c] + cost
                parent[n] = c
                (queue.appendleft if cost == 0 else queue.append)(n)
    if best is None:
        return None
    path, at = [], best
    while at >= 0:
        if not m.playable[at]:
            path.append(at)
        at = parent[at]
    return sorted(path)


def try_carve(level, m, info, log):
    """Cut a passage in the same sector: from a factory without access or from a cut-off area with targets."""
    kinds = {t['id']: t for t in m.targets}
    jobs = []
    for fid in info['no_route']:
        f = kinds[fid]
        own = set(f['cells'])
        jobs.append((f['sector'], sorted({n for c in f['cells'] for n in m.neighbours(c) if n not in own}), 'factory', fid))
    for comp in components(m, info['cut']):
        jobs.append((m.sector[comp[0]], comp, 'area', comp[0]))
    for tid in info['no_work']:
        t = kinds[tid]
        jobs.append((t['sector'], list(t['work']), 'target', tid))
    for sector, sources, what, key in jobs:
        path = carve_path(m, sources, sector)
        if not path:
            continue
        level = copy.deepcopy(level)
        for c in path:
            set_cell(level, c, True, sector)
        log['carved'].append(dict(reason=what, id=key, sector=sector, cells=path))
        return level
    return None


def problems_of(level, targets):
    m = tc.TopologyMap(level, targets)
    wave, _ = tc.check_waves(m)
    final, _ = tc.check_final(m)
    return m, wave + final


def rock_ok(level):
    """Same structural limits as MineTopologyTests: rock keeps > 1/5 of the mine and an interior wall survives."""
    rows = level['FloorRows']
    cells = level['Width'] * level['Depth']
    rock = sum(r.count('#') for r in rows)
    interior = any('#' in r.strip('#') for r in rows)
    return rock > cells // 5 and interior


def fix_zone(level, targets):
    """Returns (fixed level, log). Raises when the mine cannot be fixed."""
    log = dict(swaps=[], filled=[], carved=[])
    for _ in range(MAX_FIX_STEPS):
        m, problems = problems_of(level, targets)
        if not problems:
            if not rock_ok(level):
                raise RuntimeError(f"{level['ZoneId']}: fix step removed too much structural rock")
            return level, log
        current, m, info = total_score(level, targets)
        nxt = try_swap(level, targets, current, m, info, log, True)
        if nxt is None:
            nxt = try_swap(level, targets, current, m, info, log, False)
        if nxt is None:
            nxt = try_fill(level, m, info, log)
        if nxt is None:
            nxt = try_carve(level, m, info, log)
        if nxt is None:
            raise RuntimeError(f"{level['ZoneId']}: standing factories cannot be fixed: {problems}")
        level = nxt
    raise RuntimeError(f"{level['ZoneId']}: fix step did not converge")


def geometry_key(level):
    """Snapshot without Version - the comparison key for unchanged mines."""
    return json.dumps({k: v for k, v in level.items() if k != 'Version'}, ensure_ascii=False, separators=(',', ':'))


def chamber_of(chambers, branch):
    return next((c for c in chambers if c['branch'] == branch), None)


def build():
    data = json.loads(SOURCE.read_text())
    overrides = json.loads(OVERRIDES.read_text())
    previous = json.loads(OUT.read_text()) if OUT.exists() else None
    levels, stats, changed, factories, routes_total = {}, [], [], 0, 0
    for n, zone in enumerate(data['Zones'], 1):
        targets, _ = source_targets(data, zone, overrides['Zones'].get(zone['Id']))
        level, stat, chambers = compile_zone(data, zone, overrides['Zones'].get(zone['Id']), n)
        _, before = problems_of(level, targets)
        if before:
            base = copy.deepcopy(level)
            level, log = fix_zone(level, targets)
            moved = sorted({s['factory'] for s in log['swaps']} | {t for s in log['swaps'] for t in s['movedTargets']})
            touched = sorted({s['factory'] for s in log['swaps']} |
                             {c['id'] for c in log['carved'] if c['reason'] == 'factory'})
            changed.append(dict(
                zone=n, zoneId=zone['Id'], layout=level['Layout'], problemsBefore=before,
                factories=[t['id'] for t in targets if t['kind'] == 's'],
                fixedFactories=touched,
                chambers=[chamber_of(chambers, b) for b in sorted({p['Branch'] for p in base['Placements']
                                                                   if p['TargetId'] in moved or p['TargetId'] in touched} - {0})],
                swaps=log['swaps'], filledCells=log['filled'], carved=log['carved'],
                floorCellsBefore=sum(r.count('.') for r in base['FloorRows']),
                floorCellsAfter=sum(r.count('.') for r in level['FloorRows'])))
        m, problems = problems_of(level, targets)
        if problems:
            raise RuntimeError(f"{zone['Id']}: validator failed after the fix step: {problems}")
        routes = m.pipe_routes()
        factories += len(routes)
        routes_total += sum(1 for r in routes.values() if r is not None)
        floor = sum(r.count('.') for r in level['FloorRows'])
        stat.update(floorCells=floor, rockCells=level['Width'] * level['Depth'] - floor)
        levels[zone['Id']] = level
        stats.append(stat)
    if routes_total != factories:
        raise RuntimeError(f"pipe routes: {routes_total} of {factories} factories")
    changed_ids = {c['zoneId'] for c in changed}
    compared = 0
    if previous is not None:
        for zone_id, level in levels.items():
            if zone_id in changed_ids:
                continue
            if geometry_key(level) != geometry_key(previous['Zones'][zone_id]):
                raise RuntimeError(f"{zone_id}: unchanged mine differs from the previous mine_topology.json")
            compared += 1
    catalog = dict(Version=VERSION, SourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(), Zones=levels)
    topology_text = json.dumps(catalog, ensure_ascii=False, separators=(',', ':')) + '\n'
    report = dict(
        version=VERSION, baseVersion=BASE_VERSION, sourceSha256=catalog['SourceSha256'],
        rule="TZ 2.2 §4.1: an opened factory stays a wall; validator cases 1 (wave clearance) and 2 (final map)",
        mines=len(levels), changedMines=len(changed), unchangedMines=len(levels) - len(changed),
        unchangedComparedWithPrevious=compared, factories=factories, factoriesWithPipeRoute=routes_total,
        swaps=sum(len(c['swaps']) for c in changed), carvedCells=sum(len(x['cells']) for c in changed for x in c['carved']),
        filledCells=sum(len(c['filledCells']) for c in changed),
        zones=changed)
    return topology_text, json.dumps(stats, indent=2) + '\n', json.dumps(report, ensure_ascii=False, indent=2) + '\n', stats, changed


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--check', action='store_true', help='compare with the files on disk, write nothing')
    args = parser.parse_args(argv)
    topology, generation, factories, stats, changed = build()
    outputs = ((OUT, topology), (GENERATION_REPORT, generation), (FACTORIES_REPORT, factories))
    if args.check:
        stale = [str(path.relative_to(ROOT)) for path, text in outputs if not path.exists() or path.read_text() != text]
        if stale:
            print('Out of date: ' + ', '.join(stale))
            return 1
        print(f"Up to date: {VERSION}, {len(changed)} mines fixed for standing factories")
        return 0
    for path, text in outputs:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
    print(f"Generated {len(stats)} mines, {sum(s['chambers'] for s in stats)} chambers, {sum(s['loops'] for s in stats)} loops")
    print(f"Fixed for standing factories: {len(changed)} mines: {' '.join(str(c['zone']) for c in changed)}")
    print(f"Depth/source ratio {max(s['depth']/s['sourceDepth'] for s in stats):.2f} max; configuration {OUT.stat().st_size} bytes")
    return 0


if __name__ == '__main__':
    sys.exit(main())
