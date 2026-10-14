using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>Стиль 9-slice панели кита (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6).</summary>
    public enum PixelPanel
    {
        /// <summary>ui_panel_dark — HUD, списки, контекстная панель.</summary>
        Dark,
        /// <summary>ui_panel_wood — таблички-заголовки («Шахта N»).</summary>
        Wood,
        /// <summary>ui_panel_inset — утопленные строки внутри окон и панелей.</summary>
        Inset,
        /// <summary>ui_panel_light — светлая «пергаментная» панель (обучение, текст тёмный).</summary>
        Light,
        /// <summary>ui_window — рамка модального окна.</summary>
        Window,
        /// <summary>ui_window_header — деревянная планка заголовка окна.</summary>
        Header,
        /// <summary>ui_toast — всплывающее сообщение.</summary>
        Toast,
        /// <summary>ui_pill — тёмная «таблетка»: таймеры, короткие подписи.</summary>
        Pill,
        /// <summary>ui_bubble — речевой пузырь обучения (текст тёмный).</summary>
        Bubble,
        /// <summary>ui_slot — квадратная ячейка (без кнопки).</summary>
        Slot,
        /// <summary>ui_focus_frame — золотые уголки фокуса, середина прозрачная.</summary>
        Focus,
        /// <summary>ui_dashed_frame — пунктирная пустая ячейка (без «+»).</summary>
        Dashed,
    }

    /// <summary>Стиль кнопки: gold — главное действие экрана (одно), green — подтверждение и сбор, blue — второстепенное, red — опасное, dark — нейтральное.</summary>
    public enum PixelButton
    {
        Gold,
        Green,
        Blue,
        Red,
        Dark,
    }

    /// <summary>Цвет заливки шкалы (ui_bar_fill_*).</summary>
    public enum PixelBarFill
    {
        Gold,
        Green,
        Purple,
        Red,
    }

    /// <summary>Редкость карточки (рамка ui_card_frame_*). Из Core: <see cref="PixelUi.RarityOf"/>.</summary>
    public enum PixelRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>
    /// Пиксельный UI-кит (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6): все окна, панели, кнопки, подсказки обучения и новые
    /// фичи собираются только отсюда. Спрайты — <see cref="PixelArt.Ui"/> (1 пиксель арта = 4 единицы Canvas при эталоне
    /// 1080×1920, фильтрация Point), панели и кнопки — Image.Type.Sliced с pixelsPerUnitMultiplier = 1. Размеры и отступы
    /// кратны <see cref="Px"/>. Цвета — только <see cref="PixelPalette"/> через константы ниже.
    /// Все фабрики вызываются при построении экрана; в Update меняются только значения (SetText/SetValue/SetCount…),
    /// и только когда они изменились.
    /// </summary>
    public static class PixelUi
    {
        /// <summary>Единиц Canvas на пиксель арта: шаг всех размеров и отступов.</summary>
        public const float Px = PixelArt.UiPixel;

        // Размеры шрифта: пиксель Pixeloid Sans = 1/9 размера (UiFonts.EmPixels), поэтому значения кратны 9 и дают целые
        // 2…8 единиц Canvas на пиксель шрифта. Подобраны под прежние размеры Tiny5 (24/32/48/56/64/88): высота заглавных и
        // ширина строк почти не изменились, а букв стало больше пикселей.
        /// <summary>18: пиксель шрифта = 2 ед. Подписи, бейджи, мелкие пояснения.</summary>
        public const float FontSmall = 18f;
        /// <summary>27: пиксель шрифта = 3 ед. Основной текст, строки окон и списков, подсказки обучения.</summary>
        public const float FontBody = 27f;
        /// <summary>36: пиксель шрифта = 4 ед. = 1 пиксель арта. Кнопки, числа HUD, заголовки окон и панелей, текст экрана конца кампании.</summary>
        public const float FontLarge = 36f;
        /// <summary>45: пиксель шрифта = 5 ед. Главные кнопки и крупные подписи (окно карточки, переход на следующую шахту).</summary>
        public const float FontTitle = 45f;
        /// <summary>54: пиксель шрифта = 6 ед. Резерв для особых акцентов (сейчас не используется).</summary>
        public const float FontHero = 54f;
        /// <summary>72: пиксель шрифта = 8 ед. = 2 пикселя арта. Резерв для экранов-событий (сейчас не используется).</summary>
        public const float FontHuge = 72f;

        /// <summary>Основной текст (cream3).</summary>
        public static readonly Color TextColor = PixelPalette.Cream3;
        /// <summary>Приглушённый текст, недоступные кнопки (cream1).</summary>
        public static readonly Color TextDim = PixelPalette.Cream1;
        /// <summary>Акцент: золото, заголовки окон, цены (gold3).</summary>
        public static readonly Color TextAccent = PixelPalette.Gold3;
        /// <summary>Светлый акцент: выделенные значения (gold4).</summary>
        public static readonly Color TextAccentLight = PixelPalette.Gold4;
        /// <summary>Тёмный текст на золотых кнопках, светлых панелях и пузырях (wood0).</summary>
        public static readonly Color TextDark = PixelPalette.Wood0;
        /// <summary>Нехватка, ошибка (red5).</summary>
        public static readonly Color TextBad = PixelPalette.Red5;
        /// <summary>Готово, прибавка (green5).</summary>
        public static readonly Color TextGood = PixelPalette.Green5;
        /// <summary>Эликсир (purple5).</summary>
        public static readonly Color TextElixir = PixelPalette.Purple5;
        /// <summary>Доход в секунду под золотом в верхней полосе: холодный светлый, как «+120/с» на концепте (navy8).</summary>
        public static readonly Color TextIncome = PixelPalette.Navy8;
        /// <summary>Затемнение под окнами и вокруг цели обучения (ink0, alpha 0,62 — как у закрытых секторов).</summary>
        public static readonly Color ShadeColor = new Color(PixelPalette.Ink0.r, PixelPalette.Ink0.g, PixelPalette.Ink0.b, 0.62f);
        /// <summary>Иконка недоступной ячейки-кнопки: та же иконка, приглушённая прозрачностью.</summary>
        public static readonly Color DisabledIconTint = new Color(1f, 1f, 1f, 0.4f);

        /// <summary>Открывающий тег rich text приглушённого цвета (cream1) — для частей строки; закрывающий — "&lt;/color&gt;".</summary>
        public const string DimTag = "<color=#C8B8A0>";
        /// <summary>Открывающий тег rich text акцента (gold3).</summary>
        public const string AccentTag = "<color=#F2C230>";

        /// <summary>Высота обычной кнопки: 28 пикселей арта.</summary>
        public const float ButtonHeight = 28 * Px;
        /// <summary>Высота кнопки в строке списка: 20 пикселей арта.</summary>
        public const float SmallButtonHeight = 20 * Px;
        /// <summary>Сторона ячейки-кнопки нижней панели (иконка icon_*_big 24 px ×1 с рамкой ui_slot): 40 пикселей арта.</summary>
        public const float SlotSize = 40 * Px;
        /// <summary>Сдвиг бейджа внутрь от угла родителя.</summary>
        public const float BadgeCornerOffset = 2 * Px;
        /// <summary>Губа кнопки (2 px) поднимает лицо на 1 px: центр подписи выше геометрического на <see cref="Px"/>.</summary>
        const float LipLift = Px;

        static readonly string[] s_CountTexts = BuildCounts(99);

        // ───────────────────────────── Спрайты ─────────────────────────────

        /// <summary>Спрайт UI по имени; если его нет в атласе (необязательный спрайт), берётся fallback.</summary>
        public static Sprite SpriteOr(string name, string fallback = null)
        {
            if (fallback != null && !PixelArt.Has(name)) name = fallback;
            return PixelArt.Ui(name);
        }

        public static string PanelSprite(PixelPanel style)
        {
            switch (style)
            {
                case PixelPanel.Wood: return "ui_panel_wood";
                case PixelPanel.Inset: return "ui_panel_inset";
                case PixelPanel.Light: return "ui_panel_light";
                case PixelPanel.Window: return "ui_window";
                case PixelPanel.Header: return "ui_window_header";
                case PixelPanel.Toast: return "ui_toast";
                case PixelPanel.Pill: return "ui_pill";
                case PixelPanel.Bubble: return "ui_bubble";
                case PixelPanel.Slot: return "ui_slot";
                case PixelPanel.Focus: return "ui_focus_frame";
                case PixelPanel.Dashed: return PixelArt.Has("ui_dashed_frame") ? "ui_dashed_frame" : "ui_dashed_slot";
                default: return "ui_panel_dark";
            }
        }

        public static string ButtonSprite(PixelButton style)
        {
            switch (style)
            {
                case PixelButton.Green: return "ui_btn_green";
                case PixelButton.Blue: return "ui_btn_blue";
                case PixelButton.Red: return "ui_btn_red";
                case PixelButton.Dark: return "ui_btn_dark";
                default: return "ui_btn_gold";
            }
        }

        public static string ButtonPressedSprite(PixelButton style) => ButtonSprite(style) + "_pressed";

        /// <summary>Цвет подписи: тёмный на золотой кнопке, светлый (с тенью) на остальных.</summary>
        public static Color ButtonTextColor(PixelButton style) => style == PixelButton.Gold ? TextDark : TextColor;

        /// <summary>Подписи светлых кнопок получают пиксельную тень; на золотой тёмный текст без тени.</summary>
        public static bool ButtonTextShadow(PixelButton style) => style != PixelButton.Gold;

        /// <summary>Цвет Palette, соответствующий стилю (для совместимости со старым API).</summary>
        public static Color LegacyColor(PixelButton style)
        {
            switch (style)
            {
                case PixelButton.Green: return Palette.UiGreen;
                case PixelButton.Blue: return Palette.UiBlue;
                case PixelButton.Red: return PixelPalette.Red4;
                case PixelButton.Dark: return Palette.UiPanelLight;
                default: return Palette.UiButton;
            }
        }

        public static string BarFillSprite(PixelBarFill fill)
        {
            switch (fill)
            {
                case PixelBarFill.Green: return "ui_bar_fill_green";
                case PixelBarFill.Purple: return "ui_bar_fill_purple";
                case PixelBarFill.Red: return "ui_bar_fill_red";
                default: return "ui_bar_fill_gold";
            }
        }

        public static string CardFrameSprite(PixelRarity rarity)
        {
            switch (rarity)
            {
                case PixelRarity.Uncommon: return "ui_card_frame_uncommon";
                case PixelRarity.Rare: return "ui_card_frame_rare";
                case PixelRarity.Epic: return "ui_card_frame_epic";
                case PixelRarity.Legendary: return "ui_card_frame_legendary";
                default: return "ui_card_frame_common";
            }
        }

        /// <summary>Редкость Core (Rarity.Common = 1 … Legendary = 5, Majestic = 6) → рамка карточки.</summary>
        public static PixelRarity RarityOf(int coreRarity)
        {
            if (coreRarity <= 1) return PixelRarity.Common;
            if (coreRarity >= 5) return PixelRarity.Legendary;
            return (PixelRarity)(coreRarity - 1);
        }

        /// <summary>Ширина рамки панели в единицах Canvas (border спрайта): отступ содержимого от края.</summary>
        public static float Inset(PixelPanel style) =>
            PixelArt.TryGetDef(PanelSprite(style), out var d) ? d.L * Px : 5 * Px;

        /// <summary>Округлить до сетки пикселей арта (4 единицы).</summary>
        public static float Snap(float units) => Mathf.Round(units / Px) * Px;

        public static Vector2 Snap(Vector2 units) => new Vector2(Snap(units.x), Snap(units.y));

        // ───────────────────────────── Базовые элементы ─────────────────────────────

        /// <summary>Image с 9-slice спрайтом: Sliced, pixelsPerUnitMultiplier = 1 (пиксель арта = 4 единицы).</summary>
        public static Image Sliced(Transform parent, string name, string sprite)
        {
            var img = Ui.Rect(parent, name).gameObject.AddComponent<Image>();
            SetSliced(img, sprite);
            return img;
        }

        /// <summary>Сменить 9-slice спрайт (только если он другой).</summary>
        public static void SetSliced(Image img, string sprite)
        {
            var s = PixelArt.Ui(sprite);
            if (img.sprite != s) img.sprite = s;
            if (img.type != Image.Type.Sliced) img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.fillCenter = true;
        }

        /// <summary>
        /// Панель стиля <paramref name="style"/>. Ловит лучи UI (жест над панелью не уходит на поле); для декоративных
        /// панелей поставьте raycastTarget = false. Размер задаётся якорями (<see cref="Ui.Anchor"/>), минимум — 2 border спрайта.
        /// </summary>
        public static Image Panel(Transform parent, string name, PixelPanel style)
        {
            var img = Sliced(parent, name, PanelSprite(style));
            if (style == PixelPanel.Focus || style == PixelPanel.Dashed) img.raycastTarget = false;
            return img;
        }

        public static void SetPanel(Image img, PixelPanel style) => SetSliced(img, PanelSprite(style));

        /// <summary>
        /// Иконка в целом масштабе: размер = размер спрайта × 4 × <paramref name="scale"/>, якорь по центру родителя.
        /// Pivot берётся из атласа, поэтому центр исходного холста (16×16, 24×24) стоит в anchoredPosition даже у обрезанных
        /// сборкой спрайтов. Двигайте иконку через anchoredPosition/anchorMin=anchorMax; растягивать якорями нельзя.
        /// </summary>
        public static Image Icon(Transform parent, string name, string iconName, int scale = 1)
        {
            var rt = Ui.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            SetIcon(img, iconName, scale);
            return img;
        }

        /// <summary>Сменить спрайт иконки и её размер (только если изменилось); якоря и anchoredPosition сохраняются.</summary>
        public static void SetIcon(Image img, string iconName, int scale = 1)
        {
            var sprite = PixelArt.Ui(iconName);
            if (scale < 1) scale = 1;
            var rt = img.rectTransform;
            Vector2 size, pivot;
            if (PixelArt.TryGetDef(iconName, out var d) && d.W > 0 && d.H > 0)
            {
                size = new Vector2(d.W * Px * scale, d.H * Px * scale);
                pivot = new Vector2(d.PivotX / d.W, d.PivotY / d.H);
            }
            else
            {
                size = new Vector2(16 * Px * scale, 16 * Px * scale);
                pivot = new Vector2(0.5f, 0.5f);
            }
            if (img.sprite != sprite) img.sprite = sprite;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (rt.pivot != pivot) rt.pivot = pivot;
        }

        /// <summary>Размер иконки в единицах Canvas (спрайт × 4 × scale) — для раскладки рядом с текстом.</summary>
        public static Vector2 IconSize(string iconName, int scale = 1) =>
            PixelArt.TryGetDef(iconName, out var d) ? new Vector2(d.W * Px * scale, d.H * Px * scale) : new Vector2(16 * Px * scale, 16 * Px * scale);

        /// <summary>
        /// Текст пиксельным шрифтом: <paramref name="bold"/> — шрифт заголовков, кнопок и чисел (Pixeloid Sans Bold),
        /// <paramref name="shadow"/> — пиксельная тень ink1 на 1 пиксель шрифта вниз (светлый текст на тёмном фоне).
        /// Размеры — <see cref="FontSmall"/>…<see cref="FontHuge"/>. Перенос строк включён, переполнение — многоточие,
        /// лучи UI не ловит.
        /// </summary>
        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = false, bool shadow = false)
        {
            var rt = Ui.Rect(parent, name);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            SetFont(tmp, bold, shadow);
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>Начертание и тень текста кита (без изменений, если уже такие).</summary>
        public static void SetFont(TMP_Text text, bool bold, bool shadow)
        {
            var font = bold ? UiFonts.Bold : UiFonts.Default;
            if (font != null && text.font != font) text.font = font;
            // Пиксельный шрифт искусственно не утолщается: это размывает его пиксели. Жирность — только у запасного шрифта.
            var style = bold && !UiFonts.IsPixel ? FontStyles.Bold : FontStyles.Normal;
            if (text.fontStyle != style) text.fontStyle = style;
            SetShadow(text, shadow);
        }

        /// <summary>Включить или выключить пиксельную тень (общий материал шрифта, батчинг сохраняется).</summary>
        public static void SetShadow(TMP_Text text, bool on)
        {
            var font = text.font;
            if (font == null) return;
            var mat = on ? UiFonts.Shadow(font) : font.material;
            if (mat != null && text.fontSharedMaterial != mat) text.fontSharedMaterial = mat;
        }

        /// <summary>Тонкий горизонтальный разделитель ui_divider высотой 3 пикселя арта (растягивается по ширине якорями).</summary>
        public static Image Divider(Transform parent, string name)
        {
            var img = Sliced(parent, name, "ui_divider");
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(img.rectTransform.sizeDelta.x, 3 * Px);
            return img;
        }

        /// <summary>Полноэкранное затемнение (ink0, alpha 0,62). Ловит лучи UI: под ним поле и HUD не нажимаются.</summary>
        public static Image Shade(Transform parent, string name)
        {
            var rt = Ui.Rect(parent, name).Anchor(0, 0, 1, 1);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = ShadeColor;
            img.raycastTarget = true;
            return img;
        }

        // ───────────────────────────── Кнопки ─────────────────────────────

        /// <summary>
        /// Кнопка с подписью: спрайт ui_btn_&lt;стиль&gt;, при нажатии ui_btn_&lt;стиль&gt;_pressed и подпись на 1 пиксель арта ниже,
        /// недоступная — ui_btn_disabled (ButtonRefs.SetInteractable). Клик — UiSound.PlayClick, тап по недоступной —
        /// UiSound.PlayDenied. Подпись — Bold, по центру лица кнопки (над губой), с автоподбором до 60 % размера.
        /// Высота — от 16 пикселей арта (обычно <see cref="ButtonHeight"/>).
        /// </summary>
        public static Ui.ButtonRefs Button(Transform parent, string name, string label, PixelButton style, UnityAction onClick, float fontSize = FontLarge)
        {
            var img = Sliced(parent, name, ButtonSprite(style));
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.targetGraphic = img;
            // Без навигации кнопка не остаётся «выбранной» после тапа.
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var content = Ui.Rect(img.transform, "content").Anchor(0, 0, 1, 1);
            var text = Text(content, "label", label, fontSize, ButtonTextColor(style), TextAlignmentOptions.Center, true, ButtonTextShadow(style));
            // Губа 2 px снизу: подпись центрируется по лицу кнопки, на 1 px выше середины.
            text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(4 * Px, Px + LipLift * 2), new Vector2(-4 * Px, -Px));
            Ui.AutoSize(text, Mathf.Max(FontSmall * 0.8f, fontSize * 0.6f), fontSize);
            var press = img.gameObject.AddComponent<PixelButtonPress>();
            press.Init(btn, content);
            btn.onClick.AddListener(UiSound.PlayClick);
            if (onClick != null) btn.onClick.AddListener(onClick);
            var refs = new Ui.ButtonRefs { Button = btn, Image = img, Label = text, Content = content };
            refs.Init(style, LegacyColor(style));
            return refs;
        }

        /// <summary>Спрайты кнопки стиля (обычный, нажатый, недоступный) без лишних присваиваний.</summary>
        public static void ApplyButtonSprites(Button btn, Image img, PixelButton style)
        {
            var normal = PixelArt.Ui(ButtonSprite(style));
            if (img.sprite != normal) img.sprite = normal;
            var state = btn.spriteState;
            var pressed = PixelArt.Ui(ButtonPressedSprite(style));
            var disabled = PixelArt.Ui("ui_btn_disabled");
            if (state.pressedSprite != pressed || state.disabledSprite != disabled)
            {
                state.pressedSprite = pressed;
                state.disabledSprite = disabled;
                state.highlightedSprite = null;
                state.selectedSprite = null;
                btn.spriteState = state;
            }
        }

        /// <summary>
        /// Ячейка-кнопка ui_slot (нажатая — ui_slot_pressed) с иконкой по центру лица: для нижней панели HUD берите
        /// icon_*_big и scale 1 (ячейка <see cref="SlotSize"/>); scale 2 — только для ячеек от 240 единиц. Подпись под иконкой (Label, по умолчанию пустая, FontSmall) и красный бейдж-счётчик
        /// в правом верхнем углу (Badge.SetCount / ButtonRefs.SetBadge). Недоступная ячейка — та же рамка, иконка приглушена.
        /// </summary>
        public static Ui.ButtonRefs IconButton(Transform parent, string name, string iconName, UnityAction onClick, int iconScale = 1)
        {
            var img = Sliced(parent, name, "ui_slot");
            img.raycastTarget = true;
            img.rectTransform.sizeDelta = new Vector2(SlotSize, SlotSize);
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var state = btn.spriteState;
            state.pressedSprite = PixelArt.Ui("ui_slot_pressed");
            state.disabledSprite = PixelArt.Ui("ui_slot");
            btn.spriteState = state;
            var content = Ui.Rect(img.transform, "content").Anchor(0, 0, 1, 1);
            var icon = Icon(content, "icon", iconName, iconScale);
            icon.rectTransform.anchoredPosition = new Vector2(0f, LipLift);
            var text = Text(content, "label", "", FontSmall, TextColor, TextAlignmentOptions.Bottom, true, true);
            text.rectTransform.Anchor(0, 0, 1, 0.5f, new Vector2(2 * Px, 3 * Px), new Vector2(-2 * Px, 0));
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var badge = Badge(img.transform, "badge");
            badge.PlaceAtCorner(new Vector2(1f, 1f), BadgeCornerOffset);
            var press = img.gameObject.AddComponent<PixelButtonPress>();
            press.Init(btn, content);
            btn.onClick.AddListener(UiSound.PlayClick);
            if (onClick != null) btn.onClick.AddListener(onClick);
            var refs = new Ui.ButtonRefs { Button = btn, Image = img, Label = text, Content = content, Icon = icon, Badge = badge, IsSlot = true };
            refs.Init(PixelButton.Dark, Palette.UiPanelLight);
            return refs;
        }

        /// <summary>Красный бейдж-счётчик ui_badge_red (9×9 пикселей арта × scale), число — Bold с тенью; скрыт при 0.</summary>
        public static PixelBadge Badge(Transform parent, string name, int scale = 1)
        {
            var img = Icon(parent, name, "ui_badge_red", scale);
            var text = Text(img.transform, "count", "", FontSmall * scale, TextColor, TextAlignmentOptions.Center, true, true);
            text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(-2 * Px, 0), new Vector2(2 * Px, 0));
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            Ui.AutoSize(text, FontSmall * scale * 0.6f, FontSmall * scale);
            var badge = new PixelBadge(img, text);
            img.gameObject.SetActive(false);
            return badge;
        }

        /// <summary>Строка числа для бейджей и счётчиков без выделений памяти: 0…99, дальше «99+».</summary>
        public static string CountText(int n)
        {
            if (n < 0) n = 0;
            return n < s_CountTexts.Length ? s_CountTexts[n] : "99+";
        }

        /// <summary>
        /// Ползунок 0…1 (<see cref="PixelSlider"/>): шкала с золотой заливкой целыми пикселями и ручка ui_slider_knob.
        /// Размер задаёт вызывающий (высота строки — от 14 пикселей арта). onChanged — при изменении игроком.
        /// </summary>
        public static PixelSlider Slider(Transform parent, string name, float value, UnityAction<float> onChanged) =>
            PixelSlider.Create(parent, name, value, onChanged);

        /// <summary>Переключатель ui_toggle_on/off (20×10 пикселей арта × scale). Тап меняет состояние и вызывает onChanged.</summary>
        public static PixelToggle Toggle(Transform parent, string name, bool on, UnityAction<bool> onChanged, int scale = 2)
        {
            var img = Icon(parent, name, on ? "ui_toggle_on" : "ui_toggle_off", scale);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var toggle = new PixelToggle(btn, img, on, scale) { OnChanged = onChanged };
            btn.onClick.AddListener(toggle.Flip);
            return toggle;
        }

        // ───────────────────────────── Составные элементы ─────────────────────────────

        /// <summary>
        /// Шкала: рамка ui_bar_frame + заливка ui_bar_fill_* с отступом 2 пикселя арта. Ширина заливки кратна пикселю арта,
        /// пересчитывается при SetValue и смене размера. Высота — от 8 пикселей арта (32 единицы).
        /// </summary>
        public static PixelBar Bar(Transform parent, string name, PixelBarFill fill = PixelBarFill.Gold)
        {
            var frame = Sliced(parent, name, "ui_bar_frame");
            frame.raycastTarget = false;
            frame.rectTransform.sizeDelta = new Vector2(40 * Px, 8 * Px);
            var fillImg = Sliced(frame.transform, "fill", BarFillSprite(fill));
            fillImg.raycastTarget = false;
            var bar = frame.gameObject.AddComponent<PixelBar>();
            bar.Init(frame, fillImg, fill);
            return bar;
        }

        /// <summary>
        /// Иконка и текст в строку (валюты, цены): иконка слева, текст справа на расстоянии 2 пикселя арта; ширина строки
        /// подстраивается под текст (ContentSizeFitter). Ставьте через якоря/pivot корня: pivot (0, 0.5) — от левого края,
        /// (0.5, 0.5) — по центру.
        /// </summary>
        public static PixelIconText IconText(Transform parent, string name, string iconName, string text, float fontSize, Color color,
            bool bold = true, bool shadow = true, int iconScale = 1)
        {
            var root = Ui.Rect(parent, name);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 2 * Px;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Иконка — в обёртке фиксированного размера: группа управляет обёрткой, пиксельный размер иконки не меняется.
            var size = IconSize(iconName, iconScale);
            var holder = Ui.Rect(root, "iconBox");
            var le = holder.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = size.x;
            le.preferredHeight = le.minHeight = size.y;
            var icon = Icon(holder, "icon", iconName, iconScale);
            var label = Text(root, "text", text, fontSize, color, TextAlignmentOptions.Left, bold, shadow);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            return new PixelIconText(root, icon, label, iconScale);
        }

        /// <summary>
        /// Карточка (чертёж): рамка по редкости ui_card_frame_*, арт-иконка в окне рамки, название снизу, уровень сверху,
        /// шкала копий. Рубашка — SetFaceDown(true). Размер задаёт вызывающий (рамка 24×32 пикселей арта; кратно — 96×128, 192×256).
        /// onClick != null — карточка нажимается (звук клика, без смены спрайта).
        /// </summary>
        public static PixelCard Card(Transform parent, string name, PixelRarity rarity, string artIcon, UnityAction onClick = null)
        {
            var frame = Sliced(parent, name, CardFrameSprite(rarity));
            frame.rectTransform.sizeDelta = new Vector2(48 * Px, 64 * Px);
            frame.raycastTarget = onClick != null;
            Button btn = null;
            if (onClick != null)
            {
                btn = frame.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.targetGraphic = frame;
                btn.onClick.AddListener(UiSound.PlayClick);
                btn.onClick.AddListener(onClick);
            }
            var art = Icon(frame.transform, "art", artIcon, 2);
            art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0.5f, 0.6f);
            var level = Text(frame.transform, "level", "", FontSmall, TextColor, TextAlignmentOptions.TopLeft, true, true);
            level.rectTransform.Anchor(0, 0.8f, 1, 1, new Vector2(4 * Px, 0), new Vector2(-4 * Px, -3 * Px));
            level.textWrappingMode = TextWrappingModes.NoWrap;
            var title = Text(frame.transform, "title", "", FontSmall, TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 0.12f, 1, 0.3f, new Vector2(4 * Px, 0), new Vector2(-4 * Px, 0));
            Ui.AutoSize(title, FontSmall * 0.7f, FontSmall);
            var bar = Bar(frame.transform, "progress", PixelBarFill.Purple);
            bar.Rect.Anchor(0, 0, 1, 0, new Vector2(4 * Px, 3 * Px), new Vector2(-4 * Px, 11 * Px));
            var back = Icon(frame.transform, "paw", PixelArt.Has("ui_card_paw") ? "ui_card_paw" : "icon_paw", 2);
            back.gameObject.SetActive(false);
            return new PixelCard(frame, btn, art, title, level, bar, back, rarity);
        }

        /// <summary>
        /// Модальное окно: затемнение на весь родитель (ловит лучи), рамка ui_window, планка-заголовок ui_window_header,
        /// область содержимого Body с текстом BodyText, ряд кнопок Buttons внизу и (если onClose != null) тёмная кнопка
        /// закрытия с icon_close в правом верхнем углу. Создаётся скрытым; Show/Hide со звуками UiSound.Open/Close,
        /// появление — 0,15 с (масштаб 0,9 → 1). Размер рамки — Box.Anchor(…) (по умолчанию 88 % ширины, половина высоты).
        /// </summary>
        public static PixelWindow Window(Transform parent, string name, string title, UnityAction onClose = null)
        {
            var root = Ui.Rect(parent, name).Anchor(0, 0, 1, 1);
            var shadeImg = root.gameObject.AddComponent<Image>();
            shadeImg.color = ShadeColor;
            shadeImg.raycastTarget = true;

            var frame = Panel(root, "Box", PixelPanel.Window);
            var box = frame.rectTransform.Anchor(0.06f, 0.25f, 0.94f, 0.75f);
            box.gameObject.AddComponent<PixelPop>();

            var header = Panel(box, "header", PixelPanel.Header);
            header.raycastTarget = false;
            header.rectTransform.Anchor(0.14f, 1f, 0.86f, 1f, new Vector2(0, -8 * Px), new Vector2(0, 10 * Px));
            var titleText = Text(header.transform, "title", title, FontLarge, TextColor, TextAlignmentOptions.Center, true, true);
            titleText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(8 * Px, 2 * Px), new Vector2(-8 * Px, -Px));
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(titleText, FontBody * 0.8f, FontLarge);

            var body = Ui.Rect(box, "body");
            var bodyText = Text(body, "text", "", FontBody, TextColor, TextAlignmentOptions.Top, false, true);
            bodyText.rectTransform.Anchor(0, 0, 1, 1);
            Ui.AutoSize(bodyText, FontSmall, FontBody);

            var buttons = Ui.Rect(box, "buttons").Anchor(0, 0, 1, 0, new Vector2(12 * Px, 11 * Px), new Vector2(-12 * Px, 11 * Px + ButtonHeight));
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 6 * Px;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;

            Ui.ButtonRefs close = null;
            if (onClose != null)
            {
                close = Button(box, "close", "", PixelButton.Dark, onClose, FontBody);
                var crt = close.Rect;
                crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
                crt.pivot = new Vector2(1f, 1f);
                crt.sizeDelta = new Vector2(24 * Px, 24 * Px);
                crt.anchoredPosition = new Vector2(2 * Px, 4 * Px);
                close.SetIcon("icon_close");
                close.Icon.rectTransform.anchoredPosition = new Vector2(0f, LipLift);
            }

            var window = new PixelWindow(root, shadeImg, frame, header, titleText, body, bodyText, buttons, close);
            window.UpdateBodyArea();
            root.gameObject.SetActive(false);
            return window;
        }

        static string[] BuildCounts(int max)
        {
            var texts = new string[max + 1];
            for (int i = 0; i <= max; i++) texts[i] = i.ToString(CultureInfo.InvariantCulture);
            return texts;
        }
    }

    /// <summary>Красный бейдж (<see cref="PixelUi.Badge"/>): счётчик, короткий текст или пин без подписи.</summary>
    public sealed class PixelBadge
    {
        public readonly Image Image;
        public readonly TextMeshProUGUI Label;
        int m_Shown = int.MinValue;

        public PixelBadge(Image image, TextMeshProUGUI label)
        {
            Image = image;
            Label = label;
        }

        public RectTransform Rect => Image.rectTransform;
        public bool Visible => Image.gameObject.activeSelf;

        /// <summary>Число (0 и меньше — бейдж скрыт; больше 99 — «99+»).</summary>
        public void SetCount(int n)
        {
            if (n == m_Shown) return;
            m_Shown = n;
            Ui.SetActive(Image, n > 0);
            Ui.SetActive(Label, true);
            if (n > 0) Ui.SetText(Label, PixelUi.CountText(n));
        }

        /// <summary>Произвольный короткий текст («!»); null или пустая строка — скрыть.</summary>
        public void SetText(string text)
        {
            m_Shown = int.MinValue;
            bool on = !string.IsNullOrEmpty(text);
            Ui.SetActive(Image, on);
            Ui.SetActive(Label, true);
            if (on) Ui.SetText(Label, text);
        }

        /// <summary>Красный пин без числа или текста; false — скрыть.</summary>
        public void SetDot(bool on)
        {
            m_Shown = int.MinValue;
            Ui.SetActive(Image, on);
            Ui.SetActive(Label, false);
        }

        /// <summary>Поставить центр бейджа в угол родителя (corner — (0|1, 0|1)), сдвинув внутрь на inset единиц.</summary>
        public void PlaceAtCorner(Vector2 corner, float inset)
        {
            var rt = Rect;
            rt.anchorMin = rt.anchorMax = corner;
            rt.anchoredPosition = new Vector2(corner.x > 0.5f ? -inset : inset, corner.y > 0.5f ? -inset : inset);
        }
    }

    /// <summary>Переключатель (<see cref="PixelUi.Toggle"/>).</summary>
    public sealed class PixelToggle
    {
        public readonly Button Button;
        public readonly Image Image;
        /// <summary>Вызывается после тапа с новым состоянием (SetOn его не вызывает).</summary>
        public UnityAction<bool> OnChanged;
        readonly int m_Scale;

        public PixelToggle(Button button, Image image, bool on, int scale)
        {
            Button = button;
            Image = image;
            IsOn = on;
            m_Scale = scale;
        }

        public bool IsOn { get; private set; }
        public RectTransform Rect => Image.rectTransform;

        /// <summary>Показать состояние без вызова OnChanged (например, из сохранения).</summary>
        public void SetOn(bool on)
        {
            if (on == IsOn) return;
            IsOn = on;
            PixelUi.SetIcon(Image, on ? "ui_toggle_on" : "ui_toggle_off", m_Scale);
        }

        internal void Flip()
        {
            SetOn(!IsOn);
            // Сначала настройка, потом щелчок: включённый звук подтверждает себя щелчком, выключенный молчит.
            OnChanged?.Invoke(IsOn);
            UiSound.PlayClick();
        }
    }

    /// <summary>Иконка и текст в строку (<see cref="PixelUi.IconText"/>).</summary>
    public sealed class PixelIconText
    {
        public readonly RectTransform Root;
        public readonly Image Icon;
        public readonly TextMeshProUGUI Text;
        readonly int m_Scale;

        public PixelIconText(RectTransform root, Image icon, TextMeshProUGUI text, int scale)
        {
            Root = root;
            Icon = icon;
            Text = text;
            m_Scale = scale;
        }

        public void SetText(string value) => Ui.SetText(Text, value);

        public void SetIcon(string iconName) => PixelUi.SetIcon(Icon, iconName, m_Scale);
    }

    /// <summary>Карточка (<see cref="PixelUi.Card"/>). Сеттеры меняют элементы только при смене значения.</summary>
    public sealed class PixelCard
    {
        public readonly Image Frame;
        /// <summary>Нажатие (если карточка создана с onClick), иначе null.</summary>
        public readonly Button Button;
        public readonly Image Art;
        public readonly TextMeshProUGUI Title;
        public readonly TextMeshProUGUI Level;
        public readonly PixelBar Progress;
        readonly Image m_BackPaw;
        PixelRarity m_Rarity;
        bool m_FaceDown;

        public PixelCard(Image frame, Button button, Image art, TextMeshProUGUI title, TextMeshProUGUI level, PixelBar progress, Image backPaw, PixelRarity rarity)
        {
            Frame = frame;
            Button = button;
            Art = art;
            Title = title;
            Level = level;
            Progress = progress;
            m_BackPaw = backPaw;
            m_Rarity = rarity;
        }

        public RectTransform Rect => Frame.rectTransform;
        public PixelRarity Rarity => m_Rarity;
        public bool FaceDown => m_FaceDown;

        public void SetRarity(PixelRarity rarity)
        {
            if (rarity == m_Rarity) return;
            m_Rarity = rarity;
            if (!m_FaceDown) PixelUi.SetSliced(Frame, PixelUi.CardFrameSprite(rarity));
        }

        public void SetArt(string iconName, int scale = 2) => PixelUi.SetIcon(Art, iconName, scale);
        public void SetTitle(string text) => Ui.SetText(Title, text);
        public void SetLevel(string text) => Ui.SetText(Level, text);
        /// <summary>Шкала копий 0…1; отрицательное значение скрывает шкалу.</summary>
        public void SetProgress(float value)
        {
            Ui.SetActive(Progress, value >= 0f && !m_FaceDown);
            if (value >= 0f) Progress.SetValue(value);
        }

        /// <summary>Рубашка (ui_card_back_plain + лапка) вместо лицевой стороны.</summary>
        public void SetFaceDown(bool down)
        {
            if (down == m_FaceDown) return;
            m_FaceDown = down;
            PixelUi.SetSliced(Frame, down ? (PixelArt.Has("ui_card_back_plain") ? "ui_card_back_plain" : "ui_card_back") : PixelUi.CardFrameSprite(m_Rarity));
            Ui.SetActive(Art, !down);
            Ui.SetActive(Title, !down);
            Ui.SetActive(Level, !down);
            Ui.SetActive(m_BackPaw, down);
            if (down) Ui.SetActive(Progress, false);
        }
    }

    /// <summary>
    /// Модальное окно кита (<see cref="PixelUi.Window"/>). Root — затемнение на весь родитель, Box — рамка (её якоря задают
    /// размер), Body — область содержимого (свои элементы — сюда, BodyText можно скрыть), Buttons — ряд кнопок.
    /// </summary>
    public sealed class PixelWindow
    {
        public readonly RectTransform Root;
        public readonly Image Shade;
        public readonly Image Frame;
        public readonly Image Header;
        public readonly TextMeshProUGUI Title;
        public readonly RectTransform Body;
        public readonly TextMeshProUGUI BodyText;
        public readonly RectTransform Buttons;
        /// <summary>Кнопка закрытия (если окно создано с onClose), иначе null.</summary>
        public readonly Ui.ButtonRefs Close;
        bool m_HasButtons;
        float m_ButtonsHeight = PixelUi.ButtonHeight;

        public PixelWindow(RectTransform root, Image shade, Image frame, Image header, TextMeshProUGUI title, RectTransform body,
            TextMeshProUGUI bodyText, RectTransform buttons, Ui.ButtonRefs close)
        {
            Root = root;
            Shade = shade;
            Frame = frame;
            Header = header;
            Title = title;
            Body = body;
            BodyText = bodyText;
            Buttons = buttons;
            Close = close;
        }

        /// <summary>Рамка окна: задайте её размер якорями, например Box.Anchor(0.06f, 0.28f, 0.94f, 0.72f).</summary>
        public RectTransform Box => Frame.rectTransform;
        public bool Visible => Root.gameObject.activeSelf;

        /// <summary>Показать поверх соседей (последним в родителе), с появлением и звуком открытия.</summary>
        public void Show()
        {
            Root.SetAsLastSibling();
            if (Root.gameObject.activeSelf) return;
            Root.gameObject.SetActive(true);
            UiSound.PlayOpen();
        }

        /// <summary>Скрыть со звуком закрытия (если окно было показано).</summary>
        public void Hide()
        {
            if (!Root.gameObject.activeSelf) return;
            Root.gameObject.SetActive(false);
            UiSound.PlayClose();
        }

        public void SetTitle(string text) => Ui.SetText(Title, text);
        public void SetBody(string text) => Ui.SetText(BodyText, text);

        /// <summary>Кнопка в ряд Buttons (ряд делит ширину поровну). Имя — для поиска в тестах (окна HUD используют «btn»).</summary>
        public Ui.ButtonRefs AddButton(string name, string label, PixelButton style, UnityAction onClick, float fontSize = PixelUi.FontLarge)
        {
            var refs = PixelUi.Button(Buttons, name, label, style, onClick, fontSize);
            UpdateBodyArea();
            return refs;
        }

        /// <summary>Удалить кнопки ряда (Destroy в конце кадра). Для частых окон лучше переиспользовать кнопки через SetActive.</summary>
        public void ClearButtons()
        {
            for (int i = Buttons.childCount - 1; i >= 0; i--)
            {
                var child = Buttons.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            UpdateBodyArea();
        }

        /// <summary>
        /// Высота ряда кнопок (по умолчанию <see cref="PixelUi.ButtonHeight"/>): выше — для подписи в две строки с иконкой.
        /// Область Body пересчитывается сразу.
        /// </summary>
        public float ButtonsHeight
        {
            get => m_ButtonsHeight;
            set
            {
                if (Mathf.Approximately(m_ButtonsHeight, value)) return;
                m_ButtonsHeight = value;
                Buttons.offsetMax = new Vector2(Buttons.offsetMax.x, Buttons.offsetMin.y + value);
                ApplyBodyArea();
            }
        }

        /// <summary>
        /// Пересчитать область Body: под заголовком, над рядом кнопок, если в нём есть активные кнопки.
        /// Вызывайте после ручного включения или выключения кнопок ряда.
        /// </summary>
        public void UpdateBodyArea()
        {
            bool any = false;
            for (int i = 0; i < Buttons.childCount; i++)
                if (Buttons.GetChild(i).gameObject.activeSelf) any = true;
            if (any == m_HasButtons && Body.anchorMax == Vector2.one) return;
            m_HasButtons = any;
            ApplyBodyArea();
        }

        void ApplyBodyArea()
        {
            const float px = PixelUi.Px;
            float bottom = m_HasButtons ? 11 * px + m_ButtonsHeight + 4 * px : 12 * px;
            Body.Anchor(0, 0, 1, 1, new Vector2(12 * px, bottom), new Vector2(-12 * px, -14 * px));
        }
    }
}
