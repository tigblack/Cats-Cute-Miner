using System;
using System.Collections.Generic;
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
    /// ТЗ 17.1, группа 9 «Переход»: условия перехода (11.2), сброс и перенос состояния по таблице 11.3,
    /// награда выхода zone1–10 по нижней границе (E-13), конец кампании после zone144 (11.4).
    /// Дополняет SimulationTests.ZoneTransitionRules (zone2: неразрушенная шахта, оставшиеся камни, ca002×1).
    /// </summary>
    public class TransitionAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;

        static GameSession NewDebug(int zone) =>
            GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Debug, 1_000_000L, zone);

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Restore(string json) =>
            GameSession.FromState(TestData.Balance, SaveService.DeserializeState(json));

        static string Json(object o) => JsonConvert.SerializeObject(o, SaveService.Settings());

        /// <summary>Число полученных копий карты (уровень 1 = первая копия, плюс дубликаты). Верно, пока карты не улучшались.</summary>
        static int Copies(GameSession s, string cardId) =>
            s.Player.Cards.TryGetValue(cardId, out var c) ? (c.Level > 0 ? 1 : 0) + c.Duplicates : 0;

        static int CardLevelRaw(GameSession s, string cardId) =>
            s.Player.Cards.TryGetValue(cardId, out var c) ? c.Level : 0;

        static void ClearAndActivate(GameSession s, ZoneObject shaft)
        {
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shaft.Ordinal].Status, "шахта расчищена");
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal), "активация шахты " + shaft.TypeId);
        }

        static void BreakAndOpenGate(GameSession s, ZoneObject gate)
        {
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[gate.Ordinal].Status);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "открытие ворот " + gate.GateOrder);
        }

        /// <summary>Только обязательное по 11.2: все шахты активированы, все ворота открыты (снизу вверх), выход Broken.</summary>
        static void ClearMandatory(GameSession s)
        {
            foreach (var sh in s.Layout.Shafts) ClearAndActivate(s, sh);
            foreach (var g in s.Layout.Gates) BreakAndOpenGate(s, g);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[s.Layout.Exit.Ordinal].Status);
        }

        static ContainerState DeliveryBox(GameSession s) => s.Mine.Containers.FirstOrDefault(c => c.Kind == ContainerKind.Delivery);

        /// <summary>Идти по времени, открывая «лишние» доставки, пока на поле не окажется нужная (одна доставка на поле, 10.5).</summary>
        static ContainerState WaitForDelivery(GameSession s, Func<ContainerState, bool> wanted, string what)
        {
            for (int i = 0; i < 20000; i++)
            {
                var box = DeliveryBox(s);
                if (box != null)
                {
                    if (wanted(box)) return box;
                    Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
                }
                s.Advance(5 * S);
            }
            Assert.Fail("Не дождались доставки: " + what);
            return null;
        }

        static bool IsSoftBox(ContainerState c) => c.RewardType == RewardTypes.Currency && c.DetailedType == "Soft";
        static bool IsGoldBox(ContainerState c) => c.RewardType == RewardTypes.Currency && c.DetailedType == "Core";
        static bool IsCatBox(ContainerState c) => c.RewardType == RewardTypes.Miner;
        static bool IsDynamiteBox(ContainerState c) => c.RewardType == RewardTypes.Spell;

        // Награда выхода zone8 по нижней границе (JSON scg_m008_exit: GuaranteedCardMin, SoftCurrencyMin).
        static readonly Dictionary<string, int> Zone8ExitCards = new Dictionary<string, int> { { "ca006", 4 }, { "ca003", 6 }, { "ca015", 4 }, { "ca029", 1 } };
        const double Zone8ExitElixir = 100;

        // ───────────────────────────── Подготовленная zone8 ─────────────────────────────

        static string s_Zone8Base;
        static string s_Zone8Soft;
        static double s_Zone8SoftAmount;

        /// <summary>
        /// zone8 (Debug): все 4 шахты активированы (доставки и бесплатный сундук включены), ворота открыты, выход Broken,
        /// разбиты камни с твёрдой валютой и валютой пропуска, 4 найма и одно объединение, котик назначен на камень,
        /// кузница улучшена до 10 (первый порог), отмечен шаг обучения, прошло 4 ч 1 мин, бесплатный сундук получен.
        /// </summary>
        static GameSession Zone8Base()
        {
            if (s_Zone8Base == null)
            {
                var s = NewDebug(8);
                Assert.AreEqual(4, s.Layout.Shafts.Count);
                Assert.AreEqual(1, s.Layout.Gates.Count);
                foreach (var sh in s.Layout.Shafts) ClearAndActivate(s, sh);
                BreakAndOpenGate(s, s.Layout.Gates[0]);
                s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
                foreach (var o in s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock &&
                             (o.TypeId == "rockhardcurrencybig" || o.TypeId == "rockbattlepasscurrency")).ToList())
                    s.DebugCompleteTarget(o.Ordinal);

                s.DebugAddGold(GameNumber.FromDouble(1e9));
                for (int i = 0; i < 4; i++) Assert.AreEqual(CommandResult.Ok, s.Hire(), "наём " + i);
                var a = s.Mine.Cats[0];
                var b = s.Mine.Cats[1];
                Assert.IsTrue(s.DropCat(a.Id, s.Layout.Col(b.Cell) + 0.5, s.Layout.Row(b.Cell) + 0.5).Merged, "объединение");
                var worker = s.Mine.Cats.Last();
                var rock = s.Layout.Objects.First(o => o.Kind == ObjectKind.Rock && s.AcceptsCat(o.Ordinal, worker.Id));
                var drop = s.DropCat(worker.Id, rock.Col + 0.5, rock.Row + 0.5);
                Assert.AreEqual(CommandResult.Ok, drop.Result);
                Assert.AreEqual(rock.Index, worker.TargetIndex, "котик назначен на камень");

                int cart = s.Layout.Cart.Ordinal;
                Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(cart, out _, 9));
                Assert.AreEqual(10, s.Mine.Objects[cart].Gen.Level);
                Assert.AreEqual(1, s.Mine.Objects[cart].Gen.ThresholdsReached, "кузница: первый порог T = 10 (E-04)");

                s.Player.TutorialDone.Add("tutorial:first-hire");
                s.Advance(4 * 3600 * S + 60 * S);
                Assert.GreaterOrEqual(s.Player.FreeChest.Stock, 1, "бесплатный сундук через 4 ч после включения (E-17)");
                Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest());
                s.Events.Clear();
                s_Zone8Base = Snapshot(s);
            }
            return Restore(s_Zone8Base);
        }

        /// <summary>zone8Base + на поле неоткрытая доставка с эликсиром и контейнер бесплатного котика.</summary>
        static GameSession Zone8WithSoftDelivery(out double softAmount)
        {
            if (s_Zone8Soft == null)
            {
                var s = Zone8Base();
                var box = WaitForDelivery(s, IsSoftBox, "эликсир");
                for (int i = 0; i < 200 && !s.Mine.Containers.Any(c => c.Kind == ContainerKind.FreeCat); i++) s.Advance(60 * S);
                Assert.IsTrue(s.Mine.Containers.Any(c => c.Kind == ContainerKind.FreeCat), "контейнер бесплатного котика на поле");
                Assert.AreSame(box, DeliveryBox(s), "доставка с эликсиром не открыта");
                s_Zone8SoftAmount = box.Amount.ToDouble();
                s.Events.Clear();
                s_Zone8Soft = Snapshot(s);
            }
            softAmount = s_Zone8SoftAmount;
            return Restore(s_Zone8Soft);
        }

        // ───────────────────────────── 11.2: условия перехода ─────────────────────────────

        [Test]
        public void CompleteZoneRejectedWhileClearedShaftNotActivated()
        {
            var s = NewDebug(5);
            Assert.AreEqual(4, s.Layout.Shafts.Count);
            var shafts = s.Layout.Shafts;
            for (int i = 0; i < 3; i++) ClearAndActivate(s, shafts[i]);
            var last = shafts[3];
            s.DebugCompleteTarget(last.Ordinal);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[last.Ordinal].Status, "шахта расчищена, но не активирована");
            BreakAndOpenGate(s, s.Layout.Gates[0]);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);

            Assert.IsFalse(s.CanCompleteZone(out string reason));
            Assert.AreEqual("Shafts activated: 3/4", reason, "надпись вместо кнопки по 11.2");
            Assert.AreEqual(last.Ordinal, s.NearestInactiveShaft(), "камера ведёт к неактивированной шахте");

            s.Events.Clear();
            string before = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.CompleteZone(), "переход запрещён");
            Assert.AreEqual(before, Snapshot(s), "отказ не меняет состояние: нет награды выхода, зона та же");
            Assert.AreEqual(5, s.Mine.Zone);
            Assert.AreEqual(0, s.Player.HighestCompletedZone);
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.MineCompleted || e.Type == SimEventType.ZoneEntered));

            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(last.Ordinal));
            Assert.IsTrue(s.CanCompleteZone(out reason), reason);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(6, s.Mine.Zone);
        }

        [Test]
        public void CompleteZoneRejectedUntilEveryGateOpened()
        {
            // zone10: 2 ворот. Условие 3 из 11.2 проверяется явно, хотя выход в верхнем секторе.
            var s = NewDebug(10);
            Assert.AreEqual(2, s.Layout.Gates.Count);
            foreach (var sh in s.Layout.Shafts) ClearAndActivate(s, sh);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsFalse(s.CanCompleteZone(out _), "ворота закрыты");

            var g0 = s.Layout.Gates[0];
            var g1 = s.Layout.Gates[1];
            BreakAndOpenGate(s, g0);
            s.DebugCompleteTarget(g1.Ordinal);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[g1.Ordinal].Status);
            Assert.IsFalse(s.CanCompleteZone(out _), "Broken без тапа — ворота не открыты");
            s.Events.Clear();
            string before = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(before, Snapshot(s), "отказ не меняет состояние");
            Assert.AreEqual(10, s.Mine.Zone);

            Assert.AreEqual(CommandResult.Ok, s.OpenGate(g1.Ordinal));
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(11, s.Mine.Zone);
        }

        [Test]
        public void TransitionAllowedWithAllRocksAndChestRocksIntact()
        {
            // 11.2: «Оставшиеся камни и сундуки переход не блокируют». Ни один камень не тронут.
            int zonesWithChestRocks = 0;
            for (int zone = 1; zone <= 10; zone++)
            {
                var s = NewDebug(zone);
                ClearMandatory(s);
                var rocks = s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock).ToList();
                Assert.Greater(rocks.Count, 0, "zone" + zone);
                Assert.IsTrue(rocks.All(o => s.Mine.Objects[o.Ordinal].Status == ObjectStatus.Intact), "zone" + zone + ": все камни целы");
                bool chestRock = rocks.Any(o => TestData.Balance.Rocks[o.TypeId].RewardCycle.All(e => e.RewardType == RewardTypes.Gacha));
                if (chestRock) zonesWithChestRocks++;
                if (s.Layout.Shafts.Count > 0)
                    Assert.Greater(s.Player.ChestReveals.Count, 0, "zone" + zone + ": в очереди есть неоткрытые сундуки");
                Assert.IsTrue(s.CanCompleteZone(out string reason), "zone" + zone + ": " + reason);
                Assert.AreEqual(CommandResult.Ok, s.CompleteZone(), "zone" + zone);
                Assert.AreEqual(zone + 1, s.Mine.Zone);
                Assert.AreEqual(zone, s.Player.HighestCompletedZone);
                Assert.AreEqual(zone + 1, s.Player.CurrentZone, "Rank = номер следующей зоны");
            }
            Assert.GreaterOrEqual(zonesWithChestRocks, 8, "камни-сундуки есть в zone3–zone10");
        }

        // ───────────────────────────── E-13: награда выхода zone1–10 ─────────────────────────────

        [Test]
        public void ExitRewardZones1To10UsesLowerBound()
        {
            // JSON scg_m001…m010_exit: GuaranteedCardMin и SoftCurrencyMin. zone1–3 — контрольные значения Приложения A и E-13.
            var expectedCards = new[]
            {
                new Dictionary<string, int> { { "ca001", 1 } },
                new Dictionary<string, int> { { "ca001", 1 }, { "ca002", 1 } },
                new Dictionary<string, int> { { "ca002", 3 }, { "ca014", 1 } },
                new Dictionary<string, int> { { "ca002", 3 }, { "ca014", 1 }, { "ca026", 1 } },
                new Dictionary<string, int> { { "ca003", 4 }, { "ca014", 2 }, { "ca029", 1 } },
                new Dictionary<string, int> { { "ca001", 2 }, { "ca003", 4 }, { "ca014", 2 }, { "ca026", 1 } },
                new Dictionary<string, int> { { "ca004", 2 }, { "ca006", 5 }, { "ca015", 3 }, { "ca050", 1 } },
                new Dictionary<string, int> { { "ca006", 4 }, { "ca003", 6 }, { "ca015", 4 }, { "ca029", 1 } },
                new Dictionary<string, int> { { "ca001", 4 }, { "ca002", 8 }, { "ca016", 1 }, { "ca015", 3 }, { "ca026", 1 } },
                new Dictionary<string, int> { { "ca005", 3 }, { "ca007", 10 }, { "ca014", 1 }, { "ca016", 3 }, { "ca030", 1 } },
            };
            var expectedElixir = new double[] { 17, 30, 50, 60, 70, 80, 90, 100, 100, 100 };

            double elixirTotal = 0, elixirUpper = 0;
            int cardsTotal = 0, cardsUpper = 0;
            for (int zone = 1; zone <= 10; zone++)
            {
                string tag = "zone" + zone;
                string gachaId = "scg_m" + zone.ToString("D3") + "_exit";
                var s = NewDebug(zone);
                Assert.AreEqual(gachaId, s.Layout.Exit.RewardId, tag);
                ClearMandatory(s);

                var cards = expectedCards[zone - 1];
                var copiesBefore = cards.Keys.ToDictionary(id => id, id => Copies(s, id));
                var elixirBefore = s.Player.Elixir;
                int revealsBefore = s.Player.ChestReveals.Count;

                Assert.AreEqual(CommandResult.Ok, s.CompleteZone(), tag);

                Assert.AreEqual(revealsBefore + 1, s.Player.ChestReveals.Count, tag + ": один сундук выхода");
                var reveal = s.Player.ChestReveals.Last();
                Assert.AreEqual(gachaId, reveal.GachaId, tag);
                Assert.AreEqual(expectedElixir[zone - 1], reveal.Elixir.ToDouble(), tag + ": эликсир = SoftCurrencyMin");
                Assert.AreEqual(expectedElixir[zone - 1], (s.Player.Elixir - elixirBefore).ToDouble(), 1e-9, tag + ": начислен ровно SoftCurrencyMin");
                CollectionAssert.AreEquivalent(
                    cards.Select(kv => kv.Key + "x" + kv.Value),
                    reveal.Cards.Select(c => c.CardId + "x" + c.Count),
                    tag + ": карточки = GuaranteedCardMin, без случайных и без CardMultMax");
                foreach (var kv in cards)
                    Assert.AreEqual(copiesBefore[kv.Key] + kv.Value, Copies(s, kv.Key), tag + ": копии " + kv.Key);

                elixirTotal += expectedElixir[zone - 1];
                cardsTotal += cards.Values.Sum();
                var dto = TestData.Balance.Gacha[gachaId];
                elixirUpper += dto.SoftCurrencyMax;
                cardsUpper += dto.GuaranteedCardCounts.Sum();
            }
            Assert.AreEqual(697.0, elixirTotal);
            Assert.AreEqual(89, cardsTotal);
            // 11.2: «Разница с полным референсом — 1803 эликсира и 82 гарантированные карточки на zone1–10».
            Assert.AreEqual(1803.0, elixirUpper - elixirTotal, "выдана не верхняя граница эликсира");
            Assert.AreEqual(82, cardsUpper - cardsTotal, "выдана не верхняя граница карточек");
        }

        // ───────────────────────────── 11.3: что переносится ─────────────────────────────

        [Test]
        public void Table11_3_PlayerProgressIsCarriedOver()
        {
            var s = Zone8WithSoftDelivery(out double softAmount);
            var p = s.Player;

            // Каждая строка левого столбца перед переходом непуста.
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);
            Assert.GreaterOrEqual(p.Hard.ToDouble(), 10.0, "rockhardcurrencybig: Hard = Quantity = 10 (E-09)");
            Assert.Greater(p.Elixir.ToDouble(), 0);
            Assert.IsTrue(p.Cards.Values.Any(c => c.Level > 0));
            Assert.IsTrue(p.RewardCycle.ContainsKey("rockhardcurrencybig") && p.RewardCycle.ContainsKey("rockbattlepasscurrency"), "счётчики RewardCycle");
            Assert.Greater(p.ChestReveals.Count, 0, "очередь неоткрытых сундуков");
            Assert.IsTrue(p.FreeChest.Enabled);
            Assert.AreEqual(1, p.FreeChest.NextIndex, "индекс цикла бесплатного сундука");
            Assert.Greater(p.FreeChest.TimerTicks, 0);
            Assert.Greater(p.DeliveryHistory.DupeTimes.Count, 0, "окна MaxDupes");
            Assert.Greater(p.DeliveryHistory.ClaimTimes.Count + p.DeliveryHistory.SkippedAds, 0);
            Assert.IsTrue(p.ExcludedRewards.ContainsKey("BattlePass"), "журнал исключённых наград");
            Assert.AreEqual(1, p.TutorialDone.Count);
            Assert.Greater(softAmount, 0);

            var elixir = p.Elixir;
            var hard = p.Hard;
            int dynamite = p.Dynamite;
            var cardIds = p.Cards.Keys.Union(Zone8ExitCards.Keys).ToList();
            var copiesBefore = cardIds.ToDictionary(id => id, id => Copies(s, id));
            var levelBefore = cardIds.ToDictionary(id => id, id => CardLevelRaw(s, id));
            string rewardCycle = Json(p.RewardCycle);
            string reveals = Json(p.ChestReveals);
            int revealCount = p.ChestReveals.Count;
            string freeChest = Json(p.FreeChest);
            string history = Json(p.DeliveryHistory);
            string rng = Json(p.Rng);
            string tutorial = Json(p.TutorialDone);
            string excluded = Json(p.ExcludedRewards);

            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            p = s.Player;
            Assert.AreEqual(9, s.Mine.Zone);

            // Эликсир, твёрдая валюта; эликсир неоткрытой доставки начисляется при CompleteZone.
            Assert.AreEqual((elixir + GameNumber.FromDouble(Zone8ExitElixir) + GameNumber.FromDouble(softAmount)).ToDouble(), p.Elixir.ToDouble(), 1e-9,
                "эликсир: прежний + выход zone8 (100) + неоткрытая доставка с эликсиром");
            Assert.AreEqual(hard, p.Hard, "твёрдая валюта переносится");
            // Карточки: копии и уровни сохраняются, добавляется только награда выхода.
            foreach (var id in cardIds)
            {
                Zone8ExitCards.TryGetValue(id, out int grant);
                Assert.AreEqual(copiesBefore[id] + grant, Copies(s, id), "копии " + id);
                int expectedLevel = levelBefore[id] > 0 ? levelBefore[id] : (grant > 0 ? 1 : 0);
                Assert.AreEqual(expectedLevel, CardLevelRaw(s, id), "уровень " + id);
            }
            Assert.AreEqual(rewardCycle, Json(p.RewardCycle), "счётчики RewardCycle по Rocks.Id");
            Assert.AreEqual(dynamite, p.Dynamite, "запас динамита");
            Assert.AreEqual(revealCount + 1, p.ChestReveals.Count, "очередь сохраняется, добавлен сундук выхода");
            Assert.AreEqual(reveals, Json(p.ChestReveals.Take(revealCount).ToList()), "результаты сундуков в очереди не перебрасываются");
            Assert.AreEqual("scg_m008_exit", p.ChestReveals.Last().GachaId);
            Assert.AreEqual(freeChest, Json(p.FreeChest), "бесплатный сундук: индекс, запас, таймер, флаг");
            Assert.AreEqual(history, Json(p.DeliveryHistory), "история доставок");
            Assert.AreEqual(rng, Json(p.Rng), "ГСЧ не сбрасывается (выход по нижней границе бросков не делает)");
            Assert.AreEqual(9, p.CurrentZone);
            Assert.AreEqual(8, p.HighestCompletedZone);
            Assert.IsFalse(p.CampaignCompleted);
            Assert.AreEqual(tutorial, Json(p.TutorialDone), "обучение");
            Assert.AreEqual(excluded, Json(p.ExcludedRewards), "журнал исключённых наград");
        }

        /// <summary>
        /// 10.3, 11.2 п. 2, 11.3: определённые, но не открытые сундуки остаются в очереди PlayerProgressState и переносятся
        /// в следующую зону; длина очереди в ТЗ не ограничена. zone100 (не последняя зона): все камни, шахты, ворота и
        /// выход дают больше 50 сундуков, которые UI ещё не показал (TakeChestReveal не вызывался).
        /// </summary>
        [Test]
        public void Table11_3_ChestQueueAboveFiftyIsKeptAndCarriedOver()
        {
            var s = NewDebug(100);
            foreach (var o in s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock).ToList())
                s.DebugCompleteTarget(o.Ordinal);
            ClearMandatory(s);
            var opened = s.Events.Where(e => e.Type == SimEventType.ChestOpened).ToList();
            int determined = opened.Count;
            Assert.Greater(determined, 50, "в zone100 определено больше 50 сундуков");
            Assert.AreEqual(determined, s.Player.ChestReveals.Count, "все определённые и не показанные сундуки остаются в очереди (10.3, 11.3)");
            for (int i = 0; i < determined; i++)
            {
                Assert.AreEqual(opened[i].OpId, s.Player.ChestReveals[i].OpId, "сундук " + i + ": порядок очереди и opId");
                Assert.AreEqual(opened[i].Text, s.Player.ChestReveals[i].GachaId, "сундук " + i + ": тип сундука");
            }

            string reveals = Json(s.Player.ChestReveals);
            string exitReward = s.Layout.Exit.RewardId;
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(101, s.Mine.Zone);
            var p = s.Player;
            Assert.AreEqual(determined + 1, p.ChestReveals.Count, "очередь переносится целиком, добавлен сундук выхода (11.2 п. 2)");
            Assert.AreEqual(reveals, Json(p.ChestReveals.Take(determined).ToList()), "старые сундуки на месте, в том же порядке, без переброса");
            Assert.AreEqual(exitReward, p.ChestReveals.Last().GachaId, "сундук выхода — в конце очереди");

            // Очередь переживает save/load и разбирается тапами по порядку.
            string afterJson = Json(p.ChestReveals);
            var restored = Restore(Snapshot(s));
            Assert.AreEqual(afterJson, Json(restored.Player.ChestReveals), "очередь после загрузки та же");
            Assert.AreEqual(opened[0].OpId, restored.TakeChestReveal().OpId, "первым показывается самый старый сундук");
            Assert.AreEqual(determined, restored.Player.ChestReveals.Count);
        }

        [Test]
        public void Table11_3_UnopenedDynamiteDeliveryIsCreditedButNotAboveFour()
        {
            var s0 = Zone8Base();
            WaitForDelivery(s0, IsDynamiteBox, "динамит");
            s0.Events.Clear();
            string ready = Snapshot(s0);
            foreach (int stock in new[] { 0, 3, 4 })
            {
                var s = Restore(ready);
                s.DebugAddDynamite(-GameSession.MaxDynamite);
                s.DebugAddDynamite(stock);
                Assert.AreEqual(stock, s.Player.Dynamite);
                var elixir = s.Player.Elixir;
                Assert.AreEqual(CommandResult.Ok, s.CompleteZone(), "запас " + stock);
                Assert.AreEqual(Math.Min(4, stock + 1), s.Player.Dynamite, "запас " + stock + ": заряд из ящика, не сверх 4");
                Assert.AreEqual(elixir.ToDouble() + Zone8ExitElixir, s.Player.Elixir.ToDouble(), 1e-9, "запас " + stock + ": эликсир только от выхода");
                Assert.AreEqual(0, s.Mine.Containers.Count);
            }
        }

        // ───────────────────────────── 11.3: что создаётся заново ─────────────────────────────

        [Test]
        public void Table11_3_MineProgressIsCreatedAnew()
        {
            var s = Zone8WithSoftDelivery(out _);
            var m = s.Mine;
            int cart = s.Layout.Cart.Ordinal;

            // Перед переходом в старой зоне всё «накоплено».
            Assert.Greater(m.Gold.ToDouble(), 1e6);
            Assert.Greater(m.Cats.Count, 0);
            Assert.IsTrue(m.Containers.Any(c => c.Kind == ContainerKind.FreeCat));
            Assert.IsTrue(m.Objects.Any(o => o.Status == ObjectStatus.Destroyed));
            Assert.AreEqual(1, m.GatesOpened);
            Assert.IsTrue(s.IsSectorOpen(1));
            Assert.Greater(m.HireCount, 0);
            Assert.IsTrue(m.DeliveriesEnabled);
            Assert.AreEqual(10, m.Objects[cart].Gen.Level);
            Assert.AreEqual(1, m.Objects[cart].Gen.ThresholdsReached);
            Assert.IsTrue(s.Layout.Shafts.Any(sh => m.Objects[sh.Ordinal].Gen.PayoutReady), "есть несобранная выплата");
            Assert.Greater(m.ZoneTimeTicks, 0);
            Assert.Greater(m.RocksDestroyed, 0);
            Assert.Greater(m.CatsHired, 0);
            Assert.Greater(m.Merges, 0);

            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            var n = s.Mine;
            Assert.AreEqual(9, n.Zone);
            Assert.AreEqual("zone9", s.Layout.ZoneId);

            // Золото = StartingCoreCurrency.
            Assert.AreEqual(25.0, n.Gold.ToDouble(), "золото = StartingCoreCurrency");
            // Котики, контейнер бесплатного котика, ящики доставок — теряются.
            Assert.AreEqual(0, n.Cats.Count);
            Assert.AreEqual(0, n.Containers.Count);
            Assert.AreEqual(0, s.OccupiedSlots);
            // Состояния объектов, HP, открытые сектора; генераторы.
            Assert.AreEqual(s.Layout.Objects.Count, n.Objects.Count);
            foreach (var obj in s.Layout.Objects)
            {
                var st = n.Objects[obj.Ordinal];
                string tag = obj.TypeId + "@" + obj.Index;
                if (obj.Kind == ObjectKind.Cart)
                {
                    Assert.AreEqual(ObjectStatus.Producing, st.Status, tag);
                    Assert.AreEqual(1, st.Gen.Level, "кузница снова L1");
                    Assert.AreEqual(0, st.Gen.ThresholdsReached, "пороги кузницы сброшены");
                    Assert.AreEqual(0L, st.Gen.CycleTicks, "фаза цикла сброшена");
                    Assert.IsFalse(st.Gen.PayoutReady);
                    continue;
                }
                Assert.AreEqual(ObjectStatus.Intact, st.Status, tag);
                Assert.IsTrue(st.Gen == null || (st.Gen.Level == 1 && st.Gen.ThresholdsReached == 0 && st.Gen.CycleTicks == 0 && !st.Gen.PayoutReady && !st.Gen.Automated),
                    tag + ": генератор шахты не унаследован");
                if (obj.IsTarget) Assert.AreEqual(TestData.Balance.TargetHp(obj.TypeId, obj.Level), st.Hp, tag + ": полное HP");
            }
            Assert.AreEqual(1, s.Layout.Gates.Count);
            Assert.AreEqual(0, n.GatesOpened);
            Assert.IsFalse(s.IsSectorOpen(1), "сектор за воротами закрыт");
            Assert.AreEqual(0, n.ActivatedShafts);
            // Счётчик и уровень найма (E-05: первая цена 2, уровень 1).
            Assert.AreEqual(0, n.HireCount);
            Assert.AreEqual(1, s.HireLevel());
            Assert.AreEqual(2.0, s.HirePrice().ToDouble(), "цена без скидки ворот");
            // Бонусы ворот сброшены; остаются только эффекты карт (E-06, E-08).
            Assert.AreEqual(1.0, s.HireDivisor().ToDouble());
            Assert.AreEqual(10 + s.CardLevel("ca038"), s.UnitCap(), "лимит = BaseUnitCap + ca038");
            Assert.AreEqual(Math.Max(60.0, 2400.0 - 60.0 * s.CardLevel("ca029")), s.FreeCatIntervalSeconds(), 1e-9, "интервал без −60 с за ворота");
            int ca034 = s.CardLevel("ca034");
            double incomeMul = ca034 > 0 ? Math.Pow(3, ca034 - 1) : 1.0;
            Assert.AreEqual(incomeMul, s.GlobalIncomeMultiplier().ToDouble(), 1e-9, "доход без ×2 за ворота");
            Assert.AreEqual(1.5 * incomeMul, s.AutoIncomePerSecond().ToDouble(), 1e-9, "только кузница L1: 3 за 2 с (E-03, E-10)");
            // Таймеры зоны.
            Assert.IsFalse(n.FreeCatReady);
            Assert.IsFalse(n.DeliveriesEnabled, "доставки снова ждут условий включения");
            Assert.IsFalse(n.DeliveryReady);
            // Локальная статистика зоны.
            Assert.AreEqual(0L, n.ZoneTimeTicks);
            Assert.AreEqual(0, n.RocksDestroyed);
            Assert.AreEqual(0, n.CatsHired);
            Assert.AreEqual(0, n.Merges);
        }

        [Test]
        public void Table11_3_UnopenedGoldAndCatDeliveriesAreLost()
        {
            foreach (var kind in new[] { "gold", "cat" })
            {
                var s = Zone8Base();
                var box = WaitForDelivery(s, kind == "gold" ? (Func<ContainerState, bool>)IsGoldBox : IsCatBox, kind);
                Assert.IsTrue(kind == "gold" ? box.Amount.IsPositive : box.CatLevel >= 1, kind);
                var elixir = s.Player.Elixir;
                var hard = s.Player.Hard;
                int dynamite = s.Player.Dynamite;
                Assert.AreEqual(CommandResult.Ok, s.CompleteZone(), kind);
                Assert.AreEqual(25.0, s.Mine.Gold.ToDouble(), kind + ": золото ящика не переносится");
                Assert.AreEqual(0, s.Mine.Cats.Count, kind + ": котик ящика не переносится");
                Assert.AreEqual(0, s.Mine.Containers.Count, kind);
                Assert.AreEqual(elixir.ToDouble() + Zone8ExitElixir, s.Player.Elixir.ToDouble(), 1e-9, kind + ": эликсир только от выхода");
                Assert.AreEqual(hard, s.Player.Hard, kind);
                Assert.AreEqual(dynamite, s.Player.Dynamite, kind);
            }
        }

        [Test]
        public void Table11_3_NewZoneRestartsFreeCatTimerAndDeliveryWaitsForUnlock()
        {
            var s = Zone8WithSoftDelivery(out _);
            Assert.IsTrue(s.Mine.Containers.Any(c => c.Kind == ContainerKind.FreeCat), "в старой зоне контейнер уже был");
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());

            // Таймер бесплатного котика стартует заново: полный интервал новой зоны.
            long interval = GameSession.SecondsToTicks(s.FreeCatIntervalSeconds());
            s.Advance(interval - 1);
            Assert.IsFalse(s.Mine.Containers.Any(c => c.Kind == ContainerKind.FreeCat), "до конца интервала контейнера нет");
            Assert.IsFalse(s.Mine.Containers.Any(c => c.Kind == ContainerKind.Delivery), "доставки не идут: в новой зоне нет активированных шахт");
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "Rank 9 ≥ 4, но шахт в текущей зоне 0 из 2");
            s.Advance(1);
            Assert.AreEqual(1, s.Mine.Containers.Count(c => c.Kind == ContainerKind.FreeCat), "контейнер ровно через интервал");

            // Таймер доставки: после двух активированных шахт новой зоны — DeliveryStartupDelaySec = 20 с.
            foreach (var sh in s.Layout.Shafts.Take(2)) ClearAndActivate(s, sh);
            Assert.IsTrue(s.Mine.DeliveriesEnabled);
            Assert.AreEqual(GameSession.SecondsToTicks(20), s.Mine.DeliveryTimerTicks, "первая доставка через 20 с");
            s.Events.Clear();
            s.Advance(20 * S - 1);
            Assert.IsFalse(s.Mine.Containers.Any(c => c.Kind == ContainerKind.Delivery));
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.DeliverySkippedAd));
            s.Advance(1);
            Assert.IsTrue(s.Mine.Containers.Any(c => c.Kind == ContainerKind.Delivery) || s.Events.Any(e => e.Type == SimEventType.DeliverySkippedAd),
                "через 20 с слот доставки сработал (ящик или пропуск рекламы)");
        }

        // ───────────────────────────── 11.4: конец кампании ─────────────────────────────

        [Test]
        public void CampaignCompletedAfterZone144()
        {
            var s = NewDebug(144);
            Assert.AreEqual(144, TestData.Balance.ZoneCount);
            Assert.AreEqual("GachaCrusher", s.Layout.Exit.RewardId);
            s.DebugGiveZoneCards();
            foreach (var sh in s.Layout.Shafts)
            {
                ClearAndActivate(s, sh);
                Assert.IsTrue(s.Mine.Objects[sh.Ordinal].Gen.Automated, sh.TypeId + ": карта нужного уровня есть");
            }
            foreach (var g in s.Layout.Gates) BreakAndOpenGate(s, g);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsTrue(s.CanCompleteZone(out string reason), reason);

            var elixirBefore = s.Player.Elixir;
            int revealsBefore = s.Player.ChestReveals.Count;
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());

            Assert.IsTrue(s.Player.CampaignCompleted, "CampaignCompleted = true после zone144");
            Assert.AreEqual(144, s.Player.HighestCompletedZone);
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.CampaignCompleted), "событие CampaignCompleted");
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.MineCompleted));
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.ZoneEntered), "zone1 не запускается");
            Assert.AreEqual(144, s.Mine.Zone, "новая шахта не создаётся");

            // Награда выхода zone144: GachaCrusher обычным алгоритмом 10.3 (GachaType 0, множители Normal zone144: эликсир ×6, карты ×4).
            Assert.AreEqual(revealsBefore + 1, s.Player.ChestReveals.Count);
            var reveal = s.Player.ChestReveals.Last();
            Assert.AreEqual("GachaCrusher", reveal.GachaId);
            Assert.AreEqual(1500.0, reveal.Elixir.ToDouble(), "floor(250 × 6)");
            Assert.AreEqual(140, reveal.Cards.Sum(c => c.Count), "N = floor(35 × 4 + 0.5)");
            Assert.AreEqual(1500.0, (s.Player.Elixir - elixirBefore).ToDouble(), 1e-9);

            // Повторный переход отклоняется и ничего не выдаёт.
            Assert.IsFalse(s.CanCompleteZone(out _));
            s.Events.Clear();
            string after = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(after, Snapshot(s), "повторный CompleteZone не меняет состояние");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.CampaignCompleted || e.Type == SimEventType.MineCompleted));

            // При следующем запуске (save/load) кампания остаётся завершённой.
            var restored = Restore(after);
            Assert.IsTrue(restored.Player.CampaignCompleted);
            Assert.AreNotEqual(CommandResult.Ok, restored.CompleteZone());
            Assert.AreEqual(144, restored.Player.HighestCompletedZone);
        }
    }
}
