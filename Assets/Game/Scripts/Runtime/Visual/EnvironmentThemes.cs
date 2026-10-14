using System;
using UnityEngine;

namespace CatMiners.Runtime
{
    /// <summary>
    /// Оформление шахт по диапазонам зон — отдельная визуальная конфигурация Resources/Visual/environment_themes.json
    /// (Docs/Visual/ART_DIRECTION.md, «Окружение»). Только вид: карты, награды и баланс от оформления не зависят.
    /// Раскладка повторяется с шагом Cycle; зоны цикла без диапазона — тёплая штольня. Нет файла или он испорчен —
    /// по три зоны по кругу: штольня, пещера, выработка.
    /// </summary>
    public static class EnvironmentThemes
    {
        [Serializable]
        internal sealed class RangeDto
        {
            public int From, To;
            public string Theme;
        }

        [Serializable]
        internal sealed class ConfigDto
        {
            public int Cycle;
            public RangeDto[] Ranges;
        }

        const string ResourcePath = "Visual/environment_themes";
        const int MaxCycle = 1000;
        static readonly EnvironmentTheme[] s_Default =
        {
            EnvironmentTheme.Warm, EnvironmentTheme.Warm, EnvironmentTheme.Warm,
            EnvironmentTheme.Crystal, EnvironmentTheme.Crystal, EnvironmentTheme.Crystal,
            EnvironmentTheme.Copper, EnvironmentTheme.Copper, EnvironmentTheme.Copper,
        };

        static EnvironmentTheme[] s_Table;

        /// <summary>Оформление зоны (номер с 1).</summary>
        public static EnvironmentTheme Of(int zone)
        {
            var table = s_Table ??= Load();
            int n = table.Length;
            return table[((zone - 1) % n + n) % n];
        }

        static EnvironmentTheme[] Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[themes] нет Resources/{ResourcePath}.json — оформления по умолчанию");
                return s_Default;
            }
            var table = Parse(asset.text, out string error);
            if (table == null)
            {
                Debug.LogWarning($"[themes] {ResourcePath}.json: {error} — оформления по умолчанию");
                return s_Default;
            }
            return table;
        }

        /// <summary>Таблица оформлений по зонам цикла (индекс — зона − 1); null и текст ошибки (для журнала разработчика, по-английски) — конфигурация испорчена.</summary>
        public static EnvironmentTheme[] Parse(string json, out string error)
        {
            error = null;
            ConfigDto dto;
            try
            {
                dto = JsonUtility.FromJson<ConfigDto>(json);
            }
            catch (Exception e)
            {
                error = "not JSON: " + e.Message;
                return null;
            }
            if (dto == null || dto.Cycle < 1 || dto.Cycle > MaxCycle)
            {
                error = $"Cycle must be 1…{MaxCycle}";
                return null;
            }
            var table = new EnvironmentTheme[dto.Cycle];
            if (dto.Ranges == null) return table;
            foreach (var r in dto.Ranges)
            {
                if (r == null || r.From < 1 || r.To < r.From || r.To > dto.Cycle)
                {
                    error = $"range {r?.From}…{r?.To} is outside 1…{dto.Cycle}";
                    return null;
                }
                if (!Enum.TryParse(r.Theme, true, out EnvironmentTheme theme) || !Enum.IsDefined(typeof(EnvironmentTheme), theme))
                {
                    error = $"unknown theme '{r.Theme}'";
                    return null;
                }
                for (int z = r.From; z <= r.To; z++) table[z - 1] = theme;
            }
            return table;
        }
    }
}
