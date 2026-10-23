using System;
using System.Collections.Generic;
using System.Text;
using CatMiners.Core;
using CatMiners.Core.Grid;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Основной экран (ТЗ 13.1) в пиксельном стиле (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6, «Раскладка HUD»).
    /// Верх — табличка «Шахта N» с сектором, полоса валют (золото и доход, эликсир, котики/лимит) и шестерёнка настроек
    /// (звук, вибрация). Низ — плашка бесплатного котика и кнопка свободных котиков, панель: сундуки, динамит, «Нанять»,
    /// «Чертежи». Контекстная панель выбранного объекта, стрелки к объектам вне кадра, подсказка обучения, полёт добычи
    /// к счётчикам (<see cref="FlyLoot(Vector3, string, int)"/>), окна сундука, офлайна, перехода и настроек, «Чертежи»,
    /// экран завершения кампании.
    /// Тексты обновляются не чаще 10 раз в секунду и только когда изменились их исходные значения (ТЗ 15.3).
    /// Части класса: HudContext.cs (контекстная панель), HudWindows.cs (окна), HudBlueprints.cs («Чертежи»).
    /// </summary>
    public sealed partial class Hud : MonoBehaviour
    {
        // Эталон CanvasScaler: те же значения ставит ProjectSetup; доли панелей считаются по этой же формуле.
        const float RefWidth = 1080f, RefHeight = 1920f, RefMatch = 0.5f;

        // ── Раскладка, единицы Canvas (1 пиксель арта = 4 единицы, всё кратно ему). Подробно — руководство, 6.6.
        const float Px = PixelUi.Px;
        /// <summary>Отступ панелей от краёв safe area и между полосами.</summary>
        const float Margin = 2 * Px;
        /// <summary>Высота верхней полосы: табличка, валюты, шестерёнка.</summary>
        const float TopBarHeight = 30 * Px;
        /// <summary>Нижний край верхней полосы от верха safe area — <see cref="TopInset"/>.</summary>
        const float TopUnits = Margin + TopBarHeight;
        /// <summary>Тёмная нижняя панель (рамка ui_panel_dark — 5 пикселей арта).</summary>
        const float BarHeight = 52 * Px, BarPad = 5 * Px;
        /// <summary>«Нанять» занимает всю высоту панели внутри рамки; ячейки — <see cref="PixelUi.SlotSize"/>.</summary>
        const float HireHeight = BarHeight - 2 * BarPad;
        const float SlotGap = 3 * Px;
        /// <summary>Полоса над панелью: плашка бесплатного котика и кнопка свободных котиков.</summary>
        const float StripHeight = 24 * Px, PillHeight = 16 * Px, IdleWidth = 44 * Px;
        /// <summary>Верхний край нижней части HUD от низа safe area — <see cref="BottomInset"/>.</summary>
        const float BottomUnits = Margin + BarHeight + Margin + StripHeight;
        /// <summary>Ширина таблички «Шахта N» — доля ширины safe area.</summary>
        const float PlaqueFraction = 0.26f;
        /// <summary>
        /// Разделители сегментов полосы валют — доли её ширины. При эталонной ширине 1080 числа FontLarge помещаются
        /// без автоподбора: золото до «9.99M» (120 ед.), эликсир до «99.9K» (114), котики до «10/10» (114).
        /// </summary>
        const float GoldSegment = 0.355f, ElixirSegment = 0.655f;
        /// <summary>Кнопка перехода и причина под верхней полосой.</summary>
        const float NextMineHeight = 24 * Px, ReasonHeight = 20 * Px;
        /// <summary>Тост — под кнопкой перехода.</summary>
        const float ToastTop = TopUnits + Margin + NextMineHeight + 4 * Px;

        /// <summary>Метка слота Debug на табличке (служебная, не переводится).</summary>
        const string DebugMarker = "<color=#FF9070>[DEBUG]</color>";
        /// <summary>Открывающий тег цены, на которую не хватает (red5); закрывающий — "&lt;/color&gt;".</summary>
        const string BadTag = "<color=#FF9070>";
        const string ColorClose = "</color>";
        static string BlueprintsHint => Loc.T("hud.blueprints.hint");

        /// <summary>Набор исходных значений текста: строка собирается заново, только когда набор изменился (ТЗ 15.3).</summary>
        struct ValueKey : IEquatable<ValueKey>
        {
            public int A, B, C, D, E, F, G, H;
            public long L;
            public GameNumber N1, N2, N3, N4;

            public bool Equals(ValueKey o) =>
                A == o.A && B == o.B && C == o.C && D == o.D && E == o.E && F == o.F && G == o.G && H == o.H && L == o.L &&
                N1 == o.N1 && N2 == o.N2 && N3 == o.N3 && N4 == o.N4;

            public override bool Equals(object obj) => obj is ValueKey k && Equals(k);

            public override int GetHashCode() => (A * 397) ^ (B * 31) ^ (C << 3) ^ (D << 7) ^ L.GetHashCode();
        }

        GameController m_Game;
        Canvas m_Canvas;
        RectTransform m_Safe;

        // Верх.
        TextMeshProUGUI m_Zone, m_ZoneSub;
        TextMeshProUGUI m_Gold, m_Income, m_Elixir, m_Cats;
        Ui.ButtonRefs m_SettingsButton;
        Ui.ButtonRefs m_NextMine;
        Ui.ButtonRefs m_NextMineReason;
        bool m_ZoneTwoLines = true;

        // Низ.
        RectTransform m_Bottom;
        Ui.ButtonRefs m_Hire;
        PixelIconText m_HirePrice;
        TextMeshProUGUI m_HireLevel;
        PixelBar m_HireGauge;
        Image m_FreeCatPill;
        TextMeshProUGUI m_FreeCat;
        Ui.ButtonRefs m_Dynamite;
        Ui.ButtonRefs m_Blueprints;
        Ui.ButtonRefs m_IdleCats;
        Ui.ButtonRefs m_ChestsBadge;

        Image m_ToastBg;
        TextMeshProUGUI m_Toast;
        float m_ToastUntil;

        EdgeIndicators m_Edges;
        Image m_Ghost;
        float m_NextRefresh;
        readonly StringBuilder m_Sb = new StringBuilder(256);
        static readonly Vector3[] s_Corners = new Vector3[4];

        // Кэш показанных значений (ТЗ 15.3). Смена MineProgressState (новая зона, другой слот) сбрасывает всё.
        object m_CacheMine;
        int m_ZoneGates = -1, m_ZoneDebug = -1, m_CatsSlots = -1, m_CatsCap = -1;
        int m_IdleShown = -1, m_DynamiteShown = -1, m_BlueprintsStockShown = -1, m_FreeCatState = -1, m_HireColors = -1;
        long m_FreeCatKey = long.MinValue;
        bool m_MoneyValid;
        GameNumber m_GoldShown, m_ElixirShown, m_IncomeShown;
        ValueKey m_HireKey, m_NextMineKey, m_CtxKey, m_ChestsKey, m_BpKey, m_BpRowsKey;
        bool m_HireValid, m_NextMineValid, m_CtxValid, m_ChestsValid, m_BpValid, m_BpRowsValid;

        // Доли панелей: пересчёт только при смене размера safe area.
        float m_InsetSafeUnits = -1f, m_TopInset = 0.07f, m_BottomInset = 0.17f;
        float m_PushedTop = -1f, m_PushedBottom = -1f;

        /// <summary>
        /// Нижний край верхней полосы — доля высоты safe area от её верха (табличка, валюты, шестерёнка). Камера, ввод
        /// и стрелки считают поле между <see cref="TopInset"/> и <see cref="BottomInset"/>.
        /// </summary>
        public float TopInset
        {
            get
            {
                UpdateInsets();
                return m_TopInset;
            }
        }

        /// <summary>Верхний край нижней части HUD (панель и полоса бесплатного котика) — доля высоты safe area от её низа.</summary>
        public float BottomInset
        {
            get
            {
                UpdateInsets();
                return m_BottomInset;
            }
        }

        public bool ModalOpen => m_ModalOpen;
        /// <summary>Экран «Карты» открыт (вместе с окном карты поверх него).</summary>
        public bool BlueprintsOpen => m_CardsRoot != null && m_CardsRoot.gameObject.activeSelf;
        /// <summary>Открыто окно настроек (звук, вибрация).</summary>
        public bool SettingsOpen => m_Settings != null && m_Settings.Visible;
        /// <summary>Показан экран завершения кампании.</summary>
        public bool CampaignEndVisible => m_End != null && m_End.Visible;
        /// <summary>
        /// Открыто любое окно HUD (модальное, «Чертежи», настройки, конец кампании): подсказка обучения и стрелки у краёв
        /// под ним скрыты. Без выделений — можно спрашивать каждый кадр.
        /// </summary>
        public bool AnyWindowOpen => m_ModalOpen || BlueprintsOpen || SettingsOpen || CampaignEndVisible;

        /// <summary>
        /// Полоса поля между панелями HUD в пикселях экрана: <see cref="BottomInset"/> и <see cref="TopInset"/> — доли высоты
        /// safe area от её нижнего и верхнего края (как в CameraRig), а не всего экрана: на устройствах с вырезом и полосой
        /// жестов края поля иначе съезжают. Для ввода (PointerInput) и стрелок у краёв (EdgeIndicators). Без выделений.
        /// </summary>
        public void FieldBandPixels(out float bottom, out float top)
        {
            var safe = Screen.safeArea;
            if (safe.height <= 0f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            bottom = safe.yMin + BottomInset * safe.height;
            top = safe.yMax - TopInset * safe.height;
        }
        /// <summary>Стрелки у краёв экрана к объектам вне кадра (ТЗ 13.1).</summary>
        public EdgeIndicators Edges => m_Edges;
        /// <summary>
        /// Подсказка обучения (пузырь, лапка, рамка фокуса). HUD регистрирует в ней свои цели (TutorialHint.Target*)
        /// и передаёт её в <see cref="TutorialController.Hint"/>, как только контроллер обучения создан.
        /// </summary>
        public TutorialHint Hint { get; private set; }

        /// <summary>Нажатие кнопки сундуков (очередь неоткрытых сундуков, ТЗ 10.3). Подписывает контроллер.</summary>
        public Action OnChestsBadgeTap;

        /// <summary>Камера Canvas для пересчёта экранных точек: null у Screen Space Overlay.</summary>
        internal Camera UiCamera => m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;

        /// <summary>
        /// HUD на Canvas «HUD» из сцены (ТЗ 5.4, создаёт ProjectSetup); если его нет — Canvas создаётся кодом.
        /// Настройки Canvas, CanvasScaler 1080×1920 (match 0.5) и GraphicRaycaster выставляются в обоих случаях.
        /// </summary>
        public static Hud Create(GameController game)
        {
            var canvasGo = FindSceneCanvas();
            Canvas canvas;
            if (canvasGo == null)
            {
                canvasGo = new GameObject("HUD", typeof(RectTransform));
                canvas = canvasGo.AddComponent<Canvas>();
            }
            else
            {
                canvas = canvasGo.GetComponent<Canvas>();
                if (!canvasGo.activeSelf) canvasGo.SetActive(true);
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Выше любых спрайтов поля (оверлеи до PixelWorld.OverlayBase + 100): в режиме Screen Space Camera (скриншоты
            // и замер производительности) HUD тоже остаётся поверх поля.
            canvas.sortingOrder = 30000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = RefMatch;
            if (canvasGo.GetComponent<GraphicRaycaster>() == null) canvasGo.AddComponent<GraphicRaycaster>();
            var hud = canvasGo.AddComponent<Hud>();
            hud.m_Game = game;
            hud.m_Canvas = canvas;
            hud.Build((RectTransform)canvasGo.transform);
            return hud;
        }

        /// <summary>Корневой Canvas «HUD» сцены без компонента Hud (включая выключенный).</summary>
        static GameObject FindSceneCanvas()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            foreach (var c in canvases)
            {
                if (c == null || c.transform.parent != null || c.gameObject.name != "HUD") continue;
                if (!c.gameObject.scene.IsValid() || c.GetComponent<Hud>() != null) continue;
                if (!(c.transform is RectTransform)) continue;
                return c.gameObject;
            }
            return null;
        }

        void Build(RectTransform root)
        {
            CreateActions();

            // SafeArea из сцены переиспользуется, второй не создаётся.
            m_Safe = root.Find("SafeArea") as RectTransform;
            if (m_Safe == null) m_Safe = Ui.Rect(root, "SafeArea").Anchor(0, 0, 1, 1);
            if (m_Safe.GetComponent<SafeArea>() == null) m_Safe.gameObject.AddComponent<SafeArea>();

            // Порядок детей SafeArea — порядок слоёв: панели HUD, стрелки, тост, подсказка обучения, окна.
            BuildTopBar();
            BuildNextMine();
            BuildBottom();
            BuildContext();

            // ── Стрелки у краёв экрана (ТЗ 13.1) ──
            var edges = Ui.Rect(m_Safe, "EdgeIndicators").Anchor(0, 0, 1, 1);
            m_Edges = edges.gameObject.AddComponent<EdgeIndicators>();
            m_Edges.Init(m_Game, this);

            BuildToast();

            // ── Обучение: над панелями HUD; окна, созданные ниже, перекрывают подсказку ──
            Hint = TutorialHint.Create(m_Safe);
            Hint.RegisterTarget(TutorialHint.TargetHire, m_Hire.Rect);
            Hint.RegisterTarget(TutorialHint.TargetBlueprints, m_Blueprints.Rect);
            Hint.RegisterTarget(TutorialHint.TargetDynamite, m_Dynamite.Rect);
            Hint.RegisterTarget(TutorialHint.TargetChests, m_ChestsBadge.Rect);
            Hint.RegisterTarget(TutorialHint.TargetFreeCat, m_FreeCatPill.rectTransform);
            Hint.RegisterTarget(TutorialHint.TargetNextMine, m_NextMine.Rect);
            Hint.RegisterTarget(TutorialHint.TargetContextAction, m_CtxButtons[0].Rect);
            // Наставник с пузырём — по возможности между верхней полосой и нижней частью HUD.
            Hint.SetReserved(BottomUnits, TopUnits);

            // ── Добыча: иконки летят к счётчикам поверх панелей ──
            BuildLootFly();

            // ── Окна: экран конца кампании — под окнами, чтобы сундук награды выхода был виден поверх (ТЗ 11.4) ──
            BuildCampaignEnd();
            BuildBlueprints();
            BuildSettings();
            BuildModal();

            // ── Призрак динамита — поверх всего, на корне Canvas (MoveDynamiteGhost считает в его координатах) ──
            m_Ghost = PixelUi.Icon(root, "DynamiteGhost", "icon_dynamite_big", 2);
            m_Ghost.gameObject.SetActive(false);
        }

        void CreateActions()
        {
            m_ActActivate = () => m_Game.ActivateShaft(m_CtxOrd);
            m_ActOpenGate = () => m_Game.OpenGate(m_CtxOrd);
            m_ActNextMine = () => m_Game.RequestNextMine();
            m_ActUpgrade = () => m_Game.Upgrade(m_CtxOrd);
            m_ActToThreshold = () => m_Game.UpgradeToThreshold(m_CtxOrd);
            m_ActCollect = () => m_Game.CollectPayout(m_CtxOrd);
            m_CardComparison = CompareCards;
        }

        // ───────────── Верх ─────────────

        void BuildTopBar()
        {
            // Деревянная табличка «Шахта N» и строка сектора (как на концепте).
            var plaque = PixelUi.Panel(m_Safe, "ZonePlaque", PixelPanel.Wood);
            plaque.rectTransform.Anchor(0, 1, PlaqueFraction, 1, new Vector2(Margin, -TopUnits), new Vector2(0, -Margin));
            m_Zone = PixelUi.Text(plaque.transform, "zone", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            m_Zone.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_Zone, PixelUi.FontBody, PixelUi.FontLarge);
            m_ZoneSub = PixelUi.Text(plaque.transform, "sector", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            m_ZoneSub.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_ZoneSub, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            SetZoneLayout(false);

            // Полоса валют: тёмная панель с сегментами золото | эликсир | котики.
            var bar = PixelUi.Panel(m_Safe, "TopBar", PixelPanel.Dark).rectTransform;
            bar.Anchor(PlaqueFraction, 1, 1, 1, new Vector2(Margin, -TopUnits), new Vector2(-(Margin + TopBarHeight + Margin), -Margin));
            // Числа — FontLarge (руководство 6.1, «числа HUD»), как на концепте: цифры около половины высоты иконки.
            // Сегмент внутри рамки — 80 ед. Строка FontLarge требует 54 ед. (1,125 em), FontSmall — 27: золото и доход
            // получают свои полосы (24…84 и 4…36, цифры Tiny5 стоят по центру полосы), чтобы автоподбор не уменьшал их
            // по высоте. Цифры золота 39…69, дохода 12…28 — блок по центру монеты, между строками 3 пикселя арта.
            var gold = Segment(bar, "gold", 0f, GoldSegment, "icon_coin", out float textLeft);
            m_Gold = PixelUi.Text(gold, "gold", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Gold.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, 6 * Px), new Vector2(-Px, 21 * Px));
            NumberText(m_Gold);
            m_Income = PixelUi.Text(gold, "income", "", PixelUi.FontSmall, PixelUi.TextIncome, TextAlignmentOptions.Left, false, true);
            m_Income.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, Px), new Vector2(-Px, 9 * Px));
            m_Income.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_Income, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            Divider(bar, GoldSegment);
            var elixir = Segment(bar, "elixir", GoldSegment, ElixirSegment, "icon_elixir", out textLeft);
            m_Elixir = PixelUi.Text(elixir, "elixir", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Elixir.rectTransform.Anchor(0, 0, 1, 1, new Vector2(textLeft, 0), new Vector2(-Px, 0));
            NumberText(m_Elixir);
            Divider(bar, ElixirSegment);
            var cats = Segment(bar, "cats", ElixirSegment, 1f, "icon_cat", out textLeft);
            m_Cats = PixelUi.Text(cats, "cats", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Cats.rectTransform.Anchor(0, 0, 1, 1, new Vector2(textLeft, 0), new Vector2(-Px, 0));
            NumberText(m_Cats);

            // Шестерёнка: окно настроек (звук и вибрация переехали туда).
            m_SettingsButton = PixelUi.IconButton(m_Safe, "Settings", "icon_gear", ShowSettings);
            var srt = m_SettingsButton.Rect;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.sizeDelta = new Vector2(TopBarHeight, TopBarHeight);
            srt.anchoredPosition = new Vector2(-Margin, -Margin);
        }

        /// <summary>Сегмент полосы валют от доли <paramref name="from"/> до <paramref name="to"/> внутри рамки: иконка слева.</summary>
        static RectTransform Segment(RectTransform bar, string name, float from, float to, string icon, out float textLeft)
        {
            var seg = Ui.Rect(bar, name).Anchor(from, 0, to, 1,
                new Vector2(from <= 0f ? BarPad : 2 * Px, BarPad), new Vector2(to >= 1f ? -BarPad : -2 * Px, -BarPad));
            var img = PixelUi.Icon(seg, "icon", icon);
            var size = PixelUi.IconSize(icon);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            img.rectTransform.anchoredPosition = new Vector2(Px + size.x * 0.5f, 0f);
            textLeft = Px + size.x + 2 * Px;
            return seg;
        }

        static void Divider(RectTransform bar, float at)
        {
            var d = PixelUi.Sliced(bar, "divider", "ui_divider_v");
            d.raycastTarget = false;
            d.rectTransform.Anchor(at, 0, at, 1, new Vector2(-2 * Px, BarPad), new Vector2(2 * Px, -BarPad));
        }

        /// <summary>Число в строку: без переноса, длинное (большие суффиксы) уменьшается, а не обрезается.</summary>
        static void NumberText(TMP_Text t)
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(t, PixelUi.FontSmall, t.fontSize);
        }

        /// <summary>Табличка в одну строку («Шахта N») или в две (с сектором и меткой Debug).</summary>
        void SetZoneLayout(bool twoLines)
        {
            if (twoLines == m_ZoneTwoLines) return;
            m_ZoneTwoLines = twoLines;
            Ui.SetActive(m_ZoneSub, twoLines);
            if (twoLines)
            {
                m_Zone.rectTransform.Anchor(0, 1, 1, 1, new Vector2(5 * Px, -19 * Px), new Vector2(-5 * Px, -3 * Px));
                m_ZoneSub.rectTransform.Anchor(0, 0, 1, 0, new Vector2(5 * Px, 3 * Px), new Vector2(-5 * Px, 11 * Px));
            }
            else m_Zone.rectTransform.Anchor(0, 0, 1, 1, new Vector2(5 * Px, 3 * Px), new Vector2(-5 * Px, -3 * Px));
        }

        void BuildNextMine()
        {
            m_NextMine = PixelUi.Button(m_Safe, "NextMine", Loc.T("hud.next_mine.next"), PixelButton.Green, () => m_Game.RequestNextMine());
            m_NextMine.Rect.Anchor(0.18f, 1, 0.82f, 1, new Vector2(0, -(TopUnits + Margin + NextMineHeight)), new Vector2(0, -(TopUnits + Margin)));
            m_NextMine.SetActive(false);
            // Причина вместо кнопки перехода (ТЗ 11.2): тёмная кнопка с пояснением, ведёт камеру к неактивированной шахте.
            m_NextMineReason = PixelUi.Button(m_Safe, "NextMineReason", "", PixelButton.Dark, () => m_Game.RequestNextMine(), PixelUi.FontBody);
            PixelUi.SetFont(m_NextMineReason.Label, false, true);
            Ui.AutoSize(m_NextMineReason.Label, PixelUi.FontSmall, PixelUi.FontBody);
            m_NextMineReason.Rect.Anchor(0.06f, 1, 0.94f, 1, new Vector2(0, -(TopUnits + Margin + ReasonHeight)), new Vector2(0, -(TopUnits + Margin)));
            m_NextMineReason.SetActive(false);
        }

        // ───────────── Низ ─────────────

        void BuildBottom()
        {
            // Контейнер всей нижней части: под экраном конца кампании скрывается целиком.
            m_Bottom = Ui.Rect(m_Safe, "Bottom").Anchor(0, 0, 1, 0, Vector2.zero, new Vector2(0, BottomUnits));

            var bar = PixelUi.Panel(m_Bottom, "BottomBar", PixelPanel.Dark).rectTransform;
            bar.Anchor(0, 0, 1, 0, new Vector2(Margin, Margin), new Vector2(-Margin, Margin + BarHeight));

            // Ячейки: сундуки и динамит слева, «Чертежи» справа, «Нанять» — всё место между ними.
            m_ChestsBadge = PixelUi.IconButton(bar, "ChestsBadge", "icon_chest_big", OnChestsBadgeClick);
            PlaceSlot(m_ChestsBadge.Rect, 0, false);
            m_ChestsBadge.SetInteractable(false);

            m_Dynamite = PixelUi.IconButton(bar, "Dynamite", "icon_dynamite_big", null);
            PlaceSlot(m_Dynamite.Rect, 1, false);
            m_Dynamite.Label.alignment = TextAlignmentOptions.BottomRight;
            m_Dynamite.Label.rectTransform.Anchor(0, 0, 1, 0.5f, new Vector2(3 * Px, 3 * Px), new Vector2(-4 * Px, 0));
            // Перенос динамита начинается нажатием на ячейку (PointerInput ведёт его дальше по полю).
            var trigger = m_Dynamite.Image.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(OnDynamitePointerDown);
            trigger.triggers.Add(entry);

            m_Blueprints = PixelUi.IconButton(bar, "Blueprints", "icon_cards_big", () => ToggleBlueprints());
            PlaceSlot(m_Blueprints.Rect, 0, true);

            BuildHire(bar);

            // Полоса над панелью: плашка бесплатного котика по центру, слева — свободные котики (только если они есть).
            var strip = Ui.Rect(m_Bottom, "Strip").Anchor(0, 0, 1, 0, new Vector2(Margin, Margin + BarHeight + Margin), new Vector2(-Margin, BottomUnits));
            m_IdleCats = PixelUi.Button(strip, "IdleCats", "", PixelButton.Dark, () => m_Game.FocusIdleCat(), PixelUi.FontLarge);
            // Иконка котика у левой кромки лица кнопки (рамка 6 пикселей арта), число — справа от неё.
            m_IdleCats.Rect.Anchor(0, 0, 0, 1, Vector2.zero, new Vector2(IdleWidth, 0));
            m_IdleCats.SetIcon("icon_cat");
            m_IdleCats.Icon.rectTransform.anchoredPosition = new Vector2(-8 * Px, Px);
            m_IdleCats.Label.alignment = TextAlignmentOptions.Left;
            m_IdleCats.Label.textWrappingMode = TextWrappingModes.NoWrap;
            m_IdleCats.Label.rectTransform.Anchor(0.5f, 0, 1, 1, new Vector2(2 * Px, 3 * Px), new Vector2(-6 * Px, -Px));
            m_IdleCats.SetActive(false);

            // Плашка по центру при любом состоянии кнопки свободных котиков: поля слева и справа равны.
            float side = IdleWidth + Margin;
            m_FreeCatPill = PixelUi.Panel(strip, "FreeCat", PixelPanel.Pill);
            m_FreeCatPill.raycastTarget = false;
            m_FreeCatPill.rectTransform.Anchor(0, 0.5f, 1, 0.5f, new Vector2(side, -PillHeight / 2), new Vector2(-side, PillHeight / 2));
            m_FreeCat = PixelUi.Text(m_FreeCatPill.transform, "text", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            m_FreeCat.rectTransform.Anchor(0, 0, 1, 1, new Vector2(5 * Px, Px), new Vector2(-5 * Px, 0));
            m_FreeCat.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_FreeCat, PixelUi.FontSmall, PixelUi.FontBody);
        }

        /// <summary>Ячейка нижней панели: <paramref name="index"/>-я от левого (или правого) края внутри рамки, по центру высоты.</summary>
        static void PlaceSlot(RectTransform rt, int index, bool right)
        {
            float x = BarPad + index * (PixelUi.SlotSize + SlotGap);
            rt.anchorMin = rt.anchorMax = new Vector2(right ? 1f : 0f, 0.5f);
            rt.pivot = new Vector2(right ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(PixelUi.SlotSize, PixelUi.SlotSize);
            rt.anchoredPosition = new Vector2(right ? -x : x, 0f);
        }

        /// <summary>
        /// «Нанять» (ТЗ 7.2, 6.6): золотая кнопка — сверху «Нанять» (или причина: лимит, нет места), под ней монета и цена,
        /// внизу уровень нанимаемого котика и шкала уровня найма. Все элементы — в Content: при нажатии опускаются вместе.
        /// </summary>
        void BuildHire(RectTransform bar)
        {
            m_Hire = PixelUi.Button(bar, "Hire", Loc.T("hud.hire.title"), PixelButton.Gold, () => m_Game.Hire(), PixelUi.FontTitle);
            float left = BarPad + 2 * (PixelUi.SlotSize + SlotGap), right = BarPad + PixelUi.SlotSize + SlotGap;
            m_Hire.Rect.Anchor(0, 0.5f, 1, 0.5f, new Vector2(left, -HireHeight / 2), new Vector2(-right, HireHeight / 2));
            var title = m_Hire.Label;
            title.rectTransform.Anchor(0, 1, 1, 1, new Vector2(4 * Px, -17 * Px), new Vector2(-4 * Px, -Px));
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(title, PixelUi.FontBody * 0.8f, PixelUi.FontTitle);

            m_HirePrice = PixelUi.IconText(m_Hire.Content, "price", "icon_coin", "", PixelUi.FontTitle, PixelUi.TextDark, true, false);
            m_HirePrice.Root.anchorMin = m_HirePrice.Root.anchorMax = new Vector2(0.5f, 0f);
            m_HirePrice.Root.anchoredPosition = new Vector2(0f, 18 * Px);

            m_HireLevel = PixelUi.Text(m_Hire.Content, "level", "", PixelUi.FontSmall, PixelUi.TextDark, TextAlignmentOptions.Right, true, false);
            m_HireLevel.textWrappingMode = TextWrappingModes.NoWrap;
            m_HireLevel.overflowMode = TextOverflowModes.Overflow;
            m_HireLevel.rectTransform.Anchor(0, 0, 0.34f, 0, new Vector2(4 * Px, 2 * Px), new Vector2(0, 10 * Px));
            m_HireGauge = PixelUi.Bar(m_Hire.Content, "gauge", PixelBarFill.Green);
            m_HireGauge.Rect.Anchor(0.34f, 0, 1, 0, new Vector2(2 * Px, 3 * Px), new Vector2(-6 * Px, 9 * Px));
        }

        // ───────────── Тост и призрак ─────────────

        void BuildToast()
        {
            m_ToastBg = PixelUi.Panel(m_Safe, "ToastBg", PixelPanel.Toast);
            m_ToastBg.raycastTarget = false;
            var rt = m_ToastBg.rectTransform;
            rt.anchorMin = new Vector2(0.07f, 1f);
            rt.anchorMax = new Vector2(0.93f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -ToastTop);
            rt.sizeDelta = new Vector2(0f, 20 * Px);
            m_Toast = PixelUi.Text(m_ToastBg.transform, "Toast", "", PixelUi.FontBody, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            m_Toast.rectTransform.Anchor(0, 0, 1, 1, new Vector2(6 * Px, 4 * Px), new Vector2(-6 * Px, -4 * Px));
            m_Toast.overflowMode = TextOverflowModes.Overflow;
            m_ToastBg.gameObject.SetActive(false);
        }

        public void Toast(string text, float seconds = 2.2f)
        {
            if (string.IsNullOrEmpty(text))
            {
                Ui.SetActive(m_ToastBg, false);
                return;
            }
            Ui.SetText(m_Toast, text);
            // Высота плашки — по тексту (тост — событие, не кадр: измерение здесь допустимо).
            float width = m_ToastBg.rectTransform.rect.width;
            if (width < 40 * Px) width = m_Safe.rect.width * 0.86f;
            float h = m_Toast.GetPreferredValues(text, Mathf.Max(10 * Px, width - 12 * Px), 0f).y;
            var rt = m_ToastBg.rectTransform;
            rt.sizeDelta = new Vector2(0f, Mathf.Max(20 * Px, PixelUi.Snap(h + 9 * Px)));
            Ui.SetActive(m_ToastBg, true);
            rt.SetAsLastSibling();
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>
        /// Нажатие на ячейку динамита: перенос начинается, призрак сразу встаёт под указатель. PointerDown приходит из
        /// EventSystem.Update, а он может идти после PointerInput.Update: иначе кадр призрак висит на прежнем месте
        /// (центр Canvas или точка прошлого броска). Позиция — из того же действия Point, по которому PointerInput
        /// двигает призрак дальше (второй палец во время переноса котика не даёт скачка на следующем кадре).
        /// </summary>
        void OnDynamitePointerDown(BaseEventData e)
        {
            var input = m_Game != null ? m_Game.Input : null;
            if (input == null || !input.isActiveAndEnabled) return;
            input.BeginDynamiteDrag();
            if (!input.IsDraggingDynamite) return;
            var point = input.GameplayMap != null ? input.GameplayMap.FindAction(PointerInput.PointActionName) : null;
            if (point != null && point.enabled) MoveDynamiteGhost(point.ReadValue<Vector2>());
            else if (e is PointerEventData pe) MoveDynamiteGhost(pe.position);
        }

        public void ShowDynamiteGhost(bool on)
        {
            Ui.SetActive(m_Ghost, on);
            if (on) m_Ghost.rectTransform.SetAsLastSibling();
        }

        public void MoveDynamiteGhost(Vector2 screenPos)
        {
            var canvas = m_Canvas != null ? m_Canvas : GetComponent<Canvas>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screenPos, null, out var local);
            var rt = m_Ghost.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local;
        }

        // ───────────── Добыча: полёт к счётчику ─────────────

        /// <summary>Пул летящих иконок; за один сбор — не больше <see cref="FlyMaxPerDrop"/>.</summary>
        const int FlyPool = 24, FlyMaxPerDrop = 10;
        /// <summary>Полёт одной иконки, задержка между соседними, «пульс» счётчика на каждую прилетевшую (с; рост масштаба).</summary>
        const float FlyTime = 0.6f, FlyStagger = 0.05f, PulseTime = 0.2f, PulseGrow = 0.25f;
        /// <summary>Разлёт от точки сбора перед полётом к счётчику, пиксели арта.</summary>
        const float FlyBurstMin = 10 * Px, FlyBurstMax = 24 * Px, FlyScatter = 3 * Px;
        /// <summary>Цели полёта: золото и эликсир — иконки верхней полосы, самоцветы — «Чертежи» (там их запас).</summary>
        const int FlyGold = 0, FlyElixir = 1, FlyHard = 2;
        static readonly string[] s_FlyIcons = { "icon_coin", "icon_elixir", "icon_gem" };

        struct FlyCoin
        {
            public bool Active;
            public float Delay, Age;
            public Vector2 From, Ctrl, To;
            public int Target;
        }

        RectTransform m_FlyLayer;
        readonly Image[] m_FlyImages = new Image[FlyPool];
        readonly FlyCoin[] m_Fly = new FlyCoin[FlyPool];
        readonly RectTransform[] m_FlyTargets = new RectTransform[3];
        readonly float[] m_PulseAge = new float[3];
        int m_FlyCount, m_PulseMask;

        void BuildLootFly()
        {
            m_FlyLayer = Ui.Rect(m_Safe, "LootFly").Anchor(0, 0, 1, 1);
            m_FlyLayer.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < FlyPool; i++)
            {
                var img = PixelUi.Icon(m_FlyLayer, "loot", s_FlyIcons[FlyGold]);
                img.gameObject.SetActive(false);
                m_FlyImages[i] = img;
            }
            // Иконки сегментов верхней полосы (Segment: ребёнок «icon» рядом с числом) и иконка «Чертежей».
            m_FlyTargets[FlyGold] = m_Gold.transform.parent.Find("icon") as RectTransform;
            m_FlyTargets[FlyElixir] = m_Elixir.transform.parent.Find("icon") as RectTransform;
            m_FlyTargets[FlyHard] = m_Blueprints.Icon != null ? m_Blueprints.Icon.rectTransform : null;
        }

        static int FlyTarget(string currency)
        {
            switch (currency)
            {
                case LootState.Gold: return FlyGold;
                case LootState.Elixir: return FlyElixir;
                case LootState.Hard: return FlyHard;
                default: return -1;
            }
        }

        /// <summary>Сколько иконок запускать за сбор суммы <paramref name="amount"/>: 1 + ⌊lg amount⌋, от 1 до 10.</summary>
        public static int LootFlyCount(GameNumber amount) =>
            amount.IsPositive ? (int)Math.Max(1L, Math.Min(FlyMaxPerDrop, 1L + amount.Exponent)) : 1;

        /// <summary>Сбор суммы <paramref name="amount"/>: число иконок — <see cref="LootFlyCount"/>.</summary>
        public void FlyLoot(Vector3 worldPos, string currency, GameNumber amount) => FlyLoot(worldPos, currency, LootFlyCount(amount));

        /// <summary>
        /// Добыча собрана: <paramref name="count"/> иконок валюты (1…10; по сумме — <see cref="LootFlyCount"/>) разлетаются из
        /// экранной точки <paramref name="worldPos"/> (главная камера, с учётом режима Canvas) и по дуге друг за другом летят
        /// к её счётчику за 0,6 с; счётчик «пульсирует» на каждую. <paramref name="currency"/> — "gold", "elixir" или "hard"
        /// (<see cref="LootState"/>). Без выделений: пул из 24 иконок, лишние не запускаются; под окнами HUD — только пульс.
        /// </summary>
        public void FlyLoot(Vector3 worldPos, string currency, int count)
        {
            int target = FlyTarget(currency);
            if (target < 0 || m_FlyLayer == null) return;
            var to = m_FlyTargets[target];
            if (to == null || !to.gameObject.activeInHierarchy) return;
            var rig = m_Game != null ? m_Game.Rig : null;
            var cam = rig != null && rig.Cam != null ? rig.Cam : Camera.main;
            var screen = cam != null ? cam.WorldToScreenPoint(worldPos) : Vector3.back;
            if (AnyWindowOpen || screen.z < 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(m_FlyLayer, screen, UiCamera, out var from))
            {
                Pulse(target);
                return;
            }
            var r = m_FlyLayer.rect;
            from = new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), Mathf.Clamp(from.y, r.yMin, r.yMax));
            Vector2 end = m_FlyLayer.InverseTransformPoint(to.position);
            var toward = (end - from) * 0.25f;
            count = Mathf.Clamp(count, 1, FlyMaxPerDrop);
            int launched = 0;
            for (int i = 0; i < FlyPool && launched < count; i++)
            {
                if (m_Fly[i].Active) continue;
                // Каждая иконка сначала отлетает в свою сторону от точки сбора, затем по дуге к счётчику.
                float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI), burst = UnityEngine.Random.Range(FlyBurstMin, FlyBurstMax);
                var start = from + new Vector2(UnityEngine.Random.Range(-FlyScatter, FlyScatter), UnityEngine.Random.Range(-FlyScatter, FlyScatter));
                m_Fly[i] = new FlyCoin
                {
                    Active = true, Delay = launched * FlyStagger, Age = 0f, Target = target,
                    From = start, Ctrl = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * burst + toward, To = end,
                };
                var img = m_FlyImages[i];
                PixelUi.SetIcon(img, s_FlyIcons[target]);
                img.rectTransform.anchoredPosition = PixelUi.Snap(start);
                launched++;
            }
            if (launched == 0)
            {
                Pulse(target);
                return;
            }
            m_FlyCount += launched;
            // Поверх панелей, тоста и подсказки обучения.
            m_FlyLayer.SetAsLastSibling();
        }

        void Pulse(int target)
        {
            m_PulseAge[target] = 0f;
            m_PulseMask |= 1 << target;
        }

        /// <summary>
        /// Полёт иконок (квадратичная кривая через точку разлёта, плавный старт и финиш, позиция по сетке пикселей арта)
        /// и «пульс» счётчиков: масштаб 1 → 1,25 → 1 за 0,2 с (короткий «поп», руководство 6.3). Без выделений.
        /// </summary>
        void UpdateLootFly(float dt)
        {
            for (int i = 0; i < FlyPool && m_FlyCount > 0; i++)
            {
                if (!m_Fly[i].Active) continue;
                ref var f = ref m_Fly[i];
                f.Age += dt;
                float t = (f.Age - f.Delay) / FlyTime;
                if (t < 0f) continue;
                var img = m_FlyImages[i];
                if (t >= 1f)
                {
                    f.Active = false;
                    m_FlyCount--;
                    img.gameObject.SetActive(false);
                    Pulse(f.Target);
                    continue;
                }
                if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                float k = t * t * (3f - 2f * t), u = 1f - k;
                img.rectTransform.anchoredPosition = PixelUi.Snap(u * u * f.From + 2f * u * k * f.Ctrl + k * k * f.To);
            }
            if (m_PulseMask == 0) return;
            for (int i = 0; i < m_PulseAge.Length; i++)
            {
                if ((m_PulseMask & (1 << i)) == 0) continue;
                var icon = m_FlyTargets[i];
                m_PulseAge[i] += dt;
                float k = m_PulseAge[i] / PulseTime;
                float s = 1f;
                if (k >= 1f) m_PulseMask &= ~(1 << i);
                else s = 1f + PulseGrow * Mathf.Sin(k * Mathf.PI);
                if (icon != null) icon.localScale = new Vector3(s, s, 1f);
            }
        }

        // ───────────── Доли панелей ─────────────

        /// <summary>
        /// Пересчёт <see cref="TopInset"/>/<see cref="BottomInset"/>: высоты панелей фиксированы в единицах Canvas, поэтому
        /// их доля зависит от высоты safe area в этих единицах. Берётся настоящий размер SafeArea, пока Canvas ещё не разложен —
        /// та же формула, что у CanvasScaler (1080×1920, match 0.5), по Screen.safeArea.
        /// </summary>
        void UpdateInsets()
        {
            float units = m_Safe != null ? m_Safe.rect.height : 0f;
            if (units < 100 * Px) units = ScreenSafeUnits();
            if (Mathf.Abs(units - m_InsetSafeUnits) < 0.01f) return;
            m_InsetSafeUnits = units;
            if (units <= 1f) return;
            m_TopInset = Mathf.Clamp(TopUnits / units, 0f, 0.45f);
            m_BottomInset = Mathf.Clamp(BottomUnits / units, 0f, 0.45f);
        }

        static float ScreenSafeUnits()
        {
            float w = Screen.width, h = Screen.height;
            if (w <= 0f || h <= 0f) return RefHeight;
            float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / RefWidth, 2f), Mathf.Log(h / RefHeight, 2f), RefMatch));
            float safe = Screen.safeArea.height;
            if (safe <= 0f) safe = h;
            return scale > 0f ? safe / scale : RefHeight;
        }

        /// <summary>Доли изменились (поворот, другое окно редактора): камера получает их сразу, как от GameController.</summary>
        void PushInsets()
        {
            var rig = m_Game != null ? m_Game.Rig : null;
            if (rig == null) return;
            float top = TopInset, bottom = BottomInset;
            if (Mathf.Approximately(top, m_PushedTop) && Mathf.Approximately(bottom, m_PushedBottom)) return;
            m_PushedTop = top;
            m_PushedBottom = bottom;
            rig.SetUiInsets(top, bottom);
        }

        // ───────────── Обновление ─────────────

        // GameController создаёт обучение сразу после HUD в том же Init: к Start оно уже есть, и первая же подсказка
        // идёт пузырём, а не тостом.
        void Start() => WireTutorialHint();

        /// <summary>Передать подсказку контроллеру обучения (он создаётся после HUD).</summary>
        void WireTutorialHint()
        {
            var tutorial = m_Game != null ? m_Game.Tutorial : null;
            if (tutorial != null && tutorial.Hint == null && Hint != null) tutorial.Hint = Hint;
        }

        // ───────────── Переход в следующую шахту (ТЗ 11.2) ─────────────

        Image m_TransitionCover;

        /// <summary>Идёт анимация перехода: HUD не обновляется (показывает старую шахту), касания ловит экран перехода.</summary>
        public bool InTransition { get; private set; }

        /// <summary>
        /// Начать переход: прозрачный экран на весь Canvas (и под вырезом) ловит все касания, подсказка обучения и
        /// контекстная панель скрыты, периодическое обновление HUD остановлено. Затемнение — <see cref="SetTransitionFade"/>.
        /// </summary>
        public void BeginTransition()
        {
            InTransition = true;
            if (m_TransitionCover == null)
            {
                var rt = Ui.Rect(transform, "TransitionCover").Anchor(0, 0, 1, 1);
                m_TransitionCover = rt.gameObject.AddComponent<Image>();
                m_TransitionCover.raycastTarget = true;
            }
            SetTransitionFade(0f);
            m_TransitionCover.gameObject.SetActive(true);
            m_TransitionCover.rectTransform.SetAsLastSibling();
            if (Hint != null && Hint.Visible) Hint.Hide();
            HideContext();
        }

        /// <summary>Затемнение экрана перехода: 0 — прозрачно, 1 — тёмно-синий navy0 (ступенями по 1/8, без плавного градиента).</summary>
        public void SetTransitionFade(float k)
        {
            if (m_TransitionCover == null) return;
            float a = Mathf.Round(Mathf.Clamp01(k) * 8f) / 8f;
            var c = PixelPalette.Navy0;
            m_TransitionCover.color = new Color(c.r, c.g, c.b, a);
        }

        /// <summary>Конец перехода: экран перехода убран, HUD снова обновляется сам.</summary>
        public void EndTransition()
        {
            InTransition = false;
            if (m_TransitionCover != null) m_TransitionCover.gameObject.SetActive(false);
            m_NextRefresh = 0f;
        }

        void Update()
        {
            if (m_ToastBg.gameObject.activeSelf && Time.unscaledTime > m_ToastUntil) m_ToastBg.gameObject.SetActive(false);
            if (InTransition) return;
            if (m_FlyCount > 0 || m_PulseMask != 0) UpdateLootFly(Time.unscaledDeltaTime);
            if (m_ModalCurrent != null && m_Safe.rect.size != m_ModalLayoutSize) LayoutModal(m_ModalCurrent);
            if (CampaignEndVisible && m_Safe.rect.size != m_EndLayoutSize) LayoutCampaignEnd();
            if (Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 0.1f;
                Refresh();
            }
        }

        /// <summary>
        /// Обновить HUD. Каждая строка пересобирается, только если изменились её исходные значения
        /// (золото, эликсир, счётчики, таймеры с точностью до секунды, выбор, уровни, HP) — ТЗ 15.3.
        /// </summary>
        public void Refresh()
        {
            WireTutorialHint();
            if (m_ModalCurrent != null && m_Safe.rect.size != m_ModalLayoutSize) LayoutModal(m_ModalCurrent);
            if (CampaignEndVisible && m_Safe.rect.size != m_EndLayoutSize) LayoutCampaignEnd();

            // Экран конца кампании закрывает нижнюю часть и контекстную панель (ТЗ 11.4).
            bool end = CampaignEndVisible;
            Ui.SetActive(m_Bottom, !end);
            if (end) HideContext();
            var s = m_Game != null ? m_Game.Session : null;
            if (s == null) return;
            if (!ReferenceEquals(m_CacheMine, s.Mine)) ResetCaches(s);
            PushInsets();
            RefreshTopBar(s);
            if (!end)
            {
                RefreshHire(s);
                RefreshStrip(s);
                RefreshBottomButtons(s);
            }
            RefreshNextMine(s, end);
            RefreshChests(s, end);
            if (!end) RefreshContext(s);
            if (BlueprintsOpen) RefreshBlueprintsHeader(s);
            if (SettingsOpen) RefreshSettings(s);
        }

        void ResetCaches(GameSession s)
        {
            m_CacheMine = s.Mine;
            m_ZoneGates = m_ZoneDebug = m_CatsSlots = m_CatsCap = -1;
            m_IdleShown = m_DynamiteShown = m_BlueprintsStockShown = m_FreeCatState = m_HireColors = -1;
            m_FreeCatKey = long.MinValue;
            m_MoneyValid = m_HireValid = m_NextMineValid = m_CtxValid = m_ChestsValid = m_BpValid = m_BpRowsValid = false;
        }

        static bool VibrationOn(GameSession s) => s.Player.Settings == null || s.Player.Settings.VibrationOn;

        /// <summary>Сколько шахт зоны активировано (производит): от этого зависят условие перехода и его причина.</summary>
        static int ProducingShafts(GameSession s)
        {
            int n = 0;
            var shafts = s.Layout.Shafts;
            for (int i = 0; i < shafts.Count; i++) if (s.Mine.Objects[shafts[i].Ordinal].Status == ObjectStatus.Producing) n++;
            return n;
        }

        static long CeilSeconds(double seconds) =>
            double.IsInfinity(seconds) || double.IsNaN(seconds) ? -1 : (long)Math.Ceiling(Math.Max(0, seconds) - 1e-9);

        void RefreshTopBar(GameSession s)
        {
            int gates = s.Mine.GatesOpened, debug = s.IsDebugSlot ? 1 : 0;
            if (gates != m_ZoneGates || debug != m_ZoneDebug)
            {
                m_ZoneGates = gates;
                m_ZoneDebug = debug;
                int sectors = s.Layout.SectorCount;
                Ui.SetText(m_Zone, Loc.F("hud.top.zone", s.Mine.Zone));
                string sub = sectors > 1 ? Loc.F("hud.top.sector", gates + 1, sectors) : null;
                if (debug == 1) sub = sub != null ? sub + "  " + DebugMarker : DebugMarker;
                SetZoneLayout(sub != null);
                if (sub != null) Ui.SetText(m_ZoneSub, sub);
            }

            var gold = s.Mine.Gold;
            var elixir = s.Player.Elixir;
            var income = s.AutoIncomePerSecond();
            if (!m_MoneyValid || gold != m_GoldShown) Ui.SetText(m_Gold, NumberFormat.Format(gold));
            if (!m_MoneyValid || elixir != m_ElixirShown) Ui.SetText(m_Elixir, NumberFormat.Format(elixir));
            if (!m_MoneyValid || income != m_IncomeShown) Ui.SetText(m_Income, Loc.F("hud.top.income", NumberFormat.Format(income)));
            m_GoldShown = gold;
            m_ElixirShown = elixir;
            m_IncomeShown = income;
            m_MoneyValid = true;

            int slots = s.OccupiedSlots, cap = s.UnitCap();
            if (slots != m_CatsSlots || cap != m_CatsCap)
            {
                m_CatsSlots = slots;
                m_CatsCap = cap;
                Ui.SetText(m_Cats, Loc.F("hud.top.cats_count", slots, cap));
            }
        }

        /// <summary>
        /// Кнопка найма (ТЗ 7.2, 6.6): всегда цена и уровень котика, шкала уровня найма; при лимите вместо «Нанять» —
        /// «Лимит котиков N/M», без свободной клетки — «Нет места» (и когда золота тоже не хватает).
        /// Нехватка золота — кнопка недоступна, цена красная.
        /// </summary>
        void RefreshHire(GameSession s)
        {
            var m = s.Mine;
            var price = s.HirePrice();
            int cap = s.UnitCap();
            var key = new ValueKey
            {
                A = m.HireCount, B = s.OccupiedSlots, C = cap, D = m.RocksDestroyed, E = m.Cats.Count, F = m.Containers.Count,
                G = m.GatesOpened, H = (s.Player.CampaignCompleted ? 1 : 0) | (m.Gold >= price ? 2 : 0), N1 = price,
            };
            if (m_HireValid && key.Equals(m_HireKey)) return;
            m_HireKey = key;
            m_HireValid = true;

            var hire = s.CanHire();
            bool noSpace = hire == CommandResult.NoSpace || (hire == CommandResult.NotEnoughGold && s.FindSpawnCell() < 0);
            string title;
            if (hire == CommandResult.CapReached) title = Loc.F("hud.hire.cap", s.OccupiedSlots, cap);
            else if (noSpace) title = Loc.T("hud.hire.no_space");
            else title = Loc.T("hud.hire.title");
            m_Hire.SetLabel(title);
            m_HirePrice.SetText(NumberFormat.Format(price));
            Ui.SetText(m_HireLevel, Loc.F("hud.hire.level", s.HireLevel()));
            m_HireGauge.SetValue(Mathf.Clamp01((float)s.HireGauge()));
            bool ok = hire == CommandResult.Ok;
            m_Hire.SetInteractable(ok);
            SetHireColors(ok ? 0 : (hire == CommandResult.NotEnoughGold && !noSpace ? 1 : 2));
        }

        /// <summary>Цвета цены и уровня: 0 — тёмные на золоте, 1 — не хватает золота (цена красная), 2 — недоступно по другой причине.</summary>
        void SetHireColors(int state)
        {
            if (state == m_HireColors) return;
            m_HireColors = state;
            bool on = state == 0;
            var price = m_HirePrice.Text;
            price.color = state == 1 ? PixelUi.TextBad : (on ? PixelUi.TextDark : PixelUi.TextDim);
            PixelUi.SetShadow(price, !on);
            m_HirePrice.Icon.color = on ? Color.white : PixelUi.DisabledIconTint;
            m_HireLevel.color = on ? PixelUi.TextDark : PixelUi.TextDim;
            PixelUi.SetShadow(m_HireLevel, !on);
        }

        /// <summary>Плашка бесплатного котика (таймер, «ждёт места», «ждёт на поле») и кнопка свободных котиков.</summary>
        void RefreshStrip(GameSession s)
        {
            // Бесплатный котик: 1 — ждёт на поле, 2 — ждёт места, 0 — таймер (с точностью до секунды).
            bool containerOnField = false;
            var boxes = s.Mine.Containers;
            for (int i = 0; i < boxes.Count; i++) if (boxes[i].Kind == ContainerKind.FreeCat) containerOnField = true;
            int state = containerOnField ? 1 : (s.Mine.FreeCatReady || s.OccupiedSlots >= s.UnitCap() ? 2 : 0);
            long secs = state == 0 ? CeilSeconds(GameSession.TicksToSeconds(s.Mine.FreeCatTimerTicks)) : 0;
            long key = secs * 4 + state;
            if (key != m_FreeCatKey)
            {
                m_FreeCatKey = key;
                if (state == 1) Ui.SetText(m_FreeCat, Loc.T("hud.free_cat.on_field"));
                else if (state == 2) Ui.SetText(m_FreeCat, Loc.T("hud.free_cat.waiting_space"));
                else Ui.SetText(m_FreeCat, Loc.F("hud.free_cat.timer", NumberFormat.FormatDuration(GameSession.TicksToSeconds(s.Mine.FreeCatTimerTicks))));
                if (state != m_FreeCatState)
                {
                    m_FreeCatState = state;
                    m_FreeCat.color = state == 1 ? PixelUi.TextAccentLight : (state == 2 ? PixelUi.TextDim : PixelUi.TextColor);
                }
            }

            int idle = 0;
            var cats = s.Mine.Cats;
            for (int i = 0; i < cats.Count; i++) if (cats[i].TargetIndex < 0) idle++;
            if (idle != m_IdleShown)
            {
                m_IdleShown = idle;
                m_IdleCats.SetActive(idle > 0);
                if (idle > 0) m_IdleCats.SetLabel(PixelUi.CountText(idle));
            }
        }

        void RefreshBottomButtons(GameSession s)
        {
            int dynamite = s.Player.Dynamite;
            if (dynamite != m_DynamiteShown)
            {
                m_DynamiteShown = dynamite;
                m_Dynamite.SetLabel(Loc.F("hud.dynamite.count", dynamite));
                m_Dynamite.SetInteractable(dynamite > 0);
            }
            // Красный бейдж «Карт», как на референсе: сколько карт можно улучшить, плюс готовые бесплатные сундуки.
            int badge = UpgradeableCards(s) + s.Player.FreeChest.Stock;
            if (badge != m_BlueprintsStockShown)
            {
                m_BlueprintsStockShown = badge;
                m_Blueprints.SetBadge(badge);
            }
        }

        /// <summary>
        /// Кнопка перехода (ТЗ 11.2, 11.4, 13.1) — только при выполненных условиях; иначе при разрушенном выходе
        /// вместо неё причина «Шахты: активировано a/b», нажатие ведёт камеру к неактивированной шахте.
        /// </summary>
        void RefreshNextMine(GameSession s, bool end)
        {
            var exit = s.Mine.Objects[s.Layout.Exit.Ordinal].Status;
            var key = new ValueKey
            {
                A = (int)exit, B = ProducingShafts(s), C = s.Mine.GatesOpened,
                D = (s.Player.CampaignCompleted ? 1 : 0) | (end ? 2 : 0),
            };
            if (m_NextMineValid && key.Equals(m_NextMineKey)) return;
            m_NextMineKey = key;
            m_NextMineValid = true;

            bool canComplete = s.CanCompleteZone(out string reason);
            m_NextMine.SetActive(canComplete && !end);
            m_NextMine.SetLabel(s.Mine.Zone >= s.Balance.ZoneCount ? Loc.T("hud.next_mine.finish") : Loc.T("hud.next_mine.next"));
            bool showReason = !canComplete && exit == ObjectStatus.Broken && !s.Player.CampaignCompleted && !end;
            m_NextMineReason.SetActive(showReason);
            if (showReason)
            {
                m_NextMineReason.SetLabel(reason);
                m_NextMineReason.SetInteractable(s.NearestInactiveShaft() >= 0);
            }
        }

        /// <summary>
        /// Очередь неоткрытых сундуков (ТЗ 10.3): красный бейдж с числом на кнопке сундуков; на экране конца кампании —
        /// кнопка «Открыть сундуки: N». Пока открыто окно, кнопка недоступна (сундук откроется после него).
        /// </summary>
        void RefreshChests(GameSession s, bool end)
        {
            var reveals = s.Player.ChestReveals;
            int n = reveals != null ? reveals.Count : 0;
            bool show = n > 0 && !m_ModalOpen;
            var key = new ValueKey { A = show ? n : 0, B = end ? 1 : 0 };
            if (m_ChestsValid && key.Equals(m_ChestsKey)) return;
            m_ChestsKey = key;
            m_ChestsValid = true;

            m_ChestsBadge.SetBadge(show ? n : 0);
            m_ChestsBadge.SetInteractable(show && !end);
            bool endButton = show && end;
            if (m_EndChests.Rect.gameObject.activeSelf != endButton)
            {
                m_EndChests.SetActive(endButton);
                m_End.UpdateBodyArea();
                // Рамка экрана конца — по содержимому: ряд кнопки появился или пропал.
                LayoutCampaignEnd();
            }
            if (endButton) m_EndChests.SetLabel(Loc.F("hud.chests.open_all", n));
        }

        void OnChestsBadgeClick()
        {
            if (OnChestsBadgeTap != null)
            {
                OnChestsBadgeTap();
                return;
            }
            // Контроллер не подписан: первый сундук очереди показывается, а из очереди уходит только по «Забрать».
            var s = m_Game.Session;
            var chest = s?.PeekChestReveal();
            if (chest == null || m_ModalOpen) return;
            ShowChest(chest, () =>
            {
                if (ReferenceEquals(s.PeekChestReveal(), chest)) s.TakeChestReveal();
                m_Game.MarkDirty();
            });
        }

        void ToggleVibration()
        {
            var s = m_Game.Session;
            if (s == null) return;
            s.SetVibrationOn(!VibrationOn(s));
            m_Game.MarkDirty();
            Refresh();
        }

        // ───────────── Геометрия для стрелок у краёв ─────────────

        float ScreenEdgeY(RectTransform rt, bool top)
        {
            rt.GetWorldCorners(s_Corners);
            return RectTransformUtility.WorldToScreenPoint(UiCamera, top ? s_Corners[1] : s_Corners[0]).y;
        }

        /// <summary>Верхний край открытой контекстной панели в пикселях экрана.</summary>
        internal bool TryGetContextTop(out float screenY)
        {
            if (m_Context != null && m_Context.gameObject.activeInHierarchy)
            {
                screenY = ScreenEdgeY(m_Context, true);
                return true;
            }
            screenY = 0f;
            return false;
        }

        /// <summary>Нижний край кнопки перехода или причины под верхней панелью, если они показаны.</summary>
        internal bool TryGetTopObstacleBottom(out float screenY)
        {
            screenY = float.MaxValue;
            bool any = false;
            if (m_NextMine != null && m_NextMine.Rect.gameObject.activeInHierarchy)
            {
                screenY = Mathf.Min(screenY, ScreenEdgeY(m_NextMine.Rect, false));
                any = true;
            }
            if (m_NextMineReason != null && m_NextMineReason.Rect.gameObject.activeInHierarchy)
            {
                screenY = Mathf.Min(screenY, ScreenEdgeY(m_NextMineReason.Rect, false));
                any = true;
            }
            return any;
        }
    }
}
