using System;
using CatMiners.Core.Sim;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Короткая вибрация (ТЗ 13.1, настройка «Вибрация» в PlayerProgressState по ТЗ 11.3). Работает только на Android:
    /// Vibrator через AndroidJavaObject, на API 26+ — VibrationEffect.createOneShot. В редакторе и на других платформах — ничего.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Длительность импульса по умолчанию, мс.</summary>
        public const long DefaultMs = 25;

        // Unity добавляет разрешение VIBRATE в манифест Android, только если в сборке есть вызов Handheld.Vibrate().
        // Вызов ниже недостижим (флаг всегда false), но остаётся в коде ради разрешения; сама вибрация — через Vibrator.
        static readonly bool s_RequestVibratePermission = false;

        /// <summary>Импульс вибрации, если она включена в настройках игрока.</summary>
        public static void Pulse(GameSession s, long ms = DefaultMs)
        {
            if (s == null || s.Player.Settings == null || !s.Player.Settings.VibrationOn) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (s_RequestVibratePermission) Handheld.Vibrate();
            AndroidPulse(Math.Max(1L, ms));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject s_Vibrator;
        static int s_Sdk = -1;
        static bool s_Unavailable;

        static void AndroidPulse(long ms)
        {
            if (s_Unavailable) return;
            try
            {
                if (s_Vibrator == null)
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION")) s_Sdk = version.GetStatic<int>("SDK_INT");
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        // Context.VIBRATOR_SERVICE.
                        s_Vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                    if (s_Vibrator == null || !s_Vibrator.Call<bool>("hasVibrator"))
                    {
                        s_Unavailable = true;
                        return;
                    }
                }
                if (s_Sdk >= 26)
                {
                    using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        int amplitude = effects.GetStatic<int>("DEFAULT_AMPLITUDE");
                        using (var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude))
                            s_Vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    s_Vibrator.Call("vibrate", ms);
                }
            }
            catch (Exception e)
            {
                // Нет службы или прошивка запрещает вибрацию: больше не пытаемся, игра продолжается.
                s_Unavailable = true;
                Debug.LogWarning("[CatMiners] Вибрация недоступна: " + e.Message);
            }
        }
#endif
    }
}
