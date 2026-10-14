using System;
using System.Globalization;

namespace CatMiners.Core
{
    /// <summary>
    /// Экономическое число (ТЗ 15.1). Внутри — гибрид:
    /// значения по модулю меньше 1e300 хранятся как обычный double (целые точны, как в double),
    /// бо́льшие — как нормализованная мантисса double × 10^показатель (long).
    /// Цены улучшений поздних шахт доходят до ~1e352 и в double не помещаются.
    /// Целочисленной точности больших значений не обещает.
    /// </summary>
    public readonly struct GameNumber : IComparable<GameNumber>, IEquatable<GameNumber>
    {
        const double SmallLimit = 1e300;
        const long SmallLimitExponent = 300;
        const int DoubleMinExp = -323;
        const int DoubleMaxExp = 308;

        // m_Big == false: значение = m_V. m_Big == true: значение = m_V × 10^m_E, 1 ≤ |m_V| < 10.
        readonly double m_V;
        readonly long m_E;
        readonly bool m_Big;

        public static readonly GameNumber Zero = new GameNumber(0.0, 0L, false);
        public static readonly GameNumber One = new GameNumber(1.0, 0L, false);

        static readonly double[] s_Pow10 = BuildPow10Table();

        static double[] BuildPow10Table()
        {
            var table = new double[DoubleMaxExp - DoubleMinExp + 1];
            for (int e = DoubleMinExp; e <= DoubleMaxExp; e++)
                table[e - DoubleMinExp] = double.Parse("1e" + e.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return table;
        }

        static double Pow10(long e)
        {
            if (e < DoubleMinExp) return 0.0;
            if (e > DoubleMaxExp) return double.PositiveInfinity;
            return s_Pow10[e - DoubleMinExp];
        }

        GameNumber(double v, long e, bool big)
        {
            m_V = v;
            m_E = e;
            m_Big = big;
        }

        /// <summary>Создание из мантиссы и показателя с нормализацией.</summary>
        public GameNumber(double mantissa, long exponent)
        {
            this = FromParts(mantissa, exponent);
        }

        // ───────────── Конструирование ─────────────

        static void Check(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new ArithmeticException("GameNumber: недопустимое значение " + v.ToString(CultureInfo.InvariantCulture));
        }

        public static GameNumber FromDouble(double value)
        {
            Check(value);
            if (Math.Abs(value) < SmallLimit) return new GameNumber(value, 0L, false);
            return FromParts(value, 0L);
        }

        /// <summary>m × 10^e с нормализацией и канонизацией (малые значения — как double).</summary>
        static GameNumber FromParts(double m, long e)
        {
            Check(m);
            if (m == 0.0) return Zero;
            double abs = Math.Abs(m);
            long shift = (long)Math.Floor(Math.Log10(abs));
            if (shift != 0)
            {
                if (shift > DoubleMaxExp || shift < DoubleMinExp)
                {
                    long half = shift / 2;
                    m = m / Pow10(half) / Pow10(shift - half);
                }
                else
                {
                    m /= Pow10(shift);
                }
                e += shift;
            }
            abs = Math.Abs(m);
            if (abs >= 10.0) { m /= 10.0; e += 1; }
            else if (abs < 1.0) { m *= 10.0; e -= 1; }
            if (e < SmallLimitExponent)
            {
                // Помещается в «малую» область: храним как double.
                double v = e < DoubleMinExp ? 0.0 : m * Pow10(e);
                if (Math.Abs(v) < SmallLimit) return new GameNumber(v, 0L, false);
            }
            return new GameNumber(m, e, true);
        }

        /// <summary>10^log10Value без промежуточного double, который мог бы переполниться.</summary>
        public static GameNumber FromLog10(double log10Value)
        {
            if (double.IsNaN(log10Value)) throw new ArithmeticException("GameNumber.FromLog10: NaN");
            if (double.IsNegativeInfinity(log10Value)) return Zero;
            if (double.IsPositiveInfinity(log10Value)) throw new ArithmeticException("GameNumber.FromLog10: +Infinity");
            if (log10Value < SmallLimitExponent - 1) return FromDouble(Math.Pow(10.0, log10Value));
            double floor = Math.Floor(log10Value);
            return FromParts(Math.Pow(10.0, log10Value - floor), (long)floor);
        }

        public static implicit operator GameNumber(double value) => FromDouble(value);
        public static implicit operator GameNumber(int value) => FromDouble(value);
        public static implicit operator GameNumber(long value) => FromDouble(value);

        // ───────────── Свойства ─────────────

        public bool IsBig => m_Big;
        public bool IsZero => !m_Big && m_V == 0.0;
        public bool IsPositive => m_V > 0.0;
        public bool IsNegative => m_V < 0.0;
        public int Sign => m_V > 0 ? 1 : (m_V < 0 ? -1 : 0);

        /// <summary>Нормализованная мантисса: 1 ≤ |m| &lt; 10 (0 для нуля).</summary>
        public double Mantissa
        {
            get
            {
                if (m_Big) return m_V;
                Split(m_V, out double m, out _);
                return m;
            }
        }

        /// <summary>Десятичный показатель нормализованной формы.</summary>
        public long Exponent
        {
            get
            {
                if (m_Big) return m_E;
                Split(m_V, out _, out long e);
                return e;
            }
        }

        static void Split(double v, out double m, out long e)
        {
            if (v == 0.0) { m = 0.0; e = 0; return; }
            double abs = Math.Abs(v);
            e = (long)Math.Floor(Math.Log10(abs));
            m = v / Pow10(e);
            if (double.IsInfinity(m) || double.IsNaN(m))
            {
                // Субнормальные значения: делим в два шага.
                long half = e / 2;
                m = v / Pow10(half) / Pow10(e - half);
            }
            double am = Math.Abs(m);
            if (am >= 10.0) { m /= 10.0; e += 1; }
            else if (am < 1.0) { m *= 10.0; e -= 1; }
        }

        void Parts(out double m, out long e)
        {
            if (m_Big) { m = m_V; e = m_E; }
            else Split(m_V, out m, out e);
        }

        /// <summary>Значение в double; за пределами диапазона — ±Infinity.</summary>
        public double ToDouble()
        {
            if (!m_Big) return m_V;
            if (m_E > DoubleMaxExp + 1) return m_V > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (m_E > DoubleMaxExp) return m_V * 10.0 * Pow10(m_E - 1);
            return m_V * Pow10(m_E);
        }

        public bool FitsInDouble => !double.IsInfinity(ToDouble());

        public double Log10()
        {
            if (m_V <= 0.0) throw new ArithmeticException("GameNumber.Log10: значение должно быть положительным");
            return m_Big ? Math.Log10(m_V) + m_E : Math.Log10(m_V);
        }

        public GameNumber Abs() => m_V < 0 ? new GameNumber(-m_V, m_E, m_Big) : this;

        // ───────────── Арифметика ─────────────

        public static GameNumber operator -(GameNumber a) => new GameNumber(-a.m_V, a.m_E, a.m_Big);

        public static GameNumber operator +(GameNumber a, GameNumber b)
        {
            if (!a.m_Big && !b.m_Big)
            {
                double r = a.m_V + b.m_V;
                return Math.Abs(r) < SmallLimit ? new GameNumber(r, 0L, false) : FromParts(r, 0L);
            }
            if (a.IsZero) return b;
            if (b.IsZero) return a;
            a.Parts(out double am, out long ae);
            b.Parts(out double bm, out long be);
            if (ae < be)
            {
                double tm = am; am = bm; bm = tm;
                long te = ae; ae = be; be = te;
            }
            long diff = ae - be;
            if (diff > 17) return FromParts(am, ae);
            return FromParts(am + bm * Pow10(-diff), ae);
        }

        public static GameNumber operator -(GameNumber a, GameNumber b) => a + (-b);

        public static GameNumber operator *(GameNumber a, GameNumber b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            if (!a.m_Big && !b.m_Big)
            {
                double r = a.m_V * b.m_V;
                if (!double.IsInfinity(r) && Math.Abs(r) < SmallLimit && r != 0.0) return new GameNumber(r, 0L, false);
            }
            a.Parts(out double am, out long ae);
            b.Parts(out double bm, out long be);
            return FromParts(am * bm, ae + be);
        }

        public static GameNumber operator /(GameNumber a, GameNumber b)
        {
            if (b.IsZero) throw new DivideByZeroException("GameNumber: деление на ноль");
            if (a.IsZero) return Zero;
            if (!a.m_Big && !b.m_Big)
            {
                double r = a.m_V / b.m_V;
                if (!double.IsInfinity(r) && Math.Abs(r) < SmallLimit && r != 0.0) return new GameNumber(r, 0L, false);
            }
            a.Parts(out double am, out long ae);
            b.Parts(out double bm, out long be);
            return FromParts(am / bm, ae - be);
        }

        /// <summary>base^power. В диапазоне double — точный Math.Pow (2·4^5 = 2048), иначе через логарифм.</summary>
        public static GameNumber Pow(double baseValue, double power)
        {
            if (double.IsNaN(baseValue) || double.IsNaN(power)) throw new ArithmeticException("GameNumber.Pow: NaN");
            if (power == 0.0) return One;
            if (baseValue == 0.0)
            {
                if (power > 0) return Zero;
                throw new DivideByZeroException("GameNumber.Pow: 0 в отрицательной степени");
            }
            if (baseValue < 0.0)
            {
                if (Math.Floor(power) != power) throw new ArithmeticException("GameNumber.Pow: отрицательное основание и дробная степень");
                var r = PowPositive(-baseValue, power);
                bool odd = Math.Abs(power % 2.0) == 1.0;
                return odd ? -r : r;
            }
            return PowPositive(baseValue, power);
        }

        static GameNumber PowPositive(double baseValue, double power)
        {
            double log = power * Math.Log10(baseValue);
            if (log < SmallLimitExponent - 1 && log > -(SmallLimitExponent - 1))
            {
                double direct = Math.Pow(baseValue, power);
                if (direct != 0.0 && !double.IsInfinity(direct) && !double.IsNaN(direct)) return FromDouble(direct);
            }
            return FromLog10(log);
        }

        public GameNumber Pow(double power)
        {
            if (m_V < 0) throw new ArithmeticException("GameNumber.Pow: отрицательное основание");
            if (IsZero) return power > 0 ? Zero : One;
            if (!m_Big) return PowPositive(m_V, power);
            return FromLog10(Log10() * power);
        }

        // ───────────── Округления ─────────────

        public GameNumber Floor() => m_Big ? this : FromDouble(Math.Floor(m_V));
        public GameNumber Ceiling() => m_Big ? this : FromDouble(Math.Ceiling(m_V));

        /// <summary>2^53: начиная с этого модуля каждый double — целое число.</summary>
        const double ExactIntegerLimit = 9007199254740992.0;
        /// <summary>
        /// Предел абсолютного сдвига в округлениях с допуском: допуск гасит только шум double и никогда не сдвигает
        /// значение на целые единицы (целое не уменьшается при ceil, настоящая дробная часть не теряется).
        /// </summary>
        const double MaxAbsoluteTolerance = 1e-6;

        /// <summary>
        /// Округление вверх с допуском на шум double: 2048.0000000001 → 2048.
        /// Допуск относительный, но не больше 1e-6 по модулю. Для |v| ≥ 2^53 и большой формы — тождество (ТЗ, C.1 F-R).
        /// </summary>
        public GameNumber CeilingTolerant(double relativeTolerance = 1e-12)
        {
            if (m_Big || Math.Abs(m_V) >= ExactIntegerLimit) return this;
            double tolerance = Math.Min(Math.Abs(m_V) * relativeTolerance, MaxAbsoluteTolerance);
            return FromDouble(Math.Ceiling(m_V - tolerance));
        }

        /// <summary>
        /// Округление вниз с допуском на шум double: 156.99999999999 → 157.
        /// Допуск относительный, но не больше 1e-6 по модулю. Для |v| ≥ 2^53 и большой формы — тождество (ТЗ, C.1 F-R).
        /// </summary>
        public GameNumber FloorTolerant(double relativeTolerance = 1e-12)
        {
            if (m_Big || Math.Abs(m_V) >= ExactIntegerLimit) return this;
            double tolerance = Math.Min(Math.Abs(m_V) * relativeTolerance, MaxAbsoluteTolerance);
            return FromDouble(Math.Floor(m_V + tolerance));
        }

        public static GameNumber Max(GameNumber a, GameNumber b) => a >= b ? a : b;
        public static GameNumber Min(GameNumber a, GameNumber b) => a <= b ? a : b;

        // ───────────── Сравнение ─────────────

        public int CompareTo(GameNumber other)
        {
            if (!m_Big && !other.m_Big) return m_V.CompareTo(other.m_V);
            int sa = Sign, sb = other.Sign;
            if (sa != sb) return sa.CompareTo(sb);
            if (sa == 0) return 0;
            Parts(out double am, out long ae);
            other.Parts(out double bm, out long be);
            if (ae != be)
            {
                int byExp = ae.CompareTo(be);
                return sa > 0 ? byExp : -byExp;
            }
            return am.CompareTo(bm);
        }

        public bool Equals(GameNumber other) => m_Big == other.m_Big && m_V == other.m_V && (!m_Big || m_E == other.m_E);
        public override bool Equals(object obj) => obj is GameNumber g && Equals(g);
        public override int GetHashCode() => m_Big ? m_V.GetHashCode() * 397 ^ m_E.GetHashCode() : m_V.GetHashCode();

        public static bool operator ==(GameNumber a, GameNumber b) => a.Equals(b);
        public static bool operator !=(GameNumber a, GameNumber b) => !a.Equals(b);
        public static bool operator <(GameNumber a, GameNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(GameNumber a, GameNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(GameNumber a, GameNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GameNumber a, GameNumber b) => a.CompareTo(b) >= 0;

        public static bool ApproximatelyEqual(GameNumber a, GameNumber b, double relativeTolerance)
        {
            if (a == b) return true;
            if (a.IsZero || b.IsZero) return (a - b).Abs() <= FromDouble(relativeTolerance);
            var diff = (a - b).Abs();
            var scale = Max(a.Abs(), b.Abs());
            return diff / scale <= FromDouble(relativeTolerance);
        }

        // ───────────── Строки ─────────────

        /// <summary>Строка для сохранения: round-trip, InvariantCulture. Примеры: "15", "1.27E+19", "1.2345e352".</summary>
        public string ToStorageString()
        {
            if (!m_Big) return m_V.ToString("R", CultureInfo.InvariantCulture);
            return m_V.ToString("R", CultureInfo.InvariantCulture) + "e" + m_E.ToString(CultureInfo.InvariantCulture);
        }

        public static GameNumber Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("GameNumber: пустая строка");
            text = text.Trim();
            bool isDouble = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsInfinity(d) && !double.IsNaN(d);
            // Малая форма (|v| < 1e300) — обычный double "R": разбор как double точен.
            if (isDouble && Math.Abs(d) < SmallLimit) return FromDouble(d);
            // Большая форма "<мантисса>e<показатель>": мантисса и показатель разбираются отдельно, даже если значение
            // помещается в double. Иначе FromDouble делит на 10^показатель и сдвигает мантиссу на 1 ulp (ТЗ 12.1, round-trip).
            int ePos = text.LastIndexOfAny(new[] { 'e', 'E' });
            if (ePos <= 0)
            {
                if (isDouble) return FromDouble(d);
                throw new FormatException("GameNumber: неверная строка '" + text + "'");
            }
            double m = double.Parse(text.Substring(0, ePos), NumberStyles.Float, CultureInfo.InvariantCulture);
            long e = long.Parse(text.Substring(ePos + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
            return FromParts(m, e);
        }

        public override string ToString() => ToStorageString();
    }
}
