using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CatMiners.Core.Outpost
{
    /// <summary>Что даёт плитка магазина (ТЗ 10.5).</summary>
    public enum StoreItemKind
    {
        /// <summary>Сундук карточек записи Gacha (<see cref="StoreItemDef.ChestId"/>): карточки и эликсир.</summary>
        Chest,
        /// <summary>Тайм-скип: <see cref="StoreItemDef.Minutes"/> минут работы шахты сразу; хранится в профиле как расходник.</summary>
        TimeSkip,
        /// <summary>Эликсир (soft-валюта карточек).</summary>
        Elixir,
        /// <summary>Гемы (hard-валюта).</summary>
        Gems,
    }

    /// <summary>Чем платит игрок.</summary>
    public enum StoreCurrency
    {
        Gems,
        /// <summary>Просмотр рекламы; затем плитка ждёт <see cref="StoreItemDef.CooldownMinutes"/>.</summary>
        Ad,
        /// <summary>Реальные деньги (<see cref="StoreItemDef.PriceUsd"/>).</summary>
        Real,
    }

    /// <summary>Плитка магазина: раздел, содержимое и цена.</summary>
    [Serializable]
    public sealed class StoreItemDef
    {
        public string Id;
        public string Section;
        public StoreItemKind Kind;
        /// <summary>Запись Gacha исходника (сундук).</summary>
        public string ChestId;
        /// <summary>
        /// Длительность тайм-скипа, минуты. Шахта рассчитана на 4–12 минут игры, поэтому скипы — 10/30/60 минут (13 октября 2026);
        /// ID плиток skip_1h/skip_4h/skip_12h остались прежними: по ним хранятся расходники в сохранениях.
        /// </summary>
        public int Minutes;
        /// <summary>Эликсир или гемы обычной покупки.</summary>
        public long Amount;
        /// <summary>Гемы первой покупки (0 — бонуса нет); надпись «+BonusPercent%».</summary>
        public long BonusAmount;
        public int BonusPercent;
        public StoreCurrency Currency;
        /// <summary>Цена в гемах (Currency = Gems).</summary>
        public long Price;
        /// <summary>Цена в долларах для надписи (Currency = Real), например "1.99".</summary>
        public string PriceUsd;
        /// <summary>Пауза после рекламной покупки, минуты (Currency = Ad).</summary>
        public int CooldownMinutes;

        [JsonIgnore] public bool HasBonus => BonusAmount > 0;
        [JsonIgnore] public long CooldownMs => CooldownMinutes * 60_000L;
    }

    /// <summary>
    /// Каталог магазина (ТЗ 10.5, Resources/Outpost/store_catalog.json): разделы по порядку экрана, плитки, стартовый набор и
    /// режим тестовых покупок. Шансы сундуков — не здесь, а в card_rules.json → ChestChances (единый источник для всех сундуков).
    /// </summary>
    [Serializable]
    public sealed class StoreCatalog
    {
        public string Version;
        /// <summary>
        /// Реклама и платежи ещё не подключены: true — награда выдаётся сразу, как после просмотра или оплаты; false — плитки
        /// за рекламу и деньги недоступны («скоро появится»).
        /// </summary>
        public bool TestPurchases;
        /// <summary>Гемы, выдаваемые один раз при подключении магазина к профилю.</summary>
        public long StartGems;
        /// <summary>Расходники, выдаваемые один раз при подключении магазина к профилю: ID плитки тайм-скипа → количество.</summary>
        public Dictionary<string, int> StarterConsumables = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Разделы экрана сверху вниз: offers, timeskip, elixir, gems.</summary>
        public string[] Sections;
        public StoreItemDef[] Items;
        /// <summary>Бонус за рекламу у левого края экрана и мортира с динамитом (ТЗ 10.6); null — бонуса нет.</summary>
        public RewardedBonusRules RewardedBonus;

        Dictionary<string, StoreItemDef> m_ById;
        Dictionary<string, List<StoreItemDef>> m_BySection;

        public bool TryGet(string itemId, out StoreItemDef def)
        {
            EnsureIndex();
            def = null;
            return itemId != null && m_ById.TryGetValue(itemId, out def);
        }

        /// <summary>Плитки раздела в порядке каталога (пустой список — нет раздела).</summary>
        public IReadOnlyList<StoreItemDef> InSection(string section)
        {
            EnsureIndex();
            return section != null && m_BySection.TryGetValue(section, out var list) ? list : (IReadOnlyList<StoreItemDef>)Array.Empty<StoreItemDef>();
        }

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static StoreCatalog Parse(string json)
        {
            StoreCatalog cat;
            try
            {
                cat = OutpostJson.Deserialize<StoreCatalog>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("store_catalog: ошибка разбора: " + e.Message, e);
            }
            if (cat == null) throw new OutpostConfigException("store_catalog: пустой документ");
            cat.Validate();
            return cat;
        }

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("store_catalog: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (StartGems < 0) Fail("StartGems < 0");
            if (Sections == null || Sections.Length == 0) Fail("нет разделов");
            var sections = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in Sections)
                if (string.IsNullOrEmpty(s) || !sections.Add(s)) Fail("пустой или повторный раздел " + s);
            if (Items == null || Items.Length == 0) Fail("нет плиток");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in Items)
            {
                if (d == null || string.IsNullOrEmpty(d.Id) || !ids.Add(d.Id)) Fail("пустой или повторный Id " + d?.Id);
                string at = d.Id + ": ";
                if (!sections.Contains(d.Section ?? "")) Fail(at + "неизвестный раздел " + d.Section);
                switch (d.Kind)
                {
                    case StoreItemKind.Chest:
                        if (string.IsNullOrEmpty(d.ChestId)) Fail(at + "нет ChestId");
                        break;
                    case StoreItemKind.TimeSkip:
                        if (d.Minutes < 1) Fail(at + "Minutes < 1");
                        break;
                    default:
                        if (d.Amount <= 0) Fail(at + "Amount ≤ 0");
                        break;
                }
                if (d.BonusAmount != 0 && (d.Kind != StoreItemKind.Gems && d.Kind != StoreItemKind.Elixir || d.BonusAmount <= d.Amount))
                    Fail(at + "BonusAmount должен быть больше Amount (гемы или эликсир)");
                if (d.BonusPercent < 0) Fail(at + "BonusPercent < 0");
                switch (d.Currency)
                {
                    case StoreCurrency.Gems:
                        if (d.Price <= 0) Fail(at + "Price ≤ 0");
                        if (d.Kind == StoreItemKind.Gems) Fail(at + "гемы за гемы");
                        break;
                    case StoreCurrency.Ad:
                        if (d.CooldownMinutes < 0) Fail(at + "CooldownMinutes < 0");
                        break;
                    case StoreCurrency.Real:
                        if (string.IsNullOrEmpty(d.PriceUsd)) Fail(at + "нет PriceUsd");
                        break;
                }
            }
            if (StarterConsumables == null) StarterConsumables = new Dictionary<string, int>(StringComparer.Ordinal);
            m_ById = null;
            EnsureIndex();
            foreach (var kv in StarterConsumables)
            {
                if (!m_ById.TryGetValue(kv.Key, out var d) || d.Kind != StoreItemKind.TimeSkip) Fail("StarterConsumables: " + kv.Key + " — не тайм-скип");
                if (kv.Value < 0) Fail("StarterConsumables[" + kv.Key + "] < 0");
            }
            RewardedBonus?.Validate(Fail);
        }

        /// <summary>Каждый сундук магазина есть в каталоге карточек (вызывается, когда каталог подключён).</summary>
        public void ValidateChests(CardCatalog cards)
        {
            if (cards == null) return;
            foreach (var d in Items)
                if (d.Kind == StoreItemKind.Chest && cards.Chest(d.ChestId) == null)
                    throw new OutpostConfigException("store_catalog: " + d.Id + ": нет сундука " + d.ChestId + " в Gacha");
        }

        void EnsureIndex()
        {
            if (m_ById != null) return;
            var byId = new Dictionary<string, StoreItemDef>(StringComparer.Ordinal);
            var bySection = new Dictionary<string, List<StoreItemDef>>(StringComparer.Ordinal);
            foreach (var d in Items ?? Array.Empty<StoreItemDef>())
            {
                if (d?.Id == null) continue;
                byId[d.Id] = d;
                if (!bySection.TryGetValue(d.Section ?? "", out var list)) bySection[d.Section ?? ""] = list = new List<StoreItemDef>();
                list.Add(d);
            }
            m_BySection = bySection;
            m_ById = byId;
        }
    }

    /// <summary>
    /// Бонус за рекламу (ТЗ 10.6): иконка у левого края над нижней панелью появляется через <see cref="FirstDelaySec"/> активной
    /// игры, затем через <see cref="IntervalSec"/> после просмотра или пропуска и висит <see cref="VisibleSec"/>. Вид награды —
    /// взвешенный выбор <see cref="Weights"/> среди доступных сейчас; суммы — по ценам игры (<see cref="RewardedBonus"/>).
    /// Динамит: мортира лагеря выстреливает бочку к доступным целям сектора, тап по бочке — взрыв.
    /// </summary>
    [Serializable]
    public sealed class RewardedBonusRules
    {
        public int FirstDelaySec = 120;
        public int IntervalSec = 180;
        public int VisibleSec = 45;
        /// <summary>Веса видов по именам <see cref="RewardedBonusKind"/> (Gold, Elixir, Gems, Dynamite); 0 — вид не выпадает.</summary>
        public Dictionary<string, int> Weights = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Золото — на столько самых дешёвых покупок улучшений базы по одному уровню (одна ветка может войти дважды).</summary>
        public int GoldUpgrades = 2;
        /// <summary>Эликсир — на столько самых дешёвых улучшений своих карточек ниже максимального уровня.</summary>
        public int ElixirUpgrades = 1;
        /// <summary>Эликсир, когда улучшать нечего (нет карточек или все на максимуме).</summary>
        public long ElixirFloor = 50;
        public long Gems = 2;
        /// <summary>Радиус взрыва в клетках: от центра клетки бочки до центра ближайшей клетки цели.</summary>
        public Rational DynamiteRadius = Rational.Of(3, 2);
        /// <summary>Доля максимальной прочности цели, у которой лежит бочка (не больше остатка).</summary>
        public Rational DynamiteCenterShare = Rational.One;
        /// <summary>Доля максимальной прочности остальных доступных целей в радиусе; умножается на 1 + карточка DynamitePower.</summary>
        public Rational DynamiteRingShare = Rational.Of(1, 2);
        /// <summary>Динамит выпадает, если на поле нет бочки и в секторе не меньше стольких целей, доступных кирке.</summary>
        public int DynamiteMinTargets = 3;

        public int WeightOf(RewardedBonusKind kind) =>
            Weights != null && Weights.TryGetValue(kind.ToString(), out int w) ? w : 0;

        internal void Validate(Action<string> fail)
        {
            if (FirstDelaySec < 0 || IntervalSec < 1 || VisibleSec < 1) fail("RewardedBonus: тайминги (FirstDelaySec ≥ 0, IntervalSec ≥ 1, VisibleSec ≥ 1)");
            if (Weights == null || Weights.Count == 0) fail("RewardedBonus: нет Weights");
            int sum = 0;
            foreach (var kv in Weights)
            {
                if (!Enum.TryParse<RewardedBonusKind>(kv.Key, false, out _)) fail("RewardedBonus.Weights: неизвестный вид " + kv.Key);
                if (kv.Value < 0) fail("RewardedBonus.Weights[" + kv.Key + "] < 0");
                sum += kv.Value;
            }
            if (sum <= 0) fail("RewardedBonus.Weights: сумма весов 0");
            if (GoldUpgrades < 1 || ElixirUpgrades < 1) fail("RewardedBonus: GoldUpgrades и ElixirUpgrades ≥ 1");
            if (ElixirFloor < 1 || Gems < 1) fail("RewardedBonus: ElixirFloor и Gems ≥ 1");
            if (DynamiteRadius.Sign <= 0) fail("RewardedBonus: DynamiteRadius ≤ 0");
            if (DynamiteCenterShare.Sign <= 0 || DynamiteRingShare.Sign < 0) fail("RewardedBonus: доли урона динамита");
            if (DynamiteMinTargets < 1) fail("RewardedBonus: DynamiteMinTargets < 1");
        }
    }
}
