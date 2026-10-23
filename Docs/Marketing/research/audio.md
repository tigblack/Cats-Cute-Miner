# Audio inventory and soundtrack plan for the Cat Miners ads

Everything below was checked read-only. Nothing in the project was created or changed. The test renders and measurements ran in memory or through pipes to stdout.

The game currently runs in Outpost mode (`GameBootstrap.LegacyMode=false`), so SFX reach `Sfx` through `OutpostAudio`. The older `GameController.Present` mapping is listed only for reference.

**Key files**
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Audio/Sfx.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Outpost/OutpostAudio.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Outpost/View/OutpostBoardView.Events.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Outpost/View/OutpostBoardView.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Outpost/View/OutpostBoardView.Strike.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Outpost/OutpostController.cs and OutpostController.Loop.cs (same folder)
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/Audio/UiSound.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Runtime/UI/PixelUi.cs
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Tools/Audio/music.py
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/Audio/build_sfx.py
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/Audio/SOUNDS.md
- /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/Audio/CREDITS.md
- Hub copies of the audio (hub-only; `/Temp` is excluded from Mutagen, mutagen-ignores.txt:8):
  - /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/audio/out/
  - /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/audio/amb/
  - /mnt/claude-data/sargsyan-tinote/cats-cute-miner/Temp/audio/music/

## 1. SFX files, triggers and licenses

**What is on the PC:** 70 `.ogg` files in `Assets/Game/Resources/Audio` (57 SFX, 11 ambience, 2 music) and 2 masters in `Docs/Audio/masters`.
- All SFX are mono, 44.1 kHz.
- Durations come from Ogg granule positions, read on the PC.
- Mixing rules (build_sfx.py:113, :443): each file is normalised to −18 dBFS RMS of its audible part, with peaks at or below −1 dBFS. Weapon files peak at or below −2 dBFS.

**SHA-256 check against the PC:** 58 of the 72 PC files are already on the hub, byte-identical:
- 43 SFX in `Temp/audio/out/`
- 11 ambience files in `Temp/audio/amb/`
- 4 files in `Temp/audio/music/` (the 2 game tracks and the 2 masters)

The only files missing on the hub are the 14 `weapon_*.ogg`, and Outpost mode never plays them.

**Column legend:** values from `Sfx.Create` are volume / random pitch ±% / minimum interval (s) / priority. Priorities are F (frequent), N (normal), J (jingle).

| Files (duration, s) | Cue (Sfx.cs line) | Outpost trigger (current game) | Older trigger (GameController.cs) |
|---|---|---|---|
| hit_0..3 (0.200) | m_Hit :89, 0.22/4/0.10/F | Cue `Hit` (TargetHit, stone) → `Sfx.Hit(material)` OutpostAudio.cs:53 | TargetHit :588 |
| hit_crystal_0..2 (0.320) | :109, 0.20/4/0.10/F | same, crystal | same |
| hit_wood_0..2 (0.160) | :110, 0.24/4/0.10/F | same, wood | same |
| hit_metal_0..1 (0.220) | :111, 0.20/4/0.10/F | same, metal | same |
| player strike (reuses hit clips) | Variant :125–128: vol 0.34/0.30/0.34/0.30, base pitch 0.90/0.92, ±5 %, 0.06 s, N | Cue `PlayerStrike` → `Sfx.PlayerStrike` OutpostAudio.cs:54 | — |
| break_0..2 (0.450) | :90, 0.42/5/0.08/F | `TargetBroken` (TargetDestroyed) → `Sfx.Break` :55 | RockDestroyed :592, GateBroken |
| gate_0 (0.950) | :99, 0.65/0/0.5/J, level 2 | `SectorOpened` → `Sfx.GateOpen` :56 | CheckpointOpened :651 |
| exit_0 (0.950) | :100, 0.70/0/0.5/J, level 2 | `TeamGathered` (StageCompleted) → `Sfx.ExitOpen` :57 | ExitBroken / CampaignCompleted |
| meow_0..3 (0.240/0.398/0.223/0.447) | :94, 0.5/5/0.15/N | `WorkerSpawned` (WorkerHired) → `Sfx.Meow` :58 | CatSpawned :643 |
| upgrade_0 (0.550) | :96, 0.5/0/0.12/N; muted while a jingle plays (:203–207) | `Upgrade` (TeamUpgradePurchased) → `Sfx.Upgrade` :59; card upgrade OutpostHud.Cards.cs:279 | :664 |
| chime_0..1 (0.500) | :92, 0.3/3/0.12/F | `Equip` (EquipmentChanged / ItemUpgraded) → `Sfx.Chime` :60 | RewardGranted (non-gold) :611 |
| coin_0 (0.320), played on OutpostAudio's own sources, bypassing `Sfx` | — | `Deposit`: vol 0.28, pitch 0.95 + min(0.2, 0.03·log10 value), 2 voices by depot slot, 0.12 s throttle, OutpostAudio.cs:47–52. `SackPop` / `Pickup` / `UnloadDone`: vol 0.1, pitch 1.15. `BagFull`: vol 0.1, pitch 0.7. These share 1 voice with a 0.2 s throttle (:61–64). | coin_0..2 via `Sfx.Coin` :91 (gold, :610); LootDrop variant :123 |
| click_0 (0.100) | :101, 0.35/2/0.05/N, via `UiSound.Click` | every kit button; also `Footstep`: vol 0.045, pitch 0.65, 1 voice, 0.25 s throttle (OutpostAudio.cs:43–46) | Hire |
| open_0 (0.240) / close_0 (0.180) | :102 / :103, 0.30 / 0.28 | window `Show`/`Hide` (PixelUi.cs:868/876); Cards / Equipment / Store panels (OutpostHud.Cards.cs:148/158, .Equipment.cs:286/300, .Store.cs:181/196) | ContainerOpened :675 |
| denied_0 (0.220) | :104, 0.38/0/0.2/N | `OutpostHud.Denied` (OutpostHud.cs:585); tap on a disabled button (PixelButtonPress.cs:64) | CommandRejected |
| Not used in Outpost mode: payout_0..1 (0.55), merge_0 (0.65), unlock_0 (0.80), chest_0 (0.46), dynamite_0 (0.80), cannon_0..1 (0.45), land_0..1 (0.20), select_0 (0.07), weapon_{hammer 0.30, crusher 0.22, saw 0.32, drill 0.34, dualpick 0.25, chainsaw 0.38, laser 0.26}_0..1 | :93, :95, :97, :98, :105, :106, :121, :108, :114–120 | — | :638, :646, :656/659/668, :672, :678, :632, :805, :908 |

The store chest window plays only the open/close sounds, with no reveal sound. For ad stingers, `chest_0`, `payout_0` and `unlock_0` can be added in post.

**Ambience (original):** `amb_bed` is a 45.000 s stereo loop, which happens to equal the ad length. The rest: `amb_drip_0..3` 2.0 s; `amb_creak` 1.017 / 1.072 / 0.680 s; `amb_pebbles` 1.6 s; `amb_machine` 2.6 s. All are random events from Ambience.cs:288–319.

**Licenses**
- All third-party sources are CC0 1.0 (CREDITS.md:214–228): Kenney Impact Sounds, RPG Audio and Music Jingles; BigSoundBank (Joseph Sardin); OpenGameArt (IgnasD, Spring Spring, rubberduck).
- The music, the ambience and the synthesised layers (crystal "ting", weapon motors, laser) are original code output (CREDITS.md:230–234).
- **Paid ads are fine:** CC0 allows commercial and advertising use with no attribution.
- Residual risks are small:
  - The CC0 status on OpenGameArt and BigSoundBank is declared by the uploader. Keep CREDITS.md and its links as evidence.
  - A third party could, in theory, claim an identical public-domain sound in a rights-matching system. That is unlikely for short SFX and impossible for the synthesised music.

## 2. Music, and making a 45 s ad track with music.py

**The two game tracks**

| Track | Tempo / key / length | Instruments | Mood | Measured on the master |
|---|---|---|---|---|
| `music_warm` "Warm Adit" | 82 BPM, F major, 48 bars, 140.488 s | marimba, pluck, celesta, pad, bass, light shaker/kick/rim | cozy, calm | −14.6 LUFS, true peak −1.2 dBTP, LRA 6.5 |
| `music_crystal` "Crystal Depth" | 72 BPM, D Lydian, 44 bars, 146.667 s | FM bells, glass pad, celesta sparkles, harp, sub-bass, drops | airy, dreamy | −13.4 LUFS, true peak −1.9 dBTP |

- Both are seamless loops: rendered with the reverb tail folded back into the start. Section plans are at music.py:323–331 and :450–458.
- The game picks the track by zone: OutpostController.cs:171, `(Zone-1)%3==1` → Crystal.
- Both are too slow and calm for an upbeat ad. Their melodies can be quoted for brand continuity, e.g. `THEME_A` (music.py:332) with `transpose=-3` to move it into D.

**music.py building blocks** (all usable as a module)

| Block | Location | Notes |
|---|---|---|
| `SR = 44100` | :26 | |
| `hz(midi)` | :30 | |
| `lp` / `hp` / `bp` | :34–43 | Butterworth filters |
| `ramp_in`, `fade_out` (cosine) | :46, :53 | |
| Instruments `fn(f_hz, dur_beats_s, vel, rng)` → mono array | :61–150 | `marimba` :61, `pluck(decay, bright)` :74, `celesta` :88, `pad(attack, release, bright)` :100, `glass` :119, `bell` (FM) :131, `bass` :142 |
| `INSTR` dictionary | :188 | adds `harp`, `glasspad`, `subbass` |
| Percussion `fn(vel, rng)` | :153–185 | `shaker` :153, `kick` :161 (52 Hz plus a 48 Hz pitch drop, 0.14 s decay), `rim` :169, `drop` :178; dictionary `PERC` at :191 |
| `reverb_ir(...)` | :196 | 3-band noise impulse response |
| `Mix(seconds, seed)` | :213 | `.add(y, start_s, pan, gain, send)` :222 (equal-power pan); `.render(reverb_seconds, wet, seed, **ir)` :235 → stereo `(2, n)` with a fixed 30 Hz high-pass and 11 kHz low-pass (:242) |
| `master(x, rms_db=-17, peak_db=-1)` | :246 | RMS normalise, tanh soft-knee above −4 dBFS, sample-peak cap |
| `fold_loop` | :264 | loop folding; **must not be used for the ad** |
| `Score(bpm, bars, seed)` | :275 | `.add(bar 1-based, beat float, dur_beats, midi, inst, vel, pan, send, human=True)` :285; human = ±6 ms timing, ±7 % velocity; `.melody(bar, [(beat, dur, midi)], inst, vel, pan, send, transpose)` :292; `.render(tail, seed, **rev)` :296 **always calls `fold_loop` (:305)** |
| `analyse` | :548 | |
| `write_ogg` | :570 | writes in chunks to avoid a libsndfile 1.2 crash on long files; `sf.write` to WAV/FLAC is fine for 45 s |

**Feasibility test (in memory):** a 22-bar, 120 BPM groove (pad, bass on every beat, eighth-note marimba, four-on-the-floor kick, shaker) imported via `sys.path.insert(0,'Tools/Audio'); import music`:
- Rendered in 7.6 s (about 10 s total wall time).
- After `master()`: −15.0 LUFS integrated, true peak −3.2 dBTP, LRA 1.9.

So yes, music.py can be reused. A new script (for example `Tools/Audio/ad_music.py`, to be written by a later step) needs the following:

1. **Length and tempo.** Fix the length at 45.0 s.
   - **128 BPM is the cleanest choice:** one bar is 1.875 s, so 45 s is exactly 24 bars (`Score(128, 24)`; `Score.length` at :281 then equals 45.0).
   - 120 BPM gives 2.0 s bars (22.5 bars), so hit points land on even seconds.
   - Position of any note: `t = ((bar-1)*4 + beat) * 60/BPM`.
   - For given hit timestamps, pick the BPM (and optionally a start offset) that puts them on beats or eighth notes. Use `human=False` for hits.
2. **A non-looping render.** Copy `Score.render` lines :297–303 into a new `render_linear()` that skips `fold_loop`. Render at 45 s plus a tail, cut to `int(45.0*SR)` samples and apply a 20–50 ms cosine fade at the end.
   - Place the final chord about 1.9 s before the end, e.g. 43.125 s (bar 24, beat 0 at 128 BPM), or the downbeat of bar 22 = 42.0 s at 120 BPM.
   - Use a shorter reverb, or fade out from about 44.0 s, so the tail dies cleanly by 45.0 s.
3. **Separate buses.** `Mix` has one dry bus, so use one `Mix` per group (drums / bass / music / effects) and sum them. This allows sidechain ducking (multiply the pad and bass buses by a gain envelope driven by the kick times) and a low-pass filter sweep during the build. The sweep can be done in blocks with `sosfilt` and a carried filter state `zi`.
4. **Missing sounds that need writing** (about 20–40 lines each, numpy only):
   - clap or snare: noise band-passed to 1–5 kHz plus a 180–200 Hz body; a clap uses 3 micro-hits
   - closed and open hi-hat: noise above about 7 kHz. This bus must skip the 11 kHz low-pass at :242.
   - crash cymbal
   - riser: noise with a rising band-pass plus a rising sine, over 2–4 bars
   - snare roll that speeds up from 16ths to 32nds
   - impact / sub drop: sine sweeping 60→35 Hz plus a noise transient
   - punchier kick with a 100–150 Hz body and a click
   - catchy lead: band-limited saw or square built additively (like `pad()` but with a plucked envelope). A square lead also fits the pixel-art style.
5. **Key: D major is recommended.** A rough FFT pitch track of the hub copies suggests the stingers most likely to be layered are in D major:
   - `upgrade_0` D→E
   - `unlock_0` and `gate_0` D–E–F#–G
   - `exit_0` A–F#–A
   - These jingles play with no pitch jitter (Sfx.cs:96–100), so their pitch is fixed.
   - `chest_0` (D#/A#) clashes; pitch it down 1 semitone in the rebuild if it is used.
   - `CRYSTAL_CHORDS` (music.py:439) already holds D voicings (Dmaj7, Bm7, Gmaj7, Em7, A6, Asus2, D/F#), ready for I–V–vi–IV.
6. **Output.** Write a 44.1 kHz stereo WAV/FLAC stem at about −18 LUFS, with no `master()` soft clipping. Do loudness and limiting once, on the final mix (section 5). pyloudnorm is not installed, so measure with ffmpeg `ebur128` through a pipe.

**Quality risks**
- The existing instruments are soft and band-limited (pluck harmonics stop at 6.5 kHz, pad at 5 kHz), so they sound lo-fi, not punchy.
- The kick's 52 Hz fundamental is inaudible on phone speakers.
- Reverb at wet 0.5–0.6 is too washy for an upbeat track; use 0.2–0.3 with drums dry.
- `master()` tanh clipping above −4 dBFS will distort a dense drop.
- How catchy the track is depends on the writing; use a 2-bar hook repeated A/A′.
- Renders are deterministic (fixed seeds), so iterating is cheap: about 10–30 s per render.

## 3. Where SFX are triggered, and the proposed logging hook

**Event chain in Outpost mode**
1. Core `OutpostEvent`s are drained per frame, at most 96 per frame, so a sound can arrive 1+ frames late under load. `OutpostController.Update` (Loop.cs:27–47) calls `PresentEvents` (Loop.cs:102–118), which calls `Board.OnEvent` (Events.cs:22–178).
2. `OnEvent` raises `OutpostBoardView.Cue` (event declared at OutpostBoardView.cs:82; `Emit` at :710–717). Cue sources by line:

   | Cue | File:line |
   |---|---|
   | Hit | Events.cs:222–229 |
   | SackPop | Events.cs:49–56 |
   | Pickup | Events.cs:66 |
   | TargetBroken | Events.cs:85–90 |
   | SectorOpened | Events.cs:108–113 |
   | TeamGathered | Events.cs:121 |
   | Upgrade | Events.cs:127–132 |
   | WorkerSpawned | Events.cs:151 |
   | Equip | Events.cs:175 |
   | Deposit / UnloadDone | Events.cs:254–267 |
   | Footstep, at most 1 per frame, from `Board.Sync` | OutpostBoardView.cs:464–467 |
   | BagFull | OutpostBoardView.cs:470–475 |
   | PlayerStrike | OutpostBoardView.Strike.cs:75–80 |

3. The controller subscribes (OutpostController.cs:101). `OnBoardCue` (Loop.cs:90–92) calls `OutpostAudio.Play` (OutpostAudio.cs:37–66), which either calls a `Sfx` method or plays through its own `PlayVoice` (:32–36).

**UI sounds:** kit buttons add `UiSound.PlayClick` as a listener (PixelUi.cs:398, :451, :569; toggle :739; HudBlueprints.cs:412; PixelSlider.cs:76). `Sfx.Create` binds `UiSound` to `Sfx.Click/Open/Close/Denied` (Sfx.cs:131–134). All of these end in `Sfx.Play` (Sfx.cs:260–281).

**Two consequences for the capture harness**
- **Scripted purchases are silent unless they go through a button.** Calling `Game.Buy(...)` directly, as `OutpostGameplayRecordingTests.BuyFromDeliveredWallet` does, produces no click; only the `Upgrade` jingle cue plays. To get the click, invoke `ButtonRefs.Button.onClick.Invoke()`.
- **Throttling uses wall-clock time.** Throttles and jingle suppression use `Time.unscaledTime` (Sfx.cs:205, :213, :236, :263; OutpostAudio.cs:40). Under frame-stepped capture this may not match video time. Log `Time.unscaledTime` alongside each event; if it drifts from frame/fps, re-apply the throttle tables (Sfx.cs:89–128; OutpostAudio 0.25 / 0.12 / 0.2 s) on the hub in video time.

**Gating and real-time capture**
- Plays are skipped when `AudioListener.pause` is set or `AudioMix.SfxGain <= 0` (OutpostAudio.cs:34, :39), or when `Sfx.Enabled` is false (Sfx.cs:262). To capture silently but still log, keep Master and SFX at 1 and set `AudioListener.volume = 0`.
- A real-time tap already exists (`GameplayRecordingTests.AudioTap`, GameplayRecordingTests.cs:268–300, used at OutpostGameplayRecordingTests.cs:91, :133–143). It only stays in sync when the game runs at real-time speed (15 fps 540×960 there), so it is not suitable for frame-stepped 1080×1920 capture.

**Minimal hook.** One static delegate covers every SFX, including UI and OutpostAudio's own voices. It is free when null and does not allocate:
```csharp
// Sfx.cs, add near line 49 (fields):
/// <summary>Capture hook (tests only): a sound actually started/stopped. voice = AudioSource instance id; clip null = stop.</summary>
public static Action<AudioClip, float, float, int> Played;

// Sfx.cs:277, right after `src.Play();`
Played?.Invoke(clip, src.volume, src.pitch, src.GetInstanceID());

// Sfx.cs:256 (StopReward), turn into: if (...) { src.Stop(); Played?.Invoke(null, 0f, 0f, src.GetInstanceID()); }

// OutpostAudio.cs:35 (PlayVoice), after `source.Play();`
Sfx.Played?.Invoke(clip, source.volume, source.pitch, source.GetInstanceID());
```
- Optional: the same call at Ambience.cs:319. Not needed if the ad uses a bed instead.

**Harness side**
- Subscribe in SetUp, set back to null in TearDown.
- Log one JSON line per event: `{captureFrame, Time.frameCount, Time.unscaledTime, clip?.name ?? "STOP", volume, pitch, voice}`. Clip names equal the file names, e.g. `hit_2`. The fallback synth clips would be named `hit` etc. (Sfx.cs:465), but they are only used if a file is missing.

**Offline rebuild on the hub**
- Start time = captureFrame / fps.
- Load `Temp/audio/out/<name>.ogg`.
- Resample by `(48000/44100)/pitch`. Unity's pitch changes both pitch and duration, exactly like `build_sfx.pitch` (build_sfx.py:59–66). Use `resample_poly` with a `Fraction(...).limit_denominator(2000)` ratio.
- Multiply by `volume`; a 2D mono source at `panStereo 0` goes equally to L and R.
- A new start on the same `voice` cuts the previous clip on that voice (add a 3–5 ms fade). `STOP` cuts immediately.
- The PC's Unity output rate is 48 kHz (recording.json; ProjectSettings/AudioManager.asset:11 `m_SampleRate: 0` means device default).

## 4. Can soundfile on the hub read the .ogg files?

**Yes.**
- soundfile 0.14.0 with libsndfile 1.2.2: `available_formats()` includes `OGG`, and `available_subtypes('OGG')` returns `{'VORBIS', 'OPUS'}`.
- An in-memory Vorbis write/read round trip works.
- `sf.info` reads the hub copies, e.g. `break_0.ogg` 44100 Hz, 1 channel, 0.45 s VORBIS; `music_warm_master.ogg` 2 channels, 140.488 s; `amb_bed.ogg` 2 channels, 45.0 s.
- **Fetching is unnecessary** for every Outpost-mode SFX, the ambience and the music: they are already on the hub, hash-verified, in `Temp/audio/{out,amb,music}`.
- Only the `weapon_*` files would need `on-remote --fetch Assets/Game/Resources/Audio/weapon_<x>_<n>.ogg`.
- The hub ffmpeg also has `libvorbis`, `vorbis`, `opus` and `aac` codecs, plus `loudnorm`, `ebur128`, `alimiter`, `sidechaincompress`, `amix`, `adelay`, `afade`, `apad`, `atrim` and `rubberband`.

## 5. Loudness target and how to measure it

**Target:** Meta's Ads Guide only specifies stereo AAC at 128 kbps or higher (H.264 video, fixed frame rate). It publishes no LUFS target; Meta normalises playback itself (xHE-AAC loudness control). The usual safe master is:
- **−14 LUFS integrated**
- **true peak at or below −1 dBTP**, measured after AAC encoding (aim for −1.5 to −2 dBTP before encoding)
- AAC 192 kbps, 48 kHz stereo

**Suggested balance:** music stem at about −18 LUFS. Key SFX (hits, meows, jingles) should poke 3–6 dB above the music for a moment. Duck the music 3–4 dB under jingles (`sidechaincompress` or a numpy gain envelope).

**Measuring** (`FF=Tools/.venv/lib/python3.12/site-packages/imageio_ffmpeg/binaries/ffmpeg-linux-x86_64-v7.0.2`):
- Report: `$FF -hide_banner -nostats -i ad.mp4 -af ebur128=peak=true -f null - 2>&1 | grep -E "I:|LRA:|Peak:"` prints integrated loudness, LRA and true peak.
- Numpy without temp files: pipe `x.T.astype('<f4').tobytes()` into `$FF -f f32le -ar 44100 -ac 2 -i - -af ebur128=peak=true -f null -`. This is how the numbers above were measured.

**Normalising in two passes:**
1. `-af loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json -f null -` and read `input_i`, `input_tp`, `input_lra`, `input_thresh`, `target_offset`.
2. `-af loudnorm=I=-14:TP=-1.5:LRA=11:measured_I=..:measured_TP=..:measured_LRA=..:measured_thresh=..:offset=..:linear=true -ar 48000 -c:a aac -b:a 192k`.
- Without `linear=true`, or when the gain needed would break the true-peak limit, loudnorm falls back to dynamic mode. This already happened in a single-pass test on `music_warm`. To prevent it, put `alimiter=limit=0.8:level=false` (about −2 dBFS sample peak) before loudnorm.
- Always set `-ar 48000`: loudnorm resamples to 192 kHz internally.
- Pad or trim the audio to exactly 45.000 s (`apad,atrim=0:45`, plus `-shortest`), then re-run `ebur128` on the final mp4 to confirm I ≈ −14 ±0.5 and true peak ≤ −1.0 dBTP.

Sources:
- [Meta Ads Guide – video ad specs (Facebook Feed)](https://www.facebook.com/business/ads-guide/update/video)
- [Engineering at Meta – Why xHE-AAC is being embraced at Meta](https://engineering.fb.com/2023/04/11/video-engineering/high-quality-audio-xhe-aac-codec-meta/)
- [Social Media Today – Meta updates audio encoding for Reels and Stories](https://www.socialmediatoday.com/news/meta-updates-audio-encoding-for-reels-and-stories-to-provide-more-volume-co/647414/)
- [danmurtagh.com – LUFS loudness standards (no official social-platform targets)](https://danmurtagh.com/lufs-loudness-standards)
- [cutscore.io – −14 LUFS target for Reels](https://cutscore.io/blog/loudness-for-instagram-reels)
- [Strike Social – Meta ad specs 2026](https://strikesocial.com/blog/meta-ad-specs/)