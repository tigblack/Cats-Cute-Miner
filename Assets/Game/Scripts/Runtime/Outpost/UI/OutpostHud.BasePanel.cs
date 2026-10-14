using System;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Панель «База» (ТЗ 9, 14; RUNTIME_SPEC §5): лист снизу с подписью «На эту шахту», переключателем ×1/×10/«Макс»
    /// и шестью ветками. Строка ветки: иконка, название, уровень L/cap (и +N ур. пакета), эффект «сейчас → после» —
    /// из TeamStats ядра для текущих уровней и уровней после покупки (TeamStats.Compute, без UI-формул), цена из котировки
    /// ядра (×10/«Макс» — фактическое число уровней и точная сумма до нажатия), у каждой ветки — шкала уровня L/cap;
    /// на cap — полная зелёная шкала и «Макс.» без цены. «Команда» — только ×1. В ReadyToContinue покупки отключены
    /// с причиной. Внизу — подсказка ограничения потока по реальным состояниям команды (OutpostFlowStats).
    /// Тап по недоступной кнопке — короткая причина тостом (ТЗ 14).
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float BaseTopRow = 18 * Px, BaseRowHeight = 34 * Px, BaseRowGap = 2 * Px, BaseHintHeight = 18 * Px;
        const float BuyWidth = 64 * Px, BuyHeight = 28 * Px;

        sealed class BranchRow
        {
            public UpgradeBranch Branch;
            public TextMeshProUGUI Title, Effect;
            public PixelBar Bar;
            public Ui.ButtonRefs Buy;
            public PixelIconText Price;
            /// <summary>Код причины недоступности кнопки (для тоста по тапу); null — доступна.</summary>
            public string Reason;
            public int PriceColor = -1;
        }

        static readonly string[] s_ModeKeys = { "outpost.base.mode.one", "outpost.base.mode.ten", "outpost.base.mode.max" };

        PixelWindow m_Base;
        WindowEntry m_BaseEntry;
        readonly BranchRow[] m_Rows = new BranchRow[OutpostIds.BranchCount];
        readonly Ui.ButtonRefs[] m_ModeButtons = new Ui.ButtonRefs[3];
        BuyMode m_BuyMode = BuyMode.One;
        TextMeshProUGUI m_BaseHint;
        readonly int[] m_LevelsBuf = new int[OutpostIds.BranchCount];
        // Сигнатура показанных строк: кошелёк, уровни, режим, фаза, доступность команд, версия экипировки.
        readonly int[] m_BaseLevels = new int[OutpostIds.BranchCount];
        BigInteger m_BaseWallet;
        int m_BaseSig = int.MinValue, m_BaseHintKey = int.MinValue, m_BuyClickFrame = -1;
        bool m_BaseValid;

        /// <summary>Панель «База» открыта.</summary>
        public bool BaseOpen => m_Base != null && m_Base.Visible;

        /// <summary>Режим пакетной покупки панели (×1/×10/«Макс»); «Команда» всегда ×1.</summary>
        public BuyMode BuyMode => m_BuyMode;

        void BuildBase()
        {
            m_Base = PixelUi.Window(m_Safe, "Base", Loc.T("outpost.base.title"), HideBase);
            m_BaseEntry = new WindowEntry { Root = m_Base.Root, Back = HideBase };
            Ui.SetActive(m_Base.BodyText, false);
            int n = OutpostIds.BranchCount;
            float body = BaseTopRow + 3 * Px + n * BaseRowHeight + (n - 1) * BaseRowGap + 3 * Px + BaseHintHeight;
            float h = Mathf.Ceil((WindowTop + body + WindowBottom) / (2 * Px)) * 2 * Px;
            // Лист снизу: открывается, не возвращая камеру к лагерю (ТЗ 5).
            m_Base.Box.Anchor(0.03f, 0f, 0.97f, 0f, new Vector2(0f, Margin), new Vector2(0f, Margin + h));
            var bodyRt = m_Base.Body;

            // «На эту шахту» против «Сохраняется между шахтами» в «Экипировке» (ТЗ 9).
            var scope = PixelUi.Panel(bodyRt, "scope", PixelPanel.Pill);
            scope.raycastTarget = false;
            scope.rectTransform.Anchor(0, 1, 0.44f, 1, new Vector2(0f, -BaseTopRow), Vector2.zero);
            var scopeText = PixelUi.Text(scope.transform, "text", Loc.T("outpost.base.scope"), PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            scopeText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(4 * Px, Px), new Vector2(-4 * Px, 0f));
            scopeText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(scopeText, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);

            for (int i = 0; i < 3; i++)
            {
                var mode = (BuyMode)i;
                float x0 = 0.46f + i * 0.18f;
                var b = PixelUi.Button(bodyRt, "mode" + i, Loc.T(s_ModeKeys[i]), PixelButton.Dark, () => SetBuyMode(mode), PixelUi.FontBody);
                b.Rect.Anchor(x0, 1, x0 + 0.18f, 1, new Vector2(i == 0 ? 0f : Px, -BaseTopRow), new Vector2(i == 2 ? 0f : -Px, 0f));
                b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                m_ModeButtons[i] = b;
            }
            ApplyModeStyles();

            for (int i = 0; i < n; i++) m_Rows[i] = BuildBranchRow(bodyRt, i, BaseTopRow + 3 * Px + i * (BaseRowHeight + BaseRowGap));

            float hintTop = BaseTopRow + 3 * Px + n * BaseRowHeight + (n - 1) * BaseRowGap + 3 * Px;
            var info = PixelUi.Icon(bodyRt, "hintIcon", "icon_info");
            info.rectTransform.anchorMin = info.rectTransform.anchorMax = new Vector2(0f, 1f);
            info.rectTransform.anchoredPosition = new Vector2(8 * Px, -(hintTop + BaseHintHeight * 0.5f));
            m_BaseHint = PixelUi.Text(bodyRt, "hint", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Left, false, true);
            m_BaseHint.rectTransform.Anchor(0, 1, 1, 1, new Vector2(18 * Px, -(hintTop + BaseHintHeight)), new Vector2(0f, -hintTop));
            Ui.AutoSize(m_BaseHint, PixelUi.FontSmall * 0.75f, PixelUi.FontSmall);
        }

        BranchRow BuildBranchRow(RectTransform parent, int index, float top)
        {
            var branch = (UpgradeBranch)index;
            var row = new BranchRow { Branch = branch };
            var panel = PixelUi.Panel(parent, "branch" + index, PixelPanel.Inset);
            panel.raycastTarget = false;
            panel.rectTransform.Anchor(0, 1, 1, 1, new Vector2(0f, -(top + BaseRowHeight)), new Vector2(0f, -top));
            var icon = PixelUi.Icon(panel.transform, "icon", OutpostUiText.BranchIcon(branch));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(12 * Px, 0f) + OutpostDialog.CenterOffset(OutpostUiText.BranchIcon(branch), 1);

            float right = BuyWidth + 8 * Px;
            row.Title = PixelUi.Text(panel.transform, "title", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            row.Title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -14 * Px), new Vector2(-right, -3 * Px));
            row.Title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Title, PixelUi.FontSmall, PixelUi.FontBody);
            row.Effect = PixelUi.Text(panel.transform, "effect", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            row.Effect.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -23 * Px), new Vector2(-right, -14 * Px));
            row.Effect.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Effect, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);

            // Общая шкала купленных уровней до фактического предела ветки, включая бонусы карточек.
            row.Bar = PixelUi.Bar(panel.transform, "levelProgress", PixelBarFill.Gold);
            row.Bar.Rect.Anchor(0, 0, 1, 0, new Vector2(24 * Px, 3 * Px), new Vector2(-right, 11 * Px));

            int k = index;
            row.Buy = PixelUi.Button(panel.transform, "buy", "", PixelButton.Gold, () => OnBuy(k), PixelUi.FontBody);
            var brt = row.Buy.Rect;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(BuyWidth, BuyHeight);
            brt.anchoredPosition = new Vector2(-3 * Px, 0f);
            row.Buy.Label.textWrappingMode = TextWrappingModes.NoWrap;
            row.Price = PixelUi.IconText(row.Buy.Content, "price", "icon_coin", "", PixelUi.FontBody, PixelUi.TextDark, true, false);
            row.Price.Root.anchorMin = row.Price.Root.anchorMax = new Vector2(0.5f, 0.5f);
            row.Price.Root.pivot = new Vector2(0.5f, 0.5f);
            row.Price.Root.anchoredPosition = new Vector2(0f, Px);
            AddDeniedTap(row.Buy, () => OnBuyDenied(k));
            return row;
        }

        /// <summary>
        /// Тап по недоступной кнопке → действие (причина тостом). Звук отказа уже играет PixelButtonPress. Нажатие доступной
        /// кнопки в том же кадре (покупка сделала её недоступной) причину не показывает.
        /// </summary>
        static void AddDeniedTap(Ui.ButtonRefs button, Action onDenied)
        {
            var trigger = button.Image.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(_ =>
            {
                if (!button.Interactable) onDenied();
            });
            trigger.triggers.Add(entry);
        }

        void ApplyModeStyles()
        {
            for (int i = 0; i < m_ModeButtons.Length; i++)
            {
                var style = (int)m_BuyMode == i ? PixelButton.Blue : PixelButton.Dark;
                if (m_ModeButtons[i].Style != style) m_ModeButtons[i].SetStyle(style);
            }
        }

        void SetBuyMode(BuyMode mode)
        {
            if (mode == m_BuyMode) return;
            m_BuyMode = mode;
            ApplyModeStyles();
            InvalidateBase();
            var sim = m_Game != null ? m_Game.Sim : null;
            if (sim != null && BaseOpen) RefreshBase(sim);
        }

        void InvalidateBase()
        {
            m_BaseValid = false;
            m_BaseHintKey = int.MinValue;
        }

        /// <summary>Открыть панель «База» (кнопка «База» или тап по постройке лагеря).</summary>
        public void ShowBase()
        {
            if (m_Base == null || m_Base.Visible) return;
            InvalidateBase();
            m_Base.Show();
            Opened(m_BaseEntry);
            var sim = m_Game != null ? m_Game.Sim : null;
            if (sim != null) RefreshBase(sim);
        }

        public void HideBase()
        {
            if (m_Base == null) return;
            m_Base.Hide();
            Closed(m_BaseEntry);
        }

        public void ToggleBase()
        {
            if (BaseOpen) HideBase();
            else ShowBase();
        }

        void OnBuy(int index)
        {
            m_BuyClickFrame = Time.frameCount;
            var row = m_Rows[index];
            if (!CommandsAllowed(out string blocked))
            {
                Denied(blocked);
                return;
            }
            var mode = row.Branch == UpgradeBranch.Team ? BuyMode.One : m_BuyMode;
            var result = m_Game.Buy(row.Branch, mode);
            if (!result.Ok) Denied(result.Reason);
            InvalidateBase();
            RefreshNow();
        }

        void OnBuyDenied(int index)
        {
            if (m_BuyClickFrame == Time.frameCount) return;
            var row = m_Rows[index];
            Toast(OutpostUiText.Reason(row.Reason));
        }

        int BaseSignature(OutpostRun run, bool allowed)
        {
            unchecked
            {
                int sig = (int)m_BuyMode * 7 + (allowed ? 1 : 0) * 31 + (int)run.Phase * 131 + m_EquipVersion * 1009 + run.Zone * 7919;
                return sig;
            }
        }

        void RefreshBase(OutpostSimulation sim)
        {
            var run = sim.Run;
            bool allowed = CommandsAllowed(out string blocked);
            bool done = run.Phase == RunPhase.ReadyToContinue;
            int sig = BaseSignature(run, allowed);
            bool same = m_BaseValid && sig == m_BaseSig && run.Wallet == m_BaseWallet;
            for (int i = 0; same && i < m_BaseLevels.Length; i++) same = m_BaseLevels[i] == run.Levels[i];
            if (!same)
            {
                m_BaseValid = true;
                m_BaseSig = sig;
                m_BaseWallet = run.Wallet;
                for (int i = 0; i < m_BaseLevels.Length; i++) m_BaseLevels[i] = run.Levels[i];
                var now = sim.Stats;
                for (int i = 0; i < m_Rows.Length; i++) RefreshBranchRow(sim, m_Rows[i], now, allowed, blocked);
            }
            RefreshBaseHint(done, allowed, blocked);
        }

        void RefreshBranchRow(OutpostSimulation sim, BranchRow row, TeamStats now, bool allowed, string blocked)
        {
            var run = sim.Run;
            var b = row.Branch;
            var mode = b == UpgradeBranch.Team ? BuyMode.One : m_BuyMode;
            var q = m_Game.Quote(b, mode);
            int level = q.CurrentLevel, cap = q.Cap, n = q.Levels;
            bool atCap = level >= cap || !q.NextCost.HasValue;

            var sb = m_Sb.Clear();
            sb.Append(OutpostUiText.BranchName(b)).Append("  ").Append(PixelUi.DimTag).Append(Loc.F("outpost.base.level", level, cap)).Append(ColorClose);
            if (n > 1) sb.Append(' ').Append(PixelUi.AccentTag).Append(Loc.F("outpost.base.levels_gain", n)).Append(ColorClose);
            Ui.SetText(row.Title, sb.ToString());

            if (atCap) Ui.SetText(row.Effect, OutpostUiText.EffectNow(b, now) + "  " + PixelUi.DimTag + Loc.T("outpost.base.max") + ColorClose);
            else
            {
                // «После» — характеристики ядра с уровнем после этой покупки (×1 — следующий уровень).
                int afterLevel = Math.Min(cap, level + Math.Max(1, n));
                for (int i = 0; i < m_LevelsBuf.Length; i++) m_LevelsBuf[i] = run.Levels[i];
                m_LevelsBuf[(int)b] = afterLevel;
                var after = TeamStats.Compute(run.Balance, m_LevelsBuf, sim.Profile, m_Game.Configs != null ? m_Game.Configs.Equipment : null, run.Zone,
                    m_Game.Configs?.Cards, run.ExtraWorkers);
                Ui.SetText(row.Effect, OutpostUiText.EffectChange(b, now, after));
            }

            row.Bar.SetValue(atCap ? 1f : (float)level / Mathf.Max(1, cap));
            row.Bar.SetFill(atCap ? PixelBarFill.Green : PixelBarFill.Gold);

            if (atCap)
            {
                Ui.SetActive(row.Price.Root, false);
                row.Buy.SetLabel(Loc.T("outpost.base.max"));
                row.Buy.SetInteractable(false);
                row.Reason = "cap";
                return;
            }
            row.Buy.SetLabel("");
            Ui.SetActive(row.Price.Root, true);
            // ×10/«Макс» — точная сумма фактического числа уровней; ни одного — цена следующего уровня красным.
            var price = n > 0 ? q.Cost : q.NextCost.Value;
            row.Price.SetText(OutpostUiText.Money(price));
            bool can = allowed && q.CanBuy;
            row.Buy.SetInteractable(can);
            row.Reason = !allowed ? blocked : q.BlockReason ?? (n == 0 ? "gold" : null);
            SetPriceColor(row, can ? 0 : (q.BlockReason == "gold" ? 1 : 2));
        }

        /// <summary>Цвет цены: 0 — тёмная на золоте, 1 — не хватает доставленного золота (красная), 2 — недоступно иначе.</summary>
        static void SetPriceColor(BranchRow row, int state)
        {
            if (state == row.PriceColor) return;
            row.PriceColor = state;
            bool on = state == 0;
            var t = row.Price.Text;
            t.color = state == 1 ? PixelUi.TextBad : (on ? PixelUi.TextDark : PixelUi.TextDim);
            PixelUi.SetShadow(t, !on);
            row.Price.Icon.color = on ? Color.white : PixelUi.DisabledIconTint;
        }

        /// <summary>Нижняя строка: причина (шахта завершена, офлайн) или наблюдаемое ограничение потока за ≈20 с.</summary>
        void RefreshBaseHint(bool done, bool allowed, string blocked)
        {
            int key;
            int percent = 0;
            OutpostFlowLimit limit = OutpostFlowLimit.Unknown;
            if (done) key = -1;
            else if (!allowed) key = -2 - (blocked != null ? blocked.GetHashCode() & 0xFFFF : 0);
            else
            {
                limit = m_Flow.Evaluate(out percent);
                key = (int)limit * 1000 + percent;
            }
            if (key == m_BaseHintKey) return;
            m_BaseHintKey = key;
            string text;
            Color color = PixelUi.TextDim;
            if (done)
            {
                text = Loc.T("outpost.base.done");
                color = PixelUi.TextBad;
            }
            else if (!allowed)
            {
                text = OutpostUiText.Reason(blocked);
                color = PixelUi.TextBad;
            }
            else
            {
                switch (limit)
                {
                    case OutpostFlowLimit.Queue: text = Loc.F("outpost.flow.queue", percent); break;
                    case OutpostFlowLimit.FullBags: text = Loc.F("outpost.flow.full_bags", percent); break;
                    case OutpostFlowLimit.Mining: text = Loc.F("outpost.flow.mining", percent); break;
                    case OutpostFlowLimit.Walking: text = Loc.F("outpost.flow.walking", percent); break;
                    case OutpostFlowLimit.Balanced: text = Loc.T("outpost.flow.balanced"); break;
                    default: text = Loc.T("outpost.flow.collecting"); break;
                }
            }
            Ui.SetText(m_BaseHint, text);
            if (m_BaseHint.color != color) m_BaseHint.color = color;
        }
    }
}
