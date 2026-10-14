using System;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// ТЗ 17.1, группа 10 (офлайн) с опорой на ТЗ 12.3, 15.2 и E-19. Дополняет SimulationTests
    /// (OfflineMatchesOnlineWithoutActions, OfflineIsCappedAndMonotonic, AdvanceIsAdditive) тем, чего там нет:
    /// кадры 1/60 с, полный критерий совпадения 15.2, окно «Пока вас не было», повторные pause/focus,
    /// догон метки после перевода часов, точные границы лимита 8 ч.
    /// </summary>
    public class OfflineAcceptanceTests
    {
        const long S = GameSession.TicksPerSecond;
        const long StartUtcMs = 1_000_000L;
        const long SecondMs = 1000L;
        const long HourMs = 3600L * SecondMs;
        /// <summary>E-19: офлайн-лимит 8 ч.</summary>
        const long CapHours = 8;
        /// <summary>15.2: относительная погрешность золота и HP.</summary>
        const double Tolerance = 1e-9;

        // ───────────────────────────── Вспомогательные ─────────────────────────────

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Clone(GameSession s) =>
            GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(s)));

        static int Count(GameSession s, SimEventType type) => s.Events.Count(e => e.Type == type);

        /// <summary>Кадр i из серии 60 Гц: границы округляются от начала серии, сумма N кадров ровно N/60 с.</summary>
        static long FrameTicks60(long i) => (i + 1) * S / 60 - i * S / 60;

        /// <summary>То же в миллисекундах UTC (16 или 17 мс).</summary>
        static long FrameMs60(long i) => (i + 1) * SecondMs / 60 - i * SecondMs / 60;

        /// <summary>Zone1, слот Main, два котика на камнях — как в SimulationTests, без инструментов.</summary>
        static GameSession Zone1WithCats()
        {
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Main, StartUtcMs, 1);
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            Assert.AreEqual(CommandResult.Ok, s.DropCat(s.Mine.Cats[0].Id, 2.5, 4.5).Result);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(s.Mine.Cats[1].Id, 3.5, 4.5).Result);
            s.Events.Clear();
            return s;
        }

        /// <summary>
        /// Насыщенный сценарий без доставок (их таймер офлайн стоит по 12.3, поэтому онлайн и офлайн сравнимы только без них).
        /// Zone6, слот Debug: две активированные шахты включают бесплатный сундук (E-17), повторный вход в zone6 сбрасывает
        /// шахту, флаг сундука постоянный. В новой шахте одна активированная шахта (доставки выключены), ручной буфер,
        /// котики на камнях (в том числе сундучных) и на шахте. Таймер сундука истекает через 600 с,
        /// таймер бесплатного котика — через 900 с.
        /// </summary>
        static GameSession RichScenario()
        {
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Debug, StartUtcMs, 6);
            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.IsTrue(s.Player.FreeChest.Enabled, "E-17: две шахты на Rank ≥ 4 включают бесплатный сундук");
            s.DebugGotoZone(6);
            Assert.IsTrue(s.Player.FreeChest.Enabled, "флаг бесплатного сундука постоянный");
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "в новой шахте доставки ещё не включены");

            // Подготовительное время: таймер сундука (4 ч) почти истёк, бесплатный котик открыт и таймер перезапущен.
            s.Advance(12300 * S);
            var box = s.Mine.Containers.Single(c => c.Kind == ContainerKind.FreeCat);
            Assert.AreEqual(CommandResult.Ok, s.OpenContainer(box.Id));
            s.Advance(1500 * S);
            Assert.AreEqual(600 * S, s.Player.FreeChest.TimerTicks);
            Assert.AreEqual(900 * S, s.Mine.FreeCatTimerTicks);

            var citrine = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            s.DebugCompleteTarget(citrine.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            Assert.IsFalse(s.Mine.Objects[citrine.Ordinal].Gen.Automated, "citrine zone6 требует ca003 ур. 4 — шахта ручная");
            Assert.IsFalse(s.Mine.DeliveriesEnabled, "одна активированная шахта доставки не включает");

            // Освобождаем подходы к камням нижних рядов.
            s.DebugCompleteTarget(s.ObjectAt(s.Layout.CellIndex(1, 20)));
            s.DebugCompleteTarget(s.ObjectAt(s.Layout.CellIndex(4, 21)));
            s.DebugCompleteTarget(s.ObjectAt(s.Layout.CellIndex(4, 20)));
            foreach (int level in new[] { 1, 2, 1, 3, 2, 1, 2 }) Assert.IsTrue(s.DebugSpawnCat(level));
            foreach (var cat in s.Mine.Cats.ToList())
            {
                for (int ord = s.Layout.Objects.Count - 1; ord >= 0; ord--)
                {
                    var o = s.Layout.Objects[ord];
                    if (!s.AcceptsCat(ord, cat.Id) || s.Mine.Cats.Any(c => c.TargetIndex == o.Index)) continue;
                    Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, o.Col + 0.5, o.Row + 0.5).Result);
                    break;
                }
            }
            Assert.GreaterOrEqual(s.Mine.Cats.Count(c => c.TargetIndex >= 0), 3, "сценарий: несколько котиков работают");
            Assert.Less(s.OccupiedSlots, s.UnitCap(), "сценарий: есть место для бесплатного котика");
            s.Events.Clear();
            return s;
        }

        /// <summary>
        /// Критерий совпадения 15.2: дискретные величины точно (удары через HP и фазы, разрушенные цели, циклы,
        /// награды, RewardCycle, PRNG), золото и HP — с относительной погрешностью 1e-9.
        /// Метка lastProcessedUtcMs и список событий сюда не входят: онлайн-прогон метку не двигает, а офлайн события ударов не публикует.
        /// </summary>
        static void AssertSameByCriterion(GameSession expected, GameSession actual, string label)
        {
            string L(string what) => label + ": " + what;
            var em = expected.Mine;
            var am = actual.Mine;
            Assert.AreEqual(em.Zone, am.Zone, L("зона"));
            Assert.IsTrue(GameNumber.ApproximatelyEqual(em.Gold, am.Gold, Tolerance), L("золото " + em.Gold + " ≠ " + am.Gold));
            Assert.AreEqual(em.RocksDestroyed, am.RocksDestroyed, L("разрушено камней"));
            Assert.AreEqual(em.Objects.Count, am.Objects.Count, L("число объектов"));
            for (int i = 0; i < em.Objects.Count; i++)
            {
                var eo = em.Objects[i];
                var ao = am.Objects[i];
                string obj = "объект " + eo.Index;
                Assert.AreEqual(eo.Status, ao.Status, L(obj + ": состояние"));
                Assert.IsTrue(GameNumber.ApproximatelyEqual(eo.Hp, ao.Hp, Tolerance), L(obj + ": HP " + eo.Hp + " ≠ " + ao.Hp));
                Assert.AreEqual(eo.Gen == null, ao.Gen == null, L(obj + ": генератор"));
                if (eo.Gen == null) continue;
                Assert.AreEqual(eo.Gen.Level, ao.Gen.Level, L(obj + ": уровень генератора"));
                Assert.AreEqual(eo.Gen.CycleTicks, ao.Gen.CycleTicks, L(obj + ": фаза цикла"));
                Assert.AreEqual(eo.Gen.Automated, ao.Gen.Automated, L(obj + ": автоматизация"));
                Assert.AreEqual(eo.Gen.PayoutReady, ao.Gen.PayoutReady, L(obj + ": готовая ручная выплата"));
                Assert.IsTrue(GameNumber.ApproximatelyEqual(eo.Gen.PendingPayout, ao.Gen.PendingPayout, Tolerance), L(obj + ": размер выплаты"));
                Assert.AreEqual(eo.Gen.ThresholdsReached, ao.Gen.ThresholdsReached, L(obj + ": пороги"));
            }

            Assert.AreEqual(em.Cats.Count, am.Cats.Count, L("число котиков"));
            for (int i = 0; i < em.Cats.Count; i++)
            {
                var ec = em.Cats[i];
                var ac = am.Cats[i];
                string cat = "котик " + ec.Id;
                Assert.AreEqual(ec.Id, ac.Id, L(cat + ": ID"));
                Assert.AreEqual(ec.Level, ac.Level, L(cat + ": уровень"));
                Assert.AreEqual(ec.Cell, ac.Cell, L(cat + ": клетка"));
                Assert.AreEqual(ec.TargetIndex, ac.TargetIndex, L(cat + ": назначение"));
                Assert.AreEqual(ec.PhaseTicks, ac.PhaseTicks, L(cat + ": фаза удара"));
            }

            Assert.AreEqual(em.Containers.Count, am.Containers.Count, L("число контейнеров"));
            for (int i = 0; i < em.Containers.Count; i++)
            {
                var ec = em.Containers[i];
                var ac = am.Containers[i];
                Assert.AreEqual(ec.Id, ac.Id, L("контейнер: ID"));
                Assert.AreEqual(ec.Kind, ac.Kind, L("контейнер " + ec.Id + ": вид"));
                Assert.AreEqual(ec.Cell, ac.Cell, L("контейнер " + ec.Id + ": клетка"));
                Assert.AreEqual(ec.CatLevel, ac.CatLevel, L("контейнер " + ec.Id + ": уровень котика"));
                Assert.AreEqual(ec.DeliveryId, ac.DeliveryId, L("контейнер " + ec.Id + ": доставка"));
            }

            // Таймеры.
            Assert.AreEqual(em.FreeCatTimerTicks, am.FreeCatTimerTicks, L("таймер бесплатного котика"));
            Assert.AreEqual(em.FreeCatReady, am.FreeCatReady, L("бесплатный котик готов"));
            Assert.AreEqual(em.DeliveriesEnabled, am.DeliveriesEnabled, L("доставки включены"));
            Assert.AreEqual(em.DeliveryTimerTicks, am.DeliveryTimerTicks, L("таймер доставки"));
            var ef = expected.Player.FreeChest;
            var af = actual.Player.FreeChest;
            Assert.AreEqual(ef.Enabled, af.Enabled, L("бесплатный сундук включён"));
            Assert.AreEqual(ef.Stock, af.Stock, L("запас бесплатных сундуков"));
            Assert.AreEqual(ef.TimerTicks, af.TimerTicks, L("таймер бесплатного сундука"));
            Assert.AreEqual(ef.NextIndex, af.NextIndex, L("индекс бесплатного сундука"));
            Assert.AreEqual(expected.Player.PlayerTimeTicks, actual.Player.PlayerTimeTicks, L("время игрока"));
            Assert.AreEqual(em.ZoneTimeTicks, am.ZoneTimeTicks, L("время зоны"));

            // Награды.
            var ep = expected.Player;
            var ap = actual.Player;
            CollectionAssert.AreEquivalent(ep.RewardCycle, ap.RewardCycle, L("индексы RewardCycle"));
            Assert.AreEqual(expected.State.NextOpId, actual.State.NextOpId, L("число транзакций (opId)"));
            Assert.IsTrue(GameNumber.ApproximatelyEqual(ep.Elixir, ap.Elixir, Tolerance), L("эликсир " + ep.Elixir + " ≠ " + ap.Elixir));
            Assert.IsTrue(GameNumber.ApproximatelyEqual(ep.Hard, ap.Hard, Tolerance), L("кристаллы"));
            CollectionAssert.AreEquivalent(ep.ExcludedRewards, ap.ExcludedRewards, L("учёт исключённых наград"));
            CollectionAssert.AreEquivalent(
                ep.Cards.Where(kv => kv.Value.Level > 0 || kv.Value.Duplicates > 0).Select(kv => kv.Key + ":" + kv.Value.Level + "+" + kv.Value.Duplicates),
                ap.Cards.Where(kv => kv.Value.Level > 0 || kv.Value.Duplicates > 0).Select(kv => kv.Key + ":" + kv.Value.Level + "+" + kv.Value.Duplicates),
                L("карты"));
            Assert.AreEqual(ep.ChestReveals.Count, ap.ChestReveals.Count, L("очередь неоткрытых сундуков"));
            for (int i = 0; i < ep.ChestReveals.Count; i++)
            {
                var ec = ep.ChestReveals[i];
                var ac = ap.ChestReveals[i];
                Assert.AreEqual(ec.OpId, ac.OpId, L("сундук " + i + ": opId"));
                Assert.AreEqual(ec.GachaId, ac.GachaId, L("сундук " + i + ": вид"));
                Assert.AreEqual(ec.Source, ac.Source, L("сундук " + i + ": источник"));
                Assert.IsTrue(GameNumber.ApproximatelyEqual(ec.Elixir, ac.Elixir, Tolerance), L("сундук " + i + ": эликсир"));
                CollectionAssert.AreEqual(ec.Cards.Select(c => c.CardId + "x" + c.Count), ac.Cards.Select(c => c.CardId + "x" + c.Count), L("сундук " + i + ": карты"));
            }
            CollectionAssert.AreEquivalent(ep.Ledger.Keys, ap.Ledger.Keys, L("источники начислений"));
            foreach (var kv in ep.Ledger)
                Assert.IsTrue(GameNumber.ApproximatelyEqual(kv.Value, ap.Ledger[kv.Key], Tolerance), L("журнал " + kv.Key));

            // PRNG: все потоки.
            Assert.AreEqual(ep.Rng.Gacha.State, ap.Rng.Gacha.State, L("PRNG сундуков"));
            Assert.AreEqual(ep.Rng.Delivery.State, ap.Rng.Delivery.State, L("PRNG доставок"));
            Assert.AreEqual(ep.Rng.Crit.State, ap.Rng.Crit.State, L("PRNG критов"));
            Assert.AreEqual(ep.Rng.Timers.State, ap.Rng.Timers.State, L("PRNG таймеров"));
        }

        // ───────────────────────────── Кадры 1/60 с ≡ один Advance ≡ офлайн ─────────────────────────────

        /// <summary>
        /// 17.1 п. 10, 15.2: Advance(1/60 с) × N ≡ Advance(N/60 с) ≡ офлайн-расчёт того же интервала (N = 108 000, 30 мин)
        /// по полному критерию 15.2. Интервал содержит удары, разрушение камней с наградой и сундуком, расчистку шахты,
        /// ручной буфер, циклы кузницы, бесплатного котика и тик бесплатного сундука (12.3 «Офлайн продолжаются»).
        /// </summary>
        [Test]
        public void Frames60HzEqualSingleAdvanceEqualOfflineByCriterion15_2()
        {
            var start = RichScenario();
            const long seconds = 1800;
            const long frames = 60 * seconds;

            var byFrames = Clone(start);
            long sum = 0;
            for (long i = 0; i < frames; i++)
            {
                long dt = FrameTicks60(i);
                Assert.That(dt, Is.EqualTo(16666L).Or.EqualTo(16667L), "кадр 1/60 с в целых тиках");
                byFrames.Advance(dt);
                sum += dt;
            }
            Assert.AreEqual(seconds * S, sum, "N кадров по 1/60 с дают ровно N/60 с");

            var single = Clone(start);
            single.Advance(seconds * S);

            var offline = Clone(start);
            var summary = OfflineProgress.Apply(offline, StartUtcMs + seconds * SecondMs);
            Assert.AreEqual(seconds * S, summary.Ticks);
            Assert.IsFalse(summary.Capped);
            Assert.AreEqual(StartUtcMs + seconds * SecondMs, offline.State.LastProcessedUtcMs, "новая метка — момент расчёта");
            Assert.IsFalse(offline.OfflineMode, "после расчёта сессия снова онлайн");

            // Сценарий действительно задействует системы из 12.3.
            Assert.Greater(Count(single, SimEventType.TargetHit), 0, "сценарий: удары");
            Assert.Greater(Count(single, SimEventType.RockDestroyed), 0, "сценарий: разрушение камней");
            Assert.Greater(Count(single, SimEventType.ChestOpened), 0, "сценарий: сундук из награды камня");
            Assert.Greater(Count(single, SimEventType.ShaftCleared), 0, "сценарий: расчистка шахты");
            Assert.AreEqual(1, Count(single, SimEventType.PayoutReady), "ручная шахта — до одного готового буфера");
            Assert.AreEqual(1, Count(single, SimEventType.ContainerSpawned), "бесплатный котик — один контейнер");
            Assert.Greater(Count(single, SimEventType.GeneratorPaid), 0, "сценарий: циклы кузницы");
            Assert.AreEqual(start.Player.FreeChest.Stock + 1, single.Player.FreeChest.Stock, "сценарий: тик бесплатного сундука");

            AssertSameByCriterion(single, byFrames, "кадры 1/60 с против одного Advance");
            AssertSameByCriterion(single, offline, "офлайн против одного Advance");

            // Дискретные события онлайн-прогонов совпадают точно.
            foreach (var type in new[]
                     {
                         SimEventType.TargetHit, SimEventType.RockDestroyed, SimEventType.RewardGranted, SimEventType.ChestOpened,
                         SimEventType.ShaftCleared, SimEventType.GateBroken, SimEventType.ExitBroken, SimEventType.GeneratorPaid,
                         SimEventType.PayoutReady, SimEventType.ContainerSpawned, SimEventType.CatIdle,
                     })
                Assert.AreEqual(Count(single, type), Count(byFrames, type), "события " + type + ": кадры против одного Advance");

            // Окно «Пока вас не было» (12.3) совпадает с тем, что произошло онлайн за тот же интервал.
            Assert.AreEqual(single.Mine.RocksDestroyed - start.Mine.RocksDestroyed, summary.RocksDestroyed, "окно: разрушенные камни");
            Assert.AreEqual(Count(single, SimEventType.ChestOpened), summary.Chests, "окно: полученные сундуки");
            Assert.AreEqual(Count(single, SimEventType.ShaftCleared) + Count(single, SimEventType.GateBroken) + Count(single, SimEventType.ExitBroken),
                summary.TargetsBroken, "окно: расчищенные шахты, ворота, выход");
            Assert.AreEqual(Count(single, SimEventType.PayoutReady), summary.PayoutsReady, "окно: готовые ручные выплаты");
            Assert.AreEqual(Count(single, SimEventType.ContainerSpawned), summary.ContainersSpawned, "окно: контейнеры");
            Assert.IsTrue(GameNumber.ApproximatelyEqual(single.Mine.Gold - start.Mine.Gold, summary.Gold, Tolerance), "окно: золото");
            Assert.IsTrue(GameNumber.ApproximatelyEqual(single.Player.Elixir - start.Player.Elixir, summary.Elixir, Tolerance), "окно: эликсир");
            Assert.AreEqual(Count(single, SimEventType.ChestOpened), offline.Player.ChestReveals.Count - start.Player.ChestReveals.Count,
                "сундуки офлайн определены и стоят в очереди неоткрытых");
        }

        // ───────────────────────────── Кадр с dt > 1 с — офлайн ровно один раз ─────────────────────────────

        /// <summary>
        /// 15.2 «Длинные паузы», 17.1 п. 10: кадры по 1/60 с отмечают метку (MarkProcessed), разрыв больше 1 с
        /// обрабатывается одним OfflineProgress.Apply. Разрыв засчитывается ровно один раз: время игрока растёт ровно
        /// на прошедшее UTC-время, а состояние совпадает с непрерывным Advance того же суммарного интервала (15.2).
        /// </summary>
        [Test]
        public void FrameGapOverOneSecondIsCreditedOnceAsOffline()
        {
            var s = Zone1WithCats();
            var reference = Clone(s);
            long t0Ticks = s.Player.PlayerTimeTicks;
            long now = s.State.LastProcessedUtcMs;
            long frame = 0;

            void Frames(int count)
            {
                for (int k = 0; k < count; k++, frame++)
                {
                    long dtMs = FrameMs60(frame);
                    now += dtMs;
                    s.Advance(dtMs * 1000L);
                    OfflineProgress.MarkProcessed(s, now);
                    Assert.AreEqual(now, s.State.LastProcessedUtcMs, "кадр отмечает обработанный момент");
                }
            }

            Frames(600);
            // Время, уже обработанное кадрами, офлайном повторно не начисляется.
            string afterFrames = Snapshot(s);
            var none = OfflineProgress.Apply(s, now);
            Assert.IsTrue(none.IsEmpty, "кадры уже учли это время");
            Assert.AreEqual(afterFrames, Snapshot(s), "Apply без разрыва состояние не меняет");

            foreach (long gapMs in new[] { 5000L, 1001L, 37_250L })
            {
                now += gapMs;
                long before = s.Player.PlayerTimeTicks;
                var summary = OfflineProgress.Apply(s, now);
                Assert.AreEqual(gapMs * 1000L, summary.Ticks, "разрыв " + gapMs + " мс засчитан целиком");
                Assert.AreEqual(gapMs * 1000L, s.Player.PlayerTimeTicks - before, "разрыв " + gapMs + " мс засчитан один раз");
                Assert.AreEqual(now, s.State.LastProcessedUtcMs);
                Assert.IsFalse(s.OfflineMode, "следующие кадры идут онлайн");

                // Тот же кадр или повторный вызов: второго начисления нет.
                string snap = Snapshot(s);
                Assert.IsTrue(OfflineProgress.Apply(s, now).IsEmpty, "разрыв " + gapMs + " мс: повторный Apply");
                OfflineProgress.MarkProcessed(s, now);
                Assert.AreEqual(snap, Snapshot(s), "разрыв " + gapMs + " мс: повтор ничего не меняет");
                Frames(90);
            }

            long totalMs = now - StartUtcMs;
            Assert.AreEqual(totalMs * 1000L, s.Player.PlayerTimeTicks - t0Ticks, "время игрока = прошедшее UTC-время, без потерь и повторов");
            reference.Advance(totalMs * 1000L);
            AssertSameByCriterion(reference, s, "кадры + разрывы против одного Advance");
        }

        /// <summary>
        /// 15.2: разрыв кадра обрабатывается «как офлайн по 12.3» — таймер обычных доставок в разрыве стоит;
        /// после Apply сессия снова онлайн, и таймер доставки идёт в следующих кадрах.
        /// </summary>
        [Test]
        public void FrameGapUsesOfflineRulesThenReturnsOnline()
        {
            var s = GameSession.NewGame(TestData.Balance, 12345UL, SaveSlots.Debug, StartUtcMs, 4);
            foreach (var sh in s.Layout.Shafts.Take(2))
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.IsTrue(s.Mine.DeliveriesEnabled);
            // ТЗ 10.5, E-15: DeliveryStartupDelaySec = 20 — первая доставка через 20 с.
            Assert.AreEqual(20 * S, s.Mine.DeliveryTimerTicks);
            long freeCatBefore = s.Mine.FreeCatTimerTicks;

            var summary = OfflineProgress.Apply(s, StartUtcMs + 5 * SecondMs);
            Assert.AreEqual(5 * S, summary.Ticks);
            Assert.AreEqual(20 * S, s.Mine.DeliveryTimerTicks, "12.3: офлайн таймер доставки стоит");
            Assert.AreEqual(freeCatBefore - 5 * S, s.Mine.FreeCatTimerTicks, "12.3: таймер бесплатного котика офлайн идёт");
            Assert.IsFalse(s.OfflineMode);

            s.Advance(S);
            OfflineProgress.MarkProcessed(s, StartUtcMs + 6 * SecondMs);
            Assert.AreEqual(19 * S, s.Mine.DeliveryTimerTicks, "после разрыва кадры снова онлайн");
        }

        // ───────────────────────────── Повторные pause/focus ─────────────────────────────

        /// <summary>
        /// 12.3: «Один и тот же период не обрабатывается дважды при последовательных событиях focus/pause»;
        /// «Сначала сохраняются результат и новая метка». Последовательность Apply(now), Apply(now), перезапуск из
        /// сохранения с Apply(now), Apply(now + 0,5 с) даёт ровно интервал + 0,5 с, а сумма окон — ровно одно начисление.
        /// </summary>
        [Test]
        public void ConsecutivePauseFocusDoesNotCreditTwice()
        {
            var s = RichScenario();
            var reference = Clone(s);
            var goldStart = s.Mine.Gold;
            var elixirStart = s.Player.Elixir;
            int rocksStart = s.Mine.RocksDestroyed;
            long t1 = StartUtcMs + 10 * 60 * SecondMs;

            var first = OfflineProgress.Apply(s, t1);
            Assert.AreEqual(600 * S, first.Ticks);
            Assert.Greater(first.RocksDestroyed, 0, "сценарий: за 10 мин разрушен камень");
            string afterFirst = Snapshot(s);
            int eventsAfterFirst = s.Events.Count;

            var second = OfflineProgress.Apply(s, t1);
            Assert.IsTrue(second.IsEmpty, "повторный focus в тот же момент");
            Assert.IsFalse(second.Capped);
            Assert.IsTrue(second.Gold.IsZero && second.Elixir.IsZero, "повтор: золото и эликсир не начисляются");
            Assert.AreEqual(0, second.RocksDestroyed + second.Chests + second.TargetsBroken + second.PayoutsReady + second.ContainersSpawned,
                "повтор: окно пустое");
            Assert.AreEqual(afterFirst, Snapshot(s), "повтор не меняет состояние, включая PRNG и таймеры");
            Assert.AreEqual(eventsAfterFirst, s.Events.Count, "повтор не порождает событий");

            // Результат и метка сохранены → перезапуск → тот же момент снова не начисляется.
            var restored = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(afterFirst));
            Assert.AreEqual(t1, restored.State.LastProcessedUtcMs, "метка входит в сохранение");
            Assert.IsTrue(OfflineProgress.Apply(restored, t1).IsEmpty, "после перезапуска тот же период не начисляется");
            Assert.AreEqual(afterFirst, Snapshot(restored));

            var third = OfflineProgress.Apply(restored, t1 + 500);
            Assert.AreEqual(S / 2, third.Ticks, "начисляются только новые 0,5 с");
            Assert.AreEqual(t1 + 500, restored.State.LastProcessedUtcMs);

            reference.Advance(600 * S + S / 2);
            AssertSameByCriterion(reference, restored, "pause/focus против одного Advance");
            Assert.IsTrue(GameNumber.ApproximatelyEqual(reference.Mine.Gold - goldStart, first.Gold + third.Gold, Tolerance),
                "сумма окон по золоту = одно начисление");
            Assert.IsTrue(GameNumber.ApproximatelyEqual(reference.Player.Elixir - elixirStart, first.Elixir + third.Elixir, Tolerance),
                "сумма окон по эликсиру = одно начисление");
            Assert.AreEqual(reference.Mine.RocksDestroyed - rocksStart, first.RocksDestroyed + third.RocksDestroyed, "сумма окон по камням");
        }

        // ───────────────────────────── Перевод часов назад ─────────────────────────────

        /// <summary>
        /// 12.3: «Перевод часов назад метку не сдвигает. Если часы ранее убегали вперёд, офлайн-начисление не идёт,
        /// пока время не догонит метку». Обратный ход не меняет состояние вовсе, кадры в этот период метку не уменьшают,
        /// после догона начисляется только время сверх метки.
        /// </summary>
        [Test]
        public void ClockBackwardsGivesNothingUntilItCatchesUpWithTheMark()
        {
            var s = RichScenario();
            var reference = Clone(s);
            long mark = StartUtcMs + HourMs;
            Assert.AreEqual(3600 * S, OfflineProgress.Apply(s, mark).Ticks);
            string snap = Snapshot(s);

            foreach (long back in new[] { mark - 1, mark - 30 * 60 * SecondMs, StartUtcMs, 0L, -HourMs })
            {
                var summary = OfflineProgress.Apply(s, back);
                Assert.IsTrue(summary.IsEmpty, "часы назад до " + back + ": офлайна нет");
                Assert.AreEqual(mark, s.State.LastProcessedUtcMs, "часы назад до " + back + ": метка не сдвигается");
                OfflineProgress.MarkProcessed(s, back);
                Assert.AreEqual(mark, s.State.LastProcessedUtcMs, "MarkProcessed(" + back + ") не уменьшает метку");
                Assert.AreEqual(snap, Snapshot(s), "часы назад до " + back + ": состояние не меняется");
            }

            // После save/load метка та же.
            s = GameSession.FromState(TestData.Balance, SaveService.DeserializeState(Snapshot(s)));
            Assert.AreEqual(mark, s.State.LastProcessedUtcMs);

            // Часы догоняют метку: в самой метке ничего, дальше — только новое время.
            Assert.IsTrue(OfflineProgress.Apply(s, mark).IsEmpty, "догон до метки не даёт офлайна");
            Assert.AreEqual(snap, Snapshot(s));
            Assert.AreEqual(2 * S, OfflineProgress.Apply(s, mark + 2 * SecondMs).Ticks, "начисляется только время сверх метки");
            Assert.AreEqual(mark + 2 * SecondMs, s.State.LastProcessedUtcMs);

            reference.Advance(3600 * S + 2 * S);
            AssertSameByCriterion(reference, s, "перевод часов против одного Advance");
        }

        /// <summary>
        /// 12.3, E-19: для любой последовательности показаний часов метка = максимум увиденных показаний и не убывает,
        /// каждый вызов начисляет min(8 ч, max(0, now − метка)), сумма начислений = рост времени игрока.
        /// </summary>
        [Test]
        public void ClockSequenceKeepsMarkMonotonicAndCreditsOnlyNewTime()
        {
            var s = Zone1WithCats();
            var reference = Clone(s);
            long t0 = StartUtcMs;
            long[] readings =
            {
                t0 + 10_000, t0 + 5_000, t0 - HourMs, t0 + 10_000, t0 + 10_500, t0 + 80_500,
                t0 + 9 * HourMs, t0 + 8 * HourMs, t0 + 2 * HourMs, t0 + 9 * HourMs + 3_000,
                t0 + 9 * HourMs + 3_000, t0 + 30 * HourMs, t0 + 29 * HourMs, t0 + 30 * HourMs + 1,
            };
            long expectedMark = s.State.LastProcessedUtcMs;
            long credited = 0;
            long timeStart = s.Player.PlayerTimeTicks;
            foreach (long now in readings)
            {
                long markBefore = s.State.LastProcessedUtcMs;
                long expectedTicks = Math.Min(CapHours * 3600 * S, Math.Max(0, now - expectedMark) * 1000L);
                var summary = OfflineProgress.Apply(s, now);
                expectedMark = Math.Max(expectedMark, now);
                Assert.AreEqual(expectedTicks, summary.Ticks, "показание " + (now - t0) + " мс от старта");
                Assert.AreEqual(expectedMark, s.State.LastProcessedUtcMs, "метка = max(сохранённое, now)");
                Assert.GreaterOrEqual(s.State.LastProcessedUtcMs, markBefore, "метка не убывает");
                credited += summary.Ticks;
                Assert.AreEqual(credited, s.Player.PlayerTimeTicks - timeStart, "время игрока = сумма начислений");
            }
            // 10 с + 0,5 с + 70 с + 8 ч (лимит) + 3 с + 8 ч (лимит) + 1 мс.
            Assert.AreEqual(80_500L * 1000 + 2 * CapHours * 3600 * S + 3 * S + 1000, credited);
            reference.Advance(credited);
            AssertSameByCriterion(reference, s, "последовательность показаний против одного Advance");
        }

        // ───────────────────────────── Лимит 8 ч ─────────────────────────────

        /// <summary>E-19, 12.3: лимит ровно 8 ч, граница по миллисекунде; повторные долгие отлучки ограничиваются каждая отдельно.</summary>
        [Test]
        public void OfflineCapIsExactlyEightHours()
        {
            Assert.AreEqual(28_800_000_000L, GameSession.MaxOfflineTicks, "8 ч × 3600 с × 10^6 тиков");

            var cases = new[]
            {
                (gapMs: CapHours * HourMs - 1, ticks: CapHours * 3600 * S - 1000),
                (gapMs: CapHours * HourMs, ticks: CapHours * 3600 * S),
                (gapMs: CapHours * HourMs + 1, ticks: CapHours * 3600 * S),
                (gapMs: 7L * 24 * HourMs, ticks: CapHours * 3600 * S),
            };
            foreach (var c in cases)
            {
                var s = Zone1WithCats();
                long before = s.Player.PlayerTimeTicks;
                var summary = OfflineProgress.Apply(s, StartUtcMs + c.gapMs);
                Assert.AreEqual(c.ticks, summary.Ticks, "разрыв " + c.gapMs + " мс");
                Assert.AreEqual(c.ticks, s.Player.PlayerTimeTicks - before, "разрыв " + c.gapMs + " мс: время игрока");
                Assert.AreEqual(StartUtcMs + c.gapMs, s.State.LastProcessedUtcMs, "разрыв " + c.gapMs + " мс: метка — момент расчёта");
                if (c.gapMs > CapHours * HourMs) Assert.IsTrue(summary.Capped, "разрыв " + c.gapMs + " мс: признак лимита");
            }

            // Две отлучки по 10 ч подряд: каждая ограничена 8 ч, остаток не переносится и не теряет следующую.
            var twice = Zone1WithCats();
            Assert.AreEqual(CapHours * 3600 * S, OfflineProgress.Apply(twice, StartUtcMs + 10 * HourMs).Ticks);
            Assert.AreEqual(CapHours * 3600 * S, OfflineProgress.Apply(twice, StartUtcMs + 20 * HourMs).Ticks);
            Assert.AreEqual(5 * S, OfflineProgress.Apply(twice, StartUtcMs + 20 * HourMs + 5 * SecondMs).Ticks,
                "срезанные лимитом часы не переносятся на следующий расчёт");
        }

        /// <summary>
        /// E-19: «Ограничение действует для всех систем». Офлайн 24 ч ≡ Advance(8 ч) по критерию 15.2:
        /// добыча, награды, кузница, ручной буфер, бесплатный котик, бесплатный сундук, PRNG.
        /// </summary>
        [Test]
        public void OfflineCapAppliesToAllSystems()
        {
            var s = RichScenario();
            var reference = Clone(s);
            long zoneTimeBefore = s.Mine.ZoneTimeTicks;
            var summary = OfflineProgress.Apply(s, StartUtcMs + 24 * HourMs);
            Assert.AreEqual(CapHours * 3600 * S, summary.Ticks);
            Assert.AreEqual(CapHours * 3600 * S, s.Mine.ZoneTimeTicks - zoneTimeBefore, "время зоны тоже ограничено");
            reference.Advance(CapHours * 3600 * S);
            AssertSameByCriterion(reference, s, "офлайн 24 ч против Advance(8 ч)");
            Assert.AreEqual(2, s.Player.FreeChest.Stock, "сценарий: сундук через 600 с и через 4 ч 10 мин, запас не больше 2");
        }

        /// <summary>E-19: даже предельное показание часов (long.MaxValue мс) даёт ровно 8 ч, без переполнения.</summary>
        [Test]
        public void ExtremeClockValueIsCappedWithoutOverflow()
        {
            var s = Zone1WithCats();
            var reference = Clone(s);
            var summary = OfflineProgress.Apply(s, long.MaxValue);
            Assert.AreEqual(CapHours * 3600 * S, summary.Ticks);
            Assert.AreEqual(long.MaxValue, s.State.LastProcessedUtcMs);
            Assert.IsTrue(OfflineProgress.Apply(s, long.MaxValue).IsEmpty);
            Assert.IsTrue(OfflineProgress.Apply(s, StartUtcMs).IsEmpty, "часы вернулись к норме — метка в будущем, офлайна нет");
            reference.Advance(CapHours * 3600 * S);
            AssertSameByCriterion(reference, s, "предельные часы против Advance(8 ч)");
        }
    }
}
