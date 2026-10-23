using System;
using System.Numerics;
using CatMiners.Core.Data;

namespace CatMiners.Core.Outpost
{
    /// <summary>Результат подготовки перехода: следующий снимок целиком (профиль + новый run) и его карта.</summary>
    public sealed class TransitionResult
    {
        public bool Ok;
        /// <summary>"phase" (не ReadyToContinue), "campaign" (последняя шахта).</summary>
        public string Reason;
        public OutpostGameState State;
        public OutpostMap Map;
    }

    /// <summary>
    /// Завершение шахты, однократная награда и создание следующего run (ТЗ 10.2, 11). Переход готовит новый снимок в памяти;
    /// контроллер сохраняет его целиком и только после успешной записи переключает сцену.
    /// </summary>
    public static class StageProgressionService
    {
        /// <summary>Новый профиль режима (seed создаётся один раз) и run выбранной шахты.</summary>
        public static OutpostGameState NewGame(BalanceRepository repo, OutpostConfigSet configs, ulong seed, long nowUtcMs, int zone = 1)
        {
            var profile = NewProfile(configs, seed, zone);
            var map = OutpostLevelAdapter.BuildMine(repo, zone, configs);
            return new OutpostGameState { Profile = profile, Run = CreateRun(profile, zone, map, configs, repo, nowUtcMs) };
        }

        /// <summary>Пустой постоянный профиль: seed и генератор наград (поток RewardRngStream) создаются один раз.</summary>
        public static OutpostProfile NewProfile(OutpostConfigSet configs, ulong seed, int zone = 1)
        {
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var profile = new OutpostProfile
            {
                Seed = seed,
                RewardRng = new Pcg32(seed, configs.Rewards.RewardRngStream),
                RewardRngVersion = configs.Rewards.RewardRngVersion,
                CurrentZone = zone,
                CardsVersion = 1,
                HighestZone = zone,
                CardRng = new Pcg32(seed, configs.CardRules.CardRngStream),
            };
            // Первая сессия (progression.json): функции открываются по шахтам; учебной карточки первой доставки больше нет.
            ProgressionService.InitNewProfile(profile, configs);
            return profile;
        }

        /// <summary>
        /// Новый run: RunId = "run-{NextRunSeq++}", уровни 0, StartWorkers в SpawnCell, кошелёк 0, HP/порции из адаптера,
        /// снимок текущего профиля чисел и правил заводов (ТЗ 2.2 §3.2), заводы карты в цепях (Locked), пустые трубы и склад
        /// товаров; затем фаза 7 на Now = 0. Переход и перезапуск шахты создают run заново — заводы, трубы, склад и счётчики
        /// товара сбрасываются вместе с золотом (ТЗ 2.2 §4.6, Р3).
        /// </summary>
        public static OutpostRun CreateRun(OutpostProfile profile, int zone, OutpostMap map, OutpostConfigSet configs, BalanceRepository repo, long nowUtcMs)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Number != zone) throw new ArgumentException("Карта " + map.ZoneId + " не соответствует зоне " + zone);
            var balance = configs.Balance.Clone();
            // Профиль b6: базовые HP §12.4, затем множитель темпа шахты K(z) для r/x/p (OutpostBalanceConfig.PaceScale).
            var tb = OutpostLevelBalanceAdapter.ApplyPace(map, balance, OutpostLevelBalanceAdapter.Compute(map, balance, repo));
            CardService.ReachZone(profile, zone);
            // Вход в шахту: однократные выдачи при открытии разделов (карточка шахты 2, стартовая кирка шахты 3) — в этом же профиле.
            ProgressionService.ApplyEntryGrants(profile, zone, configs);
            // «Плакат найма»: котики сразу с начала шахты, из предела «Команды» (TeamUpgradeService.Quote уменьшает покупаемые уровни).
            int extraWorkers = CardService.EffectWhole(profile, configs.Cards, CardEffectKind.StartWorkers);
            var run = new OutpostRun
            {
                RunId = OutpostIds.RunId(profile.NextRunSeq++),
                Zone = zone,
                ZoneId = map.ZoneId,
                SourceSha256 = map.SourceSha256 ?? repo.Sha256,
                RulesVersion = OutpostIds.RulesVersion,
                AdapterVersion = OutpostIds.AdapterVersion,
                MapOverride = OutpostJson.DeepCopy(map.Override),
                MapTopology = OutpostJson.DeepCopy(map.Topology),
                MapDressing = OutpostJson.DeepCopy(map.Dressing),
                MapFinal = OutpostJson.DeepCopy(map.Final),
                Balance = balance,
                PriceFactor = DressingPriceFactor(map, tb),
                Phase = RunPhase.Running,
                Now = 0,
                LastProcessedUtcMs = nowUtcMs,
                Wallet = BigInteger.Zero,
                TotalCredited = BigInteger.Zero,
                TotalSpent = BigInteger.Zero,
                PlanningDirty = true,
                ExtraWorkers = extraWorkers,
            };
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var mt = map.Targets[i];
                var b = tb[i];
                long units = checked(b.MaxHp * OutpostIds.HpUnitsPerHp);
                run.Targets.Add(new TargetState
                {
                    Id = mt.Id,
                    Kind = mt.Kind,
                    MaxHpUnits = units,
                    RemainingHpUnits = units,
                    TotalUnits = b.TotalCargoUnits,
                    ReleasedUnits = 0,
                    ValueFactor = b.ValueFactor,
                });
            }
            AddFactories(run, map, configs);
            for (int i = 0; i < balance.StartWorkers + extraWorkers; i++)
            {
                int id = run.NextWorkerId++;
                run.Workers.Add(new WorkerState
                {
                    Id = id,
                    Look = (id - 1) % OutpostSimulation.LookCount,
                    Mode = WorkerMode.ChoosingTask,
                    Cell = map.SpawnCell,
                });
            }
            ProgressionService.Sync(profile, run, configs);
            var sim = new OutpostSimulation(new OutpostGameState { Profile = profile, Run = run }, map, configs) { RecordEvents = false };
            sim.PlanNow();
            return run;
        }

        /// <summary>
        /// Удорожание улучшений от обстановки (<see cref="OutpostRun.PriceFactor"/>): стоимость груза всех целей карты (порции ×
        /// коэффициент цены) к стоимости груза целей источника, вверх до сотых. Ранги и HP целей источника обстановка не меняет
        /// (руда-заполнитель — камень-шаблон с уже известным HP, мусор — с фиксированным рангом), поэтому знаменатель — та же
        /// шахта без обстановки. Заводы груза не дают; шахта без обстановки — 1.
        /// </summary>
        public static Rational DressingPriceFactor(OutpostMap map, TargetBalance[] balances)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (balances == null || balances.Length != map.Targets.Length) throw new ArgumentException("balances");
            Rational all = Rational.Zero, source = Rational.Zero;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var value = balances[i].ValueFactor * balances[i].TotalCargoUnits;
                if (map.Targets[i].IsFinal || map.Targets[i].IsFinalApproach) continue;
                all += value;
                if (map.Targets[i].Piece == null) source += value;
            }
            if (source.Sign <= 0 || all <= source) return Rational.One;
            return new Rational((all * 100 / source).Ceil(), 100);
        }

        /// <summary>
        /// Заводы шахты (ТЗ 2.2 §10.2): снимок factory_rules в run и по заводу на каждую цель <c>s</c> карты (по TargetId, Locked);
        /// склад, пришедшее и проданное — нулевые записи всех товаров шахты (одинаковая форма снимка онлайн, офлайн и после
        /// загрузки). Без правил в наборе run создаётся без заводов.
        /// </summary>
        static void AddFactories(OutpostRun run, OutpostMap map, OutpostConfigSet configs)
        {
            if (configs?.FactoryRules == null) return;
            run.FactoryRules = OutpostJson.DeepCopy(configs.FactoryRules);
            foreach (var mf in map.Factories)
            {
                var rule = run.FactoryRules.ForType(mf.TypeId);
                if (rule == null)
                    throw new OutpostConfigException("factory_rules: нет товара для завода " + mf.TypeId + " (" + map.ZoneId + ", цель " + mf.TargetId + ")");
                run.Factories.Add(new FactoryState
                {
                    TargetId = mf.TargetId,
                    TypeId = mf.TypeId,
                    GoodId = rule.GoodId,
                    Status = FactoryStatus.Locked,
                });
                run.Warehouse[rule.GoodId] = 0;
                run.GoodsArrived[rule.GoodId] = 0;
                run.GoodsSold[rule.GoodId] = 0;
            }
        }

        /// <summary>
        /// Однократная награда первого завершения (ключ "CatOutpostV2:zone{z}:first"): гарантия (поток не продвигается)
        /// или slot = NextInt(4), roll = NextInt(100); детали CompletionParts(z) + конвертация дубликата.
        /// Повтор ключа — ничего не выдаётся (возвращает null). Run.CompletionRewardKey = ключ.
        /// </summary>
        public static RewardRecord ApplyCompletion(OutpostGameState state, OutpostConfigSet configs)
        {
            var run = state.Run;
            var p = state.Profile;
            int z = run.Zone;
            string key = OutpostIds.FirstCompletionKey(z);
            run.CompletionRewardKey = key;
            foreach (var r in p.RewardLog)
                if (r.Key == key) return null;

            string item = configs.Rewards.GuaranteedItem(z);
            bool guaranteed = item != null;
            bool noItem = !guaranteed && configs.Rewards.NoItemFor(z);
            if (!guaranteed && !noItem)
            {
                if (p.RewardRng == null) throw new InvalidOperationException("Профиль без генератора наград");
                int slot = p.RewardRng.NextInt(OutpostIds.SlotCount);
                int roll = p.RewardRng.NextInt(100);
                item = configs.Equipment.Get((EquipSlot)slot, configs.Rewards.RarityForRoll(roll)).Id;
            }
            // Шахта без предмета (rewards-2): только детали завершения; ItemId записи — null.
            var grant = noItem ? new GrantResult() : EquipmentService.Grant(p, item, configs);
            int parts = configs.Rewards.CompletionParts(z);
            // «Клубок мастера»: +доля деталей за завершение (floor), только к деталям завершения, не к дубликату.
            var partsBonus = CardService.EffectSum(p, configs.Cards, CardEffectKind.CompletionParts);
            if (partsBonus.Sign > 0) parts = ExactMath.ToIntChecked((new Rational(parts) * (Rational.One + partsBonus)).Floor(), "CompletionParts");
            p.Parts += parts;
            var record = new RewardRecord
            {
                Key = key,
                Zone = z,
                RunId = run.RunId,
                ItemId = item,
                Duplicate = grant.Duplicate,
                DuplicateParts = grant.Parts,
                CompletionParts = parts,
                Guaranteed = guaranteed,
            };
            p.RewardLog.Add(record);
            int at = p.CompletedZones.BinarySearch(z);
            if (at < 0) p.CompletedZones.Insert(~at, z);
            if (z >= OutpostIds.LastZone) p.CampaignComplete = true;
            return record;
        }

        /// <summary>Запись награды завершения текущего run или null.</summary>
        public static RewardRecord CompletionReward(OutpostGameState state)
        {
            string key = state.Run?.CompletionRewardKey;
            if (key == null) return null;
            foreach (var r in state.Profile.RewardLog)
                if (r.Key == key) return r;
            return null;
        }

        /// <summary>
        /// Карта run совпадает с текущей конфигурацией: копия override в run равна override зоны в <paramref name="configs"/>
        /// (оба null — исходная карта).
        /// </summary>
        public static bool IsRunMapCurrent(OutpostRun run, OutpostConfigSet configs)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            var current = configs.Overrides?.For(run.ZoneId);
            if (current != null && !current.HasGeometry()) current = null;
            return ZoneOverride.SameAs(run.MapOverride, current);
        }

        /// <summary>
        /// Перезапуск текущей шахты на актуальной карте (геометрия зоны изменилась, ТЗ 8.5): новый run той же зоны с уровнями 0,
        /// двумя котиками и пустым кошельком; профиль, предметы, детали, награды и настройки сохраняются.
        /// Завершённую шахту (ReadyToContinue) не перезапускает — возвращает false; её карта восстанавливается по копии override.
        /// </summary>
        public static bool RestartRunIfMapChanged(OutpostGameState state, BalanceRepository repo, OutpostConfigSet configs, long nowUtcMs,
            out OutpostMap map)
        {
            if (state?.Run == null || state.Profile == null) throw new ArgumentException("Нет состояния шахты", nameof(state));
            map = null;
            var run = state.Run;
            if (run.Phase == RunPhase.ReadyToContinue || IsRunMapCurrent(run, configs)) return false;
            map = OutpostLevelAdapter.BuildMine(repo, run.Zone, configs);
            state.Run = CreateRun(state.Profile, run.Zone, map, configs, repo, Math.Max(nowUtcMs, run.LastProcessedUtcMs));
            return true;
        }

        /// <summary>
        /// Следующий снимок в памяти (глубокая копия профиля, CurrentZone = z+1, новый run) — только при ReadyToContinue
        /// и z &lt; 144. Исходное состояние не меняется.
        /// </summary>
        public static TransitionResult PrepareTransition(OutpostGameState state, BalanceRepository repo, OutpostConfigSet configs, long nowUtcMs)
        {
            var run = state.Run;
            if (run == null || run.Phase != RunPhase.ReadyToContinue) return new TransitionResult { Reason = "phase" };
            if (run.Zone >= OutpostIds.LastZone || run.Zone >= repo.ZoneCount) return new TransitionResult { Reason = "campaign" };
            var profile = OutpostJson.DeepCopy(state.Profile);
            int next = run.Zone + 1;
            profile.CurrentZone = next;
            CardService.ReachZone(profile, next);
            var map = OutpostLevelAdapter.BuildMine(repo, next, configs);
            var newRun = CreateRun(profile, next, map, configs, repo, Math.Max(nowUtcMs, run.LastProcessedUtcMs));
            return new TransitionResult { Ok = true, State = new OutpostGameState { Profile = profile, Run = newRun }, Map = map };
        }
    }
}
