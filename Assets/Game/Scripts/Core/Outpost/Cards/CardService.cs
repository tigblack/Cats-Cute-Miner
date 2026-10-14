using System;
using System.Collections.Generic;

namespace CatMiners.Core.Outpost
{
    /// <summary>Состояние карточки для интерфейса (CardView): шесть состояний задания «возвращение карточек».</summary>
    public enum CardViewState
    {
        /// <summary>Шахта открытия ещё не достигнута: арт виден, слегка затемнён, «Откроется на шахте N».</summary>
        Locked,
        /// <summary>Шахта достигнута, копий нет: «Доступна в сундуках».</summary>
        Available,
        /// <summary>Копии есть, особая активация не выполнена (в правилах cards-1 не встречается: первая копия активирует).</summary>
        Collecting,
        /// <summary>Получена и активна.</summary>
        Active,
        /// <summary>Хватает копий и эликсира на улучшение.</summary>
        Upgradable,
        /// <summary>Максимальный уровень.</summary>
        Max,
    }

    /// <summary>Котировка улучшения карточки L → L+1.</summary>
    public struct CardUpgradeQuote
    {
        public int Level;
        public int MaxLevel;
        /// <summary>Нужно копий и эликсира (0 — на максимуме или не получена).</summary>
        public int CopiesNeeded;
        public long ElixirNeeded;
        public long CopiesOwned;
        /// <summary>Причина отказа: "not-owned", "max", "copies", "elixir"; null — можно улучшить.</summary>
        public string BlockReason;

        public bool CanUpgrade => BlockReason == null;
    }

    /// <summary>
    /// Карточки профиля: выдача копий, улучшение по CardUpgradeCosts, доступность по MineUnlock, однократная миграция профиля
    /// и перенос карточек из прежнего сохранения. Прежние правила (GameSession.Rewards/Commands): первая копия даёт уровень 1,
    /// переход L → L+1 списывает Duplicates[L−1] копий и SoftCurrency[L−1] эликсира, максимум — 1 + длина массивов.
    /// </summary>
    public static class CardService
    {
        public const string TutorialKey = "CatOutpostV2:cards:tutorial-first-delivery";
        public const string LegacyImportKey = "CatOutpostV2:cards:legacy-import";

        public static CardProgress ProgressOf(OutpostProfile p, string cardId) =>
            p?.Cards != null && cardId != null && p.Cards.TryGetValue(cardId, out var c) ? c : null;

        /// <summary>Доступна ли карточка по прогрессу: максимальная достигнутая шахта ≥ MineUnlock.</summary>
        public static bool IsUnlocked(OutpostProfile p, CardDef def) => p != null && def != null && p.HighestZone >= def.MineUnlock;

        /// <summary>
        /// Выдать копии: первая копия даёт уровень 1, остальные копятся. true — карточка стала активной (была не получена).
        /// Копии не теряются на максимальном уровне (хранятся без эффекта).
        /// </summary>
        public static bool Grant(OutpostProfile p, CardDef def, long copies)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (copies <= 0) return false;
            if (p.Cards == null) p.Cards = new Dictionary<string, CardProgress>(StringComparer.Ordinal);
            if (!p.Cards.TryGetValue(def.Id, out var c) || c == null)
            {
                c = new CardProgress();
                p.Cards[def.Id] = c;
            }
            bool activated = false;
            if (c.Level < 1)
            {
                c.Level = 1;
                copies--;
                activated = true;
            }
            c.Copies = checked(c.Copies + copies);
            return activated;
        }

        public static CardUpgradeQuote Quote(OutpostProfile p, CardDef def)
        {
            var c = ProgressOf(p, def.Id);
            var q = new CardUpgradeQuote { Level = c?.Level ?? 0, MaxLevel = def.MaxLevel, CopiesOwned = c?.Copies ?? 0 };
            if (q.Level < 1)
            {
                q.BlockReason = "not-owned";
                return q;
            }
            if (q.Level >= def.MaxLevel)
            {
                q.BlockReason = "max";
                return q;
            }
            q.CopiesNeeded = def.UpgradeCopies[q.Level - 1];
            q.ElixirNeeded = def.UpgradeElixir[q.Level - 1];
            if (q.CopiesOwned < q.CopiesNeeded) q.BlockReason = "copies";
            else if (p.Elixir < q.ElixirNeeded) q.BlockReason = "elixir";
            return q;
        }

        /// <summary>Улучшение одной транзакцией: списать копии и эликсир, повысить уровень. Отказ ничего не меняет (причина или null).</summary>
        public static string Upgrade(OutpostProfile p, CardDef def, out CardUpgradeQuote quote)
        {
            quote = Quote(p, def);
            if (!quote.CanUpgrade) return quote.BlockReason;
            var c = p.Cards[def.Id];
            c.Copies -= quote.CopiesNeeded;
            p.Elixir -= quote.ElixirNeeded;
            c.Level++;
            return null;
        }

        public static CardViewState StateOf(OutpostProfile p, CardDef def)
        {
            int level = p.CardLevel(def.Id);
            if (level >= def.MaxLevel) return CardViewState.Max;
            if (level >= 1) return Quote(p, def).CanUpgrade ? CardViewState.Upgradable : CardViewState.Active;
            return IsUnlocked(p, def) ? CardViewState.Available : CardViewState.Locked;
        }

        /// <summary>Есть ли карточка, которую можно улучшить (значок на кнопке «Карты»).</summary>
        public static bool AnyUpgradable(OutpostProfile p, CardCatalog cat)
        {
            if (p?.Cards == null || cat == null) return false;
            foreach (var kv in p.Cards)
            {
                var def = cat.Get(kv.Key);
                if (def != null && kv.Value != null && kv.Value.Level >= 1 && Quote(p, def).CanUpgrade) return true;
            }
            return false;
        }

        /// <summary>Сумма эффектов вида kind активных карточек профиля (для правил вне TeamStats: детали, офлайн, проверки).</summary>
        public static Rational EffectSum(OutpostProfile p, CardCatalog cat, CardEffectKind kind)
        {
            var sum = Rational.Zero;
            if (p?.Cards == null || cat == null) return sum;
            foreach (var kv in p.Cards)
            {
                if (kv.Value == null || kv.Value.Level < 1) continue;
                var def = cat.Get(kv.Key);
                if (def != null && def.Effect.Kind == kind) sum += def.EffectAt(kv.Value.Level);
            }
            return sum;
        }

        /// <summary>Целая часть суммы эффектов (котики, места, часы, карты) — каждая карточка округляется вниз отдельно, как в TeamStats.</summary>
        public static int EffectWhole(OutpostProfile p, CardCatalog cat, CardEffectKind kind)
        {
            int sum = 0;
            if (p?.Cards == null || cat == null) return 0;
            foreach (var kv in p.Cards)
            {
                if (kv.Value == null || kv.Value.Level < 1) continue;
                var def = cat.Get(kv.Key);
                if (def != null && def.Effect.Kind == kind) sum += ExactMath.ToIntChecked(def.EffectAt(kv.Value.Level).Floor(), "card effect");
            }
            return sum;
        }

        /// <summary>Достигнута шахта zone (вход в неё): поднимает HighestZone, ничего не выдаёт.</summary>
        public static void ReachZone(OutpostProfile p, int zone)
        {
            if (p != null && zone > p.HighestZone) p.HighestZone = zone;
        }

        /// <summary>
        /// Однократная миграция профиля, созданного до возвращения карточек (CardsVersion 0 → 1): HighestZone из текущей и
        /// завершённых шахт, генератор сундуков из seed профиля, учебная выдача помечается выполненной (первая доставка уже была).
        /// Уже полученные данные карточек не трогает. true — профиль изменён (контроллер сразу записывает снимок).
        /// </summary>
        public static bool MigrateProfile(OutpostProfile p, CardRules rules)
        {
            if (p == null || rules == null || p.CardsVersion >= 1) return false;
            if (p.Cards == null) p.Cards = new Dictionary<string, CardProgress>(StringComparer.Ordinal);
            if (p.RockCycles == null) p.RockCycles = new Dictionary<string, int>(StringComparer.Ordinal);
            if (p.CardKeys == null) p.CardKeys = new List<string>();
            int highest = Math.Max(1, p.CurrentZone);
            if (p.CompletedZones != null)
                foreach (int z in p.CompletedZones) highest = Math.Max(highest, z);
            p.HighestZone = Math.Max(p.HighestZone, highest);
            if (p.CardRng == null) p.CardRng = new Pcg32(p.Seed, rules.CardRngStream);
            if (!p.CardKeys.Contains(TutorialKey)) p.CardKeys.Add(TutorialKey);
            p.CardsVersion = 1;
            return true;
        }

        /// <summary>
        /// Перенос карточек и эликсира прежнего режима по стабильным ID (один раз, ключ <see cref="LegacyImportKey"/>). Карточка,
        /// уже полученная в новом профиле, остаётся как есть (актуальные данные не перезаписываются архивом). Неизвестные ID
        /// пропускаются. true — что-то перенесено или ключ поставлен.
        /// </summary>
        public static bool ImportLegacy(OutpostProfile p, CardCatalog cat, IReadOnlyDictionary<string, (int Level, long Duplicates)> cards,
            long elixir, out int importedCards)
        {
            importedCards = 0;
            if (p == null || cat == null) return false;
            if (p.CardKeys == null) p.CardKeys = new List<string>();
            if (p.CardKeys.Contains(LegacyImportKey)) return false;
            if (p.Cards == null) p.Cards = new Dictionary<string, CardProgress>(StringComparer.Ordinal);
            if (cards != null)
                foreach (var kv in cards)
                {
                    var def = cat.Get(kv.Key);
                    if (def == null || kv.Value.Level < 1) continue;
                    if (p.CardLevel(def.Id) >= 1) continue;
                    p.Cards[def.Id] = new CardProgress { Level = Math.Min(kv.Value.Level, def.MaxLevel), Copies = Math.Max(0, kv.Value.Duplicates) };
                    importedCards++;
                }
            if (elixir > 0) p.Elixir = checked(p.Elixir + elixir);
            p.CardKeys.Add(LegacyImportKey);
            return true;
        }
    }
}
