# Reuse map: UI subsystem (Runtime/UI, TutorialController, Core/Text/Loc*)

> **Устарело для карточек (9 октября 2026).** Решение отключить карточки, manager-карты, сундуки и эликсир отменено: система 61 карточки возвращена в режим CatOutpostV2 (ТЗ §10.4, `Docs/Rework/CARD_EFFECT_MAPPING.md`). Пометки DROP/удалить ниже для карточек, `Gacha`, `RewardCycle` (карточки и эликсир), `cards.py`, `HudBlueprints` и `UpgradeCard` больше не действуют; это карта прежнего кода, а не инструкция.

Scope: `Assets/Game/Scripts/Runtime/UI/*.cs`, `Runtime/TutorialController.cs`, `Core/Text/Loc*.cs` (+ `Core/Numbers/NumberFormat.cs`, which every UI string goes through).
Paths below are relative to `Assets/Game/Scripts/`. `file:N` = line number at the time of writing (2026-10-08).
Target: TZ v2.1, sections 5, 9, 10, 14, 15.

## 0. Summary

| Layer | Files | Coupling to old gameplay | Verdict |
|---|---|---|---|
| Pixel kit (factories, components) | `PixelUi.cs`, `UiKit.cs` (`UiFonts`, `Ui`), `PixelBar.cs`, `PixelSlider.cs`, `PixelPop.cs`, `PixelButtonPress.cs`, `SafeArea.cs` | none (only `PixelArt`, `PixelPalette`, legacy `Palette`, `UiSound`) | **REUSE AS-IS** |
| Localization | `Core/Text/Loc.cs` + `Loc.<Section>.cs` | content only (keys for merge/cards/forge) | engine **REUSE AS-IS**; dictionaries **ADAPT** |
| Number format | `Core/Numbers/NumberFormat.cs` | takes `GameNumber`/`double` | **REUSE**, add an overload for the new exact integer money type |
| Tutorial view | `TutorialHint.cs` | target-name constants only | **REUSE**, swap the `Target*` constants |
| HUD shell | `Hud.cs` (Create/Build/Insets/Toast/FlyLoot/Transition/Update throttle) | strong: `GameController`, `GameSession`, `CommandResult`, `LootState` | **ADAPT** (keep the skeleton, rewrite every `Refresh*`) |
| Windows | `HudWindows.cs` (modal queue, settings, campaign end) | medium: `ChestResult`, `OfflineSummary`, `GameSession` settings | modal + settings **ADAPT**; chest/offline/campaign end **ADAPT** into "Mine results"/new offline |
| Cards screen | `HudBlueprints.cs` | total (cards, elixir, gacha, free chest) | **DROP**; copy its scroll grid + cell pattern into Equipment |
| Context panel | `HudContext.cs` | total (generators, gates, exit, merge text) | **ADAPT** shell (portrait + title + body + 3 buttons) for cat/target info, **DROP** the content |
| Edge arrows | `EdgeIndicators.cs` | strong (`ObjectKind/ObjectStatus`, containers, payouts) | **ADAPT** or drop (the "To base" button covers the main need) |
| Tutorial logic | `TutorialController.cs` | total (barrels, drag, merge, forge, zone1–5 steps) | **DROP** the steps; keep the scaffolding pattern |

No Back-button handling exists anywhere (grep for `Escape`/`cancel`/`Back` in Runtime finds nothing), and no tab factory or scroll-list factory exists. TZ §14 requires both Back handling and gesture cancel when a modal opens. That code has to be written from scratch.

---

## 1. How the canvas is built (in code)

The whole HUD is built in code. There are no prefabs. Entry point: `Hud.Create(GameController game)`, `Runtime/UI/Hud.cs:197`, called from `GameController.cs:134` (`Hud = Hud.Create(this)`). `TutorialController` is created right after it (`GameController.cs:138`).

- `FindSceneCanvas()` `Hud.cs:230` reuses the root Canvas named exactly `"HUD"` (created by `Editor/ProjectSetup.cs:277 CreateHudCanvas`, with a `SafeArea` child), but only if it has no `Hud` component. Otherwise it creates `new GameObject("HUD")`.
- In both cases it forces: `RenderMode.ScreenSpaceOverlay`, **`sortingOrder = 30000`** (ProjectSetup writes 10, and runtime overrides it so the HUD stays above field sprites in Screen-Space-Camera screenshots), `CanvasScaler` ScaleWithScreenSize **1080×1920, match 0.5** (`Hud.cs:28` `RefWidth/RefHeight/RefMatch`), `GraphicRaycaster`.
- `Build(RectTransform root)` `Hud.cs:243`: reuses `root.Find("SafeArea")` or creates one, then ensures the `SafeArea` component. **The order of SafeArea children is the layer order.** The current order: TopBar → NextMine → Bottom → Context → EdgeIndicators → Toast → TutorialHint → LootFly → CampaignEnd → Blueprints → Settings → Modal. The dynamite ghost sits on the canvas root.
- Unit system: **1 art pixel = 4 canvas units** (`PixelUi.Px = PixelArt.UiPixel = 4`). All sizes are `N * Px`.
- Style-guide rule (`Docs/Art/PIXEL_STYLE_GUIDE.md` §6.4): each new window lives in its own partial file `Hud<Feature>.cs` with a `Build<Feature>()` method. Call it from `Hud.Build` **after `BuildSettings()` and before `BuildModal()`** so the modal stays on top.
- EventSystem: `GameController.EnsureSceneBasics` (`GameController.cs:172`) adds `EventSystem` + `InputSystemUIInputModule` (`AssignDefaultActions()` if no asset). ProjectSetup assigns the `UI` map of `CatMinersInput`, **including the `Cancel` action** (`ProjectSetup.cs:259`). That action can drive Back, but nothing listens to it today.

`SafeArea` `Runtime/UI/SafeArea.cs:8`: `Update()` (`:12`) sets anchors from `Screen.safeArea` only when it changes. **Gotcha:** the file name must equal the class name. The component is serialized in `CoreGame.unity`, and a mismatch would load as "Missing script". The same rule applies to every new MonoBehaviour.

---

## 2. PixelUi kit API (REUSE AS-IS)

`Runtime/UI/PixelUi.cs`. Static, gameplay-free. All factories are called at build time. In `Update`, only setters run, and only when the value changed (style guide §6.3).

### 2.1 Enums and constants
- `enum PixelPanel { Dark, Wood, Inset, Light, Window, Header, Toast, Pill, Bubble, Slot, Focus, Dashed }` `:10`
- `enum PixelButton { Gold, Green, Blue, Red, Dark }` `:39`. Gold = the single main action of a screen, Green = confirm/collect, Blue = secondary, Red = danger, Dark = neutral.
- `enum PixelBarFill { Gold, Green, Purple, Red }` `:49`
- `enum PixelRarity { Common, Uncommon, Rare, Epic, Legendary }` `:58`. Frames are `ui_card_frame_<rarity>` (all five exist in the atlas). TZ equipment uses common/rare/epic/legendary, so skip Uncommon or add an explicit mapping. `RarityOf(int coreRarity)` `:220` maps the OLD Core `Rarity` (1..6), so do not use it for equipment.
- `const float Px` `:78`. Fonts (Tiny5; the font pixel is 1/8 of the size): `FontSmall=24` `:82`, `FontBody=32` `:84`, `FontLarge=48` `:86`, `FontTitle=56` `:88`, `FontHero=64` `:90`, `FontHuge=88` `:92`.
- Colors `:95-115`: `TextColor, TextDim, TextAccent, TextAccentLight, TextDark, TextBad, TextGood, TextElixir, TextIncome, ShadeColor, DisabledIconTint`. Rich-text tags: `DimTag` `:118`, `AccentTag` `:120` (Hud also has private `BadTag` = red5 at `Hud.cs:62`).
- Sizes: `ButtonHeight = 28*Px` `:123`, `SmallButtonHeight = 20*Px` `:125` (buttons inside list rows, which is what Base rows need), `SlotSize = 40*Px` `:127`, `BadgeCornerOffset` `:129`.

### 2.2 Factories (exact signatures)
```csharp
public static Sprite SpriteOr(string name, string fallback = null)                                   // :138
public static string PanelSprite(PixelPanel style) / ButtonSprite(PixelButton style) / ButtonPressedSprite(PixelButton style)  // :144 :163 :175
public static Color  ButtonTextColor(PixelButton style); public static bool ButtonTextShadow(PixelButton style)                // :178 :181
public static string BarFillSprite(PixelBarFill fill); public static string CardFrameSprite(PixelRarity rarity)                // :196 :207
public static float  Inset(PixelPanel style)                     // border of the 9-slice, in canvas units   :228
public static float  Snap(float units); public static Vector2 Snap(Vector2 units)                    // :232 :234
public static Image  Sliced(Transform parent, string name, string sprite)                            // :239
public static void   SetSliced(Image img, string sprite)                                             // :247
public static Image  Panel(Transform parent, string name, PixelPanel style)   // raycastTarget=true (blocks field) except Focus/Dashed  :260
public static void   SetPanel(Image img, PixelPanel style)                                           // :267
public static Image  Icon(Transform parent, string name, string iconName, int scale = 1)  // centred, pivot from atlas; NEVER stretch via anchors :274
public static void   SetIcon(Image img, string iconName, int scale = 1)                              // :286
public static Vector2 IconSize(string iconName, int scale = 1)                                       // :308
public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
    TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = false, bool shadow = false) // wrap on, ellipsis, no raycast :317
public static void   SetFont(TMP_Text text, bool bold, bool shadow); public static void SetShadow(TMP_Text text, bool on)   // :334 :345
public static Image  Divider(Transform parent, string name)        // ui_divider, 3 px high               :354
public static Image  Shade(Transform parent, string name)          // full-stretch, blocks raycasts       :363
public static Ui.ButtonRefs Button(Transform parent, string name, string label, PixelButton style, UnityAction onClick, float fontSize = FontLarge) // :380
public static void   ApplyButtonSprites(Button btn, Image img, PixelButton style)                    // :404
public static Ui.ButtonRefs IconButton(Transform parent, string name, string iconName, UnityAction onClick, int iconScale = 1) // ui_slot + icon + Label + Badge :426
public static PixelBadge Badge(Transform parent, string name, int scale = 1)                          // :457
public static string CountText(int n)                              // 0..99 cached, else "99+"; no allocation  :471
public static PixelSlider Slider(Transform parent, string name, float value, UnityAction<float> onChanged) // :481
public static PixelToggle Toggle(Transform parent, string name, bool on, UnityAction<bool> onChanged, int scale = 2) // :485
public static PixelBar Bar(Transform parent, string name, PixelBarFill fill = PixelBarFill.Gold)      // min height 8 px art  :504
public static PixelIconText IconText(Transform parent, string name, string iconName, string text, float fontSize, Color color,
    bool bold = true, bool shadow = true, int iconScale = 1)       // HorizontalLayoutGroup + ContentSizeFitter  :521
public static PixelCard Card(Transform parent, string name, PixelRarity rarity, string artIcon, UnityAction onClick = null) // currently UNUSED in game :555
public static PixelWindow Window(Transform parent, string name, string title, UnityAction onClose = null)  // :591
```

### 2.3 Component classes
- `PixelBadge` `:652`: `Image`, `Label`, `Rect`, `Visible`, `SetCount(int n)` `:668` (≤0 hides it), `SetText(string)` `:677`, `PlaceAtCorner(Vector2 corner, float inset)` `:686`.
- `PixelToggle` `:695`: `Button`, `Image`, `UnityAction<bool> OnChanged` `:700`, `IsOn` `:711`, `SetOn(bool)` `:715` (silent), internal `Flip()` `:722` (calls OnChanged first, then the click sound).
- `PixelIconText` `:732`: `Root`, `Icon`, `Text`, `SetText(string)` `:747`, `SetIcon(string)` `:749`. Use it for prices.
- `PixelCard` `:753`: `Frame, Button, Art, Title, Level, Progress`, `SetRarity` `:782`, `SetArt(string, int scale = 2)` `:789`, `SetTitle` `:790`, `SetLevel` `:791`, `SetProgress(float)` `:793` (<0 hides it), `SetFaceDown(bool)` `:800`. **Good base for equipment collection cells** (rarity frame + art + level + parts bar + "not owned" back).
- `PixelWindow` `:817`: `Root` (shade, raycast-blocking), `Shade`, `Frame`, `Header`, `Title`, `Body`, `BodyText`, `Buttons` (HorizontalLayoutGroup, equal widths), `Close` (null without onClose). `Box` `:846` (size the frame with `Box.Anchor(...)`; the default is 6–94 % width and 25–75 % height), `Visible` `:847`, `Show()` `:850` (SetAsLastSibling + open sound; the PixelPop on Box replays on enable), `Hide()` `:859`, `SetTitle` `:866`, `SetBody` `:867`, `AddButton(string name, string label, PixelButton style, UnityAction onClick, float fontSize = PixelUi.FontLarge)` `:870`, `ClearButtons()` `:878` (Destroy; prefer reuse plus SetActive), `UpdateBodyArea()` `:893` (call after toggling buttons by hand).

### 2.4 `UiKit.cs`
- `UiFonts` `:19`: `Default` `:40`, `Bold` `:50` (the same Tiny5 asset; no fake bold), `IsPixel` `:60`, `HasRealBold` `:70`, `Legacy` `:80` (TMP default, fallback for → ★ etc.), `DefaultShadow` `:97`, `BoldShadow` `:100`, `Shadow(TMP_FontAsset)` `:106`.
- `Ui` `:193` (legacy plus shared helpers):
  - `RectTransform Rect(Transform parent, string name)` `:288`
  - **`RectTransform Anchor(this RectTransform rt, float xMin, float yMin, float xMax, float yMax, Vector2 offsetMin = default, Vector2 offsetMax = default)`** `:295` (used everywhere)
  - `Image Panel(Transform, string, Color, bool rounded = true)` `:309`, `bool PanelStyleFor(Color, out PixelPanel)` `:319`, `PixelButton ButtonStyleFor(Color)` `:336` (legacy colour→style)
  - `TextMeshProUGUI Text(Transform, string, string, float, Color, TextAlignmentOptions = Center)` `:358` (legacy)
  - **`sealed class ButtonRefs`** `:368`: fields `Button, Image, Label, Content, Icon, Badge, Style, IsSlot, Color`. `Rect` `:396`, `Interactable` `:397`, `SetInteractable(bool)` `:408`, `SetStyle(PixelButton)` `:420`, `SetLabel(string)` `:429`, `SetActive(bool)` `:432`, `SetIcon(string iconName, int scale = 1)` `:435` (creates the icon lazily inside Content), `SetBadge(int)` `:450` (creates the badge lazily). All setters are no-ops when nothing changed.
  - `ButtonRefs Button(Transform, string, string, float fontSize, Color, UnityAction)` `:488` and `Image Icon(Transform, string, Color, float size)` `:500` (legacy, do not use in new code)
  - `VerticalLayoutGroup VList(RectTransform rt, float spacing, int padding)` `:526`, `LayoutElement Layout(Component c, float preferredHeight, float flexibleWidth = 1)` `:538`, `void Clear(Transform)` `:548` (Destroy)
  - **`void SetText(TMP_Text, string)`** `:554` (only assigns when the text differs, which avoids a TMP mesh rebuild), **`void SetActive(Component, bool)`** `:561`, **`void AutoSize(TMP_Text, float min, float max)`** `:568`
  - `Rounded/Circle/Triangle` `:200-277` are obsolete vector sprites. DROP (unused by the pixel style).

### 2.5 Small components
- `PixelBar` (`PixelBar.cs:12`, MonoBehaviour): `Frame` `:24`, `Fill` `:25`, `Rect` `:26`, `Value` `:27`, `FillStyle` `:28`, `SetValue(float)` `:43`, `SetFill(PixelBarFill)` `:51`. The fill snaps to whole art pixels, is never 0 when value>0 and never full when <1, and re-applies on rect resize `:58`.
- `PixelSlider` (`PixelSlider.cs:15`): `UnityAction<float> OnChanged` `:27`, `Value` `:28`, `Rect` `:29`, `Slider` `:30`, `internal static Create(...)` `:32`, `SetValue(float)` `:62` (silent), click on pointer-up `:76`.
- `PixelPop` (`PixelPop.cs:10`): `Duration=0.15f`, `From=0.9f`, plays on `OnEnable` `:17`, `Play()` `:20`, unscaled time.
- `PixelButtonPress` (`PixelButtonPress.cs:13`): sinks `Content` by 1 px while pressed and plays `UiSound.PlayDenied` on a tap of a button that was disabled at press time (`OnPointerClick` `:56`). Added automatically by `Button`/`IconButton`.

**Missing in the kit (must be written):** tabs (build them as a row of `PixelUi.Button` and switch Blue/Dark through `SetStyle`), a scroll-list factory (copy `HudBlueprints.cs:130-157`), a segmented ×1/×10/Max control (same as tabs), and Back handling.

---

## 3. Modal and window system (`HudWindows.cs`, ADAPT)

- `public struct ModalButton { string Label; Color Color; Action Action; PixelButton? Style; }` `:32`. Set `Style`. `Color` is the legacy path (`ModalStyle` `:257` maps Palette colours; GameController still passes `Color = Palette.UiPanelLight` at `GameController.cs:1104`).
- `struct ModalRow { string Icon; string Text; Color Color; }` `:41` and **`sealed class ModalRequest { Title, Body, Icon; ModalRow[] Rows; ModalButton[] Buttons; }` `:49` are PRIVATE.** The only public entry is `public void Modal(string title, string body, params ModalButton[] buttons)` `:100`. Icon and rows are reachable only from inside `Hud` (`ShowChest` `:277`, `ShowOffline` `:301`). **ADAPT:** expose rows and icon (make `ModalRequest` public, or add an overload) so "Mine results" (cleared %, item granted, parts, reset explanation) can reuse it.
- Behaviour: **one** reusable `PixelWindow m_ModalWin` (`BuildModal` `:90`). If a modal is already open, the request goes into a FIFO `Queue<ModalRequest>` (`Modal(ModalRequest)` `:103`). There is no stacking. `OpenModal` `:109` reuses the buttons (all named `"btn"`; PlayMode tests find them by that name in `PlayModeUtil.FindModalButton`, `Tests/PlayMode/PlayModeUtil.cs:64`; keep the name). `OnModalButton` `:142` captures the action **before** `CloseModal` `:150`, because closing may immediately open the next queued request.
- `LayoutModal` `:162`: height follows the content (icon ×2, body text measured with `GetPreferredValues`, rows 18 px each, shrinking to 12 px) and is capped at 86 % of the safe area. When content overflows, the text autosizes. The height is rounded to an even number of art pixels. `Hud.Update` re-lays out when the safe-area size changes (`Hud.cs:826`).
- Other "windows" are independent sibling roots, not part of the queue: Settings (`m_Settings`), Blueprints (a full-screen root with its own card `PixelWindow`), CampaignEnd (`m_End`). Z-order comes from `PixelWindow.Show()` calling `SetAsLastSibling`. `ShowCampaignEnd` `:510` forces the modal back on top (`m_ModalWin.Root.SetAsLastSibling()`). Hint, Toast and LootFly also call `SetAsLastSibling`, so they can draw over windows. That is why TutorialController hides the hint whenever `AnyWindowOpen` is true.
- State queries (`Hud.cs`): `ModalOpen` `:154`, `BlueprintsOpen` `:156`, `SettingsOpen` `:158`, `CampaignEndVisible` `:160`, **`AnyWindowOpen` `:165`** (used by EdgeIndicators, FlyLoot and TutorialController). **Every new window (Base, Equipment) must be added to `AnyWindowOpen`.**
- **Input blocking:** the full-stretch shade `Image` of each window has `raycastTarget=true`. `PointerInput.IsOverUi` (`PointerInput.cs:160`, `EventSystem.RaycastAll`) runs **only at gesture start** (`:250`). A drag or scroll already in progress when a modal opens is NOT cancelled. TZ §14 requires the cancel, so add a hook on window open (for example `m_Game.Input.CancelGesture()`). The simulation keeps running under modals: there is no pause.
- **Back button: not implemented.** Implement a window stack (top-most visible: card/detail → modal → Equipment/Base → Settings → selection clear) and poll the `UI/Cancel` action (or `Keyboard.current.escapeKey`; on Android, Back maps to Escape in the Input System). Per TZ, Back must not skip completion conditions.

### Settings window (ADAPT the wiring, reuse the UI)
`BuildSettings` `:331` builds 7 rows (`SettingsRowHeight = 26*Px` `:26`): 0 Sound toggle, 1 Master slider, 2 Music, 3 Sfx, 4 Ambience, 5 Vibration toggle, 6 Reduce effects toggle. Row helpers:
`Image SettingsRowBase(int index, string icon, string label, out Image iconImage, float rightReserve)` `:350` (Inset panel + icon + FontLarge label; **the template for Base rows**), `PixelToggle SettingsRow(int, string, string, UnityAction<bool>, out Image)` `:365`, `PixelSlider VolumeRow(int, string, string, AudioGroup)` `:375`.
`ShowSettings()` `:389`, `HideSettings()` `:398`, `RefreshSettings(GameSession)` `:400` (cached ints). Handlers: `OnVolume` `:424` → `GameController.SetVolume(AudioGroup, float)` (`GameController.cs:380` → `Session.SetVolume`, `AudioMix.Apply(Player.Settings)`), `OnReduceEffectsToggle` `:429` → `SetReduceEffects` (`:389`, sets `VisualSettings.ReduceEffects`), `OnSoundToggle` `:435` → `ToggleSound` (`:1023`), `OnVibrationToggle` `:442` → `Hud.ToggleVibration` `Hud.cs:1098` → `GameSession.SetVibrationOn`.
**Coupling:** the settings live in `GameSession.Player.Settings` (`PlayerSettingsState`, `Core/Sim/GameState.cs:73`; `AudioGroup` enum `:60`). The new profile needs the same `PlayerSettingsState` and a facade with the same 4 controller methods. The current set already covers the TZ list (music, SFX, ambience, vibration, reduce effects). Master and Sound can stay.

### Campaign end, chest, offline
`ShowChest(ChestResult, Action onClaim = null)` `:277` → DROP (old gacha). `ShowOffline(OfflineSummary)` `:301` → ADAPT to the new offline summary, keeping the `AddRow(list, icon, text, color)` `:326` pattern. `BuildCampaignEnd/LayoutCampaignEnd/ShowCampaignEnd(string title, string body)/HideCampaignEnd()` `:451/:474/:510/:524`: the full-screen window with a star is reusable for an end screen. The `m_EndChests` button is old.

---

## 4. HUD layout (`Hud.cs`, ADAPT)

Constants `Hud.cs:31-57`: `Margin=2px`, `TopBarHeight=30px`, `TopUnits = Margin+TopBarHeight`, `BarHeight=52px`, `BarPad=5px` (border of ui_panel_dark), `SlotGap=3px`, `StripHeight=24px`, `PillHeight=16px`, `BottomUnits = Margin+BarHeight+Margin+StripHeight`, `PlaqueFraction=0.26`, `GoldSegment=0.355`, `ElixirSegment=0.655`.

- **Top bar** `BuildTopBar` `:304`: a Wood plaque "Шахта N" (+ sector line, `SetZoneLayout` `:380`), then a Dark bar with segments built by `static RectTransform Segment(RectTransform bar, string name, float from, float to, string icon, out float textLeft)` `:353` (icon on the left, text to its right) and `Divider(bar, at)` `:365`, then the gear `IconButton` → `ShowSettings` `:344`. Segments: gold+income | elixir | cats. **New mode:** gold (delivered wallet) | undelivered cargo indicator (TZ: separate label and look) | progress destroyed/required | workers. Drop elixir. `NumberText(TMP_Text)` `:373` = no-wrap + autosize.
- **Next-mine button/reason** `BuildNextMine` `:393`: Green button and Dark "reason" button under the top bar. ADAPT to the `ReadyToContinue` button/phase.
- **Bottom** `BuildBottom` `:408`: the `m_Bottom` container holds a Dark bar with slots placed by `static void PlaceSlot(RectTransform rt, int index, bool right)` `:460` (`IconButton` at `SlotSize`): chests and dynamite on the left, Blueprints on the right, and the wide Gold Hire button (`BuildHire` `:473`: title, coin price via `IconText`, level, `PixelBar` gauge inside `Content`, so everything sinks together). Above it, the strip holds the free-cat pill and the idle-cats button. **New mode:** slots "База" (Gold main, or a wide button like Hire with an affordable-count badge), "Экипировка" (IconButton + badge), "К базе" (IconButton). Drop hire, dynamite, chests, blueprints, free cat and idle cats.
- **Insets:** `TopInset` `:135` and `BottomInset` `:145` (fractions of safe-area height, `UpdateInsets` `:731`, `ScreenSafeUnits` `:742` fallback formula equal to the CanvasScaler one), `FieldBandPixels(out float bottom, out float top)` `:172`, `PushInsets` `:753` → `CameraRig.SetUiInsets(top, bottom)`. **Keep this API:** CameraRig, PointerInput (`:180`, `:330`), EdgeIndicators and TutorialHint (`SetReserved(BottomUnits, TopUnits)` `:275`) depend on it. If bar heights change, change only the constants.
- **Toast** `BuildToast` `:497`, **`public void Toast(string text, float seconds = 2.2f)`** `:513`: auto-height, `SetAsLastSibling`, unscaled timer. REUSE as-is.
- **Loot fly** `BuildLootFly` `:591`, `public static int LootFlyCount(GameNumber amount)` `:619`, **`public void FlyLoot(Vector3 worldPos, string currency, GameNumber amount)`** `:623`, `public void FlyLoot(Vector3 worldPos, string currency, int count)` `:631`: pool of 24 icons, quadratic arc, counter "pulse", no allocation. Coupling: `FlyTarget(string)` `:607` switches on `LootState.Gold/Elixir/Hard` and targets the top-bar icons. **ADAPT:** in the new mode, fly coins on **deposit** (`CargoDeposited`) to the gold icon, not on pickup.
- **Transition** `InTransition` `:782`, `BeginTransition()` `:788`, `SetTransitionFade(float k)` `:805` (8 alpha steps, navy0), `EndTransition()` `:814`. REUSE for the stage transition.
- **Update/Refresh:** `Update` `:821` hides the toast, advances the loot fly, re-lays out modals, and calls **`Refresh()` at most 10 Hz** (`m_NextRefresh += 0.1f`). `Refresh()` `:839` reads `m_Game.Session` (old `GameSession`) and calls `RefreshTopBar/Hire/Strip/BottomButtons/NextMine/Chests/Context/BlueprintsHeader/Settings`. `ResetCaches(GameSession)` `:867` triggers when `s.Mine` reference changes.
- **Value cache pattern (REUSE):** `struct ValueKey : IEquatable<ValueKey> { int A..H; long L; GameNumber N1..N4; }` `:67`. Fill it with the source values, compare to the last key, and rebuild strings only on change (TZ 15.3 rule, "no TMP rebuild per frame"). A shared `StringBuilder m_Sb` `:114` serves for composition.
- Dynamite ghost `ShowDynamiteGhost/MoveDynamiteGhost/OnDynamitePointerDown` `:549/:555/:538` → DROP.
- Geometry helpers for arrows: `TryGetContextTop(out float)` `:1116`, `TryGetTopObstacleBottom(out float)` `:1128`, `UiCamera` `:191`.
- `Hint` `:185` (TutorialHint) and `WireTutorialHint()` `:771`, called from `Start()` `:768` and every `Refresh()`. The controller is created after the Hud.

**Old-gameplay coupling in Hud.cs (cut list):** `CreateActions` `:291` (ActivateShaft, OpenGate, Upgrade, UpgradeToThreshold, CollectPayout, RequestNextMine), `RefreshTopBar` `:890` (`s.Mine.Gold`, `s.Player.Elixir`, `AutoIncomePerSecond`, `OccupiedSlots/UnitCap`, `Layout.SectorCount`, `GatesOpened`), `RefreshHire` `:930` (`HirePrice/CanHire/CommandResult/FindSpawnCell/HireLevel/HireGauge`), `RefreshStrip` `:974` (`ContainerKind.FreeCat`, `FreeCatTimerTicks`, idle cats), `RefreshBottomButtons` `:1007` (Dynamite, `UpgradeableCards`, `FreeChest`), `RefreshNextMine` `:1029` (`Layout.Exit`, `ProducingShafts` `:879`, `CanCompleteZone`, `NearestInactiveShaft`), `RefreshChests` `:1057` and `OnChestsBadgeClick` `:1080` (`ChestReveals`), `OnChestsBadgeTap` `:188`. Generic helper worth keeping: `static long CeilSeconds(double)` `:887`.

---

## 5. Context panel (`HudContext.cs`, ADAPT shell / DROP content)

TZ §14 "Сведения о котике/цели" maps directly onto this panel.
- Shell: `BuildContext` `:47` builds a Dark panel above the bottom (`CtxHeight=91px`, `CtxHeightNoButtons=64px` `:23`), a portrait `Slot` (`PortraitSize=38px` `:25`) with 4 icon layers, a FontLarge accent title, a body text, and a row of 3 equal-width buttons (`HorizontalLayoutGroup`). `SetContextButtons(bool)` `:94` shrinks the panel when there are no buttons. `HideContext()` `:106`. Diff-apply buttons: `Want(int i, string label, bool enabled, Action action, PixelButton style)` `:112` + `ApplyCtxButtons()` `:120`. Actions are allocated once and read `m_CtxOrd` at click time (`:45`).
- `SetCatPortrait(CatState)` `:221` uses `CatLook.Body/Helmet/WeaponSprite` with old per-cat level (helmet colour by level, weapon by level). **ADAPT** it to the team equipment look (TZ 10.1: the visible kit comes from equipped items).
- `SetPortrait(string sprite)` `:210`, `SetLayer` `:233`, `static Vector2 CenterOffset(string sprite, int scale)` `:242` (centres a trimmed atlas sprite) are REUSE.
- Content to DROP: `RefreshContext` `:139` (`SelectedOrdinal/SelectedCatId`, `FindCat`, `EtaSeconds`), `ObjectKey` `:248`, `CatContext` `:286` (the merge hint text `hud.cat.idle`), `ObjectTitle/ObjectContext/GeneratorInfo` `:305/:312/:380`, `GateBonusLabels(GameSession)` `:439` (public, used by GameController `:966`). The new cat view shows: task, bag fill (`PixelBar`), cargo value; no buttons. The new target view shows: HP bar, type, workers, remaining loot, unavailability reason.
- **Tutorial target:** `TargetContextAction` is registered to `m_CtxButtons[0]` (`Hud.cs:273`).

---

## 6. Cards screen (`HudBlueprints.cs`, DROP, mine its patterns)

Everything here is old (cards, elixir, gems, free chest, `CanUpgradeCard`, `PresentationCatalog.Card*`). Patterns worth copying into Equipment:
- **Full-screen root instead of a PixelWindow:** `BuildBlueprints` `:74`: `Ui.Rect(m_Safe,"Blueprints").Anchor(0,0,1,1)`, a `Navy1` Image with `raycastTarget` that blocks the field, plus a `PixelPop`.
- **Header:** a Wood panel with a Blue "sort" square button and a Red "close" square button (`static void PinSquare(RectTransform rt, float x, float size, float inset = 0f)` `:164`), title + subtitle. A currency row uses `IconText` + `static void PinTopLeft(RectTransform rt, float x)` `:173`.
- **Scroll grid (no factory, copy it):** `:130-157`: a viewport `RectMask2D` + **transparent raycast Image** (so drags between cells scroll), a content rect anchored at the top with pivot (0.5,1), a `GridLayoutGroup` (`FixedColumnCount`), `ContentSizeFitter.verticalFit=PreferredSize`, a `ScrollRect` (vertical, `Clamped`, `scrollSensitivity=30`), and an "empty" label. `FitCardCells()` `:369` recomputes the cell width in whole art pixels on resize.
- **Cell reuse:** a `List<CardCell>`. `RebuildBlueprints()` `:338` creates cells lazily, re-sorts through `SetSiblingIndex`, and hides the extras. The grid is rebuilt only when a signature changes (`CountCards` signature hash `:244`, `RefreshBlueprintsHeader` `:291`).
- **Cell layout** `CreateCardCell(Transform parent, bool tap)` `:398` (80×106 px): rarity frame, title, layered portrait (`Layer` `:475`), effect tag, level, `PixelBar` progress with a count overlay, a card icon or upgrade arrow (`icon_upgrade`), and a face-down state (`SetFaceDown` `:482`, `FaceDownTint` `:36`). For equipment: frame = rarity, art = item icon ×2, level "ур. N/20", bar = parts toward the next level, arrow when it can be upgraded, face-down/locked when not owned.
- **Detail window** `BuildCardWindow` `:181`: a `PixelWindow` inside the screen root, the big cell on the left and info text on the right, and a tall (36 px) Green upgrade button with an `IconText` price **under** the label (`:197-208`, `FillCardWindow` `:586-640`: label/price anchoring and a red price when short). **Copy this pattern for the item detail: "сейчас → после замены", "Улучшить" with a parts price, "Надеть".**
- The open/close toggle `ToggleBlueprints()` `:211` shows the root **before** creating cells, so new TMP texts initialise in an active hierarchy.

---

## 7. Edge indicators (`EdgeIndicators.cs`, ADAPT or DROP)

`internal void Init(GameController game, Hud hud)` `:94`, `public void ClickArrow(int index)` `:102`, `RequestRefresh()` `:109`, `ActiveArrowCount` `:92`, `public static void CameraScrollRange(CameraRig rig, float boardDepth, out float minZ, out float maxZ)` `:115`.
Generic, reusable parts: 0.2 s rebuild (`UpdateInterval` `:28`), a pool of 12 chips (`IconButton` 24 px + pointer icon), merging overlapping arrows into a counter badge (`Rebuild` `:166`), 6-direction sprites without rotation (`ArrowDirection` `:423`, `ApplyDirection` `:431`), "only if a tap can bring it into view" (`AddCandidate` `:319`, `InBand` `:343`), and hiding under windows or drags (`ShouldHide` `:150`, which uses `IsDraggingCat/IsDraggingDynamite`, both old).
Old coupling: `CollectCandidates` `:270` (`ObjectKind.Shaft/Gate/Exit`, `ObjectStatus.ReadyToActivate/Broken`, `Gen.PayoutReady`, `ContainerKind.Delivery/FreeCat`), `InOpenSector` `:307` (`Layout.SectorOfRow/CellSector`, `IsSectorOpen`), and `m_Game.Board.ObjectCenter/CellCenter`. In the new mode there is nothing for the player to act on in the field, so the "К базе" button may be enough. If the component is kept, rewrite only `CollectCandidates` (for example: the base when it is off-screen, a newly opened gate). The `hud.edge.*` keys in `Loc.Hud.cs:164-169` are **unused**.

---

## 8. Tutorial system

### 8.1 `TutorialHint` (`Runtime/UI/TutorialHint.cs`, REUSE)
Created by `public static TutorialHint Create(RectTransform safeArea)` `:141` (a child of SafeArea; `CanvasGroup.blocksRaycasts=false`, **it never blocks input** `:154`).
- Display modes (`enum Mode { None, Ui, World, Drag }` `:34`):
  - **Bubble only:** `Show(text, null)`, where the dialog sits at `DefaultDialogY = 0.62` of the safe area `:53`.
  - **UI arrow/hand + focus frame:** `public void Show(string text, RectTransform uiTarget)` `:263`. Resolve the target with `FindTarget(id)`. If the target is inside a HUD block (a direct child of SafeArea), the dialog goes above the whole block (`BlockOf` `:452`, `LayoutDialog` `:532`).
  - **World point:** `public void ShowWorld(string text, Vector3 worldPos)` `:272`, `public void ShowWorld(string text, Vector3 worldPos, Vector2 cells)` `:275` (focus frame over the footprint), `public void MoveWorld(Vector3 worldPos)` `:285` (moves without re-popping; for a moving cat).
  - **Drag gesture** (old merge/assign): `ShowDrag(...)` `:296/:299`, `MoveDrag` `:311`. **Not needed** in the new mode (no manual drags).
  - **Highlight/dim:** `public bool Dim { get; set; }` `:126` (4 shade rects around the target, visual only).
- `RegisterTarget(string id, RectTransform target)` `:240`, `FindTarget(string id)` `:248`, `SetReserved(float bottom, float top)` `:255`, `Hide()` `:320`, `Visible` `:117`, `Text` `:119`, `Rect` `:120`.
- Visuals: mentor portrait `ui_portrait_frame` + `tutor_cat_0/1/2` (talk 1.2 s, blink every 3–5 s; `UpdateMentor` `:409`), `ui_bubble` + `ui_bubble_tail` with max width 190 px (`MeasureBubble` `:360`), hand `icon_hand_big ×2` with a press cycle (`s_Press` `:61`, `LayoutPointer` `:615`, mirrored at the right edge), and a breathing `ui_focus_frame`. `LateUpdate` `:380` repositions without allocating and moves elements only on change.
- **ADAPT:** the target constants `:24-31` (`TargetHire, TargetBlueprints, TargetDynamite, TargetChests, TargetFreeCat, TargetNextMine, TargetContextAction`) are old. Add `TargetBase`, `TargetEquipment`, `TargetToBase`, `TargetGold` (TZ 15.3 "highlight the change in delivered gold"), per-row Base targets (for example `base.pickaxe`, `base.team`), `TargetEquipBest`, and register them in the new Hud build. `WorldRect` `:471` uses `Camera.main` and assumes a top-down camera (world X → screen right, Z → screen up). Re-check this if the new camera differs. The comment on `DefaultDialogY` mentions the "merge zone"; it is cosmetic.

### 8.2 `TutorialController` (`Runtime/TutorialController.cs`, DROP the steps, keep the pattern)
A plain class, not a MonoBehaviour. `ctor(GameController)` `:69`, `TutorialHint Hint {get;set;}` `:77` (null → falls back to `Hud.Toast(hint, 4.5f)`), `CurrentHint` `:80`, `OnEvent(SimEvent e)` `:93` (called by `GameController.DrainEvents`, `GameController.cs:579`), `Update()` `:130` (called from `GameController.cs:475`).
Reusable scaffolding:
- Persistence: `Done(id)` `:82` / `Complete(id)` `:84` on `Session.Player.TutorialDone` (`List<string>`, `GameState.cs:47`) + `MarkDirty()`. The new profile needs an equivalent list.
- Evaluation every 0.25 s (`:140`). Under any window, the hint hides in the same frame (`:135`).
- `Evaluate()` `:381` returns the text and sets the target through `AtUi(text, target, dim)` `:216`, `AtObject(text, ordinal)` `:225`, `AtCell` `:238`, `AtLoot` `:250`, `AtDrag` `:262`, `AtDragCells` `:277`. The world variants depend on `BoardView.ObjectCenter/CellCenter/LootPos` and `ZoneLayout`, so rewrite them as `AtWorld(text, Vector3, Vector2 cells)` + `AtCat(catId)`.
- `ShowOnView` `:167` diffs text and target and only moves the pointer when the world target moves. REUSE as-is.
- One-shot hints: `OnceAtObject(id, text, ordinal)` `:486` (about 5 s on screen, then marked done). Good for TZ step 1 "auto-mining explained" and step 6 "cats choose targets themselves".
- Everything else is old gameplay: steps t1–t11 (`:396-461`), `FindMergePair` `:305`, `HireBarrelCell` `:331`, `FindDragTarget` `:343`, `ForgeUpgradeHint` `:294`, `ExitHint` `:469`, `ZoneStepHint` `:539`, `ContainerHint` `:590`, and the `SimEventType.ContainerOpened/LootCollected/PayoutCollected/CatMerged/GeneratorUpgraded` handlers. The TZ §15 steps (9 of them) map onto new events: `CargoPickedUp` (bag), `CargoDeposited` (gold highlight with `AtUi(..., TargetGold)`), `TeamUpgradePurchased`, `StageCompleted`, `EquipmentChanged`. "Обучение можно закрыть" means a skip/close is needed. The current hint has no close button and does not catch raycasts, so add a small close button outside the CanvasGroup, or a setting.

---

## 9. Localization (`Core/Text/Loc*.cs`)

Engine `Loc.cs` (REUSE AS-IS):
```csharp
public const string Language = "ru";                                   // :25
public static readonly IReadOnlyList<string> Sections                   // :28  { "Core","Hud","Controller","Tutorial","Catalog","Board" }
public static event Action<string> MissingKey;                          // :38
public static string T(string key)                                      // :49  missing → "[key]" + MissingKeys
public static string F(string key, params object[] args)                // :61  string.Format(InvariantCulture); bad template → returns template, logs FormatErrors
public static string Plural(string key, long n)                         // :83  picks key.one / .few / .many (Russian rule), {0}=n in each form
public static bool Has(string key); public static bool TryGet(string key, out string text)   // :91 :94
public static IReadOnlyList<string> Keys / MissingKeys / FormatErrors / RegistrationProblems // :101 :112 :118 :127
public static void ResetReports()                                       // :137
```
- **How keys are declared:** each section file implements `static partial void Register<Section>(Dictionary<string,string> d)` and calls `d.Add("<section>.<group>.<name>", "Русский текст")`. The prefix must equal the lower-cased section name (checked in `AddSection` `:181`). Duplicates across sections are reported. The table builds lazily, thread-safe (`Table` `:147`).
- Adding a new section takes 3 edits in `Loc.cs`: a partial declaration (`:41-46`), `AddSection(...)` in `Build` (`:171-176`), and an entry in `Sections` (`:28`). The style guide recommends putting window strings in `Loc.Hud.cs` as `hud.<window>.*` (for example `hud.base.*`, `hud.equipment.*`, `hud.results.*`). There is no `common` section: do not borrow other features' keys.
- Russian dictionaries: `Loc.Core.cs:12` (rejects `core.reject.*`, durations `core.duration.*`, rarities `core.rarity.*` with the OLD 6 rarities), `Loc.Hud.cs:12` (top/settings/hire/cards/context/gate/generator/chest/offline/edge), `Loc.Tutorial.cs:11` (`tutorial.hint.*`, all old), `Loc.Controller.cs:11` (save dialogs are reusable; next-mine/campaign texts are old), `Loc.Catalog.cs:12` (shaft and gacha names, card items), `Loc.Board.cs:11` (field labels).
  - REUSE keys: `hud.settings.*`, `hud.top.zone`, `hud.offline.title/ok/elapsed/capped/gold`, `core.reject.not_enough_gold/max_level/closed_sector`, `core.duration.*`, `controller.save.*`, `controller.config_error.title`, `catalog.shaft.*` (if shaft names survive), `catalog.cat.name`.
  - DROP or rewrite: `hud.hire.*`, `hud.free_cat.*`, `hud.dynamite.*`, `hud.blueprints.*`, `hud.cards.*`, `hud.card.*`, `hud.free_chest.*`, `hud.cat.*` (merge text), `hud.generator.*`, `hud.gate*`, `hud.edge.*` (already unused), `hud.chest.*`, every `tutorial.hint.*`, `catalog.card.*`, `catalog.gacha.*`, `core.automation.*`, `core.threshold.*`, `core.reject.not_enough_elixir/duplicates/different_levels`, `controller.next_mine.body` (it describes the old reset list).
  - NEW keys needed: "Не хватает доставленного золота" (TZ explicitly), "Нет пути", item max level, "На эту шахту" / "Сохраняется между шахтами", ×1/×10/«Макс», «Макс.», rarities common/rare/epic/legendary (the TZ set, without uncommon/majestic), the 4 slot names, the 16 item names, «Детали», «Надеть», «Надеть лучшее», compare "{0} → {1}", the 6 branch names and effects, delivery phase, cargo in transit.
- **Plural forms:** register `.one/.few/.many` (example `hud.free_chest.activated_shafts.*` at `Loc.Hud.cs:73-75`, used at `HudBlueprints.cs:320`).
- **Number formatting** (`Core/Numbers/NumberFormat.cs`): `string Format(GameNumber value)` `:20` (≤999 plain, then K/M/B/T/aa…dt, 3 significant digits truncated down, ≥1e308 scientific), `string Format(double value)` `:53`, `string SuffixForGroup(long group)` `:56`, `string FormatDuration(double seconds)` `:68` (through `core.duration.*`). Templates receive already-formatted strings. **ADAPT:** TZ §18 mandates an exact integer money layer, so add `Format(<new exact type>)` with the same truncation rules (truncate down so the display never over-promises). Never feed display strings back into purchase checks.
- **Test-enforced rules** (`Tests/EditMode/LocalizationTests.cs`): `DictionaryIsBuiltWithoutProblems` `:33`, `PluralPicksRussianForm` `:81`, **`EveryKeyUsedInCodeIsRegistered` `:106`** (regex scan of every literal `Loc.T/F/Plural("…")` in `Assets/Game/Scripts`; a `Loc.T("prefix" + id)` composite key is not checked), **`PlayerFacingCodeHasNoCyrillicLiterals` `:299`** (no Cyrillic string literals in `Runtime/**` outside `Loc.*.cs`, except exception messages, `Debug.Log*`, and statements carrying a `// loc:ignore` comment; example at `UiKit.cs:133`).
- Font gotchas: Tiny5 renders "~" as a flat dash and "≈" as a checkered blob (`Loc.Board.cs:84` comment). Glyphs such as → and ★ come from the TMP fallback (`UiFonts.Legacy`). Rich-text colour tags inside dictionary strings are fine (`tutorial.hint.merge`).

---

## 10. What the new "Base" panel (6 rows) and "Equipment" window need

### Base panel (TZ §9, §14)
Use a new partial `HudBase.cs` with `BuildBase()` called from `Hud.Build` between `BuildSettings()` and `BuildModal()`. Expose it through `AnyWindowOpen`, and the bottom slot button "База" opens it.
- Container: `PixelUi.Window(m_Safe, "Base", Loc.T("hud.base.title"), HideBase)` `PixelUi.cs:591` with `Box.Anchor(...)` sized like Settings (`HudWindows.cs:336-338`, height from the row count, rounded to an even number of art pixels). Hide `BodyText`. Alternatively, a full-screen root like Blueprints if a reset/flow hint and an Equipment tab share the screen.
- Header tabs or a mode switch ×1/×10/«Макс» go in a row of 3 `PixelUi.Button(..., PixelButton.Dark, ..., PixelUi.FontBody)`. Show the active one with `SetStyle(PixelButton.Blue)`. There is no tab factory; write a tiny helper.
- 6 rows (Команда, Кирки, Обувь, Сумки, Разгрузка, Оценка добычи): copy `SettingsRowBase` (`HudWindows.cs:350`): an `Inset` panel, an icon (`icon_cat`, `icon_pickaxe`, plus new atlas icons for boots/bag/unload/appraisal), a name + `ур. N/cap` label, effect text "сейчас → после" (FontSmall, `AccentTag`), a `PixelBar` for the level or the next threshold (thresholds only for Кирки/Оценка, per TZ), and on the right a `PixelUi.Button(..., PixelButton.Gold, ..., FontBody)` at `SmallButtonHeight` with a coin `PixelUi.IconText` price inside `Content` (pattern `BuildHire` `Hud.cs:483` / card upgrade `HudBlueprints.cs:204`). Show a red price when short (`SetHireColors` `Hud.cs:960`) and «Макс.» without a price at cap. The ×10 button shows the actual count and the total before the press.
- A scope label "На эту шахту" (vs Equipment "Сохраняется между шахтами"): a `Pill` panel or dim text.
- Refresh: one `ValueKey` per row (`Hud.cs:67`) holding level, cap, wallet≥price flags, the price (`N1`), and the buy-mode count, all at 10 Hz from `Refresh()`. No string building unless the key changed. All numbers come from the central service; no UI formulas (TZ §14).
- Disabled reason: `ButtonRefs.SetInteractable(false)` + `UiSound.PlayDenied` (automatic) + `Toast(reason)`. Short reasons go in `Loc.Hud.cs`.
- Disable everything during `ReadyToContinue`.
- Badge: `m_BaseButton.SetBadge(affordableCount)` (`UiKit.cs:450`).
- Tutorial targets: `Hint.RegisterTarget("base.pickaxe", row.Button.Rect)` etc.
- Optional flow-limit hint line: a dim text at the bottom of the window.

### Equipment window (TZ §10, §14)
Use a partial `HudEquipment.cs`, structured like the Blueprints screen.
- Root: full-screen (copy `HudBlueprints.cs:76-80`) or a tall `PixelWindow`. The header is a Wood panel with title and close (`PinSquare` `HudBlueprints.cs:164`), plus a parts counter `IconText` (a new `icon_parts` sprite is needed).
- 4 equipped slots (кирка, шляпа, рюкзак, обувь): `PixelUi.IconButton(parent, name, icon, onClick, 2)` needs a cell of at least 240 units for scale 2 (style rule), or use `PixelPanel.Slot` + `PixelUi.Icon(..., 2)` + a rarity frame through `PixelUi.Sliced(..., CardFrameSprite(r))`. Empty slots: `PixelPanel.Dashed` (`ui_dashed_frame`) or `ui_socket` (in the atlas, unused).
- Slot filter tabs (5: Все + 4 slots): the same Button-row helper as Base.
- Collection grid of 16: copy the scroll/grid block `HudBlueprints.cs:130-157` (4 columns). Use cells from `PixelUi.Card` (`PixelUi.cs:555`, `PixelCard`; already has rarity frame, art, title, level, progress, face-down) or a trimmed `CreateCardCell` (`HudBlueprints.cs:398`) with an upgrade arrow when parts suffice.
- Item detail: copy `BuildCardWindow`/`FillCardWindow` (`HudBlueprints.cs:181/586`): big cell + compare text "сейчас → после замены", a Green "Улучшить" button with the parts price under the label, a Blue "Надеть" button, and a Gold "Надеть лучшее" button at window level.
- Cat portrait and field visuals of equipped items: `CatLook` (outside this map) must take the team kit instead of per-cat level (`HudContext.cs:221`).

### Missing pieces to build
Tabs/segmented helper, scroll-list helper, a window stack with Back (Cancel action) and a gesture cancel on open, an exact-integer `NumberFormat` overload, new atlas icons (hat, backpack, boots, bag/unload/appraisal, parts, 16 item icons; pipeline `Tools/PixelArt/build.py` → `PixelAtlasData.g.cs`; existing icons: `icon_pickaxe, icon_cat, icon_coin, icon_upgrade, icon_lock, icon_check, icon_plus, icon_warning, icon_star, icon_info, icon_sort, icon_close, icon_gear, icon_clock`), and new `Loc` keys.

---

## 11. Gotchas collected from comments and code

1. **Batchmode / first frame:** before the canvas is laid out, `m_Safe.rect` is about 0. All layout code falls back to `RefWidth/RefHeight` when the rect is under `100*Px` (`HudWindows.cs:167-168`, `Hud.cs:734` `ScreenSafeUnits`). New windows that measure text must do the same.
2. TMP measuring (`GetPreferredValues`) and creating texts work reliably only in an **active** hierarchy. Show the root first, then build or fill cells (`HudBlueprints.cs:223`).
3. **Never assign unchanged values:** use `Ui.SetText`/`Ui.SetActive`/`SetIcon` (they compare first). TMP rebuilds its mesh on every `.text=`. Refresh runs at 10 Hz with `ValueKey`. Factories in `Update` are forbidden. Use `PixelUi.CountText` for small numbers so nothing allocates.
4. Sibling order is z-order. `Show()`, Toast, Hint and LootFly call `SetAsLastSibling`, so the tutorial hint must be hidden under any window (TutorialController does this through `AnyWindowOpen`). Add new windows to `AnyWindowOpen` or the hint will draw over them.
5. Window shades and panels block raycasts. PointerInput checks UI only at press (`PointerInput.cs:250`), so a gesture already in progress survives a modal opening. TZ §14 requires a fix.
6. `OnModalButton` reads the action before `CloseModal` (the queue may reopen with other actions); keep that order.
7. PlayMode tests rely on: modal buttons named `"btn"`, `gc.Hud.ModalOpen/BlueprintsOpen/SettingsOpen/ShowSettings/HideSettings/ShowChest/ShowOffline/ToggleBlueprints/Toast/Refresh`, and `Hud` living on the Canvas GameObject (`GetComponent<Canvas>()`, `GetComponent<CanvasScaler>()`). Screenshot helpers switch the canvas to ScreenSpaceCamera (`PlayModeUtil.cs:196-226`), which is why `UiCamera` (`Hud.cs:191`) and `sortingOrder 30000` exist. Keep these entry points or update the tests.
8. Fonts: Tiny5 is created at runtime as a dynamic TMP asset (`UiFonts.CreatePixel`, `UiKit.cs:145`). The shader is copied from the TMP default material so that it ships in the build. The shadow material needs the `OUTLINE_ON+UNDERLAY_ON` variant, which ships because TMP "Drop Shadow" sits in Resources (`UiKit.cs:174`). Do not use fake bold on the pixel font (`PixelUi.SetFont` `:339`).
9. `PixelUi.Icon` keeps the atlas pivot (the centre of the original canvas). Move icons only with `anchoredPosition`, never stretch them with anchors. Use `CenterOffset` (`HudContext.cs:242`) to centre trimmed sprites. Fractional scale is allowed only in pops of 0.25 s or less (`PixelPop`).
10. Even-pixel window heights keep the frame centre on the grid (`HudWindows.cs:217-218`).
11. `PixelButtonPress` decides "denied" at press time, because the click action may disable the button (`PixelButtonPress.cs:56-64`).
12. `PixelToggle.Flip` invokes `OnChanged` **before** the click sound, so switching sound on confirms itself with a click (`PixelUi.cs:722`). `SetOn`/`SetValue` are silent (use them when loading from save).
13. `Hud.Start` → `WireTutorialHint` depends on GameController creating `TutorialController` right after `Hud.Create` in the same `Init` (`Hud.cs:766-775`). Keep that order or wire explicitly.
14. `ProjectSetup` sets the HUD `sortingOrder` to 10 and the runtime forces 30000. Edit the runtime value, not the scene.
15. Loot fly is skipped (pulse only) when `AnyWindowOpen` is true or the point is behind the camera. The pool holds 24 and at most 10 per drop (`Hud.cs:567`). Fly on deposit events, not per coin.
16. `GameController.SetVolume/SetReduceEffects` are no-ops when `Session == null`. A new profile/session object must exist before the settings window works.
17. `LocalizationTests.EveryKeyUsedInCodeIsRegistered` does not check composite keys `Loc.T("x." + id)`. For the 16 item names, prefer literal keys or add a test (as `catalog.*` does by ID).
18. Sync and file rules: MonoBehaviour file name equals class name (`SafeArea.cs` remark). New MonoBehaviours (for example a `BackButtonRouter`) go in their own files.
