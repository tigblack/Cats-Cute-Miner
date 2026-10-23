using System;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Звуки интерфейса без прямой зависимости UI-кита от <see cref="Sfx"/>: кит вызывает события, Sfx на них подписывается.
    /// Не назначено — тишина (тесты, инструменты разработки).
    /// </summary>
    public static class UiSound
    {
        /// <summary>Нажатие кнопки.</summary>
        public static Action Click;
        /// <summary>Открытие окна или панели.</summary>
        public static Action Open;
        /// <summary>Закрытие окна или панели.</summary>
        public static Action Close;
        /// <summary>Действие недоступно (нажата неактивная кнопка, отказ команды).</summary>
        public static Action Denied;

        public static void PlayClick() => Click?.Invoke();
        public static void PlayOpen() => Open?.Invoke();
        public static void PlayClose() => Close?.Invoke();
        public static void PlayDenied() => Denied?.Invoke();
    }
}
