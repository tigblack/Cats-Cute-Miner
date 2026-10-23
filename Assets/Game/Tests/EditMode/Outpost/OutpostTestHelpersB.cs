using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Outpost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// Общие помощники тестов ядра CatOutpostV2, набор B (симуляция, порядок фаз, детерминизм, улучшения, переход, офлайн, кампания).
// Ожидаемые значения выводятся независимо: из ТЗ 2.1, CORE_SPEC и Docs/TZ/analysis/outpost_v2_audit.json.
// Обращения к API ядра, имя или сигнатура которых в CORE_SPEC не зафиксированы, собраны здесь (при интеграции приведены к ядру).
// Имена согласованы с набором A (OutpostTestHelpersA.cs), где это возможно.
namespace CatMiners.Tests.Outpost
{
    public static class OutpostTestHelpersB
    {
        // ───────────────────────────── Константы ТЗ/SPEC ─────────────────────────────

        /// <summary>ТЗ §6.5: тиков в секунде.</summary>
        public const long S = 1_000_000;
        /// <summary>ТЗ §12.1: интервал удара 1 с.</summary>
        public const long HitTicks = S;
        /// <summary>ТЗ §6.5: ребро при скорости 2.5 клетки/с — ceil(1e6 / 2.5).</summary>
        public const long Edge0 = 400_000;
        /// <summary>ТЗ §7.4: порция при 2 порциях/с — ceil(1e6 / 2).</summary>
        public const long Unload0 = 500_000;
        /// <summary>ТЗ §6.5: подбор 0.25 с на порцию.</summary>
        public const long Pickup0 = 250_000;
        /// <summary>ТЗ §12.5: урон 5 → 5·10^6 долей HP.</summary>
        public const long Damage0 = 5_000_000;
        public const long HourMs = 3_600_000;
        /// <summary>ТЗ §13: лимит офлайна 3 реальных часа.</summary>
        public const long OfflineCapMs = 3 * HourMs;
        /// <summary>ТЗ §20: 24 ч симуляции на карту.</summary>
        public const long DayTicks = 86_400L * S;
        /// <summary>ТЗ §20: 2 000 000 обработанных событий на карту.</summary>
        public const int EventLimit = 2_000_000;
        /// <summary>Произвольная UTC-метка старта (октябрь 2026).</summary>
        public const long StartUtcMs = 1_791_500_000_000L;
        public const ulong Seed = 0x2026_1008UL;
        public const string ModeId = "CatOutpostV2";

        public static readonly string[] SlotNames = { "pickaxe", "hat", "backpack", "boots" };
        public static readonly string[] RarityNames = { "common", "rare", "epic", "legendary" };
        /// <summary>ТЗ §10.3: детали за дубликат по редкости.</summary>
        public static readonly int[] DuplicateParts = { 5, 15, 40, 120 };
        /// <summary>ТЗ §12.3: RarityBaseBonus = [0.08, 0.18, 0.35, 0.60].</summary>
        public static readonly FracB[] RarityBaseBonus = { new FracB(2, 25), new FracB(9, 50), new FracB(7, 20), new FracB(3, 5) };
        /// <summary>ТЗ §12.3: RarityBaseCapacity = [1, 3, 6, 10].</summary>
        public static readonly int[] RarityBaseCapacity = { 1, 3, 6, 10 };

        /// <summary>Порядок политики C (ТЗ §20): Кирки, Команда, Сумки, Обувь, Разгрузка, Оценка.</summary>
        public static readonly UpgradeBranch[] PolicyCOrder =
        {
            UpgradeBranch.Pickaxes, UpgradeBranch.Team, UpgradeBranch.Bags, UpgradeBranch.Boots, UpgradeBranch.Unload, UpgradeBranch.Value,
        };

        // ───────────────────────────── Конфигурации ─────────────────────────────

        static OutpostConfigSet s_Configs;

        public static OutpostConfigSet Configs => s_Configs ?? (s_Configs = OutpostConfigSet.LoadDefault().WithTopologies(null));

        /// <summary>
        /// Набор конфигураций с правкой баланса (поля CORE_SPEC §3). Используется только для изоляции механики в тесте
        /// (например, скорость 1 клетка/с, чтобы все события шли по целым секундам); правила ядра те же.
        /// </summary>
        public static OutpostConfigSet ConfigsWith(Action<JObject> editBalance)
        {
            var j = JObject.Parse(OutpostConfigSet.DefaultBalanceJson);
            editBalance(j);
            return OutpostConfigSet.Parse(j.ToString(Formatting.None),
                OutpostConfigSet.DefaultEquipmentJson, OutpostConfigSet.DefaultRewardsJson,
                OutpostConfigSet.DefaultOverridesJson);
        }

        // ───────────────────────────── Карты ─────────────────────────────

        static readonly Dictionary<int, OutpostMap> s_Maps = new Dictionary<int, OutpostMap>();
        static BalanceRepository s_SyntheticRepo;

        public static OutpostMap BuildMap(BalanceRepository repo, int zone)
        {
            SourceZone source = SourceLevelImporter.Import(repo, zone);
            return OutpostLevelAdapter.Build(source);
        }

        /// <summary>Карта исходной зоны (неизменяемая, кэш).</summary>
        public static OutpostMap Map(int zone)
        {
            lock (s_Maps)
            {
                if (!s_Maps.TryGetValue(zone, out var map))
                {
                    map = BuildMap(TestData.Balance, zone);
                    s_Maps[zone] = map;
                }
                return map;
            }
        }

        /// <summary>Отдельная копия репозитория для синтетических планировок (общий TestData.Balance не меняется).</summary>
        static BalanceRepository SyntheticRepo
        {
            get
            {
                if (s_SyntheticRepo == null) s_SyntheticRepo = BalanceRepository.Load(TestData.BalanceBytes);
                return s_SyntheticRepo;
            }
        }

        internal static void ApplyGrid(BalanceRepository repo, int zone, string grid, int depth)
        {
            var z = repo.Zone(zone);
            z.Grid = grid;
            z.WidthCells = 7;
            z.DepthCells = depth;
        }

        /// <summary>Фикстура исходной зоны.</summary>
        public static OutpostFixtureB Real(int zone, OutpostConfigSet configs = null, Action<OutpostProfile> profileSetup = null)
        {
            return new OutpostFixtureB { Repo = TestData.Balance, Map = Map(zone), Configs = configs ?? Configs, Zone = zone, ProfileSetup = profileSetup };
        }

        /// <summary>
        /// Синтетическая планировка шириной 7 на месте zone1 (S(1) = 1, ZoneHpFactor = 1). Строки сверху вниз,
        /// 7 токенов через пробел в формате Zones[].Grid (ТЗ §8.1). Нужны ровно один c и один e.
        /// </summary>
        public static OutpostFixtureB Synthetic(OutpostConfigSet configs, Action<OutpostProfile> profileSetup, params string[] rows)
        {
            var tokens = new List<string>();
            foreach (var row in rows)
            {
                var parts = row.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(7, parts.Length, "синтетическая строка должна содержать 7 токенов: " + row);
                tokens.AddRange(parts);
            }
            string grid = string.Join(",", tokens);
            var repo = SyntheticRepo;
            OutpostMap map;
            lock (repo)
            {
                ApplyGrid(repo, 1, grid, rows.Length);
                map = BuildMap(repo, 1);
            }
            return new OutpostFixtureB
            {
                Repo = repo, Map = map, Configs = configs ?? Configs, Zone = 1, ProfileSetup = profileSetup,
                SyntheticGrid = grid, SyntheticDepth = rows.Length,
            };
        }

        public static int Cell(OutpostMap map, int col, int row) => row * map.Width + col;
        public static int RowOf(OutpostMap map, int cell) => cell / map.Width;
        public static int ColOf(OutpostMap map, int cell) => cell % map.Width;

        // ───────────────────────────── Синтетические планировки ─────────────────────────────

        public const string Exit = "e:exit:1:x";
        public const string Camp = "c:spawningcart:1";
        /// <summary>rockbasic уровня 1: исходный HP 16.</summary>
        public const string Rock1 = "r:rockbasic:1";
        /// <summary>rockbasic уровня 2: исходный HP 60.</summary>
        public const string Rock2 = "r:rockbasic:2";
        /// <summary>checkpoint уровня 1: исходный HP 150.</summary>
        public const string Gate1 = "p:checkpoint:1:g";

        /// <summary>
        /// Один камень (3,1), единственный r → rank01 = 0 → 20 HP, 2 порции по 5 (ТЗ §12.5). Depth 3, спавн (3,4).
        /// Id камня = 10. W1 → рабочая клетка (3,2) (2 ребра), W2 → (2,1) (4 ребра; (2,1) и (4,1) равны, меньший индекс).
        /// </summary>
        public static OutpostFixtureB SingleRock(OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                ". . . . . . " + Exit,
                ". . . " + Rock1 + " . . .",
                Camp + " . . . . . .");

        public const int SingleRockId = 10;

        /// <summary>
        /// Два одинаковых камня A (1,2) id 15 и B (5,2) id 19, по 20 HP. Depth 4, спавн (3,5).
        /// W1 → A через (2,2), W2 → B через (4,2): по 4 ребра, удары синхронны, возврат к приёму одновременный.
        /// </summary>
        public static OutpostFixtureB TwoRocks(OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                ". . . . . . " + Exit,
                ". . . . . . .",
                ". " + Rock1 + " . . . " + Rock1 + " .",
                Camp + " . . . . . .");

        public const int TwoRocksA = 15, TwoRocksB = 19;

        /// <summary>
        /// Большой камень B (3,2) id 17: rockbasic:2 при наличии rockbasic:1 → rank01 = 1 → 120 HP, 12 порций по 5.
        /// Малый камень C (0,0) id 0: 20 HP, 2 порции. Depth 4, спавн (3,5).
        /// W1 → (3,3) (2 ребра), W2 → (2,2) (4 ребра).
        /// </summary>
        public static OutpostFixtureB BigRock(OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                Rock1 + " . . . . . " + Exit,
                ". . . . . . .",
                ". . . " + Rock2 + " . . .",
                Camp + " . . . . . .");

        public const int BigRockId = 17, BigRockSmallId = 0;

        /// <summary>
        /// Два сектора: R1 (3,3) id 24 — сектор 0; ворота (1,2) id 15, строка 2; R2 (3,0) id 3 — сектор 1.
        /// r: 20 HP; ворота rank01 = 1 → 20·6·2.5 = 300 HP, 30 порций по 5. Depth 5, спавн (3,6).
        /// </summary>
        public static OutpostFixtureB GateTwoSectors(OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                ". . . " + Rock1 + " . . " + Exit,
                ". . . . . . .",
                ". " + Gate1 + " . . . . .",
                ". . . " + Rock1 + " . . .",
                Camp + " . . . . . .");

        public const int GateR1 = 24, GateId = 15, GateR2 = 3, GateRow = 2;

        /// <summary>Как GateTwoSectors, но выше ворот целей нет: после R1 остаются только ворота.</summary>
        public static OutpostFixtureB GateOnly(OutpostConfigSet configs = null, Action<OutpostProfile> setup = null) =>
            Synthetic(configs, setup,
                ". . . . . . " + Exit,
                ". . . . . . .",
                ". " + Gate1 + " . . . . .",
                ". . . " + Rock1 + " . . .",
                Camp + " . . . . . .");

        // ───────────────────────────── Профиль и состояние ─────────────────────────────

        /// <summary>Новый профиль (как в наборе A): поток наград Pcg32(seed, 5), ТЗ §10.2.</summary>
        public static OutpostProfile NewProfile(ulong seed, int zone)
        {
            return new OutpostProfile
            {
                ModeId = ModeId,
                // ТЗ 2.2 §10.1 и §16.1 (б): фикстура пишет текущую схему (3), как StageProgressionService.NewGame.
                SchemaVersion = OutpostIds.SchemaVersion,
                Seed = seed,
                RewardRng = new Pcg32(seed, 5),
                RewardRngVersion = 1,
                CurrentZone = zone,
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

        public static void Own(OutpostProfile p, string itemId, int level = 1) => p.Items[itemId] = level;

        public static void Wear(OutpostProfile p, string itemId, int level = 1)
        {
            Own(p, itemId, level);
            p.Equipped[SlotOf(itemId)] = itemId;
        }

        public static int SlotOf(string itemId) => Array.IndexOf(SlotNames, itemId.Substring(0, itemId.IndexOf('.')));
        public static int RarityOf(string itemId) => Array.IndexOf(RarityNames, itemId.Substring(itemId.IndexOf('.') + 1));
        public static string ItemId(int slot, int rarity) => SlotNames[slot] + "." + RarityNames[rarity];

        public static OutpostRun CreateRun(OutpostProfile profile, int zone, OutpostMap map, OutpostConfigSet configs, BalanceRepository repo, long nowUtcMs) =>
            StageProgressionService.CreateRun(profile, zone, map, configs, repo, nowUtcMs);

        public static void ApplyCompletion(OutpostGameState state, OutpostConfigSet configs) =>
            StageProgressionService.ApplyCompletion(state, configs);

        /// <summary>Переход к следующей шахте (ТЗ §11.3). Ядро возвращает TransitionResult; отказ — исключение для тестов.</summary>
        public static OutpostGameState PrepareTransition(OutpostGameState state, OutpostConfigSet configs, long nowUtcMs)
        {
            var r = StageProgressionService.PrepareTransition(state, TestData.Balance, configs, nowUtcMs);
            if (r == null || !r.Ok) throw new InvalidOperationException("переход отклонён: " + r?.Reason);
            return r.State;
        }

        /// <summary>Переход; null — отклонён (исключение или пустой результат).</summary>
        public static OutpostGameState TryPrepareTransition(OutpostGameState state, OutpostConfigSet configs, long nowUtcMs)
        {
            try { return PrepareTransition(state, configs, nowUtcMs); }
            catch (InvalidOperationException) { return null; }
            catch (ArgumentException) { return null; }
        }

        public static string RewardKey(int zone) => ModeId + ":zone" + zone + ":first";

        /// <summary>ТЗ §12.3.</summary>
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

        /// <summary>Первая сессия (rewards-2, NoItem): шахты 1 и 2 за первое завершение дают только детали, поток 5 не продвигается.</summary>
        public static bool NoItemZone(int zone) => zone == 1 || zone == 2;

        /// <summary>ТЗ §10.2: слот NextInt(4), затем NextInt(100): 0–69/70–93/94–98/99. Продвигает переданный генератор.</summary>
        public static string ExpectedRandomItem(Pcg32 rng)
        {
            int slot = rng.NextInt(4);
            int roll = rng.NextInt(100);
            int rarity = roll < 70 ? 0 : roll < 94 ? 1 : roll < 99 ? 2 : 3;
            return ItemId(slot, rarity);
        }

        /// <summary>
        /// Тестовая фикстура «доставленного» золота: кошелёк, сумма зачислений и счётчик DeliveredValue растут вместе,
        /// поэтому инварианты §13 (Wallet = TotalCredited − TotalSpent, TotalCredited = DeliveredValue) сохраняются.
        /// Ядро дополнительно ведёт ReleasedValue (выпущено = земля + сумки + зачислено): фикстура считает золото выпущенным и сданным.
        /// Применяется к копии состояния до создания симуляции.
        /// </summary>
        public static void GiveGold(OutpostGameState state, BigInteger amount)
        {
            state.Run.Wallet += amount;
            state.Run.TotalCredited += amount;
            state.Run.Counters.DeliveredValue += amount;
            state.Run.Counters.ReleasedValue += amount;
        }

        // ───────────────────────────── Сериализация ─────────────────────────────

        public static string Json(OutpostGameState state) => JsonConvert.SerializeObject(state, OutpostJson.Settings);
        public static OutpostGameState FromJson(string json) => JsonConvert.DeserializeObject<OutpostGameState>(json, OutpostJson.Settings);
        public static OutpostGameState Clone(OutpostGameState state) => FromJson(Json(state));

        /// <summary>Побайтное сравнение JSON с указанием первого расхождения.</summary>
        public static void AssertSameJson(string expected, string actual, string context)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
            int i = 0, n = Math.Min(expected.Length, actual.Length);
            while (i < n && expected[i] == actual[i]) i++;
            int from = Math.Max(0, i - 160);
            Assert.Fail(context + ": JSON различается с позиции " + i + " (длины " + expected.Length + "/" + actual.Length + ")\n" +
                "ожидалось: …" + Slice(expected, from, 320) + "\nполучено:  …" + Slice(actual, from, 320));
        }

        static string Slice(string s, int from, int len) => from >= s.Length ? "" : s.Substring(from, Math.Min(len, s.Length - from));

        // ───────────────────────────── Команды и сервисы ─────────────────────────────

        public static OutpostCommand Buy(UpgradeBranch branch, BuyMode mode = BuyMode.One) => OutpostCommand.BuyUpgrade(branch, mode);
        public static OutpostCommand EquipCmd(string itemId) => OutpostCommand.Equip(itemId);
        public static OutpostCommand UnequipCmd(EquipSlot slot) => OutpostCommand.Unequip(slot);
        public static OutpostCommand EquipBestCmd() => OutpostCommand.EquipBest();

        public static UpgradeQuote Quote(OutpostSimulation sim, UpgradeBranch branch, BuyMode mode) =>
            TeamUpgradeService.Quote(sim.State, branch, mode);

        // Поля TeamStats (CORE_SPEC §6).
        public static long StatDamageUnits(OutpostSimulation sim) => sim.Stats.DamageUnits;
        public static long StatEdgeTicks(OutpostSimulation sim) => sim.Stats.EdgeTicks;
        public static int StatCapacity(OutpostSimulation sim) => sim.Stats.Capacity;
        public static long StatUnloadTicks(OutpostSimulation sim) => sim.Stats.UnloadTicks;
        public static int StatWorkers(OutpostSimulation sim) => sim.Stats.Workers;

        public static OfflineJob BeginOffline(OutpostGameState committed, OutpostMap map, OutpostConfigSet configs, long nowUtcMs) =>
            new OfflineSimulator(map, configs).Begin(committed, nowUtcMs);

        /// <summary>Офлайн целиком; chunk — maxTicks одного Step. Step возвращает true по завершении.</summary>
        public static OutpostGameState RunOffline(OutpostGameState committed, OutpostMap map, OutpostConfigSet configs, long nowUtcMs,
            out OfflineSummary summary, int chunk = int.MaxValue)
        {
            var job = BeginOffline(committed, map, configs, nowUtcMs);
            long guard = 0;
            while (!job.Step(chunk))
                if (++guard > 50_000_000) Assert.Fail("OfflineJob.Step не завершился");
            summary = job.Summary;
            return job.State;
        }

        // ───────────────────────────── Доступ к состоянию ─────────────────────────────

        public static WorkerState W(OutpostSimulation sim, int id) => sim.State.Run.Workers.First(w => w.Id == id);
        public static TargetState T(OutpostSimulation sim, int id) => sim.State.Run.Targets.First(t => t.Id == id);
        public static MapTarget MT(OutpostMap map, int id) => map.Targets.First(t => t.Id == id);
        public static bool IsGate(OutpostMap map, int targetId) => map.Gates.Any(g => g.TargetId == targetId);
        public static string Key(CargoUnit u) => u.Source + ":" + u.Ordinal;

        public static IEnumerable<CargoUnit> GroundUnits(OutpostRun run) => run.Batches.SelectMany(b => b.Units);
        public static IEnumerable<CargoUnit> BagUnits(OutpostRun run) => run.Workers.SelectMany(w => w.Bag);
        public static IEnumerable<CargoUnit> AllUnits(OutpostRun run) => GroundUnits(run).Concat(BagUnits(run));
        public static int[] Ordinals(IEnumerable<CargoUnit> units) => units.Select(u => u.Ordinal).OrderBy(o => o).ToArray();

        /// <summary>Местоположение и стоимость каждой порции: «ключ → место=стоимость».</summary>
        public static List<string> CargoMap(OutpostRun run)
        {
            var list = new List<string>();
            foreach (var b in run.Batches)
                foreach (var u in b.Units) list.Add(Key(u) + " ground@" + b.Cell + "#" + b.Seq + "=" + u.Value);
            foreach (var w in run.Workers)
                foreach (var u in w.Bag) list.Add(Key(u) + " bag@" + w.Id + "=" + u.Value);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static HashSet<string> UnitKeys(OutpostRun run) => new HashSet<string>(AllUnits(run).Select(Key));

        public static List<OutpostEvent> Drain(OutpostSimulation sim)
        {
            var list = new List<OutpostEvent>();
            sim.DrainEvents(list);
            return list;
        }

        public static bool IsMoving(WorkerState w) => w.Action != null && w.Action.Kind == ActionKind.Edge;

        /// <summary>Оставшиеся клетки пути (начиная со следующей).</summary>
        public static IEnumerable<int> RemainingPath(WorkerState w)
        {
            if (!IsMoving(w) || w.Path == null) yield break;
            for (int i = w.PathIndex; i < w.Path.Length; i++) yield return w.Path[i];
        }

        // ───────────────────────────── Продвижение ─────────────────────────────

        /// <summary>Обработать ровно одну ближайшую отметку событий. false — событий нет.</summary>
        public static bool StepTick(OutpostSimulation sim)
        {
            long t = sim.NextEventTick();
            if (t == long.MaxValue) return false;
            sim.Advance(t);
            return true;
        }

        /// <summary>
        /// Пошаговый прогон по отметкам событий до ReadyToContinue или лимитов. afterTick получает события отметки.
        /// policy — политика C набора B (покупки через Submit после событий отметки), null — политика A.
        /// </summary>
        public static RunReportB RunToReady(OutpostSimulation sim, PolicyCB policy = null, long limitTicks = DayTicks,
            long maxEventTicks = EventLimit, Action<OutpostSimulation, List<OutpostEvent>> afterTick = null)
        {
            var report = new RunReportB();
            Drain(sim);
            while (sim.State.Run.Phase != RunPhase.ReadyToContinue && !sim.State.Run.WatchdogFired)
            {
                long t = sim.NextEventTick();
                if (t == long.MaxValue || t > limitTicks || report.Ticks >= maxEventTicks) break;
                var r = sim.Advance(t);
                report.Ticks++;
                report.Events += r.ProcessedEvents;
                var events = Drain(sim);
                afterTick?.Invoke(sim, events);
                if (policy != null && policy.Act(sim)) report.Purchases++;
            }
            report.Ready = sim.State.Run.Phase == RunPhase.ReadyToContinue;
            report.Watchdog = sim.State.Run.WatchdogFired;
            return report;
        }

        /// <summary>Прогон до ReadyToContinue с проверкой: дошли, watchdog не сработал, в пределах лимитов ТЗ §20.</summary>
        public static RunReportB RunToReadyOrFail(OutpostSimulation sim, string context, PolicyCB policy = null,
            Action<OutpostSimulation, List<OutpostEvent>> afterTick = null)
        {
            var report = RunToReady(sim, policy, DayTicks, EventLimit, afterTick);
            Assert.IsFalse(report.Watchdog, context + ": watchdog: " + sim.State.Run.WatchdogDiagnostic);
            Assert.IsTrue(report.Ready, context + ": ReadyToContinue не достигнут, t=" + sim.Now + ", отметок " + report.Ticks);
            Assert.LessOrEqual(report.Events, (long)EventLimit, context + ": превышен лимит событий");
            return report;
        }

        // ───────────────────────────── Инварианты ─────────────────────────────

        /// <summary>Инварианты ядра (CORE_SPEC §13) и независимые проверки набора B.</summary>
        public static void AssertInvariants(OutpostSimulation sim, string context, bool capacityMayDrop = false)
        {
            var core = OutpostInvariants.Check(sim.State, sim.Map);
            var extra = ExtraViolations(sim, capacityMayDrop);
            if (core.Count + extra.Count == 0) return;
            Assert.Fail(context + " t=" + sim.Now + ":\n" + string.Join("\n", core.Concat(extra).Take(25)));
        }

        /// <summary>
        /// Независимые проверки по ТЗ, без обращения к OutpostInvariants:
        /// ChoosingTask после отметки, будущие сроки таймеров, формула выпуска §7.1, уникальность и порядок ключей,
        /// барьер ворот §8.4, живой footprint, сектор добычи, фазы §11.1, кошелёк, FIFO и места склада §7.4, полная сумка §6.3.
        /// </summary>
        public static List<string> ExtraViolations(OutpostSimulation sim, bool capacityMayDrop = false)
        {
            var v = new List<string>();
            var run = sim.State.Run;
            var map = sim.Map;
            long now = run.Now;

            foreach (var w in run.Workers)
            {
                if (w.Mode == WorkerMode.ChoosingTask) v.Add("w" + w.Id + ": ChoosingTask после отметки (§7.2)");
                if (w.Action != null && w.Action.Deadline <= now) v.Add("w" + w.Id + ": срок таймера " + w.Action.Deadline + " не в будущем (§6.5)");
            }

            // §7.1: выпуск строго по целочисленной формуле, разрушение = Total.
            var byId = new Dictionary<int, TargetState>();
            foreach (var t in run.Targets)
            {
                byId[t.Id] = t;
                long expected = t.RemainingHpUnits == 0
                    ? t.TotalUnits
                    : (long)(new BigInteger(t.TotalUnits) * (t.MaxHpUnits - t.RemainingHpUnits) / t.MaxHpUnits);
                if (t.ReleasedUnits != expected) v.Add("цель " + t.Id + ": Released " + t.ReleasedUnits + " ≠ " + expected + " (§7.1)");
                if (t.Destroyed != (t.RemainingHpUnits == 0)) v.Add("цель " + t.Id + ": Destroyed не согласован с HP");
                if (t.RemainingHpUnits < 0 || t.RemainingHpUnits > t.MaxHpUnits) v.Add("цель " + t.Id + ": HP вне диапазона");
            }

            // Ключи порций уникальны, порядковые номера < Released, сумка в порядке ключей.
            var keys = new HashSet<(int, int)>();
            foreach (var b in run.Batches)
            {
                if (b.Units.Count == 0) v.Add("партия " + b.Seq + " пуста, но не удалена (§7.1 фаза 3)");
                foreach (var u in b.Units)
                {
                    if (!keys.Add((u.Source, u.Ordinal))) v.Add("дубль порции " + Key(u));
                    if (byId.TryGetValue(u.Source, out var t) && u.Ordinal >= t.ReleasedUnits) v.Add("порция " + Key(u) + " не выпущена");
                }
            }
            foreach (var w in run.Workers)
            {
                for (int i = 0; i < w.Bag.Count; i++)
                {
                    var u = w.Bag[i];
                    if (!keys.Add((u.Source, u.Ordinal))) v.Add("дубль порции " + Key(u));
                    if (i > 0)
                    {
                        var p = w.Bag[i - 1];
                        if (p.Source > u.Source || (p.Source == u.Source && p.Ordinal >= u.Ordinal)) v.Add("w" + w.Id + ": сумка не по ключу (§7.4)");
                    }
                }
            }

            // Живой footprint: не стоят, не идут, груз не лежит (§7.2, §8.1).
            var alive = new HashSet<int>();
            foreach (var mt in map.Targets)
                if (byId.TryGetValue(mt.Id, out var ts) && !ts.Destroyed && mt.Cells != null)
                    foreach (int c in mt.Cells) alive.Add(c);
            foreach (var w in run.Workers)
            {
                if (alive.Contains(w.Cell)) v.Add("w" + w.Id + ": в клетке живого footprint " + w.Cell);
                foreach (int c in RemainingPath(w))
                    if (alive.Contains(c)) { v.Add("w" + w.Id + ": путь через живой footprint " + c); break; }
            }
            foreach (var b in run.Batches)
                if (alive.Contains(b.Cell)) v.Add("партия " + b.Seq + " внутри живого footprint");

            // ТЗ 2.2 §4.1: footprint открытого завода (у run есть FactoryState) остаётся стеной — котики и груз на нём не стоят.
            var standing = new HashSet<int>();
            if (run.Factories != null)
                foreach (var f in run.Factories)
                {
                    var mt = map.Targets.FirstOrDefault(o => o.Id == f.TargetId);
                    if (mt != null && mt.Cells != null && byId.TryGetValue(mt.Id, out var fs) && fs.Destroyed)
                        foreach (int c in mt.Cells) standing.Add(c);
                }
            if (standing.Count > 0)
            {
                foreach (var w in run.Workers)
                {
                    if (standing.Contains(w.Cell)) v.Add("w" + w.Id + ": в клетке открытого завода " + w.Cell + " (ТЗ 2.2 §4.1)");
                    foreach (int c in RemainingPath(w))
                        if (standing.Contains(c)) { v.Add("w" + w.Id + ": путь через открытый завод " + c + " (ТЗ 2.2 §4.1)"); break; }
                }
                foreach (var b in run.Batches)
                    if (standing.Contains(b.Cell)) v.Add("партия " + b.Seq + " на footprint открытого завода (ТЗ 2.2 §4.1)");
            }

            // §8.4: барьер закрытых ворот.
            if (run.GatesOpened < map.Gates.Length)
            {
                int barrier = map.Gates[run.GatesOpened].Row;
                foreach (var w in run.Workers)
                {
                    if (RowOf(map, w.Cell) < barrier) v.Add("w" + w.Id + ": выше закрытых ворот (строка " + RowOf(map, w.Cell) + " < " + barrier + ")");
                    foreach (int c in RemainingPath(w))
                        if (RowOf(map, c) < barrier) { v.Add("w" + w.Id + ": путь за закрытые ворота"); break; }
                }
                foreach (var b in run.Batches)
                    if (RowOf(map, b.Cell) < barrier) v.Add("партия " + b.Seq + " за закрытыми воротами");
            }

            // Добыча только в открытом секторе; ворота — задача только после расчистки своего сектора (§8.4).
            foreach (var w in run.Workers)
            {
                if ((w.Mode != WorkerMode.MovingToWork && w.Mode != WorkerMode.Mining) || w.TargetId < 0) continue;
                var mt = MT(map, w.TargetId);
                var gate = map.Gates.FirstOrDefault(g => g.TargetId == mt.Id);
                if (gate != null)
                {
                    if (gate.Order > run.GatesOpened) v.Add("w" + w.Id + ": ворота " + mt.Id + " не очередные");
                    if (gate.Order == run.GatesOpened && map.Targets.Any(o => o.Kind != 'p' && o.Sector == gate.Order && !byId[o.Id].Destroyed))
                        v.Add("w" + w.Id + ": ворота стали задачей до расчистки сектора " + gate.Order);
                }
                else if (mt.Sector > run.GatesOpened) v.Add("w" + w.Id + ": цель " + mt.Id + " в закрытом секторе " + mt.Sector);
            }

            // §11.1: фазы.
            bool allRsx = map.Targets.Where(o => o.Kind != 'p').All(o => byId[o.Id].Destroyed);
            bool allGates = run.GatesOpened == map.Gates.Length;
            bool anyCargo = run.Batches.Count > 0 || run.Workers.Any(w => w.Bag.Count > 0 || w.Mode == WorkerMode.Unloading);
            if (run.Phase == RunPhase.Running && allRsx && allGates) v.Add("Running при полной расчистке (§11.1)");
            if (run.Phase != RunPhase.Running && !(allRsx && allGates)) v.Add(run.Phase + " до полной расчистки и открытия ворот (§11.1)");
            if (run.Phase == RunPhase.DrainingCargo && !anyCargo) v.Add("DrainingCargo без груза не завершён (§11.1)");
            if (run.Phase == RunPhase.DrainingCargo)
                foreach (var w in run.Workers)
                    if (w.Mode == WorkerMode.MovingToWork && !w.TaskInvalid) v.Add("w" + w.Id + ": новая добыча в DrainingCargo (§11.1)");
            if (run.Phase == RunPhase.ReadyToContinue)
            {
                if (anyCargo) v.Add("ReadyToContinue с грузом (§11.3)");
                if (run.Queue.Count > 0) v.Add("ReadyToContinue с очередью");
                if (run.Slots.Any(s => s != null && s.WorkerId != 0)) v.Add("ReadyToContinue с занятым местом");
                if (run.Workers.Any(w => w.Action != null)) v.Add("ReadyToContinue с таймером работника");
            }

            // §7.3: кошелёк.
            // ТЗ 2.2 §6.4, §10.2: золото продажи товара — отдельное слагаемое (GoodsSoldGold не входит в TotalCredited).
            if (run.Wallet != run.TotalCredited + run.BonusCredited + run.GoodsSoldGold - run.TotalSpent || run.Wallet.Sign < 0)
                v.Add("Wallet ≠ TotalCredited + BonusCredited + GoodsSoldGold − TotalSpent (§7.3; ТЗ 2.2 §6.4)");

            // §7.4: очередь FIFO (ArrivalTick, WorkerId), без дублей, WaitingDepot ⇔ в очереди.
            for (int i = 1; i < run.Queue.Count; i++)
            {
                var a = run.Queue[i - 1];
                var b = run.Queue[i];
                if (a.ArrivalTick > b.ArrivalTick || (a.ArrivalTick == b.ArrivalTick && a.WorkerId >= b.WorkerId)) v.Add("очередь не FIFO (§7.4)");
            }
            var queued = new HashSet<int>(run.Queue.Select(q => q.WorkerId));
            foreach (var w in run.Workers)
                if ((w.Mode == WorkerMode.WaitingDepot) != queued.Contains(w.Id)) v.Add("w" + w.Id + ": WaitingDepot ⇎ очередь");

            // Места склада и DepotSlot взаимно согласованы; разгрузка — только на клетке своего места.
            for (int s = 0; s < run.Slots.Length; s++)
            {
                int owner = run.Slots[s] == null ? 0 : run.Slots[s].WorkerId;
                if (owner != 0 && !run.Workers.Any(w => w.Id == owner && w.DepotSlot == s)) v.Add("место " + s + " без владельца");
            }
            foreach (var w in run.Workers)
            {
                if (w.DepotSlot >= 0 && (run.Slots[w.DepotSlot] == null || run.Slots[w.DepotSlot].WorkerId != w.Id)) v.Add("w" + w.Id + ": DepotSlot без места");
                if (w.Mode == WorkerMode.Unloading && (w.DepotSlot < 0 || w.Cell != map.DepotCells[w.DepotSlot])) v.Add("w" + w.Id + ": разгрузка не на клетке места");
            }

            // §6.3 п. 1: полная сумка — подбор и добыча запрещены до доставки. Если вместимость в тесте уменьшалась (смена рюкзака),
            // идущему к работе/грузу разрешено только дойти до ближайшей клетки с некорректной задачей (без телепортации, §6.4).
            {
                int cap = StatCapacity(sim);
                foreach (var w in run.Workers)
                {
                    if (w.Bag.Count < cap) continue;
                    bool moving = w.Mode == WorkerMode.MovingToWork || w.Mode == WorkerMode.MovingToCargo;
                    if (w.Mode == WorkerMode.Mining || w.Mode == WorkerMode.PickingUp || (moving && !(capacityMayDrop && w.TaskInvalid)))
                        v.Add("w" + w.Id + ": полная сумка в режиме " + w.Mode + " (§6.3)");
                }
            }

            // Число работников = StartWorkers + Levels[Team] (§9).
            if (run.Workers.Count != StatWorkers(sim)) v.Add("работников " + run.Workers.Count + " ≠ Stats.Workers " + StatWorkers(sim));

            return v;
        }

        // ───────────────────────────── Аудит ─────────────────────────────

        const string AuditRelativePath = "Docs/TZ/analysis/outpost_v2_audit.json";
        static JObject s_Audit;

        /// <summary>Путь к аудиту: как TestData.ZonesSummaryPath — поиск вверх от корня проекта (копия build/ci).</summary>
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

        public static JObject AuditZone(int zone) => (JObject)((JArray)Audit["zones"]).First(z => (string)z["zone"] == "zone" + zone);

        public static BigInteger AuditBig(JToken token) => BigInteger.Parse(token.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        public static int AuditTokens(int zone, string kind)
        {
            var t = AuditZone(zone)["tokens"][kind];
            return t == null ? 0 : (int)t;
        }

        // ───────────────────────────── Независимые формулы (ТЗ §12) ─────────────────────────────

        public static readonly FracB One = new FracB(1, 1);
        public static readonly FracB Zero = new FracB(0, 1);

        /// <summary>ТЗ §12.1: S(z) = (6/5)^(z−1).</summary>
        public static FracB ZoneScale(int zone) => FracB.Pow(new FracB(6, 5), zone - 1);

        static FracB BranchBase(UpgradeBranch b)
        {
            switch (b)
            {
                case UpgradeBranch.Team: return 50;
                case UpgradeBranch.Pickaxes: return 20;
                case UpgradeBranch.Boots: return 25;
                case UpgradeBranch.Bags: return 25;
                case UpgradeBranch.Unload: return 20;
                // Профиль b8 (первая сессия): «Места разгрузки».
                case UpgradeBranch.Bays: return 120;
                default: return 30;
            }
        }

        static FracB BranchGrowth(UpgradeBranch b)
        {
            switch (b)
            {
                case UpgradeBranch.Team: return new FracB(19, 10);
                case UpgradeBranch.Pickaxes: return new FracB(59, 50);
                case UpgradeBranch.Boots: return new FracB(13, 10);
                case UpgradeBranch.Bags: return new FracB(33, 25);
                case UpgradeBranch.Unload: return new FracB(32, 25);
                case UpgradeBranch.Bays: return 3;
                default: return new FracB(31, 25);
            }
        }

        /// <summary>ТЗ §9: пределы веток; «Оценка» — 40 (профиль b4, в b1–b3 было 50); «Места разгрузки» — 2 (профиль b8).</summary>
        public static int CapOf(UpgradeBranch b)
        {
            switch (b)
            {
                case UpgradeBranch.Team: return 10;
                case UpgradeBranch.Pickaxes: return 20;
                case UpgradeBranch.Boots: return 15;
                case UpgradeBranch.Bags: return 16;
                case UpgradeBranch.Unload: return 20;
                case UpgradeBranch.Bays: return 2;
                default: return 40;
            }
        }

        /// <summary>Профиль b4: удорожание местных улучшений с глубиной CostZoneFactor(z) = 1 + (z−1)/10 (шахта 1 — 1).</summary>
        public static FracB CostZoneFactor(int zone) => One + new FracB(zone - 1, 10);

        static JArray s_PaceScale;

        /// <summary>
        /// K(z) профиля b6 прямо из массива PaceScale встроенного outpost_balance.json (элемент z−1; нет элемента — 1), мимо
        /// OutpostBalanceConfig.ZonePace (решение владельца В4, 15 октября 2026; Docs/Rework/BALANCE_FACTORIES.md «Темп b6: 3 цикла»).
        /// </summary>
        public static FracB PaceOf(int zone)
        {
            if (s_PaceScale == null) s_PaceScale = (JArray)JObject.Parse(OutpostConfigSet.DefaultBalanceJson)["PaceScale"] ?? new JArray();
            return zone >= 1 && zone <= s_PaceScale.Count ? FracB.Parse((string)s_PaceScale[zone - 1]) : One;
        }

        /// <summary>
        /// ТЗ §12.2 и профиль b4: NextCost(z, L) = ceil(S(z) · CostZoneFactor(z) · BaseCost · Growth^L), одно итоговое округление;
        /// профиль b6 (решение владельца В4, 15 октября 2026) — ещё × K(z) (<see cref="PaceOf"/>) до того же единственного ceil.
        /// </summary>
        public static BigInteger NextCostOracle(int zone, UpgradeBranch b, int level) =>
            (ZoneScale(zone) * CostZoneFactor(zone) * PaceOf(zone) * BranchBase(b) * FracB.Pow(BranchGrowth(b), level)).Ceil();

        public static BigInteger SumCostOracle(int zone, UpgradeBranch b, int fromLevel, int count)
        {
            BigInteger sum = 0;
            for (int i = 0; i < count; i++) sum += NextCostOracle(zone, b, fromLevel + i);
            return sum;
        }

        public static int Milestone(int level, params int[] thresholds) => 1 << thresholds.Count(t => level >= t);

        /// <summary>ТЗ §12.3: ItemBonus = RarityBaseBonus · (1 + 0.05·(q − 1)).</summary>
        public static FracB ItemBonus(int rarity, int q) => RarityBaseBonus[rarity] * (One + new FracB(q - 1, 20));

        /// <summary>ТЗ §6.5 и SPEC §2: доли HP за удар = max(1, RoundHalfUp(FinalDamage·10^6)).</summary>
        public static long DamageUnitsOracle(FracB baseDamage, int level, FracB pickBonus)
        {
            var dmg = baseDamage * FracB.Pow(new FracB(61, 50), level) * Milestone(level, 10, 20) * (One + pickBonus);
            return (long)BigInteger.Max(1, (dmg * S).RoundHalfUp());
        }

        /// <summary>ТЗ §6.5: ребро = ceil(1e6 / (BaseSpeed·(1 + 0.08·L)·(1 + BootsBonus))).</summary>
        public static long EdgeTicksOracle(FracB baseSpeed, int level, FracB bootsBonus) =>
            (long)(new FracB(S, 1) / (baseSpeed * (One + new FracB(2, 25) * level) * (One + bootsBonus))).Ceil();

        /// <summary>ТЗ §7.4 и профиль b4: порция = ceil(1e6 / (BaseRate·(1 + 0.2·L))) (в b1–b3 шаг был 0.15).</summary>
        public static long UnloadTicksOracle(FracB baseRate, int level) =>
            (long)(new FracB(S, 1) / (baseRate * (One + new FracB(1, 5) * level))).Ceil();

        /// <summary>
        /// ТЗ §12.4: CargoGoldValue = max(1, ceil(5·S(z)·BaseCoinValueFactor·FinalValueMultiplier));
        /// пороги «Оценки» профиля b4 — 10/25/40.
        /// </summary>
        public static BigInteger CoinOracle(int zone, FracB valueFactor, int valueLevel, FracB hatBonus)
        {
            var mult = FracB.Pow(new FracB(11, 10), valueLevel) * Milestone(valueLevel, 10, 25, 40) * (One + hatBonus);
            return BigInteger.Max(1, (5 * ZoneScale(zone) * valueFactor * mult).Ceil());
        }

        /// <summary>ТЗ §12.4: точный MaxHp (порт adapted_hp из outpost_v2_audit.py), профиль b4: ZoneHpFactor = 1 + (z−1)/25.</summary>
        public static long AdaptedHpOracle(int zone, FracB rank01, int area, char kind)
        {
            FracB typeHp = kind == 'x' ? new FracB(4, 5) : kind == 's' ? new FracB(2, 1) : kind == 'p' ? new FracB(5, 2) : One;
            var coef = 20 * (One + new FracB(zone - 1, 25)) * (One + 5 * rank01) * typeHp;
            if (area >= 7) return (long)(coef * new FracB(5, 2)).Ceil();
            BigInteger nsq = coef.N * coef.N * area, dsq = coef.D * coef.D;
            BigInteger root = ISqrt(nsq / dsq);
            return (long)(root + (root * root * dsq < nsq ? 1 : 0));
        }

        /// <summary>ТЗ §12.4: TotalCargoUnits = max(2, ceil(MaxHp / 10)) — для r/x/p (у цепей завода s груза нет, см. перегрузку с видом).</summary>
        public static int CargoUnitsOracle(long maxHp) => (int)Math.Max(2, (maxHp + 9) / 10);

        /// <summary>ТЗ 2.2 §4.2 и §16.1 (а), адаптер adapter-2: у цепей завода (s) груза нет — 0; r/x/p — max(2, ceil(MaxHp / 10)).</summary>
        public static int CargoUnitsOracle(long maxHp, char kind) => kind == 's' ? 0 : CargoUnitsOracle(maxHp);

        public static BigInteger ISqrt(BigInteger n)
        {
            if (n.Sign <= 0) return 0;
            BigInteger x = (BigInteger)Math.Sqrt((double)n);
            while (x * x > n) x--;
            while ((x + 1) * (x + 1) <= n) x++;
            return x;
        }
    }

    /// <summary>Фикстура: репозиторий, карта, конфигурация, зона и настройка профиля.</summary>
    public sealed class OutpostFixtureB
    {
        public BalanceRepository Repo;
        public OutpostMap Map;
        public OutpostConfigSet Configs;
        public int Zone;
        public Action<OutpostProfile> ProfileSetup;
        internal string SyntheticGrid;
        internal int SyntheticDepth;

        public OutpostGameState NewState(ulong seed = OutpostTestHelpersB.Seed, long nowUtcMs = OutpostTestHelpersB.StartUtcMs)
        {
            var profile = OutpostTestHelpersB.NewProfile(seed, Zone);
            ProfileSetup?.Invoke(profile);
            OutpostRun run;
            if (SyntheticGrid == null)
            {
                run = OutpostTestHelpersB.CreateRun(profile, Zone, Map, Configs, Repo, nowUtcMs);
            }
            else
            {
                // Синтетический репозиторий общий для планировок: перед созданием run вернуть свою сетку.
                lock (Repo)
                {
                    OutpostTestHelpersB.ApplyGrid(Repo, Zone, SyntheticGrid, SyntheticDepth);
                    run = OutpostTestHelpersB.CreateRun(profile, Zone, Map, Configs, Repo, nowUtcMs);
                }
            }
            return new OutpostGameState { Profile = profile, Run = run };
        }

        public OutpostSimulation NewSim(ulong seed = OutpostTestHelpersB.Seed, long nowUtcMs = OutpostTestHelpersB.StartUtcMs) =>
            new OutpostSimulation(NewState(seed, nowUtcMs), Map, Configs);

        public OutpostSimulation Wrap(OutpostGameState state) => new OutpostSimulation(state, Map, Configs);
        public OutpostSimulation Load(string json) => Wrap(OutpostTestHelpersB.FromJson(json));
        public OutpostSimulation Reload(OutpostSimulation sim) => Load(OutpostTestHelpersB.Json(sim.State));
    }

    public sealed class RunReportB
    {
        public long Ticks;
        public long Events;
        public int Purchases;
        public bool Ready;
        public bool Watchdog;
    }

    /// <summary>
    /// Собственная политика C набора B (ТЗ §20): круговой список «Кирки, Команда, Сумки, Обувь, Разгрузка, Оценка», ×1,
    /// после покупки — следующая ветка; ветки на cap пропускаются; при нехватке доставленного золота ждём текущую ветку.
    /// Одна покупка на отметку. Не зависит от OutpostBot.
    /// </summary>
    public sealed class PolicyCB
    {
        int m_Index;
        public int Purchases;

        public bool Act(OutpostSimulation sim)
        {
            if (sim.State.Run.Phase == RunPhase.ReadyToContinue) return false;
            var order = OutpostTestHelpersB.PolicyCOrder;
            for (int k = 0; k < order.Length; k++)
            {
                var branch = order[(m_Index + k) % order.Length];
                var quote = OutpostTestHelpersB.Quote(sim, branch, BuyMode.One);
                if (quote.BlockReason == "cap") continue;
                if (quote.Levels == 0) return false;
                var result = sim.Submit(OutpostTestHelpersB.Buy(branch));
                Assert.IsTrue(result.Ok, "политика C: покупка " + branch + " отклонена: " + result.Reason);
                Assert.AreEqual(quote.Cost, result.Cost, "политика C: цена покупки ≠ цене предложения (§12.2)");
                m_Index = (m_Index + k + 1) % order.Length;
                Purchases++;
                return true;
            }
            return false;
        }
    }

    /// <summary>Точная дробь тестов (независимая от Rational ядра).</summary>
    public readonly struct FracB : IEquatable<FracB>, IComparable<FracB>
    {
        public readonly BigInteger N;
        public readonly BigInteger D;

        public FracB(BigInteger n, BigInteger d)
        {
            if (d.IsZero) throw new DivideByZeroException();
            if (d.Sign < 0) { n = -n; d = -d; }
            var g = BigInteger.GreatestCommonDivisor(n, d);
            if (!g.IsZero && !g.IsOne) { n /= g; d /= g; }
            N = n;
            D = d;
        }

        public static implicit operator FracB(long v) => new FracB(v, 1);

        public static FracB operator +(FracB a, FracB b) => new FracB(a.N * b.D + b.N * a.D, a.D * b.D);
        public static FracB operator -(FracB a, FracB b) => new FracB(a.N * b.D - b.N * a.D, a.D * b.D);
        public static FracB operator *(FracB a, FracB b) => new FracB(a.N * b.N, a.D * b.D);
        public static FracB operator /(FracB a, FracB b) => new FracB(a.N * b.D, a.D * b.N);
        public static bool operator ==(FracB a, FracB b) => a.Equals(b);
        public static bool operator !=(FracB a, FracB b) => !a.Equals(b);
        public static bool operator <(FracB a, FracB b) => a.CompareTo(b) < 0;
        public static bool operator >(FracB a, FracB b) => a.CompareTo(b) > 0;
        public static bool operator <=(FracB a, FracB b) => a.CompareTo(b) <= 0;
        public static bool operator >=(FracB a, FracB b) => a.CompareTo(b) >= 0;

        public static FracB Pow(FracB a, int e) => new FracB(BigInteger.Pow(a.N, e), BigInteger.Pow(a.D, e));

        public BigInteger Floor()
        {
            var q = BigInteger.DivRem(N, D, out var r);
            if (r.Sign < 0) q -= 1;
            return q;
        }

        public BigInteger Ceil() => -new FracB(-N, D).Floor();
        public BigInteger RoundHalfUp() => (this + new FracB(1, 2)).Floor();

        /// <summary>Значение Rational ядра (поля P0/ValueFactor типизированы Rational; в JSON — та же строка "n/d").</summary>
        public static FracB Parse(Rational r) => new FracB(r.Num, r.Den);

        /// <summary>"n/d", "n" или десятичная запись "1.18" — точно.</summary>
        public static FracB Parse(string s)
        {
            s = s.Trim();
            int slash = s.IndexOf('/');
            if (slash >= 0)
                return new FracB(BigInteger.Parse(s.Substring(0, slash), CultureInfo.InvariantCulture),
                                 BigInteger.Parse(s.Substring(slash + 1), CultureInfo.InvariantCulture));
            int dot = s.IndexOf('.');
            if (dot >= 0)
                return new FracB(BigInteger.Parse(s.Remove(dot, 1), CultureInfo.InvariantCulture), BigInteger.Pow(10, s.Length - dot - 1));
            return new FracB(BigInteger.Parse(s, CultureInfo.InvariantCulture), 1);
        }

        public bool Equals(FracB other) => N == other.N && D == other.D;
        public override bool Equals(object obj) => obj is FracB f && Equals(f);
        public override int GetHashCode() => N.GetHashCode() * 31 + D.GetHashCode();
        public int CompareTo(FracB other) => (N * other.D).CompareTo(other.N * D);
        public override string ToString() => D.IsOne ? N.ToString(CultureInfo.InvariantCulture) : N.ToString(CultureInfo.InvariantCulture) + "/" + D.ToString(CultureInfo.InvariantCulture);
    }
}
