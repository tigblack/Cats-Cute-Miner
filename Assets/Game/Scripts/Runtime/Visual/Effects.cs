using CatMiners.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Эффекты поля в пиксельном стиле (Docs/Art/PIXEL_STYLE_GUIDE.md, раздел 7). Сколы и обломки — один переиспользуемый
    /// ParticleSystem с квадратными частицами сплошного цвета (ТЗ 15.3: частицы переиспользуются). Покадровые эффекты
    /// (искры, пыль, монетки, кольцо, дым, взрыв, ореол) — фиксированный пул SpriteRenderer, анимируется в Update без выделений.
    /// Всё лежит в плоскости XZ: камера смотрит сверху, «вверх по экрану» — мировая +Z, «гравитация» — вдоль −Z.
    /// </summary>
    public sealed class Effects : MonoBehaviour
    {
        /// <summary>Последний созданный экземпляр: для видов, которым эффекты не передают явно (вспышка объединения).</summary>
        public static Effects Main { get; private set; }

        const float Px = 1f / PixelArt.CellPixels;

        // ───────── сколы (ParticleSystem) ─────────
        /// <summary>«Гравитация» сколов вдоль −Z, клеток/с²: обломки подлетают вверх по экрану и падают вниз.</summary>
        const float ChipGravity = 7f;
        ParticleSystem m_Ps;

        // ───────── пул спрайтов ─────────
        const int PoolSize = 64;

        // Темп кадров (раздел 7 руководства): искры, пыль, монетки 0,06–0,08 с; кольцо 0,06 с; взрыв 0,07 с.
        const float SparkFrame = 0.07f;
        const float PuffFrame = 0.08f;
        const float CoinFrame = 0.07f;
        const float RingFrame = 0.06f;
        const float SmokeFrame = 0.2f;
        const float BoomFrame = 0.07f;
        const float CoinLife = 0.75f;
        const float CoinGravity = 8f;

        // Порядок внутри слоя частиц: ореол под всем, кольцо под искрами, взрыв сверху. Все — над затемнением секторов.
        const int GlowOrder = PixelWorld.FxOrder - 2;
        const int RingOrder = PixelWorld.FxOrder - 1;
        const int DustOrder = PixelWorld.FxOrder;
        const int SparkOrder = PixelWorld.FxOrder + 1;
        const int BoomOrder = PixelWorld.FxOrder + 2;

        const byte FadeNone = 0;
        const byte FadeBell = 1;   // ореол: разгорается и гаснет
        const byte FadeTail = 2;   // последние 30 % жизни — ступени альфы

        struct Slot
        {
            public bool Active;
            public Sprite[] Frames;
            public float FrameTime;
            public bool Loop;
            public float T;          // < 0 — ещё ждёт задержки
            public float Life;
            public Vector3 Pos;
            public Vector3 Vel;      // клеток/с в плоскости XZ
            public float Gravity;    // вдоль −Z
            public byte Fade;
            public Color Color;
            public int Frame;
            public float Alpha;
            public Vector3 Shown;
        }

        readonly Slot[] m_Slots = new Slot[PoolSize];
        readonly SpriteRenderer[] m_Sr = new SpriteRenderer[PoolSize];
        readonly Transform[] m_Tr = new Transform[PoolSize];
        int m_Cursor;
        int m_ActiveCount;

        Sprite[] m_Spark, m_Puff, m_Coin, m_Ring, m_Smoke, m_Boom, m_Impact;
        Sprite[] m_GlowWarm, m_GlowPurple, m_GlowWhite;
        Sprite[] m_Elixir, m_Gem;

        public static Effects Create(Transform parent, Material particleMaterial)
        {
            var go = new GameObject("Effects");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<Effects>();
            fx.m_Ps = CreateChips(go, particleMaterial);
            fx.CreatePool();
            Main = fx;
            return fx;
        }

        void OnDestroy()
        {
            if (Main == this) Main = null;
        }

        // ───────────────────────── сколы ─────────────────────────

        static ParticleSystem CreateChips(GameObject go, Material particleMaterial)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.startSize = 2f * Px;
            main.startRotation = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 400;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            // Падение «вниз по экрану»: постоянная сила вдоль −Z (мировая Y сверху не видна).
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(0f);
            force.y = new ParticleSystem.MinMaxCurve(0f);
            force.z = new ParticleSystem.MinMaxCurve(-ChipGravity);
            // Размер ступенями, без плавного сжатия: целый скол, потом половинка, потом исчезает.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            // Плоские участки с крутым переходом: бесконечные касательные кривые частиц поддерживают не везде.
            var curve = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.69f, 1f, 0f, 0f),
                new Keyframe(0.7f, 0.5f, 0f, 0f), new Keyframe(1f, 0.5f, 0f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = ChipMaterial(particleMaterial);
            renderer.sortingOrder = PixelWorld.FxOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>
        /// Материал сколов: копия материала частиц из сцены (URP Particles/Unlit) — квадрат сплошного цвета вершин.
        /// Очередь прозрачных и без записи глубины: порядок над объектами задаёт sortingOrder, а не высота над полом.
        /// Спрайтовый шейдер сюда не годится: на ParticleSystemRenderer его цвет спрайта (unity_SpriteColor) не задан.
        /// </summary>
        static Material ChipMaterial(Material particleMaterial)
        {
            Material mat = null;
            if (particleMaterial != null) mat = new Material(particleMaterial);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null) mat = new Material(shader);
            }
            if (mat == null) return PixelArt.SpriteMaterial;
            mat.name = "cm_fx_chips";
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty(ZWriteId)) mat.SetFloat(ZWriteId, 0f);
            return mat;
        }

        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>Мелкие сколы при ударе: 5 штук по 2–3 пикселя арта (цвет материала цели).</summary>
        public void Chips(Vector3 pos, Color color) => Emit(pos, color, VisualSettings.Particles(5), 1.2f, 2f, 0.3f, 0.35f, 0.5f);

        /// <summary>Разлёт обломков (разрушение, объединение): count штук, быстрее и шире.</summary>
        public void Burst(Vector3 pos, Color color, int count) => Emit(pos, color, VisualSettings.Particles(count), 1.8f, 3.4f, -0.3f, 0.45f, 0.7f);

        // Контакт удара: не чаще ImpactBudget за кадр — удары многих котиков в одном кадре не сливаются во вспышку.
        const int ImpactBudget = 4;
        int m_ImpactFrame = -1, m_Impacts;

        /// <summary>Контактный акцент удара кирки в точке касания (3 кадра по 0,04 с, над объектами).</summary>
        public void Impact(Vector3 pos)
        {
            if (m_ImpactFrame != Time.frameCount)
            {
                m_ImpactFrame = Time.frameCount;
                m_Impacts = 0;
            }
            if (m_Impacts++ >= (VisualSettings.ReduceEffects ? 1 : ImpactBudget)) return;
            Spawn(m_Impact, 0.04f, false, 0f, pos, Vector3.zero, 0f, 0f, SparkOrder, 1, Color.white, FadeNone);
        }

        void Emit(Vector3 pos, Color color, int count, float minSpeed, float maxSpeed, float minUp, float minLife, float maxLife)
        {
            if (m_Ps == null || count <= 0) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var dark = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 1f);
            var light = Color.Lerp(color, Color.white, 0.3f);
            light.a = 1f;
            color.a = 1f;
            for (int i = 0; i < count; i++)
            {
                // Направление в плоскости экрана: в стороны и вверх (+Z), «падение» добавит сила вдоль −Z.
                var dir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(minUp, 1f));
                if (dir.sqrMagnitude < 1e-4f) dir.z = 1f;
                p.position = pos + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.08f, 0.12f));
                p.velocity = dir.normalized * Random.Range(minSpeed, maxSpeed);
                int shade = Random.Range(0, 3);
                p.startColor = shade == 0 ? color : (shade == 1 ? dark : light);
                // 2 или 3 пикселя арта: размер целый, без «мыльных» промежуточных.
                p.startSize = (Random.value < 0.5f ? 2f : 3f) * Px;
                p.startLifetime = Random.Range(minLife, maxLife);
                m_Ps.Emit(p, 1);
            }
        }

        // ───────────────────────── спрайтовые эффекты ─────────────────────────

        void CreatePool()
        {
            var root = PixelWorld.Visual(transform, "fxSprites");
            for (int i = 0; i < PoolSize; i++)
            {
                var sr = PixelWorld.Sprite(root, "fx", "px_white", Vector2.zero, DustOrder);
                sr.enabled = false;
                m_Sr[i] = sr;
                m_Tr[i] = sr.transform;
            }
            m_Spark = Frames("fx_spark_", 4);
            m_Puff = Frames("fx_puff_", 4);
            m_Coin = Frames("fx_coin_", 4);
            m_Ring = Frames("fx_ring_", 4);
            m_Smoke = Frames("fx_smoke_", 4);
            m_Boom = Frames("fx_boom_", 5);
            m_Impact = PixelArt.Has("fx_impact_0") ? Frames("fx_impact_", 3) : m_Spark;
            m_GlowWarm = new[] { PixelArt.World("fx_glow_warm") };
            m_GlowPurple = new[] { PixelArt.World("fx_glow_purple") };
            m_GlowWhite = new[] { PixelArt.World("fx_glow_white") };
            m_Elixir = new[] { PixelArt.World("fx_elixir") };
            m_Gem = new[] { PixelArt.World("fx_gem") };
        }

        static Sprite[] Frames(string prefix, int count)
        {
            var a = new Sprite[count];
            for (int i = 0; i < count; i++) a[i] = PixelArt.World(prefix + i);
            return a;
        }

        /// <summary>Искорки-звёздочки вокруг точки, вспыхивают по очереди (≈0,3 с каждая).</summary>
        public void Sparkle(Vector3 pos, int count = 3)
        {
            count = VisualSettings.Particles(count);
            for (int i = 0; i < count; i++)
            {
                var at = pos + new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.15f, 0.45f));
                float delay = i * 0.06f + Random.Range(0f, 0.04f);
                Spawn(m_Spark, SparkFrame, false, 0f, at, new Vector3(0f, 0f, 0.25f), 0f, delay, SparkOrder, 1, Color.white, FadeNone);
            }
        }

        /// <summary>Редкий блеск: одна звёздочка точно в точке, без разлёта (кристаллы на стенах, ≈0,3 с).</summary>
        public void Glint(Vector3 pos) =>
            Spawn(m_Spark, SparkFrame, false, 0f, pos, Vector3.zero, 0f, 0f, SparkOrder, 1, Color.white, FadeNone);

        /// <summary>Облачко пыли: два клуба расходятся в стороны и чуть вверх (≈0,32 с).</summary>
        public void Puff(Vector3 pos)
        {
            Spawn(m_Puff, PuffFrame, false, 0f, pos + new Vector3(-0.12f, 0f, 0f), new Vector3(-0.35f, 0f, 0.2f), 0f, 0f, DustOrder, 1, Color.white, FadeNone);
            Spawn(m_Puff, PuffFrame, false, 0f, pos + new Vector3(0.12f, 0f, 0.04f), new Vector3(0.35f, 0f, 0.2f), 0f, 0.03f, DustOrder, 1, Color.white, FadeNone);
        }

        /// <summary>Монетки подпрыгивают вверх по экрану, крутятся и падают обратно (≈0,75 с).</summary>
        public void CoinPop(Vector3 pos, int count = 3)
        {
            count = VisualSettings.Particles(count);
            for (int i = 0; i < count; i++)
            {
                float side = count > 1 ? Mathf.Lerp(-1f, 1f, i / (float)(count - 1)) : 0f;
                var vel = new Vector3(side * Random.Range(0.3f, 0.8f) + Random.Range(-0.15f, 0.15f), 0f, Random.Range(2.4f, 3.0f));
                Spawn(m_Coin, CoinFrame, true, CoinLife, pos, vel, CoinGravity, i * 0.05f, SparkOrder, 1, Color.white, FadeTail);
            }
        }

        /// <summary>
        /// Сбор добычи: монетки (gold), капли эликсира (elixir) или самоцветы (hard) подпрыгивают и падают, как <see cref="CoinPop"/>.
        /// </summary>
        public void LootPop(Vector3 pos, string currency, int count = 3)
        {
            if (currency == LootState.Elixir) Pop(m_Elixir, pos, count);
            else if (currency == LootState.Hard) Pop(m_Gem, pos, count);
            else CoinPop(pos, count);
        }

        void Pop(Sprite[] frames, Vector3 pos, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float side = count > 1 ? Mathf.Lerp(-1f, 1f, i / (float)(count - 1)) : 0f;
                var vel = new Vector3(side * Random.Range(0.3f, 0.8f) + Random.Range(-0.15f, 0.15f), 0f, Random.Range(2.4f, 3.0f));
                Spawn(frames, CoinLife, false, CoinLife, pos, vel, CoinGravity, i * 0.05f, SparkOrder, 1, Color.white, FadeTail);
            }
        }

        /// <summary>Расходящееся кольцо объединения на полу (4 кадра по 0,06 с).</summary>
        public void Ring(Vector3 pos) => Spawn(m_Ring, RingFrame, false, 0f, pos, Vector3.zero, 0f, 0f, RingOrder, 1, Color.white, FadeNone);

        /// <summary>Клуб дыма (труба кузницы): поднимается вверх по экрану и тает (4 кадра по 0,2 с).</summary>
        public void Smoke(Vector3 pos)
        {
            var vel = new Vector3(Random.Range(-0.1f, 0.1f), 0f, 0.5f);
            Spawn(m_Smoke, SmokeFrame, false, 0f, pos, vel, 0f, 0f, DustOrder, 1, Color.white, FadeNone);
        }

        /// <summary>Взрыв динамита (5 кадров по 0,07 с).</summary>
        public void Boom(Vector3 pos) => Spawn(m_Boom, BoomFrame, false, 0f, pos, Vector3.zero, 0f, 0f, BoomOrder, 1, Color.white, FadeNone);

        /// <summary>
        /// Мягкий ореол (свет в новом секторе и т. п.): разгорается и гаснет колоколом за <paramref name="seconds"/>,
        /// радиус — <paramref name="radiusCells"/> (целый масштаб спрайта 32×32). Тёплые и фиолетовые цвета берут свои
        /// спрайты, остальные — белый ореол, тонированный цветом.
        /// </summary>
        public void Glow(Vector3 pos, Color color, float radiusCells, float seconds)
        {
            Color.RGBToHSV(color, out float h, out float s, out _);
            Sprite[] frames;
            Color tint = Color.white;
            if (s < 0.2f) frames = m_GlowWhite;
            else if (h < 0.17f || h > 0.95f) frames = m_GlowWarm;
            else if (h > 0.68f) frames = m_GlowPurple;
            else
            {
                frames = m_GlowWhite;
                tint = color;
                tint.a = 1f;
            }
            int scale = Mathf.Max(1, Mathf.RoundToInt(radiusCells * 2f));
            Spawn(frames, 1f, true, Mathf.Max(0.1f, seconds), pos, Vector3.zero, 0f, 0f, GlowOrder, scale, tint, FadeBell);
        }

        void Spawn(Sprite[] frames, float frameTime, bool loop, float life, Vector3 pos, Vector3 vel, float gravity, float delay,
            int order, int scale, Color color, byte fade)
        {
            if (frames == null || frames.Length == 0 || m_Sr[0] == null) return;
            int i = FreeSlot();
            ref var s = ref m_Slots[i];
            if (!s.Active) m_ActiveCount++;
            s.Active = true;
            s.Frames = frames;
            s.FrameTime = frameTime;
            s.Loop = loop;
            s.T = -delay;
            s.Life = life > 0f ? life : frames.Length * frameTime;
            s.Pos = pos;
            s.Vel = vel;
            s.Gravity = gravity;
            s.Fade = fade;
            s.Color = color;
            s.Frame = 0;
            s.Alpha = -1f;
            s.Shown = PixelWorld.Snap(pos);
            var sr = m_Sr[i];
            sr.sprite = frames[0];
            sr.sortingOrder = order;
            sr.color = fade == FadeBell ? new Color(color.r, color.g, color.b, 0f) : color;
            sr.enabled = delay <= 0f;
            m_Tr[i].localScale = new Vector3(scale, scale, 1f);
            m_Tr[i].position = s.Shown;
        }

        /// <summary>Свободный слот пула; если все заняты — самый старый по кругу (эффект обрывается, без выделений).</summary>
        int FreeSlot()
        {
            for (int k = 0; k < PoolSize; k++)
            {
                int i = (m_Cursor + k) % PoolSize;
                if (!m_Slots[i].Active)
                {
                    m_Cursor = (i + 1) % PoolSize;
                    return i;
                }
            }
            int stolen = m_Cursor;
            m_Cursor = (m_Cursor + 1) % PoolSize;
            return stolen;
        }

        void Update()
        {
            if (m_ActiveCount == 0) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < PoolSize; i++)
            {
                ref var s = ref m_Slots[i];
                if (!s.Active) continue;
                s.T += dt;
                if (s.T < 0f) continue;
                var sr = m_Sr[i];
                if (s.T >= s.Life || sr == null)
                {
                    s.Active = false;
                    s.Frames = null;
                    if (sr != null) sr.enabled = false;
                    m_ActiveCount--;
                    continue;
                }
                if (!sr.enabled) sr.enabled = true;

                int n = s.Frames.Length;
                int f = (int)(s.T / s.FrameTime);
                f = s.Loop ? f % n : Mathf.Min(f, n - 1);
                if (f != s.Frame)
                {
                    s.Frame = f;
                    sr.sprite = s.Frames[f];
                }

                // Положение по формуле от старта (без накопления ошибки), снап к сетке пикселей арта.
                float t = s.T;
                var p = s.Pos + s.Vel * t;
                p.z -= 0.5f * s.Gravity * t * t;
                p = PixelWorld.Snap(p);
                if (p != s.Shown)
                {
                    s.Shown = p;
                    m_Tr[i].position = p;
                }

                if (s.Fade != FadeNone)
                {
                    float u = s.T / s.Life;
                    float a;
                    if (s.Fade == FadeBell)
                        a = u < 0.35f ? Mathf.SmoothStep(0f, 1f, u / 0.35f) : Mathf.SmoothStep(1f, 0f, (u - 0.35f) / 0.65f);
                    else
                        a = u < 0.7f ? 1f : (u < 0.85f ? 0.66f : 0.33f);
                    // Альфа ступенями по 1/8: пиксельный стиль и меньше обновлений цвета.
                    a = Mathf.Round(a * 8f) / 8f;
                    if (a != s.Alpha)
                    {
                        s.Alpha = a;
                        var c = s.Color;
                        c.a *= a;
                        sr.color = c;
                    }
                }
            }
        }
    }
}
