using System;
using System.Collections.Generic;
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
    /// Панель «База» (ТЗ 9, 14; RUNTIME_SPEC §5; первая сессия — Docs/Rework/FIRST_SESSION.md): лист снизу с подписью «Resets next
    /// mine», переключателем ×1/×10/«Макс» и строками только открытых веток (ProgressionService) — плюс одна приглушённая строка
    /// «следующей» ветки с точным требованием, без кнопки покупки. Строка ветки: иконка, название, уровень L/cap и метка NEW (до
    /// первого просмотра), эффект «сейчас → после» с единицами — из TeamStats ядра для текущих уровней и уровней после покупки
    /// (TeamStats.Compute, без UI-формул), польза одной фразой и ближайший порог («★ Lv 10: ×2 damage» — его множитель уже входит
    /// в «после» и в цену), шкала уровня и цена из котировки ядра (×10/«Макс» — фактическое число уровней и точная сумма); на cap —
    /// зелёная шкала и «Макс.» (у Loading Bays — «места у склада кончились»). «Hire Cat» — только ×1. Строки — в прокрутке, высота
    /// листа — по строкам, не выше 62 % safe area. Внизу — подсказка ограничения потока (OutpostFlowStats). Тап по недоступной
    /// кнопке — короткая причина тостом (ТЗ 14).
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float BaseTopRow = 18 * Px, BaseRowHeight = 40 * Px, BaseRowGap = 2 * Px, BaseHintHeight = 18 * Px, BasePreviewHeight = 34 * Px;
        const float BuyWidth = 64 * Px, BuyHeight = 28 * Px;
        /// <summary>Лист «Базы» не выше этой доли safe area: поле над ним остаётся видно.</summary>
        const float BaseMaxShare = 0.62f;

        sealed class BranchRow
        {
            public UpgradeBranch Branch;
            public RectTransform Root;
            public TextMeshProUGUI Title, Effect, Benefit;
            public PixelBar Bar;
            public Ui.ButtonRefs Buy;
            public PixelIconText Price;
            /// <summary>Код причины недоступности кнопки (для тоста по тапу); null — доступна.</summary>
            public string Reason;
            public int PriceColor = -1;
            public bool Visible, New;
        }

        static readonly string[] s_ModeKeys = { "outpost.base.mode.one", "outpost.base.mode.ten", "outpost.base.mode.max" };

        PixelWindow m_Base;
        WindowEntry m_BaseEntry;
        readonly BranchRow[] m_Rows = new BranchRow[OutpostIds.BranchCount];
        readonly Ui.ButtonRefs[] m_ModeButtons = new Ui.ButtonRefs[3];
        BuyMode m_BuyMode = BuyMode.One;
        TextMeshProUGUI m_BaseHint;
        RectTransform m_BaseViewport, m_BaseContent;
        ScrollRect m_BaseScroll;
        // Строка «следующей» ветки: приглушённая иконка с замком, название и требование, польза.
        RectTransform m_BasePreview;
        Image m_BasePreviewIcon;
        TextMeshProUGUI m_BasePreviewTitle, m_BasePreviewNeed, m_BasePreviewBenefit;
        string m_BasePreviewFeature;
        readonly int[] m_LevelsBuf = new int[OutpostIds.BranchCount];
        // Сигнатура показанных строк: кошелёк, уровни, режим, фаза, доступность команд, версия экипировки.
        readonly int[] m_BaseLevels = new int[OutpostIds.BranchCount];
        BigInteger m_BaseWallet;
        int m_BaseSig = int.MinValue, m_BaseHintKey = int.MinValue, m_BuyClickFrame = -1;
        bool m_BaseValid;
        // Набор видимых строк и размер safe area, для которых посчитана раскладка листа.
        string m_BaseLayoutKey;
        Vector2 m_BaseLayoutSize;
        readonly List<string> m_SeenBuf = new List<string>(OutpostIds.BranchCount);

        /// <summary>Панель «База» открыта.</summary>
        public bool BaseOpen => m_Base != null && m_Base.Visible;

        /// <summary>Режим пакетной покупки панели (×1/×10/«Макс»); «Hire Cat» всегда ×1.</summary>
        public BuyMode BuyMode => m_BuyMode;

        /// <summary>Строка ветки показана в панели (ветка открыта первой сессией).</summary>
        public bool BaseRowVisible(UpgradeBranch branch) => m_Rows[(int)branch] != null && m_Rows[(int)branch].Visible;

        /// <summary>Ветка строки «следующей» (приглушённой) или null.</summary>
        public string BasePreviewFeature => m_BasePreview != null && m_BasePreview.gameObject.activeSelf ? m_BasePreviewFeature : null;

        void BuildBase()
        {
            m_Base = PixelUi.Window(m_Safe, "Base", Loc.T("outpost.base.title"), HideBase);
            m_BaseEntry = new WindowEntry { Root = m_Base.Root, Back = HideBase };
            Ui.SetActive(m_Base.BodyText, false);
            var bodyRt = m_Base.Body;

            // «Resets next mine» против «Сохраняется между шахтами» в «Экипировке» (ТЗ 9): покупки ветки — только на эту шахту.
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

            // Строки — в прокрутке между переключателем и подсказкой потока (прозрачная подложка ловит жест между строками).
            m_BaseViewport = Ui.Rect(bodyRt, "viewport").Anchor(0, 0, 1, 1, new Vector2(0f, BaseHintHeight + 3 * Px), new Vector2(0f, -(BaseTopRow + 3 * Px)));
            m_BaseViewport.gameObject.AddComponent<RectMask2D>();
            var catcher = m_BaseViewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            m_BaseContent = Ui.Rect(m_BaseViewport, "content");
            m_BaseContent.anchorMin = new Vector2(0f, 1f);
            m_BaseContent.anchorMax = new Vector2(1f, 1f);
            m_BaseContent.pivot = new Vector2(0.5f, 1f);
            m_BaseContent.offsetMin = m_BaseContent.offsetMax = Vector2.zero;
            m_BaseScroll = m_BaseViewport.gameObject.AddComponent<ScrollRect>();
            m_BaseScroll.content = m_BaseContent;
            m_BaseScroll.viewport = m_BaseViewport;
            m_BaseScroll.horizontal = false;
            m_BaseScroll.movementType = ScrollRect.MovementType.Clamped;
            m_BaseScroll.scrollSensitivity = 30;

            for (int i = 0; i < m_Rows.Length; i++) m_Rows[i] = BuildBranchRow(m_BaseContent, i);
            BuildBasePreview(m_BaseContent);

            var info = PixelUi.Icon(bodyRt, "hintIcon", "icon_info");
            info.rectTransform.anchorMin = info.rectTransform.anchorMax = new Vector2(0f, 0f);
            info.rectTransform.anchoredPosition = new Vector2(8 * Px, BaseHintHeight * 0.5f);
            m_BaseHint = PixelUi.Text(bodyRt, "hint", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Left, false, true);
            m_BaseHint.rectTransform.Anchor(0, 0, 1, 0, new Vector2(18 * Px, 0f), new Vector2(0f, BaseHintHeight));
            Ui.AutoSize(m_BaseHint, PixelUi.FontSmall * 0.75f, PixelUi.FontSmall);
            LayoutBase(true);
        }

        BranchRow BuildBranchRow(RectTransform parent, int index)
        {
            var branch = (UpgradeBranch)index;
            var row = new BranchRow { Branch = branch };
            var panel = PixelUi.Panel(parent, "branch" + index, PixelPanel.Inset);
            panel.raycastTarget = false;
            row.Root = panel.rectTransform;
            var icon = PixelUi.Icon(panel.transform, "icon", OutpostUiText.BranchIcon(branch));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(12 * Px, 2 * Px) + OutpostDialog.CenterOffset(OutpostUiText.BranchIcon(branch), 1);

            float right = BuyWidth + 8 * Px;
            row.Title = PixelUi.Text(panel.transform, "title", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            row.Title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -13 * Px), new Vector2(-right, -3 * Px));
            row.Title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Title, PixelUi.FontSmall, PixelUi.FontBody);
            row.Effect = PixelUi.Text(panel.transform, "effect", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            row.Effect.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -21 * Px), new Vector2(-right, -13 * Px));
            row.Effect.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Effect, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            // Польза одной фразой и ближайший порог ветки.
            row.Benefit = PixelUi.Text(panel.transform, "benefit", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Left, false, true);
            row.Benefit.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -29 * Px), new Vector2(-right, -21 * Px));
            row.Benefit.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(row.Benefit, PixelUi.FontSmall * 0.65f, PixelUi.FontSmall * 0.9f);

            // Общая шкала купленных уровней до фактического предела ветки, включая бонусы карточек.
            row.Bar = PixelUi.Bar(panel.transform, "levelProgress", PixelBarFill.Gold);
            row.Bar.Rect.Anchor(0, 0, 1, 0, new Vector2(24 * Px, 3 * Px), new Vector2(-right, 9 * Px));

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
            panel.gameObject.SetActive(false);
            return row;
        }

        void BuildBasePreview(RectTransform parent)
        {
            var panel = PixelUi.Panel(parent, "nextBranch", PixelPanel.Inset);
            panel.raycastTarget = false;
            panel.color = new Color(1f, 1f, 1f, 0.7f);
            m_BasePreview = panel.rectTransform;
            m_BasePreviewIcon = PixelUi.Icon(panel.transform, "icon", "icon_star");
            m_BasePreviewIcon.rectTransform.anchorMin = m_BasePreviewIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_BasePreviewIcon.color = PixelUi.DisabledIconTint;
            var lockIcon = PixelUi.Icon(panel.transform, "lock", PixelArt.Has("icon_lock_small") ? "icon_lock_small" : "icon_lock");
            lockIcon.rectTransform.anchorMin = lockIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            lockIcon.rectTransform.anchoredPosition = new Vector2(17 * Px, -5 * Px);
            m_BasePreviewTitle = PixelUi.Text(panel.transform, "title", "", PixelUi.FontBody, PixelUi.TextDim, TextAlignmentOptions.Left, true, true);
            m_BasePreviewTitle.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -13 * Px), new Vector2(-4 * Px, -3 * Px));
            m_BasePreviewTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_BasePreviewTitle, PixelUi.FontSmall * 0.8f, PixelUi.FontBody);
            m_BasePreviewNeed = PixelUi.Text(panel.transform, "requirement", "", PixelUi.FontSmall, PixelUi.TextAccentLight, TextAlignmentOptions.Left, false, true);
            m_BasePreviewNeed.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -22 * Px), new Vector2(-4 * Px, -13 * Px));
            m_BasePreviewNeed.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_BasePreviewNeed, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            m_BasePreviewBenefit = PixelUi.Text(panel.transform, "benefit", "", PixelUi.FontSmall, PixelUi.TextDim, TextAlignmentOptions.Left, false, true);
            m_BasePreviewBenefit.rectTransform.Anchor(0, 1, 1, 1, new Vector2(24 * Px, -30 * Px), new Vector2(-4 * Px, -22 * Px));
            m_BasePreviewBenefit.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_BasePreviewBenefit, PixelUi.FontSmall * 0.65f, PixelUi.FontSmall);
            panel.gameObject.SetActive(false);
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
            m_BaseScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Закрыть «Базу»: метки NEW показанных строк сняты (просмотр записан одной записью).</summary>
        public void HideBase()
        {
            if (m_Base == null) return;
            if (m_Base.Visible) MarkBaseRowsSeen();
            m_Base.Hide();
            Closed(m_BaseEntry);
        }

        public void ToggleBase()
        {
            if (BaseOpen) HideBase();
            else ShowBase();
        }

        void MarkBaseRowsSeen()
        {
            var p = m_Game?.State?.Profile;
            if (p == null || !ProgressionService.Active(p, m_Game.Configs)) return;
            m_SeenBuf.Clear();
            for (int i = 0; i < m_Rows.Length; i++)
                if (m_Rows[i].Visible && m_Rows[i].New) m_SeenBuf.Add(SeenKey(m_Rows[i].Branch));
            if (m_SeenBuf.Count == 0) return;
            // Все отметки — в профиль, запись — одна (последней отметкой через контракт).
            for (int i = 0; i < m_SeenBuf.Count - 1; i++) ProgressionService.MarkShown(p, m_SeenBuf[i]);
            m_Game.MarkProgress(m_SeenBuf[m_SeenBuf.Count - 1]);
            InvalidateBase();
        }

        /// <summary>Отметка «строка ветки просмотрена» (снимает NEW).</summary>
        static string SeenKey(UpgradeBranch branch) => "seen." + ProgressionConfig.BranchFeature(branch);

        /// <summary>Прокрутить лист так, чтобы строка ветки была видна целиком (указатель обучения).</summary>
        public void ScrollBaseTo(UpgradeBranch branch)
        {
            var row = m_Rows[(int)branch];
            if (!BaseOpen || row == null || !row.Visible) return;
            Canvas.ForceUpdateCanvases();
            float viewH = m_BaseViewport.rect.height, contentH = m_BaseContent.rect.height;
            if (contentH <= viewH + 0.5f) return;
            float top = -row.Root.offsetMax.y, bottom = top + BaseRowHeight;
            float y = m_BaseContent.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + viewH) y = bottom - viewH;
            m_BaseContent.anchoredPosition = new Vector2(m_BaseContent.anchoredPosition.x, Mathf.Clamp(y, 0f, contentH - viewH));
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
                var progression = m_Game.State?.Profile?.Progression;
                int marks = progression != null ? progression.Unlocked.Count * 37 + progression.Shown.Count * 101 : 0;
                int sig = (int)m_BuyMode * 7 + (allowed ? 1 : 0) * 31 + (int)run.Phase * 131 + m_EquipVersion * 1009 + run.Zone * 7919 + marks * 15013;
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
                var p = sim.Profile;
                bool active = ProgressionService.Active(p, m_Game.Configs);
                for (int i = 0; i < m_Rows.Length; i++)
                {
                    var row = m_Rows[i];
                    row.Visible = run.Balance.HasBranch(row.Branch) && ProgressionService.BranchAvailable(p, m_Game.Configs, row.Branch);
                    row.New = row.Visible && active && !p.Progression.WasShown(SeenKey(row.Branch));
                    Ui.SetActive(row.Root, row.Visible);
                    if (row.Visible) RefreshBranchRow(sim, row, now, allowed, blocked);
                }
                RefreshBasePreview(p, run);
            }
            LayoutBase(false);
            RefreshBaseHint(done, allowed, blocked);
        }

        /// <summary>Строка «следующей» ветки: первая неоткрытая ветка из правил, чьё превью уже можно показать; иначе скрыта.</summary>
        void RefreshBasePreview(OutpostProfile p, OutpostRun run)
        {
            FeatureDef next = null;
            var cfg = m_Game.Configs?.Progression;
            if (cfg != null && ProgressionService.Active(p, m_Game.Configs))
                foreach (var f in cfg.Features)
                {
                    if (f == null || !f.Branch.HasValue || p.Progression.IsUnlocked(f.Id) || !run.Balance.HasBranch(f.Branch.Value)) continue;
                    next = f;
                    break;
                }
            Ui.SetActive(m_BasePreview, next != null);
            m_BasePreviewFeature = next?.Id;
            if (next == null) return;
            string icon = OutpostUiText.BranchIcon(next.Branch.Value);
            PixelUi.SetIcon(m_BasePreviewIcon, icon);
            m_BasePreviewIcon.rectTransform.anchoredPosition = new Vector2(12 * Px, Px) + OutpostDialog.CenterOffset(icon, 1);
            Ui.SetText(m_BasePreviewTitle, Loc.T("outpost.base.next") + ": " + FeatureName(next));
            Ui.SetText(m_BasePreviewNeed, FeatureRequirement(next));
            Ui.SetText(m_BasePreviewBenefit, FeatureBenefit(next));
        }

        /// <summary>
        /// Раскладка листа по видимым строкам: строки сверху вниз в прокрутке, строка «следующей» ветки — последней; высота листа —
        /// по строкам, не выше <see cref="BaseMaxShare"/> safe area (дальше — прокрутка). Пересчёт — только при смене набора строк
        /// или размера safe area.
        /// </summary>
        void LayoutBase(bool force)
        {
            var sb = m_Sb.Clear();
            for (int i = 0; i < m_Rows.Length; i++) sb.Append(m_Rows[i].Visible ? '1' : '0');
            bool preview = m_BasePreview.gameObject.activeSelf;
            sb.Append(preview ? 'p' : '-');
            var size = m_Safe.rect.size;
            if (!force && size == m_BaseLayoutSize && string.Equals(m_BaseLayoutKey, sb.ToString(), StringComparison.Ordinal)) return;
            m_BaseLayoutKey = sb.ToString();
            m_BaseLayoutSize = size;

            float y = 0f;
            int shown = 0;
            foreach (int i in RowOrder())
            {
                var row = m_Rows[i];
                if (!row.Visible) continue;
                if (shown++ > 0) y += BaseRowGap;
                row.Root.Anchor(0, 1, 1, 1, new Vector2(0f, -(y + BaseRowHeight)), new Vector2(0f, -y));
                y += BaseRowHeight;
            }
            if (preview)
            {
                if (shown > 0) y += BaseRowGap + 2 * Px;
                m_BasePreview.Anchor(0, 1, 1, 1, new Vector2(2 * Px, -(y + BasePreviewHeight)), new Vector2(-2 * Px, -y));
                y += BasePreviewHeight;
            }
            m_BaseContent.sizeDelta = new Vector2(0f, y);

            float safeH = size.y > 100 * Px ? size.y : RefHeight;
            float chrome = WindowTop + BaseTopRow + 3 * Px + 3 * Px + BaseHintHeight + WindowBottom;
            float list = Mathf.Min(Mathf.Max(y, BasePreviewHeight), safeH * BaseMaxShare - chrome);
            float h = Mathf.Ceil((chrome + list) / (2 * Px)) * 2 * Px;
            // Лист снизу: открывается, не возвращая камеру к лагерю (ТЗ 5).
            m_Base.Box.Anchor(0.03f, 0f, 0.97f, 0f, new Vector2(0f, Margin), new Vector2(0f, Margin + h));
        }

        int[] m_RowOrder;

        /// <summary>
        /// Порядок строк — порядок открытия веток в progression.json (Mining Power, Hire Cat, Carry Capacity, Movement Speed…): новая
        /// ветка встаёт под уже знакомыми. Без правил первой сессии — порядок UpgradeBranch.
        /// </summary>
        int[] RowOrder()
        {
            if (m_RowOrder != null) return m_RowOrder;
            var order = new List<int>(m_Rows.Length);
            // HUD строится раньше загрузки конфигов: порядок запоминается только когда конфиги уже есть.
            var configs = m_Game?.Configs;
            var features = configs?.Progression?.Features;
            if (features != null)
                foreach (var f in features)
                    if (f != null && f.Branch.HasValue && !order.Contains((int)f.Branch.Value)) order.Add((int)f.Branch.Value);
            for (int i = 0; i < m_Rows.Length; i++)
                if (!order.Contains(i)) order.Add(i);
            var result = order.ToArray();
            if (configs != null) m_RowOrder = result;
            return result;
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
            if (row.New) sb.Append("  ").Append(PixelUi.AccentTag).Append(Loc.T("outpost.base.new")).Append(ColorClose);
            Ui.SetText(row.Title, sb.ToString());

            int afterLevel = Math.Min(cap, level + Math.Max(1, n));
            if (atCap)
            {
                string max = b == UpgradeBranch.Bays ? Loc.T("outpost.base.bays_max") : Loc.T("outpost.base.max");
                Ui.SetText(row.Effect, OutpostUiText.EffectNow(b, now) + "  " + PixelUi.DimTag + max + ColorClose);
            }
            else
            {
                // «После» — характеристики ядра с уровнем после этой покупки (×1 — следующий уровень); порог уже внутри.
                for (int i = 0; i < m_LevelsBuf.Length; i++) m_LevelsBuf[i] = run.Levels[i];
                m_LevelsBuf[(int)b] = afterLevel;
                var after = TeamStats.Compute(run.Balance, m_LevelsBuf, sim.Profile, m_Game.Configs != null ? m_Game.Configs.Equipment : null, run.Zone,
                    m_Game.Configs?.Cards, run.ExtraWorkers);
                Ui.SetText(row.Effect, OutpostUiText.EffectChange(b, now, after));
            }

            // Польза ветки и ближайший порог: «★ Lv 10: ×2 damage»; покупка, которая его достигает, — «★ ×2 damage!» акцентом.
            sb = m_Sb.Clear();
            sb.Append(OutpostUiText.BranchDescription(b));
            string bonus = OutpostUiText.MilestoneBonus(run.Balance, b);
            int? milestone = bonus != null ? run.Balance.NextMilestone(b, level) : null;
            if (milestone.HasValue && milestone.Value <= cap)
            {
                sb.Append(" — ").Append(PixelUi.AccentTag);
                if (!atCap && afterLevel >= milestone.Value) sb.Append(Loc.F("outpost.base.milestone_hit", bonus));
                else sb.Append(Loc.F("outpost.base.milestone_next", milestone.Value, bonus));
                sb.Append(ColorClose);
            }
            Ui.SetText(row.Benefit, sb.ToString());

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
            SetPriceColor(row, can ? 0 : (q.BlockReason == "gold" || n == 0 ? 1 : 2));
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
                // Совет — только открытая ветка (первая сессия): набор открытых веток входит в ключ.
                int open = 0;
                for (int i = 0; i < m_Rows.Length; i++)
                    if (m_Rows[i].Visible) open |= 1 << i;
                key = ((int)limit * 1000 + percent) * 128 + open;
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
                // Очередь у склада: с открытой Loading Bays совет — она, иначе Unloading Speed; ветка не открыта — без совета.
                switch (limit)
                {
                    case OutpostFlowLimit.Queue:
                        text = Loc.F(BaseRowVisible(UpgradeBranch.Bays) ? "outpost.flow.queue_bays"
                            : BaseRowVisible(UpgradeBranch.Unload) ? "outpost.flow.queue" : "outpost.flow.queue_plain", percent);
                        break;
                    case OutpostFlowLimit.FullBags: text = Loc.F(BaseRowVisible(UpgradeBranch.Bags) ? "outpost.flow.full_bags" : "outpost.flow.full_bags_plain", percent); break;
                    case OutpostFlowLimit.Mining: text = Loc.F(BaseRowVisible(UpgradeBranch.Pickaxes) ? "outpost.flow.mining" : "outpost.flow.mining_plain", percent); break;
                    case OutpostFlowLimit.Walking: text = Loc.F(BaseRowVisible(UpgradeBranch.Boots) ? "outpost.flow.walking" : "outpost.flow.walking_plain", percent); break;
                    case OutpostFlowLimit.Balanced: text = Loc.T("outpost.flow.balanced"); break;
                    default: text = Loc.T("outpost.flow.collecting"); break;
                }
            }
            Ui.SetText(m_BaseHint, text);
            if (m_BaseHint.color != color) m_BaseHint.color = color;
        }
    }
}
