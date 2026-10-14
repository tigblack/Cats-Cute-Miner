using System;
using System.IO;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController
    {
        OutpostGameState m_PendingSave;
        OutpostMap m_PendingMap;
        Action m_AfterSave;
        OutpostControllerPhase m_SavePhase;
        bool m_ResetBackup;

        void Commit(OutpostGameState state, OutpostMap map, Action after, bool resetBackup = false)
        {
            if (m_SaveBlocked || SaveFailed || state == null) return;
            AccumulatePlayTime();
            if (State != null) state.Profile.PlayTimeMs = State.Profile.PlayTimeMs;
            // Keep one immutable logical snapshot across retries. Never resubmit the command/reward.
            m_PendingSave = OutpostJson.DeepCopy(state);
            m_PendingMap = map;
            m_AfterSave = after;
            m_SavePhase = Phase;
            m_ResetBackup = resetBackup;
            TryCommit();
        }

        void TryCommit()
        {
            var result = m_Saves.Save(m_PendingSave, m_Slot, m_PendingMap, m_ResetBackup);
            if (!result.Ok)
            {
                Phase = OutpostControllerPhase.Error;
                ErrorText = result.Error;
                Input.CancelGesture();
                Hud.RefreshNow();
                return;
            }
            var after = m_AfterSave;
            m_PendingSave = null;
            m_PendingMap = null;
            m_AfterSave = null;
            m_ResetBackup = false;
            ErrorText = null;
            Phase = m_SavePhase;
            after?.Invoke();
        }

        public void RetrySave()
        {
            if (!SaveFailed || m_SaveBlocked) return;
            TryCommit();
            if (!SaveFailed && Phase == OutpostControllerPhase.Playing) PresentEvents();
            // Time spent resolving a failed online write is applied once through offline processing.
            if (!SaveFailed && Phase == OutpostControllerPhase.Playing && !m_Suspended && Clock.UtcNowMs - State.Run.LastProcessedUtcMs > 1000)
                BeginOffline();
            Hud.RefreshNow();
        }

        bool TryMigrateLoadedMine(OutpostGameState loaded)
        {
            if (!MineTopologyMigration.TryPrepare(loaded, Balance, Configs, out var prepared, out var map, out var diagnostic))
            {
                if (diagnostic != null) Debug.LogWarning("[Outpost] Mine layout retained: " + diagnostic);
                return false;
            }
            // Archive the on-disk saves before the first write. A failed archive leaves the old run usable.
            try
            {
                var fs = FileSystemOverride ?? new RealFileSystem();
                string dir = Path.Combine(m_Saves.SlotDirectory(m_Slot), "pre-wide-topology-" + Guid.NewGuid().ToString("N"));
                fs.CreateDirectory(dir);
                foreach (string path in new[] { m_Saves.MainPath(m_Slot), m_Saves.BakPath(m_Slot) })
                    if (fs.Exists(path)) fs.Copy(path, Path.Combine(dir, Path.GetFileName(path)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Outpost] Mine layout archive unavailable: " + e.Message);
                return false;
            }
            // The original simulation stays in memory if saving fails. Retry writes the same
            // prepared snapshot; camera, board and offline calculation change only after commit.
            Commit(prepared, map, () =>
            {
                SetSimulation(prepared, map);
                BeginOffline();
            }, resetBackup: true);
            return true;
        }

        void ImportLegacySettings(OutpostSettings settings)
        {
            if (settings.LegacySettingsImported) return;
            // Only settings are read; the legacy GameSession is never constructed.
            string name = IsDebugProfile ? "save_debug.json" : "save_main.json";
            string path = Path.Combine(SaveDirectory, name);
            try
            {
                var fs = FileSystemOverride ?? new RealFileSystem();
                if (fs.Exists(path))
                {
                    var root = JObject.Parse(System.Text.Encoding.UTF8.GetString(fs.ReadAllBytes(path)));
                    var payload = root["payload"] ?? root["Payload"];
                    var legacy = payload != null && payload.Type == JTokenType.String ? SaveService.DeserializeState((string)payload) : null;
                    var old = legacy?.Player?.Settings;
                    if (old != null)
                    {
                        settings.Master = old.SoundOn ? old.MasterVolume : 0;
                        settings.Music = old.MusicVolume;
                        settings.Sfx = old.SfxVolume;
                        settings.Ambience = old.AmbienceVolume;
                        settings.Vibration = old.VibrationOn;
                        settings.ReduceEffects = old.ReduceEffects;
                    }
                }
                else if (PlayerPrefs.HasKey("catminers.sound")) settings.Master = PlayerPrefs.GetInt("catminers.sound", 1) != 0 ? 1 : 0;
            }
            catch (Exception e) { Debug.LogWarning("[Outpost] Legacy settings unavailable: " + e.Message); }
            settings.Master = SafeVolume(settings.Master);
            settings.Music = SafeVolume(settings.Music);
            settings.Sfx = SafeVolume(settings.Sfx);
            settings.Ambience = SafeVolume(settings.Ambience);
            settings.LegacySettingsImported = true;
        }

        /// <summary>
        /// Карточки профиля при загрузке: миграция CardsVersion 0 → 1 и однократный перенос прежних карточек/эликсира. Перед
        /// первой записью изменённого профиля main/backup копируются в &lt;слот&gt;/pre-cards (один раз). true — нужна запись.
        /// </summary>
        bool PrepareCards(OutpostProfile profile, OutpostSaveSlot slot)
        {
            bool changed = CardService.MigrateProfile(profile, Configs.CardRules);
            changed |= ImportLegacyCards(profile);
            if (changed) BackupBeforeCards(slot);
            return changed;
        }

        void BackupBeforeCards(OutpostSaveSlot slot)
        {
            try
            {
                var fs = FileSystemOverride ?? new RealFileSystem();
                string dir = Path.Combine(m_Saves.SlotDirectory(slot), "pre-cards");
                if (fs.Exists(Path.Combine(dir, "state.json")) || fs.Exists(Path.Combine(dir, "state.json.bak"))) return;
                bool any = false;
                foreach (string path in new[] { m_Saves.MainPath(slot), m_Saves.BakPath(slot) })
                    if (fs.Exists(path))
                    {
                        if (!any) fs.CreateDirectory(dir);
                        any = true;
                        fs.Copy(path, Path.Combine(dir, Path.GetFileName(path)));
                    }
            }
            catch (Exception e) { Debug.LogWarning("[Outpost] Backup before cards migration failed: " + e.Message); }
        }

        /// <summary>
        /// Перенос карточек и эликсира прежнего режима по стабильным ID (save_main.json / save_debug.json в корне сохранений или
        /// его копия в CatOutpostV2/legacy-archive; берётся более поздний валидный). Один раз на профиль; актуальные данные
        /// нового профиля не перезаписываются. true — профиль изменён.
        /// </summary>
        bool ImportLegacyCards(OutpostProfile profile)
        {
            if (Configs?.Cards == null || profile.CardKeys != null && profile.CardKeys.Contains(CardService.LegacyImportKey)) return false;
            string name = IsDebugProfile ? "save_debug.json" : "save_main.json";
            var fs = FileSystemOverride ?? new RealFileSystem();
            CatMiners.Core.Sim.GameState best = null;
            long bestAt = long.MinValue;
            foreach (string path in new[] { Path.Combine(SaveDirectory, name),
                         Path.Combine(SaveDirectory, OutpostSaveService.RootFolder, OutpostSaveService.LegacyArchiveFolder, name) })
            {
                try
                {
                    if (!fs.Exists(path)) continue;
                    var root = JObject.Parse(System.Text.Encoding.UTF8.GetString(fs.ReadAllBytes(path)));
                    var payload = root["payload"] ?? root["Payload"];
                    if (payload == null || payload.Type != JTokenType.String) continue;
                    var legacy = SaveService.DeserializeState((string)payload);
                    long at = (long?)(root["savedAtUtcMs"] ?? root["SavedAtUtcMs"]) ?? 0;
                    if (legacy?.Player != null && at > bestAt)
                    {
                        best = legacy;
                        bestAt = at;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Outpost] Legacy cards unavailable in " + path + ": " + e.Message); }
            }
            var cards = new System.Collections.Generic.Dictionary<string, (int Level, long Duplicates)>(StringComparer.Ordinal);
            long elixir = 0;
            if (best != null)
            {
                if (best.Player.Cards != null)
                    foreach (var kv in best.Player.Cards)
                        if (kv.Value != null && kv.Value.Level > 0) cards[kv.Key] = (kv.Value.Level, kv.Value.Duplicates);
                double e = best.Player.Elixir.ToDouble();
                if (e > 0 && !double.IsInfinity(e)) elixir = (long)Math.Min(Math.Floor(e), long.MaxValue / 4);
            }
            bool changed = CardService.ImportLegacy(profile, Configs.Cards, cards, elixir, out int imported);
            if (changed && (imported > 0 || elixir > 0))
                Debug.Log("[Outpost] Legacy cards imported: " + imported + ", elixir " + elixir);
            return changed;
        }

        public void RestartDebugCampaign()
        {
            if (DebugAllowed) ResetProgress();
        }

        /// <summary>Явное начало заново из настроек (включая WebGL) или Dev-панели.</summary>
        public bool CanResetProgress => !CatMiners.Runtime.Purchasing.ParentPurchaseGate.IsOpen && CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Pending != true && !PaymentRules.Pending(State?.Profile) && Services?.IsRewardShowing != true && Services?.HasEarned(State?.Profile.PendingRewardedOffer?.Id) != true && !m_RewardShowing && m_EarnedCallbacks.Count == 0 && State?.Profile.PendingRewardedOffer?.Earned != true && m_Saves != null &&
            !SaveFailed && !m_Suspended && !InTransition && m_Offline == null &&
            (Phase == OutpostControllerPhase.Playing || Phase == OutpostControllerPhase.Error && m_SaveBlocked);

        public void ResetProgress()
        {
            if (!CanResetProgress) return;
            try
            {
                // Архив не участвует в автозагрузке. Main/Debug остаются независимыми.
                var fs = FileSystemOverride ?? new RealFileSystem();
                string archive = Path.Combine(m_Saves.SlotDirectory(m_Slot), "restart-" + Guid.NewGuid().ToString("N"));
                fs.CreateDirectory(archive);
                foreach (string path in new[] { m_Saves.MainPath(m_Slot), m_Saves.BakPath(m_Slot), m_Saves.TmpPath(m_Slot) })
                    if (fs.Exists(path)) fs.Copy(path, Path.Combine(archive, Path.GetFileName(path)));
                ulong seed = unchecked((ulong)Clock.UtcNowMs ^ ((ulong)(uint)Environment.TickCount << 32));
                var state = StageProgressionService.NewGame(Balance, Configs, seed, Clock.UtcNowMs);
                PaymentRules.PreserveAcrossReset(State?.Profile, state.Profile);
                if (State?.Profile.Settings != null) state.Profile.Settings = OutpostJson.DeepCopy(State.Profile.Settings);
                state.Profile.Settings.LegacySettingsImported = true;
                // Старые карточки и эликсир не возвращаются из legacy-сохранения после начала заново.
                state.Profile.CardKeys.Add(CardService.LegacyImportKey);
                StoreService.MigrateProfile(state.Profile, Configs.Store);
                var map = ResolveMap(state);
                m_SaveBlocked = false;
                Commit(state, map, () =>
                {
                    m_Speed = 1;
                    OutpostDebugOverlay.Enabled = false;
                    PendingOffline = null;
                    PendingSkip = null;
                    m_NextStrikeTap = double.NegativeInfinity;
                    Input.CancelGesture();
                    Board.gameObject.SetActive(true);
                    SetSimulation(state, map);
                    Tutorial?.Dispose();
                    Hud.Hint?.Hide();
                    Tutorial = new OutpostTutorial(this, Hud);
                    Phase = OutpostControllerPhase.Playing;
                    Hud.CloseAllWindows();
                    Hud.RefreshNow();
                }, resetBackup: true);
            }
            catch (Exception e) { Fatal(e.Message); }
        }

        static float SafeVolume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp01(value);
        void ApplySettings()
        {
            var s = State.Profile.Settings;
            AudioMix.Set(false, s.Master, s.Music, s.Sfx, s.Ambience);
            VisualSettings.ReduceEffects = s.ReduceEffects;
        }
    }
}
