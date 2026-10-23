using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public class BalanceLoadTests
    {
        [Test]
        public void HashSizeAndCounts()
        {
            var b = TestData.Balance;
            Assert.AreEqual(BalanceRepository.ExpectedSha256, b.Sha256);
            Assert.AreEqual(2254970L, b.SizeBytes);
            Assert.AreEqual("evergreen", b.Root.Id);
            Assert.AreEqual(144, b.ZoneCount);
            Assert.AreEqual(26, b.Rocks.Count);
            Assert.AreEqual(20, b.Shafts.Count);
            Assert.AreEqual(20, b.Obstructions.Count);
            Assert.AreEqual(48, b.Root.MiningTargetHealths.Count);
            Assert.AreEqual(21, b.Objectives.Count);
            Assert.AreEqual(61, b.Cards.Count);
            Assert.AreEqual(40, b.CardsInOrder.Count(c => c.IsManager));
            Assert.AreEqual(58, b.Gacha.Count);
            Assert.AreEqual(21, b.Deliveries.Count);
            Assert.AreEqual(377, b.FreeGachaCycle.GachaIds.Length);
        }

        [Test]
        public void ZonesInCampaignOrder()
        {
            var b = TestData.Balance;
            for (int n = 1; n <= 144; n++)
                Assert.AreEqual("zone" + n.ToString(CultureInfo.InvariantCulture), b.Zone(n).Id);
        }

        [Test]
        public void BigLiteralsAndHpControlValues()
        {
            var b = TestData.Balance;
            var rockbasic = b.Root.MiningTargetHealths["rockbasic"];
            Assert.AreEqual(1.27e19, rockbasic[73], 1.27e19 * 1e-15);
            Assert.AreEqual(1.68e20, b.Shafts["heliodor"].CurrencyOutputMultiplier, 1.68e20 * 1e-15);
            foreach (var t in b.Root.MiningTargetHealths.Values)
            {
                Assert.AreEqual(99, t.Length);
                foreach (var v in t) Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v));
            }
            Assert.AreEqual(16.0, b.TargetHp("rockonboardingmine1", 1).ToDouble());
            Assert.AreEqual(60.0, b.TargetHp("rockonboardingmine1", 2).ToDouble());
            Assert.AreEqual(200.0, b.TargetHp("rockbasic", 3).ToDouble());
            Assert.AreEqual(64.0, b.TargetHp("amethyst", 1).ToDouble());
            Assert.AreEqual(20800.0, b.TargetHp("checkpoint", 5).ToDouble());
            Assert.AreEqual(100.0, b.TargetHp("exit", 1).ToDouble());
            Assert.AreEqual(1.87e25, b.TargetHp("sunstone", 99).ToDouble(), 1e10);
            Assert.Throws<BalanceLoadException>(() => b.TargetHp("rockbasic", 100));
            Assert.Throws<BalanceLoadException>(() => b.TargetHp("nosuchtarget", 1));
        }

        [Test]
        public void WrongHashIsRejected()
        {
            var bytes = TestData.BalanceBytes;
            var copy = (byte[])bytes.Clone();
            copy[copy.Length - 2] = (byte)' ';
            Assert.Throws<BalanceLoadException>(() => BalanceRepository.Load(copy));
        }

        [Test]
        public void CardMaxLevels()
        {
            var b = TestData.Balance;
            Assert.AreEqual(14, b.CardMaxLevel(Rarity.Common));
            Assert.AreEqual(12, b.CardMaxLevel(Rarity.Uncommon));
            Assert.AreEqual(9, b.CardMaxLevel(Rarity.Rare));
            Assert.AreEqual(7, b.CardMaxLevel(Rarity.Epic));
            Assert.AreEqual(5, b.CardMaxLevel(Rarity.Legendary));
            Assert.AreEqual(5, b.CardMaxLevel(Rarity.Majestic));
        }
    }

    public class LayoutTests
    {
        [Test]
        public void ValidatorPassesOnAllZones()
        {
            var report = BalanceValidator.Validate(TestData.Balance);
            Assert.AreEqual(0, report.ErrorCount, report.ToText());
            Assert.AreEqual(144, report.Zones.Count);
        }

        [Test]
        public void AmethystFootprintInZone2()
        {
            var layout = ZoneLayout.Build(TestData.Balance, 2);
            var shaft = layout.Shafts.Single();
            Assert.AreEqual("amethyst", shaft.TypeId);
            Assert.AreEqual(0, shaft.Col);
            Assert.AreEqual(5, shaft.Row);
            var cells = shaft.Cells.Select(c => (layout.Col(c), layout.Row(c))).OrderBy(t => t).ToList();
            CollectionAssert.AreEquivalent(new[] { (0, 3), (0, 4), (0, 5), (1, 3), (1, 4), (1, 5) }, cells);
            Assert.AreEqual(1, shaft.Level);
            Assert.AreEqual("scg_m002_s01", shaft.RewardId);
            Assert.AreEqual("ca002", shaft.CardId);
            Assert.AreEqual(1, shaft.CardLevel);
        }

        [Test]
        public void Block2x2InZone1()
        {
            var layout = ZoneLayout.Build(TestData.Balance, 1);
            var block = layout.Objects.First(o => o.TypeId == "block2x2");
            Assert.AreEqual((0, 1), (block.Col, block.Row));
            var cells = block.Cells.Select(c => (layout.Col(c), layout.Row(c))).OrderBy(t => t).ToList();
            CollectionAssert.AreEquivalent(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, cells);
        }

        [Test]
        public void SpecialObjectGeometry()
        {
            for (int n = 1; n <= 144; n++)
            {
                var layout = ZoneLayout.Build(TestData.Balance, n);
                Assert.IsTrue(layout.IsValid, layout.ZoneId);
                Assert.AreEqual((2, 0), (layout.Exit.Col, layout.Exit.Row), layout.ZoneId);
                Assert.AreEqual(3, layout.Exit.Cells.Length);
                Assert.AreEqual(1, layout.Cart.Col);
                Assert.IsTrue(layout.RowC == layout.Depth - 4 || layout.RowC == layout.Depth - 3, layout.ZoneId);
                Assert.AreEqual(layout.RowC + 1, layout.Cart.Row, layout.ZoneId + ": опора кузницы 2×3 — строкой ниже токена");
                Assert.AreEqual(6, layout.Cart.Cells.Length);
                foreach (var g in layout.Gates)
                {
                    Assert.AreEqual(1, g.Col);
                    Assert.AreEqual(5, g.Cells.Length);
                    Assert.AreEqual(-1, layout.SectorOfRow(g.Row));
                    for (int dr = 1; dr <= 3; dr++)
                        for (int c = 0; c < layout.Width; c++)
                            Assert.AreEqual(-1, layout.CellOwner[layout.CellIndex(c, g.Row - dr)], layout.ZoneId + " площадка над воротами");
                }
            }
        }

        [Test]
        public void MatchesZonesSummaryCsv()
        {
            var b = TestData.Balance;
            var report = BalanceValidator.Validate(b);
            var lines = File.ReadAllLines(TestData.ZonesSummaryPath).Skip(1).Where(l => l.Length > 0).ToList();
            Assert.AreEqual(144, lines.Count);
            foreach (var line in lines)
            {
                var f = line.Split(',');
                var s = report.Zones.Single(z => z.ZoneId == f[0]);
                string ctx = f[0];
                Assert.AreEqual(int.Parse(f[1]), s.Width, ctx);
                Assert.AreEqual(int.Parse(f[2]), s.Depth, ctx);
                Assert.AreEqual(int.Parse(f[3]), s.Rocks, ctx);
                Assert.AreEqual(int.Parse(f[4]), s.Shafts, ctx);
                Assert.AreEqual(int.Parse(f[5]), s.Gates, ctx);
                Assert.AreEqual(int.Parse(f[6]), s.Obstructions, ctx);
                Assert.AreEqual(int.Parse(f[7]), s.OccupiedRsx, ctx);
                Assert.AreEqual(f[8], s.ExitCol + ";" + s.ExitRow, ctx);
                Assert.AreEqual(int.Parse(f[9]), s.ExitLevel, ctx);
                Assert.AreEqual(f[10], s.ExitGacha, ctx);
                Assert.AreEqual(f[11], s.CartCol + ";" + s.CartRow, ctx);
                Assert.AreEqual(f[12], string.Join(";", s.GateRows), ctx);
                Assert.AreEqual(f[13], string.Join(";", s.GateLevels), ctx);
                Assert.AreEqual(int.Parse(f[14]), s.SectorCount, ctx);
                Assert.AreEqual(f[15], string.Join(" ", s.ShaftSpecs), ctx);
            }
        }

        [Test]
        public void AppendixAFirstZones()
        {
            var report = BalanceValidator.Validate(TestData.Balance);
            int[,] expected =
            {
                // depth, r, s, p, x
                { 12, 8, 0, 0, 12 }, { 13, 13, 1, 0, 15 }, { 16, 20, 2, 0, 11 }, { 18, 28, 3, 0, 14 }, { 26, 38, 4, 1, 19 },
                { 30, 50, 4, 1, 22 }, { 18, 33, 4, 0, 14 }, { 30, 56, 4, 1, 19 }, { 30, 66, 5, 1, 21 }, { 38, 75, 5, 2, 27 },
            };
            for (int i = 0; i < 10; i++)
            {
                var z = report.Zones[i];
                Assert.AreEqual(expected[i, 0], z.Depth, z.ZoneId);
                Assert.AreEqual(expected[i, 1], z.Rocks, z.ZoneId);
                Assert.AreEqual(expected[i, 2], z.Shafts, z.ZoneId);
                Assert.AreEqual(expected[i, 3], z.Gates, z.ZoneId);
                Assert.AreEqual(expected[i, 4], z.Obstructions, z.ZoneId);
            }
        }

        [Test]
        public void UnknownTokenIsAnErrorNotARock()
        {
            var b = TestData.Balance;
            var zone = b.Zone(1);
            var broken = new ZoneDto
            {
                Id = "zone1",
                WidthCells = zone.WidthCells,
                DepthCells = zone.DepthCells,
                RankMultipliers = zone.RankMultipliers,
                Grid = zone.Grid.Replace("r:rockonboardingmine1:2", "q:mystery:1"),
            };
            var layout = ZoneLayout.Build(b, broken);
            Assert.IsFalse(layout.IsValid);
            Assert.IsTrue(layout.Issues.Any(i => i.Message.Contains("неизвестный токен")));
            Assert.IsFalse(layout.Objects.Any(o => o.Token.StartsWith("q:", StringComparison.Ordinal)));
        }
    }
}
