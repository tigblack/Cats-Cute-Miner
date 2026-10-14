using System.Collections.Generic;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Оформление окружения для спрайтов поля (Tools/PixelArt/sprites/themes.py): пол, стены, препятствия и декор стен
    /// берут вариант «имя_crystal» / «имя_copper», если он есть в атласе, иначе базовый спрайт тёплой штольни.
    /// Камни, постройки, котики и UI от оформления не зависят. Текущее оформление задаёт BoardView.Build по зоне
    /// (<see cref="EnvironmentThemes"/>).
    /// </summary>
    public static class PixelTheme
    {
        static readonly Dictionary<string, string>[] s_Names =
        {
            new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
        };
        static readonly Dictionary<string[], string[]>[] s_Frames =
        {
            new Dictionary<string[], string[]>(), new Dictionary<string[], string[]>(), new Dictionary<string[], string[]>(),
        };

        public static EnvironmentTheme Current { get; set; }

        public static string Suffix(EnvironmentTheme theme) =>
            theme == EnvironmentTheme.Crystal ? "_crystal" : (theme == EnvironmentTheme.Copper ? "_copper" : "");

        /// <summary>Спрайт в текущем оформлении: вариант темы, если он есть в атласе, иначе сам <paramref name="sprite"/>.</summary>
        public static string Name(string sprite)
        {
            var theme = Current;
            if (sprite == null || theme == EnvironmentTheme.Warm) return sprite;
            var cache = s_Names[(int)theme];
            if (!cache.TryGetValue(sprite, out var name))
            {
                string themed = sprite + Suffix(theme);
                name = PixelArt.Has(themed) ? themed : sprite;
                cache[sprite] = name;
            }
            return name;
        }

        /// <summary>Кадры анимации в текущем оформлении (массив кэшируется: можно звать при каждой сборке).</summary>
        public static string[] Names(string[] frames)
        {
            var theme = Current;
            if (frames == null || theme == EnvironmentTheme.Warm) return frames;
            var cache = s_Frames[(int)theme];
            if (!cache.TryGetValue(frames, out var names))
            {
                names = new string[frames.Length];
                for (int i = 0; i < frames.Length; i++) names[i] = Name(frames[i]);
                cache[frames] = names;
            }
            return names;
        }

        /// <summary>Ореол друз на стенах: фиолетовый аметист штольни, бирюза пещеры, синий азурит выработки.</summary>
        public static string CrystalGlow =>
            Current == EnvironmentTheme.Crystal ? "fx_glow_teal" : (Current == EnvironmentTheme.Copper ? "fx_glow_blue" : "fx_glow_purple");

        /// <summary>Цвет глубины оформления: фон камеры за стенами и затемнение закрытых секторов.</summary>
        public static Color Dark =>
            Current == EnvironmentTheme.Crystal ? PixelPalette.Cave0 : (Current == EnvironmentTheme.Copper ? PixelPalette.Copper0 : PixelPalette.Navy0);
    }
}
