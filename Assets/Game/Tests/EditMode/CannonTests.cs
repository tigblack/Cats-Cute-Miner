using System;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Кузница 2×3 и пушка (ТЗ 6.4, 7.2). Кузница во всех зонах занимает (1..2, row_c − 1 … row_c + 1): токен — в строке
    /// row_c, опора — строкой ниже; зона мержа над ней не меняется. Пушка стоит у правого края в строках row_c − 1 … row_c;
    /// её клетки закреплены за кузницей: туда не появляются и не ставятся котики и контейнеры. Старое сохранение с котиком
    /// или бочкой на этих клетках при загрузке переносит их в клетку появления.
    /// </summary>
    public class CannonTests
    {
        const string G2 = "17.1 группа 2 Геометрия";

        static BalanceRepository B => TestData.Balance;

        [Test, Category(G2)]
        public void AllZones_ForgeIsTwoByThreeAndCannonStandsAtRightEdge()
        {
            foreach (var zone in B.Zones)
            {
                var l = ZoneLayout.Build(B, zone);
                Assert.IsTrue(l.IsValid, zone.Id + ": " + string.Join("; ", l.Issues));
                int rc = l.RowC;
                Assert.AreEqual(l.Cart.Index / l.Width, rc, zone.Id + ": row_c — строка токена кузницы");
                Assert.AreEqual(rc + 1, l.Cart.Row, zone.Id + ": опора кузницы 2×3 — строкой ниже токена");
                Assert.AreEqual((2, 3), (l.Cart.Width, l.Cart.Depth), zone.Id + ": кузница 2×3");
                Assert.AreEqual(6, l.Cart.Cells.Length, zone.Id);
                for (int r = rc - 1; r <= rc + 1; r++)
                    for (int c = 1; c <= 2; c++)
                        Assert.AreEqual(l.Cart.Ordinal, l.CellOwner[l.CellIndex(c, r)], zone.Id + ": клетка кузницы (" + c + "," + r + ")");

                Assert.IsTrue(l.HasCannon, zone.Id + ": у правого края есть место для пушки");
                Assert.AreEqual(l.Width - 1, l.CannonCol, zone.Id + ": пушка у правого края");
                Assert.AreEqual(rc, l.CannonRow, zone.Id + ": нижняя строка пушки — row_c");
                for (int dz = 0; dz < ZoneLayout.CannonDepth; dz++)
                {
                    int cell = l.CellIndex(l.CannonCol, l.CannonRow - dz);
                    Assert.IsTrue(l.IsCannonCell(cell), zone.Id);
                    Assert.AreEqual(l.Cart.Ordinal, l.CellOwner[cell], zone.Id + ": клетка пушки закреплена за кузницей");
                    Assert.AreEqual(0, l.CellSector[cell], zone.Id + ": пушка в стартовом секторе");
                }
                l.StartPadRows(out int top, out int bottom);
                Assert.AreEqual((rc - 3, Math.Min(l.Depth - 1, rc + 1)), (top, bottom), zone.Id + ": стартовая площадка — от row_c");
                Assert.IsTrue(l.TryMergeRows(top, bottom, out int upper, out int lower), zone.Id + ": зона мержа на месте");
                Assert.AreEqual((rc - 3, rc - 2), (upper, lower), zone.Id + ": зона мержа прямо над кузницей и пушкой");
            }
        }

        [Test, Category(G2)]
        public void CannonCells_TakeNoCatsOrContainers()
        {
            var s = GameSession.NewGame(B, 12345UL, SaveSlots.Debug, 1_000_000L, 1);
            var l = s.Layout;
            int low = l.CellIndex(l.CannonCol, l.CannonRow), high = l.CellIndex(l.CannonCol, l.CannonRow - 1);
            Assert.AreEqual(l.Cart.Ordinal, s.ObjectAt(low), "клетка пушки занята лагерем кузницы");
            Assert.IsFalse(s.IsCellFree(low));
            Assert.IsFalse(s.IsCellFree(high));
            int guard = 0;
            while (s.DebugSpawnCat(1)) Assert.Less(++guard, 1000);
            Assert.IsFalse(s.Mine.Cats.Any(c => l.IsCannonCell(c.Cell)), "котики не появляются под пушкой");
            // Перенос на пушку — как на кузницу: отказ, котик остаётся на месте.
            var cat = s.Mine.Cats[0];
            int origin = cat.Cell;
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, l.CannonCol + 0.5, l.CannonRow + 0.5).Result);
            Assert.AreNotEqual(CommandResult.Ok, s.DropCat(cat.Id, l.CannonCol + 0.5, l.CannonRow - 0.5).Result);
            Assert.AreEqual(origin, cat.Cell, "котик остался в своей клетке");
        }

        [Test]
        public void LegacySave_CatAndBarrelOnCannonMoveToSpawnCells()
        {
            var s = GameSession.NewGame(B, 12345UL, SaveSlots.Debug, 1_000_000L, 1);
            s.BarrelHires = true;
            Assert.IsTrue(s.DebugSpawnCat(1));
            Assert.AreEqual(CommandResult.Ok, s.Hire());
            var l = s.Layout;
            var cat = s.Mine.Cats[0];
            var box = s.Mine.Containers[0];
            // Так могло быть до пушки: котик и бочка на клетках, которые теперь под ней.
            cat.Cell = l.CellIndex(l.CannonCol, l.CannonRow);
            box.Cell = l.CellIndex(l.CannonCol, l.CannonRow - 1);

            var loaded = GameSession.FromState(B, s.State);
            var movedCat = loaded.FindCat(cat.Id);
            Assert.IsFalse(l.IsCannonCell(movedCat.Cell), "котик ушёл из-под пушки");
            Assert.AreSame(movedCat, loaded.CatAt(movedCat.Cell));
            Assert.AreEqual(-1, movedCat.TargetIndex, "перенесённый котик свободен");
            var movedBox = loaded.Mine.Containers[0];
            Assert.IsFalse(l.IsCannonCell(movedBox.Cell), "бочка ушла из-под пушки");
            Assert.AreSame(movedBox, loaded.ContainerAt(movedBox.Cell));
            Assert.AreNotEqual(movedCat.Cell, movedBox.Cell);
            Assert.IsTrue(loaded.MergeRows(out int upper, out int lower));
            Assert.That(l.Row(movedCat.Cell), Is.InRange(upper, lower), "котик — в зоне мержа");
        }
    }
}
