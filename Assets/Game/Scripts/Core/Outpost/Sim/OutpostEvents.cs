using System.Numerics;

namespace CatMiners.Core.Outpost
{
    public enum OutpostEventType
    {
        WorkerModeChanged,
        TargetHit,
        CargoReleased,
        CargoPickedUp,
        CargoDeposited,
        TargetDestroyed,
        SectorOpened,
        DrainingStarted,
        StageCompleted,
        TeamUpgradePurchased,
        WorkerHired,
        EquipmentChanged,
        ItemUpgraded,
        WatchdogFired,
        /// <summary>Удар кирки игрока (команда Strike): TargetId, Value — урон в долях HP, Cell — якорь цели.</summary>
        TargetStruck,
        /// <summary>Открыт сундук карточек: ItemId — запись Gacha, TargetId — источник (−1 — выход или учебная выдача, OutpostIds.StoreSource — магазин), Count — копий, Value — эликсир, Detail — "ca002:2,…", Extra — новых карточек.</summary>
        ChestOpened,
        /// <summary>Эликсир из камня (Soft в цикле наград): TargetId, Value.</summary>
        ElixirFound,
        /// <summary>Карточка улучшена: ItemId — ID карточки, Count — новый уровень, Value — списанный эликсир.</summary>
        CardUpgraded,
        /// <summary>Куплена плитка магазина: ItemId — ID плитки, Value — выданный эликсир или гемы (0 у сундука).</summary>
        StorePurchased,
        /// <summary>Золото награды за рекламу: Value — сумма.</summary>
        BonusGold,

        // ── Заводы и рынок (ТЗ 2.2; порядок внутри отметки — §4.5, шаг 4б и фаза 6) ──
        /// <summary>
        /// Цепи разбиты, завод ждёт ActivateFactory (без сундука и цикла): TargetId — завод, Cell — якорь
        /// цели, ItemId — GoodId, Detail — TypeId.
        /// </summary>
        FactoryUnlocked,
        /// <summary>
        /// Цикл ручного завода окончен, партия ждёт «Deliver» (Status → Ready; у автоматического вместо него GoodsDispatched):
        /// TargetId, ItemId — GoodId, Count — единиц в партии.
        /// </summary>
        FactoryReady,
        /// <summary>
        /// Включилась автодоставка (уровень карточки требования ≥ cardLevel токена; при открытии, получении копии, улучшении,
        /// загрузке): TargetId, ItemId — GoodId, Detail — ID карточки требования, Count — её уровень сейчас.
        /// </summary>
        FactoryAutomated,
        /// <summary>
        /// Партия ушла в трубу: TargetId — завод, ItemId — GoodId, Count — единиц, BatchSeq — номер партии
        /// (<see cref="PipeBatch.Seq"/>, ArriveTick — в Run.PipeBatches), Extra — 1 автодоставка, 0 команда DeliverFactory.
        /// </summary>
        GoodsDispatched,
        /// <summary>Партия пришла на склад: TargetId — завод-отправитель, ItemId — GoodId, Count — единиц, BatchSeq — номер партии.</summary>
        GoodsArrived,
        /// <summary>
        /// Товар продан (команда SellGoods): ItemId — GoodId, Count — продано единиц, Value — золото. SellGoods(null) даёт по
        /// одному событию на каждый товар с ненулевым остатком, по номеру типа; сумма Value одной команды — «+X gold».
        /// </summary>
        GoodsSold,

        // ── Бонус за рекламу и мортира (ТЗ 10.6) ──
        /// <summary>Награда бонуса за рекламу начислена: ItemId — вид (gold, elixir, gems), Value — сумма.</summary>
        BonusGranted,
        /// <summary>Мортира выстрелила бочку динамита: Cell — клетка бочки, TargetId — цель (−1 — нет), BatchSeq — номер бочки.</summary>
        DynamiteLaunched,
        /// <summary>
        /// Бочка взорвана (команда Detonate), до событий TargetStruck задетых целей: Cell — клетка, BatchSeq — номер бочки,
        /// Count — задето целей, Value — суммарный урон в долях HP.
        /// </summary>
        DynamiteExploded,

        // ── Первая сессия (Docs/Rework/FIRST_SESSION.md) ──
        /// <summary>Открыта функция первой сессии (постоянная отметка профиля): ItemId — FeatureDef.Id ("branch.team", "cards" …).</summary>
        FeatureUnlocked,
        /// <summary>
        /// Замок клетки разрушен — персонаж спасён и теперь в команде: ItemId — персонаж ("mia"), TargetId — замок, Cell — клетка
        /// персонажа (постоянное препятствие), Count — 1, если он добавлен в команду сейчас (0 — уже был).
        /// </summary>
        CharacterRescued,
    }

    /// <summary>
    /// Событие для представления (ТЗ 18): не сохраняется и не меняет состояние. Начислением управляет только фаза 4 ядра.
    /// Неиспользуемые поля: −1 для индексов/ID, 0 для счётчиков.
    /// </summary>
    public struct OutpostEvent
    {
        public OutpostEventType Type;
        public long Tick;
        public int WorkerId;
        public int TargetId;
        public int Cell;
        public int Count;
        /// <summary>GoodsSold — точное количество проданных единиц; Count ограничен int для существующего UI.</summary>
        public long Quantity;
        public long BatchSeq;
        public int Slot;
        public BigInteger Value;
        public int Branch;
        public string ItemId;
        /// <summary>Для WorkerModeChanged — новый режим.</summary>
        public WorkerMode Mode;
        /// <summary>TargetHit — критических ударов; ChestOpened — новых карточек.</summary>
        public int Extra;
        /// <summary>ChestOpened — состав "ca002:2,ca014:1".</summary>
        public string Detail;

        public static OutpostEvent Create(OutpostEventType type, long tick) => new OutpostEvent
        {
            Type = type,
            Tick = tick,
            WorkerId = -1,
            TargetId = -1,
            Cell = -1,
            BatchSeq = -1,
            Slot = -1,
            Branch = -1,
        };

        public override string ToString() =>
            Type + "@" + Tick + " w" + WorkerId + " t" + TargetId + " c" + Cell + " n" + Count + " b" + BatchSeq + " s" + Slot + " v" + Value +
            (ItemId != null ? " " + ItemId : "");
    }
}
