using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using NUnit.Framework;
using static CatMiners.Tests.Outpost.OutpostTestHelpersB;

namespace CatMiners.Tests.Outpost
{
    /// <summary>
    /// Бонус за рекламу у левого края и мортира с динамитом (ТЗ 10.6, store_catalog.json → RewardedBonus): взвешенный выбор вида
    /// среди доступных, суммы по ценам игры (золото — две самые дешёвые покупки улучшений, эликсир — самое дешёвое улучшение
    /// карточки или минимум, гемы — 2), начисление мимо учёта доставки, выстрел мортиры к доступным кирке целям, взрыв только
    /// по ним в радиусе 1,5 (цель мортиры — вся прочность, кольцо — половина максимума × карточка ca063), одна бочка на поле,
    /// сохранение бочки, однократная доставка награды за рекламу.
    /// </summary>
    public class OutpostRewardedBonusTests
    {
        static OutpostConfigSet s_Configs;
        static OutpostConfigSet BonusConfigs => s_Configs ?? (s_Configs = Configs.WithCards(TestData.Balance));
        static RewardedBonusRules Rules => BonusConfigs.Store.RewardedBonus;

        static OutpostSimulation Sim(int zone = 1, System.Action<OutpostProfile> setup = null)
        {
            var map = OutpostLevelAdapter.Build(TestData.Balance, zone, BonusConfigs.Overrides);
            var st = StageProgressionService.NewGame(TestData.Balance, BonusConfigs, Seed, StartUtcMs, zone);
            StoreService.MigrateProfile(st.Profile, BonusConfigs.Store);
            setup?.Invoke(st.Profile);
            var sim = new OutpostSimulation(st, map, BonusConfigs);
            Drain(sim);
            return sim;
        }

        /// <summary>Первая шахта, где в начале доступно кирке не меньше пяти целей (кольцо взрыва есть с кем проверить).</summary>
        static OutpostSimulation DynamiteSim(System.Action<OutpostProfile> setup = null)
        {
            foreach (int zone in new[] { 3, 4, 5, 6, 8, 10, 12, 2, 1 })
            {
                var sim = Sim(zone, setup);
                if (sim.StrikeableTargetCount() >= 5) return sim;
            }
            Assert.Fail("нет шахты с пятью доступными кирке целями");
            return null;
        }

        static bool InRadius(OutpostMap map, MapTarget mt, int cell, Rational radius)
        {
            int bc = map.Col(cell), br = map.Row(cell);
            return mt.Cells.Any(c =>
            {
                long dx = map.Col(c) - bc, dy = map.Row(c) - br;
                return (Rational)(dx * dx + dy * dy) <= radius * radius;
            });
        }

        // ───────────────────────────── Данные ─────────────────────────────

        [Test]
        public void Catalog_RewardedBonusRules_AsAgreedWithOwner()
        {
            Assert.IsNotNull(Rules, "store_catalog.json → RewardedBonus");
            Assert.AreEqual(120, Rules.FirstDelaySec);
            Assert.AreEqual(180, Rules.IntervalSec);
            Assert.AreEqual(45, Rules.VisibleSec);
            Assert.AreEqual(40, Rules.WeightOf(RewardedBonusKind.Gold));
            Assert.AreEqual(25, Rules.WeightOf(RewardedBonusKind.Elixir));
            Assert.AreEqual(15, Rules.WeightOf(RewardedBonusKind.Gems));
            Assert.AreEqual(20, Rules.WeightOf(RewardedBonusKind.Dynamite));
            Assert.AreEqual(2, Rules.GoldUpgrades);
            Assert.AreEqual(1, Rules.ElixirUpgrades);
            Assert.AreEqual(50, Rules.ElixirFloor);
            Assert.AreEqual(2, Rules.Gems);
            Assert.AreEqual(Rational.Of(3, 2), Rules.DynamiteRadius);
            Assert.AreEqual(Rational.One, Rules.DynamiteCenterShare);
            Assert.AreEqual(Rational.Of(1, 2), Rules.DynamiteRingShare);
            Assert.AreEqual(3, Rules.DynamiteMinTargets);
            Assert.Throws<OutpostConfigException>(() => StoreCatalog.Parse(
                System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.ProjectRoot, "Assets/Game/Resources/Outpost/store_catalog.json"))
                    .Replace("\"Gold\": 40", "\"Copper\": 40")), "неизвестный вид награды");
        }

        [Test]
        public void Card_ca063_DemolitionExpert_Rare_DynamitePower()
        {
            var def = BonusConfigs.Cards.Get("ca063");
            Assert.IsNotNull(def, "карточка игры сверх источника (card_rules.json → ExtraCards)");
            Assert.AreEqual(3, def.Rarity);
            Assert.AreEqual(9, def.MaxLevel);
            Assert.AreEqual(1, def.MineUnlock);
            Assert.IsFalse(def.IsManager);
            Assert.AreEqual(CardEffectKind.DynamitePower, def.Effect.Kind);
            Assert.AreEqual(Rational.Of(1, 5), def.EffectAt(1), "Lv1 +20 %");
            Assert.AreEqual(Rational.One, def.EffectAt(9), "Lv9 +100 %");
            Assert.AreEqual(10, BonusConfigs.Cards.OfRarity(3).Count, "пул Rare в сундуках — 10 карточек");
            Assert.AreEqual("Demolition Expert", Loc.T("cards.name.ca063"));
            Assert.AreEqual("Dynamite blast damage: +20%", Loc.F("cards.effect.DynamitePower", "20%"));
            Assert.AreEqual("Dynamite +20%", Loc.F("cards.short.DynamitePower", "20%"), "строка эффекта в окне карточки");
            foreach (var card in BonusConfigs.Cards.Cards)
            {
                string kind = card.Effect.Kind + (card.Effect.TargetKind != null && card.Effect.Kind.ToString().StartsWith("Kind") ? "." + card.Effect.TargetKind : "");
                Assert.IsTrue(Loc.Has("cards.short." + kind), card.Id + ": нет cards.short." + kind);
            }
            Assert.AreEqual(Rational.Of(1, 5), Sim(1, p => p.Cards["ca063"] = new CardProgress { Level = 1 }).Stats.CardDynamitePower);
            Assert.AreEqual(Rational.Zero, Sim(1).Stats.CardDynamitePower, "без карточки — без бонуса");
        }

        // ───────────────────────────── Выбор вида ─────────────────────────────

        [Test]
        public void Pick_WeightedByRoll_SkipsUnavailableKinds()
        {
            RewardedBonusKind Pick(double roll, params RewardedBonusKind[] off)
            {
                Assert.IsTrue(RewardedBonus.Pick(Rules, k => !off.Contains(k), roll, out var kind), "roll " + roll);
                return kind;
            }
            // Веса 40 / 25 / 15 / 20 из 100: [0, 0.4) золото, [0.4, 0.65) эликсир, [0.65, 0.8) гемы, [0.8, 1) динамит.
            Assert.AreEqual(RewardedBonusKind.Gold, Pick(0.0));
            Assert.AreEqual(RewardedBonusKind.Gold, Pick(0.399));
            Assert.AreEqual(RewardedBonusKind.Elixir, Pick(0.4));
            Assert.AreEqual(RewardedBonusKind.Gems, Pick(0.7));
            Assert.AreEqual(RewardedBonusKind.Dynamite, Pick(0.95));
            // Без золота: веса 25 / 15 / 20 из 60.
            Assert.AreEqual(RewardedBonusKind.Elixir, Pick(0.0, RewardedBonusKind.Gold));
            Assert.AreEqual(RewardedBonusKind.Dynamite, Pick(0.99, RewardedBonusKind.Gold));
            Assert.IsFalse(RewardedBonus.Pick(Rules, k => false, 0.5, out _), "ничего не доступно — иконки нет");
            Assert.IsTrue(RewardedBonus.TryParse("dynamite", out var d) && d == RewardedBonusKind.Dynamite);
            Assert.AreEqual("bonus_gems", RewardedBonus.Placement(RewardedBonusKind.Gems));
        }

        // ───────────────────────────── Суммы ─────────────────────────────

        [Test]
        public void Gold_IsTwoCheapestSingleLevelPurchases()
        {
            var sim = Sim(3);
            var run = sim.State.Run;
            // Ожидание — перебором всех пар покупок по одному уровню (одна ветка может войти дважды).
            BigInteger? Next(UpgradeBranch b, int level)
            {
                var levels = (int[])run.Levels.Clone();
                levels[(int)b] = level;
                var probe = new OutpostRun { Balance = run.Balance, Zone = run.Zone, Levels = levels, ExtraWorkers = run.ExtraWorkers, Phase = run.Phase };
                return TeamUpgradeService.Quote(probe, b, BuyMode.One, sim.Stats).NextCost;
            }
            BigInteger? best = null;
            for (int a = 0; a < OutpostIds.BranchCount; a++)
            for (int b = 0; b < OutpostIds.BranchCount; b++)
            {
                var first = Next((UpgradeBranch)a, run.Levels[a]);
                if (first == null) continue;
                var second = a == b ? Next((UpgradeBranch)b, run.Levels[b] + 1) : Next((UpgradeBranch)b, run.Levels[b]);
                if (second == null) continue;
                var sum = first.Value + second.Value;
                if (best == null || sum < best.Value) best = sum;
            }
            Assert.IsTrue(sim.TryQuoteBonus(RewardedBonusKind.Gold, out var gold));
            Assert.AreEqual(best.Value, gold);
            Assert.Greater(gold, BigInteger.Zero);
        }

        [Test]
        public void Elixir_CheapestOwnCardUpgrade_OrFloor_GemsTwo()
        {
            Assert.IsTrue(Sim(1).TryQuoteBonus(RewardedBonusKind.Elixir, out var none));
            Assert.AreEqual(new BigInteger(50), none, "карточек нет — ElixirFloor");
            var sim = Sim(1, p =>
            {
                p.Cards["ca026"] = new CardProgress { Level = 1 };      // Rare: Lv1 → 2 за 500
                p.Cards["ca002"] = new CardProgress { Level = 2 };      // Common: Lv2 → 3 за 100
            });
            Assert.IsTrue(sim.TryQuoteBonus(RewardedBonusKind.Elixir, out var elixir));
            Assert.AreEqual(new BigInteger(BonusConfigs.Cards.Get("ca002").UpgradeElixir[1]), elixir);
            var maxed = Sim(1, p => p.Cards["ca062"] = new CardProgress { Level = BonusConfigs.Cards.Get("ca062").MaxLevel });
            Assert.IsTrue(maxed.TryQuoteBonus(RewardedBonusKind.Elixir, out var floor));
            Assert.AreEqual(new BigInteger(50), floor, "на максимуме улучшать нечего");
            Assert.IsTrue(sim.TryQuoteBonus(RewardedBonusKind.Gems, out var gems));
            Assert.AreEqual(new BigInteger(2), gems);
        }

        // ───────────────────────────── Начисление ─────────────────────────────

        [Test]
        public void RewardBonus_Currencies_WalletBonusCredited_ProfileElixirGems_Events()
        {
            var sim = Sim(1);
            var run = sim.State.Run;
            long elixir0 = sim.State.Profile.Elixir, gems0 = sim.State.Profile.Gems;
            Assert.IsTrue(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gold, 1234)).Ok);
            Assert.AreEqual(new BigInteger(1234), run.Wallet);
            Assert.AreEqual(new BigInteger(1234), run.BonusCredited);
            Assert.AreEqual(BigInteger.Zero, run.TotalCredited, "не доставка");
            var granted = Drain(sim).Single(e => e.Type == OutpostEventType.BonusGranted);
            Assert.AreEqual("gold", granted.ItemId);
            Assert.AreEqual(new BigInteger(1234), granted.Value);
            Assert.IsTrue(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Elixir, 75)).Ok);
            Assert.IsTrue(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gems, 2)).Ok);
            Assert.AreEqual(elixir0 + 75, sim.State.Profile.Elixir);
            Assert.AreEqual(gems0 + 2, sim.State.Profile.Gems);
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Gems, 0)).Reason);
            Assert.AreEqual("unknown", sim.Submit(new OutpostCommand { Kind = CommandKind.RewardBonus, ItemId = "copper", Amount = 5 }).Reason);
            AssertInvariants(sim, "после наград");
        }

        [Test]
        public void VerifiedReward_Bonus_DeliveredOnce()
        {
            var sim = Sim(1);
            long gems0 = sim.State.Profile.Gems;
            var offer = new RewardedOffer { Id = "reward_test", RunId = sim.State.Run.RunId, Bonus = "gems", Amount = 2, Earned = true };
            sim.State.Profile.PendingRewardedOffer = offer;
            Assert.IsTrue(sim.DeliverVerifiedReward(offer, StartUtcMs).Ok);
            Assert.AreEqual(gems0 + 2, sim.State.Profile.Gems);
            Assert.IsNull(sim.State.Profile.PendingRewardedOffer);
            Assert.AreEqual("duplicate", sim.DeliverVerifiedReward(offer, StartUtcMs).Reason);
            Assert.AreEqual(gems0 + 2, sim.State.Profile.Gems, "одна награда за один просмотр");
            var gold = new RewardedOffer { Id = "reward_gold", RunId = sim.State.Run.RunId, Bonus = "gold", Gold = "777", Earned = true };
            Assert.IsTrue(sim.DeliverVerifiedReward(gold, StartUtcMs).Ok);
            Assert.AreEqual(new BigInteger(777), sim.State.Run.BonusCredited);
        }

        // ───────────────────────────── Динамит ─────────────────────────────

        [Test]
        public void Dynamite_LandsBesideStrikeableTargets_BlastHitsOnlyStrikeableInRadius_NoGold()
        {
            var sim = DynamiteSim();
            var run = sim.State.Run;
            var map = sim.Map;
            Assert.IsTrue(sim.TryQuoteBonus(RewardedBonusKind.Dynamite, out _), "динамит доступен");
            var strikeable = map.Targets.Where(t => sim.CanStrike(t.Id)).Select(t => t.Id).ToHashSet();

            Assert.IsTrue(sim.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Dynamite, 1)).Ok);
            var barrel = run.Dynamite;
            Assert.IsNotNull(barrel, "бочка на поле");
            var launched = Drain(sim).Single(e => e.Type == OutpostEventType.DynamiteLaunched);
            Assert.AreEqual(barrel.Cell, launched.Cell);
            Assert.AreEqual(barrel.Seq, launched.BatchSeq);
            Assert.IsTrue(strikeable.Contains(barrel.TargetId), "мортира целится в доступную кирке цель");
            Assert.IsFalse(sim.TryQuoteBonus(RewardedBonusKind.Dynamite, out _), "вторая бочка не предлагается");
            Assert.IsTrue(map.IsPlayable(barrel.Cell));

            var hpBefore = run.Targets.ToDictionary(t => t.Id, t => t.RemainingHpUnits);
            var wallet = run.Wallet;
            var blast = sim.Submit(OutpostCommand.Detonate());
            Assert.IsTrue(blast.Ok);
            Assert.IsNull(run.Dynamite, "бочка взорвана");
            var events = Drain(sim);
            var exploded = events.Single(e => e.Type == OutpostEventType.DynamiteExploded);
            Assert.AreEqual(barrel.Cell, exploded.Cell);
            Assert.AreEqual(OutpostEventType.DynamiteExploded, events.First(e => e.Type == OutpostEventType.DynamiteExploded || e.Type == OutpostEventType.TargetStruck).Type,
                "взрыв — до ударов по целям");

            int hit = 0;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var mt = map.Targets[i];
                var ts = run.Targets[i];
                long lost = hpBefore[mt.Id] - ts.RemainingHpUnits;
                bool shouldHit = strikeable.Contains(mt.Id) && InRadius(map, mt, barrel.Cell, Rules.DynamiteRadius);
                if (!shouldHit)
                {
                    Assert.AreEqual(0, lost, mt.Id + ": вне взрыва или недоступна кирке");
                    continue;
                }
                hit++;
                if (mt.Id == barrel.TargetId)
                    Assert.IsTrue(ts.Destroyed, "цель мортиры — вся прочность");
                else
                    Assert.AreEqual(System.Math.Min(hpBefore[mt.Id], (long)(ts.MaxHpUnits * Rules.DynamiteRingShare).Ceil()), lost, mt.Id + ": кольцо — половина максимума");
            }
            Assert.AreEqual(hit, exploded.Count);
            Assert.GreaterOrEqual(hit, 1);
            Assert.AreEqual(wallet, run.Wallet, "взрыв не начисляет золото — только груз на земле");
            AssertInvariants(sim, "после взрыва");
            Assert.AreEqual("unknown", sim.Submit(OutpostCommand.Detonate()).Reason, "бочки нет");
        }

        [Test]
        public void Dynamite_CardLevel9_RingTakesWholeMaxHp()
        {
            var plain = DynamiteSim();
            var carded = Sim(plain.State.Run.Zone, p => p.Cards["ca063"] = new CardProgress { Level = 9 });
            Assert.AreEqual(Rational.One, carded.Stats.CardDynamitePower, "Lv9 — +100 %");
            Assert.IsTrue(carded.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Dynamite, 1)).Ok);
            var barrel = carded.State.Run.Dynamite;
            var ring = carded.Map.Targets.Where(t => t.Id != barrel.TargetId && carded.CanStrike(t.Id)
                && InRadius(carded.Map, t, barrel.Cell, Rules.DynamiteRadius)).Select(t => t.Id).ToList();
            Assert.IsTrue(carded.Submit(OutpostCommand.Detonate()).Ok);
            foreach (int id in ring) Assert.IsTrue(T(carded, id).Destroyed, id + ": кольцо 1/2 × (1 + 1) = вся прочность");
            AssertInvariants(carded, "после взрыва с карточкой");
        }

        [Test]
        public void Dynamite_BarrelSurvivesSaveLoad_DeterministicPlacement()
        {
            var a = DynamiteSim();
            var b = Sim(a.State.Run.Zone);
            Assert.IsTrue(a.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Dynamite, 1)).Ok);
            Assert.IsTrue(b.Submit(OutpostCommand.RewardBonus(RewardedBonusKind.Dynamite, 1)).Ok);
            Assert.AreEqual(a.State.Run.Dynamite.Cell, b.State.Run.Dynamite.Cell, "место бочки детерминировано");
            Assert.AreEqual(a.State.Run.Dynamite.TargetId, b.State.Run.Dynamite.TargetId);
            var json = OutpostJson.Serialize(a.State);
            var loaded = OutpostJson.Deserialize<OutpostGameState>(json);
            Assert.IsNotNull(loaded.Run.Dynamite);
            Assert.AreEqual(a.State.Run.Dynamite.Cell, loaded.Run.Dynamite.Cell);
            Assert.AreEqual(a.State.Run.Dynamite.Seq, loaded.Run.Dynamite.Seq);
            Assert.AreEqual(2, loaded.Run.NextDynamiteSeq);
            Assert.IsNull(new OutpostRun().Dynamite, "новая шахта начинается без бочки");
        }
    }
}
