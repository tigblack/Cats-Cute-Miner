using System;
using System.Globalization;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Точная дробь на BigInteger (ТЗ 6.5, 12.2, 12.4): знаменатель всегда положителен, дробь несократима.
    /// default(Rational) — ноль (0/1). double допустим только для отображения (<see cref="ToDouble"/>).
    /// </summary>
    public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
    {
        readonly BigInteger m_Num;
        readonly BigInteger m_Den;   // 0 только у default — трактуется как 1

        public static readonly Rational Zero = new Rational(BigInteger.Zero, BigInteger.One, true);
        public static readonly Rational One = new Rational(BigInteger.One, BigInteger.One, true);
        public static readonly Rational Half = new Rational(BigInteger.One, new BigInteger(2), true);

        public BigInteger Num => m_Num;
        public BigInteger Den => m_Den.IsZero ? BigInteger.One : m_Den;

        Rational(BigInteger num, BigInteger den, bool normalized)
        {
            m_Num = num;
            m_Den = den;
        }

        public Rational(BigInteger num, BigInteger den)
        {
            if (den.IsZero) throw new DivideByZeroException("Rational: нулевой знаменатель");
            if (den.Sign < 0)
            {
                num = -num;
                den = -den;
            }
            var g = BigInteger.GreatestCommonDivisor(num, den);
            if (!g.IsOne && !g.IsZero)
            {
                num /= g;
                den /= g;
            }
            if (num.IsZero) den = BigInteger.One;
            m_Num = num;
            m_Den = den;
        }

        public Rational(BigInteger value) : this(value, BigInteger.One, true) { }

        public static Rational FromLong(long value) => new Rational(new BigInteger(value), BigInteger.One, true);
        public static Rational Of(long num, long den) => new Rational(new BigInteger(num), new BigInteger(den));

        public static implicit operator Rational(int value) => new Rational(new BigInteger(value), BigInteger.One, true);
        public static implicit operator Rational(long value) => new Rational(new BigInteger(value), BigInteger.One, true);
        public static implicit operator Rational(BigInteger value) => new Rational(value, BigInteger.One, true);

        public int Sign => m_Num.Sign;
        public bool IsZero => m_Num.IsZero;
        public bool IsInteger => Den.IsOne;

        public static Rational operator +(Rational a, Rational b)
        {
            if (a.Den.IsOne && b.Den.IsOne) return new Rational(a.m_Num + b.m_Num, BigInteger.One, true);
            return new Rational(a.m_Num * b.Den + b.m_Num * a.Den, a.Den * b.Den);
        }

        public static Rational operator -(Rational a, Rational b)
        {
            if (a.Den.IsOne && b.Den.IsOne) return new Rational(a.m_Num - b.m_Num, BigInteger.One, true);
            return new Rational(a.m_Num * b.Den - b.m_Num * a.Den, a.Den * b.Den);
        }

        public static Rational operator -(Rational a) => new Rational(-a.m_Num, a.Den, true);

        public static Rational operator *(Rational a, Rational b) => new Rational(a.m_Num * b.m_Num, a.Den * b.Den);

        public static Rational operator /(Rational a, Rational b)
        {
            if (b.m_Num.IsZero) throw new DivideByZeroException("Rational: деление на ноль");
            return new Rational(a.m_Num * b.Den, a.Den * b.m_Num);
        }

        public static bool operator ==(Rational a, Rational b) => a.Equals(b);
        public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
        public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
        public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
        public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

        public int CompareTo(Rational other) => (m_Num * other.Den).CompareTo(other.m_Num * Den);

        public bool Equals(Rational other) => m_Num.Equals(other.m_Num) && Den.Equals(other.Den);
        public override bool Equals(object obj) => obj is Rational r && Equals(r);
        public override int GetHashCode() => unchecked(m_Num.GetHashCode() * 397 ^ Den.GetHashCode());

        public static Rational Min(Rational a, Rational b) => a <= b ? a : b;
        public static Rational Max(Rational a, Rational b) => a >= b ? a : b;

        /// <summary>Целая степень; отрицательная — обратная дробь.</summary>
        public Rational Pow(int exponent)
        {
            if (exponent == 0) return One;
            if (exponent > 0) return new Rational(BigInteger.Pow(m_Num, exponent), BigInteger.Pow(Den, exponent), true);
            if (m_Num.IsZero) throw new DivideByZeroException("Rational: 0 в отрицательной степени");
            return new Rational(BigInteger.Pow(Den, -exponent), BigInteger.Pow(m_Num, -exponent));
        }

        /// <summary>Наименьшее целое, не меньшее дроби.</summary>
        public BigInteger Ceil() => ExactMath.CeilDiv(m_Num, Den);

        /// <summary>Наибольшее целое, не большее дроби.</summary>
        public BigInteger Floor() => ExactMath.FloorDiv(m_Num, Den);

        /// <summary>Округление половины вверх: floor(x + 1/2).</summary>
        public BigInteger RoundHalfUp() => ExactMath.FloorDiv(m_Num * 2 + Den, Den * 2);

        public double ToDouble()
        {
            // Для огромных чисел — через логарифмы, чтобы не получить ∞/∞ (только отображение).
            double n = (double)m_Num, d = (double)Den;
            if (!double.IsInfinity(n) && !double.IsInfinity(d)) return n / d;
            if (m_Num.IsZero) return 0;
            double log = BigInteger.Log(BigInteger.Abs(m_Num)) - BigInteger.Log(Den);
            return m_Num.Sign * Math.Exp(log);
        }

        /// <summary>"n/d" либо "n" для целых — каноническая строка сохранения.</summary>
        public override string ToString()
        {
            if (Den.IsOne) return m_Num.ToString(CultureInfo.InvariantCulture);
            return m_Num.ToString(CultureInfo.InvariantCulture) + "/" + Den.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Разбор "6/5", "-3/20", "1.18" (точно 59/50), "5". Десятичная запись читается без double.
        /// </summary>
        public static Rational Parse(string text)
        {
            if (!TryParse(text, out var r)) throw new FormatException("Rational: неверная запись \"" + text + "\"");
            return r;
        }

        public static bool TryParse(string text, out Rational value)
        {
            value = Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int slash = text.IndexOf('/');
            if (slash >= 0)
            {
                if (!TryParseDecimal(text.Substring(0, slash).Trim(), out var n) ||
                    !TryParseDecimal(text.Substring(slash + 1).Trim(), out var d) || d.IsZero) return false;
                value = n / d;
                return true;
            }
            return TryParseDecimal(text, out value);
        }

        static bool TryParseDecimal(string text, out Rational value)
        {
            value = Zero;
            if (text.Length == 0) return false;
            bool negative = false;
            int i = 0;
            if (text[0] == '-' || text[0] == '+')
            {
                negative = text[0] == '-';
                i = 1;
            }
            if (i >= text.Length) return false;
            var num = BigInteger.Zero;
            var den = BigInteger.One;
            bool dot = false, digits = false;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.')
                {
                    if (dot) return false;
                    dot = true;
                    continue;
                }
                if (c < '0' || c > '9') return false;
                digits = true;
                num = num * 10 + (c - '0');
                if (dot) den *= 10;
            }
            if (!digits) return false;
            value = new Rational(negative ? -num : num, den);
            return true;
        }
    }

    /// <summary>Точные целочисленные операции для формул ТЗ 12.4 (без double).</summary>
    public static class ExactMath
    {
        public static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            if (b.Sign < 0)
            {
                a = -a;
                b = -b;
            }
            var q = BigInteger.DivRem(a, b, out var r);
            if (!r.IsZero && a.Sign < 0) q -= 1;
            return q;
        }

        public static BigInteger CeilDiv(BigInteger a, BigInteger b)
        {
            if (b.Sign < 0)
            {
                a = -a;
                b = -b;
            }
            var q = BigInteger.DivRem(a, b, out var r);
            if (!r.IsZero && a.Sign > 0) q += 1;
            return q;
        }

        /// <summary>Целая часть квадратного корня неотрицательного числа (Ньютон).</summary>
        public static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n), "ISqrt: отрицательное число");
            if (n < 2) return n;
            int bits = (int)Math.Ceiling(BigInteger.Log(n, 2));
            var x = BigInteger.One << (bits / 2 + 1);
            while (true)
            {
                var y = (x + n / x) >> 1;
                if (y >= x) break;
                x = y;
            }
            while (x * x > n) x -= 1;
            while ((x + 1) * (x + 1) <= n) x += 1;
            return x;
        }

        /// <summary>
        /// Точный ceil(c * sqrt(n)) для положительной дроби c = a/b: минимальное h с h²·b² ≥ a²·n (ТЗ 12.4).
        /// Повторяет adapted_hp из Docs/TZ/analysis/outpost_v2_audit.py.
        /// </summary>
        public static BigInteger CeilTimesSqrt(Rational c, int n)
        {
            if (c.Sign <= 0) throw new ArgumentOutOfRangeException(nameof(c), "CeilTimesSqrt: нужна положительная дробь");
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
            var numSq = c.Num * c.Num * n;
            var denSq = c.Den * c.Den;
            var root = ISqrt(numSq / denSq);
            return root * root * denSq < numSq ? root + 1 : root;
        }

        public static long ToLongChecked(BigInteger v, string what)
        {
            if (v > long.MaxValue || v < long.MinValue) throw new OverflowException(what + ": значение вне диапазона long");
            return (long)v;
        }

        public static int ToIntChecked(BigInteger v, string what)
        {
            if (v > int.MaxValue || v < int.MinValue) throw new OverflowException(what + ": значение вне диапазона int");
            return (int)v;
        }
    }
}
