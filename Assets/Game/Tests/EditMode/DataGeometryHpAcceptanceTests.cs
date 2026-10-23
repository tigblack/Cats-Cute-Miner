using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Приёмочные проверки ТЗ 17.1, группы 1–3: импорт и валидатор, геометрия, HP.
    /// Дополняют BalanceLoadTests и LayoutTests (DataAndLayoutTests.cs) и не повторяют их проверок.
    /// Префикс имени теста — номер группы 17.1; категория NUnit — та же группа.
    /// В имени категории нет '-' (NUnit в Unity запрещает ',', '!', '+', '-' в категориях), поэтому «17.1 группа N».
    /// </summary>
    public class DataGeometryHpAcceptanceTests
    {
        const string G1 = "17.1 группа 1 Импорт и валидатор";
        const string G2 = "17.1 группа 2 Геометрия";
        const string G3 = "17.1 группа 3 HP";

        // ТЗ 3: авторитетный входной файл.
        const string TzSha256 = "8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5";
        const long TzSizeBytes = 2254970L;

        static BalanceRepository B => TestData.Balance;

        static ValidationReport s_CleanReport;
        static ValidationReport CleanReport => s_CleanReport ?? (s_CleanReport = BalanceValidator.Validate(B));

        static GameSession NewSession(int zone, string slot = SaveSlots.Debug) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, 1_000_000L, zone);

        static (int, int) Cr(ZoneLayout l, int cell) => (l.Col(cell), l.Row(cell));
        static List<(int, int)> Sorted(ZoneLayout l, IEnumerable<int> cells) => cells.Select(c => Cr(l, c)).OrderBy(t => t).ToList();
        static List<(int, int)> Sorted(IEnumerable<(int, int)> cells) => cells.OrderBy(t => t).ToList();
        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static List<int> WorkCellsOf(GameSession s, int ord)
        {
            var list = new List<int>();
            s.WorkCells(ord, list);
            return list;
        }

        static bool SideAdjacent(ZoneLayout l, int a, int b) =>
            Math.Abs(l.Col(a) - l.Col(b)) + Math.Abs(l.Row(a) - l.Row(b)) == 1;

        static void CompleteRocks(GameSession s, Func<ZoneObject, bool> filter)
        {
            foreach (var o in s.Layout.Objects)
                if (o.Kind == ObjectKind.Rock && filter(o)) s.DebugCompleteTarget(o.Ordinal);
        }

        static void OpenGates(GameSession s, int count)
        {
            for (int k = 0; k < count; k++)
            {
                var g = s.Layout.Gates[k];
                s.DebugCompleteTarget(g.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.OpenGate(g.Ordinal), s.Layout.ZoneId + ": открытие ворот " + k);
            }
        }

        // ───────────── Испорченные копии JSON (в памяти) ─────────────

        static JObject s_SourceJson;

        /// <summary>Исходный JSON деревом: даты не распознаются, дробные — double, целые больше Int64 — BigInteger.</summary>
        static JObject SourceJson
        {
            get
            {
                if (s_SourceJson != null) return s_SourceJson;
                var text = new UTF8Encoding(false, true).GetString(TestData.BalanceBytes);
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    s_SourceJson = JObject.Load(reader);
                return s_SourceJson;
            }
        }

        static byte[] Mutated(Action<JObject> mutate)
        {
            var root = (JObject)SourceJson.DeepClone();
            mutate(root);
            return Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
        }

        static BalanceRepository LoadMutated(Action<JObject> mutate) => BalanceRepository.Load(Mutated(mutate), requireExpectedHash: false);

        static JObject ZoneJson(JObject root, int n) =>
            ((JArray)root["Zones"]).Cast<JObject>().Single(z => (string)z["Id"] == "zone" + n.ToString(CultureInfo.InvariantCulture));

        static string[] Tokens(JObject root, int n) => ((string)ZoneJson(root, n)["Grid"]).Split(',');

        static void SetToken(JObject root, int n, int index, string token)
        {
            var t = Tokens(root, n);
            t[index] = token;
            ZoneJson(root, n)["Grid"] = string.Join(",", t);
        }

        static int FirstToken(JObject root, int n, string prefix) =>
            Array.FindIndex(Tokens(root, n), t => t.StartsWith(prefix, StringComparison.Ordinal));

        static JObject ById(JObject root, string section, string id) =>
            ((JArray)root[section]).Cast<JObject>().Single(x => (string)x["Id"] == id);

        static string ZonePath(int zone) => "Zones.zone" + zone.ToString(CultureInfo.InvariantCulture);
        static string GridPath(int zone, int index) => ZonePath(zone) + ".Grid[" + index.ToString(CultureInfo.InvariantCulture) + "]";

        static void AssertIssue(ValidationReport r, string path, bool warning, params string[] fragments)
        {
            bool found = r.Issues.Any(i => i.IsWarning == warning && i.Path == path && fragments.All(f => i.Message.Contains(f)));
            Assert.IsTrue(found, "ожидалась " + (warning ? "предупреждение " : "ошибка ") + path + " с «" + string.Join("», «", fragments) + "»\n" + r.ToText());
        }

        static void AssertError(ValidationReport r, string path, params string[] fragments) => AssertIssue(r, path, false, fragments);

        static int ZoneOfPath(string path)
        {
            const string prefix = "Zones.zone";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return -1;
            int end = prefix.Length;
            while (end < path.Length && char.IsDigit(path[end])) end++;
            return int.Parse(path.Substring(prefix.Length, end - prefix.Length), CultureInfo.InvariantCulture);
        }

        // ═════════════════════════════ Группа 1. Импорт и валидатор ═════════════════════════════

        [Test, Category(G1)]
        public void G1_FileBytesMatchTzHashSizeAndEncoding()
        {
            var bytes = TestData.BalanceBytes;
            Assert.AreEqual(TzSizeBytes, bytes.LongLength, "размер файла по ТЗ 3");
            string hash;
            using (var sha = SHA256.Create())
                hash = string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
            Assert.AreEqual(TzSha256, hash, "SHA-256, посчитанный независимо от загрузчика, совпадает с ТЗ 3");
            Assert.AreEqual(TzSha256, BalanceRepository.ExpectedSha256, "константа загрузчика равна значению из ТЗ 3");
            Assert.AreEqual(TzSha256, BalanceRepository.ComputeSha256(bytes), "hash загрузчика считается от всего файла");
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 без BOM");
            Assert.AreEqual(-1, Array.IndexOf(bytes, (byte)'\r'), "переводы строк LF, символов CR нет");
            Assert.DoesNotThrow(() => new UTF8Encoding(false, true).GetString(bytes), "файл — корректный UTF-8");
            Assert.AreEqual(TzSha256, CleanReport.Sha256, "отчёт валидатора содержит SHA-256");
            Assert.AreEqual(TzSizeBytes, CleanReport.SizeBytes, "отчёт валидатора содержит размер");
        }

        [Test, Category(G1)]
        public void G1_CrlfOrBomCopyIsRejectedWithBothHashesInMessage()
        {
            var bytes = TestData.BalanceBytes;
            // ТЗ 4.1: Git for Windows может заменить LF на CRLF и изменить hash; BOM тоже меняет байты.
            var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\n", "\r\n"));
            var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
            foreach (var copy in new[] { crlf, bom })
            {
                string actual = BalanceRepository.ComputeSha256(copy);
                Assert.AreNotEqual(TzSha256, actual);
                var e = Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(copy), "игра не стартует с изменённым файлом");
                StringAssert.Contains(TzSha256, e.Message, "ошибка конфигурации называет ожидаемый hash");
                StringAssert.Contains(actual, e.Message, "и фактический hash");
                // Содержимое при этом читается: отказ вызван именно hash.
                var relaxed = BalanceRepository.Load(copy, requireExpectedHash: false);
                Assert.AreEqual(144, relaxed.ZoneCount);
                Assert.AreEqual(actual, relaxed.Sha256);
                Assert.AreEqual(copy.LongLength, relaxed.SizeBytes);
            }
        }

        [Test, Category(G1)]
        public void G1_EmptyOrTruncatedDocumentGivesConfigErrorWithPath()
        {
            Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(new byte[0], requireExpectedHash: false), "пустой файл");
            Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(null, requireExpectedHash: false), "нет файла");
            var half = TestData.BalanceBytes.Take(1_000_000).ToArray();
            var e = Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(half, requireExpectedHash: false),
                "обрезанный документ — ошибка конфигурации, а не необработанное исключение парсера");
            StringAssert.Contains("Path '", e.Message, "в ошибке есть путь поля");
            StringAssert.Contains("MiningTargetHealths", e.Message, "путь указывает на место обрыва");
        }

        [Test, Category(G1)]
        public void G1_ZonesAreOrderedNumericallyEvenIfSourceIsLexicographic()
        {
            List<string> shuffled = null;
            var repo = LoadMutated(root =>
            {
                var zones = ((JArray)root["Zones"]).Cast<JObject>().OrderBy(z => (string)z["Id"], StringComparer.Ordinal).ToList();
                shuffled = zones.Select(z => (string)z["Id"]).ToList();
                root["Zones"] = new JArray(zones);
            });
            Assert.AreEqual("zone10", shuffled[1], "в испорченной копии зоны отсортированы лексикографически");
            Assert.AreEqual(144, repo.ZoneCount);
            for (int n = 1; n <= 144; n++)
                Assert.AreEqual("zone" + n.ToString(CultureInfo.InvariantCulture), repo.Zones[n - 1].Id, "ТЗ 6.7: порядок по числовому суффиксу");
            Assert.AreEqual("zone2", repo.Zone(2).Id);
            Assert.AreEqual("zone10", repo.Zone(10).Id);
            var report = BalanceValidator.Validate(repo);
            Assert.AreEqual(0, report.ErrorCount, report.ToText());
            CollectionAssert.AreEqual(Enumerable.Range(1, 144).Select(n => "zone" + n.ToString(CultureInfo.InvariantCulture)), report.Zones.Select(z => z.ZoneId));

            Assert.Throws<ArgumentOutOfRangeException>(() => B.Zone(0), "зоны нумеруются с 1");
            Assert.Throws<ArgumentOutOfRangeException>(() => B.Zone(145), "зон 144");
            Assert.AreEqual(10, BalanceRepository.ZoneNumber("zone10"));
            Assert.AreEqual(144, BalanceRepository.ZoneNumber("zone144"));
            Assert.Throws<BalanceLoadException>(() => BalanceRepository.ZoneNumber("zoneX"));
            Assert.Throws<BalanceLoadException>(() => BalanceRepository.ZoneNumber("Zone1"));
        }

        [Test, Category(G1)]
        public void G1_EveryGridHasCellCountKnownTokensReferencesAndOneExitAndCart()
        {
            // ТЗ 3.3 и 6.2: токены только . r s x p e c; число полей r 3, s 6, x 2, p 4, e 4, c 3.
            var fieldCounts = new Dictionary<string, int> { { "r", 3 }, { "s", 6 }, { "x", 2 }, { "p", 4 }, { "e", 4 }, { "c", 3 } };
            int maxTargetLevel = 0, shafts = 0;
            for (int n = 1; n <= 144; n++)
            {
                var z = B.Zone(n);
                string ctx = z.Id;
                Assert.AreEqual(7, z.WidthCells, ctx + ": ширина 7");
                Assert.That(z.DepthCells, Is.InRange(12, 64), ctx + ": глубина 12…64");
                var tokens = z.Grid.Split(',');
                Assert.AreEqual(z.WidthCells * z.DepthCells, tokens.Length, ctx + ": число клеток = WidthCells × DepthCells");
                var layout = ZoneLayout.Build(B, n);
                var byIndex = layout.Objects.ToDictionary(o => o.Index);
                int anchors = 0, exits = 0, carts = 0;
                for (int i = 0; i < tokens.Length; i++)
                {
                    string t = tokens[i];
                    if (t == ".") continue;
                    anchors++;
                    var f = t.Split(':');
                    string at = ctx + "[" + i + "] " + t;
                    Assert.IsTrue(fieldCounts.ContainsKey(f[0]), at + ": известный токен");
                    Assert.AreEqual(fieldCounts[f[0]], f.Length, at + ": число полей");
                    switch (f[0])
                    {
                        case "r":
                            Assert.IsTrue(B.Rocks.ContainsKey(f[1]), at + ": камень найден");
                            break;
                        case "s":
                            Assert.IsTrue(B.Shafts.ContainsKey(f[1]), at + ": шахта найдена");
                            Assert.IsTrue(B.Gacha.ContainsKey(f[3]), at + ": награда активации найдена");
                            Assert.IsTrue(B.Cards.ContainsKey(f[4]), at + ": карточка найдена");
                            var card = B.Cards[f[4]];
                            Assert.IsTrue(card.IsManager && card.TargetIds.Contains(f[1]), at + ": manager-карта нацелена на эту шахту (ТЗ 6.2)");
                            Assert.That(int.Parse(f[5], CultureInfo.InvariantCulture), Is.InRange(1, B.CardMaxLevel(card.Rarity)), at + ": уровень карты в таблице улучшения");
                            shafts++;
                            break;
                        case "x":
                            Assert.IsTrue(B.Obstructions.ContainsKey(f[1]), at + ": препятствие найдено");
                            break;
                        case "p":
                            Assert.AreEqual("checkpoint", f[1], at);
                            Assert.IsTrue(B.Gacha.ContainsKey(f[3]), at + ": награда ворот найдена");
                            break;
                        case "e":
                            Assert.AreEqual("exit", f[1], at);
                            Assert.IsTrue(B.Gacha.ContainsKey(f[3]), at + ": награда выхода найдена");
                            exits++;
                            break;
                        case "c":
                            Assert.AreEqual(BalanceRepository.CartId, f[1], at);
                            Assert.AreEqual("1", f[2], at + ": уровень кузницы во всех зонах 1");
                            carts++;
                            break;
                    }
                    if (f[0] == "r" || f[0] == "s" || f[0] == "p" || f[0] == "e")
                    {
                        int lv = int.Parse(f[2], CultureInfo.InvariantCulture);
                        Assert.That(lv, Is.InRange(1, B.Root.MiningTargetHealths[f[1]].Length), at + ": уровень укладывается в HP-таблицу");
                        maxTargetLevel = Math.Max(maxTargetLevel, lv);
                    }
                    Assert.IsTrue(byIndex.TryGetValue(i, out var obj), at + ": токен разобран в объект");
                    Assert.AreEqual(t, obj.Token, at);
                }
                Assert.AreEqual(1, exits, ctx + ": ровно один e");
                Assert.AreEqual(1, carts, ctx + ": ровно один c");
                Assert.AreEqual(anchors, layout.Objects.Count, ctx + ": ни один токен не потерян и не добавлен");
                // ТЗ 3.3: справочные поля зоны.
                Assert.AreEqual(0, z.FogOfWarCheckpoint, ctx);
                Assert.AreEqual(0, z.DecoSeed, ctx);
                Assert.IsTrue(z.GridRowRanks == null || z.GridRowRanks.Type == JTokenType.Null, ctx + ": GridRowRanks = null");
                Assert.AreEqual(1, z.RankMultipliers.Length, ctx + ": RankMultipliers — ровно один объект");
            }
            Assert.AreEqual(71, maxTargetLevel, "ТЗ 3.3: максимальный уровень цели — 71");
            Assert.AreEqual(1232, shafts, "ТЗ 6.2: 1232 шахты");
            Assert.AreEqual(0, CleanReport.Issues.Count, "на исходном файле нет ни ошибок, ни предупреждений\n" + CleanReport.ToText());
        }

        [Test, Category(G1)]
        public void G1_CoreCurrencyMaxAndIntegerLiteralsBeyondInt64()
        {
            var root = SourceJson;
            var max = (JValue)root["BalanceProperties"][0]["AntiCheatSettings"]["CoreCurrencyMax"];
            Assert.AreEqual(JTokenType.Float, max.Type);
            double v = (double)max;
            Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v), "1e200 — конечное double");
            Assert.AreEqual(1e200, v, 1e200 * 1e-15, "ТЗ 17.1: CoreCurrencyMax = 1e200");
            Assert.AreEqual(1e200, GameNumber.FromDouble(v).ToDouble(), 1e200 * 1e-15, "значение представимо в GameNumber");

            // ТЗ 8.1: rockbasic L74 — «значение больше Int64»; в файле это целый литерал.
            var rb = (JValue)root["MiningTargetHealths"]["rockbasic"][73];
            Assert.AreEqual(JTokenType.Integer, rb.Type);
            Assert.IsInstanceOf<System.Numerics.BigInteger>(rb.Value, "литерал rockbasic[73] не помещается в Int64");
            var hel = (JValue)ById(root, "MineShafts", "heliodor")["CurrencyOutputMultiplier"];
            Assert.AreEqual(JTokenType.Integer, hel.Type);
            Assert.IsInstanceOf<System.Numerics.BigInteger>(hel.Value, "литерал heliodor.CurrencyOutputMultiplier не помещается в Int64");
        }

        [Test, Category(G1)]
        public void G1_CorruptedGridTokensReportZoneIdAndTokenIndex()
        {
            // Каждая порча — в своей зоне; ошибка должна нести путь Zones.zoneN.Grid[i] (ТЗ 6.2, 17.1).
            var expected = new List<(int zone, int index, string[] fragments)>();
            int overlapMine = -1, overlapOwner = -1;
            (int, int) overlapCell = default;
            var cleanZone15 = ZoneLayout.Build(B, 15);
            var repo = LoadMutated(root =>
            {
                void Put(int zone, int index, string token, params string[] fragments)
                {
                    Assert.GreaterOrEqual(index, 0, "zone" + zone + ": нет подходящего токена для порчи");
                    SetToken(root, zone, index, token);
                    expected.Add((zone, index, fragments));
                }
                string[] Fields(int zone, int index) => Tokens(root, zone)[index].Split(':');

                Put(1, 9, "q:mystery:1", "неизвестный токен", "q:mystery:1");
                Put(2, 9, "r:nosuchrock:1", "неизвестный камень nosuchrock");
                Put(3, FirstToken(root, 3, "r:"), "r:rockbasic", "полей");
                Put(4, FirstToken(root, 4, "r:"), "r:rockbasic:abc", "не число");
                Put(5, FirstToken(root, 5, "r:"), "r:rockbasic:100", "вне HP-таблицы");
                Put(6, FirstToken(root, 6, "r:"), "r:rockbasic:0", "вне HP-таблицы");
                int s7 = FirstToken(root, 7, "s:");
                var f7 = Fields(7, s7);
                f7[4] = "ca999";
                Put(7, s7, string.Join(":", f7), "неизвестная карточка ca999");
                int s8 = FirstToken(root, 8, "s:");
                var f8 = Fields(8, s8);
                f8[3] = "nosuchgacha";
                Put(8, s8, string.Join(":", f8), "неизвестная награда nosuchgacha");
                Put(9, FirstToken(root, 9, "e:"), "e:exit:1:nosuchgacha", "неизвестная награда nosuchgacha");
                Put(10, FirstToken(root, 10, "x:"), "x:nosuchblock", "неизвестное препятствие nosuchblock");
                Put(11, FirstToken(root, 11, "r:"), Tokens(root, 11)[FirstToken(root, 11, "e:")], "второй выход");
                // Нет кузницы — ошибка уровня зоны, без индекса токена.
                SetToken(root, 12, FirstToken(root, 12, "c:"), ".");
                expected.Add((12, -1, new[] { "нет кузницы c" }));
                // Число клеток не совпадает с WidthCells × DepthCells.
                var z13 = ZoneJson(root, 13);
                int cells13 = (int)z13["WidthCells"] * (int)z13["DepthCells"];
                z13["Grid"] = (string)z13["Grid"] + ",.";
                expected.Add((13, -1, new[] { "Grid: " + (cells13 + 1) + " токенов, ожидалось " + cells13 }));
                // Выход footprint за карту: блок 2×2 в столбце 6.
                var t14 = Tokens(root, 14);
                Put(14, Enumerable.Range(0, t14.Length).First(i => i % 7 == 6 && t14[i] == "."), "x:block2x2", "выходит за карту");
                Put(16, FirstToken(root, 16, "s:"), "s:amethyst:1", "полей");
                Put(17, FirstToken(root, 17, "c:"), "c:nosuchcart:1", "неизвестная кузница nosuchcart");
                int p18 = FirstToken(root, 18, "p:");
                var f18 = Fields(18, p18);
                f18[3] = "nosuchgacha";
                Put(18, p18, string.Join(":", f18), "неизвестная награда nosuchgacha");
                int p19 = FirstToken(root, 19, "p:");
                var f19 = Fields(19, p19);
                f19[2] = "100";
                Put(19, p19, string.Join(":", f19), "вне HP-таблицы");
                // Пересечение: якорь препятствия в клетке, уже занятой footprint r/s/x.
                var t15 = Tokens(root, 15);
                overlapMine = Enumerable.Range(0, t15.Length).First(i => t15[i] == "." && cleanZone15.CellOwner[i] >= 0 &&
                                                                        cleanZone15.Objects[cleanZone15.CellOwner[i]].Kind != ObjectKind.Cart &&
                                                                        cleanZone15.Objects[cleanZone15.CellOwner[i]].Kind != ObjectKind.Exit &&
                                                                        cleanZone15.Objects[cleanZone15.CellOwner[i]].Kind != ObjectKind.Gate);
                overlapOwner = cleanZone15.Objects[cleanZone15.CellOwner[overlapMine]].Index;
                overlapCell = Cr(cleanZone15, overlapMine);
                SetToken(root, 15, overlapMine, "x:block1x1v2");
            });

            var report = BalanceValidator.Validate(repo);
            foreach (var (zone, index, fragments) in expected)
                AssertError(report, index >= 0 ? GridPath(zone, index) : ZonePath(zone), fragments);
            string cellText = "(" + overlapCell.Item1 + "," + overlapCell.Item2 + ")";
            Assert.IsTrue(report.Issues.Any(i => !i.IsWarning && (i.Path == GridPath(15, overlapMine) || i.Path == GridPath(15, overlapOwner)) &&
                                                 i.Message.Contains("пересечение") && i.Message.Contains(cellText)),
                "пересечение footprint в zone15 " + cellText + "\n" + report.ToText());

            // Испорченные зоны не попадают в сводку, остальные проверяются как обычно.
            CollectionAssert.AreEqual(Enumerable.Range(20, 125).Select(n => "zone" + n.ToString(CultureInfo.InvariantCulture)), report.Zones.Select(z => z.ZoneId));
            Assert.IsFalse(report.Issues.Any(i => ZoneOfPath(i.Path) >= 20), "нетронутые зоны без ошибок\n" + report.ToText());

            // Неизвестный объект не подменяется стандартным камнем.
            var broken2 = ZoneLayout.Build(repo, 2);
            Assert.IsFalse(broken2.IsValid);
            Assert.IsFalse(broken2.Objects.Any(o => o.Index == 9), "неизвестный камень не превращается в объект");
            // Игра не стартует с испорченной зоной, сообщение называет зону и индекс токена.
            var e = Assert.Throws<InvalidOperationException>(() => GameSession.NewGame(repo, 1UL, SaveSlots.Main, 0L, 1));
            StringAssert.Contains("zone1[9]", e.Message);
        }

        [Test, Category(G1)]
        public void G1_WrongTypeInDocumentReportsFieldPath()
        {
            var cases = new (Action<JObject> mutate, string path)[]
            {
                (r => r["Zones"][0]["WidthCells"] = "seven", "Zones[0].WidthCells"),
                (r => r["MiningTargetHealths"]["rockbasic"][3] = "abc", "MiningTargetHealths.rockbasic[3]"),
                (r => r["Rocks"][0]["RewardCycle"] = 5, "Rocks[0].RewardCycle"),
            };
            foreach (var (mutate, path) in cases)
            {
                var bytes = Mutated(mutate);
                var e = Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(bytes, requireExpectedHash: false), path);
                StringAssert.Contains(path, e.Message, "ошибка конфигурации содержит путь поля");
            }
        }

        [Test, Category(G1)]
        public void G1_MissingSectionOrIdReportsFieldPath()
        {
            var cases = new (Action<JObject> mutate, string[] fragments)[]
            {
                (r => r.Remove("Rocks"), new[] { "Rocks" }),
                (r => r.Remove("MiningTargetHealths"), new[] { "MiningTargetHealths" }),
                (r => ((JObject)r["MineShafts"][3]).Remove("Id"), new[] { "MineShafts[3]", "Id" }),
                (r => r["BalanceProperties"] = new JArray(), new[] { "BalanceProperties" }),
                (r => ById(r, "Miners", BalanceRepository.MinerId)["Id"] = "nosuchminer", new[] { "Miners", BalanceRepository.MinerId }),
            };
            foreach (var (mutate, fragments) in cases)
            {
                var bytes = Mutated(mutate);
                var e = Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(bytes, requireExpectedHash: false), fragments[0]);
                foreach (var f in fragments) StringAssert.Contains(f, e.Message, "ошибка конфигурации называет раздел или поле");
            }
        }

        [Test, Category(G1)]
        public void G1_ValidatorReportsMissingFieldsAndBadValuesWithPath()
        {
            int rockbasicIn6 = FirstToken(SourceJson, 6, "r:rockbasic:");
            Assert.GreaterOrEqual(rockbasicIn6, 0);
            var repo = LoadMutated(root =>
            {
                ZoneJson(root, 1).Remove("Grid");
                ZoneJson(root, 3).Remove("RankMultipliers");
                ZoneJson(root, 4)["FogOfWarCheckpoint"] = 1;
                ById(root, "Rocks", "rocksoftcurrencysmall").Remove("RewardCycle");
                ((JObject)root["MiningTargetHealths"]).Remove("rockbasic");
                root["MiningTargetHealths"]["amethyst"][4] = 0;
                ById(root, "Gacha", "scg_free_01")["GuaranteedCardIds"][0] = "ca999";
                root["FreeGachaCycle"][0]["GachaIds"][0] = "nosuchgacha";
            });
            var report = BalanceValidator.Validate(repo);
            AssertError(report, ZonePath(1), "Grid");
            AssertError(report, ZonePath(3) + ".RankMultipliers");
            AssertIssue(report, ZonePath(4) + ".FogOfWarCheckpoint", true);
            AssertError(report, "Rocks.rocksoftcurrencysmall.RewardCycle", "пусто");
            AssertError(report, "MiningTargetHealths", "rockbasic");
            AssertError(report, GridPath(6, rockbasicIn6), "нет HP-таблицы для rockbasic");
            AssertError(report, "MiningTargetHealths.amethyst[4]", "недопустимое значение");
            AssertError(report, "Gacha.scg_free_01.GuaranteedCardIds", "ca999");
            AssertError(report, "FreeGachaCycle[0].GachaIds", "nosuchgacha");
        }

        [Test, Category(G1)]
        public void G1_ValidatorDetectsGeometryViolationsOf64()
        {
            // Пять проверок валидатора из ТЗ 6.4, каждая на искусственно испорченной зоне.
            var repo = LoadMutated(root =>
            {
                // zone1: свободная клетка (0,10) отрезана: сверху (0,9), справа кузница 2×3 (1,10), снизу препятствие (0,11).
                SetToken(root, 1, 9 * 7 + 0, "x:block1x1v2");
                // zone2: камень (6,11) окружён препятствиями и краем карты.
                SetToken(root, 2, 11 * 7 + 6, "r:rockbasic:1");
                SetToken(root, 2, 10 * 7 + 6, "x:block1x1v2");
                SetToken(root, 2, 11 * 7 + 5, "x:block1x1v2");
                // zone2: шахта amethyst (0,5) — все её соседи по стороне становятся препятствиями.
                foreach (var (col, row) in new[] { (2, 3), (2, 4), (2, 5), (1, 6) })
                    SetToken(root, 2, row * 7 + col, "x:block1x1v2");
                // zone5: строка ворот 11 открыта слева.
                SetToken(root, 5, 11 * 7 + 0, ".");
                // zone6: якорь препятствия в клетке выхода (3,0).
                SetToken(root, 6, 3, "x:block1x1v2");
            });
            var report = BalanceValidator.Validate(repo);
            AssertError(report, ZonePath(1), "сектор 0", "2 областей");
            AssertError(report, GridPath(2, 11 * 7 + 6), "недостижима");
            AssertError(report, GridPath(2, 5 * 7 + 0), "s:amethyst", "нет ни одной потенциальной рабочей клетки");
            AssertError(report, GridPath(5, 11 * 7 + 1), "строка ворот не перекрыта в столбце 0");
            AssertError(report, GridPath(6, 3), "пересечение", "e:exit", "(3,0)");
        }

        // ═════════════════════════════ Группа 2. Геометрия ═════════════════════════════

        [Test, Category(G2)]
        public void G2_RsxFootprintsOccupyExpectedCellsInAllZones()
        {
            var csv = File.ReadAllLines(TestData.ZonesSummaryPath).Skip(1).Where(l => l.Length > 0).Select(l => l.Split(','))
                .ToDictionary(f => f[0], f => int.Parse(f[7], CultureInfo.InvariantCulture));
            int shafts = 0, shaftsLeft = 0, shaftsRight = 0;
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                var rsxCells = new HashSet<int>();
                int area = 0;
                foreach (var o in l.Objects)
                {
                    string at = l.ZoneId + "[" + o.Index + "] " + o.Token;
                    // Кузница 2×3 (ТЗ 6.4): опора footprint — строкой ниже токена.
                    int anchorRow = o.Index / l.Width + (o.Kind == ObjectKind.Cart ? ZoneLayout.CartDepth - 2 : 0);
                    Assert.AreEqual((o.Index % l.Width, anchorRow), (o.Col, o.Row), at + ": якорь по линейному индексу (ТЗ 6.1)");
                    int w, d;
                    switch (o.Kind)
                    {
                        case ObjectKind.Rock: w = B.Rocks[o.TypeId].WidthCells; d = B.Rocks[o.TypeId].DepthCells; break;
                        case ObjectKind.Shaft:
                            w = B.Shafts[o.TypeId].WidthCells;
                            d = B.Shafts[o.TypeId].DepthCells;
                            Assert.AreEqual((2, 3), (w, d), at + ": все шахты 2×3");
                            shafts++;
                            if (o.Col <= 1) shaftsLeft++;
                            else if (o.Col >= 5) shaftsRight++;
                            break;
                        case ObjectKind.Obstruction: w = B.Obstructions[o.TypeId].WidthCells; d = B.Obstructions[o.TypeId].DepthCells; break;
                        default:
                            foreach (int c in o.Cells) Assert.AreEqual(o.Ordinal, l.CellOwner[c], at + ": клетки e/p/c не пересекаются с r/s/x");
                            continue;
                    }
                    var expected = new List<(int, int)>();
                    for (int dx = 0; dx < w; dx++)
                        for (int dz = 0; dz < d; dz++)
                            expected.Add((o.Col + dx, o.Row - dz));
                    Assert.IsTrue(expected.All(p => l.InBounds(p.Item1, p.Item2)), at + ": footprint внутри карты");
                    CollectionAssert.AreEqual(Sorted(expected), Sorted(l, o.Cells), at + ": footprint +column / −row (G-01)");
                    foreach (int c in o.Cells)
                    {
                        Assert.AreEqual(o.Ordinal, l.CellOwner[c], at);
                        Assert.IsTrue(rsxCells.Add(c), at + ": пересечение в клетке " + Cr(l, c));
                    }
                    area += w * d;
                }
                Assert.AreEqual(area, rsxCells.Count, l.ZoneId + ": 0 пересечений r/s/x");
                Assert.AreEqual(csv[l.ZoneId], rsxCells.Count, l.ZoneId + ": занятые r/s/x клетки как в zones_summary.csv");
                // Пушка у кузницы (ТЗ 7.2) — две клетки лагеря кузницы вне footprint из данных.
                int cannon = l.HasCannon ? ZoneLayout.CannonWidth * ZoneLayout.CannonDepth : 0;
                Assert.AreEqual(l.Objects.Sum(o => o.Cells.Length) + cannon, l.CellOwner.Count(x => x >= 0), l.ZoneId + ": CellOwner согласован с footprint и пушкой");
            }
            Assert.AreEqual(1232, shafts, "ТЗ 6.2: 1232 шахты");
            Assert.AreEqual(622, shaftsLeft, "ТЗ 6.5: 622 шахты в столбцах 0–1");
            Assert.AreEqual(610, shaftsRight, "ТЗ 6.5: 610 шахт в столбцах 5–6");
        }

        [Test, Category(G2)]
        public void G2_OnlyPlusColumnMinusRowFootprintHasNoConflicts()
        {
            // ТЗ 3.3: (пересечения, выходы за границы) по всем 144 зонам для четырёх направлений footprint r/s/x.
            var dirs = new (int sx, int sy, int overlaps, int oob)[]
            {
                (+1, -1, 0, 0),
                (+1, +1, 3372, 142),
                (-1, -1, 2257, 2269),
                (-1, +1, 3531, 2411),
            };
            foreach (var (sx, sy, expOverlaps, expOob) in dirs)
            {
                int overlaps = 0, oob = 0;
                for (int n = 1; n <= 144; n++)
                {
                    var z = B.Zone(n);
                    var tokens = z.Grid.Split(',');
                    var occ = new HashSet<(int, int)>();
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        var f = tokens[i].Split(':');
                        int w, d;
                        if (f[0] == "r") { w = B.Rocks[f[1]].WidthCells; d = B.Rocks[f[1]].DepthCells; }
                        else if (f[0] == "s") { w = B.Shafts[f[1]].WidthCells; d = B.Shafts[f[1]].DepthCells; }
                        else if (f[0] == "x") { w = B.Obstructions[f[1]].WidthCells; d = B.Obstructions[f[1]].DepthCells; }
                        else continue;
                        int col = i % z.WidthCells, row = i / z.WidthCells;
                        for (int dx = 0; dx < w; dx++)
                            for (int dz = 0; dz < d; dz++)
                            {
                                int c = col + sx * dx, r = row + sy * dz;
                                if (c < 0 || c >= z.WidthCells || r < 0 || r >= z.DepthCells) { oob++; continue; }
                                if (!occ.Add((c, r))) overlaps++;
                            }
                    }
                }
                string dir = (sx > 0 ? "+c" : "−c") + (sy > 0 ? "+r" : "−r");
                Assert.AreEqual(expOverlaps, overlaps, dir + ": пересечения");
                Assert.AreEqual(expOob, oob, dir + ": выходы за границы");
            }
        }

        [Test, Category(G2)]
        public void G2_ExitIsThreeByOneAtTopWithRocksOnWorkRow()
        {
            int battlePassZones = 0;
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                string ctx = l.ZoneId;
                var e = l.Exit;
                Assert.AreEqual(BalanceRepository.ExitId, e.TypeId, ctx);
                CollectionAssert.AreEqual(new[] { (2, 0), (3, 0), (4, 0) }, Sorted(l, e.Cells), ctx + ": выход — клетки (2..4, 0)");
                foreach (int col in new[] { 1, 5 })
                {
                    int owner = l.CellOwner[l.CellIndex(col, 0)];
                    Assert.GreaterOrEqual(owner, 0, ctx + ": клетка (" + col + ",0) занята");
                    Assert.AreEqual(ObjectKind.Obstruction, l.Objects[owner].Kind, ctx + ": (" + col + ",0) — препятствие");
                }
                bool battlePass = false;
                foreach (int col in new[] { 2, 3, 4 })
                {
                    int owner = l.CellOwner[l.CellIndex(col, 1)];
                    Assert.GreaterOrEqual(owner, 0, ctx + ": рабочая строка выхода в начале занята");
                    var expectedKind = n == 1 && col == 4 ? ObjectKind.Obstruction : ObjectKind.Rock;
                    Assert.AreEqual(expectedKind, l.Objects[owner].Kind, ctx + ": клетка (" + col + ",1)");
                    if (l.Objects[owner].TypeId == "rockbattlepasscurrency") battlePass = true;
                }
                if (battlePass) battlePassZones++;
                Assert.AreEqual(l.Gates.Count, l.CellSector[e.Cells[0]], ctx + ": выход в верхнем секторе");
            }
            Assert.AreEqual(19, battlePassZones, "ТЗ 6.4: в 19 зонах у выхода стоит rockbattlepasscurrency");
        }

        [Test, Category(G2)]
        public void G2_GatesAreFiveByOneWithObstructionsAtBothEnds()
        {
            int total = 0;
            var perZone = new int[4];
            var withoutGates = new List<int>();
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                Assert.That(l.Gates.Count, Is.InRange(0, 3), l.ZoneId);
                perZone[l.Gates.Count]++;
                if (l.Gates.Count == 0) withoutGates.Add(n);
                foreach (var g in l.Gates)
                {
                    total++;
                    string at = l.ZoneId + "[" + g.Index + "]";
                    Assert.AreEqual("checkpoint", g.TypeId, at);
                    CollectionAssert.AreEqual(Enumerable.Range(1, 5).Select(c => (c, g.Row)), Sorted(l, g.Cells), at + ": ворота — клетки (1..5, row_p)");
                    foreach (int col in new[] { 0, 6 })
                    {
                        int owner = l.CellOwner[l.CellIndex(col, g.Row)];
                        Assert.GreaterOrEqual(owner, 0, at + ": (" + col + ", row_p) занята");
                        Assert.AreEqual(ObjectKind.Obstruction, l.Objects[owner].Kind, at + ": (" + col + ", row_p) — препятствие");
                    }
                }
            }
            Assert.AreEqual(280, total, "ТЗ 6.4: 280 ворот");
            CollectionAssert.AreEqual(new[] { 6, 35, 64, 39 }, perZone, "ТЗ 3.3: зон с 0/1/2/3 воротами");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 7, 11 }, withoutGates, "ТЗ 3.3: зоны без ворот");
        }

        [Test, Category(G2)]
        public void G2_CartIsTwoByThreeOnFreeStartBandAndNotATarget()
        {
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                string ctx = l.ZoneId;
                var c = l.Cart;
                Assert.AreEqual(BalanceRepository.CartId, c.TypeId, ctx);
                Assert.AreEqual(1, c.Level, ctx + ": уровень кузницы 1");
                Assert.IsFalse(c.IsTarget, ctx + ": кузница — не цель добычи");
                Assert.AreEqual(1, c.Col, ctx + ": якорь в столбце 1");
                int rc = l.RowC;
                Assert.AreEqual(n <= 2 ? l.Depth - 3 : l.Depth - 4, rc, ctx + ": токен в строке Depth − 3 (zone1, zone2) или Depth − 4");
                CollectionAssert.AreEqual(new[] { (1, rc - 1), (1, rc), (1, rc + 1), (2, rc - 1), (2, rc), (2, rc + 1) }, Sorted(l, c.Cells),
                    ctx + ": кузница 2×3 — клетки (1..2, row_c − 1 … row_c + 1)");
                for (int r = rc - 3; r <= rc + 1; r++)
                    for (int col = 0; col < l.Width; col++)
                    {
                        int owner = l.CellOwner[l.CellIndex(col, r)];
                        Assert.IsTrue(owner < 0 || owner == c.Ordinal, ctx + ": стартовая полоса свободна в (" + col + "," + r + ")");
                    }
                Assert.AreEqual(0, l.CellSector[c.Cells[0]], ctx + ": кузница в стартовом секторе");
            }
            Assert.IsFalse(B.HasHpTable(BalanceRepository.CartId), "у кузницы нет HP-таблицы");

            // Клетки кузницы непроходимы и недоступны для размещения.
            var s = NewSession(1);
            var cart = s.Layout.Cart;
            Assert.IsFalse(s.IsTargetAvailable(cart.Ordinal));
            foreach (int cell in cart.Cells) Assert.IsFalse(s.IsCellFree(cell));
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats[0];
            int origin = cat.Cell;
            var drop = s.DropCat(cat.Id, cart.Col + 0.5, cart.Row + 0.5);
            Assert.AreNotEqual(CommandResult.Ok, drop.Result, "на кузницу котика не поставить");
            Assert.AreEqual(origin, cat.Cell);
        }

        [Test, Category(G2)]
        public void G2_SectorsAreBoundedByGateRows()
        {
            int sectors = 0;
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                string ctx = l.ZoneId;
                var gateRows = l.Gates.Select(g => g.Row).ToList();
                for (int k = 0; k < l.Gates.Count; k++)
                {
                    Assert.AreEqual(k, l.Gates[k].GateOrder, ctx + ": ворота нумеруются снизу вверх");
                    if (k > 0) Assert.Less(l.Gates[k].Row, l.Gates[k - 1].Row, ctx + ": ворота отсортированы снизу вверх");
                    Assert.AreEqual(k, l.SectorOfRow(l.Gates[k].Row + 1), ctx + ": рабочая строка ворот в нижнем секторе");
                    for (int dr = 1; dr <= 3; dr++)
                        Assert.AreEqual(k + 1, l.SectorOfRow(l.Gates[k].Row - dr), ctx + ": посадочная площадка в следующем секторе");
                }
                for (int r = 0; r < l.Depth; r++)
                {
                    int expected = gateRows.Contains(r) ? -1 : gateRows.Count(g => g > r);
                    Assert.AreEqual(expected, l.SectorOfRow(r), ctx + ": сектор строки " + r);
                    for (int col = 0; col < l.Width; col++)
                        Assert.AreEqual(expected, l.CellSector[l.CellIndex(col, r)], ctx + ": сектор клетки (" + col + "," + r + ")");
                }
                Assert.AreEqual(0, l.SectorOfRow(l.Depth - 1), ctx + ": нижняя строка — стартовый сектор");
                Assert.AreEqual(l.Gates.Count, l.SectorOfRow(0), ctx + ": строка 0 — верхний сектор");
                Assert.AreEqual(l.Gates.Count + 1, l.SectorCount, ctx);
                sectors += l.SectorCount;
            }
            Assert.AreEqual(424, sectors, "ТЗ 3.3: всего 424 сектора");
        }

        [Test, Category(G2)]
        public void G2_FreeCellsOfEverySectorFormOne4ConnectedArea()
        {
            for (int n = 1; n <= 144; n++)
            {
                var l = ZoneLayout.Build(B, n);
                for (int s = 0; s < l.SectorCount; s++)
                {
                    var free = new HashSet<int>(Enumerable.Range(0, l.CellCount).Where(c => l.CellSector[c] == s && l.CellOwner[c] < 0));
                    Assert.Greater(free.Count, 0, l.ZoneId + ": в секторе " + s + " есть свободные клетки");
                    int components = 0;
                    var seen = new HashSet<int>();
                    foreach (int start in free)
                    {
                        if (!seen.Add(start)) continue;
                        components++;
                        var queue = new Queue<int>();
                        queue.Enqueue(start);
                        while (queue.Count > 0)
                        {
                            int cur = queue.Dequeue();
                            int col = l.Col(cur), row = l.Row(cur);
                            foreach (var (dc, dr) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
                            {
                                int c2 = col + dc, r2 = row + dr;
                                if (!l.InBounds(c2, r2)) continue;
                                int next = l.CellIndex(c2, r2);
                                if (free.Contains(next) && seen.Add(next)) queue.Enqueue(next);
                            }
                        }
                    }
                    Assert.AreEqual(1, components, l.ZoneId + ": сектор " + s + " — одна 4-связная область без карманов");
                }
            }
        }

        [Test, Category(G2)]
        public void G2_Neighbors4AreUpLeftRightDownWithoutDiagonals()
        {
            var l = ZoneLayout.Build(B, 1);
            var nb = new List<int>();
            l.Neighbors4(l.CellIndex(3, 5), nb);
            CollectionAssert.AreEqual(new[] { (3, 4), (2, 5), (4, 5), (3, 6) }, nb.Select(c => Cr(l, c)), "внутренняя клетка: верх, лево, право, низ");
            l.Neighbors4(l.CellIndex(0, 0), nb);
            CollectionAssert.AreEqual(new[] { (1, 0), (0, 1) }, nb.Select(c => Cr(l, c)), "левый верхний угол");
            l.Neighbors4(l.CellIndex(l.Width - 1, l.Depth - 1), nb);
            CollectionAssert.AreEqual(new[] { (l.Width - 1, l.Depth - 2), (l.Width - 2, l.Depth - 1) }, nb.Select(c => Cr(l, c)), "правый нижний угол");
        }

        [Test, Category(G2)]
        public void G2_SectorsOpenOneByOneThroughTheirLowerGate()
        {
            var s = NewSession(20);
            var l = s.Layout;
            var gates = l.Gates;
            Assert.AreEqual(3, gates.Count, "zone20 — первая зона с тремя воротами");
            Assert.IsTrue(s.IsSectorOpen(0));
            for (int k = 1; k <= 3; k++) Assert.IsFalse(s.IsSectorOpen(k), "в начале открыт только стартовый сектор");
            Assert.IsFalse(s.IsCellOpen(l.CellIndex(3, 0)), "верхний сектор закрыт");
            Assert.IsTrue(s.IsTargetAvailable(gates[0].Ordinal), "нижние ворота доступны со стартового сектора");
            Assert.IsFalse(s.IsTargetAvailable(gates[1].Ordinal), "рабочая строка вторых ворот в закрытом секторе");
            Assert.IsFalse(s.IsTargetAvailable(l.Exit.Ordinal));

            OpenGates(s, 1);
            Assert.IsTrue(s.IsSectorOpen(1), "ворота 0 открывают сектор 1");
            Assert.IsFalse(s.IsSectorOpen(2));
            Assert.IsTrue(s.IsTargetAvailable(gates[1].Ordinal));
            Assert.IsFalse(s.IsTargetAvailable(gates[2].Ordinal));
            foreach (int cell in gates[0].Cells)
            {
                Assert.IsFalse(s.IsCellOpen(cell), "строка ворот не принадлежит сектору");
                Assert.IsFalse(s.IsCellFree(cell), "клетки открытых ворот заняты");
            }
        }

        [Test, Category(G2)]
        public void G2_WorkCellsMatchTzExamples()
        {
            // zone1: (4,1) занята препятствием — у выхода две рабочие клетки.
            var s1 = NewSession(1);
            CompleteRocks(s1, o => true);
            CollectionAssert.AreEqual(new[] { (2, 1), (3, 1) }, Sorted(s1.Layout, WorkCellsOf(s1, s1.Layout.Exit.Ordinal)), "zone1: выход — до 2 работников");

            // zone2: выход — (2..4, 1); шахта amethyst 2×3 у края — соседи по стороне без препятствий.
            var s2 = NewSession(2);
            CompleteRocks(s2, o => true);
            CollectionAssert.AreEqual(new[] { (2, 1), (3, 1), (4, 1) }, Sorted(s2.Layout, WorkCellsOf(s2, s2.Layout.Exit.Ordinal)), "zone2: выход — до 3 работников");
            CollectionAssert.AreEqual(new[] { (1, 6), (2, 3), (2, 4), (2, 5) }, Sorted(s2.Layout, WorkCellsOf(s2, s2.Layout.Shafts.Single().Ordinal)), "zone2: amethyst");

            // zone3: камень 1×1 со свободными соседями — 4 работника, диагонали не считаются.
            var s3 = NewSession(3);
            int rock = OrdAt(s3, 3, 3);
            Assert.AreEqual(ObjectKind.Rock, s3.Layout.Objects[rock].Kind);
            CompleteRocks(s3, o => o.Ordinal != rock);
            CollectionAssert.AreEqual(new[] { (2, 3), (3, 2), (3, 4), (4, 3) }, Sorted(s3.Layout, WorkCellsOf(s3, rock)), "zone3: камень (3,3)");

            // zone5: у ворот только строка row_p + 1; клетки над воротами (закрытый сектор) не рабочие.
            var s5 = NewSession(5);
            var gate = s5.Layout.Gates[0];
            CompleteRocks(s5, o => true);
            int below = gate.Row + 1;
            CollectionAssert.AreEqual(new[] { (2, below), (3, below), (4, below) }, Sorted(s5.Layout, WorkCellsOf(s5, gate.Ordinal)),
                "zone5: (1, row_p + 1) и (5, row_p + 1) заняты препятствиями");
        }

        [Test, Category(G2)]
        public void G2_WorkCellsFollowDefinitionInAllZones()
        {
            for (int n = 1; n <= 144; n++)
            {
                var s = NewSession(n, SaveSlots.Main);
                var l = s.Layout;
                foreach (var obj in l.Objects)
                {
                    if (!obj.IsTarget) continue;
                    string at = l.ZoneId + "[" + obj.Index + "] " + obj.Token;
                    var actual = WorkCellsOf(s, obj.Ordinal);
                    // ТЗ 6.5: вне footprint, сосед по стороне, внутри карты, в открытом секторе, не занята объектом.
                    var expected = new HashSet<int>();
                    foreach (int fc in obj.Cells)
                        for (int cell = 0; cell < l.CellCount; cell++)
                            if (SideAdjacent(l, fc, cell) && l.CellOwner[cell] != obj.Ordinal && s.IsCellOpen(cell) && l.CellOwner[cell] < 0)
                                expected.Add(cell);
                    CollectionAssert.AreEquivalent(expected, actual, at);
                    Assert.AreEqual(actual.Count, actual.Distinct().Count(), at + ": без повторов");
                    int max = obj.Kind == ObjectKind.Rock ? 4 : obj.Kind == ObjectKind.Shaft ? 7 : obj.Kind == ObjectKind.Gate ? 5 : 3;
                    Assert.LessOrEqual(actual.Count, max, at + ": предел работников по ТЗ 6.5");
                    foreach (int cell in actual)
                    {
                        if (obj.Kind == ObjectKind.Gate)
                        {
                            Assert.AreEqual(obj.Row + 1, l.Row(cell), at + ": у ворот рабочая только строка row_p + 1");
                            Assert.That(l.Col(cell), Is.InRange(1, 5), at);
                        }
                    }
                    if (obj.Kind == ObjectKind.Exit) Assert.AreEqual(0, actual.Count, at + ": в начальном состоянии у выхода нет свободных рабочих клеток");
                }
            }
        }

        [Test, Category(G2)]
        public void G2_CompletedTargetsTakeNoWorkers()
        {
            // ТЗ 6.5: шахта после расчистки, разрушенные ворота и разрушенный выход рабочих клеток не имеют.
            var s = NewSession(2);
            CompleteRocks(s, o => true);
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats[0];
            var shaft = s.Layout.Shafts.Single();
            Assert.IsTrue(s.AcceptsCat(shaft.Ordinal, cat.Id), "до расчистки шахта принимает котика");
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.IsFalse(s.AcceptsCat(shaft.Ordinal, cat.Id), "расчищенная шахта");
            var drop = s.DropCat(cat.Id, 2.5, 4.5);
            Assert.AreEqual(CommandResult.Ok, drop.Result);
            Assert.AreEqual(-1, cat.TargetIndex, "рядом с расчищенной шахтой котик не назначается");

            var exit = s.Layout.Exit;
            Assert.IsTrue(s.AcceptsCat(exit.Ordinal, cat.Id), "целый выход принимает котика");
            s.DebugCompleteTarget(exit.Ordinal);
            Assert.IsFalse(s.AcceptsCat(exit.Ordinal, cat.Id), "разрушенный выход");
            int origin = cat.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, 3.5, 0.5).Result, "клетки разрушенного выхода заняты");
            Assert.AreEqual(origin, cat.Cell);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, 3.5, 1.5).Result);
            Assert.AreEqual(-1, cat.TargetIndex, "под разрушенным выходом котик не назначается");

            var s5 = NewSession(5);
            var gate = s5.Layout.Gates[0];
            CompleteRocks(s5, o => o.Row > gate.Row);
            Assert.IsTrue(s5.DebugSpawnCat(1));
            var cat5 = s5.Mine.Cats[0];
            OpenGates(s5, 1);
            Assert.IsFalse(s5.AcceptsCat(gate.Ordinal, cat5.Id), "открытые ворота");
            origin = cat5.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s5.DropCat(cat5.Id, 3.5, gate.Row + 0.5).Result, "клетки открытых ворот заняты");
            Assert.AreEqual(origin, cat5.Cell);
            foreach (int row in new[] { gate.Row + 1, gate.Row - 1 })
            {
                Assert.AreEqual(CommandResult.Ok, s5.DropCat(cat5.Id, 3.5, row + 0.5).Result);
                Assert.AreEqual(-1, cat5.TargetIndex, "у открытых ворот котик не назначается (строка " + row + ")");
            }
        }

        // Порядок появления (ТЗ 6.6, G-06): BFS от (3, нижний ряд зоны мержа), сначала клетки зоны мержа.
        static (int, int)[] StartPadOrder(int c) => new[]
        {
            (3, c - 2), (3, c - 3), (2, c - 2), (4, c - 2), (2, c - 3), (4, c - 3), (1, c - 2), (5, c - 2), (1, c - 3), (5, c - 3),
        };

        static (int, int)[] GatePadOrder(int g) => new[]
        {
            (3, g - 1), (3, g - 2), (2, g - 1), (4, g - 1), (2, g - 2), (4, g - 2), (1, g - 1), (5, g - 1), (1, g - 2), (5, g - 2),
        };

        [Test, Category(G2)]
        public void G2_SpawnCellsFollowBfsFromStartPadInAllZones()
        {
            // ТЗ 6.6: SpawnPad = строки row_c − 3 … row_c + 1 без клеток кузницы, BFS от (3, row_c − 2): верх, лево, право, низ;
            // сначала заполняется зона мержа (row_c − 3, row_c − 2).
            for (int n = 1; n <= 144; n++)
            {
                var s = NewSession(n);
                var l = s.Layout;
                foreach (var p in StartPadOrder(l.RowC))
                {
                    Assert.IsTrue(s.DebugSpawnCat(1), l.ZoneId);
                    Assert.AreEqual(p, Cr(l, s.Mine.Cats.Last().Cell), l.ZoneId + ": клетка появления " + s.Mine.Cats.Count);
                }
            }
        }

        [Test, Category(G2)]
        public void G2_SpawnCellsFollowBfsAboveTopOpenedGate()
        {
            // ТЗ 6.6: при открытых воротах SpawnPad — три строки над самыми верхними открытыми воротами, BFS от (3, row_p − 1);
            // сначала зона мержа (row_p − 2, row_p − 1).
            for (int n = 1; n <= 144; n++)
            {
                var s = NewSession(n);
                var l = s.Layout;
                if (l.Gates.Count == 0) continue;
                OpenGates(s, l.Gates.Count);
                foreach (var p in GatePadOrder(l.Gates.Last().Row))
                {
                    Assert.IsTrue(s.DebugSpawnCat(1), l.ZoneId);
                    Assert.AreEqual(p, Cr(l, s.Mine.Cats.Last().Cell), l.ZoneId + ": клетка появления " + s.Mine.Cats.Count);
                }
            }
            // Верхние открытые ворота, а не самые верхние вообще.
            var z20 = NewSession(20);
            OpenGates(z20, 1);
            Assert.AreEqual(GatePadOrder(z20.Layout.Gates[0].Row)[0], Cr(z20.Layout, z20.FindSpawnCell()), "zone20: открыты только нижние ворота");
        }

        [Test, Category(G2)]
        public void G2_SpawnFillsEveryFreeOpenCellThenNoSpace()
        {
            foreach (var (zone, gatesToOpen) in new[] { (1, 0), (5, 0), (5, 1) })
            {
                var s = NewSession(zone);
                OpenGates(s, gatesToOpen);
                var l = s.Layout;
                string ctx = l.ZoneId + ", открыто ворот " + gatesToOpen;
                var expected = Enumerable.Range(0, l.CellCount)
                    .Where(c => l.CellSector[c] >= 0 && l.CellSector[c] <= gatesToOpen && l.CellOwner[c] < 0).ToList();
                if (zone == 1) Assert.AreEqual(7 * 12 - 43 - 3 - 6 - 2, expected.Count, "zone1: 84 клетки − 43 r/s/x − 3 выхода − 6 кузницы 2×3 − 2 пушки");
                int guard = 0;
                while (s.DebugSpawnCat(1)) Assert.Less(++guard, 1000, ctx);
                CollectionAssert.AreEquivalent(expected, s.Mine.Cats.Select(c => c.Cell), ctx + ": котики заняли все свободные клетки открытых секторов, по одному");
                Assert.AreEqual(-1, s.FindSpawnCell(), ctx + ": нет места");
            }
        }

        [Test, Category(G2)]
        public void G2_HireFreeCatAndOpenedGateUseSameSpawnRule()
        {
            var s = NewSession(1, SaveSlots.Main);
            var l = s.Layout;
            int c = l.RowC;
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual((3, c - 2), Cr(l, s.Mine.Cats[0].Cell), "наём: первая клетка BFS — в зоне мержа");
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual((3, c - 3), Cr(l, s.Mine.Cats[1].Cell), "наём: вторая клетка BFS — в зоне мержа");

            var f = NewSession(1, SaveSlots.Main);
            f.Advance(2400 * GameSession.TicksPerSecond);
            var box = f.Mine.Containers.Single(x => x.Kind == ContainerKind.FreeCat);
            Assert.AreEqual((3, c - 2), Cr(f.Layout, box.Cell), "контейнер бесплатного котика — по тому же правилу");

            // Логические клетки котиков после открытия ворот не меняются.
            var g = NewSession(5);
            for (int i = 0; i < 3; i++) Assert.IsTrue(g.DebugSpawnCat(1));
            var before = g.Mine.Cats.Select(x => x.Cell).ToList();
            OpenGates(g, 1);
            CollectionAssert.AreEqual(before, g.Mine.Cats.Select(x => x.Cell), "котики остаются на месте");
            Assert.AreEqual((3, g.Layout.Gates[0].Row - 1), Cr(g.Layout, g.FindSpawnCell()), "новые появляются в зоне мержа над открытыми воротами");
        }

        // ═════════════════════════════ Группа 3. HP ═════════════════════════════

        [Test, Category(G3)]
        public void G3_HpTablesCoverEveryTargetTypeButNotCart()
        {
            var expected = B.Rocks.Keys.Concat(B.Shafts.Keys).Concat(new[] { BalanceRepository.CheckpointId, BalanceRepository.ExitId })
                .OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(expected, B.Root.MiningTargetHealths.Keys.OrderBy(x => x, StringComparer.Ordinal),
                "48 таблиц = 26 камней + 20 шахт + ворота + выход");
            Assert.IsFalse(B.HasHpTable(BalanceRepository.CartId));
            var e = Assert.Throws<BalanceLoadException>(() => B.TargetHp(BalanceRepository.CartId, 1), "кузница — не цель");
            StringAssert.Contains("MiningTargetHealths", e.Message, "ошибка называет раздел");
            StringAssert.Contains(BalanceRepository.CartId, e.Message, "ошибка называет ID");
            e = Assert.Throws<BalanceLoadException>(() => B.TargetHp("Rockbasic", 1), "ID сравниваются точно");
            StringAssert.Contains("Rockbasic", e.Message);
        }

        [Test, Category(G3)]
        public void G3_Rockbasic74IsAboveInt64AndSunstone99IsFileMaximum()
        {
            var hp74 = B.TargetHp("rockbasic", 74);
            Assert.AreEqual(1.27e19, hp74.ToDouble(), 1.27e19 * 1e-12, "ТЗ 8.1: rockbasic L74 = 1.27e19");
            Assert.Greater(hp74.ToDouble(), (double)long.MaxValue, "значение больше Int64");
            double max = 0;
            string maxId = null;
            int maxLevel = 0;
            foreach (var id in B.Root.MiningTargetHealths.Keys)
                for (int lv = 1; lv <= 99; lv++)
                {
                    double v = B.TargetHp(id, lv).ToDouble();
                    if (v > max) { max = v; maxId = id; maxLevel = lv; }
                }
            Assert.AreEqual("sunstone", maxId, "ТЗ 8.1: максимум файла — sunstone");
            Assert.AreEqual(99, maxLevel);
            Assert.AreEqual(1.87e25, max, 1.87e25 * 1e-12);
        }

        [Test, Category(G3)]
        public void G3_AllHpValuesThroughGameNumberMatchRawJson()
        {
            var raw = (JObject)SourceJson["MiningTargetHealths"];
            Assert.AreEqual(48, raw.Count);
            int beyond2p53 = 0;
            foreach (var prop in raw.Properties())
            {
                var arr = (JArray)prop.Value;
                Assert.AreEqual(99, arr.Count, prop.Name);
                for (int i = 0; i < arr.Count; i++)
                {
                    double expected = (double)arr[i];
                    var hp = B.TargetHp(prop.Name, i + 1);
                    double actual = hp.ToDouble();
                    string at = prop.Name + " L" + (i + 1);
                    Assert.IsTrue(hp.IsPositive, at);
                    Assert.IsFalse(double.IsNaN(actual) || double.IsInfinity(actual), at + ": конечно");
                    // Для значений больше 2^53 точного целочисленного равенства не требуем — только относительный допуск.
                    Assert.AreEqual(expected, actual, expected * 1e-12, at + ": HP = table[level − 1]");
                    if (expected > 9007199254740992.0) beyond2p53++;
                }
            }
            Assert.AreEqual(2060, beyond2p53, "в файле 2060 значений HP больше 2^53");
        }

        [Test, Category(G3)]
        public void G3_LevelOutsideTableThrowsForEveryTarget()
        {
            foreach (var id in B.Root.MiningTargetHealths.Keys)
            {
                int last = B.Root.MiningTargetHealths[id].Length;
                Assert.AreEqual(99, last, id);
                foreach (int bad in new[] { 0, -1, last + 1, int.MinValue, int.MaxValue })
                {
                    var e = Assert.Throws<BalanceLoadException>(() => B.TargetHp(id, bad), id + " L" + bad);
                    StringAssert.Contains("MiningTargetHealths." + id, e.Message, "ошибка с путём таблицы");
                }
            }
        }

        [Test, Category(G3)]
        public void G3_SessionHpComesFromTokenLevelNotZoneNumber()
        {
            // Контрольные значения ТЗ 8.1 на реальных токенах.
            var z1 = NewSession(1, SaveSlots.Main);
            Assert.AreEqual("r:rockonboardingmine1:1", z1.Layout.Objects[OrdAt(z1, 2, 2)].Token);
            Assert.AreEqual(16.0, z1.Mine.Objects[OrdAt(z1, 2, 2)].Hp.ToDouble());
            Assert.AreEqual("r:rockonboardingmine1:2", z1.Layout.Objects[OrdAt(z1, 2, 1)].Token);
            Assert.AreEqual(60.0, z1.Mine.Objects[OrdAt(z1, 2, 1)].Hp.ToDouble());
            Assert.AreEqual(1, z1.Layout.Exit.Level);
            Assert.AreEqual(100.0, z1.Mine.Objects[z1.Layout.Exit.Ordinal].Hp.ToDouble());
            var z2 = NewSession(2, SaveSlots.Main);
            Assert.AreEqual(1, z2.Layout.Shafts.Single().Level);
            Assert.AreEqual(64.0, z2.Mine.Objects[z2.Layout.Shafts.Single().Ordinal].Hp.ToDouble());
            var z5 = NewSession(5, SaveSlots.Main);
            Assert.AreEqual(5, z5.Layout.Gates[0].Level);
            Assert.AreEqual(20800.0, z5.Mine.Objects[z5.Layout.Gates[0].Ordinal].Hp.ToDouble());

            // zone144: номер зоны больше длины таблиц (99), уровни целей — из токенов.
            var z = NewSession(144, SaveSlots.Main);
            int targets = 0;
            foreach (var obj in z.Layout.Objects)
            {
                if (!obj.IsTarget) continue;
                targets++;
                Assert.That(obj.Level, Is.InRange(1, 71), obj.Token);
                Assert.AreEqual(B.TargetHp(obj.TypeId, obj.Level), z.Mine.Objects[obj.Ordinal].Hp, obj.Token);
            }
            Assert.Greater(targets, 0);
        }
    }
}
