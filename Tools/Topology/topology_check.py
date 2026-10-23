#!/usr/bin/env python3
"""Python copy of the C# topology validator (Core/Outpost/Level/OutpostReachability.cs).

TZ 2.2 §4.1: an opened factory (former deposit "s") keeps its 2x3 footprint as a building. The footprint is
permanently blocked: before opening by the living target, after opening by the factory. Two cases are checked:

1. Wave clearance, as the original OutpostReachability: BFS from the camp cell (Width/2, Depth+1), closed gates
   block their passage cells, every target of the current sector with a reachable work cell is removed in one
   wave; removed r/x free their cells, removed s stay a wall at the same moment.
2. Final map: r/x removed, all gates open, factories standing. For every gate stage k = 0..G no cell of a later
   sector or of a closed gate is reachable (gates are not bypassed) and every floor cell of the open sectors
   except factory footprints is connected to the base. With all gates open every target keeps a reachable work
   position, the exit is reachable, every factory has a free neighbour cell connected to IntakeCell and a pipe
   route (TZ 2.2 §5).

The map model mirrors MineTopologyAdapter.Apply: camp rows Depth..Depth+2 are playable for |col - Width/2| <= 4,
IntakeCell = (Width/2, Depth+1), gate cells come from the snapshot, footprints extend from the placement anchor
(bottom-left) upward, work cells are footprint neighbours that are playable and not gate cells (a gate is
worked from its own sector).
"""
from collections import deque

CAMP_HALF_WIDTH = 4
BASE_ROWS = 3


class TopologyMap:
    """Navigation grid of one snapshot. Cells are row * width + col, camp rows below the mine."""

    def __init__(self, snapshot, targets):
        self.zone = snapshot['ZoneId']
        self.width = w = snapshot['Width']
        self.depth = d = snapshot['Depth']
        self.rows = d + BASE_ROWS
        self.count = w * self.rows
        base = w // 2
        self.playable = [False] * self.count
        self.sector = [-1] * self.count
        self.gate_order = [-1] * self.count
        for cell in range(self.count):
            row, col = divmod(cell, w)
            if row >= d:
                self.playable[cell] = abs(col - base) <= CAMP_HALF_WIDTH
                self.sector[cell] = 0 if self.playable[cell] else -1
            elif snapshot['FloorRows'][row][col] == '.':
                self.playable[cell] = True
                self.sector[cell] = int(snapshot['SectorRows'][row][col])
        self.intake = self.cell(base, d + 1)
        self.exit = self.cell(snapshot['ExitColumn'], snapshot['ExitRow'])
        placements = {p['TargetId']: p for p in snapshot['Placements']}
        gate_cells = {}
        for order, gate in enumerate(snapshot['Gates']):
            gate_cells[gate['TargetId']] = (order, list(gate['Cells']))
            for cell in gate['Cells']:
                self.gate_order[cell] = order
        self.gate_count = len(snapshot['Gates'])
        self.targets = []
        self.occupant = [-1] * self.count
        for t in sorted(targets, key=lambda x: x['id']):
            if t['kind'] == 'p':
                order, cells = gate_cells[t['id']]
                target = dict(id=t['id'], kind='p', sector=order, cells=cells)
            else:
                p = placements[t['id']]
                cells = sorted(self.cell(p['Column'] + dx, p['Row'] - dy) for dy in range(t['h']) for dx in range(t['w']))
                target = dict(id=t['id'], kind=t['kind'], sector=t['sector'], cells=cells)
                for cell in cells:
                    if self.occupant[cell] >= 0:
                        raise ValueError(f"{self.zone}: footprints {t['id']} and {self.occupant[cell]} overlap at {cell}")
                    self.occupant[cell] = t['id']
            target['work'] = self.work_cells(target)
            self.targets.append(target)
        self.factory_cells = {c for t in self.targets if t['kind'] == 's' for c in t['cells']}

    def cell(self, col, row):
        return row * self.width + col

    def neighbours(self, cell):
        """Up, left, right, down - the order of GridNavigation and FactoryRoutes."""
        row, col = divmod(cell, self.width)
        if row > 0:
            yield cell - self.width
        if col > 0:
            yield cell - 1
        if col + 1 < self.width:
            yield cell + 1
        if row + 1 < self.rows:
            yield cell + self.width

    def work_cells(self, target):
        own = set(target['cells'])
        result = set()
        for cell in target['cells']:
            for n in self.neighbours(cell):
                if n in own or not self.playable[n] or self.gate_order[n] >= 0:
                    continue
                if target['kind'] == 'p' and self.sector[n] != target['sector']:
                    continue
                result.add(n)
        return sorted(result)

    def reach(self, blocked, gates_opened):
        """BFS from the camp cell; blocked is a set of occupied cells."""
        seen = [False] * self.count
        start = self.intake
        seen[start] = True
        queue = deque([start])
        while queue:
            cell = queue.popleft()
            for n in self.neighbours(cell):
                if seen[n] or not self.playable[n] or n in blocked:
                    continue
                if self.gate_order[n] >= gates_opened:
                    continue
                seen[n] = True
                queue.append(n)
        return seen

    def pipe_routes(self):
        """FactoryRoutes.Build: one BFS from IntakeCell, walls = rock + all factory footprints, gates open,
        living r/x ignored. Outlet = free footprint neighbour with the smallest distance, then smallest index.
        Route cells: each step goes to a neighbour one closer to IntakeCell, straight on when possible,
        otherwise the first of up, left, right, down."""
        dist = [-1] * self.count
        walls = self.factory_cells
        if self.playable[self.intake] and self.intake not in walls:
            dist[self.intake] = 0
            queue = deque([self.intake])
            while queue:
                cell = queue.popleft()
                for n in self.neighbours(cell):
                    if dist[n] >= 0 or not self.playable[n] or n in walls:
                        continue
                    dist[n] = dist[cell] + 1
                    queue.append(n)
        routes = {}
        for t in self.targets:
            if t['kind'] != 's':
                continue
            own = set(t['cells'])
            best = -1
            for cell in t['cells']:
                for n in self.neighbours(cell):
                    if n in own or dist[n] < 0:
                        continue
                    if best < 0 or dist[n] < dist[best] or dist[n] == dist[best] and n < best:
                        best = n
            if best < 0:
                routes[t['id']] = None
                continue
            routes[t['id']] = dict(outlet=best, steps=dist[best], cells=self.route_cells(dist, best))
        return routes

    def route_cells(self, dist, outlet):
        path, at, step = [outlet], outlet, None
        while dist[at] > 0:
            want = dist[at] - 1
            ahead = at + step if step is not None and self.adjacent(at, at + step) else None
            if ahead is not None and dist[ahead] == want:
                nxt = ahead
            else:
                nxt = next(n for n in self.neighbours(at) if dist[n] == want)
            step = nxt - at
            path.append(nxt)
            at = nxt
        return path

    def adjacent(self, a, b):
        if not 0 <= b < self.count:
            return False
        ra, ca = divmod(a, self.width)
        rb, cb = divmod(b, self.width)
        return abs(ra - rb) + abs(ca - cb) == 1


def check_waves(m, standing_factories=True):
    """Case 1. Returns (problems, stats)."""
    blocked = {c for c, o in enumerate(m.occupant) if o >= 0}
    remaining = [t for t in m.targets if t['kind'] != 'p']
    gates = sorted((t for t in m.targets if t['kind'] == 'p'), key=lambda t: t['sector'])
    waves = cleared = opened = 0
    for sector in range(m.gate_count + 1):
        while True:
            pending = [t for t in remaining if t['sector'] == sector]
            if not pending:
                break
            seen = m.reach(blocked, sector)
            available = [t for t in pending if any(seen[c] for c in t['work'])]
            if not available:
                return [f"{m.zone}: unreachable targets in sector {sector}: {' '.join(str(t['id']) for t in pending)}"], None
            for t in available:
                if not (standing_factories and t['kind'] == 's'):
                    blocked.difference_update(t['cells'])
                remaining.remove(t)
                cleared += 1
            waves += 1
        if sector < m.gate_count:
            seen = m.reach(blocked, sector)
            if not any(seen[c] for c in gates[sector]['work']):
                return [f"{m.zone}: no gate working position for gate {gates[sector]['id']}"], None
            opened += 1
    seen = m.reach(blocked, m.gate_count)
    if remaining or not seen[m.exit]:
        return [f"{m.zone}: incomplete clearance or unreachable exit"], None
    return [], dict(waves=waves, cleared=cleared, gates=opened)


def check_final(m):
    """Case 2. Returns a list of problems (empty - passed) and per-factory details."""
    problems = []
    blocked = m.factory_cells
    for opened in range(m.gate_count + 1):
        seen = m.reach(blocked, opened)
        for cell in range(m.count):
            if not m.playable[cell] or cell in blocked:
                continue
            closed = m.gate_order[cell] >= opened
            ahead = m.sector[cell] > opened
            if seen[cell] and (closed or ahead):
                problems.append(f"{m.zone}: gate bypass at cell {cell} with {opened} gates open")
                break
            if not seen[cell] and not closed and not ahead:
                problems.append(f"{m.zone}: floor cell {cell} cut off from the base with {opened} gates open")
                break
    seen = m.reach(blocked, m.gate_count)
    routes = m.pipe_routes()
    for t in m.targets:
        if not any(seen[c] for c in t['work']):
            what = 'factory' if t['kind'] == 's' else 'target'
            problems.append(f"{m.zone}: {what} {t['id']} has no reachable work position")
        if t['kind'] == 's' and routes[t['id']] is None:
            problems.append(f"{m.zone}: factory {t['id']} has no pipe route to IntakeCell")
    if not seen[m.exit]:
        problems.append(f"{m.zone}: exit unreachable")
    return problems, routes


def validate(snapshot, targets):
    m = TopologyMap(snapshot, targets)
    wave_problems, stats = check_waves(m)
    final_problems, routes = check_final(m)
    return m, wave_problems, final_problems, stats, routes
