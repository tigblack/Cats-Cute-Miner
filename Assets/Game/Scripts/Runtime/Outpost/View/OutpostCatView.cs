using CatMiners.Core.Outpost;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Поза котика на кадр (RUNTIME_SPEC §4.2): её считает <see cref="OutpostBoardView"/> из состояния ядра. Позиция — локальная
    /// в корне поля (формула CellCenter), на ребре пути — точная интерполяция P0 + (Now − SegmentStart)/Duration.
    /// </summary>
    public struct OutpostCatPose
    {
        /// <summary>Точка котика (центр клетки или точка на ребре + стабильное смещение), локально в корне поля.</summary>
        public Vector3 Position;
        /// <summary>Ядро ведёт котика по ребру пути (Action.Kind == Edge).</summary>
        public bool OnEdge;
        public WorkerMode Mode;
        public int Bag;
        public int Capacity;
        /// <summary>x точки, к которой повернуться стоя (цель добычи, склад, точка приёма); NaN — не задано.</summary>
        public float FaceX;
        /// <summary>Секунды времени ядра до удара (Hit.Deadline − Now) при добыче; иначе +∞.</summary>
        public float TimeToHit;
        /// <summary>Окно замаха (секунды ядра): подготовка → замах → верхняя точка заканчиваются к удару.</summary>
        public float WindUp;
        /// <summary>Доля текущего Pickup/Unload-действия 0…1; иначе −1.</summary>
        public float Progress;
        /// <summary>Наибольшее непрерывное смещение ядра с прошлого кадра (клетки): больше — разрыв, вид догоняет шагом.</summary>
        public float MaxJump;
        /// <summary>Нижняя граница порядка отрисовки (котик в строке закрытых ворот рисуется перед створками); int.MinValue — нет.</summary>
        public int MinOrder;
    }

    /// <summary>
    /// Котик нового режима (Docs/Rework/ART_NAMES.md §2): слои тень, кольцо, тело, обувь, рюкзак, груз, убор, кирка, мешочек;
    /// кадры idle/blink/stretch, walk/carry, mine (замах по времени до удара, удар — по событию TargetHit), pickup, unload, spawn, joy.
    /// Положение задаёт ядро (<see cref="Apply"/>): на ребре — точно по пути; при разрыве (очередь склада, сбор команды, конец шахты)
    /// вид догоняет новую точку шагом, не меняя логики. Время кадров — Time.deltaTime, ходьба — по пройденному пути (лапы не скользят
    /// при любой скорости ядра). Анимация ничего не начисляет и не двигает ядро.
    /// </summary>
    public sealed class OutpostCatView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги относительно точки котика (вниз по экрану), как у CatView.</summary>
        public const float FeetY = -10f * Px;
        /// <summary>Точка сумки над ногами: сюда прилетают мешочки.</summary>
        public const float BagY = 6f * Px;

        // Ожидание: дыхание, моргание, редкое потягивание.
        const float IdleFrameSeconds = 0.22f;
        const float BlinkSeconds = 0.12f;
        const float BlinkMinGap = 2f, BlinkMaxGap = 5f;
        const float StretchMinGap = 10f, StretchMaxGap = 26f;
        static readonly float[] StretchTimes = { 0.12f, 0.12f, 0.14f, 0.2f, 0.45f, 0.14f, 0.12f, 0.2f, 0.16f, 0.16f };
        // Ходьба по пройденному пути: клетки на кадр (при базовой скорости 2,5 кл/с — 0,09 с и 0,105 с на кадр).
        const float WalkStride = 0.225f, CarryStride = 0.26f;
        // Добыча: удар, отдача, возврат после TargetHit; стойка дышит двумя кадрами.
        static readonly float[] StrikeTimes = { 0.05f, 0.08f, 0.08f, 0.1f };
        static readonly int[] StrikeFrames = { OutpostLook.MineStrike, OutpostLook.MineImpact, OutpostLook.MineRecoil, OutpostLook.MineReturn };
        const float ReadyBreath = 0.42f;
        // Разовые: появление, радость, конец разгрузки.
        static readonly float[] SpawnTimes = { 0.08f, 0.06f, 0.1f, 0.08f, 0.08f, 0.12f, 0.18f, 0.25f };
        static readonly int[] SpawnLift = { 0, 3, 7, 4, 0, 0, 0, 0 };
        static readonly float[] JoyTimes = { 0.06f, 0.06f, 0.1f, 0.07f, 0.25f, 0.15f };
        static readonly int[] JoyLift = { 0, 3, 6, 0, 1, 0 };
        const float UnloadDoneSeconds = 0.3f;
        // Масштаб — только короткие «попы» не дольше 0,25 с (PIXEL_STYLE_GUIDE).
        const float PopSeconds = 0.18f, PopFrom = 0.7f, PopPeak = 1.12f;
        const float PulseSeconds = 0.2f, PulsePeak = 1.125f;
        // Догонялка при разрыве: не медленнее шага, не дольше CatchMaxSeconds.
        const float CatchMinSpeed = 2.4f, CatchMaxSeconds = 2f;
        const float CatchEpsilon = 1e-3f;
        const float FaceDeadZone = 0.05f;

        // Порядок слоёв в группе котика (ART_NAMES §2.2).
        const int ShadowOrder = 0, RingOrder = 1, BodyOrder = 2, BootsOrder = 3, PackOrder = 4, LoadOrder = 5, HatOrder = 6, PickOrder = 7, SackOrder = 8;

        enum OneShot { None, Spawn, Joy, Stretch }

        public int WorkerId { get; private set; }

        Transform m_Visual, m_Pose;
        SpriteRenderer m_Shadow, m_Ring, m_Body, m_Sack, m_Load;
        readonly SpriteRenderer[] m_Equip = new SpriteRenderer[OutpostLook.LayerCount];
        readonly Sprite[] m_BodyS = new Sprite[OutpostLook.FrameCount];
        readonly Sprite[] m_SackS = new Sprite[OutpostLook.FrameCount];
        readonly Sprite[][] m_EquipS = new Sprite[OutpostLook.LayerCount][];
        readonly Sprite[][] m_LoadS = new Sprite[OutpostLook.LoadStages][];
        readonly int[] m_Kinds = { -1, -1, -1, -1 };
        SortingGroup m_Group;
        BoxCollider m_Collider;
        OutpostPickable m_Pick;
        int m_Fur = -1;

        int m_Frame = -1;
        int m_LoadShown = -1;
        int m_Stage;
        bool m_FaceLeft;
        bool m_Selected;

        // Положение: ядро (последняя поза), смещение догонялки, показанная точка.
        bool m_HasPos;
        Vector3 m_Core;
        Vector3 m_Off;
        float m_CatchSpeed;
        Vector3 m_Shown;
        bool m_HasSpawnFrom;
        Vector3 m_SpawnFrom;

        float m_Phase, m_BlinkIn, m_BlinkLeft, m_StretchIn;
        float m_StrikeT = -1f;
        float m_UnloadDoneT = -1f;
        OneShot m_Shot;
        float m_ShotT;
        float m_WalkDist;
        int m_WalkFrame = -1;
        float m_PopT = 1f, m_PulseT = 1f;

        Vector3 m_VisualShown = new Vector3(float.NaN, 0f, 0f);
        Vector3 m_PoseShown = new Vector3(float.NaN, 0f, 0f);
        float m_ScaleShown = -1f, m_ShadowScaleShown = -1f;
        int m_Order = int.MinValue;

        /// <summary>Показанная точка котика (локально в корне поля): сюда смотрят эффекты, сведения и обучение.</summary>
        public Vector3 LocalPosition => m_Shown;
        /// <summary>Котик на экране идёт (ребро ядра или догонялка).</summary>
        public bool Moving { get; private set; }

        public static OutpostCatView Create(Transform parent)
        {
            var root = new GameObject("cat");
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<OutpostCatView>();
            v.Build();
            return v;
        }

        void Build()
        {
            var root = gameObject;
            m_Group = PixelWorld.Group(root, 0f);
            m_Visual = PixelWorld.Visual(root.transform);
            m_Shadow = PixelWorld.Sprite(m_Visual, "shadow", "shadow_s", new Vector2(0f, FeetY), ShadowOrder);
            m_Ring = PixelWorld.Sprite(m_Visual, "selectRing", "select_ring", new Vector2(0f, FeetY), RingOrder);
            m_Ring.enabled = false;
            var pose = new GameObject("pose").transform;
            pose.SetParent(m_Visual, false);
            pose.localPosition = new Vector3(0f, FeetY, 0f);
            m_Pose = pose;
            m_Body = Layer("body", BodyOrder);
            m_Equip[(int)OutpostLook.Layer.Boots] = Layer("boots", BootsOrder);
            m_Equip[(int)OutpostLook.Layer.Pack] = Layer("pack", PackOrder);
            m_Load = Layer("load", LoadOrder);
            m_Equip[(int)OutpostLook.Layer.Hat] = Layer("hat", HatOrder);
            m_Equip[(int)OutpostLook.Layer.Pick] = Layer("pick", PickOrder);
            m_Sack = Layer("sack", SackOrder);
            for (int l = 0; l < OutpostLook.LayerCount; l++) m_EquipS[l] = new Sprite[OutpostLook.FrameCount];
            for (int s = 0; s < OutpostLook.LoadStages; s++)
            {
                m_LoadS[s] = new Sprite[OutpostLook.FrameCount];
                OutpostLook.FillLoad(m_LoadS[s], s + 1);
            }
            OutpostLook.FillSack(m_SackS);

            // Коллайдер: клетка сверху и голова, заходящая в клетку выше (как у CatView).
            m_Collider = root.AddComponent<BoxCollider>();
            m_Collider.center = new Vector3(0f, 0.5f, 0.1f);
            m_Collider.size = new Vector3(0.8f, 1f, 1f);
            m_Pick = OutpostPickable.Add(root, OutpostPickKind.Worker, 0);
        }

        SpriteRenderer Layer(string name, int order)
        {
            var sr = PixelWorld.Sprite(m_Pose, name, "px_white", Vector2.zero, order);
            sr.sprite = null;
            return sr;
        }

        /// <summary>Подготовить вид (новый или из пула) для работника: окрас, сброс состояния, место без анимации.</summary>
        public void Init(int workerId, int look)
        {
            WorkerId = workerId;
            gameObject.name = "cat" + workerId;
            m_Pick.Id = workerId;
            m_Collider.enabled = true;
            int fur = OutpostLook.FurOf(look);
            if (fur != m_Fur)
            {
                m_Fur = fur;
                OutpostLook.FillBody(m_BodyS, fur);
                m_Frame = -1;
            }
            m_HasPos = false;
            m_Off = Vector3.zero;
            m_HasSpawnFrom = false;
            m_Phase = (workerId * 0.37f) % 1f * IdleFrameSeconds * OutpostLook.IdleCount;
            m_BlinkIn = Random.Range(BlinkMinGap, BlinkMaxGap);
            m_BlinkLeft = 0f;
            m_StretchIn = Random.Range(StretchMinGap, StretchMaxGap);
            m_StrikeT = -1f;
            m_UnloadDoneT = -1f;
            m_Shot = OneShot.None;
            m_WalkDist = 0f;
            m_WalkFrame = -1;
            m_PopT = m_PulseT = 1f;
            m_Stage = 0;
            m_LoadShown = -1;
            Moving = false;
            SetSelected(false);
            ApplyScale(1f, 1f);
        }

        /// <summary>
        /// Слои экипировки по надетому комплекту команды (RUNTIME_SPEC §4.2): спрайты кадров берутся заново только для слоёв,
        /// у которых сменился вид. Вызывать при создании и на EquipmentChanged/ItemUpgraded.
        /// </summary>
        public bool SetLook(OutpostProfile profile, EquipmentCatalog catalog)
        {
            bool changed = false;
            for (int s = 0; s < OutpostIds.SlotCount; s++)
            {
                var slot = (EquipSlot)s;
                int layer = (int)OutpostLook.LayerOf(slot);
                int kind = OutpostLook.KindFor(profile, catalog, slot);
                if (kind == m_Kinds[layer]) continue;
                m_Kinds[layer] = kind;
                OutpostLook.FillEquip(m_EquipS[layer], (OutpostLook.Layer)layer, kind);
                changed = true;
            }
            if (changed)
            {
                int f = m_Frame < 0 ? OutpostLook.Idle : m_Frame;
                m_Frame = -1;
                ApplyFrame(f, m_Stage);
            }
            return changed;
        }

        /// <summary>Мягкая пульсация (смена экипировки): 1 → 1.125 → 1 за 0,2 с.</summary>
        public void Pulse() => m_PulseT = 0f;

        /// <summary>Удар — в кадре события TargetHit: удар, отдача, возврат (≈0,3 с).</summary>
        public void Strike()
        {
            m_StrikeT = 0f;
            if (m_Shot != OneShot.Spawn) m_Shot = OneShot.None;
        }

        /// <summary>
        /// Появление нанятого котика: прыжок с «попом»; <paramref name="fromLocal"/> — точка лагеря (доска найма), откуда он
        /// выходит к клетке спавна ядра (только вид). null — появляется на месте.
        /// </summary>
        public void PlaySpawn(Vector3? fromLocal)
        {
            StartShot(OneShot.Spawn);
            m_PopT = 0f;
            ApplyScale(PopFrom, PopFrom);
            m_HasSpawnFrom = fromLocal.HasValue;
            if (fromLocal.HasValue) m_SpawnFrom = fromLocal.Value;
        }

        /// <summary>Радость: короткий прыжок с поднятыми лапками (цель разрушена, команда собралась).</summary>
        public void PlayJoy()
        {
            if (m_Shot == OneShot.Spawn) return;
            StartShot(OneShot.Joy);
        }

        /// <summary>Последняя порция сдана (unload3), затем обычная поза.</summary>
        public void PlayUnloadDone() => m_UnloadDoneT = 0f;

        public void SetSelected(bool on)
        {
            if (m_Selected == on && m_Ring.enabled == on) return;
            m_Selected = on;
            m_Ring.enabled = on;
        }

        /// <summary>Поставить котика в точку ядра без догонялки (сборка поля, загрузка, офлайн).</summary>
        public void Snap() => m_HasPos = false;

        void StartShot(OneShot shot)
        {
            m_Shot = shot;
            m_ShotT = 0f;
        }

        /// <summary>
        /// Кадр котика по позе ядра. Возвращает true, если в этом кадре лапа коснулась земли (подсказка шага для звука).
        /// </summary>
        public bool Apply(in OutpostCatPose pose, float dt)
        {
            var target = pose.Position;
            if (!m_HasPos)
            {
                m_HasPos = true;
                m_Core = target;
                m_Off = Vector3.zero;
                if (m_HasSpawnFrom)
                {
                    m_HasSpawnFrom = false;
                    SetOffset(m_SpawnFrom - target);
                }
                m_Shown = target + m_Off;
            }
            else
            {
                // Разрыв положения ядра (очередь склада, конец шахты, сбор команды): показанная точка не прыгает, а догоняет.
                // Пока котик выпрыгивает при найме, показанная точка стоит: смещение поглощает ход ядра.
                var jump = target - m_Core;
                jump.y = 0f;
                if (jump.magnitude > pose.MaxJump + 0.03f || (m_Shot == OneShot.Spawn && jump.sqrMagnitude > 0f)) SetOffset(m_Off - jump);
                m_Core = target;
            }

            // Догонялка — линейно к нулю; пока котик выпрыгивает при найме, он стоит на месте.
            if (m_Off.sqrMagnitude > CatchEpsilon * CatchEpsilon && m_Shot != OneShot.Spawn)
            {
                float len = m_Off.magnitude;
                float step = m_CatchSpeed * dt;
                m_Off = len <= step ? Vector3.zero : m_Off * ((len - step) / len);
            }
            else if (m_Shot != OneShot.Spawn) m_Off = Vector3.zero;

            var prev = m_Shown;
            var shown = target + m_Off;
            m_Shown = shown;
            transform.localPosition = shown;
            var delta = shown - prev;
            delta.y = 0f;
            float moved = delta.magnitude;
            bool catching = m_Off.sqrMagnitude > CatchEpsilon * CatchEpsilon;
            Moving = pose.OnEdge || catching;

            // Таймеры кадров.
            m_Phase += dt;
            if (m_StrikeT >= 0f)
            {
                m_StrikeT += dt;
                if (m_StrikeT >= Sum(StrikeTimes)) m_StrikeT = -1f;
            }
            if (m_UnloadDoneT >= 0f)
            {
                m_UnloadDoneT += dt;
                if (m_UnloadDoneT >= UnloadDoneSeconds) m_UnloadDoneT = -1f;
            }
            if (m_Shot != OneShot.None)
            {
                m_ShotT += dt;
                if (m_ShotT >= Sum(ShotTimes(m_Shot))) m_Shot = OneShot.None;
            }
            if (m_BlinkLeft > 0f) m_BlinkLeft -= dt;
            else
            {
                m_BlinkIn -= dt;
                if (m_BlinkIn <= 0f)
                {
                    m_BlinkLeft = BlinkSeconds;
                    m_BlinkIn = Random.Range(BlinkMinGap, BlinkMaxGap);
                }
            }
            bool restful = pose.Mode == WorkerMode.Idle && !Moving && m_Shot == OneShot.None && !m_Selected;
            if (restful)
            {
                m_StretchIn -= dt;
                if (m_StretchIn <= 0f)
                {
                    m_StretchIn = Random.Range(StretchMinGap, StretchMaxGap);
                    StartShot(OneShot.Stretch);
                }
            }
            else if (m_Shot == OneShot.Stretch && (Moving || pose.Mode != WorkerMode.Idle || m_Selected)) m_Shot = OneShot.None;

            bool carrying = pose.Bag > 0;
            bool footstep = false;
            int lift = 0;
            int frame;
            // Появление важнее всего (котик стоит, пока выпрыгивает); радость — только стоя: идущий котик не скользит в прыжке.
            if (m_Shot == OneShot.Spawn || (m_Shot == OneShot.Joy && !Moving))
            {
                var times = ShotTimes(m_Shot);
                int i = Step(times, m_ShotT);
                lift = m_Shot == OneShot.Spawn ? SpawnLift[i] : JoyLift[i];
                frame = (m_Shot == OneShot.Spawn ? OutpostLook.Spawn : OutpostLook.Joy) + i;
            }
            else if (Moving)
            {
                // С грузом — carry (кирка убрана), без груза — walk; кадр — по пройденному пути.
                m_WalkDist += moved;
                float stride = carrying ? CarryStride : WalkStride;
                int cycle = (int)(m_WalkDist / stride) % OutpostLook.WalkCount;
                if (cycle != m_WalkFrame)
                {
                    footstep = m_WalkFrame >= 0 && (cycle == 0 || cycle == OutpostLook.WalkCount / 2);
                    m_WalkFrame = cycle;
                }
                frame = (carrying ? OutpostLook.Carry : OutpostLook.Walk) + cycle;
            }
            else frame = SelectStanding(in pose);

            // Взгляд: в пути — по ходу; стоя — к точке позы (цель, склад, точка приёма).
            if (Moving)
            {
                if (delta.x < -1e-4f) SetFacing(true);
                else if (delta.x > 1e-4f) SetFacing(false);
            }
            else if (!float.IsNaN(pose.FaceX))
            {
                float dx = pose.FaceX - shown.x;
                if (dx < -FaceDeadZone) SetFacing(true);
                else if (dx > FaceDeadZone) SetFacing(false);
            }

            m_Stage = OutpostLook.LoadStage(pose.Bag, pose.Capacity);
            ApplyFrame(frame, m_Stage);
            ApplyPose(lift * Px);

            if (m_PopT < 1f) m_PopT = Mathf.Min(1f, m_PopT + dt / PopSeconds);
            if (m_PulseT < 1f) m_PulseT = Mathf.Min(1f, m_PulseT + dt / PulseSeconds);
            float pop = PopScale(m_PopT);
            float pulse = m_PulseT < 1f ? 1f + Mathf.Sin(m_PulseT * Mathf.PI) * (PulsePeak - 1f) : 1f;
            ApplyScale(pop * pulse, pop * (lift > 3 ? 0.8f : 1f));
            SyncVisual(pose.MinOrder);
            return footstep;
        }

        void SetOffset(Vector3 off)
        {
            off.y = 0f;
            m_Off = off;
            float len = off.magnitude;
            m_CatchSpeed = Mathf.Max(CatchMinSpeed, len / CatchMaxSeconds);
        }

        /// <summary>Кадр стоя: удар → замах/стойка добычи, подбор, разгрузка, ожидание очереди, отдых.</summary>
        int SelectStanding(in OutpostCatPose pose)
        {
            m_WalkFrame = -1;
            if (m_UnloadDoneT >= 0f) return OutpostLook.Unload + 3;
            switch (pose.Mode)
            {
                case WorkerMode.Mining:
                {
                    if (m_StrikeT >= 0f) return StrikeFrames[Step(StrikeTimes, m_StrikeT)];
                    float t = pose.TimeToHit;
                    float windUp = Mathf.Max(1e-3f, pose.WindUp);
                    if (t <= windUp)
                    {
                        float u = 1f - t / windUp;
                        return u < 0.34f ? OutpostLook.MineAntic : (u < 0.67f ? OutpostLook.MineRaise : OutpostLook.MineTop);
                    }
                    return ((int)(m_Phase / ReadyBreath) & 1) == 0 ? OutpostLook.MineReady : OutpostLook.MineReady2;
                }
                case WorkerMode.PickingUp:
                    if (pose.Progress >= 0f) return OutpostLook.Pickup + Mathf.Clamp((int)(pose.Progress * OutpostLook.PickupCount), 0, OutpostLook.PickupCount - 1);
                    break;
                case WorkerMode.Unloading:
                    if (pose.Progress >= 0f) return OutpostLook.Unload + Mathf.Clamp((int)(pose.Progress * 3f), 0, 2);
                    break;
            }
            if (m_StrikeT >= 0f) return StrikeFrames[Step(StrikeTimes, m_StrikeT)];
            if (m_Shot == OneShot.Stretch) return OutpostLook.Stretch + Step(StretchTimes, m_ShotT);
            int idle = OutpostLook.Idle + (int)(m_Phase / IdleFrameSeconds) % OutpostLook.IdleCount;
            if (m_BlinkLeft > 0f) return idle == OutpostLook.Idle + 2 || idle == OutpostLook.Idle + 3 ? OutpostLook.BlinkDown : OutpostLook.Blink;
            return idle;
        }

        static float[] ShotTimes(OneShot shot) => shot == OneShot.Spawn ? SpawnTimes : (shot == OneShot.Joy ? JoyTimes : StretchTimes);

        static float Sum(float[] times)
        {
            float s = 0f;
            for (int i = 0; i < times.Length; i++) s += times[i];
            return s;
        }

        static int Step(float[] times, float t)
        {
            for (int i = 0; i < times.Length; i++)
            {
                if (t < times[i]) return i;
                t -= times[i];
            }
            return times.Length - 1;
        }

        static float PopScale(float u)
        {
            if (u >= 1f) return 1f;
            return u < 0.6f ? Mathf.Lerp(PopFrom, PopPeak, u / 0.6f) : Mathf.Lerp(PopPeak, 1f, (u - 0.6f) / 0.4f);
        }

        void SetFacing(bool left)
        {
            if (left == m_FaceLeft) return;
            m_FaceLeft = left;
            m_Body.flipX = left;
            m_Load.flipX = left;
            m_Sack.flipX = left;
            for (int l = 0; l < m_Equip.Length; l++) m_Equip[l].flipX = left;
        }

        void ApplyFrame(int frame, int stage)
        {
            if (frame == m_Frame && stage == m_LoadShown) return;
            m_Frame = frame;
            m_LoadShown = stage;
            Set(m_Body, m_BodyS[frame]);
            for (int l = 0; l < m_Equip.Length; l++) Set(m_Equip[l], m_Kinds[l] >= 0 ? m_EquipS[l][frame] : null);
            Set(m_Load, stage > 0 ? m_LoadS[stage - 1][frame] : null);
            Set(m_Sack, m_SackS[frame]);
        }

        static void Set(SpriteRenderer sr, Sprite s)
        {
            if (sr.sprite != s) sr.sprite = s;
            bool on = s != null;
            if (sr.enabled != on) sr.enabled = on;
        }

        void ApplyPose(float lift)
        {
            var pos = new Vector3(0f, FeetY + PixelWorld.Snap(lift), 0f);
            if (pos == m_PoseShown) return;
            m_PoseShown = pos;
            m_Pose.localPosition = pos;
        }

        void ApplyScale(float body, float shadow)
        {
            if (body != m_ScaleShown)
            {
                m_ScaleShown = body;
                m_Pose.localScale = new Vector3(body, body, 1f);
            }
            if (shadow != m_ShadowScaleShown)
            {
                m_ShadowScaleShown = shadow;
                m_Shadow.transform.localScale = new Vector3(shadow, shadow, 1f);
            }
        }

        /// <summary>Визуал — на сетке пикселей арта (корень и коллайдер едут плавно); порядок — по z ног в корне поля.</summary>
        void SyncVisual(int minOrder)
        {
            var world = transform.position;
            var snapped = PixelWorld.Snap(world);
            if (snapped != m_VisualShown)
            {
                m_VisualShown = snapped;
                m_Visual.position = snapped;
            }
            int order = PixelWorld.OrderAt(m_Shown.z + FeetY);
            if (order < minOrder) order = minOrder;
            if (order != m_Order)
            {
                m_Order = order;
                m_Group.sortingOrder = order;
            }
        }
    }
}
