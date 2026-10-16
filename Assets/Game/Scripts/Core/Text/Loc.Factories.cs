using System.Collections.Generic;

namespace CatMiners.Core.Text
{
    public static partial class Loc
    {
        /// <summary>
        /// Раздел Factories (ТЗ 2.2 §8, §12): заводы на поле — имена 20 заводов и их товаров (приложение A, ключ — ID товара
        /// <c>milk</c>, <c>wood</c> …; ID типов JSON игрок не видит), склад, подсказка закрытого завода, плавающая кнопка
        /// «Deliver ×N», значок AUTO, тосты открытия и автодоставки, причины отказа команд заводов и рынка. Ключи начинаются с «factories.».
        /// Чтение имён — <c>Runtime.Outpost.FactoryText</c> (имя завода и товара по ID товара).
        /// </summary>
        static partial void RegisterFactories(Dictionary<string, string> d)
        {
            // Заводы по номеру типа (порядок MineShafts и строк рынка): имя завода — factories.name.<good>, товар — factories.good.<good>.
            d.Add("factories.name.milk", "Dairy");
            d.Add("factories.good.milk", "Milk");
            d.Add("factories.name.wood", "Sawmill");
            d.Add("factories.good.wood", "Wood");
            d.Add("factories.name.fish", "Fishery");
            d.Add("factories.good.fish", "Fish");
            d.Add("factories.name.yarn", "Yarn Mill");
            d.Add("factories.good.yarn", "Yarn");
            d.Add("factories.name.catfood", "Cat Food Factory");
            d.Add("factories.good.catfood", "Cat Food");
            d.Add("factories.name.boxes", "Box Factory");
            d.Add("factories.good.boxes", "Boxes");
            d.Add("factories.name.bricks", "Brickworks");
            d.Add("factories.good.bricks", "Bricks");
            d.Add("factories.name.steel", "Steel Mill");
            d.Add("factories.good.steel", "Steel");
            d.Add("factories.name.glass", "Glassworks");
            d.Add("factories.good.glass", "Glass");
            d.Add("factories.name.catnip", "Catnip Farm");
            d.Add("factories.good.catnip", "Catnip");
            d.Add("factories.name.toys", "Toy Factory");
            d.Add("factories.good.toys", "Toys");
            d.Add("factories.name.scratchers", "Scratcher Works");
            d.Add("factories.good.scratchers", "Scratching Posts");
            d.Add("factories.name.cushions", "Cushion Factory");
            d.Add("factories.good.cushions", "Cushions");
            d.Add("factories.name.bells", "Bell Foundry");
            d.Add("factories.good.bells", "Bells");
            d.Add("factories.name.collars", "Collar Works");
            d.Add("factories.good.collars", "Collars");
            d.Add("factories.name.gears", "Gear Works");
            d.Add("factories.good.gears", "Gears");
            d.Add("factories.name.lanterns", "Lantern Works");
            d.Add("factories.good.lanterns", "Lanterns");
            d.Add("factories.name.honey", "Apiary");
            d.Add("factories.good.honey", "Honey");
            d.Add("factories.name.silk", "Silk Mill");
            d.Add("factories.good.silk", "Silk");
            d.Add("factories.name.caviar", "Caviar Cannery");
            d.Add("factories.good.caviar", "Caviar");
            // Завод неизвестного товара (данные без правил заводов) — общее имя.
            d.Add("factories.name.generic", "Factory");
            d.Add("factories.good.generic", "Goods");

            // Склад товаров во дворе лагеря (§6.1): слово depot остаётся за приёмником монет. В интерфейсе — шапка окна рынка
            // «Market · Warehouse» (тап по складу открывает это окно): {0} — outpost.market.title, {1} — factories.warehouse.
            d.Add("factories.warehouse", "Warehouse");
            d.Add("factories.market_title", "{0} · {1}");

            // Сведения закрытого завода — удержание на нём (§2 п. 1): {0} — имя завода. Состояния Working, Ready и Stopped названы
            // в строках сведений outpost.factory.working/ready/stopped (§4.3, §8) — отдельных ключей-слов нет.
            d.Add("factories.locked_hint", "{0} — locked. Break the chains");

            // Плавающая кнопка над заводом в Ready (§4.3, §8): {0} — единиц в готовой партии. Короткая «иконка ×N» — соседние
            // готовые заводы не дают места полной (решение владельца 15.10.2026 «сокращать в тесноте»).
            d.Add("factories.deliver", "Deliver ×{0}");
            d.Add("factories.deliver_count", "×{0}");
            d.Add("factories.deliver_short", "Deliver");
            // Значок автодоставки у полосы завода (§8).
            d.Add("factories.auto", "AUTO");

            // Тосты поля (§7.1, §8): {0} — имя завода.
            d.Add("factories.toast.unlocked", "{0} unlocked!");
            d.Add("factories.toast.automated", "{0} now delivers automatically");

            // Причины отказа команд DeliverFactory/SellGoods (CORE_SPEC §16.5, UI_API §13); «phase» — outpost.reason.phase.
            d.Add("factories.reason.not_ready", "Not ready yet");
            d.Add("factories.reason.locked", "Break the chains first");
            d.Add("factories.reason.no_goods", "Nothing to sell");
            d.Add("factories.reason.no_factories", "No factories in this mine");
        }
    }
}
