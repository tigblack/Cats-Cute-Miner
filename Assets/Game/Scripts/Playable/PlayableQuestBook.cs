using System.Collections.Generic;
using CatMiners.Core.Outpost;

namespace CatMiners.Playable
{
    /// <summary>
    /// Что засчитывает квест playable (Docs/Playable/PLAYABLE_TZ.md §3.3). Прогресс — по состоянию шахты и профиля (работает и
    /// после офлайна и тайм-скипа); удары игрока, улучшения карточек и сундуки магазина — по событиям ядра.
    /// </summary>
    public enum QuestKind
    {
        /// <summary>Удары кирки игрока (TargetStruck) с начала квеста.</summary>
        Strike,
        /// <summary>Уровни ветки <see cref="QuestDef.Branch"/>, купленные с начала квеста (найм — ветка Team).</summary>
        Upgrade,
        /// <summary>Уровень ветки <see cref="QuestDef.Branch"/> не ниже Goal (веха «×2» кирки).</summary>
        Level,
        /// <summary>Котиков в шахте не меньше Goal.</summary>
        Cats,
        /// <summary>Золото, доставленное с начала квеста (Counters.DeliveredValue).</summary>
        Deliver,
        /// <summary>
        /// Лучший полученный предмет надет в каждом слоте, где предметы есть (счётчик: такие слоты / слоты с предметами).
        /// Goal — сколько слотов с предметами нужно, чтобы квест имел смысл (4 — полный комплект).
        /// </summary>
        Equip,
        /// <summary>Сундуки с начала квеста: найденные в шахте (Counters.ChestsOpened, с сундуком выхода) и из магазина.</summary>
        Chest,
        /// <summary>Улучшения карточек (CardUpgraded) с начала квеста.</summary>
        Card,
        /// <summary>Тайм-скипы, использованные с начала квеста (итог скипа PendingSkip, не разница запаса).</summary>
        Skip,
        /// <summary>Ворота, открытые с начала квеста.</summary>
        Gate,
        /// <summary>Шахта пройдена (ReadyToContinue). Последний квест каждой шахты: награда приходит в начале следующей.</summary>
        Clear,
    }

    /// <summary>Запись цепочки квестов: что считать, тексты (Loc «playable.quest.{Text}.*»), иконка и множитель награды.</summary>
    public sealed class QuestDef
    {
        public QuestKind Kind;
        /// <summary>Основа ключей словаря: .title (жёлтое название), .step (шаг, «{0}» — цель или номер шахты), .line (наставник).</summary>
        public string Text;
        public UpgradeBranch Branch;
        public int Goal = 1;
        public string Icon;
        /// <summary>Награда золотом — столько процентов цены следующего полезного уровня (PlayableQuests.RewardFor).</summary>
        public int RewardPercent = 150;

        public string TitleKey => "playable.quest." + Text + ".title";
        public string StepKey => "playable.quest." + Text + ".step";
        public string LineKey => "playable.quest." + Text + ".line";

        /// <summary>Квест покупки уровней: его цена — ориентир награды предыдущего квеста.</summary>
        public bool IsPurchase => Kind == QuestKind.Upgrade || Kind == QuestKind.Level || Kind == QuestKind.Cats;

        /// <summary>Ветка покупки квеста (котики — Team).</summary>
        public UpgradeBranch PurchaseBranch => Kind == QuestKind.Cats ? UpgradeBranch.Team : Branch;
    }

    /// <summary>
    /// Цепочка квестов playable по шахтам (ТЗ playable §3.3, reference.md §8 п. 1): шахта 1 — удар, кирки, найм, доставка
    /// (новая цель каждые 20–40 с); шахты 2–4 — экипировка, веха «×2», найм, сумки, сундук, карточка, тайм-скип; шахты 5–10 —
    /// ворота, сектора, полный комплект, большая команда. Каждая шахта заканчивается квестом <see cref="QuestKind.Clear"/>;
    /// после шахты 10 (конец отрезка) — хвост из одной покупки и «Clear Mine N». Недостижимые в этой шахте цели (ворот нет,
    /// надевать нечего, карточку не улучшить) пропускаются при старте: тупиков нет.
    /// </summary>
    public static class PlayableQuestBook
    {
        static QuestDef Strike(int n) => new QuestDef { Kind = QuestKind.Strike, Text = "strike", Goal = n, Icon = "icon_pickaxe", RewardPercent = 150 };
        static QuestDef Upgrade(string text, UpgradeBranch branch, int n, string icon) =>
            new QuestDef { Kind = QuestKind.Upgrade, Text = text, Branch = branch, Goal = n, Icon = icon };
        static QuestDef Hire(string text, int n) => Upgrade(text, UpgradeBranch.Team, n, "icon_branch_team");
        static QuestDef Level(string text, UpgradeBranch branch, int level, string icon) =>
            new QuestDef { Kind = QuestKind.Level, Text = text, Branch = branch, Goal = level, Icon = icon, RewardPercent = 200 };
        static QuestDef Cats(int n) => new QuestDef { Kind = QuestKind.Cats, Text = "cats", Goal = n, Icon = "icon_cat", RewardPercent = 200 };
        static QuestDef Deliver(int gold) => new QuestDef { Kind = QuestKind.Deliver, Text = "deliver", Goal = gold, Icon = "icon_coin" };
        static QuestDef Gear() => new QuestDef { Kind = QuestKind.Equip, Text = "gear", Goal = 1, Icon = "icon_equipment" };
        static QuestDef FullKit() => new QuestDef { Kind = QuestKind.Equip, Text = "fullkit", Goal = 4, Icon = "icon_equipment", RewardPercent = 200 };
        static QuestDef Chest() => new QuestDef { Kind = QuestKind.Chest, Text = "chest", Icon = "icon_chest" };
        static QuestDef Card() => new QuestDef { Kind = QuestKind.Card, Text = "card", Icon = "icon_cards" };
        static QuestDef Skip() => new QuestDef { Kind = QuestKind.Skip, Text = "skip", Icon = "icon_clock" };
        static QuestDef Gate(string text) => new QuestDef { Kind = QuestKind.Gate, Text = text, Icon = "icon_gate", RewardPercent = 200 };
        static QuestDef Clear() => new QuestDef { Kind = QuestKind.Clear, Text = "clear", Icon = "icon_exit", RewardPercent = 300 };

        static readonly QuestDef[][] s_Mines =
        {
            // 1: ~2,6 мин — пять целей по 20–40 с (порядок черновика ТЗ).
            new[] { Strike(5), Upgrade("pickaxes", UpgradeBranch.Pickaxes, 2, "icon_branch_pickaxes"), Hire("hire_one", 1), Deliver(300), Clear() },
            // 2: награда шахты 1 — кирка; веха «×2» на 5-м уровне (профиль p3).
            new[] { Gear(), Level("power", UpgradeBranch.Pickaxes, 5, "icon_branch_pickaxes"), Hire("hire", 3), Clear() },
            // 3: шляпа; первые сундуки и карточки.
            new[] { Gear(), Upgrade("bags", UpgradeBranch.Bags, 2, "icon_branch_bags"), Chest(), Card(), Clear() },
            // 4: рюкзак; тайм-скип из стартового набора.
            new[] { Gear(), Skip(), Hire("hire", 3), Card(), Clear() },
            // 5: сапоги — полный комплект; первые ворота.
            new[] { FullKit(), Gate("gate"), Cats(10), Clear() },
            new[] { Gear(), Gate("gate"), Upgrade("unload", UpgradeBranch.Unload, 3, "icon_branch_unload"), Clear() },
            new[] { Card(), Gate("gate"), Chest(), Clear() },
            new[] { Gear(), Cats(12), Gate("gate"), Clear() },
            new[] { Card(), Gate("gate"), Upgrade("boots", UpgradeBranch.Boots, 3, "icon_branch_boots"), Clear() },
            // 10: редкая кирка, двое ворот — конец отрезка.
            new[] { Gear(), Gate("gate"), Gate("sector"), Clear() },
        };

        static readonly QuestDef[] s_Tail = { Gear(), Upgrade("pickaxes", UpgradeBranch.Pickaxes, 3, "icon_branch_pickaxes"), Clear() };

        /// <summary>Шахт с собственной цепочкой; дальше — хвост.</summary>
        public static int MineCount => s_Mines.Length;

        /// <summary>Цепочка шахты <paramref name="zone"/>; последний элемент — всегда <see cref="QuestKind.Clear"/>.</summary>
        public static IReadOnlyList<QuestDef> ForMine(int zone) => zone >= 1 && zone <= s_Mines.Length ? s_Mines[zone - 1] : s_Tail;

        /// <summary>Все записи (проверка словаря в тестах).</summary>
        public static IEnumerable<QuestDef> All()
        {
            foreach (var mine in s_Mines)
                foreach (var q in mine) yield return q;
            foreach (var q in s_Tail) yield return q;
        }
    }
}
