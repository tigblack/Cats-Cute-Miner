# Cat Miners: reusable pixel-art pipeline and assets for hub-side ad overlays

You can reuse the game's own art directly. A prebuilt atlas (`Tools/PixelArt/out/atlas.png`) and its manifest are on the hub, and both match what the game ships. A Python script can crop any of the 3571 sprites from it, or import the generators and draw any sprite at native size. The Pixeloid Sans fonts are also on the hub and render crisply in PIL. There is no game logo or app icon anywhere.

I changed no project files. All checks ran in memory with `Tools/.venv/bin/python -B` (the `-B` stops Python writing `__pycache__` files).

Paths below are relative to `ROOT=/mnt/claude-data/sargsyan-tinote/cats-cute-miner`. Key files:
- `$ROOT/Tools/PixelArt/{pxl.py,build.py,manifest.py,overview.py,gen_palette.py,review_cards.py}`
- `$ROOT/Tools/PixelArt/sprites/*.py`
- `$ROOT/Tools/PixelArt/out/{atlas.png,manifest.json,sheet_*.png}`
- `$ROOT/Assets/Game/Scripts/Runtime/Pixel/PixelAtlasData.g.cs` (the atlas embedded in code)
- `$ROOT/Assets/Game/Scripts/Runtime/UI/{PixelUi.cs,UiKit.cs,TutorialHint.cs}`
- `$ROOT/Assets/Game/Resources/Fonts/PixeloidSans-{Regular,Bold}.ttf` and `PixeloidSans-OFL.txt`

---

## 1. How sprites are produced and reused

### 1.1 Generators (all sprites are drawn by code)
- **`pxl.py`** (the drawing library):
  - `RAMPS` (34-78), `PALETTE` (80-86), `C(color)` (89-103).
  - `class Canvas` (117-323): `rect`, `frame`, `line`, `ellipse`, `polygon`, `outline(c='ink1', diagonal, inside)` (239), `shade`, `dither`, `blit`, `flipped`, `crop`, `from_ascii`.
  - `Canvas.scaled(k)` returns a PIL RGBA image scaled with NEAREST (322-323).
  - `SpriteEntry(name, canvas, pivot, border, group, alpha, extrude, note)` (327-336). `Registry.add(...)` (339-355).
- **`build.py`**:
  - `MODULES = ['env','rocks','structures','base','themes','cats','ui','icons','fx','tutorial','loot','cards','card_art']` (line 40). `weapons.py` is imported by `cats` and is not in this list.
  - `load_modules(only) -> pxl.Registry` (45-55). Other functions: `trimmed` (89-104), `pack` (113-159), `sheet` (162).
  - Importing `build` has no side effects. **Do not call `build.main()`**: it rewrites `PixelPalette.g.cs`, `atlas.png`, `PixelAtlasData.g.cs` and `manifest.json` (242-243, 271-276). The two `.cs` files sync to the PC and would trigger a Unity recompile.
- Exact calls (run from `$ROOT`):
```python
import sys; sys.path.insert(0, 'Tools/PixelArt')
import build, pxl
reg = build.load_modules(['icons', 'ui', 'tutorial'])   # or build.MODULES
e = reg.entries['icon_hand_big']        # untrimmed canvas: 24x24, pivot (12,12) from bottom-left
img = e.canvas.scaled(4)                # PIL RGBA, 1 art px = 4 screen px
```
- Load times I measured on the hub:

| Module(s) | Time |
|---|---|
| ui | 0.05 s |
| icons | 0.24 s |
| tutorial | 0.03 s |
| cards | 0.21 s |
| card_art | 0.83 s |
| cats | 11.3 s |
| all modules | 15.7 s |

- Generator canvases are **untrimmed**, so layers share one canvas: cats 40×40 with feet pivot (20,4), card art 48×44, `_big` icons 24×24. Layers can be stacked with `alpha_composite` at (0,0).
- Ready-made layer helpers:
  - `overview.py:27` `cat(E, fur, weapon, colour, frame)` for the legacy helmet and weapon layers.
  - `out/preview_equip.py:24` `cat(E, fur, fr, hat='base', pack='base', boots='base', pick='base', load=0)` for the current equipment layers. Its `main()` is guarded, but running it writes PNGs into `out/`.
- Some generators take a size, so you can draw a sprite at its real size instead of 9-slicing:
  - `sprites/ui.py:287` `button(w, h, cols, pressed, spark=None, cut=3, lip=2)`, with `cols = ui.BTN['gold'|'green'|'blue'|'red'|'dark'|'disabled']` (271-282) and `ui.BTN_SPARK` (284).
  - `ui.plate(w, h, cut, specs, body)` (100) builds any panel.
  - I checked that `ui.button(60, 28, …)` gives the same pixels as a 9-slice of `ui_btn_{gold,green,blue}`.

### 1.2 Atlas and manifest on the hub (simplest: just crop)
- `Tools/PixelArt/out/atlas.png` is 2048×1024 RGBA. It was built 2026-10-12 22:43, after the last sprite source change (`icons.py`, 22:42).
  - Its hash `dcea9cdf0d725a67` equals `PixelAtlasData.Hash` (`PixelAtlasData.g.cs:10`).
  - It is pixel-identical to the base64 PNG embedded in that `.cs` file.
  - The PNG lives only on the hub (PNGs don't sync). If it ever goes missing, decode the `public const string Png =` literal from the `.cs` file, which does sync.
- `manifest.json` format (written at `build.py:273-276`):
```json
{"atlas":[2048,1024],"hash":"dcea9cdf0d725a67",
 "sprites":{"ui_btn_gold":{"rect":[1293,756,16,16],"pivot":[8.0,8.0],"border":[6,6,6,6],"group":"ui"}, ...}}
```
  - `rect` is x, y **from the top**, w, h, in atlas pixels. The C# table stores y from the bottom (`uy = H - y - h`, `build.py:220`).
  - `pivot` is in pixels from the **bottom-left** of the trimmed rect (Unity convention).
  - `border` is `(left, bottom, right, top)`, used for 9-slice.
  - 3571 names. Identical sprites share one rect (`build.py:107-142`).
- Sprites without a border are **trimmed** and their pivot recalculated (`build.py:89-104`). To keep layers aligned, place each crop by its pivot.
  - I checked this method against the generators: cat layers and all 61 card portraits came out identical.
```python
M = json.load(open('Tools/PixelArt/out/manifest.json'))['sprites']
A = Image.open('Tools/PixelArt/out/atlas.png').convert('RGBA')
def crop(n): x,y,w,h = M[n]['rect']; return A.crop((x,y,x+w,y+h))
def place(dst, n, ax, ay):            # (ax,ay) = pivot location in dst, top-down coords
    im = crop(n); px,py = M[n]['pivot']; dst.alpha_composite(im, (round(ax-px), round(ay-(im.height-py))))
def nine(n, W, H):                    # 9-slice in art pixels; edges and centre are uniform, so NEAREST stretch = tiling
    s = crop(n); w,h = s.size; l,b,r,t = M[n]['border']; out = Image.new('RGBA',(W,H))
    for sx0,sx1,dx0,dx1 in ((0,l,0,l),(l,w-r,l,W-r),(w-r,w,W-r,W)):
        for sy0,sy1,dy0,dy1 in ((0,t,0,t),(t,h-b,t,H-b),(h-b,h,H-b,H)):
            if sx1>sx0 and sy1>sy0: out.paste(s.crop((sx0,sy0,sx1,sy1)).resize((dx1-dx0,dy1-dy0),Image.NEAREST),(dx0,dy0))
    return out                        # then .resize((W*4,H*4), Image.NEAREST)
```
  - A cat: `place(c,'cat_body_ginger_walk0',20,36)` on a 40×40 canvas, then `boots`, `pack`, `load`, `hat`, `pick`, `sack` layers at the same anchor.
  - A card portrait: `crop('card_bg_<rarity>')`, then `place(bg,'card_art_caNNN',32,24)`. This matches `review_cards.composite`.
- Visual previews already on the hub: `out/sheet_<group>.png` at ×4 with labels, `out/overview/*.png`, `cards_overview.png`.

### 1.3 Sprite names useful for ads (atlas sizes are trimmed)

| Need | Names |
|---|---|
| Tap pointer (paw) | `icon_hand_big` (23×22; canvas 24×24, fingertip at pixel (2,2) of the canvas and of the crop). `icon_hand` (16×16, fingertip (3,0)). `icon_paw` (pink paw print), `ui_card_paw`. Game shows the hand ×2 = 8 px per art px (`TutorialHint.cs:54-55,183-185`) |
| Coin / gold | `icon_coin` 16×16 (HUD "gold"), `fx_coin_0..3` (spinning, 8×8), `loot_gold`, `cargo_sack_1/2`, `cargo_heap_s/l`, `cargo_glow`, `fx_sack_pop_0..2`, `icon_sack` ("en route"), `icon_income` |
| Gem | `icon_gem` 16×12, `fx_gem`, `loot_hard`, `store_gems_{handful,sack,bag,crate,cart,pile}` (24×24 canvas) |
| Elixir | `icon_elixir` 12×16, `fx_elixir`, `loot_elixir`, `store_elixir_{bottle,box,wagon}` |
| Chests (8 tiers, `manifest.CHESTS` line 76) | `icon_chest_{wooden,iron,forged,silver,golden,royal,mythical,legendary}` (24×22, legendary 26×23), `icon_chest`, `icon_chest_big`. In-mine chest rocks: `rock_chest_<tier>` plus damage stages `_d1.._d3`. No opened-chest or lid frames exist |
| Store tiles | Tile = `ui_btn_blue` 9-slice + `ui_panel_inset` + art ×2 (`store_<id>` or `icon_chest_<tier>`) + `ui_pill`; price shown with `icon_gem` (`StoreTileView.cs:23,56-62,72-122`). Skip art: `store_skip_{1h,4h,12h}`; these IDs are unchanged in `store_catalog.json`. `icon_store`, `icon_ad_tv` |
| Cats in the current game | Layers, bottom to top (`OutpostCatView.cs:76`): `shadow_s`, `select_ring`, `cat_body_<fur>_<fr>`, `cat_boots_<r>_<fr>`, `cat_pack_<r>_<fr>`, `cat_load_<1-3>_<fr>`, `cat_hat_<r>_<fr>`, `cat_pick_<r>_<fr>`, `cat_sack_<fr>`. fur = ginger, gray, black, white, cream, brown. r = base, common, rare, epic, legendary (`manifest.py:26,61`) |
| Cat frames | `idle0-5`, `blink`, `blink_d`, `stretch0-9`, `walk0-7`, `mine_ready`, `mine_ready2`, `mine_antic`, `mine_raise`, `mine_top`, `mine_strike`, `mine_impact`, `mine_recoil`, `mine_return`, `spawn0-7`, `joy0-5`, `carry0-7`, `pickup0-3`, `unload0-3` (`manifest.py:33-49,67-75`). The pick layer has no carry/pickup/unload frames; the sack layer only has `pickup1/2` and `unload0-2` |
| Cats, legacy mode | `cat_helmet_<yellow…white>_<fr>`, `cat_weapon_<pick…laser>_<fr>`. Badges: `badge_level(_hi,_wide)`, `digit_0-9` |
| Cat heads and portraits | `icon_cat` (16×15), `icon_cat_big` (24×21), `icon_branch_team`. Manager card layers: `card_cat` + `card_cat_helmet` or `card_cat_hat` + `card_gem` (light tones, tint it) + `card_cat_paws` |
| Mentor (tutorial advisor, goggles on helmet) | `tutor_cat_0` (calm), `_1` (talking), `_2` (blink); 37×40, canvas 40×40, centre pivot. `ui_portrait_frame` 52×52 (brass ring, navy disc); the mentor is centred in it at ×1. Speech bubble: `ui_bubble` (b7) + `ui_bubble_tail` (pivot (4.5,0), flipped in X). Target corners: `ui_focus_frame` |
| Card art | `card_art_ca001…ca060, ca062` (61 cards, 48×44 canvas). Backgrounds `card_bg_{common,uncommon,rare,epic,legendary,majestic}` 64×48. Frames `ui_card_frame_<rarity>` 24×32 b6. Backs `ui_card_back(_plain)`. Small art `card_item_*`, `card_forge`. Names: `Loc.Cards.cs:15-75`; rarity: `card_art.CATALOG` (`card_art.py:36-50`) |
| 9-slices | `ui_btn_{gold,green,blue,red,dark}` and `_pressed`, `ui_btn_disabled` (all 16×16, border 6). `ui_panel_dark` 16 b5, `ui_panel_inset` 12 b4, `ui_panel_light` 16 b5, `ui_panel_wood` 24×16 b6 (the wooden "Mine N" plate), `ui_window` 32 b10 (wood + gold frame), `ui_window_header` 32×14 b(8,4,8,4) (wood plank with gold brackets), `ui_toast` 16 b6, `ui_pill` 16×12 b(6,5,6,5), `ui_slot` / `ui_slot_pressed` 20 b6, `ui_socket`, `ui_bar_frame` 12×8 b(4,3,4,3), `ui_bar_fill_{gold,green,purple,red}` 6 b2, `ui_divider(_v)`, `ui_dashed_frame` / `ui_dashed_slot`, `ui_badge_red` 9×9 |
| Buttons as seen in game | "Base" = `PixelButton.Gold` with `FontTitle`, 42 art px tall (`OutpostHud.cs:38-40,486-491`). "Great" (`outpost.offline.ok`, `Loc.Outpost.cs:246`) = `PixelButton.Green` (`OutpostHud.Results.cs:81`). Blue = secondary ("Equipment") |
| Pickaxes and tools | `icon_pickaxe`, `icon_branch_pickaxes`, `icon_item_pickaxe_<r>` and `_big`, `icon_slot_pickaxe`. Player tap strike: `fx_strike_pick_0..3` (pivot (10.5,3.5) = contact point; 0.05/0.05/0.08/0.08 s). Other upgrade icons: `icon_branch_{team,pickaxes,boots,bags,unload,value}`, `icon_item_{hat,backpack,boots}_<r>(_big)` |
| Rocks and ores | `rock_plain_0..2` (gold nuggets), `rock_elixir_small/big`, `rock_gems_small/big`, `rock_pass`, each with `_d1.._d3`. `rock_crack_1/2`, `rock_debris_0/1`, `block_*`, `block_single_*`. Themed variants `*_crystal` / `*_copper` exist only for walls, floors and blocks |
| Base camp buildings | `depot_base` 96×64, `depot_glow`, `depot_crate_0..3`, `depot_slot(_active)`, `depot_chute(_active)`, `hire_board_0..2`, `bench_0..2`, `rack_0..2`, `wardrobe_closed/open`, `cart_0/1`, `camp_lantern_0/1`, `camp_sign`, `camp_tent`, `camp_floor_*`, `camp_edge_t/b`, `camp_ground_*`. Mine structures: `shaft_*`, `gate_*`, `exit_*` |
| Effects | `fx_spark_0..3`, `fx_impact_0..2`, `fx_hit_0..2`, `fx_puff_0..3`, `fx_ring_0..3`, `fx_boom_0..4`. Glows with alpha: `fx_glow_{warm,purple,teal,blue,white,lantern}`. `select_bracket`; shadows with alpha: `shadow_xs..xl`. Icons `icon_star`, `icon_check`, `icon_lock`, `icon_upgrade`, `icon_arrow_*` |

---

## 2. UI style rules

- **Palette**: the only source is `pxl.RAMPS` (`pxl.py:34-78`). Get colours with `pxl.PALETTE['gold3']` (RGB tuple). The build rejects any colour outside the palette (`build.py:71-74`).

| Use | Colour |
|---|---|
| Outline / text shadow | `ink1` #0b0a14 (`ink0` #07060f, `ink2` #1a1424) |
| Dark panel | outline `navy0` #0c0f22, body `navy1` #141a36, window body `navy2` #1d2549, bevel `navy4`/`navy6` #34416e / #5b6a92 |
| Wood | `wood0` #2b1612 … `wood7` #ecc99a |
| Gold button | outline `wood0`, top line `gold4` #ffe36a, band `gold3` #f2c230, body `amber3` #f2a636, shadow `amber2` #d9771f, lip `amber1` #a84b18, spark `gold5` #fff6c4 |
| Green button | `green0` #11200f / `green6` #d0ec8a / `green5` #9cc85a / body `green4` #6a9a3a / `green3` #45702e / lip `green2` #2c4a26 |
| Blue button | `blue0` #0a1638 / `blue6` #a8d2ff / `blue5` #6aa6f0 / body `blue4` #3d7ad6 / `blue3` #2655ad / lip `blue2` #18357a |
| Bubble | body `cream3` #fdf6e3, outline `ink2` |

  Button colour definitions are at `ui.py:271-284`.

- **Text colours** (`PixelUi.cs:97-122`):

| Role | Colour |
|---|---|
| Main | `cream3` #fdf6e3 |
| Dim | `cream1` #c8b8a0 |
| Accent / prices | `gold3` #f2c230; light accent `gold4` #ffe36a |
| Dark (on gold buttons, light panels, bubbles) | `wood0` #2b1612 |
| Bad | `red5` #ff9070 |
| Good | `green5` #9cc85a |
| Elixir | `purple5` #bf8ff8 |
| Income | `navy8` #9ba6c2 |
| Shade under windows | `ink0` at alpha 0.62 |

- **Drawing rules** (`PIXEL_STYLE_GUIDE.md:52-58`, `ART_DIRECTION.md:9-16`):
  - Light comes from the top-left.
  - 1 px outline in the darkest tone of the material, or `ink1`/`ink2`; never pure black.
  - Inner lines are one tone darker than the fill; 3–5 tones per material.
  - Only ordered dithering. No gradients, blur or rounded vector shapes.
  - Semi-transparency only for shadows, glows and dimming.
  - Only integer scaling, except "pop" animations of 0.25 s or less (`PIXEL_STYLE_GUIDE.md:14-22`, `:203`).
- **Scale at 1080×1920**:
  - UI: 1 art px = 4 Canvas units = **4 screen px** (`PixelArt.cs:42` `UiPixel = 4`; `PixelUi.cs:78` `Px`).
  - The CanvasScaler uses reference 1080×1920 with match 0.5 (`OutpostHud.cs:30,273-275`), so the scale is exactly 1.0 only if the capture is exactly 1080×1920.
  - Large icons are often ×2 (8 px per art px): the tutorial hand, the offline-window coin, store tile art.
  - All sizes and positions are multiples of 4 (`PixelUi.Snap`, `:234`).
  - **The game world is not on that grid.** Mines with a topology use free exploration with 10 columns across the screen (`CameraRig.cs:39,216-217`; `OutpostController.cs:168`): 1080/10 = 108 px per cell, so **3.375 screen px per art px**. The `english-ui/01-hud.png` screenshot confirms about 108 px cells. Overlays should stay on the 4 px UI grid.
- **Component sizes** (art px):
  - `ButtonHeight` 28, `SmallButtonHeight` 20, `SlotSize` 40 (`PixelUi.cs:125-131`).
  - Outpost HUD (`OutpostHud.cs:34-57`): top bar 30, bottom bar 52 with 5 padding, side buttons 40 wide, strip 44, pill 16.
  - Button label is Bold and centred on the button face, 1 art px above the geometric centre because of the 2 px lip. Pressed state uses the `_pressed` sprite and moves the content down 1 art px (`PixelUi.cs:382-403`).
  - Gold buttons get dark text (`wood0`) with no shadow; every other style gets cream text with a shadow (`:180-183`).
- **Text shadow**: `ink1`, 1 font pixel straight down, no blur (`UiKit.cs:31-35,174-189`). Fake bold is never applied to the pixel font (`PixelUi.cs:336-344`).
- **Font**:
  - Pixeloid Sans: `Assets/Game/Resources/Fonts/PixeloidSans-Regular.ttf` and `PixeloidSans-Bold.ttf`.
  - On the PC: `C:\Unity Projects\Cats Cute Miner\Assets\Game\Resources\Fonts\` (Bold 98792 B, Regular 106676 B). The **same files are on the hub** at the path above.
  - Also present: `Tiny5-Regular.ttf` (not used by the game) and TMP's `LiberationSans.ttf`.
  - **Only two weights**: Regular and Bold, version 0.4, 1027 glyphs each.
    - Missing glyphs: ★ and ♥.
    - Present glyphs: → × · — …
  - Metrics: units-per-em 1836 = 9 × 204, so one font pixel = 1/9 of the font size (`UiFonts.EmPixels = 9`, `UiKit.cs:25`).
    - Capitals 7 px, x-height 5 px.
    - Ascent 9, descent 2, line pitch about 11 font px; space advance 4 px.
  - Size table (`PixelUi.cs:84-94`): Small 18 (2 px per font px), Body 27 (3), Large 36 (4 = 1 art px), Title 45 (5), Hero 54 (6), Huge 72 (8).
  - License: SIL OFL 1.1, © 2020-2022 GGBotNet, Reserved Font Name "Pixeloid" (`PixeloidSans-OFL.txt`). Rendering it into video is allowed; the reserved name only matters if the font file itself is modified and redistributed.

---

## 3. Logo, title and app icon

None exist:
- No sprite name contains logo, title, splash, app or brand.
- `ProjectSettings.asset` has `productName: Cats Cute Miner` and `companyName: CatMiners` (15-16), `m_SplashScreenLogos: []` (42), and all 44 platform icon slots are empty (`m_Textures: []`, from line 300).
- The web template only has `<title>Cat Miners</title>` and theme colour `#13182c` (`Assets/WebGLTemplates/CatMiners/index.html:6-7`).
- `Docs/Art/Concepts/.../04_pixel_art.png` is an AI concept image with a Russian HUD, not game art; don't use it.

How to compose a logo from existing pieces:
1. Use the "CAT MINERS" wordmark in Pixeloid Bold. At 1 font px = 1 art px it is 68×7 px (caps).
   - Render it on an art-pixel canvas with the gold-button ramp: `gold4` on the top row, `gold3` in the middle, `amber3`/`amber2` at the bottom.
   - Add a 1 px `wood0` outline with `Canvas.outline('wood0', diagonal=True)` and a 1 px `ink1` drop shadow.
2. Put it on a widened `ui_panel_wood` or `ui_window_header` 9-slice.
3. Flank it with `tutor_cat_0` or a posed cat (`cat_body_*_joy*` + `cat_hat_legendary_*` + `cat_pick_legendary_*`) and a pickaxe crossed behind (`icon_item_pickaxe_legendary_big` plus a horizontally flipped copy).
4. Add `icon_coin`, `icon_gem` and `crystal_cluster_a` as accents, `fx_spark_*` as glints, and `fx_glow_warm` behind.
5. Scale the whole thing ×12 with NEAREST: 68 × 12 = 816 px, which fits within 1080.

The same parts make an app-icon-style end-card tile: `ui_slot` frame + `icon_cat_big` + `ui_portrait_frame`.

---

## 4. Recommendations for crisp overlays on 1080×1920 frames

- **Build each overlay at art-pixel resolution first** (crop, 9-slice, compose, add text), then scale once with `Image.NEAREST` by an integer factor:
  - ×4 to match the HUD and windows.
  - ×8 for hero items: tap paw, coins and chests in callouts, end-card cats.
  - ×12–16 only for the logo or wordmark.
- **Don't resize sprites with any other filter**, and don't scale world-captured footage up to "match". The footage grid is 3.375 px per art px and is a separate layer.
- **Snap overlay positions to multiples of 4 px** (at least to even numbers). The yuv420p encode samples colour at half resolution, so odd offsets smear coloured 1 px outlines. Suggested encode: H.264 `-crf 16-18 -tune animation`.
- **Text**: render at size 9 with `fontmode='1'`, then scale with NEAREST. I checked that this gives exactly the same pixels as rendering at size 9k (k = 4, both weights, 0 differing pixels). Use size multiples of 9, at positions that are multiples of k. Tested helper:
```python
def pixel_text(txt, rgb, k=4, bold=True, shadow=(11,10,20)):        # ink1 shadow, 1 font px down
    f = ImageFont.truetype(f'Assets/Game/Resources/Fonts/PixeloidSans-{"Bold" if bold else "Regular"}.ttf', 9)
    w, h = int(f.getlength(txt)) + 1, 12
    m = Image.new('L', (w, h)); d = ImageDraw.Draw(m); d.fontmode = '1'; d.text((0, 0), txt, font=f, fill=255)
    out = Image.new('RGBA', (w, h))
    out.paste(shadow + (255,), (0, 1, w, h), m.crop((0, 0, w, h - 1))); out.paste(rgb + (255,), (0, 0, w, h), m)
    return out.resize((w * k, h * k), Image.NEAREST)
```
- **Match the game's text styles**:
  - Captions: cream (`cream3`) text with the `ink1` shadow on `ui_toast`, `ui_panel_dark` or `ui_window` + `ui_window_header`. Highlight numbers and key words in `gold3`.
  - Calls to action: `ui_btn_gold` (dark `wood0` text, no shadow) or `ui_btn_green` / `ui_btn_blue` (cream text with shadow), at least 28 art px tall.
  - The game never outlines UI text. Over busy footage, put the text on a panel. If it must float, an 8-neighbour 1 font px `ink1` outline plus the shadow still follows the sprite outline rule.
  - The full-screen dim is `ink0` at 0.62.
- **Tap paw**: draw `icon_hand_big` at ×2 UI = 8 px per art px, about 184×176 on screen, so the fingertip sits at +16,+16 px from the sprite's top-left. To copy the game's press loop (`TutorialHint.cs:58-60,671,678-681`):
  - Every 0.1 s, take the next value of the lift sequence `[2,2,2,2,2,1,0,0,1,2]`.
  - Offset the hand by lift × 8 px down and to the right of the target.
  - On lift 0, scale it to 0.9 around the fingertip.
  - Mirror it in X near the right edge.
  - Add `ui_focus_frame` corners that "breathe" by 1 art px every 0.4 s.
- **Mentor cutaway**:
  - Show the `ui_portrait_frame` at ×1 UI (208 px) with the mentor centred inside.
  - After each new line, alternate `tutor_cat_1` and `tutor_cat_0` every 0.15 s for 1.2 s. Blink with `tutor_cat_2` for 0.12 s every 3–5 s.
  - Put the bubble to the right of the portrait: `ui_bubble`, `wood0` text at size 27, at most 190 art px (760 px) wide, padding 7/6 art px.
  - Place `ui_bubble_tail` 12 art px from the bubble's left edge, with the bubble bottom at 40 % of the portrait's height (`TutorialHint.cs:43-51,66`).
- **Background colours** for end cards: `navy0` #0c0f22 (camera background), `navy1` #141a36 (Cards screen), `#13182c` (web template).
- **Caveat**: sections 4–6 of `PIXEL_STYLE_GUIDE.md` and `ART_DIRECTION.md` describe the old merge-mode field (forge, cannon, merge zone). The style rules still apply, but for the current game use the base-camp sprites (`depot_*`, `hire_board_*`, etc.) and the equipment cat layers.