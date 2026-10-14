using System;
using System.Collections.Generic;
using System.Globalization;

namespace CatMiners.Core.Text
{
    /// <summary>
    /// Единый словарь строк интерфейса (ТЗ 5.1): весь текст, который видит игрок, берётся отсюда по ключу,
    /// чтобы позже добавить другие языки. Язык игры — английский (<see cref="Language"/>); русские тексты до перевода —
    /// в Backups/before-english-ui-2026-10-12.
    /// <para>
    /// Строки разложены по разделам — по файлу <c>Loc.&lt;Раздел&gt;.cs</c> на раздел: Core, Hud, Controller, Tutorial,
    /// Catalog, Board, Outpost (режим CatOutpostV2), Cards, Playable (playable-сборка). Каждый файл реализует свой частичный метод <c>static partial void Register&lt;Раздел&gt;(Dictionary&lt;string, string&gt; d)</c>
    /// и добавляет строки через <c>d.Add("раздел.группа.имя", "Текст")</c>; общий файл при этом не правится.
    /// Ключ начинается с имени раздела в нижнем регистре и точки («hud.», «core.» …), иначе это проблема регистрации.
    /// </para>
    /// <para>
    /// <see cref="T"/> — текст по ключу; <see cref="F"/> — шаблон <see cref="string.Format(IFormatProvider, string, object[])"/>
    /// с <see cref="CultureInfo.InvariantCulture"/>. Отсутствующий ключ виден на экране как «[ключ]» и попадает в
    /// <see cref="MissingKeys"/> и событие <see cref="MissingKey"/>.
    /// </para>
    /// </summary>
    public static partial class Loc
    {
        /// <summary>Язык словаря: английский (с 12 октября 2026, до этого — русский).</summary>
        public const string Language = "en";

        /// <summary>Разделы словаря в порядке регистрации (имя раздела = суффикс метода Register&lt;Раздел&gt;).</summary>
        public static readonly IReadOnlyList<string> Sections = new[] { "Core", "Hud", "Controller", "Tutorial", "Catalog", "Board", "Outpost", "Cards", "Playable" };

        static readonly object s_Lock = new object();
        static Dictionary<string, string> s_Table;
        static List<string> s_Problems;
        static readonly List<string> s_Missing = new List<string>();
        static readonly HashSet<string> s_MissingSet = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<string> s_FormatErrors = new List<string>();

        /// <summary>Запрошен ключ, которого нет в словаре (аргумент — ключ). Вызывается при каждом таком запросе.</summary>
        public static event Action<string> MissingKey;

        // Разделы: реализация — в Loc.<Раздел>.cs. Нет реализации — раздел просто пуст.
        static partial void RegisterCore(Dictionary<string, string> d);
        static partial void RegisterHud(Dictionary<string, string> d);
        static partial void RegisterController(Dictionary<string, string> d);
        static partial void RegisterTutorial(Dictionary<string, string> d);
        static partial void RegisterCatalog(Dictionary<string, string> d);
        static partial void RegisterBoard(Dictionary<string, string> d);
        static partial void RegisterOutpost(Dictionary<string, string> d);
        static partial void RegisterCards(Dictionary<string, string> d);
        static partial void RegisterPlayable(Dictionary<string, string> d);

        /// <summary>Текст по ключу. Нет ключа — «[ключ]» и запись в <see cref="MissingKeys"/>.</summary>
        public static string T(string key)
        {
            if (key != null && Table.TryGetValue(key, out var text)) return text;
            ReportMissing(key);
            return "[" + (key ?? "null") + "]";
        }

        /// <summary>
        /// Шаблон по ключу, отформатированный <see cref="string.Format(IFormatProvider, string, object[])"/> с
        /// <see cref="CultureInfo.InvariantCulture"/>: «{0}», «{1:00}» и т. п. Нет ключа — «[ключ]», как у <see cref="T"/>.
        /// Ошибка шаблона (лишний индекс, непарная скобка) не роняет игру: возвращается сам шаблон, а ошибка — в <see cref="FormatErrors"/>.
        /// </summary>
        public static string F(string key, params object[] args)
        {
            if (key == null || !Table.TryGetValue(key, out var format))
            {
                ReportMissing(key);
                return "[" + (key ?? "null") + "]";
            }
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args ?? Array.Empty<object>());
            }
            catch (FormatException e)
            {
                lock (s_Lock) s_FormatErrors.Add(key + ": " + e.Message);
                return format;
            }
        }

        /// <summary>
        /// Форма слова по числу по правилу английского языка: ключи «<paramref name="key"/>.one» (1 и −1) и «.many»
        /// (все остальные, включая 0 и 21). Подстановка {0} = <paramref name="n"/> доступна в каждой форме.
        /// </summary>
        public static string Plural(string key, long n) => F(key + (n == 1 || n == -1 ? ".one" : ".many"), n);

        /// <summary>Есть ли ключ в словаре (без записи в <see cref="MissingKeys"/>).</summary>
        public static bool Has(string key) => key != null && Table.ContainsKey(key);

        /// <summary>Текст по ключу без записи в <see cref="MissingKeys"/>: для необязательных строк с запасным текстом в коде.</summary>
        public static bool TryGet(string key, out string text)
        {
            text = null;
            return key != null && Table.TryGetValue(key, out text);
        }

        /// <summary>Все зарегистрированные ключи (по возрастанию, ординально).</summary>
        public static IReadOnlyList<string> Keys
        {
            get
            {
                var keys = new List<string>(Table.Keys);
                keys.Sort(StringComparer.Ordinal);
                return keys;
            }
        }

        /// <summary>Ключи, которые запрашивались, но не нашлись (без повторов, в порядке первого запроса).</summary>
        public static IReadOnlyList<string> MissingKeys
        {
            get { lock (s_Lock) return s_Missing.ToArray(); }
        }

        /// <summary>Ошибки форматирования <see cref="F"/>: «ключ: сообщение».</summary>
        public static IReadOnlyList<string> FormatErrors
        {
            get { lock (s_Lock) return s_FormatErrors.ToArray(); }
        }

        /// <summary>
        /// Проблемы сборки словаря: ключ в двух разделах, ключ без префикса своего раздела, повтор <c>d.Add</c> в разделе,
        /// пустой ключ или null-текст. Пустой список — словарь собран чисто.
        /// </summary>
        public static IReadOnlyList<string> RegistrationProblems
        {
            get
            {
                _ = Table;
                lock (s_Lock) return s_Problems.ToArray();
            }
        }

        /// <summary>Очищает <see cref="MissingKeys"/> и <see cref="FormatErrors"/> (для тестов).</summary>
        public static void ResetReports()
        {
            lock (s_Lock)
            {
                s_Missing.Clear();
                s_MissingSet.Clear();
                s_FormatErrors.Clear();
            }
        }

        static Dictionary<string, string> Table
        {
            get
            {
                var table = s_Table;
                if (table != null) return table;
                lock (s_Lock)
                {
                    if (s_Table == null)
                    {
                        var problems = new List<string>();
                        var built = Build(problems);
                        s_Problems = problems;
                        s_Table = built;
                    }
                    return s_Table;
                }
            }
        }

        static Dictionary<string, string> Build(List<string> problems)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            AddSection(table, owner, problems, "Core", d => RegisterCore(d));
            AddSection(table, owner, problems, "Hud", d => RegisterHud(d));
            AddSection(table, owner, problems, "Controller", d => RegisterController(d));
            AddSection(table, owner, problems, "Tutorial", d => RegisterTutorial(d));
            AddSection(table, owner, problems, "Catalog", d => RegisterCatalog(d));
            AddSection(table, owner, problems, "Board", d => RegisterBoard(d));
            AddSection(table, owner, problems, "Outpost", d => RegisterOutpost(d));
            AddSection(table, owner, problems, "Cards", d => RegisterCards(d));
            AddSection(table, owner, problems, "Playable", d => RegisterPlayable(d));
            return table;
        }

        /// <summary>Раздел собирается в свой словарь и вливается в общий: так видно, какой раздел дал ключ.</summary>
        static void AddSection(Dictionary<string, string> table, Dictionary<string, string> owner, List<string> problems,
                               string section, Action<Dictionary<string, string>> register)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                register(d);
            }
            catch (Exception e)
            {
                // Повтор d.Add внутри раздела: строки до него уже в d, остальные будут видны как «[ключ]».
                problems.Add(section + ": ошибка регистрации: " + e.Message);
            }
            string prefix = section.ToLowerInvariant() + ".";
            foreach (var kv in d)
            {
                if (string.IsNullOrEmpty(kv.Key)) { problems.Add(section + ": пустой ключ"); continue; }
                if (kv.Value == null) problems.Add(section + ": " + kv.Key + ": текст null");
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    problems.Add(section + ": " + kv.Key + ": ключ должен начинаться с «" + prefix + "»");
                if (owner.TryGetValue(kv.Key, out var other))
                {
                    problems.Add(kv.Key + ": ключ уже есть в разделе " + other + " (повтор в " + section + ")");
                    continue;
                }
                owner[kv.Key] = section;
                table[kv.Key] = kv.Value ?? "";
            }
        }

        static void ReportMissing(string key)
        {
            string k = key ?? "null";
            lock (s_Lock)
            {
                if (s_MissingSet.Add(k)) s_Missing.Add(k);
            }
            MissingKey?.Invoke(k);
        }
    }
}
