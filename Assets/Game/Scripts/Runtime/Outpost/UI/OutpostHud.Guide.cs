using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Единственное обучение первой сессии (Docs/Rework/FIRST_SESSION.md) — как спасти Mia: короткий кадр клетки в начале шахты 1
    /// (камера к клетке, реплика Mia, ввод игрока прерывает) и после сцены спасения окно «Mia is free!» — теперь она ведёт магазин
    /// (её портрет сверху Store). Знакомств разделов, лапок и подсказок покупок нет: закрытые разделы объясняют себя замком на
    /// кнопке (OutpostHud.Progress). Реплика — <see cref="TutorialHint"/> без лапки (лучи не ловит); окно — <see cref="ShowIntro"/>.
    /// Чужие окна (итоги, офлайн, магазин, рынок, настройки, требование) откладывают показ; итоги шахты ждут окна Mia.
    /// </summary>
    public sealed partial class OutpostHud
    {
        const float GuideInterval = 0.1f;
        /// <summary>Кадр клетки: пауза после начала шахты, полёт камеры и время реплики.</summary>
        const float CalloutDelay = 1f, CalloutHold = 4f;

        TutorialHint m_Hint;
        float m_GuideNext;
        // Кадр клетки: когда можно начать, до какого времени держать, кадр начала (нажатие в нём не прерывает).
        float m_CalloutReady = -1f, m_CalloutUntil = -1f;
        int m_CalloutFrame;
        string m_CalloutRunId;
        OutpostRun m_CalloutRun;

        /// <summary>Реплика Mia на экране (тесты, съёмка); текст её пузыря.</summary>
        public bool GuideHintVisible => m_Hint != null && m_Hint.Visible;
        public string GuideHintText => m_Hint != null ? m_Hint.Text : null;
        /// <summary>Идёт кадр клетки в начале шахты 1.</summary>
        public bool RescueCalloutActive => m_CalloutUntil >= 0f;

        /// <summary>
        /// Реплика создаётся при первом показе (без правил первой сессии — playable, прежние тесты — её в HUD нет); последним
        /// ребёнком safe area: над панелями HUD, окна, открытые позже, — над ней.
        /// </summary>
        TutorialHint Hint
        {
            get
            {
                if (m_Hint != null) return m_Hint;
                m_Hint = TutorialHint.Create(m_Safe);
                m_Hint.name = "GuideHint";
                m_Hint.SetReserved(BottomUnits, TopUnits + Margin);
                return m_Hint;
            }
        }

        /// <summary>Итоги шахты ждут: окно Mia ещё впереди, идёт её сцена или открыто окно после спасения.</summary>
        bool GuideHoldsResults()
        {
            if (IntroOpen || EquipmentOpen || CardsOpen || RescueCalloutActive || (m_Game != null && m_Game.RescueSceneActive)) return true;
            var p = m_Game?.State?.Profile;
            return p != null && (RevealPending(p) || EquipmentIntroPending(p));
        }

        string PendingReveal(OutpostProfile p)
        {
            if (!ProgressionService.Active(p, m_Game.Configs)) return null;
            var cfg = m_Game.Configs.Progression;
            foreach (var rescue in new[] { cfg.Rescue, cfg.Blacksmith })
                if (rescue != null && p.Progression.HasCrew(rescue.Character) &&
                    !p.Progression.WasShown(ProgressionService.RevealPrefix + rescue.Character)) return rescue.Character;
            return null;
        }

        bool RevealPending(OutpostProfile p) => PendingReveal(p) != null;
        bool EquipmentIntroPending(OutpostProfile p) => ProgressionService.Active(p, m_Game.Configs) &&
            p.Progression.IsUnlocked(ProgressionConfig.Equipment) && !p.Progression.WasShown(ProgressionService.EquipmentIntroShown);

        /// <summary>Окно, при котором показ ждёт: итоги, офлайн, расчёт, ошибка, кампания, модальное, сундук, тайм-скип, настройки, магазин, рынок, требование.</summary>
        bool ForeignWindowOpen() =>
            ResultsOpen || FactoryDetailOpen || FactoryChestOpen || OfflineOpen || OfflineCalcOpen || ErrorOpen || CampaignOpen || ModalOpen || ChestRewardOpen || SkipResultOpen ||
            SettingsOpen || StoreOpen || MarketOpen || PreviewOpen;

        // ───────────── Кадр ─────────────

        /// <summary>Каждый кадр: кадр клетки (время и прерывание вводом); решение — не чаще 10 раз в секунду.</summary>
        void UpdateGuide()
        {
            if (m_CalloutUntil >= 0f)
            {
                if (!ReferenceEquals(m_CalloutRun,m_Game?.State?.Run) || m_Game?.Sim?.HasRescue != true)
                { EndCallout(false); return; }
                var pointer = Pointer.current;
                bool touched = Time.frameCount > m_CalloutFrame && pointer != null && pointer.press.wasPressedThisFrame;
                if (touched || AnyWindowOpen) EndCallout(false);
                else if (Time.unscaledTime >= m_CalloutUntil) EndCallout(true);
                return;
            }
            if (Time.unscaledTime < m_GuideNext) return;
            m_GuideNext = Time.unscaledTime + GuideInterval;
            var sim = m_Game != null ? m_Game.Sim : null;
            if (sim == null || sim.Run == null) return;
            DecideGuide(sim);
        }

        void DecideGuide(OutpostSimulation sim)
        {
            var p = sim.Profile;
            if (!ProgressionService.Active(p, m_Game.Configs) || InTransition || !CommandsAllowed(out _) || ForeignWindowOpen() || IntroOpen) return;
            bool idle = !AnyWindowOpen;
            // 1. Окно Mia — после сцены спасения, раньше итогов шахты.
            if (RevealPending(p))
            {
                if (idle && !m_Game.RescueSceneActive) ShowReveal(sim);
                return;
            }
            // The world objective is shown first; Cards waits until the camera callout has ended.
            var run = sim.Run;
            string character = sim.Map.Rescue?.Character;
            if (sim.HasRescue && sim.RescuePending && run.Phase == RunPhase.Running &&
                !p.Progression.WasShown(ProgressionService.CalloutKey(character)))
            {
                if (m_CalloutRunId != run.RunId)
                {
                    m_CalloutRunId = run.RunId;
                    m_CalloutReady = Time.unscaledTime + CalloutDelay;
                }
                if (idle && Time.unscaledTime >= m_CalloutReady) StartCallout();
                return;
            }
            if (!idle || m_Game.RescueSceneActive) return;
            if (EquipmentIntroPending(p))
            {
                ShowEquipmentIntro(sim);
                return;
            }
            if (run.Zone == 2 && run.Phase == RunPhase.Running && FeatureOpen(ProgressionConfig.Cards) &&
                !p.Progression.WasShown(ProgressionService.CardsIntroShown))
                ShowCardsSupport();
        }

        void ShowCardsSupport()
        {
            HideGuideHint();
            ShowIntro(Loc.T("outpost.cards.support.title"), "icon_cards_big", Loc.T("outpost.cards.support.body"),
                Loc.T("outpost.cards.support.open"), PixelButton.Blue, () =>
                {
                    m_Game.MarkProgress(ProgressionService.CardsIntroShown);
                    ShowCard(m_Game.Configs.Progression.CardsIntro.Card);
                });
            m_Intro.SetButton(1, Loc.T("outpost.preview.ok"), PixelButton.Dark, () =>
            {
                HideIntro();
                m_Game.MarkProgress(ProgressionService.CardsIntroShown);
            });
            m_IntroEntry.Back = () => { HideIntro(); m_Game.MarkProgress(ProgressionService.CardsIntroShown); };
        }

        void ShowEquipmentIntro(OutpostSimulation sim)
        {
            string item = ProgressionService.StarterItem(sim.Profile, m_Game.Configs);
            if (item == null || !m_Game.Configs.Equipment.TryGet(item, out var def)) return;
            var stats = EquipDamagePreview(sim, def);
            string body = Loc.F("outpost.equip.intro.body", OutpostUiText.ItemName(def),
                OutpostUiText.ItemBonus(m_Game.Configs.Equipment, def, sim.Profile.ItemLevel(item)), stats.Before, stats.After);
            ShowIntro(Loc.T("outpost.equip.intro.title"), "bruno_portrait_0", body,
                Loc.T("outpost.equip.intro.open"), PixelButton.Blue, () =>
                {
                    m_Game.MarkProgress(ProgressionService.EquipmentIntroShown);
                    ShowItem(item);
                });
            // Skipping only dismisses the explanation; access and the actual starter are already persisted.
            m_Intro.SetButton(1, Loc.T("outpost.equip.intro.later"), PixelButton.Dark, () =>
            {
                HideIntro();
                m_Game.MarkProgress(ProgressionService.EquipmentIntroShown);
            });
            m_IntroEntry.Back = () => { HideIntro(); m_Game.MarkProgress(ProgressionService.EquipmentIntroShown); };
        }

        // ───────────── Mia ─────────────

        /// <summary>
        /// «Mia is free!»: портрет, спасибо и что дальше — она ведёт магазин (её портрет сверху Store); пока магазин закрыт — строка
        /// его требования («Unlocks in Mine 2 with Cards»). Получена миграцией прежнего профиля — одно короткое уведомление.
        /// </summary>
        void ShowReveal(OutpostSimulation sim)
        {
            HideGuideHint();
            string character = PendingReveal(sim.Profile);
            string name = Loc.T("outpost.crew." + character);
            string portrait = PixelArt.Has(character + "_portrait_0") ? character + "_portrait_0" : "icon_" + character;
            string body = character == ProgressionConfig.BlacksmithCharacter ? Loc.T("outpost.rescue.bruno.body")
                : Loc.F(sim.HasRescue ? "outpost.rescue.reveal_body" : "outpost.rescue.notice_body", name);
            var store = m_Game.Configs.Progression.Feature(ProgressionConfig.Store);
            if (character != ProgressionConfig.BlacksmithCharacter && store != null && !FeatureOpen(ProgressionConfig.Store))
                body += "\n" + PixelUi.AccentTag + FeatureName(store) + ": " + FeatureRequirement(store) + ColorClose;
            ShowIntro(Loc.F("outpost.rescue.reveal_title", name), portrait, body, Loc.T("outpost.rescue.continue"), PixelButton.Green,
                () => m_Game.MarkProgress(ProgressionService.RevealPrefix + character));
        }

        /// <summary>
        /// Кадр клетки (≈ 5 с в начале шахты 1): камера плавно к клетке (при «Fewer effects» — сразу), реплика Mia без лапки; через
        /// <see cref="CalloutHold"/> с камера возвращается к лагерю. Касание игрока прерывает кадр и оставляет камеру ему.
        /// Отметка показа — сразу: перезапуск не повторяет кадр, цель остаётся в блоке HUD.
        /// </summary>
        void StartCallout()
        {
            m_CalloutRun = m_Game?.State?.Run;
            string character = m_Game.Sim.Map.Rescue?.Character;
            m_Game.MarkProgress(ProgressionService.CalloutKey(character));
            m_CalloutUntil = Time.unscaledTime + CalloutHold;
            m_CalloutFrame = Time.frameCount;
            if (m_Game.TryGetRescueCageWorldPosition(out var cage)) m_Game.FocusWorld(cage);
            var hint = Hint;
            hint.SetPortrait(character + "_portrait", 3);
            hint.PointDown = false;
            hint.Show(character == ProgressionConfig.BlacksmithCharacter ? Loc.T("outpost.rescue.bruno.callout")
                : Loc.F("outpost.rescue.callout", Loc.T("outpost.crew." + character)), null);
            if (AnyWindowOpen) hint.Rect.SetAsLastSibling();
        }

        void EndCallout(bool returnCamera)
        {
            m_CalloutUntil = -1f;
            HideGuideHint();
            if (m_Hint != null) m_Hint.SetPortrait();
            if (returnCamera && !InTransition) m_Game.FocusBase();
        }

        void HideGuideHint()
        {
            if (m_Hint != null && m_Hint.Visible) m_Hint.Hide();
        }

        // ───────────── События ─────────────

        /// <summary>Открылся раздел или ветка (FeatureUnlocked): замки нижней панели, бейдж и строки «Базы» пересчитываются сразу.</summary>
        void OnGuideEvent(OutpostEvent e)
        {
            if (e.Type != OutpostEventType.FeatureUnlocked) return;
            for (int i = 0; i < m_NextCostLevel.Length; i++) m_NextCostLevel[i] = int.MinValue;
            m_LockKey = -1;
            InvalidateBase();
            m_NextRefresh = 0f;
        }
    }
}
