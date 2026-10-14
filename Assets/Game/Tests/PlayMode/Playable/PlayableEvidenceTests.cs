using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Playable;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Снимки playable для проверки глазами (Docs/Playable/PLAYABLE_TZ.md §3.3–3.5) — настоящие состояния одной сессии, без
    /// подставленных чисел: начало (панель квеста, реплика, лапка на камне с рудой), «QUEST COMPLETE!» после пяти ударов, лапка
    /// на «Базе» и на строке «Кирок» в её панели (баннер под окном притушен), дальше — игрок-бот: забирает награды, покупает
    /// по карману, выполняет квесты (надеть лучшее, карточка, сундук, тайм-скип) — так, как это сделал бы человек, часы
    /// идут шагами 0,75 с (онлайн, без офлайн-расчёта). По пути — «NEW CAT!», итоги шахты без надписи поверх, квест шахты 2+ с лапкой на
    /// «Экипировке»/«Магазине», «GATE OPEN!» или «PICKAXES ×2!», окно награды в шахте 2+; через 30 минут игры — финальный экран
    /// с настоящими итогами (9:16 и 9:20) и баннер после «Keep playing»; стрелка к цели за краем экрана — если встретится.
    /// Кадр 1080×1920 (и 1080×2400) вместе с HUD и Canvas рекламы; подписи, обрезанные многоточием, валят тест. Генератор, отдельно:
    /// <c>Tools/AdVideo/start_capture.ps1 -Filter "PlayableEvidenceTests"</c> → Docs/Reports/playable/screens/*.png.
    /// </summary>
    [Explicit("генератор снимков Docs/Reports/playable/screens/*.png")]
    public sealed class PlayableEvidenceTests : PlayableFixture
    {
        static string Shots => Path.Combine(PlayModeUtil.ReportsDir(), "playable", "screens");
        protected override float Aspect => 9f / 16;
        /// <summary>Шаг часов за кадр в игре бота: меньше секунды — без офлайн-расчёта.</summary>
        const double PlayStep = 0.75;
        /// <summary>Бот берётся за новый квест не сразу: лапка и реплика успевают показаться (как у человека).</summary>
        const double ReadSeconds = 2.0;
        readonly List<string> m_Truncated = new List<string>();
        readonly List<string> m_Taken = new List<string>();
        readonly List<string> m_Log = new List<string>();

        sealed class Shot
        {
            public string Name;
            public Func<bool> When;
            public bool Tall;
            /// <summary>Кадр бота, раньше которого снимок не повторяется (состояние не устояло в кадре 9:16).</summary>
            public int RetryFrame;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t.GetComponent<Canvas>() == null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Снимок 1080×<paramref name="height"/> вместе с HUD и Canvas рекламы (баннер, финальный экран — ближе к камере и выше по
        /// порядку); масштаб Canvas — как у CanvasScaler (match 0,5). <paramref name="still"/> — состояние снимка, проверяется
        /// уже в кадре снимка (раскладка 9:16 отличается от окна редактора: лапка или стрелка могут смениться): не выполнено —
        /// снимок не записывается.
        /// </summary>
        IEnumerator Capture(string name, int height = 1920, Func<bool> still = null)
        {
            var camera = Game.Rig.Cam;
            var canvases = new List<Canvas> { Game.Hud.GetComponent<Canvas>() };
            if (Cta != null && Cta.Canvas != null) canvases.Add(Cta.Canvas);
            var scales = canvases.Select(c => c.scaleFactor).ToArray();
            var texture = new RenderTexture(1080, height, 24) { filterMode = FilterMode.Point };
            var previous = camera.targetTexture;
            float capture = Time.captureDeltaTime;
            Time.captureDeltaTime = 0f;
            try
            {
                camera.targetTexture = texture;
                for (int i = 0; i < canvases.Count; i++)
                {
                    var canvas = canvases[i];
                    canvas.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1f - 0.1f * i;
                }
                yield return PlayModeUtil.WaitReal(0.35f);
                Game.Hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                if (still != null && !still())
                {
                    Debug.Log("[PlayableEvidence] " + name + ": state changed in the 9:16 frame, retry later");
                    yield break;
                }
                foreach (var canvas in canvases)
                    foreach (var t in canvas.GetComponentsInChildren<TMP_Text>())
                    {
                        if (string.IsNullOrEmpty(t.text)) continue;
                        t.ForceMeshUpdate();
                        if (t.isTextTruncated) m_Truncated.Add(name + "\t" + PathOf(t.transform) + "\t" + t.text);
                    }
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(1080, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1080, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = active;
                Directory.CreateDirectory(Shots);
                File.WriteAllBytes(Path.Combine(Shots, name), pixels.EncodeToPNG());
                UnityEngine.Object.Destroy(pixels);
                m_Taken.Add(name);
                Debug.Log("[PlayableEvidence] " + name + ": zone " + Game.State.Run.Zone + ", quest " + Quests.Zone + "/" + Quests.Index + " " +
                          Quests.Current?.Text + ", play " + Game.State.Profile.PlayTimeMs / 1000 + " s");
            }
            finally
            {
                camera.targetTexture = previous;
                for (int i = 0; i < canvases.Count; i++)
                {
                    var canvas = canvases[i];
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.worldCamera = null;
                    canvas.scaleFactor = scales[i];
                    canvas.GetComponent<CanvasScaler>().enabled = true;
                }
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                Time.captureDeltaTime = capture;
            }
        }

        /// <summary>Часы шагами <see cref="PlayStep"/> (в переходе и расчёте — кадрами), пока не <paramref name="done"/>; без бота.</summary>
        IEnumerator Wait(Func<bool> done, int frames)
        {
            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            try
            {
                for (int i = 0; i < frames && !done(); i++)
                {
                    m_Clock.Advance(Game.Phase == OutpostControllerPhase.Playing && !Game.InTransition ? PlayStep : FrameStep);
                    yield return null;
                }
            }
            finally { Time.captureDeltaTime = previous; }
        }

        [UnityTest]
        public IEnumerator RealPlayScreens()
        {
            Directory.CreateDirectory(Shots);
            foreach (string file in Directory.GetFiles(Shots, "*.png")) File.Delete(file);
            var quests = Quests;
            var juice = Juice;
            var cta = Cta;
            var hud = Game.Hud;

            // 01. Начало: панель квеста, реплика наставника, лапка на камне с рудой под панелью, баннер на месте.
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            yield return WaitFrames(() => quests.Guide.PawVisible && cta.Banner.Settled, 90, 0);
            Assert.That(quests.Guide.PawTarget, Is.GreaterThanOrEqualTo(0));
            Assert.That(Game.Map.Target(quests.Guide.PawTarget).Kind, Is.EqualTo('r'), "руда, не препятствие");
            yield return Capture("01-start-tracker-line-paw-rock.png");
            yield return Capture("01-start-tracker-line-paw-rock-2400.png", 2400);

            // 02. Пять ударов — «QUEST COMPLETE!»: шаг «Tap rocks 5/5», награда крупно, «CLAIM» готова.
            for (int i = 0; i < 16 && !quests.CurrentDone; i++) yield return StrikeOnce();
            yield return WaitFrames(() => quests.PopupOpen, 30, 0);
            Assert.That(quests.PopupOpen, Is.True);
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            Assert.That(quests.ClaimReady, Is.True);
            yield return Capture("02-quest-complete.png");
            yield return Capture("02-quest-complete-2400.png", 2400);
            quests.Claim();

            // 03. Квест «Кирок»: лапка на «Базе» (награда оплачивает уровень).
            yield return Advance(PlayableQuests.NextQuestSeconds + 0.25);
            var baseButton = hud.Hint.FindTarget(OutpostHud.TargetBase);
            yield return WaitFrames(() => quests.Guide.PawUi == baseButton, 60);
            Assert.That(quests.Guide.PawUi, Is.SameAs(baseButton));
            yield return Capture("03-paw-base-button.png");

            // 04. «База» открыта: лапка на кнопке покупки «Кирок» у её правого нижнего угла, баннер притушен под окном.
            hud.ShowBase();
            var row = hud.Hint.FindTarget(OutpostHud.BranchTarget(UpgradeBranch.Pickaxes));
            yield return WaitFrames(() => quests.Guide.PawUi == row && cta.Banner.Dimmed, 60);
            Assert.That(quests.Guide.PawUi, Is.SameAs(row));
            Assert.That(cta.Banner.Dimmed, Is.True);
            yield return Capture("04-base-open-paw-row-banner-dimmed.png");
            Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True, "уровень на золото награды");
            hud.HideBase();

            // Дальше — игра бота до финального экрана; снимки — когда состояние наступило само.
            int zoneStart = Game.State.Run.Zone;
            var shots = new List<Shot>
            {
                new Shot { Name = "05-juice-new-cat.png", When = () => juice.Visible && juice.Text != null && juice.Text.Contains("CAT") && !quests.PopupOpen && !hud.AnyWindowOpen },
                // Итоги шахты отмечают её конец сами: «MINE CLEARED!» поверх них больше не рисуется.
                new Shot { Name = "06-mine-results-no-juice.png", When = () => hud.ResultsOpen && !juice.Visible },
                new Shot
                {
                    Name = "07-mine2-quest-paw.png",
                    When = () => Game.State.Run.Zone > zoneStart && quests.Revealed && !quests.CurrentDone && quests.Guide.PawUi != null && !hud.AnyWindowOpen &&
                                 (quests.Guide.PawUi == hud.Hint.FindTarget(OutpostHud.TargetEquipment) || quests.Guide.PawUi == hud.Hint.FindTarget(OutpostHud.TargetStore)),
                },
                new Shot
                {
                    Name = "08-paw-store.png",
                    When = () => quests.Revealed && !quests.CurrentDone && quests.Guide.PawUi == hud.Hint.FindTarget(OutpostHud.TargetStore) && !hud.AnyWindowOpen,
                },
                new Shot
                {
                    Name = "09-juice-gate-or-power.png",
                    When = () => juice.Visible && (juice.Text == Loc.T("playable.juice.gate") || juice.Text != null && juice.Text.StartsWith("PICKAXES")),
                },
                new Shot { Name = "10-quest-complete-mine2plus.png", When = () => quests.ClaimReady && Game.State.Run.Zone > zoneStart, Tall = true },
                new Shot { Name = "13-edge-arrow.png", When = () => quests.Guide.ArrowVisible && !hud.AnyWindowOpen && !quests.PopupOpen },
            };

            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            double questSince = double.NaN, nextBuy = 0;
            QuestDef seen = null;
            int frame = 0;
            try
            {
                for (; frame < 6000 && !cta.EndCardOpen; frame++)
                {
                    foreach (var shot in shots)
                    {
                        if (m_Taken.Contains(shot.Name) || frame < shot.RetryFrame || !shot.When()) continue;
                        yield return Capture(shot.Name, 1920, shot.When);
                        if (!m_Taken.Contains(shot.Name)) shot.RetryFrame = frame + 40;
                        if (shot.Tall && m_Taken.Contains(shot.Name)) yield return Capture(shot.Name.Replace(".png", "-2400.png"), 2400);
                    }
                    double now = m_Clock.MonotonicSeconds;
                    if (quests.Current != seen || !quests.Revealed)
                    {
                        seen = quests.Current;
                        questSince = quests.Revealed ? now : double.NaN;
                    }
                    else if (double.IsNaN(questSince)) questSince = now;
                    bool ready = !double.IsNaN(questSince) && now - questSince >= ReadSeconds;
                    if (BotStep(ready, now >= nextBuy)) nextBuy = now + 1.5;
                    else if (now >= nextBuy) nextBuy = now + 1.5;
                    m_Clock.Advance(Game.Phase == OutpostControllerPhase.Playing && !Game.InTransition ? PlayStep : FrameStep);
                    yield return null;
                }
            }
            finally { Time.captureDeltaTime = previous; }

            var profile = Game.State.Profile;
            string summary = "frames " + frame + ", play " + profile.PlayTimeMs / 1000 + " s, mines cleared " + profile.CompletedZones.Count +
                             ", zone " + Game.State.Run.Zone + ", cats " + Game.State.Run.Workers.Count + ", gold " + OutpostUiText.Money(cta.GoldDelivered) +
                             ", quests: " + string.Join(", ", m_Log);
            Debug.Log("[PlayableEvidence] " + summary);
            Assert.That(cta.EndCardOpen, Is.True, "финальный экран через настоящую игру: " + summary);

            // 11. Финальный экран с настоящими итогами — 9:16 и 9:20.
            yield return WaitFrames(() => false, 15, 0);
            yield return Capture("11-end-card.png");
            yield return Capture("11-end-card-2400.png", 2400);

            // 12. «Keep playing»: баннер, панель квеста и подсказки снова на месте.
            cta.KeepPlaying();
            yield return Wait(() => quests.Panel.Visible, 20);
            yield return WaitFrames(() => cta.Banner.Settled, 30, 0);
            yield return Capture("12-keep-playing.png");

            var missing = shots.Where(s => !m_Taken.Contains(s.Name)).Select(s => s.Name).ToList();
            Debug.Log("[PlayableEvidence] shots: " + string.Join(", ", m_Taken) + (missing.Count > 0 ? "; not reached: " + string.Join(", ", missing) : ""));
            Assert.That(m_Truncated, Is.Empty, string.Join("\n", m_Truncated));
            Assert.That(missing.Where(n => !n.StartsWith("08-") && !n.StartsWith("09-") && !n.StartsWith("13-")), Is.Empty,
                "состояния, которые настоящая игра проходит всегда");
        }

        /// <summary>
        /// Ход бота-игрока: награда, окна (итог тайм-скипа, сундук посреди шахты), квест (через <see cref="ReadSeconds"/> после
        /// появления) и покупка по карману (раз в 1,5 с: ветка квеста или самая дешёвая). true — что-то куплено.
        /// </summary>
        bool BotStep(bool questReady, bool buyNow)
        {
            var quests = Quests;
            var hud = Game.Hud;
            if (Game.Phase != OutpostControllerPhase.Playing || Game.InTransition || Cta.EndCardOpen) return false;
            if (quests.ClaimReady)
            {
                m_Log.Add(quests.Zone + ":" + quests.Current.Text);
                quests.Claim();
                return false;
            }
            if (hud.SkipResultOpen)
            {
                hud.HandleBack();
                return false;
            }
            var run = Game.State.Run;
            if (hud.ChestRewardOpen && run.Phase == RunPhase.Running && !hud.StoreOpen)
            {
                hud.HideChestReward();
                return false;
            }
            if (hud.AnyWindowOpen || quests.PopupOpen || run.Phase != RunPhase.Running) return false;
            var q = quests.Current;
            if (questReady && q != null && !quests.CurrentDone)
                switch (q.Kind)
                {
                    case QuestKind.Equip:
                        Game.EquipBest();
                        break;
                    case QuestKind.Card:
                    {
                        var p = Game.State.Profile;
                        var card = Game.Configs.Cards?.Cards.FirstOrDefault(d => CardService.Quote(p, d).CanUpgrade);
                        if (card != null) Game.UpgradeCard(card.Id);
                        break;
                    }
                    case QuestKind.Skip:
                    {
                        var skip = PlayableQuestRules.SkipInStock(Game);
                        if (skip != null && Game.UseTimeSkip(skip.Id).Ok) return false;
                        break;
                    }
                    case QuestKind.Chest:
                    {
                        var chest = PlayableQuestRules.FreeAdChest(Game);
                        if (chest != null) Game.StoreBuy(chest.Id);
                        break;
                    }
                }
            if (!buyNow) return false;
            // Ветка квеста покупки — первой (когда квест прочитан), иначе самая дешёвая по карману.
            if (questReady && q != null && q.IsPurchase && !quests.CurrentDone && PlayableQuestRules.Affordable(Game, q.PurchaseBranch))
                return Game.Buy(q.PurchaseBranch, BuyMode.One).Ok;
            UpgradeBranch? cheapest = null;
            var best = System.Numerics.BigInteger.Zero;
            for (int b = 0; b < OutpostIds.BranchCount; b++)
            {
                var branch = (UpgradeBranch)b;
                var quote = Game.Quote(branch, BuyMode.One);
                if (!quote.CanBuy || !quote.NextCost.HasValue) continue;
                if (cheapest == null || quote.NextCost.Value < best)
                {
                    cheapest = branch;
                    best = quote.NextCost.Value;
                }
            }
            return cheapest.HasValue && Game.Buy(cheapest.Value, BuyMode.One).Ok;
        }
    }
}
