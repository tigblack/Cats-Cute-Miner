using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Автодобыча (ТЗ 7.3, <see cref="GameSession.AutoMining"/>): котик, разбивший цель, бьёт дальше только то, что достаёт
    /// со своей клетки, — соседнюю цель, для которой его клетка рабочая. Сам он никуда не переходит: если рядом целей нет,
    /// стоит в Idle на месте, пока игрок его не перенесёт. Офлайн и без переключателя (старые правила) котик остаётся в Idle.
    /// zone1: камни — столбцы 2–3, строки 1–4.
    /// </summary>
    public class AutoMiningTests
    {
        static BalanceRepository B => TestData.Balance;

        static GameSession NewDebug(bool autoMining = true)
        {
            var s = GameSession.NewGame(B, 12345UL, SaveSlots.Debug, 1_000_000L, 1);
            s.AutoMining = autoMining;
            return s;
        }

        static int OrdAt(GameSession s, int col, int row) => s.ObjectAt(s.Layout.CellIndex(col, row));

        static (int, int) Cr(GameSession s, int cell) => (s.Layout.Col(cell), s.Layout.Row(cell));

        /// <summary>Котик в клетке (col, row), назначенный на цель <paramref name="target"/> (переносом, как игрок).</summary>
        static CatState CatAt(GameSession s, int col, int row, int target)
        {
            Assert.IsTrue(s.DebugSpawnCat(1));
            var cat = s.Mine.Cats.Last();
            Assert.AreEqual(CommandResult.Ok, s.DropCat(cat.Id, col + 0.5, row + 0.5).Result);
            Assert.AreEqual((col, row), Cr(s, cat.Cell), "котик в (" + col + "," + row + ")");
            Assert.AreEqual(s.Layout.Objects[target].Index, cat.TargetIndex, "котик бьёт нужную цель");
            return cat;
        }

        [Test]
        public void SideTargetIsTakenWithoutMoving()
        {
            var s = NewDebug();
            s.DebugCompleteTarget(OrdAt(s, 3, 4));
            int up = OrdAt(s, 3, 3), left = OrdAt(s, 2, 4);
            // (3, 4) освобождён: рядом камни (3, 3) сверху и (2, 4) слева; при равенстве — меньший row, то есть (3, 3).
            var cat = CatAt(s, 3, 4, up);
            s.Events.Clear();
            s.DebugCompleteTarget(up);
            Assert.AreEqual((3, 4), Cr(s, cat.Cell), "котик остался на месте");
            Assert.AreEqual(s.Layout.Objects[left].Index, cat.TargetIndex, "котик сам взялся за соседний камень (2, 4)");
            Assert.AreEqual(0L, cat.PhaseTicks, "новое назначение — фаза с нуля (ТЗ 7.5)");
            Assert.IsTrue(s.Events.Any(e => e.Type == SimEventType.CatAssigned && e.CatId == cat.Id), "событие назначения");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.CatMoved && e.CatId == cat.Id), "без шага");
        }

        [Test]
        public void StaysInPlaceWhenNothingInReach()
        {
            var s = NewDebug();
            int rock = OrdAt(s, 2, 4);
            var cat = CatAt(s, 2, 5, rock);
            s.Events.Clear();
            s.DebugCompleteTarget(rock);
            // У (2, 5) других целей нет: котик не шагает на место разбитого камня, а стоит без дела.
            Assert.AreEqual((2, 5), Cr(s, cat.Cell), "котик остался на своей клетке");
            Assert.AreEqual(-1, cat.TargetIndex, "рядом целей нет — Idle");
            Assert.IsTrue(s.IsCellFree(s.Layout.CellIndex(2, 4)), "клетка разбитого камня свободна");
            Assert.IsFalse(s.Events.Any(e => e.Type == SimEventType.CatMoved && e.CatId == cat.Id), "без шага");

            // Котик ждёт игрока: время идёт, а он не двигается и ничего не бьёт.
            s.Advance(30 * GameSession.TicksPerSecond);
            Assert.AreEqual((2, 5), Cr(s, cat.Cell));
            Assert.AreEqual(-1, cat.TargetIndex);
        }

        [Test]
        public void RealDamageBreaksRockAndCatKeepsMining()
        {
            var s = NewDebug();
            s.DebugCompleteTarget(OrdAt(s, 3, 4));
            int rock = OrdAt(s, 3, 3), side = OrdAt(s, 2, 4);
            // (3, 4) освобождён: котик там достаёт камни (3, 3) сверху и (2, 4) слева.
            var cat = CatAt(s, 3, 4, rock);
            long guard = 0;
            while (s.Mine.Objects[rock].Status == ObjectStatus.Intact)
            {
                s.Advance(GameSession.TicksPerSecond);
                Assert.Less(++guard, 100000L, "камень разбит");
            }
            Assert.AreEqual((3, 4), Cr(s, cat.Cell), "котик не сходит со своей клетки");
            Assert.AreEqual(s.Layout.Objects[side].Index, cat.TargetIndex, "после разрушения бьёт соседний камень, который достаёт");
            var hp = s.Mine.Objects[side].Hp;
            s.Advance(10 * GameSession.TicksPerSecond);
            Assert.IsTrue(s.Mine.Objects[side].Hp < hp, "следующий камень уже получает удары");
        }

        [Test]
        public void OfflineAndClassicRulesLeaveCatIdle()
        {
            var off = NewDebug();
            int rock = OrdAt(off, 2, 4);
            var cat = CatAt(off, 2, 5, rock);
            off.OfflineMode = true;
            off.DebugCompleteTarget(rock);
            Assert.AreEqual(-1, cat.TargetIndex, "офлайн новых назначений нет (ТЗ 12.3)");
            Assert.AreEqual((2, 5), Cr(off, cat.Cell));

            var classic = NewDebug(false);
            var cat2 = CatAt(classic, 2, 5, OrdAt(classic, 2, 4));
            classic.DebugCompleteTarget(OrdAt(classic, 2, 4));
            Assert.AreEqual(-1, cat2.TargetIndex, "без AutoMining — прежнее правило: Idle");
        }

        [Test]
        public void DraggedCatIsNotReassigned()
        {
            var s = NewDebug();
            int rock = OrdAt(s, 2, 4);
            var cat = CatAt(s, 2, 5, rock);
            s.BeginDrag(cat.Id);
            s.DebugCompleteTarget(rock);
            Assert.AreEqual(-1, cat.TargetIndex, "котик в переносе сам цель не выбирает");
            Assert.AreEqual((2, 5), Cr(s, cat.Cell));
        }
    }
}
