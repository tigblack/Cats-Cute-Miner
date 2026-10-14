using System;
using System.Collections.Generic;
using System.Numerics;

// Сериализуемое состояние режима CatOutpostV2 (ТЗ 17): постоянный профиль игрока и состояние текущей шахты.
// Публичные поля, Newtonsoft с OutpostJson.Settings. Производные кэши (занятость клеток, BFS, Stats) не сохраняются.
namespace CatMiners.Core.Outpost
{
    [Serializable]
    public sealed class OutpostGameState
    {
        public OutpostProfile Profile = new OutpostProfile();
        public OutpostRun Run;
    }

    /// <summary>Постоянный профиль: переносится между шахтами.</summary>
    [Serializable]
    public sealed class OutpostProfile
    {
        public string ModeId = OutpostIds.ModeId;
        public int SchemaVersion = OutpostIds.SchemaVersion;
        /// <summary>Seed профиля: создаётся один раз и сохраняется.</summary>
        public ulong Seed;
        /// <summary>Генератор наград (поток 5): State/Inc.</summary>
        public Pcg32 RewardRng;
        public int RewardRngVersion = OutpostIds.RewardRngVersion;
        /// <summary>Зона текущего run.</summary>
        public int CurrentZone = 1;
        public bool CampaignComplete;
        /// <summary>Завершённые шахты по возрастанию.</summary>
        public List<int> CompletedZones = new List<int>();
        public List<RewardRecord> RewardLog = new List<RewardRecord>();
        /// <summary>itemId → уровень 1..MaxItemLevel; JSON — по возрастанию ключа.</summary>
        public Dictionary<string, int> Items = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Надетый комплект по <see cref="EquipSlot"/>; null — пустой слот.</summary>
        public string[] Equipped = new string[OutpostIds.SlotCount];
        public long Parts;
        public long NextRunSeq = 1;
        /// <summary>Реальное время активной игры (мс), без офлайна и тайм-скипов; сохраняется при начале заново.</summary>
        public long PlayTimeMs;
        public TutorialState Tutorial = new TutorialState();
        public OutpostSettings Settings = new OutpostSettings();

        // ── Карточки (Docs/Rework/CARD_EFFECT_MAPPING.md) ──
        /// <summary>
        /// Версия данных карточек: 0 — профиль создан до возвращения карточек (ждёт однократной миграции
        /// <see cref="CardService.MigrateProfile"/>), 1 — cards-1.
        /// </summary>
        public int CardsVersion;
        /// <summary>Карточка → прогресс; JSON — по возрастанию ID. Нет записи — карточка не получена.</summary>
        public Dictionary<string, CardProgress> Cards = new Dictionary<string, CardProgress>(StringComparer.Ordinal);
        /// <summary>Эликсир — постоянная валюта улучшения карточек (не золото шахты и не детали).</summary>
        public long Elixir;
        /// <summary>Максимальная достигнутая шахта кампании (вход в неё): карточка доступна при HighestZone ≥ MineUnlock.</summary>
        public int HighestZone;
        /// <summary>Счётчики циклов наград камней по Rocks[].Id (переносятся между шахтами, как в прежней игре).</summary>
        public Dictionary<string, int> RockCycles = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Генератор сундуков карточек (поток CardRules.CardRngStream).</summary>
        public Pcg32 CardRng;
        /// <summary>Ключи однократных выдач карточек: учебная карта, перенос из прежнего сохранения.</summary>
        public List<string> CardKeys = new List<string>();

        // ── Магазин (ТЗ 10.5, Resources/Outpost/store_catalog.json) ──
        /// <summary>
        /// Версия данных магазина: 0 — профиль создан до магазина (ждёт однократной выдачи стартового набора
        /// <see cref="StoreService.MigrateProfile"/>), 1 — набор выдан (с каталога store-1; store-2 изменил только длительности скипов).
        /// </summary>
        public PurchaseIntent PendingPurchase;
        public List<PaymentDelivery> Payments = new List<PaymentDelivery>();
        public RewardedOffer PendingRewardedOffer;
        public List<string> DeliveredRewardedOffers = new List<string>();
        public int StoreVersion;
        /// <summary>Гемы — hard-валюта магазина (покупки сундуков, тайм-скипов и эликсира).</summary>
        public long Gems;
        /// <summary>Расходники магазина: ID плитки тайм-скипа → количество в наличии; JSON — по возрастанию ключа.</summary>
        public Dictionary<string, int> Consumables = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Рекламные плитки: ID → UTC (мс), с которого плитка снова доступна.</summary>
        public Dictionary<string, long> StoreCooldownUntilUtcMs = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>Пакеты, бонус первой покупки которых уже получен.</summary>
        public List<string> StoreBonusUsed = new List<string>();

        public int ItemLevel(string itemId) => itemId != null && Items != null && Items.TryGetValue(itemId, out int q) ? q : 0;

        /// <summary>Уровень карточки (0 — не получена).</summary>
        public int CardLevel(string cardId) => cardId != null && Cards != null && Cards.TryGetValue(cardId, out var c) && c != null ? c.Level : 0;

        /// <summary>Сколько расходника магазина в наличии (0 — нет).</summary>
        public int ConsumableCount(string itemId) => itemId != null && Consumables != null && Consumables.TryGetValue(itemId, out int n) ? n : 0;
    }

    /// <summary>Прогресс карточки: первая копия даёт уровень 1, следующие — копии для улучшения.</summary>
    [Serializable]
    public sealed class CardProgress
    {
        public int Level;
        /// <summary>Копии сверх первой, ещё не потраченные на улучшение.</summary>
        public long Copies;
    }

    [Serializable]
    public sealed class RewardRecord
    {
        /// <summary>"CatOutpostV2:zoneN:first".</summary>
        public string Key;
        public int Zone;
        public string RunId;
        public string ItemId;
        public bool Duplicate;
        public int DuplicateParts;
        public int CompletionParts;
        public bool Guaranteed;
    }

    [Serializable]
    public sealed class TutorialState
    {
        public int Step;
        public bool Dismissed;
        public List<string> Seen = new List<string>();
    }

    [Serializable]
    public sealed class OutpostSettings
    {
        public float Master = 1f;
        public float Music = 0.6f;
        public float Sfx = 1f;
        public float Ambience = 0.7f;
        public bool Vibration = true;
        public bool ReduceEffects;
        public bool LegacySettingsImported;
    }

    /// <summary>Состояние текущей шахты.</summary>
    [Serializable]
    public sealed class OutpostRun
    {
        /// <summary>"run-{NextRunSeq}".</summary>
        public string RunId;
        public int Zone;
        public string ZoneId;
        public string SourceSha256;
        public string RulesVersion = OutpostIds.RulesVersion;
        public string AdapterVersion = OutpostIds.AdapterVersion;
        /// <summary>
        /// Копия override зоны с геометрией, с которой создана карта шахты, или null (исходная карта; так же читаются run,
        /// созданные до появления override). Карта сохранённой шахты строится по ней (<see cref="OutpostLevelAdapter.ForRun"/>).
        /// </summary>
        public ZoneOverride MapOverride;
        /// <summary>Saved geometry. Null/v1 can be explicitly migrated to wide mines after validation and archival.</summary>
        public MineTopology MapTopology;
        /// <summary>Разрешённый снимок профиля чисел (целиком).</summary>
        public OutpostBalanceConfig Balance;
        public RunPhase Phase;
        /// <summary>Логическое время, тики.</summary>
        public long Now;
        /// <summary>UTC-граница уже обработанного интервала (офлайн, ТЗ 13).</summary>
        public long LastProcessedUtcMs;
        public BigInteger Wallet;
        public BigInteger TotalCredited;
        public BigInteger TotalSpent;
        /// <summary>
        /// Золото, зачисленное не доставкой, а наградой (удвоение офлайн-дохода за рекламу): Wallet = TotalCredited +
        /// BonusCredited − TotalSpent; TotalCredited по-прежнему равен стоимости доставленных порций.
        /// </summary>
        public BigInteger BonusCredited;
        public int[] Levels = new int[OutpostIds.BranchCount];
        /// <summary>По Id (порядок <see cref="OutpostMap.Targets"/>).</summary>
        public List<TargetState> Targets = new List<TargetState>();
        /// <summary>Число открытых ворот: открываются строго по Order.</summary>
        public int GatesOpened;
        /// <summary>По Id.</summary>
        public List<WorkerState> Workers = new List<WorkerState>();
        /// <summary>По Seq.</summary>
        public List<CargoBatch> Batches = new List<CargoBatch>();
        public DepotSlotState[] Slots = { new DepotSlotState(), new DepotSlotState() };
        /// <summary>FIFO по (ArrivalTick, WorkerId).</summary>
        public List<QueueEntry> Queue = new List<QueueEntry>();
        public long NextBatchSeq = 1;
        public long NextTaskId = 1;
        public int NextWorkerId = 1;
        public int NavVersion;
        /// <summary>Котики сверх StartWorkers, выданные карточками при создании шахты (не покупки «Команды»).</summary>
        public int ExtraWorkers;
        public RunCounters Counters = new RunCounters();
        public bool PlanningDirty;
        public long LastChangeTick;
        public bool WatchdogFired;
        public string WatchdogDiagnostic;
        /// <summary>Ключ выданной награды завершения (ReadyToContinue).</summary>
        public string CompletionRewardKey;
    }

    [Serializable]
    public sealed class RunCounters
    {
        public int DeliveredUnits;
        /// <summary>= TotalCredited.</summary>
        public BigInteger DeliveredValue;
        public int ReleasedUnits;
        /// <summary>Суммарная стоимость выпущенных порций.</summary>
        public BigInteger ReleasedValue;
        public int ClearedTargets;
        public long FirstDeliveryTick = -1;
        public long FirstPurchaseTick = -1;
        public long ClearedTick = -1;
        public long CompletedTick = -1;
        public int MaxQueue;
        public long QueueWaitTicks;
        /// <summary>Сколько раз работник вышел из очереди к месту (для средней длительности ожидания).</summary>
        public int QueueServed;
        /// <summary>Сундуков карточек открыто в этой шахте.</summary>
        public int ChestsOpened;
        /// <summary>Копий карточек получено в этой шахте.</summary>
        public int CardsGained;
        /// <summary>Эликсира получено в этой шахте.</summary>
        public long ElixirGained;
    }

    [Serializable]
    public sealed class TargetState
    {
        public int Id;
        public char Kind;
        public long MaxHpUnits;
        public long RemainingHpUnits;
        public int TotalUnits;
        public int ReleasedUnits;
        /// <summary>BaseCoinValueFactor типа (точная дробь, JSON "n/d").</summary>
        public Rational ValueFactor;
        public bool Destroyed;
    }

    [Serializable]
    public sealed class WorkerState
    {
        public int Id;
        /// <summary>Индекс окраса (декор, из Id).</summary>
        public int Look;
        public WorkerMode Mode;
        /// <summary>Идентичность текущей задачи; 0 — нет задачи (Idle).</summary>
        public long TaskId;
        /// <summary>Текущая логическая клетка; при движении — клетка начала текущего ребра.</summary>
        public int Cell;
        /// <summary>Клетки пути после стартовой; null — нет пути.</summary>
        public int[] Path;
        /// <summary>Индекс следующей клетки: текущее ребро Cell → Path[PathIndex].</summary>
        public int PathIndex;
        public TimedAction Action;
        /// <summary>Последнее начало/завершение корректного ребра; страховка отсутствующего таймера движения (§6.4).</summary>
        public long LastMoveProgressTick;
        /// <summary>Повтор пути выполнен один раз; сбрасывается при реальном продвижении или новой задаче.</summary>
        public bool MoveRetryAttempted;
        public int TargetId = -1;
        public int WorkCell = -1;
        /// <summary>Партия резерва подбора.</summary>
        public long CargoBatchSeq = -1;
        /// <summary>Зарезервированное место склада (подзадача Returning → место).</summary>
        public int DepotSlot = -1;
        /// <summary>
        /// Задача пути стала некорректной: цель уничтожена другими во время пути к ней либо вместимость упала ниже груза
        /// (MovingToWork/MovingToCargo), либо GoingHome разбужен свободным грузом — новая задача в ближайшей клетке.
        /// </summary>
        public bool TaskInvalid;
        /// <summary>Сумка по ключу (Source, Ordinal).</summary>
        public List<CargoUnit> Bag = new List<CargoUnit>();
    }

    /// <summary>
    /// Таймер действия. Для Edge/Unload — точный прогресс: p(t) = P0 + (t − SegmentStart)/Duration,
    /// Deadline = SegmentStart + ceil((1 − P0) · Duration). Save/load восстанавливает поля без нового округления.
    /// </summary>
    [Serializable]
    public sealed class TimedAction
    {
        public ActionKind Kind;
        public long SegmentStart;
        public Rational P0;
        public long Duration;
        public long Deadline;

        /// <summary>Точный прогресс на отметке t (не больше 1).</summary>
        public Rational ProgressAt(long t)
        {
            if (Duration <= 0) return Rational.One;
            var p = P0 + Rational.Of(Math.Max(0, t - SegmentStart), Duration);
            return p > Rational.One ? Rational.One : p;
        }

        /// <summary>Прогресс для отображения (float-эквивалент), без влияния на ядро.</summary>
        public double ProgressAtForDisplay(double t)
        {
            if (Duration <= 0) return 1;
            double p = P0.ToDouble() + (t - SegmentStart) / Duration;
            return p < 0 ? 0 : p > 1 ? 1 : p;
        }
    }

    [Serializable]
    public sealed class CargoUnit
    {
        /// <summary>Id цели-источника.</summary>
        public int Source;
        public int Ordinal;
        /// <summary>Фиксированная стоимость (на момент выпуска).</summary>
        public BigInteger Value;
        /// <summary>WorkerId резерва; 0 — свободна (только на земле).</summary>
        public int ReservedBy;
        public long ReservedTask;

        public int CompareKey(CargoUnit other) =>
            Source != other.Source ? Source.CompareTo(other.Source) : Ordinal.CompareTo(other.Ordinal);
    }

    [Serializable]
    public sealed class CargoBatch
    {
        public long Seq;
        /// <summary>"b{Seq}".</summary>
        public string Key;
        public int Cell;
        public long CreatedTick;
        /// <summary>По ключу порций.</summary>
        public List<CargoUnit> Units = new List<CargoUnit>();

        public int FreeCount()
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
                if (Units[i].ReservedBy == 0) n++;
            return n;
        }
    }

    [Serializable]
    public sealed class DepotSlotState
    {
        /// <summary>0 — свободно.</summary>
        public int WorkerId;
        public long TaskId;
    }

    [Serializable]
    public sealed class QueueEntry
    {
        public int WorkerId;
        public long ArrivalTick;
    }
}
