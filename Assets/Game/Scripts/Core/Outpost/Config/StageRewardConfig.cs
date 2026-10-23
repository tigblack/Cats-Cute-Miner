// Newtonsoft.Json owns these models; they are not Unity Inspector serialization contracts.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Награды первого завершения шахты (ТЗ 10.2, 10.3, 12.3): гарантии 1–4/10/25/50, равновероятный слот,
    /// редкость по порогам броска NextInt(100), детали за завершение и дубликаты. PRNG — Pcg32, поток 5.
    /// </summary>
    public sealed class StageRewardConfig
    {
        public string Version;
        public int RewardRngVersion = OutpostIds.RewardRngVersion;
        public ulong RewardRngStream = 5;
        /// <summary>
        /// Шахты без предмета за первое завершение — только детали (rewards-2, первая сессия): награда шахты 1 — спасённая Mia, шахты 2 —
        /// карточки; экипировка открывается в шахте 3 стартовой киркой (progression.json → EquipmentIntro). Поток PRNG не продвигается.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<int> NoItem;
        /// <summary>Номер шахты (строкой) → ID гарантированного предмета; поток PRNG не продвигается.</summary>
        public Dictionary<string, string> Guaranteed = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Бросок NextInt(100): &lt;70 common, &lt;94 rare, &lt;99 epic, иначе legendary.</summary>
        public int[] RarityThresholds;
        public int CompletionPartsBase;
        public int CompletionPartsPerFive;
        /// <summary>Детали за дубликат по редкости.</summary>
        public int[] DuplicateParts;

        /// <summary>Первое завершение шахты без предмета (только детали).</summary>
        public bool NoItemFor(int zone) => NoItem != null && NoItem.Contains(zone);

        /// <summary>Гарантированный предмет шахты или null.</summary>
        public string GuaranteedItem(int zone) =>
            Guaranteed != null && Guaranteed.TryGetValue(zone.ToString(CultureInfo.InvariantCulture), out var id) ? id : null;

        /// <summary>CompletionParts(z) = Base + PerFive · floor(z / 5).</summary>
        public int CompletionParts(int zone) => CompletionPartsBase + CompletionPartsPerFive * (zone / 5);

        public ItemRarity RarityForRoll(int roll)
        {
            for (int i = 0; i < RarityThresholds.Length; i++)
                if (roll < RarityThresholds[i]) return (ItemRarity)i;
            return (ItemRarity)(RarityThresholds.Length - 1);
        }

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static StageRewardConfig Parse(string json, EquipmentCatalog catalog)
        {
            StageRewardConfig cfg;
            try
            {
                cfg = OutpostJson.Deserialize<StageRewardConfig>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("stage_rewards: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("stage_rewards: пустой документ");
            cfg.Validate(catalog);
            return cfg;
        }

        public void Validate(EquipmentCatalog catalog)
        {
            void Fail(string m) => throw new OutpostConfigException("stage_rewards: " + m);
            if (string.IsNullOrEmpty(Version)) Fail("нет Version");
            if (RewardRngVersion != OutpostIds.RewardRngVersion) Fail("RewardRngVersion должен быть " + OutpostIds.RewardRngVersion);
            if (RarityThresholds == null || RarityThresholds.Length != 4) Fail("RarityThresholds: 4 значения");
            for (int i = 0; i < RarityThresholds.Length; i++)
                if (RarityThresholds[i] <= (i == 0 ? 0 : RarityThresholds[i - 1])) Fail("RarityThresholds должны возрастать");
            if (RarityThresholds[3] != 100) Fail("последний порог должен быть 100");
            if (DuplicateParts == null || DuplicateParts.Length != 4) Fail("DuplicateParts: 4 значения");
            foreach (int p in DuplicateParts)
                if (p < 0) Fail("DuplicateParts < 0");
            if (CompletionPartsBase < 0 || CompletionPartsPerFive < 0) Fail("детали < 0");
            if (Guaranteed == null) Guaranteed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (NoItem != null)
                foreach (int z in NoItem)
                {
                    if (z < 1) Fail("NoItem: неверный номер шахты " + z);
                    if (Guaranteed.ContainsKey(z.ToString(CultureInfo.InvariantCulture))) Fail("NoItem: у шахты " + z + " есть гарантия");
                }
            foreach (var kv in Guaranteed)
            {
                if (!int.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int z) || z < 1)
                    Fail("Guaranteed: неверный номер шахты " + kv.Key);
                if (catalog != null && !catalog.TryGet(kv.Value, out _)) Fail("Guaranteed: нет предмета " + kv.Value);
            }
        }
    }
}
