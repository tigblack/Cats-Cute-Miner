using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Пиксельные спрайты на поле (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел «Мир»).
    /// Поле лежит в плоскости XZ (клетка (column, row) → x = column + 0.5, z = Depth − row − 0.5), камера смотрит строго сверху:
    /// мировая +Z — «вверх по экрану». Спрайты лежат на поле лицом к камере внутри узла <see cref="Visual"/>:
    /// там локальная X — вправо, локальная Y — вверх по экрану (мировая +Z), единица — клетка.
    /// Порядок отрисовки задаётся явно: ниже на экране (меньше z) — позже (поверх), как в классическом виде 3/4.
    /// </summary>
    public static class PixelWorld
    {
        /// <summary>Поворот узла визуала: локальная +Y спрайта → мировая +Z, лицом к камере сверху.</summary>
        public static readonly Quaternion FaceCamera = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>Шаг порядка отрисовки на клетку по Z (внутри шага — слои объекта).</summary>
        public const int OrderPerCell = 16;
        /// <summary>Пол и стены (меш атласа).</summary>
        public const int FloorOrder = -32000;
        /// <summary>Разметка пола (клетки зоны мержа) — над полом, под объектами.</summary>
        public const int FloorDecalOrder = -31000;
        /// <summary>Затемнение закрытых секторов — над объектами, под оверлеями.</summary>
        public const int FogOrder = 24000;
        /// <summary>Частицы и вспышки — над объектами и затемнением.</summary>
        public const int FxOrder = 25000;
        /// <summary>Оверлеи мира (полосы HP, бейджи уровня, пузыри статуса, подписи): всегда поверх объектов и частиц
        /// (<see cref="OverlayOrderAt"/> для z от 0 до 64 даёт OverlayBase − 1024 … OverlayBase).</summary>
        public const int OverlayBase = 28000;

        /// <summary>Порядок отрисовки для точки опоры с мировой координатой z (ниже на экране — больше).</summary>
        public static int OrderAt(float worldZ) => Mathf.Clamp(Mathf.RoundToInt(-worldZ * OrderPerCell), -20000, 20000);

        /// <summary>Порядок оверлея (HP, бейдж, пузырь) для точки опоры z: поверх объектов, между собой — тоже по z.</summary>
        public static int OverlayOrderAt(float worldZ) => OverlayBase + Mathf.Clamp(Mathf.RoundToInt(-worldZ * 4f), -4000, 4000) * 4;

        /// <summary>Дочерний узел визуала, повёрнутый лицом к камере (см. описание класса).</summary>
        public static Transform Visual(Transform parent, string name = "visual")
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localRotation = FaceCamera;
            return t;
        }

        /// <summary>Группа сортировки на корне объекта: все его спрайты рисуются вместе, порядок — по точке опоры.</summary>
        public static SortingGroup Group(GameObject root, float worldZ)
        {
            var g = root.GetComponent<SortingGroup>();
            if (g == null) g = root.AddComponent<SortingGroup>();
            g.sortingOrder = OrderAt(worldZ);
            return g;
        }

        /// <summary>
        /// Спрайт внутри узла визуала: <paramref name="pos"/> — в клетках (x вправо, y вверх по экрану),
        /// <paramref name="order"/> — порядок внутри группы (или абсолютный без группы).
        /// </summary>
        public static SpriteRenderer Sprite(Transform visual, string name, string sprite, Vector2 pos, int order, Color? color = null)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(visual, false);
            t.localPosition = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelArt.World(sprite);
            var mat = PixelArt.SpriteMaterial;
            if (mat != null) sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            if (color.HasValue) sr.color = color.Value;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            return sr;
        }

        /// <summary>9-slice спрайт заданного размера в клетках (полоса HP, уголки выделения, пузырь).</summary>
        public static SpriteRenderer Sliced(Transform visual, string name, string sprite, Vector2 pos, Vector2 sizeCells, int order, Color? color = null)
        {
            var sr = Sprite(visual, name, sprite, pos, order, color);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = sizeCells;
            return sr;
        }

        /// <summary>Сменить кадр, только если он другой (без лишней работы рендера и без выделений памяти).</summary>
        public static void SetSprite(SpriteRenderer sr, string sprite)
        {
            if (sr == null) return;
            var s = PixelArt.World(sprite);
            if (sr.sprite != s) sr.sprite = s;
        }

        /// <summary>Снап к сетке пикселей арта (1/32 клетки): движение без «дрожания» полупикселей.</summary>
        public static float Snap(float v) => Mathf.Round(v * PixelArt.CellPixels) / PixelArt.CellPixels;

        public static Vector3 Snap(Vector3 v) => new Vector3(Snap(v.x), v.y, Snap(v.z));
    }
}
