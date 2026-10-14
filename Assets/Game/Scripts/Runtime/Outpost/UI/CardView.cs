using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Общая карточка коллекции и окна сведений (одна разметка для всех состояний): рамка редкости, фон, арт (виден всегда),
    /// имя, короткий эффект (текущий уровень или уровень 1 у неполученной), уровень, подпись состояния, копии до улучшения.
    /// В окне сведений — крупный портрет, тексты и счётчик копий; у полученной карточки без повторной подписи состояния.
    /// Закрытая по прогрессу — затемнение арта вместе с фоном портрета (LockedArtAlpha), яркий замок поверх затемнения.
    /// Строки снизу вверх в UI-пикселях: копии 3–11, состояние 12–22, эффект 23–35, уровень 36–46; высота ячейки сетки — CellHeight.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float LockedArtAlpha = 0.60f;
        /// <summary>Высота ячейки сетки (UI-пиксели): три ряда помещаются на экран 9:16.</summary>
        public const int CellHeight = 126;
        public CardDef Def { get; private set; }
        public CardViewState State { get; private set; }
        public Image Art { get; private set; }
        public TextMeshProUGUI Status { get; private set; }
        public RectTransform Rect => (RectTransform)transform;
        Image m_Shade, m_Lock;
        TextMeshProUGUI m_Name, m_Level, m_Effect;
        Image m_Portrait;
        bool m_Detail;
        CardRules m_Rules;
        PixelIconText m_Copies;

        public static CardView Create(Transform parent, CardDef def, int scale, UnityAction tap = null, CardRules rules = null, bool detail = false)
        {
            var frame = PixelUi.Sliced(parent, def.Id, OutpostCardText.Frame(def.Rarity));
            var view = frame.gameObject.AddComponent<CardView>();
            view.Def = def;
            view.m_Rules = rules;
            if (tap != null)
            {
                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(tap);
            }
            const float px = PixelUi.Px;
            var portrait = PixelUi.Icon(frame.transform, "portrait", OutpostCardText.Background(def.Rarity), scale);
            view.m_Portrait = portrait;
            portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.5f, 1);
            portrait.rectTransform.pivot = new Vector2(0.5f, 1);
            portrait.rectTransform.anchoredPosition = new Vector2(0, -4 * px);
            view.Art = PixelUi.Icon(portrait.transform, "art", OutpostCardText.Art(def), scale);
            view.Art.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.Art.rectTransform.anchoredPosition = Vector2.zero;
            if (!PixelArt.Has(OutpostCardText.Art(def))) Debug.LogError("[Cards] Missing sprite " + OutpostCardText.Art(def));
            if (!PixelArt.Has(OutpostCardText.Frame(def.Rarity))) Debug.LogError("[Cards] Missing sprite " + OutpostCardText.Frame(def.Rarity));
            if (!PixelArt.Has(OutpostCardText.Background(def.Rarity))) Debug.LogError("[Cards] Missing sprite " + OutpostCardText.Background(def.Rarity));
            // Затемняем весь портрет, включая фон под замком; рамка и подписи остаются читаемыми.
            // Замок создаётся следующим соседом, поэтому рисуется поверх затемнения.
            var shade = Ui.Rect(portrait.transform, "lockedShade").Anchor(0, 0, 1, 1);
            view.m_Shade = shade.gameObject.AddComponent<Image>();
            view.m_Shade.color = new Color(0, 0, 0, LockedArtAlpha);
            view.m_Shade.raycastTarget = false;
            view.m_Lock = PixelUi.Icon(portrait.transform, "lock", "icon_lock");
            view.m_Lock.rectTransform.anchorMin = view.m_Lock.rectTransform.anchorMax = new Vector2(1, 0);
            view.m_Lock.rectTransform.pivot = new Vector2(1, 0);
            view.m_Lock.rectTransform.anchoredPosition = Vector2.zero;
            string title = OutpostCardText.Name(def);
            int space = title.IndexOf(' ');
            if (!detail && title.Length >= 18 && space > 0) title = title.Substring(0, space) + "\n" + title.Substring(space + 1);
            var name = PixelUi.Text(frame.transform, "name", title, PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            view.m_Name = name;
            name.rectTransform.Anchor(0, 1, 1, 1, new Vector2(3 * px, -(4 + 48 * scale + 21) * px), new Vector2(-3 * px, -(4 + 48 * scale) * px));
            Ui.AutoSize(name, PixelUi.FontSmall, PixelUi.FontBody);
            view.m_Level = PixelUi.Text(frame.transform, "level", "", PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            view.m_Level.rectTransform.Anchor(0, 0, 1, 0, new Vector2(3 * px, 36 * px), new Vector2(-3 * px, 46 * px));
            view.m_Effect = PixelUi.Text(frame.transform, "effect", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            view.m_Effect.rectTransform.Anchor(0, 0, 1, 0, new Vector2(3 * px, 23 * px), new Vector2(-3 * px, 35 * px));
            Ui.AutoSize(view.m_Effect, PixelUi.FontSmall * 0.75f, PixelUi.FontSmall);
            view.Status = PixelUi.Text(frame.transform, "status", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            view.Status.rectTransform.Anchor(0, 0, 1, 0, new Vector2(3 * px, 12 * px), new Vector2(-3 * px, 22 * px));
            Ui.AutoSize(view.Status, PixelUi.FontSmall * 0.85f, PixelUi.FontSmall);
            view.m_Copies = PixelUi.IconText(frame.transform, "copies", "icon_card_small", "", detail ? PixelUi.FontLarge : PixelUi.FontSmall,
                detail ? PixelUi.TextColor : PixelUi.TextDim, iconScale: detail ? 2 : 1);
            view.m_Copies.Root.anchorMin = view.m_Copies.Root.anchorMax = new Vector2(0.5f, 0);
            view.m_Copies.Root.anchoredPosition = new Vector2(0, 7 * px);
            if (detail)
            {
                view.m_Detail = true;
                foreach (var text in new[] { name, view.m_Level, view.m_Effect, view.Status })
                {
                    text.enableAutoSizing = false;
                    text.fontSize = PixelUi.FontLarge;
                }
                name.fontSize = PixelUi.FontTitle;
                view.LayoutDetail();
            }
            return view;
        }

        void OnRectTransformDimensionsChange()
        {
            if (m_Detail) LayoutDetail();
        }

        void LayoutDetail()
        {
            const float px = PixelUi.Px;
            // Сохраняем целый масштаб арта; место под крупными строками остаётся и на узких экранах.
            int scale = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min((Rect.rect.width - 4 * px) / (64 * px),
                (Rect.rect.height - 92 * px) / (48 * px))), 1, 3);
            PixelUi.SetIcon(m_Portrait, OutpostCardText.Background(Def.Rarity), scale);
            m_Portrait.rectTransform.pivot = new Vector2(0.5f, 1);
            PixelUi.SetIcon(Art, OutpostCardText.Art(Def), scale);
            m_Name.rectTransform.Anchor(0, 1, 1, 1, new Vector2(4 * px, -(6 + 48 * scale + 18) * px),
                new Vector2(-4 * px, -(6 + 48 * scale) * px));
            m_Level.rectTransform.Anchor(0, 0, 1, 0, new Vector2(4 * px, 51 * px), new Vector2(-4 * px, 67 * px));
            m_Effect.rectTransform.Anchor(0, 0, 1, 0, new Vector2(4 * px, 26 * px), new Vector2(-4 * px, 48 * px));
            Status.rectTransform.Anchor(0, 0, 1, 0, new Vector2(4 * px, 3 * px), new Vector2(-4 * px, 25 * px));
            m_Copies.Root.anchoredPosition = new Vector2(0, 12 * px);
        }

        public void Refresh(OutpostProfile profile)
        {
            State = CardService.StateOf(profile, Def);
            var quote = CardService.Quote(profile, Def);
            m_Shade.gameObject.SetActive(State == CardViewState.Locked);
            m_Lock.gameObject.SetActive(State == CardViewState.Locked);
            Ui.SetText(Status, OutpostCardText.Status(State, Def, profile));
            Status.color = State == CardViewState.Upgradable ? PixelUi.TextAccentLight : PixelUi.TextColor;
            if (m_Detail) Ui.SetActive(Status, quote.Level < 1 || quote.Level >= quote.MaxLevel);
            Ui.SetText(m_Level, quote.Level > 0 ? Loc.F("cards.level", quote.Level) : "");
            // Неполученная карточка показывает эффект уровня 1 приглушённо: игрок заранее видит, что получит.
            Ui.SetText(m_Effect, OutpostCardText.ShortEffect(Def, quote.Level, m_Rules));
            m_Effect.color = quote.Level > 0 ? PixelUi.TextColor : PixelUi.TextDim;
            m_Copies.Root.gameObject.SetActive(quote.Level > 0 && quote.Level < quote.MaxLevel);
            if (quote.Level > 0 && quote.Level < quote.MaxLevel)
            {
                m_Copies.SetText(Loc.F("cards.copies", quote.CopiesOwned, quote.CopiesNeeded));
                if (m_Detail) m_Copies.Text.color = quote.CopiesOwned >= quote.CopiesNeeded ? PixelPalette.Green6 : PixelUi.TextColor;
            }
        }
    }
}
