using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Общие помощники тестов группы A режима CatOutpostV2 (математика, конфигурации, импорт, экипировка, награды, сохранение).
    /// Обращения к API ядра, которые CORE_SPEC не фиксирует дословно, собраны здесь (при интеграции приведены к ядру:
    /// обёртки EquipmentServiceA/SaveServiceA, TeamStats.Compute(..., zone), TransitionResult). Ожидаемые значения тестов выводятся из ТЗ 2.1
    /// и Docs/TZ/analysis/outpost_v2_audit.json, а не из кода ядра.
    /// </summary>
    public static class OutpostKitA
    {
        public const string AuditRelativePath = "Docs/TZ/analysis/outpost_v2_audit.json";
        public const string ResourcesRelativeDir = "Assets/Game/Resources/Outpost";
        public const string SourceSha256 = "8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5";
        public const long Tps = 1_000_000;
        public const int ZoneCount = 144;
        public const int Width = 7;

        /// <summary>ТЗ §10.1: слоты кирка, шляпа, рюкзак, обувь; редкости common…legendary.</summary>
        public static readonly string[] SlotNames = { "pickaxe", "hat", "backpack", "boots" };
        public static readonly string[] RarityNames = { "common", "rare", "epic", "legendary" };
        /// <summary>ТЗ §10.3: детали за дубликат по редкости.</summary>
        public static readonly int[] DuplicatePartsByRarity = { 5, 15, 40, 120 };
        /// <summary>CORE_SPEC §3: порядок веток Team=0 … Value=5; профиль b8 (первая сессия): Bays=6 — «Места разгрузки».</summary>
        public static readonly string[] BranchIds = { "Team", "Pickaxes", "Boots", "Bags", "Unload", "Value", "Bays" };
        public static readonly string[] ConfigFiles = { "outpost_balance.json", "equipment_catalog.json", "stage_rewards.json", "level_overrides.json" };

        public static IEnumerable<int> AllZones() => Enumerable.Range(1, ZoneCount);

        public static string ItemId(int slot, int rarity) => SlotNames[slot] + "." + RarityNames[rarity];
        public static IEnumerable<string> AllItemIds()
        {
            for (int s = 0; s < 4; s++)
                for (int r = 0; r < 4; r++)
                    yield return ItemId(s, r);
        }
        public static int SlotOf(string itemId) => Array.IndexOf(SlotNames, itemId.Substring(0, itemId.IndexOf('.')));
        public static int RarityOf(string itemId) => Array.IndexOf(RarityNames, itemId.Substring(itemId.IndexOf('.') + 1));

        // ───────────────────────────── Источник и аудит ─────────────────────────────

        public static BalanceRepository Repo => TestData.Balance;

        /// <summary>Отдельная копия репозитория — для тестов, которые портят строку Grid или таблицу HP.</summary>
        public static BalanceRepository FreshRepo() => BalanceRepository.Load(TestData.BalanceBytes);

        static JObject s_Audit;

        /// <summary>Аудит лежит в Docs/ основного проекта; в копии build/ci ищем выше по дереву (как TestData.ZonesSummaryPath).</summary>
        public static string AuditPath
        {
            get
            {
                var dir = new DirectoryInfo(TestData.ProjectRoot);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, AuditRelativePath);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return Path.Combine(TestData.ProjectRoot, AuditRelativePath);
            }
        }

        public static JObject Audit => s_Audit ?? (s_Audit = JObject.Parse(File.ReadAllText(AuditPath)));

        public static JObject AuditZone(int zone)
        {
            foreach (var z in (JArray)Audit["zones"])
                if ((string)z["zone"] == "zone" + zone) return (JObject)z;
            throw new ArgumentException("В аудите нет zone" + zone);
        }

        /// <summary>Целое из аудита без потери точности (часть значений больше 2^53).</summary>
        public static BigInteger AuditBig(JToken token) => BigInteger.Parse(token.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        // ───────────────────────────── Конфигурации ─────────────────────────────

        static OutpostConfigSet s_Configs;

        // These fixtures assert the immutable source audit and retain the legacy layout.
        // MineTopologyTests covers the current branching campaign separately.
        public static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithTopologies(null));
        public static OutpostBalanceConfig Balance => Configs.Balance;
        static OutpostBalanceConfig s_BaseBalance;
        /// <summary>Профиль без множителя темпа шахт K(z) (b6 → числа b5): базовые формулы §12 и аудит источника.</summary>
        public static OutpostBalanceConfig BaseBalance => s_BaseBalance ?? (s_BaseBalance = Balance.WithoutPace());
        public static EquipmentCatalog Catalog => Configs.Equipment;
        public static StageRewardConfig Rewards => Configs.Rewards;
        public static LevelOverrides Overrides => Configs.Overrides;

        /// <summary>Встроенная строка конфигурации по имени файла Resources (CORE_SPEC §3: OutpostConfigSet.Default*Json).</summary>
        public static string EmbeddedJson(string fileName)
        {
            switch (fileName)
            {
                case "outpost_balance.json": return OutpostConfigSet.DefaultBalanceJson;
                case "equipment_catalog.json": return OutpostConfigSet.DefaultEquipmentJson;
                case "stage_rewards.json": return OutpostConfigSet.DefaultRewardsJson;
                case "level_overrides.json": return OutpostConfigSet.DefaultOverridesJson;
                default: throw new ArgumentException(fileName);
            }
        }

        public static string ResourcePath(string fileName) => Path.Combine(TestData.ProjectRoot, ResourcesRelativeDir, fileName);
        public static string ResourceText(string fileName) => File.ReadAllText(ResourcePath(fileName));

        /// <summary>Разбор четырёх текстов (порядок как в ConfigFiles).</summary>
        public static OutpostConfigSet ParseConfigs(string[] texts) => OutpostConfigSet.Parse(texts[0], texts[1], texts[2], texts[3]);

        public static string[] DefaultTexts() => ConfigFiles.Select(EmbeddedJson).ToArray();

        /// <summary>Набор, где один файл заменён результатом edit, остальные — встроенные.</summary>
        public static OutpostConfigSet ParseConfigsWith(string fileName, Func<string, string> edit)
        {
            var texts = DefaultTexts();
            int i = Array.IndexOf(ConfigFiles, fileName);
            texts[i] = edit(texts[i]);
            return ParseConfigs(texts);
        }

        public static OutpostConfigSet ParseConfigsWithBalance(Action<JObject> edit)
        {
            return ParseConfigsWith("outpost_balance.json", text =>
            {
                var j = JObject.Parse(text);
                edit(j);
                return j.ToString(Formatting.Indented);
            });
        }

        /// <summary>Hash каждой из четырёх конфигураций (порядок как в ConfigFiles).</summary>
        public static string[] Hashes(OutpostConfigSet s) =>
            new[] { s.BalanceHash, s.EquipmentHash, s.RewardsHash, s.OverridesHash };

        /// <summary>Hash каталога экипировки и наград, который пишется в конверт сохранения (CORE_SPEC §12).</summary>
        public static string EnvelopeCatalogHash(OutpostConfigSet s) => s.CatalogHash;

        // ───────────────────────────── Импорт и адаптация ─────────────────────────────

        static readonly Dictionary<int, SourceZone> s_Source = new Dictionary<int, SourceZone>();
        static readonly Dictionary<int, OutpostMap> s_Maps = new Dictionary<int, OutpostMap>();
        static readonly Dictionary<int, List<TargetBalanceA>> s_Balances = new Dictionary<int, List<TargetBalanceA>>();

        public static SourceZone Import(BalanceRepository repo, int zone) => SourceLevelImporter.Import(repo, zone);
        public static OutpostMap Adapt(SourceZone source) => OutpostLevelAdapter.Build(source);

        public static SourceZone Source(int zone)
        {
            if (!s_Source.TryGetValue(zone, out var s)) s_Source[zone] = s = Import(Repo, zone);
            return s;
        }

        public static OutpostMap Map(int zone)
        {
            if (!s_Maps.TryGetValue(zone, out var m)) s_Maps[zone] = m = Adapt(Source(zone));
            return m;
        }

        /// <summary>Баланс целей карты в порядке map.Targets (CORE_SPEC §4.3): Compute возвращает массив в том же порядке.</summary>
        public static List<TargetBalanceA> ComputeBalances(OutpostMap map, OutpostBalanceConfig cfg, BalanceRepository repo)
        {
            var all = OutpostLevelBalanceAdapter.Compute(map, cfg, repo);
            var result = new List<TargetBalanceA>();
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var t = map.Targets[i];
                var b = all[i];
                Assert.AreEqual(t.Id, b.TargetId, "порядок баланса = порядок map.Targets");
                result.Add(new TargetBalanceA(t, b.MaxHp, b.TotalCargoUnits, b.ValueFactor, b.Rank01));
            }
            return result;
        }

        public static List<TargetBalanceA> Balances(int zone)
        {
            if (!s_Balances.TryGetValue(zone, out var b)) s_Balances[zone] = b = ComputeBalances(Map(zone), Balance, Repo);
            return b;
        }

        /// <summary>Полный путь импорт → адаптер → баланс: ошибка данных может возникнуть на любом шаге.</summary>
        public static void RunPipeline(BalanceRepository repo, int zone)
        {
            var map = Adapt(Import(repo, zone));
            ComputeBalances(map, Balance, repo);
        }

        public static ReachabilityReport Reach(int zone) => OutpostReachability.Check(Map(zone));

        /// <summary>Строка Grid зоны в свежей копии репозитория, изменённая edit (токены после Split(',')).</summary>
        public static BalanceRepository RepoWithGrid(int zone, Action<string[]> edit)
        {
            var repo = FreshRepo();
            var dto = repo.Zone(zone);
            var tokens = dto.Grid.Split(',');
            edit(tokens);
            dto.Grid = string.Join(",", tokens);
            return repo;
        }

        /// <summary>Строка Grid зоны в свежей копии репозитория, заменённая целиком.</summary>
        public static BalanceRepository RepoWithGridText(int zone, Func<string, string> edit)
        {
            var repo = FreshRepo();
            var dto = repo.Zone(zone);
            dto.Grid = edit(dto.Grid);
            return repo;
        }

        // ───────────────────────────── Независимые эталонные формулы (ТЗ §12.4) ─────────────────────────────

        public static FracA TypeHpFactor(char kind)
        {
            switch (kind)
            {
                case 'r': return FracA.Of(1);
                case 'x': return FracA.Of(4, 5);
                case 's': return FracA.Of(2);
                case 'p': return FracA.Of(5, 2);
                default: throw new ArgumentException(kind.ToString());
            }
        }

        public static FracA TypeValueFactor(char kind)
        {
            switch (kind)
            {
                case 'r': return FracA.Of(1);
                case 'x': return FracA.Of(1, 2);
                case 's': return FracA.Of(2);
                case 'p': return FracA.Of(1);
                default: throw new ArgumentException(kind.ToString());
            }
        }

        /// <summary>
        /// ТЗ §12.4: MaxHp = ceil(20·ZoneHpFactor·(1+5·rank01)·AreaFactor·TypeHpFactor) без double;
        /// профиль b4 (аудит баланса): ZoneHpFactor = 1 + (z−1)/25 (в ТЗ 2.1 и профилях b1–b3 было (z−1)/200).
        /// </summary>
        public static long ExpectedMaxHp(int zone, FracA rank01, int area, char kind)
        {
            var a = FracA.Of(20) * (FracA.Of(1) + FracA.Of(zone - 1, 25)) * (FracA.Of(1) + FracA.Of(5) * rank01) * TypeHpFactor(kind);
            if (area >= 7) return (long)(a * FracA.Of(5, 2)).Ceil();
            BigInteger num2 = a.N * a.N * area, den2 = a.D * a.D;
            BigInteger root = ISqrt(num2 / den2);
            if (root * root * den2 < num2) root += 1;
            return (long)root;
        }

        /// <summary>ТЗ §12.4: TotalCargoUnits = max(2, ceil(MaxHp / 10)) — для r/x/p (у цепей завода s груза нет, см. перегрузку с видом).</summary>
        public static int ExpectedUnits(long maxHp) => (int)Math.Max(2, (maxHp + 9) / 10);

        /// <summary>
        /// ТЗ 2.2 §4.2 и §16.1 (а), адаптер adapter-2: цепи завода (s) монет не дают — TotalCargoUnits = 0; r/x/p — max(2, ceil(MaxHp / 10)).
        /// </summary>
        public static int ExpectedUnits(long maxHp, char kind) => kind == 's' ? 0 : ExpectedUnits(maxHp);

        /// <summary>ТЗ §12.4: CargoGoldValue = max(1, ceil(5 · 1.2^(z-1) · BaseCoinValueFactor · FinalValueMultiplier)).</summary>
        public static BigInteger ExpectedCoinValue(int zone, FracA valueFactor, FracA finalValue)
        {
            var v = (FracA.Of(5) * FracA.Of(6, 5).Pow(zone - 1) * valueFactor * finalValue).Ceil();
            return v < 1 ? BigInteger.One : v;
        }

        /// <summary>
        /// ТЗ §12.2 и профиль b4: NextCost(z, L) = ceil(S(z) · CostZoneFactor(z) · BaseCost · Growth^L), S(z) = 1.2^(z−1),
        /// CostZoneFactor(z) = 1 + (z−1)/10; одно итоговое округление.
        /// </summary>
        /// <summary>
        /// Независимая цена уровня: ceil(1.2^(z−1) · (1 + (z−1)/10) · BaseCost · Growth^L), с paced — ещё × K(z) профиля b6
        /// (<see cref="ExpectedPace"/>; решение владельца В4, 15 октября 2026).
        /// </summary>
        public static BigInteger ExpectedCost(int zone, FracA baseCost, FracA growth, int level, bool paced = false) =>
            (FracA.Of(6, 5).Pow(zone - 1) * (FracA.Of(1) + FracA.Of(zone - 1, 10)) * baseCost * growth.Pow(level) *
             (paced ? ExpectedPace(zone) : FracA.Of(1))).Ceil();

        static JArray s_PaceScale;

        /// <summary>
        /// K(z) профиля b6 прямо из массива PaceScale встроенного outpost_balance.json (элемент z−1; нет элемента — 1), мимо
        /// OutpostBalanceConfig.ZonePace (Docs/Rework/BALANCE_FACTORIES.md, «Темп b6: 3 цикла»).
        /// </summary>
        public static FracA ExpectedPace(int zone)
        {
            if (s_PaceScale == null) s_PaceScale = (JArray)JObject.Parse(OutpostConfigSet.DefaultBalanceJson)["PaceScale"] ?? new JArray();
            return zone >= 1 && zone <= s_PaceScale.Count ? FracA.Parse((string)s_PaceScale[zone - 1]) : FracA.Of(1);
        }

        public static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (n < 2) return n;
            BigInteger x = n, y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }
            return x;
        }

        // ───────────────────────────── Характеристики ─────────────────────────────

        /// <summary>Уровни всех семи веток (профиль b8: шесть основных и «Места разгрузки»), кроме branch, — 0.</summary>
        public static int[] LevelsWith(UpgradeBranch branch, int level)
        {
            var levels = new int[OutpostIds.BranchCount];
            levels[(int)branch] = level;
            return levels;
        }

        /// <summary>Характеристики команды; zone влияет только на денежный масштаб S(z) (CoinValue).</summary>
        public static TeamStats Stats(int[] levels = null, OutpostProfile profile = null, OutpostBalanceConfig balance = null, int zone = 1) =>
            TeamStats.Compute(balance ?? Balance, levels ?? new int[OutpostIds.BranchCount], profile ?? NewProfile(1), Catalog, zone);

        /// <summary>CORE_SPEC §6: цена следующего уровня; на cap — нет цены (null).</summary>
        public static BigInteger? NextCost(int zone, UpgradeBranch branch, int level, OutpostBalanceConfig balance = null) =>
            TeamUpgradeService.NextCost(balance ?? Balance, zone, branch, level);

        /// <summary>CORE_SPEC §6: CoinValue(zone, valueFactor) по текущим характеристикам.</summary>
        /// Зона фиксируется при расчёте характеристик (TeamStats.Compute(..., zone)): проверяем, что она та же.
        public static BigInteger CoinValue(TeamStats stats, int zone, Rational valueFactor)
        {
            Assert.AreEqual(zone, stats.Zone, "характеристики рассчитаны для другой зоны");
            return stats.CoinValue(valueFactor);
        }

        // ───────────────────────────── Профиль и экипировка ─────────────────────────────

        /// <summary>Новый профиль: поток наград Pcg32(seed, 5) (ТЗ §10.2); фабрику ядра проверяет OutpostRewardTests.NewProfile_RewardRngIsStream5OfSeed.</summary>
        public static OutpostProfile NewProfile(ulong seed)
        {
            return new OutpostProfile
            {
                ModeId = "CatOutpostV2",
                // ТЗ 2.2 §10.1 и §16.1 (б): фикстура пишет текущую схему (3), как StageProgressionService.NewGame.
                SchemaVersion = OutpostIds.SchemaVersion,
                Seed = seed,
                RewardRng = new Pcg32(seed, 5),
                RewardRngVersion = 1,
                CurrentZone = 1,
                CampaignComplete = false,
                CompletedZones = new List<int>(),
                RewardLog = new List<RewardRecord>(),
                Items = new Dictionary<string, int>(),
                Equipped = new string[4],
                Parts = 0,
                NextRunSeq = 1,
                Tutorial = new TutorialState { Seen = new List<string>() },
                Settings = new OutpostSettings { Master = 1f, Music = 1f, Sfx = 1f, Ambience = 1f },
            };
        }

        public static void Own(OutpostProfile p, string itemId, int level) => p.Items[itemId] = level;

        /// <summary>Положить предмет в коллекцию и надеть напрямую, без сервиса (для изолированных тестов характеристик).</summary>
        public static void Wear(OutpostProfile p, string itemId, int level)
        {
            Own(p, itemId, level);
            p.Equipped[SlotOf(itemId)] = itemId;
        }

        /// <summary>EquipmentService ядра статический: обёртка с текущим каталогом (интеграция: сигнатуры приведены к ядру).</summary>
        public static EquipmentServiceA NewEquipmentService() => new EquipmentServiceA(Configs);

        public static int LevelOf(OutpostProfile p, string itemId) => p.Items.TryGetValue(itemId, out int q) ? q : 0;

        public static string JsonOf(object value) => JsonConvert.SerializeObject(value, OutpostJson.Settings);

        // ───────────────────────────── Награды и run ─────────────────────────────

        public static string RewardKey(int zone) => "CatOutpostV2:zone" + zone + ":first";

        /// <summary>ТЗ §12.3: CompletionParts(z) = 10 + 5·floor(z/5).</summary>
        public static int CompletionParts(int zone) => 10 + 5 * (zone / 5);

        /// <summary>
        /// ТЗ §10.2 и первая сессия (rewards-2): гарантии шахт 3–5/10/25/50 — обычные шляпа, рюкзак, обувь, затем кирки; иначе null.
        /// Шахты 1–2 — без предмета (<see cref="NoItemZone"/>).
        /// </summary>
        public static string GuaranteedItem(int zone)
        {
            switch (zone)
            {
                case 3: return "hat.common";
                case 4: return "backpack.common";
                case 5: return "boots.common";
                case 10: return "pickaxe.rare";
                case 25: return "pickaxe.epic";
                case 50: return "pickaxe.legendary";
                default: return null;
            }
        }

        /// <summary>
        /// Первая сессия (rewards-2, NoItem): за первое завершение шахт 1 и 2 предмета нет — только детали; поток 5 не продвигается
        /// (награда шахты 1 — спасённая Mia, шахты 2 — карточки; экипировка открывается в шахте 3).
        /// </summary>
        public static bool NoItemZone(int zone) => zone == 1 || zone == 2;

        /// <summary>ТЗ §10.2: слот NextInt(4), затем редкость NextInt(100): 0–69/70–93/94–98/99. Продвигает переданный генератор.</summary>
        public static string ExpectedRandomItem(Pcg32 rng)
        {
            int slot = rng.NextInt(4);
            int roll = rng.NextInt(100);
            int rarity = roll < 70 ? 0 : roll < 94 ? 1 : roll < 99 ? 2 : 3;
            return ItemId(slot, rarity);
        }

        /// <summary>
        /// Состояние для награды завершения: профиль + минимальный run в фазе доставки остатков.
        /// Остальные поля run наградой не читаются (CORE_SPEC §10).
        /// </summary>
        public static OutpostGameState RewardState(int zone, ulong seed, OutpostProfile profile = null)
        {
            var p = profile ?? NewProfile(seed);
            p.CurrentZone = zone;
            var run = new OutpostRun { RunId = "run-" + zone, Zone = zone, ZoneId = "zone" + zone, Phase = RunPhase.DrainingCargo };
            return new OutpostGameState { Profile = p, Run = run };
        }

        public static void ApplyCompletion(OutpostGameState state) => StageProgressionService.ApplyCompletion(state, Configs);

        /// <summary>Подготовка перехода; null — переход отклонён (исключение или результат Ok = false).</summary>
        public static OutpostGameState TryPrepareTransition(OutpostGameState state)
        {
            try
            {
                var r = StageProgressionService.PrepareTransition(state, Repo, Configs, 0L);
                return r != null && r.Ok ? r.State : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public static OutpostRun CreateRun(OutpostProfile profile, int zone, OutpostConfigSet configs, long nowUtcMs) =>
            StageProgressionService.CreateRun(profile, zone, Map(zone), configs, Repo, nowUtcMs);

        // ───────────────────────────── Точные сравнения ─────────────────────────────

        public static FracA ToFrac(Rational r) => new FracA(r.Num, r.Den);

        public static void AssertExact(Rational actual, FracA expected, string message = null)
        {
            Assert.AreEqual(expected.N, actual.Num, (message ?? "") + " — числитель; ожидалось " + expected + ", получено " + actual);
            Assert.AreEqual(expected.D, actual.Den, (message ?? "") + " — знаменатель; ожидалось " + expected + ", получено " + actual);
        }

        public static Rational R(string text) => Rational.Parse(text);

        public static string Utf8(byte[] bytes) => new UTF8Encoding(false).GetString(bytes);
        public static byte[] Utf8Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Utf8Bytes(text));
                var sb = new StringBuilder(64);
                foreach (var b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool IsSha256Hex(string s) =>
            s != null && s.Length == 64 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    /// <summary>
    /// Обёртка статического EquipmentService ядра: возвращает CommandResult (Ok/Reason), как команды симуляции.
    /// EquipBest всегда успешна (как команда EquipBest ядра); уже лучший комплект не меняется.
    /// </summary>
    public sealed class EquipmentServiceA
    {
        readonly OutpostConfigSet m_Configs;

        public EquipmentServiceA(OutpostConfigSet configs) => m_Configs = configs;

        static CommandResult FromReason(string reason) => reason == null ? new CommandResult { Ok = true } : CommandResult.Fail(reason);

        public (bool duplicate, int parts) Grant(OutpostProfile p, string itemId)
        {
            var r = EquipmentService.Grant(p, itemId, m_Configs);
            return (r.Duplicate, r.Parts);
        }

        public CommandResult Equip(OutpostProfile p, string itemId) => FromReason(EquipmentService.Equip(p, m_Configs.Equipment, itemId));
        public CommandResult Unequip(OutpostProfile p, EquipSlot slot) => FromReason(EquipmentService.Unequip(p, slot));
        public CommandResult EquipBest(OutpostProfile p)
        {
            EquipmentService.EquipBest(p, m_Configs.Equipment);
            return new CommandResult { Ok = true };
        }

        public CommandResult UpgradeItem(OutpostProfile p, string itemId)
        {
            string reason = EquipmentService.UpgradeItem(p, m_Configs.Equipment, itemId, out long cost);
            var r = FromReason(reason);
            if (r.Ok) r.Cost = cost;
            return r;
        }
    }

    /// <summary>Доступ к каталогу по itemId (каталог ядра принимает редкость).</summary>
    public static class EquipmentCatalogExtA
    {
        public static Rational ItemBonus(this EquipmentCatalog c, string itemId, int level) => c.ItemBonus(c.Get(itemId).Rarity, level);
        public static long UpgradeCost(this EquipmentCatalog c, string itemId, int level) => c.UpgradeCost(c.Get(itemId).Rarity, level);
    }

    /// <summary>Баланс одной цели в тестовом виде (развязывает тесты от формы результата OutpostLevelBalanceAdapter).</summary>
    public sealed class TargetBalanceA
    {
        public readonly MapTarget Target;
        public readonly long MaxHp;
        public readonly int Units;
        public readonly Rational ValueFactor;
        public readonly Rational Rank01;

        public TargetBalanceA(MapTarget target, long maxHp, int units, Rational valueFactor, Rational rank01)
        {
            Target = target;
            MaxHp = maxHp;
            Units = units;
            ValueFactor = valueFactor;
            Rank01 = rank01;
        }
    }

    /// <summary>
    /// Независимая точная дробь для ожидаемых значений: не использует Rational ядра, чтобы эталон не повторял его ошибки.
    /// </summary>
    public readonly struct FracA : IEquatable<FracA>
    {
        public readonly BigInteger N;
        public readonly BigInteger D;

        public FracA(BigInteger n, BigInteger d)
        {
            if (d.IsZero) throw new DivideByZeroException();
            if (d.Sign < 0) { n = -n; d = -d; }
            var g = BigInteger.GreatestCommonDivisor(n, d);
            if (!g.IsOne && !g.IsZero) { n /= g; d /= g; }
            N = n;
            D = d;
        }

        public static FracA Of(long n, long d = 1) => new FracA(n, d);

        /// <summary>"n/d" или "n".</summary>
        public static FracA Parse(string s)
        {
            int slash = s.IndexOf('/');
            if (slash < 0) return new FracA(BigInteger.Parse(s, CultureInfo.InvariantCulture), 1);
            return new FracA(BigInteger.Parse(s.Substring(0, slash), CultureInfo.InvariantCulture),
                BigInteger.Parse(s.Substring(slash + 1), CultureInfo.InvariantCulture));
        }

        public static implicit operator FracA(long v) => new FracA(v, 1);
        public static FracA operator +(FracA a, FracA b) => new FracA(a.N * b.D + b.N * a.D, a.D * b.D);
        public static FracA operator -(FracA a, FracA b) => new FracA(a.N * b.D - b.N * a.D, a.D * b.D);
        public static FracA operator *(FracA a, FracA b) => new FracA(a.N * b.N, a.D * b.D);
        public static FracA operator /(FracA a, FracA b) => new FracA(a.N * b.D, a.D * b.N);

        public FracA Pow(int e)
        {
            if (e < 0) throw new ArgumentOutOfRangeException(nameof(e));
            return new FracA(BigInteger.Pow(N, e), BigInteger.Pow(D, e));
        }

        public BigInteger Ceil()
        {
            var q = BigInteger.DivRem(N, D, out var r);
            return r.Sign > 0 ? q + 1 : q;
        }

        public BigInteger Floor()
        {
            var q = BigInteger.DivRem(N, D, out var r);
            return r.Sign < 0 ? q - 1 : q;
        }

        public int CompareTo(FracA other) => (N * other.D).CompareTo(other.N * D);
        public bool Equals(FracA other) => N == other.N && D == other.D;
        public override bool Equals(object obj) => obj is FracA f && Equals(f);
        public override int GetHashCode() => N.GetHashCode() ^ D.GetHashCode();
        public override string ToString() => D.IsOne ? N.ToString(CultureInfo.InvariantCulture) : N.ToString(CultureInfo.InvariantCulture) + "/" + D.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Однократная ошибка Replace при живом процессе (Win32 ReplaceFile; ТЗ §17 «восстановление на каждом этапе»).</summary>
    public enum ReplaceFaultA
    {
        None,
        /// <summary>Ничего не изменено (файл занят: антивирус, индексатор).</summary>
        NothingChanged,
        /// <summary>ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 (1177): main уже стал bak, tmp не перемещён, main нет.</summary>
        BackupRenamedOnly,
    }

    /// <summary>
    /// Файловая система в памяти с имитацией сбоя питания на N-м изменяющем шаге (ТЗ §17, §20 «Аварийная запись»).
    /// После сбоя все изменяющие операции тоже падают: процесс «мёртв», на диск больше ничего не попадает.
    /// Реализует существующий CatMiners.Core.Save.IFileSystem.
    /// </summary>
    public sealed class MemoryFileSystemA : CatMiners.Core.Save.IFileSystem
    {
        readonly SortedDictionary<string, byte[]> m_Files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        readonly SortedSet<string> m_Dirs = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Номер изменяющего шага (с 1), на котором происходит сбой; 0 — без сбоя.</summary>
        public int FailAtStep;
        /// <summary>Сбой на шаге записи оставляет на диске половину байтов.</summary>
        public bool TornWrite;
        /// <summary>Replace не поддерживается: бросает до каких-либо изменений, сервис обязан идти запасным путём.</summary>
        public bool ReplaceUnsupported;
        /// <summary>Однократная ошибка ближайшего Replace без гибели процесса (IOException); затем Replace снова работает.</summary>
        public ReplaceFaultA ReplaceFault;

        public int Steps { get; private set; }
        public bool Dead { get; private set; }
        public readonly List<string> Journal = new List<string>();

        public IReadOnlyDictionary<string, byte[]> Files => m_Files;

        /// <summary>Нормализация пути: прямые слэши, без буквы диска (Path.GetFullPath на Windows).</summary>
        public static string Norm(string path)
        {
            string p = path.Replace('\\', '/');
            if (p.Length >= 2 && p[1] == ':' && char.IsLetter(p[0])) p = p.Substring(2);
            return p;
        }

        /// <summary>Сбой через stepsFromNow изменяющих шагов от текущего момента.</summary>
        public void ArmCrash(int stepsFromNow, bool torn)
        {
            FailAtStep = Steps + stepsFromNow;
            TornWrite = torn;
        }

        /// <summary>Содержимое «диска» после аварии — для загрузки новым сервисом (перезапуск приложения).</summary>
        public MemoryFileSystemA CrashImage()
        {
            var copy = new MemoryFileSystemA();
            foreach (var kv in m_Files) copy.m_Files[kv.Key] = (byte[])kv.Value.Clone();
            foreach (var d in m_Dirs) copy.m_Dirs.Add(d);
            return copy;
        }

        /// <summary>Прямая запись в обход счётчика шагов (подготовка фикстуры).</summary>
        public void Put(string path, byte[] data) => m_Files[Norm(path)] = (byte[])data.Clone();
        public void Put(string path, string text) => Put(path, OutpostKitA.Utf8Bytes(text));
        public byte[] Get(string path) => m_Files.TryGetValue(Norm(path), out var d) ? (byte[])d.Clone() : null;
        public void Remove(string path) => m_Files.Remove(Norm(path));

        void Step(string op, string path, Action action, Action tornAction = null)
        {
            if (Dead) throw new IOException("процесс уже остановлен: " + op + " " + path);
            Steps++;
            Journal.Add(op + " " + path);
            if (FailAtStep > 0 && Steps == FailAtStep)
            {
                Dead = true;
                if (TornWrite && tornAction != null) tornAction();
                throw new IOException("имитация сбоя питания на шаге " + Steps + ": " + op + " " + path);
            }
            action();
        }

        public bool Exists(string path) => m_Files.ContainsKey(Norm(path));

        public byte[] ReadAllBytes(string path)
        {
            if (!m_Files.TryGetValue(Norm(path), out var data)) throw new FileNotFoundException("нет файла", path);
            return (byte[])data.Clone();
        }

        public void WriteAllBytesDurable(string path, byte[] data)
        {
            string p = Norm(path);
            var copy = (byte[])data.Clone();
            Step("write", p, () => m_Files[p] = copy, () => m_Files[p] = copy.Take(copy.Length / 2).ToArray());
        }

        public void Replace(string source, string destination, string backup)
        {
            if (!Dead && ReplaceUnsupported) throw new PlatformNotSupportedException("Replace недоступен на этой файловой системе");
            string s = Norm(source), d = Norm(destination), b = backup == null ? null : Norm(backup);
            var fault = ReplaceFault;
            ReplaceFault = ReplaceFaultA.None;
            Step("replace", s + " -> " + d, () =>
            {
                if (!m_Files.ContainsKey(s)) throw new FileNotFoundException("нет источника", s);
                if (!m_Files.ContainsKey(d)) throw new FileNotFoundException("нет заменяемого файла", d);
                if (fault == ReplaceFaultA.NothingChanged)
                    throw new IOException("имитация ReplaceFile: файл занят другим процессом, ничего не изменено");
                if (fault == ReplaceFaultA.BackupRenamedOnly)
                {
                    // Win32 ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 (1177): заменяемый файл уже переименован в backup, замена осталась под своим именем.
                    if (b != null)
                    {
                        m_Files[b] = m_Files[d];
                        m_Files.Remove(d);
                    }
                    throw new IOException("имитация ReplaceFile 1177: main переименован в bak, tmp не перемещён");
                }
                if (b != null) m_Files[b] = m_Files[d];
                m_Files[d] = m_Files[s];
                m_Files.Remove(s);
            });
        }

        public void Move(string source, string destination)
        {
            string s = Norm(source), d = Norm(destination);
            Step("move", s + " -> " + d, () =>
            {
                if (!m_Files.ContainsKey(s)) throw new FileNotFoundException("нет источника", s);
                if (m_Files.ContainsKey(d)) throw new IOException("файл назначения уже существует: " + d);
                m_Files[d] = m_Files[s];
                m_Files.Remove(s);
            });
        }

        public void Copy(string source, string destination)
        {
            string s = Norm(source), d = Norm(destination);
            Step("copy", s + " -> " + d, () =>
            {
                if (!m_Files.ContainsKey(s)) throw new FileNotFoundException("нет источника", s);
                m_Files[d] = (byte[])m_Files[s].Clone();
            });
        }

        public void Delete(string path)
        {
            string p = Norm(path);
            Step("delete", p, () => m_Files.Remove(p));
        }

        public void CreateDirectory(string path)
        {
            string p = Norm(path);
            Step("mkdir", p, () => m_Dirs.Add(p));
        }
    }

    /// <summary>Сервис сохранения ядра, привязанный к одному слоту (main или debug).</summary>
    public sealed class SaveServiceA
    {
        public readonly OutpostSaveService Inner;
        public readonly OutpostSaveSlot Slot;

        public SaveServiceA(OutpostSaveService inner, OutpostSaveSlot slot)
        {
            Inner = inner;
            Slot = slot;
        }

        public OutpostSaveResult Save(OutpostGameState state) => Inner.Save(state, Slot, state?.Run != null ? OutpostKitA.Map(state.Run.Zone) : null);
        public OutpostLoadResult Load() => Inner.Load(Slot);
    }

    /// <summary>Помощники тестов сохранения (CORE_SPEC §12, ТЗ §17).</summary>
    public static class SaveKitA
    {
        public const string Root = "/persist";
        public const string Main = Root + "/CatOutpostV2/main/state.json";
        public const string Bak = Main + ".bak";
        public const string Tmp = Main + ".tmp";
        public const string DebugMain = Root + "/CatOutpostV2/debug/state.json";
        public const string LegacyMain = Root + "/save_main.json";

        /// <summary>Сервис ядра: корень persistentDataPath + файловая система; слот main/debug; текущие конфигурации и карта для инвариантов.</summary>
        public static SaveServiceA NewService(MemoryFileSystemA fs, bool debug = false, OutpostConfigSet configs = null) =>
            new SaveServiceA(new OutpostSaveService(Root, fs)
            {
                Configs = configs ?? OutpostKitA.Configs,
                MapResolver = s => OutpostKitA.Map(s.Run.Zone),
            }, debug ? OutpostSaveSlot.Debug : OutpostSaveSlot.Main);

        /// <summary>true — commit записан (Save возвращает результат; исключение тоже считается неудачей).</summary>
        public static bool TrySave(SaveServiceA service, OutpostGameState state)
        {
            try
            {
                return service.Save(state).Ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static OutpostLoadResult Load(SaveServiceA service) => service.Load();

        /// <summary>Загружен валидный commit (из main или из bak).</summary>
        public static bool IsLoaded(OutpostLoadResult r) =>
            r != null && r.State != null && (r.Status == OutpostLoadStatus.Loaded || r.Status == OutpostLoadStatus.LoadedFromBackup);

        public static long Seq(OutpostLoadResult r) => r.CommitSequence;

        /// <summary>
        /// Согласованное состояние zone1 сразу после создания run. Маркер commit записан в двух местах —
        /// Profile.Parts и Run.LastProcessedUtcMs: несовпадение означает смешение профиля и run из разных commit.
        /// </summary>
        public static OutpostGameState MakeState(long marker)
        {
            var profile = OutpostKitA.NewProfile(777);
            profile.Parts = marker;
            var run = OutpostKitA.CreateRun(profile, 1, OutpostKitA.Configs, marker);
            return new OutpostGameState { Profile = profile, Run = run };
        }

        public static long Marker(OutpostGameState s)
        {
            Assert.AreEqual(s.Profile.Parts, s.Run.LastProcessedUtcMs, "профиль и run из разных commit");
            return s.Profile.Parts;
        }

        public static JObject ReadEnvelope(MemoryFileSystemA fs, string path) => JObject.Parse(OutpostKitA.Utf8(fs.Get(path)));

        public static void WriteEnvelope(MemoryFileSystemA fs, string path, JObject envelope) =>
            fs.Put(path, envelope.ToString(Formatting.None));

        public static JObject Payload(JObject envelope) => JObject.Parse((string)envelope["Payload"]);

        /// <summary>Подменить Payload; rehash — пересчитать PayloadSha256 (иначе контрольная сумма станет неверной).</summary>
        public static void SetPayload(JObject envelope, JObject payload, bool rehash)
        {
            string text = payload.ToString(Formatting.None);
            envelope["Payload"] = text;
            if (rehash) envelope["PayloadSha256"] = OutpostKitA.Sha256Hex(text);
        }

        /// <summary>
        /// Согласованно отметить одну порцию первой цели как выпущенную и сданную со стоимостью value
        /// (ТЗ §7.3: созданные = на земле + в сумках + сданные; кошелёк = зачисления − расходы).
        /// </summary>
        public static void ForceDeliveredValue(OutpostGameState s, BigInteger value)
        {
            var run = s.Run;
            var t = run.Targets[0];
            t.ReleasedUnits = 1;
            // HP ровно на пороге первой порции: floor(Total·(Max−Rem)/Max) = 1
            long step = (t.MaxHpUnits + t.TotalUnits - 1) / t.TotalUnits;
            t.RemainingHpUnits = t.MaxHpUnits - step;
            run.Counters.ReleasedUnits = 1;
            run.Counters.DeliveredUnits = 1;
            run.Counters.DeliveredValue = value;
            run.Counters.ReleasedValue = value; // ядро: выпущено = земля + сумки + зачислено
            run.TotalCredited = value;
            run.TotalSpent = BigInteger.Zero;
            run.Wallet = value;
        }

        /// <summary>Стоимость каждой существующей порции (земля + сумки) по ключу (Source, Ordinal).</summary>
        public static SortedDictionary<string, BigInteger> CargoValues(OutpostGameState s)
        {
            var result = new SortedDictionary<string, BigInteger>(StringComparer.Ordinal);
            foreach (var b in s.Run.Batches)
                foreach (var u in b.Units)
                    result[u.Source + ":" + u.Ordinal] = u.Value;
            foreach (var w in s.Run.Workers)
                if (w.Bag != null)
                    foreach (var u in w.Bag)
                        result[u.Source + ":" + u.Ordinal] = u.Value;
            return result;
        }
    }
}
