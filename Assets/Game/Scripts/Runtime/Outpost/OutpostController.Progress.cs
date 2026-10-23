using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Первая сессия (Docs/Rework/FIRST_SESSION.md) для HUD: отметки показа сцен спасения Mia и просмотра строк «Базы» (профиль,
    /// сразу запись) и мировые точки сцены спасения. Доступность функций считает ядро (<see cref="ProgressionService"/>), здесь —
    /// только показ.
    /// </summary>
    public sealed partial class OutpostController
    {
        /// <summary>
        /// Отметить показ (кадр клетки, окно «Mia is free!», строка «Базы») и сразу записать снимок. Во время расчёта офлайна,
        /// перехода или ошибки записи отметка остаётся в памяти и уходит со следующей записью.
        /// </summary>
        public void MarkProgress(string key)
        {
            if (State == null || string.IsNullOrEmpty(key)) return;
            if (ProgressionService.MarkShown(State.Profile, key) && CommandsAllowed) Commit(State, Map, null);
        }

        /// <summary>Центр клетки спасения в мире; false — в шахте нет спасения.</summary>
        public bool TryGetRescueCageWorldPosition(out Vector3 world)
        {
            world = default;
            return Board != null && Board.TryGetRescueCageWorldPosition(out world);
        }

        /// <summary>Идёт сцена спасения (дверца, радость Mia): окно «Mia is free!» и итоги ждут.</summary>
        public bool RescueSceneActive => Board != null && Board.RescueSceneActive;

        /// <summary>Плавно подвести камеру к мировой точке (цель первой сессии); при «Fewer effects» — сразу.</summary>
        public void FocusWorld(Vector3 world)
        {
            if (Rig == null || InTransition) return;
            Input?.CancelGesture();
            var at = new Vector2(world.x, world.z);
            if (VisualSettings.ReduceEffects) Rig.FocusOn(at);
            else Rig.FocusOnSmooth(at, 0.7f);
        }
    }
}
