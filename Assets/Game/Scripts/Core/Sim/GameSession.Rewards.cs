using System;
using System.Collections.Generic;
using CatMiners.Core.Data;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Sim
{
    public sealed partial class GameSession
    {
        void Ledger(string key, GameNumber amount)
        {
            if (!amount.IsPositive) return;
            Player.Ledger[key] = (Player.Ledger.TryGetValue(key, out var v) ? v : GameNumber.Zero) + amount;
        }

        void AddGold(GameNumber amount, string source)
        {
            if (!amount.IsPositive) return;
            Mine.Gold += amount;
            Ledger(source, amount);
        }

        void AddElixir(GameNumber amount, string source)
        {
            if (!amount.IsPositive) return;
            Player.Elixir += amount;
            Ledger(source, amount);
        }

        void AddCards(string cardId, int count)
        {
            if (count <= 0 || !Balance.Cards.ContainsKey(cardId)) return;
            var cs = CardStateOf(cardId);
            if (cs.Level == 0)
            {
                cs.Level = 1;
                count--;
            }
            cs.Duplicates += count;
        }

        /// <summary>Награда камня по циклу (ТЗ 10.2, E-09). Счётчик — на Rocks.Id, сквозной между зонами.</summary>
        void GrantRockReward(ZoneObject obj, long op)
        {
            var rock = Balance.Rocks[obj.TypeId];
            Player.RewardCycle.TryGetValue(rock.Id, out long counter);
            var entry = rock.RewardCycle[(int)(counter % rock.RewardCycle.Length)];
            Player.RewardCycle[rock.Id] = counter + 1;

            if (entry.RewardType == RewardTypes.Gacha)
            {
                OpenChest(entry.DetailedType, "rock", op);
                return;
            }
            switch (entry.DetailedType)
            {
                case "Core":
                {
                    var seconds = Formula.Ft3Number(rock.CoreCurrencySecondsBase, rock.CoreCurrencySecondsMultiplier, rock.CoreCurrencySecondsGrowth,
                        rock.CoreCurrencySecondsPower, rock.CoreCurrencySecondsRound, obj.Level);
                    var gold = Formula.Payout(seconds * GetRewardIncomeRate());
                    if (LootPickups && gold.IsPositive)
                    {
                        DropLoot(obj, LootState.Gold, gold, op);
                        break;
                    }
                    AddGold(gold, "gold:rock");
                    Emit(new SimEvent { Type = SimEventType.RewardGranted, OpId = op, ObjectIndex = obj.Index, Amount = gold, Text = "gold" });
                    break;
                }
                case "Soft":
                {
                    var raw = Formula.Evaluate(rock.SoftCurrencyFormulaType, rock.SoftCurrencyBase, rock.SoftCurrencyMultiplier, rock.SoftCurrencyGrowth, obj.Level);
                    var elixir = Formula.Payout(raw * GameNumber.FromDouble(Balance.ZoneMultipliers(Mine.Zone).MiningSoftCurrencyMultiplier));
                    if (LootPickups && elixir.IsPositive)
                    {
                        DropLoot(obj, LootState.Elixir, elixir, op);
                        break;
                    }
                    AddElixir(elixir, "elixir:rock");
                    Emit(new SimEvent { Type = SimEventType.RewardGranted, OpId = op, ObjectIndex = obj.Index, Amount = elixir, Text = "elixir" });
                    break;
                }
                case "Hard":
                {
                    var hard = GameNumber.FromDouble(entry.Quantity);
                    if (LootPickups && hard.IsPositive)
                    {
                        DropLoot(obj, LootState.Hard, hard, op);
                        break;
                    }
                    Player.Hard += hard;
                    Ledger("hard:rock", hard);
                    Emit(new SimEvent { Type = SimEventType.RewardGranted, OpId = op, ObjectIndex = obj.Index, Amount = hard, Text = "hard" });
                    break;
                }
                default:
                {
                    // Награды исключённых систем (сезонный пропуск): не выдаются, только учёт (ТЗ 10.2).
                    // Набор выдаваемых типов выше должен совпадать с IsGrantedRockCurrency.
                    Player.ExcludedRewards.TryGetValue(entry.DetailedType, out double was);
                    Player.ExcludedRewards[entry.DetailedType] = was + entry.Quantity;
                    Emit(new SimEvent { Type = SimEventType.ExcludedReward, OpId = op, ObjectIndex = obj.Index, Text = entry.DetailedType });
                    break;
                }
            }
        }

        // ───────────────────────────── Добыча на поле (LootPickups) ─────────────────────────────

        /// <summary>Награда камня выпадает добычей в клетке его якоря; начисление — при сборе.</summary>
        void DropLoot(ZoneObject obj, string currency, GameNumber amount, long op)
        {
            var loot = new LootState { Id = Mine.NextLootId++, Cell = obj.Index, Currency = currency, Amount = amount, SourceIndex = obj.Index };
            Mine.Loot.Add(loot);
            Emit(new SimEvent { Type = SimEventType.LootDropped, OpId = op, CatId = loot.Id, Cell = loot.Cell, ObjectIndex = obj.Index, Amount = amount, Text = currency });
        }

        /// <summary>Начислить добычу с ключом журнала камня (как прежнее мгновенное начисление). Без событий.</summary>
        void CreditLoot(LootState loot)
        {
            switch (loot.Currency)
            {
                case LootState.Gold:
                    AddGold(loot.Amount, "gold:rock");
                    break;
                case LootState.Elixir:
                    AddElixir(loot.Amount, "elixir:rock");
                    break;
                case LootState.Hard:
                    Player.Hard += loot.Amount;
                    Ledger("hard:rock", loot.Amount);
                    break;
            }
        }

        /// <summary>Индекс добычи в Mine.Loot или −1.</summary>
        int LootSlot(int lootId)
        {
            var list = Mine.Loot;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == lootId) return i;
            return -1;
        }

        /// <summary>Сбор добычи одной транзакцией: снять с поля, начислить, RewardGranted + LootCollected.</summary>
        void CollectLootAt(int i, bool auto, long op)
        {
            var loot = Mine.Loot[i];
            Mine.Loot.RemoveAt(i);
            CreditLoot(loot);
            Mine.LootCollected++;
            Emit(new SimEvent { Type = SimEventType.RewardGranted, OpId = op, ObjectIndex = loot.SourceIndex, Amount = loot.Amount, Text = loot.Currency });
            Emit(new SimEvent
            {
                Type = SimEventType.LootCollected, OpId = op, CatId = loot.Id, Cell = loot.Cell, ObjectIndex = loot.SourceIndex,
                Amount = loot.Amount, Text = loot.Currency, OtherCatId = auto ? 1 : 0,
            });
        }

        /// <summary>Автосбор добычи, пролежавшей <see cref="LootAutoCollectTicks"/>, в порядке выпадения (шаг Advance, без выделений).</summary>
        void AutoCollectDueLoot()
        {
            var list = Mine.Loot;
            for (int i = 0; i < list.Count;)
            {
                if (list[i].AgeTicks >= LootAutoCollectTicks) CollectLootAt(i, true, NextOp());
                else i++;
            }
        }

        /// <summary>Валютные награды камня, которые выдаёт <see cref="GrantRockReward"/>; остальные DetailedType — исключённые системы.</summary>
        static bool IsGrantedRockCurrency(string detailedType) => detailedType == "Core" || detailedType == "Soft" || detailedType == "Hard";

        readonly Dictionary<string, bool> m_ExcludedRockCache = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> m_ChestRockCache = new Dictionary<string, bool>();

        /// <summary>
        /// Камень приносит только награды исключённых систем (ТЗ 3.1, 10.2): каждая запись RewardCycle — не сундук
        /// и не выдаваемая валюта. Правило выводится из данных, без литералов ID (ТЗ 4.1, 4.5 п. 3). Кэшируется по ID.
        /// Камень без цикла наград исключённым не считается.
        /// </summary>
        public bool IsExcludedRewardRock(string rockTypeId)
        {
            if (rockTypeId == null) return false;
            if (m_ExcludedRockCache.TryGetValue(rockTypeId, out bool cached)) return cached;
            bool excluded = false;
            if (Balance.Rocks.TryGetValue(rockTypeId, out var rock) && rock.RewardCycle != null && rock.RewardCycle.Length > 0)
            {
                excluded = true;
                foreach (var e in rock.RewardCycle)
                {
                    if (e == null) continue;
                    if (e.RewardType == RewardTypes.Gacha || IsGrantedRockCurrency(e.DetailedType))
                    {
                        excluded = false;
                        break;
                    }
                }
            }
            m_ExcludedRockCache[rockTypeId] = excluded;
            return excluded;
        }

        /// <summary>Сундучный камень: каждая запись RewardCycle — сундук (смешанные циклы вроде обычных камней не считаются).</summary>
        public bool IsChestRock(string rockTypeId)
        {
            if (rockTypeId == null) return false;
            if (m_ChestRockCache.TryGetValue(rockTypeId, out bool cached)) return cached;
            bool chest = false;
            if (Balance.Rocks.TryGetValue(rockTypeId, out var rock) && rock.RewardCycle != null && rock.RewardCycle.Length > 0)
            {
                chest = true;
                foreach (var e in rock.RewardCycle)
                {
                    if (e != null && e.RewardType == RewardTypes.Gacha) continue;
                    chest = false;
                    break;
                }
            }
            m_ChestRockCache[rockTypeId] = chest;
            return chest;
        }

        public GachaContext BuildGachaContext()
        {
            var ctx = new GachaContext
            {
                Rank = Rank,
                Multipliers = Balance.ZoneMultipliers(Mine.Zone),
                CardLevel = CardLevel,
            };
            ctx.GeneratorsInZone.Add(BalanceRepository.CartId);
            foreach (var s in Layout.Shafts)
            {
                ctx.GeneratorsInZone.Add(s.TypeId);
                if (!ctx.RequiredCardLevels.TryGetValue(s.CardId, out int req) || req < s.CardLevel)
                    ctx.RequiredCardLevels[s.CardId] = s.CardLevel;
            }
            return ctx;
        }

        /// <summary>Определить, начислить и поставить в очередь показа результат сундука (ТЗ 10.3).</summary>
        ChestResult OpenChest(string gachaId, string source, long op)
        {
            if (!Balance.Gacha.TryGetValue(gachaId, out var g)) throw new InvalidOperationException("Нет сундука " + gachaId);
            var result = GachaService.Open(Balance, g, BuildGachaContext(), Player.Rng.Gacha);
            result.OpId = op;
            result.Source = source;
            AddElixir(result.Elixir, "elixir:chest:" + source);
            foreach (var c in result.Cards)
            {
                AddCards(c.CardId, c.Count);
                Ledger("cards:" + source, GameNumber.FromDouble(c.Count));
            }
            // Очередь неоткрытых сундуков без ограничения длины: определённый сундук не теряется,
            // в том числе при CompleteZone (ТЗ 10.3, 11.2 п. 2, 11.3). Очередь разбирает только TakeChestReveal.
            Player.ChestReveals.Add(result);
            Emit(new SimEvent { Type = SimEventType.ChestOpened, OpId = op, ObjectIndex = -1, Text = gachaId, Amount = result.Elixir });
            if (OfflineMode)
            {
                // ТЗ 9.4, 12.3: офлайн ручная шахта идёт только до одного буфера, ручная выплата не подмешивается
                // в офлайн-поток. Автоматизация (и зачёт её буфера) — первым онлайн-шагом, см. RunDeferredAutomationCheck.
                if (IsAutomationDue()) Mine.AutomationCheckPending = true;
            }
            else
            {
                CheckAutomation();
            }
            return result;
        }

        /// <summary>
        /// Выполнить отложенную офлайн-проверку автоматизации (ТЗ 9.2, 12.3). Вызывается в начале каждого онлайн-Advance;
        /// контроллер может вызвать её сам после окна «Пока вас не было». true — проверка была отложена и выполнена.
        /// </summary>
        public bool RunDeferredAutomationCheck()
        {
            if (!Mine.AutomationCheckPending || OfflineMode) return false;
            Mine.AutomationCheckPending = false;
            CheckAutomation();
            return true;
        }

        /// <summary>Снять следующий сундук из очереди показа (UI).</summary>
        public ChestResult TakeChestReveal()
        {
            if (Player.ChestReveals.Count == 0) return null;
            var r = Player.ChestReveals[0];
            Player.ChestReveals.RemoveAt(0);
            return r;
        }

        /// <summary>Следующий сундук очереди неоткрытых без снятия (null — очередь пуста). Результат уже определён и начислен.</summary>
        public ChestResult PeekChestReveal() => Player.ChestReveals.Count == 0 ? null : Player.ChestReveals[0];
    }
}
