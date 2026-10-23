using System.Collections.Generic;
using System.Numerics;
using CatMiners.Core.Outpost;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using UnityEngine;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace CatMiners.Playable
{
    /// <summary>
    /// Подсказки квестов playable (ТЗ playable §3.3, reference.md §3) на своей <see cref="TutorialHint"/> поверх safe area HUD:
    /// реплика наставника при старте квеста и лапка на нужном элементе, пока действие доступно и не сделано — кнопка «База» и
    /// строка ветки в её панели (покупка по карману), «Экипировка» и «Надеть лучшее», «Карты» и улучшаемая карточка, «Магазин»
    /// и плитка тайм-скипа или бесплатного сундука, камень с рудой под ударом (до первого удара), ворота. Лапка касается кнопок
    /// у правого нижнего угла (<see cref="TutorialHint.TipAnchor"/> = <see cref="ButtonTip"/>): подпись и цена видны; кнопки
    /// нижней панели — сверху пальцем вниз (<see cref="BarTip"/>, <see cref="TutorialHint.PointDown"/>): лапка над полем, соседние
    /// кнопки открыты. Камень —
    /// целиком в полосе поля под панелью квеста и тостом HUD; руды не видно — любая цель под удар. Цель поля за краем экрана —
    /// стрелка у края полосы с иконкой квеста (тап подводит камеру). Только по состоянию игры, без таймеров простоя; время
    /// пересчёта — часы контроллера. Окно, в котором цели нет, прячет подсказку; цель внутри окна — лапка поверх окна.
    /// </summary>
    public sealed class PlayableGuide
    {
        /// <summary>Как часто пересчитывается цель (секунды часов контроллера); закрытие окном проверяется каждый кадр.</summary>
        const double ResolveInterval = 0.1;
        const float Px = PixelUi.Px;
        /// <summary>Точка касания кнопок: правее и ниже центра, лапка не закрывает подпись и цену.</summary>
        public static readonly Vector2 ButtonTip = new Vector2(0.8f, 0.3f);
        /// <summary>
        /// Точка касания кнопок нижней панели: у верхнего края по центру, лапка висит над ними пальцем вниз
        /// (<see cref="TutorialHint.PointDown"/>) — над полем, не закрывая подпись цели и соседние кнопки.
        /// </summary>
        public static readonly Vector2 BarTip = new Vector2(0.5f, 0.85f);
        static readonly Vector2 CentreTip = new Vector2(0.5f, 0.5f);
        /// <summary>Над клетками камня его спрайт выше footprint: столько клеток сверху тоже должны быть в кадре.</summary>
        const float SpriteHeadroom = 0.5f;
        /// <summary>Стрелка к цели за краем: ячейка 24×24 пикселя арта, стрелка 16×16 на её стороне к цели.</summary>
        const float ChipSize = 24 * Px, EdgeMargin = 3 * Px, BobPeriod = 0.3f;
        static readonly Vector3[] s_Corners = new Vector3[4];

        readonly OutpostController m_Game;
        readonly RectTransform m_Safe;
        readonly float m_ReservedTop;
        readonly TutorialHint m_Hint;
        readonly GameObject m_Dialog;
        readonly List<CardView> m_Cards = new List<CardView>(64);
        Button m_CardUpgrade;
        RectTransform m_Toast;
        Canvas m_Canvas;

        double m_NextResolve;
        Vector3 m_View;
        RectTransform m_Ui;
        int m_World = -1;
        bool m_HasTarget;
        int m_StrikeTarget = -1;
        readonly int[] m_ClassBest = new int[4];
        readonly float[] m_ClassScore = new float[4];
        bool m_Focused;
        QuestDef m_FocusQuest;

        // Цель поля за краем экрана: ближайшая к лагерю, стрелка у края полосы.
        int m_OffTarget = -1;
        readonly RectTransform m_Arrow;
        readonly Image m_ArrowSign, m_ArrowIcon;
        string m_ArrowIconName, m_ArrowSprite;

        // Показанное: подсказка перестраивается только при смене текста или цели.
        string m_ShownText;
        RectTransform m_ShownUi;
        int m_ShownWorld = -1;
        bool m_ShownDialog;

        public TutorialHint Hint => m_Hint;
        /// <summary>Видна лапка (цель есть и не закрыта окном).</summary>
        public bool PawVisible => m_Hint.Visible && (m_ShownUi != null || m_ShownWorld >= 0);
        /// <summary>Видна реплика наставника (пузырь).</summary>
        public bool LineVisible => m_Hint.Visible && m_ShownDialog;
        /// <summary>Цель лапки в UI (null — нет или точка поля).</summary>
        public RectTransform PawUi => PawVisible ? m_ShownUi : null;
        /// <summary>Цель лапки на поле (Id цели шахты) или −1.</summary>
        public int PawTarget => PawVisible ? m_ShownWorld : -1;
        /// <summary>Стрелка к цели квеста за краем экрана видна; <see cref="ArrowTarget"/> — Id цели.</summary>
        public bool ArrowVisible => m_Arrow.gameObject.activeSelf;
        public int ArrowTarget => ArrowVisible ? m_OffTarget : -1;
        public RectTransform Arrow => m_Arrow;

        /// <summary>Тост HUD (сообщение основной игры) на экране: реплика наставника ждёт, пока он не исчезнет.</summary>
        public bool ToastVisible
        {
            get
            {
                if (m_Toast == null && m_Game.Hud != null) m_Toast = m_Game.Hud.transform.Find("SafeArea/ToastBg") as RectTransform;
                return m_Toast != null && m_Toast.gameObject.activeSelf;
            }
        }

        /// <param name="reservedTop">Нижний край панели квеста от верха safe area (единицы Canvas): камень под лапкой — ниже.</param>
        public PlayableGuide(OutpostController game, RectTransform safe, float reservedBottom, float reservedTop)
        {
            m_Game = game;
            m_Safe = safe;
            m_ReservedTop = reservedTop;
            m_Hint = TutorialHint.Create(safe);
            m_Hint.name = "PlayableHint";
            m_Hint.SetReserved(reservedBottom, reservedTop);
            var dialog = m_Hint.Rect.Find("dialog");
            m_Dialog = dialog != null ? dialog.gameObject : null;

            // Стрелка: ячейка кита с иконкой квеста (кнопка — камера к цели) и icon_arrow_* со стороны цели.
            m_Arrow = Ui.Rect(safe, "QuestArrow");
            m_Arrow.anchorMin = m_Arrow.anchorMax = Vector2.zero;
            m_Arrow.pivot = new Vector2(0.5f, 0.5f);
            m_Arrow.sizeDelta = new Vector2(ChipSize, ChipSize);
            var chip = PixelUi.Panel(m_Arrow, "chip", PixelPanel.Slot);
            chip.rectTransform.Anchor(0, 0, 1, 1);
            chip.raycastTarget = true;
            var button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = chip;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(UiSound.PlayClick);
            button.onClick.AddListener(FocusOffTarget);
            m_ArrowIcon = PixelUi.Icon(chip.transform, "icon", "icon_pickaxe");
            m_ArrowSign = PixelUi.Icon(m_Arrow, "arrow", "icon_arrow_up");
            m_Arrow.gameObject.AddComponent<PixelPop>();
            m_Arrow.gameObject.SetActive(false);
        }

        /// <summary>
        /// Кадр подсказки: <paramref name="line"/> — реплика наставника (null — её время прошло или ждёт тост),
        /// <paramref name="blocked"/> — окно награды, переход, расчёт офлайна; <paramref name="now"/> — часы контроллера.
        /// Возвращает true, если реплика сейчас на экране (её время идёт только тогда).
        /// </summary>
        public bool Update(QuestDef quest, bool done, BigInteger progress, string line, bool blocked, double now)
        {
            var hud = m_Game.Hud;
            if (blocked || quest == null || hud == null || hud.InTransition || m_Game.Sim == null)
            {
                Hide();
                return false;
            }
            if (quest != m_FocusQuest)
            {
                m_FocusQuest = quest;
                m_Focused = false;
                m_StrikeTarget = -1;
                m_NextResolve = double.NegativeInfinity;
            }
            // Размер кадра или режим Canvas сменились (поворот, окно браузера, снимок): цель пересчитывается сразу.
            var camera = m_Game.Rig != null ? m_Game.Rig.Cam : Camera.main;
            var view = new Vector3(camera != null ? camera.pixelWidth : Screen.width, camera != null ? camera.pixelHeight : Screen.height,
                UiCamera() != null ? 1f : 0f);
            if (view != m_View)
            {
                m_View = view;
                m_NextResolve = double.NegativeInfinity;
            }
            if (now >= m_NextResolve || Covered(hud))
            {
                m_NextResolve = now + ResolveInterval;
                m_OffTarget = -1;
                m_HasTarget = !done && Resolve(quest, progress, out m_Ui, out m_World);
                if (!m_HasTarget)
                {
                    m_Ui = null;
                    m_World = -1;
                }
            }
            UpdateArrow(quest, done, hud, now);
            // Реплика без цели — только на открытом поле; с целью — там, где видна цель.
            bool showLine = line != null && (m_HasTarget || !hud.AnyWindowOpen);
            if (!showLine && !m_HasTarget)
            {
                HideHint();
                return false;
            }
            Present(showLine ? line : "", showLine);
            return showLine;
        }

        public void Hide()
        {
            HideHint();
            Ui.SetActive(m_Arrow, false);
        }

        void HideHint()
        {
            if (m_Hint.Visible) m_Hint.Hide();
            m_ShownText = null;
            m_ShownUi = null;
            m_ShownWorld = -1;
            m_ShownDialog = false;
        }

        /// <summary>Цель стала недействительной в этом кадре (открылось окно, кнопка скрылась).</summary>
        bool Covered(OutpostHud hud)
        {
            if (m_Ui != null) return !m_Ui.gameObject.activeInHierarchy || (hud.AnyWindowOpen && !InWindow(m_Ui));
            return (m_World >= 0 || m_OffTarget >= 0) && hud.AnyWindowOpen;
        }

        /// <summary>Элемент нижней части HUD (SafeArea/Bottom): кнопки «База», «Экипировка», «Карты», «Магазин».</summary>
        static bool InBottomBar(RectTransform rt)
        {
            for (Transform t = rt; t != null && t.parent != null; t = t.parent)
                if (t.parent.name == "SafeArea") return t.name == "Bottom";
            return false;
        }

        static bool InWindow(RectTransform rt)
        {
            for (var t = rt.parent; t != null; t = t.parent)
                if (t.name == "SafeArea") return false;
                else if (t.parent != null && t.parent.name == "SafeArea")
                    return t.name != "Bottom" && t.name != "TopBar" && t.name != "ZonePlaque";
            return false;
        }

        void Present(string text, bool dialog)
        {
            bool changed = !m_Hint.Visible || !string.Equals(text, m_ShownText, System.StringComparison.Ordinal) ||
                           m_ShownUi != m_Ui || m_ShownWorld != m_World;
            if (changed)
            {
                // Кнопки нижней панели — лапка сверху пальцем вниз; прочие кнопки (покупка, «Надеть лучшее», улучшение) — касание
                // у правого нижнего угла; карточка и цель поля — центр.
                bool bar = m_Ui != null && InBottomBar(m_Ui);
                m_Hint.PointDown = bar;
                m_Hint.TipAnchor = bar ? BarTip : m_Ui != null && m_Ui.GetComponent<CardView>() == null ? ButtonTip : CentreTip;
                if (m_Ui != null) m_Hint.Show(text, m_Ui);
                else if (m_World >= 0 && m_Game.TryGetTargetWorldPosition(m_World, out var world)) m_Hint.ShowWorld(text, world, Footprint(m_World));
                else m_Hint.Show(text, null);
                m_ShownText = text;
                m_ShownUi = m_Ui;
                m_ShownWorld = m_Ui == null ? m_World : -1;
                // Лапка внутри окна — поверх него (окна открываются последними детьми safe area).
                if (m_Ui != null && m_Game.Hud.AnyWindowOpen) m_Hint.Rect.SetAsLastSibling();
            }
            if (m_Dialog != null && m_Dialog.activeSelf != dialog) m_Dialog.SetActive(dialog);
            m_ShownDialog = dialog;
        }

        /// <summary>Цель лапки по квесту и открытым окнам; false — показывать нечего (действие недоступно или цель скрыта).</summary>
        bool Resolve(QuestDef q, BigInteger progress, out RectTransform ui, out int world)
        {
            ui = null;
            world = -1;
            var hud = m_Game.Hud;
            bool field = !hud.AnyWindowOpen;
            int open = OpenWindows(hud);
            switch (q.Kind)
            {
                case QuestKind.Strike:
                    if (field && progress.IsZero) world = FindStrikeTarget(false);
                    break;
                case QuestKind.Gate:
                    if (field) world = FindStrikeTarget(true);
                    break;
                case QuestKind.Upgrade:
                case QuestKind.Level:
                case QuestKind.Cats:
                {
                    var branch = q.PurchaseBranch;
                    if (!PlayableQuestRules.Affordable(m_Game, branch)) break;
                    if (hud.BaseOpen && open == 1) ui = Target(OutpostHud.BranchTarget(branch));
                    else if (field) ui = Target(OutpostHud.TargetBase);
                    break;
                }
                case QuestKind.Equip:
                    if (hud.EquipmentOpen && open == 1) ui = Target(OutpostHud.TargetEquipBest);
                    else if (field) ui = Target(OutpostHud.TargetEquipment);
                    break;
                case QuestKind.Card:
                    if (hud.CardDetailOpen && open == 2) ui = CardUpgradeButton(hud);
                    else if (hud.CardsOpen && open == 1) ui = UpgradableCard(hud);
                    else if (field) ui = Target(OutpostHud.TargetCards);
                    break;
                case QuestKind.Skip:
                {
                    var skip = PlayableQuestRules.SkipInStock(m_Game);
                    if (skip == null || m_Game.Sim.Run.Phase != RunPhase.Running) break;
                    if (hud.StoreOpen && open == 1) ui = StoreTile(hud, skip);
                    else if (field) ui = Target(OutpostHud.TargetStore);
                    break;
                }
                case QuestKind.Chest:
                {
                    var chest = PlayableQuestRules.FreeAdChest(m_Game);
                    if (chest == null) break;
                    if (hud.StoreOpen && open == 1) ui = StoreTile(hud, chest);
                    else if (field) ui = Target(OutpostHud.TargetStore);
                    break;
                }
            }
            if (ui != null && !ui.gameObject.activeInHierarchy) ui = null;
            return ui != null || world >= 0;
        }

        RectTransform Target(string id) => m_Game.Hud.FindTarget(id);

        /// <summary>Открытые окна HUD по флагам (вложенные — карточка поверх «Карт», предмет поверх «Экипировки» — считаются отдельно).</summary>
        static int OpenWindows(OutpostHud h) =>
            (h.BaseOpen ? 1 : 0) + (h.EquipmentOpen ? 1 : 0) + (h.ItemDetailOpen ? 1 : 0) + (h.CardsOpen ? 1 : 0) + (h.CardDetailOpen ? 1 : 0) +
            (h.StoreOpen ? 1 : 0) + (h.ChestRewardOpen ? 1 : 0) + (h.SkipResultOpen ? 1 : 0) + (h.SettingsOpen ? 1 : 0) + (h.ResultsOpen ? 1 : 0) +
            (h.OfflineOpen ? 1 : 0) + (h.OfflineCalcOpen ? 1 : 0) + (h.CampaignOpen ? 1 : 0) + (h.ErrorOpen ? 1 : 0) + (h.ModalOpen ? 1 : 0);

        RectTransform CardUpgradeButton(OutpostHud hud)
        {
            if (m_CardUpgrade == null)
                foreach (var b in hud.GetComponentsInChildren<Button>(true))
                    if (b.name == "cardUpgrade") { m_CardUpgrade = b; break; }
            return m_CardUpgrade != null && m_CardUpgrade.interactable && m_CardUpgrade.gameObject.activeInHierarchy
                ? (RectTransform)m_CardUpgrade.transform : null;
        }

        /// <summary>Первая (верхняя в сетке) карточка «Карт», которую можно улучшить.</summary>
        RectTransform UpgradableCard(OutpostHud hud)
        {
            var p = m_Game.State?.Profile;
            if (p == null) return null;
            hud.GetComponentsInChildren(false, m_Cards);
            CardView best = null;
            foreach (var v in m_Cards)
            {
                if (v.Def == null || v.GetComponent<Button>() == null || !CardService.Quote(p, v.Def).CanUpgrade) continue;
                if (best == null || v.transform.GetSiblingIndex() < best.transform.GetSiblingIndex()) best = v;
            }
            m_Cards.Clear();
            return best != null ? best.Rect : null;
        }

        static RectTransform StoreTile(OutpostHud hud, StoreItemDef def) =>
            hud.transform.Find("SafeArea/Store/viewport/content/grid_" + def.Section + "/" + def.Id + "/tile") as RectTransform;

        // ───────────── Цели поля ─────────────

        /// <summary>
        /// Цель под удар: камень с рудой (Kind 'r', для квеста ворот — следующие ворота) целиком в полосе поля между нижней
        /// частью HUD и панелью квеста (тостом HUD, если он виден), ближе к её центру; прежний — пока виден. Руды в кадре нет —
        /// любая цель под удар (месторождение, препятствие). Целиком не помещается ничего (низкий экран, окно редактора) — то
        /// же по центру цели в полосе. Ничего не видно: камера один раз за квест плавно подводит ближайшую к лагерю, а стрелка
        /// у края полосы ведёт к ней.
        /// </summary>
        int FindStrikeTarget(bool gate)
        {
            var sim = m_Game.Sim;
            var map = sim.Map;
            var run = sim.Run;
            var camera = m_Game.Rig != null ? m_Game.Rig.Cam : Camera.main;
            // Полоса поля в пикселях камеры (у снимка в RenderTexture — пиксели текстуры, не экрана).
            float width = camera != null ? camera.pixelWidth : Screen.width, height = camera != null ? camera.pixelHeight : Screen.height;
            float bottom = height * m_Game.Hud.BottomInset, top = Mathf.Min(height * (1f - m_Game.Hud.TopInset), OverlayTopPixels(height));
            int nearestRock = -1, nearestAny = -1;
            float rockDistance = float.MaxValue, anyDistance = float.MaxValue;
            // Классы по убыванию: руда целиком, любая цель целиком, руда центром в полосе, любая цель центром.
            for (int c = 0; c < m_ClassBest.Length; c++)
            {
                m_ClassBest[c] = -1;
                m_ClassScore[c] = float.MaxValue;
            }
            int nextGate = gate && map.Gates != null && run.GatesOpened < map.Gates.Length ? map.Gates[run.GatesOpened].TargetId : -1;
            for (int i = 0; i < map.Targets.Length; i++)
            {
                var t = map.Targets[i];
                if (gate ? t.Id != nextGate : t.IsGate) continue;
                if (!sim.CanStrike(t.Id) || !m_Game.TryGetTargetWorldPosition(t.Id, out var world)) continue;
                bool rock = gate || t.Kind == 'r';
                float distance = (world - m_Game.BaseWorldPosition).sqrMagnitude;
                if (rock && distance < rockDistance)
                {
                    nearestRock = t.Id;
                    rockDistance = distance;
                }
                if (distance < anyDistance)
                {
                    nearestAny = t.Id;
                    anyDistance = distance;
                }
                if (camera == null) continue;
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f || screen.x < 0f || screen.x > width || screen.y < bottom || screen.y > top) continue;
                bool full = FullyVisible(camera, world, Footprint(t.Id), width, bottom, top);
                // Прежняя цель остаётся, пока видна: лапка не прыгает между камнями.
                float score = t.Id == m_StrikeTarget ? -1f : new Vector2(screen.x - width * 0.5f, screen.y - (bottom + top) * 0.5f).sqrMagnitude;
                Consider(full ? (rock ? 0 : 1) : (rock ? 2 : 3), t.Id, score);
                if (full && rock) Consider(1, t.Id, score);
                if (rock) Consider(3, t.Id, score);
            }
            int best = -1;
            for (int c = 0; c < m_ClassBest.Length && best < 0; c++) best = m_ClassBest[c];
            int nearest = nearestRock >= 0 ? nearestRock : nearestAny;
            if (best < 0 && nearest >= 0)
            {
                m_OffTarget = nearest;
                if (!m_Focused && m_Game.Rig != null && m_Game.TryGetTargetWorldPosition(nearest, out var focus))
                {
                    m_Focused = true;
                    m_Game.Rig.FocusOnSmooth(new Vector2(focus.x, focus.z));
                }
            }
            m_StrikeTarget = best;
            return best;
        }

        void Consider(int cls, int id, float score)
        {
            if (score >= m_ClassScore[cls]) return;
            m_ClassScore[cls] = score;
            m_ClassBest[cls] = id;
        }

        /// <summary>Footprint цели (и спрайт над ним) целиком в полосе [0, width] × [bottom, top] пикселей камеры.</summary>
        static bool FullyVisible(Camera camera, Vector3 world, Vector2 cells, float width, float bottom, float top)
        {
            // Камера смотрит строго вниз: мировая X — вправо по экрану, Z — вверх.
            var lo = camera.WorldToScreenPoint(world - new Vector3(cells.x * 0.5f, 0f, cells.y * 0.5f));
            var hi = camera.WorldToScreenPoint(world + new Vector3(cells.x * 0.5f, 0f, cells.y * 0.5f + SpriteHeadroom));
            if (lo.z <= 0f || hi.z <= 0f) return false;
            return Mathf.Min(lo.x, hi.x) >= 0f && Mathf.Max(lo.x, hi.x) <= width && Mathf.Min(lo.y, hi.y) >= bottom && Mathf.Max(lo.y, hi.y) <= top;
        }

        /// <summary>
        /// Нижний край панели квеста — или тоста HUD, если он виден и ниже, — в пикселях камеры поля (Canvas HUD и камера
        /// рисуют в одну цель: экран или RenderTexture снимка).
        /// </summary>
        float OverlayTopPixels(float cameraHeight)
        {
            var uiCam = UiCamera();
            float canvasHeight = uiCam != null ? uiCam.pixelHeight : Screen.height;
            if (canvasHeight <= 0f) return cameraHeight;
            var r = m_Safe.rect;
            float y = RectTransformUtility.WorldToScreenPoint(uiCam, m_Safe.TransformPoint(new Vector3(r.center.x, r.yMax - m_ReservedTop, 0f))).y;
            if (ToastVisible)
            {
                m_Toast.GetWorldCorners(s_Corners);
                y = Mathf.Min(y, RectTransformUtility.WorldToScreenPoint(uiCam, s_Corners[0]).y);
            }
            return y * cameraHeight / canvasHeight;
        }

        Camera UiCamera()
        {
            if (m_Canvas == null && m_Game.Hud != null) m_Canvas = m_Game.Hud.GetComponent<Canvas>();
            return m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;
        }

        Vector2 Footprint(int id)
        {
            var map = m_Game.Map;
            var target = map?.Target(id);
            if (target?.Cells == null || target.Cells.Length == 0) return Vector2.one;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < target.Cells.Length; i++)
            {
                int x = map.Col(target.Cells[i]), y = map.Row(target.Cells[i]);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            return new Vector2(maxX - minX + 1, maxY - minY + 1);
        }

        // ───────────── Стрелка к цели за краем ─────────────

        /// <summary>
        /// Стрелка у края полосы поля к цели квеста, которой нет в кадре (камень под первый удар, ворота): ячейка с иконкой
        /// квеста и icon_arrow_* в сторону цели, покачивается на пиксель арта. Только на открытом поле.
        /// </summary>
        void UpdateArrow(QuestDef quest, bool done, OutpostHud hud, double now)
        {
            var world = Vector3.zero;
            bool show = !done && m_OffTarget >= 0 && !m_HasTarget && !hud.AnyWindowOpen &&
                        m_Game.TryGetTargetWorldPosition(m_OffTarget, out world);
            if (!show)
            {
                Ui.SetActive(m_Arrow, false);
                return;
            }
            var camera = m_Game.Rig != null ? m_Game.Rig.Cam : Camera.main;
            if (camera == null)
            {
                Ui.SetActive(m_Arrow, false);
                return;
            }
            float width = camera.pixelWidth, height = camera.pixelHeight;
            float bottom = height * hud.BottomInset, top = Mathf.Min(height * (1f - hud.TopInset), OverlayTopPixels(height));
            var p = camera.WorldToScreenPoint(world);
            if (p.z < 0f) p = new Vector3(width - p.x, -p.y, -p.z);
            int vertical = p.y > top ? 1 : p.y < bottom ? -1 : 0;
            int horizontal = p.x > width ? 1 : p.x < 0f ? -1 : 0;
            if (vertical == 0 && horizontal == 0)
            {
                Ui.SetActive(m_Arrow, false);
                return;
            }
            string sprite = vertical > 0 ? horizontal < 0 ? "icon_arrow_up_left" : horizontal > 0 ? "icon_arrow_up_right" : "icon_arrow_up"
                : vertical < 0 ? horizontal < 0 ? "icon_arrow_down_left" : horizontal > 0 ? "icon_arrow_down_right" : "icon_arrow_down"
                : horizontal < 0 ? "icon_arrow_left" : "icon_arrow_right";
            if (sprite != m_ArrowSprite)
            {
                m_ArrowSprite = sprite;
                PixelUi.SetIcon(m_ArrowSign, sprite);
            }
            string icon = !string.IsNullOrEmpty(quest.Icon) && PixelArt.Has(quest.Icon) ? quest.Icon : "icon_star";
            if (icon != m_ArrowIconName)
            {
                m_ArrowIconName = icon;
                PixelUi.SetIcon(m_ArrowIcon, icon);
                m_ArrowIcon.rectTransform.anchoredPosition = PlayableBanner.CenterOffset(icon, 1);
            }
            // Точка камеры → координаты safe area HUD; ячейка внутри полосы у её края, стрелка — снаружи ячейки к цели.
            var uiCam = UiCamera();
            float canvasHeight = uiCam != null ? uiCam.pixelHeight : Screen.height;
            float k = canvasHeight > 0f ? canvasHeight / height : 1f;
            // Стрелка снаружи ячейки (5 пикселей арта от её края, половина стрелки 8) и покачивание на пиксель: не касается
            // тоста и панелей.
            float reach = ChipSize * 0.5f + 14 * Px + EdgeMargin;
            Vector2 lo = ToSafe(new Vector2(0f, bottom) * k, uiCam), hi = ToSafe(new Vector2(width, top) * k, uiCam), at = ToSafe(new Vector2(p.x, p.y) * k, uiCam);
            float x = Mathf.Clamp(at.x, lo.x + reach, hi.x - reach), y = Mathf.Clamp(at.y, lo.y + reach, hi.y - reach);
            var dir = new Vector2(horizontal, vertical);
            float bob = ((int)(now / BobPeriod) & 1) * Px;
            m_Arrow.anchoredPosition = PixelUi.Snap(new Vector2(x, y) + dir * bob);
            // Стрелка (якорь — центр ячейки) снаружи ячейки на стороне цели, на 3 пикселя арта заходит на рамку.
            m_ArrowSign.rectTransform.anchoredPosition = PixelUi.Snap(dir.normalized * (ChipSize * 0.5f + 5 * Px)) + PlayableBanner.CenterOffset(sprite, 1);
            if (!m_Arrow.gameObject.activeSelf)
            {
                m_Arrow.gameObject.SetActive(true);
                m_Arrow.SetAsLastSibling();
            }
        }

        /// <summary>Пиксели Canvas → координаты от левого нижнего угла safe area HUD.</summary>
        Vector2 ToSafe(Vector2 screen, Camera uiCam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Safe, screen, uiCam, out var local);
            return local - m_Safe.rect.min;
        }

        /// <summary>Тап по стрелке: камера плавно к цели.</summary>
        void FocusOffTarget()
        {
            if (m_OffTarget < 0 || m_Game.Rig == null || !m_Game.TryGetTargetWorldPosition(m_OffTarget, out var world)) return;
            m_Game.Rig.FocusOnSmooth(new Vector2(world.x, world.z));
        }
    }
}
