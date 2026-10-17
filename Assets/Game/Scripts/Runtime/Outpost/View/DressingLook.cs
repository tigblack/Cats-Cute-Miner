using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Оформление семейств обстановки шахты (Docs/Rework/MINE_DRESSING.md, спрайты Tools/PixelArt/sprites/dressing.py): цвет сколов
    /// и обломков, материал звука удара, клочки на полу после разрушения. Геймплей задаёт категория семейства в ядре, а не вид.
    /// </summary>
    public readonly struct DressingLook
    {
        /// <summary>Цвет сколов при ударе и обломков при разрушении: щепки, каменная крошка, обрывки картона, волокна.</summary>
        public readonly Color Chips;
        /// <summary>Материал звука удара и разлома (у лома — ещё искры).</summary>
        public readonly HitMaterial Material;

        DressingLook(Color chips, HitMaterial material)
        {
            Chips = chips;
            Material = material;
        }

        public static DressingLook Of(string family)
        {
            switch (family)
            {
                case "planks":
                case "crates":
                case "catfurn":
                    return new DressingLook(PixelPalette.Wood5, HitMaterial.Wood);
                case "rubble":
                    return new DressingLook(PixelPalette.Stone5, HitMaterial.Stone);
                case "scrap":
                    return new DressingLook(PixelPalette.Rust6, HitMaterial.Metal);
                case "cardboard":
                    return new DressingLook(PixelPalette.Wood6, HitMaterial.Wood);
                case "yarn":
                    return new DressingLook(PixelPalette.Red4, HitMaterial.Wood);
                case "sacks":
                    return new DressingLook(PixelPalette.Cream1, HitMaterial.Wood);
                default:
                    return new DressingLook(PixelPalette.Stone4, HitMaterial.Stone);
            }
        }

        /// <summary>Клочки на полу после разрушения кучи (своё у каждого семейства; без спрайта — каменная крошка).</summary>
        public static string RestSprite(string family)
        {
            string name = "debris_" + family + "_rest";
            return PixelArt.Has(name) ? name : PixelTheme.Name("rubble_debris_0");
        }

        /// <summary>Ключ локализации имени семейства (сведения цели): outpost.dressing.&lt;семейство&gt;.</summary>
        public static string NameKey(string family) => "outpost.dressing." + family;
    }
}
