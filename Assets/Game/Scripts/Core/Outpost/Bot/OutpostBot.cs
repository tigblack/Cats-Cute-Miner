using System;
using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Воспроизводимые тестовые политики (ТЗ 20): A — без покупок; B — «Кирки» ×1 при доступности до cap;
    /// C — круговой список «Кирки, Команда, Сумки, Обувь, Разгрузка, Оценка», после покупки — следующая ветка,
    /// недоступные/cap пропускаются, ×1 из доставленного кошелька. Не утверждение об оптимальности.
    /// Заводы (ТЗ 2.2 §9) — у всех политик одинаково: перед покупками бот доставляет все готовые партии и продаёт весь товар
    /// склада (<see cref="OutpostBot.TendFactories"/>); A по-прежнему ничего не покупает.
    /// </summary>
    public enum BotPolicy
    {
        A,
        B,
        C,
    }

    public enum BotOutcome
    {
        Completed,
        Timeout,
        EventLimit,
        Watchdog,
        Error,
    }

    /// <summary>Отчёт прогона одной карты (ТЗ 20): времена в секундах симуляции, деньги — десятичные строки.</summary>
    public sealed class BotRunReport
    {
        public int Zone;
        public string ZoneId;
        public BotPolicy Policy;
        public string EquipmentLabel;
        public ulong Seed;
        public BotOutcome Outcome;
        public string BalanceVersion;
        public string BalanceHash;
        public string CatalogHash;
        public string SourceSha256;
        public double? FirstDeliverySec;
        /// <summary>null, если покупок не было.</summary>
        public double? FirstPurchaseSec;
        public double? ClearedSec;
        public double? CompletedSec;
        public long Events;
        public int Ticks;
        public int UnitsCreated;
        public string ValueCreated;
        /// <summary>Золото монет (TotalCredited); товар — отдельно, <see cref="GoodsSoldGold"/> (ТЗ 2.2 §6.4).</summary>
        public string Credited;
        public string Spent;
        /// <summary>Золото продажи товара за шахту (Run.GoodsSoldGold), десятичная строка.</summary>
        public string GoodsSoldGold;
        /// <summary>Единиц товара продано.</summary>
        public long GoodsSoldUnits;
        /// <summary>Ручных доставок «Deliver» (Counters.ManualDeliveries).</summary>
        public int ManualDeliveries;
        /// <summary>Заводов в шахте, из них с автодоставкой к концу; завершённых циклов всех заводов.</summary>
        public int Factories;
        public int FactoriesAutomated;
        public int FactoryCycles;
        public int Purchases;
        public int MaxQueue;
        public double AvgQueueWaitSec;
        public string RewardItemId;
        public int RewardParts;
        public string WatchdogDiagnostic;
        public string Error;
        public int[] FinalLevels;
        public int Workers;
        public string[] Equipped;
        public List<string> InvariantErrors = new List<string>();

        public bool Success => Outcome == BotOutcome.Completed && InvariantErrors.Count == 0;

        public override string ToString() =>
            ZoneId + " " + Policy + " " + Outcome + " completed=" + (CompletedSec?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "-") +
            "s firstDelivery=" + (FirstDeliverySec?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "-") +
            "s firstPurchase=" + (FirstPurchaseSec?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "null") +
            " events=" + Events + " units=" + UnitsCreated + " credited=" + Credited + " goods=" + GoodsSoldGold + " spent=" + Spent + " purchases=" + Purchases +
            " levels=[" + (FinalLevels == null ? "" : string.Join(",", FinalLevels)) + "] reward=" + RewardItemId + "+" + RewardParts;
    }

    /// <summary>Прогон зоны или кампании тем же ядром (ускоренный Advance меняет только скорость исполнения).</summary>
    public sealed class OutpostBot
    {
        /// <summary>Множитель пределов прогона шахты: ceil(K(z)) профиля run (1 без множителя темпа, профиль b6).</summary>
        public static long PaceLimitFactor(OutpostRun run)
        {
            var k = run?.Balance?.ZonePace(run.Zone) ?? Rational.One;
            return k <= Rational.One ? 1L : (long)k.Ceil();
        }

        /// <summary>24 часа симуляции (шахта с K(z) &gt; 1 — ещё × <see cref="PaceLimitFactor"/>).</summary>
        public const long DefaultTickLimit = 86_400_000_000L;
        public const long DefaultEventLimit = 2_000_000L;

        static readonly UpgradeBranch[] s_CycleC =
        {
            UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value,
        };

        readonly BotPolicy m_Policy;
        int m_CycleIndex;

        public OutpostBot(BotPolicy policy)
        {
            m_Policy = policy;
        }

        public long TickLimit = DefaultTickLimit;
        public long EventLimit = DefaultEventLimit;
        /// <summary>Проверять инварианты после каждой отметки (медленно; для тестов целостности).</summary>
        public bool CheckInvariantsEveryTick;
        /// <summary>Проверять инварианты в конце прогона.</summary>
        public bool CheckInvariantsAtEnd = true;
        /// <summary>Доставлять готовые партии и продавать товар (ТЗ 2.2 §9); по умолчанию включено у всех политик.</summary>
        public bool ManageFactories = true;
        public int Purchases { get; private set; }

        /// <summary>
        /// Заводы глазами игрока (ТЗ 2.2 §9): «Deliver» по каждому заводу в Ready (по возрастанию ID), затем «Sell all», если на
        /// складе есть товар. Команды идут через Submit (фаза 6) и только когда есть что делать, поэтому в шахте без заводов и без
        /// готовых партий состояние не трогается. Общая для бота и моделей игрока аудита. Возвращает число доставок и золото продажи.
        /// </summary>
        public static (int Delivered, BigInteger Sold) TendFactories(OutpostSimulation sim)
        {
            if (sim == null || sim.IsEconomyStopped) return (0, BigInteger.Zero);
            var factories = sim.Run.Factories;
            if (factories == null || factories.Count == 0) return (0, BigInteger.Zero);
            int delivered = 0;
            for (int i = 0; i < factories.Count; i++)
            {
                var f = factories[i];
                if (f.Status != FactoryStatus.Ready || f.ReadyUnits <= 0) continue;
                if (sim.Submit(OutpostCommand.DeliverFactory(f.TargetId)).Ok) delivered++;
            }
            var sold = BigInteger.Zero;
            if (sim.SellBlockReason(null) == null)
            {
                var r = sim.Submit(OutpostCommand.SellGoods(null));
                if (r.Ok) sold = r.Cost;
            }
            return (delivered, sold);
        }

        /// <summary>
        /// Применить политику на текущей отметке через Submit; возвращает число покупок.
        /// Вызывается после каждого Advance до следующего события.
        /// </summary>
        public int ApplyPolicy(OutpostSimulation sim)
        {
            if (sim.IsEconomyStopped) return 0;
            if (ManageFactories) TendFactories(sim);
            int bought = 0;
            switch (m_Policy)
            {
                case BotPolicy.A:
                    return 0;
                case BotPolicy.B:
                    while (sim.Quote(UpgradeBranch.Pickaxes, BuyMode.One).CanBuy && sim.Submit(OutpostCommand.BuyUpgrade(UpgradeBranch.Pickaxes)).Ok) bought++;
                    break;
                case BotPolicy.C:
                    while (true)
                    {
                        bool any = false;
                        for (int k = 0; k < s_CycleC.Length; k++)
                        {
                            int idx = (m_CycleIndex + k) % s_CycleC.Length;
                            var br = s_CycleC[idx];
                            if (!sim.Quote(br, BuyMode.One).CanBuy) continue;
                            if (!sim.Submit(OutpostCommand.BuyUpgrade(br)).Ok) continue;
                            m_CycleIndex = (idx + 1) % s_CycleC.Length;
                            bought++;
                            any = true;
                            break;
                        }
                        if (!any) break;
                    }
                    break;
            }
            Purchases += bought;
            return bought;
        }

        /// <summary>Прогон до ReadyToContinue, лимита времени/событий или watchdog над готовой симуляцией.</summary>
        public BotRunReport Run(OutpostSimulation sim, string equipmentLabel = "empty")
        {
            sim.RecordEvents = false;
            var state = sim.State;
            var run = state.Run;
            var report = new BotRunReport
            {
                Zone = run.Zone,
                ZoneId = run.ZoneId,
                Policy = m_Policy,
                EquipmentLabel = equipmentLabel,
                Seed = state.Profile.Seed,
                BalanceVersion = run.Balance.Version,
                BalanceHash = run.Balance.ComputeHash(),
                CatalogHash = sim.Configs.CatalogHash,
                SourceSha256 = run.SourceSha256,
                Equipped = (string[])state.Profile.Equipped.Clone(),
            };
            long startEvents = sim.ProcessedEvents;
            // Профиль b6: шахта с множителем темпа K(z) — растянутая в K раз копия прежней (HP и груз ×K), пределы растут так же.
            long pace = PaceLimitFactor(run);
            long tickLimit = TickLimit > long.MaxValue / pace ? long.MaxValue : TickLimit * pace;
            long eventLimit = EventLimit > long.MaxValue / pace ? long.MaxValue : EventLimit * pace;
            try
            {
                ApplyPolicy(sim);
                while (!sim.IsEconomyStopped)
                {
                    if (run.WatchdogFired)
                    {
                        report.Outcome = BotOutcome.Watchdog;
                        break;
                    }
                    long t = sim.NextEventTick();
                    if (t == long.MaxValue)
                    {
                        sim.AdvanceToNextEvent(); // зафиксирует watchdog «нет событий»
                        report.Outcome = BotOutcome.Watchdog;
                        break;
                    }
                    if (t > tickLimit)
                    {
                        report.Outcome = BotOutcome.Timeout;
                        break;
                    }
                    if (sim.ProcessedEvents - startEvents >= eventLimit)
                    {
                        report.Outcome = BotOutcome.EventLimit;
                        break;
                    }
                    var r = sim.Advance(t);
                    report.Ticks += r.ProcessedTicks;
                    if (CheckInvariantsEveryTick)
                    {
                        var errs = OutpostInvariants.Check(state, sim.Map, sim.Configs.Equipment, sim.Configs.Cards);
                        if (errs.Count > 0)
                        {
                            report.InvariantErrors.AddRange(errs);
                            report.Outcome = BotOutcome.Error;
                            report.Error = "инварианты на отметке " + t;
                            break;
                        }
                    }
                    ApplyPolicy(sim);
                }
                if (sim.IsEconomyStopped) report.Outcome = BotOutcome.Completed;
                else if (run.WatchdogFired && report.Outcome == BotOutcome.Completed) report.Outcome = BotOutcome.Watchdog;
            }
            catch (Exception e)
            {
                report.Outcome = BotOutcome.Error;
                report.Error = e.GetType().Name + ": " + e.Message;
            }
            if (CheckInvariantsAtEnd && report.InvariantErrors.Count == 0)
                report.InvariantErrors.AddRange(OutpostInvariants.Check(state, sim.Map, sim.Configs.Equipment, sim.Configs.Cards));
            Fill(report, sim, startEvents);
            return report;
        }

        void Fill(BotRunReport report, OutpostSimulation sim, long startEvents)
        {
            var run = sim.Run;
            var c = run.Counters;
            double Sec(long ticks) => (double)ticks / run.Balance.TicksPerSecond;
            report.FirstDeliverySec = c.FirstDeliveryTick >= 0 ? Sec(c.FirstDeliveryTick) : (double?)null;
            report.FirstPurchaseSec = c.FirstPurchaseTick >= 0 ? Sec(c.FirstPurchaseTick) : (double?)null;
            report.ClearedSec = c.ClearedTick >= 0 ? Sec(c.ClearedTick) : (double?)null;
            report.CompletedSec = c.CompletedTick >= 0 ? Sec(c.CompletedTick) : (double?)null;
            report.Events = sim.ProcessedEvents - startEvents;
            report.UnitsCreated = c.ReleasedUnits;
            report.ValueCreated = MoneyFormat.ToStorage(c.ReleasedValue);
            report.Credited = MoneyFormat.ToStorage(run.TotalCredited);
            report.Spent = MoneyFormat.ToStorage(run.TotalSpent);
            report.GoodsSoldGold = MoneyFormat.ToStorage(run.GoodsSoldGold);
            report.ManualDeliveries = c.ManualDeliveries;
            if (run.GoodsSold != null)
                foreach (var kv in run.GoodsSold) report.GoodsSoldUnits += kv.Value;
            if (run.Factories != null)
                foreach (var f in run.Factories)
                {
                    report.Factories++;
                    if (f.Automated) report.FactoriesAutomated++;
                    report.FactoryCycles += f.Cycles;
                }
            report.Purchases = Purchases;
            report.MaxQueue = c.MaxQueue;
            report.AvgQueueWaitSec = c.QueueServed > 0 ? Sec(c.QueueWaitTicks) / c.QueueServed : 0;
            report.WatchdogDiagnostic = run.WatchdogDiagnostic;
            report.FinalLevels = (int[])run.Levels.Clone();
            report.Workers = run.Workers.Count;
            var reward = StageProgressionService.CompletionReward(sim.State);
            if (reward != null)
            {
                report.RewardItemId = reward.ItemId;
                report.RewardParts = reward.CompletionParts + reward.DuplicateParts;
            }
        }

        /// <summary>Изолированная карта: чистая сессия выбранной зоны с пустым (или заданным) комплектом.</summary>
        public static BotRunReport RunZone(BalanceRepository repo, OutpostConfigSet configs, int zone, BotPolicy policy, ulong seed = 1,
            OutpostProfile equipmentFrom = null, string equipmentLabel = null, Action<OutpostBot> setup = null)
        {
            var profile = StageProgressionService.NewProfile(configs, seed, zone);
            if (equipmentFrom != null)
            {
                profile.Items = new Dictionary<string, int>(equipmentFrom.Items, StringComparer.Ordinal);
                profile.Equipped = (string[])equipmentFrom.Equipped.Clone();
                profile.Parts = equipmentFrom.Parts;
            }
            var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
            var state = new OutpostGameState { Profile = profile, Run = StageProgressionService.CreateRun(profile, zone, map, configs, repo, 0) };
            var sim = new OutpostSimulation(state, map, configs);
            var bot = new OutpostBot(policy);
            setup?.Invoke(bot);
            return bot.Run(sim, equipmentLabel ?? (equipmentFrom == null ? "empty" : "profile"));
        }

        /// <summary>
        /// Последовательная кампания: зоны fromZone..toZone, награды/детали/экипировка переносятся, перед каждой зоной —
        /// «Надеть лучшее» (если equipBest). Останавливается на первой неуспешной зоне.
        /// </summary>
        public static List<BotRunReport> RunCampaign(BalanceRepository repo, OutpostConfigSet configs, BotPolicy policy, ulong seed,
            int toZone, bool equipBest = true, Action<OutpostBot> setup = null)
        {
            var reports = new List<BotRunReport>();
            var state = StageProgressionService.NewGame(repo, configs, seed, 0, 1);
            var map = OutpostLevelAdapter.BuildMine(repo, 1, configs);
            while (true)
            {
                var sim = new OutpostSimulation(state, map, configs);
                if (equipBest) sim.Submit(OutpostCommand.EquipBest());
                var bot = new OutpostBot(policy);
                setup?.Invoke(bot);
                var report = bot.Run(sim, "campaign-seed" + seed);
                reports.Add(report);
                if (!report.Success || state.Run.Zone >= toZone) break;
                var tr = StageProgressionService.PrepareTransition(state, repo, configs, 0);
                if (!tr.Ok) break;
                state = tr.State;
                map = tr.Map;
            }
            return reports;
        }
    }
}
