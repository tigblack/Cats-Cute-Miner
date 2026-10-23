// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;

namespace CatMiners.Core
{
    /// <summary>
    /// Сериализуемый ГСЧ PCG32 (ТЗ 15.2). UnityEngine.Random и System.Random для экономики не используются.
    /// </summary>
    public sealed class Pcg32
    {
        public ulong State;
        public ulong Inc;

        public Pcg32() { }

        public Pcg32(ulong seed, ulong stream)
        {
            State = 0UL;
            Inc = (stream << 1) | 1UL;
            NextUInt();
            State += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = State;
            State = unchecked(old * 6364136223846793005UL + Inc);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Равномерно в [0, 1).</summary>
        public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

        /// <summary>Равномерно в [0, maxExclusive), без смещения.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)((0x100000000UL - bound) % bound);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public Pcg32 Clone() => new Pcg32 { State = State, Inc = Inc };
    }

    /// <summary>Отдельные потоки ГСЧ для независимых систем, чтобы они не влияли друг на друга.</summary>
    public sealed class RngStreams
    {
        public Pcg32 Gacha;
        public Pcg32 Delivery;
        public Pcg32 Crit;
        public Pcg32 Timers;

        public static RngStreams Create(ulong seed)
        {
            return new RngStreams
            {
                Gacha = new Pcg32(seed, 1),
                Delivery = new Pcg32(seed, 2),
                Crit = new Pcg32(seed, 3),
                Timers = new Pcg32(seed, 4),
            };
        }
    }
}
