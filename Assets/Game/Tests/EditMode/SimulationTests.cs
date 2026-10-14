using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public class SimulationTests
    {
        const long S = GameSession.TicksPerSecond;

        static GameSession NewSession(int zone = 1, string slot = SaveSlots.Main) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, 1_000_000L, zone);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        [Test]
        public void NewZoneStartsWithoutDeadEnd()
        {
            var s = NewSession();
            Assert.AreEqual(25.0, s.Mine.Gold.ToDouble());
            Assert.AreEqual(0, s.Mine.Cats.Count);
            Assert.AreEqual(10, s.UnitCap());
            Assert.AreEqual(2.0, s.HirePrice().ToDouble());
            Assert.AreEqual(1, s.HireLevel());
            Assert.AreEqual(CommandResult.Ok, s.CanHire());
            Assert.AreEqual(1.5, s.AutoIncomePerSecond().ToDouble(), 1e-12);
        }

        [Test]
        public void HireIsAtomicAndPriced()
        {
            var s = NewSession();
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(15.0, s.Mine.Gold.ToDouble());
            Assert.AreEqual(2, s.Mine.Cats.Count);
            Assert.AreNotEqual(s.Mine.Cats[0].Cell, s.Mine.Cats[1].Cell);
            Assert.AreEqual(32.0, s.HirePrice().ToDouble());
            // Не хватает золота: состояние не меняется.
            var before = Snapshot(s);
            Assert.AreEqual(CommandResult.NotEnoughGold, s.Hire());
            s.Events.Clear();
            Assert.AreEqual(before, Snapshot(s));
        }

        [Test]
        public void SpawnCellIsOnStartPad()
        {
            var s = NewSession();
            int cell = s.FindSpawnCell();
            int cartRow = s.Layout.RowC;
            Assert.AreEqual(3, s.Layout.Col(cell));
            Assert.That(s.Layout.Row(cell), Is.InRange(cartRow - 3, cartRow + 1));
        }

        [Test]
        public void CatMinesRockInExpectedTime()
        {
            var s = NewSession();
            s.Hire();
            var cat = s.Mine.Cats[0];
            int ord = OrdAt(s, 2, 4);
            Assert.GreaterOrEqual(ord, 0);
            Assert.IsTrue(s.IsTargetAvailable(ord));
            var hp = s.Mine.Objects[ord].Hp.ToDouble();
            var outcome = s.DropCat(cat.Id, 2.5, 4.5);
            Assert.AreEqual(CommandResult.Ok, outcome.Result);
            Assert.AreEqual(s.Layout.Objects[ord].Index, cat.TargetIndex);
            int hits = (int)Math.Ceiling(hp / s.CatDamage(1).ToDouble());
            s.Advance(hits * S - 1);
            Assert.AreEqual(ObjectStatus.Intact, s.Mine.Objects[ord].Status);
            s.Advance(1);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[ord].Status);
            Assert.AreEqual(-1, cat.TargetIndex, "после завершения цели котик свободен");
            Assert.IsTrue(s.Mine.Gold.ToDouble() > 15, "награда камня");
            Assert.AreEqual(1L, s.Player.RewardCycle["rockonboardingmine1"]);
        }

        [Test]
        public void MergeKeepsTargetHpAndFreesSlot()
        {
            var s = NewSession();
            s.Hire();
            s.Hire();
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int ord = OrdAt(s, 2, 4);
            s.DropCat(b.Id, 2.5, 4.5);
            s.Advance(3 * S);
            var hpBefore = s.Mine.Objects[ord].Hp;
            var drop = s.DropCat(a.Id, s.Layout.Col(b.Cell) + 0.5, s.Layout.Row(b.Cell) + 0.5);
            Assert.IsTrue(drop.Merged);
            Assert.AreEqual(1, s.Mine.Cats.Count);
            Assert.AreEqual(2, s.Mine.Cats[0].Level);
            Assert.AreEqual(hpBefore, s.Mine.Objects[ord].Hp);
            Assert.AreEqual(s.Layout.Objects[ord].Index, s.Mine.Cats[0].TargetIndex, "результат продолжает работу получателя");
            Assert.AreEqual(0L, s.Mine.Cats[0].PhaseTicks, "фаза удара обнуляется");
            Assert.AreEqual(1, s.OccupiedSlots);
        }

        [Test]
        public void DifferentLevelsDoNotMerge()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugSpawnCat(1);
            s.DebugSpawnCat(2);
            var a = s.Mine.Cats[0];
            var b = s.Mine.Cats[1];
            int cellA = a.Cell;
            var r = s.DropCat(a.Id, s.Layout.Col(b.Cell) + 0.5, s.Layout.Row(b.Cell) + 0.5);
            Assert.AreEqual(CommandResult.DifferentLevels, r.Result);
            Assert.AreEqual(2, s.Mine.Cats.Count);
            Assert.AreEqual(cellA, a.Cell);
        }

        [Test]
        public void ClosedSectorAndObstacleRejectDrop()
        {
            var s = NewSession(5, SaveSlots.Debug);
            s.DebugSpawnCat(1);
            var cat = s.Mine.Cats[0];
            int origin = cat.Cell;
            var gate = s.Layout.Gates[0];
            // Клетка над воротами — закрытый сектор.
            var r = s.DropCat(cat.Id, 3.5, gate.Row - 1 + 0.5);
            Assert.AreEqual(CommandResult.ClosedSector, r.Result);
            Assert.AreEqual(origin, cat.Cell);
            // Препятствие в строке ворот.
            r = s.DropCat(cat.Id, 0.5, gate.Row + 0.5);
            Assert.AreNotEqual(CommandResult.Ok, r.Result);
            Assert.AreEqual(origin, cat.Cell);
        }

        [Test]
        public void AdvanceIsAdditive()
        {
            var a = NewSession();
            var b = NewSession();
            foreach (var s in new[] { a, b })
            {
                s.Hire();
                s.Hire();
                s.DropCat(s.Mine.Cats[0].Id, 2.5, 4.5);
                s.DropCat(s.Mine.Cats[1].Id, 3.5, 4.5);
            }
            for (int i = 0; i < 500; i++) a.Advance(S / 50);
            b.Advance(10 * S);
            a.Events.Clear();
            b.Events.Clear();
            Assert.AreEqual(Snapshot(b), Snapshot(a));
        }

        [Test]
        public void OfflineMatchesOnlineWithoutActions()
        {
            var online = NewSession();
            online.Hire();
            online.Hire();
            online.DropCat(online.Mine.Cats[0].Id, 2.5, 4.5);
            online.DropCat(online.Mine.Cats[1].Id, 3.5, 4.5);
            var offline = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(online)));

            long seconds = 2 * 3600;
            for (int i = 0; i < seconds; i++) online.Advance(S);
            var summary = OfflineProgress.Apply(offline, offline.State.LastProcessedUtcMs + seconds * 1000);
            Assert.AreEqual(seconds * S, summary.Ticks);
            Assert.AreEqual(online.Mine.RocksDestroyed, offline.Mine.RocksDestroyed);
            Assert.AreEqual(online.Player.RewardCycle.Count, offline.Player.RewardCycle.Count);
            Assert.IsTrue(GameNumber.ApproximatelyEqual(online.Mine.Gold, offline.Mine.Gold, 1e-9));
            Assert.AreEqual(online.Player.Rng.Gacha.State, offline.Player.Rng.Gacha.State);
        }

        [Test]
        public void OfflineIsCappedAndMonotonic()
        {
            var s = NewSession();
            long start = s.State.LastProcessedUtcMs;
            var first = OfflineProgress.Apply(s, start + 24L * 3600 * 1000);
            Assert.IsTrue(first.Capped);
            Assert.AreEqual(GameSession.MaxOfflineTicks, first.Ticks);
            // Повторное применение того же момента не начисляет ничего.
            var again = OfflineProgress.Apply(s, start + 24L * 3600 * 1000);
            Assert.AreEqual(0, again.Ticks);
            // Перевод часов назад не сдвигает метку.
            var back = OfflineProgress.Apply(s, start);
            Assert.AreEqual(0, back.Ticks);
            Assert.AreEqual(start + 24L * 3600 * 1000, s.State.LastProcessedUtcMs);
        }

        [Test]
        public void SaveLoadRoundTripAndBackup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "catminers-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var s = NewSession();
                s.Hire();
                s.DropCat(s.Mine.Cats[0].Id, 2.5, 4.5);
                s.Advance(5 * S);
                var svc = new SaveService(dir);
                svc.Save(s.State, 2_000_000L);
                s.Advance(5 * S);
                svc.Save(s.State, 2_005_000L);
                var loaded = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status);
                Assert.AreEqual(Snapshot(s), SaveService.SerializeState(loaded.State));
                var restored = GameSession.FromState(TestData.Balance, loaded.State);
                Assert.AreEqual(1, restored.Mine.Cats.Count);

                // Повреждённый основной файл — восстановление из резервной копии.
                File.WriteAllText(svc.MainPath(SaveSlots.Main), "{ broken");
                var fromBackup = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.LoadedFromBackup, fromBackup.Status);
                Assert.IsTrue(Directory.GetFiles(dir).Any(f => f.Contains(".corrupt-")), "повреждённый файл не удаляется");

                // Другой баланс — сохранение не загружается.
                var other = svc.Load(SaveSlots.Main, new string('0', 64));
                Assert.AreEqual(LoadStatus.IncompatibleBalance, other.Status);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        sealed class FailingFs : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            public string FailOn;
            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);
            public void WriteAllBytesDurable(string p, byte[] d) { if (FailOn == "write") throw new IOException("сбой записи"); m_Real.WriteAllBytesDurable(p, d); }
            public void Replace(string s, string d, string b) { if (FailOn == "replace") throw new IOException("сбой замены"); m_Real.Replace(s, d, b); }
            public void Move(string s, string d) { if (FailOn == "move") throw new IOException("сбой переименования"); m_Real.Move(s, d); }
            public void Copy(string s, string d) => m_Real.Copy(s, d);
            public void Delete(string p) => m_Real.Delete(p);
            public void CreateDirectory(string p) => m_Real.CreateDirectory(p);
        }

        [Test]
        public void InterruptedSaveKeepsLastValidState()
        {
            string dir = Path.Combine(Path.GetTempPath(), "catminers-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var fs = new FailingFs();
                var svc = new SaveService(dir, fs);
                var s = NewSession();
                s.Hire();
                svc.Save(s.State, 1L);
                string valid = Snapshot(s);
                s.Hire();
                foreach (var mode in new[] { "write", "replace" })
                {
                    fs.FailOn = mode;
                    try { svc.Save(s.State, 2L); } catch (IOException) { }
                    fs.FailOn = null;
                    var loaded = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                    Assert.IsNotNull(loaded.State, mode);
                    var json = SaveService.SerializeState(loaded.State);
                    Assert.IsTrue(json == valid || json == Snapshot(s), mode + ": загружено неизвестное состояние");
                    Assert.LessOrEqual(loaded.State.Mine.Cats.Count, 2);
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ShaftClearActivateManualPayoutAndAutomation()
        {
            var s = NewSession(3, SaveSlots.Debug);
            var citrine = s.Layout.Shafts.First(x => x.TypeId == "citrine");
            Assert.AreEqual("ca003", citrine.CardId);
            Assert.AreEqual(1, citrine.CardLevel);
            s.DebugCompleteTarget(citrine.Ordinal);
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[citrine.Ordinal].Status);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            var gen = s.Mine.Objects[citrine.Ordinal].Gen;
            Assert.IsNotNull(gen);
            bool hadCard = s.CardLevel("ca003") >= 1;
            Assert.AreEqual(hadCard, gen.Automated, "автоматизация только при точном требовании карты");
            if (!gen.Automated)
            {
                long cycle = s.GeneratorCycleTicks(citrine.Ordinal);
                s.Advance(cycle);
                Assert.IsTrue(gen.PayoutReady, "ручная шахта ждёт сбора");
                s.Advance(cycle * 3);
                Assert.IsTrue(gen.PayoutReady);
                var goldBefore = s.Mine.Gold;
                Assert.AreEqual(CommandResult.Ok, s.CollectPayout(citrine.Ordinal));
                Assert.IsTrue(s.Mine.Gold > goldBefore);
                // Карта нужного уровня включает автоматизацию.
                s.DebugGiveZoneCards();
                Assert.IsTrue(gen.Automated);
            }
            // Повторная активация не выдаёт награду второй раз.
            Assert.AreNotEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
        }

        [Test]
        public void Zone2ShaftCannotBeAutomatedInNormalPlay()
        {
            var s = NewSession(2);
            var shaft = s.Layout.Shafts.Single();
            Assert.AreEqual("ca002", shaft.CardId);
            Assert.AreEqual(0, s.CardLevel("ca002"));
        }

        [Test]
        public void GateBrokenThenOpenedAppliesBonusesOnce()
        {
            var s = NewSession(5, SaveSlots.Debug);
            var gate = s.Layout.Gates[0];
            Assert.IsTrue(s.IsTargetAvailable(gate.Ordinal));
            int capBefore = s.UnitCap();
            var priceBefore = s.HirePrice();
            var mulBefore = s.GlobalIncomeMultiplier();
            double intervalBefore = s.FreeCatIntervalSeconds();
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(ObjectStatus.Broken, s.Mine.Objects[gate.Ordinal].Status);
            Assert.IsFalse(s.IsSectorOpen(1), "Broken без тапа не открывает сектор");
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.IsTrue(s.IsSectorOpen(1));
            Assert.AreEqual(capBefore + 1, s.UnitCap());
            Assert.AreEqual(mulBefore.ToDouble() * 2, s.GlobalIncomeMultiplier().ToDouble(), 1e-9);
            Assert.AreEqual(Math.Max(1, Math.Ceiling(priceBefore.ToDouble() / 10 - 1e-9)), s.HirePrice().ToDouble());
            Assert.AreEqual(intervalBefore - 60, s.FreeCatIntervalSeconds(), 1e-9);
            Assert.AreNotEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.AreEqual(capBefore + 1, s.UnitCap());
            // После save/load бонусы те же.
            var restored = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(s)));
            Assert.AreEqual(capBefore + 1, restored.UnitCap());
        }

        [Test]
        public void ZoneTransitionRules()
        {
            var s = NewSession(2, SaveSlots.Debug);
            Assert.IsFalse(s.CanCompleteZone(out _));
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsFalse(s.CanCompleteZone(out string reason));
            StringAssert.Contains("Shafts", reason);
            var shaft = s.Layout.Shafts.Single();
            s.DebugCompleteTarget(shaft.Ordinal);
            s.ActivateShaft(shaft.Ordinal);
            Assert.IsTrue(s.CanCompleteZone(out _), "оставшиеся камни переход не блокируют");
            Assert.Greater(s.RemainingRocks(), 0);
            s.DebugAddGold(GameNumber.FromDouble(1e6));
            var elixirBefore = s.Player.Elixir;
            var cardsBefore = s.CardLevel("ca002");
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(3, s.Mine.Zone);
            Assert.AreEqual(25.0, s.Mine.Gold.ToDouble(), "золото сбрасывается");
            Assert.AreEqual(0, s.Mine.Cats.Count);
            Assert.IsTrue(s.Player.Elixir > elixirBefore, "эликсир сохраняется и растёт");
            Assert.AreEqual(1, s.CardLevel("ca002"), "награда выхода zone2 по нижней границе даёт ca002 x1");
            Assert.AreEqual(0, cardsBefore);
            Assert.AreEqual(2, s.Player.HighestCompletedZone);
        }

        [Test]
        public void DynamiteDamagesAvailableTargetOnly()
        {
            var s = NewSession(1, SaveSlots.Debug);
            s.DebugAddDynamite(1);
            // Препятствие — заряд не тратится.
            Assert.AreNotEqual(CommandResult.Ok, s.UseDynamite(0.5, 0.5));
            Assert.AreEqual(1, s.Player.Dynamite);
            int ord = OrdAt(s, 2, 4);
            Assert.AreEqual(CommandResult.Ok, s.UseDynamite(2.5, 4.5));
            Assert.AreEqual(0, s.Player.Dynamite);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[ord].Status, "1050 урона хватает на камень L1/L2");
        }

        [Test]
        public void RewardCycleCounterPersistsAcrossZones()
        {
            var s = NewSession(6, SaveSlots.Debug);
            var rock = s.Layout.Objects.First(o => o.Kind == ObjectKind.Rock && o.TypeId == "rockbasic");
            s.DebugCompleteTarget(rock.Ordinal);
            Assert.AreEqual(1L, s.Player.RewardCycle["rockbasic"]);
            s.DebugGotoZone(7);
            Assert.AreEqual(1L, s.Player.RewardCycle["rockbasic"]);
        }

        [Test]
        public void DeliveriesStartAfterTwoShaftsFromRank4()
        {
            var s = NewSession(4, SaveSlots.Debug);
            Assert.IsFalse(s.Mine.DeliveriesEnabled);
            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                s.ActivateShaft(sh.Ordinal);
            }
            Assert.IsTrue(s.Mine.DeliveriesEnabled);
            Assert.IsTrue(s.Player.FreeChest.Enabled);
            s.Advance(21 * S);
            Assert.AreEqual(1, s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery), "первая доставка через 20 с");
            s.Advance(600 * S);
            Assert.AreEqual(1, s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery), "на поле не больше одной доставки");
            var box = s.Mine.Containers.First(c => c.Kind == ContainerKind.Delivery);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            Assert.AreEqual(0, s.Mine.Containers.Count(c => c.Kind == ContainerKind.Delivery));
        }

        [Test]
        public void FreeChestTimer()
        {
            var s = NewSession(4, SaveSlots.Debug);
            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                s.ActivateShaft(sh.Ordinal);
            }
            Assert.AreEqual(0, s.Player.FreeChest.Stock);
            s.Advance(4 * 3600 * S);
            Assert.AreEqual(1, s.Player.FreeChest.Stock);
            s.Advance(4 * 3600 * S);
            Assert.AreEqual(2, s.Player.FreeChest.Stock);
            s.Advance(4 * 3600 * S);
            Assert.AreEqual(2, s.Player.FreeChest.Stock, "запас не больше 2");
            int before = s.CardLevel("ca002") + s.CardLevel("ca015");
            Assert.AreEqual(CommandResult.Ok, s.ClaimFreeChest());
            Assert.AreEqual(1, s.Player.FreeChest.NextIndex);
            Assert.Greater(s.CardLevel("ca002") + s.CardLevel("ca015") + s.CardStateOf("ca002").Duplicates, before - 1);
        }

        [Test]
        public void FreeCatComesInContainer()
        {
            var s = NewSession();
            s.Advance(2400 * S);
            var c = s.Mine.Containers.SingleOrDefault(x => x.Kind == ContainerKind.FreeCat);
            Assert.IsNotNull(c, "бесплатный котик через 40 минут");
            Assert.AreEqual(1, c.CatLevel, "max(1, уровень найма − 1)");
            Assert.AreEqual(1, s.OccupiedSlots, "контейнер занимает место в лимите");
            s.Advance(2400 * S);
            Assert.AreEqual(1, s.Mine.Containers.Count, "второй не появляется, пока первый не открыт");
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(c.Id));
            Assert.AreEqual(1, s.Mine.Cats.Count);
        }
    }

    public class ScenarioBotTests
    {
        [Test]
        public void BotCompletesFirstZones()
        {
            var s = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Main, 1_000_000L);
            var bot = new ScenarioBot(s);
            var reports = new List<BotZoneReport>();
            for (int zone = 1; zone <= 3; zone++)
            {
                var r = bot.PlayZone();
                reports.Add(r);
                Assert.AreEqual(BotZoneOutcome.Completed, r.Outcome, ScenarioBot.FormatReport(reports));
            }
            TestContext.WriteLine(ScenarioBot.FormatReport(reports));
        }

        /// <summary>Отчёт о темпе (ТЗ 17.2) по zone1–zone10. Долгий: запускается явно фильтром.</summary>
        [Test, Explicit("долгий прогон: dotnet test --filter BotZones1To10Report")]
        public void BotZones1To10Report()
        {
            var s = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Main, 1_000_000L);
            var bot = new ScenarioBot(s);
            var reports = new List<BotZoneReport>();
            for (int zone = 1; zone <= 10; zone++)
            {
                var r = bot.PlayZone();
                reports.Add(r);
                if (r.Outcome != BotZoneOutcome.Completed) break;
            }
            string text = ScenarioBot.FormatReport(reports);
            TestContext.WriteLine(text);
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports");
            if (Directory.Exists(dir))
            {
                File.WriteAllText(Path.Combine(dir, "bot-zones-1-10.md"), text);
                File.WriteAllText(Path.Combine(dir, "bot-actions-zones-1-10.md"), ScenarioBot.FormatActionLog(reports));
            }
        }

        /// <summary>Журнал действий (ТЗ 17.2): начало сессии, шаги с командами и итог зоны, время не убывает.</summary>
        [Test]
        public void BotWritesActionLogWithSimulationTime()
        {
            var s = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Main, 1_000_000L);
            var bot = new ScenarioBot(s);
            var r = bot.PlayZone();
            Assert.AreEqual(BotZoneOutcome.Completed, r.Outcome);
            Assert.AreEqual(BotAction.KindSession, r.Actions[0].Kind);
            Assert.AreEqual(0.0, r.Actions[0].T);
            var last = r.Actions[r.Actions.Count - 1];
            Assert.AreEqual(BotAction.KindResult, last.Kind);
            Assert.AreEqual(r.SimSeconds, last.T, 1e-9);
            Assert.IsTrue(r.Actions.Any(a => a.Kind == BotAction.KindStep && a.Detail.Contains("наём")), "наём в журнале");
            Assert.IsTrue(r.Actions.Any(a => a.Kind == BotAction.KindStep && a.Detail.Contains("назначение")), "назначение в журнале");
            Assert.IsTrue(r.Actions.Any(a => a.Kind == BotAction.KindStep && a.Detail.Contains("переход: zone1 → zone2")), "переход в журнале");
            for (int i = 1; i < r.Actions.Count; i++)
                Assert.LessOrEqual(r.Actions[i - 1].T, r.Actions[i].T, "время журнала не убывает");
            string log = ScenarioBot.FormatActionLog(new[] { r });
            StringAssert.Contains("## zone1 — пройдена за", log);
            StringAssert.Contains("| 0:00:00 | сессия | начало сессии 1 |", log);
        }

        /// <summary>
        /// Сессии — непрерывный цикл (ТЗ 17.2): часы сессии переходят из зоны в зону, офлайн наступает, когда суммарное
        /// онлайн-время с начала сессии достигает длины сессии, и только после офлайна часы обнуляются.
        /// </summary>
        [Test]
        public void SessionClockCarriesAcrossZones()
        {
            var s = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Main, 1_000_000L);
            var bot = new ScenarioBot(s) { SessionSeconds = 3600 };
            var z1 = bot.PlayZone();
            Assert.AreEqual(BotZoneOutcome.Completed, z1.Outcome);
            Assert.AreEqual(0.0, z1.SessionClockAtStart);
            Assert.IsFalse(z1.Actions.Any(a => a.Kind == BotAction.KindOffline), "zone1 короче часа: офлайна нет");
            Assert.AreEqual(z1.SimSeconds, z1.OnlineSeconds, 1e-9);

            // Сессия zone1 продолжается в zone2: до офлайна остаётся 30 с.
            bot.SessionSeconds = z1.OnlineSeconds + 30;
            var z2 = bot.PlayZone();
            Assert.AreEqual(BotZoneOutcome.Completed, z2.Outcome, ScenarioBot.FormatActionLog(new[] { z1, z2 }));
            Assert.AreEqual(z1.OnlineSeconds, z2.SessionClockAtStart, 1e-9, "часы сессии не сбрасываются при переходе зоны");
            StringAssert.StartsWith("сессия 1 продолжается", z2.Actions[0].Detail);
            var offline = z2.Actions.First(a => a.Kind == BotAction.KindOffline);
            Assert.AreEqual(30.0, offline.T, 1e-9, "офлайн — когда сессия с учётом zone1 достигла своей длины");
            var next = z2.Actions.First(a => a.Kind == BotAction.KindSession && a.T > offline.T);
            Assert.AreEqual(30.0 + bot.OfflineSeconds, next.T, 1e-9);
            StringAssert.StartsWith("начало сессии 2", next.Detail);
            Assert.GreaterOrEqual(z2.SimSeconds, 30.0 + bot.OfflineSeconds);

            // После офлайна часы обнулены: следующая зона начинается с остатком сессии 2.
            var z3 = bot.PlayZone();
            int offlines = z2.Actions.Count(a => a.Kind == BotAction.KindOffline);
            Assert.AreEqual(z2.OnlineSeconds - 30 - (offlines - 1) * bot.SessionSeconds, z3.SessionClockAtStart, 1e-9);
        }

        /// <summary>
        /// «Стена» (ТЗ 17.2): 2 ч симуляции без доступной покупки, улучшения или назначения, которое увеличивает доход или урон.
        /// Голодная зона zone4: лимит занят котиками разных уровней (наёма, объединений и бесплатных котиков нет), цели котикам
        /// не по силам (HP завышено), доход — только кузница. Бот копит на выгодный пакет «до порога» (пока копит, дешёвое
        /// улучшение доступно — это не стена), покупает его, а следующее улучшение стоит больше 2 ч дохода.
        /// Итог — «стена» через 2 ч после последнего доступного улучшения, а не предел 48 ч.
        /// </summary>
        [Test]
        public void StarvedZoneEndsAsWallNotLimit()
        {
            var s = StarvedSession();
            int forge = s.Layout.Cart.Ordinal;
            var def = s.DefFor(s.Layout.Objects[forge]);
            int from = def.Thresholds[2] + 1;
            SetLevel(s, forge, from);
            int target = s.NextThresholdLevel(forge);
            var package = GameNumber.Zero;
            for (int L = from; L < target; L++) package += s.UpgradeCost(forge, L);
            var income = EconomyTables.IncomePerSecondAt(s, forge, from);
            var nextGain = EconomyTables.IncomePerSecondAt(s, forge, from + 1) - income;
            var packageGain = EconomyTables.IncomePerSecondAt(s, forge, target) - income;
            Assume.That((package / packageGain).ToDouble() < (s.UpgradeCost(forge, from) / nextGain).ToDouble(), "пакет до порога выгоднее одного уровня");
            Assume.That((s.UpgradeCost(forge, target) / EconomyTables.IncomePerSecondAt(s, forge, target)).ToDouble() > 2 * 3600, "после порога улучшение дороже 2 ч дохода");
            // До пакета не хватает 10 мин дохода.
            s.Mine.Gold = package - income * GameNumber.FromDouble(600);

            var bot = new ScenarioBot(s);
            var r = bot.PlayZone();
            string log = ScenarioBot.FormatActionLog(new[] { r });
            Assert.AreEqual(BotZoneOutcome.Wall, r.Outcome, log);
            Assert.AreEqual(target, r.ForgeLevel, "пакет до порога куплен\n" + log);
            Assert.That(r.LastImprovementAt, Is.InRange(590.0, 610.0), "последнее улучшение — покупка пакета\n" + log);
            Assert.AreEqual(r.LastImprovementAt + bot.WallSeconds, r.SimSeconds, 1e-9, "стена — через 2 ч после последнего доступного улучшения");
            Assert.Less(r.SimSeconds, bot.ZoneLimitSeconds);
            var result = r.Actions[r.Actions.Count - 1];
            Assert.AreEqual(BotAction.KindResult, result.Kind);
            StringAssert.Contains("«стена»", result.Detail);
            StringAssert.Contains("стена на", ScenarioBot.FormatReport(new[] { r }));
        }

        /// <summary>Голодная зона: zone4, лимит занят котиками уровней 1, 2, … , все цели с недостижимым HP, котики назначены.</summary>
        static GameSession StarvedSession()
        {
            var s = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Debug, 1_000_000L, 4);
            for (int level = 1; s.OccupiedSlots < s.UnitCap(); level++) Assert.IsTrue(s.DebugSpawnCat(level));
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                if (s.Layout.Objects[ord].IsTarget) s.Mine.Objects[ord].Hp = GameNumber.FromDouble(1e30);
            new ScenarioBot(s).AssignIdleCats();
            s.Events.Clear();
            return s;
        }

        /// <summary>Сбор готовой выплаты, ящики и сундуки не считаются улучшением (ТЗ 17.2): при одной лишь выплате улучшений нет.</summary>
        [Test]
        public void PayoutAloneIsNotAnImprovement()
        {
            var s = StarvedSession();
            var bot = new ScenarioBot(s);
            s.Mine.Gold = GameNumber.Zero;
            Assert.IsFalse(bot.ImprovementAvailable(), "голодная зона без золота: улучшений нет");
            var shaft = s.Layout.Shafts[0];
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.IsTrue(bot.ImprovementAvailable(), "расчищенную шахту можно активировать");
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
            // Котики с расчищенной шахты переходят на другие цели; награда шахты не должна дать улучшение карты.
            bot.AssignIdleCats();
            s.Player.Cards.Clear();
            s.Player.Elixir = GameNumber.Zero;
            var gen = s.Mine.Objects[shaft.Ordinal].Gen;
            Assert.IsFalse(gen.Automated);
            s.Advance(s.GeneratorCycleTicks(shaft.Ordinal));
            Assert.IsTrue(gen.PayoutReady);
            s.Mine.Gold = GameNumber.Zero;
            Assert.IsFalse(bot.ImprovementAvailable(), "готовая выплата — не улучшение");
        }

        /// <summary>
        /// Назначение на ближайшую цель (ТЗ 17.2, 7.3): ближайшая свободная рабочая клетка по манхэттенскому расстоянию от котика;
        /// тип цели (шахта, ворота, камень) порядок не задаёт.
        /// </summary>
        [Test]
        public void AssignIdleCatsPicksNearestFreeWorkCell()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 3);
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats[0];
            int expected = NearestFreeWorkCell(s, cat, out int expectedOrd);
            Assert.GreaterOrEqual(expected, 0);
            Assert.IsTrue(new ScenarioBot(s).AssignIdleCats());
            Assert.AreEqual(s.Layout.Objects[expectedOrd].Index, cat.TargetIndex, "цель с ближайшей свободной рабочей клеткой");
            Assert.AreEqual(expected, cat.Cell, "котик стоит на этой рабочей клетке");
            Assert.IsTrue(s.IsWorkCellOf(expectedOrd, cat.Cell));
        }

        static int NearestFreeWorkCell(GameSession s, CatState cat, out int bestOrd)
        {
            var l = s.Layout;
            var work = new List<int>();
            int best = -1, bestDist = int.MaxValue;
            bestOrd = -1;
            for (int ord = 0; ord < l.Objects.Count; ord++)
            {
                if (!l.Objects[ord].IsTarget || !s.IsTargetAvailable(ord)) continue;
                s.WorkCells(ord, work);
                foreach (int w in work)
                {
                    var other = s.CatAt(w);
                    if (other != null && other.Id != cat.Id) continue;
                    int d = Math.Abs(l.Col(w) - l.Col(cat.Cell)) + Math.Abs(l.Row(w) - l.Row(cat.Cell));
                    bool better = best < 0 || d < bestDist || (d == bestDist && (l.Row(w) < l.Row(best) || (l.Row(w) == l.Row(best) && l.Col(w) < l.Col(best))));
                    if (!better) continue;
                    best = w;
                    bestDist = d;
                    bestOrd = ord;
                }
            }
            return best;
        }

        /// <summary>
        /// Самый выгодный генератор (ТЗ 17.2): из «следующего уровня» и «до порога» всех генераторов выбирается наименьшая
        /// окупаемость «цена / прирост дохода в секунду», а не самая низкая цена.
        /// </summary>
        [Test]
        public void BuyUpgradesPicksBestPaybackNotCheapest()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 3);
            var shaft = s.Layout.Shafts.First(x => x.TypeId == "citrine");
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
            int forge = s.Layout.Cart.Ordinal;
            // Кузница L10: следующий уровень дешевле шахты, но даёт меньше дохода на единицу цены.
            SetLevel(s, forge, 10);
            var options = new List<(int ord, int levels, GameNumber cost, GameNumber ratio)>();
            foreach (int ord in new[] { forge, shaft.Ordinal })
            {
                int level = s.Mine.Objects[ord].Gen.Level;
                var now = EconomyTables.IncomePerSecondAt(s, ord, level);
                var cost = s.UpgradeCost(ord, level);
                options.Add((ord, 1, cost, cost / (EconomyTables.IncomePerSecondAt(s, ord, level + 1) - now)));
                int target = s.NextThresholdLevel(ord);
                if (target > level + 1)
                {
                    var total = GameNumber.Zero;
                    for (int L = level; L < target; L++) total += s.UpgradeCost(ord, L);
                    options.Add((ord, target - level, total, total / (EconomyTables.IncomePerSecondAt(s, ord, target) - now)));
                }
            }
            var best = options.OrderBy(o => o.ratio.ToDouble()).First();
            var cheapest = options.OrderBy(o => o.cost.ToDouble()).First();
            Assert.AreNotEqual(best, cheapest, "сценарий различает «дешевле» и «выгоднее»");
            // Золота хватает только на лучший вариант (с резервом на наём): бот берёт его, а не дешёвый.
            var reserve = s.HirePrice() * GameNumber.FromDouble(0.5);
            s.Mine.Gold = best.cost + reserve;
            int levelBefore = s.Mine.Objects[best.ord].Gen.Level;
            int otherOrd = best.ord == forge ? shaft.Ordinal : forge;
            int otherBefore = s.Mine.Objects[otherOrd].Gen.Level;
            Assert.IsTrue(new ScenarioBot(s).BuyUpgrades());
            Assert.AreEqual(levelBefore + best.levels, s.Mine.Objects[best.ord].Gen.Level);
            Assert.AreEqual(otherBefore, s.Mine.Objects[otherOrd].Gen.Level);
        }

        /// <summary>Уровень генератора с порогами, достигнутыми к нему (как после покупки).</summary>
        static void SetLevel(GameSession s, int ord, int level)
        {
            var gen = s.Mine.Objects[ord].Gen;
            var def = s.DefFor(s.Layout.Objects[ord]);
            gen.Level = level;
            gen.ThresholdsReached = def.Thresholds.Count(t => t <= level);
        }

        /// <summary>Лучший вариант пока не по карману — бот копит на него и не покупает худший дешёвый.</summary>
        [Test]
        public void BuyUpgradesSavesForBestOption()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 3);
            var shaft = s.Layout.Shafts.First(x => x.TypeId == "citrine");
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
            int forge = s.Layout.Cart.Ordinal;
            var fg = s.Mine.Objects[forge].Gen;
            SetLevel(s, forge, 10);
            var shaftCost = s.UpgradeCost(shaft.Ordinal, 1);
            var forgeCost = s.UpgradeCost(forge, 10);
            var forgeRatio = forgeCost / (EconomyTables.IncomePerSecondAt(s, forge, 11) - EconomyTables.IncomePerSecondAt(s, forge, 10));
            var shaftRatio = shaftCost / (EconomyTables.IncomePerSecondAt(s, shaft.Ordinal, 2) - EconomyTables.IncomePerSecondAt(s, shaft.Ordinal, 1));
            Assume.That(shaftRatio < forgeRatio && forgeCost < shaftCost, "шахта выгоднее, кузница дешевле");
            var reserve = s.HirePrice() * GameNumber.FromDouble(0.5);
            s.Mine.Gold = forgeCost + reserve;
            Assert.IsFalse(new ScenarioBot(s).BuyUpgrades(), "на кузницу хватает, но выгоднее шахта — бот копит");
            Assert.AreEqual(10, fg.Level);
        }

        /// <summary>
        /// Карты (ТЗ 17.2 — «когда это даёт автоматизацию»): сначала карта токена шахты зоны; прочие полезные карты — только
        /// без траты эликсира, отложенного на следующий шаг автоматизации.
        /// </summary>
        [Test]
        public void CardUpgradesKeepElixirForAutomation()
        {
            var s = GameSession.NewGame(TestData.Balance, 1UL, SaveSlots.Debug, 0L, 5);
            var token = s.Layout.Shafts.First(x => x.TypeId == "amethyst");
            Assert.AreEqual("ca002", token.CardId);
            Assert.AreEqual(4, token.CardLevel);
            foreach (var sh in s.Layout.Shafts)
                if (sh.CardId != token.CardId) s.Player.Cards.Remove(sh.CardId);
            var auto = s.CardStateOf("ca002");
            auto.Level = 3;
            auto.Duplicates = 0;
            var forgeManager = s.CardStateOf("ca001");
            forgeManager.Level = 1;
            forgeManager.Duplicates = 1000;
            Assert.IsTrue(s.TryGetCardUpgradeCost("ca002", out int autoDupes, out var autoElixir));
            Assert.IsTrue(s.TryGetCardUpgradeCost("ca001", out _, out var forgeElixir));
            var bot = new ScenarioBot(s);

            // Копий ca002 не хватает: эликсир на шаг 3→4 отложен, ca001 его не тратит.
            s.Player.Elixir = forgeElixir + autoElixir - GameNumber.One;
            Assert.IsFalse(bot.UpgradeCards());
            Assert.AreEqual(1, s.CardLevel("ca001"));
            s.Player.Elixir = forgeElixir + autoElixir;
            Assert.IsTrue(bot.UpgradeCards());
            Assert.AreEqual(2, s.CardLevel("ca001"), "излишек сверх резерва идёт в доход");
            Assert.AreEqual(3, s.CardLevel("ca002"));

            // Копии появились: эликсира хватает на одну карту — первой идёт автоматизация.
            auto.Duplicates = autoDupes;
            s.Player.Elixir = autoElixir;
            Assert.IsTrue(bot.UpgradeCards());
            Assert.AreEqual(4, s.CardLevel("ca002"));
            Assert.AreEqual(2, s.CardLevel("ca001"));
        }
    }
}
