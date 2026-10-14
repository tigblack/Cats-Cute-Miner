using System;
using System.Globalization;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Плитка магазина (ТЗ 10.5) на ките PixelUi: фон-кнопка — зелёный (бесплатно: реклама готова или расходник в наличии),
    /// синий (покупка за гемы или деньги) или тёмный (рекламная плитка на таймере, покупки недоступны); сверху название, в
    /// светлой панели — арт ×2 и количество (у бонуса первой покупки обычное количество зачёркнуто), снизу цена или действие
    /// («OPEN!» с телевизором, «USE», гем и цена, «$1.99», часы и таймер). Над углом — бейдж «+N%», под плиткой — плашка
    /// «Available: N», у сундуков — кнопка «i» (шансы выпадения). Сеттеры меняют элементы только при смене значения.
    /// </summary>
    public sealed class StoreTileView
    {
        const float Px = PixelUi.Px;
        /// <summary>Высота плитки и место под плашкой «Available» в ячейке сетки, пиксели арта.</summary>
        public const float TileHeight = 116, PillHeight = 12, PillGap = 2;
        public const float CellHeight = TileHeight + PillGap + PillHeight;

        public readonly StoreItemDef Def;
        public readonly RectTransform Root;
        readonly Ui.ButtonRefs m_Button;
        readonly TextMeshProUGUI m_Title, m_Amount, m_OldAmount, m_Action;
        readonly PixelIconText m_Price;
        readonly Image m_Art, m_Pill, m_Badge;
        readonly TextMeshProUGUI m_PillText, m_BadgeText;
        string m_Signature;

        public Ui.ButtonRefs Button => m_Button;

        StoreTileView(StoreItemDef def, RectTransform root, Ui.ButtonRefs button, TextMeshProUGUI title, Image art, TextMeshProUGUI oldAmount,
            TextMeshProUGUI amount, TextMeshProUGUI action, PixelIconText price, Image pill, TextMeshProUGUI pillText, Image badge, TextMeshProUGUI badgeText)
        {
            Def = def;
            Root = root;
            m_Button = button;
            m_Title = title;
            m_Art = art;
            m_OldAmount = oldAmount;
            m_Amount = amount;
            m_Action = action;
            m_Price = price;
            m_Pill = pill;
            m_PillText = pillText;
            m_Badge = badge;
            m_BadgeText = badgeText;
        }

        /// <summary>Арт плитки 24×24: сундук — icon_chest_&lt;тип&gt; (как в наградах шахты), остальное — store_&lt;ID плитки&gt;.</summary>
        public static string ArtOf(StoreItemDef def)
        {
            if (def == null) return "icon_chest_wooden";
            if (def.Kind == StoreItemKind.Chest && def.ChestId != null && def.ChestId.StartsWith("Gacha", StringComparison.Ordinal))
                return "icon_chest_" + def.ChestId.Substring(5).ToLowerInvariant();
            return "store_" + def.Id;
        }

        /// <summary>Количество для плитки и счётчиков: до 100 000 — полностью, с запятыми («1,700»), дальше — коротко («100K»).</summary>
        public static string FormatAmount(long n) =>
            n < 100_000 ? n.ToString("N0", CultureInfo.InvariantCulture) : OutpostUiText.Money(n);

        public static StoreTileView Create(Transform parent, StoreItemDef def, Action<StoreItemDef> onTap, Action<StoreItemDef> onInfo)
        {
            var root = Ui.Rect(parent, def.Id);
            Action tap = () => onTap?.Invoke(def);
            var button = PixelUi.Button(root, "tile", "", PixelButton.Blue, () => tap(), PixelUi.FontBody);
            button.Rect.Anchor(0, 1, 1, 1, new Vector2(0f, -TileHeight * Px), Vector2.zero);
            Ui.SetActive(button.Label, false);
            var content = button.Content;

            var title = PixelUi.Text(content, "title", OutpostUiText.StoreItemName(def), PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(4 * Px, -22 * Px), new Vector2(-4 * Px, -3 * Px));
            Ui.AutoSize(title, PixelUi.FontSmall, PixelUi.FontBody);

            var inner = PixelUi.Panel(content, "inner", PixelPanel.Inset);
            inner.raycastTarget = false;
            inner.rectTransform.Anchor(0, 0, 1, 1, new Vector2(3 * Px, 23 * Px), new Vector2(-3 * Px, -23 * Px));
            // У сундука нет строки количества — арт по центру панели; у остальных — сверху, над количеством.
            var art = PixelUi.Icon(inner.transform, "art", ArtOf(def), 2);
            bool chest = def.Kind == StoreItemKind.Chest;
            art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0.5f, chest ? 0.5f : 1f);
            art.rectTransform.anchoredPosition = chest ? Vector2.zero : new Vector2(0f, -(PixelUi.IconSize(ArtOf(def), 2).y * 0.5f + 3 * Px));
            var oldAmount = PixelUi.Text(inner.transform, "oldAmount", "", PixelUi.FontBody, PixelUi.TextBad, TextAlignmentOptions.Center, true, true);
            oldAmount.rectTransform.Anchor(0, 0, 1, 0, new Vector2(2 * Px, 12 * Px), new Vector2(-2 * Px, 21 * Px));
            oldAmount.textWrappingMode = TextWrappingModes.NoWrap;
            var amount = PixelUi.Text(inner.transform, "amount", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            amount.rectTransform.Anchor(0, 0, 1, 0, new Vector2(2 * Px, 2 * Px), new Vector2(-2 * Px, 13 * Px));
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(amount, PixelUi.FontBody, PixelUi.FontLarge);

            // Нижняя полоса: действие («OPEN!», «USE», «$5.99») текстом или цена/таймер с иконкой.
            var action = PixelUi.Text(content, "action", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            action.rectTransform.Anchor(0, 0, 1, 0, new Vector2(4 * Px, 4 * Px), new Vector2(-4 * Px, 21 * Px));
            action.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(action, PixelUi.FontBody, PixelUi.FontLarge);
            var price = PixelUi.IconText(content, "price", "icon_gem", "", PixelUi.FontLarge, PixelUi.TextColor);
            price.Root.anchorMin = price.Root.anchorMax = new Vector2(0.5f, 0f);
            price.Root.pivot = new Vector2(0.5f, 0.5f);
            price.Root.anchoredPosition = new Vector2(0f, 12.5f * Px);

            // «Available: N» — под плиткой; бейдж «+N%» — над правым верхним углом.
            var pill = PixelUi.Panel(root, "available", PixelPanel.Pill);
            pill.raycastTarget = false;
            pill.rectTransform.Anchor(0.1f, 0, 0.9f, 0, Vector2.zero, new Vector2(0f, PillHeight * Px));
            var pillText = PixelUi.Text(pill.transform, "text", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            pillText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(2 * Px, 0f), new Vector2(-2 * Px, 0f));
            pillText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(pillText, PixelUi.FontSmall, PixelUi.FontBody);
            var badge = PixelUi.Sliced(root, "bonus", "ui_btn_gold");
            badge.raycastTarget = false;
            var brt = badge.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(30 * Px, 13 * Px);
            brt.anchoredPosition = new Vector2(2 * Px, -2 * Px);
            var badgeText = PixelUi.Text(badge.transform, "text", "", PixelUi.FontBody, PixelUi.TextDark, TextAlignmentOptions.Center, true, false);
            badgeText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(Px, Px), new Vector2(-Px, 0f));
            badgeText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(badgeText, PixelUi.FontSmall, PixelUi.FontBody);

            if (def.Kind == StoreItemKind.Chest && onInfo != null)
            {
                var info = PixelUi.Button(inner.transform, "info", "", PixelButton.Dark, () => onInfo(def), PixelUi.FontBody);
                info.SetIcon("icon_info");
                var irt = info.Rect;
                irt.anchorMin = irt.anchorMax = new Vector2(1f, 1f);
                irt.pivot = new Vector2(1f, 1f);
                irt.sizeDelta = new Vector2(16 * Px, 16 * Px);
                irt.anchoredPosition = new Vector2(-Px, -Px);
            }
            return new StoreTileView(def, root, button, title, art, oldAmount, amount, action, price, pill, pillText, badge, badgeText);
        }

        /// <summary>Обновить плитку по котировке ядра; <paramref name="phaseReason"/> != null — команды сейчас недоступны.</summary>
        public void Refresh(StoreQuote q, string phaseReason)
        {
            q = OutpostController.PlatformStoreQuote(q);
            var def = Def;
            bool cooldown = q.CooldownLeftMs > 0;
            // Таймер показывается с точностью до минуты: подпись не перестраивается каждый кадр.
            long minutes = cooldown ? (q.CooldownLeftMs + 59_999) / 60_000 : 0;
            string signature = (q.Free ? "F" : "P") + q.Owned + "|" + minutes + "|" + q.Amount + "|" + (q.BonusAvailable ? 1 : 0) + "|" +
                (q.BlockReason ?? "") + "|" + (phaseReason ?? "") + "|" + CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Price(def.Id);
            if (signature == m_Signature) return;
            m_Signature = signature;
            m_Button.Button.interactable = q.CanBuy && phaseReason == null;

            var style = q.Free ? PixelButton.Green : cooldown || q.BlockReason == "unavailable" ? PixelButton.Dark : PixelButton.Blue;
            if (m_Button.Style != style) m_Button.SetStyle(style);

            // Количество: тайм-скип — длительность, эликсир и гемы — число (у бонуса обычное количество зачёркнуто), сундук — без числа.
            string amount = def.Kind == StoreItemKind.TimeSkip ? OutpostUiText.SkipDuration(def.Minutes)
                : def.Kind == StoreItemKind.Chest ? "" : FormatAmount(q.Amount);
            Ui.SetText(m_Amount, amount);
            m_Amount.color = q.BonusAvailable ? PixelPalette.Green6 : PixelUi.TextColor;
            Ui.SetActive(m_OldAmount, q.BonusAvailable);
            if (q.BonusAvailable) Ui.SetText(m_OldAmount, "<s>" + FormatAmount(def.Amount) + "</s>");
            Ui.SetActive(m_Badge, q.BonusAvailable && def.BonusPercent > 0);
            if (q.BonusAvailable) Ui.SetText(m_BadgeText, Loc.F("outpost.store.bonus", def.BonusPercent));

            // Плашка: расходники в наличии или единственная бонусная покупка.
            int available = q.Owned > 0 ? q.Owned : q.BonusAvailable ? 1 : 0;
            Ui.SetActive(m_Pill, available > 0);
            if (available > 0) Ui.SetText(m_PillText, Loc.F("outpost.store.available", available));

            // Нижняя полоса.
            string text = null, icon = null, label = null;
            var color = PixelUi.TextColor;
            if (q.Owned > 0) text = Loc.T("outpost.store.use");
            else if (cooldown)
            {
                icon = "icon_clock";
                label = NumberFormat.FormatDuration(minutes * 60.0);
            }
            else if (q.BlockReason == "unavailable") text = Loc.T("outpost.store.soon");
            else if (def.Currency == StoreCurrency.Ad)
            {
                icon = "icon_ad_tv";
                label = Loc.T("outpost.store.open");
            }
            else if (def.Currency == StoreCurrency.Real) text = OutpostController.AndroidRuntime ? CatMiners.Runtime.Purchasing.GooglePaymentsRuntime.Instance?.Price(def.Id) ?? "Unavailable" : Loc.F("outpost.store.price_usd", def.PriceUsd);
            else
            {
                icon = "icon_gem";
                label = FormatAmount(def.Price);
                if (q.BlockReason == "gems") color = PixelUi.TextBad;
            }
            Ui.SetActive(m_Action, text != null);
            Ui.SetActive(m_Price.Root, icon != null);
            if (text != null) Ui.SetText(m_Action, text);
            if (icon != null)
            {
                m_Price.SetIcon(icon);
                m_Price.SetText(label);
                m_Price.Text.color = color;
            }
        }
    }
}
