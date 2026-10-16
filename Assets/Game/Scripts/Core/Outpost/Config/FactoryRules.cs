using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Правила заводов (ТЗ 2.2 §3.2, Resources/Outpost/factory_rules.json, версия factories-1; генератор
    /// Tools/Factories/gen_factory_rules.py). Товар каждого из 20 типов MineShafts: длительность цикла из JSON, выпуск без
    /// карточек, множитель цены, цвет трубы; скорость труб. Снимок целиком хранится в <see cref="OutpostRun.FactoryRules"/> при
    /// создании шахты — правка конфигурации не меняет идущую шахту (как профиль баланса). Сверка с MineShafts и карточками —
    /// <see cref="FactoryCatalog.Build"/>.
    /// </summary>
    [Serializable]
    public sealed class FactoryRules
    {
        public string Version;
        /// <summary>Ориентир цены: стартовое правило PriceFactor = PriceRate × CycleSec / UnitsPerCycle (§3.2, калибровка §9). Ядро его не читает.</summary>
        public Rational PriceRate;
        /// <summary>Скорость партии в трубе, клеток маршрута в секунду (§5).</summary>
        public Rational PipeSpeedCellsPerSec;
        /// <summary>Наименьшее время в пути, секунды (§5).</summary>
        public Rational MinTransitSec;
        /// <summary>Товары по номеру типа: Goods[i] — товар типа MineShafts[i] (порядок JSON; проверяет FactoryCatalog.Build).</summary>
        public List<FactoryGoodRule> Goods = new List<FactoryGoodRule>();

        /// <summary>Версия конфигурации ТЗ 2.2 §10.1.</summary>
        public const string CurrentVersion = "factories-1";

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static FactoryRules Parse(string json)
        {
            FactoryRules cfg;
            try
            {
                cfg = OutpostJson.Deserialize<FactoryRules>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("factory_rules: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("factory_rules: пустой документ");
            cfg.Validate();
            return cfg;
        }

        /// <summary>Правило товара по типу завода (ID MineShafts, например "amethyst") или null. Линейный поиск: ≤ 20 записей, без выделений.</summary>
        public FactoryGoodRule ForType(string typeId)
        {
            if (typeId == null || Goods == null) return null;
            for (int i = 0; i < Goods.Count; i++)
                if (Goods[i] != null && Goods[i].TypeId == typeId) return Goods[i];
            return null;
        }

        /// <summary>Правило по ID товара ("milk") или null.</summary>
        public FactoryGoodRule ForGood(string goodId)
        {
            if (goodId == null || Goods == null) return null;
            for (int i = 0; i < Goods.Count; i++)
                if (Goods[i] != null && Goods[i].GoodId == goodId) return Goods[i];
            return null;
        }

        /// <summary>Номер типа 1…N (порядок <see cref="Goods"/>) по ID товара; 0 — нет такого товара. Порядок строк рынка (§6.3).</summary>
        public int NumberOfGood(string goodId)
        {
            if (goodId == null || Goods == null) return 0;
            for (int i = 0; i < Goods.Count; i++)
                if (Goods[i] != null && Goods[i].GoodId == goodId) return i + 1;
            return 0;
        }

        /// <summary>Проверка формы (без исходного JSON): поля, дроби, уникальность, имя тона. Сверку с MineShafts делает <see cref="FactoryCatalog.Build"/>.</summary>
        public void Validate()
        {
            void Fail(string m) => throw new OutpostConfigException("factory_rules: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (PriceRate.Sign <= 0) Fail("PriceRate должен быть > 0");
            if (PipeSpeedCellsPerSec.Sign <= 0) Fail("PipeSpeedCellsPerSec должен быть > 0");
            if (MinTransitSec.Sign < 0) Fail("MinTransitSec < 0");
            if (Goods == null || Goods.Count == 0) Fail("нет Goods");
            var types = new HashSet<string>(StringComparer.Ordinal);
            var goods = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Goods.Count; i++)
            {
                var g = Goods[i];
                string at = "Goods[" + i + "]";
                if (g == null) Fail(at + ": пустая запись");
                if (string.IsNullOrEmpty(g.TypeId)) Fail(at + ": нет TypeId");
                at += " (" + g.TypeId + ")";
                if (!IsId(g.GoodId)) Fail(at + ": GoodId должен быть [a-z][a-z0-9_]*");
                if (!types.Add(g.TypeId)) Fail(at + ": повтор TypeId");
                if (!goods.Add(g.GoodId)) Fail(at + ": повтор GoodId " + g.GoodId);
                if (g.CycleSec < 1) Fail(at + ": CycleSec < 1");
                if (g.CycleSec > long.MaxValue / OutpostIds.TicksPerSecond) Fail(at + ": CycleSec слишком велик");
                if (g.UnitsPerCycle < 1) Fail(at + ": UnitsPerCycle < 1");
                if (g.PriceFactor.Sign <= 0) Fail(at + ": PriceFactor должен быть > 0");
                if (!TrySplitTone(g.Color, out _, out _)) Fail(at + ": Color должен быть тоном палитры <рампа><индекс>, например cream4");
            }
        }

        /// <summary>
        /// Имя тона палитры <c>&lt;рампа&gt;&lt;индекс&gt;</c> ("fur_cream2" → "fur_cream", 2), как ключи PALETTE в Tools/PixelArt/pxl.py.
        /// Существование рампы и предел индекса проверяют генератор (по RAMPS pxl.py) и тест FactoryCatalogTests; в Runtime —
        /// <c>PixelPalette.ByName</c> (WP4).
        /// </summary>
        public static bool TrySplitTone(string color, out string ramp, out int index)
        {
            ramp = null;
            index = -1;
            if (string.IsNullOrEmpty(color)) return false;
            int i = color.Length;
            while (i > 0 && color[i - 1] >= '0' && color[i - 1] <= '9') i--;
            if (i == 0 || i == color.Length || color.Length - i > 3) return false;
            for (int k = 0; k < i; k++)
            {
                char c = color[k];
                if (!(c >= 'a' && c <= 'z') && c != '_') return false;
            }
            ramp = color.Substring(0, i);
            index = int.Parse(color.Substring(i), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        static bool IsId(string id)
        {
            if (string.IsNullOrEmpty(id) || !(id[0] >= 'a' && id[0] <= 'z')) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }
    }

    /// <summary>Товар одного типа завода (строка Goods factory_rules.json).</summary>
    [Serializable]
    public sealed class FactoryGoodRule
    {
        /// <summary>Тип JSON (ID MineShafts): ключ данных и сохранений, игрок его не видит.</summary>
        public string TypeId;
        /// <summary>ID товара (приложение A): milk, wood, … caviar. Ключ склада, Loc и спрайтов goods_&lt;good&gt;.</summary>
        public string GoodId;
        /// <summary>Длительность цикла, секунды: копия MineShafts.GenerationDelaySecBase (Р1).</summary>
        public long CycleSec;
        /// <summary>Выпуск за цикл без карточек.</summary>
        public int UnitsPerCycle;
        /// <summary>Безразмерный множитель цены единицы: Price = ceil(CoinBase × S(z) × PriceFactor) (§6.4).</summary>
        public Rational PriceFactor;
        /// <summary>Тон палитры полосы трубы и капсулы, например "cream4".</summary>
        public string Color;

        /// <summary>Длительность цикла в тиках: CycleSec × 10^6 (§4.4), без бонусов.</summary>
        [JsonIgnore] public long CycleTicks => checked(CycleSec * OutpostIds.TicksPerSecond);
    }
}
