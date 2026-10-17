using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Обстановка шахт (Docs/Rework/MINE_DRESSING.md): постоянные препятствия, детали стен, разрушаемый мусор. Проверки идут на
    /// выборке из 25 шахт (<see cref="Sample"/>), а не на всей кампании: ранние, средние и поздние, 0–3 ворот, все четыре плана,
    /// все три оформления, тесные и широкие, заводы у стены и в стороне от неё.
    /// </summary>
    public sealed class MineDressingTests
    {
        /// <summary>
        /// Выборка: 1 (арена override), 2 (первый завод), 3, 7, 23, 49, 59, 91, 105, 110, 122 (заводы не у стены), 5, 10, 16, 20,
        /// 30, 37, 46, 66, 70, 80, 120 (самая глубокая), 130, 140 (15 заводов, перенесённые), 144 (последняя).
        /// </summary>
        public static readonly int[] Sample = { 1, 2, 3, 5, 7, 10, 16, 20, 23, 30, 37, 46, 49, 59, 66, 70, 80, 91, 105, 110, 120, 122, 130, 140, 144 };
        /// <summary>Глубокие проверки (симуляция до конца шахты): самые требовательные планы выборки.</summary>
        public static readonly int[] Deep = { 1, 5, 30, 70, 110, 120, 140 };

        static OutpostConfigSet s_Configs;
        static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithCards(TestData.Balance));
        static OutpostConfigSet Plain => Configs.WithDressing(null);

        static readonly Dictionary<int, OutpostMap> s_Maps = new Dictionary<int, OutpostMap>();
        static readonly Dictionary<int, OutpostMap> s_PlainMaps = new Dictionary<int, OutpostMap>();

        static OutpostMap Map(int zone)
        {
            if (!s_Maps.TryGetValue(zone, out var map)) s_Maps[zone] = map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Configs);
            return map;
        }

        static OutpostMap PlainMap(int zone)
        {
            if (!s_PlainMaps.TryGetValue(zone, out var map)) s_PlainMaps[zone] = map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Plain);
            return map;
        }

        [Test]
        public void Rules_DefaultCatalog_EightPermanentEightDestructibleFamilies()
        {
            var rules = Configs.Dressing;
            Assert.That(rules, Is.Not.Null);
            Assert.That(rules.Version, Is.EqualTo("dressing-1"));
            Assert.That(rules.Families.Count(f => f.Category == DressingCategory.Permanent), Is.EqualTo(8));
            Assert.That(rules.Families.Count(f => f.Category == DressingCategory.Destructible), Is.EqualTo(8));
            Assert.That(rules.Families.Count(f => f.Category == DressingCategory.Wall), Is.GreaterThanOrEqualTo(1));
            CollectionAssert.AreEquivalent(new[] { "Light", "Medium", "Heavy" }, rules.Tiers.Keys);
            Assert.That(rules.Tier("Light").HpFactor < rules.Tier("Medium").HpFactor && rules.Tier("Medium").HpFactor < rules.Tier("Heavy").HpFactor);
            foreach (var f in rules.Families.Where(f => f.IsDestructible)) Assert.That(rules.Tier(f.Tier), Is.Not.Null, f.Id);
            // Встроенная копия совпадает с файлом Resources (как у остальных конфигураций).
            string file = File.ReadAllText(Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Resources", "Outpost", "mine_dressing.json"));
            Assert.That(OutpostConfigSet.DefaultDressingJson.Replace("\r\n", "\n"), Is.EqualTo(file.Replace("\r\n", "\n")));
            Assert.That(Configs.DressingHash, Is.Not.Null);
            Assert.That(Plain.Dressing, Is.Null);
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_Placement_KeepsSourceTargets_PassesValidator_KeepsPipes(int zone)
        {
            var plain = PlainMap(zone);
            var map = Map(zone);
            Assert.That(map.Dressing, Is.Not.Null);
            Assert.That(map.Dressing.RulesVersion, Is.EqualTo(Configs.Dressing.Version));
            var pieces = map.Targets.Where(t => t.IsDebris).ToArray();
            var props = map.Props.Where(p => !p.Wall).ToArray();
            Assert.That(pieces.Length, Is.GreaterThan(0), "мусор есть в каждой шахте выборки");
            Assert.That(props.Length, Is.GreaterThan(0), "постоянные препятствия есть в каждой шахте выборки");

            // Цели источника и их footprint не меняются, мусор — новые цели x с новыми ID.
            Assert.That(map.Targets.Length, Is.EqualTo(plain.Targets.Length + pieces.Length));
            Assert.That(map.RequiredCount, Is.EqualTo(plain.RequiredCount + pieces.Length));
            int maxSource = plain.Targets.Max(t => t.Id);
            foreach (var t in plain.Targets)
            {
                var m = map.Target(t.Id);
                Assert.That(m, Is.Not.Null, "цель источника " + t.Id);
                Assert.That(m.Kind, Is.EqualTo(t.Kind));
                Assert.That(m.TypeId, Is.EqualTo(t.TypeId));
                Assert.That(m.Sector, Is.EqualTo(t.Sector));
                Assert.That(m.RewardId, Is.EqualTo(t.RewardId));
                CollectionAssert.AreEqual(t.Cells, m.Cells, "footprint " + t.Id);
                Assert.That(m.IsDebris, Is.False);
            }
            foreach (var p in pieces)
            {
                Assert.That(p.Kind, Is.EqualTo('x'));
                Assert.That(p.Id, Is.GreaterThan(maxSource));
                Assert.That(p.TypeId, Does.StartWith(MineDressingAdapter.TypePrefix));
                Assert.That(Configs.Dressing.Family(p.Piece.Family)?.IsDestructible, Is.True, p.TypeId);
                Assert.That(p.WorkCells.Length, Is.GreaterThan(0));
            }

            // Оба случая валидатора: волновая расчистка и итоговая карта со стоящими заводами.
            var reach = OutpostReachability.Check(map);
            Assert.That(reach.Passed, Is.True, reach.Diagnostic);
            Assert.That(reach.Cleared, Is.EqualTo(map.Targets.Count(t => !t.IsGate)));

            // Производство прежнее: выход трубы и длина маршрута каждого завода не меняются.
            Assert.That(map.Factories.Length, Is.EqualTo(plain.Factories.Length));
            foreach (var f in plain.Factories)
            {
                var g = map.Factory(f.TargetId);
                Assert.That(g.OutletCell, Is.EqualTo(f.OutletCell), "выход трубы " + f.TargetId);
                Assert.That(g.RouteSteps, Is.EqualTo(f.RouteSteps), "маршрут " + f.TargetId);
            }

            // Постоянные препятствия: не пол навигации, не цель, не в лагере, ворота и выход свободны.
            var rules = Configs.Dressing;
            var dist = Distances(plain, plain.IntakeCell);
            var gateNear = new HashSet<int>();
            foreach (var t in plain.Targets.Where(t => t.IsGate))
                foreach (int c in t.Cells.Concat(t.WorkCells)) gateNear.Add(c);
            foreach (var prop in map.Props)
            {
                foreach (int c in prop.Cells)
                {
                    Assert.That(map.Row(c), Is.LessThan(map.Depth), "лагерь свободен");
                    if (prop.Wall)
                    {
                        Assert.That(plain.IsPlayable(c), Is.False, "деталь стены в породе");
                        Assert.That(map.IsPlayable(c + map.Width), Is.True, "под деталью стены — пол");
                        continue;
                    }
                    Assert.That(plain.IsPlayable(c), Is.True, "препятствие на полу");
                    Assert.That(map.IsPlayable(c), Is.False, "клетка препятствия непроходима");
                    Assert.That(map.IsProp(c) && map.IsFloor(c), Is.True);
                    Assert.That(map.InitialOccupant[c], Is.EqualTo(-1), "препятствие не цель");
                    Assert.That(map.Targets.Any(t => t.Cells.Contains(c)), Is.False, "препятствие не в списке целей");
                    Assert.That(dist[c], Is.GreaterThan(rules.Exclusions.BaseClearance), "устье над базой свободно");
                    Assert.That(gateNear.Contains(c), Is.False, "ворота и подход к ним свободны");
                }
            }
            foreach (var p in pieces)
                foreach (int c in p.Cells)
                {
                    Assert.That(map.Row(c), Is.LessThan(map.Depth), "лагерь свободен");
                    Assert.That(dist[c], Is.GreaterThan(rules.Exclusions.BaseClearance), "устье над базой свободно");
                    Assert.That(gateNear.Contains(c), Is.False, "ворота и подход к ним свободны");
                    Assert.That(Chebyshev(map, c, map.ExitCell), Is.GreaterThan(rules.Exclusions.ExitClearance), "выход свободен");
                }
            foreach (var f in plain.Factories)
                if (f.OutletCell >= 0)
                {
                    // Выход трубы считается по итоговой карте: в начале его может занимать цель источника, но не обстановка.
                    Assert.That(map.IsPlayable(f.OutletCell), Is.True, "выход трубы не под препятствием");
                    Assert.That(map.Target(map.InitialOccupant[f.OutletCell])?.IsDebris ?? false, Is.False, "выход трубы не под мусором");
                }
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_Distribution_EverySectorWithRoomGetsDebris(int zone)
        {
            var plain = PlainMap(zone);
            var map = Map(zone);
            var pieces = map.Targets.Where(t => t.IsDebris).ToArray();
            // Подходящие клетки сектора: свободный пол вне лагеря, ворот и устья у базы.
            var dist = Distances(plain, plain.IntakeCell);
            for (int s = 0; s <= plain.Gates.Length; s++)
            {
                int room = 0;
                for (int c = 0; c < plain.Width * plain.Depth; c++)
                    if (plain.IsPlayable(c) && plain.SectorAt(c) == s && plain.InitialOccupant[c] < 0 && plain.CellGateOrder[c] < 0 &&
                        dist[c] > Configs.Dressing.Exclusions.BaseClearance + 2)
                        room++;
                if (room < 12) continue;
                Assert.That(pieces.Count(p => p.Sector == s), Is.GreaterThan(0), "сектор " + s + " (" + room + " свободных клеток) без мусора");
            }
            // Разнообразие: несколько семейств мусора и постоянных препятствий.
            Assert.That(pieces.Select(p => p.Piece.Family).Distinct().Count(), Is.GreaterThanOrEqualTo(Math.Min(4, pieces.Length)));
            int props = map.Props.Count(p => !p.Wall);
            Assert.That(map.Props.Where(p => !p.Wall).Select(p => p.Family).Distinct().Count(), Is.EqualTo(props).Or.GreaterThanOrEqualTo(2));
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_Deterministic_RoundTripsThroughRunSnapshot(int zone)
        {
            var again = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Configs);
            Assert.That(OutpostJson.Serialize(again.Dressing), Is.EqualTo(OutpostJson.Serialize(Map(zone).Dressing)), "одинаковая карта при каждом создании");
            var state = StageProgressionService.NewGame(TestData.Balance, Configs, 4242, 0, zone);
            Assert.That(state.Run.MapDressing, Is.Not.Null);
            string saved = OutpostSaveService.SerializeState(state);
            var restored = OutpostSaveService.DeserializeState(saved);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run);
            Assert.That(OutpostJson.Serialize(map.Dressing), Is.EqualTo(OutpostJson.Serialize(state.Run.MapDressing)));
            CollectionAssert.AreEqual(Map(zone).InitialOccupant, map.InitialOccupant);
            CollectionAssert.AreEqual(Map(zone).PlayableCells, map.PlayableCells);
            Assert.DoesNotThrow(() => new OutpostSimulation(restored, map, Configs));
            // Правка правил не двигает объекты начатой шахты: карта строится по снимку run.
            var edited = MineDressingRules.Parse(OutpostConfigSet.DefaultDressingJson.Replace("\"Version\": \"dressing-1\"", "\"Version\": \"dressing-x\""));
            var other = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, Configs.WithDressing(edited));
            Assert.That(OutpostJson.Serialize(OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run).Dressing),
                Is.EqualTo(OutpostJson.Serialize(state.Run.MapDressing)));
            Assert.That(other.Dressing.RulesVersion, Is.EqualTo("dressing-x"));
        }

        [TestCase(5)]
        [TestCase(70)]
        [TestCase(140)]
        public void SaveReload_MidMine_ContinuesIdentically_DestroyedDebrisStaysCleared(int zone)
        {
            var configs = Configs.WithoutPace();
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 31337, 0, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs);
            // Доход идёт на «Команду» и кирки, чтобы до сохранения успели разбить хотя бы одну кучу.
            for (int k = 0; k < 40 && DestroyedDebris(sim) == 0; k++)
            {
                sim.Advance(sim.Now + 30 * OutpostIds.TicksPerSecond);
                sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Team));
                sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes));
            }
            int destroyed = DestroyedDebris(sim);
            Assert.That(destroyed, Is.GreaterThan(0), "за время прогона котики разбили мусор");
            var copy = OutpostSaveService.DeserializeState(OutpostSaveService.SerializeState(state));
            var restoredMap = OutpostLevelAdapter.ForRun(TestData.Balance, copy.Run);
            var restored = new OutpostSimulation(copy, restoredMap, configs);
            Assert.That(DestroyedDebris(restored), Is.EqualTo(destroyed), "разрушенный мусор не появляется снова");
            for (int i = 0; i < restoredMap.Targets.Length; i++)
            {
                var t = restoredMap.Targets[i];
                if (!t.IsDebris || !copy.Run.Targets[i].Destroyed) continue;
                foreach (int c in t.Cells) Assert.That(restored.Navigation.Passable(c) || c == -1, Is.True, "клетки разбитой кучи свободны");
            }
            sim.Advance(sim.Now + 240 * OutpostIds.TicksPerSecond);
            restored.Advance(restored.Now + 240 * OutpostIds.TicksPerSecond);
            Assert.That(OutpostJson.Serialize(restored.State), Is.EqualTo(OutpostJson.Serialize(sim.State)), "после загрузки — тот же расчёт");
            Assert.That(OutpostInvariants.Check(restored.State, restoredMap, configs.Equipment, configs.Cards), Is.Empty);
        }

        [Test]
        public void RunCreatedBeforeDressing_KeepsItsLayout_NextMineGetsDressing()
        {
            var state = StageProgressionService.NewGame(TestData.Balance, Plain, 99, 0, 5);
            Assert.That(state.Run.MapDressing, Is.Null);
            string saved = OutpostSaveService.SerializeState(state);
            var restored = OutpostSaveService.DeserializeState(saved);
            Assert.That(StageProgressionService.IsRunMapCurrent(restored.Run, Configs), Is.True, "шахта не перезапускается");
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, restored.Run);
            Assert.That(map.Dressing, Is.Null);
            Assert.That(map.Targets.Any(t => t.IsDebris), Is.False);
            CollectionAssert.AreEqual(PlainMap(5).InitialOccupant, map.InitialOccupant);
            var sim = new OutpostSimulation(restored, map, Configs.WithoutPace());
            var finished = new OutpostBot(BotPolicy.C).Run(sim);
            Assert.That(finished.Success, Is.True, finished.Error ?? finished.WatchdogDiagnostic);
            var next = StageProgressionService.PrepareTransition(restored, TestData.Balance, Configs, 0);
            Assert.That(next.Ok, Is.True);
            Assert.That(next.State.Run.MapDressing, Is.Not.Null, "следующая шахта — по новым правилам");
            Assert.That(next.Map.Targets.Any(t => t.IsDebris), Is.True);
        }

        [TestCaseSource(nameof(Deep))]
        public void Deep_AutoMining_ClearsDebris_NoStuckWorkers_Completes(int zone)
        {
            // Без множителя темпа K(z): одинаково растягивает HP всех целей, а поздние шахты по часам тест не уложит.
            var configs = Configs.WithoutPace();
            var profile = StageProgressionService.NewProfile(configs, 7, zone);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, configs);
            var state = new OutpostGameState { Profile = profile, Run = StageProgressionService.CreateRun(profile, zone, map, configs, TestData.Balance, 0) };
            var sim = new OutpostSimulation(state, map, configs);
            var report = new OutpostBot(BotPolicy.C).Run(sim);
            Assert.That(report.Success, Is.True, report.Error ?? report.WatchdogDiagnostic ?? string.Join("; ", report.InvariantErrors));
            Assert.That(state.Run.WatchdogFired, Is.False, state.Run.WatchdogDiagnostic);
            for (int i = 0; i < map.Targets.Length; i++)
                if (map.Targets[i].IsDebris) Assert.That(state.Run.Targets[i].Destroyed, Is.True, "куча " + map.Targets[i].Id + " разбита");
            // Груз мусора прошёл через склад: зачислено не меньше, чем выпущено всеми целями (монеты только при разгрузке).
            Assert.That(state.Run.Counters.DeliveredValue, Is.EqualTo(state.Run.Counters.ReleasedValue));
        }

        [Test]
        public void Strike_OnPermanentProp_IsNotATarget_OnDebrisIsAllowed()
        {
            var configs = Configs.WithoutPace();
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 5, 0, 30);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs);
            var prop = map.Props.First(p => !p.Wall);
            foreach (int c in prop.Cells) Assert.That(map.Target(map.InitialOccupant[c] >= 0 ? map.InitialOccupant[c] : int.MaxValue), Is.Null);
            Assert.That(sim.Navigation.Passable(prop.Cells[0]), Is.False, "котики обходят препятствие");
            // Удар разрешён по достижимой куче мусора (первая доступная в секторе 0).
            var debris = map.Targets.Where(t => t.IsDebris && t.Sector == 0)
                .FirstOrDefault(t => t.WorkCells.Any(c => sim.Navigation.Distance(map.IntakeCell, c) >= 0));
            Assume.That(debris, Is.Not.Null);
            var r = sim.Submit(OutpostCommand.Strike(debris.Id));
            Assert.That(r.Ok, Is.True, r.Reason);
        }

        // ───────────── Отчёты выборки (не проверка критериев: генераторы) ─────────────

        [Test, Explicit("Отчёт размещения обстановки на выборке из 25 шахт: Docs/Reports/dressing/layouts.md")]
        public void Report_SampleLayouts()
        {
            var sb = new StringBuilder();
            var rows = new List<object>();
            sb.Append("# Обстановка шахт: выборка из 25 шахт\n\nЛегенда: `#` порода, `.` пол, `o` камень r, `X` завал x источника, ")
              .Append("`F` завод, `G` ворота, `d` мусор обстановки, `P` постоянное препятствие, `w` деталь стены, `E` выход.\n\n");
            foreach (int zone in Sample)
            {
                var plain = PlainMap(zone);
                var map = Map(zone);
                var pieces = map.Targets.Where(t => t.IsDebris).ToArray();
                var areas = MineDressingPlacer.ClassifyAreas(plain, Configs.Dressing);
                long hpSource = 0, hpDebris = 0;
                var balances = OutpostLevelBalanceAdapter.ApplyPace(map, Configs.Balance,
                    OutpostLevelBalanceAdapter.Compute(map, Configs.Balance, TestData.Balance));
                for (int i = 0; i < map.Targets.Length; i++)
                {
                    if (map.Targets[i].IsFactory) continue;
                    if (map.Targets[i].IsDebris) hpDebris += balances[i].MaxHp;
                    else hpSource += balances[i].MaxHp;
                }
                var byArea = new int[3];
                foreach (var p in pieces) foreach (int c in p.Cells) if (areas[c] >= 0) byArea[areas[c]]++;
                var row = new
                {
                    zone,
                    layout = map.Topology.Layout,
                    size = map.Width + "x" + map.Depth,
                    gates = map.Gates.Length,
                    factories = map.Factories.Length,
                    sourceTargets = plain.Targets.Length,
                    debris = pieces.Length,
                    debrisCells = pieces.Sum(p => p.Cells.Length),
                    debrisCellsMain = byArea[(int)DressingArea.MainPassage],
                    debrisCellsSide = byArea[(int)DressingArea.SidePassage],
                    debrisCellsChamber = byArea[(int)DressingArea.Chamber],
                    props = map.Props.Count(p => !p.Wall),
                    wallProps = map.Props.Count(p => p.Wall),
                    propFamilies = map.Props.Select(p => p.Family).Distinct().OrderBy(f => f).ToArray(),
                    debrisFamilies = pieces.GroupBy(p => p.Piece.Family).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                    debrisBySector = Enumerable.Range(0, map.Gates.Length + 1).Select(s => pieces.Count(p => p.Sector == s)).ToArray(),
                    hpShareAdded = hpSource > 0 ? Math.Round((double)hpDebris / hpSource, 4) : 0,
                };
                rows.Add(row);
                sb.Append("## Шахта ").Append(zone).Append(" — ").Append(row.layout).Append(", ").Append(row.size).Append(", ворот ")
                  .Append(row.gates).Append(", заводов ").Append(row.factories).Append("\n\n");
                sb.Append("Мусор: ").Append(row.debris).Append(" куч (").Append(row.debrisCells).Append(" клеток; проход ")
                  .Append(row.debrisCellsMain).Append(", боковые ").Append(row.debrisCellsSide).Append(", камеры ").Append(row.debrisCellsChamber)
                  .Append("), по секторам ").Append(string.Join("/", row.debrisBySector)).Append("; постоянных ").Append(row.props)
                  .Append(", деталей стен ").Append(row.wallProps).Append("; HP мусора к HP целей источника +")
                  .Append((row.hpShareAdded * 100).ToString("0.0", CultureInfo.InvariantCulture)).Append(" %.\n\n```\n");
                sb.Append(Ascii(map)).Append("```\n\n");
            }
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "dressing");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "layouts.md"), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "layouts.json"), JsonConvert.SerializeObject(rows, Formatting.Indented) + "\n");
            TestContext.WriteLine(sb.ToString());
        }

        [Test, Explicit("Темп на выборке до/после обстановки: Docs/Reports/dressing/pacing.json (модель free, пустой профиль, K = 1)")]
        public void Report_SamplePacing()
        {
            // Пустой профиль и K = 1 у обеих карт: сравнивается только добавленная работа, без кампании (её не гоняем).
            var after = Configs.WithoutPace();
            var before = after.WithDressing(null);
            var rows = new List<object>();
            foreach (int zone in Sample)
            {
                var a = RunAudit(before, zone);
                var b = RunAudit(after, zone);
                rows.Add(new
                {
                    zone,
                    before = Brief(a),
                    after = Brief(b),
                    completedRatio = a.CompletedSec > 0 && b.CompletedSec.HasValue ? Math.Round(b.CompletedSec.Value / a.CompletedSec.Value, 4) : (double?)null,
                    hpRatio = a.TotalHp > 0 ? Math.Round((double)b.TotalHp / a.TotalHp, 4) : (double?)null,
                    unitsRatio = a.CargoUnits > 0 ? Math.Round((double)b.CargoUnits / a.CargoUnits, 4) : (double?)null,
                });
                TestContext.WriteLine("zone " + zone + ": " + a.CompletedSec + " s → " + b.CompletedSec + " s; max gap " +
                                      a.MaxPurchaseGapSec + " → " + b.MaxPurchaseGapSec + "; max unaffordable " + a.MaxUnaffordableSec + " → " + b.MaxUnaffordableSec);
            }
            string dir = Path.Combine(TestData.ProjectRoot, "Docs", "Reports", "dressing");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pacing.json"), JsonConvert.SerializeObject(new
            {
                method = "BalanceAudit.RunMine, model free (Bottleneck, check every 30 s), empty profile, seed 1, PaceScale K = 1 (WithoutPace), " +
                         "the same mine with and without dressing; isolated mines, not a campaign run",
                sample = Sample,
                rows,
            }, Formatting.Indented) + "\n");
        }

        static MineAudit RunAudit(OutpostConfigSet configs, int zone)
        {
            var profile = StageProgressionService.NewProfile(configs, 1, zone);
            var map = OutpostLevelAdapter.BuildMine(TestData.Balance, zone, configs);
            var state = new OutpostGameState { Profile = profile, Run = StageProgressionService.CreateRun(profile, zone, map, configs, TestData.Balance, 0) };
            var sim = new OutpostSimulation(state, map, configs);
            return BalanceAudit.RunMine(sim, BalanceAudit.Model("free"), "free");
        }

        static object Brief(MineAudit a) => new
        {
            a.Outcome, a.CompletedSec, a.ClearedSec, a.FirstDeliverySec, a.FirstPurchaseSec, a.Targets, a.TotalHp, a.CargoUnits, a.Purchases,
            a.MaxPurchaseGapSec, a.MedianPurchaseGapSec, a.MaxUnaffordableSec, a.SectorTailSec,
        };

        // ───────────── Помощники ─────────────

        static int DestroyedDebris(OutpostSimulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.Map.Targets.Length; i++)
                if (sim.Map.Targets[i].IsDebris && sim.Run.Targets[i].Destroyed) n++;
            return n;
        }

        static int Chebyshev(OutpostMap map, int a, int b) => Math.Max(Math.Abs(map.Col(a) - map.Col(b)), Math.Abs(map.Row(a) - map.Row(b)));

        /// <summary>BFS по итоговой карте (цели сняты, ворота открыты, заводы стоят).</summary>
        static int[] Distances(OutpostMap map, int start)
        {
            var wall = new bool[map.CellCount];
            for (int c = 0; c < map.CellCount; c++) wall[c] = !map.IsPlayable(c);
            foreach (var t in map.Targets.Where(t => t.IsFactory)) foreach (int c in t.Cells) wall[c] = true;
            var dist = Enumerable.Repeat(-1, map.CellCount).ToArray();
            var queue = new Queue<int>();
            dist[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int col = map.Col(c), row = map.Row(c);
                foreach (var (x, y) in new[] { (col, row - 1), (col - 1, row), (col + 1, row), (col, row + 1) })
                {
                    if (x < 0 || x >= map.Width || y < 0 || y >= map.Rows) continue;
                    int n = map.Cell(x, y);
                    if (wall[n] || dist[n] >= 0) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        static string Ascii(OutpostMap map)
        {
            var g = new char[map.Rows, map.Width];
            for (int r = 0; r < map.Rows; r++)
                for (int c = 0; c < map.Width; c++)
                {
                    int cell = map.Cell(c, r);
                    g[r, c] = map.IsPlayable(cell) ? '.' : '#';
                }
            foreach (var t in map.Targets)
            {
                char ch = t.IsDebris ? 'd' : t.Kind == 'r' ? 'o' : t.Kind == 'x' ? 'X' : t.Kind == 's' ? 'F' : 'G';
                foreach (int cell in t.Cells) g[map.Row(cell), map.Col(cell)] = ch;
            }
            foreach (var p in map.Props)
                foreach (int cell in p.Cells) g[map.Row(cell), map.Col(cell)] = p.Wall ? 'w' : 'P';
            if (map.ExitCell >= 0) g[map.Row(map.ExitCell), map.Col(map.ExitCell)] = 'E';
            var sb = new StringBuilder();
            for (int r = 0; r < map.Rows; r++)
            {
                for (int c = 0; c < map.Width; c++) sb.Append(g[r, c]);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
