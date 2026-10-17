using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    // Бочка динамита из мортиры лагеря (ТЗ 10.6): выстрел и полёт по событию DynamiteLaunched, взрыв — DynamiteExploded.
    // Sync восстанавливает бочку без полёта после загрузки или перестройки поля. Урон и место — ядро.
    public sealed partial class OutpostBoardView
    {
        DynamiteBarrelView m_Dynamite;
        System.Action<DynamiteBarrelView> m_OnDynamiteLanded;

        /// <summary>Камера для толчка при взрыве (задаёт контроллер); null — без толчка.</summary>
        public CameraRig ShakeRig { get; set; }

        /// <summary>Бочка динамита на поле (тесты, съёмка).</summary>
        public DynamiteBarrelView Dynamite => m_Dynamite;

        /// <summary>Мировая точка центра клетки поля.</summary>
        public Vector3 CellWorld(int cell) => Origin + CellCenterLocal(cell);

        /// <summary>Опора бочки в клетке: чуть ниже центра, чтобы бочка стояла в своей клетке.</summary>
        Vector3 DynamiteLocal(int cell) => CellCenterLocal(cell) + new Vector3(0f, 0f, -0.3f);

        void LaunchDynamite(OutpostEvent e, Effects fx)
        {
            RemoveDynamite();
            if (e.Cell < 0 || m_Root == null) return;
            m_Dynamite = DynamiteBarrelView.Create(m_Root, DynamiteLocal(e.Cell), e.BatchSeq);
            var mortar = m_Camp != null ? m_Camp.Mortar : null;
            if (!m_Live || mortar == null)
            {
                m_Dynamite.PlaceLanded();
                return;
            }
            mortar.Fire(fx);
            Emit(OutpostViewCue.MortarFire, mortar.transform.position - Origin);
            if (m_OnDynamiteLanded == null) m_OnDynamiteLanded = OnDynamiteLanded;
            m_Dynamite.Launch(mortar.Muzzle, mortar.MuzzleGround, m_OnDynamiteLanded);
        }

        void OnDynamiteLanded(DynamiteBarrelView v)
        {
            if (v == null) return;
            Emit(OutpostViewCue.BarrelLand, v.transform.position - Origin);
            Fx?.Puff(v.transform.position + new Vector3(0f, 0f, 0.1f));
        }

        void ExplodeDynamite(OutpostEvent e, Effects fx)
        {
            var v = m_Dynamite;
            m_Dynamite = null;
            var at = v != null ? v.transform.position : (e.Cell >= 0 ? Origin + DynamiteLocal(e.Cell) : Origin);
            Emit(OutpostViewCue.DynamiteBlast, at - Origin);
            if (v != null) v.Explode(fx, ShakeRig, VisualSettings.ReduceEffects);
            else if (fx != null) fx.Boom(at + new Vector3(0f, 0f, 0.4f));
        }

        /// <summary>
        /// Бочка ядра без вида (загрузка, перестройка поля, потерянное событие) — ставится на место без полёта; вид без бочки
        /// в ядре (переход шахты) — убирается. Свежий выстрел ждёт своего события DynamiteLaunched (полёт из мортиры).
        /// </summary>
        void SyncDynamite(OutpostRun run)
        {
            var barrel = run.Dynamite;
            if (barrel == null)
            {
                if (m_Dynamite != null && !m_Dynamite.Flying) RemoveDynamite();
                return;
            }
            if (m_Dynamite != null && m_Dynamite.Seq == barrel.Seq) return;
            if (m_Live && run.Now - barrel.LaunchedTick < 2 * OutpostIds.TicksPerSecond) return;
            RemoveDynamite();
            m_Dynamite = DynamiteBarrelView.Create(m_Root, DynamiteLocal(barrel.Cell), barrel.Seq);
            m_Dynamite.PlaceLanded();
        }

        void RemoveDynamite()
        {
            if (m_Dynamite != null) Destroy(m_Dynamite.gameObject);
            m_Dynamite = null;
        }
    }
}
