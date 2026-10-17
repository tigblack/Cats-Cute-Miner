using CatMiners.Core.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>
    /// Бочка динамита на поле (ТЗ 10.6): летит дугой из дула мортиры в свою клетку, приземляется со сплющиванием и пылью, затем
    /// ждёт тапа — фитиль искрит (barrel_tnt_0/1), бочка подпрыгивает, над ней «Tap!». Тап по коллайдеру (<see cref="OutpostPickKind.Dynamite"/>)
    /// — команда Detonate; взрыв показывает <see cref="Explode"/>. Только вид: место и урон — ядро.
    /// </summary>
    public sealed class DynamiteBarrelView : MonoBehaviour
    {
        const float Px = 1f / PixelArt.CellPixels;
        static readonly string[] s_Frames = { "barrel_tnt_0", "barrel_tnt_1" };
        const float FuseFrameSeconds = 0.12f;
        const float FlyBaseSeconds = 0.55f, FlySecondsPerCell = 0.05f, FlyMinSeconds = 0.6f, FlyMaxSeconds = 1.4f;
        const float FlyBaseHeight = 1.5f, FlyHeightPerCell = 0.18f, FlyMinHeight = 2f, FlyMaxHeight = 6f;
        const float LandSeconds = 0.3f, HopPeriod = 0.9f;
        const int LayerShadow = 0, LayerBody = 10;

        SortingGroup m_Group;
        Transform m_Visual, m_Body;
        SpriteRenderer m_Sprite, m_Shadow;
        BoxCollider m_Collider;
        Label3D m_Tap;
        int m_RestOrder;
        float m_FlyT = 1f, m_FlyTime, m_FlySeconds, m_FlyHeight, m_LandT = 1f, m_Time;
        Vector3 m_FlyFrom, m_FlyGround;
        System.Action<DynamiteBarrelView> m_Landed;

        /// <summary>Номер бочки в шахте (ключ ядра).</summary>
        public long Seq { get; private set; }
        public bool Flying => m_FlyT < 1f;

        public static DynamiteBarrelView Create(Transform parent, Vector3 localRest, long seq)
        {
            var root = new GameObject("Dynamite_" + seq);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = PixelWorld.Snap(localRest);
            var v = root.AddComponent<DynamiteBarrelView>();
            v.Seq = seq;
            v.m_Group = PixelWorld.Group(root, root.transform.position.z);
            v.m_RestOrder = v.m_Group.sortingOrder;
            v.m_Visual = PixelWorld.Visual(root.transform);
            v.m_Shadow = PixelWorld.Sprite(v.m_Visual, "shadow", "shadow_s", new Vector2(0f, 2f * Px), LayerShadow);
            v.m_Body = new GameObject("body").transform;
            v.m_Body.SetParent(v.m_Visual, false);
            v.m_Sprite = PixelWorld.Sprite(v.m_Body, "sprite", s_Frames[0], Vector2.zero, LayerBody);
            v.m_Tap = Label3D.Create(root.transform, new Vector3(0f, 0.1f, 0.95f), Label3D.SmallSize, Palette.Gold);
            v.m_Tap.Text.text = Loc.T("outpost.bonus.tap");
            // Коллайдер с запасом под палец: клетка вокруг бочки.
            v.m_Collider = root.AddComponent<BoxCollider>();
            v.m_Collider.center = new Vector3(0f, 0.4f, 0.35f);
            v.m_Collider.size = new Vector3(1.1f, 0.8f, 1.1f);
            OutpostPickable.Add(root, OutpostPickKind.Dynamite, (int)seq);
            return v;
        }

        /// <summary>
        /// Вылететь из мортиры: дугой из <paramref name="muzzle"/> в свою клетку, <paramref name="ground"/> — пол под дулом (от него
        /// тень). Пока летит, тапа и подписи нет; по приземлении — <paramref name="landed"/>.
        /// </summary>
        public void Launch(Vector3 muzzle, Vector3 ground, System.Action<DynamiteBarrelView> landed)
        {
            var rest = transform.position;
            float dist = Vector2.Distance(new Vector2(ground.x, ground.z), new Vector2(rest.x, rest.z));
            m_FlySeconds = Mathf.Clamp(FlyBaseSeconds + FlySecondsPerCell * dist, FlyMinSeconds, FlyMaxSeconds);
            m_FlyHeight = Mathf.Clamp(FlyBaseHeight + FlyHeightPerCell * dist, FlyMinHeight, FlyMaxHeight);
            m_FlyFrom = muzzle - rest;
            m_FlyGround = ground - rest;
            m_FlyT = 0f;
            m_FlyTime = 0f;
            m_LandT = 1f;
            m_Landed = landed;
            m_Collider.enabled = false;
            m_Tap.gameObject.SetActive(false);
            m_Group.sortingOrder = PixelWorld.OverlayBase - 1;
            UpdateFlight(0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Time += dt;
            if (m_FlyT < 1f)
            {
                UpdateFlight(dt);
                return;
            }
            PixelWorld.SetSprite(m_Sprite, s_Frames[(int)(m_Time / FuseFrameSeconds) & 1]);
            float sx = 1f, sy = 1f, hop = 0f;
            if (m_LandT < 1f)
            {
                m_LandT = Mathf.Min(1f, m_LandT + dt / LandSeconds);
                float a = m_LandT;
                sx = Mathf.Lerp(1.3f, 1f, a);
                sy = Mathf.Lerp(0.75f, 1f, a);
            }
            else
            {
                // Подпрыгивает раз в период: зовёт тапнуть.
                float u = (m_Time % HopPeriod) / HopPeriod;
                if (u < 0.25f) hop = Mathf.Round(Mathf.Sin(u / 0.25f * Mathf.PI) * 3f) * Px;
            }
            m_Body.localPosition = new Vector3(0f, hop, 0f);
            m_Body.localScale = new Vector3(sx, sy, 1f);
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
            m_Body.localPosition = new Vector3(PixelWorld.Snap(m_FlyFrom.x * (1f - u)), PixelWorld.Snap(gz + h), 0f);
            m_Body.localScale = Vector3.one;
            PixelWorld.SetSprite(m_Sprite, s_Frames[(int)(m_FlyTime / FuseFrameSeconds) & 1]);
            m_Shadow.transform.localPosition = new Vector3(PixelWorld.Snap(gx), 2f * Px + PixelWorld.Snap(gz), 0f);
            m_Shadow.enabled = u > 0.15f;
        }

        void Land()
        {
            m_FlyT = 1f;
            m_LandT = 0f;
            m_Body.localPosition = Vector3.zero;
            m_Shadow.transform.localPosition = new Vector3(0f, 2f * Px, 0f);
            m_Shadow.enabled = true;
            m_Group.sortingOrder = m_RestOrder;
            m_Collider.enabled = true;
            m_Tap.gameObject.SetActive(true);
            var landed = m_Landed;
            m_Landed = null;
            landed?.Invoke(this);
        }

        /// <summary>Сразу на месте, без полёта (загрузка сохранения с бочкой на поле).</summary>
        public void PlaceLanded()
        {
            m_FlyT = 1f;
            m_LandT = 1f;
            m_Body.localPosition = Vector3.zero;
            m_Shadow.enabled = true;
            m_Collider.enabled = true;
            m_Tap.gameObject.SetActive(true);
        }

        /// <summary>Взрыв: кадры fx_boom, дым и искры на месте бочки, толчок камеры; объект удаляется.</summary>
        public void Explode(Effects fx, CameraRig rig, bool reduceEffects)
        {
            var at = transform.position + new Vector3(0f, 0f, 0.4f);
            if (fx != null)
            {
                fx.Boom(at);
                fx.Smoke(at + new Vector3(0f, 0f, 0.3f));
                fx.Burst(at, Palette.Gold, 10);
            }
            if (rig != null && !reduceEffects) rig.Kick(3f);
            Destroy(gameObject);
        }
    }
}
