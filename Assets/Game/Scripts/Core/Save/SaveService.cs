using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CatMiners.Core.Save
{
    /// <summary>Файловые операции — внедряются, чтобы тесты могли прерывать запись на любом шаге (ТЗ 12.2).</summary>
    public interface IFileSystem
    {
        bool Exists(string path);
        byte[] ReadAllBytes(string path);
        void WriteAllBytesDurable(string path, byte[] data);
        void Replace(string source, string destination, string backup);
        void Move(string source, string destination);
        void Copy(string source, string destination);
        void Delete(string path);
        void CreateDirectory(string path);
    }

    /// <summary>
    /// Необязательное расширение IFileSystem: запись первых <c>count</c> байтов переиспользуемого буфера без копии массива.
    /// Файловые системы без него (тестовые двойники) получают массив точной длины.
    /// </summary>
    public interface IDurableBufferWriter
    {
        void WriteAllBytesDurable(string path, byte[] data, int count);
    }

    public sealed class RealFileSystem : IFileSystem, IDurableBufferWriter
    {
        public bool Exists(string path) => File.Exists(path);
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteAllBytesDurable(string path, byte[] data) => WriteAllBytesDurable(path, data, data.Length);

        public void WriteAllBytesDurable(string path, byte[] data, int count)
        {
            if (count < 0 || count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            // bufferSize 1 — без промежуточного буфера FileStream: данные уже лежат одним массивом.
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1))
            {
                fs.Write(data, 0, count);
                fs.Flush(true);
            }
        }

        public void Replace(string source, string destination, string backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Copy(string source, string destination) => File.Copy(source, destination, true);
        public void Delete(string path) => File.Delete(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    }

    /// <summary>JSON для GameNumber: строка round-trip, InvariantCulture.</summary>
    public sealed class GameNumberConverter : JsonConverter<GameNumber>
    {
        public override void WriteJson(JsonWriter writer, GameNumber value, JsonSerializer serializer) => writer.WriteValue(value.ToStorageString());

        public override GameNumber ReadJson(JsonReader reader, Type objectType, GameNumber existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null: return GameNumber.Zero;
                case JsonToken.String: return GameNumber.Parse((string)reader.Value);
                case JsonToken.Integer:
                case JsonToken.Float: return GameNumber.FromDouble(Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture));
                default: throw new JsonSerializationException("GameNumber: неожиданный токен " + reader.TokenType);
            }
        }
    }

    /// <summary>
    /// JsonTextWriter, который не пропускает NaN и ±Infinity в снимок (ТЗ 12.1): бросает InvalidDataException с JSON-путём поля.
    /// Вывод для конечных чисел не отличается от JsonTextWriter.
    /// </summary>
    sealed class FiniteJsonTextWriter : JsonTextWriter
    {
        public FiniteJsonTextWriter(TextWriter writer) : base(writer) { }

        public override void WriteValue(double value)
        {
            Check(value);
            base.WriteValue(value);
        }

        public override void WriteValue(double? value)
        {
            if (value.HasValue) Check(value.Value);
            base.WriteValue(value);
        }

        public override void WriteValue(float value)
        {
            Check(value);
            base.WriteValue(value);
        }

        public override void WriteValue(float? value)
        {
            if (value.HasValue) Check(value.Value);
            base.WriteValue(value);
        }

        void Check(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidDataException("NaN/Infinity в снимке сохранения: non-finite value at " + Path);
        }
    }

    /// <summary>TextWriter в растущий массив символов: переиспользуется между сохранениями без новых строк.</summary>
    sealed class CharBufferWriter : TextWriter
    {
        static readonly Encoding s_Utf8 = new UTF8Encoding(false);
        char[] m_Chars;
        int m_Length;

        public CharBufferWriter(int capacity) : base(CultureInfo.InvariantCulture) => m_Chars = new char[Math.Max(16, capacity)];

        public override Encoding Encoding => s_Utf8;
        public char[] Chars => m_Chars;
        public int Length => m_Length;
        public void Clear() => m_Length = 0;

        public override void Write(char value)
        {
            if (m_Length == m_Chars.Length) Grow(1);
            m_Chars[m_Length++] = value;
        }

        public override void Write(char[] buffer, int index, int count) => Append(buffer, index, count);

        public override void Write(string value)
        {
            if (value != null) Append(value, 0, value.Length);
        }

        public void Append(string s, int start, int count)
        {
            if (count <= 0) return;
            if (m_Length + count > m_Chars.Length) Grow(count);
            s.CopyTo(start, m_Chars, m_Length, count);
            m_Length += count;
        }

        public void Append(char[] src, int start, int count)
        {
            if (count <= 0) return;
            if (m_Length + count > m_Chars.Length) Grow(count);
            Array.Copy(src, start, m_Chars, m_Length, count);
            m_Length += count;
        }

        void Grow(int extra)
        {
            int capacity = Math.Max(m_Chars.Length * 2, m_Length + extra);
            Array.Resize(ref m_Chars, capacity);
        }
    }

    public enum LoadStatus
    {
        NoSave,
        Loaded,
        LoadedFromBackup,
        RulesChangedMineReset,
        IncompatibleBalance,
        Corrupted,
    }

    public sealed class LoadResult
    {
        public LoadStatus Status;
        public GameState State;
        /// <summary>Ошибки отвергнутых файлов, сбои переименования и пояснение статуса; null — нечего сообщить.</summary>
        public string Message;
        /// <summary>
        /// Файл, из которого загружено состояние (Loaded, LoadedFromBackup, RulesChangedMineReset) или который отвергнут
        /// (IncompatibleBalance — файл с другим балансом; Corrupted — первый проверенный файл слота). Путь до переименования.
        /// null при NoSave.
        /// </summary>
        public string SourcePath;
        /// <summary>Новые имена файлов, отложенных при загрузке (*.corrupt-…, *.incompatible-…): они не удаляются (ТЗ 12.2).</summary>
        public readonly List<string> QuarantinedFiles = new List<string>();
    }

    /// <summary>
    /// Сохранение: конверт { schemaVersion, rulesVersion, balanceSha256, slot, savedAtUtcMs, payloadSha256, payload(строка) },
    /// запись через tmp + Replace с резервной копией, загрузка main → tmp → bak (ТЗ 12.1–12.2).
    /// Экземпляр переиспользует буферы записи между сохранениями и рассчитан на один поток.
    /// </summary>
    /// <remarks>
    /// Владельцы полей payload (ТЗ 11.3: PlayerProgressState переносится между зонами, MineProgressState создаётся заново
    /// при входе в зону; в коде — классы полей GameState.Player и GameState.Mine). Каждое поле сохранения относится ровно к одному из них:
    ///
    /// | Поле payload | Владелец | Смысл |
    /// |---|---|---|
    /// | SchemaVersion, RulesVersion, BalanceSha256, Slot | корень (метаданные) | версия схемы, профиль баланса, SHA-256 JSON, слот Main/Debug; дублируются в конверте |
    /// | LastProcessedUtcMs | корень, уровень игрока | последняя обработанная метка UTC (офлайн, 12.3); при переходе зоны не сбрасывается |
    /// | NextOpId | корень, уровень игрока | сквозной ID операций (16); при переходе зоны не сбрасывается |
    /// | Player.Elixir, Player.Hard | PlayerProgressState | эликсир, твёрдая валюта |
    /// | Player.Cards | PlayerProgressState | карточки: уровни и копии |
    /// | Player.RewardCycle | PlayerProgressState | счётчики циклов наград по Rocks.Id |
    /// | Player.Dynamite | PlayerProgressState | запас динамита (≤ 4) |
    /// | Player.ChestReveals | PlayerProgressState | очередь сундуков с уже определённым результатом |
    /// | Player.FreeChest | PlayerProgressState | бесплатный сундук: флаг, запас, таймер, индекс цикла |
    /// | Player.DeliveryHistory | PlayerProgressState | окна MaxDupes, полученные доставки, пропуски рекламы |
    /// | Player.Rng | PlayerProgressState | состояние потоков PRNG |
    /// | Player.CurrentZone, Player.HighestCompletedZone, Player.CampaignCompleted | PlayerProgressState | текущая и завершённые зоны |
    /// | Player.TutorialDone | PlayerProgressState | пройденные шаги обучения |
    /// | Player.Settings | PlayerProgressState | настройки игрока (звук, вибрация) |
    /// | Player.ExcludedRewards | PlayerProgressState | журнал наград исключённых систем |
    /// | Player.PlayerTimeTicks | PlayerProgressState | монотонное время игрока для окон доставок |
    /// | Player.Ledger | PlayerProgressState | журнал начислений по источникам (4.4) |
    /// | Player.ZoneTimeTicks | PlayerProgressState | время прохождения завершённых зон (статистика) |
    /// | Mine.Zone, Mine.Gold | MineProgressState | номер зоны, золото |
    /// | Mine.Cats, Mine.NextCatId | MineProgressState | котики: ID, уровень, клетка, назначение, фаза удара |
    /// | Mine.Objects | MineProgressState | объекты: индекс якоря, состояние, HP, генератор (уровень, фаза, буфер, автоматизация, пороги) |
    /// | Mine.GatesOpened | MineProgressState | открытые ворота (их бонусы выводятся из числа) |
    /// | Mine.HireCount, Mine.ActivatedShafts | MineProgressState | счётчик найма, активированные шахты |
    /// | Mine.FreeCatTimerTicks, Mine.FreeCatReady | MineProgressState | таймер бесплатного котика |
    /// | Mine.DeliveriesEnabled, Mine.DeliveryTimerTicks, Mine.DeliveryReady | MineProgressState | таймер доставки |
    /// | Mine.Containers, Mine.NextContainerId | MineProgressState | контейнеры (бесплатный котик, бочка найма) и ящики доставок с определённым содержимым |
    /// | Mine.ZoneTimeTicks, Mine.RocksDestroyed, Mine.CatsHired, Mine.Merges | MineProgressState | локальная статистика зоны |
    /// | Mine.CameraFocusZ | MineProgressState | позиция камеры (12.1) |
    /// | Mine.AutomationCheckPending | MineProgressState | отложенная проверка автоматизации после офлайна (12.3) |
    /// | Mine.Loot, Mine.NextLootId, Mine.LootCollected | MineProgressState | добыча камней на поле: ID, клетка, валюта, сумма, возраст, камень; счётчик собранной |
    ///
    /// Новое поле сохранения добавляется в эту таблицу; изменение структуры payload — schemaVersion + 1 и миграция (ниже).
    /// </remarks>
    public sealed class SaveService
    {
        static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false);
        const string HexDigits = "0123456789abcdef";

        /// <summary>
        /// Миграции payload n → n + 1 (ТЗ 12.1): ключ — исходная версия n. Схема пока одна (v1), таблица пуста.
        /// Шаг получает разобранный payload версии n и возвращает документ версии n + 1; поле SchemaVersion выставляет Migrate.
        /// Каждая миграция тестируется на фикстурах Assets/Game/Tests/Fixtures/Saves/.
        /// </summary>
        static readonly Dictionary<int, Func<JObject, JObject>> s_Migrations = new Dictionary<int, Func<JObject, JObject>>();

        public static IReadOnlyDictionary<int, Func<JObject, JObject>> Migrations => s_Migrations;

        readonly string m_Root;
        readonly IFileSystem m_Fs;

        // Буферы записи (ТЗ 15.3: автосохранение не должно выделять сотни КБ на каждую запись).
        JsonSerializer m_Serializer;
        SHA256 m_Sha;
        readonly CharBufferWriter m_PayloadChars = new CharBufferWriter(16 * 1024);
        readonly CharBufferWriter m_EnvelopeChars = new CharBufferWriter(16 * 1024);
        byte[] m_PayloadBytes = new byte[16 * 1024];
        byte[] m_EnvelopeBytes = new byte[16 * 1024];
        readonly char[] m_Digits = new char[20];
        readonly Dictionary<string, string[]> m_SlotPaths = new Dictionary<string, string[]>();

        public SaveService(string rootDirectory, IFileSystem fileSystem = null)
        {
            m_Root = rootDirectory;
            m_Fs = fileSystem ?? new RealFileSystem();
        }

        /// <summary>Причина последнего неудачного переименования в Quarantine; null — последнее прошло успешно.</summary>
        public string LastQuarantineError { get; private set; }

        public static JsonSerializerSettings Settings()
        {
            var s = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                Culture = CultureInfo.InvariantCulture,
                FloatFormatHandling = FloatFormatHandling.String,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            };
            s.Converters.Add(new GameNumberConverter());
            return s;
        }

        public string MainPath(string slot) => Path.Combine(m_Root, "save_" + slot.ToLowerInvariant() + ".json");

        /// <summary>main, tmp, bak слота — строки создаются один раз.</summary>
        string[] SlotPaths(string slot)
        {
            if (!m_SlotPaths.TryGetValue(slot, out var paths))
            {
                string main = MainPath(slot);
                paths = new[] { main, main + ".tmp", main + ".bak" };
                m_SlotPaths[slot] = paths;
            }
            return paths;
        }

        /// <summary>
        /// Снимок в JSON. Совпадает с JsonConvert.SerializeObject(state, Settings()) байт в байт, но NaN и ±Infinity
        /// не пропускает: InvalidDataException с путём поля (ТЗ 12.1).
        /// </summary>
        public static string SerializeState(GameState state)
        {
            var serializer = JsonSerializer.CreateDefault(Settings());
            var sw = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var writer = new FiniteJsonTextWriter(sw))
            {
                writer.Formatting = serializer.Formatting;
                serializer.Serialize(writer, state);
            }
            return sw.ToString();
        }

        public static GameState DeserializeState(string json) => JsonConvert.DeserializeObject<GameState>(json, Settings());

        public static string Sha256Hex(string text) => BalanceRepository.ComputeSha256(new UTF8Encoding(false).GetBytes(text));

        /// <summary>
        /// Приводит payload версии <paramref name="schemaVersion"/> к GameState.CurrentSchemaVersion по таблице миграций.
        /// Текущая версия возвращается без изменений (без разбора и повторной сериализации).
        /// Нет шага миграции или версия вне 1..текущей — InvalidDataException.
        /// </summary>
        public static string Migrate(string payload, int schemaVersion)
        {
            if (schemaVersion < 1 || schemaVersion > GameState.CurrentSchemaVersion)
                throw new InvalidDataException("неизвестная версия схемы " + schemaVersion.ToString(CultureInfo.InvariantCulture));
            if (schemaVersion == GameState.CurrentSchemaVersion) return payload;
            var doc = MigrateDocument(ParsePayload(payload), schemaVersion, GameState.CurrentSchemaVersion, s_Migrations);
            return doc.ToString(Formatting.None);
        }

        /// <summary>Применяет шаги from → from + 1 → … → to; после каждого шага выставляет SchemaVersion документа.</summary>
        public static JObject MigrateDocument(JObject doc, int fromVersion, int toVersion, IReadOnlyDictionary<int, Func<JObject, JObject>> steps)
        {
            if (doc == null) throw new InvalidDataException("payload пуст");
            for (int v = fromVersion; v < toVersion; v++)
            {
                if (steps == null || !steps.TryGetValue(v, out var step) || step == null)
                    throw new InvalidDataException("нет миграции схемы сохранения " + v.ToString(CultureInfo.InvariantCulture) + " → " + (v + 1).ToString(CultureInfo.InvariantCulture));
                doc = step(doc) ?? throw new InvalidDataException("миграция " + v.ToString(CultureInfo.InvariantCulture) + " вернула пустой документ");
                doc["SchemaVersion"] = v + 1;
            }
            return doc;
        }

        /// <summary>Разбор payload без преобразования строк-дат и с double для дробных чисел.</summary>
        public static JObject ParsePayload(string payload)
        {
            using (var reader = new JsonTextReader(new StringReader(payload))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                Culture = CultureInfo.InvariantCulture,
            })
            {
                return JObject.Load(reader);
            }
        }

        /// <summary>Конверт сохранения как новый массив (для тестов и инструментов; Save пишет из буфера без копии).</summary>
        public byte[] BuildEnvelope(GameState state, long nowUtcMs)
        {
            int length = WriteEnvelope(state, nowUtcMs);
            var copy = new byte[length];
            Buffer.BlockCopy(m_EnvelopeBytes, 0, copy, 0, length);
            return copy;
        }

        /// <summary>
        /// Сериализует снимок (с проверкой NaN/∞) и конверт в m_EnvelopeBytes; возвращает длину.
        /// Байты совпадают с прежней сборкой через JObject: { "schemaVersion", "rulesVersion", "balanceSha256", "slot",
        /// "savedAtUtcMs", "payloadSha256", "payload" } без пробелов, экранирование строк как у Newtonsoft по умолчанию.
        /// </summary>
        int WriteEnvelope(GameState state, long nowUtcMs)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (m_Serializer == null) m_Serializer = JsonSerializer.CreateDefault(Settings());
            if (m_Sha == null) m_Sha = SHA256.Create();

            // 1. payload в буфер символов.
            var payload = m_PayloadChars;
            payload.Clear();
            using (var writer = new FiniteJsonTextWriter(payload) { CloseOutput = false })
            {
                writer.Formatting = m_Serializer.Formatting;
                m_Serializer.Serialize(writer, state);
            }

            // 2. SHA-256 от UTF-8 байтов payload.
            int payloadByteCount = Encode(payload, ref m_PayloadBytes);
            byte[] digest = m_Sha.ComputeHash(m_PayloadBytes, 0, payloadByteCount);

            // 3. Конверт.
            var env = m_EnvelopeChars;
            env.Clear();
            env.Write("{\"schemaVersion\":");
            AppendInteger(env, state.SchemaVersion);
            env.Write(",\"rulesVersion\":");
            AppendInteger(env, state.RulesVersion);
            env.Write(",\"balanceSha256\":");
            AppendJsonString(env, state.BalanceSha256);
            env.Write(",\"slot\":");
            AppendJsonString(env, state.Slot);
            env.Write(",\"savedAtUtcMs\":");
            AppendInteger(env, nowUtcMs);
            env.Write(",\"payloadSha256\":\"");
            for (int i = 0; i < digest.Length; i++)
            {
                env.Write(HexDigits[digest[i] >> 4]);
                env.Write(HexDigits[digest[i] & 0xF]);
            }
            env.Write("\",\"payload\":");
            AppendJsonString(env, payload.Chars, 0, payload.Length);
            env.Write('}');

            return Encode(env, ref m_EnvelopeBytes);
        }

        static int Encode(CharBufferWriter chars, ref byte[] bytes)
        {
            int count = s_Utf8.GetByteCount(chars.Chars, 0, chars.Length);
            if (bytes.Length < count) bytes = new byte[Math.Max(count, bytes.Length * 2)];
            return s_Utf8.GetBytes(chars.Chars, 0, chars.Length, bytes, 0);
        }

        void AppendInteger(CharBufferWriter w, long value)
        {
            if (value == 0)
            {
                w.Write('0');
                return;
            }
            ulong u;
            if (value < 0)
            {
                w.Write('-');
                u = (ulong)(-(value + 1)) + 1UL;
            }
            else
            {
                u = (ulong)value;
            }
            int n = m_Digits.Length;
            while (u != 0)
            {
                m_Digits[--n] = (char)('0' + (int)(u % 10UL));
                u /= 10UL;
            }
            w.Append(m_Digits, n, m_Digits.Length - n);
        }

        static void AppendJsonString(CharBufferWriter w, string s)
        {
            if (s == null)
            {
                w.Write("null");
                return;
            }
            w.Write('"');
            int run = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!NeedsEscape(c)) continue;
                w.Append(s, run, i - run);
                AppendEscape(w, c);
                run = i + 1;
            }
            w.Append(s, run, s.Length - run);
            w.Write('"');
        }

        static void AppendJsonString(CharBufferWriter w, char[] s, int start, int count)
        {
            w.Write('"');
            int run = start, end = start + count;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (!NeedsEscape(c)) continue;
                w.Append(s, run, i - run);
                AppendEscape(w, c);
                run = i + 1;
            }
            w.Append(s, run, end - run);
            w.Write('"');
        }

        /// <summary>Те же символы, что экранирует JsonTextWriter по умолчанию (StringEscapeHandling.Default, кавычка ").</summary>
        static bool NeedsEscape(char c) => c < ' ' || c == '"' || c == '\\' || c == '\u0085' || c == '\u2028' || c == '\u2029';

        static void AppendEscape(CharBufferWriter w, char c)
        {
            switch (c)
            {
                case '\t': w.Write("\\t"); return;
                case '\n': w.Write("\\n"); return;
                case '\r': w.Write("\\r"); return;
                case '\f': w.Write("\\f"); return;
                case '\b': w.Write("\\b"); return;
                case '\\': w.Write("\\\\"); return;
                case '"': w.Write("\\\""); return;
                default:
                    w.Write("\\u");
                    w.Write(HexDigits[(c >> 12) & 0xF]);
                    w.Write(HexDigits[(c >> 8) & 0xF]);
                    w.Write(HexDigits[(c >> 4) & 0xF]);
                    w.Write(HexDigits[c & 0xF]);
                    return;
            }
        }

        /// <summary>
        /// Запись по ТЗ 12.2: снимок в память (NaN/∞ — исключение до любых файловых операций), tmp через FileStream
        /// с Flush(true), затем Replace(tmp, main, bak) или Move; при сбое Replace — Copy / Delete / Move.
        /// </summary>
        public void Save(GameState state, long nowUtcMs)
        {
            int length = WriteEnvelope(state, nowUtcMs);
            m_Fs.CreateDirectory(m_Root);
            var paths = SlotPaths(state.Slot);
            string main = paths[0], tmp = paths[1], bak = paths[2];
            if (m_Fs is IDurableBufferWriter buffered)
            {
                buffered.WriteAllBytesDurable(tmp, m_EnvelopeBytes, length);
            }
            else
            {
                var data = new byte[length];
                Buffer.BlockCopy(m_EnvelopeBytes, 0, data, 0, length);
                m_Fs.WriteAllBytesDurable(tmp, data);
            }
            if (m_Fs.Exists(main))
            {
                try
                {
                    m_Fs.Replace(tmp, main, bak);
                }
                catch (Exception)
                {
                    m_Fs.Copy(main, bak);
                    m_Fs.Delete(main);
                    m_Fs.Move(tmp, main);
                }
            }
            else
            {
                m_Fs.Move(tmp, main);
            }
        }

        /// <summary>
        /// Загрузка по ТЗ 12.1–12.2: main → tmp → bak, первый файл с валидными payloadSha256 и schemaVersion.
        /// Повреждённые файлы переименовываются в *.corrupt-&lt;UTC&gt;; файл другого баланса — в *.incompatible-&lt;hash8&gt;,
        /// вместе с ним откладываются оставшиеся tmp/bak слота, чтобы первая новая запись их не перезаписала.
        /// </summary>
        public LoadResult Load(string slot, string expectedBalanceSha)
        {
            var candidates = SlotPaths(slot);
            var result = new LoadResult();
            var notes = new List<string>();      // причины отказа файлов и пояснение статуса
            var failures = new List<string>();   // неудачные переименования: файл остался на месте
            bool anyExists = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (!m_Fs.Exists(path)) continue;
                anyExists = true;
                if (result.SourcePath == null) result.SourcePath = path;
                if (!TryRead(path, out var env, out int schemaVersion, out string payload, out string error))
                {
                    notes.Add(Path.GetFileName(path) + ": " + error);
                    QuarantineInto(result, failures, path, "corrupt");
                    continue;
                }
                string sha = (string)env["balanceSha256"];
                if (!string.Equals(sha, expectedBalanceSha, StringComparison.OrdinalIgnoreCase))
                {
                    string tag = HashTag(sha);
                    result.Status = LoadStatus.IncompatibleBalance;
                    result.SourcePath = path;
                    result.State = null;
                    QuarantineIncompatible(result, failures, path, tag);
                    for (int j = i + 1; j < candidates.Length; j++)
                        QuarantineLeftover(result, failures, candidates[j], expectedBalanceSha);
                    result.Message = "Сохранение создано для другого баланса (" + tag + ")" +
                                     (failures.Count > 0 ? "; " + string.Join("; ", failures) : "");
                    return result;
                }
                GameState state;
                try
                {
                    payload = Migrate(payload, schemaVersion);
                    state = DeserializeState(payload);
                    if (state == null) throw new InvalidDataException("payload пуст");
                }
                catch (Exception e)
                {
                    notes.Add(Path.GetFileName(path) + ": " + e.Message);
                    QuarantineInto(result, failures, path, "corrupt");
                    continue;
                }
                result.Status = i == 0 ? LoadStatus.Loaded : LoadStatus.LoadedFromBackup;
                if (state.RulesVersion != Formula.RulesVersion)
                {
                    result.Status = LoadStatus.RulesChangedMineReset;
                    notes.Add("rulesVersion сохранения " + state.RulesVersion.ToString(CultureInfo.InvariantCulture) +
                              ", текущий " + Formula.RulesVersion.ToString(CultureInfo.InvariantCulture));
                }
                result.State = state;
                result.SourcePath = path;
                result.Message = JoinNotes(notes, failures);
                return result;
            }
            result.Status = anyExists ? LoadStatus.Corrupted : LoadStatus.NoSave;
            result.Message = JoinNotes(notes, failures);
            return result;
        }

        static string JoinNotes(List<string> notes, List<string> failures)
        {
            if (notes.Count == 0 && failures.Count == 0) return null;
            var all = new List<string>(notes);
            all.AddRange(failures);
            return string.Join("; ", all);
        }

        static string HashTag(string sha) => sha != null && sha.Length >= 8 ? sha.Substring(0, 8) : "unknown";

        void QuarantineInto(LoadResult result, List<string> failures, string path, string reason)
        {
            string moved = Quarantine(path, reason);
            if (moved != null) result.QuarantinedFiles.Add(moved);
            else failures.Add("не удалось переименовать " + LastQuarantineError);
        }

        void QuarantineIncompatible(LoadResult result, List<string> failures, string path, string tag)
        {
            string moved = QuarantineAs(path, "incompatible-" + tag);
            if (moved != null) result.QuarantinedFiles.Add(moved);
            else failures.Add("не удалось переименовать " + LastQuarantineError);
        }

        /// <summary>
        /// Оставшийся tmp/bak слота после отказа по балансу: другой баланс — *.incompatible-&lt;hash8&gt;, повреждён — *.corrupt-&lt;UTC&gt;,
        /// подходит к текущему балансу (старее отвергнутого основного) — *.leftover-&lt;UTC&gt;.
        /// </summary>
        void QuarantineLeftover(LoadResult result, List<string> failures, string path, string expectedBalanceSha)
        {
            if (!m_Fs.Exists(path)) return;
            if (!TryRead(path, out var env, out _, out _, out _))
            {
                string moved = Quarantine(path, "corrupt");
                if (moved != null) result.QuarantinedFiles.Add(moved);
                else failures.Add("не удалось переименовать " + LastQuarantineError);
                return;
            }
            string sha = (string)env["balanceSha256"];
            if (!string.Equals(sha, expectedBalanceSha, StringComparison.OrdinalIgnoreCase))
            {
                QuarantineIncompatible(result, failures, path, HashTag(sha));
                return;
            }
            string kept = Quarantine(path, "leftover");
            if (kept != null) result.QuarantinedFiles.Add(kept);
            else failures.Add("не удалось переименовать " + LastQuarantineError);
        }

        /// <summary>
        /// Откладывает файл, не удаляя его (ТЗ 12.2): переименование в path + "." + reason + "-" + UTC yyyyMMddHHmmss;
        /// если такое имя занято — с суффиксом -2, -3, …. Возвращает новый путь или null при неудаче,
        /// причина неудачи — в <see cref="LastQuarantineError"/>.
        /// </summary>
        public string Quarantine(string path, string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "quarantine";
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            return QuarantineAs(path, reason + "-" + stamp);
        }

        string QuarantineAs(string path, string suffix)
        {
            LastQuarantineError = null;
            try
            {
                if (string.IsNullOrEmpty(path)) throw new ArgumentException("путь не задан", nameof(path));
                string baseName = path + "." + suffix;
                string target = baseName;
                for (int n = 2; m_Fs.Exists(target); n++)
                {
                    if (n > 1000) throw new IOException("нет свободного имени для " + baseName);
                    target = baseName + "-" + n.ToString(CultureInfo.InvariantCulture);
                }
                m_Fs.Move(path, target);
                return target;
            }
            catch (Exception e)
            {
                LastQuarantineError = (path ?? "null") + ": " + e.Message;
                return null;
            }
        }

        bool TryRead(string path, out JObject env, out int schemaVersion, out string payload, out string error)
        {
            env = null;
            schemaVersion = 0;
            payload = null;
            error = null;
            try
            {
                string text = new UTF8Encoding(false, true).GetString(m_Fs.ReadAllBytes(path));
                env = JObject.Parse(text);
                schemaVersion = (int)env["schemaVersion"];
                if (schemaVersion < 1 || schemaVersion > GameState.CurrentSchemaVersion)
                {
                    error = "неизвестная версия схемы " + schemaVersion.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                payload = (string)env["payload"];
                string sha = (string)env["payloadSha256"];
                if (payload == null || !string.Equals(Sha256Hex(payload), sha, StringComparison.OrdinalIgnoreCase))
                {
                    error = "контрольная сумма не совпадает";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
