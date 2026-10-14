using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using Newtonsoft.Json;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 12 «Награды и сундуки»: порядок и сохранение счётчиков RewardCycle (10.2, E-09, E-20),
    /// resolver каждой пары (RewardType, DetailedType), нижняя граница наград выхода (11.2, E-13),
    /// таблица числа карт при множителях zone44 и контрольные значения E-11/E-12, детерминизм открытия (10.3 п. 8).
    /// Дополняет SimulationTests.RewardCycleCounterPersistsAcrossZones и FormulaControlValueTests.E09_RockCoreSecondsAndSoft.
    /// </summary>
    public class RewardChestAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;
        const string Mine2 = "rockonboardingmine2";

        static BalanceRepository B => TestData.Balance;

        static GameSession NewDebug(int zone, ulong seed = 12345UL) =>
            GameSession.NewGame(B, seed, SaveSlots.Debug, 1_000_000L, zone);

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Restore(string json) => GameSession.FromState(B, SaveService.DeserializeState(json));

        static string Json(object o) => JsonConvert.SerializeObject(o, SaveService.Settings());

        static long Counter(GameSession s, string rockId) => s.Player.RewardCycle.TryGetValue(rockId, out long c) ? c : 0L;

        static List<ZoneObject> RocksOf(GameSession s, string rockId) =>
            s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock && o.TypeId == rockId).ToList();

        /// <summary>F-R: выдаваемые золото и эликсир — max(1, floor(v)) при положительном v.</summary>
        static double Payout(double v) => v <= 0 ? 0 : Math.Max(1.0, Math.Floor(v + 1e-9 * Math.Max(1.0, Math.Abs(v))));

        /// <summary>Только обязательное по 11.2: шахты активированы, ворота открыты снизу вверх, выход Broken.</summary>
        static void ClearMandatory(GameSession s)
        {
            foreach (var sh in s.Layout.Shafts)
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal), "активация шахты " + sh.TypeId);
            }
            foreach (var g in s.Layout.Gates.OrderBy(g => g.GateOrder))
            {
                s.DebugCompleteTarget(g.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.OpenGate(g.Ordinal), "открытие ворот " + g.GateOrder);
            }
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);
        }

        /// <summary>Снимок ресурсов игрока для сравнения «до/после» одной транзакции.</summary>
        sealed class Wallet
        {
            public double Gold, Elixir, Hard;
            public int Reveals, Dynamite, Cats;
            public Dictionary<string, double> Excluded;
            public Dictionary<string, int> Copies;

            public static Wallet Of(GameSession s) => new Wallet
            {
                Gold = s.Mine.Gold.ToDouble(),
                Elixir = s.Player.Elixir.ToDouble(),
                Hard = s.Player.Hard.ToDouble(),
                Reveals = s.Player.ChestReveals.Count,
                Dynamite = s.Player.Dynamite,
                Cats = s.Mine.Cats.Count,
                Excluded = new Dictionary<string, double>(s.Player.ExcludedRewards),
                Copies = s.Player.Cards.ToDictionary(kv => kv.Key, kv => (kv.Value.Level > 0 ? 1 : 0) + kv.Value.Duplicates),
            };

            public int CopiesOf(string id) => Copies.TryGetValue(id, out int c) ? c : 0;
            public int TotalCopies => Copies.Values.Sum();
            public double ExcludedOf(string key) => Excluded.TryGetValue(key, out double v) ? v : 0.0;
            public string ExcludedKey => string.Join(";", Excluded.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        static string Pair(int rewardType, string detailedType) => rewardType.ToString(CultureInfo.InvariantCulture) + ":" + detailedType;

        /// <summary>
        /// Разбить камень и проверить, что начислена ровно награда записи цикла e по resolver ТЗ 10.2.
        /// Неизвестная пара (RewardType, DetailedType) — провал теста.
        /// </summary>
        static void BreakRockAndCheck(GameSession s, ZoneObject obj, RewardModelDto e, string tag)
        {
            var rock = B.Rocks[obj.TypeId];
            double rate = s.GetRewardIncomeRate().ToDouble();
            Assert.GreaterOrEqual(rate, B.Props.NoGeneratorsCurrencyRate, tag + ": E-10, ставка не ниже NoGeneratorsCurrencyRate");
            ChestResult expectedChest = null;
            if (e.RewardType == RewardTypes.Gacha)
            {
                Assert.IsTrue(B.Gacha.TryGetValue(e.DetailedType, out var g), tag + ": сундук " + e.DetailedType + " есть в Gacha");
                // Тот же алгоритм 10.3 на копии сохраняемого ГСЧ: камень обязан открыть ровно этот сундук.
                expectedChest = GachaService.Open(B, g, s.BuildGachaContext(), s.Player.Rng.Gacha.Clone());
            }
            var before = Wallet.Of(s);
            long counterBefore = Counter(s, rock.Id);
            s.Events.Clear();
            s.DebugCompleteTarget(obj.Ordinal);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[obj.Ordinal].Status, tag + ": камень разрушен");
            Assert.AreEqual(counterBefore + 1, Counter(s, rock.Id), tag + ": счётчик Rocks.Id +1 в транзакции завершения");
            var after = Wallet.Of(s);
            bool excludedEvent = s.Events.Any(ev => ev.Type == SimEventType.ExcludedReward);

            if (e.RewardType == RewardTypes.Gacha)
            {
                Assert.AreEqual(before.Reveals + 1, after.Reveals, tag + ": ровно один сундук указанной записи");
                var reveal = s.Player.ChestReveals.Last();
                Assert.AreEqual(e.DetailedType, reveal.GachaId, tag);
                Assert.AreEqual(expectedChest.Elixir.ToDouble(), reveal.Elixir.ToDouble(), 1e-9, tag + ": эликсир сундука по 10.3");
                CollectionAssert.AreEqual(expectedChest.Cards.Select(c => c.CardId + "x" + c.Count).ToArray(),
                    reveal.Cards.Select(c => c.CardId + "x" + c.Count).ToArray(), tag + ": карточки сундука по 10.3");
                Assert.AreEqual(before.Elixir + reveal.Elixir.ToDouble(), after.Elixir, 1e-9, tag + ": эликсир сундука начислен");
                foreach (var c in reveal.Cards)
                    Assert.AreEqual(before.CopiesOf(c.CardId) + c.Count, after.CopiesOf(c.CardId), tag + ": копии " + c.CardId);
                Assert.AreEqual(before.TotalCopies + reveal.Cards.Sum(c => c.Count), after.TotalCopies, tag + ": других карточек нет");
                Assert.AreEqual(before.Gold, after.Gold, tag + ": сундук не даёт золота");
                Assert.AreEqual(before.Hard, after.Hard, tag + ": сундук не даёт твёрдой валюты");
                Assert.AreEqual(before.ExcludedKey, after.ExcludedKey, tag);
                Assert.IsFalse(excludedEvent, tag);
                return;
            }

            Assert.AreEqual(RewardTypes.Currency, e.RewardType, tag + ": неизвестный RewardType " + e.RewardType);
            Assert.AreEqual(before.Reveals, after.Reveals, tag + ": валютная награда не открывает сундук");
            Assert.AreEqual(before.TotalCopies, after.TotalCopies, tag + ": валютная награда не даёт карточек");
            switch (e.DetailedType)
            {
                case "Core":
                {
                    // E-09: max(1, floor(F-3(CoreCurrencySeconds*; x = уровень камня) × GetRewardIncomeRate)).
                    double seconds = Formula.Ft3(rock.CoreCurrencySecondsBase, rock.CoreCurrencySecondsMultiplier, rock.CoreCurrencySecondsGrowth,
                        rock.CoreCurrencySecondsPower, rock.CoreCurrencySecondsRound, obj.Level);
                    Assert.Greater(seconds, 0, tag + ": секунды дохода положительны");
                    Assert.AreEqual(Payout(seconds * rate), after.Gold - before.Gold, 1e-9, tag + ": золото Core");
                    Assert.GreaterOrEqual(after.Gold - before.Gold, 1.0, tag + ": Quantity = 0 не означает «ничего»");
                    Assert.AreEqual(before.Elixir, after.Elixir, tag);
                    Assert.AreEqual(before.Hard, after.Hard, tag);
                    break;
                }
                case "Soft":
                {
                    // E-09: max(1, floor(F-0(SoftCurrency*; x = уровень камня) × MiningSoftCurrencyMultiplier)).
                    double x = obj.Level;
                    double raw = rock.SoftCurrencyBase + rock.SoftCurrencyMultiplier * x + rock.SoftCurrencyGrowth * x * x;
                    double mult = B.ZoneMultipliers(s.Mine.Zone).MiningSoftCurrencyMultiplier;
                    Assert.AreEqual(Payout(raw * mult), after.Elixir - before.Elixir, 1e-9, tag + ": эликсир Soft");
                    Assert.GreaterOrEqual(after.Elixir - before.Elixir, 1.0, tag + ": Quantity = 0 не означает «ничего»");
                    Assert.AreEqual(before.Gold, after.Gold, tag);
                    Assert.AreEqual(before.Hard, after.Hard, tag);
                    break;
                }
                case "Hard":
                    Assert.AreEqual(e.Quantity, after.Hard - before.Hard, 1e-9, tag + ": Hard = Quantity");
                    Assert.AreEqual(before.Gold, after.Gold, tag);
                    Assert.AreEqual(before.Elixir, after.Elixir, tag);
                    break;
                case "BattlePass":
                    // 10.2: не выдаётся, в журнал пишется событие ExcludedReward(BattlePass).
                    Assert.AreEqual(before.Gold, after.Gold, tag + ": BattlePass не выдаётся");
                    Assert.AreEqual(before.Elixir, after.Elixir, tag + ": BattlePass не выдаётся");
                    Assert.AreEqual(before.Hard, after.Hard, tag + ": BattlePass не выдаётся");
                    Assert.AreEqual(before.ExcludedOf("BattlePass") + e.Quantity, after.ExcludedOf("BattlePass"), 1e-9, tag + ": учёт в журнале исключённых наград");
                    Assert.IsTrue(s.Events.Any(ev => ev.Type == SimEventType.ExcludedReward && ev.Text == "BattlePass"), tag + ": событие ExcludedReward(BattlePass)");
                    return;
                default:
                    Assert.Fail(tag + ": неизвестная пара " + Pair(e.RewardType, e.DetailedType));
                    return;
            }
            Assert.AreEqual(before.ExcludedKey, after.ExcludedKey, tag + ": выдаваемая награда не попадает в исключённые");
            Assert.IsFalse(excludedEvent, tag);
        }

        // ───────────────────────────── 10.2: порядок и сохранение счётчиков RewardCycle ─────────────────────────────

        /// <summary>
        /// ТЗ 17.1 п. 12: «rockonboardingmine2: цикл 8 при 11 камнях». В zone2 11 камней, цикл из 8 записей
        /// [Core, Core, Core, Soft, Core, Core, Core, Core]: индекс = счётчик mod 8, поэтому 9–11-й камни снова идут с начала,
        /// а Soft выпадает 4-му разрушенному камню, какой бы он ни был. E-20: цикл собственный, не rockbasic по MimicId.
        /// </summary>
        [Test]
        public void RewardCycle_OnboardingMine2_EightEntriesWrapOverElevenRocksInDestroyOrder()
        {
            var rock = B.Rocks[Mine2];
            Assert.AreEqual("rockbasic", rock.MimicId);
            CollectionAssert.AreEqual(new[] { "Core", "Core", "Core", "Soft", "Core", "Core", "Core", "Core" },
                rock.RewardCycle.Select(e => e.DetailedType).ToArray(), "E-20: собственный цикл из 8 записей");
            Assert.IsTrue(rock.RewardCycle.All(e => e.RewardType == RewardTypes.Currency));
            // У rockbasic запись [3] — GachaWooden: подмена цикла по MimicId дала бы сундук 4-му камню.
            Assert.AreEqual(RewardTypes.Gacha, B.Rocks["rockbasic"].RewardCycle[3].RewardType);

            var fourth = new List<int>();
            foreach (bool reverse in new[] { false, true })
            {
                string order = reverse ? "обратный порядок" : "прямой порядок";
                var s = NewDebug(2);
                var rocks = RocksOf(s, Mine2);
                Assert.AreEqual(11, rocks.Count, "zone2: 11 камней rockonboardingmine2");
                if (reverse) rocks.Reverse();
                Assert.AreEqual(1.5, s.GetRewardIncomeRate().ToDouble(), 1e-12, "E-10: только кузница L1");

                for (int k = 0; k < rocks.Count; k++)
                {
                    string tag = order + ", камень " + (k + 1);
                    var obj = rocks[k];
                    Assert.That(obj.Level, Is.InRange(1, 2), tag);
                    var before = Wallet.Of(s);
                    s.DebugCompleteTarget(obj.Ordinal);
                    var after = Wallet.Of(s);
                    Assert.AreEqual(k + 1, Counter(s, Mine2), tag + ": счётчик");
                    Assert.AreEqual(before.Reveals, after.Reveals, tag + ": сундуков в цикле нет");
                    if (k % 8 == 3)
                    {
                        // Soft: max(1, floor((4 + 0.3·L) × 0.15)) = 1 для L1 и L2.
                        Assert.AreEqual(1.0, after.Elixir - before.Elixir, 1e-9, tag + ": запись [3] = Soft");
                        Assert.AreEqual(before.Gold, after.Gold, tag + ": Soft не даёт золота");
                        fourth.Add(obj.Ordinal);
                    }
                    else
                    {
                        // Core: 105 с (L1) / 225 с (L2) × 1.5 → 157 / 337 (E-09).
                        double gold = obj.Level == 1 ? 157 : 337;
                        Assert.AreEqual(gold, after.Gold - before.Gold, 1e-9, tag + ": запись [" + (k % 8) + "] = Core");
                        Assert.AreEqual(before.Elixir, after.Elixir, tag + ": Core не даёт эликсира");
                    }
                }
                Assert.AreEqual(11L, Counter(s, Mine2), order + ": следующий индекс 11 mod 8 = 3");
                Assert.IsFalse(s.Player.RewardCycle.ContainsKey("rockbasic"), order + ": счётчик ведётся по Rocks.Id, а не по MimicId");
            }
            Assert.AreNotEqual(fourth[0], fourth[1], "Soft определяется порядком разрушения, а не положением камня");
        }

        /// <summary>
        /// 10.2, 11.3: счётчики RewardCycle лежат в PlayerProgressState, не сбрасываются при CompleteZone и save/load,
        /// и цикл продолжается с сохранённого индекса: 12-й камень rockonboardingmine2 при новом входе в zone2 — запись [3] (Soft).
        /// </summary>
        [Test]
        public void RewardCycle_CountersSurviveCompleteZoneAndSaveLoadAndContinueTheCycle()
        {
            var s = NewDebug(2);
            foreach (var r in RocksOf(s, Mine2)) s.DebugCompleteTarget(r.Ordinal);
            var smallRocks = RocksOf(s, "rocksoftcurrencysmall");
            Assert.AreEqual(2, smallRocks.Count, "zone2: два rocksoftcurrencysmall");
            foreach (var r in smallRocks) s.DebugCompleteTarget(r.Ordinal);
            Assert.AreEqual(11L, Counter(s, Mine2));
            Assert.AreEqual(2L, Counter(s, "rocksoftcurrencysmall"), "счётчики разных Rocks.Id независимы");

            ClearMandatory(s);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(3, s.Mine.Zone);
            Assert.AreEqual(11L, Counter(s, Mine2), "CompleteZone не сбрасывает счётчик");
            Assert.AreEqual(2L, Counter(s, "rocksoftcurrencysmall"), "CompleteZone не сбрасывает счётчик");

            var r3 = Restore(Snapshot(s));
            Assert.AreEqual(11L, Counter(r3, Mine2), "save/load сохраняет счётчик");
            Assert.AreEqual(2L, Counter(r3, "rocksoftcurrencysmall"), "save/load сохраняет счётчик");

            // В zone3 тот же Rocks.Id продолжает счёт с 2, а не с 0.
            var small3 = RocksOf(r3, "rocksoftcurrencysmall").First();
            BreakRockAndCheck(r3, small3, B.Rocks["rocksoftcurrencysmall"].RewardCycle[0], "zone3 rocksoftcurrencysmall");
            Assert.AreEqual(3L, Counter(r3, "rocksoftcurrencysmall"));

            // Новый вход в zone2 («как при обычном входе», 17): MineProgressState новый, счётчик прежний.
            r3.DebugGotoZone(2);
            var again = RocksOf(r3, Mine2);
            Assert.AreEqual(11, again.Count);
            Assert.IsTrue(again.All(o => r3.Mine.Objects[o.Ordinal].Status == ObjectStatus.Intact), "камни новой зоны целы");
            var cycle = B.Rocks[Mine2].RewardCycle;
            for (int k = 0; k < 2; k++)
            {
                long counter = Counter(r3, Mine2);
                var entry = cycle[(int)(counter % cycle.Length)];
                Assert.AreEqual(k == 0 ? "Soft" : "Core", entry.DetailedType, "12-й камень — запись [3], 13-й — [4]");
                BreakRockAndCheck(r3, again[k], entry, "zone2 повторно, камень " + (12 + k));
            }
            Assert.AreEqual(13L, Counter(r3, Mine2));
        }

        /// <summary>
        /// 10.2 [Д]: rockbasic — цикл из 500 записей, сквозной между зонами. Первые 12 записей
        /// [Core, Core, Core, GachaWooden, Core, Core, Soft, Core, Core, GachaWooden, Core, Hard 1] выдаются в исходном порядке:
        /// 5 камней в zone6, затем обычный CompleteZone, затем 7 камней в zone7.
        /// </summary>
        [Test]
        public void RewardCycle_RockbasicSourceOrderContinuesFromZone6IntoZone7()
        {
            var cycle = B.Rocks["rockbasic"].RewardCycle;
            Assert.AreEqual(500, cycle.Length);
            CollectionAssert.AreEqual(
                new[] { "0:Core", "0:Core", "0:Core", "1:GachaWooden", "0:Core", "0:Core", "0:Soft", "0:Core", "0:Core", "1:GachaWooden", "0:Core", "0:Hard" },
                cycle.Take(12).Select(e => Pair(e.RewardType, e.DetailedType)).ToArray());

            var s = NewDebug(6);
            var zone6 = RocksOf(s, "rockbasic");
            for (int k = 0; k < 5; k++) BreakRockAndCheck(s, zone6[k], cycle[k], "zone6 rockbasic #" + k);
            Assert.AreEqual(5L, Counter(s, "rockbasic"));

            ClearMandatory(s);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(7, s.Mine.Zone);
            Assert.AreEqual(5L, Counter(s, "rockbasic"), "переход зоны не сбрасывает счётчик");

            var zone7 = RocksOf(s, "rockbasic");
            for (int k = 5; k < 12; k++) BreakRockAndCheck(s, zone7[k - 5], cycle[k], "zone7 rockbasic #" + k);
            Assert.AreEqual(12L, Counter(s, "rockbasic"));
        }

        // ───────────────────────────── 10.2: resolver (RewardType, DetailedType) ─────────────────────────────

        /// <summary>
        /// Все пары (RewardType, DetailedType) разделов ядра (Rocks[].RewardCycle, Deliveries[].RewardModel) относятся к известному
        /// правилу resolver 10.2. Неизвестная пара — провал. Остальные списки наград JSON (магазин, задания, колесо и т. п.) — исключённые
        /// разделы 3.1, в runtime-модели их нет.
        /// </summary>
        [Test]
        public void Resolver_EveryRewardPairInCoreSectionsIsKnown()
        {
            var rockPairs = new HashSet<string>();
            foreach (var rock in B.Root.Rocks)
            {
                for (int i = 0; i < rock.RewardCycle.Length; i++)
                {
                    var e = rock.RewardCycle[i];
                    string where = "Rocks." + rock.Id + ".RewardCycle[" + i + "] " + Pair(e.RewardType, e.DetailedType);
                    rockPairs.Add(Pair(e.RewardType, e.DetailedType));
                    if (e.RewardType == RewardTypes.Gacha)
                    {
                        Assert.IsTrue(B.Gacha.ContainsKey(e.DetailedType), where + ": сундук есть в Gacha");
                        Assert.AreEqual(0.0, e.Quantity, where + ": выдача сундука записана Quantity = 0");
                        continue;
                    }
                    Assert.AreEqual(RewardTypes.Currency, e.RewardType, where + ": неизвестный RewardType");
                    switch (e.DetailedType)
                    {
                        case "Core":
                            Assert.AreEqual(0.0, e.Quantity, where + ": вычисляемая награда, Quantity = 0");
                            Assert.AreEqual(3, rock.CoreCurrencySecondsFormulaType, where + ": E-09, секунды по F-3");
                            break;
                        case "Soft":
                            Assert.AreEqual(0.0, e.Quantity, where + ": вычисляемая награда, Quantity = 0");
                            Assert.AreEqual(0, rock.SoftCurrencyFormulaType, where + ": E-09, эликсир по F-0");
                            break;
                        case "Hard":
                            Assert.Greater(e.Quantity, 0.0, where + ": Hard = Quantity");
                            break;
                        case "BattlePass":
                            Assert.Greater(e.Quantity, 0.0, where + ": учитывается в журнале исключённых наград");
                            break;
                        default:
                            Assert.Fail(where + ": неизвестная пара");
                            break;
                    }
                }
            }
            // 4 валютные пары и 20 сундуков (8 обычных + 12 скриптовых scg_*).
            Assert.AreEqual(24, rockPairs.Count, string.Join(", ", rockPairs.OrderBy(p => p, StringComparer.Ordinal)));
            Assert.AreEqual(20, rockPairs.Count(p => p.StartsWith("1:", StringComparison.Ordinal)));

            var deliveryPairs = new HashSet<string>();
            foreach (var d in B.Root.Deliveries)
            {
                var rm = d.RewardModel;
                string where = "Deliveries[" + d.Id + "] " + Pair(rm.RewardType, rm.DetailedType);
                deliveryPairs.Add(Pair(rm.RewardType, rm.DetailedType));
                Assert.AreEqual(0.0, rm.Quantity, where + ": RewardModel.Quantity везде 0 (10.5)");
                Assert.AreEqual(3, d.QuantityFormulaType, where + ": содержимое по F-3");
                switch (rm.RewardType)
                {
                    case RewardTypes.Currency:
                        Assert.IsTrue(rm.DetailedType == "Core" || rm.DetailedType == "Soft", where + ": доставка даёт только Core/Soft");
                        break;
                    case RewardTypes.Miner:
                        Assert.AreEqual(BalanceRepository.MinerId, rm.DetailedType, where);
                        Assert.AreEqual(rm.DetailedType, B.Miner.Id, where + ": тип котика есть в Miners");
                        break;
                    case RewardTypes.Spell:
                        Assert.AreEqual("Dynamite", rm.DetailedType, where);
                        Assert.IsNotNull(B.Spell, where + ": динамит — Spells[0]");
                        break;
                    default:
                        Assert.Fail(where + ": неизвестная пара");
                        break;
                }
            }
            CollectionAssert.AreEquivalent(new[] { "0:Core", "0:Soft", "4:tinygoblin", "9:Dynamite" }, deliveryPairs, "10.2: пары доставок");
        }

        /// <summary>
        /// Resolver камня на деле: для каждого Rocks.Id и каждой его различной записи цикла (RewardType, DetailedType, Quantity)
        /// камень этого типа в первой зоне, где он встречается, начисляет ровно свой ресурс: Core — золото, Soft — эликсир,
        /// Hard — Quantity твёрдой валюты, Gacha — один сундук этой записи, BattlePass — только журнал исключённых наград.
        /// Индекс записи выставляется через счётчик PlayerProgressState.RewardCycle (публичное сохраняемое состояние).
        /// </summary>
        [Test]
        public void Resolver_EveryRockCycleEntryCreditsItsResource()
        {
            var checkedPairs = new HashSet<string>();
            int cases = 0;
            foreach (var rock in B.Root.Rocks)
            {
                int zone = -1;
                for (int n = 1; n <= B.ZoneCount && zone < 0; n++)
                    if (B.Zone(n).Grid.Split(',').Any(t => t.Trim().StartsWith("r:" + rock.Id + ":", StringComparison.Ordinal))) zone = n;
                Assert.Greater(zone, 0, rock.Id + ": камень встречается в зонах");

                var seen = new HashSet<string>();
                for (int i = 0; i < rock.RewardCycle.Length; i++)
                {
                    var e = rock.RewardCycle[i];
                    string key = Pair(e.RewardType, e.DetailedType) + "x" + e.Quantity.ToString("R", CultureInfo.InvariantCulture);
                    if (!seen.Add(key)) continue;
                    var s = NewDebug(zone);
                    var obj = RocksOf(s, rock.Id).First();
                    s.Player.RewardCycle[rock.Id] = i;
                    BreakRockAndCheck(s, obj, e, "zone" + zone + " " + rock.Id + "[" + i + "] " + key);
                    checkedPairs.Add(Pair(e.RewardType, e.DetailedType));
                    cases++;
                }
            }
            Assert.AreEqual(24, checkedPairs.Count, "проверены все 24 пары камней");
            TestContext.WriteLine("Проверено записей цикла: " + cases);
        }

        /// <summary>
        /// Resolver доставки (10.2, 10.5, 10.6): (0, Core) — золото max(1, floor(F-3(x = Rank) × GetRewardIncomeRate)),
        /// (0, Soft) — эликсир max(1, floor(F-3(x = Rank))), (4, tinygoblin) — котик в клетке ящика, (9, Dynamite) — 1 заряд.
        /// zone7 (Rank 7): все четыре вида есть в пуле без ворот.
        /// </summary>
        [Test]
        public void Resolver_EachDeliveryKindCreditsItsResource()
        {
            var s = NewDebug(7);
            foreach (var sh in s.Layout.Shafts)
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.IsTrue(s.Mine.DeliveriesEnabled, "Rank 7 ≥ 4 и активировано ≥ 2 шахт");
            var seen = new HashSet<string>();
            for (int i = 0; i < 20000 && seen.Count < 4; i++)
            {
                s.Advance(5 * S);
                s.Events.Clear();
                var box = s.Mine.Containers.FirstOrDefault(c => c.Kind == ContainerKind.Delivery);
                if (box == null) continue;
                var d = B.Deliveries[box.DeliveryId];
                string pair = Pair(box.RewardType, box.DetailedType);
                string tag = "доставка id " + d.Id + " " + pair;
                Assert.AreEqual(Pair(d.RewardModel.RewardType, d.RewardModel.DetailedType), pair, tag + ": содержимое из своей записи");
                Assert.IsFalse(d.IsAd, tag + ": рекламная запись не выдаётся");
                double q = Formula.Ft3(d.QuantityBase, d.QuantityMultiplier, d.QuantityGrowth, d.QuantityPower, d.QuantityRound, s.Rank);
                double rate = s.GetRewardIncomeRate().ToDouble();
                int cell = box.Cell, catLevel = box.CatLevel;
                double amount = box.Amount.ToDouble();
                int lNext = s.HireLevel();
                var before = Wallet.Of(s);
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id), tag);
                var after = Wallet.Of(s);
                switch (pair)
                {
                    case "0:Core":
                        Assert.AreEqual(Payout(q * rate), amount, 1e-9, tag + ": F-3(x = Rank) секунд дохода");
                        Assert.AreEqual(amount, after.Gold - before.Gold, 1e-9, tag + ": начислено золото");
                        Assert.AreEqual(before.Elixir, after.Elixir, tag);
                        Assert.AreEqual(before.Cats, after.Cats, tag);
                        Assert.AreEqual(before.Dynamite, after.Dynamite, tag);
                        break;
                    case "0:Soft":
                        Assert.AreEqual(Payout(q), amount, 1e-9, tag + ": F-3(x = Rank) эликсира");
                        Assert.AreEqual(amount, after.Elixir - before.Elixir, 1e-9, tag + ": начислен эликсир");
                        Assert.AreEqual(before.Gold, after.Gold, tag);
                        Assert.AreEqual(before.Cats, after.Cats, tag);
                        Assert.AreEqual(before.Dynamite, after.Dynamite, tag);
                        break;
                    case "4:tinygoblin":
                        // Q = −2, L_next = 1, догоняние невозможно (Lmin ≤ −2 не бывает): уровень max(1, 1 − 2) = 1.
                        Assert.AreEqual(-2.0, q, tag);
                        Assert.AreEqual(1, lNext, tag);
                        Assert.AreEqual(1, catLevel, tag + ": max(1, L_next + Q)");
                        Assert.AreEqual(before.Cats + 1, after.Cats, tag + ": котик появился");
                        Assert.IsNotNull(s.CatAt(cell), tag + ": котик в клетке ящика");
                        Assert.AreEqual(catLevel, s.CatAt(cell).Level, tag);
                        Assert.AreEqual(before.Gold, after.Gold, tag);
                        Assert.AreEqual(before.Elixir, after.Elixir, tag);
                        break;
                    case "9:Dynamite":
                        Assert.Less(before.Dynamite, GameSession.MaxDynamite, tag);
                        Assert.AreEqual(before.Dynamite + 1, after.Dynamite, tag + ": 1 заряд");
                        Assert.AreEqual(before.Gold, after.Gold, tag);
                        Assert.AreEqual(before.Elixir, after.Elixir, tag);
                        Assert.AreEqual(before.Cats, after.Cats, tag);
                        break;
                    default:
                        Assert.Fail(tag + ": неизвестная пара доставки");
                        break;
                }
                Assert.AreEqual(before.Hard, after.Hard, tag + ": доставки не дают твёрдой валюты");
                Assert.AreEqual(before.Reveals, after.Reveals, tag + ": доставки сундуков не содержат (10.3)");
                Assert.AreEqual(before.ExcludedKey, after.ExcludedKey, tag);
                seen.Add(pair);
            }
            CollectionAssert.AreEquivalent(new[] { "0:Core", "0:Soft", "4:tinygoblin", "9:Dynamite" }, seen, "встречены все виды доставок");
        }

        // ───────────────────────────── 11.2, E-13: награда выхода ─────────────────────────────

        /// <summary>
        /// E-13: у 10 записей scg_m001…m010_exit (и только у них) диапазон GuaranteedCardMin…Counts и CardMultMax.
        /// Без Crusher они всегда дают нижнюю границу: GuaranteedCardMin и SoftCurrencyMin, без CardMultMax и случайных карт,
        /// при любом seed и в контексте любой зоны (GachaType 2 — без множителей зоны). С zone11 выход — GachaCrusher.
        /// </summary>
        [Test]
        public void ExitReward_RangeRecordsAlwaysOpenAtLowerBound()
        {
            var exitIds = Enumerable.Range(1, 10).Select(n => "scg_m" + n.ToString("D3", CultureInfo.InvariantCulture) + "_exit").ToArray();
            CollectionAssert.AreEquivalent(exitIds,
                B.Root.Gacha.Where(g => g.GuaranteedCardMin != null && g.GuaranteedCardMin.Length > 0).Select(g => g.Id).ToArray(),
                "10.3 п. 1: GuaranteedCardMin непуст только у 10 наград выхода");
            CollectionAssert.AreEquivalent(exitIds, B.Root.Gacha.Where(g => g.CardMultMax > 0).Select(g => g.Id).ToArray(),
                "11.2: CardMultMax есть только у них");
            for (int n = 1; n <= B.ZoneCount; n++)
                Assert.AreEqual(n <= 10 ? exitIds[n - 1] : "GachaCrusher", ZoneLayout.Build(B, n).Exit.RewardId, "zone" + n + ": награда токена e");

            // Контрольные значения Приложения A и E-13.
            var control = new Dictionary<string, (string cards, double elixir)>
            {
                { "scg_m001_exit", ("ca001x1", 17) },
                { "scg_m002_exit", ("ca001x1 ca002x1", 30) },
                { "scg_m003_exit", ("ca002x3 ca014x1", 50) },
            };

            // Карточки заданы диапазоном хотя бы у части записей (у zone1 Min = Counts = 1); нижняя граница строго меньше верхней.
            Assert.Greater(exitIds.Sum(id => B.Gacha[id].GuaranteedCardCounts.Sum() - B.Gacha[id].GuaranteedCardMin.Sum()), 0);

            var contexts = new[] { 1, 10, 44, 144 }.Select(z => NewDebug(z).BuildGachaContext()).ToArray();
            var seeds = new[] { 1UL, 2UL, 777UL, 12345UL, ulong.MaxValue };
            foreach (var id in exitIds)
            {
                var g = B.Gacha[id];
                Assert.AreEqual(2, g.GachaType, id + ": скриптовая награда без множителей зоны");
                Assert.AreEqual(0, g.BaseNumCards, id + ": случайных карт нет");
                Assert.Less(g.SoftCurrencyMin, g.SoftCurrencyMax, id + ": эликсир задан диапазоном");
                Assert.AreEqual(g.GuaranteedCardIds.Length, g.GuaranteedCardMin.Length, id);
                Assert.IsTrue(Enumerable.Range(0, g.GuaranteedCardIds.Length).All(i => g.GuaranteedCardMin[i] <= g.GuaranteedCardCounts[i]), id + ": Min ≤ Counts");
                var lower = string.Join(" ", Enumerable.Range(0, g.GuaranteedCardIds.Length).Select(i => g.GuaranteedCardIds[i] + "x" + g.GuaranteedCardMin[i]));
                if (control.TryGetValue(id, out var c))
                {
                    Assert.AreEqual(c.cards, lower, id + ": контрольные карточки");
                    Assert.AreEqual(c.elixir, g.SoftCurrencyMin, id + ": контрольный эликсир");
                }
                foreach (var ctx in contexts)
                {
                    foreach (var seed in seeds)
                    {
                        string tag = id + " rank " + ctx.Rank + " seed " + seed;
                        var r = GachaService.Open(B, g, ctx, new Pcg32(seed, 1));
                        Assert.AreEqual(id, r.GachaId, tag);
                        Assert.AreEqual(g.SoftCurrencyMin, r.Elixir.ToDouble(), tag + ": эликсир = SoftCurrencyMin");
                        Assert.AreEqual(lower, string.Join(" ", r.Cards.Select(x => x.CardId + "x" + x.Count)), tag + ": карточки = GuaranteedCardMin");
                    }
                }
            }
        }

        /// <summary>
        /// E-13: с zone11 выход — GachaCrusher, обычный сундук 10.3 (GachaType 0) без CrushableRewards.
        /// zone14: N = floor(35 × 1.5 + 0.5) = 53 (E-11: 52.5 → 53), эликсир floor(250 × 1.2) = 300.
        /// </summary>
        [Test]
        public void ExitReward_FromZone11IsOrdinaryGachaCrusher()
        {
            var g = B.Gacha["GachaCrusher"];
            Assert.AreEqual(0, g.GachaType);
            var s = NewDebug(14);
            Assert.AreEqual("GachaCrusher", s.Layout.Exit.RewardId);
            var ctx = s.BuildGachaContext();
            ClearMandatory(s);
            int revealsBefore = s.Player.ChestReveals.Count;
            var elixirBefore = s.Player.Elixir.ToDouble();
            int copiesBefore = Wallet.Of(s).TotalCopies;

            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(15, s.Mine.Zone);
            Assert.AreEqual(revealsBefore + 1, s.Player.ChestReveals.Count, "один сундук выхода");
            var reveal = s.Player.ChestReveals.Last();
            Assert.AreEqual("GachaCrusher", reveal.GachaId);
            Assert.AreEqual(53, reveal.Cards.Sum(c => c.Count), "E-11: zone14 Crusher 52.5 → 53, без бонусов CrushableRewards");
            Assert.AreEqual(300.0, reveal.Elixir.ToDouble(), "250 × GachaSoftCurrencyMultNormal zone14 (1.2)");
            Assert.AreEqual(300.0, s.Player.Elixir.ToDouble() - elixirBefore, 1e-9, "эликсир начислен");
            Assert.AreEqual(copiesBefore + 53, Wallet.Of(s).TotalCopies, "карточки начислены");
            AssertRandomChest(reveal, g, ctx, "zone14 GachaCrusher");
        }

        // ───────────────────────────── 10.3, E-11, E-12: число карт и состав сундука ─────────────────────────────

        static double CardsMult(GachaDto g, RankMultipliersDto m)
        {
            switch (g.GachaType)
            {
                case 0: return m.GachaCardsMultNormal;
                case 1: return m.GachaCardsMultPremium;
                case 3: return m.GachaCardsMultRare;
                default: return 1.0;   // 2 — без множителей
            }
        }

        static double SoftMult(GachaDto g, RankMultipliersDto m)
        {
            switch (g.GachaType)
            {
                case 0: return m.GachaSoftCurrencyMultNormal;
                case 1: return m.GachaSoftCurrencyMultPremium;
                case 3: return m.GachaSoftCurrencyMultRare;
                default: return 1.0;
            }
        }

        /// <summary>10.3 п. 4: N = floor(BaseNumCards × GachaCardsMult + 0.5 + 1e-9), не меньше 1 при BaseNumCards &gt; 0.</summary>
        static int ExpectedN(GachaDto g, RankMultipliersDto m) =>
            g.BaseNumCards <= 0 ? 0 : Math.Max(1, (int)Math.Floor(g.BaseNumCards * CardsMult(g, m) + 0.5 + 1e-9));

        static double WeightOf(GachaDto g, int rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return g.UncommonWeight;
                case Rarity.Rare: return g.RareWeight;
                case Rarity.Epic: return g.EventEpicWeight;
                case Rarity.Legendary: return g.LegendaryWeight;
                default: return 0;
            }
        }

        /// <summary>10.3 п. 7: число разных ID = CardStackCounts[j] для наибольшего пройденного порога (N_R ≥ порога), иначе 1.</summary>
        static int StacksByRule(int rarity, int count)
        {
            var p = B.Props;
            int[] splits;
            switch (rarity)
            {
                case Rarity.Common: splits = p.CommonStackSplitAt; break;
                case Rarity.Uncommon: splits = p.UncommonStackSplitAt; break;
                case Rarity.Rare: splits = p.RareStackSplitAt; break;
                case Rarity.Epic: splits = p.EventEpicStackSplitAt; break;
                default: return 1;   // у Legendary порогов нет
            }
            int stacks = 1;
            for (int j = 0; j < splits.Length; j++)
                if (count >= splits[j]) stacks = p.CardStackCounts[j];
            return stacks;
        }

        static readonly int[] RandomRarities = { Rarity.Legendary, Rarity.Epic, Rarity.Rare, Rarity.Uncommon };

        /// <summary>
        /// Проверка результата сундука без гарантированных карт по 10.3 п. 2–7: всего N карт; редкость R — floor(N / W_R) или на 1 больше
        /// (только при дробной части); вес 0 — редкости нет; Common — остаток; кандидаты MineUnlock ≤ Rank, кроме rarity 6;
        /// стопки по *StackSplitAt, не больше числа кандидатов, поровну с остатком первой; эликсир — floor(k × mult) для целого k из [Min; Max].
        /// Возвращает число карт по редкостям.
        /// </summary>
        static int[] AssertRandomChest(ChestResult r, GachaDto g, GachaContext ctx, string tag)
        {
            Assert.IsTrue(g.GuaranteedCardIds == null || g.GuaranteedCardIds.Length == 0, tag + ": только для сундуков без гарантированных карт");
            int n = ExpectedN(g, ctx.Multipliers);
            var counts = new int[7];
            var amounts = new Dictionary<int, List<int>>();
            foreach (var c in r.Cards)
            {
                Assert.IsTrue(B.Cards.TryGetValue(c.CardId, out var card), tag + ": неизвестная карта " + c.CardId);
                Assert.AreNotEqual(Rarity.Majestic, card.Rarity, tag + ": rarity 6 не выпадает");
                Assert.LessOrEqual(card.MineUnlock, ctx.Rank, tag + ": " + c.CardId + " MineUnlock ≤ Rank");
                Assert.Greater(c.Count, 0, tag);
                counts[card.Rarity] += c.Count;
                if (!amounts.TryGetValue(card.Rarity, out var list)) amounts[card.Rarity] = list = new List<int>();
                list.Add(c.Count);
            }
            Assert.AreEqual(n, counts.Sum(), tag + ": число карт N");

            // Правило «лишнее снимается с младшей редкости» не срабатывает, если даже все «+1» укладываются в N.
            int maxSum = 0;
            foreach (int rarity in RandomRarities)
            {
                double w = WeightOf(g, rarity);
                if (w <= 0) continue;
                double q = n / w;
                int fl = (int)Math.Floor(q + 1e-9);
                maxSum += fl + (q - fl > 1e-9 ? 1 : 0);
            }
            foreach (int rarity in RandomRarities)
            {
                double w = WeightOf(g, rarity);
                string rt = tag + ": " + Rarity.Name(rarity);
                if (w <= 0)
                {
                    Assert.AreEqual(0, counts[rarity], rt + " — вес 0, редкость недоступна");
                    continue;
                }
                double q = n / w;
                int fl = (int)Math.Floor(q + 1e-9);
                int hi = fl + (q - fl > 1e-9 ? 1 : 0);
                if (maxSum <= n) Assert.That(counts[rarity], Is.InRange(fl, hi), rt + ": floor(N / W) или +1 с вероятностью дробной части");
                else Assert.That(counts[rarity], Is.InRange(0, hi), rt);
            }

            for (int rarity = Rarity.Common; rarity <= Rarity.Legendary; rarity++)
            {
                if (counts[rarity] == 0) continue;
                int cands = B.CardsInOrder.Count(c => c.Rarity == rarity && c.MineUnlock <= ctx.Rank);
                int k = Math.Min(StacksByRule(rarity, counts[rarity]), cands);
                var list = amounts[rarity].OrderByDescending(x => x).ToList();
                string rt = tag + ": стопки " + Rarity.Name(rarity) + " (" + counts[rarity] + " шт.)";
                Assert.AreEqual(k, list.Count, rt + ": число разных ID");
                int per = counts[rarity] / k, rem = counts[rarity] % k;
                var expected = Enumerable.Repeat(per, k).ToList();
                expected[0] += rem;
                CollectionAssert.AreEqual(expected, list, rt + ": поровну, остаток первой стопке");
            }

            double mult = SoftMult(g, ctx.Multipliers);
            double elixir = r.Elixir.ToDouble();
            if (g.SoftCurrencyMax <= 0) Assert.AreEqual(0.0, elixir, tag + ": эликсира нет");
            else
                Assert.IsTrue(Enumerable.Range((int)g.SoftCurrencyMin, (int)(g.SoftCurrencyMax - g.SoftCurrencyMin) + 1).Any(k => Payout(k * mult) == elixir),
                    tag + ": эликсир " + elixir + " = floor(k × " + mult.ToString(CultureInfo.InvariantCulture) + "), k ∈ [Min; Max]");
            return counts;
        }

        /// <summary>
        /// 10.3 п. 5, E-11: при множителях zone44 floor(N / Weight) воспроизводит публичную таблицу —
        /// Wooden 24 карты (не меньше 3 Uncommon), Iron 48 (не меньше 6 Uncommon), Golden 102 (не меньше 25 Uncommon и 2 Rare).
        /// Для всех случайных сундуков — общий состав по 10.3 и частота «+1» около дробной части N / W.
        /// </summary>
        [Test]
        public void ChestCardCounts_Zone44ReproducesPublicTable()
        {
            var s = NewDebug(44);
            var ctx = s.BuildGachaContext();
            Assert.AreEqual(44, ctx.Rank);

            var table = new[] { ("GachaWooden", 24, 3, 0), ("GachaIron", 48, 6, 0), ("GachaGolden", 102, 25, 2) };
            foreach (var (id, n, minU, minR) in table)
                Assert.AreEqual(n, ExpectedN(B.Gacha[id], ctx.Multipliers), id + ": N при множителях zone44");

            const int seeds = 200;
            foreach (var g in B.Root.Gacha.Where(x => x.BaseNumCards > 0))
            {
                int n = ExpectedN(g, ctx.Multipliers);
                var extra = new int[7];
                for (ulong seed = 1; seed <= seeds; seed++)
                {
                    string tag = "zone44 " + g.Id + " seed " + seed;
                    var r = GachaService.Open(B, g, ctx, new Pcg32(seed, 1));
                    var counts = AssertRandomChest(r, g, ctx, tag);
                    foreach (var (id, tn, minU, minR) in table)
                    {
                        if (id != g.Id) continue;
                        Assert.AreEqual(tn, counts.Sum(), tag + ": публичная таблица, всего карт");
                        Assert.GreaterOrEqual(counts[Rarity.Uncommon], minU, tag + ": публичная таблица, Uncommon");
                        Assert.GreaterOrEqual(counts[Rarity.Rare], minR, tag + ": публичная таблица, Rare");
                    }
                    foreach (int rarity in RandomRarities)
                    {
                        double w = WeightOf(g, rarity);
                        if (w > 0 && counts[rarity] > (int)Math.Floor(n / w + 1e-9)) extra[rarity]++;
                    }
                }
                // [Р] «+1 с вероятностью дробной части N / W»: на 200 seed частота близка к дробной части.
                foreach (int rarity in RandomRarities)
                {
                    double w = WeightOf(g, rarity);
                    if (w <= 0) continue;
                    double frac = n / w - Math.Floor(n / w + 1e-9);
                    Assert.AreEqual(frac, extra[rarity] / (double)seeds, 0.15,
                        "zone44 " + g.Id + " " + Rarity.Name(rarity) + ": частота дополнительной карты");
                }
            }
        }

        /// <summary>
        /// Контрольные значения E-11 и E-12 на настоящем алгоритме открытия: zone1 Wooden — 1 карта и эликсир 15–19;
        /// половины zone2 Golden 4.5 → 5, zone5 Forged 6.5 → 7, zone14 Crusher 52.5 → 53;
        /// zone18 Iron (N = 28, Uncommon ровно 4): Common 24 шт. → 2 разные карты по 12 (порог 7).
        /// </summary>
        [Test]
        public void ChestCardCounts_E11AndE12ControlValues()
        {
            var halves = new[] { (2, "GachaGolden", 5), (5, "GachaForged", 7), (14, "GachaCrusher", 53) };
            foreach (var (zone, id, n) in halves)
            {
                var g = B.Gacha[id];
                var ctx = NewDebug(zone).BuildGachaContext();
                Assert.AreEqual(n, ExpectedN(g, ctx.Multipliers), "zone" + zone + " " + id);
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    var r = GachaService.Open(B, g, ctx, new Pcg32(seed, 1));
                    Assert.AreEqual(n, r.Cards.Sum(c => c.Count), "zone" + zone + " " + id + " seed " + seed + ": половина вверх");
                    AssertRandomChest(r, g, ctx, "zone" + zone + " " + id + " seed " + seed);
                }
            }

            var wooden = B.Gacha["GachaWooden"];
            var ctx1 = NewDebug(1).BuildGachaContext();
            double minElixir = double.MaxValue, maxElixir = double.MinValue;
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var r = GachaService.Open(B, wooden, ctx1, new Pcg32(seed, 1));
                Assert.AreEqual(1, r.Cards.Sum(c => c.Count), "zone1 Wooden: 1 карта");
                double e = r.Elixir.ToDouble();
                Assert.That(e, Is.InRange(15.0, 19.0), "zone1 Wooden: эликсир 15–19");
                Assert.AreEqual(Math.Floor(e), e, "эликсир целый");
                minElixir = Math.Min(minElixir, e);
                maxElixir = Math.Max(maxElixir, e);
            }
            Assert.AreEqual(15.0, minElixir, "нижняя граница floor(45 × 0.35) достижима");
            Assert.AreEqual(19.0, maxElixir, "верхняя граница floor(55 × 0.35) достижима");

            var iron = B.Gacha["GachaIron"];
            var ctx18 = NewDebug(18).BuildGachaContext();
            Assert.AreEqual(28, ExpectedN(iron, ctx18.Multipliers), "zone18 Iron: N = 28");
            int common24 = 0;
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var r = GachaService.Open(B, iron, ctx18, new Pcg32(seed, 1));
                var counts = AssertRandomChest(r, iron, ctx18, "zone18 Iron seed " + seed);
                Assert.AreEqual(4, counts[Rarity.Uncommon], "28 / 7 = 4 без дробной части");
                if (counts[Rarity.Common] != 24) continue;
                common24++;
                var commons = r.Cards.Where(c => B.Cards[c.CardId].Rarity == Rarity.Common).Select(c => c.Count).ToArray();
                CollectionAssert.AreEqual(new[] { 12, 12 }, commons, "E-12: Common 24 шт. → 2 разные карты по 12");
            }
            Assert.Greater(common24, 0, "в выборке встретился случай Common = 24");
        }

        // ───────────────────────────── 10.3 п. 8: детерминизм ─────────────────────────────

        /// <summary>
        /// 10.3 п. 8, 15.2: результат открытия определяется только сохраняемым ГСЧ — один seed даёт тот же ChestResult
        /// и то же состояние ГСЧ после открытия (все 58 записей Gacha, контексты zone5 и zone44).
        /// Разные seed могут давать разное; проверяется лишь, что случайная часть действительно зависит от ГСЧ.
        /// </summary>
        [Test]
        public void ChestOpening_FixedSeedGivesIdenticalResult()
        {
            var contexts = new[] { 5, 44 }.Select(z => NewDebug(z).BuildGachaContext()).ToArray();
            foreach (var ctx in contexts)
            {
                foreach (var g in B.Root.Gacha)
                {
                    foreach (var seed in new[] { 1UL, 42UL, 12345UL })
                    {
                        var r1 = new Pcg32(seed, 1);
                        var r2 = new Pcg32(seed, 1);
                        var a = GachaService.Open(B, g, ctx, r1);
                        var b = GachaService.Open(B, g, ctx, r2);
                        string tag = g.Id + " rank " + ctx.Rank + " seed " + seed;
                        Assert.AreEqual(Json(a), Json(b), tag + ": одинаковый ChestResult");
                        Assert.AreEqual(r1.State, r2.State, tag + ": одинаковое состояние ГСЧ после открытия");
                    }
                }
            }

            var golden = B.Gacha["GachaGolden"];
            var distinct = new HashSet<string>();
            for (ulong seed = 1; seed <= 16; seed++)
                distinct.Add(Json(GachaService.Open(B, golden, contexts[1], new Pcg32(seed, 1))));
            Assert.Greater(distinct.Count, 1, "случайная часть сундука берётся из ГСЧ");
        }

        /// <summary>
        /// 10.3 п. 8: в игре результат определяется одной транзакцией по сохраняемому ГСЧ. Две сессии с одним seed
        /// получают одинаковые сундуки; перезагрузка сохранения до открытия даёт тот же результат (повторного броска нет);
        /// после save/load результат в очереди показа тот же, а ГСЧ не сдвигается.
        /// </summary>
        [Test]
        public void ChestOpening_SessionResultIsFixedBySavedRng()
        {
            string[] chestRocks = { "rockgachawooden", "rockgachairon" };

            string Play(GameSession s)
            {
                foreach (var id in chestRocks) s.DebugCompleteTarget(RocksOf(s, id).First().Ordinal);
                return Json(s.Player.ChestReveals) + "|" + Json(s.Player.Cards) + "|" + s.Player.Elixir.ToStorageString() + "|" + s.Player.Rng.Gacha.State;
            }
            Assert.AreEqual(Play(NewDebug(6)), Play(NewDebug(6)), "один seed — одинаковые сундуки и начисления");

            var s0 = NewDebug(6);
            var wooden = RocksOf(s0, "rockgachawooden").First();
            string beforeOpen = Snapshot(s0);
            s0.DebugCompleteTarget(wooden.Ordinal);
            var first = s0.Player.ChestReveals.Last();
            Assert.AreEqual("GachaWooden", first.GachaId);
            string firstJson = Json(first);

            var reloaded = Restore(beforeOpen);
            reloaded.DebugCompleteTarget(wooden.Ordinal);
            Assert.AreEqual(firstJson, Json(reloaded.Player.ChestReveals.Last()), "перезагрузка до открытия не даёт другого броска");
            Assert.AreEqual(s0.Player.Rng.Gacha.State, reloaded.Player.Rng.Gacha.State);

            var afterLoad = Restore(Snapshot(s0));
            Assert.AreEqual(Json(s0.Player.ChestReveals), Json(afterLoad.Player.ChestReveals), "результат сохранён до показа");
            Assert.AreEqual(s0.Player.Rng.Gacha.State, afterLoad.Player.Rng.Gacha.State, "загрузка не тратит ГСЧ");
            var shown = afterLoad.TakeChestReveal();
            Assert.IsNotNull(shown);
            Assert.AreEqual(firstJson, Json(shown), "показывается уже определённый результат");
            Assert.AreEqual(s0.Player.Rng.Gacha.State, afterLoad.Player.Rng.Gacha.State, "показ не перебрасывает сундук");
        }
    }
}
