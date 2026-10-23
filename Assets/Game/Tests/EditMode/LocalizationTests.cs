using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Единый словарь строк UI (ТЗ 5.1, Loc): ключи из кода зарегистрированы, весь словарь на английском (без кириллицы),
    /// тексты Core собираются по английским шаблонам, а в коде, который показывает текст игроку, нет кириллических
    /// литералов в обход словаря.
    /// </summary>
    public class LocalizationTests
    {
        const string ScriptsRelativePath = "Assets/Game/Scripts";

        static string ScriptsDir => Path.Combine(TestData.ProjectRoot, ScriptsRelativePath);

        static string Rel(string path) =>
            path.Substring(TestData.ProjectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

        // ───────────────────────────── Словарь ─────────────────────────────

        [Test]
        public void DictionaryIsBuiltWithoutProblems()
        {
            CollectionAssert.IsEmpty(Loc.RegistrationProblems, "проблемы регистрации словаря:\n" + string.Join("\n", Loc.RegistrationProblems));
            Assert.AreEqual("en", Loc.Language);
            Assert.Greater(Loc.Keys.Count, 0, "словарь пуст");
            var keyFormat = new Regex(@"^[a-z]+(\.[A-Za-z0-9_\-]+)+$");
            var bad = new List<string>();
            foreach (var key in Loc.Keys)
            {
                if (!keyFormat.IsMatch(key)) bad.Add(key + ": ключ вида «раздел.группа.имя»");
                Assert.IsTrue(Loc.TryGet(key, out var text), key);
                Assert.IsNotNull(text, key);
                if (text.IndexOf('{') >= 0 || text.IndexOf('}') >= 0)
                {
                    try { string.Format(CultureInfo.InvariantCulture, text, DummyArgs()); }
                    catch (FormatException e) { bad.Add(key + ": неверный шаблон «" + text + "»: " + e.Message); }
                }
            }
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void MissingKeyIsVisibleAndReported()
        {
            const string key = "test.no_such_key";
            Loc.ResetReports();
            var events = new List<string>();
            Action<string> handler = k => events.Add(k);
            Loc.MissingKey += handler;
            try
            {
                Assert.IsFalse(Loc.Has(key));
                Assert.IsFalse(Loc.TryGet(key, out _));
                CollectionAssert.IsEmpty(Loc.MissingKeys, "Has/TryGet не отмечают ключ отсутствующим");
                Assert.AreEqual("[" + key + "]", Loc.T(key));
                Assert.AreEqual("[" + key + "]", Loc.F(key, 1, 2));
                CollectionAssert.AreEqual(new[] { key }, Loc.MissingKeys, "ключ записан один раз");
                CollectionAssert.AreEqual(new[] { key, key }, events, "событие — на каждый запрос");
                Assert.AreEqual("[null]", Loc.T(null));
            }
            finally
            {
                Loc.MissingKey -= handler;
                Loc.ResetReports();
            }
        }

        /// <summary>Игра на английском: ни один текст словаря не содержит кириллицы (перевод полный).</summary>
        [Test]
        public void DictionaryIsEnglish()
        {
            var cyrillic = new List<string>();
            foreach (var key in Loc.Keys)
                if (Loc.TryGet(key, out var text) && HasCyrillic(text)) cyrillic.Add(key + ": «" + text + "»");
            CollectionAssert.IsEmpty(cyrillic, "тексты с кириллицей:\n" + string.Join("\n", cyrillic));
        }

        /// <summary>
        /// Формы слова — английские «.one» и «.many»: русской формы «.few» в словаре нет. Пары «.one/.many» для каждого
        /// Loc.Plural из кода проверяет <see cref="EveryKeyUsedInCodeIsRegistered"/>.
        /// </summary>
        [Test]
        public void NoRussianPluralForms()
        {
            var few = Loc.Keys.Where(k => k.EndsWith(".few", StringComparison.Ordinal)).ToList();
            CollectionAssert.IsEmpty(few, "русская форма .few:\n" + string.Join("\n", few));
        }

        [Test]
        public void PluralPicksEnglishForm()
        {
            // Ключей test.* нет: по «[ключ.форма]» видно, какая форма выбрана.
            try
            {
                var expected = new Dictionary<long, string>
                {
                    { 0, "many" }, { 1, "one" }, { -1, "one" }, { 2, "many" }, { 4, "many" }, { 5, "many" }, { 11, "many" },
                    { 21, "many" }, { 22, "many" }, { 101, "many" }, { -2, "many" },
                };
                foreach (var kv in expected)
                    Assert.AreEqual("[test.plural." + kv.Value + "]", Loc.Plural("test.plural", kv.Key), "n = " + kv.Key);
            }
            finally
            {
                Loc.ResetReports();
            }
        }

        // ───────────────────────────── Ключи из кода ─────────────────────────────

        static readonly Regex s_KeyUse = new Regex(@"\bLoc\s*\.\s*(T|F|Plural)\s*\(\s*""((?:[^""\\\r\n]|\\.)*)""\s*([,)])");

        /// <summary>Каждый литеральный ключ Loc.T("…") / Loc.F("…", …) / Loc.Plural("…", n) в Assets/Game/Scripts есть в словаре.</summary>
        [Test]
        public void EveryKeyUsedInCodeIsRegistered()
        {
            var known = new HashSet<string>(Loc.Keys, StringComparer.Ordinal);
            var missing = new List<string>();
            int uses = 0;
            foreach (var file in Directory.GetFiles(ScriptsDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string code = CSharpLiterals.StripComments(File.ReadAllText(file));
                var lines = new CSharpLiterals.LineMap(code);
                foreach (Match m in s_KeyUse.Matches(code))
                {
                    string kind = m.Groups[1].Value, key = m.Groups[2].Value;
                    if (kind == "T" && m.Groups[3].Value != ")") continue;    // Loc.T("префикс" + id) — составной ключ, не проверяется
                    uses++;
                    var keys = kind == "Plural" ? new[] { key + ".one", key + ".many" } : new[] { key };
                    foreach (var k in keys)
                        if (!known.Contains(k))
                            missing.Add(Rel(file) + ":" + lines.LineOf(m.Index) + ": " + k);
                }
            }
            Assert.Greater(uses, 0, "в коде не найдено ни одного Loc.T/Loc.F — сканер сломан");
            CollectionAssert.IsEmpty(missing, "ключи не зарегистрированы в Loc:\n" + string.Join("\n", missing));
        }

        // ───────────────────────────── Тексты Core ─────────────────────────────

        /// <summary>После форматирования всех текстов причин и всех шаблонов словаря нет отсутствующих ключей и ошибок шаблонов.</summary>
        [Test]
        public void FormattingAllReasonTextsReportsNoMissingKeys()
        {
            Loc.ResetReports();
            var texts = CoreTexts();
            foreach (var key in Loc.Keys) texts.Add(Loc.F(key, DummyArgs()));
            CollectionAssert.IsEmpty(Loc.MissingKeys, "отсутствуют ключи:\n" + string.Join("\n", Loc.MissingKeys));
            CollectionAssert.IsEmpty(Loc.FormatErrors, "ошибки шаблонов:\n" + string.Join("\n", Loc.FormatErrors));
            CollectionAssert.DoesNotContain(texts, null, "у каждой причины есть текст");
        }

        [Test]
        public void CommandRejectReasonsKeepText()
        {
            var expected = new Dictionary<CommandResult, string>
            {
                { CommandResult.Ok, "" },
                { CommandResult.NotEnoughGold, "Not enough gold" },
                { CommandResult.NotEnoughElixir, "Not enough elixir" },
                { CommandResult.NotEnoughDuplicates, "Not enough card copies" },
                { CommandResult.CapReached, "Cat limit reached" },
                { CommandResult.NoSpace, "No space" },
                { CommandResult.NoSpaceAtTarget, "No space at the target" },
                { CommandResult.DifferentLevels, "Cats must be the same level" },
                { CommandResult.InvalidTarget, "Can't go there" },
                { CommandResult.ClosedSector, "Sector is closed" },
                { CommandResult.NotAllowed, "Not allowed" },
                { CommandResult.MaxLevel, "Max level" },
                { CommandResult.EffectNotAvailable, "Effect coming later" },
                { CommandResult.NotFound, "Not found" },
            };
            foreach (CommandResult r in Enum.GetValues(typeof(CommandResult)))
            {
                Assert.IsTrue(expected.ContainsKey(r), "нет ожидаемого текста для " + r);
                Assert.AreEqual(expected[r], GameSession.Describe(r), r.ToString());
            }

            // Событие CommandRejected несёт тот же текст.
            var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, 1);
            s.Events.Clear();
            Assert.AreEqual(CommandResult.NotFound, s.UpgradeGenerator(-1, out _));
            var rejected = s.Events.Single(e => e.Type == SimEventType.CommandRejected);
            Assert.AreEqual("Not found", rejected.Text);
        }

        [Test]
        public void DurationUnitsKeepText()
        {
            var expected = new (double Seconds, string Text)[]
            {
                (double.PositiveInfinity, "—"), (double.NaN, "—"), (-5, "0s"), (0, "0s"), (1.2, "2s"), (59, "59s"),
                (61, "1m 01s"), (3599, "59m 59s"), (3600, "1h 00m"), (3661, "1h 01m"),
                (47 * 3600 + 59 * 60, "47h 59m"), (48 * 3600, "2d 0h"), (49 * 3600 + 120, "2d 1h"), (30 * 86400 + 5 * 3600, "30d 5h"),
            };
            var saved = CultureInfo.CurrentCulture;
            try
            {
                // Вывод не зависит от культуры потока: шаблоны форматируются InvariantCulture.
                foreach (var culture in new[] { CultureInfo.InvariantCulture, TryCulture("ru-RU"), TryCulture("de-DE") })
                {
                    if (culture == null) continue;
                    CultureInfo.CurrentCulture = culture;
                    foreach (var (seconds, text) in expected)
                        Assert.AreEqual(text, NumberFormat.FormatDuration(seconds), seconds + " с, культура " + culture.Name);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void RarityNamesKeepText()
        {
            Assert.AreEqual("Common", Rarity.Name(Rarity.Common));
            Assert.AreEqual("Uncommon", Rarity.Name(Rarity.Uncommon));
            Assert.AreEqual("Rare", Rarity.Name(Rarity.Rare));
            Assert.AreEqual("Epic", Rarity.Name(Rarity.Epic));
            Assert.AreEqual("Legendary", Rarity.Name(Rarity.Legendary));
            Assert.AreEqual("Majestic", Rarity.Name(Rarity.Majestic));
            Assert.AreEqual("?", Rarity.Name(0));
            Assert.AreEqual("?", Rarity.Name(7));
        }

        [Test]
        public void ZoneCompletionReasonsKeepText()
        {
            var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, 2);
            Assert.IsFalse(s.CanCompleteZone(out string reason));
            Assert.AreEqual("The exit is not open yet", reason);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            Assert.IsFalse(s.CanCompleteZone(out reason));
            Assert.AreEqual("Shafts activated: 0/" + s.Layout.Shafts.Count, reason);

            var gated = SessionWithGates();
            gated.Mine.Objects[gated.Layout.Exit.Ordinal].Status = ObjectStatus.Broken;
            foreach (var shaft in gated.Layout.Shafts) gated.Mine.Objects[shaft.Ordinal].Status = ObjectStatus.Producing;
            Assert.IsFalse(gated.CanCompleteZone(out reason));
            Assert.AreEqual("Not all gates are open", reason);

            gated.Player.CampaignCompleted = true;
            Assert.IsFalse(gated.CanCompleteZone(out reason));
            Assert.AreEqual("Campaign complete", reason);
        }

        [Test]
        public void AutomationRequirementKeepsText()
        {
            var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, 3);
            Assert.Greater(s.Layout.Shafts.Count, 0, "в zone3 есть шахты");
            bool sawNone = false, sawLevel = false;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var shaft in s.Layout.Shafts)
                {
                    int have = s.CardLevel(shaft.CardId);
                    // Сборка строки по английскому шаблону core.automation.*.
                    string expected = "Needs blueprint " + shaft.CardId + " Lv. " + shaft.CardLevel + " (have: " + (have > 0 ? "Lv. " + have : "none") + ")";
                    Assert.AreEqual(expected, s.AutomationRequirement(shaft.Ordinal));
                    if (have > 0) sawLevel = true;
                    else sawNone = true;
                }
                s.DebugGiveZoneCards();
            }
            Assert.IsTrue(sawNone, "проверен вариант «have: none»");
            Assert.IsTrue(sawLevel, "проверен вариант «have: Lv. N»");
            Assert.IsNull(s.AutomationRequirement(s.Layout.Exit.Ordinal), "не шахта — нет требования");
        }

        [Test]
        public void ThresholdIncomeTextKeepsFormat()
        {
            var e = FirstThresholdEvent();
            Assert.IsNotNull(e, "порог генератора не достигнут");
            StringAssert.StartsWith("x" + NumberFormat.Format(e.Value.Amount) + ", +", e.Value.Text);
            StringAssert.EndsWith(" elixir", e.Value.Text);
            StringAssert.IsMatch(@"^x[0-9.]+[A-Za-z]*, \+[0-9.]+[A-Za-z]* elixir$", e.Value.Text);
        }

        // ───────────────────────────── Кириллица в обход словаря ─────────────────────────────

        /// <summary>Файлы Core, тексты которых уже в словаре (раздел Core).</summary>
        static IEnumerable<string> CoreUiFiles()
        {
            foreach (var f in Directory.GetFiles(Path.Combine(ScriptsDir, "Core", "Sim"), "GameSession*.cs")) yield return f;
            yield return Path.Combine(ScriptsDir, "Core", "Numbers", "NumberFormat.cs");
        }

        /// <summary>Код, который показывает текст игроку: весь Runtime и тексты Core.</summary>
        static IEnumerable<string> PlayerFacingFiles() =>
            Directory.GetFiles(Path.Combine(ScriptsDir, "Runtime"), "*.cs", SearchOption.AllDirectories).Concat(CoreUiFiles());

        [Test]
        public void CoreUiFilesHaveNoCyrillicLiterals()
        {
            var found = CyrillicLiterals(CoreUiFiles());
            CollectionAssert.IsEmpty(found, "кириллица в обход Loc:\n" + string.Join("\n", found));
        }

        /// <summary>
        /// В Runtime и текстах Core нет строковых литералов с кириллицей, кроме Loc.*.cs. Комментарии не учитываются.
        /// Не считаются текстом игрока: сообщения исключений (throw, new …Exception(…)), Debug.Log*/Assert и литералы
        /// в операторе с комментарием «loc:ignore» (имена объектов сцены и прочие служебные строки).
        /// </summary>
        [Test]
        public void PlayerFacingCodeHasNoCyrillicLiterals()
        {
            var found = CyrillicLiterals(PlayerFacingFiles());
            if (found.Count == 0) return;
            // Сводка по файлам — в начале: длинное сообщение раннер может обрезать; полный список — и в выводе теста.
            var perFile = found.GroupBy(f => f.Substring(0, f.IndexOf(':'))).Select(g => g.Key + ": " + g.Count());
            string report = found.Count + " литералов с кириллицей в обход Loc:\n" + string.Join("\n", perFile) + "\n\n" + string.Join("\n", found);
            TestContext.WriteLine(report);
            Assert.Fail(report);
        }

        [Test]
        public void CyrillicLiteralScannerSelfCheck()
        {
            const string code =
                "// \"Комментарий\"\n" +
                "/* \"Блок\" */ var a = \"Текст\";\n" +
                "var b = @\"Дословно \"\"кавычки\"\"\";\n" +
                "var c = $\"Шаблон {(x ? \"Вложенный\" : \"y\")} {{скобки}}\";\n" +
                "char q = '\"'; var d = \"ascii\";\n" +
                "throw new InvalidOperationException(\"Исключение \" + x);\n" +
                "if (bad) throw new Exception(\n    \"Многострочное\");\n" +
                "Debug.LogWarning(\"[CatMiners] Лог \" +\n    \"продолжение\");\n" +
                "var ex = new BalanceLoadException(\"Конфиг\");\n" +
                "go.name = \"Служебное\"; // loc:ignore — имя объекта сцены\n" +
                "Hud.Toast(\"Игроку\");\n";
            var lits = CSharpLiterals.Scan(code);
            var cyr = lits.Where(l => HasCyrillic(l.Text)).ToList();
            CollectionAssert.AreEqual(
                new[] { "Текст", "Дословно \"кавычки\"", "Шаблон {…} {скобки}", "Вложенный", "Исключение ", "Многострочное", "[CatMiners] Лог ", "продолжение", "Конфиг", "Служебное", "Игроку" },
                cyr.Select(l => l.Text).ToArray(), "литералы найдены, комментарии пропущены");
            CollectionAssert.AreEqual(
                new[] { "Текст", "Дословно \"кавычки\"", "Шаблон {…} {скобки}", "Вложенный", "Игроку" },
                cyr.Where(l => !l.Exempt).Select(l => l.Text).ToArray(), "исключения, логи и loc:ignore не считаются текстом игрока");
            Assert.AreEqual(2, cyr.First(l => l.Text == "Текст").Line);
            Assert.AreEqual(13, cyr.Last().Line);
            Assert.IsFalse(CSharpLiterals.StripComments(code).Contains("Комментарий"));
            Assert.IsTrue(CSharpLiterals.StripComments(code).Contains("\"Текст\""));
        }

        static List<string> CyrillicLiterals(IEnumerable<string> files)
        {
            var found = new List<string>();
            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (Path.GetFileName(file).StartsWith("Loc.", StringComparison.Ordinal)) continue;
                foreach (var lit in CSharpLiterals.Scan(File.ReadAllText(file)))
                    if (!lit.Exempt && HasCyrillic(lit.Text))
                        found.Add(Rel(file) + ":" + lit.Line + ": \"" + lit.Text + "\"");
            }
            return found;
        }

        static bool HasCyrillic(string s)
        {
            foreach (char ch in s)
                if (ch >= '\u0400' && ch <= '\u04FF') return true;
            return false;
        }

        // ───────────────────────────── Вспомогательное ─────────────────────────────

        static object[] DummyArgs()
        {
            var args = new object[10];
            for (int i = 0; i < args.Length; i++) args[i] = i + 1;
            return args;
        }

        static CultureInfo TryCulture(string name)
        {
            try { return CultureInfo.GetCultureInfo(name); }
            catch (CultureNotFoundException) { return null; }
        }

        static GameSession SessionWithGates()
        {
            for (int zone = 1; zone <= TestData.Balance.ZoneCount; zone++)
            {
                var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, zone);
                if (s.Layout.Gates.Count > 0 && s.Layout.Shafts.Count > 0) return s;
            }
            Assert.Fail("нет зоны с воротами и шахтами");
            return null;
        }

        static SimEvent? FirstThresholdEvent()
        {
            var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, 1);
            s.DebugAddGold(GameNumber.FromDouble(1e60));
            for (int attempt = 0; attempt < 5; attempt++)
            {
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    if (!s.IsGenerator(ord)) continue;
                    s.Events.Clear();
                    s.UpgradeToThreshold(ord, out _);
                    foreach (var e in s.Events)
                        if (e.Type == SimEventType.ThresholdReached) return e;
                }
            }
            return null;
        }

        /// <summary>Текст всех причин и подписей, которые формирует Core.</summary>
        static List<string> CoreTexts()
        {
            var texts = new List<string>();
            foreach (CommandResult r in Enum.GetValues(typeof(CommandResult))) texts.Add(GameSession.Describe(r));
            for (int r = 0; r <= Rarity.Majestic + 1; r++) texts.Add(Rarity.Name(r));
            foreach (var sec in new[] { double.NaN, 0, 59, 61, 3600, 48 * 3600 }) texts.Add(NumberFormat.FormatDuration(sec));

            var s = GameSession.NewGame(TestData.Balance, 7UL, SaveSlots.Debug, 0L, 3);
            foreach (var shaft in s.Layout.Shafts) texts.Add(s.AutomationRequirement(shaft.Ordinal));
            s.DebugGiveZoneCards();
            foreach (var shaft in s.Layout.Shafts) texts.Add(s.AutomationRequirement(shaft.Ordinal));
            s.CanCompleteZone(out string reason);
            texts.Add(reason);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            s.CanCompleteZone(out reason);
            texts.Add(reason);
            var gated = SessionWithGates();
            gated.Mine.Objects[gated.Layout.Exit.Ordinal].Status = ObjectStatus.Broken;
            foreach (var shaft in gated.Layout.Shafts) gated.Mine.Objects[shaft.Ordinal].Status = ObjectStatus.Producing;
            gated.CanCompleteZone(out reason);
            texts.Add(reason);
            gated.Player.CampaignCompleted = true;
            gated.CanCompleteZone(out reason);
            texts.Add(reason);
            var threshold = FirstThresholdEvent();
            if (threshold != null) texts.Add(threshold.Value.Text);
            return texts;
        }
    }

    /// <summary>
    /// Минимальный лексер C# для тестов локализации: строковые литералы (обычные, @"…", $"…", $@"…" с вложенными
    /// строками в подстановках) и комментарии. Для каждого литерала — строка файла и признак «служебный текст»:
    /// оператор с throw / new …Exception(…) / Debug.Log*/Assert*, или строки оператора с комментарием «loc:ignore».
    /// </summary>
    static class CSharpLiterals
    {
        public sealed class Literal
        {
            public int Start;
            public int End;
            public int Line;
            public string Text;
            public bool Exempt;
        }

        sealed class Lexer
        {
            readonly string m_Code;
            public readonly char[] Masked;          // код без комментариев и содержимого литералов
            public readonly char[] NoComments;      // код без комментариев, литералы на месте
            public readonly List<Literal> Literals = new List<Literal>();
            public readonly List<(int Start, int End)> Comments = new List<(int, int)>();

            public Lexer(string code)
            {
                m_Code = code;
                Masked = code.ToCharArray();
                NoComments = code.ToCharArray();
                int i = 0;
                while (i < code.Length) i = Step(i);
            }

            /// <summary>Один шаг: комментарий, символьный или строковый литерал целиком, иначе один символ.</summary>
            int Step(int i)
            {
                string code = m_Code;
                char c = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    int e = code.IndexOf('\n', i);
                    if (e < 0) e = code.Length;
                    Comment(i, e);
                    return e;
                }
                if (c == '/' && next == '*')
                {
                    int e = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    e = e < 0 ? code.Length : e + 2;
                    Comment(i, e);
                    return e;
                }
                if (c == '\'')
                {
                    int e = i + 1;
                    while (e < code.Length && code[e] != '\'' && code[e] != '\n')
                    {
                        if (code[e] == '\\') e++;
                        e++;
                    }
                    e = Math.Min(code.Length, e + 1);
                    Mask(Masked, i, e);
                    return e;
                }
                if (StringStart(i, out bool verbatim, out bool interpolated, out int content)) return ReadString(i, content, verbatim, interpolated);
                return i + 1;
            }

            bool StringStart(int i, out bool verbatim, out bool interpolated, out int content)
            {
                string code = m_Code;
                verbatim = interpolated = false;
                content = -1;
                int j = i;
                while (j < code.Length && j - i < 2 && (code[j] == '@' || code[j] == '$'))
                {
                    if (code[j] == '@') verbatim = true;
                    else interpolated = true;
                    j++;
                }
                if (j >= code.Length || code[j] != '"') return false;
                if (j > i && i > 0 && (char.IsLetterOrDigit(code[i - 1]) || code[i - 1] == '_')) return false;
                content = j + 1;
                return true;
            }

            int ReadString(int start, int i, bool verbatim, bool interpolated)
            {
                string code = m_Code;
                var sb = new StringBuilder();
                while (i < code.Length)
                {
                    char c = code[i];
                    char next = i + 1 < code.Length ? code[i + 1] : '\0';
                    if (verbatim && c == '"')
                    {
                        if (next == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    if (!verbatim)
                    {
                        if (c == '\\') { sb.Append(c).Append(next); i += 2; continue; }
                        if (c == '"') { i++; break; }
                        if (c == '\n') break;
                    }
                    if (interpolated && c == '{')
                    {
                        if (next == '{') { sb.Append('{'); i += 2; continue; }
                        i = SkipHole(i + 1);
                        sb.Append("{…}");
                        continue;
                    }
                    if (interpolated && c == '}' && next == '}') { sb.Append('}'); i += 2; continue; }
                    sb.Append(c);
                    i++;
                }
                Mask(Masked, start, i);
                Literals.Add(new Literal { Start = start, End = i, Text = sb.ToString() });
                return i;
            }

            /// <summary>Подстановка {…} интерполированной строки: до парной «}», вложенные строки и символы — как в коде.</summary>
            int SkipHole(int i)
            {
                int depth = 0;
                while (i < m_Code.Length)
                {
                    char c = m_Code[i];
                    if (c == '{') { depth++; i++; continue; }
                    if (c == '}')
                    {
                        if (depth == 0) return i + 1;
                        depth--;
                        i++;
                        continue;
                    }
                    i = Step(i);
                }
                return i;
            }

            void Comment(int start, int end)
            {
                Comments.Add((start, end));
                Mask(Masked, start, end);
                Mask(NoComments, start, end);
            }

            static void Mask(char[] chars, int start, int end)
            {
                for (int k = start; k < end && k < chars.Length; k++)
                    if (chars[k] != '\n' && chars[k] != '\r') chars[k] = ' ';
            }
        }

        static readonly Regex s_DevStatement = new Regex(@"\bthrow\b|\bnew\s+[\w.]*Exception\s*\(|\bDebug\s*\.\s*(Log|Assert)\w*\s*\(");

        public static string StripComments(string code) => new string(new Lexer(code).NoComments);

        /// <summary>Номер строки (с 1) по смещению в коде.</summary>
        public sealed class LineMap
        {
            readonly List<int> m_Starts = new List<int> { 0 };

            public LineMap(string code)
            {
                for (int k = 0; k < code.Length; k++)
                    if (code[k] == '\n') m_Starts.Add(k + 1);
            }

            public int LineOf(int index)
            {
                int found = m_Starts.BinarySearch(index);
                return found >= 0 ? found + 1 : ~found;
            }
        }

        public static List<Literal> Scan(string code)
        {
            var lexer = new Lexer(code);
            var masked = lexer.Masked;
            var lines = new LineMap(code);
            var markerLines = new HashSet<int>();
            foreach (var (start, end) in lexer.Comments)
                if (code.IndexOf("loc:ignore", start, end - start, StringComparison.Ordinal) >= 0)
                    markerLines.Add(lines.LineOf(start));
            var result = lexer.Literals.OrderBy(l => l.Start).ToList();
            foreach (var lit in result)
            {
                lit.Line = lines.LineOf(lit.Start);
                // Оператор вокруг литерала: от предыдущей «;», «{» или «}» до следующей (литералы и комментарии замаскированы).
                int from = lit.Start - 1;
                while (from >= 0 && masked[from] != ';' && masked[from] != '{' && masked[from] != '}') from--;
                int to = lit.End;
                while (to < masked.Length && masked[to] != ';' && masked[to] != '{' && masked[to] != '}') to++;
                to = Math.Min(masked.Length, to);
                string statement = new string(masked, from + 1, to - from - 1);
                bool exempt = s_DevStatement.IsMatch(statement);
                if (!exempt)
                {
                    int first = from + 1;
                    while (first < lit.Start && char.IsWhiteSpace(masked[first])) first++;
                    for (int line = lines.LineOf(first), last = lines.LineOf(to); line <= last && !exempt; line++)
                        exempt = markerLines.Contains(line);
                }
                lit.Exempt = exempt;
            }
            return result;
        }
    }
}
