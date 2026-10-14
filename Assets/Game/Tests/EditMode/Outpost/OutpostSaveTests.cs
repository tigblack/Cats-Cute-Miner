using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostKitA;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Сохранение нового режима (ТЗ §17, §20 «Аварийная запись», «Версия конфигурации», «Большие целые»; CORE_SPEC §12):
    /// конверт и контрольная сумма, выбор commit main/bak, tmp не commit, сбой на каждом шаге записи,
    /// будущая схема и повреждение — карантин без перезаписи, BigInteger без потери единицы, баланс run не пересобирается.
    /// Маркер commit хранится в Profile.Parts и Run.LastProcessedUtcMs (SaveKitA.Marker) — их расхождение = смешение профилей.
    /// </summary>
    [TestFixture]
    public class OutpostSaveTests
    {
        [Test]
        public void PlayTime_OldPayloadStartsAtZero_NewCounterSurvivesSaveAndLoad()
        {
            var original = SaveKitA.MakeState(100);
            var payload = JObject.Parse(OutpostSaveService.SerializeState(original));
            Assert.That(((JObject)payload["Profile"]).Remove("PlayTimeMs"), Is.True);
            var legacy = OutpostSaveService.DeserializeState(payload.ToString());
            Assert.That(legacy.Profile.PlayTimeMs, Is.Zero);
            legacy.Profile.PlayTimeMs = 125 * 60_000L + 12_345;
            var service = SaveKitA.NewService(new MemoryFileSystemA());
            Assert.That(SaveKitA.TrySave(service, legacy), Is.True);
            var loaded = service.Load();
            Assert.That(loaded.State.Profile.PlayTimeMs, Is.EqualTo(legacy.Profile.PlayTimeMs));
            Assert.That(loaded.State.Run.Zone, Is.EqualTo(original.Run.Zone));
        }

        [Test]
        public void PlayTime_NegativeCounterCannotBeCommitted()
        {
            var state = SaveKitA.MakeState(100);
            state.Profile.PlayTimeMs = -1;
            var service = SaveKitA.NewService(new MemoryFileSystemA());
            Assert.That(SaveKitA.TrySave(service, state), Is.False);
        }

        /// <summary>Два commit: main = маркер 200, bak = маркер 100.</summary>
        static (MemoryFileSystemA fs, SaveServiceA svc) TwoCommits(bool replaceUnsupported = false)
        {
            var fs = new MemoryFileSystemA { ReplaceUnsupported = replaceUnsupported };
            var svc = SaveKitA.NewService(fs);
            Assert.IsTrue(SaveKitA.TrySave(svc, SaveKitA.MakeState(100)), "commit 100");
            Assert.IsTrue(SaveKitA.TrySave(svc, SaveKitA.MakeState(200)), "commit 200");
            return (fs, svc);
        }

        static long LoadMarker(MemoryFileSystemA fs, out OutpostLoadResult result)
        {
            result = SaveKitA.Load(SaveKitA.NewService(fs));
            Assert.IsTrue(SaveKitA.IsLoaded(result), "ожидался валидный commit, статус " + result.Status);
            return SaveKitA.Marker(result.State);
        }

        static long LoadMarker(MemoryFileSystemA fs) => LoadMarker(fs, out _);

        static Dictionary<string, byte[]> Snapshot(MemoryFileSystemA fs) => fs.Files.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone());

        static void AssertOriginalsUntouched(Dictionary<string, byte[]> before, MemoryFileSystemA fs)
        {
            foreach (var kv in before)
            {
                Assert.IsTrue(fs.Files.ContainsKey(kv.Key), "файл удалён: " + kv.Key);
                CollectionAssert.AreEqual(kv.Value, fs.Files[kv.Key], "файл перезаписан: " + kv.Key);
            }
        }

        static void AssertQuarantined(MemoryFileSystemA fs, byte[] original, string what)
        {
            bool found = fs.Files.Any(kv => kv.Key.Contains("quarantine-") && kv.Value.SequenceEqual(original));
            Assert.IsTrue(found, what + ": нет копии в quarantine-*. Файлы: " + string.Join(", ", fs.Files.Keys));
        }

        // ───────────────────────────── Конверт ─────────────────────────────

        [TestCase(false)]
        [TestCase(true)]
        public void ResetBackup_RecoveryKeepsFreshProgress_AndDebugSlotUntouched(bool replaceUnsupported)
        {
            var (fs, _) = TwoCommits(replaceUnsupported);
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs, debug: true), SaveKitA.MakeState(300)));
            byte[] debug = fs.Get(SaveKitA.DebugMain);
            var service = new OutpostSaveService(SaveKitA.Root, fs) { Configs = Configs };
            var fresh = SaveKitA.MakeState(0);
            Assert.IsTrue(service.Save(fresh, resetBackup: true).Ok);
            CollectionAssert.AreEqual(fs.Get(SaveKitA.Main), fs.Get(SaveKitA.Bak));
            CollectionAssert.AreEqual(debug, fs.Get(SaveKitA.DebugMain));
            fs.Put(SaveKitA.Main, "corrupt after reset");
            var loaded = service.Load();
            Assert.AreEqual(OutpostLoadStatus.LoadedFromBackup, loaded.Status);
            Assert.AreEqual(0, SaveKitA.Marker(loaded.State));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetBackup_TornBackupWrite_IsReported_AndRetryKeepsNewCommit(bool replaceUnsupported)
        {
            var (probe, _) = TwoCommits(replaceUnsupported);
            var probeService = new OutpostSaveService(SaveKitA.Root, probe) { Configs = Configs };
            int start = probe.Steps;
            var fresh = SaveKitA.MakeState(0);
            Assert.IsTrue(probeService.Save(fresh, resetBackup: true).Ok);
            int steps = probe.Steps - start;

            var (fs, _) = TwoCommits(replaceUnsupported);
            var service = new OutpostSaveService(SaveKitA.Root, fs) { Configs = Configs };
            fs.ArmCrash(steps, torn: true);
            var failed = service.Save(fresh, resetBackup: true);
            Assert.IsFalse(failed.Ok);
            Assert.AreEqual("reset-backup", failed.FailedStep);
            var restarted = fs.CrashImage();
            var retry = new OutpostSaveService(SaveKitA.Root, restarted) { Configs = Configs };
            Assert.AreEqual(0, SaveKitA.Marker(retry.Load().State), "main уже содержит новый согласованный снимок");
            Assert.IsTrue(retry.Save(fresh, resetBackup: true).Ok);
            CollectionAssert.AreEqual(restarted.Get(SaveKitA.Main), restarted.Get(SaveKitA.Bak));
            restarted.Put(SaveKitA.Main, "corrupt after retry");
            Assert.AreEqual(0, SaveKitA.Marker(retry.Load().State));
        }

        // ТЗ §17: modeId, schemaVersion 1, rulesVersion, balanceVersion, SHA-256 источника, hash каталога, commitSequence, checksum payload.
        [Test]
        public void Envelope_HasAllFields_AndValidChecksum()
        {
            var (fs, _) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            Assert.AreEqual("CatOutpostV2", (string)env["ModeId"]);
            Assert.AreEqual(OutpostIds.SchemaVersion, (int)env["SchemaVersion"]);
            Assert.AreEqual("outpost-2.1-r1", (string)env["RulesVersion"]);
            Assert.AreEqual("outpost-2.1-b4", (string)env["BalanceVersion"]);
            Assert.AreEqual(OutpostIds.BalanceVersion, (string)env["BalanceVersion"]);
            Assert.AreEqual(SourceSha256, (string)env["SourceSha256"]);
            Assert.IsTrue(IsSha256Hex((string)env["CatalogHash"]));
            Assert.AreEqual(EnvelopeCatalogHash(Configs), (string)env["CatalogHash"], "hash экипировки+наград текущих конфигураций");
            string payload = (string)env["Payload"];
            Assert.IsNotNull(payload);
            Assert.IsFalse(payload.Contains("\n"), "payload одной строкой");
            Assert.AreEqual(Sha256Hex(payload), ((string)env["PayloadSha256"]).ToLowerInvariant());
            var state = JObject.Parse(payload);
            Assert.IsNotNull(state["Profile"]);
            Assert.IsNotNull(state["Run"]);
            Assert.AreEqual("CatOutpostV2", (string)state["Profile"]["ModeId"]);
        }

        // ТЗ §17: «монотонный commitSequence»; main — новый commit, bak — предыдущий.
        [Test]
        public void CommitSequence_IsMonotone_AndBackupKeepsPrevious()
        {
            var (fs, _) = TwoCommits();
            long mainSeq = (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["CommitSequence"];
            long bakSeq = (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Bak)["CommitSequence"];
            Assert.That(bakSeq, Is.GreaterThanOrEqualTo(1));
            Assert.AreEqual(bakSeq + 1, mainSeq);
            Assert.AreEqual(200, SaveKitA.Marker(Newtonsoft.Json.JsonConvert.DeserializeObject<OutpostGameState>(
                (string)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["Payload"], OutpostJson.Settings)));
            Assert.AreEqual(100, SaveKitA.Marker(Newtonsoft.Json.JsonConvert.DeserializeObject<OutpostGameState>(
                (string)SaveKitA.ReadEnvelope(fs, SaveKitA.Bak)["Payload"], OutpostJson.Settings)));
            Assert.IsFalse(fs.Files.ContainsKey(SaveKitA.Tmp), "после успешной записи tmp не остаётся");
        }

        // Последовательность продолжается после перезапуска (новый сервис), иначе bak со старшим номером «победил» бы main.
        [Test]
        public void CommitSequence_ContinuesAfterRestart()
        {
            var (fs, _) = TwoCommits();
            long before = (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["CommitSequence"];
            var restarted = SaveKitA.NewService(fs);
            var loaded = SaveKitA.Load(restarted);
            Assert.AreEqual(before, SaveKitA.Seq(loaded));
            Assert.IsTrue(SaveKitA.TrySave(restarted, SaveKitA.MakeState(300)));
            Assert.AreEqual(before + 1, (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["CommitSequence"]);
            Assert.AreEqual(300, LoadMarker(fs));
        }

        [Test]
        public void Load_NoFiles_ReturnsNoStateWithoutError()
        {
            var fs = new MemoryFileSystemA();
            var r = SaveKitA.Load(SaveKitA.NewService(fs));
            Assert.IsNull(r.State);
            Assert.AreNotEqual(OutpostLoadStatus.Corrupt, r.Status);
            Assert.AreNotEqual(OutpostLoadStatus.Incompatible, r.Status);
            Assert.AreEqual(0, fs.Files.Count, "загрузка без файлов ничего не создаёт");
        }

        // ───────────────────────────── Выбор commit ─────────────────────────────

        // CORE_SPEC §12: отбросить невалидные (PayloadSha256) — main с неверной суммой отвергнут, используется bak.
        [Test]
        public void PayloadChecksumMismatch_MainRejected_BackupUsed()
        {
            var (fs, _) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            var payload = SaveKitA.Payload(env);
            payload["Profile"]["Parts"] = 999;
            payload["Run"]["LastProcessedUtcMs"] = 999;
            SaveKitA.SetPayload(env, payload, rehash: false);
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            Assert.AreEqual(100, LoadMarker(fs), "main с неверной контрольной суммой отвергнут");

            // контроль: та же правка с верной суммой принимается — значит, отказ вызван именно checksum
            SaveKitA.SetPayload(env, payload, rehash: true);
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            Assert.AreEqual(999, LoadMarker(fs));
        }

        [Test]
        public void PayloadSha256FieldTampered_MainRejected_BackupUsed()
        {
            var (fs, _) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            env["PayloadSha256"] = new string('0', 64);
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            Assert.AreEqual(100, LoadMarker(fs));
        }

        // ТЗ §17: «Валидатор загрузки проверяет совпадение … кошелька и счётчиков, Released <= Total».
        [TestCase("wallet")]
        [TestCase("released")]
        public void InvariantViolation_WithValidChecksum_MainRejected(string violation)
        {
            var (fs, _) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            var payload = SaveKitA.Payload(env);
            if (violation == "wallet") payload["Run"]["Wallet"] = "12345";
            else
            {
                var t = payload["Run"]["Targets"][0];
                t["ReleasedUnits"] = (int)t["TotalUnits"] + 1;
            }
            SaveKitA.SetPayload(env, payload, rehash: true);
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            Assert.AreEqual(100, LoadMarker(fs), "main с нарушенным инвариантом отвергнут: " + violation);
        }

        [Test]
        public void WrongModeId_MainRejected()
        {
            var (fs, _) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            env["ModeId"] = "GoldGoblins";
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            Assert.AreEqual(100, LoadMarker(fs));
        }

        // ТЗ §17: «выбирать валидный зафиксированный снимок main/backup с наибольшим commitSequence».
        [Test]
        public void HigherCommitSequence_WinsEvenInBackup()
        {
            var (fs, _) = TwoCommits();
            var main = fs.Get(SaveKitA.Main);
            var bak = fs.Get(SaveKitA.Bak);
            fs.Put(SaveKitA.Main, bak);
            fs.Put(SaveKitA.Bak, main);
            Assert.AreEqual(200, LoadMarker(fs, out var r));
            Assert.AreEqual((long)JObject.Parse(Utf8(main))["CommitSequence"], SaveKitA.Seq(r));
        }

        // ТЗ §17: «незавершённый tmp не считается commit» — даже валидный tmp со старшим номером.
        [Test]
        public void ValidTmpWithHigherSequence_IsIgnored()
        {
            var (fs, svc) = TwoCommits();
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            var payload = SaveKitA.Payload(env);
            payload["Profile"]["Parts"] = 999;
            payload["Run"]["LastProcessedUtcMs"] = 999;
            SaveKitA.SetPayload(env, payload, rehash: true);
            env["CommitSequence"] = (long)env["CommitSequence"] + 10;
            SaveKitA.WriteEnvelope(fs, SaveKitA.Tmp, env);
            Assert.AreEqual(200, LoadMarker(fs));
            // оставшийся tmp не мешает следующей записи
            Assert.IsTrue(SaveKitA.TrySave(svc, SaveKitA.MakeState(300)));
            Assert.AreEqual(300, LoadMarker(fs));
        }

        // ТЗ §17: «Повреждённые … не перезаписывать … попробовать валидный backup».
        [TestCase("garbage")]
        [TestCase("truncated")]
        [TestCase("empty")]
        public void CorruptMain_BackupUsed(string kind)
        {
            var (fs, _) = TwoCommits();
            var main = fs.Get(SaveKitA.Main);
            byte[] bad = kind == "garbage" ? Utf8Bytes("{{{ not json")
                : kind == "truncated" ? main.Take(main.Length / 2).ToArray()
                : new byte[0];
            fs.Put(SaveKitA.Main, bad);
            Assert.AreEqual(100, LoadMarker(fs));
        }

        // CORE_SPEC §12: «повреждение обоих — Corrupt, файлы копируются в quarantine-{utc}, ничего не перезаписывается».
        [Test]
        public void BothCorrupt_StatusCorrupt_FilesUntouched_Quarantined()
        {
            var (fs, _) = TwoCommits();
            fs.Put(SaveKitA.Main, Utf8Bytes("{{{"));
            var bak = fs.Get(SaveKitA.Bak);
            fs.Put(SaveKitA.Bak, bak.Take(bak.Length / 3).ToArray());
            var before = Snapshot(fs);
            var r = SaveKitA.Load(SaveKitA.NewService(fs));
            Assert.AreEqual(OutpostLoadStatus.Corrupt, r.Status);
            Assert.IsNull(r.State);
            AssertOriginalsUntouched(before, fs);
            AssertQuarantined(fs, before[SaveKitA.Main], "main");
            AssertQuarantined(fs, before[SaveKitA.Bak], "bak");
        }

        // ТЗ §17: «Повреждённые, несовместимые или будущие схемы не перезаписывать новой пустой игрой. Сохранить копии».
        [Test]
        public void FutureSchema_Incompatible_FilesUntouched_Quarantined()
        {
            var (fs, _) = TwoCommits();
            foreach (var path in new[] { SaveKitA.Main, SaveKitA.Bak })
            {
                var env = SaveKitA.ReadEnvelope(fs, path);
                env["SchemaVersion"] = OutpostIds.SchemaVersion + 1;
                var payload = SaveKitA.Payload(env);
                payload["Profile"]["SchemaVersion"] = OutpostIds.SchemaVersion + 1;
                SaveKitA.SetPayload(env, payload, rehash: true);
                SaveKitA.WriteEnvelope(fs, path, env);
            }
            var before = Snapshot(fs);
            var r = SaveKitA.Load(SaveKitA.NewService(fs));
            Assert.AreEqual(OutpostLoadStatus.Incompatible, r.Status);
            Assert.IsNull(r.State, "несовместимый профиль не подменяется пустой игрой");
            AssertOriginalsUntouched(before, fs);
            AssertQuarantined(fs, before[SaveKitA.Main], "main");
        }

        [Test]
        public void FutureSchema_SingleMain_Incompatible()
        {
            var fs = new MemoryFileSystemA();
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs), SaveKitA.MakeState(100)));
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            env["SchemaVersion"] = 99;
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            var before = Snapshot(fs);
            var r = SaveKitA.Load(SaveKitA.NewService(fs));
            Assert.AreEqual(OutpostLoadStatus.Incompatible, r.Status);
            AssertOriginalsUntouched(before, fs);
            AssertQuarantined(fs, before[SaveKitA.Main], "main");
        }

        // ───────────────────────────── Сбой на каждом шаге записи ─────────────────────────────

        // ТЗ §20 «Аварийная запись»: «На каждом этапе записи … восстанавливается один согласованный commit без смешения профилей».
        [TestCase(false)]
        [TestCase(true)]
        public void CrashAtEveryWriteStep_LoadsPreviousOrNewCommit(bool replaceUnsupported)
        {
            var (probe, probeSvc) = TwoCommits(replaceUnsupported);
            int start = probe.Steps;
            Assert.IsTrue(SaveKitA.TrySave(probeSvc, SaveKitA.MakeState(300)));
            int total = probe.Steps - start;
            Assert.That(total, Is.GreaterThanOrEqualTo(2), "запись tmp и замена main — минимум два шага: " + string.Join("; ", probe.Journal));

            int sawOld = 0, sawNew = 0;
            for (int step = 1; step <= total; step++)
                foreach (bool torn in new[] { false, true })
                {
                    var (fs, svc) = TwoCommits(replaceUnsupported);
                    long seqBefore = (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["CommitSequence"];
                    fs.ArmCrash(step, torn);
                    bool reported = SaveKitA.TrySave(svc, SaveKitA.MakeState(300));
                    var disk = fs.CrashImage();
                    string ctx = "шаг " + step + "/" + total + (torn ? " (обрыв записи)" : "") + "; журнал: " + string.Join("; ", fs.Journal);

                    long marker = LoadMarker(disk, out var r);
                    Assert.That(marker, Is.EqualTo(200).Or.EqualTo(300), ctx);
                    Assert.AreEqual(marker == 300 ? seqBefore + 1 : seqBefore, SaveKitA.Seq(r), "номер commit соответствует данным: " + ctx);
                    if (reported) Assert.AreEqual(300, marker, "сервис сообщил об успехе, а commit не записан: " + ctx);
                    if (step == 1) Assert.AreEqual(200, marker, "сбой до первого шага: " + ctx);
                    if (marker == 200) sawOld++; else sawNew++;

                    // после перезапуска запись снова работает и становится последним commit
                    var restarted = SaveKitA.NewService(disk);
                    SaveKitA.Load(restarted);
                    Assert.IsTrue(SaveKitA.TrySave(restarted, SaveKitA.MakeState(400)), "запись после восстановления: " + ctx);
                    Assert.AreEqual(400, LoadMarker(disk), ctx);
                }
            Assert.That(sawOld, Is.GreaterThan(0));
        }

        // После загрузки backup сбой новой записи не должен уничтожать восстановленный commit.
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void SaveAfterBackupRecovery_EveryCrashKeepsRecoveredCommit(bool replaceUnsupported, bool newerBackup)
        {
            (MemoryFileSystemA fs, SaveServiceA svc) Prepare()
            {
                var (fs, _) = TwoCommits(replaceUnsupported);
                if (newerBackup)
                {
                    byte[] main = fs.Get(SaveKitA.Main), bak = fs.Get(SaveKitA.Bak);
                    fs.Put(SaveKitA.Main, bak);
                    fs.Put(SaveKitA.Bak, main);
                }
                else fs.Put(SaveKitA.Main, "{{{ corrupt main");
                var svc = SaveKitA.NewService(fs);
                var loaded = SaveKitA.Load(svc);
                Assert.AreEqual(OutpostLoadStatus.LoadedFromBackup, loaded.Status);
                Assert.AreEqual(newerBackup ? 200 : 100, SaveKitA.Marker(loaded.State));
                return (fs, svc);
            }

            var (probe, probeSvc) = Prepare();
            int start = probe.Steps;
            Assert.IsTrue(SaveKitA.TrySave(probeSvc, SaveKitA.MakeState(300)));
            int total = probe.Steps - start;
            Assert.AreEqual(newerBackup ? 200 : 100,
                SaveKitA.Marker(OutpostSaveService.DeserializeState((string)SaveKitA.ReadEnvelope(probe, SaveKitA.Bak)["Payload"])),
                "backup сохраняет последний валидный commit, а не отвергнутый main");

            for (int step = 1; step <= total; step++)
                foreach (bool torn in new[] { false, true })
                {
                    var (fs, svc) = Prepare();
                    fs.ArmCrash(step, torn);
                    bool saved = SaveKitA.TrySave(svc, SaveKitA.MakeState(300));
                    long marker = LoadMarker(fs.CrashImage());
                    Assert.That(marker, Is.EqualTo(newerBackup ? 200 : 100).Or.EqualTo(300),
                        "сбой после восстановления backup, шаг " + step + ": " + string.Join("; ", fs.Journal));
                    if (saved) Assert.AreEqual(300, marker);
                }
        }

        [TestCase(ReplaceFaultA.NothingChanged)]
        [TestCase(ReplaceFaultA.BackupRenamedOnly)]
        public void ReplaceIOException_KeepsPreviousCommit_AndRetriesSameSnapshot(ReplaceFaultA fault)
        {
            var (fs, svc) = TwoCommits();
            fs.ReplaceFault = fault;
            var snapshot = SaveKitA.MakeState(300);
            Assert.IsFalse(SaveKitA.TrySave(svc, snapshot), "ошибку Replace нужно вернуть вызывающему для повтора записи");
            Assert.AreEqual(200, LoadMarker(fs), "ошибка Replace не уничтожает предыдущий commit");
            Assert.IsTrue(SaveKitA.TrySave(svc, snapshot), "повторяем только запись подготовленного результата");
            Assert.AreEqual(300, LoadMarker(fs));
        }

        // Самая первая запись: сбой оставляет либо «нет сохранения», либо новый commit — но не Corrupt.
        [TestCase(false)]
        [TestCase(true)]
        public void FirstSave_CrashAtEveryStep_NoCommitOrNewCommit(bool replaceUnsupported)
        {
            var probe = new MemoryFileSystemA { ReplaceUnsupported = replaceUnsupported };
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(probe), SaveKitA.MakeState(100)));
            int total = probe.Steps;
            for (int step = 1; step <= total; step++)
                foreach (bool torn in new[] { false, true })
                {
                    var fs = new MemoryFileSystemA { ReplaceUnsupported = replaceUnsupported };
                    fs.ArmCrash(step, torn);
                    bool reported = SaveKitA.TrySave(SaveKitA.NewService(fs), SaveKitA.MakeState(100));
                    var disk = fs.CrashImage();
                    string ctx = "шаг " + step + "/" + total + (torn ? " (обрыв)" : "") + "; журнал: " + string.Join("; ", fs.Journal);
                    var r = SaveKitA.Load(SaveKitA.NewService(disk));
                    Assert.AreNotEqual(OutpostLoadStatus.Corrupt, r.Status, "незавершённый tmp — не повреждение: " + ctx);
                    Assert.AreNotEqual(OutpostLoadStatus.Incompatible, r.Status, ctx);
                    if (r.State != null) Assert.AreEqual(100, SaveKitA.Marker(r.State), ctx);
                    if (reported) Assert.IsNotNull(r.State, "сервис сообщил об успехе: " + ctx);
                }
        }

        // ───────────────────────────── Профили и старые файлы ─────────────────────────────

        // ТЗ §17: отдельные каталоги main/ и debug/; старые save_main.json не используются для записи.
        [Test]
        public void DebugProfile_IsSeparate_LegacyFilesUntouched()
        {
            var fs = new MemoryFileSystemA();
            fs.Put(SaveKitA.LegacyMain, "legacy-profile");
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs, debug: true), SaveKitA.MakeState(55)));
            Assert.IsTrue(fs.Files.ContainsKey(SaveKitA.DebugMain), "debug-профиль в своём каталоге");
            Assert.IsFalse(fs.Files.ContainsKey(SaveKitA.Main), "debug не пишет в main");
            Assert.IsNull(SaveKitA.Load(SaveKitA.NewService(fs)).State, "основной профиль пуст");
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs), SaveKitA.MakeState(66)));
            Assert.AreEqual(66, LoadMarker(fs));
            Assert.AreEqual(55, SaveKitA.Marker(SaveKitA.Load(SaveKitA.NewService(fs, debug: true)).State));
            Assert.AreEqual("legacy-profile", Utf8(fs.Get(SaveKitA.LegacyMain)), "старый профиль не перезаписан");
        }

        // ───────────────────────────── Большие целые ─────────────────────────────

        // ТЗ §20 «Большие целые»: «сохранение значений выше 2^53 сохраняют единицу точно».
        [Test]
        public void BigIntegerAbove2Pow53_RoundTripsExactly()
        {
            var big = BigInteger.Pow(2, 60) + 7;
            var state = SaveKitA.MakeState(600);
            SaveKitA.ForceDeliveredValue(state, big);
            var fs = new MemoryFileSystemA();
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs), state), "состояние с одной сданной порцией согласовано");

            var run = SaveKitA.Payload(SaveKitA.ReadEnvelope(fs, SaveKitA.Main))["Run"];
            Assert.AreEqual(JTokenType.String, run["Wallet"].Type, "кошелёк — десятичная строка");
            Assert.AreEqual("1152921504606846983", (string)run["Wallet"]);
            Assert.AreEqual("1152921504606846983", (string)run["TotalCredited"]);

            LoadMarker(fs, out var r);
            Assert.AreEqual(big, r.State.Run.Wallet);
            Assert.AreEqual(big, r.State.Run.TotalCredited);
            Assert.AreEqual(big, r.State.Run.Counters.DeliveredValue);
            Assert.AreEqual(BigInteger.Zero, r.State.Run.TotalSpent);
        }

        // ───────────────────────────── Смена баланса ─────────────────────────────

        static void AdvanceUntilCargoExists(OutpostSimulation sim, long maxTicks)
        {
            long limit = sim.Now + maxTicks;
            while (SaveKitA.CargoValues(sim.State).Count == 0)
            {
                long next = sim.NextEventTick();
                if (next == long.MaxValue || next > limit) break;
                sim.Advance(next);
            }
        }

        // ТЗ §12.4: «MaxHp, TotalCargoUnits … фиксируются при создании текущей шахты. При загрузке не пересобирать HP и число порций».
        // ТЗ §17: «продолжать его с прежними HP, числом порций, ценами существующего груза и правилами цен/характеристик.
        // Новый баланс применяется при создании следующей шахты».
        [Test]
        public void BalanceChange_DoesNotRebuildCurrentRun()
        {
            var state = SaveKitA.MakeState(500);
            // Воспроизводим снимок b2, сохранённый до общего трёхчасового предела.
            state.Run.Balance.Version = "outpost-2.1-b2";
            state.Run.Balance.OfflineCapMs = 8L * 3600 * 1000;
            var map = Map(1);
            var sim = new OutpostSimulation(state, map, Configs);
            AdvanceUntilCargoExists(sim, 120 * Tps);
            state = sim.State;
            var cargoBefore = SaveKitA.CargoValues(state);
            Assert.That(cargoBefore, Is.Not.Empty, "за 120 с zone1 выпущен груз");
            var targetsBefore = state.Run.Targets.Select(t => t.Id + ":" + t.MaxHpUnits + ":" + t.TotalUnits + ":" + t.ValueFactor + ":" + t.RemainingHpUnits + ":" + t.ReleasedUnits).ToList();
            Assert.AreEqual(20L * 1_000_000, state.Run.Targets.Single(t => t.Id == 16).MaxHpUnits, "zone1 #16 — 20 HP по профилю b2");

            var fs = new MemoryFileSystemA();
            Assert.IsTrue(SaveKitA.TrySave(SaveKitA.NewService(fs), state));

            var changed = ParseConfigsWithBalance(j =>
            {
                j["Version"] = "outpost-2.1-b3";
                j["HpBase"] = "40";
                j["CoinBase"] = "50";
                j["BaseDamage"] = "50";
            });
            var loaded = SaveKitA.Load(SaveKitA.NewService(fs, configs: changed));
            Assert.IsTrue(SaveKitA.IsLoaded(loaded), "изменение баланса само по себе не делает сохранение несовместимым");
            var run = loaded.State.Run;
            CollectionAssert.AreEqual(targetsBefore,
                run.Targets.Select(t => t.Id + ":" + t.MaxHpUnits + ":" + t.TotalUnits + ":" + t.ValueFactor + ":" + t.RemainingHpUnits + ":" + t.ReleasedUnits).ToList(),
                "HP, число порций и коэффициенты целей взяты из сохранения");
            CollectionAssert.AreEqual(cargoBefore, SaveKitA.CargoValues(loaded.State), "цена существующего груза не пересчитана");
            Assert.AreEqual("outpost-2.1-b2", run.Balance.Version, "снимок баланса run сохранён");
            Assert.AreEqual("20", run.Balance.HpBase.ToString());
            Assert.AreEqual("5", run.Balance.CoinBase.ToString());

            // продолжение: характеристики и цена новых порций — по снимку run
            var sim2 = new OutpostSimulation(loaded.State, map, changed);
            AssertExact(sim2.Stats.FinalDamage, FracA.Of(5), "урон по снимку b2, а не BaseDamage = 50");
            sim2.Advance(sim2.Now + 30 * Tps);
            var after = sim2.State.Run;
            Assert.That(after.Counters.DeliveredUnits, Is.GreaterThan(0), "за 30 с продолжения груз сдаётся");
            // каждая сданная порция стоила 3 (x) или 5 (r): по ценам b3 было бы 25/50
            Assert.That(after.TotalCredited, Is.LessThanOrEqualTo(new BigInteger(5) * after.Counters.DeliveredUnits));
            Assert.That(after.TotalCredited, Is.GreaterThanOrEqualTo(new BigInteger(3) * after.Counters.DeliveredUnits));
            foreach (var kv in SaveKitA.CargoValues(sim2.State))
                Assert.That(kv.Value, Is.EqualTo(new BigInteger(3)).Or.EqualTo(new BigInteger(5)), "порция " + kv.Key + " по ценам b2 (x = 3, r = 5)");

            // следующая шахта создаётся уже по новому балансу
            var fresh = CreateRun(NewProfile(9), 1, changed, 0);
            Assert.AreEqual(40L * 1_000_000, fresh.Targets.Single(t => t.Id == 16).MaxHpUnits, "новый баланс применяется к новой шахте");
            Assert.AreEqual("outpost-2.1-b3", fresh.Balance.Version);
        }
    }
}
