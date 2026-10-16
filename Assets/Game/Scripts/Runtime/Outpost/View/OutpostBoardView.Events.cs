using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    // События ядра → эффекты вида (RUNTIME_SPEC §4.4) с бюджетом на кадр. Вызывать для каждого события кадра ДО Sync:
    // виды, созданные здесь (нанятый котик), Sync не пересоздаёт. Ничего не меняет в ядре; звук — через Cue.
    public sealed partial class OutpostBoardView
    {
        /// <summary>То же с проверкой: событие симуляции, для карты которой поле не построено (новая шахта до Build), пропускается.</summary>
        public void OnEvent(OutpostSimulation sim, OutpostEvent e, Effects fx)
        {
            if (!Matches(sim)) return;
            m_Sim = sim;
            OnEvent(e, fx);
        }

        /// <summary>
        /// Показать событие ядра: удар (Strike, сколы, контакт), выпуск и подбор груза (полёты мешочков), сдача на склад
        /// («+N», монеты, ящик), разрушение и расчистка, открытие сектора, сбор команды, вспышка постройки, найм, смена экипировки.
        /// Полёт монет к кошельку HUD запускает контроллер по CargoDeposited из <see cref="DepotIndicatorWorldPosition"/>.
        /// </summary>
        public void OnEvent(OutpostEvent e, Effects fx)
        {
            if (m_Map == null || m_Sim == null || m_Targets == null) return;
            if (fx != null) Fx = fx;
            else fx = Fx;
            var run = m_Sim.Run;
            var origin = Origin;
            switch (e.Type)
            {
                case OutpostEventType.TargetHit:
                    OnTargetHit(e, fx, run, origin);
                    break;
                case OutpostEventType.CargoReleased:
                {
                    var tv = TargetViewOf(e.TargetId);
                    var from = tv != null ? tv.FeetLocal + new Vector3(0f, 0f, Mathf.Min(tv.TopZ, tv.DepthCells) * 0.6f) : CellCenterLocal(Mathf.Max(0, e.Cell));
                    if (e.BatchSeq >= 0 && e.Cell >= 0 && e.Cell < m_Map.CellCount)
                    {
                        // Остаток не поместился в сумки — партия на земле: мешочек вылетает в клетку, куча растёт по приземлении.
                        var pile = EnsurePile(e.Cell);
                        pile.HoldIncrease(ReleaseFlight);
                        m_Flights.Launch(from, CellCenterLocal(e.Cell) + new Vector3(0f, 0f, CargoPileView.FeetY + 4f * Px), ReleaseFlight, ReleaseArc);
                    }
                    var cat = CatViewOf(e.WorkerId);
                    if (cat != null && (e.BatchSeq < 0 || CatGotCargo(e.WorkerId)))
                        m_Flights.Launch(from, cat.LocalPosition, ReleaseFlight * 0.8f, ReleaseArc * 0.7f, cat, OutpostCatView.FeetY + OutpostCatView.BagY);
                    if (Cue != null)
                    {
                        var args = OutpostViewCueArgs.Create(OutpostViewCue.SackPop, origin + from);
                        args.WorkerId = e.WorkerId;
                        args.TargetId = e.TargetId;
                        args.Count = e.Count;
                        args.Value = e.Value;
                        Cue(args);
                    }
                    break;
                }
                case OutpostEventType.CargoPickedUp:
                {
                    var cat = CatViewOf(e.WorkerId);
                    if (cat != null && e.Cell >= 0 && e.Cell < m_Map.CellCount)
                        m_Flights.Launch(CellCenterLocal(e.Cell) + new Vector3(0f, 0f, CargoPileView.PileCenterY), cat.LocalPosition, PickupFlight, PickupArc,
                            cat, OutpostCatView.FeetY + OutpostCatView.BagY);
                    Emit(OutpostViewCue.Pickup, cat != null ? cat.LocalPosition : CellCenterLocal(Mathf.Max(0, e.Cell)), e.WorkerId, e.TargetId);
                    break;
                }
                case OutpostEventType.CargoDeposited:
                    OnDeposited(e, fx, run, origin);
                    break;
                case OutpostEventType.TargetDestroyed:
                {
                    var tv = TargetViewOf(e.TargetId);
                    // Работники этой цели (по последней синхронизации) радуются.
                    for (int i = 0; i < m_Cats.Count && i < m_Track.Length; i++)
                        if (m_Cats[i] != null && m_Track[i].MiningTarget == e.TargetId) m_Cats[i].PlayJoy();
                    // Цепи завода: здание стоит, разлёт звеньев и звук — по FactoryUnlocked на той же отметке.
                    if (tv == null || tv.IsFactory) break;
                    var center = origin + tv.CenterLocal;
                    if (fx != null && TakeFx())
                    {
                        fx.Puff(center);
                        fx.Burst(center, tv.BaseColor, tv.BurstCount);
                    }
                    if (Cue != null)
                    {
                        var args = OutpostViewCueArgs.Create(OutpostViewCue.TargetBroken, center);
                        args.TargetId = e.TargetId;
                        args.Material = tv.Material;
                        Cue(args);
                    }
                    break;
                }
                case OutpostEventType.SectorOpened:
                {
                    int order = e.Count - 1;
                    if (order >= 0 && order < m_Barriers.Length)
                    {
                        m_Barriers[order].PlayOpen(fx, origin);
                        var gate = m_Map.Gates[order];
                        if (fx != null)
                        {
                            // Свет в новом секторе (без белой вспышки): над строкой ворот; затемнение гаснет в Sync.
                            fx.Glow(m_Map.Topology != null ? GateWorldPosition(order) : origin + new Vector3(m_Map.Width / 2f, 0f, m_Map.Depth - gate.Row + 1.5f), PixelPalette.Amber4, RevealGlowRadius, RevealGlowSeconds);
                            fx.Sparkle(GateWorldPosition(order), 4);
                        }
                    }
                    if (Cue != null)
                    {
                        var args = OutpostViewCueArgs.Create(OutpostViewCue.SectorOpened, order >= 0 ? GateWorldPosition(order) : BaseWorldPosition);
                        args.TargetId = e.TargetId;
                        args.Count = e.Count;
                        Cue(args);
                    }
                    break;
                }
                case OutpostEventType.StageCompleted:
                    StartGather();
                    m_Camp?.MarkWardrobeDirty();
                    if (fx != null && m_Camp != null) fx.Sparkle(origin + m_Camp.CenterLocal + new Vector3(0f, 0f, 1f), 5);
                    Emit(OutpostViewCue.TeamGathered, m_Camp != null ? m_Camp.CenterLocal : new Vector3(Width / 2f, 0f, -3f));
                    break;
                case OutpostEventType.TeamUpgradePurchased:
                    if (m_Camp != null && e.Branch >= 0)
                    {
                        m_Camp.Flash((UpgradeBranch)e.Branch, fx, origin);
                        if (Cue != null)
                        {
                            var args = OutpostViewCueArgs.Create(OutpostViewCue.Upgrade, origin + m_Camp.BranchLocal((UpgradeBranch)e.Branch));
                            args.Branch = e.Branch;
                            args.Count = e.Count;
                            Cue(args);
                        }
                    }
                    break;
                case OutpostEventType.WorkerHired:
                {
                    var workers = run.Workers;
                    for (int i = 0; i < workers.Count; i++)
                    {
                        if (workers[i].Id != e.WorkerId) continue;
                        bool exists = i < m_Cats.Count && m_Cats[i] != null && m_Cats[i].WorkerId == e.WorkerId;
                        if (!exists)
                        {
                            if (m_Track.Length <= i) System.Array.Resize(ref m_Track, Mathf.NextPowerOfTwo(i + 1));
                            EnsureCat(i, workers[i], m_Live);
                        }
                        break;
                    }
                    if (fx != null && m_Camp != null && m_Live) fx.Puff(origin + m_Camp.HireExitLocal);
                    Emit(OutpostViewCue.WorkerSpawned, m_Camp != null ? m_Camp.HireExitLocal : CellCenterLocal(m_Map.SpawnCell), e.WorkerId);
                    break;
                }
                case OutpostEventType.EquipmentChanged:
                case OutpostEventType.ItemUpgraded:
                {
                    var profile = m_Sim.Profile;
                    var catalog = m_Sim.Configs?.Equipment;
                    int sparkles = 0;
                    for (int i = 0; i < m_Cats.Count; i++)
                    {
                        var v = m_Cats[i];
                        if (v == null) continue;
                        if (v.SetLook(profile, catalog) || e.Type == OutpostEventType.ItemUpgraded)
                        {
                            v.Pulse();
                            if (fx != null && sparkles < 4 && TakeFx())
                            {
                                sparkles++;
                                fx.Sparkle(origin + v.LocalPosition + new Vector3(0f, 0f, 0.4f), 2);
                            }
                        }
                    }
                    m_Camp?.MarkWardrobeDirty();
                    Emit(OutpostViewCue.Equip, m_Camp != null ? m_Camp.CenterLocal : Vector3.zero);
                    break;
                }
                default:
                    OnFactoryEvent(e, fx, origin);
                    break;
            }
        }

        bool CatGotCargo(int workerId)
        {
            var w = m_Sim.WorkerById(workerId);
            return w != null && w.Bag.Count > 0;
        }

        /// <summary>
        /// Удар (TargetHit): цель дрожит и сыплет сколы, котики, ударившие на этой отметке, бьют (Strike) — первый ударивший из события
        /// и все, кто добывает эту цель с таймером удара от этой отметки; контактная вспышка — со стороны первого ударившего.
        /// </summary>
        void OnTargetHit(OutpostEvent e, Effects fx, OutpostRun run, Vector3 origin)
        {
            var tv = TargetViewOf(e.TargetId);
            bool budget = fx != null && TakeFx();
            if (tv != null)
            {
                var chips = origin + tv.FeetLocal + new Vector3(0f, 0f, Mathf.Min(tv.TopZ, tv.DepthCells) * 0.5f);
                tv.OnHit(budget ? fx : null, chips);
                if (tv.Kind == 'p')
                    for (int k = 0; k < m_Barriers.Length; k++)
                        if (m_Barriers[k].TargetId == e.TargetId) m_Barriers[k].OnHit();
            }
            var workers = run.Workers;
            for (int i = 0; i < workers.Count && i < m_Cats.Count; i++)
            {
                var w = workers[i];
                var a = w.Action;
                bool hitter = w.Id == e.WorkerId ||
                    (w.Mode == WorkerMode.Mining && w.TargetId == e.TargetId && a != null && a.Kind == ActionKind.Hit && a.SegmentStart == e.Tick);
                if (hitter && m_Cats[i] != null) m_Cats[i].Strike();
            }
            var first = CatViewOf(e.WorkerId);
            if (budget && tv != null && first != null)
            {
                // Контакт — на стороне цели, обращённой к котику, на высоте тела (как BoardView).
                var center = tv.CenterLocal;
                var d = first.LocalPosition - center;
                d.y = 0f;
                var at = PixelWorld.Snap(origin + center + (d.sqrMagnitude > 1e-4f ? d.normalized * 0.32f : Vector3.zero) + new Vector3(0f, 0f, 0.18f));
                fx.Impact(at);
            }
            if (Cue != null)
            {
                var args = OutpostViewCueArgs.Create(OutpostViewCue.Hit, tv != null ? origin + tv.CenterLocal : BaseWorldPosition);
                args.WorkerId = e.WorkerId;
                args.TargetId = e.TargetId;
                args.Count = e.Count;
                args.Material = tv != null ? tv.Material : HitMaterial.Stone;
                args.Chains = tv != null && tv.Chains;
                Cue(args);
            }
        }

        /// <summary>
        /// Сдача порции (CargoDeposited — единственное зачисление, фаза 4 ядра): мешочек из сумки падает в воронку, ящик под ней
        /// подпрыгивает, над местом «+N» и монетки; последняя порция сумки — unload3 у котика.
        /// </summary>
        void OnDeposited(OutpostEvent e, Effects fx, OutpostRun run, Vector3 origin)
        {
            int slot = e.Slot;
            var cat = CatViewOf(e.WorkerId);
            if (m_Camp != null && slot >= 0)
            {
                var funnel = m_Camp.FunnelLocal(slot);
                if (cat != null)
                    m_Flights.Launch(cat.LocalPosition + new Vector3(0f, 0f, OutpostCatView.FeetY + OutpostCatView.BagY), funnel, DepositFlight, DepositArc,
                        null, 0f, 1f, 0.6f);
                m_Camp.OnDeposit(slot);
                if (TakeText()) FloatingText.Spawn(origin + m_Camp.SlotIndicatorLocal(slot), PlusMoneyText(e.Value), Palette.Gold);
                if (fx != null && TakeFx()) fx.CoinPop(origin + funnel, 2);
            }
            var w = m_Sim.WorkerById(e.WorkerId);
            bool done = w != null && w.Bag.Count == 0;
            if (done && cat != null) cat.PlayUnloadDone();
            if (Cue != null)
            {
                var at = slot >= 0 ? DepotWorldPosition(slot) : BaseWorldPosition;
                var args = OutpostViewCueArgs.Create(OutpostViewCue.Deposit, at);
                args.WorkerId = e.WorkerId;
                args.TargetId = e.TargetId;
                args.Slot = slot;
                args.Count = e.Count;
                args.Value = e.Value;
                Cue(args);
                if (done)
                {
                    args.Cue = OutpostViewCue.UnloadDone;
                    Cue(args);
                }
            }
        }
    }
}
