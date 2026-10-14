using System.Collections.Generic;

namespace CatMiners.Core.Sim
{
    public enum SimEventType
    {
        CatSpawned,
        CatMerged,
        CatMoved,
        CatAssigned,
        CatIdle,
        TargetHit,
        RockDestroyed,
        ShaftCleared,
        MineShaftActivated,
        ShaftAutomated,
        PayoutReady,
        PayoutCollected,
        GeneratorPaid,
        GeneratorUpgraded,
        ThresholdReached,
        GateBroken,
        CheckpointOpened,
        ExitBroken,
        MineCompleted,
        CampaignCompleted,
        ChestOpened,
        ContainerSpawned,
        ContainerOpened,
        DeliverySkippedAd,
        DynamiteUsed,
        CardUpgraded,
        RewardGranted,
        ExcludedReward,
        ZoneEntered,
        CommandRejected,
        /// <summary>Награда камня выпала добычей (<see cref="GameSession.LootPickups"/>): CatId — ID добычи, Cell, ObjectIndex — камень, Amount, Text — валюта.</summary>
        LootDropped,
        /// <summary>Добыча собрана: CatId — ID добычи, Cell, ObjectIndex — камень, Amount, Text — валюта, OtherCatId = 1 — автосбор.</summary>
        LootCollected,
    }

    /// <summary>Событие симуляции для UI и будущих систем (ТЗ 16). Публикуется после фиксации транзакции.</summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public int Zone;
        public long OpId;
        public int ObjectIndex;   // линейный индекс якоря объекта или −1
        public int CatId;
        public int OtherCatId;
        public int Cell;
        public GameNumber Amount;
        public string Text;
        public bool Crit;

        public override string ToString() => Type + " zone=" + Zone + " obj=" + ObjectIndex + " cat=" + CatId + " amount=" + Amount + (Text != null ? " " + Text : "");
    }

    /// <summary>
    /// Точка расширения ТЗ 16: будущие награды города подключаются отдельным обработчиком завершения зоны.
    /// В ядре обработчиков нет; вызывается после фиксации транзакции CompleteZone.
    /// </summary>
    public interface IZoneCompletionHandler
    {
        /// <param name="zone">Номер завершённой зоны.</param>
        /// <param name="opId">Уникальный ID операции CompleteZone (ключ идемпотентности).</param>
        void OnZoneCompleted(GameSession session, int zone, long opId);
    }

    /// <summary>
    /// Раздача опубликованных событий подписчикам (ТЗ 16). Потребитель передаёт сюда пачку событий после транзакции;
    /// обработчики завершения зоны вызываются один раз на событие MineCompleted, повтор той же операции (тот же OpId)
    /// отсеивается — помнятся последние <see cref="RememberedOps"/> операций.
    /// </summary>
    public sealed class SimEventBus
    {
        public const int RememberedOps = 512;

        readonly List<IZoneCompletionHandler> m_ZoneHandlers = new List<IZoneCompletionHandler>();
        readonly HashSet<long> m_Seen = new HashSet<long>();
        readonly Queue<long> m_SeenOrder = new Queue<long>();

        public int ZoneHandlerCount => m_ZoneHandlers.Count;

        public void Register(IZoneCompletionHandler handler)
        {
            if (handler == null || m_ZoneHandlers.Contains(handler)) return;
            m_ZoneHandlers.Add(handler);
        }

        public void Unregister(IZoneCompletionHandler handler)
        {
            if (handler != null) m_ZoneHandlers.Remove(handler);
        }

        /// <summary>Раздать пачку событий. Порядок обработчиков — порядок регистрации.</summary>
        public void Dispatch(GameSession session, IReadOnlyList<SimEvent> batch)
        {
            if (batch == null) return;
            for (int i = 0; i < batch.Count; i++)
            {
                var e = batch[i];
                if (e.Type != SimEventType.MineCompleted) continue;
                if (!Remember(e.OpId)) continue;
                if (m_ZoneHandlers.Count == 0) continue;
                // Копия: обработчик может отписаться во время вызова.
                var handlers = m_ZoneHandlers.ToArray();
                foreach (var h in handlers) h.OnZoneCompleted(session, e.Zone, e.OpId);
            }
        }

        /// <summary>true — операция встречена впервые.</summary>
        bool Remember(long opId)
        {
            if (!m_Seen.Add(opId)) return false;
            m_SeenOrder.Enqueue(opId);
            while (m_SeenOrder.Count > RememberedOps) m_Seen.Remove(m_SeenOrder.Dequeue());
            return true;
        }
    }
}
