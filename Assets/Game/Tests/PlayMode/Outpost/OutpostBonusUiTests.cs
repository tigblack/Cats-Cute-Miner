using System.Collections;
using System.Linq;
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
    /// Бонус за рекламу у левого края и мортира с динамитом (ТЗ 10.6) с настоящим контроллером в тестовом режиме рекламы:
    /// иконка появляется по расписанию активной игры (тайминги ×0,01), тап начисляет зафиксированную награду и прячет иконку,
    /// пропущенная иконка исчезает и возвращается; динамит — выстрел мортиры, полёт и приземление бочки, взрыв по тапу.
    /// </summary>
    public sealed class OutpostBonusUiTests : OutpostCardsFixture
    {
        [UnitySetUp]
        public IEnumerator EnableRewardSimulation()
        {
            OutpostController.SimulateRewardedOffersForTests = true;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator ResetBonusTiming()
        {
            OutpostController.BonusTimeScale = 1f;
            OutpostController.SimulateRewardedOffersForTests = false;
            yield break;
        }

        Button BonusButton => Game.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == "button" && b.transform.parent.name == "Bonus");

        /// <summary>Шаги по 0,25 с часов игры (больше 1 с — разрыв и расчёт офлайна).</summary>
        IEnumerator Advance(double seconds)
        {
            while (seconds > 0)
            {
                double step = System.Math.Min(0.25, seconds);
                Clock.Advance(step);
                seconds -= step;
                yield return null;
            }
        }

        IEnumerator UntilOffer(double maxSeconds)
        {
            for (double t = 0; t < maxSeconds && !Game.BonusOffer.Valid; t += 0.25)
            {
                Clock.Advance(0.25);
                yield return null;
            }
            yield return null;
        }

        /// <summary>Контроллер видит правила бонуса из store_catalog.json и считает суммы (без расписания).</summary>
        [UnityTest]
        public IEnumerator Configs_HaveRewardedBonus_QuotesWork()
        {
            yield return null;
            var store = Game.Configs.Store;
            Assert.That(store, Is.Not.Null);
            Assert.That(store.RewardedBonus, Is.Not.Null, "RewardedBonus в каталоге " + store.Version);
            Assert.That(OutpostController.SuppressRewardedBonus, Is.False, "бонус не отключён (playable)");
            Assert.That(store.TestPurchases, Is.True, "тестовый режим рекламы");
            Assert.That(Game.Sim.Run.Phase, Is.EqualTo(RunPhase.Running));
            bool gold = Game.Sim.TryQuoteBonus(RewardedBonusKind.Gold, out var amount);
            Assert.That(gold, Is.True, "золото: " + amount + ", уровни " + string.Join(",", Game.Sim.Run.Levels));
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Gold), Is.True, "оферта показана");
            yield return null;
            Assert.That(Game.BonusOffer.Valid, Is.True, "оферта держится кадр");
        }

        [UnityTest]
        public IEnumerator Icon_AppearsOnSchedule_TapGrantsFixedReward_ThenHides()
        {
            OutpostController.BonusTimeScale = 0.01f;     // первая через 1,2 с, висит 0,45 с, интервал 1,8 с
            yield return null;
            Assert.That(Game.BonusOffer.Valid, Is.False, "в начале игры иконки нет");
            Assert.That(Game.Hud.BonusVisible, Is.False);
            yield return UntilOffer(3);
            var offer = Game.BonusOffer;
            Assert.That(offer.Valid, Is.True, "иконка появилась после FirstDelaySec активной игры");
            Assert.That(Game.Hud.BonusVisible, Is.True);
            Assert.That(offer.Amount.Sign, Is.GreaterThan(0));

            var run = Game.State.Run;
            var wallet = run.Wallet;
            long gems = Game.State.Profile.Gems, elixir = Game.State.Profile.Elixir;
            BonusButton.onClick.Invoke();
            yield return null;
            Assert.That(Game.BonusOffer.Valid, Is.False, "награда забрана — иконка до следующего интервала");
            Assert.That(Game.Hud.BonusVisible, Is.False);
            switch (offer.Kind)
            {
                case RewardedBonusKind.Gold: Assert.That(run.Wallet, Is.EqualTo(wallet + offer.Amount)); break;
                case RewardedBonusKind.Gems: Assert.That(Game.State.Profile.Gems, Is.EqualTo(gems + (long)offer.Amount)); break;
                case RewardedBonusKind.Elixir: Assert.That(Game.State.Profile.Elixir, Is.EqualTo(elixir + (long)offer.Amount)); break;
                case RewardedBonusKind.Dynamite: Assert.That(Game.State.Run.Dynamite, Is.Not.Null, "бочка на поле"); break;
                case RewardedBonusKind.Damage: Assert.That(Game.Sim.DamageBonusActive, Is.True); break;
            }
        }

        [UnityTest]
        public IEnumerator Damage_OfferExplainsDuration_ActiveCountdownAllowsOtherOffers_ThenExpires()
        {
            yield return null;
            FirstSessionTestUtil.SkipPresentation(Game);
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Damage), Is.True);
            yield return null;
            Assert.That(Game.Hud.BonusVisible, Is.True);
            Assert.That(Game.Hud.DamageBonusLabel, Is.EqualTo("Damage ×2\n5 min"));
            var icon = BonusButton.GetComponentsInChildren<Image>().Single(i => i.name == "icon");
            Assert.That(icon.sprite, Is.Not.Null);
            Assert.That(icon.sprite.name, Does.Contain("pickaxe"));
            BonusButton.onClick.Invoke();
            yield return null;
            Assert.That(Game.Sim.DamageBonusActive, Is.True);
            Assert.That(Game.Hud.BonusVisible, Is.False);
            Assert.That(Game.Hud.DamageBonusVisible, Is.True);
            Assert.That(Game.Hud.DamageBonusLabel, Is.EqualTo("×2  05:00"));
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Damage), Is.False);
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Gems), Is.True);
            yield return Advance(1.25);
            Assert.That(Game.Hud.BonusVisible, Is.True);
            Assert.That(Game.Hud.DamageBonusVisible, Is.True);
            Assert.That(Game.Hud.DamageBonusLabel, Is.EqualTo("×2  04:59"));
            Game.Hud.ShowSettings();
            yield return null;
            Assert.That(Game.Hud.DamageBonusVisible, Is.False);
            Game.Hud.HideSettings();
            yield return null;
            Assert.That(Game.Hud.DamageBonusVisible, Is.True);
            // Core tests cover the full five-minute interval; isolate HUD expiry without running a whole mine.
            Game.State.Profile.DamageBonusSecondsLeft = Rational.Of(1, Game.Sim.Balance.TicksPerSecond);
            Game.Sim.Advance(Game.Sim.Now + 1);
            yield return null;
            Assert.That(Game.Hud.DamageBonusVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator RewardedAds_SettingsShortcutMatchesProviderSupport()
        {
            Game.Hud.ShowSettings();
            yield return null;
            var buttons = Game.Hud.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Any(b => b.name == "RewardedAds"),
                Is.EqualTo(CatMiners.Runtime.Integrations.GameSdkConfiguration.AdsEnabled));
            if (OutpostController.AndroidRuntime)
                Assert.That(buttons.Any(b => b.name == "PrivacyChoices"), Is.True, "existing privacy entry is retained");
        }

        [UnityTest]
        public IEnumerator Icon_SkippedAfterVisibleTime_ReturnsAfterInterval_HiddenUnderWindows()
        {
            OutpostController.BonusTimeScale = 0.01f;
            yield return UntilOffer(3);
            Assert.That(Game.BonusOffer.Valid, Is.True);
            Game.Hud.ShowStore();
            yield return null;
            Assert.That(Game.Hud.BonusVisible, Is.False, "окно поверх — иконки не видно");
            Game.Hud.HideStore();
            yield return Advance(0.75);
            Assert.That(Game.BonusOffer.Valid, Is.False, "пропуск: висела VisibleSec");
            yield return Advance(1.0);
            Assert.That(Game.BonusOffer.Valid, Is.False, "до IntervalSec новой нет");
            yield return UntilOffer(2);
            Assert.That(Game.BonusOffer.Valid, Is.True, "вернулась через IntervalSec");
        }

        [UnityTest]
        public IEnumerator Dynamite_MortarFires_BarrelLands_TapExplodes_DamagesTargets()
        {
            // Новые шахты начинаются с узкого входа; ждём, пока рабочие откроют место для взрыва.
            Game.SwitchProfile(true);
            yield return WaitPlaying();
            Game.LoadZone(3);
            yield return WaitPlaying();
            // Окно знакомства с экипировкой (шахта 3) прячет иконку бонуса — этому тесту оно не нужно.
            FirstSessionTestUtil.SkipPresentation(Game);
            Game.SimSpeed = 20;
            for (int frame = 0; frame < 800 && !Game.Sim.TryQuoteBonus(RewardedBonusKind.Dynamite, out _); frame++)
            {
                Clock.Advance(0.25);
                yield return null;
            }
            Game.SimSpeed = 1;
            var mortar = Game.Board.Camp.Mortar;
            Assert.That(mortar, Is.Not.Null, "мортира в лагере");
            Assert.That(Game.DebugShowBonus(RewardedBonusKind.Dynamite), Is.True,
                "simulation=" + OutpostController.RewardedOffersSupported + ", phase=" + Game.State.Run.Phase +
                ", targets=" + Game.Sim.StrikeableTargetCount() + ", barrel=" + (Game.State.Run.Dynamite != null));
            yield return null;
            Assert.That(Game.Hud.BonusVisible, Is.True);
            var hp = Game.State.Run.Targets.ToDictionary(t => t.Id, t => t.RemainingHpUnits);
            BonusButton.onClick.Invoke();
            yield return null;
            var barrel = Game.State.Run.Dynamite;
            Assert.That(barrel, Is.Not.Null, "ядро: бочка на поле");
            Assert.That(mortar.Shots, Is.EqualTo(1), "мортира выстрелила");
            Assert.That(Game.Board.Dynamite, Is.Not.Null, "вид бочки");
            Assert.That(Game.Board.Dynamite.Flying, Is.True, "летит из мортиры");
            for (int frame = 0; frame < 600 && Game.Board.Dynamite.Flying; frame++) yield return null;
            Assert.That(Game.Board.Dynamite.Flying, Is.False, "приземлилась");
            var target = Game.Board.CellWorld(barrel.Cell);
            Assert.That(Vector2.Distance(new Vector2(Game.Board.Dynamite.transform.position.x, Game.Board.Dynamite.transform.position.z),
                new Vector2(target.x, target.z)), Is.LessThan(0.6f), "в своей клетке");

            Assert.That(Game.Detonate().Ok, Is.True, "тап — взрыв");
            yield return null;
            Assert.That(Game.State.Run.Dynamite, Is.Null);
            Assert.That(Game.Board.Dynamite == null, Is.True, "вид бочки убран");
            Assert.That(Game.State.Run.Targets.Any(t => t.RemainingHpUnits < hp[t.Id]), Is.True, "взрыв ранил цели");
            Assert.That(Game.Detonate().Ok, Is.False, "второй раз взрывать нечего");
        }
    }
}
