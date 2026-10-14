using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Шаг кадра для анимаций представления. В игре — обычное <see cref="Time.unscaledDeltaTime"/>. Когда запись видео
    /// фиксирует <see cref="Time.captureDeltaTime"/> (рекламная съёмка, AdCaptureTests), каждый кадр — ровно этот шаг:
    /// позы котиков, полёт мешков и монет, всплытие окон и камера идут вровень с ядром, сколько бы ни длился рендер кадра.
    /// </summary>
    public static class FrameTime
    {
        public static float Delta => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
    }
}
