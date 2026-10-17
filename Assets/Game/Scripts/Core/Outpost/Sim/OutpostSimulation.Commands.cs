using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    public enum CommandKind
    {
        BuyUpgrade,
        Equip,
        Unequip,
        EquipBest,
        UpgradeItem,
        /// <summary>Удар кирки игрока по цели (тап/клик, ТЗ 6.6).</summary>
        Strike,
        /// <summary>Улучшение карточки ItemId: копии и эликсир → уровень.</summary>
        UpgradeCard,
        /// <summary>Покупка плитки магазина ItemId (сундук, эликсир, гемы) на момент UtcMs (ТЗ 10.5).</summary>
        StoreBuy,
        /// <summary>Золото Amount за рекламу (удвоение офлайн-дохода): в кошелёк и BonusCredited, не в доставку.</summary>
        BonusGold,
        /// <summary>ТЗ 2.2 §4.4: «Deliver» — готовая партия завода TargetId (Status Ready) уходит в трубу, начинается следующий цикл.</summary>
        DeliverFactory,
        /// <summary>ТЗ 2.2 §6.3: продажа всего остатка товара ItemId со склада; ItemId == null — всех товаров («Sell all»).</summary>
        SellGoods,
        /// <summary>ТЗ 10.6: награда бонуса за рекламу — вид ItemId (gold, elixir, gems, dynamite), сумма Amount.</summary>
        RewardBonus,
        /// <summary>ТЗ 10.6: взрыв бочки динамита, лежащей на поле.</summary>
        Detonate,
    }

    /// <summary>Команда игрока (ТЗ 6.5 п.6, CORE_SPEC §8). Переход и офлайн — не команды симуляции.</summary>
    public sealed class OutpostCommand
    {
        public CommandKind Kind;
        public UpgradeBranch Branch;
        public BuyMode Mode;
        public string ItemId;
        public EquipSlot Slot;
        /// <summary>Цель удара (Strike), завод доставки (DeliverFactory) или −1.</summary>
        public int TargetId = -1;
        /// <summary>UTC (мс) нажатия — для таймера рекламной плитки магазина (StoreBuy).</summary>
        public long UtcMs;
        /// <summary>Золото награды (BonusGold).</summary>
        public BigInteger Amount;

        public static OutpostCommand BuyUpgrade(UpgradeBranch branch, BuyMode mode = BuyMode.One) =>
            new OutpostCommand { Kind = CommandKind.BuyUpgrade, Branch = branch, Mode = mode };

        public static OutpostCommand Equip(string itemId) => new OutpostCommand { Kind = CommandKind.Equip, ItemId = itemId };
        public static OutpostCommand Unequip(EquipSlot slot) => new OutpostCommand { Kind = CommandKind.Unequip, Slot = slot };
        public static OutpostCommand EquipBest() => new OutpostCommand { Kind = CommandKind.EquipBest };
        public static OutpostCommand UpgradeItem(string itemId) => new OutpostCommand { Kind = CommandKind.UpgradeItem, ItemId = itemId };
        public static OutpostCommand Strike(int targetId) => new OutpostCommand { Kind = CommandKind.Strike, TargetId = targetId };
        public static OutpostCommand UpgradeCard(string cardId) => new OutpostCommand { Kind = CommandKind.UpgradeCard, ItemId = cardId };
        public static OutpostCommand StoreBuy(string itemId, long utcMs) => new OutpostCommand { Kind = CommandKind.StoreBuy, ItemId = itemId, UtcMs = utcMs };
        public static OutpostCommand BonusGold(BigInteger amount) => new OutpostCommand { Kind = CommandKind.BonusGold, Amount = amount };
        /// <summary>«Deliver» завода (ID цели <c>s</c>).</summary>
        public static OutpostCommand DeliverFactory(int targetId) => new OutpostCommand { Kind = CommandKind.DeliverFactory, TargetId = targetId };
        /// <summary>Продать весь остаток товара goodId; null — продать всё («Sell all»).</summary>
        public static OutpostCommand SellGoods(string goodId) => new OutpostCommand { Kind = CommandKind.SellGoods, ItemId = goodId };
        public static OutpostCommand RewardBonus(RewardedBonusKind kind, BigInteger amount) =>
            new OutpostCommand { Kind = CommandKind.RewardBonus, ItemId = RewardedBonus.Id(kind), Amount = amount };
        public static OutpostCommand Detonate() => new OutpostCommand { Kind = CommandKind.Detonate };

        public override string ToString() => Kind + " " + (Kind == CommandKind.BuyUpgrade ? Branch + "×" + Mode : Kind == CommandKind.Unequip ? Slot.ToString() :
            Kind == CommandKind.Strike || Kind == CommandKind.DeliverFactory ? "t" + TargetId :
            Kind == CommandKind.SellGoods && ItemId == null ? "all" : ItemId);
    }

    /// <summary>Результат команды; отказ (Ok = false) не меняет состояние.</summary>
    public struct CommandResult
    {
        public bool Ok;
        /// <summary>
        /// Причина отказа: "cap", "gold", "phase", "mode", "unknown", "not-owned", "already", "empty", "max", "parts";
        /// удара — "phase", "disabled", "unknown", "destroyed", "locked"; карточки — "unknown", "not-owned", "max", "copies", "elixir";
        /// магазина — "unknown", "gems", "cooldown", "unavailable";
        /// «Deliver» (ТЗ 2.2) — "phase" (ReadyToContinue), "unknown" (нет такого завода), "locked" (цепи целы), "not-ready" (нет готовой партии);
        /// продажи — "no-factories" (в шахте нет заводов), "phase" (ReadyToContinue), "unknown" (товара нет среди заводов шахты),
        /// "no-goods" (на складе нечего продавать).
        /// </summary>
        public string Reason;
        /// <summary>Купленные уровни; у удара — выпущенные порции; у «Deliver» и продажи — единиц товара.</summary>
        public int Levels;
        /// <summary>Списанное золото (покупка), детали (улучшение предмета), урон удара в долях HP; у продажи — полученное золото.</summary>
        public BigInteger Cost;

        public static CommandResult Fail(string reason) => new CommandResult { Ok = false, Reason = reason };
        public override string ToString() => Ok ? "ok levels=" + Levels + " cost=" + Cost : "fail " + Reason;
    }

    public sealed partial class OutpostSimulation
    {
        /// <summary>Немедленно на отметке Now: фаза 6 (одна команда) + фаза 7. Отказ ничего не меняет.</summary>
        public CommandResult Submit(OutpostCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            var result = Execute(cmd, Run.Now);
            if (result.Ok) Plan(Run.Now);
            return result;
        }

        /// <summary>Запланировать команду в фазу 6 отметки tick (&gt; Now); команды одной отметки — в порядке поступления.</summary>
        public void Schedule(OutpostCommand cmd, long tick)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (tick <= Run.Now) throw new ArgumentOutOfRangeException(nameof(tick), tick, "Отметка команды должна быть позже Now = " + Run.Now);
            int i = m_Scheduled.Count;
            while (i > 0 && m_Scheduled[i - 1].Tick > tick) i--;
            m_Scheduled.Insert(i, new ScheduledCommand { Tick = tick, Seq = m_ScheduleSeq++, Command = cmd });
        }

        /// <summary>Котировка покупки по текущему состоянию (со скидками и пределом карточек).</summary>
        public UpgradeQuote Quote(UpgradeBranch branch, BuyMode mode) => TeamUpgradeService.Quote(Run, branch, mode, m_StatsDirty ? ComputeStats() : m_Stats);

        CommandResult Execute(OutpostCommand cmd, long t)
        {
            switch (cmd.Kind)
            {
                case CommandKind.BuyUpgrade: return ExecuteBuy(cmd, t);
                case CommandKind.Equip:
                {
                    string reason = EquipmentService.Equip(Profile, m_Configs.Equipment, cmd.ItemId);
                    if (reason != null) return CommandResult.Fail(reason);
                    OnEquipmentChanged(t, cmd.ItemId, (int)m_Configs.Equipment.Get(cmd.ItemId).Slot);
                    return new CommandResult { Ok = true };
                }
                case CommandKind.Unequip:
                {
                    string reason = EquipmentService.Unequip(Profile, cmd.Slot);
                    if (reason != null) return CommandResult.Fail(reason);
                    OnEquipmentChanged(t, null, (int)cmd.Slot);
                    return new CommandResult { Ok = true };
                }
                case CommandKind.EquipBest:
                {
                    // Комплект уже лучший — команда выполнена без изменений (не отказ): состояние и события не меняются.
                    if (EquipmentService.EquipBest(Profile, m_Configs.Equipment)) OnEquipmentChanged(t, null, -1);
                    return new CommandResult { Ok = true };
                }
                case CommandKind.UpgradeItem:
                {
                    string reason = EquipmentService.UpgradeItem(Profile, m_Configs.Equipment, cmd.ItemId, out long cost);
                    if (reason != null) return CommandResult.Fail(reason);
                    m_StatsDirty = true;
                    Run.PlanningDirty = true;
                    var e = OutpostEvent.Create(OutpostEventType.ItemUpgraded, t);
                    e.ItemId = cmd.ItemId;
                    e.Count = Profile.ItemLevel(cmd.ItemId);
                    e.Value = cost;
                    Emit(e);
                    return new CommandResult { Ok = true, Levels = 1, Cost = cost };
                }
                case CommandKind.Strike: return ExecuteStrike(cmd, t);
                case CommandKind.UpgradeCard: return ExecuteUpgradeCard(cmd, t);
                case CommandKind.StoreBuy: return ExecuteStoreBuy(cmd, t);
                case CommandKind.BonusGold: return ExecuteBonusGold(cmd, t);
                case CommandKind.DeliverFactory: return ExecuteDeliverFactory(cmd, t);
                case CommandKind.SellGoods: return ExecuteSellGoods(cmd, t);
                case CommandKind.RewardBonus: return ExecuteRewardBonus(cmd, t);
                case CommandKind.Detonate: return ExecuteDetonate(cmd, t);
                default:
                    return CommandResult.Fail("unknown");
            }
        }

        CommandResult ExecuteBuy(OutpostCommand cmd, long t)
        {
            var run = Run;
            var q = TeamUpgradeService.Quote(run, cmd.Branch, cmd.Mode, m_StatsDirty ? ComputeStats() : m_Stats);
            if (!q.CanBuy) return CommandResult.Fail(q.BlockReason ?? "gold");
            run.Wallet -= q.Cost;
            run.TotalSpent += q.Cost;
            run.Levels[(int)cmd.Branch] += q.Levels;
            if (run.Counters.FirstPurchaseTick < 0) run.Counters.FirstPurchaseTick = t;
            m_StatsDirty = true;
            run.PlanningDirty = true;
            Touch(t);
            var e = OutpostEvent.Create(OutpostEventType.TeamUpgradePurchased, t);
            e.Branch = (int)cmd.Branch;
            e.Count = q.Levels;
            e.Value = q.Cost;
            Emit(e);
            if (cmd.Branch == UpgradeBranch.Team)
                for (int i = 0; i < q.Levels; i++) HireWorker(t);
            return new CommandResult { Ok = true, Levels = q.Levels, Cost = q.Cost };
        }

        /// <summary>Найм: новый работник в SpawnCell, сразу в планировщике (ТЗ 9).</summary>
        WorkerState HireWorker(long t)
        {
            var run = Run;
            int id = run.NextWorkerId++;
            var w = new WorkerState
            {
                Id = id,
                Look = (id - 1) % LookCount,
                Mode = WorkerMode.ChoosingTask,
                Cell = m_Map.SpawnCell,
            };
            run.Workers.Add(w);
            var e = OutpostEvent.Create(OutpostEventType.WorkerHired, t);
            e.WorkerId = id;
            e.Cell = w.Cell;
            Emit(e);
            return w;
        }

        // ───────────── Заводы и рынок (ТЗ 2.2): выполнение команд — OutpostSimulation.Factories.cs ─────────────

        /// <summary>Завод по ID цели или null (не завод / в шахте нет заводов).</summary>
        public FactoryState FactoryById(int targetId) => FactoryQueries.Find(Run, targetId);

        /// <summary>Причина отказа «Deliver» или null (можно доставить). Коды — <see cref="CommandResult.Reason"/>.</summary>
        public string DeliverBlockReason(int targetId) => FactoryQueries.DeliverBlockReason(Run, targetId);

        /// <summary>Причина отказа продажи goodId (null — «Sell all») или null. Кнопка Market: "no-factories"/"phase" — неактивна.</summary>
        public string SellBlockReason(string goodId) => FactoryQueries.SellBlockReason(Run, goodId);

        /// <summary>Количество товара на складе (0 — нет).</summary>
        public long GoodsCount(string goodId) => FactoryQueries.Stock(Run, goodId);

        /// <summary>Цена единицы товара в этой шахте (ceil(CoinBase × S(z) × PriceFactor), снимки run); 0 — товара нет в правилах.</summary>
        public BigInteger GoodsPrice(string goodId) => FactoryQueries.Price(Run, goodId);

        /// <summary>Стоимость склада: goodId — одного товара, null — всего («Total value», «Sell all +X»).</summary>
        public BigInteger WarehouseValue(string goodId = null) => FactoryQueries.StockValue(Run, goodId);

        /// <summary>
        /// Выпуск за цикл завода targetId сейчас (§4.4) — <see cref="FactoryMath.UnitsPerCycle"/>(строка снимка Run.FactoryRules,
        /// профиль, каталог карточек): floor(UnitsPerCycle × (1 + Σ Output(L) Common и Uncommon карточек типа по текущим уровням));
        /// 0 — не завод или в run нет снимка правил. Сведения завода («20 Milk»), бот, отчёты. Столько же единиц зафиксирует конец
        /// цикла на этой отметке.
        /// </summary>
        public int FactoryUnitsPerCycle(int targetId)
        {
            var factory = m_Map.Factory(targetId);
            var rule = factory != null ? Run.FactoryRules?.ForType(factory.TypeId) : null;
            return FactoryMath.UnitsPerCycle(rule, Profile, m_Cards);
        }

        /// <summary>
        /// Время в пути партии завода targetId, тики (§5): <see cref="FactoryMath.TransitTicks"/>(снимок правил run,
        /// <see cref="MapFactory.RouteSteps"/>); 0 — не завод или в run нет снимка правил.
        /// </summary>
        public long FactoryTransitTicks(int targetId)
        {
            var factory = m_Map.Factory(targetId);
            var rules = Run.FactoryRules;
            return factory == null || rules == null ? 0 : FactoryMath.TransitTicks(rules, factory.RouteSteps);
        }

        void OnEquipmentChanged(long t, string itemId, int slot)
        {
            m_StatsDirty = true;
            Run.PlanningDirty = true;
            var e = OutpostEvent.Create(OutpostEventType.EquipmentChanged, t);
            e.ItemId = itemId;
            e.Slot = slot;
            Emit(e);
        }
    }
}
