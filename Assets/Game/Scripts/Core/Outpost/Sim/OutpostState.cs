// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;
using System.Numerics;

// Сериализуемое состояние режима CatOutpostV2 (ТЗ 17): постоянный профиль игрока и состояние текущей шахты.
// Публичные поля, Newtonsoft с OutpostJson.Settings. Производные кэши (занятость клеток, BFS, Stats) не сохраняются.
namespace CatMiners.Core.Outpost
{
    public sealed class OutpostGameState
    {
        public OutpostProfile Profile = new OutpostProfile();
        public OutpostRun Run;
    }

    /// <summary>Постоянный профиль: переносится между шахтами.</summary>
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
        /// <summary>Прогресс удалённого обучения (до 15 октября 2026): поле только для совместимости сохранений, игра его не читает.</summary>
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
        /// <summary>Ключи однократных выдач карточек и встреченных типов заводов (CardService.FactorySeenKey).</summary>
        public List<string> CardKeys = new List<string>();

        // ── Магазин (ТЗ 10.5, Resources/Outpost/store_catalog.json) ──
        /// <summary>
        /// Версия данных магазина: 0 — профиль создан до магазина (ждёт однократной выдачи стартового набора
        /// <see cref="StoreService.MigrateProfile"/>), 1 — прежний набор выдан, 2 — бесплатные стартовые тайм-скипы убраны.
        /// </summary>
        public PurchaseIntent PendingPurchase;
        public List<PaymentDelivery> Payments = new List<PaymentDelivery>();
        public RewardedOffer PendingRewardedOffer;
        public List<string> DeliveredRewardedOffers = new List<string>();
        /// <summary>
        /// Remaining simulated seconds of the rewarded ×2 damage boost. Exact fraction, independent of a mine's tick rate
        /// and clock origin; includes elapsed offline/time-skip simulation. Missing in old saves means inactive.
        /// </summary>
        public Rational DamageBonusSecondsLeft;
        public int StoreVersion;
        /// <summary>Гемы — hard-валюта магазина (покупки сундуков, тайм-скипов и эликсира).</summary>
        public long Gems;
        /// <summary>Расходники магазина: ID плитки тайм-скипа → количество в наличии; JSON — по возрастанию ключа.</summary>
        public Dictionary<string, int> Consumables = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Рекламные плитки: ID → UTC (мс), с которого плитка снова доступна.</summary>
        public Dictionary<string, long> StoreCooldownUntilUtcMs = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>Пакеты, бонус первой покупки которых уже получен.</summary>
        public List<string> StoreBonusUsed = new List<string>();

        /// <summary>
        /// Первая сессия (Docs/Rework/FIRST_SESSION.md): открытые функции, показанные сцены спасения, квитанции однократных выдач и
        /// спасённые персонажи. Version 0 — профиль до прогрессии (ждёт <see cref="ProgressionService.MigrateProfile"/>).
        /// </summary>
        public ProgressionState Progression = new ProgressionState();

        public int ItemLevel(string itemId) => itemId != null && Items != null && Items.TryGetValue(itemId, out int q) ? q : 0;

        /// <summary>Уровень карточки (0 — не получена).</summary>
        public int CardLevel(string cardId) => cardId != null && Cards != null && Cards.TryGetValue(cardId, out var c) && c != null ? c.Level : 0;

        /// <summary>Сколько расходника магазина в наличии (0 — нет).</summary>
        public int ConsumableCount(string itemId) => itemId != null && Consumables != null && Consumables.TryGetValue(itemId, out int n) ? n : 0;
    }

    /// <summary>Прогресс карточки: первая копия даёт уровень 1, следующие — копии для улучшения.</summary>
    public sealed class CardProgress
    {
        public int Level;
        /// <summary>Копии сверх первой, ещё не потраченные на улучшение.</summary>
        public long Copies;
    }

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

    /// <summary>
    /// Прогресс первой сессии профиля (Docs/Rework/FIRST_SESSION.md). Все списки — множества строк в порядке добавления, только
    /// растут: доступность (<see cref="Unlocked"/>) отдельно от показа сцен спасения (<see cref="Shown"/>) и выдачи
    /// (<see cref="Grants"/>); локальные покупки шахты здесь не хранятся.
    /// </summary>
    public sealed class ProgressionState
    {
        /// <summary>0 — профиль создан до прогрессии (миграция открывает уже пройденное), 1 — progression-1.</summary>
        public int Version;
        /// <summary>Открытые функции (FeatureDef.Id): постоянная отметка аккаунта, не сбрасывается новой шахтой.</summary>
        public List<string> Unlocked = new List<string>();
        /// <summary>Показанные сцены спасения и просмотренные строки «Базы» ("intro.rescue", "reveal.mia", "seen.branch.bags" …).</summary>
        public List<string> Shown = new List<string>();
        /// <summary>
        /// Отметки прежнего обучения первой сессии ("cards.upgrade", "equipment.equip"; обучение убрано 21.10.2026). Больше не
        /// пишутся и не читаются; поле хранится, чтобы прежние сохранения читались и записывались без потерь.
        /// </summary>
        public List<string> Done = new List<string>();
        /// <summary>Квитанции однократных выдач ("grant.cards", "grant.equipment"): повтор входа в шахту ничего не выдаёт.</summary>
        public List<string> Grants = new List<string>();
        /// <summary>Спасённые персонажи ("mia"): спасённая Mia встречает игрока в магазине (портрет сверху Store).</summary>
        public List<string> Crew = new List<string>();

        public bool IsUnlocked(string feature) => feature != null && Unlocked != null && Unlocked.Contains(feature);
        public bool WasShown(string key) => key != null && Shown != null && Shown.Contains(key);
        public bool HasGrant(string key) => key != null && Grants != null && Grants.Contains(key);
        public bool HasCrew(string character) => character != null && Crew != null && Crew.Contains(character);
    }

    /// <summary>Формат прежнего обучения (удалено 15 октября 2026); хранится в сохранениях без изменений.</summary>
    public sealed class TutorialState
    {
        public int Step;
        public bool Dismissed;
        public List<string> Seen = new List<string>();
    }

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
        /// <summary>
        /// Снимок обстановки шахты (постоянные препятствия, детали стен, мусор — Docs/Rework/MINE_DRESSING.md), с которым создана
        /// карта; null — шахта без обстановки (создана до неё): она доигрывается на прежней карте, следующая получает обстановку.
        /// </summary>
        public MineDressing MapDressing;
        /// <summary>Null on established pre-excavation runs: they finish unchanged. New mines snapshot the final chamber.</summary>
        public FinalExcavation MapFinal;
        /// <summary>Разрешённый снимок профиля чисел (целиком).</summary>
        public OutpostBalanceConfig Balance;
        /// <summary>
        /// Удорожание улучшений шахты от обстановки (Docs/Rework/MINE_DRESSING.md): стоимость груза всех целей карты к стоимости
        /// груза целей источника, вверх до сотых; цены всех веток умножаются на него (<see cref="TeamUpgradeService.NextCost"/>).
        /// Больше руды — дороже улучшения, ритм покупок прежний. 1 — шахта без обстановки и run, созданный до множителя.
        /// </summary>
        public Rational PriceFactor = Rational.One;
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

        /// <summary>Уровень ветки; снимок короче (run до мест разгрузки, ещё не дополненный) — 0.</summary>
        public int Level(UpgradeBranch branch) => Levels != null && (int)branch < Levels.Length ? Levels[(int)branch] : 0;

        /// <summary>По Id (порядок <see cref="OutpostMap.Targets"/>).</summary>
        public List<TargetState> Targets = new List<TargetState>();
        /// <summary>Число открытых ворот: открываются строго по Order.</summary>
        public int GatesOpened;
        /// <summary>По Id.</summary>
        public List<WorkerState> Workers = new List<WorkerState>();
        /// <summary>По Seq.</summary>
        public List<CargoBatch> Batches = new List<CargoBatch>();
        /// <summary>
        /// Места разгрузки у склада: <see cref="OutpostIds.DepotSlotCount"/> с начала шахты и по одному за уровень ветки
        /// <see cref="UpgradeBranch.Bays"/> (клетки — первые элементы <see cref="OutpostMap.DepotCells"/>).
        /// </summary>
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

        // ── Заводы, трубы, склад товаров (ТЗ 2.2 §4, §5, §6, §10.2; создаёт и сбрасывает WP1 в CreateRun/PrepareTransition) ──
        /// <summary>
        /// Снимок factory_rules.json на момент создания шахты (как <see cref="Balance"/>): цикл, выпуск, цена и трубы идущей шахты
        /// берутся отсюда, правка конфигурации на неё не влияет. null — run создан без заводов (до ТЗ 2.2).
        /// </summary>
        public FactoryRules FactoryRules;
        /// <summary>Заводы шахты (цели <c>s</c>) по возрастанию <see cref="FactoryState.TargetId"/>; пусто — в шахте заводов нет.</summary>
        public List<FactoryState> Factories = new List<FactoryState>();
        /// <summary>Партии в трубах по <see cref="PipeBatch.Seq"/>; партия удаляется в момент прибытия на склад.</summary>
        public List<PipeBatch> PipeBatches = new List<PipeBatch>();
        /// <summary>Номер следующей партии трубы (сквозной на шахту, с 1).</summary>
        public long NextPipeBatchSeq = 1;
        /// <summary>Склад товаров: GoodId → количество на складе (целое, без лимита). Нулевые записи допустимы.</summary>
        public Dictionary<string, long> Warehouse = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>GoodId → сколько единиц пришло на склад по трубам за шахту.</summary>
        public Dictionary<string, long> GoodsArrived = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>GoodId → сколько единиц продано за шахту.</summary>
        public Dictionary<string, long> GoodsSold = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>
        /// Золото от продажи товара за шахту (§6.4): Wallet = TotalCredited + BonusCredited + GoodsSoldGold − TotalSpent.
        /// В <see cref="TotalCredited"/> не входит (там только монеты).
        /// </summary>
        public BigInteger GoodsSoldGold;
        /// <summary>GoodId → отладочная выдача на склад (DevTools «Goods ×100», только debug-профиль); вне debug пусто.</summary>
        public Dictionary<string, long> DebugGoods = new Dictionary<string, long>(StringComparer.Ordinal);

        // ── Бонус за рекламу: бочка динамита из мортиры (ТЗ 10.6) ──
        /// <summary>Бочка динамита на поле, ждёт тапа (команда Detonate); null — бочки нет. Новая шахта начинается без неё.</summary>
        public DynamiteBarrel Dynamite;
        /// <summary>Номер следующей бочки в шахте (с 1).</summary>
        public long NextDynamiteSeq = 1;
    }

    /// <summary>
    /// Бочка динамита (ТЗ 10.6): мортира лагеря выстрелила её к цели <see cref="TargetId"/>, она лежит в <see cref="Cell"/> до тапа.
    /// Взрыв — урон целям, доступным кирке, в радиусе вокруг клетки; офлайн и тайм-скип бочку не трогают.
    /// </summary>
    public sealed class DynamiteBarrel
    {
        /// <summary>Номер бочки в шахте — ключ вида.</summary>
        public long Seq;
        /// <summary>Клетка бочки, центр взрыва.</summary>
        public int Cell;
        /// <summary>Цель, к которой стреляла мортира: она получает долю центра; −1 — доступных целей не было.</summary>
        public int TargetId = -1;
        public long LaunchedTick;
    }

    /// <summary>Состояние завода (ТЗ 2.2 §4.3).</summary>
    public enum FactoryStatus
    {
        /// <summary>Цепи целы: цель для котиков и ударов, HP &gt; 0.</summary>
        Locked,
        /// <summary>Идёт цикл [CycleStart, CycleEnd).</summary>
        Working,
        /// <summary>
        /// Цикл окончен, партия ReadyUnits ждёт команды DeliverFactory (только ручной режим). Цикла нет: CycleStart = CycleEnd = −1;
        /// отметка готовности — Tick события FactoryReady.
        /// </summary>
        Ready,
        /// <summary>Шахта завершена (ReadyToContinue): циклов и отправок нет.</summary>
        Stopped,
        /// <summary>Цепи разбиты; ждёт ActivateFactory и сундук. Нет циклов и автодоставки, в том числе после завершения шахты.</summary>
        PendingActivation,
    }

    /// <summary>
    /// Завод шахты (ТЗ 2.2 §10.2). Инвариант: Produced = ReadyUnits + Dispatched. Тип, товар и требование автодоставки —
    /// в карте (<see cref="OutpostMap.Factory"/>); TypeId и GoodId скопированы сюда для учёта без карты.
    /// </summary>
    public sealed class FactoryState
    {
        /// <summary>ID цели <c>s</c> (индекс якоря) — она же ID завода.</summary>
        public int TargetId;
        /// <summary>Тип JSON ("amethyst").</summary>
        public string TypeId;
        /// <summary>Товар ("milk").</summary>
        public string GoodId;
        public FactoryStatus Status;
        /// <summary>Автодоставка включена (уровень карточки требования ≥ cardLevel токена). Включившись, в этой шахте не выключается.</summary>
        public bool Automated;
        /// <summary>
        /// Тик начала идущего цикла. Цикл есть только в Working: [CycleStart, CycleEnd), CycleEnd = CycleStart + CycleTicks; в Locked,
        /// Ready, Stopped и PendingActivation оба поля −1 (ТЗ 2.2 §4.3; CORE_SPEC §16.4).
        /// </summary>
        public long CycleStart = -1;
        /// <summary>Тик конца идущего цикла (CycleStart + CycleTicks, только Working); −1 — цикла нет.</summary>
        public long CycleEnd = -1;
        /// <summary>Готовая партия в состоянии Ready (единиц, зафиксировано в конце цикла); 0 — партии нет.</summary>
        public int ReadyUnits;
        /// <summary>Единиц товара произведено за шахту (накопительно).</summary>
        public long Produced;
        /// <summary>Единиц товара отправлено в трубу за шахту (накопительно).</summary>
        public long Dispatched;
        /// <summary>Завершённых циклов за шахту (отчёты баланса §9).</summary>
        public int Cycles;
    }

    /// <summary>Партия товара в трубе (ТЗ 2.2 §5, §10.2).</summary>
    public sealed class PipeBatch
    {
        /// <summary>Номер партии (Run.NextPipeBatchSeq).</summary>
        public long Seq;
        /// <summary>Завод-отправитель (ID цели).</summary>
        public int FactoryId;
        public string GoodId;
        public int Units;
        /// <summary>Тик отправки в трубу.</summary>
        public long DepartTick;
        /// <summary>Тик прибытия на склад: DepartTick + TransitTicks маршрута завода.</summary>
        public long ArriveTick;
    }

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
        /// <summary>Выполненных команд DeliverFactory (ручных доставок) в этой шахте (отчёт §9).</summary>
        public int ManualDeliveries;
    }

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
        /// <summary>Persisted measured strike interval, independent from UTC/offline bookkeeping.</summary>
        public long FirstStrikeTick = -1;
        public long DestroyedTick = -1;
        public int DamageStage;

        public int ExpectedReleasedUnits() => RemainingHpUnits == 0 ? TotalUnits :
            MaxHpUnits > 0 ? (int)((BigInteger)TotalUnits * (MaxHpUnits - RemainingHpUnits) / MaxHpUnits) : 0;
        public void UpdateDamageStage()
        {
            if (MaxHpUnits <= 0) return;
            var percent = (BigInteger)RemainingHpUnits * 100 / MaxHpUnits;
            DamageStage = RemainingHpUnits == 0 ? 5 : percent <= 10 ? 4 : percent <= 25 ? 3 : percent <= 50 ? 2 : percent <= 75 ? 1 : 0;
        }
    }

    public sealed class WorkerState
    {
        public int Id;
        /// <summary>Индекс окраса (декор, из Id).</summary>
        public int Look;
        /// <summary>
        /// Спасённый персонаж в облике этого котика ("mia") или null — обычный котик. Только вид и имя. Новые шахты его больше не
        /// назначают (с 21.10.2026 Mia — в магазине), загрузка снимает его с котиков начатой шахты
        /// (<see cref="ProgressionService.PrepareLoaded"/>); поле хранится ради прежних сохранений.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string Crew;
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

    public sealed class DepotSlotState
    {
        /// <summary>0 — свободно.</summary>
        public int WorkerId;
        public long TaskId;
    }

    public sealed class QueueEntry
    {
        public int WorkerId;
        public long ArrivalTick;
    }
}
