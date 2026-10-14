using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CatMiners.Core.Grid;

namespace CatMiners.Core.Data
{
    public sealed class ValidationIssue
    {
        public string Path;      // путь до поля / зона и индекс токена
        public string Message;
        public bool IsWarning;
        public override string ToString() => (IsWarning ? "WARN " : "ERROR ") + Path + ": " + Message;
    }

    public sealed class ZoneSummary
    {
        public string ZoneId;
        public int Width, Depth, Rocks, Shafts, Gates, Obstructions, OccupiedRsx, SectorCount;
        public int ExitCol, ExitRow, ExitLevel, CartCol, CartRow;
        public string ExitGacha;
        public int[] GateRows;
        public int[] GateLevels;
        public string[] ShaftSpecs;  // id:level:card:cardLevel
    }

    public sealed class ValidationReport
    {
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();
        public readonly List<ZoneSummary> Zones = new List<ZoneSummary>();
        public string Sha256;
        public long SizeBytes;

        public int ErrorCount => Issues.Count(i => !i.IsWarning);
        public bool Ok => ErrorCount == 0;

        public void Error(string path, string message) => Issues.Add(new ValidationIssue { Path = path, Message = message });
        public void Warn(string path, string message) => Issues.Add(new ValidationIssue { Path = path, Message = message, IsWarning = true });

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("SHA-256: " + Sha256 + "  size: " + SizeBytes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("zones: " + Zones.Count.ToString(CultureInfo.InvariantCulture) + "  errors: " + ErrorCount.ToString(CultureInfo.InvariantCulture) +
                          "  warnings: " + (Issues.Count - ErrorCount).ToString(CultureInfo.InvariantCulture));
            foreach (var i in Issues.Take(200)) sb.AppendLine(i.ToString());
            return sb.ToString();
        }
    }

    /// <summary>
    /// Проверки схемы, ссылок, сеток и инвариантов данных (ТЗ 6.4, 17.1 п. 1–3, C.1).
    /// Ошибка выдаётся с путём поля, ID зоны и индексом токена.
    /// </summary>
    public static class BalanceValidator
    {
        static readonly int[] AppliedCardCodes = { 9, 10, 7, 1 };

        public static ValidationReport Validate(BalanceRepository b)
        {
            var report = new ValidationReport { Sha256 = b.Sha256, SizeBytes = b.SizeBytes };
            ValidateSchema(b, report);
            for (int n = 1; n <= b.ZoneCount; n++)
            {
                var zone = b.Zone(n);
                string expectedId = "zone" + n.ToString(CultureInfo.InvariantCulture);
                if (zone.Id != expectedId) report.Error("Zones", "ожидалась " + expectedId + ", найдена " + zone.Id);
                ValidateZone(b, zone, report);
            }
            return report;
        }

        static void ValidateSchema(BalanceRepository b, ValidationReport r)
        {
            var root = b.Root;
            if (root.Id != "evergreen") r.Warn("Id", "ожидался evergreen, найден " + root.Id);
            if (b.ZoneCount != 144) r.Error("Zones", "ожидалось 144 зоны, найдено " + b.ZoneCount);
            if (b.Props.StartingCoreCurrency <= 0) r.Error("BalanceProperties[0].StartingCoreCurrency", "должно быть > 0");
            if (b.Props.BaseUnitCap <= 0) r.Error("BalanceProperties[0].BaseUnitCap", "должно быть > 0");
            if (b.Props.CardStackCounts == null || b.Props.CardStackCounts.Length == 0) r.Error("BalanceProperties[0].CardStackCounts", "пусто");
            ValidateCurrencyIds(b, r);
            ValidateMinerIdCycle(b, b.Reinforcement.MinerIdCycle, "Reinforcements[0].MinerIdCycle", r);
            ValidateMinerIdCycle(b, b.Cart.MinerIdCycle, "SpawningCart[0].MinerIdCycle", r);

            foreach (var kv in root.MiningTargetHealths)
            {
                var t = kv.Value;
                if (t == null || t.Length == 0) { r.Error("MiningTargetHealths." + kv.Key, "пустая таблица"); continue; }
                for (int i = 0; i < t.Length; i++)
                    if (double.IsNaN(t[i]) || double.IsInfinity(t[i]) || t[i] <= 0)
                        r.Error("MiningTargetHealths." + kv.Key + "[" + i + "]", "недопустимое значение " + t[i].ToString(CultureInfo.InvariantCulture));
            }

            // Инварианты рабочего профиля (C.1):
            // 1) UpgradeCostMultiplier = 0 у всех 21 генераторов;
            // 2) B = 0 у Miners.Damage, Reinforcements.Cost и manager-карт;
            // 3) G = 0 у всех применяемых в v1 записей FT0 (RequireFt0Linear и проверка карт ниже).
            var cart = b.Cart;
            if (cart.UpgradeCostMultiplier != 0) r.Error("SpawningCart[0].UpgradeCostMultiplier", "ожидался 0");
            RequireFt0Linear(r, "SpawningCart[0].CurrencyOutput", cart.CurrencyOutputFormulaType, cart.CurrencyOutputGrowth);
            RequireFt0Linear(r, "SpawningCart[0].SpawnDelaySec", cart.SpawnDelaySecFormulaType, cart.SpawnDelaySecGrowth);
            RequireFt0Linear(r, "SpawningCart[0].GenerationDelaySec", cart.GenerationDelaySecFormulaType, cart.GenerationDelaySecGrowth);
            foreach (var s in b.Shafts.Values)
            {
                if (s.UpgradeCostMultiplier != 0) r.Error("MineShafts." + s.Id + ".UpgradeCostMultiplier", "ожидался 0");
                if (s.UpgradeCostFormulaType != 2) r.Error("MineShafts." + s.Id + ".UpgradeCostFormulaType", "ожидался 2");
                RequireFt0Linear(r, "MineShafts." + s.Id + ".CurrencyOutput", s.CurrencyOutputFormulaType, s.CurrencyOutputGrowth);
                RequireFt0Linear(r, "MineShafts." + s.Id + ".GenerationDelaySec", s.GenerationDelaySecFormulaType, s.GenerationDelaySecGrowth);
                if (!b.Objectives.ContainsKey(s.Id)) r.Error("GeneratorObjectives", "нет записи для " + s.Id);
                if (!b.HasHpTable(s.Id)) r.Error("MiningTargetHealths", "нет таблицы для шахты " + s.Id);
            }
            if (!b.Objectives.ContainsKey(BalanceRepository.CartId)) r.Error("GeneratorObjectives", "нет записи для spawningcart");
            var miner = b.Miner;
            string minerPath = "Miners." + miner.Id;
            if (miner.DamageBase != 0) r.Error(minerPath + ".DamageBase", "ожидался 0");
            RequireFt0Linear(r, minerPath + ".AttackDelaySec", miner.AttackDelaySecFormulaType, miner.AttackDelaySecGrowth);
            RequireFt0Linear(r, minerPath + ".CritChance", miner.CritChanceFormulaType, miner.CritChanceGrowth);
            RequireFt0Linear(r, minerPath + ".CritPower", miner.CritPowerFormulaType, miner.CritPowerGrowth);
            if (b.Reinforcement.CostBase != 0) r.Error("Reinforcements[0].CostBase", "ожидался 0");
            // Уровень найма — F-0 с x = n (E-05).
            RequireFt0Linear(r, "Reinforcements[0].Level", b.Reinforcement.LevelFormulaType, b.Reinforcement.LevelGrowth);
            // Динамит (E-16): урон — F-1 (B = 750, допустимая запись), крит — F-0.
            RequireFt0Linear(r, "Spells[0].CritChance", b.Spell.CritChanceFormulaType, b.Spell.CritChanceGrowth);
            RequireFt0Linear(r, "Spells[0].CritPower", b.Spell.CritPowerFormulaType, b.Spell.CritPowerGrowth);

            foreach (var g in b.Objectives.Values)
            {
                // Эликсир за порог (E-04) — F-0.
                RequireFt0Linear(r, "GeneratorObjectives." + g.GeneratorId + ".SoftCurrencyReward", g.SoftCurrencyRewardFormulaType, g.SoftCurrencyRewardGrowth);
                if (g.ObjectiveCount == null || g.CoreCurrencyMultiplier == null || g.ObjectiveCount.Length != g.CoreCurrencyMultiplier.Length)
                {
                    r.Error("GeneratorObjectives." + g.GeneratorId, "длины ObjectiveCount и CoreCurrencyMultiplier не совпадают");
                    continue;
                }
                int sum = g.ObjectiveCount.Sum();
                if (sum != 1199) r.Warn("GeneratorObjectives." + g.GeneratorId + ".ObjectiveCount", "сумма " + sum + ", ожидалась 1199 (максимум уровня 1200)");
            }

            foreach (var c in b.CardsInOrder)
            {
                string path = "Cards." + c.Id;
                if (!b.CardUpgradeCosts.ContainsKey(c.Rarity)) r.Error(path + ".Rarity", "нет CardUpgradeCosts для редкости " + c.Rarity);
                if (c.IsManager)
                {
                    if (c.StatModifierType != 9) r.Error(path + ".StatModifierType", "manager-карта с кодом " + c.StatModifierType);
                    if (c.ModifierBase != 0) r.Error(path + ".ModifierBase", "у manager-карты ожидался 0");
                    if (c.TargetIds == null || c.TargetIds.Length == 0) r.Error(path + ".TargetIds", "пусто");
                }
                if (Array.IndexOf(AppliedCardCodes, c.StatModifierType) >= 0 && c.ModifierFormulaType == 0 && c.ModifierGrowth != 0)
                    r.Error(path + ".ModifierGrowth", "применяемая FT0-карта с квадратичным членом");
            }

            foreach (var g in b.Gacha.Values)
            {
                string path = "Gacha." + g.Id;
                int n = g.GuaranteedCardIds?.Length ?? 0;
                if ((g.GuaranteedCardCounts?.Length ?? 0) != n) r.Error(path + ".GuaranteedCardCounts", "длина не совпадает с GuaranteedCardIds");
                if (g.GuaranteedCardMin != null && g.GuaranteedCardMin.Length != 0 && g.GuaranteedCardMin.Length != n)
                    r.Error(path + ".GuaranteedCardMin", "длина не совпадает с GuaranteedCardIds");
                if (g.GuaranteedCardIds != null)
                    foreach (var id in g.GuaranteedCardIds)
                        if (!b.Cards.ContainsKey(id)) r.Error(path + ".GuaranteedCardIds", "неизвестная карточка " + id);
                if (g.SoftCurrencyMax < g.SoftCurrencyMin) r.Error(path + ".SoftCurrencyMax", "меньше SoftCurrencyMin");
                // Веса редкостей — счётчики (ТЗ 5.1): целые и неотрицательные.
                RequireWholeCount(r, path + ".UncommonWeight", g.UncommonWeight);
                RequireWholeCount(r, path + ".RareWeight", g.RareWeight);
                RequireWholeCount(r, path + ".LegendaryWeight", g.LegendaryWeight);
                RequireWholeCount(r, path + ".EventEpicWeight", g.EventEpicWeight);
            }

            foreach (var rock in b.Rocks.Values)
            {
                string path = "Rocks." + rock.Id;
                if (rock.RewardCycle == null || rock.RewardCycle.Length == 0) r.Error(path + ".RewardCycle", "пусто");
                else
                {
                    for (int i = 0; i < rock.RewardCycle.Length; i++)
                    {
                        var rw = rock.RewardCycle[i];
                        if (rw.RewardType == RewardTypes.Gacha && !b.Gacha.ContainsKey(rw.DetailedType))
                            r.Error(path + ".RewardCycle[" + i + "]", "неизвестный сундук " + rw.DetailedType);
                        else if (rw.RewardType != RewardTypes.Gacha && rw.RewardType != RewardTypes.Currency)
                            r.Error(path + ".RewardCycle[" + i + "]", "RewardType " + rw.RewardType + " не поддерживается в камнях");
                    }
                }
                if (!b.HasHpTable(rock.Id)) r.Error("MiningTargetHealths", "нет таблицы для камня " + rock.Id);
                // Эликсир камня (E-09) — F-0.
                RequireFt0Linear(r, path + ".SoftCurrency", rock.SoftCurrencyFormulaType, rock.SoftCurrencyGrowth);
                if (!string.IsNullOrEmpty(rock.MimicId) && !b.Rocks.ContainsKey(rock.MimicId)) r.Error(path + ".MimicId", "неизвестный " + rock.MimicId);
            }

            foreach (var d in b.Deliveries.Values)
            {
                string path = "Deliveries." + d.Id;
                int rt = d.RewardModel?.RewardType ?? -1;
                if (rt != RewardTypes.Currency && rt != RewardTypes.Miner && rt != RewardTypes.Spell)
                    r.Error(path + ".RewardModel.RewardType", "не поддерживается: " + rt);
                if (d.QuantityFormulaType != 3) r.Error(path + ".QuantityFormulaType", "ожидался 3");
                RequireWholeCount(r, path + ".Weight", d.Weight);
                string detailed = d.RewardModel?.DetailedType;
                if (rt == RewardTypes.Miner && !IsMinerId(b, detailed))
                    r.Error(path + ".RewardModel.DetailedType", "котик доставки должен быть типом из Miners, найдено " + (detailed ?? "null"));
                if (rt == RewardTypes.Currency && detailed != CurrencyCore && detailed != CurrencySoft)
                    r.Error(path + ".RewardModel.DetailedType", "валюта доставки должна быть Core или Soft, найдено " + (detailed ?? "null"));
            }

            foreach (var id in b.FreeGachaCycle.GachaIds)
                if (!b.Gacha.ContainsKey(id)) r.Error("FreeGachaCycle[0].GachaIds", "неизвестный сундук " + id);

            var cp = b.CheckPoint;
            if (cp.StatModifierType == null || cp.ModifierBase == null || cp.StatModifierType.Length != cp.ModifierBase.Length)
                r.Error("CheckPoint[0]", "длины StatModifierType и ModifierBase не совпадают");
            ValidateCheckpointFt0(cp, r);
        }

        /// <summary>Значения BalanceProperties.CoreCurrencyId / SoftCurrencyId, на которые опирается код (журнал начислений, тексты событий).</summary>
        public const string CoreCurrencyIdExpected = "gold";
        public const string SoftCurrencyIdExpected = "elixir";

        /// <summary>DetailedType валюты в доставках: Core — золото, Soft — эликсир (ТЗ 10.2, 10.5).</summary>
        const string CurrencyCore = "Core";
        const string CurrencySoft = "Soft";

        /// <summary>
        /// Инвариант C.1 «G = 0 у всех применяемых в v1 записей FT0»: у записи <paramref name="path"/> FormulaType = 0 и Growth = 0.
        /// Ошибки — по путям path + "FormulaType" / path + "Growth".
        /// Допустимые не-FT0 записи (DeliveryDelaySec — FT2, урон Spells — FT1 и т. п.) сюда не передаются.
        /// </summary>
        static void RequireFt0Linear(ValidationReport r, string path, int formulaType, double growth)
        {
            if (formulaType != 0) r.Error(path + "FormulaType", "ожидался 0 (F-0), найден " + formulaType.ToString(CultureInfo.InvariantCulture));
            if (growth != 0) r.Error(path + "Growth", "ожидался 0 (FT0 без квадратичного члена), найден " + growth.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Счётчик, хранящийся в double (веса, ТЗ 5.1): конечный, ≥ 0 и целый.</summary>
        static void RequireWholeCount(ValidationReport r, string path, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value != Math.Floor(value))
                r.Error(path, "ожидалось целое ≥ 0, найдено " + value.ToString("R", CultureInfo.InvariantCulture));
        }

        static bool IsMinerId(BalanceRepository b, string id)
        {
            if (string.IsNullOrEmpty(id) || b.Root.Miners == null) return false;
            foreach (var m in b.Root.Miners)
                if (m != null && m.Id == id) return true;
            return false;
        }

        /// <summary>MinerIdCycle найма и кузницы: непустой список, каждый элемент — Id из Miners (ТЗ 3.1, раздел «В области»).</summary>
        static void ValidateMinerIdCycle(BalanceRepository b, string[] cycle, string path, ValidationReport r)
        {
            if (cycle == null || cycle.Length == 0)
            {
                r.Error(path, "пусто");
                return;
            }
            for (int i = 0; i < cycle.Length; i++)
                if (!IsMinerId(b, cycle[i]))
                    r.Error(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", "неизвестный тип котика " + (string.IsNullOrEmpty(cycle[i]) ? "(пусто)" : cycle[i]));
        }

        /// <summary>ID валют: заданы, различны и совпадают с теми, на которые опирается код (gold / elixir).</summary>
        static void ValidateCurrencyIds(BalanceRepository b, ValidationReport r)
        {
            string core = b.Props.CoreCurrencyId, soft = b.Props.SoftCurrencyId;
            if (string.IsNullOrEmpty(core)) r.Error("BalanceProperties[0].CoreCurrencyId", "пусто");
            else if (core != CoreCurrencyIdExpected) r.Error("BalanceProperties[0].CoreCurrencyId", "ожидался " + CoreCurrencyIdExpected + ", найден " + core);
            if (string.IsNullOrEmpty(soft)) r.Error("BalanceProperties[0].SoftCurrencyId", "пусто");
            else if (soft != SoftCurrencyIdExpected) r.Error("BalanceProperties[0].SoftCurrencyId", "ожидался " + SoftCurrencyIdExpected + ", найден " + soft);
            if (!string.IsNullOrEmpty(core) && core == soft) r.Error("BalanceProperties[0].SoftCurrencyId", "совпадает с CoreCurrencyId");
        }

        /// <summary>
        /// Бонусы ворот (E-08): все массивы CheckPoint[0] длины StatModifierType; каждая запись — F-0 с M = 0 и G = 0,
        /// то есть бонус равен ModifierBase при любом числе открытых ворот.
        /// </summary>
        static void ValidateCheckpointFt0(CheckPointDto cp, ValidationReport r)
        {
            if (cp.StatModifierType == null) return;
            int n = cp.StatModifierType.Length;
            bool ok = true;
            ok &= RequireLength(r, "CheckPoint[0].ModifierFormulaType", cp.ModifierFormulaType?.Length, n);
            ok &= RequireLength(r, "CheckPoint[0].ModifierMultiplier", cp.ModifierMultiplier?.Length, n);
            ok &= RequireLength(r, "CheckPoint[0].ModifierGrowth", cp.ModifierGrowth?.Length, n);
            ok &= RequireLength(r, "CheckPoint[0].ModifierPower", cp.ModifierPower?.Length, n);
            ok &= RequireLength(r, "CheckPoint[0].ModifierRound", cp.ModifierRound?.Length, n);
            if (!ok) return;
            for (int i = 0; i < n; i++)
            {
                string idx = "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (cp.ModifierFormulaType[i] != 0) r.Error("CheckPoint[0].ModifierFormulaType" + idx, "ожидался 0 (F-0)");
                if (cp.ModifierGrowth[i] != 0) r.Error("CheckPoint[0].ModifierGrowth" + idx, "ожидался 0 (FT0 без квадратичного члена)");
                if (cp.ModifierMultiplier[i] != 0) r.Error("CheckPoint[0].ModifierMultiplier" + idx, "ожидался 0 (бонус ворот — константа ModifierBase)");
            }
        }

        static bool RequireLength(ValidationReport r, string path, int? length, int expected)
        {
            if (length == expected) return true;
            r.Error(path, "длина " + (length?.ToString(CultureInfo.InvariantCulture) ?? "null") + ", ожидалась " + expected.ToString(CultureInfo.InvariantCulture) + " (как у StatModifierType)");
            return false;
        }

        static void ValidateZone(BalanceRepository b, ZoneDto zone, ValidationReport r)
        {
            var layout = ZoneLayout.Build(b, zone);
            foreach (var issue in layout.Issues) r.Error("Zones." + issue.ZoneId + (issue.TokenIndex >= 0 ? ".Grid[" + issue.TokenIndex + "]" : ""), issue.Message);
            if (!layout.IsValid) return;

            if (zone.FogOfWarCheckpoint != 0) r.Warn("Zones." + zone.Id + ".FogOfWarCheckpoint", "ожидался 0");
            if (zone.GridRowRanks != null && zone.GridRowRanks.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                r.Warn("Zones." + zone.Id + ".GridRowRanks", "ожидался null");
            if (zone.RankMultipliers == null || zone.RankMultipliers.Length != 1)
                r.Error("Zones." + zone.Id + ".RankMultipliers", "ожидался массив ровно из одного объекта");

            // Строка ворот перекрыта полностью: (0,row) и (6,row) заняты препятствиями.
            foreach (var g in layout.Gates)
            {
                for (int c = 0; c < layout.Width; c++)
                {
                    int owner = layout.CellOwner[layout.CellIndex(c, g.Row)];
                    if (owner < 0) r.Error("Zones." + zone.Id + ".Grid[" + g.Index + "]", "строка ворот не перекрыта в столбце " + c);
                }
            }
            // Выход у верхней границы.
            if (layout.Exit.Row != 0 || layout.Exit.Col != 2) r.Error("Zones." + zone.Id + ".Grid[" + layout.Exit.Index + "]", "выход не в (2,0)");

            // Ширина — ровно 7 столбцов (максимум сетки); перед игроком — зона мержа: два соседних полностью свободных
            // ряда на стартовой площадке и над каждыми воротами (ТЗ 6.4, 6.6).
            if (layout.Width != ZoneLayout.MaxWidth) r.Error("Zones." + zone.Id + ".WidthCells", "ширина " + layout.Width + ", ожидалось " + ZoneLayout.MaxWidth);
            layout.StartPadRows(out int startTop, out int startBottom);
            if (!layout.TryMergeRows(startTop, startBottom, out _, out _))
                r.Error("Zones." + zone.Id + ".Grid[" + layout.Cart.Index + "]", "на стартовой площадке нет двух соседних свободных рядов во всю ширину (зона мержа)");
            for (int gi = 0; gi < layout.Gates.Count; gi++)
            {
                layout.GatePadRows(gi, out int padTop, out int padBottom);
                if (!layout.TryMergeRows(padTop, padBottom, out _, out _))
                    r.Error("Zones." + zone.Id + ".Grid[" + layout.Gates[gi].Index + "]", "над воротами нет двух соседних свободных рядов во всю ширину (зона мержа)");
            }

            // Сектора 4-связны.
            for (int s = 0; s < layout.SectorCount; s++)
            {
                int components = CountFreeComponents(layout, s);
                if (components != 1) r.Error("Zones." + zone.Id, "сектор " + s + ": свободные клетки образуют " + components + " областей");
            }

            // Потенциальные рабочие клетки у s/p/e.
            foreach (var obj in layout.Objects)
            {
                if (obj.Kind != ObjectKind.Shaft && obj.Kind != ObjectKind.Gate && obj.Kind != ObjectKind.Exit) continue;
                if (CountPotentialWorkCells(layout, obj) == 0)
                    r.Error("Zones." + zone.Id + ".Grid[" + obj.Index + "]", obj.Token + ": нет ни одной потенциальной рабочей клетки");
            }

            // Требования карточек укладываются в максимум уровня.
            foreach (var s in layout.Shafts)
            {
                var card = b.Cards[s.CardId];
                if (!card.IsManager) r.Error("Zones." + zone.Id + ".Grid[" + s.Index + "]", "карта " + s.CardId + " не manager");
                if (card.TargetIds == null || Array.IndexOf(card.TargetIds, s.TypeId) < 0)
                    r.Error("Zones." + zone.Id + ".Grid[" + s.Index + "]", "карта " + s.CardId + " не нацелена на " + s.TypeId);
                if (s.CardLevel > b.CardMaxLevel(card.Rarity))
                    r.Error("Zones." + zone.Id + ".Grid[" + s.Index + "]", "требуемый уровень " + s.CardLevel + " выше максимума " + b.CardMaxLevel(card.Rarity));
            }

            // Достижимость при расчистке от старта.
            var unreachable = ReachabilityCheck(layout);
            foreach (var obj in unreachable)
                r.Error("Zones." + zone.Id + ".Grid[" + obj.Index + "]", obj.Token + ": недостижима при расчистке от старта");

            r.Zones.Add(Summarize(layout));
        }

        static ZoneSummary Summarize(ZoneLayout layout)
        {
            var s = new ZoneSummary
            {
                ZoneId = layout.ZoneId,
                Width = layout.Width,
                Depth = layout.Depth,
                SectorCount = layout.SectorCount,
                ExitCol = layout.Exit.Col,
                ExitRow = layout.Exit.Row,
                ExitLevel = layout.Exit.Level,
                ExitGacha = layout.Exit.RewardId,
                CartCol = layout.Cart.Col,
                CartRow = layout.RowC,
            };
            foreach (var o in layout.Objects)
            {
                switch (o.Kind)
                {
                    case ObjectKind.Rock: s.Rocks++; s.OccupiedRsx += o.Cells.Length; break;
                    case ObjectKind.Shaft: s.Shafts++; s.OccupiedRsx += o.Cells.Length; break;
                    case ObjectKind.Obstruction: s.Obstructions++; s.OccupiedRsx += o.Cells.Length; break;
                    case ObjectKind.Gate: s.Gates++; break;
                }
            }
            var gatesTopDown = layout.Gates.OrderBy(g => g.Row).ToList();
            s.GateRows = gatesTopDown.Select(g => g.Row).ToArray();
            s.GateLevels = gatesTopDown.Select(g => g.Level).ToArray();
            s.ShaftSpecs = layout.Shafts.Select(x => x.TypeId + ":" + x.Level + ":" + x.CardId + ":" + x.CardLevel).ToArray();
            return s;
        }

        static int CountFreeComponents(ZoneLayout layout, int sector)
        {
            var seen = new bool[layout.CellCount];
            var stack = new Stack<int>();
            var nb = new List<int>(4);
            int components = 0;
            for (int cell = 0; cell < layout.CellCount; cell++)
            {
                if (seen[cell] || layout.CellOwner[cell] >= 0 || layout.CellSector[cell] != sector) continue;
                components++;
                seen[cell] = true;
                stack.Push(cell);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    layout.Neighbors4(cur, nb);
                    foreach (int n in nb)
                    {
                        if (seen[n] || layout.CellOwner[n] >= 0 || layout.CellSector[n] != sector) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }
            return components;
        }

        static int CountPotentialWorkCells(ZoneLayout layout, ZoneObject obj)
        {
            var nb = new List<int>(4);
            var set = new HashSet<int>();
            int targetSector = obj.Kind == ObjectKind.Gate ? layout.SectorOfRow(obj.Row + 1) : layout.CellSector[obj.Cells[0]];
            foreach (int cell in obj.Cells)
            {
                layout.Neighbors4(cell, nb);
                foreach (int n in nb)
                {
                    int owner = layout.CellOwner[n];
                    if (owner == obj.Ordinal) continue;
                    if (owner >= 0 && layout.Objects[owner].Kind == ObjectKind.Obstruction) continue;
                    if (owner >= 0 && layout.Objects[owner].Kind != ObjectKind.Rock) continue;
                    if (layout.CellSector[n] != targetSector) continue;
                    set.Add(n);
                }
            }
            return set.Count;
        }

        /// <summary>Итеративная расчистка от старта: цель доступна, если рядом есть свободная клетка открытого сектора.</summary>
        public static List<ZoneObject> ReachabilityCheck(ZoneLayout layout)
        {
            var owner = (int[])layout.CellOwner.Clone();
            var done = new HashSet<int>();
            int openSectors = 1;
            var nb = new List<int>(4);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var obj in layout.Objects)
                {
                    if (!obj.IsTarget || done.Contains(obj.Ordinal)) continue;
                    int targetSector = obj.Kind == ObjectKind.Gate ? layout.SectorOfRow(obj.Row + 1) : layout.CellSector[obj.Cells[0]];
                    if (targetSector < 0 || targetSector >= openSectors) continue;
                    bool adjacent = false;
                    foreach (int cell in obj.Cells)
                    {
                        layout.Neighbors4(cell, nb);
                        foreach (int n in nb)
                        {
                            if (owner[n] >= 0) continue;
                            int s = layout.CellSector[n];
                            if (s >= 0 && s < openSectors) { adjacent = true; break; }
                        }
                        if (adjacent) break;
                    }
                    if (!adjacent) continue;
                    done.Add(obj.Ordinal);
                    changed = true;
                    if (obj.Kind == ObjectKind.Rock)
                        foreach (int cell in obj.Cells) owner[cell] = -1;
                    else if (obj.Kind == ObjectKind.Gate)
                        openSectors = Math.Max(openSectors, obj.GateOrder + 2);
                }
            }
            return layout.Objects.Where(o => o.IsTarget && !done.Contains(o.Ordinal)).ToList();
        }
    }
}
