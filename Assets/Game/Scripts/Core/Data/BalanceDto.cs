// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;
using CatMiners.Core.Text;
using Newtonsoft.Json.Linq;

// DTO исходного json-data.json. Поля названы как в JSON, чтобы Newtonsoft связывал их без атрибутов.
// Все балансовые величины — double (в файле есть целые больше Int64 и литерал 1e+200), коды и счётчики — int.
// Разделы исключённых систем (магазин, задания, Crusher и т. п.) не объявлены и пропускаются при чтении.
//
// Целые значения (ТЗ 5.1 «Загрузка JSON и числа»):
// - поля int (FormulaType, StatModifierType, RewardType, GachaType, Rarity, ObjectiveCount, MaxDupes, размеры,
//   счётчики) проверяются на целость при загрузке: Newtonsoft не читает в int литерал с дробной частью
//   (в том числе 2.0) и бросает исключение, BalanceRepository.Load превращает его в BalanceLoadException
//   с JSON-путём поля (например, "Deliveries[0].MaxDupes");
// - BaseUnitCap (лимит котиков) и MaxFreeGachas (запас бесплатных сундуков) — счётчики штук, а не балансовые
//   пределы вроде CoreCurrencyMax, поэтому int;
// - веса Deliveries[].Weight и Gacha *Weight объявлены double (тип полей не меняется), их целость и
//   неотрицательность проверяет BalanceValidator (пути "Deliveries.<Id>.Weight", "Gacha.<Id>.<поле>").
namespace CatMiners.Core.Data
{
    public sealed class BalanceRoot
    {
        public string Id;
        public BalancePropertiesDto[] BalanceProperties;
        public MinerDto[] Miners;
        public RockDto[] Rocks;
        public MineShaftDto[] MineShafts;
        public ObstructionDto[] Obstructions;
        public Dictionary<string, double[]> MiningTargetHealths;
        public GeneratorObjectiveDto[] GeneratorObjectives;
        public ZoneDto[] Zones;
        public ReinforcementDto[] Reinforcements;
        public SpawningCartDto[] SpawningCart;
        public CheckPointDto[] CheckPoint;
        public JToken Exit;
        public CardUpgradeCostDto[] CardUpgradeCosts;
        public CardDto[] Cards;
        public GachaDto[] Gacha;
        public DeliveryDto[] Deliveries;
        public FreeGachaCycleDto[] FreeGachaCycle;
        public SpellDto[] Spells;
    }

    public sealed class BalancePropertiesDto
    {
        public string ThemeId;
        public bool IsWorldEvergreen;
        public int RankUpType;
        /// <summary>ID основной валюты; валидатор требует "gold" — по нему ключуются журнал начислений и тексты событий.</summary>
        public string CoreCurrencyId;
        /// <summary>ID мягкой валюты; валидатор требует "elixir".</summary>
        public string SoftCurrencyId;
        public double StartingCoreCurrency;
        public double NoGeneratorsCurrencyRate;
        /// <summary>Базовый лимит котиков на поле — счётчик штук (int, целость проверяется при загрузке).</summary>
        public int BaseUnitCap;
        public int[] CardStackCounts;
        public int[] CommonStackSplitAt;
        public int[] UncommonStackSplitAt;
        public int[] RareStackSplitAt;
        public int[] EventEpicStackSplitAt;
        public double DeliveryDelaySecBase;
        public double DeliveryDelaySecMultiplier;
        public double DeliveryDelaySecGrowth;
        public int DeliveryDelaySecFormulaType;
        public double DeliveryDelaySecVariance;
        public double DeliveryClaimCountResetSec;
        public double DeliveryMaxDupesResetSec;
        public double DeliveryStartupDelaySec;
        public int DeliveryStartMineShaftNum;
        public int DeliveryStartRank;
        public int DeliveryForeverAloneDelta;
    }

    public sealed class MinerDto
    {
        public string Id;
        public int WidthCells;
        public int DepthCells;
        public double DamageBase, DamageMultiplier, DamageGrowth;
        public int DamageFormulaType;
        public double CritChanceBase, CritChanceMultiplier, CritChanceGrowth;
        public int CritChanceFormulaType;
        public double CritPowerBase, CritPowerMultiplier, CritPowerGrowth;
        public int CritPowerFormulaType;
        public double AttackDelaySecBase, AttackDelaySecMultiplier, AttackDelaySecGrowth;
        public int AttackDelaySecFormulaType;
    }

    public sealed class RewardModelDto
    {
        public int RewardType;
        public string DetailedType;
        public double Quantity;
    }

    public sealed class RockDto
    {
        public string Id;
        public string MimicId;
        public int WidthCells;
        public int DepthCells;
        public int CoreCurrencySecondsFormulaType;
        public double CoreCurrencySecondsBase, CoreCurrencySecondsMultiplier, CoreCurrencySecondsGrowth, CoreCurrencySecondsPower, CoreCurrencySecondsRound;
        public double SoftCurrencyBase, SoftCurrencyMultiplier, SoftCurrencyGrowth;
        public int SoftCurrencyFormulaType;
        public RewardModelDto[] RewardCycle;
    }

    public sealed class MineShaftDto
    {
        public string Id;
        public int WidthCells;
        public int DepthCells;
        public double GenerationDelaySecBase, GenerationDelaySecMultiplier, GenerationDelaySecGrowth;
        public int GenerationDelaySecFormulaType;
        public int CurrencyType;
        public double CurrencyOutputBase, CurrencyOutputMultiplier, CurrencyOutputGrowth;
        public int CurrencyOutputFormulaType;
        public double UpgradeCostBase, UpgradeCostMultiplier, UpgradeCostGrowth;
        public int UpgradeCostFormulaType;
    }

    public sealed class ObstructionDto
    {
        public string Id;
        public int WidthCells;
        public int DepthCells;
    }

    public sealed class GeneratorObjectiveDto
    {
        public string GeneratorId;
        public int[] ObjectiveCount;
        public double[] CoreCurrencyMultiplier;
        public double SoftCurrencyRewardBase, SoftCurrencyRewardMultiplier, SoftCurrencyRewardGrowth;
        public int SoftCurrencyRewardFormulaType;
    }

    public sealed class RankMultipliersDto
    {
        public double GenObjectiveSoftCurrencyMultiplier;
        public double MiningSoftCurrencyMultiplier;
        public double GachaSoftCurrencyMultNormal, GachaSoftCurrencyMultPremium, GachaSoftCurrencyMultRare;
        public double MiningLeaderboardCurrencyMultiplier;
        public double GachaLeaderboardCurrencyMultNormal, GachaLeaderboardCurrencyMultPremium, GachaLeaderboardCurrencyMultRare;
        public double GachaCardsMultNormal, GachaCardsMultPremium, GachaCardsMultRare;
    }

    public sealed class ZoneDto
    {
        public string Id;
        public int WidthCells;
        public int DepthCells;
        public int FogOfWarCheckpoint;
        public int DecoSeed;
        public RankMultipliersDto[] RankMultipliers;
        public string Grid;
        public int CrusherGridId;
        public JToken GridRowRanks;
    }

    public sealed class ReinforcementDto
    {
        /// <summary>Типы котиков найма по кругу; валидатор требует непустой список ID из Miners (в v1 — только tinygoblin).</summary>
        public string[] MinerIdCycle;
        public double CostBase, CostMultiplier, CostGrowth;
        public int CostFormulaType;
        public double LevelBase, LevelMultiplier, LevelGrowth;
        public int LevelFormulaType;
    }

    public sealed class SpawningCartDto
    {
        /// <summary>Типы бесплатных котиков по кругу; валидатор требует непустой список ID из Miners.</summary>
        public string[] MinerIdCycle;
        public int SpawnLevelOffset;
        public double SpawnDelaySecBase, SpawnDelaySecMultiplier, SpawnDelaySecGrowth;
        public int SpawnDelaySecFormulaType;
        public double GenerationDelaySecBase, GenerationDelaySecMultiplier, GenerationDelaySecGrowth;
        public int GenerationDelaySecFormulaType;
        public int CurrencyType;
        public double CurrencyOutputBase, CurrencyOutputMultiplier, CurrencyOutputGrowth;
        public int CurrencyOutputFormulaType;
        public double UpgradeCostBase, UpgradeCostMultiplier, UpgradeCostGrowth;
        public int UpgradeCostFormulaType;
    }

    public sealed class CheckPointDto
    {
        public string Id;
        public int[] StatModifierType;
        public double[] ModifierBase;
        public double[] ModifierMultiplier;
        public double[] ModifierGrowth;
        public double[] ModifierPower;
        public double[] ModifierRound;
        public int[] ModifierFormulaType;
    }

    public sealed class CardUpgradeCostDto
    {
        public int Rarity;
        public int[] Duplicates;
        public double[] SoftCurrency;
    }

    public sealed class CardDto
    {
        public string Id;
        public int Rarity;
        public int MineUnlock;
        public int SortingWeight;
        public int StatModifierType;
        public double ModifierBase, ModifierMultiplier, ModifierGrowth, ModifierPower, ModifierRound;
        public int ModifierFormulaType;
        public string[] TargetIds;
        public bool IsManager;
    }

    public sealed class GachaDto
    {
        public string Id;
        public int GachaType;
        public string MimicId;
        public double CardMultMax;
        public string[] GuaranteedCardIds;
        public int[] GuaranteedCardMin;
        public int[] GuaranteedCardCounts;
        public double SoftCurrencyMin;
        public double SoftCurrencyMax;
        public double LeaderboardCurrency;
        public int BaseNumCards;
        /// <summary>Веса редкостей — по ТЗ 5.1 счётчики; хранятся как double, целость и знак проверяет BalanceValidator.</summary>
        public double UncommonWeight;
        public double RareWeight;
        public double LegendaryWeight;
        public double EventEpicWeight;
        public double LowCardChanceMult;
        public double ManagerChanceMult;
    }

    public sealed class DeliveryDto
    {
        public string Id;
        public RewardModelDto RewardModel;
        public double QuantityBase, QuantityMultiplier, QuantityGrowth, QuantityPower, QuantityRound;
        public int QuantityFormulaType;
        /// <summary>Вес в пуле доставок — по ТЗ 5.1 счётчик; хранится как double, целость и знак проверяет BalanceValidator.</summary>
        public double Weight;
        public int MaxDupes;
        public int RankUnlock;
        public int CheckPointUnlock;
        public bool IsAd;
    }

    public sealed class FreeGachaCycleDto
    {
        public int StartRank;
        public int StartMineshaftNum;
        /// <summary>Максимальный запас бесплатных сундуков — счётчик штук (int, целость проверяется при загрузке).</summary>
        public int MaxFreeGachas;
        public double Hours;
        public string[] GachaIds;
    }

    public sealed class SpellDto
    {
        public int SpellType;
        public double EffectBase, EffectMultiplier, EffectGrowth;
        public int EffectFormulaType;
        public double CritChanceBase, CritChanceMultiplier, CritChanceGrowth;
        public int CritChanceFormulaType;
        public double CritPowerBase, CritPowerMultiplier, CritPowerGrowth;
        public int CritPowerFormulaType;
    }

    /// <summary>Коды RewardType, встречающиеся в разделах ядра (ТЗ 10.2).</summary>
    public static class RewardTypes
    {
        public const int Currency = 0;
        public const int Gacha = 1;
        public const int Miner = 4;
        public const int Spell = 9;
    }

    /// <summary>Редкости карточек [П]: 1 Common … 6 Majestic.</summary>
    public static class Rarity
    {
        public const int Common = 1;
        public const int Uncommon = 2;
        public const int Rare = 3;
        public const int Epic = 4;
        public const int Legendary = 5;
        public const int Majestic = 6;

        public static string Name(int rarity)
        {
            switch (rarity)
            {
                case Common: return Loc.T("core.rarity.common");
                case Uncommon: return Loc.T("core.rarity.uncommon");
                case Rare: return Loc.T("core.rarity.rare");
                case Epic: return Loc.T("core.rarity.epic");
                case Legendary: return Loc.T("core.rarity.legendary");
                case Majestic: return Loc.T("core.rarity.majestic");
                default: return Loc.T("core.rarity.unknown");
            }
        }
    }
}
