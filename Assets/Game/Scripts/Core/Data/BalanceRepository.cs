using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace CatMiners.Core.Data
{
    /// <summary>
    /// Загрузка исходного json-data.json, проверка hash и индексы по ID (ТЗ 4.1, 5.2).
    /// </summary>
    public sealed class BalanceRepository
    {
        public const string ExpectedSha256 = "8975a5a09dce8e2675d06b751c50b5cac80da3988f8ed098910492c442f85ee5";
        public const string MinerId = "tinygoblin";
        public const string CartId = "spawningcart";
        public const string CheckpointId = "checkpoint";
        public const string ExitId = "exit";

        public BalanceRoot Root { get; private set; }
        public string Sha256 { get; private set; }
        public long SizeBytes { get; private set; }

        public BalancePropertiesDto Props { get; private set; }
        public MinerDto Miner { get; private set; }
        public ReinforcementDto Reinforcement { get; private set; }
        public SpawningCartDto Cart { get; private set; }
        public CheckPointDto CheckPoint { get; private set; }
        public SpellDto Spell { get; private set; }
        public FreeGachaCycleDto FreeGachaCycle { get; private set; }

        /// <summary>Зоны в порядке кампании: zone1, zone2, …, zone144 (по числовому суффиксу).</summary>
        public IReadOnlyList<ZoneDto> Zones => m_Zones;

        public readonly Dictionary<string, RockDto> Rocks = new Dictionary<string, RockDto>();
        public readonly Dictionary<string, MineShaftDto> Shafts = new Dictionary<string, MineShaftDto>();
        public readonly Dictionary<string, ObstructionDto> Obstructions = new Dictionary<string, ObstructionDto>();
        public readonly Dictionary<string, GeneratorObjectiveDto> Objectives = new Dictionary<string, GeneratorObjectiveDto>();
        public readonly Dictionary<string, CardDto> Cards = new Dictionary<string, CardDto>();
        public readonly Dictionary<int, CardUpgradeCostDto> CardUpgradeCosts = new Dictionary<int, CardUpgradeCostDto>();
        public readonly Dictionary<string, GachaDto> Gacha = new Dictionary<string, GachaDto>();
        public readonly Dictionary<string, DeliveryDto> Deliveries = new Dictionary<string, DeliveryDto>();
        public readonly List<CardDto> CardsInOrder = new List<CardDto>();

        readonly List<ZoneDto> m_Zones = new List<ZoneDto>();

        public static BalanceRepository Load(byte[] bytes, bool requireExpectedHash = true)
        {
            if (bytes == null || bytes.Length == 0) throw new BalanceLoadException("Пустой файл баланса.");
            string hash = ComputeSha256(bytes);
            if (requireExpectedHash && !string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new BalanceLoadException("SHA-256 баланса не совпадает: ожидался " + ExpectedSha256 + ", получен " + hash);

            string json = DecodeUtf8(bytes);
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                FloatParseHandling = FloatParseHandling.Double,
                NullValueHandling = NullValueHandling.Include,
                Culture = CultureInfo.InvariantCulture,
            };
            BalanceRoot root;
            try
            {
                root = JsonConvert.DeserializeObject<BalanceRoot>(json, settings);
            }
            catch (JsonException e)
            {
                throw new BalanceLoadException("Ошибка разбора JSON баланса: " + e.Message, e);
            }
            var repo = new BalanceRepository { Root = root, Sha256 = hash, SizeBytes = bytes.LongLength };
            repo.BuildIndexes();
            return repo;
        }

        public static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                foreach (var b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string DecodeUtf8(byte[] bytes)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }

        void BuildIndexes()
        {
            var r = Root;
            Require(r.BalanceProperties, "BalanceProperties");
            Require(r.Reinforcements, "Reinforcements");
            Require(r.SpawningCart, "SpawningCart");
            Require(r.CheckPoint, "CheckPoint");
            Require(r.Spells, "Spells");
            Require(r.FreeGachaCycle, "FreeGachaCycle");
            Props = r.BalanceProperties[0];
            Reinforcement = r.Reinforcements[0];
            Cart = r.SpawningCart[0];
            CheckPoint = r.CheckPoint[0];
            Spell = r.Spells[0];
            FreeGachaCycle = r.FreeGachaCycle[0];

            // ТЗ 5.1: Miners — ровно один элемент, и это tinygoblin (единственный тип котика v1).
            Require(r.Miners, "Miners");
            if (r.Miners[0] == null || r.Miners[0].Id != MinerId)
                throw new BalanceLoadException("Miners[0]: ожидалась запись " + MinerId + ", найдена " + (r.Miners[0]?.Id ?? "null"));
            Miner = r.Miners[0];

            AddAll(r.Rocks, x => x.Id, Rocks, "Rocks");
            AddAll(r.MineShafts, x => x.Id, Shafts, "MineShafts");
            AddAll(r.Obstructions, x => x.Id, Obstructions, "Obstructions");
            AddAll(r.GeneratorObjectives, x => x.GeneratorId, Objectives, "GeneratorObjectives");
            AddAll(r.Cards, x => x.Id, Cards, "Cards");
            AddAll(r.Gacha, x => x.Id, Gacha, "Gacha");
            AddAll(r.Deliveries, x => x.Id, Deliveries, "Deliveries");
            if (r.Cards != null) CardsInOrder.AddRange(r.Cards);
            if (r.CardUpgradeCosts != null)
                foreach (var c in r.CardUpgradeCosts) CardUpgradeCosts[c.Rarity] = c;
            if (r.MiningTargetHealths == null) throw new BalanceLoadException("Нет раздела MiningTargetHealths.");

            if (r.Zones == null || r.Zones.Length == 0) throw new BalanceLoadException("Нет зон.");
            m_Zones.AddRange(r.Zones);
            m_Zones.Sort((a, b) => ZoneNumber(a.Id).CompareTo(ZoneNumber(b.Id)));
        }

        static void Require<T>(T[] arr, string name)
        {
            if (arr == null || arr.Length != 1)
                throw new BalanceLoadException(name + ": ожидался массив ровно из одного элемента, найдено " + (arr == null ? "null" : arr.Length.ToString(CultureInfo.InvariantCulture)));
        }

        static void AddAll<T>(T[] items, Func<T, string> key, Dictionary<string, T> into, string section)
        {
            if (items == null) throw new BalanceLoadException("Нет раздела " + section);
            for (int i = 0; i < items.Length; i++)
            {
                string k = key(items[i]);
                if (k == null) throw new BalanceLoadException(section + "[" + i + "]: нет Id");
                if (into.ContainsKey(k)) throw new BalanceLoadException(section + "[" + i + "]: повтор Id " + k);
                into[k] = items[i];
            }
        }

        /// <summary>Номер зоны по ID "zoneN".</summary>
        public static int ZoneNumber(string zoneId)
        {
            if (zoneId != null && zoneId.StartsWith("zone", StringComparison.Ordinal) &&
                int.TryParse(zoneId.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n;
            throw new BalanceLoadException("Неверный ID зоны: " + zoneId);
        }

        /// <summary>Зона по номеру кампании 1..144.</summary>
        public ZoneDto Zone(int number)
        {
            if (number < 1 || number > m_Zones.Count) throw new ArgumentOutOfRangeException(nameof(number), number, "Нет такой зоны");
            return m_Zones[number - 1];
        }

        public int ZoneCount => m_Zones.Count;

        /// <summary>HP цели: table[level − 1] (ТЗ 8.1).</summary>
        public GameNumber TargetHp(string targetId, int level)
        {
            if (!Root.MiningTargetHealths.TryGetValue(targetId, out var table))
                throw new BalanceLoadException("MiningTargetHealths: нет таблицы для " + targetId);
            if (level < 1 || level > table.Length)
                throw new BalanceLoadException("MiningTargetHealths." + targetId + ": уровень " + level + " вне таблицы 1.." + table.Length);
            return GameNumber.FromDouble(table[level - 1]);
        }

        public bool HasHpTable(string id) => Root.MiningTargetHealths.ContainsKey(id);

        public RankMultipliersDto ZoneMultipliers(int zoneNumber) => Zone(zoneNumber).RankMultipliers[0];

        public int CardMaxLevel(int rarity) => CardUpgradeCosts.TryGetValue(rarity, out var c) ? c.Duplicates.Length + 1 : 1;

        public static BalanceRepository LoadFile(string path) => Load(File.ReadAllBytes(path));
    }

    public sealed class BalanceLoadException : Exception
    {
        public BalanceLoadException(string message) : base(message) { }
        public BalanceLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
