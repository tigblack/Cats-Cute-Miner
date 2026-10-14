namespace CatMiners.Runtime
{
    /// <summary>
    /// Настройки вида из настроек игрока (PlayerSettingsState.ReduceEffects): «меньше эффектов» — частицы и искры реже и меньше,
    /// без импульса камеры на крупных событиях. Применяет GameController при выборе сессии и из окна настроек.
    /// </summary>
    public static class VisualSettings
    {
        public static bool ReduceEffects;

        /// <summary>Число частиц с учётом «меньше эффектов» (не меньше одной).</summary>
        public static int Particles(int count) => ReduceEffects ? (count > 1 ? (count + 1) / 2 : count) : count;
    }
}
