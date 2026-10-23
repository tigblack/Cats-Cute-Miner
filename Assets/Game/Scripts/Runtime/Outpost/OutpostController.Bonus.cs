using System;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    // Бонус за рекламу у левого края экрана (ТЗ 10.6, store_catalog.json → RewardedBonus). Расписание — по времени активной игры
    // (фаза Playing без фона; окна его не останавливают) и не сохраняется: после запуска первая иконка снова через FirstDelaySec.
    // Вид и сумма фиксируются при появлении иконки; награду выдаёт ядро (RewardBonus), на Android — после reward callback.
    public sealed partial class OutpostController
    {
        /// <summary>Playable-сборка: бонуса за рекламу нет (PlayableMode).</summary>
        public static bool SuppressRewardedBonus;
        /// <summary>
        /// Тесты и съёмка: множитель всех таймингов бонуса (0,01 — секунды вместо минут). Действует сразу, в том числе на уже
        /// назначенный показ: время активной игры копится делённым на множитель, пороги — секунды каталога.
        /// </summary>
        public static float BonusTimeScale = 1f;
        /// <summary>Повтор попытки показать иконку, когда реклама не готова или выпадать нечему, секунды каталога.</summary>
        const double BonusRetrySeconds = 5.0;

        double m_BonusActive;
        double m_BonusNextAt = -1.0;
        double m_BonusShownAt;
        double m_BonusLastMono = -1.0;
        RewardedBonusOffer m_BonusOffer;
        readonly System.Random m_BonusRng = new System.Random();

        public RewardedBonusOffer BonusOffer => m_BonusOffer;

        public float BonusTimeLeft01
        {
            get
            {
                var rules = BonusRules;
                if (!m_BonusOffer.Valid || rules == null) return 0f;
                return Mathf.Clamp01((float)(1.0 - (m_BonusActive - m_BonusShownAt) / Math.Max(0.001, rules.VisibleSec)));
            }
        }

        RewardedBonusRules BonusRules => SuppressRewardedBonus || !RewardedOffersSupported ? null : Configs?.Store?.RewardedBonus;

        /// <summary>Реклама сейчас может быть показана: на Android — готовый MAX rewarded, иначе — тестовый режим каталога.</summary>
        bool BonusAdsAvailable => AndroidRuntime ? RewardedReady || Services?.NeedsRewardRetry == true : Configs?.Store?.TestPurchases == true;

        /// <summary>Шаг расписания (каждый кадр): копит активное время, показывает и снимает иконку.</summary>
        void StepBonus()
        {
            double now = Clock.MonotonicSeconds;
            double dt = m_BonusLastMono < 0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, now - m_BonusLastMono));
            m_BonusLastMono = now;
            var rules = BonusRules;
            if (rules == null || Sim == null)
            {
                m_BonusOffer = default;
                return;
            }
            if (Phase != OutpostControllerPhase.Playing || m_Suspended || InTransition || SaveFailed) return;
            m_BonusActive += dt / Math.Max(1e-4, BonusTimeScale);
            if (m_BonusNextAt < 0) m_BonusNextAt = rules.FirstDelaySec;
            if (m_BonusOffer.Valid)
            {
                // Пропуск: иконка висела VisibleSec или шахта уже не идёт — следующая через IntervalSec.
                if (m_BonusActive - m_BonusShownAt >= rules.VisibleSec || Sim.Run.Phase == RunPhase.ReadyToContinue)
                    SkipBonus(rules);
                return;
            }
            if (m_BonusActive < m_BonusNextAt) return;
            var offer = BonusAdsAvailable && Sim.Run.Phase == RunPhase.Running ? Sim.RollBonus(m_BonusRng.NextDouble()) : default;
            if (!offer.Valid)
            {
                m_BonusNextAt = m_BonusActive + BonusRetrySeconds;
                return;
            }
            m_BonusOffer = offer;
            m_BonusShownAt = m_BonusActive;
            Hud?.RefreshNow();
        }

        void SkipBonus(RewardedBonusRules rules)
        {
            m_BonusOffer = default;
            m_BonusNextAt = m_BonusActive + rules.IntervalSec;
            Hud?.RefreshNow();
        }

        /// <summary>
        /// Забрать бонус: Android — показ рекламы (оферта записывается до показа, награда — после callback через
        /// <see cref="StepRewardDelivery"/>), редактор и браузер — тестовый режим, награда сразу командой ядра RewardBonus.
        /// </summary>
        public CommandResult ClaimBonus()
        {
            var offer = m_BonusOffer;
            var rules = BonusRules;
            if (!offer.Valid || rules == null) return CommandResult.Fail("unknown");
            if (offer.Kind == RewardedBonusKind.Damage && Sim?.DamageBonusActive == true) return CommandResult.Fail("unavailable");
            CommandResult result;
            if (AndroidRuntime) result = OfferReward(null, offer.Kind == RewardedBonusKind.Gold ? offer.Amount : System.Numerics.BigInteger.Zero, offer);
            else if (Configs?.Store == null || !Configs.Store.TestPurchases) return CommandResult.Fail("unavailable");
            else result = Execute(OutpostCommand.RewardBonus(offer.Kind, offer.Amount));
            if (result.Ok) SkipBonus(rules);
            return result;
        }

        /// <summary>Взрыв бочки динамита (тап по бочке).</summary>
        public CommandResult Detonate() =>
            Sim?.Run?.Dynamite == null ? CommandResult.Fail("unknown") : Execute(OutpostCommand.Detonate());

        /// <summary>Тесты и съёмка: показать оферту сразу (вид и сумма — по ядру, как при обычном появлении).</summary>
        public bool DebugShowBonus(RewardedBonusKind kind)
        {
            if (Sim == null || BonusRules == null || !Sim.TryQuoteBonus(kind, out var amount)) return false;
            m_BonusOffer = new RewardedBonusOffer { Valid = true, Kind = kind, Amount = amount };
            m_BonusShownAt = m_BonusActive;
            Hud?.RefreshNow();
            return true;
        }
    }
}
