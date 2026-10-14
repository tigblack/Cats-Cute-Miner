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
