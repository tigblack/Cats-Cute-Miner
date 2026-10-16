using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Каталог карточек режима (задание «возвращение карточек»): все записи Cards исходного json-data.json с исходными Id, Rarity,
    /// MineUnlock, SortingWeight, TargetIds, IsManager и модификаторами, стоимости CardUpgradeCosts, записи Gacha (сундуки), циклы
    /// наград камней и множители зон — плюс эффекты и шансы из <see cref="CardRules"/>. Неизменяем; строится один раз на источник.
    /// Дробные числа источника переводятся в точные дроби по кратчайшей десятичной записи (0.35 → 7/20), без double в расчётах.
    /// </summary>
    public sealed class CardCatalog
    {
        readonly Dictionary<string, CardDef> m_ById = new Dictionary<string, CardDef>(StringComparer.Ordinal);
        readonly Dictionary<string, ChestDef> m_Chests = new Dictionary<string, ChestDef>(StringComparer.Ordinal);
        readonly Dictionary<string, RockRewardDef> m_Rocks = new Dictionary<string, RockRewardDef>(StringComparer.Ordinal);
        readonly List<CardDef> m_Cards = new List<CardDef>();
        readonly List<CardDef>[] m_ByRarity = new List<CardDef>[CardRules.RarityCount + 1];
        ZoneChestScale[] m_Zones;

        public CardRules Rules { get; private set; }
        /// <summary>Все карточки: SortingWeight по убыванию, затем Id.</summary>
        public IReadOnlyList<CardDef> Cards => m_Cards;
        /// <summary>SHA-256 источника, из которого построен каталог.</summary>
        public string SourceSha256 { get; private set; }
        public int ZoneCount => m_Zones.Length;

        public CardDef Get(string id) => id != null && m_ById.TryGetValue(id, out var d) ? d : null;
        public bool TryGet(string id, out CardDef def) => m_ById.TryGetValue(id ?? "", out def);

        /// <summary>Карточки редкости 1…6 в порядке Id.</summary>
        public IReadOnlyList<CardDef> OfRarity(int rarity) =>
            rarity >= 1 && rarity <= CardRules.RarityCount ? m_ByRarity[rarity] : (IReadOnlyList<CardDef>)Array.Empty<CardDef>();

        public ChestDef Chest(string id) => id != null && m_Chests.TryGetValue(id, out var c) ? c : null;
        public RockRewardDef Rock(string rockTypeId) => rockTypeId != null && m_Rocks.TryGetValue(rockTypeId, out var r) ? r : null;

        /// <summary>Множители сундуков шахты z (RankMultipliers зоны).</summary>
        public ZoneChestScale Zone(int zone) => m_Zones[Math.Max(1, Math.Min(zone, m_Zones.Length)) - 1];

        public static CardCatalog Build(BalanceRepository repo, CardRules rules)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var cat = new CardCatalog { Rules = rules, SourceSha256 = repo.Sha256 };
            for (int r = 0; r < cat.m_ByRarity.Length; r++) cat.m_ByRarity[r] = new List<CardDef>();
            foreach (var c in repo.CardsInOrder)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) throw new OutpostConfigException("Cards: запись без Id");
                if (c.Rarity < 1 || c.Rarity > CardRules.RarityCount) throw new OutpostConfigException("Cards." + c.Id + ": редкость " + c.Rarity);
                if (!repo.CardUpgradeCosts.TryGetValue(c.Rarity, out var cost))
                    throw new OutpostConfigException("CardUpgradeCosts: нет редкости " + c.Rarity);
                if (cost.Duplicates == null || cost.SoftCurrency == null || cost.Duplicates.Length != cost.SoftCurrency.Length)
                    throw new OutpostConfigException("CardUpgradeCosts." + c.Rarity + ": длины Duplicates и SoftCurrency");
                var effect = EffectFor(rules, c);
                if (effect.Kind == CardEffectKind.FactoryOutput && (c.TargetIds == null || c.TargetIds.Length == 0))
                    throw new OutpostConfigException("card_rules: " + c.Id + ": FactoryOutput без TargetIds (тип завода)");
                var def = new CardDef
                {
                    Id = c.Id,
                    Rarity = c.Rarity,
                    MineUnlock = Math.Max(1, c.MineUnlock),
                    SortingWeight = c.SortingWeight,
                    TargetIds = c.TargetIds != null ? (string[])c.TargetIds.Clone() : new string[0],
                    IsManager = c.IsManager,
                    StatModifierType = c.StatModifierType,
                    ModifierFormulaType = c.ModifierFormulaType,
                    ModifierBase = c.ModifierBase,
                    ModifierMultiplier = c.ModifierMultiplier,
                    ModifierGrowth = c.ModifierGrowth,
                    ModifierPower = c.ModifierPower,
                    ModifierRound = c.ModifierRound,
                    UpgradeCopies = (int[])cost.Duplicates.Clone(),
                    UpgradeElixir = new long[cost.SoftCurrency.Length],
                    Effect = effect,
                };
                for (int i = 0; i < cost.SoftCurrency.Length; i++)
                {
                    double v = cost.SoftCurrency[i];
                    if (!(v >= 0) || v != Math.Floor(v) || v > long.MaxValue / 2)
                        throw new OutpostConfigException("CardUpgradeCosts." + c.Rarity + ".SoftCurrency[" + i + "]: не целое");
                    def.UpgradeElixir[i] = (long)v;
                    if (def.UpgradeCopies[i] < 1) throw new OutpostConfigException("CardUpgradeCosts." + c.Rarity + ".Duplicates[" + i + "] < 1");
                }
                if (cat.m_ById.ContainsKey(def.Id)) throw new OutpostConfigException("Cards: повтор " + def.Id);
                cat.m_ById[def.Id] = def;
                cat.m_Cards.Add(def);
                cat.m_ByRarity[def.Rarity].Add(def);
            }
            if (cat.m_Cards.Count == 0) throw new OutpostConfigException("Cards: пусто");
            cat.m_Cards.Sort((a, b) => a.SortingWeight != b.SortingWeight ? b.SortingWeight.CompareTo(a.SortingWeight) : string.CompareOrdinal(a.Id, b.Id));
            foreach (var list in cat.m_ByRarity) list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (rules.TutorialCard != null && !cat.m_ById.ContainsKey(rules.TutorialCard))
                throw new OutpostConfigException("card_rules: TutorialCard " + rules.TutorialCard + " нет в Cards");

            foreach (var g in repo.Gacha.Values)
            {
                var chest = new ChestDef
                {
                    Id = g.Id,
                    GachaType = g.GachaType,
                    MimicId = string.IsNullOrEmpty(g.MimicId) ? null : g.MimicId,
                    BaseNumCards = Math.Max(0, g.BaseNumCards),
                    ElixirMin = Exact(g.SoftCurrencyMin),
                    ElixirMax = Exact(Math.Max(g.SoftCurrencyMin, g.SoftCurrencyMax)),
                    UsesMinimums = g.GuaranteedCardMin != null && g.GuaranteedCardMin.Length > 0,
                    Chances = rules.ChancesFor(g.Id),
                };
                var ids = g.GuaranteedCardIds ?? new string[0];
                for (int i = 0; i < ids.Length; i++)
                {
                    int count = chest.UsesMinimums && i < g.GuaranteedCardMin.Length ? g.GuaranteedCardMin[i]
                        : g.GuaranteedCardCounts != null && i < g.GuaranteedCardCounts.Length ? g.GuaranteedCardCounts[i] : 1;
                    if (!cat.m_ById.ContainsKey(ids[i])) throw new OutpostConfigException("Gacha." + g.Id + ": нет карточки " + ids[i]);
                    if (count > 0) chest.Guaranteed.Add(new CardGrant { CardId = ids[i], Copies = count });
                }
                cat.m_Chests[chest.Id] = chest;
            }

            foreach (var rock in repo.Rocks.Values)
            {
                if (rock.RewardCycle == null || rock.RewardCycle.Length == 0) continue;
                var def = new RockRewardDef
                {
                    RockId = rock.Id,
                    Cycle = new RockReward[rock.RewardCycle.Length],
                    SoftFormulaType = rock.SoftCurrencyFormulaType,
                    SoftBase = Exact(rock.SoftCurrencyBase),
                    SoftMultiplier = Exact(rock.SoftCurrencyMultiplier),
                    SoftGrowth = Exact(rock.SoftCurrencyGrowth),
                };
                bool any = false;
                for (int i = 0; i < rock.RewardCycle.Length; i++)
                {
                    var e = rock.RewardCycle[i];
                    var kind = RockRewardKind.None;
                    string chestId = null;
                    if (e != null && e.RewardType == RewardTypes.Gacha && cat.m_Chests.ContainsKey(e.DetailedType ?? ""))
                    {
                        kind = RockRewardKind.Chest;
                        chestId = e.DetailedType;
                    }
                    else if (e != null && e.RewardType == RewardTypes.Currency && e.DetailedType == "Soft") kind = RockRewardKind.Elixir;
                    def.Cycle[i] = new RockReward { Kind = kind, ChestId = chestId };
                    any |= kind != RockRewardKind.None;
                }
                if (any && def.SoftFormulaType != 0 && Array.Exists(def.Cycle, x => x.Kind == RockRewardKind.Elixir))
                    throw new OutpostConfigException("Rocks." + rock.Id + ": эликсир с SoftCurrencyFormulaType " + def.SoftFormulaType + " (ожидался F-0)");
                if (any) cat.m_Rocks[rock.Id] = def;
            }

            cat.m_Zones = new ZoneChestScale[repo.ZoneCount];
            for (int z = 1; z <= repo.ZoneCount; z++)
            {
                var m = repo.ZoneMultipliers(z);
                cat.m_Zones[z - 1] = new ZoneChestScale
                {
                    CardsNormal = Exact(m.GachaCardsMultNormal),
                    CardsPremium = Exact(m.GachaCardsMultPremium),
                    CardsRare = Exact(m.GachaCardsMultRare),
                    ElixirNormal = Exact(m.GachaSoftCurrencyMultNormal),
                    ElixirPremium = Exact(m.GachaSoftCurrencyMultPremium),
                    ElixirRare = Exact(m.GachaSoftCurrencyMultRare),
                    MiningElixir = Exact(m.MiningSoftCurrencyMultiplier),
                };
            }
            return cat;
        }

        static CardEffectRule EffectFor(CardRules rules, CardDto c)
        {
            if (rules.Effects.TryGetValue(c.Id, out var own)) return own;
            if (c.IsManager && rules.Effects.TryGetValue("manager-" + c.Rarity.ToString(CultureInfo.InvariantCulture), out var manager)) return manager;
            throw new OutpostConfigException("card_rules: нет эффекта для " + c.Id + " (код " + c.StatModifierType + ")");
        }

        /// <summary>
        /// Точная дробь десятичной записи числа источника: double → decimal (15 значащих цифр, без экспоненты), 0.35 → 7/20.
        /// </summary>
        public static Rational Exact(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e15) throw new OutpostConfigException("число источника " + value);
            return Rational.Parse(((decimal)value).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Карточка каталога: исходные поля и адаптированный эффект.</summary>
    public sealed class CardDef
    {
        public string Id;
        public int Rarity;
        public int MineUnlock;
        public int SortingWeight;
        public string[] TargetIds;
        public bool IsManager;
        public int StatModifierType;
        public int ModifierFormulaType;
        public double ModifierBase, ModifierMultiplier, ModifierGrowth, ModifierPower, ModifierRound;
        /// <summary>Переход L → L+1: копии UpgradeCopies[L−1] и эликсир UpgradeElixir[L−1] (CardUpgradeCosts редкости).</summary>
        public int[] UpgradeCopies;
        public long[] UpgradeElixir;
        public CardEffectRule Effect;

        /// <summary>Максимальный уровень: 1 + длина массива стоимостей (Common 14 … Majestic 5).</summary>
        public int MaxLevel => UpgradeCopies.Length + 1;

        /// <summary>Значение эффекта на уровне (0 — не получена).</summary>
        public Rational EffectAt(int level) => Effect.ValueAt(Math.Min(level, MaxLevel));

        /// <summary>Карточка завода (cards-3): эффект <see cref="CardEffectKind.FactoryOutput"/> по типам TargetIds.</summary>
        public bool IsFactoryOutput => Effect != null && Effect.Kind == CardEffectKind.FactoryOutput;

        public bool Targets(string typeId)
        {
            if (typeId == null) return false;
            foreach (var t in TargetIds)
                if (t == typeId) return true;
            return false;
        }
    }

    /// <summary>Сундук (запись Gacha источника): гарантированные карты, эликсир, число случайных карт, шансы редкости Cat Miners.</summary>
    public sealed class ChestDef
    {
        public string Id;
        public int GachaType;
        public string MimicId;
        public int BaseNumCards;
        public Rational ElixirMin, ElixirMax;
        /// <summary>У записи есть GuaranteedCardMin: как в прежней игре — нижние границы карт и эликсира.</summary>
        public bool UsesMinimums;
        public int[] Chances;
        public readonly List<CardGrant> Guaranteed = new List<CardGrant>();
    }

    [Serializable]
    public sealed class CardGrant
    {
        public string CardId;
        public int Copies;
    }

    public enum RockRewardKind
    {
        None,
        Chest,
        Elixir,
    }

    public struct RockReward
    {
        public RockRewardKind Kind;
        public string ChestId;
    }

    /// <summary>Цикл наград камня (Rocks[].RewardCycle): сундуки и эликсир; золото — порциями груза.</summary>
    public sealed class RockRewardDef
    {
        public string RockId;
        public RockReward[] Cycle;
        public int SoftFormulaType;
        public Rational SoftBase, SoftMultiplier, SoftGrowth;
    }

    /// <summary>Множители сундуков шахты (RankMultipliers): карты и эликсир по GachaType, эликсир камней.</summary>
    public sealed class ZoneChestScale
    {
        public Rational CardsNormal, CardsPremium, CardsRare;
        public Rational ElixirNormal, ElixirPremium, ElixirRare;
        public Rational MiningElixir;

        /// <summary>GachaType 0 — Normal, 1 — Premium, 3 — Rare, 2 — без множителя (как в прежнем GachaService).</summary>
        public Rational CardsFor(int gachaType) =>
            gachaType == 0 ? CardsNormal : gachaType == 1 ? CardsPremium : gachaType == 3 ? CardsRare : Rational.One;

        public Rational ElixirFor(int gachaType) =>
            gachaType == 0 ? ElixirNormal : gachaType == 1 ? ElixirPremium : gachaType == 3 ? ElixirRare : Rational.One;
    }
}
