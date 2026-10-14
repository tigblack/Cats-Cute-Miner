using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Правила обучения zone1: бочки найма (<see cref="GameSession.BarrelHires"/>) и добыча камней на поле
    /// (<see cref="GameSession.LootPickups"/>). Оба переключателя включены, как в игре; по умолчанию они выключены,
    /// и остальные тесты идут по прежним правилам.
    /// </summary>
    public class BarrelLootTests
    {
        const long S = GameSession.TicksPerSecond;
        const long StartUtcMs = 1_000_000L;

        static BalanceRepository B => TestData.Balance;

        static GameSession On(GameSession s)
        {
            s.BarrelHires = true;
            s.LootPickups = true;
            return s;
        }

        static GameSession New(int zone = 1, string slot = SaveSlots.Main) => On(GameSession.NewGame(B, 12345UL, slot, StartUtcMs, zone));

        static GameSession Debug(int zone = 1) => New(zone, SaveSlots.Debug);

        /// <summary>Двойник с прежними правилами (переключатели выключены) — эталон сумм наград.</summary>
        static GameSession OldRulesDebug(int zone = 1) => GameSession.NewGame(B, 12345UL, SaveSlots.Debug, StartUtcMs, zone);

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Restore(string json) => On(GameSession.FromState(B, SaveService.DeserializeState(json)));

        static double LedgerOf(GameSession s, string key) => s.Player.Ledger.TryGetValue(key, out var v) ? v.ToDouble() : 0.0;

        static List<ContainerState> Barrels(GameSession s) => s.Mine.Containers.Where(c => c.Kind == ContainerKind.Hire).ToList();

        static RewardModelDto NextEntry(GameSession s, ZoneObject obj)
        {
            var rock = B.Rocks[obj.TypeId];
            s.Player.RewardCycle.TryGetValue(rock.Id, out long c);
            return rock.RewardCycle[(int)(c % rock.RewardCycle.Length)];
        }

        /// <summary>Целый камень, следующая награда которого — валюта detailedType; available — только допустимые цели. null — нет.</summary>
        static ZoneObject RockWithNext(GameSession s, string detailedType, bool available = true) =>
            s.Layout.Objects.FirstOrDefault(o => o.Kind == ObjectKind.Rock && s.Mine.Objects[o.Ordinal].Status == ObjectStatus.Intact &&
                                                 (!available || s.IsTargetAvailable(o.Ordinal)) &&
                                                 NextEntry(s, o).RewardType == RewardTypes.Currency && NextEntry(s, o).DetailedType == detailedType);

        static int Manhattan(GameSession s, int a, int b) =>
            Math.Abs(s.Layout.Col(a) - s.Layout.Col(b)) + Math.Abs(s.Layout.Row(a) - s.Layout.Row(b));

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

        // ───────────────────────────── Бочки найма ─────────────────────────────

        [Test]
        public void SwitchesAreOffByDefault()
        {
            var s = GameSession.NewGame(B, 12345UL, SaveSlots.Main, StartUtcMs, 1);
            Assert.IsFalse(s.BarrelHires, "по умолчанию наём даёт котика сразу");
            Assert.IsFalse(s.LootPickups, "по умолчанию награда камня начисляется сразу");
            Assert.AreEqual(30.0, GameSession.LootAutoCollectSeconds);
            Assert.AreEqual(30 * S, GameSession.LootAutoCollectTicks);
            Assert.AreEqual(2, (int)ContainerKind.Hire);
            Assert.IsEmpty(s.Mine.Loot);
            Assert.AreEqual(1, s.Mine.NextLootId);
        }

        [Test]
        public void HireSpendsGoldAndPutsBarrelInMergeZoneSpawnCell()
        {
            var s = New();
            int cell = s.FindSpawnCell();
            Assert.IsTrue(s.MergeRows(out int upper, out int lower), "в zone1 есть зона мержа");
            int row = s.Layout.Row(cell);
            Assert.IsTrue(row >= upper && row <= lower, "клетка появления — в зоне мержа (6.6)");
            var price = s.HirePrice();
            int level = s.HireLevel();
            var gold = s.Mine.Gold;
            Assert.IsTrue(price.IsPositive);
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.Hire());

            Assert.AreEqual((gold - price).ToDouble(), s.Mine.Gold.ToDouble(), "списана цена найма");
            Assert.AreEqual(price.ToDouble(), LedgerOf(s, "gold:spent:hire"), "журнал трат найма");
            Assert.AreEqual(1, s.Mine.HireCount, "счётчик найма +1");
            Assert.AreEqual(1, s.Mine.CatsHired, "статистика найма +1");
            Assert.AreEqual(0, s.Mine.Cats.Count, "котика нет, пока бочка не открыта");
            var barrel = Barrels(s).Single();
            Assert.AreEqual(cell, barrel.Cell, "бочка — в клетке появления");
            Assert.AreEqual(level, barrel.CatLevel, "в бочке котик уровня найма");
            Assert.IsTrue(barrel.HoldsCat);
            Assert.AreSame(barrel, s.ContainerAt(cell));
            Assert.AreEqual(1, s.OccupiedSlots, "бочка занимает слот котика");
            Assert.IsFalse(s.IsCellFree(cell), "клетка бочки занята");
            Assert.AreNotEqual(cell, s.FindSpawnCell(), "следующая клетка появления — другая");

            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.CatSpawned), "CatSpawned при найме нет");
            var ev = s.Events.Single(e => e.Type == SimEventType.ContainerSpawned);
            Assert.AreEqual("Hire", ev.Text);
            Assert.AreEqual(barrel.Id, ev.CatId, "CatId — ID бочки");
            Assert.AreEqual(cell, ev.Cell);
            Assert.AreEqual(-1, ev.ObjectIndex);
            Assert.Greater(ev.OpId, 0L, "наём — операция с ID");
            Assert.AreEqual(price.ToDouble(), ev.Amount.ToDouble(), "Amount — цена найма");

            // Бочка сама не открывается.
            s.Advance(600 * S);
            Assert.AreEqual(barrel.Id, Barrels(s).Single().Id, "бочка на месте через 10 мин");
            Assert.AreEqual(0, s.Mine.Cats.Count);
        }

        [Test]
        public void OpenBarrelSpawnsIdleCatOfHireLevelInBarrelCell()
        {
            var s = New();
            int level = s.HireLevel();
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var barrel = Barrels(s).Single();
            int cell = barrel.Cell;
            long freeTimer = s.Mine.FreeCatTimerTicks;
            int claims = s.Player.DeliveryHistory.ClaimTimes.Count;
            var gold = s.Mine.Gold;
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(barrel.Id));

            Assert.IsEmpty(Barrels(s), "бочка снята с поля");
            var cat = s.Mine.Cats.Single();
            Assert.AreEqual(level, cat.Level, "котик уровня найма");
            Assert.AreEqual(cell, cat.Cell, "котик в клетке бочки");
            Assert.AreEqual(-1, cat.TargetIndex, "котик в Idle");
            Assert.AreSame(cat, s.CatAt(cell));
            Assert.AreEqual(gold.ToDouble(), s.Mine.Gold.ToDouble(), "открытие бесплатно: золото списано при найме");
            Assert.AreEqual(freeTimer, s.Mine.FreeCatTimerTicks, "таймер бесплатного котика не сбрасывается");
            Assert.AreEqual(claims, s.Player.DeliveryHistory.ClaimTimes.Count, "не доставка");
            Assert.AreEqual(1, s.Mine.HireCount);
            Assert.AreEqual(1, s.Mine.CatsHired);
            Assert.AreEqual(1, s.OccupiedSlots);

            var spawned = s.Events.Single(e => e.Type == SimEventType.CatSpawned);
            Assert.AreEqual("hire", spawned.Text);
            Assert.AreEqual(cat.Id, spawned.CatId);
            Assert.AreEqual(cell, spawned.Cell);
            Assert.IsTrue(spawned.Amount.IsZero, "Amount 0: цена уже показана при найме");
            var opened = s.Events.Single(e => e.Type == SimEventType.ContainerOpened);
            Assert.AreEqual("Hire", opened.Text);
            Assert.AreEqual(barrel.Id, opened.CatId);
            Assert.AreEqual(cell, opened.Cell);
            Assert.AreEqual(spawned.OpId, opened.OpId, "одна транзакция");
            Assert.Less(s.Events.IndexOf(spawned), s.Events.IndexOf(opened), "CatSpawned, затем ContainerOpened");

            Assert.AreEqual(CommandResult.NotFound, s.OpenContainer(barrel.Id), "повторно не открывается");
            Assert.AreEqual(1, s.Mine.Cats.Count);
        }

        [Test]
        public void CatCapCountsUnopenedBarrels()
        {
            var s = Debug(1);
            s.DebugAddGold(GameNumber.FromDouble(1e12));
            int cap = s.UnitCap();
            Assert.Greater(cap, 1);
            for (int i = 0; i < 100 && s.CanHire() == CommandResult.Ok; i++) Assert.AreEqual(CommandResult.Ok, s.Hire());

            Assert.AreEqual(CommandResult.CapReached, s.CanHire(), "лимит достигнут одними бочками");
            Assert.AreEqual(cap, Barrels(s).Count);
            Assert.AreEqual(0, s.Mine.Cats.Count);
            Assert.AreEqual(cap, s.OccupiedSlots);
            Assert.AreEqual(cap, s.Mine.CatsHired);
            Assert.AreEqual(cap, Barrels(s).Select(b => b.Cell).Distinct().Count(), "у каждой бочки своя клетка");
            var gold = s.Mine.Gold;
            Assert.AreEqual(CommandResult.CapReached, s.Hire());
            Assert.AreEqual(gold.ToDouble(), s.Mine.Gold.ToDouble(), "отказ ничего не списывает");

            // Полный лимит останавливает таймер бесплатного котика (как и котики).
            s.Advance(3600 * S);
            Assert.IsFalse(s.Mine.Containers.Any(c => c.Kind == ContainerKind.FreeCat), "бесплатный котик не появляется при полном лимите");

            // Открытая бочка — тот же слот.
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(Barrels(s)[0].Id));
            Assert.AreEqual(cap, s.OccupiedSlots);
            Assert.AreEqual(CommandResult.CapReached, s.CanHire());
        }

        // ───────────────────────────── Добыча камней ─────────────────────────────

        [Test]
        public void DestroyedRockDropsLootAndGrantsNoGoldYet()
        {
            var s = Debug(1);
            var twin = OldRulesDebug(1);
            var rock = RockWithNext(s, "Core");
            Assert.IsNotNull(rock, "в zone1 есть камень с золотом");
            var gold = s.Mine.Gold;
            double ledger = LedgerOf(s, "gold:rock");
            s.Events.Clear();
            twin.Events.Clear();

            s.DebugCompleteTarget(rock.Ordinal);
            twin.DebugCompleteTarget(rock.Ordinal);

            var granted = twin.Events.Single(e => e.Type == SimEventType.RewardGranted);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[rock.Ordinal].Status);
            Assert.AreEqual(1, s.Mine.RocksDestroyed);
            Assert.AreEqual(gold.ToDouble(), s.Mine.Gold.ToDouble(), "золото ещё не начислено");
            Assert.AreEqual(ledger, LedgerOf(s, "gold:rock"), "журнал камня не тронут");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.RewardGranted), "RewardGranted — только при сборе");

            var loot = s.Mine.Loot.Single();
            Assert.AreEqual(1, loot.Id);
            Assert.AreEqual(2, s.Mine.NextLootId);
            Assert.AreEqual(rock.Index, loot.Cell, "добыча — в клетке якоря камня");
            Assert.AreEqual(rock.Index, loot.SourceIndex);
            Assert.AreEqual(LootState.Gold, loot.Currency);
            Assert.AreEqual(granted.Amount.ToDouble(), loot.Amount.ToDouble(), "сумма та же, что при мгновенном начислении");
            Assert.AreEqual(0L, loot.AgeTicks);
            Assert.AreSame(loot, s.FindLoot(loot.Id));
            Assert.IsNull(s.FindLoot(loot.Id + 100));
            Assert.AreEqual(s.IsCellOpen(loot.Cell), s.IsCellFree(loot.Cell), "добыча клетку не занимает");

            var destroyed = s.Events.Single(e => e.Type == SimEventType.RockDestroyed);
            var drop = s.Events.Single(e => e.Type == SimEventType.LootDropped);
            Assert.AreEqual(loot.Id, drop.CatId, "CatId — ID добычи");
            Assert.AreEqual(loot.Cell, drop.Cell);
            Assert.AreEqual(rock.Index, drop.ObjectIndex);
            Assert.AreEqual(loot.Amount.ToDouble(), drop.Amount.ToDouble());
            Assert.AreEqual("gold", drop.Text);
            Assert.AreEqual(destroyed.OpId, drop.OpId, "выпадение — в транзакции разрушения");
            Assert.Less(s.Events.IndexOf(destroyed), s.Events.IndexOf(drop));
        }

        [Test]
        public void CollectLootCreditsExactAmountWithRockLedgerKey()
        {
            var s = Debug(1);
            var twin = OldRulesDebug(1);
            var rock = RockWithNext(s, "Core");
            s.DebugCompleteTarget(rock.Ordinal);
            twin.DebugCompleteTarget(rock.Ordinal);
            var loot = s.Mine.Loot.Single();
            var gold = s.Mine.Gold;
            double ledger = LedgerOf(s, "gold:rock");
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.CollectLoot(loot.Id));

            Assert.AreEqual((gold + loot.Amount).ToDouble(), s.Mine.Gold.ToDouble(), "начислена ровно сумма добычи");
            Assert.AreEqual(ledger + loot.Amount.ToDouble(), LedgerOf(s, "gold:rock"), "ключ журнала gold:rock");
            Assert.AreEqual(twin.Mine.Gold.ToDouble(), s.Mine.Gold.ToDouble(), "итог как при мгновенном начислении");
            CollectionAssert.AreEquivalent(twin.Player.Ledger.ToDictionary(kv => kv.Key, kv => kv.Value.ToDouble()),
                s.Player.Ledger.ToDictionary(kv => kv.Key, kv => kv.Value.ToDouble()), "журнал как при мгновенном начислении");
            Assert.IsEmpty(s.Mine.Loot);
            Assert.IsNull(s.FindLoot(loot.Id));
            Assert.AreEqual(1, s.Mine.LootCollected);

            var granted = s.Events.Single(e => e.Type == SimEventType.RewardGranted);
            Assert.AreEqual(rock.Index, granted.ObjectIndex);
            Assert.AreEqual(loot.Amount.ToDouble(), granted.Amount.ToDouble());
            Assert.AreEqual("gold", granted.Text);
            var collected = s.Events.Single(e => e.Type == SimEventType.LootCollected);
            Assert.AreEqual(loot.Id, collected.CatId);
            Assert.AreEqual(loot.Cell, collected.Cell);
            Assert.AreEqual(rock.Index, collected.ObjectIndex);
            Assert.AreEqual(loot.Amount.ToDouble(), collected.Amount.ToDouble());
            Assert.AreEqual("gold", collected.Text);
            Assert.AreEqual(0, collected.OtherCatId, "сбор тапом");
            Assert.Greater(collected.OpId, 0L);
            Assert.AreEqual(granted.OpId, collected.OpId, "одна транзакция");

            var after = s.Mine.Gold;
            Assert.AreEqual(CommandResult.NotFound, s.CollectLoot(loot.Id), "повторный сбор");
            Assert.AreEqual(after.ToDouble(), s.Mine.Gold.ToDouble());
        }

        [Test]
        public void LootAutoCollectsAfterThirtySecondsAndNotBefore()
        {
            var s = Debug(1);
            var rock = RockWithNext(s, "Core");
            s.DebugCompleteTarget(rock.Ordinal);
            var loot = s.Mine.Loot.Single();
            double ledger = LedgerOf(s, "gold:rock");

            s.Advance(GameSession.LootAutoCollectTicks - 1);
            Assert.AreSame(loot, s.Mine.Loot.Single(), "до 30 с добыча лежит");
            Assert.AreEqual(GameSession.LootAutoCollectTicks - 1, loot.AgeTicks, "возраст — игровое время");
            Assert.AreEqual(ledger, LedgerOf(s, "gold:rock"));
            Assert.AreEqual(0, s.Mine.LootCollected);

            s.Events.Clear();
            s.Advance(1);
            Assert.IsEmpty(s.Mine.Loot, "ровно через 30 с — автосбор");
            Assert.AreEqual(ledger + loot.Amount.ToDouble(), LedgerOf(s, "gold:rock"));
            Assert.AreEqual(1, s.Mine.LootCollected);
            var collected = s.Events.Single(e => e.Type == SimEventType.LootCollected);
            Assert.AreEqual(1, collected.OtherCatId, "автосбор");
            Assert.AreEqual(loot.Id, collected.CatId);
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.RewardGranted && e.ObjectIndex == rock.Index && e.Text == "gold"));

            // Advance(a) + Advance(b) ≡ Advance(a + b) с добычей.
            var one = Debug(1);
            var split = Debug(1);
            one.DebugCompleteTarget(rock.Ordinal);
            split.DebugCompleteTarget(rock.Ordinal);
            one.Advance(45 * S);
            split.Advance(10 * S);
            split.Advance(20 * S - 7);
            split.Advance(15 * S + 7);
            Assert.AreEqual(Snapshot(one), Snapshot(split));
        }

        [Test]
        public void CompleteZoneCarriesElixirLootAndDropsGoldLoot()
        {
            GameSession s = null;
            LootState elixirLoot = null, goldLoot = null;
            for (int zone = 1; zone <= B.ZoneCount && s == null; zone++)
            {
                var c = Debug(zone);
                var soft = RockWithNext(c, "Soft", false);
                if (soft == null) continue;
                c.DebugCompleteTarget(soft.Ordinal);
                var core = RockWithNext(c, "Core", false);
                if (core == null) continue;
                c.DebugCompleteTarget(core.Ordinal);
                s = c;
                elixirLoot = c.Mine.Loot.Single(l => l.Currency == LootState.Elixir);
                goldLoot = c.Mine.Loot.Single(l => l.Currency == LootState.Gold);
            }
            Assert.IsNotNull(s, "есть зона с камнями эликсира и золота");
            Assert.AreEqual(2, s.Mine.Loot.Count);
            int zoneBefore = s.Mine.Zone;
            ClearMandatory(s);
            Assert.AreEqual(2, s.Mine.Loot.Count, "добыча ждёт на поле");

            double elixir = s.Player.Elixir.ToDouble();
            var elixirLedger = s.Player.Ledger.Where(kv => kv.Key.StartsWith("elixir:", StringComparison.Ordinal) && !kv.Key.StartsWith("elixir:spent", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToDouble());
            double rockElixir = LedgerOf(s, "elixir:rock");
            double rockGold = LedgerOf(s, "gold:rock");
            s.Events.Clear();

            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());

            Assert.AreEqual(rockElixir + elixirLoot.Amount.ToDouble(), LedgerOf(s, "elixir:rock"), 1e-9, "эликсир добычи переносится");
            Assert.AreEqual(rockGold, LedgerOf(s, "gold:rock"), "золото добычи остаётся в зоне");
            double credited = s.Player.Ledger.Where(kv => kv.Key.StartsWith("elixir:", StringComparison.Ordinal) && !kv.Key.StartsWith("elixir:spent", StringComparison.Ordinal))
                .Sum(kv => kv.Value.ToDouble() - (elixirLedger.TryGetValue(kv.Key, out double was) ? was : 0.0));
            Assert.AreEqual(credited, s.Player.Elixir.ToDouble() - elixir, 1e-6, "эликсир игрока = сумма начислений журнала");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.LootCollected), "перенос без событий сбора");
            if (zoneBefore < B.ZoneCount)
            {
                Assert.AreEqual(zoneBefore + 1, s.Mine.Zone);
                Assert.AreEqual(B.Props.StartingCoreCurrency, s.Mine.Gold.ToDouble(), "золото не переносится");
            }
            Assert.IsEmpty(s.Mine.Loot, "в новой зоне добычи нет");
            Assert.AreEqual(1, s.Mine.NextLootId);
        }

        [Test]
        public void HardLootCreditsHardCurrencyAndCarriesOver()
        {
            GameSession s = null;
            ZoneObject rock = null;
            for (int zone = 1; zone <= B.ZoneCount && s == null; zone++)
            {
                var c = Debug(zone);
                rock = RockWithNext(c, "Hard", false);
                if (rock != null) s = c;
            }
            Assert.IsNotNull(s, "есть зона с камнем кристаллов");
            double quantity = NextEntry(s, rock).Quantity;
            double hard = s.Player.Hard.ToDouble();
            s.DebugCompleteTarget(rock.Ordinal);
            var loot = s.Mine.Loot.Single();
            Assert.AreEqual(LootState.Hard, loot.Currency);
            Assert.AreEqual(quantity, loot.Amount.ToDouble());
            Assert.AreEqual(hard, s.Player.Hard.ToDouble(), "кристаллы — при сборе");
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.CollectLoot(loot.Id));
            Assert.AreEqual(hard + quantity, s.Player.Hard.ToDouble());
            Assert.AreEqual(quantity, LedgerOf(s, "hard:rock"));
            Assert.AreEqual("hard", s.Events.Single(e => e.Type == SimEventType.RewardGranted).Text);

            // Несобранные кристаллы переносятся при CompleteZone.
            var again = RockWithNext(s, "Hard", false);
            if (again == null) return;
            s.DebugCompleteTarget(again.Ordinal);
            double carried = s.Mine.Loot.Single().Amount.ToDouble();
            ClearMandatory(s);
            double before = s.Player.Hard.ToDouble();
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            Assert.AreEqual(before + carried, s.Player.Hard.ToDouble(), "кристаллы добычи переносятся");
            Assert.AreEqual(quantity + carried, LedgerOf(s, "hard:rock"));
        }

        // ───────────────────────────── Сохранение и офлайн ─────────────────────────────

        [Test]
        public void SaveLoadRoundTripsBarrelsAndLoot()
        {
            var s = Debug(1);
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var rock = RockWithNext(s, "Core");
            s.DebugCompleteTarget(rock.Ordinal);
            s.Advance(5 * S);
            var barrel = Barrels(s).Single();
            var loot = s.Mine.Loot.Single();
            string json = Snapshot(s);

            var r = Restore(json);
            Assert.AreEqual(json, Snapshot(r), "сохранить → загрузить → сохранить без изменений");
            var rb = Barrels(r).Single();
            Assert.AreEqual(barrel.Id, rb.Id);
            Assert.AreEqual(barrel.Cell, rb.Cell);
            Assert.AreEqual(barrel.CatLevel, rb.CatLevel);
            Assert.AreSame(rb, r.ContainerAt(rb.Cell), "клетка бочки занята после загрузки");
            Assert.AreEqual(1, r.OccupiedSlots);
            var rl = r.Mine.Loot.Single();
            Assert.AreEqual(loot.Id, rl.Id);
            Assert.AreEqual(loot.Cell, rl.Cell);
            Assert.AreEqual(loot.Currency, rl.Currency);
            Assert.AreEqual(loot.Amount.ToDouble(), rl.Amount.ToDouble());
            Assert.AreEqual(5 * S, rl.AgeTicks);
            Assert.AreEqual(loot.SourceIndex, rl.SourceIndex);
            Assert.AreEqual(s.Mine.NextLootId, r.Mine.NextLootId);

            // Продолжение после загрузки совпадает с непрерывной игрой.
            s.Advance(40 * S);
            r.Advance(40 * S);
            Assert.IsEmpty(r.Mine.Loot, "автосбор после загрузки");
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(barrel.Id));
            Assert.AreEqual(CommandResult.Ok, r.OpenContainer(barrel.Id));
            Assert.AreEqual(Snapshot(s), Snapshot(r));

            // Через файл сохранения (конверт, SHA-256).
            string dir = Path.Combine(Path.GetTempPath(), "catminers-barrel-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = Restore(json);
                var svc = new SaveService(dir);
                svc.Save(t.State, StartUtcMs);
                var loaded = new SaveService(dir).Load(SaveSlots.Debug, B.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status, loaded.Message);
                Assert.AreEqual(json, SaveService.SerializeState(loaded.State));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void OldSaveWithoutLootFieldsLoadsWithEmptyLoot()
        {
            var doc = JObject.Parse(Snapshot(Debug(1)));
            var mine = (JObject)doc["Mine"];
            Assert.IsNotNull(mine["Loot"]);
            mine.Remove("Loot");
            mine.Remove("NextLootId");
            mine.Remove("LootCollected");
            var old = On(GameSession.FromState(B, SaveService.DeserializeState(doc.ToString(Newtonsoft.Json.Formatting.None))));
            Assert.IsNotNull(old.Mine.Loot);
            Assert.IsEmpty(old.Mine.Loot);
            Assert.AreEqual(1, old.Mine.NextLootId);
            Assert.AreEqual(0, old.Mine.LootCollected);

            mine["Loot"] = null;
            var nulled = On(GameSession.FromState(B, SaveService.DeserializeState(doc.ToString(Newtonsoft.Json.Formatting.None))));
            Assert.IsNotNull(nulled.Mine.Loot, "Loot: null — пустой список");

            // Добыча после загрузки старого сохранения работает.
            var rock = RockWithNext(old, "Core");
            old.DebugCompleteTarget(rock.Ordinal);
            Assert.AreEqual(1, old.Mine.Loot.Single().Id);
        }

        [Test]
        public void OfflineProgressAutoCollectsLoot()
        {
            var s = Debug(1);
            var rock = RockWithNext(s, "Core");
            s.DebugCompleteTarget(rock.Ordinal);
            var loot = s.Mine.Loot.Single();
            var gold = s.Mine.Gold;
            double ledger = LedgerOf(s, "gold:rock");

            var summary = OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 60_000L);

            Assert.IsFalse(s.OfflineMode);
            Assert.IsEmpty(s.Mine.Loot, "офлайн — автосбор");
            Assert.AreEqual(ledger + loot.Amount.ToDouble(), LedgerOf(s, "gold:rock"));
            Assert.AreEqual((s.Mine.Gold - gold).ToDouble(), summary.Gold.ToDouble(), "окно офлайна — разница золота");
            Assert.GreaterOrEqual(summary.Gold.ToDouble(), loot.Amount.ToDouble(), "в окне есть золото добычи");
            Assert.IsTrue(summary.HasNews);
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.LootCollected && e.OtherCatId == 1 && e.CatId == loot.Id));
        }

        // ───────────────────────────── Сценарий обучения zone1 ─────────────────────────────

        /// <summary>
        /// t1 наём → t1b бочка → t2 котик на ближайший камень → t2b сбор добычи → t3 второй наём, на настоящих командах (слот Main).
        /// Попутно: длинный офлайн с работающим котиком собирает выпавшую добычу; Advance по шагам ≡ одним вызовом.
        /// </summary>
        [Test]
        public void TutorialFlowHireOpenAssignCollectHireAgain()
        {
            var s = New(1);
            Assert.AreEqual(CommandResult.Ok, s.CanHire(), "t1: первый наём по карману");
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var barrel = Barrels(s).Single();
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(barrel.Id));
            var cat = s.Mine.Cats.Single();

            var rock = s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock && s.AcceptsCat(o.Ordinal, cat.Id))
                .OrderBy(o => Manhattan(s, o.Index, cat.Cell)).ThenBy(o => o.Index).FirstOrDefault();
            Assert.IsNotNull(rock, "t2: есть доступный камень");
            var entry = NextEntry(s, rock);
            var outcome = s.DropCat(cat.Id, rock.Col + 0.5, rock.Row + 0.5);
            Assert.AreEqual(CommandResult.Ok, outcome.Result);
            Assert.AreEqual(rock.Index, cat.TargetIndex, "котик бьёт камень");

            var twin = Restore(Snapshot(s));
            for (int i = 0; i < 100_000 && s.Mine.RocksDestroyed == 0; i++) s.Advance(S / 10);
            Assert.AreEqual(1, s.Mine.RocksDestroyed, "камень разбит");
            long elapsed = s.Player.PlayerTimeTicks - twin.Player.PlayerTimeTicks;
            twin.Advance(elapsed);
            Assert.AreEqual(Snapshot(s), Snapshot(twin), "Advance по шагам ≡ одним вызовом");
            TestContext.WriteLine("zone1: камень " + rock.TypeId + " разбит за " + GameSession.TicksToSeconds(elapsed) + " с, награда " + entry.DetailedType);

            if (entry.RewardType == RewardTypes.Currency && entry.DetailedType == "Core")
            {
                var loot = s.Mine.Loot.Single();
                Assert.AreEqual(LootState.Gold, loot.Currency);
                Assert.AreEqual(CommandResult.Ok, s.CollectLoot(loot.Id), "t2b: сбор добычи");
                Assert.AreEqual(1, s.Mine.LootCollected);
            }
            TestContext.WriteLine("zone1: золото " + s.Mine.Gold + ", цена второго найма " + s.HirePrice());
            if (s.CanHire() == CommandResult.Ok)
            {
                Assert.AreEqual(CommandResult.Ok, s.Hire(), "t3: второй наём");
                Assert.AreEqual(2, s.Mine.CatsHired);
                Assert.AreEqual(1, Barrels(s).Count);
                Assert.AreEqual(2, s.OccupiedSlots);
            }

            // Офлайн 8 ч с работающим котиком: выпавшая добыча собирается автосбором.
            var next = s.Layout.Objects.Where(o => o.Kind == ObjectKind.Rock && s.AcceptsCat(o.Ordinal, cat.Id)).OrderBy(o => o.Index).FirstOrDefault();
            if (next != null)
            {
                Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, next.Col + 0.5, next.Row + 0.5).Result);
                int rocks = s.Mine.RocksDestroyed;
                OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 8L * 3600L * 1000L);
                Assert.Greater(s.Mine.RocksDestroyed, rocks, "офлайн котик разбивает камни");
                Assert.IsEmpty(s.Mine.Loot, "вся добыча старше 30 с собрана");
            }
        }
    }
}
