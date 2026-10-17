using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Шрифт интерфейса (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6; решение 16 октября 2026, Docs/Rework/MINE_DRESSING.md «Шрифт»):
    /// гладкий округлый Nunito (SIL OFL) из Resources/Fonts — SemiBold для текста и ExtraBold (<see cref="Bold"/>) для заголовков,
    /// кнопок и чисел. Заменил пиксельный Pixeloid Sans: у Nunito различимы 0/O, 1/I/l (l с хвостиком), 5/S, есть кириллица.
    /// Мир остаётся пиксельным (Point-фильтрация атласа), а текст — SDF TextMeshPro: чёткий при любом масштабе и не зависит от
    /// пиксельной сетки. Метрики строки статических начертаний сведены к 1,22 em (Tools/Fonts/make_nunito.py), как у прежнего
    /// шрифта. Ассеты TMP создаются один раз динамическими (глифы добавляются по мере появления в тексте); недостающие знаки
    /// (→ и т. п.) — из шрифта TMP Essentials. Без TTF — шрифт TMP Essentials и одно предупреждение в журнале.
    /// </summary>
    public static class UiFonts
    {
        const string RegularPath = "Fonts/Nunito-SemiBold";
        const string BoldPath = "Fonts/Nunito-ExtraBold";
        /// <summary>Размер растеризации SDF (пикселей на em) и отступ поля расстояний.</summary>
        const int SamplingPointSize = 90;
        const int AtlasPadding = 9;
        const int AtlasSize = 1024;
        /// <summary>
        /// Сдвиг мягкой тени вниз в долях поля SDF (padding + 1 = 10 текселей): 0,7 → 7 текселей ≈ 0,08 em, у текста
        /// <see cref="PixelUi.FontBody"/> около 2,3 единицы Canvas. Сдержанная тень под светлым текстом на тёмных панелях.
        /// </summary>
        const float ShadowOffset = 0.7f;
        const float ShadowSoftness = 0.12f;

        static TMP_FontAsset s_Legacy;
        static TMP_FontAsset s_Regular;
        static TMP_FontAsset s_Bold;
        static Material s_RegularShadow;
        static Material s_BoldShadow;
        static bool s_Warned;

        /// <summary>Основной шрифт: Nunito SemiBold (без TTF — шрифт TMP Essentials).</summary>
        public static TMP_FontAsset Default
        {
            get
            {
                Resolve();
                return s_Regular;
            }
        }

        /// <summary>Шрифт заголовков, кнопок и чисел: Nunito ExtraBold (без жирного TTF — тот же ассет, что <see cref="Default"/>).</summary>
        public static TMP_FontAsset Bold
        {
            get
            {
                Resolve();
                return s_Bold;
            }
        }

        /// <summary>Загружен шрифт интерфейса из Resources/Fonts (иначе работает запасной шрифт TMP Essentials).</summary>
        public static bool IsLoaded
        {
            get
            {
                Resolve();
                return s_Regular != null && s_Regular != s_Legacy;
            }
        }

        /// <summary>Bold — отдельное начертание (ExtraBold); тогда текст не утолщается искусственно (PixelUi.SetFont).</summary>
        public static bool HasRealBold
        {
            get
            {
                Resolve();
                return s_Bold != null && s_Bold != s_Regular;
            }
        }

        /// <summary>Шрифт TMP Essentials по умолчанию: fallback шрифта интерфейса для недостающих символов (→, ★ и т. п.).</summary>
        public static TMP_FontAsset Legacy
        {
            get
            {
                if (s_Legacy != null) return s_Legacy;
                var settings = Resources.Load<TMP_Settings>("TMP Settings");
                if (settings != null) s_Legacy = TMP_Settings.defaultFontAsset;
                if (s_Legacy == null)
                {
                    var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (font != null) s_Legacy = TMP_FontAsset.CreateFontAsset(font);
                }
                return s_Legacy;
            }
        }

        /// <summary>Материал основного начертания с мягкой тенью (ink1, вниз) — для текста на тёмном фоне.</summary>
        public static Material DefaultShadow => Shadow(Default);

        /// <summary>Материал жирного начертания с мягкой тенью.</summary>
        public static Material BoldShadow => Shadow(Bold);

        /// <summary>
        /// Общий материал тени для шрифта (создаётся один раз на шрифт, тексты с ним батчатся). Для чужих шрифтов —
        /// их обычный материал: тень есть только у шрифтов кита.
        /// </summary>
        public static Material Shadow(TMP_FontAsset font)
        {
            if (font == null) return null;
            Resolve();
            if (font == s_Regular)
            {
                if (s_RegularShadow == null) s_RegularShadow = CreateShadow(font);
                return s_RegularShadow;
            }
            if (font == s_Bold)
            {
                if (s_BoldShadow == null) s_BoldShadow = CreateShadow(font);
                return s_BoldShadow;
            }
            return font.material;
        }

        static void Resolve()
        {
            if (s_Regular != null && s_Bold != null) return;
            var legacy = Legacy;
            var regular = CreateAsset(RegularPath, legacy);
            if (regular == null)
            {
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning("[CatMiners] Шрифт интерфейса " + RegularPath + " не найден в Resources — используется шрифт TMP по умолчанию."); // loc:ignore — журнал разработчика
                }
                s_Regular = legacy;
                s_Bold = legacy;
                return;
            }
            s_Regular = regular;
            s_Bold = CreateAsset(BoldPath, legacy) ?? regular;
            s_RegularShadow = null;
            s_BoldShadow = null;
        }

        static TMP_FontAsset CreateAsset(string path, TMP_FontAsset legacy)
        {
            var font = Resources.Load<Font>(path);
            if (font == null) return null;
            TMP_FontAsset asset;
            try
            {
                asset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CatMiners] Шрифт " + path + " не создан: " + e.Message); // loc:ignore — журнал разработчика
                return null;
            }
            if (asset == null) return null;
            asset.name = font.name;
            // Шейдер — как у материала шрифта TMP по умолчанию: он точно попадает в сборку (материал лежит в Resources).
            if (legacy != null && legacy.material != null && asset.material != null && legacy.material.shader != null)
                asset.material.shader = legacy.material.shader;
            if (asset.material != null) asset.material.name = font.name + " Material";
            if (legacy != null) asset.fallbackFontAssetTable = new List<TMP_FontAsset> { legacy };
            return asset;
        }

        static Material CreateShadow(TMP_FontAsset font)
        {
            var src = font.material;
            if (src == null) return null;
            var m = new Material(src) { name = font.name + " Shadow" };
            // Комбинация OUTLINE_ON + UNDERLAY_ON есть в сборке: её использует материал «Drop Shadow» TMP Essentials в Resources.
            // Контур нулевой ширины ничего не рисует.
            m.EnableKeyword("OUTLINE_ON");
            m.SetFloat("_OutlineWidth", 0f);
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", PixelPalette.Ink1);
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", -ShadowOffset);
            m.SetFloat("_UnderlayDilate", 0.1f);
            m.SetFloat("_UnderlaySoftness", ShadowSoftness);
            return m;
        }
    }

    /// <summary>
    /// Прежний API сборки uGUI кодом (Hud, DevPanel). Внешний вид — пиксельный кит <see cref="PixelUi"/>: цвета Palette
    /// переводятся в стили панелей и кнопок (<see cref="PanelStyleFor"/>, <see cref="ButtonStyleFor"/>). Новый код пишется
    /// сразу на <see cref="PixelUi"/>; здесь остаются общие помощники (Rect, Anchor, SetText, SetActive, Layout…).
    /// </summary>
    public static class Ui
    {
        static Sprite s_Rounded;
        static Sprite s_Circle;
        static Sprite s_Triangle;

        /// <summary>Устаревшая векторная форма (скругление): пиксельный UI её не использует.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (s_Rounded != null) return s_Rounded;
                const int size = 64, radius = 20;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                        float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
                        px[y * size + x] = new Color32(255, 255, 255, a);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_Rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return s_Rounded;
            }
        }

        /// <summary>Устаревший векторный круг.</summary>
        public static Sprite Circle
        {
            get
            {
                if (s_Circle != null) return s_Circle;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                        byte a = (byte)(Mathf.Clamp01(size / 2f - d) * 255);
                        px[y * size + x] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                s_Circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
                return s_Circle;
            }
        }

        /// <summary>Устаревший равнобедренный треугольник остриём вверх, со сглаженными краями.</summary>
        public static Sprite Triangle
        {
            get
            {
                if (s_Triangle != null) return s_Triangle;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[size * size];
                // Вершины: (4, 6), (60, 6), (32, 58). Покрытие пикселя — по расстоянию до трёх сторон.
                var a = new Vector2(4f, 6f);
                var b = new Vector2(60f, 6f);
                var c = new Vector2(32f, 58f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Min(EdgeDistance(a, b, c, p), Mathf.Min(EdgeDistance(b, c, a, p), EdgeDistance(c, a, b, p)));
                        byte alpha = (byte)(Mathf.Clamp01(d + 0.5f) * 255);
                        px[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                s_Triangle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
                return s_Triangle;
            }
        }

        /// <summary>Расстояние со знаком от точки до прямой ab: положительно с той же стороны, что и вершина inside.</summary>
        static float EdgeDistance(Vector2 a, Vector2 b, Vector2 inside, Vector2 p)
        {
            var e = b - a;
            var n = new Vector2(-e.y, e.x).normalized;
            if (Vector2.Dot(inside - a, n) < 0) n = -n;
            return Vector2.Dot(p - a, n);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Anchor(this RectTransform rt, float xMin, float yMin, float xMax, float yMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>
        /// Панель по цвету Palette (совместимость): тёмные непрозрачные — <see cref="PixelPanel.Dark"/>,
        /// <see cref="Palette.UiPanelLight"/> — <see cref="PixelPanel.Inset"/>, прочие цвета (шкалы, подложки) — плоский
        /// прямоугольник этого цвета. Параметр rounded больше не используется: скруглённых векторных форм в стиле нет.
        /// </summary>
        public static Image Panel(Transform parent, string name, Color color, bool rounded = true)
        {
            if (PanelStyleFor(color, out var style)) return PixelUi.Panel(parent, name, style);
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Стиль пиксельной панели для цвета старого API; false — цвет не панельный (рисуется плоским прямоугольником).</summary>
        public static bool PanelStyleFor(Color color, out PixelPanel style)
        {
            style = PixelPanel.Dark;
            if (Same(color, Palette.UiPanelLight))
            {
                style = PixelPanel.Inset;
                return true;
            }
            if (Same(color, Palette.UiPanel)) return true;
            Color.RGBToHSV(color, out _, out _, out float v);
            return color.a >= 0.8f && v < 0.3f;
        }

        /// <summary>
        /// Стиль пиксельной кнопки для цвета старого API: UiButton/Gold — золотая, UiGreen — зелёная, UiBlue — синяя,
        /// тёмные и серые (UiPanelLight, кнопки DevPanel) — тёмная, красные — красная.
        /// </summary>
        public static PixelButton ButtonStyleFor(Color color)
        {
            if (Same(color, Palette.UiButton) || Same(color, Palette.Gold)) return PixelButton.Gold;
            if (Same(color, Palette.UiGreen)) return PixelButton.Green;
            if (Same(color, Palette.UiBlue)) return PixelButton.Blue;
            if (Same(color, Palette.UiPanelLight) || Same(color, Palette.UiPanel)) return PixelButton.Dark;
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (v < 0.45f || s < 0.35f) return PixelButton.Dark;
            if (h < 0.04f || h > 0.93f) return PixelButton.Red;
            if (h < 0.17f) return PixelButton.Gold;
            if (h < 0.45f) return PixelButton.Green;
            if (h < 0.75f) return PixelButton.Blue;
            return PixelButton.Dark;
        }

        static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>
        /// Текст пиксельным шрифтом (Regular). Светлый текст получает пиксельную тень: старые панели тёмные.
        /// Новый код — <see cref="PixelUi.Text"/> с явным выбором начертания и тени.
        /// </summary>
        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            Color.RGBToHSV(color, out _, out _, out float v);
            return PixelUi.Text(parent, name, text, size, color, align, false, v > 0.6f);
        }

        /// <summary>
        /// Ссылки на кнопку кита (<see cref="PixelUi.Button"/>, <see cref="PixelUi.IconButton"/>, <see cref="Button(Transform,string,string,float,Color,UnityAction)"/>).
        /// Состояние меняется только методами: они не трогают спрайты и текст, если значение не изменилось.
        /// </summary>
        public sealed class ButtonRefs
        {
            public Button Button;
            /// <summary>Фон кнопки (ui_btn_* или ui_slot); его RectTransform задаёт место кнопки.</summary>
            public Image Image;
            /// <summary>Подпись (у ячейки-кнопки — подпись под иконкой, по умолчанию пустая).</summary>
            public TextMeshProUGUI Label;
            /// <summary>Контейнер подписи и иконки на всю кнопку: при нажатии опускается на 1 пиксель арта. Свои «тонущие» элементы — сюда.</summary>
            public RectTransform Content;
            /// <summary>Иконка (ячейка-кнопка или <see cref="SetIcon"/>), иначе null.</summary>
            public Image Icon;
            /// <summary>Красный бейдж-счётчик (у ячейки-кнопки), иначе null.</summary>
            public PixelBadge Badge;
            /// <summary>Стиль кнопки (у ячейки-кнопки не используется).</summary>
            public PixelButton Style;
            /// <summary>Ячейка-кнопка ui_slot (а не ui_btn_*).</summary>
            public bool IsSlot;
            /// <summary>
            /// Цвет старого API. Совместимость: если код поменял Color, следующий <see cref="SetInteractable"/> переводит его
            /// в стиль (<see cref="ButtonStyleFor"/>). Новый код вызывает <see cref="SetStyle"/>.
            /// </summary>
            public Color Color;

            Color m_StyleColor;
            bool m_Applied;
            bool m_AppliedOn;
            PixelButton m_AppliedStyle;

            public RectTransform Rect => Image.rectTransform;
            public bool Interactable => Button.interactable;

            internal void Init(PixelButton style, Color color)
            {
                Style = style;
                Color = color;
                m_StyleColor = color;
                Apply();
            }

            /// <summary>Доступность: недоступная кнопка — спрайт ui_btn_disabled (у ячейки — приглушённая иконка) и тусклая подпись.</summary>
            public void SetInteractable(bool on)
            {
                if (!IsSlot && !Same(Color, m_StyleColor))
                {
                    m_StyleColor = Color;
                    Style = ButtonStyleFor(Color);
                }
                if (Button.interactable != on) Button.interactable = on;
                Apply();
            }

            /// <summary>Сменить стиль кнопки (спрайты и цвет подписи).</summary>
            public void SetStyle(PixelButton style)
            {
                Style = style;
                Color = PixelUi.LegacyColor(style);
                m_StyleColor = Color;
                Apply();
            }

            /// <summary>Подпись, только если изменилась.</summary>
            public void SetLabel(string text) => SetText(Label, text);

            /// <summary>Показать или скрыть кнопку, только если состояние меняется.</summary>
            public void SetActive(bool on) => Ui.SetActive(Image, on);

            /// <summary>Иконка кнопки (создаётся при первом вызове в <see cref="Content"/> по центру); null — скрыть.</summary>
            public void SetIcon(string iconName, int scale = 1)
            {
                if (iconName == null)
                {
                    if (Icon != null) Ui.SetActive(Icon, false);
                    return;
                }
                if (Icon == null) Icon = PixelUi.Icon(Content != null ? Content : Image.transform, "icon", iconName, scale);
                else PixelUi.SetIcon(Icon, iconName, scale);
                Ui.SetActive(Icon, true);
                m_Applied = false;
                Apply();
            }

            /// <summary>Число в бейдже (0 — бейдж скрыт). Только у ячеек-кнопок; у остальных бейдж создаётся при первом вызове.</summary>
            public void SetBadge(int count)
            {
                if (Badge == null)
                {
                    if (count <= 0) return;
                    Badge = PixelUi.Badge(Image.transform, "badge");
                    Badge.PlaceAtCorner(new Vector2(1f, 1f), PixelUi.BadgeCornerOffset);
                }
                Badge.SetCount(count);
            }

            void Apply()
            {
                bool on = Button.interactable;
                if (m_Applied && on == m_AppliedOn && Style == m_AppliedStyle) return;
                m_Applied = true;
                m_AppliedOn = on;
                m_AppliedStyle = Style;
                if (IsSlot)
                {
                    if (Icon != null) Icon.color = on ? Color.white : PixelUi.DisabledIconTint;
                    if (Label != null) Label.color = on ? PixelUi.TextColor : PixelUi.TextDim;
                    return;
                }
                PixelUi.ApplyButtonSprites(Button, Image, Style);
                if (Label != null)
                {
                    Label.color = on ? PixelUi.ButtonTextColor(Style) : PixelUi.TextDim;
                    PixelUi.SetShadow(Label, !on || PixelUi.ButtonTextShadow(Style));
                }
                if (Icon != null) Icon.color = on ? Color.white : PixelUi.DisabledIconTint;
            }
        }

        /// <summary>
        /// Кнопка по цвету старого API (стиль — <see cref="ButtonStyleFor"/>), подпись жирным пиксельным шрифтом.
        /// Новый код — <see cref="PixelUi.Button"/>.
        /// </summary>
        public static ButtonRefs Button(Transform parent, string name, string label, float fontSize, Color color, UnityAction onClick)
        {
            var refs = PixelUi.Button(parent, name, label, ButtonStyleFor(color), onClick, fontSize);
            refs.Color = color;
            refs.SetInteractable(true);
            return refs;
        }

        /// <summary>
        /// Значок по цвету старого API: Palette.Gold — монета, Palette.Elixir — эликсир, красные — бейдж ui_badge_red,
        /// прочие — устаревший круг. Пропорции спрайта сохраняются. Новый код — <see cref="PixelUi.Icon"/>.
        /// </summary>
        public static Image Icon(Transform parent, string name, Color color, float size)
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            Color.RGBToHSV(color, out float h, out float s, out _);
            string sprite = null;
            if (Same(color, Palette.Gold)) sprite = "icon_coin";
            else if (Same(color, Palette.Elixir)) sprite = "icon_elixir";
            else if (s > 0.4f && (h < 0.05f || h > 0.93f)) sprite = "ui_badge_red";
            if (sprite != null)
            {
                img.sprite = PixelArt.Ui(sprite);
                img.preserveAspect = true;
                img.color = new Color(1f, 1f, 1f, color.a);
            }
            else
            {
                img.sprite = Circle;
                img.color = color;
            }
            return img;
        }

        /// <summary>Вертикальный список с автоматической раскладкой.</summary>
        public static VerticalLayoutGroup VList(RectTransform rt, float spacing, int padding)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childControlWidth = true;
            return v;
        }

        public static LayoutElement Layout(Component c, float preferredHeight, float flexibleWidth = 1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.minHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Присвоить текст, только если он изменился: лишнее присваивание заставляет TMP перестраивать сетку (ТЗ 15.3).</summary>
        public static void SetText(TMP_Text text, string value)
        {
            if (value == null) value = "";
            if (!string.Equals(text.text, value, StringComparison.Ordinal)) text.text = value;
        }

        /// <summary>Включить или выключить объект, только если состояние меняется.</summary>
        public static void SetActive(Component c, bool on)
        {
            var go = c.gameObject;
            if (go.activeSelf != on) go.SetActive(on);
        }

        /// <summary>Автоподбор размера шрифта в пределах [min, max]: длинный текст уменьшается, а не обрезается.</summary>
        public static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }
    }
}
