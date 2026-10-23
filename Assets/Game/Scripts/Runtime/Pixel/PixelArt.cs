using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>Спрайт атласа: прямоугольник (x, y от левого нижнего угла), pivot и 9-slice border — в пикселях арта.</summary>
    public readonly struct PixelSpriteDef
    {
        public readonly string Name;
        public readonly int X, Y, W, H;
        public readonly float PivotX, PivotY;
        public readonly int L, B, R, T;

        public PixelSpriteDef(string name, int x, int y, int w, int h, float pivotX, float pivotY, int l, int b, int r, int t)
        {
            Name = name;
            X = x;
            Y = y;
            W = w;
            H = h;
            PivotX = pivotX;
            PivotY = pivotY;
            L = l;
            B = b;
            R = r;
            T = t;
        }
    }

    /// <summary>
    /// Пиксельный атлас игры (Docs/Art/PIXEL_STYLE_GUIDE.md). PNG встроен в <see cref="PixelAtlasData"/> (генерирует
    /// Tools/PixelArt/build.py), загружается один раз, фильтрация Point. Спрайты создаются лениво и кэшируются:
    /// <see cref="World"/> — 32 пикселя арта на клетку поля, <see cref="Ui"/> — 4 единицы Canvas (1080×1920) на пиксель.
    /// Неизвестное имя даёт розовую заглушку и одно предупреждение в журнале.
    /// </summary>
    public static class PixelArt
    {
        /// <summary>Пикселей арта на клетку поля (PPU мировых спрайтов).</summary>
        public const int CellPixels = 32;
        /// <summary>Единиц Canvas (эталон 1080×1920) на пиксель арта в интерфейсе.</summary>
        public const float UiPixel = 4f;
        /// <summary>PPU спрайтов UI при referencePixelsPerUnit Canvas = 100.</summary>
        const float UiPpu = 100f / UiPixel;

        static Texture2D s_Atlas;
        static Texture2D s_MissingTex;
        static Dictionary<string, PixelSpriteDef> s_Defs;
        static readonly Dictionary<string, Sprite> s_World = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> s_Ui = new Dictionary<string, Sprite>();
        static readonly HashSet<string> s_Warned = new HashSet<string>();
        static Material s_SpriteTemplate;
        static Material s_MeshTemplate;
        static Material s_SpriteMaterial;
        static Material s_MeshMaterial;
        static bool s_SpriteMaterialResolved;

        /// <summary>Шаблоны материалов из сцены (гарантируют шейдеры в сборке): спрайтовый unlit и URP Unlit для мешей.</summary>
        public static void Init(Material spriteTemplate, Material meshTemplate)
        {
            s_SpriteTemplate = spriteTemplate;
            s_MeshTemplate = meshTemplate;
            s_SpriteMaterial = null;
            s_MeshMaterial = null;
            s_SpriteMaterialResolved = false;
        }

        /// <summary>Атлас загружен из встроенных данных (иначе — только заглушки).</summary>
        public static bool Loaded
        {
            get
            {
                EnsureLoaded();
                return s_Atlas != null;
            }
        }

        public static Texture2D Atlas
        {
            get
            {
                EnsureLoaded();
                return s_Atlas;
            }
        }

        public static bool Has(string name)
        {
            EnsureLoaded();
            return name != null && s_Defs.ContainsKey(name);
        }

        /// <summary>Описание спрайта (размер, pivot, border); false — нет в атласе.</summary>
        public static bool TryGetDef(string name, out PixelSpriteDef def)
        {
            EnsureLoaded();
            if (name != null && s_Defs.TryGetValue(name, out def)) return true;
            def = default;
            return false;
        }

        /// <summary>Спрайт для мира: 1 клетка = 32 пикселя арта, pivot и border из атласа.</summary>
        public static Sprite World(string name) => Get(name, CellPixels, s_World);

        /// <summary>Спрайт для uGUI: 1 пиксель арта = 4 единицы Canvas при эталонном разрешении 1080×1920.</summary>
        public static Sprite Ui(string name) => Get(name, UiPpu, s_Ui);

        /// <summary>Нормализованные UV прямоугольника спрайта в атласе (для мешей пола и стен).</summary>
        public static Rect Uv(string name)
        {
            EnsureLoaded();
            if (s_Atlas == null || name == null || !s_Defs.TryGetValue(name, out var d))
            {
                Warn(name);
                return new Rect(0, 0, 1, 1);
            }
            float w = s_Atlas.width, h = s_Atlas.height;
            return new Rect(d.X / w, d.Y / h, d.W / w, d.H / h);
        }

        /// <summary>
        /// Материал SpriteRenderer: копия шаблона из сцены (URP 2D Sprite-Unlit-Default); без шаблона — найденный шейдер,
        /// иначе null (тогда остаётся материал SpriteRenderer по умолчанию).
        /// </summary>
        public static Material SpriteMaterial
        {
            get
            {
                if (s_SpriteMaterialResolved) return s_SpriteMaterial;
                s_SpriteMaterialResolved = true;
                if (s_SpriteTemplate != null) s_SpriteMaterial = new Material(s_SpriteTemplate) { name = "cm_sprite" };
                else
                {
                    var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
                    if (shader != null) s_SpriteMaterial = new Material(shader) { name = "cm_sprite" };
                }
                return s_SpriteMaterial;
            }
        }

        /// <summary>
        /// Материал меша с UV атласа (пол, стены): непрозрачный URP Unlit из шаблона сцены с текстурой атласа.
        /// Спрайтовый шейдер на MeshRenderer не годится: в Unity 6 он читает unity_SpriteColor, который задаёт только SpriteRenderer.
        /// </summary>
        public static Material MeshMaterial
        {
            get
            {
                if (s_MeshMaterial != null) return s_MeshMaterial;
                var tex = Atlas != null ? Atlas : MissingTexture;
                var template = s_MeshTemplate;
                if (template != null) s_MeshMaterial = new Material(template) { name = "cm_atlas_mesh" };
                else
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                    s_MeshMaterial = new Material(shader) { name = "cm_atlas_mesh" };
                }
                if (s_MeshMaterial.HasProperty("_BaseMap")) s_MeshMaterial.SetTexture("_BaseMap", tex);
                if (s_MeshMaterial.HasProperty("_BaseColor")) s_MeshMaterial.SetColor("_BaseColor", Color.white);
                s_MeshMaterial.mainTexture = tex;
                return s_MeshMaterial;
            }
        }

        static Sprite Get(string name, float ppu, Dictionary<string, Sprite> cache)
        {
            if (name == null) name = "";
            if (cache.TryGetValue(name, out var s) && s != null) return s;
            EnsureLoaded();
            if (s_Atlas != null && s_Defs.TryGetValue(name, out var d))
            {
                var pivot = new Vector2(d.W > 0 ? d.PivotX / d.W : 0.5f, d.H > 0 ? d.PivotY / d.H : 0.5f);
                s = Sprite.Create(s_Atlas, new Rect(d.X, d.Y, d.W, d.H), pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(d.L, d.B, d.R, d.T));
                s.name = name;
            }
            else
            {
                Warn(name);
                s = Sprite.Create(MissingTexture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), ppu / 4f, 0, SpriteMeshType.FullRect, Vector4.zero);
                s.name = "missing:" + name;
            }
            cache[name] = s;
            return s;
        }

        static void Warn(string name)
        {
            if (s_Warned.Add(name ?? "")) Debug.LogWarning("[CatMiners] В пиксельном атласе нет спрайта «" + name + "» — показана заглушка."); // loc:ignore — журнал разработчика
        }

        static Texture2D MissingTexture
        {
            get
            {
                if (s_MissingTex != null) return s_MissingTex;
                s_MissingTex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "cm_missing" };
                var px = new Color32[64];
                for (int i = 0; i < 64; i++) px[i] = ((i % 8) / 2 + (i / 8) / 2) % 2 == 0 ? new Color32(255, 0, 200, 255) : new Color32(40, 0, 40, 255);
                s_MissingTex.SetPixels32(px);
                s_MissingTex.Apply(false, true);
                return s_MissingTex;
            }
        }

        static void EnsureLoaded()
        {
            if (s_Defs != null) return;
            s_Defs = new Dictionary<string, PixelSpriteDef>(PixelAtlasData.Sprites.Length, StringComparer.Ordinal);
            foreach (var d in PixelAtlasData.Sprites) s_Defs[d.Name] = d;
            if (string.IsNullOrEmpty(PixelAtlasData.Png)) return;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "cm_pixel_atlas" };
                if (!tex.LoadImage(Convert.FromBase64String(PixelAtlasData.Png), true))
                {
                    Debug.LogError("[CatMiners] Пиксельный атлас не декодирован."); // loc:ignore — журнал разработчика
                    return;
                }
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.anisoLevel = 0;
                s_Atlas = tex;
            }
            catch (FormatException e)
            {
                Debug.LogError("[CatMiners] Пиксельный атлас повреждён: " + e.Message); // loc:ignore — журнал разработчика
            }
        }
    }
}
