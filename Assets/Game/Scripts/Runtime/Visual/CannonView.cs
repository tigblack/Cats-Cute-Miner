using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Пушка (ТЗ 6.4, 7.2, Docs/Art/PIXEL_STYLE_GUIDE.md): из неё вылетают бочки с котиками — нанятыми и бесплатными.
    /// Стоит у правого края поля на клетках ZoneLayout.CannonCol/CannonRow (1×2) сразу под зоной мержа, корень — в опоре
    /// (нижняя кромка footprint). Арт смотрит вверх-вправо (ствол ≈48°); пушка, стоящая правее середины поля, отражена
    /// по X и смотрит влево, на зону мержа; к цели по другую сторону она разворачивается. Слои одной группы сортировки:
    /// тень → лафет → ствол → ближнее колесо → вспышка залпа.
    /// Выстрел (<see cref="Fire"/>): пушка откатывается на пиксель арта, ствол — ещё на 2 вдоль оси, колесо
    /// проворачивается; из дула — вспышка fx_muzzle (3 кадра по 0,05 с), клубы дыма и пыль у колёс; потом всё мягко
    /// возвращается. Анимация целыми пикселями, без выделений памяти в кадре.
    /// </summary>
    public sealed class CannonView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>
        /// Центр дульного среза — точка холста 32×72 от левого верхнего угла (Tools/PixelArt/sprites/structures.py, K_M).
        /// При перерисовке пушки сверить; холст и опора (16, 0) — из манифеста.
        /// </summary>
        static readonly Vector2 MuzzleCanvas = new Vector2(26.5f, 33.5f);
        const float CanvasW = 32f, CanvasH = 72f;
        /// <summary>Ось ствола на экране (вверх — +Y), от казны к дулу: K_M − K_B = (16, −18) на холсте.</summary>
        static readonly Vector2 Axis = new Vector2(16f, 18f).normalized;

        static readonly string[] s_FlashFrames = { "fx_muzzle_0", "fx_muzzle_1", "fx_muzzle_2" };
        static readonly string[] s_WheelFrames = { "cannon_wheel_0", "cannon_wheel_1" };
        const float FlashFrameSeconds = 0.05f;
        /// <summary>Откат — резко, возврат — мягко.</summary>
        const float KickSeconds = 0.06f, ReturnSeconds = 0.32f;
        /// <summary>Второй клуб дыма — чуть позже первого.</summary>
        const float SecondSmokeAt = 0.12f;
        /// <summary>Вспышка — на 4 пикселя арта впереди дульного среза.</summary>
        const float FlashAheadPx = 4f;

        const int LayerShadow = 0, LayerBase = 10, LayerBarrel = 11, LayerWheel = 12, LayerFlash = 13;

        Transform m_Body;           // лафет, ствол и колесо: откат на пиксель
        Transform m_Barrel;         // ствол: ещё 2 пикселя вдоль оси
        SpriteRenderer m_Base, m_BarrelSr, m_Wheel, m_Flash;
        Effects m_Fx;
        bool m_Left;
        float m_T = -1f;            // время с выстрела; < 0 — покой
        bool m_SecondSmoke;
        Vector2Int m_BodyPx, m_BarrelPx;
        int m_WheelFrame, m_FlashFrame = -1;

        /// <summary>Пушка смотрит влево (отражена по X).</summary>
        public bool FacingLeft => m_Left;

        /// <summary>Выстрелов с создания (тесты, отладка).</summary>
        public int Shots { get; private set; }

        /// <summary>Центр дульного среза в мире при текущем развороте.</summary>
        public Vector3 Muzzle => transform.position + Local(MuzzleCanvas);

        /// <summary>Точка пола под дулом: отсюда бочка «взлетает» (тень полёта), высота дула над ней — в дуге полёта.</summary>
        public Vector3 MuzzleGround
        {
            get
            {
                var m = Muzzle;
                return new Vector3(m.x, 0f, transform.position.z + 0.5f);
            }
        }

        /// <param name="feet">Опора: x — середина столбца пушки, z — нижняя кромка её нижней клетки.</param>
        /// <param name="faceLeft">Смотреть влево (пушка у правого края поля).</param>
        public static CannonView Create(Transform parent, Vector3 feet, bool faceLeft)
        {
            var root = new GameObject("Cannon");
            root.transform.SetParent(parent, false);
            root.transform.position = PixelWorld.Snap(feet);
            var v = root.AddComponent<CannonView>();
            PixelWorld.Group(root, feet.z);
            var visual = PixelWorld.Visual(root.transform);
            PixelWorld.Sprite(visual, "shadow", "shadow_m", new Vector2(0f, 3f * Px), LayerShadow);
            v.m_Body = new GameObject("body").transform;
            v.m_Body.SetParent(visual, false);
            v.m_Base = PixelWorld.Sprite(v.m_Body, "base", "cannon_base", Vector2.zero, LayerBase);
            v.m_Barrel = new GameObject("barrel").transform;
            v.m_Barrel.SetParent(v.m_Body, false);
            v.m_BarrelSr = PixelWorld.Sprite(v.m_Barrel, "sprite", "cannon_barrel", Vector2.zero, LayerBarrel);
            v.m_Wheel = PixelWorld.Sprite(v.m_Body, "wheel", s_WheelFrames[0], Vector2.zero, LayerWheel);
            v.m_Flash = PixelWorld.Sprite(visual, "flash", s_FlashFrames[0], Vector2.zero, LayerFlash);
            v.m_Flash.enabled = false;
            v.Face(faceLeft);
            return v;
        }

        /// <summary>Смещение точки холста пушки от опоры (клетки, x вправо, z вверх по экрану) с учётом разворота.</summary>
        Vector3 Local(Vector2 canvas)
        {
            float x = (canvas.x - CanvasW / 2f) * Px;
            return new Vector3(m_Left ? -x : x, 0f, (CanvasH - canvas.y) * Px);
        }

        /// <summary>
        /// Выстрел: развернуться к цели (<paramref name="left"/> — цель левее пушки), откат, вспышка, дым и пыль у колёс.
        /// Повторный выстрел до конца отката начинает анимацию заново.
        /// </summary>
        public void Fire(bool left, Effects fx)
        {
            Shots++;
            m_Fx = fx;
            Face(left);
            m_T = 0f;
            m_SecondSmoke = false;
            m_FlashFrame = -1;
            var ahead = Muzzle + new Vector3((m_Left ? -Axis.x : Axis.x) * FlashAheadPx * Px, 0f, Axis.y * FlashAheadPx * Px);
            m_Flash.transform.localPosition = new Vector3(PixelWorld.Snap(ahead.x - transform.position.x), PixelWorld.Snap(ahead.z - transform.position.z), 0f);
            m_Flash.enabled = true;
            if (fx != null)
            {
                fx.Smoke(ahead);
                // Пыль из-под колёс: пушка дёргается назад.
                fx.Puff(transform.position + new Vector3(m_Left ? 0.18f : -0.18f, 0f, 0.12f));
            }
            Apply(0f);
        }

        void Update()
        {
            if (m_T < 0f) return;
            m_T += Time.deltaTime;
            if (!m_SecondSmoke && m_T >= SecondSmokeAt)
            {
                m_SecondSmoke = true;
                if (m_Fx != null) m_Fx.Smoke(Muzzle + new Vector3((m_Left ? -Axis.x : Axis.x) * 0.3f, 0f, Axis.y * 0.3f));
            }
            int frame = (int)(m_T / FlashFrameSeconds);
            if (frame != m_FlashFrame)
            {
                m_FlashFrame = frame;
                bool on = frame < s_FlashFrames.Length;
                if (m_Flash.enabled != on) m_Flash.enabled = on;
                if (on) PixelWorld.SetSprite(m_Flash, s_FlashFrames[frame]);
            }
            float kick;
            if (m_T < KickSeconds) kick = m_T / KickSeconds;
            else
            {
                float u = Mathf.Min(1f, (m_T - KickSeconds) / ReturnSeconds);
                kick = 1f - u * u * (3f - 2f * u);
            }
            Apply(kick);
            if (m_T >= KickSeconds + ReturnSeconds && frame >= s_FlashFrames.Length)
            {
                m_T = -1f;
                Apply(0f);
            }
        }

        void Face(bool left)
        {
            m_Left = left;
            m_Base.flipX = m_BarrelSr.flipX = m_Wheel.flipX = left;
            m_BodyPx = m_BarrelPx = new Vector2Int(int.MinValue, 0);   // откат пересчитается для нового разворота
            Apply(0f);
        }

        /// <summary>Откат целыми пикселями: <paramref name="kick"/> 1 — крайняя точка.</summary>
        void Apply(float kick)
        {
            int side = m_Left ? 1 : -1;     // назад — от цели
            var body = new Vector2Int(Mathf.RoundToInt(side * kick), 0);
            var barrel = new Vector2Int(Mathf.RoundToInt(side * Axis.x * 2f * kick), -Mathf.RoundToInt(Axis.y * 2f * kick));
            if (body != m_BodyPx)
            {
                m_BodyPx = body;
                m_Body.localPosition = new Vector3(body.x * Px, body.y * Px, 0f);
            }
            if (barrel != m_BarrelPx)
            {
                m_BarrelPx = barrel;
                m_Barrel.localPosition = new Vector3(barrel.x * Px, barrel.y * Px, 0f);
            }
            int wheel = kick > 0.35f ? 1 : 0;
            if (wheel != m_WheelFrame)
            {
                m_WheelFrame = wheel;
                PixelWorld.SetSprite(m_Wheel, s_WheelFrames[wheel]);
            }
        }
    }
}
