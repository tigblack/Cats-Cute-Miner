using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Зона мержа (ТЗ 6.4, 6.6): ширина поля всегда 7 клеток, перед игроком всегда два соседних ряда во всю ширину без
    /// объектов — на старте над кузницей (row_c − 3, row_c − 2), после открытия ворот — над ними (row_p − 2, row_p − 1).
    /// </summary>
    public class MergeZoneTests
    {
        const string G2 = "17.1 группа 2 Геометрия";

        static BalanceRepository B => TestData.Balance;

        [Test, Category(G2)]
        public void AllZones_WidthIsSeven()
        {
            foreach (var zone in B.Zones)
                Assert.AreEqual(ZoneLayout.MaxWidth, zone.WidthCells, zone.Id + ": ширина поля");
        }

        [Test, Category(G2)]
        public void AllZones_StartPadHasMergeRowsRightAboveForge()
        {
            foreach (var zone in B.Zones)
            {
                var l = ZoneLayout.Build(B, zone);
                l.StartPadRows(out int top, out int bottom);
                Assert.IsTrue(l.TryMergeRows(top, bottom, out int upper, out int lower), zone.Id + ": нет зоны мержа на старте");
                Assert.AreEqual(l.RowC - 3, upper, zone.Id + ": верхний ряд зоны мержа");
                Assert.AreEqual(l.RowC - 2, lower, zone.Id + ": нижний ряд зоны мержа");
                for (int c = 0; c < l.Width; c++)
                {
                    Assert.Less(l.CellOwner[l.CellIndex(c, upper)], 0, zone.Id + ": клетка (" + c + "," + upper + ") занята");
                    Assert.Less(l.CellOwner[l.CellIndex(c, lower)], 0, zone.Id + ": клетка (" + c + "," + lower + ") занята");
                }
            }
        }

        [Test, Category(G2)]
        public void AllZones_EveryGateLandingHasMergeRowsRightAboveGate()
        {
            int gates = 0;
            foreach (var zone in B.Zones)
            {
                var l = ZoneLayout.Build(B, zone);
                for (int k = 0; k < l.Gates.Count; k++)
                {
                    gates++;
                    l.GatePadRows(k, out int top, out int bottom);
                    Assert.IsTrue(l.TryMergeRows(top, bottom, out int upper, out int lower), zone.Id + ": нет зоны мержа над воротами " + k);
                    Assert.AreEqual(l.Gates[k].Row - 1, lower, zone.Id + ": зона мержа прилегает к воротам " + k);
                    Assert.AreEqual(l.Gates[k].Row - 2, upper, zone.Id + ": верхний ряд зоны мержа над воротами " + k);
                }
            }
            Assert.AreEqual(280, gates, "все ворота проверены");
        }

        [Test, Category(G2)]
        public void Session_MergeRowsFollowOpenedGates()
        {
            var s = GameSession.NewGame(B, 12345UL, SaveSlots.Debug, 1_000_000L, 5);
            Assert.IsTrue(s.MergeRows(out int upper, out int lower));
            Assert.AreEqual(s.Layout.RowC - 3, upper);
            Assert.AreEqual(s.Layout.RowC - 2, lower);

            var gate = s.Layout.Gates[0];
            s.DebugCompleteTarget(gate.Ordinal);
            Assert.AreEqual(CommandResult.Ok, s.OpenGate(gate.Ordinal));
            Assert.IsTrue(s.MergeRows(out upper, out lower));
            Assert.AreEqual(gate.Row - 2, upper, "после ворот зона мержа — над ними");
            Assert.AreEqual(gate.Row - 1, lower);
        }

        [Test, Category(G2)]
        public void Validator_ReportsNoMergeZoneErrorsOnSourceData()
        {
            var report = BalanceValidator.Validate(B);
            var merge = report.Issues.Where(i => i.Message.Contains("зона мержа") || i.Path.EndsWith(".WidthCells")).ToList();
            Assert.IsEmpty(merge, string.Join("\n", merge));
        }

        [Test, Category(G2)]
        public void Layout_RockInMergeRowBreaksMergeZone()
        {
            var src = B.Zone(1);
            var tokens = src.Grid.Split(',');
            var l0 = ZoneLayout.Build(B, src);
            int upperRow = l0.RowC - 3;
            // Камень в верхнем ряду зоны мержа: два соседних свободных ряда на стартовой площадке не остаются.
            tokens[l0.CellIndex(0, upperRow)] = "r:rockbasic:1";
            var broken = new ZoneDto { Id = src.Id, WidthCells = src.WidthCells, DepthCells = src.DepthCells, Grid = string.Join(",", tokens) };
            var l = ZoneLayout.Build(B, broken);
            Assert.IsTrue(l.IsValid, string.Join("; ", l.Issues));
            l.StartPadRows(out int top, out int bottom);
            Assert.IsFalse(l.TryMergeRows(top, bottom, out _, out _), "зона мержа должна пропасть");
        }
    }
}
