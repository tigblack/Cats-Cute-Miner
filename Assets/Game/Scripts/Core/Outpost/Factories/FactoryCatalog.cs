using System;
using System.Collections.Generic;
using System.Globalization;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Каталог заводов (ТЗ 2.2 §3): 20 типов MineShafts исходного json-data.json + товары factory_rules.json + карточки заводов
    /// (Common/Uncommon manager-карточки JSON, <c>TargetIds</c> которых — тип завода). Неизменяем; строится один раз на источник
    /// (<see cref="OutpostConfigSet.WithCards"/> → <see cref="OutpostConfigSet.Factories"/>). Проверки §3.1: тип есть в MineShafts,
    /// footprint 2×3, у GenerationDelaySec* множитель, рост и формула равны 0, CycleSec конфигурации равен
    /// GenerationDelaySecBase, правила есть ровно у всех типов и Goods[i] — товар типа MineShafts[i] (тот же порядок: номер типа),
    /// у каждого типа одна Common-карточка и не больше одной Uncommon.
    /// ca001 (TargetIds spawningcart) — не завод. Требование автодоставки токена <c>s</c> проверяет <see cref="RequirementError"/>.
    /// </summary>
    public sealed class FactoryCatalog
    {
        /// <summary>Типов заводов в исходном JSON (§3.1).</summary>
        public const int ExpectedTypeCount = 20;
        /// <summary>Footprint завода: 2×3 у всех MineShafts.</summary>
        public const int FootprintWidth = 2, FootprintDepth = 3;

        readonly List<FactoryType> m_Types = new List<FactoryType>();
        readonly Dictionary<string, FactoryType> m_ByType = new Dictionary<string, FactoryType>(StringComparer.Ordinal);
        readonly Dictionary<string, FactoryType> m_ByGood = new Dictionary<string, FactoryType>(StringComparer.Ordinal);
        readonly Dictionary<string, FactoryType> m_ByCard = new Dictionary<string, FactoryType>(StringComparer.Ordinal);
        readonly List<string> m_CardIds = new List<string>();

        /// <summary>Правила, из которых построен каталог (их снимок кладёт в run WP1 при создании шахты).</summary>
        public FactoryRules Rules { get; private set; }
        /// <summary>SHA-256 источника.</summary>
        public string SourceSha256 { get; private set; }
        /// <summary>Типы по номеру 1…20 (порядок MineShafts в JSON) — порядок строк рынка (§6.3).</summary>
        public IReadOnlyList<FactoryType> Types => m_Types;
        /// <summary>Все карточки заводов (39): по номеру типа, сначала Common, затем Uncommon.</summary>
        public IReadOnlyList<string> CardIds => m_CardIds;

        public FactoryType Get(string typeId) => typeId != null && m_ByType.TryGetValue(typeId, out var t) ? t : null;
        public bool TryGet(string typeId, out FactoryType type) => m_ByType.TryGetValue(typeId ?? "", out type);
        /// <summary>Тип по ID товара ("milk") или null.</summary>
        public FactoryType ByGood(string goodId) => goodId != null && m_ByGood.TryGetValue(goodId, out var t) ? t : null;
        /// <summary>Тип, к которому относится карточка завода, или null (ca001 и глобальные карточки — не карточки заводов).</summary>
        public FactoryType ByCard(string cardId) => cardId != null && m_ByCard.TryGetValue(cardId, out var t) ? t : null;
        public bool IsFactoryCard(string cardId) => ByCard(cardId) != null;

        /// <summary>
        /// Ошибка требования автодоставки токена <c>s:typeId:level:rewardId:cardId:cardLevel</c> (§3.1) или null: тип есть,
        /// cardId — Common или Uncommon карточка этого типа, 1 ≤ cardLevel ≤ максимум её редкости (Common 14, Uncommon 12).
        /// Вызывает импорт шахты (WP2, SourceLevelImporter) с номером шахты и индексом токена в сообщении.
        /// </summary>
        public string RequirementError(string typeId, string cardId, int cardLevel)
        {
            var type = Get(typeId);
            if (type == null) return "нет завода типа " + (typeId ?? "null") + " в MineShafts";
            int max = type.MaxLevelOf(cardId);
            if (max == 0)
                return "карточка " + (cardId ?? "null") + " не карточка завода " + typeId + " (ожидалась " + type.CommonCardId +
                       (type.UncommonCardId != null ? " или " + type.UncommonCardId : "") + ")";
            if (cardLevel < 1 || cardLevel > max)
                return "уровень карточки " + cardId + " " + cardLevel.ToString(CultureInfo.InvariantCulture) + " вне 1…" +
                       max.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        public static FactoryCatalog Build(BalanceRepository repo, FactoryRules rules)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            rules.Validate();
            void Fail(string m) => throw new OutpostConfigException("factories: " + m);
            var cat = new FactoryCatalog { Rules = rules, SourceSha256 = repo.Sha256 };
            var shafts = repo.Root?.MineShafts;
            if (shafts == null || shafts.Length == 0) Fail("в источнике нет MineShafts");
            if (shafts.Length != ExpectedTypeCount) Fail("MineShafts: " + shafts.Length + " записей, ожидалось " + ExpectedTypeCount);
            if (rules.Goods.Count != shafts.Length)
                Fail("factory_rules: " + rules.Goods.Count + " товаров, а типов MineShafts " + shafts.Length);

            for (int i = 0; i < shafts.Length; i++)
            {
                var s = shafts[i];
                if (s == null || string.IsNullOrEmpty(s.Id)) Fail("MineShafts[" + i + "] без Id");
                string at = "MineShafts." + s.Id;
                if (s.WidthCells != FootprintWidth || s.DepthCells != FootprintDepth)
                    Fail(at + ": footprint " + s.WidthCells + "×" + s.DepthCells + ", ожидался 2×3");
                // CurrencyType, CurrencyOutput*, UpgradeCost* в CatOutpostV2 не используются (Р8); BalanceValidator проверяет их формулы.
                if (s.GenerationDelaySecMultiplier != 0 || s.GenerationDelaySecGrowth != 0 || s.GenerationDelaySecFormulaType != 0)
                    Fail(at + ": GenerationDelaySecMultiplier/Growth/FormulaType должны быть 0");
                double baseSec = s.GenerationDelaySecBase;
                if (!(baseSec >= 1) || baseSec != Math.Floor(baseSec) || baseSec > long.MaxValue / OutpostIds.TicksPerSecond)
                    Fail(at + ": GenerationDelaySecBase " + baseSec.ToString(CultureInfo.InvariantCulture) + " — ожидалось целое ≥ 1");
                var rule = rules.ForType(s.Id);
                if (rule == null) Fail("factory_rules: нет товара для типа " + s.Id);
                // Номер типа — позиция в MineShafts; FactoryRules.NumberOfGood (строки рынка, §6.3) берёт его из позиции в Goods,
                // поэтому порядок Goods обязан совпадать с порядком MineShafts.
                var atIndex = rules.Goods[i];
                if (atIndex != rule)
                    Fail("factory_rules.Goods[" + i + "]: тип " + (atIndex?.TypeId ?? "null") + ", а MineShafts[" + i + "] — " + s.Id +
                         " (порядок Goods должен совпадать с порядком MineShafts: номер типа 1…" + ExpectedTypeCount + ")");
                if (rule.CycleSec != (long)baseSec)
                    Fail("factory_rules." + s.Id + ": CycleSec " + rule.CycleSec + " ≠ MineShafts.GenerationDelaySecBase " + (long)baseSec +
                         " (другое значение — только по решению владельца, ТЗ 2.2 §3.2)");
                if (cat.m_ByType.ContainsKey(s.Id)) Fail(at + ": повтор типа");
                var type = new FactoryType
                {
                    Number = i + 1,
                    TypeId = s.Id,
                    GoodId = rule.GoodId,
                    CycleSec = rule.CycleSec,
                    UnitsPerCycle = rule.UnitsPerCycle,
                    PriceFactor = rule.PriceFactor,
                    Color = rule.Color,
                    WidthCells = s.WidthCells,
                    DepthCells = s.DepthCells,
                    Rule = rule,
                };
                cat.m_Types.Add(type);
                cat.m_ByType.Add(type.TypeId, type);
                cat.m_ByGood.Add(type.GoodId, type);
            }
            foreach (var g in rules.Goods)
                if (!cat.m_ByType.ContainsKey(g.TypeId)) Fail("factory_rules: тип " + g.TypeId + " не найден в MineShafts");

            // Карточки заводов: manager-карточки, TargetIds которых указывают тип завода (ca001 → spawningcart — не завод).
            foreach (var c in repo.CardsInOrder)
            {
                if (c == null || !c.IsManager || c.TargetIds == null) continue;
                FactoryType type = null;
                foreach (var target in c.TargetIds)
                {
                    if (!cat.m_ByType.TryGetValue(target ?? "", out var t)) continue;
                    if (type != null && type != t) Fail("Cards." + c.Id + ": карточка завода указывает несколько типов");
                    type = t;
                }
                if (type == null) continue;
                if (c.TargetIds.Length != 1) Fail("Cards." + c.Id + ": у карточки завода ожидался один TargetId");
                if (!repo.CardUpgradeCosts.TryGetValue(c.Rarity, out var cost) || cost.Duplicates == null)
                    Fail("CardUpgradeCosts: нет редкости " + c.Rarity);
                int maxLevel = cost.Duplicates.Length + 1;
                if (c.Rarity == 1)
                {
                    if (type.CommonCardId != null) Fail(type.TypeId + ": две Common-карточки " + type.CommonCardId + " и " + c.Id);
                    type.CommonCardId = c.Id;
                    type.CommonMaxLevel = maxLevel;
                }
                else if (c.Rarity == 2)
                {
                    if (type.UncommonCardId != null) Fail(type.TypeId + ": две Uncommon-карточки " + type.UncommonCardId + " и " + c.Id);
                    type.UncommonCardId = c.Id;
                    type.UncommonMaxLevel = maxLevel;
                }
                else Fail("Cards." + c.Id + ": карточка завода " + type.TypeId + " редкости " + c.Rarity + " (ожидалась 1 или 2)");
                cat.m_ByCard.Add(c.Id, type);
            }
            foreach (var t in cat.m_Types)
            {
                if (t.CommonCardId == null) Fail(t.TypeId + ": нет Common-карточки завода");
                cat.m_CardIds.Add(t.CommonCardId);
            }
            foreach (var t in cat.m_Types)
                if (t.UncommonCardId != null) cat.m_CardIds.Add(t.UncommonCardId);
            cat.m_CardIds.Sort((a, b) =>
            {
                var ta = cat.m_ByCard[a];
                var tb = cat.m_ByCard[b];
                if (ta.Number != tb.Number) return ta.Number.CompareTo(tb.Number);
                return (a == ta.CommonCardId ? 0 : 1).CompareTo(b == tb.CommonCardId ? 0 : 1);
            });
            return cat;
        }

        /// <summary>
        /// Все токены <c>s</c> исходных зон 1…144 (без override) по возрастанию зоны и индекса: тип, ранг, сундук, требование
        /// автодоставки. 1 232 токена (§3.1). Для тестов, отчётов и валидаторов; карту шахты строит OutpostLevelAdapter.
        /// </summary>
        public static List<FactoryToken> ScanTokens(BalanceRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var list = new List<FactoryToken>();
            for (int z = 1; z <= repo.ZoneCount; z++)
            {
                var zone = SourceLevelImporter.Import(repo, z);
                foreach (var t in zone.Tokens)
                {
                    if (t == null || t.Kind != 's') continue;
                    list.Add(new FactoryToken
                    {
                        Zone = z,
                        Index = t.Index,
                        TypeId = t.TypeId,
                        Level = t.Level,
                        RewardId = t.RewardId,
                        CardId = t.CardId,
                        CardLevel = t.CardLevel,
                    });
                }
            }
            return list;
        }
    }

    /// <summary>Тип завода каталога: товар, цикл, цена, карточки.</summary>
    public sealed class FactoryType
    {
        /// <summary>Номер типа 1…20 (порядок MineShafts в JSON).</summary>
        public int Number;
        /// <summary>ID MineShafts ("amethyst").</summary>
        public string TypeId;
        /// <summary>ID товара ("milk").</summary>
        public string GoodId;
        public long CycleSec;
        public int UnitsPerCycle;
        public Rational PriceFactor;
        /// <summary>Тон палитры трубы ("cream4").</summary>
        public string Color;
        public int WidthCells, DepthCells;
        /// <summary>Common-карточка («… Foreman», Rarity 1), всегда есть.</summary>
        public string CommonCardId;
        /// <summary>Uncommon-карточка («… Manager», Rarity 2) или null (у sunstone её нет).</summary>
        public string UncommonCardId;
        /// <summary>Максимальные уровни карточек по CardUpgradeCosts (Common 14, Uncommon 12; 0 — карточки нет).</summary>
        public int CommonMaxLevel, UncommonMaxLevel;
        /// <summary>Строка factory_rules.json этого типа.</summary>
        public FactoryGoodRule Rule;

        /// <summary>Длительность цикла в тиках (CycleSec × 10^6).</summary>
        public long CycleTicks => checked(CycleSec * OutpostIds.TicksPerSecond);

        public bool HasCard(string cardId) => cardId != null && (cardId == CommonCardId || cardId == UncommonCardId);

        /// <summary>Максимальный уровень карточки этого типа; 0 — карточка не относится к типу.</summary>
        public int MaxLevelOf(string cardId) =>
            cardId == null ? 0 : cardId == CommonCardId ? CommonMaxLevel : cardId == UncommonCardId ? UncommonMaxLevel : 0;
    }

    /// <summary>Токен <c>s:typeId:level:rewardId:cardId:cardLevel</c> исходной зоны (<see cref="FactoryCatalog.ScanTokens"/>).</summary>
    public sealed class FactoryToken
    {
        public int Zone;
        /// <summary>Индекс якоря в исходной сетке = ID цели.</summary>
        public int Index;
        public string TypeId;
        /// <summary>Ранг HP цепей (ТЗ 2.1 §12.4).</summary>
        public int Level;
        public string RewardId;
        /// <summary>Требование автодоставки: карточка и её уровень.</summary>
        public string CardId;
        public int CardLevel;
    }
}
