using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>Presentation-only cues. Frequent logistics have dedicated, bounded voices.</summary>
    public sealed class OutpostAudio
    {
        readonly Sfx m_Sfx;
        readonly AudioSource m_Step;
        readonly AudioSource[] m_Deposit = new AudioSource[2];
        readonly AudioSource m_Logistics;
        readonly AudioClip m_Coin, m_Click;
        float m_NextStep, m_NextDeposit, m_NextLogistics;
        public OutpostAudio(Sfx sfx, Transform parent)
        {
            m_Sfx = sfx;
            var go = new GameObject("OutpostLogisticsAudio");
            go.transform.SetParent(parent, false);
            m_Step = Voice(go);
            m_Logistics = Voice(go);
            for (int i = 0; i < m_Deposit.Length; i++) m_Deposit[i] = Voice(go);
            m_Coin = Resources.Load<AudioClip>("Audio/coin_0") ?? Resources.Load<AudioClip>("Audio/coin");
            m_Click = Resources.Load<AudioClip>("Audio/click_0") ?? Resources.Load<AudioClip>("Audio/click");
            m_Coin?.LoadAudioData(); m_Click?.LoadAudioData();
        }
        static AudioSource Voice(GameObject go)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0;
            return source;
        }
        static void PlayVoice(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (clip == null || AudioMix.SfxGain <= 0 || AudioListener.pause) return;
            source.Stop(); source.clip = clip; source.volume = volume * AudioMix.SfxGain; source.pitch = pitch; source.Play();
        }
        public void Play(OutpostViewCueArgs args)
        {
            if (AudioListener.pause || AudioMix.SfxGain <= 0) return;
            float now = Time.unscaledTime;
            switch (args.Cue)
            {
                case OutpostViewCue.Footstep:
                    if (now < m_NextStep) return;
                    m_NextStep = now + 0.25f;
                    PlayVoice(m_Step, m_Click, 0.045f, 0.65f); break;
                case OutpostViewCue.Deposit:
                    if (now < m_NextDeposit) return;
                    m_NextDeposit = now + 0.12f;
                    int slot = Mathf.Clamp(args.Slot, 0, 1);
                    float pitch = 0.95f + Mathf.Min(0.2f, (float)System.Numerics.BigInteger.Log(System.Numerics.BigInteger.Max(1, args.Value), 10) * 0.03f);
                    PlayVoice(m_Deposit[slot], m_Coin, 0.28f, pitch); break;
                case OutpostViewCue.Hit: m_Sfx.Hit(args.Material); break;
                case OutpostViewCue.PlayerStrike: m_Sfx.PlayerStrike(args.Material); break;
                case OutpostViewCue.TargetBroken: m_Sfx.Break(); break;
                case OutpostViewCue.SectorOpened: m_Sfx.GateOpen(); break;
                case OutpostViewCue.TeamGathered: m_Sfx.ExitOpen(); break;
                case OutpostViewCue.WorkerSpawned: m_Sfx.Meow(); break;
                case OutpostViewCue.Upgrade: m_Sfx.Upgrade(); break;
                case OutpostViewCue.Equip: m_Sfx.Chime(); break;
                default:
                    if (now < m_NextLogistics) return;
                    m_NextLogistics = now + 0.2f;
                    PlayVoice(m_Logistics, m_Coin, 0.1f, args.Cue == OutpostViewCue.BagFull ? 0.7f : 1.15f); break;
            }
        }
    }
}
