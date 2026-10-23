using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Controller: тексты GameController: тосты событий, диалоги перехода, сохранения и конца кампании.
        /// Ключи начинаются с «controller.»; строки добавляются через d.Add("controller.группа.имя", "Текст").
        /// </summary>
        static partial void RegisterController(Dictionary<string, string> d)
        {
            // Баланс не загрузился: заголовок окна (текст — сообщение загрузчика баланса).
            d.Add("controller.config_error.title", "Configuration error");

            // Окна сохранения (ТЗ 12.1, 12.2).
            d.Add("controller.save.title", "Save");
            d.Add("controller.save.ok", "Got it");
            d.Add("controller.save.restart", "Start over");
            d.Add("controller.save.rules_changed", "Balance rules were updated: the current mine has restarted, cards and elixir are kept.");
            d.Add("controller.save.restored_from_backup", "The save was restored from a backup.");
            d.Add("controller.save.map_mismatch", "The save does not match the current map data.");
            // Запасная причина, если загрузчик не дал своей; точка добавляется в коде к любой причине.
            d.Add("controller.save.other_balance", "The save was made for a different balance");
            d.Add("controller.save.corrupted", "The save is corrupted and could not be restored from a backup.");
            // {0} — маска отложенных файлов («*.corrupt-*», «*.incompatible-*»).
            d.Add("controller.save.files_kept", "Old files were not deleted: they were renamed to {0} in the save folder.\nThe new game will start saving after you tap Start over.");
            d.Add("controller.save.files_not_kept", "The old file could not be renamed. To avoid overwriting it, the new game will not be saved.");

            // Смена слота сохранения (инструменты разработки).
            d.Add("controller.slot.debug", "DEBUG slot");
            d.Add("controller.slot.main", "Main slot");

            // Тосты событий симуляции. threshold: {0} — текст события (core.threshold.reward); zone_entered: {0} — номер шахты.
            d.Add("controller.toast.shaft_automated", "Shaft automated!");
            d.Add("controller.toast.threshold", "Milestone! Income {0}");
            d.Add("controller.toast.gate_broken", "Gate broken — open it!");
            d.Add("controller.toast.exit_broken", "Exit open!");
            d.Add("controller.toast.shaft_cleared", "Shaft cleared — activate it!");
            d.Add("controller.toast.zone_entered", "Mine {0}");
            // Открыты ворота: без бонусов и с бонусами ({0} — подписи бонусов через «, »).
            d.Add("controller.toast.sector_opened", "New sector opened");
            d.Add("controller.toast.sector_opened_bonuses", "New sector opened: {0}");

            // Подтверждение перехода в следующую шахту (ТЗ 11.2). title: {0} — номер следующей шахты;
            // body: {0} — осталось камней, {1} — сундуков.
            d.Add("controller.next_mine.title", "Go to mine {0}?");
            d.Add("controller.next_mine.title_last", "Finish the campaign?");
            d.Add("controller.next_mine.body",
                "Rocks left: {0}, chests: {1}.\n" +
                "You will lose: gold, cats, building upgrades, uncollected payouts, unopened crates with cats or gold, " +
                "the free cat container.\n" +
                "You will keep: cards, elixir, gems, dynamite, the queue of unopened chests; elixir and dynamite from unopened crates will be credited.");
            d.Add("controller.next_mine.stay", "Stay");
            d.Add("controller.next_mine.go", "Go");
            d.Add("controller.next_mine.go_last", "Finish campaign");

            // Финальный экран кампании (ТЗ 11.4). body: {0} — пройдено шахт, {1} — общее время (уже отформатировано),
            // {2} — карточек собрано, {3} — карточек всего.
            d.Add("controller.campaign_end.title", "Campaign complete!");
            d.Add("controller.campaign_end.body",
                "Mines cleared: {0}" +
                "\nTotal time: {1}" +
                "\nCards in collection: {2} of {3}" +
                "\n\nThanks for digging with the cats!");
        }
    }
}
