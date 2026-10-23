using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;
using CatMiners.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CatMiners.Tests
{
    /// <summary>
    /// Каталог видимых объектов (Docs/Visual/VISUAL_ASSET_CATALOG.csv, задание визуального прохода, раздел 5): для каждого
    /// исходного ID данных — его финальное отображение (спрайты атласа, состояния, оформления), сколько раз он встречается
    /// на 144 картах, и объекты без ID в данных (пушка, окружение, эффекты, UI). Тест проверяет, что у каждого ID из
    /// используемых записей есть арт в атласе, и пишет файл (UTF-8 с BOM, разделитель — запятая).
    /// </summary>
    public class VisualAssetCatalogTests
    {
        const string Header = "category,source_id,uses_on_maps,display_name,final_sprites,states_and_variants,themed,notes";
        static readonly string[] s_Themes = { "", "_crystal", "_copper" };

        [UnityTest]
        public IEnumerator WriteVisualAssetCatalog()
        {
            yield return PlayModeUtil.LoadCoreGame();
            var gc = GameBootstrap.Controller;
            Assert.IsNotNull(gc);
            var b = gc.Balance;

            // Использование на картах: (вид, ID) → число объектов на всех зонах.
            var uses = new Dictionary<string, int>();
            int gates = 0, exits = 0, forges = 0;
            foreach (var zone in b.Zones)
            {
                var l = ZoneLayout.Build(b, zone);
                foreach (var o in l.Objects)
                {
                    string key = o.Kind + "/" + o.TypeId;
                    uses.TryGetValue(key, out int n);
                    uses[key] = n + 1;
                    if (o.Kind == ObjectKind.Gate) gates++;
                    else if (o.Kind == ObjectKind.Exit) exits++;
                    else if (o.Kind == ObjectKind.Cart) forges++;
                }
            }
            int Uses(ObjectKind kind, string id) => uses.TryGetValue(kind + "/" + id, out int n) ? n : 0;

            var rows = new List<string> { Header };
            var missing = new List<string>();
            void Need(string sprite, string owner)
            {
                if (!PixelArt.Has(sprite)) missing.Add(owner + " → " + sprite);
            }
            string Themed(string sprite) => string.Join(";", s_Themes.Select(t => sprite + t).Where(PixelArt.Has));

            // ── Камни ──
            foreach (var id in b.Rocks.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
            {
                int n = Uses(ObjectKind.Rock, id);
                var sprites = new List<string>();
                for (int v = 0; v < 3; v++)
                {
                    string s = PresentationCatalog.RockSprite(b, id, v);
                    if (!sprites.Contains(s)) sprites.Add(s);
                }
                foreach (var s in sprites) Need(s, id);
                var stages = new List<string>();
                foreach (var s in sprites)
                    for (int d = 1; d <= 3; d++)
                        if (PixelArt.Has(s + "_d" + d)) stages.Add(s + "_d" + d);
                string kind = PresentationCatalog.RockVisual(b, id).ToString();
                rows.Add(Row("rock", id, n, PresentationCatalog.RockName(b, id), string.Join(";", sprites),
                    "вид " + kind + (stages.Count > 0 ? "; повреждение 3 стадии: " + string.Join(";", stages) : "; повреждение: rock_crack_1/2"),
                    "нет", "вариант по клетке стабилен; подпись «ур. N», полоса HP"));
            }

            // ── Шахты ──
            string[] shaftParts = { "shaft_base", "shaft_wheel_0", "shaft_gems", "shaft_rubble", "shaft_ribbon", "shaft_light", "shaft_heap", "shaft_glint_0", "shaft_plaque" };
            foreach (var id in b.Shafts.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
            {
                foreach (var p in shaftParts) Need(p, id);
                var c = (Color32)PresentationCatalog.ShaftGemColor(id);
                rows.Add(Row("mine_shaft", id, Uses(ObjectKind.Shaft, id), PresentationCatalog.ShaftName(id),
                    Themed("shaft_base") + ";shaft_wheel_0..3;shaft_gems;shaft_rubble;shaft_ribbon;shaft_light;shaft_heap;shaft_glint_0..3;shaft_plaque",
                    "завал; готова к активации (лента); работает (свет, колесо); готова выплата (горка, блеск); автоматизирована (табличка)",
                    "камень входа", "цвет самоцвета #" + ColorUtility.ToHtmlStringRGB(c) + " (shaft_gems, shaft_heap)"));
            }

            // ── Препятствия ──
            foreach (var kv in b.Obstructions.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                string id = kv.Key;
                int n = Uses(ObjectKind.Obstruction, id);
                string sprites, states;
                if (PresentationCatalog.ObstructionVisual(id) == ObstructionVisualKind.Water)
                {
                    sprites = Themed("waterfall_top") + ";waterfall_0..2;waterfall_pool_0..1";
                    states = "водопад: вода 3 кадра, пена 2 кадра";
                    Need("waterfall_0", id);
                }
                else if (PresentationCatalog.ObstructionSingleSprite(id) is string single)
                {
                    Need(single, id);
                    sprites = Themed(single);
                    states = id.StartsWith("wall") ? "каменный столб у края строки ворот" : "одиночный блок 1×1";
                }
                else
                {
                    sprites = Themed("block_c") + ";block_tl/t/tr/l/r/bl/b/br;block_vt/vm/vb;block_hl/hm/hr";
                    states = "автотайлы по footprint";
                    Need("block_c", id);
                }
                rows.Add(Row("obstruction", id, n, PresentationCatalog.ObjectName(new ZoneObject { Kind = ObjectKind.Obstruction, TypeId = id }),
                    sprites, states, "да", "не цель: камень стен, кристаллов нет"));
            }

            // ── Ворота, выход, кузница, пушка ──
            foreach (var s in new[] { "gate_frame", "gate_door_closed", "gate_door_cracked", "gate_door_broken", "gate_door_open_0", "gate_door_open_1", "gate_door_open" })
                Need(s, "checkpoint");
            rows.Add(Row("gate", "checkpoint", gates, Loc("board.object.gate", "Ворота"), "gate_frame;gate_door_closed;gate_door_cracked;gate_door_broken;gate_door_open_0..1;gate_door_open",
                "закрыты; треснули; сломаны (пузырь «Открыть»); открываются покадрово; открыты", "нет", "столбы по краям — препятствия wall1x1vL/vR"));
            Need("exit_base", "exit");
            rows.Add(Row("exit", "exit", exits, Loc("board.object.exit", "Выход"), Themed("exit_base") + ";" + Themed("exit_boards") + ";exit_glow",
                "заколочен; открыт (свет)", "скала", "котики уходят в штольню"));
            Need("forge_base", "spawningcart");
            rows.Add(Row("forge", "spawningcart", forges, Loc("board.object.forge", "Кузница"), "forge_base;forge_fire_0..2;forge_glow",
                "огонь 3 кадра; дым из трубы при работе", "нет", "«кошачья печь» 2×3"));
            Need("cannon_base", "cannon");
            rows.Add(Row("cannon", "—", forges, "Пушка", "cannon_base;cannon_barrel;cannon_wheel_0..1;fx_muzzle_0..2;barrel_fly_0..1",
                "покой; выстрел (отдача, вспышка, дым)", "нет", "не объект данных: клетки за кузницей"));

            // ── Котики ──
            var miner = b.Miner;
            rows.Add(Row("miner", miner != null ? miner.Id : "tinygoblin", 0, PresentationCatalog.CatName,
                "cat_body_<окрас>_<кадр>;cat_helmet_<цвет>_<кадр>;cat_weapon_<оружие>_<кадр>;shadow_s;badge_level;digit_*",
                "6 окрасов, 8 видов оружия, 8 цветов каски, 62 кадра: ожидание, потягивание, ходьба, добыча замахом и с упора, перенос, появление, радость; объединение",
                "нет", "уровень — бейдж; оружие по кругу из 8 уровней (кирка, молот, дробилка, циркулярная пила, бур-машина, две кирки, бензопила, лазерган), на каждом круге — новый цвет каски"));

            // ── Карты ──
            foreach (var c in b.CardsInOrder)
            {
                string art = PresentationCatalog.CardArt(c);
                string bg = PresentationCatalog.CardBackground(c.Rarity);
                if (art == "icon_cards_big") missing.Add(c.Id + " → портрет карты");
                Need(bg, c.Id);
                rows.Add(Row("card", c.Id, 0, PresentationCatalog.CardName(b, c.Id),
                    (art == "card_cat" ? "card_cat;card_cat_helmet/hat;card_gem" : art) + ";" + bg + ";" + PresentationCatalog.CardTag(c),
                    "найдена / не найдена; можно улучшить", "нет", c.IsManager ? "карта управляющего" : "предмет"));
            }

            // ── Сундуки (гача) ──
            foreach (var id in b.Gacha.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
            {
                string tier = PresentationCatalog.GachaTier(b, id);
                Need("icon_chest_" + tier, id);
                rows.Add(Row("chest", id, 0, PresentationCatalog.GachaName(b, id), "icon_chest_" + tier + ";rock_chest_" + tier,
                    "иконка окна награды; сундук в породе", "нет", "уровень " + tier));
            }

            // ── Доставки ──
            foreach (var id in b.Deliveries.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
                rows.Add(Row("delivery", id, 0, id, "crate_cat;crate_gold;crate_elixir;crate_dynamite", "ящик по содержимому, «поп» при появлении", "нет", ""));

            // ── Валюты и добыча ──
            rows.Add(Row("currency", "Soft (золото)", 0, "Золото", "icon_coin;loot_gold", "счётчик HUD; монетки к счётчику", "нет", ""));
            rows.Add(Row("currency", "Core (эликсир)", 0, "Эликсир", "icon_elixir;loot_elixir", "счётчик HUD; капли к счётчику", "нет", ""));
            rows.Add(Row("currency", "Hard (самоцветы)", 0, "Самоцветы", "icon_gem;loot_hard", "счётчик HUD", "нет", ""));
            rows.Add(Row("item", "dynamite", 0, "Динамит", "icon_dynamite;fx_boom_0..4", "кнопка HUD; взрыв", "нет", ""));

            // ── Окружение и оформления ──
            rows.Add(Row("environment", "theme:Warm", 0, "Тёплая деревянная штольня", "floor_0..3;floor_gate;wall_*;beam_post;lantern_0..1;crystal_cluster_a/b;banner_paw;barrel;crate;sack;minecart_ore;rails_v;mushrooms;sign_question;chain_hang",
                "фонари мерцают", "база", "зоны 1–3 цикла (environment_themes.json)"));
            rows.Add(Row("environment", "theme:Crystal", 0, "Прохладная кристальная пещера", "*_crystal (пол, стены, препятствия, шахта, выход);crystal_spire;fx_glow_teal",
                "друзы вдвое чаще, через одну высокие", "да", "зоны 4–6 цикла"));
            rows.Add(Row("environment", "theme:Copper", 0, "Глубокая медная выработка", "*_copper (пол, стены, препятствия, шахта, выход);gear_copper_0..7;pipe_copper;fx_glow_blue",
                "пары шестерён крутятся навстречу", "да", "зоны 7–9 цикла"));
            foreach (var s in new[] { "crystal_spire", "gear_copper_0", "pipe_copper", "fx_glow_teal", "fx_glow_blue", "floor_0_crystal", "floor_0_copper" }) Need(s, "environment");

            // ── Эффекты и UI ──
            rows.Add(Row("fx", "—", 0, "Эффекты", "fx_impact_0..2;fx_hit;fx_spark;fx_coin;fx_elixir;fx_gem;fx_ring_0..3;fx_smoke_*;fx_boom_0..4;fx_muzzle_0..2;fx_puff;fx_glow_*",
                "удар, разрушение, сбор, объединение, найм, ворота, динамит", "нет", "«Меньше эффектов» — частиц вдвое меньше"));
            rows.Add(Row("ui", "—", 0, "Интерфейс", "ui_window_*;ui_panel_*;ui_btn_*;ui_bar_*;ui_toggle_*;ui_slider_knob;ui_card_*;ui_badge_*;ui_pill_*;ui_toast;card_bg_*;icon_*;bubble_world",
                "один кит PixelUi", "нет", "PIXEL_STYLE_GUIDE.md, раздел 6"));

            // The legacy catalog remains useful, and its regeneration must retain
            // the new mode's actual atlas families rather than erase appended rows.
            AddOutpostRows(rows, Need);

            Assert.IsEmpty(missing, "нет арта: " + string.Join(", ", missing));
            string path = Path.GetFullPath(Path.Combine(PlayModeUtil.ReportsDir(), "..", "Visual", "VISUAL_ASSET_CATALOG.csv"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join("\n", rows) + "\n", new UTF8Encoding(true));
            Debug.Log("[catalog] " + (rows.Count - 1) + " строк → " + path);
        }

        static string Loc(string key, string fallback) => CatMiners.Core.Text.Loc.TryGet(key, out var t) ? t : fallback;

        static void AddOutpostRows(List<string> rows, System.Action<string, string> need)
        {
            void Family(string id, string name, string source, string states, System.Func<string, bool> include)
            {
                var names = PixelAtlasData.Sprites.Select(s => s.Name).Where(include).ToArray();
                Assert.That(names, Is.Not.Empty, "Outpost atlas family " + id);
                foreach (string sprite in names) need(sprite, "Outpost/" + id);
                rows.Add(Row("outpost", id, 0, name, string.Join(";", names), states, "по имени",
                    "mode=CatOutpostV2; path=Assets/Game/Scripts/Runtime/Pixel/PixelAtlasData.g.cs; source=Tools/PixelArt/sprites/" + source +
                    "; author=code-generated; license=project; import=runtime atlas, Point, no mipmaps; status=wired to CoreGame Outpost views/HUD, portrait scene smoke passed; Android not checked; contract=Docs/Rework/ART_NAMES.md"));
            }
            Family("cat:carry", "Котики с грузом", "cats.py", "carry0..7, pickup0..3, unload0..3; шесть окрасов",
                n => n.StartsWith("cat_body_") && (n.Contains("_carry") || n.Contains("_pickup") || n.Contains("_unload")));
            foreach (string layer in new[] { "boots", "pack", "hat", "pick", "load", "sack" })
            {
                string prefix = "cat_" + layer + "_";
                Family("cat:" + layer, "Слой котика: " + layer, "cats.py", "кадры и редкости по ART_NAMES",
                    n => n.StartsWith(prefix));
            }
            Family("camp", "База и два места склада", "base.py", "настил, склад, найм, верстак, сумки, шкаф, вагонетка",
                n => new[] { "camp_", "depot_", "hire_board_", "bench_", "rack_", "wardrobe_", "cart_" }.Any(n.StartsWith));
            Family("cargo", "Груз на земле и в пути", "loot.py", "размеры мешочков, кучки, тень, свет, полёт сдачи",
                n => n.StartsWith("cargo_") || n.StartsWith("fx_sack_"));
            Family("barrier", "Барьер ворот и расчистка", "structures.py", "закрыто, повреждения, открыто, след разрушения",
                n => new[] { "gate_barrier_", "block_crack_", "shaft_crack_" }.Any(n.StartsWith) || n == "shaft_cleared");
            Family("icons", "Иконки веток и экипировки", "icons.py", "шесть веток, четыре слота, 16 предметов, статусы",
                n => n.StartsWith("icon_item_") || n.StartsWith("icon_slot_") || n.StartsWith("icon_branch_") ||
                    new[] { "icon_parts", "icon_sack", "icon_depot", "icon_base", "icon_equipment" }.Contains(n));
        }

        static string Row(string category, string id, int uses, string name, string sprites, string states, string themed, string notes) =>
            string.Join(",", new[] { category, id, uses.ToString(CultureInfo.InvariantCulture), name, sprites, states, themed, notes }.Select(Csv));

        static string Csv(string v)
        {
            v = v ?? "";
            if (v.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
