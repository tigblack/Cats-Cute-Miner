using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CatMiners.Tests
{
    /// <summary>
    /// Замер производительности (ТЗ 15.3): ранняя zone5 (кристальная пещера, котики уровня 6, только ×1 — на ×100 они
    /// расчистили бы шахту за замер) и поздние zone93 и zone117 (10 работающих котиков уровня 30, ×1 и ×100), прокрутка
    /// всей карты, рендер 1080×1920, Quality = Mobile. Время кадра — без ограничения частоты, чтобы видеть стоимость кадра.
    /// Выделения памяти по ТЗ 15.3 — отдельный прогон каждой зоны в установившемся режиме ×1 без панелей и прокрутки,
    /// 600 кадров в темпе 60 FPS (в Editor темп держит тест, см. PaceInTest): GC.GetAllocatedBytesForCurrentThread главного потока после каждого кадра
    /// (цель — не больше 1 КБ на кадр всего), симуляция отдельно (цель — 0 байт на кадр). Это приёмочное чтение.
    /// Счётчик сначала проверяется пробой: выделяется блок 4096 байт, разница показаний должна его увидеть. Если не
    /// видит (в Mono с Boehm GC счётчик может быть заглушкой и всегда давать 0), приёмка пишется как «не измерено»
    /// (null), а не как «выполнено». Независимая проверка с собственной пробой: ProfilerRecorder «GC Allocated In Frame»
    /// в отдельных 600 кадрах того же режима и число вызовов GC.Alloc главного потока за шаги симуляции.
    /// ProfilerRecorder в прогонах с прокруткой — вторичное чтение.
    /// Результат — Docs/Reports/perf.json с отметками simAllocPass/totalAllocPass. Порогов тест не проверяет: это замер.
    /// </summary>
    public class PerformanceTests
    {
        const int Frames = 600;
        const int SettleFrames = 30;
        /// <summary>Частота кадров игры (GameBootstrap): от неё зависит доля кадров с автосохранением раз в 2 с.</summary>
        const int SteadyFrameRate = 60;
        /// <summary>
        /// Как держится темп SteadyFrameRate в установившемся режиме. В Editor ожидание по Application.targetFrameRate
        /// крутит циклы самого редактора (EditorApplication.update, тест-раннер), и их выделения на главном потоке попадают
        /// в «GC Allocated In Frame»: при targetFrameRate = 60 рекордер показывал ~78 КБ на кадр, а те же кадры без
        /// ограничения — ~0,5 КБ. На устройстве плеер в ожидании спит. Поэтому в Editor темп держит сам тест: кадр
        /// дотягивается до 1/60 с занятым ожиданием по Stopwatch (без выделений), targetFrameRate снимается.
        /// Время кадра и доля кадров с автосохранением остаются как при 60 FPS.
        /// </summary>
        static bool PaceInTest => Application.isEditor;
        static string Pacing => PaceInTest ? "editor-spin" : "targetFrameRate";
        const double SimTargetBytesPerFrame = 0;
        const double TotalTargetBytesPerFrame = 1024;
        /// <summary>Проба счётчиков: блок известного размера, который счётчик обязан увидеть целиком.</summary>
        const int ProbeBytes = 4096;
        /// <summary>
        /// Порог кадра автосохранения для ProfilerRecorder (в его проходе нет обращений к диску, отметку файла не читаем):
        /// кадр с автосохранением даёт десятки КБ (zone93 — около 45 КБ), обычный кадр — сотни байт.
        /// </summary>
        const double AutosaveSpikeBytes = 20_000;
        string m_SaveDir;
        LogWatcher m_Log;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            CameraRig.OverrideAspect = 0f;
            Application.targetFrameRate = 60;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        sealed class Run
        {
            public string Name;
            public double MedianMs, P95Ms, MaxMs, AvgFps;
            public double GcBytesPerFrameAvg, GcBytesPerFrameMax, GcBytesPerFrameMedian, GcBytesPerFrameAvgWithoutSaves;
            public int GcFramesWithAlloc;
        }

        /// <summary>
        /// Установившийся режим ×1 (ТЗ 15.3): выделения главного потока за кадр (приёмка) и ProfilerRecorder (проверка).
        /// Отметки null — «не измерено»: проба показала, что счётчик не видит выделений.
        /// </summary>
        sealed class SteadyRun
        {
            public int Zone, Frames, AutosaveFrames, PanelFrames, WorkingCats;
            public double Seconds, AvgFps;
            public long CounterProbeBytes;
            public bool CounterValid => CounterProbeBytes >= ProbeBytes;
            public double Avg, Median, Max, AvgNoSave, MedianNoSave, MaxNoSave;
            public bool? Pass => CounterValid ? Avg <= TotalTargetBytesPerFrame : (bool?)null;
            public bool? PassNoSave => CounterValid ? AvgNoSave <= TotalTargetBytesPerFrame : (bool?)null;
            /// <summary>Наибольшее показание ProfilerRecorder в кадре пробы; −1 — рекордер недоступен.</summary>
            public double RecorderProbeBytes = -1;
            public bool RecorderValid => RecorderProbeBytes >= ProbeBytes;
            public int RecorderFrames, RecorderAutosaveFrames;
            public double RecAvg, RecMedian, RecMax, RecAvgNoSave, RecMedianNoSave, RecMaxNoSave;
            public bool? RecPass => RecorderValid ? RecAvg <= TotalTargetBytesPerFrame : (bool?)null;
            public bool? RecPassNoSave => RecorderValid ? RecAvgNoSave <= TotalTargetBytesPerFrame : (bool?)null;
        }

        /// <summary>Выделения симуляции за 600 шагов: GC.GetAllocatedBytesForCurrentThread и число вызовов GC.Alloc, у каждого своя проба.</summary>
        sealed class SimAlloc
        {
            public long CounterProbeBytes;
            public bool CounterValid => CounterProbeBytes >= ProbeBytes;
            public double BytesPerFrame;
            /// <summary>Вызовов GC.Alloc при выделении пробы; −1 — сэмплер недоступен.</summary>
            public int SamplerProbe = -1;
            public bool SamplerValid => SamplerProbe >= 1;
            public int Samples = -1;
        }

        /// <summary>
        /// Проба GC.GetAllocatedBytesForCurrentThread: разница показаний вокруг выделения блока ProbeBytes.
        /// Меньше блока — счётчик в этом рантайме не работает, его нули ничего не доказывают.
        /// </summary>
        static long ProbeAllocCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var block = new byte[ProbeBytes];
            long after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(block);
            return after - before;
        }

        static void AllocateProbe() => GC.KeepAlive(new byte[ProbeBytes]);

        /// <summary>
        /// Число вызовов GC.Alloc главного потока за действие — Recorder «GC.Alloc», как Is.Not.AllocatingGCMemory()
        /// в Unity Test Framework. Не зависит от GC.GetAllocatedBytesForCurrentThread. −1 — сэмплер недоступен
        /// (действие всё равно выполняется).
        /// </summary>
        static int CountGcAllocSamples(Action action)
        {
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            if (recorder == null || !recorder.isValid)
            {
                action();
                return -1;
            }
            // Выключение сбрасывает накопленное (в том числе создание самого Recorder); дальше — только текущий поток.
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }

        /// <summary>
        /// Выделения симуляции за Frames шагов после 120 шагов прогрева. Оба чтения — в одном проходе: показания счётчика
        /// читаются внутри окна сэмплера, само чтение не выделяет. Перед проходом — пробы обоих инструментов.
        /// </summary>
        static SimAlloc MeasureSim(GameSession sim, long step)
        {
            for (int i = 0; i < 120; i++) { sim.Advance(step); sim.Events.Clear(); }
            var r = new SimAlloc { CounterProbeBytes = ProbeAllocCounter(), SamplerProbe = CountGcAllocSamples(AllocateProbe) };
            long before = 0, after = 0;
            r.Samples = CountGcAllocSamples(() =>
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < Frames; i++) { sim.Advance(step); sim.Events.Clear(); }
                after = GC.GetAllocatedBytesForCurrentThread();
            });
            r.BytesPerFrame = (after - before) / (double)Frames;
            return r;
        }

        static void Summarize(List<double> values, out double avg, out double median, out double max)
        {
            avg = Average(values);
            max = MaxOf(values);
            values.Sort();
            median = Percentile(values, 0.5);
        }

        static double Average(List<double> values)
        {
            if (values.Count == 0) return 0;
            double sum = 0;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        static double MaxOf(List<double> values)
        {
            double max = 0;
            foreach (var v in values) if (v > max) max = v;
            return max;
        }

        /// <summary>Последняя запись в каталоге сохранений: изменилась за кадр — в кадре было автосохранение.</summary>
        static long LastSaveWriteTicks(string dir)
        {
            long max = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) max = Math.Max(max, File.GetLastWriteTimeUtc(f).Ticks);
            }
            catch (IOException) { }
            return max;
        }

        /// <summary>Закрыть панель чертежей и модальные окна (сундуки и т. п.) кнопками, как игрок.</summary>
        static IEnumerator CloseHudPanels(GameController gc)
        {
            if (gc.Hud.BlueprintsOpen) gc.Hud.ToggleBlueprints();
            for (int guard = 0; guard < 50 && gc.Hud.ModalOpen; guard++)
            {
                foreach (var b in gc.Hud.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                {
                    if (b.gameObject.name != "btn") continue;
                    PlayModeUtil.Click(b);
                    break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Установившийся режим ×1 без панелей и прокрутки (ТЗ 15.3): камера один раз наводится на котиков, 30 кадров
        /// на успокоение, затем два прохода по 600 кадров.
        /// 1. Приёмка: после каждого кадра — GC.GetAllocatedBytesForCurrentThread главного потока; перед проходом — проба
        ///    счётчика. Выделения самой проверки (метка файла сохранения) исключаются повторным чтением счётчика.
        /// 2. Проверка: ProfilerRecorder «GC Allocated In Frame» за каждый кадр, без обращений к диску (рекордер видит весь
        ///    кадр, в том числе выделения теста); перед проходом — кадр с блоком ProbeBytes, рекордер обязан его увидеть.
        /// </summary>
        IEnumerator MeasureSteady(GameController gc, ProfilerRecorder gcRecorder, int zone, int working, List<SteadyRun> result)
        {
            var s = gc.Session;
            gc.SimSpeed = 1;
            Application.targetFrameRate = PaceInTest ? -1 : SteadyFrameRate;
            yield return CloseHudPanels(gc);
            double rows = 0;
            foreach (var c in s.Mine.Cats) rows += s.Layout.Row(c.Cell);
            float row = s.Mine.Cats.Count > 0 ? (float)(rows / s.Mine.Cats.Count) : s.Layout.Depth / 2f;
            gc.Rig.FocusOn(s.Layout.Depth - row - 0.5f);
            var pace = Stopwatch.StartNew();
            long paced = 0;
            for (int i = 0; i < SettleFrames; i++)
            {
                Pace(pace, ref paced);
                yield return null;
            }

            var run = new SteadyRun { Zone = zone, Frames = Frames, WorkingCats = working };

            // 1. Приёмочное чтение ТЗ 15.3.
            run.CounterProbeBytes = ProbeAllocCounter();
            var all = new List<double>(Frames);
            var noSave = new List<double>(Frames);
            long lastSave = LastSaveWriteTicks(m_SaveDir);
            var sw = Stopwatch.StartNew();
            long prev = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Frames; i++)
            {
                Pace(pace, ref paced);
                yield return null;
                double bytes = GC.GetAllocatedBytesForCurrentThread() - prev;
                long save = LastSaveWriteTicks(m_SaveDir);
                bool saved = save != lastSave;
                lastSave = save;
                if (gc.Hud.ModalOpen || gc.Hud.BlueprintsOpen) run.PanelFrames++;
                all.Add(bytes);
                if (saved) run.AutosaveFrames++;
                else noSave.Add(bytes);
                prev = GC.GetAllocatedBytesForCurrentThread();
            }
            run.Seconds = sw.Elapsed.TotalSeconds;
            run.AvgFps = run.Seconds > 0 ? Frames / run.Seconds : 0;
            Summarize(all, out run.Avg, out run.Median, out run.Max);
            Summarize(noSave, out run.AvgNoSave, out run.MedianNoSave, out run.MaxNoSave);

            // 2. Проверка ProfilerRecorder. LastValue — последний завершённый кадр: блок пробы виден в одном из двух следующих.
            if (gcRecorder.Valid)
            {
                Pace(pace, ref paced);
                yield return null;
                AllocateProbe();
                double probe = 0;
                for (int i = 0; i < 2; i++)
                {
                    Pace(pace, ref paced);
                    yield return null;
                    probe = Math.Max(probe, gcRecorder.LastValue);
                }
                run.RecorderProbeBytes = probe;
                var rec = new List<double>(Frames);
                var recNoSave = new List<double>(Frames);
                for (int i = 0; i < Frames; i++)
                {
                    Pace(pace, ref paced);
                    yield return null;
                    double b = gcRecorder.LastValue;
                    if (gc.Hud.ModalOpen || gc.Hud.BlueprintsOpen) run.PanelFrames++;
                    rec.Add(b);
                    if (b >= AutosaveSpikeBytes) run.RecorderAutosaveFrames++;
                    else recNoSave.Add(b);
                }
                run.RecorderFrames = rec.Count;
                Summarize(rec, out run.RecAvg, out run.RecMedian, out run.RecMax);
                Summarize(recNoSave, out run.RecAvgNoSave, out run.RecMedianNoSave, out run.RecMaxNoSave);
            }
            Application.targetFrameRate = -1;
            result.Add(run);
        }

        /// <summary>
        /// Темп SteadyFrameRate в Editor (см. PaceInTest): занятое ожидание до срока очередного кадра по общему секундомеру.
        /// Если кадр сам дольше срока, отставание не догоняется пачкой коротких кадров — отсчёт сдвигается.
        /// </summary>
        static void Pace(Stopwatch clock, ref long frame)
        {
            if (!PaceInTest) return;
            frame++;
            long due = frame * Stopwatch.Frequency / SteadyFrameRate;
            long now = clock.ElapsedTicks;
            if (now >= due)
            {
                frame = now * SteadyFrameRate / Stopwatch.Frequency;
                return;
            }
            while (clock.ElapsedTicks < due) { }
        }

        static string Json(SteadyRun r) =>
            "    {\"zone\": " + r.Zone + ", \"speed\": 1, \"frames\": " + r.Frames + ", \"settleFrames\": " + SettleFrames +
            ", \"targetFrameRate\": " + SteadyFrameRate + ", \"pacing\": \"" + Pacing + "\", \"seconds\": " + F(r.Seconds) + ", \"avgFps\": " + F(r.AvgFps) +
            ", \"workingCats\": " + r.WorkingCats + ", \"panelFrames\": " + r.PanelFrames + ", \"autosaveFrames\": " + r.AutosaveFrames +
            ", \"allocCounterProbeBytes\": " + r.CounterProbeBytes + ", \"allocCounterValid\": " + B(r.CounterValid) +
            ", \"totalAllocBytesPerFrameAvg\": " + N(r.Avg, r.CounterValid) + ", \"totalAllocBytesPerFrameMedian\": " + N(r.Median, r.CounterValid) +
            ", \"totalAllocBytesPerFrameMax\": " + N(r.Max, r.CounterValid) +
            ", \"totalAllocBytesPerFrameAvgWithoutAutosave\": " + N(r.AvgNoSave, r.CounterValid) +
            ", \"totalAllocBytesPerFrameMedianWithoutAutosave\": " + N(r.MedianNoSave, r.CounterValid) +
            ", \"totalAllocBytesPerFrameMaxWithoutAutosave\": " + N(r.MaxNoSave, r.CounterValid) +
            ", \"totalAllocPass\": " + B(r.Pass) + ", \"totalAllocPassWithoutAutosave\": " + B(r.PassNoSave) +
            ", \"recorderProbeBytes\": " + N(r.RecorderProbeBytes, r.RecorderProbeBytes >= 0) + ", \"recorderValid\": " + B(r.RecorderValid) +
            ", \"recorderFrames\": " + r.RecorderFrames + ", \"recorderAutosaveFrames\": " + r.RecorderAutosaveFrames +
            ", \"recorderAllocBytesPerFrameAvg\": " + N(r.RecAvg, r.RecorderValid) + ", \"recorderAllocBytesPerFrameMedian\": " + N(r.RecMedian, r.RecorderValid) +
            ", \"recorderAllocBytesPerFrameMax\": " + N(r.RecMax, r.RecorderValid) +
            ", \"recorderAllocBytesPerFrameAvgWithoutAutosave\": " + N(r.RecAvgNoSave, r.RecorderValid) +
            ", \"recorderAllocBytesPerFrameMedianWithoutAutosave\": " + N(r.RecMedianNoSave, r.RecorderValid) +
            ", \"recorderAllocBytesPerFrameMaxWithoutAutosave\": " + N(r.RecMaxNoSave, r.RecorderValid) +
            ", \"recorderAllocPass\": " + B(r.RecPass) + ", \"recorderAllocPassWithoutAutosave\": " + B(r.RecPassNoSave) + "}";

        static string B(bool v) => v ? "true" : "false";

        /// <summary>Отметка, которой может не быть: null — «не измерено».</summary>
        static string B(bool? v) => v.HasValue ? B(v.Value) : "null";

        /// <summary>Значение только из годного чтения; иначе null — «не измерено», а не ноль.</summary>
        static string N(double v, bool valid) => valid ? F(v) : "null";

        static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            double idx = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        [UnityTest]
        public IEnumerator EarlyAndLateZoneFrameTimes()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var json = new StringBuilder();
            var runs = new List<string>();

            // Загрузка баланса: SHA-256 + разбор JSON + индексы. Первый прогон — прогрев.
            var bytes = boot.BalanceJson.bytes;
            BalanceRepository.Load(bytes);
            var loadTimes = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                BalanceRepository.Load(bytes);
                loadTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
            loadTimes.Sort();

            int mobile = Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            // Рендер в 1080×1920 вместе с HUD.
            var cam = gc.Rig.Cam;
            var canvas = gc.Hud.GetComponent<Canvas>();
            var rt = new RenderTexture(1080, 1920, 24);
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;

            var steadyRuns = new List<SteadyRun>();
            using (var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                foreach (int zone in new[] { 5, 93, 117 })
                {
                    bool early = zone < 20;
                    var swBuild = Stopwatch.StartNew();
                    gc.SwitchSlot(CatMiners.Core.Sim.SaveSlots.Debug, true, zone);
                    double buildMs = swBuild.Elapsed.TotalMilliseconds;
                    var s = gc.Session;
                    for (int i = 0; i < 10; i++) s.DebugSpawnCat(early ? 6 : 30);
                    int working = PlayModeUtil.AssignIdleCats(gc);
                    gc.Hud.Toast("", 0f);
                    yield return PlayModeUtil.WaitReal(1f);

                    foreach (double speed in early ? new[] { 1.0 } : new[] { 1.0, 100.0 })
                    {
                        gc.SimSpeed = speed;
                        for (int i = 0; i < 30; i++) yield return null;
                        var times = new List<double>(Frames);
                        var gcBytes = new List<double>(Frames);
                        double gcSum = 0, gcMax = 0, gcNoSaveSum = 0;
                        int noSaveFrames = 0;
                        int gcFrames = 0;
                        int depth = s.Layout.Depth;
                        for (int i = 0; i < Frames; i++)
                        {
                            // Прокрутка всей карты снизу вверх и обратно за время замера.
                            float phase = i / (float)(Frames - 1);
                            float tri = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
                            gc.Rig.FocusOn(tri * depth);
                            yield return null;
                            times.Add(Time.unscaledDeltaTime * 1000.0);
                            if (gcRecorder.Valid)
                            {
                                double b = gcRecorder.LastValue;
                                gcSum += b;
                                gcBytes.Add(b);
                                // Кадры автосохранения (раз в несколько секунд) дают пики в десятки КБ — считаем и без них.
                                if (b < AutosaveSpikeBytes) { gcNoSaveSum += b; noSaveFrames++; }
                                if (b > gcMax) gcMax = b;
                                if (b > 0) gcFrames++;
                            }
                        }
                        times.Sort();
                        gcBytes.Sort();
                        double sum = 0;
                        foreach (var t in times) sum += t;
                        var run = new Run
                        {
                            Name = "zone" + zone + " x" + speed,
                            MedianMs = Percentile(times, 0.5),
                            P95Ms = Percentile(times, 0.95),
                            MaxMs = times[times.Count - 1],
                            AvgFps = 1000.0 * times.Count / sum,
                            GcBytesPerFrameAvg = gcSum / Frames,
                            GcBytesPerFrameMax = gcMax,
                            GcFramesWithAlloc = gcFrames,
                            GcBytesPerFrameMedian = Percentile(gcBytes, 0.5),
                            GcBytesPerFrameAvgWithoutSaves = noSaveFrames > 0 ? gcNoSaveSum / noSaveFrames : 0,
                        };
                        runs.Add("    {\"zone\": " + zone + ", \"speed\": " + F(speed) + ", \"objects\": " + s.Layout.Objects.Count +
                                 ", \"cats\": " + s.Mine.Cats.Count + ", \"workingCats\": " + working + ", \"zoneBuildMs\": " + F(buildMs) +
                                 ", \"frames\": " + Frames + ", \"medianMs\": " + F(run.MedianMs) + ", \"p95Ms\": " + F(run.P95Ms) +
                                 ", \"maxMs\": " + F(run.MaxMs) + ", \"avgFps\": " + F(run.AvgFps) +
                                 ", \"gcBytesPerFrameAvg\": " + F(run.GcBytesPerFrameAvg) + ", \"gcBytesPerFrameMax\": " + F(run.GcBytesPerFrameMax) +
                                 ", \"gcBytesPerFrameMedian\": " + F(run.GcBytesPerFrameMedian) + ", \"gcBytesPerFrameAvgWithoutSaves\": " + F(run.GcBytesPerFrameAvgWithoutSaves) +
                                 ", \"gcFramesWithAlloc\": " + run.GcFramesWithAlloc + ", \"gcRecorderValid\": " + (gcRecorder.Valid ? "true" : "false") + "}");
                    }
                    // Приёмочный замер выделений ТЗ 15.3: ×1, без панелей и прокрутки; проверка тем же рекордером.
                    yield return MeasureSteady(gc, gcRecorder, zone, working, steadyRuns);
                    gc.SimSpeed = 1;
                }
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cam.targetTexture = null;
            rt.Release();

            // Выделения памяти самой симуляции: шаг кадра ×1 и ×100 в установившемся режиме, 600 шагов.
            // Перед каждым замером — пробы счётчика и сэмплера GC.Alloc.
            var sim = gc.Session;
            var simX1 = MeasureSim(sim, GameSession.SecondsToTicks(1.0 / 60.0));
            var simX100 = MeasureSim(sim, GameSession.SecondsToTicks(100.0 / 60.0));

            // Цели ТЗ 15.3 и итог: худшая зона установившегося режима. Чтение, чья проба не прошла, — null («не измерено»).
            bool steadyCounterValid = steadyRuns.Count > 0, recorderValid = steadyRuns.Count > 0;
            long counterProbe = Math.Min(simX1.CounterProbeBytes, simX100.CounterProbeBytes);
            double worstAvg = 0, worstMedian = 0, worstMax = 0, worstAvgNoSave = 0, worstMedianNoSave = 0, worstMaxNoSave = 0;
            double recAvg = 0, recMedian = 0, recMax = 0, recAvgNoSave = 0, recMedianNoSave = 0, recMaxNoSave = 0;
            int autosaveFrames = 0, recorderAutosaveFrames = 0, panelFrames = 0;
            bool totalPass = true, totalPassNoSave = true, recPass = true, recPassNoSave = true;
            foreach (var r in steadyRuns)
            {
                steadyCounterValid &= r.CounterValid;
                recorderValid &= r.RecorderValid;
                counterProbe = Math.Min(counterProbe, r.CounterProbeBytes);
                worstAvg = Math.Max(worstAvg, r.Avg);
                worstMedian = Math.Max(worstMedian, r.Median);
                worstMax = Math.Max(worstMax, r.Max);
                worstAvgNoSave = Math.Max(worstAvgNoSave, r.AvgNoSave);
                worstMedianNoSave = Math.Max(worstMedianNoSave, r.MedianNoSave);
                worstMaxNoSave = Math.Max(worstMaxNoSave, r.MaxNoSave);
                recAvg = Math.Max(recAvg, r.RecAvg);
                recMedian = Math.Max(recMedian, r.RecMedian);
                recMax = Math.Max(recMax, r.RecMax);
                recAvgNoSave = Math.Max(recAvgNoSave, r.RecAvgNoSave);
                recMedianNoSave = Math.Max(recMedianNoSave, r.RecMedianNoSave);
                recMaxNoSave = Math.Max(recMaxNoSave, r.RecMaxNoSave);
                autosaveFrames += r.AutosaveFrames;
                recorderAutosaveFrames += r.RecorderAutosaveFrames;
                panelFrames += r.PanelFrames;
                totalPass &= r.Avg <= TotalTargetBytesPerFrame;
                totalPassNoSave &= r.AvgNoSave <= TotalTargetBytesPerFrame;
                recPass &= r.RecAvg <= TotalTargetBytesPerFrame;
                recPassNoSave &= r.RecAvgNoSave <= TotalTargetBytesPerFrame;
            }
            bool counterValid = steadyCounterValid && simX1.CounterValid && simX100.CounterValid;

            json.Append("{\n");
            json.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            json.Append("  \"platform\": \"").Append(Application.platform).Append(Application.isBatchMode ? " (batchmode)" : "").Append("\",\n");
            json.Append("  \"deviceModel\": \"").Append(Escape(SystemInfo.deviceModel)).Append("\",\n");
            json.Append("  \"cpu\": \"").Append(Escape(SystemInfo.processorType)).Append(" x").Append(SystemInfo.processorCount).Append("\",\n");
            json.Append("  \"gpu\": \"").Append(Escape(SystemInfo.graphicsDeviceName)).Append(" (").Append(SystemInfo.graphicsDeviceType).Append(")\",\n");
            json.Append("  \"ramMb\": ").Append(SystemInfo.systemMemorySize).Append(",\n");
            json.Append("  \"quality\": \"").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append("\",\n");
            json.Append("  \"render\": \"1080x1920 RenderTexture, HUD ScreenSpaceCamera, targetFrameRate=-1, vSync=0\",\n");
            json.Append("  \"balanceLoadMs\": ").Append(F(loadTimes[loadTimes.Count / 2])).Append(",\n");
            // Пробы инструментов: без них нули счётчика не отличить от неработающего счётчика.
            json.Append("  \"allocProbeBytes\": ").Append(ProbeBytes).Append(",\n");
            json.Append("  \"allocCounterProbeBytes\": ").Append(counterProbe).Append(",\n");
            json.Append("  \"allocCounterValid\": ").Append(B(counterValid)).Append(",\n");
            json.Append("  \"gcAllocSamplerProbe\": ").Append(Math.Min(simX1.SamplerProbe, simX100.SamplerProbe)).Append(",\n");
            json.Append("  \"gcAllocSamplerValid\": ").Append(B(simX1.SamplerValid && simX100.SamplerValid)).Append(",\n");
            json.Append("  \"recorderValid\": ").Append(B(recorderValid)).Append(",\n");
            json.Append("  \"simAllocBytesPerFrameX1\": ").Append(N(simX1.BytesPerFrame, simX1.CounterValid)).Append(",\n");
            json.Append("  \"simAllocBytesPerFrameX100\": ").Append(N(simX100.BytesPerFrame, simX100.CounterValid)).Append(",\n");
            json.Append("  \"simAllocSteps\": ").Append(Frames).Append(",\n");
            json.Append("  \"simAllocSamplesX1\": ").Append(N(simX1.Samples, simX1.SamplerValid)).Append(",\n");
            json.Append("  \"simAllocSamplesX100\": ").Append(N(simX100.Samples, simX100.SamplerValid)).Append(",\n");
            json.Append("  \"simTargetBytesPerFrame\": ").Append(F(SimTargetBytesPerFrame)).Append(",\n");
            json.Append("  \"totalTargetBytesPerFrame\": ").Append(F(TotalTargetBytesPerFrame)).Append(",\n");
            json.Append("  \"simAllocPass\": ").Append(B(simX1.CounterValid ? simX1.BytesPerFrame <= SimTargetBytesPerFrame : (bool?)null)).Append(",\n");
            json.Append("  \"simAllocPassBySamples\": ").Append(B(simX1.SamplerValid ? simX1.Samples == 0 : (bool?)null)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameAvg\": ").Append(N(worstAvg, steadyCounterValid)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameMedian\": ").Append(N(worstMedian, steadyCounterValid)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameMax\": ").Append(N(worstMax, steadyCounterValid)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameAvgWithoutAutosave\": ").Append(N(worstAvgNoSave, steadyCounterValid)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameMedianWithoutAutosave\": ").Append(N(worstMedianNoSave, steadyCounterValid)).Append(",\n");
            json.Append("  \"totalAllocBytesPerFrameMaxWithoutAutosave\": ").Append(N(worstMaxNoSave, steadyCounterValid)).Append(",\n");
            json.Append("  \"autosaveFrames\": ").Append(autosaveFrames).Append(",\n");
            json.Append("  \"panelFrames\": ").Append(panelFrames).Append(",\n");
            json.Append("  \"totalAllocPass\": ").Append(B(steadyCounterValid ? totalPass : (bool?)null)).Append(",\n");
            json.Append("  \"totalAllocPassWithoutAutosave\": ").Append(B(steadyCounterValid ? totalPassNoSave : (bool?)null)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameAvg\": ").Append(N(recAvg, recorderValid)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameMedian\": ").Append(N(recMedian, recorderValid)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameMax\": ").Append(N(recMax, recorderValid)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameAvgWithoutAutosave\": ").Append(N(recAvgNoSave, recorderValid)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameMedianWithoutAutosave\": ").Append(N(recMedianNoSave, recorderValid)).Append(",\n");
            json.Append("  \"recorderAllocBytesPerFrameMaxWithoutAutosave\": ").Append(N(recMaxNoSave, recorderValid)).Append(",\n");
            json.Append("  \"recorderAutosaveFrames\": ").Append(recorderAutosaveFrames).Append(",\n");
            json.Append("  \"recorderAllocPass\": ").Append(B(recorderValid ? recPass : (bool?)null)).Append(",\n");
            json.Append("  \"recorderAllocPassWithoutAutosave\": ").Append(B(recorderValid ? recPassNoSave : (bool?)null)).Append(",\n");
            string acceptance = steadyCounterValid
                ? "totalAllocBytesPerFrameAvg: GC.GetAllocatedBytesForCurrentThread главного потока (проба " + ProbeBytes + " байт видна), установившийся режим x1 без панелей и прокрутки, " +
                  Frames + " кадров в темпе " + SteadyFrameRate + " FPS (" + Pacing + "), все кадры, включая автосохранение; худшая зона (steadyRuns)"
                : "не измерено: GC.GetAllocatedBytesForCurrentThread не видит пробу " + ProbeBytes + " байт (разница показаний " + counterProbe +
                  " байт), в этом рантайме счётчик не работает; независимая проверка — recorderAllocBytesPerFrame* и simAllocSamplesX1";
            json.Append("  \"acceptanceReading\": \"").Append(acceptance).Append("\",\n");
            json.Append("  \"crossCheckReading\": \"recorderAllocBytesPerFrame*: ProfilerRecorder GC Allocated In Frame, отдельные ").Append(Frames)
                .Append(" кадров x1 в темпе ").Append(SteadyFrameRate).Append(" FPS (").Append(Pacing).Append(") без панелей, прокрутки и обращений к диску, кадры от ").Append(F(AutosaveSpikeBytes))
                .Append(" байт считаются автосохранением; simAllocSamples*: вызовы GC.Alloc главного потока (Recorder GC.Alloc) за ").Append(Frames).Append(" шагов симуляции\",\n");
            json.Append("  \"secondaryReading\": \"runs[].gcBytesPerFrame*: ProfilerRecorder GC Allocated In Frame, прокрутка карты, x1 и x100\",\n");
            json.Append("  \"steadyRuns\": [\n").Append(string.Join(",\n", steadyRuns.ConvertAll(Json))).Append("\n  ],\n");
            json.Append("  \"runs\": [\n").Append(string.Join(",\n", runs)).Append("\n  ]\n}\n");
            string dir = PlayModeUtil.ReportsDir();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "perf.json"), json.ToString());
            TestContext.WriteLine(json.ToString());

            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
