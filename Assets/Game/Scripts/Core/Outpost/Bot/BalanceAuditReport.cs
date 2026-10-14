using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Отчёт аудита баланса по одной шахте (Docs/Rework/BALANCE_AUDIT.md). Время — секунды логического времени ядра,
    /// деньги — в «базовых монетах» шахты: сумма / (CoinBase · S(z)), чтобы шахты разного масштаба сравнивались напрямую.
    /// </summary>
    public sealed class MineAudit
    {
        public int Zone;
        public string Scenario;
        public ulong Seed;
        public string Outcome;
        public double? CompletedSec;
        public double? ClearedSec;
        public double? FirstDeliverySec;
        public double? FirstPurchaseSec;
        /// <summary>Первая покупка «Команды» (первый дополнительный котик).</summary>
        public double? FirstHireSec;
        /// <summary>Доставка остатков: от расчистки до завершения.</summary>
        public double DrainSec;

        public int Targets;
        public int Gates;
        public int CargoUnits;
        public long TotalHp;
        /// <summary>Сумма груза шахты в базовых монетах без оценки, шляпы и карточек: Σ порций × коэффициент типа.</summary>
        public double RawYieldBase;
        /// <summary>Путь от точки приёма до ближайшей рабочей клетки цели по расчищенной карте (рёбра): среднее с весом груза и максимум.</summary>
        public double CargoWeightedDistance;
        public int MaxDistance;
        /// <summary>Сумма «хвостов» секторов: сколько секунд в секторе оставалась одна живая r/s/x.</summary>
        public double SectorTailSec;

        public int Purchases;
        public int[] Levels;
        public int WorkersStart;
        public int WorkersEnd;
        /// <summary>Паузы между соседними покупками (пока шахта в Running), с.</summary>
        public double MaxPurchaseGapSec;
        public double MedianPurchaseGapSec;
        /// <summary>Самый длинный отрезок Running, когда в кошельке меньше цены самого дешёвого доступного уровня.</summary>
        public double MaxUnaffordableSec;
        public double UnaffordableSec;
        /// <summary>Цена покупки / доход последних 60 с (сколько секунд дохода стоила покупка): медиана и максимум.</summary>
        public double MedianCostIncomeSec;
        public double MaxCostIncomeSec;
        public List<PurchaseRecord> PurchaseLog = new List<PurchaseRecord>();

        /// <summary>Котико-секунды по состояниям (WorkerMode) и итог.</summary>
        public Dictionary<string, double> WorkerSec = new Dictionary<string, double>(StringComparer.Ordinal);
        public double WorkerSecTotal;
        /// <summary>Среднее число котиков, занятых делом (не Idle, GoingHome, очередь склада и выбор задачи), пока шахта в Running.</summary>
        public double AvgBusyWorkers;
        public double AvgWorkers;

        public List<SectorAudit> Sectors = new List<SectorAudit>();
        /// <summary>Сумма по секторам: от расчистки r/s/x сектора до разрушения его ворот.</summary>
        public double GateWaitSec;
        public List<TargetAudit> SlowTargets = new List<TargetAudit>();
        public double LongestTargetSec;

        public double CreditedBase;
        public double SpentBase;
        public double UnspentBase;
        /// <summary>Доставлено базовых монет в минуту за всю шахту.</summary>
        public double IncomeBasePerMin;

        public int Strikes;
        /// <summary>Доля HP шахты, снятая ударами игрока.</summary>
        public double StrikeDamageShare;
        public int MaxQueue;
        public double AvgQueueWaitSec;

        public StartStats Start;
        public int CardsOwned;
        public int CardLevels;
        public long Elixir;
        public int ChestsOpened;
        public string[] Equipped;
        public int[] EquippedLevels;
        public long Parts;
        public string RewardItemId;
        public string Error;
        public List<TraceRow> Trace;
    }

    public sealed class PurchaseRecord
    {
        public double Sec;
        public string Branch;
        public int Level;
        public double CostBase;
        /// <summary>Цена / доход последних 60 с, с (null — дохода не было).</summary>
        public double? CostIncomeSec;
    }

    public sealed class SectorAudit
    {
        public int Sector;
        public int Targets;
        public long Hp;
        public int CargoUnits;
        public double? OpenSec;
        public double? RsxClearSec;
        public double? GateSec;
        public long GateHp;
        /// <summary>Сколько котиков могло одновременно бить ворота (рабочие клетки, не больше MaxWorkersPerTarget).</summary>
        public int GateWorkCells;
        /// <summary>Когда в секторе осталась одна живая r/s/x, и её прочность.</summary>
        public double? LastTargetSec;
        public long LastTargetHp;
    }

    public sealed class TargetAudit
    {
        public int Id;
        public char Kind;
        public int Sector;
        public long Hp;
        public double FirstDamageSec;
        public double DestroyedSec;
        public double DurationSec;
    }

    public sealed class StartStats
    {
        public int Workers;
        public double Damage;
        public double MoveSpeed;
        public int Capacity;
        public double UnloadRate;
        public double Value;
        public double TeamDiscount;
        public double BranchDiscount;
        public int TeamCapBonus;
        public double CritChance;
    }

    public sealed class TraceRow
    {
        public double Sec;
        public double WalletBase;
        public double CreditedBase;
        public int Workers;
        public int Sector;
        public int LevelSum;
        public int[] Modes;
    }

    /// <summary>Последовательная кампания одной модели игрока.</summary>
    public sealed class CampaignAudit
    {
        public string Scenario;
        public ulong Seed;
        public PlayerModel Model;
        public string BalanceVersion;
        public string BalanceHash;
        public string CatalogHash;
        public string TopologyVersion;
        public double TotalSec;
        public bool Success;
        public List<MineAudit> Mines = new List<MineAudit>();
    }

    /// <summary>Насыщение: та же шахта с фиксированными улучшениями и разным числом котиков, без покупок.</summary>
    public sealed class SaturationAudit
    {
        public int Zone;
        public string ProfileSource;
        public int[] FixedLevels;
        public List<SaturationRow> Rows = new List<SaturationRow>();
    }

    public sealed class SaturationRow
    {
        public int Workers;
        public double? ClearedSec;
        public double? CompletedSec;
        public double AvgBusyWorkers;
        public Dictionary<string, double> Share = new Dictionary<string, double>(StringComparer.Ordinal);
    }
}
