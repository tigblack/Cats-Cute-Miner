using UnityEngine;
using UnityEngine.EventSystems;

namespace CatMiners.Playable
{
    /// <summary>
    /// Нажатие на CTA (баннер, «PLAY FREE»): взвод перехода в магазин в жесте браузера (<see cref="PlayableStore.Arm"/>) с
    /// областью <see cref="Area"/> на экране — веб-сборка откроет ссылку, когда палец или кнопка мыши отпущены над ней. Сам
    /// переход по клику — у кнопки (<see cref="PlayableStore.Open"/>). Стоит на том же объекте, что и Button: событие нажатия
    /// получают все обработчики объекта.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableStoreTap : MonoBehaviour, IPointerDownHandler
    {
        static readonly Vector3[] s_Corners = new Vector3[4];

        /// <summary>Область, внутри которой отпускание открывает магазин (null — этот элемент).</summary>
        public RectTransform Area;

        public void OnPointerDown(PointerEventData e)
        {
            var area = Area != null ? Area : (RectTransform)transform;
            PlayableStore.Arm(ScreenRect(area, e.pressEventCamera));
        }

        /// <summary>Прямоугольник элемента в долях экрана (начало — левый нижний угол, как <see cref="Screen"/>).</summary>
        public static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(s_Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, s_Corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, s_Corners[2]);
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            return Rect.MinMaxRect(min.x / w, min.y / h, max.x / w, max.y / h);
        }
    }
}
