using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Hud: HUD, панели, модальные окна и тосты интерфейса (Runtime/UI).
        /// Ключи начинаются с «hud.»; строки добавляются через d.Add("hud.группа.имя", "Текст").
        /// Числа в шаблонах — уже отформатированные строки (NumberFormat.Format/FormatDuration) или целые.
        /// </summary>
        static partial void RegisterHud(Dictionary<string, string> d)
        {
            // Верхняя полоса: табличка «Шахта N» и сектор (вторая строка), доход в секунду, котики/лимит рядом с иконкой котика.
            d.Add("hud.top.zone", "Mine {0}");
            d.Add("hud.top.sector", "sector {0}/{1}");
            d.Add("hud.top.income", "+{0}/s");
            d.Add("hud.top.cats_count", "{0}/{1}");

            // Окно настроек (шестерёнка в верхней полосе): звук и вибрация.
            d.Add("hud.settings.title", "Settings");
            d.Add("hud.settings.sound", "Sound");
            d.Add("hud.settings.vibration", "Vibration");
            d.Add("hud.settings.master", "Volume");
            d.Add("hud.settings.music", "Music");
            d.Add("hud.settings.sfx", "Sounds");
            d.Add("hud.settings.ambience", "Ambience");
            d.Add("hud.settings.reduce_effects", "Fewer effects");

            // Кнопка перехода в следующую шахту (и кнопка выхода в контекстной панели).
            d.Add("hud.next_mine.next", "Next mine");
            d.Add("hud.next_mine.finish", "Finish campaign");

            // Кнопка найма: заголовок (при лимите или без места — причина вместо него), уровень нанимаемого котика у шкалы.
            d.Add("hud.hire.title", "Hire");
            d.Add("hud.hire.level", "Lv. {0}");
            d.Add("hud.hire.cap", "Cat limit {0}/{1}");
            d.Add("hud.hire.no_space", "No space");

            // Плашка над нижней панелью: бесплатный котик. Очередь сундуков — бейдж на кнопке сундуков; на экране конца кампании — кнопка.
            d.Add("hud.free_cat.on_field", "A free cat is waiting on the field!");
            d.Add("hud.free_cat.waiting_space", "Free cat: waiting for space");
            d.Add("hud.free_cat.timer", "Free cat in {0}");
            d.Add("hud.chests.open_all", "Open chests: {0}");

            // Ячейка динамита: число зарядов.
            d.Add("hud.dynamite.count", "×{0}");

            // Экран «Карты» (кнопка справа от «Нанять»): заголовок, подзаголовок, счётчик коллекции {0}/{1}, пояснение кнопки «i».
            d.Add("hud.blueprints.title", "Cards");
            d.Add("hud.cards.subtitle", "All cards give permanent bonuses!");
            d.Add("hud.cards.found", "Found {0}/{1}");
            d.Add("hud.blueprints.hint", "Cards come from chests: rewards for exits, shafts, gates and chest rocks. A repeated card is a copy: copies and elixir upgrade the card.");
            d.Add("hud.blueprints.empty", "There are no collection cards in the data.");
            // Сортировка (кнопка слева в шапке): тост после смены.
            d.Add("hud.cards.sort_ready", "Ready to upgrade first");
            d.Add("hud.cards.sort_collection", "Collection order");
            // Ячейка карты: уровень, максимум, рубашка ещё не найденной — «?» и шахта, с которой она выпадает.
            d.Add("hud.cards.level", "Level {0}");
            d.Add("hud.cards.max", "Max");
            d.Add("hud.cards.unknown", "?");
            d.Add("hud.cards.unlock", "Mine {0}");
            // Окно карты: {0} — уровень, {1} — максимум; эффект сейчас и на следующем уровне; копии; где выпадает.
            d.Add("hud.card.detail.level", "{0} · level {1} of {2}");
            d.Add("hud.card.detail.now", "Now: {0}");
            d.Add("hud.card.detail.next", "At level {0}: {1}");
            d.Add("hud.card.detail.copies", "Copies: {0}/{1}, elixir: {2}");
            d.Add("hud.card.detail.copies_max", "Copies: {0} — max level");
            d.Add("hud.card.detail.not_owned", "Not found yet. Drops from chests starting at mine {0}.");

            // Бесплатный сундук: {0}/{1} — запас/максимум; «закрыт» — {0} шахта старта, {1} — число шахт с формой слова.
            d.Add("hud.free_chest.locked", "The free chest unlocks at mine {0} after {1}");
            d.Add("hud.free_chest.activated_shafts.one", "{0} activated shaft");
            d.Add("hud.free_chest.activated_shafts.many", "{0} activated shafts");
            d.Add("hud.free_chest.open", "Open free chest ({0}/{1})");
            d.Add("hud.free_chest.stock", "Free chest ({0}/{1})");
            d.Add("hud.free_chest.next", "next in {0}");

            // Карта в окне карты и подписи кнопки улучшения.
            d.Add("hud.card.required", "Needs Lv. {0} to automate in this mine");
            d.Add("hud.card.upgrade", "Upgrade");
            d.Add("hud.card.max", "Max");
            // Короткие причины на кнопке улучшения (в строку над ценой); тосты отказа берут core.reject.*.
            d.Add("hud.card.need_copies", "Need copies");
            d.Add("hud.card.need_elixir", "Need elixir");

            // Контекстная панель котика.
            d.Add("hud.cat.title", "{0} · Lv. {1}");
            d.Add("hud.cat.damage", "Damage: {0} per hit, {1}/s");
            d.Add("hud.cat.working", "Working: {0}");
            d.Add("hud.cat.working_eta", "Working: {0} · {1} left");
            d.Add("hud.cat.idle", "Resting. Drag it onto a rock to dig, or onto a cat of the same level to merge.");

            // Контекстная панель объекта: заголовок с уровнем, цель расчистки.
            d.Add("hud.object.title_level", "{0} · Lv. {1}");
            d.Add("hud.object.hp", "HP: {0} / {1}");
            d.Add("hud.object.no_workers", "No workers");
            d.Add("hud.object.workers_eta", "Cats: {0} · {1} left");
            d.Add("hud.object.sector_closed", "Sector is closed");
            d.Add("hud.object.shaft_after_clear", "After clearing: activation and income");

            // Шахта, готовая к активации.
            d.Add("hud.shaft.ready", "The shaft is cleared. Activate it to get a reward and start mining.");
            d.Add("hud.shaft.activate", "Activate");

            // Ворота.
            d.Add("hud.gate.broken", "The gate is broken. Open it:");
            d.Add("hud.gate.reward_chest", "Reward: chest");
            d.Add("hud.gate.open", "Open");
            d.Add("hud.gate.opened", "The gate is open.");

            // Бонусы ворот (ТЗ 11.1): {0} — значение из данных CheckPoint.
            d.Add("hud.gate_bonus.income", "Output of all zone generators ×{0}");
            d.Add("hud.gate_bonus.hire_divisor", "Hire price ÷{0}");
            d.Add("hud.gate_bonus.free_cat_interval", "Free cat interval −{0}s");
            d.Add("hud.gate_bonus.cat_cap", "Cat limit +{0}");

            // Выход.
            d.Add("hud.exit.open_last", "The exit is open! You can finish the campaign.");
            d.Add("hud.exit.open_next", "The exit is open! You can move on to the next mine.");
            d.Add("hud.exit.go", "Go");
            d.Add("hud.exit.to_shaft", "To shaft");

            // Генератор: {0} — уровень, {1} — выход, {2} — цикл, {3} — выход в секунду (кузница: в её заголовке уровня нет).
            d.Add("hud.generator.output", "Level {0} · {1} per {2} ({3}/s)");
            // Шахта: уровень уже в заголовке «Шахта … · ур. N». {0} — выход, {1} — цикл, {2} — выход в секунду.
            d.Add("hud.generator.rate", "Output: {0} per {1} ({2}/s)");
            d.Add("hud.generator.max_level", "Max level");
            d.Add("hud.generator.threshold", "Milestone ×{0} at Lv. {1}: {2}/{3}");
            d.Add("hud.generator.automated", "Automated");
            // Ручная шахта (ТЗ 9.2): {0} — имя карты из каталога (не ID), {1} — нужный уровень,
            // {2} — что есть (core.automation.have_level / core.automation.have_none).
            d.Add("hud.generator.manual_need", "Manual collection. Needs the {0} card Lv. {1} (have: {2})");
            d.Add("hud.generator.payout_ready", "Payout ready: {0}");
            d.Add("hud.generator.upgrade_one", "+1 · {0}");
            d.Add("hud.generator.to_threshold", "To milestone");
            d.Add("hud.generator.to_threshold_cost", "To milestone: +{0} · {1}");
            d.Add("hud.generator.collect", "Collect {0}");

            // Окно сундука.
            d.Add("hud.chest.card", "{0}  ×{1}");
            d.Add("hud.chest.elixir", "Elixir +{0}");
            d.Add("hud.chest.empty", "Empty");
            d.Add("hud.chest.claim", "Claim");

            // Окно «Пока вас не было» (ТЗ 12.3).
            d.Add("hud.offline.title", "While you were away");
            d.Add("hud.offline.ok", "Great");
            d.Add("hud.offline.elapsed", "Time passed: {0}");
            d.Add("hud.offline.capped", "(up to 8 h is counted)");
            d.Add("hud.offline.gold", "Gold +{0}");
            d.Add("hud.offline.elixir", "Elixir +{0}");
            d.Add("hud.offline.rocks", "Rocks destroyed: {0}");
            d.Add("hud.offline.shafts_cleared", "Shafts cleared: {0} — activate them");
            d.Add("hud.offline.gates_broken_many", "Gates broken ({0}) — open them");
            d.Add("hud.offline.gates_broken_one", "Gate broken — open it");
            d.Add("hud.offline.exit_open", "Exit open");
            d.Add("hud.offline.chests", "Chests: {0}");
            d.Add("hud.offline.payouts", "Payouts ready: {0}");
            d.Add("hud.offline.free_cat", "A free cat is waiting on the field");

            // Подписи стрелок у краёв экрана (EdgeIndicators, ТЗ 13.1).
            d.Add("hud.edge.shaft", "Shaft");
            d.Add("hud.edge.gate", "Gate");
            d.Add("hud.edge.payout", "Payout");
            d.Add("hud.edge.exit", "Exit");
            d.Add("hud.edge.free_cat", "Cat");
            d.Add("hud.edge.delivery", "Crate");
        }
    }
}
