using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Outpost: HUD, окна и обучение режима CatOutpostV2 (Runtime/Outpost: OutpostHud, OutpostTutorial, OutpostUiText).
        /// Ключи начинаются с «outpost.»; строки добавляются через d.Add("outpost.группа.имя", "Текст").
        /// Числа в шаблонах — уже отформатированные строки (MoneyFormat/NumberFormat) или целые. Формы слова — ключи
        /// «.one/.many» для Loc.Plural (английское правило). Глифы «→», «×», «·» и «—» есть в Pixeloid Sans.
        /// Названия разделов игры (Base, Equipment, Pickaxes…) пишутся с заглавной буквы и без кавычек.
        /// </summary>
        static partial void RegisterOutpost(Dictionary<string, string> d)
        {
            d.Add("outpost.dev.reset", "Reset progress");
            d.Add("outpost.dev.reset_title", "Start over?");
            d.Add("outpost.dev.reset_body", "All progress of profile {0} will be reset: mines, gold, cards, elixir, equipment and parts. The game will start from mine 1 and the tutorial will run again.");
            d.Add("outpost.dev.reset_cancel", "Cancel");
            d.Add("outpost.dev.reset_confirm", "Start over");
            // Верхняя полоса: табличка «Шахта N» и сектор; подписи кошелька и несданного груза (разные по смыслу, ТЗ 14).
            d.Add("outpost.top.zone", "Mine {0}");
            d.Add("outpost.top.sector", "sector {0}/{1}");
            d.Add("outpost.top.gold_label", "gold");
            d.Add("outpost.top.cargo_label", "en route");   // ячейка подписи: ≈91 ед. при 9:16 и ≈67 ед. при 9:20 — «in transit» не помещается
            d.Add("outpost.top.progress", "{0}/{1}");

            // Нижняя панель.
            d.Add("outpost.bottom.base", "Base");
            d.Add("outpost.bottom.equipment", "Equipment");
            d.Add("outpost.bottom.to_base", "To base");
            d.Add("outpost.bottom.store", "Store");

            // Полоса над нижней панелью: фаза шахты, переход, повтор записи, расчёт офлайна ({0} — проценты).
            d.Add("outpost.status.draining", "Delivering the remaining coins");
            d.Add("outpost.status.next_mine", "Go to the next mine");
            d.Add("outpost.status.campaign", "Campaign complete");
            d.Add("outpost.status.retry_save", "Not saved — retry");
            d.Add("outpost.status.offline", "Counting time away: {0}%");

            // Тосты.
            d.Add("outpost.toast.sector_opened", "Sector {0}/{1} opened");
            d.Add("outpost.toast.equipped", "Equipped: {0}");
            d.Add("outpost.toast.upgraded", "{0}: level {1}");
            d.Add("outpost.toast.best_done", "Best gear equipped");
            d.Add("outpost.toast.best_equipped", "Best gear is already equipped");
            d.Add("outpost.toast.map_updated", "The map of mine {0} was updated — the mine has restarted. Items, parts and rewards are kept");

            // Причины недоступности (ТЗ 14): короткие, по коду отказа ядра или контроллера.
            d.Add("outpost.reason.gold", "Not enough delivered gold");
            d.Add("outpost.reason.cap", "Max level");
            d.Add("outpost.reason.phase", "Mine complete");
            d.Add("outpost.reason.mode", "Cats are hired one at a time");
            d.Add("outpost.reason.item_max", "Item is at max level");
            d.Add("outpost.reason.parts", "Not enough parts");
            d.Add("outpost.reason.not_owned", "Item not obtained yet");
            d.Add("outpost.reason.already", "Already equipped");
            d.Add("outpost.reason.empty", "Slot is empty");
            d.Add("outpost.reason.offline", "Counting offline progress");
            d.Add("outpost.reason.transition", "Moving to the next mine");
            d.Add("outpost.reason.loading", "The game is loading");
            d.Add("outpost.reason.closed_sector", "Closed sector");
            d.Add("outpost.reason.no_path", "No path");
            d.Add("outpost.reason.wait_sector", "Waits for the sector to be cleared");
            d.Add("outpost.reason.cleared", "Cleared");
            d.Add("outpost.reason.unknown", "Not available right now");
            d.Add("outpost.reason.gems", "Not enough gems");
            d.Add("outpost.reason.cooldown", "Not ready yet");
            d.Add("outpost.reason.unavailable", "Coming soon");

            // Панель «База»: «На эту шахту», режим покупки, уровень, пакет, порог ({0} — уровень порога, {1} — множитель).
            d.Add("outpost.base.title", "Base");
            d.Add("outpost.base.scope", "For this mine");
            d.Add("outpost.base.mode.one", "×1");
            d.Add("outpost.base.mode.ten", "×10");
            d.Add("outpost.base.mode.max", "Max");
            d.Add("outpost.base.level", "Lv. {0}/{1}");
            d.Add("outpost.base.levels_gain", "+{0} lv.");
            d.Add("outpost.base.max", "Max");
            d.Add("outpost.base.done", "Mine complete — local upgrades are no longer needed");

            // Ветки локальных улучшений (ТЗ 9).
            d.Add("outpost.branch.team", "Team");
            d.Add("outpost.branch.pickaxes", "Pickaxes");
            d.Add("outpost.branch.boots", "Boots");
            d.Add("outpost.branch.bags", "Bags");
            d.Add("outpost.branch.unload", "Unloading");
            d.Add("outpost.branch.value", "Loot Value");

            // Эффект ветки: на пределе и «сейчас → после» ({0} — сейчас, {1} — после; значения из характеристик ядра).
            d.Add("outpost.effect.team.now", "Cats: {0}");
            d.Add("outpost.effect.team.change", "Cats: {0} → {1}");
            d.Add("outpost.effect.pickaxes.now", "Damage: {0}");
            d.Add("outpost.effect.pickaxes.change", "Damage: {0} → {1}");
            d.Add("outpost.effect.boots.now", "Speed: {0} tiles/s");
            d.Add("outpost.effect.boots.change", "Speed: {0} → {1} tiles/s");
            d.Add("outpost.effect.bags.now", "Bag: {0}");
            d.Add("outpost.effect.bags.change", "Bag: {0} → {1}");
            d.Add("outpost.effect.unload.now", "Unloading: {0}/s");
            d.Add("outpost.effect.unload.change", "Unloading: {0} → {1}/s");
            d.Add("outpost.effect.value.now", "Value: ×{0}");
            d.Add("outpost.effect.value.change", "Value: ×{0} → ×{1}");

            // Подсказка ограничения потока по наблюдаемым состояниям за ≈20 с ({0} — проценты).
            d.Add("outpost.flow.collecting", "Watching the team at work");
            d.Add("outpost.flow.balanced", "The team works smoothly — no clear bottleneck");
            d.Add("outpost.flow.mining", "Cats spend {0}% of the time mining — upgrade Pickaxes");
            d.Add("outpost.flow.walking", "Cats spend {0}% of the time walking — upgrade Boots");
            d.Add("outpost.flow.queue", "Cats spend {0}% of the time waiting at the depot — upgrade Unloading");
            d.Add("outpost.flow.full_bags", "{0}% of trips end with a full bag — upgrade Bags");

            // «Экипировка» (ТЗ 10): «Сохраняется между шахтами», детали, фильтр, слоты.
            d.Add("outpost.equip.title", "Equipment");
            d.Add("outpost.equip.scope", "Kept between mines");
            d.Add("outpost.equip.best", "Equip best");
            d.Add("outpost.equip.filter.all", "All");
            d.Add("outpost.equip.filter.pickaxe", "Pickaxes");
            d.Add("outpost.equip.filter.hat", "Hats");
            d.Add("outpost.equip.filter.backpack", "Backpacks");
            d.Add("outpost.equip.filter.boots", "Boots");
            d.Add("outpost.equip.slot_empty", "Empty");
            d.Add("outpost.equip.level", "Lv. {0}");

            // Слоты и редкости (порядок ядра: кирка, шляпа, рюкзак, обувь; обычная, редкая, эпическая, легендарная).
            d.Add("outpost.slot.pickaxe", "Pickaxe");
            d.Add("outpost.slot.hat", "Hat");
            d.Add("outpost.slot.backpack", "Backpack");
            d.Add("outpost.slot.boots", "Boots");
            d.Add("outpost.rarity.common", "Common");
            d.Add("outpost.rarity.rare", "Rare");
            d.Add("outpost.rarity.epic", "Epic");
            d.Add("outpost.rarity.legendary", "Legendary");

            // 16 предметов каталога (ID «слот.редкость» — только ключ, игроку не показывается).
            d.Add("outpost.item.pickaxe.common", "Common Pickaxe");
            d.Add("outpost.item.pickaxe.rare", "Rare Pickaxe");
            d.Add("outpost.item.pickaxe.epic", "Epic Pickaxe");
            d.Add("outpost.item.pickaxe.legendary", "Legendary Pickaxe");
            d.Add("outpost.item.hat.common", "Common Hat");
            d.Add("outpost.item.hat.rare", "Rare Hat");
            d.Add("outpost.item.hat.epic", "Epic Hat");
            d.Add("outpost.item.hat.legendary", "Legendary Hat");
            d.Add("outpost.item.backpack.common", "Common Backpack");
            d.Add("outpost.item.backpack.rare", "Rare Backpack");
            d.Add("outpost.item.backpack.epic", "Epic Backpack");
            d.Add("outpost.item.backpack.legendary", "Legendary Backpack");
            d.Add("outpost.item.boots.common", "Common Boots");
            d.Add("outpost.item.boots.rare", "Rare Boots");
            d.Add("outpost.item.boots.epic", "Epic Boots");
            d.Add("outpost.item.boots.legendary", "Legendary Boots");

            // Основной бонус предмета ({0} — проценты или места в сумке из каталога ядра).
            d.Add("outpost.bonus.pickaxe", "Damage +{0}%");
            d.Add("outpost.bonus.hat", "Value +{0}%");
            d.Add("outpost.bonus.boots", "Speed +{0}%");
            d.Add("outpost.bonus.backpack.one", "Bag +{0} slot");
            d.Add("outpost.bonus.backpack.many", "Bag +{0} slots");
            d.Add("outpost.bonus.none", "no bonus");
            // Только величина бонуса — справа от зелёной стрелки в окне предмета («Damage +8.4% ➜ +8.8%»).
            d.Add("outpost.bonus.value.percent", "+{0}%");
            d.Add("outpost.bonus.value.backpack.one", "+{0} slot");
            d.Add("outpost.bonus.value.backpack.many", "+{0} slots");

            // Детали (постоянная валюта улучшения предметов).
            d.Add("outpost.parts.count.one", "{0} part");
            d.Add("outpost.parts.count.many", "{0} parts");
            d.Add("outpost.parts.gain.one", "+{0} part");
            d.Add("outpost.parts.gain.many", "+{0} parts");

            // Окно предмета: {0} — редкость, {1}/{2} — уровень и максимум. Сравнение с надетым в слоте: {0} — бонус надетого
            // с названием («Damage +8.4%»), {1} — только величина этого предмета («+60%»). «with this» поясняет, что справа —
            // этот предмет: одна стрелка без слов читалась как сложение.
            d.Add("outpost.item.rarity_level", "{0} · Lv. {1}/{2}");
            d.Add("outpost.item.equipped", "Equipped");
            d.Add("outpost.item.compare", "Now: {0} → with this: {1}");
            d.Add("outpost.item.parts", "Parts: {0}/{1}");
            d.Add("outpost.item.max", "Max level");
            d.Add("outpost.item.not_owned", "Not obtained yet. Items are awarded for clearing a mine for the first time.");
            d.Add("outpost.item.equip", "Equip");
            d.Add("outpost.item.unequip", "Unequip");
            d.Add("outpost.item.upgrade", "Upgrade");
            d.Add("outpost.item.upgrade_max", "Max");

            // Сведения о котике и цели (ТЗ 14).
            d.Add("outpost.info.worker", "Cat {0}");
            d.Add("outpost.info.task", "Task: {0}");
            d.Add("outpost.info.task_target", "{0} — {1}");
            d.Add("outpost.info.bag", "Bag: {0}/{1}");
            d.Add("outpost.info.cargo_value", "Cargo value: {0}");
            d.Add("outpost.info.hp", "HP: {0}/{1}");
            d.Add("outpost.info.workers", "Cats on target: {0}");
            d.Add("outpost.info.loot.one", "{0} coin pile left");
            d.Add("outpost.info.loot.many", "{0} coin piles left");
            d.Add("outpost.info.strike", "Tap the target to hit it with your pickaxe");

            // Задача котика по режиму ядра.
            d.Add("outpost.task.choosing", "choosing a task");
            d.Add("outpost.task.moving_to_work", "heading to the target");
            d.Add("outpost.task.mining", "mining");
            d.Add("outpost.task.moving_to_cargo", "going for coins");
            d.Add("outpost.task.picking_up", "picking up coins");
            d.Add("outpost.task.returning", "carrying coins to the base");
            d.Add("outpost.task.waiting_depot", "waiting for room at the depot");
            d.Add("outpost.task.unloading", "unloading coins at the depot");
            d.Add("outpost.task.idle", "resting");
            d.Add("outpost.task.going_home", "returning to camp");

            // Имена целей по виду токена (не архивные ID): {0} — имя месторождения из каталога.
            d.Add("outpost.target.gate", "Sector Gate");
            d.Add("outpost.target.deposit", "{0} Deposit");
            d.Add("outpost.target.deposit_generic", "Rich Deposit");
            d.Add("outpost.target.wall", "Rockfall");
            d.Add("outpost.target.waterfall", "Waterfall");
            d.Add("outpost.target.unknown", "Obstacle");

            // «Итоги шахты» (ТЗ 11): {0} — номер шахты; детали — уже с формой слова.
            d.Add("outpost.results.title", "Mine {0} Results");
            d.Add("outpost.results.cleared", "Cleared: {0}/{1}");
            d.Add("outpost.results.gold", "Gold delivered: {0}");
            d.Add("outpost.results.item_new", "New item: {0}");
            d.Add("outpost.results.item_duplicate", "Duplicate: {0} → {1}");
            d.Add("outpost.results.completion_parts", "Mine reward: {0}");
            d.Add("outpost.results.no_reward", "The reward for this mine was already received");
            d.Add("outpost.results.campaign", "That was the last mine of the campaign!");
            d.Add("outpost.results.reset", "Local base upgrades and hired cats will reset. Equipment, items and parts are kept.");
            d.Add("outpost.results.equipment", "Equipment");
            d.Add("outpost.results.next", "Next mine");
            d.Add("outpost.results.retry", "Retry saving");
            d.Add("outpost.results.finish", "Continue");

            // Завершение кампании: {0} — число шахт.
            d.Add("outpost.campaign.title", "Campaign complete!");
            d.Add("outpost.campaign.body", "All {0} mines are cleared. Equipment and settings are still available.");
            d.Add("outpost.campaign.close", "Close");

            // «Пока вас не было» (ТЗ 13): результаты уже начислены; окно показывает только золото и рекламную заглушку.
            d.Add("outpost.offline.title", "While you were away");
            d.Add("outpost.offline.elapsed", "You were away for {0}");
            d.Add("outpost.offline.capped", "Counted {0} — the limit per absence is 3 hours");
            d.Add("outpost.offline.gold", "Gold earned\n{0}");
            d.Add("outpost.offline.cleared", "Objects cleared: {0}");
            d.Add("outpost.offline.sectors", "Sectors opened: {0}");
            d.Add("outpost.offline.completed", "Mine complete — check the results");
            d.Add("outpost.offline.nothing", "The cats were resting: nothing new");
            d.Add("outpost.offline.mine_state", "Mine {0}: cleared {1}/{2}");
            d.Add("outpost.offline.ok", "Great");
            d.Add("outpost.offline.double", "Double income");
            d.Add("outpost.offline.ad", "Watch ad");
            d.Add("outpost.offline.ad_soon", "Ads are coming soon");
            d.Add("outpost.offline.calc_title", "While you were away");
            d.Add("outpost.offline.calc_body", "The cats kept working. Counting what they got done.");
            d.Add("outpost.offline.calc_percent", "{0}%");

            // Магазин (ТЗ 10.5, store_catalog.json): разделы, плитки, действия, подтверждение, окна сундука, шансов и тайм-скипа.
            d.Add("outpost.store.title", "Store");
            d.Add("outpost.store.section.offers", "Special Offers");
            d.Add("outpost.store.section.timeskip", "Time Skip");
            d.Add("outpost.store.section.elixir", "Elixir");
            d.Add("outpost.store.section.gems", "Gems");
            d.Add("outpost.store.item.chest_wooden", "Wooden Chest");
            d.Add("outpost.store.item.chest_iron", "Iron Chest");
            d.Add("outpost.store.item.chest_mythical", "Mythical Chest");
            d.Add("outpost.store.item.skip_1h", "Short Time Skip");
            d.Add("outpost.store.item.skip_4h", "Medium Time Skip");
            d.Add("outpost.store.item.skip_12h", "Huge Time Skip");
            d.Add("outpost.store.item.elixir_bottle", "Bottle of Elixir");
            d.Add("outpost.store.item.elixir_box", "Box of Elixir");
            d.Add("outpost.store.item.elixir_wagon", "Wagon of Elixir");
            d.Add("outpost.store.item.gems_handful", "Handful of Gems");
            d.Add("outpost.store.item.gems_sack", "Sack of Gems");
            d.Add("outpost.store.item.gems_bag", "Bag of Gems");
            d.Add("outpost.store.item.gems_crate", "Crate of Gems");
            d.Add("outpost.store.item.gems_cart", "Cart of Gems");
            d.Add("outpost.store.item.gems_pile", "Pile of Gems");
            d.Add("outpost.store.minutes", "{0}m");
            d.Add("outpost.store.hours", "{0}h");
            d.Add("outpost.store.hours_minutes", "{0}h {1}m");
            d.Add("outpost.store.bonus", "+{0}%");
            d.Add("outpost.store.available", "Available: {0}");
            d.Add("outpost.store.use", "USE");
            d.Add("outpost.store.open", "OPEN!");
            d.Add("outpost.store.soon", "Soon");
            d.Add("outpost.store.price_usd", "${0}");
            d.Add("outpost.store.free_in", "Free again in {0}");
            d.Add("outpost.store.confirm_title", "Buy {0}?");
            d.Add("outpost.store.confirm_gems", "Spend {0} gems?");
            d.Add("outpost.store.confirm_real", "Pay ${0}?\nTest mode: the gems are added without a real payment.");
            d.Add("outpost.store.buy", "Buy");
            d.Add("outpost.store.cancel", "Cancel");
            d.Add("outpost.store.toast.gems", "+{0} gems");
            d.Add("outpost.store.toast.elixir", "+{0} elixir");
            d.Add("outpost.store.toast.double", "Income doubled: +{0} gold");
            d.Add("outpost.store.chest_total", "Cards: {0}");
            d.Add("outpost.store.chest_card", "{0} ×{1}");
            d.Add("outpost.store.chest_new", "NEW");
            d.Add("outpost.chest.title_many", "{0} Chests");
            d.Add("outpost.chest.copies", "×{0}");
            d.Add("outpost.store.chest_more.one", "+{0} more card");
            d.Add("outpost.store.chest_more.many", "+{0} more cards");
            d.Add("outpost.store.rates_title", "{0}: Drop Rates");
            d.Add("outpost.store.rates_body", "Cards: {0}\nElixir: {1}");
            d.Add("outpost.store.rates_row", "{0}: {1}");
            d.Add("outpost.store.skip_calc_title", "Time Skip");
            d.Add("outpost.store.skip_calc_body", "The cats work {0} in a flash…");
            d.Add("outpost.store.skip_title", "Time Skip: {0}");
            d.Add("outpost.store.skip_cleared", "Cleared: {0}");
            d.Add("outpost.store.skip_completed", "The mine was cleared during the skip!");

            // Ошибка загрузки или записи; технические подробности контроллера показываются под этой фразой.
            d.Add("outpost.error.title", "Error");
            d.Add("outpost.error.unknown", "Could not load the game.");
            d.Add("outpost.error.save", "Could not save the game.");
            d.Add("outpost.error.retry", "Retry");

            // Настройки (ТЗ 14).
            d.Add("outpost.settings.title", "Settings");
            d.Add("outpost.settings.master", "Volume");
            d.Add("outpost.settings.music", "Music");
            d.Add("outpost.settings.sfx", "Sounds");
            d.Add("outpost.settings.ambience", "Ambience");
            d.Add("outpost.settings.vibration", "Vibration");
            d.Add("outpost.settings.reduce_effects", "Fewer effects");
            d.Add("outpost.settings.tutorial", "Tutorial hints");
            d.Add("outpost.settings.play_time", "Play time");
            d.Add("outpost.settings.play_time_value", "{0}h {1}m");
            d.Add("outpost.settings.reset", "Reset Progress");
            d.Add("outpost.settings.reset_title", "Start over?");
            d.Add("outpost.settings.reset_body", "Start from mine 1? Your mines, cards, equipment and currencies will be reset. The tutorial will start again. Sound settings and play time will be kept.");

            // Обучение (ТЗ 15): реплики наставника, по одному действию. pickaxes_done: {0} → {1} — урон до и после;
            // kept_bonus: {0} — урон с киркой, {1} — без неё (местный уровень 0).
            d.Add("outpost.tutorial.skip", "Skip tutorial");
            d.Add("outpost.tutorial.skipped", "Tutorial skipped. Hints can be turned on in Settings");
            d.Add("outpost.tutorial.auto", "The cats already work on their own: they break rocks and collect coins");
            d.Add("outpost.tutorial.strike", "You can break rocks too! Tap a rock to hit it with your pickaxe");
            d.Add("outpost.tutorial.bag", "A cat fills its bag with coins and carries it to the base");
            d.Add("outpost.tutorial.gold", "Coins reached the depot — your gold went up! Only delivered gold can be spent");
            d.Add("outpost.tutorial.pickaxes_open", "You have enough gold for Pickaxes — open the Base");
            d.Add("outpost.tutorial.pickaxes_buy", "Upgrade Pickaxes: the whole team gets stronger");
            d.Add("outpost.tutorial.pickaxes_done", "Team damage went up: {0} → {1}");
            d.Add("outpost.tutorial.team_open", "You can afford a third cat — take a look at the Base");
            d.Add("outpost.tutorial.team_buy", "Team hires one more worker — it gets to work right away");
            d.Add("outpost.tutorial.auto_targets", "Cats choose their next target on their own — no need to control them");
            d.Add("outpost.tutorial.reward", "Mine cleared! You got a pickaxe — equip it in Equipment");
            d.Add("outpost.tutorial.reward_closed", "Mine cleared! Your reward is in the mine results");
            d.Add("outpost.tutorial.reset", "Local base upgrades will reset, but the item and parts stay with you");
            d.Add("outpost.tutorial.kept_bonus", "Base upgrades are back to zero, but the pickaxe already works: damage {0} instead of {1}");
            d.Add("outpost.tutorial.kept_bonus_equip", "Equip the pickaxe in Equipment — its bonus works from the very first hit");
        }
    }
}
