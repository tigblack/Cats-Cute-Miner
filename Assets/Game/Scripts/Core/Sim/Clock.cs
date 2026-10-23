using System;

namespace CatMiners.Core.Sim
{
    /// <summary>
    /// Часы, внедряемые в симуляцию (ТЗ 15.2): UTC для офлайн-метки и монотонное время сеанса для кадрового dt.
    /// Ядро само часы не читает — время приходит параметрами команд и Advance; реализация для Unity живёт в Runtime.
    /// </summary>
    public interface IClock
    {
        /// <summary>Текущее время UTC, миллисекунды Unix.</summary>
        long UtcNowMs { get; }

        /// <summary>Монотонное время сеанса, секунды (не зависит от перевода системных часов).</summary>
        double MonotonicSeconds { get; }
    }

    /// <summary>
    /// Ручные часы для тестов и сценарного бота (ТЗ 15.2): UTC-часы идут вместе с симуляционным временем.
    /// </summary>
    public sealed class ManualClock : IClock
    {
        public long UtcNowMs { get; set; }
        public double MonotonicSeconds { get; set; }

        public ManualClock() { }

        public ManualClock(long utcNowMs, double monotonicSeconds = 0.0)
        {
            UtcNowMs = utcNowMs;
            MonotonicSeconds = monotonicSeconds;
        }

        /// <summary>Сдвинуть оба показания на seconds (UTC — в миллисекундах, к ближайшему целому).</summary>
        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            UtcNowMs += (long)Math.Round(seconds * 1000.0);
            MonotonicSeconds += seconds;
        }
    }
}
