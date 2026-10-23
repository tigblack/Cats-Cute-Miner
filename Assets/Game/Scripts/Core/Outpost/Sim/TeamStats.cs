using System;
using System.Collections.Generic;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Эффективные характеристики команды (ТЗ 9, 12.2, 12.3): единственное место расчёта из базовых значений,
    /// локальных уровней run, надетого постоянного комплекта и активных карточек (CARD_EFFECT_MAPPING.md). Каждый бонус
    /// карточки суммируется здесь один раз; без карточек все величины совпадают с прежними. Все величины точные.
    /// Исключение — выпуск заводов (FactoryOutput, ТЗ 2.2 §4.4): это не характеристика команды, его считает только
    /// <see cref="FactoryMath.UnitsPerCycle"/>.
    /// </summary>
    public sealed class TeamStats
    {
        public int Zone;
        public int Workers;
        public Rational PickaxeBonus;
        public Rational BootsBonus;
        public Rational HatBonus;
        public int BackpackExtra;

        public Rational LocalDamage;
        public Rational FinalDamage;
        /// <summary>Урон удара в долях HP: max(1, RoundHalfUp(FinalDamage · 10^6)).</summary>
        public long DamageUnits;
        /// <summary>Урон удара кирки игрока: max(1, RoundHalfUp(FinalDamage · TapDamageFactor · 10^6)); 0 — удары выключены.</summary>
        public long TapDamageUnits;
        public Rational LocalMoveSpeed;
        public Rational FinalMoveSpeed;
        /// <summary>Тики на ребро: ceil(TicksPerSecond / FinalMoveSpeed).</summary>
        public long EdgeTicks;
        public int LocalCapacity;
        public int Capacity;
        public Rational UnloadRate;
        /// <summary>Тики на порцию: ceil(TicksPerSecond / UnloadRate).</summary>
        public long UnloadTicks;
        public Rational LocalValue;
        public Rational FinalValue;
        /// <summary>Мест разгрузки у склада: <see cref="OutpostIds.DepotSlotCount"/> + уровень «Мест разгрузки» (вид, панель «База»).</summary>
        public int DepotSlots;
        /// <summary>S(z) = ZoneScale^(z−1).</summary>
        public Rational MoneyScale;
        public Rational CoinBase;
        /// <summary>Тики подбора одной порции: ceil(PickupTicksPerUnit / (1 + бонус подбора)).</summary>
        public long PickupTicks;

        // ── Бонусы карточек (доли и целые; 0 — нет карточек) ──
        public Rational CardGlobalValue, CardGlobalDamage, CardGateDamage, CardMoveSpeed, CardUnload, CardPickup;
        public Rational CardHireDiscount, CardBranchDiscount, CardTapDamage, CardChestLuck, CardChestElixir, CardParts;
        /// <summary>Карточка «Demolition Expert» (ТЗ 10.6): +доля к урону взрыва динамита по кольцу.</summary>
        public Rational CardDynamitePower;
        /// <summary>Шанс критического удара (доля) и множитель крита.</summary>
        public Rational CritChance, CritMultiplier = Rational.Of(2, 1);
        /// <summary>Шанс крита в миллионных долях для броска.</summary>
        public int CritChancePpm;
        public int CardTeamCap, CardStartWorkers, CardCapacity, CardOfflineHours, CardChestCards;
        /// <summary>Бонусы по виду цели ('r', 'x', 's') и по TypeId месторождения (карточки-специалисты).</summary>
        public readonly Dictionary<char, Rational> CardKindValue = new Dictionary<char, Rational>();
        public readonly Dictionary<char, Rational> CardKindDamage = new Dictionary<char, Rational>();
        public readonly Dictionary<string, Rational> CardTypeValue = new Dictionary<string, Rational>(StringComparer.Ordinal);
        public readonly Dictionary<string, Rational> CardTypeDamage = new Dictionary<string, Rational>(StringComparer.Ordinal);
        /// <summary>Есть ли бонусы урона/цены по виду или типу цели (иначе цель-специфичный расчёт совпадает с общим).</summary>
        public bool HasTargetBonuses;

        readonly Dictionary<Rational, BigInteger> m_CoinCache = new Dictionary<Rational, BigInteger>();

        /// <summary>
        /// cards — каталог карточек (null — без карточек); extraWorkers — котики сверх StartWorkers, выданные карточками при
        /// создании шахты (<see cref="OutpostRun.ExtraWorkers"/>).
        /// </summary>
        public static TeamStats Compute(OutpostBalanceConfig b, int[] levels, OutpostProfile profile, EquipmentCatalog catalog, int zone,
            CardCatalog cards = null, int extraWorkers = 0)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            int L(UpgradeBranch br) => levels != null && levels.Length > (int)br ? levels[(int)br] : 0;
            var s = new TeamStats { Zone = zone };
            if (profile != null && catalog != null)
            {
                s.PickaxeBonus = EquipmentService.SlotBonus(profile, catalog, EquipSlot.Pickaxe);
                s.HatBonus = EquipmentService.SlotBonus(profile, catalog, EquipSlot.Hat);
                s.BootsBonus = EquipmentService.SlotBonus(profile, catalog, EquipSlot.Boots);
                s.BackpackExtra = EquipmentService.BackpackExtra(profile, catalog);
            }
            if (profile != null && cards != null) s.ApplyCards(profile, cards);
            s.Workers = b.StartWorkers + Math.Max(0, extraWorkers) + L(UpgradeBranch.Team);

            s.LocalDamage = b.BaseDamage * b.DamageGrowth.Pow(L(UpgradeBranch.Pickaxes)) * b.Milestone(UpgradeBranch.Pickaxes, L(UpgradeBranch.Pickaxes));
            s.FinalDamage = s.LocalDamage * (Rational.One + s.PickaxeBonus) * (Rational.One + s.CardGlobalDamage);
            if (profile != null && profile.DamageBonusSecondsLeft.Sign > 0) s.FinalDamage *= RewardedBonus.DamageMultiplier;
            var du = (s.FinalDamage * OutpostIds.HpUnitsPerHp).RoundHalfUp();
            s.DamageUnits = Math.Max(1L, ExactMath.ToLongChecked(du, "DamageUnits"));
            if (b.TapDamageFactor.Sign > 0)
            {
                var tu = (s.FinalDamage * b.TapDamageFactor * (Rational.One + s.CardTapDamage) * OutpostIds.HpUnitsPerHp).RoundHalfUp();
                s.TapDamageUnits = Math.Max(1L, ExactMath.ToLongChecked(tu, "TapDamageUnits"));
            }

            // Пороги «Обуви», «Разгрузки» и «Сумок» (первая сессия, профиль b8): множитель или места за каждый пройденный порог.
            s.LocalMoveSpeed = b.BaseMoveSpeed * (Rational.One + b.MoveSpeedStep * L(UpgradeBranch.Boots)) * b.Milestone(UpgradeBranch.Boots, L(UpgradeBranch.Boots));
            s.FinalMoveSpeed = s.LocalMoveSpeed * (Rational.One + s.BootsBonus) * (Rational.One + s.CardMoveSpeed);
            s.EdgeTicks = Math.Max(1L, ExactMath.ToLongChecked((new Rational(b.TicksPerSecond) / s.FinalMoveSpeed).Ceil(), "EdgeTicks"));

            s.LocalCapacity = b.BaseCapacity + b.CapacityStep * L(UpgradeBranch.Bags) +
                (b.HasBranch(UpgradeBranch.Bags) ? b.Branch(UpgradeBranch.Bags).MilestoneSlots * b.MilestonesReached(UpgradeBranch.Bags, L(UpgradeBranch.Bags)) : 0);
            s.Capacity = s.LocalCapacity + s.BackpackExtra + s.CardCapacity;

            s.UnloadRate = b.BaseUnloadRate * (Rational.One + b.UnloadStep * L(UpgradeBranch.Unload)) * b.Milestone(UpgradeBranch.Unload, L(UpgradeBranch.Unload)) *
                (Rational.One + s.CardUnload);
            s.UnloadTicks = Math.Max(1L, ExactMath.ToLongChecked((new Rational(b.TicksPerSecond) / s.UnloadRate).Ceil(), "UnloadTicks"));
            s.PickupTicks = Math.Max(1L, ExactMath.ToLongChecked((new Rational(b.PickupTicksPerUnit) / (Rational.One + s.CardPickup)).Ceil(), "PickupTicks"));

            s.LocalValue = b.ValueGrowth.Pow(L(UpgradeBranch.Value)) * b.Milestone(UpgradeBranch.Value, L(UpgradeBranch.Value));
            s.FinalValue = s.LocalValue * (Rational.One + s.HatBonus) * (Rational.One + s.CardGlobalValue);
            s.MoneyScale = b.ZoneMoneyScale(zone);
            s.CoinBase = b.CoinBase;
            s.DepotSlots = OutpostIds.DepotSlotCount + L(UpgradeBranch.Bays);
            return s;
        }

        /// <summary>CoinValue = max(1, ceil(CoinBase · S(z) · valueFactor · FinalValue)) — фиксируется при выпуске порции.</summary>
        public BigInteger CoinValue(Rational valueFactor)
        {
            if (m_CoinCache.TryGetValue(valueFactor, out var v)) return v;
            v = BigInteger.Max(BigInteger.One, (CoinBase * MoneyScale * valueFactor * FinalValue).Ceil());
            m_CoinCache[valueFactor] = v;
            return v;
        }

        /// <summary>
        /// Цена новой порции цели: <see cref="CoinValue"/> × (1 + бонусы карточек к виду цели и к её TypeId — месторождению
        /// ресурса). Общий бонус цены уже в FinalValue.
        /// </summary>
        public BigInteger CoinValueFor(char kind, string typeId, Rational valueFactor)
        {
            if (!HasTargetBonuses) return CoinValue(valueFactor);
            var bonus = Get(CardKindValue, kind) + Get(CardTypeValue, typeId);
            if (bonus.Sign == 0) return CoinValue(valueFactor);
            return BigInteger.Max(BigInteger.One, (CoinBase * MoneyScale * valueFactor * FinalValue * (Rational.One + bonus)).Ceil());
        }

        /// <summary>
        /// Урон удара котика по цели в долях HP: max(1, RoundHalfUp(FinalDamage · (1 + ворота + вид + TypeId) · 10^6)); общий бонус
        /// урона уже в FinalDamage. Без цель-специфичных бонусов — <see cref="DamageUnits"/>.
        /// </summary>
        public long DamageUnitsFor(char kind, string typeId)
        {
            var bonus = kind == 'p' ? CardGateDamage : Rational.Zero;
            if (HasTargetBonuses) bonus += Get(CardKindDamage, kind) + Get(CardTypeDamage, typeId);
            if (bonus.Sign == 0) return DamageUnits;
            var du = (FinalDamage * (Rational.One + bonus) * OutpostIds.HpUnitsPerHp).RoundHalfUp();
            return Math.Max(1L, ExactMath.ToLongChecked(du, "DamageUnits"));
        }

        /// <summary>Урон критического удара: RoundHalfUp(units · CritMultiplier).</summary>
        public long CritUnits(long units) => Math.Max(units, ExactMath.ToLongChecked((CritMultiplier * units).RoundHalfUp(), "CritUnits"));

        static Rational Get<TKey>(Dictionary<TKey, Rational> d, TKey key) => key != null && d.TryGetValue(key, out var v) ? v : Rational.Zero;

        static void AddTo<TKey>(Dictionary<TKey, Rational> d, TKey key, Rational v) =>
            d[key] = d.TryGetValue(key, out var old) ? old + v : v;

        /// <summary>Суммировать эффекты активных карточек профиля (уровень ≥ 1), каждую один раз.</summary>
        void ApplyCards(OutpostProfile profile, CardCatalog cards)
        {
            CritMultiplier = cards.Rules.BaseCritMultiplier;
            if (profile.Cards == null) return;
            foreach (var kv in profile.Cards)
            {
                if (kv.Value == null || kv.Value.Level < 1) continue;
                var def = cards.Get(kv.Key);
                if (def == null) continue;
                var v = def.EffectAt(kv.Value.Level);
                int whole = ExactMath.ToIntChecked(v.Floor(), "card effect");
                switch (def.Effect.Kind)
                {
                    case CardEffectKind.TargetValue:
                        foreach (var t in def.TargetIds) AddTo(CardTypeValue, t, v);
                        HasTargetBonuses = true;
                        break;
                    case CardEffectKind.TargetDamage:
                        foreach (var t in def.TargetIds) AddTo(CardTypeDamage, t, v);
                        HasTargetBonuses = true;
                        break;
                    case CardEffectKind.FactoryOutput:
                        // ТЗ 2.2 §7.1: карточки заводов не дают ни цены порций, ни урона — только выпуск товара своего типа. Его
                        // считает единственная функция FactoryMath.UnitsPerCycle прямо по профилю на отметке конца цикла (эти
                        // характеристики пересчитываются только фазой 7), поэтому здесь эффект не копится.
                        break;
                    case CardEffectKind.KindValue:
                        AddTo(CardKindValue, def.Effect.TargetKind[0], v);
                        HasTargetBonuses = true;
                        break;
                    case CardEffectKind.KindDamage:
                        AddTo(CardKindDamage, def.Effect.TargetKind[0], v);
                        HasTargetBonuses = true;
                        break;
                    case CardEffectKind.UnloadRate: CardUnload += v; break;
                    case CardEffectKind.GlobalValue: CardGlobalValue += v; break;
                    case CardEffectKind.GlobalDamage: CardGlobalDamage += v; break;
                    case CardEffectKind.GateDamage: CardGateDamage += v; break;
                    case CardEffectKind.MoveSpeed: CardMoveSpeed += v; break;
                    case CardEffectKind.PickupSpeed: CardPickup += v; break;
                    case CardEffectKind.HireDiscount: CardHireDiscount += v; break;
                    case CardEffectKind.BranchDiscount: CardBranchDiscount += v; break;
                    case CardEffectKind.TapDamage: CardTapDamage += v; break;
                    case CardEffectKind.DynamitePower: CardDynamitePower += v; break;
                    case CardEffectKind.ChestLuck: CardChestLuck += v; break;
                    case CardEffectKind.ChestElixir: CardChestElixir += v; break;
                    case CardEffectKind.CompletionParts: CardParts += v; break;
                    case CardEffectKind.CritChance: CritChance += v; break;
                    case CardEffectKind.CritPower: CritMultiplier += v; break;
                    case CardEffectKind.TeamCap: CardTeamCap += whole; break;
                    case CardEffectKind.StartWorkers: CardStartWorkers += whole; break;
                    case CardEffectKind.Capacity: CardCapacity += whole; break;
                    case CardEffectKind.OfflineHours: CardOfflineHours += whole; break;
                    case CardEffectKind.ChestCards: CardChestCards += whole; break;
                }
            }
            var max = cards.Rules.MaxDiscount;
            if (CardHireDiscount > max) CardHireDiscount = max;
            if (CardBranchDiscount > max) CardBranchDiscount = max;
            if (CritChance > Rational.One) CritChance = Rational.One;
            CritChancePpm = ExactMath.ToIntChecked((CritChance * CardRules.ChanceScale).Floor(), "CritChancePpm");
        }

        /// <summary>Совпадение характеристик, влияющих на таймеры и расчёты (для пропуска пересчёта).</summary>
        public bool SameAs(TeamStats o) =>
            o != null && o.Zone == Zone && o.Workers == Workers && o.DamageUnits == DamageUnits && o.EdgeTicks == EdgeTicks &&
            o.Capacity == Capacity && o.UnloadTicks == UnloadTicks && o.FinalValue == FinalValue && o.FinalDamage == FinalDamage &&
            o.FinalMoveSpeed == FinalMoveSpeed && o.UnloadRate == UnloadRate;
    }

    /// <summary>Котировка пакетной покупки: число уровней, точная сумма, причина блокировки ("cap", "gold", "phase", "mode").</summary>
    public struct UpgradeQuote
    {
        public int Levels;
        public BigInteger Cost;
        public string BlockReason;
        public int CurrentLevel;
        public int Cap;
        /// <summary>Цена следующего одного уровня; null — на cap.</summary>
        public BigInteger? NextCost;

        public bool CanBuy => BlockReason == null && Levels > 0;
    }

    /// <summary>
    /// Цены и пакетные покупки локальных веток (ТЗ 9, 12.2): NextCost(z, L) = ceil(S(z) · CostZoneFactor(z) · K(z) · D · BaseCost · Growth^L ·
    /// (1 − скидка)), где K(z) — темп шахты, D — удорожание от обстановки (<see cref="OutpostRun.PriceFactor"/>),
    /// ×1/×10/«Макс» — сумма точных последовательных цен в пределах кошелька и cap. Проверка и списание используют одно значение.
    /// Скидки и добавка к пределу «Команды» — бонусы карточек из <see cref="TeamStats"/> (без stats — нет). Котики «Плаката найма»
    /// (run.ExtraWorkers) приходят из предела «Команды»: покупаемых уровней на столько же меньше, общий предел команды не растёт.
    /// </summary>
    public static class TeamUpgradeService
    {
        /// <summary>
        /// Цена уровня L → L+1 или null на cap (cap ветки + extraCap). <paramref name="priceFactor"/> — удорожание шахты от обстановки
        /// (<see cref="OutpostRun.PriceFactor"/>); null — 1.
        /// </summary>
        public static BigInteger? NextCost(OutpostBalanceConfig b, int zone, UpgradeBranch branch, int level, Rational discount = default, int extraCap = 0,
            Rational? priceFactor = null)
        {
            if (!b.HasBranch(branch)) return null;
            var br = b.Branch(branch);
            if (level >= br.Cap + extraCap) return null;
            var cost = b.PriceScale(zone) * br.BaseCost * br.GrowthPow(level);
            if (priceFactor.HasValue && priceFactor.Value != Rational.One) cost *= priceFactor.Value;
            if (discount.Sign > 0) cost *= Rational.One - discount;
            return cost.Ceil();
        }

        public static int LimitFor(BuyMode mode) => mode == BuyMode.One ? 1 : mode == BuyMode.Ten ? 10 : int.MaxValue;

        /// <summary>Скидка карточек на ветку: «Команда» — найм, остальные — улучшения базы.</summary>
        public static Rational DiscountFor(TeamStats stats, UpgradeBranch branch) =>
            stats == null ? Rational.Zero : branch == UpgradeBranch.Team ? stats.CardHireDiscount : stats.CardBranchDiscount;

        public static int ExtraCapFor(TeamStats stats, UpgradeBranch branch) =>
            stats != null && branch == UpgradeBranch.Team ? stats.CardTeamCap : 0;

        public static UpgradeQuote Quote(OutpostGameState state, UpgradeBranch branch, BuyMode mode, TeamStats stats = null) =>
            Quote(state.Run, branch, mode, stats);

        /// <summary>
        /// Котировка ветки. <paramref name="capLimit"/> — наибольший уровень по раскладке шахты (места разгрузки: свободные клетки у
        /// склада), null — без ограничения. Ветки нет в снимке run (места разгрузки в шахте, начатой до них) — "locked".
        /// </summary>
        public static UpgradeQuote Quote(OutpostRun run, UpgradeBranch branch, BuyMode mode, TeamStats stats = null, int? capLimit = null)
        {
            var b = run.Balance;
            int level = run.Level(branch);
            if (!b.HasBranch(branch))
                return new UpgradeQuote { CurrentLevel = level, Cap = level, NextCost = null, Cost = BigInteger.Zero, BlockReason = "locked" };
            var br = b.Branch(branch);
            var discount = DiscountFor(stats, branch);
            int extraCap = ExtraCapFor(stats, branch);
            if (branch == UpgradeBranch.Team) extraCap = Math.Max(-br.Cap, extraCap - run.ExtraWorkers);
            if (capLimit.HasValue && br.Cap + extraCap > capLimit.Value) extraCap = Math.Max(-br.Cap, capLimit.Value - br.Cap);
            int cap = br.Cap + extraCap;
            // Final excavations never discount live hiring, including legacy run snapshots with a 1/100 factor.
            Rational prices = run.PriceFactor;
            var q = new UpgradeQuote
            {
                CurrentLevel = level,
                Cap = cap,
                NextCost = NextCost(b, run.Zone, branch, level, discount, extraCap, prices),
                Cost = BigInteger.Zero,
            };
            if (run.Phase == RunPhase.ReadyToContinue)
            {
                q.BlockReason = "phase";
                return q;
            }
            if (branch == UpgradeBranch.Team && mode != BuyMode.One)
            {
                q.BlockReason = "mode";
                return q;
            }
            if (level >= cap)
            {
                q.BlockReason = "cap";
                return q;
            }
            int limit = LimitFor(mode);
            var sum = BigInteger.Zero;
            int n = 0;
            while (n < limit && level + n < cap)
            {
                var cost = NextCost(b, run.Zone, branch, level + n, discount, extraCap, prices).Value;
                if (sum + cost > run.Wallet) break;
                sum += cost;
                n++;
            }
            q.Levels = n;
            q.Cost = sum;
            if (n == 0) q.BlockReason = "gold";
            return q;
        }
    }
}
