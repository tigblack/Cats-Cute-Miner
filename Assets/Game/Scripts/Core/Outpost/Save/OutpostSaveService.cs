using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CatMiners.Core.Outpost
{
    public enum OutpostSaveSlot
    {
        Main,
        Debug,
    }

    public enum OutpostLoadStatus
    {
        /// <summary>Файлов нет — можно начать новый профиль.</summary>
        NoSave,
        Loaded,
        LoadedFromBackup,
        /// <summary>Будущая/несовместимая схема, режим, правила или источник: файлы скопированы в карантин, ничего не перезаписано.</summary>
        Incompatible,
        /// <summary>Повреждены все кандидаты: файлы скопированы в карантин, ничего не перезаписано.</summary>
        Corrupt,
        /// <summary>
        /// Сохранение прежней схемы (конверт SchemaVersion &lt; <see cref="OutpostIds.SchemaVersion"/>, ТЗ 2.2 §10.3): не ошибка.
        /// Прогресс не переносится — вызывающий делает <see cref="OutpostSaveService.ResetOutdated(OutpostSaveSlot, CatMiners.Core.Data.BalanceRepository, OutpostConfigSet, ulong, long)"/>
        /// (архив pre-factories/ и новая кампания). State = null; Envelope и CommitSequence — старого кандидата. Ничего не перезаписано.
        /// </summary>
        Outdated,
    }

    /// <summary>Конверт сохранения (ТЗ 17): метаданные, монотонный CommitSequence и SHA-256 строки Payload.</summary>
    public sealed class OutpostSaveEnvelope
    {
        public string ModeId = OutpostIds.ModeId;
        public int SchemaVersion = OutpostIds.SchemaVersion;
        public string RulesVersion = OutpostIds.RulesVersion;
        public string BalanceVersion;
        public string SourceSha256;
        public string CatalogHash;
        public long CommitSequence;
        public string PayloadSha256;
        /// <summary>JSON OutpostGameState одной строкой.</summary>
        public string Payload;
    }

    public sealed class OutpostLoadResult
    {
        public OutpostLoadStatus Status;
        public OutpostGameState State;
        public OutpostSaveEnvelope Envelope;
        public long CommitSequence = -1;
        /// <summary>Файл, из которого загружено состояние.</summary>
        public string SourcePath;
        /// <summary>Каталог карантина (Incompatible/Corrupt или отвергнутый кандидат) либо null.</summary>
        public string QuarantineDirectory;
        /// <summary>Причины отказа кандидатов и пояснения.</summary>
        public readonly List<string> Messages = new List<string>();
        /// <summary>Каталог экипировки/наград сохранения отличается от текущего (не ошибка: предметы сохраняются).</summary>
        public bool CatalogChanged;
        /// <summary>Что переносится из профиля устаревшего сохранения (только при <see cref="OutpostLoadStatus.Outdated"/>).</summary>
        internal OutdatedProfileCarry Outdated;
    }

    /// <summary>
    /// Переносимая часть профиля сохранения схемы &lt; 3 (ТЗ 2.2 §10.3), прочитанная из JSON (JObject), а не из класса профиля:
    /// Settings целиком, PlayTimeMs, журнал платежей Payments и намерение PendingPurchase. Журнал проверен PaymentRules.Ledger.
    /// </summary>
    sealed class OutdatedProfileCarry
    {
        public OutpostSettings Settings;
        public long PlayTimeMs;
        /// <summary>null — поля нет (схема 1).</summary>
        public List<PaymentDelivery> Payments;
        public PurchaseIntent PendingPurchase;

        /// <summary>Разбор старого профиля. Ошибка журнала платежей — исключение (файл считается повреждённым), настроек — значения по умолчанию.</summary>
        public static OutdatedProfileCarry Read(JObject profile, int schemaVersion)
        {
            if (profile == null) throw new InvalidDataException("payload без профиля");
            if (PaymentRules.String(profile, "ModeId") != OutpostIds.ModeId)
                throw new InvalidDataException("ModeId payload " + (PaymentRules.String(profile, "ModeId") ?? "null"));
            // Схема 2 обязана содержать журнал платежей (как раньше): без него файл — Corrupt, а не сброс с потерей покупок.
            if (schemaVersion >= 2 && (!(profile["Payments"] is JArray) || profile.Property("PendingPurchase") == null))
                throw new InvalidDataException("schema 2 payment ledger fields missing");
            var serializer = JsonSerializer.Create(OutpostJson.Settings);
            var carry = new OutdatedProfileCarry();
            try
            {
                if (profile["Settings"] is JObject settings) carry.Settings = settings.ToObject<OutpostSettings>(serializer);
            }
            catch (Exception)
            {
                carry.Settings = null; // настройки не стоят отказа: остаются значения по умолчанию
            }
            // Отрицательное или нецелое значение не переносится (запись отклоняет PlayTimeMs < 0).
            if (profile["PlayTimeMs"] is JValue play && play.Value is long ms && ms > 0) carry.PlayTimeMs = ms;
            if (profile["Payments"] is JArray payments) carry.Payments = payments.ToObject<List<PaymentDelivery>>(serializer);
            var intent = profile["PendingPurchase"];
            if (intent != null && intent.Type != JTokenType.Null) carry.PendingPurchase = intent.ToObject<PurchaseIntent>(serializer);
            PaymentRules.Ledger(new OutpostProfile { Payments = carry.Payments ?? new List<PaymentDelivery>(), PendingPurchase = carry.PendingPurchase });
            return carry;
        }

        /// <summary>Перенос в профиль новой кампании: Settings (LegacySettingsImported = true), PlayTimeMs, Payments, PendingPurchase.</summary>
        public void ApplyTo(OutpostProfile p)
        {
            if (Settings != null)
            {
                Settings.Master = Volume(Settings.Master);
                Settings.Music = Volume(Settings.Music);
                Settings.Sfx = Volume(Settings.Sfx);
                Settings.Ambience = Volume(Settings.Ambience);
                p.Settings = Settings;
            }
            if (p.Settings == null) p.Settings = new OutpostSettings();
            p.Settings.LegacySettingsImported = true;
            p.PlayTimeMs = PlayTimeMs;
            if (Payments != null) p.Payments = Payments;
            p.PendingPurchase = PendingPurchase;
        }

        static float Volume(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 1f : Math.Max(0f, Math.Min(1f, v));
    }

    public sealed class OutpostSaveResult
    {
        public bool Ok;
        public long CommitSequence;
        public string Error;
        /// <summary>Шаг, на котором запись прервалась: "validate", "tmp", "replace", "fallback", "move", "reset-backup".</summary>
        public string FailedStep;
    }

    /// <summary>
    /// Атомарные сохранения режима (ТЗ 17, CORE_SPEC §12): {persistent}/CatOutpostV2/{main|debug}/state.json (+ .tmp, .bak).
    /// Запись: сериализовать, разобрать обратно, проверить инварианты, tmp с Flush(true), затем Replace(tmp, main, bak)
    /// либо (нет Replace) main → bak, tmp → main. Загрузка: валидные main/bak, наибольший CommitSequence; tmp не commit.
    /// Повреждённые/несовместимые файлы не перезаписываются: копии — в quarantine-{utc}. Все операции — через IFileSystem.
    /// </summary>
    public sealed class OutpostSaveService
    {
        public const string RootFolder = "CatOutpostV2";
        public const string StateFile = "state.json";
        public const string LegacyArchiveFolder = "legacy-archive";
        /// <summary>Каталог слота с копиями main/bak/tmp сохранения схемы &lt; 3 перед новой кампанией (ТЗ 2.2 §10.3).</summary>
        public const string PreFactoriesFolder = "pre-factories";

        static readonly string[] s_LegacyFiles =
        {
            "save_main.json", "save_main.json.tmp", "save_main.json.bak",
            "save_debug.json", "save_debug.json.tmp", "save_debug.json.bak",
        };

        readonly string m_Persistent;
        readonly IFileSystem m_Fs;
        readonly Dictionary<OutpostSaveSlot, long> m_LastSequence = new Dictionary<OutpostSaveSlot, long>();
        // Если загрузка выбрала bak, он остаётся последним валидным commit до успешной записи нового main.
        readonly HashSet<OutpostSaveSlot> m_PreserveBackup = new HashSet<OutpostSaveSlot>();

        /// <param name="persistentRoot">Application.persistentDataPath (или временный каталог тестов).</param>
        public OutpostSaveService(string persistentRoot, IFileSystem fileSystem = null)
        {
            m_Persistent = persistentRoot ?? throw new ArgumentNullException(nameof(persistentRoot));
            m_Fs = fileSystem ?? new RealFileSystem();
        }

        /// <summary>Проверка инвариантов перед записью и при загрузке (по умолчанию включена).</summary>
        public bool ValidateInvariants = true;

        /// <summary>Резолвер карты для полной проверки инвариантов при загрузке (необязательно).</summary>
        public Func<OutpostGameState, OutpostMap> MapResolver;

        /// <summary>Текущие конфигурации: hash каталога для сравнения при загрузке (необязательно).</summary>
        public OutpostConfigSet Configs;

        public string SlotDirectory(OutpostSaveSlot slot) =>
            Path.Combine(m_Persistent, RootFolder, slot == OutpostSaveSlot.Main ? "main" : "debug");

        public string MainPath(OutpostSaveSlot slot) => Path.Combine(SlotDirectory(slot), StateFile);
        public string TmpPath(OutpostSaveSlot slot) => MainPath(slot) + ".tmp";
        public string BakPath(OutpostSaveSlot slot) => MainPath(slot) + ".bak";
        /// <summary>CatOutpostV2/&lt;main|debug&gt;/pre-factories — архив сохранения схемы &lt; 3 (<see cref="ResetOutdated(OutpostSaveSlot, BalanceRepository, OutpostConfigSet, ulong, long)"/>).</summary>
        public string PreFactoriesDirectory(OutpostSaveSlot slot) => Path.Combine(SlotDirectory(slot), PreFactoriesFolder);

        /// <summary>Последний известный CommitSequence слота (после Load/Save); −1 — неизвестен.</summary>
        public long LastCommitSequence(OutpostSaveSlot slot) => m_LastSequence.TryGetValue(slot, out long s) ? s : -1;

        // ---------- Сериализация ----------

        public static string SerializeState(OutpostGameState state) => OutpostJson.Serialize(state);

        public static OutpostGameState DeserializeState(string payload) => OutpostJson.Deserialize<OutpostGameState>(payload);

        public static OutpostSaveEnvelope BuildEnvelope(OutpostGameState state, string payload, long commitSequence, string catalogHash)
        {
            return new OutpostSaveEnvelope
            {
                BalanceVersion = state.Run?.Balance?.Version,
                SourceSha256 = state.Run?.SourceSha256,
                CatalogHash = catalogHash,
                CommitSequence = commitSequence,
                PayloadSha256 = OutpostJson.Sha256Hex(payload),
                Payload = payload,
            };
        }

        public static string SerializeEnvelope(OutpostSaveEnvelope env) => JsonConvert.SerializeObject(env, OutpostJson.Settings);

        // ---------- Запись ----------

        /// <summary>
        /// Commit согласованного снимка (граница — конец всех семи фаз отметки). При ошибке состояние в памяти не меняется,
        /// повторять запись того же подготовленного снимка. Номер commit монотонен (после неудачи может пропускаться).
        /// </summary>
        public OutpostSaveResult Save(OutpostGameState state, OutpostSaveSlot slot = OutpostSaveSlot.Main, OutpostMap map = null, bool resetBackup = false)
        {
            var result = new OutpostSaveResult();
            string step = "validate";
            try
            {
                if (state?.Run == null || state.Profile == null) throw new InvalidDataException("неполное состояние");
                PaymentRules.Ledger(state.Profile);
                string payload = SerializeState(state);
                var reparsed = DeserializeState(payload);
                if (reparsed == null || SerializeState(reparsed) != payload) throw new InvalidDataException("снимок не проходит round-trip");
                if (ValidateInvariants)
                {
                    // DebugGoods (DEV «Goods ×100», ТЗ 2.2 §13) допустим только в слоте Debug.
                    var errors = OutpostInvariants.Check(reparsed, map, Configs?.Equipment, Configs?.Cards, slot == OutpostSaveSlot.Debug);
                    if (errors.Count > 0) throw new InvalidDataException("инварианты: " + string.Join("; ", errors));
                }
                long seq = NextSequence(slot);
                m_LastSequence[slot] = seq;
                result.CommitSequence = seq;
                var env = BuildEnvelope(state, payload, seq, Configs?.CatalogHash);
                var bytes = OutpostJson.Utf8(SerializeEnvelope(env));

                string dir = SlotDirectory(slot), main = MainPath(slot), tmp = TmpPath(slot), bak = BakPath(slot);
                step = "tmp";
                m_Fs.CreateDirectory(dir);
                m_Fs.WriteAllBytesDurable(tmp, bytes);
                if (m_Fs.Exists(main))
                {
                    step = "replace";
                    bool preserveBackup = m_PreserveBackup.Contains(slot);
                    bool replaced;
                    try
                    {
                        m_Fs.Replace(tmp, main, preserveBackup ? null : bak);
                        replaced = true;
                    }
                    catch (Exception e) when (e is PlatformNotSupportedException || e is NotSupportedException)
                    {
                        replaced = false;
                    }
                    if (!replaced)
                    {
                        // Каждый шаг восстановим. После восстановления bak нельзя заменять отвергнутым main.
                        // Ошибки ввода-вывода Replace возвращаются вызывающему: Replace мог уже перенести main в bak.
                        step = "fallback";
                        if (preserveBackup) m_Fs.Delete(main);
                        else
                        {
                            if (m_Fs.Exists(bak)) m_Fs.Delete(bak);
                            m_Fs.Move(main, bak);
                        }
                        step = "move";
                        m_Fs.Move(tmp, main);
                    }
                }
                else
                {
                    step = "move";
                    m_Fs.Move(tmp, main);
                }
                if (resetBackup)
                {
                    // Завершённый явный reset не должен восстанавливать старый прогресс из bak.
                    // Записываем тот же новый снимок с Flush(true); при ошибке вызывающий повторяет reset.
                    step = "reset-backup";
                    m_Fs.WriteAllBytesDurable(bak, bytes);
                }
                m_PreserveBackup.Remove(slot);
                result.Ok = true;
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.FailedStep = step;
                result.Error = step + ": " + e.Message;
            }
            return result;
        }

        long NextSequence(OutpostSaveSlot slot)
        {
            if (!m_LastSequence.TryGetValue(slot, out long last) || last < 0)
            {
                last = 0;
                foreach (var path in new[] { MainPath(slot), BakPath(slot), TmpPath(slot) })
                    if (TryReadEnvelope(path, out var env, out _, out _)) last = Math.Max(last, env.CommitSequence);
            }
            return last + 1;
        }

        // ---------- Загрузка ----------

        /// <summary>
        /// Загрузка слота: валидный main/bak с наибольшим CommitSequence (Loaded/LoadedFromBackup). Конверт схемы &lt; 3 с верной
        /// контрольной суммой распознаётся сразу после разбора и контрольной суммы — раньше проверок RulesVersion, карты и
        /// инвариантов — и даёт <see cref="OutpostLoadStatus.Outdated"/>, если валидного кандидата схемы 3 нет (ТЗ 2.2 §10.3).
        /// Будущая схема или чужой режим важнее устаревшего файла (Incompatible: ничего не сбрасывается). Без кандидатов — Corrupt.
        /// </summary>
        public OutpostLoadResult Load(OutpostSaveSlot slot = OutpostSaveSlot.Main)
        {
            var result = new OutpostLoadResult();
            string main = MainPath(slot), bak = BakPath(slot);
            var candidates = new[] { main, bak };
            bool anyExists = false, anyIncompatible = false;
            var rejected = new List<string>();
            OutpostGameState bestState = null;
            OutpostSaveEnvelope bestEnv = null;
            string bestPath = null;
            OutpostSaveEnvelope outdatedEnv = null;
            OutdatedProfileCarry outdatedCarry = null;
            string outdatedPath = null;
            foreach (var path in candidates)
            {
                if (!m_Fs.Exists(path)) continue;
                anyExists = true;
                if (!TryReadEnvelope(path, out var env, out string error, out bool incompatible))
                {
                    result.Messages.Add(Path.GetFileName(path) + ": " + error);
                    anyIncompatible |= incompatible;
                    rejected.Add(path);
                    continue;
                }
                if (env.SchemaVersion < OutpostIds.SchemaVersion)
                {
                    // ТЗ 2.2 §10.3: прежняя схема — не ошибка; прогресс не читается, только переносимые поля профиля.
                    OutdatedProfileCarry carry;
                    try
                    {
                        carry = OutdatedProfileCarry.Read(ParsePayload(env.Payload)["Profile"] as JObject, env.SchemaVersion);
                    }
                    catch (Exception e)
                    {
                        result.Messages.Add(Path.GetFileName(path) + ": " + e.Message);
                        rejected.Add(path);
                        continue;
                    }
                    result.Messages.Add(Path.GetFileName(path) + ": SchemaVersion " + env.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                        " < " + OutpostIds.SchemaVersion.ToString(CultureInfo.InvariantCulture) + " (outdated)");
                    if (outdatedEnv == null || env.CommitSequence > outdatedEnv.CommitSequence)
                    {
                        outdatedEnv = env;
                        outdatedCarry = carry;
                        outdatedPath = path;
                    }
                    continue;
                }
                OutpostGameState state;
                try
                {
                    var paymentProfile = JObject.Parse(env.Payload)["Profile"] as JObject;
                    if (env.SchemaVersion >= 2 && (paymentProfile == null || !(paymentProfile["Payments"] is JArray) || paymentProfile.Property("PendingPurchase") == null))
                        throw new InvalidDataException("schema 2 payment ledger fields missing");
                    state = DeserializeState(env.Payload);
                    if (state?.Run == null || state.Profile == null) throw new InvalidDataException("payload без профиля или run");
                    if (state.Profile.ModeId != OutpostIds.ModeId) throw new InvalidDataException("ModeId payload " + state.Profile.ModeId);
                    if (state.Profile.SchemaVersion < 1 || state.Profile.SchemaVersion > OutpostIds.SchemaVersion)
                    {
                        anyIncompatible = true;
                        throw new InvalidDataException("SchemaVersion payload " + state.Profile.SchemaVersion);
                    }
                    state.Profile.SchemaVersion = OutpostIds.SchemaVersion;
                    PaymentRules.Ledger(state.Profile);
                    if (state.Run.RulesVersion != OutpostIds.RulesVersion)
                    {
                        anyIncompatible = true;
                        throw new InvalidDataException("RulesVersion " + state.Run.RulesVersion + " ≠ " + OutpostIds.RulesVersion);
                    }
                    if (ValidateInvariants)
                    {
                        OutpostMap map = null;
                        try
                        {
                            map = MapResolver?.Invoke(state);
                        }
                        catch (Exception e)
                        {
                            throw new InvalidDataException("карта: " + e.Message);
                        }
                        var errors = OutpostInvariants.Check(state, map, Configs?.Equipment, Configs?.Cards, slot == OutpostSaveSlot.Debug);
                        if (errors.Count > 0) throw new InvalidDataException("инварианты: " + string.Join("; ", errors));
                    }
                }
                catch (Exception e)
                {
                    result.Messages.Add(Path.GetFileName(path) + ": " + e.Message);
                    rejected.Add(path);
                    continue;
                }
                if (bestEnv == null || env.CommitSequence > bestEnv.CommitSequence)
                {
                    bestEnv = env;
                    bestState = state;
                    bestPath = path;
                }
            }

            if (bestState != null)
            {
                // Устаревший кандидат рядом с валидным (сбой после замены main при сбросе): копия в карантин, как у отвергнутых.
                if (outdatedPath != null) rejected.Add(outdatedPath);
                result.Status = bestPath == main ? OutpostLoadStatus.Loaded : OutpostLoadStatus.LoadedFromBackup;
                result.State = bestState;
                result.Envelope = bestEnv;
                result.CommitSequence = bestEnv.CommitSequence;
                result.SourcePath = bestPath;
                result.CatalogChanged = Configs != null && bestEnv.CatalogHash != null && bestEnv.CatalogHash != Configs.CatalogHash;
                m_LastSequence[slot] = Math.Max(bestEnv.CommitSequence, LastCommitSequence(slot));
                if (bestPath == bak) m_PreserveBackup.Add(slot);
                else m_PreserveBackup.Remove(slot);
                if (rejected.Count > 0) result.QuarantineDirectory = QuarantineCopy(slot, rejected, result.Messages);
                return result;
            }
            if (!anyExists)
            {
                result.Status = OutpostLoadStatus.NoSave;
                return result;
            }
            if (outdatedEnv != null && !anyIncompatible)
            {
                // Ничего не копируется и не перезаписывается: архив pre-factories/ и новую кампанию делает ResetOutdated.
                result.Status = OutpostLoadStatus.Outdated;
                result.Envelope = outdatedEnv;
                result.CommitSequence = outdatedEnv.CommitSequence;
                result.SourcePath = outdatedPath;
                result.Outdated = outdatedCarry;
                m_LastSequence[slot] = Math.Max(outdatedEnv.CommitSequence, LastCommitSequence(slot));
                m_PreserveBackup.Remove(slot);
                return result;
            }
            result.Status = anyIncompatible ? OutpostLoadStatus.Incompatible : OutpostLoadStatus.Corrupt;
            var all = new List<string>();
            foreach (var p in new[] { main, bak, TmpPath(slot) })
                if (m_Fs.Exists(p)) all.Add(p);
            result.QuarantineDirectory = QuarantineCopy(slot, all, result.Messages);
            return result;
        }

        /// <summary>Payload как JSON без преобразования дат и с double для дробей (значения переносятся как есть).</summary>
        static JObject ParsePayload(string payload)
        {
            using (var reader = new JsonTextReader(new StringReader(payload)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            {
                var token = JToken.ReadFrom(reader);
                if (!(token is JObject obj)) throw new InvalidDataException("payload не объект");
                return obj;
            }
        }

        /// <summary>
        /// Конверт файла: разбор, ModeId, SchemaVersion, PayloadSha256, RulesVersion. incompatible — будущая схема/чужой режим/правила.
        /// Конверт прежней схемы (1 ≤ SchemaVersion &lt; текущей) с верной суммой принимается без проверки RulesVersion — его
        /// разбирает ветка Outdated (ТЗ 2.2 §10.3); его CommitSequence продолжает нумерацию.
        /// </summary>
        bool TryReadEnvelope(string path, out OutpostSaveEnvelope env, out string error, out bool incompatible)
        {
            env = null;
            error = null;
            incompatible = false;
            try
            {
                if (!m_Fs.Exists(path))
                {
                    error = "нет файла";
                    return false;
                }
                string text = new UTF8Encoding(false, true).GetString(m_Fs.ReadAllBytes(path));
                var read = JsonConvert.DeserializeObject<OutpostSaveEnvelope>(text, OutpostJson.Settings);
                if (read == null) throw new InvalidDataException("пустой конверт");
                if (read.ModeId != OutpostIds.ModeId)
                {
                    incompatible = true;
                    error = "ModeId " + (read.ModeId ?? "null");
                    return false;
                }
                if (read.SchemaVersion < 1 || read.SchemaVersion > OutpostIds.SchemaVersion)
                {
                    incompatible = read.SchemaVersion > OutpostIds.SchemaVersion || read.SchemaVersion < 1;
                    error = "SchemaVersion " + read.SchemaVersion.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                env = read;
                if (env.Payload == null || !string.Equals(OutpostJson.Sha256Hex(env.Payload), env.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = "контрольная сумма payload не совпадает";
                    env = null;
                    return false;
                }
                if (env.SchemaVersion < OutpostIds.SchemaVersion) return true;
                if (env.RulesVersion != OutpostIds.RulesVersion)
                {
                    incompatible = true;
                    error = "RulesVersion " + env.RulesVersion;
                    env = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                env = null;
                return false;
            }
        }

        /// <summary>Копии файлов в {slot}/quarantine-{utc}; оригиналы не трогаются. Возвращает каталог или null.</summary>
        string QuarantineCopy(OutpostSaveSlot slot, List<string> files, List<string> messages)
        {
            if (files.Count == 0) return null;
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            string dir = Path.Combine(SlotDirectory(slot), "quarantine-" + stamp);
            for (int n = 2; m_Fs.Exists(Path.Combine(dir, StateFile)) && n < 1000; n++)
                dir = Path.Combine(SlotDirectory(slot), "quarantine-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture));
            try
            {
                m_Fs.CreateDirectory(dir);
                foreach (var f in files)
                {
                    string target = Path.Combine(dir, Path.GetFileName(f));
                    if (!m_Fs.Exists(target)) m_Fs.Copy(f, target);
                }
                return dir;
            }
            catch (Exception e)
            {
                messages.Add("карантин не создан: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Однократная архивная копия старых save_main/save_debug (и их tmp/bak) в CatOutpostV2/legacy-archive/.
        /// Оригиналы остаются на месте; уже скопированные файлы не перезаписываются. Возвращает число скопированных.
        /// </summary>
        public int ArchiveLegacySaves(string legacyRoot)
        {
            string dir = Path.Combine(m_Persistent, RootFolder, LegacyArchiveFolder);
            int copied = 0;
            foreach (var name in s_LegacyFiles)
            {
                string src = Path.Combine(legacyRoot, name);
                if (!m_Fs.Exists(src)) continue;
                string dst = Path.Combine(dir, name);
                if (m_Fs.Exists(dst)) continue;
                m_Fs.CreateDirectory(dir);
                m_Fs.Copy(src, dst);
                copied++;
            }
            return copied;
        }

        // ---------- Сброс сохранения прежней схемы (ТЗ 2.2 §10.3, Р9) ----------

        /// <summary>
        /// Новая кампания вместо сохранения схемы &lt; 3 (статус <see cref="OutpostLoadStatus.Outdated"/>). Прогресс не переносится.
        /// Порядок: файлы main/bak/tmp слота копируются в &lt;слот&gt;/pre-factories/ (с Flush; уже лежащая там копия не
        /// перезаписывается — другое содержимое получает имя «файл.N»); <see cref="StageProgressionService.NewGame"/>; из старого
        /// профиля (JObject) переносятся Settings целиком (LegacySettingsImported = true), PlayTimeMs, журнал Payments и намерение
        /// PendingPurchase как есть — незавершённая покупка сброс не блокирует, её завершит GooglePaymentsRuntime уже в новой
        /// кампании; добавляется <see cref="CardService.LegacyImportKey"/>; <see cref="StoreService.MigrateProfile"/>; запись
        /// схемы 3 с resetBackup: true (старый main не остаётся в bak).
        /// Ошибка архива — исключение до любой записи (старые файлы целы). Ошибка записи не бросается: результат в
        /// <paramref name="saved"/>, состояние возвращается для повторной записи вызывающим (контроллер делает Commit с resetBackup).
        /// Слот без устаревшего сохранения (валидная схема 3, нет файлов, Corrupt/Incompatible) — InvalidOperationException.
        /// </summary>
        public OutpostGameState ResetOutdated(OutpostSaveSlot slot, BalanceRepository repo, OutpostConfigSet configs, ulong seed, long nowUtcMs,
            out OutpostSaveResult saved)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var loaded = Load(slot);
            if (loaded.Status != OutpostLoadStatus.Outdated || loaded.Outdated == null)
                throw new InvalidOperationException("слот " + slot + " не содержит устаревшего сохранения: " + loaded.Status);
            ArchiveOutdated(slot);
            var state = StageProgressionService.NewGame(repo, configs, seed, nowUtcMs);
            var profile = state.Profile;
            loaded.Outdated.ApplyTo(profile);
            if (profile.CardKeys == null) profile.CardKeys = new List<string>();
            // Карточки и эликсир прежнего режима (save_main.json) в новую кампанию не возвращаются.
            if (!profile.CardKeys.Contains(CardService.LegacyImportKey)) profile.CardKeys.Add(CardService.LegacyImportKey);
            StoreService.MigrateProfile(profile, configs.Store);
            PaymentRules.Ledger(profile);
            saved = Save(state, slot, OutpostLevelAdapter.ForRun(repo, state.Run), resetBackup: true);
            return state;
        }

        /// <inheritdoc cref="ResetOutdated(OutpostSaveSlot, BalanceRepository, OutpostConfigSet, ulong, long, out OutpostSaveResult)"/>
        public OutpostGameState ResetOutdated(OutpostSaveSlot slot, BalanceRepository repo, OutpostConfigSet configs, ulong seed, long nowUtcMs) =>
            ResetOutdated(slot, repo, configs, seed, nowUtcMs, out _);

        /// <summary>Копии main/bak/tmp в pre-factories/ с записью WriteAllBytesDurable. Ошибка — исключение.</summary>
        void ArchiveOutdated(OutpostSaveSlot slot)
        {
            string dir = PreFactoriesDirectory(slot);
            bool created = false;
            foreach (var src in new[] { MainPath(slot), BakPath(slot), TmpPath(slot) })
            {
                if (!m_Fs.Exists(src)) continue;
                byte[] bytes = m_Fs.ReadAllBytes(src);
                string name = Path.GetFileName(src), dst = Path.Combine(dir, name);
                bool same = false;
                for (int n = 2; m_Fs.Exists(dst); n++)
                {
                    if (SameBytes(m_Fs.ReadAllBytes(dst), bytes))
                    {
                        same = true;
                        break;
                    }
                    if (n > 999) throw new IOException("pre-factories: нет свободного имени для " + name);
                    dst = Path.Combine(dir, name + "." + n.ToString(CultureInfo.InvariantCulture));
                }
                if (same) continue;
                if (!created)
                {
                    m_Fs.CreateDirectory(dir);
                    created = true;
                }
                m_Fs.WriteAllBytesDurable(dst, bytes);
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
