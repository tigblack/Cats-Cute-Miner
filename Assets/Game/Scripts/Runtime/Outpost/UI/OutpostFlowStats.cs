using CatMiners.Core.Outpost;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>Что сильнее всего ограничивает поток монет по наблюдаемым состояниям команды (ТЗ 14, панель «База»).</summary>
    public enum OutpostFlowLimit
    {
        /// <summary>Данных ещё мало (меньше 5 с наблюдения).</summary>
        Unknown,
        /// <summary>Явного узкого места нет.</summary>
        Balanced,
        /// <summary>Большую часть времени котики бьют породу.</summary>
        Mining,
        /// <summary>Большую часть времени котики в пути.</summary>
        Walking,
        /// <summary>Котики заметную часть времени ждут места у склада.</summary>
        Queue,
        /// <summary>Большинство рейсов на базу — с полной сумкой.</summary>
        FullBags,
    }

    /// <summary>
    /// Подсказка ограничения потока по реальным состояниям (ТЗ 14): раз в <see cref="SampleInterval"/> с реального времени
    /// считает котиков по режимам ядра (добыча / путь / очередь склада / прочее) в кольце из <see cref="Buckets"/> корзин
    /// (≈20 с), а события WorkerModeChanged → Returning — как рейсы на базу (полная сумка — Bag.Count ≥ Capacity).
    /// Ничего не прогнозирует и не считает окупаемость: только доли наблюдаемого времени. Без выделений памяти.
    /// </summary>
    public sealed class OutpostFlowStats
    {
        public const float SampleInterval = 0.5f;
        public const int Buckets = 40;
        /// <summary>Минимум корзин (5 с) до первой подсказки.</summary>
        const int MinBuckets = 10;

        readonly int[] m_Mining = new int[Buckets];
        readonly int[] m_Walking = new int[Buckets];
        readonly int[] m_Waiting = new int[Buckets];
        readonly int[] m_Total = new int[Buckets];
        readonly int[] m_Trips = new int[Buckets];
        readonly int[] m_FullTrips = new int[Buckets];
        int m_Head = -1, m_Filled;
        float m_NextSample;

        /// <summary>Сколько корзин заполнено (0…<see cref="Buckets"/>).</summary>
        public int Filled => m_Filled;

        /// <summary>Сбросить наблюдения (новая шахта, загрузка, офлайн).</summary>
        public void Reset()
        {
            for (int i = 0; i < Buckets; i++) m_Mining[i] = m_Walking[i] = m_Waiting[i] = m_Total[i] = m_Trips[i] = m_FullTrips[i] = 0;
            m_Head = -1;
            m_Filled = 0;
            m_NextSample = 0f;
        }

        /// <summary>Снять пробу, если подошло время (<paramref name="now"/> — Time.unscaledTime). Только в фазе Running.</summary>
        public void Tick(OutpostSimulation sim, float now)
        {
            if (sim == null || now < m_NextSample) return;
            m_NextSample = now + SampleInterval;
            if (sim.Run.Phase != RunPhase.Running) return;
            m_Head = (m_Head + 1) % Buckets;
            m_Mining[m_Head] = m_Walking[m_Head] = m_Waiting[m_Head] = m_Total[m_Head] = m_Trips[m_Head] = m_FullTrips[m_Head] = 0;
            if (m_Filled < Buckets) m_Filled++;
            var workers = sim.Run.Workers;
            for (int i = 0; i < workers.Count; i++)
            {
                switch (workers[i].Mode)
                {
                    case WorkerMode.Mining:
                        m_Mining[m_Head]++;
                        break;
                    case WorkerMode.MovingToWork:
                    case WorkerMode.MovingToCargo:
                    case WorkerMode.Returning:
                    case WorkerMode.GoingHome:
                        m_Walking[m_Head]++;
                        break;
                    case WorkerMode.WaitingDepot:
                        m_Waiting[m_Head]++;
                        break;
                }
                m_Total[m_Head]++;
            }
        }

        /// <summary>Событие ядра: начало рейса на базу (полная ли сумка — по текущей вместимости).</summary>
        public void OnEvent(in OutpostEvent e, OutpostSimulation sim)
        {
            if (e.Type != OutpostEventType.WorkerModeChanged || e.Mode != WorkerMode.Returning || m_Head < 0 || sim == null) return;
            var w = sim.WorkerById(e.WorkerId);
            if (w == null) return;
            m_Trips[m_Head]++;
            if (w.Bag.Count >= sim.Stats.Capacity) m_FullTrips[m_Head]++;
        }

        /// <summary>Главное ограничение за последние ≈20 с и его доля в процентах (кратно 5).</summary>
        public OutpostFlowLimit Evaluate(out int percent)
        {
            percent = 0;
            if (m_Filled < MinBuckets) return OutpostFlowLimit.Unknown;
            int mining = 0, walking = 0, waiting = 0, total = 0, trips = 0, full = 0;
            for (int i = 0; i < Buckets; i++)
            {
                mining += m_Mining[i];
                walking += m_Walking[i];
                waiting += m_Waiting[i];
                total += m_Total[i];
                trips += m_Trips[i];
                full += m_FullTrips[i];
            }
            if (total <= 0) return OutpostFlowLimit.Unknown;
            if (waiting * 5 >= total)
            {
                percent = Round5(waiting, total);
                return OutpostFlowLimit.Queue;
            }
            if (trips >= 3 && full * 5 >= trips * 3)
            {
                percent = Round5(full, trips);
                return OutpostFlowLimit.FullBags;
            }
            int top = mining >= walking ? mining : walking;
            if (top * 20 >= total * 9)
            {
                percent = Round5(top, total);
                return mining >= walking ? OutpostFlowLimit.Mining : OutpostFlowLimit.Walking;
            }
            return OutpostFlowLimit.Balanced;
        }

        static int Round5(int part, int whole) => whole <= 0 ? 0 : (part * 100 / whole + 2) / 5 * 5;
    }
}
