using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Outpost;

namespace CatMiners.Playable
{
    /// <summary>
    /// Правила квестов playable по состоянию игры (без UI): достижима ли цель в этой шахте, прогресс и цель счётчика,
    /// награда золотом. Только чтение: State, Configs и котировки контроллера.
    /// </summary>
    public static class PlayableQuestRules
    {
        /// <summary>Доставку и тайм-скип не начинаем на почти расчищенной шахте: цель не успеет набраться.</summary>
        const float DeliverClearedLimit = 0.6f, SkipClearedLimit = 0.7f;

        /// <summary>
        /// Цель достижима в текущей шахте при старте квеста: есть по чему бить (камни, завалы, ворота и цепи заводов — ТЗ 2.2 §4.2:
        /// цепи бьются, как любая цель), есть уровни до предела, есть ворота, есть что надеть или улучшить, есть сундук впереди
        /// или бесплатный сундук магазина. «Clear Mine» — всегда (цепи заводов входят в расчистку).
        /// </summary>
        public static bool IsAvailable(QuestDef q, OutpostController game)
        {
            var sim = game != null ? game.Sim : null;
            if (q == null || sim == null) return false;
            var run = sim.Run;
            bool running = run.Phase == RunPhase.Running;
            switch (q.Kind)
            {
                case QuestKind.Strike: return running && AnyStrikeable(sim);
                case QuestKind.Upgrade: return running && LevelsLeft(game, q.Branch) >= q.Goal;
                case QuestKind.Level:
                    return running && run.Levels[(int)q.Branch] < q.Goal &&
                           run.Levels[(int)q.Branch] + LevelsLeft(game, q.Branch) >= q.Goal;
                case QuestKind.Cats: return running && run.Workers.Count < q.Goal && run.Workers.Count + LevelsLeft(game, UpgradeBranch.Team) >= q.Goal;
                case QuestKind.Deliver: return running && ClearedFraction(sim) < DeliverClearedLimit;
                case QuestKind.Equip:
                {
                    EquipState(game, out int owned, out int best);
                    return run.Phase != RunPhase.ReadyToContinue && owned >= q.Goal && best < owned;
                }
                case QuestKind.Chest: return running && (ChestAhead(sim, game.Configs) || AdChestFree(game));
                case QuestKind.Card: return CardService.AnyUpgradable(sim.Profile, game.Configs?.Cards);
                case QuestKind.Skip: return running && SkipStock(game) > 0 && ClearedFraction(sim) < SkipClearedLimit;
                case QuestKind.Gate: return running && run.GatesOpened + q.Goal <= GateCount(sim);
                case QuestKind.Clear: return true;
                default: return false;
            }
        }

        /// <summary>
        /// Начатую цель ещё можно выполнить (иначе квест пропускается без награды): предел уровней не достигнут, ворота
        /// остались, сундук впереди… Шахту целиком (ReadyToContinue) обрабатывает PlayableQuests.
        /// </summary>
        public static bool StillPossible(QuestDef q, OutpostController game, BigInteger progress)
        {
            var sim = game.Sim;
            var run = sim.Run;
            switch (q.Kind)
            {
                case QuestKind.Strike: return AnyStrikeable(sim);
                case QuestKind.Upgrade: return LevelsLeft(game, q.Branch) >= q.Goal - (int)progress;
                case QuestKind.Level: return run.Levels[(int)q.Branch] + LevelsLeft(game, q.Branch) >= q.Goal;
                case QuestKind.Cats: return run.Workers.Count + LevelsLeft(game, UpgradeBranch.Team) >= q.Goal;
                case QuestKind.Chest: return ChestAhead(sim, game.Configs) || AdChestFree(game);
                case QuestKind.Card: return CardService.AnyUpgradable(sim.Profile, game.Configs?.Cards);
                case QuestKind.Skip: return run.Phase == RunPhase.Running && SkipStock(game) > 0;
                case QuestKind.Gate: return run.GatesOpened < GateCount(sim);
                default: return true;
            }
        }

        /// <summary>
        /// Опорное значение при старте квеста (относительные цели считают прибавку): уровень ветки, доставленное золото,
        /// сундуки шахты, открытые ворота. Тайм-скип считается событиями (итог скипа), без опоры.
        /// </summary>
        public static BigInteger Baseline(QuestDef q, OutpostController game)
        {
            var run = game.Sim.Run;
            switch (q.Kind)
            {
                case QuestKind.Upgrade: return run.Levels[(int)q.Branch];
                case QuestKind.Deliver: return run.Counters.DeliveredValue;
                case QuestKind.Chest: return run.Counters.ChestsOpened;
                case QuestKind.Gate: return run.GatesOpened;
                default: return BigInteger.Zero;
            }
        }

        /// <summary>
        /// Счётчик «progress/goal» квеста; <paramref name="events"/> — засчитанные события (удары, улучшения, сундуки магазина,
        /// итоги тайм-скипов).
        /// </summary>
        public static void Measure(QuestDef q, OutpostController game, BigInteger baseline, int events, out BigInteger progress, out BigInteger goal)
        {
            var sim = game.Sim;
            var run = sim.Run;
            goal = q.Goal;
            switch (q.Kind)
            {
                case QuestKind.Strike:
                case QuestKind.Card:
                case QuestKind.Skip:
                    progress = events;
                    break;
                case QuestKind.Upgrade: progress = run.Levels[(int)q.Branch] - baseline; break;
                case QuestKind.Level: progress = run.Levels[(int)q.Branch]; break;
                case QuestKind.Cats: progress = run.Workers.Count; break;
                case QuestKind.Deliver: progress = run.Counters.DeliveredValue - baseline; break;
                case QuestKind.Equip:
                {
                    EquipState(game, out int owned, out int best);
                    progress = best;
                    goal = BigInteger.Max(owned, 1);
                    break;
                }
                case QuestKind.Chest: progress = run.Counters.ChestsOpened - baseline + events; break;
                case QuestKind.Gate: progress = run.GatesOpened - baseline; break;
                case QuestKind.Clear:
                {
                    int destroyed = 0;
                    var targets = run.Targets;
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i].Destroyed) destroyed++;
                    goal = BigInteger.Max(sim.Map.RequiredCount, 1);
                    progress = run.Phase == RunPhase.ReadyToContinue ? goal : BigInteger.Min(destroyed, goal - 1);
                    break;
                }
                default: progress = BigInteger.Zero; break;
            }
            if (progress.Sign < 0) progress = BigInteger.Zero;
            if (progress > goal) progress = goal;
        }

        /// <summary>
        /// Награда золотом за <paramref name="q"/> — на экономике текущей шахты (окно награды открывается уже в шахте, где золото
        /// пригодится): RewardPercent от цены следующего уровня ветки <paramref name="next"/> (если следующий квест — покупка),
        /// иначе от дорожайшей из «Кирок» и «Команды»; округление вверх до двух значащих цифр.
        /// </summary>
        public static BigInteger RewardFor(QuestDef q, QuestDef next, OutpostController game)
        {
            BigInteger unit = BigInteger.Zero;
            if (next != null && next.IsPurchase) unit = NextCost(game, next.PurchaseBranch);
            if (unit.Sign <= 0) unit = BigInteger.Max(NextCost(game, UpgradeBranch.Pickaxes), NextCost(game, UpgradeBranch.Team));
            if (unit.Sign <= 0)
            {
                var balance = game.Sim?.Run?.Balance;
                unit = balance != null ? (balance.CoinBase * 10).Ceil() : new BigInteger(100);
            }
            var raw = (unit * q.RewardPercent + 99) / 100;
            return Nice(BigInteger.Max(raw, 10));
        }

        /// <summary>Вверх до двух значащих цифр: 44 → 44, 113 → 120, 1234 → 1300.</summary>
        public static BigInteger Nice(BigInteger v)
        {
            if (v.Sign <= 0) return BigInteger.Zero;
            var p = BigInteger.One;
            while (v >= p * 100) p *= 10;
            return (v + p - 1) / p * p;
        }

        /// <summary>Цена следующего уровня ветки (котировка ядра со скидками карточек); 0 — ветка на пределе.</summary>
        public static BigInteger NextCost(OutpostController game, UpgradeBranch branch)
        {
            if (game.Sim == null) return BigInteger.Zero;
            var cost = game.Quote(branch, BuyMode.One).NextCost;
            return cost ?? BigInteger.Zero;
        }

        /// <summary>Сколько уровней ветки ещё можно купить в этой шахте.</summary>
        public static int LevelsLeft(OutpostController game, UpgradeBranch branch)
        {
            var q = game.Quote(branch, BuyMode.One);
            return q.NextCost.HasValue ? System.Math.Max(0, q.Cap - q.CurrentLevel) : 0;
        }

        /// <summary>Покупка ×1 ветки по карману прямо сейчас (лапка на «Базу»).</summary>
        public static bool Affordable(OutpostController game, UpgradeBranch branch) =>
            game.Sim != null && game.Quote(branch, BuyMode.One).CanBuy;

        public static int GateCount(OutpostSimulation sim) => sim.Map.Gates != null ? sim.Map.Gates.Length : 0;

        public static bool AnyStrikeable(OutpostSimulation sim)
        {
            var map = sim.Map;
            var targets = sim.Run.Targets;
            for (int i = 0; i < map.Targets.Length && i < targets.Count; i++)
                if (!targets[i].Destroyed && sim.CanStrike(map.Targets[i].Id)) return true;
            return false;
        }

        public static float ClearedFraction(OutpostSimulation sim)
        {
            int required = sim.Map.RequiredCount, destroyed = 0;
            if (required <= 0) return 1f;
            var targets = sim.Run.Targets;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].Destroyed) destroyed++;
            return (float)destroyed / required;
        }

        /// <summary>
        /// Слоты, где есть полученные предметы (<paramref name="owned"/>), и из них — где надет лучший по эффекту
        /// (<paramref name="best"/>), как «Надеть лучшее».
        /// </summary>
        public static void EquipState(OutpostController game, out int owned, out int best)
        {
            owned = best = 0;
            var p = game.State?.Profile;
            var catalog = game.Configs?.Equipment;
            if (p == null || catalog == null) return;
            var defs = catalog.Defs;
            for (int s = 0; s < OutpostIds.SlotCount; s++)
            {
                var slot = (EquipSlot)s;
                bool any = false;
                var top = Rational.Zero;
                for (int i = 0; i < defs.Count; i++)
                {
                    var def = defs[i];
                    if (def.Slot != slot) continue;
                    int level = p.ItemLevel(def.Id);
                    if (level < 1) continue;
                    var v = catalog.EffectValue(def, level);
                    if (!any || v > top) top = v;
                    any = true;
                }
                if (!any) continue;
                owned++;
                string worn = p.Equipped != null && p.Equipped.Length > s ? p.Equipped[s] : null;
                if (worn != null && catalog.TryGet(worn, out var wd) && wd.Slot == slot && p.ItemLevel(worn) >= 1 &&
                    catalog.EffectValue(wd, p.ItemLevel(worn)) >= top) best++;
            }
        }

        /// <summary>Тайм-скипов в запасе (стартовый набор магазина).</summary>
        public static int SkipStock(OutpostController game)
        {
            var p = game.State?.Profile;
            var store = game.Configs?.Store;
            if (p == null || store == null) return 0;
            int n = 0;
            foreach (var def in store.Items)
                if (def.Kind == StoreItemKind.TimeSkip) n += p.ConsumableCount(def.Id);
            return n;
        }

        /// <summary>Тайм-скип из запаса, который покажет лапка в магазине (самый короткий с запасом), или null.</summary>
        public static StoreItemDef SkipInStock(OutpostController game)
        {
            var p = game.State?.Profile;
            var store = game.Configs?.Store;
            if (p == null || store == null) return null;
            foreach (var def in store.Items)
                if (def.Kind == StoreItemKind.TimeSkip && p.ConsumableCount(def.Id) > 0) return def;
            return null;
        }

        /// <summary>Сундук магазина за рекламу (тестовый режим) доступен сейчас, или null.</summary>
        public static StoreItemDef FreeAdChest(OutpostController game)
        {
            var p = game.State?.Profile;
            var store = game.Configs?.Store;
            if (p == null || store == null) return null;
            foreach (var def in store.Items)
            {
                if (def.Kind != StoreItemKind.Chest || def.Currency != StoreCurrency.Ad) continue;
                var quote = StoreService.Quote(p, store, def, game.UtcNowMs);
                if (quote.CanBuy && quote.Free) return def;
            }
            return null;
        }

        public static bool AdChestFree(OutpostController game) => FreeAdChest(game) != null;

        /// <summary>
        /// В шахте ещё выпадет сундук: завод в цепях (токен s, ТЗ 2.2 §4.2 — сундук rewardId выпадает, когда разбиты цепи; у
        /// открытого завода цель уже разрушена) или ворота с сундуком либо камень, чья очередная запись цикла наград (счётчик
        /// профиля по типу камня) — сундук. Сундук выхода не считается: он закрывает шахту.
        /// </summary>
        public static bool ChestAhead(OutpostSimulation sim, OutpostConfigSet configs)
        {
            var cards = configs?.Cards;
            if (cards == null) return false;
            var map = sim.Map;
            var run = sim.Run;
            Dictionary<string, int> rocks = null;
            for (int i = 0; i < map.Targets.Length && i < run.Targets.Count; i++)
            {
                if (run.Targets[i].Destroyed) continue;
                var mt = map.Targets[i];
                if (mt.Kind == 'r')
                {
                    if (mt.TypeId == null) continue;
                    if (rocks == null) rocks = new Dictionary<string, int>();
                    rocks.TryGetValue(mt.TypeId, out int n);
                    rocks[mt.TypeId] = n + 1;
                }
                else if ((mt.IsFactory || mt.IsGate) && mt.RewardId != null && cards.Chest(mt.RewardId) != null) return true;
            }
            if (rocks == null) return false;
            var cycles = sim.Profile.RockCycles;
            foreach (var pair in rocks)
            {
                var rock = cards.Rock(pair.Key);
                if (rock?.Cycle == null || rock.Cycle.Length == 0) continue;
                int counter = 0;
                if (cycles != null) cycles.TryGetValue(pair.Key, out counter);
                for (int k = 0; k < pair.Value && k < rock.Cycle.Length; k++)
                    if (rock.Cycle[(counter + k) % rock.Cycle.Length].Kind == RockRewardKind.Chest) return true;
            }
            return false;
        }
    }
}
