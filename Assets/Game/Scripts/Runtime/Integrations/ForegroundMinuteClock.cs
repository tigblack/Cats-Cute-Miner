using System;

namespace CatMiners.Runtime.Integrations
{
    // Samples only adjacent eligible foreground gameplay intervals. Pauses retain the fraction.
    public sealed class ForegroundMinuteClock
    {
        double last = -1, remainder;
        bool wasPlaying;
        public int Sample(double now, bool playing)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return 0;
            int minutes = 0;
            if (last >= 0 && wasPlaying && playing && now >= last)
            {
                remainder += now - last;
                minutes = (int)Math.Min(int.MaxValue, Math.Floor(remainder / 60));
                remainder -= minutes * 60d;
            }
            last = now;
            wasPlaying = playing;
            return minutes;
        }
        public void Revoke()
        {
            remainder = 0;
            wasPlaying = false;
        }
    }
}
