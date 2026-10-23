using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>Что можно выбрать тапом по полю нового режима (ТЗ 5): котик, цель, постройки лагеря.</summary>
    public enum OutpostPickKind
    {
        /// <summary>Котик: Id — WorkerId.</summary>
        Worker,
        /// <summary>Цель r/s/x/p: Id — Id цели (индекс якоря).</summary>
        Target,
        /// <summary>Постройки лагеря: открыть панель «База». Id = 0.</summary>
        Base,
        /// <summary>Склад товаров во дворе лагеря (ТЗ 2.2 §6.1): открыть рынок. Id = 0; приоритет выше <see cref="Base"/>.</summary>
        Warehouse,
        /// <summary>Бочка динамита (ТЗ 10.6): тап — взрыв. Id — номер бочки; приоритет выше всех (лежит среди котиков и целей).</summary>
        Dynamite,
    }

    /// <summary>
    /// Метка коллайдера для выбора лучом (замена старого <see cref="Pickable"/> в новом режиме). Груз на земле меток не имеет:
    /// тап по монетам ничего не зачисляет (ТЗ 5). При нескольких попаданиях выигрывает меньший <see cref="Priority"/>
    /// (котик → цель, в том числе открытый завод → склад товаров → база), при равенстве — ближайший к камере.
    /// </summary>
    public sealed class OutpostPickable : MonoBehaviour
    {
        public OutpostPickKind Kind;
        public int Id;

        /// <summary>Приоритет вида при пересечении коллайдеров: −1 — бочка динамита, 0 — котик, 1 — цель, 2 — склад товаров, 3 — база.</summary>
        public int Priority
        {
            get
            {
                switch (Kind)
                {
                    case OutpostPickKind.Dynamite: return -1;
                    case OutpostPickKind.Worker: return 0;
                    case OutpostPickKind.Target: return 1;
                    case OutpostPickKind.Warehouse: return 2;
                    default: return 3;
                }
            }
        }

        public OutpostSelection ToSelection()
        {
            switch (Kind)
            {
                case OutpostPickKind.Worker: return new OutpostSelection { Kind = OutpostSelectionKind.Worker, Id = Id };
                case OutpostPickKind.Target: return new OutpostSelection { Kind = OutpostSelectionKind.Target, Id = Id };
                case OutpostPickKind.Warehouse: return new OutpostSelection { Kind = OutpostSelectionKind.Warehouse, Id = -1 };
                case OutpostPickKind.Dynamite: return new OutpostSelection { Kind = OutpostSelectionKind.Dynamite, Id = Id };
                default: return new OutpostSelection { Kind = OutpostSelectionKind.Base, Id = -1 };
            }
        }

        public static OutpostPickable Add(GameObject go, OutpostPickKind kind, int id)
        {
            var p = go.AddComponent<OutpostPickable>();
            p.Kind = kind;
            p.Id = id;
            return p;
        }

        // Общий буфер попаданий: выбор тапом не выделяет память.
        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        /// <summary>
        /// Лучший выбор под лучом камеры (Physics.RaycastNonAlloc по всем коллайдерам с меткой): меньший приоритет,
        /// затем меньшее расстояние. false — под лучом ничего выбираемого.
        /// </summary>
        public static bool TryPickBest(Ray ray, out OutpostPickable best, float maxDistance = 500f)
        {
            best = null;
            int n = Physics.RaycastNonAlloc(ray, s_Hits, maxDistance, ~0, QueryTriggerInteraction.Collide);
            float bestDist = float.MaxValue;
            int bestPriority = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = s_Hits[i].collider;
                if (col == null || !col.enabled) continue;
                var p = col.GetComponent<OutpostPickable>();
                if (p == null || !p.isActiveAndEnabled) continue;
                int pr = p.Priority;
                float d = s_Hits[i].distance;
                if (pr < bestPriority || (pr == bestPriority && d < bestDist))
                {
                    best = p;
                    bestPriority = pr;
                    bestDist = d;
                }
            }
            return best != null;
        }
    }
}
