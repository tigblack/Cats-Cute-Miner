# Reuse map: art pipeline and audio (NAME=art-audio)

> **Устарело для карточек (9 октября 2026).** Решение отключить карточки, manager-карты, сундуки и эликсир отменено: система 61 карточки возвращена в режим CatOutpostV2 (ТЗ §10.4, `Docs/Rework/CARD_EFFECT_MAPPING.md`). Пометки DROP/удалить ниже для карточек, `Gacha`, `RewardCycle` (карточки и эликсир), `cards.py`, `HudBlueprints` и `UpgradeCard` больше не действуют; это карта прежнего кода, а не инструкция.

Scope: `Tools/PixelArt/*` and `sprites/*.py`, the Unity consumers of the atlas (only where they touch the rig),
`Runtime/Audio/*.cs`, `Tools/Audio/*`, `Docs/Audio/build_sfx.py`, and the art and audio docs.
Target: the auto-incremental mode in `Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md` (§5 base, §6 states, §7 cargo,
§10 equipment, §16 art/sound).
For `CatView`, `PixelWorld` and `BoardView` internals see `visual.md`. For `GameController.Present`, see `controller.md`.

Verdict legend: **REUSE** = use as-is · **ADAPT** = keep, but change what is listed · **DROP** = not used by the new mode.

---

## 0. TL;DR

- The pipeline is solid and mode-agnostic. All art is Python code (`sprites/*.py`) that draws into palette-checked
  canvases. `build.py` packs one atlas and embeds the PNG as base64 in `Runtime/Pixel/PixelAtlasData.g.cs`, because
  PNG files are not synced. Unity looks sprites up by name only (`PixelArt.World/Ui`). **REUSE.**
- The cat rig (`sprites/cats.py`) is the main asset to extend. It uses a 40×40 canvas with pivot (20, 4) and faces right.
  Left-facing is `flipX` around the pivot. Layers today: body (coat) → helmet (colour) → weapon. Poses are data
  (`Pose` + `POSES`). `Parts` masks and `BELT_ROW` are still in the code: they are leftovers of a removed gear layer
  (`cat_gear_*`: belt, backpack, scarf; it was removed on 8 Oct). This is exactly the hook for backpack and boots.
- Old-mode coupling in art: the 8-level weapon cycle and helmet colour cycle (`manifest.WEAPONS/HELMET_COLORS`,
  `CatLook`), merge visuals (`slot_marker*`, `fx_ring_*`, `badge_level*`), cards (`cards.py`), cannon, barrel,
  crates, forge, dynamite, and shaft automation states.
- Audio runtime is clean: `Sfx` uses a 6-voice pool with per-cue interval, jitter and priority. `MusicPlayer` crossfades
  two tracks. `Ambience` and `AudioMix` are code groups. The only old-core couplings:
  - `AudioMix` uses `Core.Sim.PlayerSettingsState` / `AudioGroup` (they live in `Core/Sim/GameState.cs`);
  - `Sfx.Hit(int weapon, …)` uses `CatLook` weapon indices;
  - all cue triggers live in `GameController.Present(SimEvent)` (`GameController.cs:576-698`).
- New cues (step, pickup, bag, depot, unload, deliver, hire, equip) are about 15 lines each in `Sfx`, plus a recipe
  in `build_sfx.py`. Several can be `Sfx.Variant(...)` of existing clips, which needs no new files.

---

## 1. Sync, environment and batch quirks (read first)

| Fact | Consequence |
|---|---|
| `*.png`, `*.ogg`, `*.wav` are in the Mutagen ignore list | The atlas lives in C# (`PixelAtlasData.g.cs`, 541 KB, atlas 2048×1024, hash `346ba57d0cbbdecb`). `Tools/PixelArt/out/*.png` (sheets, `atlas.png`, `manifest.json`) exist **only on the hub**. `Assets/Game/Resources/Audio/*.ogg` exist **only on the PC**: the hub only has their `.meta` files. Do not recreate or delete them. |
| `Tools/.venv` is ignored (`.venv`) | The venv exists only on the hub: Python 3.12 with PIL 12.3, numpy, scipy, soundfile. Hub system `python3` also has PIL 12.2. `python3 Tools/PixelArt/build.py` runs **on the hub** because python3 exists there, so it is not routed to the PC. Audio scripts need `Tools/.venv/bin/python` (numpy/scipy/soundfile). |
| Binary transfer to the PC | `tar` → `base64 -w 76 > Docs/Audio/_transfer_<name>.b64` (text syncs). Then on the PC run `powershell -ExecutionPolicy Bypass -File Tools/Audio/unpack_transfer.ps1 -Name <name> -Dest Assets/Game/Resources/Audio`. It prints SHA-256: compare it with the hub, then delete the `.b64`. Large stdin through `on-remote` is unreliable (`SOUNDS.md:152-155`). |
| Audio sources on the hub | `Temp/audio/src/` (36 MB, hub-only, `/Temp` is ignored) holds **only the subset used so far**: 9 Kenney impact files, `cloth2`, `bookClose`, 5 jingles, 4 meows, spring, rd-*. Footsteps and coin-handle files are **not** there. Re-download the full packs on the PC (URLs in `Docs/Audio/CREDITS.md`) and copy them to the hub as base64. |
| Sound never heard | Music, ambience and the new SFX were verified by signal analysis only (`VISUAL_QA.md §5, §8`). TZ §16 requires checking the music loop seam by listening after Unity import. |
| No git on the hub, and the project is not a git repo here | The removed `cat_gear_*` code cannot be recovered. It is not in the `__pycache__` either. |
| Write races | `build.py` rewrites `PixelAtlasData.g.cs` and `PixelPalette.g.cs` on the hub. Mutagen then pushes them to the PC and Unity recompiles `CatMiners.Runtime`. Do not edit the `.g.cs` files by hand or on the PC. |

---

## 2. `Tools/PixelArt` — files, API, verdicts

### 2.1 `build.py` (283 lines) — atlas build. **REUSE** (ADAPT `MODULES`)

Purpose: imports `sprites/<m>.py`, calls `build(reg)`, validates, trims, dedupes, shelf-packs the atlas, writes the
preview sheets, `manifest.json`, `atlas.png` and `PixelAtlasData.g.cs`.

```python
MODULES = ['env', 'rocks', 'structures', 'themes', 'cats', 'ui', 'icons', 'fx', 'tutorial', 'loot', 'cards']  # :40  (order matters: themes after env/rocks/structures)
PAD = 2; ATLAS_W = 1024                                                     # :41-42
def load_modules(only: List[str]) -> pxl.Registry                           # :45  sets reg.group = module name
def check(reg: pxl.Registry, require_all: bool) -> List[str]               # :58  palette, alpha, ≤512 px, border fits, non-empty, REQUIRED
def trimmed(reg: pxl.Registry) -> pxl.Registry                              # :89  crops transparent margins, recomputes pivot (except border/extrude)
def _key(e) -> Tuple                                                         # :107 (w, h, pivot, border, extrude, pixels) → dedupe key
def pack(reg) -> Tuple[Image.Image, Dict[str, Tuple[int,int,int,int]]]       # :113 shelf pack, width 1024 → 2048; height = next pow2 ≥ 64
def sheet(entries, scale: int = 4) -> Image.Image                           # :162 preview ×4 with name and red pivot cross
def write_cs(atlas, pos, reg) -> str                                         # :194 emits PixelAtlasData.g.cs (Sprites[] + Png base64 in 1600-char chunks)
def main() -> int                                                            # :236
```

**How to run** (argparse, `:237-241`):

```
python3 Tools/PixelArt/build.py                 # full: gen_palette → all MODULES → check(require_all) → trim → pack → out/atlas.png, out/manifest.json, PixelAtlasData.g.cs
python3 Tools/PixelArt/build.py --only cats ui  # those modules only: check (incl. REQUIRED_GROUPS of those modules) + out/sheet_<m>.png; NO atlas/C#
python3 Tools/PixelArt/build.py --check         # all modules, check only (still writes sheets + PixelPalette.g.cs)
python3 Tools/PixelArt/build.py --allow-missing # write atlas even if REQUIRED names are missing (Unity shows pink placeholders)
python3 Tools/PixelArt/build.py --help          # the ONLY side-effect-free invocation (parse_args exits before gen_palette)
```

Gotchas:
- **Every mode except `--help` writes files.** `gen_palette.main()` (`:242-243`) always rewrites
  `Runtime/Pixel/PixelPalette.g.cs`. The content is the same if `RAMPS` did not change, but the timestamp changes and
  Unity reimports. `out/sheet_*.png` are always written.
- `--only` with no names gives `args.only == []`, so `partial = False` (`:245`) and it runs a **full write**.
- The `--only` / `--check` exit code is 1 on errors. A full build refuses to write if any error exists (`:266-268`).
- Packing: if the atlas does not fit 2048 in height at width 2048, it silently grows to 2048×4096 (`:123-140`). Today
  it is 2048×1024, about 8 MB VRAM, and 96% of a 1024² atlas was already used (`VISUAL_QA.md §8`). The cat layers
  for the new mode (§4.6) can push it to 2048×2048. Check `out/manifest.json["atlas"]` after each build. A second atlas
  would need changes in `PixelArt.cs`, because it holds one `PixelAtlasData.Png`.
- Dedupe (`_key`) is pixel-exact including the pivot. Layers that repeat across frames (boots and hat at the same
  head or feet position) cost one atlas rect. Current state: 1821 names, about 1211 unique rects; cats are 1292 names
  and 703 rects.
- Trimming moves the pivot to rect-relative coordinates. The pivot can be negative or outside the rect, e.g. the
  helmet pivot after trim is (12, −19). This is normal: Unity uses the pivot to keep all layers aligned.
- Speed: building `cats` in memory takes 6.4 s on the hub (measured, no files written). A full build takes a few tens
  of seconds.
- `weapons.py` is **not** in `MODULES`: `cats.py` imports it. `cards.py` imports `sprites.cats` and
  `sprites.structures`. `icons.build` imports `sprites.rocks`. `themes._bases` rebuilds env/rocks/structures when it
  runs alone.

### 2.2 `pxl.py` (369 lines) — palette, canvas, registry. **REUSE**

```python
RAMPS: Dict[str, List[str]]          # :34-78  ink, navy, slate, stone, wood, amber, gold, purple, teal, blue, red, green, pink, cream, cave, frost, copper, rust, fur_{ginger,gray,black,white,cream,brown}
PALETTE: Dict[str, RGB]              # :80  '<ramp><i>' + 'white','black';  PALETTE_SET :86
def C(c: ColorLike) -> Optional[Tuple[int,int,int,int]]          # :89  'navy3' | '#hex' | RGB | RGBA | None (transparent)
def ramp(name: str) -> List[str]                                   # :106
def with_alpha(c: ColorLike, a: int) -> Tuple[int,int,int,int]     # :111
class Canvas:                                                      # :117  PIL coords, (0,0) top-left, y down
    set(x,y,c) :129  get :136  opaque :141  copy :144
    rect(x,y,w,h,c) :151  frame :156  hline(x0,x1,y,c) :165  vline :169  line(x0,y0,x1,y1,c) :173 (Bresenham)
    ellipse(cx,cy,rx,ry,c) :191  polygon(pts,c) :200  fill_where(pred,c,only_opaque=True) :217
    recolor(mapping) -> Canvas :226   outline(c='ink1', diagonal=False, inside=False) :239
    shade(light,dark,base,highlight_edge=True) :256  blit(other,x,y) :272  flipped() :277  crop(x,y,w,h) :283
    dither(x,y,w,h,c,density=0.5,seed=0) :289 (Bayer 4×4)  speckle(colors,density,seed,region=None) :299
    @staticmethod from_ascii(rows, legend) -> Canvas :308   ('.'/' ' transparent; unknown char → KeyError)
    scaled(k) -> Image :322
@dataclass SpriteEntry(name, canvas, pivot, border=(0,0,0,0), group='misc', alpha=False, extrude=False, note='')   # :327
class Registry: add(name, canvas, pivot=None, border=(0,0,0,0), alpha=False, extrude=False, note='') -> SpriteEntry # :344
def pivot_feet(cv, feet_from_bottom=0) -> (w/2, feet)              # :358
def tiled_preview(cv, nx, ny) -> Canvas                            # :363
```

Rules enforced at build time:
- Every opaque RGB must be in `PALETTE`.
- Alpha strictly between 0 and 255 is allowed only with `alpha=True`.
- Name rule: lowercase `[a-z0-9_]`, globally unique (`Registry.add` raises on a duplicate).
- The pivot and border use **Unity convention** (pixels from the bottom-left). The canvas uses PIL convention
  (from the top-left).
- New colours go only into `RAMPS`, plus a line in `PIXEL_STYLE_GUIDE.md §2`. `gen_palette` mirrors them to
  `PixelPalette.<Camel><i>` automatically.

### 2.3 `manifest.py` (207 lines) — name contract with Unity. **ADAPT**

```python
REQUIRED: Dict[str, str]; REQUIRED_GROUPS: Dict[str, List[str]]                 # :11-12
def _req(group: str, names, desc: str) -> None                                   # :15
FURS = ['ginger','gray','black','white','cream','brown']                         # :26
WEAPONS = ['pick','hammer','crusher','saw','drill','dualpick','chainsaw','laser']  # :30  ← old 8-level cycle
TOOLS = ['saw','drill','chainsaw','laser']                                       # :31
HELMET_COLORS = ['yellow','blue','red','green','purple','teal','pink','white']   # :32  ← old helmet-per-cycle
MINE_FRAMES (9) :35 · TOOL_FRAMES :37 · CAT_FRAMES (62, ORDER = Unity indices) :38-41
def weapon_frames(weapon: str)  # :44  frames a weapon layer exists for (tools skip mine_*, swing weapons skip tool_*)
CHESTS = [...] :48
```

Old-mode `_req` entries to remove together with their drawing code (otherwise the full build fails with
"нет обязательного спрайта" — "missing required sprite"):
- merge: `slot_marker`, `slot_marker_hi` (`:53,:189`);
- the whole `cards` group (`:112-122`);
- `barrel_*`, `cannon_*`, `crate_*` (`:104-109`);
- `shaft_*` automation/payout overlays (ribbon/heap/glint/plaque are not in REQUIRED, but `overview.py` uses them);
- `fx_muzzle_*`, `fx_boom_*`, `fx_ring_*`;
- `icon_cards*`, `icon_dynamite*`, `icon_chest*`, `icon_elixir`;
- `loot_elixir`, `loot_hard`;
- `ui_card_*` (keep the rarity frames if they are reused for items).

Keep: env tiles and decor, rocks (all mimic visuals stay per TZ §8.2), gate, exit, `fx` shadows/puff/spark/coin/
impact/glow/hp/bubble, ui, most icons, tutorial.

### 2.4 `gen_palette.py` (32 lines). **REUSE**

`main() -> None` (`:14`) writes `Runtime/Pixel/PixelPalette.g.cs`: `public static readonly Color <CamelRamp><i>`.
It is called by every `build.py` run.

### 2.5 `overview.py` (210 lines) — review sheets → `out/overview/`. **ADAPT**

- `cat(E, fur, weapon, colour, frame)` (`:27`) composes exactly 3 layers. Add the new layers here.
- `themes_sheet` / `shaft_sheet` reference old objects (`rock_chest_iron`, shaft states, cannon-free layout).
  They are harmless but go stale.
- Run: `Tools/.venv/bin/python Tools/PixelArt/overview.py [--out DIR]`. It writes PNGs only, on the hub. The agent can
  inspect them with the Read tool (image).

### 2.6 `sprites/*.py` verdicts

| Module | Content | Verdict |
|---|---|---|
| `env.py` (933) | floor/wall tiles (extrude), wall decor (`beam_post`, `lantern_*`, `crystal_cluster_*`, `banner_paw`, `barrel`, `crate`, `sack` :767, `minecart_ore`, `rails_v`, `mushrooms`, `sign_question`, `chain_hang`), `slot_marker` :503 | REUSE; DROP `slot_marker*` (merge zone). `sack()` (12×14 burlap sack) is a ready reference for **cargo bags on the ground**. |
| `rocks.py` (1241) | `rock_*` + `_d1..d3` damage stages, `block_*` autotiles, `block_single_*`, `wall_gate_*`, `waterfall_*` | REUSE. ADAPT: TZ §8.2 makes `x` (blocks, waterfalls, walls) minable with a "clearing animation" → add `_d1..d3` stages for `block_*`/`waterfall_*` via the existing `damaged()`/`_stone_like` helpers. |
| `structures.py` (1849) | containers, cannon, forge, shaft, gate, exit; helpers `blank :31, put :35, clip :42, remap :50, vbeam :93, hbeam :109, brace :123, bolt :131, rock :142, thick_line :178, lantern :218, paw :240, glow :255, rails :287, crate_body :322, crate_lid_strip :359, label_plate :367, band :376, coin_top :384, masonry :623, anvil :644, golden_cat :665, ingots :699` | Gate and exit: REUSE. Shaft (`s` = rich deposit): REUSE the base, wheel and light; DROP the ribbon, heap, glint and plaque states. Forge, cannon, barrel and crates: DROP (no forge footprint, no cannon, no deliveries or dynamite). **Write the new base buildings** (depot with 2 unload slots and coin crates, hire board, tool bench, bag rack, equipment wardrobe) as a new module `base.py` with these helpers. |
| `themes.py` (206) | `_ramp`, `THEMES`, `PREFIXES` :35, `GROUPS` :37, `GLOW`, `recolor()`; auto `_crystal`/`_copper` variants for prefixed names in env/rocks/structures | REUSE. ADAPT `PREFIXES`/`GROUPS` if base buildings have stone/wall parts to theme. The variant is added only if a recolourable colour exists in the sprite. |
| `cats.py` (782) | the rig, badges, `select_ring`, digits | ADAPT, see §3. `badge_level*`/`digit_*` (level badge): DROP for cats, since levels are gone. Digits may still be useful elsewhere. |
| `weapons.py` (304) | 8 weapon drawings along the frame axis | Drawings: REUSE. The 8-level cycle: DROP. See §3.5. |
| `ui.py` (668) | 9-slice kit; `_check_slice` validates slices | REUSE. Rarity frames `ui_card_frame_{common,uncommon,rare,epic,legendary}` can be reused for the 4 item rarities (skip `uncommon`). |
| `icons.py` (1171) | 16×16 / 24×24 icons; helpers `_legend :22, _art :32, _place :36, _outlined :43, _icon(rows, mat, extra, size=16, dx, dy) :48`; `icon_pickaxe :635` | REUSE most. ADD 16 item icons (pick/hat/backpack/boots × 4 rarities), slot icons, bag/cargo-in-transit, depot, team. DROP card, dynamite and chest icons. |
| `fx.py` (627) | shadows `shadow(w,h) :92`, puff, spark, coin, impact, glow, hp bars, world bubble, select bracket | REUSE (`fx_coin_*` = coins flying to the wallet at the depot). DROP `fx_ring_*` (merge), `fx_boom_*` (dynamite), `fx_muzzle_*` (cannon). |
| `tutorial.py` (399) | `tutor_cat_0..2`, `ui_portrait_frame` | REUSE |
| `loot.py` (237) | `loot_gold` 20×18 pivot (10,0), `loot_elixir`, `loot_hard`, `loot_glow`; helpers `coin_flat :59, stack :78, coin_up :87` | ADAPT: coins-only economy. Turn into **cargo-bag piles** (1 bag / small heap / big heap, for visual batch merging), reusing `sack()` + `coin_up`. DROP elixir/hard. |
| `cards.py` (755) | card portraits | DROP the module (and remove it from `MODULES`). `card_cat_hat()` (`:159`: wide-brim hat with ear cut-outs via `cats.EAR_BOXES`) is a good **reference for hat shapes**. Dropping it also removes the `cards → cats` import coupling. |

---

## 3. The cat rig (`sprites/cats.py`, `sprites/weapons.py`, Unity `CatLook`)

### 3.1 Canvas, pivot, layering

- `CW, CH = 40, 40`, `PIVOT = (20.0, 4.0)` (`cats.py:29-30`).
- The feet sit on PIL row 35. The pivot line is the boundary between rows 35 and 36. Pivot x = 20 is the **mirror
  axis**: Unity `flipX` mirrors around the sprite pivot, so every layer must share the same cat centre.
- In Unity (`Views.cs` CatView):
  - the pose transform is at `FeetY = -10 px` below the cell centre (`:215`);
  - layers are children at (0,0) under one `SortingGroup`, with `ShadowOrder=0, RingOrder=1, BodyOrder=2,
    HelmetOrder=3, WeaponOrder=4` (`:265`);
  - `SetFacing` flips body, helmet and weapon (`:635`);
  - `ApplyFrame` swaps pre-resolved `Sprite[]` arrays (`:827`), with no per-frame lookups;
  - jumps (`SpawnLift`/`JoyLift`, `:242`) and the drag lift (`:252`) move the pose node. **Vertical motion is code,
    not canvas.**
- All layers of a frame are drawn on the same 40×40 canvas and registered with the same pivot. Build trims them;
  the world position is preserved.
- Mixing canvas sizes between layers is legal **if each layer's pivot is set to the same feet point**. Example:
  a 48×48 backpack layer drawn with offset (+4, +8) registers `pivot=(24.0, 4.0)`. Adding rows on **top** does not
  change pivot y; adding columns on the **left** shifts pivot x. This is what TZ §16 means by "При расширении холста
  для рюкзака сохранить совпадающие опоры всех слоёв" (when widening the canvas for the backpack, keep the anchors of
  all layers aligned).
- Free space on the 40×40 canvas:
  - left x 0–3 (the tail starts at x = 4);
  - top y 0–5 in idle, only 3 px in `stretch3`/`stretch4` (head y = 3) and `spawn1`/`joy1` (head y = 4).
  - A tall hat clips in those frames. Either shorten the hat in those frames or extend that layer's canvas upward.

### 3.2 How the rig is built (data-driven)

```python
ROLES = [...55 roles...]                    # :32-45  fur fo,f1..f4,fs,mz,mzs,ear,chk,eye,eyeh,nose,mouth,tongue | overalls bo,b1..b4,sh,shs,btn | helmet ho,h1..h5,band,lampo,glass* | pick wo,w2,po,p1..p4 | marks pa1..4,pb1..4 | helmet details hs,hr,hg,hgl,hst
KEY = {r: (3 + 4*i, 251, 7)}                # :46  off-palette "role colours"; assert not in palette
def stamp(cv, rows, legend, x, y)           # :52  ASCII → roles ('.' skip, doesn't erase)
def mask_of(cv) -> Mask                      # :61
def stamp_mask(rows, x, y) -> Mask           # :65
L_FUR :70 · HEAD 24×20 :77 · EAR_BOXES :100 · LAMP_CENTER :102 · EYES :104 · MOUTH_AT/MOUTHS :116
TORSO 15×8 :128 · TORSO_KINDS normal/tall/squash :139 · BELT_ROW {'normal':4,'tall':5,'squash':3} :145 (unused — gear leftover)
FOOT_L/FOOT_R/DANGLE/TUCK, FEET_PLACES stand/crouch/dangle/tuck :149-160 · TAILS curl/up/low/hang/wag/hang2 :162 · PAW :241
ARMS hold/raise/strike/dangle/up :244  ((anchor_x, anchor_y), rows)
class Pose(head=(8,6), torso=(13,25), torso_kind='normal', feet='stand', feet_at=(13,33), lfoot=(0,0), rfoot=(0,0),
           tail='curl', tail_at=(4,21), arms='hold', arms_dy=0, paws=((18,29),(24,27)), eyes='open', mouth='smile',
           pick=((19,31),(34,26)), pick_half=5.5, pick_behind_head=False)          # :285
def base(dy=0, dx=0, **kw) -> Pose            # :308  idle pose shifted (bob dy, paw/pick sway dx)
WALK = [(bob, lfoot, rfoot, tail) × 8]        # :317
CROUCH / LAUNCH / AIR / DRAG presets          # :322-328
POSES: Dict[str, Pose]                        # :330-417 ; assert set(POSES) == set(CAT_FRAMES) :418
class Parts: head, tail, arms, paws, feet masks   # :421  "для отметин окраса и вырезов снаряжения" (for coat markings and gear cut-outs)
def head_layer(p) :432 · feet_stamps(p) :444 · body_master(p, parts) -> Canvas :450 · marking(master, fur, p, parts) :497
HELMET 24×11 + L_HELM :525-540 · helmet_master(p) -> Canvas :543 (ear cut-outs from EAR_BOXES)
_crescent(...) :559 · pick_master(p, front, behind, scale=1.0, thick=2.8) -> Canvas :585
fur_map(fur) :622 · HELMET_RAMPS :658 (has an UNUSED 'gold' ramp) · helmet_map(colour) :671 · PICK_IRON :680
def paint(master, mapping) -> Canvas          # :683  roles → palette names
def build(reg) -> None                        # :749-782
```

Pipeline per frame (`build`, `:749`):
1. `body_master(p, parts)` draws a role-coloured master and fills the `Parts` masks.
2. Per coat: `paint(marking(...), fur_map(fur))` → `cat_body_<fur>_<frame>`.
3. `helmet_master(p)` → per colour `paint(h, helmet_map(c))` → `cat_helmet_<colour>_<frame>`.
4. Weapons: `weapons.draw(w, p.pick[0], p.pick[1], fr, phase, pick_fn)`. Then the cut mask
   (`parts.paws | behind-head`) is cleared, so the paws (in the body layer) appear over the handle →
   `cat_weapon_<w>_<frame>`.

`phase = CAT_FRAMES.index(fr)` drives the rotation of the crusher drum and the saw disc.

Gotchas:
- **Role budget:** `KEY` red = 3 + 4i must be ≤ 255, so at most **9 more roles** (55 now, max 64) before the key
  scheme must change (e.g. use the green channel). Layers that are not recoloured (like `weapons.py`) can draw
  straight in palette colours and need no roles.
- **Frame order is an API:** `CatLook` hardcodes indices (`Idle=0 … ToolReady=53, FrameCount=62`, `CatLook.cs:31-42`).
  Append new frames to the **end** of `CAT_FRAMES`, or regenerate `CatLook` (recommended: make `build.py` emit a
  `CatFrames.g.cs`). Inserting frames also shifts `phase`, which changes drum/disc rotation.
- `weapon_frames()` decides which frames get a weapon sprite. Unity loads `null` for the frames that are not used
  (`Views.cs:394-399`), so a frame without a weapon is supported already.
- `helmet_master` builds its own copy of `HEAD` for the ear cut. A hat layer must do the same.
- `pick_master`'s `behind` mask uses `mask_of(h)` of the **helmet** (`:759`). Add any new head layer to that mask.

### 3.3 Frame sets today (62 per layer; tempos from `CatView`)

| Frames | Use | Tempo | New-mode verdict |
|---|---|---|---|
| `idle0..5`, `blink`, `blink_d` | breathing, blink every 2–5 s | 0.22 s | REUSE (Idle, WaitingDepot queue) |
| `stretch0..9` | rare idle | 0.12 s | REUSE |
| `walk0..7` | walk, weapon in paws, `eyes='look_r'` | 0.09 s | REUSE = walk **without** cargo |
| `mine_ready…mine_return` (9) | swing mining; wind-up by time-to-hit, strike on `TargetHit` | sim-driven | REUSE (all pickaxes swing) |
| `tool_ready…tool_return` (9) | push tools (saw, drill, chainsaw, laser) | sim-driven | DROP unless a pickaxe item maps to a tool (§3.5) |
| `drag0..3` | carried by the player's finger | 0.11 s | DROP (no manual drag in TZ §5) |
| `spawn0..7` | out of the barrel: crouch, jump, land, look | per table | REUSE for hire at the base (spawn at `(3, Depth+1)`) |
| `joy0..5` | joy jump | per table | REUSE (delivery done, mine complete) |

### 3.4 Adding backpack, hat and boots layers (what to write)

**Python (`cats.py` + `manifest.py`):**

1. `manifest.py`: add lists, for example:
   ```python
   EQUIP_RARITIES = ['base','common','rare','epic','legendary']   # 'base' = empty slot look (TZ §10.1)
   HATS = PACKS = BOOTS = PICKS = EQUIP_RARITIES                   # or per-slot keys
   ```
   Then add `_req('cats', [f'cat_hat_{r}_{fr}' …])`, the same for `cat_pack_…` and `cat_boots_…`, plus
   `cat_load_<n>_<fr>` (see 3.6).
2. **Hat** — `hat_master(p, kind)`, cloned from `helmet_master` (`:543`):
   - stamp an ASCII hat at `p.head` (`hx, hy`);
   - cut the ears with `EAR_BOXES` (the TZ wants "открытый силуэт ушей", an open ear silhouette);
   - per rarity, a ramp map like `helmet_map`. `HELMET_RAMPS['gold']` is ready for legendary.
   - Hat shapes from `cards.card_cat_hat` (`cards.py:159`). The existing helmet `HELMET` (`:528`) can stay as the
     "base"/common look.
3. **Backpack** — `pack_master(p, parts)`:
   - draw relative to `p.torso` (`tx, ty`) and `BELT_ROW[p.torso_kind]`. The back is the LEFT side (cat faces
     right): about x = tx−7 … tx+2, y = ty−6 … ty+6;
   - clear `parts.arms` (and `parts.paws`) so front arms stay visible; let it overlap the tail base;
   - straps across the torso at the belt and shoulder rows;
   - Unity order: above the body, under the hat and weapon.
   - If it does not fit at x ≥ 0 in some frames (squash/strike poses shift the torso by +1), draw it on a wider
     canvas (§3.1 pivot rule).
4. **Boots** — `boots_master(p, parts)`:
   - recolour or redraw over `parts.feet` (`stamp_mask` of `feet_stamps(p)`), plus a 1-px cuff row on top;
   - per-rarity ramp. Feet positions repeat a lot, so the atlas dedupe makes boots almost free.
5. Register in `build()` per frame:
   `reg.add(f'cat_hat_{r}_{fr}', paint(hat, hat_map(r)), pivot=PIVOT)`, the same for the others (`pivot=PIVOT`
   or the adjusted pivot for a wider canvas).
6. `overview.cat()`: add the layers to the composition.

**Unity (`CatLook.cs`, CatView in `Views.cs`):**
- `CatLook`: name tables like `s_Helmet` (`:69-81`) for hat, pack, boots and load. The rarity index comes from the
  equipped team set, not from the level.
- `Coat(int catId)` (`:84`) stays.
- REMOVE `Weapon(level)`, `HelmetColor(level)` and `Cycle`.
- CatView: new `SpriteRenderer`s via `PixelWorld.Sprite(pose, name, sprite, Vector2.zero, order)` (`PixelWorld.cs:61`)
  with renumbered orders, for example:
  `Shadow 0 · Ring 1 · Body 2 · Boots 3 · Pack 4 · Load 5 · Hat 6 · Weapon 7`
  (the weapon stays on top: paws are already cut out of it).
- Extend `SetFacing` (`:635`), `ApplyFrame` (`:827`) and the per-variant `Sprite[]` arrays. Today they are filled in
  `ApplyLevelVisual` (`:384`); in the new mode use "ApplyEquipmentVisual" (team-wide, on equip change).
- Tests to update: `VisualPolishTests.cs:107-150, 309-312` (asserts 8 weapons, 8 helmet colours, FrameCount and the
  level→weapon cycle).

### 3.5 Weapons / pickaxe items

`weapons.py` API:

```python
CW, CH = 40, 40 :24 · WEAPONS :27 · TOOLS :29 · FIRE_FRAMES {'tool_strike','tool_impact'} :31
STEEL, STEEL_LIGHT, DARK_STEEL, WOOD, RUBBER, RED, YELLOW, ORANGE, WHITE  (base, light, dark) :34-42 · OUTLINE='ink1'
class Frame(butt, top): st(x, y) -> (s along axis, t "up")      # :46
def rect(s0,s1,t0,t1) / disc(sc,tc,r) -> Shape                   # :65,:69
def mask_of_shape(fr, shape) -> Mask :73 · paint_part(cv, m, ramp) :83 · assemble(parts, outline) -> Canvas :97 · tool_length :105
hammer :111 · crusher :120 · saw :141 · drill :171 · chainsaw :191 · laser :209 · sparks :234 · dust :247 · beam :258
def draw(name, butt, top, frame, phase, pick_fn) -> Canvas        # :276
```

TZ §10.1: the "Кирка" (pickaxe) slot has 4 rarities, and "старый цикл восьми инструментов не определяет внешний вид"
(the old 8-tool cycle no longer defines the look). Cheapest option: reuse `pick_master(p, …, scale, thick)` (`:585`)
with new palette maps beside `PICK_IRON` (`:680`), for example:
- steel/blue for rare;
- purple crystal head for epic;
- gold head with `scale≈1.15` for legendary.

All of them use the `mine_*` swing frames, so the `tool_*` frames and their 9×N sprites can be dropped.
If designers want a tool look (e.g. a legendary drill), map that item to an existing `weapons.draw` name and keep its
`tool_*` frames and `weapon_*` sound. The visual catalogue must then record "item → pose + sound" (TZ §16).

### 3.6 Carry-bag walk and the other new states (TZ §16)

| TZ state | Frames | How |
|---|---|---|
| walk with full bag | **new `carry0..7`** | `POSES[f'carry{i}'] = base(b, dx=lf[0], lfoot=lf, rfoot=rf, tail=t, arms='carry', paws=…, eyes='focus', mouth='grit')` using `WALK`. Add `ARMS['carry']` (both paws gripping a shoulder strap). Optionally a heavier bob: `b+1` on the contact frames. Weapon: either skip these frames in `weapon_frames()` (the Unity weapon renderer gets `null`), or stow the pick diagonally across the back (`pick=((x,y),(x,y))`). |
| bag fill visible | overlay `cat_load_<n>_<frame>`, n = 1..3 (partial / full / overflow) | Drawn relative to `p.torso` on the back, independent of the pack rarity (avoids 5×4×F combinations). Uses `sack()` styling (`env.py:767`: `wood1`/`wood4`/`cream0..2`), with coins peeking out (`gold3..5`). It must exist for **all** frames the cat can show while loaded (walk/carry, idle in the queue, pickup), or CatView hides it in the others. |
| pickup | new `pickup0..3` | crouch (`CROUCH` preset) + paws low and forward + back up; or reuse `mine_antic` + `idle0`. |
| unload at depot | new `unload0..3` | raise the bag (`LAUNCH`-like `arms='up'`), drop it forward, then `joy5`/`idle`. The sim owns the timing (TZ §7.3: credit is never driven by an Animation Event). |
| turn to base | none | `flipX` (SetFacing) |
| wait in queue | `idle*` + `blink*` | reuse |

Atlas budget estimate (F ≈ 62 − 9 tool − 4 drag + 8 carry + 4 pickup + 4 unload = 65):
- body 6×65;
- hat, pack, boots and pick 5×65 each;
- load 3×65.

That is about 1,800 cat names. With dedupe, boots, load and parts of the hats collapse heavily. Expect the atlas to grow
to 2048×2048. Verify with `manifest.json`.

---

## 4. Unity atlas consumers touched by this subsystem

| Type | API (exact) | Verdict |
|---|---|---|
| `PixelSpriteDef` (`Pixel/PixelArt.cs:8`) | `PixelSpriteDef(string name, int x, int y, int w, int h, float pivotX, float pivotY, int l, int b, int r, int t)` | REUSE |
| `PixelArt` (`Pixel/PixelArt.cs:37`) | `const int CellPixels = 32; const float UiPixel = 4f; static void Init(Material spriteTemplate, Material meshTemplate); static bool Loaded; static Texture2D Atlas; static bool Has(string); static bool TryGetDef(string, out PixelSpriteDef); static Sprite World(string); static Sprite Ui(string); static Rect Uv(string); static Material SpriteMaterial; static Material MeshMaterial` | REUSE. A missing name gives a pink placeholder plus **one** `LogWarning` (not an error). The base64 decode and `LoadImage` happen once on first access, a startup cost that grows with the atlas. |
| `PixelAtlasData` (`.g.cs`, partial) | `const int Width, Height; const string Hash; static readonly PixelSpriteDef[] Sprites; const string Png` | REUSE (generated) |
| `PixelPalette` (`.g.cs`) | `public static readonly Color <Ramp><i>` | REUSE (generated) |
| `PixelTheme` (`Pixel/PixelTheme.cs:12`) | `static EnvironmentTheme Current; static string Suffix(EnvironmentTheme); static string Name(string sprite); static string[] Names(string[] frames); static string CrystalGlow; static Color Dark` | REUSE |
| `CatLook` (`Visual/CatLook.cs:14`) | `Coats, Weapons, HelmetColors, Cycle, frame consts, Frames, Coat(int), Weapon(int level), HelmetColor(int level), IsTool(int), ForWeapon(int,int), Body(int,int), Helmet(int,int), WeaponSprite(int,int)` | ADAPT. Keep `Coats`/`Coat`/`Frames`/name tables. DROP the level-based `Weapon`/`HelmetColor`/`Cycle`. Add equipment name tables and the new frame indices. |

---

## 5. Audio runtime (`Assets/Game/Scripts/Runtime/Audio`)

### 5.1 `Sfx.cs` (451 lines) — one-shot cue pool. **ADAPT** (cue set and triggers)

```csharp
const int Voices = 6;  const int SynthRate = 22050;                         // :18-19
const int PrioFrequent = 0, PrioNormal = 1, PrioJingle = 2;                 // :22
sealed class Cue { AudioClip[] Clips; float Volume, PitchJitter, MinInterval; float Pitch = 1f; int Priority;
                   float Last; int LastVariant; int Voice, Frame; }          // :25-36
public bool Enabled { get; set; }                                            // :59  false also stops the playing voices
public static Sfx Create(Transform parent)                                   // :71  6 AudioSources, all Load(...), UiSound hookup
public void Hit(); Hit(HitMaterial material); Hit(int weapon, HitMaterial material)   // :142,:148,:163
public void Select(); Break(); Coin(); Chime(); Payout(); Meow(); Merge(); Upgrade(); Unlock(); ChestOpen();
       GateOpen(); ExitOpen(); Dynamite(); LootDrop(); Cannon(); BarrelLand(); Click(); Open(); Close(); Denied();  // :170-213
void PlayJingle(Cue c, int level)      // :215  levels 1 (unlock) / 2 (gate, exit); suppresses rewards for 0.8×clip
void StopReward(Cue c)                 // :233  stops m_Upgrade/m_Chest started in the same frame before the jingle
AudioClip Play(Cue c)                  // :241  interval gate → PickVoice → random non-repeating variant → volume×AudioMix.SfxGain, pitch jitter
AudioSource PickVoice(int priority)    // :265  free voice, else the oldest voice with priority ≤ new
static Cue Load(string name, float volume, float pitchJitter, float minInterval, int priority)  // :292  Resources "Audio/<name>_0..7" or "Audio/<name>"; LoadAudioData() eagerly; none → LogWarning + Fallback(name)
static Cue Variant(Cue source, float volume, float pitch, float pitchJitter, float minInterval, int priority) // :324  new cue on existing clips (no files)
static AudioClip Fallback(string name) // :342  pentatonic marimba/bell plucks + thumps; unknown name → default C5 pluck
```

**Existing cue names** (Resources file prefixes; volume / jitter / interval / prio from `Create :87-121`):

| Cue | Vol | Jit | Int | Prio | New-mode use |
|---|---|---|---|---|---|
| `hit` (×4) / `hit_crystal` (×3) / `hit_wood` (×3) / `hit_metal` (×2) | .22/.20/.24/.20 | .04 | .10 | F | REUSE (mining; material from `PresentationCatalog.HitMaterialOf`) |
| `weapon_hammer, _crusher, _saw, _drill, _dualpick, _chainsaw, _laser` (×2 each) | .15–.26 | .03–.04 | .10–.16 | F | ADAPT: index by pickaxe item, not `CatLook.Weapon(level)`; DROP if all items are picks |
| `break` (×3) | .42 | .05 | .08 | F | REUSE (object destroyed / rubble cleared) |
| `coin` (×3) | .30 | .03 | .09 | F | REUSE (per-delivery credit) |
| `chime` (×2) | .30 | .03 | .12 | F | elixir/gems are gone → REUSE as the equip/item chime, or DROP |
| `payout` (×2) | .50 | .03 | .15 | N | REUSE → "bag delivered" |
| `meow` (×4) | .50 | .05 | .15 | N | REUSE → hire |
| `merge` | .60 | .02 | .10 | N | DROP |
| `upgrade` | .50 | 0 | .12 | N | REUSE (base upgrade purchase) |
| `unlock` | .55 | 0 | .50 | J1 | REUSE (threshold / sector) |
| `chest` | .55 | 0 | .50 | N | DROP, or reuse for "item reward" on mine completion |
| `gate` | .65 | 0 | .50 | J2 | REUSE (sector gate opened) |
| `exit` | .70 | 0 | .50 | J2 | REUSE (mine complete) |
| `click`, `open`, `close`, `denied`, `select` | .35/.30/.28/.38/.28 | ≤.03 | .05–.20 | N | REUSE |
| `dynamite`, `cannon`, `land` | .60/.50/.42 | | | N | DROP |
| `LootDrop` = `Variant(coin, 0.14, pitch 0.8, 0.06, 0.1, F)` | | | | F | REUSE → "bag dropped on ground" |

**Adding a new cue** (`SOUNDS.md:156-157`, verified against the code):
1. Add a field (`Cue m_Step;`) near `:51-56`.
2. In `Create` add `s.m_Step = Load("step", vol, jitter, interval, PrioFrequent);`, or use
   `Variant(s.m_Coin, …)` if no new file is needed.
3. Add a public method `public void Step() => Play(m_Step);`. If it can coincide with a mine-complete or gate jingle,
   add it to the `StopReward` list in `PlayJingle :222-223` and to the `m_JingleUntil` guard, the way `Upgrade` and
   `ChestOpen` do (`:184-196`).
4. Add a `case "step":` in `Fallback :344`, otherwise the file-less build plays the default pluck and logs a warning.
5. Add a recipe `"step_0": r_step(0), …` in `Docs/Audio/build_sfx.py` `RECIPES` (`:409`); build, transfer, unpack (§1).
6. Add the call site in the new presentation dispatcher. Today all calls are in `GameController.Present(SimEvent)`
   (`GameController.cs:576-698`, a `SimEventType` switch). That is the only old-core coupling of the cues.
7. Add a row to the `SOUNDS.md` table and `CREDITS.md`.

Proposed new cues (TZ §16: step, pickup, bag, depot, unload, hire, equip; "12 cats must not make continuous noise"):

| Cue | Trigger | Source idea | Vol | Jit / Int | Prio |
|---|---|---|---|---|---|
| `step` | walk frame contact (2 per cycle), **only for on-screen cats** | Kenney RPG `footstep0x` / Impact `footstep_concrete_00x` (names to verify after download), LP 3 kHz, about 0.08 s | 0.06–0.10 | ±6% / 0.15–0.2 s | F |
| `pickup` | `PickingUp` unit taken | `Variant(m_Coin, 0.16, 1.15, .05, .1, F)` or RPG `handleSmallLeather` | 0.16 | / 0.10 | F |
| `bag_full` | bag reached capacity → Returning | RPG `clothBelt`/`beltHandle` + soft coin | 0.22 | / 0.30 | F |
| `depot` | arrived at the depot slot / queue | `land`-style wood knock (`Variant(m_Land…)` if `land` is kept) | 0.20 | / 0.20 | F |
| `unload` | each portion serviced | `Variant(m_Coin, 0.12, 0.9, .03, .12, F)` | 0.12 | / 0.12 | F |
| `deliver` | the bag is fully unloaded (gold credited) | reuse the `payout` clips | 0.40 | / 0.25 | N |
| `hire` | team upgrade spawns a cat | `Meow()` + `Click()`, or a new short jingle | — | — | N |
| `equip` | item equipped / "Надеть лучшее" (equip best) | RPG `metalLatch` + `chime` | 0.40 | / 0.20 | N |

Gotchas:
- The 6 voices are shared by everything. Per-cue `MinInterval` is the **only** rate limit; there is no global per-frame
  budget. `PrioFrequent` cues never steal Normal/Jingle voices (good). With 12 cats, add a global frequent-cue cap
  (e.g. ≤ 3 frequent starts per 100 ms) or route steps through a separate 1–2-voice pool.
- Intervals and jingle windows use `Time.unscaledTime`. Simulation speed-ups (×100 debug) do not multiply SFX.
- `AudioMix.SfxGain` is applied at `Play` time only (short clips, so this is fine).
- All clips are decoded at `Create` (about 1 MB PCM). New cues add to startup time.
- The `HitMaterial` enum lives in `Runtime/PresentationCatalog.cs:15`. `HitMaterialOf(BalanceRepository, ZoneObject)`
  (`:261`) depends on the old `ZoneObject`/`RockVisualKind`. ADAPT the caller, not `Sfx`.
- Offline and long-gap events are not voiced: `GameController` strips them (`:454-458`). Keep that rule for the new
  offline catch-up (TZ §13).

### 5.2 `MusicPlayer.cs` (129 lines) — **REUSE**

```csharp
public const int Warm = 0, Crystal = 1;  s_Names = {"Audio/music_warm","Audio/music_crystal"}   // :14-15
const float CrossfadeSeconds = 3f, FirstFadeSeconds = 2f; const int OwnLoops = 2;              // :16-19
public int Current { get; }                                  // :31  −1 = silent (files missing → silent, no warning)
public static MusicPlayer Create(Transform parent)           // :33  2 looping AudioSources, subscribes AudioMix.Changed
public void SetPreferred(int track)                          // :59  same track keeps playing; while a guest track plays the call is deferred
```

Loop handling:
- The seamless loop is baked into the file: `music.py` `fold_loop()` (`:264`) folds the reverb tail into the start.
- Playback uses `AudioSource.loop = true` (`:49`).
- Track rotation: after `OwnLoops=2` cycles of the preferred track, one "guest" cycle of the other track plays, then
  it returns. The crossfade starts `CrossfadeSeconds` before the cycle end (`:113-119`). `m_Played` is an unscaled
  timer that is frozen while `AudioListener.pause` is set.

Tracks:
- `music_warm`, «Тёплая штольня» (Warm Adit): 82 BPM, F major, 48 bars, loop 2:20.5.
- `music_crystal`, «Кристальная глубина» (Crystal Depth): 72 BPM, D Lydian, 44 bars, loop 2:26.7.
- Import: Streaming, Vorbis q0.6. Masters are in `Docs/Audio/masters/` (PC only).

The caller couples to the old core: `GameController.cs:376`
`Music.SetPreferred(PresentationCatalog.MusicOf(PresentationCatalog.ThemeOf(Session.Mine.Zone)))`. Adapt that to the
new session object.

Hard limit: exactly 2 tracks (`1 - m_Current`, `new AudioClip[2]`). A third track needs code changes.
The fade advances while paused (cosmetic only).

### 5.3 `Ambience.cs` (106 lines) — **REUSE**

```csharp
const float BedVolume = 0.8f; const float MinGap = 5f, MaxGap = 14f;                                  // :13-14
s_Events = {("Audio/amb_drip_",4,.45,.7), ("Audio/amb_creak_",3,.25,.55), ("Audio/amb_pebbles_",2,.15,.5), ("Audio/amb_machine_",2,.15,.55)} // :16-22
public static Ambience Create(Transform parent)   // :30  looping bed "Audio/amb_bed" + 2 alternating one-shot sources
```

- Random pan ±0.75, pitch ±6%, volume 75–100%; no immediate repeat.
- No old-core coupling. To add an event type, append a tuple (the weights must sum to ≤ 1).

### 5.4 `AudioMix.cs` (62 lines) — **ADAPT** (one dependency)

```csharp
public static float Master=1, Music=.6f, Sfx=1, Ambience=.7f { get; private set; }  public static bool Muted   // :15-19
public static event Action Changed;                                                                         // :22
public static float MusicGain / SfxGain / AmbienceGain  (Muted ? 0 : Master*group)                          // :24-26
public static void Apply(PlayerSettingsState s)          // :28  ← CatMiners.Core.Sim (Core/Sim/GameState.cs:73)
public static void Set(bool muted, float master, float music, float sfx, float ambience)   // :34  core-free
public static float Get(AudioGroup group)                // :49  ← Core/Sim/GameState.cs:60
```

`PlayerSettingsState` (`SoundOn, VibrationOn, MasterVolume, MusicVolume, SfxVolume, AmbienceVolume, ReduceEffects`)
and `AudioGroup` are **settings, not gameplay**. Move them as-is to the new profile/settings model, or call `Set(...)`
directly. Callers: `GameController.cs:367, 369, 385, 1032`. Pause handling: `GameController.OnApplicationPause`
(`:785-788`) sets `AudioListener.pause`.

### 5.5 `UiSound.cs` (25 lines) — **REUSE**

`public static Action Click, Open, Close, Denied; PlayClick(), PlayOpen(), PlayClose(), PlayDenied()`. `Sfx.Create`
binds them; `OnDestroy` unbinds only its own. Callers in the kit: `PixelButtonPress.cs:64`, `PixelSlider.cs:76`,
`HudBlueprints.cs:220,226,412`, `PixelUi`. With no binding they are silent (tests, tools).

---

## 6. Audio tools

| File | Purpose / API | Verdict |
|---|---|---|
| `Docs/Audio/build_sfx.py` (note: it is in **Docs**, not Tools) | `load(src, rel, start=None, end=None) :37, hp :51, lp :55, pitch(x, semitones) :59, strip_lead :69, shape(x, length, fade_from, fade_in_ms) :76, mix(*layers) :96, active_rms_db :106, normalize(x, rms_db=-18, peak_db=-1) :113, bp :300, _env :304, _knock :312`; recipes `r_*` :126-405; `RECIPES = {"<cue>_<n>": fn}` :409; `TARGET_RMS = -18` :444. CLI: `Tools/.venv/bin/python Docs/Audio/build_sfx.py --src Temp/audio/src --out Temp/audio/out [names…]` → OGG Vorbis mono 44.1 kHz | REUSE; add new `r_*` recipes. Pack directories in `--src`: `impact-sounds/Audio`, `rpg-audio/Audio`, `music-jingles/Audio`, `rd-rpg`, `rd-break`, `spring`, `meow`. |
| `Tools/Audio/music.py` (597) | Synth instruments (`marimba, pluck, celesta, pad, glass, bell, bass, shaker, kick, rim, drop`), `Score(bpm, bars, seed).add/melody/render`, `fold_loop`, `master(x, -17, -1)`, `analyse` (prints the loop-seam jump), `TRACKS` :545. CLI: `Tools/.venv/bin/python Tools/Audio/music.py --out Temp/audio/music [warm] [crystal]` → `music_*.ogg` (q 0.45) + `_master.ogg`; deterministic | REUSE as-is (TZ §16 keeps both tracks). `write_ogg` writes in 32768-frame blocks because libsndfile 1.2 crashes on one big write. |
| `Tools/Audio/ambience.py` (198) | `bed(seed, 45 s, xfade 5)`, `creak`, `drip`, `machine`, `pebbles`, `norm(-22 dB)`. CLI `--out Temp/audio/amb` | REUSE |
| `Tools/Audio/unpack_transfer.ps1` | PC side: `.b64` → tar → `-Dest`, prints sha256 and the file count | REUSE |

Licence rule: CC0 only. CC-BY is acceptable only with an author line in `CREDITS.md`. NC, ND, GPL or unknown licences
are forbidden (`SOUNDS.md:159`).

---

## 7. Docs (skimmed) — what is stale for the new mode

- `Docs/Art/PIXEL_STYLE_GUIDE.md`:
  - §1–3 (scale, palette, light) and §9 ("how to add") still hold.
  - §4 "Отличия" (differences: merge zone, cannon, forge 2×3) and the size table are old-mode.
  - The cat row in the size table says `cat_pick_*`, which is **stale**: the real name is `cat_weapon_*`.
  - §5 cats: levels and the weapon cycle are old-mode.
  - §7 has cannon and barrel timings.
- `Docs/Visual/ART_DIRECTION.md`:
  - §4 "Прокачка" (progression: weapon cycle, helmet per cycle) and the merge text are old-mode.
  - §1–3 and §6 (three themes via `Resources/Visual/environment_themes.json`) stay.
  - §7 effects table: merge and hire-by-cannon are old-mode.
- `Docs/Visual/VISUAL_PROGRESS.md`:
  - The journal records the removed gear layer (`cat_gear_*`, 6 tiers at levels 1, 3, 6, 10, 15, 22), replaced on
    8 Oct by the weapon cycle (`:96-103, :151-160`).
  - Open items: sound not heard by a human, APK not built (no Android Build Support), no device test.
- `Docs/Visual/VISUAL_QA.md`:
  - Perf baseline in the editor: median frame 2.0–2.5 ms, 0 sim allocations.
  - Atlas cost: about 8 MB VRAM.
  - Tests to keep or adapt: `VisualPolishTests` (6), `VisualShowcaseTests` (zones × 3 aspects, "no missing sprites"),
    `VisualAssetCatalogTests` (writes `VISUAL_ASSET_CATALOG.csv`).
- `Docs/Visual/VISUAL_ASSET_CATALOG.csv`:
  - Generated by `VisualAssetCatalogTests`. Columns: `category, source_id, uses_on_maps, display_name, final_sprites,
    states_and_variants, themed, notes`.
  - TZ §16 additionally wants path, ID, import, author/licence and verification status, plus entries for base
    buildings, items and cargo. Extend the test, not the CSV.
- `Docs/Audio/SOUNDS.md`: the event table is keyed by old `SimEventType` names; rewrite the "Событие" (event) column
  for the new events.

---

## 8. Checklist: art and audio work for the new mode

1. **Pipeline:**
   - remove `cards` from `MODULES` and its `_req` entries;
   - remove the merge, cannon, barrel, crate, dynamite and chest `_req` entries and drawings;
   - run `build.py --only <m>` per module while iterating;
   - run the full build once at the end.
2. **Cats:**
   - append the `carry*`, `pickup*` and `unload*` frames;
   - add the hat, pack, boots and load layers with per-rarity maps (including `base`);
   - add pickaxe rarity maps;
   - drop the `tool_*`/`drag*` frames only together with the `CatLook` index rewrite;
   - update `overview.py` and the tests.
3. **World:**
   - `base.py`: depot with 2 unload slots and coin crates, hire board, tool bench, bag rack, wardrobe (theme-able);
   - cargo bags on the ground (1 / heap / big heap);
   - damage stages for `block_*`/`waterfall_*`;
   - queue markers if needed.
4. **UI and icons:**
   - 16 item icons and 4 slot icons;
   - "gold in transit" vs wallet indicator icons;
   - reuse the rarity frames.
5. **Audio:**
   - add the new cues (§5.1) with interval and voice caps;
   - remap the cue triggers from `SimEventType` to the new events;
   - move `PlayerSettingsState`/`AudioGroup` out of the old `GameState`;
   - keep `MusicPlayer` and `Ambience`;
   - do a listening pass on the PC.
