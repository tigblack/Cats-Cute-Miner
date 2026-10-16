using System;
using System.Collections;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using UnityEngine;
using CommandResult = CatMiners.Core.Outpost.CommandResult;

namespace CatMiners.Runtime.Outpost
{
    public sealed partial class OutpostController
    {
        public UpgradeQuote Quote(UpgradeBranch branch, BuyMode mode) => Sim != null ? Sim.Quote(branch, mode) : default;
        public CommandResult Buy(UpgradeBranch branch, BuyMode mode) => Execute(OutpostCommand.BuyUpgrade(branch, mode));
        public CommandResult Equip(string itemId) => Execute(OutpostCommand.Equip(itemId));
        public CommandResult Unequip(EquipSlot slot) => Execute(OutpostCommand.Unequip(slot));
        public CommandResult EquipBest() => Execute(OutpostCommand.EquipBest());
        public CommandResult UpgradeItem(string itemId) => Execute(OutpostCommand.UpgradeItem(itemId));
        public CommandResult UpgradeCard(string cardId) => Execute(OutpostCommand.UpgradeCard(cardId));

        // ───────────── Заводы, склад и рынок (ТЗ 2.2; контракт WP0, вид и UI — WP5) ─────────────

        /// <summary>«Deliver»: команда ядра DeliverFactory на текущей отметке и сразу запись (как покупки).</summary>
        public CommandResult DeliverFactory(int targetId) => Execute(OutpostCommand.DeliverFactory(targetId));
        /// <summary>Продажа товара goodId (null — всего) командой ядра SellGoods и сразу запись.</summary>
        public CommandResult SellGoods(string goodId) => Execute(OutpostCommand.SellGoods(goodId));
        public string DeliverBlockReason(int targetId) => Sim != null ? Sim.DeliverBlockReason(targetId) : "loading";
        public string SellBlockReason(string goodId) => Sim != null ? Sim.SellBlockReason(goodId) : "loading";

        int m_MarketUnlockZone = -1;

        /// <summary>
        /// Первая шахта с заводами по картам шахт (источник, override и топология — как у самой шахты). Считается один раз, при
        /// первом запросе (кнопка Market в шахте без заводов): обычно две карты — шахты 1 и 2.
        /// </summary>
        public int MarketUnlockZone
        {
            get
            {
                if (m_MarketUnlockZone < 0 && Balance != null && Configs != null)
                {
                    m_MarketUnlockZone = 0;
                    for (int zone = 1; zone <= Balance.ZoneCount; zone++)
                        if (OutpostLevelAdapter.BuildMine(Balance, zone, Configs).Factories.Length > 0)
                        {
                            m_MarketUnlockZone = zone;
                            break;
                        }
                }
                return m_MarketUnlockZone > 0 ? m_MarketUnlockZone : 0;
            }
        }

        public FactoryState FactoryOf(int targetId) => Sim?.FactoryById(targetId);
        public long GoodsCount(string goodId) => Sim != null ? Sim.GoodsCount(goodId) : 0;
        public System.Numerics.BigInteger GoodsPrice(string goodId) => Sim != null ? Sim.GoodsPrice(goodId) : System.Numerics.BigInteger.Zero;
        public System.Numerics.BigInteger WarehouseValue(string goodId) => Sim != null ? Sim.WarehouseValue(goodId) : System.Numerics.BigInteger.Zero;
        public void SelectWarehouse() { if (CommandsAllowed) SetSelection(new OutpostSelection { Kind = OutpostSelectionKind.Warehouse, Id = -1 }); }
        /// <summary>
        /// Центр footprint завода из вида (<see cref="OutpostBoardView.TryGetFactoryWorldPosition"/>); вида завода нет (run без
        /// правил заводов) — центр цели. false — не завод.
        /// </summary>
        public bool TryGetFactoryWorldPosition(int targetId, out Vector3 world)
        {
            world = default;
            if (Map?.Factory(targetId) == null || Board == null) return false;
            return Board.TryGetFactoryWorldPosition(targetId, out world) || TryGetTargetWorldPosition(targetId, out world);
        }
        /// <summary>Склад товаров во дворе лагеря (BaseCampView, столбец baseCol + 4); без склада — центр лагеря.</summary>
        public Vector3 WarehouseWorldPosition => Board != null ? Board.WarehouseWorldPosition : Vector3.zero;

        /// <summary>Минимальный интервал между тапами-ударами (секунды реального времени): частые касания не копят урон.</summary>
        public const double StrikeTapInterval = 0.4;
        double m_NextStrikeTap = double.NegativeInfinity;

        /// <summary>Тап по цели будет ударом кирки (иначе ввод показывает сведения о цели).</summary>
        public bool CanStrike(int targetId) => CommandsAllowed && Sim.CanStrike(targetId);

        /// <summary>
        /// Тап/клик по цели (ТЗ 6.6): кирка игрока появляется в точке касания <paramref name="world"/>; урон наносит команда Strike
        /// в кадр удара анимации (<see cref="StrikeNow"/>). true — касание стало ударом (в том числе слишком частое, без новой
        /// анимации); false — цель нельзя ударить сейчас.
        /// </summary>
        public bool BeginStrike(int targetId, Vector3 world)
        {
            if (!CanStrike(targetId)) return false;
            double now = Clock.MonotonicSeconds;
            // Один интервал для мыши, всех пальцев и всех целей. Повторные касания
            // поглощаются без очереди и без продления паузы; замахи не накладываются.
            if (now < m_NextStrikeTap || Board.ActiveStrikes > 0) return true;
            m_NextStrikeTap = now + StrikeTapInterval;
            Board.PlayStrike(targetId, world);
            return true;
        }

        /// <summary>
        /// Кадр удара кирки: команда Strike на текущей отметке. Без немедленной записи — удар не тратит и не начисляет золото,
        /// состояние уходит в автосохранение раз в 2 с; разрушение и груз показываются событиями следующего кадра.
        /// </summary>
        bool StrikeNow(int targetId)
        {
            if (!CommandsAllowed) return false;
            AdvanceOnline();
            if (!CommandsAllowed) return false;
            var result = Sim.Submit(OutpostCommand.Strike(targetId));
            if (!result.Ok) return false;
            Sim.DrainEvents(m_Events);
            return true;
        }

        // ───────────── Магазин (ТЗ 10.5) ─────────────

        /// <summary>Купить плитку магазина (сундук, эликсир, гемы) — команда ядра на текущем UTC; сразу запись.</summary>
        public CommandResult StoreBuy(string itemId)
        {
            if (AndroidRuntime && Configs?.Store != null && Configs.Store.TryGet(itemId, out var def))
            {
                if (def.Currency == StoreCurrency.Real) return CommandsAllowed && !IsDebugProfile && CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Buy(itemId) == true ? new CommandResult { Ok = true } : CommandResult.Fail("unavailable");
                if (def.Currency == StoreCurrency.Ad) return OfferReward(itemId, System.Numerics.BigInteger.Zero);
            }
            return Execute(OutpostCommand.StoreBuy(itemId, Clock.UtcNowMs));
        }

        /// <summary>
        /// Тайм-скип: <see cref="StoreItemDef.Minutes"/> минут работы шахты сразу. Расходник из наличия или гемы списываются на
        /// копии состояния, расчёт идёт порциями, как офлайн (окно прогресса), затем копия записывается одним снимком и
        /// показывается итог (PendingSkip).
        /// На завершённой шахте недоступен («phase»).
        /// </summary>
        public CommandResult UseTimeSkip(string itemId)
        {
            var store = Configs?.Store;
            if (store == null || !store.TryGet(itemId, out var def) || def.Kind != StoreItemKind.TimeSkip) return CommandResult.Fail("unknown");
            if (!CommandsAllowed || m_Offline != null) return CommandResult.Fail("phase");
            AdvanceOnline();
            if (!CommandsAllowed) return CommandResult.Fail("phase");
            if (State.Run.Phase == RunPhase.ReadyToContinue) { Sound.Denied(); return CommandResult.Fail("phase"); }
            var job = new OfflineSimulator(Map, Configs).BeginSkip(State, def, Clock.UtcNowMs);
            var paid = StoreService.PayTimeSkip(job.State.Profile, def);
            if (!paid.Ok) { Sound.Denied(); return paid; }
            Input.CancelGesture();
            m_Events.Clear();
            Sim.Events.Clear();
            Phase = OutpostControllerPhase.OfflineCatchUp;
            m_ShowOfflineSummary = false;
            m_OfflineOriginMonoUs = MonoUs;
            m_OfflineOriginUtcMs = Clock.UtcNowMs;
            m_Offline = job;
            Hud.RefreshNow();
            return paid;
        }

        /// <summary>
        /// «Double income» окна «Пока вас не было» (тестовый режим рекламы, ТЗ 13): золото офлайна ещё раз — командой ядра
        /// BonusGold, один раз, затем окно закрывается.
        /// </summary>
        public CommandResult ClaimOfflineDouble()
        {
            var pending = PendingOffline;
            if (pending == null) return CommandResult.Fail("unknown");
            if (AndroidRuntime) return OfferReward(null, pending.GoldDelivered);
            if (Configs?.Store == null || !Configs.Store.TestPurchases) return CommandResult.Fail("unavailable");
            if (pending.GoldDelivered.Sign <= 0)
            {
                DismissOffline();
                return new CommandResult { Ok = true };
            }
            var result = Execute(OutpostCommand.BonusGold(pending.GoldDelivered));
            if (result.Ok) DismissOffline();
            return result;
        }

        /// <summary>
        /// Золото награды вне окна офлайна (квесты playable-сборки): команда ядра BonusGold на текущей отметке, сразу запись,
        /// инвариант кошелька — как у «Double income». Тост «Income doubled» не показывается: окно награды — у вызывающего,
        /// поэтому событие BonusGold не уходит в показ. Основная игра этот путь не вызывает.
        /// </summary>
        public CommandResult GrantRewardGold(System.Numerics.BigInteger amount)
        {
            if (AndroidRuntime) return CommandResult.Fail("unavailable");
            int before = m_Events.Count;
            var result = Execute(OutpostCommand.BonusGold(amount));
            for (int i = m_Events.Count - 1; i >= before && i >= 0; i--)
                if (m_Events[i].Type == OutpostEventType.BonusGold) { m_Events.RemoveAt(i); break; }
            return result;
        }

        CommandResult Execute(OutpostCommand command)
        {
            if (!CommandsAllowed) return CommandResult.Fail("phase");
            AdvanceOnline();
            if (!CommandsAllowed) return CommandResult.Fail("phase");
            var result = Sim.Submit(command);
            if (!result.Ok) { Sound.Denied(); return result; }
            Sim.DrainEvents(m_Events);
            Commit(State, Map, null);
            if (SaveFailed) return CommandResult.Fail("save");
            Hud.RefreshNow();
            return result;
        }

        public void ContinueToNextMine()
        {
            if (!CanContinue || m_Suspended || m_RewardShowing || Services?.IsRewardShowing == true || Services?.HasEarned(State.Profile.PendingRewardedOffer?.Id) == true || State.Profile.PendingRewardedOffer?.Earned == true || m_EarnedCallbacks.Count > 0) return;
            var transition = StageProgressionService.PrepareTransition(State, Balance, Configs, Clock.UtcNowMs);
            if (!transition.Ok) return;
            InTransition = true;
            Phase = OutpostControllerPhase.Transitioning;
            Input.CancelGesture();
            // Old board remains visible on a failed commit. Retry uses this same prepared run.
            Commit(transition.State, transition.Map, () => StartCoroutine(ShowTransition(transition)));
        }

        IEnumerator ShowTransition(TransitionResult transition)
        {
            Hud.BeginTransition();
            float elapsed = 0;
            while (elapsed < 0.25f)
            {
                if (!m_Suspended) elapsed += FrameTime.Delta;
                Hud.SetTransitionFade(Mathf.Clamp01(elapsed / 0.25f));
                yield return null;
            }
            SetSimulation(transition.State, transition.Map);
            PendingOffline = null;
            PendingSkip = null;
            elapsed = 0;
            while (elapsed < 0.25f)
            {
                if (!m_Suspended) elapsed += FrameTime.Delta;
                Hud.SetTransitionFade(1 - Mathf.Clamp01(elapsed / 0.25f));
                yield return null;
            }
            Hud.EndTransition();
            InTransition = false;
            Phase = OutpostControllerPhase.Playing;
            // Include the transition animation/background interval in the new mine exactly once.
            BeginOffline(showSummary: false);
        }

        public Vector3 BaseWorldPosition => Board != null ? Board.BaseWorldPosition : Vector3.zero;
        public Vector3 DepotWorldPosition(int slot) => Board != null ? Board.DepotIndicatorWorldPosition(slot) : Vector3.zero;
        public bool TryGetWorkerWorldPosition(int id, out Vector3 world)
        {
            world = default;
            return Board != null && Board.TryGetWorkerWorldPosition(id, out world);
        }
        public bool TryGetTargetWorldPosition(int id, out Vector3 world)
        {
            world = default;
            return Board != null && Board.TryGetTargetWorldPosition(id, out world);
        }
        public void SelectWorker(int id)
        {
            if (CommandsAllowed && Sim.WorkerById(id) != null) SetSelection(new OutpostSelection { Kind = OutpostSelectionKind.Worker, Id = id });
        }
        public void SelectTarget(int id)
        {
            if (CommandsAllowed && Sim.TargetById(id) != null) SetSelection(new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = id });
        }
        public void SelectBase() { if (CommandsAllowed) SetSelection(new OutpostSelection { Kind = OutpostSelectionKind.Base, Id = -1 }); }
        public void ClearSelection() => SetSelection(OutpostSelection.None);
        void SetSelection(OutpostSelection selection)
        {
            Selection = selection;
            Board?.SetSelection(selection);
            Hud?.RefreshNow();
            if (Hud != null && Hud.TryGetInfoTop(out float top))
            {
                Vector3 world = default;
                bool found = selection.Kind == OutpostSelectionKind.Worker ? TryGetWorkerWorldPosition(selection.Id, out world) :
                    selection.Kind == OutpostSelectionKind.Target && TryGetTargetWorldPosition(selection.Id, out world);
                if (found) Rig.RevealAbove(world.z - 0.5f, world.z + 0.5f, top);
            }
        }
        public void FocusBase()
        {
            if (Sim == null || InTransition) return;
            Input.CancelGesture();
            Rig.FocusOnSmooth(new Vector2(Map.Topology != null ? Board.CellCenter(Map.SpawnCell).x : Board.BaseWorldPosition.x, Rig.MinZ));
        }

        public void SetVolume(AudioGroup group, float value)
        {
            if (!CommandsAllowed) return;
            var s = State.Profile.Settings;
            value = SafeVolume(value);
            switch (group)
            {
                case AudioGroup.Master: s.Master = value; break;
                case AudioGroup.Music: s.Music = value; break;
                case AudioGroup.Sfx: s.Sfx = value; break;
                case AudioGroup.Ambience: s.Ambience = value; break;
                default: return;
            }
            ApplySettings();
            Commit(State, Map, null);
        }
        public void SetVibration(bool on)
        {
            if (!CommandsAllowed) return;
            State.Profile.Settings.Vibration = on;
            Commit(State, Map, null);
        }
        public void SetReduceEffects(bool on)
        {
            if (!CommandsAllowed) return;
            State.Profile.Settings.ReduceEffects = on;
            ApplySettings();
            Commit(State, Map, null);
        }

        public void LoadZone(int zone)
        {
            if (!DebugAllowed || !CommandsAllowed || zone < 1 || zone > Balance.ZoneCount) return;
            var profile = OutpostJson.DeepCopy(State.Profile);
            profile.CurrentZone = zone;
            profile.CampaignComplete = false;
            var map = OutpostLevelAdapter.BuildMine(Balance, zone, Configs);
            var state = new OutpostGameState { Profile = profile, Run = StageProgressionService.CreateRun(profile, zone, map, Configs, Balance, Clock.UtcNowMs) };
            Commit(state, map, () => { SetSimulation(state, map); PendingOffline = null; PendingSkip = null; Hud.CloseAllWindows(); Hud.RefreshNow(); });
        }

        /// <summary>Debug-профиль: +10 000 гемов для проверки магазина. В Main недоступно.</summary>
        public void GrantTestGems()
        {
            if (!DebugAllowed || !CommandsAllowed) return;
            var state = OutpostJson.DeepCopy(State);
            state.Profile.Gems = checked(state.Profile.Gems + 10_000);
            var map = Map;
            Commit(state, map, () => { SetSimulation(state, map); Hud.RefreshNow(); });
        }
        public void GrantTestKit()
        {
            if (!DebugAllowed || !CommandsAllowed) return;
            var state = OutpostJson.DeepCopy(State);
            foreach (var item in Configs.Equipment.Items) EquipmentService.Grant(state.Profile, item, Configs);
            state.Profile.Parts += 10000;
            EquipmentService.EquipBest(state.Profile, Configs.Equipment);
            var map = Map;
            Commit(state, map, () => { SetSimulation(state, map); Hud.RefreshNow(); });
        }
        /// <summary>
        /// Debug-профиль: копии всех открытых по прогрессу карточек (по одной до активации и запас на улучшения) и эликсир —
        /// для проверки улучшений и максимума. В Main недоступно.
        /// </summary>
        public void GrantTestCards()
        {
            if (!DebugAllowed || !CommandsAllowed || Configs.Cards == null) return;
            var state = OutpostJson.DeepCopy(State);
            foreach (var def in Configs.Cards.Cards)
                if (CardService.IsUnlocked(state.Profile, def)) CardService.Grant(state.Profile, def, 1 + def.UpgradeCopies[0] * 3);
            state.Profile.Elixir = checked(state.Profile.Elixir + 100_000);
            var map = Map;
            Commit(state, map, () => { SetSimulation(state, map); Hud.RefreshNow(); });
        }

        public void CompleteStageNow()
        {
            if (!DebugAllowed || !CommandsAllowed) return;
            // Exercise real logistics at accelerated wall time; no fabricated cargo or wallet.
            var state = OutpostJson.DeepCopy(State);
            var map = Map;
            var job = new OutpostSimulation(state, map, Configs) { RecordEvents = false };
            Phase = OutpostControllerPhase.OfflineCatchUp;
            StartCoroutine(CompleteDebug(job, map));
        }
        IEnumerator CompleteDebug(OutpostSimulation job, OutpostMap map)
        {
            // Профиль b6: шахта с множителем темпа K(z) длиннее в K раз — предел DEV-завершения растёт так же (как у бота).
            long target = checked(job.Now + OutpostBot.DefaultTickLimit * OutpostBot.PaceLimitFactor(job.Run));
            while (!job.IsEconomyStopped && !job.Run.WatchdogFired && job.Now < target)
            {
                if (m_Suspended) { yield return null; continue; }
                job.Advance(target, 64);
                yield return null;
            }
            job.Run.LastProcessedUtcMs = Math.Max(job.Run.LastProcessedUtcMs, Clock.UtcNowMs);
            Commit(job.State, map, () => { SetSimulation(job.State, map); Phase = OutpostControllerPhase.Playing; Hud.RefreshNow(); });
        }
    }
}
