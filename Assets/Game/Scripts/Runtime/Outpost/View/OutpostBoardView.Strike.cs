using System;
using CatMiners.Core.Outpost;
using UnityEngine;

namespace CatMiners.Runtime.Outpost
{
    // Удар кирки игрока (ТЗ 6.6, 16): тап → кирка из воздуха → в кадр удара команда ядра → отклик цели. Вид ничего не начисляет:
    // урон и порции даёт команда Strike, груз ложится на землю и приходит на склад с котиками.
    public sealed partial class OutpostBoardView
    {
        Func<int, Vector3, bool> m_OnStrikeImpact;
        Action<Vector3> m_OnStrikeVanish;

        /// <summary>
        /// Обработчик кадра удара: выполнить команду Strike по цели и вернуть успех (контроллер). null — удар только показывается
        /// промахом (тесты вида).
        /// </summary>
        public Func<int, bool> StrikeHandler { get; set; }

        /// <summary>Активных анимаций удара (тесты, отладка).</summary>
        public int ActiveStrikes => m_Strikes != null ? m_Strikes.ActiveCount : 0;

        /// <summary>
        /// Показать удар кирки игрока по цели в мировой точке касания <paramref name="world"/> (кончик кирки). У правого края
        /// поля замах зеркальный, чтобы кирка не уходила за стену. Урон — в кадр удара через <see cref="StrikeHandler"/>.
        /// </summary>
        public void PlayStrike(int targetId, Vector3 world)
        {
            if (m_Strikes == null || m_Map == null) return;
            var local = world - Origin;
            var tv = TargetViewOf(targetId);
            if (tv != null)
            {
                // Касание держится в пределах спрайта цели: от ног до верхней кромки, по ширине footprint.
                float half = tv.Width / 2f - 2f * Px;
                local.x = Mathf.Clamp(local.x, tv.FeetLocal.x - half, tv.FeetLocal.x + half);
                local.z = Mathf.Clamp(local.z, tv.FeetLocal.z + 4f * Px, tv.FeetLocal.z + Mathf.Max(6f * Px, tv.TopZ - 4f * Px));
            }
            local.y = 0f;
            bool flip = local.x > m_Map.Width / 2f + 0.5f;
            m_Strikes.Launch(targetId, local, flip);
            if (Fx != null && TakeFx()) Fx.Sparkle(Origin + local + new Vector3(flip ? -0.3f : 0.3f, 0f, 0.55f), 2);
        }

        void TickStrikes(float dt)
        {
            if (m_Strikes == null || m_Strikes.ActiveCount == 0) return;
            if (m_OnStrikeImpact == null) m_OnStrikeImpact = OnStrikeImpact;
            if (m_OnStrikeVanish == null) m_OnStrikeVanish = OnStrikeVanish;
            m_Strikes.Tick(dt, m_OnStrikeImpact, m_OnStrikeVanish);
        }

        bool OnStrikeImpact(int targetId, Vector3 local)
        {
            bool ok = StrikeHandler != null && StrikeHandler(targetId);
            var fx = Fx;
            var at = Origin + local;
            if (!ok)
            {
                // Цель успели разрушить котики или удар запрещён: кирка бьёт в пыль.
                if (fx != null && TakeFx()) fx.Puff(at);
                return false;
            }
            var tv = TargetViewOf(targetId);
            if (tv != null)
            {
                bool budget = fx != null && TakeFx();
                tv.OnHit(budget ? fx : null, at + new Vector3(0f, 0f, 2f * Px));
                if (tv.Kind == 'p')
                    for (int k = 0; k < m_Barriers.Length; k++)
                        if (m_Barriers[k].TargetId == targetId) m_Barriers[k].OnHit();
            }
            // Вспышка контакта — без бюджета кадра: это прямой отклик на касание игрока (Effects ограничивает частоту сам).
            fx?.Impact(PixelWorld.Snap(at));
            if (Cue != null)
            {
                var args = OutpostViewCueArgs.Create(OutpostViewCue.PlayerStrike, at);
                args.TargetId = targetId;
                args.Material = tv != null ? tv.Material : HitMaterial.Stone;
                Cue(args);
            }
            return true;
        }

        void OnStrikeVanish(Vector3 local)
        {
            if (Fx != null && TakeFx()) Fx.Sparkle(Origin + local + new Vector3(0f, 0f, 0.35f), 1);
        }
    }
}
