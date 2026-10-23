using System.Collections;
using System.IO;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Playable;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Tests
{
    /// <summary>
    /// Правки ревью playable (Temp/playable/impl_reports.json → codeReview, uxReview): отметка финального экрана переживает
    /// перезагрузку; окно награды не открывается под финальным экраном и ждёт «Keep playing»; цель, выполненная в кадр
    /// завершения шахты, засчитывается; награда не платится в завершённую шахту — окно откроется в следующей; тайм-скип —
    /// по итогу, а не по запасу; сундук из открытого магазина не закрывается сам и держит автопереход; «Reset progress» —
    /// новая игра без архивов; реплика наставника ждёт тост HUD. Стрелка к цели за краем экрана — снимок генератора
    /// (ворота становятся целью только после расчистки сектора).
    /// </summary>
    public sealed partial class PlayableTests
    {
        /// <summary>Окно награды открыто (кадры), «CLAIM» через 0,9 с, следующий квест въехал.</summary>
        IEnumerator ClaimCurrent()
        {
            var quests = Quests;
            yield return WaitFrames(() => quests.PopupOpen, 30, 0);
            Assert.That(quests.PopupOpen, Is.True, "окно награды " + quests.Current.Text);
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            quests.Claim();
            Assert.That(quests.PopupOpen, Is.False);
            yield return Advance(PlayableQuests.NextQuestSeconds + 0.25);
        }

        /// <summary>Автопереход: итоги, затемнение и шахта <paramref name="zone"/> в игре.</summary>
        IEnumerator ContinueToZone(int zone)
        {
            for (int frame = 0; frame < 400 && (Game.State.Run.Zone != zone || Game.InTransition || Game.Phase != OutpostControllerPhase.Playing); frame++)
            {
                m_Clock.Advance(0.25);
                yield return null;
            }
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(zone));
        }

        /// <summary>Debug-профиль (в нём есть «пройти шахту»), первый квест показан.</summary>
        IEnumerator DebugProfileFirstQuest()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            Assert.That(Quests.Zone, Is.EqualTo(1));
            Assert.That(Quests.Current.Kind, Is.EqualTo(QuestKind.Strike));
        }

        [UnityTest]
        public IEnumerator EndCard_ShownOncePerProfile_ReloadAfterKeepPlayingDoesNotReshow()
        {
            yield return Advance(1);
            Game.State.Profile.PlayTimeMs = PlayableDirector.SegmentPlayTimeMs;
            yield return null;
            yield return null;
            Assert.That(Cta.EndCardOpen, Is.True);
            Cta.KeepPlaying();
            Assert.That(Cta.EndCardOpen, Is.False);
            // Автосохранение записывает время игры; отметка «показан» — в файле статистики сразу.
            yield return Advance(2.5, 0.5);

            var old = Game;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(ReferenceEquals(Game, old), Is.False, "вкладка перезагружена");
            yield return WaitPlaying();
            yield return Advance(1);
            yield return null;
            Assert.That(Director.SegmentComplete, Is.True, "отрезок пройден и после перезагрузки");
            Assert.That(Cta.EndCardShown, Is.True);
            Assert.That(Cta.EndCardOpen, Is.False, "финальный экран после «Keep playing» не повторяется");
            Assert.That(Cta.Banner.Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator Quests_PopupNeverUnderEndCard_OpensAfterKeepPlaying()
        {
            var quests = Quests;
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            Game.State.Profile.PlayTimeMs = PlayableDirector.SegmentPlayTimeMs;
            yield return null;
            yield return null;
            Assert.That(Cta.EndCardOpen, Is.True);
            yield return CompleteStrikeQuest();
            yield return Advance(1);
            Assert.That(quests.CurrentDone, Is.True, "прогресс под финальным экраном считается");
            Assert.That(quests.PopupOpen, Is.False, "окно награды не открывается под финальным экраном");
            Assert.That(quests.Panel.Visible, Is.False);
            Assert.That(quests.Guide.Hint.Visible, Is.False);

            Cta.KeepPlaying();
            yield return null;
            yield return null;
            Assert.That(quests.PopupOpen, Is.True, "после «Keep playing» — окно награды");
            Assert.That(quests.Panel.Visible, Is.True);
            Assert.That(quests.Panel.Checked, Is.True);
            Assert.That(Cta.Banner.Dimmed, Is.True, "баннер притушен под окном награды");
        }

        [UnityTest]
        public IEnumerator Quests_GoalMetAsMineClears_CountsThenPaysInNextMine()
        {
            yield return DebugProfileFirstQuest();
            var quests = Quests;
            yield return CompleteStrikeQuest();
            yield return ClaimCurrent();
            for (int i = 0; i < 2; i++)
            {
                GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Pickaxes));
                Assert.That(Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One).Ok, Is.True);
            }
            yield return Advance(0.25);
            Assert.That(quests.CurrentDone, Is.True);
            yield return ClaimCurrent();
            GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Team));
            Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True);
            yield return Advance(0.25);
            Assert.That(quests.CurrentDone, Is.True);
            yield return ClaimCurrent();
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Deliver));
            Assert.That(quests.CurrentDone, Is.False);

            // Шахта проходится целиком за один кадр показа (реальная логистика ускоренно): доставка — в том же кадре.
            Game.CompleteStageNow();
            yield return WaitPlaying();
            yield return null;
            yield return null;
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Deliver), "цель кадра завершения засчитана, не заменена «Clear Mine»");
            Assert.That(quests.CurrentDone, Is.True);
            Assert.That(quests.PopupOpen, Is.False, "в завершённой шахте окно награды не открывается");
            int mine1Targets = Game.Sim.Map.RequiredCount;

            yield return ContinueToZone(2);
            yield return WaitFrames(() => quests.PopupOpen, 60);
            Assert.That(quests.PopupOpen, Is.True, "награда — в следующей шахте");
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            var bonus = Game.State.Run.BonusCredited;
            var reward = quests.Reward;
            quests.Claim();
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            Assert.That(Game.State.Run.BonusCredited - bonus, Is.EqualTo(reward), "золото — в новую шахту");

            // «Clear Mine 1» не пропадает: он выполнен и его окно открывается следом (после паузы), тоже в новой шахте.
            Assert.That(quests.Zone, Is.EqualTo(1));
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Clear));
            Assert.That(quests.CurrentDone, Is.True);
            yield return null;
            Assert.That(quests.PopupOpen, Is.False, "не в кадр сбора прежней награды");
            yield return Advance(PlayableQuests.NextQuestSeconds + 0.1);
            yield return WaitFrames(() => quests.PopupOpen, 30, 0);
            Assert.That(quests.PopupOpen, Is.True, "окно «Clear Mine 1»");
            Assert.That(quests.Panel.Visible, Is.True);
            Assert.That(quests.Panel.Checked, Is.True);
            Assert.That(quests.Panel.Counter, Is.EqualTo(mine1Targets + "/" + mine1Targets), "счётчик — по шахте 1");
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            bonus = Game.State.Run.BonusCredited;
            var clearReward = quests.Reward;
            Assert.That(clearReward, Is.GreaterThan(reward), "«Clear Mine» — 300 %, больше награды доставки");
            quests.Claim();
            Assert.That(Game.State.Run.BonusCredited - bonus, Is.EqualTo(clearReward));
            Assert.That(quests.Zone, Is.EqualTo(2), "цепочка новой шахты");
            Assert.That(quests.Index, Is.Zero);
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator Juice_WaitsForWindows_MineClearedLeftToResults()
        {
            yield return DebugProfileFirstQuest();
            var juice = Juice;
            var hud = Game.Hud;

            // Найм при открытой «Базе»: надпись ждёт открытого поля, поверх окна не рисуется.
            hud.ShowBase();
            yield return null;
            GrantGold(PlayableQuestRules.NextCost(Game, UpgradeBranch.Team));
            Assert.That(Game.Buy(UpgradeBranch.Team, BuyMode.One).Ok, Is.True);
            for (int i = 0; i < 40; i++) yield return Advance(0.05, 0.05);
            Assert.That(juice.Visible, Is.False, "окно открыто — надпись ждёт");
            hud.HideBase();
            yield return WaitFrames(() => juice.Visible, 30, 0);
            Assert.That(juice.Text, Is.EqualTo(Loc.T("playable.juice.new_cat")), "после закрытия окна");
            hud.ShowBase();
            yield return null;
            yield return null;
            Assert.That(juice.Visible, Is.False, "открылось окно — показанная надпись снята");
            hud.HideBase();
            yield return Advance(PlayableJuice.ShowSeconds + 0.25);

            // Шахта пройдена: итоги шахты отмечают момент — «MINE CLEARED!» не появляется ни до них, ни поверх.
            Game.CompleteStageNow();
            bool cleared = false;
            float previous = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            try
            {
                // Кадры с шагом анимаций (HUD открывает окна по состоянию 10 раз в секунду); сундук выхода закрывается по часам.
                for (int frame = 0; frame < 300 && !hud.ResultsOpen; frame++)
                {
                    cleared |= juice.Visible && juice.Text == Loc.T("playable.juice.cleared");
                    m_Clock.Advance(hud.ChestRewardOpen ? 0.25 : 0.02);
                    yield return null;
                }
                Assert.That(hud.ResultsOpen, Is.True, "итоги шахты");
                for (int frame = 0; frame < 30; frame++)
                {
                    cleared |= juice.Visible && juice.Text == Loc.T("playable.juice.cleared");
                    yield return null;
                }
            }
            finally { Time.captureDeltaTime = previous; }
            Assert.That(cleared, Is.False, "«MINE CLEARED!» не рисуется поверх итогов и не мелькает до них");
            Assert.That(juice.Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator Quests_PopupOpenWhenMineCompletes_GoldGoesToNextMine()
        {
            yield return DebugProfileFirstQuest();
            var quests = Quests;
            yield return CompleteStrikeQuest();
            Assert.That(quests.PopupOpen, Is.True);
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            Assert.That(quests.ClaimReady, Is.True);
            var bonus = Game.State.Run.BonusCredited;

            Game.CompleteStageNow();
            yield return WaitPlaying();
            yield return null;
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            Assert.That(quests.PopupOpen, Is.False, "окно закрыто: в завершённую шахту золото не платится");
            quests.Claim();
            Assert.That(Game.State.Run.BonusCredited, Is.EqualTo(bonus));
            Assert.That(quests.CurrentDone, Is.True, "награда ждёт следующую шахту");

            yield return ContinueToZone(2);
            yield return WaitFrames(() => quests.PopupOpen, 60);
            Assert.That(quests.PopupOpen, Is.True);
            yield return Advance(PlayableQuests.ClaimDelaySeconds);
            bonus = Game.State.Run.BonusCredited;
            var reward = quests.Reward;
            Assert.That(reward.Sign, Is.GreaterThan(0));
            quests.Claim();
            Assert.That(Game.State.Run.BonusCredited - bonus, Is.EqualTo(reward));
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment, Game.Configs.Cards), Is.Empty);
        }

        [UnityTest]
        public IEnumerator Quests_TimeSkipCountsByUse_NotByStock()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            StoreService.EnsureCollections(Game.State.Profile);
            Game.State.Profile.Consumables["skip_1h"] = 2;
            Game.LoadZone(4);
            yield return WaitPlaying();
            yield return Advance(1.5);
            var quests = Quests;
            Assert.That(quests.Zone, Is.EqualTo(4));
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Skip), "без предметов «Gear» пропущен, следом — тайм-скип");

            // Запас вырос (раньше разница запаса уводила прогресс в минус) — квест не засчитан.
            Game.State.Profile.Consumables["skip_1h"] += 5;
            yield return Advance(1);
            Assert.That(quests.Progress, Is.EqualTo(BigInteger.Zero));
            Assert.That(quests.CurrentDone, Is.False);

            Assert.That(Game.UseTimeSkip("skip_1h").Ok, Is.True);
            for (int frame = 0; frame < 3000 && Game.PendingSkip == null; frame++)
            {
                m_Clock.Advance(0.02);
                yield return null;
            }
            Assert.That(Game.PendingSkip, Is.Not.Null);
            Assert.That(Game.PendingSkip.SkipMinutes, Is.GreaterThan(0));
            yield return null;
            yield return null;
            Assert.That(quests.Current.Kind, Is.EqualTo(QuestKind.Skip));
            Assert.That(quests.CurrentDone, Is.True, "тайм-скип засчитан по итогу");
            Assert.That(quests.PopupOpen, Is.False, "окно награды ждёт итог тайм-скипа");
        }

        [UnityTest]
        public IEnumerator CompletedMine_StoreChestStaysOpen_AutoContinueWaitsForStore()
        {
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.CompleteStageNow();
            yield return WaitPlaying();
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));

            // Сундук куплен в открытом магазине после завершения шахты: окно — выбор игрока, само не закрывается.
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.StoreBuy("chest_wooden").Ok, Is.True);
            yield return null;
            yield return null;
            Assert.That(Game.Hud.ChestRewardOpen, Is.True);
            yield return Advance(PlayableDirector.ExitChestSeconds + PlayableDirector.ResultsSeconds + 1);
            Assert.That(Game.Hud.ChestRewardOpen, Is.True, "сундук магазина закрывает игрок");
            Assert.That(Game.InTransition || Game.State.Run.Zone != 1, Is.False);

            Game.Hud.HideChestReward();
            yield return Advance(PlayableDirector.ResultsSeconds + 1);
            Assert.That(Game.InTransition || Game.State.Run.Zone != 1, Is.False, "магазин открыт — автопереход ждёт");

            Game.Hud.HideStore();
            for (int i = 0; i < 40 && !Game.InTransition && Game.State.Run.Zone == 1; i++)
            {
                m_Clock.Advance(0.25);
                yield return null;
            }
            Assert.That(Game.InTransition || Game.State.Run.Zone == 2, Is.True, "магазин закрыт — следующая шахта");
        }

        [UnityTest]
        public IEnumerator Settings_ResetProgress_RestartsPlayableWithoutArchives()
        {
            const long marker = 765_431;
            yield return Advance(1);
            Assert.That(Game.Hud.ResetProgressOverride, Is.Not.Null, "крючок HUD задан режиссёром");
            Game.State.Profile.Elixir = marker;
            yield return Advance(2.5, 0.5);
            var old = Game;
            Game.Hud.ShowSettings();
            yield return null;
            var reset = PlayModeUtil.FindButton(Game.Hud, "ResetProgress");
            Assert.That(reset, Is.Not.Null);
            reset.onClick.Invoke();
            yield return null;
            var confirm = PlayModeUtil.FindModalButton(Game.Hud, Loc.T("outpost.dev.reset_confirm"));
            Assert.That(confirm, Is.Not.Null, "подтверждение сброса");
            confirm.onClick.Invoke();
            for (int frame = 0; frame < 600 && (Game == null || ReferenceEquals(Game, old) || Game.Phase != OutpostControllerPhase.Playing); frame++)
                yield return null;
            Assert.That(ReferenceEquals(Game, old), Is.False, "«Play again»: сцена загружена заново");
            Assert.That(Game.State.Profile.Elixir, Is.Not.EqualTo(marker));
            Assert.That(Game.State.Run.Zone, Is.EqualTo(1));
            Assert.That(Directory.GetDirectories(m_Dir, "restart-*", SearchOption.AllDirectories), Is.Empty, "без архивов ResetProgress");
        }

        [UnityTest]
        public IEnumerator Quests_MentorLineWaitsForHudToast()
        {
            var quests = Quests;
            var toast = Game.Hud.transform.Find("SafeArea/ToastBg").gameObject;
            Game.Hud.Toast("Chest: cards +1", 60f);
            yield return Advance(PlayableQuests.FirstQuestSeconds + 0.25);
            Assert.That(quests.Revealed, Is.True);
            Assert.That(quests.Guide.ToastVisible, Is.True);
            yield return WaitFrames(() => quests.Guide.PawVisible);
            Assert.That(quests.Guide.LineVisible, Is.False, "пока виден тост — одна инструкция: без реплики");
            Assert.That(quests.Guide.PawVisible, Is.True, "лапка — сразу");
            yield return Advance(PlayableQuests.LineSeconds + 1);
            Assert.That(quests.Guide.LineVisible, Is.False);

            // Тост исчез — реплика показывается целиком (её 3,6 с не потрачены под тостом).
            toast.SetActive(false);
            yield return Advance(0.25);
            Assert.That(quests.Guide.LineVisible, Is.True);
            Assert.That(quests.Guide.Hint.Text, Is.EqualTo(Loc.T("playable.quest.strike.line")));
            yield return Advance(PlayableQuests.LineSeconds - 1);
            Assert.That(quests.Guide.LineVisible, Is.True);
            yield return Advance(1.5);
            Assert.That(quests.Guide.LineVisible, Is.False);
        }
    }
}
