using System;
using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Core.Text;

namespace CatMiners.Core.Sim
{
    public enum CommandResult
    {
        Ok,
        NotEnoughGold,
        NotEnoughElixir,
        NotEnoughDuplicates,
        CapReached,
        NoSpace,
        NoSpaceAtTarget,
        DifferentLevels,
        InvalidTarget,
        ClosedSector,
        NotAllowed,
        MaxLevel,
        EffectNotAvailable,
        NotFound,
    }

    public struct DropOutcome
    {
        public CommandResult Result;
        public bool Merged;
        public int TargetIndex;   // назначенная цель или −1
        public int Cell;
    }

    public sealed partial class GameSession
    {
        static string Reason(CommandResult r)
        {
            switch (r)
            {
                case CommandResult.NotEnoughGold: return Loc.T("core.reject.not_enough_gold");
                case CommandResult.NotEnoughElixir: return Loc.T("core.reject.not_enough_elixir");
                case CommandResult.NotEnoughDuplicates: return Loc.T("core.reject.not_enough_duplicates");
                case CommandResult.CapReached: return Loc.T("core.reject.cap_reached");
                case CommandResult.NoSpace: return Loc.T("core.reject.no_space");
                case CommandResult.NoSpaceAtTarget: return Loc.T("core.reject.no_space_at_target");
                case CommandResult.DifferentLevels: return Loc.T("core.reject.different_levels");
                case CommandResult.InvalidTarget: return Loc.T("core.reject.invalid_target");
                case CommandResult.ClosedSector: return Loc.T("core.reject.closed_sector");
                case CommandResult.MaxLevel: return Loc.T("core.reject.max_level");
                case CommandResult.EffectNotAvailable: return Loc.T("core.reject.effect_not_available");
                case CommandResult.NotFound: return Loc.T("core.reject.not_found");
                default: return Loc.T("core.reject.not_allowed");
            }
        }

        public static string Describe(CommandResult r) => r == CommandResult.Ok ? "" : Reason(r);

        CommandResult Reject(CommandResult r)
        {
            Emit(new SimEvent { Type = SimEventType.CommandRejected, ObjectIndex = -1, Text = Reason(r) });
            return r;
        }

        // ───────────────────────────── Наём (ТЗ 7.2) ─────────────────────────────

        public CommandResult CanHire()
        {
            if (Player.CampaignCompleted) return CommandResult.NotAllowed;
            if (OccupiedSlots >= UnitCap()) return CommandResult.CapReached;
            if (Mine.Gold < HirePrice()) return CommandResult.NotEnoughGold;
            if (FindSpawnCell() < 0) return CommandResult.NoSpace;
            return CommandResult.Ok;
        }

        public CommandResult Hire()
        {
            var check = CanHire();
            if (check != CommandResult.Ok) return Reject(check);
            var price = HirePrice();
            int level = HireLevel();
            int cell = FindSpawnCell();
            Mine.Gold -= price;
            if (BarrelHires)
            {
                // Бочка найма: котик появится при открытии (OpenContainer); слот занят уже сейчас.
                long op = NextOp();
                var barrel = AddContainer(ContainerKind.Hire, cell, op, price);
                barrel.CatLevel = level;
                Mine.HireCount++;
                Mine.CatsHired++;
                Ledger("gold:spent:hire", price);
                return CommandResult.Ok;
            }
            var cat = SpawnCat(level, cell);
            Mine.HireCount++;
            Mine.CatsHired++;
            Ledger("gold:spent:hire", price);
            Emit(new SimEvent { Type = SimEventType.CatSpawned, OpId = NextOp(), CatId = cat.Id, Cell = cell, ObjectIndex = -1, Amount = price, Text = "hire" });
            return CommandResult.Ok;
        }

        CatState SpawnCat(int level, int cell)
        {
            var cat = new CatState { Id = Mine.NextCatId++, Level = level, Cell = cell, TargetIndex = -1 };
            Mine.Cats.Add(cat);
            m_CellCat[cell] = cat.Id;
            return cat;
        }

        void MoveCat(CatState cat, int cell)
        {
            if (cat.Cell == cell) return;
            if (m_CellCat[cat.Cell] == cat.Id) m_CellCat[cat.Cell] = 0;
            cat.Cell = cell;
            m_CellCat[cell] = cat.Id;
        }

        void RemoveCat(CatState cat)
        {
            if (m_CellCat[cat.Cell] == cat.Id) m_CellCat[cat.Cell] = 0;
            Mine.Cats.Remove(cat);
        }

        // ───────────────────────────── Перенос, назначение, объединение (ТЗ 7.3–7.4) ─────────────────────────────

        // ───────────── Перенос: переходное состояние Dragging (ТЗ 7.3, 7.5), в сохранение не попадает ─────────────

        int m_DragCatId;
        long m_DragPhaseTicks;
        int m_DragTargetIndex = -1;
        int m_DragCell = -1;

        /// <summary>Котик, который сейчас переносится; 0 — переноса нет.</summary>
        public int DraggingCatId => m_DragCatId;

        /// <summary>Исходная клетка переносимого котика (куда он вернётся при отмене); −1 — переноса нет.</summary>
        public int DragOriginCell => m_DragCatId != 0 ? m_DragCell : -1;

        /// <summary>
        /// Начать перенос котика (ТЗ 7.3, 7.5). В переносе котик не бьёт, фаза удара не растёт, ETA цели его не учитывает.
        /// Одновременно переносится один котик: активный перенос другого котика сначала отменяется. Событий и opId нет.
        /// </summary>
        public CommandResult BeginDrag(int catId)
        {
            var cat = FindCat(catId);
            if (cat == null) return CommandResult.NotFound;
            if (m_DragCatId != 0) CancelDrag();
            m_DragCatId = cat.Id;
            m_DragPhaseTicks = cat.PhaseTicks;
            m_DragTargetIndex = cat.TargetIndex;
            m_DragCell = cat.Cell;
            return CommandResult.Ok;
        }

        /// <summary>
        /// Отмена переноса с возвратом (пауза, потеря фокуса, отмена касания): котик остаётся в исходной клетке
        /// с прежним назначением, фаза удара — как до начала переноса, если назначение ещё допустимо (ТЗ 7.3, 7.5).
        /// </summary>
        public void CancelDrag()
        {
            if (m_DragCatId == 0) return;
            var cat = FindCat(m_DragCatId);
            if (cat != null) RestoreDragPhase(cat);
            ClearDrag();
        }

        /// <summary>Восстановить фазу до переноса, если котик всё ещё назначен на ту же допустимую цель и стоит на её рабочей клетке.</summary>
        void RestoreDragPhase(CatState cat)
        {
            if (m_DragCatId == 0 || cat.Id != m_DragCatId) return;
            if (m_DragTargetIndex < 0 || cat.TargetIndex != m_DragTargetIndex) return;
            int ord = OrdinalOf(m_DragTargetIndex);
            if (ord < 0 || !IsTargetAvailable(ord) || !IsWorkCellOf(ord, cat.Cell)) return;
            cat.PhaseTicks = m_DragPhaseTicks;
        }

        void ClearDrag()
        {
            m_DragCatId = 0;
            m_DragPhaseTicks = 0;
            m_DragTargetIndex = -1;
            m_DragCell = -1;
        }

        /// <summary>
        /// Отпускание котика в точке поля. gx, gy — координаты в клетках (столбец + доля, строка + доля).
        /// При отказе котик остаётся в исходной клетке с прежним назначением. Если котик взят командой <see cref="BeginDrag"/>,
        /// перенос завершается на любом пути; при отказе и при отпускании на свою рабочую клетку той же цели восстанавливается
        /// фаза удара до переноса, без мгновенного дополнительного удара (ТЗ 7.5).
        /// </summary>
        public DropOutcome DropCat(int catId, double gx, double gy)
        {
            bool dragging = catId != 0 && catId == m_DragCatId;
            var outcome = DropCatCore(catId, gx, gy, dragging);
            if (dragging)
            {
                if (outcome.Result != CommandResult.Ok)
                {
                    var cat = FindCat(catId);
                    if (cat != null) RestoreDragPhase(cat);
                }
                ClearDrag();
            }
            return outcome;
        }

        DropOutcome DropCatCore(int catId, double gx, double gy, bool dragging)
        {
            var outcome = new DropOutcome { Result = CommandResult.NotFound, TargetIndex = -1, Cell = -1 };
            var cat = FindCat(catId);
            if (cat == null || Player.CampaignCompleted) return outcome;
            outcome.Cell = cat.Cell;
            outcome.TargetIndex = cat.TargetIndex;
            int col = (int)Math.Floor(gx), row = (int)Math.Floor(gy);
            if (!Layout.InBounds(col, row))
            {
                outcome.Result = Reject(CommandResult.InvalidTarget);
                return outcome;
            }
            int cell = Layout.CellIndex(col, row);

            // Котик в клетке.
            int otherId = m_CellCat[cell];
            if (otherId != 0 && otherId != catId)
            {
                if (otherId == m_DragCatId)
                {
                    // Получатель в переносе недопустим (ТЗ 7.4): перетаскиваемый возвращается.
                    outcome.Result = Reject(CommandResult.NotAllowed);
                    return outcome;
                }
                var other = FindCat(otherId);
                if (other.Level != cat.Level)
                {
                    outcome.Result = Reject(CommandResult.DifferentLevels);
                    return outcome;
                }
                Merge(cat, other);
                outcome.Result = CommandResult.Ok;
                outcome.Merged = true;
                outcome.Cell = other.Cell;
                outcome.TargetIndex = other.TargetIndex;
                return outcome;
            }
            if (otherId == catId)
            {
                // Своя клетка считается свободной (ТЗ 7.3): котик остаётся на ней и назначается по правилу свободной клетки.
                // Та же цель — назначение и фаза до переноса сохраняются; другая смежная цель — новое назначение (фаза 0);
                // смежных целей нет — без изменений.
                int ownOrd = NearestAdjacentTarget(cell, gx, gy);
                if (ownOrd >= 0)
                {
                    int index = Layout.Objects[ownOrd].Index;
                    if (index == cat.TargetIndex)
                    {
                        if (dragging) RestoreDragPhase(cat);
                    }
                    else
                    {
                        Assign(cat, index, false);
                    }
                }
                outcome.Result = CommandResult.Ok;
                outcome.Cell = cat.Cell;
                outcome.TargetIndex = cat.TargetIndex;
                return outcome;
            }

            // Объект в клетке.
            int ord = ObjectAt(cell);
            if (ord >= 0)
            {
                if (!IsTargetAvailable(ord))
                {
                    outcome.Result = Reject(IsCellOpen(cell) ? CommandResult.InvalidTarget : CommandResult.ClosedSector);
                    return outcome;
                }
                var work = new List<int>(8);
                WorkCells(ord, work);
                int chosen = -1;
                if (work.Contains(cat.Cell)) chosen = cat.Cell;
                else
                {
                    int bestDist = int.MaxValue, bestOrigin = int.MaxValue;
                    foreach (int w in work)
                    {
                        if (m_CellCat[w] != 0 && m_CellCat[w] != catId) continue;
                        int d = Manhattan(w, cell);
                        int dOrigin = Manhattan(w, cat.Cell);
                        if (d < bestDist || (d == bestDist && dOrigin < bestOrigin) ||
                            (d == bestDist && dOrigin == bestOrigin && chosen >= 0 && Before(w, chosen)))
                        {
                            bestDist = d;
                            bestOrigin = dOrigin;
                            chosen = w;
                        }
                    }
                }
                if (chosen < 0)
                {
                    outcome.Result = Reject(CommandResult.NoSpaceAtTarget);
                    return outcome;
                }
                bool sameSpot = chosen == cat.Cell && cat.TargetIndex == Layout.Objects[ord].Index;
                MoveCat(cat, chosen);
                Assign(cat, Layout.Objects[ord].Index, sameSpot);
                // Своя рабочая клетка той же цели: фаза до переноса, без дополнительного удара (ТЗ 7.5).
                if (sameSpot && dragging) RestoreDragPhase(cat);
                outcome.Result = CommandResult.Ok;
                outcome.Cell = chosen;
                outcome.TargetIndex = cat.TargetIndex;
                return outcome;
            }

            if (m_CellContainer[cell] != 0)
            {
                outcome.Result = Reject(CommandResult.InvalidTarget);
                return outcome;
            }
            if (!IsCellOpen(cell))
            {
                outcome.Result = Reject(CommandResult.ClosedSector);
                return outcome;
            }

            // Свободная клетка: ставим и назначаем на смежную цель, если она принимает котика.
            MoveCat(cat, cell);
            int bestOrd = NearestAdjacentTarget(cell, gx, gy);
            if (bestOrd >= 0) Assign(cat, Layout.Objects[bestOrd].Index, false);
            else SetIdle(cat);
            Emit(new SimEvent { Type = SimEventType.CatMoved, CatId = cat.Id, Cell = cell, ObjectIndex = cat.TargetIndex });
            outcome.Result = CommandResult.Ok;
            outcome.Cell = cell;
            outcome.TargetIndex = cat.TargetIndex;
            return outcome;
        }

        /// <summary>
        /// Цель для котика на свободной клетке cell (ТЗ 7.3, таблица отпускания): смежная по стороне допустимая цель,
        /// для которой cell — рабочая клетка. Из нескольких — та, чья клетка footprint ближе к точке отпускания (gx, gy)
        /// в мировых координатах; при равенстве — меньший row, затем меньший column. −1 — смежных целей нет.
        /// </summary>
        int NearestAdjacentTarget(int cell, double gx, double gy)
        {
            int bestOrd = -1;
            double bestDistSq = double.MaxValue;
            int bestRow = int.MaxValue, bestCol = int.MaxValue;
            Layout.Neighbors4(cell, m_Tmp);
            // Копия: IsWorkCellOf тоже пользуется m_Tmp.
            var neighbours = new List<int>(m_Tmp);
            foreach (int n in neighbours)
            {
                int nOrd = ObjectAt(n);
                if (nOrd < 0 || !IsTargetAvailable(nOrd)) continue;
                if (!IsWorkCellOf(nOrd, cell)) continue;
                double cx = Layout.Col(n) + 0.5 - gx, cy = Layout.Row(n) + 0.5 - gy;
                double dsq = cx * cx + cy * cy;
                int r = Layout.Row(n), c = Layout.Col(n);
                if (dsq < bestDistSq - 1e-12 || (Math.Abs(dsq - bestDistSq) <= 1e-12 && (r < bestRow || (r == bestRow && c < bestCol))))
                {
                    bestDistSq = dsq;
                    bestOrd = nOrd;
                    bestRow = r;
                    bestCol = c;
                }
            }
            return bestOrd;
        }

        bool Before(int a, int b)
        {
            int ra = Layout.Row(a), rb = Layout.Row(b);
            if (ra != rb) return ra < rb;
            return Layout.Col(a) < Layout.Col(b);
        }

        int Manhattan(int a, int b) => Math.Abs(Layout.Col(a) - Layout.Col(b)) + Math.Abs(Layout.Row(a) - Layout.Row(b));

        /// <summary>Назначение. Новое назначение (другая цель или другая клетка) обнуляет фазу удара (ТЗ 7.5).</summary>
        void Assign(CatState cat, int targetIndex, bool keepPhase)
        {
            if (!keepPhase) cat.PhaseTicks = 0;
            cat.TargetIndex = targetIndex;
            Emit(new SimEvent { Type = SimEventType.CatAssigned, CatId = cat.Id, Cell = cat.Cell, ObjectIndex = targetIndex });
        }

        /// <summary>Объединение одной транзакцией: результат L+1 в клетке получателя (ТЗ 7.4).</summary>
        void Merge(CatState dragged, CatState receiver)
        {
            long op = NextOp();
            RemoveCat(dragged);
            receiver.Level += 1;
            receiver.PhaseTicks = 0;
            if (receiver.TargetIndex >= 0)
            {
                int ord = OrdinalOf(receiver.TargetIndex);
                if (!IsTargetAvailable(ord) || !IsWorkCellOf(ord, receiver.Cell))
                {
                    receiver.TargetIndex = -1;
                }
            }
            Mine.Merges++;
            Emit(new SimEvent { Type = SimEventType.CatMerged, OpId = op, CatId = receiver.Id, OtherCatId = dragged.Id, Cell = receiver.Cell, ObjectIndex = receiver.TargetIndex });
        }

        // ───────────────────────────── Шахты (ТЗ 9.2) ─────────────────────────────

        public CommandResult ActivateShaft(int ord)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            if (ord < 0 || ord >= Layout.Objects.Count) return Reject(CommandResult.NotFound);
            var obj = Layout.Objects[ord];
            var st = Mine.Objects[ord];
            if (obj.Kind != ObjectKind.Shaft || st.Status != ObjectStatus.ReadyToActivate) return Reject(CommandResult.NotAllowed);
            long op = NextOp();
            st.Status = ObjectStatus.Producing;
            st.Gen = new GeneratorState { Level = 1 };
            Mine.ActivatedShafts++;
            Emit(new SimEvent { Type = SimEventType.MineShaftActivated, OpId = op, ObjectIndex = obj.Index, Text = obj.TypeId });
            OpenChest(obj.RewardId, "shaft", op);
            CheckAutomation();
            UpdateUnlocks();
            return CommandResult.Ok;
        }

        public CommandResult CollectPayout(int ord)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            if (ord < 0 || ord >= Layout.Objects.Count) return CommandResult.NotFound;
            var st = Mine.Objects[ord];
            if (st.Gen == null || !st.Gen.PayoutReady) return CommandResult.NotAllowed;
            var amount = st.Gen.PendingPayout;
            st.Gen.PayoutReady = false;
            st.Gen.PendingPayout = GameNumber.Zero;
            st.Gen.CycleTicks = 0;
            AddGold(amount, "gold:shaft");
            Emit(new SimEvent { Type = SimEventType.PayoutCollected, OpId = NextOp(), ObjectIndex = Layout.Objects[ord].Index, Amount = amount });
            return CommandResult.Ok;
        }

        /// <summary>Автоматизация: уровень карты из токена s ≥ требуемого (ТЗ 9.2). Проверяется после активации и улучшения карт.</summary>
        void CheckAutomation()
        {
            for (int ord = 0; ord < Layout.Objects.Count; ord++)
            {
                var obj = Layout.Objects[ord];
                var st = Mine.Objects[ord];
                if (obj.Kind != ObjectKind.Shaft || st.Status != ObjectStatus.Producing || st.Gen == null || st.Gen.Automated) continue;
                if (CardLevel(obj.CardId) < obj.CardLevel) continue;
                st.Gen.Automated = true;
                if (st.Gen.PayoutReady)
                {
                    var amount = st.Gen.PendingPayout;
                    st.Gen.PayoutReady = false;
                    st.Gen.PendingPayout = GameNumber.Zero;
                    st.Gen.CycleTicks = 0;
                    AddGold(amount, "gold:shaft");
                }
                Emit(new SimEvent { Type = SimEventType.ShaftAutomated, OpId = NextOp(), ObjectIndex = obj.Index, Text = obj.CardId });
            }
        }

        /// <summary>Есть ручная производящая шахта, которую <see cref="CheckAutomation"/> автоматизировала бы сейчас.</summary>
        bool IsAutomationDue()
        {
            for (int ord = 0; ord < Layout.Objects.Count; ord++)
            {
                var obj = Layout.Objects[ord];
                var st = Mine.Objects[ord];
                if (obj.Kind != ObjectKind.Shaft || st.Status != ObjectStatus.Producing || st.Gen == null || st.Gen.Automated) continue;
                if (CardLevel(obj.CardId) >= obj.CardLevel) return true;
            }
            return false;
        }

        /// <summary>Причина, по которой ручная шахта не автоматизирована.</summary>
        public string AutomationRequirement(int ord)
        {
            var obj = Layout.Objects[ord];
            if (obj.Kind != ObjectKind.Shaft) return null;
            int have = CardLevel(obj.CardId);
            return Loc.F("core.automation.requirement", obj.CardId, obj.CardLevel,
                         have > 0 ? Loc.F("core.automation.have_level", have) : Loc.T("core.automation.have_none"));
        }

        // ───────────────────────────── Улучшения генераторов (ТЗ 9.3) ─────────────────────────────

        public CommandResult UpgradeGenerator(int ord, out int levelsBought, int maxLevels = 1)
        {
            levelsBought = 0;
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            if (ord < 0 || ord >= Layout.Objects.Count || !IsGenerator(ord)) return Reject(CommandResult.NotFound);
            var gen = Mine.Objects[ord].Gen;
            int max = MaxGeneratorLevel(ord);
            if (gen.Level >= max) return Reject(CommandResult.MaxLevel);
            var spent = GameNumber.Zero;
            while (levelsBought < maxLevels && gen.Level < max)
            {
                var cost = UpgradeCost(ord, gen.Level);
                if (Mine.Gold < cost) break;
                Mine.Gold -= cost;
                spent += cost;
                gen.Level++;
                levelsBought++;
                ApplyThresholds(ord);
            }
            if (levelsBought == 0) return Reject(CommandResult.NotEnoughGold);
            Ledger("gold:spent:upgrade", spent);
            Emit(new SimEvent { Type = SimEventType.GeneratorUpgraded, OpId = NextOp(), ObjectIndex = Layout.Objects[ord].Index, Amount = spent, Text = gen.Level.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return CommandResult.Ok;
        }

        /// <summary>«До порога»: сколько уровней до ближайшего порога включительно доступно и их точная суммарная стоимость.</summary>
        public void PreviewToThreshold(int ord, out int levels, out GameNumber totalCost)
        {
            levels = 0;
            totalCost = GameNumber.Zero;
            if (!IsGenerator(ord)) return;
            var gen = Mine.Objects[ord].Gen;
            int target = NextThresholdLevel(ord);
            var gold = Mine.Gold;
            for (int L = gen.Level; L < target; L++)
            {
                var cost = UpgradeCost(ord, L);
                if (gold < totalCost + cost) break;
                totalCost += cost;
                levels++;
            }
        }

        public CommandResult UpgradeToThreshold(int ord, out int levelsBought)
        {
            if (Player.CampaignCompleted)
            {
                levelsBought = 0;
                return Reject(CommandResult.NotAllowed);
            }
            PreviewToThreshold(ord, out int levels, out _);
            if (levels <= 0)
            {
                levelsBought = 0;
                return Reject(IsGenerator(ord) && Mine.Objects[ord].Gen.Level >= MaxGeneratorLevel(ord) ? CommandResult.MaxLevel : CommandResult.NotEnoughGold);
            }
            return UpgradeGenerator(ord, out levelsBought, levels);
        }

        void ApplyThresholds(int ord)
        {
            var def = DefFor(Layout.Objects[ord]);
            var gen = Mine.Objects[ord].Gen;
            var zoneMult = Balance.ZoneMultipliers(Mine.Zone).GenObjectiveSoftCurrencyMultiplier;
            while (gen.ThresholdsReached < def.Thresholds.Length && gen.Level >= def.Thresholds[gen.ThresholdsReached])
            {
                int k = gen.ThresholdsReached + 1;
                var o = def.Objectives;
                var elixir = Formula.Payout(Formula.Evaluate(o.SoftCurrencyRewardFormulaType, o.SoftCurrencyRewardBase, o.SoftCurrencyRewardMultiplier, o.SoftCurrencyRewardGrowth, k) * GameNumber.FromDouble(zoneMult));
                gen.ThresholdsReached++;
                AddElixir(elixir, "elixir:threshold");
                Emit(new SimEvent
                {
                    Type = SimEventType.ThresholdReached,
                    ObjectIndex = Layout.Objects[ord].Index,
                    Amount = GameNumber.FromDouble(def.Objectives.CoreCurrencyMultiplier[k - 1]),
                    Text = Loc.F("core.threshold.reward", NumberFormat.Format(def.Objectives.CoreCurrencyMultiplier[k - 1]), NumberFormat.Format(elixir)),
                });
            }
        }

        // ───────────────────────────── Ворота и выход (ТЗ 11) ─────────────────────────────

        public CommandResult OpenGate(int ord)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            if (ord < 0 || ord >= Layout.Objects.Count) return Reject(CommandResult.NotFound);
            var obj = Layout.Objects[ord];
            var st = Mine.Objects[ord];
            if (obj.Kind != ObjectKind.Gate || st.Status != ObjectStatus.Broken) return Reject(CommandResult.NotAllowed);
            if (obj.GateOrder != Mine.GatesOpened) return Reject(CommandResult.NotAllowed);
            long op = NextOp();
            st.Status = ObjectStatus.Opened;
            double oldInterval = FreeCatIntervalSeconds();
            Mine.GatesOpened++;
            double newInterval = FreeCatIntervalSeconds();
            Mine.FreeCatTimerTicks = Math.Max(0, Mine.FreeCatTimerTicks - SecondsToTicks(oldInterval - newInterval));
            Emit(new SimEvent { Type = SimEventType.CheckpointOpened, OpId = op, ObjectIndex = obj.Index });
            OpenChest(obj.RewardId, "gate", op);
            TrySpawnPending();
            return CommandResult.Ok;
        }

        public bool CanCompleteZone(out string reason)
        {
            reason = null;
            if (Player.CampaignCompleted) { reason = Loc.T("core.complete.campaign_completed"); return false; }
            var exit = Mine.Objects[Layout.Exit.Ordinal];
            if (exit.Status != ObjectStatus.Broken) { reason = Loc.T("core.complete.exit_closed"); return false; }
            int total = Layout.Shafts.Count, active = 0;
            foreach (var s in Layout.Shafts)
                if (Mine.Objects[s.Ordinal].Status == ObjectStatus.Producing) active++;
            if (active < total) { reason = Loc.F("core.complete.shafts_inactive", active, total); return false; }
            if (Mine.GatesOpened < Layout.Gates.Count) { reason = Loc.T("core.complete.gates_closed"); return false; }
            return true;
        }

        /// <summary>
        /// Оставшиеся целые камни для подтверждения перехода (ТЗ 11.2), кроме камней только с наградами исключённых систем
        /// (правило из данных — <see cref="IsExcludedRewardRock"/>, ТЗ 4.1).
        /// </summary>
        public int RemainingRocks()
        {
            int n = 0;
            for (int ord = 0; ord < Layout.Objects.Count; ord++)
                if (Layout.Objects[ord].Kind == ObjectKind.Rock && Mine.Objects[ord].Status == ObjectStatus.Intact &&
                    !IsExcludedRewardRock(Layout.Objects[ord].TypeId)) n++;
            return n;
        }

        /// <summary>Оставшиеся целые сундучные камни (каждая запись цикла наград — сундук), для подтверждения перехода (ТЗ 11.2).</summary>
        public int RemainingChestRocks()
        {
            int n = 0;
            for (int ord = 0; ord < Layout.Objects.Count; ord++)
                if (Layout.Objects[ord].Kind == ObjectKind.Rock && Mine.Objects[ord].Status == ObjectStatus.Intact &&
                    IsChestRock(Layout.Objects[ord].TypeId)) n++;
            return n;
        }

        /// <summary>Неактивированная шахта, ближайшая к низу карты (для перевода камеры).</summary>
        public int NearestInactiveShaft()
        {
            int best = -1;
            foreach (var s in Layout.Shafts)
            {
                if (Mine.Objects[s.Ordinal].Status == ObjectStatus.Producing) continue;
                if (best < 0 || s.Row > Layout.Objects[best].Row) best = s.Ordinal;
            }
            return best;
        }

        /// <summary>
        /// Неактивированная шахта, ближайшая к строке refRow (например, к строке камеры или выхода) — ТЗ 11.2:
        /// «переход камеры к ближайшей неактивированной шахте». Минимум |Row − refRow|, при равенстве — меньший ordinal; −1 — нет.
        /// </summary>
        public int NearestInactiveShaft(int refRow)
        {
            int best = -1, bestDist = int.MaxValue;
            foreach (var s in Layout.Shafts)
            {
                if (Mine.Objects[s.Ordinal].Status == ObjectStatus.Producing) continue;
                int d = Math.Abs(s.Row - refRow);
                if (d < bestDist || (d == bestDist && s.Ordinal < best))
                {
                    bestDist = d;
                    best = s.Ordinal;
                }
            }
            return best;
        }

        /// <summary>Завершение зоны одной транзакцией CompleteZone (ТЗ 11.2).</summary>
        public CommandResult CompleteZone()
        {
            if (!CanCompleteZone(out _)) return Reject(CommandResult.NotAllowed);
            long op = NextOp();
            int zone = Mine.Zone;
            OpenChest(Layout.Exit.RewardId, "exit", op);
            // Переносимое содержимое неоткрытых ящиков доставки: эликсир и динамит.
            foreach (var c in Mine.Containers)
            {
                if (c.Kind != ContainerKind.Delivery) continue;
                if (c.RewardType == RewardTypes.Currency && c.DetailedType == "Soft") AddElixir(c.Amount, "elixir:delivery");
                else if (c.RewardType == RewardTypes.Spell) Player.Dynamite = Math.Min(MaxDynamite, Player.Dynamite + 1);
            }
            // Несобранная добыча: эликсир и кристаллы переносятся (начисляются без событий, как ящики доставок),
            // золото остаётся в зоне и пропадает вместе с ней.
            foreach (var l in Mine.Loot)
                if (l.Currency != LootState.Gold) CreditLoot(l);
            Mine.Loot.Clear();
            Player.HighestCompletedZone = Math.Max(Player.HighestCompletedZone, zone);
            Player.ZoneTimeTicks[ZoneKey(zone)] = Mine.ZoneTimeTicks;
            Emit(new SimEvent { Type = SimEventType.MineCompleted, OpId = op, ObjectIndex = -1, Text = ZoneKey(zone) });
            if (zone >= Balance.ZoneCount)
            {
                Player.CampaignCompleted = true;
                Emit(new SimEvent { Type = SimEventType.CampaignCompleted, OpId = op, ObjectIndex = -1 });
            }
            else
            {
                EnterZone(zone + 1);
            }
            return CommandResult.Ok;
        }

        // ───────────────────────────── Контейнеры, бесплатный сундук, динамит ─────────────────────────────

        public CommandResult OpenContainer(int containerId)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            ContainerState box = null;
            foreach (var c in Mine.Containers) if (c.Id == containerId) box = c;
            if (box == null) return CommandResult.NotFound;
            long op = NextOp();
            Mine.Containers.Remove(box);
            m_CellContainer[box.Cell] = 0;
            if (box.Kind == ContainerKind.FreeCat)
            {
                var cat = SpawnCat(Math.Max(1, box.CatLevel), box.Cell);
                Mine.FreeCatTimerTicks = SecondsToTicks(FreeCatIntervalSeconds());
                Emit(new SimEvent { Type = SimEventType.CatSpawned, OpId = op, CatId = cat.Id, Cell = box.Cell, ObjectIndex = -1, Text = "free" });
            }
            else if (box.Kind == ContainerKind.Hire)
            {
                // Бочка найма: золото списано при найме, котик в Idle в клетке бочки.
                var cat = SpawnCat(Math.Max(1, box.CatLevel), box.Cell);
                Emit(new SimEvent { Type = SimEventType.CatSpawned, OpId = op, CatId = cat.Id, Cell = box.Cell, ObjectIndex = -1, Text = "hire" });
            }
            else
            {
                switch (box.RewardType)
                {
                    case RewardTypes.Miner:
                        var cat = SpawnCat(Math.Max(1, box.CatLevel), box.Cell);
                        Emit(new SimEvent { Type = SimEventType.CatSpawned, OpId = op, CatId = cat.Id, Cell = box.Cell, ObjectIndex = -1, Text = "delivery" });
                        break;
                    case RewardTypes.Currency:
                        if (box.DetailedType == "Core") AddGold(box.Amount, "gold:delivery");
                        else if (box.DetailedType == "Soft") AddElixir(box.Amount, "elixir:delivery");
                        break;
                    case RewardTypes.Spell:
                        if (Player.Dynamite < MaxDynamite) Player.Dynamite++;
                        else Player.ExcludedRewards["DynamiteOverflow"] = (Player.ExcludedRewards.TryGetValue("DynamiteOverflow", out var v) ? v : 0) + 1;
                        break;
                }
                Player.DeliveryHistory.ClaimTimes.Add(Player.PlayerTimeTicks);
                Mine.DeliveryTimerTicks = SecondsToTicks(NextDeliveryIntervalSeconds(true));
            }
            string text = box.Kind == ContainerKind.FreeCat ? "FreeCat" : box.Kind == ContainerKind.Hire ? "Hire" : box.DetailedType;
            Emit(new SimEvent { Type = SimEventType.ContainerOpened, OpId = op, Cell = box.Cell, ObjectIndex = -1, CatId = box.Id, Amount = box.Amount, Text = text });
            return CommandResult.Ok;
        }

        /// <summary>
        /// Сбор добычи тапом (<see cref="LootPickups"/>): начисление с ключом журнала камня ("gold:rock", "elixir:rock", "hard:rock"),
        /// события RewardGranted (как при прежнем мгновенном начислении) и LootCollected (OtherCatId = 0).
        /// </summary>
        public CommandResult CollectLoot(int lootId)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            int i = LootSlot(lootId);
            if (i < 0) return CommandResult.NotFound;
            CollectLootAt(i, false, NextOp());
            return CommandResult.Ok;
        }

        public CommandResult ClaimFreeChest()
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            var fc = Player.FreeChest;
            if (!fc.Enabled || fc.Stock <= 0) return Reject(CommandResult.NotAllowed);
            var ids = Balance.FreeGachaCycle.GachaIds;
            string gachaId = ids[fc.NextIndex % ids.Length];
            fc.NextIndex++;
            bool wasFull = fc.Stock >= Balance.FreeGachaCycle.MaxFreeGachas;
            fc.Stock--;
            if (wasFull) fc.TimerTicks = FreeChestPeriodTicks;
            OpenChest(gachaId, "free", NextOp());
            return CommandResult.Ok;
        }

        public int DynamiteLevel()
        {
            int max = 0;
            foreach (var c in Mine.Cats) if (c.Level > max) max = c.Level;
            return Math.Max(1, max);
        }

        public GameNumber DynamiteDamage()
        {
            var sp = Balance.Spell;
            return Formula.Evaluate(sp.EffectFormulaType, sp.EffectBase, sp.EffectMultiplier, sp.EffectGrowth, DynamiteLevel());
        }

        /// <summary>Однократный удар динамитом по допустимой цели (ТЗ 10.6). На недопустимом месте заряд не тратится.</summary>
        public CommandResult UseDynamite(double gx, double gy)
        {
            if (Player.CampaignCompleted) return Reject(CommandResult.NotAllowed);
            if (Player.Dynamite <= 0) return Reject(CommandResult.NotAllowed);
            int col = (int)Math.Floor(gx), row = (int)Math.Floor(gy);
            if (!Layout.InBounds(col, row)) return Reject(CommandResult.InvalidTarget);
            int ord = ObjectAt(Layout.CellIndex(col, row));
            if (ord < 0 || !IsTargetAvailable(ord)) return Reject(CommandResult.InvalidTarget);
            Player.Dynamite--;
            var sp = Balance.Spell;
            int level = DynamiteLevel();
            var damage = DynamiteDamage();
            double chance = Formula.Evaluate(sp.CritChanceFormulaType, sp.CritChanceBase, sp.CritChanceMultiplier, sp.CritChanceGrowth, level).ToDouble();
            bool crit = false;
            if (chance > 0 && Player.Rng.Crit.NextDouble() < chance)
            {
                crit = true;
                damage *= Formula.Evaluate(sp.CritPowerFormulaType, sp.CritPowerBase, sp.CritPowerMultiplier, sp.CritPowerGrowth, level);
            }
            Emit(new SimEvent { Type = SimEventType.DynamiteUsed, OpId = NextOp(), ObjectIndex = Layout.Objects[ord].Index, Amount = damage, Crit = crit });
            ApplyDamage(ord, damage, 0, crit);
            return CommandResult.Ok;
        }

        // ───────────────────────────── Карточки (ТЗ 10.1, E-14) ─────────────────────────────

        /// <summary>
        /// Стоимость улучшения карты с текущего уровня (E-14): копии и эликсир. Единственное место поиска цены —
        /// её же проверяет и списывает <see cref="UpgradeCard"/>, UI берёт её отсюда (ТЗ 15.1).
        /// false — карты нет в данных, у игрока её нет (уровень &lt; 1) или уровень максимальный.
        /// </summary>
        public bool TryGetCardUpgradeCost(string cardId, out int duplicates, out GameNumber elixir)
        {
            duplicates = 0;
            elixir = GameNumber.Zero;
            if (cardId == null || !Balance.Cards.TryGetValue(cardId, out var card)) return false;
            int level = CardLevel(cardId);
            if (level < 1 || level >= Balance.CardMaxLevel(card.Rarity)) return false;
            if (!Balance.CardUpgradeCosts.TryGetValue(card.Rarity, out var costs)) return false;
            duplicates = costs.Duplicates[level - 1];
            elixir = GameNumber.FromDouble(costs.SoftCurrency[level - 1]);
            return true;
        }

        public CommandResult CanUpgradeCard(string cardId)
        {
            if (Player.CampaignCompleted) return CommandResult.NotAllowed;
            if (!Balance.Cards.TryGetValue(cardId, out var card)) return CommandResult.NotFound;
            if (!IsAppliedCardCode(card.StatModifierType)) return CommandResult.EffectNotAvailable;
            int level = CardLevel(cardId);
            if (level < 1) return CommandResult.NotEnoughDuplicates;
            if (!TryGetCardUpgradeCost(cardId, out int duplicates, out var elixir)) return CommandResult.MaxLevel;
            var cs = CardStateOf(cardId);
            if (cs.Duplicates < duplicates) return CommandResult.NotEnoughDuplicates;
            if (Player.Elixir < elixir) return CommandResult.NotEnoughElixir;
            return CommandResult.Ok;
        }

        public CommandResult UpgradeCard(string cardId)
        {
            var check = CanUpgradeCard(cardId);
            if (check != CommandResult.Ok) return Reject(check);
            TryGetCardUpgradeCost(cardId, out int duplicates, out var elixir);
            var cs = CardStateOf(cardId);
            cs.Duplicates -= duplicates;
            Player.Elixir -= elixir;
            Ledger("elixir:spent:cards", elixir);
            cs.Level++;
            Emit(new SimEvent { Type = SimEventType.CardUpgraded, OpId = NextOp(), ObjectIndex = -1, Text = cardId, Amount = GameNumber.FromDouble(cs.Level) });
            CheckAutomation();
            return CommandResult.Ok;
        }

        // ───────────────────────────── Настройки (ТЗ 11.3, 13.1) ─────────────────────────────

        /// <summary>Звук вкл./выкл. Настройка хранится в состоянии игрока и переносится между зонами; экономику не меняет.</summary>
        public void SetSoundOn(bool on)
        {
            if (Player.Settings == null) Player.Settings = new PlayerSettingsState();
            Player.Settings.SoundOn = on;
        }

        /// <summary>Вибрация вкл./выкл. Настройка хранится в состоянии игрока и переносится между зонами; экономику не меняет.</summary>
        public void SetVibrationOn(bool on)
        {
            if (Player.Settings == null) Player.Settings = new PlayerSettingsState();
            Player.Settings.VibrationOn = on;
        }

        /// <summary>Громкость группы звука 0…1 (Master, Music, Sfx, Ambience). Настройка игрока; экономику не меняет.</summary>
        public void SetVolume(AudioGroup group, float value)
        {
            if (Player.Settings == null) Player.Settings = new PlayerSettingsState();
            value = Math.Max(0f, Math.Min(1f, float.IsNaN(value) ? 1f : value));
            switch (group)
            {
                case AudioGroup.Master: Player.Settings.MasterVolume = value; break;
                case AudioGroup.Music: Player.Settings.MusicVolume = value; break;
                case AudioGroup.Sfx: Player.Settings.SfxVolume = value; break;
                case AudioGroup.Ambience: Player.Settings.AmbienceVolume = value; break;
            }
        }

        /// <summary>Уменьшение эффектов вкл./выкл.: меньше частиц, без импульса камеры. Только вид, экономику не меняет.</summary>
        public void SetReduceEffects(bool on)
        {
            if (Player.Settings == null) Player.Settings = new PlayerSettingsState();
            Player.Settings.ReduceEffects = on;
        }

        // ───────────────────────────── Инструменты разработки (только слот Debug) ─────────────────────────────

        public bool IsDebugSlot => State.Slot == SaveSlots.Debug;

        public void DebugAddGold(GameNumber amount)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            AddGold(amount, "debug");
        }

        public void DebugAddElixir(GameNumber amount)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            AddElixir(amount, "debug");
        }

        public bool DebugSpawnCat(int level)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            int cell = FindSpawnCell();
            if (cell < 0) return false;
            var cat = SpawnCat(Math.Max(1, level), cell);
            Emit(new SimEvent { Type = SimEventType.CatSpawned, CatId = cat.Id, Cell = cell, ObjectIndex = -1, Text = "debug" });
            return true;
        }

        public void DebugCompleteTarget(int ord)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            if (ord < 0 || !Layout.Objects[ord].IsTarget || Mine.Objects[ord].Status != ObjectStatus.Intact) return;
            Mine.Objects[ord].Hp = GameNumber.Zero;
            CompleteTarget(ord);
        }

        public void DebugGiveZoneCards()
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            foreach (var s in Layout.Shafts)
            {
                var cs = CardStateOf(s.CardId);
                if (cs.Level < s.CardLevel) cs.Level = s.CardLevel;
            }
            CheckAutomation();
        }

        public void DebugGotoZone(int zone)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            Player.CampaignCompleted = false;
            EnterZone(Math.Max(1, Math.Min(Balance.ZoneCount, zone)));
        }

        public void DebugAddDynamite(int n)
        {
            if (!IsDebugSlot) throw new InvalidOperationException("Инструменты доступны только в слоте Debug");
            Player.Dynamite = Math.Max(0, Math.Min(MaxDynamite, Player.Dynamite + n));
        }
    }
}
