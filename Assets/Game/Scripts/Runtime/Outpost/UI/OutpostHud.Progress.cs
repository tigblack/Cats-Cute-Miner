using System;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Первая сессия в HUD (Docs/Rework/FIRST_SESSION.md): Store, Cards, Equipment и Sell Goods всегда на нижней панели, «База» —
    /// по центру. Закрытый раздел (progression.json) — чуть затемнённая кнопка с крупным ярким замком вместо иконки; кнопка нажимается и
    /// открывает окно требования: польза раздела и шахта открытия («Unlocks in Mine 2»; Store — вместе с Cards). Блок цели слева под
    /// табличкой шахты — спасение Mia с шагом по фактам карты (тап — камера к фронту расчистки). Окно после спасения показывает
    /// OutpostHud.Guide. Обучения разделов нет.
    /// </summary>
    public sealed partial class OutpostHud
    {
        public const string TargetObjective = "ui.objective";
        const float ObjectiveHeight = 30 * Px;
        /// <summary>Ширина блока цели — доля ширины safe area (слева, под табличкой шахты и полосой).</summary>
        const float ObjectiveWidth = 0.6f;
        /// <summary>Лёгкое затемнение фона и подписи закрытой кнопки; центральный замок остаётся ярким.</summary>
        static readonly Color LockedButtonTint = new Color(0.82f, 0.82f, 0.82f, 1f);

        RectTransform m_ObjectiveRoot;
        Ui.ButtonRefs m_Objective;
        TextMeshProUGUI m_ObjectiveTitle, m_ObjectiveStep;
        PixelBar m_ObjectiveBar;
        Image m_ObjectiveIcon;
        int m_ObjectiveKey = int.MinValue;
        /// <summary>Сколько места под верхней полосой занимает блок цели: тост — ниже.</summary>
        float m_ObjectiveReserve;
        OutpostDialog m_Preview, m_Intro;
        WindowEntry m_PreviewEntry, m_IntroEntry;
        string m_PreviewFeature;
        Image m_MarketLock, m_StoreLock, m_CardsLock, m_EquipLock;
        int m_LockKey = -1;

        /// <summary>Блок цели первой сессии на экране (тесты, съёмка).</summary>
        public bool ObjectiveVisible => m_Objective != null && m_Objective.Image.gameObject.activeSelf;
        public string ObjectiveTitle => ObjectiveVisible ? m_ObjectiveTitle.text : null;
        /// <summary>Текст шага блока цели («Clear the route 3/7») или null.</summary>
        public string ObjectiveStep => ObjectiveVisible ? m_ObjectiveStep.text : null;
        public bool PreviewOpen => m_Preview != null && m_Preview.Visible;
        /// <summary>Раздел (FeatureDef.Id) открытого окна требования или null.</summary>
        public string PreviewFeature => PreviewOpen ? m_PreviewFeature : null;
        public bool IntroOpen => m_Intro != null && m_Intro.Visible;
        /// <summary>Заголовок открытого окна после спасения (тесты, съёмка) или null.</summary>
        public string IntroTitle => IntroOpen ? m_Intro.Window.Title.text : null;

        // ───────────── Состояние разделов ─────────────

        /// <summary>Раздел <paramref name="feature"/> (FeatureDef.Id) открыт; без правил первой сессии (playable, прежние профили) — всегда.</summary>
        public bool FeatureOpen(string feature) => ProgressionService.IsAvailable(m_Game?.State?.Profile, m_Game?.Configs, feature);

        /// <summary>Имя раздела для игрока: ветка — её название, иначе outpost.feature.&lt;id&gt;.</summary>
        public static string FeatureName(FeatureDef f)
        {
            if (f == null) return "";
            if (f.Branch.HasValue) return OutpostUiText.BranchName(f.Branch.Value);
            return Loc.T("outpost.feature." + f.Id);
        }

        static string FeatureBenefit(FeatureDef f)
        {
            if (f == null) return "";
            if (f.Branch.HasValue) return OutpostUiText.BranchDescription(f.Branch.Value) + ".";
            return Loc.T("outpost.feature_benefit." + f.Id);
        }

        /// <summary>
        /// Точное требование открытия по правилу раздела из progression.json: шахта («Unlocks in Mine 3»), первая доставка, первая
        /// покупка Mining Power, вместе с другим разделом («Unlocks in Mine 2 with Cards»), первая шахта с заводом (Sell Goods — по
        /// картам шахт, как кнопка).
        /// </summary>
        internal string FeatureRequirement(FeatureDef f)
        {
            if (f == null) return "";
            if (f.Id == ProgressionConfig.Equipment) return Loc.T("outpost.unlock.blacksmith");
            if (f.Id == ProgressionConfig.SellGoods)
            {
                int zone = m_Game != null ? m_Game.MarketUnlockZone : 0;
                return zone > 0 ? Loc.F("outpost.unlock.factories", zone) : FactoryText.Reason("no-factories");
            }
            if (f.With != null)
            {
                var linkedFeature = m_Game?.Configs?.Progression?.Feature(f.With);
                return linkedFeature != null ? Loc.F("outpost.unlock.with", FeatureRequirement(linkedFeature), FeatureName(linkedFeature)) : "";
            }
            if (f.Event == ProgressionConfig.EventFirstDelivery) return Loc.T("outpost.unlock.first_delivery");
            if (f.Event == ProgressionConfig.EventFirstPickaxes) return Loc.T("outpost.unlock.first_pickaxes");
            return Loc.F("outpost.unlock.zone", f.Zone);
        }

        /// <summary>Иконка раздела: мелкая (строка «Next upgrade») или крупная («_big» — окно требования), по имени из правил.</summary>
        internal static string FeatureIcon(FeatureDef f, bool big = false)
        {
            if (f == null || string.IsNullOrEmpty(f.Icon)) return "icon_star";
            string small = f.Icon.EndsWith("_big", StringComparison.Ordinal) ? f.Icon.Substring(0, f.Icon.Length - 4) : f.Icon;
            string name = big && PixelArt.Has(small + "_big") ? small + "_big" : small;
            if (PixelArt.Has(name)) return name;
            return PixelArt.Has(f.Icon) ? f.Icon : "icon_star";
        }

        // ───────────── Сборка ─────────────

        void BuildProgress()
        {
            // Блок цели: слева под табличкой шахты; кнопка — тап подводит камеру к фронту расчистки.
            m_ObjectiveRoot = Ui.Rect(m_Safe, "Objective").Anchor(0, 1, ObjectiveWidth, 1,
                new Vector2(Margin, -(TopUnits + Margin + ObjectiveHeight)), new Vector2(0f, -(TopUnits + Margin)));
            m_Objective = PixelUi.Button(m_ObjectiveRoot, "objective", "", PixelButton.Dark, OnObjectiveTap, PixelUi.FontBody);
            m_Objective.Rect.Anchor(0, 1, 1, 1, new Vector2(0f, -ObjectiveHeight), Vector2.zero);
            var face = m_Objective.Content;
            m_ObjectiveIcon = PixelUi.Icon(face, "portrait", "icon_mia");
            m_ObjectiveIcon.rectTransform.anchorMin = m_ObjectiveIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            m_ObjectiveIcon.rectTransform.anchoredPosition = new Vector2(4 * Px + 8 * Px, Px) + OutpostDialog.CenterOffset("icon_mia", 1);
            m_ObjectiveTitle = PixelUi.Text(face, "title", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Left, true, true);
            m_ObjectiveTitle.rectTransform.Anchor(0, 1, 1, 1, new Vector2(23 * Px, -13 * Px), new Vector2(-4 * Px, -3 * Px));
            m_ObjectiveTitle.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_ObjectiveTitle, PixelUi.FontSmall, PixelUi.FontBody);
            m_ObjectiveStep = PixelUi.Text(face, "step", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Left, false, true);
            m_ObjectiveStep.rectTransform.Anchor(0, 0, 1, 0, new Vector2(23 * Px, 9 * Px), new Vector2(-4 * Px, 17 * Px));
            m_ObjectiveStep.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_ObjectiveStep, PixelUi.FontSmall * 0.75f, PixelUi.FontSmall);
            m_ObjectiveBar = PixelUi.Bar(face, "progress", PixelBarFill.Gold);
            m_ObjectiveBar.Rect.Anchor(0, 0, 1, 0, new Vector2(23 * Px, 3 * Px), new Vector2(-4 * Px, 9 * Px));
            m_Objective.SetActive(false);
            m_Targets[TargetObjective] = m_Objective.Rect;

            // Окна требования и после спасения — общий вид диалога HUD.
            m_Preview = OutpostDialog.Create(m_Safe, "FeaturePreview", HidePreview);
            m_PreviewEntry = new WindowEntry { Root = m_Preview.Root, Back = HidePreview };
            m_Intro = OutpostDialog.Create(m_Safe, "FeatureIntro");
            m_IntroEntry = new WindowEntry { Root = m_Intro.Root, Back = null };

            // Закрытые разделы: крупный замок занимает место иконки, подпись остаётся видна.
            m_MarketLock = FeatureLock(m_MarketButton);
            m_StoreLock = FeatureLock(m_StoreButton);
            m_CardsLock = FeatureLock(m_CardsButton);
            m_EquipLock = FeatureLock(m_EquipButton);
        }

        static Image FeatureLock(Ui.ButtonRefs button)
        {
            if (button == null || !PixelArt.Has("icon_lock")) return null;
            var img = PixelUi.Icon(button.Content, "lock", "icon_lock");
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24 * Px, 24 * Px);
            rt.anchoredPosition = new Vector2(0f, 6 * Px);
            img.preserveAspect = true;
            img.gameObject.SetActive(false);
            return img;
        }

        // ───────────── Нижняя панель ─────────────

        /// <summary>
        /// Замки Store, Cards и Equipment по отметкам профиля (Sell Goods — <see cref="RefreshMarketButton"/>). Пересчёт — только при
        /// смене открытия или доступности кнопок (недоступная кнопка красит иконку сама).
        /// </summary>
        void RefreshFeatureLocks(bool allowed)
        {
            bool store = FeatureOpen(ProgressionConfig.Store), cards = FeatureOpen(ProgressionConfig.Cards), equip = FeatureOpen(ProgressionConfig.Equipment);
            int key = (store ? 1 : 0) | (cards ? 2 : 0) | (equip ? 4 : 0) | (allowed ? 8 : 0);
            if (key == m_LockKey) return;
            m_LockKey = key;
            SetLocked(m_StoreButton, m_StoreLock, !store, allowed);
            SetLocked(m_CardsButton, m_CardsLock, !cards, allowed);
            SetLocked(m_EquipButton, m_EquipLock, !equip, allowed);
            // Бейджи закрытого раздела не показываются: пересчёт при открытии.
            m_CardsBadgeShown = -1;
            m_BadgeValid = false;
            m_EquipBadgeShown = -1;
            m_StoreBadgeShown = -1;
        }

        static void SetLocked(Ui.ButtonRefs button, Image lockIcon, bool locked, bool allowed)
        {
            if (lockIcon != null) Ui.SetActive(lockIcon, locked);
            // Не накладываем два рисунка: после открытия возвращаем родную иконку, в том числе во время перехода.
            if (button.Icon != null) Ui.SetActive(button.Icon, !locked || lockIcon == null);
            // SpriteSwap и ButtonRefs не меняют Image.color: затемнение сохраняется и при нажатии.
            button.Image.color = locked ? LockedButtonTint : Color.white;
            // Временную недоступность и её цвета иконки/подписи оставляем ButtonRefs.SetInteractable.
            if (!allowed) return;
            if (button.Label != null) button.Label.color = PixelUi.ButtonTextColor(button.Style) * (locked ? LockedButtonTint : Color.white);
            if (button.Icon != null) button.Icon.color = locked ? LockedButtonTint : Color.white;
        }

        /// <summary>Кнопка Store: закрытый магазин — окно требования (открывается вместе с Cards), иначе магазин.</summary>
        void OnStoreButton()
        {
            if (!FeatureOpen(ProgressionConfig.Store)) ShowPreview(ProgressionConfig.Store);
            else ShowStore();
        }

        void OnCardsButton()
        {
            if (!FeatureOpen(ProgressionConfig.Cards)) ShowPreview(ProgressionConfig.Cards);
            else ShowCards();
        }

        void OnEquipButton()
        {
            if (!FeatureOpen(ProgressionConfig.Equipment)) ShowPreview(ProgressionConfig.Equipment);
            else ShowEquipment();
        }

        // ───────────── Блок цели ─────────────

        /// <summary>
        /// Блок цели (10 Гц): в шахте спасения, пока шахта не завершена, — «Rescue Mia» и шаг по фактам карты (расчищено целей
        /// маршрута из всех → «сломать замок» → «Mia is free!»). После Bruno — вся расчистка, затем доставка оставшегося груза
        /// для перехода. Шкала — те же факты. Тексты пересобираются только при смене значений.
        /// </summary>
        void RefreshObjective(OutpostSimulation sim)
        {
            if (m_Objective == null) return;
            var run = sim.Run;
            bool done = run.Phase == RunPhase.ReadyToContinue;
            string character = sim.Map.Rescue?.Character ?? ProgressionService.RescueCharacter(m_Game.Configs, run.Zone);
            bool blacksmith = character == ProgressionConfig.BlacksmithCharacter;
            bool savedBlacksmith = blacksmith && sim.Profile.Progression?.HasCrew(character) == true;
            bool objective = character != null && !done && (sim.HasRescue || savedBlacksmith);
            int cleared = 0, total = 0;
            bool lockOpen = false;
            if (objective) (cleared, total, lockOpen) = sim.RescueProgress();
            int stage = !objective ? 0 : lockOpen ? 3 : cleared >= total ? 2 : 1;
            bool clearMine = objective && blacksmith && (sim.HasRescue ? lockOpen : savedBlacksmith);
            if (clearMine)
            {
                cleared = 0;
                total = run.Targets.Count;
                foreach (var target in run.Targets) if (target.Destroyed) cleared++;
                stage = run.Phase == RunPhase.DrainingCargo ? 5 : 4;
            }
            int key = (objective ? 1 : 0) | (stage << 1) | (cleared << 4) | (total << 14);
            if (key != m_ObjectiveKey)
            {
                m_ObjectiveKey = key;
                m_Objective.SetActive(objective);
                if (objective)
                {
                    string name = Loc.T("outpost.crew." + character);
                    Ui.SetText(m_ObjectiveTitle, clearMine ? Loc.T("outpost.objective.next_mine")
                        : blacksmith && stage != 3 ? Loc.T("outpost.objective.blacksmith") : Loc.F("outpost.objective.rescue", name));
                    Ui.SetText(m_ObjectiveStep, stage == 5 ? Loc.T("outpost.objective.deliver")
                        : stage == 4 ? Loc.F("outpost.objective.clear_all", cleared, total)
                        : stage == 3 ? Loc.F("outpost.objective.done", name)
                        : stage == 2 ? Loc.F("outpost.objective.lock", name) : Loc.F("outpost.objective.route", cleared, total));
                    // Шкала — те же факты: цели маршрута и замок (без выдуманных долей).
                    m_ObjectiveBar.SetValue(clearMine ? (float)cleared / Math.Max(1, total) : stage == 3 ? 1f : (float)cleared / (total + 1));
                    m_ObjectiveBar.SetFill(stage == 3 ? PixelBarFill.Green : PixelBarFill.Gold);
                    string icon = clearMine ? stage == 5 ? OutpostUiText.BranchIcon(UpgradeBranch.Bags) : "icon_pickaxe"
                        : PixelArt.Has("icon_" + character) ? "icon_" + character : "icon_cat";
                    PixelUi.SetIcon(m_ObjectiveIcon, icon);
                    m_ObjectiveIcon.rectTransform.anchoredPosition = new Vector2(4 * Px + 8 * Px, Px) + OutpostDialog.CenterOffset(icon, 1);
                }
            }
            float reserve = objective ? ObjectiveHeight : 0f;
            if (!Mathf.Approximately(reserve, m_ObjectiveReserve))
            {
                m_ObjectiveReserve = reserve;
                PlaceToast();
            }
        }

        /// <summary>Тост — под блоком цели, если он есть, иначе на обычном месте.</summary>
        void PlaceToast()
        {
            if (m_ToastBg == null) return;
            float top = Mathf.Max(ToastTop, TopUnits + Margin + m_ObjectiveReserve + 2 * Px);
            m_ToastBg.rectTransform.anchoredPosition = new Vector2(0f, -top);
        }

        /// <summary>Тап по блоку цели: камера к ближайшей цели фронта расчистки, нет её — к клетке.</summary>
        void OnObjectiveTap()
        {
            var sim = m_Game?.Sim;
            if (sim == null) return;
            string character = sim.Map.Rescue?.Character ?? ProgressionService.RescueCharacter(m_Game.Configs, sim.Run.Zone);
            if (character == ProgressionConfig.BlacksmithCharacter && !sim.RescuePending && sim.Profile.Progression?.HasCrew(character) == true)
            {
                foreach (var target in sim.Map.Targets)
                    if (sim.CanStrike(target.Id) && m_Game.TryGetTargetWorldPosition(target.Id, out var targetWorld))
                    {
                        m_Game.FocusWorld(targetWorld);
                        return;
                    }
                m_Game.FocusBase();
                return;
            }
            int frontier = sim.RescueFrontierTarget();
            if (frontier >= 0 && m_Game.TryGetTargetWorldPosition(frontier, out var world)) m_Game.FocusWorld(world);
            else if (m_Game.TryGetRescueCageWorldPosition(out world)) m_Game.FocusWorld(world);
        }

        // ───────────── Окно требования ─────────────

        /// <summary>
        /// Окно закрытого раздела: крупная иконка, название, одно предложение пользы и точное требование («Unlocks in Mine 3»).
        /// Рабочего экрана нет; единственная кнопка — «Got it».
        /// </summary>
        public void ShowPreview(string feature)
        {
            var f = m_Game?.Configs?.Progression?.Feature(feature);
            if (f == null || m_Preview == null) return;
            string body = FeatureBenefit(f) + "\n" + PixelUi.AccentTag + FeatureRequirement(f) + ColorClose;
            m_PreviewFeature = f.Id;
            m_Preview.Begin(FeatureName(f), FeatureIcon(f, true), body);
            m_Preview.SetButton(0, Loc.T("outpost.preview.ok"), PixelButton.Green, HidePreview);
            m_Preview.HideButtonsFrom(1);
            m_Preview.Show();
            Opened(m_PreviewEntry);
        }

        public void HidePreview()
        {
            if (m_Preview == null) return;
            HideDialog(m_Preview, m_PreviewEntry);
        }

        // ───────────── Окно после спасения ─────────────

        /// <summary>
        /// Окно одного решения (после спасения Mia): заголовок, крупный портрет, текст и одна кнопка. Кнопка закрывает окно и затем
        /// вызывает своё действие; Back делает то же (окно никогда не запирает игру).
        /// </summary>
        internal void ShowIntro(string title, string icon, string body, string primary, PixelButton primaryStyle, Action onPrimary)
        {
            m_Intro.Begin(title, icon, body);
            m_Intro.SetButton(0, primary, primaryStyle, () => { HideIntro(); onPrimary?.Invoke(); });
            m_Intro.HideButtonsFrom(1);
            m_IntroEntry.Back = () => { HideIntro(); onPrimary?.Invoke(); };
            m_Intro.Show();
            Opened(m_IntroEntry);
        }

        public void HideIntro()
        {
            if (m_Intro == null || !m_Intro.Visible) return;
            HideDialog(m_Intro, m_IntroEntry);
        }
    }
}
