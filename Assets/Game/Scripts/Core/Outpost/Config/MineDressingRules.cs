using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CatMiners.Core.Outpost
{
    /// <summary>Тип участка шахты для правил обстановки (<see cref="MineDressingRules.Density"/>, <see cref="DressingFamily.Areas"/>).</summary>
    public enum DressingArea
    {
        /// <summary>Узкий проход на пути от базы к выходу (через все ворота).</summary>
        MainPassage,
        /// <summary>Узкий боковой ход: тупики, обходные галереи, входы в камеры.</summary>
        SidePassage,
        /// <summary>Широкий участок: камера или зал (в окне 5×5 не меньше <see cref="MineDressingRules.ChamberOpenCells"/> клеток пола).</summary>
        Chamber,
    }

    /// <summary>Категория семейства обстановки: геймплей задаёт категория, а не арт.</summary>
    public enum DressingCategory
    {
        /// <summary>Постоянное препятствие на полу: занимает клетки навсегда, котики обходят, не цель добычи.</summary>
        Permanent,
        /// <summary>Постоянная деталь на стене (клетка породы над полом): навигацию не меняет, не цель добычи.</summary>
        Wall,
        /// <summary>Разрушаемый мусор: цель добычи вида <c>x</c> с ярусом прочности, освобождает клетки при разрушении.</summary>
        Destructible,
    }

    /// <summary>
    /// Обстановка шахты (Resources/Outpost/mine_dressing.json, Docs/Rework/MINE_DRESSING.md): каталог семейств постоянных
    /// препятствий, деталей стен и разрушаемого мусора и правила их размещения — плотность по типу участка, исключения у базы,
    /// выхода, ворот и заводов, ярусы прочности. Источник json-data.json, override и топология не меняются: генератор
    /// <see cref="MineDressingPlacer"/> добавляет слой поверх готовой карты новой шахты, а результат целиком хранится в run
    /// (<see cref="OutpostRun.MapDressing"/>) — правка файла не двигает объекты начатой шахты.
    /// </summary>
    [Serializable]
    public sealed class MineDressingRules
    {
        public string Version;
        /// <summary>Первая шахта кампании, где действует обстановка.</summary>
        public int FromZone = 1;
        /// <summary>Порог «широкого» участка: клеток пола (включая занятые целями) в окне 5×5 вокруг клетки.</summary>
        public int ChamberOpenCells = 15;
        /// <summary>Ярусы прочности разрушаемого мусора (Light, Medium, Heavy): множители HP и цены порции к типу <c>x</c>.</summary>
        public Dictionary<string, DressingTier> Tiers = new Dictionary<string, DressingTier>(StringComparer.Ordinal);
        /// <summary>Доля подходящих свободных клеток участка, которую занимает разрушаемый мусор (по имени <see cref="DressingArea"/>).</summary>
        public Dictionary<string, Rational> Density = new Dictionary<string, Rational>(StringComparer.Ordinal);
        /// <summary>Участок (связная группа подходящих клеток сектора) от стольких клеток получает хотя бы одну кучу мусора.</summary>
        public int SectionMinCells = 4;
        /// <summary>Наибольшее число куч в одной группе (куча растёт на соседние клетки).</summary>
        public int ClusterMax = 3;
        /// <summary>Вероятность продолжить группу ещё одной кучей рядом, процент.</summary>
        public int ClusterGrowPercent = 45;
        public DressingPermanentRules Permanent = new DressingPermanentRules();
        public DressingExclusions Exclusions = new DressingExclusions();
        public List<DressingFamily> Families = new List<DressingFamily>();
        /// <summary>Композиции «ориентир + мусор вокруг»: у постоянного препятствия появляются кучи перечисленных семейств.</summary>
        public List<DressingArrangement> Arrangements = new List<DressingArrangement>();

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static MineDressingRules Parse(string json)
        {
            MineDressingRules cfg;
            try
            {
                cfg = OutpostJson.Deserialize<MineDressingRules>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("mine_dressing: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("mine_dressing: пустой документ");
            cfg.Validate();
            return cfg;
        }

        /// <summary>Семейство по ID или null.</summary>
        public DressingFamily Family(string id)
        {
            if (id == null || Families == null) return null;
            for (int i = 0; i < Families.Count; i++)
                if (Families[i] != null && Families[i].Id == id) return Families[i];
            return null;
        }

        /// <summary>Ярус по имени или null.</summary>
        public DressingTier Tier(string name) => name != null && Tiers != null && Tiers.TryGetValue(name, out var t) ? t : null;

        /// <summary>Плотность мусора участка (0 — участок без мусора).</summary>
        public Rational DensityOf(DressingArea area) =>
            Density != null && Density.TryGetValue(area.ToString(), out var d) ? d : Rational.Zero;

        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("mine_dressing: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (FromZone < OutpostIds.FirstZone) Fail("FromZone < " + OutpostIds.FirstZone);
            if (ChamberOpenCells < 1 || ChamberOpenCells > 25) Fail("ChamberOpenCells вне 1..25");
            if (SectionMinCells < 1) Fail("SectionMinCells < 1");
            if (ClusterMax < 1 || ClusterMax > 8) Fail("ClusterMax вне 1..8");
            if (ClusterGrowPercent < 0 || ClusterGrowPercent > 100) Fail("ClusterGrowPercent вне 0..100");
            if (Tiers == null || Tiers.Count == 0) Fail("нет Tiers");
            foreach (var kv in Tiers)
            {
                if (kv.Value == null) Fail("пустой ярус " + kv.Key);
                if (kv.Value.HpFactor.Sign <= 0) Fail("ярус " + kv.Key + ": HpFactor должен быть > 0");
                if (kv.Value.ValueFactor.Sign <= 0) Fail("ярус " + kv.Key + ": ValueFactor должен быть > 0");
            }
            if (Density == null) Fail("нет Density");
            foreach (var kv in Density)
            {
                if (!Enum.TryParse(kv.Key, false, out DressingArea _)) Fail("Density: неизвестный участок " + kv.Key);
                if (kv.Value.Sign < 0 || kv.Value > Rational.One) Fail("Density." + kv.Key + " вне 0..1");
            }
            if (Permanent == null) Fail("нет Permanent");
            Permanent.Validate(Fail);
            if (Exclusions == null) Fail("нет Exclusions");
            Exclusions.Validate(Fail);
            if (Families == null || Families.Count == 0) Fail("нет Families");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Families)
            {
                if (f == null) Fail("пустое семейство");
                if (!IsId(f.Id)) Fail("ID семейства должен быть [a-z][a-z0-9]*: " + f.Id);
                if (!ids.Add(f.Id)) Fail("повтор семейства " + f.Id);
                if (f.Weight < 1) Fail(f.Id + ": Weight < 1");
                if (f.Areas == null || f.Areas.Count == 0) Fail(f.Id + ": нет Areas");
                if (f.Variants == null || f.Variants.Count == 0) Fail(f.Id + ": нет Variants");
                foreach (var v in f.Variants)
                {
                    if (v == null || v.W < 1 || v.W > 2 || v.H < 1 || v.H > 2) Fail(f.Id + ": вариант вне 1..2 × 1..2");
                    if (v.Weight < 1) Fail(f.Id + ": вес варианта < 1");
                    if (f.Category == DressingCategory.Wall && (v.W != 1 || v.H != 1)) Fail(f.Id + ": деталь стены — 1×1");
                }
                if (f.Category == DressingCategory.Destructible)
                {
                    if (Tier(f.Tier) == null) Fail(f.Id + ": нет яруса " + f.Tier);
                }
                else if (!string.IsNullOrEmpty(f.Tier)) Fail(f.Id + ": ярус только у разрушаемого мусора");
            }
            if (Arrangements == null) Arrangements = new List<DressingArrangement>();
            foreach (var a in Arrangements)
            {
                if (a == null) Fail("пустая композиция");
                var anchor = Family(a.Anchor);
                if (anchor == null || anchor.Category != DressingCategory.Permanent) Fail("композиция: ориентир не постоянное препятствие " + a.Anchor);
                if (a.Debris == null || a.Debris.Count == 0) Fail("композиция " + a.Anchor + ": нет Debris");
                foreach (string d in a.Debris)
                {
                    var f = Family(d);
                    if (f == null || f.Category != DressingCategory.Destructible) Fail("композиция " + a.Anchor + ": не мусор " + d);
                }
                if (a.Min < 0 || a.Max < a.Min || a.Max > 4) Fail("композиция " + a.Anchor + ": Min/Max");
            }
        }

        static bool IsId(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] < 'a' || s[0] > 'z') return false;
            foreach (char c in s)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }
    }

    /// <summary>Ярус прочности мусора: HP = HP типа <c>x</c> той же площади × HpFactor, цена порции × ValueFactor.</summary>
    [Serializable]
    public sealed class DressingTier
    {
        public Rational HpFactor = Rational.One;
        public Rational ValueFactor = Rational.One;
    }

    /// <summary>Сколько постоянных препятствий и деталей стен получает шахта.</summary>
    [Serializable]
    public sealed class DressingPermanentRules
    {
        /// <summary>Одно постоянное препятствие на столько подходящих свободных клеток.</summary>
        public int FreeCellsPerProp = 40;
        public int Min = 2;
        public int Max = 8;
        /// <summary>Наименьшее расстояние (Чебышёв) между постоянными препятствиями.</summary>
        public int Spacing = 4;
        /// <summary>Наибольшее удлинение кратчайшего пути от базы до любой клетки пола из-за одного препятствия, шагов.</summary>
        public int MaxDetour = 6;
        /// <summary>Сколько мест-кандидатов оценивается для каждого препятствия (выбирается наименьший обход).</summary>
        public int Candidates = 10;
        /// <summary>Одна деталь стены на столько подходящих клеток стены.</summary>
        public int WallCellsPerProp = 70;
        public int WallMin = 1;
        public int WallMax = 4;

        internal void Validate(Action<string> fail)
        {
            if (FreeCellsPerProp < 1) fail("Permanent.FreeCellsPerProp < 1");
            if (Min < 0 || Max < Min || Max > 32) fail("Permanent: Min/Max");
            if (Spacing < 1) fail("Permanent.Spacing < 1");
            if (MaxDetour < 0) fail("Permanent.MaxDetour < 0");
            if (Candidates < 1 || Candidates > 64) fail("Permanent.Candidates вне 1..64");
            if (WallCellsPerProp < 1) fail("Permanent.WallCellsPerProp < 1");
            if (WallMin < 0 || WallMax < WallMin || WallMax > 16) fail("Permanent: WallMin/WallMax");
        }
    }

    /// <summary>Места, где ничего не ставится: устье у базы, выход, ворота, подходы к заводам.</summary>
    [Serializable]
    public sealed class DressingExclusions
    {
        /// <summary>Клетки не дальше стольких шагов от точки приёма (IntakeCell) остаются пустыми: устье тоннеля над базой.</summary>
        public int BaseClearance = 4;
        /// <summary>Радиус (Чебышёв) свободной зоны вокруг выхода.</summary>
        public int ExitClearance = 1;
        /// <summary>Радиус (Чебышёв) свободной зоны вокруг клеток ворот и их рабочих клеток.</summary>
        public int GateClearance = 1;
        /// <summary>Рабочие клетки заводов и клетка выхода трубы остаются свободными.</summary>
        public bool FactoryClearance = true;

        internal void Validate(Action<string> fail)
        {
            if (BaseClearance < 0) fail("Exclusions.BaseClearance < 0");
            if (ExitClearance < 0) fail("Exclusions.ExitClearance < 0");
            if (GateClearance < 0) fail("Exclusions.GateClearance < 0");
        }
    }

    /// <summary>Семейство обстановки: категория, ярус (у мусора), веса участков и варианты (footprint и вес; номер варианта — номер спрайта).</summary>
    [Serializable]
    public sealed class DressingFamily
    {
        public string Id;
        public DressingCategory Category;
        /// <summary>Ярус прочности мусора (ключ <see cref="MineDressingRules.Tiers"/>); у постоянных — пусто.</summary>
        public string Tier;
        public int Weight = 1;
        public List<DressingArea> Areas = new List<DressingArea>();
        /// <summary>На поверхности можно крепить трубы (опоры, машины).</summary>
        public bool PipeMount;
        /// <summary>Кошачья тема (для отчёта о разнообразии).</summary>
        public bool CatThemed;
        public List<DressingVariant> Variants = new List<DressingVariant>();

        [JsonIgnore] public bool IsDestructible => Category == DressingCategory.Destructible;

        public bool Allows(DressingArea area) => Areas != null && Areas.Contains(area);
    }

    /// <summary>Вариант семейства: footprint W × H клеток (якорь — нижняя левая клетка, как у целей топологии) и вес выбора.</summary>
    [Serializable]
    public sealed class DressingVariant
    {
        public int W = 1;
        public int H = 1;
        public int Weight = 1;
    }

    /// <summary>Композиция: у постоянного препятствия <see cref="Anchor"/> — от Min до Max куч мусора семейств <see cref="Debris"/>.</summary>
    [Serializable]
    public sealed class DressingArrangement
    {
        public string Anchor;
        public List<string> Debris = new List<string>();
        public int Min = 1;
        public int Max = 2;
    }
}
