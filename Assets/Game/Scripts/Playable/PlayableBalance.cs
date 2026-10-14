using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;

namespace CatMiners.Playable
{
    /// <summary>
    /// Профиль баланса playable «outpost-2.1-b4-playable» — вариант p3 замера темпа (Docs/Playable/PLAYABLE_TZ.md §3.2, §5;
    /// Tools/CoreTests/Drafts/PlayablePacingDraft.cs): 3 котика на старте, золото за порцию ×2, без удорожания по глубине,
    /// тап ½ удара котика, найм ×1,5 за уровень вместо ×1,9, вехи «×2» кирки на уровнях 5 и 10. Накладывается на копию
    /// загруженного профиля b4; экипировка, награды и карточки не меняются (CatalogHash тот же). Скорость симуляции не
    /// трогается: она задаёт и анимацию. Магазин урезан (ТЗ §3.4): только сундук за рекламу и тайм-скипы стартового набора —
    /// без пакетов гемов и эликсира и сундуков за гемы, поэтому бесплатные гемы тестового режима не ломают темп отрезка.
    /// Профиль и каталог живут только в этой сборке, не в Resources основной игры.
    /// </summary>
    public static class PlayableBalance
    {
        public const string Version = "outpost-2.1-b4-playable";
        /// <summary>Версия урезанного каталога магазина: «версия основного каталога» + этот суффикс.</summary>
        public const string StoreSuffix = "-playable";

        /// <summary>Набор конфигов с профилем playable и урезанным магазином вместо загруженных (для <see cref="Runtime.Outpost.OutpostController.ConfigsOverride"/>).</summary>
        public static OutpostConfigSet Apply(OutpostConfigSet configs)
        {
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var set = configs.WithBalance(Overlay(configs.Balance));
            return configs.Store != null ? set.WithStore(Store(configs.Store)) : set;
        }

        /// <summary>Копия профиля <paramref name="source"/> с правками p3; исходный профиль не меняется.</summary>
        public static OutpostBalanceConfig Overlay(OutpostBalanceConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var b = source.Clone();
            b.Version = Version;
            b.StartWorkers = 3;
            b.CoinBase = Rational.FromLong(10);
            b.CostZoneStep = Rational.Zero;
            b.TapDamageFactor = Rational.Of(1, 2);
            b.Branch(UpgradeBranch.Team).Growth = Rational.Of(3, 2);
            b.Branch(UpgradeBranch.Pickaxes).Milestones = new[] { 5, 10 };
            return b;
        }

        /// <summary>
        /// Каталог магазина playable из <paramref name="source"/>: сундуки за рекламу и тайм-скипы, которые выдаёт стартовый
        /// набор (их запас), в прежнем порядке разделов; без гемов на старте. Исходный каталог не меняется.
        /// </summary>
        public static StoreCatalog Store(StoreCatalog source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var starter = source.StarterConsumables ?? new Dictionary<string, int>(StringComparer.Ordinal);
            var items = new List<StoreItemDef>();
            foreach (var def in source.Items)
            {
                bool adChest = def.Kind == StoreItemKind.Chest && def.Currency == StoreCurrency.Ad;
                bool starterSkip = def.Kind == StoreItemKind.TimeSkip && starter.TryGetValue(def.Id, out int n) && n > 0;
                if (adChest || starterSkip) items.Add(def);
            }
            var sections = new List<string>();
            foreach (string section in source.Sections)
                if (items.Exists(d => d.Section == section)) sections.Add(section);
            var store = new StoreCatalog
            {
                Version = source.Version + StoreSuffix,
                TestPurchases = source.TestPurchases,
                StartGems = 0,
                StarterConsumables = new Dictionary<string, int>(starter, StringComparer.Ordinal),
                Sections = sections.ToArray(),
                Items = items.ToArray(),
            };
            store.Validate();
            return store;
        }
    }
}
