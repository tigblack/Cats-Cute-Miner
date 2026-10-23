using System.Numerics;
using Vector3 = UnityEngine.Vector3;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Точки звука и отклика, которые показал вид поля (RUNTIME_SPEC §4.4, §7). Вид не играет звук сам: контроллер подписывается
    /// на <see cref="OutpostBoardView.Cue"/> и решает, что играть (OutpostAudio ограничивает голоса и частоту).
    /// Подсказки приходят уже после бюджета эффектов кадра: отброшенный бюджетом эффект подсказки не даёт.
    /// </summary>
    public enum OutpostViewCue
    {
        /// <summary>Шаг котика (кадр касания лапы при ходьбе/переноске). Очень частый: ≤1 голос, ≥0,25 с.</summary>
        Footstep,
        /// <summary>Удар по цели (TargetHit): материал — <see cref="OutpostViewCueArgs.Material"/>.</summary>
        Hit,
        /// <summary>Порция вылетела из цели (CargoReleased): в сумку или на землю.</summary>
        SackPop,
        /// <summary>Котик подобрал порцию с земли (CargoPickedUp).</summary>
        Pickup,
        /// <summary>Сумка котика заполнилась (Bag.Count достиг вместимости).</summary>
        BagFull,
        /// <summary>Порция сдана на склад (CargoDeposited): Value — её фиксированная стоимость, Slot — место склада.</summary>
        Deposit,
        /// <summary>Котик сдал последнюю порцию сумки (разгрузка завершена).</summary>
        UnloadDone,
        /// <summary>Цель разрушена (TargetDestroyed).</summary>
        TargetBroken,
        /// <summary>Ворота открылись, сектор открыт (SectorOpened).</summary>
        SectorOpened,
        /// <summary>Новый котик вышел из лагеря (WorkerHired).</summary>
        WorkerSpawned,
        /// <summary>Куплено локальное улучшение (TeamUpgradePurchased): Branch — ветка.</summary>
        Upgrade,
        /// <summary>Сменился надетый комплект или улучшен предмет (EquipmentChanged / ItemUpgraded).</summary>
        Equip,
        /// <summary>Шахта завершена, команда собирается в лагере (StageCompleted).</summary>
        TeamGathered,
        /// <summary>Кирка игрока ударила цель (кадр удара успешной команды Strike): материал — <see cref="OutpostViewCueArgs.Material"/>.</summary>
        PlayerStrike,
        /// <summary>Цепи завода разбиты (FactoryUnlocked, ТЗ 2.2 §4.2): разлёт звеньев.</summary>
        ChainBreak,
        /// <summary>Партия ждёт доставки (FactoryReady, только ручной режим): тихий сигнал не чаще раза в 0,5 с.</summary>
        FactoryReady,
        /// <summary>Завод перешёл на автодоставку (FactoryAutomated).</summary>
        FactoryAutomated,
        /// <summary>Партия ушла в трубу (GoodsDispatched): <see cref="OutpostViewCueArgs.Automatic"/> — автодоставка, Count — автоматических заводов в кадре.</summary>
        PipeSend,
        /// <summary>Партия пришла на склад (GoodsArrived): Count — единиц товара.</summary>
        GoodsArrive,
        /// <summary>Товар продан на рынке (GoodsSold): одна команда «Sell all» — один звук (интервал Sfx).</summary>
        GoodsSold,
        /// <summary>Мортира лагеря выстрелила бочку динамита (DynamiteLaunched, ТЗ 10.6).</summary>
        MortarFire,
        /// <summary>Бочка динамита упала на поле.</summary>
        BarrelLand,
        /// <summary>Взрыв бочки динамита (DynamiteExploded).</summary>
        DynamiteBlast,
        /// <summary>Первая сессия: замок клетки разрушен, Mia спасена (CharacterRescued) — короткая мелодия и мяуканье.</summary>
        Rescue,
        /// <summary>Открыт раздел или ветка первой сессии (с кнопки снят замок): короткий звук открытия.</summary>
        FeatureUnlocked,
    }

    /// <summary>Аргументы подсказки вида: структура, передаётся без выделений памяти.</summary>
    public struct OutpostViewCueArgs
    {
        public OutpostViewCue Cue;
        /// <summary>Мировая точка (для панорамы/громкости по расстоянию до камеры).</summary>
        public Vector3 Position;
        /// <summary>WorkerId или −1.</summary>
        public int WorkerId;
        /// <summary>Id цели или −1.</summary>
        public int TargetId;
        /// <summary>Место склада (0, 1) или −1.</summary>
        public int Slot;
        /// <summary>Ветка улучшения (UpgradeBranch) или −1.</summary>
        public int Branch;
        /// <summary>Число ударивших (Hit), порций (SackPop) и т. п.; иначе 0.</summary>
        public int Count;
        /// <summary>Материал цели для звука удара/разлома.</summary>
        public HitMaterial Material;
        /// <summary>Стоимость порции (Deposit, SackPop), золото продажи (GoodsSold); иначе 0.</summary>
        public BigInteger Value;
        /// <summary>Удар пришёлся по цепям закрытого завода (Hit, PlayerStrike): звук цепей вместо материала.</summary>
        public bool Chains;
        public bool FinalExcavation;
        /// <summary>Партия отправлена автодоставкой (PipeSend).</summary>
        public bool Automatic;
        /// <summary>Товар события заводов (GoodId) или null.</summary>
        public string ItemId;

        public static OutpostViewCueArgs Create(OutpostViewCue cue, Vector3 position) => new OutpostViewCueArgs
        {
            Cue = cue,
            Position = position,
            WorkerId = -1,
            TargetId = -1,
            Slot = -1,
            Branch = -1,
        };
    }
}
