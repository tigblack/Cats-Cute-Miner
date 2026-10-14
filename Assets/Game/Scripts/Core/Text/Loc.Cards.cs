using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Cards: коллекция карточек режима CatOutpostV2 (Runtime/Outpost/UI/OutpostCardText.cs, окно «Карты»). Имена — по ID
        /// карточки источника, отдельно от технических данных; шаблоны эффектов — по CardEffectKind (CARD_EFFECT_MAPPING.md).
        /// {0}/{1} в шаблонах — уже отформатированные проценты, числа и название ресурса (cards.res.*, с заглавной буквы).
        /// </summary>
        static partial void RegisterCards(Dictionary<string, string> d)
        {
            // Имена 61 карточки. Знаток (Appraiser) — цена порций месторождения, мастер (Master) — урон по нему.
            d.Add("cards.name.ca001", "Depot Master");
            d.Add("cards.name.ca002", "Amethyst Appraiser");
            d.Add("cards.name.ca003", "Citrine Appraiser");
            d.Add("cards.name.ca004", "Agate Appraiser");
            d.Add("cards.name.ca005", "Topaz Appraiser");
            d.Add("cards.name.ca006", "Opal Appraiser");
            d.Add("cards.name.ca007", "Jade Appraiser");
            d.Add("cards.name.ca008", "Onyx Appraiser");
            d.Add("cards.name.ca009", "Sapphire Appraiser");
            d.Add("cards.name.ca010", "Tourmaline Appraiser");
            d.Add("cards.name.ca011", "Aquamarine Appraiser");
            d.Add("cards.name.ca012", "Emerald Appraiser");
            d.Add("cards.name.ca013", "Diamond Appraiser");
            d.Add("cards.name.ca039", "Garnet Appraiser");
            d.Add("cards.name.ca041", "Heliodor Appraiser");
            d.Add("cards.name.ca044", "Ruby Appraiser");
            d.Add("cards.name.ca046", "Alexandrite Appraiser");
            d.Add("cards.name.ca048", "Celestine Appraiser");
            d.Add("cards.name.ca056", "Amber Appraiser");
            d.Add("cards.name.ca058", "Moonstone Appraiser");
            d.Add("cards.name.ca060", "Sunstone Appraiser");
            d.Add("cards.name.ca014", "Amethyst Master");
            d.Add("cards.name.ca015", "Citrine Master");
            d.Add("cards.name.ca016", "Agate Master");
            d.Add("cards.name.ca017", "Topaz Master");
            d.Add("cards.name.ca018", "Opal Master");
            d.Add("cards.name.ca019", "Jade Master");
            d.Add("cards.name.ca020", "Onyx Master");
            d.Add("cards.name.ca021", "Sapphire Master");
            d.Add("cards.name.ca022", "Tourmaline Master");
            d.Add("cards.name.ca023", "Aquamarine Master");
            d.Add("cards.name.ca024", "Emerald Master");
            d.Add("cards.name.ca025", "Diamond Master");
            d.Add("cards.name.ca040", "Garnet Master");
            d.Add("cards.name.ca042", "Heliodor Master");
            d.Add("cards.name.ca045", "Ruby Master");
            d.Add("cards.name.ca047", "Alexandrite Master");
            d.Add("cards.name.ca049", "Celestine Master");
            d.Add("cards.name.ca057", "Amber Master");
            d.Add("cards.name.ca059", "Moonstone Master");
            d.Add("cards.name.ca026", "Master's Yarn");
            d.Add("cards.name.ca027", "Courier Cat");
            d.Add("cards.name.ca028", "Fish Ration");
            d.Add("cards.name.ca029", "Pocket Watch");
            d.Add("cards.name.ca030", "Milk Bowl");
            d.Add("cards.name.ca031", "Lucky Paw");
            d.Add("cards.name.ca032", "Gate Bell");
            d.Add("cards.name.ca033", "Catnip");
            d.Add("cards.name.ca043", "Toy Mouse");
            d.Add("cards.name.ca050", "Bell Collar");
            d.Add("cards.name.ca051", "Cat Bed");
            d.Add("cards.name.ca052", "Scratching Post");
            d.Add("cards.name.ca053", "Miner's Lantern");
            d.Add("cards.name.ca054", "Rubble Blaster");
            d.Add("cards.name.ca055", "Lucky Clover");
            d.Add("cards.name.ca034", "Golden Cart");
            d.Add("cards.name.ca035", "Sledgehammer");
            d.Add("cards.name.ca036", "Hiring Poster");
            d.Add("cards.name.ca037", "Gold Vein");
            d.Add("cards.name.ca038", "Golden Helmet");
            d.Add("cards.name.ca062", "Treasure Map");
            // Ресурс месторождения (TargetIds специалистов): подставляется в шаблоны эффектов.
            d.Add("cards.res.spawningcart", "Base depot");
            d.Add("cards.res.amethyst", "Amethyst");
            d.Add("cards.res.citrine", "Citrine");
            d.Add("cards.res.agate", "Agate");
            d.Add("cards.res.topaz", "Topaz");
            d.Add("cards.res.opal", "Opal");
            d.Add("cards.res.jade", "Jade");
            d.Add("cards.res.onyx", "Onyx");
            d.Add("cards.res.sapphire", "Sapphire");
            d.Add("cards.res.tourmaline", "Tourmaline");
            d.Add("cards.res.aquamarine", "Aquamarine");
            d.Add("cards.res.emerald", "Emerald");
            d.Add("cards.res.diamond", "Diamond");
            d.Add("cards.res.garnet", "Garnet");
            d.Add("cards.res.heliodor", "Heliodor");
            d.Add("cards.res.ruby", "Ruby");
            d.Add("cards.res.alexandrite", "Alexandrite");
            d.Add("cards.res.celestine", "Celestine");
            d.Add("cards.res.amber", "Amber");
            d.Add("cards.res.moonstone", "Moonstone");
            d.Add("cards.res.sunstone", "Sunstone");
            // Эффекты по виду (CardEffectKind; KindValue/KindDamage — с видом цели).
            d.Add("cards.effect.TargetValue", "Coin value from {0} deposits: +{1}");
            d.Add("cards.effect.TargetDamage", "Damage to {0} deposits: +{1}");
            d.Add("cards.effect.UnloadRate", "Depot unloading speed: +{0}");
            d.Add("cards.effect.GlobalValue", "Value of all new coin piles: +{0}");
            d.Add("cards.effect.HireDiscount", "Hiring cats costs {0} less");
            d.Add("cards.effect.TeamCap", "Team cat limit: +{0}");
            d.Add("cards.effect.StartWorkers", "Cats at mine start: +{0} (count toward the Team limit)");
            d.Add("cards.effect.CritChance", "Critical hit chance: {0}");
            d.Add("cards.effect.CritPower", "Critical hit: damage ×{0}");
            d.Add("cards.effect.GateDamage", "Damage to gates: +{0}");
            d.Add("cards.effect.KindValue.r", "Coin value from plain rocks: +{0}");
            d.Add("cards.effect.KindValue.x", "Coin value from rockfalls: +{0}");
            d.Add("cards.effect.KindValue.s", "Coin value from deposits: +{0}");
            d.Add("cards.effect.MoveSpeed", "Cat speed: +{0}");
            d.Add("cards.effect.KindDamage.r", "Damage to plain rocks: +{0}");
            d.Add("cards.effect.KindDamage.x", "Damage to rockfalls: +{0}");
            d.Add("cards.effect.KindDamage.s", "Damage to deposits: +{0}");
            d.Add("cards.effect.ChestLuck", "Rare card chance in chests: +{0}");
            d.Add("cards.effect.Capacity", "Bag slots: +{0}");
            d.Add("cards.effect.PickupSpeed", "Coin pickup speed: +{0}");
            d.Add("cards.effect.GlobalDamage", "Cat damage: +{0}");
            d.Add("cards.effect.BranchDiscount", "Base upgrades cost {0} less");
            d.Add("cards.effect.OfflineHours", "Offline progress lasts {0} h longer");
            d.Add("cards.effect.TapDamage", "Your pickaxe hits {0} harder");
            d.Add("cards.effect.ChestElixir", "Elixir from chests: +{0}");
            d.Add("cards.effect.ChestCards", "Random cards in each chest: +{0}");
            d.Add("cards.effect.CompletionParts", "Parts for clearing a mine: +{0}");
            // Короткая строка эффекта под именем в сетке коллекции (CardView).
            d.Add("cards.short.TargetValue", "{0} value +{1}");
            d.Add("cards.short.TargetDamage", "{0} mining +{1}");
            d.Add("cards.short.UnloadRate", "Unloading +{0}");
            d.Add("cards.short.GlobalValue", "Coin value +{0}");
            d.Add("cards.short.HireDiscount", "Hire cost −{0}");
            d.Add("cards.short.TeamCap", "Cat limit +{0}");
            d.Add("cards.short.StartWorkers", "Starting cats +{0}");
            d.Add("cards.short.CritChance", "Crit chance {0}");
            d.Add("cards.short.CritPower", "Crit damage ×{0}");
            d.Add("cards.short.GateDamage", "Gate damage +{0}");
            d.Add("cards.short.KindValue.r", "Rock value +{0}");
            d.Add("cards.short.KindValue.x", "Rockfall value +{0}");
            d.Add("cards.short.KindValue.s", "Deposit value +{0}");
            d.Add("cards.short.MoveSpeed", "Cat speed +{0}");
            d.Add("cards.short.KindDamage.r", "Rock damage +{0}");
            d.Add("cards.short.KindDamage.x", "Rockfall damage +{0}");
            d.Add("cards.short.KindDamage.s", "Deposit damage +{0}");
            d.Add("cards.short.ChestLuck", "Chest luck +{0}");
            d.Add("cards.short.Capacity", "Bag slots +{0}");
            d.Add("cards.short.PickupSpeed", "Coin pickup +{0}");
            d.Add("cards.short.GlobalDamage", "Cat damage +{0}");
            d.Add("cards.short.BranchDiscount", "Upgrade cost −{0}");
            d.Add("cards.short.OfflineHours", "Offline +{0} h");
            d.Add("cards.short.TapDamage", "Your hit +{0}");
            d.Add("cards.short.ChestElixir", "Chest elixir +{0}");
            d.Add("cards.short.ChestCards", "Cards per chest +{0}");
            d.Add("cards.short.CompletionParts", "Parts per mine +{0}");
            // Величина бонуса в часах для сравнения уровней (OutpostCardText.EffectValue): «+3 h».
            d.Add("cards.value.hours", "+{0} h");
            // Окно «Карты», состояния, подсказки и сообщения.
            d.Add("cards.bottom", "Cards");
            d.Add("cards.title", "Cards");
            d.Add("cards.subtitle", "Permanent bonuses — kept between mines");
            d.Add("cards.found", "Collected {0} of {1}");
            d.Add("cards.elixir", "Elixir");
            d.Add("cards.sort.default", "Default order");
            d.Add("cards.sort.upgradable", "Upgradable first");
            d.Add("cards.state.locked", "Unlocks at mine {0}");
            d.Add("cards.state.available", "Available in chests");
            d.Add("cards.state.collecting", "Collected {0}/{1}");
            d.Add("cards.state.upgradable", "Can upgrade");
            d.Add("cards.state.max", "Max level");
            d.Add("cards.level", "Lv. {0}");
            d.Add("cards.copies", "{0}/{1}");
            d.Add("cards.detail.rarity", "{0} · {1}");
            d.Add("cards.detail.unlock", "Unlocks at mine {0}");
            d.Add("cards.detail.progress", "You have reached mine {0}");
            d.Add("cards.detail.not_owned", "Not obtained yet — look for it in chests");
            d.Add("cards.detail.locked_hint", "Becomes available in chests once you reach mine {0}");
            d.Add("cards.detail.copies", "Copies to upgrade: {0}/{1}");
            d.Add("cards.detail.now", "Now: {0}");
            d.Add("cards.detail.next", "After upgrade: {0}");
            d.Add("cards.detail.at_level1", "At level 1: {0}");
            d.Add("cards.detail.upgrade", "Upgrade");
            d.Add("cards.detail.max", "Max level");
            d.Add("cards.detail.target", "Works only for {0}");
            d.Add("cards.detail.first_copy", "The first copy from a chest activates the card");
            d.Add("cards.rule.LegacyConfirmed", "Effect from the original game, adapted to auto-mining");
            d.Add("cards.rule.LegacyAssumed", "Assumed effect from the original game, adapted");
            d.Add("cards.rule.CatMiners", "New Cat Miners rule");
            d.Add("cards.reason.copies", "Copies needed: {0}");
            d.Add("cards.reason.elixir", "Elixir needed: {0}");
            d.Add("cards.reason.not-owned", "Card not obtained yet");
            d.Add("cards.reason.max", "Max level reached");
            d.Add("cards.toast.chest", "Chest: cards +{0}, elixir +{1}");
            d.Add("cards.toast.chest_cards", "Chest: cards +{0}");
            d.Add("cards.toast.new", "New card: {0}");
            d.Add("cards.toast.elixir", "Elixir +{0}");
            d.Add("cards.toast.tutorial", "First card: {0}! Take a look at Cards");
            d.Add("cards.toast.upgraded", "{0}: level {1}");
            d.Add("cards.offline", "Cards +{0}, elixir +{1}");
            d.Add("cards.results", "Mine reward: cards +{0}, elixir +{1}");
            d.Add("cards.rarity.1", "Common");
            d.Add("cards.rarity.2", "Uncommon");
            d.Add("cards.rarity.3", "Rare");
            d.Add("cards.rarity.4", "Epic");
            d.Add("cards.rarity.5", "Legendary");
            d.Add("cards.rarity.6", "Majestic");
        }
    }
}
