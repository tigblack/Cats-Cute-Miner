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
        /// Версия профиля чисел по умолчанию: b6 — числа b5 (= b4) и множитель темпа шахт K(z) (PaceScale, решение владельца В4
        /// 15 октября 2026; Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»).
        /// </summary>
        public const string BalanceVersion = "outpost-2.2-b6";
        /// <summary>Версия адаптера уровня: adapter-2 — у цепей заводов (<c>s</c>) нет груза (ТЗ 2.2 §4.2, OutpostLevelBalanceAdapter — WP2).</summary>
        public const string AdapterVersion = "adapter-2";
        public const int RewardRngVersion = 1;

        /// <summary>Логические тики в секунде (целые микросекунды, ТЗ 6.5).</summary>
        public const long TicksPerSecond = 1_000_000L;
        /// <summary>Общий потолок одного отсутствия: три реальных часа, включая прежние сохранения.</summary>
        public const long MaxOfflineMs = 3L * 60 * 60 * 1000;
        /// <summary>Долей HP в одной единице HP (ТЗ 7.1).</summary>
        public const long HpUnitsPerHp = 1_000_000L;

        /// <summary>Источник сундука из магазина в событии ChestOpened (TargetId): не цель шахты, не выход и не обучение.</summary>
        public const int StoreSource = -2;
        public const int FirstZone = 1;
        public const int LastZone = 144;
        public const int BranchCount = 6;
        public const int SlotCount = 4;
        public const int DepotSlotCount = 2;

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

    /// <summary>Ветки локальных улучшений; значение — индекс в массиве уровней.</summary>
    public enum UpgradeBranch
    {
        Team = 0,
        Pickaxes = 1,
        Boots = 2,
        Bags = 3,
        Unload = 4,
        Value = 5,
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
