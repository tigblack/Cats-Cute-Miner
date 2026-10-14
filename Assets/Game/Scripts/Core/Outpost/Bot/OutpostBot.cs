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
        public string Credited;
        public string Spent;
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
            " events=" + Events + " units=" + UnitsCreated + " credited=" + Credited + " spent=" + Spent + " purchases=" + Purchases +
            " levels=[" + (FinalLevels == null ? "" : string.Join(",", FinalLevels)) + "] reward=" + RewardItemId + "+" + RewardParts;
    }

    /// <summary>Прогон зоны или кампании тем же ядром (ускоренный Advance меняет только скорость исполнения).</summary>
    public sealed class OutpostBot
    {
        /// <summary>24 часа симуляции.</summary>
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
        public int Purchases { get; private set; }

        /// <summary>
        /// Применить политику на текущей отметке через Submit; возвращает число покупок.
        /// Вызывается после каждого Advance до следующего события.
        /// </summary>
        public int ApplyPolicy(OutpostSimulation sim)
        {
            if (sim.IsEconomyStopped) return 0;
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
                    if (t > TickLimit)
                    {
                        report.Outcome = BotOutcome.Timeout;
                        break;
                    }
                    if (sim.ProcessedEvents - startEvents >= EventLimit)
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
