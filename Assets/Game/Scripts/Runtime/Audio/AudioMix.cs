using System;
using CatMiners.Core.Sim;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Группы громкости Master, Music, SFX, Ambience (Docs/Audio/SOUNDS.md, «Группы»): итоговая громкость источника =
    /// громкость звука × группа × Master, при выключенном звуке — 0. Значения приходят из настроек игрока в сохранении
    /// (PlayerSettingsState) — <see cref="Apply"/> при выборе сессии и при каждом изменении в окне настроек.
    /// Группы сделаны кодом, а не ассетом AudioMixer: их меняют только эти четыре числа, эффекты микшера не нужны.
    /// </summary>
    public static class AudioMix
    {
        public static float Master { get; private set; } = 1f;
        public static float Music { get; private set; } = 0.6f;
        public static float Sfx { get; private set; } = 1f;
        public static float Ambience { get; private set; } = 0.7f;
        public static bool Muted { get; private set; }

        /// <summary>Громкость изменилась (плеер музыки и атмосфера пересчитывают свои источники).</summary>
        public static event Action Changed;

        public static float MusicGain => Muted ? 0f : Master * Music;
        public static float SfxGain => Muted ? 0f : Master * Sfx;
        public static float AmbienceGain => Muted ? 0f : Master * Ambience;

        public static void Apply(PlayerSettingsState s)
        {
            if (s == null) s = new PlayerSettingsState();
            Set(!s.SoundOn, s.MasterVolume, s.MusicVolume, s.SfxVolume, s.AmbienceVolume);
        }

        public static void Set(bool muted, float master, float music, float sfx, float ambience)
        {
            master = Clamp(master);
            music = Clamp(music);
            sfx = Clamp(sfx);
            ambience = Clamp(ambience);
            if (muted == Muted && master == Master && music == Music && sfx == Sfx && ambience == Ambience) return;
            Muted = muted;
            Master = master;
            Music = music;
            Sfx = sfx;
            Ambience = ambience;
            Changed?.Invoke();
        }

        public static float Get(AudioGroup group)
        {
            switch (group)
            {
                case AudioGroup.Music: return Music;
                case AudioGroup.Sfx: return Sfx;
                case AudioGroup.Ambience: return Ambience;
                default: return Master;
            }
        }

        static float Clamp(float v) => float.IsNaN(v) ? 1f : Mathf.Clamp01(v);
    }
}
