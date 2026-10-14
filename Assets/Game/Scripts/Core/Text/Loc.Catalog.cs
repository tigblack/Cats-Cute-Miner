using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Catalog: имена объектов, шахт, сундуков и карточек (PresentationCatalog).
        /// Ключи начинаются с «catalog.»; строки добавляются через d.Add("catalog.группа.имя", "Текст").
        /// Имена шахт и сундуков — по ID из данных: «catalog.shaft.&lt;ID шахты&gt;», «catalog.gacha.&lt;ID сундука&gt;».
        /// </summary>
        static partial void RegisterCatalog(Dictionary<string, string> d)
        {
            // Котик.
            d.Add("catalog.cat.name", "Miner Cat");

            // Шахты: catalog.shaft.<ID шахты>. Нет ключа — показывается сам ID.
            d.Add("catalog.shaft.amethyst", "Amethyst");
            d.Add("catalog.shaft.citrine", "Citrine");
            d.Add("catalog.shaft.agate", "Agate");
            d.Add("catalog.shaft.topaz", "Topaz");
            d.Add("catalog.shaft.opal", "Opal");
            d.Add("catalog.shaft.jade", "Jade");
            d.Add("catalog.shaft.onyx", "Onyx");
            d.Add("catalog.shaft.sapphire", "Sapphire");
            d.Add("catalog.shaft.tourmaline", "Tourmaline");
            d.Add("catalog.shaft.aquamarine", "Aquamarine");
            d.Add("catalog.shaft.emerald", "Emerald");
            d.Add("catalog.shaft.diamond", "Diamond");
            d.Add("catalog.shaft.garnet", "Garnet");
            d.Add("catalog.shaft.heliodor", "Heliodor");
            d.Add("catalog.shaft.ruby", "Ruby");
            d.Add("catalog.shaft.alexandrite", "Alexandrite");
            d.Add("catalog.shaft.celestine", "Celestine");
            d.Add("catalog.shaft.amber", "Amber");
            d.Add("catalog.shaft.moonstone", "Moonstone");
            d.Add("catalog.shaft.sunstone", "Sunstone");

            // Сундуки: catalog.gacha.<ID сундука>. Нет ключа — имя сундука-образца (MimicId), иначе catalog.chest.generic.
            d.Add("catalog.gacha.GachaWooden", "Wooden Chest");
            d.Add("catalog.gacha.GachaIron", "Iron Chest");
            d.Add("catalog.gacha.GachaForged", "Forged Chest");
            d.Add("catalog.gacha.GachaSilver", "Silver Chest");
            d.Add("catalog.gacha.GachaGolden", "Golden Chest");
            d.Add("catalog.gacha.GachaRoyal", "Royal Chest");
            d.Add("catalog.gacha.GachaMythical", "Mythical Chest");
            d.Add("catalog.gacha.GachaLegendary", "Legendary Chest");
            d.Add("catalog.gacha.GachaCrusher", "Exit Chest");
            d.Add("catalog.gacha.GachaEpic", "Epic Chest");
            d.Add("catalog.gacha.GachaLongEpic", "Big Epic Chest");
            d.Add("catalog.gacha.GachaRare", "Rare Chest");
            d.Add("catalog.gacha.GachaUncommon", "Uncommon Chest");
            d.Add("catalog.chest.generic", "Chest");

            // Камни по модели (RockVisualKind).
            d.Add("catalog.rock.elixir", "Elixir Vein");
            d.Add("catalog.rock.gems", "Gems");
            d.Add("catalog.rock.pass", "Gray Boulder");
            d.Add("catalog.rock.chest", "Chest in Rock");
            d.Add("catalog.rock.plain", "Rock");

            // Объекты поля (ObjectKind). {0} — имя шахты.
            d.Add("catalog.object.shaft", "{0} Shaft");
            d.Add("catalog.object.gate", "Gate");
            d.Add("catalog.object.exit", "Exit");
            d.Add("catalog.object.cart", "Forge");
            d.Add("catalog.object.obstruction", "Obstacle");

            // Карты. Управляющий: {0} — имя шахты; на ячейке карты — короткое имя (шахта или «Кузница»).
            d.Add("catalog.card.manager.cart", "Forge Manager");
            d.Add("catalog.card.manager.shaft", "Manager: {0}");
            d.Add("catalog.card.manager.shaft_senior", "Senior Manager: {0}");
            d.Add("catalog.card.short.cart", "Forge");
            // Карты-предметы по коду эффекта (StatModifierType → ключ: PresentationCatalog.CardItem).
            d.Add("catalog.card.item.golden_cart", "Golden Cart");          // 10
            d.Add("catalog.card.item.pocket_watch", "Pocket Watch");        // 7
            d.Add("catalog.card.item.golden_helmet", "Golden Helmet");      // 1
            d.Add("catalog.card.item.recruit_poster", "Hiring Poster");     // 4
            d.Add("catalog.card.item.lucky_paw", "Lucky Paw");              // 5
            d.Add("catalog.card.item.sledgehammer", "Sledgehammer");        // 6
            d.Add("catalog.card.item.gate_bell", "Gate Bell");              // 8
            d.Add("catalog.card.item.gold_vein", "Gold Vein");              // 11
            d.Add("catalog.card.item.fish_parcel", "Fish Parcel");          // 13
            d.Add("catalog.card.item.big_dynamite", "Big Dynamite");        // 22
            d.Add("catalog.card.item.clover", "Lucky Clover");              // 23
            d.Add("catalog.card.item.fish", "Fish");                        // 3
            d.Add("catalog.card.item.yarn", "Yarn Ball");                   // 12
            d.Add("catalog.card.item.milk_bowl", "Milk Bowl");              // 15
            d.Add("catalog.card.item.catnip", "Catnip");                    // 16
            d.Add("catalog.card.item.toy_mouse", "Toy Mouse");              // 19
            d.Add("catalog.card.item.cat_bed", "Cat Bed");                  // 20
            d.Add("catalog.card.item.scratcher", "Scratching Post");        // 21
            d.Add("catalog.card.item.collar", "Bell Collar");               // 24
            d.Add("catalog.card.item.lantern", "Miner's Lantern");          // 25
            d.Add("catalog.card.item.treasure_map", "Treasure Map");        // 26
            d.Add("catalog.card.item.unknown", "Card {0}");                 // код без предмета: {0} — ID карты
            // Лента эффекта на портрете карты.
            d.Add("catalog.card.tag.income", "Income");
            d.Add("catalog.card.tag.time", "Time");
            d.Add("catalog.card.tag.cap", "Limit");
            d.Add("catalog.card.tag.soon", "Soon");

            // Эффекты карточек. {0}/{1} — уже отформатированные числа (NumberFormat.Format), без значения — «1» или «0».
            d.Add("catalog.card.effect.pending", "Effect coming later");
            d.Add("catalog.card.effect.cart_income", "Forge income ×{0}");                                                  // 9, кузница
            d.Add("catalog.card.effect.shaft_automation", "Automates the {0} shaft (per the shaft requirement), income ×{1}"); // 9, шахта
            d.Add("catalog.card.effect.all_income", "Income of all buildings ×{0}");                                         // 10
            d.Add("catalog.card.effect.free_cat_sooner", "Free cat {0}s sooner");                                            // 7
            d.Add("catalog.card.effect.cat_cap", "Cat limit +{0}");                                                          // 1
            d.Add("catalog.card.effect.not_owned", "Not in collection");
            // Ожидаемый смысл неприменяемых кодов (ТЗ 10.1, кандидаты rulesVersion 2) — в окне карты, с пометкой «позже».
            d.Add("catalog.card.effect.future", "{0} — effect coming later");
            d.Add("catalog.card.effect.future.hire_level", "Level of hired cats");       // 4
            d.Add("catalog.card.effect.future.crit_chance", "Crit chance");              // 5
            d.Add("catalog.card.effect.future.crit_power", "Crit power");                // 6
            d.Add("catalog.card.effect.future.gate_timer", "Free cat per gate");         // 8
            d.Add("catalog.card.effect.future.rock_gold", "Gold from rocks");            // 11
            d.Add("catalog.card.effect.future.deliveries", "Deliveries");                // 13
            d.Add("catalog.card.effect.future.dynamite", "Dynamite damage");             // 22
            d.Add("catalog.card.effect.future.legendary", "Legendary card chance");      // 23
        }
    }
}
