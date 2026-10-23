# Reuse map: Visual + Pixel (Runtime/Visual/*.cs, Runtime/Pixel/*.cs)

Scope: `Assets/Game/Scripts/Runtime/Visual/` (11 files) and `Assets/Game/Scripts/Runtime/Pixel/` (5 files).
Paths below are relative to `Assets/Game/Scripts/Runtime/`. Line numbers are from the snapshot of 2026-10-08.
Target mode: TZ v2.1 (`Docs/TZ/Cat_Miners_Auto_Incremental_TZ_v2.1.md`): auto-mining, cargo bags, base depot below the map,
no merge, drag, cards, containers, cannon or generators.

---

## 0. TL;DR

| Type | File | Verdict |
|---|---|---|
| `PixelArt`, `PixelSpriteDef` | Pixel/PixelArt.cs | **REUSE AS-IS** |
| `PixelWorld` | Pixel/PixelWorld.cs | **REUSE AS-IS** |
| `PixelTheme` | Pixel/PixelTheme.cs | **REUSE AS-IS** |
| `PixelAtlasData` / `PixelPalette` (generated) | Pixel/*.g.cs | **REUSE AS-IS** (regenerate with new art) |
| `EnvironmentThemes` | Visual/EnvironmentThemes.cs | **REUSE AS-IS** |
| `Effects` | Visual/Effects.cs | **REUSE AS-IS** (small fix optional) |
| `FloatingText` | Visual/FloatingText.cs | **REUSE AS-IS** |
| `Label3D`, `HpBar` | Visual/Views.cs | **REUSE AS-IS** |
| `Pickable` | Visual/Views.cs | **ADAPT** (new `PickKind` set) |
| `LevelBadge` | Visual/LevelBadge.cs | **ADAPT / optional** (no per-cat level; can become a counter plate) |
| `CatLook` | Visual/CatLook.cs | **ADAPT** (level to equipment mapping, new layers and frames) |
| `CatView` | Visual/Views.cs | **ADAPT heavily** (keep the frame machine, drop merge/drag, add carry/path/queue) |
| `ObjectView` | Visual/ObjectView.cs | **ADAPT heavily** (HP and damage for all `r/s/x/p`, drop shaft-generator, bubble, ETA) |
| `LootView` | Visual/LootView.cs | **ADAPT** into a pooled cargo-bag view (not tappable) |
| `BoardView` | Visual/BoardView.cs | **ADAPT / split**: keep floor, decor, fog and coordinates; rewrite Sync and OnEvent; drop merge, drag, containers, cannon |
| `ContainerView` | Visual/Views.cs | **DROP-FOR-NEW-MODE** |
| `CannonView` | Visual/CannonView.cs | **DROP-FOR-NEW-MODE** (art can become base decor) |
| `Palette` | Visual/VisualKit.cs | **REUSE** (dead members `Fur`, `Helmet`, tent colours can go) |
| `Materials`, `Meshes` | Visual/VisualKit.cs | **DROP from gameplay** (only `DevTools/DevPanel.cs:856-860` uses them) |

Only `BoardView` and `ObjectView` take `GameSession` directly. `CatView`, `LootView` and `ContainerView` take Core
state DTOs (`CatState`, `LootState`, `ContainerState`). Everything else is free of the simulation.

---

## 1. Coordinate system (shared by every view)

- **Scale:** `PixelArt.CellPixels = 32` (Pixel/PixelArt.cs:40). 1 world unit is 1 grid cell, which is 32 art pixels. World sprites use PPU 32.
- **Plane:** the board lies in XZ at `y = 0`. The camera is orthographic and looks straight down (`CameraRig.Pitch = 90f`, CameraRig.cs:17).
  World **+Z is "up the screen"**. World +X is right.
- **Origin:** world `(0,0,0)` is the **bottom-left corner of the map grid**: the left edge of column 0 and the bottom edge of row `Depth-1`.
  Row 0 is at the top, furthest from the base.
- **Cell to world** (`BoardView.CellCenter`, Visual/BoardView.cs:139):
  `public Vector3 CellCenter(int cell) => new Vector3(m_L.Col(cell) + 0.5f, 0f, m_L.Depth - m_L.Row(cell) - 0.5f);`
- **Continuous grid** (Visual/BoardView.cs:140-141):
  `public Vector3 GridToWorld(double gx, double gy) => new Vector3((float)gx, 0f, (float)(m_L.Depth - gy));`
  `public Vector2 WorldToGrid(Vector3 world) => new Vector2(world.x, m_L.Depth - world.z);` To get the cell, apply `FloorToInt` to both values.
- **Object footprint center** (Visual/BoardView.cs:147-153): `x = Col + W/2`, `z = Depth - Row + D/2 - 1`. The anchor `Row` is the
  **bottom** row of the footprint, which extends to `Row - D + 1`. This matches TZ 8.1 `occupied = (col+dx, row-dz)`.
- **Object root ("feet")** (Visual/ObjectView.cs:147-148): `(Col + W/2, 0, Depth - Row - 1)`, the bottom edge of the anchor row.
- **Feet offsets inside a cell:** cat `FeetY = -10 px` (Views.cs:215), loot pile `FeetY = -9 px` (LootView.cs:22),
  container `FeetY = -0.5` (Views.cs:892).
- **Base pad (TZ 8.3, rows `Depth..Depth+2`):** the formula extends naturally to `z = 0..-3`, and `Row(cell) = cell / Width` works for
  indices `>= Width*Depth`. However, `TileName` paints `wall_bottom` for every `r >= depth` (BoardView.cs:360-366), so the pad must get
  `floor_*` tiles there. `BottomRows = 8` (BoardView.cs:21) already makes the mesh cover the pad. The spawn point `(3, Depth+1)` maps to
  world `(3.5, 0, -1.5)`.
- **Visual node** (`PixelWorld.Visual`, PixelWorld.cs:39): a child rotated by `Euler(90,0,0)`. Inside it, local +X is right and
  **local +Y is world +Z**. All sprite positions passed to `PixelWorld.Sprite(..., Vector2 pos, ...)` are `(x, z)` offsets in cells.
- **Pixel snap:** `PixelWorld.Snap(float)` and `Snap(Vector3)` round x and z to 1/32 (PixelWorld.cs:96-98). Every view snaps its visual node.
  Roots and colliders move smoothly.
- **Sorting** (PixelWorld.cs:19-36). The engine draws lower-on-screen sprites on top.

  | Constant | Value | Use |
  |---|---|---|
  | `OrderAt(z)` | `clamp(round(-z*16), ±20000)` | `OrderPerCell = 16` |
  | `FloorOrder` | -32000 | floor and wall mesh |
  | `FloorDecalOrder` | -31000 | slot markers |
  | `FogOrder` | 24000 | sector fog |
  | `FxOrder` | 25000 | particles |
  | `OverlayBase` | 28000 | overlays |

  `OverlayOrderAt(z) = 28000 + clamp(round(-z*4), ±4000)*4` is used for HP bars, badges, labels and bubbles.
  Flying items use `FogOrder + 1` (over fog, under fx).
- **Gotcha:** `BoardView` must sit at the world origin. `CellCenter` returns board-local values, but callers assign them to
  `transform.position` (BoardView.cs:1012, 1019). Glints use `m_Root.position` (BoardView.cs:482).

---

## 2. Pixel/ (render foundation): REUSE AS-IS

### Pixel/PixelArt.cs: atlas loader and sprite factory
The atlas PNG is embedded as base64 in `PixelAtlasData.Png`. It is decoded once with Point filtering. Sprites are created lazily and cached.
An unknown name returns a pink checker sprite and logs one warning per name.
- `public readonly struct PixelSpriteDef(string name, int x, int y, int w, int h, float pivotX, float pivotY, int l, int b, int r, int t)` (:8-29).
  The pivot is in pixels from the bottom-left of the **trimmed** rect and can be negative (an overlay sitting above the feet).
- `public const int CellPixels = 32;` and `public const float UiPixel = 4f;` (:40-42)
- `public static void Init(Material spriteTemplate, Material meshTemplate)` (:59). Call it before any sprite is created.
  It is called in `GameController.cs:117`.
- `public static bool Loaded` (:69), `public static Texture2D Atlas` (:78), `public static bool Has(string name)` (:87)
- `public static bool TryGetDef(string name, out PixelSpriteDef def)` (:94)
- `public static Sprite World(string name)` (:103): PPU 32. `public static Sprite Ui(string name)` (:106): PPU 25 for uGUI.
- `public static Rect Uv(string name)` (:109): normalized UVs for mesh tiles.
- `public static Material SpriteMaterial` (:125) and `public static Material MeshMaterial` (:145).
  Gotcha: in Unity 6 the sprite shader on a `MeshRenderer` reads `unity_SpriteColor`, so meshes must use `MeshMaterial` (URP Unlit).
- Gotcha: without scene templates, it falls back to `Shader.Find`, which can return null in player builds when the shader was stripped.
  Pass templates via `Init`.

### Pixel/PixelWorld.cs: world sprite helpers and sorting constants
- `public static readonly Quaternion FaceCamera = Quaternion.Euler(90f, 0f, 0f);` (:16)
- `OrderPerCell=16, FloorOrder=-32000, FloorDecalOrder=-31000, FogOrder=24000, FxOrder=25000, OverlayBase=28000` (:19-30)
- `public static int OrderAt(float worldZ)` (:33) and `public static int OverlayOrderAt(float worldZ)` (:36)
- `public static Transform Visual(Transform parent, string name = "visual")` (:39)
- `public static SortingGroup Group(GameObject root, float worldZ)` (:49): a SortingGroup on the root, ordered by the feet z.
- `public static SpriteRenderer Sprite(Transform visual, string name, string sprite, Vector2 pos, int order, Color? color = null)` (:61)
- `public static SpriteRenderer Sliced(Transform visual, string name, string sprite, Vector2 pos, Vector2 sizeCells, int order, Color? color = null)` (:79)
- `public static void SetSprite(SpriteRenderer sr, string sprite)` (:88): swaps the sprite only when it changes. It does a dictionary lookup per call.
- `public static float Snap(float v)` and `public static Vector3 Snap(Vector3 v)` (:96-98)

### Pixel/PixelTheme.cs: per-zone environment variant names
- `public static EnvironmentTheme Current { get; set; }` (:23). This is **global static** state, set by `BoardView.Build` (BoardView.cs:159).
  Set it before resolving any themed name.
- `public static string Suffix(EnvironmentTheme)` (:25), `public static string Name(string sprite)` (:29): returns `name_crystal` or
  `name_copper` if that sprite exists, otherwise the base name.
- `public static string[] Names(string[] frames)` (:44). Gotcha: the cache is keyed by **array reference**, so pass `static readonly` arrays.
- `public static string CrystalGlow` (:59), `public static Color Dark` (:63): camera background and fog colour.
- `EnvironmentTheme { Warm, Crystal, Copper }` is defined in `PresentationCatalog.cs:21`.

### Visual/EnvironmentThemes.cs: zone to theme table
Reads `Resources/Visual/environment_themes.json` and caches it in static `s_Table`. The fallback is 3 Warm, 3 Crystal, 3 Copper, repeating.
- `public static EnvironmentTheme Of(int zone)` (:40) and `public static EnvironmentTheme[] Parse(string json, out string error)` (:65).
- It is called through `PresentationCatalog.ThemeOf(zone)` (PresentationCatalog.cs:284). TZ 16 requires keeping all three themes. **REUSE AS-IS.**

### Pixel/PixelAtlasData.g.cs: generated, do not edit
- `public static partial class PixelAtlasData { public const int Width = 2048; public const int Height = 1024; public const string Hash; public static readonly PixelSpriteDef[] Sprites; public const string Png; }`
- The file has 1821 sprites and is 541 KB. It is generated by `Tools/PixelArt/build.py` from `Tools/PixelArt/sprites/*.py` (`manifest.py` lists the requirements).
  Gotcha: new art (backpack, depot, base buildings) goes through this Python pipeline. Regenerate the `.g.cs` and do not hand-edit it.
  Because the PNG is embedded in C#, the atlas syncs as code.

### Pixel/PixelPalette.g.cs: generated palette
- `public static class PixelPalette` with `public static readonly Color` ramps from dark to light:
  `Ink0-3, Navy0-8, Slate0-7, Stone0-7, Wood0-7, Amber0-5, Gold0-5, Purple0-6, Teal0-6, Blue0-7, Red0-6, Green0-6, Pink0-4, Cream0-4, Cave0-8, Frost0-7, Copper0-8, Rust0-7, FurGinger0-4, FurGray0-4, FurBlack0-4, FurWhite0-4, FurCream0-4, FurBrown0-4`.
- It is generated by `Tools/PixelArt/gen_palette.py` from `pxl.py` RAMPS.

### Atlas sprite keys by category
Theme variants (`*_crystal`, `*_copper`) exist for all floor and wall tiles, `block_*`, `waterfall*`, `beam_post`, `crystal_cluster_a/b`,
`exit_base`, `exit_boards`, `shaft_base`, `shaft_wheel_0..3` and `sign_question`.

- **Cats** (1292 sprites, 3 layers, all with feet pivot `(16,0)` on a trimmed rect; source canvas 40×40 with feet at (20,4); body 27×29):
  - `cat_body_{ginger|gray|black|white|cream|brown}_{F}`: 6 coats × 62 frames.
  - `cat_helmet_{yellow|blue|red|green|purple|teal|pink|white}_{F}`: 8 colours × 62 frames. The pivot y is negative, so the helmet sits above the feet.
  - `cat_weapon_{pick|hammer|crusher|dualpick}_{F}`: 53 frames, `mine_*` only.
  - `cat_weapon_{saw|drill|chainsaw|laser}_{F}`: 53 frames, `tool_*` only.
  - Frames `F` and their `CatLook` indices:

    | Clip | Frames | Index |
    |---|---|---|
    | idle | `idle0-5` | 0 |
    | blink | `blink`, `blink_d` | 6, 7 |
    | stretch | `stretch0-9` | 8 |
    | walk | `walk0-7` | 18 |
    | mine | `mine_{ready,ready2,antic,raise,top,strike,impact,recoil,return}` | 26-34 |
    | drag | `drag0-3` | 35 |
    | spawn | `spawn0-7` | 39 |
    | joy | `joy0-5` | 47 |
    | tool | `tool_{same 9}` | 53-61 |

  - **Missing for the new mode:** walking with a full bag, carry or backpack layer, pickup, unload, queue-wait, hat, pickaxe and boots
    layers from equipment, and up or down facing. Facing is right only, with flipX for left.
  - `tutor_cat_0..2`: 37×40 portrait for tutorial hints.
- **Rocks** (`r`): `rock_plain_{0,1,2}`, `rock_elixir_{small,big}`, `rock_gems_{small,big}`, `rock_pass`,
  `rock_chest_{wooden,iron,forged,silver,golden,royal,mythical,legendary}`. **Every rock has `_d1`, `_d2` and `_d3` damage stages.**
  - Overlay fallbacks: `rock_crack_1`, `rock_crack_2`.
  - Unused in code: `rock_debris_0`, `rock_debris_1`.
- **Obstructions** (`x`, now minable):
  - 9-tile set `block_{tl,t,tr,l,c,r,bl,b,br}`, horizontal `block_{hl,hm,hr}`, vertical `block_{vt,vm,vb}`.
  - Singles `block_single_{1..6,6l,6r}` and gate pillars `wall_gate_{l,r}`.
  - Water: `waterfall_{0,1,2}`, `waterfall_pool_{0,1}`, `waterfall_top`.
  - **No damage stages and no clearing animation exist.** TZ 8.2 requires one for `waterfall*` and `wall*`.
- **Shafts** (`s`, now a rich deposit): `shaft_base`, `shaft_gems` (tinted), `shaft_light`, `shaft_rubble`, `shaft_ribbon`, `shaft_heap`,
  `shaft_plaque`, `shaft_glint_0..3`, `shaft_wheel_0..3`. `shaft_base` is 64×104 and the wheel sits 88 px up. **No damage stages.**
- **Gate** (`p`): `gate_frame` (160×60), `gate_door_closed`, `gate_door_cracked` (**unused, a ready-made damage stage**),
  `gate_door_broken`, `gate_door_open_0`, `gate_door_open_1`, `gate_door_open`.
- **Exit** (`e`): `exit_base` (96×80), `exit_boards`, `exit_glow`.
- **Forge** (old `c`): `forge_base` (61×105), `forge_fire_0..2`, `forge_glow`.
- **Cannon:** `cannon_base`, `cannon_barrel`, `cannon_wheel_0/1`, `fx_muzzle_0..2`.
- **Containers:** `barrel_cat`, `barrel_fly_0/1`, `crate_cat`, `crate_dynamite`, `crate_elixir`, `crate_gold`.
- **Loot and cargo:** `loot_gold` (19×17), `loot_elixir`, `loot_hard`, `loot_glow` (28×12 floor glow). The decor `sack` (12×14),
  `crate` and `barrel` (12×13) are candidates for a cargo-bag or depot-crate placeholder.
- **Base buildings:** none exist. There is no depot, unload slot, hire board, workbench, bag rack or equipment cabinet. This is all new art (TZ 5).
- **Environment tiles:** `floor_0..3`, `floor_gate`, `wall_top`, `wall_top_1..3`, `wall_top_edge`, `wall_bottom`, `wall_side_0..3`,
  `wall_edge_l_0/1`, `wall_edge_r_0/1`, `wall_corner_{tl,tr,bl,br}`.
- **Wall decor:** `beam_post`, `lantern_0/1`, `banner_paw`, `chain_hang`, `mushrooms`, `crystal_cluster_a/b`, `crystal_spire`,
  `gear_copper_0..7`, `pipe_copper`, `minecart_ore`, `rails_v`, `sign_question`, `barrel`, `crate`, `sack`.
- **FX:** `fx_spark_0..3`, `fx_puff_0..3`, `fx_coin_0..3`, `fx_ring_0..3`, `fx_smoke_0..3`, `fx_boom_0..4`, `fx_impact_0..2`,
  `fx_hit_0..2` (unused), `fx_muzzle_0..2`, `fx_elixir`, `fx_gem`, `fx_glow_{warm,purple,white,teal,blue,lantern}`.
- **World UI:** `hp_frame` and `hp_fill` (9-slice), `badge_level`, `badge_level_hi`, `badge_level_wide`, `badge_level_wide_hi` (wide is unused),
  `digit_0..9` (5×7), `select_ring`, `select_bracket` (9-slice), `bubble_world` and `bubble_world_tail`, `slot_marker`, `slot_marker_hi`,
  `shadow_{xs,s,m,l,xl}`, `px_white` (1×1 used for fill and fog).
- **Icons** (UI, 16×16): `icon_{coin,elixir,gem,cat,cat_big,pickaxe,upgrade,income,clock,forge,shaft,gate,exit,lock,check,close,plus,info,
  warning,star,paw,gear,hand,hand_big,sort,cards,cards_big,card_small,dynamite,dynamite_big,chest,chest_big,chest_forged,chest_golden,
  chest_iron,chest_legendary,chest_mythical,chest_royal,chest_silver,chest_wooden,music,sound_on,sound_off,vibro_on,vibro_off,ambience,
  effects,arrow_{up,down,left,right,up_left,up_right,down_left,down_right}}`.
  - New icons needed: 16 equipment items, bag, depot, and the 6 base branches.
- **UI kit** (owned by the PixelUi map, listed for completeness):
  - Buttons: `ui_btn_{blue,dark,gold,green,red}[_pressed]`, `ui_btn_disabled`.
  - Panels: `ui_panel_{dark,inset,light,wood}`, `ui_window`, `ui_window_header`.
  - Bars: `ui_bar_frame`, `ui_bar_fill_{gold,green,purple,red}`.
  - Other: `ui_bubble[_tail]`, `ui_slot[_pressed]`, `ui_socket`, `ui_toast`, `ui_toggle_on/off`, `ui_slider_knob`, `ui_pill`,
    `ui_portrait_frame`, `ui_focus_frame`, `ui_divider[_v]`, `ui_dashed_frame`, `ui_dashed_slot`, `ui_badge_red`.
  - Card frames: `ui_card_{back,back_plain,frame_*,paw,tag}`.
- **Cards** (DROP): `card_bg_*`, `card_cat*`, `card_forge`, `card_gem`, `card_item_*` (21 items). Some item art
  (`card_item_golden_helmet`, `sledgehammer`, `lantern`, `lucky_paw`) could seed equipment icons.

---

## 3. Visual/Effects.cs: pooled FX. REUSE AS-IS

Chips use one shared `ParticleSystem` (max 400 particles, square, gravity along -Z). Frame effects use a ring pool of
**64 SpriteRenderers** (`PoolSize`, :26). When the pool is full it steals the oldest slot. Update runs only when slots are active and does not allocate.

- `public static Effects Main { get; private set; }` (:16): the last created instance. `CatView.MergeFlash` uses it.
- `public static Effects Create(Transform parent, Material particleMaterial)` (:77): called in `GameController.cs:126`.
- `public void Chips(Vector3 pos, Color color)` (:161): 5 chips on a hit.
- `public void Burst(Vector3 pos, Color color, int count)` (:164): debris on destruction.
- `public void Impact(Vector3 pos)` (:171): contact flash, at most 4 per frame (1 with ReduceEffects).
- `public void Sparkle(Vector3 pos, int count = 3)` (:240), `public void Glint(Vector3 pos)` (:252), `public void Puff(Vector3 pos)` (:256).
- `public void CoinPop(Vector3 pos, int count = 3)` (:263): coins hop up and fall, about 0.75 s. Good for unload at the depot.
- `public void LootPop(Vector3 pos, string currency, int count = 3)` (:277): gold, elixir or gem.
- `public void Ring(Vector3 pos)` (:295), `public void Smoke(Vector3 pos)` (:298), `public void Boom(Vector3 pos)` (:305).
- `public void Glow(Vector3 pos, Color color, float radiusCells, float seconds)` (:312): bell fade, integer scale. Good for the sector-reveal light.

Couplings: only the `LootState.Elixir` and `LootState.Hard` string constants (Core.Sim).
Gotchas:
- `Pop(...)` (:284) skips `VisualSettings.Particles(count)`, unlike `CoinPop`, so ReduceEffects does not halve elixir or gem pops.
- The chip material must be a URP Particles/Unlit copy because the sprite shader does not work on `ParticleSystemRenderer` (:137-156).
- Animations use `Time.deltaTime` (scaled). Decor uses `Time.time`.

## 4. Visual/FloatingText.cs: pooled "+N" text. REUSE AS-IS
`public static void Spawn(Vector3 pos, string text, Color color)` (:33). It uses a static `Stack<FloatingText>` pool, so instances
destroyed with the scene (Unity-null) are skipped. It rises 0.8 cells over 1 s in pixel steps and fades in 3 alpha steps.
- `Order = OverlayBase + 100` (:21) assumes `OverlayOrderAt(z) <= OverlayBase` for `z >= 0`. The base pad has `z < 0`, which gives an
  overlay order of up to 28048 at z = -3. That is still below 28100, but text would sink under overlays below about z = -6.
- TZ 16: spawn "+gold" **at the depot on credit**, not at the rock. `GameController.cs:600-607` currently spawns it at the loot or object position.

## 5. Visual/Views.cs

### `Pickable` (:11-18): raycast tag. ADAPT
`public enum PickKind { Cat, Object, Container, Loot }`, `public PickKind Kind; public int Id; public bool Bubble;`.
`PointerInput.cs:370-398` picks the best hit by priority (Loot -2, Bubble -1, Container 0, Cat 1, Object 2) and dispatches to
`CollectLoot`, `OpenContainer`, `SelectCat` or `TapObject`.
New mode: keep `Cat` and `Object` (info panels), add `Base` (open the base panel). Drop `Container`. Make `Loot` informational only
(TZ 5: tapping coins does not credit them) or remove it.

### `Label3D` (:26-127): pixel-font world label. REUSE AS-IS
- `public static Label3D Create(Transform parent, Vector3 localPos, float size, Color color)` (:50)
- `public void SetOrder(int order)` (:78). `public const float SmallSize = 2.5f, LargeSize = 5f` (:31-33).
- Gotchas:
  - Only sizes 2.5 and 5 are pixel-exact (:60-62).
  - One shared outline material is used for all labels (:37-41, 103-115). Do not set `tmp.outlineWidth` per label, because that clones a material each time.
  - `LateUpdate` reads `Camera.main` per label per frame (:117-126). Keep the label count low (ObjectView budgets their creation).

### `HpBar` (:134-193): 9-slice HP bar. REUSE AS-IS
`public static HpBar Create(Transform parent, Vector3 localPos, float widthCells)` (:147).
`public void Set(float fraction, bool visible)` (:165) changes the fill only when the pixel width changes. The width is rounded to an even pixel count.

### `CatView` (:204-879): layered animated cat. ADAPT HEAVILY
Structure:
- The root sits at the cell center and carries a `BoxCollider` (center (0,0.5,0.1), size (0.8,1,1)) and a `Pickable(Cat)`.
- Under it: `visual` (snapped), then `shadow` and `pose` (feet at -10 px). `pose` holds `body`, `helmet`, `weapon` and a `LevelBadge`.
- A SortingGroup orders the cat by feet z. Inner layer order: shadow 0, ring 1, body 2, helmet 3, weapon 4.
- All 62 frame sprites per layer are pre-resolved into arrays (:272-274, 351, 391, 398), so a frame swap needs no lookup.

Public API:
- `public int CatId; public int Level; public bool Dragged; public Vector3 TargetPos; public int TargetIndex = -1;` (:206-211)
- `public static CatView Create(Transform parent, CatState cat, bool appear = true)` (:330). It uses only `cat.Id` and `cat.Level`.
- `public void SetLevel(int level)` (:372) applies `ApplyLevelVisual()` (:384): weapon = `CatLook.Weapon(level)`,
  helmet = `CatLook.HelmetColor(level)`, plus the badge.
- `public void SetMining(bool mining, Vector3 facePoint)` (:411)
- `public void SetTimeToHit(float seconds, float attackDelay)` (:422) chooses antic, raise or top so that `top` lands on the hit.
  The wind-up lasts `min(0.3 s, 0.45 × delay)`.
- `public void Strike()` (:432) plays strike, impact, recoil and return (0.31 s). It is called when the `TargetHit` event arrives.
- `public void PlaySpawn()` (:446) and its alias `Pop()`: hop out with a scale pop. `public void PlayJoy()` (:457), `public void Pulse()` (:470).
- `public void SetHighlighted(bool on)` (:473): gold badge rim, a merge concept. `public void SetSelected(bool on)` (:481): `select_ring` under the feet.
- Merge: `ExpectMerge(Vector3)` (:490), `MergeFlash()` (:500), `AbsorbInto(CatView, Effects)` (:520).
- `public bool Walking` (:542) and `public void WalkInto(Vector3[] path, float delay, float speed, Effects fx)` (:549) follow a polyline
  at a given speed. At the end the cat shrinks into the exit shaft and the GameObject is deactivated.
- Deprecated no-ops: `Swing()` (:440) and `SetWindUp(bool)` (:443).

State machine (`Update` :662, `SelectFrame` :766), in priority order:
1. Drag, which loops `drag0-3` at 0.11 s.
2. One-shot spawn or joy (frames with pixel lift arrays).
3. Moving, which loops `walk0-7` at 0.09 s.
4. Strike.
5. Mining: wind-up by time-to-hit, otherwise `mine_ready` and `ready2` alternate every 0.42 s.
6. Stretch one-shot, which runs every 10-26 s while idle.
7. Idle, which loops `idle0-5` at 0.22 s with a blink every 2-5 s.

`CatLook.ForWeapon` maps `mine_*` to `tool_*` for saw, drill, chainsaw and laser.

Movement (:676-695):
- **Not path-based.** The view moves in a straight line toward `TargetPos` at a constant `StepSpeed = 2.4` cells/s (:230) and snaps on arrival.
- Facing is `flipX` by the sign of dx (dead zone 0.05). Up and down use the same side pose.

Couplings to cut: `Dragged` and drag lift, `DragOrder`, `SetHighlighted`, the merge trio, `Level`, `LevelBadge`, `CatState`,
and `WalkInto`'s exit-shaft ending.

ADAPT plan:
- Replace `SetLevel(int)` with `SetLook(int weaponKind, int helmetColour, ...)`, driven by equipped items (TZ 10, 16:
  "local damage upgrades do not change the worn pickaxe or hat").
- Add layers for backpack or bag and boots, using the same feet pivot (TZ 16: "matching feet pivots for all layers").
- Add frames for carry-walk, pickup, unload and queue idle, plus a bag-fill visual.
- Drive position from the simulation's path and edge progress, or feed `TargetPos` per edge with the sim move speed instead of a fixed 2.4.
  Otherwise cats cut corners through walls and lag behind faster upgrades.
- Add a small per-cat lateral offset so cats sharing a cell spread out (TZ 6.4 allows co-location).
- Generalize `WalkInto` into a path follower without the shaft-entry ending. That ending stays useful for the "team gathers" end-of-mine scene.
- Keep `SetMining`, `SetTimeToHit` and `Strike`. They map 1:1 to `Mining` tasks with a hit deadline.

### `ContainerView` (:886-1142): barrels and crates. DROP-FOR-NEW-MODE
`Create(Transform, ContainerState, Vector3)` (:944), `Pop()` (:1005), `Launch(Vector3 muzzle, Vector3 ground, Action<ContainerView>)` (:1016),
`Flying` (:942). It depends on `ContainerKind.Hire/FreeCat/Delivery`, `RewardTypes.Miner/Spell`, `Amount`, `CatLevel` and Loc keys
`board.container.*`. Reusable ideas: the arc-flight math (:1036-1060) and the land squash (:1084-1101).

---

## 6. Visual/CatLook.cs: frame naming and look rules. ADAPT
- `Coats` (6) (:17), `Weapons` (8) (:20), `HelmetColors` (8) (:25), `Cycle = 8` (:28)
- `Pick..Laser = 0..7` (:22)
- Frame constants: `Idle=0` (6), `Blink=6`, `BlinkDown=7`, `Stretch=8` (10), `Walk=18` (8), `MineReady..MineReturn=26..34`,
  `Drag=35` (4), `Spawn=39` (8), `Joy=47` (6), `ToolReady=53` (9), `FrameCount=62` (:31-42)
- `public static readonly string[] Frames` (:45)
- `public static int Coat(int catId) => Mathf.Abs(catId * 7 + 3) % Coats.Length;` (:84). The coat is a pure function of the cat ID, so it
  stays stable across saves. **Keep it** (TZ 16: cats keep different coats).
- `public static int Weapon(int level) => (Mathf.Max(1, level) - 1) % Cycle;` (:87)
- `public static int HelmetColor(int level) => (Mathf.Max(1, level) - 1) / Cycle % HelmetColors.Length;` (:90)
- `public static bool IsTool(int weapon)` (:93), `public static int ForWeapon(int frame, int weapon)` (:96)
- `public static string Body(int coat, int frame)` (:99), `public static string Helmet(int colour, int frame)` (:105),
  `public static string WeaponSprite(int weapon, int frame)` (:112). Names are cached in 2D tables with no per-frame concatenation.

Users: `CatView`, `HudContext.cs:223-230` (portrait), `BoardView.WeaponHitFx` (:584), and `VisualPolishTests` (:105-127), which
asserts every frame exists for each coat, helmet and weapon.
ADAPT:
- Replace the level-based `Weapon` and `HelmetColor` with equipment-to-visual mapping (pickaxe item to weapon kind, hat item to helmet sprite set).
- Add `Backpack`, `Boots` and new frame groups (carry, pickup, unload). Extend `FrameCount` and keep the manifest
  (`Tools/PixelArt/manifest.py` `CAT_FRAMES`) in the same order.
- Drop the `Drag` group if the new mode has no drag.

## 7. Visual/LevelBadge.cs: numeric plate (1-999). ADAPT / optional
`public LevelBadge(Transform parent, int centerX, int bottom)` (:47), `public Transform Transform` (:41), `public void SetOrder(int order)` (:58),
`public void SetActive(bool on)` (:65), `public void Set(int value, bool highlighted)` (:71).
It is built from 13×13 `badge_level` plates with a 5 px shift, seam pixels in palette colours, and `digit_0..9`. It has its own SortingGroup as an overlay.
New mode: there are no per-cat levels (TZ 4), so remove it from `CatView`. It could be reused as a bag-count or worker-count plate.

## 8. Visual/ObjectView.cs: map object view. ADAPT HEAVILY

Root at the feet. `visual` has a SortingGroup ordered by feet z with layers shadow 0, body 10, over 20-23. `overlay` holds the select
brackets, bubble and texts at `OverlayOrderAt(feetZ) + 0..2`. Animation tracks (`struct Track`, :65-81) cycle frames by `Time.time + phase`.

API:
- `public int Ordinal; public Color BaseColor;` (:17-18). `BaseColor` is the chip and burst colour.
- `public static readonly Vector2Int ForgeChimneyPixel` (:46)
- `public static ObjectView Create(Transform parent, BoardView board, ZoneObject obj)` (:143)
- `public void SetSelected(bool on)` (:357): gold 9-slice brackets around the footprint.
- `public void OnHit(Effects fx)` (:371): ±1 px jitter for 0.12 s plus `fx.Chips` at the body middle.
- `public void ApplyState(GameSession s, bool force)` (:377): called every frame for every object from `BoardView.Sync`.

Per-kind visuals (`BuildBody` :190-287):

| Kind | Sprites | Notes |
|---|---|---|
| Rock | `PresentationCatalog.RockSprite(balance, TypeId, variant)`, variant `Hash(Index, ZoneNumber, 17) % 3` | `HudContext.cs:202` duplicates the hash |
| Obstruction | 9-tile, bar or single blocks, or waterfall (animated) | `BuildObstruction` :293 |
| Shaft | base, light, gems, rubble, ribbon, heap, plaque, glint, wheel | |
| Gate | frame and door | |
| Exit | base, glow, boards | |
| Cart (forge) | base, glow, fire anim | smoke every 1.2 s while `Producing` |

`m_TopZ` is computed from the real sprite bounds and positions the labels and bubble.

**Damage stages** (`UpdateHp` :524-566):
- The HP fraction is `st.Hp / Balance.TargetHp(TypeId, Level)`, recomputed only when HP changes.
- The rock sprite becomes `_d1` below 75%, `_d2` below 45% and `_d3` below 20% (:548).
- If a `_dN` sprite is missing, the `rock_crack_1/2` overlay is used instead.
- The HP bar shows when the object is intact, its sector is open, and it has workers, is selected or is damaged.

**Destruction:**
- Only `Rock` with `Status == Destroyed` hides itself with `SetActive(false)` (:403-404). Shafts and obstructions never disappear.
- The burst and puff are fired by `BoardView.OnEvent` (:1187-1203): `fx.Puff(center)` and `fx.Burst(center, BaseColor, clamp(8+3·W·D, 10, 24))`.
  Workers targeting the object call `PlayJoy()`.

Gate doors:
- Status `Opened` plays `gate_door_open_0`, `_1`, then `open` at 0.12 s per frame when live.
- Status `Broken` shows `gate_door_broken`.
- Otherwise the door shows `closed`. The `gate_door_cracked` sprite is unused.

Exit: boards while `Intact`, glow otherwise.

Session couplings:
- `board.Session.Layout.CellSector` and `SectorOfRow(obj.Row + 1)` (:155-158). TZ 8.4 replaces this sector rule.
- `board.Session.Balance.TargetHp(obj.TypeId, obj.Level)` (:177).
- `s.Mine.Objects[Ordinal]`, which is an `ObjectState` with `.Status`, `.Hp` and `.Gen.PayoutReady/Automated` (:387-390).
- `s.IsSectorOpen` (:388), `s.WorkersOn` (:527), `s.IsCellOpen` and `s.IsTargetAvailable` (:579), `s.EtaSeconds` (:585).
- `ObjectStatus.{Intact, Destroyed, ReadyToActivate, Producing, Broken, Opened}`.
- `PresentationCatalog.RockVisual/RockSprite/ObstructionVisual/ObstructionSingleSprite/ShaftGemColor` read old `BalanceRepository`
  reward cycles. These can stay as **visual-only** mapping, and TZ 8.2 keeps the old look.
- Loc keys: `board.info.level|eta|noWorkers`, `board.status.activate|collect|auto|openGate|exitOpen`.

ADAPT plan:
- Introduce a view-model interface, for example `{Hp, MaxHp, Destroyed, Workers, SectorOpen}` per object, instead of `GameSession`.
- Apply HP bar, damage stages and destroy-hide to **all** `r/s/x/p`. `x` currently has no collider, no HP and no state (:165, :380-386).
  Shafts never hide.
- Damage art for `block_*`, `waterfall*` and `shaft_*`: use the crack overlay as a stopgap and request new art.
  Use `gate_door_cracked` as a gate damage stage.
- Drop the shaft generator states (ribbon, heap, plaque, glint, bubble), the status bubble with tap collider (:651-753),
  and the level "ур. N" label. TZ 8.1 says token levels are not shown.
- Replace the ETA label with an info panel (TZ 14). Optionally keep a workers or remaining-cargo label using the same TMP creation budget.
- Gate: no "Open" bubble (TZ 8.4: it opens automatically).
- Exit becomes a passive marker.
- Forge (`Cart`) becomes base decor or is removed. The new base is built separately below the map.

Perf notes:
- `ApplyState` runs for every object every frame, but heavy work is gated on status change.
- Labels are created lazily, at most 12 per frame (`InfoCreateBudget`, :60, using static `s_InfoBudgetFrame`).
- Text updates are throttled to 10 Hz (`Time.unscaledTime`).

## 9. Visual/LootView.cs: loot pile on the floor. ADAPT into a cargo-bag view
A pile sprite per currency (`loot_gold`, `loot_elixir`, `loot_hard`, falling back to `icon_*`) sits on a `loot_glow` floor glow with a
"+N" `Label3D`, a `BoxCollider` and a `Pickable(Loot)`.
- `public int LootId; public string Currency;` (:17-18), `public const float FeetY = -9f*Px; public const float PileCenterY = 0f;` (:22-24)
- `public static LootView Create(Transform parent, LootState loot, Vector3 rest, Effects fx)` (:89)
- `public void Drop(Vector3 from)` (:133): 0.36 s arc from the rock center to the cell, scale 0.5 to 1. On landing it puffs, at most 3 per frame.
- `public void Collect()` (:147): rise 10 px, swell, shrink to zero in 0.18 s, then `Destroy(gameObject)`.
- While waiting, it hops 2 px every 1.4 s and the glow dims. The floor order is `OrderAt(feetZ) - 8`, so cats draw over piles.

Couplings: `LootState` (`Id`, `Currency`, `Amount`, `Cell`) and the `LootState.Elixir/Hard` constants.
`BoardView.SyncLoot` (:1107) diffs it against `Mine.Loot`.

**Pooling: none.** Each pile is a `new GameObject` with TMP and is destroyed afterwards. TZ 19 requires a pool of bags and visual
merging of portions. ADAPT:
- Key the view by cargo batch ID. Show the unit count instead of the amount. The label is optional because TZ 14 shows value in the cat
  and target panels.
- Remove the `Pickable` (TZ 5).
- Pool views in a `Stack` like `FloatingText`.
- Replace `Collect()` with "picked up" (fly into the carrying cat's bag), and reuse `Drop()` for "released from rock".
- New art: a coin bag. The `sack` decor works as a placeholder.

## 10. Visual/CannonView.cs: DROP-FOR-NEW-MODE
`public static CannonView Create(Transform parent, Vector3 feet, bool faceLeft)` (:70), `public void Fire(bool left, Effects fx)` (:103),
`public Vector3 Muzzle` (:56), `public Vector3 MuzzleGround` (:59), `public bool FacingLeft` (:50), `public int Shots` (:53).
It is tied to `ZoneLayout.CannonCol/CannonRow` and to hiring barrels. TZ 8.3 removes the cannon cells.
Tests: `Tests/PlayMode/CannonShotTests.cs`, which should be deleted with it.

## 11. Visual/VisualKit.cs
- `Palette` (:11-97): named colours mapped to `PixelPalette`. REUSE: `RockColor(RockVisualKind)` (:84), `Gold`, `Elixir`, `RockHard`,
  `Highlight`, `TargetLabel` and others.
  - `Fur(int)` (:77) and `Helmet(int)` (:78) are **dead and inconsistent** with `CatLook`. They use a different colour order and index by
    `(level-1)%8`. Delete them.
- `Materials` (:100-169) and `Meshes` (:172-333) are legacy 3D helpers, used only by `DevTools/DevPanel.cs:856-860`. Not for gameplay.

## 12. Visual/BoardView.cs: board orchestrator. ADAPT / split
Holds `GameSession m_S` and `ZoneLayout m_L` (:42-43). Creates the floor mesh, decor, fog, merge markers, objects, cats, containers,
loot and cannon. `Sync(dt)` is called every frame from `GameController.cs:473`. `OnEvent(SimEvent, Effects)` is called for each event
from `GameController.cs:578`, **before** `Sync` in the same frame. Several comments rely on that ordering (:1009, :1025, :1207).

### Keep (REUSE or light ADAPT)

**Coordinates and API:**
- `public int Depth`, `public int Width` (:115-116)
- `public Vector3 CellCenter(int cell)` (:139), `GridToWorld(double, double)` (:140), `WorldToGrid(Vector3)` (:141)
- `public Vector3 LootPos(int cell)` (:144), `public Vector3 ObjectCenter(int ordinal)` (:147)
- `public Effects Fx { get; set; }` (:130), `public int SelectedCatId { get; set; }` (:128), `public void Select(int ordinal)` (:792)
- `public CatView CatViewOf(int id)` (:799), `public LootView LootViewOf(int id)` (:815), `public ObjectView ObjectViewOf(int ordinal)` (:816)
- `public Vector3 ExitCenter` (:838)

**Static helpers:**
- `internal static Vector2 CanvasOrigin(string sprite, float left, float bottom)` (:223)
- `internal static Vector2 CenteredPos(string sprite, Vector2 center, Vector2 size)` (:231)
- `internal static SpriteRenderer FillRect(Transform visual, string name, Vector2 center, Vector2 size, int order, Color color)` (:241)
- `internal static uint Hash(int a, int b, int c)` (:252): the deterministic variant hash. Keep it, because `HudContext` and rock variants depend on it.

**Floor and walls** (`BuildFloorAndWalls` :273, `TileName` :349): one mesh with atlas UVs (inset 0.01 texel against seams), ordered at
`FloorOrder`, at y = -0.02. It covers columns `-4..Width+3` and rows `-8..Depth+7`.
- ADAPT: in `TileName`, rows `Depth..Depth+2` become floor (the base pad) instead of `wall_bottom`. Gate-row detection uses `m_L.Gates[].Row`.
- Gotcha: the mesh switches to `UInt32` indices above 65000 vertices (:328). A 64-row map is 15 columns × 80 rows × 4 ≈ 4.8k vertices, so this is fine.

**Decor** (`BuildDecor` :407): deterministic by `Hash(zone, Balance.Zone(z).DecoSeed, 0x5EED)` (:381-387). It places lanterns every 4 rows
(2 frames, random 0.3-0.8 s flicker), crystal druses with glow and glints, copper gears and pipes (Copper theme), banners and small props.
The back wall above row 0 gets 2 lanterns, chains and a druse.
- ADAPT: rails and a minecart are placed relative to `m_L.Cart` (:429-441). Move them to the base pad or drop them.
- `sign_question` sits at row 0 on the right wall. TZ 16 keeps decor stable per `DecoSeed` and ID.
- Updaters `UpdateLanterns` (:613), `UpdateGears` (:564) and `UpdateGlints` (:604) do not allocate. `UpdateGlints` reads `m_S.IsCellOpen`.

**Fog of closed sectors** (`BuildFog` :638, `ApplyFog` :682, `UpdateFogFade` :720, `ApplyFogOrders` :669):
- One `px_white` rect per sector `k >= 1`, at alpha 0.62 in `PixelTheme.Dark`.
- Sector `k` spans the rows above gate `k-1` up to and including gate `k`. The gate row belongs to the sector below.
  This **matches TZ 8.4** (sector 0 is the bottom, the gate row belongs to the lower sector).
- Fog is driven only by `m_S.Mine.GatesOpened` (:986-993) and fades over 0.8 s when a gate opens live.
- Sorting gotcha: the lowest closed sector uses `OrderAt(bottomEdge) + 8` so the gate being mined stays visible above the fog.
- The reveal glow on `CheckpointOpened` (:1296-1307) uses `MergeRows` to place the pad. ADAPT it to the gate row or the sector bottom.

**Exit walk** (`PrepareExitWalk` :847, `PlayExitWalk` :951, `Frozen` :835) runs a BFS over the old walkability rules
(`IsCellOpen`, `ObjectAt`, `Gate Opened`, :914-929) to the exit mouth, then walks the cats with a stagger.
- New mode: the core owns navigation (TZ 6.4) and "the team gathers" at the base. Reuse the `Frozen` and staggered-walk idea, but feed core paths.

### DROP-FOR-NEW-MODE
- Merge markers: `BuildMarkers` (:738), `UpdateMergeMarkers` (:753), using `m_S.MergeRows` and `IsCellFree`.
- Drag: `DraggedCatId`, `DragWorldPos` (:117-118), `UpdateDropMark` (:1062), `DropAllowed` (:1091, using `CatAt`, `ObjectAt`,
  `AcceptsCat`, `ContainerAt`), and `HighlightMergeCandidates` (:1154).
- Containers: `m_Containers`, `ContainerViewOf` (:800), `IsFlying` (:803), `AnyContainerFlying` (:806, used by `TutorialController.cs:433`),
  `ContainerLanded` (:137), `OnContainerLanded` (:1129) and `FindContainer` (:1136). Container sync (:1034-1048) is O(n²).
- Cannon: `m_Cannon`, `Cannon` (:134), and its creation (:195-197) from `m_L.HasCannon/CannonCol/CannonRow`.
- `CheckCatalog` (:208) reads `PresentationCatalog.MissingVisualEntries(Balance)` for shaft names. Keep it only if shaft visuals stay.
- OnEvent cases `CatMerged` (:1204), `ContainerSpawned` (:1224), `ContainerOpened` (:1249), `GeneratorUpgraded`, `MineShaftActivated`
  (:1286) and the tap branch of `LootCollected`.

### Rewrite: `Sync(float dt)` (:982) and `OnEvent(SimEvent, Effects)` (:1160)
`Sync` reads:
- `m_S.Mine.GatesOpened`, `.Objects` (via `ObjectView.ApplyState`), `.Cats` (`Id`, `Cell`, `Level`, `TargetIndex`, `PhaseTicks`),
  `.Containers` and `.Loot`.
- `m_S.FindCat`, `OrdinalOf(targetIndex)`, `AttackDelayTicks(level)` and `GameSession.TicksToSeconds` (:1026-1028). These feed `SetTimeToHit`.

OnEvent reacts to `TargetHit`:
- `ObjectView.OnHit` and `CatView.Strike`.
- `fx.Impact` at the target side facing the cat, 0.32 cells from the center and lifted 0.18 (:1175-1182).
- `WeaponHitFx` by weapon: saw and chainsaw add sparkle, laser adds a red glow, hammer adds a puff.

It also reacts to `RockDestroyed`, `ShaftCleared`, `GateBroken`, `ExitBroken`, `LootDropped`, `LootCollected` and `CheckpointOpened`.

New mode: the same pattern works, a diff of the sim's cat, object and cargo collections plus event-driven one-shots. Keep the
"OnEvent before Sync" contract or document a new one. New events to visualize: cargo released, picked up, cat arrived at depot,
unload tick (`CoinPop` plus `Hud.FlyLoot` at the depot slot), queue changes and team-size changes.

---

## 13. How loot reaches the HUD today, and what TZ 16 changes
1. `LootCollected` arrives. `BoardView.OnEvent` (:1271-1285) calls `LootView.Collect()`, then `fx.LootPop(LootPos(cell), currency, tap ? 4 : 2)`
   and `fx.Sparkle`. The budget is `LootFxPerFrame = 6` (:57, `TakeLootFx` :1144).
2. `GameController.cs:618-627` calls `Hud.FlyLoot(Board.LootPos(e.Cell), e.Text, count)`. `Hud.cs:631` projects through the camera and
   runs pooled UI icons (24-icon pool, `FlyMaxPerDrop` up to 10) along a Bézier curve to the currency counter, pulsing it on arrival.
3. `RewardGranted` makes `GameController.cs:600-607` call `FloatingText.Spawn` above the pile.

New mode: credit happens only at unload (TZ 7.3, 16). Call `Hud.FlyLoot(depotSlotWorldPos, "gold", n)`,
`FloatingText.Spawn(depotSlotWorldPos, "+N", Palette.Gold)` and `Effects.CoinPop(depotSlotWorldPos)` on the sim's unload or credit event.
`Hud.FlyLoot(Vector3 worldPos, string currency, int count)` is reusable as-is.

## 14. Pooling summary

| Item | Pooled? | Mechanism |
|---|---|---|
| Sprite FX | yes | 64-slot ring with oldest-steal (`Effects.FreeSlot` :361) |
| Chips | yes | one ParticleSystem, max 400 |
| `FloatingText` | yes | static `Stack`, skips Unity-null entries |
| HUD fly icons | yes | 24 `Image`s (`Hud.cs`) |
| `CatView`, `ObjectView`, `LootView`, `ContainerView` | **no** | `new GameObject` plus `Destroy`, with Pickable and TMP on loot. Make `LootView` and bags pooled for the new mode (TZ 19). |

Per-frame budgets:

| Budget | Value | Location |
|---|---|---|
| Impacts | 4 per frame | Effects.cs:167 |
| Loot FX | 6 per frame | BoardView.cs:57 |
| Land puffs | 3 per frame | LootView.cs:44 |
| TMP label creation | 12 per frame | ObjectView.cs:60 |
| Label text rate | 10 Hz | ObjectView.cs:597 |

## 15. Gotchas collected from comments and code
- **Event order:** `OnEvent` runs before `Sync` in a frame. A view created in OnEvent, such as a loot drop, must not be recreated by
  Sync's diff. A view removed by the model is animated out by the view itself (`LootView.Collect`, `CatView.AbsorbInto`).
- **Static globals that survive scene reloads and domain-reload-less play mode:** `PixelTheme.Current`, `Effects.Main`,
  `FloatingText.s_Free`, `EnvironmentThemes.s_Table`, `BoardView.s_CatalogChecked`, `ObjectView.s_InfoBudgetFrame`,
  `LootView.s_PuffFrame`, `Label3D.s_Outline`, and the `PixelArt` caches.
- **Scaled time:** `Time.time` and `Time.deltaTime` drive all visuals, so a dev sim speed of ×100 does not speed up animations.
  Cat visual speed is fixed at 2.4 cells/s (Views.cs:230). The new mode must derive it from the sim speed.
- **Fractional scale** is allowed only in pops of 0.25 s or less (style guide rule, Views.cs:245, :914). All movement is in whole art pixels.
- **Facing** is horizontal only (flipX). There are no up or down poses, so "turn toward base" is a flip only.
- **Selected cats never stretch** (Views.cs:719).
- **Missing atlas names** do not throw. They render a pink checker and warn once. `VisualPolishTests` asserts that cat frames and theme variants exist.
- **Exact art reference points must match the art.** `ObjectView.ForgeChimneyPixel (32,2)` on the 64×106 canvas and
  `CannonView.MuzzleCanvas (26.5,33.5)` on 32×72 must be re-checked whenever the art changes (comments at ObjectView.cs:41-47 and CannonView.cs:18-23).
- **Rendering:** `Label3D.LateUpdate` calls `Camera.main` for each label. HP bars and labels each carry their own SortingGroup with `sortAtRoot`
  so they escape the object group and sort as overlays.
- **Tests touching this subsystem** need to be updated:
  - `Tests/PlayMode/VisualPolishTests.cs`: themes, atlas frames, the 8-level weapon cycle, shaft states, cats showing their level.
  - `CannonShotTests.cs`, `CoreGameSmokeTests.cs`, `PointerInputTests.cs`, `TutorialFlowTests.cs`, `VisualAssetCatalogTests.cs`.
- **Atlas regeneration** goes through Python (`Tools/PixelArt/build.py`), which writes a 541 KB `.g.cs`. Large diffs are expected, and the
  file takes time to sync to the PC.
