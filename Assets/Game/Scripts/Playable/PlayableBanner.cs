using CatMiners.Core.Text;
using CatMiners.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CatMiners.Playable
{
    /// <summary>
    /// Постоянный баннер CTA в резерве под нижней панелью HUD (ТЗ playable §3.5, reference.md §5): деревянная табличка кита,
    /// слева «иконка игры» — котик icon_cat_big в ячейке ui_slot, рядом «CAT MINERS» (FontTitle, не помещается — FontLarge) и
    /// пять звёзд icon_star (у Pixeloid нет ★), справа зелёная «PLAY FREE» (магазин — зелёный, как DOWNLOAD у референса:
    /// золотые кнопки — навигация игры) с пульсом и искрой (<see cref="PlayableShine"/>). Нажимаются только «PLAY FREE» и
    /// иконка игры: случайный тап по табличке под кнопками HUD магазин не открывает; от нижней панели HUD — зазор 4 пикселя
    /// арта. Поля внутри таблички — больше её рамки. Нажатие взводит переход в жесте браузера (<see cref="PlayableStoreTap"/>),
    /// клик открывает магазин. Въезжает снизу за <see cref="SlideSeconds"/>; пока открыто окно, притушен
    /// (<see cref="SetDimmed"/>). Только кит PixelUi; высота — резерв HUD (<see cref="PlayableMode.BannerHeight"/>).
    /// </summary>
    public sealed class PlayableBanner
    {
        const float Px = PixelUi.Px;
        /// <summary>Поля таблички от краёв safe area и снизу.</summary>
        const float Margin = 2 * Px;
        /// <summary>Зазор до нижней панели HUD сверх её собственного поля (2 пикселя арта): всего 4.</summary>
        public const float TopGap = 2 * Px;
        /// <summary>
        /// Поля внутри таблички: по бокам 7 пикселей арта (рамка 6 — кнопка и ячейка не ложатся на заклёпки), сверху и снизу 4;
        /// «PLAY FREE» ещё на 2 пикселя ниже и выше (<see cref="PlayInset"/>) — не касается внутренней линии рамки.
        /// </summary>
        const float PadX = 7 * Px, PadY = 4 * Px, Gap = 3 * Px, StarGap = Px, PlayInset = 2 * Px;
        const float ButtonWidth = 80 * Px;
        public const int Stars = 5;
        public const float SlideSeconds = 0.3f;

        public readonly RectTransform Root;
        /// <summary>Табличка (её прямоугольник — видимая область баннера; лучи ловит, но магазин не открывает).</summary>
        public readonly RectTransform Panel;
        /// <summary>Нажатие на «иконку игры» (ячейка с котиком).</summary>
        public readonly Button Tap;
        /// <summary>Зелёная кнопка «PLAY FREE».</summary>
        public readonly Ui.ButtonRefs Play;
        public readonly TextMeshProUGUI Title;
        public readonly PlayableShine Shine;
        readonly Image m_Dim;
        readonly Vector2 m_Home;
        readonly float m_Height;
        float m_Slide = 1f;

        public bool Visible => Root.gameObject.activeSelf;
        /// <summary>Баннер на месте (въезд закончен).</summary>
        public bool Settled => Visible && m_Slide >= 1f;
        /// <summary>Баннер притушен (открыто окно HUD или окно награды квеста).</summary>
        public bool Dimmed => m_Dim.gameObject.activeSelf;

        /// <summary>Баннер у нижнего края <paramref name="safe"/> высотой <paramref name="height"/> единиц Canvas; скрыт до <see cref="Show"/>.</summary>
        public PlayableBanner(RectTransform safe, float height, UnityAction onTap)
        {
            m_Height = height;
            Root = Ui.Rect(safe, "CtaBanner").Anchor(0, 0, 1, 0, new Vector2(Margin, Margin), new Vector2(-Margin, height - TopGap));
            m_Home = Root.anchoredPosition;

            var panel = PixelUi.Panel(Root, "panel", PixelPanel.Wood);
            // Табличка ловит лучи (тап не уходит под неё), но не нажимается: магазин — только по кнопке и иконке игры.
            panel.raycastTarget = true;
            Panel = panel.rectTransform.Anchor(0, 0, 1, 1);
            float inner = height - Margin - TopGap - 2 * PadY;

            // «Иконка игры»: ячейка во всю внутреннюю высоту таблички, нажимается.
            var icon = PixelUi.Panel(panel.transform, "appIcon", PixelPanel.Slot);
            icon.raycastTarget = true;
            icon.rectTransform.Anchor(0, 0.5f, 0, 0.5f, new Vector2(PadX, -inner / 2), new Vector2(PadX + inner, inner / 2));
            Tap = icon.gameObject.AddComponent<Button>();
            Tap.transition = Selectable.Transition.None;
            Tap.targetGraphic = icon;
            Tap.navigation = new Navigation { mode = Navigation.Mode.None };
            Tap.onClick.AddListener(UiSound.PlayClick);
            if (onTap != null) Tap.onClick.AddListener(onTap);
            icon.gameObject.AddComponent<PlayableStoreTap>();
            var cat = PixelUi.Icon(icon.transform, "cat", "icon_cat_big");
            cat.rectTransform.anchoredPosition = CenterOffset("icon_cat_big", 1);

            // Название над рядом звёзд, слева направо от иконки до кнопки.
            float left = PadX + inner + Gap, right = PadX + ButtonWidth + Gap;
            var starSize = PixelUi.IconSize("icon_star");
            Title = PixelUi.Text(panel.transform, "title", Loc.T("playable.cta.title"), PixelUi.FontTitle, PixelUi.TextAccentLight,
                TextAlignmentOptions.BottomLeft, true, true);
            Title.rectTransform.Anchor(0, 0, 1, 1, new Vector2(left, PadY + starSize.y + StarGap), new Vector2(-right, -PadY));
            Title.textWrappingMode = TextWrappingModes.NoWrap;
            Title.overflowMode = TextOverflowModes.Overflow;
            FitTitle(inner - starSize.y - StarGap);
            var stars = Ui.Rect(panel.transform, "stars").Anchor(0, 0, 0, 0, new Vector2(left, PadY),
                new Vector2(left + Stars * starSize.x + (Stars - 1) * StarGap, PadY + starSize.y));
            for (int i = 0; i < Stars; i++)
            {
                var star = PixelUi.Icon(stars, "star", "icon_star");
                star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                star.rectTransform.anchoredPosition = new Vector2(i * (starSize.x + StarGap) + starSize.x * 0.5f, 0f) + CenterOffset("icon_star", 1);
            }

            Play = PixelUi.Button(panel.transform, "play", Loc.T("playable.cta.play"), PixelButton.Green, onTap);
            // На 4 пикселя арта ниже внутренней высоты, по центру.
            Play.Rect.Anchor(1, 0.5f, 1, 0.5f, new Vector2(-(PadX + ButtonWidth), -(inner / 2 - PlayInset)), new Vector2(-PadX, inner / 2 - PlayInset));
            Play.Rect.gameObject.AddComponent<PlayableStoreTap>();
            Shine = PlayableShine.Attach(Play);

            // Притушить поверх всего содержимого таблички (лучи не ловит: кнопка по-прежнему нажимается).
            m_Dim = PixelUi.Shade(Root, "dim");
            m_Dim.raycastTarget = false;
            m_Dim.rectTransform.Anchor(0, 0, 1, 1);
            m_Dim.gameObject.SetActive(false);

            Root.gameObject.SetActive(false);
        }

        /// <summary>«CAT MINERS» — FontTitle, если строка помещается по высоте, иначе FontLarge (размеры только из таблицы кита).</summary>
        void FitTitle(float rowHeight)
        {
            Title.fontSize = PixelUi.FontTitle;
            if (Title.GetPreferredValues(Title.text).y > rowHeight + 2 * Px) Title.fontSize = PixelUi.FontLarge;
        }

        /// <summary>Показать: въезд снизу (<paramref name="slide"/>) или сразу на месте.</summary>
        public void Show(bool slide = true)
        {
            m_Slide = slide ? 0f : 1f;
            Place();
            Ui.SetActive(Root, true);
        }

        public void Hide() => Ui.SetActive(Root, false);

        /// <summary>Притушить (окно HUD или окно награды квеста открыто): затемнение поверх, без пульса и искры.</summary>
        public void SetDimmed(bool on)
        {
            if (m_Dim.gameObject.activeSelf == on) return;
            m_Dim.gameObject.SetActive(on);
            Shine.enabled = !on;
        }

        /// <summary>Кадр въезда (реальное время), по целым пикселям арта.</summary>
        public void Tick(float dt)
        {
            if (m_Slide >= 1f || !Visible) return;
            m_Slide = Mathf.Min(1f, m_Slide + dt / SlideSeconds);
            Place();
        }

        void Place()
        {
            float k = 1f - (1f - m_Slide) * (1f - m_Slide);
            Root.anchoredPosition = m_Home + new Vector2(0f, PixelUi.Snap(-(1f - k) * (m_Height + Margin)));
        }

        /// <summary>Сдвиг pivot спрайта, при котором центр его обрезанного прямоугольника стоит в точке привязки.</summary>
        internal static Vector2 CenterOffset(string sprite, int scale) =>
            PixelArt.TryGetDef(sprite, out var d) ? new Vector2((d.PivotX - d.W * 0.5f) * Px * scale, (d.PivotY - d.H * 0.5f) * Px * scale) : Vector2.zero;
    }
}
