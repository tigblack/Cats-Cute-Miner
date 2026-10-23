# Reuse map: save / offline / clock / numbers

Scope: `Assets/Game/Scripts/Core/Save/SaveService.cs`, `Core/Sim/{GameState,OfflineProgress,GameSession.Time,Clock}.cs`,
`Core/Numbers/{GameNumber,NumberFormat,Formula,Pcg32}.cs`, tests `Tests/EditMode/{SaveHardeningTests,SavePerformanceTests,OfflineAcceptanceTests}.cs`,
plus `Assets/Game/link.xml` and the IL2CPP/Newtonsoft/BigInteger setup. I also read the call sites in `Runtime/GameController.cs` and `Runtime/SystemClock.cs`
because they define the lifecycle protocol. Target: new profile `CatOutpostV2` (TZ v2.1 §13, §17, §18, §19).

Namespaces: numbers are in `CatMiners.Core`, state/sim in `CatMiners.Core.Sim`, save in `CatMiners.Core.Save`. Everything is in the asmdef `CatMiners.Core`
(`noEngineReferences: true`, `overrideReferences: true`, precompiled reference `Newtonsoft.Json.dll` only). The `Tools/CoreTests` dotnet harness
builds the same sources as **netstandard2.1 / C# 9** with NuGet Newtonsoft **13.0.3**. Unity uses `com.unity.nuget.newtonsoft-json` 3.2.1, which is Json.NET **13.0.2**.

---

## 0. TL;DR verdicts

| Type / member | File:line | Verdict |
|---|---|---|
| `IFileSystem`, `IDurableBufferWriter`, `RealFileSystem` | SaveService.cs:15-59 | **REUSE AS-IS** (add dir-listing/dir-exists if needed) |
| `GameNumberConverter` | SaveService.cs:62-77 | **REUSE AS-IS** (only if GameNumber stays in state, e.g. HP source) |
| `FiniteJsonTextWriter` (internal) | SaveService.cs:83-116 | **REUSE AS-IS** |
| `CharBufferWriter` (internal) | SaveService.cs:119-166 | **REUSE AS-IS** |
| `LoadStatus`, `LoadResult` | SaveService.cs:168-192 | **ADAPT**: new statuses, generic state type |
| `SaveService.Settings()` | SaveService.cs:274-287 | **ADAPT**: add BigInteger converter and `DateParseHandling.None` |
| Envelope writer (`WriteEnvelope`, `AppendInteger`, `AppendJsonString`, `Encode`, `NeedsEscape`, `AppendEscape`) | SaveService.cs:379-516 | **ADAPT**: new header fields; checksum must cover the header |
| `Save` | SaveService.cs:522-555 | **ADAPT**: commitSequence; reconsider the non-atomic fallback |
| `Load`, `TryRead`, `QuarantineLeftover` | SaveService.cs:562-623, 653-672, 709-739 | **ADAPT** (rewrite selection: max commitSequence of main/bak, ignore tmp) |
| `Quarantine`, `QuarantineAs`, `LastQuarantineError` | SaveService.cs:272, 679-707 | **REUSE AS-IS** |
| `Migrate`, `MigrateDocument`, `ParsePayload`, `s_Migrations` | SaveService.cs:248-363 | **ADAPT**: parametrize by profile (bound to `GameState.CurrentSchemaVersion` now) |
| `SerializeState` / `DeserializeState` / `Sha256Hex` | SaveService.cs:307-321 | **ADAPT** to generic `<T>`; drop the `BalanceRepository` dependency |
| `MainPath` / `SlotPaths` | SaveService.cs:289-301 | **ADAPT** to `<persistent>/CatOutpostV2/<main\|debug>/state.json` |
| Ownership-table doc comment | SaveService.cs:199-237 | **DROP** (old fields); a test parses it, see §7 |
| `GameState`, `PlayerProgressState`, `MineProgressState` and all nested DTOs | GameState.cs:8-257 | **DROP-FOR-NEW-MODE** (old merge model) |
| `PlayerSettingsState`, `AudioGroup` | GameState.cs:60-83 | **REUSE AS-IS** (TZ: settings may be imported once) |
| `SaveSlots` | GameState.cs:25-29 | **ADAPT** (`main`/`debug` profile ids) |
| `OfflineProgress.Apply` / `MarkProcessed` | OfflineProgress.cs:42-108 | **ADAPT**: keep the marker math exactly; run on a copy; commit; drop `OfflineMode` |
| `OfflineSummary` | OfflineProgress.cs:6-34 | **DROP** (old counters); write a new summary |
| `GameSession.RestartCurrentZone` (partial in OfflineProgress.cs) | OfflineProgress.cs:111-119 | **DROP** (TZ forbids hidden resets) |
| `IClock`, `ManualClock` | Clock.cs:9-41 | **REUSE AS-IS** |
| `SystemClock` (Runtime) | Runtime/SystemClock.cs:12-17 | **REUSE AS-IS** |
| `GameSession.Advance` and the event loop | GameSession.Time.cs:16-75 | **DROP code, REUSE pattern** |
| Everything else in GameSession.Time.cs (generators, deliveries, containers) | GameSession.Time.cs:77-442 | **DROP-FOR-NEW-MODE** |
| `GameNumber` | GameNumber.cs:13-391 | **REUSE AS-IS** for HP source and display only. Not for the wallet (TZ §12.2, §18) |
| `NumberFormat.Format(GameNumber/double)`, `SuffixForGroup`, `FormatDuration` | NumberFormat.cs:20-80 | **REUSE AS-IS**. **ADD** a `Format(BigInteger)` overload (§5) |
| `Formula.RulesVersion` | Formula.cs:12 | **DROP** (the new rulesVersion is a string `outpost-2.1-r1`) |
| `Formula.Ft0..Ft3Number`, `Evaluate`, `Price`, `Payout` | Formula.cs:15-96 | **DROP-FOR-NEW-MODE** (old Appendix C.1; uses tolerant double ceil) |
| `Formula.RoundToMultiple`, `RoundHalfUp` | Formula.cs:70-80 | Optional reuse (double helpers) |
| `Pcg32` | Pcg32.cs:9-51 | **REUSE AS-IS** |
| `RngStreams` | Pcg32.cs:55-71 | **ADAPT** (streams Gacha/Delivery/Crit/Timers belong to old systems) |

---

## 1. SaveService.cs: file protocol in detail

### 1.1 Filesystem abstraction (generic, reuse)

```csharp
// SaveService.cs:15
public interface IFileSystem
{
    bool Exists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytesDurable(string path, byte[] data);
    void Replace(string source, string destination, string backup);
    void Move(string source, string destination);
    void Copy(string source, string destination);
    void Delete(string path);
    void CreateDirectory(string path);
}
// SaveService.cs:31 — optional zero-copy extension
public interface IDurableBufferWriter { void WriteAllBytesDurable(string path, byte[] data, int count); }
// SaveService.cs:36
public sealed class RealFileSystem : IFileSystem, IDurableBufferWriter
```

`RealFileSystem` behaviour:
- `WriteAllBytesDurable` (43-52) opens `new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1)`. Buffer size 1 means no
  intermediate buffer. It writes `count` bytes, then calls `fs.Flush(true)` (flush to disk, i.e. fsync). `FileMode.Create` truncates a stale tmp.
- `Replace` calls `File.Replace(src, dst, bak)` (54). `Move` calls `File.Move` (55). netstandard2.1 has **no overwrite overload**, so Move throws if the
  destination exists. `Copy` calls `File.Copy(.., true)` (56).
- The directory is **not** fsynced after a rename. Rename durability on power loss depends on the OS and filesystem.
- The tests inject faults through the interface (`RecordingFs`, SaveHardeningTests.cs:80-101, including `FailMove`). Reuse that pattern for
  the TZ §20 "Аварийная запись" check at every write step.

### 1.2 Paths (old)

- `MainPath(slot) => Path.Combine(m_Root, "save_" + slot.ToLowerInvariant() + ".json")` (289). `m_Root` is passed in by the caller.
- `SlotPaths(slot)` (292-301) caches `[main, main + ".tmp", main + ".bak"]` per slot. This avoids allocating new strings on each save.
- Runtime root (GameController.cs:153-154): `SaveDirectory = SaveDirectoryOverride ?? Application.persistentDataPath`. Old files therefore sit **directly in
  persistentDataPath**: `save_main.json`, `save_debug.json`, plus `.tmp`, `.bak` and any quarantined `*.corrupt-*`, `*.incompatible-*`, `*.leftover-*` files.
- **For CatOutpostV2:** call `new SaveService(Path.Combine(persistentDataPath, "CatOutpostV2", "main"))` and the same with `"debug"`, with file name `state.json`.
  The directories are siblings of the old files, so nothing collides. `Save` already calls `m_Fs.CreateDirectory(m_Root)` on every save (525), so the
  nested directory is created automatically.

### 1.3 Envelope (old format, byte-exact)

`WriteEnvelope` (379-422) writes `{"schemaVersion":N,"rulesVersion":N,"balanceSha256":"…","slot":"…","savedAtUtcMs":N,"payloadSha256":"<64 lowercase hex>","payload":"<payload JSON as an escaped string>"}`
with no whitespace. The test `PayloadAndEnvelopeAreByteIdenticalToPlainSerializer` (SaveHardeningTests.cs:191) checks that these bytes match the old
`JObject.ToString(Formatting.None)` output exactly (`LegacySaveFormat`, SaveHardeningTests.cs:22-55).

Steps:
1. Serialize the payload with the cached `JsonSerializer` into the reusable `CharBufferWriter m_PayloadChars` through `FiniteJsonTextWriter`, which throws `InvalidDataException("… non-finite value at <json path>")` on NaN or ±Inf (111-115).
2. UTF-8 encode into the reusable `m_PayloadBytes`. Compute SHA-256 with the cached `SHA256 m_Sha` (395-396).
3. Hand-write the envelope into `m_EnvelopeChars`. Integers go through `AppendInteger` (no allocation; handles `long.MinValue`, 431-455). Strings go through
   `AppendJsonString`, which escapes the same set as Newtonsoft's default `StringEscapeHandling`: `c < ' '`, `"`, `\`, U+0085, U+2028, U+2029 (495).
4. UTF-8 encode into `m_EnvelopeBytes` and return the length (421).

Facts that matter for the new design:
- **The checksum covers only the payload.** Header fields (`schemaVersion`, `balanceSha256`, `slot`, `savedAtUtcMs`) are unauthenticated duplicates.
  `Load` reads `balanceSha256` from the **envelope** (581), not from the payload. If load selection will use `commitSequence`, put it in the checksummed
  region: either hash header + payload, or duplicate `commitSequence`, `modeId` and `profile` inside the payload and cross-check them on load.
- The envelope `slot` is never compared with the requested slot. Nor is envelope `schemaVersion` compared with payload `SchemaVersion`. TZ §20 requires
  "без смешения профилей", so check `modeId` and `profile` on load.
- `savedAtUtcMs` is informational only. It never takes part in load selection.
- Size (zone93, 240 objects, 10 cats): payload 15,293 B, envelope 18,310 B. The double escaping adds about 20% (Docs/Reports/save-perf.json).
- `SaveService` instances are **single-threaded** because of the reused buffers (doc comment at 197).

### 1.4 Save sequence (`Save(GameState state, long nowUtcMs)`, 522-555)

1. `WriteEnvelope`. A NaN or ±Inf throws here, **before any file operation** (test `NaNSnapshotTouchesNoFileOperation`, SaveHardeningTests.cs:150).
2. `CreateDirectory(root)`.
3. Write tmp durably (zero-copy if the filesystem implements `IDurableBufferWriter`; otherwise an exact-length copy).
4. If main exists: `Replace(tmp, main, bak)`. If `Replace` throws: **fallback** `Copy(main, bak); Delete(main); Move(tmp, main)`. This fallback is **not atomic**:
   between `Delete` and `Move` there is no main. The old loader survives this only because it accepts tmp (see 1.5). TZ §17 forbids calling this fallback atomic.
5. If main is absent: `Move(tmp, main)` (first save).

There is no `commitSequence`, no retry and no "prepared result" handling. Exceptions propagate, and `GameController.SaveNow` (GameController.cs:765-782) logs
and **swallows** them.

### 1.5 Load sequence (`Load(string slot, string expectedBalanceSha)`, 562-623)

The order is **main, then tmp, then bak**, and the **first** valid file wins. Validity is decided by `TryRead` (709-739):
- strict UTF-8 (`new UTF8Encoding(false, true)`), then `JObject.Parse(text)`. Note that `JObject.Parse` uses the default `DateParseHandling.DateTime`.
- `schemaVersion` must be in `1..GameState.CurrentSchemaVersion`. **A future schema is treated as corrupt and quarantined** (test `FutureSchemaVersionIsNotLoaded`, :658).
- `payloadSha256` must equal `Sha256Hex(payload)` (case-insensitive).

Outcomes:
- Unreadable file: `Quarantine(path, "corrupt")` (renamed, not deleted). Try the next candidate.
- `balanceSha256` differs from the expected value: status `IncompatibleBalance`. The file is renamed to `*.incompatible-<hash8>`. Each remaining tmp/bak goes
  through `QuarantineLeftover` (653-672): another balance becomes `incompatible-<hash8>`, a corrupt file becomes `corrupt-<UTC>`, and a file matching the current
  balance becomes `leftover-<UTC>`. This guarantees the first save of a new game cannot overwrite them. Return `State = null`.
- Valid file: `Migrate` (no-op at v1), then `DeserializeState`. A null result or an exception counts as corrupt, and the next candidate is tried.
- Status is `Loaded` if index 0 won, otherwise `LoadedFromBackup` (tmp **or** bak).
- `state.RulesVersion != Formula.RulesVersion` gives `RulesChangedMineReset` with the state returned. GameController then resets the mine (`RestoreWithMineReset`, GameController.cs:274-281).
- Nothing valid: `Corrupted` if any candidate file existed, otherwise `NoSave`.
- `LoadResult.SourcePath` is the winning or rejected path, recorded before the rename. `QuarantinedFiles` holds the new names. `Message` joins the notes
  and rename failures with `"; "`.

Quarantine naming (`Quarantine`, 679-684; `QuarantineAs`, 686-707): the new name is `path + "." + reason + "-" + DateTime.UtcNow:yyyyMMddHHmmss`. On a
collision it appends `-2`, `-3` and so on, up to 1000. On failure it returns `null` and sets `LastQuarantineError`. It uses `DateTime.UtcNow` **directly** (not IClock).

**How this differs from TZ §17 (all of these need ADAPT):**
- TZ: choose the valid **committed** snapshot among main/backup with the **largest `commitSequence`**. **tmp is never a commit.** The current code accepts tmp
  and does not compare generations.
- TZ: a balance change must **not** reject or reset the run, because the run keeps its own parameter snapshot. So `IncompatibleBalance` is not the right
  behaviour for `balanceVersion` changes. `RulesChangedMineReset` is a forbidden hidden reset.
- TZ: future schemas must be kept, not overwritten, and reported. Add a distinct status (e.g. `FutureSchema` or `IncompatibleSchema`) instead of reusing `Corrupted`.
- TZ: after a save error, retry the **same prepared bytes** (same commitSequence). Never re-run a transaction or re-roll the PRNG.
- `File.Replace` puts whatever is currently in main into bak. That is fine only because `Load` quarantines a corrupt main first.
  Keep "Load before first Save" as an invariant.

### 1.6 JSON settings (`SaveService.Settings()`, 274-287)

```csharp
Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture,
FloatFormatHandling = FloatFormatHandling.String,     // second line of defence; FiniteJsonTextWriter throws first
MissingMemberHandling = MissingMemberHandling.Ignore, // old saves without new fields load with defaults
NullValueHandling = NullValueHandling.Include,
ObjectCreationHandling = ObjectCreationHandling.Replace, // REQUIRED: field initialisers (new List<>()) are replaced, not appended to
Converters = { new GameNumberConverter() }            // GameNumber <-> round-trip string (ToStorageString/Parse)
```
- Plain public fields, no attributes. The default contract resolver serializes public fields and properties, so **get-only computed properties are
  serialized too** unless they are marked `[JsonIgnore]`. Example: `ContainerState.HoldsCat` (GameState.cs:187) ends up in the payload. Keep DTOs as plain fields.
- `DateParseHandling` is not set, so it defaults to `DateTime`. **Gotcha:** a `string` field holding an ISO-looking value would be turned into a DateTime
  and re-stringified on load, which breaks the round-trip and the SHA. Set `DateParseHandling = DateParseHandling.None` for V2. `ParsePayload` (352-363)
  already does this, along with `FloatParseHandling.Double`.
- Never call `JsonConvert.SerializeObject(gameNumber)` without `Settings()`. `GameNumber` would then serialize as its public properties
  (`IsBig`, `Mantissa`…) and fail to round-trip.
- For V2 add a `BigIntegerStringConverter`, because TZ §12.2 wants money as a decimal string with no separators. Newtonsoft's default writes BigInteger as
  a raw JSON integer, which JS and Python tools would truncate. Read leniently from both string and integer tokens.

### 1.7 Migrations (generic, adapt)

```csharp
public static IReadOnlyDictionary<int, Func<JObject, JObject>> Migrations { get; }                       // :250 (empty, v1 only)
public static string Migrate(string payload, int schemaVersion)                                          // :328
public static JObject MigrateDocument(JObject doc, int fromVersion, int toVersion,
                                      IReadOnlyDictionary<int, Func<JObject, JObject>> steps)            // :338
public static JObject ParsePayload(string payload)                                                       // :352
```
`Migrate` returns the **same string instance** for the current version (`Assert.AreSame` in the test, :608). `MigrateDocument` sets
`doc["SchemaVersion"] = v+1` after each step, and a missing step throws `InvalidDataException("нет миграции … n → n+1")`. The mechanism is generic, but
it is hard-wired to `GameState.CurrentSchemaVersion` (330-333) and to the payload key `"SchemaVersion"`. Parametrize it per profile. V2 starts its **own**
`schemaVersion = 1` (TZ: "Не использовать номер 2 как неявное соответствие старой схеме").

### 1.8 Other public API

```csharp
public SaveService(string rootDirectory, IFileSystem fileSystem = null)   // :265
public string LastQuarantineError { get; private set; }                    // :272
public static JsonSerializerSettings Settings()                            // :274
public string MainPath(string slot)                                        // :289
public static string SerializeState(GameState state)                       // :307  (== JsonConvert.SerializeObject(state, Settings()) byte-for-byte, plus NaN check)
public static GameState DeserializeState(string json)                      // :319
public static string Sha256Hex(string text)                                // :321  (via BalanceRepository.ComputeSha256 in Core.Data — inline a local SHA for V2)
public byte[] BuildEnvelope(GameState state, long nowUtcMs)                // :366  (copying; for tests/tools)
public void Save(GameState state, long nowUtcMs)                           // :522
public LoadResult Load(string slot, string expectedBalanceSha)             // :562
public string Quarantine(string path, string reason)                       // :679
public enum LoadStatus { NoSave, Loaded, LoadedFromBackup, RulesChangedMineReset, IncompatibleBalance, Corrupted }   // :168
public sealed class LoadResult { LoadStatus Status; GameState State; string Message; string SourcePath; readonly List<string> QuarantinedFiles; }  // :178
```

### 1.9 Coupling to old gameplay in SaveService.cs

- `using CatMiners.Core.Sim` everywhere for `GameState`: `SerializeState`, `DeserializeState`, `BuildEnvelope`, `WriteEnvelope`, `Save`, `LoadResult.State`.
- `GameState.CurrentSchemaVersion` at 330, 332, 720. `Formula.RulesVersion` (int) at 609-613. `state.SchemaVersion`, `RulesVersion`, `BalanceSha256` and `Slot` feed the envelope (402-408).
- `BalanceRepository.ComputeSha256` at 321 (`CatMiners.Core.Data`, BalanceRepository.cs:78). Its expected-balance semantics belong to the old `json-data.json` profile.
- The ownership-table comment (199-237) lists old fields, and a test parses it from the source (§7).

**Suggested V2 shape:** extract a generic `AtomicJsonStore<T>` (or `EnvelopeStore`) from 1.1-1.8 that keeps `IFileSystem`, `FiniteJsonTextWriter`,
`CharBufferWriter`, the hand-written envelope, `Quarantine` and `MigrateDocument`. Inject `modeId`, `currentSchemaVersion`, the migrations table,
the file name and the header validator. Leave the old `SaveService` untouched for the archived profile.

---

## 2. GameState.cs: old persistent model (DROP, apart from settings)

- `GameState` (8-23): `CurrentSchemaVersion = 1` (11), `SchemaVersion`, `RulesVersion = Formula.RulesVersion`, `BalanceSha256`, `Slot = SaveSlots.Main`,
  **`LastProcessedUtcMs`** (18, root-level UTC marker that survives zone change), `NextOpId = 1` (20, transaction id), `Player`, `Mine`.
- `SaveSlots` (25-29): `Main = "Main"`, `Debug = "Debug"`. Used in GameController, DevPanel, LevelCheat and many PlayMode tests (`SwitchSlot(SaveSlots.Debug, true, zone)`).
- `PlayerProgressState` (31-57): Elixir, Hard (GameNumber), Cards, RewardCycle, Dynamite, ChestReveals, FreeChest, DeliveryHistory, `Rng` (RngStreams),
  CurrentZone, HighestCompletedZone, CampaignCompleted, TutorialDone, **Settings**, ExcludedRewards (`Dictionary<string,double>`, used by the NaN tests),
  **PlayerTimeTicks** (monotonic player time = online + credited offline), Ledger, ZoneTimeTicks. All of this is merge/cards/gacha. **DROP.**
- `PlayerSettingsState` (72-83): SoundOn, VibrationOn, Master/Music/Sfx/AmbienceVolume (float), ReduceEffects. **REUSE.** TZ §17 allows a one-time
  import. `AudioGroup` enum (60-66) is used by the audio runtime. **REUSE.**
- `CardState`, `CardGrant`, `ChestResult`, `FreeChestState`, `DeliveryHistory`, `ObjectStatus`, `ObjectState`, `GeneratorState`, `CatState`
  (`TargetIndex`, `PhaseTicks`), `ContainerKind`/`ContainerState` (cannon barrels, deliveries), `LootState`, `MineProgressState` (86-257) are all old
  gameplay. **DROP.** Concepts worth carrying over: the root-level `LastProcessedUtcMs` and `NextOpId`, a monotonic `PlayerTimeTicks` (TZ name: `simulationTicks`),
  and the player/mine ownership split (TZ §17 calls it profile vs run).
- `float` fields (volumes, `CameraFocusZ`) go through `FiniteJsonTextWriter.WriteValue(float)`, so NaN is rejected for them too.

**Importing old settings, a gotcha:** do **not** call the old `SaveService.Load` to read `save_main.json`. On a balance mismatch or corruption it **renames**
the originals (`*.incompatible-*`, `*.corrupt-*`, `*.leftover-*`), and TZ §17 requires the originals to stay in place. Read the old file read-only
(UTF-8 → `JObject` → payload → `Player.Settings`), or first copy it into an archive directory and read the copy. The legacy PlayerPrefs key
`catminers.sound` is migrated in GameController.cs:28.

---

## 3. Offline time and UTC markers today

### 3.1 Core (`OfflineProgress.cs`)

```csharp
public static OfflineSummary Apply(GameSession session, long nowUtcMs)   // :42
public static void MarkProcessed(GameSession session, long nowUtcMs)     // :105  marker = max(marker, now)
```
`Apply` algorithm (42-102). **This math is correct and tested, so reuse it verbatim:**
1. `deltaMs = now - state.LastProcessedUtcMs`. If `deltaMs <= 0` or `CampaignCompleted`: `MarkProcessed(now)` (which never decreases the marker) and return an empty summary.
2. `ticks = deltaMs > long.MaxValue/1000 ? MaxOfflineTicks : deltaMs*1000` (overflow guard). Cap at `GameSession.MaxOfflineTicks = 8h * 3600 * 1_000_000`
   (GameSession.cs:16) and set `Capped`. The excess is **discarded**, not carried over.
3. Set `session.OfflineMode = true`, call `session.Advance(ticks)`, then restore the flag in `finally`. **The same Advance as online.**
4. Build the summary from deltas (Gold, Elixir, RocksDestroyed) and from the `SimEvent`s appended during Advance (`ChestOpened`, `GateBroken`, `ExitBroken`,
   `ShaftCleared`, `PayoutReady`, `ContainerSpawned`). This couples to `SimEventType`.
5. `MarkProcessed(now)`, which applies even when the interval was capped.

`OfflineMode` semantics (old, **must not carry over**, TZ §13 "Старый флаг OfflineMode не должен отключать назначение новых целей"):
- Delivery timer frozen (GameSession.Time.cs:94), no delivery spawn (279).
- No `TargetHit`/`GeneratorPaid` events (141, 182).
- **AutoMining continuation disabled** (`if (AutoMining && !OfflineMode)`, 216).
- Deferred automation check skipped (18).

### 3.2 Runtime lifecycle (GameController.cs, for reference)

- **Load** (`LoadSlot`, 185-267): `now = Clock.UtcNowMs`, then `Load`, then `FromState` or `NewGame(…, now)`, then `OfflineProgress.Apply(Session, now)`,
  then `Events.Clear()` (offline events are not replayed), then `SaveNow()`, then `Hud.ShowOffline(summary)` if `HasNews`.
- **Frame** (`Update`, 441-480): `realDt = MonotonicSeconds - m_LastMono`; `gapMs = UtcNow - LastProcessedUtcMs`.
  - If `realDt > 1.0` or `gapMs > 1000`: `OfflineProgress.Apply(Session, now)`, drop that interval's events, `SaveNow()`, show the window.
  - Otherwise: `Session.Advance(SecondsToTicks(min(realDt, 1.0) * speed))`, then `OfflineProgress.MarkProcessed(Session, now)`. The sim advances by the
    **monotonic** dt, and the marker is set to **UTC** now.
- **Autosave**: `SaveIntervalSeconds = 2.0` (GameController.cs:22), only if `m_Dirty`. `SaveNow()` (765-782) calls `MarkProcessed(now)`, sets
  `CameraFocusZ` and calls `m_Saves.Save(state, now)`. It **catches and logs** exceptions.
- `OnApplicationPause(true)`, `OnApplicationFocus(false)` and `OnApplicationQuit` all call `SaveNow()` (785-802).
- `m_SaveBlocked` (90) is set after a corrupt or incompatible load and lasts until the player confirms "Начать заново" (`OfferRestart`, 312-327).

### 3.3 Gaps against TZ §13/§17 (fix in the new mode)

1. **Per-frame rounding**: `SecondsToTicks(realDt)` is `Math.Round(seconds * 1e6)` (GameSession.cs:195) applied to each frame on its own. The TZ requires an
   exact accumulated tick remainder. Accumulate `MonotonicSeconds` as an absolute value and advance by `floor(total*1e6) - ticksDone`.
2. **Offline runs in place on the live session**, and the window shows even when `SaveNow` failed (the exception is swallowed) or when `m_SaveBlocked`
   is set. The TZ requires the offline calculation to run on a copy, with HP, cargo, money, PRNG, summary and marker committed in **one** transaction,
   and the window shown only after a successful write.
3. `SaveNow` sets the marker to `now` **without first advancing the sim to `now`**. The online time between the last `Update` and pause/save is
   marked as processed but never simulated, so up to one frame (≤1 s) is lost. The TZ says: finish the elapsed online interval, then mark, then save.
4. A hitch over 1 s (GC, scene load) is treated as offline, with different rules. Harmless once offline and online share semantics, which the TZ requires.
5. Clock forward jumps of 1 s to 8 h while playing are each credited as offline, with a cap per jump. The TZ explicitly leaves server-side protection out of scope.
6. **A single bogus clock reading of `long.MaxValue` pins the marker forever** (test `ExtremeClockValueIsCappedWithoutOverflow`, :553). After that, no
   offline time is ever credited. This matches the TZ ("marker never decreases"), but consider a sanity bound before committing a marker far in the future.
7. A `MonotonicSeconds` gotcha: `Time.realtimeSinceStartupAsDouble` (SystemClock.cs:16) keeps counting in the background on mobile. After a resume,
   `realDt` is large, which correctly routes to the UTC offline branch.

### 3.4 Clock (`Clock.cs`, REUSE AS-IS)

```csharp
public interface IClock { long UtcNowMs { get; } double MonotonicSeconds { get; } }          // :9
public sealed class ManualClock : IClock {                                                    // :21
    public long UtcNowMs { get; set; } public double MonotonicSeconds { get; set; }
    public ManualClock(); public ManualClock(long utcNowMs, double monotonicSeconds = 0.0);
    public void Advance(double seconds); }  // UTC += round(seconds*1000); NaN/Inf -> ArgumentOutOfRangeException  (:35)
// Runtime/SystemClock.cs:12  UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); MonotonicSeconds => Time.realtimeSinceStartupAsDouble
// GameController.ClockOverride (static IClock, :34) and SaveDirectoryOverride (static string, :31) are the test hooks (PlayModeUtil.cs:33)
```
The core never reads clocks itself, which is a good rule to keep: time comes in as parameters.

---

## 4. GameSession.Time.cs: event-step simulation (pattern only)

```csharp
public void Advance(long dtTicks)   // :16
```
Loop (16-35): `ProcessDueEvents()`. If `remaining <= 0`, stop. Otherwise `next = TimeToNextEvent()`, `step = min(remaining, next)`, `ElapseTime(step)`,
`remaining -= step`. A guard throws after 50,000,000 iterations. Documented property: `Advance(a)+Advance(b) ≡ Advance(a+b)`. It is tested in
SimulationTests (`AdvanceIsAdditive`) and OfflineAcceptanceTests. The fixed order within one instant (10-14) is: hits by cat list order, then target
completion, then generator cycles, then timers, then loot auto-collect, then `TrySpawnPending`.

- **Old coupling everywhere**: `Mine.Cats`, `PhaseTicks`, `AttackDelayTicks`, `Layout.Objects` (ZoneLayout), `ObjectKind.Cart/Shaft/Gate/Exit`,
  generators, `FreeCat`/`Delivery`/`FreeChest` timers, `Loot`, containers (cannon barrels), `SimEvent` emission, `Player.Rng.Crit/Timers/Delivery`.
  `UpdateUnlocks`, `PruneWindows`, `NextDeliveryIntervalSeconds`, `DeliveryPool`, `TryCreateDelivery` and `DeliveryCatLevel` (298-441) are all old. **DROP.**
- **Performance note for V2:** `TimeToNextEvent` (37-61) scans every cat, object and loot entry once per event. That is O(N) per event for an 8 h offline
  run. The TZ budget for offline is ≤3 s in the Editor and ≤6 s on a phone. Use a priority queue of deadlines keyed by (tick, phase, stable id) to get the
  TZ §6.5 seven-phase ordering.
- Time is kept in `ElapseTime` (63-75): `Player.PlayerTimeTicks += step`, `Mine.ZoneTimeTicks += step`. In V2 these become `simulationTicks`.
- TZ §6.5 requires exact rational edge/unload progress (`BigInteger numerator/denominator`). Nothing like that exists today. Integer `PhaseTicks` is the
  closest analogue.

---

## 5. Numbers

### 5.1 GameNumber (`GameNumber.cs`, REUSE AS-IS for HP source and display)

A hybrid representation: a plain `double` when `|v| < 1e300`, otherwise a normalized mantissa `[1,10)` × 10^`long`. It is **not** exact for integers
(comment at 11) and throws `ArithmeticException` on NaN or Inf (60-64).
```csharp
public readonly struct GameNumber : IComparable<GameNumber>, IEquatable<GameNumber>      // :13
public static readonly GameNumber Zero, One;                                             // :25-26
public GameNumber(double mantissa, long exponent)                                        // :53 (normalizes)
public static GameNumber FromDouble(double value)                                        // :66
public static GameNumber FromLog10(double log10Value)                                    // :106
public static implicit operator GameNumber(double|int|long)                              // :116-118
public bool IsBig, IsZero, IsPositive, IsNegative; public int Sign;                      // :122-126
public double Mantissa { get; } public long Exponent { get; }                            // :129, :140
public double ToDouble(); public bool FitsInDouble; public double Log10(); public GameNumber Abs();  // :174-190
operators unary -, +, -, *, /, ==, !=, <, >, <=, >=                                      // :194-244, :345-350
public static GameNumber Pow(double baseValue, double power); public GameNumber Pow(double power)    // :247, :277
public GameNumber Floor(), Ceiling(), CeilingTolerant(double rel = 1e-12), FloorTolerant(double rel = 1e-12)  // :287-318
public static GameNumber Max(a,b), Min(a,b); public static bool ApproximatelyEqual(a, b, double relativeTolerance)  // :320, :352
public string ToStorageString()   // "15", "1.27E+19", "1.2345e352" — "R" format, InvariantCulture   (:364)
public static GameNumber Parse(string text)   // big form parses mantissa and exponent separately to keep round-trip (:370)
```
- TZ §12.2 and §18: **not allowed** for the wallet, cargo values or accumulated credits, because units are lost above 2^53. It is explicitly allowed for adapting source HP (check that order is preserved) and for formatting.
- No dependencies on old gameplay.

### 5.2 NumberFormat (`NumberFormat.cs`, REUSE AS-IS, then extend)

```csharp
public const long ScientificFromExponent = 308;           // :18
public static string Format(GameNumber value)             // :20
public static string Format(double value)                 // :53
public static string SuffixForGroup(long group)           // :56   0:"",1:"K",2:"M",3:"B",4:"T",5:"aa"…; 102:"dt"; null beyond
public static string FormatDuration(double seconds)       // :68   Loc keys core.duration.{seconds,minutes_seconds,hours_minutes,days_hours,unknown}
```
Rules:
- Below 1e3: integers print as integers. 10-100 uses `"0.0"`, below 10 uses `"0.##"`. These two paths **round** rather than truncate (`ToString("0.0")`),
  which is inconsistent with the "never promise more" rule. It only matters for fractional values.
- 1e3 and up: 3 significant digits, **truncated** (`Math.Floor(x*factor + 1e-9)`), trailing zeros trimmed, then the suffix.
- 1e308 and up: `"1.23e352"`, also truncated.
- The decimal separator is always `.` (InvariantCulture), even in the Russian UI.

Tests: NumberAndFormulaTests.cs:62-85 (`"1.23K"`, `"12.7ab"`, `"1.00e352"`, `"1dt"`). 78 call sites in Scripts.
`FormatDuration` depends on `CatMiners.Core.Text.Loc`, which is fine to reuse.

**BigInteger: there is no overload today.** `System.Numerics.BigInteger` **is usable**:
- API compatibility is `.NET Standard 2.1` (ProjectSettings `apiCompatibilityLevel: 6`), and the `Tools/CoreTests` CoreLib targets `netstandard2.1`.
- The type is already used in Unity EditMode tests: DataGeometryHpAcceptanceTests.cs:314,317 assert `IsInstanceOf<System.Numerics.BigInteger>`.
- Newtonsoft already creates BigInteger at runtime. Integers in `json-data.json` that exceed Int64 are parsed as BigInteger (BalanceDto.cs:7, BalanceRepository.cs:60).
  `Pcg32.State` values ≥ 2^63 appear in every save (the fixture has `"State":16686336070592401327`), and Newtonsoft reads these through BigInteger.
- Available in netstandard2.1: `Parse`/`TryParse(string, NumberStyles, IFormatProvider)`, `ToString(IFormatProvider)`, `Pow(BigInteger,int)`, `DivRem`,
  `GreatestCommonDivisor`, `Log10`, `Compare`, explicit `(double)` and `(long)` casts.
- **Not available** (net5+/net7+): `GetBitLength`, `IsPow2`, `Log2`, generic math `INumber<T>`. The dotnet **test** project targets `net8.0`
  (Tests.csproj.template), so a test that uses net8-only APIs compiles in dotnet but **fails in Unity**. Keep tests on the netstandard2.1 surface.

Recommended adapter (exact truncation, no promise above the true value):
```csharp
public static string Format(BigInteger v) {
    if (v.IsZero) return "0";
    if (v.Sign < 0) return "-" + Format(-v);
    if (v < (BigInteger)9007199254740992L) return Format((double)(long)v);          // exact below 2^53
    string s = v.ToString(CultureInfo.InvariantCulture);                               // digits only
    int k = Math.Min(15, s.Length);                                                    // 15 truncated digits: double never rounds up across a 10^n
    double m = double.Parse(s.Substring(0, k), CultureInfo.InvariantCulture) / Math.Pow(10, k - 1);
    return Format(new GameNumber(m, s.Length - 1));
}
```
Avoid `(double)big` and `BigInteger.Log10` for display. Rounding to nearest can turn `10^18 - 1` into `"1ab"` instead of `"999aa"`, and `Log10(1000)` can come
back as 2.9999…. Formatting is display-only, and purchases must compare the exact `BigInteger` (TZ §18).

### 5.3 Formula (`Formula.cs`, mostly DROP)

- `public const int RulesVersion = 1;` (:12). It is used by `GameState.RulesVersion` (default), `SaveService.Load` (:609), `RestartCurrentZone` and GameController. V2's
  `rulesVersion` is the **string** `"outpost-2.1-r1"`, plus `balanceVersion = "outpost-2.1-b1"`. Do not reuse this constant.
- `Evaluate(int formulaType, double b, double m, double g, double x, double power = 0.0, double round = 0.0)`, `Ft0`, `Ft1`, `Ft2`, `Ft3` (double),
  `Ft3Number` (GameNumber) (:15-66) are the old Appendix C.1 FormulaTypes. **DROP.**
- `Price(GameNumber raw)` (:83) is a tolerant ceil with a minimum of 1. `Payout(GameNumber raw)` (:90) is a tolerant floor with a minimum of 1. Both are wrong for V2:
  TZ §12.2 wants exact rationals (`1.20 = 6/5`, …), a power and product taken before one single `ceil`, and BigInteger output. Write a new
  `ExactMoney.NextCost(z, L)`, e.g. `ceil(6^(z-1) * BaseCost * GrowthNum^L / (5^(z-1) * GrowthDen^L))` with `BigInteger.DivRem`.
- `RoundToMultiple(double, double)` (:70) and `RoundHalfUp(double)` (:80) are generic double helpers.

### 5.4 Pcg32 (`Pcg32.cs`)

```csharp
[Serializable] public sealed class Pcg32 { public ulong State; public ulong Inc;               // :9-12
  public Pcg32(); public Pcg32(ulong seed, ulong stream);                                      // :14-23
  public uint NextUInt(); public double NextDouble() /*[0,1), 32-bit*/; public int NextInt(int maxExclusive) /*unbiased*/;
  public Pcg32 Clone(); }                                                                       // :25-50
[Serializable] public sealed class RngStreams { public Pcg32 Gacha, Delivery, Crit, Timers;    // :55-60
  public static RngStreams Create(ulong seed); }                                                // :62  streams 1..4
```
- `Pcg32`: **REUSE AS-IS.** It is deterministic and serializable as two `ulong` fields. Values ≥ 2^63 are written as raw JSON integers and read back by
  Newtonsoft through BigInteger. That works, but consider string serialization in V2 for tool-friendliness.
- `RngStreams`: **ADAPT**. Define V2 streams such as `Rewards` (equipment generator, TZ §10.2) and maybe `Crit`. Keep "one stream per system".
  `GameSession.FromState` (GameSession.cs:85-87) back-fills missing streams from a fixed seed `0x5EED`. Use the same pattern for missing V2 streams.

---

## 6. link.xml and IL2CPP stripping (Newtonsoft and BigInteger)

`Assets/Game/link.xml`:
```xml
<linker>
  <!-- DTO и модели сохранения создаются Newtonsoft через reflection: не вырезать при IL2CPP. -->
  <assembly fullname="CatMiners.Core" preserve="all"/>
  <assembly fullname="CatMiners.Runtime" preserve="all"/>
</linker>
```
Player settings (`ProjectSettings/ProjectSettings.asset`):
- `scriptingBackend: Android: 1` means IL2CPP.
- `managedStrippingLevel: Android: 4` means **Minimal**: only the .NET class libraries and UnityEngine are stripped, and user and third-party assemblies are copied whole.
- `apiCompatibilityLevel: 6` means .NET Standard 2.1.
- `stripEngineCode: 1`.

The Newtonsoft package (`Library/PackageCache/com.unity.nuget.newtonsoft-json@74deb55db2a0`, on the PC only):
- It ships `Runtime/Newtonsoft.Json.dll` plus a separate **`Runtime/AOT/Newtonsoft.Json.dll`** for IL2CPP, chosen by plugin import settings.
- Its own `link.xml` preserves only the `System.ComponentModel.*Converter` types, which Newtonsoft uses for dictionary keys and conversion.

Assessment:
- **At Minimal today:**
  - Newtonsoft and `CatMiners.*` are not stripped.
  - BigInteger lives in the class library `System.Numerics`, which can be stripped. Newtonsoft and our code reference its members **directly**, so the
    linker keeps them.
  - Reflection-only access to BigInteger would be at risk, for example through `TypeDescriptor` or `Convert.ChangeType`.
- **If a new asmdef is created for V2** (e.g. `CatMiners.Outpost`), **add it to link.xml**. Otherwise Medium or High stripping removes the DTO
  fields and constructors that Newtonsoft fills by reflection.
- **If stripping is ever raised to Medium or High**, also add `<assembly fullname="Newtonsoft.Json" preserve="all"/>` and, defensively,
  `<assembly fullname="System.Numerics"><type fullname="System.Numerics.BigInteger" preserve="all"/></assembly>`. Converters instantiated through
  `[JsonConverter(typeof(X))]` use `Activator`. Registering converters in `Settings()` (as now) avoids that path.
- Generic collections over value types (`Dictionary<string,long>`, `List<long>`, and V2's `Dictionary<string,BigInteger>`) are created by reflection.
  Unity 6 IL2CPP has a full-generic-sharing fallback, so the classic "no AOT code was generated" failures should not occur. However, **nothing here has
  been verified in a Player build or on a device** (the APK build is blocked, per project memory). TZ §18 requires checking real Player builds. Add a
  PlayMode or player smoke test: save, load and parse a BigInteger round-trip in the built player.

---

## 7. Tests: what to port and what couples

### SaveHardeningTests.cs (680 lines)

Generic patterns to **port** to the V2 store:
- `NaNInSnapshotIsRejectedBeforeAnyFileIsWritten` (:106) and `NaNSnapshotTouchesNoFileOperation` (:150): the snapshot is checked before any I/O, the same instance stays usable afterwards, and main is untouched.
- `PayloadAndEnvelopeAreByteIdenticalToPlainSerializer` (:191): run 2 passes to prove the buffers are reused. Relevant only if V2 keeps byte-compatibility with `JsonConvert`.
- `SaveLoadSaveKeepsPayloadSha` (:218): the TZ round-trip invariant.
- `QuarantineRenamesWithReasonAndUtcStampAndAvoidsCollisions` (:243), `CorruptedMainIsQuarantinedAndReportedWithSourcePath` (:283) and
  `QuarantineFailureIsReportedInLoadMessage` (:325, uses `RecordingFs.FailMove`).
- `MigrateAppliesStepsInOrderAndRejectsGaps` (:581) and `MigrationTableCoversEveryVersionBelowCurrent` (:573).
- `FutureSchemaVersionIsNotLoaded` (:658). In V2 the expected status changes from Corrupted to a distinct incompatible/future status.

**Old-gameplay coupled, to DROP or keep only for the archived profile:**
- `IncompatibleBalanceQuarantinesMainTmpAndBak` (:356), `IncompatibleBalanceQuarantinesCorruptLeftoverAsCorrupt` (:404) and
  `RulesVersionMismatchIsReportedWithSourcePath` (:426). These semantics conflict with TZ §17.
- `GenerateSaveV1MainFixture` (:487, `[Explicit]`, run via `run.ps1 -Filter GenerateSaveV1MainFixture`; refuses to overwrite) and
  `SaveV1FixtureLoadsThroughMigrationPath` (:509). They use `Fixtures/Saves/save_v1_main.json` and old `GameSession` commands (`Hire`, `DropCat`, `AcceptsCat`).
  For V2, create its own `Fixtures/Saves/outpost_v1_main.json` with the same "generate once, never overwrite" discipline.
- `OwnershipTableInSaveServiceCoversEveryPersistedField` (:630) **reads the source file `Assets/Game/Scripts/Core/Save/SaveService.cs`**. It parses the
  `/// |` table rows and reflects `GameState.Player` and `GameState.Mine`. Rewriting or moving SaveService.cs or GameState breaks it. Port it for
  V2 profile/run ownership (TZ §17 table).
- `NewSession()` uses `GameSession.NewGame(TestData.Balance, …)` and `s.Hire()`. `LegacySaveFormat` (:22) is the old envelope reference implementation.

### SavePerformanceTests.cs (198 lines)

- Uses the old gameplay to build a heavy state (`zone93`, `DebugSpawnCat`, `DropCat`, `AcceptsCat`). **ADAPT** with a V2 state containing 12 cats,
  a large map, cargo and a queue (TZ §19 load).
- Reusable harness: `Median`/`Percentile` (:30-39), `AllocatedPerRun` (:89-96, warm-up twice, then `GC.GetAllocatedBytesForCurrentThread`), and
  `ReportsDir()` (:47-53). The `-catminersReports <dir>` argument exists because Unity batchmode runs in a **copy under build/ci** where `Docs/` is absent.
  The fallback is `TestData.ProjectRoot/Docs/Reports`. `#if CATMINERS_DOTNET_TESTS` selects the environment name.
- Gotcha: the allocation counter returns 0 on some runtimes. The test then skips the comparison (`allocMeasured: false`).
- Latest numbers (dotnet, 2026-10-08):
  - serialize: median 0.37 ms, p95 0.63 ms. **Disk write with fsync: median 13.7 ms, p95 16.8 ms.**
  - allocations per save: 37 KB with buffer reuse, against 226 KB with the legacy writer.
  - Implications: TZ §17 demands a save at least every 2 s, and TZ §19 allows at most 1 KB of GC per frame. A synchronous fsync on the main thread every 2 s
    is a likely hitch on Android. Consider serializing on the main thread into a frozen byte buffer, then writing and fsyncing on a worker thread with a
    second buffer set. The instance is not thread-safe today. The ≤10 ms limit applies only to the in-memory snapshot.

### OfflineAcceptanceTests.cs (566 lines)

- All scenarios use the old gameplay (`Zone1WithCats` :43, `RichScenario`, `AssertSameByCriterion` :114+ comparing gold, HP, generators, cats, containers,
  FreeChest, RewardCycle and Pcg32). The comparison uses `GameNumber.ApproximatelyEqual(…, 1e-9)`. V2 must compare **exactly** (TZ §13).
- **Port these scenarios one-to-one to the V2 OfflineSimulator.** They encode the marker contract:
  - `Frames60HzEqualSingleAdvanceEqualOfflineByCriterion15_2` (:221). Helper `FrameTicks60(i) = (i+1)*S/60 - i*S/60` (:37) gives an exact 60 Hz tiling, and `FrameMs60` (:40) does the same in ms.
  - `FrameGapOverOneSecondIsCreditedOnceAsOffline` (:291).
  - `ConsecutivePauseFocusDoesNotCreditTwice` (:379), including a restart from the saved snapshot.
  - `ClockBackwardsGivesNothingUntilItCatchesUpWithTheMark` (:430).
  - `ClockSequenceKeepsMarkMonotonicAndCreditsOnlyNewTime` (:467): credited = min(8h, max(0, now − mark)) and mark = running max.
  - `OfflineCapIsExactlyEightHours` (:503): ms boundary, the cut is not carried over, and `MaxOfflineTicks == 28_800_000_000`.
  - `OfflineCapAppliesToAllSystems` (:538).
  - `ExtremeClockValueIsCappedWithoutOverflow` (:553).
- `FrameGapUsesOfflineRulesThenReturnsOnline` (:347) asserts the old frozen delivery timer. **DROP.** V2 has no offline-specific rules.
- Snapshot equality is checked via `SaveService.SerializeState`, and clone via `DeserializeState(Snapshot(s))`. Reuse this idea, because the TZ requires the
  identity to survive save/load between `Advance(a)` and `Advance(b)`.

---

## 8. What is generic enough for `CatOutpostV2` (TZ §17 checklist)

| TZ §17 requirement | Existing piece | Action |
|---|---|---|
| Separate dirs `persistentDataPath/CatOutpostV2/main` and `/debug`, each with `state.json`, tmp and backup | `SaveService(root, fs)`, `SlotPaths`, `CreateDirectory` on save | Pass the dir as root and rename the file to `state.json` (new `MainPath`) |
| Serialize, validate, then write tmp with flush | `WriteEnvelope` + `FiniteJsonTextWriter` + `RealFileSystem.WriteAllBytesDurable` (`Flush(true)`) | Reuse. Add a semantic validator (cargo owners, `Released <= Total`, caps) before writing |
| Atomic replace of main, keeping the previous valid main as backup | `Replace(tmp, main, bak)` | Reuse. Replace the `Copy/Delete/Move` fallback with a generation scheme or prove it with fault-injection tests |
| `commitSequence`, payload checksum | Only `payloadSha256` exists | Add `commitSequence` and make the checksum cover the header (or cross-check it inside the payload) |
| Load the valid committed main/backup with the highest `commitSequence`; tmp is not a commit | First valid of main → tmp → bak | Rewrite the selection. Never load tmp; leave it or quarantine it as `uncommitted` |
| Corrupt, incompatible or future files are not overwritten; keep copies; try backup; report clearly | `Quarantine` (rename with UTC stamp and collision suffix), `LoadResult.Message`/`QuarantinedFiles`, GameController `m_SaveBlocked` | Reuse the quarantine. Add a `FutureSchema` status. Keep "save blocked until user decision" |
| `modeId`, own `schemaVersion = 1`, `rulesVersion`/`balanceVersion` strings, source SHA, catalog hash | Envelope with int `rulesVersion` and `balanceSha256` | New header fields. `AppendJsonString`/`AppendInteger` already cover strings and longs |
| A balance change does not reset the run | `IncompatibleBalance` rejects, `RulesChangedMineReset` resets | Drop both behaviours. The run stores its parameter snapshot |
| Named migration with test and backup | `MigrateDocument` + `Migrations` table + fixture discipline | Reuse the mechanism, parametrized per profile |
| Exact money as a decimal string | `GameNumberConverter` (GameNumber only) | New `BigIntegerStringConverter` in `Settings()` |
| Retry the same prepared result after a save error | none (`SaveNow` swallows the exception) | Keep the last envelope bytes and sequence, and retry `WriteAllBytesDurable` + `Replace` with the same bytes |
| Archive old `save_main.json`/`.bak` without touching the originals | none (old `Load` *renames* files) | Copy-only archive. Read old settings read-only (§2) |
| UTC marker and `simulationTicks` in the same snapshot | `GameState.LastProcessedUtcMs` + `Player.PlayerTimeTicks`, `MarkProcessed` | Same fields in the V2 run state. Advance to the boundary before marking (§3.3) |

## 9. Misc gotchas

- Unity batchmode and CI: Unity runs tests in a copy under `build/ci`, hence `-catminersReports`. Save tests write to `Path.GetTempPath()`, never to
  `persistentDataPath`. PlayMode tests use `GameController.SaveDirectoryOverride` (PlayModeUtil.cs:33-39).
- Sync: `Tools/CoreTests` puts `obj/` in the repo-root `obj/` (Directory.Build.props), which Mutagen does not sync. Library and PackageCache exist only on the PC (`on-remote cmd /c …`, since the PC runs Windows).
- Newtonsoft version drift: dotnet tests use 13.0.3 and Unity uses 13.0.2. Byte-identity tests could in principle diverge between the two harnesses.
- Windows `File.Replace` can fail transiently (antivirus or the indexer holding the file), which sends the save down the non-atomic fallback. As far as I
  know, Mono's and IL2CPP's POSIX `File.Replace` are two renames, not one syscall. So a window with "no main, tmp + old bak" exists on Android too. A V2
  loader that ignores tmp falls back to bak (the previous commit), losing at most one save interval. Test this with `IFileSystem` fault injection.
- `GameNumber.ToStorageString` uses `"R"`. Round-trip is guarded by `SaveLoadSaveKeepsPayloadSha`. Keep that guard if GameNumber is persisted in V2.
- `Quarantine` timestamps have 1 s resolution and use the real `DateTime.UtcNow`. Tests pre-occupy the names from −2 s to +5 s to force the `-2` suffix.
- `Save` does not sanitize `Slot` for file names. Its envelope tests use a slot containing quotes and U+2028, but only through `BuildEnvelope`.
