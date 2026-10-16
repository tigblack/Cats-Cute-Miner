using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Снимки шахт с заводами для владельца (ТЗ 2.2 §5, §11, §16.2; ворота G3): шахты 2, 10, 30, 80, 140 и 144 в форматах 9:16
    /// (1080×1920) и 9:20 (1080×2400) — камера каждого формата подогнана под его ширину, как на устройстве. На каждую шахту:
    /// <list type="bullet">
    /// <item><c>a-locked</c> — начало шахты, ближайший к лагерю завод в цепях;</item>
    /// <item><c>b-ready</c> — все цепи разбиты, циклы закончены: ручные заводы в Ready с кнопками «Deliver ×N» (самое плотное место);</item>
    /// <item><c>c-base</c> — все заводы на автомате, капсулы в трубах: лагерь, жгут и склад;</item>
    /// <item><c>d-mid</c> — то же у самого плотного места заводов;</item>
    /// <item><c>overview</c> — вся карта сверху без HUD (2 пикселя снимка на пиксель арта), раскладка труб.</item>
    /// </list>
    /// Сводка раскладки труб и кнопок «Deliver» на снимках b-ready в 9:16 и 9:20 (сколько кнопок, сколько коротких «иконка ×N»,
    /// наложения, кнопки выше ряда над заводом, стрелки на кнопках и заводах — правило владельца 15.10.2026 «сокращать в
    /// тесноте», <see cref="DeliverLayout"/>) — <c>layout.txt</c>. Каталог — Docs/Reports/factories/screens/.
    /// Отдельно: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter OutpostFactoryScreensEvidence</c>.
    /// </summary>
    [Explicit("генератор снимков Docs/Reports/factories/screens/*.png")]
    public sealed class OutpostFactoryScreensEvidence : OutpostCardsFixture
    {
        static readonly int[] Mines = { 2, 10, 30, 80, 140, 144 };
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "factories", "screens");

        readonly StringBuilder m_Log = new StringBuilder();

        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        IEnumerator CloseWindows()
        {
            for (int i = 0; i < 20 && Game.Hud.AnyWindowOpen; i++)
            {
                Game.Hud.HandleBack();
                yield return null;
            }
        }

        IEnumerator WaitFor(Func<bool> done, string what, int frames = 600)
        {
            for (int i = 0; i < frames && !done(); i++) yield return null;
            Assert.That(done(), Is.True, what);
            yield return null;
        }

        /// <summary>Снимок 1080×<paramref name="height"/> с HUD; камера подогнана под этот формат (OverrideAspect), затем 9:16 обратно.</summary>
        IEnumerator Shot(string name, int height, Action<int> rendered = null)
        {
            var camera = Game.Rig.Cam;
            var canvas = Game.Hud.GetComponent<Canvas>();
            var scaler = Game.Hud.GetComponent<CanvasScaler>();
            var texture = new RenderTexture(1080, height, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float previousScale = canvas.scaleFactor;
            CameraRig.OverrideAspect = 1080f / height;
            try
            {
                camera.targetTexture = texture;
                scaler.enabled = false;
                canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return PlayModeUtil.WaitReal(0.4f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Save(texture, name, 1080, height);
                rendered?.Invoke(height);
            }
            finally
            {
                camera.targetTexture = previous;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.scaleFactor = previousScale;
                scaler.enabled = true;
                CameraRig.OverrideAspect = 9f / 16;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        IEnumerator Shots2(string name, Action<int> rendered = null)
        {
            yield return Shot(name + "-916.png", 1920, rendered);
            yield return Shot(name + "-920.png", 2400, rendered);
        }

        /// <summary>Вся карта сверху без HUD: 2 пикселя снимка на пиксель арта (не выше 8192).</summary>
        void Overview(string name)
        {
            var camera = Game.Rig.Cam;
            var board = Game.Board;
            float w = board.Width + 1f, h = board.CameraDepth + 1f;
            int pw = Mathf.RoundToInt(w * 64f), ph = Mathf.Min(8192, Mathf.RoundToInt(h * 64f));
            var texture = new RenderTexture(pw, ph, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            var pos = camera.transform.position;
            var rot = camera.transform.rotation;
            float size = camera.orthographicSize;
            try
            {
                camera.targetTexture = texture;
                camera.aspect = pw / (float)ph;
                camera.orthographicSize = ph / 64f / 2f;
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                camera.transform.position = new Vector3(board.Width / 2f, 60f, board.TopZ - ph / 64f / 2f + 0.5f);
                camera.Render();
                Save(texture, name, pw, ph);
            }
            finally
            {
                camera.targetTexture = previous;
                camera.ResetAspect();
                camera.orthographicSize = size;
                camera.transform.SetPositionAndRotation(pos, rot);
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }

        static void Save(RenderTexture texture, string name, int w, int h)
        {
            var active = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(w, h, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            pixels.Apply();
            RenderTexture.active = active;
            Directory.CreateDirectory(Shots);
            File.WriteAllBytes(Path.Combine(Shots, name), pixels.EncodeToPNG());
            UnityEngine.Object.Destroy(pixels);
        }

        IEnumerator Focus(Vector3 world, float dz = 0f)
        {
            Game.Rig.FocusOn(new Vector2(world.x, world.z + dz));
            yield return null;
            yield return null;
        }

        /// <summary>Завод, у которого в полосе ±4 строки больше всего других заводов (при равенстве — ближе к лагерю по трубе).</summary>
        Vector3 DensestFactory()
        {
            var at = new List<(Vector3 p, int steps)>();
            foreach (var mf in Game.Map.Factories)
                if (Game.TryGetFactoryWorldPosition(mf.TargetId, out var p)) at.Add((p, mf.RouteSteps));
            return at.OrderByDescending(a => at.Count(b => Mathf.Abs(b.p.z - a.p.z) <= 4f)).ThenBy(a => a.steps).First().p;
        }

        /// <summary>Раскладка кнопок «Deliver» и стрелок на снимке b-ready по форматам (1920 — 9:16, 2400 — 9:20).</summary>
        readonly Dictionary<int, DeliverLayout> m_Deliver = new Dictionary<int, DeliverLayout>();
        readonly Dictionary<int, int> m_Dropped = new Dictionary<int, int>();
        readonly List<string> m_Problems = new List<string>();
        int m_Zone;

        /// <summary>
        /// Кнопки «Deliver» и стрелки у края на снимке (холст в этот момент ScreenSpaceCamera: углы — в пикселях снимка): число,
        /// короткие, наложения, выше ряда над заводом, стрелки на кнопках и заводах, стрелки без места (<see cref="DeliverLayout"/>).
        /// </summary>
        void MeasureDeliver(int height)
        {
            var layout = DeliverLayout.Measure(Game, "m" + m_Zone.ToString("000") + (height == 1920 ? " 9:16" : " 9:20"));
            m_Deliver[height] = layout;
            m_Dropped[height] = Game.Input.Factories.DroppedButtons;
            m_Problems.AddRange(layout.Problems);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator MineScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "*.png")) File.Delete(file);
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Assert.That(Game.IsDebugProfile, Is.True);
            m_Problems.Clear();
            m_Log.AppendLine("mine\tformat\tfactories\tpipes\tmaxPerEdge\tbands\tcouplings\tdeliverShown\tdeliverShort\tdeliverDropped\tdeliverOverlaps" +
                             "\tdeliverAboveRow\tarrowOverButton\tarrowOverFactory\tarrowNoRoom\tcapsules");
            foreach (int zone in Mines)
            {
                Game.LoadZone(zone);
                yield return WaitPlaying();
                yield return WaitFor(() => Game.State.Run.Zone == zone, "шахта " + zone);
                yield return CloseWindows();
                string tag = "m" + zone.ToString("000");
                m_Zone = zone;
                int factories = Game.Map.Factories.Length;
                Assert.That(factories, Is.GreaterThan(0), tag);

                // a: начало шахты — ближайший к лагерю завод в цепях.
                int nearest = Game.Map.Factories.OrderBy(f => f.RouteSteps).First().TargetId;
                Assert.That(Game.TryGetFactoryWorldPosition(nearest, out var near), Is.True);
                yield return Focus(near, -1f);
                yield return Shots2(tag + "-a-locked");

                // b: все цепи разбиты, циклы закончены — ручные заводы в Ready с кнопками «Deliver».
                Game.DebugUnlockFactories();
                yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Status != FactoryStatus.Locked), tag + ": цепи");
                yield return CloseWindows();
                Game.DebugFinishFactoryCycles();
                yield return WaitFor(() => Game.State.Run.Factories.Any(x => x.Status == FactoryStatus.Ready || x.Automated), tag + ": циклы");
                yield return CloseWindows();
                var dense = DensestFactory();
                yield return Focus(dense);
                yield return PlayModeUtil.WaitReal(0.3f);
                m_Deliver.Clear();
                m_Dropped.Clear();
                yield return Shots2(tag + "-b-ready", MeasureDeliver);

                // c, d: все заводы на автомате, партии в трубах.
                Game.DebugAutomateAll();
                yield return WaitFor(() => Game.State.Run.Factories.All(x => x.Automated), tag + ": AUTO");
                Game.DebugFinishFactoryCycles();
                yield return Advance(1.5);
                Game.FocusBase();
                yield return PlayModeUtil.WaitReal(0.8f);
                yield return Shots2(tag + "-c-base");
                yield return Focus(dense);
                yield return Shots2(tag + "-d-mid");
                Overview(tag + "-overview.png");

                var layout = Game.Board.Pipes.Layout;
                foreach (int height in new[] { 1920, 2400 })
                {
                    var d = m_Deliver.TryGetValue(height, out var measured) ? measured : new DeliverLayout();
                    m_Log.Append(zone).Append('\t').Append(height == 1920 ? "9:16" : "9:20").Append('\t').Append(factories).Append('\t').Append(layout.Pipes.Count)
                        .Append('\t').Append(layout.MaxShared).Append('\t').Append(layout.Bands.Count).Append('\t').Append(layout.Couplings.Count)
                        .Append('\t').Append(d.Buttons.Count).Append('\t').Append(d.Short).Append('\t').Append(m_Dropped.TryGetValue(height, out int dropped) ? dropped : 0)
                        .Append('\t').Append(d.Overlaps).Append('\t').Append(d.TooHigh).Append('\t').Append(d.ArrowOverButton).Append('\t').Append(d.ArrowOverFactory)
                        .Append('\t').Append(d.ArrowBlocked).Append('\t').Append(Game.Board.Pipes.ActiveCapsules).AppendLine();
                }
                File.WriteAllText(Path.Combine(Shots, "layout.txt"), m_Log.ToString());
            }
            Assert.That(m_Problems, Is.Empty, "раскладка кнопок «Deliver» на снимках b-ready:\n" + string.Join("\n", m_Problems));
        }
    }
}
