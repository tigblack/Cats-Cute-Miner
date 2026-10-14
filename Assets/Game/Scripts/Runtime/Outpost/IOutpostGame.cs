using System;
using CatMiners.Core.Outpost;
using CatMiners.Core.Sim;
using UnityEngine;
// В Core.Sim (старый режим) есть свои CommandResult и OfflineSummary: контракт использует типы нового ядра.
using CommandResult = CatMiners.Core.Outpost.CommandResult;
using OfflineSummary = CatMiners.Core.Outpost.OfflineSummary;

namespace CatMiners.Runtime.Outpost
{
    /// <summary>Что выбрано тапом по полю (ТЗ 5, 14): сведения без ручного назначения работы.</summary>
    public enum OutpostSelectionKind
    {
        None,
        Worker,
        Target,
        Base,
    }

    public struct OutpostSelection
    {
        public OutpostSelectionKind Kind;
        /// <summary>WorkerId для Worker, Id цели (индекс якоря) для Target; иначе −1.</summary>
        public int Id;

        public static OutpostSelection None => new OutpostSelection { Kind = OutpostSelectionKind.None, Id = -1 };
    }

    /// <summary>Состояние контроллера вокруг ядра (ТЗ 11.3, 13, 17): что сейчас можно показывать и нажимать.</summary>
    public enum OutpostControllerPhase
    {
        /// <summary>Загрузка или фатальная ошибка данных/сохранения — поле не работает.</summary>
        Loading,
        /// <summary>Обычная игра (фазы ядра Running/DrainingCargo/ReadyToContinue).</summary>
        Playing,
        /// <summary>Идёт расчёт офлайна: покупки и переход недоступны.</summary>
        OfflineCatchUp,
        /// <summary>Переход на следующую шахту (снимок записан или записывается).</summary>
        Transitioning,
        /// <summary>Ошибка загрузки/записи, требующая решения игрока.</summary>
        Error,
    }

    /// <summary>
    /// Контракт контроллера режима CatOutpostV2 для UI, обучения и DevTools (Docs/Rework/RUNTIME_SPEC.md §3.3).
    /// Реализует <see cref="OutpostController"/>. Все числа UI читает из ядра (Sim/State/Stats), а не считает сам (ТЗ 14).
    /// Методы-команды применяют изменение на текущей отметке ядра и сразу запрашивают запись (ТЗ 17).
    /// </summary>
    public interface IOutpostGame
    {
        /// <summary>Ядро текущей шахты; null до загрузки.</summary>
        OutpostSimulation Sim { get; }
        OutpostGameState State { get; }
        OutpostMap Map { get; }
        OutpostConfigSet Configs { get; }
        TeamStats Stats { get; }
        OutpostControllerPhase Phase { get; }
        /// <summary>Отладочный профиль (CatOutpostV2/debug): только в нём доступны читы и ускорение (ТЗ 19).</summary>
        bool IsDebugProfile { get; }
        /// <summary>Идёт затемнение/перестройка при переходе: поле и кнопки не реагируют.</summary>
        bool InTransition { get; }

        // ---- Локальные улучшения (ТЗ 9) ----
        UpgradeQuote Quote(UpgradeBranch branch, BuyMode mode);
        CommandResult Buy(UpgradeBranch branch, BuyMode mode);

        // ---- Экипировка (ТЗ 10) ----
        CommandResult Equip(string itemId);
        CommandResult Unequip(EquipSlot slot);
        CommandResult EquipBest();
        CommandResult UpgradeItem(string itemId);

        // ---- Карточки (CARD_EFFECT_MAPPING.md): каталог — Configs.Cards, прогресс — State.Profile ----
        /// <summary>Улучшить карточку: копии и эликсир → уровень, сразу запись.</summary>
        CommandResult UpgradeCard(string cardId);

        // ---- Магазин (ТЗ 10.5): каталог — Configs.Store, гемы и расходники — State.Profile ----
        /// <summary>Текущее UTC (мс) для таймеров магазина.</summary>
        long UtcNowMs { get; }
        /// <summary>Купить плитку магазина (сундук, эликсир, гемы); сразу запись. Тайм-скип — <see cref="UseTimeSkip"/>.</summary>
        CommandResult StoreBuy(string itemId);
        /// <summary>Тайм-скип: расходник или гемы → N минут работы шахты сразу (расчёт как офлайн, затем <see cref="PendingSkip"/>).</summary>
        CommandResult UseTimeSkip(string itemId);
        /// <summary>Минуты идущего тайм-скипа (0 — офлайн-расчёт или ничего): текст окна расчёта.</summary>
        int SkipMinutesInProgress { get; }
        /// <summary>Итог тайм-скипа для окна; null — окна нет.</summary>
        OfflineSummary PendingSkip { get; }
        void DismissSkip();

        // ---- Поле и камера (ТЗ 5) ----
        OutpostSelection Selection { get; }
        void SelectWorker(int workerId);
        void SelectTarget(int targetId);
        void SelectBase();
        void ClearSelection();
        /// <summary>Кнопка «К базе»: плавно вернуть камеру к лагерю.</summary>
        void FocusBase();
        /// <summary>Мировая позиция котика (для подсказок обучения и сведений); false, если котика нет.</summary>
        bool TryGetWorkerWorldPosition(int workerId, out Vector3 world);
        /// <summary>Мировая позиция центра лагеря / места склада slot (0, 1) / цели.</summary>
        Vector3 BaseWorldPosition { get; }
        Vector3 DepotWorldPosition(int slot);
        bool TryGetTargetWorldPosition(int targetId, out Vector3 world);

        // ---- Завершение и переход (ТЗ 11) ----
        /// <summary>Ядро в ReadyToContinue, награда записана и есть следующая шахта.</summary>
        bool CanContinue { get; }
        /// <summary>Кампания пройдена (после zone144): кнопки следующей шахты нет.</summary>
        bool CampaignComplete { get; }
        /// <summary>Запись последнего снимка не удалась: UI показывает «Повторить».</summary>
        bool SaveFailed { get; }
        void ContinueToNextMine();
        void RetrySave();

        // ---- Офлайн (ТЗ 13) ----
        /// <summary>Результат уже записанного офлайн-расчёта для окна «Пока вас не было»; null — окна нет.</summary>
        OfflineSummary PendingOffline { get; }
        /// <summary>Доля выполненного офлайн-расчёта 0..1 (индикатор во время OfflineCatchUp).</summary>
        float OfflineProgress01 { get; }
        void DismissOffline();
        /// <summary>«Double income» (тестовый режим рекламы): золото офлайна ещё раз, один раз; окно закрывается.</summary>
        CommandResult ClaimOfflineDouble();

        // ---- Настройки (ТЗ 14) ----
        void SetVolume(AudioGroup group, float value);
        void SetVibration(bool on);
        void SetReduceEffects(bool on);
        /// <summary>Начать заново после подтверждения в настройках; доступно во всех сборках.</summary>
        bool CanResetProgress { get; }
        void ResetProgress();

        // ---- Обучение (ТЗ 15) ----
        void SaveTutorialProgress();

        /// <summary>Каждое событие ядра после применения к виду (для HUD, обучения, звука). Не меняет экономику.</summary>
        event Action<OutpostEvent> EventPresented;
        /// <summary>Текст фатальной ошибки загрузки/сохранения (для окна ошибки); null — ошибки нет.</summary>
        string ErrorText { get; }
    }
}
