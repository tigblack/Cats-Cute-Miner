using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Импорт 144 карт, адаптер карты и баланса, статическая доступность (ТЗ §8, §12.4; CORE_SPEC §4).
    /// Эталон — Docs/TZ/analysis/outpost_v2_audit.json и независимый разбор строки Grid в самих тестах.
    /// </summary>
    [TestFixture]
    public class OutpostImportTests
    {
        static IEnumerable<int> Zones() => AllZones();

        // ───────────────────────────── Источник ─────────────────────────────

        [Test]
        public void Source_Sha256_MatchesAuditAndTz()
        {
            Assert.AreEqual(SourceSha256, (string)Audit["source_sha256"]);
            Assert.AreEqual(SourceSha256, Repo.Sha256);
            Assert.AreEqual(144, (int)Audit["zone_count"]);
            Assert.AreEqual(144, Repo.ZoneCount);
        }

        // ТЗ §8.1: размеры поля и число токенов каждого вида по зоне.
        [TestCaseSource(nameof(Zones))]
        public void Import_SizeAndTokenCounts_MatchAudit(int zone)
        {
            var src = Source(zone);
            var az = AuditZone(zone);
            Assert.AreEqual("zone" + zone, src.ZoneId);
            Assert.AreEqual(zone, src.Number);
            Assert.AreEqual((int)az["width"], src.Width);
            Assert.AreEqual((int)az["depth"], src.Depth);
            Assert.AreEqual(src.Width * src.Depth, src.Tokens.Length, "ровно WidthCells·DepthCells токенов");
            var counts = new Dictionary<string, int>();
            foreach (var t in src.Tokens)
            {
                string k = t == null ? "." : t.Kind.ToString();
                counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
            }
            var expected = ((JObject)az["tokens"]).Properties().ToDictionary(p => p.Name, p => (int)p.Value);
            CollectionAssert.AreEquivalent(expected, counts, "zone" + zone);
        }

        // ТЗ §8.1: схема полей токена, column = index % W, row = index / W, footprint (col + dx, row − dz).
        [TestCaseSource(nameof(Zones))]
        public void Import_TokensAndFootprints_MatchIndependentParse(int zone)
        {
            var src = Source(zone);
            var raw = Repo.Zone(zone).Grid.Split(',');
            int w = src.Width;
            for (int i = 0; i < raw.Length; i++)
            {
                string at = "zone" + zone + "#" + i;
                if (raw[i] == ".")
                {
                    Assert.IsNull(src.Tokens[i], at);
                    continue;
                }
                var f = raw[i].Split(':');
                var t = src.Tokens[i];
                Assert.IsNotNull(t, at);
                Assert.AreEqual(i, t.Index, at);
                Assert.AreEqual(f[0][0], t.Kind, at);
                Assert.AreEqual(f[1], t.TypeId, at);
                Assert.AreEqual(i % w, t.Column, at);
                Assert.AreEqual(i / w, t.Row, at);
                if (t.Kind != 'x') Assert.AreEqual(int.Parse(f[2], CultureInfo.InvariantCulture), t.Level, at);
                if (t.Kind == 's')
                {
                    Assert.AreEqual(f[3], t.RewardId, at);
                    Assert.AreEqual(f[4], t.CardId, at);
                    Assert.AreEqual(int.Parse(f[5], CultureInfo.InvariantCulture), t.CardLevel, at);
                }
                if (t.Kind == 'p' || t.Kind == 'e') Assert.AreEqual(f[3], t.RewardId, at);

                var expectedCells = new List<int>();
                if (t.Kind == 'r' || t.Kind == 's' || t.Kind == 'x')
                {
                    int fw, fd;
                    if (t.Kind == 'r') { fw = Repo.Rocks[t.TypeId].WidthCells; fd = Repo.Rocks[t.TypeId].DepthCells; }
                    else if (t.Kind == 's') { fw = Repo.Shafts[t.TypeId].WidthCells; fd = Repo.Shafts[t.TypeId].DepthCells; }
                    else { fw = Repo.Obstructions[t.TypeId].WidthCells; fd = Repo.Obstructions[t.TypeId].DepthCells; }
                    for (int dx = 0; dx < fw; dx++)
                        for (int dz = 0; dz < fd; dz++)
                            expectedCells.Add((t.Row - dz) * w + t.Column + dx);
                }
                expectedCells.Sort();
                CollectionAssert.AreEqual(expectedCells, (t.Cells ?? new int[0]).OrderBy(c => c).ToArray(), "footprint " + at);
            }
        }

        // ТЗ §8.1: «amethyst в zone2 с якорем (0, 5) и размером 2 × 3 занимает столбцы 0–1 и строки 3–5».
        [Test]
        public void Footprint_Zone2Amethyst_TzExample()
        {
            var t = Map(2).Targets.Single(x => x.TypeId == "amethyst");
            Assert.AreEqual(35, t.Id, "якорь (0, 5) = 5·7 + 0");
            Assert.AreEqual('s', t.Kind);
            Assert.AreEqual(0, t.Column);
            Assert.AreEqual(5, t.Row);
            CollectionAssert.AreEqual(new[] { 21, 22, 28, 29, 35, 36 }, t.Cells.OrderBy(c => c).ToArray());
        }

        // ───────────────────────────── Адаптер карты ─────────────────────────────

        // ТЗ §8.3: площадка Depth..Depth+2, спавн (3, Depth+1), места (2, Depth+1) и (4, Depth+1); CORE_SPEC §4.2.
        [TestCaseSource(nameof(Zones))]
        public void Adapter_BasePadSpawnDepotExit(int zone)
        {
            var src = Source(zone);
            var map = Map(zone);
            int w = src.Width, d = src.Depth;
            Assert.AreEqual(w, map.Width);
            Assert.AreEqual(d + 3, map.Rows, "Rows = Depth + 3");
            int spawn = (d + 1) * w + 3;
            Assert.AreEqual(spawn, map.SpawnCell);
            Assert.AreEqual(spawn, map.IntakeCell);
            CollectionAssert.AreEqual(new[] { (d + 1) * w + 2, (d + 1) * w + 4 }, map.DepotCells);
            CollectionAssert.AreEquivalent(Enumerable.Range(d * w, 3 * w), map.CampCells, "21 клетка площадки");
            int exit = src.Tokens.Single(t => t != null && t.Kind == 'e').Index;
            Assert.AreEqual(exit, map.ExitCell, "ExitCell — клетка токена e");
            foreach (var t in map.Targets)
                Assert.IsTrue((t.Cells ?? new int[0]).All(c => c < d * w), "площадка базы свободна: #" + t.Id);
        }

        // ТЗ §8.3: c и e — проходимые маркеры; вся старая искусственная занятость (кузница 2×3, выход 3×1, пушка, merge) убрана.
        [TestCaseSource(nameof(Zones))]
        public void Adapter_OccupancyIsExactlyRsxFootprints_CAndEPassable(int zone)
        {
            var src = Source(zone);
            var map = Map(zone);
            Assert.IsFalse(map.Targets.Any(t => t.Kind == 'c' || t.Kind == 'e'), "c/e не являются целями");
            var expected = new SortedSet<int>(src.Tokens.Where(t => t != null && "rsx".IndexOf(t.Kind) >= 0).SelectMany(t => t.Cells));
            var actual = new SortedSet<int>(map.Targets.SelectMany(t => t.Cells ?? new int[0]));
            CollectionAssert.AreEqual(expected, actual, "занятые клетки = только footprint r/s/x");
            foreach (var marker in src.Tokens.Where(t => t != null && (t.Kind == 'c' || t.Kind == 'e')))
                Assert.IsFalse(actual.Contains(marker.Index), "клетка " + marker.Kind + " свободна: " + marker.Index);
        }

        // ТЗ §8.4: ворота по gateRow убыв., затем по индексу; сектор r/s/x = count(gateRow > maxOccupiedRow).
        [TestCaseSource(nameof(Zones))]
        public void Adapter_TargetsGatesAndSectors(int zone)
        {
            var src = Source(zone);
            var map = Map(zone);
            int w = src.Width;
            var az = AuditZone(zone);

            var anchors = src.Tokens.Where(t => t != null && "rsxp".IndexOf(t.Kind) >= 0).Select(t => t.Index).ToArray();
            CollectionAssert.AreEqual(anchors, map.Targets.Select(t => t.Id).ToArray(), "цели по возрастанию Id = индекса якоря");
            foreach (var t in map.Targets)
            {
                var tok = src.Tokens[t.Id];
                Assert.AreEqual(tok.Kind, t.Kind);
                Assert.AreEqual(tok.TypeId, t.TypeId);
                Assert.AreEqual(t.Id % w, t.Column);
                Assert.AreEqual(t.Id / w, t.Row);
            }

            var gateTokens = src.Tokens.Where(t => t != null && t.Kind == 'p').OrderByDescending(t => t.Row).ThenBy(t => t.Index).ToList();
            Assert.AreEqual(gateTokens.Count, map.Gates.Length);
            for (int k = 0; k < gateTokens.Count; k++)
            {
                Assert.AreEqual(gateTokens[k].Index, map.Gates[k].TargetId, "порядок ворот k=" + k);
                Assert.AreEqual(gateTokens[k].Row, map.Gates[k].Row);
                Assert.AreEqual(k, map.Gates[k].Order, "Order 0 — нижние");
                if (k > 0) Assert.Less(map.Gates[k].Row, map.Gates[k - 1].Row, "строки ворот строго убывают");
            }

            var gateRows = gateTokens.Select(g => g.Row).ToArray();
            foreach (var t in map.Targets)
            {
                if (t.Kind == 'p')
                {
                    Assert.IsEmpty(t.Cells ?? new int[0], "у ворот нет footprint");
                    Assert.AreEqual(gateTokens.FindIndex(g => g.Index == t.Id), t.Sector, "сектор ворот = Order");
                }
                else
                {
                    int maxRow = t.Cells.Max(c => c / w);
                    Assert.AreEqual(gateRows.Count(r => r > maxRow), t.Sector, "сектор #" + t.Id);
                }
            }

            int rsx = map.Targets.Count(t => t.Kind != 'p');
            Assert.AreEqual(rsx + map.Gates.Length, map.RequiredCount, "RequiredCount = r/s/x + ворота");
            Assert.AreEqual((int)az["obligatory_targets"], map.RequiredCount);
        }

        // ТЗ §8.4: «на строках ворот находятся 560 обязательных боковых завалов wall1x1vL/R»; они в нижнем секторе.
        [Test]
        public void GateRowWalls_BelongToLowerSector_Total560()
        {
            int walls = 0;
            foreach (int zone in AllZones())
            {
                var map = Map(zone);
                foreach (var g in map.Gates)
                    foreach (var t in map.Targets.Where(x => x.Kind != 'p' && x.Row == g.Row))
                    {
                        StringAssert.StartsWith("wall1x1v", t.TypeId, "zone" + zone + "#" + t.Id);
                        Assert.AreEqual(g.Order, t.Sector, "zone" + zone + "#" + t.Id + " относится к нижнему сектору ворот");
                        walls++;
                    }
            }
            Assert.AreEqual(560, walls);
        }

        // CORE_SPEC §4.2: кандидаты рабочих клеток — соседи footprint по 4 направлениям (включая площадку), по возрастанию;
        // для ворот — вся строка ворот.
        [TestCaseSource(nameof(Zones))]
        public void Adapter_WorkCells_MatchIndependentNeighbours(int zone)
        {
            var map = Map(zone);
            int w = map.Width, rows = map.Rows;
            foreach (var t in map.Targets)
            {
                int[] expected;
                if (t.Kind == 'p')
                {
                    expected = Enumerable.Range(0, w).Select(col => t.Row * w + col).ToArray();
                }
                else
                {
                    var own = new HashSet<int>(t.Cells);
                    var set = new SortedSet<int>();
                    foreach (int cell in t.Cells)
                    {
                        int col = cell % w, row = cell / w;
                        foreach (var (dc, dr) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
                        {
                            int c2 = col + dc, r2 = row + dr;
                            if (c2 < 0 || c2 >= w || r2 < 0 || r2 >= rows) continue;
                            int n = r2 * w + c2;
                            if (!own.Contains(n)) set.Add(n);
                        }
                    }
                    expected = set.ToArray();
                }
                CollectionAssert.AreEqual(expected, t.WorkCells, "zone" + zone + "#" + t.Id);
            }
        }

        // ───────────────────────────── Адаптер баланса ─────────────────────────────

        // ТЗ §12.4: исходный HP из MiningTargetHealths[typeId][level−1]; rank01 = j/(u−1); x → 0.5; точный ceil.
        [TestCaseSource(nameof(Zones))]
        public void Balance_PerTarget_MatchesIndependentFormula(int zone)
        {
            var balances = Balances(zone);
            var health = Repo.Root.MiningTargetHealths;
            var sourceHp = new Dictionary<int, double>();
            foreach (var b in balances)
                if (b.Target.Kind != 'x')
                {
                    var tok = Source(zone).Tokens[b.Target.Id];
                    sourceHp[b.Target.Id] = health[tok.TypeId][tok.Level - 1];
                }
            var unique = sourceHp.Values.Distinct().OrderBy(v => v).ToList();
            Assert.AreEqual((int)AuditZone(zone)["unique_source_hp"], unique.Count, "уникальные исходные HP r/s/p");

            foreach (var b in balances)
            {
                string at = "zone" + zone + "#" + b.Target.Id;
                FracA rank = b.Target.Kind == 'x'
                    ? FracA.Of(1, 2)
                    : unique.Count == 1 ? FracA.Of(0) : FracA.Of(unique.IndexOf(sourceHp[b.Target.Id]), unique.Count - 1);
                AssertExact(b.Rank01, rank, "rank01 " + at);
                int area = b.Target.Kind == 'p' ? 1 : b.Target.Cells.Length;
                long hp = ExpectedMaxHp(zone, rank, area, b.Target.Kind);
                Assert.AreEqual(hp, b.MaxHp, "MaxHp " + at);
                Assert.AreEqual(ExpectedUnits(hp), b.Units, "TotalCargoUnits " + at);
                AssertExact(b.ValueFactor, TypeValueFactor(b.Target.Kind), "ValueFactor " + at);
            }
        }

        // Аудит: min/max адаптированного HP, сумма HP, порций и базового золота по зоне.
        [TestCaseSource(nameof(Zones))]
        public void Balance_ZoneTotals_MatchAudit(int zone)
        {
            var az = AuditZone(zone);
            var balances = Balances(zone);
            var stats = Stats(zone: zone);
            Assert.AreEqual((long)az["min_adapted_hp"], balances.Min(b => b.MaxHp), "min HP");
            Assert.AreEqual((long)az["max_adapted_hp"], balances.Max(b => b.MaxHp), "max HP");
            Assert.AreEqual((long)az["hp"], balances.Sum(b => b.MaxHp), "Σ HP");
            Assert.AreEqual((long)az["cargo_units"], balances.Sum(b => (long)b.Units), "Σ порций");
            BigInteger gold = 0, expectedGold = 0;
            foreach (var b in balances)
            {
                gold += CoinValue(stats, zone, b.ValueFactor) * b.Units;
                expectedGold += ExpectedCoinValue(zone, TypeValueFactor(b.Target.Kind), FracA.Of(1)) * b.Units;
            }
            Assert.AreEqual(AuditBig(az["base_gold"]), expectedGold, "самопроверка эталона");
            Assert.AreEqual(AuditBig(az["base_gold"]), gold, "Σ базового золота");
        }

        // ТЗ §12.4 «zone13 #78»: при шаге 1/200 — 318 HP; профиль b4 (шаг 1/25) — 444 HP.
        [Test]
        public void Balance_Zone13Index78_SelectedControl()
        {
            var controls = (JObject)AuditZone(13)["selected_target_controls"];
            Assert.AreEqual(444, (int)controls["78"]);
            Assert.AreEqual(444L, Balances(13).Single(b => b.Target.Id == 78).MaxHp);
        }

        // ───────────────────────────── Доступность ─────────────────────────────

        // ТЗ §8.5: последовательная расчистка проходит на каждой карте.
        [TestCaseSource(nameof(Zones))]
        public void Reachability_MatchesAudit(int zone)
        {
            var az = AuditZone(zone);
            var map = Map(zone);
            var report = Reach(zone);
            Assert.IsTrue((bool)az["static_clearance"]);
            Assert.IsTrue(report.Passed, "zone" + zone + ": статическая доступность");
            Assert.AreEqual((int)az["obligatory_targets"] - map.Gates.Length, report.Cleared, "расчищено r/s/x");
            Assert.AreEqual(map.Gates.Length, report.GatesOpened, "открыто ворот");
            Assert.AreEqual((int)az["clearance_waves"], report.Waves, "волны — копия алгоритма аудита");
        }

        // ───────────────────────────── Итоги по 144 картам ─────────────────────────────

        // ТЗ §8.1 и §8.5: 16 614 r, 1 232 s, 4 283 x, 280 p, по 144 e и c; 22 409 обязательных; 144/144, 22 129 и 280.
        [Test]
        public void AllZones_GlobalTotals_MatchTzAndAudit()
        {
            var tokens = new Dictionary<char, int>();
            int required = 0, cleared = 0, gates = 0, passed = 0, minDepth = int.MaxValue, maxDepth = 0;
            long minHp = long.MaxValue, maxHp = 0;
            foreach (int zone in AllZones())
            {
                var src = Source(zone);
                Assert.AreEqual(7, src.Width, "ширина всех карт 7");
                minDepth = Math.Min(minDepth, src.Depth);
                maxDepth = Math.Max(maxDepth, src.Depth);
                foreach (var t in src.Tokens.Where(t => t != null))
                    tokens[t.Kind] = tokens.TryGetValue(t.Kind, out int c) ? c + 1 : 1;
                var map = Map(zone);
                required += map.RequiredCount;
                var report = Reach(zone);
                if (report.Passed) passed++;
                cleared += report.Cleared;
                gates += report.GatesOpened;
                minHp = Math.Min(minHp, Balances(zone).Min(b => b.MaxHp));
                maxHp = Math.Max(maxHp, Balances(zone).Max(b => b.MaxHp));
            }
            Assert.AreEqual(16614, tokens['r']);
            Assert.AreEqual(1232, tokens['s']);
            Assert.AreEqual(4283, tokens['x']);
            Assert.AreEqual(280, tokens['p']);
            Assert.AreEqual(144, tokens['e']);
            Assert.AreEqual(144, tokens['c']);
            var auditCounts = (JObject)Audit["token_counts"];
            foreach (var kv in tokens) Assert.AreEqual((int)auditCounts[kv.Key.ToString()], kv.Value, "аудит " + kv.Key);
            Assert.AreEqual(22409, required, "обязательных r/s/x/p");
            Assert.AreEqual(144, passed, "статическая доступность 144/144");
            Assert.AreEqual(22129, cleared, "расчищено r/s/x");
            Assert.AreEqual(280, gates, "открыто ворот");
            Assert.AreEqual(12, minDepth);
            Assert.AreEqual(64, maxDepth);
            var hpRange = (JArray)Audit["adapted_hp_range"];
            Assert.AreEqual((long)hpRange[0], minHp);
            Assert.AreEqual((long)hpRange[1], maxHp);
            Assert.AreEqual(20L, minHp);
            Assert.AreEqual(3733L, maxHp, "профиль b4: все новые цели имеют 20–3733 HP (ТЗ §12.2 с шагом 1/200 — 20–961)");
        }

        // ───────────────────────────── Ошибки данных ─────────────────────────────

        static int FirstIndex(int zone, string prefix)
        {
            var raw = Repo.Zone(zone).Grid.Split(',');
            for (int i = 0; i < raw.Length; i++)
                if (raw[i].StartsWith(prefix, StringComparison.Ordinal)) return i;
            throw new InvalidOperationException("нет токена " + prefix + " в zone" + zone);
        }

        /// <summary>ТЗ §12.4/CORE_SPEC §4.1: ошибка данных содержит зону и индекс токена.</summary>
        static void AssertDataError(BalanceRepository repo, int zone, params int[] allowedIndices)
        {
            var ex = Assert.Throws<OutpostDataException>(() => RunPipeline(repo, zone));
            StringAssert.Contains("zone" + zone, ex.Message, "в сообщении есть зона");
            Assert.AreEqual("zone" + zone, ex.ZoneId);
            if (allowedIndices.Length > 0)
            {
                CollectionAssert.Contains(allowedIndices, ex.TokenIndex, "индекс токена в ошибке: " + ex.Message);
                StringAssert.Contains(ex.TokenIndex.ToString(CultureInfo.InvariantCulture), ex.Message, "в сообщении есть индекс");
            }
        }

        [TestCase("r:rockonboardingmine1", "r: 2 поля вместо 3")]
        [TestCase("r:rockonboardingmine1:1:extra", "r: 4 поля вместо 3")]
        [TestCase("s:amethyst:1", "s: 3 поля вместо 6")]
        [TestCase("x", "x: 1 поле вместо 2")]
        [TestCase("p:checkpoint:1", "p: 3 поля вместо 4")]
        [TestCase("q:rockonboardingmine1:1", "неизвестный вид токена")]
        public void DataError_BadToken_ThrowsWithZoneAndIndex(string badToken, string why)
        {
            int index = FirstIndex(1, "r:");
            var repo = RepoWithGrid(1, t => t[index] = badToken);
            AssertDataError(repo, 1, index);
        }

        [TestCase("r:nosuchrock:1")]
        [TestCase("x:nosuchblock")]
        [TestCase("s:nosuchshaft:1:a:b:1")]
        public void DataError_UnknownType_ThrowsWithZoneAndIndex(string badToken)
        {
            int index = FirstIndex(1, "r:");
            var repo = RepoWithGrid(1, t => t[index] = badToken);
            AssertDataError(repo, 1, index);
        }

        // ТЗ §12.4: «уровень вне диапазона — ошибка данных с зоной и индексом токена» (таблица rockonboardingmine1 — 99 уровней).
        [TestCase("r:rockonboardingmine1:0")]
        [TestCase("r:rockonboardingmine1:100")]
        [TestCase("r:rockonboardingmine1:-1")]
        public void DataError_LevelOutOfRange_ThrowsWithZoneAndIndex(string badToken)
        {
            int index = FirstIndex(1, "r:");
            var repo = RepoWithGrid(1, t => t[index] = badToken);
            AssertDataError(repo, 1, index);
        }

        // ТЗ §12.4: «неположительный HP … — ошибка данных».
        [Test]
        public void DataError_NonPositiveSourceHp_ThrowsWithZoneAndIndex()
        {
            var repo = FreshRepo();
            repo.Root.MiningTargetHealths["rockonboardingmine1"][0] = 0;
            // в zone1 уровень 1 rockonboardingmine1 стоит в индексах 16, 23, 24, 30, 31
            AssertDataError(repo, 1, 16, 23, 24, 30, 31);
        }

        // CORE_SPEC §4.1: выход footprint за поле — ошибка. zone1 #3 = (3, 0): block1x2 занял бы строку −1.
        [Test]
        public void DataError_FootprintOutOfBounds_Throws()
        {
            Assert.AreEqual(".", Repo.Zone(1).Grid.Split(',')[3]);
            var repo = RepoWithGrid(1, t => t[3] = "x:block1x2");
            AssertDataError(repo, 1, 3);
        }

        // CORE_SPEC §4.1: пересечение footprint — ошибка. zone1 #0 = (0, 0) уже занят block2x2 из #7.
        [Test]
        public void DataError_FootprintOverlap_Throws()
        {
            var repo = RepoWithGrid(1, t => t[0] = "r:rockonboardingmine1:1");
            AssertDataError(repo, 1, 0, 7);
        }

        // CORE_SPEC §4.1: ровно один c и один e.
        [Test]
        public void DataError_SecondExitOrMissingCart_Throws()
        {
            AssertDataError(RepoWithGrid(1, t => t[3] = "e:exit:1:dup"), 1);
            int cart = FirstIndex(1, "c:");
            AssertDataError(RepoWithGrid(1, t => t[cart] = "."), 1);
        }

        // ТЗ §8.1: «ровно WidthCells * DepthCells токенов».
        [Test]
        public void DataError_TokenCountMismatch_Throws()
        {
            AssertDataError(RepoWithGridText(1, g => g.Substring(0, g.LastIndexOf(','))), 1);
            AssertDataError(RepoWithGridText(1, g => g + ",."), 1);
        }

        // ТЗ §8.4: повтор строки ворот — ошибка данных. Ставим вторые ворота на строку существующих.
        [Test]
        public void DataError_DuplicateGateRow_Throws()
        {
            int zone = AllZones().First(z => Source(z).Tokens.Any(t => t != null && t.Kind == 'p'));
            var src = Source(zone);
            var gate = src.Tokens.First(t => t != null && t.Kind == 'p');
            int free = Enumerable.Range(gate.Row * src.Width, src.Width)
                .First(i => src.Tokens[i] == null && !Map(zone).Targets.Any(t => (t.Cells ?? new int[0]).Contains(i)));
            var raw = Repo.Zone(zone).Grid.Split(',');
            var repo = RepoWithGrid(zone, t => t[free] = raw[gate.Index]);
            AssertDataError(repo, zone);
        }
    }
}
