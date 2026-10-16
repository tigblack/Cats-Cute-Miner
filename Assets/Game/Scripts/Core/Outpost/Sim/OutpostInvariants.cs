using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Инварианты учёта (ТЗ 7.3, 17; CORE_SPEC §13; заводы — ТЗ 2.2 §10.2): проверяются в тестах после событий и save/load, при
    /// загрузке и записи. Возвращает список нарушений (пусто — состояние согласовано). Карта и каталог необязательны: без них
    /// пропускаются проверки, которым нужны рабочие клетки/ворота или уровни предметов.
    /// </summary>
    public static class OutpostInvariants
    {
        /// <param name="debugProfile">
        /// Снимок debug-профиля (слот Debug): разрешена отладочная выдача товара <see cref="OutpostRun.DebugGoods"/> (DevTools
        /// «Goods ×100», ТЗ 2.2 §13). Вне debug-профиля DebugGoods должен быть пуст.
        /// </param>
        public static List<string> Check(OutpostGameState state, OutpostMap map = null, EquipmentCatalog catalog = null, CardCatalog cards = null,
            bool debugProfile = false)
        {
            var errors = new List<string>();
            if (state == null)
            {
                errors.Add("state: null");
                return errors;
            }
            CheckProfile(state.Profile, catalog, errors);
            CheckCards(state.Profile, cards, errors);
            if (state.Run != null)
            {
                CheckRun(state.Run, map, errors, TeamCapBonus(state.Profile, cards));
                CheckFactories(state.Run, map, errors, debugProfile);
            }
            return errors;
        }

        /// <summary>Добавка карточек к пределу «Команды»; без каталога — без верхней проверки (нельзя посчитать бонус).</summary>
        static int TeamCapBonus(OutpostProfile p, CardCatalog cards)
        {
            if (p?.Cards == null || p.Cards.Count == 0) return 0;
            return cards != null ? CardService.EffectWhole(p, cards, CardEffectKind.TeamCap) : int.MaxValue / 4;
        }

        static void CheckCards(OutpostProfile p, CardCatalog cards, List<string> errors)
        {
            if (p == null) return;
            if (p.Elixir < 0) errors.Add("profile.Elixir < 0");
            if (p.CardsVersion >= 1 && p.HighestZone < p.CurrentZone) errors.Add("profile.HighestZone " + p.HighestZone + " < CurrentZone " + p.CurrentZone);
            if (p.Cards != null)
                foreach (var kv in p.Cards)
                {
                    var c = kv.Value;
                    if (c == null)
                    {
                        errors.Add("profile.Cards[" + kv.Key + "]: null");
                        continue;
                    }
                    if (c.Level < 0 || c.Copies < 0) errors.Add("profile.Cards[" + kv.Key + "]: уровень/копии < 0");
                    if (c.Level == 0 && c.Copies > 0) errors.Add("profile.Cards[" + kv.Key + "]: копии без полученной карточки");
                    if (cards != null)
                    {
                        var def = cards.Get(kv.Key);
                        if (def == null) errors.Add("profile.Cards: неизвестная карточка " + kv.Key);
                        else if (c.Level > def.MaxLevel) errors.Add("profile.Cards[" + kv.Key + "]: уровень " + c.Level + " > " + def.MaxLevel);
                    }
                }
            if (p.RockCycles != null)
                foreach (var kv in p.RockCycles)
                    if (kv.Value < 0) errors.Add("profile.RockCycles[" + kv.Key + "] < 0");
        }

        static string S(long v) => v.ToString(CultureInfo.InvariantCulture);

        static void CheckProfile(OutpostProfile p, EquipmentCatalog catalog, List<string> errors)
        {
            if (p == null)
            {
                errors.Add("profile: null");
                return;
            }
            if (p.ModeId != OutpostIds.ModeId) errors.Add("profile.ModeId: " + p.ModeId);
            if (p.Parts < 0) errors.Add("profile.Parts < 0");
            if (p.PlayTimeMs < 0) errors.Add("profile.PlayTimeMs < 0");
            if (p.Gems < 0) errors.Add("profile.Gems < 0");
            if (p.Consumables != null)
                foreach (var kv in p.Consumables)
                    if (kv.Value < 0) errors.Add("profile.Consumables[" + kv.Key + "] < 0");
            if (p.StoreCooldownUntilUtcMs != null)
                foreach (var kv in p.StoreCooldownUntilUtcMs)
                    if (kv.Value < 0) errors.Add("profile.StoreCooldownUntilUtcMs[" + kv.Key + "] < 0");
            if (p.RewardRng == null) errors.Add("profile.RewardRng: null");
            int maxLevel = catalog?.MaxItemLevel ?? int.MaxValue;
            if (p.Items != null)
                foreach (var kv in p.Items)
                {
                    if (kv.Value < 1 || kv.Value > maxLevel) errors.Add("profile.Items[" + kv.Key + "] = " + kv.Value + " вне [1, " + maxLevel + "]");
                    if (catalog != null && !catalog.TryGet(kv.Key, out _)) errors.Add("profile.Items: неизвестный предмет " + kv.Key);
                }
            if (p.Equipped == null || p.Equipped.Length != OutpostIds.SlotCount) errors.Add("profile.Equipped: ожидалось 4 слота");
            else
                for (int s = 0; s < p.Equipped.Length; s++)
                {
                    string id = p.Equipped[s];
                    if (id == null) continue;
                    if (p.ItemLevel(id) < 1) errors.Add("profile.Equipped[" + s + "] = " + id + " не в коллекции");
                    if (catalog != null && (!catalog.TryGet(id, out var def) || (int)def.Slot != s))
                        errors.Add("profile.Equipped[" + s + "] = " + id + ": предмет другого слота");
                }
            var keys = new HashSet<string>();
            if (p.RewardLog != null)
                foreach (var r in p.RewardLog)
                {
                    if (r.Key == null || !keys.Add(r.Key)) errors.Add("profile.RewardLog: повтор/пустой ключ " + r.Key);
                    if (r.DuplicateParts < 0 || r.CompletionParts < 0) errors.Add("profile.RewardLog[" + r.Key + "]: детали < 0");
                }
            if (p.CompletedZones != null)
                for (int i = 1; i < p.CompletedZones.Count; i++)
                    if (p.CompletedZones[i] <= p.CompletedZones[i - 1]) errors.Add("profile.CompletedZones: не по возрастанию");
        }

        static void CheckRun(OutpostRun run, OutpostMap map, List<string> errors, int teamCapBonus)
        {
            var b = run.Balance;
            if (b == null)
            {
                errors.Add("run.Balance: null");
                return;
            }
            // Кошелёк и учёт.
            // ТЗ 2.2 §6.4: золото продажи товара — отдельное слагаемое; TotalCredited остаётся золотом монет.
            if (run.Wallet != run.TotalCredited + run.BonusCredited + run.GoodsSoldGold - run.TotalSpent)
                errors.Add("run.Wallet ≠ TotalCredited + BonusCredited + GoodsSoldGold − TotalSpent");
            if (run.BonusCredited.Sign < 0) errors.Add("run.BonusCredited < 0");
            if (run.Wallet.Sign < 0) errors.Add("run.Wallet < 0");
            if (run.TotalSpent.Sign < 0) errors.Add("run.TotalSpent < 0");
            if (run.TotalCredited != run.Counters.DeliveredValue) errors.Add("run.TotalCredited ≠ Counters.DeliveredValue");

            // Уровни и работники.
            if (run.Levels == null || run.Levels.Length != OutpostIds.BranchCount) errors.Add("run.Levels: ожидалось 6 веток");
            else
                for (int i = 0; i < run.Levels.Length; i++)
                {
                    long cap = b.Branches[i].Cap + (i == (int)UpgradeBranch.Team ? (long)teamCapBonus : 0);
                    if (run.Levels[i] < 0 || run.Levels[i] > cap)
                        errors.Add("run.Levels[" + (UpgradeBranch)i + "] = " + run.Levels[i] + " вне [0, " + cap + "]");
                }
            if (run.ExtraWorkers < 0) errors.Add("run.ExtraWorkers < 0");
            int expectedWorkers = b.StartWorkers + run.ExtraWorkers + (run.Levels != null && run.Levels.Length > 0 ? run.Levels[0] : 0);
            if (run.Workers.Count != expectedWorkers) errors.Add("run.Workers: " + run.Workers.Count + " ≠ StartWorkers + карточки + Team = " + expectedWorkers);

            var workers = new Dictionary<int, WorkerState>();
            for (int i = 0; i < run.Workers.Count; i++)
            {
                var w = run.Workers[i];
                if (i > 0 && w.Id <= run.Workers[i - 1].Id) errors.Add("run.Workers: не по возрастанию Id");
                if (w.Id < 1 || w.Id >= run.NextWorkerId) errors.Add("worker " + w.Id + ": Id вне [1, NextWorkerId)");
                workers[w.Id] = w;
                if (map != null && !map.IsPlayable(w.Cell)) errors.Add("worker " + w.Id + ": cell in structural rock or outside map");
                if (map?.Topology != null && w.Path != null)
                    for (int step = System.Math.Max(0, w.PathIndex); step < w.Path.Length; step++)
                        if (!map.IsPlayable(w.Path[step]) || map.CellGateOrder[w.Path[step]] >= run.GatesOpened)
                            errors.Add("worker " + w.Id + ": path crosses structural rock or closed gate");
            }

            // Цели.
            var targetIndex = new Dictionary<int, TargetState>();
            for (int i = 0; i < run.Targets.Count; i++)
            {
                var t = run.Targets[i];
                if (i > 0 && t.Id <= run.Targets[i - 1].Id) errors.Add("run.Targets: не по возрастанию Id");
                targetIndex[t.Id] = t;
                if (t.ReleasedUnits < 0 || t.ReleasedUnits > t.TotalUnits) errors.Add("target " + t.Id + ": Released " + t.ReleasedUnits + " вне [0, " + t.TotalUnits + "]");
                if (t.RemainingHpUnits < 0 || t.RemainingHpUnits > t.MaxHpUnits) errors.Add("target " + t.Id + ": RemainingHp вне [0, Max]");
                if (t.Destroyed != (t.RemainingHpUnits == 0)) errors.Add("target " + t.Id + ": Destroyed ≠ (RemainingHp == 0)");
                if (t.MaxHpUnits > 0)
                {
                    long expected = t.RemainingHpUnits == 0 ? t.TotalUnits : (long)t.TotalUnits * (t.MaxHpUnits - t.RemainingHpUnits) / t.MaxHpUnits;
                    if (t.ReleasedUnits != expected) errors.Add("target " + t.Id + ": Released " + t.ReleasedUnits + " ≠ ожидаемому " + S(expected));
                }
            }

            // Порции: уникальность, принадлежность, сумма стоимостей.
            var keys = new HashSet<long>();
            var ground = new Dictionary<int, int>();
            var carried = new Dictionary<int, int>();
            BigInteger presentValue = BigInteger.Zero;
            int presentUnits = 0;
            var reservations = new Dictionary<int, int>();
            long prevSeq = 0;
            foreach (var batch in run.Batches)
            {
                if (batch.Seq <= prevSeq) errors.Add("batches: не по возрастанию Seq");
                prevSeq = batch.Seq;
                if (batch.Seq >= run.NextBatchSeq) errors.Add("batch " + batch.Seq + ": Seq ≥ NextBatchSeq");
                if (batch.Key != OutpostIds.BatchKey(batch.Seq)) errors.Add("batch " + batch.Seq + ": ключ " + batch.Key);
                if (batch.Units.Count == 0) errors.Add("batch " + batch.Seq + ": пустая партия");
                if (map != null && !map.IsPlayable(batch.Cell)) errors.Add("batch " + batch.Seq + ": cell outside playable mine");
                for (int i = 0; i < batch.Units.Count; i++)
                {
                    var u = batch.Units[i];
                    if (i > 0 && batch.Units[i - 1].CompareKey(u) >= 0) errors.Add("batch " + batch.Seq + ": порции не по ключу");
                    CheckUnit(u, "batch " + batch.Seq, targetIndex, keys, errors);
                    Inc(ground, u.Source);
                    presentValue += u.Value;
                    presentUnits++;
                    if (u.ReservedBy != 0)
                    {
                        Inc(reservations, u.ReservedBy);
                        if (!workers.TryGetValue(u.ReservedBy, out var rw)) errors.Add("unit " + Key(u) + ": резерв несуществующего работника " + u.ReservedBy);
                        else if ((rw.Mode != WorkerMode.MovingToCargo && rw.Mode != WorkerMode.PickingUp) || rw.CargoBatchSeq != batch.Seq || rw.TaskId != u.ReservedTask)
                            errors.Add("unit " + Key(u) + ": резерв работника " + rw.Id + " в режиме " + rw.Mode + " (партия " + rw.CargoBatchSeq + ", задача " + rw.TaskId + "/" + u.ReservedTask + ")");
                    }
                    else if (u.ReservedTask != 0) errors.Add("unit " + Key(u) + ": ReservedTask без ReservedBy");
                }
            }
            foreach (var w in run.Workers)
                for (int i = 0; i < w.Bag.Count; i++)
                {
                    var u = w.Bag[i];
                    if (i > 0 && w.Bag[i - 1].CompareKey(u) >= 0) errors.Add("worker " + w.Id + ": сумка не по ключу");
                    CheckUnit(u, "worker " + w.Id, targetIndex, keys, errors);
                    if (u.ReservedBy != 0) errors.Add("worker " + w.Id + ": порция в сумке с резервом");
                    Inc(carried, u.Source);
                    presentValue += u.Value;
                    presentUnits++;
                }
            int released = 0, delivered = 0;
            foreach (var t in run.Targets)
            {
                ground.TryGetValue(t.Id, out int g);
                carried.TryGetValue(t.Id, out int c);
                int d = t.ReleasedUnits - g - c;
                if (d < 0) errors.Add("target " + t.Id + ": сданных порций < 0 (Released " + t.ReleasedUnits + ", земля " + g + ", сумки " + c + ")");
                released += t.ReleasedUnits;
                delivered += d;
            }
            if (released != run.Counters.ReleasedUnits) errors.Add("Counters.ReleasedUnits " + run.Counters.ReleasedUnits + " ≠ Σ Released " + released);
            if (delivered != run.Counters.DeliveredUnits) errors.Add("Counters.DeliveredUnits " + run.Counters.DeliveredUnits + " ≠ Σ сданных " + delivered);
            if (run.Counters.ReleasedValue != presentValue + run.TotalCredited)
                errors.Add("стоимость выпущенного " + run.Counters.ReleasedValue + " ≠ земля + сумки + зачислено " + (presentValue + run.TotalCredited));

            // Работники: режимы, таймеры, рабочие клетки.
            var workCells = new HashSet<int>();
            var perTarget = new Dictionary<int, int>();
            foreach (var w in run.Workers)
            {
                string who = "worker " + w.Id;
                if (w.Mode == WorkerMode.ChoosingTask) errors.Add(who + ": ChoosingTask в согласованном снимке");
                if (map != null && (w.Cell < 0 || w.Cell >= map.CellCount)) errors.Add(who + ": клетка вне карты");
                var a = w.Action;
                switch (w.Mode)
                {
                    case WorkerMode.MovingToWork:
                    case WorkerMode.MovingToCargo:
                    case WorkerMode.Returning:
                    case WorkerMode.GoingHome:
                        if (a == null || a.Kind != ActionKind.Edge) errors.Add(who + ": " + w.Mode + " без таймера ребра");
                        if (w.Path == null || w.PathIndex < 0 || w.PathIndex >= w.Path.Length) errors.Add(who + ": неверный путь");
                        break;
                    case WorkerMode.Mining:
                        if (a == null || a.Kind != ActionKind.Hit) errors.Add(who + ": Mining без таймера удара");
                        break;
                    case WorkerMode.PickingUp:
                        if (a == null || a.Kind != ActionKind.Pickup) errors.Add(who + ": PickingUp без таймера подбора");
                        break;
                    case WorkerMode.Unloading:
                        if (a == null || a.Kind != ActionKind.Unload) errors.Add(who + ": Unloading без таймера");
                        if (w.DepotSlot < 0) errors.Add(who + ": Unloading без места");
                        if (w.Bag.Count == 0) errors.Add(who + ": Unloading с пустой сумкой");
                        break;
                    case WorkerMode.WaitingDepot:
                    case WorkerMode.Idle:
                        if (a != null) errors.Add(who + ": " + w.Mode + " с таймером");
                        break;
                }
                if (a != null)
                {
                    if (a.Deadline <= run.Now && run.Phase != RunPhase.ReadyToContinue) errors.Add(who + ": просроченный таймер " + a.Deadline + " ≤ Now " + run.Now);
                    if (a.Duration <= 0) errors.Add(who + ": длительность ≤ 0");
                    if (a.P0.Sign < 0 || a.P0 >= Rational.One) errors.Add(who + ": P0 вне [0, 1)");
                    if ((a.Kind == ActionKind.Edge || a.Kind == ActionKind.Unload) &&
                        a.Deadline != a.SegmentStart + ((Rational.One - a.P0) * a.Duration).Ceil())
                        errors.Add(who + ": Deadline не согласован с P0/Duration");
                }
                if ((w.Mode == WorkerMode.MovingToCargo || w.Mode == WorkerMode.PickingUp))
                {
                    reservations.TryGetValue(w.Id, out int rc);
                    if (rc == 0) errors.Add(who + ": " + w.Mode + " без резерва груза");
                }
                else if (w.CargoBatchSeq >= 0) errors.Add(who + ": CargoBatchSeq вне подбора");
                if (w.WorkCell >= 0)
                {
                    if (w.Mode != WorkerMode.MovingToWork && w.Mode != WorkerMode.Mining) errors.Add(who + ": WorkCell вне добычи");
                    if (!workCells.Add(w.WorkCell)) errors.Add(who + ": рабочая клетка " + w.WorkCell + " занята другим");
                    Inc(perTarget, w.TargetId);
                    if (!targetIndex.TryGetValue(w.TargetId, out var tt) || tt.Destroyed) errors.Add(who + ": рабочая клетка у мёртвой/неизвестной цели " + w.TargetId);
                    if (map != null)
                    {
                        var mt = map.Target(w.TargetId);
                        if (mt == null || System.Array.IndexOf(mt.WorkCells, w.WorkCell) < 0) errors.Add(who + ": клетка " + w.WorkCell + " не рабочая для цели " + w.TargetId);
                    }
                }
                else if (w.Mode == WorkerMode.Mining || (w.Mode == WorkerMode.MovingToWork && !w.TaskInvalid))
                    errors.Add(who + ": " + w.Mode + " без рабочей клетки");
            }
            foreach (var kv in perTarget)
                if (kv.Value > b.MaxWorkersPerTarget) errors.Add("target " + kv.Key + ": " + kv.Value + " добывающих > " + b.MaxWorkersPerTarget);

            // Склад и очередь.
            if (run.Slots == null || run.Slots.Length != OutpostIds.DepotSlotCount) errors.Add("run.Slots: ожидалось 2 места");
            else
                for (int s = 0; s < run.Slots.Length; s++)
                {
                    int wid = run.Slots[s].WorkerId;
                    if (wid == 0)
                    {
                        if (run.Slots[s].TaskId != 0) errors.Add("slot " + s + ": TaskId без работника");
                        continue;
                    }
                    if (!workers.TryGetValue(wid, out var sw)) errors.Add("slot " + s + ": несуществующий работник " + wid);
                    else
                    {
                        if (sw.DepotSlot != s) errors.Add("slot " + s + ": работник " + wid + " указывает на место " + sw.DepotSlot);
                        if (sw.Mode != WorkerMode.Returning && sw.Mode != WorkerMode.Unloading) errors.Add("slot " + s + ": работник " + wid + " в режиме " + sw.Mode);
                        if (sw.TaskId != run.Slots[s].TaskId) errors.Add("slot " + s + ": TaskId места ≠ задаче работника");
                    }
                }
            foreach (var w in run.Workers)
                if (w.DepotSlot >= 0 && (run.Slots == null || w.DepotSlot >= run.Slots.Length || run.Slots[w.DepotSlot].WorkerId != w.Id))
                    errors.Add("worker " + w.Id + ": место " + w.DepotSlot + " не за ним");
            var queued = new HashSet<int>();
            for (int i = 0; i < run.Queue.Count; i++)
            {
                var q = run.Queue[i];
                if (!queued.Add(q.WorkerId)) errors.Add("queue: дубль работника " + q.WorkerId);
                if (i > 0)
                {
                    var p = run.Queue[i - 1];
                    if (p.ArrivalTick > q.ArrivalTick || (p.ArrivalTick == q.ArrivalTick && p.WorkerId > q.WorkerId)) errors.Add("queue: нарушен порядок FIFO");
                }
                if (!workers.TryGetValue(q.WorkerId, out var qw) || qw.Mode != WorkerMode.WaitingDepot) errors.Add("queue: работник " + q.WorkerId + " не WaitingDepot");
            }
            foreach (var w in run.Workers)
                if (w.Mode == WorkerMode.WaitingDepot && !queued.Contains(w.Id)) errors.Add("worker " + w.Id + ": WaitingDepot вне очереди");

            // Фазы.
            if (map != null)
            {
                if (run.GatesOpened < 0 || run.GatesOpened > map.Gates.Length) errors.Add("run.GatesOpened вне диапазона");
                else
                    for (int k = 0; k < run.GatesOpened; k++)
                        if (!targetIndex.TryGetValue(map.Gates[k].TargetId, out var gt) || !gt.Destroyed) errors.Add("gate " + k + ": открыт, но цель жива");
                if (run.Phase != RunPhase.Running)
                {
                    foreach (var t in run.Targets)
                        if (!t.Destroyed) errors.Add(run.Phase + ": цель " + t.Id + " жива");
                    if (run.GatesOpened != map.Gates.Length) errors.Add(run.Phase + ": не все ворота открыты");
                }
            }
            if (run.Phase == RunPhase.ReadyToContinue)
            {
                if (run.Batches.Count > 0) errors.Add("ReadyToContinue: остались партии");
                if (run.Queue.Count > 0) errors.Add("ReadyToContinue: очередь не пуста");
                if (run.CompletionRewardKey == null) errors.Add("ReadyToContinue: нет ключа награды");
                if (run.Slots != null)
                    foreach (var s in run.Slots)
                        if (s.WorkerId != 0) errors.Add("ReadyToContinue: место склада занято");
                foreach (var w in run.Workers)
                {
                    if (w.Bag.Count > 0) errors.Add("ReadyToContinue: сумка работника " + w.Id + " не пуста");
                    if (w.Action != null) errors.Add("ReadyToContinue: таймер работника " + w.Id);
                }
            }
        }

        /// <summary>
        /// Заводы, трубы, склад и рынок (ТЗ 2.2 §10.2): у завода Produced = ReadyUnits + Dispatched; по товару отправлено = в трубах +
        /// GoodsArrived; GoodsArrived + DebugGoods = Warehouse + GoodsSold; вне debug DebugGoods пуст; GoodsSoldGold = Σ проданного ×
        /// цена шахты; у Locked HP &gt; 0, у открытого HP = 0; состояния согласованы с фазой и сроками (цикл [CycleStart, CycleEnd)
        /// длиной CycleTicks — только у Working, у Locked/Ready/Stopped оба поля −1). Кошелёк — в CheckRun.
        /// «Automated не снимается» — свойство переходов, его проверяют тесты (в снимке видно только значение флага).
        /// </summary>
        static void CheckFactories(OutpostRun run, OutpostMap map, List<string> errors, bool debugProfile)
        {
            if (run.GoodsSoldGold.Sign < 0) errors.Add("run.GoodsSoldGold < 0");
            if (run.Counters != null && run.Counters.ManualDeliveries < 0) errors.Add("Counters.ManualDeliveries < 0");
            var factories = run.Factories;
            var pipes = run.PipeBatches;
            if (factories == null || pipes == null || run.Warehouse == null || run.GoodsArrived == null || run.GoodsSold == null || run.DebugGoods == null)
            {
                errors.Add("run: нет полей заводов (Factories, PipeBatches, Warehouse, GoodsArrived, GoodsSold, DebugGoods)");
                return;
            }
            var rules = run.FactoryRules;
            if (rules == null && (factories.Count > 0 || pipes.Count > 0)) errors.Add("run.FactoryRules: null при заводах");
            bool stopped = run.Phase == RunPhase.ReadyToContinue;

            var targetById = new Dictionary<int, TargetState>();
            foreach (var t in run.Targets) targetById[t.Id] = t;
            var byId = new Dictionary<int, FactoryState>();
            var dispatched = new Dictionary<string, long>(System.StringComparer.Ordinal);
            for (int i = 0; i < factories.Count; i++)
            {
                var f = factories[i];
                string who = "factory " + (f == null ? "null" : S(f.TargetId));
                if (f == null)
                {
                    errors.Add("run.Factories: null");
                    continue;
                }
                if (i > 0 && factories[i - 1] != null && f.TargetId <= factories[i - 1].TargetId) errors.Add("run.Factories: не по возрастанию TargetId");
                byId[f.TargetId] = f;
                var rule = rules?.ForType(f.TypeId);
                if (rules != null && rule == null) errors.Add(who + ": тип " + f.TypeId + " не в factory_rules");
                if (rule != null && rule.GoodId != f.GoodId) errors.Add(who + ": товар " + f.GoodId + " ≠ " + rule.GoodId);
                if (f.ReadyUnits < 0 || f.Produced < 0 || f.Dispatched < 0 || f.Cycles < 0) errors.Add(who + ": отрицательные счётчики");
                if (f.Produced != f.ReadyUnits + f.Dispatched) errors.Add(who + ": Produced " + S(f.Produced) + " ≠ ReadyUnits + Dispatched " + S(f.ReadyUnits + f.Dispatched));
                if (f.GoodId != null) AddTo(dispatched, f.GoodId, f.Dispatched);

                if (!targetById.TryGetValue(f.TargetId, out var ts)) errors.Add(who + ": нет цели");
                else
                {
                    if (ts.Kind != 's') errors.Add(who + ": цель вида " + ts.Kind + ", ожидался s");
                    if (f.Status == FactoryStatus.Locked && ts.RemainingHpUnits <= 0) errors.Add(who + ": Locked при HP = 0");
                    if (f.Status != FactoryStatus.Locked && ts.RemainingHpUnits != 0) errors.Add(who + ": открыт при HP > 0");
                }
                if (map != null)
                {
                    var mf = map.Factory(f.TargetId);
                    if (mf == null) errors.Add(who + ": нет завода в карте");
                    else if (mf.TypeId != f.TypeId) errors.Add(who + ": тип " + f.TypeId + " ≠ карте " + mf.TypeId);
                }

                switch (f.Status)
                {
                    case FactoryStatus.Locked:
                        if (f.Produced != 0 || f.Cycles != 0 || f.CycleStart != -1 || f.CycleEnd != -1) errors.Add(who + ": Locked с циклами или выпуском");
                        break;
                    case FactoryStatus.Working:
                        if (stopped) errors.Add(who + ": Working в ReadyToContinue");
                        if (f.ReadyUnits != 0) errors.Add(who + ": Working с готовой партией");
                        if (f.CycleStart < 0 || f.CycleEnd <= f.CycleStart) errors.Add(who + ": цикл [" + S(f.CycleStart) + ", " + S(f.CycleEnd) + ")");
                        else if (rule != null && f.CycleEnd - f.CycleStart != rule.CycleTicks) errors.Add(who + ": длительность цикла ≠ CycleSec × 10^6");
                        if (!stopped && f.CycleEnd <= run.Now) errors.Add(who + ": просроченный конец цикла " + S(f.CycleEnd) + " ≤ Now " + S(run.Now));
                        break;
                    case FactoryStatus.Ready:
                        if (stopped) errors.Add(who + ": Ready в ReadyToContinue");
                        if (f.ReadyUnits <= 0) errors.Add(who + ": Ready без партии");
                        if (f.Automated) errors.Add(who + ": автоматический завод ждёт доставки");
                        // Цикл есть только в Working (CORE_SPEC §16.4): у готовой партии CycleStart = CycleEnd = −1.
                        if (f.CycleStart != -1 || f.CycleEnd != -1) errors.Add(who + ": Ready с циклом [" + S(f.CycleStart) + ", " + S(f.CycleEnd) + ")");
                        break;
                    case FactoryStatus.Stopped:
                        if (!stopped) errors.Add(who + ": Stopped вне ReadyToContinue");
                        if (f.CycleStart != -1 || f.CycleEnd != -1) errors.Add(who + ": Stopped с циклом");
                        break;
                }
            }
            if (map != null && rules != null)
                foreach (var mf in map.Factories)
                    if (!byId.ContainsKey(mf.TargetId)) errors.Add("factory " + S(mf.TargetId) + ": завод карты без состояния");

            // Трубы.
            var inPipes = new Dictionary<string, long>(System.StringComparer.Ordinal);
            long prev = 0;
            foreach (var b in pipes)
            {
                string who = "pipe " + (b == null ? "null" : S(b.Seq));
                if (b == null)
                {
                    errors.Add("run.PipeBatches: null");
                    continue;
                }
                if (b.Seq <= prev) errors.Add("run.PipeBatches: не по возрастанию Seq");
                prev = b.Seq;
                if (b.Seq < 1 || b.Seq >= run.NextPipeBatchSeq) errors.Add(who + ": Seq вне [1, NextPipeBatchSeq)");
                if (b.Units <= 0) errors.Add(who + ": Units ≤ 0");
                if (b.ArriveTick < b.DepartTick) errors.Add(who + ": ArriveTick < DepartTick");
                if (b.DepartTick > run.Now) errors.Add(who + ": отправлена в будущем");
                if (!stopped && b.ArriveTick <= run.Now) errors.Add(who + ": просроченное прибытие " + S(b.ArriveTick) + " ≤ Now " + S(run.Now));
                if (!byId.TryGetValue(b.FactoryId, out var f)) errors.Add(who + ": неизвестный завод " + S(b.FactoryId));
                else if (f.GoodId != b.GoodId) errors.Add(who + ": товар " + b.GoodId + " ≠ товару завода " + f.GoodId);
                if (b.GoodId != null) AddTo(inPipes, b.GoodId, b.Units);
            }
            if (run.NextPipeBatchSeq < 1) errors.Add("run.NextPipeBatchSeq < 1");

            // Учёт товара по каждому товару.
            var goods = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var d in new[] { dispatched, inPipes, run.Warehouse, run.GoodsArrived, run.GoodsSold, run.DebugGoods })
                foreach (var kv in d)
                {
                    if (kv.Key == null) continue;
                    goods.Add(kv.Key);
                    if (kv.Value < 0) errors.Add("товар " + kv.Key + ": отрицательное количество");
                }
            var soldGold = System.Numerics.BigInteger.Zero;
            foreach (string g in goods)
            {
                long sent = Get(dispatched, g), pipe = Get(inPipes, g), arrived = Get(run.GoodsArrived, g), debug = Get(run.DebugGoods, g);
                long stock = Get(run.Warehouse, g), sold = Get(run.GoodsSold, g);
                if (sent != pipe + arrived) errors.Add("товар " + g + ": отправлено " + S(sent) + " ≠ в трубах " + S(pipe) + " + пришло " + S(arrived));
                if (arrived + debug != stock + sold)
                    errors.Add("товар " + g + ": пришло " + S(arrived) + " + отладка " + S(debug) + " ≠ на складе " + S(stock) + " + продано " + S(sold));
                if (!debugProfile && debug != 0) errors.Add("товар " + g + ": DebugGoods вне debug-профиля");
                if (rules != null && rules.ForGood(g) == null) errors.Add("товар " + g + ": нет в factory_rules");
                if (sold > 0) soldGold += sold * FactoryQueries.Price(run, g);
            }
            if (rules != null && run.Balance != null && soldGold != run.GoodsSoldGold)
                errors.Add("run.GoodsSoldGold " + run.GoodsSoldGold + " ≠ Σ проданного × цена " + soldGold);
        }

        static long Get(Dictionary<string, long> d, string key) => d.TryGetValue(key, out long v) ? v : 0;

        static void AddTo(Dictionary<string, long> d, string key, long value)
        {
            d.TryGetValue(key, out long v);
            d[key] = v + value;
        }

        static void CheckUnit(CargoUnit u, string where, Dictionary<int, TargetState> targets, HashSet<long> keys, List<string> errors)
        {
            if (!targets.TryGetValue(u.Source, out var t)) errors.Add(where + ": порция неизвестной цели " + u.Source);
            else if (u.Ordinal < 0 || u.Ordinal >= t.ReleasedUnits) errors.Add(where + ": порция " + Key(u) + " вне выпущенных");
            if (!keys.Add(((long)u.Source << 32) | (uint)u.Ordinal)) errors.Add(where + ": дубль порции " + Key(u));
            if (u.Value.Sign <= 0) errors.Add(where + ": стоимость порции ≤ 0");
        }

        static string Key(CargoUnit u) => u.Source.ToString(CultureInfo.InvariantCulture) + ":" + u.Ordinal.ToString(CultureInfo.InvariantCulture);

        static void Inc(Dictionary<int, int> d, int key)
        {
            d.TryGetValue(key, out int v);
            d[key] = v + 1;
        }
    }
}
