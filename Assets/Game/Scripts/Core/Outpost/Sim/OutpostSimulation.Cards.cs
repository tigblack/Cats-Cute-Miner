using System;

namespace CatMiners.Core.Outpost
{
    public sealed partial class OutpostSimulation
    {
        /// <summary>
        /// Награда разрушенной цели (CARD_EFFECT_MAPPING.md §Источники): камень — очередная запись своего RewardCycle (счётчик по
        /// Rocks[].Id в профиле; сундук или эликсир, золото по-прежнему только порциями груза); месторождение s и ворота p —
        /// сундук записи Gacha их токена. Завалы x наград не дают; обстановка шахты (мусор и руда-заполнитель) — тоже: её золото
        /// приходит порциями груза, а карточки и эликсир не растут вместе с количеством руды.
        /// </summary>
        void GrantTargetReward(MapTarget mt, long t)
        {
            if (mt.Piece != null) return;
            // Первая сессия: до шахты открытия карточек находки шахты (сундук выхода шахты 1, эликсир камней) не выдаются —
            // карточки приходят в шахте 2 вместе с разделом и выдачей ровно на одно улучшение (ProgressionService.CardsHeld).
            if (ProgressionService.CardsHeld(Profile, m_Configs, Run.Zone)) return;
            var profile = Profile;
            switch (mt.Kind)
            {
                case 'r':
                {
                    var rock = m_Cards.Rock(mt.TypeId);
                    if (rock == null) return;
                    profile.RockCycles.TryGetValue(mt.TypeId, out int counter);
                    profile.RockCycles[mt.TypeId] = checked(counter + 1);
                    var entry = rock.Cycle[counter % rock.Cycle.Length];
                    if (entry.Kind == RockRewardKind.Chest) OpenChest(entry.ChestId, mt.Id, m_Map.Cell(mt.Column, mt.Row), t);
                    else if (entry.Kind == RockRewardKind.Elixir)
                        GrantElixir(CardChestService.RockElixir(m_Cards, rock, mt.SourceLevel, Run.Zone), mt.Id, m_Map.Cell(mt.Column, mt.Row), t);
                    break;
                }
                case 's':
                case 'p':
                    if (mt.RewardId != null) OpenChest(mt.RewardId, mt.Id, mt.IsGate ? -1 : m_Map.Cell(mt.Column, mt.Row), t);
                    break;
            }
        }

        /// <summary>Открыть сундук записи Gacha: карточки и эликсир сразу в профиль (постоянные ресурсы, не золото шахты).</summary>
        void OpenChest(string chestId, int sourceTargetId, int cell, long t, string minimumCardId = null, int minimumCardCopies = 0)
        {
            var chest = m_Cards.Chest(chestId);
            if (chest == null) return;
            var stats = m_StatsDirty ? ComputeStats() : m_Stats;
            var result = CardChestService.Open(Profile, m_Cards, chest, Run.Zone, stats, minimumCardId, minimumCardCopies);
            // Сундук магазина — не находка шахты: счётчики забега (итоги, офлайн-сводка) его не включают.
            if (sourceTargetId != OutpostIds.StoreSource)
            {
                var c = Run.Counters;
                c.ChestsOpened++;
                c.CardsGained = checked(c.CardsGained + result.TotalCopies);
                c.ElixirGained = checked(c.ElixirGained + result.Elixir);
            }
            Touch(t);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.ChestOpened, t);
                e.TargetId = sourceTargetId;
                e.Cell = cell;
                e.ItemId = chest.Id;
                e.Count = result.TotalCopies;
                e.Value = result.Elixir;
                e.Detail = result.Detail();
                e.Extra = result.NewCards.Count;
                m_Events.Add(e);
            }
            // После события сундука: FactoryAutomated первой копии идёт за ChestOpened на той же отметке (ТЗ 2.2 §4.4).
            if (result.NewCards.Count > 0) OnCardsChanged(t);
        }

        /// <summary>Некоторые токены поздних шахт не содержат rewardId: обычный деревянный сундук сохраняет карты и эликсир.</summary>
        ChestDef FactoryChest(int targetId) => m_Cards?.Chest(m_Map.Target(targetId)?.RewardId) ?? m_Cards?.Chest("GachaWooden");

        void GrantFactoryChest(FactoryState f, long t)
        {
            string key = CardService.FactorySeenKey(f.TypeId);
            if (Profile.CardKeys == null) Profile.CardKeys = new System.Collections.Generic.List<string>();
            bool first = !Profile.CardKeys.Contains(key);
            if (m_Cards != null)
            {
                var mf = m_Map.Factory(f.TargetId);
                var def = m_Cards.Get(mf.RequiredCardId);
                long missing = CardService.MissingCopiesToLevel(Profile, def, mf.RequiredCardLevel);
                int quarters = Run.Zone <= 7 ? 4 : Run.Zone <= 10 ? 3 : Run.Zone <= 15 ? 2 : 1;
                long assisted = (missing * quarters + 3) / 4; // Округление вверх: неполная копия не пропадает.
                int minimum = checked((int)Math.Max(first && def != null ? 1 : 0, assisted));
                var mt = m_Map.Target(f.TargetId);
                OpenChest(FactoryChest(f.TargetId).Id, f.TargetId, m_Map.Cell(mt.Column, mt.Row), t, def?.Id, minimum);
            }
            if (first) Profile.CardKeys.Add(key);
        }

        void GrantElixir(long amount, int targetId, int cell, long t)
        {
            if (amount <= 0) return;
            Profile.Elixir = checked(Profile.Elixir + amount);
            Run.Counters.ElixirGained = checked(Run.Counters.ElixirGained + amount);
            Touch(t);
            if (!RecordEvents) return;
            var e = OutpostEvent.Create(OutpostEventType.ElixirFound, t);
            e.TargetId = targetId;
            e.Cell = cell;
            e.Value = amount;
            m_Events.Add(e);
        }

        /// <summary>
        /// Учебная однократная выдача (правило Cat Miners): первая копия CardRules.TutorialCard при первой доставке на базу нового
        /// профиля. Ключ <see cref="CardService.TutorialKey"/>; профили, созданные до карточек, помечаются при миграции.
        /// </summary>
        void TryTutorialGrant(long t)
        {
            var profile = Profile;
            string cardId = m_Cards.Rules.TutorialCard;
            if (cardId == null || profile.CardsVersion < 1) return;
            if (profile.CardKeys == null) profile.CardKeys = new System.Collections.Generic.List<string>();
            if (profile.CardKeys.Contains(CardService.TutorialKey)) return;
            var def = m_Cards.Get(cardId);
            profile.CardKeys.Add(CardService.TutorialKey);
            bool activated = CardService.Grant(profile, def, 1);
            Run.Counters.CardsGained++;
            Touch(t);
            if (RecordEvents)
            {
                var e = OutpostEvent.Create(OutpostEventType.ChestOpened, t);
                e.ItemId = "tutorial";
                e.Count = 1;
                e.Detail = cardId + ":1";
                e.Extra = activated ? 1 : 0;
                m_Events.Add(e);
            }
            if (activated) OnCardsChanged(t);
        }

        /// <summary>Улучшение карточки одной транзакцией (копии и эликсир → уровень); разрешено и в ReadyToContinue.</summary>
        CommandResult ExecuteUpgradeCard(OutpostCommand cmd, long t)
        {
            if (m_Cards == null) return CommandResult.Fail("unknown");
            var def = m_Cards.Get(cmd.ItemId);
            if (def == null) return CommandResult.Fail("unknown");
            string reason = CardService.Upgrade(Profile, def, out var quote);
            if (reason != null) return CommandResult.Fail(reason);
            var e = OutpostEvent.Create(OutpostEventType.CardUpgraded, t);
            e.ItemId = def.Id;
            e.Count = quote.Level + 1;
            e.Value = quote.ElixirNeeded;
            Emit(e);
            OnCardsChanged(t);
            return new CommandResult { Ok = true, Levels = 1, Cost = quote.ElixirNeeded };
        }

        /// <summary>
        /// Уровень карточки вырос (первая копия из сундука цели, выхода или магазина, учебная выдача, UpgradeCard): фаза 7
        /// пересчитает характеристики (без потери груза и телепортации); требование автодоставки заводов пересчитывается на этой
        /// же отметке t (ТЗ 2.2 §4.4) — готовая партия ставшего автоматическим завода сразу уходит в трубу.
        /// </summary>
        void OnCardsChanged(long t)
        {
            m_StatsDirty = true;
            Run.PlanningDirty = true;
            SyncFactoryAutomation(t);
            // Полученная карточка открывает раздел карточек раньше его шахты — доступ, а не замок (первая сессия).
            SyncProgression(t);
        }
    }
}
