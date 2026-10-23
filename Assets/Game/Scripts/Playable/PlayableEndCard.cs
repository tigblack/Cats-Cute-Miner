using System.Numerics;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SafeArea = CatMiners.Runtime.SafeArea;
using Vector2 = UnityEngine.Vector2;

namespace CatMiners.Playable
{
    /// <summary>
    /// Финальный экран playable (ТЗ playable §3.5, reference.md §5): сплошной тёмный фон на весь экран (ink0 без прозрачности —
    /// поле, HUD и тост под ним не видны), окно кита по центру safe area. Сверху — знак игры, котик icon_cat_big ×3 в ячейке
    /// ui_slot на тёплом свечении fx_glow_warm на шаг крупнее котика (кольца видны вокруг ячейки; над ячейкой — место под
    /// свечение внутри рамки окна), «CAT MINERS» (FontTitle) и «You cleared N mines!»; итоги — сетка 2×2 плиток (иконка ×2,
    /// число FontTitle, подпись FontBody — не помещается по ширине, FontSmall: шахты, котики, золото за сессию, карточки X/61);
    /// ряд четырёх слотов экипировки — надетый предмет icon_item_*_big или замок; закрытый контент
    /// «144 mines • 61 cards • legendary gear»; большая зелёная «PLAY FREE» (магазин — зелёный, как DOWNLOAD у референса; пульс
    /// и искра), ниже тёмные «Keep playing» и «Play again». Без автоперехода в магазин. Высота окна — по содержимому; не
    /// помещается в 96 % safe area — раскладка мельче (котик ×2, плитки ниже, иконки ×1), затем ещё мельче (без ряда
    /// экипировки). Раскладка — при показе и при смене размера safe area (<see cref="Relayout"/>).
    /// </summary>
    public sealed class PlayableEndCard
    {
        const float Px = PixelUi.Px;
        const float SideMargin = 0.06f, MaxBoxWidth = 238 * Px;
        const float BoxTop = 12 * Px, BoxBottom = 12 * Px, BoxSide = 12 * Px;
        const float TitleHeight = 14 * Px, LineHeight = 12 * Px, TileGap = 3 * Px, GearGap = 6 * Px, TeaserHeight = 20 * Px;
        const float SecondaryGap = 4 * Px, SectionGap = 5 * Px;
        /// <summary>Свечение знака игры начинается на столько ниже верха окна (внутри рамки ui_window 10 пикселей).</summary>
        const float GlowTop = 4 * Px;
        /// <summary>Фон экрана — сплошной: 0,94 в линейном цвете затемняло лишь на ~84 %, верхняя полоса HUD читалась.</summary>
        public const float ShadeAlpha = 1f;

        /// <summary>Итоги отрезка для экрана.</summary>
        public struct Stats
        {
            public int Mines;
            public int Cats;
            public BigInteger Gold;
            public int Cards;
            public int CardsTotal;
            /// <summary>Шахт в полной игре (закрытый контент).</summary>
            public int MinesTotal;
            /// <summary>Надетое по слотам (Pickaxe, Hat, Backpack, Boots): иконка icon_item_*_big или null — пустой слот.</summary>
            public string[] Gear;
        }

        /// <summary>Раскладка по высоте экрана: 0 — полная, 1 — мельче, 2 — без ряда экипировки.</summary>
        struct Size
        {
            public int Hero, TileIcon;
            public float Tile, Gear, Play;
            public bool ShowGear;
        }

        static readonly Size[] s_Sizes =
        {
            new Size { Hero = 3, TileIcon = 2, Tile = 40 * Px, Gear = 36 * Px, Play = 36 * Px, ShowGear = true },
            new Size { Hero = 2, TileIcon = 1, Tile = 30 * Px, Gear = 30 * Px, Play = 32 * Px, ShowGear = true },
            new Size { Hero = 1, TileIcon = 1, Tile = 24 * Px, Gear = 0f, Play = 30 * Px, ShowGear = false },
        };

        sealed class Tile
        {
            public Image Panel, Icon;
            public TextMeshProUGUI Value, Label;
            public string IconName;
        }

        sealed class GearSlot
        {
            public Image Panel, Icon;
            public string IconName;
        }

        public readonly RectTransform Root;
        public readonly Image Shade;
        readonly RectTransform m_Safe, m_Box;
        readonly Image m_Glow, m_HeroSlot, m_Cat;
        readonly Tile[] m_Tiles = new Tile[4];
        readonly GearSlot[] m_Gear = new GearSlot[4];
        readonly string[] m_GearIcons = new string[4];
        readonly RectTransform m_GearRow;
        readonly Image m_TeaserPanel, m_Lock;
        public readonly TextMeshProUGUI Title, Line, Teaser;
        public readonly Ui.ButtonRefs Play, Keep, Again;
        public readonly PlayableShine Shine;
        Vector2 m_LayoutSize;
        int m_Level;

        public bool Visible => Root.gameObject.activeSelf;
        /// <summary>Окно с содержимым (рамка ui_window).</summary>
        public RectTransform Box => m_Box;
        /// <summary>Знак игры: ячейка с котиком (иконка icon_cat_big).</summary>
        public Image Hero => m_Cat;
        /// <summary>Раскладка по высоте экрана (0 — полная).</summary>
        public int Level => m_Level;

        /// <summary>Экран на весь <paramref name="canvasRoot"/> (поверх баннера), содержимое — в своей safe area; скрыт до <see cref="Show"/>.</summary>
        public PlayableEndCard(RectTransform canvasRoot, UnityAction onPlay, UnityAction onKeep, UnityAction onAgain)
        {
            Root = Ui.Rect(canvasRoot, "EndCard").Anchor(0, 0, 1, 1);
            Shade = PixelUi.Shade(Root, "shade");
            Shade.color = new Color(PixelPalette.Ink0.r, PixelPalette.Ink0.g, PixelPalette.Ink0.b, ShadeAlpha);
            m_Safe = Ui.Rect(Root, "SafeArea").Anchor(0, 0, 1, 1);
            m_Safe.gameObject.AddComponent<SafeArea>();

            var box = PixelUi.Panel(m_Safe, "Box", PixelPanel.Window);
            m_Box = box.rectTransform;
            m_Box.anchorMin = m_Box.anchorMax = new Vector2(0.5f, 0.5f);
            box.gameObject.AddComponent<PixelPop>();

            // Знак игры: свечение позади ячейки, котик по центру ячейки.
            m_Glow = PixelUi.Icon(m_Box, "glow", "fx_glow_warm", 3);
            m_Glow.rectTransform.anchorMin = m_Glow.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            m_HeroSlot = PixelUi.Panel(m_Box, "hero", PixelPanel.Slot);
            m_HeroSlot.raycastTarget = false;
            m_Cat = PixelUi.Icon(m_HeroSlot.transform, "cat", "icon_cat_big", 3);

            Title = PixelUi.Text(m_Box, "title", Loc.T("playable.cta.title"), PixelUi.FontTitle, PixelUi.TextAccent, TextAlignmentOptions.Center, true, true);
            Title.textWrappingMode = TextWrappingModes.NoWrap;
            Title.overflowMode = TextOverflowModes.Overflow;
            Line = PixelUi.Text(m_Box, "line", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            Line.textWrappingMode = TextWrappingModes.NoWrap;

            string[] icons = { "icon_exit", "icon_cat", "icon_coin", "icon_cards" };
            string[] labels = { "playable.end.stat.mines", "playable.end.stat.cats", "playable.end.stat.gold", "playable.end.stat.cards" };
            for (int i = 0; i < m_Tiles.Length; i++) m_Tiles[i] = CreateTile(m_Box, icons[i], Loc.T(labels[i]));

            m_GearRow = Ui.Rect(m_Box, "gear");
            for (int i = 0; i < m_Gear.Length; i++)
            {
                var slot = new GearSlot { Panel = PixelUi.Panel(m_GearRow, "slot", PixelPanel.Slot) };
                slot.Panel.raycastTarget = false;
                slot.Panel.rectTransform.anchorMin = slot.Panel.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                slot.Icon = PixelUi.Icon(slot.Panel.transform, "icon", "icon_lock");
                m_Gear[i] = slot;
            }

            m_TeaserPanel = PixelUi.Panel(m_Box, "teaser", PixelPanel.Pill);
            m_TeaserPanel.raycastTarget = false;
            m_Lock = PixelUi.Icon(m_TeaserPanel.transform, "lock", "icon_lock");
            m_Lock.rectTransform.anchorMin = m_Lock.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_Lock.rectTransform.anchoredPosition = new Vector2(6 * Px + PixelUi.IconSize("icon_lock").x * 0.5f, 0f) + PlayableBanner.CenterOffset("icon_lock", 1);
            Teaser = PixelUi.Text(m_TeaserPanel.transform, "text", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            Teaser.rectTransform.Anchor(0, 0, 1, 1, new Vector2(6 * Px + PixelUi.IconSize("icon_lock").x + 2 * Px, Px), new Vector2(-6 * Px, 0f));
            Teaser.textWrappingMode = TextWrappingModes.NoWrap;

            Play = PixelUi.Button(m_Box, "play", Loc.T("playable.cta.play"), PixelButton.Green, onPlay, PixelUi.FontTitle);
            Play.Rect.gameObject.AddComponent<PlayableStoreTap>();
            Shine = PlayableShine.Attach(Play, 2);
            Keep = PixelUi.Button(m_Box, "keep", Loc.T("playable.end.keep"), PixelButton.Dark, onKeep);
            Again = PixelUi.Button(m_Box, "again", Loc.T("playable.end.again"), PixelButton.Dark, onAgain);

            Root.gameObject.SetActive(false);
        }

        /// <summary>Плитка итога: панель Inset, иконка слева, справа число (FontTitle) и под ним подпись (FontSmall).</summary>
        static Tile CreateTile(RectTransform parent, string icon, string label)
        {
            var tile = new Tile { Panel = PixelUi.Panel(parent, "tile", PixelPanel.Inset), IconName = icon };
            tile.Panel.raycastTarget = false;
            tile.Icon = PixelUi.Icon(tile.Panel.transform, "icon", icon, 2);
            tile.Icon.rectTransform.anchorMin = tile.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            tile.Value = PixelUi.Text(tile.Panel.transform, "value", "", PixelUi.FontTitle, PixelUi.TextAccent, TextAlignmentOptions.Left, true, true);
            tile.Value.textWrappingMode = TextWrappingModes.NoWrap;
            tile.Label = PixelUi.Text(tile.Panel.transform, "label", label, PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Left, false, true);
            tile.Label.textWrappingMode = TextWrappingModes.NoWrap;
            return tile;
        }

        /// <summary>Показать с итогами <paramref name="stats"/> (поверх всего, с появлением и звуком открытия кита).</summary>
        public void Show(Stats stats)
        {
            Ui.SetText(Line, stats.Mines > 0 ? Loc.Plural("playable.end.line", stats.Mines) : Loc.T("playable.end.line.none"));
            Ui.SetText(m_Tiles[0].Value, stats.Mines.ToString());
            Ui.SetText(m_Tiles[1].Value, stats.Cats.ToString());
            Ui.SetText(m_Tiles[2].Value, OutpostUiText.Money(stats.Gold));
            Ui.SetText(m_Tiles[3].Value, Loc.F("playable.end.of", stats.Cards, stats.CardsTotal));
            Ui.SetText(Teaser, Loc.F("playable.end.teaser", stats.MinesTotal, stats.CardsTotal));
            for (int i = 0; i < m_GearIcons.Length; i++)
            {
                string icon = stats.Gear != null && i < stats.Gear.Length ? stats.Gear[i] : null;
                m_GearIcons[i] = icon != null && PixelArt.Has(icon) ? icon : null;
            }
            Root.SetAsLastSibling();
            m_LayoutSize = Vector2.zero;
            if (Visible)
            {
                Layout();
                return;
            }
            Root.gameObject.SetActive(true);
            Layout();
            UiSound.PlayOpen();
        }

        public void Hide()
        {
            if (!Visible) return;
            Root.gameObject.SetActive(false);
            UiSound.PlayClose();
        }

        /// <summary>Значение плитки итогов (0 — шахты, 1 — котики, 2 — золото, 3 — карточки).</summary>
        public string StatValue(int index) => m_Tiles[index].Value.text;

        /// <summary>Иконка слота экипировки (0 — кирка … 3 — сапоги): предмет или icon_lock.</summary>
        public string GearIcon(int index) => m_Gear[index].IconName;

        /// <summary>Ряд экипировки виден (в самой мелкой раскладке его нет).</summary>
        public bool GearVisible => m_GearRow.gameObject.activeSelf;

        /// <summary>Повторить раскладку, если размер safe area изменился (поворот, окно браузера).</summary>
        public void Relayout()
        {
            if (Visible && m_Safe.rect.size != m_LayoutSize) Layout();
        }

        /// <summary>
        /// Раскладка сверху вниз по сетке пикселей арта: окно шириной 88 % safe area (не шире <see cref="MaxBoxWidth"/>), высота —
        /// по содержимому, по центру; первая из раскладок <see cref="s_Sizes"/>, которая помещается в 96 % высоты.
        /// </summary>
        void Layout()
        {
            m_LayoutSize = m_Safe.rect.size;
            float safeW = m_Safe.rect.width > 100 * Px ? m_Safe.rect.width : 1080f;
            float safeH = m_Safe.rect.height > 100 * Px ? m_Safe.rect.height : 1920f;
            float width = Mathf.Min(MaxBoxWidth, PixelUi.Snap(safeW * (1f - 2f * SideMargin)));
            width = Mathf.Floor(width / (2 * Px)) * 2 * Px;
            float inner = width - 2 * BoxSide;

            m_Level = s_Sizes.Length - 1;
            for (int i = 0; i < s_Sizes.Length; i++)
                if (Height(s_Sizes[i]) <= safeH * 0.96f)
                {
                    m_Level = i;
                    break;
                }
            var size = s_Sizes[m_Level];
            float height = Mathf.Ceil(Height(size) / (2 * Px)) * 2 * Px;
            m_Box.sizeDelta = new Vector2(width, height);
            m_Box.anchoredPosition = Vector2.zero;

            float y = HeroTop(size);
            float hero = HeroSize(size.Hero);
            PixelUi.SetIcon(m_Cat, "icon_cat_big", size.Hero);
            m_Cat.rectTransform.anchoredPosition = PlayableBanner.CenterOffset("icon_cat_big", size.Hero);
            PlaceAt(m_HeroSlot.rectTransform, y, hero, 0f, hero);
            PixelUi.SetIcon(m_Glow, "fx_glow_warm", size.Hero + 1);
            m_Glow.rectTransform.anchoredPosition = new Vector2(0f, -(y + hero * 0.5f)) + PlayableBanner.CenterOffset("fx_glow_warm", size.Hero + 1);
            y += hero + 3 * Px;
            Place(Title.rectTransform, ref y, TitleHeight, inner, Px);
            // Строка — FontLarge, не помещается по ширине — FontBody (размеры только из таблицы кита).
            Line.fontSize = PixelUi.FontLarge;
            if (Line.GetPreferredValues(Line.text).x > inner) Line.fontSize = PixelUi.FontBody;
            Place(Line.rectTransform, ref y, LineHeight, inner, SectionGap);

            float tileW = Mathf.Floor((inner - TileGap) / 2f / Px) * Px;
            float labelFont = LabelFont(size, tileW);
            for (int i = 0; i < m_Tiles.Length; i++)
            {
                int row = i / 2, col = i % 2;
                float cx = (col == 0 ? -1f : 1f) * (tileW + TileGap) * 0.5f;
                PlaceAt(m_Tiles[i].Panel.rectTransform, y + row * (size.Tile + TileGap), size.Tile, cx, tileW);
                LayoutTile(m_Tiles[i], size, labelFont);
            }
            y += 2 * size.Tile + TileGap + SectionGap;

            Ui.SetActive(m_GearRow, size.ShowGear);
            if (size.ShowGear)
            {
                float rowW = 4 * size.Gear + 3 * GearGap;
                PlaceAt(m_GearRow, y, size.Gear, 0f, rowW);
                for (int i = 0; i < m_Gear.Length; i++) LayoutGear(m_Gear[i], m_GearIcons[i], size.Gear, i * (size.Gear + GearGap));
                y += size.Gear + SectionGap;
            }

            // Закрытый контент — FontBody, не помещается — FontSmall.
            Teaser.fontSize = PixelUi.FontBody;
            float teaserW = inner - 6 * Px - PixelUi.IconSize("icon_lock").x - 2 * Px - 6 * Px;
            if (Teaser.GetPreferredValues(Teaser.text).x > teaserW) Teaser.fontSize = PixelUi.FontSmall;
            Place(m_TeaserPanel.rectTransform, ref y, TeaserHeight, inner, 6 * Px);
            Place(Play.Rect, ref y, size.Play, PixelUi.Snap(inner * 0.84f), SecondaryGap);
            float half = (inner - SecondaryGap) * 0.5f;
            PlaceAt(Keep.Rect, y, PixelUi.ButtonHeight, -(half + SecondaryGap) * 0.5f, half);
            PlaceAt(Again.Rect, y, PixelUi.ButtonHeight, (half + SecondaryGap) * 0.5f, half);
        }

        const float TilePad = 4 * Px;

        /// <summary>Отступ текста плитки слева: поле, место под иконку ×N, зазор 3 пикселя арта.</summary>
        static float TileTextLeft(Tile tile, Size size) =>
            TilePad + Mathf.Max(PixelUi.IconSize(tile.IconName, size.TileIcon).x, 16 * Px * size.TileIcon) + 3 * Px;

        /// <summary>
        /// Подпись плиток: в крупных плитках — FontBody (FontSmall на телефоне почти не читается), если все четыре подписи
        /// помещаются по ширине, иначе (и в мелких раскладках) — FontSmall; у всех плиток одинаковая.
        /// </summary>
        float LabelFont(Size size, float tileW)
        {
            if (size.Tile < 36 * Px) return PixelUi.FontSmall;
            foreach (var tile in m_Tiles)
            {
                float room = tileW - TileTextLeft(tile, size) - TilePad;
                tile.Label.fontSize = PixelUi.FontBody;
                if (tile.Label.GetPreferredValues(tile.Label.text).x > room) return PixelUi.FontSmall;
            }
            return PixelUi.FontBody;
        }

        /// <summary>Иконка ×N слева по центру высоты, справа сверху число, под ним подпись (<paramref name="labelFont"/>).</summary>
        static void LayoutTile(Tile tile, Size size, float labelFont)
        {
            PixelUi.SetIcon(tile.Icon, tile.IconName, size.TileIcon);
            var icon = PixelUi.IconSize(tile.IconName, size.TileIcon);
            float pad = TilePad;
            tile.Icon.rectTransform.anchoredPosition = new Vector2(pad + icon.x * 0.5f, 0f) + PlayableBanner.CenterOffset(tile.IconName, size.TileIcon);
            float left = TileTextLeft(tile, size);
            bool large = size.Tile >= 36 * Px;
            float valueH = large ? 14 * Px : 11 * Px, labelH = labelFont >= PixelUi.FontBody ? 9 * Px : 7 * Px;
            float top = Mathf.Floor((size.Tile - valueH - labelH) * 0.5f / Px) * Px;
            tile.Value.fontSize = large ? PixelUi.FontTitle : PixelUi.FontLarge;
            tile.Label.fontSize = labelFont;
            tile.Value.rectTransform.Anchor(0, 1, 1, 1, new Vector2(left, -(top + valueH)), new Vector2(-pad, -top));
            tile.Label.rectTransform.Anchor(0, 1, 1, 1, new Vector2(left, -(top + valueH + labelH)), new Vector2(-pad, -(top + valueH)));
        }

        /// <summary>Слот экипировки: ячейка, внутри предмет (большая иконка, в мелкой ячейке — обычная) или замок.</summary>
        static void LayoutGear(GearSlot slot, string icon, float cell, float x)
        {
            var rt = slot.Panel.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(cell, cell);
            rt.anchoredPosition = new Vector2(x, 0f);
            string name = icon ?? "icon_lock";
            if (icon != null && PixelUi.IconSize(icon).x > cell - 4 * Px)
            {
                string small = icon.EndsWith("_big") ? icon.Substring(0, icon.Length - 4) : icon;
                if (PixelArt.Has(small)) name = small;
            }
            slot.IconName = name;
            PixelUi.SetIcon(slot.Icon, name);
            slot.Icon.rectTransform.anchoredPosition = PlayableBanner.CenterOffset(name, 1);
            slot.Icon.color = icon != null ? Color.white : PixelUi.DisabledIconTint;
        }

        /// <summary>Ячейка знака игры: котик ×N и поле по 4 пикселя арта (чётное число пикселей — центр на сетке).</summary>
        static float HeroSize(int scale)
        {
            var cat = PixelUi.IconSize("icon_cat_big", scale);
            return Mathf.Ceil((Mathf.Max(cat.x, cat.y) + 8 * Px) / (2 * Px)) * 2 * Px;
        }

        /// <summary>
        /// Верх ячейки знака игры: не выше <see cref="BoxTop"/> и так, чтобы свечение (на шаг крупнее котика) начиналось внутри
        /// рамки окна, на <see cref="GlowTop"/> ниже её верха.
        /// </summary>
        static float HeroTop(Size s)
        {
            float glow = PixelUi.IconSize("fx_glow_warm", s.Hero + 1).y;
            return Mathf.Max(BoxTop, PixelUi.Snap(GlowTop + (glow - HeroSize(s.Hero)) * 0.5f));
        }

        /// <summary>Высота окна по содержимому (единицы Canvas).</summary>
        static float Height(Size s) =>
            HeroTop(s) + HeroSize(s.Hero) + 3 * Px + TitleHeight + Px + LineHeight + SectionGap + 2 * s.Tile + TileGap + SectionGap +
            (s.ShowGear ? s.Gear + SectionGap : 0f) + TeaserHeight + 6 * Px + s.Play + SecondaryGap + PixelUi.ButtonHeight + BoxBottom;

        /// <summary>Элемент по центру окна: верх на <paramref name="y"/> от верха рамки, высота <paramref name="h"/>; y — ниже на h + gap.</summary>
        static void Place(RectTransform rt, ref float y, float h, float w, float gap)
        {
            PlaceAt(rt, y, h, 0f, w);
            y += h + gap;
        }

        /// <summary>Pivot в центре: поп кнопки «PLAY FREE» растёт от середины. Высоты — чётное число пикселей арта.</summary>
        static void PlaceAt(RectTransform rt, float y, float h, float centerX, float w)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            w = Mathf.Floor(w / (2 * Px)) * 2 * Px;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(PixelUi.Snap(centerX), -(y + h * 0.5f));
        }
    }
}
