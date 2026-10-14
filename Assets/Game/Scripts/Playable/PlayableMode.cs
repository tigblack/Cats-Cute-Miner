using System.IO;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatMiners.Playable
{
    /// <summary>
    /// Включение playable-режима (Docs/Playable/PLAYABLE_TZ.md §3.1, §6) через выключенные по умолчанию крючки основной игры:
    /// профиль баланса <see cref="PlayableBalance"/> (<see cref="OutpostController.ConfigsOverride"/>), своё сохранение
    /// persistentDataPath/<see cref="SaveFolder"/>, без окна «While you were away», место под баннер
    /// (<see cref="OutpostHud.BottomReserve"/>) и <see cref="PlayableDirector"/> на контроллере каждой загруженной сцены.
    /// Сборка с CATMINERS_PLAYABLE вызывает <see cref="Install"/> сама до загрузки первой сцены; в редакторе — только тесты,
    /// поэтому тесты основной игры его не видят. <see cref="Uninstall"/> возвращает все крючки в исходное состояние.
    /// </summary>
    public static class PlayableMode
    {
        /// <summary>Каталог сохранений playable внутри persistentDataPath: основное сохранение не трогается.</summary>
        public const string SaveFolder = "playable";
        /// <summary>
        /// Высота резерва под баннер CTA под нижней панелью HUD: 40 пикселей арта (единицы Canvas) — табличка 36 (кнопка 28 и поля
        /// по 4), поле 2 снизу и зазор 2 до нижней панели.
        /// </summary>
        public const float BannerHeight = 40 * PixelUi.Px;

        static bool s_OwnsSaveDirectory;

        /// <summary>Режим включён: крючки основной игры выставлены.</summary>
        public static bool Installed { get; private set; }

#if CATMINERS_PLAYABLE
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoInstall() => Install();
#endif

        /// <summary>
        /// Выставить крючки до загрузки сцены CoreGame. Каталог сохранений, уже заданный тестом, сохраняется. Повторный
        /// вызов ничего не меняет.
        /// </summary>
        public static void Install()
        {
            if (Installed) return;
            Installed = true;
            OutpostController.ConfigsOverride = PlayableBalance.Apply;
            if (string.IsNullOrEmpty(OutpostController.SaveDirectoryOverride))
            {
                OutpostController.SaveDirectoryOverride = Path.Combine(Application.persistentDataPath, SaveFolder);
                s_OwnsSaveDirectory = true;
            }
            OutpostController.SuppressOfflineSummary = true;
            OutpostHud.BottomReserve = BannerHeight;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>Снять все крючки (тесты): значения по умолчанию основной игры. Каталог, заданный тестом, тест сбрасывает сам.</summary>
        public static void Uninstall()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            OutpostController.ConfigsOverride = null;
            OutpostController.SuppressOfflineSummary = false;
            OutpostHud.BottomReserve = 0f;
            if (s_OwnsSaveDirectory) OutpostController.SaveDirectoryOverride = null;
            s_OwnsSaveDirectory = false;
            Installed = false;
        }

        /// <summary>Контроллер новой сцены (GameBootstrap.Awake уже выполнен) получает режиссёра playable.</summary>
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var game = GameBootstrap.OutpostController;
            if (game == null || game.GetComponent<PlayableDirector>() != null) return;
            game.gameObject.AddComponent<PlayableDirector>();
        }
    }
}
