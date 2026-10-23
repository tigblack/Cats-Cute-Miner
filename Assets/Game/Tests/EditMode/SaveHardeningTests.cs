using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Прежняя сборка конверта (JObject + ToString) — эталон байтов для новой записи из переиспользуемых буферов
    /// и база для замера выделений памяти «до».
    /// </summary>
    public static class LegacySaveFormat
    {
        public static byte[] Envelope(GameState state, long nowUtcMs)
        {
            string payload = JsonConvert.SerializeObject(state, SaveService.Settings());
            var env = new JObject
            {
                ["schemaVersion"] = state.SchemaVersion,
                ["rulesVersion"] = state.RulesVersion,
                ["balanceSha256"] = state.BalanceSha256,
                ["slot"] = state.Slot,
                ["savedAtUtcMs"] = nowUtcMs,
                ["payloadSha256"] = SaveService.Sha256Hex(payload),
                ["payload"] = payload,
            };
            return new UTF8Encoding(false).GetBytes(env.ToString(Formatting.None));
        }

        /// <summary>Прежний SaveService.Save целиком: конверт, FileStream с буфером по умолчанию и Flush(true), Replace/Move.</summary>
        public static void Save(string root, GameState state, long nowUtcMs)
        {
            Directory.CreateDirectory(root);
            string main = Path.Combine(root, "save_" + state.Slot.ToLowerInvariant() + ".json");
            string tmp = main + ".tmp";
            byte[] data = Envelope(state, nowUtcMs);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (File.Exists(main)) File.Replace(tmp, main, main + ".bak");
            else File.Move(tmp, main);
        }
    }

    /// <summary>
    /// Надёжность сохранения (ТЗ 12.1–12.2): NaN/∞ не попадают в файл, payload совпадает с обычным сериализатором,
    /// повреждённые и несовместимые файлы откладываются без удаления, миграции схемы проверяются на фикстуре v1.
    /// </summary>
    public class SaveHardeningTests
    {
        const string FixtureRelativePath = "Assets/Game/Tests/Fixtures/Saves/save_v1_main.json";
        const long Now = 1_000_000L;

        static GameSession NewSession(int zone = 1, string slot = SaveSlots.Main) =>
            GameSession.NewGame(TestData.Balance, 12345UL, slot, Now, zone);

        static string NewTempDir() => Path.Combine(Path.GetTempPath(), "catminers-save-" + Guid.NewGuid().ToString("N"));

        static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static string[] FileNames(string dir) =>
            Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray() : new string[0];

        /// <summary>Файловая система, которая считает изменяющие операции и может отказать в переименовании.</summary>
        sealed class RecordingFs : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            public readonly List<string> Ops = new List<string>();
            public bool FailMove;

            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);
            public void WriteAllBytesDurable(string p, byte[] d) { Ops.Add("write " + Path.GetFileName(p)); m_Real.WriteAllBytesDurable(p, d); }
            public void Replace(string s, string d, string b) { Ops.Add("replace"); m_Real.Replace(s, d, b); }

            public void Move(string s, string d)
            {
                Ops.Add("move " + Path.GetFileName(s));
                if (FailMove) throw new IOException("переименование запрещено");
                m_Real.Move(s, d);
            }

            public void Copy(string s, string d) { Ops.Add("copy"); m_Real.Copy(s, d); }
            public void Delete(string p) { Ops.Add("delete"); m_Real.Delete(p); }
            public void CreateDirectory(string p) { Ops.Add("mkdir"); m_Real.CreateDirectory(p); }
        }

        // ───────────── ТЗ 12.1: NaN и Infinity в сохранение не попадают ─────────────

        [Test]
        public void NaNInSnapshotIsRejectedBeforeAnyFileIsWritten()
        {
            string dir = NewTempDir();
            try
            {
                var s = NewSession();
                Assert.AreEqual(CommandResult.Ok, s.Hire());
                var svc = new SaveService(dir);
                svc.Save(s.State, Now);
                string valid = SaveService.SerializeState(s.State);
                string main = svc.MainPath(SaveSlots.Main);
                byte[] mainBytes = File.ReadAllBytes(main);

                foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    s.Player.ExcludedRewards["X"] = bad;
                    var e = Assert.Throws<InvalidDataException>(() => svc.Save(s.State, Now + 1000));
                    StringAssert.Contains("non-finite value at", e.Message);
                    StringAssert.Contains("Player.ExcludedRewards.X", e.Message, "исключение называет путь поля");
                    Assert.Throws<InvalidDataException>(() => SaveService.SerializeState(s.State), "снимок в память тоже проверяется");
                    Assert.Throws<InvalidDataException>(() => svc.BuildEnvelope(s.State, Now));

                    CollectionAssert.AreEqual(new[] { "save_main.json" }, FileNames(dir), "ни .tmp, ни .bak не появились");
                    CollectionAssert.AreEqual(mainBytes, File.ReadAllBytes(main), "основной файл не тронут");
                }

                var loaded = new SaveService(dir).Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status, loaded.Message);
                Assert.AreEqual(valid, SaveService.SerializeState(loaded.State), "загружается прежнее состояние");

                // После исправления значения запись снова проходит тем же экземпляром (буферы не испорчены).
                s.Player.ExcludedRewards["X"] = 3;
                svc.Save(s.State, Now + 2000);
                var after = new SaveService(dir).Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, after.Status, after.Message);
                Assert.AreEqual(3.0, after.State.Player.ExcludedRewards["X"]);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void NaNSnapshotTouchesNoFileOperation()
        {
            string dir = NewTempDir();
            try
            {
                var fs = new RecordingFs();
                var s = NewSession();
                s.Player.ExcludedRewards["battlepass"] = double.NaN;
                Assert.Throws<InvalidDataException>(() => new SaveService(dir, fs).Save(s.State, Now));
                CollectionAssert.IsEmpty(fs.Ops, "проверка снимка — до любых файловых операций");
                CollectionAssert.IsEmpty(FileNames(dir));
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // ───────────── Формат: payload и конверт байт в байт как раньше ─────────────

        static IEnumerable<GameState> FormatSamples()
        {
            var fresh = NewSession();
            yield return fresh.State;

            var busy = NewSession(3, SaveSlots.Debug);
            busy.Hire();
            busy.Hire();
            for (int i = 0; i < 3; i++) busy.DebugSpawnCat(5);
            busy.Advance(30 * GameSession.TicksPerSecond);
            busy.Player.ExcludedRewards["battlepass"] = 0.1 + 0.2;
            busy.Player.TutorialDone.Add("кавычка \" обратная \\ перевод\nстроки\tтаб \u0001 \u001f \u0085 \u2028 \u2029 \u00a0 é 漢");
            yield return busy.State;

            var odd = NewSession(2);
            odd.State.BalanceSha256 = null;
            odd.State.Slot = "Slot \"x\"\\ \u2028";
            yield return odd.State;
        }

        [Test]
        public void PayloadAndEnvelopeAreByteIdenticalToPlainSerializer()
        {
            var svc = new SaveService(NewTempDir());
            foreach (var pass in new[] { 1, 2 })   // второй проход — проверка переиспользования буферов
            {
                int n = 0;
                foreach (var state in FormatSamples())
                {
                    string where = "проход " + pass + ", образец " + n++;
                    string plain = JsonConvert.SerializeObject(state, SaveService.Settings());
                    Assert.AreEqual(plain, SaveService.SerializeState(state), where + ": payload");

                    long now = 1_700_000_000_000L + n;
                    byte[] legacy = LegacySaveFormat.Envelope(state, now);
                    byte[] actual = svc.BuildEnvelope(state, now);
                    Assert.AreEqual(Encoding.UTF8.GetString(legacy), Encoding.UTF8.GetString(actual), where + ": конверт");
                    CollectionAssert.AreEqual(legacy, actual, where + ": байты конверта");

                    var env = JObject.Parse(Encoding.UTF8.GetString(actual));
                    Assert.AreEqual(plain, (string)env["payload"], where);
                    Assert.AreEqual(SaveService.Sha256Hex(plain), (string)env["payloadSha256"], where);
                }
            }
        }

        /// <summary>ТЗ 12.1: сохранить → загрузить → сохранить при неизменном состоянии даёт тот же payloadSha256.</summary>
        [Test]
        public void SaveLoadSaveKeepsPayloadSha()
        {
            string dir = NewTempDir();
            try
            {
                var s = NewSession(3, SaveSlots.Debug);
                s.Hire();
                s.Advance(10 * GameSession.TicksPerSecond);
                var svc = new SaveService(dir);
                svc.Save(s.State, Now);
                string first = (string)JObject.Parse(File.ReadAllText(svc.MainPath(SaveSlots.Debug)))["payloadSha256"];
                var loaded = svc.Load(SaveSlots.Debug, TestData.Balance.Sha256);
                svc.Save(loaded.State, Now);
                string second = (string)JObject.Parse(File.ReadAllText(svc.MainPath(SaveSlots.Debug)))["payloadSha256"];
                Assert.AreEqual(first, second);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // ───────────── ТЗ 12.2: повреждённые и несовместимые файлы откладываются, не удаляются ─────────────

        [Test]
        public void QuarantineRenamesWithReasonAndUtcStampAndAvoidsCollisions()
        {
            string dir = NewTempDir();
            try
            {
                Directory.CreateDirectory(dir);
                var svc = new SaveService(dir);
                string path = Path.Combine(dir, "save_main.json");
                File.WriteAllText(path, "раз");
                string moved = svc.Quarantine(path, "corrupt");
                Assert.IsNotNull(moved, svc.LastQuarantineError);
                Assert.IsNull(svc.LastQuarantineError);
                StringAssert.IsMatch("^" + Regex.Escape(path) + @"\.corrupt-\d{14}$", moved);
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual("раз", File.ReadAllText(moved), "файл переименован, содержимое цело");

                // Имена на ближайшие секунды заняты — выбирается свободное с суффиксом счётчика.
                File.WriteAllText(path, "два");
                var start = DateTime.UtcNow;
                for (int sec = -2; sec <= 5; sec++)
                {
                    string taken = path + ".corrupt-" + start.AddSeconds(sec).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                    if (!File.Exists(taken)) File.WriteAllText(taken, "занято");
                }
                string second = svc.Quarantine(path, "corrupt");
                Assert.IsNotNull(second, svc.LastQuarantineError);
                StringAssert.IsMatch("^" + Regex.Escape(path) + @"\.corrupt-\d{14}-2$", second);
                Assert.AreEqual("два", File.ReadAllText(second));

                // Неудача не проглатывается молча: null и причина.
                Assert.IsNull(svc.Quarantine(Path.Combine(dir, "nosuchfile.json"), "corrupt"));
                StringAssert.Contains("nosuchfile.json", svc.LastQuarantineError);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void CorruptedMainIsQuarantinedAndReportedWithSourcePath()
        {
            string dir = NewTempDir();
            try
            {
                var s = NewSession();
                var svc = new SaveService(dir);
                svc.Save(s.State, Now);
                s.Hire();
                svc.Save(s.State, Now + 1000);   // прежняя запись стала .bak
                string main = svc.MainPath(SaveSlots.Main);
                File.WriteAllText(main, "{ broken");

                var r = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.LoadedFromBackup, r.Status);
                Assert.AreEqual(main + ".bak", r.SourcePath, "источник — резервная копия");
                Assert.AreEqual(1, r.QuarantinedFiles.Count);
                StringAssert.StartsWith(main + ".corrupt-", r.QuarantinedFiles[0]);
                Assert.AreEqual("{ broken", File.ReadAllText(r.QuarantinedFiles[0]), "повреждённый файл не удалён");
                StringAssert.Contains("save_main.json", r.Message, "в сообщении названа причина отказа основного файла");

                // Ни одного валидного файла: Corrupted, источник — первый проверенный файл, оба отложены.
                File.WriteAllText(main + ".bak", "{ also broken");
                var none = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Corrupted, none.Status);
                Assert.IsNull(none.State);
                Assert.AreEqual(main + ".bak", none.SourcePath);
                Assert.AreEqual(1, none.QuarantinedFiles.Count);
                Assert.IsFalse(File.Exists(main + ".bak"));
                Assert.AreEqual(2, FileNames(dir).Count(f => f.Contains(".corrupt-")), "оба повреждённых файла сохранены");

                var empty = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.NoSave, empty.Status);
                Assert.IsNull(empty.SourcePath);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void QuarantineFailureIsReportedInLoadMessage()
        {
            string dir = NewTempDir();
            try
            {
                var fs = new RecordingFs();
                var svc = new SaveService(dir, fs);
                svc.Save(NewSession().State, Now);
                string main = svc.MainPath(SaveSlots.Main);
                File.WriteAllText(main, "{ broken");
                fs.FailMove = true;
                var r = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Corrupted, r.Status);
                Assert.AreEqual(main, r.SourcePath);
                CollectionAssert.IsEmpty(r.QuarantinedFiles);
                StringAssert.Contains("не удалось переименовать", r.Message);
                StringAssert.Contains("переименование запрещено", r.Message);
                Assert.IsNotNull(svc.LastQuarantineError);
                Assert.IsTrue(File.Exists(main), "файл остался на месте, не удалён");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        /// <summary>
        /// ТЗ 12.1: другой balanceSha256 — файл переименовывается в *.incompatible-&lt;hash8&gt;; оставшиеся tmp/bak слота
        /// тоже откладываются, чтобы первая запись новой игры их не перезаписала.
        /// </summary>
        [Test]
        public void IncompatibleBalanceQuarantinesMainTmpAndBak()
        {
            string dir = NewTempDir();
            try
            {
                var s = NewSession();
                var svc = new SaveService(dir);
                svc.Save(s.State, Now);
                s.Hire();
                svc.Save(s.State, Now + 1000);
                string main = svc.MainPath(SaveSlots.Main);
                File.Copy(main, main + ".tmp");
                byte[] mainBytes = File.ReadAllBytes(main), bakBytes = File.ReadAllBytes(main + ".bak");
                string tag = TestData.Balance.Sha256.Substring(0, 8);

                var r = svc.Load(SaveSlots.Main, new string('0', 64));
                Assert.AreEqual(LoadStatus.IncompatibleBalance, r.Status);
                Assert.IsNull(r.State);
                Assert.AreEqual(main, r.SourcePath);
                StringAssert.StartsWith("Сохранение создано для другого баланса (" + tag + ")", r.Message);
                CollectionAssert.AreEquivalent(
                    new[] { main + ".incompatible-" + tag, main + ".tmp.incompatible-" + tag, main + ".bak.incompatible-" + tag },
                    r.QuarantinedFiles);
                CollectionAssert.AreEquivalent(
                    new[] { "save_main.json.bak.incompatible-" + tag, "save_main.json.incompatible-" + tag, "save_main.json.tmp.incompatible-" + tag },
                    FileNames(dir), "в слоте не осталось main/tmp/bak");

                // Новая игра пишет слот дважды (вторая запись создаёт .bak) — отложенные файлы целы.
                var fresh = GameSession.NewGame(TestData.Balance, 777UL, SaveSlots.Main, Now, 1);
                svc.Save(fresh.State, Now + 2000);
                svc.Save(fresh.State, Now + 3000);
                CollectionAssert.AreEqual(mainBytes, File.ReadAllBytes(main + ".incompatible-" + tag));
                CollectionAssert.AreEqual(mainBytes, File.ReadAllBytes(main + ".tmp.incompatible-" + tag));
                CollectionAssert.AreEqual(bakBytes, File.ReadAllBytes(main + ".bak.incompatible-" + tag));

                // Повторный отказ того же баланса не затирает прежний отложенный файл: имя со счётчиком.
                var again = svc.Load(SaveSlots.Main, new string('1', 64));
                Assert.AreEqual(LoadStatus.IncompatibleBalance, again.Status);
                CollectionAssert.Contains(again.QuarantinedFiles, main + ".incompatible-" + tag + "-2");
                CollectionAssert.AreEqual(mainBytes, File.ReadAllBytes(main + ".incompatible-" + tag));
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void IncompatibleBalanceQuarantinesCorruptLeftoverAsCorrupt()
        {
            string dir = NewTempDir();
            try
            {
                var svc = new SaveService(dir);
                svc.Save(NewSession().State, Now);
                string main = svc.MainPath(SaveSlots.Main);
                File.WriteAllText(main + ".bak", "{ torn");
                var r = svc.Load(SaveSlots.Main, new string('0', 64));
                Assert.AreEqual(LoadStatus.IncompatibleBalance, r.Status);
                Assert.AreEqual(2, r.QuarantinedFiles.Count);
                Assert.IsTrue(r.QuarantinedFiles.Any(f => f.StartsWith(main + ".bak.corrupt-", StringComparison.Ordinal)), string.Join(", ", r.QuarantinedFiles));
                Assert.IsFalse(File.Exists(main) || File.Exists(main + ".bak"));
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void RulesVersionMismatchIsReportedWithSourcePath()
        {
            string dir = NewTempDir();
            try
            {
                var s = NewSession();
                s.State.RulesVersion = Formula.RulesVersion + 1;
                var svc = new SaveService(dir);
                svc.Save(s.State, Now);
                var r = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.RulesChangedMineReset, r.Status);
                Assert.IsNotNull(r.State, "PlayerProgressState сохраняется — состояние возвращается для сброса шахты");
                Assert.AreEqual(svc.MainPath(SaveSlots.Main), r.SourcePath);
                StringAssert.Contains("rulesVersion", r.Message);
                CollectionAssert.IsEmpty(r.QuarantinedFiles, "сохранение не откладывается и не теряется");
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // ───────────── ТЗ 12.1: миграции схемы и фикстуры ─────────────

        static string FixturePath => Path.Combine(TestData.ProjectRoot, FixtureRelativePath);

        /// <summary>
        /// Детерминированная сессия фикстуры v1: zone1, слот Main, два найма, котики у целей, 90 с игры,
        /// затем новые назначения и 2.5 с — в снимке есть котики посреди работы (цель и фаза удара).
        /// </summary>
        static GameSession FixtureSession()
        {
            var s = GameSession.NewGame(TestData.Balance, 20261005UL, SaveSlots.Main, 1_760_000_000_000L, 1);
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            AssignIdleCats(s);
            s.Advance(90 * GameSession.TicksPerSecond);
            AssignIdleCats(s);
            s.Advance(2_500_000L);
            return s;
        }

        static void AssignIdleCats(GameSession s)
        {
            foreach (var cat in s.Mine.Cats.ToArray())
            {
                if (cat.TargetIndex >= 0) continue;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    var o = s.Layout.Objects[ord];
                    if (!o.IsTarget || !s.AcceptsCat(ord, cat.Id)) continue;
                    s.DropCat(cat.Id, s.Layout.Col(o.Cells[0]) + 0.5, s.Layout.Row(o.Cells[0]) + 0.5);
                    if (s.FindCat(cat.Id)?.TargetIndex >= 0) break;
                }
            }
        }

        /// <summary>
        /// Создаёт фикстуру сохранения v1 настоящим SaveService. Запускается один раз вручную:
        /// run.ps1 -Filter GenerateSaveV1MainFixture. Существующую фикстуру не перезаписывает — она фиксирует формат v1.
        /// </summary>
        [Test, Explicit("Создаёт Assets/Game/Tests/Fixtures/Saves/save_v1_main.json; запускать вручную")]
        public void GenerateSaveV1MainFixture()
        {
            if (File.Exists(FixturePath)) Assert.Ignore("Фикстура уже есть: " + FixturePath + ". Формат v1 зафиксирован, перезапись запрещена.");
            Assert.AreEqual(1, GameState.CurrentSchemaVersion, "фикстура v1 создаётся только кодом схемы v1");
            string dir = NewTempDir();
            try
            {
                var s = FixtureSession();
                var svc = new SaveService(dir);
                svc.Save(s.State, 1_760_000_092_500L);
                Directory.CreateDirectory(Path.GetDirectoryName(FixturePath));
                File.Copy(svc.MainPath(SaveSlots.Main), FixturePath);
                TestContext.WriteLine("Записано: " + FixturePath);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        [Test]
        public void SaveV1FixtureLoadsThroughMigrationPath()
        {
            Assert.IsTrue(File.Exists(FixturePath), "нет фикстуры " + FixtureRelativePath);
            string dir = NewTempDir();
            try
            {
                Directory.CreateDirectory(dir);
                var svc = new SaveService(dir);
                File.Copy(FixturePath, svc.MainPath(SaveSlots.Main));
                var env = JObject.Parse(File.ReadAllText(FixturePath, Encoding.UTF8));
                Assert.AreEqual(1, (int)env["schemaVersion"], "фикстура — схема v1");
                Assert.AreEqual(BalanceRepository.ExpectedSha256, (string)env["balanceSha256"]);

                var r = svc.Load(SaveSlots.Main, BalanceRepository.ExpectedSha256);
                var expectedStatus = (int)env["rulesVersion"] == Formula.RulesVersion ? LoadStatus.Loaded : LoadStatus.RulesChangedMineReset;
                Assert.AreEqual(expectedStatus, r.Status, r.Message);
                Assert.AreEqual(svc.MainPath(SaveSlots.Main), r.SourcePath);
                CollectionAssert.IsEmpty(r.QuarantinedFiles);

                // Ключевые поля фикстуры (значения записаны при её создании).
                var st = r.State;
                Assert.AreEqual(GameState.CurrentSchemaVersion, st.SchemaVersion);
                Assert.AreEqual(SaveSlots.Main, st.Slot);
                Assert.AreEqual(BalanceRepository.ExpectedSha256, st.BalanceSha256);
                Assert.AreEqual(FixtureLastProcessedUtcMs, st.LastProcessedUtcMs);
                Assert.AreEqual(FixtureNextOpId, st.NextOpId);
                Assert.AreEqual(1, st.Player.CurrentZone);
                Assert.AreEqual(1, st.Mine.Zone);
                Assert.AreEqual(2, st.Mine.HireCount);
                Assert.AreEqual(2, st.Mine.Cats.Count);
                Assert.AreEqual(FixtureGold, st.Mine.Gold.ToStorageString());
                Assert.AreEqual(FixtureElixir, st.Player.Elixir.ToStorageString());
                Assert.AreEqual(FixtureRocksDestroyed, st.Mine.RocksDestroyed);
                Assert.AreEqual(FixtureObjects, st.Mine.Objects.Count);
                Assert.AreEqual(2L, st.Player.RewardCycle["rockonboardingmine1"]);
                CollectionAssert.AreEqual(new[] { 23, 24 }, st.Mine.Cats.Select(c => c.TargetIndex).ToArray(), "котики посреди работы");
                CollectionAssert.AreEqual(new[] { 500_000L, 500_000L }, st.Mine.Cats.Select(c => c.PhaseTicks).ToArray(), "фаза удара");

                // Payload → Migrate → DeserializeState даёт то же состояние, что Load.
                string payload = (string)env["payload"];
                string migrated = SaveService.Migrate(payload, (int)env["schemaVersion"]);
                Assert.AreEqual(payload, migrated, "v1 — текущая схема: payload не меняется");
                var direct = SaveService.DeserializeState(migrated);
                Assert.AreEqual(SaveService.SerializeState(st), SaveService.SerializeState(direct));

                // Состояние подходит к карте зоны.
                var session = GameSession.FromState(TestData.Balance, st);
                Assert.AreEqual(2, session.Mine.Cats.Count);
            }
            finally
            {
                DeleteDir(dir);
            }
        }

        // Значения фикстуры save_v1_main.json (созданы GenerateSaveV1MainFixture).
        const long FixtureLastProcessedUtcMs = 1_760_000_000_000L;
        const long FixtureNextOpId = 5;
        const string FixtureGold = "467";
        const string FixtureElixir = "0";
        const int FixtureRocksDestroyed = 2;
        const int FixtureObjects = 22;

        [Test]
        public void MigrationTableCoversEveryVersionBelowCurrent()
        {
            for (int v = 1; v < GameState.CurrentSchemaVersion; v++)
                Assert.IsTrue(SaveService.Migrations.ContainsKey(v), "нет миграции " + v + " → " + (v + 1));
            Assert.IsFalse(SaveService.Migrations.Keys.Any(v => v < 1 || v >= GameState.CurrentSchemaVersion), "лишние шаги миграции");
        }

        [Test]
        public void MigrateAppliesStepsInOrderAndRejectsGaps()
        {
            var steps = new Dictionary<int, Func<JObject, JObject>>
            {
                [1] = doc =>
                {
                    doc["Added"] = 5;
                    return doc;
                },
                [2] = doc =>
                {
                    doc["Renamed"] = doc["Added"];
                    doc.Remove("Added");
                    return doc;
                },
            };
            var start = SaveService.ParsePayload(SaveService.SerializeState(NewSession().State));
            var v3 = SaveService.MigrateDocument((JObject)start.DeepClone(), 1, 3, steps);
            Assert.AreEqual(3, (int)v3["SchemaVersion"]);
            Assert.AreEqual(5, (int)v3["Renamed"]);
            Assert.IsNull(v3["Added"]);

            steps.Remove(2);
            var e = Assert.Throws<InvalidDataException>(() => SaveService.MigrateDocument((JObject)start.DeepClone(), 1, 3, steps));
            StringAssert.Contains("2 → 3", e.Message);

            string payload = SaveService.SerializeState(NewSession().State);
            Assert.AreSame(payload, SaveService.Migrate(payload, GameState.CurrentSchemaVersion), "текущая версия — без разбора");
            Assert.Throws<InvalidDataException>(() => SaveService.Migrate(payload, 0));
            Assert.Throws<InvalidDataException>(() => SaveService.Migrate(payload, GameState.CurrentSchemaVersion + 1));
        }

        // ───────────── ТЗ 11.3: у каждого поля сохранения указан владелец ─────────────

        static IEnumerable<string> PersistedMembers(Type type)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (var f in type.GetFields(flags))
                if (!f.IsDefined(typeof(JsonIgnoreAttribute), false)) yield return f.Name;
            foreach (var p in type.GetProperties(flags))
                if (p.CanRead && p.GetIndexParameters().Length == 0 && !p.IsDefined(typeof(JsonIgnoreAttribute), false)) yield return p.Name;
        }

        /// <summary>
        /// Таблица владельцев в комментарии SaveService (ТЗ 11.3: «в SaveService у каждого поля сохранения явно указано,
        /// к какому из классов оно относится») перечисляет каждое сохраняемое поле корня, Player и Mine.
        /// Новое поле состояния — новая строка таблицы.
        /// </summary>
        [Test]
        public void OwnershipTableInSaveServiceCoversEveryPersistedField()
        {
            string source = File.ReadAllText(Path.Combine(TestData.ProjectRoot, "Assets/Game/Scripts/Core/Save/SaveService.cs"), Encoding.UTF8);
            var rows = source.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("/// |", StringComparison.Ordinal)).ToList();
            Assert.Greater(rows.Count, 10, "таблица владельцев полей в SaveService");
            var cells = rows.Select(r => r.Substring(4).Split('|').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray()).Where(c => c.Length >= 2).ToList();

            bool Listed(string name, string owner) =>
                cells.Any(c => c[1].StartsWith(owner, StringComparison.Ordinal) &&
                               c[0].Split(',').Select(x => x.Trim()).Contains(name));

            var missing = new List<string>();
            var playerType = typeof(GameState).GetField("Player").FieldType;
            var mineType = typeof(GameState).GetField("Mine").FieldType;
            foreach (var name in PersistedMembers(typeof(GameState)))
            {
                if (name == "Player" || name == "Mine") continue;
                if (!Listed(name, "корень")) missing.Add(name + " (корень)");
            }
            foreach (var name in PersistedMembers(playerType))
                if (!Listed("Player." + name, "PlayerProgressState")) missing.Add("Player." + name + " → PlayerProgressState");
            foreach (var name in PersistedMembers(mineType))
                if (!Listed("Mine." + name, "MineProgressState")) missing.Add("Mine." + name + " → MineProgressState");
            CollectionAssert.IsEmpty(missing, "добавьте поля в таблицу владельцев в комментарии SaveService");
        }

        /// <summary>Envelope с неизвестной будущей версией схемы — файл не принимается и откладывается как повреждённый.</summary>
        [Test]
        public void FutureSchemaVersionIsNotLoaded()
        {
            string dir = NewTempDir();
            try
            {
                var svc = new SaveService(dir);
                svc.Save(NewSession().State, Now);
                string main = svc.MainPath(SaveSlots.Main);
                var env = JObject.Parse(File.ReadAllText(main));
                env["schemaVersion"] = GameState.CurrentSchemaVersion + 1;
                File.WriteAllText(main, env.ToString(Formatting.None));
                var r = svc.Load(SaveSlots.Main, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Corrupted, r.Status);
                StringAssert.Contains("версия схемы", r.Message);
                Assert.AreEqual(1, r.QuarantinedFiles.Count);
            }
            finally
            {
                DeleteDir(dir);
            }
        }
    }
}
