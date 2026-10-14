#!/usr/bin/env python3
"""Compile immutable source content into branching, reproducible mine masks.

No economy data is generated here. Target IDs, types, footprints and sectors come
from the original JSON and the existing content override. Only positions change.
Coordinates are built from the entrance upward, then converted to game rows.
"""
import collections
import hashlib
import json
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/Game/Data/Source/json-data.json'
OVERRIDES = ROOT / 'Assets/Game/Resources/Outpost/level_overrides.json'
OUT = ROOT / 'Assets/Game/Resources/Outpost/mine_topology.json'
VERSION = 'wide-branching-mines-2'


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
    return result, stats


def main():
    data = json.loads(SOURCE.read_text())
    overrides = json.loads(OVERRIDES.read_text())
    levels, stats = {}, []
    for n, zone in enumerate(data['Zones'], 1):
        level, stat = compile_zone(data, zone, overrides['Zones'].get(zone['Id']), n)
        levels[zone['Id']] = level
        stats.append(stat)
    catalog = dict(Version=VERSION, SourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(), Zones=levels)
    OUT.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',', ':'))+'\n')
    report = ROOT / 'Docs/Reports/topology/generation.json'
    report.write_text(json.dumps(stats, indent=2)+'\n')
    print(f"Generated {len(levels)} mines, {sum(s['chambers'] for s in stats)} chambers, {sum(s['loops'] for s in stats)} loops")
    print(f"Depth/source ratio {max(s['depth']/s['sourceDepth'] for s in stats):.2f} max; configuration {OUT.stat().st_size} bytes")


if __name__ == '__main__':
    main()
