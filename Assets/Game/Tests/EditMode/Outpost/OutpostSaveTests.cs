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
            // ТЗ 2.2 §10.1, §16.1 (б): схема 3, правила outpost-2.2-r1, профиль outpost-2.2-b5.
            Assert.AreEqual(3, (int)env["SchemaVersion"]);
            Assert.AreEqual(OutpostIds.SchemaVersion, (int)env["SchemaVersion"]);
            Assert.AreEqual("outpost-2.2-r1", (string)env["RulesVersion"]);
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
            // ТЗ 2.2 §10.2: состояние заводов, труб, склада и снимок factory_rules — в payload (новые проверки).
            var run = (JObject)state["Run"];
            foreach (var field in new[] { "FactoryRules", "Factories", "PipeBatches", "NextPipeBatchSeq", "Warehouse", "GoodsArrived", "GoodsSold", "GoodsSoldGold", "DebugGoods" })
                Assert.IsNotNull(run.Property(field), "поле run " + field);
            Assert.AreEqual("factories-1", (string)run["FactoryRules"]["Version"]);
            Assert.AreEqual(JTokenType.String, run["GoodsSoldGold"].Type, "золото продаж — десятичная строка");
            Assert.IsNotNull(((JObject)run["Counters"]).Property("ManualDeliveries"));
            // BalanceVersion конверта — версия снимка профиля run (outpost_balance.json, WP7).
            // Профиль b6 (решение владельца В4, 15 октября 2026: множитель темпа шахт K(z), Docs/Rework/BALANCE_FACTORIES.md);
            // текущий b8 — первая сессия: «Места разгрузки» и пороги «Обуви», «Сумок», «Разгрузки».
            Assert.AreEqual("outpost-2.2-b8", (string)env["BalanceVersion"]);
            Assert.AreEqual(OutpostIds.BalanceVersion, (string)env["BalanceVersion"]);
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

        // ───────────────────────────── Заводы в сохранении (ТЗ 2.2 §10.2) ─────────────────────────────

        static OutpostSimulation Zone2WithOpenFactories(out OutpostGameState state)
        {
            var profile = NewProfile(31);
            state = new OutpostGameState { Profile = profile, Run = CreateRun(profile, 2, Configs, 5_000) };
            var sim = new OutpostSimulation(state, Map(2), Configs) { RecordEvents = false };
            Assert.That(sim.Run.Factories.Count, Is.GreaterThan(0), "в шахте 2 есть завод (приложение A: Dairy)");
            Assert.That(sim.DebugUnlockFactories(), Is.EqualTo(sim.Run.Factories.Count), "цепи всех заводов разбиты");
            foreach (var f in sim.Run.Factories) Assert.IsTrue(sim.Submit(OutpostCommand.ActivateFactory(f.TargetId)).Ok);
            Assert.IsTrue(sim.Run.Factories.All(f => f.Status == FactoryStatus.Working && !f.Automated), "ручные заводы в работе");
            return sim;
        }

        /// <summary>Запись через сервис и загрузка новым сервисом: тот же JSON, симуляция после загрузки идёт так же.</summary>
        static OutpostSimulation SaveLoadEqual(MemoryFileSystemA fs, OutpostSimulation sim, string context)
        {
            var service = SaveKitA.NewService(fs);
            var written = service.Save(sim.State);
            Assert.IsTrue(written.Ok, context + ": " + written.Error);
            var loaded = SaveKitA.NewService(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status, context + ": " + string.Join("; ", loaded.Messages));
            Assert.AreEqual(OutpostSaveService.SerializeState(sim.State), OutpostSaveService.SerializeState(loaded.State), context + ": без нового округления");
            return new OutpostSimulation(loaded.State, sim.Map, Configs) { RecordEvents = false };
        }

        // ТЗ 2.2 §10.2: «Сохранение и загрузка посреди цикла, во время пути по трубе и в состоянии Ready восстанавливают состояние
        // без нового округления» — через конверт и файлы сервиса, с проверкой инвариантов при записи и загрузке.
        [Test]
        public void Factories_SaveLoadThroughService_MidCycleReadyAndInPipe_ByteIdentical()
        {
            var fs = new MemoryFileSystemA();
            var sim = Zone2WithOpenFactories(out _);
            var f = sim.Run.Factories[0];
            long cycleEnd = f.CycleEnd;
            Assert.That(cycleEnd, Is.GreaterThan(sim.Now));

            // Посреди цикла.
            sim.Advance(sim.Now + (cycleEnd - sim.Now) / 2);
            var copy = SaveLoadEqual(fs, sim, "посреди цикла");
            Assert.AreEqual(cycleEnd, copy.Run.Factories[0].CycleEnd);

            // Готовая партия (ручной режим): Ready, единицы зафиксированы.
            sim.Advance(cycleEnd);
            copy.Advance(cycleEnd);
            Assert.AreEqual(OutpostSaveService.SerializeState(sim.State), OutpostSaveService.SerializeState(copy.State), "загрузка не меняет ход цикла");
            Assert.AreEqual(FactoryStatus.Ready, sim.Run.Factories[0].Status);
            Assert.That(sim.Run.Factories[0].ReadyUnits, Is.GreaterThan(0));
            copy = SaveLoadEqual(fs, sim, "Ready");

            // Партия в трубе.
            var delivered = sim.Submit(OutpostCommand.DeliverFactory(f.TargetId));
            Assert.IsTrue(delivered.Ok, delivered.Reason);
            Assert.AreEqual(1, sim.Run.PipeBatches.Count);
            long arrive = sim.Run.PipeBatches[0].ArriveTick;
            Assert.That(arrive, Is.GreaterThan(sim.Now));
            sim.Advance(sim.Now + (arrive - sim.Now) / 2);
            copy = SaveLoadEqual(fs, sim, "партия в трубе");
            Assert.AreEqual(arrive, copy.Run.PipeBatches[0].ArriveTick);

            // После прибытия: склад и продажа; GoodsSoldGold входит в кошелёк.
            sim.Advance(arrive);
            copy.Advance(arrive);
            Assert.That(sim.Run.Warehouse[f.GoodId], Is.GreaterThan(0));
            Assert.IsTrue(sim.Submit(OutpostCommand.SellGoods(null)).Ok);
            Assert.IsTrue(copy.Submit(OutpostCommand.SellGoods(null)).Ok);
            Assert.That(sim.Run.GoodsSoldGold.Sign, Is.GreaterThan(0));
            copy = SaveLoadEqual(fs, sim, "после продажи");
            Assert.AreEqual(sim.Run.GoodsSoldGold, copy.Run.GoodsSoldGold);
            sim.Advance(sim.Now + 30 * Tps);
            copy.Advance(copy.Now + 30 * Tps);
            Assert.AreEqual(OutpostSaveService.SerializeState(sim.State), OutpostSaveService.SerializeState(copy.State));
        }

        // ТЗ 2.2 §10.2, §13: DebugGoods (DEV «Goods ×100») допустим только в debug-профиле: запись и загрузка слота Debug проходят,
        // слот Main такую запись отклоняет, а тот же файл в main при загрузке отвергается (инварианты).
        [Test]
        public void DebugGoods_SavedOnlyInDebugSlot()
        {
            var fs = new MemoryFileSystemA();
            var sim = Zone2WithOpenFactories(out _);
            Assert.That(sim.DebugGrantGoods(100), Is.GreaterThan(0));
            var good = sim.Run.Factories[0].GoodId;
            Assert.AreEqual(100L, sim.Run.DebugGoods[good]);
            Assert.AreEqual(100L, sim.Run.Warehouse[good]);

            var debug = SaveKitA.NewService(fs, debug: true);
            var ok = debug.Save(sim.State);
            Assert.IsTrue(ok.Ok, ok.Error);
            var loaded = SaveKitA.NewService(fs, debug: true).Load();
            Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status, string.Join("; ", loaded.Messages));
            Assert.AreEqual(100L, loaded.State.Run.DebugGoods[good]);

            var main = SaveKitA.NewService(fs).Save(sim.State);
            Assert.IsFalse(main.Ok, "Main не принимает отладочный товар");
            StringAssert.Contains("DebugGoods", main.Error);
            Assert.IsFalse(fs.Files.ContainsKey(SaveKitA.Main));

            fs.Put(SaveKitA.Main, fs.Get(SaveKitA.DebugMain));
            var rejected = SaveKitA.NewService(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Corrupt, rejected.Status, "файл debug с DebugGoods в слоте Main отвергнут");
        }

        // ───────────────────────────── Сохранения прежней схемы (ТЗ 2.2 §10.3, Р9) ─────────────────────────────

        static string Ctx(OutpostLoadResult r) => r.Status + ": " + string.Join("; ", r.Messages);

        /// <summary>Старое состояние шахты 1 записано в main и bak (два commit), затем оба файла переведены в прежнюю схему.</summary>
        static MemoryFileSystemA OldSave(int schema, RunPhase phase = RunPhase.Running, bool debug = false, bool replaceUnsupported = false)
        {
            var fs = new MemoryFileSystemA { ReplaceUnsupported = replaceUnsupported };
            var state = OutdatedSaveKit.StateIn(phase);
            var svc = SaveKitA.NewService(fs, debug);
            Assert.IsTrue(SaveKitA.TrySave(svc, state), "commit 1");
            Assert.IsTrue(SaveKitA.TrySave(svc, state), "commit 2");
            string main = debug ? SaveKitA.DebugMain : SaveKitA.Main;
            foreach (var path in new[] { main, main + ".bak" })
                OutdatedSaveKit.Downgrade(fs, path, schema, OutdatedSaveKit.OldProgress);
            return fs;
        }

        // ТЗ 2.2 §10.3, §16.1: сохранения схемы 2 в Running, DrainingCargo и ReadyToContinue и сохранение схемы 1 — статус Outdated
        // (не Incompatible и не Corrupt, ничего не перезаписано), затем метод ядра: архив pre-factories/, новая кампания схемы 3 с
        // переносом Settings, PlayTimeMs и ключом LegacyImportKey, стартовый набор магазина, запись с resetBackup; без ошибок инвариантов.
        [TestCase(2, RunPhase.Running)]
        [TestCase(2, RunPhase.DrainingCargo)]
        [TestCase(2, RunPhase.ReadyToContinue)]
        [TestCase(1, RunPhase.Running)]
        public void Outdated_LoadReportsOutdated_ResetArchivesAndStartsSchema3Campaign(int schema, RunPhase phase)
        {
            var fs = OldSave(schema, phase);
            var before = Snapshot(fs);
            long oldSeq = (long)SaveKitA.ReadEnvelope(fs, SaveKitA.Main)["CommitSequence"];
            var service = OutdatedSaveKit.Service(fs);

            var r = service.Load();
            Assert.AreEqual(OutpostLoadStatus.Outdated, r.Status, Ctx(r));
            Assert.IsNull(r.State);
            Assert.AreEqual(schema, r.Envelope.SchemaVersion);
            Assert.AreEqual(oldSeq, r.CommitSequence, "старший commit старой схемы");
            Assert.IsNull(r.QuarantineDirectory, "устаревший файл не уходит в карантин");
            Assert.AreEqual(before.Count, fs.Files.Count, "загрузка ничего не создаёт");
            AssertOriginalsUntouched(before, fs);

            var cfg = OutdatedSaveKit.GameConfigs;
            var fresh = service.ResetOutdated(OutpostSaveSlot.Main, Repo, cfg, 4242UL, 9_000_000L, out var saved);
            Assert.IsTrue(saved.Ok, saved.Error);

            // Архив: побайтные копии main и bak.
            CollectionAssert.AreEqual(before[SaveKitA.Main], fs.Get(OutdatedSaveKit.PreFactories + "/state.json"));
            CollectionAssert.AreEqual(before[SaveKitA.Bak], fs.Get(OutdatedSaveKit.PreFactories + "/state.json.bak"));

            // Новая кампания: шахта 1, схема 3, прогресс прежней игры не перенесён.
            var p = fresh.Profile;
            Assert.AreEqual(OutpostIds.SchemaVersion, p.SchemaVersion);
            Assert.AreEqual(1, p.CurrentZone);
            Assert.AreEqual(1, fresh.Run.Zone);
            Assert.AreEqual(4242UL, p.Seed);
            Assert.AreEqual(OutpostIds.RulesVersion, fresh.Run.RulesVersion);
            Assert.AreEqual(OutpostIds.AdapterVersion, fresh.Run.AdapterVersion);
            Assert.IsNotNull(fresh.Run.FactoryRules, "снимок factory_rules");
            Assert.IsEmpty(p.CompletedZones);
            Assert.IsEmpty(p.Items);
            Assert.IsEmpty(p.RewardLog);
            Assert.AreEqual(0L, p.Elixir);
            Assert.AreEqual(BigInteger.Zero, fresh.Run.Wallet);
            Assert.AreEqual(StoreService.CurrentProfileVersion, p.StoreVersion, "профиль магазина обновлён без бесплатных тайм-скипов");
            Assert.AreEqual(cfg.Store.StartGems, p.Gems, "гемы — только стартовые");
            // Перенесено: Settings целиком, PlayTimeMs; карточки прежнего режима не импортируются.
            Assert.AreEqual(OutdatedSaveKit.OldPlayTimeMs, p.PlayTimeMs);
            Assert.AreEqual(0.5f, p.Settings.Master);
            Assert.AreEqual(0.25f, p.Settings.Music);
            Assert.AreEqual(0.75f, p.Settings.Sfx);
            Assert.AreEqual(0.125f, p.Settings.Ambience);
            Assert.IsFalse(p.Settings.Vibration);
            Assert.IsTrue(p.Settings.ReduceEffects);
            Assert.IsTrue(p.Settings.LegacySettingsImported);
            CollectionAssert.Contains(p.CardKeys, CardService.LegacyImportKey);
            Assert.IsNotNull(p.Payments);
            if (schema == 1) Assert.IsEmpty(p.Payments, "в схеме 1 журнала нет");
            Assert.IsNull(p.PendingPurchase);
            var map = OutpostLevelAdapter.ForRun(Repo, fresh.Run);
            CollectionAssert.IsEmpty(OutpostInvariants.Check(fresh, map, cfg.Equipment, cfg.Cards));

            // На диске: схема 3, main = bak (resetBackup), номер commit продолжает старый.
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            Assert.AreEqual(OutpostIds.SchemaVersion, (int)env["SchemaVersion"]);
            Assert.AreEqual(OutpostIds.RulesVersion, (string)env["RulesVersion"]);
            Assert.AreEqual(oldSeq + 1, (long)env["CommitSequence"]);
            Assert.AreEqual(saved.CommitSequence, (long)env["CommitSequence"]);
            CollectionAssert.AreEqual(fs.Get(SaveKitA.Main), fs.Get(SaveKitA.Bak), "старый main не остался в bak");
            var again = OutdatedSaveKit.Service(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Loaded, again.Status, Ctx(again));
            Assert.AreEqual(OutpostSaveService.SerializeState(fresh), OutpostSaveService.SerializeState(again.State));

            // Повторный сброс невозможен: в слоте уже схема 3.
            Assert.Throws<InvalidOperationException>(() => OutdatedSaveKit.Service(fs).ResetOutdated(OutpostSaveSlot.Main, Repo, cfg, 1UL, 0L));
        }

        // ТЗ 2.2 §10.3: прежняя схема распознаётся сразу после разбора и контрольной суммы — раньше RulesVersion, AdapterVersion,
        // карты и инвариантов: старые правила, adapter-1, топология v2, кошелёк вне инварианта и недоступная карта не делают файл
        // Incompatible/Corrupt. Слоты независимы (Debug сбрасывается в debug/pre-factories, Main не трогается).
        [TestCase(false)]
        [TestCase(true)]
        public void Outdated_RecognizedBeforeRulesAdapterMapAndInvariants_SlotsIndependent(bool debug)
        {
            var fs = OldSave(2, debug: debug);
            string main = debug ? SaveKitA.DebugMain : SaveKitA.Main;
            foreach (var path in new[] { main, main + ".bak" })
                OutdatedSaveKit.Rewrite(fs, path, payload =>
                {
                    payload["Run"]["Wallet"] = "12345";
                    payload["Run"]["Zone"] = 999;
                    payload["Run"]["SourceSha256"] = new string('0', 64);
                    payload["Run"]["Targets"] = new JArray();
                });
            var before = Snapshot(fs);
            var slot = debug ? OutpostSaveSlot.Debug : OutpostSaveSlot.Main;
            var service = new OutpostSaveService(SaveKitA.Root, fs)
            {
                Configs = OutdatedSaveKit.GameConfigs,
                MapResolver = s => throw new InvalidOperationException("карта устаревшего сохранения не строится"),
            };
            var r = service.Load(slot);
            Assert.AreEqual(OutpostLoadStatus.Outdated, r.Status, Ctx(r));
            Assert.AreEqual(OutpostLoadStatus.NoSave, service.Load(debug ? OutpostSaveSlot.Main : OutpostSaveSlot.Debug).Status, "другой слот пуст");

            var fresh = service.ResetOutdated(slot, Repo, OutdatedSaveKit.GameConfigs, 77UL, 1_000L, out var saved);
            Assert.IsTrue(saved.Ok, saved.Error);
            string archive = debug ? OutdatedSaveKit.DebugPreFactories : OutdatedSaveKit.PreFactories;
            CollectionAssert.AreEqual(before[main], fs.Get(archive + "/state.json"));
            Assert.IsFalse(fs.Files.Keys.Any(k => k.Contains(debug ? "/main/" : "/debug/")), "другой слот не тронут");
            var loaded = OutdatedSaveKit.Service(fs).Load(slot);
            Assert.AreEqual(OutpostLoadStatus.Loaded, loaded.Status, Ctx(loaded));
            Assert.AreEqual(1, loaded.State.Run.Zone);
            Assert.AreEqual(OutpostSaveService.SerializeState(fresh), OutpostSaveService.SerializeState(loaded.State));
        }

        // ТЗ 2.2 §10.3: «Файл с неверной контрольной суммой или без обязательных платёжных полей схемы 2 по-прежнему Corrupt, если
        // валидного кандидата нет»; повреждённый журнал и payload без профиля — тоже. Сброс такой файл не трогает.
        [TestCase("checksum")]
        [TestCase("no-payments")]
        [TestCase("no-pending-field")]
        [TestCase("bad-ledger")]
        [TestCase("no-profile")]
        public void Outdated_BadChecksumOrLedger_StaysCorrupt_AndResetRefuses(string damage)
        {
            var fs = OldSave(2);
            fs.Remove(SaveKitA.Bak);
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            var payload = SaveKitA.Payload(env);
            var profile = (JObject)payload["Profile"];
            switch (damage)
            {
                case "checksum": profile["Parts"] = 999; break;
                case "no-payments": profile.Remove("Payments"); break;
                case "no-pending-field": profile.Remove("PendingPurchase"); break;
                case "bad-ledger":
                    profile["Payments"] = new JArray(new JObject { ["TokenHash"] = "zz", ["Sku"] = "gems_handful", ["Gems"] = 160, ["Granted"] = true });
                    break;
                case "no-profile": payload.Remove("Profile"); break;
            }
            SaveKitA.SetPayload(env, payload, rehash: damage != "checksum");
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            var before = Snapshot(fs);
            var service = OutdatedSaveKit.Service(fs);
            var r = service.Load();
            Assert.AreEqual(OutpostLoadStatus.Corrupt, r.Status, Ctx(r));
            Assert.IsNull(r.State);
            AssertOriginalsUntouched(before, fs);
            AssertQuarantined(fs, before[SaveKitA.Main], "main");
            Assert.Throws<InvalidOperationException>(() => service.ResetOutdated(OutpostSaveSlot.Main, Repo, OutdatedSaveKit.GameConfigs, 1UL, 0L));
            AssertOriginalsUntouched(before, fs);
            Assert.IsFalse(fs.Files.Keys.Any(k => k.Contains("/pre-factories/")), "архив не создан");
        }

        // Выбор между кандидатами: валидная схема 3 важнее устаревшего файла (сбой после замены main при сбросе); устаревший файл
        // рядом с повреждённым — Outdated; будущая схема важнее устаревшей (Incompatible, ничего не сбрасывается); из двух
        // устаревших берётся старший CommitSequence.
        [Test]
        public void Outdated_CandidateSelection_ValidWins_FutureIsIncompatible_HighestOutdatedSequence()
        {
            // main — схема 3 (маркер 200), bak — схема 2.
            var fs = new MemoryFileSystemA();
            var svc = SaveKitA.NewService(fs);
            Assert.IsTrue(SaveKitA.TrySave(svc, SaveKitA.MakeState(100)));
            Assert.IsTrue(SaveKitA.TrySave(svc, SaveKitA.MakeState(200)));
            OutdatedSaveKit.Downgrade(fs, SaveKitA.Bak, 2);
            var bak = fs.Get(SaveKitA.Bak);
            var r = SaveKitA.NewService(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Loaded, r.Status, Ctx(r));
            Assert.AreEqual(200, SaveKitA.Marker(r.State));
            AssertQuarantined(fs, bak, "устаревший bak рядом с валидным main");

            // main — схема 2, bak — мусор.
            fs = OldSave(2);
            fs.Put(SaveKitA.Bak, "{{{ corrupt");
            r = OutdatedSaveKit.Service(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Outdated, r.Status, Ctx(r));

            // main — будущая схема, bak — схема 2.
            fs = OldSave(2);
            var env = SaveKitA.ReadEnvelope(fs, SaveKitA.Main);
            env["SchemaVersion"] = OutpostIds.SchemaVersion + 1;
            SaveKitA.WriteEnvelope(fs, SaveKitA.Main, env);
            var before = Snapshot(fs);
            var service = OutdatedSaveKit.Service(fs);
            r = service.Load();
            Assert.AreEqual(OutpostLoadStatus.Incompatible, r.Status, Ctx(r));
            Assert.Throws<InvalidOperationException>(() => service.ResetOutdated(OutpostSaveSlot.Main, Repo, OutdatedSaveKit.GameConfigs, 1UL, 0L));
            AssertOriginalsUntouched(before, fs);

            // Оба устаревшие: старший номер — bak.
            fs = OldSave(2);
            byte[] main = fs.Get(SaveKitA.Main), older = fs.Get(SaveKitA.Bak);
            fs.Put(SaveKitA.Main, older);
            fs.Put(SaveKitA.Bak, main);
            r = OutdatedSaveKit.Service(fs).Load();
            Assert.AreEqual(OutpostLoadStatus.Outdated, r.Status, Ctx(r));
            Assert.AreEqual((long)JObject.Parse(Utf8(main))["CommitSequence"], r.CommitSequence);
            Assert.AreEqual(SaveKitA.Bak, MemoryFileSystemA.Norm(r.SourcePath));
        }

        // ТЗ §20 «Аварийная запись» для сброса §10.3: сбой на каждом шаге (архив, tmp, замена, bak) оставляет либо прежнее
        // устаревшее сохранение (повторный сброс проходит), либо новую кампанию; в архиве всегда есть побайтные копии main и bak.
        [TestCase(false)]
        [TestCase(true)]
        public void Outdated_ResetCrashAtEveryStep_KeepsOldSaveOrNewCampaign(bool replaceUnsupported)
        {
            var probe = OldSave(2, replaceUnsupported: replaceUnsupported);
            int start = probe.Steps;
            Assert.IsTrue(OutdatedSaveKit.Service(probe).ResetOutdated(OutpostSaveSlot.Main, Repo, OutdatedSaveKit.GameConfigs, 4242UL, 1_000L, out var probeSaved) != null);
            Assert.IsTrue(probeSaved.Ok, probeSaved.Error);
            int total = probe.Steps - start;
            Assert.That(total, Is.GreaterThanOrEqualTo(4), "архив main и bak, tmp и замена main: " + string.Join("; ", probe.Journal));

            int sawOld = 0, sawNew = 0;
            for (int step = 1; step <= total; step++)
                foreach (bool torn in new[] { false, true })
                {
                    var fs = OldSave(2, replaceUnsupported: replaceUnsupported);
                    byte[] oldMain = fs.Get(SaveKitA.Main), oldBak = fs.Get(SaveKitA.Bak);
                    fs.ArmCrash(step, torn);
                    bool reported = false;
                    try
                    {
                        OutdatedSaveKit.Service(fs).ResetOutdated(OutpostSaveSlot.Main, Repo, OutdatedSaveKit.GameConfigs, 4242UL, 1_000L, out var saved);
                        reported = saved.Ok;
                    }
                    catch (System.IO.IOException)
                    {
                        // сбой при записи архива — до любой замены сохранения
                    }
                    var disk = fs.CrashImage();
                    string ctx = "шаг " + step + "/" + total + (torn ? " (обрыв)" : "") + "; журнал: " + string.Join("; ", fs.Journal);
                    var r = OutdatedSaveKit.Service(disk).Load();
                    Assert.That(r.Status, Is.EqualTo(OutpostLoadStatus.Outdated).Or.EqualTo(OutpostLoadStatus.Loaded).Or.EqualTo(OutpostLoadStatus.LoadedFromBackup), ctx + "; " + Ctx(r));
                    if (reported) Assert.AreNotEqual(OutpostLoadStatus.Outdated, r.Status, "сервис сообщил об успехе: " + ctx);
                    if (r.Status == OutpostLoadStatus.Outdated)
                    {
                        sawOld++;
                        var again = OutdatedSaveKit.Service(disk).ResetOutdated(OutpostSaveSlot.Main, Repo, OutdatedSaveKit.GameConfigs, 4242UL, 1_000L, out var retry);
                        Assert.IsTrue(retry.Ok, ctx + ": " + retry.Error);
                        Assert.AreEqual(1, again.Run.Zone);
                        r = OutdatedSaveKit.Service(disk).Load();
                    }
                    else sawNew++;
                    Assert.That(r.Status, Is.EqualTo(OutpostLoadStatus.Loaded).Or.EqualTo(OutpostLoadStatus.LoadedFromBackup), ctx + "; " + Ctx(r));
                    Assert.AreEqual(1, r.State.Run.Zone, ctx);
                    Assert.AreEqual(4242UL, r.State.Profile.Seed, "новая кампания: " + ctx);
                    var archived = disk.Files.Where(kv => kv.Key.Contains("/pre-factories/")).Select(kv => kv.Value).ToList();
                    Assert.IsTrue(archived.Any(b => b.SequenceEqual(oldMain)), "архив содержит прежний main: " + ctx);
                    Assert.IsTrue(archived.Any(b => b.SequenceEqual(oldBak)), "архив содержит прежний bak: " + ctx);
                }
            Assert.That(sawOld, Is.GreaterThan(0));
            Assert.That(sawNew, Is.GreaterThan(0));
        }
    }

    /// <summary>
    /// Сохранения прежних схем для проверок сброса ТЗ 2.2 §10.3 (OutpostSaveTests, PaymentDeliveryTests): файл текущей схемы
    /// приводится к виду файла схемы 1 или 2 — версии конверта и run прежние, полей заводов нет, контрольная сумма пересчитана.
    /// </summary>
    public static class OutdatedSaveKit
    {
        public const string PreFactories = SaveKitA.Root + "/CatOutpostV2/main/pre-factories";
        public const string DebugPreFactories = SaveKitA.Root + "/CatOutpostV2/debug/pre-factories";
        public const long OldPlayTimeMs = 4_321_000L;

        static OutpostConfigSet s_Game;

        /// <summary>Конфигурации, как у игры: топология wide-branching-mines-3, карточки и заводы из источника.</summary>
        public static OutpostConfigSet GameConfigs => s_Game ?? (s_Game = OutpostConfigSet.LoadDefault().WithCards(OutpostKitA.Repo));

        /// <summary>Сервис слотов с картой из снимка run (подходит и для старой геометрии фикстур, и для новой кампании).</summary>
        public static OutpostSaveService Service(MemoryFileSystemA fs) => new OutpostSaveService(SaveKitA.Root, fs)
        {
            Configs = GameConfigs,
            MapResolver = s => OutpostLevelAdapter.ForRun(OutpostKitA.Repo, s.Run),
        };

        /// <summary>Шахта 1 (фикстуры OutpostKitA) в нужной фазе: Running — 20 с работы; иначе — по событиям до фазы.</summary>
        public static OutpostGameState StateIn(RunPhase phase)
        {
            var state = SaveKitA.MakeState(100);
            var sim = new OutpostSimulation(state, OutpostKitA.Map(1), OutpostKitA.Configs) { RecordEvents = false };
            if (phase == RunPhase.Running) sim.Advance(sim.Now + 20 * OutpostKitA.Tps);
            else
                for (int i = 0; i < 5_000_000 && sim.Run.Phase != phase; i++)
                    if (!sim.AdvanceToNextEvent()) break;
            Assert.AreEqual(phase, sim.Run.Phase, sim.Run.WatchdogDiagnostic);
            return sim.State;
        }

        /// <summary>Прогресс прежней игры (не переносится) и переносимые поля: настройки и сыгранное время.</summary>
        public static void OldProgress(JObject payload)
        {
            var profile = (JObject)payload["Profile"];
            profile["Gems"] = 999;
            profile["Elixir"] = 77;
            profile["CurrentZone"] = 4;
            profile["CompletedZones"] = new JArray(1, 2, 3);
            profile["Items"] = new JObject { ["pickaxe.rare"] = 3 };
            profile["PlayTimeMs"] = OldPlayTimeMs;
            profile["Settings"] = new JObject
            {
                ["Master"] = 0.5, ["Music"] = 0.25, ["Sfx"] = 0.75, ["Ambience"] = 0.125,
                ["Vibration"] = false, ["ReduceEffects"] = true, ["LegacySettingsImported"] = false,
            };
        }

        /// <summary>
        /// Перевести файл в схему schema: конверт (SchemaVersion, RulesVersion outpost-2.1-r1, BalanceVersion outpost-2.1-b4),
        /// профиль, run (правила, adapter-1, профиль b4, топология v2) без полей заводов ТЗ 2.2; схема 1 — без журнала платежей.
        /// </summary>
        public static void Downgrade(MemoryFileSystemA fs, string path, int schema, Action<JObject> edit = null)
        {
            var env = SaveKitA.ReadEnvelope(fs, path);
            var payload = SaveKitA.Payload(env);
            env["SchemaVersion"] = schema;
            env["RulesVersion"] = "outpost-2.1-r1";
            env["BalanceVersion"] = "outpost-2.1-b4";
            var profile = (JObject)payload["Profile"];
            profile["SchemaVersion"] = schema;
            if (schema < 2)
            {
                profile.Remove("Payments");
                profile.Remove("PendingPurchase");
            }
            var run = (JObject)payload["Run"];
            run["RulesVersion"] = "outpost-2.1-r1";
            run["AdapterVersion"] = "adapter-1";
            if (run["Balance"] is JObject balance) balance["Version"] = "outpost-2.1-b4";
            if (run["MapTopology"] is JObject topology) topology["Version"] = "wide-branching-mines-2";
            foreach (var field in new[] { "FactoryRules", "Factories", "PipeBatches", "NextPipeBatchSeq", "Warehouse", "GoodsArrived", "GoodsSold", "GoodsSoldGold", "DebugGoods" })
                run.Remove(field);
            (run["Counters"] as JObject)?.Remove("ManualDeliveries");
            edit?.Invoke(payload);
            SaveKitA.SetPayload(env, payload, rehash: true);
            SaveKitA.WriteEnvelope(fs, path, env);
        }

        /// <summary>Правка payload файла с пересчётом контрольной суммы (версии конверта не меняются).</summary>
        public static void Rewrite(MemoryFileSystemA fs, string path, Action<JObject> edit)
        {
            var env = SaveKitA.ReadEnvelope(fs, path);
            var payload = SaveKitA.Payload(env);
            edit(payload);
            SaveKitA.SetPayload(env, payload, rehash: true);
            SaveKitA.WriteEnvelope(fs, path, env);
        }
    }
}
