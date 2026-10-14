using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Что показывает плитка магазина сейчас (ТЗ 10.5): цвет, цена, таймер, бонус и возможность нажать.</summary>
    public struct StoreQuote
    {
        public StoreItemDef Def;
        /// <summary>Зелёная плитка: бесплатная награда готова (реклама) или расходник есть в наличии; иначе синяя — покупка.</summary>
        public bool Free;
        /// <summary>Расходников в наличии (тайм-скип).</summary>
        public int Owned;
        /// <summary>&gt; 0 — рекламная плитка ждёт столько миллисекунд.</summary>
        public long CooldownLeftMs;
        /// <summary>Эликсир или гемы, которые игрок получит этим нажатием (с бонусом первой покупки).</summary>
        public long Amount;
        /// <summary>Бонус первой покупки ещё доступен: зачёркнутое обычное количество, «+N%» и «Available: 1».</summary>
        public bool BonusAvailable;
        /// <summary>Причина, по которой нажатие сейчас не сработает: null, "gems", "cooldown", "unavailable", "phase".</summary>
        public string BlockReason;
        public bool CanBuy => BlockReason == null;
    }

    /// <summary>
    /// Магазин (ТЗ 10.5) над профилем: котировка плиток, оплата (гемы, реклама, деньги), выдача эликсира и гемов с бонусом
    /// первой покупки, расходники тайм-скипа и однократная выдача стартового набора. Сундуки открывает ядро командой
    /// <see cref="CommandKind.StoreBuy"/> (тот же <see cref="CardChestService"/> и тот же генератор CardRng, что у наград шахты),
    /// тайм-скип рассчитывает <see cref="OfflineSimulator.BeginSkip"/>.
    /// </summary>
    public static class StoreService
    {
        /// <summary>
        /// Однократно подключить магазин к профилю (новому или созданному до магазина): стартовые гемы и расходники.
        /// true — профиль изменён (его нужно записать).
        /// </summary>
        public static bool MigrateProfile(OutpostProfile p, StoreCatalog store)
        {
            if (p == null || store == null || p.StoreVersion >= 1) return false;
            EnsureCollections(p);
            p.Gems = checked(p.Gems + store.StartGems);
            foreach (var kv in store.StarterConsumables)
                if (kv.Value > 0) p.Consumables[kv.Key] = checked(p.ConsumableCount(kv.Key) + kv.Value);
            p.StoreVersion = 1;
            return true;
        }

        /// <summary>Сохранения до магазина не содержат его коллекций: пустые вместо null.</summary>
        public static void EnsureCollections(OutpostProfile p)
        {
            if (p.Consumables == null) p.Consumables = new Dictionary<string, int>(StringComparer.Ordinal);
            if (p.StoreCooldownUntilUtcMs == null) p.StoreCooldownUntilUtcMs = new Dictionary<string, long>(StringComparer.Ordinal);
            if (p.StoreBonusUsed == null) p.StoreBonusUsed = new List<string>();
        }

        public static bool BonusAvailable(OutpostProfile p, StoreItemDef def) =>
            def.HasBonus && (p.StoreBonusUsed == null || !p.StoreBonusUsed.Contains(def.Id));

        /// <summary>Сколько выдаст покупка сейчас: бонусное количество, пока бонус первой покупки не получен.</summary>
        public static long AmountNow(OutpostProfile p, StoreItemDef def) => BonusAvailable(p, def) ? def.BonusAmount : def.Amount;

        public static long CooldownLeftMs(OutpostProfile p, StoreItemDef def, long nowUtcMs)
        {
            if (def.Currency != StoreCurrency.Ad || p.StoreCooldownUntilUtcMs == null) return 0;
            return p.StoreCooldownUntilUtcMs.TryGetValue(def.Id, out long until) ? Math.Max(0L, until - nowUtcMs) : 0;
        }

        public static StoreQuote Quote(OutpostProfile p, StoreCatalog store, StoreItemDef def, long nowUtcMs)
        {
            var q = new StoreQuote { Def = def, Amount = AmountNow(p, def), BonusAvailable = BonusAvailable(p, def) };
            if (def.Kind == StoreItemKind.TimeSkip)
            {
                q.Owned = p.ConsumableCount(def.Id);
                if (q.Owned > 0)
                {
                    q.Free = true;
                    return q;
                }
            }
            switch (def.Currency)
            {
                case StoreCurrency.Gems:
                    if (p.Gems < def.Price) q.BlockReason = "gems";
                    break;
                case StoreCurrency.Ad:
                    q.CooldownLeftMs = CooldownLeftMs(p, def, nowUtcMs);
                    if (!store.TestPurchases) q.BlockReason = "unavailable";
                    else if (q.CooldownLeftMs > 0) q.BlockReason = "cooldown";
                    else q.Free = true;
                    break;
                case StoreCurrency.Real:
                    if (!store.TestPurchases) q.BlockReason = "unavailable";
                    break;
            }
            return q;
        }

        /// <summary>
        /// Оплатить плитку (кроме тайм-скипа — <see cref="PayTimeSkip"/>): гемы списываются, у рекламной плитки запускается
        /// таймер, деньги в тестовом режиме считаются оплаченными. Отказ ничего не меняет.
        /// </summary>
        public static CommandResult PayRewarded(OutpostProfile p, StoreItemDef def, long nowUtcMs)
        {
            if (def == null || def.Currency != StoreCurrency.Ad || def.Kind == StoreItemKind.TimeSkip) return CommandResult.Fail("unavailable");
            if (CooldownLeftMs(p, def, nowUtcMs) > 0) return CommandResult.Fail("cooldown");
            EnsureCollections(p);
            p.StoreCooldownUntilUtcMs[def.Id] = checked(nowUtcMs + def.CooldownMs);
            return new CommandResult { Ok = true };
        }

        public static CommandResult Pay(OutpostProfile p, StoreCatalog store, StoreItemDef def, long nowUtcMs)
        {
            if (def == null || def.Kind == StoreItemKind.TimeSkip) return CommandResult.Fail("unknown");
            var q = Quote(p, store, def, nowUtcMs);
            if (!q.CanBuy) return CommandResult.Fail(q.BlockReason);
            EnsureCollections(p);
            switch (def.Currency)
            {
                case StoreCurrency.Gems:
                    p.Gems -= def.Price;
                    return new CommandResult { Ok = true, Cost = def.Price };
                case StoreCurrency.Ad:
                    p.StoreCooldownUntilUtcMs[def.Id] = checked(nowUtcMs + def.CooldownMs);
                    return new CommandResult { Ok = true };
                default:
                    return new CommandResult { Ok = true };
            }
        }

        /// <summary>Выдать эликсир или гемы плитки (с бонусом первой покупки); возвращает выданное количество.</summary>
        public static long GrantAmount(OutpostProfile p, StoreItemDef def)
        {
            long amount = AmountNow(p, def);
            if (BonusAvailable(p, def))
            {
                EnsureCollections(p);
                p.StoreBonusUsed.Add(def.Id);
            }
            if (def.Kind == StoreItemKind.Elixir) p.Elixir = checked(p.Elixir + amount);
            else if (def.Kind == StoreItemKind.Gems) p.Gems = checked(p.Gems + amount);
            return amount;
        }

        /// <summary>
        /// Оплатить тайм-скип: расходник из наличия, иначе гемы по цене плитки. Вызывается на копии состояния до расчёта, поэтому
        /// списание и результат записываются одним снимком.
        /// </summary>
        public static CommandResult PayTimeSkip(OutpostProfile p, StoreItemDef def)
        {
            if (def == null || def.Kind != StoreItemKind.TimeSkip) return CommandResult.Fail("unknown");
            EnsureCollections(p);
            int owned = p.ConsumableCount(def.Id);
            if (owned > 0)
            {
                p.Consumables[def.Id] = owned - 1;
                return new CommandResult { Ok = true };
            }
            if (def.Currency != StoreCurrency.Gems) return CommandResult.Fail("unavailable");
            if (p.Gems < def.Price) return CommandResult.Fail("gems");
            p.Gems -= def.Price;
            return new CommandResult { Ok = true, Cost = def.Price };
        }
    }
}
