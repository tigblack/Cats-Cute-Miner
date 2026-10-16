using System;
using System.IO;
using CatMiners.Core.Outpost;
using CatMiners.Core.Text;
using CatMiners.Runtime;
using CatMiners.Runtime.Outpost;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatMiners.Playable
{
    /// <summary>
    /// Ход playable-сессии поверх контроллера основной игры (Docs/Playable/PLAYABLE_TZ.md §3.3–3.5, §6), только через его
    /// публичное API: игрока ведут квесты <see cref="PlayableQuests"/> (в основной игре обучения нет), рядом — надписи
    /// <see cref="PlayableJuice"/>, баннер и финальный экран <see cref="PlayableCta"/>); завершённая шахта переходит в следующую сама —
    /// окно сундука выхода закрывается через <see cref="ExitChestSeconds"/> (окно сундука, купленного в открытом магазине, —
    /// нет: его закрывает игрок), итоги видны <see cref="ResultsSeconds"/>, затем <see cref="OutpostController.ContinueToNextMine"/>
    /// (кнопка «Next mine» по-прежнему работает сразу); отсчёт стоит, пока открыто окно игрока или окно награды квеста. Конец
    /// отрезка — 30 минут игры (Profile.PlayTimeMs) или пройденная шахта 10 (<see cref="SegmentCompleted"/>). «Reset progress» в
    /// настройках ведёт в <see cref="Restart"/> (крючок HUD <see cref="OutpostHud.ResetProgressOverride"/>); магазин не показывает
    /// мёртвые «+» валют и плитки за гемы, которых нет (<see cref="OutpostHud.StoreHideUnpayable"/>). Время — часы
    /// контроллера, поэтому тесты с ManualClock идут без реального ожидания.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayableDirector : MonoBehaviour
    {
        /// <summary>Сколько секунд видны итоги шахты до автоперехода.</summary>
        public const double ResultsSeconds = 2.5;
        /// <summary>Сколько секунд видно окно сундука выхода (новые карточки) до автозакрытия.</summary>
        public const double ExitChestSeconds = 1.5;
        /// <summary>Отрезок playable: 30 минут активной игры…</summary>
        public const long SegmentPlayTimeMs = 30 * 60 * 1000L;
        /// <summary>…или пройденная шахта 10 (редкая кирка, двое ворот).</summary>
        public const int SegmentLastMine = 10;

        OutpostController m_Game;
        double m_ReadySince = double.NaN, m_ChestSince = double.NaN;

        /// <summary>Автопереход к следующей шахте (финальный экран выключает его, пока открыт).</summary>
        public bool AutoContinue = true;

        /// <summary>Отрезок пройден (30 минут или шахта 10); один раз на сессию сцены.</summary>
        public bool SegmentComplete { get; private set; }

        /// <summary>Отрезок только что пройден: <see cref="PlayableCta"/> открывает финальный экран.</summary>
        public event Action SegmentCompleted;

        public OutpostController Game => m_Game;

        /// <summary>Квесты playable на этом контроллере (добавляются вместе с режиссёром).</summary>
        public PlayableQuests Quests => GetComponent<PlayableQuests>();

        /// <summary>Баннер и финальный экран (добавляются вместе с режиссёром).</summary>
        public PlayableCta Cta => GetComponent<PlayableCta>();

        void Awake()
        {
            m_Game = GetComponent<OutpostController>();
            // Квесты ведут игрока; надписи «NEW CAT!»/«GATE OPEN!», баннер и финальный экран — рядом. Повторный режиссёр
            // (тест) их не дублирует.
            if (GetComponent<PlayableQuests>() == null) gameObject.AddComponent<PlayableQuests>();
            if (GetComponent<PlayableJuice>() == null) gameObject.AddComponent<PlayableJuice>();
            if (GetComponent<PlayableCta>() == null) gameObject.AddComponent<PlayableCta>();
        }

        void Update()
        {
            if (m_Game == null || m_Game.Hud == null || m_Game.Clock == null) return;
            // «Reset progress» в настройках — новая игра без архивов restart-*, как «Play again».
            if (m_Game.Hud.ResetProgressOverride == null) m_Game.Hud.ResetProgressOverride = Restart;
            // Урезанный магазин: «+» эликсира и гемов некуда вести, тайм-скип за гемы без гемов не купить — их не показывать.
            m_Game.Hud.StoreHideUnpayable = true;
            UpdateAutoContinue(m_Game.Clock.MonotonicSeconds);
            CheckSegment();
        }

        void UpdateAutoContinue(double now)
        {
            var hud = m_Game.Hud;
            if (!AutoContinue || !m_Game.CanContinue || hud.InTransition)
            {
                m_ReadySince = m_ChestSince = double.NaN;
                return;
            }
            // Сундук выхода с новыми карточками: окно показывается коротко, итоги откроются после него. Сундук, купленный в
            // открытом магазине, — выбор игрока: его окно не закрывается само, отсчёт стоит, пока магазин открыт.
            if (hud.ChestRewardOpen && !hud.StoreOpen)
            {
                m_ReadySince = double.NaN;
                if (double.IsNaN(m_ChestSince)) m_ChestSince = now;
                else if (now - m_ChestSince >= ExitChestSeconds)
                {
                    m_ChestSince = double.NaN;
                    hud.HideChestReward();
                }
                return;
            }
            m_ChestSince = double.NaN;
            // Окно, которое игрок открыл сам (экипировка, карточки, магазин…), и окно награды квеста останавливают отсчёт.
            if (PlayerWindowOpen(hud) || m_Game.PendingSkip != null)
            {
                m_ReadySince = double.NaN;
                return;
            }
            if (double.IsNaN(m_ReadySince))
            {
                m_ReadySince = now;
                hud.Toast(Loc.T("playable.auto_next"), (float)ResultsSeconds);
                return;
            }
            if (now - m_ReadySince < ResultsSeconds) return;
            m_ReadySince = double.NaN;
            m_Game.ContinueToNextMine();
            // «Next mine in a moment…» больше не нужен: в новой шахте он не висит поверх первого квеста.
            if (m_Game.InTransition) hud.Toast(null);
        }

        bool PlayerWindowOpen(OutpostHud hud)
        {
            var quests = Quests;
            return hud.BaseOpen || hud.EquipmentOpen || hud.CardsOpen || hud.StoreOpen || hud.SettingsOpen || hud.ModalOpen ||
                   hud.OfflineOpen || hud.SkipResultOpen || hud.CampaignOpen || hud.ErrorOpen || hud.ChestRewardOpen ||
                   quests != null && quests.PopupOpen;
        }

        void CheckSegment()
        {
            if (SegmentComplete) return;
            var profile = m_Game.State != null ? m_Game.State.Profile : null;
            if (profile == null) return;
            var done = profile.CompletedZones;
            bool mines = done != null && done.Count > 0 && done[done.Count - 1] >= SegmentLastMine;
            if (!mines && profile.PlayTimeMs < SegmentPlayTimeMs) return;
            SegmentComplete = true;
            Debug.Log("[Playable] Segment complete: " + (mines ? "mine " + SegmentLastMine + " cleared" : "30 minutes played") +
                      ", play time " + profile.PlayTimeMs / 1000 + " s");
            SegmentCompleted?.Invoke();
        }

        /// <summary>
        /// «Play again»: стереть сохранения playable и загрузить сцену заново — новая игра с нуля через обычный запуск
        /// контроллера (положение квестов лежит там же, в CatOutpostV2, и стирается вместе с ним). Не ResetProgress: тот
        /// копирует архив restart-* на каждый сброс (IndexedDB копил бы их). Старый
        /// контроллер выключается до стирания, чтобы автосохранение и переход не записали состояние обратно. Без
        /// <see cref="PlayableMode"/> ничего не делает: основное сохранение не трогается никогда.
        /// </summary>
        public void Restart()
        {
            if (!PlayableMode.Installed || m_Game == null) return;
            string root = SaveRoot(m_Game.SaveDirectory);
            if (root == null)
            {
                Debug.LogWarning("[Playable] Restart refused: no separate save directory");
                return;
            }
            // Сцена точки входа (CoreGame): загрузка заново создаёт контроллер, а PlayableMode — нового режиссёра.
            var boot = FindFirstObjectByType<GameBootstrap>();
            string scene = boot != null ? boot.gameObject.scene.name : SceneManager.GetActiveScene().name;
            m_Game.gameObject.SetActive(false);
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (Exception e) { Debug.LogWarning("[Playable] Save wipe failed: " + e.Message); }
            SceneManager.LoadScene(scene);
        }

        /// <summary>
        /// Каталог данных режима (CatOutpostV2) внутри каталога сохранений playable; null — каталог совпадает с
        /// persistentDataPath основной игры или не задан.
        /// </summary>
        static string SaveRoot(string saveDirectory)
        {
            if (string.IsNullOrEmpty(saveDirectory) || OutpostController.FileSystemOverride != null) return null;
            string dir = Path.GetFullPath(saveDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string main = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(dir, main, StringComparison.OrdinalIgnoreCase)) return null;
            return Path.Combine(dir, OutpostSaveService.RootFolder);
        }
    }
}
