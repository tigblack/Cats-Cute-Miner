using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Открытие функций первой сессии по <see cref="ProgressionConfig"/> (Docs/Rework/FIRST_SESSION.md). Доступность считается
    /// только из фактов состояния — наибольшей достигнутой шахты, первой доставки и покупки, полученных карточек и предметов,
    /// заводов текущей шахты, — и записывается в профиль навсегда (<see cref="ProgressionState.Unlocked"/>): новая шахта ничего
    /// не закрывает. Однократные выдачи при открытии разделов (карточка и эликсир на первое улучшение, стартовая кирка) хранят
    /// квитанцию в том же снимке, что и выдача, поэтому повтор, перезапуск и повторная запись ничего не удваивают. Equipment requires Bruno and actual Mine 2 completion. Показ сцен
    /// спасения Mia — забота интерфейса (<see cref="ProgressionState.Shown"/>); здесь только то, что меняет игру.
    /// </summary>
    public static class ProgressionService
    {
        /// <summary>Отметка показа: кадр клетки в начале шахты спасения (реплика Mia).</summary>
        public const string CalloutShown = "intro.rescue";
        /// <summary>Префикс отметки показа окна после спасения: "reveal.&lt;персонаж&gt;".</summary>
        public const string RevealPrefix = "reveal.";
        public const string GrantCards = "grant.cards";
        public const string GrantEquipment = "grant.equipment";
        public const string GrantBlacksmithService = "grant.service.bruno";
        public const string EquipmentIntroShown = "intro.equipment.bruno";
        public const string CardsIntroShown = "intro.cards.support";
        public const string StoreWelcomeShown = "intro.store.mia.welcome";
        public const int CurrentVersion = 2;

        public static string CalloutKey(string character) => character == "mia" ? CalloutShown : CalloutShown + "." + character;
        public static string RescueCharacter(OutpostConfigSet configs, int zone) => configs?.Progression?.RescueForZone(zone)?.Character;
        public static bool BlacksmithRescued(OutpostProfile p, OutpostConfigSet configs) =>
            Active(p, configs) && configs.Progression.Blacksmith != null && p.Progression.HasCrew(configs.Progression.Blacksmith.Character);
        public static bool Mine2Completed(OutpostProfile p) => p?.CompletedZones != null && p.CompletedZones.Contains(2);
        public static bool EquipmentReady(OutpostProfile p, OutpostConfigSet configs) => Mine2Completed(p) && BlacksmithRescued(p, configs);

        /// <summary>Правила прогрессии подключены и профиль переведён на них (Version ≥ 1).</summary>
        public static bool Active(OutpostProfile p, OutpostConfigSet configs) =>
            configs?.Progression != null && p?.Progression != null && p.Progression.Version >= 1;

        /// <summary>Функция доступна (без правил или для профиля до миграции — всё доступно, как раньше).</summary>
        public static bool IsAvailable(OutpostProfile p, OutpostConfigSet configs, string feature) =>
            !Active(p, configs) || p.Progression.IsUnlocked(feature);

        public static bool BranchAvailable(OutpostProfile p, OutpostConfigSet configs, UpgradeBranch branch) =>
            IsAvailable(p, configs, ProgressionConfig.BranchFeature(branch));

        /// <summary>
        /// Карточки ещё не открыты и шахта <paramref name="zone"/> раньше шахты их открытия: находки шахты (сундуки целей, эликсир
        /// камней) не выдаются, чтобы карточки пришли вместе с разделом, а не случайной находкой. Профиль до прогрессии — false.
        /// </summary>
        public static bool CardsHeld(OutpostProfile p, OutpostConfigSet configs, int zone)
        {
            if (!Active(p, configs) || p.Progression.IsUnlocked(ProgressionConfig.Cards)) return false;
            var cards = configs.Progression.Feature(ProgressionConfig.Cards);
            return cards != null && cards.Zone > 0 && zone < cards.Zone;
        }

        /// <summary>Персонаж спасения шахты 1 (null — правил спасения нет).</summary>
        public static string RescueCharacter(OutpostConfigSet configs) => configs?.Progression?.Rescue?.Character;

        /// <summary>
        /// Персонаж спасения свободен: спасён в профиле или профиль уже дальше шахты спасения (переход DEV-панелью). Тогда он
        /// встречает игрока в магазине. Без правил спасения — false.
        /// </summary>
        public static bool Rescued(OutpostProfile p, OutpostConfigSet configs)
        {
            var rescue = configs?.Progression?.Rescue;
            if (rescue == null || !Active(p, configs)) return false;
            return p.Progression.HasCrew(rescue.Character) || p.HighestZone > rescue.Zone;
        }

        /// <summary>
        /// Новый профиль: прогрессия с первой шахты. Учебная карточка первой доставки больше не выдаётся (карточки открываются в
        /// шахте 2) — её ключ сразу помечен выполненным.
        /// </summary>
        public static void InitNewProfile(OutpostProfile p, OutpostConfigSet configs)
        {
            if (p == null || configs?.Progression == null) return;
            p.Progression = new ProgressionState { Version = CurrentVersion };
            if (p.CardKeys == null) p.CardKeys = new List<string>();
            if (!p.CardKeys.Contains(CardService.TutorialKey)) p.CardKeys.Add(CardService.TutorialKey);
        }

        /// <summary>
        /// Загрузка снимка: профиль до прогрессии — <see cref="MigrateProfile"/>; затем отметки по текущим правилам (функция,
        /// добавленная в progression.json после создания профиля, — например Store, — открывается по тем же фактам); облик
        /// персонажа спасения снимается с котиков начатой шахты (раньше Mia была стартовым котиком, теперь она в магазине).
        /// true — снимок изменён (его нужно записать).
        /// </summary>
        public static bool PrepareLoaded(OutpostProfile p, OutpostRun run, OutpostConfigSet configs)
        {
            bool changed = MigrateProfile(p, run, configs);
            if (!Active(p, configs)) return changed;
            int receipts = p.Progression.Grants.Count, crew = p.Progression.Crew.Count, items = p.Items?.Count ?? 0;
            int starterLevel = p.ItemLevel(configs.Progression.EquipmentIntro.Item);
            if (Sync(p, run, configs) != null) changed = true;
            if (receipts != p.Progression.Grants.Count || crew != p.Progression.Crew.Count || items != (p.Items?.Count ?? 0) ||
                starterLevel != p.ItemLevel(configs.Progression.EquipmentIntro.Item)) changed = true;
            if (run?.Workers != null)
                foreach (var w in run.Workers)
                    if (w != null && w.Crew != null)
                    {
                        w.Crew = null;
                        changed = true;
                    }
            return changed;
        }

        /// <summary>
        /// Перевести профиль, созданный до прогрессии (Version 0), на правила один раз: открыть всё уже пройденное, спасённый в
        /// пройденной шахте 1 персонаж отмечается спасённым (одно уведомление показывает интерфейс). Выдачи открытия разделов — по
        /// текущей шахте; опытному игроку (уже улучшал карточку или надевал предмет) они не нужны и отмечаются выданными; пустая
        /// экипировка после завершения шахты 2 получает стартовую кирку. Ничего полученного не отнимается. true — профиль изменён.
        /// </summary>
        public static bool MigrateProfile(OutpostProfile p, OutpostRun run, OutpostConfigSet configs)
        {
            if (p == null || configs?.Progression == null) return false;
            if (p.Progression == null) p.Progression = new ProgressionState();
            if (p.Progression.Version >= 1) return MigrateBlacksmith(p, run, configs);
            var s = p.Progression;
            s.Version = 1;
            int highest = Math.Max(Math.Max(1, p.HighestZone), Math.Max(p.CurrentZone, run?.Zone ?? 0));
            if (p.CompletedZones != null)
                foreach (int z in p.CompletedZones) highest = Math.Max(highest, z);
            CardService.ReachZone(p, highest);
            Sync(p, run, configs);
            if (AnyUpgradedCard(p)) AddOnce(s.Grants, GrantCards);

            ApplyEntryGrants(p, p.HighestZone, configs);
            Sync(p, run, configs);
            MigrateBlacksmith(p, run, configs);
            return true;
        }

        /// <summary>Once-only compatible service migration. Never edits a mine or its workers/targets.</summary>
        static bool MigrateBlacksmith(OutpostProfile p, OutpostRun run, OutpostConfigSet configs)
        {
            var s = p.Progression;
            if (s.Version >= CurrentVersion) return false;
            bool previousAccess = s.IsUnlocked(ProgressionConfig.Equipment) || AnyOwnedItem(p, configs.Equipment);
            if (configs.Progression.Blacksmith != null && (Mine2Completed(p) || previousAccess))
            {
                Rescue(p, configs.Progression.Blacksmith.Character);
                AddOnce(s.Unlocked, ProgressionConfig.Equipment);
                // Existing accounts receive the service silently, without replaying rescue or equip lessons.
                AddOnce(s.Shown, RevealPrefix + configs.Progression.Blacksmith.Character);
                AddOnce(s.Shown, CalloutKey(configs.Progression.Blacksmith.Character));
                AddOnce(s.Shown, EquipmentIntroShown);
                if (configs.Progression.Rescue != null && s.HasCrew(configs.Progression.Rescue.Character))
                {
                    AddOnce(s.Shown, RevealPrefix + configs.Progression.Rescue.Character);
                    AddOnce(s.Shown, CalloutKey(configs.Progression.Rescue.Character));
                }
                EnsureStarter(p, configs);
            }
            AddOnce(s.Shown, CardsIntroShown);
            s.Version = CurrentVersion;
            return true;
        }

        static bool AnyOwnedItem(OutpostProfile p, EquipmentCatalog catalog)
        {
            if (p.Items == null) return false;
            foreach (var kv in p.Items)
                if (kv.Value >= 1 && (catalog == null || catalog.TryGet(kv.Key, out _))) return true;
            return false;
        }

        /// <summary>
        /// Факты состояния → постоянные отметки открытия (по порядку правил); персонаж пройденной шахты спасения отмечается
        /// спасённым (шахта, начатая до клетки, завершается без сцены). Возвращает новые отметки по порядку или null.
        /// </summary>
        public static List<string> Sync(OutpostProfile p, OutpostRun run, OutpostConfigSet configs)
        {
            if (!Active(p, configs)) return null;
            var cfg = configs.Progression;
            var s = p.Progression;
            List<string> added = null;
            // Pre-feature in-progress maps finish on their established layout. The service arrives at completion.
            var blacksmith = cfg.Blacksmith;
            if (blacksmith != null && Mine2Completed(p) && !s.HasCrew(blacksmith.Character) &&
                run != null && run.Zone == 2 && run.MapDressing?.Rescue?.Character != blacksmith.Character)
            {
                Rescue(p, blacksmith.Character);
                AddOnce(s.Shown, RevealPrefix + blacksmith.Character);
            }
            foreach (var f in cfg.Features)
            {
                if (f == null || s.IsUnlocked(f.Id) || !Eligible(p, run, configs, f)) continue;
                s.Unlocked.Add(f.Id);
                (added ?? (added = new List<string>())).Add(f.Id);
            }
            var rescue = cfg.Rescue;
            if (rescue != null && !s.HasCrew(rescue.Character) && p.CompletedZones != null && p.CompletedZones.Contains(rescue.Zone))
                s.Crew.Add(rescue.Character);
            if (s.IsUnlocked(ProgressionConfig.Equipment)) EnsureStarter(p, configs);
            return added;
        }

        static bool Eligible(OutpostProfile p, OutpostRun run, OutpostConfigSet configs, FeatureDef f)
        {
            // Store — вместе с Cards: та функция выше по списку, её отметка уже посчитана в этом проходе.
            if (f.With != null) return p.Progression.IsUnlocked(f.With);
            if (f.Event == ProgressionConfig.EventEquipmentReady) return EquipmentReady(p, configs);
            if (f.Zone > 0 && p.HighestZone >= f.Zone) return true;
            switch (f.Event)
            {
                case ProgressionConfig.EventFirstDelivery: return run != null && run.Counters != null && run.Counters.FirstDeliveryTick >= 0;
                case ProgressionConfig.EventFirstPickaxes: return run != null && run.Level(UpgradeBranch.Pickaxes) > 0;
                case ProgressionConfig.EventOwnCards: return AnyCard(p);
                case ProgressionConfig.EventOwnItems: return p.Items != null && p.Items.Count > 0;
                case ProgressionConfig.EventFactories: return run != null && run.Factories != null && run.Factories.Count > 0;
                default: return false;
            }
        }

        /// <summary>
        /// Вход в шахту zone (создание run): однократные выдачи открытия разделов, если их шахта достигнута, — стартовая карточка и
        /// ресурсы на её первые улучшения (по ценам CardUpgradeCosts и запасу игрока), стартовый предмет экипировки, если в его
        /// слоте пусто. Квитанция — в том же профиле: повтор ничего не выдаёт.
        /// </summary>
        public static void ApplyEntryGrants(OutpostProfile p, int zone, OutpostConfigSet configs)
        {
            if (!Active(p, configs)) return;
            var cfg = configs.Progression;
            var s = p.Progression;
            var cards = cfg.Feature(ProgressionConfig.Cards);
            if (cards != null && cards.Zone > 0 && zone >= cards.Zone && !s.HasGrant(GrantCards) && configs.Cards != null)
            {
                var def = configs.Cards.Get(cfg.CardsIntro.Card);
                if (def != null)
                {
                    var need = CardsIntroGrant(p, configs);
                    if (need.Copies > 0) CardService.Grant(p, def, need.Copies);
                    if (need.Elixir > 0) p.Elixir = checked(p.Elixir + need.Elixir);
                    s.Grants.Add(GrantCards);
                }
            }
            if (s.IsUnlocked(ProgressionConfig.Equipment) || EquipmentReady(p, configs)) EnsureStarter(p, configs);
        }

        /// <summary>The old Mine 3 path and completion path share one receipt. A valid owned pickaxe is reused.</summary>
        public static bool EnsureStarter(OutpostProfile p, OutpostConfigSet configs)
        {
            if (!Active(p, configs) || configs.Equipment == null ||
                !configs.Equipment.TryGet(configs.Progression.EquipmentIntro.Item, out var item)) return false;
            var s = p.Progression;
            // Repair an old receipt with no usable pickaxe as well; an owned appropriate item never converts to duplicate parts.
            bool granted = !OwnsSlot(p, configs.Equipment, item.Slot);
            if (granted)
            {
                if (p.Items == null) p.Items = new Dictionary<string, int>();
                if (p.Items.ContainsKey(item.Id)) p.Items[item.Id] = 1;
                else EquipmentService.Grant(p, item.Id, configs);
            }
            bool receipt = AddOnce(s.Grants, GrantEquipment);
            return granted || receipt;
        }

        public static string StarterItem(OutpostProfile p, OutpostConfigSet configs)
        {
            var catalog = configs?.Equipment;
            if (p?.Items == null || catalog == null) return null;
            string best = null;
            Rational bonus = Rational.Zero;
            foreach (var kv in p.Items)
                if (kv.Value >= 1 && catalog.TryGet(kv.Key, out var def) && def.Slot == EquipSlot.Pickaxe)
                {
                    var value = catalog.EffectValue(def, kv.Value);
                    if (best == null || value > bonus) { best = kv.Key; bonus = value; }
                }
            return best;
        }

        /// <summary>
        /// Сколько копий и эликсира не хватает, чтобы стартовая карточка дошла до уровня 1 + Upgrades (первая копия активирует,
        /// далее UpgradeCopies/UpgradeElixir каждого уровня), с учётом копий и эликсира игрока. (0, 0) — уже хватает или выше.
        /// </summary>
        public static (long Copies, long Elixir) CardsIntroGrant(OutpostProfile p, OutpostConfigSet configs)
        {
            var cfg = configs?.Progression;
            var def = cfg != null ? configs.Cards?.Get(cfg.CardsIntro.Card) : null;
            if (def == null || p == null) return (0, 0);
            int target = Math.Min(def.MaxLevel, 1 + Math.Max(0, cfg.CardsIntro.Upgrades));
            var c = CardService.ProgressOf(p, def.Id);
            int level = c?.Level ?? 0;
            if (level >= target) return (0, 0);
            long needCopies = level < 1 ? 1 : 0, needElixir = 0;
            for (int l = Math.Max(1, level); l < target; l++)
            {
                needCopies += def.UpgradeCopies[l - 1];
                needElixir += def.UpgradeElixir[l - 1];
            }
            long haveCopies = level >= 1 ? c.Copies : 0;
            return (Math.Max(0, needCopies - (level < 1 ? 0 : haveCopies)), Math.Max(0, needElixir - p.Elixir));
        }

        /// <summary>Спасение персонажа: отметка в профиле один раз. true — отмечен сейчас.</summary>
        public static bool Rescue(OutpostProfile p, string character)
        {
            if (p == null || string.IsNullOrEmpty(character)) return false;
            if (p.Progression == null) p.Progression = new ProgressionState();
            if (character == ProgressionConfig.BlacksmithCharacter) AddOnce(p.Progression.Grants, GrantBlacksmithService);
            if (p.Progression.HasCrew(character)) return false;
            p.Progression.Crew.Add(character);
            return true;
        }

        /// <summary>Отметить показ сцены спасения или просмотр строки «Базы» (интерфейс). true — отметка новая.</summary>
        public static bool MarkShown(OutpostProfile p, string key) => p?.Progression != null && AddOnce(p.Progression.Shown, key);

        static bool AnyCard(OutpostProfile p)
        {
            if (p.Cards == null) return false;
            foreach (var kv in p.Cards)
                if (kv.Value != null && kv.Value.Level >= 1) return true;
            return false;
        }

        static bool AnyUpgradedCard(OutpostProfile p)
        {
            if (p.Cards == null) return false;
            foreach (var kv in p.Cards)
                if (kv.Value != null && kv.Value.Level >= 2) return true;
            return false;
        }

        static bool OwnsSlot(OutpostProfile p, EquipmentCatalog catalog, EquipSlot slot)
        {
            if (p.Items == null) return false;
            foreach (var kv in p.Items)
                if (kv.Value >= 1 && catalog.TryGet(kv.Key, out var def) && def.Slot == slot) return true;
            return false;
        }

        static bool AddOnce(List<string> list, string key)
        {
            if (list == null || key == null || list.Contains(key)) return false;
            list.Add(key);
            return true;
        }
    }
}
