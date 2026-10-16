using System;
using System.Collections.Generic;
using System.Text;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using BigInteger = System.Numerics.BigInteger;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// HUD режима CatOutpostV2 (Docs/Rework/RUNTIME_SPEC.md §5, ТЗ 5, 9–11, 13–14) на пиксельном ките, только через контракт
    /// <see cref="IOutpostGame"/>. Холст, CanvasScaler, SafeArea и доли панелей — как у старого Hud (Docs/Rework/map/ui.md §1, §4).
    /// Верх: табличка «Шахта N» (+ «сектор k/m»), полоса: доставленное золото | несданный груз (icon_sack, «в пути») |
    /// прогресс расчистки «разрушено/всего» (с воротами) | число котиков; шестерёнка настроек. Низ (ТЗ 2.2 §6.2): Store · Cards ·
    /// широкая золотая «База» с бейджем доступных покупок · Market · Equipment · To base; над ними — строка фазы («Доставляем оставшиеся монеты»,
    /// «Перейти на следующую шахту», «Повторить сохранение», расчёт офлайна). Окна: «База», «Экипировка», сведения о котике/цели, «Итоги шахты»,
    /// «Пока вас не было», конец кампании, ошибка, настройки; стек окон и Back (верхнее окно → выбор).
    /// Тексты обновляются не чаще 10 раз в секунду и только при смене исходных значений (кэш по значению, ТЗ 19).
    /// Части класса: OutpostHud.BasePanel.cs, .Equipment.cs, .Info.cs, .Results.cs, .Settings.cs, .Cards.cs, .Store.cs, .Market.cs.
    /// API — Docs/Rework/UI_API.md.
    /// </summary>
    public sealed partial class OutpostHud : MonoBehaviour
    {
        // Эталон CanvasScaler — как у старого Hud и ProjectSetup.
        const float RefWidth = 1080f, RefHeight = 1920f, RefMatch = 0.5f;

        // ── Раскладка, единицы Canvas (1 пиксель арта = 4 единицы). Высоты панелей передаются камере через Safe Area.
        const float Px = PixelUi.Px;
        const float Margin = 2 * Px;
        const float TopBarHeight = 30 * Px;
        /// <summary>Нижний край верхней полосы от верха safe area — <see cref="TopInset"/>.</summary>
        const float TopUnits = Margin + TopBarHeight;
        const float BarHeight = 52 * Px, BarPad = 5 * Px;
        /// <summary>Кнопки нижней панели занимают всю высоту внутри рамки.</summary>
        const float BottomButtonHeight = BarHeight - 2 * BarPad;
        /// <summary>
        /// Пять кнопок, по две с каждой стороны «Базы» (решение владельца 16.10.2026: «To base» убрана ради симметрии): боковые —
        /// 40 пикселей арта с зазором 3, как до заводов; «База» по центру занимает остаток — ≈ 55 пикселей арта на 1080×2400 и
        /// ≈ 84 на 1080×1920 (не меньше <see cref="BaseMinWidth"/>).
        /// </summary>
        const float SideButtonWidth = 40 * Px, SlotGap = 3 * Px;
        /// <summary>Минимальная ширина «Базы» между боковыми кнопками (ТЗ 2.2 §6.2).</summary>
        public const float BaseMinWidth = 44 * Px;
        /// <summary>Полоса над панелью: строка фазы или крупная кнопка перехода/повтора.</summary>
        const float StripHeight = 44 * Px, PillHeight = 16 * Px;
        /// <summary>Верхний край нижней части HUD от низа safe area — <see cref="BottomInset"/>.</summary>
        const float BottomUnits = Margin + BarHeight + Margin + StripHeight;
        const float PlaqueFraction = 0.20f;
        /// <summary>
        /// Сегменты полосы: золото | груз | прогресс | котики — доли её ширины (27 / 26 / 27 / 20 %). Прогресс шире прежнего:
        /// при 9:20 «56/56» и «144/144» цифрами Pixeloid помещаются без многоточия (текст ≈74 ед.).
        /// </summary>
        const float GoldSegment = 0.27f, CargoSegment = 0.53f, ProgressSegment = 0.80f;
        /// <summary>Тост — под верхней полосой: отступ Margin и ещё 28 пикселей арта.</summary>
        const float ToastTop = TopUnits + Margin + 24 * Px + 4 * Px;
        /// <summary>Поля окна кита: тело на 14 пикселей арта ниже верха рамки, низ — 12 пикселей.</summary>
        const float WindowTop = 14 * Px, WindowBottom = 12 * Px;

        /// <summary>Метка debug-профиля на табличке (служебная, не переводится).</summary>
        const string DebugMarker = "<color=#FF9070>[DEBUG]</color>";
        const string BadTag = "<color=#FF9070>", ColorClose = "</color>";

        // ── Имена элементов HUD для указателей (FindTarget; подсказки квестов playable). Не ключи словаря.
        public const string TargetBase = "ui.base";
        public const string TargetEquipment = "ui.equipment";
        public const string TargetGold = "ui.gold";
        public const string TargetCargo = "ui.cargo";
        public const string TargetStripAction = "ui.strip_action";
        public const string TargetResultsEquipment = "ui.results.equipment";
        public const string TargetResultsNext = "ui.results.next";
        public const string TargetEquipBest = "ui.equip_best";
        static readonly string[] s_BranchTargets =
            { "ui.branch.team", "ui.branch.pickaxes", "ui.branch.boots", "ui.branch.bags", "ui.branch.unload", "ui.branch.value" };

        /// <summary>Элемент для указателя — кнопка покупки ветки в панели «База».</summary>
        public static string BranchTarget(UpgradeBranch branch) => s_BranchTargets[(int)branch];

        /// <summary>Набор исходных значений текста: строка собирается заново, только когда набор изменился.</summary>
        struct ValueKey : IEquatable<ValueKey>
        {
            public int A, B, C, D, E, F, G, H;
            public long L;
            public BigInteger N1, N2;

            public bool Equals(ValueKey o) =>
                A == o.A && B == o.B && C == o.C && D == o.D && E == o.E && F == o.F && G == o.G && H == o.H && L == o.L &&
                N1 == o.N1 && N2 == o.N2;

            public override bool Equals(object obj) => obj is ValueKey k && Equals(k);

            public override int GetHashCode() => (A * 397) ^ (B * 31) ^ (C << 3) ^ (D << 7) ^ L.GetHashCode();
        }

        /// <summary>Окно в стеке HUD: Back вызывает <see cref="Back"/>; null — окно требует решения (Back поглощается).</summary>
        sealed class WindowEntry
        {
            public RectTransform Root;
            public Action Back;
        }

        /// <summary>
        /// Место под баннер playable-сборки у нижнего края safe area, единицы Canvas: весь HUD (панели, окна, подсказки)
        /// поднимается над ним, доли <see cref="BottomInset"/>/<see cref="TopInset"/> для камеры и ввода считаются от всей safe
        /// area. 0 — обычная раскладка. Ставится до загрузки сцены; тесты сбрасывают в TearDown.
        /// </summary>
        public static float BottomReserve;

        IOutpostGame m_Game;
        Canvas m_Canvas;
        RectTransform m_Safe;
        SafeArea m_SafeArea;

        // Верх.
        TextMeshProUGUI m_Zone, m_ZoneSub;
        TextMeshProUGUI m_Gold, m_GoldLabel, m_Cargo, m_CargoLabel, m_Progress, m_Workers;
        RectTransform m_GoldSegment, m_CargoSegment, m_GoldIcon;
        Ui.ButtonRefs m_SettingsButton;
        bool m_ZoneTwoLines = true;

        // Низ.
        RectTransform m_Bottom;
        Ui.ButtonRefs m_BaseButton, m_EquipButton;
        RectTransform m_Strip;
        Image m_StatusPill;
        TextMeshProUGUI m_StatusText;
        Ui.ButtonRefs m_StripButton;
        int m_StripState = -1, m_StripPercent = -1;

        Image m_ToastBg;
        TextMeshProUGUI m_Toast;
        float m_ToastUntil;

        float m_NextRefresh;
        readonly StringBuilder m_Sb = new StringBuilder(256);
        static readonly Vector3[] s_Corners = new Vector3[4];
        readonly OutpostFlowStats m_Flow = new OutpostFlowStats();

        // Кэш показанных значений. Смена объекта run (новая шахта, загрузка, офлайн) сбрасывает всё.
        OutpostRun m_CacheRun;
        int m_ZoneShown = -1, m_ZoneGates = -1, m_ZoneGateCount = -1, m_ZoneDebug = -1;
        int m_ReleasedShown = -1, m_DeliveredShown = -1, m_DestroyedShown = -1, m_RequiredShown = -1, m_WorkersShown = -1;
        bool m_MoneyValid;
        BigInteger m_WalletShown;
        int m_BaseBadgeShown = -1, m_EquipBadgeShown = -1, m_BottomEnabledShown = -1;
        // Цена следующего уровня ветки (Quote ×1 ядра) — пересчёт только при смене уровня или шахты: бейдж сравнивает с кошельком.
        readonly BigInteger?[] m_NextCost = new BigInteger?[OutpostIds.BranchCount];
        readonly int[] m_NextCostLevel = new int[OutpostIds.BranchCount];
        /// <summary>Растёт на каждое изменение экипировки и уровней предметов: ключ пересчёта бейджа и эффектов.</summary>
        int m_EquipVersion;
        int m_EquipBadgeVersion = -1, m_EquipBadgeItems = -1;
        long m_EquipBadgeParts = -1;
        OutpostSelectionKind m_SelKindSeen = OutpostSelectionKind.None;

        // Доли панелей: пересчёт только при смене размера safe area.
        float m_InsetSafeUnits = -1f, m_InsetReserve, m_TopInset = 0.07f, m_BottomInset = 0.17f;
        float m_PushedTop = -1f, m_PushedBottom = -1f;

        // Стек окон (порядок открытия = порядок слоёв: Show ставит окно последним ребёнком).
        readonly List<WindowEntry> m_Stack = new List<WindowEntry>(8);
        int m_BackFrame = -1;
        bool m_BackResult;

        // Элементы HUD по именам Target* (заполняются в Build).
        readonly Dictionary<string, RectTransform> m_Targets = new Dictionary<string, RectTransform>(StringComparer.Ordinal);

        // ───────────── Публичное API ─────────────

        /// <summary>Контракт контроллера, переданный в <see cref="Create"/>.</summary>
        public IOutpostGame Game => m_Game;

        /// <summary>
        /// Камера поля: доли панелей уходят в <see cref="CameraRig.SetUiInsets"/> сразу при изменении, полёт монет считает
        /// экранную точку через <see cref="CameraRig.Cam"/>. null — только событие <see cref="InsetsChanged"/> и Camera.main.
        /// </summary>
        public CameraRig Rig { get; set; }

        /// <summary>Камера для перевода мировых точек в экранные (полёт монет); null — Rig.Cam или Camera.main.</summary>
        public Camera WorldCamera { get; set; }

        /// <summary>Элемент HUD по имени из констант Target* (кнопки панели, сегменты, строки «Базы», кнопки итогов); null — нет.</summary>
        public RectTransform FindTarget(string id) =>
            id != null && m_Targets.TryGetValue(id, out var rt) && rt != null ? rt : null;

        /// <summary>
        /// Опрашивать системную кнопку Back (Android Back = Escape в Input System, действие UI/Cancel модуля ввода) самому.
        /// false — контроллер/ввод вызывает <see cref="HandleBack"/> сам. Двойной вызов в одном кадре безопасен.
        /// </summary>
        public bool PollBackButton = true;

        /// <summary>Запускать полёт монет к кошельку по событию CargoDeposited самому (контроллеру не нужно звать FlyCoins).</summary>
        public bool AutoFlyOnDeposit = true;

        /// <summary>Открыто любое окно HUD (с затемнением): поле не должно получать касания. Без выделений.</summary>
        public bool AnyWindowOpen
        {
            get
            {
                Prune();
                return m_Stack.Count > 0;
            }
        }

        /// <summary>Открыто окно, которое Back не закрывает (ошибка, расчёт офлайна, решение в модальном окне).</summary>
        public bool BlockingWindowOpen
        {
            get
            {
                Prune();
                return m_Stack.Count > 0 && m_Stack[m_Stack.Count - 1].Back == null;
            }
        }

        /// <summary>
        /// Открылось любое окно HUD (в том числе окно предмета поверх «Экипировки»): контроллер отменяет текущий жест
        /// прокрутки/выбора (ТЗ 14), чтобы отпускание пальца не нажало кнопку в новом окне.
        /// </summary>
        public event Action OnWindowOpened;

        /// <summary>Доли панелей изменились (top, bottom — доли высоты safe area): для CameraRig.SetUiInsets.</summary>
        public event Action<float, float> InsetsChanged;

        /// <summary>Нижний край верхней полосы — доля высоты safe area от её верха.</summary>
        public float TopInset
        {
            get
            {
                UpdateInsets();
                return m_TopInset;
            }
        }

        /// <summary>Верхний край нижней части HUD — доля высоты safe area от её низа.</summary>
        public float BottomInset
        {
            get
            {
                UpdateInsets();
                return m_BottomInset;
            }
        }

        /// <summary>Полоса поля между панелями HUD в пикселях экрана (от safe area, как CameraRig). Без выделений.</summary>
        public void FieldBandPixels(out float bottom, out float top)
        {
            var safe = Screen.safeArea;
            if (safe.height <= 0f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            bottom = safe.yMin + BottomInset * safe.height;
            top = safe.yMax - TopInset * safe.height;
        }

        /// <summary>Камера Canvas для пересчёта экранных точек: null у Screen Space Overlay.</summary>
        internal Camera UiCamera => m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;

        /// <summary>
        /// HUD на Canvas «HUD» из сцены (если на нём нет старого Hud и нового HUD), иначе Canvas создаётся кодом.
        /// Screen Space Overlay, sortingOrder 30000, CanvasScaler 1080×1920 (match 0.5), GraphicRaycaster, SafeArea.
        /// HUD подписывается на <see cref="IOutpostGame.EventPresented"/> (полёт монет, подсказка узкого места).
        /// </summary>
        public static OutpostHud Create(IOutpostGame game, CameraRig rig = null)
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
            canvas.sortingOrder = 30000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = RefMatch;
            if (canvasGo.GetComponent<GraphicRaycaster>() == null) canvasGo.AddComponent<GraphicRaycaster>();
            var hud = canvasGo.AddComponent<OutpostHud>();
            hud.m_Game = game;
            hud.m_Canvas = canvas;
            hud.Rig = rig;
            hud.Build((RectTransform)canvasGo.transform);
            if (game != null) game.EventPresented += hud.OnGameEvent;
            return hud;
        }

        /// <summary>Корневой Canvas «HUD» сцены без HUD-компонентов (включая выключенный).</summary>
        static GameObject FindSceneCanvas()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null || c.transform.parent != null || c.gameObject.name != "HUD") continue;
                if (!c.gameObject.scene.IsValid() || c.GetComponent<OutpostHud>() != null || c.GetComponent<Hud>() != null) continue;
                if (!(c.transform is RectTransform)) continue;
                return c.gameObject;
            }
            return null;
        }

        void OnDestroy()
        {
            if (m_Game != null) m_Game.EventPresented -= OnGameEvent;
        }

        void Build(RectTransform root)
        {
            m_Safe = root.Find("SafeArea") as RectTransform;
            if (m_Safe == null) m_Safe = Ui.Rect(root, "SafeArea").Anchor(0, 0, 1, 1);
            m_SafeArea = m_Safe.GetComponent<SafeArea>();
            if (m_SafeArea == null) m_SafeArea = m_Safe.gameObject.AddComponent<SafeArea>();
            m_SafeArea.BottomReserve = Reserve;

            // Порядок детей SafeArea — порядок слоёв: панели HUD, сведения, тост, полёт монет, окна.
            BuildTopBar();
            BuildBottom();
            BuildInfo();
            BuildToast();
            BuildLootFly();

            BuildEquipment();
            BuildCards();
            BuildStore();
            BuildMarket();
            BuildBase();
            BuildSettings();
            BuildDialogs();

            m_Targets[TargetBase] = m_BaseButton.Rect;
            m_Targets[TargetEquipment] = m_EquipButton.Rect;
            m_Targets[TargetCards] = m_CardsButton.Rect;
            m_Targets[TargetStore] = m_StoreButton.Rect;
            m_Targets[TargetMarket] = m_MarketButton.Rect;
            m_Targets[TargetGold] = m_GoldSegment;
            m_Targets[TargetCargo] = m_CargoSegment;
            m_Targets[TargetStripAction] = m_StripButton.Rect;
            m_Targets[TargetEquipBest] = m_EquipBestButton.Rect;
            for (int i = 0; i < m_Rows.Length; i++) m_Targets[s_BranchTargets[i]] = m_Rows[i].Buy.Rect;
            m_Targets[TargetResultsEquipment] = m_Results.ButtonAt(0).Rect;
            m_Targets[TargetResultsNext] = m_Results.ButtonAt(1).Rect;
        }

        // ───────────── Верх ─────────────

        void BuildTopBar()
        {
            var plaque = PixelUi.Panel(m_Safe, "ZonePlaque", PixelPanel.Wood);
            plaque.rectTransform.Anchor(0, 1, PlaqueFraction, 1, new Vector2(Margin, -TopUnits), new Vector2(0, -Margin));
            m_Zone = PixelUi.Text(plaque.transform, "zone", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Center, true, true);
            m_Zone.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_Zone, PixelUi.FontBody * 0.75f, PixelUi.FontLarge);
            m_ZoneSub = PixelUi.Text(plaque.transform, "sector", "", PixelUi.FontSmall, PixelUi.TextColor, TextAlignmentOptions.Center, false, true);
            m_ZoneSub.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_ZoneSub, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            SetZoneLayout(false);

            var bar = PixelUi.Panel(m_Safe, "TopBar", PixelPanel.Dark).rectTransform;
            bar.Anchor(PlaqueFraction, 1, 1, 1, new Vector2(Margin, -TopUnits), new Vector2(-(Margin + TopBarHeight + Margin), -Margin));

            // Доставленное золото: число и подпись «золото» — единственное, с чем сравниваются цены (ТЗ 14).
            m_GoldSegment = Segment(bar, "gold", 0f, GoldSegment, "icon_coin", out float textLeft);
            m_GoldIcon = m_GoldSegment.Find("icon") as RectTransform;
            m_Gold = PixelUi.Text(m_GoldSegment, "gold", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Gold.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, 6 * Px), new Vector2(-Px, 21 * Px));
            NumberText(m_Gold);
            // Суммы с двухбуквенным суффиксом («1.42aa») при 9:20 уменьшаются, а не обрезаются, как прогресс и котики.
            Ui.AutoSize(m_Gold, PixelUi.FontSmall * 0.75f, PixelUi.FontLarge);
            m_GoldLabel = PixelUi.Text(m_GoldSegment, "label", Loc.T("outpost.top.gold_label"), PixelUi.FontSmall, PixelUi.TextAccent, TextAlignmentOptions.Left, false, true);
            m_GoldLabel.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, Px), new Vector2(-Px, 9 * Px));
            m_GoldLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_GoldLabel, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            Divider(bar, GoldSegment);

            // Несданный груз (на земле и в сумках): другая иконка, холодный цвет и подпись «в пути» — это ещё не кошелёк.
            m_CargoSegment = Segment(bar, "cargo", GoldSegment, CargoSegment, "icon_sack", out textLeft);
            m_Cargo = PixelUi.Text(m_CargoSegment, "cargo", "", PixelUi.FontLarge, PixelUi.TextIncome, TextAlignmentOptions.Left, true, true);
            m_Cargo.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, 6 * Px), new Vector2(-Px, 21 * Px));
            NumberText(m_Cargo);
            Ui.AutoSize(m_Cargo, PixelUi.FontSmall * 0.75f, PixelUi.FontLarge);
            m_CargoLabel = PixelUi.Text(m_CargoSegment, "label", Loc.T("outpost.top.cargo_label"), PixelUi.FontSmall, PixelUi.TextIncome, TextAlignmentOptions.Left, false, true);
            m_CargoLabel.rectTransform.Anchor(0, 0, 1, 0, new Vector2(textLeft, Px), new Vector2(-Px, 9 * Px));
            m_CargoLabel.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_CargoLabel, PixelUi.FontSmall * 0.7f, PixelUi.FontSmall);
            Divider(bar, CargoSegment);

            var progress = Segment(bar, "progress", CargoSegment, ProgressSegment, "icon_pickaxe", out textLeft);
            m_Progress = PixelUi.Text(progress, "progress", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Progress.rectTransform.Anchor(0, 0, 1, 1, new Vector2(textLeft, 0), new Vector2(-Px, 0));
            NumberText(m_Progress);
            Ui.AutoSize(m_Progress, PixelUi.FontSmall * 0.75f, PixelUi.FontLarge);
            Divider(bar, ProgressSegment);

            var cats = Segment(bar, "workers", ProgressSegment, 1f, "icon_cat", out textLeft);
            m_Workers = PixelUi.Text(cats, "workers", "", PixelUi.FontLarge, PixelUi.TextColor, TextAlignmentOptions.Left, true, true);
            m_Workers.rectTransform.Anchor(0, 0, 1, 1, new Vector2(textLeft, 0), new Vector2(-Px, 0));
            NumberText(m_Workers);
            Ui.AutoSize(m_Workers, PixelUi.FontSmall * 0.75f, PixelUi.FontLarge);

            m_SettingsButton = PixelUi.IconButton(m_Safe, "Settings", "icon_gear", ShowSettings);
            var srt = m_SettingsButton.Rect;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.sizeDelta = new Vector2(TopBarHeight, TopBarHeight);
            srt.anchoredPosition = new Vector2(-Margin, -Margin);
        }

        /// <summary>
        /// Сегмент верхней полосы: иконка 16×16 и место под число. При 9:20 полоса узкая, поэтому поля у краёв полосы — 3 пикселя
        /// арта, зазор иконка–текст — 1: пятизначная сумма («1.42K») помещается в FontSmall.
        /// </summary>
        static RectTransform Segment(RectTransform bar, string name, float from, float to, string icon, out float textLeft)
        {
            var seg = Ui.Rect(bar, name).Anchor(from, 0, to, 1,
                new Vector2(from <= 0f ? 3 * Px : 2 * Px, BarPad), new Vector2(to >= 1f ? -3 * Px : -2 * Px, -BarPad));
            var img = PixelUi.Icon(seg, "icon", icon);
            var size = PixelUi.IconSize(icon);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            img.rectTransform.anchoredPosition = new Vector2(Px + size.x * 0.5f, 0f);
            textLeft = Px + size.x + Px;
            return seg;
        }

        static void Divider(RectTransform bar, float at)
        {
            var d = PixelUi.Sliced(bar, "divider", "ui_divider_v");
            d.raycastTarget = false;
            d.rectTransform.Anchor(at, 0, at, 1, new Vector2(-2 * Px, BarPad), new Vector2(2 * Px, -BarPad));
        }

        static void NumberText(TMP_Text t)
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(t, PixelUi.FontSmall, t.fontSize);
        }

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

        // ───────────── Низ ─────────────

        void BuildBottom()
        {
            m_Bottom = Ui.Rect(m_Safe, "Bottom").Anchor(0, 0, 1, 0, Vector2.zero, new Vector2(0, BottomUnits));
            var bar = PixelUi.Panel(m_Bottom, "BottomBar", PixelPanel.Dark).rectTransform;
            bar.Anchor(0, 0, 1, 0, new Vector2(Margin, Margin), new Vector2(-Margin, Margin + BarHeight));

            // Порядок (решение владельца 16.10.2026): Store · Cards · Base · Equipment · Market — по две кнопки с каждой стороны,
            // «База» ровно по центру; «To base» убрана (камера к лагерю — прокруткой поля).
            m_StoreButton = SideButton(bar, "Store", "icon_store", Loc.T("outpost.bottom.store"), false, ShowStore);
            m_StoreButton.Badge = PixelUi.Badge(m_StoreButton.Image.transform, "badge");
            m_StoreButton.Badge.PlaceAtCorner(new Vector2(1f, 1f), PixelUi.BadgeCornerOffset);
            m_StoreButton.Badge.SetDot(false);
            m_CardsButton = SideButton(bar, "Cards", "icon_cards_big", Loc.T("cards.bottom"), false, ShowCards);
            m_CardsButton.Rect.anchoredPosition += new Vector2(SideButtonWidth + SlotGap, 0);
            m_EquipButton = SideButton(bar, "Equipment", "icon_equipment", Loc.T("outpost.bottom.equipment"), true, () => ShowEquipment());
            m_EquipButton.Rect.anchoredPosition -= new Vector2(SideButtonWidth + SlotGap, 0);
            m_EquipButton.Badge = PixelUi.Badge(m_EquipButton.Image.transform, "badge");
            m_EquipButton.Badge.PlaceAtCorner(new Vector2(1f, 1f), PixelUi.BadgeCornerOffset);
            m_EquipButton.Badge.SetDot(false);
            BuildMarketButton(bar);

            // «База»: главная кнопка экрана, бейдж — сколько покупок ×1 доступно по доставленному золоту.
            m_BaseButton = PixelUi.Button(bar, "Base", Loc.T("outpost.bottom.base"), PixelButton.Gold, ToggleBase, PixelUi.FontTitle);
            float side = BarPad + 2 * (SideButtonWidth + SlotGap);
            m_BaseButton.Rect.Anchor(0, 0.5f, 1, 0.5f, new Vector2(side, -BottomButtonHeight / 2), new Vector2(-side, BottomButtonHeight / 2));
            // Одна подпись по центру лица кнопки: «For this mine» владелец убрал 13 октября 2026 (охват видно в самой панели).
            var title = m_BaseButton.Label;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(title, PixelUi.FontBody, PixelUi.FontTitle);

            // Полоса над панелью: строка фазы (плашка) или кнопка перехода / повторного сохранения.
            m_Strip = Ui.Rect(m_Bottom, "Strip").Anchor(0, 0, 1, 0, new Vector2(Margin, Margin + BarHeight + Margin), new Vector2(-Margin, BottomUnits));
            m_StatusPill = PixelUi.Panel(m_Strip, "Status", PixelPanel.Pill);
            m_StatusPill.raycastTarget = false;
            m_StatusPill.rectTransform.Anchor(0.06f, 0.5f, 0.94f, 0.5f, new Vector2(0, -PillHeight / 2), new Vector2(0, PillHeight / 2));
            m_StatusText = PixelUi.Text(m_StatusPill.transform, "text", "", PixelUi.FontBody, PixelUi.TextAccentLight, TextAlignmentOptions.Center, false, true);
            m_StatusText.rectTransform.Anchor(0, 0, 1, 1, new Vector2(5 * Px, Px), new Vector2(-5 * Px, 0));
            m_StatusText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(m_StatusText, PixelUi.FontSmall, PixelUi.FontBody);
            m_StatusPill.gameObject.SetActive(false);
            m_StripButton = PixelUi.Button(m_Strip, "StripAction", "", PixelButton.Green, OnStripButton, PixelUi.FontTitle);
            m_StripButton.Rect.Anchor(0, 0f, 1, 1f, new Vector2(0, Px), new Vector2(0, -Px));
            m_StripButton.Label.textWrappingMode = TextWrappingModes.Normal;
            m_StripButton.Label.enableAutoSizing = false;
            m_StripButton.SetActive(false);

            // Недоступные кнопки (офлайн-расчёт, переход, загрузка) по тапу называют причину (ТЗ 14).
            AddDeniedTap(m_BaseButton, OnCommandDenied);
            AddDeniedTap(m_EquipButton, OnCommandDenied);
            AddDeniedTap(m_StoreButton, OnCommandDenied);
            AddDeniedTap(m_StripButton, OnResultsNextDenied);
        }

        void OnCommandDenied()
        {
            if (!CommandsAllowed(out string reason)) Toast(OutpostUiText.Reason(reason));
        }

        /// <summary>Боковая кнопка нижней панели: тёмная, иконка сверху, подпись снизу (всё в Content — тонет при нажатии).</summary>
        static Ui.ButtonRefs SideButton(RectTransform bar, string name, string icon, string label, bool right, UnityEngine.Events.UnityAction onClick)
        {
            var b = PixelUi.Button(bar, name, label, PixelButton.Dark, onClick, PixelUi.FontBody);
            var rt = b.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(right ? 1f : 0f, 0.5f);
            rt.pivot = new Vector2(right ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(SideButtonWidth, BottomButtonHeight);
            rt.anchoredPosition = new Vector2(right ? -BarPad : BarPad, 0f);
            b.SetIcon(icon);
            b.Icon.rectTransform.anchoredPosition = new Vector2(0f, 6 * Px);
            b.Label.rectTransform.Anchor(0, 0, 1, 0, new Vector2(2 * Px, 3 * Px), new Vector2(-2 * Px, 13 * Px));
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            // Подписи боковых кнопок не мельче FontSmall (ТЗ 2.2 §6.2).
            Ui.AutoSize(b.Label, PixelUi.FontSmall, PixelUi.FontBody);
            return b;
        }

        // ───────────── Тост ─────────────

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

        /// <summary>Короткое сообщение под верхней полосой (поверх окон, лучи не ловит).</summary>
        public void Toast(string text, float seconds = 2.2f)
        {
            if (string.IsNullOrEmpty(text))
            {
                Ui.SetActive(m_ToastBg, false);
                return;
            }
            Ui.SetText(m_Toast, text);
            float width = m_ToastBg.rectTransform.rect.width;
            if (width < 40 * Px) width = (m_Safe.rect.width > 100 * Px ? m_Safe.rect.width : RefWidth) * 0.86f;
            float h = m_Toast.GetPreferredValues(text, Mathf.Max(10 * Px, width - 12 * Px), 0f).y;
            var rt = m_ToastBg.rectTransform;
            rt.sizeDelta = new Vector2(0f, Mathf.Max(20 * Px, PixelUi.Snap(h + 9 * Px)));
            Ui.SetActive(m_ToastBg, true);
            rt.SetAsLastSibling();
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Причина отказа команды тостом и звуком отказа.</summary>
        void Denied(string reasonCode)
        {
            UiSound.PlayDenied();
            Toast(OutpostUiText.Reason(reasonCode));
        }

        // ───────────── Полёт монет к кошельку ─────────────

        const int FlyPool = 24, FlyMaxPerDrop = 6;
        const float FlyTime = 0.6f, FlyStagger = 0.05f, PulseTime = 0.2f, PulseGrow = 0.25f;
        const float FlyBurstMin = 10 * Px, FlyBurstMax = 24 * Px, FlyScatter = 3 * Px;
        /// <summary>Не чаще одного запуска за это время (сдача идёт порциями): остальные — только «пульс» кошелька.</summary>
        const float FlyMinInterval = 0.06f;

        struct FlyCoin
        {
            public bool Active;
            public float Delay, Age;
            public Vector2 From, Ctrl, To;
        }

        RectTransform m_FlyLayer;
        readonly Image[] m_FlyImages = new Image[FlyPool];
        readonly FlyCoin[] m_Fly = new FlyCoin[FlyPool];
        int m_FlyCount;
        bool m_Pulse;
        float m_PulseAge, m_LastFly = -10f;
        /// <summary>Что «пульсирует» по прилёте монет: кошелёк верхней полосы или строка золота окна рынка.</summary>
        RectTransform m_PulseIcon;

        void BuildLootFly()
        {
            m_FlyLayer = Ui.Rect(m_Safe, "LootFly").Anchor(0, 0, 1, 1);
            m_FlyLayer.pivot = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < FlyPool; i++)
            {
                var img = PixelUi.Icon(m_FlyLayer, "coin", "icon_coin");
                img.gameObject.SetActive(false);
                m_FlyImages[i] = img;
            }
        }

        Camera FieldCamera()
        {
            if (WorldCamera != null) return WorldCamera;
            if (Rig != null && Rig.Cam != null) return Rig.Cam;
            return Camera.main;
        }

        /// <summary>
        /// Монеты сданы на склад (ТЗ 7.3): <paramref name="count"/> монет (1…6) из мировой точки <paramref name="world"/> по дуге
        /// летят к иконке доставленного золота, кошелёк «пульсирует». Только для CargoDeposited — подбор и выпуск груза кошелёк
        /// не пополняют. Пул из 24 иконок, без выделений; под окнами HUD и при частой сдаче — только пульс.
        /// </summary>
        public void FlyCoins(Vector3 world, int count)
        {
            if (m_FlyLayer == null || m_GoldIcon == null || !m_GoldIcon.gameObject.activeInHierarchy) return;
            float now = Time.unscaledTime;
            var cam = FieldCamera();
            var screen = cam != null ? cam.WorldToScreenPoint(world) : Vector3.back;
            if (AnyWindowOpen || now - m_LastFly < FlyMinInterval || screen.z < 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(m_FlyLayer, screen, UiCamera, out var from))
            {
                Pulse(m_GoldIcon);
                return;
            }
            m_LastFly = now;
            var r = m_FlyLayer.rect;
            from = new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), Mathf.Clamp(from.y, r.yMin, r.yMax));
            Vector2 end = m_FlyLayer.InverseTransformPoint(m_GoldIcon.position);
            LaunchCoins(from, end, count, m_GoldIcon);
        }

        /// <summary>Запуск до <paramref name="count"/> монет из пула по дуге (локальные точки слоя полёта); по прилёте — пульс <paramref name="pulse"/>.</summary>
        void LaunchCoins(Vector2 from, Vector2 end, int count, RectTransform pulse)
        {
            var toward = (end - from) * 0.25f;
            count = Mathf.Clamp(count, 1, FlyMaxPerDrop);
            int launched = 0;
            for (int i = 0; i < FlyPool && launched < count; i++)
            {
                if (m_Fly[i].Active) continue;
                float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI), burst = UnityEngine.Random.Range(FlyBurstMin, FlyBurstMax);
                var start = from + new Vector2(UnityEngine.Random.Range(-FlyScatter, FlyScatter), UnityEngine.Random.Range(-FlyScatter, FlyScatter));
                m_Fly[i] = new FlyCoin
                {
                    Active = true, Delay = launched * FlyStagger, Age = 0f,
                    From = start, Ctrl = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * burst + toward, To = end,
                };
                m_FlyImages[i].rectTransform.anchoredPosition = PixelUi.Snap(start);
                launched++;
            }
            if (launched == 0)
            {
                Pulse(pulse);
                return;
            }
            SetPulseIcon(pulse);
            m_FlyCount += launched;
            m_FlyLayer.SetAsLastSibling();
        }

        void Pulse(RectTransform icon)
        {
            SetPulseIcon(icon);
            m_PulseAge = 0f;
            m_Pulse = true;
        }

        /// <summary>Пульс текущей цели (монета долетела).</summary>
        void Pulse()
        {
            m_PulseAge = 0f;
            m_Pulse = m_PulseIcon != null;
        }

        /// <summary>Сменить цель пульса: прежняя возвращается к обычному масштабу.</summary>
        void SetPulseIcon(RectTransform icon)
        {
            if (icon == null || icon == m_PulseIcon) return;
            if (m_PulseIcon != null) m_PulseIcon.localScale = Vector3.one;
            m_PulseIcon = icon;
        }

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
                    Pulse();
                    continue;
                }
                if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                float k = t * t * (3f - 2f * t), u = 1f - k;
                img.rectTransform.anchoredPosition = PixelUi.Snap(u * u * f.From + 2f * u * k * f.Ctrl + k * k * f.To);
            }
            if (!m_Pulse || m_PulseIcon == null) return;
            m_PulseAge += dt;
            float p = m_PulseAge / PulseTime;
            float s = 1f;
            if (p >= 1f) m_Pulse = false;
            else s = 1f + PulseGrow * Mathf.Sin(p * Mathf.PI);
            m_PulseIcon.localScale = new Vector3(s, s, 1f);
        }

        // ───────────── Доли панелей ─────────────

        static float Reserve => Mathf.Max(0f, BottomReserve);

        void UpdateInsets()
        {
            float reserve = Reserve;
            if (m_SafeArea != null && m_SafeArea.BottomReserve != reserve) m_SafeArea.BottomReserve = reserve;
            // Высота всей safe area: SafeArea поднимает низ HUD на резерв (offsetMin.y), а камера и ввод считают доли от всей
            // Screen.safeArea — резерв входит в нижнюю долю.
            float units = m_Safe != null ? m_Safe.rect.height + m_Safe.offsetMin.y : 0f;
            if (units < 100 * Px) units = ScreenSafeUnits();
            if (Mathf.Abs(units - m_InsetSafeUnits) < 0.01f && reserve == m_InsetReserve) return;
            m_InsetSafeUnits = units;
            m_InsetReserve = reserve;
            if (units <= 1f) return;
            m_TopInset = Mathf.Clamp(TopUnits / units, 0f, 0.45f);
            m_BottomInset = Mathf.Clamp((BottomUnits + reserve) / units, 0f, 0.45f);
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

        /// <summary>Доли изменились (поворот, вырез, окно редактора): камера получает их сразу.</summary>
        void PushInsets()
        {
            float top = TopInset, bottom = BottomInset;
            if (Mathf.Approximately(top, m_PushedTop) && Mathf.Approximately(bottom, m_PushedBottom)) return;
            m_PushedTop = top;
            m_PushedBottom = bottom;
            if (Rig != null) Rig.SetUiInsets(top, bottom);
            InsetsChanged?.Invoke(top, bottom);
        }

        // ───────────── Стек окон и Back ─────────────

        /// <summary>Окно открылось: наверх стека, сведения о выборе скрыты, контроллер отменяет жест (<see cref="OnWindowOpened"/>).</summary>
        void Opened(WindowEntry w)
        {
            m_Stack.Remove(w);
            m_Stack.Add(w);
            HideInfo();
            OnWindowOpened?.Invoke();
        }

        void Closed(WindowEntry w) => m_Stack.Remove(w);

        /// <summary>Убрать из стека окна, скрытые в обход HUD (например, вместе с родителем). Без выделений.</summary>
        void Prune()
        {
            for (int i = m_Stack.Count - 1; i >= 0; i--)
            {
                var root = m_Stack[i].Root;
                if (root == null || !root.gameObject.activeInHierarchy) m_Stack.RemoveAt(i);
            }
        }

        /// <summary>
        /// Системная кнопка Back (ТЗ 14): закрывает верхнее окно (окно предмета → «Экипировка» → …), затем снимает выбор.
        /// Окно, требующее решения (ошибка, расчёт офлайна, вопрос), Back поглощает без закрытия; во время перехода — тоже.
        /// Условия завершения шахты Back не пропускает. true — нажатие обработано. Повтор в том же кадре ничего не делает.
        /// </summary>
        public bool HandleBack()
        {
            if (m_BackFrame == Time.frameCount) return m_BackResult;
            m_BackFrame = Time.frameCount;
            m_BackResult = DoBack();
            return m_BackResult;
        }

        bool DoBack()
        {
            Prune();
            if (InTransition) return true;
            if (m_Stack.Count > 0)
            {
                var top = m_Stack[m_Stack.Count - 1];
                top.Back?.Invoke();
                return true;
            }
            if (m_Game != null && m_Game.Selection.Kind != OutpostSelectionKind.None)
            {
                m_Game.ClearSelection();
                HideInfo();
                return true;
            }
            return false;
        }

        static bool BackPressedThisFrame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            return cancel != null && cancel.enabled && cancel.WasPressedThisFrame();
        }

        /// <summary>Закрыть все окна, которые можно закрыть (переход, загрузка другой шахты).</summary>
        public void CloseAllWindows()
        {
            HideItem();
            HideEquipment();
            HideCards();
            HideStore();
            HideMarket();
            HideBase();
            HideSettings();
            HideResults();
            HideCampaign();
        }

        // ───────────── Переход в следующую шахту ─────────────

        Image m_TransitionCover;

        /// <summary>Идёт затемнение перехода: HUD не обновляется, касания ловит экран перехода.</summary>
        public bool InTransition { get; private set; }

        /// <summary>Начать затемнение: экран на весь Canvas ловит касания, окна и сведения закрыты, подсказка скрыта.</summary>
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
            CloseAllWindows();
            HideInfo();
        }

        /// <summary>Затемнение 0…1 ступенями по 1/8 (navy0).</summary>
        public void SetTransitionFade(float k)
        {
            if (m_TransitionCover == null) return;
            float a = Mathf.Round(Mathf.Clamp01(k) * 8f) / 8f;
            var c = PixelPalette.Navy0;
            m_TransitionCover.color = new Color(c.r, c.g, c.b, a);
        }

        /// <summary>Конец перехода: экран убран, HUD обновляется сразу.</summary>
        public void EndTransition()
        {
            InTransition = false;
            if (m_TransitionCover != null) m_TransitionCover.gameObject.SetActive(false);
            m_NextRefresh = 0f;
        }

        // ───────────── Кадр ─────────────

        void Update()
        {
            if (m_ToastBg.gameObject.activeSelf && Time.unscaledTime > m_ToastUntil) m_ToastBg.gameObject.SetActive(false);
            FlushCardToasts();
            FlushGoodsSold();
            if (PollBackButton && BackPressedThisFrame()) HandleBack();
            if (InTransition) return;
            if (m_FlyCount > 0 || m_Pulse) UpdateLootFly(FrameTime.Delta);
            RelayoutDialogs();
            var sim = m_Game != null ? m_Game.Sim : null;
            if (sim != null)
            {
                m_Flow.Tick(sim, Time.unscaledTime);
                CheckBaseSelection();
            }
            if (Time.unscaledTime >= m_NextRefresh) RefreshNow();
        }

        /// <summary>Обновить HUD не чаще 10 раз в секунду (можно звать каждый кадр: лишние вызовы пропускаются).</summary>
        public void Refresh()
        {
            if (Time.unscaledTime >= m_NextRefresh) RefreshNow();
        }

        /// <summary>
        /// Обновить HUD сейчас (после команды, загрузки, перехода). Каждая строка пересобирается, только если изменились её
        /// исходные значения.
        /// </summary>
        public void RefreshNow()
        {
            m_NextRefresh = Time.unscaledTime + 0.1f;
            PushInsets();
            var sim = m_Game != null ? m_Game.Sim : null;
            // Сначала сброс кэшей новой шахты (закрывает итоги прежней), затем окна по состоянию (итоги этой шахты).
            if (sim != null && sim.Run != null && !ReferenceEquals(m_CacheRun, sim.Run)) ResetCaches(sim.Run);
            UpdateAutoWindows();
            if (sim == null || sim.Run == null)
            {
                // Шахты ещё нет (загрузка, ошибка): нижние кнопки не показываются — им нечего открывать.
                HideInfo();
                Ui.SetActive(m_Bottom, false);
                return;
            }
            Ui.SetActive(m_Bottom, true);
            RefreshTopBar(sim);
            RefreshBottom(sim);
            RefreshStrip(sim);
            RefreshInfo(sim);
            if (BaseOpen) RefreshBase(sim);
            if (EquipmentOpen) RefreshEquipment();
            if (CardsOpen) RefreshCards();
            if (StoreOpen) RefreshStore();
            if (MarketOpen) RefreshMarket();
            if (SettingsOpen) RefreshSettings();
            if (ResultsOpen) RefreshResultsButtons();
        }

        void ResetCaches(OutpostRun run)
        {
            bool newRun = m_CacheRun == null || m_CacheRun.RunId != run.RunId;
            m_CacheRun = run;
            m_ZoneShown = m_ZoneGates = m_ZoneGateCount = m_ZoneDebug = -1;
            m_ReleasedShown = m_DeliveredShown = m_DestroyedShown = m_RequiredShown = m_WorkersShown = -1;
            m_BaseBadgeShown = m_EquipBadgeShown = m_BottomEnabledShown = m_MarketButtonShown = -1;
            m_StripState = m_StripPercent = -1;
            m_MoneyValid = false;
            for (int i = 0; i < m_NextCostLevel.Length; i++) m_NextCostLevel[i] = int.MinValue;
            m_EquipVersion++;
            InvalidateBase();
            InvalidateEquipment();
            InvalidateInfo();
            InvalidateMarket();
            if (newRun)
            {
                m_Flow.Reset();
                // Новая шахта: окна прежней закрыты (итоги, конец кампании); экипировку можно оставить открытой.
                HideResults();
                HideCampaign();
                HideBase();
            }
        }

        /// <summary>
        /// Тап по постройке лагеря (Selection = Base): открыть панель «База» и снять выбор. Тап по складу товаров
        /// (Selection = Warehouse, ТЗ 2.2 §6.1) — так же открыть рынок.
        /// </summary>
        void CheckBaseSelection()
        {
            var kind = m_Game.Selection.Kind;
            if (kind == m_SelKindSeen) return;
            m_SelKindSeen = kind;
            if (kind != OutpostSelectionKind.Base && kind != OutpostSelectionKind.Warehouse) return;
            m_Game.ClearSelection();
            m_SelKindSeen = OutpostSelectionKind.None;
            if (kind == OutpostSelectionKind.Warehouse) ShowMarket();
            else ShowBase();
        }

        /// <summary>Команды игрока доступны: идёт обычная игра (не офлайн-расчёт, не переход, не загрузка).</summary>
        bool CommandsAllowed(out string reason)
        {
            reason = null;
            if (m_Game == null || m_Game.Sim == null)
            {
                reason = "loading";
                return false;
            }
            if (InTransition || m_Game.InTransition || m_Game.Phase == OutpostControllerPhase.Transitioning)
            {
                reason = "transition";
                return false;
            }
            switch (m_Game.Phase)
            {
                case OutpostControllerPhase.Playing:
                    return true;
                case OutpostControllerPhase.OfflineCatchUp:
                    reason = "offline";
                    return false;
                default:
                    reason = "loading";
                    return false;
            }
        }

        void RefreshTopBar(OutpostSimulation sim)
        {
            var run = sim.Run;
            var map = sim.Map;
            int gates = map.Gates != null ? map.Gates.Length : 0, opened = run.GatesOpened, debug = m_Game.IsDebugProfile ? 1 : 0;
            if (run.Zone != m_ZoneShown || opened != m_ZoneGates || gates != m_ZoneGateCount || debug != m_ZoneDebug)
            {
                m_ZoneShown = run.Zone;
                m_ZoneGates = opened;
                m_ZoneGateCount = gates;
                m_ZoneDebug = debug;
                Ui.SetText(m_Zone, Loc.F("outpost.top.zone", run.Zone));
                // Секторов на один больше, чем ворот; текущий — следующий за открытыми воротами.
                string sub = gates > 0 ? Loc.F("outpost.top.sector", Math.Min(opened + 1, gates + 1), gates + 1) : null;
                if (debug == 1) sub = sub != null ? sub + "  " + DebugMarker : DebugMarker;
                SetZoneLayout(sub != null);
                if (sub != null) Ui.SetText(m_ZoneSub, sub);
            }

            if (!m_MoneyValid || run.Wallet != m_WalletShown)
            {
                m_WalletShown = run.Wallet;
                Ui.SetText(m_Gold, OutpostUiText.Money(run.Wallet));
            }
            // Несданный груз = выпущено − зачислено (точные счётчики ядра): на земле, в сумках, в очереди и на разгрузке.
            int released = run.Counters.ReleasedUnits, delivered = run.Counters.DeliveredUnits;
            if (!m_MoneyValid || released != m_ReleasedShown || delivered != m_DeliveredShown)
            {
                m_ReleasedShown = released;
                m_DeliveredShown = delivered;
                var cargo = run.Counters.ReleasedValue - run.Counters.DeliveredValue;
                if (cargo.Sign < 0) cargo = BigInteger.Zero;
                Ui.SetText(m_Cargo, OutpostUiText.Money(cargo));
                var color = cargo.IsZero ? PixelUi.TextDim : PixelUi.TextIncome;
                if (m_Cargo.color != color) m_Cargo.color = color;
            }
            m_MoneyValid = true;

            int destroyed = 0;
            var targets = run.Targets;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].Destroyed) destroyed++;
            int required = map.RequiredCount;
            if (destroyed != m_DestroyedShown || required != m_RequiredShown)
            {
                m_DestroyedShown = destroyed;
                m_RequiredShown = required;
                Ui.SetText(m_Progress, Loc.F("outpost.top.progress", destroyed, required));
            }
            int workers = run.Workers.Count;
            if (workers != m_WorkersShown)
            {
                m_WorkersShown = workers;
                Ui.SetText(m_Workers, PixelUi.CountText(workers));
            }
        }

        void RefreshBottom(OutpostSimulation sim)
        {
            RefreshCardsBadge();
            bool allowed = CommandsAllowed(out _);
            int enabled = allowed ? 1 : 0;
            bool enabledChanged = enabled != m_BottomEnabledShown;
            if (enabledChanged)
            {
                m_BottomEnabledShown = enabled;
                m_BaseButton.SetInteractable(allowed);
                m_EquipButton.SetInteractable(allowed);
                m_CardsButton.SetInteractable(allowed);
                m_StoreButton.SetInteractable(allowed);
            }
            RefreshMarketButton(allowed);
            RefreshStoreBadge(allowed);
            int badge = allowed ? AffordableCount(sim) : 0;
            if (badge != m_BaseBadgeShown)
            {
                m_BaseBadgeShown = badge;
                m_BaseButton.SetBadge(badge);
            }
            // Пересчёт только при смене профиля: версия (экипировка, улучшения), детали и размер коллекции (награды).
            var profile = m_Game.State != null ? m_Game.State.Profile : null;
            long parts = profile != null ? profile.Parts : -1;
            int items = profile != null && profile.Items != null ? profile.Items.Count : -1;
            if (enabledChanged || m_EquipBadgeVersion != m_EquipVersion || m_EquipBadgeParts != parts || m_EquipBadgeItems != items || m_EquipBadgeShown < 0)
            {
                m_EquipBadgeVersion = m_EquipVersion;
                m_EquipBadgeParts = parts;
                m_EquipBadgeItems = items;
                int n = allowed && HasEquipmentUpgrade() ? 1 : 0;
                if (n != m_EquipBadgeShown)
                {
                    m_EquipBadgeShown = n;
                    m_EquipButton.Badge.SetDot(n > 0);
                }
            }
        }

        /// <summary>
        /// Сколько веток можно купить ×1 по доставленному золоту (ТЗ 14: цена сравнивается только с кошельком). Цена следующего
        /// уровня — из котировки ядра, пересчёт только при смене уровня; дальше — сравнение с кошельком без выделений.
        /// </summary>
        int AffordableCount(OutpostSimulation sim)
        {
            var run = sim.Run;
            if (run.Phase == RunPhase.ReadyToContinue) return 0;
            int n = 0;
            for (int b = 0; b < OutpostIds.BranchCount; b++)
            {
                int level = run.Levels[b];
                if (m_NextCostLevel[b] != level)
                {
                    m_NextCostLevel[b] = level;
                    m_NextCost[b] = m_Game.Quote((UpgradeBranch)b, BuyMode.One).NextCost;
                }
                var cost = m_NextCost[b];
                if (cost.HasValue && run.Wallet >= cost.Value) n++;
            }
            return n;
        }

        /// <summary>Красный пин «Экипировки»: есть хотя бы один полученный предмет, который можно улучшить за детали.</summary>
        bool HasEquipmentUpgrade()
        {
            var state = m_Game.State;
            var catalog = m_Game.Configs != null ? m_Game.Configs.Equipment : null;
            if (state == null || state.Profile == null || catalog == null) return false;
            var p = state.Profile;
            var defs = catalog.Defs;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                int q = p.ItemLevel(def.Id);
                if (q < 1 || q >= catalog.MaxItemLevel) continue;
                var cost = EquipmentService.UpgradeCost(p, catalog, def.Id);
                if (cost.HasValue && p.Parts >= cost.Value) return true;
            }
            return false;
        }

        // Состояния полосы над панелью.
        const int StripNone = 0, StripDraining = 1, StripNextMine = 2, StripCampaign = 3, StripRetry = 4, StripOffline = 5;

        void RefreshStrip(OutpostSimulation sim)
        {
            var run = sim.Run;
            var phase = m_Game.Phase;
            int state, percent = 0;
            if (phase == OutpostControllerPhase.OfflineCatchUp)
            {
                state = StripOffline;
                percent = Mathf.Clamp(Mathf.FloorToInt(m_Game.OfflineProgress01 * 100f), 0, 100);
            }
            else if (m_Game.SaveFailed) state = StripRetry;
            else if (run.Phase == RunPhase.ReadyToContinue) state = m_Game.CampaignComplete ? StripCampaign : StripNextMine;
            else if (run.Phase == RunPhase.DrainingCargo) state = StripDraining;
            else state = StripNone;
            bool button = state == StripNextMine || state == StripCampaign || state == StripRetry;
            if (button) m_StripButton.SetInteractable(state != StripNextMine || (m_Game.CanContinue && CommandsAllowed(out _)));
            if (state == m_StripState && percent == m_StripPercent) return;
            m_StripState = state;
            m_StripPercent = percent;
            bool pill = state == StripDraining || state == StripOffline;
            Ui.SetActive(m_StatusPill, pill);
            m_StripButton.SetActive(button);
            if (state == StripDraining) Ui.SetText(m_StatusText, Loc.T("outpost.status.draining"));
            else if (state == StripOffline) Ui.SetText(m_StatusText, Loc.F("outpost.status.offline", percent));
            if (state == StripNextMine)
            {
                m_StripButton.SetLabel(Loc.T("outpost.status.next_mine"));
                m_StripButton.SetStyle(PixelButton.Green);
            }
            else if (state == StripCampaign)
            {
                m_StripButton.SetLabel(Loc.T("outpost.status.campaign"));
                m_StripButton.SetStyle(PixelButton.Gold);
            }
            else if (state == StripRetry)
            {
                m_StripButton.SetLabel(Loc.T("outpost.status.retry_save"));
                m_StripButton.SetStyle(PixelButton.Red);
            }
        }

        void OnStripButton()
        {
            switch (m_StripState)
            {
                case StripNextMine:
                    OnResultsNext();
                    RefreshNow();
                    break;
                case StripCampaign:
                    ShowCampaign();
                    break;
                case StripRetry:
                    m_Game?.RetrySave();
                    RefreshNow();
                    break;
            }
        }

        // ───────────── События ядра ─────────────

        /// <summary>
        /// Событие ядра после применения к виду (IOutpostGame.EventPresented): полёт монет только на сдаче (CargoDeposited),
        /// рейсы для подсказки узкого места, сброс кэшей панелей при покупках и смене экипировки. Не меняет экономику.
        /// </summary>
        void OnGameEvent(OutpostEvent e)
        {
            OnStoreEvent(e);
            OnCardEvent(e);
            OnMarketEvent(e);
            var sim = m_Game != null ? m_Game.Sim : null;
            m_Flow.OnEvent(e, sim);
            switch (e.Type)
            {
                case OutpostEventType.CargoDeposited:
                    if (AutoFlyOnDeposit && m_Game != null) FlyCoins(m_Game.DepotWorldPosition(e.Slot < 0 ? 0 : e.Slot), 1);
                    break;
                case OutpostEventType.SectorOpened:
                    if (sim != null && sim.Map.Gates != null)
                        Toast(Loc.F("outpost.toast.sector_opened", Math.Min(e.Count + 1, sim.Map.Gates.Length + 1), sim.Map.Gates.Length + 1));
                    break;
                case OutpostEventType.WorkerHired:
                case OutpostEventType.TeamUpgradePurchased:
                    InvalidateBase();
                    break;
                case OutpostEventType.EquipmentChanged:
                case OutpostEventType.ItemUpgraded:
                    m_EquipVersion++;
                    InvalidateBase();
                    InvalidateEquipment();
                    break;
                case OutpostEventType.StageCompleted:
                    // Награда уже в профиле: предмет и детали меняют коллекцию и бейдж «Экипировки».
                    m_EquipVersion++;
                    InvalidateEquipment();
                    m_NextRefresh = 0f;
                    break;
                case OutpostEventType.DrainingStarted:
                    m_NextRefresh = 0f;
                    break;
            }
        }
    }
}
