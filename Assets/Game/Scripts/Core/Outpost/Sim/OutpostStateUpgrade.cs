using System;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Дополнение снимков, записанных до первой сессии (Docs/Rework/FIRST_SESSION.md), без смены схемы сохранения: массив уровней
    /// получает уровень ветки «Мест разгрузки» (0), профиль — пустой прогресс первой сессии (Version 0, ждёт миграции). Снимок
    /// баланса run не трогается: шахта, начатая до мест разгрузки, доигрывается без этой ветки. Идемпотентно.
    /// </summary>
    public static class OutpostStateUpgrade
    {
        /// <summary>true — снимок изменён (его нужно записать, если загрузка его приняла).</summary>
        public static bool Normalize(OutpostGameState state)
        {
            if (state == null) return false;
            bool changed = NormalizeRun(state.Run);
            if (state.Profile != null && state.Profile.Progression == null)
            {
                state.Profile.Progression = new ProgressionState();
                changed = true;
            }
            return changed;
        }

        public static bool NormalizeRun(OutpostRun run)
        {
            if (run == null) return false;
            if (run.Levels == null)
            {
                run.Levels = new int[OutpostIds.BranchCount];
                return true;
            }
            if (run.Levels.Length >= OutpostIds.BranchCount) return false;
            var levels = run.Levels;
            Array.Resize(ref levels, OutpostIds.BranchCount);
            run.Levels = levels;
            return true;
        }
    }
}
