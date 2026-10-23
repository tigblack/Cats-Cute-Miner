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
            Assert.That(rules.Version, Is.EqualTo("dressing-3"));
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
            var added = map.Targets.Where(t => t.Piece != null).ToArray();
            var props = map.Props.Where(p => !p.Wall).ToArray();
            Assert.That(pieces.Length, Is.GreaterThan(0), "мусор есть в каждой шахте выборки");
            Assert.That(props.Length, Is.GreaterThan(0), "постоянные препятствия есть в каждой шахте выборки");

            // Цели источника и их footprint не меняются, мусор и руда-заполнитель — новые цели с новыми ID.
            Assert.That(map.Targets.Length, Is.EqualTo(plain.Targets.Length + added.Length));
            Assert.That(map.RequiredCount, Is.EqualTo(plain.RequiredCount + added.Length));
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
            // Первая сессия: в шахте спасения клетка Mia — постоянное препятствие, замок — куча мусора; их семейства — в
            // progression.json → Rescue, а не в каталоге mine_dressing.json.
            var rescueRules = Configs.Progression.Rescue;
            var rescue = map.Rescue;
            Assert.That(rescue != null, Is.EqualTo(zone == rescueRules.Zone), "клетка спасения — только в шахте " + rescueRules.Zone);
            if (rescue != null)
            {
                Assert.That(map.Props[rescue.CageProp].Family, Is.EqualTo(rescueRules.CageFamily));
                Assert.That(map.Props[rescue.CageProp].Wall, Is.False, "клетка — препятствие на полу");
                Assert.That(map.Target(rescue.LockTargetId)?.IsDebris, Is.True, "замок — цель-куча обстановки");
            }
            foreach (var p in pieces)
            {
                Assert.That(p.Kind, Is.EqualTo('x'));
                Assert.That(p.Id, Is.GreaterThan(maxSource));
                Assert.That(p.TypeId, Does.StartWith(MineDressingAdapter.TypePrefix));
                if (rescue != null && p.Id == rescue.LockTargetId)
                {
                    Assert.That(p.Piece.Family, Is.EqualTo(rescueRules.LockFamily), p.TypeId);
                    Assert.That(p.Piece.Tier, Is.EqualTo(rescueRules.LockTier), p.TypeId);
                    Assert.That(Configs.Dressing.Tier(p.Piece.Tier), Is.Not.Null, p.TypeId + ": ярус прочности из mine_dressing.json");
                }
                else Assert.That(Configs.Dressing.Family(p.Piece.Family)?.IsDestructible, Is.True, p.TypeId);
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
                    Assert.That(map.Target(map.InitialOccupant[f.OutletCell])?.Piece, Is.Null, "выход трубы не под мусором и рудой обстановки");
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
            var edited = MineDressingRules.Parse(OutpostConfigSet.DefaultDressingJson.Replace("\"Version\": \"dressing-3\"", "\"Version\": \"dressing-x\""));
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
        public void Rules_EveryFamilyVariant_HasArtCoveringItsFootprint()
        {
            // Правила и арт согласованы: у каждого варианта — спрайт атласа (PixelAtlasData.g.cs из Tools/PixelArt/build.py), у кучи —
            // стадии _d1/_d2 и след на полу. Спрайт 2×1 шире клетки, а куча не ниже 2/3 клетки (21 из 32 px, как камень): мусор
            // читается препятствием, которое надо разбить, а не сором на полу.
            string text = File.ReadAllText(Path.Combine(TestData.ProjectRoot, "Assets", "Game", "Scripts", "Runtime", "Pixel", "PixelAtlasData.g.cs"));
            var sprites = new Dictionary<string, (int W, int H)>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text,
                         "new PixelSpriteDef\\(\"([^\"]+)\", \\d+, \\d+, (\\d+), (\\d+)"))
                sprites[m.Groups[1].Value] = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            Assert.That(sprites.Count, Is.GreaterThan(1000));
            foreach (var f in Configs.Dressing.Families)
                for (int v = 0; v < f.Variants.Count; v++)
                {
                    string name = (f.IsDestructible ? "debris_" : "prop_") + f.Id + "_" + v;
                    Assert.That(sprites.TryGetValue(name, out var size), Is.True, "нет спрайта " + name);
                    Assert.That(size.W, Is.GreaterThan(32 * (f.Variants[v].W - 1) + 16), name + ": спрайт уже footprint");
                    if (!f.IsDestructible) continue;
                    Assert.That(size.H, Is.GreaterThanOrEqualTo(21), name + ": куча ниже 2/3 клетки");
                    Assert.That(sprites.ContainsKey(name + "_d1") && sprites.ContainsKey(name + "_d2"), Is.True, name + ": стадии");
                }
            foreach (var f in Configs.Dressing.Families.Where(f => f.IsDestructible))
                Assert.That(sprites.ContainsKey("debris_" + f.Id + "_rest"), Is.True, f.Id + ": след на полу");
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_OreFill_NoEmptyFloorInTheMine_CheapestBasicRock(int zone)
        {
            // Пустых мест нет: каждая клетка пола шахты (лагерь ниже — не шахта) занята целью, кроме клеток ворот, выхода, выходов
            // труб и входа в сектор за воротами (клетка ворот — не рабочая клетка); заполнитель — самый дешёвый обычный камень этой
            // шахты (тот же тип и уровень, что у камня источника). Первая сессия (progression.json → Rescue.ClearRouteOre): в шахте
            // спасения руда на самом дешёвом пути от базы к замку клетки снята — пустой пол там намеренный, остальное заполнено.
            var plain = PlainMap(zone);
            var map = Map(zone);
            var keep = new HashSet<int> { map.ExitCell };
            foreach (var f in map.Factories) keep.Add(f.OutletCell);
            for (int c = 0; c < map.Width * map.Depth; c++)
                if (map.CellGateOrder[c] >= 0)
                    foreach (int n in new[] { c - map.Width, c - 1, c + 1, c + map.Width })
                        if (n >= 0 && n < map.CellCount && map.CellGateOrder[n] < 0 && map.SectorAt(n) > map.CellGateOrder[c]) keep.Add(n);
            var route = map.Rescue != null ? CheapestRouteCells(map, map.Rescue.LockTargetId) : new HashSet<int>();
            Assert.That(map.Rescue == null || route.Count > 0, Is.True, "шахта " + zone + ": путь от базы к замку клетки");
            var empty = new List<int>();
            for (int c = 0; c < map.Width * map.Depth; c++)
                if (map.IsPlayable(c) && map.InitialOccupant[c] < 0 && map.CellGateOrder[c] < 0 && !keep.Contains(c) && !route.Contains(c)) empty.Add(c);
            Assert.That(empty, Is.Empty, "шахта " + zone + ": пустые клетки " + string.Join(", ", empty.Select(c => map.Col(c) + ":" + map.Row(c))));

            var ores = map.Targets.Where(t => t.IsOreFill).ToArray();
            Assert.That(ores.Length, Is.GreaterThan(0));
            var types = Configs.Dressing.OreFill.RockTypes;
            string type = types.First(id => plain.Targets.Any(t => t.Kind == 'r' && t.TypeId == id));
            int level = plain.Targets.Where(t => t.Kind == 'r' && t.TypeId == type).Min(t => t.SourceLevel);
            foreach (var t in ores)
            {
                Assert.That(t.TypeId, Is.EqualTo(type));
                Assert.That(t.SourceLevel, Is.EqualTo(level));
                Assert.That(t.Cells.Length, Is.EqualTo(1));
                Assert.That(t.WorkCells.Length, Is.GreaterThan(0));
            }
            // Ранги и HP целей источника не меняются: шаблон — уже существующий камень шахты.
            var a = OutpostLevelBalanceAdapter.Compute(plain, Configs.Balance, TestData.Balance);
            var b = OutpostLevelBalanceAdapter.Compute(map, Configs.Balance, TestData.Balance);
            for (int i = 0; i < plain.Targets.Length; i++)
            {
                int j = map.TargetIndex(plain.Targets[i].Id);
                Assert.That(b[j].MaxHp, Is.EqualTo(a[i].MaxHp), "HP цели источника " + plain.Targets[i].Id);
            }
            int cleared = route.Count(c => c < map.Width * map.Depth && map.IsPlayable(c) && map.InitialOccupant[c] < 0 && map.CellGateOrder[c] < 0 && !keep.Contains(c));
            TestContext.WriteLine("zone " + zone + ": ore fill " + ores.Length + " (" + type + " level " + level + ")" +
                (map.Rescue != null ? "; rescue route " + route.Count + " cells, " + cleared + " without ore" : ""));
        }

        [TestCase(6)]
        [TestCase(30)]
        public void OreFill_GivesGoldOnly_NoRockCycleRewards(int zone)
        {
            // Руда-заполнитель — только золото порциями груза: счётчик цикла наград камня (сундуки, эликсир) растёт лишь от камней
            // источника, иначе карточки и эликсир росли бы вместе с количеством руды.
            var configs = Configs.WithoutPace();
            var state = StageProgressionService.NewGame(TestData.Balance, configs, 77, 0, zone);
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, state.Run);
            var sim = new OutpostSimulation(state, map, configs);
            for (int k = 0; k < 20 && DestroyedOre(sim) == 0; k++) sim.Advance(sim.Now + 30 * OutpostIds.TicksPerSecond);
            Assert.That(DestroyedOre(sim), Is.GreaterThan(0), "котики разбили руду-заполнитель");
            foreach (var kv in state.Profile.RockCycles)
            {
                int source = 0;
                for (int i = 0; i < map.Targets.Length; i++)
                    if (map.Targets[i].Kind == 'r' && !map.Targets[i].IsOreFill && map.Targets[i].TypeId == kv.Key && state.Run.Targets[i].Destroyed) source++;
                Assert.That(kv.Value, Is.EqualTo(source), "цикл наград " + kv.Key + " считает только камни источника");
            }
        }

        [TestCase(2)]
        [TestCase(30)]
        [TestCase(110)]
        public void PriceFactor_IsCargoValueRatio_AndRaisesEveryBranch(int zone)
        {
            // Больше руды — дороже улучшения: множитель цен шахты — стоимость груза всех целей к стоимости груза целей источника;
            // цены всех веток с ним, а без обстановки — прежние.
            var dressed = StageProgressionService.NewGame(TestData.Balance, Configs, 5, 0, zone);
            var plain = StageProgressionService.NewGame(TestData.Balance, Plain, 5, 0, zone);
            Assert.That(plain.Run.PriceFactor, Is.EqualTo(Rational.One));
            var map = OutpostLevelAdapter.ForRun(TestData.Balance, dressed.Run);
            Rational all = Rational.Zero, source = Rational.Zero;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var v = dressed.Run.Targets[i].ValueFactor * dressed.Run.Targets[i].TotalUnits;
                all += v;
                if (map.Targets[i].Piece == null) source += v;
            }
            var ratio = all / source;
            var factor = dressed.Run.PriceFactor;
            Assert.That(factor >= ratio && factor < ratio + Rational.Of(1, 100), Is.True, "множитель " + factor + " — отношение " + ratio + " вверх до сотых");
            Assert.That(factor > Rational.One, Is.True);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                var a = TeamUpgradeService.Quote(plain, branch, BuyMode.One).NextCost.Value;
                var b = TeamUpgradeService.Quote(dressed, branch, BuyMode.One).NextCost.Value;
                Assert.That(b, Is.EqualTo((dressed.Run.Balance.PriceScale(zone) * dressed.Run.Balance.Branch(branch).BaseCost * factor).Ceil()), branch.ToString());
                Assert.That(b, Is.GreaterThan(a), branch + ": цена выросла");
            }
            TestContext.WriteLine("zone " + zone + ": price factor " + factor.ToDouble().ToString("0.00", CultureInfo.InvariantCulture));
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_Barricades_ClosePassages_WithSpacing(int zone)
        {
            var map = Map(zone);
            var rules = Configs.Dressing.Barricades;
            var debris = new Dictionary<int, int>();
            foreach (var t in map.Targets.Where(t => t.IsDebris))
                foreach (int c in t.Cells) debris[c] = t.Id;
            // Пол итоговой карты: проходимые клетки без footprint заводов (постоянные препятствия уже непроходимы).
            var factory = new HashSet<int>(map.Targets.Where(t => t.IsFactory).SelectMany(t => t.Cells));
            bool Floor(int col, int row) => col >= 0 && col < map.Width && row >= 0 && row < map.Depth &&
                                            map.IsPlayable(map.Cell(col, row)) && !factory.Contains(map.Cell(col, row));
            // Закрытые сечения: пролёт пола от стены до стены не шире MaxWidth, проход продолжается по обе стороны, все клетки — мусор.
            var closed = new List<int[]>();
            foreach (int cell in debris.Keys.OrderBy(c => c))
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                {
                    int x = map.Col(cell), y = map.Row(cell);
                    while (Floor(x - dx, y - dy)) { x -= dx; y -= dy; }
                    var run = new List<int>();
                    for (; Floor(x, y) && run.Count <= rules.MaxWidth; x += dx, y += dy) run.Add(map.Cell(x, y));
                    if (run.Count > rules.MaxWidth || !run.All(debris.ContainsKey)) continue;
                    bool before = run.Any(c => Floor(map.Col(c) - dy, map.Row(c) - dx));
                    bool after = run.Any(c => Floor(map.Col(c) + dy, map.Row(c) + dx));
                    if (before && after && !closed.Any(o => o.SequenceEqual(run))) closed.Add(run.ToArray());
                }
            Assert.That(closed.Count, Is.GreaterThan(0), "шахта " + zone + ": ни один проход не перекрыт завалом");
            // Завалы — из разных куч (сечения, закрытые одной кучей, — один завал) — не ближе Spacing − 2: сечение шириной до 3
            // клеток, закрытое кучей завала попутно, делит с ним клетку.
            for (int i = 0; i < closed.Count; i++)
                for (int j = i + 1; j < closed.Count; j++)
                {
                    var a = closed[i].Select(c => debris[c]).ToHashSet();
                    if (closed[j].Any(c => a.Contains(debris[c]))) continue;
                    int d = closed[i].Min(p => closed[j].Min(q => Math.Max(Math.Abs(map.Col(p) - map.Col(q)), Math.Abs(map.Row(p) - map.Row(q)))));
                    Assert.That(d, Is.GreaterThanOrEqualTo(rules.Spacing - 2), "шахта " + zone + ": завалы вплотную у клеток " + closed[i][0] + " и " + closed[j][0]);
                }
            TestContext.WriteLine("zone " + zone + ": closed cross-sections " + closed.Count);
        }

        [TestCaseSource(nameof(Sample))]
        public void Sample_Coverage_ClearingWorkInEveryBandOfTheMine(int zone)
        {
            // «По всей карте»: шахта режется на полосы по 12 строк (около экрана телефона по высоте); в каждой полосе, где
            // больше 24 клеток пола вне устья у базы, есть хотя бы две кучи мусора — работа котикам на каждом экране, а не
            // только у входа или в отдельных камерах.
            var plain = PlainMap(zone);
            var map = Map(zone);
            var dist = Distances(plain, plain.IntakeCell);
            var pieces = map.Targets.Where(t => t.IsDebris).ToArray();
            const int Band = 12;
            for (int top = 0; top < map.Depth; top += Band)
            {
                int bottom = Math.Min(map.Depth, top + Band);
                int floor = 0;
                for (int r = top; r < bottom; r++)
                    for (int c = 0; c < map.Width; c++)
                    {
                        int cell = map.Cell(c, r);
                        if (plain.IsPlayable(cell) && dist[cell] > Configs.Dressing.Exclusions.BaseClearance) floor++;
                    }
                if (floor <= 24) continue;
                int inBand = pieces.Count(p => p.Cells.Any(c => map.Row(c) >= top && map.Row(c) < bottom));
                Assert.That(inBand, Is.GreaterThanOrEqualTo(2), "шахта " + zone + ", строки " + top + "–" + (bottom - 1) + ": " + floor + " клеток пола, куч " + inBand);
            }
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
            // Удар разрешён по достижимой цели обстановки (первая доступная в секторе 0: руда у лагеря или куча мусора).
            var debris = map.Targets.Where(t => t.Piece != null && t.Sector == 0)
                .FirstOrDefault(t => t.WorkCells.Any(c => sim.Navigation.Distance(map.IntakeCell, c) >= 0));
            Assert.That(debris, Is.Not.Null);
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
              .Append("`F` завод, `G` ворота, `d` мусор обстановки, `*` руда-заполнитель, `P` постоянное препятствие, `w` деталь стены, `E` выход.\n\n");
            foreach (int zone in Sample)
            {
                var plain = PlainMap(zone);
                var map = Map(zone);
                var pieces = map.Targets.Where(t => t.IsDebris).ToArray();
                int ores = map.Targets.Count(t => t.IsOreFill);
                var areas = MineDressingPlacer.ClassifyAreas(plain, Configs.Dressing);
                long hpSource = 0, hpDebris = 0, hpOre = 0;
                var balances = OutpostLevelBalanceAdapter.ApplyPace(map, Configs.Balance,
                    OutpostLevelBalanceAdapter.Compute(map, Configs.Balance, TestData.Balance));
                for (int i = 0; i < map.Targets.Length; i++)
                {
                    if (map.Targets[i].IsFactory) continue;
                    if (map.Targets[i].IsDebris) hpDebris += balances[i].MaxHp;
                    else if (map.Targets[i].IsOreFill) hpOre += balances[i].MaxHp;
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
                    oreFill = ores,
                    hpShareOre = hpSource > 0 ? Math.Round((double)hpOre / hpSource, 4) : 0,
                    priceFactor = StageProgressionService.NewGame(TestData.Balance, Configs, 1, 0, zone).Run.PriceFactor.ToDouble(),
                };
                rows.Add(row);
                sb.Append("## Шахта ").Append(zone).Append(" — ").Append(row.layout).Append(", ").Append(row.size).Append(", ворот ")
                  .Append(row.gates).Append(", заводов ").Append(row.factories).Append("\n\n");
                sb.Append("Мусор: ").Append(row.debris).Append(" куч (").Append(row.debrisCells).Append(" клеток; проход ")
                  .Append(row.debrisCellsMain).Append(", боковые ").Append(row.debrisCellsSide).Append(", камеры ").Append(row.debrisCellsChamber)
                  .Append("), по секторам ").Append(string.Join("/", row.debrisBySector)).Append("; постоянных ").Append(row.props)
                  .Append(", деталей стен ").Append(row.wallProps).Append("; HP мусора к HP целей источника +")
                  .Append((row.hpShareAdded * 100).ToString("0.0", CultureInfo.InvariantCulture)).Append(" %; руды-заполнителя ")
                  .Append(row.oreFill).Append(" клеток, её HP +").Append((row.hpShareOre * 100).ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" %; множитель цен улучшений ×").Append(row.priceFactor.ToString("0.00", CultureInfo.InvariantCulture)).Append(".\n\n```\n");
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

        static int DestroyedOre(OutpostSimulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.Map.Targets.Length; i++)
                if (sim.Map.Targets[i].IsOreFill && sim.Run.Targets[i].Destroyed) n++;
            return n;
        }

        static int DestroyedDebris(OutpostSimulation sim)
        {
            int n = 0;
            for (int i = 0; i < sim.Map.Targets.Length; i++)
                if (sim.Map.Targets[i].IsDebris && sim.Run.Targets[i].Destroyed) n++;
            return n;
        }

        static int Chebyshev(OutpostMap map, int a, int b) => Math.Max(Math.Abs(map.Col(a) - map.Col(b)), Math.Abs(map.Row(a) - map.Row(b)));

        /// <summary>
        /// Клетки хотя бы одного самого дешёвого пути от базы до рабочей клетки замка спасения по модели маршрута первой сессии
        /// (Docs/Rework/FIRST_SESSION.md): шаг на пол — 1, на клетку живой цели (и ворот) — 1 + <see cref="MineRescuePlacer.TargetStepCost"/>;
        /// порода, препятствия, заводы и сам замок непроходимы. Свой расчёт, без порядка обхода ядра: Дейкстра от базы и от рабочих
        /// клеток замка, клетка на пути ⇔ сумма двух расстояний равна цене лучшего пути.
        /// </summary>
        static HashSet<int> CheapestRouteCells(OutpostMap map, int lockId)
        {
            int Enter(int c)
            {
                if (!map.IsPlayable(c)) return -1;
                int id = map.InitialOccupant[c];
                if (id < 0) return 1;
                var t = map.Target(id);
                return id == lockId || t == null || t.IsFactory ? -1 : 1 + MineRescuePlacer.TargetStepCost;
            }
            IEnumerable<int> Near(int c)
            {
                int col = map.Col(c), row = map.Row(c);
                if (row > 0) yield return c - map.Width;
                if (col > 0) yield return c - 1;
                if (col + 1 < map.Width) yield return c + 1;
                if (row + 1 < map.Rows) yield return c + map.Width;
            }
            // toward = false: цена от источников до клетки (вход в неё включён); true: цена от клетки до источников (вход в неё — нет).
            long[] Dijkstra(IEnumerable<int> sources, bool toward)
            {
                var dist = Enumerable.Repeat(long.MaxValue, map.CellCount).ToArray();
                var open = new SortedSet<(long Cost, int Cell)>();
                foreach (int s in sources)
                {
                    dist[s] = 0;
                    open.Add((0, s));
                }
                while (open.Count > 0)
                {
                    var top = open.Min;
                    open.Remove(top);
                    if (top.Cost != dist[top.Cell]) continue;
                    foreach (int nb in Near(top.Cell))
                    {
                        int step = toward ? Enter(top.Cell) : Enter(nb);
                        if (step < 0 || (toward && Enter(nb) < 0 && nb != map.IntakeCell)) continue;
                        long next = top.Cost + step;
                        if (next >= dist[nb]) continue;
                        if (dist[nb] != long.MaxValue) open.Remove((dist[nb], nb));
                        dist[nb] = next;
                        open.Add((next, nb));
                    }
                }
                return dist;
            }
            var goals = map.Target(lockId).WorkCells;
            var from = Dijkstra(new[] { map.IntakeCell }, false);
            var to = Dijkstra(goals, true);
            long best = goals.Min(g => from[g]);
            var cells = new HashSet<int>();
            if (best == long.MaxValue) return cells;
            for (int c = 0; c < map.CellCount; c++)
                if (from[c] != long.MaxValue && to[c] != long.MaxValue && from[c] + to[c] == best) cells.Add(c);
            return cells;
        }

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
                char ch = t.IsDebris ? 'd' : t.IsOreFill ? '*' : t.Kind == 'r' ? 'o' : t.Kind == 'x' ? 'X' : t.Kind == 's' ? 'F' : 'G';
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
