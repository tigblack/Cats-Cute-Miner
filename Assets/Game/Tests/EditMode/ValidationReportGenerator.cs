using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CatMiners.Core;
using CatMiners.Core.Data;
using CatMiners.Core.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CatMiners.Tests
{
    /// <summary>
    /// Собирает Docs/VALIDATION_REPORT.md (ТЗ 17.3, 19) из машинных отчётов Docs/Reports: NUnit XML Unity, validation.json,
    /// отчёт сценарного бота, perf.json, build-android.json; валидатор и таблицы C.4 считаются здесь же кодом игры.
    /// Сводку тестов вручную не пишут. Запуск: Tools/CoreTests/run.ps1 -Filter WriteValidationReport.
    /// </summary>
    public class ValidationReportGenerator
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class TestCase
        {
            public string Suite, FullName, Name, Result, Reason;
            public double Duration;
            /// <summary>[Explicit]-тест, пропущенный в обычном прогоне (генераторы отчётов): не доказательство критерия.</summary>
            public bool ExplicitSkipped;
        }

        sealed class Suite
        {
            public string Title, File;
            public bool Exists;
            public int Total, Passed, Failed, Skipped, Inconclusive;
            public string Start, End, Result;
            public readonly List<TestCase> Cases = new List<TestCase>();
        }

        static string Reports => Path.Combine(TestData.ProjectRoot, "Docs", "Reports");

        static Suite ReadNUnit(string title, string file)
        {
            var s = new Suite { Title = title, File = "Docs/Reports/" + file };
            string path = Path.Combine(Reports, file);
            if (!File.Exists(path)) return s;
            var doc = XDocument.Load(path);
            var run = doc.Root;
            if (run == null || run.Name.LocalName != "test-run") return s;
            s.Exists = true;
            int I(string a) => int.TryParse((string)run.Attribute(a), NumberStyles.Integer, Inv, out int v) ? v : 0;
            s.Total = I("total");
            s.Passed = I("passed");
            s.Failed = I("failed");
            s.Skipped = I("skipped");
            s.Inconclusive = I("inconclusive");
            s.Start = (string)run.Attribute("start-time");
            s.End = (string)run.Attribute("end-time");
            s.Result = (string)run.Attribute("result");
            foreach (var tc in doc.Descendants("test-case"))
            {
                s.Cases.Add(new TestCase
                {
                    Suite = title,
                    FullName = (string)tc.Attribute("fullname") ?? "",
                    Name = (string)tc.Attribute("name") ?? "",
                    Result = (string)tc.Attribute("result") ?? "",
                    Reason = tc.Element("reason")?.Element("message")?.Value?.Trim() ?? "",
                    Duration = double.TryParse((string)tc.Attribute("duration"), NumberStyles.Float, Inv, out double d) ? d : 0,
                    ExplicitSkipped = (string)tc.Attribute("runstate") == "Explicit" && (string)tc.Attribute("result") == "Skipped",
                });
            }
            return s;
        }

        static JObject ReadJson(string file)
        {
            string path = Path.Combine(Reports, file);
            if (!File.Exists(path)) return null;
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        static string UnityVersion()
        {
            string path = Path.Combine(TestData.ProjectRoot, "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(path)) return "?";
            foreach (var line in File.ReadAllLines(path))
                if (line.StartsWith("m_EditorVersion:", StringComparison.Ordinal)) return line.Substring("m_EditorVersion:".Length).Trim();
            return "?";
        }

        /// <summary>Критерий матрицы приёмки ТЗ 18 и тесты-доказательства (префиксы полных имён).</summary>
        sealed class Criterion
        {
            public string Name, Evidence, Who;
            public string[] Tests = new string[0];
            public Func<string> Extra;   // доказательство не из тестов: null — «готово», иначе причина
        }

        static string Status(Criterion c, List<TestCase> all, out string detail)
        {
            bool Match(TestCase t, string p) => t.FullName.StartsWith(p, StringComparison.Ordinal) || t.FullName.Contains("." + p);
            // [Explicit]-генераторы отчётов (например, EconomyReportTests.EconomyTablesReport) в обычном прогоне пропущены и не считаются.
            var hits = all.Where(t => !t.ExplicitSkipped && c.Tests.Any(p => Match(t, p))).ToList();
            int passed = hits.Count(t => t.Result == "Passed");
            int failed = hits.Count(t => t.Result == "Failed");
            // Доказательства, у которых нет ни одного результата (набор не запускался или тест не существует).
            var missing = c.Tests.Where(p => !all.Any(t => Match(t, p))).Select(p => p.Substring(p.LastIndexOf('.') + 1)).ToList();
            string extra = c.Extra?.Invoke();
            detail = c.Tests.Length > 0 ? passed + "/" + hits.Count + " тестов прошло" : "";
            if (missing.Count > 0) detail += "; нет результатов: " + string.Join(", ", missing);
            if (extra != null && extra.StartsWith("!", StringComparison.Ordinal))
            {
                detail = (detail.Length > 0 ? detail + "; " : "") + extra.Substring(1);
                return "не проверено";
            }
            bool partialExtra = extra != null && extra.StartsWith("~", StringComparison.Ordinal);
            if (partialExtra) extra = extra.Substring(1);
            if (extra != null) detail = (detail.Length > 0 ? detail + "; " : "") + extra;
            if (failed > 0) return "упал";
            if (partialExtra) return "частично";
            if (c.Tests.Length > 0 && hits.Count == 0) return "не проверено";
            if (missing.Count > 0 || passed < hits.Count) return "частично";
            return "готово";
        }

        [Test, Explicit("пишет Docs/VALIDATION_REPORT.md: dotnet test --filter WriteValidationReport")]
        public void WriteValidationReport()
        {
            var balance = TestData.Balance;
            var edit = ReadNUnit("EditMode (Unity)", "editmode-results.xml");
            var play = ReadNUnit("PlayMode (Unity)", "playmode-results.xml");
            var all = edit.Cases.Concat(play.Cases).ToList();
            var validationJson = ReadJson("validation.json");
            var perf = ReadJson("perf.json");
            var apk = ReadJson("build-android.json");
            var savePerf = ReadJson("save-perf.json");
            string botPath = Path.Combine(Reports, "bot-zones-1-10.md");
            bool botLog = File.Exists(Path.Combine(Reports, "bot-actions-zones-1-10.md"));
            string bot = File.Exists(botPath) ? File.ReadAllText(botPath) : null;
            string compileLog = Path.Combine(Reports, "unity-compile.log");
            string unity = UnityVersion();
            string sha = BalanceRepository.ComputeSha256(TestData.BalanceBytes);
            var validation = BalanceValidator.Validate(balance);

            var sb = new StringBuilder();
            sb.AppendLine("# VALIDATION_REPORT — проверка прототипа Cat Miners");
            sb.AppendLine();
            sb.AppendLine("> Файл собирается автоматически тестом `ValidationReportGenerator.WriteValidationReport` (`Tools/CoreTests/run.ps1 -Filter WriteValidationReport`) из машинных отчётов `Docs/Reports/`. Вручную не редактируется: после новых прогонов соберите заново.");
            sb.AppendLine();
            sb.AppendLine("| Параметр | Значение |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Отчёт собран (UTC) | " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", Inv) + " |");
            sb.AppendLine("| Unity | " + unity + " |");
            sb.AppendLine("| Баланс | `Assets/Game/Data/Source/json-data.json`, " + TestData.BalanceBytes.Length.ToString("N0", Inv).Replace(",", " ") + " байт, SHA-256 `" + sha + "`" +
                          (sha == BalanceRepository.ExpectedSha256 ? " (совпадает с эталоном)" : " (**не совпадает с эталоном**)") + " |");
            sb.AppendLine("| Профиль правил | `rulesVersion = " + Formula.RulesVersion.ToString(Inv) + "` (Приложение C) |");
            sb.AppendLine("| Проверенные платформы | Windows Editor (batchmode, изолированная копия `build/ci`); `dotnet test` (.NET 8) для ядра; Android — см. «Сборка Android» |");
            sb.AppendLine();

            // ───── Итог ─────
            var criteria = new List<Criterion>
            {
                new Criterion { Name = "Нет ошибок компиляции", Evidence = "`Docs/Reports/unity-compile.log`: нет строк `error CS`", Who = "ИИ",
                    Extra = () => !File.Exists(compileLog) ? "!нет лога компиляции" : (File.ReadAllText(compileLog).Contains("error CS") ? "!в логе есть error CS" : "лог без error CS") },
                new Criterion { Name = "Исходный JSON сохранён, hash совпадает (Editor и плеер)", Evidence = "Тест импорта, `validation.json`", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.BalanceLoadTests.HashSizeAndCounts", "CatMiners.Tests.BalanceLoadTests.WrongHashIsRejected" },
                    Extra = () => validationJson == null ? "!нет validation.json" :
                        ((string)validationJson["sha256"] == BalanceRepository.ExpectedSha256 ? "~validation.json: hash совпадает (Editor); в плеере не проверено — APK не собран, validation-player.json нет" : "!hash в validation.json не совпадает") },
                new Criterion { Name = "Все 144 планировки поддерживаются", Evidence = "EditMode-тесты групп 1–2, `validation.json`", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.LayoutTests", "CatMiners.Tests.BalanceLoadTests", "DataGeometryHpAcceptanceTests" },
                    Extra = () => validationJson == null ? "!нет validation.json" : "validation.json: зон " + (int)validationJson["zones"] + ", ошибок " + (int)validationJson["errors"] },
                new Criterion { Name = "Формулы соответствуют Приложению C", Evidence = "EditMode-тесты групп 4, 12", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.FormulaControlValueTests", "CatMiners.Tests.GameNumberTests", "FormulaAppendixCAcceptanceTests", "RewardChestAcceptanceTests", "CatMiners.Tests.EconomyReportTests" } },
                new Criterion { Name = "Объединение, транзакции, шахта, ворота, переход, офлайн, большие числа", Evidence = "EditMode-тесты групп 5–13", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.SimulationTests", "HireMergeAcceptanceTests", "TransactionAcceptanceTests", "ShaftGateAcceptanceTests", "TransitionAcceptanceTests",
                        "OfflineAcceptanceTests", "BigNumberAcceptanceTests", "DeliveryAcceptanceTests" } },
                new Criterion { Name = "Сцена запускается без исключений; мышь и касания (тап, перенос, объединение, прокрутка, жест на UI, отмена касания)",
                    Evidence = "PlayMode-тесты с `InputTestFixture`, smoke-тест `CoreGame` (10 с на ×20)", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.PointerInputTests", "CatMiners.Tests.CoreGameSmokeTests.CoreGameRunsAndPlays" } },
                new Criterion { Name = "Нет исключений в игровом цикле", Evidence = "PlayMode, слот Debug: zone5, zone10, zone144 по 60 с на ×100, ворота и `CompleteZone` через инструменты", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.CoreGameSmokeTests.DebugZonesRunWithoutExceptions" } },
                new Criterion { Name = "Кнопки покупки и улучшения реально меняют симуляцию", Evidence = "PlayMode-тест", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.CoreGameSmokeTests.HudButtonsChangeSimulation" } },
                new Criterion { Name = "Состояние переживает закрытие без дубликатов и потерь", Evidence = "Тесты 6, 10 и PlayMode save/load", Who = "ИИ",
                    Tests = new[] { "CatMiners.Tests.SimulationTests.SaveLoadRoundTripAndBackup", "CatMiners.Tests.SimulationTests.InterruptedSaveKeepsLastValidState",
                        "CatMiners.Tests.CoreGameSmokeTests.StateSurvivesSceneRestart", "CatMiners.Tests.CoreGameSmokeTests.FrameGapIsProcessedAsOfflineOnce" } },
                new Criterion { Name = "Сценарный бот прогнан по zone1–zone10", Evidence = "Раздел «Темп экономики» ниже (`Docs/Reports/bot-zones-1-10.md`), журнал действий `Docs/Reports/bot-actions-zones-1-10.md`", Who = "ИИ — прогон; владелец — решение по темпу",
                    Extra = () => bot == null ? "!нет отчёта бота" : (bot.Contains("стена на") || bot.Contains("предел на") ? "есть зоны со стеной или пределом" : "все зоны пройдены") },
                new Criterion { Name = "APK собран", Evidence = "`Docs/Reports/build-android.json`", Who = "ИИ",
                    Extra = () => ApkStatus(apk) },
                new Criterion { Name = "Касания на устройстве, safe area, сворачивание, визуал и звук, ручное прохождение zone1–3", Evidence = "Чек-лист README", Who = "Владелец",
                    Extra = () => "!ручная проверка владельца" },
                new Criterion { Name = "Будущий город не реализован и не влияет на экономику", Evidence = "Ревью кода: события `RockDestroyed`, `MineShaftActivated`, `CheckpointOpened`, `MineCompleted` используют только представление (эффекты, звук, вибрация, обучение), немедленное сохранение и диагностический обработчик `ZoneCompletionLog` (Editor/Development); экономику они не меняют, городских обработчиков, ресурсов, экрана и кнопки нет", Who = "ИИ",
                    Extra = () => "ревью кода" },
            };

            int editFailed = edit.Failed, playFailed = play.Failed;
            sb.AppendLine("## Итог");
            sb.AppendLine();
            sb.AppendLine("1. **Основные механики работают** — " + (edit.Exists && play.Exists && editFailed == 0 && playFailed == 0
                ? "в Unity " + unity + " упавших тестов нет: EditMode — прошло " + edit.Passed + " из " + edit.Total + " (пропущено " + edit.Skipped +
                  SkipNote(edit) + "), PlayMode — прошло " + play.Passed + " из " + play.Total + " (пропущено " + play.Skipped + SkipNote(play) + ")."
                : "есть упавшие или незапущенные наборы: EditMode " + edit.Passed + "/" + edit.Total + ", PlayMode " + play.Passed + "/" + play.Total + "."));
            var formula = all.Where(t => t.FullName.Contains("FormulaControlValueTests") || t.FullName.Contains("FormulaAppendixCAcceptanceTests")).ToList();
            sb.AppendLine("2. **Вычисления соответствуют рабочему профилю v1** — контрольные значения Приложения C: " + formula.Count(t => t.Result == "Passed") + "/" + formula.Count + " тестов прошло. Это соответствие нашему профилю, а не восстановленному алгоритму референса.");
            sb.AppendLine("3. **Темп экономики по сценарному боту** — " + (bot == null ? "отчёта нет." : BotSummary(bot)) + " Это оценка темпа рабочего профиля, решение по калибровке — за владельцем.");
            sb.AppendLine();

            sb.AppendLine("## Матрица приёмки (ТЗ 18)");
            sb.AppendLine();
            sb.AppendLine("| Критерий | Доказательство | Кто проверяет | Статус | Подробности |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in criteria)
            {
                string st = Status(c, all, out string detail);
                sb.AppendLine("| " + c.Name + " | " + c.Evidence + " | " + c.Who + " | " + st + " | " + detail + " |");
            }
            sb.AppendLine();

            // ───── Тесты ─────
            sb.AppendLine("## Сводка тестов (из NUnit XML)");
            sb.AppendLine();
            sb.AppendLine("| Набор | Источник | Всего | Прошло | Упало | Пропущено | Начало (UTC) | Unity | JSON |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---|---|---|");
            foreach (var s in new[] { edit, play })
            {
                if (!s.Exists) { sb.AppendLine("| " + s.Title + " | `" + s.File + "` | — | — | — | — | не запускался | " + unity + " | — |"); continue; }
                sb.AppendLine("| " + s.Title + " | `" + s.File + "` | " + s.Total + " | " + s.Passed + " | " + s.Failed + " | " + s.Skipped + " | " + s.Start + " | " + unity + " | `" + sha.Substring(0, 12) + "…` |");
            }
            sb.AppendLine();
            sb.AppendLine("Пропущенные тесты и причина пропуска (из XML):");
            sb.AppendLine();
            foreach (var t in edit.Cases.Concat(play.Cases).Where(t => t.Result == "Skipped").OrderBy(t => t.FullName, StringComparer.Ordinal))
                sb.AppendLine("- `" + t.FullName + "` — " + (t.Reason.Length > 0 ? "«" + t.Reason + "»" : "без причины в XML"));
            sb.AppendLine();
            foreach (var s in new[] { edit, play })
            {
                if (!s.Exists) continue;
                sb.AppendLine("<details><summary>" + s.Title + ": список тестов</summary>");
                sb.AppendLine();
                sb.AppendLine("| Тест | Результат | Время, с |");
                sb.AppendLine("|---|---|---:|");
                foreach (var t in s.Cases.OrderBy(t => t.FullName, StringComparer.Ordinal))
                    sb.AppendLine("| `" + t.FullName + "` | " + t.Result + " | " + t.Duration.ToString("0.###", Inv) + " |");
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
            }

            // ───── Валидатор ─────
            sb.AppendLine("## Валидатор баланса: все зоны");
            sb.AppendLine();
            sb.AppendLine("Зон: " + validation.Zones.Count + ", ошибок: " + validation.ErrorCount + ", предупреждений: " + (validation.Issues.Count - validation.ErrorCount) + "." +
                          (validationJson != null ? " Прогон в Unity (`Docs/Reports/validation.json`, " + (string)validationJson["checkedAtUtc"] + "): зон " + (int)validationJson["zones"] + ", ошибок " + (int)validationJson["errors"] + ", предупреждений " + (int)validationJson["warnings"] + "." : " Прогон в Unity (`validation.json`) отсутствует."));
            sb.AppendLine();
            if (validation.Issues.Count > 0)
            {
                foreach (var i in validation.Issues.Take(50)) sb.AppendLine("- " + (i.IsWarning ? "предупреждение" : "**ошибка**") + " `" + i.Path + "`: " + i.Message);
                sb.AppendLine();
            }
            sb.AppendLine("<details><summary>Сводка по зонам</summary>");
            sb.AppendLine();
            sb.AppendLine("| Зона | Размер | Камни | Шахты | Ворота | Препятствия | Клеток r/s/x | Секторы | Выход | Шахты (id:ур.:карта:ур.) |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|---|");
            foreach (var z in validation.Zones)
                sb.AppendLine("| " + z.ZoneId + " | " + z.Width + "×" + z.Depth + " | " + z.Rocks + " | " + z.Shafts + " | " + z.Gates + " | " + z.Obstructions + " | " + z.OccupiedRsx + " | " + z.SectorCount +
                              " | ур. " + z.ExitLevel + ", " + z.ExitGacha + " | " + string.Join(", ", z.ShaftSpecs ?? new string[0]) + " |");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();

            // ───── Бот ─────
            sb.AppendLine("## Темп экономики: сценарный бот, zone1–zone10 (ТЗ 17.2, C.4)");
            sb.AppendLine();
            sb.AppendLine("Бот играет в слоте Main без инструментов теми же командами, что UI (`ScenarioBot`): покупает, назначает свободных котиков на ближайшие цели, объединяет пары, улучшает самый выгодный генератор (наименьшая окупаемость), активирует шахты, открывает ворота, сундуки и доставки, улучшает чертежи для автоматизации шахт, затем доходные. Сессии — непрерывный цикл через все зоны: 30 мин игры, затем 4 ч офлайна; предел 48 ч симуляции на зону; «стена» — 2 ч симуляции без доступной покупки, улучшения или назначения, которое увеличивает доход или урон.");
            sb.AppendLine();
            sb.AppendLine(botLog ? "Журнал действий и симуляционного времени по зонам: `Docs/Reports/bot-actions-zones-1-10.md`." : "Журнал действий отсутствует: запустите `Tools/CoreTests/run.ps1 -Filter BotZones1To10Report`.");
            sb.AppendLine();
            sb.AppendLine("Ориентиры владельца (C.4): zone1 — 2–6 мин живой игры; zone1–3 — до 20–30 мин; первые ворота zone5 — примерно за час; в zone1–10 нет «стены». Для сравнения, предварительная оценка ТЗ по упрощённому боту (без карт, бесплатных котиков, доставок и динамита): zone1 ≈ 77 с, zone3 ≈ 5 мин, первые ворота zone5 ≈ 43 мин, zone7–8 не проходились за 5,5 ч. Сравнение с ориентирами и предложение по профилю — `Docs/IMPLEMENTATION_NOTES.md`, раздел 5.");
            sb.AppendLine();
            sb.AppendLine(bot ?? "Отчёт бота отсутствует: запустите `Tools/CoreTests/run.ps1 -Filter BotZones1To10Report`.");
            sb.AppendLine();
            sb.AppendLine("Обучение, шаг 8 (ТЗ 13.4): в слоте Main без инструментов автоматизация в zone3 становится доступной (см. строки zone3 в таблице «Автоматизация шахт»).");
            sb.AppendLine();

            // ───── C.4 ─────
            sb.AppendLine("## Таблицы темпа (Приложение C.4)");
            sb.AppendLine();
            sb.Append(Demote(EconomyTables.Build(balance)));
            sb.AppendLine();

            // ───── Производительность ─────
            sb.AppendLine("## Производительность (ТЗ 15.3)");
            sb.AppendLine();
            if (perf == null) sb.AppendLine("Замер не выполнялся (`Docs/Reports/perf.json` отсутствует).");
            else
            {
                sb.AppendLine("Замер — PlayMode-тест `PerformanceTests.EarlyAndLateZoneFrameTimes` (ранняя zone5 ×1, поздние zone93 и zone117 ×1 и ×100) в Editor (batchmode), рендер в `RenderTexture` 1080×1920 вместе с HUD, без ограничения частоты кадров, прокрутка всей карты за время замера. На устройстве не измерялось: " +
                    (apk != null && (string)apk["result"] == "Succeeded" ? "APK собран, но на устройстве не запускался." : "APK не собран (`build-android.json`: " + (apk != null ? (string)apk["result"] : "нет отчёта") + ")."));
                sb.AppendLine();
                sb.AppendLine("| Параметр | Значение |");
                sb.AppendLine("|---|---|");
                foreach (var key in new[] { "unityVersion", "platform", "deviceModel", "cpu", "gpu", "ramMb", "quality", "render", "balanceLoadMs", "simAllocBytesPerFrameX1", "simAllocBytesPerFrameX100" })
                    sb.AppendLine("| " + key + " | " + Cell(perf[key]) + " |");
                sb.AppendLine();
                sb.AppendLine("| Зона | Скорость | Объектов | Котиков (работают) | Построение зоны, мс | Медиана кадра, мс | p95, мс | Макс., мс | Средний FPS | GC за кадр, байт (сред./макс.) |");
                sb.AppendLine("|---|---:|---:|---|---:|---:|---:|---:|---:|---|");
                foreach (var r in perf["runs"] ?? new JArray())
                    sb.AppendLine("| zone" + r["zone"] + " | ×" + r["speed"] + " | " + r["objects"] + " | " + r["cats"] + " (" + r["workingCats"] + ") | " + r["zoneBuildMs"] + " | " + r["medianMs"] + " | " + r["p95Ms"] + " | " + r["maxMs"] + " | " + r["avgFps"] + " | " + r["gcBytesPerFrameAvg"] + " / " + r["gcBytesPerFrameMax"] + " |");
                sb.AppendLine();
                sb.AppendLine("GC в этой таблице — вторичное чтение: ProfilerRecorder «GC Allocated In Frame» во время прокрутки, ×1 и ×100.");
                sb.AppendLine();
                AppendGcAcceptance(sb, perf);
                sb.AppendLine();
                sb.AppendLine("Цели ТЗ: Android p95 ≤ 33 мс; Editor с Quality = Mobile — ориентир 60 FPS; в установившемся режиме ×1 без панелей, 600 кадров — 0 байт на кадр в симуляции и не больше 1 КБ на кадр всего (`GC.GetAllocatedBytesForCurrentThread`). В Editor главный поток включает и выделения самого редактора.");
            }
            sb.AppendLine();
            if (savePerf != null)
            {
                sb.AppendLine("Сохранение (`Docs/Reports/save-perf.json`" + (savePerf["environment"] != null ? ", " + savePerf["environment"] : "") + (savePerf["zone"] != null ? ", zone" + savePerf["zone"] : "") + "):");
                sb.AppendLine();
                sb.AppendLine("| Параметр | Значение |");
                sb.AppendLine("|---|---|");
                sb.AppendLine("| Сериализация, медиана, мс | " + (savePerf["serializeMedianMs"] ?? "—") + " |");
                sb.AppendLine("| Запись на диск, медиана, мс | " + (savePerf["diskMedianMs"] ?? "—") + " |");
                sb.AppendLine("| Запись на диск, p95, мс | " + (savePerf["diskP95Ms"] ?? "—") + " |");
                sb.AppendLine();
            }

            // ───── Android ─────
            sb.AppendLine("## Сборка Android");
            sb.AppendLine();
            if (apk == null) sb.AppendLine("Сборка не запускалась (`Docs/Reports/build-android.json` отсутствует).");
            else
            {
                AppendApk(sb, apk);
                sb.AppendLine();
                sb.AppendLine("APK — internal only (ТЗ 4.5): не публикуется.");
            }
            sb.AppendLine();

            sb.AppendLine("## Открытые вопросы");
            sb.AppendLine();
            sb.AppendLine("Решения по умолчанию и альтернативы — ТЗ, Приложение D (D-1…D-11); статус каждого правила — `Docs/BALANCE_MAPPING.md`; отличия и предложения — `Docs/IMPLEMENTATION_NOTES.md`.");

            string outPath = Path.Combine(TestData.ProjectRoot, "Docs", "VALIDATION_REPORT.md");
            File.WriteAllText(outPath, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
            TestContext.WriteLine("written " + outPath);
        }

        static string Cell(JToken v) => v == null || v.Type == JTokenType.Null ? "—" : v.ToString().Replace("\r", " ").Replace("\n", " ").Replace("|", "/");

        static string Pass(JToken v) => v == null || v.Type == JTokenType.Null ? "—" : ((bool)v ? "да" : "**нет**");

        /// <summary>
        /// Приёмочное чтение выделений ТЗ 15.3 из perf.json: установившийся режим ×1 без панелей (steadyRuns), всего за кадр
        /// со всеми кадрами и без кадров автосохранения, симуляция отдельно. Старый perf.json без этих полей — пометка.
        /// Чтение GC.GetAllocatedBytesForCurrentThread годно только после пробы (allocCounterValid): проба не прошла или
        /// отметка null — «не измерено», пробы нет — «не проверено». Нули неработающего счётчика не дают «да».
        /// Рядом — независимая проверка: ProfilerRecorder «GC Allocated In Frame» и вызовы GC.Alloc в симуляции.
        /// </summary>
        static void AppendGcAcceptance(StringBuilder sb, JObject perf)
        {
            var steady = perf["steadyRuns"] as JArray;
            if (steady == null || perf["totalTargetBytesPerFrame"] == null)
            {
                sb.AppendLine("Выделения по ТЗ 15.3 (`GC.GetAllocatedBytesForCurrentThread`, установившийся режим ×1 без панелей): в этом `perf.json` замера нет — перезапустите `PerformanceTests`. " +
                              "Симуляция: " + Cell(perf["simAllocBytesPerFrameX1"]) + " байт на кадр ×1 (счётчик не проверен пробой: ноль может означать неработающий счётчик).");
                return;
            }
            bool? counter = Probe(perf["allocCounterValid"]);
            bool? sampler = Probe(perf["gcAllocSamplerValid"]);
            bool? recorder = Probe(perf["recorderValid"]);
            sb.AppendLine("Выделения по ТЗ 15.3 — приёмочное чтение: " + Cell(perf["acceptanceReading"]) + ".");
            sb.AppendLine();
            if (counter == null)
            {
                sb.AppendLine("**Счётчик `GC.GetAllocatedBytesForCurrentThread` в этом `perf.json` не проверен пробой**: нули могут означать неработающий счётчик, поэтому отметки ниже — «не проверено». Перезапустите `PerformanceTests`.");
                sb.AppendLine();
            }
            else if (counter == false)
            {
                sb.AppendLine("**Счётчик `GC.GetAllocatedBytesForCurrentThread` в этом рантайме не работает**: проба " + Cell(perf["allocProbeBytes"]) + " байт дала разницу " +
                              Cell(perf["allocCounterProbeBytes"]) + " байт. Приёмка ТЗ 15.3 по нему **не измерена**; ниже — независимая проверка со своей пробой.");
                sb.AppendLine();
            }
            sb.AppendLine("| Показатель | Значение | Цель | Выполнено |");
            sb.AppendLine("|---|---:|---:|---|");
            sb.AppendLine("| Симуляция ×1, байт на кадр | " + Measured(perf["simAllocBytesPerFrameX1"], counter) + " | " + Cell(perf["simTargetBytesPerFrame"]) + " | " + Verdict(perf["simAllocPass"], counter) + " |");
            sb.AppendLine("| Всего, байт на кадр, среднее (все кадры) — приёмка | " + Measured(perf["totalAllocBytesPerFrameAvg"], counter) + " | " + Cell(perf["totalTargetBytesPerFrame"]) + " | " + Verdict(perf["totalAllocPass"], counter) + " |");
            sb.AppendLine("| Всего, байт на кадр, среднее без кадров автосохранения | " + Measured(perf["totalAllocBytesPerFrameAvgWithoutAutosave"], counter) + " | " + Cell(perf["totalTargetBytesPerFrame"]) + " | " + Verdict(perf["totalAllocPassWithoutAutosave"], counter) + " |");
            if (perf["simAllocSamplesX1"] != null)
                sb.AppendLine("| Проверка: симуляция ×1, вызовов GC.Alloc за " + Cell(perf["simAllocSteps"]) + " шагов (×100: " + Measured(perf["simAllocSamplesX100"], sampler) + ") | " +
                              Measured(perf["simAllocSamplesX1"], sampler) + " | 0 | " + Verdict(perf["simAllocPassBySamples"], sampler) + " |");
            if (perf["recorderAllocBytesPerFrameAvg"] != null)
            {
                sb.AppendLine("| Проверка: ProfilerRecorder «GC Allocated In Frame», байт на кадр, среднее (все кадры) | " + Measured(perf["recorderAllocBytesPerFrameAvg"], recorder) + " | " + Cell(perf["totalTargetBytesPerFrame"]) + " | " + Verdict(perf["recorderAllocPass"], recorder) + " |");
                sb.AppendLine("| Проверка: ProfilerRecorder, байт на кадр, среднее без кадров автосохранения | " + Measured(perf["recorderAllocBytesPerFrameAvgWithoutAutosave"], recorder) + " | " + Cell(perf["totalTargetBytesPerFrame"]) + " | " + Verdict(perf["recorderAllocPassWithoutAutosave"], recorder) + " |");
            }
            sb.AppendLine();
            sb.AppendLine("| Зона | Кадров | FPS | Кадров с автосохранением | Кадров с панелью | Всего: среднее / медиана / макс., байт | Без автосохранения: среднее / медиана / макс., байт | ≤ 1 КБ (все / без автосохр.) |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|---|---|");
            foreach (var r in steady)
            {
                // Своя проба прогона; в perf.json без неё — общая отметка.
                bool? run = Probe(r["allocCounterValid"]) ?? counter;
                sb.AppendLine("| zone" + Cell(r["zone"]) + " | " + Cell(r["frames"]) + " | " + Cell(r["avgFps"]) + " | " + Cell(r["autosaveFrames"]) + " | " + Cell(r["panelFrames"]) + " | " +
                              Measured(r["totalAllocBytesPerFrameAvg"], run) + " / " + Measured(r["totalAllocBytesPerFrameMedian"], run) + " / " + Measured(r["totalAllocBytesPerFrameMax"], run) + " | " +
                              Measured(r["totalAllocBytesPerFrameAvgWithoutAutosave"], run) + " / " + Measured(r["totalAllocBytesPerFrameMedianWithoutAutosave"], run) + " / " + Measured(r["totalAllocBytesPerFrameMaxWithoutAutosave"], run) + " | " +
                              Verdict(r["totalAllocPass"], run) + " / " + Verdict(r["totalAllocPassWithoutAutosave"], run) + " |");
            }
            if (!steady.Any(r => r["recorderValid"] != null)) return;
            sb.AppendLine();
            sb.AppendLine("Проверка: " + Cell(perf["crossCheckReading"]) + ".");
            sb.AppendLine();
            sb.AppendLine("| Зона | Проба счётчика, байт (из " + Cell(perf["allocProbeBytes"]) + ") | Проба ProfilerRecorder, байт | Кадров | Кадров автосохранения (≥ 50 КБ) | ProfilerRecorder: среднее / медиана / макс., байт | Без автосохранения: среднее / медиана / макс., байт | ≤ 1 КБ (все / без автосохр.) |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|---|---|");
            foreach (var r in steady)
            {
                bool? run = Probe(r["recorderValid"]) ?? recorder;
                sb.AppendLine("| zone" + Cell(r["zone"]) + " | " + Cell(r["allocCounterProbeBytes"]) + " | " + Cell(r["recorderProbeBytes"]) + " | " + Cell(r["recorderFrames"]) + " | " + Cell(r["recorderAutosaveFrames"]) + " | " +
                              Measured(r["recorderAllocBytesPerFrameAvg"], run) + " / " + Measured(r["recorderAllocBytesPerFrameMedian"], run) + " / " + Measured(r["recorderAllocBytesPerFrameMax"], run) + " | " +
                              Measured(r["recorderAllocBytesPerFrameAvgWithoutAutosave"], run) + " / " + Measured(r["recorderAllocBytesPerFrameMedianWithoutAutosave"], run) + " / " + Measured(r["recorderAllocBytesPerFrameMaxWithoutAutosave"], run) + " | " +
                              Verdict(r["recorderAllocPass"], run) + " / " + Verdict(r["recorderAllocPassWithoutAutosave"], run) + " |");
            }
        }

        /// <summary>Проба инструмента из perf.json: true — годен, false — не видит выделений, null — пробы нет.</summary>
        static bool? Probe(JToken valid) => valid != null && valid.Type == JTokenType.Boolean ? (bool)valid : (bool?)null;

        /// <summary>Значение чтения: проба не прошла или null — «не измерено»; пробы нет — значение с пометкой «(?)».</summary>
        static string Measured(JToken v, bool? probe)
        {
            if (probe == false || v == null || v.Type == JTokenType.Null) return "не измерено";
            return probe == true ? Cell(v) : Cell(v) + " (?)";
        }

        /// <summary>Отметка чтения: пробы нет — «не проверено»; проба не прошла или null — «не измерено»; иначе да/нет.</summary>
        static string Verdict(JToken pass, bool? probe)
        {
            if (probe == null) return "не проверено";
            if (probe == false || pass == null || pass.Type == JTokenType.Null) return "**не измерено**";
            return Pass(pass);
        }

        /// <summary>
        /// Сборки APK из build-android.json: новый формат {"builds": [{variant, result, path, sizeBytes, sha256, …}]},
        /// отказ до сборки {"result": "ModuleMissing", "missing": […]} или старый одиночный объект.
        /// </summary>
        static List<JObject> ApkBuilds(JObject apk)
        {
            var list = new List<JObject>();
            if (apk == null) return list;
            if (apk["builds"] is JArray builds)
            {
                foreach (var b in builds) if (b is JObject o) list.Add(o);
            }
            else if (apk["result"] != null) list.Add(apk);
            return list;
        }

        static string ApkStatus(JObject apk)
        {
            if (apk == null) return "!сборка не запускалась";
            if (apk["missing"] is JArray missingTop && apk["builds"] == null)
                return "!" + Cell(apk["result"]) + ": нет модулей " + string.Join(", ", missingTop.Select(m => m.ToString()));
            var builds = ApkBuilds(apk);
            if (builds.Count == 0) return "!" + (apk["result"] != null ? Cell(apk["result"]) : "в build-android.json нет сборок");
            var parts = builds.Select(b => (b["variant"] != null ? Cell(b["variant"]) + ": " : "") + Cell(b["result"])).ToList();
            bool ok = builds.All(b => (string)b["result"] == "Succeeded");
            return (ok ? "" : "!") + string.Join("; ", parts) + (ok ? ", на устройстве не проверен" : "");
        }

        static void AppendApk(StringBuilder sb, JObject apk)
        {
            sb.AppendLine("Итог: " + ApkStatus(apk).TrimStart('!') + ".");
            sb.AppendLine();
            if (apk["missing"] is JArray missing)
            {
                sb.AppendLine("Недостающие модули: " + string.Join(", ", missing.Select(m => "`" + m + "`")) + ".");
                sb.AppendLine();
            }
            var builds = ApkBuilds(apk);
            if (apk["builds"] is JArray)
            {
                sb.AppendLine("| Вариант | Результат | APK | Размер, байт | SHA-256 | Прочее |");
                sb.AppendLine("|---|---|---|---:|---|---|");
                var known = new HashSet<string> { "variant", "result", "path", "sizeBytes", "sha256" };
                foreach (var b in builds)
                {
                    var rest = b.Properties().Where(p => !known.Contains(p.Name)).Select(p => p.Name + " = " + Cell(p.Value));
                    sb.AppendLine("| " + Cell(b["variant"]) + " | " + Cell(b["result"]) + " | " + Cell(b["path"]) + " | " + Cell(b["sizeBytes"]) + " | " +
                                  Cell(b["sha256"]) + " | " + string.Join("; ", rest) + " |");
                }
                var other = apk.Properties().Where(p => p.Name != "builds" && p.Name != "missing").ToList();
                if (other.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var p in other) sb.AppendLine("- " + p.Name + ": " + Cell(p.Value));
                }
                return;
            }
            sb.AppendLine("| Поле | Значение |");
            sb.AppendLine("|---|---|");
            foreach (var p in apk.Properties()) sb.AppendLine("| " + p.Name + " | " + Cell(p.Value) + " |");
        }

        /// <summary>build-android.json читается в старом формате (один объект), новом ({"builds": […]}) и при отсутствии модулей.</summary>
        [Test]
        public void ApkReportReadsLegacyNewAndModuleMissingFormats()
        {
            Assert.AreEqual("!сборка не запускалась", ApkStatus(null));
            Assert.AreEqual("Succeeded, на устройстве не проверен", ApkStatus(JObject.Parse("{\"result\": \"Succeeded\", \"path\": \"a.apk\"}")));
            Assert.AreEqual("!Failed", ApkStatus(JObject.Parse("{\"result\": \"Failed\"}")));
            var two = JObject.Parse("{\"builds\": [{\"variant\": \"dev\", \"result\": \"Succeeded\", \"path\": \"d.apk\", \"sizeBytes\": 10, \"sha256\": \"ab\"}, " +
                                    "{\"variant\": \"rel\", \"result\": \"Succeeded\", \"path\": \"r.apk\", \"sizeBytes\": 9, \"sha256\": \"cd\", \"devTools\": false}]}");
            Assert.AreEqual("dev: Succeeded; rel: Succeeded, на устройстве не проверен", ApkStatus(two));
            var sb = new StringBuilder();
            AppendApk(sb, two);
            StringAssert.Contains("| dev | Succeeded | d.apk | 10 | ab |  |", sb.ToString());
            StringAssert.Contains("| rel | Succeeded | r.apk | 9 | cd | devTools = False |", sb.ToString());
            var failed = JObject.Parse("{\"builds\": [{\"variant\": \"dev\", \"result\": \"Succeeded\"}, {\"variant\": \"rel\", \"result\": \"Failed\"}]}");
            Assert.AreEqual("!dev: Succeeded; rel: Failed", ApkStatus(failed));
            var missing = JObject.Parse("{\"result\": \"ModuleMissing\", \"missing\": [\"AndroidPlayer\", \"OpenJDK\"]}");
            Assert.AreEqual("!ModuleMissing: нет модулей AndroidPlayer, OpenJDK", ApkStatus(missing));
            sb.Clear();
            AppendApk(sb, missing);
            StringAssert.Contains("Недостающие модули: `AndroidPlayer`, `OpenJDK`.", sb.ToString());
        }

        /// <summary>perf.json: приёмочное чтение выделений ТЗ 15.3 показывается с целями и отметками; старый perf.json — пометка.</summary>
        [Test]
        public void PerfReportShowsAcceptanceGcReading()
        {
            var sb = new StringBuilder();
            AppendGcAcceptance(sb, JObject.Parse("{\"simAllocBytesPerFrameX1\": 0, \"runs\": []}"));
            StringAssert.Contains("замера нет", sb.ToString());
            sb.Clear();
            AppendGcAcceptance(sb, JObject.Parse("{\"allocCounterValid\": true, \"simAllocBytesPerFrameX1\": 0, \"simTargetBytesPerFrame\": 0, \"simAllocPass\": true, \"totalTargetBytesPerFrame\": 1024, " +
                                                 "\"totalAllocBytesPerFrameAvg\": 1500, \"totalAllocPass\": false, \"totalAllocBytesPerFrameAvgWithoutAutosave\": 600, " +
                                                 "\"totalAllocPassWithoutAutosave\": true, \"acceptanceReading\": \"все кадры\", \"steadyRuns\": [{\"zone\": 93, \"frames\": 600, " +
                                                 "\"avgFps\": 60, \"autosaveFrames\": 5, \"panelFrames\": 0, \"totalAllocBytesPerFrameAvg\": 1500, \"totalAllocBytesPerFrameMedian\": 500, " +
                                                 "\"totalAllocBytesPerFrameMax\": 40000, \"totalAllocBytesPerFrameAvgWithoutAutosave\": 600, \"totalAllocBytesPerFrameMedianWithoutAutosave\": 500, " +
                                                 "\"totalAllocBytesPerFrameMaxWithoutAutosave\": 900, \"totalAllocPass\": false, \"totalAllocPassWithoutAutosave\": true}]}"));
            string text = sb.ToString();
            StringAssert.Contains("приёмочное чтение: все кадры.", text);
            StringAssert.Contains("| Симуляция ×1, байт на кадр | 0 | 0 | да |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее (все кадры) — приёмка | 1500 | 1024 | **нет** |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее без кадров автосохранения | 600 | 1024 | да |", text);
            StringAssert.Contains("| zone93 | 600 | 60 | 5 | 0 | 1500 / 500 / 40000 | 600 / 500 / 900 | **нет** / да |", text);
        }

        /// <summary>
        /// Счётчик GC.GetAllocatedBytesForCurrentThread не прошёл пробу (заглушка, всегда 0): приёмка ТЗ 15.3 — «не измерено»,
        /// а не «да», даже если в perf.json остались нули и отметки true. Проверка ProfilerRecorder и GC.Alloc показывается рядом.
        /// </summary>
        [Test]
        public void PerfReportMarksStubAllocCounterAsNotMeasured()
        {
            const string run = "{\"zone\": 93, \"frames\": 600, \"avgFps\": 60, \"autosaveFrames\": 5, \"panelFrames\": 0, \"allocCounterProbeBytes\": 0, \"allocCounterValid\": false, " +
                               "\"totalAllocBytesPerFrameAvg\": null, \"totalAllocBytesPerFrameMedian\": null, \"totalAllocBytesPerFrameMax\": null, " +
                               "\"totalAllocBytesPerFrameAvgWithoutAutosave\": null, \"totalAllocBytesPerFrameMedianWithoutAutosave\": null, \"totalAllocBytesPerFrameMaxWithoutAutosave\": null, " +
                               "\"totalAllocPass\": null, \"totalAllocPassWithoutAutosave\": null, \"recorderProbeBytes\": 4610, \"recorderValid\": true, \"recorderFrames\": 600, " +
                               "\"recorderAutosaveFrames\": 5, \"recorderAllocBytesPerFrameAvg\": 1014, \"recorderAllocBytesPerFrameMedian\": 498, \"recorderAllocBytesPerFrameMax\": 231687, " +
                               "\"recorderAllocBytesPerFrameAvgWithoutAutosave\": 629, \"recorderAllocBytesPerFrameMedianWithoutAutosave\": 498, \"recorderAllocBytesPerFrameMaxWithoutAutosave\": 900, " +
                               "\"recorderAllocPass\": true, \"recorderAllocPassWithoutAutosave\": true}";
            var sb = new StringBuilder();
            AppendGcAcceptance(sb, JObject.Parse("{\"allocProbeBytes\": 4096, \"allocCounterProbeBytes\": 0, \"allocCounterValid\": false, \"gcAllocSamplerValid\": true, " +
                                                 "\"recorderValid\": true, \"simAllocBytesPerFrameX1\": null, \"simAllocSteps\": 600, \"simAllocSamplesX1\": 0, \"simAllocSamplesX100\": 3, " +
                                                 "\"simTargetBytesPerFrame\": 0, \"totalTargetBytesPerFrame\": 1024, \"simAllocPass\": null, \"simAllocPassBySamples\": true, " +
                                                 "\"totalAllocBytesPerFrameAvg\": null, \"totalAllocBytesPerFrameAvgWithoutAutosave\": null, \"totalAllocPass\": null, \"totalAllocPassWithoutAutosave\": null, " +
                                                 "\"recorderAllocBytesPerFrameAvg\": 1014, \"recorderAllocBytesPerFrameAvgWithoutAutosave\": 629, \"recorderAllocPass\": true, " +
                                                 "\"recorderAllocPassWithoutAutosave\": true, \"acceptanceReading\": \"не измерено\", \"crossCheckReading\": \"рекордер\", \"steadyRuns\": [" + run + "]}"));
            string text = sb.ToString();
            StringAssert.Contains("в этом рантайме не работает**: проба 4096 байт дала разницу 0 байт", text);
            StringAssert.Contains("| Симуляция ×1, байт на кадр | не измерено | 0 | **не измерено** |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее (все кадры) — приёмка | не измерено | 1024 | **не измерено** |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее без кадров автосохранения | не измерено | 1024 | **не измерено** |", text);
            StringAssert.Contains("| Проверка: симуляция ×1, вызовов GC.Alloc за 600 шагов (×100: 3) | 0 | 0 | да |", text);
            StringAssert.Contains("| Проверка: ProfilerRecorder «GC Allocated In Frame», байт на кадр, среднее (все кадры) | 1014 | 1024 | да |", text);
            StringAssert.Contains("| zone93 | 600 | 60 | 5 | 0 | не измерено / не измерено / не измерено | не измерено / не измерено / не измерено | **не измерено** / **не измерено** |", text);
            StringAssert.Contains("| zone93 | 0 | 4610 | 600 | 5 | 1014 / 498 / 231687 | 629 / 498 / 900 | да / да |", text);

            // Нули и true от счётчика, не прошедшего пробу, тоже не дают «да».
            sb.Clear();
            AppendGcAcceptance(sb, JObject.Parse("{\"allocCounterValid\": false, \"simAllocBytesPerFrameX1\": 0, \"simTargetBytesPerFrame\": 0, \"simAllocPass\": true, " +
                                                 "\"totalTargetBytesPerFrame\": 1024, \"totalAllocBytesPerFrameAvg\": 0, \"totalAllocPass\": true, \"totalAllocBytesPerFrameAvgWithoutAutosave\": 0, " +
                                                 "\"totalAllocPassWithoutAutosave\": true, \"steadyRuns\": [{\"zone\": 117, \"frames\": 600, \"totalAllocBytesPerFrameAvg\": 0, " +
                                                 "\"totalAllocBytesPerFrameMax\": 0, \"totalAllocPass\": true, \"totalAllocPassWithoutAutosave\": true}]}"));
            text = sb.ToString();
            StringAssert.Contains("| Симуляция ×1, байт на кадр | не измерено | 0 | **не измерено** |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее (все кадры) — приёмка | не измерено | 1024 | **не измерено** |", text);
            StringAssert.Contains("**не измерено** / **не измерено** |", text);
            StringAssert.DoesNotContain("| да |", text);
            StringAssert.DoesNotContain("да /", text);
        }

        /// <summary>perf.json со steadyRuns, но без пробы счётчика: нули не принимаются как «да» — отметка «не проверено».</summary>
        [Test]
        public void PerfReportWithoutCounterProbeIsNotVerified()
        {
            var sb = new StringBuilder();
            AppendGcAcceptance(sb, JObject.Parse("{\"simAllocBytesPerFrameX1\": 0, \"simTargetBytesPerFrame\": 0, \"simAllocPass\": true, \"totalTargetBytesPerFrame\": 1024, " +
                                                 "\"totalAllocBytesPerFrameAvg\": 0, \"totalAllocPass\": true, \"totalAllocBytesPerFrameAvgWithoutAutosave\": 0, " +
                                                 "\"totalAllocPassWithoutAutosave\": true, \"steadyRuns\": [{\"zone\": 93, \"frames\": 600, \"avgFps\": 60, \"autosaveFrames\": 5, " +
                                                 "\"panelFrames\": 0, \"totalAllocBytesPerFrameAvg\": 0, \"totalAllocBytesPerFrameMedian\": 0, \"totalAllocBytesPerFrameMax\": 0, " +
                                                 "\"totalAllocBytesPerFrameAvgWithoutAutosave\": 0, \"totalAllocBytesPerFrameMedianWithoutAutosave\": 0, \"totalAllocBytesPerFrameMaxWithoutAutosave\": 0, " +
                                                 "\"totalAllocPass\": true, \"totalAllocPassWithoutAutosave\": true}]}"));
            string text = sb.ToString();
            StringAssert.Contains("не проверен пробой", text);
            StringAssert.Contains("| Симуляция ×1, байт на кадр | 0 (?) | 0 | не проверено |", text);
            StringAssert.Contains("| Всего, байт на кадр, среднее (все кадры) — приёмка | 0 (?) | 1024 | не проверено |", text);
            StringAssert.Contains("| zone93 | 600 | 60 | 5 | 0 | 0 (?) / 0 (?) / 0 (?) | 0 (?) / 0 (?) / 0 (?) | не проверено / не проверено |", text);
            StringAssert.DoesNotContain("| да |", text);
        }

        /// <summary>Причины пропуска тестов набора из XML: «: причина ×N; …».</summary>
        static string SkipNote(Suite s)
        {
            var groups = s.Cases.Where(t => t.Result == "Skipped").GroupBy(t => t.Reason.Length > 0 ? t.Reason : "без причины").ToList();
            if (groups.Count == 0) return "";
            return ": " + string.Join("; ", groups.Select(g => "«" + g.Key + "»" + (g.Count() > 1 ? " ×" + g.Count() : "")));
        }

        /// <summary>Понизить заголовки вложенного отчёта на один уровень.</summary>
        static string Demote(string md)
        {
            var lines = md.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("#", StringComparison.Ordinal)) lines[i] = "#" + lines[i];
            return string.Join("\n", lines);
        }

        static string BotSummary(string bot)
        {
            var parts = new List<string>();
            foreach (var line in bot.Split('\n'))
            {
                if (!line.StartsWith("| zone", StringComparison.Ordinal)) continue;
                var cells = line.Split('|');
                if (cells.Length < 3) continue;
                string outcome = cells[2].Trim();
                if (!(outcome.StartsWith("пройдена", StringComparison.Ordinal) || outcome.StartsWith("стена", StringComparison.Ordinal) || outcome.StartsWith("предел", StringComparison.Ordinal))) continue;
                parts.Add(cells[1].Trim() + " " + outcome);
            }
            return parts.Count == 0 ? "нет данных." : string.Join("; ", parts) + ".";
        }
    }
}
