using System;
using System.Globalization;
using CatMiners.Core.Text;

namespace CatMiners.Core
{
    /// <summary>
    /// Показ чисел в UI (ТЗ 15.1): до 1e3 без суффикса, затем K, M, B, T, затем двухбуквенные aa, ab, …,
    /// три значащие цифры. Суффиксы покрывают только диапазон double (до 1e308, последний — «dt» для 1e306);
    /// от 1e308 и выше — научная запись с тремя значащими цифрами и усечённой мантиссой: «1.23e352».
    /// Округление показа не влияет на расчётное значение.
    /// </summary>
    public static class NumberFormat
    {
        static readonly string[] s_Short = { "", "K", "M", "B", "T" };

        /// <summary>Наименьший десятичный показатель научной записи: всё, что не меньше 1e308, — вне диапазона суффиксов.</summary>
        public const long ScientificFromExponent = 308;

        public static string Format(GameNumber value)
        {
            if (value.IsZero) return "0";
            if (value.IsNegative) return "-" + Format(-value);

            if (value.Exponent < 3)
            {
                double d = value.ToDouble();
                if (d >= 100 || Math.Abs(d - Math.Round(d)) < 1e-9)
                    return Math.Floor(d + 1e-9).ToString("0", CultureInfo.InvariantCulture);
                if (d >= 10) return TrimZeros(d.ToString("0.0", CultureInfo.InvariantCulture));
                return TrimZeros(d.ToString("0.##", CultureInfo.InvariantCulture));
            }

            long group = value.Exponent / 3;              // 1 = тысячи
            string suffix = value.Exponent >= ScientificFromExponent ? null : SuffixForGroup(group);
            if (suffix == null)
            {
                // Три значащие цифры, мантисса усекается вниз, как и у суффиксов: 1.239e352 → «1.23e352».
                double mantissa = Math.Min(999.0, Math.Floor(value.Mantissa * 100.0 + 1e-9)) / 100.0;
                return mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + value.Exponent.ToString(CultureInfo.InvariantCulture);
            }

            int digitsBeforePoint = (int)(value.Exponent - group * 3);  // 0, 1, 2
            double scaled = value.Mantissa * Math.Pow(10, digitsBeforePoint);
            // Три значащие цифры, усечение вниз — показ не должен обещать больше, чем есть.
            int decimals = 2 - digitsBeforePoint;
            double factor = Math.Pow(10, decimals);
            double truncated = Math.Floor(scaled * factor + 1e-9) / factor;
            string num = truncated.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return TrimZeros(num) + suffix;
        }

        public static string Format(double value) => Format(GameNumber.FromDouble(value));

        /// <summary>Суффикс для группы 10^(3·group). null — вне диапазона суффиксов (группы за пределами double).</summary>
        public static string SuffixForGroup(long group)
        {
            if (group < 0) return null;
            if (group < s_Short.Length) return s_Short[group];
            if (group * 3 >= ScientificFromExponent) return null;
            long index = group - s_Short.Length;          // 0 → "aa"
            if (index >= 26 * 26) return null;
            char a = (char)('a' + index / 26);
            char b = (char)('a' + index % 26);
            return new string(new[] { a, b });
        }

        public static string FormatDuration(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return Loc.T("core.duration.unknown");
            if (seconds < 0) seconds = 0;
            long s = (long)Math.Ceiling(seconds - 1e-9);
            if (s < 60) return Loc.F("core.duration.seconds", s);
            long m = s / 60; s %= 60;
            if (m < 60) return Loc.F("core.duration.minutes_seconds", m, s);
            long h = m / 60; m %= 60;
            if (h < 48) return Loc.F("core.duration.hours_minutes", h, m);
            long days = h / 24; h %= 24;
            return Loc.F("core.duration.days_hours", days, h);
        }

        static string TrimZeros(string s)
        {
            if (s.IndexOf('.') < 0) return s;
            s = s.TrimEnd('0');
            return s.EndsWith(".", StringComparison.Ordinal) ? s.Substring(0, s.Length - 1) : s;
        }
    }
}
