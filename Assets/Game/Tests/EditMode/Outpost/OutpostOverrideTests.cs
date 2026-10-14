using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Именованные override зон (ТЗ 8.5): override арены zone1 совпадает с независимым аудитом, только добавляет цели на свободные
    /// клетки, отвергает пересечения/выход/строку ворот; run хранит копию override, карта сохранённой шахты строится по ней;
    /// незавершённая шахта со старой геометрией перезапускается с сохранением профиля.
    /// </summary>
    public class OutpostOverrideTests
    {
        static JObject EffectiveAudit => (JObject)Audit["level_overrides"]["zone1"]["effective"];

        static OutpostMap Arena() => OutpostLevelAdapter.Build(TestData.Balance, 1, Configs.Overrides);

        // ───────────────────────────── Override арены zone1 ─────────────────────────────

        [Test]
        public void Zone1Arena_EffectiveMap_MatchesIndependentAudit()
        {
            var map = Arena();
            var audit = EffectiveAudit;
            Assert.AreEqual("zone1-full-arena-1", map.OverrideName);
            Assert.AreEqual((int)audit["obligatory_targets"], map.RequiredCount);
            Assert.AreEqual(36, map.Targets.Count(t => t.Added));
            var balances = OutpostLevelBalanceAdapter.Compute(map, Configs.Balance, TestData.Balance);
            Assert.AreEqual((long)audit["hp"], balances.Sum(b => b.MaxHp), "Σ HP");
            Assert.AreEqual((int)audit["cargo_units"], balances.Sum(b => b.TotalCargoUnits), "Σ порций");
            Assert.AreEqual((long)audit["min_adapted_hp"], balances.Min(b => b.MaxHp));
            Assert.AreEqual((long)audit["max_adapted_hp"], balances.Max(b => b.MaxHp));
            var gold = BigInteger.Zero;
            foreach (var b in balances) gold += b.TotalCargoUnits * (b.ValueFactor == Rational.One ? 5 : 3);
            Assert.AreEqual(AuditBig(audit["base_gold"]), gold, "Σ базового золота (r — 5, x — 3)");
            var reach = OutpostReachability.Check(map);
            Assert.IsTrue(reach.Passed, reach.Diagnostic);
            Assert.AreEqual(map.RequiredCount, reach.Cleared, "все цели расчищаются, ворот нет");
            Assert.AreEqual((int)audit["clearance_waves"], reach.Waves, "волны — копия алгоритма аудита");
        }

        [Test]
        public void Zone1Arena_KeepsSourceObjects_AddsOnlyOnFreeCells()
        {
            var source = Map(1);
            var arena = Arena();
            foreach (var t in source.Targets)
            {
                var e = arena.Target(t.Id);
                Assert.IsNotNull(e, "исходная цель " + t.Id);
                Assert.AreEqual(t.Kind, e.Kind);
                Assert.AreEqual(t.TypeId, e.TypeId);
                Assert.AreEqual(t.SourceLevel, e.SourceLevel);
                CollectionAssert.AreEqual(t.Cells, e.Cells, "footprint цели " + t.Id);
                Assert.IsFalse(e.Added);
            }
            Assert.AreEqual(source.ExitCell, arena.ExitCell);
            Assert.AreEqual(source.SpawnCell, arena.SpawnCell);
            CollectionAssert.AreEqual(source.DepotCells, arena.DepotCells);
            foreach (var t in arena.Targets.Where(t => t.Added))
                foreach (int c in t.Cells)
                {
                    Assert.AreEqual(-1, source.InitialOccupant[c], "добавленная цель " + t.Id + " — на свободной клетке " + c);
                    Assert.AreNotEqual(arena.ExitCell, c);
                }
            // Ряды 5–10 заполнены: свободных клеток там больше нет (ряд 0 — выход и проход к нему).
            for (int row = 5; row <= 10; row++)
                for (int col = 0; col < 7; col++)
                    Assert.GreaterOrEqual(arena.InitialOccupant[Cell(arena, col, row)], 0, "клетка (" + col + ", " + row + ")");
        }

        // ───────────────────────────── Проверки override ─────────────────────────────

        static BalanceRepository GatedRepo()
        {
            // Синтетическая zone1: выход (3,0), ворота в строке 2, старт (0,4).
            var repo = BalanceRepository.Load(TestData.BalanceBytes);
            var rows = new[]
            {
                ". . . e:exit:1:x . . .",
                ". . . . . . .",
                "p:checkpoint:1:g . . . . . .",
                ". . . r:rockbasic:1 . . .",
                "c:spawningcart:1 . . . . . .",
            };
            ApplyGrid(repo, 1, string.Join(",", rows.SelectMany(r => r.Split(' '))), rows.Length);
            return repo;
        }

        static ZoneOverride One(int col, int row, string token) => new ZoneOverride
        {
            Name = "test", Reason = "test",
            AddTokens = new List<OverrideToken> { new OverrideToken { Column = col, Row = row, Token = token } },
        };

        static void Rejected(BalanceRepository repo, ZoneOverride ov, string fragment)
        {
            var e = Assert.Throws<OutpostDataException>(() => SourceLevelImporter.Import(repo, 1, ov));
            StringAssert.Contains(fragment, e.Message);
        }

        [Test]
        public void Import_RejectsAnchorOnToken_Overlap_Exit_GateRow_Bounds_UnknownType()
        {
            var repo = GatedRepo();
            Rejected(repo, One(3, 3, "r:rockbasic:1"), "якорь занят");
            Rejected(repo, One(2, 3, "x:block2x1"), "пересекается");
            Rejected(repo, One(2, 0, "x:block2x1"), "закрывает выход");
            Rejected(repo, One(5, 2, "r:rockbasic:1"), "строке ворот");
            Rejected(repo, One(5, 3, "x:block1x2"), "строке ворот");
            Rejected(repo, One(7, 1, "r:rockbasic:1"), "вне поля");
            Rejected(repo, One(4, 1, "r:nosuchrock:1"), "нет Rocks.nosuchrock");
            Rejected(repo, One(0, 0, "x:block2x2"), "footprint вне поля");
            var ok = SourceLevelImporter.Import(repo, 1, One(5, 1, "x:block2x1"));
            Assert.AreEqual("x:block2x1", ok.Override.AddTokens[0].Token);
            Assert.IsTrue(ok.Tokens[Cell(Map(1), 5, 1)].Added);
        }

        [Test]
        public void Parse_RequiresNameReason_TargetKinds_UniqueAnchors_ZoneIds()
        {
            string Doc(string zone, string body) => "{\"Version\":\"t\",\"Zones\":{\"" + zone + "\":" + body + "}}";
            const string good = "{\"Name\":\"n\",\"Reason\":\"r\",\"AddTokens\":[{\"Column\":1,\"Row\":2,\"Token\":\"r:rockbasic:1\"}]}";
            Assert.AreEqual(1, LevelOverrides.Parse(Doc("zone7", good)).For("zone7").AddTokens.Count);
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(Doc("zone7", "{\"Name\":\"n\",\"AddTokens\":[]}")), "нет причины");
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(Doc("zone7",
                "{\"Name\":\"n\",\"Reason\":\"r\",\"AddTokens\":[{\"Column\":1,\"Row\":2,\"Token\":\"p:checkpoint:1:g\"}]}")), "ворота не добавляются");
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(Doc("zone7",
                "{\"Name\":\"n\",\"Reason\":\"r\",\"AddTokens\":[{\"Column\":1,\"Row\":2,\"Token\":\"r:a:1\"},{\"Column\":1,\"Row\":2,\"Token\":\"r:b:1\"}]}")),
                "повтор якоря");
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(Doc("zone07", good)), "ID зоны");
            Assert.Throws<OutpostConfigException>(() => LevelOverrides.Parse(Doc("zone0", good)));
        }

        [Test]
        public void Build_FromImportWithoutConfiguredOverride_Throws()
        {
            Assert.Throws<ArgumentException>(() => OutpostLevelAdapter.Build(SourceLevelImporter.Import(TestData.Balance, 1), Configs.Overrides));
            Assert.AreEqual(Arena().RequiredCount,
                OutpostLevelAdapter.Build(SourceLevelImporter.Import(TestData.Balance, 1, Configs.Overrides.For("zone1")), Configs.Overrides).RequiredCount);
        }

        // ───────────────────────────── Run и сохранение ─────────────────────────────

        [Test]
        public void Run_StoresOverrideCopy_ForRunRebuildsSameMap_SimRejectsOtherGeometry()
        {
            var st = StageProgressionService.NewGame(TestData.Balance, Configs, Seed, StartUtcMs, 1);
            Assert.AreEqual("zone1-full-arena-1", st.Run.MapOverride.Name);
            Assert.AreNotSame(Configs.Overrides.For("zone1"), st.Run.MapOverride, "копия, а не ссылка на конфигурацию");
            Assert.IsTrue(StageProgressionService.IsRunMapCurrent(st.Run, Configs));
            var loaded = FromJson(Json(st));
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, loaded.Run);
            Assert.AreEqual(Arena().RequiredCount, map.RequiredCount);
            CollectionAssert.IsEmpty(OutpostInvariants.Check(loaded, map, Configs.Equipment));
            Assert.Throws<ArgumentException>(() => new OutpostSimulation(loaded, Map(1), Configs), "исходная карта не подходит run с override");

            var legacy = Real(1).NewState();
            Assert.IsNull(legacy.Run.MapOverride, "run на исходной карте (как созданные до override)");
            Assert.AreEqual(Map(1).RequiredCount, OutpostLevelAdapter.ForRun(TestData.Balance, legacy.Run).RequiredCount);
            Assert.IsFalse(StageProgressionService.IsRunMapCurrent(legacy.Run, Configs));
            var zone2 = Real(2).NewState();
            Assert.IsTrue(StageProgressionService.IsRunMapCurrent(zone2.Run, Configs), "зона без override");
            Assert.IsFalse(StageProgressionService.RestartRunIfMapChanged(zone2, TestData.Balance, Configs, StartUtcMs, out _));
        }

        [Test]
        public void LegacyZone1Run_RestartsOnArena_ProfileKept_ReadyToContinueKept()
        {
            // Незавершённая шахта со старой геометрией: новый run той же зоны (уровни 0, два котика, кошелёк 0); профиль сохраняется.
            var fx = Real(1, null, p =>
            {
                Own(p, "hat.rare", 3);
                p.Parts = 77;
                p.Tutorial.Step = 4;
                p.Tutorial.Seen.Add("o1_auto");
                p.Settings.Music = 0.25f;
            });
            var sim = fx.NewSim();
            sim.Advance(60 * S);
            var state = sim.State;
            var profileBefore = Json(new OutpostGameState { Profile = state.Profile });
            string oldRunId = state.Run.RunId;
            long seqBefore = state.Profile.NextRunSeq;
            Assert.Greater(state.Run.Counters.ReleasedUnits, 0, "старая шахта уже шла");

            Assert.IsTrue(StageProgressionService.RestartRunIfMapChanged(state, TestData.Balance, Configs, StartUtcMs + 120_000, out var map));
            var run = state.Run;
            Assert.AreNotEqual(oldRunId, run.RunId);
            Assert.AreEqual(seqBefore + 1, state.Profile.NextRunSeq);
            Assert.AreEqual(1, run.Zone);
            Assert.AreEqual("zone1-full-arena-1", run.MapOverride.Name);
            Assert.AreEqual(map.RequiredCount, run.Targets.Count);
            Assert.AreEqual(BigInteger.Zero, run.Wallet);
            Assert.IsTrue(run.Levels.All(l => l == 0));
            Assert.AreEqual(2, run.Workers.Count);
            Assert.AreEqual(StartUtcMs + 120_000, run.LastProcessedUtcMs);
            var profileAfter = FromJson(Json(new OutpostGameState { Profile = state.Profile })).Profile;
            profileAfter.NextRunSeq = seqBefore;
            AssertSameJson(profileBefore, Json(new OutpostGameState { Profile = profileAfter }), "профиль без изменений, кроме NextRunSeq");
            CollectionAssert.IsEmpty(OutpostInvariants.Check(state, map, Configs.Equipment));
            var fresh = new OutpostSimulation(state, map, Configs);
            RunToReadyOrFail(fresh, "zone1 после перезапуска");
            Assert.AreEqual(AuditBig(EffectiveAudit["base_gold"]), fresh.State.Run.TotalCredited);

            // Завершённую шахту не перезапускаем: её карта восстанавливается по копии override (здесь — исходная).
            var done = Real(1).NewSim();
            RunToReadyOrFail(done, "старая zone1");
            string before = Json(done.State);
            Assert.IsFalse(StageProgressionService.RestartRunIfMapChanged(done.State, TestData.Balance, Configs, StartUtcMs, out var none));
            Assert.IsNull(none);
            AssertSameJson(before, Json(done.State), "ReadyToContinue не меняется");
        }

        [Test]
        public void ChangedOverride_MakesRunStale_OldRunStillLoadsWithItsCopy()
        {
            var st = StageProgressionService.NewGame(TestData.Balance, Configs, Seed, StartUtcMs, 1);
            var json = OutpostConfigSet.DefaultOverridesJson.Replace("\"zone1-full-arena-1\"", "\"zone1-full-arena-2\"");
            var changed = OutpostConfigSet.Parse(OutpostConfigSet.DefaultBalanceJson, OutpostConfigSet.DefaultEquipmentJson,
                OutpostConfigSet.DefaultRewardsJson, json);
            Assert.IsFalse(StageProgressionService.IsRunMapCurrent(st.Run, changed), "другое имя — другой override");
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, st.Run);
            Assert.DoesNotThrow(() => new OutpostSimulation(st, map, changed), "карта run — по его копии override");
            Assert.IsTrue(StageProgressionService.RestartRunIfMapChanged(st, TestData.Balance, changed, StartUtcMs, out var newMap));
            Assert.AreEqual("zone1-full-arena-2", st.Run.MapOverride.Name);
            Assert.AreEqual("zone1-full-arena-2", newMap.OverrideName);
        }
    }
}
