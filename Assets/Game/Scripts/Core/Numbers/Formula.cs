using System;

namespace CatMiners.Core
{
    /// <summary>
    /// Формулы рабочего профиля баланса v1 (ТЗ, Приложение C.1). Это решения владельца [Р],
    /// а не восстановленный исходный алгоритм. Единственное место вычисления FormulaType.
    /// Соглашение: x — порядковый номер с 1; в экспоненциальных формах показатель x − 1.
    /// </summary>
    public static class Formula
    {
        public const int RulesVersion = 1;

        /// <summary>F-0…F-3. power/round используются только FT3.</summary>
        public static GameNumber Evaluate(int formulaType, double b, double m, double g, double x, double power = 0.0, double round = 0.0)
        {
            switch (formulaType)
            {
                case 0: return Ft0(b, m, g, x);
                case 1: return Ft1(b, m, g, x);
                case 2: return Ft2(b, m, g, x);
                case 3: return Ft3Number(b, m, g, power, round, x);
                default: throw new ArgumentOutOfRangeException(nameof(formulaType), formulaType, "Неизвестный FormulaType");
            }
        }

        /// <summary>F-0: B + M·x + G·x².</summary>
        public static GameNumber Ft0(double b, double m, double g, double x) => GameNumber.FromDouble(b + m * x + g * x * x);

        /// <summary>F-1: B + M·G^(x−1).</summary>
        public static GameNumber Ft1(double b, double m, double g, double x)
        {
            var result = GameNumber.FromDouble(b);
            if (m != 0.0) result += GameNumber.FromDouble(m) * GameNumber.Pow(g, x - 1.0);
            return result;
        }

        /// <summary>F-2: B·G^(x−1) + M·(x−1).</summary>
        public static GameNumber Ft2(double b, double m, double g, double x)
        {
            var result = GameNumber.FromDouble(b) * GameNumber.Pow(g, x - 1.0);
            if (m != 0.0) result += GameNumber.FromDouble(m * (x - 1.0));
            return result;
        }

        /// <summary>
        /// F-3 в double: R(B + M·(G·x)^P, |Round|). Только для счётных величин (сдвиг уровня котика доставки)
        /// и диагностики; экономические величины считаются через <see cref="Ft3Number"/>.
        /// </summary>
        public static double Ft3(double b, double m, double g, double p, double round, double x)
        {
            double v = b + m * Math.Pow(g * x, p);
            return RoundToMultiple(v, round);
        }

        /// <summary>
        /// F-3 в GameNumber (ТЗ 15.1): B + M·(G·x)^P, степень — в логарифмической форме без промежуточного double,
        /// который мог бы стать Infinity. F-R применяется, пока значение помещается в double; за его пределами — тождество
        /// (кратность там ниже точности мантиссы). Для значений меньше 1e300 результат совпадает с <see cref="Ft3"/> бит в бит.
        /// </summary>
        public static GameNumber Ft3Number(double b, double m, double g, double p, double round, double x)
        {
            var v = GameNumber.FromDouble(b);
            if (m != 0.0) v += GameNumber.FromDouble(m) * GameNumber.Pow(g * x, p);
            if (round == 0.0 || !v.FitsInDouble) return v;
            return GameNumber.FromDouble(RoundToMultiple(v.ToDouble(), round));
        }

        /// <summary>F-R: к ближайшему кратному |r|, половина от нуля, допуск 1e-9; r = 0 — без округления.</summary>
        public static double RoundToMultiple(double value, double multiple)
        {
            if (multiple == 0.0) return value;
            double r = Math.Abs(multiple);
            double q = value / r;
            double rounded = Math.Sign(q) * Math.Floor(Math.Abs(q) + 0.5 + 1e-9);
            return rounded * r;
        }

        /// <summary>Округление к ближайшему целому, половина вверх (число карт сундука, ТЗ 10.3 п. 4).</summary>
        public static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5 + 1e-9);

        /// <summary>Цена: вверх до целого, не меньше 1.</summary>
        public static GameNumber Price(GameNumber raw)
        {
            var c = raw.CeilingTolerant();
            return c < GameNumber.One ? GameNumber.One : c;
        }

        /// <summary>Выдача валюты: max(1, floor(v)) при положительном v, иначе 0.</summary>
        public static GameNumber Payout(GameNumber raw)
        {
            if (!raw.IsPositive) return GameNumber.Zero;
            var f = raw.FloorTolerant();
            return f < GameNumber.One ? GameNumber.One : f;
        }
    }
}
