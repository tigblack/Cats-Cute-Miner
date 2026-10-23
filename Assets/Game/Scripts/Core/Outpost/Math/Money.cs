using System;
using System.Globalization;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Деньги режима — точные BigInteger (ТЗ 12.2). Короткий показ: до 1000 без суффикса, затем три значащие цифры
    /// с усечением вниз и суффиксами <see cref="NumberFormat.SuffixForGroup"/> (K, M, B, T, aa, …), за пределами суффиксов —
    /// «1.23e352». Строка сохранения — десятичная запись без разделителей (round-trip без потери единицы).
    /// </summary>
    public static class MoneyFormat
    {
        public static string Format(BigInteger value)
        {
            if (value.Sign < 0) return "-" + Format(-value);
            if (value < 1000) return value.ToString(CultureInfo.InvariantCulture);
            string digits = value.ToString(CultureInfo.InvariantCulture);
            int exponent = digits.Length - 1;
            long group = exponent / 3;
            string suffix = exponent >= NumberFormat.ScientificFromExponent ? null : NumberFormat.SuffixForGroup(group);
            if (suffix == null)
                return digits[0] + "." + digits.Substring(1, 2) + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            int before = exponent - (int)group * 3 + 1;     // 1..3 цифры до точки
            string intPart = digits.Substring(0, before);
            string frac = digits.Substring(before, 3 - before).TrimEnd('0');
            return (frac.Length > 0 ? intPart + "." + frac : intPart) + suffix;
        }

        /// <summary>Десятичная строка сохранения.</summary>
        public static string ToStorage(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

        public static BigInteger Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("Деньги: пустая строка");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c < '0' || c > '9') && !(i == 0 && c == '-' && text.Length > 1))
                    throw new FormatException("Деньги: неверная запись \"" + text + "\"");
            }
            return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
    }
}
