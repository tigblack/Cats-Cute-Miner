using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Core: тексты, которые формирует CatMiners.Core для игрока, — причины отказа команд
        /// (<c>GameSession.Describe</c>, событие CommandRejected), условия перехода (<c>CanCompleteZone</c>),
        /// требование автоматизации шахты, доход порога, редкости карточек и единицы длительности (<c>NumberFormat.FormatDuration</c>).
        /// </summary>
        static partial void RegisterCore(Dictionary<string, string> d)
        {
            // Причины отказа команд (CommandResult).
            d.Add("core.reject.not_enough_gold", "Not enough gold");
            d.Add("core.reject.not_enough_elixir", "Not enough elixir");
            d.Add("core.reject.not_enough_duplicates", "Not enough card copies");
            d.Add("core.reject.cap_reached", "Cat limit reached");
            d.Add("core.reject.no_space", "No space");
            d.Add("core.reject.no_space_at_target", "No space at the target");
            d.Add("core.reject.different_levels", "Cats must be the same level");
            d.Add("core.reject.invalid_target", "Can't go there");
            d.Add("core.reject.closed_sector", "Sector is closed");
            d.Add("core.reject.max_level", "Max level");
            d.Add("core.reject.effect_not_available", "Effect coming later");
            d.Add("core.reject.not_found", "Not found");
            d.Add("core.reject.not_allowed", "Not allowed");

            // Требование автоматизации ручной шахты: {0} — карточка из токена, {1} — нужный уровень, {2} — что есть.
            d.Add("core.automation.requirement", "Needs blueprint {0} Lv. {1} (have: {2})");
            d.Add("core.automation.have_level", "Lv. {0}");
            d.Add("core.automation.have_none", "none");

            // Доход порога генератора: {0} — множитель выхода, {1} — начисленный эликсир (уже отформатированы).
            d.Add("core.threshold.reward", "x{0}, +{1} elixir");

            // Почему нельзя перейти в следующую шахту (CanCompleteZone).
            d.Add("core.complete.campaign_completed", "Campaign complete");
            d.Add("core.complete.exit_closed", "The exit is not open yet");
            d.Add("core.complete.shafts_inactive", "Shafts activated: {0}/{1}");
            d.Add("core.complete.gates_closed", "Not all gates are open");

            // Редкости карточек (Rarity.Name).
            d.Add("core.rarity.common", "Common");
            d.Add("core.rarity.uncommon", "Uncommon");
            d.Add("core.rarity.rare", "Rare");
            d.Add("core.rarity.epic", "Epic");
            d.Add("core.rarity.legendary", "Legendary");
            d.Add("core.rarity.majestic", "Majestic");
            d.Add("core.rarity.unknown", "?");

            // Длительность (NumberFormat.FormatDuration): «45s», «1m 05s», «2h 03m», «2d 5h».
            d.Add("core.duration.unknown", "—");
            d.Add("core.duration.seconds", "{0}s");
            d.Add("core.duration.minutes_seconds", "{0}m {1:00}s");
            d.Add("core.duration.hours_minutes", "{0}h {1:00}m");
            d.Add("core.duration.days_hours", "{0}d {1}h");
        }
    }
}
