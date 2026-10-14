# Reuse map: old core (grid, balance data, GameSession)

> **Устарело для карточек (9 октября 2026).** Решение отключить карточки, manager-карты, сундуки и эликсир отменено: система 61 карточки возвращена в режим CatOutpostV2 (ТЗ §10.4, `Docs/Rework/CARD_EFFECT_MAPPING.md`). Пометки DROP/удалить ниже для карточек, `Gacha`, `RewardCycle` (карточки и эликсир), `cards.py`, `HudBlueprints` и `UpgradeCard` больше не действуют; это карта прежнего кода, а не инструкция.

Scope: `Assets/Game/Scripts/Core/` → `Grid/ZoneLayout.cs` (396 lines), `Data/BalanceDto.cs` (330), `Data/BalanceRepository.cs` (194),
`Data/BalanceValidator.cs` (498), `Sim/GameSession.cs` (715), `Sim/GameSession.Commands.cs` (953), `Sim/GameSession.Rewards.cs` (296),
`Sim/SimEvent.cs` (122), `Sim/EconomyTables.cs` (270), `Sim/GachaService.cs` (219), `Sim/ScenarioBot.cs` (1100). I also skimmed
`Sim/GameSession.Time.cs` (443, the event loop) and `Sim/GameState.cs` (the state types). The oracle is `Docs/TZ/analysis/outpost_v2_audit.json`.
Target: the new auto-incremental mode (`Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md`, below "TZ2").
Line refs are `file:line`, relative to `Assets/Game/Scripts/Core/`.

Verdict legend: **REUSE** (as is) · **ADAPT** (keep the shape, change what's listed) · **DROP** (not needed in the new mode; TZ2 §4 lets it stay only in an isolated dev mode).

---

## 0. TL;DR

- **BalanceRepository can be reused as is.** It is hash-locked. It strips the BOM, uses invariant culture and Newtonsoft with `FloatParseHandling.Double`.
  It indexes `Rocks`, `MineShafts` and `Obstructions` by Id, orders the zones `zone1..zone144` by their numeric suffix, and has `TargetHp(id, level)`.
  Its only cost: `BuildIndexes` also requires the old-economy sections (Cards, Gacha, Deliveries, SpawningCart…). They are present
  in the immutable JSON, so this is harmless.
- **`MiningTargetHealths` is a `Dictionary<string,double[]>` (`Data/BalanceDto.cs:30`).** It has 48 tables, and the maximum value is 1.87e25.
  2060 of the 4752 values are above 2^53, so a double does not hold them as exact integers. I checked the per-zone sets of distinct HP values
  for r/s/p: they are **identical** under exact `Decimal` parsing (what the audit uses) and under double parsing, with 0 collapses in 144 zones,
  so the order is kept too. So `rank01` can be computed from the raw doubles, with exact `==` and no epsilon.
- **ZoneLayout has a correct generic core for r/s/x.** That core is token split, field counts, the footprint `(col+dx, row−dz)` with an anchor at the
  bottom-left, two-pass overlap detection, `Neighbors4` order and `LayoutIssue` reporting. Around it sits all the artificial occupancy that TZ2 §8.3 forbids:
  the exit 3×1, the gate 5×1, the forge 2×3 with an **anchor row shift of +1**, the cannon 1×2 that is written into `CellOwner`, and the merge-row logic.
  The old sector rule (`SectorOfRow(gateRow) = −1`) is also forbidden by TZ2 §8.4.
  **The new `SourceLevelImporter` should copy `Parse` pass 1/2 and `ParseToken` r/s/x, and give p/e/c no cells** (§3 below).
- **BalanceValidator is almost entirely old-rule.** Gate-row coverage, merge rows, sector connectivity with −1 gate rows, card requirements and the
  old `ReachabilityCheck` must all go; TZ2 §8.5 says this explicitly. Keep the report types and the MiningTargetHealths sanity loop.
- **GameSession is the old merge game.** The event-step skeleton of `Advance` is the useful part: process the due events, step to the next deadline,
  and guard against runaway loops (`Advance(a)+Advance(b) ≡ Advance(a+b)`). So are `TicksPerSecond = 1_000_000`, opId/Emit/Ledger and the Settings commands.
  Everything else couples to merge, cards, generators, deliveries or dynamite. It applies hits per cat in list order, not summed per target in anchor order,
  and it uses `GameNumber` (a double) for money.
- **SimEventBus is tiny.** It only routes `MineCompleted` to `IZoneCompletionHandler`, with opId de-duplication over the last 512 ops.
  Keep the pattern and replace the event enum.
- **GachaService, EconomyTables and ScenarioBot: DROP.** Reuse ideas only: weighted pick on `Pcg32`, the bot's session/offline loop, the report and the action log.
- **Audit oracle:** `controls` has 17 exact numbers for TZ2 §12.5, and each zone has 21 fields. The fields `hp`, `cargo_units`, `base_gold`, `unique_source_hp`, `obligatory_targets`
  and `clearance_waves` are perfect per-zone assertions for the new importer and adapter (§9).

---

## 1. `Grid/ZoneLayout.cs`: immutable logical grid of one zone

Purpose: parses `ZoneDto.Grid` into `ZoneObject`s, footprints, `CellOwner`/`CellSector`, gates/shafts lists and old special areas (forge, cannon, pads, merge rows).

### 1.1 Public API (exact)

| Line | Signature | Verdict |
|---|---|---|
| 8–16 | `public enum ObjectKind { Rock, Shaft, Obstruction, Gate, Exit, Cart }` | ADAPT: rename `Cart`→`StartMarker`, plus `x` becomes a target |
| 19–36 | `public sealed class ZoneObject { int Ordinal; int Index; string Token; ObjectKind Kind; string TypeId; int Level; string RewardId; string CardId; int CardLevel; int Col, Row; int Width, Depth; int[] Cells; int GateOrder = -1; bool IsTarget => Rock\|\|Shaft\|\|Gate\|\|Exit; }` | ADAPT: `IsTarget` must include Obstruction and exclude Exit; RewardId/CardId/CardLevel → "archived" fields |
| 38–44 | `public sealed class LayoutIssue { string ZoneId; int TokenIndex; string Message; ToString() }` | REUSE |
| 53 | `public const int ExitWidth = 3, ExitDepth = 1;` | DROP (TZ2 §8.3) |
| 54 | `public const int GateWidth = 5, GateDepth = 1;` | DROP (p has no footprint, TZ2 §8.4) |
| 59 | `public const int CartWidth = 2, CartDepth = 3;` | DROP |
| 61 | `public const int CannonWidth = 1, CannonDepth = 2;` | DROP |
| 63 | `public const int MaxWidth = 7;` | REUSE (validation only) |
| 65–69 | `ZoneId`, `ZoneNumber`, `Width`, `Depth`, `CellCount => Width * Depth` | REUSE. New nav grid needs `Depth + 3` rows for the base pad |
| 71–72 | `public readonly List<ZoneObject> Objects; public readonly List<LayoutIssue> Issues;` | REUSE |
| 75 | `public int[] CellOwner { get; private set; }` (Ordinal or −1) | ADAPT: only r/s/x write it |
| 77 | `public int[] CellSector { get; private set; }` (0 = start, **−1 = gate row**) | ADAPT: new rule, never −1 |
| 79–80 | `public ZoneObject Exit { get; } public ZoneObject Cart { get; }` | ADAPT: passive markers |
| 85 | `public int RowC { get; private set; } = -1;` | DROP (start pad is now `Depth..Depth+2`) |
| 93–95 | `CannonCol`, `CannonRow`, `HasCannon` | DROP |
| 97–99 | `Gates`, `Shafts`, `SectorCount => Gates.Count + 1` | REUSE (with the new gate sort) |
| 101 | `public bool IsValid => Issues.Count == 0;` | REUSE |
| 103 | `public static ZoneLayout Build(BalanceRepository balance, int zoneNumber)` | ADAPT → `SourceLevelImporter.Import(...)` |
| 105 | `public static ZoneLayout Build(BalanceRepository balance, ZoneDto zone)` | ADAPT |
| 118–121 | `int CellIndex(int col,int row) => row*Width+col; int Col(int cell); int Row(int cell); bool InBounds(int col,int row)` | REUSE |
| 123–132 | `public void Neighbors4(int cell, List<int> result)`: order **up, left, right, down** | REUSE. The audit BFS uses the same order (`outpost_v2_audit.py:70`) |
| 135–141 | `public bool RowFullyFree(int row)` | DROP (merge-only) |
| 144–149 | `public void StartPadRows(out int topRow, out int bottomRow)`: `row_c−3 … row_c+1` | DROP |
| 152–157 | `public void GatePadRows(int gateOrder, out int topRow, out int bottomRow)`: `row_p−3 … row_p−1` | DROP |
| 163–176 | `public bool TryMergeRows(int topRow, int bottomRow, out int upperRow, out int lowerRow)` | DROP |
| 178–187 | `public int SectorOfRow(int row)`: returns −1 on a gate row, else the count of gates with `g.Row > row` | DROP. TZ2 §8.4 forbids it (560 walls `wall1x1vL/R` sit on gate rows) |
| 298–299 | `public bool IsCannonCell(int cell)` | DROP |

### 1.2 Exactly how it parses (`Parse`, 194–281)

1. Size guard: `Width<=0||Depth<=0` → issue (196–200). `tokens = (zone.Grid ?? "").Split(',')`. The count must equal `Width*Depth` (201–206).
2. **Pass 1, anchors (209–217):** skip `"."`. `ParseToken` sets `Col = index % Width` and `Row = index / Width` (row 0 = top, 304),
   then appends with `Ordinal = Objects.Count`. A failed token returns `null`, and the issue has already been logged.
3. **Pass 2, footprints (220–246):** `CellOwner[] = -1`. For each object, `for dx in 0..W-1, for dz in 0..D-1`: `c = Col+dx`, `r = Row−dz`
   (the **anchor is the bottom-left cell**, and the footprint extends up). Out of bounds → issue "footprint выходит за карту". Overlap → issue "пересечение с …".
   Else `CellOwner[cell] = Ordinal`. `Cells[]` order is dx-major, so `Cells[0]` = the anchor.
   TZ2 example: zone2 `amethyst` anchored at (0,5) with size 2×3 → columns 0–1, rows 3–5.
4. **Specials (249–273):** Exit/Cart singletons (a second one → issue), gate list, shaft list. A missing `e` or `c` → issue (269–270). Then `PlaceCannon()` (271).
   `Gates.Sort((a,b) => b.Row.CompareTo(a.Row))` (272) sorts **by row only, and `List.Sort` is unstable**. TZ2 §8.4 wants gateRow desc, then anchor index.
   `GateOrder = k` (273).
5. `CellSector[c,r] = SectorOfRow(r)` (275–280).

`ParseToken` (301–372). Field counts are checked with `CheckFields` (374–379), and ints with `ParseInt` (381–386, `NumberStyles.Integer, InvariantCulture`; a bad int becomes 0 plus an issue):

| Token | Fields | Footprint source | Extra checks | New-mode treatment |
|---|---|---|---|---|
| `r:typeId:level` (307–315) | 3 | `Rocks[typeId].WidthCells/DepthCells` (all 1×1) | `CheckHp` | keep |
| `s:typeId:level:rewardId:cardId:cardLevel` (316–329) | 6 | `MineShafts[typeId]` (all 2×3) | Gacha(rewardId), Cards(cardId) exist; `CheckHp` | keep geometry and HP. Reward/card are archived only; do not error on them |
| `x:typeId` (330–336) | 2 | `Obstructions[typeId]` (20 types, 1×1 … 3×3, `waterfall1x4v*` 1×4) | none (no HP) | keep. It now becomes a **target**; HP comes from the adapter rule, not from JSON |
| `p:typeId:level:rewardId` (337–346) | 4 | **hard-coded 5×1** (`GateWidth`) | Gacha(rewardId), `CheckHp` | **no cells**. Keep level for HP (table `checkpoint`). Anchor column is always 1 |
| `e:typeId:level:rewardId` (347–356) | 4 | **hard-coded 3×1** (`ExitWidth`) | Gacha, `CheckHp` (table `exit`) | **no cells, no HP**. Passable marker. Always at (2,0) |
| `c:typeId:level` (357–367) | 3 | **hard-coded 2×3, and the anchor is shifted: `RowC = obj.Row; obj.Row += CartDepth − 2` (363–364)** | `typeId == "spawningcart"` | **no cells, no shift**. Passable marker. Always column 1, row `Depth−4` (142 zones) or `Depth−3` (2 zones) |

`CheckHp` (388–394) checks that `Root.MiningTargetHealths[typeId]` exists and that `1 <= level <= table.Length`. REUSE for r/s/p. TZ2 §12.4 also wants
"HP not positive" to be an error with zone and token index; the validator checks that globally at `Data/BalanceValidator.cs:87–94`.

### 1.3 Old artificial occupancy that TZ2 §8.3 says to remove

| What | Where | Cells it took |
|---|---|---|
| Exit footprint 3×1 | 53, 353 | (2..4, 0) |
| Gate footprint 5×1 | 54, 343 | (1..5, gateRow). The source has walls at (0, gateRow) and (6, gateRow) |
| Forge 2×3 + anchor shift | 59, 362–365 | cols 1–2, rows `row_c−1 … row_c+1` (the token row is `row_c`; the anchor moves to `row_c+1`) |
| Cannon 1×2 | 61, 284–295 | col 6, rows `row_c−1 … row_c`, **written into `CellOwner` with the forge's Ordinal** (292), so it is not in `Objects` |
| Merge zone | 135–176, plus `GameSession.cs:303–395` | **not** in `CellOwner`. It is a validator constraint (2 fully free rows) and the spawn preference |
| Gate row = sector −1 | 178–187, 275–280 | the whole gate row is excluded from both sectors |

The source has 578 anchors **below** `row_c`, so the bottom rows of the source map hold real objects. The new base pad is three extra rows
`Depth..Depth+2` under the map (TZ2 §8.3): spawn at (3, Depth+1), depot slots at (2, Depth+1) and (4, Depth+1). The audit's `reachable()` does exactly this (`outpost_v2_audit.py:65–81`).

### 1.4 Recipe for the new `SourceLevelImporter` (copy, then cut)

Copy verbatim: lines 118–132 (cell math, Neighbors4), the 201–206 count check, pass 1 (209–217), pass 2 (220–246), `CheckFields`, `ParseInt`, `CheckHp`,
`LayoutIssue`, and the r/s/x cases of `ParseToken`. Then change:
- p/e/c: `Width = Depth = 0`, `Cells = []`. No `RowC`, no `PlaceCannon`, no shift.
- Keep the archived strings (`RewardId`, `CardId`, `CardLevel`) without validating gacha or card ids (TZ2 §8.1).
- Gate sort: `(-Row, Index)`. Also issue an error on a duplicate gate row (the audit raises on it, `outpost_v2_audit.py:125–126`).
- Sector of r/s/x = `count(gate.Row > obj.Row)`. `obj.Row` is the anchor, which is the bottom row, i.e. `maxOccupiedRow`. Gate rows belong to the lower sector.
  Crossing check: `min(row of cells) < gateRow <= obj.Row` → the object lies wholly in the lower sector. There are 0 such objects in the data (`all_crossing_footprints: 0`).
- Gate barrier = a forbidden edge between `gateRow` and `gateRow−1`. Gate work cells = free `(col, gateRow)` with a path to the base.
- Required checks: exactly one `c` and one `e`, `Width == 7`, every r/s/p HP reference valid and > 0.
- Use `Index` (the source grid index) as the stable object key, so saves can key by Index. Old saves keyed by Ordinal **and** checked Index equality (`Sim/GameSession.cs:97–101`).

---

## 2. `Data/BalanceDto.cs`: Newtonsoft DTOs of `json-data.json`

Purpose: plain fields named as in the JSON. Undeclared sections and fields (Crusher, shop, LeaderboardCurrency*, …) are skipped (`MissingMemberHandling.Ignore`).
The header comment (6–18) states the rule: balance numbers are `double` (the file has integers above Int64 and a `1e+200` literal); codes and counters are `int`.
Newtonsoft throws on a fractional literal (even `2.0`) into an `int`, and that becomes a `BalanceLoadException` with the JSON path.

Fields relevant to TZ2 (verified against the source JSON):

| DTO (line) | Field | Type | Data fact | New use |
|---|---|---|---|---|
| `BalanceRoot` (22–43) | `Zones`, `Rocks`, `MineShafts`, `Obstructions`, `MiningTargetHealths` (30) | arrays / `Dictionary<string,double[]>` | 144 zones, 26 rocks, 20 shafts, 20 obstructions, 48 HP tables (26 rocks + 20 shafts + `checkpoint` + `exit`) | importer + HP adapter |
| `ZoneDto` (160–171) | `Id` | string `"zoneN"` | zone1..zone144 | key |
| | `WidthCells` / `DepthCells` | int | 7 / 12–64 | grid |
| | `Grid` | string | `W*D` comma tokens | importer |
| | `DecoSeed` | int | **0 in all 144 zones**, so useless as a seed | derive the deco seed from the zone number |
| | `FogOfWarCheckpoint` | int | 0 | ignore |
| | `RankMultipliers` | `RankMultipliersDto[]` | 1 element | DROP (TZ2 §12.4) |
| | `CrusherGridId` | int | 31/33/34… | ignore (TZ2 §8.1) |
| | `GridRowRanks` | `JToken` | null | ignore |
| `RockDto` (102–113) | `Id`, `MimicId`, `WidthCells`, `DepthCells` | string/int | all 1×1. 12 rocks have a `MimicId` (`rockonboarding*` → `rockbasic` / `rockgachawooden` / `rockgachairon`) | MimicId is **visual only**; HP is keyed by the source `typeId` |
| | `RewardCycle` | `RewardModelDto[]` (94–99: `int RewardType; string DetailedType; double Quantity`) | Soft/Hard/Gacha/… | **DROP** (TZ2 §8.2: disable for every rock type, including `rockbasic`) |
| | `CoreCurrencySeconds*`, `SoftCurrency*` | double/int | | DROP |
| `MineShaftDto` (116–128) | `Id`, `WidthCells`, `DepthCells` | | all 2×3 | geometry |
| | `GenerationDelaySec*`, `CurrencyOutput*`, `UpgradeCost*`, `CurrencyType` | | | DROP (no passive production) |
| `ObstructionDto` (131–136) | `Id`, `WidthCells`, `DepthCells` | | `block1x1v1..v6, v6L, v6R` 1×1; `block1x2`; `block2x1`; `block2x2` (all 140 2×2 objects); `block3x2`; `block2x3`, `block2x3vL/R`; `block3x3`; `waterfall1x4vL/R` 1×4; `wall1x1vL/R` 1×1 | geometry; `x` HP comes from the rule |
| `MiningTargetHealths` | `[typeId][level-1]` | `double` | max 1.87e25; 2060/4752 values > 2^53 | §12.4 rank source for r/s/p (the `e` table is unused) |
| `CheckPointDto` (202–212) | `StatModifierType[]`, `Modifier*[]` | | gate bonuses | **DROP** (TZ2 §4, §12.4). It holds no HP |

Everything else is DROP for the new mode: `BalancePropertiesDto` (46–75, deliveries, unit cap, stacks), `MinerDto` (78–91, old damage/attack FT formulas),
`GeneratorObjectiveDto`, `ReinforcementDto`, `SpawningCartDto`, `CardUpgradeCostDto`, `CardDto`, `GachaDto`, `DeliveryDto`, `FreeGachaCycleDto`, `SpellDto`,
`RewardTypes` (298–304). The DTOs must stay declared, because `BalanceRepository` requires them. Note: `Rarity.Name` (316–328) calls `Loc.T`, so the data layer depends on `Core/Text`.

---

## 3. `Data/BalanceRepository.cs`: load, hash and indexes. **REUSE AS-IS**

| Line | Signature | Note |
|---|---|---|
| 16 | `public const string ExpectedSha256 = "8975a5a0…85ee5";` | equals the audit `source_sha256` |
| 17–20 | `MinerId = "tinygoblin"`, `CartId = "spawningcart"`, `CheckpointId = "checkpoint"`, `ExitId = "exit"` | `CheckpointId` is the p HP table |
| 22–24 | `BalanceRoot Root; string Sha256; long SizeBytes` | |
| 35 | `public IReadOnlyList<ZoneDto> Zones` | sorted by numeric suffix (129–131) |
| 37–39 | `Dictionary<string,RockDto> Rocks; Dictionary<string,MineShaftDto> Shafts; Dictionary<string,ObstructionDto> Obstructions` | importer lookups |
| 49 | `public static BalanceRepository Load(byte[] bytes, bool requireExpectedHash = true)` | `requireExpectedHash:false` for mutation tests |
| 57–63 | settings: `MissingMemberHandling.Ignore, FloatParseHandling.Double, NullValueHandling.Include, Culture = InvariantCulture` | |
| 78 | `public static string ComputeSha256(byte[] bytes)` | reuse for config hashes (TZ2 §20 report) |
| 89–93 | `DecodeUtf8`: strips the BOM, strict UTF-8 (`throwOnInvalidBytes: true`) | |
| 153 | `public static int ZoneNumber(string zoneId)` | throws `BalanceLoadException` |
| 162 | `public ZoneDto Zone(int number)` (1-based) | |
| 168 | `public int ZoneCount` | |
| 171 | `public GameNumber TargetHp(string targetId, int level)` | returns `GameNumber.FromDouble(table[level-1])`. For rank01, prefer the raw `Root.MiningTargetHealths[id][level-1]` double |
| 180 | `public bool HasHpTable(string id)` | |
| 182, 184 | `ZoneMultipliers`, `CardMaxLevel` | DROP-use |
| 186 | `public static BalanceRepository LoadFile(string path)` | Editor/tests only |
| 189–193 | `public sealed class BalanceLoadException : Exception` | reuse for import errors |

Couplings: `BuildIndexes` (95–132) **requires** single-element `BalanceProperties`, `Reinforcements`, `SpawningCart`, `CheckPoint`, `Spells` and `FreeGachaCycle`,
`Miners[0].Id == "tinygoblin"`, and non-null Cards/Gacha/Deliveries/GeneratorObjectives. These are old-system preconditions, but they are always true for the hash-locked file.
Leave them in place.
Runtime path: `Runtime/GameController.cs:143` → `BalanceRepository.Load(boot.BalanceJson.bytes)`. `GameBootstrap.BalanceJson` is a `TextAsset`, assigned by
`Editor/ProjectSetup.cs:214`. It works in Android players (TZ2 §18). New configs (`OutpostBalanceConfig`, `EquipmentCatalog`…) can use the same TextAsset-on-bootstrap route.

---

## 4. `Data/BalanceValidator.cs`: old schema, geometry and reachability rules. **Mostly DROP**

| Line | Type / member | Verdict |
|---|---|---|
| 10–16 | `public sealed class ValidationIssue { string Path; string Message; bool IsWarning; }` | REUSE |
| 18–27 | `public sealed class ZoneSummary { ZoneId; Width, Depth, Rocks, Shafts, Gates, Obstructions, OccupiedRsx, SectorCount; ExitCol, ExitRow, ExitLevel, CartCol, CartRow; ExitGacha; int[] GateRows, GateLevels; string[] ShaftSpecs }` | ADAPT: drop ExitGacha/ShaftSpecs/Cart*; add hp/cargo/waves (see the audit fields) |
| 29–51 | `public sealed class ValidationReport { Issues; Zones; Sha256; SizeBytes; ErrorCount; Ok; Error(path,msg); Warn(path,msg); ToText() }` | REUSE. `ToText` prints only the first 200 issues (48) |
| 61 | `public static ValidationReport Validate(BalanceRepository b)` | ADAPT → new `OutpostLevelValidator.Validate` |
| 87–94 | MiningTargetHealths: every value finite and > 0 | **REUSE** |
| 96–127 | C.1 invariants (FT0, `UpgradeCostMultiplier=0`, `DamageBase=0`…) | DROP |
| 128–171 | objectives / cards / gacha | DROP |
| 173–192 | rocks: RewardCycle not empty (DROP); HP table exists (188, **REUSE**); `MimicId` refers to a rock (191, **REUSE**, visuals) | partial |
| 194–215 | deliveries / free gacha / checkpoint FT0 | DROP |
| 307–376 | `ValidateZone` | see below |
| 459 | `public static List<ZoneObject> ReachabilityCheck(ZoneLayout layout)` | **DROP**. TZ2 §8.5: "проверяет другие правила и не заменяет эту проверку" |

`ValidateZone` checks, one by one: layout issues → `Zones.<id>.Grid[<i>]` paths (310, **keep the path format**); Fog/GridRowRanks/RankMultipliers (313–317, drop);
**gate row fully covered** (319–327; only true because of the 5×1 gate footprint, so it would fail in the new mode, DROP); exit at (2,0) (329, keep as a data assertion);
width 7 (333, keep); merge rows on the start pad and above every gate (334–342, DROP); **sector 4-connectivity** using `CellSector` with −1 rows (345–349, DROP);
`CountPotentialWorkCells` (352–357, 437–456, old rules: obstruction neighbours excluded, DROP); shaft card requirements (360–368, DROP); old reachability (371–373, DROP).
Replace all of these with the audit algorithm: base-pad BFS (with the barrier at the current gate row) → remove reachable r/s/x in waves → check a gate work position
`(col, gateRow)` → next sector → exit reachable (`outpost_v2_audit.py:162–186`).

Tests pinned to the old rules, to retire or rewrite: `Tests/EditMode/DataAndLayoutTests.cs`, `MergeZoneTests.cs`, `CannonTests.cs`, `DataGeometryHpAcceptanceTests.cs`
(parts), and the `Tools/` validator CLI `Editor/BalanceValidatorCli.cs:125`.

---

## 5. `Sim/GameSession*.cs`: the old merge-game simulation. **DROP-FOR-NEW-MODE** (harvest skeleton parts)

### 5.1 Structure and event loop (`GameSession.Time.cs`)

- State: `GameState State` → `Player` (`PlayerProgressState`) + `Mine` (`MineProgressState`). Immutable `ZoneLayout Layout`. Caches: `m_CellCat`, `m_CellContainer`, `m_OrdinalByIndex`
  (`GameSession.cs:55–61`, rebuilt at 185–193).
- `public void Advance(long dtTicks)` (Time.cs:16–35):
  `loop { ProcessDueEvents(); if (remaining<=0) break; step = min(remaining, TimeToNextEvent()); ElapseTime(step); remaining -= step; if (++guard > 50_000_000) throw; }`.
  Events due exactly at the end of the interval are processed **inside** this call, so commands issued between calls run after the tick's events.
  That matches TZ2 §6.5 step 6. `Advance(a)+Advance(b) ≡ Advance(a+b)` is documented (Time.cs:13). **Keep this skeleton.**
- `TimeToNextEvent` (37–61) is a linear scan of all cats (attack phase), all objects (generator cycles), 3 timers and every loot item, clamped with `Math.Max(0, left)`.
  A due event that is never consumed gives zero steps until the guard throws. TZ2 requires that zero-length transitions cannot loop, so use a deadline priority queue.
- `ElapseTime` (63–75) accumulates `cat.PhaseTicks`, `Gen.CycleTicks`, timers, `loot.AgeTicks`, `PlayerTimeTicks` and `ZoneTimeTicks`. Progress is accumulated per cat, not stored as rational deadlines.
- `ProcessDueEvents` (101–174), same-tick order: **(1–2)** hits in `Mine.Cats` list order. Each hit applies damage **immediately** through
  `ApplyDamage` (178–189) → `CompleteTarget` (209–244) → `ContinueMining` (252–258, the AutoMining adjacent-only continuation). **(3)** generator cycles → AddGold / PayoutReady.
  **(4)** timers: FreeCat, Delivery, FreeChest. **(5)** `AutoCollectDueLoot`, then `TrySpawnPending` (261–285).
  TZ2 §6.5 instead wants: moves → hits **summed per target, one HP update, targets sorted by anchor index** → pickups → depot service → gates/completion →
  commands → reservation release and assignment by `workerId`. The old order is not reusable. Crits use `Player.Rng.Crit` (`Pcg32`).
- Ticks: `public const long TicksPerSecond = 1_000_000L;` (GameSession.cs:15, the same as TZ2 §6.5). `public const long MaxOfflineTicks = 8L*3600L*TicksPerSecond;` (16, the same 8 h).
  `public static long SecondsToTicks(double seconds) => (long)Math.Round(seconds * TicksPerSecond);` (195) rounds a double. TZ2 needs `ceil(TPS / speed)` computed exactly.
- `OfflineMode` (30) **changes the rules**: no deliveries, no TargetHit/GeneratorPaid events, no AutoMining reassignment, deferred automation. TZ2 §13 forbids this ("Старый флаг `OfflineMode` не должен отключать назначение новых целей").
- Money: `Mine.Gold` and `Player.Elixir` are `GameNumber` (a double below 1e300, then mantissa × 10^exp; "Целочисленной точности больших значений не обещает", `Numbers/GameNumber.cs:7–11`).
  TZ2 §18 needs exact integers. Audit: the maximum stage gold upper bound is `8915906358034478642`, which is above 2^53 and within 3.4% of `long.MaxValue`. Use `BigInteger` or checked `long`.
  No `System.Numerics` is used anywhere yet.

### 5.2 Public API: harvestable pieces

| Line | Signature | Verdict |
|---|---|---|
| GS:15–16 | `TicksPerSecond`, `MaxOfflineTicks` | REUSE values |
| GS:28 | `public readonly List<SimEvent> Events` ("потребитель очищает список сам") | ADAPT (typed events) |
| GS:69 | `public static GameSession NewGame(BalanceRepository balance, ulong seed, string slot = SaveSlots.Main, long nowUtcMs = 0, int startZone = 1)` | pattern only |
| GS:83 | `public static GameSession FromState(BalanceRepository balance, GameState state)`: validates the layout and checks that the object count and Index match the map | pattern (TZ2 §17 "не пересобирать HP") |
| GS:198, 200 | `long NextOp() => State.NextOpId++;` / `void Emit(SimEvent e)` | REUSE pattern (opId = idempotency key) |
| GS:206 | `public int OrdinalOf(int objectIndex)` | pattern |
| GS:215 | `public int ObjectAt(int cell)`: destroyed **rocks** free their cells; Obstruction/Shaft never do | ADAPT: r/s/x all free on destruction |
| GS:254 | `public bool IsTargetAvailable(int ord)` (gate sector = `SectorOfRow(obj.Row + 1)`, i.e. worked from below) | ADAPT idea |
| GS:265 | `public void WorkCells(int ord, List<int> result)`: side neighbours of the footprint in the open sector, with no object or container | ADAPT (new rules plus gate work cells) |
| GS:281, 289 | `IsWorkCellOf`, `AcceptsCat`: **allocate `new List<int>(8)` per call** | DROP (GC) |
| GS:303/324/344 | `SpawnPadRows`, `MergeRows`, `FindSpawnCell` (BFS from the merge row, zero-alloc buffers 331–334) | DROP. The buffer pattern is worth copying |
| GS:399–501 | `CardStateOf`, `CardLevel`, `IsAppliedCardCode`, `CardEffectValue`, `CardEffectAtLevel`, `CheckpointValue`, `GlobalIncomeMultiplier`, `ManagerMultiplier`, `UnitCap`, `HireDivisor`, `FreeCatIntervalSeconds` | DROP (cards + gate bonuses) |
| GS:515–565 | `CatDamage(int level)`, `AttackDelayTicks`, `CritChance`, `CritPower`, `CatDps`, `HireLevel`, `HireGauge`, `HirePrice`: per-cat-level `Formula` | DROP (TZ2: team stats) |
| GS:569–682 | `GeneratorDef`, `DefFor`, `IsGenerator`, `GeneratorOutput`, `GeneratorCycleTicks`, `UpgradeCost`, `NextThresholdLevel`, `MaxGeneratorLevel`, `IsAutoGenerator`, `AutoIncomePerSecond`, `GetRewardIncomeRate` | DROP |
| GS:690, 703 | `EtaSeconds(int ord)`, `WorkersOn(int ord)` | ADAPT idea for the "tap an obstacle" info (TZ2 §5) |
| C:9–25 | `public enum CommandResult { Ok, NotEnoughGold, NotEnoughElixir, NotEnoughDuplicates, CapReached, NoSpace, NoSpaceAtTarget, DifferentLevels, InvalidTarget, ClosedSector, NotAllowed, MaxLevel, EffectNotAvailable, NotFound }` | ADAPT (trim) |
| C:57, 59 | `public static string Describe(CommandResult r)`; `Reject(r)` emits `CommandRejected`. **Core calls `Loc.T`** | ADAPT: return codes, localize in the UI |
| C:861–893 | `SetSoundOn(bool)`, `SetVibrationOn(bool)`, `SetVolume(AudioGroup, float)`, `SetReduceEffects(bool)` | **REUSE AS-IS** (settings live in `Player.Settings`) |
| C:897–951 | `IsDebugSlot` plus `Debug*`, guarded by `throw` unless `SaveSlots.Debug` | REUSE the guard pattern (TZ2 §19 dev tools) |
| R:10–21 | `Ledger(string key, GameNumber)`, `AddGold(amount, source)` | ADAPT (exact integers; the ledger keys feed the TZ2 §20 report) |

### 5.3 Old systems that must be disabled (with every coupling point)

TZ2 §4: "отключить их команды, таймеры, обработчики наград и подписки … Простого скрытия кнопок недостаточно." Do not run GameSession in the new mode at all.
If any piece is borrowed, these are the hooks.

| System | Flag / state | Code points |
|---|---|---|
| **Loot auto-collect after 30 s** | `LootPickups` (GS:42), `LootAutoCollectSeconds = 30.0` / `LootAutoCollectTicks` (GS:52–53), `Mine.Loot`, `LootState.AgeTicks` | Time.cs:55–59 (next event), 72 (aging), 171–172 (`AutoCollectDueLoot`); R:62–66/75–79/87–91 (`DropLoot` in `GrantRockReward`), R:112–169; C:747 `CollectLoot`; C:677–679 (non-gold loot is credited at zone end) |
| **Free-cat timer (forge/cannon barrel)** | `Mine.FreeCatTimerTicks`, `FreeCatReady`, `ContainerKind.FreeCat`, `MinFreeCatIntervalSeconds = 60` (GS:18) | GS:180 (armed at EnterZone), 489–501; Time.cs:52, 69, 85–90, 154–158, 263–278 (spawn at `HireLevel()+SpawnLevelOffset`); C:582–585 (a gate shortens it); C:706–711 |
| **Random deliveries** | `Mine.DeliveriesEnabled`, `DeliveryTimerTicks`, `DeliveryReady`, `Player.DeliveryHistory`, `Player.Rng.Delivery/Timers` | Time.cs:53, 70, 92–97, 159–163, 279–284, 298–306 (unlock), 317–443 (`PruneWindows`, `NextDeliveryIntervalSeconds`, `DeliveryPool`, `TryCreateDelivery`, ads); C:718–737; C:669–674 |
| **Dynamite** | `Player.Dynamite`, `MaxDynamite = 4` (GS:17), `Balance.Spell` | C:771–807 (`DynamiteLevel`, `DynamiteDamage`, `UseDynamite(double gx, double gy)`), C:730–733, C:947 |
| **Free chest cycle** | `Player.FreeChest`, `FreeGachaCycle` | Time.cs:54, 71, 99, 164–169, 176, 307–312; C:756–769 `ClaimFreeChest` |
| **Generators: passive income** | Cart `ObjectStatus.Producing` set at EnterZone (GS:169–173), `GeneratorState` | GS:569–682; Time.cs:46–51, 67–68, 77–83, 128–151; C:409–425 `ActivateShaft` (opens a chest), C:427–440 `CollectPayout`, C:489–568 `UpgradeGenerator`/`PreviewToThreshold`/`UpgradeToThreshold`/`ApplyThresholds` (elixir) |
| **Rock rewards (old)** | `Player.RewardCycle` counter per Rocks.Id | R:43–107 `GrantRockReward` (gold = `FT3 seconds × GetRewardIncomeRate()`, elixir, hard, excluded currencies → `ExcludedRewards`) |
| **Cards / automation** | `Player.Cards`, `Mine.AutomationCheckPending` | R:30–40 `AddCards`, R:243–270 `OpenChest` → `GachaService.Open` + `CheckAutomation`; R:276–282 `RunDeferredAutomationCheck` (**called at the top of every online `Advance`**, Time.cs:18); C:443–485; C:809–856 `UpgradeCard`; C:929 `DebugGiveZoneCards` |
| **Gate bonuses** | `CheckPoint` codes 1/2/7/10 × `Mine.GatesOpened` | GS:427–501 (`CheckpointValue`, `GlobalIncomeMultiplier`, `UnitCap`, `HireDivisor`, `FreeCatIntervalSeconds`) |
| **Manual gate open + gate chest** | `ObjectStatus.Broken` → `Opened` only via a command | C:572–590 `OpenGate(int ord)` (manual, requires `GateOrder == GatesOpened`, opens a chest). New mode: automatic `SectorOpened` |
| **Exit as a target + old completion** | `ObjectKind.Exit` in `IsTarget`, `CanCompleteZone` needs the exit Broken, all shafts Producing and all gates opened | C:592–604, C:662–693 `CompleteZone` (exit chest, carries elixir and dynamite). New mode: all r/s/x/p cleared and all cargo delivered → `StageCompleted` |
| **Excluded-reward rocks** | `IsExcludedRewardRock` / `IsChestRock` (R:182–222), `RemainingRocks` (C:610) | DROP. In TZ2 **every** r is required, including battle-pass and chest rocks |
| **Merge** | `Mine.Merges` | C:186–405 (`DropCat` merge branch, `Merge`), `MergeRows` (GS:324) |
| **Manual drag/assign** | `m_DragCatId…` (C:129–184), `AutoMining` (GS:49, adjacent-only continuation) | C:144–337, 344–386; Time.cs:214–243, 252–258 |
| **Cannon / barrel hires** | `BarrelHires` (GS:36), `ContainerKind.Hire` | C:84–94, C:712–717; `ZoneLayout.PlaceCannon`; old-save cell relocation `RelocateFromObjectCells` (GS:114–136) |
| **Leveled hire** | `Reinforcement` FT formulas, `Mine.HireCount` | GS:544–565, C:67–101 `CanHire`/`Hire` |

---

## 6. `Sim/SimEvent.cs`: events and bus. **ADAPT**

| Line | Signature | Verdict |
|---|---|---|
| 5–41 | `public enum SimEventType { CatSpawned, CatMerged, CatMoved, CatAssigned, CatIdle, TargetHit, RockDestroyed, ShaftCleared, MineShaftActivated, ShaftAutomated, PayoutReady, PayoutCollected, GeneratorPaid, GeneratorUpgraded, ThresholdReached, GateBroken, CheckpointOpened, ExitBroken, MineCompleted, CampaignCompleted, ChestOpened, ContainerSpawned, ContainerOpened, DeliverySkippedAd, DynamiteUsed, CardUpgraded, RewardGranted, ExcludedReward, ZoneEntered, CommandRejected, LootDropped, LootCollected }` | DROP. New: `CargoReleased, CargoPickedUp, CargoDeposited, TeamUpgradePurchased, EquipmentChanged, SectorOpened, StageCompleted` (TZ2 §18), plus worker task/move events for the view |
| 44–58 | `public struct SimEvent { SimEventType Type; int Zone; long OpId; int ObjectIndex; int CatId; int OtherCatId; int Cell; GameNumber Amount; string Text; bool Crit; }` | ADAPT: fields are overloaded (`CatId` = loot id or container id; `OtherCatId = 1` means auto-collect). Use explicit fields: `WorkerId`, `CargoKey (runId, sourceGridIndex, unitOrdinal)`, `Units`, exact `Value` |
| 64–69 | `public interface IZoneCompletionHandler { void OnZoneCompleted(GameSession session, int zone, long opId); }` | ADAPT → `IStageCompletionHandler` |
| 76–121 | `public sealed class SimEventBus { const int RememberedOps = 512; void Register(IZoneCompletionHandler); void Unregister(IZoneCompletionHandler); void Dispatch(GameSession session, IReadOnlyList<SimEvent> batch); }` | ADAPT. Dispatch only reacts to `MineCompleted`, de-duplicates by `OpId` over the last 512 ops, and copies the handler array first (a handler may unsubscribe). Keep this idempotency pattern for the single completion reward |

Contract (43): "Публикуется после фиксации транзакции" (published after the transaction commits). Keep it: the simulation credits gold, and views only consume events (TZ2 §7.3: crediting is not driven by an Animation Event).

---

## 7. `Sim/EconomyTables.cs`: C.4 pace tables for the old economy. **DROP**

`public static string Build(BalanceRepository balance, int generatorLevels = 40, int generatorZones = 10)` (16) and
`public static double TimeToKill(GameSession s, string targetId, int targetLevel, int catLevel)` (38) build sessions through `NewGame`/`DebugCompleteTarget`/`ActivateShaft`.
`TimeToKillTargets` (49), `GeneratorsInFirstZones` (137) and `IncomePerSecondAt(GameSession s, int ord, int level)` (210, which mutates and restores the generator state) are generator- and card-bound.
Reusable: `public static string Short(double seconds)` (26–35, compact duration with RU units) and the "data-driven targets, no literal IDs" idea.
Tests: `Tests/EditMode/EconomyReportTests.cs`, `ValidationReportGenerator.cs:293`.

## 8. `Sim/GachaService.cs`: cards and elixir chest. **DROP**

`public sealed class GachaContext { int Rank; RankMultipliersDto Multipliers; HashSet<string> GeneratorsInZone; Dictionary<string,int> RequiredCardLevels; Func<string,int> CardLevel; }` (8–16),
`public static ChestResult Open(BalanceRepository b, GachaDto g, GachaContext ctx, Pcg32 rng)` (23) and `public static int StackCount(BalanceRepository b, int rarity, int count)` (157)
cover cards, elixir, manager weights and zone RankMultipliers, all of which TZ2 forbids.
Only pattern worth copying for `StageRewardConfig` weights (TZ2 §10.2): `PickDistinct` (195–217), a weighted draw without replacement over `Pcg32.NextDouble()`.
It uses double weights. For save/load and exact replay, prefer integer weights with `NextInt(total)`.

## 9. `Sim/ScenarioBot.cs`: old merge/card strategy bot. **DROP** (copy the harness shape)

Types: `BotZoneOutcome { Completed, Wall, Limit }` (10–15); `BotAction { T; Kind; Detail }`, with kinds step/routine/offline/session/result (18–35);
`BotZoneReport` (37–75, hires/merges/forge/shafts/elixir…, `Sources` ledger delta, `Actions`); `BotShaftInfo` (78–86).
`public sealed class ScenarioBot` (95) knobs: `DecisionStepSeconds = 1.0; SessionSeconds = 30*60; OfflineSeconds = 4*3600; OfflineChunkSeconds = 15*60; ZoneLimitSeconds = 48*3600; WallSeconds = 2*3600; ModelOffline = true` (97–104).
`public BotZoneReport PlayZone()` (127–213): loop `Act()` → `Advance(1 s)` → `OfflineProgress.MarkProcessed`; after 30 min it runs `PlayOffline` (219–271) in 15-min `OfflineProgress.Apply` chunks.
"Wall" = 2 h without any available improvement. `Act()` (395–487) is entirely old: CompleteZone, containers, free chests, ActivateShaft, OpenGate, payouts, UpgradeCards, dynamite, MergePairs, Hire, BuyUpgrades, AssignIdleCats.
Reusable for TZ2 §20 (policies A/B/C, a 24 h limit, a 2 000 000 event watchdog, a per-run report): the session/offline loop, the ledger-delta report, `Clock` (836), and the `FormatReport`/`FormatActionLog` layout (949, 1072).
The policy logic must be new.

---

## 10. Audit oracle: `Docs/TZ/analysis/outpost_v2_audit.json`

Produced by `python3 Docs/TZ/analysis/outpost_v2_audit.py Assets/Game/Data/Source/json-data.json --out Docs/TZ/analysis/outpost_v2_audit.json`.
The script parses the JSON with `parse_float=Decimal` (`outpost_v2_audit.py:195`), uses `Fraction` for all formulas, and `isqrt` for the exact ceil of sqrt.

Top level: `scope` ("Source JSON, static geometry under v2.1 rules, exact reference formulas. No Unity/worker/offline run."), `profile_version: "outpost-2.1-b1"`,
`source_sha256` (= `BalanceRepository.ExpectedSha256`), `source_bytes: 2254970`, `zone_count: 144`, `widths: [7]`, `depth_range: [12, 64]`,
`token_counts: {".":22733, "r":16614, "s":1232, "x":4283, "p":280, "e":144, "c":144}`, `definitions: {Rocks:26, MineShafts:20, Obstructions:20, CrusherGrids:34, Cards:61, Gacha:58}`,
`all_static_clearance_passed: true`, `level_overrides_required_by_static_check: []`, `all_crossing_footprints: 0`,
`original_v2_0_value_L60_unreachable_zone_count: 144`, `corrected_v2_1_value_L50_passes_necessary_bound_zone_count: 64`, `adapted_hp_range: [20, 961]`.

`controls` (17 keys, matching TZ2 §12.5):

| Key | Value |
|---|---|
| `pickaxe_costs_zone1_L0_L1_L9_L10` | `[20, 24, 89, 105]` (= `ceil(20·(59/50)^L)`) |
| `zone1_rank0_r_hp_units_gold` | `[20, 2, 10]` |
| `zone1_rank05_x_area2_hp` | `80` |
| `zone13_grid78_hp` | `318` (the double-ceil trap: a naive computation gives 319) |
| `pickaxe_milestone_L9_L10_L19_L20` | `[1, 2, 2, 4]` |
| `value_milestone_L9_L10_L24_L25_L49_L50` | `[1, 2, 2, 4, 4, 8]` |
| `original_pickaxe_L24_damage_approx` | `1182.0502418737003` |
| `corrected_pickaxe_L20_damage_approx` | `1067.1528023051294` |
| `legendary_L20_bonus_percent` | `"117"` |
| `common_L20_backpack_extra` | `20` |
| `maximum_coin_value_zone144_at_value_cap_and_legendary_hat_L20` | `"4286493441362730"` |
| `maximum_coin_exceeds_2_pow_53` | `false` |
| `maximum_stage_gold_upper_bound` | `"8915906358034478642"` |
| `maximum_stage_gold_upper_bound_exceeds_2_pow_53` | `true` |
| `item_upgrade_parts_total_1_to_20` | `[673, 1001, 1665, 2655]` |
| `completion_parts_z1_z4_z5_z10_z144` | `[10, 10, 15, 20, 150]` (= `10 + 5·(z div 5)`) |
| `guaranteed_completion_parts_total_campaign` | `11590` |

Per-zone fields (`zones[i]`, 21 keys), with what each means (`outpost_v2_audit.py:89–189`):

| Field | Meaning | Test use |
|---|---|---|
| `zone`, `width`, `depth` | id, 7, DepthCells | import |
| `tokens` | count per token kind, including `.` | importer token histogram |
| `obligatory_targets` | count of r+s+x+p (e.g. zone1 = 20, zone13 = 119, zone144 = 224) | progress counter total |
| `static_clearance` | always true | validator |
| `clearance_waves` | number of BFS removal waves over all sectors (zone1 = 4, zone13 = 19, zone144 = 30) | new reachability validator, exact match |
| `crossing_footprints` | 0 | sector assignment |
| `unique_source_hp` | distinct `MiningTargetHealths` values over r/s/p (zone13 = 20, zone144 = 66) | rank01 denominator + 1 |
| `min_adapted_hp` / `max_adapted_hp` | min and max MaxHp over r/s/x/p | adapter |
| `selected_target_controls` | `{gridIndex: hp}`, only `zone13: {"78": 318}` | exact-ceil test |
| `hp` | Σ MaxHp over r/s/x/p (zone1 = 1506) | adapter total |
| `cargo_units` | Σ `max(2, ceil(hp/10))` (zone1 = 154) | cargo total |
| `base_gold` | Σ units × `ceil(5·(6/5)^(z−1)·TypeValue)` with no bonuses (zone1 = 554; zone144 = 4375034879849264) | cargo value at zero bonuses |
| `maximum_gold_upper_bound` | the same at value cap 50 × legendary hat L20 | overflow test (zone144 is above 2^53) |
| `base_resource_budget_without_rounding` | decimal string, Σ units·5·TypeValue (without S(z)) | balance |
| `original_value_L60_*`, `corrected_value_L50_*` | budget-bound proofs (string decimals + bools) | balance docs only |

Note: the audit's rank01 ranks **all r/s/p together**, gates included (table `checkpoint`). `x` uses a fixed 0.5. Area = the footprint cell count, and `p` uses 1.
For `n >= 7`, AreaFactor = 5/2. `MaxHp` is computed in `Fraction`, not double.

---

## 11. Gotchas

- **Unstable gate sort** (`ZoneLayout.cs:272`, `List.Sort` with a row-only comparer). It is harmless today only because no zone has two gates in one row; add the `Index` tiebreak.
- **The c anchor shift** (`ZoneLayout.cs:364`): `Cart.Row` ≠ the token row, while `RowC` is the token row. Any code that ports "forge row" logic picks up a ±1 bug.
  TZ2 §8.3: do not carry this over.
- **The cannon is hidden in `CellOwner`** under the forge's Ordinal (`ZoneLayout.cs:292`), so it is invisible in `Objects`. A naive "remove the forge" still leaves the cannon cells blocked.
- **`Cells[0]` is the anchor** only because of the dx-outer, dz-inner loop order (225–227). The old code uses `CellSector[obj.Cells[0]]` as "the object's sector"
  (`GameSession.cs:260`, `BalanceValidator.cs:441,472`).
- **Old `x` is never a target** (`ZoneObject.IsTarget` excludes Obstruction), and destroyed shafts never free their cells (`GameSession.cs:219`). Both rules flip in TZ2.
- **HP precision:** never compare HP through `GameNumber` arithmetic or with an epsilon when building the distinct set; use raw `double ==`.
  Assert the per-zone distinct count against the audit's `unique_source_hp` as a guard against parser drift.
- **Never `Math.Ceiling` a double HP expression** (TZ2 §12.4; zone13 #78 = 318). Port `adapted_hp` from `outpost_v2_audit.py:53–62` with `long`/`BigInteger`.
- **Hash lock:** `json-data.json` is LF-only, has no BOM and has 40708 lines, and the repository has no `.gitattributes`. A Windows `core.autocrlf=true` checkout or any re-save changes
  SHA-256, and the game then refuses to start (`BalanceRepository.cs:53–54`). The file is synced to the hub (it is not in the ignore list), so do not re-format it.
- **Core asmdef** (`CatMiners.Core.asmdef`): `noEngineReferences: true`, `overrideReferences: true` with only `Newtonsoft.Json.dll` precompiled. The core compiles outside Unity
  (`Tools/CoreTests/run.ps1`). `TestData.ProjectRoot` walks up from the cwd or `AppContext.BaseDirectory` to find `Assets/Game/Data/Source/json-data.json`, which works in both
  Unity batchmode and `dotnet test`. Keep new core types engine-free, and check that `System.Numerics.BigInteger` resolves in both builds.
- **Localization inside the sim** (`Loc.T` in `Rarity.Name`, `Reason`, `AutomationRequirement`, the `ApplyThresholds` text). The new sim should emit codes only.
- **GC hot spots in the old code:** `IsWorkCellOf`/`AcceptsCat`/`DropCatCore` allocate lists; `NearestAdjacentTarget` copies `m_Tmp`; `FindCat`/`ContainerAt`/`FindLoot` are linear scans;
  `TimeToNextEvent` scans every object on every step; and `CheckpointValue`/`UnitCap` loop over all cards per call. The good counter-example is the zero-alloc BFS buffers in `FindSpawnCell` (`GameSession.cs:330–334`,
  "HUD спрашивает её при каждом обновлении кнопки найма — без выделений").
- **Online Advance has a side effect:** `RunDeferredAutomationCheck()` runs before the time step (`GameSession.Time.cs:18`), so online and offline paths differ. TZ2 §13 requires that they are identical.
- **`CompleteZone` silently credits non-gold loot and carries delivery boxes over** (`GameSession.Commands.cs:669–679`). These are invisible bonuses, which TZ2 §4 forbids in the new mode.
