using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Первая сессия в ядре (Docs/Rework/FIRST_SESSION.md): открытие функций по фактам шахты (<see cref="ProgressionService.Sync"/>
    /// после первой доставки, покупки «Кирок», карточек и завершения), доступность веток в котировке и покупке, места разгрузки
    /// (рост <see cref="OutpostRun.Slots"/>), спасение персонажа при разрушении замка и маршрут к его клетке, который котики
    /// расчищают первым, пока клетка закрыта.
    /// </summary>
    public sealed partial class OutpostSimulation
    {
        /// <summary>Индекс цели → цель маршрута к замку клетки (или сам замок); null — в шахте нет спасения.</summary>
        bool[] m_RouteTarget;
        /// <summary>Индекс цели-замка или −1.</summary>
        int m_RescueLockIndex = -1;
        int m_RouteFocus;

        void InitProgression()
        {
            m_RescueLockIndex = -1;
            m_RouteTarget = null;
            var rescue = m_Map.Rescue;
            if (rescue == null) return;
            m_RescueLockIndex = m_Map.TargetIndex(rescue.LockTargetId);
            if (m_RescueLockIndex < 0) return;
            m_RouteFocus = Math.Max(0, rescue.RouteFocus);
            m_RouteTarget = new bool[m_Map.Targets.Length];
            m_RouteTarget[m_RescueLockIndex] = true;
            if (rescue.RouteTargetIds != null)
                foreach (int id in rescue.RouteTargetIds)
                {
                    int i = m_Map.TargetIndex(id);
                    if (i >= 0) m_RouteTarget[i] = true;
                }
        }

        /// <summary>В шахте есть клетка с персонажем (первая сессия).</summary>
        public bool HasRescue => m_RescueLockIndex >= 0;

        /// <summary>Клетка ещё закрыта: замок цел.</summary>
        public bool RescuePending => m_RescueLockIndex >= 0 && !Run.Targets[m_RescueLockIndex].Destroyed;

        /// <summary>
        /// Шаги спасения по фактам карты: разрушено целей маршрута из всех (без замка) и цел ли замок. Без спасения — (0, 0, false).
        /// </summary>
        public (int Cleared, int Total, bool LockOpen) RescueProgress()
        {
            var rescue = m_Map.Rescue;
            if (rescue == null || m_RescueLockIndex < 0) return (0, 0, false);
            int cleared = 0, total = 0;
            if (rescue.RouteTargetIds != null)
                foreach (int id in rescue.RouteTargetIds)
                {
                    int i = m_Map.TargetIndex(id);
                    if (i < 0) continue;
                    total++;
                    if (Run.Targets[i].Destroyed) cleared++;
                }
            return (cleared, total, Run.Targets[m_RescueLockIndex].Destroyed);
        }

        /// <summary>
        /// Ближайшая к базе неразрушенная цель маршрута к клетке, до которой уже есть путь (фронт расчистки); −1 — нет (путь
        /// расчищен или клетки нет). Камера блока цели HUD подводит к ней.
        /// </summary>
        public int RescueFrontierTarget()
        {
            var rescue = m_Map.Rescue;
            if (!RescuePending) return -1;
            var field = Navigation.From(m_Map.IntakeCell);
            var ids = new List<int>(rescue.RouteTargetIds ?? new List<int>()) { rescue.LockTargetId };
            foreach (int id in ids)
            {
                int i = m_Map.TargetIndex(id);
                if (i < 0 || Run.Targets[i].Destroyed) continue;
                foreach (int wc in m_Map.Targets[i].WorkCells)
                    if (Navigation.Passable(wc) && field.Dist[wc] >= 0) return id;
            }
            return -1;
        }

        /// <summary>Предпочтение цели маршрута при выборе работы (клеток пути); 0 — не цель маршрута или клетка уже открыта.</summary>
        int RouteBonus(int targetIndex) =>
            m_RouteTarget != null && m_RouteTarget[targetIndex] && RescuePending ? m_RouteFocus : 0;

        /// <summary>Замок клетки разрушен: персонаж — в команду профиля (один раз), событие для сцены спасения.</summary>
        void OnRescueLockDestroyed(MapTarget lockTarget, long t)
        {
            var rescue = m_Map.Rescue;
            bool added = ProgressionService.Rescue(Profile, rescue.Character);
            Run.PlanningDirty = true;
            Touch(t);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.CharacterRescued, t);
                e.ItemId = rescue.Character;
                e.TargetId = lockTarget.Id;
                e.Cell = m_Map.RescueCageCell;
                e.Count = added ? 1 : 0;
                m_Events.Add(e);
            }
            SyncProgression(t);
        }

        /// <summary>Факты шахты → постоянные отметки открытия; новые — событиями FeatureUnlocked.</summary>
        void SyncProgression(long t)
        {
            var added = ProgressionService.Sync(Profile, Run, m_Configs);
            if (added == null || !RecordEvents) return;
            foreach (string id in added)
            {
                var e = OutpostEvent.Create(OutpostEventType.FeatureUnlocked, t);
                e.ItemId = id;
                m_Events.Add(e);
            }
        }

        /// <summary>Наибольший уровень ветки по раскладке шахты: места разгрузки — клетки у склада сверх начальных; иначе без ограничения.</summary>
        int? CapLimit(UpgradeBranch branch) =>
            branch == UpgradeBranch.Bays ? Math.Max(0, m_Map.DepotCells.Length - OutpostIds.DepotSlotCount) : (int?)null;

        /// <summary>Ветка ещё не открыта первой сессией: котировка с ценой и пределом, но покупка — "locked".</summary>
        UpgradeQuote GateQuote(UpgradeBranch branch, UpgradeQuote q)
        {
            if (ProgressionService.BranchAvailable(Profile, m_Configs, branch)) return q;
            q.Levels = 0;
            q.Cost = System.Numerics.BigInteger.Zero;
            q.BlockReason = "locked";
            return q;
        }

        /// <summary>Места разгрузки после покупки ветки: по месту на уровень сверх начальных (новые места свободны).</summary>
        void SyncDepotSlots()
        {
            var run = Run;
            int want = Math.Min(m_Map.DepotCells.Length, OutpostIds.DepotSlotCount + run.Level(UpgradeBranch.Bays));
            if (run.Slots.Length >= want) return;
            var slots = new DepotSlotState[want];
            Array.Copy(run.Slots, slots, run.Slots.Length);
            for (int i = run.Slots.Length; i < want; i++) slots[i] = new DepotSlotState();
            run.Slots = slots;
        }
    }
}
