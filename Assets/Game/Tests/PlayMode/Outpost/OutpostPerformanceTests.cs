using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CatMiners.Tests
{
    /// <summary>Explicit Editor stress evidence for TZ §19; no Android or fair-balance claim.</summary>
    public sealed class OutpostPerformanceTests
    {
        const int Width = 1080, Height = 1920, DebugCoinMultiplier = 100000;
        /// <summary>Шахта замера заводов (ТЗ 2.2 §15): 15 заводов.</summary>
        const int FactoryZone = 140, FactoryZoneFactories = 15;
        const double WarmupSeconds = 2, MinimumSampleSeconds = 5, MaximumSampleSeconds = 30;
        string m_Directory, m_PreviousSaveDirectory;
        IClock m_PreviousClock;
        IFileSystem m_PreviousFiles;
        bool m_PreviousLegacy, m_PreviousAudioPause, m_PreviousEffects, m_PreviousOverlay;
        float m_PreviousAspect;
        int m_PreviousFrameRate, m_PreviousVsync;
        JObject m_Report;
        byte[] m_MainBytes;
        double m_SceneLoadMs;
        OutpostController Game => GameBootstrap.OutpostController;
        string Reports => Path.Combine(PlayModeUtil.ReportsDir(), "outpost");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_PreviousSaveDirectory = OutpostController.SaveDirectoryOverride;
            m_PreviousClock = OutpostController.ClockOverride;
            m_PreviousFiles = OutpostController.FileSystemOverride;
            m_PreviousLegacy = GameBootstrap.LegacyMode;
            m_PreviousAspect = CameraRig.OverrideAspect;
            m_PreviousAudioPause = AudioListener.pause;
            m_PreviousEffects = VisualSettings.ReduceEffects;
            m_PreviousOverlay = OutpostDebugOverlay.Enabled;
            m_PreviousFrameRate = Application.targetFrameRate;
            m_PreviousVsync = QualitySettings.vSyncCount;
            m_Directory = Path.Combine(Application.temporaryCachePath, "OutpostPerformance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            OutpostController.SaveDirectoryOverride = m_Directory;
            OutpostController.ClockOverride = new SystemClock();
            OutpostController.FileSystemOverride = null;
            GameBootstrap.LegacyMode = false;
            CameraRig.OverrideAspect = 9f / 16f;
            AudioListener.pause = false;
            var timer = Stopwatch.StartNew();
            yield return SceneManager.LoadSceneAsync("CoreGame", LoadSceneMode.Single);
            yield return null;
            yield return null;
            timer.Stop();
            m_SceneLoadMs = timer.Elapsed.TotalMilliseconds;
            Assert.That(Game, Is.Not.Null);
            Assert.That(Game.Clock, Is.TypeOf<SystemClock>());
            yield return WaitPlaying();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                var cleanup = SceneManager.CreateScene("OutpostPerformanceCleanup-" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                var scene = SceneManager.GetSceneByName("CoreGame");
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
            finally
            {
                OutpostController.SaveDirectoryOverride = m_PreviousSaveDirectory;
                OutpostController.ClockOverride = m_PreviousClock;
                OutpostController.FileSystemOverride = m_PreviousFiles;
                GameBootstrap.LegacyMode = m_PreviousLegacy;
                CameraRig.OverrideAspect = m_PreviousAspect;
                AudioListener.pause = m_PreviousAudioPause;
                VisualSettings.ReduceEffects = m_PreviousEffects;
                OutpostDebugOverlay.Enabled = m_PreviousOverlay;
                Application.targetFrameRate = m_PreviousFrameRate;
                QualitySettings.vSyncCount = m_PreviousVsync;
                if (m_Directory != null && Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
            }
        }

        IEnumerator WaitPlaying()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 60;
            while (Game.Phase != OutpostControllerPhase.Playing && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Assert.That(Game.SaveFailed, Is.False, Game.ErrorText);
                yield return null;
            }
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing), Game.ErrorText);
            Assert.That(Game.SaveFailed, Is.False, Game.ErrorText);
            Game.DismissOffline();
        }

        /// <summary>
        /// ТЗ 2.2 §15: к шахтам 50, 101, 144 добавлена шахта 140 — 15 заводов, все открыты (DEV «Unlock factories»), на автомате
        /// (DEV «Auto all»), трубы и капсулы в пути (DEV «Finish factory cycles» перед замером). Бюджетных проверок нет: медиану и
        /// p95 шахты 140 сравнивают с базой шахты 144 (performance-editor-pre-factories.json) по отчёту.
        /// </summary>
        [UnityTest, Timeout(900000), Explicit("Editor stress measurements and real renders; select OutpostPerformanceTests explicitly.")]
        public IEnumerator EditorStress_Zones50_101_140_144_OfflineAutosaveAndPortraitRenders()
        {
            Assert.That(Application.isEditor, Is.True, "This report is specifically an Editor benchmark.");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                "Real screenshots require a graphics device; do not run with -nographics.");
            m_Report = NewReport();
            try
            {
                Game.SwitchProfile(true);
                yield return WaitPlaying();
                Assert.That(Game.IsDebugProfile, Is.True);
                var saves = new OutpostSaveService(m_Directory);
                m_MainBytes = File.ReadAllBytes(saves.MainPath(OutpostSaveSlot.Main));
                OutpostGameState offlineSource = null;
                OutpostMap offlineMap = null;
                foreach (int zone in new[] { 50, 101, FactoryZone, 144 })
                {
                    var zoneReport = new JObject { ["zone"] = zone };
                    ((JArray)m_Report["zones"]).Add(zoneReport);
                    var timer = Stopwatch.StartNew();
                    Game.LoadZone(zone);
                    timer.Stop();
                    zoneReport["loadZoneCommandMs"] = timer.Elapsed.TotalMilliseconds;
                    yield return WaitPlaying();
                    Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
                    Assert.That(Game.IsDebugProfile, Is.True);
                    // Debug-only money scale, BEFORE any frame/delivery. No invented cargo, worker,
                    // wallet or accounting counters. GrantTestKit rebuilds Sim and its derived stats.
                    Game.State.Run.Balance.CoinBase *= new Rational(DebugCoinMultiplier);
                    Game.GrantTestKit();
                    Game.SimSpeed = 1;
                    if (zone == FactoryZone) yield return OpenAndAutomateFactories(zoneReport);
                    double preparationStart = Time.realtimeSinceStartupAsDouble;
                    double deadline = preparationStart + 120;
                    while (Game.State.Run.Workers.Count < 12 && Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        yield return WaitPlaying();
                        if (Game.Quote(UpgradeBranch.Team, BuyMode.One).CanBuy)
                            Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True, Game.ErrorText);
                        yield return null;
                    }
                    Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(12), "Debug delivered wallet must fund ten ordinary Team ×1 purchases.");
                    Assert.That(Game.State.Run.Counters.DeliveredUnits, Is.GreaterThan(0));
                    Assert.That(Game.SimSpeed, Is.EqualTo(1));
                    // Strong legal pickaxes create spills and exercise ground-pile rendering.
                    // The first delivery's Debug-only value also funds these normal ×1 purchases.
                    while (Game.Quote(UpgradeBranch.Pickaxes, BuyMode.One).CanBuy)
                        Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True, Game.ErrorText);
                    Check(Game.State, Game.Map);
                    zoneReport["preparationSeconds"] = Time.realtimeSinceStartupAsDouble - preparationStart;
                    zoneReport["mapCells"] = Game.Map.CellCount;
                    zoneReport["mapTargets"] = Game.Map.Targets.Length;
                    zoneReport["coinBase"] = Game.State.Run.Balance.CoinBase.ToString();
                    zoneReport["equippedDebugKit"] = JArray.FromObject(Game.State.Profile.Equipped);
                    if (zone == FactoryZone)
                    {
                        // Партии всех автоматических заводов уходят в трубы: капсулы в пути входят в замер (ТЗ 2.2 §15).
                        Game.DebugFinishFactoryCycles();
                        yield return WaitPlaying();
                        zoneReport["pipeBatchesAfterFinish"] = Game.State.Run.PipeBatches.Count;
                    }
                    Game.Hud.ShowBase();
                    yield return PlayModeUtil.WaitReal((float)WarmupSeconds);
                    if (zone == FactoryZone)
                    {
                        zoneReport["pipeBatchesAtSampleStart"] = Game.State.Run.PipeBatches.Count;
                        zoneReport["capsulesShownAtSampleStart"] = Game.Board.Pipes.ActiveCapsules;
                        zoneReport["capsulePool"] = PipeNetworkView.CapsulePool;
                    }
                    yield return Measure(zoneReport);
                    if (zone == FactoryZone) zoneReport["pipeBatchesAtSampleEnd"] = Game.State.Run.PipeBatches.Count;
                    Check(Game.State, Game.Map);
                    zoneReport["invariantsPassed"] = true;
                    if (zone == 144)
                    {
                        offlineSource = OutpostJson.DeepCopy(Game.State);
                        offlineMap = Game.Map;
                    }
                    // Rendering/readback/PNG encoding are outside the frame sample window.
                    yield return Capture(zone, true, zoneReport);
                    yield return WaitPlaying();
                    Game.Hud.CloseAllWindows();
                    yield return Capture(zone, false, zoneReport);
                    yield return WaitPlaying();
                    AssertMainUnchanged(saves);
                }
                MeasureOfflineAndPersistence(offlineSource, offlineMap);
                AssertMainUnchanged(saves);
                m_Report["mainUnchanged"] = true;
                m_Report["completed"] = true;
            }
            finally
            {
                Directory.CreateDirectory(Reports);
                File.WriteAllText(Path.Combine(Reports, "performance-editor.json"), m_Report.ToString());
            }
        }

        /// <summary>ТЗ 2.2 §15: DEV «Unlock factories» и «Auto all» — все заводы шахты открыты и на автомате.</summary>
        IEnumerator OpenAndAutomateFactories(JObject zoneReport)
        {
            Game.DebugUnlockFactories();
            yield return WaitPlaying();
            Game.DebugAutomateAll();
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            var factories = Game.State.Run.Factories;
            int open = 0, automated = 0;
            foreach (var factory in factories)
            {
                if (factory.Status != FactoryStatus.Locked) open++;
                if (factory.Automated) automated++;
            }
            Assert.That(factories.Count, Is.EqualTo(FactoryZoneFactories), "ТЗ 2.2 §15: в шахте 140 — 15 заводов");
            Assert.That(open, Is.EqualTo(factories.Count), "все заводы открыты");
            Assert.That(automated, Is.EqualTo(factories.Count), "все заводы на автомате");
            Assert.That(Game.Board.Pipes, Is.Not.Null);
            zoneReport["factories"] = factories.Count;
            zoneReport["factoriesOpen"] = open;
            zoneReport["factoriesAutomated"] = automated;
        }

        JObject NewReport()
        {
            return new JObject
            {
                ["schemaVersion"] = 1, ["completed"] = false, ["utc"] = DateTime.UtcNow.ToString("O"),
                ["environment"] = new JObject
                {
                    ["unityVersion"] = Application.unityVersion, ["platform"] = Application.platform.ToString(),
                    ["buildType"] = "Editor PlayMode", ["debugBuild"] = UnityEngine.Debug.isDebugBuild,
                    ["deviceModel"] = SystemInfo.deviceModel, ["deviceType"] = SystemInfo.deviceType.ToString(),
                    ["os"] = SystemInfo.operatingSystem, ["cpu"] = SystemInfo.processorType,
                    ["cpuCores"] = SystemInfo.processorCount, ["cpuMHz"] = SystemInfo.processorFrequency,
                    ["ramMB"] = SystemInfo.systemMemorySize, ["gpu"] = SystemInfo.graphicsDeviceName,
                    ["gpuVendor"] = SystemInfo.graphicsDeviceVendor, ["gpuMemoryMB"] = SystemInfo.graphicsMemorySize,
                    ["graphicsBackend"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["graphicsVersion"] = SystemInfo.graphicsDeviceVersion, ["scriptingBackend"] = ScriptingBackend(),
                    ["screenWidth"] = Screen.width, ["screenHeight"] = Screen.height,
                    ["displayResolution"] = Screen.currentResolution.ToString(),
                    ["captureWidth"] = Width, ["captureHeight"] = Height,
                    ["overrideAspect"] = 9d / 16d, ["targetFrameRate"] = Application.targetFrameRate,
                    ["vsyncCount"] = QualitySettings.vSyncCount, ["qualityLevel"] = QualitySettings.GetQualityLevel()
                },
                ["method"] = new JObject
                {
                    ["clock"] = "SystemClock; ordinary Update; SimSpeed=1",
                    ["profile"] = "Isolated temporary Debug profile; LoadZone + GrantTestKit; Team purchases via Game.Buy(Team, One).",
                    ["debugCoinBaseMultiplier"] = DebugCoinMultiplier,
                    ["funding"] = "Debug run CoinBase multiplied before delivery; wallet funded by actual cargo deposits. Not fair balance evidence.",
                    ["pickaxes"] = "Ordinary Pickaxes ×1 purchases to cap after Team reaches 12, so ground cargo is part of the measured load.",
                    ["warmupSecondsPerZone"] = WarmupSeconds, ["minimumSampleSeconds"] = MinimumSampleSeconds,
                    ["maximumSampleSeconds"] = MaximumSampleSeconds,
                    ["frameTime"] = "Stopwatch intervals between consecutive coroutine frame resumes, includes Editor/test runner/pacing/normal autosaves; not isolated CPU/GPU time.",
                    ["percentiles"] = "Median averages the two middle values; p95 is nearest rank ceil(0.95*N).",
                    ["gc"] = "GC Allocated In Frame recorder is aggregate Editor frame allocation, including harness/editor activity; NOT isolated game allocation. GC.CollectionCount and GetTotalMemory(false) are process-wide; heap delta is live memory change, NOT allocated bytes. No forced GC.",
                    ["sampleEnd"] = "After >=5 seconds with observed ground cargo and mining/transport/two simultaneous unloading slots; otherwise stop at 30 seconds and fail coverage.",
                    ["loadZoneCommand"] = "Synchronous map creation, debug save/validation and scene view rebuild combined; initial scene load also includes two settling frames.",
                    ["androidVerified"] = false,
                    ["limitations"] = "Editor only; observed peak batches are not proof of theoretical maximum visual batches. Frame resolution is reported separately from 1080x1920 captures. No Android FPS or isolated 1KB/game-frame GC claim. Single-run timings, no budget assertions."
                },
                ["initialSceneLoadMs"] = m_SceneLoadMs, ["zones"] = new JArray()
            };
        }

        static string ScriptingBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return "Unknown (backend define unavailable)";
#endif
        }

        struct FrameSample
        {
            public double Ms;
            public long EditorAllocatedBytes;
            public int Workers, Batches, Queue, Mining, Transport, Unloading, OccupiedSlots;
            public bool Playing, BaseOpen;
        }

        IEnumerator Measure(JObject report)
        {
            var samples = new List<FrameSample>(16384);
            var beforeObjects = ObjectCounts();
            long commitBefore = DebugCommitSequence();
            var collectionBefore = Collections();
            long heapBefore = GC.GetTotalMemory(false);
            int deliveredBefore = Game.State.Run.Counters.DeliveredUnits;
            long tickBefore = Game.Sim.Now;
            bool concurrent = false, groundCargo = false;
            var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            bool recorderValid = recorder.Valid;
            double seconds = 0;
            long previous = Stopwatch.GetTimestamp(), start = previous;
            try
            {
                do
                {
                    yield return null;
                    long now = Stopwatch.GetTimestamp();
                    var run = Game.State.Run;
                    var sample = new FrameSample
                    {
                        Ms = (now - previous) * 1000d / Stopwatch.Frequency,
                        EditorAllocatedBytes = recorderValid && recorder.Count > 0 ? recorder.LastValue : -1,
                        Workers = run.Workers.Count, Batches = run.Batches.Count, Queue = run.Queue.Count,
                        Playing = Game.Phase == OutpostControllerPhase.Playing, BaseOpen = Game.Hud.BaseOpen
                    };
                    previous = now;
                    for (int i = 0; i < run.Workers.Count; i++)
                    {
                        var worker = run.Workers[i];
                        if (worker.Mode == WorkerMode.Mining) sample.Mining++;
                        if (worker.Bag.Count > 0 && worker.Mode != WorkerMode.Unloading) sample.Transport++;
                        if (worker.Mode == WorkerMode.Unloading) sample.Unloading++;
                    }
                    for (int i = 0; i < run.Slots.Length; i++)
                        if (run.Slots[i].WorkerId != 0) sample.OccupiedSlots++;
                    concurrent |= sample.Mining > 0 && sample.Transport > 0 && sample.Unloading == 2;
                    groundCargo |= sample.Batches > 0;
                    samples.Add(sample);
                    seconds = (now - start) / (double)Stopwatch.Frequency;
                } while (seconds < MinimumSampleSeconds || ((!concurrent || !groundCargo) && seconds < MaximumSampleSeconds));
            }
            finally { recorder.Dispose(); }
            long heapAfter = GC.GetTotalMemory(false);
            var collectionAfter = Collections();
            long commitAfter = DebugCommitSequence();
            var times = new double[samples.Count];
            var editorBytes = new List<double>(samples.Count);
            int maxBatches = 0, maxQueue = 0, maxMining = 0, maxTransport = 0, maxUnloading = 0, maxSlots = 0;
            int invalidFrames = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                times[i] = s.Ms;
                if (s.EditorAllocatedBytes >= 0) editorBytes.Add(s.EditorAllocatedBytes);
                maxBatches = Math.Max(maxBatches, s.Batches); maxQueue = Math.Max(maxQueue, s.Queue);
                maxMining = Math.Max(maxMining, s.Mining); maxTransport = Math.Max(maxTransport, s.Transport);
                maxUnloading = Math.Max(maxUnloading, s.Unloading); maxSlots = Math.Max(maxSlots, s.OccupiedSlots);
                if (!s.Playing || !s.BaseOpen || s.Workers != 12) invalidFrames++;
            }
            report["sampleSeconds"] = seconds;
            report["frameCount"] = samples.Count;
            report["frameWallTimeMs"] = Distribution(times);
            report["gc"] = new JObject
            {
                ["editorRecorderAvailable"] = recorderValid, ["editorRecorderSampleCount"] = editorBytes.Count,
                ["editorAggregateFrameAllocatedBytes"] = Distribution(editorBytes.ToArray()),
                ["processCollectionDeltaGen0"] = collectionAfter[0] - collectionBefore[0],
                ["processCollectionDeltaGen1"] = collectionAfter[1] - collectionBefore[1],
                ["processCollectionDeltaGen2"] = collectionAfter[2] - collectionBefore[2],
                ["processManagedLiveBytesBefore"] = heapBefore, ["processManagedLiveBytesAfter"] = heapAfter,
                ["processManagedLiveBytesDelta"] = heapAfter - heapBefore
            };
            report["objectsBefore"] = beforeObjects; report["objectsAfter"] = ObjectCounts();
            report["observedLoad"] = new JObject
            {
                ["workers"] = Game.State.Run.Workers.Count, ["maxBatches"] = maxBatches,
                ["maxDepotQueue"] = maxQueue, ["maxMiningWorkers"] = maxMining,
                ["maxTransportWorkers"] = maxTransport, ["maxUnloadingWorkers"] = maxUnloading,
                ["maxOccupiedDepotSlots"] = maxSlots, ["concurrentMiningTransportTwoUnloads"] = concurrent,
                ["groundCargoObserved"] = groundCargo,
                ["coverageShortfall"] = concurrent && groundCargo ? "" : "Ground cargo or simultaneous mining/transport/two unloads was not observed within 30 seconds.",
                ["deliveredUnitsDuringSample"] = Game.State.Run.Counters.DeliveredUnits - deliveredBefore,
                ["simulationTicksAdvanced"] = Game.Sim.Now - tickBefore, ["invalidStressFrames"] = invalidFrames,
                ["debugCommitSequenceBefore"] = commitBefore, ["debugCommitSequenceAfter"] = commitAfter,
                ["automaticCommitsDuringSample"] = commitAfter - commitBefore,
                ["automaticCommitNote"] = "Ordinary two-second autosaves; a stage-completion commit can also increment this sequence."
            };
            Assert.That(samples.Count, Is.GreaterThan(0));
            Assert.That(seconds, Is.GreaterThanOrEqualTo(MinimumSampleSeconds));
            Assert.That(invalidFrames, Is.Zero, "Sample requires ordinary Playing Update, 12 workers and open base throughout.");
            Assert.That(Game.Sim.Now, Is.GreaterThan(tickBefore), "Open base must not pause simulation.");
            Assert.That(commitAfter, Is.GreaterThan(commitBefore), "Ordinary Update must persist the run during the sample.");
            Assert.That(concurrent && groundCargo, Is.True, "Full stress coverage includes ground cargo and both unloading slots.");
        }

        long DebugCommitSequence()
        {
            string path = new OutpostSaveService(m_Directory).MainPath(OutpostSaveSlot.Debug);
            return (long)JObject.Parse(File.ReadAllText(path))["CommitSequence"];
        }

        static int[] Collections() => new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };

        static JObject Distribution(double[] values)
        {
            if (values.Length == 0) return new JObject { ["available"] = false };
            Array.Sort(values);
            double total = 0;
            foreach (double value in values) total += value;
            int middle = values.Length / 2;
            return new JObject
            {
                ["available"] = true, ["count"] = values.Length,
                ["median"] = values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle],
                ["p95"] = values[(int)Math.Ceiling(values.Length * 0.95) - 1],
                ["max"] = values[values.Length - 1], ["total"] = total
            };
        }

        JObject ObjectCounts() => new JObject
        {
            ["loadedSceneGameObjectsIncludingInactive"] = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
            ["activeCatViews"] = Game.Board.GetComponentsInChildren<OutpostCatView>(false).Length,
            ["pooledAndActiveCatViews"] = Game.Board.GetComponentsInChildren<OutpostCatView>(true).Length,
            ["activeCargoPileViews"] = Game.Board.GetComponentsInChildren<CargoPileView>(false).Length,
            ["pooledAndActiveCargoPileViews"] = Game.Board.GetComponentsInChildren<CargoPileView>(true).Length,
            ["logicalBatches"] = Game.State.Run.Batches.Count, ["depotQueue"] = Game.State.Run.Queue.Count
        };

        void MeasureOfflineAndPersistence(OutpostGameState source, OutpostMap map)
        {
            Assert.That(source.Run.Zone, Is.EqualTo(144));
            Assert.That(source.Run.Workers.Count, Is.EqualTo(12));
            Assert.That(source.Run.Phase, Is.EqualTo(RunPhase.Running), "Offline stress must start on an active mine.");
            Check(source, map);
            // An active simulation between scheduled events: measurement excludes Editor and view work.
            var probe = new OutpostSimulation(OutpostJson.DeepCopy(source), map, Game.Configs) { RecordEvents = false };
            long startTick = probe.Now;
            int probeFrames = (int)Math.Min(200, Math.Max(0, probe.NextEventTick() - startTick - 1));
            probe.Advance(startTick);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 1; frame <= probeFrames; frame++) probe.Advance(startTick + frame);
            long simBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            m_Report["simulationBetweenEvents"] = new JObject
            {
                ["frames"] = probeFrames, ["allocatedBytes"] = simBytes,
                ["scope"] = "Active core Advance in 1us steps before the next event; no Editor, views, navigation or autosave included."
            };
            Assert.That(probeFrames, Is.GreaterThan(0));
            Assert.That(simBytes, Is.Zero, "Core frames between scheduled events must not allocate.");
            string original = OutpostSaveService.SerializeState(source);
            const long absenceMs = 24L * 60 * 60 * 1000;
            var timer = Stopwatch.StartNew();
            var job = new OfflineSimulator(map, Game.Configs).Begin(source, checked(source.Run.LastProcessedUtcMs + absenceMs));
            job.RunToEnd();
            timer.Stop();
            double offlineMs = timer.Elapsed.TotalMilliseconds;
            m_Report["offline"] = new JObject
            {
                ["zone"] = 144, ["workers"] = 12, ["requestedMs"] = absenceMs,
                ["elapsedMs"] = job.Summary.ElapsedMs, ["appliedMs"] = job.Summary.AppliedMs,
                ["wallTimeMsIncludingBeginCopy"] = offlineMs, ["withinEditor3000MsBudget"] = offlineMs <= 3000,
                ["completedStage"] = job.Summary.StageCompleted,
                ["deliveredUnits"] = job.Summary.UnitsDelivered, ["goldDelivered"] = job.Summary.GoldDelivered.ToString(),
                ["sourceUnchanged"] = original == OutpostSaveService.SerializeState(source)
            };
            Assert.That(job.IsDone, Is.True);
            Assert.That(job.Summary.AppliedMs, Is.EqualTo(3L * 60 * 60 * 1000));
            Assert.That(OutpostSaveService.SerializeState(source), Is.EqualTo(original));
            Check(job.State, map);
            m_Report["offline"]["invariantsPassed"] = true;

            // Same durable service as the controller's two-second autosave; separate root and Debug slot.
            // Measure an active online snapshot, not the potentially empty completed offline result.
            var service = new OutpostSaveService(Path.Combine(m_Directory, "autosave-benchmark")) { Configs = Game.Configs };
            var saveTimes = new double[3];
            for (int i = 0; i < saveTimes.Length; i++)
            {
                timer.Restart();
                var saved = service.Save(source, OutpostSaveSlot.Debug, map);
                timer.Stop();
                saveTimes[i] = timer.Elapsed.TotalMilliseconds;
                Assert.That(saved.Ok, Is.True, saved.Error);
            }
            var reader = new OutpostSaveService(Path.Combine(m_Directory, "autosave-benchmark"))
            {
                Configs = Game.Configs,
                MapResolver = state => OutpostLevelAdapter.ForRun(Game.Balance, state.Run)
            };
            timer.Restart();
            var loaded = reader.Load(OutpostSaveSlot.Debug);
            timer.Stop();
            double loadMs = timer.Elapsed.TotalMilliseconds;
            Assert.That(loaded.Status, Is.EqualTo(OutpostLoadStatus.Loaded), string.Join("; ", loaded.Messages));
            Assert.That(OutpostSaveService.SerializeState(loaded.State), Is.EqualTo(original));
            timer.Restart();
            var rebuiltMap = OutpostLevelAdapter.ForRun(Game.Balance, loaded.State.Run);
            timer.Stop();
            double mapMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var rebuiltSim = new OutpostSimulation(loaded.State, rebuiltMap, Game.Configs);
            timer.Stop();
            m_Report["persistence"] = new JObject
            {
                ["method"] = "Three durable saves of active zone144 snapshot via OutpostSaveService, separate temp root/Debug slot; load validates main+backup with map resolver. Normal controller autosaves remain in frame samples.",
                ["autosaveWallTimeMs"] = Distribution(saveTimes),
                ["saveBytes"] = new FileInfo(service.MainPath(OutpostSaveSlot.Debug)).Length,
                ["loadWithMapValidationMs"] = loadMs, ["mapBuildMs"] = mapMs,
                ["simulationReconstructionMs"] = timer.Elapsed.TotalMilliseconds, ["roundTripUnchanged"] = true
            };
            Check(rebuiltSim.State, rebuiltMap);
        }

        void Check(OutpostGameState state, OutpostMap map)
        {
            Assert.That(OutpostInvariants.Check(state, map, Game.Configs.Equipment), Is.Empty);
            Assert.That(state.Run.WatchdogFired, Is.False, state.Run.WatchdogDiagnostic);
        }

        void AssertMainUnchanged(OutpostSaveService service) =>
            Assert.That(File.ReadAllBytes(service.MainPath(OutpostSaveSlot.Main)), Is.EqualTo(m_MainBytes));

        IEnumerator Capture(int zone, bool baseOpen, JObject report)
        {
            Assert.That(Game.Hud.BaseOpen, Is.EqualTo(baseOpen));
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(Width, Height, 24) { filterMode = FilterMode.Point };
            Texture2D pixels = null;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            bool previousScaler = scaler.enabled;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return PlayModeUtil.WaitReal(0.35f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                string name = "stress-zone" + zone + (baseOpen ? "-base-open.png" : "-base-closed.png");
                string directory = Path.Combine(Reports, "screenshots");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
                if (report["screenshots"] == null) report["screenshots"] = new JArray();
                ((JArray)report["screenshots"]).Add("screenshots/" + name);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                scaler.enabled = previousScaler;
                if (pixels != null) UnityEngine.Object.Destroy(pixels);
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
