using System;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Конфигурации режима и их hash (ТЗ 18). Runtime читает тексты из Resources/Outpost/*.json
    /// (<see cref="ResourcePaths"/>) и вызывает <see cref="Parse"/>; ядро и тесты — <see cref="LoadDefault"/> из встроенных
    /// строк <c>Default*Json</c> (OutpostConfigSet.Defaults.cs, генерируется Tools/gen_outpost_config_defaults.py из тех же файлов).
    /// Hash — SHA-256 канонической сериализации разобранного объекта, а не байтов файла.
    /// </summary>
    public sealed partial class OutpostConfigSet
    {
        /// <summary>
        /// Пути Resources.Load&lt;TextAsset&gt; (без расширения): Balance, Equipment, Rewards, Overrides, CardRules, Topology, Store,
        /// FactoryRules (ТЗ 2.2). Все тексты по порядку принимает <see cref="ParseResources"/>.
        /// </summary>
        public static readonly string[] ResourcePaths =
        {
            "Outpost/outpost_balance",
            "Outpost/equipment_catalog",
            "Outpost/stage_rewards",
            "Outpost/level_overrides",
            "Outpost/card_rules",
            "Outpost/mine_topology",
            "Outpost/store_catalog",
            "Outpost/factory_rules",
        };

        public OutpostBalanceConfig Balance { get; private set; }
        public EquipmentCatalog Equipment { get; private set; }
        public StageRewardConfig Rewards { get; private set; }
        public LevelOverrides Overrides { get; private set; }
        /// <summary>Правила карточек Cat Miners (эффекты, шансы сундуков).</summary>
        public CardRules CardRules { get; private set; }
        public MineTopologyCatalog Topologies { get; private set; }
        /// <summary>Каталог магазина (ТЗ 10.5): плитки, цены, стартовый набор. Не входит в <see cref="CatalogHash"/>.</summary>
        public StoreCatalog Store { get; private set; }
        /// <summary>
        /// Каталог карточек из исходного json-data.json и <see cref="CardRules"/>; null — набор без карточек (тесты ядра без
        /// источника): нет сундуков, учебной выдачи и бонусов. Игра подключает его через <see cref="WithCards"/>.
        /// </summary>
        public CardCatalog Cards { get; private set; }
        /// <summary>Правила заводов (ТЗ 2.2 §3.2, factory_rules.json): есть всегда; снимок кладётся в run при создании шахты.</summary>
        public FactoryRules FactoryRules { get; private set; }
        /// <summary>
        /// Каталог заводов (ТЗ 2.2 §3): 20 типов MineShafts + товары + карточки заводов, сверенный с источником. Строится вместе с
        /// <see cref="Cards"/> в <see cref="WithCards"/>; null — набор без источника (тесты ядра): нет бонусов выпуска карточек.
        /// </summary>
        public FactoryCatalog Factories { get; private set; }

        public string BalanceHash { get; private set; }
        public string EquipmentHash { get; private set; }
        public string RewardsHash { get; private set; }
        public string OverridesHash { get; private set; }
        public string CardRulesHash { get; private set; }
        /// <summary>Hash каталога магазина: цены не влияют на совместимость сохранений и в CatalogHash не входят.</summary>
        public string StoreHash { get; private set; }
        /// <summary>Hash правил заводов: значения снимаются в run, поэтому в CatalogHash не входит.</summary>
        public string FactoryRulesHash { get; private set; }
        /// <summary>Hash каталога экипировки, наград и правил карточек (поле конверта сохранения CatalogHash).</summary>
        public string CatalogHash { get; private set; }

        OutpostConfigSet() { }

        /// <summary>Встроенные значения по умолчанию (совпадают с файлами Resources/Outpost), без каталога карточек.</summary>
        public static OutpostConfigSet LoadDefault() =>
            Parse(DefaultBalanceJson, DefaultEquipmentJson, DefaultRewardsJson, DefaultOverridesJson, DefaultCardRulesJson, DefaultTopologyJson,
                DefaultStoreJson, DefaultFactoryRulesJson);

        /// <summary>Четыре конфигурации; правила карточек — встроенные по умолчанию.</summary>
        public static OutpostConfigSet Parse(string balanceJson, string equipmentJson, string rewardsJson, string overridesJson) =>
            Parse(balanceJson, equipmentJson, rewardsJson, overridesJson, DefaultCardRulesJson);

        public static OutpostConfigSet Parse(string balanceJson, string equipmentJson, string rewardsJson, string overridesJson, string cardRulesJson)
        {
            var set = new OutpostConfigSet
            {
                Balance = OutpostBalanceConfig.Parse(balanceJson),
                Equipment = EquipmentCatalog.Parse(equipmentJson),
                Overrides = LevelOverrides.Parse(overridesJson),
                CardRules = CardRules.Parse(cardRulesJson),
                Store = StoreCatalog.Parse(DefaultStoreJson),
                FactoryRules = FactoryRules.Parse(DefaultFactoryRulesJson),
            };
            set.Rewards = StageRewardConfig.Parse(rewardsJson, set.Equipment);
            set.RecomputeHashes();
            return set;
        }

        public static OutpostConfigSet Parse(string balanceJson, string equipmentJson, string rewardsJson, string overridesJson,
            string cardRulesJson, string topologyJson)
        {
            var set = Parse(balanceJson, equipmentJson, rewardsJson, overridesJson, cardRulesJson);
            set.Topologies = MineTopologyCatalog.Parse(topologyJson);
            return set;
        }

        /// <summary>Семь файлов Resources/Outpost; правила заводов — встроенные по умолчанию.</summary>
        public static OutpostConfigSet Parse(string balanceJson, string equipmentJson, string rewardsJson, string overridesJson,
            string cardRulesJson, string topologyJson, string storeJson)
        {
            var set = Parse(balanceJson, equipmentJson, rewardsJson, overridesJson, cardRulesJson, topologyJson);
            set.Store = StoreCatalog.Parse(storeJson);
            set.RecomputeHashes();
            return set;
        }

        /// <summary>Все восемь файлов Resources/Outpost в порядке <see cref="ResourcePaths"/>.</summary>
        public static OutpostConfigSet Parse(string balanceJson, string equipmentJson, string rewardsJson, string overridesJson,
            string cardRulesJson, string topologyJson, string storeJson, string factoryRulesJson)
        {
            var set = Parse(balanceJson, equipmentJson, rewardsJson, overridesJson, cardRulesJson, topologyJson, storeJson);
            set.FactoryRules = FactoryRules.Parse(factoryRulesJson);
            set.RecomputeHashes();
            return set;
        }

        /// <summary>Тексты всех файлов в порядке <see cref="ResourcePaths"/> (Runtime, Editor). Длина должна совпадать.</summary>
        public static OutpostConfigSet ParseResources(string[] texts)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));
            if (texts.Length != ResourcePaths.Length)
                throw new ArgumentException("ожидалось " + ResourcePaths.Length + " конфигураций, получено " + texts.Length, nameof(texts));
            return Parse(texts[0], texts[1], texts[2], texts[3], texts[4], texts[5], texts[6], texts[7]);
        }

        /// <summary>Legacy geometry is retained for comparisons and for runs without a topology snapshot.</summary>
        public OutpostConfigSet WithTopologies(MineTopologyCatalog topologies)
        {
            var set = new OutpostConfigSet { Balance = Balance, Equipment = Equipment, Rewards = Rewards,
                Overrides = Overrides, CardRules = CardRules, Cards = Cards, Topologies = topologies, Store = Store,
                FactoryRules = FactoryRules, Factories = Factories };
            set.RecomputeHashes();
            return set;
        }

        /// <summary>Набор с заменённым профилем чисел (тесты версий конфигурации, калибровка).</summary>
        public OutpostConfigSet WithBalance(OutpostBalanceConfig balance)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            balance.Validate();
            var set = new OutpostConfigSet { Balance = balance, Equipment = Equipment, Rewards = Rewards, Overrides = Overrides, CardRules = CardRules, Cards = Cards, Topologies = Topologies, Store = Store,
                FactoryRules = FactoryRules, Factories = Factories };
            set.RecomputeHashes();
            return set;
        }

        /// <summary>Набор с профилем без множителя темпа шахт (K = 1, <see cref="OutpostBalanceConfig.WithoutPace"/>).</summary>
        public OutpostConfigSet WithoutPace() => WithBalance(Balance.WithoutPace());

        /// <summary>
        /// Набор с заменённым каталогом магазина (playable-сборка: только сундук за рекламу и тайм-скипы стартового набора).
        /// Каталог проверяется, сундуки — по подключённому каталогу карточек; CatalogHash не меняется.
        /// </summary>
        public OutpostConfigSet WithStore(StoreCatalog store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            store.Validate();
            store.ValidateChests(Cards);
            var set = new OutpostConfigSet { Balance = Balance, Equipment = Equipment, Rewards = Rewards, Overrides = Overrides, CardRules = CardRules, Cards = Cards, Topologies = Topologies, Store = store,
                FactoryRules = FactoryRules, Factories = Factories };
            set.RecomputeHashes();
            return set;
        }

        /// <summary>
        /// Набор с каталогом карточек исходника repo (61 запись Cards, CardUpgradeCosts, Gacha, циклы камней) и каталогом заводов
        /// (<see cref="Factories"/>: MineShafts + <see cref="FactoryRules"/> + карточки заводов, проверки ТЗ 2.2 §3.1).
        /// </summary>
        public OutpostConfigSet WithCards(CatMiners.Core.Data.BalanceRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var set = new OutpostConfigSet { Balance = Balance, Equipment = Equipment, Rewards = Rewards, Overrides = Overrides, CardRules = CardRules, Topologies = Topologies, Store = Store,
                FactoryRules = FactoryRules };
            set.Cards = CardCatalog.Build(repo, CardRules);
            set.Factories = FactoryCatalog.Build(repo, FactoryRules);
            set.Store?.ValidateChests(set.Cards);
            set.RecomputeHashes();
            return set;
        }

        void RecomputeHashes()
        {
            BalanceHash = Balance.ComputeHash();
            EquipmentHash = Equipment.ComputeHash();
            RewardsHash = Rewards.ComputeHash();
            OverridesHash = Overrides.ComputeHash();
            CardRulesHash = CardRules.ComputeHash();
            StoreHash = Store?.ComputeHash();
            FactoryRulesHash = FactoryRules?.ComputeHash();
            CatalogHash = OutpostJson.Sha256Hex(Equipment.CanonicalJson() + "\n" + Rewards.CanonicalJson() + "\n" + CardRules.CanonicalJson());
        }
    }
}
