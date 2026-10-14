using System;
using System.Collections.Generic;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Save;
using CatMiners.Core.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Правила ядра, доведённые до ТЗ: перенос котика (7.3–7.5, 8.2), BFS клетки появления через открытые ворота (6.6, G-06),
    /// данные вместо литералов ID (4.1), блокировка команд после конца кампании (11.4), отложенная офлайн-автоматизация
    /// (9.4, 12.3), цена карты одним методом (15.1), поток ГСЧ разброса таймеров (15.2), F-3 в GameNumber (15.1),
    /// окно «Пока вас не было» (12.3), настройки и камера в сохранении (11.3, 12.1, 13.2), часы (15.2), обработчик
    /// завершения зоны (16), очередь сундуков (10.3).
    /// </summary>
    public class CoreRulesFixTests
    {
        const long S = GameSession.TicksPerSecond;
        const long StartUtcMs = 1_000_000L;
        const long HourMs = 3600L * 1000L;

        static BalanceRepository B => TestData.Balance;

        static GameSession Debug(int zone) => GameSession.NewGame(B, 12345UL, SaveSlots.Debug, StartUtcMs, zone);

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static string Snapshot(GameSession s) => SaveService.SerializeState(s.State);

        static GameSession Restore(string json) => GameSession.FromState(B, SaveService.DeserializeState(json));

        static CatState Spawn(GameSession s, int level)
        {
            Assert.IsTrue(s.DebugSpawnCat(level), "есть клетка появления");
            return s.Mine.Cats.Last();
        }

        static DropOutcome DropAt(GameSession s, CatState cat, int cell) =>
            s.DropCat(cat.Id, s.Layout.Col(cell) + 0.5, s.Layout.Row(cell) + 0.5);

        static int Hits(GameSession s, int catId) => s.Events.Count(e => e.Type == SimEventType.TargetHit && e.CatId == catId);

        static (int, int) Cr(GameSession s, int cell) => (s.Layout.Col(cell), s.Layout.Row(cell));

        static void OpenGates(GameSession s, int count)
        {
            for (int k = 0; k < count; k++)
            {
                var gate = s.Layout.Gates[k];
                s.DebugCompleteTarget(gate.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal), "ворота " + k);
            }
            Assert.AreEqual(count, s.Mine.GatesOpened);
        }

        /// <summary>Zone1: котик L1 на камне (2, 4) с рабочей клетки (2, 5), фаза — половина задержки атаки.</summary>
        static GameSession MiningCat(out CatState cat, out int rock, out long phase)
        {
            var s = Debug(1);
            cat = Spawn(s, 1);
            rock = OrdAt(s, 2, 4);
            Assert.GreaterOrEqual(rock, 0);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, 2.5, 4.5).Result);
            Assert.AreEqual(s.Layout.Objects[rock].Index, cat.TargetIndex);
            Assert.AreEqual((2, 5), Cr(s, cat.Cell));
            long delay = s.AttackDelayTicks(cat.Level);
            s.Advance(delay / 2);
            phase = cat.PhaseTicks;
            Assert.AreEqual(delay / 2, phase);
            s.Events.Clear();
            return s;
        }

        /// <summary>Свободная клетка, рабочая для двух разных допустимых целей; nA, nB — их соседние клетки footprint.</summary>
        static int CellBetweenTwoTargets(GameSession s, out int nA, out int nB)
        {
            var l = s.Layout;
            var neighbours = new List<int>();
            for (int c = 0; c < l.CellCount; c++)
            {
                if (!s.IsCellFree(c)) continue;
                l.Neighbors4(c, neighbours);
                var ords = new List<int>();
                var cells = new List<int>();
                foreach (int n in neighbours.ToList())
                {
                    int ord = s.ObjectAt(n);
                    if (ord < 0 || ords.Contains(ord) || !s.IsTargetAvailable(ord) || !s.IsWorkCellOf(ord, c)) continue;
                    ords.Add(ord);
                    cells.Add(n);
                }
                if (ords.Count < 2) continue;
                nA = cells[0];
                nB = cells[1];
                return c;
            }
            Assert.Fail(l.ZoneId + ": нет клетки между двумя целями");
            nA = nB = -1;
            return -1;
        }

        // ───────────────────────────── 7.5: перенос ─────────────────────────────

        [Test]
        public void HeldCatNeitherHitsNorGainsPhase_CancelRestoresPhaseWithoutExtraHit()
        {
            var s = MiningCat(out var cat, out int rock, out long phase);
            long delay = s.AttackDelayTicks(cat.Level);
            var hp = s.Mine.Objects[rock].Hp;

            Assert.AreEqual(0, s.DraggingCatId);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            Assert.AreEqual(cat.Id, s.DraggingCatId);
            Assert.AreEqual(cat.Cell, s.DragOriginCell);
            Assert.IsFalse(s.Events.Any(), "начало переноса не публикует событий");
            s.Advance(10 * delay);
            Assert.AreEqual(0, Hits(s, cat.Id), "в переносе котик не наносит ударов");
            Assert.AreEqual(hp, s.Mine.Objects[rock].Hp, "HP цели не меняется");
            Assert.AreEqual(phase, cat.PhaseTicks, "фаза во время переноса не растёт");
            Assert.AreEqual(s.Layout.Objects[rock].Index, cat.TargetIndex, "назначение до отпускания прежнее");

            // Офлайн-расчёт идёт тем же Advance: переносимый котик не бьёт и там.
            OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + HourMs);
            Assert.AreEqual(hp, s.Mine.Objects[rock].Hp, "офлайн тоже без ударов");
            Assert.AreEqual(phase, cat.PhaseTicks);

            s.CancelDrag();
            Assert.AreEqual(0, s.DraggingCatId);
            Assert.AreEqual(-1, s.DragOriginCell);
            Assert.AreEqual(phase, cat.PhaseTicks, "отмена: фаза до переноса");
            Assert.AreEqual(s.Layout.Objects[rock].Index, cat.TargetIndex);
            s.Events.Clear();
            s.Advance(delay - phase - 1);
            Assert.AreEqual(0, Hits(s, cat.Id), "мгновенного дополнительного удара нет");
            s.Advance(1);
            Assert.AreEqual(1, Hits(s, cat.Id), "удар — когда фаза дошла до задержки");
            s.CancelDrag();   // без переноса — ничего не делает
        }

        [Test]
        public void DropOnOwnWorkCellOfSameTargetRestoresPhase()
        {
            var s = MiningCat(out var cat, out int rock, out long phase);
            long delay = s.AttackDelayTicks(cat.Level);
            int index = s.Layout.Objects[rock].Index;
            int cell = cat.Cell;

            // Своя клетка (правило свободной клетки).
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            s.Advance(5 * delay);
            var o = DropAt(s, cat, cell);
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.IsFalse(o.Merged);
            Assert.AreEqual(cell, o.Cell);
            Assert.AreEqual(index, o.TargetIndex);
            Assert.AreEqual(index, cat.TargetIndex);
            Assert.AreEqual(phase, cat.PhaseTicks, "своя рабочая клетка той же цели — фаза до переноса");
            Assert.AreEqual(0, s.DraggingCatId, "перенос завершён");

            // Footprint той же цели: котик остаётся на своей рабочей клетке.
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            s.Advance(5 * delay);
            o = s.DropCat(cat.Id, 2.5, 4.5);
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.AreEqual(cell, cat.Cell);
            Assert.AreEqual(index, cat.TargetIndex);
            Assert.AreEqual(phase, cat.PhaseTicks);
            Assert.AreEqual(0, s.DraggingCatId);
            Assert.AreEqual(0, Hits(s, cat.Id));

            s.Advance(delay - phase - 1);
            Assert.AreEqual(0, Hits(s, cat.Id), "без дополнительного удара");
            s.Advance(1);
            Assert.AreEqual(1, Hits(s, cat.Id));
        }

        [Test]
        public void RejectedDropRestoresPhaseAndEndsDrag()
        {
            var s = MiningCat(out var cat, out int rock, out long phase);
            long delay = s.AttackDelayTicks(cat.Level);
            int index = s.Layout.Objects[rock].Index;
            int cell = cat.Cell;

            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            s.Advance(3 * delay);
            var o = s.DropCat(cat.Id, -2.5, 3.5);
            Assert.AreEqual(CommandResult.InvalidTarget, o.Result, "за картой");
            Assert.AreEqual(cell, cat.Cell);
            Assert.AreEqual(index, cat.TargetIndex);
            Assert.AreEqual(phase, cat.PhaseTicks);
            Assert.AreEqual(0, s.DraggingCatId);

            var other = Spawn(s, 2);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            s.Advance(3 * delay);
            o = DropAt(s, cat, other.Cell);
            Assert.AreEqual(CommandResult.DifferentLevels, o.Result);
            Assert.AreEqual(cell, cat.Cell);
            Assert.AreEqual(index, cat.TargetIndex);
            Assert.AreEqual(phase, cat.PhaseTicks);
            Assert.AreEqual(0, s.DraggingCatId);
            Assert.AreEqual(0, Hits(s, cat.Id));

            // Получатель в переносе недопустим (7.4): перетаскиваемый другой котик возвращается.
            var same = Spawn(s, 1);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            int count = s.Mine.Cats.Count;
            o = DropAt(s, same, cat.Cell);
            Assert.AreNotEqual(CommandResult.Ok, o.Result);
            Assert.IsFalse(o.Merged);
            Assert.AreEqual(count, s.Mine.Cats.Count, "объединения нет");
            Assert.AreEqual(cat.Id, s.DraggingCatId, "перенос другого котика не завершён");
            s.CancelDrag();
            Assert.AreEqual(phase, cat.PhaseTicks);

            Assert.AreEqual(CommandResult.NotFound, s.BeginDrag(9999));
            Assert.AreEqual(0, s.DraggingCatId);
        }

        [Test]
        public void NewAssignmentOrMergeDuringDragStartsFromZeroPhase()
        {
            var s = MiningCat(out var cat, out _, out _);
            int other = OrdAt(s, 3, 4);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            var o = s.DropCat(cat.Id, 3.5, 4.5);
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.AreEqual(s.Layout.Objects[other].Index, cat.TargetIndex, "новая цель");
            Assert.AreEqual(0L, cat.PhaseTicks, "новое назначение — фаза 0");
            Assert.AreEqual(0, s.DraggingCatId);

            // Второй перенос сразу отменяет первый (одновременно переносится один котик).
            var b = Spawn(s, 1);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(b.Id));
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            Assert.AreEqual(cat.Id, s.DraggingCatId);
            o = DropAt(s, cat, b.Cell);
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.IsTrue(o.Merged, "объединение");
            Assert.AreEqual(0, s.DraggingCatId);
            Assert.IsNull(s.FindCat(cat.Id), "перетаскиваемый удалён");
            Assert.AreEqual(2, b.Level);
            Assert.AreEqual(0L, b.PhaseTicks);
        }

        [Test]
        public void EtaAndWorkersExcludeHeldCat_TargetFinishedByOthersLeavesHeldCatIdle()
        {
            // Zone3: у камня (2, 7) две свободные рабочие клетки — (3, 7) и (2, 8).
            var s = Debug(3);
            var rock = s.Layout.Objects[OrdAt(s, 2, 7)];
            Assert.AreEqual(ObjectKind.Rock, rock.Kind);
            var work = new List<int>();
            s.WorkCells(rock.Ordinal, work);
            Assert.AreEqual(2, work.Count, "две рабочие клетки");
            var weak = Spawn(s, 1);
            var strong = Spawn(s, 8);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(weak.Id, rock.Col + 0.5, rock.Row + 0.5).Result);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(strong.Id, rock.Col + 0.5, rock.Row + 0.5).Result);
            Assert.AreEqual(rock.Index, weak.TargetIndex);
            Assert.AreEqual(rock.Index, strong.TargetIndex);
            Assert.AreNotEqual(weak.Cell, strong.Cell);
            Assert.AreEqual(2, s.WorkersOn(rock.Ordinal));
            double both = s.EtaSeconds(rock.Ordinal);
            var hp = s.Mine.Objects[rock.Ordinal].Hp;

            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(weak.Id));
            Assert.AreEqual(1, s.WorkersOn(rock.Ordinal), "переносимый котик не работник");
            double strongOnly = s.EtaSeconds(rock.Ordinal);
            Assert.AreEqual((hp / s.CatDps(strong.Level)).ToDouble(), strongOnly, 1e-9 * strongOnly, "ETA по оставшимся работникам");
            Assert.Greater(strongOnly, both, "ETA обновляется при переносе (8.2)");
            s.CancelDrag();
            Assert.AreEqual(2, s.WorkersOn(rock.Ordinal));
            Assert.AreEqual(both, s.EtaSeconds(rock.Ordinal));

            // Цель завершили другие, пока котик в переносе: он в Idle, отмене восстанавливать нечего.
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(weak.Id));
            double eta = s.EtaSeconds(rock.Ordinal);
            Assert.Less(eta, 3600.0);
            s.Events.Clear();
            s.Advance((long)Math.Ceiling(eta + 5) * S);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[rock.Ordinal].Status);
            Assert.AreEqual(0, Hits(s, weak.Id));
            Assert.AreEqual(-1, weak.TargetIndex, "цель завершилась — Idle");
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.CatIdle && e.CatId == weak.Id));
            s.CancelDrag();
            Assert.AreEqual(-1, weak.TargetIndex);
            Assert.AreEqual(0L, weak.PhaseTicks);
            Assert.AreEqual(0, s.DraggingCatId);
            Assert.AreEqual(double.PositiveInfinity, s.EtaSeconds(OrdAt(s, 3, 6)), "без работников — бесконечность (UI пишет «Нет работников»)");
        }

        [Test]
        public void DragStateIsTransientAndClearedOnLoadAndZoneEntry()
        {
            var s = MiningCat(out var cat, out _, out long phase);
            Assert.AreEqual(CommandResult.Ok, s.BeginDrag(cat.Id));
            string json = Snapshot(s);
            Assert.IsFalse(json.Contains("Drag"), "переходное состояние не сохраняется");
            var r = Restore(json);
            Assert.AreEqual(0, r.DraggingCatId, "при загрузке переходных состояний нет");
            var rc = r.FindCat(cat.Id);
            Assert.AreEqual(phase, rc.PhaseTicks);
            r.Events.Clear();
            r.Advance(r.AttackDelayTicks(1) - phase);
            Assert.AreEqual(1, Hits(r, cat.Id), "после загрузки котик снова бьёт");

            s.DebugGotoZone(2);
            Assert.AreEqual(0, s.DraggingCatId, "вход в зону сбрасывает перенос");
        }

        // ───────────────────────────── 7.3: отпускание на свою клетку ─────────────────────────────

        [Test]
        public void IdleCatDroppedOnOwnCellNextToTargetGetsAssigned()
        {
            // Zone3: клетка (3, 7) — рабочая для камней (3, 6) и (2, 7).
            var s = Debug(3);
            int cell = CellBetweenTwoTargets(s, out int nA, out int nB);
            var cat = Spawn(s, 1);
            Assert.AreEqual(CommandResult.Ok, DropAt(s, cat, cell).Result);
            Assert.AreEqual(cell, cat.Cell);
            int first = s.OrdinalOf(cat.TargetIndex);
            int ordA = s.ObjectAt(nA), ordB = s.ObjectAt(nB);
            Assert.That(first, Is.EqualTo(ordA).Or.EqualTo(ordB));
            int second = first == ordA ? ordB : ordA;

            s.DebugCompleteTarget(first);
            Assert.AreEqual(-1, cat.TargetIndex, "цель завершилась — Idle, новую цель котик сам не выбирает");
            s.Advance(3 * S);
            Assert.AreEqual(-1, cat.TargetIndex);

            s.Events.Clear();
            var o = DropAt(s, cat, cell);
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.AreEqual(cell, o.Cell);
            Assert.AreEqual(cell, cat.Cell, "котик остаётся на своей клетке");
            Assert.AreEqual(s.Layout.Objects[second].Index, cat.TargetIndex, "своя клетка свободна для него: назначение на смежную цель");
            Assert.AreEqual(cat.TargetIndex, o.TargetIndex);
            Assert.AreEqual(0L, cat.PhaseTicks, "новое назначение — фаза 0");
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.CatAssigned && e.CatId == cat.Id));
        }

        [Test]
        public void MiningCatDroppedOnOwnCellIsReassignedToNearerAdjacentTarget()
        {
            var s = Debug(3);
            int cell = CellBetweenTwoTargets(s, out int nA, out int nB);
            var cat = Spawn(s, 1);
            Assert.AreEqual(CommandResult.Ok, DropAt(s, cat, cell).Result);
            int current = s.OrdinalOf(cat.TargetIndex);
            int nCurrent = s.ObjectAt(nA) == current ? nA : nB;
            int nOther = nCurrent == nA ? nB : nA;
            s.Advance(s.AttackDelayTicks(1) / 2);
            long phase = cat.PhaseTicks;
            Assert.Greater(phase, 0L);

            double Toward(int n, bool col) =>
                col ? s.Layout.Col(cell) + 0.5 + 0.45 * (s.Layout.Col(n) - s.Layout.Col(cell))
                    : s.Layout.Row(cell) + 0.5 + 0.45 * (s.Layout.Row(n) - s.Layout.Row(cell));

            var o = s.DropCat(cat.Id, Toward(nCurrent, true), Toward(nCurrent, false));
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.AreEqual(s.Layout.Objects[current].Index, cat.TargetIndex, "та же цель — без изменений");
            Assert.AreEqual(phase, cat.PhaseTicks);

            o = s.DropCat(cat.Id, Toward(nOther, true), Toward(nOther, false));
            Assert.AreEqual(CommandResult.Ok, o.Result);
            Assert.AreEqual(cell, cat.Cell);
            Assert.AreEqual(s.Layout.Objects[s.ObjectAt(nOther)].Index, cat.TargetIndex, "ближе к точке отпускания другая смежная цель");
            Assert.AreEqual(0L, cat.PhaseTicks);
        }

        // ───────────────────────────── 7.3: проверка назначений при загрузке ─────────────────────────────

        [Test]
        public void InvalidLoadedAssignmentBecomesIdle()
        {
            var s = Debug(5);
            var a = Spawn(s, 1);
            var b = Spawn(s, 1);
            var c = Spawn(s, 1);
            int rockA = OrdAt(s, 1, 18);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(a.Id, 1.5, 18.5).Result);
            Assert.AreEqual(s.Layout.Objects[rockA].Index, a.TargetIndex);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(b.Id, 2.5, 18.5).Result);
            Assert.AreEqual(s.Layout.Objects[OrdAt(s, 2, 18)].Index, b.TargetIndex);
            s.Advance(S / 2);

            var state = SaveService.DeserializeState(Snapshot(s));
            var la = state.Mine.Cats.Single(x => x.Id == a.Id);
            var lb = state.Mine.Cats.Single(x => x.Id == b.Id);
            var lc = state.Mine.Cats.Single(x => x.Id == c.Id);
            long phaseA = la.PhaseTicks;
            // b: клетка не рабочая для цели (далёкий камень); c: цель в закрытом секторе над воротами.
            int far = s.Layout.Objects.First(o => o.Kind == ObjectKind.Rock && o.Row > s.Layout.Gates[0].Row && !s.IsWorkCellOf(o.Ordinal, lb.Cell)).Index;
            lb.TargetIndex = far;
            int closed = s.Layout.Objects.First(o => o.Kind == ObjectKind.Rock && o.Row < s.Layout.Gates[0].Row).Index;
            lc.TargetIndex = closed;
            lc.PhaseTicks = 123;

            var r = GameSession.FromState(B, state);
            Assert.AreEqual(s.Layout.Objects[rockA].Index, r.FindCat(a.Id).TargetIndex, "допустимая пара TargetKey/WorkCell сохраняется");
            Assert.AreEqual(phaseA, r.FindCat(a.Id).PhaseTicks);
            Assert.AreEqual(-1, r.FindCat(b.Id).TargetIndex, "WorkCell не рабочая клетка цели — Idle");
            Assert.AreEqual(0L, r.FindCat(b.Id).PhaseTicks);
            Assert.AreEqual(-1, r.FindCat(c.Id).TargetIndex, "цель в закрытом секторе — Idle");
            Assert.AreEqual(0L, r.FindCat(c.Id).PhaseTicks);
        }

        // ───────────────────────────── 6.6, G-06: клетка появления ─────────────────────────────

        /// <summary>Независимая запись правила 6.6: BFS по открытым секторам и строкам открытых ворот от нижнего ряда зоны мержа; сначала зона мержа, затем SpawnPad.</summary>
        static int ReferenceSpawnCell(GameSession s, out int top, out int bottom)
        {
            var l = s.Layout;
            int opened = s.Mine.GatesOpened;
            if (opened > 0)
            {
                int g = l.Gates[opened - 1].Row;
                top = g - 3;
                bottom = g - 1;
            }
            else
            {
                int c = l.RowC;
                top = c - 3;
                bottom = Math.Min(l.Depth - 1, c + 1);
            }
            top = Math.Max(0, top);
            var gateRows = new HashSet<int>(l.Gates.Where(g => g.GateOrder < opened).Select(g => g.Row));
            bool Open(int cell) => l.CellSector[cell] >= 0 && l.CellSector[cell] <= opened;
            bool Passable(int cell) => Open(cell) || gateRows.Contains(l.Row(cell));
            bool Free(int cell) => Open(cell) && s.ObjectAt(cell) < 0 && s.CatAt(cell) == null && s.ContainerAt(cell) == null;

            // Зона мержа — самая нижняя пара соседних строк площадки без footprint объектов во всю ширину.
            bool FullyFree(int row) => Enumerable.Range(0, l.Width).All(col => l.CellOwner[l.CellIndex(col, row)] < 0);
            int mergeBottom = -1;
            for (int r = bottom; r - 1 >= top; r--)
                if (FullyFree(r) && FullyFree(r - 1)) { mergeBottom = r; break; }
            int start = l.CellIndex(3, mergeBottom >= 0 ? mergeBottom : (top + bottom) / 2);
            var order = new List<int>();
            if (Passable(start))
            {
                var seen = new HashSet<int> { start };
                var queue = new Queue<int>();
                queue.Enqueue(start);
                var dirs = new[] { (0, -1), (-1, 0), (1, 0), (0, 1) };   // верх, лево, право, низ
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    order.Add(cur);
                    foreach (var (dc, dr) in dirs)
                    {
                        int nc = l.Col(cur) + dc, nr = l.Row(cur) + dr;
                        if (!l.InBounds(nc, nr)) continue;
                        int n = l.CellIndex(nc, nr);
                        if (seen.Contains(n) || !Passable(n)) continue;
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            if (mergeBottom >= 0)
                foreach (int c in order)
                    if ((l.Row(c) == mergeBottom || l.Row(c) == mergeBottom - 1) && Free(c)) return c;
            int t = top, b = bottom;
            foreach (int c in order)
                if (l.Row(c) >= t && l.Row(c) <= b && Free(c)) return c;
            foreach (int c in order)
                if (Free(c)) return c;
            return -1;
        }

        [Test]
        public void SpawnBfsCrossesOpenedGateRow_TopSectorFullSpillsBelowGate()
        {
            var s = Debug(5);
            var l = s.Layout;
            var gate = l.Gates[0];
            OpenGates(s, 1);
            s.SpawnPadRows(out int top, out int bottom);
            Assert.AreEqual((gate.Row - 3, gate.Row - 1), (top, bottom), "SpawnPad — три строки над открытыми воротами");

            // Заполняем все свободные клетки верхнего (только что открытого) сектора.
            for (int c = 0; c < l.CellCount; c++)
            {
                if (l.CellSector[c] != 1 || !s.IsCellFree(c)) continue;
                var cat = Spawn(s, 1);
                if (cat.Cell != c) Assert.AreEqual(CommandResult.Ok, DropAt(s, cat, c).Result, "ставим котика в " + Cr(s, c));
                Assert.AreEqual(c, cat.Cell);
            }
            Assert.IsFalse(Enumerable.Range(0, l.CellCount).Any(c => l.CellSector[c] == 1 && s.IsCellFree(c)), "верхний сектор заполнен");

            int spill = s.FindSpawnCell();
            Assert.AreEqual(ReferenceSpawnCell(s, out _, out _), spill, "тот же порядок обхода, что у BFS по ТЗ 6.6");
            Assert.AreEqual(0, l.CellSector[spill], "нижний открытый сектор");
            // Строки 12–17 под воротами заняты объектами: первая свободная клетка обхода — (3, 18), а не низ сектора у кузницы.
            Assert.AreEqual((3, gate.Row + 7), Cr(s, spill), "ближайшая по обходу свободная клетка под открытыми воротами");
            Assert.Less(l.Row(spill), l.RowC - 3, "не нижняя часть карты у кузницы");
        }

        [Test]
        public void SpawnCellsFollowReferenceBfsUntilNoSpace()
        {
            foreach (var (zone, gates) in new[] { (1, 0), (5, 1), (20, 1), (20, 2) })
            {
                var s = Debug(zone);
                int open = Math.Min(gates, s.Layout.Gates.Count);
                OpenGates(s, open);
                string ctx = s.Layout.ZoneId + ", открыто ворот " + open;
                int guard = 0;
                while (true)
                {
                    int expected = ReferenceSpawnCell(s, out _, out _);
                    Assert.AreEqual(expected, s.FindSpawnCell(), ctx + ": котик " + (s.Mine.Cats.Count + 1));
                    if (expected < 0) break;
                    Assert.IsTrue(s.DebugSpawnCat(1));
                    Assert.Less(++guard, 3000, ctx);
                }
                // Все свободные клетки открытых секторов заняты, строки ворот не выдаются.
                Assert.IsTrue(s.Mine.Cats.All(c => s.Layout.CellSector[c.Cell] >= 0 && s.Layout.CellSector[c.Cell] <= open), ctx);
            }
        }

        [Test]
        public void SpawnPadAndStartCellAreOpenInEveryZoneAndGateCount()
        {
            for (int n = 1; n <= B.ZoneCount; n++)
            {
                var s = Debug(n);
                for (int k = 0; k <= s.Layout.Gates.Count; k++)
                {
                    s.Mine.GatesOpened = k;
                    int expected = ReferenceSpawnCell(s, out int top, out int bottom);
                    s.SpawnPadRows(out int t, out int b);
                    Assert.AreEqual((top, bottom), (t, b), s.Layout.ZoneId + ": SpawnPad при " + k + " воротах");
                    Assert.GreaterOrEqual(expected, 0, s.Layout.ZoneId + ": есть клетка появления при " + k + " воротах");
                    Assert.AreEqual(expected, s.FindSpawnCell(), s.Layout.ZoneId + ": клетка появления при " + k + " воротах");
                }
            }
        }

        // ───────────────────────────── 4.1: правила из данных, а не литералы ID ─────────────────────────────

        [Test]
        public void ExcludedAndChestRocksAreDerivedFromRewardCycles()
        {
            var s = Debug(1);
            foreach (var kv in B.Rocks)
            {
                var cycle = kv.Value.RewardCycle ?? Array.Empty<RewardModelDto>();
                bool excluded = cycle.Length > 0 && cycle.All(e => e.RewardType != RewardTypes.Gacha && e.DetailedType != "Core" && e.DetailedType != "Soft" && e.DetailedType != "Hard");
                bool chest = cycle.Length > 0 && cycle.All(e => e.RewardType == RewardTypes.Gacha);
                Assert.AreEqual(excluded, s.IsExcludedRewardRock(kv.Key), kv.Key);
                Assert.AreEqual(chest, s.IsChestRock(kv.Key), kv.Key);
            }
            Assert.IsTrue(s.IsExcludedRewardRock("rockbattlepasscurrency"), "только награда сезонного пропуска");
            Assert.AreEqual(1, B.Rocks.Keys.Count(s.IsExcludedRewardRock), "в текущих данных такой камень один");
            Assert.IsFalse(s.IsExcludedRewardRock("rockbasic"));
            Assert.IsFalse(s.IsExcludedRewardRock("нет такого"));
            Assert.IsTrue(s.IsChestRock("rockgachawooden"));
            Assert.IsTrue(s.IsChestRock("rockonboardinggachawoodenmine3"));
            Assert.IsFalse(s.IsChestRock("rockbasic"), "смешанный цикл — не сундучный камень");
            Assert.IsFalse(s.IsChestRock("rockonboardingmine5"), "смешанный цикл — не сундучный камень");

            // zone3: один сундучный камень (3, 2).
            var z3 = Debug(3);
            Assert.AreEqual(1, z3.RemainingChestRocks());
            int rocks = z3.RemainingRocks();
            Assert.AreEqual(z3.Layout.Objects.Count(o => o.Kind == ObjectKind.Rock), rocks);
            z3.DebugCompleteTarget(OrdAt(z3, 3, 2));
            Assert.AreEqual(0, z3.RemainingChestRocks());
            Assert.AreEqual(rocks - 1, z3.RemainingRocks());

            // zone7: камень сезонного пропуска в «оставшиеся камни» не входит.
            var z7 = Debug(7);
            int all = z7.Layout.Objects.Count(o => o.Kind == ObjectKind.Rock);
            int pass = z7.Layout.Objects.Count(o => o.Kind == ObjectKind.Rock && o.TypeId == "rockbattlepasscurrency");
            Assert.Greater(pass, 0);
            Assert.AreEqual(all - pass, z7.RemainingRocks());
        }

        // ───────────────────────────── 11.4: конец кампании ─────────────────────────────

        [Test]
        public void CommandsAreRejectedAfterCampaignCompleted()
        {
            var s = Debug(5);
            s.DebugAddGold(GameNumber.FromDouble(1e12));
            s.DebugAddElixir(GameNumber.FromDouble(1e12));
            var shafts = s.Layout.Shafts;
            // Ручная шахта с готовой выплатой.
            var manual = shafts.First();
            s.DebugCompleteTarget(manual.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(manual.Ordinal));
            if (!s.Mine.Objects[manual.Ordinal].Gen.Automated)
            {
                s.Advance(s.GeneratorCycleTicks(manual.Ordinal));
                Assert.IsTrue(s.Mine.Objects[manual.Ordinal].Gen.PayoutReady);
            }
            var ready = shafts.Skip(1).First();
            s.DebugCompleteTarget(ready.Ordinal);
            var gate = s.Layout.Gates[0];
            s.DebugCompleteTarget(gate.Ordinal);
            s.Advance((long)Math.Ceiling(s.FreeCatIntervalSeconds() + 1) * S);
            Assert.IsTrue(s.Mine.Containers.Any(), "контейнер бесплатного котика");
            s.Player.FreeChest.Enabled = true;
            s.Player.FreeChest.Stock = 1;
            s.DebugAddDynamite(1);
            var card = B.CardsInOrder.First(c => GameSession.IsAppliedCardCode(c.StatModifierType) && B.CardMaxLevel(c.Rarity) >= 2);
            s.Player.Cards[card.Id] = new CardState { Level = 1, Duplicates = 1000 };
            Assert.AreEqual(CommandResult.Ok, s.CanUpgradeCard(card.Id));
            int cart = s.Layout.Cart.Ordinal;
            int rock = OrdAt(s, 2, 12);
            Assert.IsTrue(s.IsTargetAvailable(rock));

            s.Player.CampaignCompleted = true;
            s.Events.Clear();
            string before = Snapshot(s);
            var results = new Dictionary<string, CommandResult>();
            results["UseDynamite"] = s.UseDynamite(2.5, 12.5);
            results["CollectPayout"] = s.CollectPayout(manual.Ordinal);
            results["UpgradeGenerator"] = s.UpgradeGenerator(cart, out int bought);
            results["UpgradeToThreshold"] = s.UpgradeToThreshold(cart, out int boughtT);
            results["ActivateShaft"] = s.ActivateShaft(ready.Ordinal);
            results["OpenGate"] = s.OpenGate(gate.Ordinal);
            results["OpenContainer"] = s.OpenContainer(s.Mine.Containers[0].Id);
            results["ClaimFreeChest"] = s.ClaimFreeChest();
            results["UpgradeCard"] = s.UpgradeCard(card.Id);
            foreach (var kv in results) Assert.AreEqual(CommandResult.NotAllowed, kv.Value, kv.Key);
            Assert.AreEqual(0, bought);
            Assert.AreEqual(0, boughtT);
            Assert.AreEqual(results.Count, s.Events.Count(e => e.Type == SimEventType.CommandRejected), "каждый отказ — событие CommandRejected");
            Assert.AreEqual(results.Count, s.Events.Count, "других событий нет");
            Assert.AreEqual(before, Snapshot(s), "состояние не меняется, награды не выдаются");
            Assert.AreEqual(CommandResult.NotAllowed, s.CanUpgradeCard(card.Id));
            Assert.AreEqual(CommandResult.NotAllowed, s.CanHire());

            // Выбор зоны после конца кампании — только инструменты разработчика.
            s.DebugGotoZone(5);
            Assert.IsFalse(s.Player.CampaignCompleted);
            s.DebugAddGold(GameNumber.FromDouble(1e12));
            Assert.AreEqual(CommandResult.Ok, s.UpgradeGenerator(s.Layout.Cart.Ordinal, out _));
            Assert.AreEqual(CommandResult.Ok, s.UpgradeCard(card.Id));
        }

        // ───────────────────────────── 9.4, 12.3: офлайн-карта не автоматизирует ручную шахту ─────────────────────────────

        [Test]
        public void OfflineCardDefersAutomationUntilFirstOnlineStep()
        {
            var s = Debug(3);
            var citrine = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            Assert.AreEqual("ca003", citrine.CardId);
            Assert.AreEqual(1, citrine.CardLevel);
            s.DebugCompleteTarget(citrine.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(citrine.Ordinal));
            var gen = s.Mine.Objects[citrine.Ordinal].Gen;
            Assert.IsFalse(gen.Automated);
            Assert.AreEqual(0, s.CardLevel("ca003"));
            var buffer = s.GeneratorOutput(citrine.Ordinal);
            Assert.AreEqual(6000.0, buffer.ToDouble(), "citrine L1: F-0 0 + 6000·1, без карты ca003 и ворот");

            // Сундучный камень (3, 2) с ca003: освобождаем его рабочую клетку (3, 3) и ставим котика.
            int chestRock = OrdAt(s, 3, 2);
            Assert.AreEqual("rockonboardinggachawoodenmine3", s.Layout.Objects[chestRock].TypeId);
            s.DebugCompleteTarget(OrdAt(s, 3, 3));
            var cat = Spawn(s, 1);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, 3.5, 2.5).Result);
            Assert.AreEqual(s.Layout.Objects[chestRock].Index, cat.TargetIndex);
            double eta = s.EtaSeconds(chestRock);
            Assert.That(eta, Is.InRange(16.0, 3000.0), "буфер (15 с) готов раньше, чем придёт карта; камень разрушится за час");

            var goldBefore = s.Mine.Gold;
            s.Events.Clear();
            var summary = OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + HourMs);
            Assert.AreEqual(ObjectStatus.Destroyed, s.Mine.Objects[chestRock].Status);
            Assert.AreEqual(1, s.CardLevel("ca003"), "карта пришла из сундука офлайн");
            Assert.IsFalse(gen.Automated, "до конца офлайн-интервала шахта ручная");
            Assert.IsTrue(gen.PayoutReady, "не больше одного готового буфера");
            Assert.AreEqual(buffer, gen.PendingPayout);
            Assert.IsTrue(s.Mine.AutomationCheckPending);
            Assert.AreEqual(1, summary.PayoutsReady);
            Assert.IsFalse(s.Player.Ledger.ContainsKey("gold:shaft"), "ручная выплата не подмешана в офлайн-золото");
            Assert.AreEqual(s.Mine.Gold - goldBefore, summary.Gold);
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.ShaftAutomated));
            Assert.IsTrue(summary.HasNews);

            // Флаг сохраняется; первый онлайн-шаг автоматизирует шахту и зачисляет её буфер.
            var r = Restore(Snapshot(s));
            Assert.IsTrue(r.Mine.AutomationCheckPending);
            var gold0 = r.Mine.Gold;
            r.Events.Clear();
            r.Advance(1);
            var rgen = r.Mine.Objects[citrine.Ordinal].Gen;
            Assert.IsTrue(rgen.Automated);
            Assert.IsFalse(rgen.PayoutReady);
            Assert.IsFalse(r.Mine.AutomationCheckPending);
            Assert.AreEqual(buffer, r.Player.Ledger["gold:shaft"]);
            Assert.IsTrue(r.Mine.Gold - gold0 >= buffer, "буфер зачислен в золото");
            Assert.AreEqual(1, r.Events.Count(e => e.Type == SimEventType.ShaftAutomated));
            Assert.IsFalse(r.RunDeferredAutomationCheck(), "повторно не выполняется");

            // Тот же результат прямым вызовом (контроллер после окна).
            Assert.IsTrue(s.RunDeferredAutomationCheck());
            Assert.IsTrue(gen.Automated);
            Assert.AreEqual(buffer, s.Player.Ledger["gold:shaft"]);
        }

        // ───────────────────────────── 15.1: цена карты — одним методом ─────────────────────────────

        [Test]
        public void CardUpgradeCostLookupIsWhatUpgradeDebits()
        {
            var s = Debug(1);
            var card = B.CardsInOrder.First(c => GameSession.IsAppliedCardCode(c.StatModifierType) && B.CardMaxLevel(c.Rarity) >= 3);
            Assert.IsFalse(s.TryGetCardUpgradeCost(card.Id, out _, out _), "карты нет");
            Assert.IsFalse(s.TryGetCardUpgradeCost("нет такой", out _, out _));
            Assert.IsFalse(s.TryGetCardUpgradeCost(null, out _, out _));
            s.Player.Cards[card.Id] = new CardState { Level = 1, Duplicates = 1000 };
            s.DebugAddElixir(GameNumber.FromDouble(1e12));
            var costs = B.CardUpgradeCosts[card.Rarity];
            for (int level = 1; level <= 2; level++)
            {
                Assert.IsTrue(s.TryGetCardUpgradeCost(card.Id, out int dupes, out var elixir));
                Assert.AreEqual(costs.Duplicates[level - 1], dupes);
                Assert.AreEqual(GameNumber.FromDouble(costs.SoftCurrency[level - 1]), elixir);
                int dupesBefore = s.Player.Cards[card.Id].Duplicates;
                var elixirBefore = s.Player.Elixir;
                Assert.AreEqual(CommandResult.Ok, s.UpgradeCard(card.Id));
                Assert.AreEqual(dupesBefore - dupes, s.Player.Cards[card.Id].Duplicates, "списаны те же копии");
                Assert.AreEqual(elixirBefore - elixir, s.Player.Elixir, "списан тот же эликсир");
                Assert.AreEqual(level + 1, s.CardLevel(card.Id));
            }
            s.Player.Cards[card.Id].Level = B.CardMaxLevel(card.Rarity);
            Assert.IsFalse(s.TryGetCardUpgradeCost(card.Id, out _, out _), "максимальный уровень");
            Assert.AreEqual(CommandResult.MaxLevel, s.CanUpgradeCard(card.Id));
        }

        // ───────────────────────────── 15.2: потоки ГСЧ ─────────────────────────────

        [Test]
        public void DeliveryIntervalVarianceDrawsFromTimersStream()
        {
            var s = Debug(1);
            double variance = B.Props.DeliveryDelaySecVariance;
            Assert.Greater(variance, 0.0);
            var rng = s.Player.Rng;
            ulong timers = rng.Timers.State, delivery = rng.Delivery.State;
            double plain = s.NextDeliveryIntervalSeconds(false);
            Assert.AreEqual(timers, rng.Timers.State);
            Assert.AreEqual(delivery, rng.Delivery.State);

            double draw = rng.Timers.Clone().NextDouble();
            double withVariance = s.NextDeliveryIntervalSeconds(true);
            Assert.AreNotEqual(timers, rng.Timers.State, "разброс таймера — из потока Timers");
            Assert.AreEqual(delivery, rng.Delivery.State, "поток доставок не тратится на разброс");
            Assert.AreEqual(Math.Max(1.0, plain * (1.0 + variance * (2.0 * draw - 1.0))), withVariance, 1e-9);

            // Старое сохранение без потока Timers: поток создаётся детерминированно (поток 4).
            var st1 = SaveService.DeserializeState(Snapshot(s));
            var st2 = SaveService.DeserializeState(Snapshot(s));
            st1.Player.Rng.Timers = null;
            st2.Player.Rng.Timers = null;
            var a = GameSession.FromState(B, st1);
            var b = GameSession.FromState(B, st2);
            Assert.IsNotNull(a.Player.Rng.Timers);
            Assert.AreEqual(a.Player.Rng.Timers.State, b.Player.Rng.Timers.State);
            Assert.AreEqual(a.Player.Rng.Timers.Inc, b.Player.Rng.Timers.Inc);
            Assert.AreEqual((4UL << 1) | 1UL, a.Player.Rng.Timers.Inc, "поток 4");
            Assert.AreEqual(delivery, a.Player.Rng.Delivery.State, "остальные потоки не трогаются");
        }

        // ───────────────────────────── 15.1: F-3 в GameNumber ─────────────────────────────

        [Test]
        public void Ft3NumberMatchesDoubleOnDataAndSurvivesBeyondDouble()
        {
            int compared = 0;
            void Check(double b, double m, double g, double p, double r, double x, string what)
            {
                double d = Formula.Ft3(b, m, g, p, r, x);
                if (double.IsNaN(d) || double.IsInfinity(d)) return;
                var n = Formula.Ft3Number(b, m, g, p, r, x);
                Assert.AreEqual(GameNumber.FromDouble(d), n, what + " x=" + x);
                Assert.AreEqual(n, Formula.Evaluate(3, b, m, g, x, p, r), what + ": Evaluate case 3");
                compared++;
            }
            foreach (var rock in B.Rocks.Values)
                for (int level = 1; level <= 120; level++)
                    Check(rock.CoreCurrencySecondsBase, rock.CoreCurrencySecondsMultiplier, rock.CoreCurrencySecondsGrowth,
                        rock.CoreCurrencySecondsPower, rock.CoreCurrencySecondsRound, level, rock.Id);
            foreach (var dlv in B.Root.Deliveries)
                for (int rank = 1; rank <= B.ZoneCount; rank++)
                    Check(dlv.QuantityBase, dlv.QuantityMultiplier, dlv.QuantityGrowth, dlv.QuantityPower, dlv.QuantityRound, rank, dlv.Id);
            Assert.Greater(compared, 1000);

            // (G·x)^P за пределами double: без Infinity и исключений.
            Assert.IsTrue(double.IsInfinity(Formula.Ft3(0, 1, 10, 400, 0, 1)), "в double — Infinity");
            var big = Formula.Ft3Number(0, 1, 10, 400, 5, 1);
            Assert.IsFalse(big.FitsInDouble);
            Assert.AreEqual(400.0, big.Log10(), 1e-9);
            Assert.AreEqual(big, Formula.Evaluate(3, 0, 1, 10, 1, 400, 5));
            Assert.AreEqual(GameNumber.FromDouble(2.5), Formula.Ft3Number(2.5, 0, 10, 400, 0, 1), "M = 0: степень не вычисляется");
            Assert.AreEqual(GameNumber.FromDouble(130), Formula.Ft3Number(0, 1, 2, 7, 10, 1), "F-R: 128 → 130");
        }

        // ───────────────────────────── 12.3: окно «Пока вас не было» ─────────────────────────────

        [Test]
        public void OfflineSummaryHasNewsAndSeparatesGateExitShaft()
        {
            Assert.IsFalse(new OfflineSummary().HasNews);
            Assert.IsFalse(new OfflineSummary { Ticks = 3600 * S }.HasNews, "одно время без событий — не новость");
            Assert.IsTrue(new OfflineSummary { Gold = GameNumber.One }.HasNews);
            Assert.IsTrue(new OfflineSummary { Elixir = GameNumber.One }.HasNews);
            Assert.IsTrue(new OfflineSummary { RocksDestroyed = 1 }.HasNews);
            Assert.IsTrue(new OfflineSummary { TargetsBroken = 1 }.HasNews);
            Assert.IsTrue(new OfflineSummary { Chests = 1 }.HasNews);
            Assert.IsTrue(new OfflineSummary { PayoutsReady = 1 }.HasNews);
            Assert.IsTrue(new OfflineSummary { ContainersSpawned = 1 }.HasNews);

            // zone3: выход и шахта citrine разрушаются офлайн.
            var s = Debug(3);
            var exit = s.Layout.Exit;
            var citrine = s.Layout.Shafts.Single(x => x.TypeId == "citrine");
            // Подходы закрыты камнями: освобождаем рабочую клетку (2, 1) выхода и (4, 4) шахты.
            s.DebugCompleteTarget(OrdAt(s, 2, 1));
            s.DebugCompleteTarget(OrdAt(s, 4, 4));
            var exitCat = Spawn(s, 12);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(exitCat.Id, exit.Col + 1.5, exit.Row + 0.5).Result);
            Assert.AreEqual(exit.Index, exitCat.TargetIndex);
            var shaftCat = Spawn(s, 12);
            Assert.AreEqual(CommandResult.Ok, s.DropCat(shaftCat.Id, citrine.Col + 0.5, citrine.Row - 0.5).Result);
            Assert.AreEqual(citrine.Index, shaftCat.TargetIndex);
            Assert.Less(s.EtaSeconds(exit.Ordinal), 8 * 3600.0);
            Assert.Less(s.EtaSeconds(citrine.Ordinal), 8 * 3600.0);
            var summary = OfflineProgress.Apply(s, s.State.LastProcessedUtcMs + 8 * HourMs);
            Assert.IsTrue(summary.ExitBroken, "окно: выход разрушен");
            Assert.AreEqual(1, summary.ShaftsCleared, "окно: шахта расчищена");
            Assert.AreEqual(0, summary.GatesBroken);
            Assert.AreEqual(2, summary.TargetsBroken, "общий счётчик сохраняется");
            Assert.IsTrue(summary.HasNews);

            // zone5: ворота разрушаются офлайн.
            var g = Debug(5);
            var gate = g.Layout.Gates[0];
            g.DebugCompleteTarget(OrdAt(g, 3, gate.Row + 1));
            var gateCat = Spawn(g, 12);
            Assert.AreEqual(CommandResult.Ok, g.DropCat(gateCat.Id, 3.5, gate.Row + 0.5).Result);
            Assert.AreEqual(gate.Index, gateCat.TargetIndex);
            Assert.Less(g.EtaSeconds(gate.Ordinal), 8 * 3600.0);
            var gs = OfflineProgress.Apply(g, g.State.LastProcessedUtcMs + 8 * HourMs);
            Assert.AreEqual(1, gs.GatesBroken, "окно: ворота разрушены");
            Assert.IsFalse(gs.ExitBroken);
            Assert.AreEqual(0, gs.ShaftsCleared);
            Assert.AreEqual(1, gs.TargetsBroken);
            Assert.AreEqual(ObjectStatus.Broken, g.Mine.Objects[gate.Ordinal].Status, "ворота ждут открытия: офлайн их не открывают");
        }

        // ───────────────────────────── 11.3, 12.1, 13.2: настройки и камера ─────────────────────────────

        [Test]
        public void SettingsLiveInPlayerStateCameraInMineState()
        {
            Assert.AreEqual("PlayerProgressState", typeof(GameState).GetField("Player").FieldType.Name);
            Assert.AreEqual("MineProgressState", typeof(GameState).GetField("Mine").FieldType.Name);
            Assert.AreEqual(1, GameState.CurrentSchemaVersion, "поля добавлены без смены схемы");

            var s = Debug(1);
            Assert.IsTrue(s.Player.Settings.SoundOn);
            Assert.IsTrue(s.Player.Settings.VibrationOn);
            Assert.AreEqual(-1f, s.Mine.CameraFocusZ, "новая зона — позиция не задана (стартовая площадка)");
            s.SetSoundOn(false);
            s.SetVibrationOn(false);
            s.Mine.CameraFocusZ = 7.25f;

            var r = Restore(Snapshot(s));
            Assert.IsFalse(r.Player.Settings.SoundOn);
            Assert.IsFalse(r.Player.Settings.VibrationOn);
            Assert.AreEqual(7.25f, r.Mine.CameraFocusZ, "загрузка — сохранённая позиция камеры");
            Assert.AreEqual(Snapshot(r), Snapshot(Restore(Snapshot(r))), "сохранить → загрузить → сохранить без изменений");

            r.SetSoundOn(true);
            r.DebugGotoZone(2);
            Assert.AreEqual(-1f, r.Mine.CameraFocusZ, "новая зона сбрасывает камеру");
            Assert.IsTrue(r.Player.Settings.SoundOn, "настройки переносятся между зонами");
            Assert.IsFalse(r.Player.Settings.VibrationOn);

            // Сохранение без новых полей (до их появления) загружается со значениями по умолчанию.
            var doc = JObject.Parse(Snapshot(s));
            ((JObject)doc["Player"]).Remove("Settings");
            ((JObject)doc["Mine"]).Remove("CameraFocusZ");
            ((JObject)doc["Mine"]).Remove("AutomationCheckPending");
            var old = GameSession.FromState(B, SaveService.DeserializeState(doc.ToString(Newtonsoft.Json.Formatting.None)));
            Assert.IsTrue(old.Player.Settings.SoundOn);
            Assert.IsTrue(old.Player.Settings.VibrationOn);
            Assert.AreEqual(-1f, old.Mine.CameraFocusZ);
            Assert.IsFalse(old.Mine.AutomationCheckPending);
        }

        // ───────────────────────────── 15.2: часы ─────────────────────────────

        [Test]
        public void ManualClockAdvancesUtcAndMonotonicTogether()
        {
            var manual = new ManualClock(StartUtcMs, 5.0);
            IClock clock = manual;
            Assert.AreEqual(StartUtcMs, clock.UtcNowMs);
            Assert.AreEqual(5.0, clock.MonotonicSeconds);
            manual.Advance(1.5);
            Assert.AreEqual(StartUtcMs + 1500, clock.UtcNowMs);
            Assert.AreEqual(6.5, clock.MonotonicSeconds, 1e-12);
            manual.UtcNowMs -= 10_000;   // перевод часов назад не трогает монотонное время
            Assert.AreEqual(6.5, clock.MonotonicSeconds, 1e-12);

            // UTC-часы идут вместе с симуляцией: офлайн по этим часам засчитывает ровно прошедшее.
            var s = Debug(1);
            var c = new ManualClock(s.State.LastProcessedUtcMs);
            c.Advance(90);
            var summary = OfflineProgress.Apply(s, c.UtcNowMs);
            Assert.AreEqual(90 * S, summary.Ticks);
            Assert.AreEqual(c.UtcNowMs, s.State.LastProcessedUtcMs);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Advance(double.NaN));
        }

        // ───────────────────────────── 16: обработчик завершения зоны ─────────────────────────────

        sealed class ZoneRecorder : IZoneCompletionHandler
        {
            public readonly List<(int zone, long op)> Calls = new List<(int, long)>();
            public void OnZoneCompleted(GameSession session, int zone, long opId) => Calls.Add((zone, opId));
        }

        [Test]
        public void SimEventBusCallsZoneHandlersOncePerOperation()
        {
            var bus = new SimEventBus();
            var rec = new ZoneRecorder();
            bus.Register(rec);
            bus.Register(rec);
            Assert.AreEqual(1, bus.ZoneHandlerCount, "повторная регистрация не дублирует");
            var batch = new List<SimEvent>
            {
                new SimEvent { Type = SimEventType.MineCompleted, Zone = 3, OpId = 10 },
                new SimEvent { Type = SimEventType.RockDestroyed, Zone = 3, OpId = 11 },
                new SimEvent { Type = SimEventType.MineCompleted, Zone = 3, OpId = 10 },
                new SimEvent { Type = SimEventType.MineCompleted, Zone = 4, OpId = 12 },
            };
            bus.Dispatch(null, batch);
            CollectionAssert.AreEqual(new[] { (3, 10L), (4, 12L) }, rec.Calls);
            bus.Dispatch(null, batch);
            Assert.AreEqual(2, rec.Calls.Count, "повторная раздача той же операции не вызывает обработчик");
            bus.Unregister(rec);
            bus.Dispatch(null, new[] { new SimEvent { Type = SimEventType.MineCompleted, Zone = 5, OpId = 13 } });
            Assert.AreEqual(2, rec.Calls.Count);

            // Настоящий CompleteZone: zone1 без шахт и ворот.
            var s = Debug(1);
            Assert.AreEqual(0, s.Layout.Shafts.Count);
            Assert.AreEqual(0, s.Layout.Gates.Count);
            s.DebugCompleteTarget(s.Layout.Exit.Ordinal);
            s.Events.Clear();
            Assert.AreEqual(CommandResult.Ok, s.CompleteZone());
            var live = new SimEventBus();
            var rec2 = new ZoneRecorder();
            live.Register(rec2);
            live.Dispatch(s, s.Events);
            live.Dispatch(s, s.Events);
            long op = s.Events.Single(e => e.Type == SimEventType.MineCompleted).OpId;
            CollectionAssert.AreEqual(new[] { (1, op) }, rec2.Calls, "один вызов с зоной и opId транзакции");
        }

        // ───────────────────────────── 10.3, 11.2: очередь сундуков и ближайшая шахта ─────────────────────────────

        [Test]
        public void PeekChestRevealDoesNotRemove()
        {
            var s = Debug(3);
            Assert.IsNull(s.PeekChestReveal());
            var shaft = s.Layout.Shafts[0];
            s.DebugCompleteTarget(shaft.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(shaft.Ordinal));
            int count = s.Player.ChestReveals.Count;
            Assert.Greater(count, 0);
            var head = s.PeekChestReveal();
            Assert.AreSame(s.Player.ChestReveals[0], head);
            Assert.AreEqual(count, s.Player.ChestReveals.Count, "просмотр не снимает сундук");
            Assert.AreSame(head, s.PeekChestReveal());
            Assert.AreSame(head, s.TakeChestReveal());
            Assert.AreEqual(count - 1, s.Player.ChestReveals.Count);
        }

        [Test]
        public void NearestInactiveShaftToReferenceRow()
        {
            var s = Debug(5);
            var shafts = s.Layout.Shafts;
            Assert.AreEqual(4, shafts.Count);
            int bottom = shafts.OrderByDescending(x => x.Row).ThenBy(x => x.Ordinal).First().Ordinal;
            Assert.AreEqual(bottom, s.NearestInactiveShaft(), "без строки — прежнее правило (нижняя шахта)");

            int Expected(int refRow) => shafts.Where(x => s.Mine.Objects[x.Ordinal].Status != ObjectStatus.Producing)
                .OrderBy(x => Math.Abs(x.Row - refRow)).ThenBy(x => x.Ordinal).Select(x => x.Ordinal).DefaultIfEmpty(-1).First();
            for (int row = 0; row < s.Layout.Depth; row++)
                Assert.AreEqual(Expected(row), s.NearestInactiveShaft(row), "строка " + row);
            int top = shafts.OrderBy(x => x.Row).First().Ordinal;
            Assert.AreEqual(top, s.NearestInactiveShaft(0), "от выхода — верхняя шахта");

            // Активированные шахты не считаются.
            var lower = shafts.Where(x => x.Row > s.Layout.Gates[0].Row).ToList();
            foreach (var sh in lower)
            {
                s.DebugCompleteTarget(sh.Ordinal);
                Assert.AreEqual(CommandResult.Ok, s.ActivateShaft(sh.Ordinal));
            }
            Assert.AreEqual(Expected(s.Layout.Depth - 1), s.NearestInactiveShaft(s.Layout.Depth - 1));
            Assert.IsTrue(shafts.Where(x => x.Row < s.Layout.Gates[0].Row).Any(x => x.Ordinal == s.NearestInactiveShaft(s.Layout.Depth - 1)));
        }
    }
}
