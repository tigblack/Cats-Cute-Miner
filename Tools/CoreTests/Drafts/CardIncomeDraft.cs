using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Черновик для BALANCE_V2.md (вне Assets): кампания ботом C с карточками и без. Игрок-модель после каждой шахты
    /// улучшает все карточки, на которые хватает копий и эликсира (дешёвые сначала). Печать: время шахты, сундуки, копии,
    /// эликсир, коллекция и уровни. Запуск: Tools/CoreTests/run.ps1 -Draft CardIncomeDraft.cs -Filter CardIncomeDraft.
    /// </summary>
    public class CardIncomeDraft
    {
        const int ToZone = 144;
        static readonly HashSet<int> s_Detail = new HashSet<int> { 1, 2, 3, 10, 20, 50, 100, 138, 144 };

        [Test]
        public void Campaign_CardIncome_WithAndWithoutCards()
        {
            var plain = Run(Configs, false);
            var cards = Run(Configs.WithCards(TestData.Balance), true);
            var ic = CultureInfo.InvariantCulture;
            TestContext.Progress.WriteLine("zone | sec(no cards) | sec(cards) | chests | copies | elixir+ | owned | levels | upgrades | elixirLeft | cards");
            double sumA = 0, sumB = 0;
            for (int i = 0; i < Math.Min(plain.Count, cards.Count); i++)
            {
                var a = plain[i];
                var b = cards[i];
                TestContext.Progress.WriteLine(string.Format(ic, "{0,4} | {1,8:0} | {2,8:0} ({3,6:+0.0;-0.0}%) | {4,3} | {5,4} | {6,6} | {7,2} | {8,3} | {9,3} | {10,7} | {11}",
                    a.Zone, a.Sec, b.Sec, (b.Sec / a.Sec - 1) * 100, b.Chests, b.Copies, b.Elixir, b.Owned, b.Levels, b.Upgrades, b.ElixirLeft,
                    s_Detail.Contains(a.Zone) ? b.Detail : ""));
                sumA += a.Sec;
                sumB += b.Sec;
            }
            TestContext.Progress.WriteLine(string.Format(ic, "total campaign: {0:0} s without cards, {1:0} s with cards ({2:+0.0;-0.0}%)", sumA, sumB, (sumB / sumA - 1) * 100));
            Assert.AreEqual(plain.Count, cards.Count);
        }

        sealed class Row
        {
            public int Zone;
            public double Sec;
            public int Chests, Copies, Owned, Levels, Upgrades;
            public long Elixir, ElixirLeft;
            public string Detail;
        }

        static List<Row> Run(OutpostConfigSet configs, bool upgrade)
        {
            var rows = new List<Row>();
            var repo = TestData.Balance;
            var state = StageProgressionService.NewGame(repo, configs, 1, 0, 1);
            var map = OutpostLevelAdapter.Build(repo, 1, configs.Overrides);
            int upgradesTotal = 0;
            while (true)
            {
                var sim = new OutpostSimulation(state, map, configs);
                sim.Submit(OutpostCommand.EquipBest());
                var bot = new OutpostBot(BotPolicy.C);
                var report = bot.Run(sim, "draft");
                Assert.IsTrue(report.Success, report.ToString());
                var p = state.Profile;
                int ups = 0;
                if (upgrade && configs.Cards != null)
                {
                    bool any = true;
                    while (any)
                    {
                        any = false;
                        CardDef best = null;
                        long bestCost = long.MaxValue;
                        foreach (var def in configs.Cards.Cards)
                        {
                            var q = CardService.Quote(p, def);
                            if (q.CanUpgrade && q.ElixirNeeded < bestCost)
                            {
                                best = def;
                                bestCost = q.ElixirNeeded;
                            }
                        }
                        if (best != null)
                        {
                            Assert.IsNull(CardService.Upgrade(p, best, out _));
                            ups++;
                            any = true;
                        }
                    }
                }
                upgradesTotal += ups;
                var c = state.Run.Counters;
                var owned = p.Cards.Where(kv => kv.Value.Level >= 1).OrderBy(kv => kv.Key).ToList();
                rows.Add(new Row
                {
                    Zone = state.Run.Zone,
                    Sec = report.CompletedSec ?? double.NaN,
                    Chests = c.ChestsOpened,
                    Copies = c.CardsGained,
                    Elixir = c.ElixirGained,
                    Owned = owned.Count,
                    Levels = owned.Sum(kv => kv.Value.Level),
                    Upgrades = upgradesTotal,
                    ElixirLeft = p.Elixir,
                    Detail = string.Join(" ", owned.Select(kv => kv.Key + "L" + kv.Value.Level + "+" + kv.Value.Copies)),
                });
                if (state.Run.Zone >= ToZone) break;
                var tr = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                Assert.IsTrue(tr.Ok);
                state = tr.State;
                map = tr.Map;
            }
            return rows;
        }
    }
}

namespace CatMiners.Tests.Outpost
{
    /// <summary>Вклад видов эффектов: шахта N с коллекцией из кампании — без вида / только этот вид.</summary>
    public class CardAblationDraft
    {
        [TestCase(20)]
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(144)]
        public void Ablation(int zone)
        {
            var repo = TestData.Balance;
            var configs = Configs.WithCards(repo);
            var state = StageProgressionService.NewGame(repo, configs, 1, 0, 1);
            var map = OutpostLevelAdapter.Build(repo, 1, configs.Overrides);
            while (state.Run.Zone < zone)
            {
                var sim = new OutpostSimulation(state, map, configs);
                sim.Submit(OutpostCommand.EquipBest());
                var report = new OutpostBot(BotPolicy.C).Run(sim, "draft");
                Assert.IsTrue(report.Success, report.ToString());
                UpgradeAll(state.Profile, configs.Cards);
                var tr = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                Assert.IsTrue(tr.Ok);
                state = tr.State;
                map = tr.Map;
            }
            var baseProfile = state.Profile;
            double Time(Func<CardDef, bool> keep)
            {
                var p = OutpostJson.DeepCopy(baseProfile);
                foreach (var def in configs.Cards.Cards)
                    if (!keep(def)) p.Cards.Remove(def.Id);
                var m = OutpostLevelAdapter.Build(repo, zone, configs.Overrides);
                var s = new OutpostGameState { Profile = p, Run = StageProgressionService.CreateRun(p, zone, m, configs, repo, 0) };
                var sim = new OutpostSimulation(s, m, configs);
                sim.Submit(OutpostCommand.EquipBest());
                var r = new OutpostBot(BotPolicy.C).Run(sim, "ablation");
                Assert.IsTrue(r.Success, r.ToString());
                return r.CompletedSec ?? double.NaN;
            }
            var ic = CultureInfo.InvariantCulture;
            double all = Time(_ => true), none = Time(_ => false);
            TestContext.Progress.WriteLine(string.Format(ic, "zone {0}: all {1:0} s, none {2:0} s ({3:+0.0;-0.0}%)", zone, all, none, (all / none - 1) * 100));
            foreach (CardEffectKind kind in Enum.GetValues(typeof(CardEffectKind)))
            {
                if (!configs.Cards.Cards.Any(d => d.Effect.Kind == kind && baseProfile.CardLevel(d.Id) >= 1)) continue;
                double without = Time(d => d.Effect.Kind != kind), only = Time(d => d.Effect.Kind == kind);
                TestContext.Progress.WriteLine(string.Format(ic, "  {0,-16} without {1,6:0} s ({2:+0.0;-0.0}% vs all) | only {3,6:0} s ({4:+0.0;-0.0}% vs none)",
                    kind, without, (without / all - 1) * 100, only, (only / none - 1) * 100));
            }
        }

        internal static void UpgradeAll(OutpostProfile p, CardCatalog cat)
        {
            while (true)
            {
                CardDef best = null;
                long bestCost = long.MaxValue;
                foreach (var def in cat.Cards)
                {
                    var q = CardService.Quote(p, def);
                    if (q.CanUpgrade && q.ElixirNeeded < bestCost)
                    {
                        best = def;
                        bestCost = q.ElixirNeeded;
                    }
                }
                if (best == null) return;
                Assert.IsNull(CardService.Upgrade(p, best, out _));
            }
        }
    }
}
