using System;
using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using UnityEngine;
using CommandResult = CatMiners.Core.Outpost.CommandResult;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController
    {
        string m_IntegrationRun;
        bool m_GameLoadedReported, m_RewardShowing;
        readonly HashSet<string> m_EarnedCallbacks = new HashSet<string>();
        GameSdkRuntime Services => GameSdkRuntime.Instance;
        // Ordinary Editor previews follow the release too. Only tests can opt into simulated rewards.
#if UNITY_EDITOR
        public static bool SimulateRewardedOffersForTests;
#endif
        public static bool RewardedOffersSupported
        {
            get {
#if UNITY_EDITOR
                return SimulateRewardedOffersForTests;
#else
                return GameSdkConfiguration.AdsEnabled;
#endif
            }
        }
        public bool RewardedReady => Services != null && Services.RewardReady && !m_RewardShowing && !SaveFailed;
        public string RewardedStatus => Services?.RewardStatus ?? "unavailable";
        public static bool AndroidRuntime
        {
            get {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
        void IntegrationLoaded()
        {
            if (!AndroidRuntime || State == null) return;
            if (Services != null) Services.GameplayActive = () => this != null && isActiveAndEnabled &&
                !IsDebugProfile && CommandsAllowed && m_Offline == null && PendingOffline == null && PendingSkip == null &&
                !m_RewardShowing && CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Pending != true;
            BindPurchases();
            if (!m_GameLoadedReported) { m_GameLoadedReported = true; Services?.Track(GameEvent.GameLoaded, State.Run.Zone); }
            if (m_IntegrationRun != State.Run.RunId)
            { m_IntegrationRun = State.Run.RunId; Services?.Track(GameEvent.MineStarted, State.Run.Zone); }
        }
        public static StoreQuote PlatformStoreQuote(StoreQuote quote)
        {
            if (quote.Def.Currency == StoreCurrency.Ad && !RewardedOffersSupported)
            {
                quote.Free = false;
                quote.BlockReason = "unavailable";
                return quote;
            }
            if (!AndroidRuntime) return quote;
            if (quote.Def.Currency == StoreCurrency.Real)
            {
                var money = CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance;
                quote.BlockReason = money == null || !money.Ready || money.Pending || money.Price(quote.Def.Id) == null ? "unavailable" : null;
            }
            if (quote.Def.Currency == StoreCurrency.Ad)
            {
                quote.Free = false;
                if (quote.CooldownLeftMs > 0) quote.BlockReason = "cooldown";
                else if (GameSdkRuntime.Instance == null || !GameSdkRuntime.Instance.RewardReady && !GameSdkRuntime.Instance.NeedsRewardRetry) quote.BlockReason = "unavailable";
                else { quote.BlockReason = null; quote.Free = true; }
            }
            return quote;
        }
        CommandResult OfferReward(string item, BigInteger gold, RewardedBonusOffer? bonus = null)
        {
            if (!RewardedOffersSupported) return CommandResult.Fail("unavailable");
            if (Services?.NeedsRewardRetry == true)
            {
                Services.RetryUncorrelatedShow();
                m_RewardShowing = false;
                return CommandResult.Fail("unavailable");
            }
            if (!CommandsAllowed || !RewardedReady || State.Profile.PendingRewardedOffer?.Earned == true) return CommandResult.Fail("unavailable");
            if (item != null)
            {
                if (!Configs.Store.TryGet(item, out var def) || def.Currency != StoreCurrency.Ad || StoreService.CooldownLeftMs(State.Profile, def, Clock.UtcNowMs) > 0)
                    return CommandResult.Fail("unavailable");
            }
            else if (bonus == null && gold.Sign <= 0) return CommandResult.Fail("unknown");
            var offer = new RewardedOffer { Id = "reward_" + Guid.NewGuid().ToString("N"), RunId = State.Run.RunId,
                ItemId = item, Gold = gold.ToString(), OfferedUtcMs = Clock.UtcNowMs };
            // Бонус за рекламу (ТЗ 10.6): вид и сумма записываются до показа вместе с офертой.
            if (bonus.HasValue)
            {
                offer.Bonus = RewardedBonus.Id(bonus.Value.Kind);
                if (bonus.Value.Kind != RewardedBonusKind.Gold) offer.Amount = (long)bonus.Value.Amount;
            }
            string placement = item ?? (bonus.HasValue ? RewardedBonus.Placement(bonus.Value.Kind) : null);
            var prepared = OutpostJson.DeepCopy(State);
            prepared.Profile.PendingRewardedOffer = offer;
            var map = Map;
            m_RewardShowing = true;
            // No ad starts before the offered placement/amount is durable.
            Commit(prepared, map, () => {
                SetSimulation(prepared, map);
                Services?.Track(GameEvent.RewardOffered, State.Run.Zone, placement);
                if (Services == null || !Services.ShowReward(offer.Id,
                    () => { if (this != null) m_EarnedCallbacks.Add(offer.Id); },
                    () => { if (this != null) { m_RewardShowing = false; Hud?.RefreshNow(); } })) m_RewardShowing = false;
            });
            return SaveFailed ? CommandResult.Fail("save") : new CommandResult { Ok = true };
        }
        void StepRewardDelivery()
        {
#if UNITY_EDITOR
            if (!SimulateRewardedOffersForTests) return;
#else
            if (!AndroidRuntime) return;
#endif
            if (State == null || SaveFailed || m_SaveBlocked || InTransition) return;
            var offer = State.Profile.PendingRewardedOffer;
            if (offer == null || offer.RunId != State.Run.RunId || (!offer.Earned && !m_EarnedCallbacks.Contains(offer.Id) && Services?.HasEarned(offer.Id) != true)) return;
            // An earned callback may arrive while Android is paused. Save it before waiting for foreground.
            if (!offer.Earned)
            {
                var receipt = OutpostJson.DeepCopy(State);
                receipt.Profile.PendingRewardedOffer.Earned = true;
                Commit(receipt, Map, () => {
                    // Receipt-only persistence must not rebase the clock or replace partially simulated work.
                    offer.Earned = true;
                    if (m_Offline != null) m_Offline.State.Profile.PendingRewardedOffer = OutpostJson.DeepCopy(receipt.Profile.PendingRewardedOffer);
                });
            }
            if (SaveFailed || m_Suspended || m_Offline != null || Phase != OutpostControllerPhase.Playing) return;
            // Detect an unreported background gap and consume all pre-reward online/catch-up time first.
            // This also includes time spent processing a completed offline job, whose origin predates this frame.
            AdvanceOnline();
            if (SaveFailed || m_Offline != null || Phase != OutpostControllerPhase.Playing) return;
            var prepared = OutpostJson.DeepCopy(State);
            var map = Map;
            var sim = new OutpostSimulation(prepared, map, Configs);
            var result = sim.DeliverVerifiedReward(prepared.Profile.PendingRewardedOffer, Clock.UtcNowMs);
            if (!result.Ok) return;
            var events = new List<OutpostEvent>();
            sim.DrainEvents(events);
            Commit(prepared, map, () => {
                SetSimulation(prepared, map);
                m_Events.AddRange(events);
                m_EarnedCallbacks.Remove(offer.Id);
                Services?.AcknowledgeReward(offer.Id);
                if (offer.ItemId == null && offer.Bonus == null) DismissOffline();
                Services?.Track(GameEvent.AdRewardSaved, State.Run.Zone);
                Services?.Track(GameEvent.RewardDelivered, State.Run.Zone, offer.ItemId ?? (offer.Bonus != null ? "bonus_" + offer.Bonus : null));
                Hud?.RefreshNow();
            });
        }
    }
}
