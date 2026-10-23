using System;
using System.Collections;
using System.Linq;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CatMiners.Tests
{
    /// <summary>
    /// Первая сессия и прерывания (Docs/Rework/FIRST_SESSION.md): перезапуск сцены с тем же каталогом сохранений сразу после
    /// спасения Mia (до «Continue»), после окна «Mia is free!» и сразу после входа в шахту 2 (выдача стартовой карточки). Спасение и
    /// выдачи — один раз; окно показывается, пока игрок его не закрыл, и больше не повторяется; в шахте 2 Store и Cards открыты,
    /// Mia — за прилавком магазина, а не котиком в шахте; окон обучения нет.
    /// </summary>
    public sealed class FirstSessionSaveTests : OutpostCardsFixture
    {
        OutpostHud Hud => Game.Hud;

        IEnumerator Play(double seconds, Func<bool> until)
        {
            double next = 0;
            while (seconds > 0 && !until())
            {
                Clock.Advance(0.5);
                seconds -= 0.5;
                next -= 0.5;
                if (next <= 0 && Game.State.Run.Phase == RunPhase.Running && !Hud.IntroOpen)
                {
                    next = 1;
                    foreach (var b in new[] { UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags })
                        if (Game.Quote(b, BuyMode.One).CanBuy)
                        {
                            Game.Buy(b, BuyMode.One);
                            break;
                        }
                }
                if (Hud.ChestRewardOpen) Hud.HideChestReward();
                yield return null;
            }
        }

        /// <summary>Записать снимок (интервал записи контроллера — 2 с часов) и перезапустить сцену с тем же каталогом.</summary>
        IEnumerator SaveAndReload()
        {
            Clock.Advance(OutpostController.SaveIntervalSeconds + 0.5);
            yield return null;
            yield return null;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            yield return WaitPlaying();
            yield return PlayModeUtil.WaitReal(0.3f);
        }

        IEnumerator WaitIntro(string title)
        {
            float until = Time.realtimeSinceStartup + 6f;
            while (!Hud.IntroOpen && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(Hud.IntroTitle, Is.EqualTo(title));
        }

        void TapIntro(int index)
        {
            var buttons = Hud.transform.Find("SafeArea/FeatureIntro").GetComponentsInChildren<Button>(false).Where(b => b.name == "btn").ToList();
            buttons[index].onClick.Invoke();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Rescue_ReloadBeforeAndAfterReveal_MiaOnce_RevealOnce_CardsGrantOnce_StoreWithMia()
        {
            yield return WaitPlaying();
            Assert.That(Game.Sim.HasRescue, Is.True);
            yield return Play(1200, () => !Game.Sim.RescuePending);
            Assert.That(Game.Sim.RescuePending, Is.False, "rescued");
            int workers = Game.State.Run.Workers.Count;

            // Прерывание до «Continue»: Mia спасена, окно покажется после перезапуска.
            yield return SaveAndReload();
            var p = Game.State.Profile;
            Assert.That(p.Progression.Crew.Count(c => c == "mia"), Is.EqualTo(1));
            Assert.That(Game.Sim.RescuePending, Is.False, "the cage stays open after reload");
            Assert.That(Game.State.Run.Workers.Count, Is.EqualTo(workers));
            yield return WaitIntro("Mia is free!");
            TapIntro(0);
            yield return null;
            Assert.That(p.Progression.WasShown("reveal.mia"), Is.True);

            // После «Continue»: перезапуск — без повтора окна и без второй отметки.
            yield return SaveAndReload();
            yield return PlayModeUtil.WaitReal(1.5f);
            Assert.That(Hud.IntroOpen, Is.False, "no second reveal");
            Assert.That(Game.State.Profile.Progression.Crew.Count(c => c == "mia"), Is.EqualTo(1));

            // Шахта 2: выдача карточки знакомства — один раз, и после перезапуска до знакомства.
            yield return Play(3600, () => Game.State.Run.Phase == RunPhase.ReadyToContinue);
            Assert.That(Game.State.Run.Phase, Is.EqualTo(RunPhase.ReadyToContinue));
            for (int i = 0; i < 300 && !Hud.ResultsOpen; i++)
            {
                if (Hud.ChestRewardOpen) Hud.HideChestReward();
                yield return null;
            }
            Assert.That(Hud.ResultsOpen, Is.True, "results after the rescue scene and Mia's window");
            Hud.FindTarget(OutpostHud.TargetResultsNext).GetComponent<Button>().onClick.Invoke();
            for (int i = 0; i < 900 && !(Game.State.Run.Zone == 2 && Game.Phase == OutpostControllerPhase.Playing && !Hud.InTransition); i++) yield return null;
            Assert.That(Game.State.Run.Zone, Is.EqualTo(2));
            var card = Game.Configs.Cards.Get(Game.Configs.Progression.CardsIntro.Card);
            p = Game.State.Profile;
            long copies = p.Cards[card.Id].Copies, elixir = p.Elixir;
            Assert.That(p.CardLevel(card.Id), Is.EqualTo(1));
            Assert.That(CardService.Quote(p, card).CanUpgrade, Is.True);
            Assert.That(p.Progression.IsUnlocked("cards") && p.Progression.IsUnlocked("store"), Is.True, "Store opens together with Cards");
            Assert.That(Game.State.Run.Workers.Count(w => w.Crew != null), Is.Zero, "Mia runs the Store, not a cat in the mine");

            yield return SaveAndReload();
            p = Game.State.Profile;
            Assert.That(p.Cards[card.Id].Copies, Is.EqualTo(copies), "no second grant after reload");
            Assert.That(p.Elixir, Is.EqualTo(elixir));
            // Bruno's callout precedes the short Cards lesson; neither repeats its grant after reload.
            yield return PlayModeUtil.WaitReal(6f);
            yield return WaitIntro("A little help from Cards");
            Assert.That(Hud.RescueCalloutActive, Is.False, "Cards waits for Bruno's presentation");
            TapIntro(1);
            yield return null;
            Assert.That(p.Progression.WasShown(ProgressionService.CardsIntroShown), Is.True);
            Assert.That(Hud.IntroOpen || Hud.GuideHintVisible, Is.False, "queued lesson dismissed");
            Hud.transform.Find("SafeArea/Bottom/BottomBar/Store").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(Hud.StoreOpen && Hud.StoreKeeperVisible, Is.True, "Mia at the top of the Store");
            Hud.HideStore();
            Assert.That(OutpostInvariants.Check(Game.State, Game.Map, Game.Configs.Equipment), Is.Empty);
        }
    }
}
