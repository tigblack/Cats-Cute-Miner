using System;
using CatMiners.Core.Sim;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Часы игры для Unity (ТЗ 15.2): UTC — системное время для офлайн-метки (ТЗ 12.3),
    /// монотонное время сеанса — Time.realtimeSinceStartupAsDouble для кадрового dt (не зависит от перевода системных часов).
    /// Тесты подменяют часы через <see cref="GameController.ClockOverride"/> (например, <see cref="ManualClock"/>).
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public double MonotonicSeconds => Time.realtimeSinceStartupAsDouble;
    }
}
