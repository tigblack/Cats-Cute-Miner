#!/usr/bin/env python3
"""Independent data/geometry/formula audit for the Outpost v2 specification.

Does not run the Unity game or simulate workers. Reads the source JSON and
prints a reproducible JSON report; optionally writes it under --out.
With --overrides (Assets/Game/Resources/Outpost/level_overrides.json) it also
audits every zone override independently: added r/s/x tokens go only on "."
anchors, their footprints may not overlap source objects, cover the exit e or
lie in a gate row p; the effective zone is audited with the same rules and
reported under "level_overrides". Per-zone "zones" entries stay source-only.

Balance profile: outpost-2.2-b6 = the numbers of outpost-2.1-b4 (HP step (z-1)/25,
local upgrade prices with the depth factor 1 + (z-1)/10, unload step 1/5, Value cap
40 with milestones 10/25/40) plus the mine pace multiplier K(z) of b6, which is applied
when a run is created and is not part of these base HP/cargo/price figures. Card effects are not modelled. Fields prefixed
"original_v2_0" are a historical statement about the original v2.0 rules (HP step
(z-1)/200, no depth factor, Value cap 60) and are evaluated entirely under those rules.

TZ 2.2 rules (factories instead of deposits; adapter-2):
- factory chains (former deposits "s") carry no coin cargo: cargo_units, base_gold,
  maximum_gold_upper_bound and the base resource budget count s as 0 units
  (TotalCargoUnits = max(2, ceil(HP/10)) only for r/x/p);
- an opened factory keeps its 2x3 footprint as a building: the static clearance never
  frees factory cells (case 1 of OutpostReachability), and the final source map
  (r/x removed, gates open, factories standing) must keep every floor cell connected
  to the base, at every gate stage, and give every factory a free connected neighbour
  (case 2). The wide campaign topology is validated by Tools/Topology/build_topology.py.
The previous reference (v2.1 rules, profile outpost-2.1-b4) is kept as
outpost_v2_audit_2.1.json next to this script.
"""
import argparse
from collections import Counter, deque
from decimal import Decimal, localcontext
from fractions import Fraction
import hashlib
import json
from math import isqrt
from pathlib import Path

EXPECTED_SHA = "8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5"
TYPE_HP = {"r": Fraction(1), "x": Fraction(4, 5), "s": Fraction(2), "p": Fraction(5, 2)}
TYPE_VALUE = {"r": Fraction(1), "x": Fraction(1, 2), "s": Fraction(2), "p": Fraction(1)}
FIELDS = {"r": 3, "s": 6, "x": 2, "p": 4, "e": 4, "c": 3}
PROFILE_VERSION = "outpost-2.2-b6"
# Profile b6 = b5 numbers plus the mine pace multiplier K(z) (PaceScale, owner decision V4 2026-10-15); the audit keeps the base HP of §12.4.
# Profile b5 = numbers of b4. Profiles b1-b3: HP step 1/200, no depth price factor, unload step 3/20, Value cap 50, milestones 10/25/50.
# TZ 2.2 §4.2 (adapter-2): factory chains release no cargo.
NO_CARGO_KINDS = frozenset("s")
ZONE_HP_STEP = Fraction(1, 25)
COST_ZONE_STEP = Fraction(1, 10)
UNLOAD_STEP = Fraction(1, 5)
VALUE_CAP = 40
VALUE_MILESTONES = (10, 25, 40)
# Original v2.0 rules, used only for the historical "Value L60 unreachable" fields.
V2_0_ZONE_HP_STEP = Fraction(1, 200)
V2_0_COST_ZONE_STEP = Fraction(0)
V2_0_VALUE_CAP = 60
V2_0_VALUE_MILESTONES = (10, 25, 50)
# Legendary hat at item level 20: ItemBonus = 0.60 * (1 + 0.05 * 19) = 1.17.
LEGENDARY_HAT_L20 = 1 + Fraction(3, 5) * Fraction(39, 20)


def ceil_fraction(value):
    return -(-value.numerator // value.denominator)


def decimal_fraction(value):
    return Decimal(value.numerator) / Decimal(value.denominator)


def cost_zone_factor(z, step=COST_ZONE_STEP):
    """Depth price factor of local upgrades: 1 + CostZoneStep * (z - 1)."""
    return 1 + step * (z - 1)


def local_cost(z, base, growth, level, step=COST_ZONE_STEP):
    """NextCost(z, L) = ceil(S(z) * CostZoneFactor(z) * BaseCost * Growth^L), one exact ceil."""
    return ceil_fraction(Fraction(6, 5) ** (z - 1) * cost_zone_factor(z, step) * base * growth ** level)


def value_milestone(level, milestones=VALUE_MILESTONES):
    return 2 ** sum(level >= t for t in milestones)


def value_level_budget_bound(z, cap, milestones=VALUE_MILESTONES, cost_step=COST_ZONE_STEP):
    """Optimistic necessary base-resource budget to buy Value L0..cap-1, even with max legendary hat.

    Relaxes discrete cargo into divisible value and permits each upgrade to be
    paid at the best value ratio then available. Includes actual per-coin ceil
    and the exact local price (with the depth factor of the given profile).
    Thus exceeding the map's entire resource budget proves unreachability
    without card effects. Passing the bound does NOT prove an attainable
    purchasing policy.
    """
    scale = Fraction(6, 5) ** (z - 1)
    required = Decimal(0)
    with localcontext() as ctx:
        ctx.prec = 70
        for level in range(cap):
            multiplier = Fraction(11, 10) ** level * value_milestone(level, milestones) * Fraction(217, 100)
            # Allow whichever cargo type gives the best rounding ratio, even
            # if the map has fewer of those units: another optimistic relaxation.
            ratio = max(Fraction(ceil_fraction(5 * scale * factor * multiplier), 1) / (5 * scale * factor) for factor in (Fraction(1, 2), Fraction(1), Fraction(2)))
            cost = local_cost(z, 30, Fraction(31, 25), level, cost_step)
            required += decimal_fraction(Fraction(cost, 1) / scale / ratio)
        return +required


def adapted_hp(z, rank, area, kind, step=ZONE_HP_STEP):
    coefficient = 20 * (1 + step * (z - 1)) * (1 + 5 * rank) * TYPE_HP[kind]
    if area >= 7:
        return ceil_fraction(coefficient * Fraction(5, 2))
    # ceil(a * sqrt(area) / b), using integer comparisons only. Avoids
    # floating point producing e.g. ceil(318.00000000000006) == 319.
    numerator_squared = coefficient.numerator ** 2 * area
    denominator_squared = coefficient.denominator ** 2
    root = isqrt(numerator_squared // denominator_squared)
    return root + int(root * root * denominator_squared < numerator_squared)


def reachable(width, depth, occupied, gate_row):
    start = (width // 2, depth + 1)
    seen = {start}
    todo = deque([start])
    while todo:
        col, row = todo.popleft()
        for cell in ((col, row - 1), (col - 1, row), (col + 1, row), (col, row + 1)):
            c, r = cell
            if not (0 <= c < width and 0 <= r < depth + 3):
                continue
            if gate_row is not None and r < gate_row:
                continue
            if cell in occupied or cell in seen:
                continue
            seen.add(cell)
            todo.append(cell)
    return seen


def work_cells(cells):
    owned = set(cells)
    return {(c + dc, r + dr) for c, r in cells for dc, dr in ((0, -1), (-1, 0), (1, 0), (0, 1))} - owned


def apply_override(zone, override, geometry):
    """Effective zone: override tokens on "." anchors; the audit below re-checks footprints and overlaps."""
    width, depth = zone["WidthCells"], zone["DepthCells"]
    tokens = zone["Grid"].split(",")
    added = set()
    for item in override.get("AddTokens") or []:
        col, row, token = item["Column"], item["Row"], item["Token"]
        if not (0 <= col < width and 0 <= row < depth):
            raise ValueError(f"{zone['Id']} override: anchor out of bounds {(col, row)}")
        index = row * width + col
        fields = token.split(":")
        if fields[0] not in "rsx" or len(fields[0]) != 1 or len(fields) != FIELDS[fields[0]]:
            raise ValueError(f"{zone['Id']} override: only r/s/x tokens allowed, got {token}")
        if tokens[index] != "." or index in added:
            raise ValueError(f"{zone['Id']} override: anchor {(col, row)} is not a free source anchor")
        tokens[index] = token
        added.add(index)
    gate_rows = {i // width for i, t in enumerate(tokens) if t.startswith("p:")}
    exit_index = next(i for i, t in enumerate(tokens) if t.startswith("e:"))
    for index in added:
        fields = tokens[index].split(":")
        definition = geometry[fields[0]][fields[1]]
        col, row = index % width, index // width
        for dx in range(definition["WidthCells"]):
            for dz in range(definition["DepthCells"]):
                cell_row = row - dz
                if cell_row * width + col + dx == exit_index:
                    raise ValueError(f"{zone['Id']} override: footprint covers the exit")
                if cell_row in gate_rows:
                    raise ValueError(f"{zone['Id']} override: footprint in gate row {cell_row}")
    effective = dict(zone)
    effective["Grid"] = ",".join(tokens)
    return effective, len(added), dict(Counter(tokens[i].split(":")[0] for i in added))


def audit_zone(source, zone, geometry):
    zone_id = zone["Id"]
    number = int(zone_id[4:])
    width, depth = zone["WidthCells"], zone["DepthCells"]
    tokens = zone["Grid"].split(",")
    if len(tokens) != width * depth:
        raise ValueError(f"{zone_id}: token count mismatch")
    objects, occupied, gates, exits = [], {}, [], []
    counts = Counter(t.split(":")[0] for t in tokens)
    for index, token in enumerate(tokens):
        if token == ".":
            continue
        fields = token.split(":")
        kind = fields[0]
        if kind not in FIELDS or len(fields) != FIELDS[kind]:
            raise ValueError(f"{zone_id}:{index}: invalid token {token}")
        col, row = index % width, index // width
        obj = {"index": index, "kind": kind, "id": fields[1], "row": row, "col": col, "cells": []}
        if kind in geometry:
            definition = geometry[kind][fields[1]]
            obj["cells"] = [(col + dx, row - dz) for dx in range(definition["WidthCells"]) for dz in range(definition["DepthCells"])]
            for cell in obj["cells"]:
                if not (0 <= cell[0] < width and 0 <= cell[1] < depth):
                    raise ValueError(f"{zone_id}:{index}: out of bounds {cell}")
                if cell in occupied:
                    raise ValueError(f"{zone_id}:{index}: overlaps {occupied[cell]} at {cell}")
                occupied[cell] = index
        if kind in "rsp":
            level = int(fields[2])
            table = source["MiningTargetHealths"][fields[1]]
            if not (1 <= level <= len(table)) or table[level - 1] <= 0:
                raise ValueError(f"{zone_id}:{index}: invalid HP reference")
            obj["source_hp"] = Decimal(table[level - 1])
        if kind == "p":
            gates.append(obj)
        if kind == "e":
            exits.append((col, row))
        objects.append(obj)
    if counts["c"] != 1 or counts["e"] != 1:
        raise ValueError(f"{zone_id}: expected one c and one e")
    gates.sort(key=lambda g: (-g["row"], g["index"]))
    if len({g["row"] for g in gates}) != len(gates):
        raise ValueError(f"{zone_id}: duplicate gate row")
    hp_values = sorted({obj["source_hp"] for obj in objects if "source_hp" in obj})
    hp_rank = {hp: Fraction(i, max(1, len(hp_values) - 1)) for i, hp in enumerate(hp_values)}
    totals = {"hp": 0, "cargo_units": 0, "base_gold": 0, "maximum_gold_upper_bound": 0}
    raw_budget = Fraction(0)
    raw_budget_v2_0 = Fraction(0)
    target_hps = []
    selected_target_controls = {}
    crossing_count = 0
    scale = Fraction(6, 5) ** (number - 1)
    max_value = Fraction(11, 10) ** VALUE_CAP * value_milestone(VALUE_CAP) * LEGENDARY_HAT_L20
    for obj in objects:
        if obj["kind"] not in "rxsp":
            continue
        kind = obj["kind"]
        rank = Fraction(1, 2) if kind == "x" else hp_rank[obj["source_hp"]]
        area = len(obj["cells"]) if kind != "p" else 1
        hp = adapted_hp(number, rank, area, kind)
        if (zone_id, obj["index"]) == ("zone13", 78):
            selected_target_controls[str(obj["index"])] = hp
        units = 0 if kind in NO_CARGO_KINDS else max(2, (hp + 9) // 10)
        base_value = ceil_fraction(5 * scale * TYPE_VALUE[kind])
        max_coin_value = ceil_fraction(5 * scale * TYPE_VALUE[kind] * max_value)
        totals["hp"] += hp
        totals["cargo_units"] += units
        totals["base_gold"] += units * base_value
        totals["maximum_gold_upper_bound"] += units * max_coin_value
        raw_budget += units * 5 * TYPE_VALUE[kind]
        units_v2_0 = max(2, (adapted_hp(number, rank, area, kind, V2_0_ZONE_HP_STEP) + 9) // 10)
        raw_budget_v2_0 += units_v2_0 * 5 * TYPE_VALUE[kind]
        target_hps.append(hp)
        obj["sector"] = sum(g["row"] > obj["row"] for g in gates)
        crossing_count += sum(min(r for _, r in obj["cells"]) < g["row"] <= obj["row"] for g in gates) if obj["cells"] else 0

    # Monotone topological clearance, NOT a time/worker simulation. TZ 2.2 §4.1: an opened factory stays a wall.
    factory_cells = {cell for obj in objects if obj["kind"] == "s" for cell in obj["cells"]}
    remaining = {obj["index"]: obj for obj in objects if obj["kind"] in "rxs"}
    cleared, waves = 0, 0
    for sector in range(len(gates) + 1):
        gate_row = gates[sector]["row"] if sector < len(gates) else None
        while True:
            pending = [obj for obj in remaining.values() if obj["sector"] == sector]
            if not pending:
                break
            free = reachable(width, depth, occupied, gate_row)
            available = [obj for obj in pending if work_cells(obj["cells"]) & free]
            if not available:
                raise ValueError(f"{zone_id}: unreachable targets in sector {sector}: {[o['index'] for o in pending]}")
            for obj in available:
                if obj["kind"] != "s":
                    for cell in obj["cells"]:
                        del occupied[cell]
                del remaining[obj["index"]]
                cleared += 1
            waves += 1
        if gate_row is not None:
            free = reachable(width, depth, occupied, gate_row)
            if not any((col, gate_row) in free for col in range(width)):
                raise ValueError(f"{zone_id}: no gate working position in row {gate_row}")
    if remaining or not all(exit_cell in reachable(width, depth, occupied, None) for exit_cell in exits):
        raise ValueError(f"{zone_id}: incomplete clearance or unreachable exit")
    # Final map with standing factories: every floor cell of the open rows is connected to the base at every gate
    # stage, every factory keeps a free connected neighbour cell (pipe outlet, TZ 2.2 §5).
    standing = dict.fromkeys(factory_cells, -1)
    for stage in range(len(gates) + 1):
        gate_row = gates[stage]["row"] if stage < len(gates) else None
        free = reachable(width, depth, standing, gate_row)
        for row in range(depth + 3):
            for col in range(width):
                if (gate_row is None or row >= gate_row) and (col, row) not in standing and (col, row) not in free:
                    raise ValueError(f"{zone_id}: cell {(col, row)} cut off by standing factories with {stage} gates open")
    free = reachable(width, depth, standing, None)
    for obj in objects:
        if obj["kind"] == "s" and not work_cells(obj["cells"]) - factory_cells & free:
            raise ValueError(f"{zone_id}:{obj['index']}: standing factory without a free connected neighbour")
    bound_v2_0 = value_level_budget_bound(number, V2_0_VALUE_CAP, V2_0_VALUE_MILESTONES, V2_0_COST_ZONE_STEP)
    bound_cap = value_level_budget_bound(number, VALUE_CAP)
    return {"zone": zone_id, "width": width, "depth": depth, "tokens": dict(counts), "obligatory_targets": cleared + len(gates), "static_clearance": True, "clearance_waves": waves, "crossing_footprints": crossing_count, "unique_source_hp": len(hp_values), "min_adapted_hp": min(target_hps), "max_adapted_hp": max(target_hps), "selected_target_controls": selected_target_controls, "base_resource_budget_without_rounding": str(decimal_fraction(raw_budget)),
            "original_v2_0_base_resource_budget_without_rounding": str(decimal_fraction(raw_budget_v2_0)), "original_value_L60_optimistic_required_budget": str(bound_v2_0), "original_value_L60_provably_unreachable": bound_v2_0 > decimal_fraction(raw_budget_v2_0),
            f"value_cap{VALUE_CAP}_optimistic_required_budget": str(bound_cap), f"value_cap{VALUE_CAP}_passes_necessary_budget_bound": bound_cap <= decimal_fraction(raw_budget), **totals}


def build_report(path, overrides_path=None):
    raw = path.read_bytes()
    sha = hashlib.sha256(raw).hexdigest()
    if sha != EXPECTED_SHA:
        raise ValueError(f"Unexpected SHA-256: {sha}")
    source = json.loads(raw, parse_float=Decimal)
    geometry = {kind: {obj["Id"]: obj for obj in source[table]} for kind, table in (("r", "Rocks"), ("s", "MineShafts"), ("x", "Obstructions"))}
    zones = sorted(source["Zones"], key=lambda z: int(z["Id"][4:]))
    if [z["Id"] for z in zones] != [f"zone{n}" for n in range(1, 145)]:
        raise ValueError("Expected zone1..zone144 exactly")
    details = [audit_zone(source, zone, geometry) for zone in zones]
    all_counts = sum((Counter(row["tokens"]) for row in details), Counter())
    overrides = {}
    overrides_version = None
    if overrides_path is not None:
        document = json.loads(overrides_path.read_text(encoding="utf-8"))
        overrides_version = document["Version"]
        by_id = {zone["Id"]: zone for zone in zones}
        for zone_id, override in sorted((document.get("Zones") or {}).items(), key=lambda kv: int(kv[0][4:])):
            if not override.get("Name") or not override.get("Reason"):
                raise ValueError(f"{zone_id} override: name and reason are required")
            if not override.get("AddTokens"):
                continue
            effective, count, kinds = apply_override(by_id[zone_id], override, geometry)
            overrides[zone_id] = {"name": override["Name"], "added_tokens": count, "added_by_kind": kinds,
                                  "effective": audit_zone(source, effective, geometry)}
    effective_details = [overrides[row["zone"]]["effective"] if row["zone"] in overrides else row for row in details]
    max_coin = ceil_fraction(5 * Fraction(6, 5) ** 143 * 2 * Fraction(11, 10) ** VALUE_CAP * value_milestone(VALUE_CAP) * LEGENDARY_HAT_L20)
    value_costs_144 = [local_cost(144, 30, Fraction(31, 25), level) for level in range(VALUE_CAP)]
    max_gold_zone = max(details, key=lambda row: row["maximum_gold_upper_bound"])
    return {
        "scope": "Source JSON, static geometry under v2.1 rules with TZ 2.2 standing factories, balance profile " + PROFILE_VERSION + " (adapter-2: factory chains carry no cargo), exact reference formulas, no card effects. No Unity/worker/offline run.",
        "profile_version": PROFILE_VERSION,
        "source_sha256": sha, "source_bytes": len(raw), "zone_count": len(zones),
        "widths": sorted({z["WidthCells"] for z in zones}), "depth_range": [min(z["DepthCells"] for z in zones), max(z["DepthCells"] for z in zones)],
        "token_counts": dict(all_counts), "definitions": {table: len(source[table]) for table in ("Rocks", "MineShafts", "Obstructions", "CrusherGrids", "Cards", "Gacha")},
        "all_static_clearance_passed": True, "level_overrides_required_by_static_check": [],
        "level_overrides_version": overrides_version,
        "level_overrides": overrides,
        "static_clearance_totals": {
            "source": {"cleared_rsx": sum(row["obligatory_targets"] - row["tokens"].get("p", 0) for row in details),
                       "gates": sum(row["tokens"].get("p", 0) for row in details)},
            "effective": {"cleared_rsx": sum(row["obligatory_targets"] - row["tokens"].get("p", 0) for row in effective_details),
                          "gates": sum(row["tokens"].get("p", 0) for row in effective_details)},
        },
        "all_crossing_footprints": sum(row["crossing_footprints"] for row in details),
        "original_v2_0_value_L60_unreachable_zone_count": sum(row["original_value_L60_provably_unreachable"] for row in details),
        f"value_cap{VALUE_CAP}_passes_necessary_bound_zone_count": sum(row[f"value_cap{VALUE_CAP}_passes_necessary_budget_bound"] for row in details),
        "adapted_hp_range": [min(row["min_adapted_hp"] for row in details), max(row["max_adapted_hp"] for row in details)],
        "controls": {
            "pickaxe_costs_zone1_L0_L1_L9_L10": [local_cost(1, 20, Fraction(59, 50), L) for L in (0, 1, 9, 10)],
            "cost_zone_factor_z1_z2_z13_z144": [str(cost_zone_factor(z)) for z in (1, 2, 13, 144)],
            "pickaxe_costs_zone144_L0_L19": [str(local_cost(144, 20, Fraction(59, 50), L)) for L in (0, 19)],
            f"value_cost_zone144_L{VALUE_CAP - 1}": str(value_costs_144[-1]),
            f"value_cost_zone144_L{VALUE_CAP - 1}_exceeds_2_pow_53": value_costs_144[-1] > 2 ** 53,
            f"value_branch_total_zone144_L0_L{VALUE_CAP - 1}": str(sum(value_costs_144)),
            "unload_rate_L1_L20": [str(2 * (1 + UNLOAD_STEP * L)) for L in (1, 20)],
            "zone1_rank0_r_hp_units_gold": [20, 2, 10],
            "zone1_rank05_x_area2_hp": adapted_hp(1, Fraction(1, 2), 2, "x"),
            "zone13_grid78_hp": next(row for row in details if row["zone"] == "zone13")["selected_target_controls"]["78"],
            "pickaxe_milestone_L9_L10_L19_L20": [2 ** sum(L >= t for t in (10, 20)) for L in (9, 10, 19, 20)],
            f"value_milestone_L9_L10_L24_L25_L{VALUE_CAP - 1}_L{VALUE_CAP}": [value_milestone(L) for L in (9, 10, 24, 25, VALUE_CAP - 1, VALUE_CAP)],
            "original_pickaxe_L24_damage_approx": float(5 * Fraction(61, 50) ** 24 * 2),
            "corrected_pickaxe_L20_damage_approx": float(5 * Fraction(61, 50) ** 20 * 4),
            "legendary_L20_bonus_percent": "117", "common_L20_backpack_extra": 20,
            "maximum_coin_value_zone144_at_value_cap_and_legendary_hat_L20": str(max_coin),
            "maximum_coin_exceeds_2_pow_53": max_coin > 2 ** 53,
            "maximum_stage_gold_upper_bound": str(max_gold_zone["maximum_gold_upper_bound"]),
            "maximum_stage_gold_upper_bound_zone": max_gold_zone["zone"],
            "maximum_stage_gold_upper_bound_exceeds_2_pow_53": max_gold_zone["maximum_gold_upper_bound"] > 2 ** 53,
            "item_upgrade_parts_total_1_to_20": [sum(ceil_fraction(6 * Fraction(117, 100) ** (q - 1) * multiplier) for q in range(1, 20)) for multiplier in (Fraction(1), Fraction(3, 2), Fraction(5, 2), Fraction(4))],
            "completion_parts_z1_z4_z5_z10_z144": [10 + 5 * (z // 5) for z in (1, 4, 5, 10, 144)],
            "guaranteed_completion_parts_total_campaign": sum(10 + 5 * (z // 5) for z in range(1, 145)),
        },
        "zones": details,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--overrides", type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    report = build_report(args.source, args.overrides)
    output = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(output, encoding="utf-8")
        report.pop("zones")
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
