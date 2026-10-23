using System.Collections.Generic;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Спасение Mia в шахте первой сессии (Docs/Rework/FIRST_SESSION.md): клетка — постоянное препятствие обстановки слоями (пол и
    /// задняя стенка, Mia внутри, перед с дверцей), над ней метка цели; замок — обычная цель мусора на подходе. Разрушение замка
    /// (событие CharacterRescued) — сцена ≈ 1,6 с: дверца открывается, Mia радуется, затем сама идёт по пути из ядра к лагерю (камера
    /// не привязана) и ждёт там до конца шахты; дальше Mia — за прилавком магазина (портрет сверху Store), в шахты она не выходит.
    /// Вид только читает ядро: после загрузки, офлайна или тайм-скипа клетка открыта, а Mia сразу стоит в лагере. Mia здесь —
    /// временный вид, не работник: команду она не дублирует.
    /// </summary>
    public sealed partial class OutpostBoardView
    {
        enum RescueActorState { None, Caged, Celebrating, Walking, AtCamp }

        const float DoorFrameSeconds = 0.12f, CelebrateSeconds = 1.5f, MarkerBob = 2f * Px, MarkerPeriod = 0.9f;
        /// <summary>После радости Mia выходит из клетки на глазах игрока: окно «Mia is free!» ждёт столько, камера свободна.</summary>
        const float WalkOutSeconds = 1.2f;
        /// <summary>Скорость Mia по пути к лагерю, клеток в секунду (как у котика без улучшений).</summary>
        const float MiaWalkSpeed = 2.5f;

        Transform m_CageBackGroup, m_CageFrontGroup;
        SpriteRenderer m_CageFront, m_Marker;
        Vector3 m_CageLocal, m_MarkerHome;
        OutpostCatView m_RescueActor;
        RescueActorState m_RescueActorState;
        float m_RescueActorTimer;
        int m_DoorFrame;
        readonly List<Vector3> m_RescueActorPath = new List<Vector3>();
        int m_RescueActorLeg;
        float m_RescueActorLegT;
        Vector3 m_RescueActorPos, m_CampSpot;

        /// <summary>В шахте есть клетка спасения (вид построен).</summary>
        public bool HasRescueCage => m_CageFront != null;

        /// <summary>Центр клетки спасения в мире (камера к цели, лапка подсказки); false — клетки нет.</summary>
        public bool TryGetRescueCageWorldPosition(out Vector3 world)
        {
            world = HasRescueCage ? Origin + m_CageLocal : Vector3.zero;
            return HasRescueCage;
        }

        /// <summary>Идёт сцена спасения (дверца, радость Mia, первые шаги из клетки): итоги шахты и окно «Mia is free!» ждут её конца.</summary>
        public bool RescueSceneActive => m_RescueActorState == RescueActorState.Celebrating || (m_RescueActorState == RescueActorState.Walking && m_RescueActorTimer < CelebrateSeconds + WalkOutSeconds);

        /// <summary>Mia идёт к лагерю после спасения (камера не привязана).</summary>
        public bool MiaWalking => m_Map?.Rescue?.Character == OutpostLook.MiaCrew && RescueCharacterWalking;
        public bool RescueCharacterWalking => m_RescueActorState == RescueActorState.Walking;
        public bool RescueCharacterAtCamp => m_RescueActorState == RescueActorState.AtCamp;
        public string RescueCharacterId => m_Map?.Rescue?.Character;

        /// <summary>Мировая точка Mia в шахте спасения (в клетке, на пути или в лагере); false — её нет.</summary>
        public bool TryGetMiaWorldPosition(out Vector3 world) => TryGetRescueCharacterWorldPosition(out world);

        public bool TryGetRescueCharacterWorldPosition(out Vector3 world)
        {
            world = m_RescueActor != null && m_RescueActorState != RescueActorState.None ? Origin + m_RescueActorPos : Vector3.zero;
            return m_RescueActor != null && m_RescueActorState != RescueActorState.None;
        }

        void ResetRescue()
        {
            m_CageBackGroup = m_CageFrontGroup = null;
            m_CageFront = m_Marker = null;
            m_RescueActor = null;
            m_RescueActorState = RescueActorState.None;
            m_RescueActorPath.Clear();
        }

        /// <summary>Клетка спасения вместо обычного препятствия: задняя стенка, перед с дверцей и метка — свои группы сортировки.</summary>
        bool TryBuildRescueCage(Transform root, MapProp prop)
        {
            var rescue = m_Map.Rescue;
            if (rescue == null || m_Map.Props == null || rescue.CageProp < 0 || rescue.CageProp >= m_Map.Props.Length ||
                !ReferenceEquals(m_Map.Props[rescue.CageProp], prop) || !PixelArt.Has("rescue_cage_back"))
                return false;
            float feetZ = Depth - prop.Row - 1f;
            float x = prop.Column + prop.Width / 2f;
            m_CageLocal = new Vector3(x, 0f, feetZ + 0.5f);

            var back = new GameObject("rescueCageBack");
            back.transform.SetParent(root, false);
            back.transform.localPosition = new Vector3(x, 0f, feetZ + 0.45f);
            var backVisual = PixelWorld.Visual(back.transform);
            PixelWorld.Group(backVisual.gameObject, feetZ + 0.45f);
            PixelWorld.Sprite(backVisual, "shadow", "shadow_m", new Vector2(0f, 4f * Px), 0);
            PixelWorld.Sprite(backVisual, "back", "rescue_cage_back", new Vector2(0f, -0.45f), 1);
            m_CageBackGroup = back.transform;

            var front = new GameObject("rescueCageFront");
            front.transform.SetParent(root, false);
            front.transform.localPosition = new Vector3(x, 0f, feetZ - 0.02f);
            var frontVisual = PixelWorld.Visual(front.transform);
            PixelWorld.Group(frontVisual.gameObject, feetZ - 0.02f);
            m_CageFront = PixelWorld.Sprite(frontVisual, "front", "rescue_cage_front_0", new Vector2(0f, 0.02f), 0);
            m_CageFrontGroup = front.transform;

            // Метка цели над клеткой (оверлей поверх объектов, покачивается).
            m_MarkerHome = new Vector2(x, feetZ + 1.75f);
            m_Marker = PixelWorld.Sprite(PixelWorld.Visual(root, "rescueMarker"), "marker", "marker_rescue", new Vector2(x, feetZ + 1.75f),
                PixelWorld.OverlayOrderAt(feetZ));
            m_Marker.enabled = false;
            return true;
        }

        /// <summary>Вид Mia (временный, не работник): создаётся при первой синхронизации со спасением.</summary>
        void EnsureRescueActor()
        {
            if (m_RescueActor != null || m_CatsRoot == null) return;
            m_RescueActor = OutpostCatView.Create(m_CatsRoot);
            m_RescueActor.Init(-1, 0, m_Map.Rescue.Character);
            m_RescueActor.gameObject.name = m_Map.Rescue.Character;
            m_RescueActor.SetLook(null, m_Sim?.Configs?.Equipment);
            var collider = m_RescueActor.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        /// <summary>Привести клетку и Mia к ядру (каждый кадр).</summary>
        void SyncRescue(OutpostSimulation sim, float dt)
        {
            if (!HasRescueCage) return;
            bool pending = sim.RescuePending;
            if (m_Marker != null)
            {
                bool show = pending && sim.Run.Phase == RunPhase.Running;
                if (m_Marker.enabled != show) m_Marker.enabled = show;
                if (show)
                {
                    float bob = Mathf.Round(Mathf.Sin(Time.time * Mathf.PI * 2f / MarkerPeriod) * 2f) * Px;
                    m_Marker.transform.localPosition = new Vector3(m_MarkerHome.x, m_MarkerHome.y + bob, 0f);
                }
            }
            EnsureRescueActor();
            if (m_RescueActor == null) return;
            if (m_RescueActorState == RescueActorState.None)
            {
                if (pending) PlaceCaged();
                else PlaceAtCamp();
            }
            else if (m_RescueActorState == RescueActorState.Caged && !pending)
            {
                // Замок разрушен без события (офлайн, тайм-скип, загрузка): клетка открыта, Mia уже в лагере.
                SetDoor(3);
                PlaceAtCamp();
            }
            TickRescueActor(sim, dt);
        }

        void PlaceCaged()
        {
            m_RescueActorState = RescueActorState.Caged;
            SetDoor(0);
            m_RescueActorPos = m_CageLocal + new Vector3(0f, 0f, 0.1f);
            m_RescueActor.Snap();
        }

        void PlaceAtCamp()
        {
            if (m_DoorFrame != 3) SetDoor(3);
            m_RescueActorState = RescueActorState.AtCamp;
            m_RescueActorPos = CampSpot();
            m_RescueActor.Snap();
        }

        /// <summary>Место Mia в лагере: нижний ряд площадки слева от точки приёма (не на местах разгрузки и не на пути найма).</summary>
        Vector3 CampSpot()
        {
            if (m_Map.Rescue?.Character == OutpostLook.BrunoCrew) return BaseCampView.BlacksmithSpot(m_Map);
            int baseCol = m_Map.Col(m_Map.SpawnCell);
            int col = Mathf.Max(0, baseCol - 3), row = m_Map.Depth + 2;
            return CellCenterLocal(m_Map.Cell(col, row)) + new Vector3(0.1f, 0f, 0.05f);
        }

        void SetDoor(int frame)
        {
            m_DoorFrame = frame;
            if (m_CageFront != null) PixelWorld.SetSprite(m_CageFront, "rescue_cage_front_" + frame);
        }

        /// <summary>
        /// Событие CharacterRescued: дверца открывается по кадрам, Mia радуется и вскоре идёт к лагерю по пути навигации ядра
        /// (замок уже снят). Без вида клетки — ничего.
        /// </summary>
        public void PlayRescue(OutpostSimulation sim, Effects fx)
        {
            if (!HasRescueCage || sim == null) return;
            EnsureRescueActor();
            if (m_RescueActor == null) return;
            if (m_RescueActorState == RescueActorState.None) PlaceCaged();
            m_RescueActorState = RescueActorState.Celebrating;
            m_RescueActorTimer = 0f;
            m_RescueActor.PlayJoy();
            var world = Origin + m_CageLocal;
            if (fx != null)
            {
                fx.Sparkle(world + new Vector3(0f, 0f, 0.5f), 6);
                if (!VisualSettings.ReduceEffects) fx.Glow(world, PixelPalette.Pink3, 1.6f, 0.6f);
            }
            Emit(OutpostViewCue.Rescue, m_CageLocal);
            BuildRescuePath(sim);
        }

        /// <summary>Путь Mia: клетка → подход (бывший замок) → путь ядра до места в лагере; центры клеток в корне поля.</summary>
        void BuildRescuePath(OutpostSimulation sim)
        {
            m_RescueActorPath.Clear();
            m_RescueActorLeg = 0;
            m_RescueActorLegT = 0f;
            var rescue = m_Map.Rescue;
            var lockTarget = rescue != null ? m_Map.Target(rescue.LockTargetId) : null;
            m_RescueActorPath.Add(m_CageLocal + new Vector3(0f, 0f, 0.1f));
            if (lockTarget == null) return;
            int from = m_Map.Cell(lockTarget.Column, lockTarget.Row);
            m_RescueActorPath.Add(CellCenterLocal(from));
            m_CampSpot = CampSpot();
            int campCell = WorldToCell(Origin + m_CampSpot);
            var path = campCell >= 0 ? sim.Navigation.BuildPath(from, campCell) : null;
            if (path == null)
            {
                // Do not draw a straight line through uncleared targets. Sync retries when navigation changes.
                m_RescueActorPath.Clear();
                m_RescueActorPath.Add(m_CageLocal + new Vector3(0f, 0f, 0.1f));
                return;
            }
            foreach (int cell in path) m_RescueActorPath.Add(CellCenterLocal(cell));
            m_RescueActorPath.Add(m_CampSpot);
        }

        void TickRescueActor(OutpostSimulation sim, float dt)
        {
            var pose = new OutpostCatPose
            {
                Mode = WorkerMode.Idle,
                FaceX = float.NaN,
                TimeToHit = float.PositiveInfinity,
                Progress = -1f,
                MaxJump = float.MaxValue,
                MinOrder = int.MinValue,
            };
            switch (m_RescueActorState)
            {
                case RescueActorState.Caged:
                    pose.FaceX = m_CageLocal.x - 1f;
                    break;
                case RescueActorState.Celebrating:
                {
                    m_RescueActorTimer += dt;
                    int frame = Mathf.Clamp(1 + Mathf.FloorToInt(m_RescueActorTimer / DoorFrameSeconds), 1, 3);
                    if (frame != m_DoorFrame) SetDoor(frame);
                    if (m_RescueActorTimer >= CelebrateSeconds * 0.5f && m_RescueActorTimer - dt < CelebrateSeconds * 0.5f) m_RescueActor.PlayJoy();
                    if (m_RescueActorTimer >= CelebrateSeconds)
                    {
                        if (m_RescueActorPath.Count <= 1) BuildRescuePath(sim);
                        if (m_RescueActorPath.Count > 1) m_RescueActorState = RescueActorState.Walking;
                    }
                    break;
                }
                case RescueActorState.Walking:
                {
                    m_RescueActorTimer += dt;
                    float step = MiaWalkSpeed * dt;
                    while (step > 0f && m_RescueActorLeg + 1 < m_RescueActorPath.Count)
                    {
                        var a = m_RescueActorPath[m_RescueActorLeg];
                        var b = m_RescueActorPath[m_RescueActorLeg + 1];
                        float len = Mathf.Max(0.001f, Vector3.Distance(a, b));
                        float left = (1f - m_RescueActorLegT) * len;
                        if (step < left)
                        {
                            m_RescueActorLegT += step / len;
                            step = 0f;
                        }
                        else
                        {
                            step -= left;
                            m_RescueActorLeg++;
                            m_RescueActorLegT = 0f;
                        }
                    }
                    if (m_RescueActorLeg + 1 >= m_RescueActorPath.Count)
                    {
                        m_RescueActorState = RescueActorState.AtCamp;
                        m_RescueActorPos = m_RescueActorPath[m_RescueActorPath.Count - 1];
                        m_RescueActor.PlayJoy();
                    }
                    else
                    {
                        var a = m_RescueActorPath[m_RescueActorLeg];
                        var b = m_RescueActorPath[m_RescueActorLeg + 1];
                        m_RescueActorPos = Vector3.Lerp(a, b, m_RescueActorLegT);
                        pose.OnEdge = true;
                        pose.Mode = WorkerMode.GoingHome;
                        pose.FaceX = b.x;
                    }
                    break;
                }
                case RescueActorState.AtCamp:
                    pose.FaceX = m_Map.Width / 2f;
                    break;
            }
            pose.Position = m_RescueActorPos;
            pose.Capacity = 1;
            m_RescueActor.Apply(in pose, dt);
        }
    }
}
