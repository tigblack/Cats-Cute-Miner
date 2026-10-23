using System.Globalization;

namespace CatMiners.Core.Outpost
{
    /// <summary>Идентификаторы и версии режима CatOutpostV2 (ТЗ 17).</summary>
    public static class OutpostIds
    {
        public const string ModeId = "CatOutpostV2";
        /// <summary>
        /// Версия формата снимка. 3 — заводы ТЗ 2.2 (§10.1); сохранения схемы &lt; 3 не переносятся, а архивируются со сбросом
        /// кампании (§10.3, WP6). 2 — журнал платежей.
        /// </summary>
        public const int SchemaVersion = 3;
        /// <summary>Версия поведения ядра (ТЗ 2.2: заводы, трубы, склад, рынок).</summary>
        public const string RulesVersion = "outpost-2.2-r1";
        /// <summary>
        /// Версия профиля чисел: b7 — прежний общий K(z) и отдельный шестичасовой средний участок шахты 7; b8 — первая сессия
        /// (Docs/Rework/FIRST_SESSION.md): ветка «Места разгрузки» и пороги «Обуви», «Сумок» и «Разгрузки» на уровнях 5 и 10.
        /// </summary>
        public const string BalanceVersion = "outpost-2.2-b8";
        /// <summary>Версия адаптера уровня: adapter-2 — у цепей заводов (<c>s</c>) нет груза (ТЗ 2.2 §4.2, OutpostLevelBalanceAdapter — WP2).</summary>
        public const string AdapterVersion = "adapter-2";
        public const int RewardRngVersion = 1;

        /// <summary>Логические тики в секунде (целые микросекунды, ТЗ 6.5).</summary>
        public const long TicksPerSecond = 1_000_000L;
        /// <summary>Стандартный потолок отсутствия: три часа. Текущая политика TargetPaceBands разрешает 6 часов в шахте 7.</summary>
        public const long MaxOfflineMs = 3L * 60 * 60 * 1000;
        /// <summary>Долей HP в одной единице HP (ТЗ 7.1).</summary>
        public const long HpUnitsPerHp = 1_000_000L;

        /// <summary>Источник сундука из магазина в событии ChestOpened (TargetId): не цель шахты, не выход и не обучение.</summary>
        public const int StoreSource = -2;
        public const int FirstZone = 1;
        public const int LastZone = 144;
        /// <summary>
        /// Ветки локальных улучшений: шесть основных и «Места разгрузки» (<see cref="UpgradeBranch.Bays"/>, первая сессия). Снимки
        /// run, созданные до мест разгрузки, хранят шесть веток и шесть уровней: уровни дополняет <see cref="OutpostStateUpgrade"/>,
        /// а ветки нет — в такой шахте места разгрузки не покупаются (<see cref="OutpostBalanceConfig.HasBranch"/>).
        /// </summary>
        public const int BranchCount = 7;
        /// <summary>Ветки до мест разгрузки (снимки run профилей b1–b7).</summary>
        public const int LegacyBranchCount = 6;
        public const int SlotCount = 4;
        /// <summary>Места разгрузки у склада в начале каждой шахты (без покупок ветки <see cref="UpgradeBranch.Bays"/>).</summary>
        public const int DepotSlotCount = 2;
        /// <summary>Наибольшее число мест разгрузки: столько клеток у склада размечает карта (<see cref="OutpostMap.DepotCells"/>).</summary>
        public const int MaxDepotSlots = 4;

        /// <summary>Ключ однократной награды первого завершения: "CatOutpostV2:zoneN:first".</summary>
        public static string FirstCompletionKey(int zone) =>
            ModeId + ":zone" + zone.ToString(CultureInfo.InvariantCulture) + ":first";

        public static string ZoneId(int zone) => "zone" + zone.ToString(CultureInfo.InvariantCulture);

        public static string RunId(long seq) => "run-" + seq.ToString(CultureInfo.InvariantCulture);

        public static string BatchKey(long seq) => "b" + seq.ToString(CultureInfo.InvariantCulture);

        /// <summary>Строковый ключ порции для логов: "{RunId}:{Source}:{Ordinal}".</summary>
        public static string UnitKey(string runId, int source, int ordinal) =>
            runId + ":" + source.ToString(CultureInfo.InvariantCulture) + ":" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Ветки локальных улучшений; значение — индекс в массиве уровней. Названия для игрока (первая сессия): Team — Hire Cat,
    /// Pickaxes — Mining Power, Boots — Movement Speed, Bags — Carry Capacity, Unload — Unloading Speed, Value — Coin Value,
    /// Bays — Loading Bays (ещё одно место разгрузки у склада).
    /// </summary>
    public enum UpgradeBranch
    {
        Team = 0,
        Pickaxes = 1,
        Boots = 2,
        Bags = 3,
        Unload = 4,
        Value = 5,
        Bays = 6,
    }

    public enum BuyMode
    {
        One,
        Ten,
        Max,
    }

    public enum EquipSlot
    {
        Pickaxe = 0,
        Hat = 1,
        Backpack = 2,
        Boots = 3,
    }

    public enum ItemRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    public enum RunPhase
    {
        Running,
        DrainingCargo,
        ReadyToContinue,
    }

    public enum WorkerMode
    {
        ChoosingTask,
        MovingToWork,
        Mining,
        MovingToCargo,
        PickingUp,
        Returning,
        WaitingDepot,
        Unloading,
        Idle,
        GoingHome,
    }

    public enum ActionKind
    {
        Edge,
        Hit,
        Pickup,
        Unload,
    }
}
