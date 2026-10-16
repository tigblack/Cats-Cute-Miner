using System;
using System.Collections.Generic;
using CatMiners.Core;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Окна-диалоги (ТЗ 11, 13, 14; RUNTIME_SPEC §3.4–3.5): «Итоги шахты» (расчищено N/N, доставлено золота, предмет —
    /// новый или повтор → детали, детали за завершение, объяснение сброса; «Экипировка» и «Следующая шахта» / «Повторить
    /// сохранение» / «Далее» после последней шахты), «Пока вас не было» (уже начисленное золото и заглушка рекламного
    /// удвоения) и шкала расчёта офлайна, завершение кампании, окно ошибки загрузки/записи и общее модальное
    /// окно с очередью для контроллера. Итоги, офлайн, расчёт и ошибка открываются сами по состоянию контракта.
    /// </summary>
    public sealed partial class OutpostHud
    {
        /// <summary>Кнопка общего модального окна (<see cref="Modal(string, string, ModalButton[])"/>).</summary>
        public struct ModalButton
        {
            public string Label;
            public PixelButton Style;
            public Action Action;
            /// <summary>Эту кнопку нажимает Back; если такой нет, Back окно не закрывает (нужно решение).</summary>
            public bool Cancel;
        }

        sealed class ModalRequest
        {
            public string Title, Body, Icon;
            public ModalButton[] Buttons;
        }

        const int ModalMaxButtons = 3;

        /// <summary>Кнопки окна «Пока вас не было»: две строки на кнопке рекламы (подпись и иконка телевизора с «Watch ad»).</summary>
        const float OfflineButtonHeight = 36 * Px;
        OutpostDialog m_Results, m_Offline, m_Calc, m_Campaign, m_Error, m_Modal;
        WindowEntry m_ResultsEntry, m_OfflineEntry, m_CalcEntry, m_CampaignEntry, m_ErrorEntry, m_ModalEntry;
        PixelBar m_CalcBar;
        TextMeshProUGUI m_CalcText;
        int m_CalcPercent = -1;
        string m_ResultsAutoRunId;
        OfflineSummary m_OfflineShown;
        string m_ErrorShown;
        int m_ErrorRetryShown = -1;
        int m_ResultsButtonsKey = -1;
        ModalRequest m_ModalCurrent;
        readonly Queue<ModalRequest> m_ModalQueue = new Queue<ModalRequest>();
        readonly Action[] m_ModalActions = new Action[ModalMaxButtons];

        public bool ResultsOpen => m_Results != null && m_Results.Visible;
        public bool OfflineOpen => m_Offline != null && m_Offline.Visible;
        public bool OfflineCalcOpen => m_Calc != null && m_Calc.Visible;
        public bool CampaignOpen => m_Campaign != null && m_Campaign.Visible;
        public bool ErrorOpen => m_Error != null && m_Error.Visible;
        public bool ModalOpen => m_Modal != null && m_Modal.Visible;

        void BuildDialogs()
        {
            m_Results = OutpostDialog.Create(m_Safe, "Results", HideResults);
            m_Results.SetButton(0, Loc.T("outpost.results.equipment"), PixelButton.Blue, OnResultsEquipment);
            m_Results.SetButton(1, Loc.T("outpost.results.next"), PixelButton.Green, OnResultsNext);
            m_ResultsEntry = new WindowEntry { Root = m_Results.Root, Back = HideResults };
            AddDeniedTap(m_Results.ButtonAt(1), OnResultsNextDenied);

            // «Пока вас не было»: текст крупнее обычного (сумма — 200 % от него). Кнопка рекламы выше обычной: сверху
            // «Double income», под ним иконка телевизора и «Watch ad» — видно, что удвоение даётся за просмотр рекламы.
            m_Offline = OutpostDialog.Create(m_Safe, "Offline");
            m_Offline.BodyFontSize = PixelUi.FontLarge;
            m_Offline.Window.ButtonsHeight = OfflineButtonHeight;
            var ad = m_Offline.SetButton(0, Loc.T("outpost.offline.double"), PixelButton.Gold, OnOfflineDouble);
            ad.Label.rectTransform.Anchor(0, 0.5f, 1, 1, new Vector2(4 * Px, 0f), new Vector2(-4 * Px, -Px));
            var watchAd = PixelUi.IconText(ad.Content, "watchAd", "icon_ad_tv", Loc.T("outpost.offline.ad"), PixelUi.FontLarge, ad.Label.color, true, false);
            watchAd.Root.anchorMin = watchAd.Root.anchorMax = new Vector2(0.5f, 0f);
            watchAd.Root.pivot = new Vector2(0.5f, 0f);
            watchAd.Root.anchoredPosition = new Vector2(0f, 2 * Px);
            m_Offline.SetButton(1, Loc.T("outpost.offline.ok"), PixelButton.Green, DismissOfflineWindow);
            m_OfflineEntry = new WindowEntry { Root = m_Offline.Root, Back = DismissOfflineWindow };

            // Расчёт офлайна: окно без кнопок и без закрытия — покупки и переход до commit недоступны (ТЗ 13).
            m_Calc = OutpostDialog.Create(m_Safe, "OfflineCalc");
            var extra = m_Calc.UseExtra(18 * Px);
            m_CalcBar = PixelUi.Bar(extra, "bar", PixelBarFill.Gold);
            m_CalcBar.Rect.Anchor(0, 1, 1, 1, new Vector2(4 * Px, -8 * Px), new Vector2(-4 * Px, 0f));
            m_CalcText = PixelUi.Text(extra, "percent", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Center, true, true);
            m_CalcText.rectTransform.Anchor(0, 0, 1, 0, Vector2.zero, new Vector2(0f, 9 * Px));
            m_CalcText.textWrappingMode = TextWrappingModes.NoWrap;
            m_CalcEntry = new WindowEntry { Root = m_Calc.Root, Back = null };

            m_Campaign = OutpostDialog.Create(m_Safe, "CampaignComplete", HideCampaign);
            m_Campaign.Window.Shade.color = new Color(PixelPalette.Ink0.r, PixelPalette.Ink0.g, PixelPalette.Ink0.b, 0.94f);
            m_Campaign.SetButton(0, Loc.T("outpost.results.equipment"), PixelButton.Blue, () => ShowEquipment());
            m_Campaign.SetButton(1, Loc.T("outpost.campaign.close"), PixelButton.Dark, HideCampaign);
            m_CampaignEntry = new WindowEntry { Root = m_Campaign.Root, Back = HideCampaign };

            m_Error = OutpostDialog.Create(m_Safe, "Error");
            m_ErrorEntry = new WindowEntry { Root = m_Error.Root, Back = null };

            m_Modal = OutpostDialog.Create(m_Safe, "Modal");
            m_ModalEntry = new WindowEntry { Root = m_Modal.Root, Back = null };
            for (int i = 0; i < ModalMaxButtons; i++)
            {
                int k = i;
                m_ModalActions[i] = () => OnModalButton(k);
            }
        }

        void RelayoutDialogs()
        {
            m_Results.Relayout();
            m_Offline.Relayout();
            m_Calc.Relayout();
            m_Campaign.Relayout();
            m_Error.Relayout();
            m_Modal.Relayout();
            m_ChestReward?.Relayout();
            m_DropRates?.Relayout();
            m_SkipResult?.Relayout();
        }

        /// <summary>
        /// Окна по состоянию контракта (10 Гц): ошибка (Phase.Error) → шкала расчёта (OfflineCatchUp) → «Пока вас не было»
        /// (PendingOffline уже записан) → «Итоги шахты» (ReadyToContinue, один раз на run; кнопка в полосе — переход дальше).
        /// Итоги ждут закрытия окна сундука: сундук выхода с новой карточкой открывается вместе с завершением шахты.
        /// </summary>
        void UpdateAutoWindows()
        {
            if (m_Game == null) return;
            var phase = m_Game.Phase;

            if (phase == OutpostControllerPhase.Error) ShowError();
            else if (ErrorOpen) HideDialog(m_Error, m_ErrorEntry);

            if (phase == OutpostControllerPhase.OfflineCatchUp) ShowOfflineCalc();
            else if (OfflineCalcOpen) HideDialog(m_Calc, m_CalcEntry);

            var pending = m_Game.PendingOffline;
            if (pending == null && OfflineOpen) HideDialog(m_Offline, m_OfflineEntry);
            if (pending != null && phase == OutpostControllerPhase.Playing && !ReferenceEquals(pending, m_OfflineShown)) ShowOffline(pending);

            // Итог тайм-скипа магазина (ТЗ 10.5): после записи расчёта, до итогов шахты.
            var skip = m_Game.PendingSkip;
            if (skip == null && SkipResultOpen) HideDialog(m_SkipResult, m_SkipResultEntry);
            if (skip != null && phase == OutpostControllerPhase.Playing && !ReferenceEquals(skip, m_SkipResultShown)) ShowSkipResult(skip);

            var sim = m_Game.Sim;
            var run = sim != null ? sim.Run : null;
            if (run != null && phase == OutpostControllerPhase.Playing && run.Phase == RunPhase.ReadyToContinue && pending == null && skip == null &&
                !ChestRewardOpen && !m_Game.InTransition && !InTransition && m_ResultsAutoRunId != run.RunId)
            {
                m_ResultsAutoRunId = run.RunId;
                ShowResults();
            }
        }

        void HideDialog(OutpostDialog dialog, WindowEntry entry)
        {
            dialog.Hide();
            Closed(entry);
        }

        // ───────────── Итоги шахты ─────────────

        /// <summary>
        /// «Итоги шахты» (ТЗ 11.1–11.3): награда уже выдана и записана ядром (CompletionReward), окно её только показывает —
        /// повторное открытие ничего не бросает заново.
        /// </summary>
        public void ShowResults()
        {
            var sim = m_Game != null ? m_Game.Sim : null;
            var state = m_Game != null ? m_Game.State : null;
            if (sim == null || state == null || sim.Run == null) return;
            var run = sim.Run;
            // Показ вручную (контроллер на StageCompleted) тоже считается: автооткрытие не повторит окно этой шахты.
            if (run.Phase == RunPhase.ReadyToContinue) m_ResultsAutoRunId = run.RunId;
            var catalog = m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            var reward = StageProgressionService.CompletionReward(state);
            EquipmentItemDef item = null;
            if (reward != null && catalog != null) catalog.TryGet(reward.ItemId, out item);

            m_Results.Begin(Loc.F("outpost.results.title", run.Zone), item != null ? OutpostUiText.ItemIcon(item, true) : "icon_star", Loc.T("outpost.results.reset"));
            int destroyed = 0;
            for (int i = 0; i < run.Targets.Count; i++)
                if (run.Targets[i].Destroyed) destroyed++;
            m_Results.AddRow("icon_pickaxe", Loc.F("outpost.results.cleared", destroyed, sim.Map.RequiredCount), PixelUi.TextColor);
            m_Results.AddRow("icon_coin", Loc.F("outpost.results.gold", OutpostUiText.Money(run.TotalCredited)), PixelUi.TextAccentLight);
            // ТЗ 2.2 §4.6: золото продаж товара (Run.GoodsSoldGold) — отдельной строкой под монетами, только если было.
            if (run.GoodsSoldGold.Sign > 0)
                m_Results.AddRow("icon_market", Loc.F("outpost.results.goods", OutpostUiText.Money(run.GoodsSoldGold)), PixelUi.TextAccentLight);
            if (reward != null && item != null)
            {
                if (!reward.Duplicate)
                    m_Results.AddRow(OutpostUiText.ItemIcon(item, false), Loc.F("outpost.results.item_new", OutpostUiText.ItemName(item)), PixelUi.TextAccentLight);
                else
                    m_Results.AddRow(OutpostUiText.ItemIcon(item, false),
                        Loc.F("outpost.results.item_duplicate", OutpostUiText.ItemName(item), OutpostUiText.PartsGain(reward.DuplicateParts)), PixelUi.TextColor);
                if (reward.CompletionParts > 0)
                    m_Results.AddRow("icon_parts", Loc.F("outpost.results.completion_parts", OutpostUiText.PartsGain(reward.CompletionParts)), PixelUi.TextColor);
            }
            else m_Results.AddRow("icon_info", Loc.T("outpost.results.no_reward"), PixelUi.TextDim);
            if (run.Counters.CardsGained != 0 || run.Counters.ElixirGained != 0)
                m_Results.AddRow("icon_cards", Loc.F("cards.results", run.Counters.CardsGained, run.Counters.ElixirGained), PixelUi.TextColor);
            if (m_Game.CampaignComplete) m_Results.AddRow("icon_star", Loc.T("outpost.results.campaign"), PixelUi.TextAccent);
            m_ResultsButtonsKey = -1;
            RefreshResultsButtons();
            m_Results.Show();
            Opened(m_ResultsEntry);
        }

        public void HideResults()
        {
            if (m_Results == null) return;
            HideDialog(m_Results, m_ResultsEntry);
        }

        /// <summary>Кнопки итогов: «Экипировка» и «Следующая шахта» / «Повторить сохранение» / «Далее» (после zone144).</summary>
        void RefreshResultsButtons()
        {
            if (m_Game == null) return;
            bool campaign = m_Game.CampaignComplete, failed = m_Game.SaveFailed;
            bool busy = InTransition || m_Game.InTransition || m_Game.Phase == OutpostControllerPhase.Transitioning;
            bool allowed = CommandsAllowed(out _);
            int key = (campaign ? 1 : 0) | (failed ? 2 : 0) | (busy ? 4 : 0) | (m_Game.CanContinue ? 8 : 0) | (allowed ? 16 : 0);
            if (key == m_ResultsButtonsKey) return;
            m_ResultsButtonsKey = key;
            m_Results.SetButton(0, Loc.T("outpost.results.equipment"), PixelButton.Blue, OnResultsEquipment).SetInteractable(allowed);
            Ui.ButtonRefs next;
            if (failed) next = m_Results.SetButton(1, Loc.T("outpost.results.retry"), PixelButton.Red, OnResultsRetry);
            else if (campaign) next = m_Results.SetButton(1, Loc.T("outpost.results.finish"), PixelButton.Gold, OnResultsFinish);
            else next = m_Results.SetButton(1, Loc.T("outpost.results.next"), PixelButton.Green, OnResultsNext);
            next.SetInteractable(failed || campaign || (m_Game.CanContinue && !busy));
        }

        void OnResultsEquipment() => ShowEquipment();

        void OnResultsNext()
        {
            if (m_Game == null) return;
            if (!m_Game.CanContinue || InTransition || m_Game.InTransition)
            {
                Denied(m_Game.InTransition || InTransition ? "transition" : "phase");
                return;
            }
            // Повторный тап блокируется контроллером (transitionId) и здесь — кнопка недоступна до конца перехода.
            m_Game.ContinueToNextMine();
            m_ResultsButtonsKey = -1;
            RefreshResultsButtons();
        }

        void OnResultsNextDenied()
        {
            if (m_Game == null) return;
            bool busy = InTransition || m_Game.InTransition || m_Game.Phase == OutpostControllerPhase.Transitioning;
            Toast(OutpostUiText.Reason(busy ? "transition" : !CommandsAllowed(out string reason) ? reason : "phase"));
        }

        void OnResultsRetry()
        {
            m_Game?.RetrySave();
            m_ResultsButtonsKey = -1;
            RefreshResultsButtons();
        }

        void OnResultsFinish()
        {
            HideResults();
            ShowCampaign();
        }

        // ───────────── Завершение кампании ─────────────

        /// <summary>Конец доступной кампании (после zone144): экипировка и настройки доступны, кнопки следующей шахты нет.</summary>
        public void ShowCampaign()
        {
            if (m_Campaign == null) return;
            m_Campaign.Begin(Loc.T("outpost.campaign.title"), "icon_star", Loc.F("outpost.campaign.body", OutpostIds.LastZone));
            m_Campaign.Show();
            Opened(m_CampaignEntry);
        }

        public void HideCampaign()
        {
            if (m_Campaign == null) return;
            HideDialog(m_Campaign, m_CampaignEntry);
        }

        // ───────────── Офлайн ─────────────

        /// <summary>
        /// «Пока вас не было» (ТЗ 13): результат уже применён и записан одной транзакцией. В окне только сумма золота,
        /// включая ноль (шрифт <see cref="PixelUi.FontLarge"/>, сумма — 72); слева рекламная заглушка с иконкой телевизора,
        /// справа подтверждение. Остальные награды остаются в состоянии игры.
        /// </summary>
        public void ShowOffline(OfflineSummary sum)
        {
            if (sum == null) return;
            m_OfflineShown = sum;
            string gold = PixelUi.AccentTag + "<size=200%>" + OutpostUiText.Money(sum.GoldDelivered) + "</size></color>";
            m_Offline.Begin(Loc.T("outpost.offline.title"), "icon_coin", Loc.F("outpost.offline.gold", gold));
            m_Offline.Show();
            Opened(m_OfflineEntry);
        }

        /// <summary>
        /// «Double income»: в тестовом режиме рекламы (store_catalog.json → TestPurchases) золото офлайна начисляется ещё раз и
        /// окно закрывается; без него — «Ads are coming soon».
        /// </summary>
        void OnOfflineDouble()
        {
            var result = m_Game != null ? m_Game.ClaimOfflineDouble() : CommandResult.Fail("unavailable");
            if (result.Ok)
            {
                // Сводка забрана контроллером; окно закрывается сразу, тост о доходе — по событию BonusGold.
                if (!OutpostController.AndroidRuntime && OfflineOpen) HideDialog(m_Offline, m_OfflineEntry);
                m_NextRefresh = 0f;
                return;
            }
            if (result.Reason == "unavailable") Toast(Loc.T("outpost.offline.ad_soon"));
            else Denied(result.Reason);
        }

        void DismissOfflineWindow()
        {
            HideDialog(m_Offline, m_OfflineEntry);
            m_Game?.DismissOffline();
            m_NextRefresh = 0f;
        }

        void ShowOfflineCalc()
        {
            int percent = Mathf.Clamp(Mathf.FloorToInt((m_Game != null ? m_Game.OfflineProgress01 : 0f) * 100f), 0, 100);
            if (!m_Calc.Visible)
            {
                int skipMinutes = m_Game != null ? m_Game.SkipMinutesInProgress : 0;
                if (skipMinutes > 0)
                    m_Calc.Begin(Loc.T("outpost.store.skip_calc_title"), "icon_clock", Loc.F("outpost.store.skip_calc_body", OutpostUiText.SkipDuration(skipMinutes)));
                else m_Calc.Begin(Loc.T("outpost.offline.calc_title"), "icon_clock", Loc.T("outpost.offline.calc_body"));
                m_Calc.Show();
                Opened(m_CalcEntry);
                m_CalcPercent = -1;
            }
            if (percent == m_CalcPercent) return;
            m_CalcPercent = percent;
            m_CalcBar.SetValue(percent / 100f);
            Ui.SetText(m_CalcText, Loc.F("outpost.offline.calc_percent", percent));
        }

        // ───────────── Ошибка ─────────────

        /// <summary>
        /// Окно ошибки загрузки/записи (Phase.Error): фраза для игрока из словаря, под ней приглушённо — технический текст
        /// контроллера (для отчёта об ошибке); «Повторить», если не удалась запись.
        /// </summary>
        void ShowError()
        {
            int retry = m_Game.SaveFailed ? 1 : 0;
            string text = Loc.T(retry == 1 ? "outpost.error.save" : "outpost.error.unknown");
            if (!string.IsNullOrEmpty(m_Game.ErrorText)) text += "\n" + PixelUi.DimTag + m_Game.ErrorText + ColorClose;
            if (m_Error.Visible && string.Equals(text, m_ErrorShown, StringComparison.Ordinal) && retry == m_ErrorRetryShown) return;
            m_ErrorShown = text;
            m_ErrorRetryShown = retry;
            m_Error.Begin(Loc.T("outpost.error.title"), "icon_warning", text);
            if (retry == 1) m_Error.SetButton(0, Loc.T("outpost.error.retry"), PixelButton.Red, OnErrorRetry);
            else m_Error.HideButtonsFrom(0);
            m_Error.Show();
            Opened(m_ErrorEntry);
        }

        void OnErrorRetry()
        {
            m_Game?.RetrySave();
            m_NextRefresh = 0f;
        }

        // ───────────── Общее модальное окно ─────────────

        /// <summary>
        /// Модальное окно для контроллера (вопрос о новой кампании, сообщения): если уже открыто другое — в очередь.
        /// Кнопка закрывает окно и затем вызывает своё действие; Back нажимает кнопку с <see cref="ModalButton.Cancel"/>.
        /// </summary>
        public void Modal(string title, string body, params ModalButton[] buttons) => Modal(title, body, null, buttons);

        /// <summary>Модальное окно с крупной иконкой ×2 над текстом.</summary>
        public void Modal(string title, string body, string icon, params ModalButton[] buttons)
        {
            var request = new ModalRequest { Title = title, Body = body, Icon = icon, Buttons = buttons ?? Array.Empty<ModalButton>() };
            if (m_ModalCurrent != null) m_ModalQueue.Enqueue(request);
            else OpenModal(request);
        }

        void OpenModal(ModalRequest r)
        {
            m_ModalCurrent = r;
            m_Modal.Begin(r.Title, r.Icon, r.Body);
            int n = Mathf.Min(r.Buttons.Length, ModalMaxButtons);
            bool cancel = false;
            for (int i = 0; i < n; i++)
            {
                m_Modal.SetButton(i, r.Buttons[i].Label, r.Buttons[i].Style, m_ModalActions[i]);
                cancel |= r.Buttons[i].Cancel;
            }
            m_Modal.HideButtonsFrom(n);
            m_ModalEntry.Back = cancel ? (Action)OnModalBack : null;
            m_Modal.Show();
            Opened(m_ModalEntry);
        }

        void OnModalButton(int index)
        {
            var r = m_ModalCurrent;
            // Действие берётся до закрытия: CloseModal может сразу открыть следующее окно очереди.
            var action = r != null && index < r.Buttons.Length ? r.Buttons[index].Action : null;
            CloseModal();
            action?.Invoke();
        }

        void OnModalBack()
        {
            var r = m_ModalCurrent;
            if (r == null) return;
            for (int i = 0; i < r.Buttons.Length; i++)
                if (r.Buttons[i].Cancel)
                {
                    OnModalButton(i);
                    return;
                }
        }

        void CloseModal()
        {
            m_ModalCurrent = null;
            HideDialog(m_Modal, m_ModalEntry);
            if (m_ModalQueue.Count > 0) OpenModal(m_ModalQueue.Dequeue());
        }
    }
}
