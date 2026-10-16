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

        /// <summary>Золото монет, сданных котиками (TotalCredited), в базовых монетах; товар сюда не входит (ТЗ 2.2 §6.4).</summary>
        public double CreditedBase;
        public double SpentBase;
        public double UnspentBase;
        /// <summary>Доставлено базовых монет в минуту за всю шахту (только монеты).</summary>
        public double IncomeBasePerMin;

        // ── Заводы и товар (ТЗ 2.2 §9) ──

        /// <summary>Золото продажи товара (Run.GoodsSoldGold) в базовых монетах — отдельно от монет CreditedBase.</summary>
        public double GoodsSoldBase;
        /// <summary>Выполненных моделью продаж «Sell all».</summary>
        public int Sales;
        /// <summary>Ручных доставок «Deliver» за шахту (Counters.ManualDeliveries).</summary>
        public int ManualDeliveries;
        /// <summary>
        /// Товар, пропавший при переходе (Р3): на складе и в трубах при ReadyToContinue, базовые монеты по цене Price(t, z) шахты.
        /// Готовые, но не доставленные партии ручных заводов — отдельно, <see cref="GoodsLostReadyBase"/>.
        /// </summary>
        public double GoodsLostBase;
        public double GoodsLostWarehouseBase;
        public double GoodsLostPipesBase;
        public double GoodsLostReadyBase;
        /// <summary>Заводы шахты по возрастанию ID (пусто — заводов нет).</summary>
        public List<FactoryAudit> Factories = new List<FactoryAudit>();
        /// <summary>Опыты отсутствия (ТЗ 2.2 §9, «вклад офлайна»); null — не проводились.</summary>
        public List<AbsenceAudit> Absences;

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
        /// <summary>Цена / доход последних 60 с (монеты + продажа товара, ТЗ 2.2 §6.4), с (null — дохода не было).</summary>
        public double? CostIncomeSec;
    }

    /// <summary>Завод в итоге шахты (ТЗ 2.2 §9): автодоставка в начале и в конце, открытие, циклы.</summary>
    public sealed class FactoryAudit
    {
        /// <summary>ID цели <c>s</c>.</summary>
        public int Id;
        public string TypeId;
        public string GoodId;
        /// <summary>Номер типа 1…20 (приложение A ТЗ 2.2).</summary>
        public int TypeNumber;
        public long CycleSec;
        public int Sector;
        /// <summary>Требование автодоставки токена: карточка и уровень.</summary>
        public string RequiredCardId;
        public int RequiredCardLevel;
        /// <summary>Уровень карточки требования в начале и в конце шахты (0 — нет копий).</summary>
        public int CardLevelStart;
        public int CardLevelEnd;
        /// <summary>Требование выполнено уже в начале шахты (карточки, улучшенные до неё).</summary>
        public bool AutoAtStart;
        /// <summary>Флаг Automated в конце шахты (с карточками сундуков этой шахты, в том числе сундука выхода).</summary>
        public bool AutoAtEnd;
        /// <summary>Цепи разбиты, с от начала шахты; null — не открыт.</summary>
        public double? UnlockSec;
        /// <summary>Первая отметка с включённой автодоставкой, с; null — не включалась.</summary>
        public double? AutoSec;
        /// <summary>Завершённых циклов за шахту.</summary>
        public int Cycles;
        /// <summary>Единиц товара за шахту.</summary>
        public long Produced;
        /// <summary>Выпуск за цикл в начале шахты (с бонусами карточек).</summary>
        public int UnitsStart;
        /// <summary>Цена единицы, базовые монеты шахты.</summary>
        public double PriceBase;
    }

    /// <summary>
    /// Опыт отсутствия (ТЗ 2.2 §9): та же шахта той же моделью с того же начала, но на отметке StartSec игрок уходит на Hours
    /// часов (ядро считает интервал тем же Advance, что и офлайн), затем возвращается и доигрывает шахту.
    /// </summary>
    public sealed class AbsenceAudit
    {
        public double Hours;
        public double StartSec;
        public double ReturnSec;
        /// <summary>Отсутствие кончилось до ReadyToContinue (шахта ещё шла).</summary>
        public bool ReturnedBeforeComplete;
        /// <summary>Завершение шахты в этом прогоне, с.</summary>
        public double? CompletedSec;
        public string Outcome;
        /// <summary>Заводов на автомате в момент ухода.</summary>
        public int AutoAtLeave;
        /// <summary>
        /// Заводов к возвращению: всего, ещё в цепях, на автомате (был при уходе или включился, пока шахта шла), ручных.
        /// </summary>
        public int Factories;
        public int Locked;
        public int AutoFactories;
        public int ManualFactories;
        /// <summary>Стоимость товара на складе к возвращению по цене Price(t, z): автоматических и ручных заводов, базовые монеты.</summary>
        public double WarehouseAutoBase;
        public double WarehouseManualBase;
        /// <summary>Готовые партии ручных заводов к возвращению (ждут «Deliver»).</summary>
        public double ReadyManualBase;
        /// <summary>Партии в трубах к возвращению.</summary>
        public double PipesBase;
        /// <summary>Монеты, сданные котиками за время отсутствия.</summary>
        public double CoinsDuringBase;
        /// <summary>Товар на складе и в трубах при ReadyToContinue этого прогона — пропал при переходе; готовые партии — отдельно.</summary>
        public double LostBase;
        public double LostReadyBase;
        /// <summary>Золото продажи товара за всю шахту этого прогона.</summary>
        public double GoodsSoldBase;
    }

    /// <summary>План отсутствия для <see cref="BalanceAudit.RunMine"/>.</summary>
    public sealed class AbsencePlan
    {
        public double StartSec;
        public double Hours;
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
        /// <summary>Золото продажи товара к этой отметке, базовые монеты.</summary>
        public double GoodsSoldBase;
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
        /// <summary>Правила заводов (ТЗ 2.2 §3.2): версия и PriceRate набора.</summary>
        public string FactoryRulesVersion;
        public string PriceRate;
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
