using System;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Офлайн тем же ядром (ТЗ §13, §19, §20 «Офлайн и онлайн», «Часы и повторные callbacks», «Завершённая шахта офлайн»;
    /// CORE_SPEC §11): elapsed = clamp(now − LastProcessedUtcMs, 0, 3 ч), target = Now + elapsed·1000,
    /// LastProcessedUtcMs = max(prev, now), расчёт на копии, порционный Step.
    /// </summary>
    public class OutpostOfflineTests
    {
        /// <summary>Состояние зоны после seconds онлайн-секунд; UTC-маркер согласован с обработанным временем (§13).</summary>
        static OutpostGameState MidRun(int zone, long seconds)
        {
            var sim = Real(zone).NewSim();
            sim.Advance(seconds * S);
            var st = Clone(sim.State);
            st.Run.LastProcessedUtcMs = StartUtcMs + seconds * 1000;
            return st;
        }

        [TestCase(long.MinValue, long.MaxValue, long.MaxValue, OfflineCapMs)]
        [TestCase(long.MaxValue, long.MinValue, 0L, 0L)]
        public void ExtremeClockDelta_DoesNotOverflow(long previous, long now, long elapsed, long applied)
        {
            var state = MidRun(1, 30);
            state.Run.LastProcessedUtcMs = previous;
            var job = new OfflineSimulator(Map(1), Configs).Begin(state, now);
            Assert.AreEqual(elapsed, job.Summary.ElapsedMs, "интервал насыщается, а не переполняется");
            Assert.AreEqual(applied, job.Summary.AppliedMs, "назад — ноль, вперёд — не больше трёх часов");
            job.RunToEnd();
            Assert.AreEqual(Math.Max(previous, now), job.State.Run.LastProcessedUtcMs);
            Assert.AreEqual(state.Run.Now + applied * 1000, job.State.Run.Now);
        }

        [Test]
        public void ClockMovedBack_ZeroElapsed_StateAndMarkerUnchanged()
        {
            // ТЗ §13: «Длительность отсутствия: max(0, nowUtcMs − lastProcessedUtcMs)… При переводе часов назад маркер не уменьшается».
            var st = MidRun(1, 30);
            long marker = st.Run.LastProcessedUtcMs;
            string json = Json(st);
            var res = RunOffline(st, Map(1), Configs, marker - 5 * HourMs, out var sum);
            AssertSameJson(json, Json(st), "офлайн считается на копии (§13)");
            Assert.AreEqual(0L, sum.AppliedMs);
            Assert.AreEqual(marker, res.Run.LastProcessedUtcMs, "маркер не уменьшается");
            AssertSameJson(json, Json(res), "нулевой интервал ничего не меняет");
            Assert.AreEqual(BigInteger.Zero, sum.GoldDelivered);
        }

        [TestCase(4)]
        [TestCase(9)]
        [TestCase(24)]
        [TestCase(24 * 30)]
        public void LongAbsence_CappedToThreeHours_MarkerMovesToNow(int hours)
        {
            // ТЗ §13: не более трёх реальных часов; маркер поглощает весь интервал, включая неучтённый остаток.
            var st = MidRun(5, 30);
            string committed = Json(st);
            long marker = st.Run.LastProcessedUtcMs;
            long now = marker + hours * HourMs;
            var res = RunOffline(st, Map(5), Configs, now, out var sum);
            AssertSameJson(committed, Json(st), "исходный снимок не тронут");
            Assert.AreEqual(st.Run.Now + OfflineCapMs * 1000, res.Run.Now, "применено ровно 3 ч логического времени");
            Assert.AreEqual(OfflineCapMs, sum.AppliedMs);
            Assert.AreEqual(hours * HourMs, sum.ElapsedMs);
            Assert.AreEqual(now, res.Run.LastProcessedUtcMs, "маркер = now, а не prev + 3 ч");
            var again = RunOffline(res, Map(5), Configs, now, out var sum2);
            AssertSameJson(Json(res), Json(again), "повторный resume с тем же now не начисляет второй раз");
            Assert.AreEqual(0L, sum2.AppliedMs);
        }

        [TestCase(0L)]
        [TestCase(2 * HourMs)]
        [TestCase(3 * HourMs - 1)]
        [TestCase(3 * HourMs)]
        [TestCase(3 * HourMs + 1)]
        public void ThreeHourBoundary_DoesNotRoundUpAbsence(long absenceMs)
        {
            var state = MidRun(5, 30);
            long now = state.Run.LastProcessedUtcMs + absenceMs;
            var result = RunOffline(state, Map(5), Configs, now, out var summary);
            long expected = Math.Min(absenceMs, 3 * HourMs);
            Assert.AreEqual(absenceMs, summary.ElapsedMs);
            Assert.AreEqual(expected, summary.AppliedMs);
            Assert.AreEqual(state.Run.Now + expected * 1000, result.Run.Now);
            Assert.AreEqual(now, result.Run.LastProcessedUtcMs);
        }

        [TestCase(0L)]
        [TestCase(8 * HourMs)]
        [TestCase(15 * HourMs)]
        public void PreviousBalanceSnapshot_DayAwayEqualsThreeHours_ThenResumesNormally(long savedCap)
        {
            var state = MidRun(5, 30);
            state.Run.Balance.Version = "outpost-2.1-b2";
            state.Run.Balance.OfflineCapMs = savedCap;
            string balance = state.Run.Balance.CanonicalJson();
            long marker = state.Run.LastProcessedUtcMs;
            var online = new OutpostSimulation(Clone(state), Map(5), Configs);
            online.Advance(state.Run.Now + 3 * HourMs * 1000);
            online.Run.LastProcessedUtcMs = marker + 24 * HourMs;
            var offline = RunOffline(state, Map(5), Configs, marker + 24 * HourMs, out var summary);
            AssertSameJson(Json(online.State), Json(offline), "сутки отсутствия = ровно 3 ч того же ядра");
            Assert.AreEqual(3 * HourMs, summary.AppliedMs);
            AssertSameJson(balance, offline.Run.Balance.CanonicalJson(), "снимок экономики шахты не переписывается");
            var next = RunOffline(offline, Map(5), Configs, marker + 24 * HourMs + 5000, out var nextSummary);
            Assert.AreEqual(5000, nextSummary.AppliedMs, "остаток первых суток не доначисляется");
            Assert.AreEqual(offline.Run.Now + 5 * S, next.Run.Now);
        }

        [Test]
        public void ConfigurationCannotExtendRealAbsenceBeyondThreeHours()
        {
            var balance = Configs.Balance.Clone();
            balance.OfflineCapMs = long.MaxValue;
            var state = MidRun(5, 30);
            var job = new OfflineSimulator(Map(5), Configs.WithBalance(balance)).Begin(state, state.Run.LastProcessedUtcMs + 24 * HourMs);
            Assert.AreEqual(3 * HourMs, job.Summary.AppliedMs, "общий потолок действует даже на прежний/ошибочный конфиг");
        }

        [TestCase(1, 30)]
        [TestCase(5, 120)]
        [TestCase(10, 45)]
        public void Offline_EqualsOnlineAdvanceOfSameDuration(int zone, int minutes)
        {
            // ТЗ §13 «Используй ту же симуляцию, что работает онлайн»; §20 «Офлайн и онлайн: одинаковое исходное состояние без команд
            // даёт точное равенство экономического состояния, HP, задач, очереди и PRNG».
            var st = MidRun(zone, 40);
            long now = st.Run.LastProcessedUtcMs + minutes * 60_000L;
            var offline = RunOffline(st, Map(zone), Configs, now, out var sum);
            var online = new OutpostSimulation(Clone(st), Map(zone), Configs);
            online.Advance(st.Run.Now + minutes * 60_000L * 1000);
            online.State.Run.LastProcessedUtcMs = now;
            AssertSameJson(Json(online.State), Json(offline), "zone" + zone + ": офлайн " + minutes + " мин = онлайн");
            // Окно «Пока вас не было» — уже начисленные результаты (§13).
            Assert.AreEqual(offline.Run.Wallet - st.Run.Wallet, sum.GoldDelivered, "без покупок доставлено = прирост кошелька");
            Assert.AreEqual(offline.Run.Counters.DeliveredUnits - st.Run.Counters.DeliveredUnits, sum.UnitsDelivered);
            Assert.AreEqual(offline.Run.Counters.ClearedTargets - st.Run.Counters.ClearedTargets, sum.TargetsCleared);
            Assert.AreEqual(offline.Run.GatesOpened - st.Run.GatesOpened, sum.SectorsOpened);
            Assert.AreEqual(offline.Run.Phase == RunPhase.ReadyToContinue && st.Run.Phase != RunPhase.ReadyToContinue, sum.StageCompleted);
            Assert.Greater(sum.UnitsDelivered, 0, "офлайн продолжает всю цепочку");
        }

        [Test]
        public void OfflineInTwoResumes_EqualsOneResume_ClockBackDoesNotReapply()
        {
            // ТЗ §13: «нельзя затем повторно выдать уже обработанное время»; §20 «повторный запуск или resume не повторяет начисление».
            var st = MidRun(5, 40);
            long m = st.Run.LastProcessedUtcMs;
            var once = RunOffline(st, Map(5), Configs, m + 30 * 60_000L, out _);
            var first = RunOffline(st, Map(5), Configs, m + 20 * 60_000L, out _);
            var second = RunOffline(first, Map(5), Configs, m + 30 * 60_000L, out var s2);
            AssertSameJson(Json(once), Json(second), "20 мин + 10 мин = 30 мин (Advance(a+b) = Advance(a); Advance(b))");
            Assert.AreEqual(10 * 60_000L, s2.AppliedMs);

            var back = RunOffline(second, Map(5), Configs, m + 5 * 60_000L, out var sb);
            AssertSameJson(Json(second), Json(back), "часы назад после обработки — ничего");
            Assert.AreEqual(0L, sb.AppliedMs);
            Assert.AreEqual(m + 30 * 60_000L, back.Run.LastProcessedUtcMs);
            var forward = RunOffline(back, Map(5), Configs, m + 40 * 60_000L, out var sf);
            Assert.AreEqual(10 * 60_000L, sf.AppliedMs, "начисляется только время после прежнего маркера");
            Assert.AreEqual(back.Run.Now + 10 * 60_000L * 1000, forward.Run.Now);
        }

        [Test]
        public void FinishedMapOffline_StopsAtReadyToContinue_NextZoneNotStarted()
        {
            // ТЗ §13: «Если текущая шахта завершена, симуляция доходит до ReadyToContinue и останавливается. Пустая завершённая карта
            // больше не производит золото»; «В офлайне нет… перехода на следующую шахту»; §20 «Завершённая шахта офлайн».
            var st = Real(1).NewState();
            var res = RunOffline(st, Map(1), Configs, StartUtcMs + OfflineCapMs, out var sum);
            var run = res.Run;
            var audit = AuditZone(1);
            Assert.AreEqual(RunPhase.ReadyToContinue, run.Phase);
            Assert.AreEqual(1, run.Zone, "следующая шахта не начата");
            Assert.AreEqual(1, res.Profile.CurrentZone);
            Assert.AreEqual(AuditBig(audit["base_gold"]), run.Wallet, "весь груз карты сдан офлайн");
            Assert.AreEqual(AuditBig(audit["base_gold"]), sum.GoldDelivered);
            Assert.AreEqual((int)audit["cargo_units"], sum.UnitsDelivered);
            Assert.AreEqual((int)audit["obligatory_targets"], sum.TargetsCleared);
            Assert.AreEqual(0, sum.SectorsOpened, "в zone1 ворот нет");
            Assert.IsTrue(sum.StageCompleted);
            Assert.AreEqual(1, res.Profile.RewardLog.Count, "награда выдана один раз в транзакции офлайна");
            // Первая сессия (rewards-2): награда шахты 1 — только детали завершения, без предмета.
            Assert.IsNull(res.Profile.RewardLog[0].ItemId);
            Assert.AreEqual(CompletionParts(1), res.Profile.RewardLog[0].CompletionParts);
            Assert.AreEqual((long)CompletionParts(1), res.Profile.Parts);
            Assert.AreEqual(StartUtcMs + OfflineCapMs, run.LastProcessedUtcMs);

            var again = RunOffline(res, Map(1), Configs, StartUtcMs + 2 * OfflineCapMs, out var sum2);
            Assert.AreEqual(run.Wallet, again.Run.Wallet, "завершённая карта не производит золото");
            Assert.AreEqual(BigInteger.Zero, sum2.GoldDelivered);
            Assert.IsFalse(sum2.StageCompleted);
            Assert.AreEqual(1, again.Profile.RewardLog.Count);
            Assert.AreEqual(1, again.Run.Zone);
            Assert.AreEqual(RunPhase.ReadyToContinue, again.Run.Phase);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void ChunkedSteps_EqualSingleStep(int zone)
        {
            // ТЗ §19: «Длинный расчёт дробить по стендовому времени с индикатором, сохраняя один логический результат».
            var st = MidRun(zone, 20);
            string committed = Json(st);
            long now = st.Run.LastProcessedUtcMs + 2 * HourMs;
            var whole = RunOffline(st, Map(zone), Configs, now, out var s1, int.MaxValue);
            var chunked = RunOffline(st, Map(zone), Configs, now, out var s2, 7);
            AssertSameJson(committed, Json(st), "исходный снимок не тронут");
            AssertSameJson(Json(whole), Json(chunked), "zone" + zone + ": Step(7)… = один Step");
            Assert.AreEqual(s1.GoldDelivered, s2.GoldDelivered);
            Assert.AreEqual(s1.UnitsDelivered, s2.UnitsDelivered);
            Assert.AreEqual(s1.TargetsCleared, s2.TargetsCleared);
            Assert.AreEqual(s1.SectorsOpened, s2.SectorsOpened);
            Assert.AreEqual(s1.StageCompleted, s2.StageCompleted);
            Assert.AreEqual(s1.AppliedMs, s2.AppliedMs);
        }

        [Test]
        public void OfflineResult_PassesInvariantsAndContinuesOnline()
        {
            // ТЗ §13: результат подменяет состояние после commit — затем обычная онлайн-работа без расхождений.
            var st = MidRun(10, 30);
            var res = RunOffline(st, Map(10), Configs, st.Run.LastProcessedUtcMs + HourMs, out _);
            var sim = new OutpostSimulation(res, Map(10), Configs);
            AssertInvariants(sim, "после офлайна");
            sim.Advance(sim.Now + 120 * S);
            AssertInvariants(sim, "онлайн после офлайна");
            Assert.IsFalse(sim.State.Run.WatchdogFired, sim.State.Run.WatchdogDiagnostic);
        }
    }
}
