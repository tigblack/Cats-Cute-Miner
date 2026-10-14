using System;
using System.Collections.Generic;
using System.Globalization;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Именованные проверяемые исключения адаптера по ID зоны (ТЗ 8.5). Статическая проверка 144/144 прошла без них;
    /// override добавляется только как осознанное изменение карты с именем и причиной и перечисляется в отчёте.
    /// </summary>
    [Serializable]
    public sealed class LevelOverrides
    {
        public string Version;
        /// <summary>zoneId → override.</summary>
        public Dictionary<string, ZoneOverride> Zones = new Dictionary<string, ZoneOverride>(StringComparer.Ordinal);

        public ZoneOverride For(string zoneId) =>
            zoneId != null && Zones != null && Zones.TryGetValue(zoneId, out var o) ? o : null;

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        public static LevelOverrides Parse(string json)
        {
            LevelOverrides cfg;
            try
            {
                cfg = OutpostJson.Deserialize<LevelOverrides>(json);
            }
            catch (Exception e)
            {
                throw new OutpostConfigException("level_overrides: ошибка разбора: " + e.Message, e);
            }
            if (cfg == null) throw new OutpostConfigException("level_overrides: пустой документ");
            if (string.IsNullOrEmpty(cfg.Version)) throw new OutpostConfigException("level_overrides: нет Version");
            if (cfg.Zones == null) cfg.Zones = new Dictionary<string, ZoneOverride>(StringComparer.Ordinal);
            foreach (var kv in cfg.Zones)
            {
                if (!IsZoneId(kv.Key)) throw new OutpostConfigException("level_overrides: неверный ID зоны \"" + kv.Key + "\"");
                if (kv.Value == null || string.IsNullOrEmpty(kv.Value.Name) || string.IsNullOrEmpty(kv.Value.Reason))
                    throw new OutpostConfigException("level_overrides: у override зоны " + kv.Key + " нет имени или причины");
                kv.Value.Validate(kv.Key);
            }
            return cfg;
        }

        static bool IsZoneId(string id) =>
            id != null && id.StartsWith("zone", StringComparison.Ordinal) &&
            int.TryParse(id.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= OutpostIds.FirstZone &&
            id == OutpostIds.ZoneId(n);
    }

    /// <summary>
    /// Override зоны: имя, причина и добавленные объекты. Override только добавляет цели на свободные клетки исходной карты:
    /// исходные токены, их footprint, выход e и строки ворот p не меняются (проверяет <see cref="SourceLevelImporter"/>).
    /// Run хранит копию override, с которой создан (<see cref="OutpostRun.MapOverride"/>), поэтому карта сохранённой шахты
    /// восстанавливается без текущего файла; смену override обнаруживает <see cref="StageProgressionService.IsRunMapCurrent"/>.
    /// </summary>
    [Serializable]
    public sealed class ZoneOverride
    {
        public string Name;
        public string Reason;
        /// <summary>Добавленные цели r/s/x: токен в формате Zones[].Grid на якоре (Column, Row) — нижней левой клетке footprint.</summary>
        public List<OverrideToken> AddTokens = new List<OverrideToken>();

        /// <summary>Есть добавленные объекты (метод, а не свойство: класс сериализуется в конфигурацию и run).</summary>
        public bool HasGeometry() => AddTokens != null && AddTokens.Count > 0;

        public string CanonicalJson() => OutpostJson.Serialize(this);
        public string ComputeHash() => OutpostJson.Sha256Hex(CanonicalJson());

        /// <summary>Совпадение override (null — исходная геометрия) по канонической сериализации.</summary>
        public static bool SameAs(ZoneOverride a, ZoneOverride b) =>
            a == null || b == null ? a == null && b == null : a.CanonicalJson() == b.CanonicalJson();

        /// <summary>Проверка без исходных таблиц: поля, вид токена, повтор якоря. Типы и footprint проверяет импорт.</summary>
        internal void Validate(string zoneId)
        {
            if (AddTokens == null) AddTokens = new List<OverrideToken>();
            var anchors = new HashSet<long>();
            foreach (var a in AddTokens)
            {
                string where = "level_overrides: " + zoneId + " (" + Name + ")";
                if (a == null || string.IsNullOrEmpty(a.Token)) throw new OutpostConfigException(where + ": пустой токен");
                if (a.Column < 0 || a.Row < 0) throw new OutpostConfigException(where + ": отрицательный якорь у \"" + a.Token + "\"");
                char kind = a.Token[0];
                if ((kind != 'r' && kind != 's' && kind != 'x') || a.Token.Length < 3 || a.Token[1] != ':')
                    throw new OutpostConfigException(where + ": допускаются только цели r/s/x, получено \"" + a.Token + "\"");
                if (!anchors.Add(((long)a.Row << 32) | (uint)a.Column))
                    throw new OutpostConfigException(where + ": повтор якоря (" + a.Column + ", " + a.Row + ")");
            }
        }
    }

    /// <summary>Добавленный объект override: якорь и исходный токен ("r:rockbasic:2", "x:block1x2").</summary>
    [Serializable]
    public sealed class OverrideToken
    {
        public int Column;
        public int Row;
        public string Token;
    }
}
