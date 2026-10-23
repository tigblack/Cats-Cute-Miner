using System.Collections.Generic;
using CatMiners.Core.Text;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Тексты заводов для поля и HUD (ТЗ 2.2 §8, §12; раздел Loc «Factories»): имя завода и товара по ID товара, подписи кнопки
    /// «Deliver», причины отказа команд заводов и рынка. Имена и причины берутся из словаря при каждом вызове: вызывать при смене
    /// значения, не каждый кадр. Подписи кнопки и имена иконок кэшируются — без выделений при повторе.
    /// </summary>
    public static class FactoryText
    {
        /// <summary>Имя завода по ID товара («milk» → «Dairy»); неизвестный товар — «Factory».</summary>
        public static string FactoryName(string goodId) =>
            goodId != null && Loc.TryGet("factories.name." + goodId, out var text) ? text : Loc.T("factories.name.generic");

        /// <summary>Имя товара по ID («milk» → «Milk»); неизвестный — «Goods».</summary>
        public static string GoodName(string goodId) =>
            goodId != null && Loc.TryGet("factories.good." + goodId, out var text) ? text : Loc.T("factories.good.generic");

        static readonly Dictionary<int, string> s_Deliver = new Dictionary<int, string>();
        static readonly Dictionary<int, string> s_DeliverCount = new Dictionary<int, string>();

        /// <summary>Полная подпись плавающей кнопки «Deliver ×N» (§4.3, §8); кэш по N.</summary>
        public static string DeliverLabel(int units) => Cached(s_Deliver, "factories.deliver", units);

        /// <summary>
        /// Короткая подпись «×N» той же кнопки — соседние готовые заводы не дают места полной (решение владельца 15.10.2026
        /// «сокращать в тесноте»); кэш по N.
        /// </summary>
        public static string DeliverCount(int units) => Cached(s_DeliverCount, "factories.deliver_count", units);

        static string Cached(Dictionary<int, string> cache, string key, int units)
        {
            if (cache.TryGetValue(units, out var text)) return text;
            text = Loc.F(key, units);
            if (cache.Count < 512) cache[units] = text;
            return text;
        }

        /// <summary>
        /// Текст причины отказа «Deliver»/«Sell» (коды CORE_SPEC §16.5): заводские коды — из раздела Factories, остальные
        /// («phase», «loading», «transition» …) — общий <see cref="OutpostUiText.Reason"/>.
        /// </summary>
        public static string Reason(string code)
        {
            switch (code)
            {
                case "not-ready": return Loc.T("factories.reason.not_ready");
                case "locked": return Loc.T("factories.reason.locked");
                case "no-goods": return Loc.T("factories.reason.no_goods");
                case "no-factories": return Loc.T("factories.reason.no_factories");
                default: return OutpostUiText.Reason(code);
            }
        }

        static readonly Dictionary<string, string> s_Icons = new Dictionary<string, string>(System.StringComparer.Ordinal);
        static readonly Dictionary<string, string> s_SmallIcons = new Dictionary<string, string>(System.StringComparer.Ordinal);

        /// <summary>Иконка товара 16×16 для UI (рынок, кнопка «Deliver», стрелка у края, «+N»); имя кэшируется — без выделений.</summary>
        public static string GoodIcon(string goodId) => Icon(s_Icons, goodId, "");

        /// <summary>Малая иконка товара 8×8 (заглушка трубы).</summary>
        public static string GoodIconSmall(string goodId) => Icon(s_SmallIcons, goodId, "_8");

        static string Icon(Dictionary<string, string> cache, string goodId, string suffix)
        {
            if (goodId == null) return "icon_factory";
            if (cache.TryGetValue(goodId, out var name)) return name;
            name = "goods_" + goodId + suffix;
            if (!PixelArt.Has(name)) name = "icon_factory";
            if (PixelArt.Loaded) cache[goodId] = name;
            return name;
        }
    }
}
