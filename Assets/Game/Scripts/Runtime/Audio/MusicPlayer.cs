using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Фоновая музыка (Docs/Audio/SOUNDS.md, «Музыка»): два оригинальных трека-цикла — «Тёплая штольня» (music_warm)
    /// и «Кристальная глубина» (music_crystal), Tools/Audio/music.py. Трек выбирает оформление шахты (<see cref="SetPreferred"/>);
    /// чтобы долгая сессия не утомляла, после двух циклов своего трека звучит один цикл другого. Смена — плавная
    /// перекрёстная (два источника, 3 с). Окна и перестройка поля музыку не перезапускают: плеер живёт всю игру.
    /// Громкость — группа Music (<see cref="AudioMix"/>); на паузе приложения звук приостанавливает GameController.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        public const int Warm = 0, Crystal = 1;
        static readonly string[] s_Names = { "Audio/music_warm", "Audio/music_crystal" };
        const float CrossfadeSeconds = 3f;
        const float FirstFadeSeconds = 2f;
        /// <summary>Циклов своего трека до одного цикла другого.</summary>
        const int OwnLoops = 2;

        readonly AudioClip[] m_Clips = new AudioClip[2];
        AudioSource m_A, m_B;          // m_A — текущий, m_B — уходящий
        float m_FadeA = 0f, m_FadeB = 0f;
        float m_FadeSpeed = 1f / FirstFadeSeconds;
        int m_Current = -1;
        int m_Preferred = Warm;
        float m_Played;                // секунд текущего трека с момента старта
        bool m_Guest;                  // сейчас звучит «чужой» трек — один цикл

        /// <summary>Индекс звучащего трека (−1 — тишина: файлов нет).</summary>
        public int Current => m_Current;

        public static MusicPlayer Create(Transform parent)
        {
            var go = new GameObject("Music");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<MusicPlayer>();
            m.m_A = m.NewSource();
            m.m_B = m.NewSource();
            for (int i = 0; i < s_Names.Length; i++) m.m_Clips[i] = Resources.Load<AudioClip>(s_Names[i]);
            AudioMix.Changed += m.ApplyVolume;
            return m;
        }

        AudioSource NewSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.priority = 0;
            src.volume = 0f;
            return src;
        }

        void OnDestroy() => AudioMix.Changed -= ApplyVolume;

        /// <summary>Трек оформления шахты: при смене — плавный переход; тот же трек продолжает играть с того же места.</summary>
        public void SetPreferred(int track)
        {
            m_Preferred = Mathf.Clamp(track, 0, s_Names.Length - 1);
            if (m_Current == m_Preferred)
            {
                // Гостевой трек стал своим (сменилось оформление): продолжает играть, счёт циклов — заново.
                if (m_Guest)
                {
                    m_Guest = false;
                    m_Played = 0f;
                }
                return;
            }
            if (m_Current >= 0 && m_Guest) return;   // гостевой цикл доиграет и вернёт свой трек
            CrossfadeTo(m_Preferred, false);
        }

        void CrossfadeTo(int track, bool guest)
        {
            var clip = m_Clips[track];
            if (clip == null) return;
            // Уходящий источник становится вторым и гаснет; новый трек стартует с начала цикла и разгорается.
            var old = m_A;
            m_A = m_B;
            m_B = old;
            m_FadeB = m_FadeA;
            m_FadeA = 0f;
            m_FadeSpeed = 1f / (m_Current < 0 ? FirstFadeSeconds : CrossfadeSeconds);
            m_A.clip = clip;
            m_A.time = 0f;
            m_A.Play();
            m_Current = track;
            m_Guest = guest;
            m_Played = 0f;
            ApplyVolume();
        }

        void Update()
        {
            if (m_Current < 0) return;
            float dt = Time.unscaledDeltaTime;
            if (m_FadeA < 1f || m_FadeB > 0f)
            {
                m_FadeA = Mathf.MoveTowards(m_FadeA, 1f, m_FadeSpeed * dt);
                m_FadeB = Mathf.MoveTowards(m_FadeB, 0f, m_FadeSpeed * dt);
                if (m_FadeB <= 0f && m_B.isPlaying) m_B.Stop();
                ApplyVolume();
            }
            if (AudioListener.pause) return;
            m_Played += dt;
            var clip = m_Clips[m_Current];
            if (clip == null) return;
            float loop = clip.length;
            // Переход начинается за CrossfadeSeconds до конца цикла: новый трек вступает, пока старый договаривает фразу.
            if (!m_Guest && m_Played >= OwnLoops * loop - CrossfadeSeconds)
            {
                int other = 1 - m_Current;
                if (m_Clips[other] != null) CrossfadeTo(other, true);
                else m_Played = 0f;
            }
            else if (m_Guest && m_Played >= loop - CrossfadeSeconds) CrossfadeTo(m_Preferred, false);
        }

        void ApplyVolume()
        {
            float g = AudioMix.MusicGain;
            if (m_A != null) m_A.volume = g * m_FadeA;
            if (m_B != null) m_B.volume = g * m_FadeB;
        }
    }
}
