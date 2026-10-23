using System;
using System.IO;
using CatMiners.Core.Data;

namespace CatMiners.Tests
{
    /// <summary>
    /// Общие данные тестов. Работает и в Unity (текущая папка — корень проекта),
    /// и в dotnet test (поиск вверх от папки сборки).
    /// </summary>
    public static class TestData
    {
        public const string BalanceRelativePath = "Assets/Game/Data/Source/json-data.json";
        public const string ZonesSummaryRelativePath = "Docs/TZ/analysis/zones_summary.csv";

        static BalanceRepository s_Balance;
        static string s_Root;

        public static string ProjectRoot
        {
            get
            {
                if (s_Root != null) return s_Root;
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        if (File.Exists(Path.Combine(dir.FullName, BalanceRelativePath)))
                        {
                            s_Root = dir.FullName;
                            return s_Root;
                        }
                        dir = dir.Parent;
                    }
                }
                throw new FileNotFoundException("Не найден " + BalanceRelativePath);
            }
        }

        public static byte[] BalanceBytes => File.ReadAllBytes(Path.Combine(ProjectRoot, BalanceRelativePath));

        public static BalanceRepository Balance
        {
            get
            {
                if (s_Balance == null) s_Balance = BalanceRepository.Load(BalanceBytes);
                return s_Balance;
            }
        }

        /// <summary>CSV лежит в Docs/ основного проекта; в копии build/ci его ищем выше по дереву.</summary>
        public static string ZonesSummaryPath
        {
            get
            {
                var dir = new DirectoryInfo(ProjectRoot);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, ZonesSummaryRelativePath);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return Path.Combine(ProjectRoot, ZonesSummaryRelativePath);
            }
        }
    }
}
