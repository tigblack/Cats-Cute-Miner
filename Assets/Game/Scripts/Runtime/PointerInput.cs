using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Жесты (ТЗ 13.3) через карту действий Gameplay из Assets/Game/Input/CatMinersInput.inputactions,
    /// единый путь для мыши и первого касания:
    /// тап — выбор/действие (добыча на поле собирается первой); перенос, начатый на котике, — перенос/объединение; на фоне — прокрутка;
    /// жест, начатый на UI, поле не трогает; колесо мыши прокручивает поле.
    /// Действия опрашиваются в Update, без callback: принадлежность UI определяется в момент нажатия
    /// через EventSystem.RaycastAll (в callback InputAction Input System отдаёт UI-состояние прошлого кадра).
    /// </summary>
    public sealed class PointerInput : MonoBehaviour
    {
        public const string GameplayMapName = "Gameplay";
        public const string PointActionName = "Point";
        public const string PressActionName = "Press";
        public const string ScrollActionName = "Scroll";
        public const string TouchPhaseActionName = "PrimaryTouchPhase";

        enum Mode { None, Pending, DragCat, Scroll, OverUi, DragDynamite }

        /// <summary>Краевая зона автопрокрутки при переносе — доля высоты поля (ТЗ 13.3: около 15%).</summary>
        public float AutoScrollEdgeFraction = 0.15f;
        /// <summary>Скорость автопрокрутки у самого края, строк в секунду (ТЗ 13.3: до 12).</summary>
        public float AutoScrollMaxRowsPerSec = 12f;

        GameController m_Game;
        Mode m_Mode;
        Vector2 m_PressPos;
        Vector2 m_LastPos;
        float m_LastTime;
        float m_ScrollVelocity;
        int m_PressCatId;
        bool m_DynEnteredField;
        Canvas m_HudCanvas;
        readonly List<RaycastResult> m_UiHits = new List<RaycastResult>();

        InputActionAsset m_ActionsCopy;
        InputActionMap m_Map;
        InputAction m_Point, m_Press, m_Scroll, m_TouchPhase;

        public bool IsDraggingCat => m_Mode == Mode.DragCat;
        public bool IsDraggingDynamite => m_Mode == Mode.DragDynamite;
        /// <summary>Карта Gameplay, которую опрашивает компонент (личная копия, создаётся при включении компонента; null, пока он выключен).</summary>
        public InputActionMap GameplayMap => m_Map;

        public void Init(GameController game)
        {
            m_Game = game;
            if (isActiveAndEnabled) EnableActions();
        }

        void OnEnable() => EnableActions();

        // Выключение сразу освобождает личную копию: мониторы состояния снимаются в той системе ввода,
        // где карта включалась (важно, если систему ввода потом подменяют, например InputTestFixture).
        // Повторное включение создаёт копию заново.
        void OnDisable() => ReleaseActions();

        void OnDestroy() => ReleaseActions();

        // ───────────── Действия ─────────────

        void EnableActions()
        {
            if (m_Map == null) CreateActions();
            if (!m_Map.enabled) m_Map.Enable();
        }

        /// <summary>
        /// Источник карты: ассет сцены (GameBootstrap.InputActions), иначе project-wide actions с картой Gameplay,
        /// иначе такая же карта, собранная кодом (сцена ещё не настроена). Используется личная копия:
        /// включение и выключение не трогают общий ассет, который заодно служит модулю UI и project-wide actions.
        /// </summary>
        void CreateActions()
        {
            var source = GameBootstrap.ActiveInputActions;
            if (source == null || source.FindActionMap(GameplayMapName) == null) source = InputSystem.actions;
            if (source != null && source.FindActionMap(GameplayMapName) != null)
            {
                m_ActionsCopy = Instantiate(source);
                m_Map = m_ActionsCopy.FindActionMap(GameplayMapName);
                if (BindActions()) return;
                Debug.LogWarning("[CatMiners] В карте " + GameplayMapName + " ассета " + source.name + " не хватает действий; используется карта по умолчанию.");
                Destroy(m_ActionsCopy);
                m_ActionsCopy = null;
            }
            m_Map = CreateDefaultGameplayMap();
            BindActions();
        }

        bool BindActions()
        {
            m_Point = m_Map.FindAction(PointActionName);
            m_Press = m_Map.FindAction(PressActionName);
            m_Scroll = m_Map.FindAction(ScrollActionName);
            m_TouchPhase = m_Map.FindAction(TouchPhaseActionName);
            return m_Point != null && m_Press != null && m_Scroll != null && m_TouchPhase != null;
        }

        /// <summary>Та же карта, что в CatMinersInput.inputactions (ТЗ 13.3).</summary>
        public static InputActionMap CreateDefaultGameplayMap()
        {
            var map = new InputActionMap(GameplayMapName);
            // PassThrough: позиция последнего активного указателя, без выбора «самого сильного» по длине вектора.
            var point = map.AddAction(PointActionName, InputActionType.PassThrough, "<Pointer>/position", expectedControlLayout: "Vector2");
            point.wantsInitialStateCheck = true;
            map.AddAction(PressActionName, InputActionType.Button, "<Pointer>/press", expectedControlLayout: "Button");
            map.AddAction(ScrollActionName, InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");
            // Фаза первого касания: отмена касания системой без опроса устройств.
            map.AddAction(TouchPhaseActionName, InputActionType.Value, "<Touchscreen>/primaryTouch/phase", expectedControlLayout: "TouchPhase");
            return map;
        }

        void ReleaseActions()
        {
            if (m_Map == null) return;
            m_Map.Disable();
            // Состояние действий (у копии ассета оно общее для всех её карт) освобождается сразу:
            // Destroy ассета отложен до конца кадра, а к тому времени глобальное состояние ввода может быть другим.
            m_Map.Dispose();
            if (m_ActionsCopy != null) Destroy(m_ActionsCopy);
            m_ActionsCopy = null;
            m_Map = null;
            m_Point = m_Press = m_Scroll = m_TouchPhase = null;
        }

        // ───────────── Жесты ─────────────

        /// <summary>Порог начала переноса: max(pixelDragThreshold, 0.1 дюйма); при dpi = 0 — 10 px × scaleFactor Canvas HUD.</summary>
        float DragThresholdPixels
        {
            get
            {
                float dpi = Screen.dpi;
                float byDpi = dpi > 0 ? 0.1f * dpi : 10f * HudScaleFactor();
                int es = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10;
                return Mathf.Max(es, byDpi);
            }
        }

        float HudScaleFactor()
        {
            if (m_HudCanvas == null && m_Game.Hud != null)
            {
                // Компонент Hud живёт на объекте Canvas; на случай другой иерархии ищется и выше, и ниже.
                m_HudCanvas = m_Game.Hud.GetComponentInParent<Canvas>();
                if (m_HudCanvas == null) m_HudCanvas = m_Game.Hud.GetComponentInChildren<Canvas>();
            }
            if (m_HudCanvas == null) return 1f;
            float scale = m_HudCanvas.rootCanvas.scaleFactor;
            return scale > 0f ? scale : 1f;
        }

        bool IsOverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = pos };
            m_UiHits.Clear();
            es.RaycastAll(data, m_UiHits);
            return m_UiHits.Count > 0;
        }

        bool LastPointerIsTouch()
        {
            var control = m_Point.activeControl;
            return control != null && control.device is Touchscreen;
        }

        /// <summary>Точка над полем — между нижней и верхней панелями HUD.</summary>
        bool IsInsideField(Vector2 pos)
        {
            // Полоса поля между панелями HUD — от safe area (на телефонах с вырезом доли не от всего экрана).
            m_Game.Hud.FieldBandPixels(out float bottom, out float top);
            return pos.y >= bottom && pos.y <= top;
        }

        /// <summary>Начать перенос динамита из ячейки HUD.</summary>
        public void BeginDynamiteDrag()
        {
            if (m_Game.Session.Player.Dynamite <= 0) return;
            // Второй палец на ячейке во время переноса котика: котик возвращается, а не остаётся «в руке».
            if (m_Mode == Mode.DragCat) m_Game.CancelCatDrag();
            m_Mode = Mode.DragDynamite;
            // Автопрокрутка включится, когда палец дойдёт до поля: ячейка лежит в нижней панели, ниже края поля.
            m_DynEnteredField = false;
            m_Game.Hud.ShowDynamiteGhost(true);
        }

        void Update()
        {
            if (m_Game == null || m_Game.Session == null || m_Map == null || !m_Map.enabled) return;
            // Анимация перехода в следующую шахту: поле показывает старую карту, жесты не принимаются.
            if (m_Game.InTransition)
            {
                if (m_Mode != Mode.None) CancelAll();
                return;
            }
            Vector2 pos = m_Point.ReadValue<Vector2>();
            bool pressed = m_Press.IsPressed();
            bool down = m_Press.WasPressedThisFrame();
            bool up = m_Press.WasReleasedThisFrame();

            // Касание отменено системой (звонок, системный жест): перенос отменяется, тап и сброс не выполняются.
            // Фаза Canceled держится до следующего касания, поэтому вне кадра отмены она учитывается, только пока
            // последним указателем был палец: мышь после отменённого касания работает как обычно.
            if (m_TouchPhase.ReadValue<TouchPhase>() == TouchPhase.Canceled && (m_TouchPhase.WasPerformedThisFrame() || LastPointerIsTouch()))
            {
                if (m_Mode != Mode.None) CancelAll();
                return;
            }

            if (m_Mode == Mode.None)
            {
                float wheel = m_Scroll.ReadValue<float>();
                if (Mathf.Abs(wheel) > 0.01f && !IsOverUi(pos)) m_Game.Rig.ScrollWorld(wheel > 0 ? 1.2f : -1.2f);
            }

            if (m_Mode == Mode.DragDynamite)
            {
                m_Game.Hud.MoveDynamiteGhost(pos);
                if (!m_DynEnteredField && IsInsideField(pos)) m_DynEnteredField = true;
                if (m_DynEnteredField) AutoScroll(pos);
                if (up || !pressed)
                {
                    m_Game.Hud.ShowDynamiteGhost(false);
                    m_Mode = Mode.None;
                    if (!IsOverUi(pos) && m_Game.Rig.ScreenToGround(pos, out var world))
                    {
                        var g = m_Game.Board.WorldToGrid(world);
                        m_Game.UseDynamite(g.x, g.y);
                    }
                }
                return;
            }

            if (down)
            {
                m_PressPos = pos;
                m_LastPos = pos;
                m_LastTime = Time.unscaledTime;
                m_ScrollVelocity = 0f;
                m_Game.Rig.StopFling();
                if (IsOverUi(pos))
                {
                    m_Mode = Mode.OverUi;
                    return;
                }
                m_Mode = Mode.Pending;
                m_PressCatId = PickCat(pos);
                return;
            }

            if (m_Mode == Mode.OverUi)
            {
                if (up || !pressed) m_Mode = Mode.None;
                return;
            }

            if (m_Mode == Mode.Pending)
            {
                if (up || !pressed)
                {
                    m_Mode = Mode.None;
                    Tap(pos);
                    return;
                }
                if ((pos - m_PressPos).magnitude >= DragThresholdPixels)
                {
                    if (m_PressCatId > 0 && m_Game.Session.FindCat(m_PressCatId) != null)
                    {
                        m_Mode = Mode.DragCat;
                        m_Game.BeginCatDrag(m_PressCatId);
                    }
                    else m_Mode = Mode.Scroll;
                }
            }

            if (m_Mode == Mode.Scroll)
            {
                float dy = pos.y - m_LastPos.y;
                m_Game.Rig.ScrollByPixels(dy);
                float dt = Mathf.Max(1e-3f, Time.unscaledTime - m_LastTime);
                m_ScrollVelocity = Mathf.Lerp(m_ScrollVelocity, dy / dt, 0.5f);
                if (up || !pressed)
                {
                    m_Game.Rig.Fling(m_ScrollVelocity);
                    m_Mode = Mode.None;
                }
            }
            else if (m_Mode == Mode.DragCat)
            {
                if (m_Game.Session.FindCat(m_PressCatId) == null)
                {
                    m_Game.CancelCatDrag();
                    m_Mode = Mode.None;
                }
                else
                {
                    if (m_Game.Rig.ScreenToGround(pos, out var world)) m_Game.UpdateCatDrag(world);
                    AutoScroll(pos);
                    if (up || !pressed)
                    {
                        m_Mode = Mode.None;
                        if (!IsOverUi(pos) && m_Game.Rig.ScreenToGround(pos, out var dropWorld))
                        {
                            var g = m_Game.Board.WorldToGrid(dropWorld);
                            m_Game.DropCat(m_PressCatId, g.x, g.y);
                        }
                        else m_Game.CancelCatDrag();
                    }
                }
            }
            m_LastPos = pos;
            m_LastTime = Time.unscaledTime;
        }

        /// <summary>
        /// Автопрокрутка у верхнего и нижнего края поля при переносе (обязательна, ТЗ 13.3): краевая зона —
        /// AutoScrollEdgeFraction высоты поля, скорость растёт к краю до AutoScrollMaxRowsPerSec.
        /// </summary>
        void AutoScroll(Vector2 pos)
        {
            m_Game.Hud.FieldBandPixels(out float bottom, out float top);
            float zone = (top - bottom) * Mathf.Clamp(AutoScrollEdgeFraction, 0f, 0.5f);
            float maxSpeed = Mathf.Max(0f, AutoScrollMaxRowsPerSec);
            float speedRows = 0f;
            if (pos.y > top - zone) speedRows = Mathf.InverseLerp(top - zone, top, pos.y) * maxSpeed;
            else if (pos.y < bottom + zone) speedRows = -Mathf.InverseLerp(bottom + zone, bottom, pos.y) * maxSpeed;
            if (Mathf.Abs(speedRows) > 0.01f) m_Game.Rig.ScrollWorld(speedRows * Time.unscaledDeltaTime);
        }

        /// <summary>Отмена переноса при паузе, потере фокуса или отмене касания.</summary>
        public void CancelAll()
        {
            if (m_Mode == Mode.DragCat) m_Game.CancelCatDrag();
            if (m_Mode == Mode.DragDynamite) m_Game.Hud.ShowDynamiteGhost(false);
            m_Mode = Mode.None;
        }

        int PickCat(Vector2 pos)
        {
            var ray = m_Game.Rig.Cam.ScreenPointToRay(pos);
            var hits = Physics.RaycastAll(ray, 200f);
            float best = float.MaxValue;
            int id = 0;
            foreach (var h in hits)
            {
                var p = h.collider.GetComponent<Pickable>();
                if (p == null || p.Kind != Pickable.PickKind.Cat) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    id = p.Id;
                }
            }
            return id;
        }

        void Tap(Vector2 pos)
        {
            var ray = m_Game.Rig.Cam.ScreenPointToRay(pos);
            var hits = Physics.RaycastAll(ray, 200f);
            Pickable best = null;
            float bestDist = float.MaxValue;
            int bestPriority = int.MaxValue;
            foreach (var h in hits)
            {
                var p = h.collider.GetComponent<Pickable>();
                if (p == null) continue;
                // Добыча ждёт тапа и лежит поверх клетки — она важнее всего под ней; пузырь статуса висит над объектом — он важнее
                // котика или контейнера под ним.
                int priority = p.Kind == Pickable.PickKind.Loot ? -2
                    : (p.Bubble ? -1 : (p.Kind == Pickable.PickKind.Container ? 0 : (p.Kind == Pickable.PickKind.Cat ? 1 : 2)));
                if (priority < bestPriority || (priority == bestPriority && h.distance < bestDist))
                {
                    best = p;
                    bestDist = h.distance;
                    bestPriority = priority;
                }
            }
            if (best == null)
            {
                m_Game.ClearSelection();
                return;
            }
            switch (best.Kind)
            {
                case Pickable.PickKind.Loot: m_Game.CollectLoot(best.Id); break;
                case Pickable.PickKind.Container: m_Game.OpenContainer(best.Id); break;
                case Pickable.PickKind.Cat: m_Game.SelectCat(best.Id); break;
                case Pickable.PickKind.Object: m_Game.TapObject(best.Id); break;
            }
        }
    }
}
