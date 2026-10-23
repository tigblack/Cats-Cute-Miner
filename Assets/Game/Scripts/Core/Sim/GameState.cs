// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;

// Сохраняемое состояние игры. Разделено на PlayerProgressState (переносится между зонами)
// и MineProgressState (создаётся заново при входе в зону) — ТЗ 11.3, 16.
namespace CatMiners.Core.Sim
{
    public sealed class GameState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public int RulesVersion = Formula.RulesVersion;
        public string BalanceSha256;
        public string Slot = SaveSlots.Main;
        /// <summary>Метаданные корня уровня игрока (ТЗ 12.1, 12.3): последняя обработанная метка UTC; при CompleteZone не сбрасывается.</summary>
        public long LastProcessedUtcMs;
        /// <summary>Метаданные корня уровня игрока: следующий ID операции (ТЗ 16), сквозной между зонами, не сбрасывается.</summary>
        public long NextOpId = 1;
        public PlayerProgressState Player = new PlayerProgressState();
        public MineProgressState Mine = new MineProgressState();
    }

    public static class SaveSlots
    {
        public const string Main = "Main";
        public const string Debug = "Debug";
    }

    public sealed class PlayerProgressState
    {
        public GameNumber Elixir;
        public GameNumber Hard;
        public Dictionary<string, CardState> Cards = new Dictionary<string, CardState>();
        public Dictionary<string, long> RewardCycle = new Dictionary<string, long>();
        public int Dynamite;
        /// <summary>Сундуки, результат которых уже определён и начислен; ждут показа игроку.</summary>
        public List<ChestResult> ChestReveals = new List<ChestResult>();
        public FreeChestState FreeChest = new FreeChestState();
        public DeliveryHistory DeliveryHistory = new DeliveryHistory();
        public RngStreams Rng;
        public int CurrentZone = 1;
        public int HighestCompletedZone;
        public bool CampaignCompleted;
        public List<string> TutorialDone = new List<string>();
        /// <summary>Настройки игрока (ТЗ 11.3, 13.1): переносятся между зонами, хранятся в сохранении слота.</summary>
        public PlayerSettingsState Settings = new PlayerSettingsState();
        /// <summary>Награды исключённых систем (например, валюта сезонного пропуска): только учёт.</summary>
        public Dictionary<string, double> ExcludedRewards = new Dictionary<string, double>();
        /// <summary>Монотонное время игрока (онлайн + учтённый офлайн), тики. Окна доставок считаются по нему.</summary>
        public long PlayerTimeTicks;
        /// <summary>Журнал начислений по источникам (ТЗ 4.4): "gold:forge", "elixir:rock" и т. п.</summary>
        public Dictionary<string, GameNumber> Ledger = new Dictionary<string, GameNumber>();
        public Dictionary<string, long> ZoneTimeTicks = new Dictionary<string, long>();
    }

    /// <summary>Группы громкости звука (настройки игрока).</summary>
    public enum AudioGroup
    {
        Master,
        Music,
        Sfx,
        Ambience,
    }

    /// <summary>
    /// Настройки (ТЗ 13.1): звук (общий выключатель и громкость групп Master, Music, SFX, Ambience — 0…1), вибрация,
    /// уменьшение эффектов. Экономику не меняют. Старые сохранения без полей загружаются со значениями по умолчанию.
    /// </summary>
    public sealed class PlayerSettingsState
    {
        public bool SoundOn = true;
        public bool VibrationOn = true;
        public float MasterVolume = 1f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 1f;
        public float AmbienceVolume = 0.7f;
        /// <summary>Меньше частиц и без импульса камеры на крупных событиях.</summary>
        public bool ReduceEffects;
    }

    public sealed class CardState
    {
        public int Level;       // 0 — карты нет
        public int Duplicates;  // копии сверх первой
    }

    public sealed class CardGrant
    {
        public string CardId;
        public int Count;
    }

    public sealed class ChestResult
    {
        public long OpId;
        public string GachaId;
        public string Source;
        public GameNumber Elixir;
        public List<CardGrant> Cards = new List<CardGrant>();
    }

    public sealed class FreeChestState
    {
        public bool Enabled;
        public int Stock;
        public long TimerTicks;
        public int NextIndex;
    }

    public sealed class DeliveryHistory
    {
        public List<long> ClaimTimes = new List<long>();
        public Dictionary<string, List<long>> DupeTimes = new Dictionary<string, List<long>>();
        public int SkippedAds;
    }

    public enum ObjectStatus
    {
        Intact = 0,          // камень цел; шахта Blocked; ворота/выход Closed; препятствие
        Destroyed = 1,       // камень разрушен
        Broken = 2,          // ворота / выход разрушены
        Opened = 3,          // ворота открыты
        ReadyToActivate = 4, // шахта расчищена
        Producing = 5,       // шахта или кузница производит
    }

    public sealed class ObjectState
    {
        public int Index;            // линейный индекс якоря (ключ экземпляра)
        public ObjectStatus Status;
        public GameNumber Hp;
        public GeneratorState Gen;
    }

    public sealed class GeneratorState
    {
        public int Level = 1;
        public long CycleTicks;
        public bool Automated;
        public bool PayoutReady;
        public GameNumber PendingPayout;
        public int ThresholdsReached;
    }

    public sealed class CatState
    {
        public int Id;
        public int Level;
        public int Cell;
        /// <summary>Линейный индекс якоря цели или −1 (Idle).</summary>
        public int TargetIndex = -1;
        public long PhaseTicks;
    }

    public enum ContainerKind
    {
        FreeCat = 0,
        Delivery = 1,
        /// <summary>Бочка найма (<see cref="GameSession.BarrelHires"/>): котик уровня найма CatLevel появляется при открытии.</summary>
        Hire = 2,
    }

    public sealed class ContainerState
    {
        public int Id;
        public ContainerKind Kind;
        public int Cell;
        public int CatLevel;
        public string DeliveryId;
        public int RewardType;
        public string DetailedType;
        public GameNumber Amount;

        public bool HoldsCat => Kind == ContainerKind.FreeCat || Kind == ContainerKind.Hire || RewardType == Data.RewardTypes.Miner;
    }

    /// <summary>
    /// Добыча на поле (<see cref="GameSession.LootPickups"/>): валютная награда разрушенного камня ждёт сбора тапом
    /// (<see cref="GameSession.CollectLoot"/>) или автосбора через <see cref="GameSession.LootAutoCollectSeconds"/>. Клетку не занимает.
    /// </summary>
    public sealed class LootState
    {
        public const string Gold = "gold";
        public const string Elixir = "elixir";
        public const string Hard = "hard";

        public int Id;
        /// <summary>Клетка якоря камня-источника.</summary>
        public int Cell;
        /// <summary><see cref="Gold"/>, <see cref="Elixir"/> или <see cref="Hard"/>.</summary>
        public string Currency;
        public GameNumber Amount;
        /// <summary>Возраст в тиках игрового времени; автосбор при <see cref="GameSession.LootAutoCollectTicks"/>.</summary>
        public long AgeTicks;
        /// <summary>Линейный индекс якоря камня-источника (ObjectState.Index).</summary>
        public int SourceIndex;
    }

    public sealed class MineProgressState
    {
        public int Zone = 1;
        public GameNumber Gold;
        public List<CatState> Cats = new List<CatState>();
        public int NextCatId = 1;
        public List<ObjectState> Objects = new List<ObjectState>();
        public int GatesOpened;
        public int HireCount;
        public int ActivatedShafts;

        public long FreeCatTimerTicks;
        public bool FreeCatReady;

        public bool DeliveriesEnabled;
        public long DeliveryTimerTicks;
        public bool DeliveryReady;

        public List<ContainerState> Containers = new List<ContainerState>();
        public int NextContainerId = 1;

        public long ZoneTimeTicks;
        public int RocksDestroyed;
        public int CatsHired;
        public int Merges;

        /// <summary>
        /// Позиция камеры (ТЗ 12.1, 13.2): мировая Z точки фокуса, которую восстанавливает загрузка.
        /// Отрицательное значение — не задана (новая зона: камера на стартовой площадке).
        /// </summary>
        public float CameraFocusZ = -1f;

        /// <summary>
        /// Проверка автоматизации отложена (ТЗ 9.4, 12.3): карта пришла из сундука офлайн. Ручная шахта до конца офлайн-интервала
        /// остаётся ручной (не больше одного буфера); проверка выполняется первым онлайн-шагом.
        /// </summary>
        public bool AutomationCheckPending;

        /// <summary>Добыча камней на поле (<see cref="GameSession.LootPickups"/>); старые сохранения без поля — пустой список.</summary>
        public List<LootState> Loot = new List<LootState>();
        public int NextLootId = 1;
        /// <summary>Собрано добычи в зоне (тапом и автосбором).</summary>
        public int LootCollected;
    }
}
