using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CatMiners.Tests
{
    /// <summary>
    /// Обстановка шахт в игре (Docs/Rework/MINE_DRESSING.md): глубокая проверка 7 шахт выборки — 1, 5, 30, 70, 110, 120, 140 (самые
    /// плотные и глубокие планы). В каждой: вид построен по карте (постоянные препятствия без выбора и удара, мусор — цели), трубы
    /// идут по стенам, котики добывают, носят и разгружают без простоя при доступной работе, наблюдатель не срабатывает, кадры
    /// замеряются на плотной сцене. Снимки и отчёт — Docs/Reports/dressing/. Не проверка всей кампании.
    /// Запуск: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter OutpostDressingEvidenceTests</c>.
    /// </summary>
    [Explicit("обстановка шахт: 7 шахт выборки в игре, снимки Docs/Reports/dressing/*.png")]
    public sealed class OutpostDressingEvidenceTests : OutpostCardsFixture
    {
        static readonly int[] Deep = { 1, 5, 30, 70, 110, 120, 140 };
        static readonly int[] ShotZones = { 30, 110, 140 };
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "dressing");

        [UnityTest, Timeout(1800000)]
        public IEnumerator DeepSample_RuntimeChecks_FrameTimes_Shots()
        {
            Directory.CreateDirectory(Shots);
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            var report = new JArray();
            foreach (int zone in Deep)
            {
                Game.LoadZone(zone);
                yield return null;
                yield return WaitPlaying();
                Game.Hud.CloseAllWindows();
                var map = Game.Map;
                Assert.That(map.Number, Is.EqualTo(zone));
                Assert.That(map.Dressing, Is.Not.Null, "шахта " + zone + " с обстановкой");
                var row = new JObject { ["zone"] = zone };

                // Вид: постоянные препятствия — без коллайдера и выбора; мусор — цели с видом кучи.
                int floorProps = map.Props.Count(p => !p.Wall);
                Assert.That(Game.Board.PropViewCount, Is.EqualTo(floorProps));
                int debris = 0;
                foreach (var t in map.Targets)
                {
                    if (!t.IsDebris) continue;
                    debris++;
                    var view = Game.Board.TargetViewOf(t.Id);
                    Assert.That(view, Is.Not.Null);
                    Assert.That(view.IsDebris, Is.True, "куча " + t.Id);
                }
                foreach (var prop in map.Props.Where(p => !p.Wall))
                    foreach (int c in prop.Cells)
                    {
                        Assert.That(Game.Board.WorldToCell(Game.Board.CellCenter(c % map.Width, c / map.Width)), Is.EqualTo(-1),
                            "клетка препятствия не клетка навигации");
                        Assert.That(Game.Sim.Navigation.Passable(c), Is.False);
                    }
                row["debris"] = debris;
                row["props"] = floorProps;
                row["wallProps"] = map.Props.Length - floorProps;

                // Трубы по стенам: вид построен по маршрутам PipeMountRoutes, клетки маршрута не на воротах и не на площадке.
                var layout = Game.Board.Pipes?.Layout;
                if (map.Factories.Length > 0)
                {
                    Assert.That(layout, Is.Not.Null);
                    Assert.That(layout.Pipes.Count, Is.EqualTo(map.Factories.Length), "у каждого завода труба");
                    Assert.That(layout.Pipes.All(p => p.WallMounted), Is.True);
                    row["pipes"] = layout.Pipes.Count;
                    row["pipeCrossingCells"] = layout.Pipes.Sum(p => p.CrossingCells);
                    row["pipeDeepRockCells"] = layout.Pipes.Sum(p => p.DeepRockCells);
                    row["pipeMaxShared"] = layout.MaxShared;
                }

                // Котики в настоящем цикле: снаряжение тест-набора, 10 минут игры — DEV-скорость ×100 и шаг часов 0,2 с за кадр
                // (сдвиг больше секунды за кадр контроллер считает разрывом и догоняет офлайн-расчётом, а не обычной игрой).
                Game.GrantTestKit();
                yield return null;
                Game.SimSpeed = 100;
                Assert.That(Game.SimSpeed, Is.EqualTo(100));
                var run = Game.State.Run;
                int clearedBefore = run.Counters.ClearedTargets;
                long startTick = Game.Sim.Now;
                int idleWithWork = 0, checks = 0;
                for (int step = 0; step < 30; step++)
                {
                    Clock.Advance(0.2);
                    yield return null;
                    Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing), "без офлайн-догона");
                    // Каждый кадр — 20 с игры: проверка простоя и покупки «Команды» и кирок.
                    checks++;
                    if (IdleWhileWorkAvailable()) idleWithWork++;
                    Game.Buy(UpgradeBranch.Team, BuyMode.One);
                    Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One);
                }
                Assert.That(Game.Sim.Now - startTick, Is.GreaterThanOrEqualTo(590L * OutpostIds.TicksPerSecond), "прошло 10 минут игры");
                Game.SimSpeed = 1;
                run = Game.State.Run;
                Assert.That(run.WatchdogFired, Is.False, run.WatchdogDiagnostic);
                Assert.That(run.Counters.DeliveredUnits, Is.GreaterThan(0), "груз доставлен на базу");
                int debrisCleared = 0;
                for (int i = 0; i < map.Targets.Length; i++)
                    if (map.Targets[i].IsDebris && run.Targets[i].Destroyed) debrisCleared++;
                Assert.That(debrisCleared, Is.GreaterThan(0), "котики разбили мусор");
                Assert.That(idleWithWork, Is.LessThanOrEqualTo(1), "простой при доступной работе (проверок " + checks + ")");
                row["simSeconds"] = 600;
                row["cleared"] = run.Counters.ClearedTargets - clearedBefore;
                row["debrisCleared"] = debrisCleared;
                row["delivered"] = run.Counters.DeliveredUnits;
                row["workers"] = run.Workers.Count;
                row["idleWithWorkChecks"] = idleWithWork + "/" + checks;

                // Кадры в обычном темпе (1/30 с игры на кадр).
                var ms = new List<double>();
                long previous = Stopwatch.GetTimestamp();
                for (int f = 0; f < 120; f++)
                {
                    Clock.Advance(1.0 / 30);
                    yield return null;
                    long now = Stopwatch.GetTimestamp();
                    ms.Add((now - previous) * 1000.0 / Stopwatch.Frequency);
                    previous = now;
                }
                ms.Sort();
                row["frameMedianMs"] = Math.Round(ms[ms.Count / 2], 2);
                row["frameP95Ms"] = Math.Round(ms[(int)Math.Ceiling(ms.Count * 0.95) - 1], 2);
                row["sprites"] = Game.Board.GetComponentsInChildren<SpriteRenderer>(false).Length;

                if (ShotZones.Contains(zone))
                {
                    yield return CaptureCloseUp("z" + zone + "-closeup.png");
                    yield return CaptureOverview("z" + zone + "-overview.png");
                    // Трубы: все заводы открыты (DEV), снимок всей шахты.
                    Game.DebugUnlockFactories();
                    for (int k = 0; k < 30; k++)
                    {
                        Clock.Advance(0.1);
                        yield return null;
                    }
                    yield return CaptureOverview("z" + zone + "-pipes.png");
                    yield return CaptureFactory("z" + zone + "-pipe-closeup.png");
                }
                report.Add(row);
            }
            File.WriteAllText(Path.Combine(Shots, "runtime.json"), new JObject
            {
                ["method"] = "PlayMode, debug profile, LoadZone + GrantTestKit, 600 s of game time at DEV speed x100, 0.2 s of clock per frame (Team and Pickaxes bought every 20 s), " +
                             "then 120 frames at 1/30 s per frame; frame times include the Editor test runner.",
                ["zones"] = report,
            }.ToString() + "\n");
        }

        /// <summary>Котик ждёт (Idle), хотя в открытом секторе есть живая цель со свободной достижимой рабочей клеткой и местом.</summary>
        bool IdleWhileWorkAvailable()
        {
            var sim = Game.Sim;
            var run = sim.Run;
            if (run.Phase != RunPhase.Running) return false;
            var idle = run.Workers.Where(w => w.Mode == WorkerMode.Idle).ToList();
            if (idle.Count == 0) return false;
            var taken = new HashSet<int>(run.Workers.Where(w => w.WorkCell >= 0).Select(w => w.WorkCell));
            var assigned = run.Workers.Where(w => w.TargetId >= 0).GroupBy(w => w.TargetId).ToDictionary(g => g.Key, g => g.Count());
            var map = sim.Map;
            foreach (var w in idle)
                for (int i = 0; i < map.Targets.Length; i++)
                {
                    var t = map.Targets[i];
                    if (t.IsGate || t.Sector != run.GatesOpened || run.Targets[i].Destroyed) continue;
                    if (assigned.TryGetValue(t.Id, out int n) && n >= run.Balance.MaxWorkersPerTarget) continue;
                    foreach (int c in t.WorkCells)
                        if (!taken.Contains(c) && sim.Navigation.Passable(c) && sim.Navigation.Distance(w.Cell, c) >= 0) return true;
                }
            return false;
        }

        // ───────────── Снимки поля (без HUD) ─────────────

        /// <summary>Крупно: постоянное препятствие с мусором рядом, обычный масштаб камеры игры.</summary>
        IEnumerator CaptureCloseUp(string name)
        {
            var map = Game.Map;
            var prop = map.Props.Where(p => !p.Wall)
                .OrderByDescending(p => map.Targets.Count(t => t.IsDebris && Near(map, t.Cells, p.Cells, 3)))
                .ThenBy(p => p.Row).First();
            var center = Game.Board.CellCenter(prop.Column, prop.Row) + new Vector3(0f, 0f, 1.5f);
            yield return Render(name, center, Game.Rig.Cam.orthographicSize, 1080, 1920);
        }

        /// <summary>Крупно: завод с открытой трубой у стены.</summary>
        IEnumerator CaptureFactory(string name)
        {
            var map = Game.Map;
            var f = map.Factories.Select(x => map.Target(x.TargetId)).Where(t => t != null).OrderBy(t => t.Row).Last();
            var center = Game.Board.CellCenter(f.Column, f.Row) + new Vector3(0.5f, 0f, -0.5f);
            yield return Render(name, center, Game.Rig.Cam.orthographicSize * 1.4f, 1080, 1920);
        }

        /// <summary>Вся шахта с лагерем сверху: распределение препятствий и мусора, трубы.</summary>
        IEnumerator CaptureOverview(string name)
        {
            var map = Game.Map;
            var bottomLeft = Game.Board.CellCenter(0, map.Rows - 1) - new Vector3(0.5f, 0f, 0.5f);
            float width = map.Width, height = map.Rows + 4f;
            var center = bottomLeft + new Vector3(width / 2f, 0f, height / 2f - 3.5f);
            int h = 2048, w = Mathf.Clamp(Mathf.RoundToInt(h * width / height), 256, 2048);
            if (w == 2048) h = Mathf.Clamp(Mathf.RoundToInt(w * height / width), 256, 2048);
            yield return Render(name, center, height / 2f, w, h);
        }

        IEnumerator Render(string name, Vector3 center, float ortho, int w, int h)
        {
            var rig = Game.Rig;
            var cam = rig.Cam;
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            bool rigEnabled = rig.enabled;
            var position = cam.transform.position;
            float size = cam.orthographicSize;
            var target = cam.targetTexture;
            try
            {
                rig.enabled = false;
                yield return null;
                cam.transform.position = new Vector3(center.x, position.y, center.z);
                cam.orthographicSize = ortho;
                cam.targetTexture = rt;
                cam.Render();
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                File.WriteAllBytes(Path.Combine(Shots, name), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            finally
            {
                cam.targetTexture = target;
                cam.orthographicSize = size;
                cam.transform.position = position;
                rig.enabled = rigEnabled;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        static bool Near(OutpostMap map, int[] a, int[] b, int radius)
        {
            foreach (int x in a)
                foreach (int y in b)
                    if (Math.Max(Math.Abs(map.Col(x) - map.Col(y)), Math.Abs(map.Row(x) - map.Row(y))) <= radius) return true;
            return false;
        }
    }
}
