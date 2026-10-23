using System.Collections.Generic;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Цвета игры (ТЗ 14): тёплые постройки на холодном камне. Значения взяты из палитры пиксель-арта
    /// (<see cref="PixelPalette"/>, Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 2), чтобы частицы, подписи и интерфейс
    /// совпадали по цвету со спрайтами. Имена сохранены для совместимости с кодом, который их использует.
    /// </summary>
    public static class Palette
    {
        public static readonly Color FloorOpenA = PixelPalette.Slate3;
        public static readonly Color FloorOpenB = PixelPalette.Slate2;
        public static readonly Color FloorClosed = PixelPalette.Navy1;
        public static readonly Color Wall = PixelPalette.Navy2;
        public static readonly Color WallTop = PixelPalette.Navy3;
        public static readonly Color Obstruction = PixelPalette.Navy4;
        public static readonly Color Water = PixelPalette.Blue5;
        // Цвета пород — для осколков и вспышек камней: серый камень, эликсир, самоцветы, дерево сундука, жетон сезона.
        public static readonly Color RockBasic = PixelPalette.Stone5;
        public static readonly Color RockSoft = PixelPalette.Purple4;
        public static readonly Color RockHard = PixelPalette.Teal4;
        public static readonly Color RockChest = PixelPalette.Wood4;
        public static readonly Color RockPass = PixelPalette.Blue4;
        public static readonly Color Wood = PixelPalette.Wood4;
        public static readonly Color WoodDark = PixelPalette.Wood2;
        public static readonly Color Metal = PixelPalette.Stone6;
        public static readonly Color Gold = PixelPalette.Gold3;
        public static readonly Color Elixir = PixelPalette.Purple4;
        public static readonly Color Fire = PixelPalette.Amber3;
        public static readonly Color HpBack = PixelPalette.Ink2;
        public static readonly Color HpFill = PixelPalette.Gold3;
        public static readonly Color Highlight = PixelPalette.Gold4;
        public static readonly Color UiPanel = new Color(PixelPalette.Navy1.r, PixelPalette.Navy1.g, PixelPalette.Navy1.b, 0.97f);
        public static readonly Color UiPanelLight = new Color(PixelPalette.Navy3.r, PixelPalette.Navy3.g, PixelPalette.Navy3.b, 0.95f);
        public static readonly Color UiButton = PixelPalette.Amber2;
        public static readonly Color UiButtonDisabled = PixelPalette.Slate4;
        public static readonly Color UiGreen = PixelPalette.Green4;
        public static readonly Color UiBlue = PixelPalette.Blue4;
        public static readonly Color UiText = PixelPalette.Cream3;
        public static readonly Color UiTextDim = PixelPalette.Cream1;
        // Метки целей и выделение котика.
        public static readonly Color TargetLabel = PixelPalette.Cream3;
        public static readonly Color SelectionRing = PixelPalette.Gold3;
        // Лагерь у кузницы и окружение (ТЗ 9.1, 14): тёплый свет построек на холодном камне.
        public static readonly Color Lantern = PixelPalette.Amber4;
        public static readonly Color LanternCap = PixelPalette.Ink3;
        public static readonly Color TentCanvas = PixelPalette.Wood6;
        public static readonly Color TentStripe = PixelPalette.Red4;
        public static readonly Color TentDoor = PixelPalette.Wood0;
        public static readonly Color Pennant = PixelPalette.Red4;
        public static readonly Color PennantAlt = PixelPalette.Gold4;
        public static readonly Color PennantBlue = PixelPalette.Blue5;
        public static readonly Color Rope = PixelPalette.Wood5;
        public static readonly Color Bedroll = PixelPalette.Blue4;
        public static readonly Color Rail = PixelPalette.Stone5;
        public static readonly Color CartIron = PixelPalette.Stone3;
        public static readonly Color CrystalDefault = PixelPalette.Blue6;
        // Свет нового сектора после открытия ворот (ТЗ 11.1, 14): мягкий, без сильной вспышки.
        public static readonly Color GateLight = PixelPalette.Amber4;

        // Окрасы котиков (как fur_* в палитре): рыжий, серый, чёрный, белый, кремовый, коричневый.
        static readonly Color[] s_Fur =
        {
            PixelPalette.FurGinger2, PixelPalette.FurGray2, PixelPalette.FurBlack2,
            PixelPalette.FurWhite3, PixelPalette.FurCream2, PixelPalette.FurBrown2,
        };

        // Каски по (уровень − 1) mod 8: yellow, green, blue, purple, red, teal, pink, white (руководство по стилю, раздел 5).
        static readonly Color[] s_Helmet =
        {
            PixelPalette.Gold3, PixelPalette.Green4, PixelPalette.Blue4, PixelPalette.Purple4,
            PixelPalette.Red4, PixelPalette.Teal4, PixelPalette.Pink2, PixelPalette.Cream3,
        };

        public static Color Fur(int seed) => s_Fur[Mathf.Abs(seed) % s_Fur.Length];
        public static Color Helmet(int level) => s_Helmet[Mathf.Abs(level - 1) % s_Helmet.Length];

        /// <summary>Цвет самоцвета шахты — из каталога отображения (ТЗ 4.1).</summary>
        public static Color Gem(string shaftId) => PresentationCatalog.ShaftGemColor(shaftId);

        /// <summary>Цвет породы по модели камня из каталога отображения.</summary>
        public static Color RockColor(RockVisualKind kind)
        {
            switch (kind)
            {
                case RockVisualKind.Elixir: return RockSoft;
                case RockVisualKind.Gems: return RockHard;
                case RockVisualKind.Chest: return RockChest;
                case RockVisualKind.Pass: return RockPass;
                default: return RockBasic;
            }
        }

        public static Color Dim(Color c, float k = 0.35f) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }

    /// <summary>Материалы URP Lit/Unlit с кэшем по цвету. Базовые материалы приходят из сцены (гарантирует шейдеры в сборке).</summary>
    public static class Materials
    {
        static Material s_LitTemplate;
        static Material s_UnlitTemplate;
        static readonly Dictionary<long, Material> s_Cache = new Dictionary<long, Material>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        public static void Init(Material lit, Material unlit)
        {
            s_LitTemplate = lit;
            s_UnlitTemplate = unlit;
        }

        static Material LitTemplate
        {
            get
            {
                if (s_LitTemplate == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    s_LitTemplate = new Material(shader);
                }
                return s_LitTemplate;
            }
        }

        static Material UnlitTemplate
        {
            get
            {
                if (s_UnlitTemplate == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    s_UnlitTemplate = new Material(shader);
                }
                return s_UnlitTemplate;
            }
        }

        static long Key(Color c, int kind)
        {
            var c32 = (Color32)c;
            return ((long)kind << 40) | ((long)c32.r << 24) | ((long)c32.g << 16) | ((long)c32.b << 8) | c32.a;
        }

        public static Material Lit(Color c) => Get(c, 0);
        public static Material Glow(Color c) => Get(c, 1);
        public static Material Unlit(Color c) => Get(c, 2);

        static Material Get(Color c, int kind)
        {
            long key = Key(c, kind);
            if (s_Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(kind == 2 ? UnlitTemplate : LitTemplate) { name = "cm_" + kind + "_" + ColorUtility.ToHtmlStringRGBA(c) };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, 0.15f);
            if (kind == 1 && m.HasProperty(EmissionId))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionId, c * 1.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            s_Cache[key] = m;
            return m;
        }
    }

    /// <summary>Процедурные меши: кубы и сферы из встроенных примитивов, низкополигональный камень, пирамидка для ушей.</summary>
    public static class Meshes
    {
        static readonly Dictionary<PrimitiveType, Mesh> s_Primitives = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<int, Mesh> s_Rocks = new Dictionary<int, Mesh>();
        static Mesh s_Pyramid;
        static Mesh s_Prism;
        static Mesh s_Ring;

        public static Mesh Primitive(PrimitiveType type)
        {
            if (s_Primitives.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            s_Primitives[type] = mesh;
            return mesh;
        }

        /// <summary>Низкополигональный камень: икосаэдр с плоскими гранями и сдвигом вершин (сид — вариант формы).</summary>
        public static Mesh Rock(int variant)
        {
            variant = Mathf.Abs(variant) % 8;
            if (s_Rocks.TryGetValue(variant, out var cached) && cached != null) return cached;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var rng = new System.Random(1234 + variant * 977);
            for (int i = 0; i < v.Count; i++)
            {
                var n = v[i].normalized;
                float k = 0.78f + (float)rng.NextDouble() * 0.38f;
                v[i] = new Vector3(n.x * k, n.y * k * 0.82f, n.z * k);
            }
            var verts = new List<Vector3>(f.Length);
            var tris = new int[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                verts.Add(v[f[i]]);
                tris[i] = i;
            }
            var mesh = new Mesh { name = "rock" + variant };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            s_Rocks[variant] = mesh;
            return mesh;
        }

        /// <summary>Четырёхгранная пирамидка с основанием 1×1 и высотой 1 (уши, крыши, кристаллы).</summary>
        public static Mesh Pyramid
        {
            get
            {
                if (s_Pyramid != null) return s_Pyramid;
                var a = new Vector3(-0.5f, 0, -0.5f);
                var b = new Vector3(0.5f, 0, -0.5f);
                var c = new Vector3(0.5f, 0, 0.5f);
                var d = new Vector3(-0.5f, 0, 0.5f);
                var top = new Vector3(0, 1, 0);
                Vector3[] verts = { a, top, b, b, top, c, c, top, d, d, top, a, a, b, c, a, c, d };
                int[] tris = new int[verts.Length];
                for (int i = 0; i < tris.Length; i++) tris[i] = i;
                s_Pyramid = new Mesh { name = "pyramid", vertices = verts, triangles = tris };
                s_Pyramid.RecalculateNormals();
                s_Pyramid.RecalculateBounds();
                return s_Pyramid;
            }
        }

        /// <summary>Треугольная призма (двускатная крыша): основание 1×1, высота 1, конёк вдоль Z.</summary>
        public static Mesh Prism
        {
            get
            {
                if (s_Prism != null) return s_Prism;
                var a = new Vector3(-0.5f, 0, -0.5f);
                var b = new Vector3(0.5f, 0, -0.5f);
                var c = new Vector3(0, 1, -0.5f);
                var a2 = new Vector3(-0.5f, 0, 0.5f);
                var b2 = new Vector3(0.5f, 0, 0.5f);
                var c2 = new Vector3(0, 1, 0.5f);
                Vector3[] verts = { a, c, b, a2, b2, c2, a, a2, c2, a, c2, c, b, c, c2, b, c2, b2 };
                int[] tris = new int[verts.Length];
                for (int i = 0; i < tris.Length; i++) tris[i] = i;
                s_Prism = new Mesh { name = "prism", vertices = verts, triangles = tris };
                s_Prism.RecalculateNormals();
                s_Prism.RecalculateBounds();
                return s_Prism;
            }
        }

        /// <summary>Плоское кольцо в плоскости XZ (нормаль вверх): внешний радиус 0.5, внутренний 0.38.</summary>
        public static Mesh Ring
        {
            get
            {
                if (s_Ring != null) return s_Ring;
                const int segments = 40;
                const float outer = 0.5f, inner = 0.38f;
                var verts = new Vector3[segments * 2];
                var normals = new Vector3[segments * 2];
                var tris = new int[segments * 6];
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts[i * 2] = dir * outer;
                    verts[i * 2 + 1] = dir * inner;
                    normals[i * 2] = Vector3.up;
                    normals[i * 2 + 1] = Vector3.up;
                    int n = (i + 1) % segments;
                    int t = i * 6;
                    // Обход по часовой стрелке при взгляде сверху — лицевая сторона смотрит вверх.
                    tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = n * 2;
                    tris[t + 3] = n * 2; tris[t + 4] = i * 2 + 1; tris[t + 5] = n * 2 + 1;
                }
                s_Ring = new Mesh { name = "ring", vertices = verts, normals = normals, triangles = tris };
                s_Ring.RecalculateBounds();
                return s_Ring;
            }
        }

        /// <summary>Создать дочерний объект с мешем и материалом.</summary>
        public static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPos, Vector3 localScale, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static GameObject Cube(Transform parent, string name, Color color, Vector3 pos, Vector3 scale, Quaternion? rot = null) =>
            Part(parent, name, Primitive(PrimitiveType.Cube), Materials.Lit(color), pos, scale, rot);

        public static GameObject Sphere(Transform parent, string name, Color color, Vector3 pos, Vector3 scale) =>
            Part(parent, name, Primitive(PrimitiveType.Sphere), Materials.Lit(color), pos, scale);

        public static GameObject Cylinder(Transform parent, string name, Color color, Vector3 pos, Vector3 scale, Quaternion? rot = null) =>
            Part(parent, name, Primitive(PrimitiveType.Cylinder), Materials.Lit(color), pos, scale, rot);

        public static void SetColor(GameObject go, Color c)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = Materials.Lit(c);
        }
    }
}
