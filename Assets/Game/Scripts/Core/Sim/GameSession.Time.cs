using System;
using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Sim
{
    public sealed partial class GameSession
    {
        /// <summary>
        /// Продвинуть симуляцию на dt тиков событийными шагами (ТЗ 15.2).
        /// Порядок событий в один момент: удары (по ID котика) → завершение цели → циклы генераторов → таймеры → автосбор добычи.
        /// Advance(a) + Advance(b) ≡ Advance(a + b). Переносимый котик (<see cref="DraggingCatId"/>) не бьёт, его фаза не растёт.
        /// Онлайн-шаг сначала выполняет проверку автоматизации, отложенную офлайн-расчётом (<see cref="RunDeferredAutomationCheck"/>).
        /// </summary>
        public void Advance(long dtTicks)
        {
            if (!OfflineMode) RunDeferredAutomationCheck();
            if (dtTicks <= 0 || Player.CampaignCompleted) return;
            long remaining = dtTicks;
            int guard = 0;
            while (true)
            {
                ProcessDueEvents();
                if (remaining <= 0) break;
                long next = TimeToNextEvent();
                long step = Math.Min(remaining, next);
                if (step > 0)
                {
                    ElapseTime(step);
                    remaining -= step;
                }
                if (++guard > 50_000_000) throw new InvalidOperationException("Advance: слишком много событий");
            }
        }

        long TimeToNextEvent()
        {
            long best = long.MaxValue;
            foreach (var cat in Mine.Cats)
            {
                if (cat.TargetIndex < 0 || cat.Id == m_DragCatId) continue;
                long left = AttackDelayTicks(cat.Level) - cat.PhaseTicks;
                if (left < best) best = Math.Max(0, left);
            }
            for (int ord = 0; ord < Mine.Objects.Count; ord++)
            {
                if (!IsGeneratorRunning(ord)) continue;
                long left = GeneratorCycleTicks(ord) - Mine.Objects[ord].Gen.CycleTicks;
                if (left < best) best = Math.Max(0, left);
            }
            if (FreeCatTimerRunning() && Mine.FreeCatTimerTicks < best) best = Math.Max(0, Mine.FreeCatTimerTicks);
            if (DeliveryTimerRunning() && Mine.DeliveryTimerTicks < best) best = Math.Max(0, Mine.DeliveryTimerTicks);
            if (FreeChestTimerRunning() && Player.FreeChest.TimerTicks < best) best = Math.Max(0, Player.FreeChest.TimerTicks);
            foreach (var loot in Mine.Loot)
            {
                long left = LootAutoCollectTicks - loot.AgeTicks;
                if (left < best) best = Math.Max(0, left);
            }
            return best;
        }

        void ElapseTime(long step)
        {
            foreach (var cat in Mine.Cats)
                if (cat.TargetIndex >= 0 && cat.Id != m_DragCatId) cat.PhaseTicks += step;
            for (int ord = 0; ord < Mine.Objects.Count; ord++)
                if (IsGeneratorRunning(ord)) Mine.Objects[ord].Gen.CycleTicks += step;
            if (FreeCatTimerRunning()) Mine.FreeCatTimerTicks -= step;
            if (DeliveryTimerRunning()) Mine.DeliveryTimerTicks -= step;
            if (FreeChestTimerRunning()) Player.FreeChest.TimerTicks -= step;
            foreach (var loot in Mine.Loot) loot.AgeTicks += step;
            Player.PlayerTimeTicks += step;
            Mine.ZoneTimeTicks += step;
        }

        bool IsGeneratorRunning(int ord)
        {
            var st = Mine.Objects[ord];
            if (st.Status != ObjectStatus.Producing || st.Gen == null) return false;
            if (Layout.Objects[ord].Kind == ObjectKind.Cart) return true;
            return st.Gen.Automated || !st.Gen.PayoutReady;
        }

        bool FreeCatTimerRunning()
        {
            if (Mine.FreeCatReady) return false;
            foreach (var c in Mine.Containers) if (c.Kind == ContainerKind.FreeCat) return false;
            return OccupiedSlots < UnitCap();
        }

        bool DeliveryTimerRunning()
        {
            if (!Mine.DeliveriesEnabled || Mine.DeliveryReady || OfflineMode) return false;
            foreach (var c in Mine.Containers) if (c.Kind == ContainerKind.Delivery) return false;
            return true;
        }

        bool FreeChestTimerRunning() => Player.FreeChest.Enabled && Player.FreeChest.Stock < Balance.FreeGachaCycle.MaxFreeGachas;

        void ProcessDueEvents()
        {
            // 1–2. Удары и завершение целей. В переносе котик не наносит ударов (ТЗ 7.5).
            for (int i = 0; i < Mine.Cats.Count; i++)
            {
                var cat = Mine.Cats[i];
                if (cat.TargetIndex < 0 || cat.Id == m_DragCatId) continue;
                long delay = AttackDelayTicks(cat.Level);
                if (cat.PhaseTicks < delay) continue;
                cat.PhaseTicks -= delay;
                int ord = OrdinalOf(cat.TargetIndex);
                if (ord < 0 || Mine.Objects[ord].Status != ObjectStatus.Intact || !Mine.Objects[ord].Hp.IsPositive)
                {
                    SetIdle(cat);
                    continue;
                }
                var damage = CatDamage(cat.Level);
                bool crit = false;
                double chance = CritChance(cat.Level);
                if (chance > 0 && Player.Rng.Crit.NextDouble() < chance)
                {
                    crit = true;
                    damage *= GameNumber.FromDouble(CritPower(cat.Level));
                }
                ApplyDamage(ord, damage, cat.Id, crit);
            }

            // 3. Циклы генераторов.
            for (int ord = 0; ord < Mine.Objects.Count; ord++)
            {
                if (!IsGeneratorRunning(ord)) continue;
                var gen = Mine.Objects[ord].Gen;
                long cycle = GeneratorCycleTicks(ord);
                if (gen.CycleTicks < cycle) continue;
                var output = GeneratorOutput(ord);
                bool isCart = Layout.Objects[ord].Kind == ObjectKind.Cart;
                if (isCart || gen.Automated)
                {
                    gen.CycleTicks -= cycle;
                    AddGold(output, isCart ? "gold:forge" : "gold:shaft");
                    if (!OfflineMode)
                        Emit(new SimEvent { Type = SimEventType.GeneratorPaid, ObjectIndex = Layout.Objects[ord].Index, Amount = output });
                }
                else
                {
                    gen.CycleTicks = 0;
                    gen.PayoutReady = true;
                    gen.PendingPayout = output;
                    Emit(new SimEvent { Type = SimEventType.PayoutReady, ObjectIndex = Layout.Objects[ord].Index, Amount = output });
                }
            }

            // 4. Таймеры.
            if (FreeCatTimerRunning() && Mine.FreeCatTimerTicks <= 0)
            {
                Mine.FreeCatTimerTicks = 0;
                Mine.FreeCatReady = true;
            }
            if (DeliveryTimerRunning() && Mine.DeliveryTimerTicks <= 0)
            {
                Mine.DeliveryTimerTicks = 0;
                Mine.DeliveryReady = true;
            }
            if (FreeChestTimerRunning() && Player.FreeChest.TimerTicks <= 0)
            {
                var fc = Player.FreeChest;
                fc.Stock++;
                fc.TimerTicks = fc.Stock < Balance.FreeGachaCycle.MaxFreeGachas ? FreeChestPeriodTicks : 0;
            }

            // 5. Автосбор добычи (и офлайн): начисление — как при сборе тапом.
            if (Mine.Loot.Count > 0) AutoCollectDueLoot();
            TrySpawnPending();
        }

        long FreeChestPeriodTicks => SecondsToTicks(Balance.FreeGachaCycle.Hours * 3600.0);

        void ApplyDamage(int ord, GameNumber damage, int catId, bool crit)
        {
            var st = Mine.Objects[ord];
            st.Hp -= damage;
            if (!OfflineMode)
                Emit(new SimEvent { Type = SimEventType.TargetHit, ObjectIndex = Layout.Objects[ord].Index, CatId = catId, Amount = damage, Crit = crit });
            if (!st.Hp.IsPositive)
            {
                st.Hp = GameNumber.Zero;
                CompleteTarget(ord);
            }
        }

        void SetIdle(CatState cat)
        {
            if (cat.TargetIndex < 0) return;
            // Цель переносимого котика завершилась: при отмене переноса восстанавливать нечего.
            if (cat.Id == m_DragCatId) m_DragTargetIndex = -1;
            cat.TargetIndex = -1;
            cat.PhaseTicks = 0;
            Emit(new SimEvent { Type = SimEventType.CatIdle, CatId = cat.Id, ObjectIndex = -1, Cell = cat.Cell });
        }

        void IdleCatsOn(int ord)
        {
            int index = Layout.Objects[ord].Index;
            foreach (var c in Mine.Cats)
                if (c.TargetIndex == index) SetIdle(c);
        }

        /// <summary>Завершение цели одной транзакцией (ТЗ 8.3).</summary>
        void CompleteTarget(int ord)
        {
            var obj = Layout.Objects[ord];
            var st = Mine.Objects[ord];
            long op = NextOp();
            // Работники цели (кроме переносимого) — до снятия назначений: с AutoMining они возьмутся за следующую цель.
            m_Workers.Clear();
            if (AutoMining && !OfflineMode)
                foreach (var c in Mine.Cats)
                    if (c.TargetIndex == obj.Index && c.Id != m_DragCatId) m_Workers.Add(c);
            IdleCatsOn(ord);
            switch (obj.Kind)
            {
                case ObjectKind.Rock:
                    st.Status = ObjectStatus.Destroyed;
                    Mine.RocksDestroyed++;
                    Emit(new SimEvent { Type = SimEventType.RockDestroyed, OpId = op, ObjectIndex = obj.Index, Text = obj.TypeId });
                    GrantRockReward(obj, op);
                    break;
                case ObjectKind.Shaft:
                    st.Status = ObjectStatus.ReadyToActivate;
                    Emit(new SimEvent { Type = SimEventType.ShaftCleared, OpId = op, ObjectIndex = obj.Index, Text = obj.TypeId });
                    break;
                case ObjectKind.Gate:
                    st.Status = ObjectStatus.Broken;
                    Emit(new SimEvent { Type = SimEventType.GateBroken, OpId = op, ObjectIndex = obj.Index });
                    break;
                case ObjectKind.Exit:
                    st.Status = ObjectStatus.Broken;
                    Emit(new SimEvent { Type = SimEventType.ExitBroken, OpId = op, ObjectIndex = obj.Index });
                    break;
            }
            // Цель уже не цела (камень освободил клетки): работники по порядку списка берутся за следующую.
            for (int i = 0; i < m_Workers.Count; i++) ContinueMining(m_Workers[i], obj);
            m_Workers.Clear();
        }

        /// <summary>
        /// AutoMining (ТЗ 7.3, решение владельца от 8 октября 2026): котик, чья цель <paramref name="done"/> завершилась,
        /// бьёт дальше только то, что достаёт со своей клетки: соседнюю допустимую цель, для которой его клетка — рабочая
        /// (правило свободной клетки, 7.3); из нескольких — ближе к завершённой цели. Сам котик никуда не переходит:
        /// если рядом целей нет, он остаётся в Idle на месте, пока игрок не перенесёт его.
        /// </summary>
        void ContinueMining(CatState cat, ZoneObject done)
        {
            if (cat.TargetIndex >= 0 || FindCat(cat.Id) == null) return;
            double gx = done.Col + done.Width / 2.0, gy = done.Row - done.Depth / 2.0 + 1.0;
            int next = NearestAdjacentTarget(cat.Cell, gx, gy);
            if (next >= 0) Assign(cat, Layout.Objects[next].Index, false);
        }

        /// <summary>Создать отложенные контейнеры, если появились место и клетка.</summary>
        void TrySpawnPending()
        {
            if (Mine.FreeCatReady && OccupiedSlots < UnitCap())
            {
                bool hasContainer = false;
                foreach (var c in Mine.Containers) if (c.Kind == ContainerKind.FreeCat) hasContainer = true;
                if (!hasContainer)
                {
                    int cell = FindSpawnCell();
                    if (cell >= 0)
                    {
                        int level = Math.Max(1, HireLevel() + Balance.Cart.SpawnLevelOffset);
                        var c = AddContainer(ContainerKind.FreeCat, cell);
                        c.CatLevel = level;
                        Mine.FreeCatReady = false;
                    }
                }
            }
            if (Mine.DeliveryReady && !OfflineMode)
            {
                bool hasBox = false;
                foreach (var c in Mine.Containers) if (c.Kind == ContainerKind.Delivery) hasBox = true;
                if (!hasBox) TryCreateDelivery();
            }
        }

        ContainerState AddContainer(ContainerKind kind, int cell, long op = 0, GameNumber amount = default)
        {
            var c = new ContainerState { Id = Mine.NextContainerId++, Kind = kind, Cell = cell };
            Mine.Containers.Add(c);
            m_CellContainer[cell] = c.Id;
            Emit(new SimEvent { Type = SimEventType.ContainerSpawned, OpId = op, Cell = cell, ObjectIndex = -1, CatId = c.Id, Amount = amount, Text = kind.ToString() });
            return c;
        }

        // ───────────────────────────── Включение доставок и бесплатного сундука ─────────────────────────────

        void UpdateUnlocks()
        {
            var p = Balance.Props;
            if (!Mine.DeliveriesEnabled && Rank >= p.DeliveryStartRank && Mine.ActivatedShafts >= p.DeliveryStartMineShaftNum)
            {
                Mine.DeliveriesEnabled = true;
                Mine.DeliveryTimerTicks = SecondsToTicks(p.DeliveryStartupDelaySec);
                Mine.DeliveryReady = false;
            }
            var fg = Balance.FreeGachaCycle;
            if (!Player.FreeChest.Enabled && Rank >= fg.StartRank && Mine.ActivatedShafts >= fg.StartMineshaftNum)
            {
                Player.FreeChest.Enabled = true;
                Player.FreeChest.TimerTicks = FreeChestPeriodTicks;
            }
        }

        // ───────────────────────────── Доставки (ТЗ 10.5, E-15) ─────────────────────────────

        void PruneWindows()
        {
            var h = Player.DeliveryHistory;
            long now = Player.PlayerTimeTicks;
            long claimWindow = SecondsToTicks(Balance.Props.DeliveryClaimCountResetSec);
            h.ClaimTimes.RemoveAll(t => now - t >= claimWindow);
            long dupeWindow = SecondsToTicks(Balance.Props.DeliveryMaxDupesResetSec);
            foreach (var list in h.DupeTimes.Values) list.RemoveAll(t => now - t >= dupeWindow);
        }

        public double NextDeliveryIntervalSeconds(bool withVariance)
        {
            PruneWindows();
            var p = Balance.Props;
            int claims = Player.DeliveryHistory.ClaimTimes.Count;
            double s = Formula.Evaluate(p.DeliveryDelaySecFormulaType, p.DeliveryDelaySecBase, p.DeliveryDelaySecMultiplier, p.DeliveryDelaySecGrowth, claims + 1).ToDouble();
            // Разброс таймера — из своего потока ГСЧ (ТЗ 15.2); поток доставок только выбирает содержимое.
            if (withVariance && p.DeliveryDelaySecVariance > 0)
                s *= 1.0 + p.DeliveryDelaySecVariance * (2.0 * Player.Rng.Timers.NextDouble() - 1.0);
            return Math.Max(1.0, s);
        }

        public List<DeliveryDto> DeliveryPool()
        {
            PruneWindows();
            var pool = new List<DeliveryDto>();
            var h = Player.DeliveryHistory;
            bool catRoom = OccupiedSlots < UnitCap();
            foreach (var d in Balance.Root.Deliveries)
            {
                if (d.RankUnlock > Rank) continue;
                if (d.CheckPointUnlock > Mine.GatesOpened) continue;
                if (d.MaxDupes >= 0)
                {
                    int used = h.DupeTimes.TryGetValue(d.Id, out var list) ? list.Count : 0;
                    if (used >= d.MaxDupes) continue;
                }
                int rt = d.RewardModel.RewardType;
                if (rt == RewardTypes.Miner && !catRoom) continue;
                if (rt == RewardTypes.Spell && Player.Dynamite >= MaxDynamite) continue;
                if (d.Weight <= 0) continue;
                pool.Add(d);
            }
            return pool;
        }

        void TryCreateDelivery()
        {
            int cell = FindSpawnCell();
            if (cell < 0) return;           // ждём клетку, содержимое ещё не определено
            var pool = DeliveryPool();
            Mine.DeliveryReady = false;
            if (pool.Count == 0)
            {
                Mine.DeliveryTimerTicks = SecondsToTicks(NextDeliveryIntervalSeconds(true));
                return;
            }
            double total = 0;
            foreach (var d in pool) total += d.Weight;
            double r = Player.Rng.Delivery.NextDouble() * total;
            var pick = pool[pool.Count - 1];
            foreach (var d in pool)
            {
                r -= d.Weight;
                if (r < 0) { pick = d; break; }
            }
            var h = Player.DeliveryHistory;
            if (!h.DupeTimes.TryGetValue(pick.Id, out var times))
            {
                times = new List<long>();
                h.DupeTimes[pick.Id] = times;
            }
            times.Add(Player.PlayerTimeTicks);

            if (pick.IsAd)
            {
                // Реклама не превращается в бесплатную награду: слот пропускается (ТЗ 10.5).
                h.SkippedAds++;
                Mine.DeliveryTimerTicks = SecondsToTicks(NextDeliveryIntervalSeconds(true));
                Emit(new SimEvent { Type = SimEventType.DeliverySkippedAd, ObjectIndex = -1, Text = pick.Id });
                return;
            }

            var box = AddContainer(ContainerKind.Delivery, cell);
            box.DeliveryId = pick.Id;
            box.RewardType = pick.RewardModel.RewardType;
            box.DetailedType = pick.RewardModel.DetailedType;
            switch (box.RewardType)
            {
                case RewardTypes.Miner:
                    // Счётная величина (сдвиг уровня) — в double.
                    box.CatLevel = DeliveryCatLevel(Formula.Ft3(pick.QuantityBase, pick.QuantityMultiplier, pick.QuantityGrowth, pick.QuantityPower, pick.QuantityRound, Rank));
                    break;
                case RewardTypes.Currency:
                {
                    // Экономическая величина — в GameNumber (ТЗ 15.1).
                    var q = Formula.Ft3Number(pick.QuantityBase, pick.QuantityMultiplier, pick.QuantityGrowth, pick.QuantityPower, pick.QuantityRound, Rank);
                    if (box.DetailedType == "Core")
                        box.Amount = Formula.Payout(q * GetRewardIncomeRate());
                    else
                        box.Amount = Formula.Payout(q);
                    break;
                }
                case RewardTypes.Spell:
                    box.Amount = GameNumber.One;
                    break;
            }
        }

        /// <summary>Уровень котика из доставки: max(1, L_next + Q) с правилом догоняния (ТЗ 10.5).</summary>
        int DeliveryCatLevel(double q)
        {
            int lNext = HireLevel();
            int minLevel = int.MaxValue, minCount = 0;
            void Consider(int level)
            {
                if (level < minLevel) { minLevel = level; minCount = 1; }
                else if (level == minLevel) minCount++;
            }
            foreach (var c in Mine.Cats) Consider(c.Level);
            foreach (var c in Mine.Containers) if (c.HoldsCat && c.CatLevel > 0) Consider(c.CatLevel);
            int delta = Balance.Props.DeliveryForeverAloneDelta;
            if (minCount == 1 && minLevel <= lNext - 1 - delta) return minLevel;
            return Math.Max(1, lNext + (int)Math.Round(q));
        }
    }
}
