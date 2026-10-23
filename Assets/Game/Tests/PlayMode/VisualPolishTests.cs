using System.Collections;
using System.Collections.Generic;
using CatMiners.Core.Sim;
using CatMiners.DevTools;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Визуальная полировка (Docs/Visual/VISUAL_QA.md): оформления окружения по диапазонам зон из визуальной конфигурации,
    /// варианты тем в атласе, полный набор кадров котика по окрасам и ступеням экипировки, фон, затемнение, механизмы
    /// и музыка по оформлению, сохраняемая громкость групп.
    /// </summary>
    public class VisualPolishTests
    {
        static readonly string[] s_ThemedTiles =
        {
            "floor_0", "floor_1", "floor_2", "floor_3", "floor_gate",
            "wall_side_0", "wall_side_1", "wall_side_2", "wall_side_3",
            "wall_edge_l_0", "wall_edge_l_1", "wall_edge_r_0", "wall_edge_r_1",
            "wall_top", "wall_top_1", "wall_top_2", "wall_top_3", "wall_top_edge", "wall_bottom",
            "wall_corner_tl", "wall_corner_tr", "wall_corner_bl", "wall_corner_br",
            "block_tl", "block_t", "block_tr", "block_l", "block_c", "block_r", "block_bl", "block_b", "block_br",
            "block_vt", "block_vm", "block_vb", "block_hl", "block_hm", "block_hr",
            "block_single_1", "block_single_2", "block_single_3", "block_single_4", "block_single_5", "block_single_6",
            "wall_gate_l", "wall_gate_r", "waterfall_top", "crystal_cluster_a", "crystal_cluster_b", "beam_post",
        };

        string m_SaveDir;
        LogWatcher m_Log;
        ManualClock m_Clock;

        /// <summary>
        /// Часы игры в тесте стоят (задержка кадра в batchmode не превращается в офлайн), симуляция идёт, только когда
        /// тест их двигает: <paramref name="seconds"/> реального времени кадрами, не больше 0,05 с часов на кадр.
        /// </summary>
        IEnumerator RunSim(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                m_Clock.Advance(Mathf.Min(0.05f, Mathf.Max(0.001f, Time.unscaledDeltaTime)));
                yield return null;
            }
        }

        [Test]
        public void ThemeConfig_AssignsZoneRangesAndRepeats()
        {
            for (int z = 1; z <= 3; z++) Assert.AreEqual(EnvironmentTheme.Warm, EnvironmentThemes.Of(z), "zone" + z);
            for (int z = 4; z <= 6; z++) Assert.AreEqual(EnvironmentTheme.Crystal, EnvironmentThemes.Of(z), "zone" + z);
            for (int z = 7; z <= 9; z++) Assert.AreEqual(EnvironmentTheme.Copper, EnvironmentThemes.Of(z), "zone" + z);
            Assert.AreEqual(EnvironmentTheme.Warm, EnvironmentThemes.Of(10), "раскладка повторяется с шагом 9");
            var seen = new HashSet<EnvironmentTheme>();
            for (int z = 1; z <= 144; z++) seen.Add(EnvironmentThemes.Of(z));
            Assert.AreEqual(3, seen.Count, "все три оформления встречаются в кампании");
            Assert.AreEqual(MusicPlayer.Crystal, PresentationCatalog.MusicOf(EnvironmentTheme.Crystal));
            Assert.AreEqual(MusicPlayer.Warm, PresentationCatalog.MusicOf(EnvironmentTheme.Copper));
        }

        [Test]
        public void ThemeConfig_RejectsBrokenConfig()
        {
            Assert.IsNull(EnvironmentThemes.Parse("{\"Cycle\":0}", out _), "пустой цикл");
            Assert.IsNull(EnvironmentThemes.Parse("{\"Cycle\":3,\"Ranges\":[{\"From\":2,\"To\":5,\"Theme\":\"Warm\"}]}", out _), "диапазон вне цикла");
            Assert.IsNull(EnvironmentThemes.Parse("{\"Cycle\":3,\"Ranges\":[{\"From\":1,\"To\":2,\"Theme\":\"Lava\"}]}", out var error), "неизвестная тема");
            StringAssert.Contains("Lava", error);
            var table = EnvironmentThemes.Parse("{\"Cycle\":4,\"Ranges\":[{\"From\":2,\"To\":3,\"Theme\":\"copper\"}]}", out error);
            Assert.IsNotNull(table, error);
            CollectionAssert.AreEqual(new[] { EnvironmentTheme.Warm, EnvironmentTheme.Copper, EnvironmentTheme.Copper, EnvironmentTheme.Warm }, table);
        }

        [Test]
        public void Atlas_HasThemeVariantsOfEveryTileObstructionAndWallDecor()
        {
            foreach (var theme in new[] { EnvironmentTheme.Crystal, EnvironmentTheme.Copper })
            {
                string suffix = PixelTheme.Suffix(theme);
                foreach (var name in s_ThemedTiles)
                    Assert.IsTrue(PixelArt.Has(name + suffix), $"нет {name}{suffix}");
            }
            for (int i = 0; i < 8; i++) Assert.IsTrue(PixelArt.Has("gear_copper_" + i), "шестерня, кадр " + i);
            foreach (var name in new[] { "pipe_copper", "crystal_spire", "fx_glow_teal", "fx_glow_blue", "fx_glow_purple" })
                Assert.IsTrue(PixelArt.Has(name), name);

            var saved = PixelTheme.Current;
            try
            {
                PixelTheme.Current = EnvironmentTheme.Copper;
                Assert.AreEqual("floor_0_copper", PixelTheme.Name("floor_0"));
                Assert.AreEqual("rock_plain_0", PixelTheme.Name("rock_plain_0"), "камни добычи не перекрашиваются");
                PixelTheme.Current = EnvironmentTheme.Warm;
                Assert.AreEqual("floor_0", PixelTheme.Name("floor_0"));
            }
            finally
            {
                PixelTheme.Current = saved;
            }
        }

        [Test]
        public void Atlas_HasEveryCatFrameForEveryCoatHelmetAndWeapon()
        {
            Assert.GreaterOrEqual(CatLook.Coats.Length, 6, "не меньше 6 окрасов");
            Assert.AreEqual(8, CatLook.Weapons.Length, "8 видов оружия в круге");
            Assert.AreEqual(8, CatLook.HelmetColors.Length, "8 цветов каски");
            Assert.AreEqual(CatLook.FrameCount, CatLook.Frames.Length);
            for (int f = 0; f < CatLook.FrameCount; f++)
            {
                for (int c = 0; c < CatLook.Coats.Length; c++)
                    Assert.IsTrue(PixelArt.Has(CatLook.Body(c, f)), CatLook.Body(c, f));
                for (int h = 0; h < CatLook.HelmetColors.Length; h++)
                    Assert.IsTrue(PixelArt.Has(CatLook.Helmet(h, f)), CatLook.Helmet(h, f));
                // Кадры состояний выбирает CatView (до tool_*); с инструментом фазы добычи показываются кадрами tool_*.
                if (f >= CatLook.ToolReady) continue;
                for (int w = 0; w < CatLook.Weapons.Length; w++)
                {
                    int shown = CatLook.ForWeapon(f, w);
                    Assert.IsTrue(PixelArt.Has(CatLook.WeaponSprite(w, shown)), CatLook.WeaponSprite(w, shown));
                }
            }
        }

        [Test]
        public void WeaponAndHelmetFollowEightLevelCycle()
        {
            // Уровни 1–8: кирка, молот, дробилка, циркулярная пила, бур-машина, две кирки, бензопила, лазерган; каска — первый цвет.
            int[] expected = { CatLook.Pick, CatLook.Hammer, CatLook.Crusher, CatLook.Saw, CatLook.Drill, CatLook.DualPick, CatLook.Chainsaw, CatLook.Laser };
            for (int level = 1; level <= 8; level++)
            {
                Assert.AreEqual(expected[level - 1], CatLook.Weapon(level), "оружие уровня " + level);
                Assert.AreEqual(0, CatLook.HelmetColor(level), "каска уровня " + level);
            }
            // Уровень 9 — новый круг: снова кирка, каска другого цвета; 16 — лазерган; 17 — третий цвет.
            Assert.AreEqual(CatLook.Pick, CatLook.Weapon(9));
            Assert.AreEqual(1, CatLook.HelmetColor(9));
            Assert.AreEqual(CatLook.Laser, CatLook.Weapon(16));
            Assert.AreEqual(1, CatLook.HelmetColor(16));
            Assert.AreEqual(CatLook.Pick, CatLook.Weapon(17));
            Assert.AreEqual(2, CatLook.HelmetColor(17));
            Assert.AreEqual(0, CatLook.HelmetColor(8 * CatLook.HelmetColors.Length + 1), "после восьмого цвета цвета повторяются");
            // Инструменты добывают «с упора»: кадры добычи tool_*, ударное оружие — mine_*.
            Assert.IsTrue(CatLook.IsTool(CatLook.Saw) && CatLook.IsTool(CatLook.Drill) && CatLook.IsTool(CatLook.Chainsaw) && CatLook.IsTool(CatLook.Laser));
            Assert.IsFalse(CatLook.IsTool(CatLook.Pick) || CatLook.IsTool(CatLook.Hammer) || CatLook.IsTool(CatLook.Crusher) || CatLook.IsTool(CatLook.DualPick));
            Assert.AreEqual("tool_strike", CatLook.Frames[CatLook.ForWeapon(CatLook.MineStrike, CatLook.Laser)]);
            Assert.AreEqual("mine_strike", CatLook.Frames[CatLook.ForWeapon(CatLook.MineStrike, CatLook.Hammer)]);
            Assert.AreEqual(CatLook.Idle, CatLook.ForWeapon(CatLook.Idle, CatLook.Drill), "кадры вне добычи общие");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = PlayModeUtil.UseTempSaveDir();
            CameraRig.OverrideAspect = 1080f / 1920f;
            m_Clock = new ManualClock(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 100.0);
            GameController.ClockOverride = m_Clock;
            m_Log = new LogWatcher();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Log.Dispose();
            GameController.ClockOverride = null;
            CameraRig.OverrideAspect = 0f;
            PlayModeUtil.DropTempSaveDir(m_SaveDir);
            yield break;
        }

        [UnityTest]
        public IEnumerator ZoneThemeDrivesBackgroundFogDecorAndMusic()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            Assert.AreEqual(EnvironmentTheme.Warm, PixelTheme.Current, "zone1 — тёплая штольня");
            Assert.AreEqual((Color32)PixelPalette.Navy0, (Color32)gc.Rig.Cam.backgroundColor);
            var cheat = LevelCheat.Attach(gc);

            // Медная выработка: фон и затемнение — медная глубина, на стенах крутятся шестерни. Поле после перехода может
            // перестроиться ещё раз (событие ZoneEntered), поэтому шестерни ищутся заново; кадр зависит только от времени.
            cheat.JumpTo(7);
            yield return PlayModeUtil.WaitReal(0.3f);
            Assert.AreEqual(7, gc.Session.Mine.Zone);
            Assert.AreEqual(EnvironmentTheme.Copper, PixelTheme.Current);
            Assert.AreEqual((Color32)PixelPalette.Copper0, (Color32)gc.Rig.Cam.backgroundColor);
            var gears = Gears(gc);
            Assert.Greater(gears.Count, 0, "в медной выработке есть шестерни");
            string first = gears[0].sprite.name;
            yield return PlayModeUtil.WaitReal(0.5f);
            gears = Gears(gc);
            Assert.AreNotEqual(first, gears[0].sprite.name, "шестерня поворачивается");

            // Кристальная пещера: своя музыка.
            cheat.JumpTo(4);
            yield return null;
            Assert.AreEqual(EnvironmentTheme.Crystal, PixelTheme.Current);
            Assert.AreEqual((Color32)PixelPalette.Cave0, (Color32)gc.Rig.Cam.backgroundColor);
            if (gc.Music.Current >= 0) Assert.AreEqual(MusicPlayer.Crystal, gc.Music.Current, "в пещере — «Кристальная глубина»");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        /// <summary>
        /// Состояния шахты различимы на самом спрайте (ТЗ 8.3), а не только пузырём: завал — доски и камни; готова
        /// к активации — лента с бантом; работает — свет; готова выплата — горка самоцветов и блеск; автоматизирована —
        /// табличка управляющего.
        /// </summary>
        [UnityTest]
        public IEnumerator ShaftStatesAreVisiblyDistinct()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            yield return PlayModeUtil.CloseModals(gc);
            LevelCheat.Attach(gc).JumpTo(3);
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            Assert.Greater(s.Layout.Shafts.Count, 0, "в zone3 есть шахты");
            int shaft = s.Layout.Shafts[0].Ordinal;
            foreach (var sh in s.Layout.Shafts)
                if (s.IsCellOpen(sh.Cells[0])) { shaft = sh.Ordinal; break; }
            var view = gc.Board.ObjectViewOf(shaft);
            Assert.IsNotNull(view);
            AssertParts(view, "завал", rubble: true, ribbon: false, light: false, heap: false, plaque: false);

            s.DebugCompleteTarget(shaft);
            yield return null;
            yield return null;
            Assert.AreEqual(ObjectStatus.ReadyToActivate, s.Mine.Objects[shaft].Status);
            AssertParts(view, "готова к активации", rubble: false, ribbon: true, light: false, heap: false, plaque: false);

            gc.ActivateShaft(shaft);
            yield return null;
            yield return PlayModeUtil.CloseModals(gc);
            Assert.AreEqual(ObjectStatus.Producing, s.Mine.Objects[shaft].Status);
            if (!s.Mine.Objects[shaft].Gen.Automated)
            {
                AssertParts(view, "работает", rubble: false, ribbon: false, light: true, heap: false, plaque: false);
                // Ускоренное время до готовой выплаты (ручной сбор): ×100, часы — по 0,05 с за кадр.
                gc.SimSpeed = 100;
                for (int i = 0; i < 600 && !s.Mine.Objects[shaft].Gen.PayoutReady; i++)
                {
                    m_Clock.Advance(0.05);
                    yield return null;
                }
                gc.SimSpeed = 1;
                yield return null;
                Assert.IsTrue(s.Mine.Objects[shaft].Gen.PayoutReady, "выплата накопилась");
                AssertParts(view, "готова выплата", rubble: false, ribbon: false, light: true, heap: true, plaque: false);
            }

            // Карты зоны — шахта автоматизирована: выплата собирается сама, на стойке табличка управляющего.
            s.DebugGiveZoneCards();
            yield return null;
            yield return PlayModeUtil.CloseModals(gc);
            Assert.IsTrue(s.Mine.Objects[shaft].Gen.Automated);
            AssertParts(view, "автоматизирована", rubble: false, ribbon: false, light: true, heap: false, plaque: true);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        static void AssertParts(ObjectView view, string state, bool rubble, bool ribbon, bool light, bool heap, bool plaque)
        {
            Assert.AreEqual(rubble, Part(view, "rubble"), state + ": завал");
            Assert.AreEqual(ribbon, Part(view, "ribbon"), state + ": лента");
            Assert.AreEqual(light, Part(view, "light"), state + ": свет");
            Assert.AreEqual(heap, Part(view, "heap"), state + ": горка");
            Assert.AreEqual(heap, Part(view, "glint"), state + ": блеск");
            Assert.AreEqual(plaque, Part(view, "plaque"), state + ": табличка");
        }

        static bool Part(ObjectView view, string name)
        {
            foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.name == name) return sr.enabled && sr.gameObject.activeInHierarchy;
            Assert.Fail("нет части шахты " + name);
            return false;
        }

        /// <summary>
        /// В игре котик показывает оружие и каску своего уровня (уровни 1, 4, 8, 9: кирка, пила, лазерган, снова кирка
        /// в каске нового цвета), инструмент добывает кадрами «с упора», ударное оружие — замахом; «пропавших» спрайтов нет.
        /// </summary>
        [UnityTest]
        public IEnumerator CatsShowWeaponAndHelmetOfTheirLevel()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            yield return PlayModeUtil.CloseModals(gc);
            LevelCheat.Attach(gc).JumpTo(2);
            yield return PlayModeUtil.WaitReal(0.3f);
            yield return PlayModeUtil.CloseModals(gc);
            var s = gc.Session;
            foreach (int level in new[] { 1, 4, 8, 9 }) Assert.IsTrue(s.DebugSpawnCat(level), "котик уровня " + level);
            PlayModeUtil.AssignIdleCats(gc);
            int checkedCats = 0, toolsMining = 0;
            for (int round = 0; round < 12; round++)
            {
                yield return RunSim(0.25f);
                foreach (var view in gc.Board.GetComponentsInChildren<CatView>())
                {
                    var cat = s.FindCat(view.CatId);
                    if (cat == null) continue;
                    int weapon = CatLook.Weapon(cat.Level);
                    string weaponSprite = Layer(view, "weapon"), helmetSprite = Layer(view, "helmet"), body = Layer(view, "body");
                    StringAssert.StartsWith("cat_weapon_" + CatLook.Weapons[weapon] + "_", weaponSprite, "оружие уровня " + cat.Level);
                    StringAssert.StartsWith("cat_helmet_" + CatLook.HelmetColors[CatLook.HelmetColor(cat.Level)] + "_", helmetSprite);
                    if (CatLook.IsTool(weapon))
                    {
                        StringAssert.DoesNotContain("_mine_", body + weaponSprite, "инструмент не замахивается над головой");
                        if (body.Contains("_tool_")) toolsMining++;
                    }
                    else StringAssert.DoesNotContain("_tool_", body + weaponSprite);
                    if (round == 0) checkedCats++;
                }
            }
            Assert.GreaterOrEqual(checkedCats, 4, "все котики на поле");
            Assert.Greater(toolsMining, 0, "котик с пилой или лазером добывал кадрами с упора");
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }

        static string Layer(CatView view, string name)
        {
            foreach (var sr in view.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.name == name)
                {
                    Assert.IsNotNull(sr.sprite, name);
                    Assert.IsFalse(sr.sprite.name.StartsWith("missing:"), sr.sprite.name);
                    return sr.sprite.name;
                }
            Assert.Fail("нет слоя " + name);
            return null;
        }

        static List<SpriteRenderer> Gears(GameController gc)
        {
            var gears = new List<SpriteRenderer>();
            foreach (var sr in gc.Board.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.name.StartsWith("gear_copper_")) gears.Add(sr);
            return gears;
        }

        [UnityTest]
        public IEnumerator VolumeGroupsAreSavedAndApplied()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            gc.SetVolume(AudioGroup.Music, 0.25f);
            gc.SetVolume(AudioGroup.Ambience, 0.1f);
            gc.SetVolume(AudioGroup.Master, 0.5f);
            gc.SetReduceEffects(true);
            var s = gc.Session.Player.Settings;
            Assert.AreEqual(0.25f, s.MusicVolume, 1e-4f);
            Assert.AreEqual(0.1f, s.AmbienceVolume, 1e-4f);
            Assert.AreEqual(0.5f, s.MasterVolume, 1e-4f);
            Assert.IsTrue(s.ReduceEffects);
            Assert.AreEqual(0.125f, AudioMix.MusicGain, 1e-4f, "группа × Master");
            Assert.AreEqual(0.05f, AudioMix.AmbienceGain, 1e-4f);
            Assert.IsTrue(VisualSettings.ReduceEffects);
            Assert.AreEqual(2, VisualSettings.Particles(4), "«Меньше эффектов» — вдвое меньше частиц");
            gc.SetReduceEffects(false);
            gc.SetVolume(AudioGroup.Master, 1f);
            gc.SetVolume(AudioGroup.Music, 0.6f);
            gc.SetVolume(AudioGroup.Ambience, 0.7f);
            Assert.IsEmpty(m_Log.Errors, m_Log.Report());
        }
    }
}
