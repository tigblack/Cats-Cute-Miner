using System;
using System.Collections.Generic;
using System.Globalization;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Профиль чисел режима (ТЗ 9, 12): все коэффициенты — точные дроби. Снимок целиком хранится в run
    /// (<see cref="OutpostRun.Balance"/>), поэтому смена профиля не пересобирает текущую шахту.
    /// Исключение — OfflineCapMs: общий предел отсутствия берётся из актуального конфига, не больше трёх реальных часов.
    /// Порядок полей класса — порядок канонической сериализации (hash).
    /// </summary>
    [Serializable]
    public sealed class OutpostBalanceConfig
    {
        public string Version;
        public long TicksPerSecond = OutpostIds.TicksPerSecond;
        public long HitIntervalTicks;
        public long PickupTicksPerUnit;
        /// <summary>Лимит текущей политики офлайна; OfflineJob дополнительно ограничивает его OutpostIds.MaxOfflineMs.</summary>
        public long OfflineCapMs;
        public int StartWorkers;
        public int MaxWorkersPerTarget;
        public long MoveStuckTicks;
        public long WatchdogTicks;
        /// <summary>S(z) = ZoneScale^(z-1) — масштаб денежных значений шахты.</summary>
        public Rational ZoneScale;
        /// <summary>
        /// Удорожание локальных улучшений с глубиной шахты сверх S(z): CostZoneFactor(z) = 1 + CostZoneStep·(z−1)
        /// (Docs/Rework/BALANCE_AUDIT.md). Снимки run до профиля b4 получают 0 — прежние цены.
        /// </summary>
        public Rational CostZoneStep;
        /// <summary>Ветки по порядку <see cref="UpgradeBranch"/>.</summary>
        public List<UpgradeBranchConfig> Branches = new List<UpgradeBranchConfig>();
        public Rational MilestoneMultiplier;
        public Rational BaseDamage;
        public Rational DamageGrowth;
        /// <summary>
        /// Урон удара кирки игрока как доля урона удара котика (ТЗ 6.6, 12.2); 0 выключает удары. Значение по умолчанию
        /// получают снимки run профиля b1, где поля ещё не было.
        /// </summary>
        public Rational TapDamageFactor = Rational.Of(1, 4);
        public Rational BaseMoveSpeed;
        public Rational MoveSpeedStep;
        public int BaseCapacity;
        public int CapacityStep;
        public Rational BaseUnloadRate;
        public Rational UnloadStep;
        public Rational ValueGrowth;
        public Rational HpBase;
        public Rational ZoneHpStep;
        public Rational RankSpan;
        public Rational AreaFactorCap;
        /// <summary>Площадь (клеток), с которой AreaFactor = AreaFactorCap: n ≥ 7 ⇔ sqrt(n) ≥ 2.5.</summary>
        public int AreaCapFromCells;
        public int CargoHpDivisor;
        public int MinCargoUnits;
        public Rational CoinBase;
        /// <summary>Коэффициенты типов целей: "r", "x", "s", "p".</summary>
        public Dictionary<string, TargetTypeConfig> Types = new Dictionary<string, TargetTypeConfig>(StringComparer.Ordinal);

        public UpgradeBranchConfig Branch(UpgradeBranch branch) => Branches[(int)branch];

        public TargetTypeConfig Type(char kind)
        {
            if (Types != null && Types.TryGetValue(kind.ToString(), out var t)) return t;
            throw new OutpostConfigException("outpost_balance: нет коэффициентов типа '" + kind + "'");
        }

        /// <summary>S(z) = ZoneScale^(z-1).</summary>
        public Rational ZoneMoneyScale(int zone) => ZoneScale.Pow(zone - 1);

        /// <summary>Множитель цен локальных улучшений: 1 + CostZoneStep·(z−1).</summary>
        public Rational CostZoneFactor(int zone) => Rational.One + CostZoneStep * Math.Max(0, zone - 1);

        // Кэш S(z)·CostZoneFactor(z) по зоне: точная степень 6/5 до 143 в каждой котировке — основная цена Quote.
        // Приватное поле не сериализуется (hash, снимок run и Clone его не видят); значения неизменяемы.
        readonly Dictionary<int, Rational> m_PriceScale = new Dictionary<int, Rational>();
        Rational m_PriceScaleZoneScale, m_PriceScaleStep;

        /// <summary>S(z)·CostZoneFactor(z) — общий множитель цены любой ветки в зоне z.</summary>
        public Rational PriceScale(int zone)
        {
            lock (m_PriceScale)
            {
                // Тесты меняют поля профиля на месте: кэш действителен только для тех же ZoneScale и CostZoneStep.
                if (m_PriceScaleZoneScale != ZoneScale || m_PriceScaleStep != CostZoneStep)
                {
                    m_PriceScale.Clear();
                    m_PriceScaleZoneScale = ZoneScale;
                    m_PriceScaleStep = CostZoneStep;
                }
                if (!m_PriceScale.TryGetValue(zone, out var v))
                {
                    v = ZoneMoneyScale(zone) * CostZoneFactor(zone);
                    m_PriceScale[zone] = v;
                }
                return v;
            }
        }

        /// <summary>Множитель порогов ветки: MilestoneMultiplier^count(L ≥ t).</summary>
        public Rational Milestone(UpgradeBranch branch, int level)
        {
            var b = Branch(branch);
            int count = 0;
            if (b.Milestones != null)
                foreach (int t in b.Milestones)
                    if (level >= t) count++;
            return MilestoneMultiplier.Pow(count);
        }

        /// <summary>Каноническая строка (Newtonsoft, Formatting.None, порядок полей класса).</summary>
        public string CanonicalJson() => OutpostJson.Serialize(this);

        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public OutpostBalanceConfig Clone() => OutpostJson.DeepCopy(this);

        public static OutpostBalanceConfig Parse(string json)
        {
            OutpostBalanceConfig cfg;
            try
            {
                cfg = OutpostJson.Deserialize<OutpostBalanceConfig>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("outpost_balance: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("outpost_balance: пустой документ");
            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("outpost_balance: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (TicksPerSecond <= 0 || TicksPerSecond % 1000 != 0) Fail("TicksPerSecond должен быть положительным и кратным 1000");
            if (HitIntervalTicks <= 0) Fail("HitIntervalTicks ≤ 0");
            if (PickupTicksPerUnit <= 0) Fail("PickupTicksPerUnit ≤ 0");
            if (OfflineCapMs < 0) Fail("OfflineCapMs < 0");
            if (StartWorkers < 1) Fail("StartWorkers < 1");
            if (MaxWorkersPerTarget < 1) Fail("MaxWorkersPerTarget < 1");
            if (WatchdogTicks <= 0) Fail("WatchdogTicks ≤ 0");
            if (ZoneScale.Sign <= 0) Fail("ZoneScale ≤ 0");
            if (CostZoneStep.Sign < 0) Fail("CostZoneStep < 0");
            if (Branches == null || Branches.Count != OutpostIds.BranchCount) Fail("ожидалось 6 веток");
            for (int i = 0; i < OutpostIds.BranchCount; i++)
            {
                var b = Branches[i];
                string expected = ((UpgradeBranch)i).ToString();
                if (b == null || b.Id != expected) Fail("ветка " + i + ": ожидалась " + expected);
                if (b.BaseCost.Sign <= 0 || b.Growth.Sign <= 0) Fail("ветка " + expected + ": цена/рост ≤ 0");
                if (b.Cap < 0) Fail("ветка " + expected + ": Cap < 0");
                if (b.Milestones == null) b.Milestones = new int[0];
                for (int m = 1; m < b.Milestones.Length; m++)
                    if (b.Milestones[m] <= b.Milestones[m - 1]) Fail("ветка " + expected + ": пороги не возрастают");
            }
            if (MilestoneMultiplier.Sign <= 0) Fail("MilestoneMultiplier ≤ 0");
            if (BaseDamage.Sign <= 0 || DamageGrowth.Sign <= 0) Fail("урон ≤ 0");
            if (TapDamageFactor.Sign < 0) Fail("TapDamageFactor < 0");
            if (BaseMoveSpeed.Sign <= 0 || MoveSpeedStep.Sign < 0) Fail("скорость");
            if (BaseCapacity < 1 || CapacityStep < 0) Fail("вместимость");
            if (BaseUnloadRate.Sign <= 0 || UnloadStep.Sign < 0) Fail("разгрузка");
            if (ValueGrowth.Sign <= 0) Fail("ValueGrowth ≤ 0");
            if (HpBase.Sign <= 0 || ZoneHpStep.Sign < 0 || RankSpan.Sign < 0) Fail("HP");
            if (AreaFactorCap < Rational.One) Fail("AreaFactorCap < 1");
            // n ≥ AreaCapFromCells ⇔ sqrt(n) ≥ AreaFactorCap для целых n: (n-1) < cap² ≤ n.
            var capSq = AreaFactorCap * AreaFactorCap;
            if (!(capSq <= AreaCapFromCells && capSq > AreaCapFromCells - 1)) Fail("AreaCapFromCells не согласован с AreaFactorCap");
            if (CargoHpDivisor < 1 || MinCargoUnits < 1) Fail("груз");
            if (CoinBase.Sign <= 0) Fail("CoinBase ≤ 0");
            foreach (var k in new[] { "r", "x", "s", "p" })
            {
                if (Types == null || !Types.TryGetValue(k, out var t) || t == null) Fail("нет типа " + k);
                else if (t.HpFactor.Sign <= 0 || t.ValueFactor.Sign <= 0) Fail("тип " + k + ": коэффициенты ≤ 0");
            }
            if (!Types["x"].FixedRank.HasValue) Fail("тип x: нужен FixedRank");
        }
    }

    [Serializable]
    public sealed class UpgradeBranchConfig
    {
        // Кэш Growth^L по уровню (котировки идут на каждом событии бота и каждом кадре панели); не сериализуется.
        readonly List<Rational> m_GrowthPow = new List<Rational>();
        Rational m_GrowthPowBase;

        /// <summary>Growth^level с кэшем; кэш сбрасывается, если Growth изменили на месте.</summary>
        public Rational GrowthPow(int level)
        {
            lock (m_GrowthPow)
            {
                if (m_GrowthPowBase != Growth)
                {
                    m_GrowthPow.Clear();
                    m_GrowthPowBase = Growth;
                }
                while (m_GrowthPow.Count <= level)
                    m_GrowthPow.Add(m_GrowthPow.Count == 0 ? Rational.One : m_GrowthPow[m_GrowthPow.Count - 1] * Growth);
                return m_GrowthPow[level];
            }
        }

        public string Id;
        public Rational BaseCost;
        public Rational Growth;
        public int Cap;
        public int[] Milestones = new int[0];
    }

    [Serializable]
    public sealed class TargetTypeConfig
    {
        public Rational HpFactor;
        public Rational ValueFactor;
        /// <summary>Фиксированный rank01 (у x — 1/2); null — ранг по исходному HP.</summary>
        public Rational? FixedRank;
    }

    /// <summary>Ошибка конфигурации режима.</summary>
    public sealed class OutpostConfigException : Exception
    {
        public OutpostConfigException(string message) : base(message) { }
        public OutpostConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Ошибка исходных данных уровня: зона и индекс токена (ТЗ 8.1, 12.4).</summary>
    public sealed class OutpostDataException : Exception
    {
        public string ZoneId { get; }
        public int TokenIndex { get; }

        public OutpostDataException(string zoneId, int tokenIndex, string reason)
            : base((zoneId ?? "?") + (tokenIndex >= 0 ? ":" + tokenIndex.ToString(CultureInfo.InvariantCulture) : "") + ": " + reason)
        {
            ZoneId = zoneId;
            TokenIndex = tokenIndex;
        }
    }
}
