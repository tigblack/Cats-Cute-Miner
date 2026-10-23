using CatMiners.Core.Outpost;
using CatMiners.Runtime.Outpost;

namespace CatMiners.Tests
{
    /// <summary>
    /// Тесты механик (заводы, рынок, камера, бонусы), которым сцены спасения Mia мешают: кадр клетки и окно «Mia is free!»
    /// отмечаются показанными, строки «Базы» — просмотренными. Сама первая сессия проверяется FirstSession*Tests.
    /// </summary>
    public static class FirstSessionTestUtil
    {
        /// <summary>
        /// Отметить кадр клетки, окно после спасения и просмотр строк «Базы»; открытое окно закрыть. Вызывать после загрузки шахты
        /// (LoadZone заменяет профиль копией).
        /// </summary>
        public static void SkipPresentation(OutpostController game)
        {
            var p = game != null && game.State != null ? game.State.Profile : null;
            var cfg = game != null && game.Configs != null ? game.Configs.Progression : null;
            if (p == null || cfg == null || p.Progression == null) return;
            foreach (var f in cfg.Features) ProgressionService.MarkShown(p, "seen." + f.Id);
            ProgressionService.MarkShown(p, ProgressionService.CalloutShown);
            if (cfg.Rescue != null) ProgressionService.MarkShown(p, ProgressionService.RevealPrefix + cfg.Rescue.Character);
            if (cfg.Blacksmith != null)
            {
                ProgressionService.MarkShown(p, ProgressionService.CalloutKey(cfg.Blacksmith.Character));
                ProgressionService.MarkShown(p, ProgressionService.RevealPrefix + cfg.Blacksmith.Character);
            }
            ProgressionService.MarkShown(p, ProgressionService.CardsIntroShown);
            ProgressionService.MarkShown(p, ProgressionService.EquipmentIntroShown);
            if (game.Hud != null && game.Hud.IntroOpen) game.Hud.HideIntro();
        }
    }
}
