using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CatMiners.Core.Outpost;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Черновик (вне Assets): сила тайм-скипа магазина. Кампания эталонного игрока free (BALANCE_AUDIT.md) до шахты Z;
    /// на её старте и после P секунд игры free тайм-скип длиной D (тот же расчёт ядра, без покупок): доля расчищенных
    /// целей, доля золота шахты, завершена ли шахта, и за сколько котики без покупок дошли бы до конца.
    /// Запуск: Tools/CoreTests/run.ps1 -Draft TimeSkipStrengthDraft.cs -Filter TimeSkipStrengthDraft.
    /// </summary>
    public class TimeSkipStrengthDraft
    {
        static readonly int[] s_Zones = { 1, 2, 3, 5, 10, 20, 30, 50, 75, 100, 125, 144 };
        static readonly int[] s_SkipMinutes = { 5, 10, 15, 30, 60, 240, 720 };
        static readonly int[] s_PlayedSec = { 0, 60, 120 };

        [Test]
        public void SkipVsMine()
        {
            var repo = TestData.Balance;
            var configs = OutpostConfigSet.LoadDefault().WithCards(repo);
            var starts = new Dictionary<int, OutpostGameState>();
            var campaign = BalanceAudit.RunCampaign(repo, configs, BalanceAudit.Model("free"), 1, 144, null,
                (zone, state) => { if (Array.IndexOf(s_Zones, zone) >= 0) starts[zone] = OutpostJson.DeepCopy(state); });
            Assert.IsTrue(campaign.Success);
            var ic = CultureInfo.InvariantCulture;
            var head = new StringBuilder("zone | free s | played s | no-buy rest s |");
            foreach (int m in s_SkipMinutes) head.Append(m < 60 ? $" {m}m" : $" {m / 60}h").Append(" cleared/gold |");
            TestContext.Progress.WriteLine(head.ToString());
            foreach (int zone in s_Zones)
            {
                double freeSec = campaign.Mines[zone - 1].CompletedSec ?? double.NaN;
                foreach (int played in s_PlayedSec)
                {
                    var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
                    var state = OutpostJson.DeepCopy(starts[zone]);
                    PlayFree(state, map, configs, played);
                    var row = new StringBuilder(string.Format(ic, "{0,4} | {1,6:0} | {2,4} |", zone, freeSec, played));
                    var full = Skip(state, map, configs, 48 * 3600);
                    double restSec = full.CompletedSec;
                    row.Append(string.Format(ic, " {0,8:0} |", restSec));
                    foreach (int m in s_SkipMinutes)
                    {
                        var r = Skip(state, map, configs, m * 60);
                        row.Append(string.Format(ic, " {0,5:0.0}% {1,5:0.0}%{2} |", r.ClearedShare * 100,
                            full.Gold.IsZero ? 0 : (double)(r.Gold * 10000 / full.Gold) / 100, r.Completed ? " DONE" : "     "));
                    }
                    TestContext.Progress.WriteLine(row.ToString());
                }
            }
        }

        struct SkipResult
        {
            public double ClearedShare;
            public BigInteger Gold;
            public bool Completed;
            public double CompletedSec;
        }

        /// <summary>Тайм-скип на копии: сим без событий, без покупок — как <see cref="OfflineSimulator.BeginSkip"/>.</summary>
        static SkipResult Skip(OutpostGameState committed, OutpostMap map, OutpostConfigSet configs, long seconds)
        {
            var copy = OutpostJson.DeepCopy(committed);
            var sim = new OutpostSimulation(copy, map, configs) { RecordEvents = false };
            var run = copy.Run;
            long start = run.Now;
            BigInteger goldBefore = run.TotalCredited;
            int clearedBefore = run.Counters.ClearedTargets;
            int targets = 0, alive = 0;
            foreach (var t in run.Targets)
                if (t.Kind != 'p')
                {
                    targets++;
                    if (!t.Destroyed) alive++;
                }
            sim.Advance(checked(start + seconds * run.Balance.TicksPerSecond));
            int cleared = run.Counters.ClearedTargets - clearedBefore;
            return new SkipResult
            {
                ClearedShare = alive == 0 ? 1 : Math.Min(1.0, (double)cleared / alive),
                Gold = run.TotalCredited - goldBefore,
                Completed = run.Phase == RunPhase.ReadyToContinue,
                CompletedSec = run.Counters.CompletedTick >= 0 ? (run.Counters.CompletedTick - start) / (double)run.Balance.TicksPerSecond : double.NaN,
            };
        }

        /// <summary>Сыграть P секунд моделью free (как <see cref="BalanceAudit.RunMine"/>, но с пределом времени).</summary>
        static void PlayFree(OutpostGameState state, OutpostMap map, OutpostConfigSet configs, int seconds)
        {
            var sim = new OutpostSimulation(state, map, configs) { RecordEvents = false };
            sim.Submit(OutpostCommand.EquipBest());
            if (seconds <= 0) return;
            var model = BalanceAudit.Model("free");
            var report = new MineAudit { Zone = state.Run.Zone, Scenario = "skip-draft" };
            var probe = new BalanceProbe(sim, report, false);
            model.BeginMine();
            var run = sim.Run;
            long tps = run.Balance.TicksPerSecond;
            long end = run.Now + seconds * tps;
            long checkEvery = (long)(model.CheckIntervalSec * tps);
            long nextCheck = run.Now + (long)(model.FirstCheckSec * tps);
            while (!sim.IsEconomyStopped && run.Now < end)
            {
                long t = Math.Min(Math.Min(sim.NextEventTick(), nextCheck), end);
                probe.AdvanceTo(t);
                sim.Advance(t);
                if (!sim.IsEconomyStopped && t >= nextCheck)
                {
                    model.Buy(sim, probe);
                    nextCheck = t + checkEvery;
                }
                probe.Snapshot();
            }
        }
    }
}
