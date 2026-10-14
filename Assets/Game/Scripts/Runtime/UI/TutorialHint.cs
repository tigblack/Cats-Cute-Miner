using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Подсказка обучения (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 6.5), как в эталоне: слева портрет кота-наставника
    /// (рамка ui_portrait_frame, внутри tutor_cat_0 — спокойно, tutor_cat_1 — говорит, tutor_cat_2 — моргает), справа светлый
    /// пузырь ui_bubble с хвостиком к наставнику и тёмным текстом; большая лапка icon_hand_big (×2) «нажимает» на цель,
    /// золотые уголки ui_focus_frame обводят её.
    /// Цель — элемент UI (<see cref="Show"/>), точка поля (<see cref="ShowWorld"/>, через главную камеру) или жест переноса
    /// (<see cref="ShowDrag"/>: лапка нажимает на котика, ведёт его к камню, отпускает и повторяет). Без цели — только портрет
    /// с пузырём на <see cref="DefaultDialogY"/> высоты.
    /// Портрет с пузырём стоят над целью, если она в нижней половине экрана, и под ней (под лапкой), если в верхней;
    /// по возможности между панелями HUD (<see cref="SetReserved"/>), всегда целиком внутри safe area.
    /// Подсказка никогда не ловит лучи UI: тап проходит к цели и полю; <see cref="Dim"/> — только визуальное затемнение.
    /// Положение пересчитывается каждый кадр без выделений памяти; элементы двигаются только при смене положения.
    /// </summary>
    public sealed class TutorialHint : MonoBehaviour
    {
        // Имена целей UI (RegisterTarget). HUD регистрирует свои элементы, TutorialController ссылается на них по имени.
        public const string TargetHire = "hire";
        public const string TargetBlueprints = "blueprints";
        public const string TargetDynamite = "dynamite";
        public const string TargetChests = "chests";
        public const string TargetFreeCat = "free_cat";
        public const string TargetNextMine = "next_mine";
        /// <summary>Первая кнопка контекстной панели («Активировать», «+1», «Открыть»…).</summary>
        public const string TargetContextAction = "context_action";

        // Что показывает указатель: ничего, элемент UI, точку поля или жест переноса между двумя точками поля.
        enum Mode
        {
            None,
            Ui,
            World,
            Drag,
        }

        const float Px = PixelArt.UiPixel;
        /// <summary>Поля пузыря вокруг текста и отступ от краёв safe area.</summary>
        const float BubblePadX = 7 * Px, BubblePadY = 6 * Px, ScreenMargin = 4 * Px;
        const float BubbleMaxWidth = 190 * Px, BubbleMinHeight = 22 * Px;
        /// <summary>Зазоры: рамка фокуса — цель; цель с лапкой — портрет с пузырём; портрет — пузырь.</summary>
        const float FocusPad = 2 * Px, DialogGap = 6 * Px, PortraitGap = 2 * Px;
        /// <summary>Низ пузыря над низом портрета — доля высоты портрета: хвостик смотрит вниз, к наставнику.</summary>
        const float BubbleLift = 0.4f;
        /// <summary>Хвостик — на столько от левого края пузыря (за скруглением угла).</summary>
        const float TailInset = 12 * Px;
        /// <summary>Портрет с пузырём без цели — центр на этой доле высоты safe area (над зоной мержа и нижней панелью).</summary>
        const float DefaultDialogY = 0.62f;
        /// <summary>Целый масштаб лапки icon_hand_big.</summary>
        const int HandScale = 2;
        /// <summary>«Дыхание» рамки фокуса на пиксель арта.</summary>
        const float BreathStep = 0.4f;
        /// <summary>Нажатие: кадры по PressStep — отступ лапки от цели в пикселях её спрайта; 0 — касание с масштабом PressScale.</summary>
        const float PressStep = 0.1f, PressScale = 0.9f;
        const int MaxLift = 2;
        static readonly int[] s_Press = { 2, 2, 2, 2, 2, 1, 0, 0, 1, 2 };
        /// <summary>Перенос, с: зависание над котиком, нажатие, ведение к камню, отпускание, пауза — и сначала.</summary>
        const float DragHover = 0.25f, DragDown = 0.2f, DragSlide = 0.9f, DragUp = 0.2f, DragPause = 0.55f;
        const float DragCycle = DragHover + DragDown + DragSlide + DragUp + DragPause;
        /// <summary>Наставник «говорит» TalkTime после нового текста (кадры 1/0 по TalkStep) и моргает раз в 3–5 с.</summary>
        const float TalkStep = 0.15f, TalkTime = 1.2f, BlinkTime = 0.12f, BlinkMin = 3f, BlinkMax = 5f;
        /// <summary>Затемнение заходит за края safe area (вырез, полоса жестов).</summary>
        const float DimBleed = 1000f;

        static readonly Vector3[] s_Corners = new Vector3[4];

        RectTransform m_Root;
        Canvas m_Canvas;
        CanvasGroup m_Group;
        RectTransform m_Dialog;
        PixelPop m_DialogPop;
        Image m_Frame, m_Mentor;
        readonly Sprite[] m_MentorFrames = new Sprite[3];
        float m_PortraitSize;
        Image m_Bubble;
        Image m_Tail;
        TextMeshProUGUI m_Text;
        Image m_Hand;
        Image m_Focus;
        readonly Image[] m_Dim = new Image[4];
        // Кончик пальца лапки относительно её pivot (центр исходного холста), единицы Canvas; палец смотрит вверх-влево.
        Vector2 m_Fingertip;
        // От кончика пальца до низа лапки (с отступом нажатия) и до её дальнего края по X; шаг отступа нажатия.
        float m_HandBelow, m_HandReach, m_LiftUnit;
        readonly Dictionary<string, RectTransform> m_Targets = new Dictionary<string, RectTransform>();

        string m_ShownText;
        bool m_Visible;
        Mode m_Mode;
        RectTransform m_UiTarget;
        // Блок HUD с целью (прямой ребёнок safe area: нижняя панель, контекстная панель): пузырь не закрывает его содержимое.
        RectTransform m_UiBlock;
        Vector3 m_World, m_DragFrom;
        Vector2 m_WorldCells = Vector2.one;
        bool m_Dimmed;
        float m_ReserveBottom, m_ReserveTop;

        // Показанное состояние: элементы двигаются, только если оно изменилось.
        Rect m_ShownTarget, m_ShownFrom;
        bool m_ShownHasTarget;
        int m_ShownBreath = -1;
        bool m_LayoutDirty;
        float m_BubbleHeight;
        Vector2 m_HandPos = new Vector2(float.NaN, float.NaN);
        Vector3 m_HandScaleShown;
        // Наставник: показанный кадр, начало речи, следующее моргание; начало цикла переноса.
        int m_MentorFrame = -1;
        float m_TalkStart = -100f, m_NextBlink, m_BlinkUntil;
        float m_DragStart;

        /// <summary>
        /// Точка касания лапкой цели-клика в долях её прямоугольника (0,5; 0,5 — центр, как всегда в основной игре). Поле
        /// экземпляра: playable ставит своей подсказке (0,8; 0,3), чтобы лапка не закрывала подпись и цену кнопки.
        /// </summary>
        public Vector2 TipAnchor = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Лапка над целью-кликом пальцем вниз (отражена по вертикали), пузырь — над лапкой. Поле экземпляра, по умолчанию
        /// false — лапка под пальцем, как всегда в основной игре: playable ставит его своей подсказке для кнопок нижней панели,
        /// чтобы лапка висела над полем, а не закрывала соседние кнопки. Жест переноса не меняет.
        /// </summary>
        public bool PointDown;

        /// <summary>Подсказка показана.</summary>
        public bool Visible => m_Visible;
        /// <summary>Текст показанной подсказки (null — скрыта).</summary>
        public string Text => m_Visible ? m_ShownText : null;
        public RectTransform Rect => m_Root;

        /// <summary>
        /// Затемнение вокруг цели (ShadeColor, alpha 0,62) — только визуальное: лучи UI не ловит, тап по полю и HUD проходит
        /// (обучение ТЗ 13.4 не блокирует игру). Включайте для шагов, где важно выделить одну цель (шаг 1 — «Нанять»).
        /// </summary>
        public bool Dim
        {
            get => m_Dimmed;
            set
            {
                if (m_Dimmed == value) return;
                m_Dimmed = value;
                m_LayoutDirty = true;
            }
        }

        /// <summary>
        /// Создать подсказку на весь <paramref name="safeArea"/> (последним ребёнком: поверх панелей HUD). Окна, открытые позже,
        /// перекрывают её; TutorialController прячет подсказку под окнами.
        /// </summary>
        public static TutorialHint Create(RectTransform safeArea)
        {
            var root = Ui.Rect(safeArea, "TutorialHint").Anchor(0, 0, 1, 1);
            var hint = root.gameObject.AddComponent<TutorialHint>();
            hint.Build(root);
            return hint;
        }

        void Build(RectTransform root)
        {
            m_Root = root;
            m_Canvas = root.GetComponentInParent<Canvas>();
            // Подсказка не ловит лучи никогда: ни затемнение, ни пузырь, ни лапка.
            m_Group = root.gameObject.AddComponent<CanvasGroup>();
            m_Group.interactable = false;
            m_Group.blocksRaycasts = false;

            for (int i = 0; i < m_Dim.Length; i++)
            {
                var d = Ui.Rect(root, "dim" + i).gameObject.AddComponent<Image>();
                d.color = PixelUi.ShadeColor;
                d.raycastTarget = false;
                var rt = d.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                d.gameObject.SetActive(false);
                m_Dim[i] = d;
            }

            m_Focus = PixelUi.Panel(root, "focus", PixelPanel.Focus);
            var frt = m_Focus.rectTransform;
            frt.anchorMin = frt.anchorMax = Vector2.zero;
            frt.pivot = new Vector2(0.5f, 0.5f);
            m_Focus.gameObject.SetActive(false);

            BuildDialog(root);

            bool big = PixelArt.Has("icon_hand_big");
            int scale = big ? HandScale : HandScale + 1;
            m_Hand = PixelUi.Icon(root, "hand", big ? "icon_hand_big" : "icon_hand", scale);
            var hrt = m_Hand.rectTransform;
            hrt.anchorMin = hrt.anchorMax = Vector2.zero;
            // Кончик пальца: icon_hand_big — пиксель (2, 2) холста 24×24, icon_hand — (3, 0) холста 16×16.
            m_Fingertip = (big ? new Vector2(2.5f - 12f, 12f - 2.5f) : new Vector2(3.5f - 8f, 8f - 0.5f)) * (scale * Px);
            m_LiftUnit = scale * Px;
            var size = hrt.sizeDelta;
            m_HandBelow = m_Fingertip.y + size.y * hrt.pivot.y + MaxLift * m_LiftUnit;
            m_HandReach = size.x * (1f - hrt.pivot.x) - m_Fingertip.x + MaxLift * m_LiftUnit;
            m_Hand.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
        }

        /// <summary>Портрет наставника и пузырь в общем контейнере: «поп» при новом тексте, двигаются вместе.</summary>
        void BuildDialog(RectTransform root)
        {
            m_Dialog = Ui.Rect(root, "dialog");
            m_Dialog.anchorMin = m_Dialog.anchorMax = Vector2.zero;
            m_Dialog.pivot = new Vector2(0.5f, 0.5f);
            m_DialogPop = m_Dialog.gameObject.AddComponent<PixelPop>();

            // Рамка с тёмным полем, кот поверх — в центре холста рамки. Без спрайтов в атласе остаётся один пузырь.
            if (PixelArt.Has("ui_portrait_frame") && PixelArt.Has("tutor_cat_0"))
            {
                m_Frame = PixelUi.Icon(m_Dialog, "portrait", "ui_portrait_frame");
                var frt = m_Frame.rectTransform;
                frt.anchorMin = frt.anchorMax = Vector2.zero;
                frt.anchoredPosition = Vector2.Scale(frt.sizeDelta, frt.pivot);
                m_PortraitSize = Mathf.Max(frt.sizeDelta.x, frt.sizeDelta.y);
                m_Mentor = PixelUi.Icon(frt, "mentor", "tutor_cat_0");
                var mrt = m_Mentor.rectTransform;
                mrt.anchorMin = mrt.anchorMax = frt.pivot;
                mrt.anchoredPosition = Vector2.zero;
                for (int i = 0; i < m_MentorFrames.Length; i++)
                {
                    string name = "tutor_cat_" + i;
                    m_MentorFrames[i] = PixelArt.Has(name) ? PixelArt.Ui(name) : m_Mentor.sprite;
                }
            }

            m_Bubble = PixelUi.Panel(m_Dialog, "bubble", PixelPanel.Bubble);
            m_Bubble.raycastTarget = false;
            var brt = m_Bubble.rectTransform;
            brt.anchorMin = brt.anchorMax = Vector2.zero;
            brt.pivot = Vector2.zero;
            m_Text = PixelUi.Text(m_Bubble.transform, "text", "", PixelUi.FontBody, PixelUi.TextDark, TextAlignmentOptions.Center);
            m_Text.rectTransform.Anchor(0, 0, 1, 1, new Vector2(BubblePadX, BubblePadY), new Vector2(-BubblePadX, -BubblePadY));
            m_Text.overflowMode = TextOverflowModes.Overflow;
            // Хвостик — у левого нижнего угла пузыря, кончиком (pivot) на 4 пикселя арта ниже кромки, к наставнику; отражён
            // по X, чтобы кончик клонился влево. Верх хвостика перекрывает нижние строки пузыря.
            m_Tail = PixelUi.Icon(m_Bubble.transform, "tail", "ui_bubble_tail");
            var trt = m_Tail.rectTransform;
            trt.anchorMin = trt.anchorMax = Vector2.zero;
            trt.anchoredPosition = new Vector2(TailInset, -4 * Px);
            trt.localScale = new Vector3(-1f, 1f, 1f);
            m_Tail.gameObject.SetActive(m_Frame != null);
        }

        /// <summary>Зарегистрировать элемент UI под именем (константы Target*), чтобы обучение могло указать на него.</summary>
        public void RegisterTarget(string id, RectTransform target)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (target == null) m_Targets.Remove(id);
            else m_Targets[id] = target;
        }

        /// <summary>Зарегистрированный элемент UI или null.</summary>
        public RectTransform FindTarget(string id) =>
            id != null && m_Targets.TryGetValue(id, out var rt) && rt != null ? rt : null;

        /// <summary>
        /// Полосы HUD у нижнего и верхнего края safe area, единицы Canvas: портрет с пузырём по возможности ставятся между
        /// ними и не закрывают панели. HUD задаёт их после создания подсказки.
        /// </summary>
        public void SetReserved(float bottom, float top)
        {
            m_ReserveBottom = Mathf.Max(0f, bottom);
            m_ReserveTop = Mathf.Max(0f, top);
            m_LayoutDirty = true;
        }

        /// <summary>Показать подсказку с указателем на элемент UI (null — без указателя).</summary>
        public void Show(string text, RectTransform uiTarget)
        {
            m_UiTarget = uiTarget;
            m_UiBlock = BlockOf(uiTarget);
            m_Mode = uiTarget != null ? Mode.Ui : Mode.None;
            Open(text);
        }

        /// <summary>Показать подсказку с указателем на точку поля (центр footprint одной клетки).</summary>
        public void ShowWorld(string text, Vector3 worldPos) => ShowWorld(text, worldPos, Vector2.one);

        /// <summary>Показать подсказку с указателем на объект поля: центр и размер footprint в клетках (рамка фокуса по нему).</summary>
        public void ShowWorld(string text, Vector3 worldPos, Vector2 cells)
        {
            m_UiTarget = null;
            m_Mode = Mode.World;
            m_World = worldPos;
            m_WorldCells = cells;
            Open(text);
        }

        /// <summary>Сдвинуть указатель на другую точку поля без перестройки текста (цель движется: котик).</summary>
        public void MoveWorld(Vector3 worldPos)
        {
            m_Mode = Mode.World;
            m_UiTarget = null;
            m_World = worldPos;
        }

        /// <summary>
        /// Показать жест переноса: лапка нажимает на <paramref name="fromWorld"/> (котик), за ~0,9 с ведёт к
        /// <paramref name="toWorld"/> (камень, рамка фокуса — по одной клетке), отпускает, ждёт и повторяет.
        /// </summary>
        public void ShowDrag(string text, Vector3 fromWorld, Vector3 toWorld) => ShowDrag(text, fromWorld, toWorld, Vector2.one);

        /// <summary>Жест переноса к объекту поля: центр и footprint цели <paramref name="toCells"/> в клетках.</summary>
        public void ShowDrag(string text, Vector3 fromWorld, Vector3 toWorld, Vector2 toCells)
        {
            m_UiTarget = null;
            m_Mode = Mode.Drag;
            m_DragFrom = fromWorld;
            m_World = toWorld;
            m_WorldCells = toCells;
            m_DragStart = Time.unscaledTime;
            Open(text);
        }

        /// <summary>Сдвинуть концы жеста переноса без перестройки текста (котик переместился, сменился камень).</summary>
        public void MoveDrag(Vector3 fromWorld, Vector3 toWorld)
        {
            if (m_Mode != Mode.Drag) m_DragStart = Time.unscaledTime;
            m_Mode = Mode.Drag;
            m_UiTarget = null;
            m_DragFrom = fromWorld;
            m_World = toWorld;
        }

        public void Hide()
        {
            if (!m_Visible) return;
            m_Visible = false;
            m_ShownText = null;
            m_UiTarget = null;
            m_Mode = Mode.None;
            m_Root.gameObject.SetActive(false);
        }

        void Open(string text)
        {
            if (text == null) text = "";
            bool textChanged = !string.Equals(text, m_ShownText, System.StringComparison.Ordinal);
            m_ShownText = text;
            if (!m_Visible)
            {
                m_Visible = true;
                m_Root.gameObject.SetActive(true);
                m_Root.SetAsLastSibling();
                textChanged = true;
            }
            if (textChanged)
            {
                Ui.SetText(m_Text, text);
                MeasureBubble();
                m_DialogPop.Play();
                // Новая реплика: наставник «говорит», моргание — после речи.
                float now = Time.unscaledTime;
                m_TalkStart = now;
                m_NextBlink = now + TalkTime + Random.Range(BlinkMin, BlinkMax);
            }
            m_LayoutDirty = true;
            LateUpdate();
        }

        /// <summary>
        /// Пузырь — до 190 пикселей арта (с портретом не шире safe area), высота по тексту, кратно пикселю арта; низ пузыря
        /// на <see cref="BubbleLift"/> высоты портрета. Размер контейнера — по портрету и пузырю.
        /// </summary>
        void MeasureBubble()
        {
            float width = BubbleWidth();
            var size = m_Text.GetPreferredValues(m_ShownText, width - 2 * BubblePadX, 0f);
            m_BubbleHeight = Mathf.Max(BubbleMinHeight, PixelUi.Snap(size.y + 2 * BubblePadY + Px));
            var brt = m_Bubble.rectTransform;
            brt.sizeDelta = new Vector2(width, m_BubbleHeight);
            float left = m_Frame != null ? m_PortraitSize + PortraitGap : 0f;
            float lift = m_Frame != null ? PixelUi.Snap(m_PortraitSize * BubbleLift) : 0f;
            brt.anchoredPosition = new Vector2(left, lift);
            m_Dialog.sizeDelta = new Vector2(left + width, Mathf.Max(m_Frame != null ? m_PortraitSize : 0f, lift + m_BubbleHeight));
        }

        float BubbleWidth()
        {
            float portrait = m_Frame != null ? m_PortraitSize + PortraitGap : 0f;
            float avail = m_Root.rect.width - 2 * ScreenMargin - portrait;
            return Mathf.Max(40 * Px, Mathf.Min(BubbleMaxWidth, Mathf.Floor(avail / Px) * Px));
        }

        void LateUpdate()
        {
            if (!m_Visible) return;
            float now = Time.unscaledTime;
            UpdateMentor(now);
            bool has = TryGetTargets(out Rect target, out Rect from);
            bool moved = has != m_ShownHasTarget ||
                (has && (!Same(target, m_ShownTarget) || (m_Mode == Mode.Drag && !Same(from, m_ShownFrom))));
            if (moved || m_LayoutDirty)
            {
                if (Mathf.Abs(m_Bubble.rectTransform.sizeDelta.x - BubbleWidth()) > 0.5f) MeasureBubble();
                LayoutDialog(has, target, from);
                LayoutDim(has, target);
                m_ShownHasTarget = has;
                m_ShownTarget = target;
                m_ShownFrom = from;
                m_LayoutDirty = false;
                m_ShownBreath = -1;
            }
            LayoutPointer(has, target, from, now);
        }

        static bool Same(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 0.5f && Mathf.Abs(a.yMin - b.yMin) < 0.5f && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;

        /// <summary>
        /// Наставник: после новой реплики TalkTime чередует «говорит»/«спокойно» по TalkStep, затем спокоен и моргает раз
        /// в 3–5 с. Спрайт меняется только при смене кадра.
        /// </summary>
        void UpdateMentor(float now)
        {
            if (m_Mentor == null) return;
            int frame;
            float talk = now - m_TalkStart;
            if (talk >= 0f && talk < TalkTime) frame = ((int)(talk / TalkStep) & 1) == 0 ? 1 : 0;
            else
            {
                if (now >= m_NextBlink)
                {
                    m_BlinkUntil = now + BlinkTime;
                    m_NextBlink = now + Random.Range(BlinkMin, BlinkMax);
                }
                frame = now < m_BlinkUntil ? 2 : 0;
            }
            if (frame == m_MentorFrame) return;
            m_MentorFrame = frame;
            m_Mentor.sprite = m_MentorFrames[frame];
        }

        /// <summary>
        /// Прямоугольники в координатах корня (начало — левый нижний угол safe area): <paramref name="target"/> — цель
        /// (у переноса — куда), <paramref name="from"/> — откуда (только перенос). false — цели нет или она вне экрана.
        /// </summary>
        bool TryGetTargets(out Rect target, out Rect from)
        {
            target = default;
            from = default;
            var uiCam = m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;
            switch (m_Mode)
            {
                case Mode.Ui:
                    return UiRect(m_UiTarget, uiCam, out target);
                case Mode.World:
                    return WorldRect(m_World, m_WorldCells, uiCam, out target);
                case Mode.Drag:
                    return WorldRect(m_DragFrom, Vector2.one, uiCam, out from) && WorldRect(m_World, m_WorldCells, uiCam, out target);
                default:
                    return false;
            }
        }

        /// <summary>Прямой ребёнок safe area, в котором лежит <paramref name="t"/> (блок HUD), иначе null.</summary>
        RectTransform BlockOf(RectTransform t)
        {
            var safe = m_Root != null ? m_Root.parent : null;
            if (t == null || safe == null) return null;
            Transform cur = t;
            while (cur.parent != null && cur.parent != safe) cur = cur.parent;
            return cur.parent == safe ? cur as RectTransform : null;
        }

        bool UiRect(RectTransform rt, Camera uiCam, out Rect rect)
        {
            rect = default;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            rt.GetWorldCorners(s_Corners);
            if (!ToLocal(RectTransformUtility.WorldToScreenPoint(uiCam, s_Corners[0]), uiCam, out var min)) return false;
            if (!ToLocal(RectTransformUtility.WorldToScreenPoint(uiCam, s_Corners[2]), uiCam, out var max)) return false;
            return Inside(min, max, out rect);
        }

        bool WorldRect(Vector3 world, Vector2 cells, Camera uiCam, out Rect rect)
        {
            rect = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var c = cam.WorldToScreenPoint(world);
            if (c.z < 0f) return false;
            // Камера смотрит строго вниз: мировая X — вправо по экрану, Z — вверх.
            var half = new Vector3(cells.x * 0.5f, 0f, cells.y * 0.5f);
            var lo = cam.WorldToScreenPoint(world - half);
            var hi = cam.WorldToScreenPoint(world + half);
            if (!ToLocal(new Vector2(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y)), uiCam, out var min)) return false;
            if (!ToLocal(new Vector2(Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y)), uiCam, out var max)) return false;
            return Inside(min, max, out rect);
        }

        /// <summary>Прямоугольник по углам; центр за пределами safe area — указатель не показывается.</summary>
        bool Inside(Vector2 min, Vector2 max, out Rect rect)
        {
            rect = UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var bounds = m_Root.rect;
            float cx = rect.center.x, cy = rect.center.y;
            return cx >= 0f && cx <= bounds.width && cy >= 0f && cy <= bounds.height;
        }

        /// <summary>Экранная точка → координаты от левого нижнего угла корня.</summary>
        bool ToLocal(Vector2 screen, Camera uiCam, out Vector2 local)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, screen, uiCam, out local)) return false;
            var r = m_Root.rect;
            local -= r.min;
            return true;
        }

        /// <summary>
        /// Портрет с пузырём: над целью, если её центр в нижней половине экрана, иначе под ней вместе с лапкой. Сначала
        /// между панелями HUD (<see cref="SetReserved"/>), затем в пределах всей safe area; по горизонтали — по центру.
        /// </summary>
        void LayoutDialog(bool has, Rect target, Rect from)
        {
            var bounds = m_Root.rect;
            var size = m_Dialog.sizeDelta;
            float lo = ScreenMargin, hi = bounds.height - ScreenMargin;
            float bottom;
            if (!has) bottom = bounds.height * DefaultDialogY - size.y * 0.5f;
            else
            {
                // Полоса, которую занимают цель и лапка (лапка висит под кончиком пальца).
                float occHi = target.yMax + FocusPad, occLo, centerY;
                if (m_Mode == Mode.Drag)
                {
                    occHi = Mathf.Max(occHi, from.yMax + FocusPad);
                    occLo = Mathf.Min(Mathf.Min(from.yMin, target.yMin) - FocusPad, Mathf.Min(from.center.y, target.center.y) - m_HandBelow);
                    centerY = (Mathf.Min(from.yMin, target.yMin) + Mathf.Max(from.yMax, target.yMax)) * 0.5f;
                }
                else
                {
                    var tip = ClickTip(target);
                    // Лапка пальцем вниз висит над целью: занятая полоса идёт вверх до её верха, под целью свободно.
                    if (PointDown)
                    {
                        occHi = Mathf.Max(occHi, tip.y + m_HandBelow);
                        occLo = target.yMin - FocusPad;
                    }
                    else occLo = Mathf.Min(target.yMin - FocusPad, tip.y - m_HandBelow);
                    centerY = target.center.y;
                    // Цель внутри блока HUD (кнопка «+1» контекстной панели): наставник с пузырём — над всем блоком,
                    // иначе пузырь закроет заголовок и строки панели, о которых идёт речь.
                    if (m_Mode == Mode.Ui && m_UiBlock != null && m_UiBlock != m_UiTarget)
                    {
                        var uiCam = m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;
                        if (UiRect(m_UiBlock, uiCam, out var block)) occHi = Mathf.Max(occHi, block.yMax);
                    }
                }
                float fieldLo = Mathf.Max(lo, m_ReserveBottom + ScreenMargin);
                float fieldHi = Mathf.Min(hi, bounds.height - m_ReserveTop - ScreenMargin);
                bottom = PlaceDialog(centerY < bounds.height * 0.5f, occHi + DialogGap, occLo - DialogGap, size.y, fieldLo, fieldHi, lo, hi);
            }
            bottom = Mathf.Clamp(bottom, lo, Mathf.Max(lo, hi - size.y));
            float left = Mathf.Max(ScreenMargin, (bounds.width - size.x) * 0.5f);
            // Угол контейнера — на сетке пикселей арта: портрет и пузырь привязаны к нему.
            m_Dialog.anchoredPosition = PixelUi.Snap(new Vector2(left, bottom)) + size * 0.5f;
        }

        /// <summary>
        /// Низ контейнера высотой <paramref name="h"/>: над занятой полосой (не ниже <paramref name="above"/>) или под ней
        /// (верх не выше <paramref name="belowTop"/>). Порядок: своя сторона между панелями, другая сторона между панелями,
        /// затем то же в пределах safe area; если нигде не помещается — своя сторона (дальше ограничит Clamp).
        /// </summary>
        static float PlaceDialog(bool preferAbove, float above, float belowTop, float h, float fieldLo, float fieldHi, float lo, float hi)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float minY = pass == 0 ? fieldLo : lo, maxY = pass == 0 ? fieldHi : hi;
                for (int side = 0; side < 2; side++)
                {
                    bool up = side == 0 ? preferAbove : !preferAbove;
                    if (up)
                    {
                        float y = Mathf.Max(above, minY);
                        if (y + h <= maxY) return y;
                    }
                    else
                    {
                        float top = Mathf.Min(belowTop, maxY);
                        if (top - h >= minY) return top - h;
                    }
                }
            }
            return preferAbove ? above : belowTop - h;
        }

        /// <summary>Затемнение — четыре прямоугольника вокруг цели (середина открыта), только при цели на экране.</summary>
        void LayoutDim(bool has, Rect target)
        {
            bool on = m_Dimmed && has;
            for (int i = 0; i < m_Dim.Length; i++) Ui.SetActive(m_Dim[i], on);
            if (!on) return;
            var b = m_Root.rect;
            var hole = UnityEngine.Rect.MinMaxRect(target.xMin - FocusPad, target.yMin - FocusPad, target.xMax + FocusPad, target.yMax + FocusPad);
            float x0 = -DimBleed, y0 = -DimBleed, x1 = b.width + DimBleed, y1 = b.height + DimBleed;
            // Низ, верх, лево, право вокруг «окна» цели.
            SetDim(0, x0, y0, x1 - x0, Mathf.Max(0f, hole.yMin - y0));
            SetDim(1, x0, hole.yMax, x1 - x0, Mathf.Max(0f, y1 - hole.yMax));
            SetDim(2, x0, hole.yMin, Mathf.Max(0f, hole.xMin - x0), hole.height);
            SetDim(3, hole.xMax, hole.yMin, Mathf.Max(0f, x1 - hole.xMax), hole.height);
        }

        void SetDim(int i, float x, float y, float w, float h)
        {
            var rt = m_Dim[i].rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>
        /// Точка касания цели-клика — её центр (<see cref="TipAnchor"/>); у нижнего края экрана выше (но внутри цели), чтобы
        /// лапка, висящая под пальцем, не уходила за safe area (кнопка «Нанять»). Лапка пальцем вниз (<see cref="PointDown"/>)
        /// висит над целью — ей подъём не нужен.
        /// </summary>
        Vector2 ClickTip(Rect target)
        {
            bool centre = TipAnchor.x == 0.5f && TipAnchor.y == 0.5f;
            float x = centre ? target.center.x : target.xMin + target.width * TipAnchor.x;
            float y = centre ? target.center.y : target.yMin + target.height * TipAnchor.y, need = m_HandBelow + ScreenMargin;
            if (!PointDown && y < need) y = Mathf.Max(y, Mathf.Min(target.yMax - 4 * Px, need));
            return new Vector2(x, y);
        }

        /// <summary>
        /// Рамка фокуса вокруг цели «дышит» на пиксель арта. Лапка касается цели кончиком пальца: у клика — цикл нажатия
        /// (отступ 2–1–0 пикселей спрайта по диагонали, касание с масштабом 0,9), у переноса — нажатие на котике, ведение
        /// к камню, отпускание и пауза. Лапка отражается по X, если иначе уходит за правый край.
        /// </summary>
        void LayoutPointer(bool has, Rect target, Rect from, float now)
        {
            Ui.SetActive(m_Focus, has);
            Ui.SetActive(m_Hand, has);
            if (!has) return;
            int breath = (int)(now / BreathStep) & 1;
            if (breath != m_ShownBreath)
            {
                m_ShownBreath = breath;
                float grow = 2 * (FocusPad + breath * Px);
                var frt = m_Focus.rectTransform;
                frt.anchoredPosition = PixelUi.Snap(target.center);
                frt.sizeDelta = PixelUi.Snap(new Vector2(Mathf.Max(target.width + grow, 12 * Px), Mathf.Max(target.height + grow, 12 * Px)));
            }

            float right = m_Root.rect.width - ScreenMargin;
            Vector2 tip;
            int lift;
            bool pressed = false, flip;
            if (m_Mode == Mode.Drag)
            {
                Vector2 a = from.center, b = target.center;
                flip = Mathf.Max(a.x, b.x) + m_HandReach > right;
                float t = Mathf.Repeat(now - m_DragStart, DragCycle);
                if (t < DragHover)
                {
                    tip = a;
                    lift = MaxLift;
                }
                else if ((t -= DragHover) < DragDown)
                {
                    tip = a;
                    lift = 0;
                    pressed = true;
                }
                else if ((t -= DragDown) < DragSlide)
                {
                    float u = t / DragSlide;
                    tip = Vector2.Lerp(a, b, u * u * (3f - 2f * u));
                    lift = 0;
                }
                else if (t - DragSlide < DragUp)
                {
                    tip = b;
                    lift = 1;
                }
                else
                {
                    tip = b;
                    lift = MaxLift;
                }
            }
            else
            {
                tip = ClickTip(target);
                flip = tip.x + m_HandReach > right;
                lift = s_Press[(int)(now / PressStep) % s_Press.Length];
                pressed = lift == 0;
            }
            PlaceHand(tip, lift, pressed, flip, PointDown && m_Mode != Mode.Drag);
        }

        /// <summary>
        /// Лапка кончиком пальца в <paramref name="tip"/>, отступ по диагонали от цели; «касание» — масштаб 0,9 вокруг кончика.
        /// <paramref name="down"/> — лапка отражена по вертикали и висит над целью пальцем вниз.
        /// </summary>
        void PlaceHand(Vector2 tip, int lift, bool pressed, bool flip, bool down)
        {
            float sx = flip ? -1f : 1f, sy = down ? -1f : 1f, k = pressed ? PressScale : 1f, off = lift * m_LiftUnit;
            var pos = PixelUi.Snap(new Vector2(tip.x - m_Fingertip.x * sx * k + off * sx, tip.y - (m_Fingertip.y * k + off) * sy));
            var scale = new Vector3(sx * k, sy * k, 1f);
            var hand = m_Hand.rectTransform;
            if (pos != m_HandPos)
            {
                m_HandPos = pos;
                hand.anchoredPosition = pos;
            }
            if (scale != m_HandScaleShown)
            {
                m_HandScaleShown = scale;
                hand.localScale = scale;
            }
        }

        void OnRectTransformDimensionsChange()
        {
            if (m_Visible) m_LayoutDirty = true;
        }
    }
}
