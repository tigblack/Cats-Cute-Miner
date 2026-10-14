using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace CatMiners.Playable
{
    /// <summary>
    /// Переход в магазин из баннера и финального экрана (Docs/Playable/PLAYABLE_TZ.md §3.5, research/integration.md §8,
    /// reference.md §5): только по тапу игрока, без автоперехода. Ссылка — из параметра страницы «?store=» (только http/https),
    /// иначе <see cref="DefaultUrl"/>. В веб-сборке — Plugins/CatMinersPlayable.jslib: SDK рекламной сети (install/gameReady,
    /// mraid, dapi, FbPlayableAd, ExitApi), иначе window.open в новой вкладке. Чтобы браузер не заблокировал вкладку, нажатие
    /// на CTA взводит одноразовый слушатель отпускания (<see cref="Arm"/>): ссылка открывается в самом жесте и помечается
    /// использованной, а клик C# того же нажатия (<see cref="Open"/>) — когда бы он ни пришёл (кадром позже или после
    /// возвращения на вкладку) — пропускается до следующего взвода. Клик вне жеста вкладку не уводит (без location.href).
    /// В редакторе и на других платформах — Application.OpenURL; тесты подменяют открытие через <see cref="OpenOverride"/>.
    /// </summary>
    public static class PlayableStore
    {
        /// <summary>Ссылка по умолчанию: полная веб-версия игры (ссылок на магазины пока нет).</summary>
        public const string DefaultUrl = "https://tigblack.github.io/Cat-Miners/";
        /// <summary>Параметр адреса страницы со ссылкой магазина (значение — в URL-кодировке, «&amp;» внутри ссылки — %26).</summary>
        public const string QueryKey = "store";

        /// <summary>
        /// Тесты: вместо открытия ссылка передаётся сюда (null — обычное открытие). Сбрасывается в TearDown.
        /// </summary>
        public static Action<string> OpenOverride;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string CM_StoreNetwork();
        [DllImport("__Internal")] static extern void CM_StoreArm(string url, float x0, float y0, float x1, float y1);
        [DllImport("__Internal")] static extern int CM_StoreOpen(string url);
#endif

        /// <summary>Ссылка магазина для этой страницы.</summary>
        public static string Url => UrlFromPage(Application.absoluteURL);

        /// <summary>
        /// Рекламная сеть, в которой открыта страница: mintegral, mraid, ironsource, facebook, google или web; вне веб-сборки — editor.
        /// </summary>
        public static string Network
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return CM_StoreNetwork();
#else
                return "editor";
#endif
            }
        }

        /// <summary>
        /// Ссылка из «?store=» адреса <paramref name="pageUrl"/>; без параметра, с пустым значением или со схемой не http/https
        /// (javascript:, data: и т. п. в новой вкладке выполнились бы от имени страницы) — <see cref="DefaultUrl"/>.
        /// </summary>
        public static string UrlFromPage(string pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return DefaultUrl;
            int start = pageUrl.IndexOf('?');
            if (start < 0) return DefaultUrl;
            int end = pageUrl.IndexOf('#', start);
            string query = end < 0 ? pageUrl.Substring(start + 1) : pageUrl.Substring(start + 1, end - start - 1);
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || !string.Equals(pair.Substring(0, eq), QueryKey, StringComparison.Ordinal)) continue;
                string value;
                try { value = Uri.UnescapeDataString(pair.Substring(eq + 1)).Trim(); }
                catch (Exception) { return DefaultUrl; }
                return IsWebLink(value) ? value : DefaultUrl;
            }
            return DefaultUrl;
        }

        static bool IsWebLink(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            !string.IsNullOrEmpty(uri.Host);

        /// <summary>
        /// Нажатие на CTA: веб-сборка открывает магазин при отпускании пальца или кнопки мыши внутри <paramref name="screenRect"/>
        /// (доли экрана, начало — левый нижний угол, как Screen); отпускание вне области снимает взвод. В остальных сборках — ничего.
        /// </summary>
        public static void Arm(Rect screenRect)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (OpenOverride == null) CM_StoreArm(Url, screenRect.xMin, screenRect.yMin, screenRect.xMax, screenRect.yMax);
#endif
        }

        /// <summary>Тап по CTA: открыть магазин (или передать ссылку <see cref="OpenOverride"/>).</summary>
        public static void Open()
        {
            string url = Url;
            if (OpenOverride != null)
            {
                OpenOverride(url);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // 0 — магазин уже открыт жестом этого нажатия, −1 — окно заблокировано вне жеста (вкладка остаётся).
            int route = CM_StoreOpen(url);
            if (route > 0) Debug.Log("[Playable] Store opened (" + Network + ", route " + route + "): " + url);
            else if (route < 0) Debug.LogWarning("[Playable] Store link blocked outside a tap gesture: " + url);
#else
            Debug.Log("[Playable] Store: " + url);
            Application.OpenURL(url);
#endif
        }
    }
}
