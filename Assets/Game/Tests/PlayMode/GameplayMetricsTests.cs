using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CatMiners.Core.Outpost;
using CatMiners.Runtime.Integrations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    public sealed class GameplayMetricsTests
    {
        sealed class Pending
        {
            public GameEvent Kind;
            public int Recipients;
            public JObject Data;
        }
        static List<Pending> Events(GameSdkRuntime runtime)
        {
            var result = new List<Pending>();
            var queue = (IEnumerable)typeof(GameSdkRuntime).GetField("telemetry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            foreach (var item in queue)
            {
                var type = item.GetType();
                result.Add(new Pending {
                    Kind = (GameEvent)type.GetField("Kind").GetValue(item),
                    Recipients = (int)type.GetField("Recipients").GetValue(item),
                    Data = JObject.Parse(JsonConvert.SerializeObject(type.GetField("Data").GetValue(item)))
                });
            }
            return result;
        }
        static void WithRuntime(Action<GameSdkRuntime> check, IntegrationPrivacy privacy = null)
        {
            Assert.IsNull(GameSdkRuntime.Instance);
            var runtime = GameSdkRuntime.Begin(privacy ?? new IntegrationPrivacy());
            try { check(runtime); }
            finally { UnityEngine.Object.DestroyImmediate(runtime.gameObject); }
        }
        static void Sample(GameSdkRuntime runtime, double now, bool playing) =>
            typeof(GameSdkRuntime).GetMethod("SampleGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, new object[] { now, playing });

        [Test]
        public void CompletedForegroundMinutesAreCountedAsMinutesWithoutDoubleCountingLegacyEvent()
        {
            WithRuntime(runtime => {
                Sample(runtime, 0, true);
                Sample(runtime, 59.9, true);
                Assert.IsFalse(Events(runtime).Any(e => e.Kind == GameEvent.PlaytimeMinutes));
                Sample(runtime, 180.5, true);
                var minutes = Events(runtime).Where(e => e.Kind == GameEvent.PlaytimeMinutes).ToArray();
                Assert.AreEqual(3, minutes.Length);
                foreach (var e in minutes)
                {
                    Assert.AreEqual(1, (int)e.Data["minutes"]);
                    Assert.AreEqual(JTokenType.Integer, e.Data["minutes"].Type);
                    Assert.AreEqual(60, (int)e.Data["seconds"]);
                    Assert.AreEqual(1, e.Recipients);
                }
                Assert.IsFalse(Events(runtime).Any(e => e.Kind == GameEvent.TimeSpent));
                Assert.AreEqual("playtime_minutes", GameSdkRuntime.EventName(GameEvent.PlaytimeMinutes));
            });
        }

        [Test]
        public void BackgroundTimeIsExcludedAndPartialGameplayMinuteSurvivesPause()
        {
            WithRuntime(runtime => {
                Sample(runtime, 0, true);
                Sample(runtime, 40, true);
                Sample(runtime, 40, false);
                Sample(runtime, 1000, false);
                Sample(runtime, 1000, true);
                Sample(runtime, 1019, true);
                Assert.IsFalse(Events(runtime).Any(e => e.Kind == GameEvent.PlaytimeMinutes));
                Sample(runtime, 1020, true);
                Assert.AreEqual(1, Events(runtime).Count(e => e.Kind == GameEvent.PlaytimeMinutes));
            });
        }

        [Test]
        public void MinuteMetricDoesNotBreakOptionalAdjustSecondsBinding()
        {
            WithRuntime(runtime => {
                Sample(runtime, 0, true);
                Sample(runtime, 60, true);
                var events = Events(runtime);
                Assert.AreEqual(1, events.Single(e => e.Kind == GameEvent.PlaytimeMinutes).Recipients);
                var legacy = events.Single(e => e.Kind == GameEvent.TimeSpent);
                Assert.AreEqual(2, legacy.Recipients);
                Assert.AreEqual("60", (string)legacy.Data["value"]);
            }, new IntegrationPrivacy { Age = NeutralAge.Age16OrOlder, ChoicesMade = true, Adjust = true });
        }

        [Test]
        public void ChestSourcesReuseRegisteredOptionalAdjustBindingWithoutSharingItsMinuteEvent()
        {
            WithRuntime(runtime => {
                runtime.Config.adjustPurchaseEventToken = "";
                runtime.Config.adjustEvents = new[] {
                    new GameSdkConfiguration.AdjustEventBinding { name = "chest_opened", token = "abc123" }
                };
                var lookup = typeof(GameSdkRuntime).GetMethod("AdjustEventToken", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var kind in new[] { GameEvent.StoreChestOpened, GameEvent.ArenaChestOpened })
                    Assert.AreEqual("abc123", lookup.Invoke(runtime, new object[] { kind }));
                Assert.IsNull(lookup.Invoke(runtime, new object[] { GameEvent.PlaytimeMinutes }));
            });
        }

        [TestCase(1)]
        [TestCase(5)]
        public void CardUpgradeCountsOneUpgradeAndPreservesCardAndLevels(int newLevel)
        {
            WithRuntime(runtime => {
                var action = OutpostEvent.Create(OutpostEventType.CardUpgraded, 10);
                action.ItemId = "ca002"; action.Count = newLevel; action.Value = 40;
                runtime.GameAction(action, 3);
                var e = Events(runtime).Single(x => x.Kind == GameEvent.CardUpgraded);
                Assert.AreEqual(1, (int)e.Data["count"]);
                Assert.AreEqual("ca002", (string)e.Data["card_id"]);
                Assert.AreEqual(newLevel - 1, (int)e.Data["from_level"]);
                Assert.AreEqual(newLevel, (int)e.Data["to_level"]);
                Assert.AreEqual(3, (int)e.Data["zone"]);
                Assert.AreEqual("40", (string)e.Data["elixir_spent"]);
                Assert.AreEqual(JTokenType.Integer, e.Data["count"].Type);
            });
        }

        [Test]
        public void SellAllCountsUnitsPerGoodRatherThanGoldOrNumberOfSaleEvents()
        {
            WithRuntime(runtime => {
                foreach (var item in new[] { ("milk", 20, 200), ("wood", 7, 1400) })
                {
                    var action = OutpostEvent.Create(OutpostEventType.GoodsSold, 10);
                    action.ItemId = item.Item1; action.Count = item.Item2;
                    action.Quantity = item.Item2; action.Value = item.Item3;
                    runtime.GameAction(action, 3);
                }
                var sales = Events(runtime).Where(x => x.Kind == GameEvent.GoodsSold).ToArray();
                Assert.AreEqual(2, sales.Length);
                Assert.AreEqual(27, sales.Sum(x => (long)x.Data["quantity"]));
                Assert.AreEqual("milk", (string)sales[0].Data["good_id"]);
                Assert.AreEqual("200", (string)sales[0].Data["gold_earned"]);
                Assert.AreEqual(JTokenType.Integer, sales[0].Data["quantity"].Type);
                Assert.AreEqual(2, Events(runtime).Count(x => x.Kind == GameEvent.CurrencyEarned));
            });
        }

        [Test]
        public void LargeSaleRetainsLongQuantityInsteadOfTruncatedUiCount()
        {
            WithRuntime(runtime => {
                long quantity = (long)int.MaxValue + 100;
                var action = OutpostEvent.Create(OutpostEventType.GoodsSold, 10);
                action.ItemId = "milk"; action.Count = int.MaxValue;
                action.Quantity = quantity; action.Value = (BigInteger)quantity * 10;
                runtime.GameAction(action, 2);
                Assert.AreEqual(quantity, (long)Events(runtime).Single(x => x.Kind == GameEvent.GoodsSold).Data["quantity"]);
            });
        }

        [TestCase(OutpostIds.StoreSource, GameEvent.StoreChestOpened, "store")]
        [TestCase(42, GameEvent.ArenaChestOpened, "arena")]
        [TestCase(-1, GameEvent.ArenaChestOpened, "arena")]
        public void ActualChestOpeningCountsOneChestNotItsCardCopies(int target, GameEvent expected, string source)
        {
            WithRuntime(runtime => {
                var action = OutpostEvent.Create(OutpostEventType.ChestOpened, 10);
                action.ItemId = "scg_m002_s01"; action.TargetId = target;
                action.Count = 12; action.Extra = 2; action.Value = 300;
                runtime.GameAction(action, 2);
                var e = Events(runtime).Single(x => x.Kind == expected);
                Assert.AreEqual(1, (int)e.Data["count"]);
                Assert.AreEqual(source, (string)e.Data["source"]);
                Assert.AreEqual(12, (int)e.Data["card_copies"]);
                Assert.AreEqual(2, (int)e.Data["new_cards"]);
                Assert.AreEqual("online", (string)e.Data["mode"]);
                Assert.AreEqual(2, (int)e.Data["zone"]);
                Assert.IsFalse(Events(runtime).Any(x => x.Kind == GameEvent.ChestOpened || x.Kind == GameEvent.CardUpgraded));
            });
        }

        [Test]
        public void TutorialCardDoesNotInflateChestOpeningOrCardUpgradeMetrics()
        {
            WithRuntime(runtime => {
                var action = OutpostEvent.Create(OutpostEventType.ChestOpened, 10);
                action.ItemId = "tutorial"; action.Count = 1;
                runtime.GameAction(action, 1);
                CollectionAssert.AreEqual(new[] { GameEvent.SessionStarted, GameEvent.TutorialCardGranted }, Events(runtime).Select(x => x.Kind));
            });
        }

        [TestCase(0, "offline")]
        [TestCase(10, "time_skip")]
        public void CommittedOfflineChestTotalsAreAggregatedWithoutManufacturingPlaytime(int skipMinutes, string mode)
        {
            WithRuntime(runtime => {
                runtime.OfflineProgress(new OfflineSummary { ChestsOpened = 4, CardsGained = 25, SkipMinutes = skipMinutes }, 3);
                var e = Events(runtime).Single(x => x.Kind == GameEvent.ArenaChestOpened);
                Assert.AreEqual(4, (int)e.Data["count"]);
                Assert.AreEqual("arena", (string)e.Data["source"]);
                Assert.AreEqual(mode, (string)e.Data["mode"]);
                Assert.IsFalse(Events(runtime).Any(x => x.Kind == GameEvent.PlaytimeMinutes || x.Kind == GameEvent.StoreChestOpened));
                runtime.OfflineProgress(new OfflineSummary(), 3);
                Assert.AreEqual(1, Events(runtime).Count(x => x.Kind == GameEvent.ArenaChestOpened));
            });
        }

        [Test]
        public void SavedOptOutBlocksAllNewMetrics()
        {
            WithRuntime(runtime => {
                Sample(runtime, 0, true); Sample(runtime, 60, true);
                foreach (var type in new[] { OutpostEventType.CardUpgraded, OutpostEventType.GoodsSold, OutpostEventType.ChestOpened })
                {
                    var e = OutpostEvent.Create(type, 10);
                    e.ItemId = "fixture"; e.Count = 2; e.Quantity = 2; e.Value = 50;
                    runtime.GameAction(e, 3);
                }
                runtime.OfflineProgress(new OfflineSummary { ChestsOpened = 3 }, 3);
                Assert.IsEmpty(Events(runtime));
            }, new IntegrationPrivacy { AppMetricaDisabled = true });
        }
    }
}
