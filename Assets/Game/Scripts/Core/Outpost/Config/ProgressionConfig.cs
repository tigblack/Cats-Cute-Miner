// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Первая сессия и открытие функций (Resources/Outpost/progression.json, Docs/Rework/FIRST_SESSION.md): какие разделы и ветки
    /// улучшений доступны с какой шахты или после какого события, что выдаётся один раз при открытии карточек и экипировки и где
    /// стоит клетка Mia в шахте 1. Доступность — постоянная отметка профиля (<see cref="ProgressionState.Unlocked"/>), её считает
    /// <see cref="ProgressionService"/> по этим правилам. Те же записи читает интерфейс: закрытая кнопка раздела — с замком, тап
    /// называет требование («Unlocks in Mine 2»). Bruno unlocks Equipment after actual Mine 2 completion; section introductions are queued by the HUD.
    /// </summary>
    public sealed class ProgressionConfig
    {
        public const string CurrentVersion = "progression-4";

        // Ключи функций (FeatureDef.Id). Ветки улучшений — "branch.&lt;имя ветки в нижнем регистре&gt;".
        public const string Cards = "cards";
        public const string Store = "store";
        public const string Equipment = "equipment";
        public const string SellGoods = "sellgoods";

        // События раннего открытия (FeatureDef.Event): по фактам состояния, а не по нажатиям интерфейса.
        /// <summary>Первая сданная на базе порция (Counters.FirstDeliveryTick ≥ 0).</summary>
        public const string EventFirstDelivery = "first-delivery";
        /// <summary>Первая покупка «Кирок» (Mining Power) в шахте.</summary>
        public const string EventFirstPickaxes = "first-pickaxes";
        /// <summary>У игрока уже есть полученная карточка (сундук до шахты открытия — доступ, а не замок).</summary>
        public const string EventOwnCards = "own-cards";
        /// <summary>У игрока уже есть предмет экипировки.</summary>
        public const string EventOwnItems = "own-items";
        /// <summary>Текущая шахта с заводами — существующее открытие продажи товаров (Sell Goods).</summary>
        public const string EventFactories = "factories";
        public const string EventEquipmentReady = "blacksmith-and-mine2";
        public const string BlacksmithCharacter = "bruno";

        public string Version;
        /// <summary>Функции в порядке открытия (строка «Next upgrade» панели «База» берёт первую неоткрытую ветку).</summary>
        public List<FeatureDef> Features = new List<FeatureDef>();
        /// <summary>Открытие карточек (шахта 2): какая карточка выдаётся и на сколько улучшений хватает выданных ресурсов.</summary>
        public CardsIntroRules CardsIntro = new CardsIntroRules();
        /// <summary>Equipment after Bruno rescue and Mine 2 completion: guarantee a usable owned pickaxe.</summary>
        public EquipmentIntroRules EquipmentIntro = new EquipmentIntroRules();
        /// <summary>Спасение персонажа в последнем участке шахты (клетка и замок — <see cref="MineRescuePlacer"/>).</summary>
        public RescueRules Rescue = new RescueRules();
        /// <summary>Separate Mine 2 rescue; never joins the mining crew.</summary>
        public RescueRules Blacksmith;

        public RescueRules RescueForZone(int zone) => Blacksmith != null && Blacksmith.Zone == zone ? Blacksmith : Rescue != null && Rescue.Zone == zone ? Rescue : null;

        /// <summary>Ключ функции ветки: "branch.pickaxes" и т. п.</summary>
        public static string BranchFeature(UpgradeBranch branch) => "branch." + branch.ToString().ToLowerInvariant();

        public FeatureDef Feature(string id)
        {
            if (id == null || Features == null) return null;
            foreach (var f in Features)
                if (f != null && f.Id == id) return f;
            return null;
        }

        public FeatureDef BranchDef(UpgradeBranch branch) => Feature(BranchFeature(branch));

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static ProgressionConfig Parse(string json)
        {
            ProgressionConfig cfg;
            try
            {
                cfg = OutpostJson.Deserialize<ProgressionConfig>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("progression: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("progression: пустой документ");
            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("progression: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (Features == null || Features.Count == 0) Fail("нет Features");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var branches = new HashSet<UpgradeBranch>();
            foreach (var f in Features)
            {
                if (f == null || string.IsNullOrEmpty(f.Id)) Fail("функция без Id");
                if (!ids.Add(f.Id)) Fail("повтор функции " + f.Id);
                if (f.Zone < 0 || f.Zone > OutpostIds.LastZone) Fail(f.Id + ": Zone вне [0, " + OutpostIds.LastZone + "]");
                if (f.Event != null && f.Event != EventFirstDelivery && f.Event != EventFirstPickaxes && f.Event != EventOwnCards &&
                    f.Event != EventOwnItems && f.Event != EventFactories && f.Event != EventEquipmentReady)
                    Fail(f.Id + ": неизвестное событие " + f.Event);
                if (f.With != null)
                {
                    // «Вместе с» — только по уже перечисленной функции: открытие считается в том же проходе по порядку правил.
                    if (f.With == f.Id || !ids.Contains(f.With)) Fail(f.Id + ": With должна ссылаться на функцию выше по списку");
                    if (f.Zone != 0 || f.Event != null || f.Branch.HasValue) Fail(f.Id + ": With без Zone, Event и Branch");
                }
                else if (f.Zone == 0 && f.Event == null) Fail(f.Id + ": нет ни Zone, ни Event, ни With");
                if (f.Branch.HasValue)
                {
                    if (f.Id != BranchFeature(f.Branch.Value)) Fail(f.Id + ": ключ ветки должен быть " + BranchFeature(f.Branch.Value));
                    branches.Add(f.Branch.Value);
                }
                else if (f.Id.StartsWith("branch.", StringComparison.Ordinal)) Fail(f.Id + ": нет Branch");
            }
            for (int b = 0; b < OutpostIds.BranchCount; b++)
                if (!branches.Contains((UpgradeBranch)b)) Fail("нет функции ветки " + (UpgradeBranch)b);
            if (Feature(Cards) == null || Feature(Store) == null || Feature(Equipment) == null || Feature(SellGoods) == null)
                Fail("нужны функции cards, store, equipment, sellgoods");
            if (CardsIntro == null || string.IsNullOrEmpty(CardsIntro.Card) || CardsIntro.Upgrades < 0) Fail("CardsIntro");
            if (EquipmentIntro == null || string.IsNullOrEmpty(EquipmentIntro.Item)) Fail("EquipmentIntro");
            foreach (var Rescue in new[] { this.Rescue, Blacksmith })
            {
                if (Rescue == null) continue;
                if (Rescue.Zone < 1 || Rescue.Zone > OutpostIds.LastZone) Fail("Rescue.Zone");
                if (string.IsNullOrEmpty(Rescue.Character) || string.IsNullOrEmpty(Rescue.CageFamily) || string.IsNullOrEmpty(Rescue.LockFamily))
                    Fail("Rescue: нужны Character, CageFamily, LockFamily");
                if (string.IsNullOrEmpty(Rescue.LockTier)) Fail("Rescue.LockTier");
                if (Rescue.RouteFocus < 0) Fail("Rescue.RouteFocus < 0");
            }
            if (Blacksmith != null && (Blacksmith.Character != BlacksmithCharacter || Blacksmith.Zone != 2 ||
                Rescue != null && (Rescue.Zone == Blacksmith.Zone || Rescue.Character == Blacksmith.Character)))
                Fail("Blacksmith must be a distinct bruno rescue in Mine 2");
            if (Feature(Equipment).Event == EventEquipmentReady && (Feature(Equipment).Zone != 0 || Blacksmith == null))
                Fail("Equipment completion rule requires Blacksmith and cannot have a zone-entry fallback");
        }

        /// <summary>Проверка ссылок на каталоги (стартовая карточка, стартовый предмет); null — каталог не подключён.</summary>
        public void ValidateRefs(CardCatalog cards, EquipmentCatalog equipment)
        {
            if (cards != null && cards.Get(CardsIntro.Card) == null) throw new OutpostConfigException("progression: нет карточки " + CardsIntro.Card);
            if (equipment != null)
            {
                if (!equipment.TryGet(EquipmentIntro.Item, out var starter)) throw new OutpostConfigException("progression: нет предмета " + EquipmentIntro.Item);
                if (starter.Slot != EquipSlot.Pickaxe) throw new OutpostConfigException("progression: EquipmentIntro must grant a pickaxe");
            }
        }
    }

    /// <summary>Функция первой сессии: раздел (карточки, магазин, экипировка, продажа товаров) или ветка улучшений.</summary>
    public sealed class FeatureDef
    {
        public string Id;
        /// <summary>Ветка улучшений этой функции; null — раздел.</summary>
        public UpgradeBranch? Branch;
        /// <summary>Постоянное открытие при входе в шахту Zone (наибольшая достигнутая шахта профиля); 0 — только по событию.</summary>
        public int Zone;
        /// <summary>Раннее открытие по факту состояния (константы Event* <see cref="ProgressionConfig"/>); null — только по шахте.</summary>
        public string Event;
        /// <summary>
        /// Открывается вместе с функцией With (её Id выше по списку): Store — вместе с Cards. Тогда Zone, Event и Branch не задаются,
        /// требование на закрытой кнопке — требование той функции.
        /// </summary>
        public string With;
        /// <summary>Иконка окна требования закрытого раздела и строки «Next upgrade» (имя спрайта атласа).</summary>
        public string Icon;
    }

    public sealed class CardsIntroRules
    {
        /// <summary>Существующая стартовая карточка (не придуманный ID): полезный пассивный бонус, работает с автоматической добычей.</summary>
        public string Card;
        /// <summary>Сколько улучшений после первой копии гарантируют выданные копии и эликсир (по CardUpgradeCosts и запасу игрока).</summary>
        public int Upgrades = 1;
    }

    public sealed class EquipmentIntroRules
    {
        /// <summary>Стартовый предмет каталога экипировки; не выдаётся, если в его слоте у игрока уже есть предмет.</summary>
        public string Item;
    }

    /// <summary>Спасение персонажа шахты: клетка-препятствие с персонажем и замок — цель добычи на подходе к ней.</summary>
    public sealed class RescueRules
    {
        public int Zone = 1;
        /// <summary>ID персонажа: после спасения отмечен в профиле (<see cref="ProgressionState.Crew"/>) и встречает игрока в магазине.</summary>
        public string Character;
        /// <summary>Семейство постоянного препятствия-клетки (спрайты prop_&lt;семейство&gt;_*).</summary>
        public string CageFamily;
        /// <summary>Семейство разрушаемого замка (цель <c>x</c>, спрайты debris_&lt;семейство&gt;_*).</summary>
        public string LockFamily;
        /// <summary>Ярус прочности замка из правил обстановки (mine_dressing.json → Tiers).</summary>
        public string LockTier = "Medium";
        /// <summary>
        /// Насколько клеток пути котики предпочитают цели на маршруте к клетке, пока она закрыта (без штрафа за занятость ветви):
        /// автоматическая команда сама расчищает путь к цели первой сессии. 0 — обычный выбор ближайшей цели.
        /// </summary>
        public int RouteFocus = 8;
        /// <summary>
        /// Руда-заполнитель на маршруте к клетке снимается (остаются завалы и цели источника): «расчистить путь» — разбить завалы
        /// главного прохода, а не всю руду до конца шахты (время спасения первой сессии).
        /// </summary>
        public bool ClearRouteOre;
    }
}
