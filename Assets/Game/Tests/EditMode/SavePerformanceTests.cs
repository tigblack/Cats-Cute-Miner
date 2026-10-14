using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Замер сохранения (ТЗ 12.2, 15.3): снимок самой тяжёлой зоны (zone93, 240 объектов, 10 котиков у целей)
    /// в память — медиана ≤ 10 мс за 20 повторов; полная запись на диск с Flush(true) — замер без порога;
    /// выделения памяти на одну запись до и после переиспользования буферов. Результат — Docs/Reports/save-perf.json.
    /// Сохранения пишутся во временный каталог, persistentDataPath не используется.
    /// </summary>
    public class SavePerformanceTests
    {
        const int Zone = 93;
        const int Objects = 240;
        const int Runs = 20;
        const int AllocRuns = 10;
        const double SerializeMedianLimitMs = 10.0;
        const string ReportFile = "save-perf.json";
        const long Now = 1_760_000_000_000L;

        static double Median(List<double> values) => Percentile(values, 0.5);

        static double Percentile(List<double> values, double p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return 0;
            double idx = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Каталог отчётов: аргумент -catminersReports (Unity запускается в копии build/ci, Docs туда не копируется)
        /// или Docs/Reports корня проекта (dotnet test, Unity в самом проекте).
        /// </summary>
        static string ReportsDir()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-catminersReports") return args[i + 1];
            return Path.Combine(TestData.ProjectRoot, "Docs", "Reports");
        }

        static string EnvironmentName =>
#if CATMINERS_DOTNET_TESTS
            "dotnet";
#else
            "unity";
#endif

        /// <summary>zone93, 10 котиков от DebugSpawnCat, назначенных на цели переносом, и несколько секунд работы.</summary>
        static GameSession HeavySession(out int working)
        {
            var s = GameSession.NewGame(TestData.Balance, 93UL, SaveSlots.Debug, Now, Zone);
            Assert.AreEqual(Objects, s.Layout.Objects.Count, "zone93 — 240 объектов");
            for (int i = 0; i < 10; i++) Assert.IsTrue(s.DebugSpawnCat(30), "котик " + i);
            working = 0;
            foreach (var cat in s.Mine.Cats.ToArray())
            {
                if (cat.TargetIndex >= 0) continue;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    var o = s.Layout.Objects[ord];
                    if (!o.IsTarget || !s.AcceptsCat(ord, cat.Id)) continue;
                    s.DropCat(cat.Id, s.Layout.Col(o.Cells[0]) + 0.5, s.Layout.Row(o.Cells[0]) + 0.5);
                    var after = s.FindCat(cat.Id);
                    if (after != null && after.TargetIndex >= 0)
                    {
                        working++;
                        break;
                    }
                }
            }
            s.Advance(5 * GameSession.TicksPerSecond);
            return s;
        }

        static long AllocatedPerRun(Action action, int runs)
        {
            action();   // прогрев: JIT, кэши контрактов Newtonsoft, рост буферов
            action();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < runs; i++) action();
            return (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
        }

        [Test]
        public void Zone93SnapshotSerializationMedianWithin10Ms()
        {
            var s = HeavySession(out int working);
            var state = s.State;
            Assert.AreEqual(10, s.Mine.Cats.Count, "10 котиков");
            Assert.Greater(working, 0, "котики работают у целей");

            string dir = Path.Combine(Path.GetTempPath(), "catminers-saveperf-" + Guid.NewGuid().ToString("N"));
            string legacyDir = dir + "-legacy";
            try
            {
                var memory = new SaveService(dir);
                // Прогрев: JIT и кэши контрактов сериализатора.
                for (int i = 0; i < 3; i++)
                {
                    SaveService.SerializeState(state);
                    memory.BuildEnvelope(state, Now);
                }

                // Снимок в память: payload + SHA-256 + конверт (BuildEnvelope включает SerializeState с проверкой NaN/∞).
                var serialize = new List<double>(Runs);
                var payloadOnly = new List<double>(Runs);
                int payloadBytes = 0, envelopeBytes = 0;
                for (int i = 0; i < Runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    string payload = SaveService.SerializeState(state);
                    payloadOnly.Add(sw.Elapsed.TotalMilliseconds);
                    payloadBytes = new UTF8Encoding(false).GetByteCount(payload);

                    sw.Restart();
                    byte[] env = memory.BuildEnvelope(state, Now + i);
                    serialize.Add(sw.Elapsed.TotalMilliseconds);
                    envelopeBytes = env.Length;
                }

                // Полная запись на диск: новый SaveService на каждый повтор, RealFileSystem, Flush(true).
                var disk = new List<double>(Runs);
                for (int i = 0; i < Runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    new SaveService(dir).Save(state, Now + i);
                    disk.Add(sw.Elapsed.TotalMilliseconds);
                }

                // Выделения на одно автосохранение тем же экземпляром (ТЗ 15.3): прежняя запись против текущей.
                var saver = new SaveService(dir);
                long counter = 0;
                long allocAfter = AllocatedPerRun(() => saver.Save(state, Now + ++counter), AllocRuns);
                long allocBefore = AllocatedPerRun(() => LegacySaveFormat.Save(legacyDir, state, Now + ++counter), AllocRuns);
                long envelopeAllocAfter = AllocatedPerRun(() => memory.BuildEnvelope(state, Now), AllocRuns);
                long envelopeAllocBefore = AllocatedPerRun(() => LegacySaveFormat.Envelope(state, Now), AllocRuns);

                var loaded = new SaveService(dir).Load(SaveSlots.Debug, TestData.Balance.Sha256);
                Assert.AreEqual(LoadStatus.Loaded, loaded.Status, "записанный снимок загружается: " + loaded.Message);
                Assert.AreEqual(SaveService.SerializeState(state), SaveService.SerializeState(loaded.State));

                double serializeMedian = Median(serialize);
                var json = new StringBuilder();
                json.Append("{\n");
                json.Append("  \"serializeMedianMs\": ").Append(F(serializeMedian)).Append(",\n");
                json.Append("  \"serializeP95Ms\": ").Append(F(Percentile(serialize, 0.95))).Append(",\n");
                json.Append("  \"payloadOnlyMedianMs\": ").Append(F(Median(payloadOnly))).Append(",\n");
                json.Append("  \"diskMedianMs\": ").Append(F(Median(disk))).Append(",\n");
                json.Append("  \"diskP95Ms\": ").Append(F(Percentile(disk, 0.95))).Append(",\n");
                json.Append("  \"payloadBytes\": ").Append(payloadBytes.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"envelopeBytes\": ").Append(envelopeBytes.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"saveAllocBytesBefore\": ").Append(allocBefore.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"saveAllocBytesAfter\": ").Append(allocAfter.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"envelopeAllocBytesBefore\": ").Append(envelopeAllocBefore.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"envelopeAllocBytesAfter\": ").Append(envelopeAllocAfter.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"allocMeasured\": ").Append(allocBefore > 0 ? "true" : "false").Append(",\n");
                json.Append("  \"zone\": ").Append(Zone.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"objects\": ").Append(s.Layout.Objects.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"cats\": ").Append(s.Mine.Cats.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"workingCats\": ").Append(working.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"runs\": ").Append(Runs.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                json.Append("  \"serializeMedianLimitMs\": ").Append(F(SerializeMedianLimitMs)).Append(",\n");
                json.Append("  \"environment\": \"").Append(EnvironmentName).Append("\",\n");
                json.Append("  \"measuredAtUtc\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append("\"\n");
                json.Append("}\n");
                string reports = ReportsDir();
                Directory.CreateDirectory(reports);
                string report = Path.Combine(reports, ReportFile);
                File.WriteAllText(report, json.ToString(), new UTF8Encoding(false));
                TestContext.WriteLine(json.ToString());

                Assert.LessOrEqual(serializeMedian, SerializeMedianLimitMs, "ТЗ 12.2: медиана сериализации снимка zone93 в память");
                // Счётчик выделений есть не в каждом рантайме: 0 — замер недоступен, сравнение пропускается (см. allocMeasured).
                if (allocBefore > 0)
                    Assert.Less(allocAfter, allocBefore / 2, "переиспользование буферов: выделений на запись меньше вдвое");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(legacyDir)) Directory.Delete(legacyDir, true);
            }
        }
    }
}
