using System;
using System.Collections.Generic;

namespace CatMiners.Core.Sim
{
    public sealed class OfflineSummary
    {
        public long Ticks;
        public bool Capped;
        public GameNumber Gold;
        public GameNumber Elixir;
        public int RocksDestroyed;
        public int Chests;
        /// <summary>Расчищенные шахты, разрушенные ворота и выход вместе.</summary>
        public int TargetsBroken;
        /// <summary>Расчищенные шахты (ждут активации).</summary>
        public int ShaftsCleared;
        /// <summary>Разрушенные ворота (ждут открытия), ТЗ 12.3.</summary>
        public int GatesBroken;
        /// <summary>Выход разрушен за офлайн, ТЗ 12.3.</summary>
        public bool ExitBroken;
        public int PayoutsReady;
        public int ContainersSpawned;

        public double Seconds => Ticks / (double)GameSession.TicksPerSecond;
        public bool IsEmpty => Ticks <= 0;

        /// <summary>
        /// Есть что показать в окне «Пока вас не было» (ТЗ 12.3): золото или эликсир, разрушенные объекты, сундуки,
        /// ворота или выход, готовые выплаты, контейнеры. Порога по длительности нет.
        /// </summary>
        public bool HasNews =>
            Gold.IsPositive || Elixir.IsPositive || RocksDestroyed > 0 || TargetsBroken > 0 || Chests > 0 || PayoutsReady > 0 || ContainersSpawned > 0;
    }

    /// <summary>
    /// Офлайн-прогресс (ТЗ 12.3): тот же Advance, лимит 8 ч, монотонная метка lastProcessedUtc.
    /// Сначала результат и новая метка сохраняются, затем UI показывает окно «Пока вас не было».
    /// </summary>
    public static class OfflineProgress
    {
        public static OfflineSummary Apply(GameSession session, long nowUtcMs)
        {
            var summary = new OfflineSummary();
            var state = session.State;
            long last = state.LastProcessedUtcMs;
            long deltaMs = nowUtcMs - last;
            if (deltaMs <= 0 || session.Player.CampaignCompleted)
            {
                // Часы переведены назад или ещё не догнали метку: офлайн не начисляется.
                MarkProcessed(session, nowUtcMs);
                return summary;
            }
            long ticks = deltaMs > long.MaxValue / 1000L ? GameSession.MaxOfflineTicks : deltaMs * 1000L;
            if (ticks > GameSession.MaxOfflineTicks)
            {
                ticks = GameSession.MaxOfflineTicks;
                summary.Capped = true;
            }
            var goldBefore = session.Mine.Gold;
            var elixirBefore = session.Player.Elixir;
            int rocksBefore = session.Mine.RocksDestroyed;
            int eventsStart = session.Events.Count;

            session.OfflineMode = true;
            try
            {
                session.Advance(ticks);
            }
            finally
            {
                session.OfflineMode = false;
            }

            summary.Ticks = ticks;
            summary.Gold = GameNumber.Max(GameNumber.Zero, session.Mine.Gold - goldBefore);
            summary.Elixir = GameNumber.Max(GameNumber.Zero, session.Player.Elixir - elixirBefore);
            summary.RocksDestroyed = session.Mine.RocksDestroyed - rocksBefore;
            for (int i = eventsStart; i < session.Events.Count; i++)
            {
                switch (session.Events[i].Type)
                {
                    case SimEventType.ChestOpened: summary.Chests++; break;
                    case SimEventType.GateBroken:
                        summary.GatesBroken++;
                        summary.TargetsBroken++;
                        break;
                    case SimEventType.ExitBroken:
                        summary.ExitBroken = true;
                        summary.TargetsBroken++;
                        break;
                    case SimEventType.ShaftCleared:
                        summary.ShaftsCleared++;
                        summary.TargetsBroken++;
                        break;
                    case SimEventType.PayoutReady: summary.PayoutsReady++; break;
                    case SimEventType.ContainerSpawned: summary.ContainersSpawned++; break;
                }
            }
            MarkProcessed(session, nowUtcMs);
            return summary;
        }

        /// <summary>Монотонная отметка: сохраняется max(сохранённое, now).</summary>
        public static void MarkProcessed(GameSession session, long nowUtcMs)
        {
            if (nowUtcMs > session.State.LastProcessedUtcMs) session.State.LastProcessedUtcMs = nowUtcMs;
        }
    }

    public sealed partial class GameSession
    {
        /// <summary>Сброс текущей зоны к началу с сохранением PlayerProgressState (смена rulesVersion, ТЗ 12.1).</summary>
        public void RestartCurrentZone()
        {
            State.RulesVersion = Formula.RulesVersion;
            EnterZone(Mine.Zone);
        }
    }
}
