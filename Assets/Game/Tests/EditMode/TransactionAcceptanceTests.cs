using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 6 «Транзакции»: двойной клик покупки; одновременное завершение цели;
    /// повторное открытие сундука/контейнера и повторный opId; прерывание записи на каждом шаге 12.2.
    /// Шаги 12.2, которые уже проверяет SimulationTests.InterruptedSaveKeepsLastValidState
    /// (сбой до записи tmp при существующем main и отказ Replace с успешным запасным путём), здесь не повторяются.
    /// </summary>
    public class TransactionAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;

        static GameSession NewSession(int zone = 1, string slot = SaveSlots.Main) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, 1_000_000L, zone);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Restore(string json) => GameSession.FromState(TestData.Balance, SaveService.DeserializeState(json));

        /// <summary>Полученные копии карты без улучшений: первая копия — уровень 1, остальные — дубликаты (ТЗ 10.1). Состояние не трогает.</summary>
        static int Copies(GameSession s, string cardId) =>
            s.Player.Cards.TryGetValue(cardId, out var cs) && cs.Level > 0 ? 1 + cs.Duplicates : 0;

        static string Describe(ChestResult r) =>
            r.GachaId + " эликсир=" + r.Elixir.ToStorageString() + " карты=" +
            string.Join(",", r.Cards.Select(c => c.CardId + "x" + c.Count));

        static string NewTempDir() => Path.Combine(Path.GetTempPath(), "catminers-tx-" + Guid.NewGuid().ToString("N"));

        static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        // ───────────────────────────── 1. Двойной клик покупки (ТЗ 7.2, 9.3, 10.1, 15.1) ─────────────────────────────

        [Test]
        public void HireDoubleClickAtCapCreatesExactlyOneCat()
        {
            // ТЗ 7.2: «Быстрые повторные нажатия не создают котиков бесплатно»; при отказе по лимиту счётчики не меняются.
            var s = NewSession(1, SaveSlots.Debug);
            for (int i = 0; i < 9; i++) Assert.IsTrue(s.DebugSpawnCat(1), "нет клетки для котика " + i);
            Assert.AreEqual(9, s.OccupiedSlots);
            Assert.AreEqual(10, s.UnitCap(), "BaseUnitCap = 10 (ТЗ 7.1)");
            Assert.AreEqual(2.0, s.HirePrice().ToDouble(), "цена первой покупки 2 (E-05)");
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.Hire(), "первое нажатие");
            string afterFirst = Snapshot(s);
            Assert.AreEqual(CommandResult.CapReached, s.Hire(), "второе нажатие в том же тике упирается в лимит");
            Assert.AreEqual(afterFirst, Snapshot(s), "отклонённое нажатие не меняет состояние");

            Assert.AreEqual(10, s.Mine.Cats.Count, "ровно один новый котик");
            Assert.AreEqual(1, s.Mine.HireCount, "счётчик покупок вырос один раз");
            Assert.AreEqual(23.0, s.Mine.Gold.ToDouble(), "списана одна цена: 25 − 2");
            Assert.AreEqual(8.0, s.HirePrice().ToDouble(), "следующая цена — как после одной покупки (E-05: 2; 8; …)");
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.CatSpawned), "одно событие появления котика");
        }

        [Test]
        public void ForgeUpgradeDoubleClickChargesOnce()
        {
            // ТЗ 15.1: стоимость проверяется и списывается одним методом; второе нажатие без золота ничего не меняет.
            var s = NewSession();
            int cart = s.Layout.Cart.Ordinal;
            s.Advance(32 * S);
            Assert.AreEqual(73.0, s.Mine.Gold.ToDouble(), "25 + 16 циклов кузницы по 3 золота (E-03)");
            Assert.AreEqual(72.0, s.UpgradeCost(cart, 1).ToDouble(), "цена 1→2 кузницы (E-03)");
            Assert.AreEqual(103.0, s.UpgradeCost(cart, 2).ToDouble(), "цена 2→3 кузницы (E-03)");
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(cart, out int first), "первое нажатие");
            Assert.AreEqual(1, first);
            string afterFirst = Snapshot(s);
            Assert.AreEqual(CommandResult.NotEnoughGold, s.UpgradeGenerator(cart, out int second), "второе нажатие: 1 < 103");
            Assert.AreEqual(0, second);
            Assert.AreEqual(afterFirst, Snapshot(s), "отклонённое нажатие не меняет состояние");

            Assert.AreEqual(2, s.Mine.Objects[cart].Gen.Level, "куплен ровно один уровень");
            Assert.AreEqual(1.0, s.Mine.Gold.ToDouble(), "списано ровно 72");
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.GeneratorUpgraded));
        }

        [Test]
        public void CardUpgradeDoubleClickChargesOnce()
        {
            // ТЗ 10.1 / E-14: Common 1→2 списывает 2 дубликата и 50 эликсира; второе нажатие без дубликатов отклоняется.
            var s = NewSession(3, SaveSlots.Debug);
            var citrine = s.Layout.Shafts.First(x => x.TypeId == "citrine");
            s.DebugCompleteTarget(citrine.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            // Награда scg_m003_s02 (JSON): ca001 × 3 и 35 эликсира, GachaType 2 — без множителей зоны.
            Assert.AreEqual(1, s.CardLevel("ca001"));
            Assert.AreEqual(2, s.Player.Cards["ca001"].Duplicates);
            Assert.AreEqual(35.0, s.Player.Elixir.ToDouble());
            s.DebugAddElixir(GameNumber.FromDouble(100));
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard("ca001"), "первое нажатие");
            string afterFirst = Snapshot(s);
            Assert.AreEqual(CommandResult.NotEnoughDuplicates, s.UpgradeCard("ca001"), "второе нажатие: для 2→3 нужно 5 дубликатов");
            Assert.AreEqual(afterFirst, Snapshot(s), "отклонённое нажатие не меняет состояние");

            Assert.AreEqual(2, s.CardLevel("ca001"), "уровень вырос один раз");
            Assert.AreEqual(0, s.Player.Cards["ca001"].Duplicates, "списано 2 дубликата один раз");
            Assert.AreEqual(85.0, s.Player.Elixir.ToDouble(), "списано 50 эликсира один раз: 135 − 50");
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.CardUpgraded));
        }

        // ───────────────────────────── 2. Одновременное завершение цели (ТЗ 8.2, 8.3, 15.2) ─────────────────────────────

        [Test]
        public void TwoCatsFinishSameRockInOneTickRewardOnce()
        {
            // ТЗ 8.2: у цели одно общее HP и одна награда. ТЗ 8.3: одновременные удары не дают повторного завершения или награды.
            var s = NewSession(2);
            int ord = OrdAt(s, 1, 6);
            Assert.GreaterOrEqual(ord, 0, "камень в (1, 6) zone2");
            var rock = s.Layout.Objects[ord];
            Assert.AreEqual("rockonboardingmine2", rock.TypeId);
            Assert.AreEqual(1, rock.Level);
            Assert.AreEqual(16.0, s.Mine.Objects[ord].Hp.ToDouble(), "HP камня L1 (ТЗ 8.1)");
            var work = new List<int>();
            s.WorkCells(ord, work);
            Assert.GreaterOrEqual(work.Count, 2, "у камня не меньше двух рабочих клеток");

            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            Assert.Less(a.Id, b.Id);
            Assert.AreEqual(rock.Index, s.DropCat(a.Id, 1.5, 6.5).TargetIndex, "котик A назначен на камень");
            s.Advance(S);
            Assert.AreEqual(15.0, s.Mine.Objects[ord].Hp.ToDouble(), "удар A в t = 1 с");
            Assert.AreEqual(rock.Index, s.DropCat(b.Id, 1.5, 6.5).TargetIndex, "котик B назначен на тот же камень");
            Assert.AreNotEqual(a.Cell, b.Cell);
            // Удары A и B в t = 2…8 с: 15 − 14 = 1. Следующие удары обоих — в одном тике t = 9 с.
            s.Advance(8 * S - 1);
            Assert.AreEqual(1.0, s.Mine.Objects[ord].Hp.ToDouble(), 1e-12);
            Assert.AreEqual(rock.Index, a.TargetIndex);
            Assert.AreEqual(rock.Index, b.TargetIndex);

            var goldBefore = s.Mine.Gold;
            s.Events.Clear();
            s.Advance(1);

            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[ord].Status);
            Assert.IsFalse(s.Mine.Objects[ord].Hp.IsPositive);
            Assert.AreEqual(1L, s.Player.RewardCycle["rockonboardingmine2"], "счётчик RewardCycle вырос на 1");
            Assert.AreEqual(1, s.Mine.RocksDestroyed);
            var destroyed = s.Events.Where(e => e.Type == SimEventType.RockDestroyed).ToList();
            Assert.AreEqual(1, destroyed.Count, "одно завершение");
            Assert.AreEqual(rock.Index, destroyed[0].ObjectIndex);
            var rewards = s.Events.Where(e => e.Type == SimEventType.RewardGranted || e.Type == SimEventType.ChestOpened).ToList();
            Assert.AreEqual(1, rewards.Count, "одна награда");
            Assert.AreEqual(destroyed[0].OpId, rewards[0].OpId, "награда — в той же транзакции завершения");
            // RewardCycle[0] у rockonboardingmine2 = (0, Core); E-09: floor(105 с × 1.5/с) = 157.
            Assert.AreEqual(157.0, rewards[0].Amount.ToDouble(), "E-09: золото камня L1 при доходе 1.5/с");
            Assert.AreEqual(157.0, (s.Mine.Gold - goldBefore).ToDouble(), "золото начислено один раз (кузница в t = 9 с не платит)");
            Assert.AreEqual(-1, a.TargetIndex, "A в Idle (ТЗ 8.3)");
            Assert.AreEqual(-1, b.TargetIndex, "B в Idle (ТЗ 8.3)");

            s.Events.Clear();
            s.Advance(10 * S);
            Assert.AreEqual(0, s.Events.Count(e => e.Type == SimEventType.RockDestroyed || e.Type == SimEventType.RewardGranted ||
                                                  e.Type == SimEventType.ChestOpened || (e.Type == SimEventType.TargetHit && e.ObjectIndex == rock.Index)),
                "после завершения по камню больше ничего не происходит");
            Assert.AreEqual(1L, s.Player.RewardCycle["rockonboardingmine2"]);
            Assert.AreEqual(1, s.Mine.RocksDestroyed);
            Assert.AreEqual(-1, a.TargetIndex, "новой цели котик себе не выбирает (ТЗ 7.3)");
            Assert.AreEqual(-1, b.TargetIndex, "новой цели котик себе не выбирает (ТЗ 7.3)");
        }

        [Test]
        public void DynamiteAndPendingCatHitCompleteRockOnce()
        {
            // ТЗ 10.6: удар динамита — один удар; завершение и награда той же транзакцией. Удар котика в тот же момент не завершает цель повторно.
            var s = NewSession(2, SaveSlots.Debug);
            int ord = OrdAt(s, 1, 6);
            var rock = s.Layout.Objects[ord];
            Assert.AreEqual("rockonboardingmine2", rock.TypeId);
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var cat = s.Mine.Cats[0];
            Assert.AreEqual(rock.Index, s.DropCat(cat.Id, 1.5, 6.5).TargetIndex);
            s.Advance(2 * S - 1);
            Assert.AreEqual(15.0, s.Mine.Objects[ord].Hp.ToDouble(), "один удар в t = 1 с, следующий — через 1 мкс");
            s.DebugAddDynamite(1);
            var goldBefore = s.Mine.Gold;
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.UseDynamite(1.5, 6.5));
            Assert.AreEqual(0, s.Player.Dynamite);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[ord].Status, "1050 урона (E-16) хватает на 15 HP");
            Assert.AreEqual(1L, s.Player.RewardCycle["rockonboardingmine2"]);
            Assert.AreEqual(157.0, (s.Mine.Gold - goldBefore).ToDouble(), "одна награда 157 (E-09)");
            Assert.AreEqual(-1, cat.TargetIndex, "котик освобождён в Idle");

            s.Advance(1);
            s.Advance(5 * S);
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.RockDestroyed), "одно завершение");
            Assert.AreEqual(1, s.Events.Count(e => e.Type == SimEventType.RewardGranted), "одна награда");
            Assert.AreEqual(0, s.Events.Count(e => e.Type == SimEventType.TargetHit && e.CatId == cat.Id), "отложенный удар котика не прошёл");
            Assert.AreEqual(1L, s.Player.RewardCycle["rockonboardingmine2"]);
            Assert.AreEqual(1, s.Mine.RocksDestroyed);
        }

        // ───────────────────────────── 3. Повторное открытие сундука и контейнера (ТЗ 10.3–10.5, 10.7, 11.2) ─────────────────────────────

        [Test]
        public void FreeCatContainerSecondOpenIsRejected()
        {
            // ТЗ 10.4: тап открывает контейнер, котик появляется в той же клетке. Повторный тап и перезапуск ничего не выдают.
            var s = NewSession();
            s.Advance(2400 * S);
            var box = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            string afterFirst = Snapshot(s);

            Assert.AreNotEqual(CommandResult.Ok, s.OpenContainer(box.Id), "повторный тап по открытому контейнеру");
            Assert.AreEqual(afterFirst, Snapshot(s), "повторный тап не меняет состояние");
            Assert.AreEqual(1, s.Mine.Cats.Count, "один котик");
            Assert.AreEqual(0, s.Mine.Containers.Count);
            Assert.AreEqual(1, s.OccupiedSlots);

            var restored = Restore(afterFirst);
            Assert.AreNotEqual(CommandResult.Ok, restored.OpenContainer(box.Id), "после перезапуска контейнер не открывается снова");
            Assert.AreEqual(afterFirst, Snapshot(restored));
            Assert.AreEqual(1, restored.Mine.Cats.Count);
        }

        [Test]
        public void DeliveryBoxSecondOpenIsRejected()
        {
            // ТЗ 10.5: тап открывает ящик одной транзакцией; содержимое начисляется один раз.
            var s = NewSession(4, SaveSlots.Debug);
            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.IsTrue(s.Mine.DeliveriesEnabled);
            ContainerState box = null;
            for (int i = 0; i < 600 && box == null; i++)
            {
                s.Advance(S);
                box = s.Mine.Containers.FirstOrDefault(c => c.Kind == ContainerKind.Delivery);
            }
            Assert.IsNotNull(box, "доставка появилась");

            int cats = s.Mine.Cats.Count, slots = s.OccupiedSlots, dynamite = s.Player.Dynamite;
            int claims = s.Player.DeliveryHistory.ClaimTimes.Count;
            var gold = s.Mine.Gold;
            var elixir = s.Player.Elixir;

            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            string afterFirst = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.OpenContainer(box.Id), "повторный тап по открытому ящику");
            Assert.AreEqual(afterFirst, Snapshot(s), "повторный тап не меняет состояние");
            Assert.AreNotEqual(CommandResult.Ok, Restore(afterFirst).OpenContainer(box.Id), "после перезапуска ящик не открывается снова");

            Assert.AreEqual(0, s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery));
            Assert.AreEqual(claims + 1, s.Player.DeliveryHistory.ClaimTimes.Count, "счётчик полученных доставок вырос один раз");
            string what = "ящик " + box.DeliveryId + " (" + box.RewardType + ", " + box.DetailedType + ")";
            if (box.RewardType == RewardTypes.Miner)
            {
                Assert.AreEqual(cats + 1, s.Mine.Cats.Count, what + ": один котик");
                Assert.AreEqual(slots, s.OccupiedSlots, what + ": место было зарезервировано ящиком (ТЗ 7.2)");
            }
            else
            {
                Assert.AreEqual(cats, s.Mine.Cats.Count, what);
            }
            if (box.RewardType == RewardTypes.Currency && box.DetailedType == "Core")
                Assert.AreEqual(box.Amount.ToDouble(), (s.Mine.Gold - gold).ToDouble(), what + ": золото начислено один раз");
            else
                Assert.AreEqual(gold, s.Mine.Gold, what);
            if (box.RewardType == RewardTypes.Currency && box.DetailedType == "Soft")
                Assert.AreEqual(box.Amount.ToDouble(), (s.Player.Elixir - elixir).ToDouble(), what + ": эликсир начислен один раз");
            else
                Assert.AreEqual(elixir, s.Player.Elixir, what);
            Assert.AreEqual(box.RewardType == RewardTypes.Spell ? dynamite + 1 : dynamite, s.Player.Dynamite, what + ": динамит");
        }

        [Test]
        public void ChestResultIsFixedAcrossRestartAndRevealDoesNotCreditAgain()
        {
            // ТЗ 10.3 п. 8: результат определяется одной транзакцией и сохраняется до анимации;
            // закрытие приложения во время открытия не даёт повторного броска. Очередь — в PlayerProgressState.
            var s = NewSession(6, SaveSlots.Debug);
            var shaft = s.Layout.Shafts.First(x => x.RewardId == "GachaIron");
            Assert.AreEqual("topaz", shaft.TypeId, "zone6: s:topaz:5:GachaIron:ca005:4");
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(0, s.Player.ChestReveals.Count);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));

            Assert.AreEqual(1, s.Player.ChestReveals.Count, "результат сундука определён и стоит в очереди показа");
            var chest = s.Player.ChestReveals[0];
            Assert.AreEqual("GachaIron", chest.GachaId);
            // E-11: N = floor(14 × 0.4 + 0.5) = 6 случайных карт (GachaCardsMultNormal zone6 = 0.4).
            Assert.AreEqual(6, chest.Cards.Sum(c => c.Count), Describe(chest));
            Assert.AreEqual(chest.Elixir, s.Player.Elixir, "эликсир сундука начислен при определении");
            foreach (var c in chest.Cards) Assert.AreEqual(c.Count, Copies(s, c.CardId), "карты начислены при определении: " + c.CardId);

            // Приложение закрыто во время анимации открытия: перезапуск из сохранённого снимка.
            string closed = Snapshot(s);
            var r = Restore(closed);
            Assert.AreEqual(closed, Snapshot(r));
            var shown = r.TakeChestReveal();
            Assert.IsNotNull(shown, "сундук остался в очереди после перезапуска");
            Assert.AreEqual(Describe(chest), Describe(shown), "содержимое не перебрасывается");
            Assert.AreEqual(s.Player.Elixir, r.Player.Elixir, "показ не начисляет эликсир повторно");
            foreach (var c in chest.Cards) Assert.AreEqual(Copies(s, c.CardId), Copies(r, c.CardId), "показ не начисляет карту повторно: " + c.CardId);
            Assert.AreEqual(s.Player.Rng.Gacha.State, r.Player.Rng.Gacha.State, "генератор сундуков не сдвинулся — повторного броска нет");
            Assert.IsNull(r.TakeChestReveal(), "второй тап: очередь пуста");

            string beforeRepeat = Snapshot(r);
            Assert.AreNotEqual(CommandResult.Ok, r.ActivateShaft(shaft.Ordinal), "повторная активация после перезапуска");
            Assert.AreEqual(beforeRepeat, Snapshot(r), "второго сундука нет");
        }

        [Test]
        public void FreeChestWithoutStockIsRejectedAndDoubleClaimCreditsOnce()
        {
            // ТЗ 10.7 / E-17: первый бесплатный сундук scg_free_01 — ca002, ca015, 19 эликсира; без запаса открыть нельзя.
            var s = NewSession(4, SaveSlots.Debug);
            Assert.IsFalse(s.Player.FreeChest.Enabled);
            string before = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.ClaimFreeChest(), "источник ещё не включён");
            Assert.AreEqual(before, Snapshot(s));

            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.IsTrue(s.Player.FreeChest.Enabled);
            Assert.AreEqual(0, s.Player.FreeChest.Stock, "при включении сундук сразу не выдаётся");
            before = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.ClaimFreeChest(), "запас 0");
            Assert.AreEqual(before, Snapshot(s), "отказ при запасе 0 не меняет состояние (индекс цикла не расходуется)");

            s.Advance(4 * 3600 * S);
            Assert.AreEqual(1, s.Player.FreeChest.Stock);
            Assert.AreEqual(0, s.Player.FreeChest.NextIndex);
            var elixir = s.Player.Elixir;
            int ca002 = Copies(s, "ca002"), ca015 = Copies(s, "ca015");
            int reveals = s.Player.ChestReveals.Count;

            Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest(), "первое нажатие");
            string afterFirst = Snapshot(s);
            Assert.AreNotEqual(CommandResult.Ok, s.ClaimFreeChest(), "второе нажатие при запасе 0");
            Assert.AreEqual(afterFirst, Snapshot(s), "второе нажатие ничего не меняет");

            Assert.AreEqual(0, s.Player.FreeChest.Stock);
            Assert.AreEqual(1, s.Player.FreeChest.NextIndex, "индекс GachaIds израсходован один раз");
            Assert.AreEqual(19.0, (s.Player.Elixir - elixir).ToDouble(), "scg_free_01: 19 эликсира один раз");
            Assert.AreEqual(ca002 + 1, Copies(s, "ca002"), "scg_free_01: ca002 × 1");
            Assert.AreEqual(ca015 + 1, Copies(s, "ca015"), "scg_free_01: ca015 × 1");
            Assert.AreEqual(reveals + 1, s.Player.ChestReveals.Count, "один сундук в очереди показа");
            Assert.AreEqual("scg_free_01", s.Player.ChestReveals[s.Player.ChestReveals.Count - 1].GachaId);
        }

        [Test]
        public void CompleteZoneOnceAcrossDoubleTapInterruptedSaveAndReload()
        {
            // ТЗ 11.2: CompleteZone — одна транзакция и одна запись. При перезапуске до сохранения игрок остаётся
            // в старой зоне с разрушенным выходом без награды. После сохранения — уже новая зона, повтор игнорируется.
            string dir = NewTempDir();
            try
            {
                var s = NewSession(2, SaveSlots.Debug);
                var shaft = s.Layout.Shafts.Single();
                s.DebugCompleteTarget(shaft.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
                s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
                Assert.IsTrue(s.CanCompleteZone(out _));
                new SaveService(dir).Save(s.State, 2_000_000L);
                string before = Snapshot(s);
                double elixirBefore = s.Player.Elixir.ToDouble();
                int ca001 = Copies(s, "ca001"), ca002 = Copies(s, "ca002");

                Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
                string after = Snapshot(s);
                Assert.AreNotEqual(CommandResult.Ok, s.CompleteZone(), "двойной тап «Следующая шахта»");
                Assert.AreEqual(after, Snapshot(s), "второй тап ничего не меняет");
                Assert.AreEqual(3, s.Mine.Zone);
                // E-13: награда выхода zone2 по нижней границе — ca001 × 1, ca002 × 1, 30 эликсира.
                Assert.AreEqual(elixirBefore + 30, s.Player.Elixir.ToDouble(), "30 эликсира один раз");
                Assert.AreEqual(ca001 + 1, Copies(s, "ca001"));
                Assert.AreEqual(ca002 + 1, Copies(s, "ca002"));

                // Сбой посреди записи tmp: перезапуск — старая зона, выход разрушен, награды нет.
                var crashing = new SaveService(dir, new CrashingFs { CrashOn = "write", TornWrite = true });
                Assert.Catch<IOException>(() => crashing.Save(s.State, 2_001_000L));
                var loaded = new SaveService(dir).Load(SaveSlots.Debug, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status);
                Assert.AreEqual(before, SaveService.SerializeState(loaded.State), "загружено состояние до перехода");
                var r = GameSession.FromState(TestData.Balance, loaded.State);
                Assert.AreEqual(2, r.Mine.Zone);
                Assert.AreEqual(ObjectStatus.Broken, r.Mine.Objects[r.Layout.Exit.Ordinal].Status);
                Assert.AreEqual(elixirBefore, r.Player.Elixir.ToDouble(), "награды выхода нет");
                Assert.AreEqual(0, r.Player.HighestCompletedZone);

                // Игрок повторяет переход — результат совпадает с первой попыткой: награда ровно одна.
                Assert.AreEqual(CommandResult.Ok, r.CompleteZone());
                Assert.AreEqual(after, Snapshot(r));

                // Запись прошла: после перезапуска уже zone3, повторный переход отклоняется.
                new SaveService(dir).Save(r.State, 2_002_000L);
                var reloaded = new SaveService(dir).Load(SaveSlots.Debug, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, reloaded.Status);
                var q = GameSession.FromState(TestData.Balance, reloaded.State);
                Assert.AreEqual(3, q.Mine.Zone);
                Assert.AreNotEqual(CommandResult.Ok, q.CompleteZone(), "повторный переход после перезапуска");
                Assert.AreEqual(after, Snapshot(q), "награда выхода не выдана второй раз");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // ───────────────────────────── 4. ID операций (ТЗ 11.1, 11.2, 16) ─────────────────────────────

        /// <summary>Следит за opId транзакций в событиях нескольких сессий (до и после перезапуска).</summary>
        sealed class OpTracker
        {
            public long Last;
            public readonly List<SimEvent> All = new List<SimEvent>();

            public void Committed(GameSession s, Func<CommandResult> command, string what)
            {
                int from = s.Events.Count;
                Assert.AreEqual(CommandResult.Ok, command(), what);
                var fresh = s.Events.Skip(from).ToList();
                var ops = fresh.Where(e => e.OpId != 0).Select(e => e.OpId).ToList();
                Assert.IsNotEmpty(ops, what + ": транзакция без opId");
                Assert.Greater(ops.Min(), Last, what + ": opId новой транзакции больше всех предыдущих");
                Last = ops.Max();
                All.AddRange(fresh);
            }

            public void Rejected(GameSession s, Func<CommandResult> command, string what)
            {
                string before = Snapshot(s);
                int from = s.Events.Count;
                Assert.AreNotEqual(CommandResult.Ok, command(), what);
                Assert.IsFalse(s.Events.Skip(from).Any(e => e.OpId != 0), what + ": отклонённая команда не публикует транзакцию");
                Assert.AreEqual(before, Snapshot(s), what + ": отклонённая команда не меняет состояние");
            }
        }

        static Func<CommandResult> Act(Action action) => () =>
        {
            action();
            return CommandResult.Ok;
        };

        [Test]
        public void OperationIdsAreUniqueIncreasingAndSurviveReload()
        {
            // ТЗ 16: события RockDestroyed, MineShaftActivated, CheckpointOpened, MineCompleted — с ID зоны и уникальным ID операции.
            // ТЗ 11.1/11.2: открытие ворот и CompleteZone — транзакции с opId; повтор ничего не выдаёт.
            var s = NewSession(5, SaveSlots.Debug);
            var t = new OpTracker();
            t.Committed(s, s.Hire, "наём 1");
            t.Committed(s, s.Hire, "наём 2");
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            t.Committed(s, () => s.DropCat(a.Id, s.Layout.Col(b.Cell) + 0.5, s.Layout.Row(b.Cell) + 0.5).Result, "объединение");
            foreach (var rock in s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock).Take(3).ToList())
                t.Committed(s, Act(() => s.DebugCompleteTarget(rock.Ordinal)), "камень " + rock.Ordinal);

            var gate = s.Layout.Gates[0];
            t.Committed(s, Act(() => s.DebugCompleteTarget(gate.Ordinal)), "ворота разрушены");
            t.Committed(s, () => s.OpenGate(gate.Ordinal), "ворота открыты");
            t.Rejected(s, () => s.OpenGate(gate.Ordinal), "повторное открытие ворот");

            var shafts = s.Layout.Shafts.ToList();
            foreach (var sh in shafts)
            {
                t.Committed(s, Act(() => s.DebugCompleteTarget(sh.Ordinal)), "шахта расчищена " + sh.TypeId);
                t.Committed(s, () => s.ActivateShaft(sh.Ordinal), "шахта активирована " + sh.TypeId);
                t.Rejected(s, () => s.ActivateShaft(sh.Ordinal), "повторная активация " + sh.TypeId);
            }
            t.Committed(s, Act(() => s.DebugCompleteTarget(s.Layout.Exit.Ordinal)), "выход разрушен");
            t.Committed(s, s.CompleteZone, "переход zone5 → zone6");
            Assert.AreEqual(6, s.Mine.Zone);
            t.Rejected(s, s.CompleteZone, "повторный переход");

            // Перезапуск: ID операций продолжаются, а не начинаются заново.
            var r = Restore(Snapshot(s));
            t.Committed(r, r.Hire, "наём после перезапуска");
            var rock6 = r.Layout.Objects.First(o => o.Kind == ObjectKind.Rock);
            t.Committed(r, Act(() => r.DebugCompleteTarget(rock6.Ordinal)), "камень zone6 после перезапуска");

            var keyTypes = new HashSet<SimEventType>
            {
                SimEventType.RockDestroyed, SimEventType.MineShaftActivated, SimEventType.CheckpointOpened, SimEventType.MineCompleted,
            };
            var key = t.All.Where(e => keyTypes.Contains(e.Type)).ToList();
            Assert.IsTrue(key.All(e => e.OpId > 0), "у каждого события есть ID операции");
            Assert.AreEqual(key.Count, key.Select(e => e.OpId).Distinct().Count(), "ID операции уникален: " + string.Join("; ", key));

            var destroyed = key.Where(e => e.Type == SimEventType.RockDestroyed).ToList();
            Assert.AreEqual(4, destroyed.Count);
            Assert.AreEqual(new[] { 5, 5, 5, 6 }, destroyed.Select(e => e.Zone).ToArray(), "ID зоны у RockDestroyed");
            var activated = key.Where(e => e.Type == SimEventType.MineShaftActivated).ToList();
            Assert.AreEqual(shafts.Count, activated.Count);
            Assert.IsTrue(activated.All(e => e.Zone == 5), "ID зоны у MineShaftActivated");
            var opened = key.Single(e => e.Type == SimEventType.CheckpointOpened);
            Assert.AreEqual(5, opened.Zone);
            Assert.AreEqual(gate.Index, opened.ObjectIndex);
            var completed = key.Single(e => e.Type == SimEventType.MineCompleted);
            Assert.AreEqual(5, completed.Zone, "MineCompleted несёт ID завершённой зоны");
        }

        // ───────────────────────────── 5. Прерывание записи на каждом шаге 12.2 ─────────────────────────────

        /// <summary>
        /// Файловая система, которая «роняет процесс» на заданном шаге записи (ТЗ 12.2).
        /// После сбоя все изменяющие операции тоже падают: процесс остановлен, на диск больше ничего не попадает.
        /// Загрузка после сбоя идёт через новый SaveService с настоящей файловой системой — это перезапуск.
        /// </summary>
        sealed class CrashingFs : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            /// <summary>Шаг сбоя: write | replace | copy | delete | move.</summary>
            public string CrashOn;
            /// <summary>false — сбой до операции; true — операция выполнена, сбой сразу после неё.</summary>
            public bool CrashAfter;
            /// <summary>Сбой посреди записи tmp: на диске остаётся половина байтов.</summary>
            public bool TornWrite;
            /// <summary>File.Replace недоступен: бросает до каких-либо изменений, SaveService идёт запасным путём.</summary>
            public bool ReplaceUnsupported;
            /// <summary>File.Replace успел превратить основной файл в .bak, но не переименовал tmp (ERROR_UNABLE_TO_MOVE_REPLACEMENT).</summary>
            public bool ReplacePartial;
            bool m_Dead;

            void Step(string op, Action action)
            {
                if (m_Dead) throw new IOException("процесс уже остановлен: " + op);
                bool hit = op == CrashOn;
                if (hit && !CrashAfter)
                {
                    m_Dead = true;
                    throw new IOException("сбой перед шагом " + op);
                }
                action();
                if (hit && CrashAfter)
                {
                    m_Dead = true;
                    throw new IOException("сбой после шага " + op);
                }
            }

            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);

            public void WriteAllBytesDurable(string p, byte[] d)
            {
                if (TornWrite && CrashOn == "write" && !m_Dead)
                {
                    m_Dead = true;
                    var part = new byte[d.Length / 2];
                    Array.Copy(d, part, part.Length);
                    m_Real.WriteAllBytesDurable(p, part);
                    throw new IOException("сбой посреди записи tmp");
                }
                Step("write", () => m_Real.WriteAllBytesDurable(p, d));
            }

            public void Replace(string s, string d, string b)
            {
                if (!m_Dead && ReplaceUnsupported) throw new IOException("File.Replace недоступен");
                if (!m_Dead && ReplacePartial)
                {
                    m_Real.Copy(d, b);
                    m_Real.Delete(d);
                    throw new IOException("Replace прерван: основной файл уже стал .bak, tmp не переименован");
                }
                Step("replace", () => m_Real.Replace(s, d, b));
            }

            public void Move(string s, string d) => Step("move", () => m_Real.Move(s, d));
            public void Copy(string s, string d) => Step("copy", () => m_Real.Copy(s, d));
            public void Delete(string p) => Step("delete", () => m_Real.Delete(p));
            public void CreateDirectory(string p) => Step("mkdir", () => m_Real.CreateDirectory(p));
        }

        static CrashingFs CrashScenario(string scenario)
        {
            switch (scenario)
            {
                case "first-torn-tmp":
                case "torn-tmp": return new CrashingFs { CrashOn = "write", TornWrite = true };
                case "first-crash-before-move": return new CrashingFs { CrashOn = "move" };
                case "crash-before-replace": return new CrashingFs { CrashOn = "replace" };
                case "crash-after-replace": return new CrashingFs { CrashOn = "replace", CrashAfter = true };
                case "replace-partial": return new CrashingFs { ReplacePartial = true };
                case "fallback-crash-before-copy": return new CrashingFs { ReplaceUnsupported = true, CrashOn = "copy" };
                case "fallback-crash-before-delete": return new CrashingFs { ReplaceUnsupported = true, CrashOn = "delete" };
                case "fallback-crash-before-move": return new CrashingFs { ReplaceUnsupported = true, CrashOn = "move" };
                case "fallback-crash-after-move": return new CrashingFs { ReplaceUnsupported = true, CrashOn = "move", CrashAfter = true };
                default: throw new ArgumentException(scenario);
            }
        }

        /// <summary>Транзакция, которую нельзя потерять или повторить (ТЗ 12.2): объединение, активация шахты с сундуком, покупка.</summary>
        static void PlayTransaction(GameSession s, int draggedId, int receiverId, int shaftOrd)
        {
            var receiver = s.FindCat(receiverId);
            var drop = s.DropCat(draggedId, s.Layout.Col(receiver.Cell) + 0.5, s.Layout.Row(receiver.Cell) + 0.5);
            Assert.IsTrue(drop.Merged, "объединение");
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaftOrd), "активация шахты");
            Assert.AreEqual(CommandResult.Ok, s.Hire(), "покупка");
        }

        /// <param name="scenario">Шаг 12.2 и момент сбоя. Префикс first- — самая первая запись слота (основного файла ещё нет).</param>
        /// <param name="expected">Что обязана вернуть загрузка по порядку main → tmp → bak: old — последнее записанное, new — новое, none — валидного файла нет.</param>
        [TestCase("first-torn-tmp", "none")]
        [TestCase("first-crash-before-move", "new")]
        [TestCase("torn-tmp", "old")]
        [TestCase("crash-before-replace", "old")]
        [TestCase("crash-after-replace", "new")]
        [TestCase("replace-partial", "new")]
        [TestCase("fallback-crash-before-copy", "old")]
        [TestCase("fallback-crash-before-delete", "old")]
        [TestCase("fallback-crash-before-move", "new")]
        [TestCase("fallback-crash-after-move", "new")]
        public void InterruptedSaveStepNeverDuplicatesCatsOrRewards(string scenario, string expected)
        {
            string dir = NewTempDir();
            try
            {
                bool first = scenario.StartsWith("first-", StringComparison.Ordinal);
                var s = NewSession(3, SaveSlots.Debug);
                Assert.AreEqual(CommandResult.Ok, s.Hire());
                if (!first) new SaveService(dir).Save(s.State, 2_000_000L);   // более старая запись: после следующей станет .bak
                Assert.AreEqual(CommandResult.Ok, s.Hire());
                s.DebugAddGold(GameNumber.FromDouble(100));
                var citrine = s.Layout.Shafts.First(x => x.TypeId == "citrine");
                s.DebugCompleteTarget(citrine.Ordinal);
                if (!first) new SaveService(dir).Save(s.State, 2_001_000L);
                string old = Snapshot(s);
                int draggedId = s.Mine.Cats[0].Id, receiverId = s.Mine.Cats[1].Id;

                PlayTransaction(s, draggedId, receiverId, citrine.Ordinal);
                string fresh = Snapshot(s);
                Assert.AreEqual(2, s.Mine.Cats.Count, "после транзакции: результат объединения и купленный котик");
                Assert.AreEqual(1, s.Player.ChestReveals.Count, "после транзакции: один сундук шахты");

                var fs = CrashScenario(scenario);
                Assert.Catch<IOException>(() => new SaveService(dir, fs).Save(s.State, 2_002_000L), "запись прервана");

                var loaded = new SaveService(dir).Load(SaveSlots.Debug, TestData.Balance.Sha256);
                if (expected == "none")
                {
                    Assert.IsNull(loaded.State, scenario + ": недописанный файл не принимается");
                    Assert.AreEqual(LoadStatus.Corrupted, loaded.Status, scenario);
                    Assert.IsTrue(Directory.GetFiles(dir).Any(f => f.Contains(".corrupt-")), scenario + ": повреждённый файл переименован, не удалён");
                    return;
                }
                Assert.IsNotNull(loaded.State, scenario + ": " + loaded.Status + " " + loaded.Message);
                string json = SaveService.SerializeState(loaded.State);
                Assert.AreEqual(expected == "old" ? old : fresh, json, scenario + ": загружено не то состояние (порядок main → tmp → bak)");

                // Игрок после перезапуска повторяет действия: итог всегда ровно одна транзакция.
                var r = GameSession.FromState(TestData.Balance, loaded.State);
                if (expected == "old")
                {
                    PlayTransaction(r, draggedId, receiverId, citrine.Ordinal);
                }
                else
                {
                    Assert.AreEqual(CommandResult.NotFound, r.DropCat(draggedId, 3.5, 9.5).Result, scenario + ": перетаскиваемый котик уже объединён");
                    Assert.AreNotEqual(CommandResult.Ok, r.ActivateShaft(citrine.Ordinal), scenario + ": шахта уже активирована");
                }
                Assert.AreEqual(fresh, Snapshot(r), scenario + ": итог отличается от одной транзакции");
                Assert.AreEqual(2, r.Mine.Cats.Count, scenario + ": нет дубликатов котиков");
                CollectionAssert.AreEquivalent(new[] { 1, 2 }, r.Mine.Cats.Select(c => c.Level).ToArray(), scenario + ": исходные котики не вернулись рядом с результатом (ТЗ 7.4)");
                Assert.AreEqual(1, r.Player.ChestReveals.Count, scenario + ": нет дубликатов наград");
                Assert.AreEqual(35.0, r.Player.Elixir.ToDouble(), scenario + ": scg_m003_s02 — 35 эликсира один раз");
                Assert.AreEqual(3, Copies(r, "ca001"), scenario + ": scg_m003_s02 — ca001 × 3 один раз");
            }
            finally
            {
                DeleteDir(dir);
            }
        }
    }
}
