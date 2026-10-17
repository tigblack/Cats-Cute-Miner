namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>
        /// Покупка плитки магазина (ТЗ 10.5): оплата через <see cref="StoreService.Pay"/>, затем сундук — тот же
        /// <see cref="OpenChest"/>, что у наград шахты (источник <see cref="OutpostIds.StoreSource"/>, событие ChestOpened),
        /// эликсир и гемы — в профиль.
        /// Тайм-скип командой не покупается: его рассчитывает контроллер (<see cref="OfflineSimulator.BeginSkip"/>).
        /// </summary>
        CommandResult ExecuteStoreBuy(OutpostCommand cmd, long t, bool verifiedReward = false)
        {
            var store = m_Configs.Store;
            if (store == null || !store.TryGet(cmd.ItemId, out var def) || def.Kind == StoreItemKind.TimeSkip) return CommandResult.Fail("unknown");
            if (def.Kind == StoreItemKind.Chest && (m_Cards == null || m_Cards.Chest(def.ChestId) == null)) return CommandResult.Fail("unknown");
            var paid = verifiedReward ? StoreService.PayRewarded(Profile, def, cmd.UtcMs) : StoreService.Pay(Profile, store, def, cmd.UtcMs);
            if (!paid.Ok) return paid;
            long amount = 0;
            if (def.Kind == StoreItemKind.Chest) OpenChest(def.ChestId, OutpostIds.StoreSource, -1, t);
            else amount = StoreService.GrantAmount(Profile, def);
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.StorePurchased, t);
            e.ItemId = def.Id;
            e.Value = amount;
            Emit(e);
            return new CommandResult { Ok = true, Cost = paid.Cost };
        }

        // Called only by the durable delivery pipeline after a matched SDK earned callback.
        public CommandResult DeliverVerifiedReward(RewardedOffer offer, long utcMs)
        {
            if (offer == null || !offer.Earned || string.IsNullOrEmpty(offer.Id) || offer.RunId != Run.RunId)
                return CommandResult.Fail("unmatched");
            if (Profile.DeliveredRewardedOffers == null) Profile.DeliveredRewardedOffers = new System.Collections.Generic.List<string>();
            if (Profile.DeliveredRewardedOffers.Contains(offer.Id)) return CommandResult.Fail("duplicate");
            CommandResult result;
            if (offer.Bonus != null)
            {
                if (!RewardedBonus.TryParse(offer.Bonus, out var kind)) return CommandResult.Fail("unknown");
                var amount = kind == RewardedBonusKind.Gold
                    ? (System.Numerics.BigInteger.TryParse(offer.Gold, out var gold) ? gold : System.Numerics.BigInteger.Zero)
                    : new System.Numerics.BigInteger(offer.Amount);
                result = ExecuteRewardBonus(OutpostCommand.RewardBonus(kind, amount), Now);
            }
            else if (offer.ItemId != null) result = ExecuteStoreBuy(OutpostCommand.StoreBuy(offer.ItemId, utcMs), Now, true);
            else if (System.Numerics.BigInteger.TryParse(offer.Gold, out var amount) && amount.Sign > 0)
                result = ExecuteBonusGold(OutpostCommand.BonusGold(amount), Now);
            else return CommandResult.Fail("unknown");
            if (!result.Ok) return result;
            Profile.DeliveredRewardedOffers.Add(offer.Id);
            Profile.PendingRewardedOffer = null;
            return result;
        }

        /// <summary>Золото награды за рекламу: в кошелёк и BonusCredited (TotalCredited остаётся стоимостью доставки).</summary>
        CommandResult ExecuteBonusGold(OutpostCommand cmd, long t)
        {
            if (cmd.Amount.Sign <= 0) return CommandResult.Fail("unknown");
            var run = Run;
            run.Wallet += cmd.Amount;
            run.BonusCredited += cmd.Amount;
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.BonusGold, t);
            e.Value = cmd.Amount;
            Emit(e);
            return new CommandResult { Ok = true, Cost = cmd.Amount };
        }
    }
}
