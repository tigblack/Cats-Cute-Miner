using System;
using System.Collections.Generic;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Экранная часть заводов на поле (ТЗ 2.2 §4.3, §8), собранная из PixelUi в safe area HUD под окнами и тостом:
    /// <list type="bullet">
    /// <item>плавающая кнопка «Deliver ×N» с иконкой товара над каждым видимым заводом в Ready (ручной режим) — кнопка UI,
    /// поэтому касание получает раньше поля (ввод поля не начинает жест над UI); нажатие — <see cref="IOutpostGame.DeliverFactory"/>,
    /// отказ — тост с причиной. Кнопка стоит над полосой своего завода; в тесноте соседних готовых заводов она сокращается до
    /// иконки и «×N» и сдвигается вбок в пределах здания, но не поднимается (см. <see cref="LayoutButtons"/>);</item>
    /// <item>стрелки у краёв полосы поля к готовым заводам за кадром (по одной на сторону, с иконкой товара ближайшего и счётчиком,
    /// если их несколько) не ложатся на кнопки и заводы на экране; тап подводит камеру к заводу;</item>
    /// <item>тосты «&lt;Factory&gt; unlocked!» и «&lt;Factory&gt; now delivers automatically» по событиям ядра.</item>
    /// </list>
    /// Пока открыто окно, идёт переход или загрузка — всё скрыто. Пересчёт каждый кадр после вида поля, без выделений памяти
    /// (подписи — из кэша FactoryText и меняются только при смене числа, товара или формы кнопки).
    /// </summary>
    /// <remarks>
    /// Порядок 100 — после CameraRig (0): камера двигается в его LateUpdate (инерция, плавный перевод, импульс), и раскладка по
    /// прежнему положению камеры отставала бы от заводов на кадр при прокрутке.
    /// </remarks>
    [DefaultExecutionOrder(100)]
    public sealed class FactoryOverlay : MonoBehaviour
    {
        const float Px = PixelUi.Px;
        /// <summary>Заводов в шахте не больше 15 (шахты 130 и 140).</summary>
        public const int MaxButtons = 15;
        const float ButtonHeight = 22 * Px;
        const float IconPad = 5 * Px, TextPad = 6 * Px, IconGap = 3 * Px;
        /// <summary>Поля короткой кнопки «иконка ×N»: уже полной, чтобы и трёхзначное N помещалось над зданием в два столбца на 9:21.</summary>
        const float ShortIconPad = 4 * Px, ShortTextPad = 5 * Px, ShortIconGap = 2 * Px;
        /// <summary>Отступ кнопки от боковых краёв экрана.</summary>
        const float SidePad = 2 * Px;
        const float ChipSize = 24 * Px, EdgeMargin = 3 * Px, BobPeriod = 0.3f;
        /// <summary>Зазор между кнопками «Deliver» соседних заводов, между кнопкой или заводом и стрелкой у края.</summary>
        const float StackGap = 2 * Px;
        /// <summary>Знак стрелки выступает из ячейки наружу на столько (см. <see cref="PlaceArrow"/>).</summary>
        const float SignReach = 14 * Px;
        /// <summary>Стороны стрелок: вверх, вниз, влево, вправо.</summary>
        const int Sides = 4;

        IOutpostGame m_Game;
        OutpostController m_Controller;
        OutpostHud m_Hud;
        RectTransform m_Rect;
        Canvas m_Canvas;
        /// <summary>Корневой Canvas HUD: его rect — весь экран холста в единицах (см. <see cref="ToLocal"/>).</summary>
        RectTransform m_Root;

        sealed class DeliverButton
        {
            public Ui.ButtonRefs Refs;
            public Image Icon;
            public int TargetId = -1;
            public int Units = -1;
            public bool Activation;
            public string Good;
            // Подписи обеих форм (кэш FactoryText) и ширины — пересчитываются при смене завода, числа или товара.
            public string FullText, ShortText;
            public float IconW, FullW, ShortW;
            /// <summary>Показанная форма: 0 — полная «Deliver ×N», 1 — короткая «×N», −1 — подписи сменились.</summary>
            public int Form = -1;
            // Раскладка кадра (единицы оверлея от левого нижнего угла): середина верха полосы завода, полуширина здания, низ
            // кнопки, допустимый сдвиг середины, итоговая середина, форма и признак «поместилась».
            public float Ax, HalfBuilding, Y, Lo, Hi, X;
            public bool Short, Placed;
            public float Width => Short ? ShortW : FullW;
        }

        sealed class Arrow
        {
            public RectTransform Rect;
            public Image Icon, Sign;
            public PixelBadge Badge;
            public int TargetId = -1;
            public string IconName, SignName;
            public float Distance;
            public int Count;
            public Vector2 At;
        }

        readonly DeliverButton[] m_Buttons = new DeliverButton[MaxButtons];
        readonly Arrow[] m_Arrows = new Arrow[Sides];
        /// <summary>Слотов кнопок, занятых готовыми заводами на экране в этом кадре (поместившихся и нет).</summary>
        int m_ButtonsShown;
        int m_Visible, m_Short, m_Dropped;
        // Раскладка кадра без выделений: порядок кнопок слева направо, заводы на экране (препятствия стрелок), запретные
        // отрезки положения стрелки.
        readonly int[] m_Order = new int[MaxButtons];
        readonly Rect[] m_Bodies = new Rect[MaxButtons];
        readonly int[] m_BodyIds = new int[MaxButtons];
        int m_BodyCount;
        readonly float[] m_ForbidLo = new float[2 * MaxButtons], m_ForbidHi = new float[2 * MaxButtons];
        readonly bool[] m_ArrowBlocked = new bool[Sides];
        // Ширина подписи по тексту (подписи — из кэша FactoryText, шрифт и размер у всех кнопок общие).
        readonly Dictionary<string, float> m_TextWidths = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Кнопок «Deliver» на экране сейчас.</summary>
        public int VisibleButtons => m_Visible;
        /// <summary>Из них короткие «иконка ×N» (тесты, снимки).</summary>
        public int ShortButtons => m_Short;
        /// <summary>Готовых заводов на экране, чьей кнопке не нашлось места без наложения (тесты, снимки; в шахтах кампании — 0).</summary>
        public int DroppedButtons => m_Dropped;

        public static FactoryOverlay Create(OutpostController game)
        {
            var hud = game != null ? game.Hud : null;
            if (hud == null) return null;
            var safe = hud.transform.Find("SafeArea") as RectTransform;
            if (safe == null) return null;
            var rt = Ui.Rect(safe, "FactoryOverlay").Anchor(0, 0, 1, 1);
            // Под тостом и окнами HUD: окна — последние дети safe area, тост — над кнопками заводов.
            var toast = safe.Find("ToastBg");
            if (toast != null) rt.SetSiblingIndex(toast.GetSiblingIndex());
            var overlay = rt.gameObject.AddComponent<FactoryOverlay>();
            overlay.Init(game, hud, rt);
            return overlay;
        }

        void Init(OutpostController game, OutpostHud hud, RectTransform rt)
        {
            m_Game = game;
            m_Controller = game;
            m_Hud = hud;
            m_Rect = rt;
            m_Canvas = hud.GetComponent<Canvas>();
            m_Root = m_Canvas != null ? m_Canvas.rootCanvas.transform as RectTransform : null;
            for (int i = 0; i < MaxButtons; i++) m_Buttons[i] = BuildButton(i);
            for (int s = 0; s < Sides; s++) m_Arrows[s] = BuildArrow(s);
            game.EventPresented += OnGameEvent;
        }

        void OnDestroy()
        {
            if (m_Game != null) m_Game.EventPresented -= OnGameEvent;
        }

        DeliverButton BuildButton(int index)
        {
            var b = new DeliverButton();
            int slot = index;
            b.Refs = PixelUi.Button(m_Rect, "deliver" + index, Loc.T("factories.deliver_short"), PixelButton.Green, () => OnDeliver(slot), PixelUi.FontBody);
            var rt = b.Refs.Rect;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(80 * Px, ButtonHeight);
            b.Icon = PixelUi.Icon(b.Refs.Content, "goods", "goods_milk");
            b.Icon.rectTransform.anchorMin = b.Icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            var label = b.Refs.Label;
            label.enableAutoSizing = false;
            label.fontSize = PixelUi.FontBody;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Left;
            rt.gameObject.SetActive(false);
            return b;
        }

        Arrow BuildArrow(int side)
        {
            var a = new Arrow();
            a.Rect = Ui.Rect(m_Rect, "factoryArrow" + side);
            a.Rect.anchorMin = a.Rect.anchorMax = Vector2.zero;
            a.Rect.pivot = new Vector2(0.5f, 0.5f);
            a.Rect.sizeDelta = new Vector2(ChipSize, ChipSize);
            var chip = PixelUi.Panel(a.Rect, "chip", PixelPanel.Slot);
            chip.rectTransform.Anchor(0, 0, 1, 1);
            chip.raycastTarget = true;
            var button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = chip;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(UiSound.PlayClick);
            int s = side;
            button.onClick.AddListener(() => OnArrow(s));
            a.Icon = PixelUi.Icon(chip.transform, "icon", "goods_milk");
            a.Sign = PixelUi.Icon(a.Rect, "arrow", "icon_arrow_up");
            a.Badge = PixelUi.Badge(a.Rect, "count");
            a.Badge.PlaceAtCorner(new Vector2(1f, 1f), PixelUi.BadgeCornerOffset);
            a.Rect.gameObject.AddComponent<PixelPop>();
            a.Rect.gameObject.SetActive(false);
            return a;
        }

        // ───────────── Действия ─────────────

        void OnDeliver(int slot)
        {
            var b = m_Buttons[slot];
            if (b.TargetId < 0 || m_Game == null) return;
            var r = b.Activation ? m_Game.ActivateFactory(b.TargetId) : m_Game.DeliverFactory(b.TargetId);
            if (!r.Ok) m_Hud?.Toast(FactoryText.Reason(r.Reason));
            else Hide(b);
        }

        void OnArrow(int side)
        {
            var a = m_Arrows[side];
            if (a.TargetId < 0 || m_Controller == null || m_Controller.Rig == null) return;
            if (!m_Game.TryGetFactoryWorldPosition(a.TargetId, out var world)) return;
            m_Controller.Rig.FocusOnSmooth(new Vector2(world.x, world.z));
        }

        /// <summary>Нажать кнопку «Deliver» завода (тесты); false — кнопки нет на экране.</summary>
        public bool ClickDeliver(int targetId)
        {
            for (int i = 0; i < MaxButtons; i++)
            {
                var b = m_Buttons[i];
                if (b.TargetId != targetId || !b.Refs.Rect.gameObject.activeInHierarchy) continue;
                b.Refs.Button.onClick.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>Кнопка «Deliver» завода на экране (тесты); null — нет.</summary>
        public Button DeliverButtonOf(int targetId)
        {
            for (int i = 0; i < MaxButtons; i++)
                if (m_Buttons[i].TargetId == targetId && m_Buttons[i].Refs.Rect.gameObject.activeInHierarchy) return m_Buttons[i].Refs.Button;
            return null;
        }

        /// <summary>Подпись кнопки «Deliver» завода (тесты); null — кнопки нет.</summary>
        public string DeliverLabelOf(int targetId)
        {
            var btn = DeliverButtonOf(targetId);
            return btn != null ? btn.GetComponentInChildren<TMP_Text>().text : null;
        }

        /// <summary>Кнопка завода на экране в короткой форме «иконка ×N» (тесты, снимки).</summary>
        public bool DeliverShortOf(int targetId)
        {
            for (int i = 0; i < MaxButtons; i++)
                if (m_Buttons[i].TargetId == targetId && m_Buttons[i].Refs.Rect.gameObject.activeInHierarchy) return m_Buttons[i].Form == 1;
            return false;
        }

        /// <summary>Стрелке стороны <paramref name="side"/> не нашлось места мимо кнопок и заводов — стоит против завода (тесты).</summary>
        public bool ArrowBlocked(int side) => side >= 0 && side < Sides && m_ArrowBlocked[side];

        /// <summary>Стрелка у края к заводу <paramref name="targetId"/> на экране (тесты): сторона 0 — вверх, 1 — вниз, 2 — влево, 3 — вправо; −1 — нет.</summary>
        public int ArrowSideOf(int targetId)
        {
            for (int s = 0; s < Sides; s++)
                if (m_Arrows[s].Rect.gameObject.activeSelf && m_Arrows[s].TargetId == targetId) return s;
            return -1;
        }

        /// <summary>Нажать стрелку стороны <paramref name="side"/> (тесты).</summary>
        public void ClickArrow(int side) => OnArrow(side);

        void OnGameEvent(OutpostEvent e)
        {
            if (m_Hud == null) return;
            switch (e.Type)
            {
                case OutpostEventType.FactoryUnlocked:
                    m_Hud.Toast(Loc.F("factories.toast.unlocked", FactoryText.FactoryName(e.ItemId)));
                    break;
                case OutpostEventType.FactoryAutomated:
                    m_Hud.Toast(Loc.F("factories.toast.automated", FactoryText.FactoryName(e.ItemId)));
                    break;
            }
        }

        // ───────────── Кадр ─────────────

        void LateUpdate() => Refresh();

        /// <summary>Пересчитать кнопки и стрелки по ядру и камере (каждый кадр после вида поля; без выделений памяти).</summary>
        public void Refresh()
        {
            var board = m_Controller != null ? m_Controller.Board : null;
            var sim = m_Game?.Sim;
            bool active = sim != null && board != null && board.FactoryViews.Count > 0 && m_Game.Phase == OutpostControllerPhase.Playing &&
                          !m_Game.InTransition && m_Hud != null && !m_Hud.AnyWindowOpen && !m_Hud.InTransition && board.Matches(sim) &&
                          m_Controller.Rig != null && m_Controller.Rig.Cam != null;
            if (!active)
            {
                HideAll();
                return;
            }
            var cam = m_Controller.Rig.Cam;
            var run = sim.Run;
            var uiCam = m_Canvas != null && m_Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? m_Canvas.worldCamera : null;
            float camHeight = Mathf.Max(1f, cam.pixelHeight);
            float canvasHeight = uiCam != null ? uiCam.pixelHeight : Screen.height;
            float k = canvasHeight > 0f ? canvasHeight / camHeight : 1f;
            var rect = m_Rect.rect;
            float bandBottom = rect.height * m_Hud.BottomInset, bandTop = rect.height * (1f - m_Hud.TopInset);
            // Открытые сведения объекта закрывают низ поля: кнопки и нижние стрелки — над их панелью, не поверх «Deliver» в ней.
            if (m_Hud.TryGetInfoTop(out float infoTop) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Rect, new Vector2(0f, infoTop), uiCam, out var infoLocal))
                bandBottom = Mathf.Max(bandBottom, infoLocal.y - rect.min.y);
            float width = rect.width;
            for (int s = 0; s < Sides; s++)
            {
                m_Arrows[s].Count = 0;
                m_Arrows[s].Distance = float.MaxValue;
            }

            int shown = 0;
            m_BodyCount = 0;
            var views = board.FactoryViews;
            for (int i = 0; i < views.Count; i++)
            {
                var fv = views[i];
                // Завод на экране: от ног footprint до верха полосы цикла. Середина верхнего края — опора кнопки и стрелки.
                if (!board.TryGetFactoryWorldBounds(fv.TargetId, out var lowWorld, out var highWorld)) continue;
                var low = ToLocal(cam.WorldToScreenPoint(lowWorld), k, uiCam);
                var high = ToLocal(cam.WorldToScreenPoint(highWorld), k, uiCam);
                var body = Rect.MinMaxRect(Mathf.Min(low.x, high.x), Mathf.Min(low.y, high.y), Mathf.Max(low.x, high.x), Mathf.Max(low.y, high.y));
                if (m_BodyCount < m_Bodies.Length && body.xMax > 0f && body.xMin < width && body.yMax > bandBottom && body.yMin < bandTop)
                {
                    m_Bodies[m_BodyCount] = body;
                    m_BodyIds[m_BodyCount++] = fv.TargetId;
                }
                var f = FactoryQueries.Find(run, fv.TargetId);
                if (f == null || (f.Status != FactoryStatus.PendingActivation &&
                    (f.Status != FactoryStatus.Ready || f.Automated || f.ReadyUnits <= 0 || run.Phase == RunPhase.ReadyToContinue))) continue;
                var at = new Vector2(body.center.x, body.yMax);
                int vertical = at.y > bandTop ? 1 : (at.y < bandBottom ? -1 : 0);
                int horizontal = at.x > width ? 1 : (at.x < 0f ? -1 : 0);
                if (vertical == 0 && horizontal == 0)
                {
                    if (shown < MaxButtons)
                    {
                        PrepareButton(m_Buttons[shown], fv.TargetId, f, at, body.width * 0.5f, bandTop);
                        shown++;
                    }
                    continue;
                }
                int side = vertical > 0 ? 0 : (vertical < 0 ? 1 : (horizontal < 0 ? 2 : 3));
                var a = m_Arrows[side];
                float dist = Mathf.Abs(at.y - (bandBottom + bandTop) * 0.5f) + Mathf.Abs(at.x - width * 0.5f);
                a.Count++;
                if (dist >= a.Distance) continue;
                a.Distance = dist;
                a.TargetId = fv.TargetId;
                a.At = at;
                SetArrowIcon(a, FactoryText.GoodIcon(f.GoodId));
            }
            for (int i = shown; i < m_ButtonsShown; i++) Hide(m_Buttons[i]);
            m_ButtonsShown = shown;
            LayoutButtons(shown, width);
            for (int s = 0; s < Sides; s++) PlaceArrow(m_Arrows[s], s, bandBottom, bandTop, width);
        }

        /// <summary>
        /// Точка экрана камеры поля → единицы оверлея от его левого нижнего угла. Пиксель холста переводится через прямоугольник
        /// корневого Canvas (rect — весь экран холста в единицах) и относительное положение оверлея в иерархии, а не лучом на
        /// плоскость холста: в ScreenSpaceCamera (портретные снимки и записи роликов) холст встаёт перед камерой только при
        /// отрисовке, и в кадре сдвига камеры ScreenPointToLocalPointInRectangle попадал в прежнюю плоскость — кнопки и стрелки
        /// отставали от заводов на кадр (обход камеры в DeliverButtons_TopEdgeAndCameraSweep_StayOverOwnFactory: кнопка «выше
        /// ряда», стрелка на заводе). В Overlay результат тот же. Без выделений.
        /// </summary>
        Vector2 ToLocal(Vector3 screen, float k, Camera uiCam)
        {
            if (screen.z < 0f) screen = new Vector3(-screen.x, -screen.y, -screen.z);
            var p = new Vector2(screen.x, screen.y) * k;
            if (m_Root == null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Rect, p, uiCam, out var ray);
                return ray - m_Rect.rect.min;
            }
            var pixels = m_Canvas.rootCanvas.pixelRect;
            var units = m_Root.rect;
            if (pixels.width <= 0f || pixels.height <= 0f) return Vector2.zero;
            var inRoot = new Vector2(units.xMin + (p.x - pixels.xMin) * (units.width / pixels.width),
                units.yMin + (p.y - pixels.yMin) * (units.height / pixels.height));
            Vector2 local = m_Rect.InverseTransformPoint(m_Root.TransformPoint(inRoot));
            return local - m_Rect.rect.min;
        }

        /// <summary>Подписи и ширины обеих форм (при смене завода, числа или товара) и опора кнопки в этом кадре.</summary>
        void PrepareButton(DeliverButton b, int targetId, FactoryState f, Vector2 at, float halfBuilding, float bandTop)
        {
            bool activation = f.Status == FactoryStatus.PendingActivation;
            bool changed = b.TargetId != targetId || b.Units != f.ReadyUnits || b.Activation != activation || !string.Equals(b.Good, f.GoodId, StringComparison.Ordinal);
            b.TargetId = targetId;
            if (changed)
            {
                b.Units = f.ReadyUnits;
                b.Activation = activation;
                b.Good = f.GoodId;
                string icon = activation ? "icon_chest" : FactoryText.GoodIcon(f.GoodId);
                PixelUi.SetIcon(b.Icon, icon);
                b.IconW = PixelUi.IconSize(icon).x;
                b.FullText = activation ? Loc.T("factories.activate") : FactoryText.DeliverLabel(f.ReadyUnits);
                b.ShortText = activation ? b.FullText : FactoryText.DeliverCount(f.ReadyUnits);
                b.FullW = PixelUi.Snap(IconPad + b.IconW + IconGap + TextWidth(b.Refs.Label, b.FullText) + TextPad);
                b.ShortW = PixelUi.Snap(ShortIconPad + b.IconW + ShortIconGap + TextWidth(b.Refs.Label, b.ShortText) + ShortTextPad);
                b.Form = -1;
            }
            // Над полосой завода; у верхнего края полосы поля — ниже, на своём здании (в полосе поля целиком).
            b.Ax = at.x;
            b.HalfBuilding = halfBuilding;
            b.Y = Mathf.Min(at.y + Px, bandTop - ButtonHeight);
        }

        float TextWidth(TMP_Text label, string text)
        {
            if (m_TextWidths.TryGetValue(text, out float w)) return w;
            w = Mathf.Ceil(label.GetPreferredValues(text).x / Px) * Px;
            m_TextWidths[text] = w;
            return w;
        }

        /// <summary>Кнопки двух заводов в одной строке экрана: по высоте они перекрылись бы (без зазора в пиксель).</summary>
        static bool SameRow(DeliverButton a, DeliverButton b) => Mathf.Abs(a.Y - b.Y) < ButtonHeight + Px;

        /// <summary>
        /// Кнопки «Deliver» соседних готовых заводов не закрывают друг друга и не уходят от своего здания (решение владельца
        /// 15.10.2026 «сокращать в тесноте»; раньше они поднимались «ёлочкой» на 2–3 ряда над зданием и у верхнего края полосы поля
        /// прижимались поверх соседней). Здание завода — два столбца, а полная «Deliver ×24» шире:
        /// <list type="number">
        /// <item>обычно — полная кнопка над полосой завода; если её прямоугольник лёг бы на полную кнопку другого готового завода,
        /// обе сокращаются до иконки и «×N» (на своём месте — не выше одного ряда клеток над зданием);</item>
        /// <item>кнопки, которые и так задевают соседей (завод срезан краем экрана, трёхзначное N), раздвигаются по горизонтали:
        /// середина — в пределах ширины своего здания, кнопка — на экране; проходы слева направо и обратно (порядок — по середине
        /// завода), сдвиг — наименьший;</item>
        /// <item>кнопка, которой и так не нашлось места, не показывается (завод в Ready доставляет и тап по нему). На шахтах
        /// кампании такого нет: короткая кнопка уже здания в два столбца — проверка <see cref="DroppedButtons"/> в тестах.</item>
        /// </list>
        /// Вверх кнопка не сдвигается. Без выделений: массивы и подписи — заранее.
        /// </summary>
        void LayoutButtons(int n, float width)
        {
            // 1. Форма: полная, пока её прямоугольник не ложится на полную кнопку другого готового завода в той же строке.
            for (int i = 0; i < n; i++)
            {
                var b = m_Buttons[i];
                b.Short = false;
                float xi = ClampToScreen(b.Ax, b.FullW, width);
                for (int j = 0; j < n && !b.Short; j++)
                {
                    var o = m_Buttons[j];
                    if (j != i && SameRow(b, o) && Mathf.Abs(ClampToScreen(o.Ax, o.FullW, width) - xi) < (b.FullW + o.FullW) * 0.5f + StackGap)
                        b.Short = true;
                }
            }
            // 2. Допустимые середины: над своим зданием (середина кнопки — в пределах его ширины) и кнопка целиком на экране.
            // Сдвиг нужен только у края экрана: кнопку срезанного краем завода экран прижимает к соседу, и сосед отходит от неё.
            for (int i = 0; i < n; i++)
            {
                var b = m_Buttons[i];
                float half = b.Width * 0.5f;
                float pref = ClampToScreen(b.Ax, b.Width, width);
                b.Lo = Mathf.Max(half + SidePad, b.Ax - b.HalfBuilding);
                b.Hi = Mathf.Min(Mathf.Max(half + SidePad, width - half - SidePad), b.Ax + b.HalfBuilding);
                if (b.Lo > b.Hi) b.Lo = b.Hi = pref;
                b.X = Mathf.Clamp(pref, b.Lo, b.Hi);
                // Порядок слева направо по середине завода (вставками).
                int k = i;
                while (k > 0 && m_Buttons[m_Order[k - 1]].Ax > b.Ax)
                {
                    m_Order[k] = m_Order[k - 1];
                    k--;
                }
                m_Order[k] = i;
            }
            // 3. Сдвиг по горизонтали: слева направо кнопка отходит вправо от левых соседей, затем справа налево — влево от правых.
            for (int a = 0; a < n; a++)
            {
                var b = m_Buttons[m_Order[a]];
                for (int c = 0; c < a; c++)
                {
                    var o = m_Buttons[m_Order[c]];
                    if (SameRow(b, o)) b.X = Mathf.Max(b.X, o.X + (o.Width + b.Width) * 0.5f + StackGap);
                }
                b.X = Mathf.Min(b.X, b.Hi);
            }
            for (int a = n - 1; a >= 0; a--)
            {
                var b = m_Buttons[m_Order[a]];
                for (int c = a + 1; c < n; c++)
                {
                    var o = m_Buttons[m_Order[c]];
                    if (SameRow(b, o)) b.X = Mathf.Min(b.X, o.X - (o.Width + b.Width) * 0.5f - StackGap);
                }
                b.X = Mathf.Max(b.X, b.Lo);
            }
            // 4. Показ: кнопка, которая всё же ложится на уже поставленную, не показывается (не прижимается поверх).
            m_Visible = m_Short = m_Dropped = 0;
            for (int a = 0; a < n; a++)
            {
                var b = m_Buttons[m_Order[a]];
                b.Placed = true;
                for (int c = 0; c < a && b.Placed; c++)
                {
                    var o = m_Buttons[m_Order[c]];
                    if (o.Placed && SameRow(b, o) && Mathf.Abs(o.X - b.X) < (o.Width + b.Width) * 0.5f + StackGap * 0.5f) b.Placed = false;
                }
                var rt = b.Refs.Rect;
                if (!b.Placed)
                {
                    // Слот остаётся за заводом (подписи не пересчитываются), кнопка скрыта.
                    m_Dropped++;
                    if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                    continue;
                }
                ApplyForm(b);
                var pos = PixelUi.Snap(new Vector2(b.X, b.Y));
                if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                m_Visible++;
                if (b.Short) m_Short++;
            }
        }

        static float ClampToScreen(float x, float w, float width)
        {
            float half = w * 0.5f;
            return Mathf.Clamp(x, half + SidePad, Mathf.Max(half + SidePad, width - half - SidePad));
        }

        /// <summary>Подпись, ширина и поля формы кнопки (только при смене формы или подписей).</summary>
        void ApplyForm(DeliverButton b)
        {
            int form = b.Short ? 1 : 0;
            if (b.Form == form) return;
            b.Form = form;
            float pad = b.Short ? ShortIconPad : IconPad, gap = b.Short ? ShortIconGap : IconGap, textPad = b.Short ? ShortTextPad : TextPad;
            var label = b.Refs.Label;
            Ui.SetText(label, b.Short ? b.ShortText : b.FullText);
            b.Refs.Rect.sizeDelta = new Vector2(b.Width, ButtonHeight);
            b.Icon.rectTransform.anchoredPosition = new Vector2(pad + b.IconW * 0.5f, Px);
            label.rectTransform.Anchor(0, 0, 1, 1, new Vector2(pad + b.IconW + gap, 3 * Px), new Vector2(-textPad + 2 * Px, -Px));
        }

        /// <summary>
        /// Стрелка у края не ложится на кнопки «Deliver» и на заводы на экране — полосу цикла, колесо и здание (снимки шахты 140:
        /// стрелка вправо закрывала кнопку завода у правого края, затем — колесо крайнего завода). Боковая стрелка уходит по высоте,
        /// верхняя и нижняя — вбок, в ближайшее к заводу свободное место полосы поля. Здание своего завода (может выглядывать
        /// из-за края) стрелке не мешает. Свободного места нет — стрелка остаётся против завода (<see cref="ArrowBlocked"/>).
        /// </summary>
        Vector2 AvoidObstacles(Vector2 c, int side, int targetId, float bandBottom, float bandTop, float width, out bool blocked)
        {
            blocked = false;
            bool horizontal = side >= 2;
            float hx = ChipSize * 0.5f + (horizontal ? SignReach : 0f), hy = ChipSize * 0.5f + (horizontal ? 0f : SignReach);
            float reach = ChipSize * 0.5f + SignReach + EdgeMargin;
            int n = 0;
            for (int i = 0; i < m_ButtonsShown; i++)
            {
                var o = m_Buttons[i].Refs.Rect;
                if (!o.gameObject.activeSelf) continue;
                var p = o.anchoredPosition;
                float half = o.sizeDelta.x * 0.5f;
                n = Forbid(n, Rect.MinMaxRect(p.x - half, p.y, p.x + half, p.y + ButtonHeight), c, horizontal, hx, hy);
            }
            for (int i = 0; i < m_BodyCount; i++)
                if (m_BodyIds[i] != targetId) n = Forbid(n, m_Bodies[i], c, horizontal, hx, hy);
            if (n == 0) return c;
            // Ближайшая к заводу свободная точка: сама точка или край запретного отрезка, в пределах полосы поля.
            float pref = horizontal ? c.y : c.x;
            float lo = horizontal ? bandBottom + reach : reach, hi = horizontal ? bandTop - reach : width - reach;
            float best = pref, bestDist = float.MaxValue;
            for (int k = -1; k < 2 * n; k++)
            {
                float v = k < 0 ? pref : (k & 1) == 0 ? m_ForbidLo[k >> 1] : m_ForbidHi[k >> 1];
                if (v < lo - 0.01f || v > hi + 0.01f) continue;
                float d = Mathf.Abs(v - pref);
                if (d >= bestDist) continue;
                bool free = true;
                for (int i = 0; i < n && free; i++) free = v <= m_ForbidLo[i] + 0.01f || v >= m_ForbidHi[i] - 0.01f;
                if (!free) continue;
                best = v;
                bestDist = d;
            }
            if (bestDist == float.MaxValue)
            {
                blocked = true;
                return c;
            }
            if (horizontal) c.y = best;
            else c.x = best;
            return c;
        }

        /// <summary>Запретный отрезок середины стрелки от препятствия <paramref name="r"/>, если оно на её пути (по другой оси).</summary>
        int Forbid(int n, Rect r, Vector2 c, bool horizontal, float hx, float hy)
        {
            if (n >= m_ForbidLo.Length) return n;
            if (horizontal)
            {
                if (c.x + hx <= r.xMin - StackGap || c.x - hx >= r.xMax + StackGap) return n;
                m_ForbidLo[n] = r.yMin - StackGap - hy;
                m_ForbidHi[n] = r.yMax + StackGap + hy;
            }
            else
            {
                if (c.y + hy <= r.yMin - StackGap || c.y - hy >= r.yMax + StackGap) return n;
                m_ForbidLo[n] = r.xMin - StackGap - hx;
                m_ForbidHi[n] = r.xMax + StackGap + hx;
            }
            return n + 1;
        }

        static void Hide(DeliverButton b)
        {
            b.TargetId = -1;
            if (b.Refs.Rect.gameObject.activeSelf) b.Refs.Rect.gameObject.SetActive(false);
        }

        static void SetArrowIcon(Arrow a, string icon)
        {
            if (icon == a.IconName) return;
            a.IconName = icon;
            PixelUi.SetIcon(a.Icon, icon);
        }

        void PlaceArrow(Arrow a, int side, float bandBottom, float bandTop, float width)
        {
            if (a.Count == 0 || a.TargetId < 0)
            {
                m_ArrowBlocked[side] = false;
                a.TargetId = a.Count == 0 ? -1 : a.TargetId;
                if (a.Rect.gameObject.activeSelf) a.Rect.gameObject.SetActive(false);
                return;
            }
            var dir = side == 0 ? Vector2.up : side == 1 ? Vector2.down : side == 2 ? Vector2.left : Vector2.right;
            string sign = side == 0 ? (a.At.x < 0f ? "icon_arrow_up_left" : a.At.x > width ? "icon_arrow_up_right" : "icon_arrow_up")
                : side == 1 ? (a.At.x < 0f ? "icon_arrow_down_left" : a.At.x > width ? "icon_arrow_down_right" : "icon_arrow_down")
                : side == 2 ? "icon_arrow_left" : "icon_arrow_right";
            if (sign != a.SignName)
            {
                a.SignName = sign;
                PixelUi.SetIcon(a.Sign, sign);
            }
            // Ячейка у края полосы поля против завода, стрелка — снаружи ячейки к заводу, покачивается на пиксель.
            float reach = ChipSize * 0.5f + SignReach + EdgeMargin;
            float x = Mathf.Clamp(a.At.x, reach, width - reach);
            float y = Mathf.Clamp(a.At.y, bandBottom + reach, bandTop - reach);
            var c = AvoidObstacles(new Vector2(x, y), side, a.TargetId, bandBottom, bandTop, width, out m_ArrowBlocked[side]);
            float bob = ((int)(Time.unscaledTime / BobPeriod) & 1) * Px;
            var pos = PixelUi.Snap(c + dir * bob);
            if (a.Rect.anchoredPosition != pos) a.Rect.anchoredPosition = pos;
            var signPos = PixelUi.Snap(dir * (ChipSize * 0.5f + 5 * Px));
            if (a.Sign.rectTransform.anchoredPosition != signPos) a.Sign.rectTransform.anchoredPosition = signPos;
            a.Badge.SetCount(a.Count > 1 ? a.Count : 0);
            if (!a.Rect.gameObject.activeSelf) a.Rect.gameObject.SetActive(true);
        }

        void HideAll()
        {
            for (int i = 0; i < m_ButtonsShown; i++) Hide(m_Buttons[i]);
            m_ButtonsShown = m_Visible = m_Short = m_Dropped = 0;
            for (int s = 0; s < Sides; s++)
            {
                m_Arrows[s].TargetId = -1;
                m_ArrowBlocked[s] = false;
                if (m_Arrows[s].Rect.gameObject.activeSelf) m_Arrows[s].Rect.gameObject.SetActive(false);
            }
        }
    }
}
