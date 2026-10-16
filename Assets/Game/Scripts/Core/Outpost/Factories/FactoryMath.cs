using System;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Формулы заводов ТЗ 2.2 (§4.4, §5, §6.4) — чистые функции без состояния, точная арифметика (BigInteger/Rational).
    /// Ими пользуются ядро (WP1), UI (WP5), бот и аудит (WP7).
    /// </summary>
    public static class FactoryMath
    {
        /// <summary>
        /// <b>Единственная функция правила выпуска (§4.4, §7.1)</b>: единиц товара за цикл завода типа <c>rule.TypeId</c> для профиля
        /// сейчас — <c>floor(UnitsPerCycle × (1 + Σ Output(L)))</c>. <c>UnitsPerCycle</c> — строка снимка правил шахты
        /// (<see cref="OutpostRun.FactoryRules"/>), Σ Output(L) — <see cref="CardService.FactoryOutputBonus"/>: эффекты
        /// <see cref="CardEffectKind.FactoryOutput"/> Common и Uncommon карточек типа по текущим уровням профиля (уровень 0 — 0;
        /// cards = null — без бонуса). Ядро фиксирует этим выпуск в конце цикла (по уровням на этой отметке, даже если
        /// <see cref="TeamStats"/> ещё не пересчитан фазой 7); сведения завода, бот, аудит и UI читают его же — напрямую или через
        /// <c>OutpostSimulation.FactoryUnitsPerCycle(targetId)</c>. Свои копии формулы не заводить. rule = null — 0.
        /// </summary>
        public static int UnitsPerCycle(FactoryGoodRule rule, OutpostProfile profile, CardCatalog cards) =>
            rule == null ? 0 : Units(rule.UnitsPerCycle, CardService.FactoryOutputBonus(profile, cards, rule.TypeId));

        /// <summary>
        /// Арифметика правила выпуска: floor(unitsPerCycle × (1 + bonus)) для уже посчитанного бонуса (контрольные значения
        /// приложения B, таблицы). Выпуск завода для профиля — только <see cref="UnitsPerCycle"/>. Отрицательный бонус не допускается.
        /// </summary>
        public static int Units(int unitsPerCycle, Rational bonus)
        {
            if (unitsPerCycle < 0) throw new ArgumentOutOfRangeException(nameof(unitsPerCycle));
            if (bonus.Sign < 0) throw new ArgumentOutOfRangeException(nameof(bonus), "бонус выпуска < 0");
            return ExactMath.ToIntChecked((Rational.FromLong(unitsPerCycle) * (Rational.One + bonus)).Floor(), "выпуск завода");
        }

        /// <summary>Цена единицы товара: ceil(CoinBase × S(z) × PriceFactor), S(z) = ZoneScale^(z−1) снимка баланса шахты (§6.4).</summary>
        public static BigInteger Price(OutpostBalanceConfig balance, int zone, Rational priceFactor)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            return (balance.CoinBase * balance.ZoneMoneyScale(zone) * priceFactor).Ceil();
        }

        /// <summary>
        /// Время в пути, тики: max(MinTransitSec, ceil(RouteSteps / PipeSpeedCellsPerSec)) × 10^6 (§5); результат округляется вверх
        /// до целого тика. routeSteps &lt; 0 (маршрута нет — топология до wide-branching-mines-3) считается как 0 шагов.
        /// </summary>
        public static long TransitTicks(FactoryRules rules, int routeSteps)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            Rational steps = Math.Max(0, routeSteps);
            Rational sec = Rational.Max(rules.MinTransitSec, new Rational((steps / rules.PipeSpeedCellsPerSec).Ceil()));
            return ExactMath.ToLongChecked((sec * OutpostIds.TicksPerSecond).Ceil(), "время в пути");
        }
    }

    /// <summary>
    /// Чтение заводов, склада и цен из состояния шахты (ТЗ 2.2 §6, §10.2) без изменений и выделений сверх BigInteger-арифметики.
    /// Общие для ядра (причины отказа команд), контроллера и UI.
    /// </summary>
    public static class FactoryQueries
    {
        /// <summary>Завод по ID цели или null.</summary>
        public static FactoryState Find(OutpostRun run, int targetId)
        {
            var list = run?.Factories;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].TargetId == targetId) return list[i];
            return null;
        }

        /// <summary>В шахте есть завод с товаром goodId.</summary>
        public static bool HasGood(OutpostRun run, string goodId)
        {
            var list = run?.Factories;
            if (list == null || goodId == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].GoodId == goodId) return true;
            return false;
        }

        /// <summary>Остаток товара на складе (0 — нет).</summary>
        public static long Stock(OutpostRun run, string goodId) =>
            goodId != null && run?.Warehouse != null && run.Warehouse.TryGetValue(goodId, out long n) ? n : 0;

        /// <summary>На складе есть хоть одна единица товара (красная точка кнопки Market, §6.2).</summary>
        public static bool AnyStock(OutpostRun run)
        {
            if (run?.Warehouse == null) return false;
            foreach (var kv in run.Warehouse)
                if (kv.Value > 0) return true;
            return false;
        }

        /// <summary>Цена единицы товара в шахте run (снимки Balance и FactoryRules); 0 — снимка или товара нет.</summary>
        public static BigInteger Price(OutpostRun run, string goodId)
        {
            var rule = run?.FactoryRules?.ForGood(goodId);
            if (rule == null || run.Balance == null) return BigInteger.Zero;
            return FactoryMath.Price(run.Balance, run.Zone, rule.PriceFactor);
        }

        /// <summary>Стоимость остатка: goodId — одного товара, null — всего склада.</summary>
        public static BigInteger StockValue(OutpostRun run, string goodId = null)
        {
            if (run?.Warehouse == null) return BigInteger.Zero;
            if (goodId != null) return Stock(run, goodId) * Price(run, goodId);
            var sum = BigInteger.Zero;
            foreach (var kv in run.Warehouse)
                if (kv.Value > 0) sum += kv.Value * Price(run, kv.Key);
            return sum;
        }

        /// <summary>Причина отказа «Deliver»: "phase", "unknown", "locked", "not-ready"; null — можно.</summary>
        public static string DeliverBlockReason(OutpostRun run, int targetId)
        {
            if (run == null) return "unknown";
            if (run.Phase == RunPhase.ReadyToContinue) return "phase";
            var f = Find(run, targetId);
            if (f == null) return "unknown";
            if (f.Status == FactoryStatus.Locked) return "locked";
            if (f.Status != FactoryStatus.Ready || f.ReadyUnits <= 0) return "not-ready";
            return null;
        }

        /// <summary>
        /// Причина отказа продажи goodId (null — «Sell all»): "no-factories", "phase", "unknown", "no-goods"; null — можно.
        /// Кнопка Market неактивна при "no-factories" и "phase"; при "no-goods" окно открывается с неактивными кнопками.
        /// </summary>
        public static string SellBlockReason(OutpostRun run, string goodId)
        {
            if (run?.Factories == null || run.Factories.Count == 0) return "no-factories";
            if (run.Phase == RunPhase.ReadyToContinue) return "phase";
            if (goodId != null)
            {
                if (!HasGood(run, goodId)) return "unknown";
                return Stock(run, goodId) > 0 ? null : "no-goods";
            }
            return AnyStock(run) ? null : "no-goods";
        }
    }
}
