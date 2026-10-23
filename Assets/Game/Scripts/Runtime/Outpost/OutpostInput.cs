using CatMiners.Core.Outpost;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Ввод поля (ТЗ 4, 6.6): перетаскивание и колесо прокручивают камеру; тап/клик по цели, которую можно ударить, — удар кирки
    /// игрока; по котику, базе или недоступной цели — сведения; удержание без сдвига (<see cref="LongPressSeconds"/>) — сведения
    /// о любом объекте, в том числе о цели, которую тап бы ударил.
    /// Заводы (ТЗ 2.2 §8): тап по закрытому заводу — удар по цепям, по заводу в Ready — доставка партии, по остальным открытым —
    /// сведения; тап по складу товаров во дворе — рынок. Плавающие кнопки «Deliver» и стрелки к готовым заводам —
    /// <see cref="FactoryOverlay"/> (UI: касание по ним поле не получает).
    /// </summary>
    public sealed class OutpostInput : MonoBehaviour
    {
        /// <summary>Удержание без сдвига, после которого касание открывает сведения вместо удара (секунды).</summary>
        public const float LongPressSeconds = 0.45f;

        OutpostController m_Game;
        bool m_Down, m_Drag, m_Cancelled, m_LongPressed;
        int m_TouchId = -1;
        Vector2 m_Start, m_Last;
        Vector2 m_Velocity;
        float m_DownTime;
        FactoryOverlay m_Factories;
        /// <summary>Кнопки «Deliver», стрелки к готовым заводам и тосты заводов (создаются в <see cref="Init"/>).</summary>
        public FactoryOverlay Factories => m_Factories;
        public void Init(OutpostController game)
        {
            m_Game = game;
            if (m_Factories == null && game != null) m_Factories = FactoryOverlay.Create(game);
        }
        public void CancelGesture()
        {
            m_Down = false;
            m_Cancelled = true;
            m_Drag = false;
            m_LongPressed = false;
            m_Velocity = Vector2.zero;
            m_Game?.Rig?.StopFling();
        }
        bool FieldEnabled => m_Game != null && m_Game.Phase == OutpostControllerPhase.Playing && !m_Game.InTransition && !m_Game.Hud.AnyWindowOpen;
        bool InField(Vector2 p)
        {
            m_Game.Hud.FieldBandPixels(out float bottom, out float top);
            return p.y >= bottom && p.y <= top;
        }
        void Update()
        {
            if (m_Game == null) return;
            // HUD owns Back (including the InputSystem UI cancel action).
            if (!FieldEnabled) { if (m_Down) CancelGesture(); return; }
            var touch = Touchscreen.current;
            var mouse = Mouse.current;
            Vector2 position;
            bool pressed, released, held;
            int id;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
            {
                var t = touch.primaryTouch;
                if (t.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled) { CancelGesture(); return; }
                position = t.position.ReadValue(); id = t.touchId.ReadValue();
                pressed = t.press.wasPressedThisFrame; released = t.press.wasReleasedThisFrame; held = t.press.isPressed;
            }
            else if (mouse != null)
            {
                position = mouse.position.ReadValue(); id = -1;
                pressed = mouse.leftButton.wasPressedThisFrame; released = mouse.leftButton.wasReleasedThisFrame; held = mouse.leftButton.isPressed;
                if (!m_Down && InField(position) && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                {
                    Vector2 wheel = mouse.scroll.ReadValue();
                    if (wheel.sqrMagnitude > 0f) m_Game.Rig.ScrollByPixels(-wheel * 0.6f);
                }
            }
            else return;
            if (m_Down && id != m_TouchId) { CancelGesture(); return; }
            if (pressed)
            {
                m_Cancelled = false;
                if (!InField(position) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(id))) { m_Cancelled = true; return; }
                m_Down = true; m_Drag = false; m_LongPressed = false; m_TouchId = id;
                m_Start = m_Last = position; m_Velocity = Vector2.zero;
                m_DownTime = Time.unscaledTime;
                m_Game.Rig.StopFling();
            }
            if (!m_Down || m_Cancelled) return;
            if (id != m_TouchId) { CancelGesture(); return; }
            if (held || released)
            {
                float threshold = Mathf.Max(8, Screen.height * 0.008f);
                if ((position - m_Start).sqrMagnitude > threshold * threshold) m_Drag = true;
                if (m_Drag)
                {
                    Vector2 delta = position - m_Last;
                    m_Game.Rig.ScrollByPixels(delta);
                    Vector2 velocity = delta / Mathf.Max(0.001f, Time.unscaledDeltaTime);
                    m_Velocity = Vector2.Lerp(m_Velocity, velocity, 0.5f);
                }
                else if (held && !m_LongPressed && Time.unscaledTime - m_DownTime >= LongPressSeconds)
                {
                    // Удержание — сведения о любом объекте (удар по отпусканию уже не выполняется).
                    m_LongPressed = true;
                    if (TryPick(m_Start, out var selection, out _)) Select(selection);
                }
                m_Last = position;
            }
            if (released)
            {
                m_Down = false;
                if (m_Drag) { m_Game.Rig.Fling(m_Velocity); return; }
                if (m_LongPressed || !InField(position) ||
                    (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(id))) return;
                if (!TryPick(position, out var selection, out var world)) { m_Game.ClearSelection(); return; }
                Tap(selection, world);
            }
        }

        /// <summary>
        /// Тап по предприятию всегда открывает карточку его менеджера. Прочие доступные цели — удар кирки игрока;
        /// остальные объекты — сведения или рынок (склад).
        /// </summary>
        public void Tap(OutpostSelection selection, Vector3 world)
        {
            if (m_Game == null) return;
            // Бочка динамита (ТЗ 10.6): тап — взрыв.
            if (selection.Kind == OutpostSelectionKind.Dynamite)
            {
                m_Game.Detonate();
                return;
            }
            if (selection.Kind == OutpostSelectionKind.Target)
            {
                var factory = m_Game.FactoryOf(selection.Id);
                if (factory != null)
                {
                    m_Game.SelectTarget(selection.Id);
                    return;
                }
                // Тап по цели — удар кирки игрока; если цель сейчас ударить нельзя (за воротами, разрушена, открытый завод) — сведения.
                if (m_Game.BeginStrike(selection.Id, world)) return;
            }
            Select(selection);
        }

        /// <summary>Объект под точкой экрана и мировая точка касания на плоскости поля.</summary>
        bool TryPick(Vector2 screen, out OutpostSelection selection, out Vector3 world)
        {
            var ray = m_Game.Rig.Cam.ScreenPointToRay(screen);
            bool ground = m_Game.Rig.ScreenToGround(screen, out world);
            if (OutpostPickable.TryPickBest(ray, out var picked))
            {
                selection = picked.ToSelection();
                if (!ground && selection.Kind == OutpostSelectionKind.Target) m_Game.TryGetTargetWorldPosition(selection.Id, out world);
                return true;
            }
            if (ground && m_Game.Board.TryPickAtWorld(world, out selection)) return true;
            selection = OutpostSelection.None;
            return false;
        }

        void Select(OutpostSelection selection)
        {
            switch (selection.Kind)
            {
                case OutpostSelectionKind.Worker: m_Game.SelectWorker(selection.Id); break;
                case OutpostSelectionKind.Target: m_Game.SelectTarget(selection.Id); break;
                case OutpostSelectionKind.Base: m_Game.SelectBase(); break;
                // Склад товаров (ТЗ 2.2 §6.1): тап открывает рынок (окно отменяет текущий жест; выбора на поле не остаётся).
                case OutpostSelectionKind.Warehouse:
                    m_Game.ClearSelection();
                    m_Game.Hud.ShowMarket();
                    break;
                default: m_Game.ClearSelection(); break;
            }
        }
        void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }
        void OnApplicationPause(bool paused) { if (paused) CancelGesture(); }
        void OnDisable() => CancelGesture();
    }
}
