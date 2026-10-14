using CatMiners.Core;
using CatMiners.Core.Sim;
using CatMiners.Core.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>Компонент для выбора объектов лучом.</summary>
    public sealed class Pickable : MonoBehaviour
    {
        public enum PickKind { Cat, Object, Container, Loot }
        public PickKind Kind;
        public int Id;  // id котика / ordinal объекта / id контейнера / id добычи
        /// <summary>Область тапа пузыря статуса («Активировать», «Собрать», «Открыть»): важнее котика и контейнера под ним.</summary>
        public bool Bubble;
    }

    /// <summary>
    /// Подпись в мире (TextMeshPro, пиксельный шрифт UiFonts.Default), повёрнутая к камере. Это оверлей
    /// (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 4): своя SortingGroup с sortAtRoot выводит её из группы объекта,
    /// порядок по умолчанию — <see cref="PixelWorld.OverlayOrderAt"/> по мировой z подписи, <see cref="SetOrder"/> задаёт его явно.
    /// <c>localPos</c> — в пространстве корня родителя: x вправо, +Z вверх по экрану (камера смотрит сверху).
    /// </summary>
    public sealed class Label3D : MonoBehaviour
    {
        public TextMeshPro Text;

        /// <summary>Мелкие подписи на поле (уровень, ETA, пузыри, ящики): пиксель шрифта = 1 пиксель арта (9/32 клетки на em).</summary>
        public const float SmallSize = 2.8125f;
        /// <summary>Крупные подписи (всплывающие награды): пиксель шрифта = 2 пикселя арта.</summary>
        public const float LargeSize = 5.625f;

        const float OutlineWidth = 0.2f;
        static readonly Color32 OutlineColor = PixelPalette.Ink1;
        // Один общий материал с обводкой на все подписи (ТЗ 15.3): сеттеры tmp.outlineWidth/outlineColor
        // создают копию материала шрифта на каждую подпись, которую TextMeshPro.OnDestroy не удаляет,
        // а разные материалы не батчатся.
        static Material s_OutlineSource;
        static Material s_Outline;
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        SortingGroup m_Group;
        bool m_AutoOrder = true;
        int m_Order = int.MinValue;
        float m_OrderZ = float.NaN;

        public static Label3D Create(Transform parent, Vector3 localPos, float size, Color color)
        {
            var go = new GameObject("label");
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.rotation = PixelWorld.FaceCamera;
            var tmp = go.AddComponent<TextMeshPro>();
            var font = UiFonts.Default;
            if (font != null) tmp.font = font;
            // Размер TMP 3D: 1 em = size / 10 клетки, пиксель шрифта = 1/9 em (UiFonts.EmPixels), пиксель арта = 1/32 клетки.
            // Размер 2,8125 (SmallSize) даёт пиксель шрифта ровно в 1 пиксель арта, 5,625 (LargeSize) — в 2; промежуточные
            // значения размыли бы пиксели шрифта.
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            var outline = OutlineMaterial(tmp.fontSharedMaterial);
            if (outline != null) tmp.fontSharedMaterial = outline;
            tmp.rectTransform.sizeDelta = new Vector2(4, 1);
            var l = go.AddComponent<Label3D>();
            l.Text = tmp;
            l.m_Group = go.AddComponent<SortingGroup>();
            l.m_Group.sortAtRoot = true;
            l.UpdateAutoOrder();
            return l;
        }

        /// <summary>Явный порядок отрисовки (например, всплывающий текст поверх полос HP); авто-порядок по z отключается.</summary>
        public void SetOrder(int order)
        {
            m_AutoOrder = false;
            ApplyOrder(order);
        }

        void ApplyOrder(int order)
        {
            if (order == m_Order || m_Group == null) return;
            m_Order = order;
            m_Group.sortingOrder = order;
        }

        void UpdateAutoOrder()
        {
            float z = transform.position.z;
            if (z == m_OrderZ) return;
            m_OrderZ = z;
            ApplyOrder(PixelWorld.OverlayOrderAt(z));
        }

        /// <summary>
        /// Копия материала шрифта с обводкой — создаётся один раз на исходный материал и делится всеми подписями.
        /// Те же свойства, что раньше выставляли сеттеры TMP на копии у каждой подписи.
        /// </summary>
        static Material OutlineMaterial(Material source)
        {
            if (source == null) return null;
            if (s_Outline != null && s_OutlineSource == source) return s_Outline;
            var mat = new Material(source) { name = source.name + " (world labels)", hideFlags = HideFlags.DontSave };
            mat.shaderKeywords = source.shaderKeywords;
            mat.SetFloat(OutlineWidthId, OutlineWidth);
            mat.SetColor(OutlineColorId, OutlineColor);
            // Прежний общий материал не удаляем: им могут пользоваться уже созданные подписи.
            s_OutlineSource = source;
            s_Outline = mat;
            return mat;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var r = cam.transform.rotation;
                if (transform.rotation != r) transform.rotation = r;
            }
            if (m_AutoOrder) UpdateAutoOrder();
        }
    }

    /// <summary>
    /// Полоса HP над целью: рамка <c>hp_frame</c> и заливка <c>hp_fill</c> (9-slice), ширина заливки — целые пиксели арта.
    /// Оверлей: своя SortingGroup с sortAtRoot, порядок по мировой z полосы. <c>localPos</c> — в пространстве корня родителя
    /// (x вправо, +Z вверх по экрану), ширина — в клетках.
    /// </summary>
    public sealed class HpBar : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        const int FrameHeight = 6;   // hp_frame: контур 1 px, внутри 4 px
        const int FillHeight = 4;

        SpriteRenderer m_Fill;
        SortingGroup m_Group;
        int m_WidthPx;
        int m_FillPx = -1;
        int m_Order = int.MinValue;
        float m_OrderZ = float.NaN;

        public static HpBar Create(Transform parent, Vector3 localPos, float widthCells)
        {
            var go = new GameObject("hp");
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(PixelWorld.Snap(localPos.x), localPos.y, PixelWorld.Snap(localPos.z));
            t.rotation = PixelWorld.FaceCamera;
            var bar = go.AddComponent<HpBar>();
            // Чётная ширина: рамка по центру узла ложится краями на целые пиксели.
            bar.m_WidthPx = Mathf.Max(6, Mathf.RoundToInt(widthCells * PixelArt.CellPixels * 0.5f) * 2);
            PixelWorld.Sliced(t, "frame", "hp_frame", Vector2.zero, new Vector2(bar.m_WidthPx * Px, FrameHeight * Px), 0);
            bar.m_Fill = PixelWorld.Sliced(t, "fill", "hp_fill", Vector2.zero, new Vector2(Px, FillHeight * Px), 1);
            bar.m_Group = go.AddComponent<SortingGroup>();
            bar.m_Group.sortAtRoot = true;
            go.SetActive(false);
            return bar;
        }

        public void Set(float fraction, bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;
            int inner = m_WidthPx - 2;
            float f = Mathf.Clamp01(fraction);
            int px = Mathf.RoundToInt(inner * f);
            if (px == 0 && f > 0f) px = 1;
            if (px == m_FillPx) return;
            m_FillPx = px;
            m_Fill.enabled = px > 0;
            if (px == 0) return;
            m_Fill.size = new Vector2(px * Px, FillHeight * Px);
            // Заливка прижата к левому краю внутри контура (1 px).
            float left = -m_WidthPx * 0.5f + 1f;
            m_Fill.transform.localPosition = new Vector3((left + px * 0.5f) * Px, 0f, 0f);
        }

        void LateUpdate()
        {
            float z = transform.position.z;
            if (z == m_OrderZ) return;
            m_OrderZ = z;
            int order = PixelWorld.OverlayOrderAt(z);
            if (order == m_Order) return;
            m_Order = order;
            m_Group.sortingOrder = order;
        }
    }

    /// <summary>
    /// Котик-шахтёр из пиксельных слоёв (ART_DIRECTION.md, «Котики» и «Анимация котиков»): тень, тело по окрасу
    /// с отметинами, каска цвета круга уровней и оружие уровня (<see cref="CatLook"/>), бейдж уровня.
    /// Корень стоит в центре клетки (коллайдер и выбор — по нему), визуал снапится к сетке пикселей арта, ноги — на
    /// 10 пикселей ниже центра клетки. Кадры выбираются по настоящему состоянию котика: перенос, появление и радость
    /// (разовые), ходьба к новой клетке, добыча (замах и удар синхронны фазе симуляции и событию TargetHit), ожидание
    /// с дыханием, морганием и редким потягиванием. Урон и награды анимация не определяет: она только показывает
    /// состояние симуляции.
    /// </summary>
    public sealed class CatView : MonoBehaviour
    {
        public int CatId;
        public int Level;
        public bool Dragged;
        public Vector3 TargetPos;
        /// <summary>Цель добычи по последней синхронизации (линейный индекс объекта, −1 — нет): радость после разрушения.</summary>
        public int TargetIndex = -1;

        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги относительно центра клетки (вниз по экрану).</summary>
        const float FeetY = -10f * Px;
        /// <summary>Голова над центром клетки — точка искорок вспышки объединения.</summary>
        const float HeadY = 10f * Px;
        // Бейдж справа снизу, как на концепте: центр на 11 px правее, низ на 6 px ниже ног (у нижней кромки клетки).
        const int BadgeCenterX = 11;
        const int BadgeBottom = -6;

        // Ожидание: 6 кадров дыхания, моргание, редкое потягивание (у каждого котика свой темп).
        const float IdleFrameSeconds = 0.22f;
        const float BlinkSeconds = 0.12f;
        const float BlinkMinGap = 2f, BlinkMaxGap = 5f;
        const float StretchMinGap = 10f, StretchMaxGap = 26f;
        static readonly float[] StretchTimes = { 0.12f, 0.12f, 0.14f, 0.2f, 0.45f, 0.14f, 0.12f, 0.2f, 0.16f, 0.16f };
        // Ходьба: 8 кадров; скорость шага к новой клетке (клеток/с).
        const float WalkFrameSeconds = 0.09f;
        const float StepSpeed = 2.4f;
        const float ArriveSqr = 0.02f * 0.02f;
        // Добыча: замах занимает до WindUpMax перед ударом (не больше половины задержки атаки); после удара — удар,
        // отдача и возврат; в стойке котик дышит (два кадра).
        const float WindUpMax = 0.3f, WindUpShare = 0.45f;
        static readonly float[] StrikeTimes = { 0.05f, 0.08f, 0.08f, 0.1f };
        static readonly int[] StrikeFrames = { CatLook.MineStrike, CatLook.MineImpact, CatLook.MineRecoil, CatLook.MineReturn };
        const float ReadyBreath = 0.42f;
        // Перенос: лапки болтаются.
        const float DragFrameSeconds = 0.11f;
        // Появление из бочки и радость: длительность кадров и высота прыжка (пиксели арта) — форма тела в кадрах, высота — здесь.
        static readonly float[] SpawnTimes = { 0.08f, 0.06f, 0.1f, 0.08f, 0.08f, 0.12f, 0.18f, 0.25f };
        static readonly int[] SpawnLift = { 0, 3, 7, 4, 0, 0, 0, 0 };
        static readonly float[] JoyTimes = { 0.06f, 0.06f, 0.1f, 0.07f, 0.25f, 0.15f };
        static readonly int[] JoyLift = { 0, 3, 6, 0, 1, 0 };
        // Масштаб — только короткие «попы» не дольше 0,25 с.
        const float PopSeconds = 0.18f;
        const float PopFrom = 0.7f;
        const float PopPeak = 1.12f;
        const float PulseSeconds = 0.2f;
        const float PulsePeak = 1.125f;
        // Перенос: тело поднято на 8 px, тень остаётся на полу и чуть уменьшается.
        const float DragLift = 8f * Px;
        const float LiftSpeed = 3f;         // клеток/с: подъём за ≈0,08 с
        const float DragShadowScale = 0.8f;
        const int DragOrder = PixelWorld.FogOrder + 1;   // несомый котик поверх объектов и затемнения, под частицами
        const float FaceDeadZone = 0.05f;

        // Объединение (ТЗ 14): стягивание, вспышка, новый номер уровня.
        const float AbsorbSeconds = 0.2f;
        const float AbsorbHop = 6f * Px;
        const float HoldLevelMaxSeconds = 0.6f;
        const float LeanCells = 4f * Px;

        // Порядок слоёв внутри группы котика.
        const int ShadowOrder = 0, RingOrder = 1, BodyOrder = 2, HelmetOrder = 3, WeaponOrder = 4;

        enum OneShot { None, Spawn, Joy, Stretch }

        Transform m_Visual;
        Transform m_Pose;              // точка ног: тело, каска, оружие, бейдж; масштаб «попа» — от ног
        SpriteRenderer m_Shadow, m_Ring, m_Body, m_Helmet, m_Weapon;
        readonly Sprite[] m_BodySprites = new Sprite[CatLook.FrameCount];
        readonly Sprite[] m_HelmetSprites = new Sprite[CatLook.FrameCount];
        readonly Sprite[] m_WeaponSprites = new Sprite[CatLook.FrameCount];
        LevelBadge m_Badge;
        SortingGroup m_Group;

        int m_Frame = -1;
        int m_ShownLevel;
        int m_WeaponKind = -1;     // CatLook.Weapon(уровень): кирка … лазерган
        int m_HelmetColour = -1;   // CatLook.HelmetColor(уровень)
        bool m_FaceLeft;
        bool m_Highlighted;
        bool m_Selected;
        bool m_Mining;
        Vector3 m_FacePoint;
        float m_TimeToHit = float.MaxValue;
        float m_WindUp = WindUpMax;

        float m_Phase;             // общий таймер кадров (у каждого котика свой сдвиг)
        float m_BlinkIn;
        float m_BlinkLeft;
        float m_StretchIn;
        float m_StrikeT = -1f;     // ≥ 0 — идёт удар (кадры StrikeFrames)
        OneShot m_Shot;
        float m_ShotT;
        bool m_Moving;
        float m_WalkT;
        float m_PopT = 1f;
        float m_PulseT = 1f;
        float m_Lift;

        bool m_HoldLevel;          // получатель ждёт стягивания: новый номер покажем во вспышке
        float m_HoldT;
        Vector3 m_LeanDir;
        Vector3 m_Lean;
        bool m_Absorbing;          // этот вид втягивается в получателя и будет удалён
        // Уход в следующую шахту (ТЗ 11.2): путь по клеткам к выходу, затем котик скрывается в штольне.
        const float ExitFrameSeconds = 0.07f;
        const float EnterSeconds = 0.3f;
        const float EnterDepth = 6f * Px;
        Vector3[] m_WalkPath;
        int m_WalkIndex;
        float m_WalkDelay, m_WalkSpeed;
        float m_EnterT = -1f;
        Effects m_WalkFx;
        CatView m_AbsorbInto;
        Effects m_AbsorbFx;
        Vector3 m_AbsorbFrom;
        float m_AbsorbT;

        // Последние записанные значения: трансформы и сортировка меняются, только когда значение другое.
        Vector3 m_VisualShown = new Vector3(float.NaN, 0f, 0f);
        Vector3 m_PoseShown = new Vector3(float.NaN, 0f, 0f);
        float m_ScaleShown = -1f;
        float m_ShadowScaleShown = -1f;
        int m_Order = int.MinValue;

        /// <param name="appear">Котик только что появился (из бочки, ящика, бесплатного контейнера): прыжок появления.</param>
        public static CatView Create(Transform parent, CatState cat, bool appear = true)
        {
            var root = new GameObject("cat" + cat.Id);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CatView>();
            view.CatId = cat.Id;
            // Котики дышат, моргают и потягиваются вразнобой.
            view.m_Phase = (cat.Id * 0.37f) % 1f * IdleFrameSeconds * CatLook.IdleCount;
            view.m_BlinkIn = Random.Range(BlinkMinGap, BlinkMaxGap);
            view.m_StretchIn = Random.Range(StretchMinGap, StretchMaxGap);
            view.m_Group = PixelWorld.Group(root, root.transform.position.z + FeetY);

            var visual = PixelWorld.Visual(root.transform);
            view.m_Visual = visual;
            view.m_Shadow = PixelWorld.Sprite(visual, "shadow", "shadow_s", new Vector2(0f, FeetY), ShadowOrder);
            var pose = new GameObject("pose").transform;
            pose.SetParent(visual, false);
            pose.localPosition = new Vector3(0f, FeetY, 0f);
            view.m_Pose = pose;

            int coat = CatLook.Coat(cat.Id);
            for (int f = 0; f < CatLook.FrameCount; f++) view.m_BodySprites[f] = PixelArt.World(CatLook.Body(coat, f));
            view.m_Body = PixelWorld.Sprite(pose, "body", CatLook.Body(coat, CatLook.Idle), Vector2.zero, BodyOrder);
            view.m_Helmet = PixelWorld.Sprite(pose, "helmet", CatLook.Helmet(0, CatLook.Idle), Vector2.zero, HelmetOrder);
            view.m_Weapon = PixelWorld.Sprite(pose, "weapon", CatLook.WeaponSprite(CatLook.Pick, CatLook.Idle), Vector2.zero, WeaponOrder);
            view.m_Badge = new LevelBadge(pose, BadgeCenterX, BadgeBottom);

            // Коллайдер: клетка сверху и голова, заходящая в клетку выше.
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0.1f);
            col.size = new Vector3(0.8f, 1f, 1f);
            var p = root.AddComponent<Pickable>();
            p.Kind = Pickable.PickKind.Cat;
            p.Id = cat.Id;

            view.Level = cat.Level;
            view.ApplyLevelVisual();
            // Появление: вид создаётся в кадре события CatSpawned (OnEvent приходит раньше Sync) — котик выпрыгивает.
            if (appear) view.PlaySpawn();
            return view;
        }

        public void SetLevel(int level)
        {
            if (Level == level) return;
            Level = level;
            if (m_HoldLevel) return;   // номер, каска и оружие обновятся во вспышке после стягивания
            ApplyLevelVisual();
        }

        /// <summary>
        /// Вид уровня: оружие по кругу из 8 уровней и цвет каски круга (<see cref="CatLook"/>). Спрайты кадров берутся заново
        /// только при смене оружия или цвета; у инструмента кадры добычи — tool_* (<see cref="CatLook.ForWeapon"/>).
        /// </summary>
        void ApplyLevelVisual()
        {
            m_ShownLevel = Level;
            int weapon = CatLook.Weapon(Level), colour = CatLook.HelmetColor(Level);
            if (weapon != m_WeaponKind || colour != m_HelmetColour)
            {
                if (colour != m_HelmetColour)
                    for (int f = 0; f < CatLook.FrameCount; f++) m_HelmetSprites[f] = PixelArt.World(CatLook.Helmet(colour, f));
                if (weapon != m_WeaponKind)
                {
                    for (int f = 0; f < CatLook.FrameCount; f++)
                    {
                        // Кадры, которые этот вид оружия не показывает (mine_* у инструмента, tool_* у ударного), не грузятся.
                        bool used = CatLook.ForWeapon(f, weapon) == f && (CatLook.IsTool(weapon) || f < CatLook.ToolReady);
                        m_WeaponSprites[f] = used ? PixelArt.World(CatLook.WeaponSprite(weapon, f)) : null;
                    }
                }
                m_WeaponKind = weapon;
                m_HelmetColour = colour;
                int frame = m_Frame < 0 ? CatLook.Idle : m_Frame;
                m_Frame = -1;
                ApplyFrame(frame);
            }
            m_Badge.Set(m_ShownLevel, m_Highlighted);
        }

        /// <summary>Добыча: котик назначен на цель (взгляд — к ней) или свободен.</summary>
        public void SetMining(bool mining, Vector3 facePoint)
        {
            m_Mining = mining;
            m_FacePoint = facePoint;
            if (!mining) m_TimeToHit = float.MaxValue;
        }

        /// <summary>
        /// Сколько секунд игрового времени до следующего удара по фазе симуляции и задержка атаки: по ним выбираются кадры
        /// замаха — подготовка, замах и верхняя точка — так, чтобы верхняя точка совпала с ударом (TargetHit).
        /// </summary>
        public void SetTimeToHit(float seconds, float attackDelay)
        {
            m_TimeToHit = seconds;
            m_WindUp = Mathf.Min(WindUpMax, attackDelay * WindUpShare);
        }

        /// <summary>
        /// Удар оружием — в кадре события TargetHit: удар, сильная поза с отдачей, возврат в стойку (≈0,3 с).
        /// Повторный вызов начинает удар заново. Радость и потягивание удар прерывает: он синхронен симуляции.
        /// </summary>
        public void Strike()
        {
            if (Dragged || m_Absorbing) return;
            m_StrikeT = 0f;
            if (m_Shot != OneShot.Spawn) m_Shot = OneShot.None;
        }

        /// <summary>Прежнее имя <see cref="Strike"/>.</summary>
        public void Swing() => Strike();

        /// <summary>Совместимость: прежний флаг замаха. Замах теперь выбирается по <see cref="SetTimeToHit"/>.</summary>
        public void SetWindUp(bool on) { }

        /// <summary>Появление (событие CatSpawned): котик выпрыгивает — присел, прыжок, приземлился, оглядывается, радуется.</summary>
        public void PlaySpawn()
        {
            StartShot(OneShot.Spawn);
            m_PopT = 0f;
            ApplyScale(PopFrom, PopFrom);
        }

        /// <summary>Прежнее имя появления.</summary>
        public void Pop() => PlaySpawn();

        /// <summary>Радость: короткий прыжок с поднятыми лапками (разбил камень, объединение).</summary>
        public void PlayJoy()
        {
            if (Dragged || m_Absorbing || m_WalkPath != null) return;
            StartShot(OneShot.Joy);
        }

        void StartShot(OneShot shot)
        {
            m_Shot = shot;
            m_ShotT = 0f;
        }

        /// <summary>Мягкая пульсация (заметно слабее появления): масштаб 1 → 1.125 → 1 за 0,2 с.</summary>
        public void Pulse() => m_PulseT = 0f;

        /// <summary>Кандидат на объединение при переносе: золотая кайма бейджа (badge_level_hi).</summary>
        public void SetHighlighted(bool on)
        {
            if (m_Highlighted == on) return;
            m_Highlighted = on;
            if (m_ShownLevel > 0) m_Badge.Set(m_ShownLevel, on);
        }

        /// <summary>Выделение котика на поле: кольцо select_ring под ним, без коллайдера.</summary>
        public void SetSelected(bool on)
        {
            if (m_Selected == on) return;
            m_Selected = on;
            if (on && m_Ring == null) m_Ring = PixelWorld.Sprite(m_Visual, "selectRing", "select_ring", new Vector2(0f, FeetY), RingOrder);
            if (m_Ring != null) m_Ring.enabled = on && !m_Absorbing;
        }

        /// <summary>Получатель объединения: ждёт втягивания второго котика, слегка тянется к нему.</summary>
        public void ExpectMerge(Vector3 fromWorld)
        {
            m_HoldLevel = true;
            m_HoldT = 0f;
            var dir = fromWorld - transform.position;
            dir.y = 0f;
            m_LeanDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
        }

        /// <summary>Вспышка объединения: новый уровень (оружие, цвет каски, бейдж), кольцо, искорки, радость и пульсация.</summary>
        public void MergeFlash() => MergeFlash(null);

        void MergeFlash(Effects fx)
        {
            m_HoldLevel = false;
            ApplyLevelVisual();
            m_PulseT = 0f;
            PlayJoy();
            if (fx == null) fx = Effects.Main;
            if (fx == null) return;
            var p = transform.position;
            fx.Ring(new Vector3(p.x, p.y, p.z + FeetY));
            fx.Glow(new Vector3(p.x, p.y, p.z + HeadY * 0.5f), PixelPalette.Amber4, 1.2f, 0.5f);
            fx.Sparkle(new Vector3(p.x, p.y, p.z + HeadY), 4);
        }

        /// <summary>
        /// Втянуть этот вид в получателя (≈0.2 с, с уменьшением) и удалить. Коллайдер и выбор отключаются сразу:
        /// котика уже нет в модели. По прибытии получатель играет вспышку.
        /// </summary>
        public void AbsorbInto(CatView receiver, Effects fx)
        {
            m_Absorbing = true;
            m_AbsorbInto = receiver;
            m_AbsorbFx = fx;
            m_AbsorbFrom = transform.position;
            m_AbsorbT = 0f;
            Dragged = false;
            m_Mining = false;
            m_StrikeT = -1f;
            m_Shot = OneShot.None;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            var pick = GetComponent<Pickable>();
            if (pick != null) Destroy(pick);
            if (m_Ring != null) m_Ring.enabled = false;
            m_Badge.SetActive(false);
            ApplyFrame(CatLook.Joy + 2);
            if (receiver != null) receiver.ExpectMerge(m_AbsorbFrom);
        }

        /// <summary>Котик уходит в следующую шахту (вид заморожен, модель уже в новой зоне).</summary>
        public bool Walking => m_WalkPath != null;

        /// <summary>
        /// Уйти по пути <paramref name="path"/> (мировые точки; последняя — в устье штольни выхода) со скоростью
        /// <paramref name="speed"/> клеток/с после паузы <paramref name="delay"/> с; у выхода — искорки, котик уменьшается
        /// и скрывается в штольне. Выбор и тапы по котику отключаются сразу.
        /// </summary>
        public void WalkInto(Vector3[] path, float delay, float speed, Effects fx)
        {
            if (path == null || path.Length == 0 || m_Absorbing) return;
            m_WalkPath = path;
            m_WalkIndex = 0;
            m_WalkDelay = Mathf.Max(0f, delay);
            m_WalkSpeed = Mathf.Max(0.5f, speed);
            m_EnterT = -1f;
            m_WalkFx = fx;
            Dragged = false;
            m_Mining = false;
            m_StrikeT = -1f;
            m_Shot = OneShot.None;
            m_HoldLevel = false;
            // Котик, только что вышедший из бочки, уходит в полный рост: «поп» появления не обрывается на малом масштабе.
            m_PopT = 1f;
            m_PulseT = 1f;
            ApplyScale(1f, 1f);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (m_Ring != null) m_Ring.enabled = false;
            SetHighlighted(false);
        }

        void UpdateWalk(float dt)
        {
            m_Phase += dt;
            var t = transform;
            var p = t.position;
            if (m_WalkDelay > 0f)
            {
                m_WalkDelay -= dt;
                ApplyFrame(IdleFrame());
                SyncVisual(p);
                return;
            }
            if (m_EnterT < 0f)
            {
                float step = m_WalkSpeed * dt;
                while (step > 0f && m_WalkIndex < m_WalkPath.Length)
                {
                    var to = m_WalkPath[m_WalkIndex];
                    var d = new Vector3(to.x - p.x, 0f, to.z - p.z);
                    float len = d.magnitude;
                    if (Mathf.Abs(d.x) > FaceDeadZone) SetFacing(d.x < 0f);
                    if (len <= step)
                    {
                        p = new Vector3(to.x, p.y, to.z);
                        step -= len;
                        m_WalkIndex++;
                    }
                    else
                    {
                        p += d * (step / len);
                        step = 0f;
                    }
                }
                t.position = p;
                // Быстрый шаг: те же 8 кадров ходьбы чаще.
                m_WalkT += dt;
                ApplyFrame(CatLook.Walk + (int)(m_WalkT / ExitFrameSeconds) % CatLook.WalkCount);
                ApplyPose(0f, 0f);
                if (m_WalkIndex >= m_WalkPath.Length)
                {
                    m_EnterT = 0f;
                    m_Badge.SetActive(false);
                    if (m_WalkFx != null) m_WalkFx.Sparkle(p, 2);
                }
            }
            else
            {
                // В штольне: котик уменьшается и уходит вглубь (вверх по экрану), затем исчезает.
                m_EnterT = Mathf.Min(1f, m_EnterT + dt / EnterSeconds);
                float s = Mathf.Lerp(1f, 0.3f, m_EnterT);
                ApplyScale(s, s);
                ApplyPose(0f, m_EnterT * EnterDepth);
                if (m_EnterT >= 1f)
                {
                    gameObject.SetActive(false);
                    return;
                }
            }
            SyncVisual(p);
        }

        /// <summary>Взгляд влево или вправо: отражение по X всех слоёв, только при смене.</summary>
        void SetFacing(bool left)
        {
            if (left == m_FaceLeft) return;
            m_FaceLeft = left;
            m_Body.flipX = left;
            m_Helmet.flipX = left;
            m_Weapon.flipX = left;
        }

        void UpdateAbsorb(float dt)
        {
            m_AbsorbT = Mathf.Min(1f, m_AbsorbT + dt / AbsorbSeconds);
            float k = m_AbsorbT * m_AbsorbT * (3f - 2f * m_AbsorbT);
            bool alive = m_AbsorbInto != null;
            var to = alive ? m_AbsorbInto.transform.position : m_AbsorbFrom;
            var p = Vector3.Lerp(m_AbsorbFrom, to, k);
            transform.position = p;
            // Дуга прыжка — вверх по экрану; подъём переноса (если был) опускается по ходу.
            ApplyPose(0f, m_Lift * (1f - k) + Mathf.Sin(k * Mathf.PI) * AbsorbHop);
            float s = Mathf.Lerp(1f, 0.15f, k);
            ApplyScale(s, s);
            SyncVisual(p);
            if (m_AbsorbT < 1f) return;
            if (alive) m_AbsorbInto.MergeFlash(m_AbsorbFx);
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_Absorbing)
            {
                UpdateAbsorb(dt);
                return;
            }
            if (m_WalkPath != null)
            {
                UpdateWalk(dt);
                return;
            }

            // Перемещение: в переносе — за пальцем; иначе котик идёт к своей клетке шагом (ходьба), доехав — встаёт на сетку.
            var t = transform;
            var p = t.position;
            var target = Dragged ? TargetPos : PixelWorld.Snap(TargetPos);
            var delta = new Vector3(target.x - p.x, 0f, target.z - p.z);
            m_Moving = false;
            if (Dragged)
            {
                if (p != target) p = Vector3.Lerp(p, target, 1f - Mathf.Exp(-30f * dt));
            }
            else if (delta.sqrMagnitude > ArriveSqr)
            {
                float len = delta.magnitude;
                float step = StepSpeed * dt;
                p = len <= step ? target : p + delta * (step / len);
                m_Moving = len > step;
                if (Mathf.Abs(delta.x) > FaceDeadZone) SetFacing(delta.x < 0f);
            }
            else p = target;
            t.position = p;

            // Таймеры кадров.
            m_Phase += dt;
            if (m_StrikeT >= 0f)
            {
                m_StrikeT += dt;
                if (m_StrikeT >= Sum(StrikeTimes)) m_StrikeT = -1f;
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
            bool idle = !Dragged && !m_Mining && !m_Moving && m_Shot == OneShot.None && m_StrikeT < 0f && !m_Selected;
            if (idle)
            {
                m_StretchIn -= dt;
                if (m_StretchIn <= 0f)
                {
                    m_StretchIn = Random.Range(StretchMinGap, StretchMaxGap);
                    StartShot(OneShot.Stretch);
                }
            }
            else if (m_Shot == OneShot.Stretch && (Dragged || m_Mining || m_Moving)) m_Shot = OneShot.None;

            int lift = 0;
            int frame = SelectFrame(ref lift);
            ApplyFrame(frame);

            // Взгляд: при добыче — к цели; при ходьбе — по ходу (выше); в переносе и в ожидании — как был.
            if (m_Mining && !Dragged && !m_Moving)
            {
                float dx = m_FacePoint.x - p.x;
                if (dx < -FaceDeadZone) SetFacing(true);
                else if (dx > FaceDeadZone) SetFacing(false);
            }

            // Получатель объединения тянется к втягиваемому котику; если вспышки нет слишком долго — показать уровень сразу.
            if (m_HoldLevel)
            {
                m_HoldT += dt;
                if (m_HoldT > HoldLevelMaxSeconds) MergeFlash();
            }
            var lean = m_HoldLevel ? m_LeanDir * (LeanCells * Mathf.Clamp01(m_HoldT / AbsorbSeconds)) : Vector3.zero;
            m_Lean = Vector3.Lerp(m_Lean, lean, 1f - Mathf.Exp(-20f * dt));
            m_Lift = Mathf.MoveTowards(m_Lift, Dragged ? DragLift : 0f, LiftSpeed * dt);
            ApplyPose(m_Lean.x, m_Lift + m_Lean.z + lift * Px);

            if (m_PopT < 1f) m_PopT = Mathf.Min(1f, m_PopT + dt / PopSeconds);
            if (m_PulseT < 1f) m_PulseT = Mathf.Min(1f, m_PulseT + dt / PulseSeconds);
            float pop = PopScale(m_PopT);
            float pulse = m_PulseT < 1f ? 1f + Mathf.Sin(m_PulseT * Mathf.PI) * (PulsePeak - 1f) : 1f;
            // В прыжке тень на полу меньше.
            float shadow = (Dragged ? DragShadowScale : 1f) * (lift > 3 ? 0.8f : 1f);
            ApplyScale(pop * pulse, pop * shadow);

            SyncVisual(p);
        }

        /// <summary>Кадр по состоянию: перенос → появление/радость → ходьба → удар и замах → стойка добычи → ожидание.</summary>
        int SelectFrame(ref int lift)
        {
            if (Dragged) return CatLook.Drag + (int)(m_Phase / DragFrameSeconds) % CatLook.DragCount;
            if (m_Shot == OneShot.Spawn || m_Shot == OneShot.Joy)
            {
                var times = ShotTimes(m_Shot);
                int i = Step(times, m_ShotT);
                lift = m_Shot == OneShot.Spawn ? SpawnLift[i] : JoyLift[i];
                return (m_Shot == OneShot.Spawn ? CatLook.Spawn : CatLook.Joy) + i;
            }
            if (m_Moving)
            {
                m_WalkT += Time.deltaTime;
                return CatLook.Walk + (int)(m_WalkT / WalkFrameSeconds) % CatLook.WalkCount;
            }
            if (m_StrikeT >= 0f) return StrikeFrames[Step(StrikeTimes, m_StrikeT)];
            if (m_Mining)
            {
                float t = m_TimeToHit;
                if (t <= m_WindUp)
                {
                    // Подготовка → замах → верхняя точка; верхняя точка — в кадре перед ударом.
                    float u = 1f - t / Mathf.Max(1e-3f, m_WindUp);
                    return u < 0.34f ? CatLook.MineAntic : (u < 0.67f ? CatLook.MineRaise : CatLook.MineTop);
                }
                return ((int)(m_Phase / ReadyBreath) & 1) == 0 ? CatLook.MineReady : CatLook.MineReady2;
            }
            if (m_Shot == OneShot.Stretch) return CatLook.Stretch + Step(StretchTimes, m_ShotT);
            int idle = IdleFrame();
            if (m_BlinkLeft > 0f) return idle == CatLook.Idle + 2 || idle == CatLook.Idle + 3 ? CatLook.BlinkDown : CatLook.Blink;
            return idle;
        }

        int IdleFrame() => CatLook.Idle + (int)(m_Phase / IdleFrameSeconds) % CatLook.IdleCount;

        static float[] ShotTimes(OneShot shot) => shot == OneShot.Spawn ? SpawnTimes : (shot == OneShot.Joy ? JoyTimes : StretchTimes);

        static float Sum(float[] times)
        {
            float s = 0f;
            for (int i = 0; i < times.Length; i++) s += times[i];
            return s;
        }

        /// <summary>Номер кадра по времени от начала разовой анимации (последний — до конца).</summary>
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

        void ApplyFrame(int frame)
        {
            if (frame == m_Frame) return;
            m_Frame = frame;
            // Инструмент (пила, бур, бензопила, лазер) добывает «с упора»: фазы mine_* показываются кадрами tool_*.
            int f = CatLook.ForWeapon(frame, m_WeaponKind);
            m_Body.sprite = m_BodySprites[f];
            m_Helmet.sprite = m_HelmetSprites[f];
            m_Weapon.sprite = m_WeaponSprites[f];
        }

        /// <summary>Смещение позы от ног в клетках (x вправо, y вверх по экрану), снап к пикселям арта.</summary>
        void ApplyPose(float x, float y)
        {
            var pos = new Vector3(PixelWorld.Snap(x), FeetY + PixelWorld.Snap(y), 0f);
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

        /// <summary>Визуал — на сетке пикселей арта (корень и коллайдер едут плавно); порядок — по z ног.</summary>
        void SyncVisual(Vector3 p)
        {
            var snapped = PixelWorld.Snap(p);
            if (snapped != m_VisualShown)
            {
                m_VisualShown = snapped;
                m_Visual.position = snapped;
            }
            float feetZ = p.z + FeetY;
            int order = Dragged ? DragOrder : PixelWorld.OrderAt(feetZ);
            if (order != m_Order)
            {
                m_Order = order;
                m_Group.sortingOrder = order;
            }
            m_Badge.SetOrder(PixelWorld.OverlayOrderAt(feetZ));
        }
    }

    /// <summary>
    /// Контейнер: бочка с бесплатным или нанятым котиком, ящик доставки. Ящики и бочки бесплатного котика мягко подпрыгивают
    /// на месте; бочка найма (<see cref="ContainerKind.Hire"/>) зовёт тапнуть: два коротких прыжка, пауза и толчок изнутри.
    /// Появление во время игры — короткий «поп» масштаба (<see cref="Pop"/>).
    /// </summary>
    public sealed class ContainerView : MonoBehaviour
    {
        public int ContainerId;

        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>Ноги спрайта — нижняя кромка клетки.</summary>
        const float FeetY = -0.5f;
        const float HopPixels = 3f;
        const float HopSpeed = 3f;      // |sin|: прыжок примерно раз в секунду
        // Бочка найма: цикл «прыжок 3 px, прыжок 2 px, пауза, толчок ±1 px», фазы цикла — в секундах от его начала.
        const float CallPeriod = 1.8f;
        const float CallHop1 = 0.2f, CallHop2Start = 0.24f, CallHop2End = 0.4f;
        const float CallShakeStart = 1.0f, CallShakeEnd = 1.24f, CallShakeStep = 0.06f;
        // «Поп» появления, как у котика: 0,25 → 1,2 → 1 за 0,22 с.
        const float PopSeconds = 0.22f;
        const float PopFrom = 0.25f;
        const float PopPeak = 1.2f;
        /// <summary>Подпись над контейнером: вверх по экрану, над крышкой (холст 32×36 от нижней кромки клетки).</summary>
        const float LabelZ = 0.78f;
        /// <summary>Размер подписи: 1 em ≈ 0.28 клетки, «Котик 12» — чуть шире клетки.</summary>
        const float LabelSize = Label3D.SmallSize;

        // Полёт из пушки (ТЗ 7.2): дуга от дула в клетку. Длительность и высота дуги растут с расстоянием (клетки).
        const float FlyBaseSeconds = 0.45f, FlySecondsPerCell = 0.06f, FlyMinSeconds = 0.55f, FlyMaxSeconds = 1.1f;
        const float FlyBaseHeight = 1.2f, FlyHeightPerCell = 0.2f, FlyMinHeight = 1.5f, FlyMaxHeight = 3.2f;
        /// <summary>Бочка вылетает центром из дула: ноги — на полклетки ниже.</summary>
        const float FlyCenterLift = 0.45f;
        const float FlyFrameSeconds = 0.1f;
        /// <summary>Вылет из дула — короткий «поп» масштаба (руководство: дробный масштаб — только до 0,25 с).</summary>
        const float LaunchPopSeconds = 0.12f, LaunchPopFrom = 0.6f;
        /// <summary>Тень полёта: появляется, когда бочка отошла от пушки; высоко — маленькая.</summary>
        const float ShadowFromT = 0.15f, ShadowSmallAbove = 1.0f;
        /// <summary>Приземление: сплющивание и отскок формы за 0,18 с.</summary>
        const float LandSeconds = 0.18f;
        /// <summary>Летящая бочка — над объектами и затемнением, под частицами (как летящая добыча).</summary>
        const int FlyOrder = PixelWorld.FogOrder + 1;
        static readonly string[] s_FlyFrames = { "barrel_fly_0", "barrel_fly_1" };

        Transform m_Body;
        float m_T;
        bool m_Calling;
        int m_HopPx = -1;
        int m_ShakePx;
        float m_PopT = 1f;
        SpriteRenderer m_Box, m_Shadow;
        string m_RestSprite;
        Label3D m_Label;
        BoxCollider m_Collider;
        SortingGroup m_Group;
        int m_RestOrder;
        float m_FlyT = 1f, m_FlyTime, m_FlySeconds, m_FlyHeight;
        Vector3 m_FlyFrom, m_FlyGround;     // от ног бочки в клетке: дуло (с поправкой на центр) и пол под ним
        float m_LandT = 1f;
        System.Action<ContainerView> m_Landed;

        /// <summary>Бочка летит из пушки: тап, подпись и прыжки — после приземления.</summary>
        public bool Flying => m_FlyT < 1f;

        public static ContainerView Create(Transform parent, ContainerState c, Vector3 pos)
        {
            var root = new GameObject("container" + c.Id);
            root.transform.SetParent(parent, false);
            root.transform.position = PixelWorld.Snap(pos);
            var v = root.AddComponent<ContainerView>();
            v.ContainerId = c.Id;
            v.m_T = (c.Id * 0.61f) % 1f;   // контейнеры прыгают вразнобой
            v.m_Calling = c.Kind == ContainerKind.Hire;
            v.m_Group = PixelWorld.Group(root, pos.z + FeetY);
            v.m_RestOrder = v.m_Group.sortingOrder;

            string sprite;
            string text;
            if (c.Kind == ContainerKind.FreeCat || c.Kind == ContainerKind.Hire)
            {
                sprite = "barrel_cat";
                text = Loc.F("board.container.cat", c.CatLevel);
            }
            else
            {
                switch (c.RewardType)
                {
                    case Core.Data.RewardTypes.Miner:
                        sprite = "crate_cat";
                        text = Loc.F("board.container.cat", c.CatLevel);
                        break;
                    case Core.Data.RewardTypes.Spell:
                        sprite = "crate_dynamite";
                        text = Loc.T("board.container.dynamite");
                        break;
                    default:
                        bool soft = c.DetailedType == "Soft";
                        sprite = soft ? "crate_elixir" : "crate_gold";
                        text = soft ? Loc.F("board.container.elixir", NumberFormat.Format(c.Amount)) : Loc.F("board.container.gold", NumberFormat.Format(c.Amount));
                        break;
                }
            }

            var visual = PixelWorld.Visual(root.transform);
            v.m_Shadow = PixelWorld.Sprite(visual, "shadow", "shadow_s", new Vector2(0f, FeetY + 3f * Px), 0);
            var body = new GameObject("body").transform;
            body.SetParent(visual, false);
            body.localPosition = new Vector3(0f, FeetY, 0f);
            v.m_Box = PixelWorld.Sprite(body, "box", sprite, Vector2.zero, 1);
            v.m_RestSprite = sprite;
            v.m_Body = body;
            v.m_Label = Label3D.Create(root.transform, new Vector3(0f, 0.1f, LabelZ), LabelSize, PixelPalette.Gold4);
            v.m_Label.Text.text = text;

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.4f, 0.05f);
            col.size = new Vector3(0.9f, 0.9f, 1f);
            v.m_Collider = col;
            var p = root.AddComponent<Pickable>();
            p.Kind = Pickable.PickKind.Container;
            p.Id = c.Id;
            return v;
        }

        /// <summary>Появление во время игры (событие ContainerSpawned): короткий «поп» масштаба от ног.</summary>
        public void Pop()
        {
            m_PopT = 0f;
            m_Body.localScale = new Vector3(PopFrom, PopFrom, 1f);
        }

        /// <summary>
        /// Вылететь из пушки (ТЗ 7.2): дугой из <paramref name="muzzle"/> (дуло) в свою клетку; <paramref name="ground"/> —
        /// пол под дулом, от него идёт тень. Пока бочка летит, тапа и подписи нет, котик в ней машет лапками; по приземлении —
        /// сплющивание, затем обычные прыжки и <paramref name="landed"/>.
        /// </summary>
        public void Launch(Vector3 muzzle, Vector3 ground, System.Action<ContainerView> landed)
        {
            var rest = transform.position;
            float dist = Vector2.Distance(new Vector2(ground.x, ground.z), new Vector2(rest.x, rest.z));
            m_FlySeconds = Mathf.Clamp(FlyBaseSeconds + FlySecondsPerCell * dist, FlyMinSeconds, FlyMaxSeconds);
            m_FlyHeight = Mathf.Clamp(FlyBaseHeight + FlyHeightPerCell * dist, FlyMinHeight, FlyMaxHeight);
            m_FlyFrom = muzzle - rest - new Vector3(0f, 0f, FlyCenterLift);
            m_FlyGround = ground - rest;
            m_FlyT = 0f;
            m_FlyTime = 0f;
            m_PopT = 1f;
            m_LandT = 1f;
            m_Landed = landed;
            m_Collider.enabled = false;
            m_Label.gameObject.SetActive(false);
            m_Shadow.enabled = false;
            m_Group.sortingOrder = FlyOrder;
            UpdateFlight(0f);
        }

        void UpdateFlight(float dt)
        {
            m_FlyTime += dt;
            m_FlyT = Mathf.Min(1f, m_FlyTime / m_FlySeconds);
            float u = m_FlyT;
            if (u >= 1f)
            {
                Land();
                return;
            }
            // Пол движется равномерно, высота: дуло над полом гаснет линейно, сверху — парабола дуги.
            float gx = m_FlyGround.x * (1f - u), gz = m_FlyGround.z * (1f - u);
            float h = (m_FlyFrom.z - m_FlyGround.z) * (1f - u) + 4f * m_FlyHeight * u * (1f - u);
            m_Body.localPosition = new Vector3(PixelWorld.Snap(gx), FeetY + PixelWorld.Snap(gz + h), 0f);
            float k = m_FlyTime < LaunchPopSeconds ? Mathf.Lerp(LaunchPopFrom, 1f, m_FlyTime / LaunchPopSeconds) : 1f;
            m_Body.localScale = new Vector3(k, k, 1f);
            PixelWorld.SetSprite(m_Box, s_FlyFrames[(int)(m_FlyTime / FlyFrameSeconds) & 1]);
            bool shadow = u >= ShadowFromT;
            if (m_Shadow.enabled != shadow) m_Shadow.enabled = shadow;
            if (shadow)
            {
                PixelWorld.SetSprite(m_Shadow, h > ShadowSmallAbove ? "shadow_xs" : "shadow_s");
                m_Shadow.transform.localPosition = new Vector3(PixelWorld.Snap(gx), FeetY + 3f * Px + PixelWorld.Snap(gz), 0f);
            }
        }

        void Land()
        {
            m_FlyT = 1f;
            PixelWorld.SetSprite(m_Box, m_RestSprite);
            PixelWorld.SetSprite(m_Shadow, "shadow_s");
            m_Shadow.transform.localPosition = new Vector3(0f, FeetY + 3f * Px, 0f);
            m_Shadow.enabled = true;
            m_Body.localPosition = new Vector3(0f, FeetY, 0f);
            m_HopPx = -1;
            m_ShakePx = 0;
            m_T = 0f;
            m_LandT = 0f;
            m_Group.sortingOrder = m_RestOrder;
            m_Collider.enabled = true;
            m_Label.gameObject.SetActive(true);
            ApplyLand();
            var landed = m_Landed;
            m_Landed = null;
            landed?.Invoke(this);
        }

        /// <summary>Сплющивание при приземлении: шире и ниже, затем вытягивается и встаёт (только масштаб тела).</summary>
        void ApplyLand()
        {
            float u = m_LandT;
            float sx, sy;
            if (u < 0.35f)
            {
                float a = u / 0.35f;
                sx = Mathf.Lerp(1.25f, 0.92f, a);
                sy = Mathf.Lerp(0.78f, 1.1f, a);
            }
            else
            {
                float a = (u - 0.35f) / 0.65f;
                sx = Mathf.Lerp(0.92f, 1f, a);
                sy = Mathf.Lerp(1.1f, 1f, a);
            }
            m_Body.localScale = u >= 1f ? Vector3.one : new Vector3(sx, sy, 1f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_FlyT < 1f)
            {
                UpdateFlight(dt);
                return;
            }
            m_T += dt;
            if (m_LandT < 1f)
            {
                m_LandT = Mathf.Min(1f, m_LandT + dt / LandSeconds);
                ApplyLand();
            }
            else if (m_PopT < 1f)
            {
                m_PopT = Mathf.Min(1f, m_PopT + dt / PopSeconds);
                float u = m_PopT;
                float k = u >= 1f ? 1f : (u < 0.6f ? Mathf.Lerp(PopFrom, PopPeak, u / 0.6f) : Mathf.Lerp(PopPeak, 1f, (u - 0.6f) / 0.4f));
                m_Body.localScale = new Vector3(k, k, 1f);
            }
            int px, shake = 0;
            if (m_Calling)
            {
                // Целыми пикселями арта: два прыжка вверх по экрану, затем котик внутри толкает бочку вбок.
                float t = m_T % CallPeriod;
                if (t < CallHop1) px = Mathf.RoundToInt(Mathf.Sin(t / CallHop1 * Mathf.PI) * HopPixels);
                else if (t >= CallHop2Start && t < CallHop2End)
                    px = Mathf.RoundToInt(Mathf.Sin((t - CallHop2Start) / (CallHop2End - CallHop2Start) * Mathf.PI) * (HopPixels - 1f));
                else px = 0;
                if (t >= CallShakeStart && t < CallShakeEnd) shake = (((int)((t - CallShakeStart) / CallShakeStep)) & 1) == 0 ? 1 : -1;
            }
            // Прыжок вверх по экрану целыми пикселями; тень остаётся на полу.
            else px = Mathf.RoundToInt(Mathf.Abs(Mathf.Sin(m_T * HopSpeed)) * HopPixels);
            if (px == m_HopPx && shake == m_ShakePx) return;
            m_HopPx = px;
            m_ShakePx = shake;
            m_Body.localPosition = new Vector3(shake * Px, FeetY + px * Px, 0f);
        }
    }
}
