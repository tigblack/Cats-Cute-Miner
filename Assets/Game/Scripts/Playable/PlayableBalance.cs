using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;

namespace CatMiners.Playable
{
    /// <summary>
    /// Профиль баланса playable «outpost-2.2-b5-playable» — вариант p3 замера темпа (Docs/Playable/PLAYABLE_TZ.md §3.2, §5;
    /// Tools/CoreTests/Drafts/PlayablePacingDraft.cs): 3 котика на старте, золото за порцию ×2, без удорожания по глубине,
    /// тап ½ удара котика, найм ×1,5 за уровень вместо ×1,9, вехи «×2» кирки на уровнях 5 и 10. Накладывается на копию
    /// загруженного профиля b5 (ТЗ 2.2 §14: числа наложения прежние, меняется только версия; заводы и рынок — из общего ядра,
    /// цена товара следует за CoinBase = 10 этого профиля); экипировка, награды и карточки не меняются (CatalogHash тот же). Скорость симуляции не
    /// трогается: она задаёт и анимацию. Магазин урезан (ТЗ §3.4): только сундук за рекламу и платные тайм-скипы —
    /// без пакетов гемов и эликсира и сундуков за гемы, поэтому бесплатные гемы тестового режима не ломают темп отрезка.
    /// Профиль и каталог живут только в этой сборке, не в Resources основной игры.
    /// </summary>
    public static class PlayableBalance
    {
        public const string Version = "outpost-2.2-b5-playable";
        /// <summary>Версия урезанного каталога магазина: «версия основного каталога» + этот суффикс.</summary>
        public const string StoreSuffix = "-playable";

        /// <summary>
        /// Набор конфигов с профилем playable и урезанным магазином вместо загруженных (для <see cref="Runtime.Outpost.OutpostController.ConfigsOverride"/>).
        /// Без правил первой сессии (Docs/Rework/FIRST_SESSION.md): у playable свои квесты, все разделы открыты сразу, спасения нет,
        /// награды шахт — прежняя таблица (<see cref="Rewards"/>: кирка за шахту 1, на ней держатся квесты экипировки шахт 2–4).
        /// </summary>
        public static OutpostConfigSet Apply(OutpostConfigSet configs)
        {
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var set = configs.WithBalance(Overlay(configs.Balance)).WithProgression(null).WithRewards(Rewards(configs.Rewards));
            return configs.Store != null ? set.WithStore(Store(configs.Store)) : set;
        }

        /// <summary>
        /// Таблица наград playable — прежняя, до первой сессии (`rewards-1`): предмет с первой шахты, гарантии 1 — обычная кирка,
        /// 2 — шляпа, 3 — рюкзак, 4 — ботинки, 10/25/50 — кирки как в игре. Остальное — копия <paramref name="source"/>.
        /// </summary>
        public static StageRewardConfig Rewards(StageRewardConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var r = OutpostJson.DeepCopy(source);
            r.Version = "rewards-1";
            r.NoItem = null;
            r.Guaranteed = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "1", "pickaxe.common" }, { "2", "hat.common" }, { "3", "backpack.common" }, { "4", "boots.common" },
                { "10", "pickaxe.rare" }, { "25", "pickaxe.epic" }, { "50", "pickaxe.legendary" },
            };
            return r;
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
        /// Каталог магазина playable из <paramref name="source"/>: сундуки за рекламу и все тайм-скипы
        /// в прежнем порядке разделов; без гемов на старте. Исходный каталог не меняется.
        /// </summary>
        public static StoreCatalog Store(StoreCatalog source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var starter = source.StarterConsumables ?? new Dictionary<string, int>(StringComparer.Ordinal);
            var items = new List<StoreItemDef>();
            foreach (var def in source.Items)
            {
                bool adChest = def.Kind == StoreItemKind.Chest && def.Currency == StoreCurrency.Ad;
                bool timeSkip = def.Kind == StoreItemKind.TimeSkip;
                if (adChest || timeSkip) items.Add(def);
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
