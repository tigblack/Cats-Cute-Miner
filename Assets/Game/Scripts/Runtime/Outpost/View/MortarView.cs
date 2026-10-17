using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Мортира лагеря (ТЗ 10.6, Docs/Art/PIXEL_STYLE_GUIDE.md): из неё вылетает бочка динамита — награда бонуса за рекламу.
    /// Слои одной группы сортировки: тень → колода (mortar_base) → ствол (mortar_tube, общий холст и опора) → вспышка залпа.
    /// Выстрел (<see cref="Fire"/>): ствол оседает на 2 пикселя арта, колода — на 1, вспышка fx_muzzle (3 кадра по 0,05 с) и клубы
    /// дыма над дулом, пыль у основания; потом всё мягко возвращается. Анимация целыми пикселями, без выделений памяти в кадре.
    /// </summary>
    public sealed class MortarView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        /// <summary>
        /// Центр дульного среза — точка холста от левого верхнего угла (Tools/PixelArt/sprites/structures.py, build_mortar);
        /// холст общий у mortar_base и mortar_tube, опора — середина нижней кромки. При перерисовке сверить с манифестом.
        /// </summary>
        static readonly Vector2 MuzzleCanvas = new Vector2(15f, 4f);
        static readonly Vector2 Canvas = new Vector2(28f, 24f);

        static readonly string[] s_FlashFrames = { "fx_muzzle_0", "fx_muzzle_1", "fx_muzzle_2" };
        const float FlashFrameSeconds = 0.05f;
        const float KickSeconds = 0.06f, ReturnSeconds = 0.3f, SecondSmokeAt = 0.12f;
        const int LayerShadow = 0, LayerBase = 10, LayerTube = 11, LayerFlash = 13;

        Transform m_Body, m_Tube;
        SpriteRenderer m_Flash;
        Effects m_Fx;
        float m_T = -1f;
        bool m_SecondSmoke;
        int m_BodyPx = int.MinValue, m_TubePx = int.MinValue, m_FlashFrame = -1;

        /// <summary>Выстрелов с создания (тесты, отладка).</summary>
        public int Shots { get; private set; }

        /// <summary>Центр дульного среза в мире.</summary>
        public Vector3 Muzzle => transform.position + new Vector3((MuzzleCanvas.x - Canvas.x / 2f) * Px, 0f, (Canvas.y - MuzzleCanvas.y) * Px);

        /// <summary>Точка пола под дулом: отсюда бочка «взлетает» (тень полёта).</summary>
        public Vector3 MuzzleGround => new Vector3(Muzzle.x, 0f, transform.position.z + 0.3f);

        /// <param name="feet">Опора: середина клетки, нижняя кромка мортиры.</param>
        public static MortarView Create(Transform parent, Vector3 localFeet)
        {
            var root = new GameObject("Mortar");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = PixelWorld.Snap(localFeet);
            var v = root.AddComponent<MortarView>();
            PixelWorld.Group(root, root.transform.position.z);
            var visual = PixelWorld.Visual(root.transform);
            PixelWorld.Sprite(visual, "shadow", "shadow_m", new Vector2(0f, 2f * Px), LayerShadow);
            v.m_Body = new GameObject("body").transform;
            v.m_Body.SetParent(visual, false);
            PixelWorld.Sprite(v.m_Body, "base", "mortar_base", Vector2.zero, LayerBase);
            v.m_Tube = new GameObject("tube").transform;
            v.m_Tube.SetParent(v.m_Body, false);
            PixelWorld.Sprite(v.m_Tube, "sprite", "mortar_tube", Vector2.zero, LayerTube);
            v.m_Flash = PixelWorld.Sprite(visual, "flash", s_FlashFrames[0], Vector2.zero, LayerFlash);
            v.m_Flash.enabled = false;
            return v;
        }

        /// <summary>Выстрел: оседание, вспышка, дым над дулом и пыль у колоды. Повторный выстрел начинает анимацию заново.</summary>
        public void Fire(Effects fx)
        {
            Shots++;
            m_Fx = fx;
            m_T = 0f;
            m_SecondSmoke = false;
            m_FlashFrame = -1;
            var ahead = Muzzle + new Vector3(0f, 0f, 3f * Px);
            m_Flash.transform.localPosition = new Vector3(PixelWorld.Snap(ahead.x - transform.position.x), PixelWorld.Snap(ahead.z - transform.position.z), 0f);
            m_Flash.enabled = true;
            if (fx != null)
            {
                fx.Smoke(ahead);
                fx.Puff(transform.position + new Vector3(0f, 0f, 0.1f));
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
                if (m_Fx != null) m_Fx.Smoke(Muzzle + new Vector3(0f, 0f, 0.35f));
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

        /// <summary>Отдача вниз целыми пикселями: <paramref name="kick"/> 1 — крайняя точка.</summary>
        void Apply(float kick)
        {
            int body = -Mathf.RoundToInt(kick);
            int tube = -Mathf.RoundToInt(2f * kick);
            if (body != m_BodyPx)
            {
                m_BodyPx = body;
                m_Body.localPosition = new Vector3(0f, body * Px, 0f);
            }
            if (tube != m_TubePx)
            {
                m_TubePx = tube;
                m_Tube.localPosition = new Vector3(0f, (tube - body) * Px, 0f);
            }
        }
    }
}
