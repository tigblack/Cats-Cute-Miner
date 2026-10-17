using System;
using System.Diagnostics;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController
    {
        long m_BaseTick, m_BaseMonoUs, m_BaseUtcMs, m_LastMonoUs, m_LastUtcMs;
        long m_PlayTimeMonoUs, m_PlayTimeRemainderUs;
        double m_NextSaveMono;
        long m_OfflineOriginMonoUs, m_OfflineOriginUtcMs;
        bool m_ShowOfflineSummary;
        long MonoUs => checked((long)Math.Floor(Clock.MonotonicSeconds * 1_000_000d));

        void RebaseClock(long? originMonoUs = null, long? originUtcMs = null)
        {
            m_BaseTick = Sim?.Now ?? 0;
            m_LastMonoUs = MonoUs;
            m_LastUtcMs = Clock.UtcNowMs;
            m_BaseMonoUs = originMonoUs ?? m_LastMonoUs;
            m_BaseUtcMs = originUtcMs ?? m_LastUtcMs;
            m_NextSaveMono = Clock.MonotonicSeconds + SaveIntervalSeconds;
        }

        void Update()
        {
            if (!m_Initialized || Sim == null) return;
            AccumulatePlayTime();
            StepRewardDelivery();
            if (m_Suspended) return;
            if (Phase == OutpostControllerPhase.OfflineCatchUp) StepOffline();
            else if (Phase == OutpostControllerPhase.Playing)
            {
                AdvanceOnline();
                if (Phase == OutpostControllerPhase.Playing && Clock.MonotonicSeconds >= m_NextSaveMono)
                {
                    Commit(State, Map, null);
                    m_NextSaveMono = Clock.MonotonicSeconds + SaveIntervalSeconds;
                }
            }
            StepBonus();
            if (Phase == OutpostControllerPhase.Playing)
            {
                PresentEvents();
                Board.Sync(Sim, FrameTime.Delta);
                Rig.SetContextTop(Hud.TryGetInfoTop(out float top) ? top : 0);
            }
        }

        void AccumulatePlayTime()
        {
            long mono = MonoUs;
            long delta = mono - m_PlayTimeMonoUs;
            m_PlayTimeMonoUs = mono;
            // Focus/pause callbacks exclude background intervals. Foreground hitches still count as play time.
            if (State == null || m_Suspended || m_SaveBlocked || SaveFailed || delta < 0 ||
                (Phase != OutpostControllerPhase.Playing && Phase != OutpostControllerPhase.Transitioning &&
                 Phase != OutpostControllerPhase.OfflineCatchUp)) return;
            long elapsed = delta + m_PlayTimeRemainderUs;
            long milliseconds = elapsed / 1000;
            m_PlayTimeRemainderUs = elapsed % 1000;
            State.Profile.PlayTimeMs += Math.Min(milliseconds, long.MaxValue - State.Profile.PlayTimeMs);
        }

        void AdvanceOnline(bool detectGap = true)
        {
            if (Phase != OutpostControllerPhase.Playing || Sim == null || SaveFailed) return;
            long mono = MonoUs, utc = Clock.UtcNowMs;
            long delta = mono - m_LastMonoUs;
            long utcDelta = utc - m_LastUtcMs;
            if (detectGap && (delta < 0 || delta > 1_000_000 || Math.Abs(utcDelta - delta / 1000) > 1000))
            {
                BeginOffline();
                return;
            }
            long elapsed = Math.Max(0, mono - m_BaseMonoUs);
            Sim.Advance(checked(m_BaseTick + elapsed * (IsDebugProfile ? m_Speed : 1)));
            Sim.Run.LastProcessedUtcMs = Math.Max(Sim.Run.LastProcessedUtcMs, m_BaseUtcMs + elapsed / 1000);
            m_LastMonoUs = mono;
            m_LastUtcMs = utc;
            bool completed = false;
            var events = Sim.Events;
            for (int i = 0; i < events.Count; i++)
                if (events[i].Type == OutpostEventType.StageCompleted) { completed = true; break; }
            Sim.DrainEvents(m_Events);
            if (completed) Commit(State, Map, null);
        }

        float m_NextVibration;
        void OnBoardCue(OutpostViewCueArgs cue)
        {
            m_Audio.Play(cue);
#if UNITY_ANDROID || UNITY_IOS
            if (State == null || !State.Profile.Settings.Vibration || !Application.isMobilePlatform || AudioListener.pause || Time.unscaledTime < m_NextVibration) return;
            if (cue.Cue == OutpostViewCue.Upgrade || cue.Cue == OutpostViewCue.Equip || cue.Cue == OutpostViewCue.SectorOpened || cue.Cue == OutpostViewCue.TeamGathered)
            {
                m_NextVibration = Time.unscaledTime + 0.4f;
                Handheld.Vibrate();
            }
#endif
        }

        void PresentEvents()
        {
            // Drain bounded work each frame. Completion remains hidden while its commit is pending.
            int count = Math.Min(96, m_Events.Count);
            for (int i = 0; i < count; i++)
            {
                var e = m_Events[i];
                Board.OnEvent(Sim, e, Fx);
                if (e.Type == OutpostEventType.SectorOpened && Rig.FocusZ > Board.BaseFocusZ + Rig.BandWorldHeight())
                {
                    var gate = Board.GateWorldPosition(e.Count - 1);
                    Rig.FocusOnSmooth(new UnityEngine.Vector2(gate.x, gate.z));
                }
                // Бочка динамита (ТЗ 10.6): камера плавно подходит к месту падения.
                if (e.Type == OutpostEventType.DynamiteLaunched && e.Cell >= 0)
                {
                    var at = Board.CellWorld(e.Cell);
                    Rig.FocusOnSmooth(new UnityEngine.Vector2(at.x, at.z));
                }
                if (e.Type == OutpostEventType.WatchdogFired) UnityEngine.Debug.LogError(Sim.Run.WatchdogDiagnostic);
                if (AndroidRuntime) Services?.GameAction(e, State.Run.Zone);
                EventPresented?.Invoke(e);
            }
            if (count > 0) m_Events.RemoveRange(0, count);
        }

        void BeginOffline(bool showSummary = true)
        {
            if (Sim == null || SaveFailed || m_SaveBlocked || m_Offline != null || InTransition) return;
            Input.CancelGesture();
            m_Events.Clear();
            Sim.Events.Clear();
            Phase = OutpostControllerPhase.OfflineCatchUp;
            m_ShowOfflineSummary = showSummary && !SuppressOfflineSummary;
            StartOfflineCalculation();
            Hud.RefreshNow();
        }

        void StartOfflineCalculation()
        {
            m_OfflineOriginMonoUs = MonoUs;
            m_OfflineOriginUtcMs = Clock.UtcNowMs;
            m_Offline = new OfflineSimulator(Map, Configs).Begin(State, m_OfflineOriginUtcMs);
        }

        void StepOffline()
        {
            if (m_Offline == null) return;
            long start = Stopwatch.GetTimestamp();
            do
            {
                if (m_Offline.Step(64))
                {
                    var job = m_Offline;
                    var map = Map;
                    long originMono = m_OfflineOriginMonoUs, originUtc = m_OfflineOriginUtcMs;
                    bool showSummary = m_ShowOfflineSummary;
                    m_Offline = null;
                    Commit(job.State, map, () =>
                    {
                        SetSimulation(job.State, map);
                        // Catch-up processing time belongs to ordinary online time after this boundary.
                        RebaseClock(originMono, originUtc);
                        if (job.Summary.SkipMinutes > 0) PendingSkip = job.Summary;
                        else if (showSummary && (PendingOffline != null || job.Summary.ElapsedMs >= 5000 || job.Summary.StageCompleted))
                            AccumulateOfflineSummary(job.Summary);
                        Phase = OutpostControllerPhase.Playing;
                    });
                    Hud.RefreshNow();
                    return;
                }
            } while ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency < 8d);
        }

        void AccumulateOfflineSummary(CatMiners.Core.Outpost.OfflineSummary summary)
        {
            if (PendingOffline == null) { PendingOffline = summary; return; }
            var pending = PendingOffline;
            // Publish a new value so an already displayed dialog refreshes as well.
            PendingOffline = new CatMiners.Core.Outpost.OfflineSummary
            {
                ElapsedMs = (long)System.Numerics.BigInteger.Min(long.MaxValue, (System.Numerics.BigInteger)pending.ElapsedMs + summary.ElapsedMs),
                AppliedMs = (long)System.Numerics.BigInteger.Min(long.MaxValue, (System.Numerics.BigInteger)pending.AppliedMs + summary.AppliedMs),
                GoldDelivered = pending.GoldDelivered + summary.GoldDelivered,
                UnitsDelivered = pending.UnitsDelivered + summary.UnitsDelivered,
                TargetsCleared = pending.TargetsCleared + summary.TargetsCleared,
                SectorsOpened = pending.SectorsOpened + summary.SectorsOpened,
                StageCompleted = pending.StageCompleted || summary.StageCompleted,
                ChestsOpened = pending.ChestsOpened + summary.ChestsOpened,
                CardsGained = pending.CardsGained + summary.CardsGained,
                ElixirGained = pending.ElixirGained + summary.ElixirGained
            };
        }

        public void DismissOffline() { PendingOffline = null; Hud.RefreshNow(); }
        public void DismissSkip() { PendingSkip = null; Hud.RefreshNow(); }

        void OnApplicationPause(bool paused) { m_Paused = paused; UpdateSuspension(); }
        void OnApplicationFocus(bool focused) { m_Unfocused = !focused; UpdateSuspension(); }
        void UpdateSuspension()
        {
            if (!m_Initialized || Clock == null) return;
            bool suspended = m_Paused || m_Unfocused;
            if (suspended == m_Suspended) return;
            AccumulatePlayTime();
            m_Suspended = suspended;
            Input?.CancelGesture();
            AudioListener.pause = suspended;
            if (suspended)
            {
                if (Phase == OutpostControllerPhase.Playing)
                {
                    // A long gap goes through the same sliced offline job on resume.
                    AdvanceOnline();
                    if (Phase == OutpostControllerPhase.Playing) Commit(State, Map, null);
                }
            }
            else if (Phase == OutpostControllerPhase.Playing) BeginOffline();
            else if (Phase == OutpostControllerPhase.OfflineCatchUp && m_Offline != null)
                // Partial work was never committed: restart on the same source with the full absence boundary.
                StartOfflineCalculation();
        }

        void OnApplicationQuit()
        {
            if (!m_Initialized || Sim == null || m_SaveBlocked || SaveFailed || Phase != OutpostControllerPhase.Playing) return;
            AdvanceOnline();
            if (Phase == OutpostControllerPhase.Playing) Commit(State, Map, null);
        }
    }
}
