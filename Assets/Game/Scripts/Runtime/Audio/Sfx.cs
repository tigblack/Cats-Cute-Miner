using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Звуки игры (ТЗ 14): записанные сэмплы CC0 из <c>Resources/Audio/&lt;имя&gt;_&lt;n&gt;</c>, список, источники и громкости —
    /// Docs/Audio/SOUNDS.md. Файла нет — запасной звук синтезируется один раз при создании: мягкие щипки «маримбы/колокольчика»
    /// на пентатонике и глухие удары, без шумовых всплесков.
    /// Частые звуки (удары, монеты) тихие, с несколькими вариантами, разбросом высоты и минимальным интервалом,
    /// чтобы удары многих работников не превращались в шум. События офлайна не озвучиваются (их итог — окно «Пока вас не было»).
    /// Все клипы загружаются в память при создании, а не первым запуском в игре.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        /// <summary>Голосов одновременно: удары, награда, мяу и джингл звучат вместе без обрывов.</summary>
        const int Voices = 6;
        const int SynthRate = 22050;

        /// <summary>Приоритет при нехватке голосов: частый звук не обрывает награду или джингл.</summary>
        const int PrioFrequent = 0, PrioNormal = 1, PrioJingle = 2;

        /// <summary>Один звук: варианты, громкость, разброс высоты, минимальный интервал между запусками.</summary>
        sealed class Cue
        {
            public AudioClip[] Clips;
            public float Volume, PitchJitter, MinInterval;
            /// <summary>Базовая высота: звук-вариант на чужих клипах (падение добычи, «поп» бочки) звучит иначе оригинала.</summary>
            public float Pitch = 1f;
            public int Priority;
            public float Last = -100f;
            public int LastVariant = -1;
            /// <summary>Голос и кадр последнего запуска: джингл того же кадра снимает награду, начатую раньше него.</summary>
            public int Voice = -1, Frame = -1;
        }

        AudioSource[] m_Voices;
        int[] m_VoicePriority;
        int m_NextVoice, m_LastVoice = -1;
        bool m_Enabled = true;
        /// <summary>
        /// До этого момента звучит джингл уровня <see cref="m_JingleLevel"/> (1 — запуск шахты, порог; 2 — ворота, выход):
        /// звуки наград (улучшение, сундук) и джинглы не важнее него не накладываются. Награда, запущенная в том же кадре
        /// раньше джингла (события пачки идут в любом порядке), снимается в <see cref="PlayJingle"/>.
        /// </summary>
        float m_JingleUntil;
        int m_JingleLevel;
        Action m_UiClick, m_UiOpen, m_UiClose, m_UiDenied;

        Cue m_Hit, m_Break, m_Coin, m_Chime, m_Payout, m_Meow, m_Merge, m_Upgrade, m_Unlock, m_Chest, m_Gate, m_Exit;
        Cue m_Click, m_Open, m_Close, m_Denied, m_Dynamite, m_Cannon, m_Land;
        Cue m_Select, m_HitCrystal, m_HitWood, m_HitMetal;
        /// <summary>Удары оружия по индексу CatLook.Weapons (у кирки — звук материала цели, элемент 0 не используется).</summary>
        readonly Cue[] m_Weapon = new Cue[8];
        Cue m_LootDrop;
        /// <summary>Удар кирки игрока по материалам: громче удара котика и с коротким интервалом — каждый тап слышен.</summary>
        Cue m_StrikeStone, m_StrikeCrystal, m_StrikeWood, m_StrikeMetal;
        /// <summary>Заводы ТЗ 2.2 §11 (Docs/Audio/SOUNDS.md, «Заводы»): цепи, готовность, труба, склад, рынок.</summary>
        Cue m_HitChain, m_StrikeChain, m_ChainBreak, m_FactoryReady, m_PipeSend, m_PipeSendAuto, m_GoodsArrive, m_GoodsSell;

        /// <summary>Звук включён (настройка игрока). Выключение глушит то, что уже звучит.</summary>
        public bool Enabled
        {
            get => m_Enabled;
            set
            {
                m_Enabled = value;
                if (value || m_Voices == null) return;
                for (int i = 0; i < m_Voices.Length; i++)
                    if (m_Voices[i] != null) m_Voices[i].Stop();
            }
        }

        public static Sfx Create(Transform parent)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Sfx>();
            s.m_Voices = new AudioSource[Voices];
            s.m_VoicePriority = new int[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                s.m_Voices[i] = src;
            }

            // Громкости — микс при файлах одинаковой громкости (build_sfx.py нормирует по RMS): частое тише наград.
            s.m_Hit = Load("hit", 0.22f, 0.04f, 0.1f, PrioFrequent);
            s.m_Break = Load("break", 0.42f, 0.05f, 0.08f, PrioFrequent);
            s.m_Coin = Load("coin", 0.3f, 0.03f, 0.09f, PrioFrequent);
            s.m_Chime = Load("chime", 0.3f, 0.03f, 0.12f, PrioFrequent);
            s.m_Payout = Load("payout", 0.5f, 0.03f, 0.15f, PrioNormal);
            s.m_Meow = Load("meow", 0.5f, 0.05f, 0.15f, PrioNormal);
            s.m_Merge = Load("merge", 0.6f, 0.02f, 0.1f, PrioNormal);
            s.m_Upgrade = Load("upgrade", 0.5f, 0f, 0.12f, PrioNormal);
            s.m_Unlock = Load("unlock", 0.55f, 0f, 0.5f, PrioJingle);
            s.m_Chest = Load("chest", 0.55f, 0f, 0.5f, PrioNormal);
            s.m_Gate = Load("gate", 0.65f, 0f, 0.5f, PrioJingle);
            s.m_Exit = Load("exit", 0.7f, 0f, 0.5f, PrioJingle);
            s.m_Click = Load("click", 0.35f, 0.02f, 0.05f, PrioNormal);
            s.m_Open = Load("open", 0.3f, 0.02f, 0.08f, PrioNormal);
            s.m_Close = Load("close", 0.28f, 0.02f, 0.08f, PrioNormal);
            s.m_Denied = Load("denied", 0.38f, 0f, 0.2f, PrioNormal);
            s.m_Dynamite = Load("dynamite", 0.6f, 0.03f, 0.15f, PrioNormal);
            s.m_Cannon = Load("cannon", 0.5f, 0.04f, 0.1f, PrioNormal);
            // Выбор и удары по материалам (кристаллы, дерево, металл) — как удар по камню: тихо, с разбросом и интервалом.
            s.m_Select = Load("select", 0.28f, 0.03f, 0.06f, PrioNormal);
            s.m_HitCrystal = Load("hit_crystal", 0.2f, 0.04f, 0.1f, PrioFrequent);
            s.m_HitWood = Load("hit_wood", 0.24f, 0.04f, 0.1f, PrioFrequent);
            s.m_HitMetal = Load("hit_metal", 0.2f, 0.04f, 0.1f, PrioFrequent);
            // Оружие уровня (CatLook): молот, дробилка, пила, бур, две кирки, бензопила, лазер. Моторные инструменты тише
            // и реже: их звук длиннее удара кирки.
            s.m_Weapon[CatLook.Hammer] = Load("weapon_hammer", 0.26f, 0.04f, 0.1f, PrioFrequent);
            s.m_Weapon[CatLook.Crusher] = Load("weapon_crusher", 0.24f, 0.04f, 0.12f, PrioFrequent);
            s.m_Weapon[CatLook.Saw] = Load("weapon_saw", 0.17f, 0.03f, 0.14f, PrioFrequent);
            s.m_Weapon[CatLook.Drill] = Load("weapon_drill", 0.17f, 0.03f, 0.14f, PrioFrequent);
            s.m_Weapon[CatLook.DualPick] = Load("weapon_dualpick", 0.22f, 0.04f, 0.1f, PrioFrequent);
            s.m_Weapon[CatLook.Chainsaw] = Load("weapon_chainsaw", 0.15f, 0.03f, 0.16f, PrioFrequent);
            s.m_Weapon[CatLook.Laser] = Load("weapon_laser", 0.17f, 0.04f, 0.12f, PrioFrequent);
            s.m_Land = Load("land", 0.42f, 0.05f, 0.08f, PrioNormal);
            // Вариант на уже загруженных клипах (без новых файлов): добыча упала на пол — тихий низкий звон монеты.
            s.m_LootDrop = Variant(s.m_Coin, 0.14f, 0.8f, 0.06f, 0.1f, PrioFrequent);
            // Кирка игрока (тап по камню): те же клипы удара, ниже и громче, не чаще раза в 0,06 с.
            s.m_StrikeStone = Variant(s.m_Hit, 0.34f, 0.9f, 0.05f, 0.06f, PrioNormal);
            s.m_StrikeCrystal = Variant(s.m_HitCrystal, 0.3f, 0.92f, 0.05f, 0.06f, PrioNormal);
            s.m_StrikeWood = Variant(s.m_HitWood, 0.34f, 0.9f, 0.05f, 0.06f, PrioNormal);
            s.m_StrikeMetal = Variant(s.m_HitMetal, 0.3f, 0.92f, 0.05f, 0.06f, PrioNormal);
            // Заводы (ТЗ 2.2 §11): громкость, разброс, интервал и приоритет — по таблице SOUNDS.md «Заводы». Удар игрока по цепям —
            // те же клипы громче и с коротким интервалом (как кирка по камню); автодоставка — вдвое тише ручной отправки.
            s.m_HitChain = Load("hit_chain", 0.2f, 0.04f, 0.1f, PrioFrequent);
            s.m_StrikeChain = Variant(s.m_HitChain, 0.3f, 0.92f, 0.05f, 0.06f, PrioNormal);
            s.m_ChainBreak = Load("chain_break", 0.45f, 0.03f, 0.15f, PrioNormal);
            s.m_FactoryReady = Load("factory_ready", 0.22f, 0f, 0.5f, PrioNormal);
            s.m_PipeSend = Load("pipe_send", 0.26f, 0.04f, 0.12f, PrioFrequent);
            s.m_PipeSendAuto = Variant(s.m_PipeSend, 0.13f, 1f, 0.04f, 0.12f, PrioFrequent);
            s.m_GoodsArrive = Load("goods_arrive", 0.24f, 0.04f, 0.15f, PrioFrequent);
            s.m_GoodsSell = Load("goods_sell", 0.5f, 0.02f, 0.2f, PrioNormal);

            // Кит UI зовёт UiSound, не зная о Sfx; делегаты храним, чтобы снять ровно свои при уничтожении.
            UiSound.Click = s.m_UiClick = s.Click;
            UiSound.Open = s.m_UiOpen = s.Open;
            UiSound.Close = s.m_UiClose = s.Close;
            UiSound.Denied = s.m_UiDenied = s.Denied;
            return s;
        }

        void OnDestroy()
        {
            if (UiSound.Click == m_UiClick) UiSound.Click = null;
            if (UiSound.Open == m_UiOpen) UiSound.Open = null;
            if (UiSound.Close == m_UiClose) UiSound.Close = null;
            if (UiSound.Denied == m_UiDenied) UiSound.Denied = null;
        }

        // ───────────── События ─────────────

        /// <summary>Удар кирки по камню: тихо, не чаще раза в 0,1 с, случайный вариант и высота ±4 %.</summary>
        public void Hit() => Play(m_Hit);

        /// <summary>
        /// Удар кирки по материалу цели: камень, кристаллы (самоцветы, эликсир), дерево (ворота, выход), металл (сундук в породе,
        /// жетон). Удары разных материалов звучат вместе, но каждый — не чаще своего интервала.
        /// </summary>
        public void Hit(HitMaterial material)
        {
            switch (material)
            {
                case HitMaterial.Crystal: Play(m_HitCrystal); break;
                case HitMaterial.Wood: Play(m_HitWood); break;
                case HitMaterial.Metal: Play(m_HitMetal); break;
                default: Play(m_Hit); break;
            }
        }

        /// <summary>Удар кирки игрока по материалу цели (тап/клик по камню, ТЗ 6.6).</summary>
        public void PlayerStrike(HitMaterial material)
        {
            switch (material)
            {
                case HitMaterial.Crystal: Play(m_StrikeCrystal); break;
                case HitMaterial.Wood: Play(m_StrikeWood); break;
                case HitMaterial.Metal: Play(m_StrikeMetal); break;
                default: Play(m_StrikeStone); break;
            }
        }

        /// <summary>
        /// Удар оружием котика (<see cref="CatLook.Weapon"/>): кирка звучит по материалу цели (<see cref="Hit(HitMaterial)"/>),
        /// остальное оружие — своим звуком: молот, дробилка, циркулярная пила, бур, две кирки, бензопила, лазер.
        /// </summary>
        public void Hit(int weapon, HitMaterial material)
        {
            if (weapon <= CatLook.Pick || weapon >= m_Weapon.Length || m_Weapon[weapon] == null) Hit(material);
            else Play(m_Weapon[weapon]);
        }

        /// <summary>Выбор котика или объекта на поле: короткий мягкий «тик».</summary>
        public void Select() => Play(m_Select);

        // ───────────── Заводы (ТЗ 2.2 §11) ─────────────

        /// <summary>Удар котика по цепям закрытого завода: металл со звяканьем звеньев, не чаще раза в 0,1 с.</summary>
        public void HitChain() => Play(m_HitChain);
        /// <summary>Удар кирки игрока по цепям: те же клипы ниже и громче, не чаще раза в 0,06 с.</summary>
        public void StrikeChain() => Play(m_StrikeChain);
        /// <summary>Цепи завода разорваны (FactoryUnlocked).</summary>
        public void ChainBreak() => Play(m_ChainBreak);
        /// <summary>Партия ждёт доставки (FactoryReady): тихий сигнал, не чаще раза в 0,5 с.</summary>
        public void FactoryReady() => Play(m_FactoryReady);
        /// <summary>Партия ушла в трубу (GoodsDispatched); <paramref name="automatic"/> — автодоставка, вдвое тише.</summary>
        public void PipeSend(bool automatic) => Play(automatic ? m_PipeSendAuto : m_PipeSend);
        /// <summary>Партия пришла на склад (GoodsArrived), не чаще раза в 0,15 с.</summary>
        public void GoodsArrive() => Play(m_GoodsArrive);
        /// <summary>Продажа на рынке (GoodsSold): «Sell all» по нескольким товарам звучит один раз (интервал 0,2 с).</summary>
        public void GoodsSell() => Play(m_GoodsSell);
        /// <summary>Камень (ворота, завал шахты) рассыпался.</summary>
        public void Break() => Play(m_Break);
        /// <summary>Награда золотом.</summary>
        public void Coin() => Play(m_Coin);
        /// <summary>Награда эликсиром или кристаллами — мягкий колокольчик, отличимый от монеты.</summary>
        public void Chime() => Play(m_Chime);
        /// <summary>Собрана выплата шахты: горсть монет.</summary>
        public void Payout() => Play(m_Payout);
        /// <summary>Появился котик.</summary>
        public void Meow() => Play(m_Meow);
        /// <summary>Объединение котиков.</summary>
        public void Merge() => Play(m_Merge);
        /// <summary>Улучшение здания или карты. Под джинглом (порог, автоматизация шахты) не звучит.</summary>
        public void Upgrade()
        {
            if (Time.unscaledTime < m_JingleUntil) return;
            Play(m_Upgrade);
        }
        /// <summary>Запуск шахты, автоматизация, порог уровня.</summary>
        public void Unlock() => PlayJingle(m_Unlock, 1);
        /// <summary>Получен сундук. Под джинглом (ворота, выход, автоматизация шахты) не звучит.</summary>
        public void ChestOpen()
        {
            if (Time.unscaledTime < m_JingleUntil) return;
            Play(m_Chest);
        }
        /// <summary>Открыты ворота сектора.</summary>
        public void GateOpen() => PlayJingle(m_Gate, 2);
        /// <summary>Пробит выход шахты, пройдена кампания.</summary>
        public void ExitOpen() => PlayJingle(m_Exit, 2);
        /// <summary>Взрыв динамита.</summary>
        public void Dynamite() => Play(m_Dynamite);
        /// <summary>Добыча выпала из камня: тихо и ниже обычной монеты, не чаще раза в 0,1 с (динамит роняет несколько куч сразу).</summary>
        public void LootDrop() => Play(m_LootDrop);
        /// <summary>Пушка у кузницы выстрелила бочкой с котиком: мягкий глухой «пумф».</summary>
        public void Cannon() => Play(m_Cannon);
        /// <summary>Бочка из пушки приземлилась: деревянный «тук».</summary>
        public void BarrelLand() => Play(m_Land);
        public void Click() => Play(m_Click);
        public void Open() => Play(m_Open);
        public void Close() => Play(m_Close);
        /// <summary>Действие недоступно; повтор в пределах 0,2 с (кнопка + отказ команды в одном кадре) не дублируется.</summary>
        public void Denied() => Play(m_Denied);

        void PlayJingle(Cue c, int level)
        {
            float now = Time.unscaledTime;
            if (now < m_JingleUntil && level <= m_JingleLevel) return;
            var clip = Play(c);
            if (clip == null) return;
            // Глушим только когда джингл действительно зазвучал: отброшенный интервалом или нехваткой голосов не оставит тишины.
            StopReward(m_Upgrade);
            StopReward(m_Chest);
            m_JingleUntil = now + clip.length * 0.8f;
            m_JingleLevel = level;
        }

        /// <summary>
        /// Награда, начатая в этом же кадре раньше джингла (сундук выхода перед концом кампании, улучшение карты или сундук,
        /// автоматизировавшие шахту), останавливается: Play и Stop в одном кадре не слышны. Голос, уже отданный другому звуку
        /// (в том числе самому джинглу), не трогаем.
        /// </summary>
        void StopReward(Cue c)
        {
            if (c == null || c.Frame != Time.frameCount || c.Voice < 0 || c.LastVariant < 0) return;
            var src = m_Voices[c.Voice];
            if (src != null && src.clip == c.Clips[c.LastVariant]) src.Stop();
            c.Frame = -1;
        }

        AudioClip Play(Cue c)
        {
            if (!m_Enabled || c == null || c.Clips == null || m_Voices == null) return null;
            float now = Time.unscaledTime;
            if (now - c.Last < c.MinInterval) return null;
            var src = PickVoice(c.Priority);
            if (src == null) return null;
            c.Last = now;
            int n = c.Clips.Length;
            int v = Random.Range(0, n);
            if (n > 1 && v == c.LastVariant) v = (v + 1 + Random.Range(0, n - 1)) % n;
            c.LastVariant = v;
            var clip = c.Clips[v];
            src.clip = clip;
            // Группа SFX и общая громкость (AudioMix): настройки игрока.
            src.volume = c.Volume * AudioMix.SfxGain;
            src.pitch = c.Pitch * (c.PitchJitter > 0f ? 1f + Random.Range(-c.PitchJitter, c.PitchJitter) : 1f);
            src.Play();
            c.Voice = m_LastVoice;
            c.Frame = Time.frameCount;
            return clip;
        }

        /// <summary>Свободный голос; если все заняты — самый старый не важнее нового звука (джингл не обрывается ударом).</summary>
        AudioSource PickVoice(int priority)
        {
            for (int k = 0; k < Voices; k++)
            {
                int i = (m_NextVoice + k) % Voices;
                var src = m_Voices[i];
                if (src != null && !src.isPlaying) return Take(i, priority);
            }
            for (int k = 0; k < Voices; k++)
            {
                int i = (m_NextVoice + k) % Voices;
                if (m_Voices[i] != null && m_VoicePriority[i] <= priority) return Take(i, priority);
            }
            return null;
        }

        AudioSource Take(int i, int priority)
        {
            m_NextVoice = (i + 1) % Voices;
            m_LastVoice = i;
            m_VoicePriority[i] = priority;
            return m_Voices[i];
        }

        // ───────────── Загрузка ─────────────

        /// <summary>Варианты <c>Audio/&lt;name&gt;_0..n</c> (или один <c>Audio/&lt;name&gt;</c>); нет ни одного — запасной синтез.</summary>
        static Cue Load(string name, float volume, float pitchJitter, float minInterval, int priority)
        {
            int count = 0;
            var found = new AudioClip[8];
            for (int i = 0; i < found.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + name + "_" + i);
                if (clip == null) break;
                found[count++] = clip;
            }
            if (count == 0)
            {
                var single = Resources.Load<AudioClip>("Audio/" + name);
                if (single != null) found[count++] = single;
            }
            AudioClip[] clips;
            if (count > 0)
            {
                clips = new AudioClip[count];
                Array.Copy(found, clips, count);
                // Импорт по умолчанию без Preload Audio Data: иначе первый Play каждого клипа грузит и декодирует его в игре.
                for (int i = 0; i < count; i++) clips[i].LoadAudioData();
            }
            else
            {
                Debug.LogWarning("Sfx: нет файла Resources/Audio/" + name + "_0, используется запасной синтез (Docs/Audio/SOUNDS.md).");
                clips = new[] { Fallback(name) };
            }
            return new Cue { Clips = clips, Volume = volume, PitchJitter = pitchJitter, MinInterval = minInterval, Priority = priority };
        }

        /// <summary>Звук на клипах другого (те же варианты в памяти): своя громкость, высота, интервал и приоритет.</summary>
        static Cue Variant(Cue source, float volume, float pitch, float pitchJitter, float minInterval, int priority)
        {
            return new Cue { Clips = source?.Clips, Volume = volume, Pitch = pitch, PitchJitter = pitchJitter, MinInterval = minInterval, Priority = priority };
        }

        // ───────────── Запасной синтез ─────────────
        // Ноты — пентатоника до мажор: любые сочетания звучат согласно. Тембры — затухающие частичные:
        // «маримба» (1 : 4, верх гаснет быстрее) и «колокольчик» (1 : 2,76 : 5,4). Удары — синус с падающей высотой.

        const float C4 = 261.63f, D4 = 293.66f, E4 = 329.63f, G4 = 392f, A4 = 440f;
        const float C5 = 523.25f, D5 = 587.33f, E5 = 659.25f, G5 = 783.99f, A5 = 880f;
        const float C6 = 1046.5f, E6 = 1318.5f, G6 = 1568f, A6 = 1760f;

        static readonly float[] Marimba = { 1f, 4f };
        static readonly float[] MarimbaGain = { 1f, 0.22f };
        static readonly float[] Bell = { 1f, 2.76f, 5.4f };
        static readonly float[] BellGain = { 1f, 0.3f, 0.1f };

        static AudioClip Fallback(string name)
        {
            switch (name)
            {
                case "hit": return Clip(name, Notes(0.12f, new[] { E4 }, 0f, 0.03f, Marimba, MarimbaGain, 0.5f));
                case "break":
                {
                    var d = Thump(0.3f, 170f, 70f, 0.12f, 0.6f);
                    // «Камешки» — три коротких тихих щипка вразнобой по времени, не шум.
                    Add(d, Notes(0.3f, new[] { G5, D5, A5 }, 0.045f, 0.025f, Marimba, MarimbaGain, 0.18f), 0.02f);
                    return Clip(name, d);
                }
                case "coin": return Clip(name, Notes(0.3f, new[] { E6, A6 }, 0.07f, 0.09f, Bell, BellGain, 0.35f));
                case "chime": return Clip(name, Notes(0.5f, new[] { G5 }, 0f, 0.22f, Bell, BellGain, 0.4f));
                case "payout": return Clip(name, Notes(0.45f, new[] { E6, G6, A6 }, 0.06f, 0.1f, Bell, BellGain, 0.32f));
                case "meow": return Clip(name, Notes(0.3f, new[] { G5, C6 }, 0.08f, 0.08f, Marimba, MarimbaGain, 0.45f));
                case "merge": return Clip(name, Notes(0.6f, new[] { C5, E5, G5, C6 }, 0.06f, 0.18f, Bell, BellGain, 0.35f));
                case "upgrade": return Clip(name, Notes(0.45f, new[] { D5, G5 }, 0.09f, 0.15f, Marimba, MarimbaGain, 0.45f));
                case "unlock": return Clip(name, Notes(0.7f, new[] { C5, D5, E5, G5 }, 0.09f, 0.2f, Marimba, MarimbaGain, 0.4f));
                case "chest": return Clip(name, Notes(0.5f, new[] { E5, A5 }, 0.1f, 0.2f, Bell, BellGain, 0.38f));
                case "gate": return Clip(name, Notes(0.9f, new[] { C5, E5, G5, C6 }, 0.11f, 0.3f, Marimba, MarimbaGain, 0.4f));
                case "exit": return Clip(name, Notes(1f, new[] { G4, C5, E5, G5, C6 }, 0.1f, 0.3f, Bell, BellGain, 0.35f));
                case "click": return Clip(name, Notes(0.08f, new[] { C6 }, 0f, 0.02f, Marimba, MarimbaGain, 0.35f));
                case "open": return Clip(name, Notes(0.2f, new[] { C5, G5 }, 0.05f, 0.06f, Marimba, MarimbaGain, 0.35f));
                case "close": return Clip(name, Notes(0.2f, new[] { G5, C5 }, 0.05f, 0.06f, Marimba, MarimbaGain, 0.35f));
                case "denied": return Clip(name, Notes(0.25f, new[] { A4, D4 }, 0.11f, 0.05f, Marimba, MarimbaGain, 0.5f));
                case "dynamite":
                {
                    var d = Thump(0.5f, 130f, 45f, 0.18f, 0.8f);
                    Add(d, Notes(0.1f, new[] { C5 }, 0f, 0.03f, Marimba, MarimbaGain, 0.3f), 0f);
                    return Clip(name, d);
                }
                case "cannon":
                {
                    var d = Thump(0.35f, 160f, 55f, 0.11f, 0.75f);
                    Add(d, Notes(0.12f, new[] { G4 }, 0f, 0.04f, Marimba, MarimbaGain, 0.25f), 0f);
                    return Clip(name, d);
                }
                case "land":
                {
                    var d = Thump(0.18f, 240f, 110f, 0.05f, 0.6f);
                    Add(d, Notes(0.1f, new[] { D4 }, 0f, 0.03f, Marimba, MarimbaGain, 0.2f), 0.005f);
                    return Clip(name, d);
                }
                default: return Clip(name, Notes(0.15f, new[] { C5 }, 0f, 0.05f, Marimba, MarimbaGain, 0.4f));
            }
        }

        /// <summary>Последовательность щипков: каждая нота — сумма затухающих частичных, верхние гаснут быстрее; атака 3 мс без щелчка.</summary>
        static float[] Notes(float duration, float[] notes, float step, float decay, float[] ratios, float[] gains, float volume)
        {
            int n = Mathf.CeilToInt(duration * SynthRate);
            var d = new float[n];
            const float attack = 0.003f;
            for (int k = 0; k < notes.Length; k++)
            {
                int start = Mathf.RoundToInt(k * step * SynthRate);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)SynthRate;
                    float env = Mathf.Min(1f, t / attack);
                    float v = 0f;
                    for (int p = 0; p < ratios.Length; p++)
                        v += gains[p] * Mathf.Exp(-t * (1f + p * 2f) / decay) * Mathf.Sin(2f * Mathf.PI * notes[k] * ratios[p] * t);
                    d[i] += v * env * volume;
                }
            }
            FadeTail(d);
            return d;
        }

        /// <summary>Глухой удар: синус, высота которого быстро падает от f0 к f1, с мягкой атакой.</summary>
        static float[] Thump(float duration, float f0, float f1, float decay, float volume)
        {
            int n = Mathf.CeilToInt(duration * SynthRate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SynthRate;
                float f = f1 + (f0 - f1) * Mathf.Exp(-t / 0.04f);
                phase += 2f * Mathf.PI * f / SynthRate;
                d[i] = Mathf.Sin(phase) * Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t / decay) * volume;
            }
            FadeTail(d);
            return d;
        }

        static void Add(float[] dst, float[] src, float offset)
        {
            int o = Mathf.RoundToInt(offset * SynthRate);
            for (int i = 0; i < src.Length && i + o < dst.Length; i++) dst[i + o] += src[i];
        }

        /// <summary>Последние 10 мс — к нулю: обрыв буфера не щёлкает.</summary>
        static void FadeTail(float[] d)
        {
            int n = Mathf.Min(d.Length, SynthRate / 100);
            for (int i = 0; i < n; i++) d[d.Length - 1 - i] *= i / (float)n;
        }

        static AudioClip Clip(string name, float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create(name, data.Length, 1, SynthRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
