using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Атмосфера шахты (Docs/Audio/SOUNDS.md, «Атмосфера»): бесшовный фон пещеры amb_bed и редкие события раз в 5–14 с —
    /// капли, скрип крепи, осыпь камешков, далёкий механизм (Tools/Audio/ambience.py). События — со случайной панорамой
    /// и громкостью, одно и то же подряд не повторяется. Громкость — группа Ambience (<see cref="AudioMix"/>): атмосфера
    /// остаётся фоном под музыкой и звуками.
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        const float BedVolume = 0.8f;
        const float MinGap = 5f, MaxGap = 14f;
        // Тип события: префикс файлов, число вариантов, вес, громкость (доля).
        static readonly (string Name, int Count, float Weight, float Volume)[] s_Events =
        {
            ("Audio/amb_drip_", 4, 0.45f, 0.7f),
            ("Audio/amb_creak_", 3, 0.25f, 0.55f),
            ("Audio/amb_pebbles_", 2, 0.15f, 0.5f),
            ("Audio/amb_machine_", 2, 0.15f, 0.55f),
        };

        AudioSource m_Bed;
        readonly AudioSource[] m_Shots = new AudioSource[2];
        AudioClip[][] m_Clips;
        float m_Next;
        int m_LastType = -1, m_LastVariant = -1, m_Shot;

        public static Ambience Create(Transform parent)
        {
            var go = new GameObject("Ambience");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<Ambience>();
            a.m_Bed = a.NewSource(true);
            for (int i = 0; i < a.m_Shots.Length; i++) a.m_Shots[i] = a.NewSource(false);
            var bed = Resources.Load<AudioClip>("Audio/amb_bed");
            if (bed != null)
            {
                a.m_Bed.clip = bed;
                a.m_Bed.Play();
            }
            a.m_Clips = new AudioClip[s_Events.Length][];
            for (int t = 0; t < s_Events.Length; t++)
            {
                a.m_Clips[t] = new AudioClip[s_Events[t].Count];
                for (int v = 0; v < s_Events[t].Count; v++) a.m_Clips[t][v] = Resources.Load<AudioClip>(s_Events[t].Name + v);
            }
            a.m_Next = Time.unscaledTime + Random.Range(MinGap, MaxGap);
            AudioMix.Changed += a.ApplyVolume;
            a.ApplyVolume();
            return a;
        }

        AudioSource NewSource(bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.priority = 200;
            return src;
        }

        void OnDestroy() => AudioMix.Changed -= ApplyVolume;

        void ApplyVolume()
        {
            if (m_Bed != null) m_Bed.volume = AudioMix.AmbienceGain * BedVolume;
        }

        void Update()
        {
            if (AudioListener.pause || Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + Random.Range(MinGap, MaxGap);
            float gain = AudioMix.AmbienceGain;
            if (gain <= 0f) return;
            // Тип по весу, без повтора того же варианта подряд.
            float roll = Random.value, acc = 0f;
            int type = 0;
            for (int t = 0; t < s_Events.Length; t++)
            {
                acc += s_Events[t].Weight;
                if (roll <= acc)
                {
                    type = t;
                    break;
                }
            }
            var clips = m_Clips[type];
            int variant = Random.Range(0, clips.Length);
            if (type == m_LastType && variant == m_LastVariant) variant = (variant + 1) % clips.Length;
            var clip = clips[variant];
            if (clip == null) return;
            m_LastType = type;
            m_LastVariant = variant;
            var src = m_Shots[m_Shot];
            m_Shot = (m_Shot + 1) % m_Shots.Length;
            src.clip = clip;
            src.panStereo = Random.Range(-0.75f, 0.75f);
            src.pitch = Random.Range(0.94f, 1.06f);
            src.volume = gain * s_Events[type].Volume * Random.Range(0.75f, 1f);
            src.Play();
        }
    }
}
