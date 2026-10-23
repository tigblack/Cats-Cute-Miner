using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CatMiners.Core.Outpost;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace CatMiners.Tests
{
    /// <summary>
    /// Съёмка геймплея для рекламных роликов (Docs/Marketing/AD_SCRIPTS.md): настоящая игра, сценарные действия, кадры
    /// 1080×1920 при 30 fps: каждый кадр — ровно 1/30 с часов игры и анимаций (Time.captureDeltaTime → FrameTime.Delta),
    /// сколько бы ни длился рендер, поэтому медленный ПК не портит запись.
    /// Кадры уходят прямо в ffmpeg проекта (Tools/AdVideo/.venv, imageio-ffmpeg) → build/adcapture/&lt;shot&gt;.mp4 (yuv444, crf 10),
    /// журнал кадров, тапов, звуковых сигналов и событий → Docs/Reports/ads/&lt;shot&gt;.json: по нему монтаж ставит лапку
    /// и собирает звук игры синхронно с кадрами (Tools/AdVideo/compose.py).
    /// Запуск: <c>Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter AdCaptureTests</c>.
    /// </summary>
    [Explicit("съёмка рекламных роликов: build/adcapture/*.mp4|wav + Docs/Reports/ads/*.json")]
    public sealed class AdCaptureTests
    {
        string m_SaveDir;
        ManualClock m_Clock;
        AdRecorder m_Rec;
        OutpostController Game => GameBootstrap.OutpostController;

        /// <summary>Автосохранение раз в 2 с без fsync: запись не должна выбивать кадр из 33 мс.</summary>
        sealed class FastFiles : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);
            public void WriteAllBytesDurable(string p, byte[] d) => File.WriteAllBytes(p, d);
            public void Replace(string a, string b, string c) => m_Real.Replace(a, b, c);
            public void Move(string a, string b) => m_Real.Move(a, b);
            public void Copy(string a, string b) => m_Real.Copy(a, b);
            public void Delete(string p) => m_Real.Delete(p);
            public void CreateDirectory(string p) => m_Real.CreateDirectory(p);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SaveDir = Path.Combine(Application.temporaryCachePath, "AdCapture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_SaveDir);
            CameraRig.OverrideAspect = 9f / 16;
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;
            OutpostController.SaveDirectoryOverride = m_SaveDir;
            OutpostController.FileSystemOverride = new FastFiles();
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            m_Rec?.Dispose();
            m_Rec = null;
            Time.captureDeltaTime = 0;
            Application.targetFrameRate = 60;
            var empty = SceneManager.CreateScene("AdCaptureCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            OutpostController.FileSystemOverride = null;
            AudioListener.pause = false;
            if (Directory.Exists(m_SaveDir)) Directory.Delete(m_SaveDir, true);
        }

        // ───────────── общие шаги ─────────────

        IEnumerator WaitPlaying()
        {
            for (int f = 0; f < 3000 && Game.Phase != OutpostControllerPhase.Playing; f++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        /// <summary>Debug-профиль (LoadZone, SimSpeed, наборы) на шахте <paramref name="zone"/>.</summary>
        IEnumerator StartDebug(int zone)
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            if (zone != Game.State.Run.Zone)
            {
                Game.LoadZone(zone);
                yield return WaitPlaying();
            }
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
            Game.SetVolume(AudioGroup.Master, 1);
            Game.SetVolume(AudioGroup.Sfx, 1);
            Game.SetVolume(AudioGroup.Music, 0);
            Game.SetVolume(AudioGroup.Ambience, 0);
            Game.Hud.CloseAllWindows();
            m_Rec = new AdRecorder(Game, m_Clock);
            yield return m_Rec.Prepare();
        }

        /// <summary>Золото на покупку командой BonusGold (инвариант кошелька сохраняется); тост «Income doubled» не нужен.</summary>
        void GrantGold(System.Numerics.BigInteger amount)
        {
            if (amount.Sign <= 0) return;
            Assert.That(Game.Sim.Submit(OutpostCommand.BonusGold(amount)).Ok, Is.True);
            Game.Sim.Events.Clear();
            Game.Hud.RefreshNow();
        }

        /// <summary>
        /// Доплатить золото, чтобы покупка в режиме <paramref name="mode"/> взяла столько уровней, сколько позволяет предел
        /// (×1 — один, ×10 — десять). Пока золота мало, котировка даёт Levels = 0, поэтому доплата идёт по NextCost.
        /// </summary>
        void GrantFor(UpgradeBranch branch, BuyMode mode)
        {
            int want = mode == BuyMode.One ? 1 : 10;
            for (int i = 0; i < 80; i++)
            {
                var q = Game.Quote(branch, mode);
                if (q.BlockReason != null && q.BlockReason != "gold") return;
                if (q.Levels >= want || q.CurrentLevel + q.Levels >= q.Cap || q.NextCost == null) return;
                GrantGold(q.Levels == 0 ? q.NextCost.Value - Game.State.Run.Wallet : q.NextCost.Value);
            }
        }

        /// <summary>Купить уровень, доплатив недостающее золото; false — предел.</summary>
        bool BuyWithGrant(UpgradeBranch branch, BuyMode mode = BuyMode.One)
        {
            GrantFor(branch, mode);
            return Game.Quote(branch, mode).Levels > 0 && Game.Buy(branch, mode).Ok;
        }

        void BuyLevels(UpgradeBranch branch, int levels)
        {
            for (int i = 0; i < levels; i++) if (!BuyWithGrant(branch)) break;
        }

        Vector2 CampFocus() => new Vector2(Game.Board.CellCenter(Game.Map.SpawnCell).x, Game.Rig.MinZ);

        Vector2 TargetFocus(int id)
        {
            Assert.That(Game.TryGetTargetWorldPosition(id, out var w), Is.True);
            return new Vector2(w.x, w.z);
        }

        TargetState TargetStateOf(int id) => Game.State.Run.Targets.First(t => t.Id == id);

        double HpShare(int id)
        {
            var t = TargetStateOf(id);
            return t.Destroyed || t.MaxHpUnits <= 0 ? 0 : t.RemainingHpUnits / (double)t.MaxHpUnits;
        }

        Transform Named(string name) => Game.Hud.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Button DialogButton(string dialog, int index) =>
            Named(dialog).GetComponentsInChildren<Button>(true).Where(b => b.name == "btn").ElementAt(index);

        Button StoreTile(string id) => Game.Hud.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == id && t.parent != null && t.parent.name.StartsWith("grid_"))
            .GetComponentsInChildren<Button>(true).First(b => b.name == "tile");

        Button BaseBuy(UpgradeBranch branch) => Named("branch" + (int)branch).GetComponentsInChildren<Button>(true).First(b => b.name == "buy");

        /// <summary>Записать <paramref name="seconds"/> с, каждый кадр вызывая <paramref name="perFrame"/>(номер кадра).</summary>
        IEnumerator Shot(string name, double seconds, Action<int> perFrame = null, Func<int, bool> stop = null)
        {
            m_Rec.BeginShot(name);
            int frames = (int)Math.Round(seconds * AdRecorder.Fps);
            for (int f = 0; f < frames; f++)
            {
                perFrame?.Invoke(f);
                m_Rec.EndFrame();
                yield return null;
                if (stop != null && stop(f)) break;
            }
            yield return m_Rec.EndShot();
        }

        // ───────────── шахта 1: старт, тап, найм, панель Base, финал ─────────────

        [UnityTest, Timeout(1_800_000)]
        public IEnumerator Mine1()
        {
            yield return StartDebug(1);
            m_Rec.Focus(CampFocus());

            // 1. Старт: два котика выходят из лагеря к камням.
            yield return Shot("m1_start", 9, f => m_Rec.Focus(CampFocus()));

            // 2. Тап: дальний камень, к которому котики ещё не дошли, — кирка игрока одна.
            var targets = Game.Map.Targets.Where(t => Game.CanStrike(t.Id) && TargetFocus(t.Id).y > Game.Rig.MinZ).ToList();
            Assert.That(targets, Is.Not.Empty);
            int far = targets.OrderByDescending(t => TargetFocus(t.Id).y).First().Id;
            m_Rec.ZoomPixelPerfect(4);
            var farFocus = TargetFocus(far);
            yield return Shot("m1_tap", 10, f =>
            {
                m_Rec.Focus(farFocus + new Vector2(0f, -1.2f));
                if (f >= 15 && f % 12 == 3 && Game.CanStrike(far)) m_Rec.Tap(far, new Vector3(0f, 0f, 0.2f));
            });
            m_Rec.Zoom(null);

            // 3. Найм: котики один за другим выпрыгивают у доски найма.
            yield return Shot("m1_hire", 9, f =>
            {
                m_Rec.Focus(CampFocus());
                if (f >= 20 && f % 16 == 4 && f <= 20 + 16 * 10) { if (BuyWithGrant(UpgradeBranch.Team)) m_Rec.Mark("hire"); }
            });

            // 4. Панель Base: ×10, покупки нажатиями по кнопкам.
            Game.Hud.ShowBase();
            yield return Shot("m1_base", 10, f =>
            {
                if (f == 15) m_Rec.TapUi(Named("mode1").GetComponent<Button>());
                var order = new[] { UpgradeBranch.Pickaxes, UpgradeBranch.Boots, UpgradeBranch.Bags, UpgradeBranch.Unload, UpgradeBranch.Value, UpgradeBranch.Pickaxes };
                int k = (f - 45) / 36;
                if (f >= 45 && (f - 45) % 36 == 0 && k < order.Length)
                {
                    GrantFor(order[k], BuyMode.Ten);
                    m_Rec.TapUi(BaseBuy(order[k]));
                }
            });
            Game.Hud.CloseAllWindows();

            // 5. Финал шахты: расчистка без записи, затем последние мешки, сбор котиков, итоги, следующая шахта.
            yield return m_Rec.FastForwardUntil(() => Game.State.Run.Phase != RunPhase.Running, 900, 20);
            yield return m_Rec.FastForwardUntil(() => Game.State.Run.Phase == RunPhase.ReadyToContinue || Game.State.Run.Batches.Count <= 3, 120, 5);
            int resultsAt = -1, chestAt = -1;
            m_Rec.AutoCloseChests = false;
            yield return Shot("m1_clear", 26, f =>
            {
                m_Rec.Focus(CampFocus());
                if (chestAt < 0 && Game.Hud.ChestRewardOpen) { chestAt = f; m_Rec.Mark("exit_chest"); }
                if (chestAt >= 0 && f == chestAt + 60 && Game.Hud.ChestRewardOpen) Game.Hud.HandleBack();
                if (resultsAt < 0 && Game.Hud.ResultsOpen) { resultsAt = f; m_Rec.Mark("results"); }
                if (resultsAt >= 0 && f == resultsAt + 75) m_Rec.TapUi(DialogButton("Results", 1));
            }, f => resultsAt >= 0 && f >= resultsAt + 75 + 90);
            Assert.That(resultsAt, Is.GreaterThanOrEqualTo(0), "окно итогов шахты 1");
        }

        // ───────────── шахта 13 (кристаллы): армия, экипировка, ASMR, ворота; шахта 16 (медь) ─────────────

        [UnityTest, Timeout(1_800_000)]
        public IEnumerator Mine13()
        {
            yield return StartDebug(13);
            Game.GrantTestCards();
            yield return null;
            for (int i = 0; i < 5; i++) if (!Game.UpgradeCard("ca038").Ok) break;
            Game.GrantTestKit();
            yield return null;
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot))) Game.Unequip(slot);
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 6);
            BuyLevels(UpgradeBranch.Boots, 5);
            BuyLevels(UpgradeBranch.Bags, 5);
            BuyLevels(UpgradeBranch.Unload, 5);
            Game.Hud.CloseAllWindows();
            yield return m_Rec.FastForward(45, 5);

            // 1. Экипировка: котики в базовой одежде получают легендарные вещи — вспышка на каждом.
            m_Rec.ZoomPixelPerfect(4);
            var camp = CampFocus() + new Vector2(0f, 2.5f);
            var items = new[] { "pickaxe.legendary", "hat.legendary", "backpack.legendary", "boots.legendary" };
            yield return Shot("m13_glowup", 7, f =>
            {
                m_Rec.Focus(camp);
                if (f >= 30 && (f - 30) % 18 == 0 && (f - 30) / 18 < items.Length)
                {
                    Assert.That(Game.Equip(items[(f - 30) / 18]).Ok, Is.True);
                    m_Rec.Mark("equip");
                }
            });
            m_Rec.Zoom(null);
            yield return m_Rec.FastForward(20, 5);

            // 2. Армия в лагере: очередь у склада, монеты летят в счётчик.
            yield return Shot("m13_swarm_camp", 10, f => m_Rec.Focus(CampFocus()));

            // 3. Панорама по тоннелю от лагеря к фронту работ.
            var front = Game.State.Run.Workers.Where(w => w.Mode == WorkerMode.Mining)
                .Select(w => Game.TryGetWorkerWorldPosition(w.Id, out var p) ? new Vector2(p.x, p.z) : CampFocus())
                .DefaultIfEmpty(CampFocus()).OrderByDescending(p => p.y).First();
            var from = CampFocus();
            yield return Shot("m13_pan", 8, f => m_Rec.FocusEase(from, front, f, 8 * AdRecorder.Fps));

            // 4. ASMR: крупно камень, который долбят котики.
            var miner = Game.State.Run.Workers.FirstOrDefault(w => w.Mode == WorkerMode.Mining);
            Vector2 close = front;
            if (miner != null && Game.TryGetWorkerWorldPosition(miner.Id, out var mp)) close = new Vector2(mp.x, mp.z + 0.6f);
            m_Rec.ZoomPixelPerfect(6);
            yield return Shot("m13_asmr", 8, f => m_Rec.Focus(close));
            m_Rec.Zoom(null);

            // 5. Ворота: расчистка сектора без записи, затем удары котиков и игрока до открытия.
            if (Game.Map.Gates != null && Game.Map.Gates.Length > 0)
            {
                int gate = Game.Map.Gates.OrderBy(g => g.Order).First().TargetId;
                yield return m_Rec.FastForwardUntil(() => Game.Sim.CanStrike(gate) || Game.State.Run.GatesOpened > 0, 1200, 20);
                yield return m_Rec.FastForwardUntil(() => HpShare(gate) < 0.3 || Game.State.Run.GatesOpened > 0, 900, 5);
                var gateFocus = TargetFocus(gate) + new Vector2(0f, -1.5f);
                int openedAt = -1;
                yield return Shot("m13_gate", 20, f =>
                {
                    m_Rec.Focus(gateFocus);
                    if (openedAt < 0 && Game.State.Run.GatesOpened > 0) { openedAt = f; m_Rec.Mark("gate_open"); }
                    if (openedAt < 0 && f % 12 == 5 && Game.CanStrike(gate)) m_Rec.Tap(gate, new Vector3(0f, 0f, 0.3f));
                }, f => openedAt >= 0 && f >= openedAt + 75);
            }

            // 6. Шахта 16 (медная пещера): та же армия, другой цвет пещеры.
            Game.LoadZone(16);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 6);
            BuyLevels(UpgradeBranch.Boots, 5);
            BuyLevels(UpgradeBranch.Bags, 5);
            yield return m_Rec.Settle();
            yield return m_Rec.FastForward(60, 5);
            yield return Shot("m16_swarm", 9, f => m_Rec.Focus(CampFocus() + new Vector2(0f, 3f)));
        }

        // ───────────── шахта 100: общий план, большие числа, сундук в породе, сундук магазина, офлайн, тайм-скип ─────────────

        [UnityTest, Timeout(1_800_000)]
        public IEnumerator Mine100()
        {
            yield return StartDebug(100);
            Game.GrantTestKit();
            yield return null;
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 10);
            BuyLevels(UpgradeBranch.Boots, 8);
            BuyLevels(UpgradeBranch.Bags, 8);
            BuyLevels(UpgradeBranch.Unload, 8);
            BuyLevels(UpgradeBranch.Value, 10);
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();

            // 1. Общий план всей шахты без HUD: камера отъезжает и опускается к лагерю.
            m_Rec.Hud(false);
            float top = Game.Rig.MaxZ, bottom = Game.Rig.MinZ, midX = (Game.Rig.MinX + Game.Rig.MaxX) * 0.5f;
            yield return Shot("m100_overview", 7, f =>
            {
                float t = f / (7f * AdRecorder.Fps - 1);
                float e = t * t * (3f - 2f * t);
                m_Rec.Zoom(Mathf.Lerp(30f, 10f, e));
                m_Rec.Focus(new Vector2(midX, Mathf.Lerp(top, bottom, e)));
            });
            m_Rec.Zoom(null);
            m_Rec.Hud(true);

            // 2. Большие числа: золото копится, лагерь в работе.
            yield return m_Rec.FastForward(150, 20);
            yield return Shot("m100_swarm", 10, f => m_Rec.Focus(CampFocus() + new Vector2(0f, 1.5f)));

            // 3. Сундук в породе: котики и игрок добивают его — окно с новыми карточками.
            var chests = Game.Map.Targets.Where(t => PresentationCatalog.RockVisual(Game.Balance, t.TypeId) == RockVisualKind.Chest).Select(t => t.Id).ToList();
            m_Rec.Mark("chest_rocks:" + chests.Count);
            int chest = -1;
            yield return m_Rec.FastForwardUntil(() =>
            {
                chest = chests.Where(id => !TargetStateOf(id).Destroyed && Game.Sim.CanStrike(id) && HpShare(id) < 0.97).DefaultIfEmpty(-1).First();
                return chest >= 0;
            }, 1500, 5);
            Game.Hud.CloseAllWindows();
            if (chest >= 0)
            {
                var chestFocus = TargetFocus(chest) + new Vector2(0f, -1.2f);
                m_Rec.ZoomPixelPerfect(4);
                m_Rec.AutoCloseChests = false;
                int windowAt = -1;
                yield return Shot("m100_chest", 16, f =>
                {
                    m_Rec.Focus(chestFocus);
                    if (windowAt < 0 && Game.Hud.ChestRewardOpen) { windowAt = f; m_Rec.Mark("chest_window"); }
                    if (windowAt < 0 && f % 12 == 5 && Game.CanStrike(chest)) m_Rec.Tap(chest, new Vector3(0f, 0f, 0.3f));
                }, f => windowAt >= 0 && f >= windowAt + 120);
                m_Rec.Zoom(null);
                m_Rec.AutoCloseChests = true;
                Game.Hud.CloseAllWindows();
            }

            // 4. Деревянный сундук магазина (за рекламу, бесплатно): окно карточек.
            Game.Hud.ShowStore();
            m_Rec.AutoCloseChests = false;
            yield return Shot("m100_store_chest", 8, f =>
            {
                if (f == 30) m_Rec.TapUi(StoreTile("chest_wooden"));
            });
            m_Rec.AutoCloseChests = true;
            Game.Hud.CloseAllWindows();
            Game.Hud.HideStore();

            // 5. Покупка тайм-скипа 30 минут (до офлайна: на завершённой шахте скип недоступен).
            Game.State.Profile.Gems = System.Math.Max(Game.State.Profile.Gems, 150);
            Game.Hud.ShowStore();
            Game.Hud.RefreshNow();
            int skipAt = -1;
            yield return Shot("m100_skip", 12, f =>
            {
                if (f == 30) m_Rec.TapUi(StoreTile("skip_4h"));
                if (f == 60 && Game.Hud.ModalOpen) m_Rec.TapUi(DialogButton("Modal", 1));
                if (skipAt < 0 && Game.Hud.SkipResultOpen) { skipAt = f; m_Rec.Mark("skip_result"); }
            }, f => skipAt >= 0 && f >= skipAt + 90);
            Game.Hud.CloseAllWindows();
            Game.Hud.HideStore();

            // 6. «Пока вас не было»: часы уходят на 3 ч, окно с золотом, «Double income».
            yield return m_Rec.Settle();
            m_Rec.BeginShot("m100_offline");
            m_Rec.JumpClock(3 * 3600);
            int summaryAt = -1;
            for (int f = 0; f < 12 * AdRecorder.Fps; f++)
            {
                if (summaryAt < 0 && Game.Hud.OfflineOpen) { summaryAt = f; m_Rec.Mark("offline_summary"); }
                if (summaryAt >= 0 && f == summaryAt + 60) m_Rec.TapUi(DialogButton("Offline", 0));
                m_Rec.EndFrame();
                yield return null;
                if (summaryAt >= 0 && f >= summaryAt + 60 + 60) break;
            }
            yield return m_Rec.EndShot();
            Game.Hud.CloseAllWindows();
        }

        // ───────────── пересъёмка: офлайн на незавершённой шахте, ворота с начала удара ─────────────

        [UnityTest, Timeout(1_800_000)]
        public IEnumerator Extras()
        {
            yield return StartDebug(100);
            Game.GrantTestKit();
            yield return null;
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 10);
            BuyLevels(UpgradeBranch.Boots, 8);
            BuyLevels(UpgradeBranch.Bags, 8);
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();
            yield return m_Rec.FastForward(60, 20);

            // 1. «Пока вас не было»: шахта ещё идёт, часы уходят на 3 ч, окно с золотом, «Double income».
            m_Rec.BeginShot("m100_offline");
            m_Rec.JumpClock(3 * 3600);
            int summaryAt = -1;
            for (int f = 0; f < 12 * AdRecorder.Fps; f++)
            {
                if (summaryAt < 0 && Game.Hud.OfflineOpen) { summaryAt = f; m_Rec.Mark("offline_summary"); }
                if (summaryAt >= 0 && f == summaryAt + 60) m_Rec.TapUi(DialogButton("Offline", 0));
                m_Rec.EndFrame();
                yield return null;
                if (summaryAt >= 0 && f >= summaryAt + 60 + 60) break;
            }
            yield return m_Rec.EndShot();
            Game.Hud.CloseAllWindows();

            // 2. Ворота шахты 13: запись с первых ударов команды до открытия сектора.
            Game.LoadZone(13);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 4);
            BuyLevels(UpgradeBranch.Boots, 5);
            BuyLevels(UpgradeBranch.Bags, 5);
            yield return m_Rec.Settle();
            int gate = Game.Map.Gates.OrderBy(g => g.Order).First().TargetId;
            yield return m_Rec.FastForwardUntil(() => Game.Sim.CanStrike(gate) || Game.State.Run.GatesOpened > 0, 1200, 20);
            yield return m_Rec.FastForwardUntil(() => HpShare(gate) < 0.97 || Game.State.Run.GatesOpened > 0, 120, 1);
            var gateFocus = TargetFocus(gate) + new Vector2(0f, -1.5f);
            int openedAt = -1;
            yield return Shot("m13_gate", 30, f =>
            {
                m_Rec.Focus(gateFocus);
                if (openedAt < 0 && Game.State.Run.GatesOpened > 0) { openedAt = f; m_Rec.Mark("gate_open"); }
                if (openedAt < 0 && f % 12 == 5 && Game.CanStrike(gate)) m_Rec.Tap(gate, new Vector3(0f, 0f, 0.3f));
            }, f => openedAt >= 0 && f >= openedAt + 90);
        }

        // ───────────── карточки и экипировка (шахта 138: открыты все 61 карточки) ─────────────

        [UnityTest, Timeout(1_800_000)]
        public IEnumerator Collection()
        {
            yield return StartDebug(138);
            Game.GrantTestCards();
            Game.GrantTestKit();
            yield return null;
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();

            // 1. Коллекция: плавная прокрутка всех карточек.
            Game.Hud.ShowCards();
            yield return m_Rec.Settle();
            var scroll = Game.Hud.GetComponentsInChildren<ScrollRect>().First(s => s.gameObject.activeInHierarchy);
            scroll.verticalNormalizedPosition = 1f;
            yield return Shot("cards_scroll", 9, f =>
            {
                float t = Mathf.Clamp01((f - 20) / (8f * AdRecorder.Fps - 20));
                scroll.verticalNormalizedPosition = 1f - t * t * (3f - 2f * t);
            });

            // 2. Улучшение карточки.
            var def = Game.Configs.Cards.Cards.Where(d => CardService.Quote(Game.State.Profile, d).CanUpgrade)
                .OrderByDescending(d => d.Rarity).FirstOrDefault();
            if (def != null)
            {
                Game.Hud.ShowCard(def.Id);
                yield return Shot("cards_upgrade", 7, f =>
                {
                    if (f == 40 || f == 110)
                    {
                        var b = Game.Hud.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "cardUpgrade");
                        if (b != null && b.interactable) m_Rec.TapUi(b);
                    }
                });
            }
            Game.Hud.CloseAllWindows();

            // 3. Экипировка: легендарная кирка, улучшения.
            Game.Hud.ShowEquipment();
            Game.Hud.ShowItem("pickaxe.legendary");
            yield return Shot("equip_upgrade", 7, f =>
            {
                if (f == 35 || f == 80 || f == 125)
                {
                    var b = Game.Hud.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "upgrade" && x.gameObject.activeInHierarchy);
                    if (b != null && b.interactable) m_Rec.TapUi(b);
                }
            });
            Game.Hud.CloseAllWindows();
        }

        // ───────────── заводы (ТЗ 2.2, ролик 4): цепи, «Deliver», трубы, рынок, автодоставка по карточке ─────────────

        FactoryOverlay Overlay => Game.Input.Factories;

        Vector2 FactoryFocus(int id)
        {
            Assert.That(Game.TryGetFactoryWorldPosition(id, out var w), Is.True);
            return new Vector2(w.x, w.z);
        }

        FactoryState FactoryOf(int id) => Game.State.Run.Factories.First(f => f.TargetId == id);

        int CardLevel(string id) => Game.State.Profile.Cards.TryGetValue(id, out var c) ? c.Level : 0;

        Button BottomButton(string name) => Game.Hud.GetComponentsInChildren<Button>(true)
            .Single(b => b.name == name && b.transform.parent != null && b.transform.parent.name == "BottomBar");

        Button SellAll() => Game.Hud.GetComponentsInChildren<Transform>(true)
            .First(t => t.name == "Market" && t.parent != null && t.parent.name == "SafeArea")
            .GetComponentsInChildren<Button>(true).First(b => b.name == "sellAll");

        /// <summary>Нажать «Deliver» завода лапкой (кнопка должна быть на экране); false — кнопки нет.</summary>
        bool TapDeliver(int id)
        {
            var b = Overlay.DeliverButtonOf(id);
            if (b == null) return false;
            m_Rec.TapUi(b);
            m_Rec.Mark("deliver:" + id);
            return true;
        }

        /// <summary>Завод с наибольшим числом соседних заводов (полоса ±4 клетки по x, ±2 по z) — кадр «много заводов».</summary>
        List<int> FactoryCluster(int max)
        {
            var at = Game.Map.Factories.Select(f => (id: f.TargetId, p: FactoryFocus(f.TargetId))).ToList();
            var best = at.OrderByDescending(a => at.Count(b => Mathf.Abs(b.p.x - a.p.x) <= 4.5f && Mathf.Abs(b.p.y - a.p.y) <= 2.5f))
                .ThenBy(a => a.id).First();
            return at.Where(b => Mathf.Abs(b.p.x - best.p.x) <= 4.5f && Mathf.Abs(b.p.y - best.p.y) <= 2.5f)
                .OrderBy(b => Vector2.Distance(b.p, best.p)).Take(max).OrderBy(b => b.p.x).Select(b => b.id).ToList();
        }

        Vector2 Center(IEnumerable<int> ids)
        {
            var ps = ids.Select(FactoryFocus).ToList();
            return new Vector2(ps.Average(p => p.x), ps.Average(p => p.y));
        }

        [UnityTest, Timeout(2_400_000)]
        public IEnumerator Factories()
        {
            // 1. Шахта 10: команда и лапка разбивают цепи завода — разлёт звеньев, тост «… unlocked!», труба к складу.
            yield return StartDebug(10);
            BuyLevels(UpgradeBranch.Team, 6);
            BuyLevels(UpgradeBranch.Pickaxes, 4);
            BuyLevels(UpgradeBranch.Boots, 3);
            BuyLevels(UpgradeBranch.Bags, 2);
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();
            int fid = -1;
            yield return m_Rec.FastForwardUntil(() =>
            {
                fid = Game.Map.Factories.Where(f => FactoryOf(f.TargetId).Status == FactoryStatus.Locked && Game.Sim.CanStrike(f.TargetId)
                        && HpShare(f.TargetId) < 0.95)
                    .OrderBy(f => HpShare(f.TargetId)).Select(f => f.TargetId).DefaultIfEmpty(-1).First();
                return fid >= 0;
            }, 1500, 5);
            Assert.That(fid, Is.GreaterThanOrEqualTo(0), "котики бьют цепи завода шахты 10");
            yield return m_Rec.FastForwardUntil(() => HpShare(fid) < 0.7 || FactoryOf(fid).Status != FactoryStatus.Locked, 600, 2);
            Assert.That(FactoryOf(fid).Status, Is.EqualTo(FactoryStatus.Locked), "цепи ещё целы к началу записи");
            Game.Hud.CloseAllWindows();
            m_Rec.ZoomPixelPerfect(4);
            var chainsFocus = FactoryFocus(fid) + new Vector2(0f, -0.6f);
            int unlockedAt = -1;
            yield return Shot("f10_chains", 20, f =>
            {
                m_Rec.Focus(chainsFocus);
                if (unlockedAt < 0 && FactoryOf(fid).Status != FactoryStatus.Locked) { unlockedAt = f; m_Rec.Mark("unlocked"); }
                if (unlockedAt < 0 && f >= 10 && f % 12 == 5 && Game.CanStrike(fid)) m_Rec.Tap(fid, new Vector3(0f, 0f, 0.3f));
            }, f => unlockedAt >= 0 && f >= unlockedAt + 135);
            Assert.That(unlockedAt, Is.GreaterThanOrEqualTo(0), "цепи разбиты в кадре");
            m_Rec.Zoom(null);

            // 2. Цикл завода (полоса над зданием), «Deliver ×N» лапкой, партия едет по трубе на склад во дворе.
            yield return m_Rec.FastForwardUntil(() => FactoryOf(fid).Status == FactoryStatus.Ready, 900, 2);
            Assert.That(FactoryOf(fid).Status, Is.EqualTo(FactoryStatus.Ready), "партия готова");
            Game.Hud.CloseAllWindows();
            var factoryView = FactoryFocus(fid) + new Vector2(0f, -1.5f);
            m_Rec.Focus(factoryView);
            yield return m_Rec.Settle();
            int sentAt = -1, arrivedAt = -1;
            long arrivedBefore = Game.State.Run.GoodsArrived.Values.Sum();
            yield return Shot("f10_deliver", 14, f =>
            {
                if (f == 60 && TapDeliver(fid)) sentAt = f;
                if (sentAt >= 0 && arrivedAt < 0 && Game.State.Run.GoodsArrived.Values.Sum() > arrivedBefore) { arrivedAt = f; m_Rec.Mark("arrived"); }
                if (sentAt < 0 || f < sentAt + 6) m_Rec.Focus(factoryView);
                else m_Rec.FocusEase(factoryView, CampFocus() + new Vector2(0f, 1.5f), f - sentAt - 6, 75);
            }, f => arrivedAt >= 0 && f >= arrivedAt + 45 && f >= sentAt + 240);
            Assert.That(sentAt, Is.GreaterThanOrEqualTo(0), "кнопка «Deliver» нажата");

            // 3. Шахта 3: карточка «Dairy Foreman» на уровень, который просит завод, — завод переходит на автодоставку.
            Game.LoadZone(3);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            Game.GrantTestCards();
            yield return null;
            BuyLevels(UpgradeBranch.Team, 4);
            BuyLevels(UpgradeBranch.Pickaxes, 3);
            Game.DebugUnlockFactories();
            yield return null;
            var dairy = Game.Map.Factories.OrderByDescending(f => f.RequiredCardLevel).First(f => f.RequiredCardLevel >= 2);
            string card = dairy.RequiredCardId;
            for (int i = 0; i < 12 && CardLevel(card) < dairy.RequiredCardLevel - 1; i++) if (!Game.UpgradeCard(card).Ok) break;
            Assert.That(CardLevel(card), Is.EqualTo(dairy.RequiredCardLevel - 1), "карточке не хватает одного уровня");
            Assert.That(FactoryOf(dairy.TargetId).Automated, Is.False);
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();
            yield return m_Rec.FastForward(20, 2);
            Game.Hud.ShowCard(card);
            yield return m_Rec.Settle();
            int autoAt = -1;
            yield return Shot("f3_card", 7, f =>
            {
                if (f == 45)
                {
                    var b = Game.Hud.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "cardUpgrade");
                    if (b != null && b.interactable) m_Rec.TapUi(b);
                }
                if (autoAt < 0 && FactoryOf(dairy.TargetId).Automated) { autoAt = f; m_Rec.Mark("automated"); }
            });
            Assert.That(FactoryOf(dairy.TargetId).Automated, Is.True, "карточка нужного уровня включила автодоставку");
            Game.Hud.CloseAllWindows();

            // 4. Тот же завод сам отправляет партии: значок AUTO, капсулы по трубе без нажатий.
            var dairyView = FactoryFocus(dairy.TargetId) + new Vector2(0f, -1.5f);
            m_Rec.Focus(dairyView);
            yield return m_Rec.Settle();
            yield return Shot("f3_auto", 14, f =>
            {
                if (f < 150) m_Rec.Focus(dairyView);
                else m_Rec.FocusEase(dairyView, CampFocus() + new Vector2(0f, 1.5f), f - 150, 90);
            });

            // 5. Шахта 30: три соседних завода готовы — лапка нажимает «Deliver» у каждого; затем рынок и «Sell all».
            Game.LoadZone(30);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            BuyLevels(UpgradeBranch.Team, 8);
            BuyLevels(UpgradeBranch.Pickaxes, 4);
            Game.DebugUnlockFactories();
            yield return null;
            Game.DebugFinishFactoryCycles();
            yield return null;
            var trio = FactoryCluster(3);
            Assert.That(trio.Count, Is.GreaterThanOrEqualTo(2), "соседние заводы шахты 30");
            var trioView = Center(trio) + new Vector2(0f, -1.2f);
            m_Rec.Focus(trioView);
            yield return m_Rec.Settle();
            yield return Shot("f30_deliver", 8, f =>
            {
                m_Rec.Focus(trioView);
                for (int k = 0; k < trio.Count; k++) if (f == 24 + 16 * k) TapDeliver(trio[k]);
            });
            foreach (var mf in Game.Map.Factories) if (FactoryOf(mf.TargetId).Status == FactoryStatus.Ready) Overlay.ClickDeliver(mf.TargetId);
            yield return m_Rec.FastForwardUntil(() => Game.State.Run.PipeBatches.Count == 0, 120, 2);
            Game.Hud.CloseAllWindows();
            m_Rec.Focus(CampFocus() + new Vector2(0f, 1.5f));
            yield return m_Rec.Settle();
            int soldAt = -1;
            yield return Shot("f30_market", 9, f =>
            {
                if (f == 15) m_Rec.TapUi(BottomButton("Market"));
                if (f == 75) { var b = SellAll(); if (b.interactable) m_Rec.TapUi(b); }
                if (soldAt < 0 && Game.State.Run.GoodsSoldGold.Sign > 0) { soldAt = f; m_Rec.Mark("sold"); }
            }, f => soldAt >= 0 && f >= soldAt + 75);
            Assert.That(soldAt, Is.GreaterThanOrEqualTo(0), "товар продан");
            Game.Hud.CloseAllWindows();

            // 6. Шахта 140: 15 заводов на автодоставке — сеть труб, капсулы, склад, рынок с большими суммами.
            Game.LoadZone(140);
            yield return WaitPlaying();
            Game.Hud.CloseAllWindows();
            Game.GrantTestKit();
            yield return null;
            BuyLevels(UpgradeBranch.Team, 10);
            BuyLevels(UpgradeBranch.Pickaxes, 6);
            BuyLevels(UpgradeBranch.Boots, 6);
            BuyLevels(UpgradeBranch.Bags, 6);
            Game.DebugUnlockFactories();
            yield return null;
            Game.DebugAutomateAll();
            yield return null;
            Game.Hud.CloseAllWindows();
            yield return m_Rec.Settle();
            yield return m_Rec.FastForward(150, 5);
            Game.Hud.CloseAllWindows();

            m_Rec.Hud(false);
            float top = Game.Rig.MaxZ, bottom = Game.Rig.MinZ, midX = (Game.Rig.MinX + Game.Rig.MaxX) * 0.5f;
            yield return Shot("f140_overview", 8, f =>
            {
                float t = f / (8f * AdRecorder.Fps - 1);
                float e = t * t * (3f - 2f * t);
                m_Rec.Zoom(Mathf.Lerp(26f, 10f, e));
                m_Rec.Focus(new Vector2(midX, Mathf.Lerp(top, bottom, e)));
            });
            m_Rec.Zoom(null);
            m_Rec.Hud(true);

            var cluster = FactoryCluster(4);
            var clusterView = Center(cluster) + new Vector2(0f, -1.2f);
            m_Rec.Focus(clusterView);
            yield return m_Rec.Settle();
            yield return Shot("f140_factories", 10, f => m_Rec.Focus(clusterView));

            m_Rec.ZoomPixelPerfect(4);
            var yard = CampFocus() + new Vector2(1.5f, 1.2f);
            m_Rec.Focus(yard);
            yield return m_Rec.Settle();
            yield return Shot("f140_yard", 10, f => m_Rec.Focus(yard));
            m_Rec.Zoom(null);

            Game.Hud.CloseAllWindows();
            m_Rec.Focus(CampFocus() + new Vector2(0f, 1.5f));
            yield return m_Rec.Settle();
            soldAt = -1;
            ScrollRect list = null;
            yield return Shot("f140_market", 11, f =>
            {
                if (f == 15) m_Rec.TapUi(BottomButton("Market"));
                if (f == 30) list = Game.Hud.GetComponentsInChildren<ScrollRect>().FirstOrDefault(x => x.gameObject.activeInHierarchy);
                // Список 15 товаров: вниз и обратно вверх, к «Sell all».
                if (list != null && f >= 30 && f < 150)
                    list.verticalNormalizedPosition = f < 90 ? 1f - Mathf.SmoothStep(0f, 1f, (f - 30) / 60f) : Mathf.SmoothStep(0f, 1f, (f - 90) / 60f);
                if (f == 165) { var b = SellAll(); if (b.interactable) m_Rec.TapUi(b); }
                if (soldAt < 0 && f > 165 && Game.State.Run.GoodsSoldGold.Sign > 0) { soldAt = f; m_Rec.Mark("sold"); }
            }, f => soldAt >= 0 && f >= soldAt + 90);
            Assert.That(soldAt, Is.GreaterThanOrEqualTo(0), "товар шахты 140 продан");
            Game.Hud.CloseAllWindows();
        }
    }

    /// <summary>
    /// Запись кадров для рекламы: 30 fps по часам игры (не по стене), AsyncGPUReadback каждого кадра в
    /// endContextRendering → ffmpeg (stdin, rawvideo) → mp4; звуковые сигналы, события, тапы и метки — JSON.
    /// </summary>
    public sealed class AdRecorder : IDisposable
    {
        public const int W = 1080, H = 1920, Fps = 30, FrameBytes = W * H * 4, PipeChunk = 1 << 16, MaxPending = 40;
        static readonly byte[] s_Missing = new byte[0];
        static readonly Regex s_Debug = new Regex(@"\s*<color=#FF9070>\[DEBUG\]</color>");

        static string Root => Path.GetFullPath(Path.Combine(PlayModeUtil.ReportsDir(), "..", ".."));
        static string OutDir => Path.Combine(Root, "build", "adcapture");
        static string MetaDir => Path.Combine(PlayModeUtil.ReportsDir(), "ads");
        static string Ffmpeg => Directory.GetFiles(Path.Combine(Root, "Tools", "AdVideo", ".venv", "Lib", "site-packages", "imageio_ffmpeg", "binaries"), "ffmpeg*.exe").First();

        readonly OutpostController m_Game;
        readonly ManualClock m_Clock;
        readonly long m_Utc0;
        readonly double m_Mono0;
        double m_T;
        readonly Camera m_Cam;
        readonly Canvas m_Canvas;
        readonly CanvasScaler m_Scaler;
        readonly RenderTexture m_Rt;
        CanvasGroup m_HudGroup;
        TMP_Text m_Sector;
        float m_DefaultOrtho;
        float? m_Zoom;
        object m_Rp;
        System.Reflection.PropertyInfo m_RenderScale;
        float m_OldScale = -1;
        Action m_Click, m_Open, m_Close, m_Denied;
        bool m_Subscribed;

        string m_Shot;
        bool m_On;
        int m_First, m_Requested, m_Done, m_Errors, m_Late;
        Process m_Ffmpeg;
        Stream m_Stdin;
        Thread m_Writer;
        readonly object m_Lock = new object();
        readonly SortedDictionary<int, byte[]> m_Pending = new SortedDictionary<int, byte[]>();
        readonly Stack<byte[]> m_Pool = new Stack<byte[]>();
        int m_NextWrite, m_FramesTotal = -1, m_Written;
        byte[] m_LastWritten;
        bool m_WriterFailed;
        readonly StringBuilder m_FfmpegLog = new StringBuilder();
        readonly List<object> m_Frames = new List<object>(), m_Events = new List<object>();

        public AdRecorder(OutpostController game, ManualClock clock)
        {
            m_Game = game;
            m_Clock = clock;
            m_Utc0 = clock.UtcNowMs;
            m_Mono0 = clock.MonotonicSeconds;
            m_Cam = game.Rig.Cam;
            m_Canvas = game.Hud.GetComponent<Canvas>();
            m_Scaler = game.Hud.GetComponent<CanvasScaler>();
            m_Rt = new RenderTexture(W, H, 24) { filterMode = FilterMode.Point };
        }

        public IEnumerator Prepare()
        {
            Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True);
            Assert.That(File.Exists(Ffmpeg), Is.True, "ffmpeg: Tools/AdVideo/.venv (pip install imageio-ffmpeg)");
            Time.captureDeltaTime = 1f / Fps;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            var rp = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            m_Rp = rp;
            m_RenderScale = rp != null ? rp.GetType().GetProperty("renderScale") : null;
            if (m_RenderScale != null) { m_OldScale = (float)m_RenderScale.GetValue(rp); m_RenderScale.SetValue(rp, 1f); }
            UnityEngine.Random.InitState(20261013);
            m_Cam.targetTexture = m_Rt;
            m_Scaler.enabled = false;
            m_Canvas.scaleFactor = 1f;
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = m_Cam;
            m_Canvas.planeDistance = 1;
            m_HudGroup = m_Game.Hud.GetComponent<CanvasGroup>();
            if (m_HudGroup == null) m_HudGroup = m_Game.Hud.gameObject.AddComponent<CanvasGroup>();
            Hud(true);
            HideNode("SafeArea/Settings");
            m_Game.Hud.CloseAllWindows();
            m_Game.ClearSelection();
            Subscribe();
            m_Click = UiSound.Click; m_Open = UiSound.Open; m_Close = UiSound.Close; m_Denied = UiSound.Denied;
            UiSound.Click = () => { Log("ui", "click"); m_Click?.Invoke(); };
            UiSound.Open = () => { Log("ui", "open"); m_Open?.Invoke(); };
            UiSound.Close = () => { Log("ui", "close"); m_Close?.Invoke(); };
            UiSound.Denied = () => { Log("ui", "denied"); m_Denied?.Invoke(); };
            RenderPipelineManager.endContextRendering += OnRendered;
            yield return Settle();
            m_DefaultOrtho = m_Cam.orthographicSize;
        }

        void Subscribe()
        {
            if (m_Subscribed) return;
            m_Game.EventPresented += OnCore;
            m_Game.Board.Cue += OnCue;
            m_Subscribed = true;
        }

        /// <summary>Несколько кадров без записи: раскладка HUD, вставки камеры, прогрев шейдеров; пересчёт темпа.</summary>
        public IEnumerator Settle(int frames = 15)
        {
            for (int i = 0; i < frames; i++) { EndFrame(); yield return null; }
            m_Game.Hud.RefreshNow();
            Canvas.ForceUpdateCanvases();
            if (!m_Zoom.HasValue) m_DefaultOrtho = m_Cam.orthographicSize;
        }

        void Advance(double s)
        {
            m_T += s;
            m_Clock.MonotonicSeconds = m_Mono0 + m_T;
            m_Clock.UtcNowMs = m_Utc0 + (long)Math.Round(m_T * 1000.0);
        }

        /// <summary>Скачок часов (офлайн): контроллер сам посчитает отсутствие и покажет окно.</summary>
        public void JumpClock(double seconds) => Advance(seconds);

        /// <summary>Без записи и без темпа; шаг часов ≤ 1 с, иначе контроллер уйдёт в офлайн.</summary>
        public IEnumerator FastForward(double simSeconds, int speed)
        {
            m_Game.SimSpeed = speed;
            double step = 0.25;
            for (double t = 0; t < simSeconds; t += step * Math.Max(1, m_Game.SimSpeed)) { Advance(step); StripDebugMarker(); yield return null; }
            m_Game.SimSpeed = 1;
            m_Game.Hud.RefreshNow();
        }

        public IEnumerator FastForwardUntil(Func<bool> done, double maxSimSeconds, int speed)
        {
            m_Game.SimSpeed = speed;
            double step = 0.25;
            for (double t = 0; t < maxSimSeconds && !done(); t += step * Math.Max(1, m_Game.SimSpeed)) { Advance(step); StripDebugMarker(); yield return null; }
            m_Game.SimSpeed = 1;
            m_Game.Hud.RefreshNow();
        }

        /// <summary>Конец итерации кадра — вызывать прямо перед <c>yield return null</c>.</summary>
        /// <summary>Закрывать окно сундука, которое игра открывает сама (новая карточка из сундука шахты), — кроме кадров о сундуках.</summary>
        public bool AutoCloseChests = true;

        public void EndFrame()
        {
            if (AutoCloseChests && m_Game.Hud.ChestRewardOpen) m_Game.Hud.HandleBack();
            if (m_Zoom.HasValue && !Mathf.Approximately(m_Cam.orthographicSize, m_Zoom.Value)) { m_Cam.orthographicSize = m_Zoom.Value; Refocus(); }
            StripDebugMarker();
            if (m_On) m_Frames.Add(new { i = Time.frameCount - m_First, dt = Math.Round(Time.unscaledDeltaTime * 1000, 2), sim = m_Game.Sim != null ? m_Game.Sim.Now : -1,
                fx = Math.Round(m_Game.Rig.FocusX, 3), fz = Math.Round(m_Game.Rig.FocusZ, 3), ortho = Math.Round(m_Cam.orthographicSize, 3),
                gold = m_Game.State?.Run?.Wallet.ToString() });
            Advance(1.0 / Fps);
            WaitForWriter();
        }

        /// <summary>Не больше MaxPending кадров (по 8 МБ) в памяти: если кодер отстаёт, кадр ждёт его (кадр станет поздним).</summary>
        void WaitForWriter()
        {
            if (!m_On || m_Writer == null) return;
            lock (m_Lock)
                while (m_Pending.Count > MaxPending && m_Writer.IsAlive && !m_WriterFailed) Monitor.Wait(m_Lock, 20);
            if (m_WriterFailed) throw new InvalidOperationException("ffmpeg writer: " + m_FfmpegLog);
        }

        public void BeginShot(string name)
        {
            m_Shot = name;
            m_Frames.Clear(); m_Events.Clear();
            m_Requested = m_Done = m_Errors = m_Late = 0;
            m_NextWrite = 0; m_Written = 0; m_FramesTotal = -1; m_WriterFailed = false; m_LastWritten = null;
            m_Pending.Clear();
            m_FfmpegLog.Clear();
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(MetaDir);
            string mp4 = Path.Combine(OutDir, name + ".mp4");
            if (File.Exists(mp4)) File.Delete(mp4);
            var psi = new ProcessStartInfo(Ffmpeg,
                "-hide_banner -loglevel warning -y -f rawvideo -pix_fmt rgba -s " + W + "x" + H + " -r " + Fps + " -i - " +
                "-vf vflip -c:v libx264 -preset superfast -crf 10 -pix_fmt yuv444p -g 30 -movflags +faststart \"" + mp4 + "\"")
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
            m_Ffmpeg = Process.Start(psi);
            m_Ffmpeg.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (m_FfmpegLog) m_FfmpegLog.AppendLine(e.Data); };
            m_Ffmpeg.BeginErrorReadLine();
            m_Stdin = m_Ffmpeg.StandardInput.BaseStream;
            m_Writer = new Thread(WriterLoop) { IsBackground = true, Name = "AdCaptureWriter" };
            m_Writer.Start();
            m_First = Time.frameCount;
            m_On = true;
        }

        /// <summary>Пишет кадры в ffmpeg строго по порядку; пропуск (ошибка чтения) заменяется предыдущим кадром.</summary>
        void WriterLoop()
        {
            try
            {
                while (true)
                {
                    byte[] buf = null;
                    lock (m_Lock)
                    {
                        while (true)
                        {
                            if (m_Pending.TryGetValue(m_NextWrite, out buf)) { m_Pending.Remove(m_NextWrite); break; }
                            if (m_FramesTotal >= 0 && m_NextWrite >= m_FramesTotal) { buf = null; break; }
                            if (m_FramesTotal >= 0 && m_Done + m_Errors >= m_Requested)
                            { buf = m_LastWritten ?? new byte[FrameBytes]; break; }
                            Monitor.Wait(m_Lock, 50);
                        }
                    }
                    if (buf == null) break;
                    if (ReferenceEquals(buf, s_Missing)) buf = m_LastWritten ?? new byte[FrameBytes];
                    // Кусками: одна запись 8 МБ в канал Windows падает с ERROR_NO_SYSTEM_RESOURCES (1450).
                    for (int off = 0; off < FrameBytes; off += PipeChunk) m_Stdin.Write(buf, off, Math.Min(PipeChunk, FrameBytes - off));
                    lock (m_Lock)
                    {
                        if (m_LastWritten != null && !ReferenceEquals(m_LastWritten, buf)) m_Pool.Push(m_LastWritten);
                        m_LastWritten = buf;
                        m_NextWrite++;
                        m_Written++;
                        Monitor.PulseAll(m_Lock);
                    }
                }
            }
            catch (Exception e)
            {
                m_WriterFailed = true;
                lock (m_FfmpegLog) m_FfmpegLog.AppendLine("writer: " + e.Message);
            }
        }

        void OnRendered(ScriptableRenderContext ctx, List<Camera> cameras)
        {
            if (!m_On || !cameras.Contains(m_Cam)) return;
            int idx = Time.frameCount - m_First;
            m_Requested++;
            AsyncGPUReadback.Request(m_Rt, 0, TextureFormat.RGBA32, r =>
            {
                if (r.hasError) { lock (m_Lock) { m_Pending[idx] = s_Missing; m_Errors++; Monitor.PulseAll(m_Lock); } return; }
                byte[] buf;
                lock (m_Lock) buf = m_Pool.Count > 0 ? m_Pool.Pop() : new byte[FrameBytes];
                r.GetData<byte>().CopyTo(buf);
                lock (m_Lock) { m_Pending[idx] = buf; m_Done++; Monitor.PulseAll(m_Lock); }
            });
        }

        public IEnumerator EndShot()
        {
            m_On = false;
            int frames = Time.frameCount - m_First;
            AsyncGPUReadback.WaitAllRequests();
            for (int g = 0; g < 300 && m_Done + m_Errors < m_Requested; g++) yield return null;
            lock (m_Lock) { m_FramesTotal = frames; Monitor.PulseAll(m_Lock); }
            while (m_Writer.IsAlive) yield return null;
            try { m_Stdin.Flush(); m_Stdin.Close(); } catch (Exception e) { m_FfmpegLog.AppendLine("close: " + e.Message); }
            var sw = Stopwatch.StartNew();
            while (!m_Ffmpeg.HasExited && sw.Elapsed.TotalSeconds < 300) yield return null;
            int exit = m_Ffmpeg.HasExited ? m_Ffmpeg.ExitCode : -999;
            string log;
            lock (m_FfmpegLog) log = m_FfmpegLog.ToString();
            File.WriteAllText(Path.Combine(MetaDir, m_Shot + ".json"), OutpostJson.Serialize(new
            {
                shot = m_Shot, width = W, height = H, fps = Fps, frames, written = m_Written, requested = m_Requested, done = m_Done,
                readbackErrors = m_Errors, lateFrames = m_Late, ffmpegExit = exit, ffmpegLog = log, writerFailed = m_WriterFailed,
                unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], renderScaleWas = m_OldScale,
                zone = m_Game.State.Run.Zone, balance = m_Game.Configs.Balance.Version,
                frameLog = m_Frames, events = m_Events,
            }) + "\n");
            Debug.Log("[AdCapture] " + m_Shot + " frames=" + frames + " written=" + m_Written + " late=" + m_Late + " ffmpeg=" + exit);
            m_Ffmpeg.Dispose();
            m_Ffmpeg = null;
            Assert.That(exit, Is.Zero, "ffmpeg " + m_Shot + ": " + log);
            Assert.That(m_Written, Is.EqualTo(frames), "кадры " + m_Shot);
        }

        // ───────────── журнал (кадр f = Time.frameCount − m_First; событие Update кадра F видно на кадре F) ─────────────

        void Log(string kind, string name, object data = null)
        {
            if (m_On) m_Events.Add(new { f = Time.frameCount - m_First, kind, name, data });
        }

        Vector2 ToImage(Vector3 world)
        {
            var p = m_Cam.WorldToScreenPoint(world);
            return new Vector2(Mathf.Round(p.x), Mathf.Round(H - p.y));
        }

        void OnCore(OutpostEvent e) => Log("core", e.Type.ToString(),
            new { e.TargetId, e.WorkerId, e.Count, e.Branch, e.Slot, value = e.Value.ToString(), e.ItemId, e.Extra, e.Detail });

        void OnCue(OutpostViewCueArgs c)
        {
            var p = ToImage(c.Position);
            Log("sfx", c.Cue.ToString(), new { x = p.x, y = p.y, c.TargetId, c.WorkerId, c.Slot, c.Branch, chains = c.Chains, auto = c.Automatic, c.Count });
        }

        public void Mark(string name) => Log("mark", name);

        /// <summary>Удар кирки игрока по цели (как тап пальцем); точка касания — в журнал для лапки.</summary>
        public bool Tap(int targetId, Vector3 offset = default)
        {
            if (!m_Game.TryGetTargetWorldPosition(targetId, out var world)) return false;
            world += offset;
            var p = ToImage(world);
            bool ok = m_Game.BeginStrike(targetId, world);
            Log("tap", "strike", new { targetId, x = p.x, y = p.y, ok });
            return ok;
        }

        /// <summary>Нажатие кнопки HUD через систему событий; центр кнопки — в журнал для лапки.</summary>
        public void TapUi(Button b)
        {
            var rt = (RectTransform)b.transform;
            var p = RectTransformUtility.WorldToScreenPoint(m_Cam, rt.TransformPoint(rt.rect.center));
            Log("tap", "ui:" + b.name, new { x = Mathf.Round(p.x), y = Mathf.Round(H - p.y) });
            PlayModeUtil.Click(b);
        }

        // ───────────── камера и HUD ─────────────

        public void Focus(Vector2 xz) => m_Game.Rig.FocusOn(xz);

        public void FocusEase(Vector2 a, Vector2 b, int frame, int frames)
        {
            float t = Mathf.Clamp01(frame / (float)Mathf.Max(1, frames - 1));
            Focus(Vector2.Lerp(a, b, t * t * (3f - 2f * t)));
        }

        /// <summary>Целое число экранных пикселей на пиксель арта: ortho = 1920 / (2·32·k).</summary>
        public void ZoomPixelPerfect(int screenPxPerArtPx) => Zoom(H / (2f * PixelArt.CellPixels * screenPxPerArtPx));

        public void Zoom(float? ortho)
        {
            m_Zoom = ortho;
            m_Cam.orthographicSize = ortho ?? m_DefaultOrtho;
            Refocus();
        }

        void Refocus() => m_Game.Rig.FocusOn(new Vector2(m_Game.Rig.FocusX, m_Game.Rig.FocusZ));

        public void Hud(bool on) => m_HudGroup.alpha = on ? 1f : 0f;

        void HideNode(string path)
        {
            var t = m_Game.Hud.transform.Find(path);
            if (t == null) return;
            var g = t.GetComponent<CanvasGroup>();
            if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
        }

        void StripDebugMarker()
        {
            if (m_Sector == null) m_Sector = m_Game.Hud.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "sector");
            if (m_Sector != null && m_Sector.text.Contains("[DEBUG]")) m_Sector.text = s_Debug.Replace(m_Sector.text, "");
        }

        public void Dispose()
        {
            m_On = false;
            RenderPipelineManager.endContextRendering -= OnRendered;
            if (m_Game != null)
            {
                m_Game.EventPresented -= OnCore;
                if (m_Game.Board != null) m_Game.Board.Cue -= OnCue;
            }
            if (m_Ffmpeg != null) { try { if (!m_Ffmpeg.HasExited) m_Ffmpeg.Kill(); } catch (Exception) { } m_Ffmpeg.Dispose(); }
            UiSound.Click = m_Click; UiSound.Open = m_Open; UiSound.Close = m_Close; UiSound.Denied = m_Denied;
            if (m_RenderScale != null && m_OldScale > 0) m_RenderScale.SetValue(m_Rp, m_OldScale);
            Time.captureDeltaTime = 0;
            if (m_Cam != null) m_Cam.targetTexture = null;
            if (m_Canvas != null) { m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay; m_Canvas.worldCamera = null; }
            if (m_Scaler != null) m_Scaler.enabled = true;
            if (m_HudGroup != null) m_HudGroup.alpha = 1f;
            m_Rt.Release();
            UnityEngine.Object.Destroy(m_Rt);
        }
    }
}
