using System;
using System.Collections.Generic;
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
    /// ТЗ 17.1, группа 13 «Доставки» (ТЗ 10.5, Приложение C E-15): условия включения, фильтры пула,
    /// пропуск рекламного слота, лимит котиков, одна доставка на поле, фиксация содержимого после перезагрузки.
    /// Дополняет SimulationTests.DeliveriesStartAfterTwoShaftsFromRank4 и NumberAndFormulaTests.E15_DeliveryIntervalAndQuantity.
    /// «Слот» — момент окончания таймера доставки: появляется ящик или пропускается рекламная запись.
    /// </summary>
    public class DeliveryAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;
        const long Hour = 3600L * S;
        // Окна из BalanceProperties (ТЗ 10.5): DeliveryClaimCountResetSec = 3600, DeliveryMaxDupesResetSec = 14400.
        const long ClaimWindow = 3600L * S;
        const long DupeWindow = 14400L * S;

        static BalanceRepository B => TestData.Balance;

        static GameSession DebugSession(int zone, ulong seed = 12345UL) =>
            GameSession.NewGame(TestData.Balance, seed, SaveSlots.Debug, 1_000_000L, zone);

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession FromJson(string json) => GameSession.FromState(B, SaveService.DeserializeState(json));

        static GameSession Reload(GameSession s) => FromJson(Snapshot(s));

        /// <summary>Единственный ящик доставки на поле или null (SingleOrDefault — заодно проверка «не больше одного»).</summary>
        static ContainerState Box(GameSession s) => s.Mine.Containers.SingleOrDefault(c => c.Kind == ContainerKind.Delivery);

        static int Boxes(GameSession s) => s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery);

        static DeliveryHistory History(GameSession s) => s.Player.DeliveryHistory;

        static int TotalDrops(GameSession s) => History(s).DupeTimes.Values.Sum(l => l.Count);

        static int DropsAt(GameSession s, long t) => History(s).DupeTimes.Values.Sum(l => l.Count(x => x == t));

        static int DropsAfter(GameSession s, long t) => History(s).DupeTimes.Values.Sum(l => l.Count(x => x > t));

        static string[] PoolIds(GameSession s) => s.DeliveryPool().Select(d => d.Id).OrderBy(id => int.Parse(id)).ToArray();

        static string[] Ids(string list) => list.Split(' ');

        static bool IsCat(ContainerState c) => c.RewardType == RewardTypes.Miner;

        static GameNumber Ledger(GameSession s, string key) => s.Player.Ledger.TryGetValue(key, out var v) ? v : GameNumber.Zero;

        static string BoxText(ContainerState c) =>
            "id=" + c.Id + " cell=" + c.Cell + " kind=" + c.Kind + " delivery=" + c.DeliveryId + " rt=" + c.RewardType +
            " dt=" + c.DetailedType + " amount=" + c.Amount + " catLevel=" + c.CatLevel;

        static double Cx(GameSession s, int cell) => s.Layout.Col(cell) + 0.5;

        static double Cy(GameSession s, int cell) => s.Layout.Row(cell) + 0.5;

        /// <summary>F-2(22, 1, 1.02; x) из E-15 — интервал доставки без разброса, секунды.</summary>
        static double Ft2(int x) => 22.0 * Math.Pow(1.02, x - 1) + (x - 1);

        static void ActivateShafts(GameSession s, int count)
        {
            foreach (var sh in s.Layout.Shafts.Take(count))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal), "активация шахты " + sh.TypeId);
            }
        }

        static void BreakAndOpenGate(GameSession s, ZoneObject gate)
        {
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "открытие ворот " + gate.GateOrder);
        }

        /// <summary>Объединять пары котиков одного уровня, пока они есть.</summary>
        static void MergeAllPairs(GameSession s)
        {
            while (true)
            {
                var group = s.Mine.Cats.GroupBy(c => c.Level).FirstOrDefault(g => g.Count() >= 2);
                if (group == null) return;
                var a = group.ElementAt(0);
                var b = group.ElementAt(1);
                Assert.IsTrue(s.DropCat(a.Id, Cx(s, b.Cell), Cy(s, b.Cell)).Merged, "объединение котиков уровня " + b.Level);
            }
        }

        static void AssertNoSlots(GameSession s, string what)
        {
            Assert.AreEqual(0, Boxes(s), what + ": ящиков доставки нет");
            Assert.AreEqual(0, TotalDrops(s), what + ": ни одна запись не выпадала");
            Assert.AreEqual(0, History(s).SkippedAds, what + ": пропусков рекламы нет");
            Assert.AreEqual(0, History(s).ClaimTimes.Count, what + ": полученных нет");
        }

        sealed class Slot
        {
            public long At;
            public ContainerState Box;
            public string SkippedId;
        }

        /// <summary>
        /// Дойти до следующего слота ровно по таймеру доставки: за тик до окончания ничего не разыграно,
        /// в момент окончания — ровно одно выпадение записи: ящик или пропуск рекламы.
        /// </summary>
        static Slot NextSlot(GameSession s)
        {
            Assert.IsTrue(s.Mine.DeliveriesEnabled, "доставки включены");
            Assert.AreEqual(0, Boxes(s), "пока ящик на поле, следующего слота нет");
            long timer = s.Mine.DeliveryTimerTicks;
            Assert.Greater(timer, 0L, "таймер доставки запущен");
            long start = s.Player.PlayerTimeTicks;
            int skipped = History(s).SkippedAds;
            s.Events.Clear();
            s.Advance(timer - 1);
            Assert.AreEqual(0, Boxes(s), "до окончания таймера ящика нет");
            Assert.AreEqual(0, DropsAfter(s, start), "до окончания таймера слот не разыгрывается");
            Assert.AreEqual(skipped, History(s).SkippedAds, "до окончания таймера пропусков нет");
            int expectedCell = s.FindSpawnCell();
            s.Events.Clear();
            s.Advance(1);
            var slot = new Slot { At = s.Player.PlayerTimeTicks, Box = Box(s) };
            var skips = s.Events.Where(e => e.Type == SimEventType.DeliverySkippedAd).Select(e => e.Text).ToList();
            Assert.LessOrEqual(skips.Count, 1, "один слот — не больше одного пропуска");
            slot.SkippedId = skips.FirstOrDefault();
            Assert.IsTrue((slot.Box != null) != (slot.SkippedId != null), "по окончании таймера — либо ящик, либо пропуск рекламы");
            Assert.AreEqual(1, DropsAt(s, slot.At), "один слот — одно выпадение записи");
            bool freeCatSameTick = s.Events.Any(e => e.Type == SimEventType.ContainerSpawned && e.Text == ContainerKind.FreeCat.ToString());
            if (slot.Box != null && !freeCatSameTick)
                Assert.AreEqual(expectedCell, slot.Box.Cell, "ящик появляется в клетке появления (6.6)");
            return slot;
        }

        /// <summary>Идти по слотам, открывая «лишние» ящики, пока на поле не окажется нужный.</summary>
        static ContainerState WaitForBox(GameSession s, Func<ContainerState, bool> wanted, string what)
        {
            for (int i = 0; i < 500; i++)
            {
                var box = Box(s);
                if (box == null)
                {
                    NextSlot(s);
                    continue;
                }
                if (wanted(box)) return box;
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            }
            Assert.Fail("не дождались доставки: " + what);
            return null;
        }

        /// <summary>Таймер после пропуска или получения: F-2(x) × U[0.9; 1.1], x = полученных за последние 3600 с + 1. Возвращает множитель.</summary>
        static double CheckRestartedTimer(GameSession s, List<long> claims, long now, string what)
        {
            int x = claims.Count(t => now - t < ClaimWindow) + 1;
            double expected = Ft2(x);
            double actual = GameSession.TicksToSeconds(s.Mine.DeliveryTimerTicks);
            Assert.That(actual, Is.InRange(0.9 * expected - 1e-6, 1.1 * expected + 1e-6),
                what + ": таймер = F-2(x = " + x + ") = " + expected + " с × [0.9; 1.1]");
            return actual / expected;
        }

        // ───────────────────────────── Условия включения ─────────────────────────────

        [Test]
        public void Enabling_Rank4AndTwoActivatedShafts_FirstSlot20sAfterEnabling()
        {
            var p = B.Props;
            Assert.AreEqual(4, p.DeliveryStartRank);
            Assert.AreEqual(2, p.DeliveryStartMineShaftNum);
            Assert.AreEqual(20.0, p.DeliveryStartupDelaySec);

            // Rank 3 < 4: обе шахты zone3 активированы, но слотов нет и через час.
            var s3 = DebugSession(3);
            Assert.AreEqual(2, s3.Layout.Shafts.Count);
            ActivateShafts(s3, 2);
            Assert.AreEqual(2, s3.Mine.ActivatedShafts);
            Assert.IsFalse(s3.Mine.DeliveriesEnabled, "Rank 3 < DeliveryStartRank");
            s3.Advance(Hour);
            AssertNoSlots(s3, "zone3");

            // Rank 4: расчищенные, но не активированные шахты не считаются.
            var s = DebugSession(4);
            var shafts = s.Layout.Shafts;
            Assert.AreEqual(3, shafts.Count);
            s.DebugCompleteTarget(shafts[0].Ordinal);
            s.DebugCompleteTarget(shafts[1].Ordinal);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shafts[0].Ordinal].Status);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shafts[1].Ordinal].Status);
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "две шахты расчищены, активированных нет");
            s.Advance(120 * S);
            AssertNoSlots(s, "zone4 без активированных шахт");

            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shafts[0].Ordinal));
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "одной активированной шахты мало");
            s.Advance(600 * S);
            AssertNoSlots(s, "zone4 с одной шахтой");

            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shafts[1].Ordinal));
            Assert.IsTrue(s.Mine.DeliveriesEnabled, "Rank 4 и две активированные шахты");
            long enabledAt = s.Player.PlayerTimeTicks;
            // Первый таймер — DeliveryStartupDelaySec от включения; 12 минут до включения не засчитываются.
            var first = NextSlot(s);
            Assert.AreEqual(20 * S, first.At - enabledAt, "первый слот ровно через 20 с после включения");
            Assert.IsNotNull(first.Box, "на ранге 4 рекламных записей нет — первый слот даёт ящик");
            CollectionAssert.Contains(Ids("3 13 15"), first.Box.DeliveryId);
        }

        // ───────────────────────────── Фильтры пула ─────────────────────────────

        [Test]
        public void Pool_RankUnlockCheckPointUnlockAndDynamiteStock()
        {
            // Ожидаемые наборы выписаны из JSON Deliveries: RankUnlock ≤ Rank и CheckPointUnlock ≤ открытых ворот.
            const string upTo15 = "1 2 3 4 5 6 7 8 9 10 11 12 13 14 15";
            var closed = new Dictionary<int, string>
            {
                { 4, "3 13 15" },
                { 5, "1 3 4 6 8 12 13 15" },
                { 6, "1 2 3 4 6 7 8 11 12 13 15" },
                { 7, upTo15 + " 20" },
                { 8, upTo15 + " 19 20" },
                { 9, upTo15 + " 17 19 20" },
                { 10, upTo15 + " 17 19 20" },
            };
            foreach (var kv in closed)
            {
                var s = DebugSession(kv.Key);
                Assert.AreEqual(0, s.Mine.GatesOpened);
                CollectionAssert.AreEqual(Ids(kv.Value), PoolIds(s), "zone" + kv.Key + ": RankUnlock ≤ Rank, ворота закрыты");
            }

            // Рекламные записи остаются в пуле: перенормировка без рекламы запрещена (10.5).
            var s7 = DebugSession(7);
            CollectionAssert.AreEquivalent(Ids("1 2 6 10"), s7.DeliveryPool().Where(d => d.IsAd).Select(d => d.Id), "рекламные записи в пуле ранга 7");

            // zone5: ворота открыты, но записей с CheckPointUnlock = 1 и RankUnlock ≤ 5 нет — пул тот же.
            var s5 = DebugSession(5);
            Assert.AreEqual(1, s5.Layout.Gates.Count);
            BreakAndOpenGate(s5, s5.Layout.Gates[0]);
            Assert.AreEqual(1, s5.Mine.GatesOpened);
            CollectionAssert.AreEqual(Ids(closed[5]), PoolIds(s5), "zone5: открытые ворота не снимают RankUnlock");

            // zone6: после ворот добавляется запись 21 (RankUnlock 6, CheckPointUnlock 1).
            var s6 = DebugSession(6);
            Assert.AreEqual(1, s6.Layout.Gates.Count);
            BreakAndOpenGate(s6, s6.Layout.Gates[0]);
            CollectionAssert.AreEqual(Ids(closed[6] + " 21"), PoolIds(s6), "zone6 после ворот");

            // zone8: после ворот добавляются 18 и 21; 16 (RankUnlock 9) — нет.
            var s8 = DebugSession(8);
            Assert.AreEqual(1, s8.Layout.Gates.Count);
            BreakAndOpenGate(s8, s8.Layout.Gates[0]);
            CollectionAssert.AreEqual(Ids(upTo15 + " 18 19 20 21"), PoolIds(s8), "zone8 после ворот");

            // zone10: двое ворот; записей с CheckPointUnlock > 1 нет, вторые ворота пул не меняют.
            var s10 = DebugSession(10);
            var gates = s10.Layout.Gates.OrderBy(g => g.GateOrder).ToList();
            Assert.AreEqual(2, gates.Count);
            BreakAndOpenGate(s10, gates[0]);
            string all = upTo15 + " 16 17 18 19 20 21";
            CollectionAssert.AreEqual(Ids(all), PoolIds(s10), "zone10 после первых ворот");
            BreakAndOpenGate(s10, gates[1]);
            CollectionAssert.AreEqual(Ids(all), PoolIds(s10), "zone10 после вторых ворот");

            // Динамит: запас меньше 4. Все шесть записей 16–21 доступны по рангу и воротам.
            s10.DebugAddDynamite(3);
            CollectionAssert.AreEqual(Ids(all), PoolIds(s10), "3 заряда < 4 — динамит в пуле");
            s10.DebugAddDynamite(1);
            Assert.AreEqual(GameSession.MaxDynamite, s10.Player.Dynamite);
            Assert.AreEqual(4, s10.Player.Dynamite);
            CollectionAssert.AreEqual(Ids(upTo15), PoolIds(s10), "4 заряда — записей 16–21 в пуле нет");
        }

        [Test]
        public void Pool_MaxDupesCountsDropsInLast14400s()
        {
            Assert.AreEqual(14400.0, B.Props.DeliveryMaxDupesResetSec);
            Assert.AreEqual(8, B.Deliveries["3"].MaxDupes);
            Assert.AreEqual(6, B.Deliveries["13"].MaxDupes);
            Assert.AreEqual(-1, B.Deliveries["15"].MaxDupes);

            // zone4: пул {3, 13, 15}. Ящики открываются сразу; контейнер бесплатного котика не открывается
            // (8 котиков из записи 3 + 1 контейнер = 9 < 10 — фильтр лимита не вмешивается).
            var s = DebugSession(4);
            ActivateShafts(s, 2);
            long enabledAt = s.Player.PlayerTimeTicks;
            var dropTimes = new List<long>();
            var dropIds = new List<string>();
            while (s.Player.PlayerTimeTicks - enabledAt < 3 * Hour)
            {
                var slot = NextSlot(s);
                Assert.IsNull(slot.SkippedId, "на ранге 4 рекламных записей нет");
                string id = slot.Box.DeliveryId;
                CollectionAssert.Contains(Ids("3 13 15"), id, "пул ранга 4");
                dropTimes.Add(slot.At);
                dropIds.Add(id);
                int max = B.Deliveries[id].MaxDupes;
                if (max >= 0)
                {
                    int inWindow = Enumerable.Range(0, dropIds.Count).Count(i => dropIds[i] == id && slot.At - dropTimes[i] < DupeWindow);
                    Assert.LessOrEqual(inWindow, max, "запись " + id + ": не больше MaxDupes выпадений за 14 400 с");
                }
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(slot.Box.Id));

                // Все выпадения пока внутри одного окна 14 400 с: исчерпанная запись не в пуле, неисчерпанная — в пуле.
                var pool = PoolIds(s);
                CollectionAssert.Contains(pool, "15", "MaxDupes = −1 — без ограничения");
                foreach (var rec in new[] { "3", "13" })
                {
                    int used = dropIds.Count(x => x == rec);
                    if (used >= B.Deliveries[rec].MaxDupes)
                        CollectionAssert.DoesNotContain(pool, rec, "запись " + rec + " исчерпала MaxDupes");
                    else if (s.OccupiedSlots < s.UnitCap())
                        CollectionAssert.Contains(pool, rec, "запись " + rec + ": " + used + " из " + B.Deliveries[rec].MaxDupes);
                }
            }
            TestContext.WriteLine("выпадений за 3 ч: 3 — " + dropIds.Count(x => x == "3") + ", 13 — " + dropIds.Count(x => x == "13") +
                                  ", 15 — " + dropIds.Count(x => x == "15"));
            Assert.AreEqual(8, dropIds.Count(x => x == "3"), "за 3 ч запись 3 выпала ровно MaxDupes = 8 раз");
            Assert.AreEqual(6, dropIds.Count(x => x == "13"), "за 3 ч запись 13 выпала ровно MaxDupes = 6 раз");
            Assert.Greater(dropIds.Count(x => x == "15"), 8, "запись 15 без ограничения: дальше идёт только золото");

            // Окно скользящее: запись 3 возвращается, когда её первое выпадение старше 14 400 с.
            long first3 = dropTimes[dropIds.IndexOf("3")];
            if (Box(s) == null) NextSlot(s);
            Assert.IsNotNull(Box(s), "ящик остаётся на поле — слоты не разыгрываются");
            Assert.Less(s.Player.PlayerTimeTicks, first3 + DupeWindow - S);
            s.Advance(first3 + DupeWindow - S - s.Player.PlayerTimeTicks);
            Assert.Less(s.OccupiedSlots, s.UnitCap());
            CollectionAssert.DoesNotContain(PoolIds(s), "3", "за 1 с до конца окна первого выпадения — 8 из 8");
            s.Advance(2 * S);
            CollectionAssert.Contains(PoolIds(s), "3", "первое выпадение старше 14 400 с — в окне 7 из 8");
        }

        // ───────────────────────────── Лимит котиков ─────────────────────────────

        [Test]
        public void Pool_CatRecordsNeedFreeSlot_UnopenedCatContainerCounts()
        {
            var s = DebugSession(4);
            Assert.AreEqual(10, s.UnitCap());
            for (int i = 0; i < 9; i++) Assert.IsTrue(s.DebugSpawnCat(1));
            Assert.AreEqual(9, s.OccupiedSlots);
            CollectionAssert.AreEqual(Ids("3 13 15"), PoolIds(s), "9 из 10 мест — запись с котиком в пуле");

            // Контейнер бесплатного котика (через 40 мин) занимает десятое место.
            s.Advance(2400 * S);
            var free = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(10, s.OccupiedSlots);
            CollectionAssert.AreEqual(Ids("13 15"), PoolIds(s), "неоткрытый контейнер с котиком занимает место — записей с котиками нет");
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(free.Id));
            Assert.AreEqual(10, s.Mine.Cats.Count);
            CollectionAssert.AreEqual(Ids("13 15"), PoolIds(s), "10 котиков из 10");

            // Объединение освобождает место — запись 3 возвращается.
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            Assert.AreEqual(a.Level, b.Level);
            Assert.IsTrue(s.DropCat(a.Id, Cx(s, b.Cell), Cy(s, b.Cell)).Merged);
            Assert.AreEqual(9, s.OccupiedSlots);
            CollectionAssert.AreEqual(Ids("3 13 15"), PoolIds(s), "место освободилось");

            // Рекламные записи с котиками (1, 2) фильтруются так же, как обычные (3, 4, 5).
            var s7 = DebugSession(7);
            while (s7.OccupiedSlots < s7.UnitCap()) Assert.IsTrue(s7.DebugSpawnCat(1));
            Assert.IsFalse(s7.DeliveryPool().Any(d => d.RewardModel.RewardType == RewardTypes.Miner), "лимит заполнен — котиков в пуле нет");
            CollectionAssert.AreEqual(Ids("6 7 8 9 10 11 12 13 14 15 20"), PoolIds(s7));
        }

        [Test]
        public void CatBox_HoldsSlot_CellBlocked_OpensAtFullCap()
        {
            var s = DebugSession(4);
            ActivateShafts(s, 2);
            s.DebugAddGold(GameNumber.FromDouble(1e9));

            // Ящик с золотом занимает клетку, но не место в лимите.
            var gold = WaitForBox(s, c => !IsCat(c), "золото");
            Assert.AreEqual(RewardTypes.Currency, gold.RewardType);
            Assert.AreEqual(0, s.Mine.Containers.Count(c => c.Kind == ContainerKind.FreeCat));
            Assert.AreEqual(s.Mine.Cats.Count, s.OccupiedSlots, "ящик с золотом место не занимает");
            Assert.IsFalse(s.IsCellFree(gold.Cell), "ящик занимает клетку");
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(gold.Id));
            Assert.IsTrue(s.IsCellFree(gold.Cell), "после открытия клетка свободна");

            // Ящик с котиком занимает место.
            var box = WaitForBox(s, IsCat, "котик");
            Assert.AreEqual(0, s.Mine.Containers.Count(c => c.Kind == ContainerKind.FreeCat));
            Assert.AreEqual(s.Mine.Cats.Count + 1, s.OccupiedSlots, "ящик с котиком занимает место в лимите");

            while (s.OccupiedSlots < s.UnitCap()) Assert.IsTrue(s.DebugSpawnCat(1));
            Assert.AreEqual(s.UnitCap() - 1, s.Mine.Cats.Count);
            Assert.AreEqual(CommandResult.CapReached, s.CanHire(), "место зарезервировано ящиком");
            Assert.AreEqual(CommandResult.CapReached, s.Hire());
            Assert.AreEqual(s.UnitCap() - 1, s.Mine.Cats.Count);

            // Клетка ящика занята: поставить туда котика нельзя.
            var cat = s.Mine.Cats[0];
            int origin = cat.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, Cx(s, box.Cell), Cy(s, box.Cell)).Result);
            Assert.AreEqual(origin, cat.Cell);
            Assert.AreSame(box, Box(s));

            // Открытие при заполненном лимите не блокируется: котик в клетке ящика, в Idle.
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            Assert.AreEqual(s.UnitCap(), s.Mine.Cats.Count);
            Assert.AreEqual(s.UnitCap(), s.OccupiedSlots);
            var spawned = s.CatAt(box.Cell);
            Assert.IsNotNull(spawned, "котик появляется в клетке ящика");
            Assert.AreEqual(box.CatLevel, spawned.Level);
            Assert.AreEqual(-1, spawned.TargetIndex, "котик из доставки — в Idle");

            // При заполненном лимите 20 минут идут только ящики без котиков.
            long from = s.Player.PlayerTimeTicks;
            int boxes = 0;
            while (s.Player.PlayerTimeTicks - from < 1200 * S)
            {
                var slot = NextSlot(s);
                Assert.AreNotEqual(RewardTypes.Miner, slot.Box.RewardType, "лимит заполнен — котик не выпадает");
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(slot.Box.Id));
                boxes++;
            }
            Assert.Greater(boxes, 10);
            Assert.AreEqual(s.UnitCap(), s.Mine.Cats.Count);
        }

        // ───────────────────────────── Одна доставка на поле ─────────────────────────────

        [Test]
        public void OneBoxOnField_TimerStandsUntilClaim()
        {
            // zone7: в пуле есть реклама, поэтому идущий таймер был бы виден и по пропускам.
            var s = DebugSession(7);
            ActivateShafts(s, 2);
            Slot slot = null;
            for (int i = 0; i < 20 && (slot == null || slot.Box == null); i++) slot = NextSlot(s);
            Assert.IsNotNull(slot.Box);
            var box = slot.Box;
            var h = History(s);
            Assert.AreEqual(0, h.ClaimTimes.Count);
            int drops = TotalDrops(s);
            int skippedAds = h.SkippedAds;
            string content = BoxText(box);

            s.Events.Clear();
            s.Advance(2 * Hour);
            Assert.AreEqual(1, Boxes(s), "на поле не больше одной неоткрытой доставки");
            Assert.AreSame(box, Box(s));
            Assert.AreEqual(content, BoxText(Box(s)), "содержимое не меняется");
            Assert.AreEqual(drops, TotalDrops(s), "пока ящик на поле, слоты не разыгрываются");
            Assert.AreEqual(skippedAds, h.SkippedAds, "пока ящик на поле, реклама не пропускается");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.DeliverySkippedAd), "событий пропуска нет");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.ContainerSpawned && e.Text == ContainerKind.Delivery.ToString()), "новых ящиков нет");

            // Таймер следующей запускается после получения: x = 1 полученная за час + 1 = 2 → F-2 = 23.44 с ± 10 %.
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            long openedAt = s.Player.PlayerTimeTicks;
            Assert.AreEqual(1, h.ClaimTimes.Count);
            Assert.AreEqual(23.44, Ft2(2), 1e-9);
            long timer = s.Mine.DeliveryTimerTicks;
            Assert.That(GameSession.TicksToSeconds(timer), Is.InRange(0.9 * 23.44 - 1e-6, 1.1 * 23.44 + 1e-6),
                "два часа с ящиком на поле в новый таймер не засчитываются");
            var next = NextSlot(s);
            Assert.AreEqual(openedAt + timer, next.At, "следующий слот — ровно по таймеру после получения");
        }

        [Test]
        public void NoFreeCell_CreationDeferredAndTimerStands()
        {
            var s = DebugSession(4);
            ActivateShafts(s, 2);
            int spawned = 0;
            while (s.DebugSpawnCat(1)) spawned++;
            Assert.Greater(spawned, s.UnitCap());
            Assert.AreEqual(-1, s.FindSpawnCell(), "свободных клеток нет");

            s.Advance(600 * S);
            Assert.AreEqual(0, Boxes(s), "клетки нет — ящик не создаётся");
            Assert.AreEqual(0, TotalDrops(s), "за 10 минут ожидания слоты не разыгрывались");

            // Объединение освобождает одну клетку — отложенный ящик появляется в ней без нового интервала.
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int freed = a.Cell;
            Assert.IsTrue(s.DropCat(a.Id, Cx(s, b.Cell), Cy(s, b.Cell)).Merged);
            s.Advance(1);
            var box = Box(s);
            Assert.IsNotNull(box, "клетка появилась — создание отложенной доставки");
            Assert.AreEqual(freed, box.Cell);
            Assert.AreEqual(1, TotalDrops(s), "за всё ожидание разыгран ровно один слот");
            Assert.AreNotEqual(RewardTypes.Miner, box.RewardType, "котиков больше лимита — запись с котиком не выпадает");
        }

        // ───────────────────────────── Пропуск рекламного слота ─────────────────────────────

        [Test]
        public void AdSlot_SkippedRecordedAndTimerRestarted()
        {
            var ads = B.Root.Deliveries.Where(d => d.IsAd).ToList();
            CollectionAssert.AreEquivalent(Ids("1 2 6 10"), ads.Select(d => d.Id));
            Assert.IsTrue(ads.All(d => d.MaxDupes == 1), "у рекламных записей MaxDupes = 1");

            var s = DebugSession(7);
            ActivateShafts(s, 2);
            var h = History(s);
            var claims = new List<long>();
            var skipped = new List<string>();
            var multipliers = new List<double>();
            long start = s.Player.PlayerTimeTicks;
            while (s.Player.PlayerTimeTicks - start < 2 * Hour)
            {
                int skippedBefore = h.SkippedAds;
                var claimTimesBefore = h.ClaimTimes.ToList();
                var goldDelivery = Ledger(s, "gold:delivery");
                var elixirDelivery = Ledger(s, "elixir:delivery");
                int dynamite = s.Player.Dynamite;
                int cats = s.Mine.Cats.Count;
                var slot = NextSlot(s);
                if (slot.SkippedId != null)
                {
                    string id = slot.SkippedId;
                    Assert.IsTrue(B.Deliveries[id].IsAd, "пропускается только рекламная запись: " + id);
                    Assert.AreEqual(skippedBefore + 1, h.SkippedAds, "DeliveryHistory.SkippedAds");
                    Assert.AreEqual(0, Boxes(s), "доставка не появляется");
                    Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.ContainerSpawned && e.Text == ContainerKind.Delivery.ToString()));
                    Assert.AreEqual(goldDelivery, Ledger(s, "gold:delivery"), "золото не выдаётся");
                    Assert.AreEqual(elixirDelivery, Ledger(s, "elixir:delivery"), "эликсир не выдаётся");
                    Assert.AreEqual(dynamite, s.Player.Dynamite, "динамит не выдаётся");
                    Assert.AreEqual(cats, s.Mine.Cats.Count, "котик не выдаётся");
                    CollectionAssert.Contains(h.DupeTimes[id], slot.At, "счётчик MaxDupes растёт как при обычном выпадении");
                    CollectionAssert.DoesNotContain(h.ClaimTimes, slot.At, "счётчик полученных не меняется");
                    CollectionAssert.IsSubsetOf(h.ClaimTimes, claimTimesBefore, "счётчик полученных не меняется");
                    CollectionAssert.DoesNotContain(PoolIds(s), id, "MaxDupes = 1 исчерпан до конца окна");
                    multipliers.Add(CheckRestartedTimer(s, claims, slot.At, "после пропуска " + id));
                    skipped.Add(id);
                }
                else
                {
                    Assert.IsFalse(B.Deliveries[slot.Box.DeliveryId].IsAd, "рекламная запись ящиком не выдаётся");
                    Assert.AreEqual(skippedBefore, h.SkippedAds);
                    Assert.AreEqual(CommandResult.Ok, s.OpenContainer(slot.Box.Id));
                    claims.Add(s.Player.PlayerTimeTicks);
                    multipliers.Add(CheckRestartedTimer(s, claims, s.Player.PlayerTimeTicks, "после получения " + slot.Box.DeliveryId));
                }
            }
            TestContext.WriteLine("пропущены: " + string.Join(", ", skipped) + "; получено: " + claims.Count +
                                  "; множитель интервала: " + multipliers.Min().ToString("0.000") + "…" + multipliers.Max().ToString("0.000"));
            Assert.That(skipped.Count, Is.InRange(1, 4), "MaxDupes = 1 за 14 400 с: не больше 4 пропусков за 4 часа");
            CollectionAssert.AllItemsAreUnique(skipped, "каждая рекламная запись — не больше одного раза за окно");
            Assert.AreEqual(skipped.Count, h.SkippedAds);
            Assert.Greater(multipliers.Max() - multipliers.Min(), 0.1, "случайный множитель из [0.9; 1.1] действительно применяется");
        }

        // ───────────────────────────── Фиксация содержимого ─────────────────────────────

        [Test]
        public void Content_FixedAtCreation_SameResultAfterReload()
        {
            // Содержимое ранга 7 по 10.5 / E-15: FT3(Quantity*, x = 7) из JSON.
            var soft = new Dictionary<string, double> { { "7", 35 }, { "8", 11 }, { "9", 11 } };
            var coreSeconds = new Dictionary<string, double> { { "11", 720 }, { "12", 420 }, { "13", 300 }, { "14", 300 }, { "15", 120 } };

            var s = DebugSession(7);
            ActivateShafts(s, 2);
            var seen = new HashSet<string>();
            bool rateChecked = false, dynamiteCapChecked = false;
            for (int guard = 0; guard < 400 && (seen.Count < 4 || !rateChecked || !dynamiteCapChecked); guard++)
            {
                var slot = NextSlot(s);
                if (slot.Box == null) continue;
                var box = slot.Box;
                string id = box.DeliveryId;
                var rec = B.Deliveries[id];
                Assert.AreEqual(rec.RewardModel.RewardType, box.RewardType, "RewardType записи " + id);
                Assert.AreEqual(rec.RewardModel.DetailedType, box.DetailedType, "DetailedType записи " + id);
                double rate = s.GetRewardIncomeRate().ToDouble();
                if (IsCat(box))
                {
                    Assert.AreEqual(1, s.HireLevel());
                    Assert.AreEqual(1, box.CatLevel, "котик: max(1, L_next − 2) при L_next = 1");
                }
                else if (box.RewardType == RewardTypes.Currency && box.DetailedType == "Soft")
                    Assert.AreEqual(soft[id], box.Amount.ToDouble(), "эликсир записи " + id + " на ранге 7");
                else if (box.RewardType == RewardTypes.Currency && box.DetailedType == "Core")
                    Assert.AreEqual(Math.Max(1, Math.Floor(coreSeconds[id] * rate + 1e-9)), box.Amount.ToDouble(), "золото записи " + id + ": FT3 × ставка " + rate);
                else
                {
                    Assert.AreEqual(RewardTypes.Spell, box.RewardType);
                    Assert.AreEqual("Dynamite", box.DetailedType);
                    Assert.AreEqual(1.0, box.Amount.ToDouble(), "динамит — 1 заряд");
                }
                seen.Add(IsCat(box) ? "Miner" : box.DetailedType);

                // Загрузка ничего не меняет и не перебрасывает содержимое.
                string json = Snapshot(s);
                var reloaded = FromJson(json);
                Assert.AreEqual(json, Snapshot(reloaded), "состояние после загрузки совпадает");
                var rb = Box(reloaded);
                Assert.IsNotNull(rb);
                Assert.AreEqual(BoxText(box), BoxText(rb), "ящик после загрузки тот же");
                Assert.IsFalse(reloaded.IsCellFree(box.Cell), "клетка ящика занята и после загрузки");

                // Core: после загрузки ставка дохода выросла, а сумма — зафиксированная при создании.
                if (box.DetailedType == "Core" && !rateChecked)
                {
                    var changed = FromJson(json);
                    changed.DebugAddGold(GameNumber.FromDouble(1e6));
                    Assert.AreEqual(CommandResult.Ok, changed.UpgradeGenerator(changed.Layout.Cart.Ordinal, out _));
                    changed = Reload(changed);
                    double newRate = changed.GetRewardIncomeRate().ToDouble();
                    Assert.Greater(newRate, rate);
                    Assert.AreNotEqual(box.Amount.ToDouble(), Math.Max(1, Math.Floor(coreSeconds[id] * newRate + 1e-9)), "переброс дал бы другую сумму");
                    var before = Ledger(changed, "gold:delivery");
                    Assert.AreEqual(CommandResult.Ok, changed.OpenContainer(box.Id));
                    Assert.AreEqual(box.Amount, Ledger(changed, "gold:delivery") - before, "выдана сумма, зафиксированная при создании");
                    rateChecked = true;
                }

                // Динамит при запасе 4 к моменту открытия не начисляется сверх лимита.
                if (box.RewardType == RewardTypes.Spell && !dynamiteCapChecked)
                {
                    var full = FromJson(json);
                    full.DebugAddDynamite(GameSession.MaxDynamite);
                    Assert.AreEqual(4, full.Player.Dynamite);
                    Assert.AreEqual(CommandResult.Ok, full.OpenContainer(box.Id));
                    Assert.AreEqual(4, full.Player.Dynamite, "заряд не начисляется сверх 4");
                    Assert.AreEqual(0, Boxes(full), "ящик открыт");
                    dynamiteCapChecked = true;
                }

                // Открытие после перезагрузки даёт тот же результат, что и без неё.
                var gold = Ledger(s, "gold:delivery");
                var elixir = Ledger(s, "elixir:delivery");
                int dynamite = s.Player.Dynamite;
                s.Events.Clear();
                reloaded.Events.Clear();
                Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
                Assert.AreEqual(CommandResult.Ok, reloaded.OpenContainer(rb.Id));
                Assert.AreEqual(Snapshot(s), Snapshot(reloaded), "открытие после перезагрузки: запись " + id);
                if (IsCat(box))
                {
                    var cat = s.CatAt(box.Cell);
                    Assert.IsNotNull(cat);
                    Assert.AreEqual(box.CatLevel, cat.Level);
                }
                else if (box.DetailedType == "Core")
                    Assert.AreEqual(box.Amount, Ledger(s, "gold:delivery") - gold);
                else if (box.DetailedType == "Soft")
                    Assert.AreEqual(box.Amount, Ledger(s, "elixir:delivery") - elixir);
                else
                    Assert.AreEqual(Math.Min(4, dynamite + 1), s.Player.Dynamite);

                s = reloaded; // дальше играем в загруженной сессии
            }
            CollectionAssert.AreEquivalent(new[] { "Miner", "Core", "Soft", "Dynamite" }, seen.ToArray(), "встречены все четыре вида содержимого");
            Assert.IsTrue(rateChecked, "встречен ящик с золотом");
            Assert.IsTrue(dynamiteCapChecked, "встречен ящик с динамитом");
        }

        // ───────────────────────────── Правило догоняния (E-15) ─────────────────────────────

        [Test]
        public void CatchUp_SingleWeakestCatGetsPair()
        {
            Assert.AreEqual(2, B.Props.DeliveryForeverAloneDelta);
            var s = DebugSession(4);
            ActivateShafts(s, 2);
            s.DebugAddGold(GameNumber.FromDouble(1e15));
            for (int i = 0; i < 21; i++)
            {
                Assert.AreEqual(CommandResult.Ok, s.Hire(), "найм " + (i + 1));
                MergeAllPairs(s);
            }
            Assert.AreEqual(4, s.HireLevel(), "L_next после 21 найма: 1 + floor(0.4 + 0.125·21) = 4");
            if (!s.Mine.Cats.Any(c => c.Level == 1)) Assert.IsTrue(s.DebugSpawnCat(1));
            Assert.AreEqual(1, s.Mine.Cats.Count(c => c.Level == 1));
            Assert.AreEqual(1, s.Mine.Cats.Min(c => c.Level));
            Assert.AreEqual(0, s.Mine.Containers.Count);
            string json = Snapshot(s);

            // Один котик минимального уровня 1 ≤ L_next − 1 − 2 = 1 — доставка приносит ему пару.
            var single = FromJson(json);
            var boxA = WaitForBox(single, IsCat, "котик");
            Assert.AreEqual(4, single.HireLevel());
            Assert.AreEqual(1, boxA.CatLevel, "догоняние: уровень Lmin = 1");

            // Два котика уровня 1 — правило не применяется: max(1, 4 − 2) = 2.
            var pair = FromJson(json);
            Assert.IsTrue(pair.DebugSpawnCat(1));
            var boxB = WaitForBox(pair, IsCat, "котик");
            Assert.AreEqual(4, pair.HireLevel());
            Assert.AreEqual(2, boxB.CatLevel, "без догоняния: max(1, L_next − 2) = 2");
        }
    }
}
