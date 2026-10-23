using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Sim
{
    public enum BotZoneOutcome
    {
        Completed,
        Wall,
        Limit,
    }

    /// <summary>Запись журнала действий бота (ТЗ 17.2).</summary>
    public sealed class BotAction
    {
        /// <summary>Успешные команды одного шага решения.</summary>
        public const string KindStep = "step";
        /// <summary>Подряд идущие шаги, в которых были только сбор выплат, ящики и бесплатные сундуки.</summary>
        public const string KindRoutine = "routine";
        /// <summary>Офлайн между сессиями.</summary>
        public const string KindOffline = "offline";
        /// <summary>Начало или продолжение сессии игрока.</summary>
        public const string KindSession = "session";
        /// <summary>Итог зоны.</summary>
        public const string KindResult = "result";

        /// <summary>Симуляционное время от начала зоны, секунды (вместе с офлайном).</summary>
        public double T;
        public string Kind;
        public string Detail;
    }

    public sealed class BotZoneReport
    {
        public int Zone;
        public BotZoneOutcome Outcome;
        public double SimSeconds;
        public double OnlineSeconds;
        public int Hires;
        public int Merges;
        public int MaxCatLevel;
        public int ForgeLevel;
        public int ShaftsAutomated;
        public int Shafts;
        public GameNumber Elixir;
        public int Deliveries;
        public int SkippedAds;
        public int CardUpgrades;
        public string Note;
        /// <summary>Часы сессии в начале зоны: сколько секунд текущей 30-минутной сессии уже сыграно в прошлых зонах.</summary>
        public double SessionClockAtStart;
        /// <summary>Последний момент (секунды от начала зоны), когда было доступно или сделано улучшение. «Стена» = он + 2 ч.</summary>
        public double LastImprovementAt;
        /// <summary>Начисления и траты за зону по ключам журнала (ТЗ 4.4), включая награду выхода.</summary>
        public Dictionary<string, GameNumber> Sources = new Dictionary<string, GameNumber>();
        /// <summary>Исключённые награды за зону (сезонный пропуск, переполнение динамита).</summary>
        public Dictionary<string, double> Excluded = new Dictionary<string, double>();
        public List<BotShaftInfo> ShaftInfo = new List<BotShaftInfo>();
        /// <summary>Журнал действий и симуляционного времени зоны (ТЗ 17.2).</summary>
        public List<BotAction> Actions = new List<BotAction>();

        public GameNumber Source(string key) => Sources.TryGetValue(key, out var v) ? v : GameNumber.Zero;

        public GameNumber SourcePrefix(string prefix)
        {
            var sum = GameNumber.Zero;
            foreach (var kv in Sources)
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) sum += kv.Value;
            return sum;
        }
    }

    /// <summary>Шахта зоны в отчёте бота: когда активирована и когда автоматизирована (секунды от начала зоны, −1 — нет).</summary>
    public sealed class BotShaftInfo
    {
        public int Ordinal;
        public string TypeId;
        public string CardId;
        public int CardLevel;
        public double ActivatedAt = -1;
        public double AutomatedAt = -1;
    }

    /// <summary>
    /// Сценарный бот (ТЗ 17.2): играет по правилам слота Main теми же командами, что и UI.
    /// Сессии — непрерывный цикл через все зоны: 30 мин игры, затем 4 ч офлайна (12.3); часы сессии при переходе зоны
    /// не сбрасываются. Предел на зону — 48 ч симуляции. «Стена» — 2 ч симуляции без единой доступной покупки, улучшения
    /// или назначения, которое увеличивает доход или урон (<see cref="ImprovementAvailable"/>); сбор выплат, ящики,
    /// сундуки и динамит её не прерывают. Бот пишет журнал действий по зонам (<see cref="FormatActionLog"/>).
    /// </summary>
    public sealed class ScenarioBot
    {
        public double DecisionStepSeconds = 1.0;
        public double SessionSeconds = 30 * 60;
        public double OfflineSeconds = 4 * 3600;
        /// <summary>Офлайн считается порциями (ТЗ 15.2: разбиение не меняет результат); после каждой проверяется «стена».</summary>
        public double OfflineChunkSeconds = 15 * 60;
        public double ZoneLimitSeconds = 48 * 3600;
        public double WallSeconds = 2 * 3600;
        public bool ModelOffline = true;

        readonly GameSession m_S;
        readonly StepLog m_Step = new StepLog();
        readonly StepLog m_RoutineLog = new StepLog();
        readonly List<int> m_Work = new List<int>(8);
        /// <summary>Часы текущей сессии: общие для всех зон, сбрасываются только после офлайна.</summary>
        double m_SessionClock;
        int m_Sessions;
        /// <summary>Время зоны текущего шага (для журнала).</summary>
        double m_T;
        int m_Deliveries;
        int m_CardUpgrades;
        BotZoneReport m_Report;
        BotAction m_Routine;
        double m_RoutineEnd;
        bool m_StatsTaken;

        public ScenarioBot(GameSession session)
        {
            m_S = session;
        }

        public BotZoneReport PlayZone()
        {
            var s = m_S;
            int zone = s.Mine.Zone;
            double t = 0, lastProgress = 0;
            m_T = 0;
            m_Deliveries = 0;
            m_CardUpgrades = 0;
            m_Step.Clear();
            m_Routine = null;
            long utc = s.State.LastProcessedUtcMs;
            var report = new BotZoneReport { Zone = zone, SessionClockAtStart = m_SessionClock };
            m_Report = report;
            m_StatsTaken = false;
            var ledgerStart = new Dictionary<string, GameNumber>(s.Player.Ledger);
            var excludedStart = new Dictionary<string, double>(s.Player.ExcludedRewards);
            int skippedStart = s.Player.DeliveryHistory.SkippedAds;
            foreach (var sh in s.Layout.Shafts)
                report.ShaftInfo.Add(new BotShaftInfo { Ordinal = sh.Ordinal, TypeId = sh.TypeId, CardId = sh.CardId, CardLevel = sh.CardLevel });
            if (m_Sessions == 0)
            {
                m_Sessions = 1;
                Log(0, BotAction.KindSession, "начало сессии 1");
            }
            else
            {
                Log(0, BotAction.KindSession, "сессия " + m_Sessions.ToString(CultureInfo.InvariantCulture) + " продолжается: сыграно " +
                                              NumberFormat.FormatDuration(m_SessionClock) + " из " + NumberFormat.FormatDuration(SessionSeconds));
            }
            while (true)
            {
                if (s.Player.CampaignCompleted || s.Mine.Zone != zone)
                {
                    report.Outcome = BotZoneOutcome.Completed;
                    break;
                }
                m_T = t;
                // «Стена» считается от последнего момента, когда улучшение было доступно или сделано.
                bool available = ImprovementAvailable();
                bool improved = Act();
                if (s.Mine.Zone != zone || s.Player.CampaignCompleted)
                {
                    report.Outcome = BotZoneOutcome.Completed;
                    break;
                }
                TrackShafts(report, t);
                if (available || improved) lastProgress = t;
                if (t - lastProgress >= WallSeconds)
                {
                    report.Outcome = BotZoneOutcome.Wall;
                    break;
                }
                if (t > ZoneLimitSeconds)
                {
                    report.Outcome = BotZoneOutcome.Limit;
                    break;
                }
                if (ModelOffline && m_SessionClock >= SessionSeconds)
                {
                    if (PlayOffline(report, ref t, ref utc, ref lastProgress)) break;
                    continue;
                }
                s.Advance(GameSession.SecondsToTicks(DecisionStepSeconds));
                utc += (long)(DecisionStepSeconds * 1000);
                OfflineProgress.MarkProcessed(s, utc);
                t += DecisionStepSeconds;
                m_SessionClock += DecisionStepSeconds;
                report.OnlineSeconds += DecisionStepSeconds;
                s.Events.Clear();
                TrackShafts(report, t);
            }
            CloseRoutine();
            report.LastImprovementAt = report.Outcome == BotZoneOutcome.Completed ? t : lastProgress;
            report.SimSeconds = report.Outcome == BotZoneOutcome.Wall ? lastProgress + WallSeconds : t;
            if (!m_StatsTaken) FillStats(report, zone);
            report.Sources = Delta(ledgerStart, s.Player.Ledger);
            foreach (var kv in s.Player.ExcludedRewards)
            {
                double was = excludedStart.TryGetValue(kv.Key, out double v) ? v : 0;
                if (kv.Value - was > 0) report.Excluded[kv.Key] = kv.Value - was;
            }
            report.SkippedAds = s.Player.DeliveryHistory.SkippedAds - skippedStart;
            report.CardUpgrades = m_CardUpgrades;
            Log(report.SimSeconds, BotAction.KindResult, ResultText(report));
            m_Report = null;
            return report;
        }

        /// <summary>
        /// Офлайн между сессиями: порции по <see cref="OfflineChunkSeconds"/> без действий игрока; после каждой порции
        /// проверяется доступность улучшения. true — зона закончилась «стеной» или пределом во время офлайна.
        /// </summary>
        bool PlayOffline(BotZoneReport report, ref double t, ref long utc, ref double lastProgress)
        {
            var s = m_S;
            CloseRoutine();
            double start = t, done = 0;
            var gold = GameNumber.Zero;
            var elixir = GameNumber.Zero;
            int rocks = 0, targets = 0, chunks = 0;
            bool ended = false;
            double chunkSeconds = OfflineChunkSeconds > 0 ? OfflineChunkSeconds : OfflineSeconds;
            while (done < OfflineSeconds - 1e-9)
            {
                double chunk = Math.Min(chunkSeconds, OfflineSeconds - done);
                utc += (long)Math.Round(chunk * 1000);
                var sum = OfflineProgress.Apply(s, utc);
                s.Events.Clear();
                gold += sum.Gold;
                elixir += sum.Elixir;
                rocks += sum.RocksDestroyed;
                targets += sum.TargetsBroken;
                chunks++;
                done += chunk;
                t += chunk;
                TrackShafts(report, t);
                if (ImprovementAvailable()) lastProgress = t;
                if (t - lastProgress >= WallSeconds)
                {
                    report.Outcome = BotZoneOutcome.Wall;
                    ended = true;
                    break;
                }
                if (t > ZoneLimitSeconds)
                {
                    report.Outcome = BotZoneOutcome.Limit;
                    ended = true;
                    break;
                }
            }
            m_SessionClock = 0;
            var inv = CultureInfo.InvariantCulture;
            var text = new StringBuilder("офлайн " + NumberFormat.FormatDuration(done));
            if (chunks > 1) text.Append(" (").Append(chunks.ToString(inv)).Append(" порций)");
            text.Append(": золото +").Append(NumberFormat.Format(gold)).Append(", эликсир +").Append(NumberFormat.Format(elixir))
                .Append(", камней ").Append(rocks.ToString(inv));
            if (targets > 0) text.Append(", шахт, ворот и выхода расчищено ").Append(targets.ToString(inv));
            Log(start, BotAction.KindOffline, text.ToString());
            if (!ended)
            {
                m_Sessions++;
                Log(t, BotAction.KindSession, "начало сессии " + m_Sessions.ToString(inv));
            }
            return ended;
        }

        static Dictionary<string, GameNumber> Delta(Dictionary<string, GameNumber> from, Dictionary<string, GameNumber> to)
        {
            var d = new Dictionary<string, GameNumber>();
            foreach (var kv in to)
            {
                var was = from.TryGetValue(kv.Key, out var v) ? v : GameNumber.Zero;
                var diff = kv.Value - was;
                if (diff.IsPositive) d[kv.Key] = diff;
            }
            return d;
        }

        /// <summary>Отметить момент активации и автоматизации шахт (вызывается, пока бот в этой зоне).</summary>
        void TrackShafts(BotZoneReport r, double t)
        {
            var s = m_S;
            if (s.Mine.Zone != r.Zone) return;
            foreach (var info in r.ShaftInfo)
            {
                var st = s.Mine.Objects[info.Ordinal];
                if (info.ActivatedAt < 0 && st.Status == ObjectStatus.Producing) info.ActivatedAt = t;
                if (info.AutomatedAt < 0 && st.Gen != null && st.Gen.Automated) info.AutomatedAt = t;
            }
        }

        void FillStats(BotZoneReport r, int zone)
        {
            var s = m_S;
            r.Elixir = s.Player.Elixir;
            r.Deliveries = m_Deliveries;
            if (s.Mine.Zone == zone)
            {
                r.Hires = s.Mine.CatsHired;
                r.Merges = s.Mine.Merges;
                foreach (var c in s.Mine.Cats) r.MaxCatLevel = Math.Max(r.MaxCatLevel, c.Level);
                r.ForgeLevel = s.Mine.Objects[s.Layout.Cart.Ordinal].Gen.Level;
                foreach (var sh in s.Layout.Shafts)
                {
                    r.Shafts++;
                    var gen = s.Mine.Objects[sh.Ordinal].Gen;
                    if (gen != null && gen.Automated) r.ShaftsAutomated++;
                }
            }
        }

        // ───────────────────────────── Критерий «стены» ─────────────────────────────

        /// <summary>
        /// Доступно улучшение, которое увеличивает доход или урон (критерий «стены», ТЗ 17.2): наём; улучшение производящего
        /// генератора, на которое хватает золота (резерв на наём не учитывается); улучшение карты, дающее автоматизацию шахты
        /// зоны или доход либо урон (<see cref="CardUpgradeImproves"/>); активация расчищенной шахты или открытие разрушенных
        /// ворот; свободный котик и цель, которая его принимает; пара котиков одного уровня; переход в следующую зону.
        /// Сбор выплат, ящики, сундуки и динамит улучшением не считаются. Проверка не меняет состояние.
        /// </summary>
        public bool ImprovementAvailable()
        {
            var s = m_S;
            if (s.Player.CampaignCompleted) return false;
            if (s.CanCompleteZone(out _)) return true;
            if (s.CanHire() == CommandResult.Ok) return true;
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                var obj = s.Layout.Objects[ord];
                var st = s.Mine.Objects[ord];
                if (obj.Kind == ObjectKind.Shaft && st.Status == ObjectStatus.ReadyToActivate) return true;
                if (obj.Kind == ObjectKind.Gate && st.Status == ObjectStatus.Broken && obj.GateOrder == s.Mine.GatesOpened) return true;
                if (s.IsGenerator(ord) && st.Gen.Level < s.MaxGeneratorLevel(ord) && s.Mine.Gold >= s.UpgradeCost(ord, st.Gen.Level)) return true;
            }
            foreach (var card in s.Balance.CardsInOrder)
                if (s.CardLevel(card.Id) >= 1 && CardUpgradeImproves(card) && s.CanUpgradeCard(card.Id) == CommandResult.Ok) return true;
            var cats = s.Mine.Cats;
            for (int i = 0; i < cats.Count; i++)
                for (int j = i + 1; j < cats.Count; j++)
                    if (cats[i].Level == cats[j].Level) return true;
            foreach (var cat in cats)
            {
                if (cat.TargetIndex >= 0) continue;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                    if (s.Layout.Objects[ord].IsTarget && s.AcceptsCat(ord, cat.Id)) return true;
            }
            return false;
        }

        /// <summary>
        /// Улучшение карты на следующий уровень полезно в текущей зоне: доводит карту до уровня, который требует токен шахты
        /// (автоматизация), или повышает доход либо урон — код 10 (доход всех генераторов), код 9 с целью — генератором
        /// этой зоны (кузница или шахта зоны), код 1 (лимит котиков), код 7, пока интервал бесплатного котика выше минимума.
        /// </summary>
        public bool CardUpgradeImproves(CardDto card)
        {
            var s = m_S;
            if (!GameSession.IsAppliedCardCode(card.StatModifierType)) return false;
            int level = s.CardLevel(card.Id);
            foreach (var sh in s.Layout.Shafts)
                if (sh.CardId == card.Id && level < sh.CardLevel && level + 1 >= sh.CardLevel) return true;
            switch (card.StatModifierType)
            {
                case 10: return true;
                case 1: return true;
                case 7: return s.FreeCatIntervalSeconds() > GameSession.MinFreeCatIntervalSeconds;
                case 9: return TargetsZoneShaft(card) || TargetsForge(card);
                default: return false;
            }
        }

        bool TargetsZoneShaft(CardDto card)
        {
            if (card.TargetIds == null) return false;
            foreach (var sh in m_S.Layout.Shafts)
                if (Array.IndexOf(card.TargetIds, sh.TypeId) >= 0) return true;
            return false;
        }

        static bool TargetsForge(CardDto card) => card.TargetIds != null && Array.IndexOf(card.TargetIds, BalanceRepository.CartId) >= 0;

        // ───────────────────────────── Решения ─────────────────────────────

        /// <summary>
        /// Одно решение. Возвращает true, если было улучшение, которое увеличивает доход или урон (наём, улучшение генератора,
        /// полезное улучшение карты, активация шахты, открытие ворот, назначение свободного котика, объединение, переход зоны).
        /// Сбор выплат, ящики, сундуки и динамит выполняются, но улучшением не считаются.
        /// </summary>
        bool Act()
        {
            var s = m_S;
            var log = m_Step;
            log.Clear();
            bool improved = false;

            if (s.CanCompleteZone(out _))
            {
                FillStats(m_Report, s.Mine.Zone);
                m_StatsTaken = true;
                int zone = s.Mine.Zone;
                if (s.CompleteZone() == CommandResult.Ok)
                    log.Events.Add(s.Player.CampaignCompleted ? "zone" + zone.ToString(CultureInfo.InvariantCulture) + " завершена, кампания пройдена"
                        : "переход: zone" + zone.ToString(CultureInfo.InvariantCulture) + " → zone" + s.Mine.Zone.ToString(CultureInfo.InvariantCulture));
                FlushStep();
                return true;
            }

            // Контейнеры и сундуки.
            foreach (var c in s.Mine.Containers.ToArray())
            {
                var kind = c.Kind;
                if (s.OpenContainer(c.Id) != CommandResult.Ok) continue;
                if (kind == ContainerKind.Delivery)
                {
                    m_Deliveries++;
                    log.Deliveries++;
                }
                else log.FreeCats++;
            }
            while (s.Player.FreeChest.Enabled && s.Player.FreeChest.Stock > 0)
            {
                if (s.ClaimFreeChest() != CommandResult.Ok) break;
                log.FreeChests++;
            }
            // Окно сундука закрывается сразу: содержимое уже начислено.
            for (int i = 0; i < 1000 && s.TakeChestReveal() != null; i++) { }

            // Объекты: активация, ворота, выплаты.
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                var obj = s.Layout.Objects[ord];
                var st = s.Mine.Objects[ord];
                if (obj.Kind == ObjectKind.Shaft && st.Status == ObjectStatus.ReadyToActivate && s.ActivateShaft(ord) == CommandResult.Ok)
                {
                    log.Events.Add("активация " + GeneratorName(ord));
                    improved = true;
                }
                if (obj.Kind == ObjectKind.Gate && st.Status == ObjectStatus.Broken && s.OpenGate(ord) == CommandResult.Ok)
                {
                    log.Events.Add("ворота " + (obj.GateOrder + 1).ToString(CultureInfo.InvariantCulture) + " открыты");
                    improved = true;
                }
                if (st.Gen != null && st.Gen.PayoutReady && s.CollectPayout(ord) == CommandResult.Ok) log.Payouts++;
            }

            // Карточки: сначала автоматизация шахт зоны, затем доход — без траты резерва на следующую автоматизацию.
            improved |= UpgradeCards();

            // Динамит — в самую «дорогую» доступную цель.
            if (s.Player.Dynamite > 0)
            {
                int best = BestTargetForDynamite();
                if (best >= 0)
                {
                    var o = s.Layout.Objects[best];
                    if (s.UseDynamite(s.Layout.Col(o.Cells[0]) + 0.5, s.Layout.Row(o.Cells[0]) + 0.5) == CommandResult.Ok)
                        log.Events.Add("динамит → " + o.TypeId + " ур. " + o.Level.ToString(CultureInfo.InvariantCulture));
                }
            }

            // Объединение пар одного уровня (сначала свободные котики).
            improved |= MergePairs();

            // Наём, затем улучшения генераторов.
            while (s.CanHire() == CommandResult.Ok)
            {
                int level = s.HireLevel();
                if (s.Hire() != CommandResult.Ok) break;
                log.Hires++;
                log.HireLevelMin = Math.Min(log.HireLevelMin, level);
                log.HireLevelMax = Math.Max(log.HireLevelMax, level);
                improved = true;
                MergePairs();
            }
            improved |= BuyUpgrades();

            // Назначение свободных котиков.
            improved |= AssignIdleCats();
            FlushStep();
            return improved;
        }

        /// <summary>Объединить все пары котиков одного уровня. true — было хотя бы одно объединение.</summary>
        public bool MergePairs()
        {
            var s = m_S;
            bool merged = false;
            bool again = true;
            while (again)
            {
                again = false;
                var cats = s.Mine.Cats;
                for (int i = 0; i < cats.Count && !again; i++)
                {
                    for (int j = i + 1; j < cats.Count && !again; j++)
                    {
                        if (cats[i].Level != cats[j].Level) continue;
                        // Перетаскиваем свободного на работающего (или первого на второго).
                        var drag = cats[i].TargetIndex < 0 ? cats[i] : cats[j];
                        var recv = drag == cats[i] ? cats[j] : cats[i];
                        var r = s.DropCat(drag.Id, s.Layout.Col(recv.Cell) + 0.5, s.Layout.Row(recv.Cell) + 0.5);
                        if (r.Merged)
                        {
                            merged = true;
                            again = true;
                            m_Step.Merges++;
                            m_Step.MergeLevelMax = Math.Max(m_Step.MergeLevelMax, recv.Level);
                        }
                    }
                }
            }
            return merged;
        }

        /// <summary>
        /// Улучшить самый выгодный генератор (ТЗ 17.2): кандидаты — следующий уровень и «до порога» каждого производящего
        /// генератора; выбирается наименьшая окупаемость «цена / прирост дохода в секунду»
        /// (<see cref="EconomyTables.IncomePerSecondAt"/>). Если лучший вариант пока не по карману (с резервом на наём,
        /// пока лимит не заполнен), бот копит на него, а не покупает худший, но дешёвый.
        /// </summary>
        public bool BuyUpgrades()
        {
            var s = m_S;
            bool bought = false;
            for (int guard = 0; guard < 200; guard++)
            {
                var reserve = s.OccupiedSlots < s.UnitCap() ? s.HirePrice() * GameNumber.FromDouble(0.5) : GameNumber.Zero;
                // Дешевле следующего уровня ни один вариант не стоит: если и на него не хватает — выбирать нечего.
                bool any = false;
                var cheapest = GameNumber.Zero;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    if (!s.IsGenerator(ord)) continue;
                    var gen = s.Mine.Objects[ord].Gen;
                    if (gen.Level >= s.MaxGeneratorLevel(ord)) continue;
                    var cost = s.UpgradeCost(ord, gen.Level);
                    if (!any || cost < cheapest) cheapest = cost;
                    any = true;
                }
                if (!any || s.Mine.Gold < cheapest + reserve) break;

                int bestOrd = -1;
                bool bestToThreshold = false;
                var bestCost = GameNumber.Zero;
                var bestRatio = GameNumber.Zero;
                for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
                {
                    if (!s.IsGenerator(ord)) continue;
                    var gen = s.Mine.Objects[ord].Gen;
                    int level = gen.Level;
                    if (level >= s.MaxGeneratorLevel(ord)) continue;
                    var now = EconomyTables.IncomePerSecondAt(s, ord, level);
                    var cost = s.UpgradeCost(ord, level);
                    Consider(ord, false, cost, EconomyTables.IncomePerSecondAt(s, ord, level + 1) - now, ref bestOrd, ref bestToThreshold, ref bestCost, ref bestRatio);
                    int target = s.NextThresholdLevel(ord);
                    if (target > level + 1)
                    {
                        var total = GameNumber.Zero;
                        for (int L = level; L < target; L++) total += s.UpgradeCost(ord, L);
                        Consider(ord, true, total, EconomyTables.IncomePerSecondAt(s, ord, target) - now, ref bestOrd, ref bestToThreshold, ref bestCost, ref bestRatio);
                    }
                }
                if (bestOrd < 0 || s.Mine.Gold < bestCost + reserve) break;
                int from = s.Mine.Objects[bestOrd].Gen.Level;
                var r = bestToThreshold ? s.UpgradeToThreshold(bestOrd, out _) : s.UpgradeGenerator(bestOrd, out _, 1);
                if (r != CommandResult.Ok) break;
                m_Step.AddUpgrade(GeneratorName(bestOrd), from, s.Mine.Objects[bestOrd].Gen.Level);
                bought = true;
            }
            s.Events.Clear();
            return bought;
        }

        static void Consider(int ord, bool toThreshold, GameNumber cost, GameNumber gain,
                             ref int bestOrd, ref bool bestToThreshold, ref GameNumber bestCost, ref GameNumber bestRatio)
        {
            // Окупаемость в секундах; прироста нет — сравнивается сама цена.
            var ratio = gain.IsPositive ? cost / gain : cost;
            if (bestOrd >= 0 && ratio >= bestRatio) return;
            bestOrd = ord;
            bestToThreshold = toThreshold;
            bestCost = cost;
            bestRatio = ratio;
        }

        /// <summary>
        /// Улучшение карт (ТЗ 17.2 — «когда это даёт автоматизацию»). Проход 1: карты токенов шахт текущей зоны
        /// (сначала производящих шахт) поднимаются до требуемого уровня. Затем запоминается эликсир на следующий шаг
        /// автоматизации, который пока не выполнить. Проход 2: прочие полезные карты (<see cref="CardUpgradeImproves"/>) —
        /// managers шахт зоны, коды 10, 1, 7, manager кузницы последней — только если после оплаты эликсира не меньше этого резерва.
        /// </summary>
        public bool UpgradeCards()
        {
            var s = m_S;
            bool improved = false;
            var plan = AutomationPlan();
            foreach (var step in plan)
            {
                while (s.CardLevel(step.Key) < step.Value && s.CanUpgradeCard(step.Key) == CommandResult.Ok)
                {
                    if (!UpgradeCard(step.Key)) break;
                    improved = true;
                }
            }
            var reserve = GameNumber.Zero;
            foreach (var step in plan)
            {
                if (s.CardLevel(step.Key) >= step.Value) continue;
                if (s.TryGetCardUpgradeCost(step.Key, out _, out var elixir))
                {
                    reserve = elixir;
                    break;
                }
            }
            foreach (var card in IncomeCardsInOrder())
            {
                while (s.CanUpgradeCard(card.Id) == CommandResult.Ok && CardUpgradeImproves(card) &&
                       s.TryGetCardUpgradeCost(card.Id, out _, out var cost) && s.Player.Elixir - cost >= reserve)
                {
                    if (!UpgradeCard(card.Id)) break;
                    improved = true;
                }
            }
            return improved;
        }

        bool UpgradeCard(string cardId)
        {
            var s = m_S;
            int from = s.CardLevel(cardId);
            int automatedBefore = AutomatedShafts();
            if (s.UpgradeCard(cardId) != CommandResult.Ok) return false;
            m_CardUpgrades++;
            m_Step.AddCard(cardId, from, s.CardLevel(cardId), AutomatedShafts() > automatedBefore);
            return true;
        }

        int AutomatedShafts()
        {
            var s = m_S;
            int n = 0;
            foreach (var sh in s.Layout.Shafts)
            {
                var gen = s.Mine.Objects[sh.Ordinal].Gen;
                if (gen != null && gen.Automated) n++;
            }
            return n;
        }

        /// <summary>Карты токенов шахт зоны и требуемый уровень (максимум по токенам); сначала производящие шахты.</summary>
        List<KeyValuePair<string, int>> AutomationPlan()
        {
            var s = m_S;
            var plan = new List<KeyValuePair<string, int>>();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var sh in s.Layout.Shafts)
                {
                    if (string.IsNullOrEmpty(sh.CardId)) continue;
                    bool producing = s.Mine.Objects[sh.Ordinal].Status == ObjectStatus.Producing;
                    if (producing != (pass == 0)) continue;
                    int required = 0;
                    foreach (var other in s.Layout.Shafts)
                        if (other.CardId == sh.CardId) required = Math.Max(required, other.CardLevel);
                    bool seen = false;
                    foreach (var p in plan) if (p.Key == sh.CardId) seen = true;
                    if (!seen) plan.Add(new KeyValuePair<string, int>(sh.CardId, required));
                }
            }
            return plan;
        }

        /// <summary>Порядок второго прохода: managers шахт зоны, коды 10, 1, 7, manager кузницы последней.</summary>
        List<CardDto> IncomeCardsInOrder()
        {
            var s = m_S;
            var result = new List<CardDto>();
            var groups = new[] { new List<CardDto>(), new List<CardDto>(), new List<CardDto>(), new List<CardDto>(), new List<CardDto>() };
            foreach (var card in s.Balance.CardsInOrder)
            {
                if (s.CardLevel(card.Id) < 1) continue;
                switch (card.StatModifierType)
                {
                    case 9:
                        if (TargetsZoneShaft(card)) groups[0].Add(card);
                        else if (TargetsForge(card)) groups[4].Add(card);
                        break;
                    case 10: groups[1].Add(card); break;
                    case 1: groups[2].Add(card); break;
                    case 7: groups[3].Add(card); break;
                }
            }
            foreach (var g in groups) result.AddRange(g);
            return result;
        }

        /// <summary>
        /// Назначить свободных котиков на ближайшие цели, которые принимают котика (ТЗ 17.2, 7.3). Ближайшая — по манхэттенскому
        /// расстоянию от клетки котика до свободной рабочей клетки цели; при равенстве — меньшая строка, затем меньший столбец
        /// рабочей клетки, затем меньший ordinal цели. Котик отпускается на клетку footprint цели рядом с выбранной рабочей клеткой.
        /// </summary>
        public bool AssignIdleCats()
        {
            var s = m_S;
            var layout = s.Layout;
            bool assigned = false;
            // Сильные котики — первыми.
            var idle = new List<CatState>();
            foreach (var c in s.Mine.Cats) if (c.TargetIndex < 0) idle.Add(c);
            idle.Sort((a, b) => b.Level.CompareTo(a.Level));
            foreach (var cat in idle)
            {
                if (cat.TargetIndex >= 0 || s.FindCat(cat.Id) == null) continue;
                int bestOrd = -1, bestWork = -1, bestDist = int.MaxValue;
                for (int ord = 0; ord < layout.Objects.Count; ord++)
                {
                    if (!layout.Objects[ord].IsTarget || !s.IsTargetAvailable(ord)) continue;
                    s.WorkCells(ord, m_Work);
                    foreach (int w in m_Work)
                    {
                        var other = s.CatAt(w);
                        if (other != null && other.Id != cat.Id) continue;
                        int d = Manhattan(layout, cat.Cell, w);
                        if (bestOrd >= 0 && (d > bestDist || (d == bestDist && !Before(layout, w, bestWork)))) continue;
                        bestOrd = ord;
                        bestWork = w;
                        bestDist = d;
                    }
                }
                if (bestOrd < 0) continue;
                int drop = -1;
                foreach (int cell in layout.Objects[bestOrd].Cells)
                    if (Manhattan(layout, cell, bestWork) == 1) { drop = cell; break; }
                if (drop < 0) drop = layout.Objects[bestOrd].Cells[0];
                var r = s.DropCat(cat.Id, layout.Col(drop) + 0.5, layout.Row(drop) + 0.5);
                if (r.Result == CommandResult.Ok && r.TargetIndex >= 0)
                {
                    assigned = true;
                    m_Step.Assigns++;
                }
            }
            return assigned;
        }

        static int Manhattan(ZoneLayout l, int a, int b) => Math.Abs(l.Col(a) - l.Col(b)) + Math.Abs(l.Row(a) - l.Row(b));

        /// <summary>Клетка a раньше b: меньшая строка, затем меньший столбец.</summary>
        static bool Before(ZoneLayout l, int a, int b)
        {
            int ra = l.Row(a), rb = l.Row(b);
            if (ra != rb) return ra < rb;
            return l.Col(a) < l.Col(b);
        }

        int BestTargetForDynamite()
        {
            var s = m_S;
            int best = -1;
            GameNumber bestHp = GameNumber.Zero;
            for (int ord = 0; ord < s.Layout.Objects.Count; ord++)
            {
                if (!s.IsTargetAvailable(ord)) continue;
                var hp = s.Mine.Objects[ord].Hp;
                if (best < 0 || hp > bestHp)
                {
                    best = ord;
                    bestHp = hp;
                }
            }
            return best;
        }

        string GeneratorName(int ord)
        {
            var o = m_S.Layout.Objects[ord];
            if (o.Kind == ObjectKind.Cart) return "кузница";
            return o.TypeId + " (ряд " + o.Row.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // ───────────────────────────── Журнал ─────────────────────────────

        void Log(double t, string kind, string detail)
        {
            if (m_Report == null) return;
            CloseRoutine();
            m_Report.Actions.Add(new BotAction { T = t, Kind = kind, Detail = detail });
        }

        /// <summary>Записать успешные команды шага: рутинные шаги подряд сводятся в одну запись.</summary>
        void FlushStep()
        {
            var log = m_Step;
            if (m_Report == null || log.IsEmpty) return;
            if (log.HasNotable)
            {
                Log(m_T, BotAction.KindStep, log.Format());
                return;
            }
            if (m_Routine == null)
            {
                m_Routine = new BotAction { T = m_T, Kind = BotAction.KindRoutine };
                m_Report.Actions.Add(m_Routine);
                m_RoutineLog.Clear();
            }
            m_RoutineLog.AddRoutine(log);
            m_RoutineEnd = m_T;
        }

        void CloseRoutine()
        {
            if (m_Routine == null) return;
            m_Routine.Detail = m_RoutineLog.Format() + (m_RoutineEnd > m_Routine.T ? " (до " + Clock(m_RoutineEnd) + ")" : "");
            m_Routine = null;
        }

        static string ResultText(BotZoneReport r)
        {
            switch (r.Outcome)
            {
                case BotZoneOutcome.Completed:
                    return "зона пройдена за " + NumberFormat.FormatDuration(r.SimSeconds) + ", онлайн " + NumberFormat.FormatDuration(r.OnlineSeconds);
                case BotZoneOutcome.Wall:
                    return "«стена»: с " + Clock(r.LastImprovementAt) + " 2 ч без доступной покупки, улучшения или назначения, которое увеличивает доход или урон";
                default:
                    return "предел 48 ч симуляции на зону";
            }
        }

        /// <summary>Время в журнале: ч:мм:сс.</summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            long s = (long)Math.Round(seconds);
            var inv = CultureInfo.InvariantCulture;
            return (s / 3600).ToString(inv) + ":" + (s / 60 % 60).ToString("00", inv) + ":" + (s % 60).ToString("00", inv);
        }

        /// <summary>Успешные команды одного шага решения (или серии рутинных шагов) для журнала.</summary>
        sealed class StepLog
        {
            public readonly List<string> Events = new List<string>();
            public int Hires, HireLevelMin = int.MaxValue, HireLevelMax;
            public int Merges, MergeLevelMax;
            public int Assigns;
            public int Payouts, Deliveries, FreeCats, FreeChests;
            readonly List<string> m_UpgradeOrder = new List<string>();
            readonly Dictionary<string, int[]> m_Upgrades = new Dictionary<string, int[]>();
            readonly List<string> m_CardOrder = new List<string>();
            readonly Dictionary<string, int[]> m_Cards = new Dictionary<string, int[]>();

            /// <summary>Есть что-то кроме сбора выплат, ящиков и бесплатных сундуков.</summary>
            public bool HasNotable => Events.Count > 0 || Hires > 0 || Merges > 0 || Assigns > 0 || m_UpgradeOrder.Count > 0 || m_CardOrder.Count > 0;
            public bool IsEmpty => !HasNotable && Payouts == 0 && Deliveries == 0 && FreeCats == 0 && FreeChests == 0;

            public void Clear()
            {
                Events.Clear();
                Hires = Merges = Assigns = Payouts = Deliveries = FreeCats = FreeChests = 0;
                HireLevelMin = int.MaxValue;
                HireLevelMax = MergeLevelMax = 0;
                m_UpgradeOrder.Clear();
                m_Upgrades.Clear();
                m_CardOrder.Clear();
                m_Cards.Clear();
            }

            public void AddUpgrade(string name, int from, int to)
            {
                if (m_Upgrades.TryGetValue(name, out var v)) v[1] = to;
                else
                {
                    m_Upgrades[name] = new[] { from, to };
                    m_UpgradeOrder.Add(name);
                }
            }

            public void AddCard(string cardId, int from, int to, bool automation)
            {
                if (m_Cards.TryGetValue(cardId, out var v))
                {
                    v[1] = to;
                    if (automation) v[2] = 1;
                }
                else
                {
                    m_Cards[cardId] = new[] { from, to, automation ? 1 : 0 };
                    m_CardOrder.Add(cardId);
                }
            }

            public void AddRoutine(StepLog o)
            {
                Payouts += o.Payouts;
                Deliveries += o.Deliveries;
                FreeCats += o.FreeCats;
                FreeChests += o.FreeChests;
            }

            public string Format()
            {
                var inv = CultureInfo.InvariantCulture;
                var parts = new List<string>(Events);
                if (Hires > 0)
                    parts.Add("наём ×" + Hires.ToString(inv) + " (ур. " + (HireLevelMin == HireLevelMax ? HireLevelMax.ToString(inv)
                                                                              : HireLevelMin.ToString(inv) + "–" + HireLevelMax.ToString(inv)) + ")");
                if (Merges > 0) parts.Add("объединение ×" + Merges.ToString(inv) + " (до ур. " + MergeLevelMax.ToString(inv) + ")");
                foreach (var name in m_UpgradeOrder)
                {
                    var v = m_Upgrades[name];
                    parts.Add(name + " → ур. " + v[1].ToString(inv) + " (+" + (v[1] - v[0]).ToString(inv) + ")");
                }
                foreach (var id in m_CardOrder)
                {
                    var v = m_Cards[id];
                    parts.Add("чертёж " + id + " → ур. " + v[1].ToString(inv) + (v[2] != 0 ? ", автоматизация" : ""));
                }
                if (Assigns > 0) parts.Add("назначение ×" + Assigns.ToString(inv));
                if (Payouts > 0) parts.Add("выплата ×" + Payouts.ToString(inv));
                if (Deliveries > 0) parts.Add("доставка ×" + Deliveries.ToString(inv));
                if (FreeCats > 0) parts.Add("бесплатный котик ×" + FreeCats.ToString(inv));
                if (FreeChests > 0) parts.Add("бесплатный сундук ×" + FreeChests.ToString(inv));
                return string.Join("; ", parts);
            }
        }

        // ───────────────────────────── Отчёты ─────────────────────────────

        static string Share(GameNumber part, GameNumber total)
        {
            if (!part.IsPositive) return "0";
            if (!total.IsPositive) return NumberFormat.Format(part);
            double p = (part / total).ToDouble() * 100.0;
            return NumberFormat.Format(part) + " (" + p.ToString("0", CultureInfo.InvariantCulture) + "%)";
        }

        static string At(double seconds) => seconds < 0 ? "нет" : NumberFormat.FormatDuration(seconds);

        static string Outcome(BotZoneReport r) =>
            r.Outcome == BotZoneOutcome.Completed ? "пройдена за " + NumberFormat.FormatDuration(r.SimSeconds)
            : (r.Outcome == BotZoneOutcome.Wall ? "стена на " : "предел на ") + NumberFormat.FormatDuration(r.SimSeconds);

        /// <summary>Отчёт темпа (ТЗ 17.2, C.4): время, источники золота, эликсира и карточек, доставки, автоматизация шахт.</summary>
        public static string FormatReport(IEnumerable<BotZoneReport> reports)
        {
            var list = new List<BotZoneReport>(reports);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.AppendLine("### Темп по зонам");
            sb.AppendLine();
            sb.AppendLine("Время — симуляционное, вместе с офлайном. Сессии идут непрерывным циклом через все зоны: 30 мин игры, затем 4 ч офлайна (ТЗ 17.2, 12.3); «Сессия в начале» — сколько текущей сессии уже сыграно в прошлых зонах. " +
                          "«Стена» — 2 ч симуляции без единой доступной покупки, улучшения или назначения, которое увеличивает доход или урон. Наймы, объединения, уровень котика и кузницы — к концу зоны. " +
                          "Журнал действий — `Docs/Reports/bot-actions-zones-1-10.md`.");
            sb.AppendLine();
            sb.AppendLine("| Зона | Итог | Из них онлайн | Сессия в начале | Наймов | Объединений | Макс. уровень котика | Кузница | Шахты авто/всего | Доставок | Доставок в час онлайн | Пропущено рекламных |");
            sb.AppendLine("|---|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|");
            foreach (var r in list)
            {
                double perHour = r.OnlineSeconds > 0 ? r.Deliveries / (r.OnlineSeconds / 3600.0) : 0;
                sb.AppendLine(string.Format(inv, "| zone{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8}/{9} | {10} | {11:0.0} | {12} |",
                    r.Zone, Outcome(r), NumberFormat.FormatDuration(r.OnlineSeconds), NumberFormat.FormatDuration(r.SessionClockAtStart), r.Hires, r.Merges, r.MaxCatLevel, r.ForgeLevel,
                    r.ShaftsAutomated, r.Shafts, r.Deliveries, perHour, r.SkippedAds));
            }

            sb.AppendLine();
            sb.AppendLine("### Золото по источникам за зону");
            sb.AppendLine();
            sb.AppendLine("| Зона | Кузница | Шахты | Камни | Доставки | Всего | Потрачено на наём | Потрачено на улучшения |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in list)
            {
                var forge = r.Source("gold:forge");
                var shafts = r.Source("gold:shaft");
                var rocks = r.Source("gold:rock");
                var deliveries = r.Source("gold:delivery");
                var total = forge + shafts + rocks + deliveries;
                sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + Share(forge, total) + " | " + Share(shafts, total) + " | " + Share(rocks, total) + " | " +
                              Share(deliveries, total) + " | " + NumberFormat.Format(total) + " | " + NumberFormat.Format(r.Source("gold:spent:hire")) + " | " +
                              NumberFormat.Format(r.Source("gold:spent:upgrade")) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("### Эликсир по источникам за зону");
            sb.AppendLine();
            sb.AppendLine("Сундуки включают награды выхода, шахт, ворот, камней-сундуков и бесплатный сундук. Траты — улучшение чертежей.");
            sb.AppendLine();
            sb.AppendLine("| Зона | Камни | Пороги | Сундуки | Доставки | Всего за зону | Потрачено на чертежи | Улучшений чертежей | Баланс в конце |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            var sumElixir = GameNumber.Zero;
            foreach (var r in list)
            {
                var rocks = r.Source("elixir:rock");
                var thresholds = r.Source("elixir:threshold");
                var chests = r.SourcePrefix("elixir:chest:");
                var deliveries = r.Source("elixir:delivery");
                var total = rocks + thresholds + chests + deliveries;
                sumElixir += total;
                sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + Share(rocks, total) + " | " + Share(thresholds, total) + " | " + Share(chests, total) + " | " +
                              Share(deliveries, total) + " | " + NumberFormat.Format(total) + " | " + NumberFormat.Format(r.Source("elixir:spent:cards")) + " | " +
                              r.CardUpgrades.ToString(inv) + " | " + NumberFormat.Format(r.Elixir) + " |");
            }
            sb.AppendLine("| итого | | | | | " + NumberFormat.Format(sumElixir) + " | | | |");

            sb.AppendLine();
            sb.AppendLine("### Чертежи (карточки) по источникам за зону, штук");
            sb.AppendLine();
            sb.AppendLine("| Зона | Камни-сундуки | Шахты | Ворота | Выход | Бесплатный сундук | Всего |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            var sumCards = GameNumber.Zero;
            foreach (var r in list)
            {
                var total = r.SourcePrefix("cards:");
                sumCards += total;
                sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + NumberFormat.Format(r.Source("cards:rock")) + " | " + NumberFormat.Format(r.Source("cards:shaft")) + " | " +
                              NumberFormat.Format(r.Source("cards:gate")) + " | " + NumberFormat.Format(r.Source("cards:exit")) + " | " +
                              NumberFormat.Format(r.Source("cards:free")) + " | " + NumberFormat.Format(total) + " |");
            }
            sb.AppendLine("| итого | | | | | | " + NumberFormat.Format(sumCards) + " |");

            sb.AppendLine();
            sb.AppendLine("### Автоматизация шахт");
            sb.AppendLine();
            sb.AppendLine("Время — от начала зоны. «нет» — шахта не была автоматизирована до перехода.");
            sb.AppendLine();
            sb.AppendLine("| Зона | Шахта | Нужен чертёж | Активирована | Автоматизирована |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in list)
                foreach (var sh in r.ShaftInfo)
                    sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + sh.TypeId + " | " + sh.CardId + " ур. " + sh.CardLevel.ToString(inv) + " | " +
                                  At(sh.ActivatedAt) + " | " + At(sh.AutomatedAt) + " |");

            bool anyExcluded = false;
            foreach (var r in list) if (r.Excluded.Count > 0) anyExcluded = true;
            if (anyExcluded)
            {
                sb.AppendLine();
                sb.AppendLine("### Невыданные награды (ТЗ 4.3)");
                sb.AppendLine();
                sb.AppendLine("| Зона | Награда | Количество |");
                sb.AppendLine("|---|---|---:|");
                foreach (var r in list)
                    foreach (var kv in r.Excluded)
                        sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + kv.Key + " | " + kv.Value.ToString("0.##", inv) + " |");
            }
            return sb.ToString();
        }

        static string KindName(string kind)
        {
            switch (kind)
            {
                case BotAction.KindStep: return "шаг";
                case BotAction.KindRoutine: return "сбор";
                case BotAction.KindOffline: return "офлайн";
                case BotAction.KindSession: return "сессия";
                case BotAction.KindResult: return "итог";
                default: return kind;
            }
        }

        /// <summary>
        /// Журнал действий и симуляционного времени по зонам (ТЗ 17.2): одна строка — успешные команды одного шага решения;
        /// подряд идущие шаги только со сбором выплат, ящиков и бесплатных сундуков сведены в одну строку «сбор»; отдельно —
        /// офлайн, начало сессий и итог зоны.
        /// </summary>
        public static string FormatActionLog(IEnumerable<BotZoneReport> reports)
        {
            var list = new List<BotZoneReport>(reports);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Журнал действий сценарного бота (ТЗ 17.2)");
            sb.AppendLine();
            sb.AppendLine("Файл пишет тест `ScenarioBotTests.BotZones1To10Report` вместе с `bot-zones-1-10.md`. Время — симуляционное, от начала зоны (ч:мм:сс), вместе с офлайном. " +
                          "Строка «шаг» — успешные команды одного шага решения (шаг 1 с): переходы, активации, ворота, наём, объединения, улучшения генераторов («→ ур. N (+k)»), чертежи, назначения, а также выплаты и ящики того же шага. " +
                          "Строка «сбор» — подряд идущие шаги, в которых были только сбор выплат, ящики и бесплатные сундуки. Сессии идут непрерывным циклом через все зоны: 30 мин игры, затем 4 ч офлайна.");
            sb.AppendLine();
            sb.AppendLine("| Зона | Итог | Записей |");
            sb.AppendLine("|---|---|---:|");
            foreach (var r in list)
                sb.AppendLine("| zone" + r.Zone.ToString(inv) + " | " + Outcome(r) + " | " + r.Actions.Count.ToString(inv) + " |");
            foreach (var r in list)
            {
                sb.AppendLine();
                sb.AppendLine("## zone" + r.Zone.ToString(inv) + " — " + Outcome(r));
                sb.AppendLine();
                sb.AppendLine("| Время | Запись | Действия |");
                sb.AppendLine("|---:|---|---|");
                foreach (var a in r.Actions)
                    sb.AppendLine("| " + Clock(a.T) + " | " + KindName(a.Kind) + " | " + (a.Detail ?? "").Replace("|", "/") + " |");
            }
            return sb.ToString();
        }
    }
}
