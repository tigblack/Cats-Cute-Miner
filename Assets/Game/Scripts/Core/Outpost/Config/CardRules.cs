using System;
using System.Collections.Generic;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Правила карточек Cat Miners (Resources/Outpost/card_rules.json): адаптация 61 карточки исходного json-data.json к режиму
    /// CatOutpostV2. Исходные поля карточек (Id, Rarity, MineUnlock, TargetIds, IsManager, модификаторы, CardUpgradeCosts) читаются
    /// из источника; здесь — только новые числа эффектов и сундуков, отдельно от исходного JSON (Docs/Rework/CARD_EFFECT_MAPPING.md).
    /// cards-3 (ТЗ 2.2 §7.1): 39 manager-карточек заводов (manager-1 «Foreman», manager-2 «Manager») дают одинаковый
    /// <see cref="CardEffectKind.FactoryOutput"/>; ca001 остаётся складом базы.
    /// cards-4 (ТЗ 10.6): карточки игры сверх источника — <see cref="ExtraCards"/> (ca063 «Demolition Expert», урон динамита).
    /// </summary>
    [Serializable]
    public sealed class CardRules
    {
        /// <summary>Версия правил в card_rules.json (ТЗ 2.2 §10.1).</summary>
        public const string CurrentVersion = "cards-4";

        public string Version;
        /// <summary>Поток Pcg32 сундуков карточек (отдельно от наград экипировки, поток 5).</summary>
        public ulong CardRngStream = 6;
        /// <summary>Учебная однократная выдача: карточка при первой доставке нового профиля.</summary>
        public string TutorialCard;
        /// <summary>
        /// Эффект по ключу: ID карточки (не manager) либо "manager-1"/"manager-2" (manager по редкости); ключ карточки важнее
        /// общего ключа manager (ca001 — склад базы, а не завод).
        /// </summary>
        public Dictionary<string, CardEffectRule> Effects = new Dictionary<string, CardEffectRule>(StringComparer.Ordinal);
        /// <summary>Шансы редкости одной случайной карты сундука, ppm по кодам 1…6 (сумма 1 000 000), по ID записи Gacha.</summary>
        public Dictionary<string, int[]> ChestChances = new Dictionary<string, int[]>(StringComparer.Ordinal);
        /// <summary>Шансы для записей Gacha без своей строки.</summary>
        public int[] DefaultChances;
        /// <summary>
        /// Карточки игры, которых нет в исходном json-data.json (он закреплён SHA-256): те же поля записи Cards; цены улучшения —
        /// CardUpgradeCosts их редкости, эффект — <see cref="Effects"/> по ID. Каталог добавляет их после карточек источника.
        /// </summary>
        public List<CardDto> ExtraCards = new List<CardDto>();
        /// <summary>Потолок скидок (доля), чтобы цена не обнулялась.</summary>
        public Rational MaxDiscount = Rational.Of(9, 10);
        /// <summary>Множитель силы крита без карточки «Кувалда».</summary>
        public Rational BaseCritMultiplier = Rational.Of(2, 1);

        public const int RarityCount = 6;
        public const int ChanceScale = 1_000_000;

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static CardRules Parse(string json)
        {
            CardRules cfg;
            try
            {
                cfg = OutpostJson.Deserialize<CardRules>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("card_rules: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("card_rules: пустой документ");
            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("card_rules: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (Effects == null || Effects.Count == 0) Fail("нет Effects");
            foreach (var kv in Effects)
            {
                var e = kv.Value;
                if (e == null) Fail("пустой эффект " + kv.Key);
                if (e.Base.Sign < 0 || e.PerLevel.Sign < 0) Fail(kv.Key + ": отрицательные коэффициенты");
                if ((e.Kind == CardEffectKind.KindValue || e.Kind == CardEffectKind.KindDamage) &&
                    e.TargetKind != "r" && e.TargetKind != "x" && e.TargetKind != "s")
                    Fail(kv.Key + ": TargetKind должен быть r, x или s");
                if (e.Kind == CardEffectKind.FactoryOutput && !string.IsNullOrEmpty(e.TargetKind))
                    Fail(kv.Key + ": у FactoryOutput нет TargetKind (тип завода — TargetIds карточки)");
                if (string.IsNullOrEmpty(e.Rule)) Fail(kv.Key + ": нет Rule (LegacyConfirmed / LegacyAssumed / CatMiners)");
            }
            if (ChestChances == null) ChestChances = new Dictionary<string, int[]>(StringComparer.Ordinal);
            CheckChances("DefaultChances", DefaultChances);
            foreach (var kv in ChestChances) CheckChances(kv.Key, kv.Value);
            if (ExtraCards == null) ExtraCards = new List<CardDto>();
            foreach (var c in ExtraCards)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) Fail("ExtraCards: запись без Id");
                if (!Effects.ContainsKey(c.Id)) Fail("ExtraCards." + c.Id + ": нет эффекта в Effects");
                if (c.IsManager) Fail("ExtraCards." + c.Id + ": manager-карточки только из источника");
            }
            if (MaxDiscount.Sign <= 0 || MaxDiscount >= Rational.One) Fail("MaxDiscount вне (0, 1)");
            if (BaseCritMultiplier < Rational.One) Fail("BaseCritMultiplier < 1");

            void CheckChances(string key, int[] p)
            {
                if (p == null || p.Length != RarityCount) Fail(key + ": ожидалось 6 шансов");
                long sum = 0;
                foreach (int v in p)
                {
                    if (v < 0) Fail(key + ": отрицательный шанс");
                    sum += v;
                }
                if (sum != ChanceScale) Fail(key + ": сумма шансов " + sum + " ≠ " + ChanceScale);
            }
        }

        /// <summary>Шансы редкости для записи Gacha.</summary>
        public int[] ChancesFor(string gachaId) =>
            gachaId != null && ChestChances.TryGetValue(gachaId, out var p) ? p : DefaultChances;
    }

    /// <summary>Эффект карточки: значение на уровне L ≥ 1 — Base + PerLevel · (L − 1).</summary>
    [Serializable]
    public sealed class CardEffectRule
    {
        public CardEffectKind Kind;
        /// <summary>Вид цели для KindValue/KindDamage: "r", "x" или "s".</summary>
        public string TargetKind;
        public Rational Base;
        public Rational PerLevel;
        /// <summary>LegacyConfirmed — смысл подтверждён старым кодом; LegacyAssumed — предположение старого ТЗ; CatMiners — новое правило.</summary>
        public string Rule;
        /// <summary>Исходный эффект и обоснование адаптации (для отчёта).</summary>
        public string Source;

        /// <summary>Значение на уровне level (≥ 1); 0 — карточка не получена.</summary>
        public Rational ValueAt(int level) => level < 1 ? Rational.Zero : Base + PerLevel * (level - 1);
    }

    /// <summary>Что меняет карточка (Docs/Rework/CARD_EFFECT_MAPPING.md).</summary>
    public enum CardEffectKind
    {
        /// <summary>
        /// cards-2: +доля к цене новых порций целей с TypeId из TargetIds (месторождения ресурса). В cards-3 не используется
        /// (manager-1 → <see cref="FactoryOutput"/>); оставлен для чтения прежних конфигов и тестов.
        /// </summary>
        TargetValue,
        /// <summary>cards-2: +доля к урону котиков по целям с TypeId из TargetIds. В cards-3 не используется (manager-2 → FactoryOutput).</summary>
        TargetDamage,
        /// <summary>+доля к скорости разгрузки на складе.</summary>
        UnloadRate,
        /// <summary>+доля к цене всех новых порций.</summary>
        GlobalValue,
        /// <summary>Скидка на найм («Команда»).</summary>
        HireDiscount,
        /// <summary>+котиков к пределу «Команды» (целое).</summary>
        TeamCap,
        /// <summary>+котиков в начале каждой новой шахты (целое).</summary>
        StartWorkers,
        /// <summary>Шанс критического удара котика.</summary>
        CritChance,
        /// <summary>+к множителю критического удара.</summary>
        CritPower,
        /// <summary>+доля к урону по воротам.</summary>
        GateDamage,
        /// <summary>+доля к цене порций целей вида TargetKind.</summary>
        KindValue,
        /// <summary>+доля к скорости перемещения.</summary>
        MoveSpeed,
        /// <summary>+доля к урону по целям вида TargetKind.</summary>
        KindDamage,
        /// <summary>+доля к шансам редкостей Rare и выше в сундуках.</summary>
        ChestLuck,
        /// <summary>+мест в сумке (целое).</summary>
        Capacity,
        /// <summary>+доля к скорости подбора с земли.</summary>
        PickupSpeed,
        /// <summary>+доля к урону котиков по всем целям.</summary>
        GlobalDamage,
        /// <summary>Скидка на улучшения базы, кроме «Команды».</summary>
        BranchDiscount,
        /// <summary>Прежний эффект cards-1, оставлен для чтения старых конфигов; предел офлайна больше не увеличивает.</summary>
        OfflineHours,
        /// <summary>+доля к урону удара кирки игрока.</summary>
        TapDamage,
        /// <summary>+доля к эликсиру из сундуков.</summary>
        ChestElixir,
        /// <summary>+случайных карт в каждом сундуке (целое).</summary>
        ChestCards,
        /// <summary>+доля к деталям экипировки за завершение шахты.</summary>
        CompletionParts,
        /// <summary>
        /// ТЗ 2.2 §7.1 (cards-3, manager-1/manager-2): +доля к выпуску товара за цикл заводов TypeId из TargetIds; бонусы Common и
        /// Uncommon карточек типа складываются: Units = floor(UnitsPerCycle × (1 + Σ EffectAt(L))). Считает FactoryMath.UnitsPerCycle;
        /// TeamStats его не копит (CORE_SPEC §16.5).
        /// </summary>
        FactoryOutput,
        /// <summary>
        /// ТЗ 10.6 (cards-4, ca063): +доля к урону взрыва динамита по кольцу — доля максимальной прочности остальных целей в радиусе
        /// = DynamiteRingShare × (1 + Σ EffectAt(L)), не больше остатка; цель у бочки получает DynamiteCenterShare без карточки.
        /// </summary>
        DynamitePower,
    }
}
