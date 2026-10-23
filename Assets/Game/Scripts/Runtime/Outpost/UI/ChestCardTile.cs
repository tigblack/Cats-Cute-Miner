using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Карточка в окне награды сундука (ТЗ 10.5): рамка редкости, портрет (фон редкости и арт ×1), число копий «×N» в углу
    /// портрета, имя под ним и золотой бейдж «NEW» у впервые полученной. Фон портрета 64×48 обрезается по ширине плитки:
    /// на узком экране три карточки в ряд всё равно помещаются, арт (до 48 пикселей) виден целиком. Плитки создаются один
    /// раз и переиспользуются (<see cref="Set"/>, <see cref="Place"/>).
    /// </summary>
    public sealed class ChestCardTile
    {
        const float Px = PixelUi.Px;
        /// <summary>Высота плитки, пиксели арта: рамка 4 + портрет 48 + зазор 1 + имя в две строки 17 + низ 4.</summary>
        public const int HeightPx = 74;
        /// <summary>Ширина, при которой фон портрета виден целиком (64 + рамка по 4).</summary>
        public const int FullWidthPx = 72;

        public readonly RectTransform Root;
        readonly Image m_Frame, m_Back, m_Art, m_Badge;
        readonly TextMeshProUGUI m_Name, m_Copies;

        /// <summary>Показанная карточка и пометка NEW (тесты и снимки).</summary>
        public string CardId { get; private set; }
        public bool IsNew { get; private set; }

        ChestCardTile(Image frame, Image back, Image art, TextMeshProUGUI copies, TextMeshProUGUI name, Image badge)
        {
            Root = frame.rectTransform;
            m_Frame = frame;
            m_Back = back;
            m_Art = art;
            m_Copies = copies;
            m_Name = name;
            m_Badge = badge;
        }

        public static ChestCardTile Create(Transform parent, string name)
        {
            var frame = PixelUi.Sliced(parent, name, OutpostCardText.Frame(1));
            frame.raycastTarget = false;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);

            var portrait = Ui.Rect(frame.transform, "portrait").Anchor(0, 1, 1, 1, new Vector2(3 * Px, -52 * Px), new Vector2(-3 * Px, -4 * Px));
            portrait.gameObject.AddComponent<RectMask2D>();
            var back = PixelUi.Icon(portrait, "back", OutpostCardText.Background(1));
            var art = PixelUi.Icon(portrait, "art", "icon_cards");
            var copies = PixelUi.Text(portrait, "copies", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.BottomRight, true, true);
            copies.rectTransform.Anchor(0, 0, 1, 0, new Vector2(2 * Px, Px), new Vector2(-2 * Px, 11 * Px));
            copies.textWrappingMode = TextWrappingModes.NoWrap;

            var title = PixelUi.Text(frame.transform, "name", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            title.rectTransform.Anchor(0, 0, 1, 0, new Vector2(3 * Px, 4 * Px), new Vector2(-3 * Px, 21 * Px));
            Ui.AutoSize(title, PixelUi.FontSmall, PixelUi.FontBody);

            // «NEW» — в правом верхнем углу портрета, внутри плитки: соседняя карточка не перекрывается.
            var badge = PixelUi.Sliced(frame.transform, "new", "ui_btn_gold");
            badge.raycastTarget = false;
            var brt = badge.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 1f);
            brt.sizeDelta = new Vector2(24 * Px, 12 * Px);
            brt.anchoredPosition = new Vector2(-2 * Px, -3 * Px);
            var badgeText = PixelUi.Text(badge.transform, "text", Loc.T("outpost.store.chest_new"), PixelUi.FontBody, PixelUi.TextDark, TextAlignmentOptions.Center, true, false);
            badgeText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(Px, Px), new Vector2(-Px, 0f));
            badgeText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(badgeText, PixelUi.FontSmall, PixelUi.FontBody);
            return new ChestCardTile(frame, back, art, copies, title, badge);
        }

        /// <summary>Карточка, копии и пометка NEW.</summary>
        public void Set(CardDef def, long copies, bool isNew)
        {
            CardId = def.Id;
            IsNew = isNew;
            PixelUi.SetSliced(m_Frame, OutpostCardText.Frame(def.Rarity));
            PixelUi.SetIcon(m_Back, OutpostCardText.Background(def.Rarity));
            // Арт — по центру обрезанного прямоугольника, как в коллекции (CardView).
            PixelUi.SetIcon(m_Art, OutpostCardText.Art(def));
            m_Art.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            m_Art.rectTransform.anchoredPosition = Vector2.zero;
            Ui.SetText(m_Copies, Loc.F("outpost.chest.copies", copies));
            Ui.SetText(m_Name, OutpostCardText.Name(def));
            Ui.SetActive(m_Badge, isNew);
        }

        /// <summary>Левый верхний угол (x, y) от левого верхнего угла родителя и ширина — единицы Canvas.</summary>
        public void Place(float x, float y, float width)
        {
            Root.anchoredPosition = new Vector2(x, -y);
            Root.sizeDelta = new Vector2(width, HeightPx * Px);
        }

        public void SetActive(bool on) => Ui.SetActive(Root, on);
    }
}
